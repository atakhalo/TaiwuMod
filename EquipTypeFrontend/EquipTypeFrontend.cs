using CharacterDataMonitor;
using Config;
using FrameWork;
using FrameWork.ModSystem;
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
using UICommon.Character.Avatar;
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
            Game.Instance.StartCoroutine(DelayCoroutine(action, delay, real));
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

        private static SortedDictionary<int, string> equipFilterName = new SortedDictionary<int, string>()
        {
            { 1, LocalStringManager.Get(LanguageKey.LK_Equip_Slot_Name_Short_Weapon) },
            { 3, LocalStringManager.Get(LanguageKey.LK_Equip_Slot_Name_Short_Torso) },
        };
        private static SortedDictionary<int, string> resFilterName = new SortedDictionary<int, string>()
        {
            { 1, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Material_Food) },
            { 2, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Material_Wood) },
            { 3, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Material_Metal) },
            { 4, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Material_Jade) },
            { 5, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Material_Fabric) },
            { 6, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Material_Medicine) },
        };

        private static SortedDictionary<int, string> foodTypeName = new SortedDictionary<int, string>()
        { 
            // 菜
            { 700, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Food_Vegetarian) },
            { 701, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Food_Meat) },
            { 900, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Food_Tea) },
            { 901, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Food_Wine) },
        };
        private static SortedDictionary<int, string> bookTypeName = new SortedDictionary<int, string>()
        { 
            // 书
            { 1000, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Book_LifeSkill) },
            { 1001, LocalStringManager.Get(LanguageKey.LK_Item_Filter_SubType_Book_CombatSkill) },
        };

        private static SortedDictionary<int, string> miscTypeName = new SortedDictionary<int, string>()
        {
            // 其他 心
            { 1100, LocalStringManager.Get(LanguageKey.LK_ItemSubType_1100) },
            { 1200, LocalStringManager.Get(LanguageKey.LK_ItemSubType_1200) },
            { 1205, LocalStringManager.Get(LanguageKey.LK_ItemSubType_1205) },
            { 1206, LocalStringManager.Get(LanguageKey.LK_ItemSubType_1206) },
        };

        private static SortedDictionary<int, (SortedDictionary<int, string> sub, string name)> filterName = new SortedDictionary<int, (SortedDictionary<int, string> sub, string name)>()
        {
            { 1, (foodTypeName, LocalStringManager.Get(LanguageKey.LK_Item_Filter_Type_Food)) }, // itemtype 7 9
            { 2, (null, LocalStringManager.Get(LanguageKey.LK_Item_Filter_Type_Medicine)) }, // itemtype 8
            { 3, (equipFilterName, LocalStringManager.Get(LanguageKey.LK_Item_Filter_Type_Equipment)) }, // itemtype 01234
            { 4, (bookTypeName, LocalStringManager.Get(LanguageKey.LK_Item_Filter_Type_Book)) },  // itemtype 10
            { 5, (null, LocalStringManager.Get(LanguageKey.LK_Item_Filter_Type_Make)) }, //  itemtype 6
            { 6, (resFilterName, LocalStringManager.Get(LanguageKey.LK_Item_Filter_Type_Material)) },  // itemtype 5
            { 7, (miscTypeName, LocalStringManager.Get(LanguageKey.LK_Item_Filter_Type_Other)) },  // itemtype 大于10 其他 12
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

        [HarmonyPostfix, HarmonyPatch(typeof(UI_Warehouse), "Awake")]
        public static void UI_Warehouse_Awake(UI_Warehouse __instance)
        {
            CreateTipsText(__instance.RectTransform, "wa_warehouse", ref warehouseText);
            warehouseText.rectTransform.anchoredPosition = new Vector2(-500f, 580f);

            CreateTipsText(__instance.RectTransform, "wa_treasury", ref treasuryText);
            treasuryText.rectTransform.anchoredPosition = new Vector2(-900f, 580f);

            CreateTipsText(__instance.RectTransform, "wa_stock", ref stockText);
            stockText.rectTransform.anchoredPosition = new Vector2(-100f, 580f);

            CreateTipsText(__instance.RectTransform, "wa_inventory", ref inventoryText);
            inventoryText.rectTransform.anchoredPosition = new Vector2(600f, 580f);

            var _warehouseScroll = Traverse.Create(__instance).Field("_warehouseScroll").GetValue<GroupedItemScrollView>();
            _warehouseScroll.ItemListChangedAction += OnWareItemListChange;

            var _inventoryScroll = Traverse.Create(__instance).Field("_inventoryScroll").GetValue<ItemScrollView>();
            _inventoryScroll.ItemListChangedAction += OnInventoryItemListChange;
        }

        private static void OnWareItemListChange()
        {
            var ui = UIElement.Warehouse.UiBaseAs<UI_Warehouse>();
            UpdateWareLoad(ui);
            if (!weightAnalyze)
            {
                warehouseText.gameObject.SetActive(false);
                treasuryText.gameObject.SetActive(false);
                stockText.gameObject.SetActive(false);
                return;
            }
            var _warehouseScroll = Traverse.Create(ui).Field("_warehouseScroll").GetValue<GroupedItemScrollView>();
            if(_warehouseScroll.MySortAndFilter.SortFilterSetting.SortTypes.Contains(ItemSortAndFilter.SortType.Weight))
            {
                warehouseText.gameObject.SetActive(true);
                treasuryText.gameObject.SetActive(true);
                stockText.gameObject.SetActive(true);
            }
            else
            {
                warehouseText.gameObject.SetActive(false);
                treasuryText.gameObject.SetActive(false);
                stockText.gameObject.SetActive(false);
            }
        }

        private static void OnInventoryItemListChange()
        {
            var ui = UIElement.Warehouse.UiBaseAs<UI_Warehouse>();
            UpdateInventoryLoad(ui);
            if (!weightAnalyze)
            {
                inventoryText.gameObject.SetActive(false);
                return;
            }

            var _inventoryScroll = Traverse.Create(ui).Field("_inventoryScroll").GetValue<ItemScrollView>();
            if (_inventoryScroll.MySortAndFilter.SortFilterSetting.SortTypes.Contains(ItemSortAndFilter.SortType.Weight))
            {
                inventoryText.gameObject.SetActive(true);
            }
            else
            {
                inventoryText.gameObject.SetActive(false);
            }
        }

        public static void CreateTipsText(Transform parent, string name, ref TextMeshProUGUI text)
        {
            text = GameObjectCreationUtils.UGUICreateTMPText(parent, new Vector2(0.5f, 0.5f), new Vector2(450f, 300f), 22f, "");
            text.color = new Color(0.9725f,0.902f,0.7569f,1);
            text.name = name;
        }

        private static void UpdateInventoryLoad(UI_Warehouse __instance)
        {
            var _inventoryScroll = Traverse.Create(__instance).Field("_inventoryScroll").GetValue<ItemScrollView>();
            var sp = LocalStringManager.Get(LanguageKey.LK_Colon_Symbol);
            if (!curWeight)
            {
                var origin = __instance.CGet<TextMeshProUGUI>("InventoryLoadTips").text;
                var s = origin.Split(sp);
                MyUtils.MyLog($"UpdateInventoryLoad {s[0]}");
                __instance.CGet<TextMeshProUGUI>("InventoryLoadTips").text = $"{s[0]}{sp}";
            }
            else
            {
                var sum = SumWeight(_inventoryScroll.MySortAndFilter.OutputItemList);
                var origin = __instance.CGet<TextMeshProUGUI>("InventoryLoadTips").text;
                var s = origin.Split(sp);
                __instance.CGet<TextMeshProUGUI>("InventoryLoadTips").text = $"{s[0]}{sp}{sum / 100f:f1}/";
            }
        }

        private static void UpdateWareLoad(UI_Warehouse __instance)
        {
            var _warehouseScroll = Traverse.Create(__instance).Field("_warehouseScroll").GetValue<GroupedItemScrollView>();
            var sp = LocalStringManager.Get(LanguageKey.LK_Colon_Symbol);
            if (!curWeight)
            {
                var origin = __instance.CGet<TextMeshProUGUI>("WarehouseLoadTips").text;
                var s = origin.Split(sp);
                __instance.CGet<TextMeshProUGUI>("WarehouseLoadTips").text = $"{s[0]}{sp}";
            }
            else
            {
                var sum = SumWeight(_warehouseScroll.MySortAndFilter.OutputItemList);
                var origin = __instance.CGet<TextMeshProUGUI>("WarehouseLoadTips").text;
                var s = origin.Split(sp);
                //MyUtils.MyLog($"UpdateWareLoad {s.Length}");
                __instance.CGet<TextMeshProUGUI>("WarehouseLoadTips").text = $"{s[0]}{sp}{sum/100f:f1}/";
            }
        }

        private static int SumWeight(List<ItemDisplayData> items)
        {
            return items.Sum(item=>item.Amount *  item.Weight);
        }


        [HarmonyPostfix, HarmonyPatch(typeof(UI_Warehouse), "RefreshInventoryItems")]
        public static void RefreshInventoryItems(UI_Warehouse __instance)
        {
            if (!UIElement.Warehouse.Exist) return;
            if (inventoryText == null) return;
            if (!weightAnalyze) return;

            var _itemDict = Traverse.Create(__instance).Field("_itemDict").GetValue<Dictionary<ItemSourceType, List<ItemDisplayData>>>();
            var bagStr = UpdateSource(LocalStringManager.Get(LanguageKey.LK_Inventory), inventoryWeights, _itemDict[ItemSourceType.Inventory]);
            inventoryText.text = bagStr;
            //MyUtils.MyLog(bagStr);
            //UpdateInventoryLoad(__instance);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UI_Warehouse), "RefreshWarehouseItems")]
        public static void RefreshWarehouseItems(UI_Warehouse __instance)
        {
            if (!UIElement.Warehouse.Exist) return;
            if (warehouseText == null) return;
            if (!weightAnalyze) return;

            var _itemDict = Traverse.Create(__instance).Field("_itemDict").GetValue<Dictionary<ItemSourceType, List<ItemDisplayData>>>();
            var wareStr = UpdateSource(LocalStringManager.Get(LanguageKey.LK_Warehouse), warehouseWeights, _itemDict[ItemSourceType.Warehouse]);
            var treasuryStr = UpdateSource(LocalStringManager.Get(LanguageKey.LK_Treasury), treasuryWeights, _itemDict[ItemSourceType.Treasury]);
            var stockStr = UpdateSource(LocalStringManager.Get(LanguageKey.LK_StockStorageGoodsShelf), stockWeights, _itemDict[ItemSourceType.StockStorageGoodsShelf]);
            warehouseText.text = wareStr;
            treasuryText.text = treasuryStr;
            stockText.text = stockStr;
            //MyUtils.MyLog(wareStr);
            //MyUtils.MyLog(treasuryStr);
            //MyUtils.MyLog(stockStr);

            //UpdateWareLoad(__instance);
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
