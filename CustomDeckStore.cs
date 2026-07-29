using LitJson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shadowbus
{
    internal static class CustomDeckStore
    {
        internal static JsonData LoadDeckList(CustomFormatDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            Directory.CreateDirectory(definition.DeckDirectory);
            var loadedDecks = new List<JsonData>();
            var existingDeckNos = new HashSet<int>();
            bool hasEmptyDeck = false;

            foreach (string file in Directory.GetFiles(
                definition.DeckDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly))
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
                    loadedDecks.Add(deck);
                    existingDeckNos.Add(deck["deck_no"].ToInt());
                    JsonData cards = deck["card_id_array"];
                    hasEmptyDeck |= cards.IsArray && cards.Count == 0;
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        $"[CustomFormats] Failed to load {definition.Id} deck from {file}: {ex.Message}");
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
                string file = Path.Combine(definition.DeckDirectory, $"deck_{deckNo}.json");
                File.WriteAllText(file, json);
                loadedDecks.Add(JsonMapper.ToObject(json));
            }

            JsonData deckList = new JsonData();
            foreach (JsonData deck in loadedDecks
                .OrderBy(item => item["card_id_array"].Count == 0 ? 1 : 0)
                .ThenBy(item => GetOrderNumber(item))
                .ThenBy(item => item["deck_no"].ToInt()))
            {
                deckList.Add(deck);
            }
            return deckList;
        }

        internal static IEnumerable<string> EnumerateDeckFiles(CustomFormatDefinition definition)
        {
            Directory.CreateDirectory(definition.DeckDirectory);
            return Directory.GetFiles(definition.DeckDirectory, "*.json", SearchOption.TopDirectoryOnly);
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
