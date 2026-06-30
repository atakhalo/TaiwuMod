using FrameWork.ModSystem;
using Game.Views.Mod.Upload;
using HarmonyLib;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Serialization;
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
    }
}
