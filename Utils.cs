using System.Collections.Generic;
using System.IO;
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
