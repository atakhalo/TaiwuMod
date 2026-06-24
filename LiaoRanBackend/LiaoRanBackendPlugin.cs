using Config;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.CombatSkill;
using GameData.Domains.Extra;
using GameData.Domains.Item;
using GameData.Domains.Taiwu;
using GameData.GameDataBridge;
using GameData.Serializer;
using GameData.Utilities;
using HarmonyLib;
using NLog;
using TaiwuModdingLib.Core.Plugin;

namespace LiaoRanBackend
{
    [PluginConfig(pluginName: "LiaoRan", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class LiaoRanBackendPlugin : TaiwuRemakePlugin
    {
        private Harmony? harmony;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public override void Initialize()
        {
            logger.Info("[LiaoRan] Backend Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(LiaoRanBackendPlugin));
        }

        public override void Dispose()
        {
            harmony?.UnpatchSelf();
        }

        private static int GetEfficiency()
        {
            short oldProgress = DomainManager.Extra.GetActiveReadingProgress();
            var ea = GlobalConfig.Instance.ActiveReadProgressAffectedEfficiency;
            int index = oldProgress / 10;
            if (index >= ea.Length) return ea[^1];
            return ea[index];
        }

        // 完整复制 GetReadingResult 逻辑，加上效率系数
        public static int[] GetReadingResultWithEfficiency(TaiwuDomain __instance)
        {
            int efficiency = GetEfficiency();

            ItemKey curReadingBook = Traverse.Create(__instance).Field("_curReadingBook").GetValue<ItemKey>();
            var taiwuChar = Traverse.Create(__instance).Field("_taiwuChar").GetValue<GameData.Domains.Character.Character>();

            int[] progress = new int[6];
            if (!curReadingBook.IsValid() || __instance.GetCurrReadingBanByWug())
            {
                Array.Resize(ref progress, 7);
                progress[6] = efficiency;
                return progress;
            }

            int readPageCount = 0;
            int tempPageReadStatus = 0;
            int remainingSpeedPercent = 100;
            GameData.Domains.Item.SkillBook book = DomainManager.Item.GetElement_SkillBooks(curReadingBook.Id);
            byte limit = book.GetPageCount();
            ReadingBookStrategies strategies = __instance.GetCurReadingStrategies();
            bool isCombatSkill = book.IsCombatSkillBook();
            byte pageTypes = book.GetPageTypes();
            short skillAttainment = 0;
            byte readingPage;
            object skill;
            if (isCombatSkill)
            {
                short skillTemplateId = book.GetCombatSkillTemplateId();
                var combatSkill = Traverse.Create(__instance).Method("GetTaiwuCombatSkill", skillTemplateId).GetValue();
                readingPage = __instance.GetCurrentReadingPage(book, strategies, (TaiwuCombatSkill)combatSkill);
                skill = combatSkill;
            }
            else
            {
                short skillTemplateId2 = book.GetLifeSkillTemplateId();
                var lifeSkill = Traverse.Create(__instance).Method("GetTaiwuLifeSkill", skillTemplateId2).GetValue();
                readingPage = __instance.GetCurrentReadingPage(book, strategies, (TaiwuLifeSkill)lifeSkill);
                skill = lifeSkill;
            }

            // 反射获取 GetBookPageReadingProgress
            var skillType = skill.GetType();
            var getProgressMethod = skillType.GetMethod("GetBookPageReadingProgress", new[] { typeof(byte) });

            while (readingPage < limit)
            {
                sbyte readingProgress;
                if (isCombatSkill)
                {
                    byte internalIdx = CombatSkillStateHelper.GetPageInternalIndex(
                        SkillBookStateHelper.GetOutlinePageType(pageTypes),
                        SkillBookStateHelper.GetNormalPageType(pageTypes, readingPage), readingPage);
                    readingProgress = (sbyte)getProgressMethod.Invoke(skill, new object[] { internalIdx });
                }
                else
                {
                    readingProgress = (sbyte)getProgressMethod.Invoke(skill, new object[] { readingPage });
                }

                if (readingProgress == 100)
                {
                    tempPageReadStatus |= 1 << (int)readingPage;
                    readPageCount++;
                }
                else
                {
                    bool skipPage = strategies.GetSkipPage(readingPage);
                    if (!skipPage)
                    {
                        if (!isCombatSkill)
                        {
                            skillAttainment = taiwuChar.GetPredictLifeSkillAttainment(
                                (short)book.GetLifeSkillType(), book.GetLifeSkillTemplateId(), readPageCount);
                        }

                        sbyte baseReadingSpeed = Traverse.Create(__instance).Method("GetBaseReadingSpeed", readingPage).GetValue<sbyte>();
                        int readingSpeedBonus = Traverse.Create(__instance).Method("GetReadingSpeedBonus",
                            readingPage, false, tempPageReadStatus, skillAttainment).GetValue<int>();

                        int readingSpeed = (int)baseReadingSpeed * readingSpeedBonus / 100;

                        // 核心：应用效率系数
                        readingSpeed = readingSpeed * efficiency / 100;

                        int addingProgress = readingSpeed * remainingSpeedPercent / 100;
                        int addedProgress = Math.Min(100, addingProgress + (int)readingProgress) - readingProgress;
                        if (readingSpeed == 0) break;
                        remainingSpeedPercent -= addedProgress * 100 / readingSpeed;
                        progress[(int)readingPage] = (sbyte)addedProgress;
                        if (addingProgress + readingProgress >= 100)
                        {
                            tempPageReadStatus |= 1 << (int)readingPage;
                            readPageCount++;
                        }
                        else break;
                    }
                }
                readingPage++;
            }
            if (readingPage == limit)
            {
                skillAttainment = (short)(isCombatSkill ? 0 : taiwuChar.GetPredictLifeSkillAttainment(
                    (short)book.GetLifeSkillType(), book.GetLifeSkillTemplateId(), readPageCount));
                Traverse.Create(__instance).Method("GetReadPageCountByRereading",
                    false, remainingSpeedPercent, progress, tempPageReadStatus, skillAttainment).GetValue();
            }

            Array.Resize(ref progress, 7);
            progress[6] = efficiency;

            int total = progress[0] + progress[1] + progress[2] + progress[3] + progress[4] + progress[5];
            // logger.Info($"[LiaoRan] 2001: efficiency={efficiency}%, sum={total}");
            return progress;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TaiwuDomain), "CallMethod")]
        public static bool CallMethod2001(TaiwuDomain __instance, ref int __result,
            Operation operation, RawDataPool argDataPool, RawDataPool returnDataPool, DataContext context)
        {
            if (operation.MethodId != 2001) return true;

            int[] result = GetReadingResultWithEfficiency(__instance);
            __result = Serializer.Serialize(result, returnDataPool);
            return false;
        }
    }
}
