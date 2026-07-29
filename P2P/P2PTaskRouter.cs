using Cute;
using LitJson;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wizard;
using Wizard.RoomMatch;

namespace Shadowbus
{
    internal static class P2PTaskRouter
    {
        private const float JoinTimeoutSeconds = 15f;

        private static readonly HashSet<string> SimpleRoomTaskNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "OpenRoomBattleChooseDeckTask",
                "OpenRoomBattleForceCloseRoomTask",
                "OpenRoomBattleForceKickRoomTask",
                "OpenRoomBattleKickRoomTask",
                "OpenRoomInitializeOwnerRoomRelationsTask"
            };

        internal static bool CanHandle(NetworkTask task)
        {
            if (task == null)
            {
                return false;
            }
            if (task is OpenRoomBattleCreateRoomTask)
            {
                return true;
            }
            if (task is OpenRoomBattleEnterRoomTask enterTask)
            {
                OpenRoomBattleEnterRoomTask.OpenRoomBattleEnterRoomTaskParam parameters =
                    enterTask.Params as OpenRoomBattleEnterRoomTask.OpenRoomBattleEnterRoomTaskParam;
                return parameters != null &&
                    parameters.room_id != null &&
                    parameters.room_id.StartsWith(
                        P2PConnectionCode.Prefix,
                        StringComparison.OrdinalIgnoreCase);
            }
            if (!P2PRuntime.IsActive)
            {
                return false;
            }

            return task is OpenRoomBattleSetDeckTask ||
                task is OpenRoomInitilizeRoomBattle ||
                task is RoomBattleDoMatchingTask ||
                task is RoomBattleFinishTask ||
                task is OpenRoomBattleCloseRoomTask ||
                task is OpenRoomBattleLeaveRoomTask ||
                task is OpenRoomBattleResetHistoryTask ||
                task is OpenRoomBattleGetHistoryTask ||
                SimpleRoomTaskNames.Contains(task.GetType().Name);
        }

        internal static IEnumerator Process(NetworkManager manager, NetworkTask task)
        {
            while (manager.isConnect)
            {
                yield return null;
            }

            manager.isConnect = true;
            manager.isTimeOut = false;
            manager.isError = false;

            bool joinStarted = false;
            Exception startError = null;
            try
            {
                if (task is OpenRoomBattleCreateRoomTask createTask)
                {
                    StartHost(createTask);
                }
                else if (task is OpenRoomBattleEnterRoomTask enterTask)
                {
                    StartJoin(enterTask);
                    joinStarted = true;
                }
            }
            catch (Exception ex)
            {
                startError = ex;
            }

            if (joinStarted && startError == null)
            {
                float deadline = Time.realtimeSinceStartup + JoinTimeoutSeconds;
                while (!P2PRuntime.JoinFinished && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                if (!P2PRuntime.JoinFinished)
                {
                    P2PRuntime.FailJoin("Timed out while connecting to the room host.");
                }
            }

            Exception processError = startError;
            try
            {
                if (processError == null)
                {
                    JsonData response = CreateResponse(task);
                    task.SetResponseData(response);
                    task.CheckResultCodeToPopupCreate_ReturnStatus(0);
                    if (task is OpenRoomBattleEnterRoomTask && !P2PRuntime.JoinSucceeded)
                    {
                        P2PRuntime.AbortFailedJoin();
                    }
                    Plugin.Logger.LogInfo(
                        $"[P2P] Completed local room task: {task.GetType().Name}.");
                }
            }
            catch (Exception ex)
            {
                processError = ex;
            }

            if (processError != null)
            {
                manager.isError = true;
                Plugin.Logger.LogError(
                    $"[P2P] Failed room task {task.GetType().Name}: {processError}");
                task.CallbackOnFailure?.Invoke(NetworkTask.ResultCode.Error);
                if (task is OpenRoomBattleCreateRoomTask)
                {
                    P2PRuntime.Shutdown();
                }
            }

            if (manager.NetworkUI != null)
            {
                manager.NetworkUI.StopLoading();
            }
            manager.ClearLastRequestTask();
            manager.isConnect = false;
        }

        private static void StartHost(OpenRoomBattleCreateRoomTask task)
        {
            OpenRoomBattleCreateRoomTask.OpenRoomBattleCreateRoomTaskParam parameters =
                task.Params as OpenRoomBattleCreateRoomTask.OpenRoomBattleCreateRoomTaskParam;
            if (parameters == null)
            {
                throw new InvalidOperationException("The create-room parameters are unavailable.");
            }
            if (parameters.battle_rule != (int)RoomConnectController.BattleRule.Bo1)
            {
                throw new NotSupportedException("P2P rooms currently support BO1 only.");
            }
            if (parameters.battle_type != (int)NetworkDefine.ServerBattleType.OpenRoom ||
                parameters.two_pick_type != (int)TwoPickFormat.None)
            {
                throw new NotSupportedException(
                    "P2P rooms currently support normal constructed Open Room battles only.");
            }
            Format deckFormat = Data.ParseApiFormat(parameters.deck_format);
            if (deckFormat != Format.Unlimited)
            {
                throw new NotSupportedException(
                    $"P2P rooms currently support Unlimited format only " +
                    $"(received api={parameters.deck_format}, format={deckFormat}).");
            }

            P2PRuntime.StartHosting(new P2PRoomRules
            {
                BattleType = parameters.battle_type,
                DeckFormat = parameters.deck_format,
                CustomFormatId = CustomFormatContext.RoomFormatId,
                TwoPickType = parameters.two_pick_type,
                BattleRule = parameters.battle_rule,
                InitialMaxLife = P2PRoomRules.DefaultInitialMaxLife,
                IsDeckOpen = task._room != null &&
                    task._room.BattleParameterInstance != null &&
                    task._room.BattleParameterInstance.IsOpenDeckRoom
            });
        }

        private static void StartJoin(OpenRoomBattleEnterRoomTask task)
        {
            OpenRoomBattleEnterRoomTask.OpenRoomBattleEnterRoomTaskParam parameters =
                task.Params as OpenRoomBattleEnterRoomTask.OpenRoomBattleEnterRoomTaskParam;
            if (parameters == null ||
                !P2PConnectionCode.TryDecode(parameters.room_id, out P2PConnectionInfo info))
            {
                throw new FormatException("The P2P room password is invalid.");
            }
            P2PRuntime.BeginJoin(info);
        }

        private static JsonData CreateResponse(NetworkTask task)
        {
            Dictionary<string, object> data;
            if (task is OpenRoomBattleCreateRoomTask)
            {
                data = new Dictionary<string, object>
                {
                    ["battle_id"] = P2PRuntime.BattleId,
                    ["room_id"] = P2PRuntime.RoomId,
                    ["display_room_id"] = "P2P",
                    ["is_invitation_user"] = false,
                    ["is_enabled_all_card"] = true,
                    ["node_server_url"] = "p2p://local"
                };
            }
            else if (task is OpenRoomBattleEnterRoomTask)
            {
                data = CreateEnterRoomData();
                if (P2PRuntime.JoinSucceeded && RoomBase.ConnectController != null)
                {
                    RoomBase.ConnectController.RoomID = P2PRuntime.RoomId;
                    RoomBase.ConnectController.DisplayRoomID = "P2P";
                }
            }
            else if (task is OpenRoomBattleSetDeckTask)
            {
                CaptureSelectedDeck();
                data = new Dictionary<string, object>();
            }
            else if (task is OpenRoomInitilizeRoomBattle)
            {
                data = new Dictionary<string, object>
                {
                    ["battle_id"] = P2PRuntime.BattleId,
                    ["my_battle_result"] = new Dictionary<string, object>(),
                    ["opponent_battle_result"] = new Dictionary<string, object>(),
                    ["used_deck"] = 0,
                    ["is_settled"] = 0
                };
            }
            else if (task is RoomBattleDoMatchingTask)
            {
                CaptureSelectedDeck();
                if (P2PRuntime.LocalDeck == null)
                {
                    throw new InvalidOperationException("No P2P room deck has been selected.");
                }
                data = new Dictionary<string, object>
                {
                    ["matching_state"] = P2PRuntime.Role == P2PRole.Host ? 3007 : 3004,
                    ["battle_state"] = 0,
                    ["battle_id"] = P2PRuntime.BattleId,
                    ["card_master_id"] = 1
                };
            }
            else if (task is RoomBattleFinishTask finishTask)
            {
                BattleFinishParam parameters = finishTask.Params as BattleFinishParam;
                data = new Dictionary<string, object>
                {
                    ["battle_result"] = parameters?.battle_result ?? 0,
                    ["class_level"] = 0,
                    ["class_experience"] = 0,
                    ["get_class_experience"] = 0
                };
            }
            else if (task is OpenRoomBattleLeaveRoomTask)
            {
                data = new Dictionary<string, object>
                {
                    ["result_reason"] = 9,
                    ["room_result"] = 1
                };
                P2PRuntime.LeaveRoom("The other player left the room.");
            }
            else if (task is OpenRoomBattleGetHistoryTask)
            {
                data = new Dictionary<string, object>
                {
                    ["my_battle_result"] = new Dictionary<string, object>(),
                    ["opponent_battle_result"] = new Dictionary<string, object>(),
                    ["owner_available_deck"] = 1
                };
            }
            else
            {
                data = new Dictionary<string, object>();
                string taskName = task.GetType().Name;
                if (task is OpenRoomBattleKickRoomTask ||
                    taskName == "OpenRoomBattleForceKickRoomTask" ||
                    taskName == "OpenRoomBattleForceCloseRoomTask")
                {
                    data["room_result"] = 1;
                }
                if (task is OpenRoomBattleCloseRoomTask ||
                    taskName == "OpenRoomBattleForceCloseRoomTask")
                {
                    P2PRuntime.LeaveRoom("The host closed the room.");
                }
            }

            return Wrap(data);
        }

        private static Dictionary<string, object> CreateEnterRoomData()
        {
            if (!P2PRuntime.JoinSucceeded || P2PRuntime.RemoteProfile == null)
            {
                return new Dictionary<string, object>
                {
                    ["result_reason"] = (int)RoomConnectController.ConnectRoomResult.CONNECT_ERROR,
                    ["battle_id"] = string.Empty
                };
            }

            P2PProfile host = P2PRuntime.RemoteProfile;
            P2PRoomRules rules = P2PRuntime.Rules ?? new P2PRoomRules();
            return new Dictionary<string, object>
            {
                ["result_reason"] = 0,
                ["battle_id"] = P2PRuntime.BattleId,
                ["is_friend"] = 0,
                ["guild_id"] = 0,
                ["oppo_guild_id"] = 0,
                ["oppo_info"] = new Dictionary<string, object>
                {
                    ["oppoId"] = host.ViewerId,
                    ["battlePoint"] = host.BattlePoint,
                    ["degreeId"] = host.DegreeId,
                    ["emblemId"] = host.EmblemId,
                    ["country_code"] = host.CountryCode ?? string.Empty,
                    ["rank"] = host.Rank,
                    ["max_rank"] = host.Rank,
                    ["userName"] = host.UserName ?? "Player",
                    ["isOfficial"] = host.IsOfficial ? 1 : 0
                },
                ["battle_type"] = rules.BattleType,
                ["deck_format"] = rules.DeckFormat,
                ["two_pick_type"] = rules.TwoPickType,
                ["battle_rule"] = rules.BattleRule,
                ["is_deck_confirmable"] = rules.IsDeckOpen ? 1 : 0,
                ["is_invitation_user"] = false,
                ["is_enabled_all_card"] = true,
                ["node_server_url"] = "p2p://local"
            };
        }

        private static void CaptureSelectedDeck()
        {
            DeckData selected = RoomBase.ConnectController?.OwnCtrl?.Target?.SelectedDeck;
            if (selected != null)
            {
                P2PRuntime.SetLocalDeck(selected);
            }
        }

        private static JsonData Wrap(Dictionary<string, object> data)
        {
            Dictionary<string, object> response = new Dictionary<string, object>
            {
                ["data_headers"] = new Dictionary<string, object>
                {
                    ["short_udid"] = 0,
                    ["viewer_id"] = P2PIdentity.ViewerId,
                    ["sid"] = string.Empty,
                    ["servertime"] = (long)TimeNativePlugin.GetDeviceOperatingTime(),
                    ["result_code"] = 1
                },
                ["data"] = data ?? new Dictionary<string, object>()
            };
            return JsonMapper.ToObject(JsonConvert.SerializeObject(response));
        }
    }
}
