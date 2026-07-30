using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Wizard;
using Wizard.DeckCardEdit;
using Wizard.Dialog.Setting;

namespace Shadowbus
{
    internal static class DeckFormatUI
    {
        private const int FormatDetailsButtonWidth = 64;
        private const int MinimumFormatSelectorWidth = 120;
        private const int InlineControlGap = 8;

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

        [HarmonyPatch(typeof(DeckCreateMenuUI), "Start")]
        [HarmonyPostfix]
        private static void DeckCreateMenuUI_Start_Postfix(DeckCreateMenuUI __instance)
        {
            DeckData deck = DeckCardEditUI.CurrentDeckData;
            if (__instance == null || deck == null ||
                __instance._conventionDeckList != null ||
                __instance._format != Format.Unlimited || !deck.IsNoCard())
            {
                return;
            }

            CustomFormats.ReloadForUi("new deck menu");
            List<CustomFormatDefinition> definitions = CustomFormats.All.ToList();
            SettingBase settingPrefab = UIManager.GetInstance().OptionSettingPrefab;
            GameObject selectPrefab = settingPrefab?._itemSelect;
            if (definitions.Count == 0 || selectPrefab == null)
            {
                Plugin.Logger.LogWarning(
                    "[CustomFormats] Could not create the inline deck format selector.");
                return;
            }

            ItemSelect selector = NGUITools.AddChild(
                __instance.gameObject,
                selectPrefab).GetComponent<ItemSelect>();
            selector.name = "ShadowbusDeckFormatSelect";
            selector._isOpenDirectionUp = true;
            selector._title.gameObject.SetActive(false);
            selector.SetActive_SeparatorLine(false);
            selector.SetPossibleValues(
                definitions.Select(definition => definition.DisplayName).ToList(),
                true);
            selector.SetValue(CustomFormatContext.DeckEditFormat.DisplayName);
            selector.AddChangeCallback(() =>
            {
                string selectedName = selector.GetValue();
                CustomFormatDefinition selected = definitions.FirstOrDefault(definition =>
                    string.Equals(
                        definition.DisplayName,
                        selectedName,
                        StringComparison.Ordinal));
                if (selected == null)
                {
                    return;
                }
                CustomFormatContext.DeckEditFormatId = selected.Id;
                Plugin.Logger.LogInfo(
                    $"[CustomFormats] New deck will use " +
                    $"{CustomFormatContext.DeckEditFormatId}.");
            });

            LayoutInlineFormatControls(__instance, selector, definitions);
        }

        private static void LayoutInlineFormatControls(
            DeckCreateMenuUI menu,
            ItemSelect selector,
            List<CustomFormatDefinition> definitions)
        {
            if (menu._btnLibrary == null || menu.m_btnCreateNew == null ||
                menu.m_btnAutoDeck == null || selector._button == null)
            {
                Plugin.Logger.LogWarning(
                    "[CustomFormats] Could not position the inline format controls.");
                return;
            }

            Vector3 libraryPosition = menu.transform.InverseTransformPoint(
                menu._btnLibrary.transform.position);
            Vector3 rightColumnPosition = menu.transform.InverseTransformPoint(
                menu.m_btnAutoDeck.transform.position);
            Vector3 slotPosition = new Vector3(
                rightColumnPosition.x,
                libraryPosition.y,
                libraryPosition.z);

            int slotWidth = GetButtonWidth(menu._btnLibrary.gameObject);
            int detailsWidth = Mathf.Min(FormatDetailsButtonWidth,
                Mathf.Max(48, slotWidth / 3));
            int selectorWidth = Mathf.Max(
                MinimumFormatSelectorWidth,
                slotWidth - detailsWidth - InlineControlGap);
            int combinedWidth = selectorWidth + InlineControlGap + detailsWidth;

            ResizeFormatSelector(selector, selectorWidth);
            Vector3 selectorButtonPosition = slotPosition + new Vector3(
                (selectorWidth - combinedWidth) * 0.5f,
                0f,
                0f);
            Vector3 selectorButtonWorldPosition =
                menu.transform.TransformPoint(selectorButtonPosition);
            selector.transform.position +=
                selectorButtonWorldPosition - selector._button.transform.position;

            UIButton detailsButton = CreateDetailsButton(
                menu,
                detailsWidth,
                slotPosition + new Vector3(
                    (combinedWidth - detailsWidth) * 0.5f,
                    0f,
                    0f));
            if (detailsButton == null)
            {
                return;
            }

            UIEventListener.Get(detailsButton.gameObject).onClick = _ =>
            {
                GameMgr.GetIns().GetSoundMgr().PlaySe(
                    Se.TYPE.SYS_COMMON_BUTTON,
                    false);
                string selectedName = selector.GetValue();
                CustomFormatDefinition selected = definitions.FirstOrDefault(definition =>
                    string.Equals(
                        definition.DisplayName,
                        selectedName,
                        StringComparison.Ordinal)) ?? CustomFormatContext.DeckEditFormat;
                CustomFormatDetailsDialog.Show(selected);
            };
        }

        private static UIButton CreateDetailsButton(
            DeckCreateMenuUI menu,
            int width,
            Vector3 localPosition)
        {
            GameObject source = menu.m_btnAutoDeck?.gameObject;
            if (source == null)
            {
                return null;
            }

            GameObject buttonObject = UnityEngine.Object.Instantiate(source);
            buttonObject.name = "ShadowbusFormatDetailsButton";
            buttonObject.transform.SetParent(menu.transform, false);
            buttonObject.transform.localPosition = localPosition;
            buttonObject.transform.localRotation = source.transform.localRotation;
            buttonObject.transform.localScale = source.transform.localScale;
            buttonObject.SetActive(true);

            foreach (UILocalize localize in
                buttonObject.GetComponentsInChildren<UILocalize>(true))
            {
                localize.enabled = false;
                UnityEngine.Object.Destroy(localize);
            }
            foreach (StaticTextForUILabel staticText in
                buttonObject.GetComponentsInChildren<StaticTextForUILabel>(true))
            {
                staticText.enabled = false;
                UnityEngine.Object.Destroy(staticText);
            }

            UIButton button = buttonObject.GetComponent<UIButton>();
            if (button == null)
            {
                UnityEngine.Object.Destroy(buttonObject);
                return null;
            }
            button.onClick.Clear();
            UIEventListener.Get(buttonObject).onClick = null;
            ResizeButton(buttonObject, width);

            foreach (UILabel label in
                buttonObject.GetComponentsInChildren<UILabel>(true))
            {
                label.text = "\u8be6\u60c5";
                label.width = Mathf.Max(36, width - 10);
                label.maxLineCount = 1;
                label.overflowMethod = UILabel.Overflow.ShrinkContent;
            }
            return button;
        }

        private static void ResizeFormatSelector(ItemSelect selector, int width)
        {
            ResizeButton(selector._button.gameObject, width);
            Transform center = selector._state?.transform.Find("center");
            UILabel valueLabel = center?.GetComponent<UILabel>();
            if (valueLabel != null)
            {
                valueLabel.width = Mathf.Max(70, width - 34);
                valueLabel.maxLineCount = 1;
                valueLabel.overflowMethod = UILabel.Overflow.ShrinkContent;
            }
        }

        private static void ResizeButton(GameObject buttonObject, int width)
        {
            UISprite surface = buttonObject.GetComponent<UISprite>() ??
                buttonObject.GetComponentsInChildren<UISprite>(true)
                    .OrderByDescending(sprite => sprite.width)
                    .FirstOrDefault();
            if (surface != null)
            {
                surface.width = width;
                surface.ResetAndUpdateAnchors();
            }

            BoxCollider collider = buttonObject.GetComponent<BoxCollider>();
            if (collider != null)
            {
                Vector3 size = collider.size;
                size.x = width;
                collider.size = size;
            }
        }

        private static int GetButtonWidth(GameObject buttonObject)
        {
            UISprite surface = buttonObject?.GetComponent<UISprite>() ??
                buttonObject?.GetComponentsInChildren<UISprite>(true)
                    .OrderByDescending(sprite => sprite.width)
                    .FirstOrDefault();
            if (surface != null && surface.width > 0)
            {
                return surface.width;
            }

            BoxCollider collider = buttonObject?.GetComponent<BoxCollider>();
            return collider == null
                ? 260
                : Mathf.Max(1, Mathf.RoundToInt(collider.size.x));
        }

        [HarmonyPatch(typeof(DeckCardEditUI), nameof(DeckCardEditUI.onFirstStart))]
        [HarmonyPrefix]
        private static void DeckCardEditUI_onFirstStart_Prefix()
        {
            CustomFormats.ReloadForUi("deck editor");
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

        [HarmonyPatch(
            typeof(DeckUI),
            nameof(DeckUI.UpdateView),
            new Type[]
            {
                typeof(DeckData),
                typeof(DeckUI.eViewType),
                typeof(bool),
                typeof(bool)
            })]
        [HarmonyPostfix]
        private static void DeckUI_UpdateView_Postfix(
            DeckUI __instance,
            DeckData deckData,
            DeckUI.eViewType viewType)
        {
            DeckFormatBadge.Refresh(__instance, deckData, viewType);
        }
    }

    internal sealed class DeckFormatBadge : MonoBehaviour
    {
        private const string ObjectName = "ShadowbusDeckFormatBadge";
        private const int BadgeWidth = 118;
        private const int BadgeHeight = 28;
        private const float BadgeMargin = 8f;

        private UILabel label;

        internal static void Refresh(
            DeckUI deckUI,
            DeckData deckData,
            DeckUI.eViewType viewType)
        {
            if (deckUI == null)
            {
                return;
            }

            DeckFormatBadge badge = deckUI.GetComponent<DeckFormatBadge>() ??
                deckUI.gameObject.AddComponent<DeckFormatBadge>();
            badge.RefreshView(deckUI, deckData, viewType);
        }

        private void RefreshView(
            DeckUI deckUI,
            DeckData deckData,
            DeckUI.eViewType viewType)
        {
            EnsureLabel(deckUI);
            if (label == null)
            {
                return;
            }

            label.gameObject.SetActive(false);
            if (viewType != DeckUI.eViewType.Normal || deckData == null ||
                deckData.DeckAttributeType != DeckAttributeType.CustomDeck ||
                deckData.IsNoCard())
            {
                return;
            }

            string formatId = CustomDeckStore.GetDeckFormatId(deckData.GetDeckID());
            label.text = CustomFormats.Get(formatId).DisplayName;
            label.gameObject.SetActive(true);
        }

        private void EnsureLabel(DeckUI deckUI)
        {
            if (label != null)
            {
                return;
            }

            Transform existing = deckUI.transform.Find(ObjectName);
            if (existing != null)
            {
                label = existing.GetComponent<UILabel>();
                if (label != null)
                {
                    return;
                }
            }

            GameObject normalRoot = deckUI._rootNormalDeckView;
            UILabel source = deckUI._appealLabelRight ??
                deckUI._appealLabelLeft ?? deckUI._deckName;
            if (normalRoot == null || source == null)
            {
                Plugin.Logger.LogWarning(
                    "[CustomFormats] Could not create a deck format badge: " +
                    "the normal deck root or source label is missing.");
                return;
            }

            Bounds bounds = NGUIMath.CalculateRelativeWidgetBounds(
                normalRoot.transform,
                normalRoot.transform,
                true);
            int depth = deckUI.GetComponentsInChildren<UIWidget>(true)
                .Select(widget => widget.depth)
                .DefaultIfEmpty(source.depth)
                .Max() + 1;

            GameObject badgeObject = UnityEngine.Object.Instantiate(source.gameObject);
            badgeObject.name = ObjectName;
            badgeObject.transform.SetParent(normalRoot.transform, false);
            badgeObject.transform.localRotation = Quaternion.identity;
            badgeObject.transform.localScale = Vector3.one;
            badgeObject.layer = normalRoot.layer;

            DisableAutomaticText(badgeObject);
            label = badgeObject.GetComponent<UILabel>();
            if (label == null)
            {
                UnityEngine.Object.Destroy(badgeObject);
                return;
            }

            ClearAnchors(label);
            label.pivot = UIWidget.Pivot.TopRight;
            label.alignment = NGUIText.Alignment.Center;
            label.width = BadgeWidth;
            label.height = BadgeHeight;
            label.fontSize = Mathf.Clamp(source.fontSize, 16, 22);
            label.overflowMethod = UILabel.Overflow.ShrinkContent;
            label.maxLineCount = 1;
            label.effectStyle = UILabel.Effect.Outline;
            label.effectColor = new Color(0f, 0f, 0f, 0.9f);
            label.depth = depth;
            label.transform.localPosition = new Vector3(
                bounds.max.x - BadgeMargin,
                bounds.max.y - BadgeMargin,
                source.transform.localPosition.z);
            label.gameObject.SetActive(false);
        }

        private static void ClearAnchors(UIRect rect)
        {
            rect.leftAnchor.target = null;
            rect.rightAnchor.target = null;
            rect.bottomAnchor.target = null;
            rect.topAnchor.target = null;
            rect.ResetAnchors();
        }

        private static void DisableAutomaticText(GameObject gameObject)
        {
            foreach (UILocalize localize in
                gameObject.GetComponentsInChildren<UILocalize>(true))
            {
                localize.enabled = false;
                UnityEngine.Object.Destroy(localize);
            }
            foreach (StaticTextForUILabel staticText in
                gameObject.GetComponentsInChildren<StaticTextForUILabel>(true))
            {
                staticText.enabled = false;
                UnityEngine.Object.Destroy(staticText);
            }
        }
    }
}
