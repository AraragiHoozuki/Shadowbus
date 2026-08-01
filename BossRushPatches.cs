using HarmonyLib;
using Cute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Wizard;

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

            int charaId = BossRushOfflineData.GetCurrentBoss()?.EnemyCharaId ?? 1;
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
                    int charaId = BossRushOfflineData.GetCurrentBoss()?.EnemyCharaId ?? 1;
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
                    int charaId = BossRushOfflineData.GetCurrentBoss()?.EnemyCharaId ?? 1;
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
                return;
            }

            try
            {
                if (Toolbox.ResourcesManager.LoadObject<Texture>(__result, false, false) != null)
                {
                    return;
                }
            }
            catch
            {
            }

            Plugin.Logger.LogWarning($"[BossRush] Lobby background '{background}' is unavailable; using the Quest background.");
            __result = Toolbox.ResourcesManager.GetAssetTypePath(
                "bg_quest", ResourcesManager.AssetLoadPathType.Background, true);
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
            if (!string.IsNullOrEmpty(deckPath) && System.IO.File.Exists(deckPath)) AIManager.RegisterLocalDeckCsv(deckPath);
            if (!string.IsNullOrEmpty(stylePath) && System.IO.File.Exists(stylePath)) AIManager.RegisterLocalStyleCsv(stylePath);
            if (!string.IsNullOrEmpty(emotePath) && System.IO.File.Exists(emotePath)) AIManager.RegisterLocalEmoteCsv(emotePath);
            try
            {
                StoryAISettingData setting = Data.Master.QuestAISettingList?.GetSettingData(boss.EnemyAiId);
                if (setting != null)
                {
                    if (!string.IsNullOrEmpty(stylePath) && Data.Master.AIStyleDic != null)
                    {
                        string localKey = "shadowbus/ai/style/" + System.IO.Path.GetFileNameWithoutExtension(stylePath);
                        List<AIPolicyDataAsset> local;
                        if (Data.Master.AIStyleDic.TryGetValue(localKey, out local))
                        {
                            Data.Master.AIStyleDic["ai/" + Data.Master.AIStyleFileNameList.GetFileName(setting.StyleId)] = local;
                        }
                    }
                    if (!string.IsNullOrEmpty(emotePath) && Data.Master.AIEmoteDic != null)
                    {
                        string localKey = "shadowbus/ai/emote/" + System.IO.Path.GetFileNameWithoutExtension(emotePath);
                        List<AIEmoteDataAsset> local;
                        if (Data.Master.AIEmoteDic.TryGetValue(localKey, out local))
                        {
                            Data.Master.AIEmoteDic["ai/" + Data.Master.AIEmoteFileNameList.GetFileName(setting.EmoteId)] = local;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Local style/emote alias setup failed: {exception.Message}");
            }
            if (!string.IsNullOrEmpty(deckPath) || !string.IsNullOrEmpty(stylePath) || !string.IsNullOrEmpty(emotePath))
            {
                DataMgr dataMgr = GameMgr.GetIns().GetDataMgr();
                dataMgr.RegisterAllAIData();
            }
        }
    }
}
