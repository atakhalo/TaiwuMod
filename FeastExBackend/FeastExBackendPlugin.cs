using GameData.Common;
using GameData.Domains;
using GameData.Domains.Building;
using GameData.Domains.Extra;
using GameData.Domains.Item;
using HarmonyLib;
using NLog;
using NLog.Fluent;
using TaiwuModdingLib.Core.Plugin;

namespace FeastExBackend
{
    [PluginConfig(pluginName: "FeastEx", creatorId: "atakhalo", pluginVersion: "2025.10.13.1")]
    public class FeastExBackendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static bool pluginEnable; // 开关 是否隐藏装备或预设中

        public const short feastIdEgg = 18888;
        public const string feastNameEgg = "减脂宴";

        public static Dictionary<EFoodFoodType, int> countByFoodType;
        public static Dictionary<short, int> countBySubType;

        public override void Initialize()
        {
            logger.Info("[FeastEx] Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(FeastExBackendPlugin));
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
            DomainManager.Mod.GetSetting(ModIdStr, "pluginEnable", ref pluginEnable);
        }


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
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Feast), "Check")]
        public static void CatchCountDict(Dictionary<EFoodFoodType, int> countByFoodType, Dictionary<short, int> countBySubType)
        {
            if (pluginEnable)
            {
                FeastExBackendPlugin.countByFoodType = countByFoodType;
                FeastExBackendPlugin.countBySubType = countBySubType;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ExtraDomain), "FeastEatDish")]
        public static void FeastExEffect(ExtraDomain __instance, 
            DataContext context, GameData.Domains.Building.Feast feast, List<ItemKey> items, 
            List<int> indexes, GameData.Domains.Character.Character character, short feastType, List<int> characters)
        {
            if (pluginEnable)
            {
                if(CheckFeastType(feast, out var feastExType, out _))
                {
                    var health = character.GetHealth();
                    var maxHealth = character.GetLeftMaxHealth(false);
                    if(health < maxHealth)
                    {
                        var newHealth = (short)Math.Clamp(health + 12, health, character.GetLeftMaxHealth(false));
                        character.SetHealth(newHealth, context);
                        //logger.Info($"[FeastEx] {character.GetFullName()} heal ({newHealth}) ({health})");
                    }
                }
            }
        }
    }

}
