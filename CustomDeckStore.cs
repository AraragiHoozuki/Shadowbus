using LitJson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shadowbus
{
    internal static class CustomDeckStore
    {
        internal static void MigrateLegacyModernDecks()
        {
            string sourceDirectory = Path.Combine(
                PathHelper.LegacyCustomFormatPath,
                CustomFormats.ModernId,
                "Decks");
            if (!Directory.Exists(sourceDirectory))
            {
                return;
            }

            Directory.CreateDirectory(PathHelper.UnlimitedDeckPath);
            var usedDeckNos = new HashSet<int>();
            foreach (string currentFile in EnumerateDeckFiles())
            {
                try
                {
                    JsonData current = JsonMapper.ToObject(File.ReadAllText(currentFile));
                    if (current.IsObject && current.Keys.Contains("deck_no"))
                    {
                        usedDeckNos.Add(current["deck_no"].ToInt());
                    }
                }
                catch
                {
                    // The normal loader reports malformed active deck files.
                }
            }

            foreach (string sourceFile in Directory.GetFiles(
                sourceDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly))
            {
                string targetFile = Path.Combine(
                    PathHelper.UnlimitedDeckPath,
                    "legacy_modern_" + Path.GetFileName(sourceFile));
                if (File.Exists(targetFile))
                {
                    continue;
                }

                try
                {
                    JsonData deck = JsonMapper.ToObject(File.ReadAllText(sourceFile));
                    if (!deck.IsObject || !deck.Keys.Contains("deck_no") ||
                        !deck.Keys.Contains("card_id_array") ||
                        !deck["card_id_array"].IsArray ||
                        deck["card_id_array"].Count == 0)
                    {
                        continue;
                    }

                    int deckNo = deck["deck_no"].ToInt();
                    while (usedDeckNos.Contains(deckNo))
                    {
                        deckNo++;
                    }
                    deck["deck_no"] = deckNo;
                    deck["format_id"] = CustomFormats.ModernId;
                    File.WriteAllText(targetFile, deck.ToJson());
                    usedDeckNos.Add(deckNo);
                    Plugin.Logger.LogInfo(
                        $"[CustomFormats] Imported legacy Modern deck {sourceFile} " +
                        $"as deck {deckNo} in the shared Unlimited deck folder.");
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        $"[CustomFormats] Failed to import legacy Modern deck " +
                        $"{sourceFile}: {ex.Message}");
                }
            }
        }

        internal static JsonData LoadDeckList()
        {
            Directory.CreateDirectory(PathHelper.UnlimitedDeckPath);
            var loadedDecks = new List<JsonData>();
            var existingDeckNos = new HashSet<int>();
            bool hasEmptyDeck = false;

            foreach (string file in EnumerateDeckFiles())
            {
                try
                {
                    JsonData deck = JsonMapper.ToObject(File.ReadAllText(file));
                    if (!deck.IsObject || !deck.Keys.Contains("deck_no") ||
                        !deck.Keys.Contains("card_id_array") ||
                        !deck["card_id_array"].IsArray)
                    {
                        throw new InvalidDataException("Required deck fields are missing.");
                    }
                    EnsureFormatId(deck);
                    loadedDecks.Add(deck);
                    existingDeckNos.Add(deck["deck_no"].ToInt());
                    hasEmptyDeck |= deck["card_id_array"].Count == 0;
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        $"[CustomFormats] Failed to load deck from {file}: {ex.Message}");
                }
            }

            if (!hasEmptyDeck)
            {
                int deckNo = 1;
                while (existingDeckNos.Contains(deckNo))
                {
                    deckNo++;
                }

                string json = CreateEmptyDeckJson(deckNo);
                string file = Path.Combine(PathHelper.UnlimitedDeckPath, $"deck_{deckNo}.json");
                File.WriteAllText(file, json);
                loadedDecks.Add(JsonMapper.ToObject(json));
            }

            return ToDeckList(loadedDecks);
        }

        internal static JsonData LoadCompliantDeckList(CustomFormatDefinition definition)
        {
            JsonData filtered = new JsonData();
            int rejected = 0;
            foreach (JsonData deck in LoadDeckList().Cast<JsonData>())
            {
                JsonData cards = deck["card_id_array"];
                if (cards.Count == 0)
                {
                    continue;
                }
                if (CustomFormats.IsDeckCompliant(deck, definition, out string reason))
                {
                    filtered.Add(deck);
                }
                else
                {
                    rejected++;
                    Plugin.Logger.LogInfo(
                        $"[CustomFormats] Excluded deck {deck["deck_no"].ToInt()} from " +
                        $"{definition.Id}: {reason}.");
                }
            }

            Plugin.Logger.LogInfo(
                $"[CustomFormats] Room format {definition.Id}: " +
                $"accepted={filtered.Count}, rejected={rejected}.");
            return filtered;
        }

        internal static IEnumerable<string> EnumerateDeckFiles()
        {
            Directory.CreateDirectory(PathHelper.UnlimitedDeckPath);
            return Directory.GetFiles(
                PathHelper.UnlimitedDeckPath,
                "*.json",
                SearchOption.TopDirectoryOnly);
        }

        internal static string GetDeckFormatId(int deckNo)
        {
            foreach (string file in EnumerateDeckFiles())
            {
                try
                {
                    JsonData deck = JsonMapper.ToObject(File.ReadAllText(file));
                    if (deck.IsObject && deck.Keys.Contains("deck_no") &&
                        deck["deck_no"].ToInt() == deckNo)
                    {
                        return ReadFormatId(deck);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning(
                        $"[CustomFormats] Could not inspect deck metadata in {file}: {ex.Message}");
                }
            }
            return CustomFormats.UnlimitedId;
        }

        internal static string ReadFormatId(JsonData deck)
        {
            if (deck != null && deck.IsObject && deck.Keys.Contains("format_id"))
            {
                return CustomFormats.Get(deck["format_id"].ToString()).Id;
            }
            return CustomFormats.UnlimitedId;
        }

        internal static void EnsureFormatId(JsonData deck)
        {
            deck["format_id"] = ReadFormatId(deck);
        }

        private static JsonData ToDeckList(IEnumerable<JsonData> decks)
        {
            JsonData deckList = new JsonData();
            foreach (JsonData deck in decks
                .OrderBy(item => item["card_id_array"].Count == 0 ? 1 : 0)
                .ThenBy(GetOrderNumber)
                .ThenBy(item => item["deck_no"].ToInt()))
            {
                deckList.Add(deck);
            }
            return deckList;
        }

        private static int GetOrderNumber(JsonData deck)
        {
            if (deck.Keys.Contains("order_num"))
            {
                int order = deck["order_num"].ToInt();
                if (order > 0)
                {
                    return order;
                }
            }
            return int.MaxValue;
        }

        private static string CreateEmptyDeckJson(int deckNo)
        {
            return "{\r\n" +
                $"  \"deck_no\": {deckNo},\r\n" +
                "  \"class_id\": 1,\r\n" +
                "  \"sleeve_id\": 3000011,\r\n" +
                "  \"leader_skin_id\": 0,\r\n" +
                "  \"deck_name\": \"\",\r\n" +
                "  \"format_id\": \"unlimited\",\r\n" +
                "  \"card_id_array\": [],\r\n" +
                "  \"is_complete_deck\": 0,\r\n" +
                "  \"restricted_card_exists\": false,\r\n" +
                "  \"is_available_deck\": 1,\r\n" +
                "  \"maintenance_card_ids\": [],\r\n" +
                "  \"is_include_un_possession_card\": false,\r\n" +
                "  \"is_random_leader_skin\": 0,\r\n" +
                "  \"leader_skin_id_list\": [0],\r\n" +
                "  \"order_num\": 0,\r\n" +
                "  \"create_deck_time\": null\r\n" +
                "}";
        }
    }
}
