using Cute;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        // Master key prefix of the AI data that Shadowbus registers from local CSV files.
        private const string LocalAIKeyPrefix = "shadowbus/ai/";

        // Emote CSV cells may use {LEADER} (aliases: {CHARA}, {VOICE}, optionally suffixed
        // with _ID) instead of a hard-coded voice prefix. It is replaced with the voice ID
        // prefix of the leader that was picked for the AI, so one CSV can serve every leader.
        private static readonly Regex LeaderVoiceIdPlaceholder =
            new Regex(@"\{\s*(?:LEADER|CHARA|VOICE)(?:_?ID)?\s*\}", RegexOptions.IgnoreCase);

        // Emotes whose voice ID is most likely to belong to the leader itself rather than to
        // a guest character, checked before falling back to the rest of the emote master.
        private static readonly ClassCharaPrm.EmotionType[] PreferredVoiceIdEmotions =
        {
            ClassCharaPrm.EmotionType.GREET,
            ClassCharaPrm.EmotionType.BATTLESTART_DIFF,
            ClassCharaPrm.EmotionType.BATTLESTART_SAME,
            ClassCharaPrm.EmotionType.WIN,
            ClassCharaPrm.EmotionType.LOSE,
            ClassCharaPrm.EmotionType.SELECT,
            ClassCharaPrm.EmotionType.LEADER_SELECT
        };

        // FaceID, MotionID and TextID cells may be written as {AUTO} to copy the values the
        // original data pairs with that VoiceID.
        private static readonly Regex OriginalEmoteDataPlaceholder =
            new Regex(@"^\s*\{\s*(?:AUTO|ORIG|ORIGINAL)\s*\}\s*$", RegexOptions.IgnoreCase);

        private const int EmoteCsvFaceIdColumn = 2;
        private const int EmoteCsvMotionIdColumn = 3;
        private const int EmoteCsvVoiceIdColumn = 4;
        private const int EmoteCsvTextIdColumn = 5;

        // Matches the defaults that Wizard.Emotion uses for blank cells: skin_01 and idle.
        private const string DefaultFaceId = "1";
        private const string DefaultMotionId = "1";

        // Key prefix of the emote master text that the original data pairs with an AI voice line,
        // for example ET_AI_02001_000_101 for the voice ID 02001_000_101.
        private const string AIEmoteTextIdPrefix = "ET_AI_";

        private sealed class OriginalEmoteData
        {
            public int FaceId;
            public int MotionId;
            public string Text;
        }

        private sealed class OriginalEmoteIndex
        {
            // Voice ID -> the data the original files pair with exactly that line.
            public Dictionary<string, OriginalEmoteData> ByVoiceId;

            // Voice ID without the leader prefix (for example 000_101) -> the data of whichever
            // leader the game happens to describe that line for. Only used as a fallback for
            // leaders whose own AI emote set is not part of this battle.
            public Dictionary<string, OriginalEmoteData> BySuffix;
        }

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
                string leaderVoiceId = ResolveLeaderVoiceId(settings.Leader);

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
                            string emoteAIKey = RegisterLocalEmoteCsv(settings.LocalEmoteCsvPath, leaderVoiceId, settings.Leader) ??
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
                                $"class={classId}, leader={settings.Leader.chara_id}, skin={settings.Leader.skin_id}, " +
                                $"leaderVoiceId='{leaderVoiceId}', logic={settings.LogicLevel}, " +
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

        internal static string RegisterLocalDeckCsv(string path)
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

        internal static string RegisterLocalStyleCsv(string path)
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

        internal static string RegisterLocalEmoteCsv(string path, string leaderVoiceId, ClassCharacterMasterData leader)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string key = GetLocalAIKey("emote", path);
            string fileName = Path.GetFileName(path);
            List<string[]> csv = ReadCsv(path);
            ApplyLeaderVoiceId(csv, leaderVoiceId, fileName);
            ApplyOriginalEmoteData(csv, leader, fileName);
            List<AIEmoteDataAsset> data = csv
                .Select(columns => new AIEmoteDataAsset(columns))
                .ToList();
            Data.Master.AIEmoteDic ??= new Dictionary<string, List<AIEmoteDataAsset>>();
            Data.Master.AIEmoteDic[key] = data;
            return key;
        }

        internal static string RegisterLocalEmoteCsv(string path)
        {
            return RegisterLocalEmoteCsv(path, null, null);
        }

        /// <summary>
        /// Replaces {AUTO} in the FaceID, MotionID and TextID columns with the values the
        /// original data pairs with the VoiceID of that row, so an emote CSV only has to
        /// carry a Category and a VoiceID.
        /// </summary>
        private static void ApplyOriginalEmoteData(List<string[]> csv, ClassCharacterMasterData leader, string fileName)
        {
            bool hasPlaceholder = csv.Any(columns => columns.Any(cell =>
                !string.IsNullOrEmpty(cell) && OriginalEmoteDataPlaceholder.IsMatch(cell)));
            if (!hasPlaceholder)
            {
                return;
            }

            OriginalEmoteIndex index = BuildOriginalEmoteIndex(leader);
            int exactRows = 0;
            int approximateRows = 0;
            var unresolvedVoiceIds = new List<string>();

            foreach (string[] columns in csv)
            {
                if (!columns.Any(cell => !string.IsNullOrEmpty(cell) && OriginalEmoteDataPlaceholder.IsMatch(cell)))
                {
                    continue;
                }

                string voiceId = columns.Length > EmoteCsvVoiceIdColumn ? columns[EmoteCsvVoiceIdColumn] : null;
                OriginalEmoteData original = null;
                if (!string.IsNullOrEmpty(voiceId))
                {
                    if (index.ByVoiceId.TryGetValue(voiceId, out original))
                    {
                        exactRows++;
                    }
                    else if (TryGetVoiceIdSuffix(voiceId, out string suffix) &&
                             index.BySuffix.TryGetValue(suffix, out original))
                    {
                        approximateRows++;
                    }
                }

                if (original == null)
                {
                    unresolvedVoiceIds.Add(string.IsNullOrEmpty(voiceId) ? "(empty)" : voiceId);
                }

                FillPlaceholder(columns, EmoteCsvFaceIdColumn,
                    original == null ? DefaultFaceId : original.FaceId.ToString());
                FillPlaceholder(columns, EmoteCsvMotionIdColumn,
                    original == null ? DefaultMotionId : original.MotionId.ToString());
                FillPlaceholder(columns, EmoteCsvTextIdColumn, ResolveEmoteText(original, voiceId));
            }

            Plugin.Logger.LogInfo(
                $"[AIManager] Emote CSV '{fileName}': filled {exactRows} row(s) from the original data of " +
                $"that exact voice line and {approximateRows} row(s) from another leader's line with the " +
                $"same number.");

            if (unresolvedVoiceIds.Count > 0)
            {
                Plugin.Logger.LogWarning(
                    $"[AIManager] Emote CSV '{fileName}': {unresolvedVoiceIds.Count} row(s) use a voice ID " +
                    $"that the original data does not describe and fell back to face {DefaultFaceId} / " +
                    $"motion {DefaultMotionId}: " +
                    string.Join(", ", unresolvedVoiceIds.Distinct().Take(10).ToArray()) + ".");
            }
        }

        /// <summary>
        /// Returns the emote text of a voice line. The emote master text is loaded for every
        /// character, so it resolves even for leaders whose AI emote set is not part of this
        /// battle.
        /// </summary>
        private static string ResolveEmoteText(OriginalEmoteData original, string voiceId)
        {
            if (original != null && !string.IsNullOrEmpty(original.Text))
            {
                return original.Text;
            }

            if (string.IsNullOrEmpty(voiceId))
            {
                return string.Empty;
            }

            string textId = AIEmoteTextIdPrefix + voiceId;
            string text = Data.Master.GetEmoteWordText(textId);

            // The master returns the key itself when it holds no entry for it.
            return string.IsNullOrEmpty(text) || text == textId ? string.Empty : text;
        }

        private static bool TryGetVoiceIdSuffix(string voiceId, out string suffix)
        {
            int separatorIndex = voiceId == null ? -1 : voiceId.IndexOf('_');
            suffix = separatorIndex > 0 ? voiceId.Substring(separatorIndex + 1) : null;
            return !string.IsNullOrEmpty(suffix);
        }

        private static void FillPlaceholder(string[] columns, int columnIndex, string value)
        {
            if (columnIndex < columns.Length &&
                !string.IsNullOrEmpty(columns[columnIndex]) &&
                OriginalEmoteDataPlaceholder.IsMatch(columns[columnIndex]))
            {
                columns[columnIndex] = value;
            }
        }

        /// <summary>
        /// Indexes every voice ID the game currently knows about by the FaceID, MotionID and
        /// text the original data pairs with it.
        /// </summary>
        private static OriginalEmoteIndex BuildOriginalEmoteIndex(ClassCharacterMasterData leader)
        {
            var index = new OriginalEmoteIndex
            {
                ByVoiceId = new Dictionary<string, OriginalEmoteData>(StringComparer.OrdinalIgnoreCase),
                BySuffix = new Dictionary<string, OriginalEmoteData>(StringComparer.OrdinalIgnoreCase)
            };

            // The original AI emote sets loaded so far. These hold the AI-only voice lines,
            // for example 02001_000_101. Emote sets that a previous battle registered from a
            // local CSV are skipped: they are already resolved output, not original data.
            Dictionary<string, List<AIEmoteDataAsset>> aiEmoteDic = Data.Master.AIEmoteDic;
            if (aiEmoteDic != null)
            {
                foreach (KeyValuePair<string, List<AIEmoteDataAsset>> entry in aiEmoteDic)
                {
                    if (entry.Key != null && entry.Key.StartsWith(LocalAIKeyPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    List<AIEmoteDataAsset> emoteSet = entry.Value;
                    foreach (AIEmoteDataAsset asset in emoteSet ?? Enumerable.Empty<AIEmoteDataAsset>())
                    {
                        if (asset == null || string.IsNullOrEmpty(asset.VoiceID))
                        {
                            continue;
                        }

                        var original = new OriginalEmoteData
                        {
                            FaceId = asset.FaceID,
                            MotionId = asset.MotionID,
                            Text = asset.TextID
                        };
                        index.ByVoiceId[asset.VoiceID] = original;

                        // Only the face and motion carry over to another leader; the text of
                        // this row belongs to whoever speaks the line.
                        if (TryGetVoiceIdSuffix(asset.VoiceID, out string suffix) &&
                            !index.BySuffix.ContainsKey(suffix))
                        {
                            index.BySuffix[suffix] = new OriginalEmoteData
                            {
                                FaceId = asset.FaceID,
                                MotionId = asset.MotionID,
                                Text = null
                            };
                        }
                    }
                }
            }

            // The leader's own emote master wins: it describes the greeting, evolution and
            // idle lines, and it is the only source for skins that have no AI voice lines.
            AddLeaderEmoteMasterToIndex(index.ByVoiceId, leader);
            return index;
        }

        private static void AddLeaderEmoteMasterToIndex(
            Dictionary<string, OriginalEmoteData> index,
            ClassCharacterMasterData leader)
        {
            if (leader == null)
            {
                return;
            }

            try
            {
                Dictionary<string, Dictionary<ClassCharaPrm.EmotionType, Wizard.Emotion>> emoteMaster =
                    Data.Master._emotionDic;
                if (emoteMaster == null ||
                    !emoteMaster.TryGetValue(leader.skin_id.ToString(), out Dictionary<ClassCharaPrm.EmotionType, Wizard.Emotion> emotions) ||
                    emotions == null)
                {
                    return;
                }

                foreach (Wizard.Emotion emotion in emotions.Values)
                {
                    string voiceId = emotion?.GetVoiceId();
                    if (string.IsNullOrEmpty(voiceId))
                    {
                        continue;
                    }

                    index[voiceId] = new OriginalEmoteData
                    {
                        FaceId = (int)emotion.face_id,
                        MotionId = (int)emotion.motion_id,
                        Text = emotion.GetText()
                    };
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning(
                    $"[AIManager] Failed to index the emote master of leader {leader.chara_id} " +
                    $"(skin {leader.skin_id}): {exception.Message}");
            }
        }

        /// <summary>
        /// Replaces the leader voice ID placeholder in an emote CSV with the voice ID prefix of
        /// the leader chosen for the AI, so a single CSV can be reused for every leader.
        /// </summary>
        private static void ApplyLeaderVoiceId(List<string[]> csv, string leaderVoiceId, string fileName)
        {
            int replacedCells = 0;
            bool hasVoiceId = !string.IsNullOrEmpty(leaderVoiceId);

            foreach (string[] columns in csv)
            {
                for (int i = 0; i < columns.Length; i++)
                {
                    string cell = columns[i];
                    if (string.IsNullOrEmpty(cell) || !LeaderVoiceIdPlaceholder.IsMatch(cell))
                    {
                        continue;
                    }

                    // Without a voice ID the placeholder would become a broken cue name, so
                    // blank the cell instead: the emote then plays silently.
                    columns[i] = hasVoiceId
                        ? LeaderVoiceIdPlaceholder.Replace(cell, leaderVoiceId)
                        : string.Empty;
                    replacedCells++;
                }
            }

            if (replacedCells == 0)
            {
                return;
            }

            if (hasVoiceId)
            {
                Plugin.Logger.LogInfo(
                    $"[AIManager] Emote CSV '{fileName}': applied leader voice ID '{leaderVoiceId}' " +
                    $"to {replacedCells} cell(s).");
            }
            else
            {
                Plugin.Logger.LogWarning(
                    $"[AIManager] Emote CSV '{fileName}': the leader voice ID is unknown; " +
                    $"cleared {replacedCells} placeholder cell(s) so the emotes play without voice.");
            }
        }

        /// <summary>
        /// Returns the part of the leader skin's voice IDs before the first underscore, for
        /// example "1602" for the voice IDs "1602_000_001" and "1602_000_023". Most skins use
        /// their own skin ID; the eight starting leaders use a separate code such as "02001".
        /// </summary>
        private static string ResolveLeaderVoiceId(ClassCharacterMasterData leader)
        {
            if (leader == null)
            {
                return null;
            }

            string skinId = leader.skin_id.ToString();

            try
            {
                string voiceId = GetVoiceIdFromEmoteMaster(skinId);
                if (!string.IsNullOrEmpty(voiceId))
                {
                    return voiceId;
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning(
                    $"[AIManager] Failed to read the voice ID of leader skin {skinId} " +
                    $"from the emote master: {exception.Message}");
            }

            // Every other skin names its voice lines after its own skin ID, so that is the only
            // safe guess. Deriving one from the class would play a different leader's voice.
            Plugin.Logger.LogWarning(
                $"[AIManager] The emote master has no voice ID for leader skin {skinId}; " +
                $"using the skin ID itself.");
            return skinId;
        }

        private static string GetVoiceIdFromEmoteMaster(string skinId)
        {
            Dictionary<string, Dictionary<ClassCharaPrm.EmotionType, Wizard.Emotion>> emoteMaster =
                Data.Master._emotionDic;
            if (emoteMaster == null ||
                !emoteMaster.TryGetValue(skinId, out Dictionary<ClassCharaPrm.EmotionType, Wizard.Emotion> emotions) ||
                emotions == null)
            {
                return null;
            }

            IEnumerable<Wizard.Emotion> orderedEmotions = PreferredVoiceIdEmotions
                .Where(emotionType => emotions.ContainsKey(emotionType))
                .Select(emotionType => emotions[emotionType])
                .Concat(emotions.OrderBy(entry => (int)entry.Key).Select(entry => entry.Value));

            foreach (Wizard.Emotion emotion in orderedEmotions)
            {
                if (TryGetVoiceIdPrefix(emotion?.GetVoiceId(), out string prefix))
                {
                    return prefix;
                }
            }

            return null;
        }

        /// <summary>
        /// Splits off the leading number of a voice ID. Entries such as "char_select_1602" are
        /// rejected because their first segment is not the voice ID prefix.
        /// </summary>
        private static bool TryGetVoiceIdPrefix(string voiceId, out string prefix)
        {
            prefix = null;
            int separatorIndex = voiceId == null ? -1 : voiceId.IndexOf('_');
            if (separatorIndex <= 0)
            {
                return false;
            }

            string candidate = voiceId.Substring(0, separatorIndex);
            if (!candidate.All(char.IsDigit))
            {
                return false;
            }

            prefix = candidate;
            return true;
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
            return $"{LocalAIKeyPrefix}{type}/{Path.GetFileNameWithoutExtension(path)}";
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
            // These files are diagnostic/reference data for the local Practice AI editor.
            // Do not overwrite them on every master reload: users may edit or annotate the
            // exported JSON, and Master.StartLoadAIIndividualData can run more than once.
            ExportPracticeAIJsonIfMissing("ai_basic.json", __instance.AIBasicDataList);
            ExportPracticeAIJsonIfMissing("ai_common.json", __instance.AICommonDataList);
            ExportPracticeAIJsonIfMissing("ai_ally_common.json", __instance.AIAllyCommonDataList);
            ExportPracticeAIJsonIfMissing("ai_deck.json", __instance.AIDeckDic);
            ExportPracticeAIJsonIfMissing("ai_emote.json", __instance.AIEmoteDic);
            ExportPracticeAIJsonIfMissing("ai_style.json", __instance.AIStyleDic);
        }

        private static void ExportPracticeAIJsonIfMissing(string fileName, object value)
        {
            try
            {
                string path = Path.Combine(PathHelper.AIDataPath, fileName);
                if (File.Exists(path))
                {
                    return;
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(value));
                Plugin.Logger.LogInfo($"[AIManager] Exported Practice AI reference: {fileName}");
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[AIManager] Failed to export Practice AI reference '{fileName}': {exception.Message}");
            }
        }

    }
}
