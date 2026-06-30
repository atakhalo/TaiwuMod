using FrameWork.ModSystem;
using Game.Views.Mod.Upload;
using GameData.Domains.Mod;
using HarmonyLib;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine;

namespace ModSet
{
    [PluginConfig(pluginName: "ModSet", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class ModSetFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public override void Initialize()
        {
            MyUtils.modName = nameof(ModSet);
            MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(ModSetFrontendPlugin));
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
        }

        /// ================================================================
        /// 补丁 1：Refresh Postfix
        /// 目标：ModUploadEditPanel.Refresh() 完成后，将 _tempModSettingEntries
        ///       替换为从 Config.Lua 原始数据解析的默认设置项。
        /// 原因：原逻辑将 _curEditModInfo.ModSettingEntries（已加载 Settings.Lua
        ///       的用户当前值）复制到 _tempModSettingEntries，导致编辑器中看到
        ///       的是用户当前设置而非默认设置。
        /// ================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModUploadEditPanel), "Refresh")]
        public static void Postfix_Refresh(object __instance)
        {
            var curEditModInfo = (ModInfoWithDisplayData)AccessTools
                .Field(typeof(ModUploadEditPanel), "_curEditModInfo")
                .GetValue(__instance);

            var tempSettingEntries = (List<SettingEntry>)AccessTools
                .Field(typeof(ModUploadEditPanel), "_tempModSettingEntries")
                .GetValue(__instance);

            if (curEditModInfo?.SourceLuaTable == null || tempSettingEntries == null)
                return;

            if (!curEditModInfo.SourceLuaTable.ContainsKey("DefaultSettings"))
                return;

            curEditModInfo.SourceLuaTable.Load("DefaultSettings", out Table settingsTable);
            if (settingsTable == null || settingsTable.Length == 0)
                return;

            var defaultEntries = new List<SettingEntry>();
            for (int i = 1; i <= settingsTable.Length; i++)
            {
                settingsTable.Load(i, out Table settingEntryTable);
                if (settingEntryTable == null) continue;

                settingEntryTable.Load("SettingType", out string settingType);

                SettingEntry settingEntry = settingType switch
                {
                    "Toggle" => new ToggleSetting(),
                    "ToggleGroup" => new ToggleGroupSetting(),
                    "InputField" => new InputFieldSetting(),
                    "Slider" => new SliderSetting(),
                    "Dropdown" => new DropdownSetting(),
                    _ => null
                };

                if (settingEntry == null)
                {
                    MyUtils.MyLog($"Unknown setting type: {settingType}");
                    continue;
                }

                settingEntry.LoadDefaultSetting(settingEntryTable);
                defaultEntries.Add(settingEntry);
            }

            if (defaultEntries.Count > 0)
            {
                tempSettingEntries.Clear();
                tempSettingEntries.AddRange(defaultEntries);
                MyUtils.MyLog($"已恢复 {defaultEntries.Count} 个设置为默认值");
            }
        }

        // ================================================================
        // SaveMod 备份/恢复 静态字段
        // ================================================================
        private static List<SettingEntry> _backupSettingEntries;
        private static string _backupModDirectory;

        /// ================================================================
        /// 补丁 2：SaveMod Prefix
        /// 目标：备份用户当前的运行时设置（ModSettingEntries），以便在
        ///       SaveMod 完成后恢复并重写 Settings.Lua。
        /// 原因：SaveMod 会将 _tempModSettingEntries（默认值）写入
        ///       ModSettingEntries，随后 SaveModSettings(false) 将
        ///       ModSettingEntries（此时已是默认值）写入 Settings.Lua，
        ///       导致用户的运行时设置丢失。
        /// ================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModUploadEditPanel), "SaveMod")]
        public static void Prefix_SaveMod(object __instance)
        {
            var curEditModInfo = (ModInfoWithDisplayData)AccessTools
                .Field(typeof(ModUploadEditPanel), "_curEditModInfo")
                .GetValue(__instance);

            if (curEditModInfo?.ModSettingEntries == null)
            {
                _backupSettingEntries = null;
                _backupModDirectory = null;
                return;
            }

            // Deep Clone 备份当前用户运行时设置
            _backupSettingEntries = curEditModInfo.ModSettingEntries
                .Select(e => e.Clone())
                .ToList();
            _backupModDirectory = curEditModInfo.DirectoryName;

            MyUtils.MyLog($"已备份 {_backupSettingEntries.Count} 个运行时设置");
        }

        /// ================================================================
        /// 补丁 3：SaveMod Postfix
        /// 目标：仅重写 Settings.Lua 为用户运行时设置（覆盖 SaveModSettings
        ///       写入的默认值），但不恢复内存中的 ModSettingEntries。
        /// 
        /// 不恢复 ModSettingEntries 的原因：SaveMod 之后 UploadMod 会调用
        /// ExcludeModSettingFile() → SaveModInfo()，若 ModSettingEntries
        /// 已被恢复为用户设置，则 Config.Lua 的 DefaultSettings 又被覆盖。
        /// 内存中的 ModSettingEntries 保留默认值，后续操作（如 UpdateModList
        /// 重新加载）会从磁盘重新读取正确的值。
        /// ================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModUploadEditPanel), "SaveMod")]
        public static void Postfix_SaveMod(object __instance)
        {
            if (_backupSettingEntries == null || string.IsNullOrEmpty(_backupModDirectory))
                return;

            // 仅重写 Settings.Lua 文件，不碰内存中的 ModSettingEntries
            string settingsPath = Path.Combine(_backupModDirectory, "Settings.Lua");
            try
            {
                Table settingTable = new Table(null);
                foreach (var entry in _backupSettingEntries)
                {
                    entry.SaveToLuaTable(settingTable);
                }
                File.WriteAllText(settingsPath, settingTable.Serialize(true, 0));
                MyUtils.MyLog("已恢复 Settings.Lua 为用户运行时设置");
            }
            catch (System.Exception ex)
            {
                MyUtils.MyLog($"保存 Settings.Lua 失败: {ex.Message}");
            }

            _backupSettingEntries = null;
            _backupModDirectory = null;
        }

		/// ================================================================
		/// 补丁 4：ReadModInfo Postfix
		/// 目标：读取 mod 信息后，优先从 ModSettingSave/{Title}_{Source}_{FileId}/Settings.Lua
		///       加载设置。若该文件不存在，则从原路径迁移一次。
		/// 
		/// 原因：将 Settings.Lua 重定向到独立于 mod 目录的存储位置，
		///       避免 Steam 更新 mod 时 Config.Lua 变化导致的设置覆盖。
		///       ModSettingSave 目录与 Mod 目录同级，不受 Steam 操作影响。
		/// ================================================================
		[HarmonyPostfix]
        [HarmonyPatch(typeof(ModManager), "ReadModInfo")]
        public static void Postfix_ReadModInfo(ref ModInfoWithDisplayData __result, string configPath, bool loadOnRead)
        {
            if (__result == null || !loadOnRead || string.IsNullOrEmpty(__result.Title))
                return;

            string newPath = GetModSettingSavePath(__result);
            string newDir = Path.GetDirectoryName(newPath);

            if (!File.Exists(newPath))
            {
                // 首次使用 ModSet：从原路径迁移到新路径
                string origPath = Path.Combine(Path.GetDirectoryName(configPath), "Settings.Lua");
                if (File.Exists(origPath))
                {
                    try
                    {
                        if (!Directory.Exists(newDir))
                            Directory.CreateDirectory(newDir);
                        File.Copy(origPath, newPath, true);
                        MyUtils.MyLog($"已迁移 Settings.Lua: {__result.Title}");
                    }
                    catch (Exception ex)
                    {
                        MyUtils.MyLog($"迁移失败: {ex.Message}");
                    }
                }
                return;
            }

            // 从新路径加载设置（覆盖原 ModSettingEntries 中已从原 Settings.Lua 加载的值）
            try
            {
                string text = File.ReadAllText(newPath);
                Script luaScript = new Script();
                // Settings.Lua 是 SaveModSettings 写入的完整 chunk 格式
                // （Serialize(true,0) 生成含 return 的完整语句）
                DynValue dynVal = luaScript.DoString(text);
                if (dynVal.Type == DataType.Table)
                {
                    __result.LoadSettingsFromLuaTable(dynVal.Table);
                    // 同步更新 ModSettings（ApplySettings 在 ReadModInfo 中已经执行过了）
                    __result.ApplySettings();
                }
            }
            catch (Exception ex)
            {
                MyUtils.MyLog($"加载设置失败: {ex.Message}");
            }
        }

        /// ================================================================
        /// 补丁 5：SaveModSettings Postfix
        /// 目标：在正常保存 Settings.Lua 后，额外保存一份到
        ///       ModSettingSave/{Title}/Settings.Lua。
        /// 
        /// 原因：确保 ModSettingSave 中的设置始终与当前设置同步。
        ///       原路径的保存逻辑不变，保持兼容性。
        /// ================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModManager), "SaveModSettings")]
        public static void Postfix_SaveModSettings(bool onlySaveCommonSetting)
        {
            if (onlySaveCommonSetting)
                return;

            foreach (var kv in ModManager.LocalMods)
            {
                var modInfo = kv.Value;
                if (modInfo == null || string.IsNullOrEmpty(modInfo.Title))
                    continue;

                string newPath = GetModSettingSavePath(modInfo);
                try
                {
                    string newDir = Path.GetDirectoryName(newPath);
                    if (!Directory.Exists(newDir))
                        Directory.CreateDirectory(newDir);

                    Table settingTable = new Table(null);
                    modInfo.SaveSettingsToLuaTable(settingTable);
                    File.WriteAllText(newPath, settingTable.Serialize(true, 0));
                }
                catch (Exception ex)
                {
                    MyUtils.MyLog($"保存设置到新路径失败: {ex.Message}");
                }
            }
        }

        /// ================================================================
        /// 工具方法：获取 ModSettingSave 根目录
        /// ================================================================
        private static string GetModSettingSaveRoot()
        {
            // ModSettingSave 与 Mod 目录同级
            string modRoot = ModManager.GetModRootFolder();
            string gameRoot = Path.GetDirectoryName(modRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.Combine(gameRoot, "ModSettingSave");
        }

        /// <summary>
        /// 工具方法：获取指定 mod 的 Settings.Lua 新路径
        /// 格式：ModSettingSave/{Title}_{Source}_{FileId}/Settings.Lua
        /// </summary>
        private static string GetModSettingSavePath(string title, byte source, ulong fileId)
        {
            string safeName = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
            string folderName = $"{safeName}_{source}_{fileId}";
            return Path.Combine(GetModSettingSaveRoot(), folderName, "Settings.Lua");
        }

        /// <summary>
        /// 从 ModInfoWithDisplayData 获取保存路径的重载
        /// </summary>
        private static string GetModSettingSavePath(ModInfoWithDisplayData modInfo)
        {
            if (modInfo == null || string.IsNullOrEmpty(modInfo.Title))
                return null;
            return GetModSettingSavePath(modInfo.Title, modInfo.ModId.Source, modInfo.ModId.FileId);
        }
    }
}
