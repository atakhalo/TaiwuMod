#pragma warning disable CS8618, CS8600, CS8603, CS8625, CS8601, CS8604

using Config;
using GameData.Common;
using GameData.DLC;
using GameData.Domains;
using GameData.Domains.Building;
using HarmonyLib;
using NLog;
using TaiwuModdingLib.Core.Plugin;

namespace WeaveUnlockBackend
{
    /// <summary>
    /// 改制解锁：让绣楼改制界面的"可选款式"包含所有衣装，不受"已获取过"解锁限制。
    /// 原理：改制界面的可选款式列表完全来自 BuildingDomain.GetBuildingMakeDisplayData 下发的
    ///       BuildingMakeDisplayData.OwnedClothingList（原逻辑 = 太吾已获取过的衣装 OwnedClothingSet）。
    ///       本 mod 只在该方法返回时把列表替换为全部衣装模板，不改动任何存档数据，
    ///       后端执行改制的 WeaveClothingItem 本身也没有解锁校验，因此完全非侵入。
    /// 注意：DLC 专属衣装（ClothingItem.DlcName 非空）对应的外观图集只在安装对应 DLC 时才加载，
    ///       若未安装 DLC 就把该衣装塞进列表，前端渲染卡片会报 "Failed to find atlas ..." 错误。
    ///       因此这里会过滤掉「DlcName 非空且对应 DLC 未安装」的衣装。
    /// </summary>
    [PluginConfig(pluginName: "WeaveUnlock", creatorId: "atakhalo", pluginVersion: "0.1.1.1")]
    public class WeaveUnlockBackendPlugin : TaiwuRemakePlugin
    {
        private Harmony? harmony;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>总开关：改制解锁</summary>
        public static bool pluginEnable = true;

        /// <summary>衣装 DlcName → Steam DLC AppId（与前端 DlcManager 一致）</summary>
        private static readonly Dictionary<string, ulong> DlcNameToAppId = new Dictionary<string, ulong>
        {
            { "GiftFromConchShip1", 2241120 },
            { "GiftFromConchShip2", 2172690 },
            { "InteractOfLove", 0 },
            { "FiveLoong", 2764950 },
            { "HappyNewYear2024", 2764960 },
            { "YearOfSnakeCloth", 3464590 },
            { "HappyNewYear2026", 4395170 },
            { "EightYears", 4834440 },
            { "GreenHillsRemain", 4834450 },
        };

        public static void MyLog(string log)
        {
            logger.Info($"[WeaveUnlock] {log}");
        }

        public override void Initialize()
        {
            MyLog("Backend Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(WeaveUnlockBackendPlugin));
        }

        public override void Dispose()
        {
            harmony?.UnpatchSelf();
        }

        public override void OnModSettingUpdate()
        {
            DomainManager.Mod.GetSetting(ModIdStr, "pluginEnable", ref pluginEnable);
        }

        /// <summary>
        /// 改制界面下发数据时，将"可选款式"列表替换为全部衣装模板，解锁所有改制外观。
        /// DLC 衣装仅在该 DLC 已安装时保留（否则前端无对应外观图集会报错）。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BuildingDomain), "GetBuildingMakeDisplayData")]
        public static void UnlockAllWeaveMaterials(BuildingMakeDisplayData __result)
        {
            if (!pluginEnable || __result == null)
            {
                return;
            }

            List<short> allKeys = Clothing.Instance.GetAllKeys();
            List<short> available = new List<short>(allKeys.Count);
            foreach (short templateId in allKeys)
            {
                ClothingItem clothing = Clothing.Instance[templateId];
                if (clothing == null)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(clothing.DlcName)
                    && DlcNameToAppId.TryGetValue(clothing.DlcName, out ulong appId)
                    && appId > 0
                    && !DlcManager.IsDlcInstalled(appId))
                {
                    continue;
                }
                available.Add(templateId);
            }

            __result.OwnedClothingList = available;
        }
    }
}
