using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Wizard;
using Wizard.DeckCardEdit;

namespace Shadowbus
{
    internal static class DeckFormatUI
    {
        [HarmonyPatch(typeof(DeckCardEditUI), nameof(DeckCardEditUI.SetDeckEditParameter))]
        [HarmonyPostfix]
        private static void DeckCardEditUI_SetDeckEditParameter_Postfix(
            DeckData deck,
            ConventionDeckList conventionDeckList)
        {
            if (deck == null || conventionDeckList != null || deck.Format != Format.Unlimited)
            {
                return;
            }

            CustomFormatContext.DeckEditFormatId =
                CustomDeckStore.GetDeckFormatId(deck.GetDeckID());
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Editing deck {deck.GetDeckID()} as " +
                $"{CustomFormatContext.DeckEditFormatId}.");
        }

        [HarmonyPatch(typeof(DeckCreateMenuUI), nameof(DeckCreateMenuUI.ShowDeckCreateMenu))]
        [HarmonyPostfix]
        private static void DeckCreateMenuUI_ShowDeckCreateMenu_Postfix(
            DeckData deck,
            ConventionDeckList conventionDeckList)
        {
            if (deck == null || conventionDeckList != null ||
                deck.Format != Format.Unlimited || !deck.IsNoCard())
            {
                return;
            }

            List<CustomFormatDefinition> definitions = CustomFormats.All.ToList();
            if (definitions.Count == 0)
            {
                return;
            }

            int selectedIndex = Math.Max(
                0,
                definitions.FindIndex(definition => string.Equals(
                    definition.Id,
                    CustomFormatContext.DeckEditFormatId,
                    StringComparison.OrdinalIgnoreCase)));
            int pendingIndex = selectedIndex;
            DialogBase selector = null;
            selector = DrumrollDialog.Create(
                definitions.Select(definition => definition.DisplayName).ToList(),
                selectedIndex,
                index => pendingIndex = index,
                null,
                null,
                string.Empty);
            selector.SetTitleLabel("\u9009\u62e9\u724c\u7ec4\u8d5b\u5236");
            selector.SetButtonLayout(DialogBase.ButtonLayout.DecisionBtn);
            selector.SetPanelDepth(2000, false);
            selector.onPushButton1 = () =>
            {
                CustomFormatContext.DeckEditFormatId = definitions[pendingIndex].Id;
                selector.SetDisp(false);
                Plugin.Logger.LogInfo(
                    $"[CustomFormats] New deck will use " +
                    $"{CustomFormatContext.DeckEditFormatId}.");
            };
            selector.onCloseWithoutSelect = () => selector.SetDisp(false);
            selector.ResetBackViewAlpha();
        }

        [HarmonyPatch(typeof(DeckCardEditUI), nameof(DeckCardEditUI.onFirstStart))]
        [HarmonyPostfix]
        private static void DeckCardEditUI_onFirstStart_Postfix(DeckCardEditUI __instance)
        {
            if (__instance == null || DeckCardEditUI.EditDeckFormat != Format.Unlimited)
            {
                return;
            }

            TopBar topBar = __instance.GetComponentsInChildren<TopBar>(true).FirstOrDefault();
            if (topBar == null)
            {
                Plugin.Logger.LogWarning(
                    "[CustomFormats] Could not find the deck editor top bar.");
                return;
            }

            topBar.SetTitleLabel(
                "\u724c\u7ec4\u7f16\u8f91 - " +
                CustomFormatContext.DeckEditFormat.DisplayName,
                false);
            topBar.SetTitleLabelWidth(460);
        }
    }
}
