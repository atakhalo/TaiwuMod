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
using Spine;
using Spine.Unity;
using UnityEngine.UI;
using Game.Components.Avatar;

namespace UabHooker
{
    [PluginConfig(pluginName: "UabHooker", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class UabHookerFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        // ← 替换规则（源数据，含 enable 条件，List 支持同 key 多条件条目）
        private static Dictionary<string, List<FileReplaceInfo>> _replaceUab = new Dictionary<string, List<FileReplaceInfo>>();
        private static Dictionary<string, Dictionary<string, List<FileReplaceInfo>>> _replaceImg = new Dictionary<string, Dictionary<string, List<FileReplaceInfo>>>();
        private static Dictionary<string, Dictionary<string, List<FileReplaceInfo>>> _replaceSpineImg = new Dictionary<string, Dictionary<string, List<FileReplaceInfo>>>();

        // ← 运行时有效替换（按 enable 条件过滤后的快照）
        private static Dictionary<string, FileReplaceInfo> _activeUab = new Dictionary<string, FileReplaceInfo>();
        private static Dictionary<string, Dictionary<string, FileReplaceInfo>> _activeImg = new Dictionary<string, Dictionary<string, FileReplaceInfo>>();
        private static Dictionary<string, Dictionary<string, FileReplaceInfo>> _activeSpineImg = new Dictionary<string, Dictionary<string, FileReplaceInfo>>();
        private static Dictionary<string, Dictionary<string, List<SpriteReplaceInfo>>> _activeAtlas = new Dictionary<string, Dictionary<string, List<SpriteReplaceInfo>>>(); // 运行时有效快照

        // ← 源数据：Spine 完整资源替换（hookSpine，atlas + skel，简单 SkeletonGraphic）
        // List 用于支持同一 name 多个条件条目（如不同 dropdown 索引的不同 spine 配置）
        private static Dictionary<string, List<SpineReplaceInfo>> _replaceSpine = new Dictionary<string, List<SpineReplaceInfo>>();
        // ← 运行时快照（hookSpine）
        private static Dictionary<string, SpineReplaceInfo> _activeSpine = new Dictionary<string, SpineReplaceInfo>();
        // ← 源数据：AvatarSpine 完整资源替换（hookAvatar，含 cover/coverKeep）
        // List 用于支持同一 name 多个条件条目
        private static Dictionary<string, List<SpineReplaceInfo>> _replaceAvatar = new Dictionary<string, List<SpineReplaceInfo>>();
        // ← 运行时快照（hookAvatar）
        private static Dictionary<string, SpineReplaceInfo> _activeAvatar = new Dictionary<string, SpineReplaceInfo>();
        // ← Spine 运行时缓存（避免反复创建 SkeletonDataAsset）
        private static Dictionary<string, SpineCachedAssets> _spineCache = new Dictionary<string, SpineCachedAssets>();

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

        // ← 通用文件替换信息（含可选的 w/h 尺寸，0=不指定）
        private class FileReplaceInfo
        {
            public string filePath = "";
            public EnableCondition enableCond = new EnableCondition();
            public int w = 0; // 0 表示不指定，使用原始尺寸
            public int h = 0;
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

        // ← Spine 完整资源替换信息（atlas + skel 文件路径）
        private class SpineReplaceInfo : FileReplaceInfo
        {
            public string atlasPath = ""; // .atlas 文件路径
            public string skelPath = "";  // .skel / .json 文件路径
            public bool coverKeep = false; // true=保留原始 cover，false=隐藏（默认）
            public string coverAtlasPath = ""; // cover .atlas 文件路径（不为空时替换 cover）
            public string coverSkelPath = "";  // cover .skel / .json 文件路径
            public string objDir = ""; // 可选：匹配 GameObject 名称或路径后缀（如 "NpcSpine" 或 "Body/NpcSpine"），用于区分重名 spine
        }

        // ← Spine 运行时资源缓存
        private class SpineCachedAssets
        {
            public SpineAtlasAsset AtlasAsset;
            public SkeletonDataAsset SkeletonAsset;
            public List<Texture2D> Textures = new List<Texture2D>();

            public void Destroy()
            {
                if (SkeletonAsset != null)
                {
                    UnityEngine.Object.Destroy(SkeletonAsset);
                    SkeletonAsset = null;
                }
                if (AtlasAsset != null)
                {
                    UnityEngine.Object.Destroy(AtlasAsset);
                    AtlasAsset = null;
                }
                foreach (var tex in Textures)
                {
                    if (tex != null)
                        UnityEngine.Object.Destroy(tex);
                }
                Textures.Clear();
            }
        }

        // ← 贴图缓存（文件路径 → Texture2D）
        private static Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        // ← 日志开关
        private static bool logScan = false;
		private static bool logReplace = true;
		private static bool logEntryUab = false;
        private static bool logEntryImg = false;
        private static bool logEntrySpineImg = false;
        private static bool logEntryAtlas = false;
        private static bool logEntrySpine = false;
        private static bool logEntryAvatar = false;

        public override void Initialize()
        {
            MyUtils.modName = nameof(UabHooker);
            MyUtils.MyLog("Initialize");

            // 扫描所有启用mod的uabhook.xml
            ScanConfigs();

            harmony = Harmony.CreateAndPatchAll(typeof(UabHookerFrontendPlugin));

            MyUtils.MyLog($"初始化完成: Uab={_replaceUab.Sum(kv=>kv.Value.Count)}, Img={_replaceImg.Sum(kv=>kv.Value.Sum(kv2=>kv2.Value.Count))}, SpineImg={_replaceSpineImg.Sum(kv=>kv.Value.Sum(kv2=>kv2.Value.Count))}, Spine={_replaceSpine.Sum(kv=>kv.Value.Count)}, Avatar={_replaceAvatar.Sum(kv=>kv.Value.Count)}, Atlas={_sourceAtlas.Sum(kv=>kv.Value.Sum(kv2=>kv2.Value.Count))}");

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
                    if (!_replaceUab.TryGetValue(name, out var list))
                        _replaceUab[name] = list = new List<FileReplaceInfo>();
                    list.Add(info);
                    if(logScan) MyUtils.MyLog($"配置[HookUab] {name}[{list.Count-1}] -> {to}");
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
                        if (!_replaceUab.TryGetValue(bundleName, out var list))
                            _replaceUab[bundleName] = list = new List<FileReplaceInfo>();
                        list.Add(uabInfo);
						if (logScan) MyUtils.MyLog($"配置[HookImg->整包] {bundleName}[{list.Count-1}] -> {to}");
                        continue;
                    }

                    if (!_replaceImg.TryGetValue(bundleName, out var map))
                        _replaceImg[bundleName] = map = new Dictionary<string, List<FileReplaceInfo>>();

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
                        int wVal, hVal;
                        if (img.Attribute("w") != null && int.TryParse((string)img.Attribute("w"), out wVal))
                            imgInfo.w = wVal;
                        if (img.Attribute("h") != null && int.TryParse((string)img.Attribute("h"), out hVal))
                            imgInfo.h = hVal;
                        if (!map.TryGetValue(assetPath, out var innerList))
                            map[assetPath] = innerList = new List<FileReplaceInfo>();
                        innerList.Add(imgInfo);
                        string sizeInfo = imgInfo.w > 0 && imgInfo.h > 0 ? $" w={imgInfo.w} h={imgInfo.h}" : "";
						if (logScan) MyUtils.MyLog($"配置[HookImg] [{bundleName}] {assetPath}[{innerList.Count-1}] -> {imgTo}{sizeInfo}");
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

                    if (!_replaceSpineImg.TryGetValue(skelName, out var skelMap))
                        _replaceSpineImg[skelName] = skelMap = new Dictionary<string, List<FileReplaceInfo>>();

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
                        if (!skelMap.TryGetValue(imgName, out var innerList))
                            skelMap[imgName] = innerList = new List<FileReplaceInfo>();
                        innerList.Add(imgInfo);
						if (logScan) MyUtils.MyLog($"配置[HookSpineImg] [{skelName}] {imgName}[{innerList.Count-1}] -> {to}");
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
						if (logScan) MyUtils.MyLog($"配置[HookAtlas] [{atlasName}] {spriteName} -> {to}{extInfo}");
                    }
                }
            }

            // HookSpine: Spine 完整资源替换（简单 SkeletonGraphic，atlas + skel）
            foreach (var hook in root.Elements("HookSpine"))
            {
                foreach (var spine in hook.Elements("spine"))
                {
                    string spineName = (string)spine.Attribute("name") ?? "";
                    string atlasTo = ResolveToPath((string)spine.Attribute("atlas") ?? "", baseDir);
                    string skelTo = ResolveToPath((string)spine.Attribute("skel") ?? "", baseDir);
                    string enableRaw = (string)spine.Attribute("enable") ?? "true";
                    if (string.IsNullOrEmpty(spineName) || string.IsNullOrEmpty(atlasTo) || string.IsNullOrEmpty(skelTo))
                        continue;

                    var info = new SpineReplaceInfo { atlasPath = atlasTo, skelPath = skelTo };
                    info.enableCond = ParseEnableCondition(enableRaw, modIdStr);
                    if (info.enableCond.type == EnableCondition.CondType.Static && !info.enableCond.staticValue)
                        continue;
                    info.objDir = (string)spine.Attribute("objDir") ?? "";

                    if (!_replaceSpine.TryGetValue(spineName, out var list))
                        _replaceSpine[spineName] = list = new List<SpineReplaceInfo>();
                    list.Add(info);
                    string objDirLog = string.IsNullOrEmpty(info.objDir) ? "" : $" objDir={info.objDir}";
					if (logScan) MyUtils.MyLog($"配置[HookSpine] {spineName}[{list.Count-1}] -> atlas={atlasTo}, skel={skelTo}{objDirLog}");
                }
            }

            // HookAvatar: AvatarSpine 完整资源替换（AvatarSkeleton，含 cover/coverKeep）
            foreach (var hook in root.Elements("HookAvatar"))
            {
                foreach (var spine in hook.Elements("spine"))
                {
                    string spineName = (string)spine.Attribute("name") ?? "";
                    string atlasTo = ResolveToPath((string)spine.Attribute("atlas") ?? "", baseDir);
                    string skelTo = ResolveToPath((string)spine.Attribute("skel") ?? "", baseDir);
                    string enableRaw = (string)spine.Attribute("enable") ?? "true";
                    if (string.IsNullOrEmpty(spineName) || string.IsNullOrEmpty(atlasTo) || string.IsNullOrEmpty(skelTo))
                        continue;

                    var info = new SpineReplaceInfo { atlasPath = atlasTo, skelPath = skelTo };
                    info.enableCond = ParseEnableCondition(enableRaw, modIdStr);
                    if (info.enableCond.type == EnableCondition.CondType.Static && !info.enableCond.staticValue)
                        continue;
                    info.objDir = (string)spine.Attribute("objDir") ?? "";

                    // coverkeep: true=保留原始 cover，不填或 false=隐藏 cover（默认）
                    string coverKeepRaw = (string)spine.Attribute("coverkeep") ?? "false";
                    bool.TryParse(coverKeepRaw, out info.coverKeep);

                    // coverAtlas/coverSkel: 自定义 cover 资源路径（不为空时替换 cover）
                    info.coverAtlasPath = ResolveToPath((string)spine.Attribute("coverAtlas") ?? "", baseDir);
                    info.coverSkelPath = ResolveToPath((string)spine.Attribute("coverSkel") ?? "", baseDir);

                    if (!_replaceAvatar.TryGetValue(spineName, out var list))
                        _replaceAvatar[spineName] = list = new List<SpineReplaceInfo>();
                    list.Add(info);
                    string logExtra = info.coverKeep ? " coverKeep=true" : "";
                    if (!string.IsNullOrEmpty(info.coverAtlasPath))
                        logExtra += " coverAtlas=" + info.coverAtlasPath;
                    if (!string.IsNullOrEmpty(info.coverSkelPath))
                        logExtra += " coverSkel=" + info.coverSkelPath;
                    string objDirLog = string.IsNullOrEmpty(info.objDir) ? "" : $" objDir={info.objDir}";
					if (logScan) MyUtils.MyLog($"配置[HookAvatar] {spineName}[{list.Count-1}] -> atlas={atlasTo}, skel={skelTo}" + logExtra + objDirLog);
                }
            }
        }

        // ← 记录我们关注的 mod 的 ModId 字符串，用于 UpdateModSettingsInGame 过滤
        private static HashSet<string> _watchedModIdStrs = new HashSet<string>();

        public override void Dispose() { harmony?.UnpatchSelf(); }
        public override void OnModSettingUpdate()
        {
            ModManager.GetSetting(ModIdStr, "logScan", ref logScan);
			ModManager.GetSetting(ModIdStr, "logReplace", ref logReplace);
			ModManager.GetSetting(ModIdStr, "logEntryUab", ref logEntryUab);
            ModManager.GetSetting(ModIdStr, "logEntryImg", ref logEntryImg);
            ModManager.GetSetting(ModIdStr, "logEntrySpineImg", ref logEntrySpineImg);
            ModManager.GetSetting(ModIdStr, "logEntryAtlas", ref logEntryAtlas);
            ModManager.GetSetting(ModIdStr, "logEntrySpine", ref logEntrySpine);
            ModManager.GetSetting(ModIdStr, "logEntryAvatar", ref logEntryAvatar);

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
            // _replaceUab（list 结构，取第一个 enable 的）
            _activeUab.Clear();
            foreach (var kv in _replaceUab)
                foreach (var info in kv.Value)
                    if (info.enableCond.IsEnabled())
                    {
                        _activeUab[kv.Key] = info;
                        break;
                    }

            // _replaceImg（内层 list，取第一个 enable 的）
            _activeImg.Clear();
            foreach (var bundleKv in _replaceImg)
            {
                var inner = new Dictionary<string, FileReplaceInfo>();
                foreach (var kv in bundleKv.Value)
                    foreach (var info in kv.Value)
                        if (info.enableCond.IsEnabled())
                        {
                            inner[kv.Key] = info;
                            break;
                        }
                if (inner.Count > 0)
                    _activeImg[bundleKv.Key] = inner;
            }

            // _replaceSpineImg（内层 list，取第一个 enable 的）
            _activeSpineImg.Clear();
            foreach (var skelKv in _replaceSpineImg)
            {
                var inner = new Dictionary<string, FileReplaceInfo>();
                foreach (var kv in skelKv.Value)
                    foreach (var info in kv.Value)
                        if (info.enableCond.IsEnabled())
                        {
                            inner[kv.Key] = info;
                            break;
                        }
                if (inner.Count > 0)
                    _activeSpineImg[skelKv.Key] = inner;
            }

            // _replaceSpine（list 结构，同 name 多条，取第一个 enable 的）
            _activeSpine.Clear();
            foreach (var kv in _replaceSpine)
                foreach (var info in kv.Value)
                    if (info.enableCond.IsEnabled())
                    {
                        _activeSpine[kv.Key] = info;
                        break;
                    }

            // _replaceAvatar（list 结构，同 name 多条，取第一个 enable 的）
            _activeAvatar.Clear();
            foreach (var kv in _replaceAvatar)
                foreach (var info in kv.Value)
                    if (info.enableCond.IsEnabled())
                    {
                        _activeAvatar[kv.Key] = info;
                        break;
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
            if (logEntryUab) MyUtils.MyLog("[HookUab] 入口: path=" + (path ?? "null"));

            if (string.IsNullOrEmpty(path) || _activeUab.Count == 0) return;

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
            // 如果 _activeImg 还未填充，立即构建
            if (_activeImg.Count == 0 && _replaceImg.Count > 0)
                RebuildActiveEntries();

            if (logEntryImg) MyUtils.MyLog("[HookImg] 入口: assetPath=" + (assetPath ?? "null") + " type=" + type?.Name + " activeKeys=" + string.Join(",", _activeImg.Keys));

            if (_activeImg.Count == 0) return true;

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
                        if (logEntryImg) MyUtils.MyLog("[HookImg] 匹配到: bundle=" + bundleKv.Key + " filePath=" + imgInfo.filePath + " exists=" + File.Exists(imgInfo.filePath));
                        var r = LoadRep(imgInfo.filePath, type, imgInfo.w, imgInfo.h);
                        if (r != null) { if (logReplace) MyUtils.MyLog("[HookImg] 替换: " + assetPath + " -> " + imgInfo.filePath); __result = new ValueTuple<FrameWork.AssetBundlePackage.ResourcePackage, string, UnityEngine.Object>(null, null, r); return false; }
                        else if (logEntryImg) MyUtils.MyLog("[HookImg] LoadRep失败: " + imgInfo.filePath);
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
                            if (logEntryImg) MyUtils.MyLog("[HookImg] 匹配到短名: bundle=" + bundleKv.Key + " filePath=" + imgInfo.filePath + " exists=" + File.Exists(imgInfo.filePath));
                            var r = LoadRep(imgInfo.filePath, type, imgInfo.w, imgInfo.h);
                            if (r != null) { if (logReplace) MyUtils.MyLog("[HookImg] 替换(短名): " + shortName + " -> " + imgInfo.filePath); __result = new ValueTuple<FrameWork.AssetBundlePackage.ResourcePackage, string, UnityEngine.Object>(null, null, r); return false; }
                        }
                    }
                }
            }

            if (logEntryImg) MyUtils.MyLog("[HookImg] 未匹配到任何条目: assetPath=" + assetPath);

            return true;
        }

		/// <summary>
		/// 检查 SkeletonGraphic 的 GameObject 名称或完整路径后缀是否匹配 objDir。
		/// 支持：
		///   - "NpcSpine" → 匹配任何名为 NpcSpine 的对象（包括自身）
		///   - "Body/NpcSpine" → 匹配路径以 Body/NpcSpine 结尾
		///   - "AvatarContainer/Body/NpcSpine" → 完整路径匹配
		/// </summary>
		private static bool MatchesObjDir(SkeletonGraphic sg, string objDir)
		{
			if (sg == null || string.IsNullOrEmpty(objDir)) return true; // 无限制
			// 1. 直接匹配当前对象名称
			if (sg.gameObject.name == objDir) return true;
			// 2. 构建从自身到根的完整路径（用 / 分隔），检查是否以 objDir 结尾
			var parts = new List<string>();
			var t = sg.transform;
			while (t != null)
			{
				parts.Add(t.name);
				t = t.parent;
			}
			parts.Reverse(); // 现在 parts = [root, ..., parent, self]
			string fullPath = string.Join("/", parts);
			if (fullPath.EndsWith(objDir, StringComparison.OrdinalIgnoreCase))
				return true;
			return false;
		}

		// ═══════════════════════════════════════════════════════════════
		//  Hook 3a: SkeletonGraphic.Initialize — Spine 完整资源替换（简单 SkeletonGraphic）
		//  替换 atlas + skel，强制 overwrite=true 确保 Skeleton + AnimationState 完整重建
		// ═══════════════════════════════════════════════════════════════

		[HarmonyPrefix]
		[HarmonyPriority(Priority.HigherThanNormal)]
		[HarmonyPatch(typeof(SkeletonGraphic), "Initialize", new Type[] { typeof(bool) })]
		public static void SkeletonGraphic_Pre_Spine(SkeletonGraphic __instance, ref bool overwrite)
        {
            if (__instance == null) return;

            // 如果 _activeSpine 还未填充（DelayedRebuild 延迟一帧），立即构建
            if (_activeSpine.Count == 0 && _replaceSpine.Count > 0)
                RebuildActiveEntries();

            if (logEntrySpine)
                MyUtils.MyLog($"[HookSpine] 入口: _activeSpine={_activeSpine.Count} _replaceSpine={_replaceSpine.Sum(kv=>kv.Value.Count)} instance={__instance.name}");
            if (_activeSpine.Count == 0) return;

            var sda = __instance.SkeletonDataAsset;
            if (sda == null)
            {
                if (logEntrySpine) MyUtils.MyLog("[HookSpine] sda=null, 跳过");
                return;
            }
            string sdaName = sda.name;
            if (string.IsNullOrEmpty(sdaName))
            {
                if (logEntrySpine) MyUtils.MyLog("[HookSpine] sda.name 为空, 跳过");
                return;
            }
            if (logEntrySpine) MyUtils.MyLog("[HookSpine] 入口: sda=" + sdaName);

            foreach (var kv in _activeSpine)
            {
                var info = kv.Value;

                // objDir 过滤：检查对象名/路径后缀（支持 "NpcSpine" 或 "Body/NpcSpine" 等）
                if (!MatchesObjDir(__instance, info.objDir))
                {
                    if (logEntrySpine) MyUtils.MyLog($"[HookSpine] objDir不匹配: need={info.objDir} 跳过 [{kv.Key}]");
                    continue;
                }

                // 用 sda.name 匹配
                if (sdaName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // cacheKey 包含文件路径，保证不同配置（同 name 不同文件）不命中旧缓存
                string cacheKey = kv.Key + "|" + info.atlasPath + "|" + info.skelPath;

                if (!_spineCache.TryGetValue(cacheKey, out var cached) || cached == null || cached.SkeletonAsset == null)
                {
                    if (logEntrySpine) MyUtils.MyLog("[HookSpine] 缓存未命中，加载资源: " + cacheKey);
                    var newAsset = LoadAndCacheSpineAsset(cacheKey, info.atlasPath, info.skelPath, __instance.material);
                    if (newAsset == null) continue;
                    cached = _spineCache[cacheKey];
                }

                __instance.skeletonDataAsset = cached.SkeletonAsset;
                __instance.Skeleton = null;
                Traverse.Create(__instance).Field("state").SetValue(null);
                overwrite = true;
                if (logReplace) MyUtils.MyLog("[HookSpine] 替换: [" + kv.Key + "] skeletonDataAsset -> " + cached.SkeletonAsset.name);
                break;
            }
        }

		// ═══════════════════════════════════════════════════════════════
		//  Hook 3b: AvatarSkeleton.SetupSkeletonGraphic — AvatarSpine 完整资源替换
		//  在 SetupSkeletonGraphic 阶段替换 skeletonDataAsset 参数，比 Hook Initialize 更干净
		// ═══════════════════════════════════════════════════════════════

		[HarmonyPrefix]
		[HarmonyPatch(typeof(AvatarSkeleton), "SetupSkeletonGraphic")]
		public static void SetupSkeletonGraphic_Pre(SkeletonGraphic target, ref SkeletonDataAsset skeletonDataAsset)
        {
            string sdaName = skeletonDataAsset?.name;
            if (logEntryAvatar) MyUtils.MyLog("[HookAvatar] SetupSkeletonGraphic 入口: sda=" + (sdaName ?? "null"));

            if (_activeAvatar.Count == 0 || skeletonDataAsset == null) return;
            if (string.IsNullOrEmpty(sdaName)) return;

            foreach (var kv in _activeAvatar)
            {
                var info = kv.Value;

                // objDir 过滤：检查对象名/路径后缀
                if (!MatchesObjDir(target, info.objDir))
                {
                    if (logEntryAvatar) MyUtils.MyLog($"[HookAvatar] objDir不匹配: need={info.objDir} 跳过 [{kv.Key}]");
                    continue;
                }

                // 用 sda.name 匹配
                if (sdaName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // cacheKey 包含文件路径，保证不同配置不命中旧缓存
                string cacheKey = kv.Key + "|" + info.atlasPath + "|" + info.skelPath;

                if (!_spineCache.TryGetValue(cacheKey, out var cached) || cached == null || cached.SkeletonAsset == null)
                {
                    if (logEntryAvatar) MyUtils.MyLog("[HookAvatar] 缓存未命中，加载资源: " + cacheKey);

                    // 从原始 asset 提取材质用于创建新 atlas
                    Material templateMat = GetMaterialFromSda(skeletonDataAsset);

                    var newAsset = LoadAndCacheSpineAsset(cacheKey, info.atlasPath, info.skelPath, templateMat);
                    if (newAsset == null) continue;
                    cached = _spineCache[cacheKey];
                }

                skeletonDataAsset = cached.SkeletonAsset;
                if (logReplace) MyUtils.MyLog("[HookAvatar] SetupSkeletonGraphic 替换: [" + kv.Key + "] skeletonDataAsset -> " + cached.SkeletonAsset.name);
                break;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Hook: AvatarSkeleton.Refresh Postfix — 刷新完成后重绑所有 BoneFollower
        //  BoneFollowerGraphic 不是 SkeletonGraphic 的子对象，而是 AvatarSkeleton 下的同级对象，
        //  所以不能在 SkeletonGraphic.Initialize 的 Postfix 中处理，必须在 AvatarSkeleton 层级处理
        // ═══════════════════════════════════════════════════════════════

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Game.Components.Avatar.AvatarSkeleton), "Refresh")]
        public static void AvatarSkeleton_Refresh_Post(AvatarSkeleton __instance)
        {
            if (_activeAvatar.Count == 0) return;

            // 1. 处理 clothingCover
            try
            {
                var cover = Traverse.Create(__instance).Field("clothingCover").GetValue<SkeletonGraphic>();
                if (cover != null)
                {
                    string coverName = cover.skeletonDataAsset?.name ?? "";
                    foreach (var kv in _activeAvatar)
                    {
                        if (!coverName.Contains(kv.Key) && !coverName.StartsWith(kv.Key))
                            continue;

                        var info = kv.Value;
                        string cacheKey = kv.Key + "_cover";

                        if (!string.IsNullOrEmpty(info.coverAtlasPath) && !string.IsNullOrEmpty(info.coverSkelPath))
                        {
                            var newAsset = LoadAndCacheSpineAsset(cacheKey, info.coverAtlasPath, info.coverSkelPath, cover.material);
                            if (newAsset != null)
                            {
                                cover.skeletonDataAsset = newAsset;
                                cover.Skeleton = null;
                                Traverse.Create(cover).Field("state").SetValue(null);
                                cover.Initialize(true);
                                cover.gameObject.SetActive(true);
                                if (logReplace)
                                    MyUtils.MyLog("[HookAvatar] 替换 clothingCover: " + coverName + " -> " + cacheKey);
                            }
                        }
                        else
                        {
                            if (!info.coverKeep)
                            {
                                if (cover.gameObject.activeSelf)
                                {
                                    cover.gameObject.SetActive(false);
                                    if (logReplace)
                                        MyUtils.MyLog("[HookAvatar] 隐藏 clothingCover: " + coverName + " (匹配 " + kv.Key + ")");
                                }
                            }
                            else if (logReplace && cover.gameObject.activeSelf)
                                MyUtils.MyLog("[HookAvatar] 保留 clothingCover: " + coverName + " (coverKeep=true)");
                        }
                        break;
                    }
                }
            }
            catch { }

            // 2. 重绑 BoneFollowerGraphic
            var followers = __instance.GetComponentsInChildren<BoneFollowerGraphic>(true);
            int bfCount = 0;
            foreach (var bf in followers)
            {
                if (bf == null || string.IsNullOrEmpty(bf.boneName))
                    continue;
                try
                {
                    bf.SetBone(bf.boneName);
                    bfCount++;
                }
                catch (Exception ex)
                {
                    MyUtils.MyLog("[HookAvatar] BoneFollower 重绑失败: " + bf.boneName + " - " + ex.Message);
                }
            }
            if (bfCount > 0)
			{
				if (logReplace)
					MyUtils.MyLog("[HookAvatar] 刷新后重绑 " + bfCount + " 个 BoneFollowerGraphic");
			}
        }

        /// <summary>
        /// 解析 Spine .atlas 文件内容，提取 page 贴图文件名列表（按顺序）
        /// </summary>
        private static List<string> ParseAtlasPageFileNames(string atlasContent)
        {
            var pages = new List<string>();
            using (var reader = new StringReader(atlasContent))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.Contains(':')) continue;
                    // page 贴图文件名总是包含图片扩展名
                    if (trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                    {
                        pages.Add(trimmed);
                    }
                }
            }
            return pages;
        }

		// ═══════════════════════════════════════════════════════════════
		//  Hook 3b: SkeletonGraphic.Initialize — Spine图片替换（纹理级）
		// ═══════════════════════════════════════════════════════════════

		[HarmonyPrefix]
		[HarmonyPatch(typeof(SkeletonGraphic), "Initialize", new Type[] { typeof(bool) })]
		public static void SkeletonGraphic_Pre(SkeletonGraphic __instance)
        {
            if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] 入口: instance=" + (__instance?.name ?? "null"));

            if (_activeSpineImg.Count == 0 || __instance == null) return;

            var tra = Traverse.Create(__instance);
            var sda = tra.Property("SkeletonDataAsset").GetValue<SkeletonDataAsset>();
            if (sda == null) { if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] sda=null, 跳过"); return; }

            string sdaName = sda.name;
			var mainTexture = tra.Property("mainTexture").GetValue<Texture2D>();
			if (logEntrySpineImg) MyUtils.MyLog("[HookSpineImg] sda=" + (sdaName ?? "null") + " tex=" + (mainTexture?.name ?? "null"));
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
            string atlasName = __instance?.name ?? "null";
            if (logEntryAtlas) MyUtils.MyLog("[HookAtlas] 入口: atlas=" + atlasName + " sprite=" + name);

            if (_activeAtlas.Count == 0) return true;

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
            if (logEntryAtlas) MyUtils.MyLog("[HookAtlas] SetImageSpriteOnly 入口: sprite=" + (spriteName ?? "null"));

            if (_activeAtlas.Count == 0) return true;
            if (image == null || string.IsNullOrEmpty(spriteName)) return true;

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
                    sprite.name = spriteName; // 注意sprite都是用原名，判断替换成功看贴图名
                    image.sprite = sprite;

					if (info.w > 0 && info.h > 0)
                        image.rectTransform.sizeDelta = new Vector2(info.w, info.h);
                    else if (image.AutoSize)
                        image.SetNativeSize();
                    if (info.hasPos)
                        image.rectTransform.anchoredPosition = new Vector2(info.posX, info.posY);
					image.OnSpriteChange?.Invoke();

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

        /// <summary>
        /// 从 SkeletonDataAsset 中提取模板材质
        /// </summary>
        private static Material GetMaterialFromSda(SkeletonDataAsset sda)
        {
            if (sda == null) return null;
            try
            {
                var sdaTra = Traverse.Create(sda);
                object atlasAssets = sdaTra.Field("atlasAssets").GetValue();
                if (atlasAssets is Array arr && arr.Length > 0)
                {
                    var aa = arr.GetValue(0);
                    if (aa != null)
                    {
                        var pm = Traverse.Create(aa).Property("PrimaryMaterial").GetValue() as Material;
                        if (pm != null) return pm;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 从文件加载 Spine 资源并缓存，返回 SkeletonDataAsset
        /// </summary>
        private static SkeletonDataAsset LoadAndCacheSpineAsset(string cacheKey, string atlasPath, string skelPath, Material templateMat = null)
        {
            if (_spineCache.TryGetValue(cacheKey, out var cached) && cached != null && cached.SkeletonAsset != null)
                return cached.SkeletonAsset;

            if (!File.Exists(atlasPath)) { MyUtils.MyLog("[HookSpine] atlas 文件不存在: " + atlasPath); return null; }
            if (!File.Exists(skelPath)) { MyUtils.MyLog("[HookSpine] skel 文件不存在: " + skelPath); return null; }

            string atlasContent;
            try { atlasContent = File.ReadAllText(atlasPath); }
            catch (Exception ex) { MyUtils.MyLog("[HookSpine] 读取 atlas 失败: " + ex.Message); return null; }

            string atlasDir = Path.GetDirectoryName(atlasPath);
            var pageFileNames = ParseAtlasPageFileNames(atlasContent);
            var textures = new List<Texture2D>();
            foreach (var pf in pageFileNames)
            {
                string pngName = Path.GetFileNameWithoutExtension(pf);
                string pngPath = Path.Combine(atlasDir, pf);
                if (!File.Exists(pngPath))
                {
                    pngPath = Path.Combine(atlasDir, pngName + ".png");
                    if (!File.Exists(pngPath)) continue;
                }
                var tex = new Texture2D(1, 1);
                try
                {
                    tex.LoadImage(File.ReadAllBytes(pngPath));
                    tex.name = pngName;
                    textures.Add(tex);
                }
                catch (Exception ex) { MyUtils.MyLog("[HookSpine] 加载贴图失败: " + pngPath + " - " + ex.Message); }
            }
            if (textures.Count == 0) { MyUtils.MyLog("[HookSpine] 未加载到任何贴图: " + atlasPath); return null; }

            var textAsset = new TextAsset(atlasContent);
            SpineAtlasAsset atlasAsset;
            try { atlasAsset = SpineAtlasAsset.CreateRuntimeInstance(textAsset, textures.ToArray(), templateMat, true); }
            catch (Exception ex) { MyUtils.MyLog("[HookSpine] 创建 SpineAtlasAsset 失败: " + ex.Message); return null; }

            SkeletonDataAsset skeletonAsset = null;
            try
            {
                if (skelPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    string jsonContent = File.ReadAllText(skelPath);
                    var jsonTextAsset = new TextAsset(jsonContent);
                    skeletonAsset = SkeletonDataAsset.CreateRuntimeInstance(jsonTextAsset, atlasAsset, false, 0.01f);
                }
                else
                {
                    var atlas = atlasAsset.GetAtlas();
                    if (atlas == null) { MyUtils.MyLog("[HookSpine] 获取 Atlas 失败"); return null; }
                    var binary = new SkeletonBinary(atlas);
                    binary.Scale = 0.01f;
                    var skeletonData = binary.ReadSkeletonData(skelPath);
                    skeletonAsset = SkeletonDataAsset.CreateRuntimeInstance(new TextAsset(), atlasAsset, false, 0.01f);
                    Traverse.Create(skeletonAsset).Field("skeletonData").SetValue(skeletonData);
                    Traverse.Create(skeletonAsset).Field("stateData").SetValue(new AnimationStateData(skeletonData));
                }
            }
            catch (Exception ex) { MyUtils.MyLog("[HookSpine] 创建 SkeletonDataAsset 失败: " + ex.Message); return null; }

            skeletonAsset.name = cacheKey;

            try
            {
                var verifyData = skeletonAsset.GetSkeletonData(false);
                if (verifyData == null) { MyUtils.MyLog("[HookSpine] 验证失败: GetSkeletonData 返回 null"); return null; }
            }
            catch (Exception ex) { MyUtils.MyLog("[HookSpine] 验证失败: GetSkeletonData 异常 - " + ex.Message); return null; }

            _spineCache[cacheKey] = new SpineCachedAssets
            {
                AtlasAsset = atlasAsset,
                SkeletonAsset = skeletonAsset,
                Textures = textures
            };
            if (logEntrySpine) MyUtils.MyLog("[HookSpine] 加载完成并缓存: " + cacheKey);
            return skeletonAsset;
        }

        private static Texture2D GetOrLoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_texCache.TryGetValue(path, out var cached)) return cached;
            if (!File.Exists(path))
            {
                // 兜底：尝试补 .png
                string withExt = path + ".png";
                if (File.Exists(withExt))
                    path = withExt;
                else
                    return null;
            }
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); } catch { return null; }
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes)) return null;
            tex.name = "uabhook_" + Path.GetFileNameWithoutExtension(path);
            _texCache[path] = tex;
            if (logReplace) MyUtils.MyLog("[缓存] 加载贴图: " + path);
            return tex;
        }

        private static UnityEngine.Object LoadRep(string f, Type t, int w = 0, int h = 0)
        {
            if (!File.Exists(f))
            {
                // 兜底：尝试补 .png
                string withExt = f + ".png";
                if (!File.Exists(withExt)) return null;
                f = withExt;
            }
            byte[] b; try { b = File.ReadAllBytes(f); } catch { return null; }
            if (t == typeof(Texture2D) || t == null) { var tx = GetOrLoadTexture(f); if (tx != null && w > 0 && h > 0) tx = ScaleTexture(tx, w, h); return tx; }
            if (t == typeof(Sprite)) { var tx = GetOrLoadTexture(f); if (tx != null) { if (w > 0 && h > 0) tx = ScaleTexture(tx, w, h); return Sprite.Create(tx, new Rect(0, 0, tx.width, tx.height), new Vector2(0.5f, 0.5f)); } }
            if (t == typeof(TextAsset)) return new TextAsset(System.Text.Encoding.UTF8.GetString(b));
            var fb = GetOrLoadTexture(f); return fb;
        }

        /// <summary>
        /// 双线性插值缩放 Texture2D 到指定尺寸
        /// </summary>
        private static Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            var result = new Texture2D(targetWidth, targetHeight, source.format, false);
            float incX = 1f / targetWidth;
            float incY = 1f / targetHeight;
            var pixels = new Color[targetWidth * targetHeight];
            for (int py = 0; py < targetHeight; py++)
            {
                for (int px = 0; px < targetWidth; px++)
                {
                    pixels[py * targetWidth + px] = source.GetPixelBilinear((px + 0.5f) * incX, (py + 0.5f) * incY);
                }
            }
            result.SetPixels(pixels);
            result.Apply();
            result.name = source.name;
            return result;
        }
    }
}
