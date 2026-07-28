using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Shadowbus
{
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

    internal sealed class P2PRoomRules
    {
        [JsonProperty("battleType")]
        public int BattleType { get; set; }

        [JsonProperty("deckFormat")]
        public int DeckFormat { get; set; }

        [JsonProperty("twoPickType")]
        public int TwoPickType { get; set; }

        [JsonProperty("battleRule")]
        public int BattleRule { get; set; }

        [JsonProperty("isDeckOpen")]
        public bool IsDeckOpen { get; set; }
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
            if (result.TryGetValue("targetList", out object targets))
            {
                // The client emits targetList, but live opponent messages use
                // oppoTargetList so the receiver reads action-relative isSelf values.
                result.Remove("targetList");
                result["oppoTargetList"] = targets;
            }
            return result;
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
    }
}
