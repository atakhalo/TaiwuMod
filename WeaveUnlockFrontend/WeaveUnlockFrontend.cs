#pragma warning disable CS8618, CS8600, CS8603, CS8625, CS8601, CS8604

using FrameWork.ModSystem;
using Game.Views.Make;
using HarmonyLib;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine;

namespace WeaveUnlockFrontend
{
    /// <summary>
    /// 改制解锁 - 前端：提供"改制造诣需求归零"开关。
    /// 原理：绣楼改制界面的造诣需求判断全部经由 ViewMake.GetAttainmentByBuildingEffect(10, WeaveNeedAttainment)
    ///       （lifeSkillType=10 即织锦，改制专属），该方法的其他调用（毒=8 等制造）不受影响。
    ///       开启后把改制场景的返回造诣需求改为 0：
    ///       - 卡片"可选款式"的造诣红蓝显示变为满足（蓝色）
    ///       - 界面"需求造诣：xxx"显示为 0
    ///       - 确认按钮不再因造诣不足被禁用
    ///       后端执行改制的 WeaveClothingItem 本身没有造诣校验，因此只需 patch 前端判断即可。
    /// </summary>
    [PluginConfig(pluginName: "WeaveUnlock", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class WeaveUnlockFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony? harmony;

        /// <summary>改制造诣需求归零</summary>
        public static bool zeroWeaveAttainment = false;

        public override void Initialize()
        {
            Debug.Log("[WeaveUnlock] Frontend Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(WeaveUnlockFrontendPlugin));
        }

        public override void Dispose()
        {
            harmony?.UnpatchSelf();
        }

        public override void OnModSettingUpdate()
        {
            ModManager.GetSetting(ModIdStr, "zeroWeaveAttainment", ref zeroWeaveAttainment);
        }

        /// <summary>
        /// 改制界面造诣需求归零：仅当 lifeSkillType == 10（织锦/改制）时把返回需求改为 0。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ViewMake), "GetAttainmentByBuildingEffect")]
        public static void ZeroWeaveAttainment(sbyte lifeSkillType, ref short __result)
        {
            if (zeroWeaveAttainment && lifeSkillType == 10)
            {
                __result = 0;
            }
        }
    }
}
