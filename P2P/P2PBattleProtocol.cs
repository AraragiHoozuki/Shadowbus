using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shadowbus
{
    internal enum P2PBattleStateCheckDecision
    {
        Wait,
        Synchronized,
        Desynchronized,
        Stalled
    }

    internal enum P2PBattleRoute
    {
        Opponent,
        Source,
        Consume
    }

    internal static class P2PBattleProtocol
    {
        internal const string PlayActionsUri = "PlayActions";
        internal const string TurnEndActionsUri = "TurnEndActions";
        internal const string TurnEndUri = "TurnEnd";
        internal const string TurnEndFinalUri = "TurnEndFinal";
        internal const string TurnStartUri = "TurnStart";
        internal const string JudgeUri = "Judge";
        internal const string EchoUri = "Echo";
        // Unlike normal battle messages, this table is intentionally sent once at
        // battle setup so both local rule engines can evaluate hidden-zone effects
        // against the same card identities.
        internal const string OpponentDeckIdentityKey = "p2pOpponentDeck";

        internal static List<object> CreateDeckIdentityPayload(IList<int> cardIds)
        {
            if (cardIds == null || cardIds.Count == 0)
            {
                throw new ArgumentException("A non-empty deck is required.", nameof(cardIds));
            }

            List<object> payload = new List<object>(cardIds.Count);
            for (int i = 0; i < cardIds.Count; i++)
            {
                int cardId = cardIds[i];
                if (cardId <= 0)
                {
                    throw new ArgumentException(
                        $"Deck card at position {i + 1} has an invalid ID {cardId}.",
                        nameof(cardIds));
                }
                payload.Add(new Dictionary<string, object>
                {
                    ["idx"] = i + 1,
                    ["cardId"] = cardId
                });
            }
            return payload;
        }

        internal static bool TryReadDeckIdentityPayload(
            Dictionary<string, object> data,
            int expectedCount,
            out List<object> deckData,
            out string error)
        {
            deckData = null;
            error = string.Empty;
            if (data == null || !data.TryGetValue(
                    OpponentDeckIdentityKey, out object rawPayload))
            {
                error = "the Matched message did not contain an opponent deck identity table";
                return false;
            }
            if (!(rawPayload is IEnumerable rawItems) || rawPayload is string)
            {
                error = "the opponent deck identity table was not an array";
                return false;
            }

            List<object> items = new List<object>();
            foreach (object item in rawItems)
            {
                items.Add(item);
            }
            if (expectedCount <= 0)
            {
                error = $"the expected opponent deck size was invalid ({expectedCount})";
                return false;
            }
            if (items.Count != expectedCount)
            {
                error = $"opponent deck identity count was {items.Count}, expected {expectedCount}";
                return false;
            }

            deckData = new List<object>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (!(items[i] is IDictionary<string, object> item))
                {
                    deckData = null;
                    error = $"opponent deck identity entry {i + 1} was not an object";
                    return false;
                }
                if (!TryConvertInt(item, "idx", out int index) || index != i + 1)
                {
                    deckData = null;
                    error = $"opponent deck identity entry {i + 1} had index " +
                        (item.ContainsKey("idx") ? item["idx"]?.ToString() : "<missing>") +
                        $", expected {i + 1}";
                    return false;
                }
                if (!TryConvertInt(item, "cardId", out int cardId) || cardId <= 0)
                {
                    deckData = null;
                    error = $"opponent deck identity entry {i + 1} had an invalid card ID " +
                        (item.ContainsKey("cardId")
                            ? item["cardId"]?.ToString() ?? "<null>"
                            : "<missing>");
                    return false;
                }
                deckData.Add(new Dictionary<string, object>
                {
                    ["idx"] = index,
                    ["cardId"] = cardId
                });
            }
            return true;
        }

        private static bool TryConvertInt(
            IDictionary<string, object> data,
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

        internal static P2PBattleRoute GetRoute(string uri)
        {
            if (string.Equals(uri, EchoUri, StringComparison.Ordinal))
            {
                return P2PBattleRoute.Consume;
            }
            if (string.Equals(uri, JudgeUri, StringComparison.Ordinal))
            {
                return P2PBattleRoute.Source;
            }
            return P2PBattleRoute.Opponent;
        }

        internal static bool RequiresActiveTurnState(string uri)
        {
            return string.Equals(uri, PlayActionsUri, StringComparison.Ordinal) ||
                string.Equals(uri, TurnEndActionsUri, StringComparison.Ordinal) ||
                string.Equals(uri, TurnEndUri, StringComparison.Ordinal) ||
                string.Equals(uri, TurnStartUri, StringComparison.Ordinal) ||
                string.Equals(uri, JudgeUri, StringComparison.Ordinal);
        }

        internal static bool CarriesBattleStateCheckpoint(string uri)
        {
            return string.Equals(uri, TurnEndActionsUri, StringComparison.Ordinal) ||
                string.Equals(uri, TurnEndUri, StringComparison.Ordinal) ||
                string.Equals(uri, TurnEndFinalUri, StringComparison.Ordinal) ||
                string.Equals(uri, TurnStartUri, StringComparison.Ordinal);
        }
    }

    internal sealed class P2PBattleSelectionTracker
    {
        private const int SelectSkillHandUri = 2;
        private const int StartSelectOperation = 0;
        private const int SelectCardOperation = 1;
        private const int CancelSelectOperation = 2;
        private const int CompleteSelectOperation = 7;

        private readonly List<SelectionTarget> burialTargets =
            new List<SelectionTarget>();
        private readonly List<int> announcedSkillIndexes = new List<int>();
        private int playIndex = -1;
        private bool isSelectionActive;
        private bool isBurialRite;
        private bool isEvolutionSelection;

        internal void Reset()
        {
            burialTargets.Clear();
            announcedSkillIndexes.Clear();
            playIndex = -1;
            isSelectionActive = false;
            isBurialRite = false;
            isEvolutionSelection = false;
        }

        internal bool RecordHandData(
            int handUri,
            IList<object> parameters,
            out string summary)
        {
            summary = string.Empty;
            if (handUri != SelectSkillHandUri || parameters == null ||
                parameters.Count < 3 ||
                !TryConvertInt(parameters[0], out int operation) ||
                !TryConvertBool(parameters[2], out bool burialRite))
            {
                return false;
            }

            if (operation == CancelSelectOperation)
            {
                bool hadSelection = isSelectionActive;
                Reset();
                summary = hadSelection ? "cancelled" : string.Empty;
                return hadSelection;
            }

            if (operation == StartSelectOperation)
            {
                Reset();
                if (parameters.Count < 4 ||
                    !TryConvertBool(parameters[1], out isEvolutionSelection) ||
                    !TryConvertInt(parameters[3], out playIndex))
                {
                    return false;
                }

                isSelectionActive = true;
                isBurialRite = burialRite;
                CaptureAnnouncedSkillIndexes(parameters);
                summary = $"started kind={(isBurialRite ? "burial-rite" : "skill")}, " +
                    $"playIdx={playIndex}, evolve={(isEvolutionSelection ? 1 : 0)}, " +
                    $"skills=[{string.Join(",", announcedSkillIndexes)}]";
                return true;
            }

            if ((operation != SelectCardOperation &&
                    operation != CompleteSelectOperation) ||
                (!isSelectionActive && !burialRite) || parameters.Count < 4 ||
                !TryParseTarget(parameters[3], out SelectionTarget target))
            {
                return false;
            }

            isSelectionActive = true;
            isBurialRite |= burialRite;
            if (!burialTargets.Any(existing => existing.Index == target.Index &&
                    existing.IsSelf == target.IsSelf))
            {
                burialTargets.Add(target);
            }
            summary = $"{(operation == CompleteSelectOperation ? "completed" : "selected")} " +
                $"target={target.Index}, isSelf={(target.IsSelf ? 1 : 0)}";
            return true;
        }

        internal bool PrepareOutgoingAction(
            Dictionary<string, object> data,
            Func<int, IEnumerable<int>> burialSkillResolver,
            out string summary)
        {
            summary = string.Empty;
            if (!isSelectionActive || burialTargets.Count == 0 || data == null ||
                !data.TryGetValue("uri", out object rawUri) ||
                !string.Equals(rawUri?.ToString(), P2PBattleProtocol.PlayActionsUri,
                    StringComparison.Ordinal) ||
                !TryGetInt(data, "playIdx", out int outgoingPlayIndex))
            {
                return false;
            }

            if (playIndex >= 0 && outgoingPlayIndex != playIndex)
            {
                summary = $"discarded stale selection for playIdx={playIndex}; " +
                    $"next action used playIdx={outgoingPlayIndex}";
                Reset();
                return false;
            }

            List<int> skillIndexes = ResolveSkillIndexes(
                outgoingPlayIndex, data,
                isBurialRite ? burialSkillResolver : null);
            List<object> existingTargets = ToObjectList(
                data.TryGetValue("targetList", out object rawTargets)
                    ? rawTargets : null);
            List<object> completedTargets = new List<object>();

            foreach (SelectionTarget selected in burialTargets)
            {
                Dictionary<string, object> target = existingTargets
                    .OfType<Dictionary<string, object>>()
                    .FirstOrDefault(candidate => TargetMatches(candidate, selected));
                if (target == null)
                {
                    target = new Dictionary<string, object>
                    {
                        ["targetIdx"] = selected.Index,
                        ["isSelf"] = selected.IsSelf ? 1 : 0
                    };
                }
                if (skillIndexes.Count > 0)
                {
                    target["selectSkillIndex"] = new List<int>(skillIndexes);
                }
                completedTargets.Add(target);
            }

            foreach (object target in existingTargets)
            {
                if (!(target is Dictionary<string, object> dictionary) ||
                    !burialTargets.Any(selected => TargetMatches(dictionary, selected)))
                {
                    completedTargets.Add(target);
                }
            }

            data["targetList"] = completedTargets;
            data["type"] = isEvolutionSelection ? 21 : 31;
            summary = $"kind={(isBurialRite ? "burial-rite" : "skill")}, " +
                $"playIdx={outgoingPlayIndex}, " +
                $"targets=[{string.Join(",", burialTargets.Select(target => target.Index))}], " +
                $"skills=[{string.Join(",", skillIndexes)}]";
            Reset();
            return true;
        }

        private void CaptureAnnouncedSkillIndexes(IList<object> parameters)
        {
            for (int i = 4; i < parameters.Count; i++)
            {
                foreach (int index in ToIntList(parameters[i]))
                {
                    if (index >= 0 && !announcedSkillIndexes.Contains(index))
                    {
                        announcedSkillIndexes.Add(index);
                    }
                }
            }
        }

        private List<int> ResolveSkillIndexes(
            int outgoingPlayIndex,
            Dictionary<string, object> data,
            Func<int, IEnumerable<int>> burialSkillResolver)
        {
            List<int> result = new List<int>();
            if (data.TryGetValue("targetList", out object rawTargets))
            {
                foreach (Dictionary<string, object> target in
                    ToObjectList(rawTargets).OfType<Dictionary<string, object>>())
                {
                    if (target.TryGetValue("selectSkillIndex", out object rawSelect))
                    {
                        AddDistinct(result, ToIntList(rawSelect));
                    }
                }
            }
            if (result.Count == 0)
            {
                AddDistinct(result, announcedSkillIndexes);
            }

            if (result.Count == 0 && burialSkillResolver != null)
            {
                try
                {
                    AddDistinct(result, burialSkillResolver(outgoingPlayIndex));
                }
                catch (Exception)
                {
                }
            }
            return result;
        }

        private static void AddDistinct(List<int> destination, IEnumerable<int> values)
        {
            if (values == null)
            {
                return;
            }
            foreach (int value in values)
            {
                if (value >= 0 && !destination.Contains(value))
                {
                    destination.Add(value);
                }
            }
        }

        private static bool TryParseTarget(object rawTarget, out SelectionTarget target)
        {
            target = default;
            string encoded = rawTarget?.ToString() ?? string.Empty;
            if (encoded.Length < 2 || (encoded[0] != '0' && encoded[0] != '1') ||
                !int.TryParse(encoded.Substring(1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int index))
            {
                return false;
            }
            target = new SelectionTarget(index, encoded[0] == '1');
            return true;
        }

        private static bool TargetMatches(
            Dictionary<string, object> data,
            SelectionTarget target)
        {
            return TryGetInt(data, "targetIdx", out int index) &&
                TryGetInt(data, "isSelf", out int side) &&
                index == target.Index && side == (target.IsSelf ? 1 : 0);
        }

        private static List<object> ToObjectList(object value)
        {
            List<object> result = new List<object>();
            if (value is string || value == null)
            {
                return result;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    result.Add(item);
                }
            }
            return result;
        }

        private static IEnumerable<int> ToIntList(object value)
        {
            if (value is string || value == null || !(value is IEnumerable enumerable))
            {
                yield break;
            }
            foreach (object item in enumerable)
            {
                if (TryConvertInt(item, out int converted))
                {
                    yield return converted;
                }
            }
        }

        private static bool TryGetInt(
            Dictionary<string, object> data,
            string key,
            out int result)
        {
            result = 0;
            return data != null && data.TryGetValue(key, out object value) &&
                TryConvertInt(value, out result);
        }

        private static bool TryConvertInt(object value, out int result)
        {
            try
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                result = 0;
                return false;
            }
        }

        private static bool TryConvertBool(object value, out bool result)
        {
            if (value is bool boolean)
            {
                result = boolean;
                return true;
            }
            if (bool.TryParse(value?.ToString(), out result))
            {
                return true;
            }
            if (TryConvertInt(value, out int integer))
            {
                result = integer != 0;
                return true;
            }
            result = false;
            return false;
        }

        private readonly struct SelectionTarget
        {
            internal SelectionTarget(int index, bool isSelf)
            {
                Index = index;
                IsSelf = isSelf;
            }

            internal int Index { get; }
            internal bool IsSelf { get; }
        }
    }

    internal sealed class P2PBattleCardTracker
    {
        private readonly Dictionary<int, int> hostCards = new Dictionary<int, int>();
        private readonly Dictionary<int, int> guestCards = new Dictionary<int, int>();
        private readonly Dictionary<int, int> hostCardCosts = new Dictionary<int, int>();
        private readonly Dictionary<int, int> guestCardCosts = new Dictionary<int, int>();
        private readonly Dictionary<int, CardMutation> hostCardMutations =
            new Dictionary<int, CardMutation>();
        private readonly Dictionary<int, CardMutation> guestCardMutations =
            new Dictionary<int, CardMutation>();

        internal void Reset(IList<int> hostDeck, IList<int> guestDeck)
        {
            hostCards.Clear();
            guestCards.Clear();
            hostCardCosts.Clear();
            guestCardCosts.Clear();
            hostCardMutations.Clear();
            guestCardMutations.Clear();
            RegisterDeck(hostCards, hostDeck);
            RegisterDeck(guestCards, guestDeck);
        }

        internal void Clear()
        {
            hostCards.Clear();
            guestCards.Clear();
            hostCardCosts.Clear();
            guestCardCosts.Clear();
            hostCardMutations.Clear();
            guestCardMutations.Clear();
        }

        internal void RememberSourceCard(
            bool sourceIsHost,
            int index,
            int cardId,
            int cost)
        {
            if (index < 0 || cardId <= 0)
            {
                return;
            }

            GetCards(sourceIsHost)[index] = cardId;
            if (cost >= 0)
            {
                GetCardCosts(sourceIsHost)[index] = cost;
            }
        }

        internal bool RememberSourceCardMutation(
            bool sourceIsHost,
            int index,
            int originalCardId,
            int originalCost,
            int mutationCardId,
            int mutationCost,
            int keyActionType)
        {
            const int Accelerated = 2;
            const int Crystallize = 3;
            if (index < 0 || originalCardId <= 0 || mutationCardId <= 0 ||
                originalCardId == mutationCardId || mutationCost < 0 ||
                (keyActionType != Accelerated && keyActionType != Crystallize))
            {
                return false;
            }

            Dictionary<int, CardMutation> mutations =
                GetCardMutations(sourceIsHost);
            if (mutations.TryGetValue(index, out CardMutation existing) &&
                existing.OriginalCardId == originalCardId &&
                existing.OriginalCost == originalCost &&
                existing.CardId == mutationCardId &&
                existing.Cost == mutationCost &&
                existing.KeyActionType == keyActionType)
            {
                return false;
            }

            mutations[index] = new CardMutation(
                originalCardId, originalCost, mutationCardId, mutationCost,
                keyActionType);
            return true;
        }

        internal bool PrepareOutgoingAction(
            bool sourceIsHost,
            Dictionary<string, object> data,
            out int playIndex,
            out int cardId,
            Func<int, int> sourceCardIdResolver = null,
            Func<int, int> sourceCardCostResolver = null,
            Action<string> diagnosticLogger = null)
        {
            playIndex = -1;
            cardId = 0;
            if (data == null)
            {
                return false;
            }

            ObserveKnownCards(sourceIsHost, data, "knownList");
            ObserveKnownCards(sourceIsHost, data, "uList");
            ObserveOrderList(sourceIsHost, data);
            RevealPublicMoves(sourceIsHost, data,
                sourceCardIdResolver, sourceCardCostResolver, diagnosticLogger);
            RevealFusionIngredients(sourceIsHost, data,
                sourceCardIdResolver, sourceCardCostResolver, diagnosticLogger);
            RevealOpenedHandCards(sourceIsHost, data,
                sourceCardIdResolver, sourceCardCostResolver, diagnosticLogger);

            if (!TryGetInt(data, "playIdx", out playIndex) || playIndex < 0)
            {
                return false;
            }

            if (TryRevealCardMutation(sourceIsHost, data, playIndex,
                    sourceCardIdResolver, sourceCardCostResolver,
                    diagnosticLogger, out cardId))
            {
                PlaceMutationKnownListAfterKeyAction(data);
                return true;
            }

            if (!TryResolveSourceCardId(sourceIsHost, playIndex,
                    sourceCardIdResolver, out cardId))
            {
                return false;
            }

            int? cost = ResolveSourceCardCost(
                sourceIsHost, playIndex, sourceCardCostResolver);
            RevealKnownCard(data, playIndex, cardId, true, cost);
            return true;
        }

        private bool TryRevealCardMutation(
            bool sourceIsHost,
            Dictionary<string, object> data,
            int playIndex,
            Func<int, int> sourceCardIdResolver,
            Func<int, int> sourceCardCostResolver,
            Action<string> diagnosticLogger,
            out int cardId)
        {
            cardId = 0;
            if (!TryGetMutationKeyAction(data, out int keyActionType,
                    out int originalCardId))
            {
                return false;
            }

            if (GetCardMutations(sourceIsHost).TryGetValue(
                    playIndex, out CardMutation mutation) &&
                mutation.KeyActionType == keyActionType &&
                (originalCardId <= 0 || mutation.OriginalCardId == originalCardId))
            {
                cardId = mutation.CardId;
                RememberSourceCard(
                    sourceIsHost, playIndex, mutation.CardId, mutation.Cost);
                RevealKnownCard(data, playIndex, mutation.CardId, true,
                    mutation.OriginalCost, null, null, true);
                return true;
            }

            if (TryGetKnownCardState(data, playIndex, true,
                    out int knownCardId, out int? knownCost) &&
                knownCardId > 0 && knownCardId != originalCardId)
            {
                cardId = knownCardId;
                RememberSourceCard(
                    sourceIsHost, playIndex, knownCardId, knownCost ?? -1);
                RevealKnownCard(data, playIndex, knownCardId, true,
                    knownCost, null, null, true);
                return true;
            }

            if (TryResolveSourceCardId(sourceIsHost, playIndex,
                    sourceCardIdResolver, out int resolvedCardId) &&
                resolvedCardId != originalCardId)
            {
                int? resolvedCost = ResolveSourceCardCost(
                    sourceIsHost, playIndex, sourceCardCostResolver);
                cardId = resolvedCardId;
                RevealKnownCard(data, playIndex, resolvedCardId, true,
                    resolvedCost, null, null, true);
                return true;
            }

            diagnosticLogger?.Invoke(
                $"could not resolve mutation card idx={playIndex}, " +
                $"keyActionType={keyActionType}, originalCardId={originalCardId}");
            return false;
        }

        private static void PlaceMutationKnownListAfterKeyAction(
            Dictionary<string, object> data)
        {
            if (!data.TryGetValue("keyAction", out object keyActions) ||
                !data.TryGetValue("knownList", out object knownList))
            {
                return;
            }

            List<KeyValuePair<string, object>> fields = data.ToList();
            data.Clear();
            bool insertedKnownList = false;
            foreach (KeyValuePair<string, object> field in fields)
            {
                if (string.Equals(field.Key, "knownList", StringComparison.Ordinal))
                {
                    continue;
                }

                data[field.Key] = field.Value;
                if (!insertedKnownList &&
                    string.Equals(field.Key, "keyAction", StringComparison.Ordinal))
                {
                    data["knownList"] = knownList;
                    insertedKnownList = true;
                }
            }

            if (!insertedKnownList)
            {
                data["keyAction"] = keyActions;
                data["knownList"] = knownList;
            }
        }

        private static bool TryGetMutationKeyAction(
            Dictionary<string, object> data,
            out int keyActionType,
            out int originalCardId)
        {
            const int Accelerated = 2;
            const int Crystallize = 3;
            keyActionType = 0;
            originalCardId = 0;
            if (!data.TryGetValue("keyAction", out object rawKeyActions))
            {
                return false;
            }

            foreach (object item in Enumerate(rawKeyActions))
            {
                if (!(item is Dictionary<string, object> keyAction) ||
                    !TryGetInt(keyAction, "type", out int type) ||
                    (type != Accelerated && type != Crystallize))
                {
                    continue;
                }

                keyActionType = type;
                TryGetInt(keyAction, "cardId", out originalCardId);
                return true;
            }
            return false;
        }

        private static bool TryGetKnownCardState(
            Dictionary<string, object> data,
            int index,
            bool isSelf,
            out int cardId,
            out int? cost)
        {
            cardId = 0;
            cost = null;
            if (!data.TryGetValue("knownList", out object rawKnownList))
            {
                return false;
            }

            foreach (object item in Enumerate(rawKnownList))
            {
                if (!(item is Dictionary<string, object> known) ||
                    IsSelf(known) != isSelf ||
                    !GetIndices(known).Contains(index) ||
                    !TryGetInt(known, "cardId", out cardId))
                {
                    continue;
                }

                if (TryGetInt(known, "cost", out int knownCost))
                {
                    cost = knownCost;
                }
                return true;
            }
            return false;
        }

        private static void RegisterDeck(Dictionary<int, int> cards, IList<int> deck)
        {
            if (deck == null)
            {
                return;
            }
            for (int i = 0; i < deck.Count; i++)
            {
                cards[i + 1] = deck[i];
            }
        }

        private void ObserveKnownCards(
            bool sourceIsHost,
            Dictionary<string, object> data,
            string key)
        {
            if (!data.TryGetValue(key, out object rawList))
            {
                return;
            }
            foreach (object item in Enumerate(rawList))
            {
                Dictionary<string, object> card = item as Dictionary<string, object>;
                if (card == null || !TryGetInt(card, "cardId", out int cardId) || cardId <= 0)
                {
                    continue;
                }
                bool ownerIsHost = IsSelf(card) ? sourceIsHost : !sourceIsHost;
                foreach (int index in GetIndices(card))
                {
                    GetCards(ownerIsHost)[index] = cardId;
                }
            }
        }

        private void ObserveOrderList(bool sourceIsHost, Dictionary<string, object> data)
        {
            if (!data.TryGetValue("orderList", out object rawOrderList))
            {
                return;
            }
            foreach (object item in Enumerate(rawOrderList))
            {
                Dictionary<string, object> order = item as Dictionary<string, object>;
                if (order == null)
                {
                    continue;
                }
                if (order.TryGetValue("add", out object rawAdd))
                {
                    ObserveAddedCard(sourceIsHost, rawAdd as Dictionary<string, object>);
                }
                if (order.TryGetValue("metamorphose", out object rawMetamorphose))
                {
                    ObserveMetamorphose(sourceIsHost,
                        rawMetamorphose as Dictionary<string, object>);
                }
            }
        }

        private void RevealPublicMoves(
            bool sourceIsHost,
            Dictionary<string, object> data,
            Func<int, int> sourceCardIdResolver,
            Func<int, int> sourceCardCostResolver,
            Action<string> diagnosticLogger)
        {
            if (data.TryGetValue("orderList", out object rawOrderList))
            {
                foreach (object item in Enumerate(rawOrderList))
                {
                    Dictionary<string, object> order = item as Dictionary<string, object>;
                    if (order != null && order.TryGetValue("move", out object rawMove))
                    {
                        RevealPublicMove(sourceIsHost, data,
                            rawMove as Dictionary<string, object>,
                            sourceCardIdResolver, sourceCardCostResolver,
                            diagnosticLogger);
                    }
                }
            }
            if (data.TryGetValue("uList", out object rawUnapprovedList))
            {
                foreach (object item in Enumerate(rawUnapprovedList))
                {
                    RevealPublicMove(sourceIsHost, data,
                        item as Dictionary<string, object>,
                        sourceCardIdResolver, sourceCardCostResolver,
                        diagnosticLogger);
                }
            }
        }

        private void RevealOpenedHandCards(
            bool sourceIsHost,
            Dictionary<string, object> data,
            Func<int, int> sourceCardIdResolver,
            Func<int, int> sourceCardCostResolver,
            Action<string> diagnosticLogger)
        {
            if (!data.TryGetValue("orderList", out object rawOrderList))
            {
                return;
            }

            foreach (object item in Enumerate(rawOrderList))
            {
                Dictionary<string, object> order = item as Dictionary<string, object>;
                if (order == null ||
                    !order.TryGetValue("openMyCards", out object rawOpenCards) ||
                    !(rawOpenCards is Dictionary<string, object> openCards))
                {
                    continue;
                }

                foreach (int index in GetIndices(openCards))
                {
                    if (TryResolveSourceCardId(sourceIsHost, index,
                            sourceCardIdResolver, out int cardId))
                    {
                        int? cost = ResolveSourceCardCost(
                            sourceIsHost, index, sourceCardCostResolver);
                        RevealKnownCard(data, index, cardId, true, cost);
                    }
                    else
                    {
                        diagnosticLogger?.Invoke(
                            $"could not reveal opened hand card idx={index}");
                    }
                }
            }
        }

        private void RevealFusionIngredients(
            bool sourceIsHost,
            Dictionary<string, object> data,
            Func<int, int> sourceCardIdResolver,
            Func<int, int> sourceCardCostResolver,
            Action<string> diagnosticLogger)
        {
            if (!data.TryGetValue("orderList", out object rawOrderList))
            {
                return;
            }

            foreach (object item in Enumerate(rawOrderList))
            {
                Dictionary<string, object> order = item as Dictionary<string, object>;
                if (order == null || !order.TryGetValue("fusion", out object rawFusion) ||
                    !(rawFusion is Dictionary<string, object> fusion) ||
                    !fusion.TryGetValue("ingredients", out object rawIngredients))
                {
                    continue;
                }

                foreach (object rawIndex in Enumerate(rawIngredients))
                {
                    if (!TryConvertInt(rawIndex, out int index) ||
                        !TryResolveSourceCardId(sourceIsHost, index,
                            sourceCardIdResolver, out int cardId))
                    {
                        continue;
                    }

                    int? cost = ResolveSourceCardCost(
                        sourceIsHost, index, sourceCardCostResolver);
                    RevealKnownCard(data, index, cardId, true, cost, 10, 60);
                }
            }
        }

        private void RevealPublicMove(
            bool sourceIsHost,
            Dictionary<string, object> data,
            Dictionary<string, object> move,
            Func<int, int> sourceCardIdResolver,
            Func<int, int> sourceCardCostResolver,
            Action<string> diagnosticLogger)
        {
            if (move == null || !TryGetInt(move, "from", out int from) ||
                !TryGetInt(move, "to", out int to) ||
                !IsSelf(move))
            {
                return;
            }

            IEnumerable<int> revealedIndices;
            if (IsHiddenToPublicMove(from, to))
            {
                revealedIndices = GetIndices(move);
            }
            else if (from == 0 && to == 10)
            {
                revealedIndices = GetOpenMoveIndices(move);
            }
            else
            {
                return;
            }

            foreach (int index in revealedIndices)
            {
                if (TryResolveSourceCardId(sourceIsHost, index,
                        sourceCardIdResolver, out int cardId))
                {
                    int? cost = ResolveSourceCardCost(
                        sourceIsHost, index, sourceCardCostResolver);
                    RevealKnownCard(data, index, cardId, true, cost, from, to);
                }
                else
                {
                    diagnosticLogger?.Invoke(
                        $"could not reveal public card idx={index}, from={from}, to={to}");
                }
            }
        }

        private static IEnumerable<int> GetOpenMoveIndices(
            Dictionary<string, object> move)
        {
            if (!move.TryGetValue("is_open", out object rawOpen))
            {
                yield break;
            }

            List<int> indices = GetIndices(move).ToList();
            if (rawOpen is string || !(rawOpen is IEnumerable))
            {
                if (TryConvertInt(rawOpen, out int isOpen) && isOpen != 0)
                {
                    foreach (int index in indices)
                    {
                        yield return index;
                    }
                }
                yield break;
            }

            foreach (object item in Enumerate(rawOpen))
            {
                if (TryConvertInt(item, out int position) &&
                    position >= 0 && position < indices.Count)
                {
                    yield return indices[position];
                }
            }
        }

        private static bool IsHiddenToPublicMove(int from, int to)
        {
            const int Deck = 0;
            const int Hand = 10;
            const int Field = 20;
            const int Cemetery = 30;
            const int Banish = 40;
            return (from == Deck || from == Hand) &&
                (to == Field || to == Cemetery || to == Banish);
        }

        private static void RevealKnownCard(
            Dictionary<string, object> data,
            int index,
            int cardId,
            bool isSelf,
            int? cost = null,
            int? from = null,
            int? to = null,
            bool prioritize = false)
        {
            List<object> knownList = GetOrCreateObjectList(data, "knownList");
            for (int i = 0; i < knownList.Count; i++)
            {
                Dictionary<string, object> known =
                    knownList[i] as Dictionary<string, object>;
                if (known == null || IsSelf(known) != isSelf ||
                    !GetIndices(known).Contains(index))
                {
                    continue;
                }
                known["cardId"] = cardId;
                known["is_open"] = 1;
                AddKnownCardState(known, cost, from, to);
                if (prioritize && i > 0)
                {
                    knownList.RemoveAt(i);
                    knownList.Insert(0, known);
                }
                return;
            }
            Dictionary<string, object> revealed = new Dictionary<string, object>
            {
                ["idx"] = index,
                ["cardId"] = cardId,
                ["isSelf"] = isSelf ? 1 : 0,
                ["is_open"] = 1
            };
            AddKnownCardState(revealed, cost, from, to);
            if (prioritize)
            {
                knownList.Insert(0, revealed);
            }
            else
            {
                knownList.Add(revealed);
            }
        }

        private static void AddKnownCardState(
            Dictionary<string, object> known,
            int? cost,
            int? from,
            int? to)
        {
            if (cost.HasValue && cost.Value >= 0)
            {
                known["cost"] = cost.Value;
            }
            if (from.HasValue && !known.ContainsKey("from"))
            {
                known["from"] = from.Value;
            }
            if (to.HasValue && !known.ContainsKey("to"))
            {
                known["to"] = to.Value;
            }
        }

        private void ObserveAddedCard(
            bool sourceIsHost,
            Dictionary<string, object> addition)
        {
            if (addition == null || !(addition.TryGetValue("card", out object rawCard)) ||
                !(rawCard is Dictionary<string, object> card))
            {
                return;
            }

            bool ownerIsHost = IsSelf(addition) ? sourceIsHost : !sourceIsHost;
            Dictionary<int, int> ownerCards = GetCards(ownerIsHost);
            int cardId = 0;
            TryGetInt(card, "cardId", out cardId);
            if (cardId <= 0 && TryGetInt(card, "baseIdx", out int baseIndex))
            {
                ownerCards.TryGetValue(baseIndex, out cardId);
            }
            if (cardId <= 0)
            {
                return;
            }
            foreach (int index in GetIndices(addition))
            {
                ownerCards[index] = cardId;
            }
        }

        private void ObserveMetamorphose(
            bool sourceIsHost,
            Dictionary<string, object> metamorphose)
        {
            if (metamorphose == null ||
                !metamorphose.TryGetValue("after", out object rawAfter) ||
                !(rawAfter is Dictionary<string, object> after) ||
                !TryGetInt(after, "cardId", out int cardId) || cardId <= 0)
            {
                return;
            }

            bool ownerIsHost = IsSelf(metamorphose) ? sourceIsHost : !sourceIsHost;
            foreach (int index in GetIndices(metamorphose))
            {
                GetCards(ownerIsHost)[index] = cardId;
            }
        }

        private Dictionary<int, int> GetCards(bool ownerIsHost)
        {
            return ownerIsHost ? hostCards : guestCards;
        }

        private Dictionary<int, int> GetCardCosts(bool ownerIsHost)
        {
            return ownerIsHost ? hostCardCosts : guestCardCosts;
        }

        private Dictionary<int, CardMutation> GetCardMutations(bool ownerIsHost)
        {
            return ownerIsHost ? hostCardMutations : guestCardMutations;
        }

        private bool TryResolveSourceCardId(
            bool sourceIsHost,
            int index,
            Func<int, int> sourceCardIdResolver,
            out int cardId)
        {
            Dictionary<int, int> sourceCards = GetCards(sourceIsHost);
            if (sourceCardIdResolver != null)
            {
                try
                {
                    cardId = sourceCardIdResolver(index);
                    if (cardId > 0)
                    {
                        sourceCards[index] = cardId;
                        return true;
                    }
                }
                catch (Exception)
                {
                }
            }
            return sourceCards.TryGetValue(index, out cardId) && cardId > 0;
        }

        private int? ResolveSourceCardCost(
            bool sourceIsHost,
            int index,
            Func<int, int> sourceCardCostResolver)
        {
            if (sourceCardCostResolver != null)
            {
                try
                {
                    int resolved = sourceCardCostResolver(index);
                    if (resolved >= 0)
                    {
                        GetCardCosts(sourceIsHost)[index] = resolved;
                        return resolved;
                    }
                }
                catch (Exception)
                {
                }
            }
            return GetCardCosts(sourceIsHost).TryGetValue(index, out int cached)
                ? (int?)cached
                : null;
        }

        private static List<object> GetOrCreateObjectList(
            Dictionary<string, object> data,
            string key)
        {
            if (data.TryGetValue(key, out object value) && value is List<object> list)
            {
                return list;
            }
            List<object> result = new List<object>();
            foreach (object item in Enumerate(value))
            {
                result.Add(item);
            }
            data[key] = result;
            return result;
        }

        private static IEnumerable<object> Enumerate(object value)
        {
            if (value is string || value == null)
            {
                yield break;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<int> GetIndices(Dictionary<string, object> data)
        {
            if (!data.TryGetValue("idx", out object value) &&
                !data.TryGetValue("idxList", out value))
            {
                yield break;
            }
            if (TryConvertInt(value, out int single))
            {
                yield return single;
                yield break;
            }
            foreach (object item in Enumerate(value))
            {
                if (TryConvertInt(item, out int index))
                {
                    yield return index;
                }
            }
        }

        private static bool IsSelf(Dictionary<string, object> data)
        {
            return TryGetInt(data, "isSelf", out int side) && side == 1;
        }

        private static bool TryGetInt(
            Dictionary<string, object> data,
            string key,
            out int result)
        {
            result = 0;
            return data != null && data.TryGetValue(key, out object value) &&
                TryConvertInt(value, out result);
        }

        private static bool TryConvertInt(object value, out int result)
        {
            try
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                result = 0;
                return false;
            }
        }

        private sealed class CardMutation
        {
            internal CardMutation(
                int originalCardId,
                int originalCost,
                int cardId,
                int cost,
                int keyActionType)
            {
                OriginalCardId = originalCardId;
                OriginalCost = originalCost;
                CardId = cardId;
                Cost = cost;
                KeyActionType = keyActionType;
            }

            internal int OriginalCardId { get; }
            internal int OriginalCost { get; }
            internal int CardId { get; }
            internal int Cost { get; }
            internal int KeyActionType { get; }
        }
    }

    internal static class P2PBattleStateDiagnostics
    {
        internal const string StateKey = "p2pState";

        internal static P2PBattleStateCheckDecision DecideCheck(
            bool statesMatch,
            bool effectsComplete,
            bool timedOut)
        {
            if (!timedOut)
            {
                return statesMatch && effectsComplete
                    ? P2PBattleStateCheckDecision.Synchronized
                    : P2PBattleStateCheckDecision.Wait;
            }
            if (!statesMatch)
            {
                return P2PBattleStateCheckDecision.Desynchronized;
            }
            return effectsComplete
                ? P2PBattleStateCheckDecision.Synchronized
                : P2PBattleStateCheckDecision.Stalled;
        }

        internal static IReadOnlyList<string> Compare(
            Dictionary<string, object> expected,
            Dictionary<string, object> actual)
        {
            Dictionary<string, string> expectedValues = Flatten(expected);
            Dictionary<string, string> actualValues = Flatten(actual);
            SortedSet<string> keys = new SortedSet<string>(expectedValues.Keys,
                StringComparer.Ordinal);
            keys.UnionWith(actualValues.Keys);

            List<string> differences = new List<string>();
            foreach (string key in keys)
            {
                bool hasExpected = expectedValues.TryGetValue(key, out string expectedValue);
                bool hasActual = actualValues.TryGetValue(key, out string actualValue);
                if (!hasExpected || !hasActual ||
                    !string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
                {
                    differences.Add(
                        $"{key}: expected={(hasExpected ? expectedValue : "<missing>")}, " +
                        $"actual={(hasActual ? actualValue : "<missing>")}");
                }
            }
            return differences;
        }

        internal static string DescribeBattleMessage(Dictionary<string, object> data)
        {
            if (data == null)
            {
                return "<null>";
            }

            string uri = Read(data, "uri", "?");
            string playIndex = Read(data, "playIdx", "-");
            string type = Read(data, "type", "-");
            int targets = Count(data, "targetList") + Count(data, "oppoTargetList");
            int known = Count(data, "knownList");
            int orders = Count(data, "orderList");
            List<string> moves = new List<string>();
            if (data.TryGetValue("orderList", out object rawOrders))
            {
                foreach (object item in Enumerate(rawOrders))
                {
                    if (!(item is Dictionary<string, object> order) ||
                        !order.TryGetValue("move", out object rawMove) ||
                        !(rawMove is Dictionary<string, object> move))
                    {
                        continue;
                    }
                    moves.Add(
                        $"{Read(move, "from", "?")}->{Read(move, "to", "?")}" +
                        $"[{ReadIndices(move)}]/self={Read(move, "isSelf", "?")}");
                }
            }
            return $"uri={uri}, playIdx={playIndex}, type={type}, " +
                $"targets={targets}, known={known}, orders={orders}, " +
                $"moves=[{string.Join(";", moves)}]";
        }

        private static Dictionary<string, string> Flatten(
            Dictionary<string, object> source)
        {
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.Ordinal);
            FlattenValue(string.Empty, source, result);
            return result;
        }

        private static void FlattenValue(
            string path,
            object value,
            Dictionary<string, string> result)
        {
            if (value is Dictionary<string, object> dictionary)
            {
                foreach (string key in dictionary.Keys.OrderBy(key => key,
                    StringComparer.Ordinal))
                {
                    FlattenValue(string.IsNullOrEmpty(path) ? key : path + "." + key,
                        dictionary[key], result);
                }
                return;
            }

            if (!(value is string) && value is IEnumerable enumerable)
            {
                List<string> items = new List<string>();
                foreach (object item in enumerable)
                {
                    items.Add(FormatValue(item));
                }
                result[path] = "[" + string.Join(",", items) + "]";
                return;
            }

            result[path] = FormatValue(value);
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "null";
            }
            if (value is bool boolean)
            {
                return boolean ? "true" : "false";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string Read(
            Dictionary<string, object> data,
            string key,
            string fallback)
        {
            return data.TryGetValue(key, out object value)
                ? FormatValue(value)
                : fallback;
        }

        private static int Count(Dictionary<string, object> data, string key)
        {
            return data.TryGetValue(key, out object value)
                ? Enumerate(value).Count()
                : 0;
        }

        private static string ReadIndices(Dictionary<string, object> data)
        {
            if (!data.TryGetValue("idx", out object value) &&
                !data.TryGetValue("idxList", out value))
            {
                return string.Empty;
            }
            if (value is string || !(value is IEnumerable))
            {
                return FormatValue(value);
            }
            return string.Join(",", Enumerate(value).Select(FormatValue));
        }

        private static IEnumerable<object> Enumerate(object value)
        {
            if (value is string || value == null)
            {
                yield break;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    yield return item;
                }
            }
        }
    }
}
