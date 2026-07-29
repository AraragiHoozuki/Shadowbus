using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Wizard;

namespace Shadowbus
{
    internal sealed class CustomFormatDeckTabs : MonoBehaviour
    {
        private const string PrefabPath = "UI/layoutParts/Guild/FormatChangeUI";
        private static readonly Vector3 TitlePosition = new Vector3(-350f, 65f, 0f);

        private DeckSelectUIDialog dialog;
        private CustomFormatContextKind context;
        private readonly List<CustomFormatDefinition> definitions =
            new List<CustomFormatDefinition>();
        private readonly List<UIButton> buttons = new List<UIButton>();
        private readonly List<UILabel> labels = new List<UILabel>();
        private string selectedId;
        private bool allowCreateNewForUnlimited;

        internal static void Attach(
            DeckSelectUIDialog dialog,
            CustomFormatContextKind context,
            string initialFormatId,
            bool allowCreateNewForUnlimited)
        {
            if (dialog?.Dialog == null || dialog._deckSelectUI == null)
            {
                return;
            }

            CustomFormatDeckTabs controller =
                dialog.Dialog.gameObject.AddComponent<CustomFormatDeckTabs>();
            controller.dialog = dialog;
            controller.context = context;
            controller.allowCreateNewForUnlimited = allowCreateNewForUnlimited;
            controller.definitions.AddRange(CustomFormats.All.Where(item => item.Supports(context)));
            controller.selectedId = CustomFormats.Get(initialFormatId).Id;
            controller.CreateTabs();
            controller.UpdateTitle();
        }

        private void CreateTabs()
        {
            if (definitions.Count < 2)
            {
                return;
            }

            GameObject root = UnityEngine.Object.Instantiate(Resources.Load(PrefabPath)) as GameObject;
            if (root == null)
            {
                Plugin.Logger.LogError("[CustomFormats] Deck tab prefab could not be loaded.");
                return;
            }

            root.name = "ShadowbusCustomFormatDeckTabs";
            FormatChangeUI template = root.GetComponent<FormatChangeUI>();
            if (template == null)
            {
                UnityEngine.Object.Destroy(root);
                Plugin.Logger.LogError("[CustomFormats] Deck tab prefab has no FormatChangeUI component.");
                return;
            }

            var availableButtons = new List<UIButton>
            {
                template._btnRotation,
                template._btnUnlimited,
                template._btnAnother
            };
            while (availableButtons.Count < definitions.Count)
            {
                UIButton source = availableButtons[availableButtons.Count - 1];
                GameObject cloneObject = UnityEngine.Object.Instantiate(source.gameObject);
                cloneObject.name = "ShadowbusCustomFormatTab" + availableButtons.Count;
                cloneObject.transform.SetParent(source.transform.parent, false);
                Vector3 position = source.transform.localPosition;
                cloneObject.transform.localPosition = new Vector3(
                    position.x + 230f,
                    position.y,
                    position.z);
                cloneObject.transform.localRotation = source.transform.localRotation;
                cloneObject.transform.localScale = source.transform.localScale;
                availableButtons.Add(cloneObject.GetComponent<UIButton>());
            }
            for (int index = 0; index < availableButtons.Count; index++)
            {
                UIButton button = availableButtons[index];
                bool visible = index < definitions.Count;
                button.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                CustomFormatDefinition definition = definitions[index];
                button.onClick.Clear();
                button.onClick.Add(new EventDelegate(() => SelectFormat(definition.Id)));
                buttons.Add(button);
                labels.Add(CreateButtonLabel(button, definition.DisplayName));
            }

            root.SetActive(true);
            dialog.Dialog.AttachObjToTitleLabel(root, TitlePosition);
            dialog._deckSelectDialogTitleUI._isExistFormatChangeUI = true;
            dialog._deckSelectDialogTitleUI.UpdateTablePosition();
            RefreshButtonState();
        }

        private UILabel CreateButtonLabel(UIButton button, string text)
        {
            foreach (UILabel existing in button.GetComponentsInChildren<UILabel>(true))
            {
                existing.gameObject.SetActive(false);
            }
            UILabel source = dialog._deckSelectDialogTitleUI._formatLabel;
            GameObject labelObject = UnityEngine.Object.Instantiate(source.gameObject);
            labelObject.name = "ShadowbusCustomFormatLabel";
            labelObject.transform.SetParent(button.transform, false);
            labelObject.transform.localPosition = Vector3.zero;
            labelObject.transform.localRotation = Quaternion.identity;
            labelObject.transform.localScale = Vector3.one;
            labelObject.layer = button.gameObject.layer;

            foreach (UILocalize localize in labelObject.GetComponentsInChildren<UILocalize>(true))
            {
                localize.enabled = false;
                UnityEngine.Object.Destroy(localize);
            }

            UILabel label = labelObject.GetComponent<UILabel>();
            UISprite buttonSprite = button.GetComponentsInChildren<UISprite>(true)
                .OrderByDescending(sprite => (long)sprite.width * sprite.height)
                .FirstOrDefault();
            label.pivot = UIWidget.Pivot.Center;
            label.width = Math.Max(72, (buttonSprite?.width ?? 120) - 16);
            label.height = 42;
            label.fontSize = 18;
            label.depth = NGUITools.CalculateNextDepth(button.gameObject) + 1;
            if (buttonSprite != null)
            {
                label.transform.localPosition = button.transform.InverseTransformPoint(
                    buttonSprite.transform.position);
            }
            label.overflowMethod = UILabel.Overflow.ShrinkContent;
            label.maxLineCount = 1;
            label.text = text;
            return label;
        }

        private void SelectFormat(string formatId)
        {
            CustomFormatDefinition definition = CustomFormats.Get(formatId);
            if (string.Equals(selectedId, definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            selectedId = definition.Id;
            CustomFormatContext.SelectionFormatId = selectedId;
            JsonDeckSelection selection = CreateSelection(definition);

            dialog._deckSelectUI.gameObject.SetActive(false);
            dialog._deckSelectUI.UpdateDeckView(
                new List<DeckGroup> { selection.Group },
                definition.BaseGameFormat,
                allowCreateNewForUnlimited && definition.Id == CustomFormats.UnlimitedId,
                () =>
                {
                    if (dialog?._deckSelectUI == null)
                    {
                        return;
                    }
                    dialog._deckSelectUI.gameObject.SetActive(true);
                    UpdateTitle();
                    RefreshButtonState();
                });

            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_TOGGLE_ON, false);
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Switched {context} deck selection to {definition.Id} " +
                $"({selection.NonEmptyDeckCount} deck(s)).");
        }

        private static JsonDeckSelection CreateSelection(CustomFormatDefinition definition)
        {
            LitJson.JsonData decks = CustomDeckStore.LoadDeckList(definition);
            DeckGroup group = DeckListUtility.CreateDeckGroup(
                decks,
                definition.BaseGameFormat,
                DeckAttributeType.CustomDeck);
            return new JsonDeckSelection(
                group,
                group.DeckDataList.Count(deck => !deck.IsNoCard()));
        }

        private void UpdateTitle()
        {
            if (dialog?._deckSelectDialogTitleUI == null)
            {
                return;
            }

            CustomFormatDefinition definition = CustomFormats.Get(selectedId);
            DeckSelectUIDialogTitle title = dialog._deckSelectDialogTitleUI;
            title.UpdateFormatObj(definition.BaseGameFormat);
            foreach (UILocalize localize in
                title._formatLabel.gameObject.GetComponentsInChildren<UILocalize>(true))
            {
                localize.enabled = false;
            }
            title._formatLabel.text = definition.DisplayName;
            title.UpdateTablePosition();
        }

        private void RefreshButtonState()
        {
            for (int index = 0; index < buttons.Count; index++)
            {
                bool selected = string.Equals(
                    definitions[index].Id,
                    selectedId,
                    StringComparison.OrdinalIgnoreCase);
                labels[index].color = selected
                    ? new Color32(255, 225, 135, 255)
                    : Color.white;
                UIManager.SetObjectToGrey(buttons[index].gameObject, false, null, null);
            }
        }

        private void LateUpdate()
        {
            if (dialog?._deckSelectDialogTitleUI?._formatLabel == null)
            {
                return;
            }

            string expected = CustomFormats.Get(selectedId).DisplayName;
            if (!string.Equals(
                dialog._deckSelectDialogTitleUI._formatLabel.text,
                expected,
                StringComparison.Ordinal))
            {
                dialog._deckSelectDialogTitleUI._formatLabel.text = expected;
            }
        }

        private sealed class JsonDeckSelection
        {
            internal JsonDeckSelection(DeckGroup group, int nonEmptyDeckCount)
            {
                Group = group;
                NonEmptyDeckCount = nonEmptyDeckCount;
            }

            internal DeckGroup Group { get; }
            internal int NonEmptyDeckCount { get; }
        }
    }
}
