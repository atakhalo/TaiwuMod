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
using UnityEngine.U2D;
using Spine.Unity;
using UnityEngine.UI;

namespace UabHooker
{
    [PluginConfig(pluginName: "UabHooker", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class UabHookerFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        // ← 替换规则（源数据，含 enable 条件）
        private static Dictionary<string, FileReplaceInfo> _replaceUab = new Dictionary<string, FileReplaceInfo>();
        private static Dictionary<string, Dictionary<string, FileReplaceInfo>> _replaceImg = new Dictionary<string, Dictionary<string, FileReplaceInfo>>();
        private static Dictionary<string, Dictionary<string, FileReplaceInfo>> _replaceSpineImg = new Dictionary<string, Dictionary<string, FileReplaceInfo>>();

        // ← 运行时有效替换（按 enable 条件过滤后的快照）
        private static Dictionary<string, FileReplaceInfo> _activeUab = new Dictionary<string, FileReplaceInfo>();
        private static Dictionary<string, Dictionary<string, FileReplaceInfo>> _activeImg = new Dictionary<string, Dictionary<string, FileReplaceInfo>>();
        private static Dictionary<string, Dictionary<string, FileReplaceInfo>> _activeSpineImg = new Dictionary<string, Dictionary<string, FileReplaceInfo>>();
        private static Dictionary<string, Dictionary<string, List<SpriteReplaceInfo>>> _activeAtlas = new Dictionary<string, Dictionary<string, List<SpriteReplaceInfo>>>(); // 运行时有效快照

        // ← 图集精灵源数据（含 enable 条件）
        private static Dictionary<string, Dictionary<string, List<SpriteReplaceInfo>>> _sourceAtlas = new Dictionary<string, Dictionary<string, List<SpriteReplaceInfo>>>();

        // ← 启用条件评估
        private class EnableCondition
        {
            public enum CondType { Static, Dropdown, Toggle }
            public CondType type = CondType.Static;
            public bool staticValue = true;
            public string modIdStr = "";
            public string key = "";
            public int idx = 0;

            public bool IsEnabled()
            {
                switch (type)
                {
                    case CondType.Static: return staticValue;
                    case CondType.Toggle:
                        bool boolVal = false;
                        ModManager.GetSetting(modIdStr, key, ref boolVal);
                        return boolVal;
                    case CondType.Dropdown:
                        int intVal = 0;
                        ModManager.GetSetting(modIdStr, key, ref intVal);
                        return intVal == idx;
                }
                return true;
            }
        }

        // ← 通用文件替换信息
        private class FileReplaceInfo
        {
            public string filePath = "";
            public EnableCondition enableCond = new EnableCondition();
        }

        // ← 精灵替换信息（含可选的 w/h 尺寸、坐标）
        private class SpriteReplaceInfo : FileReplaceInfo
        {
            public int w = -1; // -1 表示不指定，使用原始尺寸
            public int h = -1;
            public bool hasPos = false; // 是否指定了 posx/posy
            public float posX = 0;
            public float posY = 0;
        }
        // ← 贴图缓存（文件路径 → Texture2D）
        private static Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        // ← 日志开关
        private static bool logReplace = true;
        private static bool logEntryUab = false;
        private static bool logEntryImg = false;
        private static bool logEntrySpineImg = false;
        private static bool logEntryAtlas = false;

        public override void Initialize()
        {
            MyUtils.modName = nameof(UabHooker);
            MyUtils.MyLog("Initialize");

            // 扫描所有启用mod的uabhook.xml
            ScanConfigs();

            harmony = Harmony.CreateAndPatchAll(typeof(UabHookerFrontendPlugin));

            MyUtils.MyLog($"初始化完成: Uab={_replaceUab.Count}, Img={_replaceImg.Sum(kv=>kv.Value.Count)}, SpineImg={_replaceSpineImg.Sum(kv=>kv.Value.Count)}, Atlas={_sourceAtlas.Count}");

            // 延迟一帧刷新有效条目，等所有 mod 设置还原完成
            GameApp.Instance.StartCoroutine(DelayedRebuild());
        }

        // ═══════════════════════════════════════════════════════════════
        //  XML 配置扫描与解析
        // ═══════════════════════════════════════════════════════════════

        public void ScanConfigs()
        {
            foreach (var mod in ModManager.EnabledMods)
            {
                var modInfo = ModManager.GetModInfo(mod);
                if (modInfo == null) continue;
                string configPath = Path.Combine(modInfo.DirectoryName, "uabhook.xml");
                if (!File.Exists(configPath)) continue;
                string modIdStr = mod.ToString();
                _watchedModIdStrs.Add(modIdStr); // 记录关注的 mod
                ParseConfig(configPath, modInfo.DirectoryName, modIdStr);
            }
        }

        private static EnableCondition ParseEnableCondition(string raw, string modIdStr)
        {
            var cond = new EnableCondition();
            if (string.IsNullOrEmpty(raw))
            {
                cond.staticValue = true;
                return cond;
            }
            if (raw.StartsWith("Dropdown:"))
            {
                var parts = raw.Split(':');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int idx))
                {
                    cond.type = EnableCondition.CondType.Dropdown;
                    cond.modIdStr = modIdStr;
                    cond.key = parts[1];
                    cond.idx = idx;
                    return cond;
                }
            }
            else if (raw.StartsWith("Toggle:"))
            {
                var toggles = raw.Split(':');
                if (toggles.Length >= 2)
                {
                    cond.type = EnableCondition.CondType.Toggle;
                    cond.modIdStr = modIdStr;
                    cond.key = toggles[1];
                    return cond;
                }
            }
            // 静态 bool
            if (bool.TryParse(raw, out bool b))
                cond.staticValue = b;
            return cond;
        }

        private static string ResolveToPath(string to, string baseDir)
        {
            if (string.IsNullOrEmpty(to) || Path.IsPathRooted(to)) return to;
            return Path.GetFullPath(Path.Combine(baseDir, to));
        }

        public void ParseConfig(string configPath, string baseDir, string modIdStr)
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
                    string enableRaw = (string)uab.Attribute("enable") ?? "true";
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(to)) continue;
                    var info = new FileReplaceInfo { filePath = to };
                    info.enableCond = ParseEnableCondition(enableRaw, modIdStr);
                    if (info.enableCond.type == EnableCondition.CondType.Static && !info.enableCond.staticValue)
                        continue;
                    _replaceUab[name] = info;
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
                    string uabEnableRaw = (string)uab.Attribute("enable") ?? "true";
                    if (string.IsNullOrEmpty(bundleName)) continue;

                    var uabInfo = new FileReplaceInfo { filePath = to };
                    uabInfo.enableCond = ParseEnableCondition(uabEnableRaw, modIdStr);
                    if (uabInfo.enableCond.type == EnableCondition.CondType.Static && !uabInfo.enableCond.staticValue)
                        continue;

                    if (!string.IsNullOrEmpty(to))
                    {
                        _replaceUab[bundleName] = uabInfo;
                        MyUtils.MyLog($"配置[HookImg->整包] {bundleName} -> {to}");
                        continue;
                    }

                    if (!_replaceImg.TryGetValue(bundleName, out var map))
                        _replaceImg[bundleName] = map = new Dictionary<string, FileReplaceInfo>();

                    foreach (var img in uab.Elements("img"))
                    {
                        string assetPath = (string)img.Attribute("assetPath") ?? "";
                        string imgTo = ResolveToPath((string)img.Attribute("to") ?? "", baseDir);
                        string imgEnableRaw = (string)img.Attribute("enable") ?? "true";
                        if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(imgTo)) continue;
                        var imgInfo = new FileReplaceInfo { filePath = imgTo };
                        imgInfo.enableCond = ParseEnableCondition(imgEnableRaw, modIdStr);
                        if (imgInfo.enableCond.type == EnableCondition.CondType.Static && !imgInfo.enableCond.staticValue)
                            continue;
                        map[assetPath] = imgInfo;
                        MyUtils.MyLog($"配置[HookImg] [{bundleName}] {assetPath} -> {imgTo}");
                    }
                }
            }

            // HookSpineImg: Spine图片替换
            foreach (var hook in root.Elements("HookSpineImg"))
            {
                foreach (var skel in hook.Elements("skel"))
                {
                    string skelName = (string)skel.Attribute("name") ?? "";
                    string skelEnableRaw = (string)skel.Attribute("enable") ?? "true";
                    if (string.IsNullOrEmpty(skelName)) continue;

                    var skelInfo = new FileReplaceInfo();
                    skelInfo.enableCond = ParseEnableCondition(skelEnableRaw, modIdStr);
                    if (skelInfo.enableCond.type == EnableCondition.CondType.Static && !skelInfo.enableCond.staticValue)
                        continue;

                    if (!_replaceSpineImg.TryGetValue(skelName, out var map))
                        _replaceSpineImg[skelName] = map = new Dictionary<string, FileReplaceInfo>();

                    foreach (var img in skel.Elements("img"))
                    {
                        string imgName = (string)img.Attribute("name") ?? "";
                        string to = ResolveToPath((string)img.Attribute("to") ?? "", baseDir);
                        string imgEnableRaw = (string)img.Attribute("enable") ?? "true";
                        if (string.IsNullOrEmpty(imgName) || string.IsNullOrEmpty(to)) continue;
                        var imgInfo = new FileReplaceInfo { filePath = to };
                        imgInfo.enableCond = ParseEnableCondition(imgEnableRaw, modIdStr);
                        if (imgInfo.enableCond.type == EnableCondition.CondType.Static && !imgInfo.enableCond.staticValue)
                            continue;
                        map[imgName] = imgInfo;
                        MyUtils.MyLog($"配置[HookSpineImg] [{skelName}] {imgName} -> {to}");
                    }
                }
            }

            // HookAtlas: 图集精灵替换（替换 SpriteAtlas.GetSprite 返回的精灵）
            foreach (var hook in root.Elements("HookAtlas"))
            {
                foreach (var atlas in hook.Elements("atlas"))
                {
                    string atlasName = (string)atlas.Attribute("name") ?? "";
                    bool atlasEnable = (bool?)atlas.Attribute("enable") ?? true;
                    if (!atlasEnable || string.IsNullOrEmpty(atlasName)) continue;

                    if (!_sourceAtlas.TryGetValue(atlasName, out var map))
                        _sourceAtlas[atlasName] = map = new Dictionary<string, List<SpriteReplaceInfo>>();

                    foreach (var sprite in atlas.Elements("sprite"))
                    {
                        string spriteName = (string)sprite.Attribute("name") ?? "";
                        string to = ResolveToPath((string)sprite.Attribute("to") ?? "", baseDir);
                        string enableRaw = (string)sprite.Attribute("enable") ?? "true";
                        if (string.IsNullOrEmpty(spriteName) || string.IsNullOrEmpty(to)) continue;

                        var info = new SpriteReplaceInfo { filePath = to };
                        info.enableCond = ParseEnableCondition(enableRaw, modIdStr);
                        // 静态 false 直接跳过
                        if (info.enableCond.type == EnableCondition.CondType.Static && !info.enableCond.staticValue)
                            continue;
                        int wVal, hVal;
                        float px, py;
                        if (sprite.Attribute("w") != null && int.TryParse((string)sprite.Attribute("w"), out wVal))
                            info.w = wVal;
                        if (sprite.Attribute("h") != null && int.TryParse((string)sprite.Attribute("h"), out hVal))
                            info.h = hVal;
                        if (sprite.Attribute("posx") != null && float.TryParse((string)sprite.Attribute("posx"), out px))
                        {
                            info.hasPos = true; info.posX = px;
                        }
                        if (sprite.Attribute("posy") != null && float.TryParse((string)sprite.Attribute("posy"), out py))
                        {
                            info.hasPos = true; info.posY = py;
                        }

                        // 添加到列表，支持同名不同条件
                        if (!map.TryGetValue(spriteName, out var list))
                            map[spriteName] = list = new List<SpriteReplaceInfo>();
                        list.Add(info);
                        string extInfo = "";
                        if (info.w > 0 && info.h > 0) extInfo += $" w={info.w} h={info.h}";
                        if (info.hasPos) extInfo += $" pos({info.posX},{info.posY})";
                        MyUtils.MyLog($"配置[HookAtlas] [{atlasName}] {spriteName} -> {to}{extInfo}");
                    }
                }
            }
        }

        // ← 记录我们关注的 mod 的 ModId 字符串，用于 UpdateModSettingsInGame 过滤
        private static HashSet<string> _watchedModIdStrs = new HashSet<string>();

        public override void Dispose() { harmony?.UnpatchSelf(); }
        public override void OnModSettingUpdate()
        {
            ModManager.GetSetting(ModIdStr, "logReplace", ref logReplace);
            ModManager.GetSetting(ModIdStr, "logEntryUab", ref logEntryUab);
            ModManager.GetSetting(ModIdStr, "logEntryImg", ref logEntryImg);
            ModManager.GetSetting(ModIdStr, "logEntrySpineImg", ref logEntrySpineImg);
            ModManager.GetSetting(ModIdStr, "logEntryAtlas", ref logEntryAtlas);

            // 首次启动时延迟重建，等所有 mod 设置还原
            GameApp.Instance.StartCoroutine(DelayedRebuild());
        }

        private static IEnumerator DelayedRebuild()
        {
            yield return null;
            RebuildActiveEntries();
        }

        /// <summary>
        /// Hook ModManager.UpdateModSettingsInGame —— 当游戏内任何 mod 的设置发生变更时触发。
        /// 根据 modId 过滤 _watchedModIdStrs，只在我们关注的 mod 上重建。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModManager), "UpdateModSettingsInGame")]
        public static void OnUpdateModSettingsInGame(GameData.Domains.Mod.ModId modId)
        {
            string key = modId.ToString();
            if (_watchedModIdStrs.Contains(key))
            {
                MyUtils.MyLog($"[Hook] 检测到关注的 mod 设置变化: {key}，重建有效条目");
                RebuildActiveEntries();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  运行时快照刷新 — 在设置变化时按 enable 条件过滤出有效条目
        // ═══════════════════════════════════════════════════════════════

        private static void RebuildActiveEntries()
        {
            // _replaceUab
            _activeUab.Clear();
            foreach (var kv in _replaceUab)
                if (kv.Value.enableCond.IsEnabled())
                    _activeUab[kv.Key] = kv.Value;

            // _replaceImg
            _activeImg.Clear();
            foreach (var bundleKv in _replaceImg)
            {
                var inner = new Dictionary<string, FileReplaceInfo>();
                foreach (var kv in bundleKv.Value)
                    if (kv.Value.enableCond.IsEnabled())
                        inner[kv.Key] = kv.Value;
                if (inner.Count > 0)
                    _activeImg[bundleKv.Key] = inner;
            }

            // _replaceSpineImg
            _activeSpineImg.Clear();
            foreach (var skelKv in _replaceSpineImg)
            {
                var inner = new Dictionary<string, FileReplaceInfo>();
                foreach (var kv in skelKv.Value)
                    if (kv.Value.enableCond.IsEnabled())
                        inner[kv.Key] = kv.Value;
                if (inner.Count > 0)
                    _activeSpineImg[skelKv.Key] = inner;
            }

            // _sourceAtlas → _activeAtlas
            _activeAtlas.Clear();
            foreach (var atlasKv in _sourceAtlas)
            {
                var inner = new Dictionary<string, List<SpriteReplaceInfo>>();
                foreach (var spriteKv in atlasKv.Value)
                {
                    var list = new List<SpriteReplaceInfo>();
                    foreach (var info in spriteKv.Value)
                        if (info.enableCond.IsEnabled())
                            list.Add(info);
                    if (list.Count > 0)
                        inner[spriteKv.Key] = list;
                }
                if (inner.Count > 0)
                    _activeAtlas[atlasKv.Key] = inner;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Hook 1: LoadFromFile — 整包替换
        // ═══════════════════════════════════════════════════════════════

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AssetBundle), "LoadFromFile", new Type[] { typeof(string) })]
        public static void Prefix_LoadFromFile(ref string path)
        {
            if (string.IsNullOrEmpty(path) || _activeUab.Count == 0)
            { if (logEntryUab && !string.IsNullOrEmpty(path)) MyUtils.MyLog("[HookUab] 入口: path=" + path); return; }
            if (logEntryUab) MyUtils.MyLog("[HookUab] 入口: path=" + path);

            if (_activeUab.TryGetValue(path, out var info))
            { if (logReplace) MyUtils.MyLog("[HookUab] 替换: " + path + " -> " + info.filePath); path = info.filePath; return; }

            string fn = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(fn) && _activeUab.TryGetValue(fn, out info))
            { if (logReplace) MyUtils.MyLog("[HookUab] 替换: " + path + " -> " + info.filePath); path = info.filePath; }
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
            if (_activeImg.Count == 0) { if (logEntryImg && !string.IsNullOrEmpty(assetPath)) MyUtils.MyLog("[HookImg] 入口: assetPath=" + assetPath + " type=" + type?.Name); return true; }
            if (logEntryImg) MyUtils.MyLog("[HookImg] 入口: assetPath=" + (assetPath ?? assetName) + " type=" + type?.Name);

            // 只替换我们支持的资源类型，其他类型放行
            if (type != typeof(Texture2D) && type != typeof(Sprite) && type != typeof(TextAsset) && type != null)
                return true;

            // 尝试匹配完整 assetPath
            if (!string.IsNullOrEmpty(assetPath))
            {
                foreach (var bundleKv in _activeImg)
                {
                    if (bundleKv.Value.TryGetValue(assetPath, out var imgInfo))
                    {
                        var r = LoadRep(imgInfo.filePath, type);
                        if (r != null) { if (logReplace) MyUtils.MyLog("[HookImg] 替换: " + assetPath + " -> " + imgInfo.filePath); __result = new ValueTuple<FrameWork.AssetBundlePackage.ResourcePackage, string, UnityEngine.Object>(null, null, r); return false; }
                    }
                }
                // 也匹配短名
                string shortName = Path.GetFileName(assetPath);
                if (shortName != assetPath)
                {
                    foreach (var bundleKv in _activeImg)
                    {
                        if (bundleKv.Value.TryGetValue(shortName, out var imgInfo))
                        {
                            var r = LoadRep(imgInfo.filePath, type);
                            if (r != null) { if (logReplace) MyUtils.MyLog("[HookImg] 替换(短名): " + shortName + " -> " + imgInfo.filePath); __result = new ValueTuple<FrameWork.AssetBundlePackage.ResourcePackage, string, UnityEngine.Object>(null, null, r); return false; }
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
            if (_activeSpineImg.Count == 0 || __instance == null) { if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 入口: cnt=0/null"); return; }

            var tra = Traverse.Create(__instance);
            var sda = tra.Property("SkeletonDataAsset").GetValue<SkeletonDataAsset>();
            if (sda == null) { if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 入口: sda=null"); return; }

            string sdaName = sda.name;
			var mainTexture = tra.Property("mainTexture").GetValue<Texture2D>();
			if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 入口: sda=" + (sdaName ?? "null") + " tex=" + (mainTexture?.name ?? "null"));
            if (string.IsNullOrEmpty(sdaName)) return;
			if (mainTexture == null) return;

			// 按skel名称匹配
			foreach (var skelKv in _activeSpineImg)
            {
                if (sdaName.IndexOf(skelKv.Key, StringComparison.OrdinalIgnoreCase) < 0) continue;

				var sdaTra = Traverse.Create(sda);
				if (skelKv.Value.TryGetValue(mainTexture.name, out var spineInfo))
				{
					if (mainTexture.name.StartsWith("uabhook_"))
					{ if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 已替换跳过: " + mainTexture.name); continue; }

					Texture2D newTex = GetOrLoadTexture(spineInfo.filePath);
					if (newTex == null) continue;
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SpriteAtlas), "GetSprite", new Type[] { typeof(string) })]
        public static bool SpriteAtlas_GetSprite_Pre(SpriteAtlas __instance, string name, ref Sprite __result)
        {
            if (_activeAtlas.Count == 0) return true;
            string atlasName = __instance.name;
            if (logEntryAtlas) MyUtils.MyLog("[HookAtlas] 入口: atlas=" + atlasName + " sprite=" + name);

            if (_activeAtlas.TryGetValue(atlasName, out var sprites) && sprites.TryGetValue(name, out var list))
            {
                foreach (var info in list)
                {
                    Texture2D tex = GetOrLoadTexture(info.filePath);
                    if (tex == null) { if (logReplace) MyUtils.MyLog("[HookAtlas] 加载失败: " + info.filePath); continue; }

                    var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    sprite.name = name;
                    __result = sprite;
                    if (logReplace) MyUtils.MyLog("[HookAtlas] 替换: [" + atlasName + "] " + name + " -> " + info.filePath);
                    return false;
                }
            }
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Hook 5: AtlasInfo.SetImageSpriteOnly — CImage 精灵替换
        // ═══════════════════════════════════════════════════════════════

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AtlasInfo), "SetImageSpriteOnly", new Type[] { typeof(CImage), typeof(string) })]
        public static bool AtlasInfo_SetImageSpriteOnly_Pre(CImage image, string spriteName, ref bool __result)
        {
            if (_activeAtlas.Count == 0) return true;
            if (image == null || string.IsNullOrEmpty(spriteName)) return true;
            if (logEntryAtlas) MyUtils.MyLog("[HookAtlas] SetImageSpriteOnly 入口: sprite=" + spriteName);

            foreach (var atlasKv in _activeAtlas)
            {
                if (!atlasKv.Value.TryGetValue(spriteName, out var list)) continue;
                foreach (var info in list)
                {
                    Texture2D tex = GetOrLoadTexture(info.filePath);
                    if (tex == null)
                    {
                        if (logReplace) MyUtils.MyLog("[HookAtlas] SetImageSpriteOnly 加载失败: " + info.filePath);
                        continue;
                    }
                    var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    sprite.name = spriteName;
                    image.sprite = sprite;
                    if (info.w > 0 && info.h > 0)
                        image.rectTransform.sizeDelta = new Vector2(info.w, info.h);
                    else if (image.AutoSize)
                        image.SetNativeSize();
                    if (info.hasPos)
                        image.rectTransform.anchoredPosition = new Vector2(info.posX, info.posY);
                    image.SetEnabled(true);
                    __result = true;
                    if (logReplace) MyUtils.MyLog("[HookAtlas] SetImageSpriteOnly 替换: " + spriteName + " -> " + info.filePath + (info.w > 0 ? $" ({info.w}x{info.h})" : "") + (info.hasPos ? $" pos({info.posX},{info.posY})" : ""));
                    return false;
                }
            }
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  贴图缓存与加载工具
        // ═══════════════════════════════════════════════════════════════

        private static Texture2D GetOrLoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_texCache.TryGetValue(path, out var cached)) return cached;
            if (!File.Exists(path)) return null;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); } catch { return null; }
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes)) return null;
            tex.name = "uabhook_" + Path.GetFileNameWithoutExtension(path);
            _texCache[path] = tex;
            if (logReplace) MyUtils.MyLog("[缓存] 加载贴图: " + path);
            return tex;
        }

        private static UnityEngine.Object LoadRep(string f, Type t)
        {
            if (!File.Exists(f)) return null;
            byte[] b; try { b = File.ReadAllBytes(f); } catch { return null; }
            if (t == typeof(Texture2D) || t == null) { var tx = GetOrLoadTexture(f); return tx; }
            if (t == typeof(Sprite)) { var tx = GetOrLoadTexture(f); if (tx != null) return Sprite.Create(tx, new Rect(0, 0, tx.width, tx.height), new Vector2(0.5f, 0.5f)); }
            if (t == typeof(TextAsset)) return new TextAsset(System.Text.Encoding.UTF8.GetString(b));
            var fb = GetOrLoadTexture(f); return fb;
        }
    }
}
