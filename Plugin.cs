using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using Wizard;

namespace Shadowbus;

[BepInPlugin("08c8e386-a794-442f-a98c-aec65a183898", "GeorgesZebit.Shadowbus", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger;
    public static Plugin Instance { get; private set; }

    private Rect _windowRect = new Rect(20, 20, 250, 100);
    private int shrinkedHeight = 64;
    private int fullHeight = 600;
    private bool isShrinked = false;
    private bool customSelfDeck = false;
    private bool customOpponentDeck = false;
    private int selectedIndexSelf = 0;
    private int selectedIndexOpponent = 0;
    private bool customSelfDeckSelectorShow = false;
    private bool customOpponentDeckSelectorShow = false;
    private string[] decks = ["a", "b", "c"];
    public BattleCardBase SelectedCard { get; set; }
    public bool UseCustomDeckSelf => customSelfDeck;
    public bool UseCustomDeckOpponent => customOpponentDeck;
    public bool CustomDeckSave { get; set; } = false;
    public string CustomDeckName { get; set; } = "MyDeck.svd";

    public string CustomDeckSelf => decks[selectedIndexSelf];
    public string CustomDeckOpponent => decks[selectedIndexOpponent];

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
            Harmony.CreateAndPatchAll(typeof(CustomDeck));
            Harmony.CreateAndPatchAll(typeof(CardMasterPatcher));

        }
        catch
        {
            Logger.LogError("Harmony - FAILED to Apply Patch(s)!");
        }
        decks = CustomDeck.GetDeckNames().ToArray();
    }

    void OnGUI()
    {
        if (isShrinked)
        {
            _windowRect.height = shrinkedHeight;
            
        } else
        {
            _windowRect.height = fullHeight;
        }
        _windowRect = GUI.Window(0, _windowRect, DrawWindowContent, "Mod Menu");
    }
    void DrawWindowContent(int windowID)
    {
        GUILayout.BeginVertical();

        isShrinked = GUILayout.Toggle(isShrinked, "缩小窗口");
        GUILayout.Space(10);

        if (GUILayout.Button("重载Mods"))
        {
            CardMasterPatcher.ApplyCardMasterPatches();
            decks = CustomDeck.GetDeckNames().ToArray();
        }
        if (GUILayout.Button("还原自定义卡牌"))
        {
            CardMasterPatcher.RevokeCardMasterPatches();
        }

        GUILayout.Space(10);
        GUILayout.Label("自定义卡组:");
        customSelfDeck = GUILayout.Toggle(customSelfDeck, "启用我方自定义卡组");
        if (GUILayout.Button(decks[selectedIndexSelf]))
        {
            customSelfDeckSelectorShow = !customSelfDeckSelectorShow;
        }
        if (customSelfDeckSelectorShow)
        {
            int newSelectedIndex = GUILayout.SelectionGrid(selectedIndexSelf, decks, 2);
            if (newSelectedIndex != selectedIndexSelf)
            {
                selectedIndexSelf = newSelectedIndex;
                customSelfDeckSelectorShow = false; 
            }
        }
        customOpponentDeck = GUILayout.Toggle(customOpponentDeck, "启用对方自定义卡组");
        if (GUILayout.Button(decks[selectedIndexOpponent]))
        {
            customOpponentDeckSelectorShow = !customSelfDeckSelectorShow;
        }
        if (customOpponentDeckSelectorShow)
        {
            int newSelectedIndex = GUILayout.SelectionGrid(selectedIndexOpponent, decks, 2);
            if (newSelectedIndex != selectedIndexOpponent)
            {
                selectedIndexOpponent = newSelectedIndex;
                customOpponentDeckSelectorShow = false;
            }
        }
        GUILayout.Space(10);
        CustomDeckSave = GUILayout.Toggle(CustomDeckSave, "启用无限制卡组编辑");
        GUILayout.Label("启用后，编辑卡组时可以选择所有卡牌");
        GUILayout.Label("且点击保存时保存在本地，不上传服务器");
        CustomDeckName = GUILayout.TextField(CustomDeckName);

        GUILayout.EndVertical();
        GUI.DragWindow();
    }
}
