using FrameWork.ModSystem;
using HarmonyLib;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine;

namespace DepPopup
{
    [PluginConfig(pluginName: "DepPopup", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class DepPopupFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;


		public override void Initialize()
        {
			MyUtils.modName = nameof(DepPopup);
			MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(DepPopupFrontendPlugin));

            // SteamManager 是 internal 的，不能用 typeof，需手动patch
            Type steamMgr = Type.GetType("SteamManager, Assembly-CSharp");
            MethodInfo target = AccessTools.Method(steamMgr, "CheckModDependencyHasChanged",
                new Type[] { typeof(uint), typeof(ModInfoWithDisplayData), typeof(uint), typeof(List<ulong>).MakeByRefType() });
            if (target != null)
            {
                MethodInfo prefix = AccessTools.Method(typeof(FixDependencyCheck), nameof(FixDependencyCheck.Prefix));
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                MyUtils.MyLog("FixDependencyCheck patched successfully");
            }
            else
            {
                MyUtils.MyLog("ERROR: Could not find CheckModDependencyHasChanged method");
            }
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
            // ModManager.GetSetting(ModIdStr, "avgGrade", ref avgGrade);
        }
    }

    /// <summary>
    /// 修复 UpdateTargetItems 中因 Source 不匹配导致 _localMods 查不到本地上传mod，
    /// 新建的空白 ModInfoWithDisplayData 的 Dependencies 为空，被 CheckModDependencyHasChanged
    /// 判定为"依赖变更"的问题。
    ///
    /// 具体原因：
    ///   UpdateItem 用 Source=1 创建 ModId → key="1_{FileId}"
    ///   但本地mod存为 Source=0 → key="0_{FileId}"
    ///   查不到 → 新建空白 modInfo → Dependencies=[] → 与 Steam Children 不匹配 → 误报
    ///
    /// 修复方式：
    ///   在 CheckModDependencyHasChanged 前拦截，当 modInfo.Dependencies 为空时，
    ///   查找本地mod的真实 Dependencies 进行对比。
    /// </summary>
    public static class FixDependencyCheck
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ref bool __result,
            uint index,
            ModInfoWithDisplayData modInfo,
            uint childNumber,
            ref List<ulong> dependencies)
        {
            // 如果 modInfo 本身就有 Dependencies，说明数据正常，让原方法执行
            if (modInfo.Dependencies != null && modInfo.Dependencies.Count > 0)
                return true;

            // modInfo.Dependencies 为空 → 尝试从本地mod数据中查找
            ulong fileId = modInfo.ModId.FileId;
            string localKey = $"0_{fileId}";

            if (ModManager.LocalMods.TryGetValue(localKey, out var localModInfo)
                && localModInfo.Dependencies != null && localModInfo.Dependencies.Count > 0)
            {
                // 读取 Steam Workshop 上记录的 Children（当前依赖列表）
                if (dependencies == null) dependencies = new List<ulong>();
                dependencies.Clear();

                if (childNumber > 0)
                {
                    var childIds = new PublishedFileId_t[childNumber];
                    var steamMgr = Type.GetType("SteamManager, Assembly-CSharp");
                    var queryHandle = (UGCQueryHandle_t)steamMgr
                        .GetField("_ugcQueryHandle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .GetValue(null);

                    if (SteamUGC.GetQueryUGCChildren(queryHandle, index, childIds, childNumber))
                    {
                        foreach (var childId in childIds)
                            dependencies.Add(childId.m_PublishedFileId);
                    }
                }

                // 用本地 Config.Lua 中的 Dependencies 与 Steam Children 比较
                __result = ListContentIsDifferent(dependencies, localModInfo.Dependencies);
                return false; // 跳过原方法
            }

            return true; // 没找到本地数据，走原逻辑
        }

        /// <summary>ContentIsDifferent 的内联实现（省去对 Extentions 扩展方法的依赖）</summary>
        private static bool ListContentIsDifferent(IList<ulong> a, List<ulong> b)
        {
            int aCnt = a?.Count ?? 0;
            int bCnt = b?.Count ?? 0;
            if (aCnt == 0 && bCnt == 0) return false;
            if (aCnt != bCnt) return true;
            for (int i = 0; i < aCnt; i++)
            {
                if (!a[i].Equals(b[i])) return true;
            }
            return false;
        }
    }
}
