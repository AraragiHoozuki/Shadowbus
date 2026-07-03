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
        public static readonly string AISettingsPath = Path.Combine(ModPath, "AISettings.json");

        static PathHelper()
        {
            Directory.CreateDirectory(ModPath);
            Directory.CreateDirectory(UnlimitedDeckPath);
            Directory.CreateDirectory(CardMasterPath);
        }
    }
}
