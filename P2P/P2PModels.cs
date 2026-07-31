using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shadowbus
{
    internal sealed class CustomFormatDefinition
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("deckSizeLimit", NullValueHandling = NullValueHandling.Include)]
        public int? DeckSizeLimit { get; set; }

        [JsonProperty("sameCardLimit", NullValueHandling = NullValueHandling.Include)]
        public int? SameCardLimit { get; set; }

        [JsonProperty("tokenCardTotalLimit", NullValueHandling = NullValueHandling.Include)]
        public int? TokenCardTotalLimit { get; set; }

        [JsonProperty("tokenSameCardLimit", NullValueHandling = NullValueHandling.Include)]
        public int? TokenSameCardLimit { get; set; }

        [JsonProperty("cardLimits")]
        public Dictionary<int, int> CardLimits { get; set; } = new Dictionary<int, int>();

        internal CustomFormatDefinition Clone()
        {
            return new CustomFormatDefinition
            {
                Id = Id,
                DisplayName = DisplayName,
                DeckSizeLimit = DeckSizeLimit,
                SameCardLimit = SameCardLimit,
                TokenCardTotalLimit = TokenCardTotalLimit,
                TokenSameCardLimit = TokenSameCardLimit,
                CardLimits = CardLimits == null
                    ? new Dictionary<int, int>()
                    : new Dictionary<int, int>(CardLimits)
            };
        }
    }

    internal enum P2PRole
    {
        None,
        Host,
        Guest
    }

    internal sealed class P2PProfile
    {
        [JsonProperty("viewerId")]
        public int ViewerId { get; set; }

        [JsonProperty("userName")]
        public string UserName { get; set; }

        [JsonProperty("rank")]
        public int Rank { get; set; }

        [JsonProperty("battlePoint")]
        public int BattlePoint { get; set; }

        [JsonProperty("masterPoint")]
        public int MasterPoint { get; set; }

        [JsonProperty("degreeId")]
        public int DegreeId { get; set; }

        [JsonProperty("emblemId")]
        public long EmblemId { get; set; }

        [JsonProperty("countryCode")]
        public string CountryCode { get; set; }

        [JsonProperty("isOfficial")]
        public bool IsOfficial { get; set; }
    }

    internal sealed class P2PDeckSnapshot
    {
        [JsonProperty("cards")]
        public List<int> Cards { get; set; } = new List<int>();

        [JsonProperty("classId")]
        public int ClassId { get; set; }

        [JsonProperty("subclassId")]
        public int SubclassId { get; set; } = 10;

        [JsonProperty("charaId")]
        public int CharaId { get; set; }

        [JsonProperty("sleeveId")]
        public long SleeveId { get; set; }
    }

    internal sealed class P2PTwoPickClassRuleDefinition
    {
        [JsonProperty("cardClasses", NullValueHandling = NullValueHandling.Include)]
        public List<int> CardClasses { get; set; }

        [JsonProperty("additionalCards")]
        public List<int> AdditionalCards { get; set; } = new List<int>();

        [JsonProperty("description", NullValueHandling = NullValueHandling.Include)]
        public string Description { get; set; }

        internal P2PTwoPickClassRuleDefinition Clone()
        {
            return new P2PTwoPickClassRuleDefinition
            {
                CardClasses = CardClasses == null
                    ? null
                    : new List<int>(CardClasses),
                AdditionalCards = AdditionalCards == null
                    ? new List<int>()
                    : new List<int>(AdditionalCards),
                Description = Description
            };
        }
    }

    internal sealed class P2PTwoPickRoundRuleDefinition
    {
        [JsonProperty("rounds")]
        public List<int> Rounds { get; set; } = new List<int>();

        [JsonProperty("costs", NullValueHandling = NullValueHandling.Include)]
        public List<int> Costs { get; set; }

        [JsonProperty("rarities", NullValueHandling = NullValueHandling.Include)]
        public List<int> Rarities { get; set; }

        [JsonProperty("cards", NullValueHandling = NullValueHandling.Include)]
        public List<int> Cards { get; set; }

        internal P2PTwoPickRoundRuleDefinition Clone()
        {
            return new P2PTwoPickRoundRuleDefinition
            {
                Rounds = Rounds == null ? new List<int>() : new List<int>(Rounds),
                Costs = Costs == null ? null : new List<int>(Costs),
                Rarities = Rarities == null ? null : new List<int>(Rarities),
                Cards = Cards == null ? null : new List<int>(Cards)
            };
        }
    }

    internal sealed class P2PTwoPickRuleDefinition
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("finalDeckSize")]
        public int FinalDeckSize { get; set; } = 30;

        [JsonProperty("candidateClassCount")]
        public int CandidateClassCount { get; set; } = 3;

        [JsonProperty("offersPerRound")]
        public int OffersPerRound { get; set; } = 2;

        [JsonProperty("cardsPerOffer")]
        public int CardsPerOffer { get; set; } = 2;

        [JsonProperty("allowDuplicatePicks")]
        public bool AllowDuplicatePicks { get; set; } = true;

        [JsonProperty("sameCardLimit", NullValueHandling = NullValueHandling.Include)]
        public int? SameCardLimit { get; set; }

        [JsonProperty("candidateClasses", NullValueHandling = NullValueHandling.Include)]
        public List<int> CandidateClasses { get; set; }

        [JsonProperty("classRules")]
        public Dictionary<int, P2PTwoPickClassRuleDefinition> ClassRules { get; set; } =
            new Dictionary<int, P2PTwoPickClassRuleDefinition>();

        [JsonProperty("roundRules")]
        public List<P2PTwoPickRoundRuleDefinition> RoundRules { get; set; } =
            new List<P2PTwoPickRoundRuleDefinition>();

        [JsonProperty("cardPool", NullValueHandling = NullValueHandling.Include)]
        public List<int> CardPool { get; set; }

        [JsonProperty("excludedCards")]
        public List<int> ExcludedCards { get; set; } = new List<int>();

        [JsonProperty("cardWeights")]
        public Dictionary<int, int> CardWeights { get; set; } =
            new Dictionary<int, int>();

        internal P2PTwoPickRuleDefinition Clone()
        {
            return new P2PTwoPickRuleDefinition
            {
                Id = Id,
                DisplayName = DisplayName,
                FinalDeckSize = FinalDeckSize,
                CandidateClassCount = CandidateClassCount,
                OffersPerRound = OffersPerRound,
                CardsPerOffer = CardsPerOffer,
                AllowDuplicatePicks = AllowDuplicatePicks,
                SameCardLimit = SameCardLimit,
                CandidateClasses = CandidateClasses == null
                    ? null
                    : new List<int>(CandidateClasses),
                ClassRules = ClassRules == null
                    ? new Dictionary<int, P2PTwoPickClassRuleDefinition>()
                    : ClassRules.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value?.Clone() ??
                            new P2PTwoPickClassRuleDefinition()),
                RoundRules = RoundRules == null
                    ? new List<P2PTwoPickRoundRuleDefinition>()
                    : RoundRules.Select(rule =>
                        rule?.Clone() ?? new P2PTwoPickRoundRuleDefinition()).ToList(),
                CardPool = CardPool == null ? null : new List<int>(CardPool),
                ExcludedCards = ExcludedCards == null
                    ? new List<int>()
                    : new List<int>(ExcludedCards),
                CardWeights = CardWeights == null
                    ? new Dictionary<int, int>()
                    : new Dictionary<int, int>(CardWeights)
            };
        }
    }

    internal sealed class P2PRoomRules
    {
        internal const int DefaultInitialMaxLife = 20;
        internal const int MinimumInitialMaxLife = 20;
        internal const int MaximumInitialMaxLife = 200;

        private int initialMaxLife = DefaultInitialMaxLife;

        [JsonProperty("battleType")]
        public int BattleType { get; set; }

        [JsonProperty("deckFormat")]
        public int DeckFormat { get; set; }

        [JsonProperty("customFormatId")]
        public string CustomFormatId { get; set; } = "unlimited";

        [JsonProperty("formatDefinition", NullValueHandling = NullValueHandling.Ignore)]
        public CustomFormatDefinition FormatDefinition { get; set; }

        [JsonProperty("twoPickType")]
        public int TwoPickType { get; set; }

        [JsonProperty("twoPickRule", NullValueHandling = NullValueHandling.Ignore)]
        public P2PTwoPickRuleDefinition TwoPickRule { get; set; }

        [JsonProperty("battleRule")]
        public int BattleRule { get; set; }

        [JsonProperty("isDeckOpen")]
        public bool IsDeckOpen { get; set; }

        [JsonProperty("initialMaxLife")]
        public int InitialMaxLife
        {
            get => initialMaxLife;
            set => initialMaxLife = ClampInitialMaxLife(value);
        }

        internal static int ClampInitialMaxLife(int value)
        {
            if (value < MinimumInitialMaxLife)
            {
                return MinimumInitialMaxLife;
            }
            if (value > MaximumInitialMaxLife)
            {
                return MaximumInitialMaxLife;
            }
            return value;
        }
    }

    internal sealed class P2PWireMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("viewerId", NullValueHandling = NullValueHandling.Ignore)]
        public int ViewerId { get; set; }

        [JsonProperty("battleId", NullValueHandling = NullValueHandling.Ignore)]
        public string BattleId { get; set; }

        [JsonProperty("profile", NullValueHandling = NullValueHandling.Ignore)]
        public P2PProfile Profile { get; set; }

        [JsonProperty("deck", NullValueHandling = NullValueHandling.Ignore)]
        public P2PDeckSnapshot Deck { get; set; }

        [JsonProperty("rules", NullValueHandling = NullValueHandling.Ignore)]
        public P2PRoomRules Rules { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Data { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }
    }

    internal static class P2PJson
    {
        internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None
        };

        internal static P2PWireMessage DeserializeMessage(string json)
        {
            JObject root = JObject.Parse(json);
            P2PWireMessage message = root.ToObject<P2PWireMessage>(JsonSerializer.Create(Settings));
            if (root["data"] != null)
            {
                message.Data = ConvertToken(root["data"]) as Dictionary<string, object>;
            }
            return message;
        }

        internal static Dictionary<string, object> CloneDictionary(Dictionary<string, object> source)
        {
            if (source == null)
            {
                return new Dictionary<string, object>();
            }
            string json = JsonConvert.SerializeObject(source, Settings);
            return ConvertToken(JObject.Parse(json)) as Dictionary<string, object>;
        }

        private static object ConvertToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }
            if (token.Type == JTokenType.Object)
            {
                Dictionary<string, object> result = new Dictionary<string, object>();
                foreach (JProperty property in ((JObject)token).Properties())
                {
                    result[property.Name] = ConvertToken(property.Value);
                }
                return result;
            }
            if (token.Type == JTokenType.Array)
            {
                List<object> result = new List<object>();
                foreach (JToken item in (JArray)token)
                {
                    result.Add(ConvertToken(item));
                }
                return result;
            }
            if (token.Type == JTokenType.Integer)
            {
                long value = token.Value<long>();
                return value >= int.MinValue && value <= int.MaxValue ? (object)(int)value : value;
            }
            if (token.Type == JTokenType.Float)
            {
                return token.Value<double>();
            }
            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }
            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }
            return ((JValue)token).Value;
        }
    }

    internal static class P2PMessageTransform
    {
        internal static Dictionary<string, object> FlipPerspective(
            Dictionary<string, object> source)
        {
            Dictionary<string, object> result = P2PJson.CloneDictionary(source);
            FlipDictionary(result);
            return result;
        }

        internal static Dictionary<string, object> PrepareOpponentBattleMessage(
            Dictionary<string, object> source)
        {
            Dictionary<string, object> result = FlipPerspective(source);
            NormalizeOpponentKeyActions(result);
            if (result.TryGetValue("targetList", out object targets))
            {
                // The client emits targetList, but live opponent messages use
                // oppoTargetList so the receiver reads action-relative isSelf values.
                result.Remove("targetList");
                result["oppoTargetList"] = targets;
            }
            return result;
        }

        private static void NormalizeOpponentKeyActions(
            Dictionary<string, object> message)
        {
            if (!message.TryGetValue("keyAction", out object rawKeyActions) ||
                !(rawKeyActions is List<object> keyActions))
            {
                return;
            }

            const int ChoiceKeyAction = 1;
            const int HaveBeforeSkillChoiceKeyAction = 5;
            const int BurialRiteKeyAction = 6;
            const int ChoiceEvolutionKeyAction = 7;
            const int ChoiceBraveKeyAction = 8;
            foreach (object rawKeyAction in keyActions)
            {
                if (!(rawKeyAction is Dictionary<string, object> keyAction) ||
                    !TryConvertInt(keyAction.TryGetValue("type", out object rawType)
                        ? rawType : null, out int type) ||
                    !keyAction.TryGetValue("selectCard", out object rawSelection) ||
                    !(rawSelection is Dictionary<string, object> selection))
                {
                    continue;
                }

                if (type == BurialRiteKeyAction &&
                    !keyAction.ContainsKey("cardIdx") &&
                    selection.TryGetValue("cardIdx", out object selectedIndices))
                {
                    // SendKeyActionDataManager emits selectCard.cardIdx, while the live
                    // battle receiver expects the server response at keyAction.cardIdx.
                    keyAction["cardIdx"] = selectedIndices;
                }

                if ((type == ChoiceKeyAction ||
                        type == HaveBeforeSkillChoiceKeyAction ||
                        type == ChoiceEvolutionKeyAction ||
                        type == ChoiceBraveKeyAction) &&
                    selection.TryGetValue("cardId", out object selectedCardIds))
                {
                    // Choice requests wrap the selected IDs in selectCard.cardId. The
                    // server flattens that wrapper before NetworkBattleReceiver sees it.
                    keyAction["selectCard"] = selectedCardIds;
                }
            }
        }

        private static void FlipDictionary(Dictionary<string, object> dictionary)
        {
            foreach (string key in new List<string>(dictionary.Keys))
            {
                object value = dictionary[key];
                if (string.Equals(key, "isSelf", StringComparison.Ordinal))
                {
                    dictionary[key] = FlipSideValue(value);
                }
                else if (string.Equals(key, "targetList", StringComparison.Ordinal) ||
                    string.Equals(key, "oppoTargetList", StringComparison.Ordinal))
                {
                    // Target sides are relative to the acting player, not the receiver.
                }
                else
                {
                    FlipNestedValue(value);
                }
            }

            bool hasSelfSeed = dictionary.TryGetValue("idxChangeSeed", out object selfSeed);
            bool hasOpponentSeed = dictionary.TryGetValue("oppoIdxChangeSeed", out object opponentSeed);
            dictionary.Remove("idxChangeSeed");
            dictionary.Remove("oppoIdxChangeSeed");
            if (hasSelfSeed)
            {
                dictionary["oppoIdxChangeSeed"] = selfSeed;
            }
            if (hasOpponentSeed)
            {
                dictionary["idxChangeSeed"] = opponentSeed;
            }
        }

        private static void FlipNestedValue(object value)
        {
            if (value is Dictionary<string, object> dictionary)
            {
                FlipDictionary(dictionary);
                return;
            }
            if (value is List<object> list)
            {
                foreach (object item in list)
                {
                    FlipNestedValue(item);
                }
            }
        }

        private static object FlipSideValue(object value)
        {
            if (value is bool boolean)
            {
                return !boolean;
            }
            try
            {
                int side = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (side == 0 || side == 1)
                {
                    return side == 0 ? 1 : 0;
                }
            }
            catch (Exception)
            {
            }
            return value;
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
