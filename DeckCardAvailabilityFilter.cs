using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Shadowbus
{
    internal enum DeckCardAvailabilityMode
    {
        All,
        Original,
        Special
    }

    internal sealed class DeckCardAvailabilityFilter : MonoBehaviour
    {
        private const string BasicRowName = "ShadowbusCardAvailabilityBasic";
        private const string DetailRowName = "ShadowbusCardAvailabilityDetail";
        private const int CardTypeFilterIndex = 3;

        private static readonly MethodInfo RenameButtonSpriteMethod =
            AccessTools.Method(typeof(FilterController), "RenameBtnSprite");
        private static readonly FieldInfo OnValidateField =
            AccessTools.Field(typeof(FilterController), "OnValidate");
        private static readonly ConditionalWeakTable<UIBase_CardManager.FilterParameter, FilterModeMarker>
            ParameterModes =
                new ConditionalWeakTable<UIBase_CardManager.FilterParameter, FilterModeMarker>();

        private readonly List<UIButton[]> buttonRows = new List<UIButton[]>();
        private FilterController owner;
        private bool rowsCreated;

        internal DeckCardAvailabilityMode Mode { get; private set; } =
            DeckCardAvailabilityMode.All;

        internal static DeckCardAvailabilityFilter Attach(FilterController controller)
        {
            if (controller == null)
            {
                return null;
            }

            DeckCardAvailabilityFilter filter =
                controller.GetComponent<DeckCardAvailabilityFilter>() ??
                controller.gameObject.AddComponent<DeckCardAvailabilityFilter>();
            filter.owner = controller;
            return filter;
        }

        internal static void MarkParameter(
            UIBase_CardManager.FilterParameter parameter,
            DeckCardAvailabilityMode mode)
        {
            if (ReferenceEquals(parameter, null))
            {
                return;
            }

            ParameterModes.GetOrCreateValue(parameter).Mode = mode;
        }

        internal static bool TryGetParameterMode(
            UIBase_CardManager.FilterParameter parameter,
            out DeckCardAvailabilityMode mode)
        {
            FilterModeMarker marker;
            if (!ReferenceEquals(parameter, null) &&
                ParameterModes.TryGetValue(parameter, out marker))
            {
                mode = marker.Mode;
                return true;
            }

            mode = DeckCardAvailabilityMode.All;
            return false;
        }

        internal void EnsureRowsCreated()
        {
            if (rowsCreated || owner == null)
            {
                return;
            }

            if (owner.BtnArray == null ||
                owner.BtnArray.Length <= CardTypeFilterIndex ||
                owner.BtnArray[CardTypeFilterIndex] == null ||
                owner.BtnArray[CardTypeFilterIndex].Length < 4 ||
                owner._basicGrid == null ||
                owner._detailGrid == null)
            {
                Plugin.Logger.LogError(
                    "[DeckFilter] Cannot find the original card type filter row.");
                return;
            }

            FilterController.ButtonArray sourceArray =
                owner.BtnArray[CardTypeFilterIndex];
            UIButton[] sourceButtons = new UIButton[sourceArray.Length];
            for (int index = 0; index < sourceButtons.Length; index++)
            {
                sourceButtons[index] = sourceArray[index];
            }

            GameObject sourceRow = sourceButtons[0].transform.parent.gameObject;
            GameObject basicRow = null;
            GameObject detailRow = null;
            try
            {
                basicRow = CreateRow(
                    sourceRow,
                    sourceButtons,
                    owner._basicGrid,
                    BasicRowName);
                detailRow = CreateRow(
                    sourceRow,
                    sourceButtons,
                    owner._detailGrid,
                    DetailRowName);
                rowsCreated = true;
                RefreshButtonSprites();
                owner._basicGrid.Reposition();
                owner._detailGrid.Reposition();
                owner._scrollView.UpdateScrollbars();
                owner._scrollView.ResetPosition();
            }
            catch (Exception exception)
            {
                if (basicRow != null)
                {
                    Destroy(basicRow);
                }
                if (detailRow != null)
                {
                    Destroy(detailRow);
                }
                buttonRows.Clear();
                Plugin.Logger.LogError(
                    $"[DeckFilter] Failed to create the card availability filter: {exception}");
            }
        }

        internal void ResetMode()
        {
            Mode = DeckCardAvailabilityMode.All;
            RefreshButtonSprites();
        }

        private GameObject CreateRow(
            GameObject sourceRow,
            UIButton[] sourceButtons,
            FlexibleGrid targetGrid,
            string rowName)
        {
            GameObject row = Instantiate(sourceRow);
            row.name = rowName;
            row.transform.SetParent(targetGrid.transform, false);
            row.transform.SetSiblingIndex(0);

            UIButton[] clonedButtons = new UIButton[sourceButtons.Length];
            for (int index = 0; index < sourceButtons.Length; index++)
            {
                Transform clonedTransform = FindClonedTransform(
                    sourceRow.transform,
                    sourceButtons[index].transform,
                    row.transform);
                clonedButtons[index] = clonedTransform.GetComponent<UIButton>();
                ClearButtonClickEvents(clonedButtons[index]);
            }

            DisableAutomaticText(row);
            SetRowTitle(row, clonedButtons, "\u5361\u724c\u8303\u56f4");
            SetButtonText(clonedButtons[0], "\u6240\u6709\u5361");
            SetButtonText(clonedButtons[1], "\u539f\u7248\u53ef\u7528\u5361");
            SetButtonText(clonedButtons[3], "\u7279\u6b8a\u5361");

            Vector3 thirdButtonPosition = clonedButtons[2].transform.localPosition;
            clonedButtons[2].gameObject.SetActive(false);
            clonedButtons[3].transform.localPosition = thirdButtonPosition;

            UIButton[] visibleButtons =
            {
                clonedButtons[0],
                clonedButtons[1],
                clonedButtons[3]
            };
            BindButton(visibleButtons[0], DeckCardAvailabilityMode.All);
            BindButton(visibleButtons[1], DeckCardAvailabilityMode.Original);
            BindButton(visibleButtons[2], DeckCardAvailabilityMode.Special);
            buttonRows.Add(visibleButtons);
            row.SetActive(true);
            return row;
        }

        private void BindButton(UIButton button, DeckCardAvailabilityMode mode)
        {
            UIEventListener.Get(button.gameObject).onClick = delegate
            {
                SelectMode(mode);
            };
        }

        private void SelectMode(DeckCardAvailabilityMode mode)
        {
            Mode = mode;
            RefreshButtonSprites();
            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_TOGGLE_ON, false);
            (OnValidateField?.GetValue(owner) as Action)?.Invoke();
        }

        private void RefreshButtonSprites()
        {
            foreach (UIButton[] row in buttonRows)
            {
                RenameButtonSprite(row[0], Mode == DeckCardAvailabilityMode.All);
                RenameButtonSprite(row[1], Mode == DeckCardAvailabilityMode.Original);
                RenameButtonSprite(row[2], Mode == DeckCardAvailabilityMode.Special);
            }
        }

        private void RenameButtonSprite(UIButton button, bool selected)
        {
            if (button != null)
            {
                RenameButtonSpriteMethod?.Invoke(owner, new object[] { button, selected });
            }
        }

        private static void ClearButtonClickEvents(UIButton button)
        {
            button.onClick.Clear();
            UIEventListener listener = button.GetComponent<UIEventListener>();
            if (listener != null)
            {
                listener.onClick = null;
            }
        }

        private static void DisableAutomaticText(GameObject row)
        {
            foreach (UILocalize localize in row.GetComponentsInChildren<UILocalize>(true))
            {
                localize.enabled = false;
                Destroy(localize);
            }
            foreach (StaticTextForUILabel staticText in
                row.GetComponentsInChildren<StaticTextForUILabel>(true))
            {
                staticText.enabled = false;
                Destroy(staticText);
            }
        }

        private static void SetRowTitle(
            GameObject row,
            IEnumerable<UIButton> buttons,
            string text)
        {
            UILabel title = row.GetComponentsInChildren<UILabel>(true)
                .FirstOrDefault(label => buttons.All(button =>
                    !label.transform.IsChildOf(button.transform)));
            SetLabelText(title, text);
        }

        private static void SetButtonText(UIButton button, string text)
        {
            foreach (UILabel label in button.GetComponentsInChildren<UILabel>(true))
            {
                SetLabelText(label, text);
            }
        }

        private static void SetLabelText(UILabel label, string text)
        {
            if (label == null)
            {
                return;
            }

            label.overflowMethod = UILabel.Overflow.ShrinkContent;
            label.maxLineCount = 1;
            label.text = text;
        }

        private static Transform FindClonedTransform(
            Transform sourceRoot,
            Transform sourceChild,
            Transform clonedRoot)
        {
            List<int> siblingIndices = new List<int>();
            Transform current = sourceChild;
            while (current != sourceRoot)
            {
                if (current == null)
                {
                    throw new InvalidOperationException(
                        "The source button is not inside the source filter row.");
                }
                siblingIndices.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            siblingIndices.Reverse();
            current = clonedRoot;
            foreach (int siblingIndex in siblingIndices)
            {
                current = current.GetChild(siblingIndex);
            }
            return current;
        }

        private sealed class FilterModeMarker
        {
            internal DeckCardAvailabilityMode Mode;
        }
    }
}
