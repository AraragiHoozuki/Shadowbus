using BestHTTP.SocketIO;
using Convention;
using Cute;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Wizard;
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
