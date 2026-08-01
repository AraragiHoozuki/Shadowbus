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
        public static readonly string FormatPath = Path.Combine(ModPath, "Format");
        public static readonly string TwoPickPath = Path.Combine(ModPath, "TwoPick");
        public static readonly string LegacyCustomFormatPath =
            Path.Combine(ModPath, "CustomFormats");
        public static readonly string CardMasterPath = Path.Combine(ModPath, "CardMaster");
        public static readonly string AIDataPath = Path.Combine(ModPath, "AIData");
        public static readonly string AIDeckPath = Path.Combine(AIDataPath, "deck");
        public static readonly string AIStylePath = Path.Combine(AIDataPath, "style");
        public static readonly string AIEmotePath = Path.Combine(AIDataPath, "emote");
        public static readonly string MyPageBackgroundSettingsPath = Path.Combine(ModPath, "MyPageBackground.json");
        public static readonly string ProfileSettingsPath = Path.Combine(ModPath, "Profile.json");
        public static readonly string P2PIdentityPath = Path.Combine(ModPath, "P2PIdentity.json");
        public static readonly string BossRushPath = Path.Combine(ModPath, "BossRush");
        public static readonly string BossRushStatePath = Path.Combine(BossRushPath, "State");
        public static readonly string BossRushReferencePath = Path.Combine(BossRushPath, "Reference");

        static PathHelper()
        {
            Directory.CreateDirectory(ModPath);
            Directory.CreateDirectory(UnlimitedDeckPath);
            Directory.CreateDirectory(FormatPath);
            Directory.CreateDirectory(TwoPickPath);
            Directory.CreateDirectory(CardMasterPath);
            Directory.CreateDirectory(AIDataPath);
            Directory.CreateDirectory(AIDeckPath);
            Directory.CreateDirectory(AIStylePath);
            Directory.CreateDirectory(AIEmotePath);
            Directory.CreateDirectory(BossRushPath);
            Directory.CreateDirectory(BossRushStatePath);
            Directory.CreateDirectory(BossRushReferencePath);
        }
    }
}
