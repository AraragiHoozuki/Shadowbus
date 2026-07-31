using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Wizard.RoomMatch;

namespace Shadowbus
{
    [HarmonyPatch(typeof(TwoPickClassSelectBase), nameof(TwoPickClassSelectBase.onClickClassImage))]
    internal static class P2PTwoPickClassDescriptionPatch
    {
        private static void Postfix(TwoPickClassSelectBase __instance, int inClassId)
        {
            if (!P2PRuntime.IsTwoPickRoom || __instance == null)
            {
                return;
            }

            string description = P2PTwoPickRules.GetClassDescription(inClassId);
            if (string.IsNullOrEmpty(description))
            {
                return;
            }

            TwoPickClassSelectView view =
                __instance.GetComponent<TwoPickClassSelectView>();
            if (view?.ChoiceClassInfoLabel != null)
            {
                view.ChoiceClassInfoLabel.text = description;
            }
        }
    }

    [HarmonyPatch]
    internal static class P2PTwoPickDeckSizePatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TwoPickCardSelectBase), "Init");
            yield return AccessTools.Method(typeof(TwoPickCardSelectBase), "NextCardSelect");
            yield return AccessTools.Method(typeof(TwoPickCardSelectBase), "CardDecide");
            yield return AccessTools.Method(typeof(RoomTwoPickDeckSelect), "CreateGameObject");
            yield return AccessTools.Method(typeof(RoomTwoPickPlayerDisplay), "UpdateDeckCreateNumber");
            yield return AccessTools.Method(typeof(RoomTwoPickUICommon), "OnSetupFinish");
            yield return AccessTools.Method(typeof(RoomBase), "InitializeDeckConfirm");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            return ReplaceDeckSize(instructions, original);
        }

        internal static IEnumerable<CodeInstruction> ReplaceDeckSize(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            MethodInfo getter = AccessTools.PropertyGetter(
                typeof(P2PTwoPickRules),
                nameof(P2PTwoPickRules.FinalDeckSize));
            int replacementCount = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (LoadsThirty(instruction))
                {
                    replacementCount++;
                    yield return new CodeInstruction(OpCodes.Call, getter)
                        .MoveLabelsFrom(instruction)
                        .MoveBlocksFrom(instruction);
                }
                else
                {
                    yield return instruction;
                }
            }

            if (replacementCount == 0)
            {
                Plugin.Logger.LogWarning(
                    $"[P2P] No Two Pick deck-size constant was found in {original}.");
            }
        }

        private static bool LoadsThirty(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_S)
            {
                return Convert.ToInt32(instruction.operand) == 30;
            }
            if (instruction.opcode == OpCodes.Ldc_I4)
            {
                return Convert.ToInt32(instruction.operand) == 30;
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class P2PTwoPickCompletionPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo cardSet = AccessTools.Method(typeof(TwoPickCardSelectBase), "CardSet");
            return AccessTools.EnumeratorMoveNext(cardSet);
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            return P2PTwoPickDeckSizePatches.ReplaceDeckSize(instructions, original);
        }
    }
}
