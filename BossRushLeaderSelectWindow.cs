using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cute;
using HarmonyLib;
using UnityEngine;
using Wizard;
using Wizard.Dialog.Setting;

namespace Shadowbus
{
    /// <summary>
    /// Local dialog that lets the player pick which leader the next BossRush
    /// opponent uses. Only the character is changed; the boss keeps its own
    /// class, deck, life and skills, so this never rewrites the package.
    /// </summary>
    internal sealed class BossRushLeaderSelectWindow : MonoBehaviour
    {
        private const int LeadersPerPage = 10;
        private const int OtherClassFilter = 0;

        // One shared row height so the class filter row cannot drift away from
        // the rest of the layout when the panel is rearranged.
        private const float ClassButtonRowY = -76f;
        private const int MaxEnemyLife = 99;

        private sealed class CsvChoice
        {
            public string Label;
            public string Path;
        }

        private sealed class DeckChoice
        {
            public string Label;
            public string Path;
        }

        private sealed class SkillChoice
        {
            public string Label;
            public int AbilityId;
        }

        private DialogBase _dialog;
        private BossRushLobby _lobby;
        private int _bossIndex;
        private int _defaultCharaId;
        private int _selectedCharaId;
        private int _classFilter = 1;
        private int _leaderIndex;
        private int _leaderPageIndex;
        private int _leaderBuildVersion;
        private bool _isDestroyed;

        private List<ClassCharacterMasterData> _allLeaders = new List<ClassCharacterMasterData>();
        private List<ClassCharacterMasterData> _leaders = new List<ClassCharacterMasterData>();
        private List<int> _classFilters = new List<int>();
        private List<string> _loadedLeaderPaths = new List<string>();
        private List<CsvChoice> _styleCsvChoices = new List<CsvChoice>();
        private List<CsvChoice> _emoteCsvChoices = new List<CsvChoice>();
        private List<CsvChoice> _deckCsvChoices = new List<CsvChoice>();
        private List<DeckChoice> _deckChoices = new List<DeckChoice>();
        private List<SkillChoice> _skillChoices = new List<SkillChoice>();
        private int _styleCsvIndex;
        private int _emoteCsvIndex;
        private int _deckCsvIndex;
        private int _deckIndex;
        private int _skillIndex;
        private int _enemyLife = 20;
        private bool _updatingLifeSlider;

        private GameObject _contentRoot;
        private GameObject _leaderStripRoot;
        private UILabel _bossLabel;
        private UILabel _leaderNameLabel;
        private UILabel _leaderHintLabel;
        private UILabel _leaderPageLabel;
        private UIButton _leaderPreviousButton;
        private UIButton _leaderNextButton;
        private UIButton _styleCsvButton;
        private UIButton _emoteCsvButton;
        private UIButton _deckCsvButton;
        private UIButton _deckButton;
        private UIButton _skillButton;
        private ItemSlider _lifeSlider;
        private readonly List<UIButton> _classButtons = new List<UIButton>();
        private readonly List<SelectRandomSkinButton> _leaderButtons = new List<SelectRandomSkinButton>();
        private SettingBase _settingTemplate;
        private SelectRandomSkinDialog _skinDialogTemplate;

        internal static void Open(BossRushLobby lobby)
        {
            int bossIndex = BossRushOfflineData.GetNextBattleBossIndex();
            BossRushBoss boss = BossRushOfflineData.GetBossByIndex(bossIndex);
            if (boss == null)
            {
                ShowMessage("对手自选设置", "当前没有可以设置的对手。");
                return;
            }

            try
            {
                DialogBase dialog = UIManager.GetInstance().CreateDialogClose(false, false);
                dialog.SetSize(DialogBase.Size.XL);
                dialog.SetTitleLabel("对手自选设置");
                dialog.gameObject
                    .AddComponent<BossRushLeaderSelectWindow>()
                    .Initialize(dialog, lobby, bossIndex, boss);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError($"[BossRush] Failed to open the opponent setup dialog.\n{exception}");
                ShowMessage("对手自选设置", "打开对手设置界面失败，请查看 BepInEx 日志。");
            }
        }

        private void Initialize(DialogBase dialog, BossRushLobby lobby, int bossIndex, BossRushBoss boss)
        {
            _dialog = dialog;
            _lobby = lobby;
            _bossIndex = bossIndex;
            _defaultCharaId = boss.EnemyCharaId;
            _selectedCharaId = BossRushOfflineData.ResolveCharaId(boss);

            try
            {
                _allLeaders = (Data.Master?.ClassCharacterList ?? new List<ClassCharacterMasterData>())
                    .Where(leader => leader != null && leader.is_usable)
                    .GroupBy(leader => leader.chara_id)
                    .Select(group => group.First())
                    .ToList();
                _classFilters = BuildClassFilters(_allLeaders);
                _classFilter = ResolveInitialClassFilter(boss);
                _enemyLife = BossRushOfflineData.ResolveEnemyLife(boss);
                RefreshCsvChoices();

                BuildNativeUi(boss);
                RebuildLeaders();
                UpdateClassButtons();
                UpdateCsvButtons();
                UpdateLifeSlider();
                BeginRebuildLeaderButtons();
            }
            catch
            {
                _dialog.CloseSoon();
                throw;
            }
        }

        private static List<int> BuildClassFilters(List<ClassCharacterMasterData> leaders)
        {
            var filters = Enumerable.Range(1, 8)
                .Where(classId => leaders.Any(leader => leader.class_id == classId))
                .ToList();
            if (leaders.Any(leader => leader.class_id < 1 || leader.class_id > 8))
            {
                filters.Add(OtherClassFilter);
            }
            return filters;
        }

        private int ResolveInitialClassFilter(BossRushBoss boss)
        {
            ClassCharacterMasterData selected = _allLeaders.FirstOrDefault(leader => leader.chara_id == _selectedCharaId);
            int classId = selected?.class_id ?? boss.EnemyClass;
            if (classId < 1 || classId > 8)
            {
                classId = OtherClassFilter;
            }
            return _classFilters.Contains(classId)
                ? classId
                : _classFilters.FirstOrDefault();
        }

        private void BuildNativeUi(BossRushBoss boss)
        {
            _dialog.SetButtonLayout(DialogBase.ButtonLayout.BlueBtn_CancelBtn);
            _dialog.SetButtonText("确定", "取消");
            _dialog.SetButtonDelegate(Confirm);
            _dialog.isNotCloseWindowButton1 = true;
            _dialog.DetailMsg.gameObject.SetActive(false);

            _settingTemplate = UIManager.GetInstance().OptionSettingPrefab;
            if (_settingTemplate == null)
            {
                throw new InvalidOperationException("OptionSettingPrefab is unavailable.");
            }

            GameObject skinDialogPrefab = Resources.Load<GameObject>("UI/layoutParts/Dialog/SelectRandomSkinDialog");
            _skinDialogTemplate = skinDialogPrefab == null ? null : skinDialogPrefab.GetComponent<SelectRandomSkinDialog>();
            if (_skinDialogTemplate == null)
            {
                throw new InvalidOperationException("SelectRandomSkinDialog is unavailable.");
            }

            _contentRoot = new GameObject("BossRushLeaderSelectContent");
            _contentRoot.layer = _dialog.gameObject.layer;

            CreateSectionHeader("对手设置", new Vector3(-500f, 178f, 0f), 1000);
            _bossLabel = CreateLabel(
                DescribeBoss(boss),
                new Vector3(-500f, 146f, 0f),
                760,
                30,
                17,
                NGUIText.Alignment.Left);
            CreateNativeButton("一键恢复默认", new Vector3(400f, 146f, 0f), 200, 36, RestoreAllDefaults);

            CreateLabel("卡组", new Vector3(-500f, 112f, 0f), 130, 32, 17, NGUIText.Alignment.Left);
            _deckButton = CreateNativeButton(
                string.Empty,
                new Vector3(-230f, 112f, 0f),
                460,
                38,
                OpenDeckDialog);

            CreateLabel("Deck CSV", new Vector3(-500f, 74f, 0f), 130, 32, 17, NGUIText.Alignment.Left);
            _deckCsvButton = CreateNativeButton(
                string.Empty,
                new Vector3(-230f, 74f, 0f),
                460,
                38,
                () => OpenCsvDialog("选择 Deck CSV", _deckCsvChoices, _deckCsvIndex, index => _deckCsvIndex = index));

            CreateLabel("Style CSV", new Vector3(-500f, 36f, 0f), 130, 32, 17, NGUIText.Alignment.Left);
            _styleCsvButton = CreateNativeButton(
                string.Empty,
                new Vector3(-230f, 36f, 0f),
                460,
                38,
                () => OpenCsvDialog("选择 Style CSV", _styleCsvChoices, _styleCsvIndex, index => _styleCsvIndex = index));

            CreateLabel("Emote CSV", new Vector3(-500f, -2f, 0f), 130, 32, 17, NGUIText.Alignment.Left);
            _emoteCsvButton = CreateNativeButton(
                string.Empty,
                new Vector3(-230f, -2f, 0f),
                460,
                38,
                () => OpenCsvDialog("选择 Emote CSV", _emoteCsvChoices, _emoteCsvIndex, index => _emoteCsvIndex = index));

            CreateLabel("技能", new Vector3(60f, 112f, 0f), 130, 32, 17, NGUIText.Alignment.Left);
            _skillButton = CreateNativeButton(
                string.Empty,
                new Vector3(320f, 112f, 0f),
                420,
                38,
                OpenSkillDialog);

            _lifeSlider = NGUITools.AddChild(_contentRoot, _settingTemplate.m_itemSlider).GetComponent<ItemSlider>();
            _lifeSlider.name = "EnemyLifeSlider";
            _lifeSlider.transform.localPosition = new Vector3(290f, 66f, 0f);
            _lifeSlider.transform.localScale = new Vector3(0.78f, 0.78f, 1f);
            _lifeSlider.SetTitleLabel("生命上限");
            _lifeSlider.SetActive_SeparatorLine(false);
            _lifeSlider.m_slider.numberOfSteps = MaxEnemyLife;
            _lifeSlider.AddChangeCallback(OnLifeSliderChanged);

            CreateNativeButton("恢复默认生命", new Vector3(180f, 4f, 0f), 190, 36, RestoreDefaultLife);
            CreateNativeButton("刷新列表", new Vector3(400f, 4f, 0f), 170, 36, RefreshCsvChoicesAndControls);

            CreateSectionHeader("主战者", new Vector3(-500f, -32f, 0f), 1000);
            CreateClassButtons();
            CreateNativeButton("恢复默认主战者", new Vector3(-410f, -122f, 0f), 220, 36, RestoreDefaultLeader);
            _leaderPageLabel = CreateLabel(string.Empty, new Vector3(455f, -122f, 0f), 160, 26, 15, NGUIText.Alignment.Right);

            _leaderStripRoot = new GameObject("LeaderStrip");
            _leaderStripRoot.transform.parent = _contentRoot.transform;
            _leaderStripRoot.transform.localPosition = new Vector3(0f, -188f, 0f);
            _leaderStripRoot.transform.localScale = Vector3.one;
            _leaderStripRoot.layer = _contentRoot.layer;

            CreateLeaderPageButtons();
            _leaderNameLabel = CreateLabel(string.Empty, new Vector3(0f, -252f, 0f), 900, 28, 18, NGUIText.Alignment.Center);
            _leaderHintLabel = CreateLabel(string.Empty, new Vector3(0f, -282f, 0f), 900, 26, 14, NGUIText.Alignment.Center);
            _leaderHintLabel.color = new Color(0.82f, 0.82f, 0.82f, 1f);

            _dialog.SetObj(_contentRoot, Vector3.zero);
            _contentRoot.transform.localScale = new Vector3(0.80f, 0.80f, 1f);
        }

        private string DescribeBoss(BossRushBoss boss)
        {
            string position = _bossIndex == BossRushOfflineData.HiddenBossIndex
                ? "隐藏 Boss"
                : $"第 {_bossIndex + 1} 战";
            ClassCharacterMasterData defaultLeader = _allLeaders.FirstOrDefault(leader => leader.chara_id == _defaultCharaId);
            string defaultName = defaultLeader?.chara_name ?? $"chara {_defaultCharaId}";
            return $"{position}：{boss.Name}　配置默认主战者：{defaultName}（{_defaultCharaId}）";
        }

        private void CreateClassButtons()
        {
            _classButtons.Clear();
            for (int i = 0; i < _classFilters.Count; i++)
            {
                int classId = _classFilters[i];
                UIButton button = CreateNativeButton(
                    GetClassFilterName(classId),
                    new Vector3(-420f + i * 105f, ClassButtonRowY, 0f),
                    96,
                    36,
                    () => SelectClassFilter(classId));
                _classButtons.Add(button);
            }
        }

        private static string GetClassFilterName(int classId)
        {
            if (classId == OtherClassFilter)
            {
                return "其他";
            }

            try
            {
                return GameMgr.GetIns().GetDataMgr().GetClanNameByKey(classId);
            }
            catch
            {
                return "职业 " + classId;
            }
        }

        private void SelectClassFilter(int classId)
        {
            if (_classFilter == classId)
            {
                return;
            }

            _classFilter = classId;
            RebuildLeaders();
            UpdateClassButtons();
            BeginRebuildLeaderButtons();
        }

        private void UpdateClassButtons()
        {
            for (int i = 0; i < _classButtons.Count && i < _classFilters.Count; i++)
            {
                SetButtonSelected(_classButtons[i], _classFilters[i] == _classFilter);
            }
        }

        private void RebuildLeaders()
        {
            _leaders = _allLeaders
                .Where(leader => _classFilter == OtherClassFilter
                    ? leader.class_id < 1 || leader.class_id > 8
                    : leader.class_id == _classFilter)
                .OrderBy(leader => leader.skin_id)
                .ToList();

            _leaderIndex = _leaders.FindIndex(leader => leader.chara_id == _selectedCharaId);
            _leaderPageIndex = _leaderIndex < 0 ? 0 : _leaderIndex / LeadersPerPage;
        }

        private void BeginRebuildLeaderButtons()
        {
            int version = ++_leaderBuildVersion;
            DestroyLeaderButtons();
            ReleaseLeaderResources();
            UpdateLeaderPage();

            if (_leaders.Count == 0)
            {
                return;
            }

            List<string> paths = _leaders
                .Select(leader => Toolbox.ResourcesManager.GetAssetTypePath(
                    leader.skin_id.ToString(),
                    ResourcesManager.AssetLoadPathType.ClassCharaButton,
                    false))
                .Distinct()
                .ToList();
            StartCoroutine(LoadLeaderButtons(paths, version));
        }

        private IEnumerator LoadLeaderButtons(List<string> paths, int version)
        {
            yield return StartCoroutine(Toolbox.ResourcesManager.LoadAssetGroupAsync(paths, null, true));

            if (_isDestroyed || version != _leaderBuildVersion)
            {
                Toolbox.ResourcesManager.RemoveAssetGroup(paths);
                yield break;
            }

            _loadedLeaderPaths = paths;
            for (int i = 0; i < _leaders.Count; i++)
            {
                ClassCharacterMasterData leader = _leaders[i];
                GameObject buttonObject = NGUITools.AddChild(
                    _leaderStripRoot,
                    _skinDialogTemplate._skinButtonItemOriginal);
                buttonObject.name = "Leader_" + leader.skin_id;
                buttonObject.transform.localScale = new Vector3(0.52f, 0.52f, 1f);
                int leaderIndex = i;
                SelectRandomSkinButton button = buttonObject.GetComponent<SelectRandomSkinButton>();
                button.Initialize(
                    leader.skin_id,
                    i == _leaderIndex,
                    (skinId, status) => SelectLeader(leaderIndex),
                    obj => { },
                    (obj, direction) => { });
                _leaderButtons.Add(button);
            }

            UpdateLeaderPage();
        }

        private void SelectLeader(int index)
        {
            if (index < 0 || index >= _leaders.Count)
            {
                return;
            }

            _leaderIndex = index;
            _selectedCharaId = _leaders[index].chara_id;
            _leaderPageIndex = _leaderIndex / LeadersPerPage;
            UpdateLeaderPage();
        }

        private void RestoreDefaultLeader()
        {
            _selectedCharaId = _defaultCharaId;
            ClassCharacterMasterData defaultLeader = _allLeaders
                .FirstOrDefault(leader => leader.chara_id == _selectedCharaId);
            RebuildLeaders();

            // The configured leader usually belongs to another class than the tab
            // the player is currently browsing, so switch to the tab holding it.
            if (_leaderIndex < 0 && defaultLeader != null)
            {
                int classId = defaultLeader.class_id >= 1 && defaultLeader.class_id <= 8
                    ? defaultLeader.class_id
                    : OtherClassFilter;
                if (_classFilters.Contains(classId))
                {
                    SelectClassFilter(classId);
                    return;
                }
            }

            UpdateLeaderPage();
        }

        /// <summary>
        /// Builds the CSV lists from the config package's own `ai` folder and the
        /// shared `Mods/AIData` folder, keeping the current selection if the file
        /// is still there.
        /// </summary>
        private void RefreshCsvChoices()
        {
            string selectedStyle = GetSelectedPath(_styleCsvChoices, _styleCsvIndex);
            string selectedEmote = GetSelectedPath(_emoteCsvChoices, _emoteCsvIndex);
            string selectedDeckCsv = GetSelectedPath(_deckCsvChoices, _deckCsvIndex);
            if (_styleCsvChoices.Count == 0)
            {
                // First build: start from whatever the state file already holds.
                selectedStyle = BossRushOfflineData.GetAiCsvOverride(_bossIndex, "style");
                selectedEmote = BossRushOfflineData.GetAiCsvOverride(_bossIndex, "emote");
                selectedDeckCsv = BossRushOfflineData.GetAiCsvOverride(_bossIndex, "deck");
            }

            _styleCsvChoices = BuildCsvChoices("style");
            _emoteCsvChoices = BuildCsvChoices("emote");
            _deckCsvChoices = BuildCsvChoices("deck");
            _styleCsvIndex = FindPathIndex(_styleCsvChoices, selectedStyle);
            _emoteCsvIndex = FindPathIndex(_emoteCsvChoices, selectedEmote);
            _deckCsvIndex = FindPathIndex(_deckCsvChoices, selectedDeckCsv);

            string selectedDeck = _deckChoices.Count == 0
                ? BossRushOfflineData.GetDeckOverride(_bossIndex)
                : GetSelectedDeckPath();
            _deckChoices = BuildDeckChoices();
            _deckIndex = Math.Max(0, _deckChoices.FindIndex(
                choice => string.Equals(choice.Path, selectedDeck, StringComparison.OrdinalIgnoreCase)));

            int selectedSkill = _skillChoices.Count == 0
                ? BossRushOfflineData.GetSkillOverride(_bossIndex)
                : GetSelectedSkillId();
            _skillChoices = BuildSkillChoices();
            _skillIndex = Math.Max(0, _skillChoices.FindIndex(choice => choice.AbilityId == selectedSkill));
        }

        /// <summary>
        /// Enemy deck candidates: every deck under Mods/UnlimitedDecks, plus the
        /// entry that keeps the package's own `custom_deck_card_ids`.
        /// </summary>
        private List<DeckChoice> BuildDeckChoices()
        {
            var choices = new List<DeckChoice> { new DeckChoice { Label = "使用配置文件卡组", Path = null } };
            try
            {
                foreach (string path in CustomDeckStore.EnumerateDeckFiles()
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    choices.Add(new DeckChoice { Label = DescribeDeckFile(path), Path = path });
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not list local decks: {exception.Message}");
            }
            return choices;
        }

        private static string DescribeDeckFile(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            try
            {
                LocalDeckInfo info = Newtonsoft.Json.JsonConvert.DeserializeObject<LocalDeckInfo>(File.ReadAllText(path));
                string name = string.IsNullOrWhiteSpace(info?.DeckName) ? fileName : info.DeckName;
                int count = info?.CardIds == null ? 0 : info.CardIds.Count;
                return $"{fileName}  {name}  ({count}张)";
            }
            catch
            {
                return fileName;
            }
        }

        private sealed class LocalDeckInfo
        {
            [Newtonsoft.Json.JsonProperty("deck_name")] public string DeckName { get; set; }
            [Newtonsoft.Json.JsonProperty("card_id_array")] public List<int> CardIds { get; set; }
        }

        /// <summary>
        /// Enemy skill candidates. The package's `abilities` double as a skill
        /// library, so anything usable as a player buff can be given to a boss.
        /// </summary>
        private List<SkillChoice> BuildSkillChoices()
        {
            var choices = new List<SkillChoice> { new SkillChoice { Label = "使用配置文件技能", AbilityId = 0 } };
            foreach (BossRushAbility ability in BossRushOfflineData.GetAvailableAbilities())
            {
                if (string.IsNullOrWhiteSpace(ability.Skill))
                {
                    continue;
                }
                choices.Add(new SkillChoice
                {
                    Label = DescribeSkill(ability),
                    AbilityId = ability.AbilityId
                });
            }
            return choices;
        }

        private static string DescribeSkill(BossRushAbility ability)
        {
            string desc = ability.SpecialAbilityDesc ?? string.Empty;
            desc = string.Join(" ", desc.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()).ToArray());
            if (desc.Length > 52)
            {
                desc = desc.Substring(0, 51).TrimEnd() + "…";
            }
            return $"[{ability.AbilityId}] {desc}";
        }

        private string GetSelectedDeckPath()
        {
            return _deckIndex >= 0 && _deckIndex < _deckChoices.Count ? _deckChoices[_deckIndex].Path : null;
        }

        private int GetSelectedSkillId()
        {
            return _skillIndex >= 0 && _skillIndex < _skillChoices.Count ? _skillChoices[_skillIndex].AbilityId : 0;
        }

        private void OpenDeckDialog()
        {
            OpenListDialog("选择对手卡组", _deckChoices.Select(choice => choice.Label).ToList(), _deckIndex, index =>
            {
                _deckIndex = index;
                UpdateCsvButtons();
            });
        }

        private void OpenSkillDialog()
        {
            OpenListDialog("选择对手技能", _skillChoices.Select(choice => choice.Label).ToList(), _skillIndex, index =>
            {
                _skillIndex = index;
                UpdateCsvButtons();
            });
        }

        private void OpenListDialog(string title, List<string> labels, int selectedIndex, Action<int> onSelect)
        {
            if (labels == null || labels.Count == 0)
            {
                return;
            }

            DialogBase choiceDialog = DrumrollDialog.Create(
                labels,
                Mathf.Clamp(selectedIndex, 0, labels.Count - 1),
                null,
                null,
                onSelect,
                title);
            RaiseDialogAbove(choiceDialog, _dialog);
        }

        private void OnLifeSliderChanged()
        {
            if (_updatingLifeSlider)
            {
                return;
            }

            _enemyLife = Mathf.Clamp(
                Mathf.RoundToInt(1f + _lifeSlider.GetValue() * (MaxEnemyLife - 1f)), 1, MaxEnemyLife);
            UpdateLifeValueLabel();
        }

        private void UpdateLifeSlider()
        {
            if (_lifeSlider == null)
            {
                return;
            }

            _updatingLifeSlider = true;
            _lifeSlider.SetValue((_enemyLife - 1f) / (MaxEnemyLife - 1f));
            _updatingLifeSlider = false;
            UpdateLifeValueLabel();
        }

        private void UpdateLifeValueLabel()
        {
            if (_lifeSlider != null && _lifeSlider.m_valueLabel != null)
            {
                _lifeSlider.m_valueLabel.text = _enemyLife.ToString();
            }
        }

        /// <summary>
        /// Drops every choice on this screen back to what the package configures.
        /// Like the other reset buttons it only edits the screen; 确定 is what
        /// writes the cleared state.
        /// </summary>
        private void RestoreAllDefaults()
        {
            _deckIndex = 0;
            _deckCsvIndex = 0;
            _styleCsvIndex = 0;
            _emoteCsvIndex = 0;
            _skillIndex = 0;
            RestoreDefaultLife();
            UpdateCsvButtons();
            RestoreDefaultLeader();
        }

        private void RestoreDefaultLife()
        {
            BossRushBoss boss = BossRushOfflineData.GetBossByIndex(_bossIndex);
            _enemyLife = Math.Max(1, boss?.EnemyLife ?? 20);
            UpdateLifeSlider();
        }

        private List<CsvChoice> BuildCsvChoices(string kind)
        {
            BossRushBoss boss = BossRushOfflineData.GetBossByIndex(_bossIndex);
            string configured = kind == "style" ? boss?.StyleCsv : kind == "deck" ? boss?.DeckCsv : boss?.EmoteCsv;
            var choices = new List<CsvChoice>
            {
                new CsvChoice
                {
                    Label = string.IsNullOrWhiteSpace(configured)
                        ? "使用配置文件设置（未设置）"
                        : $"使用配置文件设置（{configured}）",
                    Path = null
                }
            };

            string packageDirectory = BossRushOfflineData.GetPackageDirectory();
            if (!string.IsNullOrEmpty(packageDirectory))
            {
                AddCsvFiles(choices, Path.Combine(packageDirectory, "ai", kind), "配置包");
            }
            string sharedRoot = kind == "style"
                ? PathHelper.AIStylePath
                : kind == "deck" ? PathHelper.AIDeckPath : PathHelper.AIEmotePath;
            AddCsvFiles(choices, sharedRoot, "AIData");
            return choices;
        }

        private static void AddCsvFiles(List<CsvChoice> choices, string directory, string source)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (string path in Directory.EnumerateFiles(directory, "*.csv", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (choices.Any(choice => string.Equals(choice.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                choices.Add(new CsvChoice
                {
                    Label = $"[{source}] {Path.GetFileName(path)}",
                    Path = path
                });
            }
        }

        private static string GetSelectedPath(List<CsvChoice> choices, int index)
        {
            return choices != null && index >= 0 && index < choices.Count ? choices[index].Path : null;
        }

        private static int FindPathIndex(List<CsvChoice> choices, string path)
        {
            int index = choices.FindIndex(choice => string.Equals(choice.Path, path, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? 0 : index;
        }

        private void RefreshCsvChoicesAndControls()
        {
            RefreshCsvChoices();
            UpdateCsvButtons();
        }

        private void UpdateCsvButtons()
        {
            if (_styleCsvButton != null)
            {
                SetNativeButtonText(_styleCsvButton, DescribeCsvChoice(_styleCsvChoices, _styleCsvIndex));
            }
            if (_emoteCsvButton != null)
            {
                SetNativeButtonText(_emoteCsvButton, DescribeCsvChoice(_emoteCsvChoices, _emoteCsvIndex));
            }
            if (_deckCsvButton != null)
            {
                SetNativeButtonText(_deckCsvButton, DescribeCsvChoice(_deckCsvChoices, _deckCsvIndex));
            }
            if (_deckButton != null)
            {
                SetNativeButtonText(_deckButton, _deckIndex >= 0 && _deckIndex < _deckChoices.Count
                    ? _deckChoices[_deckIndex].Label
                    : "使用配置文件卡组");
            }
            if (_skillButton != null)
            {
                SetNativeButtonText(_skillButton, _skillIndex >= 0 && _skillIndex < _skillChoices.Count
                    ? _skillChoices[_skillIndex].Label
                    : "使用配置文件技能");
            }
        }

        private static string DescribeCsvChoice(List<CsvChoice> choices, int index)
        {
            return choices != null && index >= 0 && index < choices.Count
                ? choices[index].Label
                : "使用配置文件设置";
        }

        private void OpenCsvDialog(
            string title,
            List<CsvChoice> choices,
            int selectedIndex,
            Action<int> onSelect)
        {
            if (choices == null || choices.Count == 0)
            {
                return;
            }

            DialogBase choiceDialog = DrumrollDialog.Create(
                choices.Select(choice => choice.Label).ToList(),
                Mathf.Clamp(selectedIndex, 0, choices.Count - 1),
                null,
                null,
                index =>
                {
                    onSelect(index);
                    UpdateCsvButtons();
                },
                title);
            RaiseDialogAbove(choiceDialog, _dialog);
        }

        /// <summary>
        /// The choice list opens on top of this dialog, so its panels have to be
        /// pushed above the parent's depth range to stay visible and clickable.
        /// </summary>
        private static void RaiseDialogAbove(DialogBase dialog, DialogBase parentDialog)
        {
            UIPanel[] parentPanels = parentDialog.GetComponentsInChildren<UIPanel>(true);
            UIPanel[] dialogPanels = dialog.GetComponentsInChildren<UIPanel>(true);
            int parentMaxDepth = parentPanels.Length == 0
                ? parentDialog.GetPanelDepth()
                : parentPanels.Max(panel => panel.depth);
            int parentMaxSortingOrder = parentPanels.Length == 0
                ? 0
                : parentPanels.Max(panel => panel.sortingOrder);

            if (dialogPanels.Length > 0)
            {
                int dialogMinDepth = dialogPanels.Min(panel => panel.depth);
                int depthOffset = parentMaxDepth + 2 - dialogMinDepth;
                foreach (UIPanel panel in dialogPanels)
                {
                    panel.depth += depthOffset;
                    panel.sortingOrder = parentMaxSortingOrder + 1;
                }
            }

            UIPanel backPanel = dialog.backView?.GetComponent<UIPanel>();
            if (backPanel != null)
            {
                backPanel.depth = parentMaxDepth + 1;
                backPanel.sortingOrder = parentMaxSortingOrder + 1;
            }
        }

        private void CreateLeaderPageButtons()
        {
            // Unity's null check has to go through the == overload, so a missing
            // prefab reference cannot be resolved with ??.
            bool mirrorPrevious = _skinDialogTemplate._btnPrevPage == null;
            UIButton previousTemplate = mirrorPrevious
                ? _skinDialogTemplate._btnNextPage
                : _skinDialogTemplate._btnPrevPage;
            _leaderPreviousButton = CloneLeaderPageButton(
                previousTemplate,
                new Vector3(-470f, -188f, 0f),
                mirrorPrevious,
                ShowPreviousLeaderPage);
            _leaderNextButton = CloneLeaderPageButton(
                _skinDialogTemplate._btnNextPage,
                new Vector3(470f, -188f, 0f),
                false,
                ShowNextLeaderPage);
        }

        private UIButton CloneLeaderPageButton(
            UIButton template,
            Vector3 position,
            bool mirrorHorizontally,
            Action onClick)
        {
            if (template == null)
            {
                return null;
            }

            GameObject buttonObject = NGUITools.AddChild(_contentRoot, template.gameObject);
            buttonObject.transform.localPosition = position;
            buttonObject.transform.localScale = new Vector3(mirrorHorizontally ? -0.78f : 0.78f, 0.78f, 1f);
            buttonObject.SetActive(true);

            UIButton button = buttonObject.GetComponent<UIButton>();
            button.onClick.Clear();
            button.onClick.Add(new EventDelegate(delegate
            {
                GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_SLIDE_BTN, false);
                onClick();
            }));
            return button;
        }

        private void ShowPreviousLeaderPage()
        {
            _leaderPageIndex = Mathf.Max(0, _leaderPageIndex - 1);
            UpdateLeaderPage();
        }

        private void ShowNextLeaderPage()
        {
            _leaderPageIndex = Mathf.Min(GetLeaderPageCount() - 1, _leaderPageIndex + 1);
            UpdateLeaderPage();
        }

        private void UpdateLeaderPage()
        {
            int pageCount = GetLeaderPageCount();
            _leaderPageIndex = Mathf.Clamp(_leaderPageIndex, 0, Mathf.Max(0, pageCount - 1));
            int firstIndex = _leaderPageIndex * LeadersPerPage;
            int lastIndex = Mathf.Min(firstIndex + LeadersPerPage, _leaderButtons.Count);
            int visibleCount = lastIndex - firstIndex;

            for (int i = 0; i < _leaderButtons.Count; i++)
            {
                bool visible = i >= firstIndex && i < lastIndex;
                SelectRandomSkinButton button = _leaderButtons[i];
                button.gameObject.SetActive(visible);
                button.SetSelectStatus(i == _leaderIndex);
                if (visible)
                {
                    int visibleIndex = i - firstIndex;
                    float x = (visibleIndex - (visibleCount - 1) * 0.5f) * 86f;
                    button.transform.localPosition = new Vector3(x, 0f, 0f);
                }
            }

            bool hasMultiplePages = pageCount > 1;
            if (_leaderPreviousButton != null)
            {
                _leaderPreviousButton.gameObject.SetActive(hasMultiplePages && _leaderPageIndex > 0);
            }
            if (_leaderNextButton != null)
            {
                _leaderNextButton.gameObject.SetActive(hasMultiplePages && _leaderPageIndex < pageCount - 1);
            }
            if (_leaderPageLabel != null)
            {
                _leaderPageLabel.text = hasMultiplePages ? $"{_leaderPageIndex + 1} / {pageCount}" : string.Empty;
            }

            UpdateSelectionLabels();
        }

        private void UpdateSelectionLabels()
        {
            ClassCharacterMasterData selected = _allLeaders.FirstOrDefault(leader => leader.chara_id == _selectedCharaId);
            if (_leaderNameLabel != null)
            {
                _leaderNameLabel.text = selected == null
                    ? $"当前选择：chara {_selectedCharaId}"
                    : $"当前选择：{selected.chara_name}（chara {selected.chara_id} / skin {selected.skin_id}）";
            }

            if (_leaderHintLabel == null)
            {
                return;
            }

            if (_leaders.Count == 0)
            {
                _leaderHintLabel.text = "该分类下没有可用主战者。";
                return;
            }

            BossRushBoss boss = BossRushOfflineData.GetBossByIndex(_bossIndex);
            bool crossClass = selected != null && boss != null && selected.class_id != boss.EnemyClass;
            _leaderHintLabel.text = crossClass
                ? "该主战者与本关职业不同：卡组、技能和生命仍按配置文件执行，只有登场角色改变。"
                : string.Empty;
        }

        private int GetLeaderPageCount()
        {
            return Mathf.Max(1, Mathf.CeilToInt(_leaders.Count / (float)LeadersPerPage));
        }

        private void Confirm()
        {
            BossRushOfflineData.SetLeaderOverride(
                _bossIndex,
                _selectedCharaId == _defaultCharaId ? 0 : _selectedCharaId);
            BossRushOfflineData.SetAiCsvOverride(_bossIndex, "style", GetSelectedPath(_styleCsvChoices, _styleCsvIndex));
            BossRushOfflineData.SetAiCsvOverride(_bossIndex, "emote", GetSelectedPath(_emoteCsvChoices, _emoteCsvIndex));
            BossRushOfflineData.SetAiCsvOverride(_bossIndex, "deck", GetSelectedPath(_deckCsvChoices, _deckCsvIndex));
            BossRushOfflineData.SetDeckOverride(_bossIndex, GetSelectedDeckPath());
            BossRushOfflineData.SetSkillOverride(_bossIndex, GetSelectedSkillId());
            BossRushBoss configuredBoss = BossRushOfflineData.GetBossByIndex(_bossIndex);
            BossRushOfflineData.SetLifeOverride(
                _bossIndex,
                configuredBoss != null && _enemyLife == Math.Max(1, configuredBoss.EnemyLife) ? 0 : _enemyLife);
            ApplyToLobby(_lobby, _bossIndex, _selectedCharaId);
            _dialog.CloseSoon();
        }

        /// <summary>
        /// Pushes the chosen leader into the lobby data the client already
        /// fetched, so the panel and the character art update without leaving
        /// the lobby. The battle itself reads the value again from the patched
        /// BossRush battle data.
        /// </summary>
        private static void ApplyToLobby(BossRushLobby lobby, int bossIndex, int charaId)
        {
            if (lobby == null || charaId <= 0 || bossIndex < 0)
            {
                return;
            }

            try
            {
                PreloadCharacterAssets(charaId);
                BossRushLobbyData lobbyData = AccessTools.Field(typeof(BossRushLobby), "_lobbyData")?
                    .GetValue(lobby) as BossRushLobbyData;
                if (lobbyData == null)
                {
                    return;
                }

                // The battle reads its enemy out of the lobby data the client
                // parsed on entry. Only the leader used to be rewritten here, so
                // life and skill changes did not reach the upcoming battle and
                // showed up one battle late instead.
                BossRushBoss boss = BossRushOfflineData.GetBossByIndex(bossIndex);
                int life = BossRushOfflineData.ResolveEnemyLife(boss);
                string skill = BossRushOfflineData.BuildEnemySkillText(boss);

                List<BossRushLobbyBossData> bossList = lobbyData.BossDataList;
                if (bossList != null && bossIndex < bossList.Count)
                {
                    ApplyToBossData(bossList[bossIndex], charaId, life, skill);
                }
                ApplyToBossData(lobbyData.CurrentBattleBossData, charaId, life, skill);

                RefreshLobbyVisuals(lobby, charaId, life);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning(
                    $"[BossRush] Could not refresh the lobby after the leader change: {exception.Message}");
            }
        }

        private static void ApplyToBossData(BossRushLobbyBossData bossData, int charaId, int life, string skill)
        {
            if (bossData == null)
            {
                return;
            }

            SetBackingField(bossData, "CharacterId", charaId);
            SetBackingField(bossData, "Life", life);
            SetBackingField(bossData, "Skill", skill ?? string.Empty);
        }

        private static void RefreshLobbyVisuals(BossRushLobby lobby, int charaId, int life)
        {
            BossRushLobbyBossPanel panel = AccessTools.Field(typeof(BossRushLobby), "_bossPanel")?
                .GetValue(lobby) as BossRushLobbyBossPanel;
            if (panel != null)
            {
                // The panel keeps its own reference to the boss entry it was
                // initialised with, which may or may not be the same object as
                // the one already updated in the lobby data.
                SetBackingField(
                    AccessTools.Field(typeof(BossRushLobbyBossPanel), "_bossData")?.GetValue(panel),
                    "CharacterId",
                    charaId);

                UILabel lifeLabel = AccessTools.Field(typeof(BossRushLobbyBossPanel), "_life")?
                    .GetValue(panel) as UILabel;
                if (lifeLabel != null)
                {
                    lifeLabel.text = life.ToString();
                }

                UITexture bossTexture = AccessTools.Field(typeof(BossRushLobbyBossPanel), "_bossCharaTexture")?
                    .GetValue(panel) as UITexture;
                RefreshTexture(
                    bossTexture,
                    charaId,
                    () => AccessTools.Method(typeof(BossRushLobbyBossPanel), "SetBossChara")?.Invoke(panel, null),
                    "boss panel");
            }

            UITexture classCharacter = AccessTools.Field(typeof(BossRushLobby), "_classCharacter")?
                .GetValue(lobby) as UITexture;
            RefreshTexture(
                classCharacter,
                charaId,
                () => AccessTools.Method(typeof(BossRushLobby), "InitializeClassCharacter")?.Invoke(lobby, null),
                "lobby character");
        }

        /// <summary>
        /// Reloads one lobby texture through the game's own setup method. The old
        /// texture is cleared first so a failed reload cannot leave the previous
        /// leader on screen, and restored at the end if nothing could be loaded.
        /// </summary>
        private static void RefreshTexture(UITexture target, int charaId, Action refresh, string description)
        {
            if (target == null)
            {
                return;
            }

            Texture previous = target.mainTexture;
            target.mainTexture = null;

            try
            {
                refresh();
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[BossRush] Could not refresh the {description}: {exception.Message}");
            }

            if (target.mainTexture != null)
            {
                return;
            }

            // The lobby only preloads the art the package configured, so a leader
            // chosen afterwards may still have to be loaded on demand.
            ApplyCharacterTexture(target, charaId);
            if (target.mainTexture == null)
            {
                target.mainTexture = previous;
                Plugin.Logger.LogWarning(
                    $"[BossRush] No texture found for chara {charaId}; the {description} keeps the previous image.");
            }
        }

        private static readonly ResourcesManager.AssetLoadPathType[] CharacterPathTypes =
        {
            ResourcesManager.AssetLoadPathType.ClassCharaBase,
            ResourcesManager.AssetLoadPathType.ClassCharaWideThumbnail,
            ResourcesManager.AssetLoadPathType.ClassCharaSkinThumbnail,
            ResourcesManager.AssetLoadPathType.ClassCharaButton
        };

        /// <summary>
        /// The lobby only preloads the art the package configured, and a plain
        /// LoadObject cannot pull in a bundle that was never loaded, so the
        /// chosen leader's bundles are loaded before the panels are refreshed.
        /// </summary>
        private static void PreloadCharacterAssets(int charaId)
        {
            ResourcesManager resources = Toolbox.ResourcesManager;
            var assets = new List<string>();
            foreach (ResourcesManager.AssetLoadPathType pathType in CharacterPathTypes)
            {
                try
                {
                    string path = resources.GetAssetTypePath(charaId.ToString(), pathType, false);
                    if (!string.IsNullOrEmpty(path) &&
                        !assets.Contains(path) &&
                        resources.ExistsAssetBundleManifest(path))
                    {
                        assets.Add(path);
                    }
                }
                catch
                {
                }
            }

            if (assets.Count == 0)
            {
                // Either the character really has no bundle, or the manifest check
                // does not accept this path form. Try the main art regardless.
                Plugin.Logger.LogWarning(
                    $"[BossRush] No character asset bundle was reported for chara {charaId}; loading the base art anyway.");
                assets.Add(resources.GetAssetTypePath(
                    charaId.ToString(),
                    ResourcesManager.AssetLoadPathType.ClassCharaBase,
                    false));
            }

            resources.LoadAssetGroupSync(assets, null, false);
            Plugin.Logger.LogInfo($"[BossRush] Loaded {assets.Count} character asset(s) for chara {charaId}.");
        }

        private static void ApplyCharacterTexture(UITexture target, int charaId)
        {
            ResourcesManager resources = Toolbox.ResourcesManager;
            foreach (ResourcesManager.AssetLoadPathType pathType in CharacterPathTypes)
            {
                try
                {
                    Texture texture = resources.LoadObject<Texture>(
                        resources.GetAssetTypePath(charaId.ToString(), pathType, true),
                        true,
                        false);
                    if (texture != null)
                    {
                        target.mainTexture = texture;
                        return;
                    }
                }
                catch
                {
                }
            }
        }

        private static void SetBackingField(object target, string propertyName, object value)
        {
            if (target == null)
            {
                return;
            }

            FieldInfo field = AccessTools.Field(target.GetType(), $"<{propertyName}>k__BackingField");
            if (field == null)
            {
                Plugin.Logger.LogWarning($"[BossRush] Property '{propertyName}' has no backing field to update.");
                return;
            }
            field.SetValue(target, value);
        }

        private void CreateSectionHeader(string text, Vector3 position, int width)
        {
            UILabel label = CreateLabel(text, position, width, 34, 22, NGUIText.Alignment.Left);
            label.fontStyle = FontStyle.Bold;

            GameObject lineObject = NGUITools.AddChild(_contentRoot, _dialog.titleLine.gameObject);
            lineObject.name = text + "Line";
            UISprite line = lineObject.GetComponent<UISprite>();
            line.ResetAnchors();
            line.SetDimensions(width, Mathf.Max(2, line.height));
            line.pivot = UIWidget.Pivot.Left;
            lineObject.transform.localPosition = position + new Vector3(0f, -23f, 0f);
            lineObject.transform.localScale = Vector3.one;
            lineObject.SetActive(true);
        }

        private UILabel CreateLabel(
            string text,
            Vector3 position,
            int width,
            int height,
            int fontSize,
            NGUIText.Alignment alignment)
        {
            GameObject labelObject = NGUITools.AddChild(_contentRoot, _dialog.DetailMsg.gameObject);
            labelObject.name = "Label_" + text;
            UILabel label = labelObject.GetComponent<UILabel>();
            label.ResetAnchors();
            label.pivot = alignment == NGUIText.Alignment.Center
                ? UIWidget.Pivot.Center
                : alignment == NGUIText.Alignment.Right
                    ? UIWidget.Pivot.Right
                    : UIWidget.Pivot.Left;
            label.alignment = alignment;
            label.overflowMethod = UILabel.Overflow.ShrinkContent;
            label.SetDimensions(width, height);
            label.fontSize = fontSize;
            label.text = text;
            labelObject.transform.localPosition = position;
            labelObject.transform.localScale = Vector3.one;
            labelObject.SetActive(true);
            return label;
        }

        private UIButton CreateNativeButton(
            string text,
            Vector3 position,
            int width,
            int height,
            Action onClick)
        {
            GameObject itemObject = NGUITools.AddChild(_contentRoot, _settingTemplate.m_itemButton);
            itemObject.name = "Button_" + text;
            itemObject.transform.localPosition = position;
            itemObject.transform.localScale = Vector3.one;
            itemObject.SetActive(true);

            ItemButton item = itemObject.GetComponent<ItemButton>();
            item.SetActive_SeparatorLine(false);
            item.SetActive_SpriteOnButton(false);
            item._subLabel.gameObject.SetActive(false);
            item._sprite.ResetAnchors();
            item._sprite.pivot = UIWidget.Pivot.Center;
            item._sprite.transform.localPosition = Vector3.zero;
            item._sprite.SetDimensions(width, height);
            item._label.ResetAnchors();
            item._label.pivot = UIWidget.Pivot.Center;
            item._label.alignment = NGUIText.Alignment.Center;
            item._label.overflowMethod = UILabel.Overflow.ShrinkContent;
            item._label.SetDimensions(width - 12, height - 4);
            item._label.transform.localPosition = Vector3.zero;
            item.SetValue(text);
            item._collider.size = new Vector3(width, height, item._collider.size.z);

            UIButton button = item._button;
            button.onClick.Clear();
            button.onClick.Add(new EventDelegate(delegate
            {
                GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_COMMON_BUTTON, false);
                onClick?.Invoke();
            }));

            SetButtonSelected(button, false);
            return button;
        }

        private static void SetNativeButtonText(UIButton button, string text)
        {
            ItemButton item = button == null ? null : button.GetComponentInParent<ItemButton>();
            if (item != null)
            {
                item.SetValue(text);
            }
        }

        private static void SetButtonSelected(UIButton button, bool selected)
        {
            string normalSprite = selected ? "btn_common_02_m_off" : "btn_common_01_m_off";
            string pressedSprite = selected ? "btn_common_02_m_on" : "btn_common_01_m_on";
            button.normalSprite = normalSprite;
            button.hoverSprite = normalSprite;
            button.pressedSprite = pressedSprite;
            UISprite sprite = button.GetComponent<UISprite>() ?? button.GetComponentInChildren<UISprite>(true);
            if (sprite != null)
            {
                sprite.spriteName = normalSprite;
            }
        }

        private static void ShowMessage(string title, string message)
        {
            DialogBase dialog = UIManager.GetInstance().CreateDialogClose(false, false);
            dialog.SetSize(DialogBase.Size.M);
            dialog.SetTitleLabel(title);
            dialog.SetButtonLayout(DialogBase.ButtonLayout.OkBtn);
            dialog.SetText(message, true);
        }

        private void DestroyLeaderButtons()
        {
            foreach (SelectRandomSkinButton button in _leaderButtons)
            {
                if (button != null)
                {
                    button.gameObject.SetActive(false);
                    Destroy(button.gameObject);
                }
            }
            _leaderButtons.Clear();
        }

        private void ReleaseLeaderResources()
        {
            if (_loadedLeaderPaths.Count == 0)
            {
                return;
            }

            Toolbox.ResourcesManager.RemoveAssetGroup(_loadedLeaderPaths);
            _loadedLeaderPaths.Clear();
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _leaderBuildVersion++;
            DestroyLeaderButtons();
            ReleaseLeaderResources();
        }
    }
}
