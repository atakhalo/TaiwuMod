#pragma warning disable CS8618, CS8600, CS8603, CS8625, CS8601, CS8604

using GameData.Domains;
using GameData.Domains.Character;
using HarmonyLib;
using NLog;
using Redzen.Random;
using TaiwuModdingLib.Core.Plugin;

namespace ChunXiaoBuYunBackend
{
    /// <summary>
    /// 春宵不孕：共度春宵时不导致怀孕（双方都不怀孕）。
    /// 原理：游戏内所有"春宵 → 怀孕"路径（玩家主动/同道/NPC 离线模拟/剧情事件）最终都统一调用
    ///       PregnantState.CheckPregnant 进行怀孕判定，唯一调用点在 Character.OfflineMakeLove 内。
    ///       本 mod 只 patch 这一个静态方法：命中目标角色时让判定直接返回"不怀孕"，
    ///       春宵本身（特性、好感、生活记录、事件）完全不受影响。
    /// 范围：默认太吾本人参与春宵不怀孕；可开关扩展到同道（太吾队伍成员）、村民（太吾村人物）。
    /// 注意：不影响非春宵怀孕途径（无父怀孕/梦境怀孕/GM 命令等）。
    /// </summary>
    [PluginConfig(pluginName: "ChunXiaoBuYun", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class ChunXiaoBuYunBackendPlugin : TaiwuRemakePlugin
    {
        private Harmony? harmony;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>功能开关</summary>
        public static bool pluginEnable = true;

        /// <summary>对同道（太吾队伍成员）生效</summary>
        public static bool effectTeammate = false;

        /// <summary>对村民（太吾村人物）生效</summary>
        public static bool effectVillager = false;

        /// <summary>太吾村组织的模板 ID（官方判定 CheckCondition_CharIsTaiwuVillager 用）</summary>
        private const sbyte TaiwuVillageOrgTemplateId = 16;

        public static void MyLog(string log)
        {
            logger.Info($"[ChunXiaoBuYun] {log}");
        }

        public override void Initialize()
        {
            MyLog("Backend Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(ChunXiaoBuYunBackendPlugin));
        }

        public override void Dispose()
        {
            harmony?.UnpatchSelf();
        }

        public override void OnModSettingUpdate()
        {
            DomainManager.Mod.GetSetting(ModIdStr, "pluginEnable", ref pluginEnable);
            DomainManager.Mod.GetSetting(ModIdStr, "effectTeammate", ref effectTeammate);
            DomainManager.Mod.GetSetting(ModIdStr, "effectVillager", ref effectVillager);
        }

        /// <summary>
        /// 怀孕判定拦截：命中目标角色时直接返回"不怀孕"。
        /// 判定为"不怀孕"后 OfflineMakeLove 返回 false，不会创建 PregnantState，
        /// 即春宵双方都不会怀孕；春宵的其他效果（杂阳毁阴特性、好感、生活记录、事件）照常。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PregnantState), "CheckPregnant")]
        public static bool CheckPregnantPrefix(Character father, Character mother, ref bool __result)
        {
            if (!pluginEnable)
            {
                return true;
            }

            // 太吾本人：默认生效
            bool fatherIsTaiwu = father.IsTaiwu();
            bool motherIsTaiwu = mother.IsTaiwu();
            if (fatherIsTaiwu || motherIsTaiwu)
            {
                __result = false;
                return false;
            }

            // 同道：太吾队伍成员（IsInTaiwuGroup 含本人，这里排除本人避免重复判断）
            if (effectTeammate)
            {
                bool fatherIsTeammate = father.IsInTaiwuGroup() && !fatherIsTaiwu;
                bool motherIsTeammate = mother.IsInTaiwuGroup() && !motherIsTaiwu;
                if (fatherIsTeammate || motherIsTeammate)
                {
                    __result = false;
                    return false;
                }
            }

            // 村民：太吾村人物（含村长，Grade==7 同属 OrgTemplateId==16）
            if (effectVillager)
            {
                bool fatherIsVillager = father.GetOrganizationInfo().OrgTemplateId == TaiwuVillageOrgTemplateId;
                bool motherIsVillager = mother.GetOrganizationInfo().OrgTemplateId == TaiwuVillageOrgTemplateId;
                if (fatherIsVillager || motherIsVillager)
                {
                    __result = false;
                    return false;
                }
            }

            return true;
        }
    }
}
