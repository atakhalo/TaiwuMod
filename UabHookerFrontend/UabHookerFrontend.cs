using HarmonyLib;
using FrameWork;
using FrameWork.ModSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine;
using Spine.Unity;
using UnityEngine.UI;

namespace UabHooker
{
    [PluginConfig(pluginName: "UabHooker", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class UabHookerFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        // ← 三种替换规则
        private static Dictionary<string, string> _replaceUab = new Dictionary<string, string>(); // uab名 → 替换文件
        private static Dictionary<string, Dictionary<string, string>> _replaceImg = new Dictionary<string, Dictionary<string, string>>(); // bundle名 → { assetPath → 文件 }
        private static Dictionary<string, Dictionary<string, string>> _replaceSpineImg = new Dictionary<string, Dictionary<string, string>>(); // skel名称 → { 纹理名 → 文件 }

        // ← 日志开关
        private static bool logReplace = true;
        private static bool logEntryUab = false;
        private static bool logEntryImg = false;
        private static bool logEntrySpineImg = false;

        public override void Initialize()
        {
            MyUtils.modName = nameof(UabHooker);
            MyUtils.MyLog("Initialize");

            // 扫描所有启用mod的uabhook.xml
            ScanConfigs();

            harmony = Harmony.CreateAndPatchAll(typeof(UabHookerFrontendPlugin));

            MyUtils.MyLog($"初始化完成: Uab={_replaceUab.Count}, Img={_replaceImg.Sum(kv=>kv.Value.Count)}, SpineImg={_replaceSpineImg.Sum(kv=>kv.Value.Count)}");
        }

        // ═══════════════════════════════════════════════════════════════
        //  XML 配置扫描与解析
        // ═══════════════════════════════════════════════════════════════

        public void ScanConfigs()
        {
            foreach (var mod in ModManager.EnabledMods)
            {
                var modInfo = ModManager.GetModInfo(mod);
                string configPath = Path.Combine(modInfo.DirectoryName, "uabhook.xml");
                if (!File.Exists(configPath)) continue;
                ParseConfig(configPath, modInfo.DirectoryName);
            }
        }

        private static string ResolveToPath(string to, string baseDir)
        {
            if (string.IsNullOrEmpty(to) || Path.IsPathRooted(to)) return to;
            return Path.GetFullPath(Path.Combine(baseDir, to));
        }

        public void ParseConfig(string configPath, string baseDir)
        {
            XDocument doc = XDocument.Load(configPath);
            XElement root = doc.Root;

            // HookUab: 整包替换
            foreach (var hook in root.Elements("HookUab"))
            {
                foreach (var uab in hook.Elements("uab"))
                {
                    string name = (string)uab.Attribute("name") ?? "";
                    string to = ResolveToPath((string)uab.Attribute("to") ?? "", baseDir);
                    bool enable = (bool?)uab.Attribute("enable") ?? true;
                    if (!enable || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(to)) continue;
                    _replaceUab[name] = to;
                    MyUtils.MyLog($"配置[HookUab] {name} -> {to}");
                }
            }

            // HookImg: 单图替换（走TryGetAssetBundleLoadData）
            foreach (var hook in root.Elements("HookImg"))
            {
                foreach (var uab in hook.Elements("uab"))
                {
                    string bundleName = (string)uab.Attribute("name") ?? "";
                    string to = ResolveToPath((string)uab.Attribute("to") ?? "", baseDir);
                    bool enable = (bool?)uab.Attribute("enable") ?? true;
                    if (!enable || string.IsNullOrEmpty(bundleName)) continue;

                    if (!string.IsNullOrEmpty(to))
                    {
                        _replaceUab[bundleName] = to;
                        MyUtils.MyLog($"配置[HookImg->整包] {bundleName} -> {to}");
                        continue;
                    }

                    if (!_replaceImg.TryGetValue(bundleName, out var map))
                        _replaceImg[bundleName] = map = new Dictionary<string, string>();

                    foreach (var img in uab.Elements("img"))
                    {
                        string assetPath = (string)img.Attribute("assetPath") ?? "";
                        string imgTo = ResolveToPath((string)img.Attribute("to") ?? "", baseDir);
                        bool imgEnable = (bool?)img.Attribute("enable") ?? true;
                        if (!imgEnable || string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(imgTo)) continue;
                        {
                            map[assetPath] = imgTo;
                            MyUtils.MyLog($"配置[HookImg] [{bundleName}] {assetPath} -> {imgTo}");
                        }
                    }
                }
            }

            // HookSpineImg: Spine图片替换
            foreach (var hook in root.Elements("HookSpineImg"))
            {
                foreach (var skel in hook.Elements("skel"))
                {
                    string skelName = (string)skel.Attribute("name") ?? "";
                    bool enable = (bool?)skel.Attribute("enable") ?? true;
                    if (!enable || string.IsNullOrEmpty(skelName)) continue;

                    if (!_replaceSpineImg.TryGetValue(skelName, out var map))
                        _replaceSpineImg[skelName] = map = new Dictionary<string, string>();

                    foreach (var img in skel.Elements("img"))
                    {
                        string imgName = (string)img.Attribute("name") ?? "";
                        string to = ResolveToPath((string)img.Attribute("to") ?? "", baseDir);
                        bool imgEnable = (bool?)img.Attribute("enable") ?? true;
                        if (!imgEnable || string.IsNullOrEmpty(imgName) || string.IsNullOrEmpty(to)) continue;
                        {
                            map[imgName] = to;
                            MyUtils.MyLog($"配置[HookSpineImg] [{skelName}] {imgName} -> {to}");
                        }
                    }
                }
            }
        }

        public override void Dispose() { harmony?.UnpatchSelf(); }
        public override void OnModSettingUpdate()
        {
            ModManager.GetSetting(ModIdStr, "logReplace", ref logReplace);
            ModManager.GetSetting(ModIdStr, "logEntryUab", ref logEntryUab);
            ModManager.GetSetting(ModIdStr, "logEntryImg", ref logEntryImg);
            ModManager.GetSetting(ModIdStr, "logEntrySpineImg", ref logEntrySpineImg);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Hook 1: LoadFromFile — 整包替换
        // ═══════════════════════════════════════════════════════════════

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AssetBundle), "LoadFromFile", new Type[] { typeof(string) })]
        public static void Prefix_LoadFromFile(ref string path)
        {
            if (string.IsNullOrEmpty(path) || _replaceUab.Count == 0)
            { if (logEntryUab && !string.IsNullOrEmpty(path)) MyUtils.MyLog("[HookUab] 入口: path=" + path); return; }
            if (logEntryUab) MyUtils.MyLog("[HookUab] 入口: path=" + path);

            if (_replaceUab.TryGetValue(path, out string rp))
            { if (logReplace) MyUtils.MyLog("[HookUab] 替换: " + path + " -> " + rp); path = rp; return; }

            string fn = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(fn) && _replaceUab.TryGetValue(fn, out rp))
            { if (logReplace) MyUtils.MyLog("[HookUab] 替换: " + path + " -> " + rp); path = rp; }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Hook 2: TryGetAssetBundleLoadData — 单图替换
        // ═══════════════════════════════════════════════════════════════

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FrameWork.AssetBundlePackage.ResourcePackage), "TryGetAssetBundleLoadData",
            new Type[] { typeof(Type), typeof(List<string>), typeof(string), typeof(string) })]
        public static bool ResourcePackage_Pre(Type type, List<string> dependenceList, string assetPath, string assetName,
            ref ValueTuple<FrameWork.AssetBundlePackage.ResourcePackage, string, UnityEngine.Object> __result)
        {
            if (_replaceImg.Count == 0) { if (logEntryImg && !string.IsNullOrEmpty(assetPath)) MyUtils.MyLog("[HookImg] 入口: assetPath=" + assetPath); return true; }
            if (logEntryImg && !string.IsNullOrEmpty(assetPath)) MyUtils.MyLog("[HookImg] 入口: assetPath=" + assetPath + " assetName=" + assetName);

            // 尝试匹配完整 assetPath
            if (!string.IsNullOrEmpty(assetPath))
            {
                foreach (var bundleKv in _replaceImg)
                {
                    if (bundleKv.Value.TryGetValue(assetPath, out string f))
                    {
                        var r = LoadRep(f, type);
                        if (r != null) { if (logReplace) MyUtils.MyLog("[HookImg] 替换: " + assetPath + " -> " + f); __result = new ValueTuple<FrameWork.AssetBundlePackage.ResourcePackage, string, UnityEngine.Object>(null, null, r); return false; }
                    }
                }
                // 也匹配短名
                string shortName = Path.GetFileName(assetPath);
                if (shortName != assetPath)
                {
                    foreach (var bundleKv in _replaceImg)
                    {
                        if (bundleKv.Value.TryGetValue(shortName, out string f))
                        {
                            var r = LoadRep(f, type);
                            if (r != null) { if (logReplace) MyUtils.MyLog("[HookImg] 替换(短名): " + shortName + " -> " + f); __result = new ValueTuple<FrameWork.AssetBundlePackage.ResourcePackage, string, UnityEngine.Object>(null, null, r); return false; }
                        }
                    }
                }
            }

            return true;
        }

		// ═══════════════════════════════════════════════════════════════
		//  Hook 3: SkeletonGraphic.Initialize — Spine图片替换
		// ═══════════════════════════════════════════════════════════════

		[HarmonyPrefix]
		[HarmonyPatch(typeof(SkeletonGraphic), "Initialize")]
		public static void SkeletonGraphic_Pre(SkeletonGraphic __instance)
        {
            if (_replaceSpineImg.Count == 0 || __instance == null) { if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 入口: cnt=0/null"); return; }

            var tra = Traverse.Create(__instance);
            var sda = tra.Property("SkeletonDataAsset").GetValue<SkeletonDataAsset>();
            if (sda == null) { if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 入口: sda=null"); return; }

            string sdaName = sda.name;
			var mainTexture = tra.Property("mainTexture").GetValue<Texture2D>();
			if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 入口: sda=" + (sdaName ?? "null") + " tex=" + (mainTexture?.name ?? "null"));
            if (string.IsNullOrEmpty(sdaName)) return;
			if (mainTexture == null) return;

			// 按skel名称匹配
			foreach (var skelKv in _replaceSpineImg)
            {
                if (sdaName.IndexOf(skelKv.Key, StringComparison.OrdinalIgnoreCase) < 0) continue;

				var sdaTra = Traverse.Create(sda);
				if (skelKv.Value.TryGetValue(mainTexture.name, out string pngFile))
				{
					if (mainTexture.name.StartsWith("uabhook_"))
					{ if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 已替换跳过: " + mainTexture.name); continue; }

					Texture2D newTex = new Texture2D(2, 2);
					byte[] bytes = File.ReadAllBytes(pngFile);
					if (!newTex.LoadImage(bytes)) continue;
					newTex.name = "uabhook_" + mainTexture.name;
					__instance.OverrideTexture = newTex;
					if (logReplace) MyUtils.MyLog("[HookSpineImg] 替换: [" + skelKv.Key + "] " + mainTexture.name + " -> " + __instance.OverrideTexture.name);

					// 替换 atlasAssets 中所有材质的纹理（子mesh的材质来源）
					// 关键材质如 "avatar_6_hair1_17_Material" (shader: Spine/Skeleton)
					object atlasAssets = sdaTra.Field("atlasAssets").GetValue();
					if (atlasAssets == null) continue;
					Array arr = atlasAssets as Array;
					if (arr != null)
					{
						for (int ai = 0; ai < arr.Length; ai++)
						{
							var aa = arr.GetValue(ai);
							if (aa == null) continue;
							var aaT = Traverse.Create(aa);
							// 替换 materials 数组
							var aaMats = aaT.Field("materials").GetValue();
							if (aaMats is Array matArr)
							{
								for (int mi = 0; mi < matArr.Length; mi++)
								{
									var m = matArr.GetValue(mi) as Material;
									if (m != null && m.mainTexture != null && m.mainTexture.name == mainTexture.name)
										m.mainTexture = newTex;
								}
							}
							// 替换 PrimaryMaterial
							var pm = aaT.Property("PrimaryMaterial").GetValue() as Material;
							if (pm != null && pm.mainTexture != null && pm.mainTexture.name == mainTexture.name)
								pm.mainTexture = newTex;
						}
					}
				}
			}
        }

        private static UnityEngine.Object LoadRep(string f, Type t)
        {
            if (!File.Exists(f)) return null;
            byte[] b; try { b = File.ReadAllBytes(f); } catch { return null; }
            if (t == typeof(Texture2D) || t == null) { var tx = new Texture2D(2, 2); if (tx.LoadImage(b)) return tx; }
            if (t == typeof(Sprite)) { var tx = new Texture2D(2, 2); if (tx.LoadImage(b)) return Sprite.Create(tx, new Rect(0, 0, tx.width, tx.height), new Vector2(0.5f, 0.5f)); }
            if (t == typeof(TextAsset)) return new TextAsset(System.Text.Encoding.UTF8.GetString(b));
            var fb = new Texture2D(2, 2); if (fb.LoadImage(b)) return fb; return null;
        }
    }
}
