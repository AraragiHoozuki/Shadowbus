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
using BattleResultChapterCharaDecider = Wizard.Story.ChapterSelection.SelectionProcessing.BattleResult.ChapterCharaDecider;
using BattleResultParameter = Wizard.Story.ChapterSelection.SelectionProcessing.BattleResult.Parameter;
using MainChapterCharaDecider = Wizard.Story.ChapterSelection.SelectionProcessing.Main.ChapterCharaDecider;
using MainParameter = Wizard.Story.ChapterSelection.SelectionProcessing.Main.Parameter;
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

        // Asset manifests are normally populated by the online boot flow.  The offlinizer
        // skips that flow, so a voice file can exist on disk while its AssetHandle is absent.
        // Keep track of the aliases we create so repeated story loads stay quiet.
        private static readonly HashSet<string> RegisteredVoiceAssetKeys =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly HashSet<string> MissingVoiceCueWarnings =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool HasLoggedScenarioVoicePlayback;

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

        [HarmonyPatch(typeof(MainChapterCharaDecider), "GetChapterCharaId")]
        [HarmonyPrefix]
        private static bool MainChapterCharaDecider_GetChapterCharaId_Prefix(
            MainParameter param,
            ref int? __result)
        {
            if (param?.DeckData == null || param.ChapterData == null)
            {
                return true;
            }

            BattleSettingData setting = ResolveDeckBattleSetting(
                param.ChapterData,
                param.DeckData,
                "chapter start");
            if (setting == null)
            {
                return true;
            }

            __result = setting.PlayerCharaId;
            return false;
        }

        [HarmonyPatch(typeof(MainChapterCharaDecider), "GetChapterCharaId")]
        [HarmonyPostfix]
        private static void ChapterCharaDecider_GetChapterCharaId_Postfix(
            MainParameter param,
            ref int? __result)
        {
            if (__result == null && param?.ChapterData != null && param.ChapterData.CharaId != 0)
            {
                __result = param.ChapterData.CharaId;
            }
        }

        [HarmonyPatch(typeof(BattleResultChapterCharaDecider), "GetChapterCharaId")]
        [HarmonyPrefix]
        private static bool BattleResultChapterCharaDecider_GetChapterCharaId_Prefix(
            BattleResultParameter param,
            ref int __result)
        {
            if (param?.DeckData == null || param.ChapterData == null)
            {
                return true;
            }

            BattleSettingData setting = ResolveDeckBattleSetting(
                param.ChapterData,
                param.DeckData,
                "battle-result deck selection");
            if (setting == null)
            {
                return true;
            }

            __result = setting.PlayerCharaId;
            return false;
        }

        private static BattleSettingData ResolveDeckBattleSetting(
            StoryChapterData chapterData,
            DeckData deckData,
            string context)
        {
            int deckSkinId = deckData.GetSkinId(false);
            BattleSettingData setting =
                chapterData.FindBattleSettingDataByDeckSkinId(deckSkinId);
            if (setting != null)
            {
                return setting;
            }

            int deckClassId = deckData.GetDeckClassID();
            setting = chapterData.FindBattleSettingDataByDeckClassId(deckClassId);
            if (setting != null)
            {
                Plugin.Logger.LogWarning(
                    $"[Offlinizer] Story {context} could not match deck skin {deckSkinId}; " +
                    $"using the chapter battle setting for class {deckClassId}.");
                return setting;
            }

            setting = chapterData.BattleSettingDatas?.FirstOrDefault();
            if (setting != null)
            {
                Plugin.Logger.LogWarning(
                    $"[Offlinizer] Story {context} could not match deck skin {deckSkinId} " +
                    $"or class {deckClassId}; using the chapter's first battle setting.");
            }
            else
            {
                Plugin.Logger.LogError(
                    $"[Offlinizer] Story {context} has no battle setting for deck " +
                    $"skin {deckSkinId}, class {deckClassId}.");
            }

            return setting;
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
            int registeredCount = 0;
            int keptVoiceCount = 0;
            List<IResourceHandle> availableHandles = new List<IResourceHandle>(handles.Count);
            foreach (IResourceHandle handle in handles)
            {
                bool isVoice = handle is VoiceHandle;
                if (isVoice && !AreResourcesAvailable(handle, ref registeredCount))
                {
                    // Commands wait for every handle's IsLoaded flag. Missing voices must be
                    // marked complete before they are removed from the actual load request.
                    handle.IsLoaded = true;
                    skippedCount++;
                    continue;
                }

                if (isVoice)
                {
                    keptVoiceCount++;
                }

                availableHandles.Add(handle);
            }

            if (skippedCount == 0 && registeredCount == 0)
            {
                return;
            }

            if (skippedCount > 0)
            {
                handles = availableHandles;
            }

            Plugin.Logger.LogInfo(
                $"[Offlinizer] Story voice resources: kept={keptVoiceCount}, " +
                $"registeredLocal={registeredCount}, skippedUnavailable={skippedCount}.");
        }

        private static bool AreResourcesAvailable(
            IResourceHandle handle,
            ref int registeredCount)
        {
            if (handle?.ResourcePaths == null || handle.ResourcePaths.Count == 0)
            {
                return false;
            }

            foreach (string path in handle.ResourcePaths)
            {
                if (!TryEnsureLocalVoiceAsset(path, out bool registered))
                {
                    return false;
                }

                if (registered)
                {
                    registeredCount++;
                }
            }

            return true;
        }

        private static bool TryEnsureLocalVoiceAsset(string resourcePath, out bool registered)
        {
            registered = false;
            if (string.IsNullOrWhiteSpace(resourcePath) || Toolbox.AssetManager == null)
            {
                return false;
            }

            AssetHandle existingHandle = Toolbox.AssetManager.GetAssetHandle(resourcePath, false);
            if (IsLocalAssetHandleAvailable(existingHandle))
            {
                return true;
            }

            foreach (string candidatePath in GetVoicePathCandidates(resourcePath))
            {
                AssetHandle candidateHandle = existingHandle;
                if (candidateHandle == null || !string.Equals(candidatePath, resourcePath, StringComparison.Ordinal))
                {
                    candidateHandle = CreateLocalAssetHandle(candidatePath);
                }

                if (!IsLocalAssetHandleAvailable(candidateHandle))
                {
                    continue;
                }

                // Register an alias under the path requested by VoiceHandle.  This also
                // makes ResourcesManager.LoadAssetGroup* call the normal cue-sheet loader.
                if (existingHandle == null)
                {
                    try
                    {
                        if (Toolbox.AssetManager.RegistHandle(resourcePath, candidateHandle))
                        {
                            existingHandle = Toolbox.AssetManager.GetAssetHandle(resourcePath, false);
                            registered = RegisteredVoiceAssetKeys.Add(resourcePath);
                        }
                    }
                    catch (Exception exception)
                    {
                        Plugin.Logger.LogWarning(
                            $"[Offlinizer] Could not register local story voice '{resourcePath}': " +
                            exception.Message);
                    }
                }

                return existingHandle != null && IsLocalAssetHandleAvailable(existingHandle);
            }

            return false;
        }

        private static AssetHandle CreateLocalAssetHandle(string resourcePath)
        {
            string localHash = string.Empty;
            try
            {
                localHash = Toolbox.AssetManager.GetLocalDatahash(resourcePath) ?? string.Empty;
            }
            catch
            {
                // A missing local manifest database is harmless for an already present file.
            }

            return new AssetHandle(resourcePath, localHash, null, null, null, null, false, false);
        }

        private static bool IsLocalAssetHandleAvailable(AssetHandle assetHandle)
        {
            if (assetHandle == null)
            {
                return false;
            }

            try
            {
                return File.Exists(assetHandle.BuildLocalCachePath());
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> GetVoicePathCandidates(string resourcePath)
        {
            string normalizedPath = resourcePath.Replace('\\', '/');
            yield return normalizedPath;

            string extension = Path.GetExtension(normalizedPath);
            if (!string.Equals(extension, ".acb", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            string fileName = Path.GetFileNameWithoutExtension(normalizedPath);
            bool hasVoicePrefix = fileName.StartsWith("vo_", StringComparison.OrdinalIgnoreCase);
            string directory = normalizedPath.Substring(0, normalizedPath.Length - fileName.Length - extension.Length);
            string fileWithExtension = fileName + extension;
            if (!hasVoicePrefix)
            {
                yield return directory + "vo_" + fileWithExtension;
            }

            string alternateDirectory = normalizedPath.StartsWith("v/t/", StringComparison.OrdinalIgnoreCase)
                ? "v/"
                : "v/t/";
            yield return alternateDirectory + fileWithExtension;
            if (!hasVoicePrefix)
            {
                yield return alternateDirectory + "vo_" + fileWithExtension;
            }
        }

        [HarmonyPatch(
            typeof(Wizard.Scenario2.Player.VoiceManager),
            nameof(Wizard.Scenario2.Player.VoiceManager.Init))]
        [HarmonyPostfix]
        private static void VoiceManager_Init_Postfix(bool canPlayVoice)
        {
            Plugin.Logger.LogInfo(
                canPlayVoice
                    ? "[Offlinizer] Story voice playback is enabled."
                    : "[Offlinizer] Story voice playback is disabled by the story selection option.");
        }

        [HarmonyPatch(typeof(Voice), nameof(Voice.PlayScenario))]
        [HarmonyPrefix]
        private static void Voice_PlayScenario_Prefix(ref string cuename)
        {
            if (string.IsNullOrWhiteSpace(cuename))
            {
                return;
            }

            string normalizedCueName = NormalizeVoiceCueName(cuename);
            if (!string.Equals(normalizedCueName, cuename, StringComparison.Ordinal))
            {
                Plugin.Logger.LogDebug(
                    $"[Offlinizer] Normalized story voice cue '{cuename}' to '{normalizedCueName}'.");
                cuename = normalizedCueName;
            }

            // ResourceManager normally registers the cue sheet while loading VoiceHandle.
            // Ensure a locally cached sheet is available even when the startup manifest did
            // not contain this voice entry.
            string resourcePath = "v/" + normalizedCueName + ".acb";
            if (!TryLoadLocalVoiceCueSheet(resourcePath))
            {
                if (MissingVoiceCueWarnings.Add(resourcePath))
                {
                    Plugin.Logger.LogWarning(
                        $"[Offlinizer] Story requested voice '{normalizedCueName}', " +
                        "but its local ACB file could not be loaded from 'v' or 'v/t'.");
                }
                return;
            }

            if (!HasLoggedScenarioVoicePlayback)
            {
                HasLoggedScenarioVoicePlayback = true;
                Plugin.Logger.LogInfo(
                    $"[Offlinizer] Playing local story voice cue '{normalizedCueName}'.");
            }
        }

        private static bool TryLoadLocalVoiceCueSheet(string resourcePath)
        {
            if (!TryEnsureLocalVoiceAsset(resourcePath, out _) || Toolbox.AudioManager == null)
            {
                return false;
            }

            AssetHandle assetHandle = Toolbox.AssetManager.GetAssetHandle(resourcePath, false);
            if (!IsLocalAssetHandleAvailable(assetHandle))
            {
                return false;
            }

            try
            {
                // Mirrors AssetHandle._LoadPostProcess for Sound resources. AddCueSheet is
                // idempotent, so preloaded voices return immediately while late temporary
                // voices are registered just before their command executes.
                return Toolbox.AudioManager.AddCueSheet(
                    assetHandle.filename,
                    Path.GetFileName(assetHandle.filename),
                    assetHandle.directory,
                    string.Empty);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning(
                    $"[Offlinizer] Failed to load local story voice '{resourcePath}': " +
                    exception.Message);
                return false;
            }
        }

        private static string NormalizeVoiceCueName(string cueName)
        {
            if (cueName.StartsWith("vo_", StringComparison.OrdinalIgnoreCase))
            {
                return cueName;
            }

            string prefixedName = "vo_" + cueName;
            return TryEnsureLocalVoiceAsset("v/" + prefixedName + ".acb", out _)
                ? prefixedName
                : cueName;
        }
    }
}
