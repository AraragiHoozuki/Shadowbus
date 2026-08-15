using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Wizard;

namespace Shadowbus.LLMAI
{
    internal static class LLMAITurnController
    {
        private sealed class ActiveTurn
        {
            public Coroutine Coroutine;
            public UnityWebRequest Request;
            public bool Cancelled;
        }

        private static readonly Dictionary<EnemyAI, ActiveTurn> ActiveTurns =
            new Dictionary<EnemyAI, ActiveTurn>();
        private static readonly HashSet<EnemyAI> OriginalAIBypass = new HashSet<EnemyAI>();
        private static LLMAISettings _settings = new LLMAISettings();

        internal static void Configure(LLMAISettings settings)
        {
            _settings = settings ?? new LLMAISettings();
            if (_settings.Enabled && !_settings.IsUsable(out string reason))
            {
                LLMDecisionLog.Warning("CONFIG", "Configured but unavailable: " + reason);
            }
            else if (_settings.Enabled)
            {
                LLMEndpointSet endpoints = LLMEndpointResolver.Resolve(_settings);
                LLMDecisionLog.Configuration(_settings, endpoints);
            }
        }

        internal static bool DefaultEnabled =>
            _settings.Enabled && _settings.IsUsable(out _);

        internal static bool IsAvailable(out string reason)
        {
            return _settings.IsUsable(out reason);
        }

        internal static bool IsControlling(EnemyAI ai)
        {
            return ai != null && ActiveTurns.ContainsKey(ai);
        }

        internal static bool TryIntercept(EnemyAI ai, bool useWait)
        {
            if (ai == null)
            {
                return false;
            }
            if (OriginalAIBypass.Remove(ai))
            {
                return false;
            }
            if (!_settings.IsUsable(out _) ||
                !AIManager.IsLLMAIEnabledForBattle(ai.BattleMgr) ||
                ai.PlayerPair?.Self == null ||
                ai.PlayerPair.Self.IsPlayer ||
                ai.BattleMgr.IsBattleEnd)
            {
                return false;
            }
            if (ActiveTurns.ContainsKey(ai))
            {
                LLMDecisionLog.Warning("TURN", "Duplicate ExecuteEnemyAI call ignored while a turn is active");
                return true;
            }

            ActiveTurn active = new ActiveTurn();
            ActiveTurns.Add(ai, active);
            active.Coroutine = Plugin.Instance.StartCoroutine(RunTurn(ai, useWait, active));
            return true;
        }

        internal static void Cancel(EnemyAI ai, string reason)
        {
            if (ai == null || !ActiveTurns.TryGetValue(ai, out ActiveTurn active))
            {
                return;
            }
            active.Cancelled = true;
            active.Request?.Abort();
            if (active.Coroutine != null && Plugin.Instance != null)
            {
                Plugin.Instance.StopCoroutine(active.Coroutine);
            }
            ActiveTurns.Remove(ai);
            LLMDecisionLog.Warning("CANCEL", "Active request/plan cancelled: " + reason);
        }

        internal static void CancelAll(string reason)
        {
            foreach (EnemyAI ai in new List<EnemyAI>(ActiveTurns.Keys))
            {
                Cancel(ai, reason);
            }
        }

        private static IEnumerator RunTurn(EnemyAI ai, bool useWait, ActiveTurn active)
        {
            int apiCalls = 0;
            int planNumber = 0;
            bool stateChanged = false;
            string continuationGoal = null;
            string replanReason = null;
            List<TurnPlanStep> executedSteps = new List<TurnPlanStep>();

            if (useWait)
            {
                yield return null;
            }

            while (!active.Cancelled && ai.BattleMgr != null && !ai.BattleMgr.IsBattleEnd)
            {
                BattlePlayerPair virtualPair;
                AIOperationSimulatorAccessor actionAccessor;
                List<LegalActionCandidate> legalActions;
                BattleSnapshotDto snapshot;
                List<AISinglePlayptnRecord> lethalPatterns = new List<AISinglePlayptnRecord>();
                try
                {
                    virtualPair = ai.PlayerPair.VirtualClone(CloneActualFlags.All);
                    actionAccessor = new AIOperationSimulatorAccessor(ai);
                    legalActions = LegalActionBuilder.Build(
                        ai,
                        virtualPair,
                        actionAccessor,
                        _settings.MaxCandidates,
                        null,
                        lethalPatterns);

                    // Play-pattern enumeration mutates its AIVirtualField and the original
                    // rollback processor does not restore every counter included in our hash.
                    // Rebuild from the untouched pair before serializing the decision state.
                    AIOperationSimulatorAccessor snapshotAccessor = new AIOperationSimulatorAccessor(ai);
                    snapshotAccessor.UpdateCurrentField(virtualPair, EnemyAI.EmptyPlayPtn);
                    LLMStateBuilder.SynchronizeResources(snapshotAccessor.CurrentField, virtualPair);
                    snapshot = LLMStateBuilder.BuildSnapshot(
                        virtualPair,
                        snapshotAccessor.CurrentField,
                        legalActions,
                        continuationGoal == null
                            ? null
                            : new PlanContinuationDto
                            {
                                Goal = continuationGoal,
                                ExecutedSteps = new List<TurnPlanStep>(executedSteps),
                                ReplanReason = replanReason
                            });
                }
                catch (Exception exception)
                {
                    Fallback(ai, useWait, "state_build_failed:" + Safe(exception.Message), planNumber, null);
                    yield break;
                }

                LLMDecisionLog.TurnStart(
                    ai.PlayerPair.Self.Turn,
                    snapshot.StateHash,
                    legalActions.Count,
                    continuationGoal);

                TurnPlan plan = null;
                CompiledTurnPlan compiled = null;
                bool forcedPlan = false;
                if (legalActions.Count == 1)
                {
                    forcedPlan = true;
                    plan = CreateForcedPlan(snapshot.StateHash, legalActions[0]);
                    planNumber++;
                    if (!LLMPlanCompiler.TryCompile(
                            ai,
                            plan,
                            _settings,
                            out compiled,
                            out string forcedReason,
                            out string forcedStep))
                    {
                        Fallback(ai, useWait, "forced_action_invalid:" + forcedReason, planNumber, forcedStep);
                        yield break;
                    }
                }
                else if (_settings.LethalSearchMaxPatterns > 0 && _settings.LethalSearchBudgetMs > 0)
                {
                    yield return TryFindLocalLethal(
                        ai,
                        snapshot.StateHash,
                        lethalPatterns,
                        _settings,
                        (foundPlan, foundCompiled) =>
                        {
                            plan = foundPlan;
                            compiled = foundCompiled;
                        });
                }

                if (compiled != null)
                {
                    if (!forcedPlan)
                    {
                        planNumber++;
                    }
                }
                else
                {
                    if (apiCalls >= _settings.MaxApiCallsPerTurn)
                    {
                        Fallback(ai, useWait, $"api_call_limit:{apiCalls}", planNumber, null);
                        yield break;
                    }
                    LLMApiResult apiResult = null;
                    apiCalls++;
                    LLMDecisionLog.Request(
                        ai.PlayerPair.Self.Turn,
                        apiCalls,
                        snapshot.StateHash,
                        legalActions.Count,
                        continuationGoal);
                    yield return LLMOpenAIClient.RequestPlan(
                        _settings,
                        snapshot,
                        request => active.Request = request,
                        result => apiResult = result);
                    active.Request = null;
                    if (active.Cancelled)
                    {
                        yield break;
                    }
                    if (apiResult == null || !apiResult.Success)
                    {
                        string error = apiResult == null
                            ? "request_cancelled"
                            : $"http_{apiResult.HttpStatus}:{Safe(apiResult.Error)}";
                        Fallback(ai, useWait, error, planNumber, null);
                        yield break;
                    }
                    LLMDecisionLog.Response(
                        ai.PlayerPair.Self.Turn,
                        apiCalls,
                        apiResult,
                        _settings.ReasoningEffort,
                        DescribeUsage(apiResult.Usage));

                    planNumber++;
                    if (!TurnPlanParser.TryParse(
                            apiResult.Content,
                            _settings.MaxPlanSteps,
                            out plan,
                            out string parseReason))
                    {
                        Fallback(ai, useWait, parseReason, planNumber, null);
                        yield break;
                    }
                    if (!string.Equals(plan.StateHash, snapshot.StateHash, StringComparison.Ordinal))
                    {
                        Fallback(ai, useWait, "response_state_hash_mismatch", planNumber, null);
                        yield break;
                    }

                    if (!LLMPlanCompiler.TryCompile(
                            ai,
                            plan,
                            _settings,
                            out compiled,
                            out string compileReason,
                            out string failedStep))
                    {
                        Fallback(ai, useWait, compileReason, planNumber, failedStep);
                        yield break;
                    }
                }

                if (!forcedPlan)
                {
                    continuationGoal = plan.Goal;
                }
                LLMDecisionLog.Plan(ai.PlayerPair.Self.Turn, planNumber, plan, compiled);

                bool shouldReplan = false;
                for (int stepIndex = 0; stepIndex < compiled.Steps.Count; stepIndex++)
                {
                    CompiledPlanStep step = compiled.Steps[stepIndex];
                    if (active.Cancelled || ai.BattleMgr.IsBattleEnd)
                    {
                        yield break;
                    }

                    LLMDecisionLog.Execute(
                        ai.PlayerPair.Self.Turn,
                        planNumber,
                        stepIndex + 1,
                        compiled.Steps.Count,
                        step.Source);

                    if (step.Candidate.Type == "turn_end")
                    {
                        ActiveTurns.Remove(ai);
                        try
                        {
                            ai.TurnEnd();
                            LLMDecisionLog.TurnEnded(
                                ai.PlayerPair.Self.Turn,
                                planNumber,
                                step.Source.StepId);
                        }
                        catch (Exception exception)
                        {
                            Fallback(ai, useWait, "turn_end_failed:" + Safe(exception.Message), planNumber, step.Source.StepId);
                        }
                        yield break;
                    }

                    try
                    {
                        ExecuteStep(ai, step.Candidate);
                    }
                    catch (Exception exception)
                    {
                        Fallback(
                            ai,
                            useWait,
                            "operation_enqueue_failed:" + Safe(exception.Message),
                            planNumber,
                            step.Source.StepId);
                        yield break;
                    }
                    stateChanged = true;
                    bool operationSettled = false;
                    yield return DrainOperation(ai, active, settled => operationSettled = settled);
                    if (!operationSettled && !active.Cancelled)
                    {
                        replanReason = "operation_settlement_timeout:" + step.Source.StepId;
                        LLMDecisionLog.Replan(
                            ai.PlayerPair.Self.Turn,
                            planNumber,
                            step.Source.StepId,
                            "operation settlement timed out");
                        shouldReplan = true;
                        break;
                    }
                    if (active.Cancelled || ai.BattleMgr.IsBattleEnd)
                    {
                        yield break;
                    }

                    string actualHash;
                    string actualSummary;
                    try
                    {
                        ai.UpdateAICurrentVirtualField(false);
                        actualHash = LLMStateBuilder.Hash(ai.CurrentVirtualField, ai.PlayerPair);
                        actualSummary = LLMStateBuilder.Describe(ai.CurrentVirtualField, ai.PlayerPair);
                    }
                    catch (Exception exception)
                    {
                        replanReason = "actual_state_refresh_failed:" + Safe(exception.Message);
                        LLMDecisionLog.Replan(
                            ai.PlayerPair.Self.Turn,
                            planNumber,
                            step.Source.StepId,
                            "actual state refresh failed: " + Safe(exception.Message));
                        shouldReplan = true;
                        break;
                    }

                    executedSteps.Add(step.Source);
                    if (!string.Equals(actualHash, step.ExpectedStateHash, StringComparison.Ordinal))
                    {
                        replanReason = "state_diverged_after:" + step.Source.StepId;
                        LLMDecisionLog.Replan(
                            ai.PlayerPair.Self.Turn,
                            planNumber,
                            step.Source.StepId,
                            "predicted state differs from the resolved battle state",
                            step.ExpectedStateHash,
                            actualHash);
                        LLMDecisionLog.Debug(
                            "STATE",
                            "predicted=" + SafePayload(step.ExpectedStateSummary) +
                            " actual=" + SafePayload(actualSummary));
                        shouldReplan = true;
                        break;
                    }
                    LLMDecisionLog.StepPassed(step.Source.StepId, actualHash);
                    if (step.Source.ReplanAfter)
                    {
                        replanReason = "model_replan_after:" + step.Source.StepId;
                        LLMDecisionLog.Replan(
                            ai.PlayerPair.Self.Turn,
                            planNumber,
                            step.Source.StepId,
                            "action revealed hidden information or requires fresh legal actions");
                        shouldReplan = true;
                        break;
                    }
                }

                if (!shouldReplan)
                {
                    replanReason = "plan_exhausted_without_turn_end";
                }
            }

            if (!active.Cancelled && ai.BattleMgr != null && !ai.BattleMgr.IsBattleEnd)
            {
                Fallback(ai, useWait, stateChanged ? replanReason : "controller_stopped", planNumber, null);
            }
        }

        private static TurnPlan CreateForcedPlan(string stateHash, LegalActionCandidate action)
        {
            return new TurnPlan
            {
                StateHash = stateHash,
                Goal = "forced",
                Reason = "Only one legal action is available; model bypassed.",
                Steps = new List<TurnPlanStep>
                {
                    new TurnPlanStep
                    {
                        StepId = "forced_" + (action.Type ?? "action"),
                        Type = action.Type,
                        Actor = action.ActorRef,
                        Mode = action.Mode,
                        Targets = new List<string>(action.TargetRefs ?? new List<string>()),
                        ReplanAfter = action.RequiresReplanAfter
                    }
                }
            };
        }

        private static IEnumerator TryFindLocalLethal(
            EnemyAI ai,
            string stateHash,
            List<AISinglePlayptnRecord> patterns,
            LLMAISettings settings,
            Action<TurnPlan, CompiledTurnPlan> onFound)
        {
            List<AISinglePlayptnRecord> ordered = (patterns ?? new List<AISinglePlayptnRecord>())
                .Where(record => record != null && record.IsValid)
                .OrderBy(record => record.PlayPtnCount == 0 ? 0 : 1)
                .Take(settings.LethalSearchMaxPatterns)
                .ToList();
            if (ordered.Count == 0)
            {
                yield break;
            }

            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            int checkedPatterns = 0;
            foreach (AISinglePlayptnRecord pattern in ordered)
            {
                if (timer.ElapsedMilliseconds >= settings.LethalSearchBudgetMs)
                {
                    break;
                }
                checkedPatterns++;
                ai.OutputLethalPlan = null;
                IEnumerator simulation;
                try
                {
                    simulation = new AILethalSimulator(ai).SimulateByAISimulator(pattern);
                }
                catch (Exception exception)
                {
                    LLMDecisionLog.Warning(
                        "LETHAL",
                        "search setup failed; model planning continues: " + Safe(exception.Message));
                    yield break;
                }

                bool completed = false;
                string failure = null;
                yield return RunBudgeted(
                    simulation,
                    timer,
                    settings.LethalSearchBudgetMs,
                    (done, error) =>
                    {
                        completed = done;
                        failure = error;
                    });
                if (failure != null)
                {
                    LLMDecisionLog.Warning(
                        "LETHAL",
                        "simulation failed; model planning continues: " + Safe(failure));
                    ai.OutputLethalPlan = null;
                    yield break;
                }
                if (!completed)
                {
                    LLMDecisionLog.Warning(
                        "LETHAL",
                        $"search budget reached after {checkedPatterns}/{ordered.Count} patterns " +
                        $"({timer.ElapsedMilliseconds}ms); model planning continues");
                    ai.OutputLethalPlan = null;
                    yield break;
                }

                AILethalPlan lethal = ai.OutputLethalPlan;
                ai.OutputLethalPlan = null;
                if (lethal == null || !lethal.IsSuccess || lethal.ActionSequence == null ||
                    lethal.ActionSequence.Count == 0 || lethal.ActionSequence.Count > settings.MaxPlanSteps)
                {
                    continue;
                }
                if (!TryMapLethalPlan(stateHash, pattern, lethal, out TurnPlan plan, out string mapReason))
                {
                    LLMDecisionLog.Warning("LETHAL", "mapping rejected: " + Safe(mapReason));
                    continue;
                }
                if (LLMPlanCompiler.TryCompile(
                        ai, plan, settings, out CompiledTurnPlan compiled,
                        out string compileReason, out string failedStep))
                {
                    LLMDecisionLog.Lethal(
                        ai.PlayerPair.Self.Turn,
                        checkedPatterns,
                        timer.ElapsedMilliseconds,
                        compiled.Steps.Count,
                        stateHash);
                    onFound(plan, compiled);
                    yield break;
                }
                LLMDecisionLog.Warning(
                    "LETHAL",
                    $"verification rejected at {failedStep ?? "-"}: {Safe(compileReason)}");
            }

            LLMDecisionLog.Debug(
                "LETHAL",
                $"none found; patterns={checkedPatterns}/{ordered.Count} elapsed={timer.ElapsedMilliseconds}ms");
        }

        private static IEnumerator RunBudgeted(
            IEnumerator root,
            System.Diagnostics.Stopwatch timer,
            int budgetMs,
            Action<bool, string> onComplete)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                if (timer.ElapsedMilliseconds >= budgetMs)
                {
                    onComplete(false, null);
                    yield break;
                }
                IEnumerator current = stack.Peek();
                bool moved;
                object yielded = null;
                try
                {
                    moved = current.MoveNext();
                    if (moved)
                    {
                        yielded = current.Current;
                    }
                }
                catch (Exception exception)
                {
                    onComplete(false, exception.Message);
                    yield break;
                }
                if (!moved)
                {
                    stack.Pop();
                    continue;
                }
                if (yielded is IEnumerator nested)
                {
                    stack.Push(nested);
                }
                else
                {
                    yield return yielded;
                }
            }
            onComplete(true, null);
        }

        private static bool TryMapLethalPlan(
            string stateHash,
            AISinglePlayptnRecord pattern,
            AILethalPlan lethal,
            out TurnPlan plan,
            out string reason)
        {
            plan = new TurnPlan
            {
                StateHash = stateHash,
                Goal = "lethal",
                Reason = "locally_verified_lethal",
                Steps = new List<TurnPlanStep>()
            };
            reason = null;
            for (int i = 0; i < lethal.ActionSequence.Count; i++)
            {
                AISituationInfo action = lethal.ActionSequence[i];
                TurnPlanStep step = new TurnPlanStep
                {
                    StepId = "lethal_" + (i + 1),
                    Actor = LLMStateBuilder.CardRef(action.OriginalCard),
                    Targets = new List<string>(),
                    ReplanAfter = false
                };
                switch (action.ActionType)
                {
                    case AIOperationType.ATTACK:
                        step.Type = "attack";
                        AIVirtualAttackInfo attack = action as AIVirtualAttackInfo;
                        if (attack?.AttackTarget == null)
                        {
                            reason = "attack_missing_target";
                            return false;
                        }
                        step.Targets.Add(LLMStateBuilder.CardRef(attack.AttackTarget));
                        break;
                    case AIOperationType.PLAY:
                        step.Type = "play";
                        PlayedCardInfo played = pattern?.FindPlayedCardInfo(action.OriginalCard);
                        PlaySimulationType playType = played?.PlayType ?? PlaySimulationType.Normal;
                        step.Mode = LegalActionBuilder.Mode(playType);
                        step.Targets = LegalActionBuilder.OrderedTargets(action.SelectedTargets)
                            .Select(LLMStateBuilder.CardRef)
                            .ToList();
                        break;
                    case AIOperationType.EVOLVE:
                        step.Type = "evolve";
                        step.Targets = LegalActionBuilder.OrderedTargets(action.SelectedTargets)
                            .Select(LLMStateBuilder.CardRef)
                            .ToList();
                        break;
                    default:
                        reason = "unsupported_action:" + action.ActionType;
                        return false;
                }
                if (string.IsNullOrWhiteSpace(step.Actor))
                {
                    reason = "missing_actor";
                    return false;
                }
                plan.Steps.Add(step);
            }
            return true;
        }

        private static void ExecuteStep(EnemyAI ai, LegalActionCandidate candidate)
        {
            if (candidate.Type == "attack")
            {
                ai.OprAttack(candidate.Action);
                return;
            }
            AIVirtualTargetSelectAction action = (AIVirtualTargetSelectAction)candidate.Action;
            ai.OprTargetSelect(action.OriginalCard, action.SelectedTargets, action.ActionType);
        }

        private static IEnumerator DrainOperation(EnemyAI ai, ActiveTurn active, Action<bool> onComplete)
        {
            float timeout = Math.Max(15f, _settings.TimeoutSeconds * 2f);
            float elapsed = 0f;
            int settledFrames = 0;
            while (!active.Cancelled && ai.BattleMgr != null && !ai.BattleMgr.IsBattleEnd)
            {
                bool vfxEnded = ai.BattleMgr.VfxMgr != null && ai.BattleMgr.VfxMgr.IsEnd;
                if (vfxEnded && ai.AIOperationQueue.Count > 0)
                {
                    try
                    {
                        ai.ExecuteActionOperationQueue();
                    }
                    catch (Exception exception)
                    {
                        LLMDecisionLog.Error("OPERATION", "queue execution failed: " + Safe(exception.Message));
                        LLMDecisionLog.Debug("OPERATION", exception.ToString());
                        onComplete(false);
                        yield break;
                    }
                    settledFrames = 0;
                }
                else if (vfxEnded && ai.AIOperationQueue.Count == 0)
                {
                    settledFrames++;
                    if (settledFrames >= 2)
                    {
                        onComplete(true);
                        yield break;
                    }
                }
                else
                {
                    settledFrames = 0;
                }

                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= timeout)
                {
                    onComplete(false);
                    yield break;
                }
                yield return null;
            }
            onComplete(false);
        }

        private static void Fallback(EnemyAI ai, bool useWait, string reason, int planNumber, string stepId)
        {
            ActiveTurns.Remove(ai);
            string step = string.IsNullOrEmpty(stepId) ? "-" : stepId;
            LLMDecisionLog.Fallback(
                ai?.PlayerPair?.Self?.Turn ?? -1,
                planNumber,
                step,
                Safe(reason));
            if (ai == null || ai.BattleMgr == null || ai.BattleMgr.IsBattleEnd)
            {
                return;
            }
            OriginalAIBypass.Add(ai);
            ai.ExecuteEnemyAI(useWait);
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown_error";
            }
            value = value.Replace('\r', ' ').Replace('\n', ' ');
            return value.Length <= 300 ? value : value.Substring(0, 300);
        }

        private static string DescribeUsage(OpenAIUsage usage)
        {
            if (usage == null)
            {
                return "unknown";
            }
            int input = usage.InputTokens != 0 ? usage.InputTokens : usage.PromptTokens;
            int output = usage.OutputTokens != 0 ? usage.OutputTokens : usage.CompletionTokens;
            int total = usage.TotalTokens != 0 ? usage.TotalTokens : input + output;
            return $"input:{input},output:{output},total:{total}";
        }

        private static string SafePayload(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "empty";
            }
            value = value.Replace("\r", "\\r").Replace("\n", "\\n");
            return value.Length <= 12000 ? value : value.Substring(0, 12000) + "...[truncated]";
        }
    }

    internal static class LLMAIPatches
    {
        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.ExecuteEnemyAI))]
        [HarmonyPrefix]
        private static bool EnemyAI_ExecuteEnemyAI_Prefix(EnemyAI __instance, bool useWait)
        {
            return !LLMAITurnController.TryIntercept(__instance, useWait);
        }

        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.StopEnemyAI))]
        [HarmonyPrefix]
        private static void EnemyAI_StopEnemyAI_Prefix(EnemyAI __instance)
        {
            LLMAITurnController.Cancel(__instance, "StopEnemyAI");
        }

        [HarmonyPatch(typeof(SingleBattleMgr), nameof(SingleBattleMgr.FinishBattle))]
        [HarmonyPostfix]
        private static void SingleBattleMgr_FinishBattle_Postfix()
        {
            LLMAITurnController.CancelAll("battle_finished");
        }

        [HarmonyPatch(typeof(SingleBattleMgr), nameof(SingleBattleMgr.DisposeBattleGameObj))]
        [HarmonyPostfix]
        private static void SingleBattleMgr_DisposeBattleGameObj_Postfix()
        {
            LLMAITurnController.CancelAll("battle_disposed");
        }
    }
}
