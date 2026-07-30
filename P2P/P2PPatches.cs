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
            if (!isTwoPick && !isSelectBaseRule)
            {
                ApplyFixedP2PRoomRule(setting);
            }
        }

        [HarmonyPatch(typeof(RoomRuleSelectDialog), "Start")]
        [HarmonyPostfix]
        private static void RoomRuleSelectDialog_Start_Postfix(
            RoomRuleSelectDialog __instance)
        {
            if (__instance == null || __instance._is2pick ||
                RoomRuleSelectDialog._isSelectBaseRule)
            {
                return;
            }

            ApplyFixedP2PRoomRule(__instance._setting);
            __instance._normalRuleLabel.text = RoomRuleSetting.GetWinTypeString(
                RoomConnectController.BattleRule.Bo1);
            __instance._formatLabel.text = CustomFormatContext.RoomFormat.DisplayName;
            DisableRuleChangeButton(__instance._normalRuleChangeButton);
            EnableRuleChangeButton(__instance._formatChangeButton);
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
            if (__instance != null && !__instance._is2pick &&
                !RoomRuleSelectDialog._isSelectBaseRule)
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

        [HarmonyPatch(
            typeof(BattleManagerBase),
            nameof(BattleManagerBase.SetupInitialGameState),
            new[] { typeof(bool), typeof(bool), typeof(int), typeof(int) })]
        [HarmonyPrefix]
        private static void BattleManagerBase_SetupInitialGameState_Prefix(
            ref int playerMaxLife,
            ref int enemyMaxLife)
        {
            if (!P2PRuntime.IsActive)
            {
                return;
            }

            int initialMaxLife = P2PRuntime.InitialMaxLife;
            playerMaxLife = initialMaxLife;
            enemyMaxLife = initialMaxLife;
            Plugin.Logger.LogInfo(
                $"[P2P] Applying initial maximum life {initialMaxLife} to both players.");
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
            typeof(NetworkBattleManagerBase),
            nameof(NetworkBattleManagerBase.ConductReceiveData))]
        [HarmonyPrefix]
        private static void NetworkBattleManagerBase_ConductReceiveData_Prefix(
            NetworkBattleReceiver.ReceiveData receiveData)
        {
            if (!P2PRuntime.IsActive || receiveData == null ||
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
