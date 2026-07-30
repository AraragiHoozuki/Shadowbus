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
        }

        internal static void Reload()
        {
            Definitions.Clear();
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
                    Definitions[definition.Id] = definition;
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        $"[CustomFormats] Failed to load format file {file}: {ex.Message}");
                }
            }

            EnsureBuiltInDefinition(CreateUnlimitedDefinition());
            EnsureBuiltInDefinition(CreateModernDefinition());
            Plugin.Logger.LogInfo(
                $"[CustomFormats] Loaded {Definitions.Count} format definition(s) from " +
                $"{PathHelper.FormatPath}.");
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
            List<int> cards = cardIds?.ToList() ?? new List<int>();
            if (definition.DeckSizeLimit.HasValue &&
                cards.Count > definition.DeckSizeLimit.Value)
            {
                reason = $"deck size {cards.Count} exceeds {definition.DeckSizeLimit.Value}";
                return false;
            }

            Dictionary<int, int> counts = cards
                .GroupBy(cardId => cardId)
                .ToDictionary(group => group.Key, group => group.Count());
            bool needsTokenData = definition.TokenCardTotalLimit.HasValue ||
                definition.TokenSameCardLimit.HasValue;
            CardMaster cardMaster = needsTokenData ? CardMaster.GetInstanceForBattle() : null;
            if (needsTokenData && cardMaster == null)
            {
                reason = "card master is unavailable for token validation";
                return false;
            }

            int tokenTotal = 0;
            foreach (KeyValuePair<int, int> item in counts)
            {
                CardParameter parameter = needsTokenData
                    ? cardMaster.GetCardParameterFromId(item.Key)
                    : null;
                if (needsTokenData && parameter == null)
                {
                    reason = $"card {item.Key} is missing from the card master";
                    return false;
                }

                bool isToken = parameter != null && parameter.IsTokenCard;
                if (isToken)
                {
                    tokenTotal += item.Value;
                }

                int? limit = null;
                if (definition.CardLimits != null &&
                    definition.CardLimits.TryGetValue(item.Key, out int cardLimit))
                {
                    limit = cardLimit;
                }
                else if (isToken && definition.TokenSameCardLimit.HasValue)
                {
                    limit = definition.TokenSameCardLimit;
                }
                else
                {
                    limit = definition.SameCardLimit;
                }

                if (limit.HasValue && item.Value > limit.Value)
                {
                    reason = $"card {item.Key} count {item.Value} exceeds {limit.Value}";
                    return false;
                }
            }

            if (definition.TokenCardTotalLimit.HasValue &&
                tokenTotal > definition.TokenCardTotalLimit.Value)
            {
                reason = $"token card count {tokenTotal} exceeds " +
                    definition.TokenCardTotalLimit.Value;
                return false;
            }

            reason = null;
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

        private static void EnsureBuiltInDefinition(CustomFormatDefinition fallback)
        {
            if (!Definitions.ContainsKey(fallback.Id))
            {
                Definitions[fallback.Id] = fallback;
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
