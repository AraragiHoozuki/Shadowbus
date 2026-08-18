using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Shadowbus.LLMAI;
using System.Linq;


namespace Shadowbus;

[BepInPlugin("08c8e386-a794-442f-a98c-aec65a183898", "GeorgesZebit.Shadowbus", "2.4.0")]
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
    private ConfigEntry<float> aiStallTimeout;
    private ConfigEntry<bool> llmAIEnabled;
    private ConfigEntry<string> llmAIEndpoint;
    private ConfigEntry<string> llmAIResponsesEndpoint;
    private ConfigEntry<string> llmAIChatCompletionsEndpoint;
    private ConfigEntry<string> llmAIApiMode;
    private ConfigEntry<string> llmAIApiKey;
    private ConfigEntry<string> llmAIModel;
    private ConfigEntry<string> llmAIReasoningEffort;
    private ConfigEntry<float> llmAITimeout;
    private ConfigEntry<int> llmAIMaxCandidates;
    private ConfigEntry<int> llmAIMaxPlanSteps;
    private ConfigEntry<int> llmAIMaxApiCallsPerTurn;
    private ConfigEntry<int> llmAIMaxResponseTokens;
    private ConfigEntry<int> llmAIMaxOutputTokens;
    private ConfigEntry<int> llmAILethalSearchMaxPatterns;
    private ConfigEntry<int> llmAILethalSearchBudgetMs;
    private ConfigEntry<string> llmAIPromptFile;
    private ConfigEntry<bool> llmAIDebugLogPayloads;
    private ConfigEntry<float> aiUnknownCardPlayBonusMin;
    private ConfigEntry<float> aiUnknownCardPlayBonusMax;
    private ConfigEntry<bool> aiPriceUnpricedCards;
    private ConfigEntry<bool> aiRespectPlayLimitLocks;
    private ConfigEntry<int> aiLowLifeHealThreshold;
    private ConfigEntry<bool> bossRushAbilityPicker;

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
        aiStallTimeout = Config.Bind(
            "AI",
            "StallTimeoutSeconds",
            30f,
            "Seconds the enemy AI may make no progress before its turn is force ended. Use 0 to disable.");
        AITurnGuard.Configure(aiStallTimeout.Value);
        llmAIEnabled = Config.Bind("LLMAI", "Enabled", false,
            "Default state of the in-game LLM AI switch for new custom practice battles.");
        llmAIEndpoint = Config.Bind("LLMAI", "Endpoint", "https://api.openai.com/v1/chat/completions",
            "Backward-compatible API endpoint or /v1 base. Auto derives Responses and Chat Completions URLs.");
        llmAIResponsesEndpoint = Config.Bind("LLMAI", "ResponsesEndpoint", string.Empty,
            "Optional Responses API endpoint override.");
        llmAIChatCompletionsEndpoint = Config.Bind("LLMAI", "ChatCompletionsEndpoint", string.Empty,
            "Optional Chat Completions endpoint override.");
        llmAIApiMode = Config.Bind("LLMAI", "ApiMode", "Auto",
            "API mode: Auto, Responses, or ChatCompletions. Auto prefers Responses.");
        llmAIApiKey = Config.Bind("LLMAI", "ApiKey", string.Empty,
            "Bearer API key. This value is never written to the log or model payload.");
        llmAIModel = Config.Bind("LLMAI", "Model", string.Empty,
            "Model name.");
        llmAIReasoningEffort = Config.Bind("LLMAI", "ReasoningEffort", "high",
            "Reasoning effort: none, minimal, low, medium, high, or xhigh. Empty omits the field.");
        llmAITimeout = Config.Bind("LLMAI", "TimeoutSeconds", 12f,
            "Timeout for one model request.");
        llmAIMaxCandidates = Config.Bind("LLMAI", "MaxCandidates", 512,
            "Maximum legal actions at one simulated node before falling back to the original AI.");
        llmAIMaxPlanSteps = Config.Bind("LLMAI", "MaxPlanSteps", 12,
            "Maximum number of actions accepted in one TurnPlan.");
        llmAIMaxApiCallsPerTurn = Config.Bind("LLMAI", "MaxApiCallsPerTurn", 12,
            "Maximum model calls during one turn, including replans.");
        llmAIMaxResponseTokens = Config.Bind("LLMAI", "MaxResponseTokens", 768,
            "Legacy Chat Completions maximum response tokens.");
        llmAIMaxOutputTokens = Config.Bind("LLMAI", "MaxOutputTokens", 4096,
            "Responses API output budget shared by reasoning and final JSON.");
        llmAILethalSearchMaxPatterns = Config.Bind("LLMAI", "LethalSearchMaxPatterns", 32,
            "Maximum original-AI play patterns checked by local lethal search per decision state.");
        llmAILethalSearchBudgetMs = Config.Bind("LLMAI", "LethalSearchBudgetMs", 1000,
            "Total local lethal-search budget in milliseconds per decision state.");
        llmAIPromptFile = Config.Bind("LLMAI", "PromptFile", "Mods/AIData/llm_prompt.txt",
            "Optional prompt path relative to the game root. A built-in prompt is used when absent.");
        llmAIDebugLogPayloads = Config.Bind("LLMAI", "DebugLogPayloads", false,
            "Log public model payloads and responses. Authorization is never logged.");
        if (!LLMEndpointResolver.TryParseMode(llmAIApiMode.Value, out LLMApiMode apiMode))
        {
            Logger.LogWarning($"[LLMAI] Invalid ApiMode '{llmAIApiMode.Value}'; using Auto.");
            apiMode = LLMApiMode.Auto;
        }
        LLMAITurnController.Configure(new LLMAISettings
        {
            Enabled = llmAIEnabled.Value,
            Endpoint = llmAIEndpoint.Value,
            ResponsesEndpoint = llmAIResponsesEndpoint.Value,
            ChatCompletionsEndpoint = llmAIChatCompletionsEndpoint.Value,
            ApiMode = apiMode,
            ApiKey = llmAIApiKey.Value,
            Model = llmAIModel.Value,
            ReasoningEffort = llmAIReasoningEffort.Value,
            TimeoutSeconds = System.Math.Max(1f, llmAITimeout.Value),
            MaxCandidates = System.Math.Max(1, llmAIMaxCandidates.Value),
            MaxPlanSteps = System.Math.Max(1, llmAIMaxPlanSteps.Value),
            MaxApiCallsPerTurn = System.Math.Max(1, llmAIMaxApiCallsPerTurn.Value),
            MaxResponseTokens = System.Math.Max(64, llmAIMaxResponseTokens.Value),
            MaxOutputTokens = System.Math.Max(64, llmAIMaxOutputTokens.Value),
            LethalSearchMaxPatterns = System.Math.Max(0, llmAILethalSearchMaxPatterns.Value),
            LethalSearchBudgetMs = System.Math.Max(0, llmAILethalSearchBudgetMs.Value),
            PromptFile = llmAIPromptFile.Value,
            DebugLogPayloads = llmAIDebugLogPayloads.Value
        });
        aiUnknownCardPlayBonusMin = Config.Bind(
            "AI",
            "UnknownCardPlayBonusMin",
            0.5f,
            "Lowest play bonus given to a card that has no AI data. Set both bounds to 0 to keep the crash fix but stop the AI from playing such cards.");
        aiUnknownCardPlayBonusMax = Config.Bind(
            "AI",
            "UnknownCardPlayBonusMax",
            1.5f,
            "Highest play bonus given to a card that has no AI data. The original data keeps most numeric play bonuses between 0 and 2.");
        aiPriceUnpricedCards = Config.Bind(
            "AI",
            "PriceUnpricedCards",
            true,
            "Score spells and amulets whose AI tags describe an effect but never give it a value. Without this the AI leaves them in hand for the whole game.");
        aiRespectPlayLimitLocks = Config.Bind(
            "AI",
            "RespectPlayLimitLocks",
            false,
            "Leave cards the original data locked with a playLimit tag unpriced. Only 4 cards are both locked and unpriced, and all sit far below their threshold, so this changes nothing today; it guards against a future card whose threshold a synthesized bonus could cross.");
        aiLowLifeHealThreshold = Config.Bind(
            "AI",
            "LowLifeHealThreshold",
            10,
            "Leader healing only scores when the AI is at this much life or less. Use 0 to score it like any other unpriced effect.");
        AICardDataFallback.Configure(
            aiUnknownCardPlayBonusMin.Value,
            aiUnknownCardPlayBonusMax.Value,
            aiPriceUnpricedCards.Value,
            aiRespectPlayLimitLocks.Value,
            aiLowLifeHealThreshold.Value);
        bossRushAbilityPicker = Config.Bind(
            "BossRush",
            "AbilityPicker",
            true,
            "Shows a 随便选 button on the BossRush ability select screen that offers every configured buff instead of the three random candidates. Set to false to hide the button and keep the original random selection.");
        BossRushAbilityPicker.Configure(bossRushAbilityPicker.Value);
        CustomFormats.Initialize();
        P2PTwoPickRules.Initialize();
        BossRushOfflineData.Initialize();
        BossRushReferenceExporter.Export();

        try
        {
            var harmony = new Harmony("GeorgesZebit.Shadowbus");
            Harmony.CreateAndPatchAll(typeof(DebugPatcher));
            Harmony.CreateAndPatchAll(typeof(DeckEdit));
            Harmony.CreateAndPatchAll(typeof(CardMasterPatcher));
            try
            {
                // Isolated: a reference dump must never block the card master.
                Harmony.CreateAndPatchAll(typeof(CardSkillExporter));
            }
            catch (System.Exception exception)
            {
                Logger.LogError($"[CardSkill] FAILED to apply the card skill export patch: {exception}");
            }
            Harmony.CreateAndPatchAll(typeof(Offlinizer));
            var deckListHotReloadHarmony = Harmony.CreateAndPatchAll(typeof(DeckListHotReload));
            Logger.LogInfo(
                $"[DeckListHotReload] Harmony registration complete: " +
                $"{deckListHotReloadHarmony.GetPatchedMethods().Count()} game method(s) patched.");
            Harmony.CreateAndPatchAll(typeof(FakeConnect));
            Harmony.CreateAndPatchAll(typeof(BossRushPatches));
            try
            {
                // Isolated: an optional testing aid must not take down the rest of
                // the BossRush patches if the ability select screen changes.
                Harmony.CreateAndPatchAll(typeof(BossRushAbilityPicker));
            }
            catch (System.Exception exception)
            {
                Logger.LogError($"[BossRush] FAILED to apply the ability picker patch: {exception}");
            }
            Harmony.CreateAndPatchAll(typeof(AIManager));
            Harmony.CreateAndPatchAll(typeof(LLMAIPatches));
            try
            {
                Harmony.CreateAndPatchAll(typeof(PracticeDualAI));
            }
            catch (System.Exception exception)
            {
                Logger.LogError($"[AIManager] FAILED to apply the dual-practice AI patches: {exception}");
            }
            Harmony.CreateAndPatchAll(typeof(BossRushReferenceExporter));
            try
            {
                // Isolated: these patches bind to private and virtual game methods, and a
                // binding failure must not take down the patches that follow.
                Harmony.CreateAndPatchAll(typeof(AITurnGuard));
            }
            catch (System.Exception exception)
            {
                Logger.LogError($"[AITurnGuard] FAILED to apply the AI stall patches: {exception}");
            }
            try
            {
                Harmony.CreateAndPatchAll(typeof(AICardDataFallback));
            }
            catch (System.Exception exception)
            {
                Logger.LogError($"[AICardData] FAILED to apply the AI card data fallback patch: {exception}");
            }
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
            Harmony.CreateAndPatchAll(typeof(P2PTwoPickClassDescriptionPatch));
            Harmony.CreateAndPatchAll(typeof(P2PTwoPickClassIconPatch));
            Harmony.CreateAndPatchAll(typeof(P2PTwoPickDeckSizePatches));
            Harmony.CreateAndPatchAll(typeof(P2PTwoPickCompletionPatch));

        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Harmony - FAILED to Apply Patch(s): {exception}");
        }
    }

    private void Update()
    {
        P2PRuntime.Update();
        PracticeDualAI.Update();
        AITurnGuard.Update();
        AICardDataFallback.Update();
    }

    private void OnDestroy()
    {
        LLMAITurnController.CancelAll("plugin_destroyed");
        P2PRuntime.Shutdown();
    }
}
