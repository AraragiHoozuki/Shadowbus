using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Wizard;
using Wizard.DeckCardEdit;

namespace Shadowbus
{
    internal static class CustomFormatDeckEditRules
    {
        [HarmonyPatch(
            typeof(CardBundleController),
            "DECK_CARD_NUM_EDIT_MAX",
            MethodType.Getter)]
        [HarmonyPrefix]
        private static bool CardBundleController_DECK_CARD_NUM_EDIT_MAX_Prefix(
            ref int __result)
        {
            if (!ShouldValidate())
            {
                return true;
            }

            __result = GetDeckSizeLimit();
            return false;
        }

        [HarmonyPatch(
            typeof(CardBundleController),
            nameof(CardBundleController.InsertToSelectionArea))]
        [HarmonyPrefix]
        private static bool CardBundleController_InsertToSelectionArea_Prefix(
            CardBundleController __instance,
            CardObject card,
            ref int __result)
        {
            if (card == null || !ShouldValidate())
            {
                return true;
            }

            List<int> candidate = new List<int>(
                __instance.SelectionAreaList.IdList);
            candidate.Add(card.CardId);
            if (Validate(__instance, candidate))
            {
                return true;
            }

            __result = -1;
            return false;
        }

        [HarmonyPatch(typeof(DeckCardEditUI), "AddCardForSwipe")]
        [HarmonyPrefix]
        private static bool DeckCardEditUI_AddCardForSwipe_Prefix(
            DeckCardEditUI __instance,
            int cardNo)
        {
            if (!ShouldValidate())
            {
                return true;
            }

            CardBundleController controller = __instance._deckCardBundle;
            List<int> candidate = new List<int>(
                controller.SelectionAreaList.IdList);
            candidate.Add(cardNo);
            return Validate(controller, candidate);
        }

        [HarmonyPatch(typeof(DeckSave), nameof(DeckSave.Start))]
        [HarmonyPrefix]
        private static bool DeckSave_Start_Prefix(
            DeckSave __instance,
            DeckSave.Option option)
        {
            if (!ShouldValidate() || option?.CardIds == null)
            {
                return true;
            }

            CustomFormatDefinition definition = CustomFormatContext.DeckEditFormat;
            IFormatBehavior formatBehavior = FormatBehaviorManager.Create(
                option.Format,
                option.ConventionDeckList);
            CardMaster cardMaster = CardMaster.GetInstance(formatBehavior.CardMasterId);
            if (CustomFormats.IsDeckCompliant(
                option.CardIds,
                definition,
                cardMaster,
                out CustomFormatViolation violation))
            {
                return true;
            }

            ShowViolation(
                "无法保存牌组",
                definition,
                violation,
                cardMaster);
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Rejected saving deck {option.DeckId} for " +
                $"{definition.Id}: {violation.ToLogMessage()}.");
            __instance.Destroy();
            return false;
        }

        [HarmonyPatch(typeof(DeckSave), nameof(DeckSave.Start))]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> DeckSave_Start_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceNativeDeckSizeLimit(instructions, nameof(DeckSave.Start));
        }

        [HarmonyPatch(typeof(DeckSave), "SaveRequest")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> DeckSave_SaveRequest_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceNativeDeckSizeLimit(instructions, "SaveRequest");
        }

        private static bool ShouldValidate()
        {
            return DeckCardEditUI.CurrentDeckData != null &&
                DeckCardEditUI.CurrentDeckData.DeckAttributeType ==
                    DeckAttributeType.CustomDeck &&
                DeckCardEditUI.EditDeckFormat == Format.Unlimited &&
                DeckCardEditUI._conventionDeckList == null;
        }

        private static bool Validate(
            CardBundleController controller,
            IEnumerable<int> candidate)
        {
            CustomFormatDefinition definition = CustomFormatContext.DeckEditFormat;
            CardMaster cardMaster = CardMaster.GetInstance(
                controller.FormatBehavior.CardMasterId);
            if (CustomFormats.IsDeckCompliant(
                candidate,
                definition,
                cardMaster,
                out CustomFormatViolation violation))
            {
                return true;
            }

            ShowViolation("无法加入卡牌", definition, violation, cardMaster);
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Rejected a deck edit for {definition.Id}: " +
                violation.ToLogMessage() + ".");
            return false;
        }

        private static void ShowViolation(
            string title,
            CustomFormatDefinition definition,
            CustomFormatViolation violation,
            CardMaster cardMaster)
        {
            UIManager uiManager = UIManager.GetInstance();
            if (uiManager == null || uiManager.isOpenDialog())
            {
                return;
            }

            DialogBase dialog = uiManager.CreateDialogClose(false, false);
            dialog.SetSize(DialogBase.Size.M);
            dialog.SetTitleLabel(title);
            dialog.SetText(BuildMessage(definition, violation, cardMaster), true);
            dialog.SetButtonLayout(DialogBase.ButtonLayout.OkBtn);
        }

        private static int GetDeckSizeLimit()
        {
            return CustomFormatContext.DeckEditFormat.DeckSizeLimit ?? int.MaxValue;
        }

        private static IEnumerable<CodeInstruction> ReplaceNativeDeckSizeLimit(
            IEnumerable<CodeInstruction> instructions,
            string methodName)
        {
            int replacementCount = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (LoadsInteger(instruction, 50))
                {
                    replacementCount++;
                    yield return new CodeInstruction(
                            OpCodes.Call,
                            AccessTools.Method(
                                typeof(CustomFormatDeckEditRules),
                                nameof(GetNativeSaveDeckSizeLimit)))
                        .MoveLabelsFrom(instruction)
                        .MoveBlocksFrom(instruction);
                    continue;
                }

                yield return instruction;
            }

            if (replacementCount != 1)
            {
                Plugin.Logger.LogWarning(
                    $"[CustomFormats] Expected one native deck-size limit in " +
                    $"DeckSave.{methodName}, but changed {replacementCount}.");
            }
        }

        private static int GetNativeSaveDeckSizeLimit()
        {
            return ShouldValidate() ? GetDeckSizeLimit() : 50;
        }

        private static bool LoadsInteger(CodeInstruction instruction, int value)
        {
            if (instruction.opcode == OpCodes.Ldc_I4 ||
                instruction.opcode == OpCodes.Ldc_I4_S)
            {
                return Convert.ToInt32(instruction.operand) == value;
            }
            return false;
        }

        private static string BuildMessage(
            CustomFormatDefinition definition,
            CustomFormatViolation violation,
            CardMaster cardMaster)
        {
            return $"该操作不符合「{definition.DisplayName}」的规则。\n" +
                CustomFormatViolationText.Describe(violation, cardMaster);
        }
    }
}
