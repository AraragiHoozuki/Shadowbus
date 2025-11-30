using Cute;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wizard;

namespace Shadowbus
{
    public class DebugPatcher
    {
        [HarmonyPatch(typeof(Wizard.Battle.ActionProcessor), nameof(Wizard.Battle.ActionProcessor.PlayCard))]
        [HarmonyPrefix]
        public static bool ActionProcessor_PlayCard(Wizard.Battle.ActionProcessor __instance, ref BattleCardBase card)
        {
            Plugin.Logger.LogInfo($"{card.BaseParameter.CardName} is played");
            return true;
        }

        [HarmonyPatch(typeof(TouchControl), nameof(TouchControl.StartOpenHandDetail))]
        [HarmonyPrefix]
        public static bool TouchControl_StartOpenHandDetail(TouchControl __instance, ref BattleCardBase card)
        {
            Plugin.Logger.LogInfo($"{card.BaseParameter.CardName} is selected");
            Plugin.Instance.SelectedCard = card;
            return true;
        }

        
    }
}
