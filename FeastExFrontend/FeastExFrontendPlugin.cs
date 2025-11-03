using FrameWork;
using GameData.Domains.Building;
using GameData.Domains.Item;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using TaiwuModdingLib.Core.Plugin;
using TMPro;
using UnityEngine;

namespace FeastExFrontend
{
    [PluginConfig(pluginName: "FeastEx", creatorId: "atakhalo", pluginVersion: "2025.10.13.1")]
    public class FeastExFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool pluginEnable; // 开关 是否隐藏装备或预设中
        public const short feastIdEgg = 18888;
        public const string feastNameEgg = "减脂宴";
        public const string feastCondEgg = "布置3份蛋类菜品";
        public const string feastEffectEgg = "宾客将恢复大量健康";

        public static Dictionary<EFoodFoodType, int> countByFoodType;
        public static Dictionary<short, int> countBySubType;

        public static short curFeastId;


        public override void Initialize()
        {
            Debug.Log($"[FeastEx] Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(FeastExFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "pluginEnable", ref pluginEnable);
        }

        #region
        public static bool CheckFeastType(Feast feast, out short feastType, out string name)
        {
            feastType = Config.Feast.DefValue.None.TemplateId;
            name = "";

            if (countByFoodType == null)
                return false;

            if (countByFoodType.ContainsKey(EFoodFoodType.Egg) && countByFoodType[EFoodFoodType.Egg] == GlobalConfig.Instance.FeastCount)
            {
                feastType = feastIdEgg;
                name = feastNameEgg;
                return true;
            }
            return false;

    //        if (feast.Dish.Count < GlobalConfig.Instance.FeastCount)
    //        {
    //            return false;
    //        }

    //        foreach (var itemKey in feast.Dish.Values)
    //        {
    //            if (!itemKey.IsValid())
    //            {
    //                return false;
    //            }
				//short itemSubType = ItemTemplateHelper.GetItemSubType(itemKey.ItemType, itemKey.TemplateId);
    //            if (itemSubType == 900 || itemSubType == 901)
    //                return false;

    //            Config.FoodItem foodItem = Config.Food.Instance[itemKey.TemplateId];
    //            if(foodItem.FoodType == null || !foodItem.FoodType.Contains(EFoodFoodType.Egg))
    //            {
    //                return false;
    //            }
    //        }
    //        feastType = feastIdEgg;
    //        name = feastNameEgg;
    //        return true;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Feast), "Check")]
        public static void CatchCountDict(Dictionary<EFoodFoodType, int> countByFoodType, Dictionary<short, int> countBySubType)
        {
            if (pluginEnable)
            {
                FeastExFrontendPlugin.countByFoodType = countByFoodType;
                FeastExFrontendPlugin.countBySubType = countBySubType;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UI_BuildingManage), "RefreshEntertainPage")]
        public static void ShowFeastType(UI_BuildingManage __instance, 
            Feast ____feast, Refers ____entertainPage
            )
        {
            curFeastId = 0;
            if (pluginEnable)
            {
                if(____feast.GetFeastType() == Config.Feast.DefValue.None.TemplateId
                    && !____entertainPage.CGet<GameObject>("TitleBack").activeSelf)
                {

                    if(CheckFeastType(____feast, out var feastType, out string name))
                    {
                        ____entertainPage.CGet<GameObject>("TitleBack").SetActive(true);
                        ____entertainPage.CGet<TextMeshProUGUI>("TitleText").text = name;
                        MouseTipDisplayer tip = ____entertainPage.CGet<MouseTipDisplayer>("TitleTip");
                        if (tip.RuntimeParam == null)
                        {
                            tip.RuntimeParam = new ArgumentBox();
                        }
                        tip.RuntimeParam.Set("type", feastType);
                        tip.Type = TipType.BuildingFeast;
                        curFeastId = feastType;
                        //Debug.Log($"[FeastEx] set typeEx {feastType} {curFeastId}");
                    }
                }
            }
        }
        #endregion

        #region 减脂餐

        [HarmonyPostfix,  HarmonyPatch(typeof(UI_MouseTipBuildingFeast), "Init")]
        public static void FeastTipsShow(UI_MouseTipBuildingFeast __instance, ArgumentBox argsBox)
        {
            //Debug.Log($"[FeastEx] OnRenderFeastEx run");

            if (pluginEnable)
            {
                //Debug.Log($"[FeastEx] curFeastId {curFeastId}");

                if (curFeastId == feastIdEgg)
                {
                    __instance.CGet<TextMeshProUGUI>("Title").text = feastNameEgg;
                    __instance.CGet<TextMeshProUGUI>("ConditionContent").text = feastCondEgg;
                    __instance.CGet<TextMeshProUGUI>("EffectContent").text = feastEffectEgg;
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UI_BuildingFeastMenu), "OnSearch")]
        public static void AddFeastEx(UI_BuildingFeastMenu __instance, string value,
            List<short> ____tempMenuList)
        {
            if (pluginEnable) {
                __instance.CGet<InfinityScroll>("ScrollView").UpdateData(____tempMenuList.Count + 1);
            }
            else
            {
                __instance.CGet<InfinityScroll>("ScrollView").UpdateData(____tempMenuList.Count);
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UI_BuildingFeastMenu), "OnItemRender")]
        public static bool OnRenderFeastEx(UI_BuildingFeastMenu __instance, int index, Refers refers,
            List<short> ____tempMenuList)
        {
            //Debug.Log($"[FeastEx] OnRenderFeastEx run");
            if (pluginEnable)
            {
                if(index == ____tempMenuList.Count)
                {
                    refers.CGet<TextMeshProUGUI>("Title").text = feastNameEgg;
                    refers.CGet<TextMeshProUGUI>("ConditionContent").text = feastCondEgg;
                    refers.CGet<TextMeshProUGUI>("EffectContent").text = feastEffectEgg;
                    return false;
                }
            }
            return true;
        }
        #endregion 减脂餐
    }
}
