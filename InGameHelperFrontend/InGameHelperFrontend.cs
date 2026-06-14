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
using UnityEngine.EventSystems;

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

					case "front_code":
				case "simulate_click":
				case "shop_buy_filtered":
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
			object rawObject = null; // 供 attach 链使用的原始对象

			switch (request.Type)
			{
				case "scene_hierarchy":
					resultData = SceneQueryService.QueryHierarchy(request.Params);
					break;
				case "component_info":
					rawObject = SceneQueryService.FindComponent(request.Params);
					resultData = SceneQueryService.QueryComponentInfo(request.Params);
					break;
				case "component_field":
					resultData = SceneQueryService.QueryComponentField(request.Params, out rawObject);
					break;
				case "front_code":
					resultData = ExecuteFrontCode(request.RequestId, request.Params);
					break;
				case "simulate_click":
					resultData = SimulateClick(request.Params);
					break;
				case "shop_buy_filtered":
					resultData = ShopBuyFiltered(request.Params);
					break;
			}

			// 统一处理 attach
			if (resultData != null && rawObject != null && request.Params != null
				&& request.Params.TryGetValue("attach", out var attachVal) && attachVal is JArray attachArr && attachArr.Count > 0)
			{
				var lastAttach = attachArr[attachArr.Count - 1];
				var attachParams = lastAttach["params"] as JObject;
				if (attachParams != null)
				{
					var attachChain = attachParams["chain"]?.ToObject<List<FrontendChainStep>>();
					int attachDepth = attachParams["resultDepth"]?.Value<int>() ?? 3;
					if (attachChain != null && attachChain.Count > 0)
					{
						var attachResult = FrontendChainExecutor.ExecuteToObject(rawObject, attachChain);
						if (attachResult != null)
							resultData = JTokenConverter.ConvertToJToken(attachResult, attachDepth);
					}
				}
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

		/// <summary>执行 front_code 请求：entry → chain → attach → JToken（前后端各一份，协议对齐）</summary>
		private static JToken ExecuteFrontCode(string requestId, Dictionary<string, object> rawParams)
		{
			if (rawParams == null)
				return new JObject { ["error"] = "params 不能为空" };

			EntryInfo entry = null;
			if (rawParams.TryGetValue("entry", out var entryObj) && entryObj is JObject entryJObj)
				entry = entryJObj.ToObject<EntryInfo>();

			List<FrontendChainStep> chain = null;
			if (rawParams.TryGetValue("chain", out var chainObj) && chainObj is JArray chainJArr)
				chain = chainJArr.ToObject<List<FrontendChainStep>>();

			int resultDepth = 3;
			if (rawParams.TryGetValue("resultDepth", out var depthVal))
			{
				if (depthVal is long dl) resultDepth = (int)dl;
				else if (depthVal is int di) resultDepth = di;
			}

			JArray attachArr = null;
			if (rawParams.TryGetValue("attach", out var attachVal) && attachVal is JArray attachJArr)
				attachArr = attachJArr;

			object current;
			if (entry != null)
			{
				current = FrontendChainExecutor.ResolveEntry(entry);
				if (current == null)
					return new JObject { ["error"] = $"找不到类型: {entry.Name}" };
			}
			else
			{
				return new JObject { ["error"] = "独立请求必须指定 entry" };
			}

			if (chain != null && chain.Count > 0)
			{
				current = FrontendChainExecutor.ExecuteToObject(current, chain);
				if (current == null)
				{
					var detail = FrontendChainExecutor.LastError ?? "(无详细信息)";
					return new JObject { ["error"] = $"链式调用执行失败: {detail}" };
				}
			}

			if (attachArr != null)
			{
				foreach (var item in attachArr)
				{
					var attachParams = item["params"] as JObject;
					if (attachParams == null) continue;

					var attachChain = attachParams["chain"]?.ToObject<List<FrontendChainStep>>();
					if (attachChain == null || attachChain.Count == 0) continue;

					var attachDepth = attachParams["resultDepth"]?.Value<int>() ?? resultDepth;
					resultDepth = attachDepth;

					current = FrontendChainExecutor.ExecuteToObject(current, attachChain);
					if (current == null)
					{
						var detail = FrontendChainExecutor.LastError ?? "(无详细信息)";
						return new JObject { ["error"] = $"附加链调用失败: {detail}" };
					}
				}
			}

			return JTokenConverter.ConvertToJToken(current, resultDepth);
		}

		/// <summary>模拟指针点击，通过 EventSystem 触发 IPointerClickHandler</summary>
		private static JToken SimulateClick(Dictionary<string, object> rawParams)
		{
			var path = rawParams.GetValueOrDefault<string>("gameObjectPath") ?? "";
			if (string.IsNullOrEmpty(path))
				return new JObject { ["error"] = "gameObjectPath 不能为空" };

			var go = SceneQueryService.FindGameObjectByPath(path);
			if (go == null)
				return new JObject { ["error"] = $"未找到 GameObject: {path}" };

			if (EventSystem.current == null)
				return new JObject { ["error"] = "EventSystem.current 为空" };

			var ped = new PointerEventData(EventSystem.current);
			// 设置一个默认的点击位置
			var rt = go.GetComponent<RectTransform>();
			if (rt != null)
			{
				var rect = rt.rect;
				// 计算屏幕中心位置
				var corners = new Vector3[4];
				rt.GetWorldCorners(corners);
				ped.position = new Vector2(
					(corners[0].x + corners[2].x) / 2f,
					(corners[0].y + corners[2].y) / 2f
				);
			}
			else
			{
				ped.position = Vector2.zero;
			}

			// 查找所有 IPointerClickHandler 组件
			var clickHandlers = go.GetComponents<IPointerClickHandler>();
			if (clickHandlers == null || clickHandlers.Length == 0)
				return new JObject { ["error"] = $"在 {go.name} 上找不到 IPointerClickHandler" };

			int executed = 0;
			foreach (var handler in clickHandlers)
			{
				ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
				executed++;
			}

			return new JObject
			{
				["success"] = true,
				["target"] = go.name,
				["executed"] = executed
			};
		}

		/// <summary>
		/// 通用商店筛选购买。通过 filters 参数指定筛选条件，只将匹配的商品加入交换列表。
		/// filter 每项格式：
		/// {
		///   "itemType": sbyte,   // 物品类型（5=Material, 7=Food...），可选
		///   "minGrade": sbyte,   // 最低品级（含），可选
		///   "maxGrade": sbyte,   // 最高品级（含），可选
		///   "templateId": int,   // 具体模板ID，可选
		///   "name": string,      // 商品名包含（模糊匹配），可选
		///   "amount": int,       // 购买数量（0=全部），可选，默认全部
		///   "maxPrice": int,     // 最高单价，可选
		/// }
		/// 条件之间是与(AND)关系。
		/// </summary>
		private static JToken ShopBuyFiltered(Dictionary<string, object> rawParams)
		{
			var shopGo = SceneQueryService.FindGameObjectByPath("Camera_UIRoot/Canvas/LayerPopUp/ViewShop");
			if (shopGo == null)
				return new JObject { ["error"] = "未找到 ViewShop" };

			var viewShop = shopGo.GetComponent("ViewShop");
			if (viewShop == null)
				return new JObject { ["error"] = "未找到 ViewShop 组件" };

			var t = HarmonyLib.Traverse.Create(viewShop);

			// 获取 Exchange 对象
			var exchange = t.Property("Exchange").GetValue();
			if (exchange == null)
				return new JObject { ["error"] = "Exchange 为空" };

			// 获取商品列表
			var listObj = t.Method("GetTargetTradeableList").GetValue();
			if (listObj == null)
				return new JObject { ["error"] = "GetTargetTradeableList 返回空" };

			var itemList = listObj as System.Collections.IList;
			if (itemList == null)
				return new JObject { ["error"] = "商品列表不是 IList" };

			// 解析筛选条件
			var filters = new List<JObject>();
			if (rawParams.TryGetValue("filters", out var filtersVal) && filtersVal is JArray filtersArr)
			{
				foreach (var f in filtersArr)
				{
					if (f is JObject jo)
						filters.Add(jo);
				}
			}
			// 如果没有筛选条件，默认只买材料
			if (filters.Count == 0)
			{
				filters.Add(new JObject { ["itemType"] = 5 });
			}

			int addedCount = 0;
			int skippedCount = 0;
			var addedItems = new JArray();

			// 遍历所有商品
			foreach (var item in itemList)
			{
				if (item == null) continue;

				var itemTraverse = HarmonyLib.Traverse.Create(item);

				// 读取 Key
				var key = itemTraverse.Property("Key").GetValue();
				if (key == null) { skippedCount++; continue; }

				var keyTraverse = HarmonyLib.Traverse.Create(key);

				// 读取字段（全部提前读出）
				sbyte itemType = GetSbyte(keyTraverse.Field("ItemType").GetValue());
				short templateId = GetShort(keyTraverse.Field("TemplateId").GetValue());
				int itemId = GetInt(keyTraverse.Field("Id").GetValue());

				// 读取 PricePercent（涨跌价%，负=降价，正=涨价）
				int pricePercent = GetInt(itemTraverse.Field("PricePercent").GetValue());

				// 读取 Grade（property，范围 0~9，-1 表示无）
				var gradeVal = itemTraverse.Property("Grade").GetValue();
				sbyte grade = -1;
				if (gradeVal is sbyte gsb) grade = gsb;
				else if (gradeVal is int giv) grade = (sbyte)giv;

				int amount = GetInt(itemTraverse.Field("Amount").GetValue());
				int value = GetInt(itemTraverse.Field("Value").GetValue()); // 单价

				// 读取商品名（ToString 中有名字，但更准确的方式是查 TemplateId 对应配置）
				// 用 ToString() 取个简短名字
				var rawName = item.ToString() ?? "";
				var itemName = ExtractNameFromString(rawName);

				// 检查是否匹配任一筛选条件（条件之间 OR）
				bool matched = false;
				JObject matchedFilter = null;
				foreach (var filter in filters)
				{
					if (MatchFilter(item, itemTraverse, keyTraverse,
						itemType, templateId, itemId, grade, amount, value, itemName, pricePercent,
						filter))
					{
						matched = true;
						matchedFilter = filter;
						break;
					}
				}

				if (!matched)
				{
					skippedCount++;
					continue;
				}

				// 确定购买数量
				int buyAmount = amount; // 默认全部
				if (matchedFilter != null && matchedFilter.TryGetValue("amount", out var amtToken))
				{
					int reqAmt = amtToken.Value<int>();
					if (reqAmt > 0 && reqAmt < buyAmount)
						buyAmount = reqAmt;
				}

				// 调用 Exchange.SelectTargetItem(item, buyAmount)
				var exchangeTraverse = HarmonyLib.Traverse.Create(exchange);
				try
				{
					exchangeTraverse.Method("SelectTargetItem", new object[] { item, buyAmount }).GetValue();
					addedCount++;
					addedItems.Add(new JObject
					{
						["name"] = itemName,
						["type"] = itemType,
						["grade"] = grade,
						["amount"] = buyAmount,
						["raw"] = rawName.Length > 80 ? rawName.Substring(0, 80) : rawName
					});
				}
				catch (Exception ex)
				{
					MyUtils.MyLog($"ShopBuyFiltered: SelectTargetItem 失败: {ex.Message}");
				}
			}

			// 调用 Refresh 更新 UI
			try { t.Method("Refresh").GetValue(); } catch { }

			return new JObject
			{
				["success"] = true,
				["totalItems"] = itemList.Count,
				["added"] = addedCount,
				["skipped"] = skippedCount,
				["items"] = addedItems
			};
		}

		/// <summary>检查单个商品是否匹配筛选条件</summary>
		private static bool MatchFilter(object item, HarmonyLib.Traverse itemTraverse, HarmonyLib.Traverse keyTraverse,
			sbyte itemType, short templateId, int itemId, sbyte grade, int amount, int value, string itemName, int pricePercent,
			JObject filter)
		{
			// itemType 匹配
			if (filter.TryGetValue("itemType", out var itToken))
			{
				var reqType = itToken.Value<int>();
				if (itemType != (sbyte)reqType) return false;
			}

			// templateId 匹配（具体商品）
			if (filter.TryGetValue("templateId", out var tplToken))
			{
				var reqTpl = tplToken.Value<int>();
				if (templateId != reqTpl) return false;
			}

			// 品级下限
			if (filter.TryGetValue("minGrade", out var minGToken))
			{
				var minG = minGToken.Value<int>();
				if (grade < (sbyte)minG) return false;
			}

			// 品级上限
			if (filter.TryGetValue("maxGrade", out var maxGToken))
			{
				var maxG = maxGToken.Value<int>();
				if (grade > (sbyte)maxG) return false;
			}

			// 商品名模糊匹配
			if (filter.TryGetValue("name", out var nameToken))
			{
				var namePattern = nameToken.Value<string>();
				if (!string.IsNullOrEmpty(namePattern) && !itemName.Contains(namePattern))
					return false;
			}

			// 最高单价
			if (filter.TryGetValue("maxPrice", out var priceToken))
			{
				var maxPrice = priceToken.Value<int>();
				if (value > maxPrice) return false;
			}

			// 涨跌价筛选：pricePercent<0=降价, pricePercent>0=涨价, pricePercent=0=原价
			if (filter.TryGetValue("pricePercent", out var ppToken))
			{
				var ppVal = ppToken.Value<int>();
				if (ppVal < 0 && pricePercent >= 0) return false;  // 要降价，但实际非降价
				if (ppVal > 0 && pricePercent <= 0) return false;  // 要涨价，但实际非涨价
				if (ppVal == 0 && pricePercent != 0) return false; // 要原价，但实际有涨跌
			}

			return true; // 所有条件通过
		}

		/// <summary>从 ItemDisplayData.ToString() 中提取商品名</summary>
		private static string ExtractNameFromString(string raw)
		{
			// 格式: "ItemDisplayData({Material, 绍兴麻鸭 (58), 137569, 1000}, 3, 0/0)"
			try
			{
				var start = raw.IndexOf(", ") + 2;
				var end = raw.IndexOf(" (");
				if (start > 1 && end > start)
					return raw.Substring(start, end - start);
			}
			catch { }
			return raw;
		}

		private static sbyte GetSbyte(object val)
		{
			if (val is sbyte sb) return sb;
			if (val is int iv) return (sbyte)iv;
			if (val is long lv) return (sbyte)lv;
			if (val is byte bv) return (sbyte)bv;
			return -1;
		}

		private static short GetShort(object val)
		{
			if (val is short s) return s;
			if (val is int iv) return (short)iv;
			if (val is long lv) return (short)lv;
			if (val is byte bv) return bv;
			return -1;
		}

		private static int GetInt(object val)
		{
			if (val is int iv) return iv;
			if (val is long lv) return (int)lv;
			if (val is short sv) return sv;
			return 1;
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
		/// <summary>从请求参数中提取路径并查找 GameObject（供 attach 链使用）</summary>
		public static object FindGameObject(Dictionary<string, object> rawParams)
		{
			if (rawParams == null) return null;
			var path = rawParams.GetValueOrDefault<string>("gameObjectPath") ?? "";
			if (string.IsNullOrEmpty(path)) return null;
			return FindGameObjectByPath(path);
		}

		/// <summary>从请求参数中提取路径和组件类型，查找匹配的 Component（供 attach 链使用）</summary>
		public static object FindComponent(Dictionary<string, object> rawParams)
		{
			if (rawParams == null) return null;
			var path = rawParams.GetValueOrDefault<string>("gameObjectPath") ?? "";
			var compTypeName = rawParams.GetValueOrDefault<string>("componentType") ?? "";
			if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(compTypeName)) return null;
			var go = FindGameObjectByPath(path);
			if (go == null) return null;
			foreach (var comp in go.GetComponents<Component>())
			{
				if (comp == null) continue;
				var name = comp.GetType().Name;
				if (string.Equals(name, compTypeName, StringComparison.OrdinalIgnoreCase) ||
					comp.GetType().FullName.Contains(compTypeName))
					return comp;
			}
			return null;
		}

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

		public static JToken QueryComponentField(Dictionary<string, object> rawParams, out object rawFieldValue)
		{
			rawFieldValue = null;
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
					rawFieldValue = val;
					if (val != null)
						return new JObject { ["type"] = comp.GetType().FullName, ["componentType"] = name, ["field"] = fieldName, ["value"] = JTokenConverter.ConvertToJToken(val, 1) };
				}
				var f = t.Field(fieldName);
				if (f != null)
				{
					var val = f.GetValue();
					rawFieldValue = val;
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
			foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(f => ConvertUtils.IsSimpleType(f.FieldType)).Take(20))
			{
				try { var val = t.Field(f.Name).GetValue(); if (val != null) fields[f.Name] = JTokenConverter.ConvertToJToken(val, 1); } catch { }
			}
			foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && ConvertUtils.IsSimpleType(p.PropertyType)).Take(20))
			{
				try { var val = t.Property(p.Name).GetValue(); if (val != null) fields[p.Name] = JTokenConverter.ConvertToJToken(val, 1); } catch { }
			}
		}

		public static GameObject FindGameObjectByPath(string path)
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

	// ===================================================================
	//  Chain 模型 — 反射链数据结构（前后端各一份，协议对齐）
	// ===================================================================

	public class FrontendChainStep
	{
		[JsonProperty("step")] public string Step { get; set; }      // "method" | "field" | "property"
		[JsonProperty("name")] public string Name { get; set; }      // 成员名
		[JsonProperty("stepType")] public string StepType { get; set; } // 结果类型全名
		[JsonProperty("argTypes")] public string[] ArgTypes { get; set; } // 方法参数类型（仅 method）
		[JsonProperty("args")] public object[] Args { get; set; }    // 方法参数值（仅 method）
		[JsonProperty("codeArgs")] public int[] CodeArgs { get; set; }
		// codeArgs: 参数注入标记，与 args 索引一一对应，值为 1 时表示对应 args 元素为 code 子请求
	}

	public class EntryInfo
	{
		[JsonProperty("name")] public string Name { get; set; }      // 入口完整类名
	}

	// ===================================================================
	//  FrontendChainExecutor — 前端反射链执行器（前后端各一份，协议对齐）
	// ===================================================================

	public static class FrontendChainExecutor
	{
		private static string _lastInvokeError;

		/// <summary>最近一次链式调用的失败原因</summary>
		public static string LastError => _lastInvokeError;

		/// <summary>从 entry 解析出起点对象</summary>
		public static object ResolveEntry(EntryInfo entry)
		{
			if (entry == null || string.IsNullOrEmpty(entry.Name)) return null;
			Type targetType = null;
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				targetType = asm.GetType(entry.Name, false);
				if (targetType != null) break;
			}
			if (targetType == null) return null;

			// 尝试常见静态实例获取模式
			try
			{
				var instProp = AccessTools.Property(targetType, "Instance");
				if (instProp != null) return instProp.GetValue(null);
			}
			catch { }
			try
			{
				var instField = AccessTools.Field(targetType, "Instance");
				if (instField != null) return instField.GetValue(null);
			}
			catch { }
			try
			{
				var getInst = AccessTools.Method(targetType, "getInstance");
				if (getInst != null) return getInst.Invoke(null, null);
			}
			catch { }

			return targetType;
		}

		public static JToken Execute(object obj, List<FrontendChainStep> chain, int depth)
		{
			var resultObj = ExecuteToObject(obj, chain);
			if (resultObj == null) return JValue.CreateNull();
			return JTokenConverter.ConvertToJToken(resultObj, depth);
		}

		public static object ExecuteToObject(object obj, List<FrontendChainStep> chain)
		{
			if (chain == null || chain.Count == 0) return obj;
			object current = obj;
			for (int i = 0; i < chain.Count; i++)
			{
				_lastInvokeError = null;
				current = ExecuteStep(current, chain[i]);
				if (current == null)
				{
					MyUtils.MyLog($"[FrontendChain] 第 {i} 步失败: {chain[i].Step} {chain[i].Name} — {_lastInvokeError ?? "返回 null"}");
					return null;
				}
			}
			return current;
		}

		private static object ExecuteStep(object obj, FrontendChainStep step)
		{
			if (obj == null) return null;
			var objType = obj is Type st ? st : obj.GetType();
			var traverseTarget = obj is Type staticType
				? HarmonyLib.Traverse.Create(staticType)
				: HarmonyLib.Traverse.Create(obj);

			object val;
			switch (step.Step)
			{
				case "method":
					// 处理 codeArgs：将 args 中的子请求递归解析为实际对象
					var resolvedArgs = ResolveCodeArgs(step.Args, step.CodeArgs);
					return InvokeMethod(obj, step.Name, step.ArgTypes, resolvedArgs);
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

		/// <summary>将 args 中标记为 code 子请求的参数递归解析为实际对象</summary>
		private static object[] ResolveCodeArgs(object[] args, int[] codeArgs)
		{
			if (args == null || codeArgs == null || codeArgs.Length == 0)
				return args;

			// 创建副本以免修改原始 step
			var resolved = new object[args.Length];
			Array.Copy(args, resolved, args.Length);

			for (int idx = 0; idx < codeArgs.Length && idx < resolved.Length; idx++)
			{
				if (codeArgs[idx] != 1) continue;
				if (!(resolved[idx] is JObject subRequest)) continue;

				// 子请求必须为 front_code 类型（前端只支持同类型注入）
				var subType = subRequest["type"]?.ToString();
				if (subType != "front_code") continue;

				var subParams = subRequest["params"] as JObject;
				if (subParams == null) continue;

				// 解析子请求的 entry
				EntryInfo entry = null;
				if (subParams.TryGetValue("entry", out var entryObj) && entryObj is JObject entryJObj)
					entry = entryJObj.ToObject<EntryInfo>();

				// 解析子请求的 chain
				List<FrontendChainStep> subChain = null;
				if (subParams.TryGetValue("chain", out var chainObj) && chainObj is JArray chainJArr)
					subChain = chainJArr.ToObject<List<FrontendChainStep>>();

				if (entry == null) continue;

				// 递归处理子请求 chain 中的 codeArgs
				if (subChain != null)
				{
					foreach (var subStep in subChain)
					{
						if (subStep.CodeArgs != null && subStep.CodeArgs.Length > 0 && subStep.Args != null)
						{
							subStep.Args = ResolveCodeArgs(subStep.Args, subStep.CodeArgs);
						}
					}
				}

				// 执行子请求的链，得到原始对象
				object current;
				try
				{
					current = ResolveEntry(entry);
					if (current == null)
					{
						MyUtils.MyLog($"[FrontendChain] codeArgs 子请求入口解析失败: {entry.Name}");
						continue;
					}
					if (subChain != null && subChain.Count > 0)
					{
						current = ExecuteToObject(current, subChain);
						if (current == null)
						{
							MyUtils.MyLog($"[FrontendChain] codeArgs 子请求链执行失败: {entry.Name}");
							continue;
						}
					}
				}
				catch (Exception ex)
				{
					MyUtils.MyLog($"[FrontendChain] codeArgs 子请求异常: {ex.Message}");
					continue;
				}

				resolved[idx] = current;
			}

			return resolved;
		}

		private static object InvokeMethod(object obj, string methodName, string[] argTypes, object[] args)
		{
			if (string.IsNullOrEmpty(methodName)) return null;

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
			int argCount = args?.Length ?? 0;

			if (argTypes != null && argTypes.Length > 0)
			{
				var paramTypes = argTypes.Select(FindTypeName).ToArray();
				var mi = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, paramTypes, null);
				if (mi != null)
				{
					try
					{
						var method = ApplyGenericIfNeeded(mi, genericTypeName);
						var conv = ConvertArgsToParamTypes(args, paramTypes);
						return method.Invoke(null, conv);
					}
					catch (Exception ex)
					{
						var inner = ex.InnerException?.Message ?? ex.Message;
						_lastInvokeError = $"静态方法 {methodName} 调用失败: {inner}";
						return null;
					}
				}
			}

			var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
				.Where(m => m.Name == methodName && m.GetParameters().Length == argCount).ToList();
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

			_lastInvokeError = $"找不到静态方法 {methodName}({argCount} 参数) 在 {type.Name}";
			return null;
		}

		private static object InvokeInstanceMethod(object obj, string methodName, string genericTypeName, string[] argTypes, object[] args)
		{
			var t = HarmonyLib.Traverse.Create(obj);
			int argCount = args?.Length ?? 0;

			// 先用 Traverse
			if (argCount > 0)
			{
				try { var v = t.Method(methodName, args).GetValue(); if (v != null) return v; } catch { }
			}
			else
			{
				try { var v = t.Method(methodName).GetValue(); if (v != null) return v; } catch { }
			}

			// argTypes 精确查找
			if (argTypes != null && argTypes.Length > 0)
			{
				var paramTypes = argTypes.Select(FindTypeName).ToArray();
				var mi = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, paramTypes, null);
				if (mi != null)
				{
					try
					{
						var method = ApplyGenericIfNeeded(mi, genericTypeName);
						var conv = ConvertArgsToParamTypes(args, paramTypes);
						var result = method.Invoke(obj, conv);
						// void 方法返回 null，应返回原对象以继续链式执行
						if (result == null && method.ReturnType == typeof(void)) return obj;
						return result;
					}
					catch (Exception ex)
					{
						_lastInvokeError = $"方法 {methodName} 精确调用失败: {ex.InnerException?.Message ?? ex.Message}";
					}
				}
			}

			// 遍历所有同名方法尝试调用
			var methods = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				.Where(m => m.Name == methodName && m.GetParameters().Length == argCount).ToList();
			foreach (var m in methods)
			{
				try
				{
					var method = ApplyGenericIfNeeded(m, genericTypeName);
					var pa = method.GetParameters();
					var conv = ConvertArgsToParamTypes(args, pa.Select(p => p.ParameterType).ToArray());
					var result = method.Invoke(obj, conv);
					// void 方法返回 null，应返回原对象以继续链式执行
					if (result == null && method.ReturnType == typeof(void)) return obj;
					return result;
				}
				catch { continue; }
			}

			_lastInvokeError = $"方法 {methodName} 全部尝试失败，objType={obj.GetType().Name}";
			return null;
		}

		private static MethodInfo ApplyGenericIfNeeded(MethodInfo method, string genericTypeName)
		{
			if (genericTypeName == null || !method.IsGenericMethodDefinition) return method;
			var genericType = FindTypeName(genericTypeName);
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
					try { result[i] = Convert.ChangeType(args[i], t); continue; } catch { }
				}
				result[i] = args[i];
			}
			return result;
		}

		private static Type FindTypeName(string name)
		{
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				var t = asm.GetType(name, false);
				if (t != null) return t;
			}
			return null;
		}
	}
}
