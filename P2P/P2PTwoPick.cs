using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wizard;

namespace Shadowbus
{
    internal sealed class P2PTwoPickOffer
    {
        internal int Id { get; set; }
        internal int Turn { get; set; }
        internal int SetNumber { get; set; }
        internal int CardId1 { get; set; }
        internal int CardId2 { get; set; }
    }

    internal sealed class P2PTwoPickDraftSession
    {
        private readonly Random random = new Random(Guid.NewGuid().GetHashCode());
        private P2PTwoPickRuleDefinition rule;

        internal int SelectedClassId { get; private set; }
        internal bool IsComplete { get; private set; }
        internal List<int> Deck { get; } = new List<int>();
        internal List<int> CandidateClasses { get; } = new List<int>();
        internal List<P2PTwoPickOffer> Offers { get; } =
            new List<P2PTwoPickOffer>();
        internal P2PTwoPickRuleDefinition Rule => rule?.Clone();

        internal void Reset(P2PTwoPickRuleDefinition definition)
        {
            rule = P2PTwoPickRules.Normalize(definition);
            SelectedClassId = 0;
            IsComplete = false;
            Deck.Clear();
            CandidateClasses.Clear();
            Offers.Clear();
        }

        internal void Begin(Func<int, int, IList<int>> poolFactory)
        {
            EnsureRule();
            CandidateClasses.Clear();
            int roundCount = rule.FinalDeckSize / rule.CardsPerOffer;
            int cardsNeededPerRound = rule.CardsPerOffer * rule.OffersPerRound;
            CandidateClasses.AddRange(rule.CandidateClasses.Where(classId =>
            {
                return Enumerable.Range(1, roundCount).All(round =>
                {
                    IList<int> pool = poolFactory?.Invoke(classId, round);
                    return pool != null &&
                        pool.Distinct().Count() >= cardsNeededPerRound;
                });
            }));
            if (CandidateClasses.Count < rule.CandidateClassCount)
            {
                throw new InvalidOperationException(
                    $"The Two Pick rule has only {CandidateClasses.Count} class(es) " +
                    "with enough usable cards; at least 3 are required.");
            }
            Shuffle(CandidateClasses);
            if (CandidateClasses.Count > rule.CandidateClassCount)
            {
                CandidateClasses.RemoveRange(
                    rule.CandidateClassCount,
                    CandidateClasses.Count - rule.CandidateClassCount);
            }
            Plugin.Logger.LogInfo(
                $"[P2P] Generated local Two Pick class candidates " +
                $"[{string.Join(",", CandidateClasses)}] for {rule.Id}.");
        }

        internal void SelectClass(int classId, Func<int, int, IList<int>> poolFactory)
        {
            EnsureRule();
            if (!CandidateClasses.Contains(classId))
            {
                throw new InvalidOperationException(
                    $"Class {classId} was not offered by the local Two Pick draft.");
            }
            SelectedClassId = classId;
            Deck.Clear();
            IsComplete = false;
            GenerateOffers(poolFactory);
        }

        internal void SelectCard(int offerId, Func<int, int, IList<int>> poolFactory)
        {
            EnsureRule();
            if (IsComplete || SelectedClassId == 0)
            {
                throw new InvalidOperationException("The Two Pick draft is not awaiting a card pick.");
            }

            P2PTwoPickOffer offer = Offers.FirstOrDefault(item => item.Id == offerId);
            if (offer == null)
            {
                throw new InvalidOperationException(
                    $"Card offer {offerId} is not active in the local Two Pick draft.");
            }

            Deck.Add(offer.CardId1);
            Deck.Add(offer.CardId2);
            if (Deck.Count >= rule.FinalDeckSize)
            {
                Deck.RemoveRange(rule.FinalDeckSize, Deck.Count - rule.FinalDeckSize);
                IsComplete = true;
                Offers.Clear();
                return;
            }

            GenerateOffers(poolFactory);
        }

        private void GenerateOffers(Func<int, int, IList<int>> poolFactory)
        {
            Offers.Clear();
            int turn = Deck.Count / rule.CardsPerOffer + 1;
            IList<int> pool = poolFactory(SelectedClassId, turn);
            if (pool == null || pool.Count < rule.CardsPerOffer * rule.OffersPerRound)
            {
                throw new InvalidOperationException(
                    $"The Two Pick card pool for class {SelectedClassId} has " +
                    $"fewer than {rule.CardsPerOffer * rule.OffersPerRound} usable cards.");
            }

            HashSet<int> offeredThisRound = new HashSet<int>();
            for (int index = 0; index < rule.OffersPerRound; index++)
            {
                List<int> available = new List<int>(pool.Distinct());
                List<int> picked = new List<int>(rule.CardsPerOffer);
                for (int cardIndex = 0; cardIndex < rule.CardsPerOffer; cardIndex++)
                {
                    available.RemoveAll(card =>
                        offeredThisRound.Contains(card) || !CanPick(card));
                    if (available.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "The configured Two Pick card pool cannot satisfy its " +
                            "duplicate-card rule for the remaining picks.");
                    }

                    int selected = PickWeighted(available);
                    picked.Add(selected);
                    offeredThisRound.Add(selected);
                    available.Remove(selected);
                }

                Offers.Add(new P2PTwoPickOffer
                {
                    Id = turn * 100 + index + 1,
                    Turn = turn,
                    SetNumber = index + 1,
                    CardId1 = picked[0],
                    CardId2 = picked[1]
                });
            }
        }

        private bool CanPick(int cardId)
        {
            if (rule.AllowDuplicatePicks && !rule.SameCardLimit.HasValue)
            {
                return true;
            }
            int limit = rule.SameCardLimit ?? 1;
            return Deck.Count(card => card == cardId) < limit;
        }

        private int PickWeighted(IList<int> cards)
        {
            long totalWeight = 0;
            foreach (int card in cards)
            {
                int weight = 1;
                if (rule.CardWeights != null && rule.CardWeights.TryGetValue(card, out int configured))
                {
                    weight = Math.Max(1, configured);
                }
                totalWeight += weight;
            }

            double roll = random.NextDouble() * totalWeight;
            foreach (int card in cards)
            {
                int weight = 1;
                if (rule.CardWeights != null && rule.CardWeights.TryGetValue(card, out int configured))
                {
                    weight = Math.Max(1, configured);
                }
                if (roll < weight)
                {
                    return card;
                }
                roll -= weight;
            }
            return cards[cards.Count - 1];
        }

        private void Shuffle(IList<int> values)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(index + 1);
                int value = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = value;
            }
        }

        private void EnsureRule()
        {
            if (rule == null)
            {
                Reset(P2PTwoPickRules.Load());
            }
        }
    }

    internal static class P2PTwoPickRules
    {
        internal const string DefaultId = "normal";
        private static readonly object Sync = new object();
        private static readonly P2PTwoPickDraftSession Draft =
            new P2PTwoPickDraftSession();
        private static string selectedRuleId = DefaultId;

        internal static string RulesPath =>
            Path.Combine(PathHelper.TwoPickPath, DefaultId + ".json");

        internal static void Initialize()
        {
            Directory.CreateDirectory(PathHelper.TwoPickPath);
            if (!File.Exists(RulesPath))
            {
                P2PTwoPickRuleDefinition definition = CreateDefault();
                File.WriteAllText(
                    RulesPath,
                    JsonConvert.SerializeObject(
                        definition,
                        Formatting.Indented,
                        P2PJson.Settings));
            }
        }

        internal static IReadOnlyList<P2PTwoPickRuleDefinition> LoadAll()
        {
            Initialize();
            IReadOnlyList<P2PTwoPickRuleDefinition> definitions =
                P2PTwoPickRuleFiles.Load(
                    PathHelper.TwoPickPath,
                    P2PJson.Settings,
                    (source, fileId) => Normalize(source),
                    message => Plugin.Logger.LogError("[P2P] " + message));
            if (definitions.Count > 0)
            {
                return definitions;
            }

            Plugin.Logger.LogWarning(
                "[P2P] No valid Two Pick rule files were found; using the built-in default.");
            return new List<P2PTwoPickRuleDefinition>
            {
                Normalize(CreateDefault())
            }.AsReadOnly();
        }

        internal static P2PTwoPickRuleDefinition Load(string id = DefaultId)
        {
            string normalizedId = string.IsNullOrWhiteSpace(id) ? DefaultId : id.Trim();
            IReadOnlyList<P2PTwoPickRuleDefinition> definitions = LoadAll();
            P2PTwoPickRuleDefinition definition = definitions.FirstOrDefault(item =>
                string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                definition = definitions.FirstOrDefault(item =>
                    string.Equals(item.Id, DefaultId, StringComparison.OrdinalIgnoreCase)) ??
                    definitions[0];
                Plugin.Logger.LogWarning(
                    $"[P2P] Two Pick rule '{normalizedId}' was not found; " +
                    $"using '{definition.Id}'.");
            }
            return definition.Clone();
        }

        internal static string SelectedRuleId
        {
            get
            {
                lock (Sync)
                {
                    return selectedRuleId;
                }
            }
        }

        internal static P2PTwoPickRuleDefinition LoadSelected()
        {
            lock (Sync)
            {
                P2PTwoPickRuleDefinition definition = Load(selectedRuleId);
                selectedRuleId = definition.Id;
                return definition;
            }
        }

        internal static P2PTwoPickRuleDefinition Select(string id)
        {
            lock (Sync)
            {
                P2PTwoPickRuleDefinition definition = Load(id);
                selectedRuleId = definition.Id;
                return definition;
            }
        }

        internal static P2PTwoPickRuleDefinition Normalize(
            P2PTwoPickRuleDefinition source)
        {
            P2PTwoPickRuleDefinition definition = source?.Clone() ?? CreateDefault();
            definition.Id = string.IsNullOrWhiteSpace(definition.Id)
                ? DefaultId
                : definition.Id.Trim();
            definition.DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.Id
                : definition.DisplayName.Trim();
            if (definition.FinalDeckSize < 6 || definition.FinalDeckSize > 200 ||
                definition.FinalDeckSize % 2 != 0)
            {
                throw new FormatException(
                    "Two Pick finalDeckSize must be an even number from 6 to 200.");
            }
            if (definition.CandidateClassCount != 3 ||
                definition.OffersPerRound != 2 || definition.CardsPerOffer != 2)
            {
                throw new FormatException(
                    "The current Two Pick UI requires 3 classes, 2 offers, and 2 cards per offer.");
            }
            definition.CandidateClasses = (definition.CandidateClasses ??
                Enumerable.Range(1, 8).ToList()).Distinct().Where(id => id >= 1 && id <= 8).ToList();
            if (definition.CandidateClasses.Count < definition.CandidateClassCount)
            {
                throw new FormatException("Two Pick candidateClasses must contain at least 3 classes.");
            }
            definition.ClassRules = NormalizeClassRules(definition.ClassRules);
            definition.RoundRules = NormalizeRoundRules(
                definition.RoundRules,
                definition.FinalDeckSize / definition.CardsPerOffer);
            definition.ExcludedCards = (definition.ExcludedCards ?? new List<int>()).Distinct().ToList();
            definition.CardWeights = definition.CardWeights ?? new Dictionary<int, int>();
            if (definition.SameCardLimit.HasValue && definition.SameCardLimit.Value < 1)
            {
                throw new FormatException("Two Pick sameCardLimit must be positive or null.");
            }
            if (definition.CardPool != null)
            {
                definition.CardPool = definition.CardPool.Where(id => id > 0).Distinct().ToList();
            }
            return definition;
        }

        private static Dictionary<int, P2PTwoPickClassRuleDefinition> NormalizeClassRules(
            Dictionary<int, P2PTwoPickClassRuleDefinition> source)
        {
            Dictionary<int, P2PTwoPickClassRuleDefinition> result =
                new Dictionary<int, P2PTwoPickClassRuleDefinition>();
            foreach (KeyValuePair<int, P2PTwoPickClassRuleDefinition> pair in
                source ?? new Dictionary<int, P2PTwoPickClassRuleDefinition>())
            {
                if (pair.Key < 1 || pair.Key > 8)
                {
                    continue;
                }

                P2PTwoPickClassRuleDefinition classRule =
                    pair.Value?.Clone() ?? new P2PTwoPickClassRuleDefinition();
                if (classRule.CardClasses != null)
                {
                    classRule.CardClasses = classRule.CardClasses
                        .Where(classId => classId >= 0 && classId <= 8)
                        .Distinct()
                        .ToList();
                }
                classRule.AdditionalCards = (classRule.AdditionalCards ?? new List<int>())
                    .Where(cardId => cardId > 0)
                    .Distinct()
                    .ToList();
                classRule.Description = string.IsNullOrWhiteSpace(classRule.Description)
                    ? null
                    : classRule.Description.Trim();
                result[pair.Key] = classRule;
            }
            return result;
        }

        private static List<P2PTwoPickRoundRuleDefinition> NormalizeRoundRules(
            List<P2PTwoPickRoundRuleDefinition> source,
            int roundCount)
        {
            List<P2PTwoPickRoundRuleDefinition> result =
                new List<P2PTwoPickRoundRuleDefinition>();
            HashSet<int> configuredRounds = new HashSet<int>();
            foreach (P2PTwoPickRoundRuleDefinition sourceRule in
                source ?? new List<P2PTwoPickRoundRuleDefinition>())
            {
                if (sourceRule == null || sourceRule.Rounds == null ||
                    sourceRule.Rounds.Count == 0)
                {
                    throw new FormatException(
                        "Each Two Pick roundRules entry must contain at least one round.");
                }

                P2PTwoPickRoundRuleDefinition roundRule = sourceRule.Clone();
                roundRule.Rounds = roundRule.Rounds.Distinct().OrderBy(round => round).ToList();
                foreach (int round in roundRule.Rounds)
                {
                    if (round < 1 || round > roundCount)
                    {
                        throw new FormatException(
                            $"Two Pick round {round} is outside the valid range 1-{roundCount}.");
                    }
                    if (!configuredRounds.Add(round))
                    {
                        throw new FormatException(
                            $"Two Pick round {round} is configured more than once.");
                    }
                }

                roundRule.Costs = NormalizeOptionalValues(
                    roundRule.Costs,
                    value => value >= 0,
                    "Two Pick round costs must be zero or positive.");
                roundRule.Rarities = NormalizeOptionalValues(
                    roundRule.Rarities,
                    value => value >= 1 && value <= 4,
                    "Two Pick round rarities must be from 1 to 4.");
                roundRule.Cards = NormalizeOptionalValues(
                    roundRule.Cards,
                    value => value > 0,
                    "Two Pick round card IDs must be positive.");
                result.Add(roundRule);
            }
            return result.OrderBy(rule => rule.Rounds[0]).ToList();
        }

        private static List<int> NormalizeOptionalValues(
            List<int> source,
            Func<int, bool> isValid,
            string error)
        {
            if (source == null || source.Count == 0)
            {
                return null;
            }
            if (source.Any(value => !isValid(value)))
            {
                throw new FormatException(error);
            }
            return source.Distinct().OrderBy(value => value).ToList();
        }

        internal static P2PTwoPickRuleDefinition CreateDefault()
        {
            return new P2PTwoPickRuleDefinition
            {
                Id = DefaultId,
                DisplayName = "\u6807\u51c6\u53cc\u9009",
                FinalDeckSize = 30,
                CandidateClassCount = 3,
                OffersPerRound = 2,
                CardsPerOffer = 2,
                AllowDuplicatePicks = true,
                CandidateClasses = Enumerable.Range(1, 8).ToList(),
                ClassRules = new Dictionary<int, P2PTwoPickClassRuleDefinition>(),
                RoundRules = new List<P2PTwoPickRoundRuleDefinition>(),
                CardPool = null,
                ExcludedCards = new List<int>(),
                CardWeights = new Dictionary<int, int>()
            };
        }

        internal static void ResetDraft(P2PTwoPickRuleDefinition definition = null)
        {
            lock (Sync)
            {
                Draft.Reset(definition ?? Load());
            }
        }

        internal static int[] BeginDraft(P2PTwoPickRuleDefinition definition = null)
        {
            lock (Sync)
            {
                if (definition != null)
                {
                    Draft.Reset(definition);
                }
                Draft.Begin(BuildCardPool);
                return Draft.CandidateClasses.ToArray();
            }
        }

        internal static void SelectClass(int classId)
        {
            lock (Sync)
            {
                Draft.SelectClass(classId, BuildCardPool);
            }
        }

        internal static void SelectCard(int offerId)
        {
            lock (Sync)
            {
                Draft.SelectCard(offerId, BuildCardPool);
            }
        }

        internal static P2PTwoPickRuleDefinition ActiveRule
        {
            get
            {
                lock (Sync)
                {
                    return Draft.Rule ?? Normalize(P2PRuntime.Rules?.TwoPickRule ?? Load());
                }
            }
        }

        internal static int FinalDeckSize => P2PRuntime.IsTwoPickRoom
            ? P2PRuntime.Rules?.TwoPickRule?.FinalDeckSize ?? 30
            : 30;

        internal static List<int> Deck
        {
            get
            {
                lock (Sync)
                {
                    return new List<int>(Draft.Deck);
                }
            }
        }

        internal static bool IsComplete
        {
            get
            {
                lock (Sync)
                {
                    return Draft.IsComplete;
                }
            }
        }

        internal static List<P2PTwoPickOffer> Offers
        {
            get
            {
                lock (Sync)
                {
                    return new List<P2PTwoPickOffer>(Draft.Offers);
                }
            }
        }

        internal static int SelectedClassId
        {
            get
            {
                lock (Sync)
                {
                    return Draft.SelectedClassId;
                }
            }
        }

        internal static List<int> BuildCardPool(int classId, int round)
        {
            P2PTwoPickRuleDefinition definition = ActiveRule;
            HashSet<int> excluded = new HashSet<int>(definition.ExcludedCards ?? new List<int>());
            P2PTwoPickRoundRuleDefinition roundRule = definition.RoundRules
                .FirstOrDefault(rule => rule.Rounds.Contains(round));
            bool usesSpecificCards = roundRule?.Cards != null && roundRule.Cards.Count > 0;
            definition.ClassRules.TryGetValue(
                classId,
                out P2PTwoPickClassRuleDefinition classRule);
            HashSet<int> cardClasses = classRule?.CardClasses == null
                ? new HashSet<int> { 0, classId }
                : new HashSet<int>(classRule.CardClasses);
            HashSet<int> additionalCards = new HashSet<int>(
                classRule?.AdditionalCards ?? new List<int>());
            List<int> source;
            if (usesSpecificCards)
            {
                source = new List<int>(roundRule.Cards);
            }
            else
            {
                source = definition.CardPool == null
                    ? GetAllCardIds()
                    : new List<int>(definition.CardPool);
                source.AddRange(additionalCards);
            }
            CardMaster master = CardMaster.GetInstanceForBattle();
            List<int> result = new List<int>();
            HashSet<int> added = new HashSet<int>();
            foreach (int cardId in source)
            {
                if (cardId <= 0 || excluded.Contains(cardId) || !added.Add(cardId))
                {
                    continue;
                }
                CardParameter parameter = master?.GetCardParameterFromId(cardId);
                bool isAdditionalCard = additionalCards.Contains(cardId);
                if (parameter == null || !IsUsableCard(
                        parameter,
                        usesSpecificCards || definition.CardPool != null || isAdditionalCard))
                {
                    continue;
                }
                if (!usesSpecificCards && !isAdditionalCard &&
                    !cardClasses.Contains((int)parameter.Clan))
                {
                    continue;
                }
                if (!usesSpecificCards && roundRule?.Costs != null &&
                    !roundRule.Costs.Contains(parameter.Cost))
                {
                    continue;
                }
                if (!usesSpecificCards && roundRule?.Rarities != null &&
                    !roundRule.Rarities.Contains(parameter.Rarity))
                {
                    continue;
                }
                result.Add(cardId);
            }
            return result;
        }

        internal static string GetClassDescription(int classId)
        {
            P2PTwoPickRuleDefinition definition = ActiveRule;
            return definition.ClassRules.TryGetValue(
                classId,
                out P2PTwoPickClassRuleDefinition classRule)
                ? classRule.Description
                : null;
        }

        private static bool IsUsableCard(CardParameter parameter, bool explicitPool)
        {
            if (explicitPool)
            {
                return parameter.CharType == CardBasePrm.CharaType.NORMAL ||
                    parameter.CharType == CardBasePrm.CharaType.FIELD ||
                    parameter.CharType == CardBasePrm.CharaType.CHANT_FIELD ||
                    parameter.CharType == CardBasePrm.CharaType.SPELL;
            }
            try
            {
                return !parameter.IsFoil && !parameter.IsTokenCard &&
                    !parameter.IsChoiceEvolutionCard && !parameter.IsPhantomCard &&
                    (parameter.CharType == CardBasePrm.CharaType.NORMAL ||
                        parameter.CharType == CardBasePrm.CharaType.FIELD ||
                        parameter.CharType == CardBasePrm.CharaType.CHANT_FIELD ||
                        parameter.CharType == CardBasePrm.CharaType.SPELL);
            }
            catch
            {
                return false;
            }
        }

        private static List<int> GetAllCardIds()
        {
            CardMaster master = CardMaster.GetInstanceForBattle();
            return master?.GetAllCardIds() ?? new List<int>();
        }

        internal static Dictionary<string, object> CreateCandidateData()
        {
            List<P2PTwoPickOffer> offers = Offers;
            List<object> result = new List<object>(offers.Count);
            foreach (P2PTwoPickOffer offer in offers)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["id"] = offer.Id,
                    ["turn"] = offer.Turn,
                    ["set_num"] = offer.SetNumber,
                    ["card_id_1"] = offer.CardId1,
                    ["card_id_2"] = offer.CardId2,
                    ["is_selected"] = 0
                });
            }
            return new Dictionary<string, object> { ["candidate_card_list"] = result };
        }

        internal static Dictionary<string, object> CreateDeckData()
        {
            return new Dictionary<string, object>
            {
                ["two_pick_entry_id"] = 1,
                ["class_id"] = SelectedClassId,
                ["is_select_completed"] = IsComplete ? 1 : 0,
                ["selected_card_ids"] = Deck.Cast<object>().ToList(),
                ["select_turn"] = Deck.Count / 2
            };
        }
    }
}
