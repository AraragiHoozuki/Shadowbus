using Cute;
using LitJson;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Wizard;

namespace Shadowbus
{
    internal static class StoryOfflineData
    {
        private const string StoryInfoTaskName = nameof(StoryInfoTask);
        private const string StoryLeaderSelectTaskName = nameof(StoryLeaderSelectTask);
        private const string StoryStartTaskName = nameof(StoryStartTask);
        private const string StoryDeckListTaskName = nameof(StoryDeckListTask);
        private const string StoryFinishTaskName = nameof(StoryFinishTask);
        private const string StoryAllFinishTaskName = nameof(StoryAllFinishTask);
        private const int DefaultChapterButtonBackgroundId = 2;

        private static readonly Regex ScenarioParamRegex = new Regex(
            @"^story_scenario_param_(?<section>\d+)_(?<class>\d+)_(?<chapter>\d+[a-z]?)_(?<part>[12])(?:_subchapter(?<subchapter>\d+))?\.unity3d$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // The story AI master only links an AI id to deck/style/emote CSV files. Enemy
        // class is server metadata, so recover it from the dominant non-neutral class
        // in each original AI deck bundled with the client.
        private static readonly HashSet<int>[] OriginalEnemyAiIdsByClass =
        {
            new HashSet<int>(),
            new HashSet<int> { 1006, 1007, 1009, 1010, 2003, 2012, 3007, 4006, 5005, 5009, 7009, 8003, 101003, 110303, 110503, 110706, 110708, 110202, 110206, 110212, 120005, 120013, 130607, 130509, 150101, 150108, 150407, 150201, 150206, 150702, 150708, 160005, 160012, 160015, 170005, 200108, 270013, 280004 },
            new HashSet<int> { 1001, 1008, 2001, 2006, 2011, 3009, 3011, 3012, 4005, 6001, 6008, 6009, 7005, 7010, 101002, 110106, 110112, 110312, 110609, 110611, 110510, 110408, 110409, 110707, 110806, 110807, 110809, 110810, 110211, 120003, 120010, 130608, 130306, 150411, 150204, 160004, 180303, 180703, 220010, 230611, 270010, 280010 },
            new HashSet<int> { 1005, 1014, 2007, 2010, 2014, 3001, 3006, 3010, 3014, 4004, 4008, 4013, 4014, 5003, 5010, 5014, 6005, 6013, 6014, 7002, 7014, 8004, 101004, 101009, 110105, 110302, 110307, 110605, 110507, 110410, 110204, 120014, 130604, 130308, 130807, 130808, 150111, 150202, 150705, 160006, 170006, 280020 },
            new HashSet<int> { 1004, 2004, 3004, 3008, 3013, 4001, 4007, 4009, 4010, 4011, 5004, 6003, 7003, 7012, 101005, 110102, 110311, 110504, 120004, 130507, 130804, 140004, 150104, 150401, 150405, 150408, 150207, 200405, 200407, 200408, 200504, 200606, 220006, 240407, 240411, 240508, 250007, 260709, 270009, 280015 },
            new HashSet<int> { 1003, 1012, 3003, 4002, 5001, 5006, 5008, 5011, 6002, 6011, 7004, 7011, 8005, 101008, 110103, 110304, 110604, 110502, 110511, 110402, 110406, 110710, 110802, 120008, 120009, 130303, 130810, 150102, 150404, 150210, 180310, 190008, 200503, 200508, 200607, 230607, 240410, 240509, 270008, 290008 },
            new HashSet<int> { 1002, 1011, 2002, 2008, 3005, 4003, 5002, 5012, 6004, 6007, 6010, 7006, 7013, 101007, 110110, 110603, 110512, 110404, 110711, 110803, 120002, 130603, 130503, 130309, 150105, 150203, 150703, 170003, 180802, 180809, 200103, 200104, 200506, 200604, 210006, 210007, 210008, 210010, 230612, 250015, 290032 },
            new HashSet<int> { 1013, 2005, 2009, 2013, 3002, 4012, 5007, 5013, 6006, 6012, 7001, 7007, 7008, 101006, 110111, 110306, 110607, 110703, 110804, 110210, 120012, 130609, 130508, 130805, 150103, 150402, 150701, 150704, 150706, 160007, 170004, 180807 },
            new HashSet<int> { 8001, 8002, 8006, 101010, 101011, 110602, 110411, 110705, 110207, 120015, 130002, 130610, 130505, 130511, 130305, 130312, 130811, 140006, 140007, 140008, 140011, 170007, 170011, 170012, 180206, 180209, 180210, 180309, 180707, 180711, 190006, 190007, 190010, 210012, 210013, 220004, 220005, 220007, 220021, 220024, 240511, 250005, 250010, 260710, 290017, 290024, 290035, 290038 }
        };

        private static readonly Dictionary<int, int> OriginalEnemyCharaIdByAiId =
            new Dictionary<int, int>
            {
                [140006] = 500049, // Megaera
                [140007] = 500048, // Alecto
                [140008] = 500047, // Tisiphone
                [140011] = 500046, // Belphomet
                [160004] = 500101, // Bayleon
                [160015] = 500107, // Viridia Magna
                [170007] = 500217, // mechanical infantry
                [170012] = 500211, // fused Belphomet
                [190010] = 500310, // Iceschillendrig
                [220021] = 508,    // Nexus
                [220024] = 500601, // transformed Iceschillendrig
                [240410] = 3505,   // Anserge
                [240411] = 3514,   // Suhlon
                [240508] = 3504,   // Mizuchi
                [250007] = 3514,   // Suhlon
                [250015] = 500712, // transformed Taketsumi
                [260709] = 500943, // dragon
                [270008] = 4105,   // Cornelius
                [270009] = 4104,   // Lilium
                [270010] = 4102,   // Weiss
                [270013] = 500948, // Castelle
                [290008] = 500211, // fused Belphomet
                [290017] = 500731, // Maiser
                [290024] = 508,    // Nexus
                [290032] = 500712, // transformed Taketsumi
                [290035] = 4528,   // Nerva
                [290038] = 4528    // Nerva
            };

        private static readonly object ManifestLock = new object();
        private static StoryManifest _cachedManifest;
        private static DateTime _cachedManifestWriteTimeUtc;

        internal static bool CanHandle(string taskName)
        {
            return taskName == StoryInfoTaskName ||
                taskName == StoryLeaderSelectTaskName ||
                taskName == StoryStartTaskName ||
                taskName == StoryDeckListTaskName ||
                taskName == StoryFinishTaskName ||
                taskName == StoryAllFinishTaskName;
        }

        internal static bool TryCreateResponse(NetworkTask task, out JsonData response)
        {
            response = null;
            try
            {
                object responseObject;
                if (task is StoryInfoTask && task.Params is StoryInfoTask.StoryInfoTaskParam infoParam)
                {
                    responseObject = CreateStoryInfoResponse(infoParam);
                }
                else if (task is StoryLeaderSelectTask && task.Params is StoryLeaderSelectTask.StoryLeaderSelectTaskParam leaderParam)
                {
                    responseObject = CreateLeaderSelectResponse(leaderParam);
                }
                else if (task is StoryStartTask)
                {
                    responseObject = CreateStoryStartResponse();
                }
                else if (task is StoryDeckListTask)
                {
                    responseObject = CreateStoryDeckListResponse();
                }
                else if (task is StoryFinishTask)
                {
                    responseObject = CreateStoryFinishResponse();
                }
                else if (task is StoryAllFinishTask)
                {
                    responseObject = CreateResponseEnvelope(new Dictionary<string, object>());
                }
                else
                {
                    return false;
                }

                response = JsonMapper.ToObject(JsonConvert.SerializeObject(responseObject));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Offlinizer] Failed to create local story data for {task.GetType().Name}: {ex}");
                return false;
            }
        }

        private static object CreateStoryInfoResponse(StoryInfoTask.StoryInfoTaskParam param)
        {
            StoryManifest manifest = GetManifest();
            int? selectedClassId = GetClassId(param.chara_id);
            List<ScenarioChapter> chapters = manifest.GetChapters(param.section_id, selectedClassId);
            List<Dictionary<string, object>> chapterResponses = new List<Dictionary<string, object>>();

            for (int index = 0; index < chapters.Count; index++)
            {
                chapterResponses.Add(CreateChapterResponse(
                    chapters[index],
                    GetNextChapterId(chapters, index),
                    param.chara_id,
                    index));
            }

            Plugin.Logger.LogInfo(
                $"[Offlinizer] Created StoryInfoTask data: section={param.section_id}, " +
                $"class={(selectedClassId?.ToString(CultureInfo.InvariantCulture) ?? "all")}, " +
                $"chapters={chapterResponses.Count}, battles={chapters.Count(chapter => chapter.HasSecondHalf)}.");

            return CreateResponseEnvelope(new Dictionary<string, object>
            {
                ["story_master_list"] = chapterResponses,
                ["maintenance_card_list"] = Array.Empty<int>()
            });
        }

        private static object CreateLeaderSelectResponse(StoryLeaderSelectTask.StoryLeaderSelectTaskParam param)
        {
            StoryManifest manifest = GetManifest();
            List<Dictionary<string, object>> leaders = manifest.GetClassIds(param.section_id)
                .Select(classId => GetDefaultCharacterId(classId))
                .Where(charaId => charaId != 0)
                .Select(charaId => new Dictionary<string, object>
                {
                    ["chara_id"] = charaId,
                    ["is_finished"] = false,
                    ["current_chapter"] = "1"
                })
                .ToList();

            Plugin.Logger.LogInfo(
                $"[Offlinizer] Created StoryLeaderSelectTask data: section={param.section_id}, leaders={leaders.Count}.");

            return CreateResponseEnvelope(new Dictionary<string, object>
            {
                ["leader_list"] = leaders,
                ["leader_count"] = leaders.Count
            });
        }

        private static object CreateStoryStartResponse()
        {
            // StoryStartTask reads the first value in data. An empty value means that the
            // chapter has no server-defined special battle overrides.
            return CreateResponseEnvelope(new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>()
            });
        }

        private static object CreateStoryDeckListResponse()
        {
            List<object> unlimitedDecks = new List<object>();
            if (Directory.Exists(Plugin.UnlimitedDeckPath))
            {
                foreach (string path in Directory.GetFiles(Plugin.UnlimitedDeckPath, "*.json")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        unlimitedDecks.Add(JsonConvert.DeserializeObject<object>(File.ReadAllText(path)));
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogWarning(
                            $"[Offlinizer] Ignoring invalid story deck file '{Path.GetFileName(path)}': {ex.Message}");
                    }
                }
            }

            Plugin.Logger.LogInfo($"[Offlinizer] Created StoryDeckListTask data with {unlimitedDecks.Count} unlimited decks.");
            return CreateResponseEnvelope(new Dictionary<string, object>
            {
                ["user_deck_rotation"] = Array.Empty<object>(),
                ["user_deck_unlimited"] = unlimitedDecks,
                ["user_deck_my_rotation"] = Array.Empty<object>(),
                ["maintenance_card_list"] = Array.Empty<int>()
            });
        }

        private static object CreateStoryFinishResponse()
        {
            GetCurrentClassProgress(out int classLevel, out int classExperience);
            return CreateResponseEnvelope(new Dictionary<string, object>
            {
                ["get_class_experience"] = 0,
                ["class_experience"] = classExperience,
                ["class_level"] = classLevel,
                ["achieved_info"] = Array.Empty<object>(),
                ["story_reward_list"] = Array.Empty<object>(),
                ["reward_list"] = Array.Empty<object>(),
                ["quest"] = new Dictionary<string, object> { ["is_display_badge"] = false },
                ["story_notification"] = new Dictionary<string, object> { ["is_display_badge"] = false },
                ["basic_puzzle"] = new Dictionary<string, object> { ["is_display_badge"] = false },
                ["shop_notification"] = new Dictionary<string, object>
                {
                    ["card_pack"] = false,
                    ["build_deck"] = false,
                    ["sleeve"] = false,
                    ["leader_skin"] = false
                },
                ["receive_friend_apply_count"] = 0,
                ["competition_info"] = new Dictionary<string, object> { ["is_competition_period"] = false },
                ["gathering_info"] = new Dictionary<string, object> { ["has_invite"] = 0 },
                ["is_available_colosseum_free_entry"] = false
            });
        }

        private static void GetCurrentClassProgress(out int classLevel, out int classExperience)
        {
            classLevel = 1;
            classExperience = 0;
            try
            {
                DataMgr dataMgr = GameMgr.GetIns().GetDataMgr();
                int classId = dataMgr.GetPlayerClassId();
                ClassCharaPrm classData = classId > 0 ? dataMgr.GetClassPrm(classId) : null;
                if (classData != null)
                {
                    classLevel = Math.Max(1, classData.GetClassCharaLv());
                    classExperience = Math.Max(0, classData.GetClassCharaExp());
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Offlinizer] Could not read story class progress: {ex.Message}");
            }
        }

        private static object CreateResponseEnvelope(object data)
        {
            return new Dictionary<string, object>
            {
                ["data_headers"] = new Dictionary<string, object>
                {
                    ["short_udid"] = 0,
                    ["viewer_id"] = 0,
                    ["sid"] = string.Empty,
                    ["servertime"] = 0L,
                    ["result_code"] = 1
                },
                ["data"] = data
            };
        }

        private static Dictionary<string, object> CreateChapterResponse(
            ScenarioChapter chapter,
            string nextChapterId,
            int selectedCharaId,
            int displayIndex)
        {
            int chapterCharaId = selectedCharaId;
            if (chapterCharaId == 0 && chapter.ClassId != 0)
            {
                chapterCharaId = GetDefaultCharacterId(chapter.ClassId);
            }

            bool hasBattle = chapter.HasSecondHalf && chapter.BattleClassIds.Count > 0;
            StoryAISettingData aiSetting = hasBattle ? ResolveOriginalStoryAiSetting(chapter) : null;
            int battleAiId = aiSetting?.EnemyAiId ?? 0;
            int enemyClassId = hasBattle ? ResolveOriginalEnemyClassId(aiSetting) : 0;
            int enemyCharaId = hasBattle
                ? ResolveOriginalEnemyCharaId(aiSetting, enemyClassId)
                : 0;
            List<Dictionary<string, object>> battleSettings = hasBattle
                ? chapter.BattleClassIds.Select(classId => new Dictionary<string, object>
                {
                    ["deck_class_id"] = classId,
                    ["deck_skin_id_override"] = 0,
                    ["skin_id_override"] = GetDefaultCharacterId(classId),
                    ["player_emotion_override"] = 0,
                    ["enemy_emotion_override"] = 0,
                    ["battle3dfield_id_override"] = 0,
                    ["bgm_id_override"] = "0"
                }).ToList()
                : new List<Dictionary<string, object>>();

            int column = displayIndex % 6;
            int mapRow = displayIndex / 6;

            return new Dictionary<string, object>
            {
                ["story_id"] = CreateStoryId(chapter.SectionId, chapter.ClassId, chapter.ChapterId),
                ["section_id"] = chapter.SectionId,
                ["chara_id"] = chapterCharaId,
                ["chapter_id"] = chapter.ChapterId,
                ["next_chapter_id"] = nextChapterId,
                ["sub_chapters"] = chapter.SubChapterIds.Select(subChapterId => new Dictionary<string, object>
                {
                    ["story_id"] = CreateStoryId(chapter.SectionId, chapter.ClassId, chapter.ChapterId) + subChapterId,
                    ["sub_chapter_id"] = subChapterId,
                    ["is_finish"] = false,
                    ["is_maintenance_chapter"] = false
                }).ToList(),
                ["battle_exists"] = hasBattle,
                ["show_subtitles"] = 1,
                ["is_maintenance_chapter"] = false,
                ["is_released"] = true,
                ["is_lock"] = false,
                ["unlock_text"] = string.Empty,
                ["is_finish"] = false,
                ["is_skipped"] = false,
                ["story_reward"] = Array.Empty<object>(),
                ["chapter_clear_text_id"] = string.Empty,
                ["is_skip_enabled"] = false,
                ["enemy_chara_id"] = enemyCharaId,
                ["enemy_class"] = enemyClassId,
                ["enemy_ai_id"] = hasBattle ? battleAiId : 0,
                ["battle3dfield_id"] = hasBattle ? 1 : 0,
                ["bgm_id"] = "0",
                ["battle_settings"] = battleSettings,
                ["x_coordinate"] = -500 + column * 200,
                ["y_coordinate"] = 260 - mapRow * 150,
                ["show_coordinate"] = 1,
                ["is_camera_ movable"] = 1,
                ["bg_file_name"] = DefaultChapterButtonBackgroundId.ToString(CultureInfo.InvariantCulture),
                ["chapter_effect_path"] = string.Empty,
                ["selection_display_position"] = GetSelectionDisplayPosition(chapter.ChapterId),
                ["selection_text_id"] = string.Empty,
                ["required_chapter_id"] = string.Empty,
                ["is_released_another_end"] = false,
                ["is_play_another_end_appearance_animation"] = false
            };
        }

        private static StoryAISettingData ResolveOriginalStoryAiSetting(ScenarioChapter chapter)
        {
            IReadOnlyList<StoryAISettingData> settings = Data.Master?.StoryAISettingList?.GetSettingDataTable();
            if (settings == null || settings.Count == 0)
            {
                throw new InvalidOperationException("No Story AI setting is available in the loaded master data.");
            }

            int chapterNumber = GetChapterRow(chapter.ChapterId);
            int originalAiId = CreateOriginalStoryAiId(
                chapter.SectionId,
                chapter.ClassId,
                chapterNumber);
            StoryAISettingData originalSetting = settings.FirstOrDefault(
                setting => setting != null && setting.EnemyAiId == originalAiId);
            if (originalSetting != null)
            {
                return originalSetting;
            }

            StoryAISettingData fallback = settings.FirstOrDefault(
                setting => setting != null && setting.EnemyAiId > 0 && setting.DeckId >= 0);
            if (fallback == null)
            {
                throw new InvalidOperationException("No usable Story AI setting is available in the loaded master data.");
            }

            Plugin.Logger.LogWarning(
                $"[Offlinizer] Original Story AI was not resolved for section={chapter.SectionId}, " +
                $"class={chapter.ClassId}, chapter={chapter.ChapterId}; using AI {fallback.EnemyAiId}.");
            return fallback;
        }

        private static int ResolveOriginalEnemyClassId(StoryAISettingData aiSetting)
        {
            if (aiSetting == null)
            {
                return 8;
            }

            for (int classId = 1; classId < OriginalEnemyAiIdsByClass.Length; classId++)
            {
                if (OriginalEnemyAiIdsByClass[classId].Contains(aiSetting.EnemyAiId))
                {
                    return classId;
                }
            }

            // Keep support for future locally available AI data that was not in the
            // final client table used to generate OriginalEnemyAiIdsByClass.
            try
            {
                string deckName = Data.Master.AIDeckFileNameList.GetFileName(aiSetting.DeckId);
                string deckKey = "ai/" + deckName;
                if (Data.Master.AIDeckDic.TryGetValue(deckKey, out AICardDataAssetSet deck))
                {
                    IGrouping<int, AICardDataAsset> dominantClass = deck.Set
                        .Where(card => card != null)
                        .GroupBy(card => GetCardClassId(card.CardID))
                        .Where(group => group.Key >= 1 && group.Key <= 8)
                        .OrderByDescending(group => group.Sum(card => Math.Max(1, card.CardNum)))
                        .FirstOrDefault();
                    if (dominantClass != null)
                    {
                        return dominantClass.Key;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    $"[Offlinizer] Could not infer enemy class for story AI {aiSetting.EnemyAiId}: {ex.Message}");
            }

            Plugin.Logger.LogWarning(
                $"[Offlinizer] Enemy class is unknown for story AI {aiSetting.EnemyAiId}; using Portalcraft.");
            return 8;
        }

        private static int ResolveOriginalEnemyCharaId(StoryAISettingData aiSetting, int enemyClassId)
        {
            if (aiSetting != null &&
                OriginalEnemyCharaIdByAiId.TryGetValue(aiSetting.EnemyAiId, out int originalCharaId) &&
                IsKnownCharacter(originalCharaId))
            {
                return originalCharaId;
            }

            // Several late-story emote ids are the actual story character id. Smaller
            // ids are often shared dialogue sets and must not be treated as leaders.
            if (aiSetting != null && aiSetting.EmoteId >= 500000 && IsKnownCharacter(aiSetting.EmoteId))
            {
                return aiSetting.EmoteId;
            }

            return GetDefaultCharacterId(enemyClassId);
        }

        private static bool IsKnownCharacter(int charaId)
        {
            try
            {
                return GameMgr.GetIns().GetDataMgr().GetCharaPrmByCharaId(charaId) != null;
            }
            catch
            {
                return false;
            }
        }

        private static int GetCardClassId(int cardId)
        {
            return cardId / 100000 % 10;
        }

        private static int CreateOriginalStoryAiId(int sectionId, int classId, int chapterNumber)
        {
            switch (sectionId)
            {
                case 1:
                    return classId * 1000 + chapterNumber;
                case 2:
                    return 101000 + chapterNumber;
                case 3:
                    return 110000 + classId * 100 + chapterNumber;
                case 4:
                    return 120000 + chapterNumber;
                case 5:
                    return chapterNumber == 2
                        ? 130002
                        : 130000 + classId * 100 + chapterNumber;
                case 6:
                    return 140000 + chapterNumber;
                case 7:
                    return 150000 + classId * 100 + chapterNumber;
                case 8:
                    return 160000 + chapterNumber;
                case 9:
                    return 170000 + chapterNumber;
                case 10:
                    return 180000 + classId * 100 + chapterNumber;
                case 11:
                    return 190000 + chapterNumber;
                case 12:
                    return 200000 + classId * 100 + chapterNumber;
                case 13:
                    return 210000 + chapterNumber;
                case 14:
                    return 220000 + chapterNumber;
                case 15:
                    return (classId == 6 ? 230000 : 240000) + classId * 100 + chapterNumber;
                case 16:
                    return 250000 + chapterNumber;
                case 17:
                    return 260000 + classId * 100 + chapterNumber;
                case 18:
                    return 270000 + chapterNumber;
                case 19:
                    if (chapterNumber == 5)
                    {
                        return 280004;
                    }
                    if (chapterNumber == 11)
                    {
                        return 280010;
                    }
                    return 280000 + chapterNumber;
                case 20:
                    return 290000 + chapterNumber;
                default:
                    return 0;
            }
        }

        private static StoryManifest GetManifest()
        {
            string manifestPath = Path.Combine(Application.persistentDataPath, "manifest", "story_assetmanifest");
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("The local story asset manifest was not found.", manifestPath);
            }

            DateTime writeTimeUtc = File.GetLastWriteTimeUtc(manifestPath);
            lock (ManifestLock)
            {
                if (_cachedManifest != null && _cachedManifestWriteTimeUtc == writeTimeUtc)
                {
                    return _cachedManifest;
                }

                _cachedManifest = StoryManifest.Load(manifestPath);
                _cachedManifestWriteTimeUtc = writeTimeUtc;
                Plugin.Logger.LogInfo(
                    $"[Offlinizer] Loaded {_cachedManifest.ChapterCount} story chapters from the local asset manifest.");
                return _cachedManifest;
            }
        }

        private static int? GetClassId(int charaId)
        {
            if (charaId == 0)
            {
                return null;
            }

            ClassCharacterMasterData charaData = GameMgr.GetIns().GetDataMgr().GetCharaPrmByCharaId(charaId);
            if (charaData == null)
            {
                Plugin.Logger.LogWarning($"[Offlinizer] Story character {charaId} was not found; using the combined chapter list.");
                return null;
            }

            return charaData.class_id;
        }

        private static int GetDefaultCharacterId(int classId)
        {
            try
            {
                return GameMgr.GetIns().GetDataMgr().GetCharaPrmByClassId(classId, false)?.chara_id ?? 0;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    $"[Offlinizer] Could not resolve the default character for story class {classId}: {ex.Message}");
                return 0;
            }
        }

        private static int CreateStoryId(int sectionId, int classId, string chapterId)
        {
            int row = GetChapterRow(chapterId);
            int suffix = GetChapterSuffixIndex(chapterId);
            return checked(sectionId * 100000 + classId * 10000 + row * 100 + suffix * 10);
        }

        private static string GetNextChapterId(IReadOnlyList<ScenarioChapter> chapters, int currentIndex)
        {
            ScenarioChapter current = chapters[currentIndex];
            int currentRow = GetChapterRow(current.ChapterId);
            int nextRow = chapters
                .Select(chapter => GetChapterRow(chapter.ChapterId))
                .Where(row => row > currentRow)
                .DefaultIfEmpty(0)
                .Min();
            if (nextRow == 0)
            {
                return "0";
            }

            List<ScenarioChapter> nextRowChapters = chapters
                .Where(chapter => GetChapterRow(chapter.ChapterId) == nextRow)
                .ToList();
            string currentSuffix = GetChapterSuffix(current.ChapterId);
            if (!string.IsNullOrEmpty(currentSuffix))
            {
                ScenarioChapter matchingBranch = nextRowChapters.FirstOrDefault(
                    chapter => GetChapterSuffix(chapter.ChapterId) == currentSuffix);
                if (matchingBranch != null)
                {
                    return matchingBranch.ChapterId;
                }
            }

            ScenarioChapter mainRoute = nextRowChapters.FirstOrDefault(
                chapter => string.IsNullOrEmpty(GetChapterSuffix(chapter.ChapterId)));
            return mainRoute?.ChapterId ?? string.Join(" ", nextRowChapters.Select(chapter => chapter.ChapterId).ToArray());
        }

        private static string GetSelectionDisplayPosition(string chapterId)
        {
            int suffix = GetChapterSuffixIndex(chapterId);
            return suffix == 0 ? string.Empty : suffix.ToString(CultureInfo.InvariantCulture);
        }

        private static int GetChapterRow(string chapterId)
        {
            int digitCount = 0;
            while (digitCount < chapterId.Length && char.IsDigit(chapterId[digitCount]))
            {
                digitCount++;
            }

            return int.Parse(chapterId.Substring(0, digitCount), CultureInfo.InvariantCulture);
        }

        private static string GetChapterSuffix(string chapterId)
        {
            int digitCount = 0;
            while (digitCount < chapterId.Length && char.IsDigit(chapterId[digitCount]))
            {
                digitCount++;
            }

            return chapterId.Substring(digitCount);
        }

        private static int GetChapterSuffixIndex(string chapterId)
        {
            string suffix = GetChapterSuffix(chapterId);
            return string.IsNullOrEmpty(suffix) ? 0 : char.ToLowerInvariant(suffix[0]) - 'a' + 1;
        }

        private sealed class StoryManifest
        {
            private readonly List<ScenarioChapter> _chapters;

            private StoryManifest(List<ScenarioChapter> chapters)
            {
                _chapters = chapters;
            }

            internal int ChapterCount => _chapters.Count;

            internal static StoryManifest Load(string manifestPath)
            {
                Dictionary<string, ScenarioChapter> chapters = new Dictionary<string, ScenarioChapter>(StringComparer.Ordinal);
                foreach (string line in File.ReadLines(manifestPath))
                {
                    int commaIndex = line.IndexOf(',');
                    string assetName = commaIndex >= 0 ? line.Substring(0, commaIndex) : line;
                    Match match = ScenarioParamRegex.Match(assetName);
                    if (!match.Success)
                    {
                        continue;
                    }

                    int sectionId = int.Parse(match.Groups["section"].Value, CultureInfo.InvariantCulture);
                    int classId = int.Parse(match.Groups["class"].Value, CultureInfo.InvariantCulture);
                    string chapterId = match.Groups["chapter"].Value;
                    string key = sectionId.ToString(CultureInfo.InvariantCulture) + ":" +
                        classId.ToString(CultureInfo.InvariantCulture) + ":" + chapterId;
                    if (!chapters.TryGetValue(key, out ScenarioChapter chapter))
                    {
                        chapter = new ScenarioChapter(sectionId, classId, chapterId);
                        chapters.Add(key, chapter);
                    }

                    int part = int.Parse(match.Groups["part"].Value, CultureInfo.InvariantCulture);
                    chapter.AvailableClassIds.Add(classId);
                    if (part == 1)
                    {
                        chapter.HasFirstHalf = true;
                    }
                    else
                    {
                        chapter.HasSecondHalf = true;
                        if (classId != 0)
                        {
                            chapter.BattleClassIds.Add(classId);
                        }
                    }

                    if (match.Groups["subchapter"].Success)
                    {
                        chapter.SubChapterIds.Add(int.Parse(
                            match.Groups["subchapter"].Value,
                            CultureInfo.InvariantCulture));
                    }
                }

                return new StoryManifest(chapters.Values.ToList());
            }

            internal List<int> GetClassIds(int sectionId)
            {
                return _chapters
                    .Where(chapter => chapter.SectionId == sectionId && chapter.ClassId != 0)
                    .Select(chapter => chapter.ClassId)
                    .Distinct()
                    .OrderBy(classId => classId)
                    .ToList();
            }

            internal List<ScenarioChapter> GetChapters(int sectionId, int? selectedClassId)
            {
                IEnumerable<ScenarioChapter> sectionChapters = _chapters
                    .Where(chapter => chapter.SectionId == sectionId);

                if (selectedClassId != null)
                {
                    sectionChapters = sectionChapters.Where(chapter => chapter.ClassId == selectedClassId.Value);
                }
                else
                {
                    sectionChapters = sectionChapters
                        .GroupBy(chapter => chapter.ChapterId, StringComparer.Ordinal)
                        .Select(MergeChapterVariants);
                }

                return sectionChapters
                    .OrderBy(chapter => GetChapterRow(chapter.ChapterId))
                    .ThenBy(chapter => GetChapterSuffixIndex(chapter.ChapterId))
                    .ToList();
            }

            private static ScenarioChapter MergeChapterVariants(IGrouping<string, ScenarioChapter> variants)
            {
                ScenarioChapter preferred = variants.FirstOrDefault(chapter => chapter.ClassId == 0) ?? variants.First();
                ScenarioChapter merged = new ScenarioChapter(preferred.SectionId, preferred.ClassId, preferred.ChapterId);
                foreach (ScenarioChapter variant in variants)
                {
                    merged.MergeFrom(variant);
                }

                return merged;
            }
        }

        private sealed class ScenarioChapter
        {
            internal ScenarioChapter(int sectionId, int classId, string chapterId)
            {
                SectionId = sectionId;
                ClassId = classId;
                ChapterId = chapterId;
            }

            internal int SectionId { get; }
            internal int ClassId { get; }
            internal string ChapterId { get; }
            internal bool HasFirstHalf { get; set; }
            internal bool HasSecondHalf { get; set; }
            internal SortedSet<int> AvailableClassIds { get; } = new SortedSet<int>();
            internal SortedSet<int> BattleClassIds { get; } = new SortedSet<int>();
            internal SortedSet<int> SubChapterIds { get; } = new SortedSet<int>();

            internal void MergeFrom(ScenarioChapter other)
            {
                HasFirstHalf |= other.HasFirstHalf;
                HasSecondHalf |= other.HasSecondHalf;
                AvailableClassIds.UnionWith(other.AvailableClassIds);
                BattleClassIds.UnionWith(other.BattleClassIds);
                SubChapterIds.UnionWith(other.SubChapterIds);
            }
        }
    }
}
