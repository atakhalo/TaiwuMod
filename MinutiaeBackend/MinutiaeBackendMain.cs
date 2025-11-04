
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
using System.Collections.Generic;
using System.Diagnostics;
using TaiwuModdingLib.Core.Plugin;

namespace MinutiaeBackend
{

    [PluginConfig(pluginName: "Minutiae", creatorId: "atakhalo", pluginVersion: "2025.11.3.1")]
    public class MinutiaeBackendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;
        public static bool noPenalty; // 开关 取消宴堂惩罚
        public static bool skipFinish = true; // 开关 战斗读书不读已完的书

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        
        public override void Initialize()
        {
            logger.Info("[Minutiae] Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(MinutiaeBackendPlugin));
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
            DomainManager.Mod.GetSetting(ModIdStr, "noPenalty", ref noPenalty);
            DomainManager.Mod.GetSetting(ModIdStr, "skipFinish", ref skipFinish);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ExtraDomain), "FeastEmptyPenalty")]
        public static bool FilterItemEquiped()
        {
            return !noPenalty;
        }


        [HarmonyPrefix, HarmonyPatch(typeof(TaiwuDomain), "CallMethod")]
        public static bool CallMethodCatch(TaiwuDomain __instance, ref int __result,
            Operation operation, RawDataPool argDataPool, RawDataPool returnDataPool, DataContext context
            )
        {
            //logger.Info($"[Minutiae] CallMethod {operation.MethodId}");
            switch (operation.MethodId)
            {
                case 2001:
                    __result = Serializer.Serialize(GetRealSpeed(__instance), returnDataPool);
                    return false; 
            }
            return true;
        }

        public static int GetEfficiency()
        {
			short oldProgress = DomainManager.Extra.GetActiveReadingProgress();
            var index = (int)(oldProgress / 10);
            var ea = GlobalConfig.Instance.ActiveReadProgressAffectedEfficiency;
            if (index >= ea.Length)
            {
                return ea[^1];
            }
            else return ea[index];
        }

        // 直接搬 GetReadingResult，但是加了efficiency， （还处理一些私有字段）
        public static int[] GetRealSpeed(TaiwuDomain __instance)
        {
            //logger.Info($"[Minutiae] GetRealSpeed");
            int efficiency = GetEfficiency();

            ItemKey curReadingBook = Traverse.Create(__instance).Field("_curReadingBook").GetValue<ItemKey>();
            Character taiwuChar = Traverse.Create(__instance).Field("_taiwuChar").GetValue<Character>();

            int[] progress = new int[6];
            bool flag = !curReadingBook.IsValid() || __instance.GetCurrReadingBanByWug();
            int[] array;
            if (flag)
            {
                array = progress;
            }
            else
            {
                int readPageCount = 0;
                int tempPageReadStatus = 0;
                int remainingSpeedPercent = 100;
                GameData.Domains.Item.SkillBook book = DomainManager.Item.GetElement_SkillBooks(curReadingBook.Id);
                byte limit = book.GetPageCount();
                ReadingBookStrategies strategies = __instance.GetCurReadingStrategies();
                bool isCombatSkill = book.IsCombatSkillBook();
                byte pageTypes = book.GetPageTypes();
                short skillAttainment = 0;
                bool flag2 = isCombatSkill;
                byte readingPage;
                TaiwuSkill skill;
                if (flag2)
                {
                    short skillTemplateId = book.GetCombatSkillTemplateId();

                    TaiwuCombatSkill combatSkill = Traverse.Create(__instance).Method("GetTaiwuCombatSkill", skillTemplateId).GetValue<TaiwuCombatSkill>();

                    readingPage = __instance.GetCurrentReadingPage(book, strategies, combatSkill);
                    skill = combatSkill;
                }
                else
                {
                    short skillTemplateId2 = book.GetLifeSkillTemplateId();
                    TaiwuLifeSkill lifeSkill = Traverse.Create(__instance).Method("GetTaiwuLifeSkill", skillTemplateId2).GetValue<TaiwuLifeSkill>();
                    readingPage = __instance.GetCurrentReadingPage(book, strategies, lifeSkill);
                    skill = lifeSkill;
                }
                while (readingPage < limit)
                {
                    sbyte readingProgress = (isCombatSkill ? skill.GetBookPageReadingProgress(CombatSkillStateHelper.GetPageInternalIndex(SkillBookStateHelper.GetOutlinePageType(pageTypes), SkillBookStateHelper.GetNormalPageType(pageTypes, readingPage), readingPage)) : skill.GetBookPageReadingProgress(readingPage));
                    bool flag3 = readingProgress == 100;
                    if (flag3)
                    {
                        tempPageReadStatus |= 1 << (int)readingPage;
                        readPageCount++;
                    }
                    else
                    {
                        bool skipPage = strategies.GetSkipPage(readingPage);
                        if (!skipPage)
                        {
                            bool flag4 = !isCombatSkill;
                            if (flag4)
                            {
                                skillAttainment = taiwuChar.GetPredictLifeSkillAttainment((short)book.GetLifeSkillType(), book.GetLifeSkillTemplateId(), readPageCount);
                            }

                            sbyte baseReadingSpeed = Traverse.Create(__instance).Method("GetBaseReadingSpeed", readingPage).GetValue<sbyte>();
                            int readingSpeedBonus = Traverse.Create(__instance).Method("GetReadingSpeedBonus", readingPage, false, tempPageReadStatus, skillAttainment).GetValue<int>();

                            int readingSpeed = (int)baseReadingSpeed * readingSpeedBonus / 100;

                            // 核心修改， 计算倍率
                            readingSpeed = readingSpeed * efficiency / 100;

                            int addingProgress = readingSpeed * remainingSpeedPercent / 100;
                            int addedProgress = Math.Min(100, addingProgress + (int)readingProgress) - (int)readingProgress;
                            bool flag5 = readingSpeed == 0;
                            if (flag5)
                            {
                                break;
                            }
                            remainingSpeedPercent -= addedProgress * 100 / readingSpeed;
                            progress[(int)readingPage] = (int)((sbyte)addedProgress);
                            bool flag6 = addingProgress + (int)readingProgress < 100;
                            if (flag6)
                            {
                                break;
                            }
                            tempPageReadStatus |= 1 << (int)readingPage;
                            readPageCount++;
                        }
                    }
                    readingPage += 1;
                }
                bool flag7 = readingPage == limit;
                if (flag7)
                {
                    skillAttainment = (short)(isCombatSkill ? 0 : taiwuChar.GetPredictLifeSkillAttainment((short)book.GetLifeSkillType(), book.GetLifeSkillTemplateId(), readPageCount));
                    Traverse.Create(__instance).Method("GetReadPageCountByRereading", false, remainingSpeedPercent, progress, tempPageReadStatus, skillAttainment).GetValue<int>();
                }
                array = progress;
            }
            var ps = $"";
            for (int i = 0; i < progress.Length; i++)
            {
                ps += $"{progress[i]};";
            }
            //logger.Info($"[Minutiae] {efficiency}% : progress {ps}");

            return array;
        }

        #region 战斗读书跳过已完
        [HarmonyPrefix, HarmonyPatch(typeof(CombatDomain), "CalcReadInCombat")]
        public static bool CalcReadInCombat(CombatDomain __instance, DataContext context)
        {
            if(!skipFinish) return true;
            ItemKey currBook = DomainManager.Taiwu.GetCurReadingBook();
            if(currBook.IsValid() && DomainManager.Taiwu.GetTotalReadingProgress(currBook.Id) >= 100)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
        #endregion
    }
}
