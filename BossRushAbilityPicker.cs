using System;
using System.Collections.Generic;
using System.Linq;
using Cute;
using HarmonyLib;
using UnityEngine;
using Wizard;
using Wizard.Dialog.Setting;

namespace Shadowbus
{
    /// <summary>
    /// Optional testing aid: adds a button to the BossRush ability select screen
    /// that offers every configured buff instead of the three random candidates.
    /// The button only exists while the feature is switched on in the plugin
    /// config, so a normal run keeps the original random selection.
    /// </summary>
    public static class BossRushAbilityPicker
    {
        private const string ButtonName = "BossRushAbilityPickerButton";

        // The screen adds QuestBossRushAbilitySelect.CARD_DEPTH_ADD_VALUE to the
        // depth of the card it focuses, so leave room above the panel depths.
        private const int CardDepthHeadroom = 10;

        private const int ButtonWidth = 140;
        private const int ButtonHeight = 40;
        private const int MaxDescriptionLength = 56;

        private static bool _enabled;

        public static void Configure(bool enabled)
        {
            _enabled = enabled;
            Plugin.Logger.LogInfo(
                $"[BossRush] Ability picker button is {(enabled ? "enabled" : "disabled")}.");
        }

        [HarmonyPatch(typeof(QuestBossRushAbilitySelect), nameof(QuestBossRushAbilitySelect.Init))]
        [HarmonyPostfix]
        private static void AbilitySelect_Init_Postfix(QuestBossRushAbilitySelect __instance)
        {
            if (!_enabled || !BossRushOfflineData.IsActive)
            {
                return;
            }

            try
            {
                CreatePickerButton(__instance);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not add the ability picker button: {exception.Message}");
            }
        }

        private static void CreatePickerButton(QuestBossRushAbilitySelect select)
        {
            UIButton deckButton = AccessTools.Field(typeof(QuestBossRushAbilitySelect), "_deckButton")?
                .GetValue(select) as UIButton;
            UIButton skillDetailButton = AccessTools.Field(typeof(QuestBossRushAbilitySelect), "_skillDetailButton")?
                .GetValue(select) as UIButton;
            Transform anchor = skillDetailButton != null ? skillDetailButton.transform : deckButton?.transform;
            if (anchor == null)
            {
                Plugin.Logger.LogWarning("[BossRush] Ability select screen has no anchor button; picker skipped.");
                return;
            }

            GameObject parent = anchor.parent == null ? select.gameObject : anchor.parent.gameObject;
            if (parent.transform.Find(ButtonName) != null)
            {
                return;
            }

            SettingBase settingTemplate = UIManager.GetInstance().OptionSettingPrefab;
            if (settingTemplate == null || settingTemplate.m_itemButton == null)
            {
                Plugin.Logger.LogWarning("[BossRush] OptionSettingPrefab unavailable; ability picker skipped.");
                return;
            }

            GameObject buttonObject = NGUITools.AddChild(parent, settingTemplate.m_itemButton);
            buttonObject.name = ButtonName;
            buttonObject.layer = anchor.gameObject.layer;
            buttonObject.transform.localScale = anchor.localScale;
            buttonObject.transform.localPosition = anchor.localPosition + GetBelowAnchorOffset(anchor);
            buttonObject.SetActive(true);

            ItemButton item = buttonObject.GetComponent<ItemButton>();
            item.SetActive_SeparatorLine(false);
            item.SetActive_SpriteOnButton(false);
            item._subLabel.gameObject.SetActive(false);
            item._sprite.ResetAnchors();
            item._sprite.pivot = UIWidget.Pivot.Center;
            item._sprite.transform.localPosition = Vector3.zero;
            item._sprite.SetDimensions(ButtonWidth, ButtonHeight);
            item._label.ResetAnchors();
            item._label.pivot = UIWidget.Pivot.Center;
            item._label.alignment = NGUIText.Alignment.Center;
            item._label.overflowMethod = UILabel.Overflow.ShrinkContent;
            item._label.SetDimensions(ButtonWidth - 12, ButtonHeight - 4);
            item._label.transform.localPosition = Vector3.zero;
            item.SetValue("随便选");
            item._collider.size = new Vector3(ButtonWidth, ButtonHeight, item._collider.size.z);

            UIButton button = item._button;
            button.isEnabled = true;
            button.onClick.Clear();
            button.onClick.Add(new EventDelegate(delegate
            {
                GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_BTN_DECIDE, false);
                OpenPicker(select, button);
            }));

            UIManager.SetObjectToGrey(buttonObject, false, null, null);
            Plugin.Logger.LogInfo(
                $"[BossRush] Ability picker button placed at {buttonObject.transform.localPosition} " +
                $"under '{parent.name}' (deck={FormatPosition(deckButton)}, skill={FormatPosition(skillDetailButton)}).");
        }

        /// <summary>
        /// Places the button directly under its anchor. The stock buttons sit in
        /// a row that already reaches the right edge of the screen, so extending
        /// that row would push the button out of view; the gap is derived from
        /// the anchor's own height instead of a fixed guess.
        /// </summary>
        private static Vector3 GetBelowAnchorOffset(Transform anchor)
        {
            UIWidget widget = anchor.GetComponent<UIWidget>() ?? anchor.GetComponentInChildren<UIWidget>(true);
            float anchorHeight = widget != null && widget.height > 0 ? widget.height : ButtonHeight;
            float scale = Mathf.Abs(anchor.localScale.y);
            if (scale <= 0.01f)
            {
                scale = 1f;
            }

            float step = ((anchorHeight + ButtonHeight) * 0.5f + 10f) * scale;
            Plugin.Logger.LogInfo(
                $"[BossRush] Ability picker anchor height {anchorHeight}, scale {scale}, step {step}.");
            return new Vector3(0f, -step, 0f);
        }

        private static string FormatPosition(UIButton button)
        {
            return button == null ? "none" : button.transform.localPosition.ToString();
        }

        private static void OpenPicker(QuestBossRushAbilitySelect select, UIButton button)
        {
            List<BossRushAbility> abilities = BossRushOfflineData.GetAvailableAbilities();
            if (abilities.Count == 0)
            {
                ShowMessage("随便选", "当前配置没有可用的 Buff。检查 abilities 中的 ability_id 是否存在于 CardMaster。");
                return;
            }

            BossRushState state = BossRushOfflineData.GetState();
            List<string> labels = abilities.Select(ability => DescribeAbility(ability, state)).ToList();
            DialogBase dialog = DrumrollDialog.Create(labels, 0, null, null, index =>
            {
                if (index < 0 || index >= abilities.Count)
                {
                    return;
                }
                ApplyAbility(select, button, abilities[index]);
            }, "随便选 Buff");
            RaiseDialogAbove(dialog, select.gameObject);
        }

        /// <summary>
        /// The ability select screen raises the depth of its own cards, so the
        /// choice list is pushed above whatever the screen currently uses. Depth
        /// is only ever raised, never lowered.
        /// </summary>
        private static void RaiseDialogAbove(DialogBase dialog, GameObject reference)
        {
            if (dialog == null || reference == null)
            {
                return;
            }

            try
            {
                UIPanel[] referencePanels = reference.GetComponentsInChildren<UIPanel>(true);
                UIPanel[] dialogPanels = dialog.GetComponentsInChildren<UIPanel>(true);
                if (referencePanels.Length == 0 || dialogPanels.Length == 0)
                {
                    return;
                }

                int referenceMaxDepth = referencePanels.Max(panel => panel.depth) + CardDepthHeadroom;
                int referenceMaxSortingOrder = referencePanels.Max(panel => panel.sortingOrder);
                int depthOffset = referenceMaxDepth + 2 - dialogPanels.Min(panel => panel.depth);
                if (depthOffset <= 0)
                {
                    return;
                }

                foreach (UIPanel panel in dialogPanels)
                {
                    panel.depth += depthOffset;
                    panel.sortingOrder = Math.Max(panel.sortingOrder, referenceMaxSortingOrder + 1);
                }

                UIPanel backPanel = dialog.backView?.GetComponent<UIPanel>();
                if (backPanel != null)
                {
                    backPanel.depth = referenceMaxDepth + 1;
                    backPanel.sortingOrder = Math.Max(backPanel.sortingOrder, referenceMaxSortingOrder + 1);
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not raise the ability picker dialog: {exception.Message}");
            }
        }

        private static string DescribeAbility(BossRushAbility ability, BossRushState state)
        {
            var parts = new List<string> { $"{GetCardName(ability.AbilityId)} [{ability.AbilityId}]" };
            parts.Add(DescribeEffect(ability));

            int owned = state?.SelectedAbilities?.Count(item => item.AbilityId == ability.AbilityId) ?? 0;
            if (owned > 0)
            {
                parts.Add($"已取得 x{owned}");
            }
            return string.Join("  ", parts.ToArray());
        }

        /// <summary>
        /// Uses the same description the select screen shows on the card. Only
        /// when the package leaves it blank does this fall back to describing the
        /// structured fields, so the row never reads as an empty effect.
        /// </summary>
        private static string DescribeEffect(BossRushAbility ability)
        {
            if (!string.IsNullOrWhiteSpace(ability.SpecialAbilityDesc))
            {
                return Shorten(ability.SpecialAbilityDesc, MaxDescriptionLength);
            }

            var effects = new List<string>();
            if (ability.MaxLifeChange != 0)
            {
                effects.Add($"最大生命{ability.MaxLifeChange:+#;-#;0}");
            }
            if (ability.LifeChange != 0)
            {
                effects.Add($"生命{ability.LifeChange:+#;-#;0}");
            }
            if (!string.IsNullOrWhiteSpace(ability.Skill))
            {
                effects.Add("带技能（无说明）");
            }
            return effects.Count == 0 ? "无效果" : string.Join(" ", effects.ToArray());
        }

        /// <summary>
        /// Flattens a multi-line description into one drumroll row. Long rows
        /// would otherwise shrink until they are unreadable.
        /// </summary>
        private static string Shorten(string text, int maxLength)
        {
            string flat = string.Join(
                " ",
                text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToArray());
            return flat.Length <= maxLength ? flat : flat.Substring(0, maxLength - 1).TrimEnd() + "…";
        }

        private static string GetCardName(int cardId)
        {
            try
            {
                CardParameter parameter = CardMaster.GetInstance(CardMaster.CardMasterId.Default)
                    .GetCardParameterFromId(cardId);
                if (!string.IsNullOrEmpty(parameter?.CardName))
                {
                    return parameter.CardName;
                }
            }
            catch
            {
            }
            return "Buff";
        }

        /// <summary>
        /// Hands the chosen ability to the screen's own confirm path, so the
        /// selection task, the local response and the lobby refresh all run
        /// exactly as they do for a normal pick.
        /// </summary>
        private static void ApplyAbility(QuestBossRushAbilitySelect select, UIButton button, BossRushAbility ability)
        {
            try
            {
                if (button != null)
                {
                    // The screen closes itself after the task completes; blocking a
                    // second click avoids sending two selections for one battle.
                    button.isEnabled = false;
                    UIManager.SetObjectToGrey(button.gameObject, true, null, null);
                }

                Plugin.Logger.LogInfo($"[BossRush] Ability picker selected {ability.AbilityId} ('{GetCardName(ability.AbilityId)}').");
                AccessTools.Method(typeof(QuestBossRushAbilitySelect), "AbilitySelectProcess")
                    ?.Invoke(select, new object[] { ability.AbilityId, ability.IsFoil });
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError($"[BossRush] Ability picker failed to apply {ability.AbilityId}.\n{exception}");
                ShowMessage("随便选", "应用 Buff 失败，请查看 BepInEx 日志。");
                if (button != null)
                {
                    button.isEnabled = true;
                    UIManager.SetObjectToGrey(button.gameObject, false, null, null);
                }
            }
        }

        private static void ShowMessage(string title, string message)
        {
            DialogBase dialog = UIManager.GetInstance().CreateDialogClose(false, false);
            dialog.SetSize(DialogBase.Size.M);
            dialog.SetTitleLabel(title);
            dialog.SetButtonLayout(DialogBase.ButtonLayout.OkBtn);
            dialog.SetText(message, true);
        }
    }
}
