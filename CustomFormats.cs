using LitJson;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Wizard;

namespace Shadowbus
{
    internal static class CustomFormats
    {
        internal const string UnlimitedId = "unlimited";
        internal const string ModernId = "modern";

        private static readonly Dictionary<string, CustomFormatDefinition> Definitions =
            new Dictionary<string, CustomFormatDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerSettings FileJsonSettings =
            new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include
            };

        internal static IReadOnlyList<CustomFormatDefinition> All => Definitions.Values
            .OrderBy(definition => definition.Id == UnlimitedId ? 0 :
                definition.Id == ModernId ? 1 : 2)
            .ThenBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        internal static CustomFormatDefinition Unlimited => Get(UnlimitedId);

        internal static void Initialize()
        {
            Directory.CreateDirectory(PathHelper.FormatPath);
            WriteDefaultIfMissing(CreateUnlimitedDefinition());
            WriteDefaultIfMissing(CreateModernDefinition());
            Reload();
            CustomDeckStore.MigrateLegacyModernDecks();
            CustomDeckStore.MigrateLegacyDeckFilenames();
        }

        internal static void Reload()
        {
            Dictionary<string, CustomFormatDefinition> loadedDefinitions =
                new Dictionary<string, CustomFormatDefinition>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(
                PathHelper.FormatPath,
                "*.json",
                SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    CustomFormatDefinition definition = JsonConvert.DeserializeObject<CustomFormatDefinition>(
                        File.ReadAllText(file),
                        FileJsonSettings);
                    definition = Normalize(definition);
                    loadedDefinitions[definition.Id] = definition;
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        $"[CustomFormats] Failed to load format file {file}: {ex.Message}");
                }
            }

            EnsureBuiltInDefinition(
                loadedDefinitions,
                CreateUnlimitedDefinition());
            EnsureBuiltInDefinition(
                loadedDefinitions,
                CreateModernDefinition());
            Definitions.Clear();
            foreach (KeyValuePair<string, CustomFormatDefinition> item in loadedDefinitions)
            {
                Definitions[item.Key] = item.Value;
            }
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Loaded {Definitions.Count} format definition(s) from " +
                $"{PathHelper.FormatPath}.");
        }

        internal static bool ReloadForUi(string uiName)
        {
            try
            {
                Reload();
                Plugin.Logger.LogInfo(
                    $"[CustomFormats] Reloaded format files for {uiName}.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    $"[CustomFormats] Failed to reload format files for {uiName}; " +
                    $"keeping the current definitions: {ex}");
                return false;
            }
        }

        internal static CustomFormatDefinition Get(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && Definitions.TryGetValue(id, out var definition))
            {
                return definition;
            }
            if (Definitions.TryGetValue(UnlimitedId, out definition))
            {
                return definition;
            }
            return CreateUnlimitedDefinition();
        }

        internal static bool TryGet(string id, out CustomFormatDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                definition = null;
                return false;
            }
            return Definitions.TryGetValue(id, out definition);
        }

        internal static CustomFormatDefinition InstallRoomDefinition(
            CustomFormatDefinition received)
        {
            CustomFormatDefinition definition = Normalize(received);
            string file = GetDefinitionPath(definition.Id);
            Definitions[definition.Id] = definition;
            try
            {
                File.WriteAllText(
                    file,
                    JsonConvert.SerializeObject(definition, FileJsonSettings),
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    $"[CustomFormats] Installed room format {definition.Id} in memory, " +
                    $"but could not persist {file}: {ex.Message}");
                return definition;
            }
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Installed room format {definition.Id} from the host.");
            return definition;
        }

        internal static bool IsDeckCompliant(
            JsonData deck,
            CustomFormatDefinition definition,
            out string reason)
        {
            if (deck == null || !deck.IsObject || !deck.Keys.Contains("card_id_array") ||
                !deck["card_id_array"].IsArray)
            {
                reason = "card_id_array is missing";
                return false;
            }

            return IsDeckCompliant(
                deck["card_id_array"].Cast<JsonData>().Select(card => card.ToInt()),
                definition,
                out reason);
        }

        internal static bool IsDeckCompliant(
            IEnumerable<int> cardIds,
            CustomFormatDefinition definition,
            out string reason)
        {
            definition = definition ?? Unlimited;
            bool needsCardData = definition.SameCardLimit.HasValue ||
                definition.TokenCardTotalLimit.HasValue ||
                definition.TokenSameCardLimit.HasValue ||
                (definition.CardLimits != null && definition.CardLimits.Count > 0);
            bool compliant = IsDeckCompliant(
                cardIds,
                definition,
                needsCardData ? CardMaster.GetInstanceForBattle() : null,
                out CustomFormatViolation violation);
            reason = violation?.ToLogMessage();
            return compliant;
        }

        internal static bool IsDeckCompliant(
            IEnumerable<int> cardIds,
            CustomFormatDefinition definition,
            CardMaster cardMaster,
            out CustomFormatViolation violation)
        {
            definition = definition ?? Unlimited;
            List<int> cards = cardIds?.ToList() ?? new List<int>();
            if (definition.DeckSizeLimit.HasValue &&
                cards.Count > definition.DeckSizeLimit.Value)
            {
                violation = new CustomFormatViolation(
                    CustomFormatRule.DeckSize,
                    0,
                    cards.Count,
                    definition.DeckSizeLimit.Value);
                return false;
            }

            bool needsCardData = definition.SameCardLimit.HasValue ||
                definition.TokenCardTotalLimit.HasValue ||
                definition.TokenSameCardLimit.HasValue ||
                (definition.CardLimits != null && definition.CardLimits.Count > 0);
            if (!needsCardData)
            {
                violation = null;
                return true;
            }
            if (cardMaster == null)
            {
                violation = new CustomFormatViolation(
                    CustomFormatRule.CardDataUnavailable,
                    0,
                    0,
                    0);
                return false;
            }

            var resolvedCards = new List<KeyValuePair<int, CardParameter>>(cards.Count);
            foreach (int cardId in cards)
            {
                CardParameter parameter = cardMaster.GetCardParameterFromId(cardId);
                if (parameter == null)
                {
                    violation = new CustomFormatViolation(
                        CustomFormatRule.CardDataUnavailable,
                        cardId,
                        0,
                        0);
                    return false;
                }
                resolvedCards.Add(
                    new KeyValuePair<int, CardParameter>(cardId, parameter));
            }

            Dictionary<int, int> individualLimits = new Dictionary<int, int>();
            foreach (KeyValuePair<int, int> item in
                definition.CardLimits ?? new Dictionary<int, int>())
            {
                CardParameter parameter = cardMaster.GetCardParameterFromId(item.Key);
                int baseCardId = parameter?.BaseCardId ?? item.Key;
                if (!individualLimits.TryGetValue(baseCardId, out int currentLimit) ||
                    item.Value < currentLimit)
                {
                    individualLimits[baseCardId] = item.Value;
                }
            }

            int tokenTotal = resolvedCards.Count(item => item.Value.IsTokenCard);
            if (definition.TokenCardTotalLimit.HasValue &&
                tokenTotal > definition.TokenCardTotalLimit.Value)
            {
                violation = new CustomFormatViolation(
                    CustomFormatRule.TokenCardTotal,
                    0,
                    tokenTotal,
                    definition.TokenCardTotalLimit.Value);
                return false;
            }

            foreach (IGrouping<int, KeyValuePair<int, CardParameter>> group in
                resolvedCards.GroupBy(item => item.Value.BaseCardId))
            {
                int count = group.Count();
                int baseCardId = group.Key;
                bool isToken = group.First().Value.IsTokenCard;
                if (individualLimits.TryGetValue(baseCardId, out int individualLimit) &&
                    count > individualLimit)
                {
                    violation = new CustomFormatViolation(
                        CustomFormatRule.IndividualCard,
                        baseCardId,
                        count,
                        individualLimit);
                    return false;
                }
                if (isToken && definition.TokenSameCardLimit.HasValue &&
                    count > definition.TokenSameCardLimit.Value)
                {
                    violation = new CustomFormatViolation(
                        CustomFormatRule.TokenSameCard,
                        baseCardId,
                        count,
                        definition.TokenSameCardLimit.Value);
                    return false;
                }
                if (!isToken && definition.SameCardLimit.HasValue &&
                    count > definition.SameCardLimit.Value)
                {
                    violation = new CustomFormatViolation(
                        CustomFormatRule.SameCard,
                        baseCardId,
                        count,
                        definition.SameCardLimit.Value);
                    return false;
                }
            }

            violation = null;
            return true;
        }

        private static void WriteDefaultIfMissing(CustomFormatDefinition definition)
        {
            string file = GetDefinitionPath(definition.Id);
            if (File.Exists(file))
            {
                return;
            }
            File.WriteAllText(
                file,
                JsonConvert.SerializeObject(definition, FileJsonSettings),
                new UTF8Encoding(false));
        }

        private static void EnsureBuiltInDefinition(
            IDictionary<string, CustomFormatDefinition> definitions,
            CustomFormatDefinition fallback)
        {
            if (!definitions.ContainsKey(fallback.Id))
            {
                definitions[fallback.Id] = fallback;
            }
        }

        private static string GetDefinitionPath(string id)
        {
            return Path.Combine(PathHelper.FormatPath, id + ".json");
        }

        private static CustomFormatDefinition Normalize(CustomFormatDefinition definition)
        {
            if (definition == null)
            {
                throw new InvalidDataException("The format definition is empty.");
            }

            string id = (definition.Id ?? string.Empty).Trim().ToLowerInvariant();
            if (id.Length == 0 || id.Any(character =>
                !(character >= 'a' && character <= 'z') &&
                !(character >= '0' && character <= '9') &&
                character != '-' && character != '_'))
            {
                throw new InvalidDataException(
                    "Format IDs may contain only lowercase ASCII letters, digits, '-' and '_'.");
            }

            ValidateLimit(definition.DeckSizeLimit, "deckSizeLimit");
            ValidateLimit(definition.SameCardLimit, "sameCardLimit");
            ValidateLimit(definition.TokenCardTotalLimit, "tokenCardTotalLimit");
            ValidateLimit(definition.TokenSameCardLimit, "tokenSameCardLimit");
            Dictionary<int, int> cardLimits = definition.CardLimits ??
                new Dictionary<int, int>();
            if (cardLimits.Any(item => item.Key <= 0 || item.Value < 0))
            {
                throw new InvalidDataException(
                    "cardLimits requires positive card IDs and non-negative limits.");
            }

            definition.Id = id;
            definition.DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? id
                : definition.DisplayName.Trim();
            definition.CardLimits = new Dictionary<int, int>(cardLimits);
            return definition;
        }

        private static void ValidateLimit(int? value, string name)
        {
            if (value.HasValue && value.Value < 0)
            {
                throw new InvalidDataException(name + " cannot be negative.");
            }
        }

        private static CustomFormatDefinition CreateUnlimitedDefinition()
        {
            return new CustomFormatDefinition
            {
                Id = UnlimitedId,
                DisplayName = "\u65e0\u9650\u8d5b\u5236"
            };
        }

        private static CustomFormatDefinition CreateModernDefinition()
        {
            return new CustomFormatDefinition
            {
                Id = ModernId,
                DisplayName = "\u6469\u767b\u8d5b\u5236",
                TokenCardTotalLimit = 10
            };
        }
    }

    internal enum CustomFormatRule
    {
        DeckSize,
        SameCard,
        TokenCardTotal,
        TokenSameCard,
        IndividualCard,
        CardDataUnavailable
    }

    internal sealed class CustomFormatViolation
    {
        internal CustomFormatViolation(
            CustomFormatRule rule,
            int cardId,
            int actualCount,
            int limit)
        {
            Rule = rule;
            CardId = cardId;
            ActualCount = actualCount;
            Limit = limit;
        }

        internal CustomFormatRule Rule { get; }
        internal int CardId { get; }
        internal int ActualCount { get; }
        internal int Limit { get; }

        internal string ToLogMessage()
        {
            switch (Rule)
            {
                case CustomFormatRule.DeckSize:
                    return $"deck size {ActualCount} exceeds {Limit}";
                case CustomFormatRule.TokenCardTotal:
                    return $"token card count {ActualCount} exceeds {Limit}";
                case CustomFormatRule.CardDataUnavailable:
                    return CardId > 0
                        ? $"card {CardId} is missing from the card master"
                        : "card master is unavailable for format validation";
                default:
                    return $"card {CardId} count {ActualCount} exceeds {Limit}";
            }
        }
    }

    internal static class CustomFormatViolationText
    {
        internal static string Describe(
            CustomFormatViolation violation,
            CardMaster cardMaster)
        {
            if (violation == null)
            {
                return string.Empty;
            }

            switch (violation.Rule)
            {
                case CustomFormatRule.DeckSize:
                    return $"卡组最多只能放入 {violation.Limit} 张卡牌。";
                case CustomFormatRule.SameCard:
                    return
                        $"同名卡牌「{GetCardName(cardMaster, violation.CardId)}」" +
                        $"最多只能放入 {violation.Limit} 张。";
                case CustomFormatRule.TokenCardTotal:
                    return $"Token 卡牌总数最多只能为 {violation.Limit} 张。";
                case CustomFormatRule.TokenSameCard:
                    return
                        $"同名 Token 卡牌「{GetCardName(cardMaster, violation.CardId)}」" +
                        $"最多只能放入 {violation.Limit} 张。";
                case CustomFormatRule.IndividualCard:
                    return
                        $"卡牌「{GetCardName(cardMaster, violation.CardId)}」" +
                        $"最多只能放入 {violation.Limit} 张。";
                default:
                    return "暂时无法读取卡牌数据，请稍后重试。";
            }
        }

        private static string GetCardName(CardMaster cardMaster, int cardId)
        {
            CardParameter parameter = cardMaster?.GetCardParameterFromId(cardId);
            return string.IsNullOrEmpty(parameter?.CardName)
                ? cardId.ToString()
                : parameter.CardName;
        }
    }

    internal static class CustomFormatContext
    {
        private static string deckEditFormatId = CustomFormats.UnlimitedId;
        private static string roomFormatId = CustomFormats.UnlimitedId;

        internal static string DeckEditFormatId
        {
            get => deckEditFormatId;
            set => deckEditFormatId = CustomFormats.Get(value).Id;
        }

        internal static string RoomFormatId
        {
            get => roomFormatId;
            set => roomFormatId = CustomFormats.Get(value).Id;
        }

        internal static CustomFormatDefinition DeckEditFormat =>
            CustomFormats.Get(DeckEditFormatId);

        internal static CustomFormatDefinition RoomFormat =>
            CustomFormats.Get(RoomFormatId);
    }
}
