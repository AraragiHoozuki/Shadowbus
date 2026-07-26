using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Shadowbus
{
    internal sealed class MirrorResidentVisual : MonoBehaviour
    {
        private BattleCardBase _card;
        private GameObject _followTarget;
        private readonly List<Material> _ownedMaterials = new List<Material>();

        internal BattleCardBase Card => _card;
        internal GameObject FollowTarget => _followTarget;

        internal void Initialize(
            BattleCardBase card,
            GameObject followTarget,
            IEnumerable<Material> ownedMaterials)
        {
            _card = card;
            _followTarget = followTarget;
            _ownedMaterials.AddRange(ownedMaterials.Where(material => material != null));
            FollowCard();
        }

        private void LateUpdate()
        {
            if (!MirrorResidentEffectManager.ShouldShow(_card) ||
                _followTarget == null ||
                _card.BattleCardView?.CardWrapObject != _followTarget)
            {
                MirrorResidentEffectManager.Remove(_card, this);
                return;
            }

            FollowCard();
        }

        private void FollowCard()
        {
            if (_followTarget == null)
            {
                return;
            }

            transform.SetPositionAndRotation(
                _followTarget.transform.position,
                _followTarget.transform.rotation);
        }

        private void OnDestroy()
        {
            MirrorResidentEffectManager.NotifyDestroyed(_card, this);
            foreach (Material material in _ownedMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }
        }
    }

    internal static class MirrorResidentEffectManager
    {
        private static readonly Color MirrorPurple = new Color(0.72f, 0.18f, 1f, 1f);
        private static readonly Dictionary<BattleCardBase, MirrorResidentVisual> Visuals =
            new Dictionary<BattleCardBase, MirrorResidentVisual>();

        private const float RefreshInterval = 0.15f;
        private static float _nextRefreshTime;

        internal static void Tick(BattleManagerBase battleManager)
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + RefreshInterval;
            HashSet<BattleCardBase> inPlayCards = new HashSet<BattleCardBase>();
            AddInPlayCards(battleManager?.BattlePlayer?.InPlayCards, inPlayCards);
            AddInPlayCards(battleManager?.BattleEnemy?.InPlayCards, inPlayCards);

            foreach (BattleCardBase card in inPlayCards)
            {
                Refresh(card);
            }

            foreach (BattleCardBase card in Visuals.Keys.ToList())
            {
                if (!inPlayCards.Contains(card) || !ShouldShow(card))
                {
                    Remove(card);
                }
            }
        }

        internal static bool ShouldShow(BattleCardBase card)
        {
            try
            {
                return MirrorSkillPatcher.HasActiveMirror(card);
            }
            catch
            {
                return false;
            }
        }

        internal static void Remove(
            BattleCardBase card,
            MirrorResidentVisual expectedVisual = null)
        {
            if (card == null || !Visuals.TryGetValue(card, out MirrorResidentVisual visual))
            {
                return;
            }
            if (expectedVisual != null && visual != expectedVisual)
            {
                return;
            }

            Visuals.Remove(card);
            if (visual != null)
            {
                visual.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(visual.gameObject);
            }
        }

        internal static void NotifyDestroyed(
            BattleCardBase card,
            MirrorResidentVisual destroyedVisual)
        {
            if (card != null &&
                Visuals.TryGetValue(card, out MirrorResidentVisual currentVisual) &&
                currentVisual == destroyedVisual)
            {
                Visuals.Remove(card);
            }
        }

        private static void AddInPlayCards(
            IEnumerable<BattleCardBase> cards,
            ISet<BattleCardBase> destination)
        {
            if (cards == null)
            {
                return;
            }

            foreach (BattleCardBase card in cards)
            {
                if (card != null)
                {
                    destination.Add(card);
                }
            }
        }

        private static void Refresh(BattleCardBase card)
        {
            GameObject followTarget = card?.BattleCardView?.CardWrapObject;
            if (!ShouldShow(card) || followTarget == null)
            {
                Remove(card);
                return;
            }

            if (Visuals.TryGetValue(card, out MirrorResidentVisual currentVisual))
            {
                if (currentVisual != null && currentVisual.FollowTarget == followTarget)
                {
                    return;
                }
                Remove(card);
            }

            MirrorResidentVisual visual = CreateVisual(card, followTarget);
            if (visual != null)
            {
                Visuals[card] = visual;
            }
        }

        private static MirrorResidentVisual CreateVisual(
            BattleCardBase card,
            GameObject followTarget)
        {
            try
            {
                EffectMgr effectManager = GameMgr.GetIns()?.GetEffectMgr();
                Effect template = effectManager?._effectList?.FirstOrDefault(effect =>
                    effect != null &&
                    effect.GetEffectType() == EffectMgr.EffectType.STT_LOOP_UNSELECTED_1 &&
                    effect.GetGameObjIns() != null);
                if (template == null)
                {
                    return null;
                }

                GameObject instance = UnityEngine.Object.Instantiate(template.GetGameObjIns());
                instance.name = "Shadowbus_MirrorResidentEffect";
                instance.SetActive(false);

                foreach (Effect pooledEffect in instance.GetComponentsInChildren<Effect>(true))
                {
                    pooledEffect.enabled = false;
                }

                List<Material> ownedMaterials = TintRenderers(instance);
                ParticleSystem[] particleSystems =
                    instance.GetComponentsInChildren<ParticleSystem>(true);
                foreach (ParticleSystem particleSystem in particleSystems)
                {
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystem.MainModule main = particleSystem.main;
                    main.startColor = MirrorPurple;
                }

                MirrorResidentVisual visual = instance.AddComponent<MirrorResidentVisual>();
                visual.Initialize(card, followTarget, ownedMaterials);
                instance.SetActive(true);
                foreach (ParticleSystem particleSystem in particleSystems)
                {
                    particleSystem.Play(true);
                }
                return visual;
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError(
                    $"[Mirror] Failed to create resident effect: {exception}");
                return null;
            }
        }

        private static List<Material> TintRenderers(GameObject instance)
        {
            List<Material> ownedMaterials = new List<Material>();
            HashSet<int> materialIds = new HashSet<int>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.materials;
                foreach (Material material in materials)
                {
                    if (material == null)
                    {
                        continue;
                    }
                    if (materialIds.Add(material.GetInstanceID()))
                    {
                        ownedMaterials.Add(material);
                    }

                    TintMaterialProperty(material, "_Color", false);
                    TintMaterialProperty(material, "_TintColor", false);
                    TintMaterialProperty(material, "_EmissionColor", true);
                }
            }
            return ownedMaterials;
        }

        private static void TintMaterialProperty(
            Material material,
            string propertyName,
            bool keepBlack)
        {
            if (!material.HasProperty(propertyName))
            {
                return;
            }

            Color original = material.GetColor(propertyName);
            float intensity = Mathf.Max(original.r, Mathf.Max(original.g, original.b));
            if (keepBlack && intensity <= 0.001f)
            {
                return;
            }
            intensity = Mathf.Max(intensity, 1f);
            material.SetColor(propertyName, new Color(
                MirrorPurple.r * intensity,
                MirrorPurple.g * intensity,
                MirrorPurple.b * intensity,
                original.a));
        }
    }

    public static class MirrorResidentEffectPatcher
    {
        [HarmonyPatch(typeof(BattleManagerBase), nameof(BattleManagerBase.Update))]
        [HarmonyPostfix]
        private static void BattleManagerBase_Update_Postfix(BattleManagerBase __instance)
        {
            MirrorResidentEffectManager.Tick(__instance);
        }
    }
}
