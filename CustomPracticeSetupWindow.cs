using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cute;
using UnityEngine;
using Wizard;
using Wizard.Dialog.Setting;

namespace Shadowbus
{
    internal sealed class CustomPracticeSetupWindow : MonoBehaviour
    {
        private const int LeadersPerPage = 10;
        private const float ColumnScale = 0.78f;

        private sealed class AIPresetChoice
        {
            public PracticeAISettingData Setting;
            public string Label;
        }

        private sealed class CsvChoice
        {
            public string Label;
            public string Path;
        }

        private DialogBase _dialog;
        private ClassSelectionPage _page;
        private List<AIManager.CustomPracticeDeckChoice> _decks;
        private int _deckIndex;
        private int _classId;
        private List<ClassCharacterMasterData> _leaders;
        private int _leaderIndex;
        private int _leaderPageIndex;
        private List<AIPresetChoice> _presets;
        private int _presetIndex;
        private List<CsvChoice> _deckCsvChoices;
        private List<CsvChoice> _styleCsvChoices;
        private List<CsvChoice> _emoteCsvChoices;
        private int _deckCsvIndex;
        private int _styleCsvIndex;
        private int _emoteCsvIndex;
        private int _logicLevel;
        private int _maxLife;
        private bool _isStarting;
        private bool _updatingLifeSlider;
        private bool _isDestroyed;
        private int _leaderBuildVersion;

        private GameObject _contentRoot;
        private GameObject _leaderStripRoot;
        private UILabel _leaderNameLabel;
        private UILabel _validationLabel;
        private UILabel _leaderPageLabel;
        private ItemSlider _lifeSlider;
        private UIButton _leaderPreviousButton;
        private UIButton _leaderNextButton;
        private UIButton _deckButton;
        private UIButton _presetButton;
        private UIButton _deckCsvButton;
        private UIButton _styleCsvButton;
        private UIButton _emoteCsvButton;
        private readonly List<UIButton> _classButtons = new List<UIButton>();
        private readonly List<UIButton> _logicButtons = new List<UIButton>();
        private readonly List<SelectRandomSkinButton> _leaderButtons = new List<SelectRandomSkinButton>();
        private List<string> _loadedLeaderPaths = new List<string>();
        private SettingBase _settingTemplate;
        private SelectRandomSkinDialog _skinDialogTemplate;

        public void Initialize(
            DialogBase dialog,
            ClassSelectionPage page,
            List<AIManager.CustomPracticeDeckChoice> decks)
        {
            _dialog = dialog;
            _page = page;
            _decks = decks;
            _logicLevel = 2;
            _maxLife = 20;

            try
            {
                RefreshCsvChoices();
                SelectDeck(0, false);
                BuildNativeUi();
                RefreshAllControls();
                BeginRebuildLeaderButtons();
            }
            catch
            {
                _dialog.CloseSoon();
                throw;
            }
        }

        private void BuildNativeUi()
        {
            _dialog.SetSize(DialogBase.Size.XL);
            _dialog.SetTitleLabel("自定义练习");
            _dialog.SetButtonLayout(DialogBase.ButtonLayout.BlueBtn_CancelBtn);
            _dialog.SetButtonText("开始对战", "取消");
            _dialog.SetButtonDelegate(StartBattle);
            _dialog.isNotCloseWindowButton1 = true;
            _dialog.DetailMsg.gameObject.SetActive(false);

            _contentRoot = new GameObject("CustomPracticeNativeContent");
            _contentRoot.layer = _dialog.gameObject.layer;

            _settingTemplate = UIManager.GetInstance().OptionSettingPrefab;
            if (_settingTemplate == null)
            {
                throw new InvalidOperationException("OptionSettingPrefab is unavailable.");
            }

            GameObject skinDialogPrefab = Resources.Load<GameObject>("UI/layoutParts/Dialog/SelectRandomSkinDialog");
            if (skinDialogPrefab == null)
            {
                throw new InvalidOperationException("SelectRandomSkinDialog resource is unavailable.");
            }

            _skinDialogTemplate = skinDialogPrefab.GetComponent<SelectRandomSkinDialog>();
            if (_skinDialogTemplate == null)
            {
                throw new InvalidOperationException("SelectRandomSkinDialog component is unavailable.");
            }

            CreateSectionHeader("对手设置", new Vector3(-500f, 190f, 0f));
            CreateSectionHeader("AI 数据", new Vector3(20f, 190f, 0f));

            CreateLabel("卡组", new Vector3(-470f, 142f, 0f), 90, 34, 18, NGUIText.Alignment.Left);
            _deckButton = CreateNativeButton(
                string.Empty,
                new Vector3(-210f, 142f, 0f),
                320,
                38,
                OpenDeckDialog);

            CreateLabel("原作预设", new Vector3(30f, 142f, 0f), 130, 34, 18, NGUIText.Alignment.Left);
            _presetButton = CreateNativeButton(
                string.Empty,
                new Vector3(330f, 142f, 0f),
                350,
                38,
                OpenPresetDialog);

            CreateLabel("Deck CSV", new Vector3(30f, 92f, 0f), 130, 34, 18, NGUIText.Alignment.Left);
            _deckCsvButton = CreateNativeButton(
                string.Empty,
                new Vector3(330f, 92f, 0f),
                350,
                38,
                () => OpenCsvDialog("选择 Deck CSV", _deckCsvChoices, _deckCsvIndex, index => _deckCsvIndex = index));

            CreateLabel("Style CSV", new Vector3(30f, 42f, 0f), 130, 34, 18, NGUIText.Alignment.Left);
            _styleCsvButton = CreateNativeButton(
                string.Empty,
                new Vector3(330f, 42f, 0f),
                350,
                38,
                () => OpenCsvDialog("选择 Style CSV", _styleCsvChoices, _styleCsvIndex, index => _styleCsvIndex = index));

            CreateLabel("Emote CSV", new Vector3(30f, -8f, 0f), 130, 34, 18, NGUIText.Alignment.Left);
            _emoteCsvButton = CreateNativeButton(
                string.Empty,
                new Vector3(330f, -8f, 0f),
                350,
                38,
                () => OpenCsvDialog("选择 Emote CSV", _emoteCsvChoices, _emoteCsvIndex, index => _emoteCsvIndex = index));

            CreateLabel("职业", new Vector3(-470f, 102f, 0f), 120, 32, 19, NGUIText.Alignment.Left);
            CreateClassButtons();

            CreateLabel("AI 逻辑", new Vector3(-470f, -4f, 0f), 120, 32, 19, NGUIText.Alignment.Left);
            CreateLogicButtons();

            _lifeSlider = NGUITools.AddChild(_contentRoot, _settingTemplate.m_itemSlider).GetComponent<ItemSlider>();
            _lifeSlider.name = "MaxLifeSlider";
            _lifeSlider.transform.localPosition = new Vector3(-270f, -82f, 0f);
            _lifeSlider.transform.localScale = new Vector3(ColumnScale, ColumnScale, 1f);
            _lifeSlider.SetTitleLabel("生命上限");
            _lifeSlider.SetActive_SeparatorLine(false);
            _lifeSlider.m_slider.numberOfSteps = 100;
            _lifeSlider.AddChangeCallback(OnLifeSliderChanged);

            CreateNativeButton("刷新 CSV", new Vector3(390f, -66f, 0f), 150, 38, RefreshCsvChoicesAndControls);

            CreateSectionHeader("主战者", new Vector3(-500f, -116f, 0f), 1000);
            _leaderStripRoot = new GameObject("LeaderStrip");
            _leaderStripRoot.transform.parent = _contentRoot.transform;
            _leaderStripRoot.transform.localPosition = new Vector3(0f, -164f, 0f);
            _leaderStripRoot.transform.localScale = Vector3.one;
            _leaderStripRoot.layer = _contentRoot.layer;

            CreateLeaderPageButtons();
            _leaderNameLabel = CreateLabel(string.Empty, new Vector3(0f, -211f, 0f), 520, 28, 16, NGUIText.Alignment.Center);
            _leaderPageLabel = CreateLabel(string.Empty, new Vector3(445f, -116f, 0f), 100, 28, 15, NGUIText.Alignment.Right);
            _validationLabel = CreateLabel(string.Empty, new Vector3(0f, -211f, 0f), 900, 28, 15, NGUIText.Alignment.Center);
            _validationLabel.color = new Color(1f, 0.72f, 0.72f, 1f);

            _dialog.SetObj(_contentRoot, Vector3.zero);
        }

        private void CreateSectionHeader(string text, Vector3 position, int width = 470)
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

        private void CreateClassButtons()
        {
            string[] names = Enumerable.Range(1, 8)
                .Select(id => GameMgr.GetIns().GetDataMgr().GetClanNameByKey(id))
                .ToArray();

            for (int i = 0; i < names.Length; i++)
            {
                int classId = i + 1;
                float x = -420f + i % 4 * 105f;
                float y = 76f - i / 4 * 42f;
                UIButton button = CreateNativeButton(
                    names[i],
                    new Vector3(x, y, 0f),
                    96,
                    36,
                    () => SelectClass(classId));
                _classButtons.Add(button);
            }
        }

        private void CreateLogicButtons()
        {
            string[] labels = { "弱", "中", "强" };
            for (int i = 0; i < labels.Length; i++)
            {
                int logicLevel = i;
                UIButton button = CreateNativeButton(
                    labels[i],
                    new Vector3(-385f + i * 116f, -31f, 0f),
                    108,
                    36,
                    () =>
                    {
                        _logicLevel = logicLevel;
                        UpdateLogicButtons();
                    });
                _logicButtons.Add(button);
            }
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

        private void CreateLeaderPageButtons()
        {
            _leaderPreviousButton = CloneLeaderPageButton(
                _skinDialogTemplate._btnNextPage,
                new Vector3(-485f, -164f, 0f),
                true,
                ShowPreviousLeaderPage);
            _leaderNextButton = CloneLeaderPageButton(
                _skinDialogTemplate._btnNextPage,
                new Vector3(485f, -164f, 0f),
                false,
                ShowNextLeaderPage);
        }

        private UIButton CloneLeaderPageButton(
            UIButton template,
            Vector3 position,
            bool mirrorHorizontally,
            Action onClick)
        {
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

        private void SelectDeck(int index, bool refreshControls = true)
        {
            _deckIndex = Mathf.Clamp(index, 0, _decks.Count - 1);
            SelectClass(_decks[_deckIndex].EnemyClassId, refreshControls);
            UpdateDeckButton();
        }

        private void OpenDeckDialog()
        {
            var deckGroup = new DeckGroup(
                _decks.Select(choice => choice.Deck).ToList(),
                Format.Unlimited,
                DeckAttributeType.CustomDeck);
            DeckSelectUIDialog deckSelector = DeckSelectUIDialog.Create(
                "选择 AI 卡组",
                new DeckGroupListData(deckGroup),
                Format.Unlimited,
                DeckSelectUIDialog.eFormatChangeUIType.SingleFormat,
                false,
                OnDeckSelected,
                new DeckSelectUI.InitOptions
                {
                    PrimaryFirstDisplayDeck = _decks[_deckIndex].Deck
                });
            RaiseDialogAbove(deckSelector.Dialog, _dialog);
        }

        private void OnDeckSelected(DialogBase deckDialog, DeckData deck)
        {
            int index = _decks.FindIndex(choice =>
                ReferenceEquals(choice.Deck, deck) ||
                choice.Deck.GetDeckID() == deck.GetDeckID());
            if (index < 0)
            {
                return;
            }

            deckDialog.CloseSoon();
            SelectDeck(index);
            ClearValidation();
        }

        private void SelectClass(int classId, bool refreshControls = true)
        {
            _classId = Mathf.Clamp(classId, 1, 8);
            RebuildLeaders();
            RebuildPresets();
            ClearValidation();

            if (refreshControls && _contentRoot != null)
            {
                RefreshClassDependentControls();
                BeginRebuildLeaderButtons();
            }
        }

        private void RebuildLeaders()
        {
            DataMgr dataMgr = GameMgr.GetIns().GetDataMgr();
            _leaders = Data.Master.ClassCharacterList
                .Where(leader => leader.is_usable && leader.IsAcquired && leader.class_id == _classId)
                .OrderBy(leader => leader.skin_id)
                .ToList();

            ClassCharacterMasterData currentLeader = dataMgr.GetCharaPrmByClassId(_classId, true);
            if (_leaders.Count == 0 && currentLeader != null)
            {
                _leaders.Add(currentLeader);
            }

            int currentLeaderIndex = currentLeader == null
                ? -1
                : _leaders.FindIndex(leader => leader.chara_id == currentLeader.chara_id);
            if (currentLeaderIndex > 0)
            {
                ClassCharacterMasterData selectedLeader = _leaders[currentLeaderIndex];
                _leaders.RemoveAt(currentLeaderIndex);
                _leaders.Insert(0, selectedLeader);
            }

            _leaderIndex = 0;
            _leaderPageIndex = 0;
        }

        private void RebuildPresets()
        {
            List<PracticeAISettingData> settings = Data.Master.PracticeAISettingList?
                .GetSettingDataTable()?
                .Where(setting => setting.ClassId == _classId)
                .OrderBy(setting => setting.Difficulty)
                .ToList() ?? new List<PracticeAISettingData>();

            _presets = settings.Select((setting, index) => new AIPresetChoice
            {
                Setting = setting,
                Label = $"原作预设 {index + 1}  [{GetDeckFileName(setting)}]"
            }).ToList();

            _presetIndex = settings.FindIndex(setting => setting.Difficulty == 1);
            if (_presetIndex < 0)
            {
                _presetIndex = 0;
            }

            if (_presets.Count > 0)
            {
                ApplyPresetDefaults(_presets[_presetIndex].Setting);
            }
        }

        private void SelectPreset(int index)
        {
            if (_presets == null || _presets.Count == 0)
            {
                return;
            }

            _presetIndex = Mathf.Clamp(index, 0, _presets.Count - 1);
            ApplyPresetDefaults(_presets[_presetIndex].Setting);
            UpdateLogicButtons();
            UpdateLifeSlider();
            UpdateAIPresetButton();
            ClearValidation();
        }

        private void OpenPresetDialog()
        {
            if (_presets == null || _presets.Count == 0)
            {
                return;
            }

            OpenListDialog(
                "选择原作 AI 预设",
                _presets.Select(choice => choice.Label).ToList(),
                _presetIndex,
                SelectPreset);
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

            OpenListDialog(
                title,
                choices.Select(choice => choice.Label).ToList(),
                selectedIndex,
                index =>
                {
                    onSelect(index);
                    RefreshCsvControls();
                    ClearValidation();
                });
        }

        private void OpenListDialog(
            string title,
            List<string> choices,
            int selectedIndex,
            Action<int> onSelect)
        {
            DialogBase choiceDialog = DrumrollDialog.Create(
                choices,
                Mathf.Clamp(selectedIndex, 0, choices.Count - 1),
                null,
                null,
                onSelect,
                title);
            RaiseDialogAbove(choiceDialog, _dialog);
        }

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

        private void ApplyPresetDefaults(PracticeAISettingData preset)
        {
            _logicLevel = Mathf.Clamp(preset.LogicLevel, 0, 2);
            _maxLife = Mathf.Clamp(preset.MaxLife > 0 ? preset.MaxLife : 20, 1, 100);
        }

        private void RefreshCsvChoices()
        {
            string selectedDeckPath = GetSelectedPath(_deckCsvChoices, _deckCsvIndex);
            string selectedStylePath = GetSelectedPath(_styleCsvChoices, _styleCsvIndex);
            string selectedEmotePath = GetSelectedPath(_emoteCsvChoices, _emoteCsvIndex);

            _deckCsvChoices = BuildCsvChoices(PathHelper.AIDeckPath);
            _styleCsvChoices = BuildCsvChoices(PathHelper.AIStylePath);
            _emoteCsvChoices = BuildCsvChoices(PathHelper.AIEmotePath);

            _deckCsvIndex = FindPathIndex(_deckCsvChoices, selectedDeckPath);
            _styleCsvIndex = FindPathIndex(_styleCsvChoices, selectedStylePath);
            _emoteCsvIndex = FindPathIndex(_emoteCsvChoices, selectedEmotePath);
        }

        private void RefreshCsvChoicesAndControls()
        {
            RefreshCsvChoices();
            RefreshCsvControls();
            ClearValidation();
        }

        private static List<CsvChoice> BuildCsvChoices(string directory)
        {
            Directory.CreateDirectory(directory);
            var choices = new List<CsvChoice>
            {
                new CsvChoice { Label = "使用原作预设", Path = null }
            };
            choices.AddRange(Directory.EnumerateFiles(directory, "*.csv", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Select(path => new CsvChoice
                {
                    Label = Path.GetFileName(path),
                    Path = path
                }));
            return choices;
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

        private void RefreshAllControls()
        {
            UpdateDeckButton();
            RefreshClassDependentControls();
            RefreshCsvControls();
        }

        private void UpdateDeckButton()
        {
            string label = _decks != null && _decks.Count > 0
                ? GetDeckChoiceLabel(_decks[Mathf.Clamp(_deckIndex, 0, _decks.Count - 1)])
                : "无可用卡组";
            SetNativeButtonText(_deckButton, label);
        }

        private void RefreshClassDependentControls()
        {
            UpdateClassButtons();
            UpdateLogicButtons();
            UpdateLifeSlider();
            UpdateAIPresetButton();
        }

        private void RefreshCsvControls()
        {
            SetNativeButtonText(_deckCsvButton, GetChoiceLabel(_deckCsvChoices, _deckCsvIndex));
            SetNativeButtonText(_styleCsvButton, GetChoiceLabel(_styleCsvChoices, _styleCsvIndex));
            SetNativeButtonText(_emoteCsvButton, GetChoiceLabel(_emoteCsvChoices, _emoteCsvIndex));
        }

        private void UpdateAIPresetButton()
        {
            string label = _presets != null && _presets.Count > 0
                ? _presets[Mathf.Clamp(_presetIndex, 0, _presets.Count - 1)].Label
                : "无可用预设";
            SetNativeButtonText(_presetButton, label);
        }

        private static string GetChoiceLabel(List<CsvChoice> choices, int index)
        {
            return choices != null && choices.Count > 0
                ? choices[Mathf.Clamp(index, 0, choices.Count - 1)].Label
                : "无可用选项";
        }

        private static void SetNativeButtonText(UIButton button, string text)
        {
            ItemButton item = button == null ? null : button.GetComponentInParent<ItemButton>();
            if (item != null)
            {
                item.SetValue(text);
            }
        }

        private string GetDeckChoiceLabel(AIManager.CustomPracticeDeckChoice choice)
        {
            DeckData deck = choice.Deck;
            string name = string.IsNullOrEmpty(deck.GetDeckName()) ? "未命名卡组" : deck.GetDeckName();
            string className = GameMgr.GetIns().GetDataMgr().GetClanNameByKey(choice.EnemyClassId);
            return $"{name}  [{className} / {deck.GetCardIdList().Count}张]";
        }

        private void UpdateClassButtons()
        {
            for (int i = 0; i < _classButtons.Count; i++)
            {
                SetButtonSelected(_classButtons[i], i + 1 == _classId);
            }
        }

        private void UpdateLogicButtons()
        {
            for (int i = 0; i < _logicButtons.Count; i++)
            {
                SetButtonSelected(_logicButtons[i], i == _logicLevel);
            }
        }

        private void OnLifeSliderChanged()
        {
            if (_updatingLifeSlider)
            {
                return;
            }

            _maxLife = Mathf.Clamp(Mathf.RoundToInt(1f + _lifeSlider.GetValue() * 99f), 1, 100);
            UpdateLifeValueLabel();
        }

        private void UpdateLifeSlider()
        {
            if (_lifeSlider == null)
            {
                return;
            }

            _updatingLifeSlider = true;
            _lifeSlider.SetValue((_maxLife - 1f) / 99f);
            _updatingLifeSlider = false;
            UpdateLifeValueLabel();
        }

        private void UpdateLifeValueLabel()
        {
            if (_lifeSlider != null && _lifeSlider.m_valueLabel != null)
            {
                _lifeSlider.m_valueLabel.text = _maxLife.ToString();
            }
        }

        private void BeginRebuildLeaderButtons()
        {
            int version = ++_leaderBuildVersion;
            DestroyLeaderButtons();
            ReleaseLeaderResources();
            UpdateLeaderPage();

            if (_leaders == null || _leaders.Count == 0)
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
            _leaderIndex = Mathf.Clamp(index, 0, _leaders.Count - 1);
            _leaderPageIndex = _leaderIndex / LeadersPerPage;
            UpdateLeaderPage();
            ClearValidation();
        }

        private void ShowPreviousLeaderPage()
        {
            _leaderPageIndex = Mathf.Max(0, _leaderPageIndex - 1);
            UpdateLeaderPage();
        }

        private void ShowNextLeaderPage()
        {
            int pageCount = GetLeaderPageCount();
            _leaderPageIndex = Mathf.Min(pageCount - 1, _leaderPageIndex + 1);
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
            if (_leaderNameLabel != null)
            {
                _leaderNameLabel.text = _leaders != null && _leaders.Count > 0
                    ? _leaders[Mathf.Clamp(_leaderIndex, 0, _leaders.Count - 1)].chara_name
                    : "没有可用主战者";
            }
        }

        private int GetLeaderPageCount()
        {
            return Mathf.Max(1, Mathf.CeilToInt((_leaders?.Count ?? 0) / (float)LeadersPerPage));
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

        private void StartBattle()
        {
            if (_isStarting)
            {
                return;
            }
            if (_leaders == null || _leaders.Count == 0)
            {
                ShowValidation("所选职业没有可用主战者。");
                return;
            }
            if (_presets == null || _presets.Count == 0)
            {
                ShowValidation("所选职业没有可用的原作 AI 预设。");
                return;
            }

            _isStarting = true;
            _dialog.IsButton1Enabled = false;
            var settings = new AIManager.CustomPracticeSettings
            {
                Deck = _decks[_deckIndex].Deck,
                EnemyClassId = _classId,
                Leader = _leaders[_leaderIndex],
                AIPreset = _presets[_presetIndex].Setting,
                LocalDeckCsvPath = GetSelectedPath(_deckCsvChoices, _deckCsvIndex),
                LocalStyleCsvPath = GetSelectedPath(_styleCsvChoices, _styleCsvIndex),
                LocalEmoteCsvPath = GetSelectedPath(_emoteCsvChoices, _emoteCsvIndex),
                LogicLevel = _logicLevel,
                MaxLife = _maxLife
            };
            _dialog.CloseSoon();
            AIManager.StartCustomPractice(_page, settings);
        }

        private void ShowValidation(string message)
        {
            if (_validationLabel != null)
            {
                _validationLabel.text = message;
            }
            if (_leaderNameLabel != null)
            {
                _leaderNameLabel.text = string.Empty;
            }
        }

        private void ClearValidation()
        {
            if (_validationLabel != null)
            {
                _validationLabel.text = string.Empty;
            }
            UpdateLeaderPage();
        }

        private static string GetDeckFileName(PracticeAISettingData setting)
        {
            try
            {
                return Data.Master.AIDeckFileNameList.GetFileName(setting.DeckId);
            }
            catch
            {
                return $"Deck ID {setting.DeckId}";
            }
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
