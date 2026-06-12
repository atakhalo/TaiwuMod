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
//using UnityEngine.Windows;
//using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
//using static GameData.Domains.Item.ItemOperationType;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

namespace TuYiLe
{
    [PluginConfig(pluginName: "TuYiLe", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class TuYiLeFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool avgGrade = true; // 装备开关

        public override void Initialize()
        {
			MyUtils.modName = nameof(TuYiLe);
			MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(TuYiLeFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "avgGrade", ref avgGrade);
        }
    }


}
