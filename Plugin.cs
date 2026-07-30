using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using System.Linq;


namespace Shadowbus;

[BepInPlugin("08c8e386-a794-442f-a98c-aec65a183898", "GeorgesZebit.Shadowbus", "2.0.0")]
public class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger;
    public static readonly string ModPath = System.IO.Path.Combine(Paths.GameRootPath, "Mods");
    public static readonly string UnlimitedDeckPath = System.IO.Path.Combine(ModPath, "UnlimitedDecks");
    public static readonly string CardMasterPath = System.IO.Path.Combine(ModPath, "CardMaster");
    public static Plugin Instance { get; private set; }

    public BattleCardBase SelectedCard { get; set; }

    private ConfigEntry<string> p2pBindAddress;
    private ConfigEntry<string> p2pAdvertisedAddress;
    private ConfigEntry<int> p2pPort;

    private void Awake()
    {
        Instance = this;
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin Shadowbus is loaded!");

        p2pBindAddress = Config.Bind(
            "P2P",
            "BindAddress",
            "0.0.0.0",
            "Local address used by the room host TCP listener.");
        p2pAdvertisedAddress = Config.Bind(
            "P2P",
            "AdvertisedAddress",
            string.Empty,
            "IP address embedded in the room password. Empty uses a concrete BindAddress or selects a same-family local address.");
        p2pPort = Config.Bind(
            "P2P",
            "Port",
            29600,
            "TCP port used by P2P room hosting. Use 0 for an automatically assigned port.");
        P2PRuntime.Configure(
            p2pBindAddress.Value,
            p2pAdvertisedAddress.Value,
            p2pPort.Value);
        CustomFormats.Initialize();

        try
        {
            var harmony = new Harmony("GeorgesZebit.Shadowbus");
            Harmony.CreateAndPatchAll(typeof(DebugPatcher));
            Harmony.CreateAndPatchAll(typeof(DeckEdit));
            Harmony.CreateAndPatchAll(typeof(CardMasterPatcher));
            Harmony.CreateAndPatchAll(typeof(Offlinizer));
            var deckListHotReloadHarmony = Harmony.CreateAndPatchAll(typeof(DeckListHotReload));
            Logger.LogInfo(
                $"[DeckListHotReload] Harmony registration complete: " +
                $"{deckListHotReloadHarmony.GetPatchedMethods().Count()} game method(s) patched.");
            Harmony.CreateAndPatchAll(typeof(FakeConnect));
            Harmony.CreateAndPatchAll(typeof(AIManager));
            Harmony.CreateAndPatchAll(typeof(ActiveSkill));
            Harmony.CreateAndPatchAll(typeof(GeminizeSkillPatcher));
            Harmony.CreateAndPatchAll(typeof(AcquireSkillsSkillPatcher));
            Harmony.CreateAndPatchAll(typeof(MirrorSkillPatcher));
            Harmony.CreateAndPatchAll(typeof(MirrorResidentEffectPatcher));
            Harmony.CreateAndPatchAll(typeof(StoryOfflinePatches));
            Harmony.CreateAndPatchAll(typeof(LanguageVoicePatches));
            Harmony.CreateAndPatchAll(typeof(DeckFormatUI));
            Harmony.CreateAndPatchAll(typeof(LocalDeckCodePatches));
            var deckFormatRulesHarmony =
                Harmony.CreateAndPatchAll(typeof(CustomFormatDeckEditRules));
            Logger.LogInfo(
                $"[CustomFormats] Deck edit rule registration complete: " +
                $"{deckFormatRulesHarmony.GetPatchedMethods().Count()} game method(s) patched.");
            Harmony.CreateAndPatchAll(typeof(P2PPatches));

        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Harmony - FAILED to Apply Patch(s): {exception}");
        }
    }

    private void Update()
    {
        P2PRuntime.Update();
    }

    private void OnDestroy()
    {
        P2PRuntime.Shutdown();
    }
}
