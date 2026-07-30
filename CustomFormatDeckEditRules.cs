using HarmonyLib;
using System.Collections.Generic;
using Wizard;
using Wizard.DeckCardEdit;

namespace Shadowbus
{
    internal static class CustomFormatDeckEditRules
    {
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

            List<int> candidate = __instance.SelectionAreaList.IdList;
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
            List<int> candidate = controller.SelectionAreaList.IdList;
            candidate.Add(cardNo);
            return Validate(controller, candidate);
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

            ShowViolation(definition, violation, cardMaster);
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Rejected a deck edit for {definition.Id}: " +
                violation.ToLogMessage() + ".");
            return false;
        }

        private static void ShowViolation(
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
            dialog.SetTitleLabel("无法加入卡牌");
            dialog.SetText(BuildMessage(definition, violation, cardMaster), true);
            dialog.SetButtonLayout(DialogBase.ButtonLayout.OkBtn);
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
