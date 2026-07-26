using Cute;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Wizard;
using Wizard.Scenario2.Resource;
using Wizard.Story.ChapterSelection.SelectionProcessing.Main;
using ScenarioResourceManager = Wizard.Scenario2.Resource.ResourceManager;

namespace Shadowbus
{
    internal static class StoryOfflinePatches
    {
        private static readonly Dictionary<int, int> StoryBackgroundFallbacks =
            new Dictionary<int, int>
            {
                [5] = 4,
                [6] = 4,
                [8] = 7,
                [11] = 10,
                [13] = 12,
                [14] = 12,
                [16] = 15,
                [17] = 15,
                [18] = 15,
                [9005] = 15
            };

        private static readonly FieldInfo StoryBackgroundIdField =
            AccessTools.Field(typeof(StorySectionData), "<BackGroundId>k__BackingField");

        private static readonly FieldInfo AreaSelectSectionDataField =
            AccessTools.Field(typeof(AreaSelectBG), "_sectionData");

        private static readonly FieldInfo AreaSelectBgEffectField =
            AccessTools.Field(typeof(AreaSelectBG), "_bgEffect");

        private static readonly FieldInfo AreaSelectBgEffectControlField =
            AccessTools.Field(typeof(AreaSelectBG), "_bgEffectControl");

        private static readonly FieldInfo ScenarioSummaryDataTableField =
            AccessTools.Field(typeof(ScenarioSummary), "_dataTable");

        private static readonly HashSet<string> MissingScenarioSummaryKeys =
            new HashSet<string>(StringComparer.Ordinal);

        [HarmonyPatch(typeof(AreaSelectBG), nameof(AreaSelectBG.LoadBG))]
        [HarmonyPrefix]
        private static void AreaSelectBG_LoadBG_Prefix(StorySectionData sectionData)
        {
            if (sectionData == null ||
                !StoryBackgroundFallbacks.TryGetValue(sectionData.BackGroundId, out int fallbackId))
            {
                return;
            }

            int invalidId = sectionData.BackGroundId;
            if (StoryBackgroundIdField == null)
            {
                Plugin.Logger.LogError(
                    $"[Offlinizer] Cannot replace unavailable story background {invalidId}: backing field was not found.");
                return;
            }

            StoryBackgroundIdField.SetValue(sectionData, fallbackId);
            Plugin.Logger.LogWarning(
                $"[Offlinizer] Story background {invalidId} is unavailable; using local background {fallbackId}.");
        }

        [HarmonyPatch(typeof(AreaSelectBG), "_OnLoadEndBG")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> AreaSelectBG_OnLoadEndBG_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            bool injected = false;
            MethodInfo ensureControllerMethod = AccessTools.Method(
                typeof(StoryOfflinePatches),
                nameof(EnsureSection20BackgroundController));

            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;

                if (!injected &&
                    instruction.opcode == OpCodes.Stfld &&
                    Equals(instruction.operand, AreaSelectBgEffectControlField))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, ensureControllerMethod);
                    injected = true;
                }
            }

            if (!injected)
            {
                Plugin.Logger.LogError(
                    "[Offlinizer] Failed to patch the story background controller initialization point.");
            }
        }

        private static void EnsureSection20BackgroundController(AreaSelectBG areaSelectBG)
        {
            StorySectionData sectionData =
                AreaSelectSectionDataField?.GetValue(areaSelectBG) as StorySectionData;
            if (sectionData?.Id != 20)
            {
                return;
            }

            AreaSelectEffectControlBase controller =
                AreaSelectBgEffectControlField?.GetValue(areaSelectBG) as AreaSelectEffectControlBase;
            if (controller != null)
            {
                return;
            }

            ParticleSystem backgroundEffect =
                AreaSelectBgEffectField?.GetValue(areaSelectBG) as ParticleSystem;
            if (backgroundEffect == null || AreaSelectBgEffectControlField == null)
            {
                Plugin.Logger.LogError(
                    "[Offlinizer] Cannot repair section 20 story background: the instantiated effect was not found.");
                return;
            }

            controller = backgroundEffect.gameObject.AddComponent<AreaSelectEffectControlBackGroundId2>();
            AreaSelectBgEffectControlField.SetValue(areaSelectBG, controller);
            Plugin.Logger.LogWarning(
                "[Offlinizer] Added the missing section 20 story background controller.");
        }

        [HarmonyPatch(typeof(ScenarioSummary), nameof(ScenarioSummary.GetData))]
        [HarmonyPrefix]
        private static bool ScenarioSummary_GetData_Prefix(
            ScenarioSummary __instance,
            string chapterId,
            int? subChapterId,
            ref ScenarioSummary.Data __result)
        {
            Dictionary<string, ScenarioSummary.Data> dataTable =
                ScenarioSummaryDataTableField?.GetValue(__instance) as
                    Dictionary<string, ScenarioSummary.Data>;
            if (dataTable == null)
            {
                return true;
            }

            string summaryKey = CreateScenarioSummaryKey(chapterId, subChapterId);
            dataTable.TryGetValue(summaryKey, out ScenarioSummary.Data summaryData);
            if (summaryData != null && !string.IsNullOrEmpty(summaryData.Title))
            {
                return true;
            }

            __result = CreateFallbackScenarioSummaryData(chapterId, summaryData);
            LogMissingScenarioSummary(summaryKey, __result.Title);
            return false;
        }

        [HarmonyPatch(typeof(ScenarioSummary), nameof(ScenarioSummary.GetData))]
        [HarmonyPostfix]
        private static void ScenarioSummary_GetData_Postfix(
            string chapterId,
            ref ScenarioSummary.Data __result)
        {
            if (__result != null && !string.IsNullOrEmpty(__result.Title))
            {
                return;
            }

            __result = CreateFallbackScenarioSummaryData(chapterId, __result);
            LogMissingScenarioSummary(chapterId ?? string.Empty, __result.Title);
        }

        private static ScenarioSummary.Data CreateFallbackScenarioSummaryData(
            string chapterId,
            ScenarioSummary.Data summaryData)
        {
            return new ScenarioSummary.Data(
                CreateFallbackChapterTitle(chapterId),
                summaryData?.PastSummary ?? string.Empty,
                summaryData?.BeforeSummary ?? string.Empty,
                summaryData?.AfterSummary ?? string.Empty);
        }

        private static string CreateScenarioSummaryKey(string chapterId, int? subChapterId)
        {
            if (subChapterId.HasValue && subChapterId.Value != StoryChapterData.SUB_CHAPTER_ALL)
            {
                return $"{chapterId}_{subChapterId.Value}";
            }

            return chapterId ?? string.Empty;
        }

        private static void LogMissingScenarioSummary(string summaryKey, string fallbackTitle)
        {
            lock (MissingScenarioSummaryKeys)
            {
                if (MissingScenarioSummaryKeys.Add(summaryKey))
                {
                    Plugin.Logger.LogWarning(
                        $"[Offlinizer] Story summary text is missing for chapter '{summaryKey}'; " +
                        $"using fallback title '{fallbackTitle}'.");
                }
            }
        }

        private static string CreateFallbackChapterTitle(string chapterId)
        {
            string displayId = string.IsNullOrEmpty(chapterId) ? "?" : chapterId;
            int digitCount = 0;
            while (digitCount < displayId.Length && char.IsDigit(displayId[digitCount]))
            {
                digitCount++;
            }

            string chapterNumber = digitCount > 0
                ? displayId.Substring(0, digitCount)
                : displayId;
            string chapterNumberText = Data.SystemText.Get(
                "Story_Short_Chapter_Number",
                new[] { chapterNumber });
            if (string.IsNullOrEmpty(chapterNumberText))
            {
                chapterNumberText = displayId;
            }

            // Flow-chart chapter nodes split the number and title at the first space.
            return $"{chapterNumberText} {displayId}";
        }

        [HarmonyPatch(typeof(ChapterCharaDecider), "GetChapterCharaId")]
        [HarmonyPostfix]
        private static void ChapterCharaDecider_GetChapterCharaId_Postfix(
            Parameter param,
            ref int? __result)
        {
            if (__result == null && param?.ChapterData != null && param.ChapterData.CharaId != 0)
            {
                __result = param.ChapterData.CharaId;
            }
        }

        [HarmonyPatch(typeof(ScenarioTemporaryVoice), nameof(ScenarioTemporaryVoice.GetDownloadInfoCoroutine))]
        [HarmonyPrefix]
        private static bool ScenarioTemporaryVoice_GetDownloadInfoCoroutine_Prefix(
            Action<ScenarioTemporaryVoice.DownloadInfo> finishCallback,
            ref IEnumerator __result)
        {
            __result = ReturnDownloadedVoiceInfo(finishCallback);
            return false;
        }

        [HarmonyPatch(typeof(ScenarioTemporaryVoice), nameof(ScenarioTemporaryVoice.DownloadCoroutine))]
        [HarmonyPrefix]
        private static bool ScenarioTemporaryVoice_DownloadCoroutine_Prefix(
            Action finishCallback,
            ref IEnumerator __result)
        {
            __result = CompleteVoiceDownload(finishCallback);
            return false;
        }

        [HarmonyPatch(
            typeof(ScenarioResourceManager),
            nameof(ScenarioResourceManager.LoadSyncCoroutine),
            new[] { typeof(IReadOnlyList<IResourceHandle>) })]
        [HarmonyPrefix]
        private static void ResourceManager_LoadSyncCoroutine_Prefix(
            ref IReadOnlyList<IResourceHandle> __0)
        {
            MarkMissingVoicesAsLoaded(ref __0);
        }

        [HarmonyPatch(
            typeof(ScenarioResourceManager),
            nameof(ScenarioResourceManager.LoadAsyncCoroutine),
            new[] { typeof(IReadOnlyList<IResourceHandle>) })]
        [HarmonyPrefix]
        private static void ResourceManager_LoadAsyncCoroutine_Prefix(
            ref IReadOnlyList<IResourceHandle> __0)
        {
            MarkMissingVoicesAsLoaded(ref __0);
        }

        private static IEnumerator ReturnDownloadedVoiceInfo(
            Action<ScenarioTemporaryVoice.DownloadInfo> finishCallback)
        {
            finishCallback?.Invoke(new ScenarioTemporaryVoice.DownloadInfo(new List<string>(), 0f));
            yield break;
        }

        private static IEnumerator CompleteVoiceDownload(Action finishCallback)
        {
            finishCallback?.Invoke();
            yield break;
        }

        private static void MarkMissingVoicesAsLoaded(ref IReadOnlyList<IResourceHandle> handles)
        {
            if (handles == null || handles.Count == 0)
            {
                return;
            }

            int skippedCount = 0;
            List<IResourceHandle> availableHandles = new List<IResourceHandle>(handles.Count);
            foreach (IResourceHandle handle in handles)
            {
                if (handle is VoiceHandle && !AreResourcesAvailable(handle))
                {
                    // Commands wait for every handle's IsLoaded flag. Missing voices must be
                    // marked complete before they are removed from the actual load request.
                    handle.IsLoaded = true;
                    skippedCount++;
                    continue;
                }

                availableHandles.Add(handle);
            }

            if (skippedCount == 0)
            {
                return;
            }

            handles = availableHandles;
            Plugin.Logger.LogDebug($"[Offlinizer] Skipped {skippedCount} unavailable story voice resource(s).");
        }

        private static bool AreResourcesAvailable(IResourceHandle handle)
        {
            return handle.ResourcePaths.All(path =>
            {
                try
                {
                    AssetHandle assetHandle = Toolbox.AssetManager.GetAssetHandle(path, false);
                    return assetHandle != null && File.Exists(assetHandle.BuildLocalCachePath());
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
