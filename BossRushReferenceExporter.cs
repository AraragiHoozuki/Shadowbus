using HarmonyLib;
using Cute;
using LitJson;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Wizard;

namespace Shadowbus
{
    /// <summary>
    /// Copies the text-only AI masters that are useful when authoring a local
    /// BossRush package. No scene, UI, audio or texture asset is written.
    /// </summary>
    public static class BossRushReferenceExporter
    {
        private static bool _exported;

        [HarmonyPatch(typeof(Master), nameof(Master.StartLoadAIIndividualData))]
        [HarmonyPostfix]
        private static void MasterLoaded_Postfix()
        {
            Export();
        }

        public static void Export()
        {
            if (_exported)
            {
                return;
            }

            try
            {
                string root = PathHelper.BossRushReferencePath;
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(Path.Combine(root, "deck"));
                Directory.CreateDirectory(Path.Combine(root, "style"));
                Directory.CreateDirectory(Path.Combine(root, "emote"));

                bool hasAiMaster = Data.Master != null &&
                    Data.Master.QuestAISettingList != null &&
                    Data.Master.AIDeckFileNameList != null &&
                    Data.Master.AIStyleFileNameList != null &&
                    Data.Master.AIEmoteFileNameList != null;

                if (!hasAiMaster)
                {
                    Plugin.Logger.LogWarning("[BossRush] AI master data is not loaded yet; reference export will be completed when it becomes available.");
                    return;
                }

                CopyMasterText("ai/quest_ai_setting", Path.Combine(root, "quest_ai_setting.csv"));
                CopyMasterText("ai/ai_deck_filelist", Path.Combine(root, "ai_deck_filelist.csv"));
                CopyMasterText("ai/ai_style_filelist", Path.Combine(root, "ai_style_filelist.csv"));
                CopyMasterText("ai/ai_emote_filelist", Path.Combine(root, "ai_emote_filelist.csv"));
                int characterCount = ExportCharacterMap(Path.Combine(root, "enemy_chara_ids.csv"));

                int deckCount = 0;
                int styleCount = 0;
                int emoteCount = 0;
                foreach (string fileName in Data.Master.AIDeckFileNameList?.GetFileNameList() ?? new List<string>())
                {
                    if (CopyMasterText("ai/" + fileName, Path.Combine(root, "deck", CsvFileName(fileName)))) deckCount++;
                }
                foreach (string fileName in Data.Master.AIStyleFileNameList?.GetFileNameList() ?? new List<string>())
                {
                    if (string.IsNullOrEmpty(fileName)) continue;
                    if (CopyMasterText("ai/" + fileName, Path.Combine(root, "style", CsvFileName(fileName)))) styleCount++;
                }
                foreach (string fileName in Data.Master.AIEmoteFileNameList?.GetFileNameList() ?? new List<string>())
                {
                    if (string.IsNullOrEmpty(fileName)) continue;
                    if (CopyMasterText("ai/" + fileName, Path.Combine(root, "emote", CsvFileName(fileName)))) emoteCount++;
                }

                var settings = Data.Master.QuestAISettingList?.GetSettingDataTable() ?? new List<StoryAISettingData>();
                File.WriteAllText(Path.Combine(root, "manifest.json"), JsonConvert.SerializeObject(new
                {
                    exported_at_utc = DateTime.UtcNow.ToString("o"),
                    source = "runtime TextAsset master data",
                    quest_ai_settings = settings.Count,
                    deck_files = deckCount,
                    style_files = styleCount,
                    emote_files = emoteCount,
                    enemy_characters = characterCount,
                    entries = settings.Select(setting => new
                    {
                        enemy_ai_id = setting.EnemyAiId,
                        deck_id = setting.DeckId,
                        style_id = setting.StyleId,
                        emote_id = setting.EmoteId,
                        logic_level = setting.LogicLevel,
                        use_inner_emote = setting.UseInnerEmote
                    }).ToList()
                }, Formatting.Indented));
                _exported = true;
                Plugin.Logger.LogInfo($"[BossRush] Reference export completed: deck={deckCount}, style={styleCount}, emote={emoteCount}, characters={characterCount}.");
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Reference AI export failed: {exception.Message}");
            }
        }

        /// <summary>
        /// Saves a raw BossRush response when the game is temporarily allowed to
        /// contact the original service. AI master files do not contain the
        /// BossRush opponent/ability table, so this is the only reliable source
        /// for the complete legacy event definition.
        /// </summary>
        public static void CaptureResponse(string taskName, JsonData response)
        {
            if (string.IsNullOrEmpty(taskName) || response == null ||
                (taskName != nameof(QuestInfoTask) &&
                 taskName != nameof(BossRushLobbyInfoTask) &&
                 taskName != nameof(BossRushHiddenBattleStartTask)))
            {
                return;
            }

            try
            {
                string root = PathHelper.BossRushReferencePath;
                Directory.CreateDirectory(root);
                WriteJsonData(Path.Combine(root, taskName + ".raw.json"), response);

                if (taskName == nameof(BossRushLobbyInfoTask))
                {
                    ExportLobbyReference(response);
                }
                else if (taskName == nameof(BossRushHiddenBattleStartTask))
                {
                    ExportHiddenBossReference(response);
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not capture {taskName}: {exception.Message}");
            }
        }

        private static void ExportLobbyReference(JsonData response)
        {
            JsonData data;
            if (!TryGet(response, "data", out data)) return;

            BossRushPackage package = new BossRushPackage
            {
                Id = "reference",
                DisplayName = "BossRush (Captured from server)",
                DefaultPlayerLife = GetInt(data, "max_life", 20),
                InitialProgress = GetInt(data, "bossrush_progress", 0),
                Abilities = new List<BossRushAbility>(),
                Bosses = new List<BossRushBoss>()
            };

            JsonData opponents;
            if (TryGet(data, "bossrush_opponent_list", out opponents) && opponents.IsArray)
            {
                for (int index = 0; index < opponents.Count; index++)
                {
                    JsonData item = opponents[index];
                    package.Bosses.Add(new BossRushBoss
                    {
                        Name = GetString(item, "name", "Boss " + (index + 1)),
                        EnemyClass = GetInt(item, "enemy_class", 1),
                        EnemyCharaId = GetInt(item, "enemy_chara_id", 1),
                        EnemyEmblemId = GetLong(item, "enemy_emblem_id", 0),
                        EnemyDegreeId = GetLong(item, "enemy_degree_id", 0),
                        BossrushStageId = GetInt(item, "bossrush_stage_id", 1),
                        Battle3dfieldId = GetInt(item, "battle3dfield_id", 1),
                        BgmId = GetString(item, "bgm_id", string.Empty),
                        EnemyLife = GetInt(item, "enemy_life", 20),
                        RecoveryPoint = GetInt(item, "recovery_point", 0),
                        EnemySkill = GetString(item, "enemy_skill", string.Empty),
                        EnemySkillDesc = GetString(item, "enemy_skill_desc", string.Empty),
                        EnemyAiId = GetInt(item, "enemy_ai_id", 1)
                    });
                }
            }

            AddAbilities(package, data, "special_ability_list");
            AddAbilities(package, data, "special_ability_candidate_list");
            WriteJsonObject(Path.Combine(PathHelper.BossRushReferencePath, "bossrush.reference.json"), package);
            Plugin.Logger.LogInfo($"[BossRush] Full BossRush reference exported: bosses={package.Bosses.Count}, abilities={package.Abilities.Count}.");
        }

        private static void ExportHiddenBossReference(JsonData response)
        {
            JsonData data;
            JsonData hidden;
            if (!TryGet(response, "data", out data) || !TryGet(data, "hidden_boss_info", out hidden)) return;
            string path = Path.Combine(PathHelper.BossRushReferencePath, "bossrush.reference.json");
            if (!File.Exists(path)) return;

            BossRushPackage package = JsonConvert.DeserializeObject<BossRushPackage>(File.ReadAllText(path));
            package.HiddenBoss = new BossRushBoss
            {
                Name = GetString(hidden, "name", "Hidden Boss"),
                EnemyClass = GetInt(hidden, "enemy_class", 1),
                EnemyCharaId = GetInt(hidden, "enemy_chara_id", GetInt(hidden, "texture_id", 1)),
                EnemyEmblemId = GetLong(hidden, "enemy_emblem_id", 0),
                EnemyDegreeId = GetLong(hidden, "enemy_degree_id", 0),
                BossrushStageId = GetInt(hidden, "quest_stage_id", 1),
                Battle3dfieldId = GetInt(hidden, "battle3dfield_id", 1),
                BgmId = GetString(hidden, "bgm_id", string.Empty),
                EnemyLife = GetInt(hidden, "enemy_life", 20),
                RecoveryPoint = GetInt(hidden, "recovery_point", 0),
                EnemySkill = GetString(hidden, "enemy_skill", string.Empty),
                EnemySkillDesc = GetString(hidden, "enemy_skill_desc", string.Empty),
                EnemyAiId = GetInt(hidden, "enemy_ai_id", 1)
            };
            WriteJsonObject(path, package);
            Plugin.Logger.LogInfo("[BossRush] Hidden Boss appended to bossrush.reference.json.");
        }

        private static void AddAbilities(BossRushPackage package, JsonData data, string key)
        {
            JsonData list;
            if (!TryGet(data, key, out list) || !list.IsArray) return;
            for (int index = 0; index < list.Count; index++)
            {
                JsonData item = list[index];
                int id = GetInt(item, "ability_id", 0);
                if (id <= 0 || package.Abilities.Any(value => value.AbilityId == id)) continue;
                package.Abilities.Add(new BossRushAbility
                {
                    AbilityId = id,
                    IsFoil = GetBool(item, "is_foil", false),
                    Skill = GetString(item, "skill", string.Empty),
                    SpecialAbilityDesc = GetString(item, "special_ability_desc", string.Empty)
                });
            }
        }

        private static bool TryGet(JsonData value, string key, out JsonData result)
        {
            result = null;
            return value != null && value.IsObject && value.TryGetValue(key, out result) && result != null;
        }

        private static string GetString(JsonData value, string key, string fallback)
        {
            JsonData item;
            return TryGet(value, key, out item) ? item.ToString() : fallback;
        }

        private static int GetInt(JsonData value, string key, int fallback)
        {
            JsonData item;
            try { return TryGet(value, key, out item) ? item.ToInt() : fallback; }
            catch { return fallback; }
        }

        private static long GetLong(JsonData value, string key, long fallback)
        {
            JsonData item;
            try { return TryGet(value, key, out item) ? item.ToLong() : fallback; }
            catch { return fallback; }
        }

        private static bool GetBool(JsonData value, string key, bool fallback)
        {
            JsonData item;
            try { return TryGet(value, key, out item) ? item.ToBoolean() : fallback; }
            catch { return fallback; }
        }

        private static void WriteJsonData(string path, JsonData value)
        {
            string json = JsonMapper.ToJson(value);
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(JsonConvert.DeserializeObject(json), Formatting.Indented));
            }
            catch
            {
                File.WriteAllText(path, json);
            }
        }

        private static void WriteJsonObject(string path, object value)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
        }

        private static bool CopyMasterText(string logicalName, string destination)
        {
            try
            {
                string assetPath = Toolbox.ResourcesManager.GetAssetTypePath(logicalName, ResourcesManager.AssetLoadPathType.Master, true);
                TextAsset asset = Toolbox.ResourcesManager.LoadObject(assetPath, true, false) as TextAsset;
                if (asset == null) return false;
                File.WriteAllText(destination, asset.text ?? asset.ToString());
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not export '{logicalName}': {exception.Message}");
                return false;
            }
        }

        private static int ExportCharacterMap(string destination)
        {
            List<ClassCharacterMasterData> characters = Data.Master?.ClassCharacterList;
            if (characters == null || characters.Count == 0)
            {
                return 0;
            }

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("enemy_chara_id,chara_name,enemy_class,class_name,skin_id,path,is_usable,is_3d");
            foreach (ClassCharacterMasterData character in characters.OrderBy(item => item.chara_id))
            {
                csv.Append(character.chara_id).Append(',')
                    .Append(EscapeCsv(character.chara_name)).Append(',')
                    .Append(character.class_id).Append(',')
                    .Append(EscapeCsv(character._className)).Append(',')
                    .Append(character.skin_id).Append(',')
                    .Append(EscapeCsv(character.path)).Append(',')
                    .Append(character.is_usable ? 1 : 0).Append(',')
                    .Append(character.Is3d ? 1 : 0).AppendLine();
            }
            File.WriteAllText(destination, csv.ToString(), new UTF8Encoding(false));
            return characters.Count;
        }

        private static string EscapeCsv(string value)
        {
            string text = value ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return text;
            }
            return '"' + text.Replace("\"", "\"\"") + '"';
        }

        private static string SafeFileName(string name)
        {
            string value = name.Replace('/', '_').Replace('\\', '_');
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static string CsvFileName(string name)
        {
            string safe = SafeFileName(name);
            return safe.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? safe : safe + ".csv";
        }
    }
}
