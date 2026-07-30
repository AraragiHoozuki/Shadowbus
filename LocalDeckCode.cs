using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Shadowbus
{
    internal sealed class LocalDeckCodePayload
    {
        [JsonProperty("v")]
        public int Version { get; set; } = LocalDeckCode.CurrentVersion;

        [JsonProperty("c")]
        public int ClanId { get; set; }

        [JsonProperty("sc", NullValueHandling = NullValueHandling.Ignore)]
        public int? SubClanId { get; set; }

        [JsonProperty("f", NullValueHandling = NullValueHandling.Ignore)]
        public string FormatId { get; set; }

        [JsonProperty("n", NullValueHandling = NullValueHandling.Ignore)]
        public string DeckName { get; set; }

        [JsonProperty("sl", NullValueHandling = NullValueHandling.Ignore)]
        public long? SleeveId { get; set; }

        [JsonProperty("sk", NullValueHandling = NullValueHandling.Ignore)]
        public int? SkinId { get; set; }

        [JsonProperty("r", NullValueHandling = NullValueHandling.Ignore)]
        public string MyRotationId { get; set; }

        [JsonProperty("d")]
        public List<int> CardIds { get; set; } = new List<int>();
    }

    internal static class LocalDeckCode
    {
        internal const int CurrentVersion = 1;
        internal const int MaximumLength = 8192;
        private const int MaximumCards = 1000;
        private const int MaximumPayloadBytes = 65536;
        private const int ChecksumBytes = 10;
        private const string Prefix = "SVL1";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly JsonSerializerSettings JsonSettings =
            new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore
            };

        internal static string Encode(LocalDeckCodePayload payload)
        {
            string validationError = Validate(payload);
            if (validationError != null)
            {
                throw new InvalidDataException(validationError);
            }

            payload.Version = CurrentVersion;
            byte[] json = Utf8.GetBytes(JsonConvert.SerializeObject(payload, JsonSettings));
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(
                    output,
                    CompressionLevel.Optimal,
                    true))
                {
                    deflate.Write(json, 0, json.Length);
                }
                compressed = output.ToArray();
            }

            string code = Prefix + "." + ToBase64Url(compressed) + "." +
                ToBase64Url(ComputeChecksum(compressed));
            if (code.Length > MaximumLength)
            {
                throw new InvalidDataException(
                    $"The encoded deck exceeds {MaximumLength} characters.");
            }
            return code;
        }

        internal static bool TryDecode(
            string code,
            out LocalDeckCodePayload payload,
            out string error)
        {
            payload = null;
            error = null;
            try
            {
                string normalized = (code ?? string.Empty).Trim();
                if (normalized.Length == 0 || normalized.Length > MaximumLength)
                {
                    throw new InvalidDataException("The deck code length is invalid.");
                }

                string[] parts = normalized.Split('.');
                if (parts.Length != 3 ||
                    !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "This is not a Shadowbus local deck code.");
                }

                byte[] compressed = FromBase64Url(parts[1]);
                byte[] suppliedChecksum = FromBase64Url(parts[2]);
                byte[] expectedChecksum = ComputeChecksum(compressed);
                if (!EqualBytes(suppliedChecksum, expectedChecksum))
                {
                    throw new InvalidDataException(
                        "The deck code is damaged or incomplete.");
                }

                byte[] json;
                using (var input = new MemoryStream(compressed, false))
                using (var deflate = new DeflateStream(
                    input,
                    CompressionMode.Decompress,
                    false))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[4096];
                    int count;
                    while ((count = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + count > MaximumPayloadBytes)
                        {
                            throw new InvalidDataException(
                                "The decoded deck payload is too large.");
                        }
                        output.Write(buffer, 0, count);
                    }
                    json = output.ToArray();
                }

                payload = JsonConvert.DeserializeObject<LocalDeckCodePayload>(
                    Utf8.GetString(json),
                    JsonSettings);
                string validationError = Validate(payload);
                if (validationError != null)
                {
                    throw new InvalidDataException(validationError);
                }
                return true;
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is FormatException ||
                ex is InvalidDataException ||
                ex is IOException ||
                ex is JsonException)
            {
                payload = null;
                error = ex.Message;
                return false;
            }
        }

        private static string Validate(LocalDeckCodePayload payload)
        {
            if (payload == null)
            {
                return "The deck payload is empty.";
            }
            if (payload.Version != CurrentVersion)
            {
                return $"Deck code version {payload.Version} is not supported.";
            }
            if (payload.ClanId <= 0 || payload.ClanId > 20)
            {
                return "The deck class is invalid.";
            }
            if (payload.SubClanId.HasValue &&
                (payload.SubClanId.Value <= 0 || payload.SubClanId.Value > 20))
            {
                return "The deck subclass is invalid.";
            }
            if (payload.CardIds == null || payload.CardIds.Count == 0 ||
                payload.CardIds.Count > MaximumCards ||
                payload.CardIds.Any(cardId => cardId <= 0))
            {
                return "The deck card list is invalid.";
            }
            if ((payload.FormatId?.Length ?? 0) > 64 ||
                (payload.DeckName?.Length ?? 0) > 128 ||
                (payload.MyRotationId?.Length ?? 0) > 128)
            {
                return "The deck metadata is too long.";
            }
            if (payload.SleeveId.HasValue && payload.SleeveId.Value <= 0)
            {
                return "The deck sleeve is invalid.";
            }
            if (payload.SkinId.HasValue && payload.SkinId.Value < 0)
            {
                return "The deck leader skin is invalid.";
            }
            return null;
        }

        private static byte[] ComputeChecksum(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(data).Take(ChecksumBytes).ToArray();
            }
        }

        private static bool EqualBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }
            return difference == 0;
        }

        private static string ToBase64Url(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] FromBase64Url(string value)
        {
            string base64 = (value ?? string.Empty)
                .Replace('-', '+')
                .Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 0:
                    break;
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
                default:
                    throw new FormatException("The deck code encoding is invalid.");
            }
            return Convert.FromBase64String(base64);
        }
    }
}
