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
using GameData.Domains.Mod;
using GameData.Domains.World;
using HarmonyLib;
using HarmonyLib.Tools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
using Game.Views.Loading;
//using UnityEngine.Windows;
//using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
//using static GameData.Domains.Item.ItemOperationType;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

namespace LoadBg
{
    [PluginConfig(pluginName: "LoadBg", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class LoadBgFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool hookLoad = true; // 开关
		public static bool keepOld = true; // 开关
		public static string customDir = "";
		public static bool useCustom = true; // 开关
		public static bool justCustom = false; // 开关
		public static bool hideDiwen = false; 

		private static List<string> imageFiles = new List<string>();
		private static Dictionary<string, Texture2D> _cachedBgs = new Dictionary<string, Texture2D>();

		public override void Initialize()
        {
			MyUtils.modName = nameof(LoadBg);
            harmony = Harmony.CreateAndPatchAll(typeof(LoadBgFrontendPlugin));
		}

		public override void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
            // 清理缓存的纹理
            foreach (var kv in _cachedBgs)
            {
                if (kv.Value != null)
                    UnityEngine.Object.DestroyImmediate(kv.Value);
            }
            _cachedBgs.Clear();
            imageFiles.Clear();
        }

        public override void OnModSettingUpdate()
        {
            ModManager.GetSetting(ModIdStr, "hookLoad", ref hookLoad);
			ModManager.GetSetting(ModIdStr, "keepOld", ref keepOld);
			ModManager.GetSetting(ModIdStr, "customDir", ref customDir);
			ModManager.GetSetting(ModIdStr, "useCustom", ref useCustom);
			ModManager.GetSetting(ModIdStr, "justCustom", ref justCustom);
			ModManager.GetSetting(ModIdStr, "hideDiwen", ref hideDiwen);
			MyUtils.MyLog($"{hookLoad}, {keepOld}, {customDir}, {useCustom}, {justCustom}, {hideDiwen}");

			ScanBg();
			if (UIElement.Loading.Exist)
			{
				var i = UIElement.Loading.UiBaseAs<ViewLoading>();
				if(i)
				{
					Patch_LoadRandomBg(i);
					Patch_OnEnable(i);
				}
			}
		}

		private void ScanBg()
		{
			// 清理缓存的纹理
			foreach (var kv in _cachedBgs)
			{
				if (kv.Value != null)
					UnityEngine.Object.DestroyImmediate(kv.Value);
			}
			_cachedBgs.Clear();
			imageFiles.Clear();

			// 获取自定义背景文件夹路径
			if(useCustom && customDir != "")
			{
				// 扫描图片文件（递归子文件夹）
				if (Directory.Exists(customDir))
				{
					imageFiles.AddRange(Directory.GetFiles(customDir, "*.*", SearchOption.AllDirectories)
						.Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
								|| f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
								|| f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
						.ToList());
				}
				// MyUtils.MyLog($"{customDir}");
			}
			if (!justCustom)
			{
				ModInfo modInfo = ModManager.GetModInfo(ModIdStr);
				string bgDir = Path.Combine(modInfo.DirectoryName, "MyLoad");

				// 扫描图片文件（递归子文件夹）
				if (Directory.Exists(bgDir))
				{
					imageFiles.AddRange(Directory.GetFiles(bgDir, "*.*", SearchOption.AllDirectories)
						.Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
								|| f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
								|| f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
						.ToList());
				}
				// MyUtils.MyLog($"扫描 MyLoad");

			}

			MyUtils.MyLog($"扫描到 {imageFiles.Count} 张图");
		}

		[HarmonyPrefix, HarmonyPatch(typeof(ViewLoading), "LoadRandomBg")]
		private static bool Patch_LoadRandomBg(ViewLoading __instance)
		{
			if (!hookLoad)
				return true; // 开关关闭，放行原方法

			if (imageFiles.Count == 0)
				return true; // 没有自定义图，放行原方法

			int orgCount = __instance.RandomBackgroundAmount;
			int myCount = imageFiles.Count;
			int poolCount = myCount;
			if(keepOld) poolCount += orgCount;

			var r = UnityEngine.Random.Range(0, poolCount);
			var isOrg = false;
			if(r >= myCount)
			{
				isOrg = true;
				r -= myCount;
			}

			if (isOrg)
			{
				// 选中原图 -> 加载指定索引的原图
				string resPath = string.Format("RemakeResources/Textures/UITexturesRemake/ui9_tex_loading_swordtomb_{0}", r);
				RawImage bgImg = Traverse.Create(__instance).Field("mainBg").GetValue<RawImage>();
				ResLoader.Load<Texture2D>(resPath, delegate(Texture2D tex)
				{
					if (tex != null && bgImg != null)
						bgImg.texture = tex;
				}, null);
				return false;
			}

			// 选中自定义图
			string selectedPath = imageFiles[r];
			if (!_cachedBgs.TryGetValue(selectedPath, out Texture2D cachedTex))
			{
				byte[] bytes = File.ReadAllBytes(selectedPath);
				cachedTex = new Texture2D(2, 2);
				if (!cachedTex.LoadImage(bytes))
				{
					// MyUtils.MyLog($"加载自定义背景失败: {selectedPath}");
					return true;
				}
				_cachedBgs[selectedPath] = cachedTex;
			}

			// 通过 Traverse 访问 ViewLoading 的私有字段 mainBg
			RawImage mainBg = Traverse.Create(__instance).Field("mainBg").GetValue<RawImage>();
			if (mainBg != null)
			{
				mainBg.texture = cachedTex;
				// MyUtils.MyLog($"已设置自定义背景: {selectedPath}");
				return false; // 跳过原方法
			}

			return true;
		}

		[HarmonyPostfix, HarmonyPatch(typeof(ViewLoading), "OnEnable")]
		private static void Patch_OnEnable(ViewLoading __instance)
		{
			var hide = hookLoad && hideDiwen;
			var a = Traverse.Create(__instance).Field("effFlowAnimation").GetValue<Animation>();
			if (a)
			{
				a.gameObject.SetActive(!hide);
			}
		}
	}
}
