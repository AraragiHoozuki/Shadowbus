using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Shadowbus
{
    internal static class P2PIdentity
    {
        private sealed class IdentityFile
        {
            [JsonProperty("viewer_id")]
            public int ViewerId { get; set; }
        }

        private static int viewerId;

        internal static int ViewerId
        {
            get
            {
                if (viewerId == 0)
                {
                    viewerId = LoadOrCreate();
                }
                return viewerId;
            }
        }

        private static int LoadOrCreate()
        {
            try
            {
                if (File.Exists(PathHelper.P2PIdentityPath))
                {
                    IdentityFile existing = JsonConvert.DeserializeObject<IdentityFile>(
                        File.ReadAllText(PathHelper.P2PIdentityPath, Encoding.UTF8));
                    if (existing != null && existing.ViewerId > 0)
                    {
                        return existing.ViewerId;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[P2P] Ignored invalid identity file: " + ex.Message);
            }

            byte[] bytes = new byte[4];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            int generated = 100000000 + (int)((uint)BitConverter.ToInt32(bytes, 0) % 800000000U);
            try
            {
                string json = JsonConvert.SerializeObject(
                    new IdentityFile { ViewerId = generated }, Formatting.Indented);
                File.WriteAllText(PathHelper.P2PIdentityPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[P2P] Could not persist local identity: " + ex.Message);
            }
            return generated;
        }
    }
}
