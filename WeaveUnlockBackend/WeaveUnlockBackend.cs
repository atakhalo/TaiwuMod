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
    ///       若未安装 DLC 就把该衣装塞进列表，前端渲染卡片会报
    ///       "Failed to load Resource GameAtlas/AvatarPackers/avatar_x_cloth_xxxxx_normal"。
    ///       因此这里会过滤掉「DlcName 对应 DLC 未安装」的衣装；
    ///       DlcName → AppId 的对应关系直接查游戏配置表 Config.ImplementedDlc，游戏新增 DLC 时无需改本 mod。
    /// </summary>
    [PluginConfig(pluginName: "WeaveUnlock", creatorId: "atakhalo", pluginVersion: "0.1.3.0")]
    public class WeaveUnlockBackendPlugin : TaiwuRemakePlugin
    {
        private Harmony? harmony;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>总开关：改制解锁</summary>
        public static bool pluginEnable = true;

        /// <summary>DlcName → Steam AppId 映射缓存（来源：游戏配置表 ImplementedDlc）</summary>
        private static Dictionary<string, uint>? cachedDlcNameToAppId;

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

            Dictionary<string, uint> dlcNameToAppId = GetDlcNameToAppIdMap();
            List<short> allKeys = Clothing.Instance.GetAllKeys();
            List<short> available = new List<short>(allKeys.Count);
            foreach (short templateId in allKeys)
            {
                ClothingItem clothing = Clothing.Instance[templateId];
                if (clothing == null)
                {
                    continue;
                }

                string? dlcName = clothing.DlcName;
                if (!string.IsNullOrEmpty(dlcName) && !IsDlcContentAvailable(dlcName, dlcNameToAppId))
                {
                    continue;
                }

                available.Add(templateId);
            }

            __result.OwnedClothingList = available;
        }

        /// <summary>
        /// 取 DlcName → Steam AppId 映射，来源是游戏自身的 DLC 配置表 Config.ImplementedDlc，
        /// 因此游戏新增 DLC 时本 mod 无需改动。配置表尚未就绪（读到空表）时不缓存，下次再读。
        /// </summary>
        private static Dictionary<string, uint> GetDlcNameToAppIdMap()
        {
            if (cachedDlcNameToAppId != null)
            {
                return cachedDlcNameToAppId;
            }

            Dictionary<string, uint> map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ImplementedDlcItem dlc in ImplementedDlc.Instance)
                {
                    if (!string.IsNullOrEmpty(dlc.Name))
                    {
                        map[dlc.Name] = dlc.AppId;
                    }
                }
            }
            catch (Exception e)
            {
                // 配置表异常只影响过滤精度，不应连累改制界面的数据下发
                MyLog($"读取 DLC 配置表失败，本次不做 DLC 过滤：{e.Message}");
                return map;
            }

            if (map.Count > 0)
            {
                cachedDlcNameToAppId = map;
                MyLog($"已从配置表 ImplementedDlc 读入 {map.Count} 条 DLC 映射");
            }
            else
            {
                MyLog("DLC 配置表 ImplementedDlc 尚未就绪，本次不做 DLC 过滤");
            }
            return map;
        }

        /// <summary>
        /// 判断 DLC 衣装当前是否可用：未安装 DLC 的衣装没有外观图集，下发到前端渲染会报
        /// "Failed to load Resource GameAtlas/AvatarPackers/avatar_x_cloth_xxxxx_normal"。
        /// DlcName 不在 DLC 配置表里的一律保留（如 mod 自加内容，其外观图集由 mod 自己提供）。
        /// </summary>
        private static bool IsDlcContentAvailable(string dlcName, Dictionary<string, uint> dlcNameToAppId)
        {
            if (!dlcNameToAppId.TryGetValue(dlcName, out uint appId))
            {
                return true;
            }
            return appId == 0 || DlcManager.IsDlcInstalled(appId);
        }
    }
}
