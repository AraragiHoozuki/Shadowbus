using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Wizard;

namespace Shadowbus.LLMAI
{
    internal sealed class CandidateOverflowException : Exception
    {
        internal CandidateOverflowException(int limit)
            : base("candidate_overflow:" + limit)
        {
        }
    }

    internal sealed class LegalActionCandidate
    {
        public string Type;
        public string ActorRef;
        public string Mode;
        public List<string> TargetRefs;
        public AIVirtualActionInfo Action;
        public AISinglePlayptnRecord PlayRecord;
        public int PpCost;
        public int PpAfter;
        public int EpCost;
        public int DrawCount;
        public bool RevealsHiddenInformation;
        public bool RequiresReplanAfter;

        internal LegalActionDto ToDto()
        {
            return new LegalActionDto
            {
                Type = Type,
                Actor = ActorRef,
                Mode = Mode,
                Targets = new List<string>(TargetRefs ?? new List<string>()),
                PpCost = PpCost,
                PpAfter = PpAfter,
                EpCost = EpCost,
                DrawCount = DrawCount,
                RevealsHiddenInformation = RevealsHiddenInformation,
                RequiresReplanAfter = RequiresReplanAfter
            };
        }

        internal string Signature()
        {
            return string.Join("|", Type ?? string.Empty, ActorRef ?? string.Empty, Mode ?? string.Empty,
                string.Join(",", TargetRefs ?? new List<string>()));
        }
    }

    internal static class LLMStateBuilder
    {
        internal static BattleSnapshotDto BuildSnapshot(
            BattlePlayerPair pair,
            AIVirtualField field,
            List<LegalActionCandidate> candidates,
            PlanContinuationDto continuation)
        {
            return new BattleSnapshotDto
            {
                StateHash = Hash(field, pair),
                Turn = pair.Self.Turn,
                Self = BuildPlayer(field, pair.Self, true),
                Opponent = BuildPlayer(field, pair.Opponent, false),
                LegalActions = candidates.Select(candidate => candidate.ToDto()).ToList(),
                Continuation = continuation
            };
        }

        internal static void SynchronizeResources(AIVirtualField field, BattlePlayerPair pair)
        {
            if (field == null || pair?.Self == null || pair.Opponent == null)
            {
                return;
            }

            // AIVirtualField.CreateTemporaryVirtualField reads these values from the real
            // EnemyAI players even when it is built from a simulated BattlePlayerPair.
            field.AllyPp = pair.Self.Pp;
            field.AllyPpTotal = pair.Self.PpTotal;
            field.EnemyPp = pair.Opponent.Pp;
            field.EnemyPpTotal = pair.Opponent.PpTotal;
            field.AllyEvolutionCount = pair.Self.CurrentEpCount;
            field.EnemyEvolutionCount = pair.Opponent.CurrentEpCount;
        }

        private static PlayerSnapshotDto BuildPlayer(AIVirtualField field, BattlePlayerBase player, bool self)
        {
            AIVirtualCard leader = self ? field.AllyClass : field.EnemyClass;
            List<AIVirtualCard> board = self ? field.AllyInplayCards : field.EnemyInplayCards;
            List<AIVirtualCard> hand = self ? field.AllyHandCards : null;
            return new PlayerSnapshotDto
            {
                Turn = player.Turn,
                Life = leader.Life,
                Pp = self ? field.AllyPp : field.EnemyPp,
                PpTotal = self ? field.AllyPpTotal : field.EnemyPpTotal,
                Ep = player.CurrentEpCount,
                EpTotal = player.EpTotal,
                EpUsedGame = player.GameUsedEpCount,
                EpUsedTurn = player.TurnUsedEpCount,
                CanEvolve = player.IsEvolve || player.IsExceptionEvolve,
                DeckCount = player.DeckCardList?.Count ?? 0,
                DeckSummary = self ? BuildDeckSummary(player.DeckCardList) : null,
                HandCount = player.HandCardList?.Count ?? 0,
                Hand = hand?.Select(CardSnapshot).ToList(),
                Board = board.Where(card => !card.IsDead).Select(CardSnapshot).ToList(),
                CemeteryCount = player.CemeteryList?.Count ?? 0,
                EvolvedGame = self ? field.AllyEvolvedCountInGame : field.EnemyEvolvedCountInGame,
                EvolvedPreviousTurn = self ? field.AllyEvolvedCountInPreviousTurn : field.EnemyEvolvedCountInPreviousTurn,
                CardsPlayedTurn = player.TurnPlayCards?.Count ?? 0,
                CardsPlayedGame = player.GamePlayCards?.Count ?? 0,
                Rally = player.RallyCount,
                CemeteryConsumed = player.GameNecromanceCount,
                CardsDrawnTurn = player.TurnDrawCards?.Count ?? 0,
                CardsDrawnGame = player.GameDrawCards?.Count ?? 0,
                ResonanceTurn = player.TurnResonanceStartCount,
                ResonanceGame = player.GameResonanceStartCount,
                FusionTurn = player.TurnFusionCountInfo?
                    .Where(info => info.Turn == player.BattleMgr.CurrentTurn)
                    .Sum(info => info.Value) ?? 0,
                FusionGame = player.TurnFusionCountInfo?.Sum(info => info.Value) ?? 0,
                BurialRiteTurn = player.TurnBurialRiteCards?.Count ?? 0,
                BurialRiteGame = player.GameBurialRiteCards?.Count ?? 0,
                DamageCountGame = self ? field.AllyDamageCountInGame : field.EnemyDamageCountInGame,
                DamageCountTurn = self ? field.AllyDamageCountInTurn : 0,
                PpUsedGame = player.GameUsedPpCount
            };
        }

        private static List<DeckCardSummaryDto> BuildDeckSummary(IEnumerable<BattleCardBase> cards)
        {
            return (cards ?? Enumerable.Empty<BattleCardBase>())
                .GroupBy(card => card.CardId)
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    BattleCardBase card = group.First();
                    return new DeckCardSummaryDto
                    {
                        BaseCardId = group.Key,
                        Count = group.Count(),
                        Cost = card.Cost,
                        Name = card.BaseParameter?.CardName,
                        Text = SafeDescription(card, false)
                    };
                })
                .ToList();
        }

        private static CardSnapshotDto CardSnapshot(AIVirtualCard card)
        {
            BattleCardBase real = card.BaseCard;
            return new CardSnapshotDto
            {
                Ref = CardRef(card),
                BaseCardId = card.BaseId,
                Name = card.CardName,
                Cost = card.Cost,
                Attack = card.Attack,
                Life = card.Life,
                Evolved = card.IsEvolution,
                EvolutionAttack = card.IsUnit ? card.EvolutionAttack : 0,
                EvolutionLife = card.IsUnit ? card.EvolutionLife : 0,
                Text = SafeDescription(real, false),
                EvolvedText = card.IsUnit ? SafeDescription(real, true) : null,
                Spellboost = card.HasSpellboost ? card.SpellboostCount : 0,
                Countdown = card.IsCountdownAmulet ? card.ChantCount : 0,
                Stack = card.IsStackWhiteRitual ? card.WhiteRitualCount : 0,
                UnionBurstCount = card.HasUnionBurst ? card.UnionBurstCount : 0,
                SkyboundArtCount = card.HasSkyboundArt ? card.SkyboundArtCount : 0,
                DamagedCount = real?.DamagedCounter == null ? 0 :
                    real.DamagedCounter.GetDamageCount(true) + real.DamagedCounter.GetDamageCount(false),
                CanEvolve = card.IsUnit && !card.IsEvolution && card.IsAbleEvolution(),
                EvolveConsumesEp = card.IsUnit && !card.IsNotConsumeEp
            };
        }

        private static string SafeDescription(BattleCardBase card, bool evolved)
        {
            if (card == null)
            {
                return null;
            }
            try
            {
                string text = evolved ? card.EvoSkillDescription() : card.SkillDescription();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }
                text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                return text.Length <= 1200 ? text : text.Substring(0, 1200);
            }
            catch
            {
                return null;
            }
        }

        internal static string CardRef(AIVirtualCard card)
        {
            if (card == null)
            {
                return null;
            }
            if (card.IsLeader)
            {
                return card.IsAlly ? "self_leader" : "opponent_leader";
            }
            return $"card:{(card.IsAlly ? "self" : "opponent")}:{card.CardIndex}";
        }

        internal static string Hash(AIVirtualField field, BattlePlayerPair pair = null)
        {
            string value = Describe(field, pair);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2"));
                }
                return hex.ToString();
            }
        }

        internal static string Describe(AIVirtualField field, BattlePlayerPair pair = null)
        {
            StringBuilder value = new StringBuilder(1024);
            value.Append(field.CurrentTurnCount).Append('|')
                .Append(field.AllyClass.Life).Append('|').Append(field.EnemyClass.Life).Append('|')
                .Append(field.AllyPp).Append('|').Append(field.AllyPpTotal).Append('|')
                .Append(field.EnemyPp).Append('|').Append(field.EnemyPpTotal).Append('|')
                .Append(field.AllyEvolutionCount).Append('|').Append(field.EnemyEvolutionCount).Append('|')
                .Append(field.AllyTurnCount).Append('|').Append(field.EnemyTurnCount).Append('|')
                .Append(field.AllyDeckCount).Append('|').Append(field.OpponentDeckCount).Append('|')
                .Append(field.UsedEpCount).Append('|').Append(field.UsedPpCount).Append('|')
                .Append(field.AllyEvolvedCountInGame).Append('|').Append(field.AllyEvolvedCountInPreviousTurn).Append('|')
                .Append(field.EnemyEvolvedCountInGame).Append('|').Append(field.EnemyEvolvedCountInPreviousTurn).Append('|')
                .Append(field.TurnDrawCount).Append('|').Append(field.GameDrawCount).Append('|').Append(field.VirtualDrawCount).Append('|')
                .Append(field.AllyRallyCount).Append('|').Append(field.EnemyRallyCount).Append('|')
                .Append(field.AllyGameResonanceStartCount).Append('|').Append(field.AllyTurnResonanceStartCount).Append('|')
                .Append(field.EnemyGameResonanceStartCount).Append('|').Append(field.EnemyTurnResonanceStartCount).Append('|')
                .Append(field.AllyGameUsedStackCount).Append('|').Append(field.EnemyGameUsedStackCount).Append('|')
                .Append(field.AllyDamageCountInGame).Append('|').Append(field.AllyDamageCountInTurn).Append('|')
                .Append(field.EnemyDamageCountInGame).Append('|').Append(field.AllyNecromancedCountInGame).Append('|')
                .Append(field.EnemyNecromancedCountInGame).Append('|');
            AppendPlayerCounters(value, pair?.Self);
            AppendPlayerCounters(value, pair?.Opponent);
            AppendCards(value, "ah", field.AllyHandCards);
            AppendCards(value, "ab", field.AllyInplayCards);
            AppendCards(value, "eb", field.EnemyInplayCards);
            value.Append("eh:").Append(field.GetEnemyHandCardList().Count).Append('|');
            return value.ToString();
        }

        private static void AppendPlayerCounters(StringBuilder value, BattlePlayerBase player)
        {
            if (player == null)
            {
                return;
            }
            value.Append("p:").Append(player.Turn).Append(':').Append(player.CurrentEpCount)
                .Append(':').Append(player.GameUsedEpCount).Append(':').Append(player.TurnUsedEpCount)
                .Append(':').Append(player.DeckCardList?.Count ?? 0).Append(':').Append(player.CemeteryList?.Count ?? 0)
                .Append(':').Append(player.GameNecromanceCount).Append(':').Append(player.GameUsedPpCount)
                .Append(':').Append(player.TurnPlayCards?.Count ?? 0).Append(':').Append(player.GamePlayCards?.Count ?? 0)
                .Append(':').Append(player.TurnDrawCards?.Count ?? 0).Append(':').Append(player.GameDrawCards?.Count ?? 0)
                .Append(':').Append(player.RallyCount).Append(':').Append(player.GameResonanceStartCount)
                .Append(':').Append(player.TurnResonanceStartCount).Append(':').Append(player.TurnFusionCards?.Count ?? 0)
                .Append(':').Append(player.TurnFusionCountInfo?.Sum(info => info.Value) ?? 0)
                .Append(':').Append(player.TurnBurialRiteCards?.Count ?? 0)
                .Append(':').Append(player.GameBurialRiteCards?.Count ?? 0)
                .Append('|');
        }

        private static void AppendCards(StringBuilder value, string zone, IEnumerable<AIVirtualCard> cards)
        {
            foreach (AIVirtualCard card in cards.Where(card => !card.IsDead).OrderBy(card => card.CardIndex))
            {
                value.Append(zone).Append(':').Append(card.CardIndex).Append(':').Append(card.BaseId)
                    .Append(':').Append(card.Cost).Append(':').Append(card.Attack).Append(':').Append(card.Life)
                    .Append(':').Append(card.IsEvolution ? 1 : 0).Append(':').Append(card.AttackableCount).Append('|');
            }
        }
    }

    internal static class LegalActionBuilder
    {
        internal static List<LegalActionCandidate> Build(
            EnemyAI ai,
            BattlePlayerPair pair,
            AIOperationSimulatorAccessor accessor,
            int maxCandidates,
            string requestedType = null,
            List<AISinglePlayptnRecord> validPlayPatterns = null)
        {
            accessor.UpdateCurrentField(pair, EnemyAI.EmptyPlayPtn);
            AIVirtualField field = accessor.CurrentField;
            LLMStateBuilder.SynchronizeResources(field, pair);
            List<LegalActionCandidate> result = new List<LegalActionCandidate>();
            HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);

            if (requestedType == null || requestedType == "attack" || requestedType == "turn_end")
            {
                AddAttacksAndTurnEnd(field, result, signatures, maxCandidates);
            }
            if (requestedType == null || requestedType == "evolve")
            {
                AddEvolutions(field, result, signatures, maxCandidates);
            }

            if (requestedType == null || requestedType == "play" || requestedType == "fusion")
            {
                AddCardActions(
                    ai, field, requestedType, validPlayPatterns,
                    result, signatures, maxCandidates);
            }

            return result;
        }

        private static void AddCardActions(
            EnemyAI ai,
            AIVirtualField field,
            string requestedType,
            List<AISinglePlayptnRecord> validPlayPatterns,
            List<LegalActionCandidate> result,
            HashSet<string> signatures,
            int maxCandidates)
        {
            AIPlayptnRecorder recorder = new AIPlayptnRecorder();
            AIVirtualField previousField = ai._currentVirtualField;
            AIPlayptnRecorder previousRecorder = ai.PlayPtnRecorder;
            try
            {
                // Several original helpers ignore their field argument and read these
                // properties from EnemyAI. Keep those implicit inputs on the simulated state.
                ai._currentVirtualField = field;
                ai.PlayPtnRecorder = recorder;
                recorder.CreateValidPlayPtnList(field);
                validPlayPatterns?.AddRange(recorder.ValidPlayPtnList);
                if (requestedType == null || requestedType == "play")
                {
                    AddPlays(field, recorder, result, signatures, maxCandidates);
                }
                if (requestedType == null || requestedType == "fusion")
                {
                    AddFusions(field, recorder, result, signatures, maxCandidates);
                }
            }
            finally
            {
                ai.PlayPtnRecorder = previousRecorder;
                ai._currentVirtualField = previousField;
            }
        }

        private static void AddAttacksAndTurnEnd(
            AIVirtualField field,
            List<LegalActionCandidate> result,
            HashSet<string> signatures,
            int maxCandidates)
        {
            foreach (AIVirtualActionInfo action in
                     AISimulationUtility.GetAllMovesForFullSimulation(field, false, null))
            {
                if (action.ActionType == AIOperationType.ATTACK)
                {
                    AIVirtualAttackInfo attack = (AIVirtualAttackInfo)action;
                    Add(new LegalActionCandidate
                    {
                        Type = "attack",
                        ActorRef = LLMStateBuilder.CardRef(attack.Actor),
                        TargetRefs = new List<string> { LLMStateBuilder.CardRef(attack.AttackTarget) },
                        Action = attack,
                        PpAfter = field.AllyPp
                    }, result, signatures, maxCandidates);
                }
                else if (action.ActionType == AIOperationType.TURNEND)
                {
                    Add(new LegalActionCandidate
                    {
                        Type = "turn_end",
                        TargetRefs = new List<string>(),
                        Action = action,
                        PpAfter = field.AllyPp
                    }, result, signatures, maxCandidates);
                }
            }
        }

        private static void AddEvolutions(
            AIVirtualField field,
            List<LegalActionCandidate> result,
            HashSet<string> signatures,
            int maxCandidates)
        {
            if (field.EvoUsedCard != null)
            {
                return;
            }
            foreach (AIVirtualCard card in field.AllyInplayCards.Where(card => !card.IsDead && card.IsAbleEvolution()))
            {
                AIVirtualTargetSelectAction action =
                    new AIVirtualTargetSelectAction(card, card, AIOperationType.EVOLVE, (AISelectedTargetInfoSet)null);
                AddTargetVariants(field, action, null, "evolve", null, result, signatures, maxCandidates);
            }
        }

        private static void AddPlays(
            AIVirtualField field,
            AIPlayptnRecorder recorder,
            List<LegalActionCandidate> result,
            HashSet<string> signatures,
            int maxCandidates)
        {
            foreach (AISinglePlayptnRecord record in recorder.ValidPlayPtnList)
            {
                if (!record.IsValid || record.PlayedCardList == null || record.PlayedCardList.Count == 0)
                {
                    continue;
                }
                PlayedCardInfo played = record.PlayedCardList[0];
                if (!played.IsPlayable)
                {
                    continue;
                }
                AIVirtualCard original = played.Card;
                AIVirtualCard actor = played.TransformCard ?? original;
                AIVirtualTargetSelectAction action = new AIVirtualTargetSelectAction(
                    actor,
                    original,
                    AIOperationType.PLAY,
                    played.PreDecidedSelectTargets);
                AddTargetVariants(
                    field,
                    action,
                    record,
                    "play",
                    Mode(played.PlayType),
                    result,
                    signatures,
                    maxCandidates);
            }
        }

        private static void AddTargetVariants(
            AIVirtualField field,
            AIVirtualTargetSelectAction action,
            AISinglePlayptnRecord record,
            string type,
            string mode,
            List<LegalActionCandidate> result,
            HashSet<string> signatures,
            int maxCandidates)
        {
            List<AIVirtualTargetSelectInfo> selectInfos = action.Actor.CreateAIVirtualSelectInfo(field, action);
            if (selectInfos == null || selectInfos.Count == 0)
            {
                AddTargetAction(field, action, record, type, mode, result, signatures, maxCandidates);
                return;
            }

            AISelectedTargetInfoSet preselected = action.SelectedTargets != null && action.SelectedTargets.IsAnyTargetExists()
                ? action.SelectedTargets
                : null;
            List<AISelectedTargetInfoSet> patterns =
                AIVirtualTargetSelectSimulator.GetAllTargetSelectSimulationPattern(
                    selectInfos,
                    preselected,
                    action,
                    field,
                    record);
            foreach (AISelectedTargetInfoSet targets in patterns ?? new List<AISelectedTargetInfoSet>())
            {
                AIVirtualTargetSelectAction variant = new AIVirtualTargetSelectAction(
                    action.Actor,
                    action.OriginalCard,
                    action.ActionType,
                    targets);
                AddTargetAction(field, variant, record, type, mode, result, signatures, maxCandidates);
            }
        }

        private static void AddTargetAction(
            AIVirtualField field,
            AIVirtualTargetSelectAction action,
            AISinglePlayptnRecord record,
            string type,
            string mode,
            List<LegalActionCandidate> result,
            HashSet<string> signatures,
            int maxCandidates)
        {
            PlayedCardInfo played = record?.FindPlayedCardInfo(action.OriginalCard);
            int drawCount = played?.DrawCount ?? 0;
            bool reveals = drawCount > 0 || type == "fusion";
            Add(new LegalActionCandidate
            {
                Type = type,
                ActorRef = LLMStateBuilder.CardRef(action.OriginalCard),
                Mode = mode,
                TargetRefs = OrderedTargets(action.SelectedTargets)
                    .Select(LLMStateBuilder.CardRef)
                    .ToList(),
                Action = action,
                PlayRecord = record,
                PpCost = played?.UsedCost ?? 0,
                PpAfter = played?.RestPp ?? field.AllyPp,
                EpCost = type == "evolve" && !action.OriginalCard.IsNotConsumeEp ? 1 : 0,
                DrawCount = drawCount,
                RevealsHiddenInformation = reveals,
                RequiresReplanAfter = reveals
            }, result, signatures, maxCandidates);
        }

        private static void AddFusions(
            AIVirtualField field,
            AIPlayptnRecorder recorder,
            List<LegalActionCandidate> result,
            HashSet<string> signatures,
            int maxCandidates)
        {
            AISinglePlayptnRecord emptyRecord = recorder.ValidPlayPtnList
                .FirstOrDefault(record => record.PlayedCardList == null || record.PlayedCardList.Count == 0);
            List<int> playPattern = emptyRecord?.PlayPtn ?? EnemyAI.EmptyPlayPtn;
            foreach (AIVirtualCard actor in field.AllyHandCards)
            {
                if (!actor.IsFusionable || !actor.TagCollectionContainer.HasTag(AIPlayTagType.Fusion))
                {
                    continue;
                }
                AIFusionSituationInfo fusion = new AIFusionSituationInfo(actor, null);
                if (!fusion.InitializeFusionParameter(field, playPattern))
                {
                    continue;
                }
                List<AIVirtualCard> materials = AIFilteringUtility.MultipleFiltering<AIVirtualCard>(
                    field.AllyHandCards,
                    fusion.Range,
                    actor,
                    playPattern,
                    fusion,
                    true);
                materials?.Remove(actor);
                if (materials == null || materials.Count == 0)
                {
                    continue;
                }
                int subsetCount = 1 << materials.Count;
                for (int mask = 1; mask < subsetCount; mask++)
                {
                    List<AIVirtualCard> selected = new List<AIVirtualCard>();
                    for (int i = 0; i < materials.Count; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                        {
                            selected.Add(materials[i]);
                        }
                    }
                    AISelectedTargetInfoSet targetSet = new AISelectedTargetInfoSet();
                    targetSet.Set(
                        new AISelectedTargetInfo(selected, TargetSelectType.NormalRuleBase, AIRemovalType.None),
                        0);
                    AIVirtualTargetSelectAction action = new AIVirtualTargetSelectAction(
                        actor,
                        actor,
                        AIOperationType.FUSION,
                        targetSet);
                    int before = result.Count;
                    AddTargetAction(field, action, emptyRecord, "fusion", "fusion", result, signatures, maxCandidates);
                    if (result.Count > before)
                    {
                        int drawCount = actor.GetFusionDrawCount(playPattern, fusion);
                        result[result.Count - 1].DrawCount = Math.Max(0, drawCount);
                    }
                }
            }
        }

        internal static List<AIVirtualCard> OrderedTargets(AISelectedTargetInfoSet set)
        {
            List<AIVirtualCard> result = new List<AIVirtualCard>();
            if (set == null)
            {
                return result;
            }
            Append(result, set.PreprocessTarget);
            Append(result, set.ChoiceTarget);
            for (int i = 0; i < AISelectedTargetInfoSet.LENGTH; i++)
            {
                Append(result, set.Get(i));
            }
            return result;
        }

        private static void Append(List<AIVirtualCard> result, AISelectedTargetInfo info)
        {
            if (info?.Targets == null)
            {
                return;
            }
            result.AddRange(info.Targets);
        }

        internal static string Mode(PlaySimulationType type)
        {
            switch (type)
            {
                case PlaySimulationType.Normal: return "normal";
                case PlaySimulationType.Enhance: return "enhance";
                case PlaySimulationType.Accelerate: return "accelerate";
                case PlaySimulationType.Crystalize: return "crystalize";
                case PlaySimulationType.ChoiceTransform: return "choice_transform";
                default: return "normal";
            }
        }

        private static void Add(
            LegalActionCandidate candidate,
            List<LegalActionCandidate> result,
            HashSet<string> signatures,
            int maxCandidates)
        {
            if (!signatures.Add(candidate.Signature()))
            {
                return;
            }
            result.Add(candidate);
            if (result.Count > maxCandidates)
            {
                throw new CandidateOverflowException(maxCandidates);
            }
        }
    }
}
