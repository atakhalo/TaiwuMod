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
using Game.Views.CharacterMenu;
using GameData.Domains.World;
using HarmonyLib;
using HarmonyLib.Tools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaiwuModdingLib.Core.Plugin;
using TaiwuModdingLib.Core.Utils;
using TMPro;
using UICommon.Character;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GameData.Domains.Character.AvatarSystem;
//using UnityEngine.Windows;
//using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
//using static GameData.Domains.Item.ItemOperationType;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

namespace WuZhe
{
    [PluginConfig(pluginName: "WuZhe", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class WuZheFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool ju = true; // 香驹衣开关
		public static bool juBack = true; // 香驹衣后发开关

		public override void Initialize()
        {
			MyUtils.modName = nameof(WuZhe);
			MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(WuZheFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "ju", ref ju);
			ModManager.GetSetting(ModIdStr, "juBack", ref juBack);
			MyUtils.MyLog($"ju={ju}, juBack={juBack}");

		}

		/// <summary>
		/// 静态模式：阻止香驹衣的帽子后片替换后发
		/// </summary>
		[HarmonyPrefix, HarmonyPatch(typeof(Game.Components.Avatar.Avatar), "SetClothHatBackPart")]
		public static bool Avatar_SetClothHatBackPart_Prefix(Game.Components.Avatar.Avatar __instance, ref bool __result)
		{
			if (ju && juBack && __instance.Data.ClothDisplayId == 30011)
			{
				__result = false;
				return false;
			}
			return true;
		}

		/// <summary>
		/// 静态+动态模式：香驹衣强制后发可见（覆盖前发 BanElements/DisableRelativeType 限制）
		/// </summary>
		[HarmonyPostfix, HarmonyPatch(typeof(Game.Components.Avatar.Avatar), "CalcCanShowBackHair")]
		public static void Avatar_CalcCanShowBackHair_Postfix(Game.Components.Avatar.Avatar __instance, ref bool __result)
		{
			if (ju && juBack && __instance.Data.ClothDisplayId == 30011)
			{
				__result = true;
			}
		}

		/// <summary>
		/// Spine 动态模式：阻止 RefreshHatBackDisplay 禁用 HairBack 的 SkeletonGraphic
		/// </summary>
		[HarmonyPrefix, HarmonyPatch(typeof(Game.Components.Avatar.AvatarSkeleton), "RefreshHatBackDisplay")]
		public static bool RefreshHatBackDisplay_Prefix(AvatarData data)
		{
			if (ju && juBack && data.ClothDisplayId == 30011)
			{
				return false; // 跳过原方法
			}
			return true;
		}

	}


}
