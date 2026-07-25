using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Wizard.Battle.View.Vfx;

namespace Shadowbus
{
    public sealed class Skill_acquire_skills : SkillBase
    {
        private static readonly FieldInfo HasAnySkillField =
            AccessTools.Field(typeof(BattleCardBase), "<HasAnySkill>k__BackingField");

        private static readonly FieldInfo HasSkillNecromanceField =
            AccessTools.Field(typeof(BattleCardBase), "<HasSkillNecromance>k__BackingField");

        private static readonly FieldInfo OnCopySkillCompleteField =
            AccessTools.Field(typeof(BattleCardBase), "OnCopySkillComplete");

        public Skill_acquire_skills(SkillParameter skillPrm, string option)
            : base(skillPrm, option)
        {
        }

        public override VfxWithLoading Start(SkillBase.CallParameter parameter)
        {
            BattleCardBase owner = SkillPrm.ownerCard;
            BattleCardBase target = parameter?.targetCards?
                .FirstOrDefault(card => card != null && card.IsUnit && !card.IsDead);
            if (owner == null || !owner.IsUnit || target == null)
            {
                return NullVfxWithLoading.GetInstance();
            }

            List<SkillCreator.SkillBuildInfo> copiedNormalBuilds =
                Skill_geminize.SnapshotBuildInfos(target.NormalSkills, target)
                    .Where(build => !AcquireSkillsSkillPatcher.IsAcquireSkillsBuild(build))
                    .ToList();
            List<SkillCreator.SkillBuildInfo> copiedEvolutionBuilds =
                Skill_geminize.SnapshotBuildInfos(target.EvolutionSkills, target)
                    .Where(build => !AcquireSkillsSkillPatcher.IsAcquireSkillsBuild(build))
                    .ToList();
            BattleCardBase targetSnapshot = target.VirtualClone(
                target.SelfBattlePlayer,
                target.OpponentBattlePlayer);

            SequentialVfxPlayer sequence = SequentialVfxPlayer.Create(Array.Empty<VfxBase>());
            sequence.Register<VfxBase>(owner.SkillApplyInformation.AllSkillEffectStop(
                false,
                false,
                false,
                false));

            Skill_geminize.RebuildSkillCollection(
                owner,
                owner.NormalSkills,
                copiedNormalBuilds);
            Skill_geminize.RebuildSkillCollection(
                owner,
                owner.EvolutionSkills,
                copiedEvolutionBuilds);
            owner.NormalSkillBuildInfos.AddRange(copiedNormalBuilds);
            owner.EvolveSkillBuildInfos.AddRange(copiedEvolutionBuilds);
            owner.NormalSkills.Complete();
            owner.EvolutionSkills.Complete();
            Skill_geminize.SetCurrentSkills(owner);
            SetPublishedActiveSkillCounts(
                owner,
                copiedNormalBuilds,
                copiedEvolutionBuilds);

            HasAnySkillField?.SetValue(owner, true);
            HasSkillNecromanceField?.SetValue(
                owner,
                owner.HasSkillNecromance || targetSnapshot.HasSkillNecromance);

            targetSnapshot.SkillApplyInformation.AttachedSkillsInfo.Clear();
            owner.SkillApplyInformation.Combine(targetSnapshot.SkillApplyInformation);
            owner.CostModifierList.AddRange(targetSnapshot.CostModifierList);
            owner.attackCountinfo.AddRange(targetSnapshot.attackCountinfo);
            CopyAbilityBuffs(owner, target);

            BuffInfo copiedSkillsBuff = new BuffInfo(
                targetSnapshot.BaseParameter.BaseCardId,
                targetSnapshot.BaseParameter.NormalCardId,
                this);
            copiedSkillsBuff.IsCopied = true;
            copiedSkillsBuff.IsCopiedEvolutionSkill = target.IsEvolution;
            copiedSkillsBuff.IsEvolutionSkill = target.IsEvolution;
            copiedSkillsBuff.IsPlayer = target.IsPlayer;
            copiedSkillsBuff.SetPreviousOwner(targetSnapshot);
            owner.AddBuffInfo(copiedSkillsBuff);

            sequence.Register<VfxBase>(owner.SkillApplyInformation.AllSkillEffectRestart());
            sequence.Register<VfxBase>(owner.BattleCardView.InitializeBattleCardIcon(
                owner,
                owner.Skills,
                false));
            (OnCopySkillCompleteField?.GetValue(owner) as Action<BattleCardBase>)?.Invoke(owner);

            VfxWithLoadingSequential result = VfxWithLoadingSequential.Create();
            result.RegisterVfxWithLoading(CreateSkillEffect(
                SkillPrm.resourceMgr,
                new[] { target },
                false,
                true,
                false));
            result.RegisterToMainVfx(sequence);
            return result;
        }

        private static void CopyAbilityBuffs(BattleCardBase owner, BattleCardBase target)
        {
            foreach (BuffInfo sourceBuff in target.BuffInfoList.Where(IsAbilityBuff))
            {
                BuffInfo copiedBuff = sourceBuff.Clone();
                copiedBuff.TargetCard = sourceBuff.TargetCard;
                copiedBuff.SpecialSkillInfo = sourceBuff.SpecialSkillInfo;
                copiedBuff.IsCopied = true;
                copiedBuff.IsCopiedEvolutionSkill = sourceBuff.IsCopied
                    ? sourceBuff.IsCopiedEvolutionSkill
                    : sourceBuff.IsEvolutionSkill;
                copiedBuff.SetPreviousOwner(sourceBuff.GetDisplayCard() ?? target);
                copiedBuff.CopiedSkillDescriptionValueList.AddRange(
                    sourceBuff.CopiedSkillDescriptionValueList);
                copiedBuff.CopiedEvoSkillDescriptionValueList.AddRange(
                    sourceBuff.CopiedEvoSkillDescriptionValueList);
                owner.AddBuffInfo(copiedBuff);
            }
        }

        private static bool IsAbilityBuff(BuffInfo buff)
        {
            return buff != null &&
                   !(buff.SkillFrom is Skill_powerup) &&
                   !(buff.SkillFrom is Skill_power_down);
        }

        private static void SetPublishedActiveSkillCounts(
            BattleCardBase owner,
            ICollection<SkillCreator.SkillBuildInfo> normalBuilds,
            ICollection<SkillCreator.SkillBuildInfo> evolutionBuilds)
        {
            foreach (SkillBase skill in owner.NormalSkills.Where(skill =>
                         normalBuilds.Contains(skill.SkillPrm.buildInfo)))
            {
                skill.SetAndAddPublishedActiveSkillCount();
            }

            foreach (SkillBase skill in owner.EvolutionSkills.Where(skill =>
                         evolutionBuilds.Contains(skill.SkillPrm.buildInfo)))
            {
                skill.SetAndAddPublishedActiveSkillCount();
            }
        }
    }

    public static class AcquireSkillsSkillPatcher
    {
        public const string Keyword = "skill_acquire_skills";

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

            __result = new Skill_acquire_skills(skillParam, buildInfo._option);
            return false;
        }

        internal static bool IsAcquireSkillsBuild(SkillCreator.SkillBuildInfo buildInfo)
        {
            if (buildInfo == null || string.IsNullOrEmpty(buildInfo._type))
            {
                return false;
            }

            string skillName = buildInfo._type;
            int callCountSeparator = skillName.IndexOf('@');
            if (callCountSeparator >= 0)
            {
                skillName = skillName.Substring(0, callCountSeparator);
            }
            return string.Equals(skillName, Keyword, StringComparison.OrdinalIgnoreCase);
        }
    }
}
