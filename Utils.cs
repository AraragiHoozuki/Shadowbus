using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Shadowbus
{
    public static class Utils
    {
        private static Dictionary<int, Texture2D> _texCache = [];
        public static Texture2D GetExternalTexture(int id)
        {
            if (_texCache.ContainsKey(id)) return _texCache[id];
            string path = Path.Combine("Mods", "CardImages", $"{id}.png");
            if (File.Exists(path))
            {
                byte[] data = File.ReadAllBytes(path);
                Plugin.Logger.LogInfo($"Custom png at {path} loaded");
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(data))
                {
                    tex.wrapMode = TextureWrapMode.Clamp;
                    _texCache[id] = tex;
                    return tex;
                }
            }
            return null;
        }
    }
}
