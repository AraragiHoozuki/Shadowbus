using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
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
        private static bool finishResultSent;
        private static bool? retiringHost;
        private static bool localRetired;
        private static List<int> shuffledHostDeck;
        private static List<int> shuffledGuestDeck;
        private static readonly P2PBattleCardTracker BattleCardTracker =
            new P2PBattleCardTracker();
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
            finishResultSent = false;
            retiringHost = null;
            localRetired = false;
            shuffledHostDeck = null;
            shuffledGuestDeck = null;
            BattleCardTracker.Clear();
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

            bool revealed = BattleCardTracker.PrepareOutgoingAction(
                sourceIsHost, data, out int playIndex, out int cardId,
                null, null,
                warning => Plugin.Logger.LogWarning(
                    $"[P2P] {SideName(sourceIsHost)} hidden-card synchronization: " +
                    warning + "."));
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
                ["selfDeck"] = deckData
            };
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
            return new Dictionary<string, object>
            {
                ["uri"] = NetworkBattleDefine.NetworkBattleURI.BattleStart.ToString(),
                ["bid"] = BattleId,
                ["battleStartDate"] = UnixMilliseconds(),
                ["selfInfo"] = CreateBattleInfo(selfProfile, selfDeck, oppoProfile, oppoDeck,
                    battleSeed),
                ["oppoInfo"] = CreateBattleInfo(oppoProfile, oppoDeck, selfProfile, selfDeck,
                    battleSeed)
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
            NetworkBattleManagerBase manager =
                BattleManagerBase.GetIns() as NetworkBattleManagerBase;
            return manager == null
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
            Dictionary<string, object> expectedBattleState = null;
            if (data != null &&
                data.TryGetValue(P2PBattleStateDiagnostics.StateKey, out object rawState))
            {
                expectedBattleState = rawState as Dictionary<string, object>;
                data.Remove(P2PBattleStateDiagnostics.StateKey);
            }
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
                ["hand"] = FormatCardIndices(player.HandCardList),
                ["cemetery"] = FormatPublicCards(player.CemeteryList),
                ["banish"] = FormatCardIndices(player.BanishList),
                ["field"] = FormatFieldCards(player.InPlayCards)
            };
        }

        private static string FormatCardIndices(IEnumerable<BattleCardBase> cards)
        {
            return cards == null
                ? string.Empty
                : string.Join(",", cards.Where(card => card != null)
                    .Select(card => card.Index)
                    .OrderBy(index => index));
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

        private static void SendWire(P2PWireMessage message)
        {
            if (transport == null || !transport.Send(message))
            {
                LastError = "The P2P connection is not available.";
            }
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
    }
}
