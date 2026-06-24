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
using Game.Views.MouseTips;
//using UnityEngine.Windows;
//using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
//using static GameData.Domains.Item.ItemOperationType;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

namespace LiaoRan
{
    [PluginConfig(pluginName: "LiaoRan", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class LiaoRanFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool showEfficiency = true; // 开关 显示加成后的阅读速度

        public override void Initialize()
        {
			MyUtils.modName = nameof(LiaoRan);
			MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(LiaoRanFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "showEfficiency", ref showEfficiency);
        }

        #region 读书效率显示
        // 在 GetReadingResult 异步请求发起后，用自定义 methodId 2001 获取带效率的结果
        [HarmonyPostfix, HarmonyPatch(typeof(TaiwuDomainMethod.AsyncCall), "GetReadingResult")]
        public static void OnGetReadingResult()
        {
            if (!showEfficiency) return;
            if (!UIElement.MouseTipReading.Exist) return;
            // MyUtils.MyLog($"OnGetReadingResult called, showEfficiency={showEfficiency}, MouseTipExist={UIElement.MouseTipReading.Exist}");

            var tips = UIElement.MouseTipReading.UiBase;
            tips.AsyncMethodCall(5, 2001, new AsyncMethodCallbackDelegate(OnEfficiencyResult));
        }

        /// <summary>
        /// 接收 methodId 2001 的异步回调。
        /// dataPool 中为 int[7]: [0..5] 各页进度（已含效率系数），[6] 效率百分比。
        /// </summary>
        private static void OnEfficiencyResult(int offset, RawDataPool dataPool)
        {
            if (!showEfficiency || !UIElement.MouseTipReading.Exist) return;

            try
            {
                int[] progress = null;
                Serializer.Deserialize(dataPool, offset, ref progress);
                if (progress == null || progress.Length < 7) return;

                int efficiency = progress[6];
				var rProgress = new List<int>(progress);
				rProgress.RemoveAt(6);
				var progressSum = ProgressSum(rProgress.ToArray());
				var sum = progressSum;

				var sum1 = 0;
				var ps = $"";
				for (int i = 0; i < rProgress.Count; i++)
				{
					ps += $"{progress[i]};";
					sum1 += progress[i];
				}

				var tips = UIElement.MouseTipReading.UiBase;
                var text = tips.transform.Find("ReadEffectLayout/ReadingProgress/ReadingProgressValue")
                    ?.GetComponent<TextMeshProUGUI>();
                if (text == null || string.IsNullOrEmpty(text.text)) return;

				var s = text.text.Split(' ');
				text.text = $"{s[0]} ({efficiency}%:<color=#F8E0CAFF>{sum}%</color>)";
				// MyUtils.MyLog($"Updated text: '{s[0]}' -> efficiency={efficiency}%, sum={sum}%");
            }
            catch (Exception e)
            {
                MyUtils.MyLog($"OnEfficiencyResult error: {e.Message}");
            }
        }

		// 对应 MouseTipReading 中 GetReadingResult 的回调, 处理数字总和过大的问题
		public static int ProgressSum(int[] progress)
		{
			var tips = UIElement.MouseTipReading.UiBase as MouseTipReading;
			var _skillBookPageDisplayData = Traverse.Create(tips).Field("_skillBookPageDisplayData").GetValue<SkillBookPageDisplayData>();
			var ReadingProgress = _skillBookPageDisplayData.ReadingProgress;
			int currReadingEfficiency = 0;

			var ps = $"";
			for (int i = 0; i < ReadingProgress.Length; i++)
			{
				ps += $"{ReadingProgress[i]};";
			}
			//Debug.Log($"[Minutiae] : ReadingProgress {ps}");

			for (int i = 0; i < ReadingProgress.Length; i++)
			{
				if (ReadingProgress[i] < 100)
				{
					//var afterRead = Math.Min(progress[i], 100);
					//currReadingEfficiency += afterRead - ReadingProgress[i];
					currReadingEfficiency += Math.Min(progress[i], (int)(100 - ReadingProgress[i]));
				}
				else
				{
					// 读完的不用加
					//currReadingEfficiency += 100;
				}
			}
			if (currReadingEfficiency == 0) // 读完后等于0
				return progress.Sum();
			return currReadingEfficiency;
		}
		#endregion
	}
}
