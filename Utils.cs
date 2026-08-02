using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Shadowbus
{
    public static class Utils
    {
        private static readonly Dictionary<string, Texture2D> _texCache =
            new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

        public static Texture2D GetExternalTexture(int id)
        {
            return GetExternalTexture(id, false);
        }

        public static Texture2D GetExternalTexture(int id, bool isEvolution)
        {
            string path = ResolveExternalTexturePath(id, isEvolution);
            if (path == null)
            {
                return null;
            }

            if (_texCache.TryGetValue(path, out Texture2D cachedTexture))
            {
                return cachedTexture;
            }

            byte[] data = File.ReadAllBytes(path);
            Plugin.Logger.LogInfo($"Custom png at {path} loaded");
            Texture2D texture = new Texture2D(2, 2);
            if (!texture.LoadImage(data))
            {
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            _texCache[path] = texture;
            return texture;
        }

        public static bool HasExternalTexture(int id, bool isEvolution)
        {
            return ResolveExternalTexturePath(id, isEvolution) != null;
        }

        private static string ResolveExternalTexturePath(int id, bool isEvolution)
        {
            string normalPath = Path.Combine(PathHelper.CardImagePath, $"{id}.png");
            if (isEvolution)
            {
                string evolutionPath = Path.Combine(PathHelper.CardImagePath, $"{id}_evo.png");
                if (File.Exists(evolutionPath))
                {
                    return evolutionPath;
                }
            }

            return File.Exists(normalPath) ? normalPath : null;
        }

        public static void PrintAllComponents(MonoBehaviour mb)
        {
            Plugin.Logger.LogInfo($"=== 开始分析 {mb.name} 上的组件 ===");
            Component[] selfComponents = mb.GetComponents<Component>();
            foreach (var comp in selfComponents)
            {
                if (comp != null)
                {
                    Plugin.Logger.LogInfo($"自身组件: {comp.GetType().FullName}");
                }
            }
            Plugin.Logger.LogInfo("---");

            Component[] childComponents = mb.GetComponentsInChildren<Component>(true);
            foreach (var comp in childComponents)
            {
                if (comp != null && comp.gameObject != mb.gameObject)
                {
                    Plugin.Logger.LogInfo($"子物体 [{comp.gameObject.name}] 上的组件: {comp.GetType().FullName}");
                }
            }

            Plugin.Logger.LogInfo("=== 分析结束 ===");
        }

        public static void ChangeChildUILabelText(GameObject obj, string name,string text, bool withStaticText = true)
        {
            var child = obj.transform.Find(name);
            if (child != null)
            {
                var label = child.GetComponent<UILabel>();
                if (label != null)
                {
                    if (withStaticText)
                    {
                        var staticText = child.GetComponent<StaticTextForUILabel>();
                        if (staticText != null)
                        {
                            staticText.enabled = false;
                        }
                    }
                    label.text = text;
                }
            }
        }
    }
}
