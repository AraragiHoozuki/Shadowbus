using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Wizard;
using Wizard.Battle.Mulligan;
using Wizard.Battle.View;
using Wizard.Battle.View.Vfx;

namespace Shadowbus
{
    /// <summary>
    /// Runs a second SoloBattleEnemyAI on the player side for the custom practice mode.
    /// The normal practice AI remains owned by SingleBattleMgr.EnemyAI; this class only
    /// activates when a custom practice session explicitly enables the player-side AI.
    /// </summary>
    internal static class PracticeDualAI
    {
        private sealed class PlayerAIConfiguration
        {
            public bool Enabled;
            public int ClassId;
            public AI_LOGIC_LV LogicLevel;
            public string DeckName;
            public string StyleName;
            public string EmoteName;
        }

        private static readonly FieldInfo PlayerCharaIdField =
            AccessTools.Field(typeof(EnemyAI), "<PlayerCharaId>k__BackingField");
        private static readonly FieldInfo AISubClassTypeField =
            AccessTools.Field(typeof(EnemyAI), "AISubClassType");
        private static readonly FieldInfo PlayerSubClassTypeField =
            AccessTools.Field(typeof(EnemyAI), "PlayerSubClassType");

        private static PlayerAIConfiguration _configuration;
        private static EnemyAI _playerAI;
        private static AIBattleInfoReceiver _playerBattleInfoReceiver;
        private static CanNotTouchCardVfx _playerAITouchGuard;
        private static bool _playerAITurnActive;
        private static bool _mulliganAutoSubmitAttached;

        internal static bool IsPlayerAIExecuting => _playerAITurnActive;

        internal static void Configure(
            bool enabled,
            int classId,
            AI_LOGIC_LV logicLevel,
            string deckName,
            string styleName,
            string emoteName)
        {
            _mulliganAutoSubmitAttached = false;
            _configuration = enabled
                ? new PlayerAIConfiguration
                {
                    Enabled = true,
                    ClassId = classId,
                    LogicLevel = logicLevel,
                    DeckName = deckName,
                    StyleName = styleName,
                    EmoteName = emoteName
                }
                : null;

            Plugin.Logger.LogInfo(
                enabled
                    ? $"[AIManager] Player-side AI configured: class={classId}, logic={logicLevel}, " +
                      $"deck='{deckName}', style='{styleName}', emote='{emoteName}'."
                    : "[AIManager] Player-side AI disabled for the next custom practice battle.");
        }

        internal static void ClearConfiguration()
        {
            StopPlayerAI();
            _configuration = null;
            _mulliganAutoSubmitAttached = false;
        }

        private static bool IsCustomPracticeBattle(BattleManagerBase battleMgr = null)
        {
            battleMgr ??= BattleManagerBase.GetIns();
            if (!(battleMgr is SingleBattleMgr) || battleMgr.IsBattleEnd)
            {
                return false;
            }

            DataMgr dataMgr = GameMgr.GetIns()?.GetDataMgr();
            return dataMgr != null &&
                   dataMgr.m_BattleType == DataMgr.BattleType.Practice &&
                   dataMgr.m_EnemyAIDeckId == int.MinValue;
        }

        private static bool IsEnabledForBattle(BattleManagerBase battleMgr = null)
        {
            return _configuration != null &&
                   _configuration.Enabled &&
                   IsCustomPracticeBattle(battleMgr);
        }

        internal static void TrySetupPlayerAI(SingleBattleMgr battleMgr)
        {
            if (!IsEnabledForBattle(battleMgr) ||
                _playerAI != null ||
                battleMgr == null ||
                battleMgr.BattlePlayer == null ||
                battleMgr.BattleEnemy == null)
            {
                return;
            }

            PlayerAIConfiguration configuration = _configuration;
            if (string.IsNullOrEmpty(configuration.DeckName) ||
                string.IsNullOrEmpty(configuration.StyleName) ||
                string.IsNullOrEmpty(configuration.EmoteName))
            {
                Plugin.Logger.LogError(
                    "[AIManager] Player-side AI was enabled but one or more AI CSV keys are empty.");
                return;
            }

            try
            {
                EnemyAI playerAI = new SoloBattleEnemyAI();
                // The normal SingleBattleMgr path calls this before InitOnGame. It binds the
                // AI to the current battle manager and creates the operation/event helpers;
                // without it the second AI can be configured successfully but never owns a
                // live battle.
                playerAI.LoadBufferedBattleState();
                if (!playerAI.SetUpBattleState(
                        configuration.ClassId,
                        configuration.LogicLevel,
                        configuration.DeckName,
                        configuration.StyleName,
                        configuration.EmoteName,
                        -1))
                {
                    Plugin.Logger.LogError("[AIManager] Player-side AI SetUpBattleState returned false.");
                    return;
                }

                playerAI.InitOnGame(battleMgr.BattlePlayer, battleMgr.BattleEnemy);
                FixPlayerSideScriptContext(playerAI);
                if (!playerAI.IsRankMatchAI)
                {
                    playerAI.EmoteQuery.SetOnOffEmote(true, true);
                    playerAI.EmoteCtrl().SetUpEmoteEvent(
                        battleMgr.BattlePlayer,
                        battleMgr.BattleEnemy,
                        battleMgr.OperateMgr);
                }

                _playerAI = playerAI;
                _playerBattleInfoReceiver = new AIBattleInfoReceiver(playerAI);
                Plugin.Logger.LogInfo(
                    $"[AIManager] Player-side AI initialized for custom practice: " +
                    $"class={configuration.ClassId}, deck='{configuration.DeckName}', " +
                    $"style='{configuration.StyleName}', emote='{configuration.EmoteName}'.");
            }
            catch (Exception exception)
            {
                _playerAI = null;
                _playerBattleInfoReceiver = null;
                Plugin.Logger.LogError(
                    $"[AIManager] Failed to initialize the player-side AI.\n{exception}");
            }
        }

        private static void FixPlayerSideScriptContext(EnemyAI playerAI)
        {
            DataMgr dataMgr = GameMgr.GetIns().GetDataMgr();
            SetBackingField(PlayerCharaIdField, playerAI, dataMgr.GetEnemyCharaId());
            SetBackingField(
                AISubClassTypeField,
                playerAI,
                (CardBasePrm.ClanType)dataMgr.GetPlayerSubClassId());
            SetBackingField(
                PlayerSubClassTypeField,
                playerAI,
                (CardBasePrm.ClanType)dataMgr.GetEnemySubClassId());
            playerAI.StyleQuery.UpdateStyle();
        }

        private static void SetBackingField(FieldInfo field, EnemyAI target, object value)
        {
            if (field == null)
            {
                return;
            }

            field.SetValue(target, value);
        }

        private static bool IsCurrentPlayerAI(BattleManagerBase battleMgr)
        {
            return IsEnabledForBattle(battleMgr) &&
                   _playerAI != null &&
                   ReferenceEquals(_playerAI.BattleMgr, battleMgr);
        }

        /// <summary>
        /// Returns true only while the player-side AI is actively consuming the local
        /// player's turn. This is intentionally tied to the AI's battle manager so a
        /// stale configuration cannot disable controls in another scene.
        /// </summary>
        internal static bool IsPlayerAITurnFor(BattleManagerBase battleMgr)
        {
            if (!IsEnabledForBattle(battleMgr) ||
                _playerAI == null ||
                battleMgr == null ||
                !ReferenceEquals(_playerAI.BattleMgr, battleMgr))
            {
                return false;
            }

            if (_playerAITurnActive)
            {
                return true;
            }

            // TurnEndOperation normally clears the active flag synchronously, while the
            // battle player may still report the old self-turn for the remainder of that
            // frame. Treat that short transition as AI-owned as long as the player is still
            // inactive; this closes the input window opened by late VFX callbacks.
            BattlePlayer battlePlayer = battleMgr.BattlePlayer;
            return battlePlayer != null && battlePlayer.IsSelfTurn && !battlePlayer._isPlayerActive &&
                   !battleMgr.IsBattleEnd;
        }

        /// <summary>
        /// The base game can recreate/show the turn-end button from several VFX callbacks
        /// after SetActive has already returned. Keep the player inactive and the button
        /// hidden for the whole AI turn, not only at its first frame.
        /// </summary>
        internal static void Update()
        {
            if (_playerAI == null)
            {
                return;
            }

            BattleManagerBase battleMgr = _playerAI.BattleMgr;
            if (!IsPlayerAITurnFor(battleMgr))
            {
                return;
            }

            BattlePlayer battlePlayer = battleMgr.BattlePlayer;
            if (battlePlayer == null)
            {
                return;
            }

            battlePlayer._isPlayerActive = false;
            battlePlayer.BattleView?.HideTurnEndButton();
            battlePlayer.BattleView?.SetTouchable(false);
            if (battlePlayer.BattleView?.TurnEndBtn != null)
            {
                battlePlayer.BattleView.TurnEndBtn.SetActive(false);
            }
        }

        private static bool IsPlayerAITurnView(BattlePlayerView view)
        {
            BattleManagerBase battleMgr = BattleManagerBase.GetIns();
            return view != null &&
                   IsPlayerAITurnFor(battleMgr) &&
                   battleMgr.BattlePlayer != null &&
                   ReferenceEquals(battleMgr.BattlePlayer.PlayerBattleView, view);
        }

        private static void StartPlayerAITurn(BattlePlayer battlePlayer)
        {
            BattleManagerBase battleMgr = battlePlayer?.BattleMgr;
            if (!IsCurrentPlayerAI(battleMgr) ||
                _playerAITurnActive ||
                !battlePlayer.IsSelfTurn ||
                battleMgr.IsBattleEnd)
            {
                return;
            }

            _playerAITurnActive = true;
            battlePlayer._isPlayerActive = false;
            battlePlayer.BattleView.HideTurnEndButton();
            _playerAITouchGuard = new CanNotTouchCardVfx(true, false);
            battleMgr.VfxMgr.RegisterImmediateVfx<CanNotTouchCardVfx>(_playerAITouchGuard);

            try
            {
                _playerAI.ExecuteEnemyAI(true);
                Plugin.Logger.LogInfo(
                    $"[AIManager] Started player-side AI turn {battlePlayer.Turn}.");
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError(
                    $"[AIManager] Failed to start player-side AI turn.\n{exception}");
                EndPlayerAITurn();
                battleMgr.OperateMgr.TurnEndOperation(true);
            }
        }

        private static void EndPlayerAITurn()
        {
            _playerAITurnActive = false;
            if (_playerAITouchGuard != null)
            {
                _playerAITouchGuard.End(false);
                _playerAITouchGuard = null;
            }
        }

        private static void StopPlayerAI()
        {
            EndPlayerAITurn();
            if (_playerAI != null)
            {
                try
                {
                    _playerAI.StopEnemyAI();
                }
                catch (Exception exception)
                {
                    Plugin.Logger.LogWarning(
                        $"[AIManager] Failed to stop player-side AI cleanly: {exception.Message}");
                }
            }

            _playerAI = null;
            _playerBattleInfoReceiver = null;
        }

        private static void AutoSubmitPlayerMulligan(PlayerMulliganCtrl mulliganCtrl)
        {
            if (_mulliganAutoSubmitAttached)
            {
                return;
            }

            BattleManagerBase battleMgr = BattleManagerBase.GetIns();
            if (!IsCurrentPlayerAI(battleMgr))
            {
                return;
            }

            _mulliganAutoSubmitAttached = true;

            try
            {
                List<BattleCardBase> abandonCards = new List<BattleCardBase>();
                _playerAI.Mulligan(
                    abandonCards,
                    battleMgr.BattlePlayer,
                    battleMgr.BattleEnemy);
                foreach (BattleCardBase card in abandonCards)
                {
                    mulliganCtrl.RegisterAbandonCard(card);
                }

                battleMgr.MulliganSubmit();
                Plugin.Logger.LogInfo(
                    $"[AIManager] Player-side AI submitted mulligan with {abandonCards.Count} card(s) changed.");
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError(
                    $"[AIManager] Player-side AI mulligan failed; submitting no-change mulligan.\n{exception}");
                battleMgr.MulliganSubmit();
            }
        }

        [HarmonyPatch(typeof(SingleBattleMgr), nameof(SingleBattleMgr.SetupEnemyAI))]
        [HarmonyPostfix]
        private static void SingleBattleMgr_SetupEnemyAI_Postfix(SingleBattleMgr __instance)
        {
            TrySetupPlayerAI(__instance);
        }

        [HarmonyPatch(typeof(BattlePlayer), "SetActive")]
        [HarmonyPostfix]
        private static void BattlePlayer_SetActive_Postfix(BattlePlayer __instance)
        {
            if (__instance?.BattleMgr is SingleBattleMgr battleMgr)
            {
                // SetupEnemyAI can run before the battle players have been created. Retry at
                // the first real player turn, when both sides and their decks are guaranteed
                // to exist.
                TrySetupPlayerAI(battleMgr);
            }
            StartPlayerAITurn(__instance);
        }

        [HarmonyPatch(typeof(BattlePlayer), "EnableBattleMenu")]
        [HarmonyPrefix]
        private static bool BattlePlayer_EnableBattleMenu_Prefix()
        {
            return !IsPlayerAITurnFor(BattleManagerBase.GetIns());
        }

        [HarmonyPatch(typeof(BattleButtonControl), nameof(BattleButtonControl.OnPressTurnEnd))]
        [HarmonyPrefix]
        private static bool BattleButtonControl_OnPressTurnEnd_Prefix()
        {
            // This is the final input path used by the visible turn-end button. Keep the
            // guard even if a stale collider remains active for one frame.
            return !IsPlayerAITurnFor(BattleManagerBase.GetIns());
        }

        [HarmonyPatch(typeof(BattlePlayerView), nameof(BattlePlayerView.ShowTurnEndButton))]
        [HarmonyPrefix]
        private static bool BattlePlayerView_ShowTurnEndButton_Prefix(BattlePlayerView __instance)
        {
            return !IsPlayerAITurnView(__instance);
        }

        [HarmonyPatch(typeof(BattlePlayerView), nameof(BattlePlayerView.ForceShowTurnEndButton))]
        [HarmonyPrefix]
        private static bool BattlePlayerView_ForceShowTurnEndButton_Prefix(BattlePlayerView __instance)
        {
            return !IsPlayerAITurnView(__instance);
        }

        [HarmonyPatch(typeof(TurnEndButtonUI), nameof(TurnEndButtonUI.ShowBtn))]
        [HarmonyPrefix]
        private static bool TurnEndButtonUI_ShowBtn_Prefix()
        {
            return !IsPlayerAITurnFor(BattleManagerBase.GetIns());
        }

        [HarmonyPatch(typeof(TurnEndButtonUI), nameof(TurnEndButtonUI.StartTurnEndCountdown))]
        [HarmonyPrefix]
        private static bool TurnEndButtonUI_StartTurnEndCountdown_Prefix()
        {
            return !IsPlayerAITurnFor(BattleManagerBase.GetIns());
        }

        [HarmonyPatch(typeof(SingleBattleMgr), nameof(SingleBattleMgr.Update))]
        [HarmonyPostfix]
        private static void SingleBattleMgr_Update_Postfix(SingleBattleMgr __instance)
        {
            // This also covers the opening/mulligan window, where SetActive has not fired yet.
            TrySetupPlayerAI(__instance);
        }

        [HarmonyPatch(typeof(PlayerMulliganCtrl), nameof(PlayerMulliganCtrl.StartMulliganVfx))]
        [HarmonyPostfix]
        private static void PlayerMulliganCtrl_StartMulliganVfx_Postfix(PlayerMulliganCtrl __instance)
        {
            if (!IsEnabledForBattle() || __instance == null)
            {
                return;
            }

            if (BattleManagerBase.GetIns() is SingleBattleMgr battleMgr)
            {
                TrySetupPlayerAI(battleMgr);
            }

            __instance.OnMulliganLaunchComplete += () => AutoSubmitPlayerMulligan(__instance);
        }

        [HarmonyPatch(typeof(OperateMgr), nameof(OperateMgr.TurnEndOperation))]
        [HarmonyPostfix]
        private static void OperateMgr_TurnEndOperation_Postfix(bool isPlayer)
        {
            // The AI operation pipeline normally reports true for the player side, but some
            // turn-end paths use the local-view flag instead. Any real turn-end operation
            // while this AI is active must release the guard so the next player turn can run.
            if (_playerAITurnActive)
            {
                EndPlayerAITurn();
            }
        }

        [HarmonyPatch(typeof(SingleBattleMgr), nameof(SingleBattleMgr.FinishBattle))]
        [HarmonyPostfix]
        private static void SingleBattleMgr_FinishBattle_Postfix()
        {
            StopPlayerAI();
        }

        [HarmonyPatch(typeof(SingleBattleMgr), nameof(SingleBattleMgr.DisposeBattleGameObj))]
        [HarmonyPostfix]
        private static void SingleBattleMgr_DisposeBattleGameObj_Postfix()
        {
            StopPlayerAI();
        }

        [HarmonyPatch(
            typeof(BattleManagerBase),
            nameof(BattleManagerBase.IsVirtualBattleEnemyTurn),
            MethodType.Getter)]
        [HarmonyPrefix]
        private static bool BattleManagerBase_IsVirtualBattleEnemyTurn_Prefix(
            BattleManagerBase __instance,
            ref bool __result)
        {
            if (_playerAITurnActive &&
                _playerAI != null &&
                ReferenceEquals(_playerAI.BattleMgr, __instance))
            {
                __result = true;
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(SingleSkill_attach_skill), nameof(SingleSkill_attach_skill.GiveSkill))]
        [HarmonyPostfix]
        private static void SingleSkill_attach_skill_GiveSkill_Postfix(
            SingleSkill_attach_skill __instance,
            List<BattleCardBase> targets)
        {
            if (_playerBattleInfoReceiver == null ||
                BattleManagerBase.IsForecast ||
                targets == null ||
                !IsCurrentPlayerAI(BattleManagerBase.GetIns()))
            {
                return;
            }

            try
            {
                foreach (BattleCardBase target in targets)
                {
                    if (target != null && __instance?.SkillPrm?.ownerCard != null)
                    {
                        _playerBattleInfoReceiver.ReceiveAttachedSkillInfo(
                            __instance.SkillPrm.ownerCard,
                            target);
                    }
                }
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning(
                    $"[AIManager] Failed to forward an attached skill to the player-side AI: " +
                    exception.Message);
            }
        }
    }
}
