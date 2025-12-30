
using GameData.Common;
using GameData.Domains;
using GameData.Domains.Character;
using GameData.Domains.Combat;
using GameData.Domains.CombatSkill;
using GameData.Domains.Extra;
using GameData.Domains.Item;
using GameData.Domains.Mod;
using GameData.Domains.Taiwu;
using GameData.GameDataBridge;
using GameData.Serializer;
using GameData.Utilities;
using HarmonyLib;
using NLog;
using NLog.Fluent;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TaiwuModdingLib.Core.Plugin;

namespace SwitchTongdaoBackend
{

    [PluginConfig(pluginName: "SwitchTongdao", creatorId: "atakhalo", pluginVersion: "2025.12.30.1")]
    public class SwitchTongdaoPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        
        public static bool switchTongdao; // 开关
		public static bool justAllow; // 开关 允许同道战斗时生效
		public static bool quickEmpty; // 开关 一键留空
		public static bool noKillEmpty; // 开关 非死斗留空

        private static bool toEmpty; // 留空

		// 每个同道的3个指令， 第一层同道，第二层指令
		// 01 作为特殊值，用于玩家设置输入；会在判断的时候减掉
		public static List<List<int>> allCommands = new(){new(){ 3, 4, 5 }, new(){ 3, 4, 5 }, new(){ 3, 4, 5 } }; 

        private static HashSet<int> haveAddCharIds = new(3); // 记录已经安排上的同道

        private static List<int> combatGroupCharIds = new(); // 记录进战斗前的同道

        public override void Initialize()
        {
            logger.Info("[SwitchTongdao] Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(SwitchTongdaoPlugin));
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
            DomainManager.Mod.GetSetting(ModIdStr, "switchTongdao", ref switchTongdao);
            DomainManager.Mod.GetSetting(ModIdStr, "quickEmpty", ref quickEmpty);
            DomainManager.Mod.GetSetting(ModIdStr, "justAllow", ref justAllow);
			DomainManager.Mod.GetSetting(ModIdStr, "noKillEmpty", ref noKillEmpty);

			// 处理指令值的时候会把前面的特殊选项减掉
			int temp = 3;
            DomainManager.Mod.GetSetting(ModIdStr, "command1", ref temp); allCommands[0][0] = temp;
            DomainManager.Mod.GetSetting(ModIdStr, "command1_2", ref temp); allCommands[0][1] = temp;
            DomainManager.Mod.GetSetting(ModIdStr, "command1_3", ref temp); allCommands[0][2] = temp;
            DomainManager.Mod.GetSetting(ModIdStr, "command2", ref temp); allCommands[1][0] = temp;
            DomainManager.Mod.GetSetting(ModIdStr, "command2_2", ref temp); allCommands[1][1] = temp;
			DomainManager.Mod.GetSetting(ModIdStr, "command2_3", ref temp); allCommands[1][2] = temp;
			DomainManager.Mod.GetSetting(ModIdStr, "command3", ref temp); allCommands[2][0] = temp;
			DomainManager.Mod.GetSetting(ModIdStr, "command3_2", ref temp); allCommands[2][1] = temp;
			DomainManager.Mod.GetSetting(ModIdStr, "command3_3", ref temp); allCommands[2][2] = temp;
		}

        [HarmonyPrefix, HarmonyPatch(typeof(CombatDomain), "CombatEntry")]
        public static void OnCombatEntry(DataContext context, short combatConfigTemplateId)
        {
			if(!switchTongdao) return;
            toEmpty = false;
            var config = Config.CombatConfig.Instance[combatConfigTemplateId];
            // 仅允许同道上场的战斗才生效
            // logger.Info($"[SwitchTongdao] 是否允许同道 {config.AllowGroupMember}");
			if(justAllow && !config.AllowGroupMember) return;
            // 非死斗留空
            if (noKillEmpty && config.CombatType != 2) { toEmpty = true; }

			haveAddCharIds.Clear(); // 清空
			combatGroupCharIds.Clear();

			// 先把当前的同道存起来, 用于战后恢复
			var origin = Traverse.Create(DomainManager.Taiwu).Field("_combatGroupCharIds").GetValue<int[]>();
			combatGroupCharIds.AddRange(origin);
			// 处理跳过
			haveAddCharIds.Add(DomainManager.Taiwu.GetTaiwuCharId()); // 跳过太吾
			PreSetChar(context); // 跳过第一个指令填“留空”的同道

			var groupCharIds = DomainManager.Taiwu.GetGroupCharIds();
			TrySetChar(context, groupCharIds, 0);
			TrySetChar(context, groupCharIds, 1);
			TrySetChar(context, groupCharIds, 2);
			haveAddCharIds.Clear(); // 清空
        }

        // 先处理不替换，加入跳过人物
        public static void PreSetChar(DataContext context)
        {
            for (int i = 0; i < allCommands.Count; i++)
            {
                if (allCommands[i][0] == 1) // 仅处理第一个指令
                {
                    var charId = combatGroupCharIds[i];// 判断原来是否为空，否则将原来的人物标记已加，防止重复
                    if (charId != -1)
                    {
                        haveAddCharIds.Add(charId);
                    }
                }
            }
        }

        public static void TrySetChar(DataContext context, CharacterSet groupCharIds, int index)
        {
            if(quickEmpty || toEmpty || allCommands[index][0] == 0) // 留空
            {
                DomainManager.Taiwu.SetElement_CombatGroupCharIds(index, -1, context);
                //logger.Info($"[SwitchTongdao] Entry {index} set char emtpy");

            }
            else if(allCommands[index][0] == 1) // 不替换
            {
                //logger.Info($"[SwitchTongdao] Entry {index} not set char");
            }
            else
            {
                var charId = -1;
                try
                {
                    charId = FindChar(groupCharIds, index); // 尝试寻找指令同道，有则标记已加并设置
                }
                catch (Exception e)
                {
                    logger.Info($"[SwitchTongdao] bug");
                }
                //logger.Info($"[SwitchTongdao] Entry {index} set char to {charId}");
                if (charId != -1)
                {
                    haveAddCharIds.Add(charId);
                    DomainManager.Taiwu.SetElement_CombatGroupCharIds(index, charId, context);
                }
                else
                {
                    // 没找到，留空
                    DomainManager.Taiwu.SetElement_CombatGroupCharIds(index, -1, context);
                }
            }
        }

        public static int FindChar(CharacterSet groupCharIds, int index)
        {
            var group =  groupCharIds.GetCollection();
            int i = 0;
            var _charTeammateCommandDict = Traverse.Create(DomainManager.Extra)
                .Field("_charTeammateCommandDict").GetValue<Dictionary<int, SByteList>>();
            foreach (var charid in group)
            {
                if(haveAddCharIds.Contains(charid)) // 跳过已经加过的
                {
                    continue;
                }
                if (_charTeammateCommandDict.ContainsKey(charid))
                {
					var charCommand = _charTeammateCommandDict[charid].Items;
                    if (charCommand == null || charCommand.Count == 0) continue;
					var r = false;
                    if (charCommand.Contains((sbyte)(allCommands[index][0] - 2))) // 匹配第一个
					{
						r = true;
						if(allCommands[index][1] >= 2 
							&& !charCommand.Contains((sbyte)(allCommands[index][1] - 2))) // 第二个不是 01，且不满足
						{
							r = false;
						}
						if (allCommands[index][2] >= 2
							&& !charCommand.Contains((sbyte)(allCommands[index][2] - 2))) // 第三个不是 01，且不满足
						{
							r = false;
						}
					}
					if(r) return charid;
                }
                else
                {
                    //logger.Info($"[SwitchTongdao] FindChar _charTeammateCommandDict null");
                }
            }
            return -1;
        }


        [HarmonyPrefix, HarmonyPatch(typeof(CombatCharacter), "OnCombatEnd")]
        public static void OnTaiwuCombatEnd(CombatCharacter __instance, DataContext context)
        {
            if(switchTongdao && __instance.IsTaiwu)
            {
                //logger.Info($"[SwitchTongdao] CombatEnd run");
                for (var i = 0; i < combatGroupCharIds.Count; i++)
                {
                    //logger.Info($"[SwitchTongdao] CombatEnd {i} set char {combatGroupCharIds[i]}");
                    DomainManager.Taiwu.SetElement_CombatGroupCharIds(i, combatGroupCharIds[i], context);
                }
            }
        }

        //[HarmonyPrefix, HarmonyPatch(typeof(TaiwuDomain), "CallMethod")]
        //public static bool CallMethodCatch(TaiwuDomain __instance, ref int __result,
        //    Operation operation, RawDataPool argDataPool, RawDataPool returnDataPool, DataContext context
        //    )
        //{
        //    return true;
        //}    
    }
}
