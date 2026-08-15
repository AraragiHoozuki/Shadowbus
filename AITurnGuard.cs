using HarmonyLib;
using Shadowbus.LLMAI;
using System;
using UnityEngine;
using Wizard;

namespace Shadowbus
{
    /// <summary>
    /// Keeps a battle from stalling forever when the enemy AI stops making progress.
    ///
    /// The AI turn is driven by EnemyAI.EnemyAI_Move, a coroutine whose only way out of its
    /// main loop is to reach the turn end check at the bottom. Every dead end is a continue,
    /// and the game has no turn timer outside of ranked matches, so anything that keeps the
    /// loop from advancing hangs the battle on the AI's thinking animation.
    ///
    /// Two things are done about it:
    ///
    /// 1. EnemyAI_Play.CalcMostValuableHandPtn runs on a worker thread inside a job whose
    ///    catch block is empty. When it throws, the job leaves its result null, and
    ///    BattleAI_HandPlay then indexes that null array. The resulting exception kills the
    ///    coroutine, and the parent coroutine waiting on it never resumes. The finalizer below
    ///    turns that into an empty candidate list, which the game already handles as "play
    ///    nothing", and reports what actually threw.
    /// 2. A watchdog force ends the turn if the AI stops making progress altogether, which
    ///    covers the stalls that do not come from that one exception.
    /// </summary>
    public static class AITurnGuard
    {
        // Size of the play pattern candidate array that CalcMostValuableHandPtn allocates.
        private const int HandPlayCandidateCount = 6;

        private static float _stallTimeoutSeconds;

        private static EnemyAI _watchedAI;
        private static float _idleSeconds;
        private static int _lastActionCount;
        private static int _lastQueueCount;

        // CalcMostValuableHandPtn runs on a LeanThreadPool worker, so the exception is handed
        // to the main thread instead of being logged from there.
        private static readonly object SimulationExceptionLock = new object();
        private static Exception _pendingSimulationException;
        private static int _suppressedSimulationExceptions;

        internal static void Configure(float stallTimeoutSeconds)
        {
            _stallTimeoutSeconds = stallTimeoutSeconds;
            Plugin.Logger.LogInfo(
                _stallTimeoutSeconds > 0f
                    ? $"[AITurnGuard] Watching for AI turns that stop making progress for {_stallTimeoutSeconds:0.#} s."
                    : "[AITurnGuard] The stall watchdog is disabled; only the simulation guard is active.");
        }

        [HarmonyPatch(typeof(EnemyAI_Play), "CalcMostValuableHandPtn")]
        [HarmonyFinalizer]
        public static Exception EnemyAI_Play_CalcMostValuableHandPtn_Finalizer(
            Exception __exception,
            ref PlayPtnWithToken[] __result,
            ref AISinglePlayptnRecord playOutPlan)
        {
            if (__exception == null)
            {
                return null;
            }

            // BattleAI_HandPlay reads candidates[0] without a null check, so the array has to
            // exist. All-null entries make it fall through to "no card is worth playing".
            playOutPlan = null;
            __result = new PlayPtnWithToken[HandPlayCandidateCount];

            lock (SimulationExceptionLock)
            {
                _suppressedSimulationExceptions++;
                _pendingSimulationException ??= __exception;
            }

            // Swallow it: the caller's own catch block would have hidden it anyway.
            return null;
        }

        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.ExecuteEnemyAI))]
        [HarmonyPostfix]
        public static void EnemyAI_ExecuteEnemyAI_Postfix(EnemyAI __instance)
        {
            // Ranked matches already have their own 90 second turn timer.
            if (__instance == null || __instance.IsRankMatchAI)
            {
                return;
            }

            _watchedAI = __instance;
            _idleSeconds = 0f;
            _lastActionCount = -1;
            _lastQueueCount = -1;
        }

        [HarmonyPatch(typeof(OperateMgr), nameof(OperateMgr.TurnEndOperation))]
        [HarmonyPostfix]
        public static void OperateMgr_TurnEndOperation_Postfix(bool isPlayer)
        {
            // Both sides can now be driven by SoloBattleEnemyAI in custom practice.
            // A turn-end operation means the currently watched AI has finished regardless
            // of which side it controls; the next AI turn will register itself again.
            _watchedAI = null;
            _idleSeconds = 0f;
            _lastActionCount = -1;
            _lastQueueCount = -1;
        }

        internal static void Update()
        {
            ReportSimulationExceptions();
            UpdateStallWatchdog();
        }

        private static void ReportSimulationExceptions()
        {
            Exception exception;
            int count;
            lock (SimulationExceptionLock)
            {
                exception = _pendingSimulationException;
                count = _suppressedSimulationExceptions;
                _pendingSimulationException = null;
                _suppressedSimulationExceptions = 0;
            }

            if (exception == null)
            {
                return;
            }

            Plugin.Logger.LogError(
                $"[AITurnGuard] The AI hand play simulation threw {count} time(s); the AI played " +
                $"nothing instead of hanging the turn. First exception:\n{exception}");
        }

        private static void UpdateStallWatchdog()
        {
            EnemyAI ai = _watchedAI;
            if (ai == null || _stallTimeoutSeconds <= 0f)
            {
                return;
            }

            // Model requests and plan validation intentionally leave the original operation
            // queue idle. The LLM controller has its own HTTP and settlement timeouts.
            if (LLMAITurnController.IsControlling(ai))
            {
                _idleSeconds = 0f;
                _lastActionCount = ai.oprationQueueActCount;
                _lastQueueCount = ai.AIOperationQueue?.Count ?? 0;
                return;
            }

            BattleManagerBase battleMgr = ai.BattleMgr;
            if (battleMgr == null || battleMgr.IsBattleEnd)
            {
                _watchedAI = null;
                return;
            }

            int actionCount = ai.oprationQueueActCount;
            int queueCount = ai.AIOperationQueue?.Count ?? 0;
            bool isIdle = battleMgr.VfxMgr != null && battleMgr.VfxMgr.IsEnd;

            // Anything that moves counts as progress, including a long but healthy simulation
            // that keeps feeding the operation queue.
            if (!isIdle || actionCount != _lastActionCount || queueCount != _lastQueueCount)
            {
                _lastActionCount = actionCount;
                _lastQueueCount = queueCount;
                _idleSeconds = 0f;
                return;
            }

            _idleSeconds += Time.unscaledDeltaTime;
            if (_idleSeconds < _stallTimeoutSeconds)
            {
                return;
            }

            ForceTurnEnd(ai);
        }

        private static void ForceTurnEnd(EnemyAI ai)
        {
            _watchedAI = null;
            _idleSeconds = 0f;

            Plugin.Logger.LogWarning(
                $"[AITurnGuard] The enemy AI made no progress for {_stallTimeoutSeconds:0.#} s. " +
                $"Forcing its turn to end so the battle can continue.");

            try
            {
                // Stops the stuck AI coroutines and registers the same turn end operation the
                // AI would have run itself.
                ai.TurnEnd();
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError($"[AITurnGuard] Failed to force the AI turn to end.\n{exception}");
            }
        }
    }
}
