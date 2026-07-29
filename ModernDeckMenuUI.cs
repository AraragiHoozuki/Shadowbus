using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Wizard;

namespace Shadowbus
{
    internal sealed class ModernDeckMenuUI : MonoBehaviour
    {
        private const string CustomButtonNamePrefix = "ShadowbusCustomDeckButton_";
        private const float CustomButtonVerticalSpacing = 160f;
        private readonly List<UIButton> customButtons = new List<UIButton>();
        private readonly Dictionary<UIButton, UILabel> customButtonLabels =
            new Dictionary<UIButton, UILabel>();
        private MyPageItemCard owner;

        internal static void Attach(MyPageItemCard item)
        {
            if (item == null)
            {
                return;
            }

            ModernDeckMenuUI controller = item.GetComponent<ModernDeckMenuUI>() ??
                item.gameObject.AddComponent<ModernDeckMenuUI>();
            controller.owner = item;
            controller.EnsureButtons();
            controller.ApplyLayout();
        }

        internal static void Refresh(MyPageItemCard item)
        {
            ModernDeckMenuUI controller = item?.GetComponent<ModernDeckMenuUI>();
            if (controller == null)
            {
                Attach(item);
                return;
            }
            controller.ApplyLayout();
        }

        private void EnsureButtons()
        {
            if (owner?._deckUnlimitedButtons == null)
            {
                return;
            }

            foreach (UIButton source in owner._deckUnlimitedButtons.Where(button => button != null))
            {
                ClearLayoutConstraints(source.gameObject);
                Transform parent = source.transform.parent;
                List<CustomFormatDefinition> definitions = CustomFormats.All
                    .Where(definition => definition.Id != CustomFormats.UnlimitedId &&
                        definition.Supports(CustomFormatContextKind.DeckList))
                    .ToList();
                for (int index = 0; index < definitions.Count; index++)
                {
                    CustomFormatDefinition definition = definitions[index];
                    string buttonName = CustomButtonNamePrefix + definition.Id;
                    UIButton existing = parent
                        .GetComponentsInChildren<UIButton>(true)
                        .FirstOrDefault(button => button.name == buttonName);
                    if (existing != null)
                    {
                        if (!customButtons.Contains(existing))
                        {
                            customButtons.Add(existing);
                        }
                        ConfigureCustomButtonLabel(existing, definition);
                        continue;
                    }

                    GameObject cloneObject = UnityEngine.Object.Instantiate(source.gameObject);
                    cloneObject.name = buttonName;
                    cloneObject.transform.SetParent(parent, false);
                    cloneObject.transform.localPosition = source.transform.localPosition +
                        new Vector3(0f, -CustomButtonVerticalSpacing * (index + 1), 0f);
                    cloneObject.transform.localRotation = source.transform.localRotation;
                    cloneObject.transform.localScale = source.transform.localScale;
                    cloneObject.layer = source.gameObject.layer;
                    ClearLayoutConstraints(cloneObject);

                    UIButton clone = cloneObject.GetComponent<UIButton>();
                    clone.onClick.Clear();
                    clone.onClick.Add(new EventDelegate(
                        () => OpenCustomDeckList(definition.Id)));
                    ConfigureCustomButtonLabel(clone, definition);
                    cloneObject.SetActive(true);
                    customButtons.Add(clone);
                }
            }
        }

        private void ConfigureCustomButtonLabel(
            UIButton button,
            CustomFormatDefinition definition)
        {
            foreach (UILocalize localize in
                button.GetComponentsInChildren<UILocalize>(true))
            {
                localize.enabled = false;
                UnityEngine.Object.Destroy(localize);
            }

            UILabel label = button.GetComponentsInChildren<UILabel>(true)
                .FirstOrDefault(item => item.name == "ShadowbusCustomDeckLabel");
            if (label == null)
            {
                UILabel labelSource = button.GetComponentsInChildren<UILabel>(true)
                    .Where(item => item != null)
                    .OrderByDescending(item => item.fontSize)
                    .FirstOrDefault() ??
                    owner.GetComponentsInChildren<UILabel>(true)
                        .FirstOrDefault(item => item != null);
                if (labelSource == null)
                {
                    Plugin.Logger.LogError(
                        $"[CustomFormats] Cannot create the {definition.Id} deck button label.");
                    return;
                }

                GameObject labelObject = UnityEngine.Object.Instantiate(labelSource.gameObject);
                labelObject.name = "ShadowbusCustomDeckLabel";
                labelObject.transform.SetParent(button.transform, false);
                label = labelObject.GetComponent<UILabel>();
            }

            foreach (UILabel original in button.GetComponentsInChildren<UILabel>(true))
            {
                if (original != label)
                {
                    original.enabled = false;
                }
            }
            foreach (UILocalize localize in
                label.GetComponentsInChildren<UILocalize>(true))
            {
                localize.enabled = false;
                UnityEngine.Object.Destroy(localize);
            }

            label.name = "ShadowbusCustomDeckLabel";
            label.transform.SetParent(button.transform, false);
            label.transform.localRotation = Quaternion.identity;
            label.transform.localScale = Vector3.one;
            label.transform.localPosition = GetButtonVisualCenter(button);
            label.pivot = UIWidget.Pivot.Center;
            label.width = 260;
            label.height = 52;
            label.fontSize = Math.Max(label.fontSize, 24);
            label.depth = NGUITools.CalculateNextDepth(button.gameObject) + 1;
            label.color = Color.white;
            label.alpha = 1f;
            label.overflowMethod = UILabel.Overflow.ShrinkContent;
            label.maxLineCount = 1;
            label.text = definition.DisplayName;
            label.enabled = true;
            label.gameObject.SetActive(true);
            customButtonLabels[button] = label;
        }

        private static Vector3 GetButtonVisualCenter(UIButton button)
        {
            UISprite background = button.GetComponentsInChildren<UISprite>(true)
                .OrderByDescending(sprite => (long)sprite.width * sprite.height)
                .FirstOrDefault();
            return background == null
                ? Vector3.zero
                : button.transform.InverseTransformPoint(background.transform.position);
        }

        private Vector3 PositionUnlimitedButton(UIButton source)
        {
            UIButton replacement = owner._deckRotationButtons?
                .FirstOrDefault(button => button != null &&
                    button.transform.parent == source.transform.parent);
            if (replacement != null)
            {
                source.transform.localPosition = replacement.transform.localPosition;
            }
            return source.transform.localPosition;
        }

        private void ApplyLayout()
        {
            if (owner == null)
            {
                return;
            }

            SetButtonsActive(owner._deckUnlimitedButtons, true);
            ApplyButtonPositions();
            SetButtonsActive(owner._deckRotationButtons, false);
            SetButtonsActive(owner._deckIntroductionButtons, false);
            SetButtonActive(owner._deckPreRotationButton, false);
            SetButtonActive(owner._deckCrossoverButton, false);
            SetButtonActive(owner._deckMyRotationButton, false);

            foreach (UIButton button in customButtons)
            {
                if (button != null)
                {
                    button.gameObject.SetActive(true);
                }
            }
        }

        private void LateUpdate()
        {
            if (owner?._deckEditMenuRoot != null &&
                owner._deckEditMenuRoot.activeInHierarchy)
            {
                ApplyButtonPositions();
                RefreshCustomButtonLabels();
            }
        }

        private void RefreshCustomButtonLabels()
        {
            foreach (KeyValuePair<UIButton, UILabel> item in customButtonLabels)
            {
                UIButton button = item.Key;
                UILabel label = item.Value;
                if (button == null || label == null)
                {
                    continue;
                }

                string formatId = button.name.Substring(CustomButtonNamePrefix.Length);
                string expected = CustomFormats.Get(formatId).DisplayName;
                if (!string.Equals(label.text, expected, StringComparison.Ordinal))
                {
                    label.text = expected;
                }
                if (!label.enabled)
                {
                    label.enabled = true;
                }
                if (!label.gameObject.activeSelf)
                {
                    label.gameObject.SetActive(true);
                }
            }
        }

        private void ApplyButtonPositions()
        {
            foreach (UIButton button in owner?._deckUnlimitedButtons ?? Array.Empty<UIButton>())
            {
                if (button == null)
                {
                    continue;
                }

                Vector3 anchor = PositionUnlimitedButton(button);
                Transform parent = button.transform.parent;
                List<CustomFormatDefinition> definitions = CustomFormats.All
                    .Where(definition => definition.Id != CustomFormats.UnlimitedId &&
                        definition.Supports(CustomFormatContextKind.DeckList))
                    .ToList();
                for (int index = 0; index < definitions.Count; index++)
                {
                    string buttonName = CustomButtonNamePrefix + definitions[index].Id;
                    UIButton customButton = customButtons.FirstOrDefault(candidate =>
                        candidate != null &&
                        candidate.name == buttonName &&
                        candidate.transform.parent == parent);
                    if (customButton != null)
                    {
                        customButton.transform.localPosition = anchor + new Vector3(
                            0f,
                            -CustomButtonVerticalSpacing * (index + 1),
                            0f);
                    }
                }
            }
        }

        private static void ClearLayoutConstraints(GameObject root)
        {
            foreach (UIAnchor anchor in root.GetComponentsInChildren<UIAnchor>(true))
            {
                anchor.enabled = false;
            }
            foreach (UIRect rect in root.GetComponentsInChildren<UIRect>(true))
            {
                rect.leftAnchor.target = null;
                rect.rightAnchor.target = null;
                rect.bottomAnchor.target = null;
                rect.topAnchor.target = null;
                rect.ResetAnchors();
            }
        }

        private static void SetButtonsActive(IEnumerable<UIButton> buttons, bool active)
        {
            if (buttons == null)
            {
                return;
            }
            foreach (UIButton button in buttons)
            {
                SetButtonActive(button, active);
            }
        }

        private static void SetButtonActive(UIButton button, bool active)
        {
            if (button != null)
            {
                button.gameObject.SetActive(active);
            }
        }

        private static void OpenCustomDeckList(string formatId)
        {
            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_BTN_DECIDE, false);
            CustomFormatContext.OpenDeckList(formatId);
        }

        [HarmonyPatch(typeof(MyPageItemCard), nameof(MyPageItemCard.Initialize))]
        [HarmonyPostfix]
        private static void MyPageItemCard_Initialize_Postfix(MyPageItemCard __instance)
        {
            Attach(__instance);
        }

        [HarmonyPatch(typeof(MyPageItemCard), nameof(MyPageItemCard.Show))]
        [HarmonyPostfix]
        private static void MyPageItemCard_Show_Postfix(MyPageItemCard __instance)
        {
            Refresh(__instance);
        }

        [HarmonyPatch(typeof(MyPageItemCard), "ShowDeckMenu")]
        [HarmonyPostfix]
        private static void MyPageItemCard_ShowDeckMenu_Postfix(MyPageItemCard __instance)
        {
            Refresh(__instance);
        }

        [HarmonyPatch(typeof(MyPageItemCard), "OnPushDeckEditUnlimited")]
        [HarmonyPrefix]
        private static void MyPageItemCard_OnPushDeckEditUnlimited_Prefix()
        {
            CustomFormatContext.DeckListFormatId = CustomFormats.UnlimitedId;
        }

        [HarmonyPatch(typeof(DeckListUI), "UpdateTopBarText")]
        [HarmonyPostfix]
        private static void DeckListUI_UpdateTopBarText_Postfix(DeckListUI __instance)
        {
            CustomFormatDefinition definition = CustomFormatContext.DeckListFormat;
            if (definition.Id == CustomFormats.UnlimitedId || __instance?._topBar == null)
            {
                return;
            }

            __instance._topBar.SetTitleLabel(definition.DisplayName, false);
            __instance._topBar.SetTitleLabelWidth(420);
        }
    }
}
