using Cute;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Wizard;

namespace Shadowbus
{
    internal static class CustomFormatDetailsDialog
    {
        internal static void Show(CustomFormatDefinition definition)
        {
            definition = (definition ?? CustomFormats.Unlimited).Clone();

            DialogBase dialog = UIManager.GetInstance().CreateDialogClose(false, false);
            dialog.SetSize(DialogBase.Size.XL);
            dialog.SetTitleLabel(definition.DisplayName + " - \u8d5b\u5236\u8be6\u60c5");
            dialog.SetButtonLayout(DialogBase.ButtonLayout.CloseBtn);
            dialog.SetPanelDepth(2000, false);

            CustomFormatDetailsView view =
                dialog.gameObject.AddComponent<CustomFormatDetailsView>();
            view.Initialize(dialog, definition);
        }
    }

    internal sealed class CustomFormatDetailsView : MonoBehaviour
    {
        private const int ContentWidth = 980;
        private const int CardsPerRow = 8;
        private const int CardRowHeight = 255;
        private const float CardScale = 0.5f;
        private const float CardSpacing = 116f;

        private readonly List<string> loadedAssetPaths = new List<string>();
        private readonly List<UIBase_CardManager.CardObjData> loadedCards =
            new List<UIBase_CardManager.CardObjData>();

        private DialogBase dialog;
        private CustomFormatDefinition definition;
        private UITable table;
        private CardDetailUI cardDetailDialog;
        private GameObject loadingRow;

        internal void Initialize(
            DialogBase ownerDialog,
            CustomFormatDefinition formatDefinition)
        {
            dialog = ownerDialog;
            definition = formatDefinition;

            dialog.ScrollView.transform.DestroyChildren();
            dialog.ScrollView.panel.leftAnchor.absolute = 30;
            dialog.ScrollView.panel.rightAnchor.absolute = -30;
            dialog.ScrollView.contentPivot = UIWidget.Pivot.Top;

            GameObject tableObject = new GameObject("ShadowbusFormatDetailsTable");
            tableObject.layer = dialog.gameObject.layer;
            dialog.AttachToScrollView(tableObject.transform);
            table = tableObject.AddComponent<UITable>();
            table.columns = 1;
            table.padding = new Vector2(0f, 8f);
            table.keepWithinPanel = true;
            table.pivot = UIWidget.Pivot.Top;
            table.cellAlignment = UIWidget.Pivot.Top;

            CreateTextRow("\u57fa\u672c\u9650\u5236", 42, 28, true);
            CreateTextRow(BuildGeneralRulesText(definition), 142, 24, false);
            CreateTextRow("\u7279\u6b8a\u5361\u724c\u9650\u5236", 42, 28, true);

            if (definition.CardLimits == null || definition.CardLimits.Count == 0)
            {
                CreateTextRow("\u65e0", 42, 24, false);
                RefreshLayout(true);
                return;
            }

            loadingRow = CreateTextRow("\u6b63\u5728\u52a0\u8f7d\u5361\u724c...", 42, 22, false);
            cardDetailDialog = DialogCreator.CreateCardDetailDialog(
                dialog.gameObject,
                "Detail");
            cardDetailDialog.gameObject.SetActive(false);
            RefreshLayout(true);
            LoadSpecialLimitCards();
        }

        private void LoadSpecialLimitCards()
        {
            List<int> cardIds = definition.CardLimits.Keys
                .OrderBy(cardId => cardId)
                .ToList();
            CardMaster cardMaster = CardMaster.GetInstance(CardMaster.CardMasterId.Default);
            List<int> loadableIds = cardIds
                .Where(cardId => cardMaster.GetCardParameterFromId(cardId) != null)
                .ToList();

            if (loadableIds.Count == 0)
            {
                FinishCardLoading(new List<UIBase_CardManager.CardObjData>());
                return;
            }

            UIManager.GetInstance().CardLoadSelect(
                null,
                loadableIds,
                dialog.gameObject.layer,
                true,
                () =>
                {
                    List<UIBase_CardManager.CardObjData> managerCards =
                        UIManager.GetInstance().getCardList2DObjs();
                    List<UIBase_CardManager.CardObjData> cards =
                        new List<UIBase_CardManager.CardObjData>(managerCards);
                    managerCards.Clear();

                    List<string> managerAssetPaths =
                        Toolbox.ResourcesManager.CardListAssetPathList;
                    List<string> assetPaths = new List<string>(managerAssetPaths);
                    managerAssetPaths.Clear();

                    if (this == null || dialog == null)
                    {
                        foreach (UIBase_CardManager.CardObjData card in cards)
                        {
                            if (card?.CardObj != null)
                            {
                                Destroy(card.CardObj);
                            }
                        }
                        Toolbox.ResourcesManager.RemoveAssetGroup(assetPaths);
                        return;
                    }

                    loadedAssetPaths.AddRange(assetPaths);
                    loadedCards.AddRange(cards);
                    FinishCardLoading(cards);
                },
                false,
                CardMaster.CardMasterId.Default);
        }

        private void FinishCardLoading(
            List<UIBase_CardManager.CardObjData> cards)
        {
            if (loadingRow != null)
            {
                Destroy(loadingRow);
                loadingRow = null;
            }

            Dictionary<int, UIBase_CardManager.CardObjData> cardsById = cards
                .Where(card => card != null && card.CardObj != null)
                .GroupBy(card => card.ids)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (IGrouping<int, KeyValuePair<int, int>> limitGroup in
                definition.CardLimits
                    .OrderBy(item => item.Value)
                    .ThenBy(item => item.Key)
                    .GroupBy(item => item.Value))
            {
                string header = limitGroup.Key == 0
                    ? "\u6700\u591a 0 \u5f20\uff08\u7981\u6b62\u4f7f\u7528\uff09"
                    : $"\u6700\u591a {limitGroup.Key} \u5f20";
                CreateTextRow(header, 42, 25, true);

                List<int> groupCardIds = limitGroup
                    .Select(item => item.Key)
                    .OrderBy(cardId => cardId)
                    .ToList();
                List<int> missingIds = groupCardIds
                    .Where(cardId => !cardsById.ContainsKey(cardId))
                    .ToList();

                for (int offset = 0; offset < groupCardIds.Count; offset += CardsPerRow)
                {
                    List<UIBase_CardManager.CardObjData> rowCards = groupCardIds
                        .Skip(offset)
                        .Take(CardsPerRow)
                        .Where(cardsById.ContainsKey)
                        .Select(cardId => cardsById[cardId])
                        .ToList();
                    if (rowCards.Count > 0)
                    {
                        CreateCardRow(rowCards);
                    }
                }

                if (missingIds.Count > 0)
                {
                    CreateTextRow(
                        "\u65e0\u6cd5\u8bfb\u53d6\u5361\u724c\u56fe\u7247\uff1aID " +
                        string.Join(", ", missingIds),
                        42,
                        20,
                        false);
                    Plugin.Logger.LogWarning(
                        "[CustomFormats] Could not load special-limit card images for IDs " +
                        string.Join(", ", missingIds) + ".");
                }
            }

            RefreshLayout(false);
        }

        private GameObject CreateTextRow(
            string text,
            int height,
            int fontSize,
            bool isHeader)
        {
            GameObject row = Instantiate(dialog.titleLabel.gameObject);
            row.name = isHeader
                ? "ShadowbusFormatDetailsHeader"
                : "ShadowbusFormatDetailsText";
            row.transform.SetParent(table.transform, false);
            row.transform.localPosition = Vector3.zero;
            row.transform.localRotation = Quaternion.identity;
            row.transform.localScale = Vector3.one;
            row.layer = table.gameObject.layer;

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

            UILabel label = row.GetComponent<UILabel>();
            label.leftAnchor.target = null;
            label.rightAnchor.target = null;
            label.bottomAnchor.target = null;
            label.topAnchor.target = null;
            label.ResetAnchors();
            label.pivot = UIWidget.Pivot.Center;
            label.alignment = NGUIText.Alignment.Left;
            label.width = ContentWidth;
            label.height = height;
            label.fontSize = fontSize;
            label.spacingY = 4;
            label.maxLineCount = 0;
            label.overflowMethod = UILabel.Overflow.ShrinkContent;
            label.text = text;
            if (isHeader)
            {
                label.effectStyle = UILabel.Effect.Outline;
                label.effectColor = new Color(0f, 0f, 0f, 0.8f);
            }

            row.AddMissingComponent<UIDragScrollView>().scrollView = dialog.ScrollView;
            row.SetActive(true);
            return row;
        }

        private void CreateCardRow(List<UIBase_CardManager.CardObjData> cards)
        {
            GameObject row = new GameObject("ShadowbusFormatDetailsCardRow");
            row.layer = table.gameObject.layer;
            row.transform.SetParent(table.transform, false);
            row.transform.localPosition = Vector3.zero;
            row.transform.localRotation = Quaternion.identity;
            row.transform.localScale = Vector3.one;

            UIWidget rowWidget = row.AddComponent<UIWidget>();
            rowWidget.width = ContentWidth;
            rowWidget.height = CardRowHeight;
            rowWidget.pivot = UIWidget.Pivot.Center;

            float firstX = -(cards.Count - 1) * CardSpacing * 0.5f;
            for (int index = 0; index < cards.Count; index++)
            {
                UIBase_CardManager.CardObjData card = cards[index];
                CardListTemplate template = card.CardObj.GetComponent<CardListTemplate>();
                if (template == null)
                {
                    continue;
                }

                template.SetParentAndResetPos(row.transform);
                UIManager.GetInstance().SetLayerRecursive(
                    template.transform,
                    dialog.ScrollView.gameObject.layer);
                NGUITools.MarkParentAsChanged(card.CardObj);
                foreach (UIWidget widget in
                    template.GetComponentsInChildren<UIWidget>(true))
                {
                    widget.RemoveFromPanel();
                    widget.CreatePanel();
                    widget.MarkAsChanged();
                }
                template.transform.localPosition = new Vector3(
                    firstX + index * CardSpacing,
                    0f,
                    0f);
                template.SetScale(CardScale);
                template.HideNum();
                template.HideNewLabel();

                UIEventListener listener = template.AddColliderToFrame(1f);
                GameObject cardObject = card.CardObj;
                listener.onClick = _ =>
                {
                    if (cardDetailDialog != null)
                    {
                        cardDetailDialog.OnPushCardDetailOn(cardObject);
                    }
                };
                listener.gameObject.AddMissingComponent<UIDragScrollView>().scrollView =
                    dialog.ScrollView;
            }
        }

        private void RefreshLayout(bool resetPosition)
        {
            table.repositionNow = true;
            table.Reposition();
            dialog.ScrollView.panel.RebuildAllDrawCalls();
            dialog.SetScrollViewActive(true);
            StartCoroutine(RefreshScrollViewNextFrame(resetPosition));
        }

        private IEnumerator RefreshScrollViewNextFrame(bool resetPosition)
        {
            yield return null;
            if (dialog == null || dialog.ScrollView == null)
            {
                yield break;
            }
            table.Reposition();
            if (resetPosition)
            {
                dialog.ScrollView.ResetPosition();
            }
            dialog.ScrollView.UpdateScrollbars(true);
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            if (loadedAssetPaths.Count > 0)
            {
                Toolbox.ResourcesManager.RemoveAssetGroup(loadedAssetPaths);
                loadedAssetPaths.Clear();
            }
            loadedCards.Clear();
        }

        private static string BuildGeneralRulesText(CustomFormatDefinition format)
        {
            return string.Join("\n", new[]
            {
                "\u724c\u7ec4\u5f20\u6570\uff1a" + DescribeLimit(format.DeckSizeLimit),
                "\u666e\u901a\u540c\u540d\u5361\uff1a" + DescribeLimit(format.SameCardLimit),
                "Token \u5361\u603b\u6570\uff1a" + DescribeLimit(format.TokenCardTotalLimit),
                "\u540c\u540d Token \u5361\uff1a" + DescribeLimit(format.TokenSameCardLimit)
            });
        }

        private static string DescribeLimit(int? limit)
        {
            return limit.HasValue
                ? $"\u6700\u591a {limit.Value} \u5f20"
                : "\u65e0\u9650\u5236";
        }
    }
}
