using CharacterDataMonitor;
using Config;
using FrameWork;
using GameData.DLC.FiveLoong;

//using FrameWork.Linq;
using GameData.Domains.Character;
using GameData.Domains.Character.Display;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Map;
using GameData.Domains.Merchant;
using GameData.Domains.Taiwu;
using GameData.Domains.Taiwu.Display;
using GameData.GameDataBridge;
using GameData.Serializer;
using GameData.Utilities;
using HarmonyLib;
using HarmonyLib.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TaiwuModdingLib.Core.Plugin;
using TaiwuModdingLib.Core.Utils;
using TMPro;
using UICommon.Character;
using UICommon.Character.Avatar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
//using UnityEngine.Windows;
using static GameData.Domains.Item.ItemOperationType;
using static Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics;


namespace Minutiae
{
    public class MyUtils
    {
        public static void MyLog(string log)
        {
            Debug.Log($"[Minutiae] {log}");
        }

        public static void ShowMonoCur(GameObject gameObject)
        {
            ShowMonoHelper(gameObject.transform, 0, gameObject.transform);
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

        public static void ShowMonoOne(Transform transform, int depth, Transform sp)
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
            var str = $"{indent}{transform.gameObject.name} {specialMark} ({monoNames}) {isbtn}";

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
    }


    [PluginConfig(pluginName: "Minutiae", creatorId: "atakhalo", pluginVersion: "2025.11.4.1")]
    public class MinutiaeFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool hideEquiped; // 开关 是否隐藏装备或预设中
        public static bool noLimit; // 开关 是否取消仅相邻驿站限制
        public static bool lockCore; // 开关 限制心材放入 公库
        public static bool showEfficiency; // 开关 显示加成后的阅读速度

        public static bool restFilter; // 开关 空闲筛选

        public static bool selectAllEnable = true; // 开关 全选中
        public static bool selectGrade = false; // 开关 拆解全选非贵重
        public static bool whenSelectAll; // 全选中, 跳过选数量ui
        public static string selectAllText; // 全选文本 替换 处理选项

        public static bool sortZizhi = true; // 开关 资质排序
        public static bool showZizhi = true; // 开关 资质显示

        public static bool quickShop = true; // 开关 快速打开商店界面
        public static bool noLimitShop = false; // 开关 不受距离限制
        public static bool blockShop = true; // 开关 商队页显示商人

        public override void Initialize()
        {
            MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(MinutiaeFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "hideEquiped", ref hideEquiped);
            ModManager.GetSetting(ModIdStr, "noLimit", ref noLimit);
            ModManager.GetSetting(ModIdStr, "lockCore", ref lockCore);
            ModManager.GetSetting(ModIdStr, "showEfficiency", ref showEfficiency);
            ModManager.GetSetting(ModIdStr, "restFilter", ref restFilter);

            ModManager.GetSetting(ModIdStr, "selectAllEnable", ref selectAllEnable);
            ModManager.GetSetting(ModIdStr, "selectGrade", ref selectGrade);

            ModManager.GetSetting(ModIdStr, "sortZizhi", ref sortZizhi);
            ModManager.GetSetting(ModIdStr, "showZizhi", ref showZizhi);

            ModManager.GetSetting(ModIdStr, "quickShop", ref quickShop);
            ModManager.GetSetting(ModIdStr, "noLimitShop", ref noLimitShop);
            ModManager.GetSetting(ModIdStr, "blockShop", ref blockShop);
            MyUtils.MyLog($"setting {hideEquiped}, {noLimit}, {lockCore} {showEfficiency}, " +
                $"{restFilter}, {selectAllEnable}, {sortZizhi}, {showZizhi}");
        }


        #region 装备中
        // 跟筛选mod冲突； 设置低优先级
        [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(ItemSortAndFilter), "UpdateItemList")]
        public static void FilterItemEquiped(ItemSortAndFilter __instance)
        {
            if (hideEquiped)
            {
                if(__instance.OutputItemList.Count > 0)
                {
                    bool toHide = false;

                    if (UIElement.Warehouse.Exist || UIElement.Shop.Exist)
                        toHide = true;
                    if (UI_CharacterMenu.CurSubPage == (UIElement.CharacterMenuItems.UiBase as UI_CharacterMenuItems).Key)
                    {
                        toHide = true;
                    }
                    if (UIElement.ItemMultiplyOperation.Exist)
                    {
                        var ui = UIElement.ItemMultiplyOperation.UiBase as UI_ItemMultiplyOperation;
                        var cToggleGroup = Traverse.Create(ui).Field("_toggleGroup").GetValue<CToggleGroup>();
                        var aTog = cToggleGroup.GetActive();
                        if (aTog.Key != (int)EItemOperationType.Repair)
                        {
                            toHide = true;
                        }
                        else
                        {
                            toHide = false;
                        }
                    }
                    if (toHide)
                    {
                        var oldCount = __instance.OutputItemList.Count;
                        __instance.OutputItemList.RemoveAll((data) =>
                        {
                            return data.UsingType == ItemDisplayData.ItemUsingType.EquipmentPlaned
                            || data.UsingType == ItemDisplayData.ItemUsingType.Equiped;
                        });
                        var _onItemListChanged = Traverse.Create(__instance).Field("_onItemListChanged").GetValue<Action>();
                        _onItemListChanged?.Invoke();
                        var newCount = __instance.OutputItemList.Count;
                        //Debug.Log($"[Minutiae] hideEquiped: count:{newCount}(old: {oldCount})");
                    }
                }
            }
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(ItemSortAndFilter), "OnItemFilterTogChange")]
        public static void FilterItemEquiped2(ItemSortAndFilter __instance)
        {
            FilterItemEquiped(__instance);
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(ItemSortAndFilter), "OnEquipFilterTogChange")]
        public static void FilterItemEquiped3(ItemSortAndFilter __instance)
        {
            FilterItemEquiped(__instance);
        }
        #endregion

        #region 驿站
        [HarmonyPostfix, HarmonyPatch(typeof(TravelUtils), "SetAsFailed")]
        public static void CancelLimit(ref bool __result, DialogCmd dialogCmd, CrossAreaMoveInfo travelInfo, ResourceMonitor resourceMonitor)
        {
            if (noLimit)
            {
                if (__result == true && dialogCmd.Content == LocalStringManager.Get(LanguageKey.UI_Dialog_Unlock_Neighbor_Area_Content))
                {
                    if(travelInfo.AuthorityCost > resourceMonitor.Resources[7])
                    {
                        TravelUtils.SetDialogTravelAuthorityNotEnough(dialogCmd, travelInfo.AuthorityCost, false);
                        __result = true;
                    }
                    else
                    {
                        __result = false;
                    }
                }
            }
        }
        #endregion

        #region 仓库锁
        public static bool IsCore(ItemDisplayData item)
        {
            return ItemTemplateHelper.GetItemSubType(item.Key.ItemType, item.Key.TemplateId) == 1205;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UI_Warehouse), "CheckItemIsLocked")]
        public static void LockItem(UI_Warehouse __instance, ref bool __result, 
            ItemSourceType ____warehouseItemSourceType, 
            ItemDisplayData itemData, bool isInventory)
        {
            if(lockCore)
            {
                if(__result == false)
                {
                    // 背包界面， 心材， 非私库
                    if(isInventory && IsCore(itemData) && ____warehouseItemSourceType != ItemSourceType.Warehouse)
                    {
                        __result = true;
                    }
                }
            }
        }
        [HarmonyPostfix, HarmonyPatch(typeof(UI_Warehouse), "OnRenderInventoryItem")]
        public static void OnRenderLockItem(UI_Warehouse __instance, ItemSourceType ____warehouseItemSourceType, ItemDisplayData itemData, ItemView itemView)
        {
            if (lockCore)
            {
                if (IsCore(itemData) && ____warehouseItemSourceType != ItemSourceType.Warehouse)
                {
                    itemView.SetInteractionStateLockText(LocalStringManager.Get(LanguageKey.LK_Item_Operation_TypeNotMatch));
                }
            }
        }

        #endregion



        #region 读书速度
        // 返回的时候，判断是否tips在请求，是的话 发起计算请求
        [HarmonyPostfix, HarmonyPatch(typeof(TaiwuDomainMethod.AsyncCall), "GetReadingResult")]
        public static void CatchGetResult()
        {
            if(showEfficiency)
            {
                //Debug.Log($"[minutiae] tips send {UIElement.MouseTipReading.Exist}");
                if(UIElement.MouseTipReading.Exist)
                {
                    //Debug.Log($"[minutiae] send req");
                    var tips = UIElement.MouseTipReading.UiBase as MouseTipReading;
                    tips.AsyncMethodCall(5, 2001, ShowReadingResultEx);
                    //GetReadingResultEx(tips, ShowReadingResultEx); // 很奇怪，直接回调了, 再测试
                }
            }
        }

        public static void ShowReadingResultEx(int offset, RawDataPool dataPool)
        {
            if (showEfficiency)
            {
                //Debug.Log($"[minutiae] tips recv {UIElement.MouseTipReading.Exist} ");

                if (UIElement.MouseTipReading.Exist)
                {
                    int[] progress = null;
                    Serializer.Deserialize(dataPool, offset, ref progress);
                    var progressSum = ProgressSum(progress);
                    var sum = progressSum;

                    var sum1 = 0;
                    var ps = $"";
                    for (int i = 0; i < progress.Length; i++)
                    {
                        ps += $"{progress[i]};";
                        sum1 += progress[i];
                    }
                    //Debug.Log($"[Minutiae] : progressSum {progressSum} sum {sum1} speed {ps}");

                    var tips = UIElement.MouseTipReading.UiBase as MouseTipReading;
                    var Text = tips.CGet<TextMeshProUGUI>("ReadingProgressText");
                    var ori = Text.text;
                    var s = ori.Split(' ');

                    ReadAndLoop readAndLoop = Traverse.Create(UIElement.Bottom.UiBase as UI_Bottom).Field("_readAndLoop").GetValue<ReadAndLoop>();
                    int stageIdx = Traverse.Create(readAndLoop).Property("CurrentReadStageIndex").GetValue<int>();
		            short efficiency = GlobalConfig.Instance.ActiveReadProgressAffectedEfficiency[stageIdx];
                    var ea = GlobalConfig.Instance.ActiveReadProgressAffectedEfficiency;
                    //Debug.Log($"[minutiae] {stageIdx}: {ea[0]}, {ea[1]},{ea[2]}");
                    Text.text =  $"{s[0]} ({efficiency}%:<color=#F8E0CAFF>{sum}%</color>)";
                }
            }
        }

        // 对应 MouseTipReading 中 GetReadingResult 的回调, 处理数字总和过大的问题
        public static int ProgressSum(int[] progress)
        {
            var tips = UIElement.MouseTipReading.UiBase as MouseTipReading;
            var _skillBookPageDisplayData = Traverse.Create(tips).Field("_skillBookPageDisplayData").GetValue<SkillBookPageDisplayData>();
            var ReadingProgress = _skillBookPageDisplayData.ReadingProgress;
            int currReadingEfficiency = 0;

            var ps = $"";
            for (int i = 0; i < ReadingProgress.Length; i++)
            {
                ps += $"{ReadingProgress[i]};";
            }
            //Debug.Log($"[Minutiae] : ReadingProgress {ps}");

            for (int i = 0; i < ReadingProgress.Length; i++)
            {
                if (ReadingProgress[i] < 100)
                {
                    //var afterRead = Math.Min(progress[i], 100);
                    //currReadingEfficiency += afterRead - ReadingProgress[i];
                    currReadingEfficiency += Math.Min(progress[i], (int)(100 - ReadingProgress[i]));
                }
                else
                {
                    // 读完的不用加
                    //currReadingEfficiency += 100;
                }
            }
            if (currReadingEfficiency == 0) // 读完后等于0
                return progress.Sum();
            return currReadingEfficiency;
        }

        #endregion

        #region 村子
        #region 建筑选人
        [HarmonyPrefix, HarmonyPatch(typeof(UI_SelectVillagerCharInLineage), "ReadArgs")]
        public static void OnReadArgsPre(UI_SelectVillagerCharInLineage __instance, ref ArgumentBox argsBox)
        {
            if(restFilter)
            {
                // 强制开筛选
                argsBox.SetObject("VillagerFilterType", 
                    EVillagerFilterType.All | EVillagerFilterType.Adult | EVillagerFilterType.Teenager 
                    | EVillagerFilterType.Learning | EVillagerFilterType.FinishLearning
                    | EVillagerFilterType.Farmer);
            }
        }


        [HarmonyPostfix, HarmonyPatch(typeof(UI_SelectVillagerCharInLineage), "ReadArgs")]
        public static void OnReadArgs(UI_SelectVillagerCharInLineage __instance, ArgumentBox argsBox)
        {
            if (restFilter)
            {
                var _villagerFilter = Traverse.Create(__instance).Field("_villagerFilter").GetValue<SelectVillagerFilter>();
                // 添加农民筛选
                var _toggleDic = Traverse.Create(_villagerFilter).Field("_toggleDic").GetValue<Dictionary<EVillagerFilterType, CToggle>>();
                if(_toggleDic.ContainsKey(EVillagerFilterType.Farmer))
                {
                    var toggle = _toggleDic[EVillagerFilterType.Farmer];
                    toggle.GetComponent<Refers>().CGet<TextMeshProUGUI>("Label").text = "闲";
                    //Debug.Log($"[Minutiae] add toggle");
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UI_SelectVillagerCharInLineage), "FilterVillager")]
        public static void FilterRest(UI_SelectVillagerCharInLineage __instance, ref bool __result,
            int charId)
        {
            if (restFilter)
            {
                if(__result == true)
                {
                    var _villagerFilter = Traverse.Create(__instance).Field("_villagerFilter").GetValue<SelectVillagerFilter>();
                    // 把农民改成闲来用
                    if(_villagerFilter.SelectedType.HasFlag(EVillagerFilterType.Farmer))
                    {
                        var _villagerCharacterDisplayDataDict = Traverse.Create(__instance).Field("_villagerCharacterDisplayDataDict").
                            GetValue<Dictionary<int, VillagerRoleCharacterDisplayData>>();
                        var displayData = _villagerCharacterDisplayDataDict[charId];

                        BuildingModel buildingModel = SingletonObject.getInstance<BuildingModel>();
                        VillagerWorkData workData;
                        buildingModel.VillagerWork.TryGetValue(charId, out workData);

                        if( workData != null )
                        {
                            //Debug.Log($"[Minutiae] {charId} {displayData.Name} {workData} {workData.WorkType}");
                            __result = false;
                        }
                        else
                        {
                            __result = true;
                        }
                        return;
                    }
                }
            }
        }
        #endregion


        #region 建筑选人 资质
        [HarmonyPrefix, HarmonyPatch(typeof(UI_SelectVillagerCharInLineage), "SetupVillagerRoleSort")]
        public static bool SetupVillagerRoleSort(UI_SelectVillagerCharInLineage __instance, 
            SimpleAbstractSort abstractSort, Action onSortChanged, string sorterSaveKey)
        {
            if (!sortZizhi) return true;
            abstractSort.Init(new SimpleAbstractSort.Config(new List<SimpleAbstractSort.ItemConfig>
            {
                new SimpleAbstractSort.ItemConfig
                {
                    Id = 0,
                    Text = LocalStringManager.Get(LanguageKey.LK_Character_Sort_Type_Personality)
                },
                new SimpleAbstractSort.ItemConfig
                {
                    Id = 1,
                    Text = LocalStringManager.Get(LanguageKey.LK_Character_Sort_Type_LifeSkillAttainment)
                },
                new SimpleAbstractSort.ItemConfig
                {
                    Id = 2,
                    Text = "质",
                },
            }, onSortChanged, ""));// 不存排序key
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UI_SelectVillagerCharInLineage), "Compare")]
        public static bool UI_SelectVillagerCharInLineage_Compare(UI_SelectVillagerCharInLineage __instance, ref int __result,
            int charIdL, int charIdR)
        {
            if (!sortZizhi) return true;

            SimpleAbstractSort abstractSort = Traverse.Create(__instance).Field("_abstractSort").GetValue<SimpleAbstractSort>();
            bool hasSort = abstractSort != null && abstractSort.IsAnySortActive;
            if(!hasSort)
            {
                return true;
            }
            var villagerCharacterDisplayDataDict = Traverse.Create(__instance).Field("_villagerCharacterDisplayDataDict").GetValue<Dictionary<int, VillagerRoleCharacterDisplayData>>();
            VillagerRoleCharacterDisplayData displayDataL = villagerCharacterDisplayDataDict[charIdL];
            VillagerRoleCharacterDisplayData displayDataR = villagerCharacterDisplayDataDict[charIdR];

            //MyLog($"UI_SelectVillagerCharInLineage_Compare run");
            var villagerDisplayType = Traverse.Create(__instance).Field("_villagerDisplayType").GetValue<UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType>();
            bool noType = villagerDisplayType == UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType.None;
            if (noType) // 没有类型要求时
            {
                //MyLog($"UI_SelectVillagerCharInLineage_Compare noType");

                var roleId = Traverse.Create(__instance).Field("_roleId").GetValue<short>();
                int diff = 0;
                VillagerRoleCompare(__instance, ref diff, displayDataL, displayDataR, roleId, abstractSort);
                if (diff != 0)
                {
                    __result = diff;
                    return false;
                }
                __result = charIdL.CompareTo(charIdR);
                return false;
            }
            else
            {
                //MyLog($"UI_SelectVillagerCharInLineage_Compare Type");

                // readarg的时候按id存了对应的类型（即赋性或战斗或技艺）
                var sortIdDic = Traverse.Create(__instance).Field("sortIdDic").GetValue<Dictionary<int, UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType>>();
                // 三个item，表示需求的赋性、战斗、技艺类型
                var characterPersonalityTypeList = Traverse.Create(__instance).Field("_characterPersonalityTypeList").GetValue< List<sbyte>>();

                foreach (SimpleAbstractSort.Sort sort in abstractSort.Sorts)
                {

                    UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType propType;
                    bool getType;
                    if(sort.Id == 2)
                    {
                        getType = sortIdDic.TryGetValue(1, out propType);
                    }
                    else
                    {
                        getType = sortIdDic.TryGetValue(sort.Id, out propType);
                    }
                    //MyLog($"UI_SelectVillagerCharInLineage_Compare sort {sort.Id} : {propType}");

                    if (getType)
                    {
                        int num = 0;
                        if (propType != UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType.Personality)
                        {
                            if (propType != UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType.LifeSkill)
                            {
                                if (propType != UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType.CombatSkill)
                                {
                                    num = 0;
                                }
                                else
                                {
                                    if (sort.Id == 1) // 造诣
                                    {
                                        var index = characterPersonalityTypeList[1];
                                        var dL = displayDataL.CombatSkillAttainments[index];
                                        var dR = displayDataR.CombatSkillAttainments[index];
                                        num = sort.IsAscending ? dL.CompareTo(dR) : dR.CompareTo(dL);
                                        //MyLog($"战斗造诣 {displayDataL.Name}:{dL} {displayDataR}:{dR} num{num} index {index} {displayDataL.CombatSkillAttainments} ");
                                    }
                                    else if (sort.Id == 2) // 资质
                                    {
                                        var index = characterPersonalityTypeList[1];
                                        var dL = displayDataL.CombatSkillQualifications[index];
                                        var dR = displayDataR.CombatSkillQualifications[index];
                                        num = sort.IsAscending ? dL.CompareTo(dR) : dR.CompareTo(dL);
                                        //MyLog($"战斗资质 {displayDataL.Name}:{dL} {displayDataR}:{dR} num{num} index {index} {displayDataL.CombatSkillQualifications} ");
                                    }
                                }
                            }
                            else
                            {
                                if (sort.Id == 1) // 造诣
                                {
                                    var index = characterPersonalityTypeList[2];
                                    var dL = displayDataL.LifeSkillAttainments[index];
                                    var dR = displayDataR.LifeSkillAttainments[index];
                                    num = sort.IsAscending ? dL.CompareTo(dR) : dR.CompareTo(dL);
                                    //MyLog($"技艺造诣 {displayDataL.Name}:{dL} {displayDataR}:{dR} num{num} index {index} {displayDataL.LifeSkillAttainments} ");
                                }
                                else if (sort.Id == 2) // 资质
                                {
                                    var index = characterPersonalityTypeList[2];
                                    var dL = displayDataL.LifeSkillQualifications[index];
                                    var dR = displayDataR.LifeSkillQualifications[index];
                                    num = sort.IsAscending ? dL.CompareTo(dR) : dR.CompareTo(dL);
                                    //MyLog($"技艺资质 {displayDataL.Name}:{dL} {displayDataR}:{dR} num{num} index {index} {displayDataL.LifeSkillQualifications} ");
                                }
                            }
                        }
                        else
                        {
                            var index = characterPersonalityTypeList[0];
                            var dL = displayDataL.Personalities[index];
                            var dR = displayDataR.Personalities[index];
                            num = sort.IsAscending ? dL.CompareTo(dR) : dR.CompareTo(dL);
                            //MyLog($"赋性 {displayDataL.Name}:{dL} {displayDataR}:{dR} num{num} index {index} {displayDataL.Personalities} ");
                        }
                        int diff2 = num;
                        if (diff2 != 0)
                        {
                            __result = diff2;
                            return false;
                        }
                    }
                    }
                }
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UI_SelectVillagerCharInLineage), "VillagerRoleCompare")]
        public static bool VillagerRoleCompare(UI_SelectVillagerCharInLineage __instance, ref int __result,
            VillagerRoleCharacterDisplayData displayDataL, VillagerRoleCharacterDisplayData displayDataR, short roleId, SimpleAbstractSort sorta)
        {
            if (!sortZizhi) return true;

            //MyLog($"VillagerRoleCompare run");

            ValueTuple<short, short> tupleL = GameData.Domains.Building.SharedMethods.CalcVillagerRoleLifeSkillAndPersonalityType(roleId, displayDataL.LifeSkillAttainments, displayDataL.Personalities);
            ValueTuple<short, short> tupleL2 = GameData.Domains.Building.SharedMethods.CalcVillagerRoleLifeSkillAndPersonalityType(roleId, displayDataL.LifeSkillQualifications, displayDataL.Personalities);
            ValueTuple<short, short> tupleR = GameData.Domains.Building.SharedMethods.CalcVillagerRoleLifeSkillAndPersonalityType(roleId, displayDataR.LifeSkillAttainments, displayDataR.Personalities);
            ValueTuple<short, short> tupleR2 = GameData.Domains.Building.SharedMethods.CalcVillagerRoleLifeSkillAndPersonalityType(roleId, displayDataR.LifeSkillQualifications, displayDataR.Personalities);
            foreach (SimpleAbstractSort.Sort sort in sorta.Sorts)
            {
                int id = sort.Id;
                int diff = 0;
                if (id == 0)
                {
                    diff = (sort.IsAscending ?
                        displayDataL.Personalities[(int)tupleL.Item2].CompareTo(displayDataR.Personalities[(int)tupleR.Item2])
                        : displayDataR.Personalities[(int)tupleR.Item2].CompareTo(displayDataL.Personalities[(int)tupleL.Item2]));
                }
                else if (id == 1)
                {
                    diff = (sort.IsAscending ?
                    displayDataL.LifeSkillAttainments[(int)tupleL.Item1].CompareTo(displayDataR.LifeSkillAttainments[(int)tupleR.Item1])
                        : displayDataR.LifeSkillAttainments[(int)tupleR.Item1].CompareTo(displayDataL.LifeSkillAttainments[(int)tupleL.Item1]));
                }
                else if (id == 2)
                {
                    diff = (sort.IsAscending ?
                    displayDataL.LifeSkillQualifications[(int)tupleL2.Item1].CompareTo(displayDataR.LifeSkillQualifications[(int)tupleR2.Item1])
                        : displayDataR.LifeSkillQualifications[(int)tupleR2.Item1].CompareTo(displayDataL.LifeSkillQualifications[(int)tupleL2.Item1]));
                }
                if (diff != 0)
                {
                    __result = diff;
                    return false;
                }
            }
            return false;
        }
        #endregion

        [HarmonyPrefix, HarmonyPatch(typeof(UI_SelectVillagerCharInLineage), "RefreshCharacterPersonalities")]
        public static bool RefreshCharacterPersonalities(UI_SelectVillagerCharInLineage __instance, 
            List<Refers> personalityList, VillagerRoleCharacterDisplayData displayData,
            UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType ____villagerDisplayType,
            List<sbyte> ____characterPersonalityTypeList)
        {
            if (!showZizhi) return true;

            if (____villagerDisplayType == UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType.None)
            {
                return true;
            }

            int referIndex = 0;
            //sbyte personalityType = ____characterPersonalityTypeList[0];
            //bool flag2 = ____villagerDisplayType.HasFlag(UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType.Personality) && personalityType >= 0;
            //if (flag2)
            //{
            //    Traverse.Create(__instance).Method("SetPropInfoRefer", personalityList[referIndex++], string.Format("mousetip_qiyuan_{0}", personalityType), displayData.Personalities[(int)personalityType].ToString(), false).GetValue();
            //}
            sbyte combatSkillType = ____characterPersonalityTypeList[1];
            bool flag3 = ____villagerDisplayType.HasFlag(UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType.CombatSkill) && combatSkillType >= 0;
            if (flag3)
            {
                Traverse.Create(__instance).Method("SetPropInfoRefer", personalityList[referIndex++], string.Format("mousetip_gongfa_{0}", combatSkillType), displayData.CombatSkillQualifications[(int)combatSkillType].ToString(), false).GetValue();
                Traverse.Create(__instance).Method("SetPropInfoRefer", personalityList[referIndex++], string.Format("mousetip_gongfa_{0}", combatSkillType), displayData.CombatSkillAttainments[(int)combatSkillType].ToString(), false).GetValue();
            }
            sbyte lifeSkillType = ____characterPersonalityTypeList[2];
            bool flag4 = ____villagerDisplayType.HasFlag(UI_SelectVillagerCharInLineage.ESelectVillagerDisplayType.LifeSkill) && referIndex <= 1 && lifeSkillType >= 0;
            if (flag4)
            {
                Traverse.Create(__instance).Method("SetPropInfoRefer", personalityList[referIndex++], string.Format("mousetip_jiyi_{0}", lifeSkillType), displayData.LifeSkillQualifications[(int)lifeSkillType].ToString(), false).GetValue();
                Traverse.Create(__instance).Method("SetPropInfoRefer", personalityList[referIndex++], string.Format("mousetip_jiyi_{0}", lifeSkillType), displayData.LifeSkillAttainments[(int)lifeSkillType].ToString(), false).GetValue();
            }
            while (referIndex <= 1)
            {
                personalityList[referIndex++].gameObject.SetActive(false);
            }
            return false;
        }
        #endregion

        #region 义父所制 (不开，精致后不能再改精致）
        //[HarmonyPostfix, HarmonyPatch(typeof(UI_SetEquipmentEffect), "RefreshEffectData")]
        //public static void RefreshEffectData(UI_SetEquipmentEffect __instance, List<EquipmentEffectItem> ____effectConfigList)
        //{
        //    ____effectConfigList.Add(EquipmentEffect.Instance[54]);
        //}
        #endregion
        #region 全选

        [HarmonyPrefix, HarmonyPatch(typeof(MultiplyItemScrollView), "OpenMultiplyOption")]
        public static bool OpenMultiplyOption(MultiplyItemScrollView __instance, CButton button)
        {
            if (!selectAllEnable) return true;

            if (!UIElement.ItemMultiplyOperation.Exist) return true;

            var ui = UIElement.ItemMultiplyOperation.UiBase as UI_ItemMultiplyOperation;
            var cToggleGroup = Traverse.Create(ui).Field("_toggleGroup").GetValue<CToggleGroup>();
            var aTog = cToggleGroup.GetActive();
            bool toClick = true;
            string content = "";
            bool toSelect = true;
            sbyte grade = -1;
            bool checkTool = aTog.Key == (int)EItemOperationType.Disassemble;
            if (aTog.Key == (int)EItemOperationType.Repair || aTog.Key == (int)EItemOperationType.Disassemble)
            {
                toClick = false;
                var items = __instance.CurMultiplyScrollView.SortAndFilter.OutputItemList;
                if (items.Count > 0)
                {
                    if (__instance.SelectedMultiplyItemOrderedList.Contains(items[0])) // 第一个已经被选
                        toSelect = false;
                    else
                    {
                        if(selectGrade && aTog.Key == (int)EItemOperationType.Disassemble) // 贵重只影响拆解
                        {
                            grade = GetMultiplyGrade(__instance);
                        }
                    }
                }
                if (toSelect)
                {
                    content = "是否全选?\n";
                    if (grade == -1) { }
                    else
                    {
                        // 看 GetItemGradeShortNameWithMoreThan
                        sbyte downGrade = (sbyte)(grade - 1);
                        var gradeText = ItemView.GetGradeText(downGrade).SetColor(Colors.Instance.GradeColors[downGrade]);
                        var gradeStr = gradeText + "·" + CommonUtils.GetItemGradeShortName(downGrade) + "" + LocalStringManager.Get(LanguageKey.LK_Grade_LessThan);
                        content += $"({gradeStr})";
                    }
                }
                else
                {
                    content = "是否全不选?\n(第一个物品被选中，当前功能为全不选)";
                }
            }
            if(toClick)
            {
                content = "是否全选当前显示物品?\n （非修理、拆解界面，只能模拟点击）";
            }
            DialogCmd cmd = new DialogCmd
            {
                Title = "全选",
                Content = content,
                Type = 1, // 1是 两个按钮，2是一个按钮； 4是两个按钮显示字
                Yes = () => SelectAll(__instance, toClick, toSelect, grade, checkTool),
                //No = ()=> MyLog($"lalaala"),
                //GroupYesText = "全选",
                //GroupNoText = "取消"
            };
            UIElement.Dialog.SetOnInitArgs(EasyPool.Get<ArgumentBox>().SetObject("Cmd", cmd));
            UIManager.Instance.ShowUI(UIElement.Dialog);
            return false;
        }

        public static void SelectAll(MultiplyItemScrollView __instance, bool toClick, bool toSelect, int grade, bool checkTool)
        {
            whenSelectAll = true;
            var items = __instance.CurMultiplyScrollView.SortAndFilter.OutputItemList;
            //MyLog($"to SelectAll {items.Count}");
            //MyLog($" before {__instance.SelectedMultiplyItemOrderedList.Count}");

            foreach (ItemDisplayData item in items)
            {
                if (grade != -1 && item.Grade >= grade)
                    continue;
                //if(checkTool)  // 看 MultiplyItemScrollView OnRenderItemMultiply 没有用，一直返回0
                //{
                //    var tool = __instance.GetAvailableToolList(item);
                //    MyUtils.MyLog($"checkTool {item.Grade} {tool?.Count}");
                //    if (tool == null || tool.Count == 0)
                //        continue;
                //}
                if(toClick)
                {
                    var view = __instance.CurMultiplyScrollView.FindItemViewByItem(item.Key);
                    view?.Click();
                    UIElement.SetSelectCount?.UiBaseAs<UI_SetSelectCount>()?.Confirm();
                }
                else
                {
                    if(toSelect)
                    {
                        List<ItemDisplayData> availableToolList = __instance.GetAvailableToolList(item);
                        Traverse.Create(__instance).Method("SetItemSelectCount", item, item.Amount, availableToolList).GetValue<bool>();
                    }
                    else
                    {
                        ArgumentBox args = EasyPool.Get<ArgumentBox>().SetObject("ItemData", item);
                        GEvent.OnEvent(UiEvents.ItemMultiplyOperationCancelSelection, args);
                    }
                }
            }
            //MyLog($" after {__instance.SelectedMultiplyItemOrderedList.Count}");
            whenSelectAll = false;
        }

        public static sbyte GetMultiplyGrade(MultiplyItemScrollView __instance)
        {
            var type = Traverse.Create(__instance).Property("CurItemGradeFilterSourceType").GetValue<ItemGradeFilterSetting.ItemGradeFilterSourceType>();
            var setting = SingletonObject.getInstance<GameSort>().GetItemGradeFilterSetting();
            var grade = setting.GetGrade(type);
            var index = setting.GetIndex(type);
            return grade;
        }

        // 阻止调用 选数ui，直接回调
        [HarmonyPrefix, HarmonyPatch(typeof(ItemScrollView), "SetItemToSelectCountMode")]
        public static bool SetItemToSelectCountMode(ItemScrollView __instance, int index, Action<int> onConfirmSetCount, int limitCount)
        {
            if (!selectAllEnable) return true;

            if (whenSelectAll)
            {
                ItemDisplayData itemData = __instance.MySortAndFilter.OutputItemList[index];
                int maxCount = ((limitCount > 0) ? Mathf.Min(limitCount, itemData.Amount) : itemData.Amount);
                //MyLog($"ItemScrollView SetItemToSelectCountMode");
                onConfirmSetCount?.Invoke(maxCount);
                return false;
            }
            return true;
        }
        // 阻止调用 选数ui，直接回调
        [HarmonyPrefix, HarmonyPatch(typeof(GroupedItemScrollView), "SetItemToSelectCountMode")]
        public static bool SetItemToSelectCountMode(GroupedItemScrollView __instance, int index, Action<int> onConfirmSetCount, int limitCount)
        {
            if (!selectAllEnable) return true;

            if (whenSelectAll)
            {
                ItemDisplayData itemData = __instance.MySortAndFilter.OutputItemList[index];
                int maxCount = ((limitCount > 0) ? Mathf.Min(limitCount, itemData.Amount) : itemData.Amount);
                //MyLog($"GroupedItemScrollView SetItemToSelectCountMode ");
                onConfirmSetCount?.Invoke(maxCount);
                return false;
            }
            return true;
        }

        // 进入多选时改成“全选”
        [HarmonyPostfix, HarmonyPatch(typeof(MultiplyItemScrollView), "EnterMultiplyMode")]
        public static void EnterMultiplyMode(MultiplyItemScrollView __instance)
        {
            if (!selectAllEnable) return;
            var btnList = GetBtnMultiplyOption(__instance);
            if(btnList.Count > 0)
            {
                foreach (var btn in btnList)
                {
                    selectAllText = btn.GetComponentInChildren<TextMeshProUGUI>().text;
                    btn.GetComponentInChildren<TextMeshProUGUI>().text = "全选";
                }
            }
        }
        // 退出多选时改回原名
        [HarmonyPostfix, HarmonyPatch(typeof(MultiplyItemScrollView), "ExitMultiplyMode")]
        public static void ExitMultiplyMode(MultiplyItemScrollView __instance)
        {
            if (!selectAllEnable) return;
            var btnList = GetBtnMultiplyOption(__instance);
            if (btnList.Count > 0)
            {
                foreach (var btn in btnList)
                {
                    btn.GetComponentInChildren<TextMeshProUGUI>().text = selectAllText;
                }
            }
        }

        // 获取“处理选项”按钮
        public static List<CButton> GetBtnMultiplyOption(MultiplyItemScrollView __instance)
        {
            List<CButton> r = new List<CButton>();
            Refers refers = null;
            CButton btnMultiplyOption;
            if (__instance.IsInventoryMultiply) // 这个值在点击批量或贵重按钮的时候才被赋值，其他地方不可用
            {
                refers = __instance.CGet<Refers>("Inventory");
                if (refers != null)
                {
                    if (refers.CTryGet<CButton>("BtnMultiplyOption", out btnMultiplyOption))
                    {
                        r.Add(btnMultiplyOption);
                    }
                }
            }
            else
            {
                refers = __instance.CGet<Refers>("Warehouse");
                if (refers != null)
                {
                    if (refers.CTryGet<CButton>("BtnMultiplyOption", out btnMultiplyOption))
                    {
                        r.Add(btnMultiplyOption);
                    }
                }
            }
            return r;
        }
        #endregion

        #region 人物互动
        #region 打开商店
        // 用于超距离打开商店
        [HarmonyPostfix, HarmonyPatch(typeof(MapBlockCharBase), "RefreshInteraction")]
        public static void MapBlockCharBase_RefreshInteraction(MapBlockCharBase __instance)
        {
            if (!quickShop || !noLimitShop) return;
            if (__instance is MapBlockCharNormal || __instance is MapBlockCharCaravan || __instance is MapBlockCharNormalMerchant)
            {
                var button = Traverse.Create(__instance).Field("button").GetValue<CButton>();
                button.interactable = true;
                //var OnClickButton = AccessTools.Method(typeof(MapBlockCharBase), "OnClickButton");
                //MyUtils.MyLog("add MapBlockCharBase");
                button.ClearAndAddListener(() =>
                {
                    //MyUtils.MyLog("点了 MapBlockCharBase");
                    Traverse.Create(__instance).Method("OnClickButton").GetValue();
                });
            }
        }


        [HarmonyPrefix, HarmonyPatch(typeof(MapBlockCharNormal), "OnClickButton")]
        public static bool MapBlockCharNormal_OnClickButton(MapBlockCharNormal __instance)
        {
            if (!quickShop) return true;

            //MyUtils.MyLog("点了 Normal");

            var obj = Traverse.Create(__instance).Field("merchantNameBg").GetValue<GameObject>();
            if (IsClickName(obj.transform as RectTransform))
            {
                OpenCharShop(__instance);
                return false;
            }
            //MyUtils.MyLog("没点到名字");
            return true;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(MapBlockCharCaravan), "OnClickButton")]
        public static bool MapBlockCharCaravan_OnClickButton(MapBlockCharCaravan __instance)
        {
            if(!quickShop) return true;
            //MyUtils.MyLog("点了 Caravan");

            var obj = Traverse.Create(__instance).Field("merchantNameBg").GetValue<GameObject>();
            if (IsClickName(obj.transform as RectTransform))
            {
                var _caravanData = Traverse.Create(__instance).Field("_caravanData").GetValue<CaravanDisplayData>();
                OpenCaravan(__instance, _caravanData.CaravanId);
                return false;
            }
            //MyUtils.MyLog("没点到名字");
            return true;
        }
        public static bool IsClickName(RectTransform rectTransform)
        {
            if(!rectTransform.gameObject.activeInHierarchy) return false;
            var canvas = rectTransform.GetComponentInParent<Canvas>();
            return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, Input.mousePosition, canvas.worldCamera);
        }

        public static void OpenCharShop(MapBlockCharBase __instance)
        {
            //MyUtils.MyLog("点了 OpenCharShop");
            if (!quickShop) return;
            if (!CheckBlock(__instance)) return;

            // 可看 EventHelper.StartMerchantAction
            var charId = Traverse.Create(__instance).Field("CharId").GetValue<int>();
            OpenShopEventArguments openShopEventArguments = new OpenShopEventArguments
            {
                Id = charId,
                MerchantSourceType = 0
            };
            ArgumentBox args = EasyPool.Get<ArgumentBox>().SetObject("OpenShopEventArguments", openShopEventArguments);
            UIElement.Shop.SetOnInitArgs(args);
            UIManager.Instance.ShowUI(UIElement.Shop);
        }
        public static void OpenCaravan(MapBlockCharBase __instance, int caravanId)
        {
            //MyUtils.MyLog("点了 OpenCaravan");
            // 可看 EventHelper.StartCaravanShopAction
            if (!quickShop) return;
            if (!CheckBlock(__instance)) return;

            OpenShopEventArguments openShopEventArguments = new OpenShopEventArguments
            {
                Id = caravanId,
                MerchantSourceType = 5
            };
            ArgumentBox args = EasyPool.Get<ArgumentBox>().SetObject("OpenShopEventArguments", openShopEventArguments);
            UIElement.Shop.SetOnInitArgs(args);
            UIManager.Instance.ShowUI(UIElement.Shop);
        }
        public static bool CheckBlock(MapBlockCharBase blockChar)
        {
            // 看 MapBlockCharNormal OnClickButton
            var mapBlock = Traverse.Create(blockChar).Field("MapBlock").GetValue<MapBlockData>();
            var curBlock = SingletonObject.getInstance<WorldMapModel>().CurrentBlockId;
            //MyUtils.MyLog($"CheckBlock {mapBlock.BlockId} {curBlock}");
            if (mapBlock.BlockId == curBlock)
                return true;
            else
            {
                if (noLimitShop) return true;
                return  false;
            }
        }


        #endregion
        #region 商队页显示商人
        // 增加商队页的数量；让商队页可以点击
        [HarmonyPostfix, HarmonyPatch(typeof(UI_MapBlockCharList), "get_HasSpecial")]
        public static void UI_MapBlockCharList_get_HasSpecial(UI_MapBlockCharList __instance, ref bool __result)
        {
            if (!blockShop) return;
            if (__result) return;

            var _normalCharList = Traverse.Create(__instance).Field("_normalCharList").GetValue<List<int>>();
            var merchantList = GetMerchant(__instance, _normalCharList, "");
            if(merchantList.Count > 0)
            {
                __result = true;
            }
            //MyUtils.MyLog($"get_HasSpecial  merchantList {merchantList.Count}");
        }
        // 把商人加入到 搜索结果列表中
        [HarmonyPostfix, HarmonyPatch(typeof(UI_MapBlockCharList), "RefreshSearchedCaravanCharacterData")]
        public static void RefreshSearchedCaravanCharacterData(UI_MapBlockCharList __instance, ref int __result)
        {
            if (!blockShop) return;

            // 可看 UI_MapBlockCharList RefreshSearchedNormalCharacterData
            var _searchedSpecialCharList = Traverse.Create(__instance).Field("_searchedSpecialCharList").GetValue<List<int>>();
            var _searchInputField = Traverse.Create(__instance).Field("_searchInputField").GetValue<TMP_InputField>();
            var _normalCharList = Traverse.Create(__instance).Field("_normalCharList").GetValue<List<int>>();
            var _specialCharList = Traverse.Create(__instance).Field("_specialCharList").GetValue<List<int>>();

            //MyUtils.MyLog($"RefreshSearchedCaravanCharacterData  before {_searchedSpecialCharList.Count}");
            string search = _searchInputField.text;
            var merchantList = GetMerchant(__instance, _normalCharList, search);
            if (merchantList.Count > 0) {
                _searchedSpecialCharList.AddRange(merchantList);
                __result = _searchedSpecialCharList.Count;
            }
            //MyUtils.MyLog($"RefreshSearchedCaravanCharacterData  after {_searchedSpecialCharList.Count}");
        }
        /// <summary>
        /// 从人物列表中获取商人
        /// </summary>
        public static List<int> GetMerchant(UI_MapBlockCharList __instance, List<int> normalCharList, string search)
        {
            var _charDataDict = Traverse.Create(__instance).Field("_charDataDict").GetValue<Dictionary<int, CharacterDisplayData>>();
            return normalCharList.Where(charId =>
            {
                if (_charDataDict.TryGetValue(charId, out var characterDisplayData))
                {
                    if (IsMerchantShow(characterDisplayData)) // 是否商人
                    {
                        if (!string.IsNullOrEmpty(search)) // 是否搜索的 城镇、身份、名字
                        {
                            string nameContent = NameCenter.GetMonasticTitleOrDisplayName(characterDisplayData, isTaiwu: false);
                            string org = CommonUtils.GetOrganizationGradeString(characterDisplayData.OrgInfo, characterDisplayData.Gender, characterDisplayData.CurrAge, -1);
                            return nameContent.Contains(search) || org.Contains(search);
                        }
                        else
                            return true;
                    }
                }
                return false;
            }).ToList();
        }
        /// <summary>
        /// 判断是商人（非小孩）
        /// </summary>
        public static bool IsMerchantShow(CharacterDisplayData characterDisplayData)
        {
            bool isChild = CommonUtils.CheckCharIsChild(characterDisplayData.OrgInfo, characterDisplayData.CurrAge);
            bool isMerchant = CommonUtils.CheckCharIsMerchant(characterDisplayData.OrgInfo);
            return !isChild && isMerchant;
        }

        /// <summary>
        /// 处理商人显示；添加 MapBlockCharNormalMerchant 类
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(UI_MapBlockCharList), "OnRenderCharCaravan")]
        public static bool OnRenderCharCaravan(UI_MapBlockCharList __instance, 
            int index, Refers charRefers)
        {
            if (!blockShop) return true; // 执行原函数

            // 看 OnRenderCharNormal
            var _searchedSpecialCharList = Traverse.Create(__instance).Field("_searchedSpecialCharList").GetValue<List<int>>();
            var _specialCharList = Traverse.Create(__instance).Field("_specialCharList").GetValue<List<int>>();
            var _normalCharList = Traverse.Create(__instance).Field("_normalCharList").GetValue<List<int>>();

            int charId = (_searchedSpecialCharList.CheckIndex(index) ? _searchedSpecialCharList[index] : (-1));
            if(_specialCharList.Contains(charId)) // 如果charid属于商队，原函数处理
            {
                return true;
            }
            var _charDataDict = Traverse.Create(__instance).Field("_charDataDict").GetValue<Dictionary<int, CharacterDisplayData>>();
            //MyUtils.MyLog($"charId {charId}");
            //MyUtils.MyLog($"_charDataDict {_charDataDict.Count}");
            if (_charDataDict.TryGetValue(charId, out var characterDisplayData)) // 是添加进去的商人
            {
                //MyUtils.MyLog($"characterDisplayData {characterDisplayData}");
                var _canSeeDetail = Traverse.Create(__instance).Field("_canSeeDetail").GetValue<bool>();
		        int togKey = UI_MapBlockCharList.TogKeyCaravan;
                var prefabName = Traverse.Create(__instance).Method("GetCharPrefabName", togKey, _canSeeDetail).GetValue<string>();

                var _canInteract = Traverse.Create(__instance).Field("_canInteract").GetValue<bool>();
                var _block = Traverse.Create(__instance).Field("_block").GetValue<MapBlockData>();
                var _loongInfos = Traverse.Create(__instance).Field("_loongInfos").GetValue<List<LoongInfo>>();
                //MyUtils.MyLog($"_canSeeDetail {_canSeeDetail}");
                if (_canSeeDetail)
                {
                    //ShowMonoCur(charRefers.gameObject);
                    if(charRefers.CTryGet<MapBlockCharCaravan>(prefabName, out var mapBlockCharCaravan))
                    {
                        var mapBlockCharNormalMerchant = mapBlockCharCaravan.gameObject.GetComponent<MapBlockCharNormalMerchant>();
                        if(mapBlockCharNormalMerchant == null) // 挂自己的MapBlockCharNormalMerchant 处理显示
                        {
                            //MyUtils.MyLog($"add mapBlockCharNormalMerchant {charId}");
                            mapBlockCharNormalMerchant = mapBlockCharCaravan.gameObject.AddComponent<MapBlockCharNormalMerchant>();
                            mapBlockCharNormalMerchant.SetUI(mapBlockCharCaravan);
                        }
                        mapBlockCharNormalMerchant.Init(_canInteract, _block, characterDisplayData, _loongInfos);
                    }
                }
                else
                {
                    MapBlockCharUnknown mapBlockCharUnknown;
                    bool flag4 = charRefers.CTryGet<MapBlockCharUnknown>(prefabName, out mapBlockCharUnknown);
                    if (flag4)
                    {
                        mapBlockCharUnknown.Init(_canInteract, _block);
                    }
                }

                return false;
            }
            return true;
        }

        // 地块商人item，主要在商队ui上显示人物信息
        public class MapBlockCharNormalMerchant : MapBlockCharAlive
        {
            //private GameObject merchantNameBg;
            private CharacterDisplayData _characterDisplayData;
            private CharacterItem _charConfig;


            private MapBlockCharCaravan caravanUI;
            private GameObject merchantNameBg;
            private TextMeshProUGUI merchantNameText;
            private CImage merchantLevelImage;
            TextMeshProUGUI businessMoveTime;

            public void SetUI(MapBlockCharCaravan mapBlockCharCaravan)
            {
                MyUtils.CopyBaseClassFieldsIncludingParents(mapBlockCharCaravan, this, typeof(MapBlockCharAlive));
                //MyUtils.MyLog($"this.nameText {this.nameText}");

                caravanUI = mapBlockCharCaravan;
                merchantNameBg = Traverse.Create(mapBlockCharCaravan).Field("merchantNameBg").GetValue<GameObject>();
                merchantNameText = Traverse.Create(mapBlockCharCaravan).Field("merchantNameText").GetValue<TextMeshProUGUI>();
                merchantLevelImage = Traverse.Create(mapBlockCharCaravan).Field("merchantLevelImage").GetValue<CImage>();
            }
            public void Init(bool canInteract, MapBlockData mapBlock, CharacterDisplayData characterDisplayData, List<LoongInfo> loongInfos)
            {
                base.Init(canInteract, mapBlock);
                this._characterDisplayData = characterDisplayData;
                this.CharId = this._characterDisplayData.CharacterId;
                this._charConfig = Character.Instance[this._characterDisplayData.TemplateId];
                this.Refresh();
            }
            protected override void Refresh()
            {
                base.Refresh();
                RefreshFavor();
            }
            protected override void RefreshName()
            {
                string nameContent = NameCenter.GetMonasticTitleOrDisplayName(_characterDisplayData, isTaiwu: false);
                //MyUtils.MyLog($"nameContent {nameContent}");
                //MyUtils.MyLog($"nameText {this.nameText}");
                this.nameText.text = nameContent;
            }
            protected override void RefreshOrganization()
            {
                this.organizationText.text = CommonUtils.GetOrganizationGradeString(this._characterDisplayData.OrgInfo, this._characterDisplayData.Gender, this._characterDisplayData.CurrAge, (int)this._characterDisplayData.TemplateId);
                bool isChild = CommonUtils.CheckCharIsChild(this._characterDisplayData.OrgInfo, this._characterDisplayData.CurrAge);
                bool isMerchant = CommonUtils.CheckCharIsMerchant(this._characterDisplayData.OrgInfo);
                bool flag = !isChild && isMerchant;
                if (flag)
                {
                    MerchantDomainMethod.AsyncCall.GetMerchantTemplateId(null, this._characterDisplayData.CharacterId, delegate (int offset, RawDataPool dataPool)
                    {
                        sbyte merchantTemplateId = -1;
                        Serializer.Deserialize(dataPool, offset, ref merchantTemplateId);
                        bool flag2 = merchantTemplateId > -1;
                        if (flag2)
                        {
                            this.merchantNameBg.gameObject.SetActive(true);
                            MerchantItem merchantConfig = Merchant.Instance[merchantTemplateId];
                            MerchantTypeItem merchantTypeConfig = global::Config.MerchantType.Instance[merchantConfig.MerchantType];
                            this.merchantNameText.text = merchantTypeConfig.Name;
                            this.merchantLevelImage.SetSprite(base.GetMerchantLevelImage(merchantConfig.Level), false, null);
                        }
                    });
                }
                else
                {
                    this.merchantNameBg.gameObject.SetActive(false);
                }
                this.organizationIcon.gameObject.SetActive(true);
                this.organizationIcon.SetSprite(CommonUtils.GetIdentityIcon(this._characterDisplayData.OrgInfo.Grade), false, null);
            }
            protected override void RefreshAvatar()
            {
                base.RefreshAvatar();
                base.CharacterAvatar.CharacterId = this._characterDisplayData.CharacterId;
            }

            protected override void RefreshIcon()
            {
                this.iconImage.SetSprite("sp_icon_shanghui", false, null);
            }
            private void RefreshFavor()
            {
                var merchantRefer = Traverse.Create(caravanUI).Field("merchantRefer").GetValue<Refers>();
                var merchantFavor = merchantRefer.CGet<TextMeshProUGUI>("Favor");
                var businessMoveTime = merchantRefer.CGet<TextMeshProUGUI>("MoveTime");

                var s = CommonUtils.GetFavorString(_characterDisplayData.FavorabilityToTaiwu);
                merchantFavor.text = $"商人好感: {s}";
                businessMoveTime.text = "0";
            }
            protected override void OnClickButton()
            {
                //MyUtils.MyLog("点了 merchant");
                if(quickShop && IsClickName(merchantNameBg.transform as RectTransform))
                {
                    // 打开商店
                    OpenCharShop(this);
                }
                else 
                {
                    //MyUtils.MyLog("没点到名字");
                    // 打开人物界面
                    var curBlock = SingletonObject.getInstance<WorldMapModel>().CurrentBlockId;
                    if (MapBlock.BlockId == curBlock)
                    {
                        GameDataBridge.AddMethodCall<int>(-1, 12, 13, this._characterDisplayData.CharacterId);
                        base.OnClickButton();
                    }
                }
            }
        }

        #endregion
        #endregion
    }
}
