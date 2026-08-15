using System;
using System.Collections.Generic;
using System.Linq;
using Wizard;

namespace Shadowbus.LLMAI
{
    internal sealed class CompiledPlanStep
    {
        public TurnPlanStep Source;
        public LegalActionCandidate Candidate;
        public string ExpectedStateHash;
        public string ExpectedStateSummary;
    }

    internal sealed class CompiledTurnPlan
    {
        public string Goal;
        public string Reason;
        public List<CompiledPlanStep> Steps = new List<CompiledPlanStep>();
    }

    internal static class LLMPlanCompiler
    {
        internal static bool TryCompile(
            EnemyAI ai,
            TurnPlan plan,
            LLMAISettings settings,
            out CompiledTurnPlan compiled,
            out string reason,
            out string failedStep)
        {
            compiled = null;
            reason = null;
            failedStep = null;
            try
            {
                BattlePlayerPair pair = ai.PlayerPair.VirtualClone(CloneActualFlags.All);
                AIOperationSimulatorAccessor accessor = new AIOperationSimulatorAccessor(ai);
                accessor.UpdateCurrentField(pair, EnemyAI.EmptyPlayPtn);
                LLMStateBuilder.SynchronizeResources(accessor.CurrentField, pair);
                string actualHash = LLMStateBuilder.Hash(accessor.CurrentField, pair);
                if (!string.Equals(plan.StateHash, actualHash, StringComparison.Ordinal))
                {
                    reason = "stale_state_hash";
                    return false;
                }

                CompiledTurnPlan output = new CompiledTurnPlan { Goal = plan.Goal, Reason = plan.Reason };
                Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (TurnPlanStep step in plan.Steps)
                {
                    failedStep = step.StepId;
                    HashSet<string> cardsBefore = VisibleCardKeys(accessor.CurrentField);
                    List<LegalActionCandidate> candidates =
                        LegalActionBuilder.Build(ai, pair, accessor, settings.MaxCandidates, step.Type);
                    List<LegalActionCandidate> matches = candidates
                        .Where(candidate => Matches(step, candidate, aliases))
                        .ToList();
                    if (matches.Count == 0)
                    {
                        reason = "no_matching_legal_action";
                        return false;
                    }
                    if (matches.Count > 1)
                    {
                        reason = "ambiguous_legal_action";
                        return false;
                    }

                    LegalActionCandidate selected = matches[0];
                    string expectedHash = null;
                    switch (selected.Type)
                    {
                        case "attack":
                        {
                            AIVirtualAttackInfo attack = (AIVirtualAttackInfo)selected.Action;
                            pair = accessor.CallAttack(
                                pair,
                                attack.Actor.BaseCard,
                                attack.AttackTarget.BaseCard,
                                EnemyAI.EmptyPlayPtn);
                            LLMStateBuilder.SynchronizeResources(accessor.CurrentField, pair);
                            expectedHash = LLMStateBuilder.Hash(accessor.CurrentField, pair);
                            break;
                        }
                        case "play":
                        {
                            AIVirtualTargetSelectAction play = (AIVirtualTargetSelectAction)selected.Action;
                            pair = accessor.CallPlay(
                                pair,
                                play.OriginalCard.BaseCard,
                                LegalActionBuilder.OrderedTargets(play.SelectedTargets)
                                    .Select(card => card.BaseCard)
                                    .ToList(),
                                selected.PlayRecord?.PlayPtn ?? EnemyAI.EmptyPlayPtn);
                            LLMStateBuilder.SynchronizeResources(accessor.CurrentField, pair);
                            expectedHash = LLMStateBuilder.Hash(accessor.CurrentField, pair);
                            break;
                        }
                        case "evolve":
                        {
                            AIVirtualTargetSelectAction evolve = (AIVirtualTargetSelectAction)selected.Action;
                            pair = accessor.CallEvolve(
                                pair,
                                evolve.OriginalCard.BaseCard,
                                LegalActionBuilder.OrderedTargets(evolve.SelectedTargets)
                                    .Select(card => card.BaseCard)
                                    .ToList(),
                                EnemyAI.EmptyPlayPtn);
                            LLMStateBuilder.SynchronizeResources(accessor.CurrentField, pair);
                            expectedHash = LLMStateBuilder.Hash(accessor.CurrentField, pair);
                            break;
                        }
                        case "fusion":
                        {
                            AIVirtualFusionSimulator.Fusion(
                                (AIVirtualTargetSelectAction)selected.Action,
                                accessor.CurrentField);
                            expectedHash = LLMStateBuilder.Hash(accessor.CurrentField, pair);
                            break;
                        }
                        case "turn_end":
                            if (!ReferenceEquals(step, plan.Steps[plan.Steps.Count - 1]))
                            {
                                reason = "turn_end_must_be_last";
                                return false;
                            }
                            break;
                        default:
                            reason = "unsupported_action_type";
                            return false;
                    }

                    if (selected.Type != "turn_end")
                    {
                        BindResultAliases(step.StepId, cardsBefore, accessor.CurrentField, aliases);
                    }
                    output.Steps.Add(new CompiledPlanStep
                    {
                        Source = step,
                        Candidate = selected,
                        ExpectedStateHash = expectedHash,
                        ExpectedStateSummary = expectedHash == null ? null : LLMStateBuilder.Describe(accessor.CurrentField, pair)
                    });
                    if (selected.RequiresReplanAfter)
                    {
                        step.ReplanAfter = true;
                        break;
                    }
                }

                if (plan.Goal == "lethal" && !accessor.CurrentField.EnemyClass.IsDead)
                {
                    reason = "lethal_not_verified";
                    failedStep = plan.Steps[plan.Steps.Count - 1].StepId;
                    return false;
                }

                compiled = output;
                failedStep = null;
                return true;
            }
            catch (CandidateOverflowException exception)
            {
                reason = exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                LLMDecisionLog.Error(
                    "SIMULATION",
                    $"plan failed at step {failedStep ?? "-"}: {exception.Message}");
                LLMDecisionLog.Debug("SIMULATION", exception.ToString());
                reason = "simulation_exception:" + exception.Message;
                return false;
            }
        }

        private static bool Matches(
            TurnPlanStep step,
            LegalActionCandidate candidate,
            Dictionary<string, string> aliases)
        {
            if (!string.Equals(step.Type, candidate.Type, StringComparison.Ordinal))
            {
                return false;
            }
            string actor = ResolveAlias(step.Actor, aliases);
            if (!string.Equals(actor, candidate.ActorRef, StringComparison.Ordinal))
            {
                return false;
            }
            string requestedMode = step.Mode ?? string.Empty;
            string candidateMode = candidate.Mode ?? string.Empty;
            if (!string.Equals(requestedMode, candidateMode, StringComparison.Ordinal))
            {
                return false;
            }
            List<string> requestedTargets = (step.Targets ?? new List<string>())
                .Select(target => ResolveAlias(target, aliases))
                .ToList();
            return requestedTargets.SequenceEqual(candidate.TargetRefs ?? new List<string>(), StringComparer.Ordinal);
        }

        private static string ResolveAlias(string value, Dictionary<string, string> aliases)
        {
            return value != null && aliases.TryGetValue(value, out string resolved) ? resolved : value;
        }

        private static HashSet<string> VisibleCardKeys(AIVirtualField field)
        {
            return new HashSet<string>(
                VisibleCards(field).Select(card => CardKey(card)),
                StringComparer.Ordinal);
        }

        private static IEnumerable<AIVirtualCard> VisibleCards(AIVirtualField field)
        {
            return field.AllyHandCards
                .Concat(field.AllyInplayCards)
                .Concat(field.EnemyInplayCards)
                .Where(card => !card.IsDead);
        }

        private static string CardKey(AIVirtualCard card)
        {
            return $"{(card.IsAlly ? 'a' : 'e')}:{card.CardIndex}:{card.BaseId}";
        }

        private static void BindResultAliases(
            string stepId,
            HashSet<string> before,
            AIVirtualField after,
            Dictionary<string, string> aliases)
        {
            foreach (IGrouping<int, AIVirtualCard> group in VisibleCards(after)
                         .Where(card => !before.Contains(CardKey(card)))
                         .OrderBy(card => card.CardIndex)
                         .GroupBy(card => card.BaseId))
            {
                int ordinal = 0;
                foreach (AIVirtualCard card in group)
                {
                    aliases[$"result:{stepId}:{group.Key}:{ordinal++}"] = LLMStateBuilder.CardRef(card);
                }
            }
        }
    }
}
