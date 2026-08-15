using HarmonyLib;
using System.Collections.Generic;
using Wizard;

namespace Shadowbus
{
    /// <summary>
    /// Lets the original AI evaluate custom cards that have no entry in the game's AI data.
    /// </summary>
    [HarmonyPatch(typeof(AIEvaluateTagExtension), nameof(AIEvaluateTagExtension.EvaluatePlayValue))]
    public static class AIEvaluateTagCompatibility
    {
        [HarmonyPrefix]
        public static bool AIEvaluateTagExtension_EvaluatePlayValue_Prefix(
            AIVirtualCard card,
            List<int> playPtn,
            AISituationInfo situation,
            ref float __result)
        {
            if (card == null)
            {
                __result = 0f;
                return false;
            }

            if (card.AIData != null)
            {
                return true;
            }

            // The original method dereferences AIData unconditionally. A missing AI data
            // record contributes no expression bonus, but tag and board bonuses still apply.
            __result = (
                card.GetPlayBonus(playPtn, situation) +
                card.GetFanfareBonus(playPtn, situation) +
                AIEvaluateBonusFromOhterUtility.GetAllyPlayBonus(card, playPtn, situation) +
                AIEvaluateBonusFromOhterUtility.GetEnemyPlayBonus(card, playPtn, situation)) *
                card.GetPlayBonusRate(playPtn, situation);
            return false;
        }
    }
}
