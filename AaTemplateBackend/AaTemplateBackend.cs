#pragma warning disable CS8618, CS8600, CS8603, CS8625, CS8601, CS8604

using Config;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.CombatSkill;
using GameData.Domains.Extra;
using GameData.Domains.Item;
using GameData.Domains.Taiwu;
using GameData.GameDataBridge;
using GameData.Serializer;
using GameData.Utilities;
using HarmonyLib;
using NLog;
using TaiwuModdingLib.Core.Plugin;

namespace AaTemplateBackend
{
    [PluginConfig(pluginName: "AaTemplate", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class AaTemplateBackendPlugin : TaiwuRemakePlugin
    {
        private Harmony? harmony;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
		public static bool switchTongdao; // 开关

		public static void MyLog(string log)
		{
			logger.Info($"[AaTemplate] {log}");
		}

		public override void Initialize()
        {
			MyLog("[AaTemplate] Backend Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(AaTemplateBackendPlugin));
        }

        public override void Dispose()
        {
            harmony?.UnpatchSelf();
        }

		public override void OnModSettingUpdate()
		{
			DomainManager.Mod.GetSetting(ModIdStr, "switchTongdao", ref switchTongdao);		
		}
	}
}
