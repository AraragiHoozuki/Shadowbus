using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Globalization;
using Newtonsoft.Json;
using Wizard;
using Wizard.RoomMatch;

namespace Shadowbus
{
    internal static class P2PRuntime
    {
        private const int BattleStateCheckTimeoutSeconds = 15;
        private const int MaxDeferredAgentDeliveries = 128;

        private static readonly ConcurrentQueue<Action> MainThreadActions =
            new ConcurrentQueue<Action>();
        private static readonly P2PRoomRoundState RoomRoundState =
            new P2PRoomRoundState();
        private static readonly P2PDealState DealState = new P2PDealState();
        private static readonly P2PDeliverySequence GuestDeliverySequence =
            new P2PDeliverySequence();
        private static readonly Queue<Dictionary<string, object>> DeferredGuestDeliveries =
            new Queue<Dictionary<string, object>>();
        private static readonly Queue<Dictionary<string, object>> DeferredAgentDeliveries =
            new Queue<Dictionary<string, object>>();
        private static readonly P2PBattleSelectionTracker BattleSelectionTracker =
            new P2PBattleSelectionTracker();
        private static readonly Queue<PendingBattleStateCheck> PendingBattleStateChecks =
            new Queue<PendingBattleStateCheck>();

        private static P2PTransport transport;
        private static string bindAddress = "0.0.0.0";
        private static string advertisedAddress = string.Empty;
        private static int configuredPort = 29600;
        private static RealTimeNetworkAgent currentAgent;
        private static int hostPlaySequence;
        private static int guestPlaySequence;
        private static bool hostInitBattle;
        private static bool guestInitBattle;
        private static bool matchedSent;
        private static bool hostLoaded;
        private static bool guestLoaded;
        private static bool battleStartSent;
        private static bool battleStartReceived;
        private static bool mulliganReadyReceived;
        private static bool finishResultSent;
        private static bool? retiringHost;
        private static bool localRetired;
        private static List<int> shuffledHostDeck;
        private static List<int> shuffledGuestDeck;
        private static readonly P2PBattleCardTracker BattleCardTracker =
            new P2PBattleCardTracker();
        // The native server only sends private card state when a card is involved
        // in a particular register action.  In P2P both clients execute the same
        // effects locally, so keep a compact per-index snapshot and publish every
        // hidden-zone state change.  The receiver feeds these entries through the
        // original knownList/ReplaceReceivedCard path.
        private static readonly Dictionary<int, string> LocalHiddenCardStateSignatures =
            new Dictionary<int, string>();
        // Keep the latest pre-action state separately from the last state sent to
        // the peer. A played/discarded card is no longer in a private zone when
        // EmitMsg runs, so without this cache its hand/deck-only state is lost at
        // exactly the point where the receiver replaces the hidden card.
        private static readonly Dictionary<int, Dictionary<string, object>>
            LocalHiddenCardStates =
            new Dictionary<int, Dictionary<string, object>>();
        // Hidden card snapshots use a P2P-only side channel because the native
        // CardDataModel has no fields for generic skill values, fusion turns, or
        // Super Skybound Art. Keep the latest complete state per owner/index so
        // a later native card replacement can apply it deterministically.
        private static readonly Dictionary<string, Dictionary<string, object>>
            ReceivedHiddenCardStates =
            new Dictionary<string, Dictionary<string, object>>();
        private static readonly Dictionary<string, string>
            ReceivedHiddenCardStateSignatures =
            new Dictionary<string, string>();
        private static readonly Dictionary<string, AppliedHiddenCardState>
            AppliedReceivedHiddenCardStates =
            new Dictionary<string, AppliedHiddenCardState>();
        private static readonly HashSet<string> PrivateConditionWarnings =
            new HashSet<string>(StringComparer.Ordinal);
        // Both clients are trusted in a friends-only room. Exchange the complete
        // private-zone baseline once so ordinary actions do not carry the same
        // hand/deck state over and over again.
        private static bool localPrivateStateSent;
        private static bool remotePrivateStateReceived;
        private static readonly Queue<Dictionary<string, object>> PendingFusionActions =
            new Queue<Dictionary<string, object>>();
        private static readonly HashSet<string> PendingFusionActionSignatures =
            new HashSet<string>(StringComparer.Ordinal);
        private const string PlayerHistoryStateKey = "p2pPlayerHistory";
        private static readonly Dictionary<string, PendingPlayerHistoryState>
            ReceivedPlayerHistoryStates =
            new Dictionary<string, PendingPlayerHistoryState>();
        private static readonly Dictionary<int, int> AppliedPlayerHistoryRevisions =
            new Dictionary<int, int>();
        private static string localPlayerHistoryStateSignature = string.Empty;
        private static int localPlayerHistoryRevision;
        private static List<int> hostMulliganHand;
        private static List<int> guestMulliganHand;
        private static bool hostSwapped;
        private static bool guestSwapped;
        private static bool mulliganReadySent;
        private static int battleSeed;
        private static bool hostFirst;
        private static int localEmitSequence = 1;
        private static bool peerDisconnected;
        private static bool roomReleaseInjected;
        private static bool pendingOpponentSync;
        private static bool applyingReceivedHiddenCardStates;
        private static bool applyingReceivedPlayerHistoryStates;
        private static Dictionary<string, object> hostDeckEntry;
        private static Dictionary<string, object> guestDeckEntry;
        private static int sessionGeneration;

        internal static P2PRole Role { get; private set; }
        internal static bool IsActive { get; private set; }
        internal static string ConnectionCode { get; private set; }
        internal static string RoomId { get; private set; }
        internal static string BattleId { get; private set; }
        internal static P2PProfile LocalProfile { get; private set; }
        internal static P2PProfile RemoteProfile { get; private set; }
        internal static P2PDeckSnapshot LocalDeck { get; private set; }
        internal static P2PDeckSnapshot RemoteDeck { get; private set; }
        internal static P2PRoomRules Rules { get; private set; }
        internal static int InitialMaxLife =>
            Rules?.InitialMaxLife ?? P2PRoomRules.DefaultInitialMaxLife;
        internal static bool IsTwoPickRoom => IsActive &&
            Rules?.TwoPickType == (int)TwoPickFormat.Normal &&
            Rules?.BattleType == (int)NetworkDefine.ServerBattleType.RoomTwoPick;
        internal static bool CanEditRoomRules =>
            IsActive &&
            Role == P2PRole.Host &&
            !RoomRoundState.HostReady &&
            !RoomRoundState.GuestReady &&
            !RoomRoundState.ReadySent;
        internal static bool JoinFinished { get; private set; }
        internal static bool JoinSucceeded { get; private set; }
        internal static string LastError { get; private set; }

        internal static void Configure(string bind, string advertised, int port)
        {
            bindAddress = string.IsNullOrWhiteSpace(bind) ? "0.0.0.0" : bind.Trim();
            advertisedAddress = advertised?.Trim() ?? string.Empty;
            configuredPort = port < 0 || port > ushort.MaxValue ? 29600 : port;
        }

        internal static void Update()
        {
            CacheLocalBattleCardIdentities();
            int count = 0;
            while (count++ < 128 && MainThreadActions.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError("[P2P] Main-thread action failed: " + ex);
                    if (Role == P2PRole.Guest && !JoinFinished)
                    {
                        FailJoin("Failed while processing the room join response: " + ex.Message);
                    }
                }
            }
            TrySynchronizeOpponentRoomState();
            TrySendInitialPrivateStateSnapshot();
            TryApplyPendingHiddenCardStates();
            TryApplyPendingPlayerHistoryStates();
            TryApplyPendingFusionActions();
            ObserveLocalHiddenCardStates();
            TryCheckPendingBattleStates();
            if (!peerDisconnected)
            {
                return;
            }

            bool hasBattleManager =
                BattleManagerBase.GetIns() is NetworkBattleManagerBase;
            RoomBase room = RoomBase.GetInstance();
            P2PDisconnectAction disconnectAction = P2PDisconnectPolicy.Evaluate(
                peerDisconnected,
                finishResultSent,
                roomReleaseInjected,
                hasBattleManager,
                IsBattleScene(),
                room != null && room.IsInitializeDone,
                room != null && room.IsRoomReadyComplete,
                currentAgent != null && IsRoomAgentReady());
            if (disconnectAction == P2PDisconnectAction.BattleResult)
            {
                InjectPeerDisconnectResult();
            }
            else if (disconnectAction == P2PDisconnectAction.RoomRelease)
            {
                InjectRoomRelease();
            }
            else if (disconnectAction == P2PDisconnectAction.ForceRoomExit)
            {
                ForceExitDisconnectedRoom(room);
            }
        }

        internal static void StartHosting(P2PRoomRules rules)
        {
            ResetSession();
            Role = P2PRole.Host;
            IsActive = true;
            Rules = rules ?? new P2PRoomRules();
            if (Rules.TwoPickType == (int)TwoPickFormat.Normal)
            {
                Rules.TwoPickRule = P2PTwoPickRules.Normalize(
                    Rules.TwoPickRule ?? P2PTwoPickRules.LoadSelected());
                P2PTwoPickRules.ResetDraft(Rules.TwoPickRule);
            }
            CustomFormatDefinition roomFormat = CustomFormats.Get(Rules.CustomFormatId);
            Rules.CustomFormatId = roomFormat.Id;
            Rules.FormatDefinition = roomFormat.Clone();
            CustomFormatContext.RoomFormatId = Rules.CustomFormatId;
            LocalProfile = CreateLocalProfile();
            RoomId = CreateNumericId();
            BattleId = CreateNumericId();

            byte[] token = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(token);
            }

            IPAddress bind = ParseAddress(bindAddress, IPAddress.Any);
            CreateTransport();
            transport.StartHost(bind, configuredPort, token);
            IPAddress advertised = string.IsNullOrWhiteSpace(advertisedAddress)
                ? (IPAddress.Any.Equals(bind) || IPAddress.IPv6Any.Equals(bind)
                    ? FindAdvertisedAddress(bind.AddressFamily)
                    : bind)
                : ParseAddress(advertisedAddress, null);
            if (!IsUsableAdvertisedAddress(advertised) ||
                advertised.AddressFamily != bind.AddressFamily)
            {
                throw new InvalidOperationException(
                    "P2P advertised address is not usable by the configured listener.");
            }
            ConnectionCode = P2PConnectionCode.Create(advertised, transport.BoundPort, token);
            Plugin.Logger.LogInfo(
                $"[P2P] Hosting room on {bind}:{transport.BoundPort}; advertised as {advertised}; " +
                $"format={Rules.CustomFormatId}; openDeck={Rules.IsDeckOpen}; " +
                $"twoPick={Rules.TwoPickType}; " +
                $"twoPickRule={Rules.TwoPickRule?.Id ?? string.Empty}; " +
                $"draftSize={Rules.TwoPickRule?.FinalDeckSize ?? 0}; " +
                $"initialMaxLife={Rules.InitialMaxLife}.");
        }

        internal static bool TrySetInitialMaxLife(int value)
        {
            if (!CanEditRoomRules || Rules == null)
            {
                return false;
            }

            int clamped = P2PRoomRules.ClampInitialMaxLife(value);
            if (Rules.InitialMaxLife == clamped)
            {
                return true;
            }

            Rules.InitialMaxLife = clamped;
            if (RemoteProfile != null)
            {
                SendWire(new P2PWireMessage
                {
                    Type = "rules_update",
                    Rules = Rules
                });
            }
            Plugin.Logger.LogInfo(
                $"[P2P] Host set both players' initial maximum life to {clamped}.");
            return true;
        }

        internal static void BeginJoin(P2PConnectionInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }
            ResetSession();
            Role = P2PRole.Guest;
            IsActive = true;
            LocalProfile = CreateLocalProfile();
            JoinFinished = false;
            JoinSucceeded = false;
            CreateTransport();
            transport.Connect(info.Address, info.Port, info.Token);
            Plugin.Logger.LogInfo($"[P2P] Connecting to room host at {info.Address}:{info.Port}.");
        }

        internal static bool IsCurrentAgent(RealTimeNetworkAgent agent)
        {
            return agent != null && ReferenceEquals(currentAgent, agent);
        }

        internal static void SetCurrentAgent(RealTimeNetworkAgent agent, string source)
        {
            if (agent == null)
            {
                return;
            }
            if (ReferenceEquals(currentAgent, agent))
            {
                return;
            }
            currentAgent = agent;
            localEmitSequence = 1;
            if (Role == P2PRole.Host)
            {
                hostPlaySequence = 0;
            }
            else if (Role == P2PRole.Guest)
            {
                guestPlaySequence = 0;
            }
            Plugin.Logger.LogInfo(
                $"[P2P] Bound realtime agent from {source ?? "unknown"}: {agent.GetType().Name}.");
            FlushDeferredAgentDeliveries();
        }

        internal static void SetLocalDeck(DeckData deck)
        {
            if (deck == null || deck.GetCardIdList() == null)
            {
                throw new InvalidOperationException("No room deck is selected.");
            }
            if (IsTwoPickRoom)
            {
                int expectedCount = Rules?.TwoPickRule?.FinalDeckSize ?? 30;
                if (deck.GetCardIdList().Count != expectedCount)
                {
                    throw new InvalidOperationException(
                        $"The completed Two Pick deck contains " +
                        $"{deck.GetCardIdList().Count} cards; expected {expectedCount}.");
                }
            }
            else if (!IsDeckAllowed(
                         deck,
                         out CustomFormatDefinition definition,
                         out CustomFormatViolation violation))
            {
                throw new InvalidOperationException(
                    $"Deck {deck.GetDeckID()} is not valid for room format " +
                    $"{definition.Id}: {violation.ToLogMessage()}.");
            }
            LocalDeck = new P2PDeckSnapshot
            {
                Cards = new List<int>(deck.GetCardIdList()),
                ClassId = deck.GetDeckClassID(),
                SubclassId = deck.GetDeckSubClassID(),
                CharaId = deck.GetSkinId(false),
                SleeveId = deck.GetDeckSleeveID()
            };
            if (Role == P2PRole.Guest)
            {
                SendWire(new P2PWireMessage { Type = "deck", Deck = LocalDeck });
            }
            else if (Role == P2PRole.Host)
            {
                TrySendMatched();
            }
        }

        internal static bool IsDeckAllowed(
            DeckData deck,
            out CustomFormatDefinition definition,
            out CustomFormatViolation violation)
        {
            definition = Rules?.FormatDefinition ??
                CustomFormats.Get(Rules?.CustomFormatId);
            if (deck == null || deck.GetCardIdList() == null)
            {
                violation = new CustomFormatViolation(
                    CustomFormatRule.CardDataUnavailable,
                    0,
                    0,
                    0);
                return false;
            }

            if (IsTwoPickRoom)
            {
                violation = null;
                return deck.GetCardIdList().Count > 0;
            }

            return CustomFormats.IsDeckCompliant(
                deck.GetCardIdList(),
                definition,
                CardMaster.GetInstanceForBattle(),
                out violation);
        }

        internal static void HandleEmit(string uri, Dictionary<string, object> data)
        {
            if (!IsActive || string.IsNullOrEmpty(uri))
            {
                return;
            }
            CacheLocalBattleCardIdentities();
            Dictionary<string, object> messageData = P2PJson.CloneDictionary(data);
            messageData["uri"] = uri;
            messageData["viewerId"] = LocalProfile?.ViewerId ?? P2PIdentity.ViewerId;
            messageData["bid"] = BattleId ?? string.Empty;
            AppendLocalHiddenCardState(uri, messageData);
            AppendLocalPlayerHistoryState(uri, messageData);
            RemovePrivateTwoPickDraftData(messageData, uri);
            if (string.Equals(
                    uri,
                    PlayerController.ROOM_URI.RoomEntry.ToString(),
                    StringComparison.Ordinal))
            {
                Plugin.Logger.LogInfo(
                    $"[P2P] Emitting RoomEntry as {Role}; agentReady={currentAgent != null}.");
            }

            if (P2PBattleProtocol.CarriesBattleStateCheckpoint(uri))
            {
                // A local checkpoint means the battle has advanced beyond any peer
                // snapshot still waiting for the shared VFX queue to become idle.
                PendingBattleStateChecks.Clear();
                Dictionary<string, object> state = CaptureBattleState();
                if (state != null)
                {
                    messageData[P2PBattleStateDiagnostics.StateKey] = state;
                }
            }

            if (string.Equals(
                    uri,
                    NetworkBattleDefine.NetworkBattleURI.Retire.ToString(),
                    StringComparison.Ordinal))
            {
                localRetired = true;
            }
            else if (string.Equals(
                    uri,
                    NetworkBattleDefine.NetworkBattleURI.JudgeResult.ToString(),
                    StringComparison.Ordinal))
            {
                int localResult = GetLocalFinishResult();
                messageData["p2pLocalResult"] = localResult;
                Plugin.Logger.LogInfo(
                    $"[P2P] Reporting local battle result {localResult} with JudgeResult.");
            }

            bool attachedBurialSelection =
                BattleSelectionTracker.PrepareOutgoingAction(
                    messageData,
                    ResolveLocalBurialRiteSkillIndexes,
                    out string selectionSummary);
            if (attachedBurialSelection)
            {
                Plugin.Logger.LogInfo(
                    "[P2P] Attached skill selection to outgoing action: " +
                    selectionSummary + ".");
            }
            else if (!string.IsNullOrEmpty(selectionSummary))
            {
                Plugin.Logger.LogWarning(
                    "[P2P] Did not attach skill selection: " +
                    selectionSummary + ".");
            }

            if (!string.Equals(uri, P2PBattleProtocol.EchoUri,
                    StringComparison.Ordinal))
            {
                BattleCardTracker.PrepareOutgoingAction(
                    Role == P2PRole.Host, messageData, out _, out _,
                    ResolveLocalCardId, ResolveLocalCardCost,
                    warning => Plugin.Logger.LogWarning(
                        "[P2P] Hidden-card synchronization: " + warning + "."));
            }

            if (Role == P2PRole.Host)
            {
                HandleServerEmit(true, messageData);
            }
            else
            {
                SendWire(new P2PWireMessage
                {
                    Type = "emit",
                    ViewerId = LocalProfile.ViewerId,
                    BattleId = BattleId,
                    Data = messageData
                });
            }
        }

        internal static void HandleHandData(
            RealTimeNetworkAgent agent,
            List<object> parameters,
            NetworkBattleSender.HAND_URI_TYPE uri)
        {
            if (!IsActive)
            {
                return;
            }

            CacheLocalBattleCardIdentities();
            if (BattleSelectionTracker.RecordHandData(
                    (int)uri, parameters, out string selectionSummary))
            {
                Plugin.Logger.LogInfo(
                    "[P2P] Recorded skill selection: " +
                    selectionSummary + ".");
            }

            if (uri != NetworkBattleSender.HAND_URI_TYPE.SELECT_SKILL_URI &&
                uri != NetworkBattleSender.HAND_URI_TYPE.SLIDE_OBJECT_URI)
            {
                return;
            }

            int sequenceNumber = ++localEmitSequence;
            if (agent != null)
            {
                agent.LastEmitSeqNumber = sequenceNumber;
            }

            List<object> stockHandData = new List<object>
            {
                (int)uri,
                LocalProfile?.ViewerId ?? P2PIdentity.ViewerId,
                string.Empty,
                sequenceNumber
            };
            if (parameters != null)
            {
                stockHandData.AddRange(parameters);
            }

            Dictionary<string, object> acknowledgement =
                new Dictionary<string, object>
                {
                    ["StockHandData"] = stockHandData,
                    ["pubSeq"] = sequenceNumber
                };
            Enqueue(() => agent?.OnAck?.Invoke(acknowledgement));
        }

        internal static void QueueAcknowledgement(
            RealTimeNetworkAgent agent,
            string uri,
            Dictionary<string, object> data,
            Action onFinished,
            bool isGetableAck,
            bool isStockData,
            int fixedSeqNumber)
        {
            if (!isGetableAck)
            {
                Enqueue(() => onFinished?.Invoke());
                return;
            }

            int sequenceNumber = fixedSeqNumber;
            if (sequenceNumber < 0)
            {
                sequenceNumber = ++localEmitSequence;
                if (agent != null)
                {
                    agent.LastEmitSeqNumber = sequenceNumber;
                }
            }
            data["pubSeq"] = sequenceNumber;
            Dictionary<string, object> acknowledgement = P2PJson.CloneDictionary(data);
            acknowledgement["uri"] = uri;
            acknowledgement["viewerId"] = LocalProfile?.ViewerId ?? P2PIdentity.ViewerId;
            acknowledgement["bid"] = BattleId ?? string.Empty;
            Enqueue(() =>
            {
                try
                {
                    agent?.OnAck?.Invoke(acknowledgement);
                    if (agent != null &&
                        string.Equals(uri, NetworkBattleDefine.NetworkBattleURI.TurnStart.ToString(),
                            StringComparison.Ordinal))
                    {
                        agent.AddActionSequence();
                    }
                }
                finally
                {
                    onFinished?.Invoke();
                }
            });
        }

        private static void ResetBattleState()
        {
            hostInitBattle = false;
            guestInitBattle = false;
            matchedSent = false;
            hostLoaded = false;
            guestLoaded = false;
            battleStartSent = false;
            battleStartReceived = false;
            mulliganReadyReceived = false;
            finishResultSent = false;
            retiringHost = null;
            localRetired = false;
            shuffledHostDeck = null;
            shuffledGuestDeck = null;
            BattleCardTracker.Clear();
            LocalHiddenCardStateSignatures.Clear();
            LocalHiddenCardStates.Clear();
            ReceivedHiddenCardStates.Clear();
            ReceivedHiddenCardStateSignatures.Clear();
            AppliedReceivedHiddenCardStates.Clear();
            PrivateConditionWarnings.Clear();
            localPrivateStateSent = false;
            remotePrivateStateReceived = false;
            PendingFusionActions.Clear();
            PendingFusionActionSignatures.Clear();
            ReceivedPlayerHistoryStates.Clear();
            AppliedPlayerHistoryRevisions.Clear();
            localPlayerHistoryStateSignature = string.Empty;
            localPlayerHistoryRevision = 0;
            BattleSelectionTracker.Reset();
            PendingBattleStateChecks.Clear();
            hostMulliganHand = null;
            guestMulliganHand = null;
            hostSwapped = false;
            guestSwapped = false;
            mulliganReadySent = false;
            battleSeed = 0;
            DealState.Reset();
        }

        internal static void FailJoin(string error)
        {
            LastError = string.IsNullOrWhiteSpace(error)
                ? "The room join failed."
                : error;
            JoinFinished = true;
            JoinSucceeded = false;
            transport?.Stop(false);
            Plugin.Logger.LogError("[P2P] Room join failed: " + LastError);
        }

        internal static void AbortFailedJoin()
        {
            string error = LastError;
            ResetSession();
            LastError = error;
        }

        internal static void LeaveRoom(string reason)
        {
            if (transport != null)
            {
                transport.Send(new P2PWireMessage
                {
                    Type = "close",
                    Error = reason
                });
            }
            ResetSession();
        }

        internal static void Shutdown()
        {
            ResetSession();
        }

        private static void CreateTransport()
        {
            int generation = sessionGeneration;
            transport = new P2PTransport();
            transport.Connected += () => Enqueue(generation, OnTransportConnected);
            transport.MessageReceived += message =>
                Enqueue(generation, () => HandleWireMessage(message));
            transport.Disconnected += error =>
                Enqueue(generation, () => OnTransportDisconnected(error));
        }

        private static void OnTransportConnected()
        {
            Plugin.Logger.LogInfo($"[P2P] Transport authenticated as {Role}.");
            if (Role == P2PRole.Guest)
            {
                SendWire(new P2PWireMessage
                {
                    Type = "join",
                    ViewerId = LocalProfile.ViewerId,
                    Profile = LocalProfile
                });
            }
        }

        internal static void RememberLocalCardMutation(
            int playIndex,
            int originalCardId,
            int originalCost,
            int mutationCardId,
            int mutationCost,
            int keyActionType)
        {
            if (!IsActive)
            {
                return;
            }

            bool recorded = BattleCardTracker.RememberSourceCardMutation(
                Role == P2PRole.Host,
                playIndex,
                originalCardId,
                originalCost,
                mutationCardId,
                mutationCost,
                keyActionType);
            if (!recorded)
            {
                return;
            }

            Plugin.Logger.LogInfo(
                $"[P2P] Recorded card mutation: playIdx={playIndex}, " +
                $"type={keyActionType}, originalCardId={originalCardId}, " +
                $"originalCost={originalCost}, mutationCardId={mutationCardId}, " +
                $"mutationCost={mutationCost}.");
        }

        private static void OnTransportDisconnected(string error)
        {
            LastError = error;
            if (Role == P2PRole.Guest && !JoinFinished)
            {
                JoinFinished = true;
                JoinSucceeded = false;
            }
            else
            {
                HandlePeerDisconnected(error);
            }
            Plugin.Logger.LogWarning("[P2P] " + error);
        }

        private static void HandleWireMessage(P2PWireMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Type))
            {
                return;
            }
            switch (message.Type)
            {
                case "join":
                    if (Role != P2PRole.Host || message.Profile == null)
                    {
                        return;
                    }
                    if (message.Profile.ViewerId == LocalProfile.ViewerId)
                    {
                        SendWire(new P2PWireMessage
                        {
                            Type = "join_reject",
                            Error = "Both players have the same local P2P identity."
                        });
                        return;
                    }
                    GuestDeliverySequence.Reset();
                    DeferredGuestDeliveries.Clear();
                    guestPlaySequence = 0;
                    RemoteProfile = message.Profile;
                    pendingOpponentSync = true;
                    SendWire(new P2PWireMessage
                    {
                        Type = "join_ok",
                        ViewerId = LocalProfile.ViewerId,
                        BattleId = BattleId,
                        Profile = LocalProfile,
                        Rules = Rules,
                        Data = new Dictionary<string, object> { ["roomId"] = RoomId }
                    });
                    Plugin.Logger.LogInfo($"[P2P] Guest '{RemoteProfile.UserName}' joined the transport session.");
                    break;
                case "join_ok":
                    HandleJoinAccepted(message);
                    break;
                case "rules_update":
                    if (Role != P2PRole.Guest || message.Rules == null)
                    {
                        return;
                    }
                    ApplyReceivedRoomRules(message.Rules);
                    Plugin.Logger.LogInfo(
                        $"[P2P] Received room rule update: " +
                        $"format={Rules.CustomFormatId}; initialMaxLife={Rules.InitialMaxLife}.");
                    break;
                case "join_reject":
                    LastError = message.Error ?? "The room host rejected the connection.";
                    JoinFinished = true;
                    JoinSucceeded = false;
                    transport?.Stop(false);
                    break;
                case "deck":
                    if (Role == P2PRole.Host)
                    {
                        RemoteDeck = message.Deck;
                        TrySendMatched();
                    }
                    break;
                case "emit":
                    if (Role == P2PRole.Host && message.Data != null)
                    {
                        HandleServerEmit(false, message.Data);
                    }
                    break;
                case "deliver":
                    if (Role == P2PRole.Guest && message.Data != null)
                    {
                        string deliveredUri = GetUri(message.Data);
                        if (string.Equals(
                                deliveredUri,
                                PlayerController.ROOM_URI.RoomEntry.ToString(),
                                StringComparison.Ordinal))
                        {
                            Plugin.Logger.LogInfo(
                                $"[P2P] Received RoomEntry from the host; " +
                                $"agentReady={currentAgent != null}.");
                        }
                        if (message.Data.TryGetValue("playSeq", out object playSequence))
                        {
                            guestPlaySequence = Math.Max(guestPlaySequence,
                                Convert.ToInt32(playSequence));
                        }
                        if (currentAgent == null)
                        {
                            DeferAgentDelivery(message.Data);
                        }
                        else
                        {
                            Inject(message.Data);
                        }
                    }
                    break;
                case "private_state":
                    if (message.Data != null)
                    {
                        RememberReceivedPrivateStateSnapshot(message.Data);
                        // The host answers the guest's baseline with its own
                        // baseline. If cards are not loaded yet, Update() will
                        // retry this after the battle manager becomes ready.
                        if (Role == P2PRole.Host)
                        {
                            TrySendInitialPrivateStateSnapshot();
                        }
                    }
                    break;
                case "diagnostic":
                    if (Role == P2PRole.Host && !string.IsNullOrEmpty(message.Error))
                    {
                        Plugin.Logger.LogError(
                            "[P2P] Remote client diagnostic: " + message.Error);
                    }
                    break;
                case "close":
                    transport?.Stop(false);
                    HandlePeerDisconnected(
                        message.Error ?? "The room was closed by the other player.");
                    break;
            }
        }

        private static void HandleJoinAccepted(P2PWireMessage message)
        {
            if (Role != P2PRole.Guest)
            {
                return;
            }

            Plugin.Logger.LogInfo("[P2P] Received join_ok; synchronizing room rules.");
            try
            {
                if (message.Profile == null)
                {
                    throw new InvalidDataException(
                        "The room host did not provide its player profile.");
                }
                if (message.Rules == null)
                {
                    throw new InvalidDataException(
                        "The room host did not provide room rules.");
                }
                if (string.IsNullOrWhiteSpace(message.BattleId))
                {
                    throw new InvalidDataException(
                        "The room host did not provide a battle ID.");
                }

                string receivedRoomId = message.Data != null &&
                    message.Data.TryGetValue("roomId", out object roomId)
                        ? roomId?.ToString()
                        : message.BattleId;
                if (string.IsNullOrWhiteSpace(receivedRoomId))
                {
                    throw new InvalidDataException(
                        "The room host did not provide a room ID.");
                }

                ApplyReceivedRoomRules(message.Rules);
                RemoteProfile = message.Profile;
                BattleId = message.BattleId;
                RoomId = receivedRoomId;
                pendingOpponentSync = true;
                LastError = null;
                JoinSucceeded = true;
                JoinFinished = true;
                Plugin.Logger.LogInfo(
                    $"[P2P] Joined room hosted by '{RemoteProfile.UserName}' " +
                    $"(format={Rules.CustomFormatId}, openDeck={Rules.IsDeckOpen}, " +
                    $"twoPick={Rules.TwoPickType}, " +
                    $"twoPickRule={Rules.TwoPickRule?.Id ?? string.Empty}, " +
                    $"draftSize={Rules.TwoPickRule?.FinalDeckSize ?? 0}, " +
                    $"initialMaxLife={Rules.InitialMaxLife}).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    "[P2P] Failed to apply the host's room rules: " + ex);
                FailJoin("The host's room rules could not be synchronized: " + ex.Message);
            }
        }

        private static void ApplyReceivedRoomRules(P2PRoomRules receivedRules)
        {
            P2PRoomRules synchronizedRules = receivedRules ??
                throw new ArgumentNullException(nameof(receivedRules));
            if (synchronizedRules.TwoPickType == (int)TwoPickFormat.Normal)
            {
                if (synchronizedRules.BattleType !=
                    (int)NetworkDefine.ServerBattleType.RoomTwoPick)
                {
                    throw new FormatException(
                        "Normal Two Pick rules require the RoomTwoPick battle type.");
                }
                synchronizedRules.TwoPickRule = P2PTwoPickRules.Normalize(
                    synchronizedRules.TwoPickRule ?? P2PTwoPickRules.Load());
            }
            CustomFormatDefinition definition = null;
            if (synchronizedRules.FormatDefinition != null)
            {
                try
                {
                    definition = CustomFormats.InstallRoomDefinition(
                        synchronizedRules.FormatDefinition.Clone());
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        "[P2P] Rejected the host's format definition: " + ex.Message);
                }
            }

            definition = definition ?? CustomFormats.Get(synchronizedRules.CustomFormatId);
            synchronizedRules.CustomFormatId = definition.Id;
            synchronizedRules.FormatDefinition = definition.Clone();

            if (synchronizedRules.TwoPickType == (int)TwoPickFormat.Normal)
            {
                P2PTwoPickRules.ResetDraft(synchronizedRules.TwoPickRule);
            }

            Rules = synchronizedRules;
            CustomFormatContext.RoomFormatId = definition.Id;
            Plugin.Logger.LogInfo(
                $"[P2P] Room rules synchronized: battleType={Rules.BattleType}, " +
                $"deckFormat={Rules.DeckFormat}, twoPick={Rules.TwoPickType}, " +
                $"twoPickRule={Rules.TwoPickRule?.Id ?? string.Empty}, " +
                $"draftSize={Rules.TwoPickRule?.FinalDeckSize ?? 0}.");
        }

        private static void HandleServerEmit(bool sourceIsHost, Dictionary<string, object> data)
        {
            string uri = data["uri"].ToString();
            if (TryGetRoomUri(data, uri, out PlayerController.ROOM_URI roomUri))
            {
                HandleRoomEmit(sourceIsHost, roomUri, data);
                return;
            }

            if (uri == NetworkBattleDefine.NetworkBattleURI.InitNetwork.ToString())
            {
                return;
            }
            if (uri == NetworkBattleDefine.NetworkBattleURI.InitRoomBattle.ToString() ||
                uri == NetworkBattleDefine.NetworkBattleURI.InitBattle.ToString())
            {
                if (sourceIsHost) hostInitBattle = true; else guestInitBattle = true;
                Plugin.Logger.LogInfo(
                    $"[P2P] {SideName(sourceIsHost)} initialized the battle session ({uri}).");
                TrySendMatched();
                return;
            }
            if (uri == NetworkBattleDefine.NetworkBattleURI.Loaded.ToString())
            {
                if (sourceIsHost) hostLoaded = true; else guestLoaded = true;
                Plugin.Logger.LogInfo(
                    $"[P2P] {SideName(sourceIsHost)} finished loading the battle scene.");
                TrySendBattleStart();
                return;
            }
            if (uri == NetworkBattleDefine.NetworkBattleURI.Deal.ToString())
            {
                SendDeal(sourceIsHost);
                return;
            }
            if (uri == NetworkBattleDefine.NetworkBattleURI.Swap.ToString())
            {
                HandleSwap(sourceIsHost, data);
                return;
            }
            if (uri == NetworkBattleDefine.NetworkBattleURI.Retire.ToString())
            {
                retiringHost = sourceIsHost;
                Dictionary<string, object> retireOther = P2PJson.CloneDictionary(data);
                retireOther["isWin"] = 1;
                Deliver(!sourceIsHost, retireOther, SourceViewerId(sourceIsHost), true);
                return;
            }
            if (uri == NetworkBattleDefine.NetworkBattleURI.JudgeResult.ToString())
            {
                SendFinishResult(sourceIsHost, data);
                return;
            }

            bool revealed = P2PBattleProtocol.TryReadPreparedAction(
                data, out int playIndex, out int cardId);
            if (!revealed)
            {
                // Compatibility fallback for an unprepared peer. Current peers
                // prepare at the source, so forwarding must preserve that data.
                revealed = BattleCardTracker.PrepareOutgoingAction(
                    sourceIsHost, data, out playIndex, out cardId,
                    null, null,
                    warning => Plugin.Logger.LogWarning(
                        $"[P2P] {SideName(sourceIsHost)} hidden-card synchronization: " +
                        warning + "."));
            }
            if (uri == NetworkBattleDefine.NetworkBattleURI.PlayActions.ToString())
            {
                Plugin.Logger.LogInfo(
                    $"[P2P] Battle emit {SideName(sourceIsHost)} -> opponent: {uri} " +
                    $"playIdx={playIndex}, cardId={(revealed ? cardId : 0)}, " +
                    $"keys=[{string.Join(",", data.Keys)}]; " +
                    P2PBattleStateDiagnostics.DescribeBattleMessage(data) + ".");
            }

            P2PBattleRoute route = P2PBattleProtocol.GetRoute(uri);
            if (route == P2PBattleRoute.Consume)
            {
                Plugin.Logger.LogInfo(
                    $"[P2P] Consumed {uri} confirmation from {SideName(sourceIsHost)}.");
                return;
            }
            if (P2PBattleProtocol.RequiresActiveTurnState(uri))
            {
                data["turnState"] = 0;
            }

            bool toHost = route == P2PBattleRoute.Source
                ? sourceIsHost
                : !sourceIsHost;
            if (uri == NetworkBattleDefine.NetworkBattleURI.TurnEndActions.ToString() ||
                uri == NetworkBattleDefine.NetworkBattleURI.TurnEnd.ToString() ||
                uri == NetworkBattleDefine.NetworkBattleURI.TurnStart.ToString() ||
                uri == NetworkBattleDefine.NetworkBattleURI.Judge.ToString())
            {
                Plugin.Logger.LogInfo(
                    $"[P2P] Battle emit {SideName(sourceIsHost)} -> " +
                    $"{(toHost ? "Host" : "Guest")}: {uri} (turnState=0).");
            }
            Dictionary<string, object> routedData = route == P2PBattleRoute.Opponent
                ? P2PMessageTransform.PrepareOpponentBattleMessage(data)
                : data;
            Deliver(toHost, routedData, SourceViewerId(sourceIsHost));
        }

        private static void RemovePrivateTwoPickDraftData(
            Dictionary<string, object> data,
            string uri)
        {
            if (!IsTwoPickRoom ||
                !TryGetRoomUri(data, uri, out PlayerController.ROOM_URI roomUri))
            {
                return;
            }

            switch (roomUri)
            {
                case PlayerController.ROOM_URI.BeginCreateDeck:
                    data.Remove("candidateClassIds");
                    break;
                case PlayerController.ROOM_URI.SelectClass:
                    data.Remove("classInfo");
                    data.Remove("candidateCardList");
                    break;
                case PlayerController.ROOM_URI.SelectCardSet:
                    data.Remove("deckInfo");
                    data.Remove("candidateCardList");
                    break;
            }
        }

        private static void HandleRoomEmit(
            bool sourceIsHost,
            PlayerController.ROOM_URI roomUri,
            Dictionary<string, object> data)
        {
            switch (roomUri)
            {
                case PlayerController.ROOM_URI.Reenter:
                    RoomRoundState.Reenter(sourceIsHost);
                    if (sourceIsHost)
                    {
                        hostPlaySequence = 0;
                    }
                    else
                    {
                        guestPlaySequence = 0;
                        GuestDeliverySequence.Reset();
                        GuestDeliverySequence.Open();
                        DeferredGuestDeliveries.Clear();
                    }
                    return;
                case PlayerController.ROOM_URI.RoomCreate:
                    Deliver(true, new Dictionary<string, object>
                    {
                        ["uri"] = roomUri.ToString(),
                        ["resultCode"] = 1,
                        ["isSelf"] = 1
                    }, LocalProfile.ViewerId);
                    return;
                case PlayerController.ROOM_URI.RoomEntry:
                    if (sourceIsHost || RemoteProfile == null)
                    {
                        return;
                    }
                    bool openedGuestDelivery = GuestDeliverySequence.Open();
                    if (openedGuestDelivery)
                    {
                        guestPlaySequence = 0;
                    }
                    Dictionary<string, object> guestAck = CreateRoomPlayerData(LocalProfile);
                    guestAck["uri"] = roomUri.ToString();
                    guestAck["resultCode"] = 1;
                    guestAck["isSelf"] = 1;
                    Deliver(false, guestAck, RemoteProfile.ViewerId);
                    if (openedGuestDelivery)
                    {
                        FlushDeferredGuestDeliveries();
                    }

                    Dictionary<string, object> hostEntry = CreateRoomPlayerData(RemoteProfile);
                    hostEntry["uri"] = roomUri.ToString();
                    hostEntry["resultCode"] = 1;
                    hostEntry["isSelf"] = 0;
                    Deliver(true, hostEntry, RemoteProfile.ViewerId);
                    Plugin.Logger.LogInfo("[P2P] RoomEntry delivered to both players.");
                    return;
                case PlayerController.ROOM_URI.SetupComplete:
                    bool shouldStartBattle = RoomRoundState.MarkReady(sourceIsHost);
                    Plugin.Logger.LogInfo(
                        $"[P2P] {SideName(sourceIsHost)} marked ready in the room " +
                        $"(host={RoomRoundState.HostReady}, guest={RoomRoundState.GuestReady}, " +
                        $"start={shouldStartBattle}).");
                    EchoRoomToBoth(sourceIsHost, data);
                    if (shouldStartBattle)
                    {
                        ResetBattleState();
                        Dictionary<string, object> ready = new Dictionary<string, object>
                        {
                            ["uri"] = PlayerController.ROOM_URI.RoomReady.ToString()
                        };
                        Deliver(true, ready, 0);
                        Deliver(false, ready, 0);
                        Plugin.Logger.LogInfo(
                            "[P2P] Both players are ready; starting battle matching.");
                    }
                    return;
                case PlayerController.ROOM_URI.SetupCancel:
                    RoomRoundState.CancelReady(sourceIsHost);
                    EchoRoomToBoth(sourceIsHost, data);
                    return;
                case PlayerController.ROOM_URI.Leave:
                case PlayerController.ROOM_URI.Release:
                case PlayerController.ROOM_URI.Kick:
                    EchoRoomToBoth(sourceIsHost, data);
                    return;
                case PlayerController.ROOM_URI.DeckEntry:
                    HandleDeckEntry(sourceIsHost, data);
                    return;
                case PlayerController.ROOM_URI.DeckSelect:
                case PlayerController.ROOM_URI.DeckConfirm:
                case PlayerController.ROOM_URI.ChatStamp:
                case PlayerController.ROOM_URI.RoomNotify:
                case PlayerController.ROOM_URI.TurnSelect:
                    Deliver(!sourceIsHost, data, SourceViewerId(sourceIsHost), true);
                    return;
                default:
                    Deliver(!sourceIsHost, data, SourceViewerId(sourceIsHost), true);
                    return;
            }
        }

        private static void HandleDeckEntry(
            bool sourceIsHost,
            Dictionary<string, object> data)
        {
            Dictionary<string, object> cached = P2PJson.CloneDictionary(data);
            if (sourceIsHost)
            {
                hostDeckEntry = cached;
            }
            else
            {
                guestDeckEntry = cached;
            }

            Deliver(!sourceIsHost, cached, SourceViewerId(sourceIsHost), true);
            Plugin.Logger.LogInfo(
                $"[P2P] {SideName(sourceIsHost)} submitted open-deck data " +
                $"(hostCached={hostDeckEntry != null}, guestCached={guestDeckEntry != null}).");

            // A deck entry can arrive while the peer's room listener is still starting or
            // while its deck dialog blocks node messages. Replaying the cached peer entry
            // after this side submits proves both UIs have reached the deck-exchange stage.
            Dictionary<string, object> peerEntry = sourceIsHost
                ? guestDeckEntry
                : hostDeckEntry;
            if (peerEntry == null)
            {
                return;
            }

            bool peerIsHost = !sourceIsHost;
            Deliver(
                sourceIsHost,
                peerEntry,
                SourceViewerId(peerIsHost),
                true);
            Plugin.Logger.LogInfo(
                $"[P2P] Replayed {SideName(peerIsHost)} open-deck data to " +
                $"{SideName(sourceIsHost)} after deck submission.");
        }

        private static bool TryGetRoomUri(
            Dictionary<string, object> data,
            string uri,
            out PlayerController.ROOM_URI roomUri)
        {
            roomUri = default;
            if (!data.TryGetValue("cat", out object category))
            {
                return false;
            }
            try
            {
                return Convert.ToInt32(category) ==
                        (int)RealTimeNetworkAgent.EmitCategory.room &&
                    Enum.TryParse(uri, out roomUri);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void EchoRoomToBoth(bool sourceIsHost, Dictionary<string, object> data)
        {
            Dictionary<string, object> hostData = sourceIsHost
                ? P2PJson.CloneDictionary(data)
                : P2PMessageTransform.FlipPerspective(data);
            Dictionary<string, object> guestData = sourceIsHost
                ? P2PMessageTransform.FlipPerspective(data)
                : P2PJson.CloneDictionary(data);
            hostData["isSelf"] = sourceIsHost ? 1 : 0;
            guestData["isSelf"] = sourceIsHost ? 0 : 1;
            hostData["resultCode"] =
                (int)NetworkBattleDefine.ReceiveNodeResultCode.Success;
            guestData["resultCode"] =
                (int)NetworkBattleDefine.ReceiveNodeResultCode.Success;
            int viewerId = SourceViewerId(sourceIsHost);
            Deliver(true, hostData, viewerId);
            Deliver(false, guestData, viewerId);
        }

        private static void TrySendMatched()
        {
            if (matchedSent || !hostInitBattle || !guestInitBattle ||
                LocalDeck == null || RemoteDeck == null)
            {
                return;
            }
            if (LocalDeck.Cards.Count == 0 || RemoteDeck.Cards.Count == 0)
            {
                LastError = "Both players must select a non-empty deck.";
                return;
            }

            shuffledHostDeck = Shuffle(LocalDeck.Cards);
            shuffledGuestDeck = Shuffle(RemoteDeck.Cards);
            BattleCardTracker.Reset(shuffledHostDeck, shuffledGuestDeck);
            battleSeed = CreatePositiveInt();
            DealState.Initialize(CreatePositiveInt(), CreatePositiveInt());
            hostFirst = (CreatePositiveInt() & 1) == 0;
            matchedSent = true;

            Deliver(true, CreateMatchedData(true), 0);
            Deliver(false, CreateMatchedData(false), 0);
            Plugin.Logger.LogInfo(
                $"[P2P] Matched delivered; {(hostFirst ? "Host" : "Guest")} goes first.");
        }

        private static Dictionary<string, object> CreateMatchedData(bool forHost)
        {
            P2PProfile selfProfile = forHost ? LocalProfile : RemoteProfile;
            P2PProfile oppoProfile = forHost ? RemoteProfile : LocalProfile;
            P2PDeckSnapshot selfDeck = forHost ? LocalDeck : RemoteDeck;
            P2PDeckSnapshot oppoDeck = forHost ? RemoteDeck : LocalDeck;
            List<int> cards = forHost ? shuffledHostDeck : shuffledGuestDeck;
            List<int> opponentCards = forHost ? shuffledGuestDeck : shuffledHostDeck;
            List<object> deckData = new List<object>(cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                deckData.Add(new Dictionary<string, object>
                {
                    ["idx"] = i + 1,
                    ["cardId"] = cards[i]
                });
            }
            return new Dictionary<string, object>
            {
                ["uri"] = NetworkBattleDefine.NetworkBattleURI.Matched.ToString(),
                ["bid"] = BattleId,
                ["turnState"] = (forHost == hostFirst) ? 0 : 1,
                ["selfInfo"] = CreateBattleInfo(selfProfile, selfDeck, oppoProfile, oppoDeck,
                    battleSeed),
                ["oppoInfo"] = CreateBattleInfo(oppoProfile, oppoDeck, selfProfile, selfDeck,
                    battleSeed),
                ["selfDeck"] = deckData,
                [P2PBattleProtocol.OpponentDeckIdentityKey] =
                    P2PBattleProtocol.CreateDeckIdentityPayload(opponentCards)
            };
        }

        internal static void ApplyOpponentDeckIdentity(
            Dictionary<string, object> synchronizeData)
        {
            if (!IsActive || synchronizeData == null ||
                !synchronizeData.TryGetValue("uri", out object rawUri) ||
                (!string.Equals(
                    rawUri?.ToString(),
                    NetworkBattleDefine.NetworkBattleURI.Matched.ToString(),
                    StringComparison.Ordinal) &&
                 !string.Equals(
                     rawUri?.ToString(),
                     NetworkBattleDefine.NetworkBattleURI.BattleStart.ToString(),
                     StringComparison.Ordinal)))
            {
                return;
            }

            int expectedCount = 0;
            if (synchronizeData.TryGetValue("oppoInfo", out object rawOpponentInfo) &&
                rawOpponentInfo is Dictionary<string, object> opponentInfo &&
                opponentInfo.TryGetValue("deckCount", out object rawDeckCount))
            {
                try
                {
                    expectedCount = Convert.ToInt32(rawDeckCount);
                }
                catch (Exception)
                {
                    expectedCount = 0;
                }
            }
            if (expectedCount <= 0)
            {
                P2PDeckSnapshot expectedDeck = Role == P2PRole.Host
                    ? RemoteDeck
                    : LocalDeck;
                expectedCount = expectedDeck?.Cards?.Count ?? 0;
            }
            if (!P2PBattleProtocol.TryReadDeckIdentityPayload(
                    synchronizeData,
                    expectedCount,
                    out List<object> deckData,
                    out string error))
            {
                Plugin.Logger.LogError(
                    "[P2P] Opponent deck identity was rejected: " + error + ".");
                return;
            }

            try
            {
                GameMgr game = GameMgr.GetIns();
                NetworkUserInfoData networkInfo =
                    game?.GetNetworkUserInfoData();
                if (networkInfo == null)
                {
                    Plugin.Logger.LogError(
                        "[P2P] Could not install opponent deck identity: " +
                        "NetworkUserInfoData was unavailable.");
                    return;
                }
                // SetOppoDeck uses DataMgr as its size fallback while the Matched
                // callback has not populated oppoInfo yet.
                game.GetDataMgr()?.SetDeckMaxCount(expectedCount, false);
                networkInfo.SetOppoDeck(deckData);
                Plugin.Logger.LogInfo(
                    $"[P2P] Installed {deckData.Count}-card opponent deck identity " +
                    "before battle load.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    "[P2P] Failed to install opponent deck identity: " + ex);
            }
        }

        private static void TrySendBattleStart()
        {
            if (battleStartSent || !hostLoaded || !guestLoaded || !matchedSent)
            {
                return;
            }
            battleStartSent = true;
            Deliver(true, CreateBattleStartData(true), 0);
            Deliver(false, CreateBattleStartData(false), 0);
            Plugin.Logger.LogInfo("[P2P] BattleStart delivered to both players.");
        }

        private static Dictionary<string, object> CreateBattleStartData(bool forHost)
        {
            P2PProfile selfProfile = forHost ? LocalProfile : RemoteProfile;
            P2PProfile oppoProfile = forHost ? RemoteProfile : LocalProfile;
            P2PDeckSnapshot selfDeck = forHost ? LocalDeck : RemoteDeck;
            P2PDeckSnapshot oppoDeck = forHost ? RemoteDeck : LocalDeck;
            List<int> opponentCards = forHost ? shuffledGuestDeck : shuffledHostDeck;
            return new Dictionary<string, object>
            {
                ["uri"] = NetworkBattleDefine.NetworkBattleURI.BattleStart.ToString(),
                ["bid"] = BattleId,
                ["battleStartDate"] = UnixMilliseconds(),
                ["selfInfo"] = CreateBattleInfo(selfProfile, selfDeck, oppoProfile, oppoDeck,
                    battleSeed),
                ["oppoInfo"] = CreateBattleInfo(oppoProfile, oppoDeck, selfProfile, selfDeck,
                    battleSeed),
                [P2PBattleProtocol.OpponentDeckIdentityKey] =
                    P2PBattleProtocol.CreateDeckIdentityPayload(opponentCards)
            };
        }

        private static Dictionary<string, object> CreateBattleInfo(
            P2PProfile profile,
            P2PDeckSnapshot deck,
            P2PProfile opponent,
            P2PDeckSnapshot opponentDeck,
            int seed)
        {
            return new Dictionary<string, object>
            {
                ["viewerId"] = profile.ViewerId,
                ["oppoId"] = opponent.ViewerId,
                ["userName"] = profile.UserName ?? string.Empty,
                ["rank"] = profile.Rank,
                ["battlePoint"] = profile.BattlePoint,
                ["masterPoint"] = profile.MasterPoint,
                ["isMasterRank"] = profile.MasterPoint > 0 ? 1 : 0,
                ["classId"] = deck.ClassId,
                ["subclassId"] = deck.SubclassId,
                ["charaId"] = deck.CharaId,
                ["sleeveId"] = deck.SleeveId,
                ["emblemId"] = profile.EmblemId,
                ["degreeId"] = profile.DegreeId,
                ["country_code"] = profile.CountryCode ?? string.Empty,
                ["isOfficial"] = profile.IsOfficial,
                ["fieldId"] = 1,
                ["seed"] = seed,
                ["deckCount"] = deck.Cards.Count,
                ["oppoDeckCount"] = opponentDeck.Cards.Count
            };
        }

        private static void SendDeal(bool toHost)
        {
            if (!matchedSent || !DealState.TryClaim(
                    toHost,
                    out int idxChangeSeed,
                    out int opponentIdxChangeSeed))
            {
                return;
            }
            if (hostMulliganHand == null) hostMulliganHand = new List<int> { 1, 2, 3 };
            if (guestMulliganHand == null) guestMulliganHand = new List<int> { 1, 2, 3 };
            List<int> self = toHost ? hostMulliganHand : guestMulliganHand;
            List<int> oppo = toHost ? guestMulliganHand : hostMulliganHand;
            Deliver(toHost, new Dictionary<string, object>
            {
                ["uri"] = NetworkBattleDefine.NetworkBattleURI.Deal.ToString(),
                ["idxChangeSeed"] = idxChangeSeed,
                ["oppoIdxChangeSeed"] = opponentIdxChangeSeed,
                ["self"] = CreateIndexList(self),
                ["oppo"] = CreateIndexList(oppo)
            }, 0);
            Plugin.Logger.LogInfo($"[P2P] Deal delivered to {SideName(toHost)}.");
        }

        private static void HandleSwap(bool sourceIsHost, Dictionary<string, object> data)
        {
            List<int> hand = sourceIsHost
                ? hostMulliganHand ?? new List<int> { 1, 2, 3 }
                : guestMulliganHand ?? new List<int> { 1, 2, 3 };
            List<int> selected = ToIntList(data.TryGetValue("idxList", out object value) ? value : null);
            int replacement = 4;
            foreach (int selectedIndex in selected)
            {
                int position = hand.IndexOf(selectedIndex);
                if (position >= 0)
                {
                    hand[position] = replacement++;
                }
            }
            if (sourceIsHost)
            {
                hostMulliganHand = hand;
                hostSwapped = true;
            }
            else
            {
                guestMulliganHand = hand;
                guestSwapped = true;
            }
            Plugin.Logger.LogInfo(
                $"[P2P] {SideName(sourceIsHost)} completed mulligan selection.");
            Deliver(sourceIsHost, new Dictionary<string, object>
            {
                ["uri"] = NetworkBattleDefine.NetworkBattleURI.Swap.ToString(),
                ["self"] = CreateIndexList(hand)
            }, SourceViewerId(sourceIsHost));

            if (hostSwapped && guestSwapped && !mulliganReadySent)
            {
                mulliganReadySent = true;
                Deliver(true, new Dictionary<string, object>
                {
                    ["uri"] = NetworkBattleDefine.NetworkBattleURI.Ready.ToString(),
                    ["self"] = CreateIndexList(hostMulliganHand),
                    ["oppo"] = CreateIndexList(guestMulliganHand)
                }, 0);
                Deliver(false, new Dictionary<string, object>
                {
                    ["uri"] = NetworkBattleDefine.NetworkBattleURI.Ready.ToString(),
                    ["self"] = CreateIndexList(guestMulliganHand),
                    ["oppo"] = CreateIndexList(hostMulliganHand)
                }, 0);
                Plugin.Logger.LogInfo("[P2P] Mulligan Ready delivered to both players.");
            }
        }

        private static void SendFinishResult(
            bool sourceIsHost,
            Dictionary<string, object> request)
        {
            if (finishResultSent)
            {
                return;
            }
            P2PBattleResultPair results;
            int authoritativeLocalResult;
            bool authoritativeSideIsHost;
            string authority;
            if (retiringHost.HasValue)
            {
                authoritativeSideIsHost = retiringHost.Value;
                authoritativeLocalResult =
                    (int)NetworkBattleReceiver.RESULT_CODE.RetireLose;
                authority = "retirement";
            }
            else if (TryReadReportedLocalResult(request, out int reportedLocalResult))
            {
                authoritativeSideIsHost = sourceIsHost;
                authoritativeLocalResult = reportedLocalResult;
                authority = SideName(sourceIsHost) + " report";
            }
            else
            {
                authoritativeSideIsHost = true;
                authoritativeLocalResult = GetLocalFinishResult();
                authority = "Host fallback";
            }

            if (!P2PBattleResult.IsPairedResult(authoritativeLocalResult))
            {
                Deliver(sourceIsHost, new Dictionary<string, object>
                {
                    ["uri"] = NetworkBattleDefine.NetworkBattleURI.JudgeResult.ToString(),
                    ["result"] = (int)NetworkBattleReceiver.RESULT_CODE.NotFinish
                }, 0);
                Plugin.Logger.LogInfo(
                    $"[P2P] Battle result is not final yet ({authority}=" +
                    $"{authoritativeLocalResult}); requested a retry.");
                return;
            }

            results = P2PBattleResult.FromLocalResult(
                authoritativeSideIsHost,
                authoritativeLocalResult);
            finishResultSent = true;
            Deliver(true, new Dictionary<string, object>
            {
                ["uri"] = NetworkBattleDefine.NetworkBattleURI.JudgeResult.ToString(),
                ["result"] = results.Host
            }, 0);
            Deliver(false, new Dictionary<string, object>
            {
                ["uri"] = NetworkBattleDefine.NetworkBattleURI.JudgeResult.ToString(),
                ["result"] = results.Guest
            }, 0);
            Plugin.Logger.LogInfo(
                $"[P2P] Battle result delivered from {authority} " +
                $"({authoritativeLocalResult}): host receives local result " +
                $"{results.Host}, guest receives local result {results.Guest}.");
        }

        private static bool TryReadReportedLocalResult(
            Dictionary<string, object> request,
            out int result)
        {
            result = 0;
            if (request == null ||
                !request.TryGetValue("p2pLocalResult", out object value))
            {
                return false;
            }

            try
            {
                result = Convert.ToInt32(value);
                return true;
            }
            catch (Exception)
            {
                result = 0;
                return false;
            }
        }

        private static int GetLocalFinishResult()
        {
            if (!matchedSent || !battleStartReceived ||
                !mulliganReadyReceived)
            {
                return (int)NetworkBattleReceiver.RESULT_CODE.NotFinish;
            }
            NetworkBattleManagerBase manager =
                BattleManagerBase.GetIns() as NetworkBattleManagerBase;
            return manager == null || manager.BattlePlayer?.Class == null ||
                manager.BattleEnemy?.Class == null
                ? (int)NetworkBattleReceiver.RESULT_CODE.NotFinish
                : (int)manager.JudgeCurrentFinishStatus();
        }

        private static void Deliver(
            bool toHost,
            Dictionary<string, object> source,
            int viewerId,
            bool flipPerspective = false)
        {
            Dictionary<string, object> data = flipPerspective
                ? P2PMessageTransform.FlipPerspective(source)
                : P2PJson.CloneDictionary(source);
            data.Remove("pubSeq");
            data.Remove("cat");
            data.Remove("try");
            data["viewerId"] = viewerId;
            data["bid"] = BattleId ?? string.Empty;
            if (!toHost && Role == P2PRole.Host && !GuestDeliverySequence.IsOpen)
            {
                if (RemoteProfile != null)
                {
                    DeferredGuestDeliveries.Enqueue(data);
                    Plugin.Logger.LogInfo(
                        $"[P2P] Deferred '{GetUri(data)}' until the guest sends RoomEntry " +
                        $"(queued={DeferredGuestDeliveries.Count}).");
                }
                return;
            }

            int playSequence;
            if (toHost)
            {
                playSequence = ++hostPlaySequence;
            }
            else if (Role == P2PRole.Host)
            {
                if (!GuestDeliverySequence.TryNext(out playSequence))
                {
                    return;
                }
                guestPlaySequence = playSequence;
            }
            else
            {
                playSequence = ++guestPlaySequence;
            }
            data["playSeq"] = playSequence;
            data["time"] = UnixMilliseconds();
            if (toHost)
            {
                Enqueue(() => Inject(data));
            }
            else
            {
                SendWire(new P2PWireMessage
                {
                    Type = "deliver",
                    ViewerId = viewerId,
                    BattleId = BattleId,
                    Data = data
                });
            }
        }

        private static void FlushDeferredGuestDeliveries()
        {
            int deferredCount = DeferredGuestDeliveries.Count;
            Plugin.Logger.LogInfo(
                $"[P2P] Guest room stream opened at playSeq=1; " +
                $"replaying {deferredCount} deferred message(s).");
            while (DeferredGuestDeliveries.Count > 0)
            {
                Dictionary<string, object> data = DeferredGuestDeliveries.Dequeue();
                if (!GuestDeliverySequence.TryNext(out int playSequence))
                {
                    DeferredGuestDeliveries.Clear();
                    return;
                }
                guestPlaySequence = playSequence;
                data["playSeq"] = playSequence;
                data["time"] = UnixMilliseconds();
                SendWire(new P2PWireMessage
                {
                    Type = "deliver",
                    ViewerId = data.TryGetValue("viewerId", out object viewerId)
                        ? Convert.ToInt32(viewerId)
                        : 0,
                    BattleId = BattleId,
                    Data = data
                });
            }
        }

        private static void DeferAgentDelivery(Dictionary<string, object> data)
        {
            string uri = GetUri(data);
            if (DeferredAgentDeliveries.Count >= MaxDeferredAgentDeliveries)
            {
                Plugin.Logger.LogError(
                    $"[P2P] Could not defer '{uri}' because the realtime-agent queue is full.");
                if (Role == P2PRole.Guest && !JoinFinished)
                {
                    FailJoin("Too many room messages arrived before the realtime agent was ready.");
                }
                return;
            }

            DeferredAgentDeliveries.Enqueue(P2PJson.CloneDictionary(data));
            Plugin.Logger.LogInfo(
                $"[P2P] Deferred incoming '{uri}' until the realtime agent is ready " +
                $"(queued={DeferredAgentDeliveries.Count}).");
        }

        private static void FlushDeferredAgentDeliveries()
        {
            if (currentAgent == null || DeferredAgentDeliveries.Count == 0)
            {
                return;
            }

            int count = DeferredAgentDeliveries.Count;
            Plugin.Logger.LogInfo(
                $"[P2P] Replaying {count} message(s) deferred before realtime-agent setup.");
            while (currentAgent != null && DeferredAgentDeliveries.Count > 0)
            {
                Inject(DeferredAgentDeliveries.Dequeue());
            }
        }

        private static string GetUri(Dictionary<string, object> data)
        {
            return data != null && data.TryGetValue("uri", out object uri)
                ? uri?.ToString() ?? "?"
                : "?";
        }

        private static void Inject(Dictionary<string, object> data)
        {
            string uri = data != null && data.TryGetValue("uri", out object value)
                ? value?.ToString() ?? "?"
                : "?";
            if (string.Equals(uri,
                    PlayerController.ROOM_URI.RoomReady.ToString(),
                    StringComparison.Ordinal))
            {
                // The host resets when both players become ready. The guest must
                // reset at the same boundary so no previous round state can make
                // startup Judge messages appear final.
                ResetBattleState();
            }
            else if (string.Equals(uri,
                    NetworkBattleDefine.NetworkBattleURI.Matched.ToString(),
                    StringComparison.Ordinal))
            {
                matchedSent = true;
            }
            else if (string.Equals(uri,
                    NetworkBattleDefine.NetworkBattleURI.BattleStart.ToString(),
                    StringComparison.Ordinal))
            {
                battleStartReceived = true;
            }
            else if (string.Equals(uri,
                    NetworkBattleDefine.NetworkBattleURI.Ready.ToString(),
                    StringComparison.Ordinal))
            {
                mulliganReadyReceived = true;
            }
            Dictionary<string, object> expectedBattleState = null;
            if (data != null &&
                data.TryGetValue(P2PBattleStateDiagnostics.StateKey, out object rawState))
            {
                expectedBattleState = rawState as Dictionary<string, object>;
                data.Remove(P2PBattleStateDiagnostics.StateKey);
            }
            RememberReceivedHiddenCardStates(data);
            RememberReceivedPlayerHistoryState(data, false);
            TryApplyPendingHiddenCardStates();
            ApplyReceivedFusionAction(data);
            if (string.Equals(
                    uri,
                    NetworkBattleDefine.NetworkBattleURI.JudgeResult.ToString(),
                    StringComparison.Ordinal) &&
                data.TryGetValue("result", out object resultValue))
            {
                try
                {
                    if (P2PBattleResult.IsPairedResult(Convert.ToInt32(resultValue)))
                    {
                        finishResultSent = true;
                    }
                }
                catch (Exception)
                {
                }
            }
            if (currentAgent == null)
            {
                RealTimeNetworkAgent gameAgent = ToolboxGame.RealTimeNetworkAgent;
                if (gameAgent != null)
                {
                    SetCurrentAgent(gameAgent, "ToolboxGame fallback");
                }
                if (currentAgent == null)
                {
                    Plugin.Logger.LogWarning(
                        $"[P2P] Dropped realtime message '{uri}' because no agent is active " +
                        $"(game singleton available: {gameAgent != null}).");
                    return;
                }
            }
            // Matching starts battle loading from the Matched callback before
            // RealTimeNetworkBattleAgent applies SetNetworkInfo. Install the
            // private opponent deck table first so that callback can construct
            // real opponent cards instead of the all-Dummy fallback.
            if (string.Equals(
                    uri,
                    NetworkBattleDefine.NetworkBattleURI.Matched.ToString(),
                    StringComparison.Ordinal) ||
                string.Equals(
                    uri,
                    NetworkBattleDefine.NetworkBattleURI.BattleStart.ToString(),
                    StringComparison.Ordinal))
            {
                ApplyOpponentDeckIdentity(data);
            }
            PendingBattleStateCheck pendingStateCheck = null;
            if (expectedBattleState != null)
            {
                // Boundary messages can be injected before the previous message's VFX
                // has drained. Only the newest checkpoint describes the state that will
                // exist when the shared queue next becomes idle.
                PendingBattleStateChecks.Clear();
                pendingStateCheck = new PendingBattleStateCheck(
                    uri,
                    P2PJson.CloneDictionary(expectedBattleState),
                    DateTime.UtcNow.AddSeconds(BattleStateCheckTimeoutSeconds));
                PendingBattleStateChecks.Enqueue(pendingStateCheck);
            }
            try
            {
                if (uri == PlayerController.ROOM_URI.RoomEntry.ToString())
                {
                    string playSequence = data.TryGetValue("playSeq", out object sequence)
                        ? sequence?.ToString() ?? "?"
                        : "?";
                    Plugin.Logger.LogInfo(
                        $"[P2P] Injecting RoomEntry as {Role}; agent={currentAgent.GetType().Name}, " +
                        $"status={currentAgent.CurrentMatchingStatus}, playSeq={playSequence}.");
                }
                currentAgent.ProcessingRecivedData(data);
            }
            catch (Exception ex)
            {
                if (pendingStateCheck != null)
                {
                    pendingStateCheck.InjectionError = ex.ToString();
                }
                ReportBattleDiagnostic(
                    $"Failed to inject '{uri}' message; " +
                    P2PBattleStateDiagnostics.DescribeBattleMessage(data) +
                    $". Exception: {ex}");
            }
        }

        private static bool IsPrivateStateSyncActive =>
            localPrivateStateSent && remotePrivateStateReceived;

        private static void TrySendInitialPrivateStateSnapshot()
        {
            if (!IsActive || !battleStartReceived || localPrivateStateSent ||
                transport == null ||
                !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager) ||
                manager.BattlePlayer == null)
            {
                return;
            }

            List<Dictionary<string, object>> cards = new List<Dictionary<string, object>>();
            HashSet<int> initializedDeckIndices = new HashSet<int>();
            try
            {
                if (manager.BattlePlayer.AllCards != null)
                {
                    foreach (BattleCardBase card in manager.BattlePlayer.AllCards)
                    {
                        if (card != null && card.Index > 0 && card.CardId > 0)
                        {
                            initializedDeckIndices.Add(card.Index);
                        }
                    }
                }
                foreach (BattleCardBase card in EnumeratePrivateCards(
                    manager.BattlePlayer))
                {
                    if (card == null || card.Index <= 0 || card.CardId <= 0)
                    {
                        continue;
                    }
                    cards.Add(CreateHiddenCardState(card));
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug(
                    "[P2P] Could not capture the initial private state: " + ex.Message);
                return;
            }

            int expectedCardCount = LocalDeck?.Cards?.Count ?? 0;
            if (expectedCardCount <= 0 || cards.Count == 0 ||
                Enumerable.Range(1, expectedCardCount)
                    .Any(index => !initializedDeckIndices.Contains(index)))
            {
                // BattlePlayer becomes visible before all deck and hand cards
                // have necessarily been created. Sending at that point would
                // permanently establish an incomplete one-shot baseline.
                return;
            }

            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                ["owner"] = Role == P2PRole.Host ? 1 : 0,
                ["cards"] = cards.Select(card => (object)card).ToList()
            };
            if (!SendWire(new P2PWireMessage
            {
                Type = "private_state",
                ViewerId = LocalProfile?.ViewerId ?? P2PIdentity.ViewerId,
                BattleId = BattleId,
                Data = payload
            }))
            {
                return;
            }

            localPrivateStateSent = true;
            foreach (Dictionary<string, object> card in cards)
            {
                if (!TryGetStateInt(card, "idx", out int index) || index <= 0)
                {
                    continue;
                }
                LocalHiddenCardStates[index] = P2PJson.CloneDictionary(card);
                LocalHiddenCardStateSignatures[index] =
                    JsonConvert.SerializeObject(card, P2PJson.Settings);
            }
            Plugin.Logger.LogInfo(
                $"[P2P] Sent initial private state ({cards.Count} cards, " +
                $"owner={(Role == P2PRole.Host ? "Host" : "Guest")}); " +
                $"incremental snapshots active={IsPrivateStateSyncActive}.");
        }

        private static void RememberReceivedPrivateStateSnapshot(
            Dictionary<string, object> payload)
        {
            if (!IsActive || payload == null ||
                !TryGetStateInt(payload, "owner", out int owner) ||
                (owner != 0 && owner != 1) ||
                owner == (Role == P2PRole.Host ? 1 : 0))
            {
                return;
            }

            bool ownerIsHost = owner == 1;
            if (payload.TryGetValue("cards", out object rawCards) &&
                rawCards is IEnumerable cards && !(rawCards is string))
            {
                foreach (object rawCard in cards)
                {
                    if (!(rawCard is Dictionary<string, object> card) ||
                        !TryGetStateInt(card, "idx", out int index) || index <= 0)
                    {
                        continue;
                    }
                    StoreReceivedHiddenCardState(ownerIsHost, card);
                }
            }

            remotePrivateStateReceived = true;
            Plugin.Logger.LogInfo(
                $"[P2P] Received initial private state from {SideName(ownerIsHost)}; " +
                $"incremental snapshots active={IsPrivateStateSyncActive}.");
        }

        private static void RememberReceivedHiddenCardStates(
            Dictionary<string, object> data)
        {
            if (data == null ||
                !data.TryGetValue("p2pHiddenOwner", out object rawOwner))
            {
                return;
            }

            int owner;
            try
            {
                owner = Convert.ToInt32(rawOwner, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return;
            }

            if (owner != 0 && owner != 1)
            {
                return;
            }

            bool ownerIsHost = owner == 1;

            // A tombstone means the card has left a hidden zone. Do not consume
            // its cached state here: BeforeSettingReceiveData still needs that
            // state while replacing the card for this very action. Cleanup runs
            // from the ReceivedMessage postfix after native processing succeeds.

            if (!data.TryGetValue("p2pHiddenCards", out object rawCards) ||
                rawCards is string || !(rawCards is IEnumerable cards))
            {
                return;
            }

            foreach (object rawCard in cards)
            {
                Dictionary<string, object> card =
                    rawCard as Dictionary<string, object>;
                if (card == null || !TryGetStateInt(card, "idx", out int index) ||
                    index <= 0)
                {
                    continue;
                }

                StoreReceivedHiddenCardState(ownerIsHost, card);
            }
        }

        private static void StoreReceivedHiddenCardState(
            bool ownerIsHost,
            Dictionary<string, object> card)
        {
            if (card == null || !TryGetStateInt(card, "idx", out int index) ||
                index <= 0)
            {
                return;
            }
            string key = HiddenStateKey(ownerIsHost, index);
            Dictionary<string, object> clone = P2PJson.CloneDictionary(card);
            ReceivedHiddenCardStates[key] = clone;
            ReceivedHiddenCardStateSignatures[key] =
                JsonConvert.SerializeObject(clone, P2PJson.Settings);
        }

        internal static void FinalizeReceivedHiddenCardRemovals(
            Dictionary<string, object> data)
        {
            if (!IsActive || data == null ||
                !data.TryGetValue("p2pHiddenOwner", out object rawOwner) ||
                !data.TryGetValue("p2pHiddenRemoved", out object rawRemoved) ||
                rawRemoved is string || !(rawRemoved is IEnumerable removed))
            {
                return;
            }

            int owner;
            try
            {
                owner = Convert.ToInt32(rawOwner, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return;
            }
            if (owner != 0 && owner != 1)
            {
                return;
            }

            bool ownerIsHost = owner == 1;
            foreach (object rawIndex in removed)
            {
                int index;
                try
                {
                    index = Convert.ToInt32(rawIndex,
                        CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    continue;
                }
                if (index <= 0)
                {
                    continue;
                }

                string key = HiddenStateKey(ownerIsHost, index);
                ReceivedHiddenCardStates.Remove(key);
                ReceivedHiddenCardStateSignatures.Remove(key);
                AppliedReceivedHiddenCardStates.Remove(key);
            }
        }

        private static string HiddenStateKey(bool ownerIsHost, int index)
        {
            return (ownerIsHost ? "1:" : "0:") +
                index.ToString(CultureInfo.InvariantCulture);
        }

        private static void RememberReceivedPlayerHistoryState(
            Dictionary<string, object> data,
            bool readyToApply)
        {
            if (data == null ||
                !data.TryGetValue(PlayerHistoryStateKey, out object rawState) ||
                !(rawState is Dictionary<string, object> state) ||
                !TryGetStateInt(state, "owner", out int owner) ||
                (owner != 0 && owner != 1) ||
                !TryGetStateInt(state, "revision", out int revision) ||
                revision <= 0)
            {
                return;
            }

            if (AppliedPlayerHistoryRevisions.TryGetValue(
                    owner, out int appliedRevision) &&
                revision <= appliedRevision)
            {
                return;
            }

            string key = PlayerHistoryStateKeyFor(owner, revision);
            if (!ReceivedPlayerHistoryStates.TryGetValue(
                    key, out PendingPlayerHistoryState pending))
            {
                pending = new PendingPlayerHistoryState
                {
                    Owner = owner,
                    Revision = revision,
                    State = P2PJson.CloneDictionary(state),
                    FirstSeenUtc = DateTime.UtcNow
                };
                ReceivedPlayerHistoryStates[key] = pending;
            }
            if (readyToApply)
            {
                pending.ReadyToApply = true;
            }
        }

        internal static void MarkReceivedPlayerHistoryStateReady(
            Dictionary<string, object> data)
        {
            if (!IsActive)
            {
                return;
            }
            RememberReceivedPlayerHistoryState(data, true);
        }

        private static string PlayerHistoryStateKeyFor(int owner, int revision)
        {
            return owner.ToString(CultureInfo.InvariantCulture) + ":" +
                revision.ToString(CultureInfo.InvariantCulture);
        }

        internal static void TryApplyPendingPlayerHistoryStates()
        {
            if (applyingReceivedPlayerHistoryStates ||
                !IsActive || ReceivedPlayerHistoryStates.Count == 0 ||
                !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager) ||
                manager.BattlePlayer == null || manager.BattleEnemy == null ||
                manager.VfxMgr == null || !manager.VfxMgr.IsEnd)
            {
                return;
            }

            applyingReceivedPlayerHistoryStates = true;
            try
            {
                DateTime now = DateTime.UtcNow;
                int localOwner = Role == P2PRole.Host ? 1 : 0;
                List<PendingPlayerHistoryState> candidates =
                    ReceivedPlayerHistoryStates.Values
                        .Where(state => state.ReadyToApply &&
                            state.Owner != localOwner &&
                            state.NextAttemptUtc <= now)
                        .GroupBy(state => state.Owner)
                        .Select(group => group.OrderByDescending(
                            state => state.Revision).First())
                        .ToList();
                foreach (PendingPlayerHistoryState pending in candidates)
                {
                    if (AppliedPlayerHistoryRevisions.TryGetValue(
                            pending.Owner, out int appliedRevision) &&
                        pending.Revision <= appliedRevision)
                    {
                        RemovePlayerHistoryStatesThrough(
                            pending.Owner, appliedRevision);
                        continue;
                    }

                    BattlePlayerBase target = pending.Owner == localOwner
                        ? manager.BattlePlayer
                        : manager.BattleEnemy;
                    bool complete = ApplyPlayerHistoryState(
                        target, pending.State, out string unresolved);
                    pending.Attempts++;
                    if (!complete)
                    {
                        pending.NextAttemptUtc = now.AddMilliseconds(100);
                        if (!pending.WarningLogged &&
                            now - pending.FirstSeenUtc >= TimeSpan.FromSeconds(5))
                        {
                            pending.WarningLogged = true;
                            Plugin.Logger.LogWarning(
                                $"[P2P] Player history revision {pending.Revision} " +
                                $"is still waiting for card references: {unresolved}.");
                        }
                        continue;
                    }

                    AppliedPlayerHistoryRevisions[pending.Owner] = pending.Revision;
                    RemovePlayerHistoryStatesThrough(
                        pending.Owner, pending.Revision);
                    Plugin.Logger.LogDebug(
                        $"[P2P] Applied remote player history revision " +
                        $"{pending.Revision}.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug(
                    "[P2P] Could not apply player history state: " + ex.Message);
            }
            finally
            {
                applyingReceivedPlayerHistoryStates = false;
            }
        }

        private static void RemovePlayerHistoryStatesThrough(
            int owner,
            int revision)
        {
            foreach (string key in ReceivedPlayerHistoryStates
                .Where(item => item.Value.Owner == owner &&
                    item.Value.Revision <= revision)
                .Select(item => item.Key)
                .ToList())
            {
                ReceivedPlayerHistoryStates.Remove(key);
            }
        }

        private static bool ApplyPlayerHistoryState(
            BattlePlayerBase player,
            Dictionary<string, object> state,
            out string unresolved)
        {
            unresolved = string.Empty;
            if (player == null || state == null)
            {
                unresolved = "player unavailable";
                return false;
            }

            int previousPp = player.Pp;
            int previousPpTotal = player.PpTotal;
            if (state.TryGetValue("scalars", out object rawScalars) &&
                rawScalars is Dictionary<string, object> scalars)
            {
                ApplyPlayerHistoryScalars(player, scalars);
                if (previousPp != player.Pp || previousPpTotal != player.PpTotal)
                {
                    try
                    {
                        player.StatusPanelControl?.SetPp(
                            player.Pp, player.PpTotal, false);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            List<string> unresolvedLists = new List<string>();
            if (state.TryGetValue("class", out object rawClassState) &&
                rawClassState is Dictionary<string, object> classState &&
                classState.Count > 0 &&
                !ApplyPlayerClassState(player.Class, classState))
            {
                unresolvedLists.Add("class=card references or preprocess state");
            }
            if (!state.TryGetValue("lists", out object rawLists) ||
                !(rawLists is Dictionary<string, object> lists))
            {
                unresolved = string.Join(", ", unresolvedLists);
                return unresolvedLists.Count == 0;
            }

            foreach (KeyValuePair<string, object> listState in lists)
            {
                if (!PlayerHistoryListNameSet.Contains(listState.Key) ||
                    !TryGetPlayerHistoryListMember(
                        player, listState.Key, out Type listType,
                        out object rawTarget) ||
                    !listType.IsGenericType ||
                    listType.GetGenericTypeDefinition() != typeof(List<>))
                {
                    continue;
                }
                if (!(rawTarget is IList target))
                {
                    continue;
                }

                Type elementType = listType.GetGenericArguments()[0];
                if (!TryBuildPlayerHistoryList(
                        listState.Value, elementType,
                        out List<object> replacements,
                        out string listUnresolved))
                {
                    unresolvedLists.Add(listState.Key + "=" + listUnresolved);
                    continue;
                }

                target.Clear();
                foreach (object replacement in replacements)
                {
                    target.Add(replacement);
                }
            }

            unresolved = string.Join(", ", unresolvedLists);
            return unresolvedLists.Count == 0;
        }

        private static bool ApplyPlayerClassState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            if (card == null || state == null)
            {
                return false;
            }

            ApplyNativeCompatibleState(card, state);
            ApplyCardModifierState(card, state);
            ApplyPrimitiveState(card, state, "p2pCardPrimitive");
            ApplySkillPrimitiveState(card, state);
            ApplyCardSkillState(card, state);
            ApplyGenericState(card, state);
            ApplyIntegerListState(card, state);
            ApplyStructuredSkillCollectionState(card, state);
            ApplyDamagedCounterState(card, state);
            ApplyAttackCountState(card, state);
            ApplySkillActivationState(card, state);
            ApplySkillCounterState(card, state);
            bool referencesComplete = ApplyCardReferenceState(card, state);
            bool preprocessComplete = ApplyPreprocessState(card, state);
            return ApplyFusionState(card, state) &&
                referencesComplete && preprocessComplete;
        }

        private static void ApplyPlayerHistoryScalars(
            BattlePlayerBase player,
            Dictionary<string, object> scalars)
        {
            foreach (KeyValuePair<string, object> value in scalars)
            {
                if (!PlayerHistoryScalarNameSet.Contains(value.Key) ||
                    !TryFindInstanceProperty(player.GetType(), value.Key,
                        out PropertyInfo property) ||
                    !IsSimpleStateType(property.PropertyType))
                {
                    continue;
                }

                try
                {
                    object converted = ConvertStateValue(
                        value.Value, property.PropertyType);
                    if (TryFindBackingField(player.GetType(), value.Key,
                            out FieldInfo backingField) &&
                        !backingField.IsInitOnly && !backingField.IsLiteral)
                    {
                        backingField.SetValue(player, converted);
                        continue;
                    }

                    string explicitFieldName = GetPlayerHistoryScalarFieldName(
                        value.Key);
                    if (!string.IsNullOrEmpty(explicitFieldName) &&
                        TryFindInstanceField(player.GetType(), explicitFieldName,
                            out FieldInfo explicitField) &&
                        !explicitField.IsInitOnly && !explicitField.IsLiteral)
                    {
                        explicitField.SetValue(player, converted);
                        continue;
                    }

                    MethodInfo setter = property.GetSetMethod(true);
                    setter?.Invoke(player, new[] { converted });
                }
                catch (Exception)
                {
                }
            }
        }

        private static string GetPlayerHistoryScalarFieldName(string propertyName)
        {
            switch (propertyName)
            {
                case "PpTotal":
                    return "_ppTotal";
                case "EpTotal":
                    return "m_EpTotal";
                case "GameUsedEpCount":
                    return "_gameUsedEpCount";
                case "TurnUsedEpCount":
                    return "_turnUsedEpCount";
                default:
                    return string.Empty;
            }
        }

        private static bool TryBuildPlayerHistoryList(
            object rawList,
            Type elementType,
            out List<object> replacements,
            out string unresolved)
        {
            replacements = new List<object>();
            unresolved = string.Empty;
            if (rawList is string || !(rawList is IEnumerable values))
            {
                return true;
            }

            int position = 0;
            foreach (object rawValue in values)
            {
                if (elementType == typeof(BattleCardBase))
                {
                    if (!TryResolvePlayerHistoryCard(rawValue, out BattleCardBase card))
                    {
                        unresolved = DescribeUnresolvedCard(rawValue, position);
                        return false;
                    }
                    replacements.Add(card);
                }
                else if (elementType == typeof(BattlePlayerBase.TurnAndCard))
                {
                    if (!(rawValue is Dictionary<string, object> item) ||
                        !TryResolvePlayerHistoryCard(
                            item.TryGetValue("card", out object rawCard)
                                ? rawCard : null,
                            out BattleCardBase card))
                    {
                        unresolved = DescribeUnresolvedCard(rawValue, position);
                        return false;
                    }
                    TryGetStateInt(item, "turn", out int turn);
                    TryGetStateInt(item, "end", out int end);
                    replacements.Add(new BattlePlayerBase.TurnAndCard(
                        turn, GetLocalTurnFlag(item), card, end != 0));
                }
                else if (elementType == typeof(BattlePlayerBase.CardAndTribe))
                {
                    if (!(rawValue is Dictionary<string, object> item) ||
                        !TryResolvePlayerHistoryCard(
                            item.TryGetValue("card", out object rawCard)
                                ? rawCard : null,
                            out BattleCardBase card))
                    {
                        unresolved = DescribeUnresolvedCard(rawValue, position);
                        return false;
                    }
                    List<CardBasePrm.TribeType> tribes = item.TryGetValue(
                            "tribes", out object rawTribes)
                        ? ToIntArray(rawTribes)
                            .Select(value => (CardBasePrm.TribeType)value)
                            .ToList()
                        : new List<CardBasePrm.TribeType>();
                    replacements.Add(
                        new BattlePlayerBase.CardAndTribe(card, tribes));
                }
                else if (elementType == typeof(BattlePlayerBase.CardAndId))
                {
                    if (!(rawValue is Dictionary<string, object> item) ||
                        !TryResolvePlayerHistoryCard(
                            item.TryGetValue("card", out object rawCard)
                                ? rawCard : null,
                            out BattleCardBase card))
                    {
                        unresolved = DescribeUnresolvedCard(rawValue, position);
                        return false;
                    }
                    TryGetStateInt(item, "id", out int id);
                    replacements.Add(new BattlePlayerBase.CardAndId(card, id));
                }
                else if (elementType == typeof(BattlePlayerBase.CardAndValue))
                {
                    if (!(rawValue is Dictionary<string, object> item) ||
                        !TryResolvePlayerHistoryCard(
                            item.TryGetValue("card", out object rawCard)
                                ? rawCard : null,
                            out BattleCardBase card))
                    {
                        unresolved = DescribeUnresolvedCard(rawValue, position);
                        return false;
                    }
                    TryGetStateInt(item, "value", out int value);
                    replacements.Add(
                        new BattlePlayerBase.CardAndValue(card, value));
                }
                else if (elementType == typeof(TurnAndIntValue))
                {
                    if (!(rawValue is Dictionary<string, object> item))
                    {
                        position++;
                        continue;
                    }
                    TryGetStateInt(item, "value", out int value);
                    TryGetStateInt(item, "turn", out int turn);
                    replacements.Add(
                        new TurnAndIntValue(
                            value, turn, GetLocalTurnFlag(item)));
                }
                else if (elementType == typeof(int))
                {
                    try
                    {
                        replacements.Add(Convert.ToInt32(
                            rawValue, CultureInfo.InvariantCulture));
                    }
                    catch (Exception)
                    {
                        replacements.Add(0);
                    }
                }
                else if (elementType == typeof(List<BattleCardBase>))
                {
                    List<BattleCardBase> nested = new List<BattleCardBase>();
                    if (!(rawValue is string) && rawValue is IEnumerable rawCards)
                    {
                        foreach (object rawCard in rawCards)
                        {
                            if (!TryResolvePlayerHistoryCard(
                                    rawCard, out BattleCardBase card))
                            {
                                unresolved = DescribeUnresolvedCard(
                                    rawCard, position);
                                return false;
                            }
                            nested.Add(card);
                        }
                    }
                    replacements.Add(nested);
                }
                else
                {
                    return true;
                }
                position++;
            }
            return true;
        }

        private static bool GetLocalTurnFlag(
            Dictionary<string, object> item)
        {
            if (TryGetStateInt(item, "turnOwner", out int turnOwner) &&
                (turnOwner == 0 || turnOwner == 1))
            {
                bool turnOwnerIsHost = turnOwner == 1;
                return turnOwnerIsHost == (Role == P2PRole.Host);
            }
            return TryGetStateInt(item, "self", out int legacySelf) &&
                legacySelf != 0;
        }

        private static bool TryResolvePlayerHistoryCard(
            object rawReference,
            out BattleCardBase card)
        {
            card = null;
            if (!(rawReference is Dictionary<string, object> reference) ||
                !TryGetStateInt(reference, "idx", out int index))
            {
                return false;
            }
            if (index <= 0)
            {
                return true;
            }
            card = ResolveCardReference(reference);
            return card != null;
        }

        private static string DescribeUnresolvedCard(object rawReference, int position)
        {
            Dictionary<string, object> reference = rawReference as
                Dictionary<string, object>;
            if (reference != null && reference.TryGetValue(
                    "card", out object nested))
            {
                reference = nested as Dictionary<string, object>;
            }
            string owner = reference != null &&
                reference.TryGetValue("owner", out object rawOwner)
                    ? rawOwner?.ToString() ?? "?"
                    : "?";
            string index = reference != null &&
                reference.TryGetValue("idx", out object rawIndex)
                    ? rawIndex?.ToString() ?? "?"
                    : "?";
            return $"entry {position} owner={owner} idx={index}";
        }

        internal static void ApplyReceivedHiddenCardState(
            BattleCardBase card,
            bool nativeStateInherited = false)
        {
            if (!IsActive || card == null || card.Index <= 0)
            {
                return;
            }

            bool localOwnerIsHost = Role == P2PRole.Host;
            bool ownerIsHost = card.IsPlayer ? localOwnerIsHost : !localOwnerIsHost;
            string key = HiddenStateKey(ownerIsHost, card.Index);
            if (!ReceivedHiddenCardStates.TryGetValue(
                    key,
                    out Dictionary<string, object> state))
            {
                return;
            }

            try
            {
                ReceivedHiddenCardStateSignatures.TryGetValue(
                    key, out string signature);
                AppliedReceivedHiddenCardStates.TryGetValue(
                    key, out AppliedHiddenCardState previous);
                bool sameCoreState = previous != null &&
                    ReferenceEquals(previous.Card, card) &&
                    string.Equals(previous.Signature, signature ?? string.Empty,
                        StringComparison.Ordinal);

                // Unresolved card references can remain pending for several frames.
                // Apply absolute modifiers only once per card/signature and retry only
                // the reference-bearing portions, otherwise an unresolved token would
                // repeatedly clear and rebuild cost/attack/skill modifiers every frame.
                if (!sameCoreState)
                {
                    ApplyNativeCompatibleState(card, state);
                    ApplyCardModifierState(card, state);
                    ApplyPrimitiveState(card, state, "p2pCardPrimitive");
                    ApplySkillPrimitiveState(card, state);
                    ApplyCardSkillState(card, state);
                    ApplyGenericState(card, state);
                    ApplyIntegerListState(card, state);
                    ApplyStructuredSkillCollectionState(card, state);
                    ApplyDamagedCounterState(card, state);
                    ApplyAttackCountState(card, state);
                    ApplySkillActivationState(card, state);
                    ApplySkillCounterState(card, state);
                }
                bool referenceStateComplete = ApplyCardReferenceState(card, state);
                bool preprocessStateComplete = ApplyPreprocessState(card, state);
                bool nativeStateComplete = nativeStateInherited ||
                    (sameCoreState && previous.NativeStateInherited);
                bool stateComplete = ApplyFusionState(card, state) &&
                    referenceStateComplete &&
                    (preprocessStateComplete || nativeStateComplete);
                AppliedReceivedHiddenCardStates[key] = new AppliedHiddenCardState
                {
                    Card = card,
                    Signature = signature ?? string.Empty,
                    NativeStateInherited = nativeStateComplete,
                    StateComplete = stateComplete,
                    NextRetryUtc = stateComplete
                        ? DateTime.MinValue
                        : DateTime.UtcNow.AddMilliseconds(100)
                };
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    $"[P2P] Could not apply hidden card state idx={card.Index}: " +
                    ex.Message);
            }
        }

        internal static void ApplyReceivedFusionAction(
            Dictionary<string, object> data)
        {
            if (!IsActive || data == null ||
                !data.TryGetValue("p2pFusionActions", out object rawActions) ||
                rawActions is string)
            {
                return;
            }

            if (rawActions is Dictionary<string, object> singleAction)
            {
                QueueOrApplyFusionAction(singleAction);
                return;
            }
            if (!(rawActions is IEnumerable actions))
            {
                return;
            }
            foreach (object rawAction in actions)
            {
                if (rawAction is Dictionary<string, object> action)
                {
                    QueueOrApplyFusionAction(action);
                }
            }
        }

        private static void QueueOrApplyFusionAction(
            Dictionary<string, object> action)
        {
            if (TryApplyFusionAction(action))
            {
                PendingFusionActionSignatures.Remove(
                    JsonConvert.SerializeObject(action, P2PJson.Settings));
                return;
            }
            Dictionary<string, object> copy = P2PJson.CloneDictionary(action);
            string signature = JsonConvert.SerializeObject(copy, P2PJson.Settings);
            if (PendingFusionActionSignatures.Add(signature))
            {
                PendingFusionActions.Enqueue(copy);
            }
        }

        private static void TryApplyPendingFusionActions()
        {
            if (!IsActive || PendingFusionActions.Count == 0)
            {
                return;
            }

            int count = PendingFusionActions.Count;
            for (int i = 0; i < count; i++)
            {
                Dictionary<string, object> action = PendingFusionActions.Dequeue();
                if (!TryApplyFusionAction(action))
                {
                    PendingFusionActions.Enqueue(action);
                    continue;
                }
                PendingFusionActionSignatures.Remove(
                    JsonConvert.SerializeObject(action, P2PJson.Settings));
            }
        }

        private static bool TryApplyFusionAction(
            Dictionary<string, object> action)
        {
            if (action == null ||
                !TryGetStateInt(action, "owner", out int owner) ||
                (owner != 0 && owner != 1) ||
                !TryGetStateInt(action, "targetIdx", out int targetIndex) ||
                targetIndex <= 0 || !(BattleManagerBase.GetIns() is
                    NetworkBattleManagerBase manager))
            {
                return true;
            }

            bool ownerIsHost = owner == 1;
            bool localOwnerIsHost = Role == P2PRole.Host ? true : false;
            if (ownerIsHost == localOwnerIsHost)
            {
                return true;
            }

            BattleCardBase target = ResolveCardReference(new Dictionary<string, object>
            {
                ["idx"] = targetIndex,
                ["owner"] = owner
            });
            if (target == null ||
                !(action.TryGetValue("ingredients", out object rawIngredients)) ||
                rawIngredients is string || !(rawIngredients is IEnumerable ingredients))
            {
                return target == null ? false : true;
            }

            BattlePlayerBase ownerPlayer = ownerIsHost == localOwnerIsHost
                ? manager.BattlePlayer : manager.BattleEnemy;
            if (ownerPlayer == null)
            {
                return false;
            }

            SkillApplyInformation information =
                target.SkillApplyInformation as SkillApplyInformation;
            if (information == null)
            {
                return false;
            }

            List<FusionIngredientInfo> replacements =
                new List<FusionIngredientInfo>();
            foreach (object rawIngredient in ingredients)
            {
                if (!(rawIngredient is Dictionary<string, object> ingredient) ||
                    !TryGetStateInt(ingredient, "idx", out int index) || index <= 0)
                {
                    continue;
                }

                BattleCardBase ingredientCard = ResolveCardReference(
                    new Dictionary<string, object>
                    {
                        ["idx"] = index,
                        ["owner"] = owner
                    });
                if (ingredientCard == null)
                {
                    return false;
                }

                int turn = ownerPlayer.Turn;
                TryGetStateInt(ingredient, "turn", out turn);
                replacements.Add(new FusionIngredientInfo(turn, ingredientCard));
            }

            information.FusionIngredients.Clear();
            information.FusionIngredients.AddRange(replacements);
            return true;
        }

        private static void ApplyNativeCompatibleState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            if (card == null || state == null)
            {
                return;
            }

            if (TryGetStateInt(state, "cardId", out int cardId) &&
                cardId > 0 && card.CardId != cardId)
            {
                Plugin.Logger.LogDebug(
                    $"[P2P] Deferred hidden state idx={card.Index} currently has " +
                    $"cardId={card.CardId}; authoritative cardId={cardId}.");
            }

            if (TryGetStateInt(state, "spellboost", out int spellboost))
            {
                card.SetSpellChargeCount(spellboost);
            }
            if (TryGetStateInt(state, "cost", out int cost))
            {
                card.ClearCostModifier();
                card.AddCostModifier(new CostSetModifier(Math.Max(0, cost), false),
                    null, false);
            }

            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null)
            {
                return;
            }

            information.OffenseModifierList.Clear();
            information.LifeModifierList.Clear();
            information.ChantCountModifierList.Clear();
            if (TryGetStateInt(state, "setAtk", out int attack))
            {
                information.AddOffenseModifier(new OffenseSetModifier(attack));
            }
            if (TryGetStateInt(state, "setLife", out int life))
            {
                information.AddLifeModifier(new LifeSetModifier(life));
            }
            if (TryGetStateInt(state, "setChantCount", out int chantCount))
            {
                information.GiveChantCount(
                    new ChantCountSetModifier(chantCount));
            }

            if (state.ContainsKey("clan") || state.ContainsKey("tribe"))
            {
                information.ForceDepriveChangeAffiliation();
                CardBasePrm.ClanType clan = card.BaseParameter.Clan;
                if (TryGetStateInt(state, "clan", out int rawClan))
                {
                    clan = (CardBasePrm.ClanType)rawClan;
                }
                CardBasePrm.TribeInfo tribe = null;
                if (state.TryGetValue("tribe", out object rawTribe) &&
                    !string.IsNullOrEmpty(rawTribe?.ToString()) &&
                    !string.Equals(rawTribe.ToString(), "NONE",
                        StringComparison.Ordinal))
                {
                    tribe = new CardBasePrm.TribeInfo(
                        CardParameter.CreateTribeList(rawTribe.ToString()),
                        CardBasePrm.TribeChangeType.CHANGE);
                }
                information.GiveChangeAffiliation(clan, tribe, false);
            }
        }

        private static void ApplySkillPrimitiveState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            ApplyPrimitiveState(card.SkillApplyInformation, state,
                "p2pSkillPrimitive");
        }

        private static void ApplyCardSkillState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            if (TryGetStateInt(state, "p2pSkillActivatedCount",
                    out int activatedCount))
            {
                card.SetSkillActivatedCount(activatedCount);
            }
            if (TryGetStateInt(state, "p2pSkillActivatedWrap",
                    out int wrapValue))
            {
                card.SetSkillActivatedCountWrapValue(wrapValue);
            }

            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null)
            {
                return;
            }
            bool hasRandomArray = TryGetStateInt(state,
                "p2pSkillRandomArrayPresent", out int randomArrayPresent) &&
                randomArrayPresent != 0;
            information.GiveSkillRandomArray(hasRandomArray &&
                state.TryGetValue("p2pSkillRandomArray", out object rawRandomArray)
                    ? ToIntArray(rawRandomArray)
                    : null);
        }

        private static void ApplyPrimitiveState(
            object target,
            Dictionary<string, object> state,
            string stateKey)
        {
            if (target == null || state == null ||
                !(state.TryGetValue(stateKey, out object rawValues) &&
                    rawValues is Dictionary<string, object> values))
            {
                return;
            }

            foreach (KeyValuePair<string, object> value in values)
            {
                if (!TryFindBackingField(target.GetType(), value.Key,
                        out FieldInfo field) || field.IsInitOnly || field.IsLiteral ||
                    !IsSimpleStateType(field.FieldType))
                {
                    continue;
                }

                try
                {
                    object converted = ConvertStateValue(value.Value, field.FieldType);
                    field.SetValue(target, converted);
                }
                catch (Exception)
                {
                    // A field may be a runtime-version-specific implementation
                    // detail. Ignore only that field and keep the rest of the
                    // snapshot usable.
                }
            }
        }

        private static void ApplyGenericState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null)
            {
                return;
            }

            bool hasArray = TryGetStateInt(state, "p2pGenericArrayPresent",
                out int arrayPresent) && arrayPresent != 0;
            if (hasArray && state.TryGetValue("p2pGenericArray", out object rawArray))
            {
                information.SetSkillGenericArray(ToIntArray(rawArray));
            }
            else
            {
                information.SetSkillGenericArray(null);
            }

            information.SkillGenericKeyAndValue.Clear();
            if (state.TryGetValue("p2pGenericKeys", out object rawKeys) &&
                rawKeys is Dictionary<string, object> keys)
            {
                foreach (KeyValuePair<string, object> key in keys)
                {
                    try
                    {
                        information.SetSkillGenericKeyAndValue(key.Key,
                            Convert.ToInt32(key.Value, CultureInfo.InvariantCulture));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static void ApplyIntegerListState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null ||
                !state.TryGetValue("p2pIntLists", out object rawLists) ||
                !(rawLists is Dictionary<string, object> lists))
            {
                return;
            }

            ReplaceIntList(information.CantAtkUnitBaseCardIdList,
                lists, "cantAtkBaseIds");
            ReplaceIntList(information.DecreaseTurnStartPPList,
                lists, "decreaseTurnStartPP");
            ReplaceIntList(information.CantEvolutionList,
                lists, "cantEvolution");
            ReplaceIntList(information.SkillHealList,
                lists, "skillHeal");
        }

        private static void ReplaceIntList(
            List<int> target,
            Dictionary<string, object> lists,
            string key)
        {
            if (target == null || !lists.TryGetValue(key, out object rawValues))
            {
                return;
            }
            target.Clear();
            target.AddRange(ToIntArray(rawValues));
        }

        private static void ApplyStructuredSkillCollectionState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null ||
                !state.TryGetValue("p2pSkillCollections", out object rawState) ||
                !(rawState is Dictionary<string, object> collections))
            {
                return;
            }

            information.TurnBuffCountList.Clear();
            if (collections.TryGetValue("turnBuff", out object rawTurnBuff) &&
                !(rawTurnBuff is string) && rawTurnBuff is IEnumerable turnBuffs)
            {
                foreach (object rawTurn in turnBuffs)
                {
                    if (!(rawTurn is Dictionary<string, object> turn) ||
                        !TryGetStateInt(turn, "turn", out int turnNumber))
                    {
                        continue;
                    }
                    information.TurnBuffCountList.Add(
                        new BuffCountInfo(turnNumber, GetLocalTurnFlag(turn)));
                }
            }

            information.TokenDrawModifiers.Clear();
            if (collections.TryGetValue("tokenDraw", out object rawTokenDraw) &&
                !(rawTokenDraw is string) && rawTokenDraw is IEnumerable tokenDraws)
            {
                foreach (object rawModifier in tokenDraws)
                {
                    if (!(rawModifier is Dictionary<string, object> modifier) ||
                        !TryGetStateInt(modifier, "cardId", out int cardId) ||
                        !TryGetStateInt(modifier, "count", out int count))
                    {
                        continue;
                    }
                    information.TokenDrawModifiers.Add(
                        new TokenDrawModifier(cardId, count));
                }
            }

            if (!HasExactLifeModifierState(state) &&
                collections.TryGetValue("lifeHistory", out object rawLifeHistory))
            {
                information.LifeModifierList.RemoveAll(modifier =>
                    modifier is DamageCardParameterModifier ||
                    modifier is HealCardParameterModifier ||
                    modifier is HiddenCardLifeStateModifier);
                if (!(rawLifeHistory is string) &&
                    rawLifeHistory is IEnumerable lifeHistory)
                {
                    foreach (object rawModifier in lifeHistory)
                    {
                        if (!(rawModifier is Dictionary<string, object> modifier) ||
                            !TryGetStateInt(modifier, "value", out int value) ||
                            !TryGetStateInt(modifier, "turn", out int turn))
                        {
                            continue;
                        }

                        string kind = modifier.TryGetValue(
                                "kind", out object rawKind)
                            ? rawKind?.ToString()
                            : string.Empty;
                        if (string.Equals(kind, "damage",
                                StringComparison.Ordinal))
                        {
                            information.LifeModifierList.Add(
                                new DamageCardParameterModifier(
                                    value, turn, GetLocalTurnFlag(modifier)));
                        }
                        else if (string.Equals(kind, "heal",
                                     StringComparison.Ordinal))
                        {
                            information.LifeModifierList.Add(
                                new HealCardParameterModifier(
                                    value, turn, GetLocalTurnFlag(modifier)));
                        }
                    }
                }

                CorrectHiddenCardLifeState(card, information, state);
            }

            ReplaceTurnValueCollection(
                information.CausedDamageModifierList,
                collections,
                "causedDamage",
                (value, turn, isSelfTurn) =>
                    new CausedDamageCardParameterModifier(
                        value, turn, isSelfTurn));
            ReplaceTurnValueCollection(
                information.PpModifierList,
                collections,
                "ppAdd",
                (value, turn, isSelfTurn) =>
                    new PpAddModifier(value, turn, isSelfTurn));
        }

        private static bool HasExactLifeModifierState(
            Dictionary<string, object> state)
        {
            return state.TryGetValue("p2pModifiers", out object rawModifiers) &&
                rawModifiers is Dictionary<string, object> modifiers &&
                modifiers.ContainsKey("life");
        }

        private static void ApplyCardModifierState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            if (card == null ||
                !state.TryGetValue("p2pModifiers", out object rawModifiers) ||
                !(rawModifiers is Dictionary<string, object> modifiers))
            {
                return;
            }

            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null)
            {
                return;
            }

            if (TryGetStateCollection(modifiers, "offense", out IEnumerable offense))
            {
                information.OffenseModifierList.Clear();
                foreach (object rawModifier in offense)
                {
                    ICardOffenseModifier modifier =
                        CreateOffenseModifier(rawModifier);
                    if (modifier != null)
                    {
                        information.OffenseModifierList.Add(modifier);
                    }
                }
            }

            if (TryGetStateCollection(modifiers, "life", out IEnumerable life))
            {
                information.LifeModifierList.Clear();
                foreach (object rawModifier in life)
                {
                    ICardLifeModifier modifier = CreateLifeModifier(rawModifier);
                    if (modifier != null)
                    {
                        information.LifeModifierList.Add(modifier);
                    }
                }
                CorrectHiddenCardLifeState(card, information, state);
            }

            if (TryGetStateCollection(modifiers, "cost", out IEnumerable cost))
            {
                card.CostModifierList.Clear();
                foreach (object rawModifier in cost)
                {
                    ICardCostModifier modifier = CreateCostModifier(rawModifier);
                    if (modifier != null)
                    {
                        card.CostModifierList.Add(modifier);
                    }
                }
            }

            if (TryGetStateCollection(modifiers, "chant", out IEnumerable chant))
            {
                information.ChantCountModifierList.Clear();
                foreach (object rawModifier in chant)
                {
                    ICardChantCountModifier modifier =
                        CreateChantCountModifier(rawModifier);
                    if (modifier != null)
                    {
                        information.ChantCountModifierList.Add(modifier);
                    }
                }
            }
        }

        private static bool TryGetStateCollection(
            Dictionary<string, object> state,
            string key,
            out IEnumerable values)
        {
            values = null;
            if (!state.TryGetValue(key, out object rawValues) ||
                rawValues is string || !(rawValues is IEnumerable collection))
            {
                return false;
            }
            values = collection;
            return true;
        }

        private static ICardOffenseModifier CreateOffenseModifier(object rawState)
        {
            if (!(rawState is Dictionary<string, object> state) ||
                !TryGetModifierKindAndValue(state, out string kind, out int value))
            {
                return null;
            }
            switch (kind)
            {
                case "add":
                    return new OffenseAddModifier(value);
                case "set":
                    return new OffenseSetModifier(value);
                case "multiply":
                    return new OffenseMultiplyModifier(value);
                default:
                    return null;
            }
        }

        private static ICardLifeModifier CreateLifeModifier(object rawState)
        {
            if (!(rawState is Dictionary<string, object> state) ||
                !TryGetModifierKindAndValue(state, out string kind, out int value))
            {
                return null;
            }
            switch (kind)
            {
                case "add":
                    return new LifeAddModifier(value);
                case "set":
                    return new LifeSetModifier(value);
                case "multiply":
                    return new LifeMultiplyModifier(value);
                case "damage":
                    return TryGetStateInt(state, "turn", out int damageTurn)
                        ? new DamageCardParameterModifier(
                            value, damageTurn, GetLocalTurnFlag(state))
                        : null;
                case "heal":
                    return TryGetStateInt(state, "turn", out int healTurn)
                        ? new HealCardParameterModifier(
                            value, healTurn, GetLocalTurnFlag(state))
                        : null;
                default:
                    return null;
            }
        }

        private static ICardCostModifier CreateCostModifier(object rawState)
        {
            if (!(rawState is Dictionary<string, object> state) ||
                !state.TryGetValue("kind", out object rawKind))
            {
                return null;
            }
            string kind = rawKind?.ToString();
            bool resident = TryGetStateInt(state, "resident", out int rawResident) &&
                rawResident != 0;
            switch (kind)
            {
                case "add":
                    return TryGetStateInt(state, "value", out int add)
                        ? new CostAddModifier(add, resident)
                        : null;
                case "set":
                    return TryGetStateInt(state, "value", out int set)
                        ? new CostSetModifier(set, resident)
                        : null;
                case "halfUp":
                    return new CostHalfRoundUpModifier(resident);
                case "halfDown":
                    return new CostHalfRoundDownModifier(resident);
                default:
                    return null;
            }
        }

        private static ICardChantCountModifier CreateChantCountModifier(
            object rawState)
        {
            if (!(rawState is Dictionary<string, object> state) ||
                !TryGetModifierKindAndValue(state, out string kind, out int value))
            {
                return null;
            }
            switch (kind)
            {
                case "add":
                    return new ChantCountAddModifier(value);
                case "set":
                    return new ChantCountSetModifier(value);
                default:
                    return null;
            }
        }

        private static bool TryGetModifierKindAndValue(
            Dictionary<string, object> state,
            out string kind,
            out int value)
        {
            kind = null;
            value = 0;
            if (!state.TryGetValue("kind", out object rawKind) ||
                !TryGetStateInt(state, "value", out value))
            {
                return false;
            }
            kind = rawKind?.ToString();
            return !string.IsNullOrEmpty(kind);
        }

        private static void CorrectHiddenCardLifeState(
            BattleCardBase card,
            SkillApplyInformation information,
            Dictionary<string, object> state)
        {
            if (!state.TryGetValue("p2pLifeState", out object rawLifeState) ||
                !(rawLifeState is Dictionary<string, object> lifeState) ||
                !TryGetStateInt(lifeState, "life", out int life) ||
                !TryGetStateInt(lifeState, "maxLife", out int maxLife) ||
                (card.Life == life && card.MaxLife == maxLife))
            {
                return;
            }

            // Damage and healing entries double as condition history and life
            // modifiers. Their original ordering relative to max-life buffs is
            // private, so retain the authoritative final life values as well.
            information.LifeModifierList.Add(
                new HiddenCardLifeStateModifier(life, maxLife));
        }

        private static void ReplaceTurnValueCollection<T>(
            List<T> target,
            Dictionary<string, object> collections,
            string key,
            Func<int, int, bool, T> create)
        {
            if (target == null || !collections.TryGetValue(key, out object rawValues))
            {
                return;
            }

            target.Clear();
            if (rawValues is string || !(rawValues is IEnumerable values))
            {
                return;
            }
            foreach (object rawValue in values)
            {
                if (!(rawValue is Dictionary<string, object> value) ||
                    !TryGetStateInt(value, "value", out int amount) ||
                    !TryGetStateInt(value, "turn", out int turn))
                {
                    continue;
                }
                target.Add(create(amount, turn, GetLocalTurnFlag(value)));
            }
        }

        private static void ApplyDamagedCounterState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            if (card?.DamagedCounter == null ||
                !state.TryGetValue("p2pDamagedCounter", out object rawCounter) ||
                !(rawCounter is Dictionary<string, object> counter))
            {
                return;
            }

            TryGetStateInt(counter, "selfTurn", out int selfTurn);
            TryGetStateInt(counter, "opponentTurn", out int opponentTurn);
            card.DamagedCounter.Clear();
            for (int i = 0; i < Math.Max(0, selfTurn); i++)
            {
                card.DamagedCounter.AddDamageCount(true);
            }
            for (int i = 0; i < Math.Max(0, opponentTurn); i++)
            {
                card.DamagedCounter.AddDamageCount(false);
            }
        }

        private static void ApplySkillActivationState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            if (card?.SkillActivationList == null ||
                !state.TryGetValue("p2pSkillActivationIds", out object rawIds) ||
                rawIds is string || !(rawIds is IEnumerable ids))
            {
                return;
            }

            card.SkillActivationList.Clear();
            foreach (object rawId in ids)
            {
                try
                {
                    long id = Convert.ToInt64(
                        rawId, CultureInfo.InvariantCulture);
                    card.SkillActivationList.Add(
                        new BattleCardBase.SkillActivationInfo(id, null));
                }
                catch (Exception)
                {
                }
            }
        }

        private static void ApplyAttackCountState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            if (card?.attackCountinfo == null ||
                !TryGetStateInt(state, "p2pMaxAttackableCount", out int count))
            {
                return;
            }

            card.attackCountinfo.Clear();
            if (count != 1)
            {
                // Native replay data represents remote attack-count changes as
                // one absolute SetAttackCountInfo entry too.
                card.attackCountinfo.Add(
                    new BattleCardBase.SetAttackCountInfo(null, count));
            }
        }

        private static bool ApplyCardReferenceState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null ||
                !state.TryGetValue("p2pCardReferences", out object rawLists) ||
                !(rawLists is Dictionary<string, object> lists))
            {
                return true;
            }

            bool complete = true;
            complete &= ReplaceCardReferenceList(
                information.RandomSelectedCardList, lists, "randomSelected");
            complete &= ReplaceCardReferenceList(
                information.SkillDrewCardList, lists, "skillDrew");
            complete &= ReplaceCardReferenceList(
                information.SavedTargetList, lists, "savedTargets");
            complete &= ReplaceCardReferenceList(
                information.SavedBurialRiteTargetList, lists,
                "savedBurialTargets");
            complete &= ReplaceCardReferenceList(
                information.LastBurialRiteCardList, lists,
                "lastBurialTargets");
            complete &= ReplaceCardReferenceList(
                information.GetOnCards, lists, "getOn");
            complete &= ReplaceCardReferenceList(
                card.GetOffCards, lists, "getOff");

            information.SavedTargetCardIdDict.Clear();
            if (state.TryGetValue("p2pSavedTargetIds", out object rawSavedIds) &&
                rawSavedIds is Dictionary<string, object> savedIds)
            {
                foreach (KeyValuePair<string, object> saved in savedIds)
                {
                    if (long.TryParse(saved.Key, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out long id))
                    {
                        information.SavedTargetCardIdDict[id] =
                            ToIntArray(saved.Value).ToList();
                    }
                }
            }
            return complete;
        }

        private static bool ApplyPreprocessState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            if (!state.TryGetValue("p2pPreprocess", out object rawState) ||
                !(rawState is Dictionary<string, object> preprocessState))
            {
                return true;
            }

            bool normalComplete = ApplyPreprocessCollection(
                card.NormalSkills, preprocessState, "normal");
            bool evolutionComplete = ApplyPreprocessCollection(
                card.EvolutionSkills, preprocessState, "evolution");
            return normalComplete && evolutionComplete;
        }

        private static bool ApplyPreprocessCollection(
            IEnumerable<SkillBase> skills,
            Dictionary<string, object> preprocessState,
            string key)
        {
            if (!preprocessState.TryGetValue(key, out object rawSkills) ||
                rawSkills is string || !(rawSkills is IEnumerable skillStates))
            {
                return true;
            }

            List<SkillBase> actualSkills = skills?.ToList() ??
                new List<SkillBase>();
            List<object> expectedSkills = skillStates.Cast<object>().ToList();
            bool complete = actualSkills.Count == expectedSkills.Count;
            for (int i = 0; i < expectedSkills.Count; i++)
            {
                if (!(expectedSkills[i] is Dictionary<string, object> expected) ||
                    i >= actualSkills.Count || actualSkills[i] == null)
                {
                    complete = false;
                    continue;
                }

                SkillBase actual = actualSkills[i];
                if (!expected.TryGetValue("type", out object rawType) ||
                    !string.Equals(rawType?.ToString(),
                        actual.GetType().FullName, StringComparison.Ordinal))
                {
                    complete = false;
                    continue;
                }
                if (!expected.TryGetValue("items", out object rawItems) ||
                    rawItems is string || !(rawItems is IEnumerable itemStates))
                {
                    continue;
                }

                List<SkillPreprocessBase> actualItems = actual.PreprocessList?
                    .ToList() ?? new List<SkillPreprocessBase>();
                List<object> expectedItems = itemStates.Cast<object>().ToList();
                if (actualItems.Count != expectedItems.Count)
                {
                    complete = false;
                }
                for (int j = 0; j < expectedItems.Count; j++)
                {
                    if (!(expectedItems[j] is Dictionary<string, object> item) ||
                        j >= actualItems.Count || actualItems[j] == null ||
                        !item.TryGetValue("type", out object rawItemType) ||
                        !string.Equals(rawItemType?.ToString(),
                            actualItems[j].GetType().FullName,
                            StringComparison.Ordinal))
                    {
                        complete = false;
                        continue;
                    }
                    if (item.TryGetValue("fields", out object rawFields) &&
                        rawFields is Dictionary<string, object> fields)
                    {
                        ApplyMutableSimpleFields(actualItems[j], fields);
                    }
                }
            }
            return complete;
        }

        private static void ApplyMutableSimpleFields(
            object target,
            Dictionary<string, object> values)
        {
            if (target == null || values == null)
            {
                return;
            }
            foreach (KeyValuePair<string, object> value in values)
            {
                if (!TryFindInstanceField(target.GetType(), value.Key,
                        out FieldInfo field) || field.IsStatic || field.IsInitOnly ||
                    field.IsLiteral || !IsSimpleStateType(field.FieldType))
                {
                    continue;
                }
                try
                {
                    field.SetValue(target,
                        ConvertStateValue(value.Value, field.FieldType));
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool ReplaceCardReferenceList(
            List<BattleCardBase> target,
            Dictionary<string, object> lists,
            string key)
        {
            if (target == null || !lists.TryGetValue(key, out object rawValues) ||
                rawValues is string || !(rawValues is IEnumerable values))
            {
                return true;
            }

            List<BattleCardBase> replacements = new List<BattleCardBase>();
            foreach (object rawValue in values)
            {
                BattleCardBase resolved = ResolveCardReference(
                    rawValue as Dictionary<string, object>);
                if (resolved == null)
                {
                    return false;
                }
                replacements.Add(resolved);
            }
            target.Clear();
            target.AddRange(replacements);
            return true;
        }

        private static BattleCardBase ResolveCardReference(
            Dictionary<string, object> reference)
        {
            if (reference == null ||
                !TryGetStateInt(reference, "idx", out int index) || index <= 0 ||
                !TryGetStateInt(reference, "owner", out int owner) ||
                (owner != 0 && owner != 1) ||
                !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager))
            {
                return null;
            }

            bool ownerIsHost = owner == 1;
            bool isLocalPlayer = ownerIsHost == (Role == P2PRole.Host);
            BattlePlayerBase player = isLocalPlayer
                ? manager.BattlePlayer
                : manager.BattleEnemy;
            if (player == null)
            {
                return null;
            }

            BattleCardBase resolved = player.AllCardsWithSkillIngredient?
                .FirstOrDefault(candidate => candidate != null &&
                    candidate.Index == index) ??
                player.AllCards?.FirstOrDefault(candidate => candidate != null &&
                    candidate.Index == index);
            return resolved ?? FindCardInPlayerReferences(player, index);
        }

        private static BattleCardBase FindCardInPlayerReferences(
            BattlePlayerBase player,
            int index)
        {
            IEnumerable<IEnumerable<BattleCardBase>> coreLists =
                new IEnumerable<BattleCardBase>[]
                {
                    player.HandCardList,
                    player.DeckCardList,
                    player.ClassAndInPlayCardList,
                    player.CemeteryList,
                    player.BanishList
                };
            foreach (IEnumerable<BattleCardBase> list in coreLists)
            {
                BattleCardBase coreCard = list?.FirstOrDefault(card =>
                    card != null && card.Index == index);
                if (coreCard != null)
                {
                    return coreCard;
                }
            }

            foreach (string name in PlayerHistoryListNames)
            {
                if (!TryGetPlayerHistoryListMember(
                        player, name, out _, out object rawList))
                {
                    continue;
                }
                BattleCardBase card = FindCardInHistoryValue(rawList, index);
                if (card != null)
                {
                    return card;
                }
            }
            return null;
        }

        private static BattleCardBase FindCardInHistoryValue(
            object value,
            int index)
        {
            if (value is BattleCardBase card)
            {
                return card.Index == index ? card : null;
            }
            if (value is BattlePlayerBase.TurnAndCard turnAndCard)
            {
                card = turnAndCard.Card as BattleCardBase;
                return card != null && card.Index == index ? card : null;
            }
            if (value is BattlePlayerBase.CardAndTribe cardAndTribe)
            {
                card = cardAndTribe.Card as BattleCardBase;
                return card != null && card.Index == index ? card : null;
            }
            if (value is BattlePlayerBase.CardAndId cardAndId)
            {
                card = cardAndId.Card as BattleCardBase;
                return card != null && card.Index == index ? card : null;
            }
            if (value is BattlePlayerBase.CardAndValue cardAndValue)
            {
                card = cardAndValue.Card as BattleCardBase;
                return card != null && card.Index == index ? card : null;
            }
            if (value is string || !(value is IEnumerable values))
            {
                return null;
            }
            foreach (object item in values)
            {
                card = FindCardInHistoryValue(item, index);
                if (card != null)
                {
                    return card;
                }
            }
            return null;
        }

        private static int[] ToIntArray(object raw)
        {
            if (raw == null || raw is string || !(raw is IEnumerable values))
            {
                return Array.Empty<int>();
            }

            List<int> result = new List<int>();
            foreach (object value in values)
            {
                try
                {
                    result.Add(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                    result.Add(0);
                }
            }
            return result.ToArray();
        }

        private static void ApplySkillCounterState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null)
            {
                return;
            }

            if (TryGetStateInt(state, "p2pUnionBurstCount", out int unionBurst))
            {
                information.UnionBurstCountModifierList.Clear();
                information.GiveUnionBurstCount(
                    new UnionBurstCountAddModifier(unionBurst - 10));
            }
            if (TryGetStateInt(state, "p2pSkyboundArtCount", out int skyboundArt))
            {
                information.SkyboundArtCountModifierList.Clear();
                information.GiveSkyboundArtCount(
                    new SkyboundArtCountAddModifier(skyboundArt - 10));
            }
            if (TryGetStateInt(state, "p2pSuperSkyboundArtCount",
                    out int superSkyboundArt))
            {
                information.SuperSkyboundArtCountModifierList.Clear();
                information.GiveSuperSkyboundArtCount(
                    new SuperSkyboundArtCountAddModifier(superSkyboundArt - 15));
            }
        }

        private static bool ApplyFusionState(
            BattleCardBase card,
            Dictionary<string, object> state)
        {
            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (information == null)
            {
                return false;
            }

            if (!state.TryGetValue("p2pFusion", out object rawFusion) ||
                rawFusion is string || !(rawFusion is IEnumerable ingredients))
            {
                information.FusionIngredients.Clear();
                return true;
            }

            bool ownerIsHost = card.IsPlayer == (Role == P2PRole.Host);
            return TryApplyFusionIngredients(card, ownerIsHost, ingredients);
        }

        private static bool TryApplyFusionIngredients(
            BattleCardBase card,
            bool ownerIsHost,
            IEnumerable ingredients)
        {
            if (card == null || ingredients == null ||
                !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager))
            {
                return false;
            }

            BattlePlayerBase owner = ownerIsHost == (Role == P2PRole.Host)
                ? manager.BattlePlayer : manager.BattleEnemy;
            SkillApplyInformation information =
                card.SkillApplyInformation as SkillApplyInformation;
            if (owner == null || information == null)
            {
                return false;
            }

            List<FusionIngredientInfo> replacements =
                new List<FusionIngredientInfo>();
            foreach (object rawIngredient in ingredients)
            {
                Dictionary<string, object> ingredient =
                    rawIngredient as Dictionary<string, object>;
                if (ingredient == null ||
                    !TryGetStateInt(ingredient, "idx", out int index))
                {
                    continue;
                }

                BattleCardBase ingredientCard = ResolveCardReference(
                    new Dictionary<string, object>
                    {
                        ["idx"] = index,
                        ["owner"] = ownerIsHost ? 1 : 0
                    });
                if (ingredientCard == null)
                {
                    return false;
                }

                int turn = owner.Turn;
                TryGetStateInt(ingredient, "turn", out turn);
                replacements.Add(new FusionIngredientInfo(turn, ingredientCard));
            }
            information.FusionIngredients.Clear();
            information.FusionIngredients.AddRange(replacements);
            return true;
        }

        internal static void TryApplyPendingHiddenCardStates()
        {
            if (applyingReceivedHiddenCardStates)
            {
                return;
            }

            applyingReceivedHiddenCardStates = true;
            try
            {
                TryApplyPendingHiddenCardStatesCore();
            }
            finally
            {
                applyingReceivedHiddenCardStates = false;
            }
        }

        internal static void WarnIfPrivateConditionHasDummyCards(
            SkillBase skill)
        {
            if (!IsActive || skill?.SkillPrm?.ownerCard == null)
            {
                return;
            }

            BattlePlayerBase owner = skill.SkillPrm.ownerCard.SelfBattlePlayer;
            if (owner == null)
            {
                return;
            }

            List<int> hand = FindUnresolvedPrivateCardIndices(owner.HandCardList);
            List<int> deck = FindUnresolvedPrivateCardIndices(owner.DeckCardList);
            if (hand.Count == 0 && deck.Count == 0)
            {
                return;
            }

            string warningKey = skill.SkillPrm.ownerCard.Index + ":" +
                skill.GetType().FullName + ":" + string.Join(",", hand) + ":" +
                string.Join(",", deck);
            if (!PrivateConditionWarnings.Add(warningKey))
            {
                return;
            }

            Plugin.Logger.LogWarning(
                $"[P2P] Private hand/deck condition still contains unresolved " +
                $"Dummy cards: ownerIdx={skill.SkillPrm.ownerCard.Index}, " +
                $"skill={skill.GetType().Name}, handIdx=[{string.Join(",", hand)}], " +
                $"deckIdx=[{string.Join(",", deck)}].");
        }

        private static List<int> FindUnresolvedPrivateCardIndices(
            IEnumerable<BattleCardBase> cards)
        {
            if (cards == null)
            {
                return new List<int>();
            }
            return cards
                .Where(card => card != null && card.Index > 0 &&
                    (card is NullBattleCard || card.CardId <= 0))
                .Select(card => card.Index)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
        }

        private static void TryApplyPendingHiddenCardStatesCore()
        {
            if (!IsActive || ReceivedHiddenCardStates.Count == 0 ||
                !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager) ||
                manager.BattleEnemy == null)
            {
                return;
            }

            List<BattleCardBase> privateCards;
            try
            {
                privateCards = EnumeratePrivateCards(manager.BattleEnemy).ToList();
            }
            catch (Exception)
            {
                return;
            }

            bool canReplace = manager.VfxMgr != null && manager.VfxMgr.IsEnd;
            DateTime now = DateTime.UtcNow;
            List<BattleCardBase> replacementsToLoad =
                new List<BattleCardBase>();
            foreach (BattleCardBase card in privateCards)
            {
                if (card == null || card.Index <= 0)
                {
                    continue;
                }

                bool remoteOwnerIsHost = Role != P2PRole.Host;
                string key = HiddenStateKey(remoteOwnerIsHost, card.Index);
                if (!ReceivedHiddenCardStates.TryGetValue(
                        key, out Dictionary<string, object> state) ||
                    !ReceivedHiddenCardStateSignatures.TryGetValue(
                        key, out string signature))
                {
                    continue;
                }

                AppliedReceivedHiddenCardStates.TryGetValue(
                    key, out AppliedHiddenCardState applied);
                bool sameApplication = applied != null &&
                    ReferenceEquals(applied.Card, card) &&
                    string.Equals(applied.Signature, signature,
                        StringComparison.Ordinal);
                bool retryDue = applied == null ||
                    applied.NextRetryUtc <= now;
                if (!sameApplication || (!applied.StateComplete && retryDue))
                {
                    ApplyReceivedHiddenCardState(card, false);
                    AppliedReceivedHiddenCardStates.TryGetValue(key, out applied);
                }

                if (!canReplace || applied == null ||
                    applied.NativeStateInherited ||
                    !CanReplacePrivateCard(manager.BattleEnemy, card))
                {
                    continue;
                }

                BattleCardBase replacement = TryReplacePendingHiddenCard(
                    manager, state, key, card);
                if (replacement != null)
                {
                    replacementsToLoad.Add(replacement);
                }
            }

            if (replacementsToLoad.Count > 0 && !manager.IsRecovery &&
                manager.VfxMgr != null)
            {
                // A complete baseline can replace an entire enemy deck at once.
                // Load all resulting card resources through one sequential job
                // instead of creating one loader per card.
                manager.VfxMgr.RegisterSequentialVfx<
                    Wizard.Battle.View.Vfx.VfxBase>(
                    manager.LoadCardResources(replacementsToLoad, false));
            }
        }

        private static bool CanReplacePrivateCard(
            BattlePlayerBase player,
            BattleCardBase card)
        {
            return player.HandCardList.Contains(card) ||
                player.DeckCardList.Contains(card) ||
                player.ReservedCardList.Contains(card) ||
                player.NecromanceZoneList.Contains(card);
        }

        private static BattleCardBase TryReplacePendingHiddenCard(
            NetworkBattleManagerBase manager,
            Dictionary<string, object> state,
            string key,
            BattleCardBase oldCard)
        {
            try
            {
                CardDataModel model = CreateHiddenCardDataModel(state);
                if (model == null)
                {
                    return null;
                }

                BattleCardBase replacement =
                    new ReplaceReceivedCard(manager, model)
                        .ReplaceCard(manager.BattleEnemy);
                if (replacement == null)
                {
                    return null;
                }
                Plugin.Logger.LogDebug(
                    $"[P2P] Replayed deferred hidden state idx={oldCard.Index}, " +
                    $"cardId={replacement.CardId} after the card entered its zone.");
                return replacement;
            }
            catch (Exception ex)
            {
                // Keep the entry pending. It can be retried after the next action
                // instead of losing the authoritative state permanently.
                AppliedReceivedHiddenCardStates.Remove(key);
                Plugin.Logger.LogDebug(
                    $"[P2P] Could not replay deferred hidden state idx={oldCard.Index}: " +
                    ex.Message);
                return null;
            }
        }

        private static CardDataModel CreateHiddenCardDataModel(
            Dictionary<string, object> state)
        {
            if (!TryGetStateInt(state, "idx", out int index) || index <= 0 ||
                !TryGetStateInt(state, "cardId", out int cardId) || cardId <= 0)
            {
                return null;
            }

            CardDataModel model = new CardDataModel
            {
                Index = index,
                CardId = cardId,
                isOpponent = true
            };
            if (TryGetStateInt(state, "cost", out int cost))
            {
                model.playCardCost = cost;
            }
            if (TryGetStateInt(state, "spellboost", out int spellboost))
            {
                model.Spellboost = spellboost;
            }
            if (TryGetStateInt(state, "setAtk", out int attack))
            {
                model.SetAtk = attack;
            }
            if (TryGetStateInt(state, "setLife", out int life))
            {
                model.SetLife = life;
            }
            if (TryGetStateInt(state, "setChantCount", out int chantCount))
            {
                model.SetChantCount = chantCount;
            }
            if (TryGetStateInt(state, "unionburst", out int unionBurst))
            {
                model.UnionBurstCount = unionBurst;
            }
            if (TryGetStateInt(state, "skyboundArt", out int skyboundArt))
            {
                model.SkyboundArtCount = skyboundArt;
            }
            if (TryGetStateInt(state, "clan", out int clan))
            {
                model.Clan = clan;
            }
            if (state.TryGetValue("tribe", out object rawTribe))
            {
                model.Tribe = rawTribe?.ToString() ?? "NONE";
            }
            if (state.TryGetValue("attachTarget", out object rawAttach))
            {
                model.SetAttachTarget(rawAttach?.ToString() ?? string.Empty);
            }
            if (state.TryGetValue("fusion", out object rawFusion))
            {
                model.FusionIngredientList = ToIntArray(rawFusion).ToList();
            }
            return model;
        }

        private static bool TryGetStateInt(
            Dictionary<string, object> data,
            string key,
            out int value)
        {
            value = 0;
            if (data == null || !data.TryGetValue(key, out object rawValue))
            {
                return false;
            }
            try
            {
                value = Convert.ToInt32(rawValue, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static object ConvertStateValue(object value, Type targetType)
        {
            if (value == null)
            {
                return null;
            }

            Type underlyingType = Nullable.GetUnderlyingType(targetType);
            Type effectiveType = underlyingType ?? targetType;
            if (effectiveType.IsEnum)
            {
                return Enum.ToObject(effectiveType,
                    Convert.ToInt32(value, CultureInfo.InvariantCulture));
            }
            if (effectiveType == typeof(string))
            {
                return value.ToString();
            }
            if (effectiveType == typeof(bool))
            {
                if (value is string text && int.TryParse(text,
                        NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int boolValue))
                {
                    return boolValue != 0;
                }
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            return Convert.ChangeType(value, effectiveType,
                CultureInfo.InvariantCulture);
        }

        private static bool IsSimpleStateType(Type type)
        {
            Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
            return effectiveType.IsEnum || effectiveType == typeof(string) ||
                effectiveType == typeof(decimal) ||
                effectiveType.IsPrimitive;
        }

        private static bool TryFindBackingField(
            Type type,
            string propertyName,
            out FieldInfo field)
        {
            return TryFindInstanceField(type,
                "<" + propertyName + ">k__BackingField", out field);
        }

        private static bool TryFindInstanceProperty(
            Type type,
            string propertyName,
            out PropertyInfo property)
        {
            property = null;
            for (Type current = type; current != null; current = current.BaseType)
            {
                property = current.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindInstanceField(
            Type type,
            string fieldName,
            out FieldInfo field)
        {
            field = null;
            for (Type current = type; current != null; current = current.BaseType)
            {
                field = current.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static int ReadPrivateIntField(
            object target,
            string fieldName,
            int fallback)
        {
            if (target == null || !TryFindInstanceField(target.GetType(), fieldName,
                    out FieldInfo field))
            {
                return fallback;
            }
            try
            {
                return Convert.ToInt32(field.GetValue(target),
                    CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static void CacheLocalBattleCardIdentities()
        {
            if (!IsActive || !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager) ||
                manager.BattlePlayer == null)
            {
                return;
            }

            try
            {
                foreach (BattleCardBase card in manager.BattlePlayer.AllCards)
                {
                    if (card != null)
                    {
                        BattleCardTracker.RememberSourceCard(
                            Role == P2PRole.Host,
                            card.Index,
                            card.CardId,
                            card.Cost);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug(
                    "[P2P] Could not refresh the local card identity cache: " +
                    ex.Message);
            }
        }

        private static void ObserveLocalHiddenCardStates()
        {
            if (!IsActive ||
                IsPrivateStateSyncActive ||
                !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager) ||
                manager.BattlePlayer == null)
            {
                return;
            }

            try
            {
                foreach (BattleCardBase card in EnumeratePrivateCards(
                    manager.BattlePlayer))
                {
                    if (card == null || card.Index <= 0 || card.CardId <= 0)
                    {
                        continue;
                    }
                    LocalHiddenCardStates[card.Index] =
                        P2PJson.CloneDictionary(CreateHiddenCardState(card));
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug(
                    "[P2P] Could not observe local hidden card state: " +
                    ex.Message);
            }
        }

        private static void AppendLocalHiddenCardState(
            string uri,
            Dictionary<string, object> data)
        {
            if (data == null || string.Equals(uri, P2PBattleProtocol.EchoUri,
                    StringComparison.Ordinal) ||
                !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager) ||
                manager.BattlePlayer == null)
            {
                return;
            }

            // The initial private_state message establishes the complete baseline.
            // Normal operations only inspect cards referenced by that operation;
            // checkpoints scan every private card as a safety net for effects whose
            // native order data omitted a hidden target.
            bool sendCompleteSnapshot = LocalHiddenCardStateSignatures.Count == 0;
            bool scanAllCards = sendCompleteSnapshot ||
                P2PBattleProtocol.CarriesBattleStateCheckpoint(uri);
            HashSet<int> candidateIndices = scanAllCards
                ? null
                : CollectLocalCardIndices(data);
            Dictionary<int, BattleCardBase> presentCards =
                new Dictionary<int, BattleCardBase>();
            List<Dictionary<string, object>> changedCards =
                new List<Dictionary<string, object>>();

            try
            {
                foreach (BattleCardBase card in EnumeratePrivateCards(
                    manager.BattlePlayer))
                {
                    if (card == null || card.Index <= 0 || card.CardId <= 0)
                    {
                        continue;
                    }

                    presentCards[card.Index] = card;
                    if (!scanAllCards && !candidateIndices.Contains(card.Index))
                    {
                        continue;
                    }
                    Dictionary<string, object> state;
                    string signature;
                    try
                    {
                        state = CreateHiddenCardState(card);
                        signature = JsonConvert.SerializeObject(state,
                            P2PJson.Settings);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogDebug(
                            $"[P2P] Could not capture hidden card idx={card.Index}: " +
                            ex.Message);
                        continue;
                    }
                    LocalHiddenCardStates[card.Index] =
                        P2PJson.CloneDictionary(state);
                    if (!sendCompleteSnapshot &&
                        LocalHiddenCardStateSignatures.TryGetValue(
                            card.Index, out string previousSignature) &&
                        string.Equals(previousSignature, signature,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    LocalHiddenCardStateSignatures[card.Index] = signature;
                    changedCards.Add(state);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug(
                    "[P2P] Could not capture hidden card state: " + ex.Message);
                return;
            }

            IEnumerable<int> removalCandidates = scanAllCards
                ? LocalHiddenCardStateSignatures.Keys.ToList()
                : candidateIndices;
            List<int> removedIndices = removalCandidates
                .Where(index => LocalHiddenCardStateSignatures.ContainsKey(index) &&
                    !presentCards.ContainsKey(index))
                .Distinct()
                .OrderBy(index => index)
                .ToList();
            foreach (int index in removedIndices)
            {
                // EmitMsg runs after the local action. Use the state observed
                // before that action so a played or discarded card retains its
                // hidden buffs, attached skills, counters, and saved targets
                // while the peer performs the matching native replacement.
                if (!IsAcceleratedOrCrystallizedDeparture(data, index) &&
                    LocalHiddenCardStates.TryGetValue(
                        index, out Dictionary<string, object> departedState))
                {
                    changedCards.Add(P2PJson.CloneDictionary(departedState));
                }
                LocalHiddenCardStateSignatures.Remove(index);
                LocalHiddenCardStates.Remove(index);
            }

            if (changedCards.Count == 0 && removedIndices.Count == 0)
            {
                return;
            }

            if (changedCards.Count > 0)
            {
                List<object> knownList = GetOrCreateKnownList(data);
                foreach (Dictionary<string, object> state in changedCards)
                {
                    MergeKnownCardState(knownList, state);
                }
                data["p2pHiddenCards"] = changedCards
                    .Select(state => (object)P2PJson.CloneDictionary(state))
                    .ToList();
            }
            data["p2pHiddenOwner"] = Role == P2PRole.Host ? 1 : 0;
            if (removedIndices.Count > 0)
            {
                data["p2pHiddenRemoved"] = removedIndices
                    .Select(index => (object)index)
                    .ToList();
            }

            Plugin.Logger.LogDebug(
                $"[P2P] Attached {changedCards.Count} hidden hand/deck state " +
                $"snapshot(s) and {removedIndices.Count} tombstone(s) to {uri}" +
                $"{(scanAllCards ? " (full scan)" : string.Empty)}.");
        }

        private static HashSet<int> CollectLocalCardIndices(
            Dictionary<string, object> data)
        {
            HashSet<int> indices = new HashSet<int>();
            CollectLocalCardIndices(data, true, indices);
            return indices;
        }

        private static void CollectLocalCardIndices(
            object value,
            bool inheritedIsLocal,
            HashSet<int> indices)
        {
            if (value == null || indices == null || value is string)
            {
                return;
            }

            if (value is Dictionary<string, object> dictionary)
            {
                bool isLocal = inheritedIsLocal;
                if (dictionary.TryGetValue("isSelf", out object rawSide))
                {
                    try
                    {
                        isLocal = Convert.ToInt32(rawSide,
                            CultureInfo.InvariantCulture) != 0;
                    }
                    catch (Exception)
                    {
                    }
                }

                if (isLocal)
                {
                    AddCardIndices(dictionary, "idx", indices);
                    AddCardIndices(dictionary, "idxList", indices);
                    AddCardIndices(dictionary, "playIdx", indices);
                    AddCardIndices(dictionary, "targetIdx", indices);
                    AddCardIndices(dictionary, "ingredients", indices);
                    AddCardIndices(dictionary, "baseIdx", indices);
                    AddCardIndices(dictionary, "baseCardIdx", indices);
                    AddCardIndices(dictionary, "handIdxList", indices);
                    AddCardIndices(dictionary, "skillKeyCardIdx", indices);
                    AddCardIndices(dictionary, "randomTargetIdx", indices);
                    AddCardIndices(dictionary, "hasGuard", indices);
                }

                foreach (KeyValuePair<string, object> field in dictionary)
                {
                    CollectLocalCardIndices(field.Value, isLocal, indices);
                }
                return;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    CollectLocalCardIndices(item, inheritedIsLocal, indices);
                }
            }
        }

        private static void AddCardIndices(
            Dictionary<string, object> data,
            string key,
            HashSet<int> indices)
        {
            if (!data.TryGetValue(key, out object rawIndices) ||
                rawIndices == null || rawIndices is string)
            {
                if (TryGetStateInt(data, key, out int singleIndex) &&
                    singleIndex > 0)
                {
                    indices.Add(singleIndex);
                }
                return;
            }

            if (rawIndices is IEnumerable values)
            {
                foreach (object rawIndex in values)
                {
                    try
                    {
                        int index = Convert.ToInt32(rawIndex,
                            CultureInfo.InvariantCulture);
                        if (index > 0)
                        {
                            indices.Add(index);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                return;
            }

            if (TryGetStateInt(data, key, out int indexValue) && indexValue > 0)
            {
                indices.Add(indexValue);
            }
        }

        private static bool IsAcceleratedOrCrystallizedDeparture(
            Dictionary<string, object> data,
            int index)
        {
            if (!TryGetStateInt(data, "playIdx", out int playIndex) ||
                playIndex != index ||
                !data.TryGetValue("keyAction", out object rawActions) ||
                rawActions is string || !(rawActions is IEnumerable actions))
            {
                return false;
            }

            foreach (object rawAction in actions)
            {
                if (rawAction is Dictionary<string, object> action &&
                    TryGetStateInt(action, "type", out int type) &&
                    (type == (int)SendKeyActionDataManager.KeyActionType.Accelerated ||
                     type == (int)SendKeyActionDataManager.KeyActionType.Crystallize))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AppendLocalPlayerHistoryState(
            string uri,
            Dictionary<string, object> data)
        {
            if (data == null || string.Equals(uri, P2PBattleProtocol.EchoUri,
                    StringComparison.Ordinal) ||
                (IsPrivateStateSyncActive &&
                    !P2PBattleProtocol.CarriesBattleStateCheckpoint(uri)) ||
                !(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager) ||
                manager.BattlePlayer == null)
            {
                return;
            }

            try
            {
                bool ownerIsHost = Role == P2PRole.Host;
                Dictionary<string, object> state =
                    CapturePlayerHistoryState(manager.BattlePlayer, ownerIsHost);
                string signature = JsonConvert.SerializeObject(
                    state, P2PJson.Settings);
                if (string.Equals(signature, localPlayerHistoryStateSignature,
                        StringComparison.Ordinal))
                {
                    return;
                }

                localPlayerHistoryStateSignature = signature;
                state["revision"] = ++localPlayerHistoryRevision;
                data[PlayerHistoryStateKey] = state;
                Plugin.Logger.LogDebug(
                    $"[P2P] Attached player history revision " +
                    $"{localPlayerHistoryRevision} to {uri}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug(
                    "[P2P] Could not capture player history state: " + ex.Message);
            }
        }

        private static Dictionary<string, object> CapturePlayerHistoryState(
            BattlePlayerBase player,
            bool ownerIsHost)
        {
            Dictionary<string, object> scalars =
                new Dictionary<string, object>();
            foreach (string propertyName in PlayerHistoryScalarNames)
            {
                if (!TryFindInstanceProperty(player.GetType(), propertyName,
                        out PropertyInfo property) ||
                    !IsSimpleStateType(property.PropertyType))
                {
                    continue;
                }
                try
                {
                    scalars[propertyName] = property.GetValue(player, null);
                }
                catch (Exception)
                {
                }
            }

            Dictionary<string, object> lists =
                new Dictionary<string, object>();
            foreach (string propertyName in PlayerHistoryListNames)
            {
                if (!TryGetPlayerHistoryListMember(
                        player, propertyName, out Type listType,
                        out object listValue) ||
                    !listType.IsGenericType ||
                    listType.GetGenericTypeDefinition() != typeof(List<>))
                {
                    continue;
                }
                try
                {
                    Type elementType = listType.GetGenericArguments()[0];
                    object captured = CapturePlayerHistoryList(
                        listValue, elementType, ownerIsHost);
                    if (captured != null)
                    {
                        lists[propertyName] = captured;
                    }
                }
                catch (Exception)
                {
                }
            }

            return new Dictionary<string, object>
            {
                ["owner"] = ownerIsHost ? 1 : 0,
                ["scalars"] = scalars,
                ["lists"] = lists,
                ["class"] = player.Class == null
                    ? new Dictionary<string, object>()
                    : CreateHiddenCardState(player.Class)
            };
        }

        private static object CapturePlayerHistoryList(
            object rawList,
            Type elementType,
            bool defaultOwnerIsHost)
        {
            if (!(rawList is IEnumerable values))
            {
                return new List<object>();
            }

            List<object> result = new List<object>();
            if (elementType == typeof(BattleCardBase))
            {
                foreach (object value in values)
                {
                    result.Add(CapturePlayerCardReference(
                        value as BattleCardBase, defaultOwnerIsHost));
                }
                return result;
            }
            if (elementType == typeof(BattlePlayerBase.TurnAndCard))
            {
                foreach (BattlePlayerBase.TurnAndCard value in values)
                {
                    if (value == null)
                    {
                        continue;
                    }
                    result.Add(new Dictionary<string, object>
                    {
                        ["card"] = CapturePlayerCardReference(
                            value.Card as BattleCardBase, defaultOwnerIsHost),
                        ["turn"] = value.Turn,
                        ["turnOwner"] = GetAbsoluteTurnOwner(value.IsSelfTurn),
                        ["end"] = value.IsTurnEnd ? 1 : 0
                    });
                }
                return result;
            }
            if (elementType == typeof(BattlePlayerBase.CardAndTribe))
            {
                foreach (BattlePlayerBase.CardAndTribe value in values)
                {
                    if (value == null)
                    {
                        continue;
                    }
                    result.Add(new Dictionary<string, object>
                    {
                        ["card"] = CapturePlayerCardReference(
                            value.Card as BattleCardBase, defaultOwnerIsHost),
                        ["tribes"] = value.Tribes == null
                            ? new List<object>()
                            : value.Tribes.Select(tribe => (object)(int)tribe)
                                .ToList()
                    });
                }
                return result;
            }
            if (elementType == typeof(BattlePlayerBase.CardAndId))
            {
                foreach (BattlePlayerBase.CardAndId value in values)
                {
                    if (value == null)
                    {
                        continue;
                    }
                    result.Add(new Dictionary<string, object>
                    {
                        ["card"] = CapturePlayerCardReference(
                            value.Card as BattleCardBase, defaultOwnerIsHost),
                        ["id"] = value.Id
                    });
                }
                return result;
            }
            if (elementType == typeof(BattlePlayerBase.CardAndValue))
            {
                foreach (BattlePlayerBase.CardAndValue value in values)
                {
                    if (value == null)
                    {
                        continue;
                    }
                    result.Add(new Dictionary<string, object>
                    {
                        ["card"] = CapturePlayerCardReference(
                            value.Card as BattleCardBase, defaultOwnerIsHost),
                        ["value"] = value.Value
                    });
                }
                return result;
            }
            if (elementType == typeof(TurnAndIntValue))
            {
                foreach (TurnAndIntValue value in values)
                {
                    if (value == null)
                    {
                        continue;
                    }
                    result.Add(new Dictionary<string, object>
                    {
                        ["value"] = value.Value,
                        ["turn"] = value.Turn,
                        ["turnOwner"] = GetAbsoluteTurnOwner(value.IsSelfTurn)
                    });
                }
                return result;
            }
            if (elementType == typeof(int))
            {
                foreach (object value in values)
                {
                    result.Add(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                }
                return result;
            }
            if (elementType == typeof(List<BattleCardBase>))
            {
                foreach (object value in values)
                {
                    List<object> nested = new List<object>();
                    if (value is IEnumerable cards)
                    {
                        foreach (object card in cards)
                        {
                            nested.Add(CapturePlayerCardReference(
                                card as BattleCardBase, defaultOwnerIsHost));
                        }
                    }
                    result.Add(nested);
                }
                return result;
            }
            return null;
        }

        private static int GetAbsoluteTurnOwner(bool isLocalPlayerTurn)
        {
            bool localOwnerIsHost = Role == P2PRole.Host;
            bool turnOwnerIsHost = isLocalPlayerTurn
                ? localOwnerIsHost
                : !localOwnerIsHost;
            return turnOwnerIsHost ? 1 : 0;
        }

        private static Dictionary<string, object> CapturePlayerCardReference(
            BattleCardBase card,
            bool defaultOwnerIsHost)
        {
            bool ownerIsHost = defaultOwnerIsHost;
            if (card != null)
            {
                bool localOwnerIsHost = Role == P2PRole.Host;
                ownerIsHost = card.IsPlayer
                    ? localOwnerIsHost
                    : !localOwnerIsHost;
            }
            return new Dictionary<string, object>
            {
                ["idx"] = card?.Index ?? 0,
                ["cardId"] = card?.CardId ?? 0,
                ["owner"] = ownerIsHost ? 1 : 0
            };
        }

        private static IEnumerable<BattleCardBase> EnumeratePrivateCards(
            BattlePlayerBase player)
        {
            HashSet<int> seen = new HashSet<int>();
            IEnumerable<IEnumerable<BattleCardBase>> zones =
                new IEnumerable<BattleCardBase>[]
                {
                    player.HandCardList,
                    player.DeckCardList,
                    player.FusionIngredientList,
                    player.ReservedCardList,
                    // Necromance cards are not always public in the local
                    // network representation, but the original replacement
                    // path can resolve this zone by index as well.
                    player.NecromanceZoneList
                };
            foreach (IEnumerable<BattleCardBase> zone in zones)
            {
                if (zone == null)
                {
                    continue;
                }
                foreach (BattleCardBase card in zone)
                {
                    if (card != null && seen.Add(card.Index))
                    {
                        yield return card;
                    }
                }
            }
        }

        private static Dictionary<string, object> CreateHiddenCardState(
            BattleCardBase card)
        {
            Dictionary<string, object> state = new Dictionary<string, object>
            {
                ["idx"] = card.Index,
                ["cardId"] = card.CardId,
                ["isSelf"] = 1,
                ["cost"] = card.Cost,
                ["spellboost"] = card.SpellChargeCount
            };

            SkillApplyInformation skillInformation =
                card.SkillApplyInformation as SkillApplyInformation;
            state["p2pCardPrimitive"] = CaptureSimpleBackingProperties(
                card, HiddenCardPrimitiveExclusions);
            state["p2pSkillPrimitive"] = CaptureSimpleBackingProperties(
                skillInformation, HiddenSkillPrimitiveExclusions);
            state["p2pLifeState"] = new Dictionary<string, object>
            {
                ["life"] = card.Life,
                ["maxLife"] = card.MaxLife
            };
            state["p2pDamagedCounter"] = new Dictionary<string, object>
            {
                ["selfTurn"] = card.DamagedCounter?.SelfTurnDamage ?? 0,
                ["opponentTurn"] =
                    card.DamagedCounter?.OpponentTurnDamage ?? 0
            };
            state["p2pModifiers"] = CaptureCardModifierState(
                card, skillInformation);
            state["p2pSkillActivationIds"] = card.SkillActivationList == null
                ? new List<object>()
                : card.SkillActivationList
                    .Select(value => (object)value.SkillId)
                    .ToList();
            state["p2pMaxAttackableCount"] = card.MaxAttackableCount;
            state["p2pSkillActivatedCount"] = card.SkillActivatedCount;
            state["p2pSkillActivatedWrap"] = ReadPrivateIntField(
                card, "_skillActivatedCountWrapValue", -1);
            state["p2pSkillRandomArrayPresent"] =
                skillInformation?.SkillRandomArray != null ? 1 : 0;
            if (skillInformation?.SkillRandomArray != null)
            {
                state["p2pSkillRandomArray"] = skillInformation.SkillRandomArray
                    .Select(value => (object)value).ToList();
            }
            state["p2pGenericArrayPresent"] =
                skillInformation?.SkillGenericValueArray != null ? 1 : 0;
            if (skillInformation?.SkillGenericValueArray != null)
            {
                state["p2pGenericArray"] = skillInformation.SkillGenericValueArray
                    .Select(value => (object)value).ToList();
            }
            Dictionary<string, object> genericKeys = new Dictionary<string, object>();
            if (skillInformation?.SkillGenericKeyAndValue != null)
            {
                foreach (KeyValuePair<string, int> key in
                    skillInformation.SkillGenericKeyAndValue)
                {
                    genericKeys[key.Key] = key.Value;
                }
            }
            state["p2pGenericKeys"] = genericKeys;
            if (skillInformation != null)
            {
                state["p2pIntLists"] = new Dictionary<string, object>
                {
                    ["cantAtkBaseIds"] = ToObjectList(
                        skillInformation.CantAtkUnitBaseCardIdList),
                    ["decreaseTurnStartPP"] = ToObjectList(
                        skillInformation.DecreaseTurnStartPPList),
                    ["cantEvolution"] = ToObjectList(
                        skillInformation.CantEvolutionList),
                    ["skillHeal"] = ToObjectList(
                        skillInformation.SkillHealList)
                };
                state["p2pSkillCollections"] =
                    new Dictionary<string, object>
                    {
                        ["turnBuff"] = skillInformation.TurnBuffCountList
                            .Where(value => value != null)
                            .Select(value => (object)new Dictionary<string, object>
                            {
                                ["turn"] = value.Turn,
                                ["turnOwner"] =
                                    GetAbsoluteTurnOwner(value.IsSelfTurn)
                            }).ToList(),
                        ["tokenDraw"] = skillInformation.TokenDrawModifiers
                            .Where(value => value != null)
                            .Select(value => (object)new Dictionary<string, object>
                            {
                                ["cardId"] = value.CardId,
                                ["count"] = value.MultiplyCount
                            }).ToList(),
                        ["lifeHistory"] = CaptureLifeHistory(
                            skillInformation.LifeModifierList),
                        ["causedDamage"] = CaptureTurnValueCollection(
                            skillInformation.CausedDamageModifierList),
                        ["ppAdd"] = CaptureTurnValueCollection(
                            skillInformation.PpAddList)
                    };
                state["p2pCardReferences"] = new Dictionary<string, object>
                {
                    ["randomSelected"] = CaptureCardReferences(
                        skillInformation.RandomSelectedCardList),
                    ["skillDrew"] = CaptureCardReferences(
                        skillInformation.SkillDrewCardList),
                    ["savedTargets"] = CaptureCardReferences(
                        skillInformation.SavedTargetList),
                    ["savedBurialTargets"] = CaptureCardReferences(
                        skillInformation.SavedBurialRiteTargetList),
                    ["lastBurialTargets"] = CaptureCardReferences(
                        skillInformation.LastBurialRiteCardList),
                    ["getOn"] = CaptureCardReferences(
                        skillInformation.GetOnCards),
                    ["getOff"] = CaptureCardReferences(card.GetOffCards)
                };
                Dictionary<string, object> savedTargetIds =
                    new Dictionary<string, object>();
                foreach (KeyValuePair<long, List<int>> saved in
                    skillInformation.SavedTargetCardIdDict.OrderBy(
                        value => value.Key))
                {
                    savedTargetIds[saved.Key.ToString(
                        CultureInfo.InvariantCulture)] = ToObjectList(saved.Value);
                }
                state["p2pSavedTargetIds"] = savedTargetIds;
                state["p2pPreprocess"] = new Dictionary<string, object>
                {
                    ["normal"] = CapturePreprocessCollection(card.NormalSkills),
                    ["evolution"] = CapturePreprocessCollection(
                        card.EvolutionSkills)
                };
            }
            if (skillInformation != null)
            {
                state["p2pUnionBurstCount"] = skillInformation.UnionBurstCount;
                state["p2pSkyboundArtCount"] = skillInformation.SkyboundArtCount;
                state["p2pSuperSkyboundArtCount"] =
                    skillInformation.SuperSkyboundArtCount;

                List<object> fusionState = new List<object>();
                if (skillInformation.FusionIngredients != null)
                {
                    foreach (FusionIngredientInfo ingredient in
                        skillInformation.FusionIngredients)
                    {
                        if (ingredient?.Card == null || ingredient.Card.Index <= 0)
                        {
                            continue;
                        }
                        fusionState.Add(new Dictionary<string, object>
                        {
                            ["idx"] = ingredient.Card.Index,
                            ["cardId"] = ingredient.Card.CardId,
                            ["turn"] = ingredient.FusionTurn
                        });
                    }
                }
                state["p2pFusion"] = fusionState;
            }
            else
            {
                state["p2pGenericKeys"] = genericKeys;
                state["p2pUnionBurstCount"] = 10;
                state["p2pSkyboundArtCount"] = 10;
                state["p2pSuperSkyboundArtCount"] = 15;
                state["p2pFusion"] = new List<object>();
            }

            CardParameter baseParameter = card.BaseParameter;
            if (baseParameter == null)
            {
                return state;
            }

            // Use absolute values for the few card properties accepted by
            // CardDataModel.  This makes repeated P2P snapshots idempotent even
            // when the original modifier was an add/half/temporary modifier.
            if (card.IsUnit && card.Atk != card.BaseAtk)
            {
                state["setAtk"] = card.Atk;
            }
            if (card.IsUnit && card.MaxLife != card.BaseMaxLife)
            {
                state["setLife"] = card.MaxLife;
            }
            if (card.ChantCount != baseParameter.ChantCount)
            {
                state["setChantCount"] = card.ChantCount;
            }
            // ReplaceReceivedCard interprets these two wire values as the
            // reduction from the built-in defaults, rather than the remaining
            // count.  Sending the current count here would apply the modifier
            // twice and make hand/deck condition checks disagree.
            if (card.HasUnionBurst &&
                card.SkillApplyInformation != null &&
                card.SkillApplyInformation.UnionBurstCount != 10)
            {
                state["unionburst"] = 10 -
                    card.SkillApplyInformation.UnionBurstCount;
            }
            if (card.HasSkyboundArt &&
                card.SkillApplyInformation != null &&
                card.SkillApplyInformation.SkyboundArtCount != 10)
            {
                state["skyboundArt"] = 10 -
                    card.SkillApplyInformation.SkyboundArtCount;
            }
            if (card.Clan != baseParameter.Clan)
            {
                state["clan"] = (int)card.Clan;
            }
            if (card.Tribe != null && baseParameter.Tribe != null &&
                !card.Tribe.SequenceEqual(baseParameter.Tribe))
            {
                state["tribe"] = string.Join(",", card.Tribe.Select(
                    tribe => tribe.ToString()));
            }

            string attachedSkills = GetAttachedSkillState(card);
            if (!string.IsNullOrEmpty(attachedSkills))
            {
                state["attachTarget"] = attachedSkills;
            }

            List<BattleCardBase> fusionIngredients = card.FusionIngredients;
            if (fusionIngredients != null && fusionIngredients.Count > 0)
            {
                state["fusion"] = fusionIngredients
                    .Where(ingredient => ingredient != null && ingredient.Index > 0)
                    .Select(ingredient => (object)ingredient.Index)
                    .ToList();
            }
            return state;
        }

        private static Dictionary<string, object> CaptureCardModifierState(
            BattleCardBase card,
            SkillApplyInformation information)
        {
            return new Dictionary<string, object>
            {
                ["offense"] = CaptureOffenseModifiers(
                    information?.OffenseModifierList),
                ["life"] = CaptureLifeModifiers(
                    information?.LifeModifierList),
                ["cost"] = CaptureCostModifiers(card?.CostModifierList),
                ["chant"] = CaptureChantCountModifiers(
                    information?.ChantCountModifierList)
            };
        }

        private static List<object> CaptureOffenseModifiers(
            IEnumerable<ICardOffenseModifier> modifiers)
        {
            List<object> result = new List<object>();
            if (modifiers == null)
            {
                return result;
            }
            foreach (ICardOffenseModifier modifier in modifiers)
            {
                if (modifier is OffenseAddModifier add)
                {
                    result.Add(CaptureModifier("add", add.Offense));
                }
                else if (modifier is OffenseSetModifier set)
                {
                    result.Add(CaptureModifier("set", set.Offense));
                }
                else if (modifier is OffenseMultiplyModifier multiply)
                {
                    result.Add(CaptureModifier("multiply", multiply.Multipli));
                }
            }
            return result;
        }

        private static List<object> CaptureLifeModifiers(
            IEnumerable<ICardLifeModifier> modifiers)
        {
            List<object> result = new List<object>();
            if (modifiers == null)
            {
                return result;
            }
            foreach (ICardLifeModifier modifier in modifiers)
            {
                Dictionary<string, object> captured = null;
                if (modifier is LifeAddModifier add)
                {
                    captured = CaptureModifier("add", add.Life);
                }
                else if (modifier is LifeSetModifier set)
                {
                    captured = CaptureModifier("set", set.Life);
                }
                else if (modifier is LifeMultiplyModifier multiply)
                {
                    captured = CaptureModifier("multiply", multiply.Multipli);
                }
                else if (modifier is DamageCardParameterModifier damage)
                {
                    captured = CaptureTurnModifier("damage", damage);
                }
                else if (modifier is HealCardParameterModifier heal)
                {
                    captured = CaptureTurnModifier("heal", heal);
                }
                if (captured != null)
                {
                    result.Add(captured);
                }
            }
            return result;
        }

        private static List<object> CaptureCostModifiers(
            IEnumerable<ICardCostModifier> modifiers)
        {
            List<object> result = new List<object>();
            if (modifiers == null)
            {
                return result;
            }
            foreach (ICardCostModifier modifier in modifiers)
            {
                Dictionary<string, object> captured = null;
                if (modifier is CostAddModifier add)
                {
                    captured = CaptureModifier("add", add.Cost);
                }
                else if (modifier is CostSetModifier set)
                {
                    captured = CaptureModifier("set", set.Cost);
                }
                else if (modifier is CostHalfRoundUpModifier)
                {
                    captured = new Dictionary<string, object>
                    {
                        ["kind"] = "halfUp"
                    };
                }
                else if (modifier is CostHalfRoundDownModifier)
                {
                    captured = new Dictionary<string, object>
                    {
                        ["kind"] = "halfDown"
                    };
                }
                if (captured != null)
                {
                    captured["resident"] = modifier.IsResidentModifier ? 1 : 0;
                    result.Add(captured);
                }
            }
            return result;
        }

        private static List<object> CaptureChantCountModifiers(
            IEnumerable<ICardChantCountModifier> modifiers)
        {
            List<object> result = new List<object>();
            if (modifiers == null)
            {
                return result;
            }
            foreach (ICardChantCountModifier modifier in modifiers)
            {
                if (modifier is ChantCountAddModifier add)
                {
                    result.Add(CaptureModifier("add", add.ChantCount));
                }
                else if (modifier is ChantCountSetModifier set)
                {
                    result.Add(CaptureModifier("set", set.ChantCount));
                }
            }
            return result;
        }

        private static Dictionary<string, object> CaptureModifier(
            string kind,
            int value)
        {
            return new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["value"] = value
            };
        }

        private static Dictionary<string, object> CaptureTurnModifier(
            string kind,
            TurnAndIntValue value)
        {
            Dictionary<string, object> result = CaptureModifier(kind, value.Value);
            result["turn"] = value.Turn;
            result["turnOwner"] = GetAbsoluteTurnOwner(value.IsSelfTurn);
            return result;
        }

        private static List<object> CaptureLifeHistory(
            IEnumerable<ICardLifeModifier> modifiers)
        {
            List<object> result = new List<object>();
            if (modifiers == null)
            {
                return result;
            }

            foreach (ICardLifeModifier modifier in modifiers)
            {
                string kind;
                TurnAndIntValue value;
                if (modifier is DamageCardParameterModifier damage)
                {
                    kind = "damage";
                    value = damage;
                }
                else if (modifier is HealCardParameterModifier heal)
                {
                    kind = "heal";
                    value = heal;
                }
                else
                {
                    continue;
                }
                result.Add(new Dictionary<string, object>
                {
                    ["kind"] = kind,
                    ["value"] = value.Value,
                    ["turn"] = value.Turn,
                    ["turnOwner"] = GetAbsoluteTurnOwner(value.IsSelfTurn)
                });
            }
            return result;
        }

        private static List<object> CaptureTurnValueCollection(
            IEnumerable<TurnAndIntValue> values)
        {
            return values == null
                ? new List<object>()
                : values.Where(value => value != null)
                    .Select(value => (object)new Dictionary<string, object>
                    {
                        ["value"] = value.Value,
                        ["turn"] = value.Turn,
                        ["turnOwner"] = GetAbsoluteTurnOwner(value.IsSelfTurn)
                    }).ToList();
        }

        private static List<object> ToObjectList(IEnumerable<int> values)
        {
            return values == null
                ? new List<object>()
                : values.Select(value => (object)value).ToList();
        }

        private static List<object> CaptureCardReferences(
            IEnumerable<BattleCardBase> cards)
        {
            List<object> result = new List<object>();
            if (cards == null)
            {
                return result;
            }

            bool localOwnerIsHost = Role == P2PRole.Host;
            foreach (BattleCardBase referencedCard in cards)
            {
                if (referencedCard == null || referencedCard.Index <= 0)
                {
                    continue;
                }
                bool ownerIsHost = referencedCard.IsPlayer
                    ? localOwnerIsHost
                    : !localOwnerIsHost;
                result.Add(new Dictionary<string, object>
                {
                    ["idx"] = referencedCard.Index,
                    ["cardId"] = referencedCard.CardId,
                    ["owner"] = ownerIsHost ? 1 : 0
                });
            }
            return result;
        }

        private static List<object> CapturePreprocessCollection(
            IEnumerable<SkillBase> skills)
        {
            List<object> result = new List<object>();
            if (skills == null)
            {
                return result;
            }

            foreach (SkillBase skill in skills)
            {
                if (skill == null)
                {
                    result.Add(new Dictionary<string, object>());
                    continue;
                }

                List<object> items = new List<object>();
                foreach (SkillPreprocessBase preprocess in
                    skill.PreprocessList ?? new List<SkillPreprocessBase>())
                {
                    if (preprocess == null)
                    {
                        items.Add(new Dictionary<string, object>());
                        continue;
                    }
                    items.Add(new Dictionary<string, object>
                    {
                        ["type"] = preprocess.GetType().FullName,
                        ["fields"] = CaptureMutableSimpleFields(preprocess)
                    });
                }
                result.Add(new Dictionary<string, object>
                {
                    ["type"] = skill.GetType().FullName,
                    ["items"] = items
                });
            }
            return result;
        }

        private static Dictionary<string, object> CaptureMutableSimpleFields(
            object target)
        {
            Dictionary<string, object> result =
                new Dictionary<string, object>();
            if (target == null)
            {
                return result;
            }

            for (Type current = target.GetType(); current != null;
                current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .OrderBy(field => field.Name, StringComparer.Ordinal))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral ||
                        !IsSimpleStateType(field.FieldType) ||
                        result.ContainsKey(field.Name))
                    {
                        continue;
                    }
                    try
                    {
                        result[field.Name] = field.GetValue(target);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            return result;
        }

        private static Dictionary<string, object> CaptureSimpleBackingProperties(
            object target,
            HashSet<string> excludedNames)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            if (target == null)
            {
                return result;
            }

            foreach (PropertyInfo property in target.GetType().GetProperties(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length > 0 ||
                    excludedNames.Contains(property.Name) ||
                    !IsSimpleStateType(property.PropertyType) ||
                    !TryFindBackingField(target.GetType(), property.Name,
                        out FieldInfo field) || field.IsStatic || field.IsInitOnly ||
                    field.IsLiteral)
                {
                    continue;
                }

                try
                {
                    result[property.Name] = field.GetValue(target);
                }
                catch (Exception)
                {
                }
            }
            return result;
        }

        private static readonly HashSet<string> HiddenCardPrimitiveExclusions =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "CardId",
                "IsPlayer",
                "IsFirstTurn",
                "IsOnMove",
                "IsSelfTurn",
                "IsTokenLoad",
                "NormalIndividualId",
                "EvolutionIndividualId",
                "BaseAtk",
                "BaseCost",
                "BaseMaxLife",
                "Atk",
                "Cost",
                "Life",
                "MaxLife",
                "SpellChargeCount",
                "ChantCount",
                "GenericValueArray"
            };

        private static readonly HashSet<string> HiddenSkillPrimitiveExclusions =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Player",
                "Enemy",
                "SkillGenericValueArray",
                "SkillGenericKeyAndValue",
                "UnionBurstCount",
                "SkyboundArtCount",
                "SuperSkyboundArtCount",
                "UnionBurstCountModifierList",
                "SkyboundArtCountModifierList",
                "SuperSkyboundArtCountModifierList",
                "FusionIngredients",
                "AttachedSkillsInfo",
                "GetOnCards"
            };

        private static string GetAttachedSkillState(BattleCardBase card)
        {
            try
            {
                AttachedSkillInformation attached = card.SkillApplyInformation?
                    .AttachedSkillsInfo;
                if (attached?.CreatorSkillList == null)
                {
                    return string.Empty;
                }

                List<string> publishedSkills = new List<string>();
                for (int i = 0; i < attached.CreatorSkillList.Count; i++)
                {
                    SkillBase skill = attached.CreatorSkillList[i];
                    if (skill == null)
                    {
                        continue;
                    }
                    int count = NetworkBattleGenericTool.GetPublishSkillCount(skill);
                    if (count >= 0)
                    {
                        publishedSkills.Add(count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture));
                        continue;
                    }

                    // Private creator skills do not receive a published count.
                    // ReplaceReceivedCard also accepts ownerCardId|skillIndex|evo,
                    // which lets the peer reconstruct these attachments directly.
                    if (i < attached.OwnerCardIdList.Count &&
                        i < attached.CreatorSkillIndexList.Count)
                    {
                        int ownerCardId = attached.OwnerCardIdList[i];
                        int skillIndex = attached.CreatorSkillIndexList[i];
                        if (ownerCardId > 0 && skillIndex >= 0)
                        {
                            bool isEvolution = skill.SkillPrm?.ownerCard?.EvolutionSkills
                                ?.Contains(skill) == true;
                            publishedSkills.Add(string.Join("|", new[]
                            {
                                ownerCardId.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture),
                                skillIndex.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture),
                                isEvolution ? "1" : "0"
                            }));
                        }
                    }
                }
                return string.Join(",", publishedSkills);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static List<object> GetOrCreateKnownList(
            Dictionary<string, object> data)
        {
            if (data.TryGetValue("knownList", out object rawKnownList) &&
                rawKnownList is List<object> knownList)
            {
                return knownList;
            }

            List<object> result = new List<object>();
            if (rawKnownList is IEnumerable enumerable && !(rawKnownList is string))
            {
                foreach (object item in enumerable)
                {
                    result.Add(item);
                }
            }
            data["knownList"] = result;
            return result;
        }

        private static void MergeKnownCardState(
            List<object> knownList,
            Dictionary<string, object> state)
        {
            int index = Convert.ToInt32(state["idx"]);
            foreach (object item in knownList)
            {
                if (!(item is Dictionary<string, object> known) ||
                    !IsSelfKnownCard(known) || !KnownCardContainsIndex(known, index))
                {
                    continue;
                }

                // Native messages may group several indices under idxList.  A
                // snapshot carries one complete card state, so keep it as a
                // separate entry instead of overwriting the group's scalar idx.
                if (known.ContainsKey("idxList") && !known.ContainsKey("idx"))
                {
                    continue;
                }

                foreach (string stateKey in HiddenCardStateKeys)
                {
                    if (!state.ContainsKey(stateKey))
                    {
                        known.Remove(stateKey);
                    }
                }
                foreach (KeyValuePair<string, object> field in state)
                {
                    known[field.Key] = field.Value;
                }
                return;
            }
            knownList.Add(state);
        }

        private static readonly string[] HiddenCardStateKeys =
        {
            "cost",
            "spellboost",
            "setAtk",
            "setLife",
            "setChantCount",
            "unionburst",
            "skyboundArt",
            "clan",
            "tribe",
            "attachTarget",
            "fusion",
            "p2pCardPrimitive",
            "p2pSkillPrimitive",
            "p2pLifeState",
            "p2pDamagedCounter",
            "p2pModifiers",
            "p2pSkillActivationIds",
            "p2pMaxAttackableCount",
            "p2pSkillActivatedCount",
            "p2pSkillActivatedWrap",
            "p2pSkillRandomArrayPresent",
            "p2pSkillRandomArray",
            "p2pGenericArrayPresent",
            "p2pGenericArray",
            "p2pGenericKeys",
            "p2pIntLists",
            "p2pSkillCollections",
            "p2pCardReferences",
            "p2pSavedTargetIds",
            "p2pPreprocess",
            "p2pUnionBurstCount",
            "p2pSkyboundArtCount",
            "p2pSuperSkyboundArtCount",
            "p2pFusion"
        };

        private static readonly string[] PlayerHistoryListNames =
        {
            "HandCardList",
            "DeckCardList",
            "BattleStartDeckCardList",
            "DeckSkillCardList",
            "FusionIngredientList",
            "TurnFusionCards",
            "NecromanceZoneList",
            "DiscardedCardList",
            "FusionIngredientAndDiscardedCardList",
            "ReservedCardList",
            "UniteList",
            "GetOnList",
            "BlackHole",
            "ChoiceBraveCardList",
            "PredictionCemeteryRandomCards",
            "PredictionDamageRandomCards",
            "PredictionBanishRandomCards",
            "ReturnList",
            "LastTargetCardsList",
            "InHandCards",
            "SkillDiscards",
            "SelfDiscardList",
            "SkillBanishCards",
            "HealingCards",
            "SkillSummonedCards",
            "SummonedCards",
            "EvolvedCards",
            "DestroyedWhenDestroyCards",
            "TurnPlayCardCountInfo",
            "TurnFusionCountInfo",
            "TurnEvolveCardCountInfo",
            "TurnPlayCards",
            "TurnDrawCards",
            "TurnDrawTokenCardsWithId",
            "GameDrawCards",
            "GameDrawTokenCards",
            "GameAddUpdateDeckCards",
            "GameSummonCards",
            "GameSummonMomentTribe",
            "GamePlayMomentTribe",
            "GamePlayMomentSpellChargeCards",
            "GameUpdateDeckMomentTribe",
            "GamePlayCards",
            "GameTurnPlayCards",
            "GameEnhancePlayCards",
            "GameCrystallizedPlayCards",
            "GameLeftCards",
            "GameTurnLeftCards",
            "GameReturnedCards",
            "GameSuperSkyboundArtCards",
            "GameInplayMetamorphoseCards",
            "TurnDestroyCards",
            "TurnWhenHealingCount",
            "GameBurialRiteCards",
            "TurnBurialRiteCards",
            "BurialRiteOrDiscardCardHandIndexList",
            "GameReanimatedCards",
            "AddToDeckCardList",
            "TurnStartLifeList",
            "GameSkillReturnCardCountList",
            "GameSkillDiscardCountList",
            "GameSkillBuffCountList",
            "GameSkillMetamorphoseCountList",
            "GameQuickAttackCards"
        };

        private static readonly HashSet<string> PlayerHistoryListNameSet =
            new HashSet<string>(PlayerHistoryListNames, StringComparer.Ordinal);

        private static readonly string[] PlayerHistoryScalarNames =
        {
            "Pp",
            "PpTotal",
            "Bp",
            "EpTotal",
            "CurrentEpCount",
            "EvolveWaitTurnCount",
            "NowTurnEvol",
            "IsEpEvolveThisTurn",
            "GameUsedEpCount",
            "TurnUsedEpCount",
            "IsAlreadyChoiceBraveInThisTurn",
            "IsChoiceBraveEffectTiming",
            "TurnNecromanceCount",
            "GameNecromanceCount",
            "GameUsedPpCount",
            "RallyCount",
            "DeckBanishCount",
            "GameResonanceStartCount",
            "TurnResonanceStartCount",
            "GameUsedWhiteRitualCount",
            "LastInplayWhiteRitualStack",
            "GameSkillDiscardCount",
            "IsShortageDeck",
            "IsShortageDeckLose",
            "extraTurnCount",
            "cardTotalNum",
            "_cumulativeEvolutionCount"
        };

        private static readonly HashSet<string> PlayerHistoryScalarNameSet =
            new HashSet<string>(PlayerHistoryScalarNames, StringComparer.Ordinal);

        private static bool TryGetPlayerHistoryListMember(
            BattlePlayerBase player,
            string name,
            out Type listType,
            out object value)
        {
            listType = null;
            value = null;
            if (player == null || !PlayerHistoryListNameSet.Contains(name))
            {
                return false;
            }
            try
            {
                if (TryFindInstanceProperty(player.GetType(), name,
                        out PropertyInfo property))
                {
                    listType = property.PropertyType;
                    value = property.GetValue(player, null);
                    return true;
                }
                if (TryFindInstanceField(player.GetType(), name,
                        out FieldInfo field))
                {
                    listType = field.FieldType;
                    value = field.GetValue(player);
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static bool IsSelfKnownCard(Dictionary<string, object> known)
        {
            if (known == null || !known.TryGetValue("isSelf", out object rawSelf))
            {
                return false;
            }
            try
            {
                return Convert.ToInt32(rawSelf) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool KnownCardContainsIndex(
            Dictionary<string, object> known,
            int index)
        {
            if (known.TryGetValue("idx", out object rawIndex))
            {
                try
                {
                    return Convert.ToInt32(rawIndex) == index;
                }
                catch (Exception)
                {
                }
            }
            if (!known.TryGetValue("idxList", out object rawIndices) ||
                rawIndices is string || !(rawIndices is IEnumerable enumerable))
            {
                return false;
            }
            foreach (object rawValue in enumerable)
            {
                try
                {
                    if (Convert.ToInt32(rawValue) == index)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                }
            }
            return false;
        }

        private static Dictionary<string, object> CaptureBattleState()
        {
            if (!(BattleManagerBase.GetIns() is NetworkBattleManagerBase manager) ||
                manager.BattlePlayer == null || manager.BattleEnemy == null)
            {
                return null;
            }

            BattlePlayerBase host = Role == P2PRole.Host
                ? manager.BattlePlayer
                : manager.BattleEnemy;
            BattlePlayerBase guest = Role == P2PRole.Host
                ? manager.BattleEnemy
                : manager.BattlePlayer;
            return new Dictionary<string, object>
            {
                ["host"] = CapturePlayerState(host),
                ["guest"] = CapturePlayerState(guest)
            };
        }

        private static Dictionary<string, object> CapturePlayerState(
            BattlePlayerBase player)
        {
            bool localOwnerIsHost = Role == P2PRole.Host;
            bool ownerIsHost = player.IsPlayer
                ? localOwnerIsHost
                : !localOwnerIsHost;
            return new Dictionary<string, object>
            {
                ["life"] = player.Class?.Life ?? 0,
                ["maxLife"] = player.Class?.MaxLife ?? 0,
                ["pp"] = player.Pp,
                ["ppTotal"] = player.PpTotal,
                ["ep"] = player.CurrentEpCount,
                ["turn"] = player.Turn,
                ["isTurn"] = player.IsSelfTurn,
                ["deckCount"] = player.DeckCardList?.Count ?? 0,
                ["deck"] = FormatCardIndices(player.DeckCardList),
                ["deckState"] = FormatPrivateCardStates(player.DeckCardList),
                ["hand"] = FormatCardIndices(player.HandCardList),
                ["handState"] = FormatPrivateCardStates(player.HandCardList),
                ["cemetery"] = FormatPublicCards(player.CemeteryList),
                ["banish"] = FormatCardIndices(player.BanishList),
                ["field"] = FormatFieldCards(player.InPlayCards),
                ["history"] = CapturePlayerHistoryDiagnosticState(
                    player, ownerIsHost)
            };
        }

        private static Dictionary<string, object>
            CapturePlayerHistoryDiagnosticState(
                BattlePlayerBase player,
                bool ownerIsHost)
        {
            Dictionary<string, object> state =
                CapturePlayerHistoryState(player, ownerIsHost);
            Dictionary<string, object> result =
                new Dictionary<string, object>();
            if (state.TryGetValue("scalars", out object rawScalars) &&
                rawScalars is Dictionary<string, object> scalars)
            {
                foreach (KeyValuePair<string, object> value in scalars)
                {
                    result["scalar." + value.Key] = value.Value;
                }
            }
            if (state.TryGetValue("lists", out object rawLists) &&
                rawLists is Dictionary<string, object> lists)
            {
                foreach (KeyValuePair<string, object> value in lists)
                {
                    result["list." + value.Key] = JsonConvert.SerializeObject(
                        value.Value, P2PJson.Settings);
                }
            }
            if (state.TryGetValue("class", out object rawClass) &&
                rawClass is Dictionary<string, object> classState)
            {
                result["class"] = NormalizeDiagnosticValue(classState);
            }
            return result;
        }

        private static object NormalizeDiagnosticValue(object value)
        {
            if (value is Dictionary<string, object> dictionary)
            {
                Dictionary<string, object> normalized =
                    new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> item in dictionary)
                {
                    normalized[item.Key] = NormalizeDiagnosticValue(item.Value);
                }
                return normalized;
            }
            if (!(value is string) && value is IEnumerable)
            {
                return JsonConvert.SerializeObject(value, P2PJson.Settings);
            }
            return value;
        }

        private static string FormatCardIndices(IEnumerable<BattleCardBase> cards)
        {
            return cards == null
                ? string.Empty
                : string.Join(",", cards.Where(card => card != null)
                    .Select(card => $"{card.Index}:{card.CardId}"));
        }

        private static string FormatPrivateCardStates(
            IEnumerable<BattleCardBase> cards)
        {
            if (cards == null)
            {
                return string.Empty;
            }

            List<string> states = new List<string>();
            foreach (BattleCardBase card in cards.Where(card => card != null)
                .OrderBy(card => card.Index))
            {
                try
                {
                    string attach = GetAttachedSkillState(card);
                    string tribe = card.Tribe == null
                        ? string.Empty
                        : string.Join(",", card.Tribe.Select(value => value.ToString()));
                    SkillApplyInformation skillInformation =
                        card.SkillApplyInformation as SkillApplyInformation;
                    string fusion = skillInformation?.FusionIngredients == null
                        ? string.Empty
                        : string.Join(",", skillInformation.FusionIngredients
                            .Where(value => value?.Card != null)
                            .Select(value => value.Card.Index.ToString(
                                CultureInfo.InvariantCulture) + "@" +
                                value.FusionTurn.ToString(CultureInfo.InvariantCulture)));
                    string genericArray = skillInformation?.SkillGenericValueArray == null
                        ? "-"
                        : string.Join(",", skillInformation.SkillGenericValueArray
                            .Select(value => value.ToString(CultureInfo.InvariantCulture)));
                    string genericKeys = skillInformation?.SkillGenericKeyAndValue == null
                        ? string.Empty
                        : string.Join(",", skillInformation.SkillGenericKeyAndValue
                            .OrderBy(value => value.Key, StringComparer.Ordinal)
                            .Select(value => value.Key + "=" +
                                value.Value.ToString(CultureInfo.InvariantCulture)));
                    string randomArray = skillInformation?.SkillRandomArray == null
                        ? "-"
                        : string.Join(",", skillInformation.SkillRandomArray
                            .Select(value => value.ToString(CultureInfo.InvariantCulture)));
                    string referenceState = skillInformation == null
                        ? string.Empty
                        : string.Join("|", new[]
                        {
                            "random=" + FormatCardReferences(
                                skillInformation.RandomSelectedCardList),
                            "drew=" + FormatCardReferences(
                                skillInformation.SkillDrewCardList),
                            "saved=" + FormatCardReferences(
                                skillInformation.SavedTargetList),
                            "burial=" + FormatCardReferences(
                                skillInformation.LastBurialRiteCardList),
                            "getOn=" + FormatCardReferences(
                                skillInformation.GetOnCards),
                            "getOff=" + FormatCardReferences(card.GetOffCards)
                        });
                    string savedTargetIds = skillInformation == null
                        ? string.Empty
                        : string.Join(",", skillInformation.SavedTargetCardIdDict
                            .OrderBy(value => value.Key)
                            .Select(value => value.Key.ToString(
                                    CultureInfo.InvariantCulture) + "=" +
                                string.Join("/", value.Value)));
                    string modifierState = FormatCardModifierDiagnosticState(
                        card, skillInformation);
                    string turnHistory = skillInformation == null
                        ? string.Empty
                        : "caused=" + JsonConvert.SerializeObject(
                            CaptureTurnValueCollection(
                                skillInformation.CausedDamageModifierList),
                            P2PJson.Settings) + ",ppAdd=" +
                            JsonConvert.SerializeObject(
                                CaptureTurnValueCollection(
                                    skillInformation.PpAddList),
                                P2PJson.Settings);
                    string activationIds = card.SkillActivationList == null
                        ? string.Empty
                        : string.Join(",", card.SkillActivationList
                            .Select(value => value.SkillId.ToString(
                                CultureInfo.InvariantCulture)));
                    states.Add(
                        $"idx={card.Index}[id={card.CardId},cost={card.Cost}," +
                        $"spellboost={card.SpellChargeCount},atk={card.Atk}," +
                        $"life={card.Life}/{card.MaxLife},chant={card.ChantCount}," +
                        $"union={skillInformation?.UnionBurstCount.ToString(CultureInfo.InvariantCulture) ?? "-"}," +
                        $"skybound={skillInformation?.SkyboundArtCount.ToString(CultureInfo.InvariantCulture) ?? "-"}," +
                        $"superSkybound={skillInformation?.SuperSkyboundArtCount.ToString(CultureInfo.InvariantCulture) ?? "-"}," +
                        $"clan={(int)card.Clan},tribe={tribe},attach={attach}," +
                        $"fusion={fusion},skillCount={card.SkillActivatedCount}," +
                        $"generic=[{genericArray}],genericKeys=[{genericKeys}]," +
                        $"random=[{randomArray}],refs=[{referenceState}]," +
                        $"savedIds=[{savedTargetIds}],mods=[{modifierState}]," +
                        $"turnHistory=[{turnHistory}],damageCount=" +
                        $"{card.DamagedCounter?.SelfTurnDamage ?? 0}/" +
                        $"{card.DamagedCounter?.OpponentTurnDamage ?? 0}," +
                        $"maxAttackCount={card.MaxAttackableCount}," +
                        $"activationIds=[{activationIds}]]");
                }
                catch (Exception ex)
                {
                    // A partially initialized token should not prevent the
                    // remaining battle state from being compared or logged.
                    states.Add($"{card.Index}:{card.CardId}:state-error:{ex.GetType().Name}");
                }
            }
            return string.Join(";", states);
        }

        private static string FormatCardModifierDiagnosticState(
            BattleCardBase card,
            SkillApplyInformation information)
        {
            Dictionary<string, object> modifiers =
                CaptureCardModifierState(card, information);
            List<string> result = new List<string>();
            foreach (KeyValuePair<string, object> entry in modifiers)
            {
                if (entry.Value is ICollection collection && collection.Count == 0)
                {
                    continue;
                }
                result.Add(entry.Key + "=" + JsonConvert.SerializeObject(
                    entry.Value, P2PJson.Settings));
            }
            return string.Join("|", result);
        }

        private static string FormatCardReferences(
            IEnumerable<BattleCardBase> cards)
        {
            if (cards == null)
            {
                return string.Empty;
            }
            bool localOwnerIsHost = Role == P2PRole.Host;
            return string.Join(",", cards.Where(card => card != null)
                .Select(card =>
                {
                    bool ownerIsHost = card.IsPlayer
                        ? localOwnerIsHost
                        : !localOwnerIsHost;
                    return (ownerIsHost ? "H" : "G") + ":" +
                        card.Index.ToString(CultureInfo.InvariantCulture);
                }));
        }

        private static string FormatPublicCards(IEnumerable<BattleCardBase> cards)
        {
            return cards == null
                ? string.Empty
                : string.Join(",", cards.Where(card => card != null)
                    .Select(card => $"{card.Index}:{card.CardId}"));
        }

        private static string FormatFieldCards(IEnumerable<BattleCardBase> cards)
        {
            return cards == null
                ? string.Empty
                : string.Join(",", cards.Where(card => card != null)
                    .Select(card =>
                        $"{card.Index}:{card.CardId}:{card.Atk}:{card.Life}:" +
                        $"{card.MaxLife}:{(card.IsEvolution ? 1 : 0)}:{card.ChantCount}"));
        }

        private static void TryCheckPendingBattleStates()
        {
            if (PendingBattleStateChecks.Count == 0)
            {
                return;
            }

            NetworkBattleManagerBase manager =
                BattleManagerBase.GetIns() as NetworkBattleManagerBase;
            bool effectsComplete = manager?.VfxMgr != null && manager.VfxMgr.IsEnd;
            DateTime now = DateTime.UtcNow;
            while (PendingBattleStateChecks.Count > 0)
            {
                PendingBattleStateCheck pending = PendingBattleStateChecks.Peek();
                bool timedOut = now >= pending.DeadlineUtc;
                Dictionary<string, object> actual = CaptureBattleState();
                if (actual == null)
                {
                    if (!timedOut)
                    {
                        return;
                    }
                    PendingBattleStateChecks.Dequeue();
                    ReportBattleDiagnostic(
                        $"State check after {pending.Uri} failed: " +
                        "the network battle manager is unavailable.");
                    continue;
                }

                IReadOnlyList<string> differences =
                    P2PBattleStateDiagnostics.Compare(pending.Expected, actual);
                P2PBattleStateCheckDecision decision =
                    P2PBattleStateDiagnostics.DecideCheck(
                        differences.Count == 0,
                        effectsComplete,
                        timedOut);
                if (decision == P2PBattleStateCheckDecision.Wait)
                {
                    return;
                }

                PendingBattleStateChecks.Dequeue();
                if (decision == P2PBattleStateCheckDecision.Synchronized)
                {
                    if (!string.IsNullOrEmpty(pending.InjectionError))
                    {
                        ReportBattleDiagnostic(
                            $"Message injection failed after {pending.Uri}, although the " +
                            "state snapshot currently matches the peer: " +
                            pending.InjectionError);
                        continue;
                    }
                    Plugin.Logger.LogInfo(
                        $"[P2P] State synchronized after {pending.Uri}.");
                    continue;
                }

                if (decision == P2PBattleStateCheckDecision.Stalled)
                {
                    ReportBattleDiagnostic(
                        $"TURN-END STALL after {pending.Uri}: the effect queue did not " +
                        $"finish within {BattleStateCheckTimeoutSeconds} seconds; " +
                        DescribeEffectQueue(manager) +
                        ". The state snapshot currently matches the peer.");
                    continue;
                }

                string waitReason = timedOut && !effectsComplete
                    ? $" The effect queue did not finish within " +
                        $"{BattleStateCheckTimeoutSeconds} seconds; " +
                        DescribeEffectQueue(manager) + "."
                    : string.Empty;
                string injectionReason = string.IsNullOrEmpty(pending.InjectionError)
                    ? string.Empty
                    : " Message injection failed: " + pending.InjectionError;
                ReportBattleDiagnostic(
                    $"DATA DESYNC after {pending.Uri}.{waitReason}" +
                    injectionReason + " " +
                    string.Join("; ", differences));
            }
        }

        private static string DescribeEffectQueue(NetworkBattleManagerBase manager)
        {
            try
            {
                string current = manager?.VfxMgr?.CurrentVfxName;
                List<string> queued = manager?.VfxMgr?.GetSequentialVfxPlayerNames();
                return $"currentVfx={current ?? "<none>"}, " +
                    $"queuedVfx=[{string.Join(",", queued ?? new List<string>())}]";
            }
            catch (Exception ex)
            {
                return "effect queue details unavailable: " + ex.Message;
            }
        }

        private static void ReportBattleDiagnostic(string message)
        {
            Plugin.Logger.LogError("[P2P] " + message);
            if (Role == P2PRole.Guest && IsActive)
            {
                SendWire(new P2PWireMessage
                {
                    Type = "diagnostic",
                    BattleId = BattleId,
                    Error = message
                });
            }
        }

        private static void TrySynchronizeOpponentRoomState()
        {
            if (!pendingOpponentSync || !IsActive || RemoteProfile == null)
            {
                return;
            }

            RoomBase room = RoomBase.GetInstance();
            RoomConnectController controller = RoomBase.ConnectController;
            if (room == null || controller == null || controller.OppoCtrl == null ||
                !room.IsInitializeDone)
            {
                return;
            }

            Player opponent = controller.OppoCtrl.Target;
            bool targetWasValid = opponent != null && opponent.IsValid;
            bool roomHadOpponent = room.IsExistOppo;
            if (targetWasValid && roomHadOpponent)
            {
                pendingOpponentSync = false;
                Plugin.Logger.LogInfo(
                    $"[P2P] Native room state already contains opponent " +
                    $"'{opponent.Name}' ({opponent.ViewerId}); fallback was not needed.");
                return;
            }

            try
            {
                Dictionary<string, object> received = CreateRoomPlayerData(RemoteProfile);
                controller.InitializeOpponentPlayer();
                controller.OppoCtrl.Target.DeckCreateNumber = 0;
                controller.OppoCtrl.Target.OnEnter(received);
                controller.FormatEventHandler.OnEnterOpponent();
                controller.OppoCtrl.EnterRoomServer(string.Empty);

                // Visitors normally get this state during SetupFirstOnly. If the initial
                // RoomEntry arrived before the regular listener existed, refresh it here.
                if (!room.IsExistOppo)
                {
                    room.SetExistOpponent(true, true);
                }

                pendingOpponentSync = false;
                Plugin.Logger.LogInfo(
                    $"[P2P] Synchronized opponent room state as {Role}: " +
                    $"'{RemoteProfile.UserName}' ({RemoteProfile.ViewerId}); " +
                    $"before targetValid={targetWasValid}, roomHasOpponent={roomHadOpponent}; " +
                    $"after targetValid={controller.OppoCtrl.Target.IsValid}, " +
                    $"roomHasOpponent={room.IsExistOppo}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError("[P2P] Failed to synchronize opponent room state: " + ex);
            }
        }

        private static bool SendWire(P2PWireMessage message)
        {
            if (transport == null || !transport.Send(message))
            {
                LastError = "The P2P connection is not available.";
                return false;
            }
            return true;
        }

        private static void HandlePeerDisconnected(string error)
        {
            if (peerDisconnected)
            {
                return;
            }
            peerDisconnected = true;
            LastError = error;

            if (BattleManagerBase.GetIns() is NetworkBattleManagerBase)
            {
                InjectPeerDisconnectResult();
            }
        }

        private static void InjectPeerDisconnectResult()
        {
            if (finishResultSent || currentAgent == null)
            {
                return;
            }

            int localResult = P2PBattleResult.ResolveLocalResultAfterDisconnect(
                localRetired,
                GetLocalFinishResult());

            finishResultSent = true;
            Inject(new Dictionary<string, object>
            {
                ["uri"] = NetworkBattleDefine.NetworkBattleURI.JudgeResult.ToString(),
                ["result"] = localResult,
                ["viewerId"] = 0,
                ["bid"] = BattleId ?? string.Empty,
                ["playSeq"] = Role == P2PRole.Host
                    ? ++hostPlaySequence
                    : ++guestPlaySequence,
                ["time"] = UnixMilliseconds()
            });
            Plugin.Logger.LogInfo(
                $"[P2P] Peer disconnected; received local result {localResult}, " +
                $"localRetired={localRetired}.");
        }

        private static void InjectRoomRelease()
        {
            if (roomReleaseInjected)
            {
                return;
            }
            roomReleaseInjected = true;
            Dictionary<string, object> release = new Dictionary<string, object>
            {
                ["uri"] = PlayerController.ROOM_URI.Release.ToString(),
                ["resultCode"] = (int)NetworkBattleDefine.ReceiveNodeResultCode.Success,
                ["isSelf"] = 0,
                ["viewerId"] = RemoteProfile?.ViewerId ?? 0,
                ["bid"] = BattleId ?? string.Empty,
                ["playSeq"] = Role == P2PRole.Host
                    ? ++hostPlaySequence
                    : ++guestPlaySequence,
                ["time"] = UnixMilliseconds()
            };
            Inject(release);
        }

        private static void ForceExitDisconnectedRoom(RoomBase room)
        {
            if (roomReleaseInjected || room == null)
            {
                return;
            }

            roomReleaseInjected = true;
            try
            {
                // Prevent CheckMatching from starting after the disconnect dialog is created.
                if (room._isRoomReady && !room._isMatchingStart)
                {
                    room._isRoomReady = false;
                }
                room.DisconnectForceExitRoom();
            }
            catch (Exception ex)
            {
                roomReleaseInjected = false;
                Plugin.Logger.LogError("[P2P] Failed to exit the disconnected room: " + ex);
            }
        }

        private static bool IsBattleScene()
        {
            try
            {
                UIManager manager = UIManager.GetInstance();
                return manager != null && manager.GetCurrentScene() == UIManager.ViewScene.Battle;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsRoomAgentReady()
        {
            return currentAgent.CurrentMatchingStatus == RealTimeNetworkAgent.MatchingStatus.Room ||
                currentAgent.CurrentMatchingStatus == RealTimeNetworkAgent.MatchingStatus.RoomReady;
        }

        private static Dictionary<string, object> CreateRoomPlayerData(P2PProfile profile)
        {
            return new Dictionary<string, object>
            {
                ["userName"] = profile.UserName ?? string.Empty,
                ["emblemId"] = profile.EmblemId,
                ["degreeId"] = profile.DegreeId,
                ["countryCode"] = profile.CountryCode ?? string.Empty,
                ["rank"] = profile.Rank,
                ["maxRank"] = profile.Rank,
                ["isGuildMember"] = false,
                ["isGuildJoined"] = false,
                ["oppoId"] = profile.ViewerId,
                ["isOfficial"] = profile.IsOfficial ? 1 : 0,
                ["isFriend"] = 0
            };
        }

        private static P2PProfile CreateLocalProfile()
        {
            return ProfileOfflineData.CreateP2PProfile(P2PIdentity.ViewerId);
        }

        private static List<int> Shuffle(IEnumerable<int> cards)
        {
            List<int> result = cards.ToList();
            Random random = new Random(CreatePositiveInt());
            for (int i = result.Count - 1; i > 0; i--)
            {
                int target = random.Next(i + 1);
                int value = result[i];
                result[i] = result[target];
                result[target] = value;
            }
            return result;
        }

        private static List<object> CreateIndexList(IList<int> indices)
        {
            List<object> result = new List<object>(indices.Count);
            for (int i = 0; i < indices.Count; i++)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["pos"] = i,
                    ["idx"] = indices[i]
                });
            }
            return result;
        }

        private static List<int> ToIntList(object value)
        {
            if (value is IEnumerable<object> objectValues)
            {
                return objectValues.Select(Convert.ToInt32).ToList();
            }
            if (value is IEnumerable<int> intValues)
            {
                return intValues.ToList();
            }
            return new List<int>();
        }

        private static int SourceViewerId(bool sourceIsHost)
        {
            return sourceIsHost ? LocalProfile.ViewerId : RemoteProfile?.ViewerId ?? 0;
        }

        private static int ResolveLocalCardId(int index)
        {
            BattleCardBase card = ResolveLocalCard(index);
            return card?.CardId ?? 0;
        }

        private static int ResolveLocalCardCost(int index)
        {
            BattleCardBase card = ResolveLocalCard(index);
            return card?.Cost ?? -1;
        }

        private static IEnumerable<int> ResolveLocalBurialRiteSkillIndexes(int index)
        {
            BattleCardBase card = ResolveLocalCard(index);
            if (card?.Skills == null)
            {
                return Enumerable.Empty<int>();
            }

            List<int> result = new List<int>();
            int skillIndex = 0;
            foreach (SkillBase skill in card.Skills)
            {
                if (skill != null && skill.IsBurialRite)
                {
                    result.Add(skillIndex);
                }
                skillIndex++;
            }
            return result;
        }

        private static BattleCardBase ResolveLocalCard(int index)
        {
            NetworkBattleManagerBase manager =
                BattleManagerBase.GetIns() as NetworkBattleManagerBase;
            if (manager == null || manager.BattlePlayer == null)
            {
                return null;
            }

            return NetworkBattleGenericTool.GetIndexToCardBase(
                manager, manager.BattlePlayer, index);
        }

        private static string SideName(bool isHost)
        {
            return isHost ? "Host" : "Guest";
        }

        private static void ResetSession()
        {
            sessionGeneration++;
            transport?.Stop(false);
            transport = null;
            currentAgent = null;
            Role = P2PRole.None;
            IsActive = false;
            ConnectionCode = null;
            RoomId = null;
            BattleId = null;
            LocalProfile = null;
            RemoteProfile = null;
            LocalDeck = null;
            RemoteDeck = null;
            Rules = null;
            JoinFinished = false;
            JoinSucceeded = false;
            LastError = null;
            hostPlaySequence = 0;
            guestPlaySequence = 0;
            GuestDeliverySequence.Reset();
            DeferredGuestDeliveries.Clear();
            DeferredAgentDeliveries.Clear();
            RoomRoundState.Reset();
            ResetBattleState();
            localEmitSequence = 1;
            peerDisconnected = false;
            roomReleaseInjected = false;
            pendingOpponentSync = false;
            hostDeckEntry = null;
            guestDeckEntry = null;
        }

        private static void Enqueue(Action action)
        {
            Enqueue(sessionGeneration, action);
        }

        private static void Enqueue(int generation, Action action)
        {
            MainThreadActions.Enqueue(() =>
            {
                if (generation == sessionGeneration)
                {
                    action();
                }
            });
        }

        private static string CreateNumericId()
        {
            long value = 100000000000L + (uint)CreatePositiveInt();
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int CreatePositiveInt()
        {
            byte[] bytes = new byte[4];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
        }

        private static long UnixMilliseconds()
        {
            return (DateTime.UtcNow.Ticks - 621355968000000000L) /
                TimeSpan.TicksPerMillisecond;
        }

        private static IPAddress ParseAddress(string value, IPAddress fallback)
        {
            if (IPAddress.TryParse(value, out IPAddress address))
            {
                return address;
            }
            if (fallback != null)
            {
                return fallback;
            }
            throw new FormatException("Invalid IP address: " + value);
        }

        private static IPAddress FindAdvertisedAddress(AddressFamily preferredFamily)
        {
            IEnumerable<UnicastIPAddressInformation> addresses = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                    network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses);
            IPAddress preferred = addresses
                .Select(item => item.Address)
                .FirstOrDefault(address => address.AddressFamily == preferredFamily &&
                    IsUsableAdvertisedAddress(address) && !IPAddress.IsLoopback(address));
            if (preferred != null)
            {
                return preferred;
            }
            return null;
        }

        private static bool IsUsableAdvertisedAddress(IPAddress address)
        {
            if (address == null || IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address))
            {
                return false;
            }
            return address.AddressFamily != AddressFamily.InterNetworkV6 ||
                (!address.IsIPv6LinkLocal && !address.IsIPv6Multicast && address.ScopeId == 0);
        }

        private sealed class PendingBattleStateCheck
        {
            internal PendingBattleStateCheck(
                string uri,
                Dictionary<string, object> expected,
                DateTime deadlineUtc)
            {
                Uri = uri;
                Expected = expected;
                DeadlineUtc = deadlineUtc;
            }

            internal string Uri { get; }
            internal Dictionary<string, object> Expected { get; }
            internal DateTime DeadlineUtc { get; }
            internal string InjectionError { get; set; }
        }

        private sealed class AppliedHiddenCardState
        {
            internal BattleCardBase Card { get; set; }
            internal string Signature { get; set; }
            internal bool NativeStateInherited { get; set; }
            internal bool StateComplete { get; set; }
            internal DateTime NextRetryUtc { get; set; }
        }

        private sealed class HiddenCardLifeStateModifier : ICardLifeModifier
        {
            private readonly int life;
            private readonly int maxLife;

            internal HiddenCardLifeStateModifier(int life, int maxLife)
            {
                this.life = life;
                this.maxLife = maxLife;
            }

            public bool IsChangeMaxLife => false;
            public bool IsClearBeforeModifier => false;
            public int CalcLife(int baseLife) => life;
            public int CalcMaxLife(int baseMaxLife) => maxLife;
        }

        private sealed class PendingPlayerHistoryState
        {
            internal int Owner { get; set; }
            internal int Revision { get; set; }
            internal Dictionary<string, object> State { get; set; }
            internal bool ReadyToApply { get; set; }
            internal int Attempts { get; set; }
            internal DateTime FirstSeenUtc { get; set; }
            internal DateTime NextAttemptUtc { get; set; }
            internal bool WarningLogged { get; set; }
        }
    }
}
