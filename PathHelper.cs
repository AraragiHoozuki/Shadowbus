using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shadowbus
{
    public static class PathHelper
    {
        public static readonly string ModPath = Path.Combine(Paths.GameRootPath, "Mods");
        public static readonly string UnlimitedDeckPath = Path.Combine(ModPath, "UnlimitedDecks");
        public static readonly string CardMasterPath = Path.Combine(ModPath, "CardMaster");
        public static readonly string AIDataPath = Path.Combine(ModPath, "AIData");
        public static readonly string AIDeckPath = Path.Combine(AIDataPath, "deck");
        public static readonly string AIStylePath = Path.Combine(AIDataPath, "style");
        public static readonly string AIEmotePath = Path.Combine(AIDataPath, "emote");

        static PathHelper()
        {
            Directory.CreateDirectory(ModPath);
            Directory.CreateDirectory(UnlimitedDeckPath);
            Directory.CreateDirectory(CardMasterPath);
            Directory.CreateDirectory(AIDataPath);
            Directory.CreateDirectory(AIDeckPath);
            Directory.CreateDirectory(AIStylePath);
            Directory.CreateDirectory(AIEmotePath);
        }
    }
}
