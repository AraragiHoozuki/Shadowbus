using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Wizard.Battle;
using Wizard.Battle.View.Vfx;

namespace Shadowbus
{
    public sealed class Skill_mirror : SkillBase
    {
        public Skill_mirror(SkillParameter skillPrm, string option)
            : base(skillPrm, option)
        {
        }

        public override VfxWithLoading Start(SkillBase.CallParameter parameter)
        {
            return NullVfxWithLoading.GetInstance();
        }

        public bool ApplyToAll => GetBooleanOption(
            SkillFilterCreator.ContentKeyword.all,
            false);

        public bool IncludeSelf => GetBooleanOption(
            SkillFilterCreator.ContentKeyword.include_self,
            true);

        private bool GetBooleanOption(
            SkillFilterCreator.ContentKeyword keyword,
            bool defaultValue)
        {
            string value = OptionValue.GetString(
                keyword,
                defaultValue ? "true" : "false");
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   value == "1";
        }
    }

    public static class MirrorSkillPatcher
    {
        public const string Keyword = "skill_mirror";

        private sealed class MirrorCallState
        {
            public List<MirrorTrigger> Triggers { get; } = new List<MirrorTrigger>();
        }

        private sealed class MirrorTrigger
        {
            public BattleCardBase Card { get; set; }
            public bool ApplyToAll { get; set; }
            public bool IncludeSelf { get; set; }
        }

        [HarmonyPatch(typeof(SkillCreator), "CreateSkillFactory")]
        [HarmonyPrefix]
        public static bool SkillCreator_CreateSkillFactory_Prefix(
            string skillName,
            SkillCreator.SkillBuildInfo buildInfo,
            SkillParameter skillParam,
            ref SkillBase __result)
        {
            if (!string.Equals(skillName, Keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            __result = new Skill_mirror(skillParam, buildInfo._option);
            return false;
        }

        [HarmonyPatch(typeof(SkillBase), nameof(SkillBase.CallStart))]
        [HarmonyPrefix]
        private static void SkillBase_CallStart_Prefix(
            SkillBase __instance,
            SkillConditionCheckerOption checkerOption,
            SkillProcessor.ProcessCallType callType,
            out MirrorCallState __state)
        {
            __state = null;
            BattleCardBase spell = __instance?.SkillPrm?.ownerCard;
            if (callType != SkillProcessor.ProcessCallType.Start ||
                spell == null ||
                !spell.IsSpell ||
                !IsMirrorableEffect(__instance) ||
                checkerOption?.SelectedCards == null)
            {
                return;
            }

            List<BattleCardBase> mirrorTargets = checkerOption.SelectedCards
                .Where(selected => IsSelectedTargetForSkill(__instance, selected))
                .Select(selected => selected.SelectCard)
                .Where(HasActiveMirror)
                .Distinct()
                .ToList();
            if (mirrorTargets.Count == 0)
            {
                return;
            }

            __state = new MirrorCallState();
            foreach (BattleCardBase mirrorTarget in mirrorTargets)
            {
                Skill_mirror mirrorSkill = GetActiveMirrorSkill(mirrorTarget);
                if (mirrorSkill == null)
                {
                    continue;
                }

                __state.Triggers.Add(new MirrorTrigger
                {
                    Card = mirrorTarget,
                    ApplyToAll = mirrorSkill.ApplyToAll,
                    IncludeSelf = mirrorSkill.IncludeSelf
                });
            }
        }

        [HarmonyPatch(typeof(SkillBase), nameof(SkillBase.CallStart))]
        [HarmonyPostfix]
        private static void SkillBase_CallStart_Postfix(
            SkillBase __instance,
            SkillBase.CallParameter parameter,
            MirrorCallState __state,
            ref VfxBase __result)
        {
            if (__state == null || parameter?.targetCards == null)
            {
                return;
            }

            List<BattleCardBase> resolvedTargets = parameter.targetCards
                .Where(card => card != null)
                .ToList();
            List<MirrorTrigger> triggeredMirrors = __state.Triggers
                .Where(trigger => resolvedTargets.Any(target =>
                    SameCard(target, trigger.Card)))
                .ToList();
            if (triggeredMirrors.Count == 0)
            {
                return;
            }

            BattleCardBase spell = __instance.SkillPrm.ownerCard;
            BattleManagerBase battleManager = spell.SelfBattlePlayer?.BattleMgr;
            if (battleManager == null || battleManager.IsBattleEnd)
            {
                return;
            }

            VfxWithLoadingSequential sequence = VfxWithLoadingSequential.Create();
            sequence.RegisterToMainVfx(__result);

            foreach (MirrorTrigger trigger in triggeredMirrors)
            {
                List<BattleCardBase> candidates = spell.SelfBattlePlayer.InPlayCards
                    .Where(card => card != null && card.IsUnit && !card.IsDead)
                    .Where(card => trigger.IncludeSelf ||
                                   !SameCard(card, trigger.Card))
                    .ToList();
                if (candidates.Count == 0 || battleManager.IsBattleEnd)
                {
                    continue;
                }

                IReadOnlyList<BattleCardBase> mirrorTargets = trigger.ApplyToAll
                    ? candidates
                    : new[]
                    {
                        candidates[battleManager.StableRandom(candidates.Count)]
                    };
                SkillBase.CallParameter mirrorParameter = new SkillBase.CallParameter
                {
                    targetCards = mirrorTargets,
                    skillProcessor = parameter.skillProcessor,
                    calledSkillResultInfo = new SkillBase.SkillResultInfo()
                };

                try
                {
                    if (trigger.Card.BattleCardView != null && !trigger.Card.IsDead)
                    {
                        sequence.RegisterToMainVfx(
                            new WhenPlaySkillActivationVfx(trigger.Card.BattleCardView));
                    }
                    sequence.RegisterVfxWithLoading(__instance.Start(mirrorParameter));
                }
                catch (Exception exception)
                {
                    Plugin.Logger.LogError(
                        $"[Mirror] Failed to repeat spell skill '{__instance.GetType().Name}': " +
                        exception);
                }
            }

            __result = sequence;
        }

        private static bool IsSelectedTargetForSkill(
            SkillBase skill,
            SkillConditionCheckerOption.SkillAndSelectTarget selected)
        {
            if (selected?.SelectCard == null)
            {
                return false;
            }
            if (ReferenceEquals(selected.SelectSkill, skill))
            {
                return true;
            }
            if (selected.SelectSkill == null && skill.IsUserSelectType)
            {
                return true;
            }
            return skill.ApplyingTargetFilter is SkillTargetSelectedCardsFilter ||
                   skill.ApplyingTargetFilter is SkillTargetLastTargetFilter;
        }

        private static bool IsMirrorableEffect(SkillBase skill)
        {
            return !(skill is Skill_none) &&
                   !(skill is Skill_select) &&
                   !(skill is Skill_choice) &&
                   !(skill is Skill_invoke_skill) &&
                   !(skill is Skill_loop_skill) &&
                   !(skill is Skill_mirror);
        }

        internal static bool HasActiveMirror(BattleCardBase card)
        {
            return card != null &&
                   card.IsUnit &&
                   card.IsInplay &&
                   !card.IsDead &&
                   GetActiveMirrorSkill(card) != null;
        }

        private static Skill_mirror GetActiveMirrorSkill(BattleCardBase card)
        {
            return card?.Skills?
                .OfType<Skill_mirror>()
                .FirstOrDefault(skill => skill.IsResidentSkillStartFlag);
        }

        private static bool SameCard(BattleCardBase left, BattleCardBase right)
        {
            return ReferenceEquals(left, right) || left.EquelsID(right);
        }
    }
}
