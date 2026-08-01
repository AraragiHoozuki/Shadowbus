using LitJson;
using Newtonsoft.Json;
using Cute;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wizard;

namespace Shadowbus
{
    /// <summary>
    /// Local data provider for the legacy BossRush protocol. The game still owns
    /// all presentation and battle code; this class only supplies the responses
    /// that used to come from the service.
    /// </summary>
    public static class BossRushOfflineData
    {
        private static readonly object Sync = new object();
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private static readonly List<BossRushPackage> Packages = new List<BossRushPackage>();
        private static readonly Dictionary<string, BossRushState> States = new Dictionary<string, BossRushState>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> PackageDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static BossRushPackage _current;
        private static bool _hiddenBattleActive;

        public static BossRushPackage CurrentPackage => _current;
        public static IReadOnlyList<BossRushPackage> AvailablePackages => Packages;
        public static bool IsActive => _current != null;
        public static bool IsHiddenBattleActive => _hiddenBattleActive;

        public static string GetLobbyBackgroundName()
        {
            if (_current == null)
            {
                return "bg_gp_special_01";
            }

            if (!string.IsNullOrWhiteSpace(_current.LobbyBackground))
            {
                return _current.LobbyBackground.Trim();
            }

            switch ((_current.UiTheme ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "classic": return "bg_boss_rush";
                case "grand_prix_2": return "bg_gp_special_02";
                case "colosseum_1": return "bg_colosseum_01";
                case "colosseum_2": return "bg_colosseum_02";
                case "two_pick": return "bg_2pick";
                case "quest": return "bg_quest";
                case "grand_prix_1":
                default:
                    return "bg_gp_special_01";
            }
        }

        public static void Initialize()
        {
            lock (Sync)
            {
                Directory.CreateDirectory(PathHelper.BossRushPath);
                Directory.CreateDirectory(PathHelper.BossRushStatePath);
                Packages.Clear();
                PackageDirectories.Clear();
                EnsureDefaultPackage();

                foreach (string directory in Directory.GetDirectories(PathHelper.BossRushPath))
                {
                    string name = Path.GetFileName(directory);
                    if (string.Equals(name, "Reference", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "State", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string configPath = Path.Combine(directory, "bossrush.json");
                    if (!File.Exists(configPath))
                    {
                        continue;
                    }

                    try
                    {
                        BossRushPackage package = JsonConvert.DeserializeObject<BossRushPackage>(File.ReadAllText(configPath), JsonSettings);
                        ValidatePackage(package, configPath);
                        if (Packages.Any(existing => string.Equals(existing.Id, package.Id, StringComparison.OrdinalIgnoreCase)))
                        {
                            Plugin.Logger.LogWarning($"[BossRush] Duplicate config id '{package.Id}', skipping '{configPath}'.");
                            continue;
                        }

                        package.Normalize();
                        Packages.Add(package);
                        PackageDirectories[package.Id] = directory;
                    }
                    catch (Exception exception)
                    {
                        Plugin.Logger.LogError($"[BossRush] Invalid config '{configPath}': {exception.Message}");
                    }
                }

                Packages.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
                if (Packages.Count > 0)
                {
                    string last = LoadLastSelectedId();
                    _current = Packages.FirstOrDefault(package => string.Equals(package.Id, last, StringComparison.OrdinalIgnoreCase)) ?? Packages[0];
                }
            }
        }

        public static void SelectPackage(string id)
        {
            lock (Sync)
            {
                BossRushPackage package = Packages.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
                if (package == null)
                {
                    return;
                }

                _current = package;
                File.WriteAllText(Path.Combine(PathHelper.BossRushPath, "selected.txt"), package.Id);
                GetState(package);
            }
        }

        public static BossRushState GetState(BossRushPackage package = null)
        {
            package = package ?? _current;
            if (package == null)
            {
                return null;
            }

            lock (Sync)
            {
                BossRushState state;
                if (!States.TryGetValue(package.Id, out state))
                {
                    string path = Path.Combine(PathHelper.BossRushStatePath, package.Id + ".json");
                    state = File.Exists(path)
                        ? JsonConvert.DeserializeObject<BossRushState>(File.ReadAllText(path), JsonSettings)
                        : null;
                    state = state ?? BossRushState.Create(package);
                    state.Normalize(package);
                    States[package.Id] = state;
                }

                return state;
            }
        }

        public static void CaptureDeck(DeckData deck)
        {
            if (_current == null || deck == null)
            {
                return;
            }

            BossRushState state = GetState();
            state.DeckNo = deck.GetDeckID();
            state.DeckClassId = deck.GetDeckClassID();
            state.DeckFormat = (int)deck.Format;
            state.DeckName = deck.GetDeckName() ?? "BossRush Deck";
            state.PlayerDeckCardIds = (deck.GetCardIdList() ?? new List<int>()).ToList();
            SaveState(_current, state);
        }

        public static void CaptureAbility(int abilityId, bool isFoil, int maxLifeChange, int lifeChange)
        {
            if (_current == null)
            {
                return;
            }

            BossRushState state = GetState();
            BossRushAbility configured = _current.Abilities.FirstOrDefault(item => item.AbilityId == abilityId);
            if (configured == null)
            {
                Plugin.Logger.LogWarning($"[BossRush] Ignoring ability {abilityId} because it is not in the current package.");
                return;
            }
            if (configured != null && maxLifeChange == 0 && lifeChange == 0)
            {
                maxLifeChange = configured.MaxLifeChange;
                lifeChange = configured.LifeChange;
            }

            state.SelectedAbilities.Add(new BossRushSelectedAbility
            {
                AbilityId = abilityId,
                IsFoil = isFoil,
                MaxLifeChange = maxLifeChange,
                LifeChange = lifeChange
            });
            state.MaxLife = Math.Max(1, state.MaxLife + maxLifeChange);
            state.CurrentLife = Math.Max(0, Math.Min(state.MaxLife, state.CurrentLife + lifeChange));
            state.AbilityCandidateIds.Clear();
            state.AbilityCandidateSelection = -1;
            SaveState(_current, state);
        }

        public static void CaptureFinish(bool isWin, int currentLife, int maxLife, int totalTurn)
        {
            if (_current == null)
            {
                return;
            }

            BossRushState state = GetState();
            state.CurrentLife = Math.Max(0, currentLife);
            state.MaxLife = Math.Max(1, maxLife);
            state.TotalTurns = Math.Max(state.TotalTurns, totalTurn);
            if (isWin)
            {
                BossRushBoss defeatedBoss = _current.Bosses.Count == 0
                    ? null
                    : _current.Bosses[Math.Max(0, Math.Min(state.Progress, _current.Bosses.Count - 1))];
                if (defeatedBoss != null && defeatedBoss.RecoveryPoint > 0)
                {
                    state.CurrentLife = Math.Min(state.MaxLife, state.CurrentLife + defeatedBoss.RecoveryPoint);
                }
                state.BestTurns = state.BestTurns == 0 ? totalTurn : Math.Min(state.BestTurns, totalTurn);
                state.IsLose = false;
                if (_current.Bosses.Count > 0 && state.Progress >= _current.Bosses.Count - 1)
                {
                    state.IsFinished = true;
                    state.HiddenBossUnlocked = _current.HiddenBoss != null;
                }
                else
                {
                    state.Progress = Math.Min(state.Progress + 1, Math.Max(0, _current.Bosses.Count - 1));
                }
            }
            else
            {
                state.IsLose = true;
            }
            SaveState(_current, state);
        }

        public static void CaptureHiddenFinish(bool isWin, int totalTurn)
        {
            if (_current == null) return;
            BossRushState state = GetState();
            state.HiddenBossFinished = isWin;
            if (isWin && totalTurn > 0)
            {
                state.BestTurns = state.BestTurns == 0 ? totalTurn : Math.Min(state.BestTurns, totalTurn);
            }
            SaveState(_current, state);
            _hiddenBattleActive = false;
        }

        public static void ClearRun(bool resetProgress, bool clearDeck = false)
        {
            if (_current == null)
            {
                return;
            }

            BossRushState state = GetState();
            state.IsLose = false;
            state.SelectedAbilities.Clear();
            if (resetProgress)
            {
                state.MaxLife = _current.DefaultPlayerLife;
                state.CurrentLife = state.MaxLife;
                state.Progress = 0;
                state.TotalTurns = 0;
                state.IsFinished = false;
                state.HiddenBossUnlocked = false;
                state.HiddenBossFinished = false;
            }
            else
            {
                state.CurrentLife = state.MaxLife;
            }
            state.AbilityCandidateIds.Clear();
            state.AbilityCandidateSelection = -1;
            if (clearDeck)
            {
                state.DeckNo = 0;
                state.DeckClassId = 1;
                state.DeckFormat = 0;
                state.DeckName = null;
                state.PlayerDeckCardIds.Clear();
            }
            SaveState(_current, state);
        }

        public static bool TryCreateResponse(NetworkTask task, out JsonData response)
        {
            response = null;
            if (_current == null || task == null)
            {
                return false;
            }

            string name = task.GetType().Name;
            if (task is QuestInfoTask)
            {
                response = CreateResponse(CreateQuestInfoData());
            }
            else if (task is BossRushLobbyInfoTask)
            {
                response = CreateResponse(CreateLobbyData());
            }
            else if (task is BossRushClearDeckListTask)
            {
                response = CreateResponse(CreateHiddenDeckListData());
            }
            else if (task is QuestBossRushRegisterDeckTask || task is QuestBossRushSetAbilityTask || task is BossRushStartTask)
            {
                JsonData data = new JsonData();
                data["mission_parameter"] = CreateMissionParameter(GetCurrentBoss());
                response = CreateResponse(data);
            }
            else if (task is BossRushFinishTask || task is BossRushRetireTask || task is BossRushLoseFinishTask || task is BossRushHiddenBattleFinishTask)
            {
                response = CreateResponse(CreateFinishData());
            }
            else if (task is BossRushReceiveRewardTask)
            {
                response = CreateResponse(CreateRewardData());
            }
            else if (task is BossRushHiddenBattleStartTask)
            {
                _hiddenBattleActive = true;
                response = CreateResponse(CreateHiddenStartData());
            }
            else
            {
                return false;
            }

            Plugin.Logger.LogInfo($"[BossRush] Local response generated for {name} ({_current.Id}).");
            return true;
        }

        public static string ResolveAiPath(BossRushBoss boss, string kind)
        {
            if (boss == null || string.IsNullOrEmpty(kind))
            {
                return null;
            }
            string value = kind == "deck" ? boss.DeckCsv : kind == "style" ? boss.StyleCsv : boss.EmoteCsv;
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            if (Path.IsPathRooted(value))
            {
                return value;
            }
            string directory;
            return PackageDirectories.TryGetValue(_current.Id, out directory) ? Path.GetFullPath(Path.Combine(directory, value)) : null;
        }

        public static BossRushBoss GetCurrentBoss()
        {
            if (_current == null)
            {
                return null;
            }
            if (_hiddenBattleActive)
            {
                return _current.HiddenBoss;
            }
            BossRushState state = GetState();
            int index = Math.Max(0, Math.Min(state.Progress, _current.Bosses.Count - 1));
            return _current.Bosses.Count == 0 ? null : _current.Bosses[index];
        }

        private static JsonData CreateQuestInfoData()
        {
            BossRushState state = GetState();
            JsonData data = new JsonData();
            data["start_time"] = DateTime.UtcNow.AddDays(-1).ToString("o");
            data["end_time"] = DateTime.UtcNow.AddYears(10).ToString("o");
            data["is_last_day"] = false;
            data["is_open_extra"] = false;
            data["opponent_list"] = NewArray();
            data["unreceived_reward_count"] = 0;
            data["is_display_badge"] = false;
            data["is_display_tweet_reward_banner"] = false;
            data["announce_id"] = string.Empty;
            data["quest_id"] = 0;

            JsonData info = new JsonData();
            info["is_finished_quest_battle"] = true;
            info["bossrush_progress"] = state.Progress;
            info["is_received_bossrush_reward"] = true;
            info["is_first_challenge"] = state.Progress == 0 && !state.IsFinished;
            info["is_max_challenge"] = state.IsFinished;
            info["is_deck_registered"] = state.PlayerDeckCardIds != null && state.PlayerDeckCardIds.Count > 0;
            info["shortest_clear_turns"] = state.BestTurns;
            info["shortest_clear_class"] = state.DeckClassId;
            info["is_hidden_boss_playable"] = state.HiddenBossUnlocked;
            info["is_win_hidden_boss"] = state.HiddenBossFinished ? 1 : 0;
            if (_current.HiddenBoss != null)
            {
                info["hidden_boss_character_info"] = new JsonData();
                info["hidden_boss_character_info"]["texture_id"] = _current.HiddenBoss.EnemyCharaId;
            }
            data["bossrush_info"] = info;
            return data;
        }

        private static JsonData CreateLobbyData()
        {
            BossRushState state = GetState();
            int progress = _current.Bosses.Count == 0 ? 0 : Math.Min(Math.Max(0, state.Progress), _current.Bosses.Count - 1);
            JsonData data = new JsonData();
            data["bossrush_progress"] = progress;
            data["is_lose"] = state.IsLose;
            bool hasAbility = HasAbilityForCurrentBattle(state);
            data["is_finished_special_ability_select"] = hasAbility;
            data["bossrush_opponent_list"] = CreateBossList(state);
            data["special_ability_list"] = CreateSelectedAbilities(state);
            data["special_ability_candidate_list"] = hasAbility ? NewArray() : CreateAbilityCandidates(state);
            data["user_bossrush_deck"] = NewArray();
            data["user_bossrush_deck"].Add(CreateDeckData(state));
            data["current_life"] = Math.Max(0, state.CurrentLife);
            data["max_life"] = Math.Max(1, state.MaxLife);
            data["total_turns"] = Math.Min(9999, state.TotalTurns);
            data["reward_info"] = NewArray();
            data["reward_list"] = NewArray();
            data["rewards"] = NewArray();
            data["hidden_boss_reward_list"] = NewArray();
            data["reward_grade"] = 0;
            data["is_received_all_rewards"] = true;
            data["announce_id"] = string.Empty;
            return data;
        }

        private static JsonData CreateBossList(BossRushState state)
        {
            JsonData list = NewArray();
            for (int index = 0; index < _current.Bosses.Count; index++)
            {
                BossRushBoss boss = _current.Bosses[index];
                JsonData value = new JsonData();
                value["name"] = boss.Name;
                value["enemy_class"] = boss.EnemyClass;
                value["enemy_chara_id"] = boss.EnemyCharaId;
                value["enemy_emblem_id"] = boss.EnemyEmblemId;
                value["enemy_degree_id"] = boss.EnemyDegreeId;
                value["enemy_ai_id"] = ResolveAiId(boss.EnemyAiId);
                value["bossrush_stage_id"] = boss.BossrushStageId;
                value["battle3dfield_id"] = boss.Battle3dfieldId;
                value["bgm_id"] = boss.BgmId ?? string.Empty;
                value["enemy_life"] = Math.Max(1, boss.EnemyLife);
                value["enemy_skill"] = CreateEnemySkill(boss);
                value["enemy_skill_desc"] = boss.EnemySkillDesc ?? string.Empty;
                value["recovery_point"] = boss.RecoveryPoint;
                value["is_clear_battle"] = index < state.Progress || state.IsFinished;
                list.Add(value);
            }
            return list;
        }

        private static JsonData CreateSelectedAbilities(BossRushState state, BossRushBoss battleBoss = null)
        {
            JsonData list = NewArray();
            foreach (BossRushSelectedAbility selected in state.SelectedAbilities)
            {
                BossRushAbility ability = _current.Abilities.FirstOrDefault(item => item.AbilityId == selected.AbilityId);
                if (ability == null || !IsAbilityAvailable(ability.AbilityId)) continue;
                list.Add(CreateAbilityData(ability, selected.IsFoil));
            }
            AppendPlayerStartFieldSkills(list, battleBoss ?? GetCurrentBoss());
            return list;
        }

        private static void AppendPlayerStartFieldSkills(JsonData list, BossRushBoss boss)
        {
            if (boss?.PlayerStartFieldCardIds == null) return;
            foreach (int cardId in boss.PlayerStartFieldCardIds.Take(5))
            {
                if (!IsAbilityAvailable(cardId)) continue;
                JsonData value = new JsonData();
                value["ability_id"] = cardId;
                value["is_foil"] = false;
                value["skill"] = CreateStartFieldSkill(cardId);
                value["special_ability_desc"] = $"Start this battle with card {cardId} in play.";
                list.Add(value);
            }
        }

        private static JsonData CreateAbilityCandidates(BossRushState state)
        {
            JsonData list = NewArray();
            List<BossRushAbility> available = GetAvailableAbilities();
            if (available.Count == 0)
            {
                return list;
            }

            bool pendingIsValid = state.AbilityCandidateSelection == state.SelectedAbilities.Count &&
                state.AbilityCandidateIds.Count > 0 &&
                state.AbilityCandidateIds.All(id => available.Any(ability => ability.AbilityId == id));
            if (!pendingIsValid)
            {
                HashSet<int> acquired = new HashSet<int>(state.SelectedAbilities.Select(item => item.AbilityId));
                List<BossRushAbility> pool = available.Where(ability => !acquired.Contains(ability.AbilityId)).ToList();
                if (pool.Count == 0)
                {
                    pool = available;
                }

                state.AbilityCandidateIds = pool
                    .OrderBy(_ => Guid.NewGuid())
                    .Take(3)
                    .Select(ability => ability.AbilityId)
                    .ToList();
                state.AbilityCandidateSelection = state.SelectedAbilities.Count;
                SaveState(_current, state);
            }

            foreach (int abilityId in state.AbilityCandidateIds)
            {
                BossRushAbility ability = available.FirstOrDefault(item => item.AbilityId == abilityId);
                if (ability != null)
                {
                    list.Add(CreateAbilityData(ability, ability.IsFoil));
                }
            }
            return list;
        }

        private static bool HasAbilityForCurrentBattle(BossRushState state)
        {
            if (state.IsFinished || state.IsLose || GetAvailableAbilities().Count == 0)
            {
                return true;
            }

            int requiredSelections = Math.Min(state.Progress + 1, _current.Bosses.Count);
            return state.SelectedAbilities.Count >= requiredSelections;
        }

        private static List<BossRushAbility> GetAvailableAbilities()
        {
            return (_current?.Abilities ?? new List<BossRushAbility>())
                .Where(ability => ability != null && IsAbilityAvailable(ability.AbilityId))
                .GroupBy(ability => ability.AbilityId)
                .Select(group => group.First())
                .ToList();
        }

        private static JsonData CreateAbilityData(BossRushAbility ability, bool foil)
        {
            JsonData value = new JsonData();
            value["ability_id"] = ability.AbilityId;
            value["is_foil"] = foil;
            value["skill"] = ability.Skill ?? string.Empty;
            value["special_ability_desc"] = ability.SpecialAbilityDesc ?? string.Empty;
            return value;
        }

        private static string CreateEnemySkill(BossRushBoss boss)
        {
            List<string> skills = new List<string>();
            if (!string.IsNullOrWhiteSpace(boss?.EnemySkill)) skills.Add(boss.EnemySkill.Trim().Trim(','));
            if (boss?.EnemySkills != null)
            {
                skills.AddRange(boss.EnemySkills
                    .Where(skill => !string.IsNullOrWhiteSpace(skill))
                    .Select(skill => skill.Trim().Trim(',')));
            }
            if (boss?.EnemyStartFieldCardIds != null)
            {
                foreach (int cardId in boss.EnemyStartFieldCardIds.Take(5))
                {
                    if (IsAbilityAvailable(cardId)) skills.Add(CreateStartFieldSkill(cardId));
                }
            }
            return string.Join(",", skills.Where(skill => !string.IsNullOrWhiteSpace(skill)).ToArray());
        }

        private static string CreateStartFieldSkill(int cardId)
        {
            return $"(skill:summon_token)(timing:when_battle_start)(condition:character=me)(target:none)(option:summon_token={cardId})(preprocess:remove_after_action=(count=1))";
        }

        private static bool IsAbilityAvailable(int abilityId)
        {
            if (abilityId <= 0) return false;
            try
            {
                return CardMaster.GetInstance(CardMaster.CardMasterId.Default).GetCardParameterFromId(abilityId) != null;
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Ignoring invalid ability card {abilityId}: {exception.Message}");
                return false;
            }
        }

        private static JsonData CreateDeckData(BossRushState state)
        {
            JsonData value = new JsonData();
            value["deck_no"] = state.DeckNo;
            value["deck_name"] = state.DeckName ?? "BossRush Deck";
            value["deck_format"] = state.DeckFormat;
            value["format"] = state.DeckFormat;
            value["current_format"] = state.DeckFormat;
            value["class_id"] = state.DeckClassId <= 0 ? 1 : state.DeckClassId;
            value["sub_class_id"] = 10;
            value["is_complete_deck"] = true;
            value["is_include_un_possession_card"] = false;
            value["sleeve_id"] = 3000011L;
            value["card_id_array"] = NewArray();
            foreach (int cardId in state.PlayerDeckCardIds ?? new List<int>()) value["card_id_array"].Add(cardId);
            value["card_id_list"] = value["card_id_array"];
            return value;
        }

        private static JsonData CreateHiddenDeckListData()
        {
            JsonData list = NewArray();
            BossRushState state = GetState();
            JsonData deck = CreateDeckData(state);
            deck["challenge_count_num"] = state.DeckNo;
            deck["special_ability_list"] = CreateSelectedAbilities(state, _current.HiddenBoss);
            list.Add(deck);
            return list;
        }

        private static JsonData CreateFinishData()
        {
            JsonData data = new JsonData();
            data["get_class_experience"] = 0;
            data["class_experience"] = 0;
            data["class_level"] = 1;
            data["current_point"] = 0;
            data["add_point"] = 0;
            data["class_bonus_point"] = 0;
            data["format_bonus_point"] = 0;
            data["point_reward_list"] = NewArray();
            data["current_life"] = GetState().CurrentLife;
            data["max_life"] = GetState().MaxLife;
            data["shortest_clear_turns"] = GetState().BestTurns;
            data["total_turns"] = GetState().TotalTurns;
            data["is_special_result"] = false;
            data["is_special_effect"] = false;
            data["achieved_info"] = NewObject();
            data["battle_dialog_list"] = NewArray();
            data["reward_list"] = NewArray();
            data["rewards"] = NewArray();
            data["hidden_boss_reward_list"] = NewArray();
            data["reward_info"] = NewArray();
            return data;
        }

        private static JsonData CreateRewardData()
        {
            JsonData data = new JsonData();
            data["rewards"] = NewArray();
            data["reward_list"] = NewArray();
            data["hidden_boss_reward_list"] = NewArray();
            data["is_received_all_rewards"] = true;
            data["is_play_hidden_boss_appear_animation"] = false;
            return data;
        }

        private static JsonData CreateHiddenStartData()
        {
            JsonData data = new JsonData();
            if (_current.HiddenBoss != null)
            {
                BossRushBoss boss = _current.HiddenBoss;
                data["hidden_boss_info"] = BossToQuestData(boss);
            }
            data["mission_parameter"] = CreateMissionParameter(_current.HiddenBoss);
            return data;
        }

        private static JsonData CreateMissionParameter(BossRushBoss boss)
        {
            JsonData data = NewObject();
            if (boss?.MissionParameter == null)
            {
                return data;
            }

            foreach (KeyValuePair<string, string> item in boss.MissionParameter)
            {
                if (!string.IsNullOrWhiteSpace(item.Key))
                {
                    data[item.Key] = item.Value ?? string.Empty;
                }
            }
            return data;
        }

        private static JsonData BossToQuestData(BossRushBoss boss)
        {
            JsonData data = new JsonData();
            data["name"] = boss.Name;
            data["enemy_class"] = boss.EnemyClass;
            data["enemy_chara_id"] = boss.EnemyCharaId;
            data["texture_id"] = boss.EnemyCharaId;
            data["enemy_emblem_id"] = boss.EnemyEmblemId;
            data["enemy_degree_id"] = boss.EnemyDegreeId;
            data["enemy_ai_id"] = ResolveAiId(boss.EnemyAiId);
            data["enemy_life"] = boss.EnemyLife;
            data["battle3dfield_id"] = boss.Battle3dfieldId;
            data["bgm_id"] = boss.BgmId ?? string.Empty;
            data["quest_stage_id"] = boss.BossrushStageId;
            data["enemy_skill"] = CreateEnemySkill(boss);
            data["enemy_skill_desc"] = boss.EnemySkillDesc ?? string.Empty;
            data["recovery_point"] = boss.RecoveryPoint;
            return data;
        }

        private static int ResolveAiId(int requested)
        {
            try
            {
                if (Data.Master?.QuestAISettingList != null)
                {
                    Data.Master.QuestAISettingList.GetSettingData(requested);
                    return requested;
                }
            }
            catch
            {
                StoryAISettingData fallback = Data.Master.QuestAISettingList?.GetSettingDataTable()?.FirstOrDefault();
                if (fallback != null)
                {
                    Plugin.Logger.LogWarning($"[BossRush] AI id {requested} is unavailable; using official AI id {fallback.EnemyAiId}.");
                    return fallback.EnemyAiId;
                }
            }
            return requested;
        }

        private static JsonData CreateResponse(JsonData payload)
        {
            JsonData response = new JsonData();
            JsonData headers = new JsonData();
            headers["short_udid"] = 0L;
            headers["viewer_id"] = 0L;
            headers["sid"] = string.Empty;
            headers["servertime"] = 0L;
            headers["result_code"] = 1;
            response["data_headers"] = headers;
            response["data"] = payload;
            response["is_hidden_boss_appeared"] = GetState()?.HiddenBossUnlocked ?? false;
            return response;
        }

        private static JsonData NewArray()
        {
            JsonData value = new JsonData();
            value.SetJsonType(JsonType.Array);
            return value;
        }

        private static JsonData NewObject()
        {
            JsonData value = new JsonData();
            value.SetJsonType(JsonType.Object);
            return value;
        }

        private static void SaveState(BossRushPackage package, BossRushState state)
        {
            lock (Sync)
            {
                state.Normalize(package);
                States[package.Id] = state;
                string path = Path.Combine(PathHelper.BossRushStatePath, package.Id + ".json");
                string temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(state, JsonSettings));
                if (File.Exists(path))
                {
                    try { File.Replace(temporaryPath, path, null); }
                    catch { File.Delete(path); File.Move(temporaryPath, path); }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
        }

        private static void ValidatePackage(BossRushPackage package, string path)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.Id)) throw new InvalidDataException("id is required");
            if (package.Bosses == null || package.Bosses.Count == 0) throw new InvalidDataException("bosses must contain at least one boss");
            if (package.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("id contains invalid path characters");
        }

        private static void EnsureDefaultPackage()
        {
            string directory = Path.Combine(PathHelper.BossRushPath, "default");
            string path = Path.Combine(directory, "bossrush.json");
            bool upgradeLegacyDefault = false;
            bool upgradeGeneratedDefault = false;
            if (File.Exists(path))
            {
                try
                {
                    BossRushPackage existing = JsonConvert.DeserializeObject<BossRushPackage>(File.ReadAllText(path), JsonSettings);
                    upgradeLegacyDefault = existing != null && existing.IsLegacyGeneratedDefault();
                    upgradeGeneratedDefault = existing != null &&
                        (existing.IsGeneratedV2Default() || existing.IsGeneratedV3Default() || existing.IsGeneratedV4Default());
                }
                catch
                {
                    // A user-authored file may be incomplete while it is being
                    // edited. Leave it untouched and let normal validation log it.
                    return;
                }

                if (!upgradeLegacyDefault && !upgradeGeneratedDefault) return;
            }
            Directory.CreateDirectory(Path.Combine(directory, "ai", "deck"));
            Directory.CreateDirectory(Path.Combine(directory, "ai", "style"));
            Directory.CreateDirectory(Path.Combine(directory, "ai", "emote"));
            BossRushPackage package = BossRushPackage.CreateDefault();
            File.WriteAllText(path, JsonConvert.SerializeObject(package, JsonSettings));
            if (upgradeLegacyDefault)
            {
                ResetDefaultStateForUpgrade(package);
                Plugin.Logger.LogInfo("[BossRush] Upgraded the generated default package to the multi-stage sample.");
            }
            else if (upgradeGeneratedDefault)
            {
                Plugin.Logger.LogInfo("[BossRush] Updated the generated default package to the latest configuration schema.");
            }
        }

        private static void ResetDefaultStateForUpgrade(BossRushPackage package)
        {
            string path = Path.Combine(PathHelper.BossRushStatePath, package.Id + ".json");
            if (!File.Exists(path)) return;

            try
            {
                BossRushState state = JsonConvert.DeserializeObject<BossRushState>(File.ReadAllText(path), JsonSettings) ?? BossRushState.Create(package);
                state.Progress = package.InitialProgress;
                state.CurrentLife = package.DefaultPlayerLife;
                state.MaxLife = package.DefaultPlayerLife;
                state.SelectedAbilities = new List<BossRushSelectedAbility>();
                state.AbilityCandidateIds = new List<int>();
                state.AbilityCandidateSelection = -1;
                state.TotalTurns = 0;
                state.IsLose = false;
                state.IsFinished = false;
                state.HiddenBossUnlocked = false;
                state.HiddenBossFinished = false;
                state.Normalize(package);

                string temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(state, JsonSettings));
                if (File.Exists(path))
                {
                    try { File.Replace(temporaryPath, path, null); }
                    catch { File.Delete(path); File.Move(temporaryPath, path); }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not reset legacy default state: {exception.Message}");
            }
        }

        private static string LoadLastSelectedId()
        {
            string path = Path.Combine(PathHelper.BossRushPath, "selected.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
    }

    public sealed class BossRushPackage
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; } = 5;
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("display_name")] public string DisplayName { get; set; }
        [JsonProperty("detail_title")] public string DetailTitle { get; set; }
        [JsonProperty("detail_text")] public string DetailText { get; set; }
        [JsonProperty("ui_theme")] public string UiTheme { get; set; } = "grand_prix_1";
        [JsonProperty("lobby_background")] public string LobbyBackground { get; set; }
        [JsonProperty("default_player_life")] public int DefaultPlayerLife { get; set; } = 20;
        [JsonProperty("initial_progress")] public int InitialProgress { get; set; }
        [JsonProperty("abilities")] public List<BossRushAbility> Abilities { get; set; } = new List<BossRushAbility>();
        [JsonProperty("bosses")] public List<BossRushBoss> Bosses { get; set; } = new List<BossRushBoss>();
        [JsonProperty("hidden_boss")]
        public BossRushBoss HiddenBoss { get; set; }

        public void Normalize()
        {
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
            DetailTitle = string.IsNullOrWhiteSpace(DetailTitle) ? DisplayName : DetailTitle;
            DetailText = DetailText ?? string.Empty;
            UiTheme = string.IsNullOrWhiteSpace(UiTheme) ? "grand_prix_1" : UiTheme.Trim();
            LobbyBackground = string.IsNullOrWhiteSpace(LobbyBackground) ? null : LobbyBackground.Trim();
            DefaultPlayerLife = DefaultPlayerLife <= 0 ? 20 : DefaultPlayerLife;
            InitialProgress = Math.Max(0, Math.Min(InitialProgress, Math.Max(0, Bosses.Count - 1)));
            Abilities = Abilities ?? new List<BossRushAbility>();
            Bosses = Bosses ?? new List<BossRushBoss>();
            foreach (BossRushBoss boss in Bosses) boss.Normalize();
            HiddenBoss?.Normalize();
        }

        public static BossRushPackage CreateDefault()
        {
            const string drawOne = "(skill:draw)(timing:self_turn_start)(condition:{me.inplay.class.turn}=1)(target:character=me&target=deck&card_type=all&random_count=1)(option:none)(preprocess:remove_after_action=(count=1))";
            const string drawTwo = "(skill:draw)(timing:self_turn_start)(condition:{me.inplay.class.turn}=1)(target:character=me&target=deck&card_type=all&random_count=2)(option:none)(preprocess:remove_after_action=(count=1))";
            const string recoverOneEp = "(skill:possess_ep_modifier)(timing:self_turn_start)(condition:{me.usable_ep}<=0&&evolvable_turn=true)(target:character=me&target=inplay&card_type=class)(option:add_ep=1)(preprocess:remove_after_action=(count=1))(effect_path:btl_ep_cure_1)(se_path:se_btl_ep_cure_1)(effect_move_type:DIRECT_EPPANEL_SELF)(engine_type:SHURIKEN)(effect_time:0.5)(effect_target_type:single)";
            const string recoverTwoEp = "(skill:possess_ep_modifier)(timing:self_turn_start)(condition:{me.usable_ep}<=0&&evolvable_turn=true)(target:character=me&target=inplay&card_type=class)(option:add_ep=2)(preprocess:remove_after_action=(count=1))(effect_path:btl_ep_cure_1)(se_path:se_btl_ep_cure_1)(effect_move_type:DIRECT_EPPANEL_SELF)(engine_type:SHURIKEN)(effect_time:0.5)(effect_target_type:single)";

            return new BossRushPackage
            {
                SchemaVersion = 5,
                Id = "default",
                DisplayName = "BossRush: Offline Gauntlet",
                DetailTitle = "Offline Gauntlet",
                DetailText = "Defeat three bosses in sequence with one deck. Before every main battle, choose one of three upgrades. Life carries between battles, while each defeated boss restores part of it. Clear all main battles to unlock the hidden challenger.",
                UiTheme = "grand_prix_1",
                DefaultPlayerLife = 20,
                InitialProgress = 0,
                Abilities = new List<BossRushAbility>
                {
                    new BossRushAbility
                    {
                        AbilityId = 117031020,
                        Skill = drawOne,
                        SpecialAbilityDesc = "Increase maximum life by 5, recover 5 life, and draw 1 extra card on turn 1.",
                        MaxLifeChange = 5,
                        LifeChange = 5
                    },
                    new BossRushAbility
                    {
                        AbilityId = 100011020,
                        Skill = drawOne,
                        SpecialAbilityDesc = "At the start of your first turn, draw 1 card."
                    },
                    new BossRushAbility
                    {
                        AbilityId = 100012010,
                        Skill = drawTwo,
                        SpecialAbilityDesc = "At the start of your first turn, draw 2 cards."
                    },
                    new BossRushAbility
                    {
                        AbilityId = 100011030,
                        Skill = recoverOneEp,
                        SpecialAbilityDesc = "Once per battle, recover 1 EP at the start of your turn when you have no usable EP."
                    },
                    new BossRushAbility
                    {
                        AbilityId = 100011040,
                        Skill = recoverTwoEp,
                        SpecialAbilityDesc = "Once per battle, recover 2 EP at the start of your turn when you have no usable EP."
                    },
                    new BossRushAbility
                    {
                        AbilityId = 100011050,
                        Skill = drawOne + "," + recoverOneEp,
                        SpecialAbilityDesc = "Draw 1 extra card on turn 1 and recover 1 EP once when depleted."
                    }
                },
                Bosses = new List<BossRushBoss>
                {
                    new BossRushBoss
                    {
                        Name = "Vanguard Commander",
                        EnemyClass = 1,
                        EnemyCharaId = 1,
                        EnemyEmblemId = 0,
                        EnemyDegreeId = 0,
                        BossrushStageId = 1,
                        Battle3dfieldId = 1,
                        BgmId = string.Empty,
                        EnemyLife = 20,
                        RecoveryPoint = 5,
                        EnemySkill = drawOne,
                        EnemySkillDesc = "At the start of the first turn, draw 1 extra card.",
                        EnemyAiId = 1,
                        PlayerFirstTurn = true,
                        CustomDeckCardIds = CreateStarterDeck(1),
                        LogicLevel = 1,
                        UseInnerEmote = true
                    },
                    new BossRushBoss
                    {
                        Name = "Verdant Warden",
                        EnemyClass = 4,
                        EnemyCharaId = 4,
                        EnemyEmblemId = 0,
                        EnemyDegreeId = 0,
                        BossrushStageId = 1,
                        Battle3dfieldId = 1,
                        BgmId = string.Empty,
                        EnemyLife = 25,
                        RecoveryPoint = 5,
                        EnemySkill = recoverOneEp,
                        EnemySkillDesc = "Once per battle, recover 1 EP when no usable EP remains.",
                        EnemyAiId = 1,
                        PlayerFirstTurn = false,
                        CustomDeckCardIds = CreateStarterDeck(4),
                        LogicLevel = 2,
                        UseInnerEmote = true
                    },
                    new BossRushBoss
                    {
                        Name = "Nexus Overlord",
                        EnemyClass = 8,
                        EnemyCharaId = 8,
                        EnemyEmblemId = 0,
                        EnemyDegreeId = 0,
                        BossrushStageId = 1,
                        Battle3dfieldId = 1,
                        BgmId = string.Empty,
                        EnemyLife = 30,
                        RecoveryPoint = 0,
                        EnemySkill = drawTwo + "," + recoverTwoEp,
                        EnemySkillDesc = "Draw 2 extra cards on turn 1 and recover 2 EP once when depleted.",
                        EnemyAiId = 1,
                        EnemyStartPp = 1,
                        CustomDeckCardIds = CreateStarterDeck(8),
                        LogicLevel = 2,
                        UseInnerEmote = true
                    }
                },
                HiddenBoss = new BossRushBoss
                {
                    Name = "Abyssal Challenger",
                    EnemyClass = 6,
                    EnemyCharaId = 6,
                    EnemyEmblemId = 0,
                    EnemyDegreeId = 0,
                    BossrushStageId = 1,
                    Battle3dfieldId = 1,
                    BgmId = string.Empty,
                    EnemyLife = 35,
                    RecoveryPoint = 0,
                    EnemySkill = drawTwo + "," + recoverTwoEp,
                    EnemySkillDesc = "Draw 2 extra cards on turn 1 and recover 2 EP once when depleted.",
                    EnemyAiId = 1,
                    PlayerFirstTurn = false,
                    EnemyStartPp = 1,
                    CustomDeckCardIds = CreateStarterDeck(6),
                    LogicLevel = 2,
                    UseInnerEmote = true
                }
            };
        }

        public bool IsLegacyGeneratedDefault()
        {
            return SchemaVersion < 2 &&
                string.Equals(Id, "default", StringComparison.OrdinalIgnoreCase) &&
                (Abilities == null || Abilities.Count == 0) &&
                Bosses != null && Bosses.Count == 1 &&
                string.Equals(Bosses[0].Name, "Training Boss", StringComparison.Ordinal) &&
                Bosses[0].EnemyClass == 1 && Bosses[0].EnemyCharaId == 1 &&
                Bosses[0].EnemyLife == 20 && Bosses[0].EnemyAiId == 1;
        }

        public bool IsGeneratedV2Default()
        {
            return SchemaVersion == 2 &&
                string.Equals(Id, "default", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(DisplayName, "BossRush: Offline Gauntlet", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(DetailText) &&
                Abilities != null && Abilities.Count == 6 &&
                Bosses != null && Bosses.Count == 3 &&
                string.Equals(Bosses[0].Name, "Vanguard Commander", StringComparison.Ordinal) &&
                string.Equals(Bosses[1].Name, "Verdant Warden", StringComparison.Ordinal) &&
                string.Equals(Bosses[2].Name, "Nexus Overlord", StringComparison.Ordinal);
        }

        public bool IsGeneratedV3Default()
        {
            return SchemaVersion == 3 &&
                string.Equals(Id, "default", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(DisplayName, "BossRush: Offline Gauntlet", StringComparison.Ordinal) &&
                string.Equals(DetailTitle, "Offline Gauntlet", StringComparison.Ordinal) &&
                Abilities != null && Abilities.Count == 6 &&
                Bosses != null && Bosses.Count == 3 &&
                string.Equals(Bosses[0].Name, "Vanguard Commander", StringComparison.Ordinal) &&
                string.Equals(Bosses[1].Name, "Verdant Warden", StringComparison.Ordinal) &&
                string.Equals(Bosses[2].Name, "Nexus Overlord", StringComparison.Ordinal);
        }

        public bool IsGeneratedV4Default()
        {
            return SchemaVersion == 4 &&
                string.Equals(Id, "default", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(DisplayName, "BossRush: Offline Gauntlet", StringComparison.Ordinal) &&
                string.Equals(DetailTitle, "Offline Gauntlet", StringComparison.Ordinal) &&
                Abilities != null && Abilities.Count == 6 &&
                Bosses != null && Bosses.Count == 3 &&
                string.Equals(Bosses[0].Name, "Vanguard Commander", StringComparison.Ordinal) &&
                string.Equals(Bosses[1].Name, "Verdant Warden", StringComparison.Ordinal) &&
                string.Equals(Bosses[2].Name, "Nexus Overlord", StringComparison.Ordinal);
        }

        private static List<int> CreateStarterDeck(int clan)
        {
            switch (clan)
            {
                case 1:
                    return new List<int>
                    {
                        100111010, 100111010, 100111010, 100011020, 100011020, 100011020,
                        100012010, 100111020, 100111020, 100111020, 100111040, 100111040,
                        100111040, 100114010, 100114010, 100114010, 100011030, 100011030,
                        100011030, 100111060, 100111060, 100111060, 100011040, 100011040,
                        100011040, 100111030, 100111030, 100111030, 100111050, 100111050,
                        100111050, 100011050, 100011050, 100011050, 100111070, 100111070,
                        100111070, 100121010, 100121010, 100121010
                    };
                case 4:
                    return new List<int>
                    {
                        100414020, 100414020, 100414020, 100011020, 100011020, 100011020,
                        100012010, 100411010, 100411010, 100411010, 100414010, 100414010,
                        100414010, 100011030, 100011030, 100011030, 100411050, 100411050,
                        100411050, 100011040, 100011040, 100011040, 100411030, 100411030,
                        100411030, 100414030, 100414030, 100414030, 100011050, 100011050,
                        100011050, 100411020, 100411020, 100411020, 100411040, 100411040,
                        100411040, 100421020, 100421020, 100421020
                    };
                case 6:
                    return new List<int>
                    {
                        100011020, 100011020, 100011020, 100012010, 100611010, 100611010,
                        100611010, 100611020, 100611020, 100611020, 100614010, 100614010,
                        100614010, 100614020, 100614020, 100614020, 100011030, 100011030,
                        100011030, 100611030, 100611030, 100611030, 100011040, 100011040,
                        100011040, 100611050, 100611050, 100611050, 100614030, 100614030,
                        100614030, 100011050, 100011050, 100011050, 100611040, 100611040,
                        100611040, 100621010, 100621010, 100621010
                    };
                case 8:
                    return new List<int>
                    {
                        100011020, 100011020, 100011020, 100012010, 100811020, 100811020,
                        100811020, 100811060, 100811060, 100811060, 100811070, 100811070,
                        100811070, 100814010, 100814010, 100814010, 100011030, 100011030,
                        100011030, 100811010, 100811010, 100811010, 100811030, 100811030,
                        100811030, 100011040, 100011040, 100011040, 100811040, 100811040,
                        100811040, 100824010, 100824010, 100824010, 100011050, 100011050,
                        100011050, 100811050, 100811050, 100811050
                    };
                default:
                    return new List<int>();
            }
        }
    }

    public sealed class BossRushBoss
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("enemy_class")] public int EnemyClass { get; set; } = 1;
        [JsonProperty("enemy_chara_id")] public int EnemyCharaId { get; set; } = 1;
        [JsonProperty("enemy_emblem_id")] public long EnemyEmblemId { get; set; }
        [JsonProperty("enemy_degree_id")] public long EnemyDegreeId { get; set; }
        [JsonProperty("bossrush_stage_id")] public int BossrushStageId { get; set; } = 1;
        [JsonProperty("battle3dfield_id")] public int Battle3dfieldId { get; set; } = 1;
        [JsonProperty("bgm_id")] public string BgmId { get; set; }
        [JsonProperty("enemy_life")] public int EnemyLife { get; set; } = 20;
        [JsonProperty("recovery_point")] public int RecoveryPoint { get; set; }
        [JsonProperty("enemy_skill")] public string EnemySkill { get; set; }
        [JsonProperty("enemy_skills")] public List<string> EnemySkills { get; set; } = new List<string>();
        [JsonProperty("enemy_skill_desc")] public string EnemySkillDesc { get; set; }
        [JsonProperty("enemy_ai_id")] public int EnemyAiId { get; set; } = 1;
        [JsonProperty("player_first_turn")] public bool? PlayerFirstTurn { get; set; }
        [JsonProperty("player_start_pp")] public int PlayerStartPp { get; set; }
        [JsonProperty("enemy_start_pp")] public int EnemyStartPp { get; set; }
        [JsonProperty("player_start_field_card_ids")] public List<int> PlayerStartFieldCardIds { get; set; } = new List<int>();
        [JsonProperty("enemy_start_field_card_ids")] public List<int> EnemyStartFieldCardIds { get; set; } = new List<int>();
        [JsonProperty("enemy_sleeve_id")] public long EnemySleeveId { get; set; } = 3000011L;
        [JsonProperty("player_emotion_override")] public int PlayerEmotionOverride { get; set; }
        [JsonProperty("enemy_emotion_override")] public int EnemyEmotionOverride { get; set; }
        [JsonProperty("special_battle_id")] public string SpecialBattleId { get; set; }
        [JsonProperty("id_override_in_battle_log")] public string IdOverrideInBattleLog { get; set; }
        [JsonProperty("token_draw_effect_override")] public string TokenDrawEffectOverride { get; set; }
        [JsonProperty("special_token_draw_effect_override")] public string SpecialTokenDrawEffectOverride { get; set; }
        [JsonProperty("vs_effect_override")] public bool VsEffectOverride { get; set; }
        [JsonProperty("class_destroy_effect_override")] public int ClassDestroyEffectOverride { get; set; }
        [JsonProperty("mission_parameter")] public Dictionary<string, string> MissionParameter { get; set; } = new Dictionary<string, string>();
        [JsonProperty("custom_deck_card_ids")] public List<int> CustomDeckCardIds { get; set; } = new List<int>();
        [JsonProperty("deck_csv")] public string DeckCsv { get; set; }
        [JsonProperty("style_csv")] public string StyleCsv { get; set; }
        [JsonProperty("emote_csv")] public string EmoteCsv { get; set; }
        [JsonProperty("logic_level")] public int LogicLevel { get; set; } = 1;
        [JsonProperty("use_inner_emote")] public bool UseInnerEmote { get; set; } = true;

        public void Normalize()
        {
            Name = string.IsNullOrWhiteSpace(Name) ? "Boss" : Name;
            EnemyClass = EnemyClass <= 0 ? 1 : EnemyClass;
            EnemyCharaId = EnemyCharaId <= 0 ? 1 : EnemyCharaId;
            BossrushStageId = BossrushStageId <= 0 ? 1 : BossrushStageId;
            Battle3dfieldId = Battle3dfieldId <= 0 ? 1 : Battle3dfieldId;
            EnemyLife = EnemyLife <= 0 ? 20 : EnemyLife;
            EnemySkills = (EnemySkills ?? new List<string>())
                .Where(skill => !string.IsNullOrWhiteSpace(skill))
                .Select(skill => skill.Trim().Trim(','))
                .Where(skill => skill.Length > 0)
                .ToList();
            PlayerStartPp = Math.Max(0, Math.Min(10, PlayerStartPp));
            EnemyStartPp = Math.Max(0, Math.Min(10, EnemyStartPp));
            PlayerStartFieldCardIds = (PlayerStartFieldCardIds ?? new List<int>()).Take(5).ToList();
            EnemyStartFieldCardIds = (EnemyStartFieldCardIds ?? new List<int>()).Take(5).ToList();
            EnemySleeveId = EnemySleeveId <= 0 ? 3000011L : EnemySleeveId;
            MissionParameter = MissionParameter ?? new Dictionary<string, string>();
            CustomDeckCardIds = CustomDeckCardIds ?? new List<int>();
            LogicLevel = Math.Max(0, Math.Min(2, LogicLevel));
        }
    }

    public sealed class BossRushAbility
    {
        [JsonProperty("ability_id")] public int AbilityId { get; set; }
        [JsonProperty("is_foil")] public bool IsFoil { get; set; }
        [JsonProperty("skill")] public string Skill { get; set; }
        [JsonProperty("special_ability_desc")] public string SpecialAbilityDesc { get; set; }
        [JsonProperty("max_life_change")] public int MaxLifeChange { get; set; }
        [JsonProperty("life_change")] public int LifeChange { get; set; }
    }

    public sealed class BossRushSelectedAbility
    {
        public int AbilityId { get; set; }
        public bool IsFoil { get; set; }
        public int MaxLifeChange { get; set; }
        public int LifeChange { get; set; }
    }

    public sealed class BossRushState
    {
        public int Progress { get; set; }
        public int DeckNo { get; set; }
        public int DeckClassId { get; set; } = 1;
        public int DeckFormat { get; set; } = 0;
        public string DeckName { get; set; }
        public List<int> PlayerDeckCardIds { get; set; } = new List<int>();
        public int CurrentLife { get; set; }
        public int MaxLife { get; set; }
        public List<BossRushSelectedAbility> SelectedAbilities { get; set; } = new List<BossRushSelectedAbility>();
        public List<int> AbilityCandidateIds { get; set; } = new List<int>();
        public int AbilityCandidateSelection { get; set; } = -1;
        public int TotalTurns { get; set; }
        public bool IsLose { get; set; }
        public bool IsFinished { get; set; }
        public bool HiddenBossUnlocked { get; set; }
        public bool HiddenBossFinished { get; set; }
        public int BestTurns { get; set; }

        public static BossRushState Create(BossRushPackage package)
        {
            return new BossRushState
            {
                Progress = package.InitialProgress,
                CurrentLife = package.DefaultPlayerLife,
                MaxLife = package.DefaultPlayerLife,
                SelectedAbilities = new List<BossRushSelectedAbility>(),
                AbilityCandidateIds = new List<int>(),
                AbilityCandidateSelection = -1,
                PlayerDeckCardIds = new List<int>()
            };
        }

        public void Normalize(BossRushPackage package)
        {
            Progress = Math.Max(0, Math.Min(Progress, Math.Max(0, package.Bosses.Count - 1)));
            MaxLife = MaxLife <= 0 ? package.DefaultPlayerLife : MaxLife;
            CurrentLife = Math.Max(0, Math.Min(CurrentLife, MaxLife));
            SelectedAbilities = SelectedAbilities ?? new List<BossRushSelectedAbility>();
            AbilityCandidateIds = AbilityCandidateIds ?? new List<int>();
            PlayerDeckCardIds = PlayerDeckCardIds ?? new List<int>();
        }
    }
}
