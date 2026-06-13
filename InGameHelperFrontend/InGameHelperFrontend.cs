using FrameWork.ModSystem;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine;

namespace InGameHelper
{
	/// <summary>
	/// InGameHelper 前端插件 — 场景信息查询 + 请求路由
	/// FileSystemWatcher 监听 quest.json → 场景请求自己处理 → 数据请求转发给后端(写 _backend_quest.json)
	/// </summary>
	[PluginConfig(pluginName: "InGameHelper", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
	public class InGameHelperFrontendPlugin : TaiwuRemakePlugin
	{
		private Harmony _harmony;

		// 配置路径
		private string _workDir;
		private string _questPath;
		private string _resultPath;
		private string _resultIndexPath;
		private string _backendQuestPath;
		private string _backendResultPath;

		// FileSystemWatcher
		private FileSystemWatcher _questWatcher;
		private FileSystemWatcher _backendResultWatcher;

		// 线程安全: 后台线程→主线程的场景查询传递
		private string _pendingSceneJson;
		private readonly object _pendingLock = new object();

		// 后端结果等待
		private DateTime _lastBackendResultTime = DateTime.MinValue;
		private string _pendingDataRequestId; // 正在等待的数据请求ID

		// 链式调用错误信息（供 GetSingletonData 返回给客户端）
		private static string _lastInvokeError;

		public override void Initialize()
		{
			MyUtils.modName = nameof(InGameHelper);
			MyUtils.MyLog("Initialize");
			_harmony = Harmony.CreateAndPatchAll(typeof(InGameHelperFrontendPlugin));
			TryLoadConfig();
			StartWatchers();
			StartSceneProcessingCoroutine();
		}

		public override void Dispose()
		{
			MyUtils.MyLog("Dispose");
			_harmony?.UnpatchSelf();
			_questWatcher?.Dispose();
			_backendResultWatcher?.Dispose();
		}

		public override void OnModSettingUpdate() { }

		// ===================== 配置加载 =====================

		public void TryLoadConfig()
		{
			try
			{
				var modInfo = ModManager.GetModInfo(ModIdStr);
				var configPath = Path.Combine(modInfo.DirectoryName, "config.xml");
				if (!File.Exists(configPath))
				{
					MyUtils.MyLog($"配置文件不存在: {configPath}");
					return;
				}

				XDocument doc = XDocument.Load(configPath);
				var workDirRaw = doc.Descendants("workDir").FirstOrDefault()?.Value ?? "";
				if (string.IsNullOrEmpty(workDirRaw))
				{
					MyUtils.MyLog("配置错误: workDir 为空");
					return;
				}

				// 相对路径 → 相对于 mod 目录
				_workDir = Path.IsPathRooted(workDirRaw)
					? workDirRaw
					: Path.Combine(modInfo.DirectoryName, workDirRaw);
				Directory.CreateDirectory(_workDir);

				var questFile = doc.Descendants("quest").FirstOrDefault()?.Value ?? "quest.json";
				_questPath = Path.Combine(_workDir, questFile);

				var resultFile = doc.Descendants("result").FirstOrDefault()?.Value ?? "result.json";
				_resultPath = Path.Combine(_workDir, resultFile);

				var resultIndexFile = doc.Descendants("resultIndex").FirstOrDefault()?.Value ?? "result_index.json";
				_resultIndexPath = Path.Combine(_workDir, resultIndexFile);

				var backendQuestFile = doc.Descendants("backendQuest").FirstOrDefault()?.Value ?? "_backend_quest.json";
				_backendQuestPath = Path.Combine(_workDir, backendQuestFile);

				var backendResultFile = doc.Descendants("backendResult").FirstOrDefault()?.Value ?? "_backend_result.json";
				_backendResultPath = Path.Combine(_workDir, backendResultFile);

				MyUtils.MyLog($"配置: workDir={_workDir}, quest={_questPath}, result={_resultPath}");
				MyUtils.MyLog($"配置: backendQuest={_backendQuestPath}, backendResult={_backendResultPath}");
			}
			catch (Exception ex)
			{
				MyUtils.MyLog($"加载配置失败: {ex.Message}");
			}
		}

		// ===================== FileSystemWatcher 监听 =====================

		private void StartWatchers()
		{
			if (string.IsNullOrEmpty(_questPath) || string.IsNullOrEmpty(_resultPath))
			{
				MyUtils.MyLog("路径未配置，无法启动监听");
				return;
			}

			// 监听外部请求文件
			_questWatcher = new FileSystemWatcher(Path.GetDirectoryName(_questPath), Path.GetFileName(_questPath))
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
				EnableRaisingEvents = true
			};
			_questWatcher.Changed += OnQuestFileChanged;
			_questWatcher.Created += OnQuestFileChanged;

			// 监听后端结果文件
			_backendResultWatcher = new FileSystemWatcher(Path.GetDirectoryName(_backendResultPath), Path.GetFileName(_backendResultPath))
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
				EnableRaisingEvents = true
			};
			_backendResultWatcher.Changed += OnBackendResultChanged;
			_backendResultWatcher.Created += OnBackendResultChanged;

			MyUtils.MyLog($"监听已启动: quest={_questPath}, backendResult={_backendResultPath}");
		}

		// ---- 请求文件变化（后台线程） ----

		private void OnQuestFileChanged(object sender, FileSystemEventArgs e)
		{
			try
			{
				// 等一小段时间确保写入完成
				Thread.Sleep(100);
				if (!File.Exists(_questPath)) return;

				string json;
				try { json = SafeReadAllText(_questPath); }
				catch { return; }
				if (string.IsNullOrEmpty(json)) return;

				GameRequest request;
				try { request = JsonConvert.DeserializeObject<GameRequest>(json); }
				catch { return; }
				if (request == null || string.IsNullOrEmpty(request.RequestId)) return;

				MyUtils.MyLog($"收到请求: id={request.RequestId}, type={request.Type}");

				switch (request.Type)
				{
					case "scene_hierarchy":
					case "component_info":
					case "component_field":
						// 场景请求 → 交给主线程协程处理
						lock (_pendingLock) { _pendingSceneJson = json; }
						break;

					case "frontend_data":
						// 前端可直接获取的数据 → 主线程协程处理
						lock (_pendingLock) { _pendingSceneJson = json; }
						break;

					default:
						// 数据请求 → 转发给后端（文件通讯）
						ForwardToBackend(json);
						break;
				}
			}
			catch (Exception ex)
			{
				MyUtils.MyLog($"处理请求异常: {ex.Message}");
			}
		}

		// ---- 后端结果变化（后台线程） ----

		private void OnBackendResultChanged(object sender, FileSystemEventArgs e)
		{
			try
			{
				Thread.Sleep(100);
				if (!File.Exists(_backendResultPath)) return;

				var json = SafeReadAllText(_backendResultPath);
				if (string.IsNullOrEmpty(json)) return;

				// 检查后端结果大小，大结果拆分到独立文件
				if (json.Length > 1000)
				{
					// 解析获取 requestId
					string requestId = _pendingDataRequestId ?? "unknown";
					try
					{
						var parsed = JObject.Parse(json);
						requestId = parsed["requestId"]?.ToString() ?? requestId;
					}
					catch { }

					var longResultFile = $"result_{requestId}.json";
					SafeWriteFile(Path.Combine(_workDir, longResultFile), json);

					var brief = new JObject
					{
						["requestId"] = requestId,
						["success"] = true,
						["hasLongResult"] = true,
						["longResultFile"] = longResultFile
					};
					SafeWriteFile(_resultPath, brief.ToString(Formatting.Indented));

					AppendResultIndex(requestId, "backend_data", longResultFile, json.Length);

					MyUtils.MyLog($"后端结果已拆分写入: {longResultFile}");
				}
				else
				{
					// 小结果直接转发
					SafeWriteFile(_resultPath, json);
					MyUtils.MyLog($"后端结果已转发: {_pendingDataRequestId}");
				}

				_pendingDataRequestId = null;
			}
			catch (Exception ex)
			{
				MyUtils.MyLog($"处理后端结果异常: {ex.Message}");
			}
		}

		// ---- 转发到后端 ----

		private void ForwardToBackend(string requestJson)
		{
			try
			{
				var request = JsonConvert.DeserializeObject<GameRequest>(requestJson);
				_pendingDataRequestId = request?.RequestId;
				SafeWriteFile(_backendQuestPath, requestJson);
				MyUtils.MyLog($"已转发到后端: {_pendingDataRequestId}");
			}
			catch (Exception ex)
			{
				MyUtils.MyLog($"转发到后端失败: {ex.Message}");
			}
		}

		// ===================== 主线程场景请求处理（协程） =====================

		private void StartSceneProcessingCoroutine()
		{
			GameApp.Instance.StartCoroutine(SceneProcessRoutine());
		}

		private IEnumerator SceneProcessRoutine()
		{
			var wait = new WaitForSecondsRealtime(0.2f);
			while (true)
			{
				yield return wait;

				string json = null;
				lock (_pendingLock)
				{
					if (_pendingSceneJson != null)
					{
						json = _pendingSceneJson;
						_pendingSceneJson = null;
					}
				}
				if (json == null) continue;

				try
				{
					ProcessSceneRequest(json);
				}
				catch (Exception ex)
				{
					MyUtils.MyLog($"场景请求处理异常: {ex.Message}");
				}
			}
		}

		private void ProcessSceneRequest(string json)
		{
			var request = JsonConvert.DeserializeObject<GameRequest>(json);
			if (request == null) return;

			MyUtils.MyLog($"处理场景请求: id={request.RequestId}, type={request.Type}");
			var sw = Stopwatch.StartNew();

			JToken resultData = null;
			switch (request.Type)
			{
				case "scene_hierarchy":
					resultData = SceneQueryService.QueryHierarchy(request.Params);
					break;
				case "component_info":
					resultData = SceneQueryService.QueryComponentInfo(request.Params);
					break;
				case "component_field":
					resultData = SceneQueryService.QueryComponentField(request.Params);
					break;
				case "frontend_data":
					resultData = QueryFrontendData(request.Params);
					break;
			}

			if (resultData == null)
			{
				WriteResponse(new GameResponse { RequestId = request.RequestId, Success = false, Error = "查询结果为空" });
				return;
			}

			var hasLongResult = false;
			string longResultFile = null;
			var resultStr = resultData.ToString(Formatting.None);
			if (resultStr.Length > 1000)
			{
				hasLongResult = true;
				longResultFile = $"result_{request.RequestId}.json";
				SafeWriteFile(Path.Combine(_workDir, longResultFile), resultData.ToString(Formatting.Indented));
				AppendResultIndex(request.RequestId, request.Type, longResultFile, resultStr.Length);
			}

			sw.Stop();
			MyUtils.MyLog($"场景查询完成: {request.RequestId}, {sw.ElapsedMilliseconds}ms, {resultStr.Length}B");
			WriteResponse(new GameResponse
			{
				RequestId = request.RequestId,
				Success = true,
				Data = resultData,
				HasLongResult = hasLongResult,
				LongResultFile = longResultFile
			});
		}

		// ===================== 响应写入 =====================

		private void WriteResponse(GameResponse response)
		{
			try
			{
				if (response.HasLongResult)
				{
					// 长结果 → result.json 只写简要引用，完整数据在独立文件中
					var brief = new JObject
					{
						["requestId"] = response.RequestId,
						["success"] = response.Success,
						["hasLongResult"] = true,
						["longResultFile"] = response.LongResultFile
					};
					if (!string.IsNullOrEmpty(response.Error))
						brief["error"] = response.Error;
					SafeWriteFile(_resultPath, brief.ToString(Formatting.Indented));
				}
				else
				{
					var json = JsonConvert.SerializeObject(response, Formatting.Indented);
					SafeWriteFile(_resultPath, json);
				}
			}
			catch (Exception ex) { MyUtils.MyLog($"写入响应失败: {ex.Message}"); }
		}

		private void AppendResultIndex(string requestId, string type, string fileName, int size)
		{
			try
			{
				var entry = new JObject { ["requestId"] = requestId, ["type"] = type, ["fileName"] = fileName, ["size"] = size, ["timestamp"] = DateTime.UtcNow.ToString("O") };
				JArray index;
				if (File.Exists(_resultIndexPath))
					index = JArray.Parse(SafeReadAllText(_resultIndexPath));
				else
					index = new JArray();
				index.Add(entry);
				while (index.Count > 100) index.RemoveAt(0);
				SafeWriteFile(_resultIndexPath, index.ToString(Formatting.Indented));
			}
			catch (Exception ex) { MyUtils.MyLog($"写入结果索引失败: {ex.Message}"); }
		}

		// ===================== 前端数据查询 =====================

		/// <summary>
		/// 查询前端可直接访问的游戏数据（不走后端）。
		/// 通用实现：data 参数为类名，通过 SingletonObject.getInstance&lt;T&gt;() 获取实例。
		/// 支持任意注册为 Singleton 的类型，如 BasicGameData、CharacterMonitorModel 等。
		/// </summary>
		private static JToken QueryFrontendData(Dictionary<string, object> rawParams)
		{
			var dataType = rawParams.GetValueOrDefault<string>("data") ?? "";
			if (string.IsNullOrEmpty(dataType))
				return new JObject { ["error"] = "缺少 data 参数" };

			return GetSingletonData(dataType, rawParams);
		}

		/// <summary>通用方法：通过类名获取 SingletonObject 实例并读取其数据</summary>
		private static JToken GetSingletonData(string typeName, Dictionary<string, object> rawParams)
		{
			Type targetType = null;

			// 所有程序集中搜索指定类名
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				targetType = asm.GetType(typeName, false);
				if (targetType != null) break;
			}

			if (targetType == null)
				return new JObject { ["error"] = $"找不到类型: {typeName}，请使用完整类名" };

			try
			{
				var getInstanceMethod = typeof(SingletonObject).GetMethod("getInstance",
					BindingFlags.Public | BindingFlags.Static);
				if (getInstanceMethod == null)
					return new JObject { ["error"] = "未找到 SingletonObject.getInstance" };

				var genericMethod = getInstanceMethod.MakeGenericMethod(targetType);
				var instance = genericMethod.Invoke(null, null);
				if (instance == null)
					return new JObject { ["error"] = $"{typeName} 实例为空（可能未初始化）" };

				// ====== 链式调用对象方法（多级） ======
				_lastInvokeError = null;
				instance = ApplyFrontendMethodChain(instance, rawParams);

				if (instance == null)
				{
					var errMsg = _lastInvokeError ?? "未知错误";
					return new JObject { ["error"] = $"对象方法链调用失败: {errMsg}" };
				}

				var t = HarmonyLib.Traverse.Create(instance);

				// 如果指定了 field，只返回该字段
				var fieldName = rawParams.GetValueOrDefault<string>("field");
				if (!string.IsNullOrEmpty(fieldName))
				{
					var val = t.Field(fieldName).GetValue();
					if (val == null)
						val = t.Property(fieldName).GetValue();
					if (val == null)
						return new JObject { ["error"] = $"在 {typeName} 上未找到字段: {fieldName}" };

					return new JObject
					{
						["_type"] = typeName,
						["field"] = fieldName,
						["value"] = JTokenConverter.ConvertToJToken(val, 3)
					};
				}

				// 未指定 field → 返回所有公开字段
				var data = new JObject { ["_type"] = typeName };
				var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Instance);
				foreach (var f in fields)
				{
					try
					{
						var val = t.Field(f.Name).GetValue();
						if (val != null)
							data[f.Name] = JTokenConverter.ConvertToJToken(val, 2);
					}
					catch { }
				}

				return data;
			}
			catch (Exception ex)
			{
				return new JObject { ["error"] = $"获取 {typeName} 失败: {ex.Message}" };
			}
		}

		/// <summary>前端版链式方法调用（与后端 ApplyObjectMethodChain 类似，支持泛型方法、枚举转换）</summary>
		private static object ApplyFrontendMethodChain(object obj, Dictionary<string, object> rawParams)
		{
			if (obj == null) return obj;

			// 解析 objectMethods
			if (!(rawParams.TryGetValue("objectMethods", out var methodsObj) && methodsObj is JArray methodsArr))
				return obj;
			if (methodsArr.Count == 0) return obj;

			var methods = methodsArr.Select(m => m.ToString()).ToList();

			// 解析 objectArgsList（数组的数组，可选，与 methods 对应）
			var argsList = new List<object[]>();
			if (rawParams.TryGetValue("objectArgsList", out var argsListObj) && argsListObj is JArray argsArr)
			{
				foreach (var item in argsArr)
				{
					if (item is JArray ja) argsList.Add(NormalizeJArray(ja));
					else argsList.Add(null);
				}
			}

			var current = obj;
			for (int i = 0; i < methods.Count; i++)
			{
				var args = (i < argsList.Count) ? argsList[i] : null;
				current = InvokeFrontendMethodDirect(current, methods[i], args);
				if (current == null) return null;
			}
			return current;
		}

		/// <summary>对对象调用方法（支持泛型、枚举转换、类型转换）</summary>
		private static object InvokeFrontendMethodDirect(object obj, string methodName, object[] args)
		{
			if (obj == null) return null;
			MyUtils.MyLog($"[FEMethod] {methodName}({FormatArgs(args)}) on {obj.GetType().Name}");

			// 1️⃣ 先 Traverse 尝试（最简单的情况）
			var t = HarmonyLib.Traverse.Create(obj);
			if (args != null && args.Length > 0)
			{
				try
				{
					var val = t.Method(methodName, args).GetValue();
					if (val != null) return val;
				}
				catch (Exception ex)
				{
					_lastInvokeError = $"Traverse({methodName}) 调用异常: {ex.Message}";
				}
			}
			else
			{
				try
				{
					var val = t.Method(methodName).GetValue();
					if (val != null) return val;
				}
				catch (Exception ex)
				{
					_lastInvokeError = $"Traverse({methodName}) 调用异常: {ex.Message}";
				}
			}

			// 2️⃣ 反射调用（支持泛型方法 + 枚举转换 + 类型转换）
			try
			{
				var result = InvokeFrontendMethodReflection(obj, methodName, args);
				if (result != null) return result;
			}
			catch (Exception ex)
			{
				var inner = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
				_lastInvokeError = $"反射({methodName}) 调用异常: {inner}";
				MyUtils.MyLog($"[FEMethod] 反射调用失败: {ex.Message} | Inner: {inner}");
			}

			if (_lastInvokeError == null)
				_lastInvokeError = $"{methodName} 全部尝试失败，objType={obj.GetType().Name}";
			MyUtils.MyLog($"[FEMethod] {methodName} 全部尝试失败");
			return null;
		}

		/// <summary>反射调用方法：支持泛型方法名 GetComponent&lt;T&gt;、int→enum 转换、数字类型兼容</summary>
		private static object InvokeFrontendMethodReflection(object obj, string methodName, object[] args)
		{
			// 解析泛型方法名: "GetComponent<RectTransform>" → name="GetComponent", typeArg="RectTransform"
			string genericTypeName = null;
			var genericMatch = System.Text.RegularExpressions.Regex.Match(methodName, @"^(\w+)<(.+)>$");
			if (genericMatch.Success)
			{
				methodName = genericMatch.Groups[1].Value;
				genericTypeName = genericMatch.Groups[2].Value;
			}

			var type = obj.GetType();
			var argCount = args?.Length ?? 0;

			// 查找名称 + 参数数量匹配的方法
			var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Where(m => m.Name == methodName && m.GetParameters().Length == argCount)
				.ToList();

			if (methods.Count == 0)
			{
				_lastInvokeError = $"找不到匹配方法 {methodName}({argCount} params) 在 {type.Name}";
				MyUtils.MyLog($"[FERefl] 未找到 {methodName}({argCount} params) 在 {type.Name}");
				return null;
			}

			foreach (var method in methods)
			{
				try
				{
					var parameters = method.GetParameters();
					MethodInfo methodToInvoke = method;

					// 泛型方法处理
					if (genericTypeName != null)
					{
						if (!method.IsGenericMethod) continue;
						var genericType = FindTypeInAssemblies(genericTypeName);
						if (genericType == null)
						{
							MyUtils.MyLog($"[FERefl] 找不到泛型类型: {genericTypeName}");
							continue;
						}
						methodToInvoke = method.MakeGenericMethod(genericType);
					}
					else if (method.IsGenericMethodDefinition)
					{
						continue; // 需要泛型参数但未提供
					}

					// 参数转换（枚举 + 数字类型兼容）
					var convertedArgs = new object[argCount];
					for (int i = 0; i < argCount; i++)
					{
						var paramType = parameters[i].ParameterType;
						var arg = args[i];
						convertedArgs[i] = ConvertArgToParamType(arg, paramType);
					}

					var result = methodToInvoke.Invoke(obj, convertedArgs);
					MyUtils.MyLog($"[FERefl] {methodName} 成功 → {result?.GetType()?.Name ?? "null"}");
					return result;
				}
				catch (Exception ex)
				{
					var inner = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
					_lastInvokeError = $"{method.Name} 调用异常: {inner}";
					MyUtils.MyLog($"[FERefl] 方法 {method.Name} 失败: {ex.Message} | Inner: {inner}");
					continue;
				}
			}
			return null;
		}

		/// <summary>将参数值转换为目标参数类型（支持 int↔enum、数字类型提升）</summary>
		private static object ConvertArgToParamType(object arg, Type targetType)
		{
			if (arg == null) return null;

			// 类型直接匹配
			if (targetType.IsAssignableFrom(arg.GetType())) return arg;

			// int→enum
			if (targetType.IsEnum)
			{
				if (arg is int i) return Enum.ToObject(targetType, i);
				if (arg is long l) return Enum.ToObject(targetType, (int)l);
				if (arg is string s && Enum.IsDefined(targetType, s)) return Enum.Parse(targetType, s);
			}

			// 数字类型提升 / 降级
			if (targetType == typeof(long) && arg is int iv) return (long)iv;
			if (targetType == typeof(int) && arg is long lv) return (int)lv;
			if (targetType == typeof(float) && arg is double d) return (float)d;
			if (targetType == typeof(double) && arg is float f) return (double)f;
			if (targetType == typeof(long) && arg is float fv) return (long)fv;
			if (targetType == typeof(int) && arg is float fv2) return (int)fv2;
			if (targetType == typeof(float) && arg is long lv2) return (float)lv2;
			if (targetType == typeof(float) && arg is int iv2) return (float)iv2;

			return arg; // 原样返回
		}

		/// <summary>在所有已加载程序集中查找类型</summary>
		private static Type FindTypeInAssemblies(string typeName)
		{
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				var t = asm.GetType(typeName, false);
				if (t != null) return t;
			}
			return null;
		}

		/// <summary>标准化 JArray（long→int 转换）</summary>
		private static object[] NormalizeJArray(JArray ja)
		{
			var result = new List<object>();
			foreach (var j in ja)
			{
				if (j is JValue jv)
				{
					var val = jv.Value;
					// JSON 数字默认 long → int
					if (val is long l) result.Add((int)l);
					else result.Add(val);
				}
				else
				{
					result.Add(j); // 复杂类型（JArray/JObject）原样保留
				}
			}
			return result.ToArray();
		}

		/// <summary>格式化参数列表用于日志</summary>
		private static string FormatArgs(object[] args)
		{
			if (args == null || args.Length == 0) return "";
			return string.Join(", ", args.Select(a => a?.ToString() ?? "null"));
		}

		// ===================== 文件工具 =====================

		private static string SafeReadAllText(string path)
		{
			for (int i = 0; i < 5; i++)
			{
				try
				{
					using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
					using var sr = new StreamReader(fs);
					return sr.ReadToEnd();
				}
				catch (IOException) { Thread.Sleep(50); }
			}
			return null;
		}

		private static void SafeWriteFile(string path, string content)
		{
			var tmpPath = path + ".tmp";
			File.WriteAllText(tmpPath, content);
			if (File.Exists(path)) File.Replace(tmpPath, path, null);
			else File.Move(tmpPath, path);
		}
	}

	// ===================================================================
	//  模型类
	// ===================================================================

	public class GameRequest
	{
		[JsonProperty("requestId")] public string RequestId { get; set; }
		[JsonProperty("type")] public string Type { get; set; }
		[JsonProperty("params")] public Dictionary<string, object> Params { get; set; } = new Dictionary<string, object>();
	}

	public class GameResponse
	{
		[JsonProperty("requestId")] public string RequestId { get; set; }
		[JsonProperty("success")] public bool Success { get; set; }
		[JsonProperty("data")] public JToken Data { get; set; }
		[JsonProperty("error")] public string Error { get; set; }
		[JsonProperty("hasLongResult")] public bool HasLongResult { get; set; }
		[JsonProperty("longResultFile")] public string LongResultFile { get; set; }
	}

	// ===================================================================
	//  场景查询服务 — 遍历 GameObject 层级、查询组件信息
	// ===================================================================

	public static class SceneQueryService
	{
		public static JToken QueryHierarchy(Dictionary<string, object> rawParams)
		{
			var rootType = rawParams.GetValueOrDefault<string>("rootType") ?? "all_canvases";
			if (rootType == "path")
			{
				var path = rawParams.GetValueOrDefault<string>("path") ?? "";
				return QueryByPath(path);
			}
			return ListAllCanvases();
		}

		private static JToken ListAllCanvases()
		{
			var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
			var arr = new JArray();
			foreach (var canvas in canvases)
			{
				if (canvas == null) continue;
				arr.Add(BuildTransformNode(canvas.transform, 0, 5));
			}
			return arr;
		}

		private static JToken QueryByPath(string path)
		{
			if (string.IsNullOrEmpty(path)) return new JObject { ["error"] = "path 不能为空" };
			var parts = path.Split('/');
			if (parts.Length == 0) return new JObject { ["error"] = "路径格式无效" };

			var rootObjs = UnityEngine.Object.FindObjectsOfType<GameObject>(true)
				.Where(go => go.transform.parent == null).ToList();
			Transform cur = null;
			foreach (var r in rootObjs) { if (r.name == parts[0]) { cur = r.transform; break; } }
			if (cur == null) return new JObject { ["error"] = $"未找到根对象: {parts[0]}" };

			for (int i = 1; i < parts.Length; i++)
			{
				cur = cur.Find(parts[i]);
				if (cur == null) return new JObject { ["error"] = $"未找到: {parts[i]}" };
			}
			return BuildTransformNode(cur, 0, 5);
		}

		private static JObject BuildTransformNode(Transform t, int depth, int maxDepth)
		{
			var obj = new JObject();
			obj["name"] = t.gameObject.name;
			obj["active"] = t.gameObject.activeInHierarchy;
			obj["path"] = GetFullPath(t);
			obj["layer"] = t.gameObject.layer;
			obj["tag"] = t.gameObject.tag;

			var compArr = new JArray();
			foreach (var c in t.GetComponents<Component>())
			{
				if (c != null) compArr.Add(c.GetType().Name);
			}
			obj["components"] = compArr;
			obj["localPosition"] = new JObject { ["x"] = t.localPosition.x, ["y"] = t.localPosition.y, ["z"] = t.localPosition.z };
			obj["childCount"] = t.childCount;

			if (depth < maxDepth)
			{
				var children = new JArray();
				for (int i = 0; i < t.childCount; i++)
					children.Add(BuildTransformNode(t.GetChild(i), depth + 1, maxDepth));
				obj["children"] = children;
			}
			else if (t.childCount > 0)
			{
				obj["childrenTruncated"] = true;
			}
			return obj;
		}

		public static JToken QueryComponentInfo(Dictionary<string, object> rawParams)
		{
			var path = rawParams.GetValueOrDefault<string>("gameObjectPath") ?? "";
			var compTypeName = rawParams.GetValueOrDefault<string>("componentType");
			if (string.IsNullOrEmpty(path)) return new JObject { ["error"] = "gameObjectPath 不能为空" };

			var go = FindGameObjectByPath(path);
			if (go == null) return new JObject { ["error"] = $"未找到: {path}" };

			var result = new JArray();
			foreach (var comp in go.GetComponents<Component>())
			{
				if (comp == null) continue;
				var name = comp.GetType().Name;
				if (!string.IsNullOrEmpty(compTypeName) &&
					!string.Equals(name, compTypeName, StringComparison.OrdinalIgnoreCase) &&
					!comp.GetType().FullName.Contains(compTypeName))
					continue;

				var compObj = new JObject { ["type"] = comp.GetType().FullName, ["typeName"] = name };
				var fields = new JObject();
				var t = HarmonyLib.Traverse.Create(comp);
				ReadCommonComponentFields(comp, t, fields);
				compObj["fields"] = fields;
				result.Add(compObj);
			}
			return result;
		}

		public static JToken QueryComponentField(Dictionary<string, object> rawParams)
		{
			var path = rawParams.GetValueOrDefault<string>("gameObjectPath") ?? "";
			var compTypeName = rawParams.GetValueOrDefault<string>("componentType") ?? "";
			var fieldName = rawParams.GetValueOrDefault<string>("field") ?? "";
			if (string.IsNullOrEmpty(path)) return new JObject { ["error"] = "gameObjectPath 不能为空" };
			if (string.IsNullOrEmpty(compTypeName)) return new JObject { ["error"] = "componentType 不能为空" };
			if (string.IsNullOrEmpty(fieldName)) return new JObject { ["error"] = "field 不能为空" };

			var go = FindGameObjectByPath(path);
			if (go == null) return new JObject { ["error"] = $"未找到: {path}" };

			foreach (var comp in go.GetComponents<Component>())
			{
				if (comp == null) continue;
				var name = comp.GetType().Name;
				if (!string.Equals(name, compTypeName, StringComparison.OrdinalIgnoreCase) &&
					!comp.GetType().FullName.Contains(compTypeName))
					continue;

				var t = HarmonyLib.Traverse.Create(comp);
				var prop = t.Property(fieldName);
				if (prop != null)
				{
					var val = prop.GetValue();
					if (val != null)
						return new JObject { ["type"] = comp.GetType().FullName, ["componentType"] = name, ["field"] = fieldName, ["value"] = JTokenConverter.ConvertToJToken(val, 1) };
				}
				var f = t.Field(fieldName);
				if (f != null)
				{
					var val = f.GetValue();
					return new JObject { ["type"] = comp.GetType().FullName, ["componentType"] = name, ["field"] = fieldName, ["value"] = val != null ? JTokenConverter.ConvertToJToken(val, 1) : JValue.CreateNull() };
				}
				return new JObject { ["error"] = $"在 {name} 上未找到: {fieldName}" };
			}
			return new JObject { ["error"] = $"未找到组件: {compTypeName}" };
		}

		private static void ReadCommonComponentFields(Component comp, HarmonyLib.Traverse t, JObject fields)
		{
			if (comp is Transform tf)
			{
				fields["localPosition"] = Vec3ToJToken(tf.localPosition);
				fields["localRotation"] = Vec3ToJToken(tf.localRotation.eulerAngles);
				fields["localScale"] = Vec3ToJToken(tf.localScale);
				fields["childCount"] = tf.childCount;
			}
			if (comp is RectTransform rt)
			{
				fields["rect"] = new JObject { ["x"] = rt.rect.x, ["y"] = rt.rect.y, ["width"] = rt.rect.width, ["height"] = rt.rect.height };
				fields["anchoredPosition"] = Vec2ToJToken(rt.anchoredPosition);
				fields["sizeDelta"] = Vec2ToJToken(rt.sizeDelta);
				fields["pivot"] = Vec2ToJToken(rt.pivot);
			}

			var type = comp.GetType();
			foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => ConvertUtils.IsSimpleType(f.FieldType)).Take(20))
			{
				try { var val = t.Field(f.Name).GetValue(); if (val != null) fields[f.Name] = JTokenConverter.ConvertToJToken(val, 1); } catch { }
			}
			foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && ConvertUtils.IsSimpleType(p.PropertyType)).Take(20))
			{
				try { var val = t.Property(p.Name).GetValue(); if (val != null) fields[p.Name] = JTokenConverter.ConvertToJToken(val, 1); } catch { }
			}
		}

		private static GameObject FindGameObjectByPath(string path)
		{
			var parts = path.Split('/');
			if (parts.Length == 0) return null;
			var roots = UnityEngine.Object.FindObjectsOfType<GameObject>(true).Where(go => go.transform.parent == null).ToList();
			Transform cur = null;
			foreach (var r in roots) { if (r.name == parts[0]) { cur = r.transform; break; } }
			if (cur == null) return null;
			for (int i = 1; i < parts.Length; i++) { cur = cur.Find(parts[i]); if (cur == null) return null; }
			return cur.gameObject;
		}

		private static string GetFullPath(Transform t) => t.parent == null ? t.name : GetFullPath(t.parent) + "/" + t.name;
		private static JToken Vec3ToJToken(Vector3 v) => new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
		private static JToken Vec2ToJToken(Vector2 v) => new JObject { ["x"] = v.x, ["y"] = v.y };
	}

	// ===================================================================
	//  通用工具类
	// ===================================================================

	internal static class ConvertUtils
	{
		internal static T GetValueOrDefault<T>(this Dictionary<string, object> dict, string key)
		{
			if (dict == null) return default;
			if (dict.TryGetValue(key, out var val) && val is T tVal) return tVal;
			return default;
		}

		internal static bool IsSimpleType(Type type)
		{
			if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
				type == typeof(DateTime) || type == typeof(bool) || type.IsEnum ||
				type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) ||
				type == typeof(Color) || type == typeof(Quaternion) || type == typeof(Rect))
				return true;
			if (Nullable.GetUnderlyingType(type) != null) return true;
			return false;
		}
	}

	internal static class JTokenConverter
	{
		internal static JToken ConvertToJToken(object obj, int maxDepth)
		{
			if (obj == null || maxDepth <= 0) return JValue.CreateNull();
			var type = obj.GetType();

			if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return new JValue(obj);
			if (type.IsEnum) return new JValue(obj.ToString());
			if (type == typeof(bool)) return new JValue((bool)obj);
			if (type == typeof(DateTime)) return new JValue(((DateTime)obj).ToString("O"));
			if (type == typeof(Vector2)) { var v = (Vector2)obj; return new JObject { ["x"] = v.x, ["y"] = v.y }; }
			if (type == typeof(Vector3)) { var v = (Vector3)obj; return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z }; }
			if (type == typeof(Vector4)) { var v = (Vector4)obj; return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z, ["w"] = v.w }; }
			if (type == typeof(Color)) { var c = (Color)obj; return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a }; }
			if (type == typeof(Quaternion)) { var q = (Quaternion)obj; return new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w }; }
			if (type == typeof(Rect)) { var r = (Rect)obj; return new JObject { ["x"] = r.x, ["y"] = r.y, ["width"] = r.width, ["height"] = r.height }; }
			if (Nullable.GetUnderlyingType(type) != null)
				return ConvertToJToken(type.GetProperty("Value")?.GetValue(obj), maxDepth - 1);

			if (obj is IList list) { var arr = new JArray(); foreach (var item in list) arr.Add(ConvertToJToken(item, maxDepth - 1)); return arr; }
			if (obj is System.Collections.IDictionary dict) { var jo = new JObject(); foreach (var k in dict.Keys) jo[k?.ToString() ?? ""] = ConvertToJToken(dict[k], maxDepth - 1); return jo; }

			if (maxDepth <= 1) return new JObject { ["_type"] = type.FullName, ["_toString"] = obj.ToString() };

			var t = HarmonyLib.Traverse.Create(obj);
			var result = new JObject { ["_type"] = type.FullName };
			try { var idVal = t.Method("GetId").GetValue(); if (idVal != null) result["_id"] = ConvertToJToken(idVal, 1); } catch { }

			foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Take(50))
			{
				try
				{
					var name = f.Name.TrimStart('_');
					if (name.Length > 0 && char.IsUpper(name[0])) name = char.ToLower(name[0]) + name.Substring(1);
					var val = t.Field(f.Name).GetValue();
					if (val != null)
					{
						var converted = ConvertToJToken(val, maxDepth - 1);
						if (converted != null && !(converted is JValue jv && jv.Type == JTokenType.Null))
							result[name] = converted;
					}
				}
				catch { }
			}
			return result;
		}
	}
}
