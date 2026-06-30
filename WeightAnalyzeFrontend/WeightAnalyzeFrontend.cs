//#define taiwuNormal
#define taiwuTest


using CharacterDataMonitor;
using Config;
using FrameWork;
using FrameWork.ModSystem;
using FrameWork.UISystem.UIElements;
using Game.Components.ListStyleGeneralScroll.Item;
using Game.Views.Exchange;
using GameData.Domains.Character;
using GameData.Domains.Character.Display;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Map;
using GameData.Domains.Taiwu;
using GameData.Domains.Taiwu.Display;
using GameData.Domains.TaiwuEvent.DisplayEvent;
using GameData.Serializer;
using GameData.Utilities;
using HarmonyLib;
using HarmonyLib.Tools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaiwuModdingLib.Core.Plugin;
using TaiwuModdingLib.Core.Utils;
using TMPro;
using UICommon.Character;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
//using UnityEngine.Windows;
//using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
//using static GameData.Domains.Item.ItemOperationType;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;



namespace WeightAnalyze
{
    public class MyUtils
    {
        public static void MyLog(string log)
        {
            Debug.Log($"[{nameof(WeightAnalyze)}] {log}");
        }

        public static void DelayCall(Action action, float delay, bool real)
        {
            //Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFace, 0, avatar, null, relatedData));
            GameApp.Instance.StartCoroutine(DelayCoroutine(action, delay, real));
        }

        private static IEnumerator DelayCoroutine(Action action, float delay, bool real)
        {
            if(real)
                yield return new WaitForSecondsRealtime(delay);
            else 
                yield return new WaitForSeconds(delay);
            action?.Invoke();
        }

        public static void ShowMonoCur(GameObject gameObject)
        {
            ShowMonoHelper(gameObject.transform, 0, gameObject.transform);
        }

        public static void ShowMonoToParent(Transform transform)
        {
            var canvas = transform.GetComponentInParent<Canvas>();
            if( canvas != null )
            {
                var depth = 0;
                var cur = transform;
                while(cur != canvas.transform)
                {
                    ShowMonoOne(cur, depth, prefix:cur.GetSiblingIndex().ToString());
                    cur = cur.parent;
                }
                ShowMonoOne(canvas.transform, depth);
            }
        }

        public static void ShowMono(GameObject gameObject)
        {
            var canvas = gameObject.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                ShowMonoHelper(canvas.transform, 0, gameObject.transform);
            }
        }

        public static void ShowMonoHelper(Transform transform, int depth, Transform sp)
        {
            ShowMonoOne(transform, depth, sp);
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                ShowMonoHelper(child, depth + 1, sp);
            }
        }

        public static void ShowMonoOne(Transform transform, int depth=0, Transform sp=null, string prefix="", string postfix = "")
        {
            // 构建缩进字符串
            var indent = new string('\t', depth);
            var specialMark = (sp == transform) ? "<<" : "";

            // 构建组件信息
            var monos = transform.GetComponents<MonoBehaviour>();
            var monoNames = monos == null ? "" : string.Join(",", monos.Select(m => m.GetType().Name));

            var btn = transform.GetComponent<Button>();
            var isbtn = btn == null ? "" : "(isbtn)";

            // 构建完整日志信息
            var str = $"{indent}{prefix}{transform.gameObject.name} {specialMark} ({monoNames}) {isbtn}{postfix}";

            // 先打印当前节点，再递归子节点
            MyLog(str);
        }

        public static void CopyBaseClassFieldsIncludingParents(Component source, Component destination, Type baseType)
        {
            Type currentType = baseType;

            // 遍历从baseType开始到MonoBehaviour的整个继承链
            while (currentType != null && currentType != typeof(MonoBehaviour) && currentType != typeof(System.Object))
            {
                // 获取当前类型的字段
                FieldInfo[] fields = currentType.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (FieldInfo field in fields)
                {
                    // 跳过不应该复制的字段
                    if (field.IsStatic) continue;

                    try
                    {
                        field.SetValue(destination, field.GetValue(source));
                        //MyLog($"复制字段 {field.Name} {field.GetValue(destination)}<-{field.GetValue(source)}");
                    }
                    catch (Exception ex)
                    {
                        //Debug.LogWarning($"复制字段 {field.Name} 时出错: {ex.Message}");
                    }
                }

                // 移动到父类
                currentType = currentType.BaseType;
            }
        }

        public static bool isAnyKey(List<KeyCode> keyCodes, out KeyCode keyCode)
        {
            for (int i = 0; i < keyCodes.Count; i++)
            {
                if (Input.GetKeyDown(keyCodes[i]))
                {
                    keyCode = keyCodes[i];
                    return true;
                }
            }
            keyCode = KeyCode.None;
            return false;
        }

        public static Color Color16A(uint hex)
        {
            return new Color32(
                    (byte)((hex >> 24) & 0xFF),
                    (byte)((hex >> 16) & 0xFF),
                    (byte)((hex >> 8) & 0xFF),
                    (byte)(hex & 0xFF));
        }
    }


    [PluginConfig(pluginName: nameof(WeightAnalyze), creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class WeightAnalyzeFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool weightAnalyze = true; // 开关
        public static bool curWeight = true; // 开关

        // 第一个key是 filtertype （不是itemtype），第二个是subtype
        private static Dictionary<int, TypeWeight> inventoryWeights = new Dictionary<int, TypeWeight>();
        private static Dictionary<int, TypeWeight> warehouseWeights = new Dictionary<int, TypeWeight>();
        private static Dictionary<int, TypeWeight> treasuryWeights = new Dictionary<int, TypeWeight>();
        private static Dictionary<int, TypeWeight> stockWeights = new Dictionary<int, TypeWeight>();

#if taiwuNormal
        private static string ToLoc(ushort languageKey)
        {
            return LocalStringManager.Get((ushort)languageKey);
        }
#else
        private static string ToLoc(LanguageKey languageKey)
        {
            return LocalStringManager.Get(languageKey);
        }
#endif

        /*
         * LanguageKey -> 中文映射（保留备查，如需恢复本地化调用可用 ToLoc(LanguageKey.XXX) 替换）
         * LK_Equip_Slot_Name_Short_Weapon   => "兵"
         * LK_Equip_Slot_Name_Short_Torso    => "甲"
         * LK_Item_Filter_SubType_Material_Food     => "食"
         * LK_Item_Filter_SubType_Material_Wood     => "木"
         * LK_Item_Filter_SubType_Material_Metal    => "铁"
         * LK_Item_Filter_SubType_Material_Jade     => "玉"
         * LK_Item_Filter_SubType_Material_Fabric   => "织"
         * LK_Item_Filter_SubType_Material_Medicine => "药"
         * LK_Item_Filter_SubType_Food_Vegetarian   => "素"
         * LK_Item_Filter_SubType_Food_Meat  => "荤"
         * LK_Item_Filter_SubType_Food_Tea   => "茶"
         * LK_Item_Filter_SubType_Food_Wine  => "酒"
         * LK_Item_Filter_SubType_Book_LifeSkill    => "技"
         * LK_Item_Filter_SubType_Book_CombatSkill  => "武"
         * LK_ItemSubType_1100 => "促织"
         * LK_ItemSubType_1200 => "杂物"
         * LK_ItemSubType_1205 => "心材"
         * LK_ItemSubType_1206 => "绳索"
         * LK_Item_Filter_Type_Food   => "食"
         * LK_Item_Filter_Type_Medicine       => "药"
         * LK_Item_Filter_Type_Equipment      => "装"
         * LK_Item_Filter_Type_Book    => "书"
         * LK_Item_Filter_Type_Make    => "制"
         * LK_Item_Filter_Type_Material       => "材"
         * LK_Item_Filter_Type_Other   => "它"
         * LK_Inventory => "行囊"
         * LK_Warehouse => "私库"
         * LK_Treasury  => "公库"
         * LK_StockStorageGoodsShelf  => "货架"
         * LK_Trough    => "饲槽"
         */
        private static SortedDictionary<int, string> equipFilterName = new SortedDictionary<int, string>()
        {
            { 1, "兵" },
            { 3, "甲" },
        };
        private static SortedDictionary<int, string> resFilterName = new SortedDictionary<int, string>()
        {
            { 1, "食" },
            { 2, "木" },
            { 3, "铁" },
            { 4, "玉" },
            { 5, "织" },
            { 6, "药" },
        };

        private static SortedDictionary<int, string> foodTypeName = new SortedDictionary<int, string>()
        { 
            // 菜
            { 700, "素" },
            { 701, "荤" },
            { 900, "茶" },
            { 901, "酒" },
        };
        private static SortedDictionary<int, string> bookTypeName = new SortedDictionary<int, string>()
        { 
            // 书
            { 1000, "技" },
            { 1001, "武" },
        };

        private static SortedDictionary<int, string> miscTypeName = new SortedDictionary<int, string>()
        {
            // 其他 心
            { 1100, "促织" },
            { 1200, "杂物" },
            { 1205, "心材" },
            { 1206, "绳索" },
        };

        private static SortedDictionary<int, (SortedDictionary<int, string> sub, string name)> filterName = new SortedDictionary<int, (SortedDictionary<int, string> sub, string name)>()
        {
            { 1, (foodTypeName, "食") }, // itemtype 7 9
            { 2, (null, "药") }, // itemtype 8
            { 3, (equipFilterName, "装") }, // itemtype 01234
            { 4, (bookTypeName, "书") },  // itemtype 10
            { 5, (null, "制") }, //  itemtype 6
            { 6, (resFilterName, "材") },  // itemtype 5
            { 7, (miscTypeName, "它") },  // itemtype 大于10 其他 12
        };
        private static List<int> filterSort = new List<int>() { 1, 2, 3, 4, 6, 5, 7 };

        private static TextMeshProUGUI warehouseText;
        private static TextMeshProUGUI inventoryText;
        private static TextMeshProUGUI treasuryText;
        private static TextMeshProUGUI stockText;

		private class TypeWeight
        {
            public Dictionary<int, int> subs = new Dictionary<int, int>();
            public int weight = 0;
        }

        public override void Initialize()
        {
            MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(WeightAnalyzeFrontendPlugin));
        }

        public override void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
        }

        public override void OnModSettingUpdate()
        {
            ModManager.GetSetting(ModIdStr, "weightAnalyze", ref weightAnalyze);
            ModManager.GetSetting(ModIdStr, "curWeight", ref curWeight);
            MyUtils.MyLog($"setting {weightAnalyze},{curWeight}");
        }

        private static void TryInit(Dictionary<int, TypeWeight> weights)
        {
            foreach(var (filter,name) in filterName)
            {
                if(!weights.ContainsKey(filter))
                    weights[filter] = new TypeWeight();
                else
                    weights[filter].weight = 0;
                if (filterName[filter].sub != null)
                {
                    
                    InitSub(weights[filter], filterName[filter].sub);
                }
            }
        }

        private static void InitSub(TypeWeight typeWeight, SortedDictionary<int, string> subs)
        {
            foreach (var (sub, _) in subs)
            {
                typeWeight.subs[sub] = 0;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ViewWarehouse), "OnInit")]
        public static void ViewWarehouse_OnInit(ViewWarehouse __instance)
        {
			MyUtils.MyLog("ViewWarehouse OnInit");
			// 已经初始化过了
			if(__instance.transform.Find("wa_warehouse") == null)
			{
				CreateTipsText(__instance.RectTransform, "wa_warehouse", ref warehouseText);
				warehouseText.rectTransform.anchoredPosition = new Vector2(-500f, 600f);

				CreateTipsText(__instance.RectTransform, "wa_treasury", ref treasuryText);
				treasuryText.rectTransform.anchoredPosition = new Vector2(-900f, 600f);

				CreateTipsText(__instance.RectTransform, "wa_stock", ref stockText);
				stockText.rectTransform.anchoredPosition = new Vector2(100f, 600f);

				CreateTipsText(__instance.RectTransform, "wa_inventory", ref inventoryText);
				inventoryText.rectTransform.anchoredPosition = new Vector2(500f, 600f);

				warehouseText.gameObject.SetActive(false);
				treasuryText.gameObject.SetActive(false);
				stockText.gameObject.SetActive(false);
				inventoryText.gameObject.SetActive(false);
			}
			else
			{
				warehouseText.gameObject.SetActive(false);
				treasuryText.gameObject.SetActive(false);
				stockText.gameObject.SetActive(false);
				inventoryText.gameObject.SetActive(false);
			}
				
				
			var needBtnTran = Traverse.Create(__instance).Field("openItemTake")?.GetValue<CButton>()?.transform;
			// if (needBtnTran == null)
			// {
			// 	MyUtils.MyLog("ViewWarehouse openItemTake nofind");
			// 	needBtnTran = __instance.transform.Find("NeedItem");
			// }
			// if(needBtnTran == null)
			// {
			// 	MyUtils.MyLog("ViewWarehouse NeedItem nofind");
			// 	needBtnTran = __instance.transform.GetChild(2).GetChild(1);
			// }
			if(!needBtnTran) return;


			var btnTran = needBtnTran.transform.parent.Find("weightBtn");
			if (btnTran == null)
			{
				var weightBtnObj = GameObject.Instantiate(needBtnTran.gameObject, needBtnTran.parent);
				weightBtnObj.name = "weightBtn";
				var pos = weightBtnObj.transform.localPosition;
				pos.x = 570; pos.y = 215;
				weightBtnObj.transform.localPosition = pos;

				var tl = weightBtnObj.GetComponentInChildren<TextLanguage>(true);
				tl.enabled = false;
				tl.Key = "";
				GameObject.DestroyImmediate(tl);

				// weightBtnObj.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "重量分析1";
				weightBtnObj.GetComponentInChildren<TextMeshProUGUI>(true).text = "重量分析";
				weightBtnObj.GetComponent<Button>().onClick.RemoveAllListeners();
				weightBtnObj.GetComponent<Button>().onClick.AddListener(OnBtnClick);

				btnTran = weightBtnObj.transform;
			}

			if(btnTran)
			{
				btnTran.gameObject.SetActive(weightAnalyze);
			}

		}
		private static void OnBtnClick()
		{
			MyUtils.MyLog("点击按钮");
			warehouseText.gameObject.SetActive(!warehouseText.gameObject.activeSelf);
			treasuryText.gameObject.SetActive(!treasuryText.gameObject.activeSelf);
			stockText.gameObject.SetActive(!stockText.gameObject.activeSelf);
			inventoryText.gameObject.SetActive(!inventoryText.gameObject.activeSelf);
		}


		private static void OnWareItemListChange()
        {
            // var ui = UIElement.Warehouse.UiBaseAs<ViewWarehouse>();
            // UpdateWareLoad(ui);
            // if (!weightAnalyze)
            // {
            //     warehouseText.gameObject.SetActive(false);
            //     treasuryText.gameObject.SetActive(false);
            //     stockText.gameObject.SetActive(false);
            //     return;
            // }
            // var _warehouseScroll = Traverse.Create(ui).Field("_warehouseScroll").GetValue<GroupedItemScrollView>();
            // if(_warehouseScroll.MySortAndFilter.SortFilterSetting.SortTypes.Contains(ItemSortAndFilter.SortType.Weight))
            // {
            //     warehouseText.gameObject.SetActive(true);
            //     treasuryText.gameObject.SetActive(true);
            //     stockText.gameObject.SetActive(true);
            // }
            // else
            // {
            //     warehouseText.gameObject.SetActive(false);
            //     treasuryText.gameObject.SetActive(false);
            //     stockText.gameObject.SetActive(false);
            // }
        }

        private static void OnInventoryItemListChange()
        {
            // var ui = UIElement.Warehouse.UiBaseAs<ViewWarehouse>();
            // UpdateInventoryLoad(ui);
            // if (!weightAnalyze)
            // {
            //     inventoryText.gameObject.SetActive(false);
            //     return;
            // }

            // var _inventoryScroll = Traverse.Create(ui).Field("_inventoryScroll").GetValue<ItemScrollView>();
            // if (_inventoryScroll.MySortAndFilter.SortFilterSetting.SortTypes.Contains(ItemSortAndFilter.SortType.Weight))
            // {
            //     inventoryText.gameObject.SetActive(true);
            // }
            // else
            // {
            //     inventoryText.gameObject.SetActive(false);
            // }
        }

        public static void CreateTipsText(Transform parent, string name, ref TextMeshProUGUI text)
        {
			var textObj = new GameObject(name, new[] { typeof(RectTransform), });
			textObj.transform.SetParent(parent);
			var r = textObj.GetComponent<RectTransform>();
			r.sizeDelta = new Vector2(450f, 300f);
			r.localScale = Vector3.one;

			text = textObj.AddComponent<TextMeshProUGUI>();
			text.raycastTarget = false;
			text.fontSize = 22f;
			text.alignment = TextAlignmentOptions.Center;
			// 从游戏现有 UI 文本中获取正确的字体（LiberationSans 在游戏中不存在）
			// 使用 parent 所在 Canvas 下的文本来避免 DontDestroyOnLoad 场景问题
			var canvas = parent.GetComponentInParent<Canvas>(true);
			TextMeshProUGUI refText = null;
			if (canvas != null)
			{
				refText = canvas.GetComponentInChildren<TextMeshProUGUI>(true);
			}
			if (refText != null && refText.font != null)
			{
				text.font = refText.font;
				// fontMaterial 的 getter 内部会复制材质，可能抛出异常，只设置 font 即可
			}

			text.color = new Color(0.9725f,0.902f,0.7569f,1);
        }

        private static void UpdateInventoryLoad(ViewWarehouse __instance)
        {
            var container = Traverse.Create(__instance).Field("exchangeContainer").GetValue<ExchangeContainer>();
            if (container == null || container.selfValue1 == null || container.selfItemList == null) return;

            if (!curWeight)
            {
				return;
            }
            else
            {
                var filteredList = container.selfItemList.FilteredData;
                if (filteredList == null) return;
                var sum = filteredList.Cast<ItemDisplayData>().Sum(item => item.Amount * item.Weight);
                var origin = container.selfValue1.text;
				if(origin.Contains('\n')) // 更新
				{
					var text = container.selfValue1.text; // 保留\n后面的字符, 前面更新
					var parts = text.Split('\n');
					container.selfValue1.text = $"<size=22>        {sum / 100f:f1}\n{parts[^1]}";
				}
				else // 第一次添加
				{
					container.selfValue1.text = $"<size=22>        {sum / 100f:f1}\n{origin}</size>"; // 8个空格让上面的居中
				}
				MyUtils.MyLog($"UpdateInventoryLoad {origin} -> {container.selfValue1.text}");

			}
        }

        private static void UpdateWareLoad(ViewWarehouse __instance)
        {
            var container = Traverse.Create(__instance).Field("exchangeContainer").GetValue<ExchangeContainer>();
            if (container == null || container.targetValue1 == null) return;

            if (!curWeight)
            {
				return;
			}
            else
            {
                var filteredList = container.targetItemList.FilteredData;
                if (filteredList == null) return;
                var sum = filteredList.Cast<ItemDisplayData>().Sum(item => item.Amount * item.Weight);
                var origin = container.targetValue1.text;
				if (origin.Contains('\n')) // 更新
				{
					var text = container.targetValue1.text; // 保留\n后面的字符, 前面更新
					var parts = text.Split('\n');
					container.targetValue1.text = $"<size=22>        {sum / 100f:f1}\n{parts[^1]}";
				}
				else // 第一次添加
				{
					container.targetValue1.text = $"<size=22>        {sum / 100f:f1}\n{origin}</size>"; // 8个空格让上面的居中
				}
				MyUtils.MyLog($"UpdateInventoryLoad {origin} -> {container.selfValue1.text}");
			}
		}

        private static int SumWeight(List<ItemDisplayData> items)
        {
            return items.Sum(item=>item.Amount *  item.Weight);
        }

		[HarmonyPostfix, HarmonyPatch(typeof(ItemListScroll), "SetItemList", argumentTypes: new Type[1] { typeof(IReadOnlyList<ITradeableContent>) })]
		public static void ItemListScroll_SetItemList(ItemListScroll __instance)
		{
			// MyUtils.MyLog("ItemListScroll_SetItemList");
			if(UIElement.Warehouse.Exist)
			{
				var wh = __instance.GetComponentInParent<ViewWarehouse>();
				// MyUtils.MyLog("ItemListScroll_SetItemList  111");
				if (wh)
				{
					// MyUtils.MyLog("ItemListScroll_SetItemList  222");
					var container = Traverse.Create(wh).Field("exchangeContainer").GetValue<ExchangeContainer>();
					var _warehouseScroll = Traverse.Create(container).Field("targetItemList").GetValue<ItemListScroll>();
					var _inventoryScroll = Traverse.Create(container).Field("selfItemList").GetValue<ItemListScroll>();
					if(__instance == _inventoryScroll)
					{
						// var datalist = Traverse.Create(_inventoryScroll).Property("DataList").GetValue<IReadOnlyList<object>>();
						// MyUtils.MyLog($"ItemListScroll_SetItemList  333 self  datalist {datalist?.Count}");
						UpdateInventoryLoad(wh);
					}
					else if (__instance == _warehouseScroll)
					{
						// var datalist = Traverse.Create(_warehouseScroll).Property("DataList").GetValue<IReadOnlyList<object>>();
						// MyUtils.MyLog($"ItemListScroll_SetItemList  444 ware  datalist {datalist?.Count}");
						UpdateWareLoad(wh);
					}
				}
			}
		}


		[HarmonyPostfix, HarmonyPatch(typeof(ViewExchangeBase), "OnSelfSortAndFilterChangedCallback")]
		public static void ViewWarehouse_OnSelfSortAndFilterChangedCallback(ViewExchangeBase __instance)
		{
			// MyUtils.MyLog("OnSelfSortAndFilterChangedCallback");
			if(__instance is ViewWarehouse wh)
			{
				UpdateInventoryLoad(wh);
				// MyUtils.MyLog("OnSelfSortAndFilterChangedCallback  wh");
			}
		}

		[HarmonyPostfix, HarmonyPatch(typeof(ViewExchangeBase), "OnTargetSortAndFilterChangedCallback")]
		public static void ViewWarehouse_OnTargetSortAndFilterChangedCallback(ViewExchangeBase __instance)
		{
			// MyUtils.MyLog("OnTargetSortAndFilterChangedCallback");
			if (__instance is ViewWarehouse wh)
			{
				UpdateWareLoad(wh);
				// MyUtils.MyLog("OnTargetSortAndFilterChangedCallback  wh");
			}
		}

		[HarmonyPostfix, HarmonyPatch(typeof(ViewWarehouse), "RefreshValues")]
        public static void ViewWarehouse_RefreshValues(ViewWarehouse __instance)
        {
            if (__instance == null) return;
            if (!weightAnalyze) return;


			var cache = Traverse.Create(__instance).Field("_cache").GetValue();
			if (cache == null) return;

			// 获取各物品列表
			var cacheTrav = Traverse.Create(cache);

			// 获取各物品列表 (因为多了一层 exchange列表，所以这里不会立即更新，切换仓库类型才会更新)
			var invItems = cacheTrav.Field("TaiwuInventoryItemDisplayDataList").GetValue<List<ItemDisplayData>>();
			var wareItems = cacheTrav.Field("TaiwuWarehouseItemDisplayDataList").GetValue<List<ItemDisplayData>>();
			var treasuryItems = cacheTrav.Field("TaiwuTreasuryItemDisplayDataList").GetValue<List<ItemDisplayData>>();
			var stockItems = cacheTrav.Field("TaiwuStockItemDisplayDataList").GetValue<List<ItemDisplayData>>();
			var troughItems = cacheTrav.Field("TaiwuTroughItemDisplayDataList").GetValue<List<ItemDisplayData>>();

			// MyUtils.MyLog($"RefreshValues invItems {invItems.Count}");
			// MyUtils.MyLog($"RefreshValues wareItems {wareItems.Count}");
			// MyUtils.MyLog($"RefreshValues treasuryItems {treasuryItems.Count}");
			// MyUtils.MyLog($"RefreshValues stockItems {stockItems.Count}");
			// MyUtils.MyLog($"RefreshValues troughItems {troughItems?.Count}");

			// 更新背包文本
			if (inventoryText != null)
            {
                if (invItems != null)
                    inventoryText.text = UpdateSource("行囊", inventoryWeights, invItems);
                else
                    inventoryText.text = "";
            }

            // 更新仓库文本
            if (warehouseText != null)
            {
                if (wareItems != null)
                    warehouseText.text = UpdateSource("私库", warehouseWeights, wareItems);
                else
                    warehouseText.text = "";
            }

            // 更新公库文本
            if (treasuryText != null)
            {
                if (treasuryItems != null)
                    treasuryText.text = UpdateSource("公库", treasuryWeights, treasuryItems);
                else
                    treasuryText.text = "";
            }

            // 更新货仓/食槽文本
            if (stockText != null)
            {
                if (stockItems != null)
                    stockText.text = UpdateSource("货架", stockWeights, stockItems);
                else if (troughItems != null)
                    stockText.text = UpdateSource("饲槽", stockWeights, troughItems);
                else
                    stockText.text = "";
            }

            // 更新筛选栏位重量
            UpdateInventoryLoad(__instance);
            UpdateWareLoad(__instance);

        }

        private static string UpdateSource(string name, Dictionary<int, TypeWeight> weights, List<ItemDisplayData> allItems)
        {
            TryInit(weights);
            var sum = ItemSum(weights, allItems);
            return $"{name} {sum / 100f:f0}\n{FormatWeight(weights, sum)}";
        }

        private static int ItemSum(Dictionary<int, TypeWeight> weights, List<ItemDisplayData> allItems)
        {
            int sum = 0;
            foreach (var item in allItems)
            {
                var filter = (int)ItemSortAndFilter.GetFilterType(item.Key.ItemType);
                if (!weights.ContainsKey(filter))
                    continue;
                var weight = item.Amount * item.Weight;
                weights[filter].weight += weight;
                sum += weight;
                DealSub(weights, filter, item, weight);
            }
            return sum;
        }

        private static void DealSub(Dictionary<int, TypeWeight> weights, int filter, ItemDisplayData item, int weight)
        {
            var filterType = (ItemSortAndFilter.ItemFilterType)filter;
            int sub = -1;
            if(filterType == ItemSortAndFilter.ItemFilterType.Equip)
            {
                sub = (int)ItemSortAndFilter.GetEquipFilterType(item.Key);
            }
            else if(filterType == ItemSortAndFilter.ItemFilterType.Material)
                sub = (int)ItemSortAndFilter.GetMaterialFilterType(item.Key);
            else
            {
                sub = ItemTemplateHelper.GetItemSubType(item.Key.ItemType, item.Key.TemplateId);
            }

            var subWeights = weights[filter].subs;
            if (!subWeights.ContainsKey(sub))
                return;

            subWeights[sub] += weight;
        }

        private static string SetLoadStr(int cur, int max, out int level)
        {
            var text = $"{cur / 100f:f0}";
            if (max == 0) { level = 0; return text; }
            if (cur >= max / 3 * 2)
            {
                level = 2;
                return text.SetGradeColor(7);
            }
            else if (cur >= max / 3 * 1)
            {
                level = 1;
                return text.SetGradeColor(4);
            }
            else
            {
                level = 0;
                return text;
            }
        }

        private static string FormatWeight(Dictionary<int, TypeWeight> weights, int sum)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach(var filter in filterSort)
            {
                var filterConfig = filterName[filter];
                //MyUtils.MyLog($"FormatWeight {weights},{filter}, {weights[filter]}");
                var filterWeight = weights[filter].weight;
                var filterWeightStr = SetLoadStr(filterWeight, sum, out var level);
                stringBuilder.Append($"{filterConfig.name}:{filterWeightStr}");
                if (filterWeight != 0)
                {
                    if (filterConfig.sub != null) // 需要显示子类型的重量
                    {
                        foreach (var (subType, subName) in filterConfig.sub)
                        {
                            var subWeight = weights[filter].subs[subType];
                            string subWeightStr;
                            if (level > 0) // 大类占比高的才上色
                                subWeightStr = SetLoadStr(subWeight, filterWeight, out _);
                            else
                                subWeightStr = $"{subWeight / 100f:f0}";
                            stringBuilder.Append($" {subName}:{subWeightStr}");
                        }
                    }
                }

                if (filter == 1 || filter == 3 || filter == 5)
                {
                    stringBuilder.Append($" | ".SetGradeColor(6));
                }
                else if (filter == 2 || filter == 4 || filter == 6)
                {
                    stringBuilder.Append($"\n");
                }
                else { }
            }
            return stringBuilder.ToString();
        }
    }
}
