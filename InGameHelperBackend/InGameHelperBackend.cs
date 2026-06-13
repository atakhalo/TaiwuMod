using GameData.Domains;
using GameData.Domains.Character;
using GameData.Domains.Mod;
using GameData.Domains.Taiwu;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TaiwuModdingLib.Core.Plugin;

namespace InGameHelper
{
	/// <summary>
	/// InGameHelper 后端插件 — 游戏数据查询
	/// FileSystemWatcher 监听 _backend_quest.json（前端写入的请求），处理后写 _backend_result.json。
	/// </summary>
	[PluginConfig(pluginName: "InGameHelperBackend", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
	public class InGameHelperBackendPlugin : TaiwuRemakePlugin
	{
		private Harmony? _harmony;
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private string _workDir;
		private string _backendQuestPath;
		private string _backendResultPath;
		private FileSystemWatcher _questWatcher;

		public override void Initialize()
		{
			Logger.Info("[InGameHelperBackend] Initialize");
			_harmony = Harmony.CreateAndPatchAll(typeof(InGameHelperBackendPlugin));
			TryLoadConfig();
			StartWatcher();
		}

		public override void Dispose()
		{
			Logger.Info("[InGameHelperBackend] Dispose");
			_harmony?.UnpatchSelf();
			_questWatcher?.Dispose();
		}

		public override void OnModSettingUpdate() { }

		// ===================== 配置加载 =====================

		public void TryLoadConfig()
		{
			try
			{
				var modDir = DomainManager.Mod.GetModDirectory(ModIdStr);
				if (string.IsNullOrEmpty(modDir))
				{
					Logger.Warn($"无法获取 mod 目录: {ModIdStr}");
					return;
				}
				var configPath = Path.Combine(modDir, "config.xml");
				if (!File.Exists(configPath))
				{
					Logger.Warn($"配置文件不存在: {configPath}");
					return;
				}

				XDocument doc = XDocument.Load(configPath);
				var workDirRaw = doc.Descendants("workDir").FirstOrDefault()?.Value ?? "";
				if (string.IsNullOrEmpty(workDirRaw))
				{
					Logger.Warn("配置错误: workDir 为空");
					return;
				}

				// 相对路径 → 相对于 mod 目录
				_workDir = Path.IsPathRooted(workDirRaw)
					? workDirRaw
					: Path.Combine(modDir, workDirRaw);
				Directory.CreateDirectory(_workDir);

				var backendQuestFile = doc.Descendants("backendQuest").FirstOrDefault()?.Value ?? "_backend_quest.json";
				_backendQuestPath = Path.Combine(_workDir, backendQuestFile);

				var backendResultFile = doc.Descendants("backendResult").FirstOrDefault()?.Value ?? "_backend_result.json";
				_backendResultPath = Path.Combine(_workDir, backendResultFile);

				Logger.Info($"配置: modDir={modDir}, workDir={_workDir}, backendQuest={_backendQuestPath}, backendResult={_backendResultPath}");
			}
			catch (Exception ex)
			{
				Logger.Error($"加载配置失败: {ex.Message}");
			}
		}

		// ===================== FileSystemWatcher 监听 =====================

		private void StartWatcher()
		{
			if (string.IsNullOrEmpty(_backendQuestPath))
			{
				Logger.Warn("路径未配置，无法启动监听");
				return;
			}

			var dir = Path.GetDirectoryName(_backendQuestPath);
			var file = Path.GetFileName(_backendQuestPath);
			Logger.Info($"启动 FileSystemWatcher: dir={dir}, file={file}");
			_questWatcher = new FileSystemWatcher(dir, file)
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
				EnableRaisingEvents = true
			};
			_questWatcher.Changed += OnQuestChanged;
			_questWatcher.Created += OnQuestChanged;

			Logger.Info($"监听已启动: {_backendQuestPath}");
		}

		private void OnQuestChanged(object sender, FileSystemEventArgs e)
		{
			try
			{
				System.Threading.Thread.Sleep(200); // 等写入完成
				if (!File.Exists(_backendQuestPath)) return;

				string json;
				try { json = SafeReadAllText(_backendQuestPath); }
				catch { return; }
				if (string.IsNullOrEmpty(json)) return;

				Logger.Info($"收到后端请求");

				var resultJson = BackendBridge.ProcessRequest(json);
				if (resultJson != null)
				{
					SafeWriteFile(_backendResultPath, resultJson);
					Logger.Info($"后端结果已写入: {_backendResultPath}");
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"处理后端请求异常: {ex.Message}");
			}
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
				catch (IOException) { System.Threading.Thread.Sleep(100); }
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
	//  后端桥接 — 前端通过反射调用此类的静态方法
	// ===================================================================

	public static class BackendBridge
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		/// <summary>
		/// 处理游戏数据查询请求。
		/// 由前端通过反射调用，接收请求JSON，返回响应JSON。
		/// </summary>
		public static string ProcessRequest(string requestJson)
		{
			BackendGameRequest request;
			try { request = JsonConvert.DeserializeObject<BackendGameRequest>(requestJson); }
			catch (Exception ex)
			{
				Logger.Warn($"[BackendBridge] JSON解析失败: {ex.Message}");
				return JsonConvert.SerializeObject(new BackendGameResponse
				{
					RequestId = "unknown",
					Success = false,
					Error = $"JSON解析失败: {ex.Message}"
				});
			}

			if (request == null || string.IsNullOrEmpty(request.RequestId))
			{
				return JsonConvert.SerializeObject(new BackendGameResponse
				{
					RequestId = request?.RequestId ?? "unknown",
					Success = false,
					Error = "请求格式无效"
				});
			}

			Logger.Info($"[BackendBridge] 处理请求: id={request.RequestId}, type={request.Type}");

			try
			{
				JToken resultData = null;

				switch (request.Type)
				{
					case "back_code":
						resultData = ExecuteBackCode(request.RequestId, request.Params);
						break;
					case "taiwu_info":
						resultData = BackendDataService.QueryTaiwuInfo();
						break;
					case "character_info":
						resultData = BackendDataService.QueryCharacterInfo(request.Params);
						break;
					case "character_name":
						resultData = BackendDataService.QueryCharacterName(request.Params);
						break;
					case "world_info":
						resultData = BackendDataService.QueryWorldInfo();
						break;
					default:
						return JsonConvert.SerializeObject(new BackendGameResponse
						{
							RequestId = request.RequestId,
							Success = false,
							Error = $"不支持的类型: {request.Type}"
						});
				}

				return JsonConvert.SerializeObject(new BackendGameResponse
				{
					RequestId = request.RequestId,
					Success = true,
					Data = resultData
				});
			}
			catch (Exception ex)
			{
				Logger.Error($"[BackendBridge] 处理失败: {request.RequestId}, {ex.Message}");
				return JsonConvert.SerializeObject(new BackendGameResponse
				{
					RequestId = request.RequestId,
					Success = false,
					Error = ex.Message
				});
			}
		}

		/// <summary>执行 back_code 请求：entry → chain → attach → JToken</summary>
		private static JToken ExecuteBackCode(string requestId, Dictionary<string, object> rawParams)
		{
			if (rawParams == null)
				return new JObject { ["error"] = "params 不能为空" };

			// 解析 entry
			EntryInfo entry = null;
			if (rawParams.TryGetValue("entry", out var entryObj) && entryObj is JObject entryJObj)
				entry = entryJObj.ToObject<EntryInfo>();

			// 解析 chain
			List<BackendChainStep> chain = null;
			if (rawParams.TryGetValue("chain", out var chainObj) && chainObj is JArray chainJArr)
				chain = chainJArr.ToObject<List<BackendChainStep>>();

			// 解析 resultDepth（默认 3）
			int resultDepth = 3;
			if (rawParams.TryGetValue("resultDepth", out var depthVal))
			{
				if (depthVal is long dl) resultDepth = (int)dl;
				else if (depthVal is int di) resultDepth = di;
			}

			// 解析 attach
			JArray attachArr = null;
			if (rawParams.TryGetValue("attach", out var attachVal) && attachVal is JArray attachJArr)
				attachArr = attachJArr;

			// 解析起点
			object current;
			if (entry != null)
			{
				current = BackendChainExecutor.ResolveEntry(entry);
				if (current == null)
					return new JObject { ["error"] = $"找不到类型: {entry.Name}" };
			}
			else
			{
				return new JObject { ["error"] = "独立请求必须指定 entry" };
			}

			// 执行主链
			if (chain != null && chain.Count > 0)
			{
				current = BackendChainExecutor.ExecuteToObject(current, chain);
				if (current == null)
				{
					var detail = BackendChainExecutor.LastError ?? "(无详细信息)";
					return new JObject { ["error"] = $"链式调用执行失败: {detail}" };
				}
			}

			// 执行 attach 链（在原始对象上继续反射）
			if (attachArr != null)
			{
				foreach (var item in attachArr)
				{
					var attachParams = item["params"] as JObject;
					if (attachParams == null) continue;

					var attachChain = attachParams["chain"]?.ToObject<List<BackendChainStep>>();
					if (attachChain == null || attachChain.Count == 0) continue;

					var attachDepth = attachParams["resultDepth"]?.Value<int>() ?? resultDepth;
					resultDepth = attachDepth;

					current = BackendChainExecutor.ExecuteToObject(current, attachChain);
					if (current == null)
					{
						var detail = BackendChainExecutor.LastError ?? "(无详细信息)";
						return new JObject { ["error"] = $"附加链调用失败: {detail}" };
					}
				}
			}

			return BackendJTokenConverter.ConvertToJToken(current, resultDepth);
		}
	}

	// ===================================================================
	//  模型类（与前端保持兼容，字段名匹配）
	// ===================================================================

	public class BackendGameRequest
	{
		[JsonProperty("requestId")] public string RequestId { get; set; }
		[JsonProperty("type")] public string Type { get; set; }
		[JsonProperty("params")] public Dictionary<string, object> Params { get; set; } = new Dictionary<string, object>();
	}

	public class BackendGameResponse
	{
		[JsonProperty("requestId")] public string RequestId { get; set; }
		[JsonProperty("success")] public bool Success { get; set; }
		[JsonProperty("data")] public JToken Data { get; set; }
		[JsonProperty("error")] public string Error { get; set; }
		[JsonProperty("hasLongResult")] public bool HasLongResult { get; set; }
		[JsonProperty("longResultFile")] public string LongResultFile { get; set; }
	}

	// ===================================================================
	//  Chain 模型 — 反射链数据结构（前后端共用协议）
	// ===================================================================

	public class BackendChainStep
	{
		[JsonProperty("step")] public string Step { get; set; }      // "method" | "field" | "property"
		[JsonProperty("name")] public string Name { get; set; }      // 成员名
		[JsonProperty("stepType")] public string StepType { get; set; } // 结果类型全名
		[JsonProperty("argTypes")] public string[] ArgTypes { get; set; } // 方法参数类型（仅 method）
		[JsonProperty("args")] public object[] Args { get; set; }    // 方法参数值（仅 method）
	}

	public class EntryInfo
	{
		[JsonProperty("name")] public string Name { get; set; }      // 入口完整类名
	}

	// ===================================================================
	//  游戏数据查询服务 — 通过 DomainManager + Traverse 查询游戏数据
	// ===================================================================

	public static class BackendDataService
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		/// <summary>查询太吾信息</summary>
		public static JToken QueryTaiwuInfo()
		{
			var td = DomainManager.Taiwu;
			var t = HarmonyLib.Traverse.Create(td);

			int taiwuCharId = t.Field("_taiwuCharId").GetValue<int>();
			int closeFriendId = t.Field("_taiwuCharIdForCloseFriend").GetValue<int>();
			int generations = t.Field("_taiwuGenerationsCount").GetValue<int>();
			int legacyPoint = t.Field("_legacyPoint").GetValue<int>();
			short activeBuildings = t.Field("_activeBuildingCount").GetValue<short>();

			var result = new JObject
			{
				["taiwuCharId"] = taiwuCharId,
				["closeFriendCharId"] = closeFriendId,
				["generationsCount"] = generations,
				["legacyPoint"] = legacyPoint,
				["activeBuildingCount"] = activeBuildings
			};

			if (taiwuCharId >= 0)
			{
				var brief = GetCharacterBrief(taiwuCharId);
				if (brief != null) result["taiwuCharacter"] = brief;
			}
			if (closeFriendId >= 0)
			{
				var brief = GetCharacterBrief(closeFriendId);
				if (brief != null) result["closeFriendCharacter"] = brief;
			}

			return result;
		}

		/// <summary>查询角色信息</summary>
		public static JToken QueryCharacterInfo(Dictionary<string, object> rawParams)
		{
			if (rawParams == null || !rawParams.ContainsKey("charId"))
				return new JObject { ["error"] = "缺少 charId 参数" };

			int charId = ParseInt(rawParams["charId"]);
			if (charId < 0)
				return new JObject { ["error"] = $"charId 格式无效: {rawParams["charId"]}" };

			return GetCharacterDetail(charId);
		}

		/// <summary>查询角色姓名 — 使用 CharacterDomain 专用 API</summary>
		public static JToken QueryCharacterName(Dictionary<string, object> rawParams)
		{
			if (rawParams == null || !rawParams.ContainsKey("charId"))
				return new JObject { ["error"] = "缺少 charId 参数" };

			int charId = ParseInt(rawParams["charId"]);
			if (charId < 0)
				return new JObject { ["error"] = $"charId 格式无效: {rawParams["charId"]}" };

			try
			{
				// 方式 1: GetNameRelatedData — 含 FullName 的结构体
				var nameData = HarmonyLib.Traverse.Create(DomainManager.Character)
					.Method("GetNameRelatedData", new object[] { charId })
					.GetValue();

				if (nameData == null)
					return new JObject { ["error"] = "未找到角色名称数据" };

				var nt = HarmonyLib.Traverse.Create(nameData);
				var fullNameObj = nt.Field("FullName").GetValue();

				string surname = "", givenName = "";
				if (fullNameObj != null)
				{
					var ft = HarmonyLib.Traverse.Create(fullNameObj);
					surname = ft.Field("Surname").GetValue()?.ToString() ?? "";
					givenName = ft.Field("GivenName").GetValue()?.ToString() ?? "";
				}

				// 方式 2: GetRealName 作为备选/补充
				string surname2 = "", givenName2 = "";
				try
				{
					var charObj = HarmonyLib.Traverse.Create(DomainManager.Character)
						.Method("GetElement_Objects", new object[] { charId }).GetValue();
					if (charObj != null)
					{
						var realNameResult = HarmonyLib.Traverse.Create(typeof(CharacterDomain))
							.Method("GetRealName", new object[] { charObj }).GetValue();
						if (realNameResult != null)
						{
							var rt2 = HarmonyLib.Traverse.Create(realNameResult);
							surname2 = rt2.Field("Item1").GetValue()?.ToString() ?? "";
							givenName2 = rt2.Field("Item2").GetValue()?.ToString() ?? "";
						}
					}
				}
				catch { /* GetRealName 作为补充，失败也可接受 */ }

				return new JObject
				{
					["charId"] = charId,
					["surname"] = string.IsNullOrEmpty(surname) ? surname2 : surname,
					["givenName"] = string.IsNullOrEmpty(givenName) ? givenName2 : givenName,
					["fullName"] = (string.IsNullOrEmpty(surname) ? surname2 : surname)
						+ (string.IsNullOrEmpty(givenName) ? givenName2 : givenName)
				};
			}
			catch (Exception ex)
			{
				return new JObject { ["error"] = $"获取角色姓名失败: {ex.Message}" };
			}
		}

		/// <summary>查询世界信息</summary>
		public static JToken QueryWorldInfo()
		{
			var wd = DomainManager.World;
			var t = HarmonyLib.Traverse.Create(wd);

			int currDate = wd.GetCurrDate();
			int year = currDate / 12;
			int month = currDate % 12 + 1;
			uint worldId = t.Field("_worldId").GetValue<uint>();
			bool finishedInit = t.Field("_isFinishedInit").GetValue<bool>();

			return new JObject
			{
				["currDate"] = currDate,
				["year"] = year,
				["month"] = month,
				["worldId"] = worldId,
				["isFinishedInit"] = finishedInit
			};
		}

		private static JToken GetCharacterBrief(int charId)
		{
			try
			{
				var cd = DomainManager.Character;
				var charObj = HarmonyLib.Traverse.Create(cd).Method("GetElement_Objects", new object[] { charId }).GetValue();
				if (charObj == null) return null;

				var ct = HarmonyLib.Traverse.Create(charObj);

				// 姓名: Character 没有 _name 字段，须通过 GetRealName 获取
				string name = "";
				try
				{
					// CharacterDomain.GetRealName(Character) → (surname, givenName)
					var realNameResult = HarmonyLib.Traverse.Create(typeof(CharacterDomain))
						.Method("GetRealName", new object[] { charObj })
						.GetValue();
					if (realNameResult != null)
					{
						var rt = HarmonyLib.Traverse.Create(realNameResult);
						var surname = rt.Field("Item1").GetValue()?.ToString() ?? "";
						var givenName = rt.Field("Item2").GetValue()?.ToString() ?? "";
						name = surname + givenName;
					}
				}
				catch (Exception ex)
				{
					Logger.Warn($"[GetCharacterBrief] GetRealName 失败: {ex.Message}");
				}

				var loc = ct.Field("_location").GetValue();
				var age = ct.Field("_currAge").GetValue<short>();
				var gender = ct.Field("_gender").GetValue<sbyte>();

				return new JObject
				{
					["charId"] = charId,
					["name"] = name,
					["age"] = age,
					["gender"] = gender,
					["location"] = loc?.ToString()
				};
			}
			catch (Exception ex)
			{
				Logger.Warn($"[GetCharacterBrief] 获取角色信息失败: charId={charId}, {ex.Message}");
				return null;
			}
		}

		private static JToken GetCharacterDetail(int charId)
		{
			var cd = DomainManager.Character;
			var charObj = HarmonyLib.Traverse.Create(cd).Method("GetElement_Objects", new object[] { charId }).GetValue();
			if (charObj == null)
				return new JObject { ["error"] = $"未找到角色: charId={charId}" };

			return BackendJTokenConverter.ConvertToJToken(charObj, 3);
		}

		private static int ParseInt(object val)
		{
			if (val is int i) return i;
			if (val is long l) return (int)l;
			if (val is string s && int.TryParse(s, out var r)) return r;
			return -1;
		}

	}

	internal static class BackendJTokenConverter
	{
		internal static JToken ConvertToJToken(object obj, int maxDepth)
		{
			if (obj == null || maxDepth <= 0) return JValue.CreateNull();
			var type = obj.GetType();

			if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return new JValue(obj);
			if (type.IsEnum) return new JValue(obj.ToString());
			if (type == typeof(bool)) return new JValue((bool)obj);
			if (type == typeof(DateTime)) return new JValue(((DateTime)obj).ToString("O"));
			if (Nullable.GetUnderlyingType(type) != null)
				return ConvertToJToken(type.GetProperty("Value")?.GetValue(obj), maxDepth - 1);

			if (obj is IList list)
			{
				var arr = new JArray();
				foreach (var item in list) arr.Add(ConvertToJToken(item, maxDepth - 1));
				return arr;
			}
			if (obj is System.Collections.IDictionary dict)
			{
				var jo = new JObject();
				foreach (var k in dict.Keys) jo[k?.ToString() ?? ""] = ConvertToJToken(dict[k], maxDepth - 1);
				return jo;
			}

			if (maxDepth <= 1)
				return new JObject { ["_type"] = type.FullName, ["_toString"] = obj.ToString() };

			var t = HarmonyLib.Traverse.Create(obj);
			var result = new JObject { ["_type"] = type.FullName };
			try
			{
				var idVal = t.Method("GetId").GetValue();
				if (idVal != null) result["_id"] = ConvertToJToken(idVal, 1);
			}
			catch { /* 对象无 GetId 方法时跳过 */ }

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
				catch { /* 跳过序列化失败的字段 */ }
			}
			return result;
		}
	}

	// ===================================================================
	//  BackendChainExecutor — 反射链执行器（前后端各一份，协议对齐）
	// ===================================================================

	public static class BackendChainExecutor
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		private static string _lastInvokeError;

		/// <summary>最近一次链式调用的失败原因</summary>
		public static string LastError => _lastInvokeError;

		/// <summary>从 entry 解析出起点对象：按类名查找 → 尝试 Instance/GetInstance → 返回 Type 本身（静态方法用）</summary>
		public static object ResolveEntry(EntryInfo entry)
		{
			if (entry == null || string.IsNullOrEmpty(entry.Name))
				return null;

			var type = AccessTools.TypeByName(entry.Name);
			if (type == null)
				throw new ArgumentException($"找不到类型: {entry.Name}");

			// 尝试常见静态实例获取模式
			try
			{
				var instProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
				if (instProp != null) return instProp.GetValue(null);
			}
			catch { }
			try
			{
				var instField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
				if (instField != null) return instField.GetValue(null);
			}
			catch { }
			try
			{
				var getInst = type.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
				if (getInst != null) return getInst.Invoke(null, null);
			}
			catch { }

			// 没有实例 → 返回类型本身（供静态方法/字段调用）
			return type;
		}

		/// <summary>执行链返回 JToken 序列化结果</summary>
		public static JToken Execute(object obj, List<BackendChainStep> chain, int depth)
		{
			var resultObj = ExecuteToObject(obj, chain);
			if (resultObj == null) return JValue.CreateNull();
			return BackendJTokenConverter.ConvertToJToken(resultObj, depth);
		}

		/// <summary>执行链返回原始对象（供后续 attach 链接）</summary>
		public static object ExecuteToObject(object obj, List<BackendChainStep> chain)
		{
			if (chain == null || chain.Count == 0) return obj;
			object current = obj;
			for (int i = 0; i < chain.Count; i++)
			{
				_lastInvokeError = null;
				current = ExecuteStep(current, chain[i]);
				if (current == null)
				{
					Logger.Warn($"[BackendChain] 第 {i} 步失败: {chain[i].Step} {chain[i].Name} — {_lastInvokeError ?? "返回 null"}");
					return null;
				}
			}
			return current;
		}

		private static object ExecuteStep(object obj, BackendChainStep step)
		{
			if (obj == null) return null;
			var objType = obj is Type st ? st : obj.GetType();
			// obj 为 Type 时用 Traverse.Create(Type) 重载以访问静态成员
			var traverseTarget = obj is Type staticType
				? HarmonyLib.Traverse.Create(staticType)
				: HarmonyLib.Traverse.Create(obj);

			object val;
			switch (step.Step)
			{
				case "method":
					return InvokeMethod(obj, step.Name, step.ArgTypes, step.Args);
				case "field":
					val = traverseTarget.Field(step.Name).GetValue();
					if (val == null) _lastInvokeError = $"在 {objType.Name} 上找不到字段: {step.Name}";
					return val;
				case "property":
					val = traverseTarget.Property(step.Name).GetValue();
					if (val == null) _lastInvokeError = $"在 {objType.Name} 上找不到属性: {step.Name}";
					return val;
				default:
					_lastInvokeError = $"未知 step 类型: {step.Step}";
					return null;
			}
		}

		private static object InvokeMethod(object obj, string methodName, string[] argTypes, object[] args)
		{
			if (string.IsNullOrEmpty(methodName)) return null;
			int argCount = args?.Length ?? 0;

			// 解析泛型方法名: GetComponent<RectTransform> → 方法名 GetComponent, 类型 RectTransform
			string genericTypeName = null;
			var genericMatch = System.Text.RegularExpressions.Regex.Match(methodName, @"^(\w+)<(.+)>$");
			if (genericMatch.Success)
			{
				methodName = genericMatch.Groups[1].Value;
				genericTypeName = genericMatch.Groups[2].Value;
			}

			if (obj is Type staticType)
				return InvokeStaticMethod(staticType, methodName, genericTypeName, argTypes, args);
			else
				return InvokeInstanceMethod(obj, methodName, genericTypeName, argTypes, args);
		}

		private static object InvokeStaticMethod(Type type, string methodName, string genericTypeName, string[] argTypes, object[] args)
		{
			// 如果指定了 argTypes，用精确类型查找
			if (argTypes != null && argTypes.Length > 0)
			{
				var paramTypes = argTypes.Select(AccessTools.TypeByName).ToArray();
				var mi = AccessTools.Method(type, methodName, paramTypes);
				if (mi != null)
				{
					try
					{
						var method = ApplyGenericIfNeeded(mi, genericTypeName);
						return method.Invoke(null, args);
					}
					catch (Exception ex)
					{
						var inner = ex.InnerException?.Message ?? ex.Message;
						throw new InvalidOperationException($"静态方法 {methodName} 调用失败: {inner}");
					}
				}
			}

			// 无 argTypes → 枚举尝试
			var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Where(m => m.Name == methodName && m.GetParameters().Length == (args?.Length ?? 0))
				.ToList();

			foreach (var m in methods)
			{
				try
				{
					var method = ApplyGenericIfNeeded(m, genericTypeName);
					var pa = method.GetParameters();
					var conv = ConvertArgsToParamTypes(args, pa.Select(p => p.ParameterType).ToArray());
					return method.Invoke(null, conv);
				}
				catch { continue; }
			}

			_lastInvokeError = $"找不到静态方法 {methodName}({args?.Length ?? 0} 参数) 在 {type.Name}";
			return null;
		}

		private static object InvokeInstanceMethod(object obj, string methodName, string genericTypeName, string[] argTypes, object[] args)
		{
			var t = HarmonyLib.Traverse.Create(obj);
			int argCount = args?.Length ?? 0;

			// 先用 Traverse 尝试（最简路径）
			if (argCount > 0)
			{
				try
				{
					var val = t.Method(methodName, args).GetValue();
					if (val != null) return val;
				}
				catch { }
			}
			else
			{
				try
				{
					var val = t.Method(methodName).GetValue();
					if (val != null) return val;
				}
				catch { }
			}

			// 如果指定了 argTypes，用精确类型查找
			if (argTypes != null && argTypes.Length > 0)
			{
				var paramTypes = argTypes.Select(AccessTools.TypeByName).ToArray();
				var mi = AccessTools.Method(obj.GetType(), methodName, paramTypes);
				if (mi != null)
				{
					try
					{
						var method = ApplyGenericIfNeeded(mi, genericTypeName);
						var conv = ConvertArgsToParamTypes(args, paramTypes);
						return method.Invoke(obj, conv);
					}
					catch (Exception ex)
					{
						var inner = ex.InnerException?.Message ?? ex.Message;
						_lastInvokeError = $"实例方法 {methodName} (精确类型) 调用失败: {inner}";
					}
				}
			}

			// 遍历所有同名方法，参数数量匹配的，尝试枚举/数字转换后调用
			var methods = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Where(m => m.Name == methodName && m.GetParameters().Length == argCount)
				.ToList();

			foreach (var method in methods)
			{
				try
				{
					var toInvoke = ApplyGenericIfNeeded(method, genericTypeName);
					var pa = toInvoke.GetParameters();
					var conv = ConvertArgsToParamTypes(args, pa.Select(p => p.ParameterType).ToArray());
					var result = toInvoke.Invoke(obj, conv);
					if (result != null) return result;
				}
				catch { continue; }
			}

			_lastInvokeError = $"方法 {methodName} 全部尝试失败，objType={obj.GetType().Name}";
			return null;
		}

		private static MethodInfo ApplyGenericIfNeeded(MethodInfo method, string genericTypeName)
		{
			if (genericTypeName == null || !method.IsGenericMethodDefinition) return method;
			var genericType = AccessTools.TypeByName(genericTypeName);
			if (genericType == null) throw new ArgumentException($"找不到泛型类型: {genericTypeName}");
			return method.MakeGenericMethod(genericType);
		}

		private static object[] ConvertArgsToParamTypes(object[] args, Type[] targetTypes)
		{
			if (args == null || args.Length == 0) return args;
			var result = new object[args.Length];
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == null) { result[i] = null; continue; }
				var t = targetTypes[i];
				if (t.IsAssignableFrom(args[i].GetType())) { result[i] = args[i]; continue; }
				if (t.IsEnum && args[i] is int iv) { result[i] = Enum.ToObject(t, iv); continue; }
				if (t.IsEnum && args[i] is long lv) { result[i] = Enum.ToObject(t, (int)lv); continue; }
				if (args[i] is IConvertible && t.IsValueType && !t.IsEnum)
				{
					try { result[i] = Convert.ChangeType(args[i], t); continue; }
					catch { }
				}
				result[i] = args[i];
			}
			return result;
		}
	}
}
