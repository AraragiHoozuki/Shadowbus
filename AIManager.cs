using Cute;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Wizard;

namespace Shadowbus
{
    internal sealed class CustomPracticeButtonLabelGuard : MonoBehaviour
    {
        private const string CustomLabelText = "自定义卡组";
        private UILabel _label;

        public void Initialize(UILabel label)
        {
            _label = label;
            ApplyLabel();
        }

        private void LateUpdate()
        {
            if (_label != null && _label.text != CustomLabelText)
            {
                ApplyLabel();
            }
        }

        private void ApplyLabel()
        {
            if (_label == null)
            {
                return;
            }

            _label.text = CustomLabelText;
            _label.overflowMethod = UILabel.Overflow.ShrinkContent;
        }
    }

    public class AIManager
    {
        private const string CustomPracticeButtonName = "ShadowbusCustomPracticeButton";

        internal sealed class CustomPracticeSettings
        {
            public DeckData Deck;
            public int EnemyClassId;
            public ClassCharacterMasterData Leader;
            public PracticeAISettingData AIPreset;
            public string LocalDeckCsvPath;
            public string LocalStyleCsvPath;
            public string LocalEmoteCsvPath;
            public int LogicLevel;
            public int MaxLife;
        }

        internal sealed class CustomPracticeDeckChoice
        {
            public DeckData Deck;
            public int EnemyClassId;
        }

        private sealed class CustomPracticeSession
        {
            public string DeckName;
            public List<int> EnemyDeck;
            public int EnemyClassId;
            public int EnemyCharaId;
            public int Difficulty;
            public int LogicLevel;
            public int MaxLife;
            public int StyleId;
            public int EmoteId;
            public bool UseInnerEmote;
            public int EnemyAIId;
            public string DeckAIKey;
            public string StyleAIKey;
            public string EmoteAIKey;
            public int DifficultyDegreeId;
            public int FieldId;
        }

        private static CustomPracticeSession ActiveCustomPracticeSession;

        [HarmonyPatch(typeof(ClassSelectionPage), "CreateClassButton")]
        [HarmonyPostfix]
        public static void ClassSelectionPage_CreateClassButton_Postfix(ClassSelectionPage __instance)
        {
            if (__instance.Mode != ClassSelectionPage.eMode.PracticeSelect ||
                __instance._classButtonGrid == null ||
                __instance._classButtonParts == null ||
                __instance._classCharacterMasterDatas == null ||
                __instance._classCharacterMasterDatas.Count == 0)
            {
                return;
            }

            Transform customButtonParent = __instance._classButtonGrid.transform.parent;
            if (customButtonParent == null || customButtonParent.Find(CustomPracticeButtonName) != null)
            {
                return;
            }

            try
            {
                ResourcesManager resourcesManager = Toolbox.ResourcesManager;
                GameObject buttonObject = NGUITools.AddChild(
                    customButtonParent.gameObject,
                    __instance._classButtonParts.gameObject);
                buttonObject.name = CustomPracticeButtonName;
                buttonObject.SetActive(true);

                Transform topRightClassButton = __instance._classSelectionButtonList
                    .Where(classButton => classButton != null)
                    .Select(classButton => classButton.transform)
                    .OrderByDescending(buttonTransform => buttonTransform.localPosition.x)
                    .ThenByDescending(buttonTransform => buttonTransform.localPosition.y)
                    .First();
                Vector3 customGridPosition = topRightClassButton.localPosition +
                    new Vector3(
                        __instance._classButtonGrid.cellWidth,
                        __instance._classButtonGrid.cellHeight,
                        0f);
                buttonObject.transform.position = __instance._classButtonGrid.transform.TransformPoint(customGridPosition);

                ClassSelectionButton button = buttonObject.GetComponent<ClassSelectionButton>();
                Texture texture = resourcesManager.LoadObject<Texture>(
                    resourcesManager.GetAssetTypePath(
                        ClassSelectionButton.CLASS_SELECT_BUTTON_EMPTY,
                        ResourcesManager.AssetLoadPathType.ClassCharaButton,
                        true),
                    true,
                    false);

                button.Init(
                    __instance._classCharacterMasterDatas[0],
                    texture,
                    delegate
                    {
                        GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_TOGGLE_ON, false);
                        ShowDeckSelection(__instance);
                    },
                    false,
                    false,
                    false);

                button._texture.color = new Color(0.72f, 0.34f, 0.88f, 1f);
                foreach (UILocalize usedLabelLocalize in button._usedLabel.GetComponents<UILocalize>())
                {
                    usedLabelLocalize.enabled = false;
                    UnityEngine.Object.Destroy(usedLabelLocalize);
                }
                button._usedLabel.gameObject.SetActive(true);
                button._button.isEnabled = true;
                buttonObject.AddComponent<CustomPracticeButtonLabelGuard>().Initialize(button._usedLabel);

                Plugin.Logger.LogInfo(
                    $"[AIManager] Added the custom practice button outside the class grid: " +
                    $"localPosition={buttonObject.transform.localPosition}.");
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError($"[AIManager] Failed to add the custom practice button.\n{exception}");
            }
        }

        private static void ShowDeckSelection(ClassSelectionPage page)
        {
            try
            {
                List<CustomPracticeDeckChoice> decks = GetUnlimitedDeckChoices();

                if (decks == null || decks.Count == 0)
                {
                    ShowMessage("自定义练习", "没有可用的无限制卡组。请先在无限制卡组列表中创建一副非空卡组。");
                    return;
                }

                Plugin.Logger.LogInfo(
                    $"[AIManager] Custom practice deck selector contains {decks.Count} non-empty Unlimited deck(s).");

                DialogBase dialog = UIManager.GetInstance().CreateDialogClose(false, false);
                dialog.SetSize(DialogBase.Size.XL);
                dialog.SetTitleLabel("自定义练习");
                dialog.gameObject.AddComponent<CustomPracticeSetupWindow>().Initialize(dialog, page, decks);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError($"[AIManager] Failed to open the custom deck selector.\n{exception}");
                ShowMessage("自定义练习", "读取无限制卡组失败，请查看 BepInEx 日志。");
            }
        }

        private static List<CustomPracticeDeckChoice> GetUnlimitedDeckChoices()
        {
            DeckGroup deckGroup = DeckListUtility.DeckGroupDataBase?.FirstOrDefault(group =>
                group != null &&
                group.DeckFormat == Format.Unlimited &&
                group.AttributeType == DeckAttributeType.CustomDeck);

            return deckGroup?.DeckDataList?
                .Where(deck =>
                    deck != null &&
                    deck.GetCardIdList() != null &&
                    deck.GetCardIdList().Count > 0)
                .Select(deck => new CustomPracticeDeckChoice
                {
                    Deck = deck,
                    EnemyClassId = ResolveEnemyClassId(deck)
                })
                .ToList();
        }

        private static int ResolveEnemyClassId(DeckData deck)
        {
            int deckClassId = deck.GetDeckClassID();
            if (deckClassId >= 1 && deckClassId <= 8)
            {
                return deckClassId;
            }

            try
            {
                CardMaster cardMaster = CardMaster.GetInstanceForBattle();
                int inferredClassId = deck.GetCardIdList()
                    .Select(cardId => cardMaster?.GetCardParameterFromId(cardId))
                    .Where(card => card != null && (int)card.Clan >= 1 && (int)card.Clan <= 8)
                    .GroupBy(card => (int)card.Clan)
                    .OrderByDescending(group => group.Count())
                    .Select(group => group.Key)
                    .FirstOrDefault();
                if (inferredClassId >= 1 && inferredClassId <= 8)
                {
                    Plugin.Logger.LogWarning(
                        $"[AIManager] Deck '{deck.GetDeckName()}' has invalid class {deckClassId}; " +
                        $"using inferred class {inferredClassId} for the AI opponent.");
                    return inferredClassId;
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning(
                    $"[AIManager] Failed to infer the class of deck '{deck.GetDeckName()}': {exception.Message}");
            }

            Plugin.Logger.LogWarning(
                $"[AIManager] Deck '{deck.GetDeckName()}' has no usable class; using class 1 for the AI opponent.");
            return 1;
        }

        internal static void StartCustomPractice(ClassSelectionPage page, CustomPracticeSettings settings)
        {
            try
            {
                int classId = settings.EnemyClassId;
                List<int> deckCardIds = settings.Deck.GetCardIdList().ToList();
                PracticeData practiceData = GetPracticeData(classId);
                int fieldId = practiceData?.Battle3dFieldId ?? 1;

                UIManager.GetInstance().createInSceneCenterLoading(false, false, true, null);
                DataMgr dataMgr = GameMgr.GetIns().GetDataMgr();
                dataMgr.Load();
                dataMgr.SetEnemyCharaId(settings.Leader.chara_id);

                Data.Master.LoadAICsv(
                    new AICsvLoadingInfo(
                        settings.AIPreset.DeckId,
                        settings.AIPreset.StyleId,
                        settings.AIPreset.EmoteId),
                    delegate
                    {
                        try
                        {
                            string deckAIKey = RegisterLocalDeckCsv(settings.LocalDeckCsvPath) ??
                                "ai/" + Data.Master.AIDeckFileNameList.GetFileName(settings.AIPreset.DeckId);
                            string styleAIKey = RegisterLocalStyleCsv(settings.LocalStyleCsvPath) ??
                                "ai/" + Data.Master.AIStyleFileNameList.GetFileName(settings.AIPreset.StyleId);
                            string emoteAIKey = RegisterLocalEmoteCsv(settings.LocalEmoteCsvPath) ??
                                "ai/" + Data.Master.AIEmoteFileNameList.GetFileName(settings.AIPreset.EmoteId);

                            dataMgr.RegisterAllAIData();
                            dataMgr.SetEnemyAIDeckFromCustomDeck(
                                classId,
                                deckCardIds,
                                -1,
                                settings.LogicLevel,
                                settings.MaxLife,
                                settings.AIPreset.StyleId,
                                settings.AIPreset.EmoteId,
                                true,
                                -1);
                            dataMgr.m_AIDataLibrary.SaveBattleSetUpInfo(
                                classId,
                                GetLogicLevel(settings.LogicLevel),
                                deckAIKey,
                                styleAIKey,
                                emoteAIKey,
                                true,
                                true,
                                -1,
                                null);
                            dataMgr.LoadEnemyClassData();
                            dataMgr.PracticeDifficultyDegreeId = practiceData?.DegreeId ?? 0;
                            dataMgr.SetSoroPlay3DFieldID(fieldId);
                            dataMgr.Practice3DfieldId = fieldId;
                            ActiveCustomPracticeSession = new CustomPracticeSession
                            {
                                DeckName = settings.Deck.GetDeckName(),
                                EnemyDeck = deckCardIds.ToList(),
                                EnemyClassId = classId,
                                EnemyCharaId = settings.Leader.chara_id,
                                Difficulty = -1,
                                LogicLevel = settings.LogicLevel,
                                MaxLife = settings.MaxLife,
                                StyleId = settings.AIPreset.StyleId,
                                EmoteId = settings.AIPreset.EmoteId,
                                UseInnerEmote = true,
                                EnemyAIId = -1,
                                DeckAIKey = deckAIKey,
                                StyleAIKey = styleAIKey,
                                EmoteAIKey = emoteAIKey,
                                DifficultyDegreeId = dataMgr.PracticeDifficultyDegreeId,
                                FieldId = fieldId
                            };
                            UIManager.GetInstance().closeInSceneCenterLoading(true, false);

                            Plugin.Logger.LogInfo(
                                $"[AIManager] Starting custom practice: deck='{settings.Deck.GetDeckName()}', " +
                                $"class={classId}, leader={settings.Leader.chara_id}, logic={settings.LogicLevel}, " +
                                $"maxLife={settings.MaxLife}, deckAI='{deckAIKey}', styleAI='{styleAIKey}', " +
                                $"emoteAI='{emoteAIKey}'.");

                            PracticeStartTask practiceStartTask = new PracticeStartTask();
                            page.StartCoroutine(Toolbox.NetworkManager.Connect(
                                practiceStartTask,
                                delegate(NetworkTask.ResultCode ret)
                                {
                                    UIManager.ChangeViewSceneParam sceneParam = new UIManager.ChangeViewSceneParam
                                    {
                                        IsShow_CardIntroduction = true
                                    };
                                    UIManager.GetInstance().ChangeViewScene(
                                        UIManager.ViewScene.Battle,
                                        sceneParam,
                                        null);
                                },
                                null,
                                null,
                                true,
                                false,
                                true,
                                true));
                        }
                        catch (Exception exception)
                        {
                            UIManager.GetInstance().closeInSceneCenterLoading(true, false);
                            Plugin.Logger.LogError($"[AIManager] Failed to prepare the custom practice battle.\n{exception}");
                            ShowMessage("自定义练习", "准备对战失败，请查看 BepInEx 日志。");
                        }
                    });
            }
            catch (Exception exception)
            {
                UIManager.GetInstance().closeInSceneCenterLoading(true, false);
                Plugin.Logger.LogError($"[AIManager] Failed to start the custom practice battle.\n{exception}");
                ShowMessage("自定义练习", "启动对战失败，请查看 BepInEx 日志。");
            }
        }

        internal static bool TryRestoreCustomPracticeForRetry(DataMgr dataMgr)
        {
            CustomPracticeSession session = ActiveCustomPracticeSession;
            if (dataMgr == null || session == null ||
                dataMgr.m_BattleType != DataMgr.BattleType.Practice ||
                dataMgr.m_EnemyAIDeckId != int.MinValue)
            {
                return false;
            }

            try
            {
                // Rebuild the runtime library because battle teardown may have replaced
                // its buffered setup. The local CSV assets remain registered in Master.
                dataMgr.RegisterAllAIData();
                dataMgr.SetEnemyCharaId(session.EnemyCharaId);
                dataMgr.SetEnemyAIDeckFromCustomDeck(
                    session.EnemyClassId,
                    session.EnemyDeck.ToList(),
                    session.Difficulty,
                    session.LogicLevel,
                    session.MaxLife,
                    session.StyleId,
                    session.EmoteId,
                    session.UseInnerEmote,
                    session.EnemyAIId);
                dataMgr.m_AIDataLibrary.SaveBattleSetUpInfo(
                    session.EnemyClassId,
                    GetLogicLevel(session.LogicLevel),
                    session.DeckAIKey,
                    session.StyleAIKey,
                    session.EmoteAIKey,
                    true,
                    session.UseInnerEmote,
                    session.EnemyAIId,
                    null);
                dataMgr.PracticeDifficultyDegreeId = session.DifficultyDegreeId;
                dataMgr.SetSoroPlay3DFieldID(session.FieldId);
                dataMgr.Practice3DfieldId = session.FieldId;
                dataMgr.LoadEnemyClassData();

                Plugin.Logger.LogInfo(
                    $"[AIManager] Restored custom practice for retry: " +
                    $"deck='{session.DeckName}', class={session.EnemyClassId}, " +
                    $"leader={session.EnemyCharaId}, logic={session.LogicLevel}, " +
                    $"maxLife={session.MaxLife}, deckAI='{session.DeckAIKey}', " +
                    $"styleAI='{session.StyleAIKey}', emoteAI='{session.EmoteAIKey}'.");
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError(
                    $"[AIManager] Failed to restore custom practice for retry.\n{exception}");
                return false;
            }
        }

        private static PracticeData GetPracticeData(int classId)
        {
            List<PracticeData> practiceData = Data.PracticeDataMgr.GetClassDataList(classId);
            return practiceData?.FirstOrDefault(data => !data.IsMaintenance)
                   ?? practiceData?.FirstOrDefault();
        }

        private static AI_LOGIC_LV GetLogicLevel(int logicLevel)
        {
            return logicLevel == 0
                ? AI_LOGIC_LV.WEAK
                : logicLevel == 1
                    ? AI_LOGIC_LV.MIDDLE
                    : AI_LOGIC_LV.STRONG;
        }

        private static string RegisterLocalDeckCsv(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string key = GetLocalAIKey("deck", path);
            var data = new AICardDataAssetSet();
            data.ConvertCsvTextToAsset(ReadCsv(path));
            Data.Master.AIDeckDic ??= new Dictionary<string, AICardDataAssetSet>();
            Data.Master.AIDeckDic[key] = data;
            return key;
        }

        private static string RegisterLocalStyleCsv(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string key = GetLocalAIKey("style", path);
            List<AIPolicyDataAsset> data = ReadCsv(path)
                .Select(columns => new AIPolicyDataAsset(columns))
                .ToList();
            Data.Master.AIStyleDic ??= new Dictionary<string, List<AIPolicyDataAsset>>();
            Data.Master.AIStyleDic[key] = data;
            return key;
        }

        private static string RegisterLocalEmoteCsv(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string key = GetLocalAIKey("emote", path);
            List<AIEmoteDataAsset> data = ReadCsv(path)
                .Select(columns => new AIEmoteDataAsset(columns))
                .ToList();
            Data.Master.AIEmoteDic ??= new Dictionary<string, List<AIEmoteDataAsset>>();
            Data.Master.AIEmoteDic[key] = data;
            return key;
        }

        private static List<string[]> ReadCsv(string path)
        {
            List<string[]> csv = Utility.ConvertCSV_Array(File.ReadAllText(path), true);
            if (csv == null || csv.Count == 0)
            {
                throw new InvalidDataException($"AI CSV is empty: {path}");
            }

            return csv;
        }

        private static string GetLocalAIKey(string type, string path)
        {
            return $"shadowbus/ai/{type}/{Path.GetFileNameWithoutExtension(path)}";
        }

        private static void ShowMessage(string title, string message)
        {
            DialogBase dialog = UIManager.GetInstance().CreateDialogClose(false, false);
            dialog.SetSize(DialogBase.Size.M);
            dialog.SetTitleLabel(title);
            dialog.SetButtonLayout(DialogBase.ButtonLayout.OkBtn);
            dialog.SetText(message, true);
        }

        [HarmonyPatch(typeof(Master), nameof(Master.StartLoadAIIndividualData))]
        [HarmonyPostfix]
        public static void Master_StartLoadAIIndividualData_Postfix(Master __instance)
        {
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_basic.json"), JsonConvert.SerializeObject(__instance.AIBasicDataList));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_common.json"), JsonConvert.SerializeObject(__instance.AICommonDataList));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_ally_common.json"), JsonConvert.SerializeObject(__instance.AIAllyCommonDataList));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_deck.json"), JsonConvert.SerializeObject(__instance.AIDeckDic));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_emote.json"), JsonConvert.SerializeObject(__instance.AIEmoteDic));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_style.json"), JsonConvert.SerializeObject(__instance.AIStyleDic));
        }

    }
}
