using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Wizard;

namespace Shadowbus
{
    public static class CardParameterCloner
    {
        public static CardParameter DeepClone(CardParameter source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var clonedReferences = new Dictionary<object, object>();
            var clone = source.Clone();

            clone.Tribe = CloneList(source.Tribe, clonedReferences);
            clone.SkillHandCardFrameEffectType = CloneArray(source.SkillHandCardFrameEffectType, clonedReferences);
            clone.AtkEffectParameter = CloneAttackEffectParameter(source.AtkEffectParameter, clonedReferences);

            clone.SkillEffectPath = CloneArray(source.SkillEffectPath, clonedReferences);
            clone.SkillSe = CloneArray(source.SkillSe, clonedReferences);
            clone.SkillMoveType = CloneArray(source.SkillMoveType, clonedReferences);
            clone.SkillEffectEnginType = CloneArray(source.SkillEffectEnginType, clonedReferences);
            clone.SkillEffectTime = CloneArray(source.SkillEffectTime, clonedReferences);
            clone.SkillEffectTargetType = CloneArray(source.SkillEffectTargetType, clonedReferences);

            clone.EvoSkillEffectTargetType = CloneArray(source.EvoSkillEffectTargetType, clonedReferences);
            clone.EvoSkillEffectPath = CloneArray(source.EvoSkillEffectPath, clonedReferences);
            clone.EvoSkillSe = CloneArray(source.EvoSkillSe, clonedReferences);
            clone.EvoSkillMoveType = CloneArray(source.EvoSkillMoveType, clonedReferences);
            clone.EvoSkillEffectEnginType = CloneArray(source.EvoSkillEffectEnginType, clonedReferences);
            clone.EvoSkillEffectTime = CloneArray(source.EvoSkillEffectTime, clonedReferences);

            clone.EvolEffectPath = CloneArray(source.EvolEffectPath, clonedReferences);
            clone.EvolSePath = CloneArray(source.EvolSePath, clonedReferences);

            return clone;
        }

        private static CardParameter.AttackEffectParameter CloneAttackEffectParameter(
            CardParameter.AttackEffectParameter source,
            IDictionary<object, object> clonedReferences)
        {
            if (source == null)
            {
                return null;
            }

            if (clonedReferences.TryGetValue(source, out var existingClone))
            {
                return (CardParameter.AttackEffectParameter)existingClone;
            }

            // AttackEffectParameter has no copy or parameterless constructor.
            var clone = (CardParameter.AttackEffectParameter)FormatterServices.GetUninitializedObject(
                typeof(CardParameter.AttackEffectParameter));
            clonedReferences.Add(source, clone);

            clone._effectPath = CloneList(source._effectPath, clonedReferences);
            clone._se = CloneList(source._se, clonedReferences);
            clone._moveType = CloneList(source._moveType, clonedReferences);
            clone._effectEnginType = CloneList(source._effectEnginType, clonedReferences);
            clone._time = CloneList(source._time, clonedReferences);

            return clone;
        }

        private static T[] CloneArray<T>(T[] source, IDictionary<object, object> clonedReferences)
        {
            if (source == null)
            {
                return null;
            }

            if (clonedReferences.TryGetValue(source, out var existingClone))
            {
                return (T[])existingClone;
            }

            var clone = (T[])source.Clone();
            clonedReferences.Add(source, clone);
            return clone;
        }

        private static List<T> CloneList<T>(List<T> source, IDictionary<object, object> clonedReferences)
        {
            if (source == null)
            {
                return null;
            }

            if (clonedReferences.TryGetValue(source, out var existingClone))
            {
                return (List<T>)existingClone;
            }

            var clone = new List<T>(source);
            clonedReferences.Add(source, clone);
            return clone;
        }
    }
}
