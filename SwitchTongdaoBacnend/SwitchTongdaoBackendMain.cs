
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TaiwuModdingLib.Core.Plugin;

namespace MinutiaeBackend
{

    [PluginConfig(pluginName: "SwitchTongdao", creatorId: "atakhalo", pluginVersion: "2025.10.15.1")]
    public class SwitchTongdaoPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        
        public static bool switchTongdao; // 开关
        public static int command1; // 指令
        public static int command2; // 指令
        public static int command3; // 指令
        public static int[] commands = new[] { 3, 4, 5}; // 开关
        public static bool quickEmpty; // 开关 一键留空

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
            DomainManager.Mod.GetSetting(ModIdStr, "command1", ref commands[0]);
            DomainManager.Mod.GetSetting(ModIdStr, "command2", ref commands[1]);
            DomainManager.Mod.GetSetting(ModIdStr, "command3", ref commands[2]);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CombatDomain), "CombatEntry")]
        public static void OnCombatEntry(DataContext context)
        {
            if(switchTongdao)
            {
                haveAddCharIds.Clear(); // 清空
                combatGroupCharIds.Clear();

                // 先把当前的存起来
                var origin = Traverse.Create(DomainManager.Taiwu).Field("_combatGroupCharIds").GetValue<int[]>();
                combatGroupCharIds.AddRange(origin);
                // 处理跳过
                haveAddCharIds.Add(DomainManager.Taiwu.GetTaiwuCharId()); // 跳过太吾
                PreSetChar(context);

                var groupCharIds = DomainManager.Taiwu.GetGroupCharIds();
                TrySetChar(context, groupCharIds, 0);
                TrySetChar(context, groupCharIds, 1);
                TrySetChar(context, groupCharIds, 2);
                haveAddCharIds.Clear(); // 清空
            }
        }

        // 先处理不替换，加入跳过人物
        public static void PreSetChar(DataContext context)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                if (commands[i] == 1)
                {
                    var charId = combatGroupCharIds[i];
                    if (charId != -1)
                    {
                        haveAddCharIds.Add(charId);
                    }
                }
            }
        }

        public static void TrySetChar(DataContext context, CharacterSet groupCharIds, int index)
        {
            if(quickEmpty || commands[index] == 0) // 留空
            {
                DomainManager.Taiwu.SetElement_CombatGroupCharIds(index, -1, context);
                //logger.Info($"[SwitchTongdao] Entry {index} set char emtpy");

            }
            else if(commands[index] == 1) // 不替换
            {
                //logger.Info($"[SwitchTongdao] Entry {index} not set char");
            }
            else
            {
                var charId = FindChar(groupCharIds, index);
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
            foreach (var charid in group)
            {
                if(haveAddCharIds.Contains(charid)) // 跳过已经加过的
                {
                    continue;
                }

                var character = DomainManager.Character.GetElement_Objects(charid);
                var _charTeammateCommandDict = Traverse.Create(DomainManager.Extra)
                    .Field("_charTeammateCommandDict").GetValue<Dictionary<int, SByteList>>();
                if(_charTeammateCommandDict.ContainsKey(charid))
                {
                    if (_charTeammateCommandDict[charid].Items.Contains((sbyte)(commands[index] - 2)))
                    {
                        //logger.Info($"[SwitchTongdao] FindChar {index} char {charid}");
                        return charid;
                    }
                    else
                    {
                        //logger.Info($"[SwitchTongdao] FindChar {index}  no find");
                    }
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
