using Config;
using GameData.Domains.CombatSkill;
using HarmonyLib;
using TaiwuModdingLib.Core.Plugin;
using TMPro;
using UnityEngine;

namespace AttackTotal
{
	[PluginConfig(pluginName: "攻击总和", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
	public class AttackTotalFrontendPlugin : TaiwuRemakePlugin
	{
		private Harmony harmony;

		public override void Initialize()
		{
			harmony = Harmony.CreateAndPatchAll(typeof(AttackTotalFrontendPlugin));
		}

		public override void Dispose()
		{
			if (harmony != null)
			{
				harmony.UnpatchSelf();
			}
		}

		/// <summary>
		/// 游戏每次刷新功法 tips 的"攻击属性"区域后，
		/// 在标题后追加攻击总和（破体显示值 + 破气显示值 - 100）。
		/// 该方法是唯一统一入口：实战/模板两种模式都会经过，参数 isAttackSkill 即"是否催破"判断。
		/// 游戏每次刷新都会先把标题重置为"攻击属性"，因此本处追加不会叠加。
		/// </summary>
		[HarmonyPostfix]
		[HarmonyPatch(typeof(Game.Views.MouseTips.TooltipCombatSkill), "RefreshAttackPropertyInfo")]
		private static void RefreshAttackPropertyInfoPostfix(Game.Views.MouseTips.TooltipCombatSkill __instance, bool isAttackSkill)
		{
			// 只有催破功法才显示破体破气
			if (!isAttackSkill)
			{
				return;
			}

			CombatSkillDisplayData display = AccessTools.Field(typeof(Game.Views.MouseTips.TooltipCombatSkill), "_combatSkillDisplayData").GetValue(__instance) as CombatSkillDisplayData;
			CombatSkillItem config = AccessTools.Field(typeof(Game.Views.MouseTips.TooltipCombatSkill), "_configData").GetValue(__instance) as CombatSkillItem;

			int outer;
			int inner;
			if (display != null)
			{
				outer = 100 + display.PenetrateValueOuter;
				inner = 100 + display.PenetrateValueInner;
			}
			else if (config != null)
			{
				int total = config.Penetrate;
				int baseInner = total * config.BaseInnerRatio / 100;
				outer = 100 + total - baseInner;
				inner = 100 + baseInner;
			}
			else
			{
				return;
			}

			// 攻击总和 = 破体显示值 + 破气显示值 - 100
			int sum = outer + inner - 100;

			TextMeshProUGUI title = AccessTools.Field(typeof(Game.Views.MouseTips.TooltipCombatSkill), "attackPropertyTitleText").GetValue(__instance) as TextMeshProUGUI;
			if (title != null)
			{
				// 游戏的颜色名称占位符 <color=#brightblue> 需经 ColorReplace() 替换为真实 hex，否则 TMP 无法解析
				title.text = (title.text + "  <color=#brightblue>" + sum + "%</color>").ColorReplace();
			}
		}
	}
}
