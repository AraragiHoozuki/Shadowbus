using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;


namespace Shadowbus;

[BepInPlugin("08c8e386-a794-442f-a98c-aec65a183898", "GeorgesZebit.Shadowbus", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger;
    public static readonly string ModPath = System.IO.Path.Combine(Paths.GameRootPath, "Mods");
    public static readonly string UnlimitedDeckPath = System.IO.Path.Combine(ModPath, "UnlimitedDecks");
    public static readonly string AISettingsPath = System.IO.Path.Combine(ModPath, "AISettings.json");
    public static Plugin Instance { get; private set; }

    public BattleCardBase SelectedCard { get; set; }

    private void Awake()
    {
        Instance = this;
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin Shadowbus is loaded!");

        try
        {
            var harmony = new Harmony("GeorgesZebit.Shadowbus");
            Harmony.CreateAndPatchAll(typeof(DebugPatcher));
            Harmony.CreateAndPatchAll(typeof(DeckEdit));
            Harmony.CreateAndPatchAll(typeof(CardMasterPatcher));
            Harmony.CreateAndPatchAll(typeof(Offlinizer));
            Harmony.CreateAndPatchAll(typeof(FakeConnect));
            Harmony.CreateAndPatchAll(typeof(AIManager));

        }
        catch
        {
            Logger.LogError("Harmony - FAILED to Apply Patch(s)!");
        }
    }
}
