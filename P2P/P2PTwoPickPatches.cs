using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Wizard;
using Wizard.RoomMatch;

namespace Shadowbus
{
    [HarmonyPatch(typeof(TwoPickClassSelectBase), nameof(TwoPickClassSelectBase.onClickClassImage))]
    internal static class P2PTwoPickClassDescriptionPatch
    {
        private static void Postfix(TwoPickClassSelectBase __instance, int inClassId)
        {
            if (!P2PRuntime.IsTwoPickRoom || __instance == null)
            {
                return;
            }

            string description = P2PTwoPickRules.GetClassDescription(inClassId);
            if (string.IsNullOrEmpty(description))
            {
                return;
            }

            TwoPickClassSelectView view =
                __instance.GetComponent<TwoPickClassSelectView>();
            if (view?.ChoiceClassInfoLabel != null)
            {
                view.ChoiceClassInfoLabel.text = description;
            }
        }
    }

    [HarmonyPatch(typeof(TwoPickClassSelectBase), nameof(TwoPickClassSelectBase.setClass))]
    internal static class P2PTwoPickClassIconPatch
    {
        private const int MaximumWaitFrames = 600;

        private static void Postfix(TwoPickClassSelectBase __instance, int[] ids)
        {
            if (!P2PRuntime.IsTwoPickRoom || __instance == null ||
                ids == null || ids.Length == 0)
            {
                return;
            }

            __instance.StartCoroutine(ApplyWhenReady(__instance, ids.ToArray()));
        }

        private static IEnumerator ApplyWhenReady(
            TwoPickClassSelectBase selector,
            int[] classIds)
        {
            yield return null;
            bool[] completed = new bool[classIds.Length];
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                if (!P2PRuntime.IsTwoPickRoom || selector == null)
                {
                    yield break;
                }

                TwoPickClassSelectView view =
                    selector.GetComponent<TwoPickClassSelectView>();
                bool allCompleted = view != null;
                for (int index = 0; index < classIds.Length; index++)
                {
                    if (completed[index])
                    {
                        continue;
                    }
                    if (!IsCandidateReady(view, index, classIds[index]))
                    {
                        allCompleted = false;
                        continue;
                    }

                    ApplyClassIcons(view.ClassObjs[index], classIds[index]);
                    completed[index] = true;
                }

                if (allCompleted && completed.All(value => value))
                {
                    yield break;
                }
                yield return null;
            }

            Plugin.Logger.LogWarning(
                "[P2P] Timed out while waiting to display Two Pick source class icons.");
        }

        private static bool IsCandidateReady(
            TwoPickClassSelectView view,
            int index,
            int classId)
        {
            if (view?.ClassObjs == null || index < 0 || index >= view.ClassObjs.Length)
            {
                return false;
            }

            NguiObjs candidate = view.ClassObjs[index];
            return candidate != null && candidate.sprites != null &&
                candidate.sprites.Length > 0 && candidate.sprites[0] != null &&
                candidate.sprites[0].spriteName == ClassCharaPrm.GetIconSpriteName(
                    (CardBasePrm.ClanType)classId) &&
                candidate.textures != null && candidate.textures.Length > 0 &&
                candidate.textures[0] != null && candidate.textures[0].mainTexture != null &&
                candidate.labels != null && candidate.labels.Length > 1;
        }

        private static void ApplyClassIcons(NguiObjs candidate, int classId)
        {
            TwoPickClassSelectCharaPanel panel =
                candidate.GetComponent<TwoPickClassSelectCharaPanel>();
            if (panel == null)
            {
                return;
            }

            List<int> sourceClassIds =
                P2PTwoPickRules.GetPossibleCardClasses(classId);
            P2PTwoPickClassIconMarker marker =
                panel.GetComponent<P2PTwoPickClassIconMarker>();
            if (marker != null && marker.ClassId == classId &&
                marker.SourceClassIds.SequenceEqual(sourceClassIds))
            {
                return;
            }
            if (marker == null)
            {
                marker = panel.gameObject.AddComponent<P2PTwoPickClassIconMarker>();
            }
            marker.ResetGeneratedIcons();
            marker.ClassId = classId;
            marker.SourceClassIds = new List<int>(sourceClassIds);
            if (sourceClassIds.Count <= 1)
            {
                panel._chaosRoot.SetActive(false);
                panel._defaultClassIconSprite.gameObject.SetActive(true);
                panel._defaultClassNameLabel.gameObject.SetActive(true);
                panel._defaultCharaNameLabel.gameObject.SetActive(true);
                return;
            }

            panel._chaosRoot.SetActive(true);
            panel._defaultClassIconSprite.gameObject.SetActive(false);
            panel._defaultClassNameLabel.gameObject.SetActive(false);
            panel._defaultCharaNameLabel.gameObject.SetActive(false);
            panel._chaosCharaClassIconSprite.spriteName =
                ClassCharaPrm.GetIconSpriteName((CardBasePrm.ClanType)classId);
            panel._chaosCharaNameLabel.text = candidate.labels[1].text;
            ClassCharaPrm.SetClassLabelSetting(
                panel._chaosCharaNameLabel,
                (CardBasePrm.ClanType)classId);
            string displayName = P2PTwoPickRules.GetClassDisplayName(classId);
            panel._chaosDeckNameLabel.text = string.IsNullOrEmpty(displayName)
                ? candidate.labels[0].text
                : displayName;
            ClassCharaPrm.SetClassLabelSetting(
                panel._chaosDeckNameLabel,
                (CardBasePrm.ClanType)classId);

            panel._chaosClassIconSprite.spriteName = ClassCharaPrm.GetIconSpriteName(
                (CardBasePrm.ClanType)sourceClassIds[0]);
            for (int index = 1; index < sourceClassIds.Count; index++)
            {
                UISprite icon = NGUITools.AddChild(
                        panel._chaosClassIconGrid.gameObject,
                        panel._chaosClassIconSprite.gameObject)
                    .GetComponent<UISprite>();
                icon.spriteName = ClassCharaPrm.GetIconSpriteName(
                    (CardBasePrm.ClanType)sourceClassIds[index]);
                marker.GeneratedIcons.Add(icon.gameObject);
            }
            panel._chaosClassIconGrid.Reposition();
            Plugin.Logger.LogInfo(
                $"[P2P] Displayed source class icons " +
                $"[{string.Join(",", sourceClassIds)}] for Two Pick candidate {classId}.");
        }
    }

    internal sealed class P2PTwoPickClassIconMarker : MonoBehaviour
    {
        internal int ClassId { get; set; } = -1;
        internal List<int> SourceClassIds { get; set; } = new List<int>();
        internal List<GameObject> GeneratedIcons { get; } = new List<GameObject>();

        internal void ResetGeneratedIcons()
        {
            foreach (GameObject icon in GeneratedIcons)
            {
                if (icon == null)
                {
                    continue;
                }
                icon.SetActive(false);
                Destroy(icon);
            }
            GeneratedIcons.Clear();
        }
    }

    [HarmonyPatch]
    internal static class P2PTwoPickDeckSizePatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TwoPickCardSelectBase), "Init");
            yield return AccessTools.Method(typeof(TwoPickCardSelectBase), "NextCardSelect");
            yield return AccessTools.Method(typeof(TwoPickCardSelectBase), "CardDecide");
            yield return AccessTools.Method(typeof(RoomTwoPickDeckSelect), "CreateGameObject");
            yield return AccessTools.Method(typeof(RoomTwoPickPlayerDisplay), "UpdateDeckCreateNumber");
            yield return AccessTools.Method(typeof(RoomTwoPickUICommon), "OnSetupFinish");
            yield return AccessTools.Method(typeof(RoomBase), "InitializeDeckConfirm");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            return ReplaceDeckSize(instructions, original);
        }

        internal static IEnumerable<CodeInstruction> ReplaceDeckSize(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            MethodInfo getter = AccessTools.PropertyGetter(
                typeof(P2PTwoPickRules),
                nameof(P2PTwoPickRules.FinalDeckSize));
            int replacementCount = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (LoadsThirty(instruction))
                {
                    replacementCount++;
                    yield return new CodeInstruction(OpCodes.Call, getter)
                        .MoveLabelsFrom(instruction)
                        .MoveBlocksFrom(instruction);
                }
                else
                {
                    yield return instruction;
                }
            }

            if (replacementCount == 0)
            {
                Plugin.Logger.LogWarning(
                    $"[P2P] No Two Pick deck-size constant was found in {original}.");
            }
        }

        private static bool LoadsThirty(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_S)
            {
                return Convert.ToInt32(instruction.operand) == 30;
            }
            if (instruction.opcode == OpCodes.Ldc_I4)
            {
                return Convert.ToInt32(instruction.operand) == 30;
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class P2PTwoPickCompletionPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo cardSet = AccessTools.Method(typeof(TwoPickCardSelectBase), "CardSet");
            return AccessTools.EnumeratorMoveNext(cardSet);
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            return P2PTwoPickDeckSizePatches.ReplaceDeckSize(instructions, original);
        }
    }
}
