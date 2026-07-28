using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shadowbus
{
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
        internal const string TurnStartUri = "TurnStart";
        internal const string JudgeUri = "Judge";
        internal const string EchoUri = "Echo";

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
        private bool isBurialRite;

        internal void Reset()
        {
            burialTargets.Clear();
            announcedSkillIndexes.Clear();
            playIndex = -1;
            isBurialRite = false;
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
                bool hadBurialRite = isBurialRite;
                Reset();
                summary = hadBurialRite ? "cancelled" : string.Empty;
                return hadBurialRite;
            }

            if (operation == StartSelectOperation)
            {
                Reset();
                if (!burialRite || parameters.Count < 4 ||
                    !TryConvertInt(parameters[3], out playIndex))
                {
                    return false;
                }

                isBurialRite = true;
                CaptureAnnouncedSkillIndexes(parameters);
                summary = $"started playIdx={playIndex}, " +
                    $"skills=[{string.Join(",", announcedSkillIndexes)}]";
                return true;
            }

            if ((operation != SelectCardOperation &&
                    operation != CompleteSelectOperation) ||
                (!burialRite && !isBurialRite) || parameters.Count < 4 ||
                !TryParseTarget(parameters[3], out SelectionTarget target))
            {
                return false;
            }

            isBurialRite = true;
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
            if (!isBurialRite || burialTargets.Count == 0 || data == null ||
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
                outgoingPlayIndex, data, burialSkillResolver);
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
            data["type"] = 31;
            summary = $"playIdx={outgoingPlayIndex}, " +
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

        internal void Reset(IList<int> hostDeck, IList<int> guestDeck)
        {
            hostCards.Clear();
            guestCards.Clear();
            RegisterDeck(hostCards, hostDeck);
            RegisterDeck(guestCards, guestDeck);
        }

        internal void Clear()
        {
            hostCards.Clear();
            guestCards.Clear();
        }

        internal bool PrepareOutgoingAction(
            bool sourceIsHost,
            Dictionary<string, object> data,
            out int playIndex,
            out int cardId,
            Func<int, int> sourceCardIdResolver = null,
            Func<int, int> sourceCardCostResolver = null)
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
                sourceCardIdResolver, sourceCardCostResolver);
            RevealFusionIngredients(sourceIsHost, data,
                sourceCardIdResolver, sourceCardCostResolver);
            RevealOpenedHandCards(sourceIsHost, data,
                sourceCardIdResolver, sourceCardCostResolver);

            if (!TryGetInt(data, "playIdx", out playIndex) || playIndex < 0)
            {
                return false;
            }

            if (!TryResolveSourceCardId(sourceIsHost, playIndex,
                    sourceCardIdResolver, out cardId))
            {
                return false;
            }

            int? cost = ResolveSourceCardCost(playIndex, sourceCardCostResolver);
            RevealKnownCard(data, playIndex, cardId, true, cost);
            return true;
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
            Func<int, int> sourceCardCostResolver)
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
                            sourceCardIdResolver, sourceCardCostResolver);
                    }
                }
            }
            if (data.TryGetValue("uList", out object rawUnapprovedList))
            {
                foreach (object item in Enumerate(rawUnapprovedList))
                {
                    RevealPublicMove(sourceIsHost, data,
                        item as Dictionary<string, object>,
                        sourceCardIdResolver, sourceCardCostResolver);
                }
            }
        }

        private void RevealOpenedHandCards(
            bool sourceIsHost,
            Dictionary<string, object> data,
            Func<int, int> sourceCardIdResolver,
            Func<int, int> sourceCardCostResolver)
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
                            index, sourceCardCostResolver);
                        RevealKnownCard(data, index, cardId, true, cost);
                    }
                }
            }
        }

        private void RevealFusionIngredients(
            bool sourceIsHost,
            Dictionary<string, object> data,
            Func<int, int> sourceCardIdResolver,
            Func<int, int> sourceCardCostResolver)
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

                    int? cost = ResolveSourceCardCost(index, sourceCardCostResolver);
                    RevealKnownCard(data, index, cardId, true, cost, 10, 60);
                }
            }
        }

        private void RevealPublicMove(
            bool sourceIsHost,
            Dictionary<string, object> data,
            Dictionary<string, object> move,
            Func<int, int> sourceCardIdResolver,
            Func<int, int> sourceCardCostResolver)
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
                    int? cost = ResolveSourceCardCost(index, sourceCardCostResolver);
                    RevealKnownCard(data, index, cardId, true, cost, from, to);
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
            int? to = null)
        {
            List<object> knownList = GetOrCreateObjectList(data, "knownList");
            foreach (object item in knownList)
            {
                Dictionary<string, object> known = item as Dictionary<string, object>;
                if (known == null || IsSelf(known) != isSelf ||
                    !GetIndices(known).Contains(index))
                {
                    continue;
                }
                known["cardId"] = cardId;
                known["is_open"] = 1;
                AddKnownCardState(known, cost, from, to);
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
            knownList.Add(revealed);
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

        private static int? ResolveSourceCardCost(
            int index,
            Func<int, int> sourceCardCostResolver)
        {
            if (sourceCardCostResolver == null)
            {
                return null;
            }
            try
            {
                int cost = sourceCardCostResolver(index);
                return cost >= 0 ? (int?)cost : null;
            }
            catch (Exception)
            {
                return null;
            }
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
    }
}
