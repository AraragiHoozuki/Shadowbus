using BestHTTP.SocketIO;
using Convention;
using Cute;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Wizard;
using Wizard.Battle;
using Wizard.RoomMatch;

namespace Shadowbus
{
    internal static class P2PPatches
    {
        [HarmonyPatch(
            typeof(PlayerControllerForOwn),
            MethodType.Constructor,
            new[] { typeof(Player), typeof(RoomConnectController) })]
        [HarmonyPostfix]
        private static void PlayerControllerForOwn_Constructor_Postfix(Player target)
        {
            ApplyProfileToRoomPlayer(
                target,
                ProfileOfflineData.CreateP2PProfile(P2PIdentity.ViewerId),
                "local room player");
        }

        [HarmonyPatch(
            typeof(PlayerControllerForOwn),
            nameof(PlayerControllerForOwn.SelectDeck),
            new[] { typeof(DeckData), typeof(bool) })]
        [HarmonyPrefix]
        private static bool PlayerControllerForOwn_SelectDeck_Prefix(DeckData deck)
        {
            if (!P2PRuntime.IsActive || P2PRuntime.IsDeckAllowed(
                deck,
                out CustomFormatDefinition definition,
                out CustomFormatViolation violation))
            {
                return true;
            }

            CardMaster cardMaster = CardMaster.GetInstanceForBattle();
            DialogBase dialog = UIManager.GetInstance().CreateDialogClose(false, false);
            dialog.SetSize(DialogBase.Size.M);
            dialog.SetTitleLabel("无法选择卡组");
            dialog.SetText(
                $"该卡组不符合「{definition.DisplayName}」的规则。\n" +
                CustomFormatViolationText.Describe(violation, cardMaster),
                true);
            dialog.SetButtonLayout(DialogBase.ButtonLayout.OkBtn);
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Rejected room deck {deck?.GetDeckID() ?? 0} " +
                $"for {definition.Id} based on its actual cards: " +
                violation.ToLogMessage() + ".");
            return false;
        }

        [HarmonyPatch(typeof(RoomPlayerDisplayBase), "SetPlayerData")]
        [HarmonyPrefix]
        private static void RoomPlayerDisplayBase_SetPlayerData_Prefix(Player player)
        {
            if (!P2PRuntime.IsActive || player == null)
            {
                return;
            }

            RoomConnectController controller = RoomBase.ConnectController;
            bool isOwnPlayer = ReferenceEquals(controller?.OwnCtrl?.Target, player);
            P2PProfile profile = isOwnPlayer
                ? ProfileOfflineData.CreateP2PProfile(P2PIdentity.ViewerId)
                : P2PRuntime.RemoteProfile;
            ApplyProfileToRoomPlayer(
                player,
                profile,
                isOwnPlayer ? "local room display" : "remote room display");
        }

        private static void ApplyProfileToRoomPlayer(
            Player player,
            P2PProfile profile,
            string context)
        {
            if (player == null || profile == null)
            {
                return;
            }

            string previousName = player.Name;
            player.Name = profile.UserName ?? string.Empty;
            player.Emblem = profile.EmblemId;
            player.Degree = profile.DegreeId;
            player.Country = profile.CountryCode ?? string.Empty;
            player.Rank = profile.Rank;
            player.HighFormatRank = profile.Rank;
            player.IsOfficialUser = profile.IsOfficial;
            player.ViewerId = profile.ViewerId;

            if (!string.Equals(previousName, player.Name, StringComparison.Ordinal))
            {
                Plugin.Logger.LogInfo(
                    $"[P2P] Updated {context} name from '{previousName}' to '{player.Name}'.");
            }
        }

        [HarmonyPatch(typeof(MyPageItemBattle), nameof(MyPageItemBattle.StartRoomIDInput))]
        [HarmonyPrefix]
        private static bool MyPageItemBattle_StartRoomIDInput_Prefix(
            MyPageItemBattle __instance,
            bool isWatch,
            ConventionInfo conventionInfo)
        {
            if (isWatch || conventionInfo != null)
            {
                return true;
            }

            DialogBase dialog = InputDialog.Create(
                P2PConnectionCode.MaximumLength,
                0,
                UIInput.KeyboardType.ASCIICapable);
            dialog.SetSize(DialogBase.Size.XL);
            dialog.SetTitleLabel(Data.SystemText.Get("RoomBattle_0006"));
            dialog.InputAreaObjs.labels[1].text = string.Empty;
            dialog.InputAreaObjs.labels[2].text = P2PConnectionCode.Prefix;
            dialog.InputAreaObjs.labels[3].text = string.Empty;
            dialog.SetButtonDisable(true, false, false, false);

            UIInput input = dialog.GetComponentInChildren<UIInput>();
            if (input == null)
            {
                Plugin.Logger.LogError("[P2P] The connection-code text input could not be created.");
                dialog.CloseWithoutSelect();
                return false;
            }

            input.validation = UIInput.Validation.None;
            input.characterLimit = P2PConnectionCode.MaximumLength;
            input.defaultText = P2PConnectionCode.Prefix + "...";
            if (input.label != null)
            {
                input.label.overflowMethod = UILabel.Overflow.ClampContent;
                input.label.maxLineCount = 1;
            }
            input.onChange.Add(new EventDelegate(delegate
            {
                dialog.SetButtonDisable(
                    !P2PConnectionCode.TryDecode(input.value, out _),
                    false,
                    false,
                    false);
            }));

            dialog.onPushButton1 = delegate
            {
                string connectionCode = input.value;
                if (!P2PConnectionCode.TryDecode(connectionCode, out _))
                {
                    return;
                }
                RoomConnectController.InitializeParameter parameters =
                    new RoomConnectController.InitializeParameter(
                        RoomConnectController.PositionMode.VISITOR,
                        new BattleParameter(
                            NetworkDefine.ServerBattleType.Free,
                            Format.Max,
                            TwoPickFormat.None,
                            RoomConnectController.BattleRule.None,
                            false),
                        connectionCode);
                __instance.Parent.StartCoroutine(__instance.JoinRoom(parameters, false));
            };
            return false;
        }

        [HarmonyPatch(typeof(IDInput), "Paste")]
        [HarmonyPrefix]
        private static bool IDInput_Paste_Prefix(IDInput __instance)
        {
            string clipboard = ClipboardHelper.Clipboard;
            if (string.IsNullOrWhiteSpace(clipboard))
            {
                return true;
            }
            string password = Regex.Replace(clipboard, "\\s", string.Empty);
            if (!password.StartsWith(P2PConnectionCode.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_TOGGLE_ON, false);
            if (!P2PConnectionCode.TryDecode(password, out _))
            {
                Plugin.Logger.LogWarning("[P2P] The pasted room password is invalid.");
                return false;
            }

            __instance.InputID = password;
            __instance.InputIndex = __instance._maxIndex + 1;
            if (__instance._currentLayout != null)
            {
                foreach (UILabel label in __instance._currentLayout._label)
                {
                    label.text = "*";
                }
            }
            __instance.CurrentDialogBase.SetButtonDisable(false, false, false, false);
            return false;
        }

        [HarmonyPatch(typeof(IDInput), nameof(IDInput.ClearNum))]
        [HarmonyPrefix]
        private static bool IDInput_ClearNum_Prefix(IDInput __instance)
        {
            if (string.IsNullOrEmpty(__instance.InputID) ||
                !__instance.InputID.StartsWith(
                    P2PConnectionCode.Prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            __instance.InputID = string.Empty;
            __instance.InputIndex = 0;
            if (__instance._currentLayout != null)
            {
                foreach (UILabel label in __instance._currentLayout._label)
                {
                    label.text = "_";
                }
            }
            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_TOGGLE_OFF, false);
            __instance.CurrentDialogBase.SetButtonDisable(true, false, false, false);
            return false;
        }

        [HarmonyPatch(typeof(RoomUIBase), "OnClickRoomIdCopy")]
        [HarmonyPrefix]
        private static bool RoomUIBase_OnClickRoomIdCopy_Prefix(RoomUIBase __instance)
        {
            if (!P2PRuntime.IsActive || P2PRuntime.Role != P2PRole.Host ||
                string.IsNullOrEmpty(P2PRuntime.ConnectionCode))
            {
                return true;
            }

            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_BTN_DECIDE, false);
            NativePluginWrapper.SetStringToClipboard(P2PRuntime.ConnectionCode);
            __instance.CreateDialog(
                Data.SystemText.Get("RoomBattle_0170"),
                Data.SystemText.Get("RoomBattle_0171"),
                null);
            return false;
        }

        [HarmonyPatch(typeof(RoomRuleSelectDialog), nameof(RoomRuleSelectDialog.Initialize))]
        [HarmonyPrefix]
        private static void RoomRuleSelectDialog_Initialize_Prefix(
            RoomRuleSetting setting,
            bool isTwoPick,
            bool isSelectBaseRule)
        {
            if (isSelectBaseRule)
            {
                return;
            }
            if (isTwoPick)
            {
                ApplyFixedP2PTwoPickRule(setting);
            }
            else
            {
                ApplyFixedP2PRoomRule(setting);
            }
        }

        [HarmonyPatch(typeof(RoomRuleSelectDialog), "Start")]
        [HarmonyPostfix]
        private static void RoomRuleSelectDialog_Start_Postfix(
            RoomRuleSelectDialog __instance)
        {
            if (__instance == null || RoomRuleSelectDialog._isSelectBaseRule)
            {
                return;
            }

            if (__instance._is2pick)
            {
                ApplyFixedP2PTwoPickRule(__instance._setting);
                __instance._normalRuleLabel.text = RoomRuleSetting.GetWinTypeString(
                    RoomConnectController.BattleRule.Bo1);
                __instance._twoPickLabel.text =
                    P2PTwoPickRules.LoadSelected().DisplayName;
                DisableRuleChangeButton(__instance._normalRuleChangeButton);
                EnableRuleChangeButton(__instance._twoPickRuleChangeButton);
                return;
            }

            ApplyFixedP2PRoomRule(__instance._setting);
            __instance._normalRuleLabel.text = RoomRuleSetting.GetWinTypeString(
                RoomConnectController.BattleRule.Bo1);
            __instance._formatLabel.text = CustomFormatContext.RoomFormat.DisplayName;
            DisableRuleChangeButton(__instance._normalRuleChangeButton);
            EnableRuleChangeButton(__instance._formatChangeButton);
        }

        [HarmonyPatch(typeof(RoomRuleSelectDialog), "OnPushBattleTypeButton")]
        [HarmonyPrefix]
        private static bool RoomRuleSelectDialog_OnPushBattleTypeButton_Prefix(
            RoomRuleSelectDialog __instance)
        {
            if (__instance == null || !__instance._is2pick ||
                RoomRuleSelectDialog._isSelectBaseRule)
            {
                return true;
            }

            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_COMMON_BUTTON, false);
            List<P2PTwoPickRuleDefinition> definitions =
                P2PTwoPickRules.LoadAll().ToList();
            List<string> names = definitions
                .Select(definition => definition.DisplayName)
                .ToList();
            int selectedIndex = Math.Max(
                0,
                definitions.FindIndex(definition => string.Equals(
                    definition.Id,
                    P2PTwoPickRules.SelectedRuleId,
                    StringComparison.OrdinalIgnoreCase)));
            int pendingIndex = selectedIndex;
            __instance.SaveCurrentSetting();
            DialogBase selector = null;
            selector = DrumrollDialog.Create(
                names,
                selectedIndex,
                index => pendingIndex = index,
                null,
                null,
                string.Empty);
            __instance._dialogSelf.SetDisp(false);
            selector.SetTitleLabel(Data.SystemText.Get("RoomBattle_0094"));
            selector.SetButtonLayout(DialogBase.ButtonLayout.DecisionBtn);
            selector.onPushButton1 = () =>
            {
                P2PTwoPickRuleDefinition selected =
                    P2PTwoPickRules.Select(definitions[pendingIndex].Id);
                ApplyFixedP2PTwoPickRule(RoomRuleSelectDialog._settingSave);
                RoomRuleSelectDialog.ReCreateDialog(RoomRuleSelectDialog._settingSave);
                selector.SetDisp(false);
                Plugin.Logger.LogInfo(
                    $"[P2P] Selected Two Pick rule {selected.Id} " +
                    $"('{selected.DisplayName}').");
            };
            selector.onCloseWithoutSelect = () =>
            {
                RoomRuleSelectDialog.ReCreateDialog(RoomRuleSelectDialog._settingSave);
                selector.SetDisp(false);
            };
            selector.ResetBackViewAlpha();
            return false;
        }

        [HarmonyPatch(typeof(RoomRuleSelectDialog), "OnClickFormatChangeButton")]
        [HarmonyPrefix]
        private static bool RoomRuleSelectDialog_OnClickFormatChangeButton_Prefix(
            RoomRuleSelectDialog __instance)
        {
            if (__instance == null || __instance._is2pick ||
                RoomRuleSelectDialog._isSelectBaseRule)
            {
                return true;
            }

            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_COMMON_BUTTON, false);
            List<CustomFormatDefinition> definitions = CustomFormats.All.ToList();
            List<string> names = definitions.Select(definition => definition.DisplayName).ToList();
            int selectedIndex = Math.Max(
                0,
                definitions.FindIndex(definition => string.Equals(
                    definition.Id,
                    CustomFormatContext.RoomFormatId,
                    StringComparison.OrdinalIgnoreCase)));
            int pendingIndex = selectedIndex;
            __instance.SaveCurrentSetting();
            DialogBase selector = null;
            selector = DrumrollDialog.Create(
                names,
                selectedIndex,
                index => pendingIndex = index,
                null,
                null,
                string.Empty);
            __instance._dialogSelf.SetDisp(false);
            selector.SetTitleLabel(Data.SystemText.Get("RoomBattle_0107"));
            selector.SetButtonLayout(DialogBase.ButtonLayout.DecisionBtn);
            selector.onPushButton1 = () =>
            {
                CustomFormatContext.RoomFormatId = definitions[pendingIndex].Id;
                ApplyFixedP2PRoomRule(RoomRuleSelectDialog._settingSave);
                RoomRuleSelectDialog.ReCreateDialog(RoomRuleSelectDialog._settingSave);
                selector.SetDisp(false);
                Plugin.Logger.LogInfo(
                    $"[P2P] Selected room format {CustomFormatContext.RoomFormatId}.");
            };
            selector.onCloseWithoutSelect = () =>
            {
                RoomRuleSelectDialog.ReCreateDialog(RoomRuleSelectDialog._settingSave);
                selector.SetDisp(false);
            };
            selector.ResetBackViewAlpha();
            return false;
        }

        [HarmonyPatch(typeof(RoomRuleSetting), nameof(RoomRuleSetting.GetTopBarString))]
        [HarmonyPostfix]
        private static void RoomRuleSetting_GetTopBarString_Postfix(
            BattleParameter battleParameter,
            ref string __result)
        {
            if (P2PRuntime.IsActive &&
                battleParameter?.BattleType == NetworkDefine.ServerBattleType.RoomTwoPick &&
                P2PRuntime.Rules?.TwoPickRule != null)
            {
                __result = P2PRuntime.Rules.TwoPickRule.DisplayName + " " +
                    RoomRuleSetting.GetWinTypeString(battleParameter.Rule);
                return;
            }

            CustomFormatDefinition definition = CustomFormatContext.RoomFormat;
            if (!P2PRuntime.IsActive ||
                definition.Id == CustomFormats.UnlimitedId ||
                battleParameter?.DeckFormat != Format.Unlimited)
            {
                return;
            }

            string unlimitedName = FormatBehaviorManager.GetFormatName(Format.Unlimited);
            string commonUnlimitedName = Data.SystemText.Get("Common_0155");
            string updated = ReplaceFormatName(__result, unlimitedName, definition.DisplayName);
            updated = ReplaceFormatName(
                updated,
                commonUnlimitedName,
                definition.DisplayName);
            updated = ReplaceFormatName(
                updated,
                CustomFormats.Unlimited.DisplayName,
                definition.DisplayName);
            if (string.Equals(updated, __result, StringComparison.Ordinal))
            {
                updated = definition.DisplayName + " " +
                    RoomRuleSetting.GetWinTypeString(battleParameter.Rule);
            }
            __result = updated;
        }

        private static string ReplaceFormatName(
            string text,
            string oldValue,
            string newValue)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(oldValue))
            {
                return text;
            }
            return text.Replace(oldValue, newValue);
        }

        [HarmonyPatch(typeof(RoomRuleSelectDialog), "OnPushCreateButton")]
        [HarmonyPrefix]
        private static void RoomRuleSelectDialog_OnPushCreateButton_Prefix(
            RoomRuleSelectDialog __instance)
        {
            if (__instance == null || RoomRuleSelectDialog._isSelectBaseRule)
            {
                return;
            }
            if (__instance._is2pick)
            {
                ApplyFixedP2PTwoPickRule(__instance._setting);
            }
            else
            {
                ApplyFixedP2PRoomRule(__instance._setting);
            }
        }

        private static void ApplyFixedP2PRoomRule(RoomRuleSetting setting)
        {
            BattleParameter parameter = setting?.BattleParameterInstance;
            if (parameter == null)
            {
                return;
            }

            parameter.BattleType = NetworkDefine.ServerBattleType.OpenRoom;
            parameter.DeckFormat = Format.Unlimited;
            parameter.TwoPickFormat = TwoPickFormat.None;
            parameter.Rule = RoomConnectController.BattleRule.Bo1;
            PlayerPrefsWrapper.SetValue(
                PlayerPrefsWrapper.ROOM_MATCH_FORMAT,
                (int)Format.Unlimited);
            PlayerPrefsWrapper.SetValue(
                PlayerPrefsWrapper.LAST_ROOM_MATCH_RULE,
                (int)RoomConnectController.BattleRule.Bo1);
        }

        private static void ApplyFixedP2PTwoPickRule(RoomRuleSetting setting)
        {
            BattleParameter parameter = setting?.BattleParameterInstance;
            if (parameter == null)
            {
                return;
            }

            parameter.BattleType = NetworkDefine.ServerBattleType.RoomTwoPick;
            parameter.DeckFormat = Format.TwoPick;
            parameter.TwoPickFormat = TwoPickFormat.Normal;
            parameter.Rule = RoomConnectController.BattleRule.Bo1;
            PlayerPrefsWrapper.SetValue(
                PlayerPrefsWrapper.LAST_ROOM_MATCH_RULE,
                (int)RoomConnectController.BattleRule.Bo1);
        }

        private static void DisableRuleChangeButton(UIButton button)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.Clear();
            button.isEnabled = false;
            UIManager.SetObjectToGrey(button.gameObject, true, null, null);
        }

        private static void EnableRuleChangeButton(UIButton button)
        {
            if (button == null)
            {
                return;
            }

            button.isEnabled = true;
            UIManager.SetObjectToGrey(button.gameObject, false, null, null);
        }

        [HarmonyPatch(typeof(RoomBo1UI), nameof(RoomBo1UI.Initialize))]
        [HarmonyPostfix]
        private static void RoomBo1UI_Initialize_Postfix(RoomBo1UI __instance)
        {
            P2PRoomLifeUI.Attach(__instance);
            P2PRoomFormatUI.Attach(__instance);
        }

        [HarmonyPatch(typeof(RoomBo1DeckOpenUI), nameof(RoomBo1DeckOpenUI.Initialize))]
        [HarmonyPostfix]
        private static void RoomBo1DeckOpenUI_Initialize_Postfix(
            RoomBo1DeckOpenUI __instance)
        {
            P2PRoomLifeUI.Attach(__instance);
            P2PRoomFormatUI.Attach(__instance);
        }

        [HarmonyPatch(typeof(RoomBase), nameof(RoomBase.DestroyMyRoomInfo))]
        [HarmonyPostfix]
        private static void RoomBase_DestroyMyRoomInfo_Postfix()
        {
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            Plugin.Logger.LogInfo(
                "[P2P] Native room state was destroyed; clearing the P2P session.");
            P2PRuntime.Shutdown();
        }

        [HarmonyPatch(
            typeof(BattleManagerBase),
            nameof(BattleManagerBase.SetupInitialGameState),
            new[] { typeof(bool), typeof(bool), typeof(int), typeof(int) })]
        [HarmonyPrefix]
        private static void BattleManagerBase_SetupInitialGameState_Prefix(
            BattleManagerBase __instance,
            ref int playerMaxLife,
            ref int enemyMaxLife)
        {
            if (!P2PRuntime.IsActive || !(__instance is NetworkStandardBattleMgr))
            {
                return;
            }

            int initialMaxLife = P2PRuntime.InitialMaxLife;
            playerMaxLife = initialMaxLife;
            enemyMaxLife = initialMaxLife;
            Plugin.Logger.LogInfo(
                $"[P2P] Applying initial maximum life {initialMaxLife} to both players.");
        }

        [HarmonyPatch(typeof(NetworkBattleManagerBase), nameof(NetworkBattleManagerBase.MetamorphoseCard))]
        [HarmonyPrefix]
        private static void NetworkBattleManagerBase_MetamorphoseCard_Prefix(
            NetworkBattleManagerBase __instance,
            bool isPlayer,
            int index,
            bool isFusion,
            out bool __state)
        {
            __state = false;
            if (!P2PRuntime.IsActive || isPlayer || isFusion || __instance == null)
            {
                return;
            }

            GameMgr game = GameMgr.GetIns();
            if (game == null || game.IsAdmin ||
                NetworkBattleGenericTool.GetCardPlaceState(
                    __instance.BattleEnemy,
                    index) != NetworkBattleDefine.NetworkCardPlaceState.Hand)
            {
                return;
            }

            // The server client normally restores secret opponent-hand transforms
            // immediately. P2P needs the transformed rule object on both peers.
            game.IsAdmin = true;
            __state = true;
        }

        [HarmonyPatch(typeof(NetworkBattleManagerBase), nameof(NetworkBattleManagerBase.MetamorphoseCard))]
        [HarmonyFinalizer]
        private static Exception NetworkBattleManagerBase_MetamorphoseCard_Finalizer(
            bool __state,
            Exception __exception)
        {
            if (__state)
            {
                GameMgr game = GameMgr.GetIns();
                if (game != null)
                {
                    game.IsAdmin = false;
                }
            }
            return __exception;
        }

        [HarmonyPatch(
            typeof(SendKeyActionDataManager),
            nameof(SendKeyActionDataManager.SettingKeyActionData),
            new[]
            {
                typeof(BattleCardBase),
                typeof(BattleCardBase),
                typeof(List<int>),
                typeof(bool)
            })]
        [HarmonyPostfix]
        private static void SendKeyActionDataManager_SettingKeyActionData_Postfix(
            BattleCardBase originalCard,
            BattleCardBase playCard,
            bool isEvol)
        {
            if (!P2PRuntime.IsActive || isEvol || originalCard == null ||
                playCard == null || !originalCard.IsPlayer ||
                originalCard.CardId == playCard.CardId)
            {
                return;
            }

            try
            {
                SendKeyActionDataManager.KeyActionType keyActionType;
                if (NetworkBattleGenericTool.IsAcceleratedCard(originalCard))
                {
                    keyActionType = SendKeyActionDataManager.KeyActionType.Accelerated;
                }
                else if (NetworkBattleGenericTool.IsCrystallizeCard(originalCard))
                {
                    keyActionType = SendKeyActionDataManager.KeyActionType.Crystallize;
                }
                else
                {
                    return;
                }

                P2PRuntime.RememberLocalCardMutation(
                    playCard.Index,
                    originalCard.CardId,
                    originalCard.Cost,
                    playCard.CardId,
                    playCard.Cost,
                    (int)keyActionType);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    "[P2P] Could not record an Accelerate/Crystallize card mutation: " +
                    ex.Message);
            }
        }

        [HarmonyPatch(typeof(ActionProcessor), "SwapTransformCard")]
        [HarmonyPostfix]
        private static void ActionProcessor_SwapTransformCard_Postfix(
            BattleCardBase originalCard,
            int transformCardID,
            SkillConditionCheckerOption option,
            BattleCardBase.TransformType transformType)
        {
            if (!P2PRuntime.IsActive || originalCard == null ||
                !originalCard.IsPlayer ||
                (transformType != BattleCardBase.TransformType.Accelerate &&
                    transformType != BattleCardBase.TransformType.Crystallize))
            {
                return;
            }

            try
            {
                BattleCardBase mutationCard = option?.PlayedCard;
                int mutationCardId = mutationCard?.CardId ?? transformCardID;
                Skill_pp_fixeduse fixedUseSkill =
                    NetworkBattleGenericTool.GetMutationPpFixedUseSkill(originalCard)
                        as Skill_pp_fixeduse;
                int mutationCost = fixedUseSkill?._fixedUsePP ??
                    mutationCard?.Cost ?? -1;
                int keyActionType = transformType ==
                        BattleCardBase.TransformType.Accelerate
                    ? (int)SendKeyActionDataManager.KeyActionType.Accelerated
                    : (int)SendKeyActionDataManager.KeyActionType.Crystallize;
                P2PRuntime.RememberLocalCardMutation(
                    originalCard.Index,
                    originalCard.CardId,
                    originalCard.Cost,
                    mutationCardId,
                    mutationCost,
                    keyActionType);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    "[P2P] Could not capture the created Accelerate/Crystallize card: " +
                    ex.Message);
            }
        }

        [HarmonyPatch(
            typeof(BattleCardBase),
            nameof(BattleCardBase.FusionMaterialized))]
        [HarmonyPostfix]
        private static void BattleCardBase_FusionMaterialized_Postfix(
            BattleCardBase fusionCard)
        {
            if (!P2PRuntime.IsActive || fusionCard == null)
            {
                return;
            }

            // FusionMaterialized is the native mutation point. Capturing here
            // preserves the exact cumulative N-state before metamorphose or the
            // outbound PlayActions callback can replace the card object.
            P2PRuntime.RememberLocalFusionIngredientState(fusionCard);
        }

        [HarmonyPatch(
            typeof(ActionProcessor),
            nameof(ActionProcessor.PlayCard),
            new[]
            {
                typeof(BattleCardBase),
                typeof(IEnumerable<BattleCardBase>),
                typeof(List<int>),
                typeof(bool)
            })]
        [HarmonyPrefix]
        private static void ActionProcessor_PlayCard_Prefix(
            BattleCardBase card)
        {
            P2PRuntime.CaptureLocalActionStart(card);
        }

        [HarmonyPatch(
            typeof(ActionProcessor),
            nameof(ActionProcessor.Evolution),
            new[]
            {
                typeof(BattleCardBase),
                typeof(IEnumerable<BattleCardBase>),
                typeof(List<int>)
            })]
        [HarmonyPrefix]
        private static void ActionProcessor_Evolution_Prefix(
            BattleCardBase card)
        {
            P2PRuntime.CaptureLocalActionStart(card);
        }

        [HarmonyPatch(
            typeof(ActionProcessor),
            nameof(ActionProcessor.Fusion),
            new[] { typeof(BattleCardBase), typeof(List<BattleCardBase>) })]
        [HarmonyPrefix]
        private static void ActionProcessor_Fusion_Prefix(
            BattleCardBase fusionCard)
        {
            P2PRuntime.CaptureLocalActionStart(fusionCard);
        }

        [HarmonyPatch(
            typeof(ActionProcessor),
            nameof(ActionProcessor.Attack),
            new[] { typeof(IBattleCardUniqueID), typeof(IBattleCardUniqueID) })]
        [HarmonyPrefix]
        private static void ActionProcessor_Attack_Prefix()
        {
            P2PRuntime.CaptureLocalActionStart();
        }

        [HarmonyPatch(typeof(BattleCardBase), "CopyToVirtualCardBase")]
        [HarmonyPrefix]
        private static void BattleCardBase_CopyToVirtualCardBase_Prefix(
            BattleCardBase __instance)
        {
            // Older P2P snapshots could create AttackCountInfo with a null
            // originating skill. The native clone path dereferences that skill
            // without checking it, so sanitize any state already present in a
            // running battle before damage/forecast code creates a virtual card.
            P2PRuntime.RepairInvalidAttackCountState(
                __instance, "virtual-card clone");
        }

        [HarmonyPatch(
            typeof(Wizard.Battle.Touch.PlayCardProcessor),
            nameof(Wizard.Battle.Touch.PlayCardProcessor.End))]
        [HarmonyPrefix]
        private static void PlayCardProcessor_End_Prefix(
            Wizard.Battle.Touch.PlayCardProcessor __instance)
        {
            if (!P2PRuntime.IsActive || __instance == null)
            {
                return;
            }

            try
            {
                var actCardField = AccessTools.Field(
                    typeof(Wizard.Battle.Touch.PlayCardProcessor), "_actCard");
                BattleCardBase staleCard =
                    actCardField?.GetValue(__instance) as BattleCardBase;
                List<BattleCardBase> hand =
                    staleCard?.SelfBattlePlayer?.HandCardList;
                if (staleCard == null || hand == null ||
                    hand.Contains(staleCard) || staleCard.Index <= 0)
                {
                    return;
                }

                BattleCardBase currentCard = hand.SingleOrDefault(card =>
                    card != null && card.Index == staleCard.Index);
                if (currentCard == null ||
                    !ReferenceEquals(
                        currentCard.SelfBattlePlayer,
                        staleCard.SelfBattlePlayer))
                {
                    Plugin.Logger.LogError(
                        $"[P2P] Play-card touch state lost its hand object: " +
                        $"idx={staleCard.Index}, cardId={staleCard.CardId}, " +
                        $"hand=[{string.Join(",", hand.Where(card => card != null)
                            .Select(card => $"{card.Index}:{card.CardId}"))}].");
                    return;
                }

                actCardField.SetValue(__instance, currentCard);
                Plugin.Logger.LogWarning(
                    $"[P2P] Rebound stale fusion/metamorphose play-card object: " +
                    $"idx={staleCard.Index}, oldCardId={staleCard.CardId}, " +
                    $"currentCardId={currentCard.CardId}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    "[P2P] Could not repair a stale play-card touch object: " + ex);
            }
        }

        [HarmonyPatch(typeof(BattlePlayerBase), nameof(BattlePlayerBase.TurnStart))]
        [HarmonyPrefix]
        private static void BattlePlayerBase_TurnStart_Prefix(
            BattlePlayerBase __instance)
        {
            P2PRuntime.CaptureLocalAutomaticActionStart(
                __instance, "turn-start");
        }

        [HarmonyPatch(typeof(BattlePlayerBase), nameof(BattlePlayerBase.TurnEnd))]
        [HarmonyPrefix]
        private static void BattlePlayerBase_TurnEnd_Prefix(
            BattlePlayerBase __instance)
        {
            P2PRuntime.CaptureLocalAutomaticActionStart(
                __instance, "turn-end");
        }

        [HarmonyPatch(
            typeof(NetworkBattleManagerBase),
            nameof(NetworkBattleManagerBase.ConductReceiveData))]
        [HarmonyPrefix]
        private static void NetworkBattleManagerBase_ConductReceiveData_Prefix(
            NetworkBattleReceiver.ReceiveData receiveData)
        {
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            // The receiver only starts a new operation after the previous VFX
            // queue is idle. Apply that previous operation's full history first,
            // so conditions in this operation see the authoritative counters.
            P2PRuntime.TryApplyPendingHiddenCardStates();
            P2PRuntime.TryApplyPendingPlayerHistoryStates();
            if (receiveData == null ||
                !receiveData.IsAcceleratedOrCrystallize)
            {
                return;
            }

            CardDataModel playCard = receiveData.knownCardList?
                .FirstOrDefault(card => card.Index == receiveData.playCardIndex);
            Plugin.Logger.LogInfo(
                $"[P2P] Receiving card mutation: playIdx={receiveData.playCardIndex}, " +
                $"types=[{string.Join(",", receiveData.keyActionType)}], " +
                $"originalCardId={receiveData.transformBeforeCardId}, " +
                $"mutationCardId={receiveData.mutationAfterCardId}, " +
                $"replacementCost={playCard?.playCardCost ?? -1}.");
        }

        [HarmonyPatch(
            typeof(NetworkBattleManagerBase),
            nameof(NetworkBattleManagerBase.ConductReceiveData_NotHaveSequence))]
        [HarmonyPrefix]
        private static void
            NetworkBattleManagerBase_ConductReceiveData_NotHaveSequence_Prefix()
        {
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            P2PRuntime.TryApplyPendingHiddenCardStates();
            P2PRuntime.TryApplyPendingPlayerHistoryStates();
            P2PRuntime.ApplyStagedPreNativeFusionActions();
        }

        [HarmonyPatch(
            typeof(NetworkBattleReceiver),
            nameof(NetworkBattleReceiver.ReceivedMessage))]
        [HarmonyPrefix]
        private static void NetworkBattleReceiver_ReceivedMessage_Prefix(
            NetworkBattleDefine.NetworkBattleURI uri,
            Dictionary<string, object> data)
        {
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            if (!TryRunReceiveMetadataStage(
                    "prepare metadata", uri, GetReceivePlayIndex(data),
                    () => P2PRuntime.PrepareNativeReceivedActionMetadata(
                        uri, data)))
            {
                TryRunReceiveMetadataStage(
                    "cleanup after prepare failure", uri,
                    GetReceivePlayIndex(data),
                    () => P2PRuntime.CompleteNativeReceivedActionMetadata(
                        data, false));
            }
        }

        [HarmonyPatch(
            typeof(NetworkBattleReceiver),
            nameof(NetworkBattleReceiver.ReceivedMessage))]
        [HarmonyPostfix]
        private static void NetworkBattleReceiver_ReceivedMessage_Postfix(
            NetworkBattleDefine.NetworkBattleURI uri,
            Dictionary<string, object> data,
            bool __result)
        {
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            int playIndex = GetReceivePlayIndex(data);
            bool metadataAccepted = __result;
            if (__result)
            {
                // The payload is a post-action snapshot. It must not become
                // eligible until the matching native operation was accepted.
                metadataAccepted &= TryRunReceiveMetadataStage(
                    "mark player history ready", uri, playIndex,
                    () => P2PRuntime.MarkReceivedPlayerHistoryStateReady(data));
                metadataAccepted &= TryRunReceiveMetadataStage(
                    "apply post-action fusion", uri, playIndex,
                    () => P2PRuntime.ApplyReceivedFusionAction(data, true));
                metadataAccepted &= TryRunReceiveMetadataStage(
                    "finalize hidden-card removals", uri, playIndex,
                    () => P2PRuntime.FinalizeReceivedHiddenCardRemovals(data));
            }
            TryRunReceiveMetadataStage(
                "complete metadata", uri, playIndex,
                () => P2PRuntime.CompleteNativeReceivedActionMetadata(
                    data, metadataAccepted));
        }

        [HarmonyPatch(
            typeof(NetworkBattleReceiver),
            nameof(NetworkBattleReceiver.ReceivedMessage))]
        [HarmonyFinalizer]
        private static Exception NetworkBattleReceiver_ReceivedMessage_Finalizer(
            NetworkBattleDefine.NetworkBattleURI uri,
            Dictionary<string, object> data,
            Exception __exception)
        {
            if (P2PRuntime.IsActive && __exception != null)
            {
                int playIndex = GetReceivePlayIndex(data);
                LogReceiveMetadataFailure(
                    "native ReceivedMessage", uri, playIndex, __exception);
                TryRunReceiveMetadataStage(
                    "cleanup after native failure", uri, playIndex,
                    () => P2PRuntime.CompleteNativeReceivedActionMetadata(
                        data, false));
            }
            return __exception;
        }

        [HarmonyPatch(
            typeof(NetworkBattleData),
            nameof(NetworkBattleData.BeforeSettingReceiveData))]
        [HarmonyPostfix]
        private static void NetworkBattleData_BeforeSettingReceiveData_Postfix(
            NetworkBattleData __instance)
        {
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            NetworkBattleReceiver.ReceiveData receiveData = null;
            try
            {
                receiveData = __instance?.GetReceiveData();
            }
            catch (Exception ex)
            {
                LogReceiveMetadataFailure(
                    "read converted receive data",
                    default(NetworkBattleDefine.NetworkBattleURI), -1, ex);
            }
            NetworkBattleDefine.NetworkBattleURI uri = receiveData == null
                ? default(NetworkBattleDefine.NetworkBattleURI)
                : receiveData.dataUri;
            int playIndex = receiveData?.playCardIndex ?? -1;

            // NetworkBattleData has now replaced knownList/unapproved cards.
            // Apply the source's action-start hidden history and cumulative
            // fusion state against those real objects before the operation
            // collection evaluates any private-state condition.
            TryRunReceiveMetadataStage(
                "apply pre-action hidden cards", uri, playIndex,
                P2PRuntime.TryApplyPendingHiddenCardStates);
            TryRunReceiveMetadataStage(
                "apply pre-action player history", uri, playIndex,
                P2PRuntime.ApplyPendingPreActionPlayerHistoryState);
            TryRunReceiveMetadataStage(
                "apply pre-action fusion", uri, playIndex,
                P2PRuntime.ApplyStagedPreNativeFusionActions);
        }

        private static bool TryRunReceiveMetadataStage(
            string stage,
            NetworkBattleDefine.NetworkBattleURI uri,
            int playIndex,
            Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                LogReceiveMetadataFailure(stage, uri, playIndex, ex);
                return false;
            }
        }

        private static void LogReceiveMetadataFailure(
            string stage,
            NetworkBattleDefine.NetworkBattleURI uri,
            int playIndex,
            Exception exception)
        {
            Plugin.Logger.LogError(
                $"[P2P] Receive stage failed: stage={stage}, uri={uri}, " +
                $"playIdx={playIndex}. {exception}");
        }

        private static int GetReceivePlayIndex(
            Dictionary<string, object> data)
        {
            if (data == null || !data.TryGetValue("playIdx", out object rawIndex))
            {
                return -1;
            }
            try
            {
                return Convert.ToInt32(rawIndex);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [HarmonyPatch(typeof(ReplaceReceivedCard), "InheritedCardData")]
        [HarmonyPostfix]
        private static void ReplaceReceivedCard_InheritedCardData_Postfix(
            BattleCardBase receivedCard)
        {
            if (!P2PRuntime.IsActive || receivedCard == null)
            {
                return;
            }

            // The native CardDataModel cannot carry generic skill values,
            // fusion turns, or Super Skybound Art. Apply the P2P snapshot after
            // the native replacement has copied all public fields and attached
            // skills, so the resulting card is authoritative before it enters
            // the hand/deck list.
            P2PRuntime.ApplyReceivedHiddenCardState(receivedCard, true);
        }

        [HarmonyPatch(
            typeof(ReplaceReceivedCard),
            "SearchForDummyCardInHandAndDeck")]
        [HarmonyPrefix]
        private static void
            ReplaceReceivedCard_SearchForDummyCardInHandAndDeck_Prefix(
                ReplaceReceivedCard __instance,
                BattlePlayerBase battlePlayer)
        {
            if (!P2PRuntime.IsActive || __instance == null || battlePlayer == null)
            {
                return;
            }

            P2PRuntime.RepairDuplicateReceivedCardZoneIndices(
                battlePlayer, __instance.CardIdx, __instance.CardId);
        }

        [HarmonyPatch(
            typeof(NetworkExecutionInfoCreator),
            nameof(NetworkExecutionInfoCreator.FixedSkillApplyTarget))]
        [HarmonyPostfix]
        private static void
            NetworkExecutionInfoCreator_FixedSkillApplyTarget_Postfix(
                NetworkExecutionInfoCreator __instance,
                ref Wizard.Battle.View.Vfx.VfxWith<
                    List<BattleCardBase>, Dictionary<int, BattleCardBase>> __result)
        {
            if (!P2PRuntime.IsActive || __instance == null || __result == null)
            {
                return;
            }

            try
            {
                SkillBase skill = AccessTools.Field(
                        typeof(ExecutionInfoCreatorBase), "_skill")?
                    .GetValue(__instance) as SkillBase;
                P2PRuntime.SynchronizeAuthoritativeRandomSkillTargets(
                    skill, ref __result);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    "[P2P] Could not synchronize authoritative random targets: " +
                    ex.Message);
            }
        }

        [HarmonyPatch(typeof(SkillBase), nameof(SkillBase.CallStart))]
        [HarmonyPrefix]
        private static void SkillBase_CallStart_AuthoritativeEvaluation_Prefix(
            SkillBase __instance,
            ref P2PRuntime.AuthoritativeSkillEvaluationScope __state)
        {
            __state = P2PRuntime.BeginAuthoritativeSkillEvaluation(__instance);
        }

        [HarmonyPatch(typeof(SkillBase), nameof(SkillBase.CallStart))]
        [HarmonyFinalizer]
        private static Exception SkillBase_CallStart_AuthoritativeEvaluation_Finalizer(
            P2PRuntime.AuthoritativeSkillEvaluationScope __state,
            Exception __exception)
        {
            P2PRuntime.CompleteAuthoritativeSkillEvaluation(
                __state, __exception == null);
            return __exception;
        }

        [HarmonyPatch(
            typeof(SkillOptionValue),
            nameof(SkillOptionValue.GetInt),
            new[]
            {
                typeof(SkillFilterCreator.ContentKeyword),
                typeof(int?),
                typeof(bool)
            })]
        [HarmonyPostfix]
        private static void SkillOptionValue_GetInt_AuthoritativeEvaluation_Postfix(
            SkillOptionValue __instance,
            SkillFilterCreator.ContentKeyword nameType,
            ref int __result)
        {
            // Let the native method consume one-shot replacement data first,
            // then make the acting peer's resolved value authoritative.
            if (P2PRuntime.TryGetAuthoritativeSkillOptionValue(
                    __instance, nameType, out int value))
            {
                __result = value;
            }
            P2PRuntime.ObserveAuthoritativeSkillOptionValue(
                __instance, nameType, __result);
        }

        [HarmonyPatch(
            typeof(NetworkSkillPreprocessConditionCheck),
            nameof(NetworkSkillPreprocessConditionCheck.IsRight),
            new[]
            {
                typeof(BattlePlayerReadOnlyInfoPair),
                typeof(SkillConditionCheckerOption),
                typeof(bool)
            })]
        [HarmonyPostfix]
        private static void
            NetworkSkillPreprocessConditionCheck_IsRight_AuthoritativeEvaluation_Postfix(
                bool preexecutionCheck,
                ref bool __result)
        {
            // Preserve the native event registration/state updates, but do not
            // trust its hidden-zone result on the receiving peer.
            if (P2PRuntime.TryGetAuthoritativePreprocessResult(
                    preexecutionCheck, out bool result))
            {
                __result = result;
            }
            P2PRuntime.ObserveAuthoritativePreprocessResult(
                preexecutionCheck, __result);
        }

        [HarmonyPatch(
            typeof(NetworkExecutionInfoCreator),
            nameof(NetworkExecutionInfoCreator.CheckCondition),
            new[]
            {
                typeof(BattlePlayerReadOnlyInfoPair),
                typeof(SkillConditionCheckerOption),
                typeof(bool),
                typeof(bool)
            })]
        [HarmonyPostfix]
        private static void NetworkExecutionInfoCreator_CheckCondition_Postfix(
            NetworkExecutionInfoCreator __instance,
            BattlePlayerReadOnlyInfoPair playerInfoPair,
            SkillConditionCheckerOption option,
            bool isPrePlay,
            bool isSkipTarget,
            ref bool __result)
        {
            if (!P2PRuntime.IsActive || __instance == null)
            {
                return;
            }

            try
            {
                SkillBase skill = AccessTools.Field(
                        typeof(ExecutionInfoCreatorBase), "_skill")?
                    .GetValue(__instance) as SkillBase;
                if (skill?.SkillPrm?.ownerCard == null)
                {
                    return;
                }
                if (skill.SkillPrm.ownerCard.IsPlayer ||
                    !UsesPrivateCardInformation(skill))
                {
                    return;
                }

                // The official server supplies activate/count/highlander results
                // for private zones. P2P deliberately shares those zones, so the
                // peer can evaluate the original condition against the complete
                // synchronized hand/deck instead of waiting for a server-only flag.
                P2PRuntime.TryApplyPendingHiddenCardStates();
                P2PRuntime.TryApplyPendingPlayerHistoryStates();
                P2PRuntime.WarnIfPrivateConditionHasDummyCards(skill);
                bool localResult = skill.ConditionFilterCollection.Filtering(
                    playerInfoPair,
                    skill.SkillPrm.ownerCard,
                    option,
                    skill.OptionValue,
                    isPrePlay,
                    skill,
                    isSkipTarget);
                if (__result != localResult)
                {
                    Plugin.Logger.LogDebug(
                        $"[P2P] Replaced server-only private condition result for " +
                        $"card idx={skill.SkillPrm.ownerCard.Index}, " +
                        $"skill={skill.GetType().Name}: received={__result}, " +
                        $"local={localResult}.");
                }
                __result = localResult;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug(
                    "[P2P] Could not evaluate a private hand/deck condition locally: " +
                    ex.Message);
            }
        }

        private static bool UsesPrivateCardInformation(SkillBase skill)
        {
            return RegisterSkillConditionCheck.IsSkillConditionCheck(skill) ||
                RegisterSkillConditionCheck.IsPreprocessConditionCheck(skill) ||
                RegisterSkillConditionCheck.DoesSkillUsePrivateCount(
                    skill, false, false) ||
                RegisterSkillConditionCheck.IsHighlander(
                    skill.ConditionFilterCollection) ||
                RegisterSkillConditionCheck.IsHighlanderPreprocessConditionCheck(
                    skill) ||
                skill.PreprocessList.Any(preprocess =>
                    preprocess is NetworkSkillPreprocessConditionCheck);
        }

        [HarmonyPatch(typeof(ActionProcessor), "SetSkillConditionCheckeroptionSelectCards")]
        [HarmonyPrefix]
        private static void ActionProcessor_SetSkillConditionCheckeroptionSelectCards_Prefix(
            BattleCardBase card)
        {
            if (!P2PRuntime.IsActive || card == null || card.IsPlayer)
            {
                return;
            }

            NetworkBattleManagerBase battleManager =
                BattleManagerBase.GetIns() as NetworkBattleManagerBase;
            NetworkBattleReceiver.ReceiveData receiveData =
                battleManager?.networkBattleData?.GetReceiveData();
            if (receiveData == null || !receiveData.IsAcceleratedOrCrystallize ||
                receiveData.playCardIndex != card.Index)
            {
                return;
            }

            Skill_pp_fixeduse fixedUse =
                NetworkBattleGenericTool.GetMutationPpFixedUseSkill(card)
                    as Skill_pp_fixeduse;
            Skill_transform transform = card.GetAccelerateOrCrystallizeTransformSkill();
            Plugin.Logger.LogInfo(
                $"[P2P] Executing received card mutation: playIdx={card.Index}, " +
                $"cardId={card.CardId}, cost={card.Cost}, pp={card.SelfBattlePlayer.Pp}, " +
                $"fixedCost={fixedUse?._fixedUsePP ?? -1}, " +
                $"transformId={transform?.TransformId ?? -1}.");
        }

        [HarmonyPatch(
            typeof(RealTimeNetworkAgent),
            nameof(RealTimeNetworkAgent.Connect),
            new[] { typeof(int), typeof(string), typeof(Action), typeof(Matching), typeof(bool) })]
        [HarmonyPrefix]
        private static bool RealTimeNetworkAgent_Connect_Prefix(
            RealTimeNetworkAgent __instance,
            int viewerId,
            string battleId,
            Action onEveryTimeFail,
            Matching matching)
        {
            if (!P2PRuntime.IsActive)
            {
                return true;
            }
            PrepareAgent(__instance, viewerId, battleId, onEveryTimeFail, matching);
            return false;
        }

        [HarmonyPatch(
            typeof(RealTimeNetworkAgent),
            nameof(RealTimeNetworkAgent.Connect),
            new[] { typeof(int), typeof(string), typeof(Action) })]
        [HarmonyPrefix]
        private static bool RealTimeNetworkAgent_ConnectSimple_Prefix(
            RealTimeNetworkAgent __instance,
            int viewerId,
            string battleId,
            Action onEveryTimeFail,
            ref SocketManager.States __result)
        {
            if (!P2PRuntime.IsActive)
            {
                return true;
            }
            PrepareAgent(__instance, viewerId, battleId, onEveryTimeFail, null);
            __result = SocketManager.States.Open;
            return false;
        }

        [HarmonyPatch(typeof(RealTimeNetworkAgent), nameof(RealTimeNetworkAgent.IsOpen))]
        [HarmonyPostfix]
        private static void RealTimeNetworkAgent_IsOpen_Postfix(
            RealTimeNetworkAgent __instance,
            ref bool __result)
        {
            if (P2PRuntime.IsActive && P2PRuntime.IsCurrentAgent(__instance))
            {
                __result = true;
            }
        }

        [HarmonyPatch(typeof(RealTimeNetworkAgent), nameof(RealTimeNetworkAgent.IsInitNetworkSuccess))]
        [HarmonyPostfix]
        private static void RealTimeNetworkAgent_IsInitNetworkSuccess_Postfix(
            RealTimeNetworkAgent __instance,
            ref bool __result)
        {
            if (P2PRuntime.IsActive && P2PRuntime.IsCurrentAgent(__instance))
            {
                __result = true;
            }
        }

        [HarmonyPatch(typeof(RealTimeNetworkAgent), nameof(RealTimeNetworkAgent.SetNetworkInfo))]
        [HarmonyPostfix]
        private static void RealTimeNetworkAgent_SetNetworkInfo_Postfix(
            Dictionary<string, object> synchronizeData)
        {
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            // SetNetworkInfo has already populated oppoInfo here. Installing the
            // identity table at this point is early enough for Matching.StartBattleLoad
            // and avoids changing the original server message handling path.
            P2PRuntime.ApplyOpponentDeckIdentity(synchronizeData);
        }

        [HarmonyPatch(
            typeof(RealTimeNetworkAgent),
            nameof(RealTimeNetworkAgent.EmitMsgPack),
            new[]
            {
                typeof(string),
                typeof(RealTimeNetworkAgent.EmitCategory),
                typeof(Dictionary<string, object>),
                typeof(Action),
                typeof(bool),
                typeof(bool),
                typeof(int)
            })]
        [HarmonyPrefix]
        private static bool RealTimeNetworkAgent_EmitMsgPack_Prefix(
            RealTimeNetworkAgent __instance,
            string uri,
            RealTimeNetworkAgent.EmitCategory emitCategory,
            ref Dictionary<string, object> info,
            Action onFinishedSend,
            bool isGetableAck,
            bool isStockData,
            int fixedSeqNumber)
        {
            if (!P2PRuntime.IsActive)
            {
                return true;
            }

            P2PRuntime.SetCurrentAgent(__instance, "EmitMsgPack");
            if (info == null)
            {
                info = new Dictionary<string, object>();
            }
            info["cat"] = (int)emitCategory;
            P2PRuntime.QueueAcknowledgement(
                __instance,
                uri,
                info,
                onFinishedSend,
                isGetableAck,
                isStockData,
                fixedSeqNumber);
            P2PRuntime.HandleEmit(uri, info);
            return false;
        }

        [HarmonyPatch(typeof(RealTimeNetworkAgent), nameof(RealTimeNetworkAgent.EmitHandData))]
        [HarmonyPrefix]
        private static bool RealTimeNetworkAgent_EmitHandData_Prefix(
            RealTimeNetworkAgent __instance,
            List<object> parameters,
            NetworkBattleSender.HAND_URI_TYPE uri)
        {
            if (!P2PRuntime.IsActive)
            {
                return true;
            }

            P2PRuntime.SetCurrentAgent(__instance, "EmitHandData");
            P2PRuntime.HandleHandData(__instance, parameters, uri);
            return false;
        }

        [HarmonyPatch(typeof(RealTimeNetworkAgent), nameof(RealTimeNetworkAgent.StartGungnir))]
        [HarmonyPrefix]
        private static bool RealTimeNetworkAgent_StartGungnir_Prefix()
        {
            return !P2PRuntime.IsActive;
        }

        [HarmonyPatch(typeof(OperateReceiveChecker), nameof(OperateReceiveChecker.IsOperateReceive))]
        [HarmonyPrefix]
        private static bool OperateReceiveChecker_IsOperateReceive_Prefix(ref bool __result)
        {
            if (!P2PRuntime.IsActive)
            {
                return true;
            }

            __result = true;
            return false;
        }

        private static void PrepareAgent(
            RealTimeNetworkAgent agent,
            int viewerId,
            string battleId,
            Action onEveryTimeFail,
            Matching matching)
        {
            agent.SettingMatchingClass(matching);
            agent.viewerId = viewerId;
            agent.battleRoomId = battleId;
            agent._onEveryTimeFail = onEveryTimeFail;
            agent._initNetworkSuccess = true;
            agent.InitCurrentMatchingStatus();
            agent.SetCurrentMatchingStatus(RealTimeNetworkAgent.MatchingStatus.Connect);
            agent.PlayerNetworkStatus?.ToKeepAlive();
            agent.OpponentNetworkStatus?.ToKeepAlive();
            P2PRuntime.SetCurrentAgent(agent, "Connect");
        }
    }
}
