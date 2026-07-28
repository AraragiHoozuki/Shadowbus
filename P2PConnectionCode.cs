using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Shadowbus
{
    internal sealed class P2PConnectionInfo
    {
        public IPAddress Address { get; set; }
        public int Port { get; set; }
        public byte[] Token { get; set; }
    }

    internal static class P2PConnectionCode
    {
        internal const string Prefix = "SVP1-";
        internal const int MaximumLength = 128;

        private static readonly byte[] EncryptionKey = DeriveKey(
            "Shadowbus.P2P.ConnectionCode.Encryption.v1");
        private static readonly byte[] AuthenticationKey = DeriveKey(
            "Shadowbus.P2P.ConnectionCode.Authentication.v1");

        internal static string Create(IPAddress address, int port, byte[] token)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }
            if (port < 1 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }
            if (token == null || token.Length != 16)
            {
                throw new ArgumentException("The room token must contain 16 bytes.", nameof(token));
            }

            byte[] addressBytes = address.GetAddressBytes();
            byte family;
            if (addressBytes.Length == 4)
            {
                family = 4;
            }
            else if (addressBytes.Length == 16)
            {
                family = 6;
            }
            else
            {
                throw new ArgumentException("Only IPv4 and IPv6 addresses are supported.", nameof(address));
            }

            byte[] plain;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write((byte)1);
                writer.Write(family);
                writer.Write(addressBytes);
                writer.Write((byte)(port >> 8));
                writer.Write((byte)port);
                writer.Write(token);
                writer.Flush();
                plain = stream.ToArray();
            }

            byte[] iv = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(iv);
            }

            byte[] cipher;
            using (Aes aes = Aes.Create())
            {
                aes.Key = EncryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }
            }

            byte[] authenticated = Combine(iv, cipher);
            byte[] tag;
            using (HMACSHA256 hmac = new HMACSHA256(AuthenticationKey))
            {
                tag = hmac.ComputeHash(authenticated);
            }

            byte[] result = new byte[authenticated.Length + 16];
            Buffer.BlockCopy(authenticated, 0, result, 0, authenticated.Length);
            Buffer.BlockCopy(tag, 0, result, authenticated.Length, 16);
            return Prefix + ToBase64Url(result);
        }

        internal static bool TryDecode(string code, out P2PConnectionInfo info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            string normalized = code.Trim().Replace(" ", string.Empty)
                .Replace("\r", string.Empty).Replace("\n", string.Empty);
            if (!normalized.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                string encoded = normalized.Substring(Prefix.Length);
                byte[] packed = FromBase64Url(encoded);
                if (!string.Equals(ToBase64Url(packed), encoded, StringComparison.Ordinal))
                {
                    return false;
                }
                if (packed.Length < 48)
                {
                    return false;
                }

                int authenticatedLength = packed.Length - 16;
                byte[] authenticated = new byte[authenticatedLength];
                byte[] actualTag = new byte[16];
                Buffer.BlockCopy(packed, 0, authenticated, 0, authenticatedLength);
                Buffer.BlockCopy(packed, authenticatedLength, actualTag, 0, actualTag.Length);

                byte[] expectedTag;
                using (HMACSHA256 hmac = new HMACSHA256(AuthenticationKey))
                {
                    expectedTag = hmac.ComputeHash(authenticated);
                }
                if (!FixedTimeEquals(actualTag, expectedTag, 16))
                {
                    return false;
                }

                byte[] iv = new byte[16];
                byte[] cipher = new byte[authenticatedLength - iv.Length];
                Buffer.BlockCopy(authenticated, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(authenticated, iv.Length, cipher, 0, cipher.Length);

                byte[] plain;
                using (Aes aes = Aes.Create())
                {
                    aes.Key = EncryptionKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                    }
                }

                using (MemoryStream stream = new MemoryStream(plain, false))
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    if (reader.ReadByte() != 1)
                    {
                        return false;
                    }
                    byte family = reader.ReadByte();
                    int addressLength = family == 4 ? 4 : family == 6 ? 16 : 0;
                    if (addressLength == 0)
                    {
                        return false;
                    }
                    byte[] addressBytes = reader.ReadBytes(addressLength);
                    if (addressBytes.Length != addressLength)
                    {
                        return false;
                    }
                    int port = (reader.ReadByte() << 8) | reader.ReadByte();
                    byte[] token = reader.ReadBytes(16);
                    if (port == 0 || token.Length != 16 || stream.Position != stream.Length)
                    {
                        return false;
                    }
                    info = new P2PConnectionInfo
                    {
                        Address = new IPAddress(addressBytes),
                        Port = port,
                        Token = token
                    };
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static byte[] DeriveKey(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            }
        }

        private static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right, int count)
        {
            if (left == null || right == null || left.Length < count || right.Length < count)
            {
                return false;
            }
            int difference = 0;
            for (int i = 0; i < count; i++)
            {
                difference |= left[i] ^ right[i];
            }
            return difference == 0;
        }

        private static string ToBase64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
                case 1:
                    throw new FormatException("Invalid Base64Url length.");
            }
            return Convert.FromBase64String(base64);
        }
    }
}
