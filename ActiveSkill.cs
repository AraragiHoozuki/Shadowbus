using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Wizard;
using Wizard.Battle;
using Wizard.Battle.Touch;
using Wizard.Battle.UI;
using Wizard.Battle.View.Vfx;

namespace Shadowbus
{
    public static class DetailPanelControlExtensions
    {
        private static readonly ConditionalWeakTable<DetailPanelControl, UIButton> CustomButtonMap =
            new ConditionalWeakTable<DetailPanelControl, UIButton>();

        public static void SetCustomButton(this DetailPanelControl panel, UIButton button)
        {
            CustomButtonMap.Remove(panel);
            CustomButtonMap.Add(panel, button);
        }

        public static UIButton GetCustomButton(this DetailPanelControl panel)
        {
            return CustomButtonMap.TryGetValue(panel, out UIButton button) ? button : null;
        }
    }

    public sealed class DetailBottomButtonTracker : MonoBehaviour
    {
        public DetailPanelControl TargetPanel;

        private BattleCardBase _lastCard;
        private DetailPanelControl.ShowRequest _lastShowRequest;
        private bool _lastIsShow;
        private bool _hasObservedPanelState;

        private static readonly MethodInfo GetLastBottomWidgetMethod =
            AccessTools.Method(typeof(DetailPanelControl), "GetLastBottomWidget");

        private void LateUpdate()
        {
            if (TargetPanel == null || GetLastBottomWidgetMethod == null)
            {
                return;
            }

            BattleCardBase card = TargetPanel._card;
            bool panelStateChanged = !_hasObservedPanelState ||
                                     !ReferenceEquals(_lastCard, card) ||
                                     _lastShowRequest != TargetPanel.CurrentShowRequest ||
                                     _lastIsShow != TargetPanel.IsShow;
            if (panelStateChanged)
            {
                _hasObservedPanelState = true;
                _lastCard = card;
                _lastShowRequest = TargetPanel.CurrentShowRequest;
                _lastIsShow = TargetPanel.IsShow;
                if (card != null)
                {
                    ActiveSkill.RefreshButtonFromTracker(TargetPanel);
                }
            }

            UIButton button = TargetPanel.GetCustomButton();
            UIWidget buttonWidget = button != null ? button.GetComponent<UIWidget>() : null;
            UIWidget bottomWidget = GetLastBottomWidgetMethod.Invoke(TargetPanel, null) as UIWidget;
            if (button == null || buttonWidget == null || bottomWidget == null ||
                !button.gameObject.activeInHierarchy)
            {
                return;
            }

            Vector3 bottomCenter = (bottomWidget.worldCorners[0] + bottomWidget.worldCorners[3]) * 0.5f;
            float buttonHeight = Mathf.Abs(buttonWidget.worldCorners[1].y - buttonWidget.worldCorners[0].y);
            float padding = 8f * Mathf.Abs(button.transform.lossyScale.y);
            button.transform.position = new Vector3(
                bottomCenter.x,
                bottomCenter.y - buttonHeight * 0.5f - padding,
                bottomCenter.z);
            ActiveSkill.EnsureButtonVisuals(button);
        }
    }

    public static class ActiveSkill
    {
        public const string Timing = "when_activate";

        private static readonly FieldInfo IsOnceCallTimingField =
            AccessTools.Field(typeof(SkillBase), "<IsOnceCallTiming>k__BackingField");

        private static ActivationSession _activationSession;

        private sealed class ActivationSession
        {
            public BattleManagerBase BattleManager;
            public OperateMgr OperateManager;
            public BattleCardBase Card;
            public List<SkillBase> ActivationSkills;
            public List<SkillBase> ExpandedSelectSkills;
        }

        private sealed class SelectionStartState
        {
            public SkillBase Skill;
            public uint OriginalWhenEvolveStart;
        }

        public static bool IsWhenActivate(this SkillBase skill)
        {
            return skill != null && string.Equals(skill.SkillTimingText, Timing, StringComparison.Ordinal);
        }

        [HarmonyPatch(typeof(DetailPanelControl), nameof(DetailPanelControl.Start))]
        [HarmonyPostfix]
        public static void DetailPanelControl_Start_Postfix(DetailPanelControl __instance)
        {
            try
            {
                if (__instance.EvolveButton == null || __instance.GetCustomButton() != null)
                {
                    return;
                }

                Transform buttonParent = __instance.EvolveButton.transform.parent ?? __instance.transform;
                GameObject customButtonObject = UnityEngine.Object.Instantiate(
                    __instance.EvolveButton.gameObject,
                    buttonParent);
                customButtonObject.name = "ActiveSkillButton";
                customButtonObject.transform.SetSiblingIndex(__instance.EvolveButton.transform.GetSiblingIndex() + 1);

                TweenAlpha tween = customButtonObject.GetComponent<TweenAlpha>();
                if (tween != null)
                {
                    tween.enabled = false;
                    UnityEngine.Object.Destroy(tween);
                }

                UIAnchor anchor = customButtonObject.GetComponent<UIAnchor>();
                if (anchor != null)
                {
                    anchor.enabled = false;
                    UnityEngine.Object.Destroy(anchor);
                }

                UIButton button = customButtonObject.GetComponent<UIButton>();
                if (button == null)
                {
                    UnityEngine.Object.Destroy(customButtonObject);
                    return;
                }

                __instance.SetCustomButton(button);
                button.CacheDefaultColor();
                button.defaultColor = Color.white;
                button.hover = Color.white;
                button.pressed = Color.white;
                button.SetState(UIButtonColor.State.Normal, true);
                button.onClick.Clear();
                button.onClick.Add(new EventDelegate(() => OnActivateButtonClicked(__instance)));

                UIEventListener eventListener = customButtonObject.GetComponent<UIEventListener>();
                if (eventListener != null)
                {
                    eventListener.onClick = null;
                    eventListener.onPress = null;
                    eventListener.onDragOut = null;
                    eventListener.onClickRight = null;
                    eventListener.onPressRight = null;
                }

                DetailBottomButtonTracker tracker = __instance.gameObject.AddComponent<DetailBottomButtonTracker>();
                tracker.TargetPanel = __instance;

                Transform labelTransform = customButtonObject.transform.Find("Label");
                UILabel label = labelTransform != null ? labelTransform.GetComponent<UILabel>() : null;
                if (label != null)
                {
                    label.text = "\u542f\u52a8";
                }

                foreach (UIWidget widget in customButtonObject.GetComponentsInChildren<UIWidget>(true))
                {
                    widget.depth += 100;
                    widget.alpha = 1f;
                    widget.enabled = true;
                }

                customButtonObject.SetActive(false);
            }
            catch
            {
            }
        }

        [HarmonyPatch(typeof(DetailPanelControl), "EvolutionConfigSetup")]
        [HarmonyPostfix]
        public static void DetailPanelControl_EvolutionConfigSetup_Postfix(
            DetailPanelControl __instance,
            BattleCardBase targetCard,
            DetailPanelControl.ShowRequest showRequest)
        {
            RefreshButton(__instance, targetCard, showRequest);
        }

        [HarmonyPatch(typeof(SkillBase), nameof(SkillBase.SetIsOnceCallTiming))]
        [HarmonyPostfix]
        public static void SkillBase_SetIsOnceCallTiming_Postfix(SkillBase __instance)
        {
            if (__instance.IsWhenActivate())
            {
                IsOnceCallTimingField?.SetValue(__instance, true);
            }
        }

        [HarmonyPatch(typeof(SkillCollectionBase), nameof(SkillCollectionBase.GetSelectTypeSkill))]
        [HarmonyPostfix]
        private static void SkillCollectionBase_GetSelectTypeSkill_Postfix(
            ref IEnumerable<SkillBase> __result)
        {
            if (__result != null)
            {
                __result = __result.Where(skill => !skill.IsWhenActivate()).ToList();
            }
        }

        [HarmonyPatch(typeof(SkillTargetSelectTouchProcessor), nameof(SkillTargetSelectTouchProcessor.Start))]
        [HarmonyPrefix]
        private static void SkillTargetSelectTouchProcessor_Start_Prefix(
            SkillTargetSelectTouchProcessor __instance,
            ref SelectionStartState __state)
        {
            ActivationSession session = _activationSession;
            SkillBase selectSkill = __instance._selectSkill;
            if (session == null || selectSkill == null || !ReferenceEquals(session.Card, __instance._actCard))
            {
                return;
            }

            __state = new SelectionStartState
            {
                Skill = selectSkill,
                OriginalWhenEvolveStart = selectSkill.OnWhenEvolveStart
            };
            selectSkill.OnWhenEvolveStart = Math.Max(1U, selectSkill.OnWhenEvolveStart);
        }

        [HarmonyPatch(typeof(SkillTargetSelectTouchProcessor), nameof(SkillTargetSelectTouchProcessor.Start))]
        [HarmonyPostfix]
        private static void SkillTargetSelectTouchProcessor_Start_Postfix(SelectionStartState __state)
        {
            if (__state?.Skill != null)
            {
                __state.Skill.OnWhenEvolveStart = __state.OriginalWhenEvolveStart;
            }
        }

        [HarmonyPatch(typeof(EvolutionSimpleProcessor), nameof(EvolutionSimpleProcessor.Start))]
        [HarmonyPrefix]
        public static void EvolutionSimpleProcessor_Start_Prefix()
        {
            // A real evolution always wins over a stale or interrupted activation session.
            _activationSession = null;
        }

        [HarmonyPatch(typeof(BattleCardBase), nameof(BattleCardBase.GetBurialRiteCount))]
        [HarmonyPrefix]
        public static bool BattleCardBase_GetBurialRiteCount_Prefix(
            BattleCardBase __instance,
            BattlePlayerReadOnlyInfoPair playerInfoPair,
            SkillConditionCheckerOption option,
            bool isPrePlay,
            ref int __result)
        {
            ActivationSession session = _activationSession;
            if (session == null || !ReferenceEquals(session.Card, __instance))
            {
                return true;
            }

            __result = session.ActivationSkills.Count(skill =>
                skill.CheckConditionWithoutBurialRite(playerInfoPair, option, isPrePlay));
            return false;
        }

        [HarmonyPatch(typeof(OperateMgr), nameof(OperateMgr.EvolutionCard))]
        [HarmonyPrefix]
        public static bool OperateMgr_EvolutionCard_Prefix(
            OperateMgr __instance,
            BattleCardBase card,
            bool isPlayer,
            List<BattleCardBase> selectCards,
            List<int> selectChoiceId,
            ref VfxBase __result)
        {
            ActivationSession session = _activationSession;
            if (!MatchesSession(session, __instance, card))
            {
                return true;
            }

            _activationSession = null;
            __result = ActivateCard(
                session.BattleManager,
                card,
                session.ActivationSkills,
                session.ExpandedSelectSkills,
                selectCards,
                selectChoiceId);
            return false;
        }

        [HarmonyPatch(typeof(OperateMgr), nameof(OperateMgr.CancelSelect))]
        [HarmonyPostfix]
        public static void OperateMgr_CancelSelect_Postfix(OperateMgr __instance, BattleCardBase card)
        {
            ClearSessionIfMatches(__instance, card);
        }

        [HarmonyPatch(typeof(OperateMgr), nameof(OperateMgr.CancelChoice))]
        [HarmonyPostfix]
        public static void OperateMgr_CancelChoice_Postfix(OperateMgr __instance, BattleCardBase card)
        {
            ClearSessionIfMatches(__instance, card);
        }

        internal static void RefreshButtonFromTracker(DetailPanelControl panel)
        {
            RefreshButton(
                panel,
                panel != null ? panel._card : null,
                panel != null ? panel.CurrentShowRequest : default);
        }

        internal static void EnsureButtonVisuals(UIButton button)
        {
            if (button == null)
            {
                return;
            }

            foreach (UIWidget widget in button.GetComponentsInChildren<UIWidget>(true))
            {
                widget.alpha = 1f;
                widget.enabled = true;
            }
        }

        private static void SetButtonLabel(UIButton button, int ppCost)
        {
            Transform labelTransform = button != null ? button.transform.Find("Label") : null;
            UILabel label = labelTransform != null ? labelTransform.GetComponent<UILabel>() : null;
            if (label != null)
            {
                label.text = ppCost > 0
                    ? $"\u542f\u52a8 ({ppCost} PP)"
                    : "\u542f\u52a8";
            }
        }

        private static void RefreshButton(
            DetailPanelControl panel,
            BattleCardBase card,
            DetailPanelControl.ShowRequest showRequest)
        {
            UIButton button = panel != null ? panel.GetCustomButton() : null;
            if (button == null)
            {
                return;
            }

            try
            {
                List<SkillBase> activationSkills = card?.Skills?
                    .Where(skill => skill != null && skill.IsWhenActivate())
                    .ToList() ?? new List<SkillBase>();
                bool normalRequest = showRequest == DetailPanelControl.ShowRequest.NORMAL;
                bool isPlayer = card != null && card.IsPlayer;
                bool isInplay = card != null && card.IsInplay;
                bool isClass = card != null && card.IsClass;
                bool hasActivationTiming = activationSkills.Count > 0;
                bool visible = normalRequest && isPlayer && isInplay && !isClass && hasActivationTiming;
                bool canStart = visible && CanStartActivation(card);
                int configuredPpCost = GetActivationPpCost(activationSkills);
                int requiredPp = configuredPpCost;
                int currentPp = card?.SelfBattlePlayer != null ? card.SelfBattlePlayer.Pp : 0;
                bool available = visible && TryGetActivationAvailability(card, out requiredPp, out currentPp);
                bool enabled = canStart && available;
                int displayedPpCost = requiredPp > 0 ? requiredPp : configuredPpCost;

                button.gameObject.SetActive(visible);
                if (visible)
                {
                    SetButtonLabel(button, displayedPpCost);
                    button.isEnabled = enabled;
                    button.SetState(
                        enabled ? UIButtonColor.State.Normal : UIButtonColor.State.Disabled,
                        true);
                    EnsureButtonVisuals(button);
                }
            }
            catch
            {
                button.gameObject.SetActive(false);
            }
        }

        private static void OnActivateButtonClicked(DetailPanelControl panel)
        {
            BattleCardBase card = panel != null ? panel._card : null;
            if (!CanStartActivation(card))
            {
                RefreshButton(panel, card, panel.CurrentShowRequest);
                return;
            }

            List<SkillBase> activationSkills = GetActivationSkills(card).ToList();
            bool hasAvailableActivation = TryGetActivationAvailability(card, out _, out _);
            if (activationSkills.Count == 0 || !hasAvailableActivation)
            {
                RefreshButton(panel, card, panel.CurrentShowRequest);
                return;
            }

            BattleManagerBase battleManager = BattleManagerBase.GetIns();
            TouchControl touchControl = battleManager.TouchControl;
            List<SkillBase> selectSkills = GetActivationSelectSkills(card, activationSkills);

            _activationSession = null;
            card.SelfBattlePlayer.LastTargetCardsList.Clear();
            card.OpponentBattlePlayer.LastTargetCardsList.Clear();
            panel.Hide();
            GameMgr.GetIns().GetSoundMgr().PlaySe(Se.TYPE.SYS_BTN_DECIDE, false);

            if (selectSkills.Count == 0)
            {
                battleManager.BattleUIContainer.DisableMenu(false);
                BattleLogManager.GetInstance().DisableButton();
                VfxBase activateVfx = ActivateCard(
                    battleManager,
                    card,
                    activationSkills,
                    new List<SkillBase>(),
                    null,
                    null);
                battleManager.VfxMgr.RegisterSequentialVfx<VfxBase>(activateVfx);
                return;
            }

            ActivationSession session = new ActivationSession
            {
                BattleManager = battleManager,
                OperateManager = battleManager.OperateMgr,
                Card = card,
                ActivationSkills = activationSkills,
                ExpandedSelectSkills = new List<SkillBase>()
            };
            _activationSession = session;
            session.ExpandedSelectSkills = card.GetSelectSkillsNoDuplication(selectSkills);

            battleManager.BattlePlayer.PlayerBattleView._isEvolutionSkillSelect = true;
            ITouchProcessor processor;
            if (selectSkills.Any(skill => skill.IsChoiceType))
            {
                processor = new ChoiceTouchProcessor(
                    battleManager,
                    card,
                    touchControl.GetPrediction(),
                    selectSkills,
                    true,
                    false,
                    null);
            }
            else
            {
                processor = SkillTargetSelectTouchProcessor.Create(
                    battleManager,
                    card,
                    selectSkills,
                    touchControl.GetPrediction(),
                    null,
                    true,
                    false,
                    null,
                    null,
                    null);
            }

            battleManager.BattleUIContainer.DisableMenu(false);
            Plugin.Instance.StartCoroutine(RegisterSelectionNextFrame(session, touchControl, processor));
        }

        private static IEnumerator RegisterSelectionNextFrame(
            ActivationSession session,
            TouchControl touchControl,
            ITouchProcessor processor)
        {
            yield return null;

            if (!ReferenceEquals(_activationSession, session) || !CanStartActivation(session.Card))
            {
                if (ReferenceEquals(_activationSession, session))
                {
                    _activationSession = null;
                    session.BattleManager.VfxMgr.RegisterImmediateVfx<VfxBase>(
                        CreateFinishActivationVfx(session.BattleManager));
                }
                yield break;
            }

            VfxBase startVfx = touchControl.RegisterTouchProcessor(processor);
            session.BattleManager.VfxMgr.RegisterImmediateVfx<VfxBase>(startVfx);
        }

        private static IEnumerable<SkillBase> GetActivationSkills(BattleCardBase card)
        {
            return card?.Skills == null
                ? Enumerable.Empty<SkillBase>()
                : card.Skills.Where(skill => skill.IsWhenActivate());
        }

        private static List<SkillBase> GetActivationSelectSkills(
            BattleCardBase card,
            IEnumerable<SkillBase> activationSkills)
        {
            BattlePlayerReadOnlyInfoPair pair = new BattlePlayerReadOnlyInfoPair(
                card.SelfBattlePlayer,
                card.OpponentBattlePlayer);
            SkillConditionCheckerOption option = new SkillConditionCheckerOption();
            List<SkillBase> result = new List<SkillBase>();

            foreach (SkillBase skill in activationSkills)
            {
                if (!skill.CheckCondition(pair, option, true))
                {
                    continue;
                }

                if (skill.IsChoiceType)
                {
                    result.Add(skill);
                    continue;
                }

                if (skill.IsUserSelectType)
                {
                    if (skill.GetSelectableCards(pair, option, false, null).Any())
                    {
                        result.Add(skill);
                    }
                    continue;
                }

                if (skill.IsBurialRite && skill.PreprocessList.Any(preprocess =>
                        preprocess is SkillPreprocessBurialRite &&
                        preprocess.IsRightPrePlay(pair, option, false)))
                {
                    result.Add(skill);
                }
            }

            return result;
        }

        private static bool TryGetActivationAvailability(
            BattleCardBase card,
            out int requiredPp,
            out int currentPp)
        {
            requiredPp = 0;
            currentPp = card?.SelfBattlePlayer != null ? card.SelfBattlePlayer.Pp : 0;
            if (!IsEligibleCard(card))
            {
                return false;
            }

            BattlePlayerReadOnlyInfoPair pair = new BattlePlayerReadOnlyInfoPair(
                card.SelfBattlePlayer,
                card.OpponentBattlePlayer);
            SkillConditionCheckerOption option = new SkillConditionCheckerOption();
            List<SkillBase> availableSkills = GetActivationSkills(card)
                .Where(skill => skill.VisualCheckCondition(pair, option, true))
                .Where(skill => !skill.IsUserSelectType ||
                                skill.GetSelectableCards(pair, option, false, null).Any())
                .ToList();
            if (availableSkills.Count == 0)
            {
                return false;
            }

            requiredPp = GetActivationPpCost(availableSkills);
            return currentPp >= requiredPp;
        }

        private static int GetActivationPpCost(IEnumerable<SkillBase> skills)
        {
            int total = 0;
            foreach (SkillBase skill in skills ?? Enumerable.Empty<SkillBase>())
            {
                if (skill?.PreprocessList == null)
                {
                    continue;
                }

                int callCount;
                try
                {
                    callCount = Math.Max(1, skill.CallCount);
                }
                catch
                {
                    callCount = 1;
                }

                foreach (SkillPreprocessUsePp usePp in skill.PreprocessList.OfType<SkillPreprocessUsePp>())
                {
                    total += Math.Max(0, usePp.ConsumeValue) * callCount;
                }
            }

            return total;
        }

        private static bool IsEligibleCard(BattleCardBase card)
        {
            return card != null &&
                   card.IsPlayer &&
                   card.IsInplay &&
                   !card.IsClass;
        }

        private static bool CanStartActivation(BattleCardBase card)
        {
            if (!IsEligibleCard(card))
            {
                return false;
            }

            BattleManagerBase battleManager = BattleManagerBase.GetIns();
            GameMgr gameManager = GameMgr.GetIns();
            return battleManager != null &&
                   gameManager != null &&
                   !battleManager.IsBattleEnd &&
                   !battleManager.IsStopOperate &&
                   !battleManager.BattlePlayer.Class.IsDead &&
                   !battleManager.BattleEnemy.Class.IsDead &&
                   card.SelfBattlePlayer.IsSelfTurn &&
                   !gameManager.IsWatchBattle &&
                   !gameManager.IsReplayBattle;
        }

        private static VfxBase ActivateCard(
            BattleManagerBase battleManager,
            BattleCardBase card,
            List<SkillBase> activationSkills,
            List<SkillBase> expandedSelectSkills,
            IEnumerable<BattleCardBase> selectedCards,
            List<int> selectedChoiceIds)
        {
            SequentialVfxPlayer sequence = SequentialVfxPlayer.Create(Array.Empty<VfxBase>());
            try
            {
                if (battleManager == null || card == null || battleManager.IsBattleEnd ||
                    battleManager.BattlePlayer.Class.IsDead || battleManager.BattleEnemy.Class.IsDead ||
                    !card.IsPlayer || !card.IsInplay || !card.SelfBattlePlayer.IsSelfTurn)
                {
                    sequence.Register<VfxBase>(CreateFinishActivationVfx(battleManager));
                    return sequence;
                }

                List<BattleCardBase> selectedCardList = selectedCards?
                    .Where(selectedCard => selectedCard != null)
                    .ToList() ?? new List<BattleCardBase>();
                foreach (BattleCardBase selectedCard in selectedCardList)
                {
                    selectedCard.SelfBattlePlayer.AddLastTargetCardsList(selectedCard);
                }

                SkillConditionCheckerOption option = BuildActivationOption(
                    card,
                    expandedSelectSkills ?? new List<SkillBase>(),
                    selectedCardList,
                    selectedChoiceIds);
                SkillProcessor skillProcessor = new SkillProcessor();
                BattlePlayerReadOnlyInfoPair readOnlyPair = new BattlePlayerReadOnlyInfoPair(
                    card.SelfBattlePlayer,
                    card.OpponentBattlePlayer);

                List<SkillBase> currentActivationSkills = activationSkills
                    .Where(skill => skill != null && skill.IsWhenActivate() && card.Skills.Contains(skill))
                    .ToList();
                foreach (SkillBase skill in currentActivationSkills)
                {
                    skill.InitSetIndividualId();
                }
                if (currentActivationSkills.Any(skill => skill.HasIndividualId))
                {
                    card.SelfBattlePlayer.BattleMgr.IncrementIndividualId();
                }

                Func<SkillBase, uint> timingSelector = skill => skill.IsWhenActivate() ? 1U : 0U;
                List<SkillBase> activeSkills = card.Skills.GetActiveSkills(
                    timingSelector,
                    readOnlyPair,
                    option,
                    skillProcessor,
                    false,
                    true);

                int requiredPp = GetActivationPpCost(activeSkills);
                int currentPp = card.SelfBattlePlayer.Pp;

                if (activeSkills.Count > 0 && currentPp >= requiredPp)
                {
                    sequence.Register<VfxBase>(CreateActivationEffectVfx(card));
                    SkillProcessor.ProcessInfo processInfo = new SkillProcessor.ProcessInfoCollection(
                        card,
                        card.Skills,
                        skillProcessor,
                        readOnlyPair,
                        option,
                        activeSkills);
                    skillProcessor.Register(processInfo, false);
                    sequence.Register<VfxBase>(skillProcessor.Process(
                        new BattlePlayerPair(card.SelfBattlePlayer, card.OpponentBattlePlayer),
                        false));
                }

                sequence.Register<VfxBase>(battleManager.BattlePlayer.UpdateInPlayBattleCardIconLabel());
                sequence.Register<VfxBase>(battleManager.JudgeBattleResult());
            }
            catch
            {
            }

            sequence.Register<VfxBase>(CreateFinishActivationVfx(battleManager));
            return sequence;
        }

        private static VfxBase CreateActivationEffectVfx(BattleCardBase card)
        {
            if (card?.BattleCardView == null)
            {
                return NullVfx.GetInstance();
            }

            return new WhenPlaySkillActivationVfx(card.BattleCardView);
        }

        private static SkillConditionCheckerOption BuildActivationOption(
            BattleCardBase card,
            IReadOnlyList<SkillBase> expandedSelectSkills,
            IReadOnlyList<BattleCardBase> selectedCards,
            IEnumerable<int> selectedChoiceIds)
        {
            SkillConditionCheckerOption option = new SkillConditionCheckerOption();
            if (selectedChoiceIds != null)
            {
                option.ChosenCards.AddRange(selectedChoiceIds);
            }

            int count = Math.Min(expandedSelectSkills.Count, selectedCards.Count);
            for (int index = 0; index < count; index++)
            {
                SkillBase skill = expandedSelectSkills[index];
                BattleCardBase selectedCard = selectedCards[index];
                if (skill.IsChoiceType)
                {
                    option.PlayedCard = card;
                    if (selectedChoiceIds == null)
                    {
                        option.ChosenCards.Add(selectedCard.BaseParameter.CardId);
                    }
                    continue;
                }

                option.SelectedCards.Add(
                    new SkillConditionCheckerOption.SkillAndSelectTarget(selectedCard, skill));
                if (skill.IsBurialRite)
                {
                    option.BurialRiteCards.Add(selectedCard);
                }
            }

            return option;
        }

        private static VfxBase CreateFinishActivationVfx(BattleManagerBase battleManager)
        {
            return InstantVfx.Create(() =>
            {
                if (battleManager == null)
                {
                    return;
                }

                battleManager.BattlePlayer.UpdateHandCardsPlayability(false);
                battleManager.BattleEnemy.UpdateHandCardsPlayability(false);
                battleManager.BattlePlayer.PlayerBattleView._isEvolutionSkillSelect = false;
                battleManager.BattlePlayer.PlayerBattleView.ClearSelectCardList();
                if (!battleManager.IsBattleEnd &&
                    !battleManager.BattlePlayer.Class.IsDead &&
                    !battleManager.BattleEnemy.Class.IsDead &&
                    battleManager.BattlePlayer.IsSelfTurn)
                {
                    battleManager.BattlePlayer.PlayerBattleView.ShowTurnEndButton(false);
                }
                battleManager.BattlePlayer.PlayerBattleView.UpdateTurnEndPulseEffect();
                battleManager.BattleUIContainer.EnableMenu();
                BattleLogManager.GetInstance().EnableButton();
            });
        }

        private static bool MatchesSession(
            ActivationSession session,
            OperateMgr operateManager,
            BattleCardBase card)
        {
            return session != null &&
                   ReferenceEquals(session.OperateManager, operateManager) &&
                   ReferenceEquals(session.Card, card);
        }

        private static void ClearSessionIfMatches(OperateMgr operateManager, BattleCardBase card)
        {
            if (MatchesSession(_activationSession, operateManager, card))
            {
                _activationSession = null;
            }
        }
    }
}
