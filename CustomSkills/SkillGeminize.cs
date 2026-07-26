using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Wizard;
using Wizard.Battle.View.Vfx;

namespace Shadowbus
{
    public sealed class Skill_geminize : SkillBase
    {
        private static readonly FieldInfo CurrentSkillsField =
            AccessTools.Field(typeof(BattleCardBase), "<Skills>k__BackingField");

        private static readonly MethodInfo CurrentSkillsSetter =
            AccessTools.PropertySetter(typeof(BattleCardBase), nameof(BattleCardBase.Skills));

        private static readonly FieldInfo HasSkillNecromanceField =
            AccessTools.Field(typeof(BattleCardBase), "<HasSkillNecromance>k__BackingField");

        private static readonly FieldInfo BaseParameterField =
            AccessTools.Field(typeof(BattleCardBase), "_baseParameter");

        private static readonly FieldInfo EvolveToOtherCardBaseParameterField =
            AccessTools.Field(typeof(BattleCardBase), "_evolveToOtherCardBaseParameter");

        public Skill_geminize(SkillParameter skillPrm, string option)
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

            int copiedAttack = target.Atk;
            int copiedLife = target.Life;
            CardParameter targetParameter = target.BaseParameter;
            string copiedCardName = targetParameter.CardName;
            string copiedTribeName = targetParameter.TribeName;
            string copiedSkillDescription = targetParameter.SkillDescription;
            string copiedEvoSkillDescription = targetParameter.EvoSkillDescription;
            string copiedDescription = targetParameter.Description;
            string copiedEvoDescription = targetParameter.EvoDescription;
            List<CardBasePrm.TribeType> copiedTribes = target.Tribe == null
                ? new List<CardBasePrm.TribeType>()
                : new List<CardBasePrm.TribeType>(target.Tribe);
            int originalAttackableCount = owner.AttackableCount;
            bool originalSummonDrunkenness = owner.IsSummonDrunkenness;

            List<Skill_geminize> retainedNormalSkills = owner.NormalSkills
                .OfType<Skill_geminize>()
                .ToList();
            List<Skill_geminize> retainedEvolutionSkills = owner.EvolutionSkills
                .OfType<Skill_geminize>()
                .ToList();

            List<SkillCreator.SkillBuildInfo> copiedNormalBuilds =
                SnapshotBuildInfos(target.NormalSkills, target);
            List<SkillCreator.SkillBuildInfo> copiedEvolutionBuilds =
                SnapshotBuildInfos(target.EvolutionSkills, target);
            RemoveRetainedDuplicates(copiedNormalBuilds, retainedNormalSkills);
            RemoveRetainedDuplicates(copiedEvolutionBuilds, retainedEvolutionSkills);

            BattleCardBase targetSnapshot = target.VirtualClone(
                target.SelfBattlePlayer,
                target.OpponentBattlePlayer);
            SkillProcessor skillProcessor = parameter.skillProcessor ?? new SkillProcessor();
            SequentialVfxPlayer sequence = SequentialVfxPlayer.Create(Array.Empty<VfxBase>());

            sequence.Register<VfxBase>(owner.LoseSkill(this, skillProcessor));
            owner.AttackableCount = originalAttackableCount;
            owner.IsSummonDrunkenness = originalSummonDrunkenness;
            owner.IsSkillLost = false;
            owner.RemoveBuffInfo(_ => true);
            owner.SkillApplyInformation.ClearParameterModifier();

            RebuildSkillCollection(owner, owner.NormalSkills, copiedNormalBuilds);
            RebuildSkillCollection(owner, owner.EvolutionSkills, copiedEvolutionBuilds);
            foreach (Skill_geminize retainedSkill in retainedNormalSkills)
            {
                owner.NormalSkills.Add(retainedSkill);
            }
            foreach (Skill_geminize retainedSkill in retainedEvolutionSkills)
            {
                owner.EvolutionSkills.Add(retainedSkill);
            }

            owner.NormalSkillBuildInfos.AddRange(copiedNormalBuilds);
            owner.NormalSkillBuildInfos.AddRange(retainedNormalSkills.Select(skill => skill.SkillPrm.buildInfo));
            owner.EvolveSkillBuildInfos.AddRange(copiedEvolutionBuilds);
            owner.EvolveSkillBuildInfos.AddRange(retainedEvolutionSkills.Select(skill => skill.SkillPrm.buildInfo));

            owner.NormalSkills.Complete();
            owner.EvolutionSkills.Complete();
            SetCurrentSkills(owner);
            RegisterResidentSkills(owner, skillProcessor);
            HasSkillNecromanceField?.SetValue(owner, targetSnapshot.HasSkillNecromance);
            targetSnapshot.SkillApplyInformation.AttachedSkillsInfo.Clear();
            owner.SkillApplyInformation.Combine(targetSnapshot.SkillApplyInformation);
            ApplyCopiedCardMetadata(
                owner,
                copiedCardName,
                copiedTribeName,
                copiedTribes,
                copiedSkillDescription,
                copiedEvoSkillDescription,
                copiedDescription,
                copiedEvoDescription);

            BuffInfo copiedCardBuff = new BuffInfo(
                targetSnapshot.BaseParameter.BaseCardId,
                targetSnapshot.BaseParameter.NormalCardId,
                this);
            copiedCardBuff.IsCopied = true;
            copiedCardBuff.IsCopiedEvolutionSkill = target.IsEvolution;
            copiedCardBuff.IsEvolutionSkill = target.IsEvolution;
            copiedCardBuff.IsPlayer = target.IsPlayer;
            copiedCardBuff.SetPreviousOwner(targetSnapshot);
            owner.AddBuffInfo(copiedCardBuff);

            sequence.Register<VfxBase>(owner.SkillApplyInformation.AllSkillEffectRestart());
            sequence.Register<VfxBase>(owner.SkillApplyInformation.GiveCombatValueModifier(
                new OffenseSetModifier(copiedAttack),
                new LifeSetModifier(copiedLife),
                skillProcessor));
            sequence.Register<InstantVfx>(InstantVfx.Create(() =>
            {
                if (owner.BattleCardView?.CardTemplate?.NormalNameLabelTemp == null)
                {
                    return;
                }

                owner.BattleCardView.CardTemplate.NormalNameLabelTemp.text = copiedCardName;
                Global.SetRepositionNameLabel(
                    owner.BattleCardView.CardTemplate.NormalNameLabelTemp,
                    copiedCardName,
                    false);
            }));
            sequence.Register<VfxBase>(owner.BattleCardView.InitializeBattleCardIcon(
                owner,
                owner.Skills,
                false));

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

        private static void ApplyCopiedCardMetadata(
            BattleCardBase owner,
            string cardName,
            string tribeName,
            List<CardBasePrm.TribeType> tribes,
            string skillDescription,
            string evoSkillDescription,
            string description,
            string evoDescription)
        {
            CardParameter runtimeParameter = CardParameterCloner.DeepClone(owner.BaseParameter);
            runtimeParameter.Tribe = new List<CardBasePrm.TribeType>(tribes);
            CardMasterPatcher.SetRuntimeCardText(
                runtimeParameter,
                cardName,
                tribeName,
                skillDescription,
                evoSkillDescription,
                description,
                evoDescription);

            if (EvolveToOtherCardBaseParameterField?.GetValue(owner) is CardParameter)
            {
                EvolveToOtherCardBaseParameterField.SetValue(owner, runtimeParameter);
                return;
            }

            BaseParameterField?.SetValue(owner, runtimeParameter);
        }

        internal static List<SkillCreator.SkillBuildInfo> SnapshotBuildInfos(
            SkillCollectionBase skills,
            BattleCardBase previousOwner)
        {
            return skills
                .Where(skill => skill?.SkillPrm?.buildInfo != null)
                .Select(skill => CloneBuildInfo(skill.SkillPrm.buildInfo, previousOwner))
                .ToList();
        }

        private static SkillCreator.SkillBuildInfo CloneBuildInfo(
            SkillCreator.SkillBuildInfo source,
            BattleCardBase previousOwner)
        {
            SkillCreator.SkillBuildInfo clone = new SkillCreator.SkillBuildInfo(
                source._type,
                source._timing,
                source._condition,
                source._target,
                source._option,
                source._preprocess,
                source._handCardFrameEffectType,
                source._icon,
                null,
                source._effectPath,
                source._engineType,
                source._sePath,
                source._effectTime,
                source._effectMoveType,
                source._effectTargetType,
                source._voice);
            clone._previousSkillOwner = source._previousSkillOwner ?? previousOwner;
            return clone;
        }

        private static void RemoveRetainedDuplicates(
            List<SkillCreator.SkillBuildInfo> copiedBuilds,
            IEnumerable<Skill_geminize> retainedSkills)
        {
            List<SkillCreator.SkillBuildInfo> retainedBuilds = retainedSkills
                .Select(skill => skill.SkillPrm.buildInfo)
                .Where(build => build != null)
                .ToList();
            copiedBuilds.RemoveAll(copiedBuild =>
                GeminizeSkillPatcher.IsGeminizeBuild(copiedBuild) &&
                retainedBuilds.Any(retainedBuild => retainedBuild.IsSameSkill(copiedBuild)));
        }

        internal static void RebuildSkillCollection(
            BattleCardBase owner,
            SkillCollectionBase destination,
            IEnumerable<SkillCreator.SkillBuildInfo> buildInfos)
        {
            SkillCreator creator = owner.CreateSkillCreator(
                owner.SelfBattlePlayer,
                owner.OpponentBattlePlayer,
                owner.ResourceMgr);
            List<SkillPreprocessBase> previousPreprocessList = null;
            foreach (SkillCreator.SkillBuildInfo buildInfo in buildInfos)
            {
                SkillBase skill = creator.Create(buildInfo, previousPreprocessList, false, null);
                destination.Add(skill);
                if (!skill.PreprocessList.Any())
                {
                    continue;
                }

                if (skill.PreprocessList.Any(preprocess =>
                        preprocess is SkillPreprocessReferencePrevious))
                {
                    previousPreprocessList ??= new List<SkillPreprocessBase>();
                    previousPreprocessList.AddRange(skill.PreprocessList);
                }
                else
                {
                    previousPreprocessList = skill.PreprocessList.ToList();
                }
            }
        }

        internal static void SetCurrentSkills(BattleCardBase owner)
        {
            SkillCollectionBase currentSkills = owner.IsEvolution && owner.EvolutionSkills.Any()
                ? owner.EvolutionSkills
                : owner.NormalSkills;
            if (CurrentSkillsField != null)
            {
                CurrentSkillsField.SetValue(owner, currentSkills);
                return;
            }

            CurrentSkillsSetter?.Invoke(owner, new object[] { currentSkills });
        }

        internal static void RegisterResidentSkills(
            BattleCardBase owner,
            SkillProcessor skillProcessor)
        {
            if (owner == null || !owner.IsInplay || skillProcessor == null)
            {
                return;
            }

            owner.Skills.CreateAndRegisterWhenChangeInplayInfo(
                new List<BattleCardBase> { owner },
                skillProcessor,
                new BattlePlayerReadOnlyInfoPair(
                    owner.SelfBattlePlayer,
                    owner.OpponentBattlePlayer),
                true,
                null,
                null);
        }
    }

    public static class GeminizeSkillPatcher
    {
        public const string Keyword = "skill_geminize";

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

            __result = new Skill_geminize(skillParam, buildInfo._option);
            return false;
        }

        internal static bool IsGeminizeBuild(SkillCreator.SkillBuildInfo buildInfo)
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
