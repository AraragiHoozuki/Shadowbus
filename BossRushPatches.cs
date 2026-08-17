using HarmonyLib;
using Cute;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Wizard;
using Wizard.Dialog.Setting;

namespace Shadowbus
{
    public static class BossRushPatches
    {
        [HarmonyPatch(typeof(MyPageItemSoroPlay), nameof(MyPageItemSoroPlay.Show))]
        [HarmonyPrefix]
        private static void MyPageItemSoroPlay_Show_Prefix()
        {
            // The current offline My Page hides the Quest card when the server
            // reports no active quest. BossRush is local, so expose that card
            // before the original layout code decides which cards to display.
            if (!BossRushOfflineData.IsActive || Data.MyPageNotifications?.data == null)
            {
                return;
            }

            try
            {
                QuestOpenInfo questOpenInfo = Data.MyPageNotifications.data.QuestOpenInfo;
                if (questOpenInfo == null)
                {
                    return;
                }

                SetPrivateProperty(questOpenInfo, nameof(QuestOpenInfo.IsOpen), true);
                SetPrivateProperty(
                    questOpenInfo,
                    nameof(QuestOpenInfo.QuestPanelBandText),
                    BossRushOfflineData.CurrentPackage?.DisplayName ?? "BossRush");
                SetPrivateProperty(
                    questOpenInfo,
                    nameof(QuestOpenInfo.EndTime),
                    DateTime.UtcNow.AddYears(10));
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Failed to expose the local Quest card: {exception.Message}");
            }
        }

        [HarmonyPatch(typeof(MyPageItemSoroPlay), nameof(MyPageItemSoroPlay.Show))]
        [HarmonyPostfix]
        private static void MyPageItemSoroPlay_Show_Postfix(MyPageItemSoroPlay __instance)
        {
            if (!BossRushOfflineData.IsActive)
            {
                return;
            }

            try
            {
                MyPageCardPanel questCard = AccessTools.Field(typeof(MyPageItemSoroPlay), "_questCardPanel")?
                    .GetValue(__instance) as MyPageCardPanel;
                if (questCard == null)
                {
                    return;
                }

                ApplyQuestCardTexture(questCard);
                if (questCard.gameObject.activeSelf)
                {
                    return;
                }

                // Covers cached My Page instances where Show ran before the
                // notification object was populated or before the prefix could
                // expose the local Quest state.
                ExposeLocalQuestCard();
                AccessTools.Method(typeof(MyPageItemSoroPlay), "SetCardPanelAnimation")?.Invoke(__instance, null);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Failed to restore the Quest card layout: {exception.Message}");
            }
        }

        private static void ApplyQuestCardTexture(MyPageCardPanel questCard)
        {
            if (questCard.Texture == null)
            {
                return;
            }

            int charaId = GetCurrentEnemyCharaId();
            ResourcesManager resources = Toolbox.ResourcesManager;
            string[] paths =
            {
                resources.GetAssetTypePath(charaId.ToString(), ResourcesManager.AssetLoadPathType.ClassCharaBase, true),
                resources.GetAssetTypePath(charaId.ToString(), ResourcesManager.AssetLoadPathType.ClassCharaWideThumbnail, true),
                resources.GetAssetTypePath("boss_rush", ResourcesManager.AssetLoadPathType.ClassCharaBase, true)
            };

            foreach (string path in paths)
            {
                try
                {
                    Texture texture = resources.LoadObject<Texture>(path, true, false);
                    if (texture != null)
                    {
                        questCard.Texture.mainTexture = texture;
                        Plugin.Logger.LogInfo($"[BossRush] Applied local Quest card texture from '{path}'.");
                        return;
                    }
                }
                catch
                {
                }
            }

            // The retired BossRush card bundle is not shipped by some game
            // versions. Reuse a loaded single-player card as a guaranteed
            // visual fallback instead of leaving a white/empty panel.
            MyPageCardPanel storyCard = AccessTools.Field(typeof(MyPageItemSoroPlay), "_storyCardPanel")?
                .GetValue(questCard.GetComponentInParent<MyPageItemSoroPlay>()) as MyPageCardPanel;
            if (storyCard?.Texture?.mainTexture != null)
            {
                questCard.Texture.mainTexture = storyCard.Texture.mainTexture;
                Plugin.Logger.LogWarning("[BossRush] BossRush card texture unavailable; reused the story card texture.");
                return;
            }

            Plugin.Logger.LogWarning("[BossRush] No available character texture could be used for the Quest card.");
        }

        private static void ExposeLocalQuestCard()
        {
            if (Data.MyPageNotifications?.data == null)
            {
                return;
            }

            QuestOpenInfo questOpenInfo = Data.MyPageNotifications.data.QuestOpenInfo;
            if (questOpenInfo == null)
            {
                return;
            }

            SetPrivateProperty(questOpenInfo, nameof(QuestOpenInfo.IsOpen), true);
            SetPrivateProperty(
                questOpenInfo,
                nameof(QuestOpenInfo.QuestPanelBandText),
                BossRushOfflineData.CurrentPackage?.DisplayName ?? "BossRush");
            SetPrivateProperty(
                questOpenInfo,
                nameof(QuestOpenInfo.EndTime),
                DateTime.UtcNow.AddYears(10));
        }

        private static void SetPrivateProperty(object target, string propertyName, object value)
        {
            PropertyInfo property = AccessTools.Property(target.GetType(), propertyName);
            MethodInfo setter = property?.GetSetMethod(true);
            setter?.Invoke(target, new[] { value });
        }

        private static int GetCurrentEnemyCharaId()
        {
            BossRushBoss boss = BossRushOfflineData.GetCurrentBoss();
            int charaId = BossRushOfflineData.ResolveCharaId(boss);
            return charaId > 0 ? charaId : 1;
        }

        [HarmonyPatch(typeof(QuestSelectionPage), "CollectBossRushResourcePaths")]
        [HarmonyPrefix]
        private static bool QuestSelectionPage_CollectBossRushResourcePaths_Prefix(ref List<string> __result)
        {
            // The retired BossRush bundle is not present in every offline install.
            // The selection page can use its normal quest background and does not
            // need to preload the optional BossRush thumbnail bundle.
            __result = new List<string>();
            return false;
        }

        [HarmonyPatch(typeof(QuestSelectionPage), "ChangeBossRushTexture")]
        [HarmonyPostfix]
        private static void QuestSelectionPage_ChangeBossRushTexture_Postfix(QuestSelectionPage __instance)
        {
            try
            {
                UITexture texture = AccessTools.Field(typeof(QuestSelectionPage), "_selectCharaTexture")?.GetValue(__instance) as UITexture;
                if (texture != null && texture.mainTexture == null)
                {
                    int charaId = GetCurrentEnemyCharaId();
                    texture.mainTexture = Toolbox.ResourcesManager.LoadObject<Texture>(
                        Toolbox.ResourcesManager.GetAssetTypePath(charaId.ToString(), ResourcesManager.AssetLoadPathType.ClassCharaBase, true),
                        true,
                        false);
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Quest selection texture fallback failed: {exception.Message}");
            }
        }

        [HarmonyPatch(typeof(QuestEventBossRushButton), "SetTexture")]
        [HarmonyPostfix]
        private static void QuestEventBossRushButton_SetTexture_Postfix(QuestEventBossRushButton __instance)
        {
            try
            {
                UITexture texture = AccessTools.Field(typeof(QuestEventBossRushButton), "_texture")?.GetValue(__instance) as UITexture;
                if (texture != null && texture.mainTexture == null)
                {
                    int charaId = GetCurrentEnemyCharaId();
                    texture.mainTexture = Toolbox.ResourcesManager.LoadObject<Texture>(
                        Toolbox.ResourcesManager.GetAssetTypePath(charaId.ToString(), ResourcesManager.AssetLoadPathType.ClassCharaWideThumbnail, true),
                        true,
                        false);
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] BossRush button texture fallback failed: {exception.Message}");
            }
        }

        [HarmonyPatch(typeof(QuestEventBossRushButton), "SetNameLabel")]
        [HarmonyPostfix]
        private static void QuestEventBossRushButton_SetNameLabel_Postfix(QuestEventBossRushButton __instance)
        {
            UILabel label = AccessTools.Field(typeof(QuestEventBossRushButton), "_nameLabel")?.GetValue(__instance) as UILabel;
            if (label != null && BossRushOfflineData.CurrentPackage != null)
            {
                label.text = BossRushOfflineData.CurrentPackage.DisplayName;
            }
        }

        [HarmonyPatch(typeof(QuestEventBossRushButton), "SetRewardStatusLabel")]
        [HarmonyPostfix]
        private static void QuestEventBossRushButton_SetRewardStatusLabel_Postfix(QuestEventBossRushButton __instance)
        {
            UILabel label = AccessTools.Field(typeof(QuestEventBossRushButton), "_rewardStatusLabel")?.GetValue(__instance) as UILabel;
            BossRushState state = BossRushOfflineData.GetState();
            if (label == null || state == null) return;

            string textId = state.IsFinished
                ? "BossRush_0015"
                : state.Progress == 0 && (state.PlayerDeckCardIds == null || state.PlayerDeckCardIds.Count == 0)
                    ? "BossRush_0013"
                    : "BossRush_0014";
            label.text = Data.SystemText.Get(textId);
        }

        [HarmonyPatch(typeof(QuestSelectionPage), nameof(QuestSelectionPage.SelectBossRushButton))]
        [HarmonyPostfix]
        private static void QuestSelectionPage_SelectBossRushButton_Postfix(QuestSelectionPage __instance)
        {
            UILabel label = AccessTools.Field(typeof(QuestSelectionPage), "_decideButtonTextLabel")?.GetValue(__instance) as UILabel;
            BossRushState state = BossRushOfflineData.GetState();
            if (label != null && state != null)
            {
                bool hasDeck = state.PlayerDeckCardIds != null && state.PlayerDeckCardIds.Count > 0;
                label.text = Data.SystemText.Get(hasDeck ? "BossRush_0008" : "BossRush_0009");
            }
        }

        [HarmonyPatch(typeof(QuestBossRushRegisterDeckTask), nameof(QuestBossRushRegisterDeckTask.SetParameter))]
        [HarmonyPostfix]
        private static void RegisterDeck_Postfix(DeckData deckData)
        {
            BossRushOfflineData.CaptureDeck(deckData);
        }

        [HarmonyPatch(typeof(QuestBossRushSetAbilityTask), nameof(QuestBossRushSetAbilityTask.SetParameter))]
        [HarmonyPostfix]
        private static void SetAbility_Postfix(int abilityId, bool isFoil, int maxLifeChange, int lifeChange)
        {
            BossRushOfflineData.CaptureAbility(abilityId, isFoil, maxLifeChange, lifeChange);
        }

        [HarmonyPatch(typeof(BossRushFinishTask), nameof(BossRushFinishTask.SetParameter))]
        [HarmonyPostfix]
        private static void Finish_Postfix(int currentLife, int maxLife, bool is_win, int totalTurn)
        {
            BossRushOfflineData.CaptureFinish(is_win, currentLife, maxLife, totalTurn);
        }

        [HarmonyPatch(typeof(BossRushHiddenBattleFinishTask), nameof(BossRushHiddenBattleFinishTask.SetParameter))]
        [HarmonyPostfix]
        private static void HiddenFinish_Postfix(bool is_win, int totalTurn)
        {
            BossRushOfflineData.CaptureHiddenFinish(is_win, totalTurn);
        }

        [HarmonyPatch(typeof(QuestEventBossRushButton), nameof(QuestEventBossRushButton.OnDecideButtonClick))]
        [HarmonyPrefix]
        private static bool BossRushButton_Prefix(QuestEventBossRushButton __instance)
        {
            BossRushOfflineData.ReloadPackages();
            IReadOnlyList<BossRushPackage> packages = BossRushOfflineData.AvailablePackages;
            if (!BossRushOfflineData.IsActive || packages == null || packages.Count == 0)
            {
                return true;
            }

            GameMgr.GetIns().GetDataMgr().SetQuestBattleData(null);
            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_BTN_DECIDE, false);

            if (packages.Count == 1)
            {
                BossRushOfflineData.SelectPackage(packages[0].Id);
                EnterSelectedPackage(__instance);
                return false;
            }

            List<string> labels = packages.Select(package => package.DisplayName + " [" + package.Id + "]").ToList();
            int selected = Math.Max(0, packages.ToList().FindIndex(package => package == BossRushOfflineData.CurrentPackage));
            DrumrollDialog.Create(labels, selected, null, null, index =>
            {
                if (index < 0 || index >= packages.Count) return;
                BossRushOfflineData.SelectPackage(packages[index].Id);
                EnterSelectedPackage(__instance);
            }, "BossRush");
            return false;
        }

        private static void EnterSelectedPackage(QuestEventBossRushButton button)
        {
            try
            {
                BossRushState state = BossRushOfflineData.GetState();
                string methodName = state?.PlayerDeckCardIds != null && state.PlayerDeckCardIds.Count > 0
                    ? "MoveToBossRush"
                    : "ShowSelectDeck";
                AccessTools.Method(typeof(QuestEventBossRushButton), methodName)?.Invoke(button, null);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError($"[BossRush] Failed to enter selected package: {exception}");
            }
        }

        [HarmonyPatch(typeof(BossRushLobby), "InitializeTopBar")]
        [HarmonyPrefix]
        private static bool BossRushLobby_InitializeTopBar_Prefix(BossRushLobby __instance)
        {
            BossRushPackage package = BossRushOfflineData.CurrentPackage;
            if (package == null)
            {
                return true;
            }

            UIManager.ChangeViewSceneParam changeViewSceneParam = new UIManager.ChangeViewSceneParam
            {
                MyPageMenuIndex = 1,
                IsCutCardMotion = true
            };
            UIManager.GetInstance().CreateTopBar(
                __instance.gameObject,
                package.DisplayName,
                UIManager.ViewScene.QuestSelectionPage,
                false,
                changeViewSceneParam,
                false).gameObject.layer = LayerMask.NameToLayer("FrontUI");
            return false;
        }

        [HarmonyPatch(typeof(BossRushLobby), "OnClickDetailButton")]
        [HarmonyPrefix]
        private static bool BossRushLobby_OnClickDetailButton_Prefix()
        {
            BossRushPackage package = BossRushOfflineData.CurrentPackage;
            if (package == null || string.IsNullOrWhiteSpace(package.DetailText))
            {
                return true;
            }

            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_BTN_DECIDE, false);
            DialogBase dialog = UIManager.GetInstance().CreateDialogClose(false, false);
            dialog.SetSize(DialogBase.Size.L);
            dialog.SetTitleLabel(package.DetailTitle);
            dialog.SetText(package.DetailText, true);
            dialog.SetButtonLayout(DialogBase.ButtonLayout.OkBtn);
            return false;
        }

        [HarmonyPatch(typeof(DataMgr), nameof(DataMgr.SetSpecialBattleSetting))]
        [HarmonyPrefix]
        private static void DataMgr_SetSpecialBattleSetting_Prefix(
            DataMgr __instance,
            ref bool? isPlayerFirst,
            ref int playerPp,
            ref int enemyPp,
            ref string idOverrideBattleLogText,
            ref string id,
            ref string tokenDrawEffectOverride,
            ref string specialTokenDrawEffectOverride,
            ref bool isVsEffectOverride,
            ref int classDestroyEffectOverride)
        {
            if (!BossRushOfflineData.IsActive ||
                (__instance.m_BattleType != DataMgr.BattleType.BossRushQuest &&
                 __instance.m_BattleType != DataMgr.BattleType.SecretBossQuest))
            {
                return;
            }

            BossRushBoss boss = BossRushOfflineData.GetCurrentBoss();
            if (boss == null) return;
            if (!isPlayerFirst.HasValue) isPlayerFirst = boss.PlayerFirstTurn;
            if (playerPp == 0) playerPp = boss.PlayerStartPp;
            if (enemyPp == 0) enemyPp = boss.EnemyStartPp;
            if (string.IsNullOrEmpty(idOverrideBattleLogText)) idOverrideBattleLogText = boss.IdOverrideInBattleLog ?? string.Empty;
            if (string.IsNullOrEmpty(id)) id = boss.SpecialBattleId ?? string.Empty;
            if (string.IsNullOrEmpty(tokenDrawEffectOverride)) tokenDrawEffectOverride = boss.TokenDrawEffectOverride ?? string.Empty;
            if (string.IsNullOrEmpty(specialTokenDrawEffectOverride)) specialTokenDrawEffectOverride = boss.SpecialTokenDrawEffectOverride ?? string.Empty;
            if (!isVsEffectOverride) isVsEffectOverride = boss.VsEffectOverride;
            if (classDestroyEffectOverride == 0) classDestroyEffectOverride = boss.ClassDestroyEffectOverride;
        }

        [HarmonyPatch(typeof(DataMgr), nameof(DataMgr.SetBossRushBattleData))]
        [HarmonyPostfix]
        private static void DataMgr_SetBossRushBattleData_Postfix(DataMgr __instance)
        {
            BossRushBoss boss = BossRushOfflineData.GetCurrentBoss();
            BossRushBattleData battleData = __instance.BossRushBattleData;
            if (!BossRushOfflineData.IsActive || boss == null || battleData == null)
            {
                return;
            }

            SetPrivateProperty(battleData, nameof(BossRushBattleData.PlayerEmotionOverride), boss.PlayerEmotionOverride);
            SetPrivateProperty(battleData, nameof(BossRushBattleData.EnemyEmotionOverride), boss.EnemyEmotionOverride);

            // The lobby response already carries the chosen leader, but the client
            // may still be holding boss data fetched before the player picked one.
            // This is the last point before the battle scene reads the chara id.
            int charaId = BossRushOfflineData.ResolveCharaId(boss);
            if (charaId > 0 && battleData.CharaId != charaId)
            {
                Plugin.Logger.LogInfo(
                    $"[BossRush] Enemy leader for '{boss.Name}' switched from chara {battleData.CharaId} to {charaId}.");
                SetPrivateProperty(battleData, nameof(BossRushBattleData.CharaId), charaId);
            }
        }

        [HarmonyPatch(typeof(DataMgr), nameof(DataMgr.SetEnemySleeveId))]
        [HarmonyPrefix]
        private static void DataMgr_SetEnemySleeveId_Prefix(DataMgr __instance, ref long sleeveId)
        {
            if (!BossRushOfflineData.IsActive ||
                (__instance.m_BattleType != DataMgr.BattleType.BossRushQuest &&
                 __instance.m_BattleType != DataMgr.BattleType.SecretBossQuest) ||
                (sleeveId != 0L && sleeveId != 3000011L))
            {
                return;
            }

            BossRushBoss boss = BossRushOfflineData.GetCurrentBoss();
            if (boss != null)
            {
                sleeveId = boss.EnemySleeveId;
            }
        }

        [HarmonyPatch(typeof(BossRushLobby), "GetBackGroundTexturePath")]
        [HarmonyPostfix]
        private static void BossRushBackground_Postfix(bool isFetch, ref string __result)
        {
            string background = BossRushOfflineData.GetLobbyBackgroundName();
            __result = Toolbox.ResourcesManager.GetAssetTypePath(
                background, ResourcesManager.AssetLoadPathType.Background, isFetch);
            if (!isFetch)
            {
                Plugin.Logger.LogInfo($"[BossRush] Loading lobby background '{background}'.");
            }
        }

        [HarmonyPatch(typeof(BossRushLobby), "Initialize")]
        [HarmonyPostfix]
        private static void BossRushLobby_Initialize_Postfix(BossRushLobby __instance)
        {
            UITexture background = AccessTools.Field(typeof(BossRushLobby), "_bgTexture")?
                .GetValue(__instance) as UITexture;
            if (background != null && background.mainTexture == null)
            {
                background.mainTexture = Texture2D.whiteTexture;
                background.color = new Color32(38, 35, 43, 255);
                Plugin.Logger.LogWarning(
                    $"[BossRush] Lobby background '{BossRushOfflineData.GetLobbyBackgroundName()}' is unavailable; " +
                    "using a neutral fallback.");
            }

            try
            {
                int themedLabelCount = ApplyLobbyUiTheme(__instance);
                __instance.StartCoroutine(ReapplyLobbyUiTheme(__instance));
                Plugin.Logger.LogInfo(
                    $"[BossRush] Applied UI theme '{BossRushOfflineData.CurrentPackage?.UiTheme}' " +
                    $"to {themedLabelCount} lobby labels.");
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not apply lobby UI theme: {exception.Message}");
            }

            try
            {
                CreateLeaderSelectButton(__instance);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not add the leader selection button: {exception.Message}");
            }
        }

        /// <summary>
        /// Adds a local opponent setup button to the lobby. The stock lobby prefab
        /// has no free control, and its own buttons carry localised or image based
        /// captions, so a settings-style button is built from scratch next to the
        /// detail button instead of cloning one.
        /// </summary>
        private static void CreateLeaderSelectButton(BossRushLobby lobby)
        {
            if (!BossRushOfflineData.IsActive)
            {
                return;
            }

            Transform anchor = GetLobbyButtonAnchor(lobby, out Vector3 position);
            if (anchor == null)
            {
                Plugin.Logger.LogWarning("[BossRush] No lobby anchor found; the opponent setup button was skipped.");
                return;
            }

            GameObject parent = anchor.parent == null ? lobby.gameObject : anchor.parent.gameObject;
            if (parent.transform.Find("BossRushLeaderSelectButton") != null)
            {
                // Initialize runs again when the lobby returns from a battle.
                return;
            }

            SettingBase settingTemplate = UIManager.GetInstance().OptionSettingPrefab;
            if (settingTemplate == null || settingTemplate.m_itemButton == null)
            {
                Plugin.Logger.LogWarning("[BossRush] OptionSettingPrefab unavailable; the opponent setup button was skipped.");
                return;
            }

            GameObject buttonObject = NGUITools.AddChild(parent, settingTemplate.m_itemButton);
            buttonObject.name = "BossRushLeaderSelectButton";
            buttonObject.layer = anchor.gameObject.layer;
            buttonObject.transform.localScale = anchor.localScale;
            buttonObject.transform.localPosition = position;
            buttonObject.SetActive(true);

            ItemButton item = buttonObject.GetComponent<ItemButton>();
            item.SetActive_SeparatorLine(false);
            item.SetActive_SpriteOnButton(false);
            item._subLabel.gameObject.SetActive(false);
            item._sprite.ResetAnchors();
            item._sprite.pivot = UIWidget.Pivot.Center;
            item._sprite.transform.localPosition = Vector3.zero;
            item._sprite.SetDimensions(120, 40);
            item._label.ResetAnchors();
            item._label.pivot = UIWidget.Pivot.Center;
            item._label.alignment = NGUIText.Alignment.Center;
            item._label.overflowMethod = UILabel.Overflow.ShrinkContent;
            item._label.SetDimensions(108, 36);
            item._label.transform.localPosition = Vector3.zero;
            item.SetValue("自选");
            item._collider.size = new Vector3(120, 40, item._collider.size.z);

            UIButton button = item._button;
            button.isEnabled = true;
            button.onClick.Clear();
            button.onClick.Add(new EventDelegate(delegate
            {
                GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_BTN_DECIDE, false);
                BossRushLeaderSelectWindow.Open(lobby);
            }));

            UIManager.SetObjectToGrey(buttonObject, false, null, null);
            Plugin.Logger.LogInfo(
                $"[BossRush] Opponent setup button placed at {buttonObject.transform.localPosition} " +
                $"under '{parent.name}'.");
        }

        /// <summary>
        /// Picks where the added button sits. The local mode always reports empty
        /// rewards, so the treasure box slot above the reward label is free space
        /// in the stock layout; the button row below the boss panel is the
        /// fallback when that object is missing.
        /// </summary>
        private static Transform GetLobbyButtonAnchor(BossRushLobby lobby, out Vector3 position)
        {
            UISprite treasureBox = AccessTools.Field(typeof(BossRushLobby), "_treasureBoxSprite")?
                .GetValue(lobby) as UISprite;
            if (treasureBox != null)
            {
                position = treasureBox.transform.localPosition;
                return treasureBox.transform;
            }

            UIButton receiveReward = AccessTools.Field(typeof(BossRushLobby), "_receiveRewardButton")?
                .GetValue(lobby) as UIButton;
            if (receiveReward != null)
            {
                position = receiveReward.transform.localPosition + new Vector3(0f, 90f, 0f);
                return receiveReward.transform;
            }

            UIButton detailButton = AccessTools.Field(typeof(BossRushLobby), "_detailButton")?
                .GetValue(lobby) as UIButton;
            if (detailButton != null)
            {
                position = detailButton.transform.localPosition + new Vector3(0f, -46f, 0f);
                return detailButton.transform;
            }

            position = Vector3.zero;
            return null;
        }

        private static IEnumerator ReapplyLobbyUiTheme(BossRushLobby lobby)
        {
            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
                if (lobby == null) yield break;

                try
                {
                    ApplyLobbyUiTheme(lobby);
                }
                catch (Exception exception)
                {
                    Plugin.Logger.LogWarning($"[BossRush] Could not reapply lobby UI theme: {exception.Message}");
                    yield break;
                }
            }
        }

        private static int ApplyLobbyUiTheme(BossRushLobby lobby)
        {
            UISprite window = AccessTools.Field(typeof(BossRushLobby), "_windowSprite")?
                .GetValue(lobby) as UISprite;
            if (window != null)
            {
                window.color = GetLobbyPanelColor();
            }

            GameObject panelRoot = AccessTools.Field(typeof(BossRushLobby), "_animationFromLeft")?
                .GetValue(lobby) as GameObject;
            if (panelRoot == null) return 0;

            int themedLabelCount = 0;
            foreach (UILabel label in panelRoot.GetComponentsInChildren<UILabel>(true))
            {
                if (label == null) continue;
                label.applyGradient = false;
                label.color = new Color32(255, 248, 224, 255);
                label.effectStyle = UILabel.Effect.Outline8;
                label.effectColor = new Color32(30, 25, 33, 255);
                label.effectDistance = new Vector2(2f, 2f);
                themedLabelCount++;
            }

            return themedLabelCount;
        }

        private static Color GetLobbyPanelColor()
        {
            string theme = BossRushOfflineData.CurrentPackage?.UiTheme?.Trim().ToLowerInvariant();
            switch (theme)
            {
                case "grand_prix_2": return new Color32(66, 48, 70, 255);
                case "colosseum_1": return new Color32(45, 66, 56, 255);
                case "colosseum_2": return new Color32(74, 48, 50, 255);
                case "two_pick": return new Color32(45, 56, 78, 255);
                case "quest": return new Color32(58, 59, 56, 255);
                case "classic": return new Color32(56, 49, 58, 255);
                case "grand_prix_1":
                default:
                    return new Color32(46, 58, 64, 255);
            }
        }

        [HarmonyPatch(typeof(BossRushLobby), "InitializeBattleButton")]
        [HarmonyPostfix]
        private static void BossRushLobby_InitializeBattleButton_Postfix(BossRushLobby __instance)
        {
            if (!BossRushOfflineData.IsActive)
            {
                return;
            }

            try
            {
                UIButton retireButton = AccessTools.Field(typeof(BossRushLobby), "_retireButton")?
                    .GetValue(__instance) as UIButton;
                if (retireButton == null)
                {
                    return;
                }

                BossRushState state = BossRushOfflineData.GetState();
                if (state == null || !state.IsFinished)
                {
                    return;
                }

                // The stock lobby greys this control whenever status != BATTLE.
                // A completed local run uses Retire as "End and restart".
                UIManager.SetObjectToGrey(retireButton.gameObject, false, null, null);
                retireButton.isEnabled = true;
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Failed to re-enable completed-run retire button: {exception.Message}");
            }
        }

        [HarmonyPatch(
            typeof(DataMgr),
            nameof(DataMgr.SetCurrentEnemyDeckDataFromAIDeck),
            new Type[]
            {
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(bool),
                typeof(int),
                typeof(List<int>)
            })]
        [HarmonyPrefix]
        private static bool DataMgr_SetCurrentEnemyDeckDataFromAIDeck_Prefix(
            DataMgr __instance,
            int classID,
            int difficulty,
            ref int logicLevel,
            int maxLife,
            int deckId,
            int styleId,
            int emoteId,
            ref bool useInnerEmote,
            int enemyAiID,
            List<int> specialAbilityIdList)
        {
            if (__instance.m_BattleType != DataMgr.BattleType.BossRushQuest &&
                __instance.m_BattleType != DataMgr.BattleType.SecretBossQuest)
            {
                return true;
            }
            BossRushBoss boss = BossRushOfflineData.GetCurrentBoss();
            if (boss == null)
            {
                return true;
            }

            // These are direct SetCurrentEnemyDeckDataFromAIDeck arguments, so
            // the local package should control them for both preset and custom decks.
            logicLevel = boss.LogicLevel;
            useInnerEmote = boss.UseInnerEmote;

            try
            {
                RegisterBossRushCsv(boss);
                List<int> customDeck = boss.CustomDeckCardIds?.ToList() ?? new List<int>();
                if (customDeck.Count == 0)
                {
                    string localDeckPath = BossRushOfflineData.ResolveAiPath(boss, "deck");
                    if (!string.IsNullOrEmpty(localDeckPath))
                    {
                        string key = "shadowbus/ai/deck/" + System.IO.Path.GetFileNameWithoutExtension(localDeckPath);
                        AICardDataAssetSet assetSet;
                        if (Data.Master.AIDeckDic != null && Data.Master.AIDeckDic.TryGetValue(key, out assetSet))
                        {
                            foreach (AICardDataAsset card in assetSet.Set)
                            {
                                for (int count = 0; count < card.CardNum; count++) customDeck.Add(card.CardID);
                            }
                        }
                    }
                }
                customDeck = customDeck.Where(cardId =>
                {
                    try { return CardMaster.GetInstance(CardMaster.CardMasterId.Default).GetCardParameterFromId(cardId) != null; }
                    catch { return false; }
                }).ToList();
                if (customDeck.Count == 0)
                {
                    return true;
                }
                __instance.SetEnemyAIDeckFromCustomDeck(
                    boss.EnemyClass,
                    customDeck,
                    difficulty,
                    logicLevel,
                    boss.EnemyLife,
                    styleId,
                    emoteId,
                    useInnerEmote,
                    enemyAiID);
                Plugin.Logger.LogInfo($"[BossRush] Applied custom enemy deck for '{boss.Name}' ({customDeck.Count} cards).");
                return false;
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Custom AI setup failed for '{boss.Name}', falling back to official AI: {exception.Message}");
                return true;
            }
        }

        private static void RegisterBossRushCsv(BossRushBoss boss)
        {
            string deckPath = BossRushOfflineData.ResolveAiPath(boss, "deck");
            string stylePath = BossRushOfflineData.ResolveAiPath(boss, "style");
            string emotePath = BossRushOfflineData.ResolveAiPath(boss, "emote");
            string styleKey = null;
            string emoteKey = null;
            if (!string.IsNullOrEmpty(deckPath) && System.IO.File.Exists(deckPath)) AIManager.RegisterLocalDeckCsv(deckPath);
            if (!string.IsNullOrEmpty(stylePath) && System.IO.File.Exists(stylePath)) styleKey = AIManager.RegisterLocalStyleCsv(stylePath);
            if (!string.IsNullOrEmpty(emotePath) && System.IO.File.Exists(emotePath))
            {
                // Registering without a leader blanks every {LEADER} cell and leaves
                // {AUTO} unresolved, so the boss would emote silently. The leader is
                // known here, and one CSV can serve bosses with different leaders as
                // long as each registration gets its own cache key.
                ClassCharacterMasterData leader = ResolveBossLeader(boss);
                string leaderVoiceId = AIManager.ResolveLeaderVoiceId(leader);
                emoteKey = AIManager.RegisterLocalEmoteCsvForLeader(
                    emotePath,
                    leaderVoiceId,
                    leader,
                    "bossrush_" + BossRushOfflineData.ResolveCharaId(boss));
            }
            int aiId = BossRushOfflineData.ResolveAiId(boss.EnemyAiId);
            try
            {
                // The battle loads the AI the lobby response reported, which is the
                // resolved id. Reading the raw one throws on installs that do not
                // have it and used to skip both aliases.
                StoryAISettingData setting = Data.Master.QuestAISettingList?.GetSettingData(aiId);
                if (setting != null)
                {
                    if (!string.IsNullOrEmpty(styleKey) && Data.Master.AIStyleDic != null)
                    {
                        List<AIPolicyDataAsset> local;
                        if (Data.Master.AIStyleDic.TryGetValue(styleKey, out local))
                        {
                            Data.Master.AIStyleDic["ai/" + Data.Master.AIStyleFileNameList.GetFileName(setting.StyleId)] = local;
                        }
                    }
                    if (!string.IsNullOrEmpty(emoteKey) && Data.Master.AIEmoteDic != null)
                    {
                        List<AIEmoteDataAsset> local;
                        if (Data.Master.AIEmoteDic.TryGetValue(emoteKey, out local))
                        {
                            Data.Master.AIEmoteDic["ai/" + Data.Master.AIEmoteFileNameList.GetFileName(setting.EmoteId)] = local;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning(
                    $"[BossRush] Local style/emote alias setup failed for AI id {aiId}: {exception.Message}");
            }
            if (!string.IsNullOrEmpty(deckPath) || !string.IsNullOrEmpty(stylePath) || !string.IsNullOrEmpty(emotePath))
            {
                DataMgr dataMgr = GameMgr.GetIns().GetDataMgr();
                dataMgr.RegisterAllAIData();
            }
        }

        private static ClassCharacterMasterData ResolveBossLeader(BossRushBoss boss)
        {
            int charaId = BossRushOfflineData.ResolveCharaId(boss);
            if (charaId <= 0)
            {
                return null;
            }

            try
            {
                ClassCharacterMasterData leader = GameMgr.GetIns().GetDataMgr().GetCharaPrmByCharaId(charaId);
                if (leader != null)
                {
                    return leader;
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning(
                    $"[BossRush] Could not read leader data for chara {charaId}: {exception.Message}");
            }

            return Data.Master?.ClassCharacterList?.FirstOrDefault(item => item != null && item.chara_id == charaId);
        }
    }
}
