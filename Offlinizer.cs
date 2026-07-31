using Cute;
using HarmonyLib;
using LitJson;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Wizard;
using Wizard.RoomMatch;
using Wizard.Title;

namespace Shadowbus
{
    public class Offlinizer
    {
        private static bool ForceLocalPracticeDeckDialog;
        private static bool ForceLocalStoryDeckDialog;
        private static bool ForceLocalPracticeDeckUi;
        private static bool BuildLocalPracticeDeckPages;
        private static readonly HashSet<int> LocalPracticeDeckUiIds = new HashSet<int>();
        private static List<int> LocalPracticeRetryEnemyDeck;

        #region GameStart
        [HarmonyPatch(typeof(AssetManager), nameof(AssetManager.InitializeManifest))]
        [HarmonyPrefix]
        public static bool AssetManager_InitializeManifest_Prefix(AssetManager __instance, Action completeCallback, ref IEnumerator __result)
        {
            __result = SkipInitializeManifestCoroutine(__instance, completeCallback);
            
            return false;
        }
        private static IEnumerator SkipInitializeManifestCoroutine(AssetManager __instance, Action completeCallback)
        {
            
            List<string> list;
            List<string> loadList;
            __instance.PrepareManifestList(out list, out loadList, true);
            //Plugin.Logger.LogInfo(string.Join("\n",__instance.handleDictionary.Keys.Select(x => $"[Offlinizer] AssetHandle Key: {x}")));
            yield return __instance.StartCoroutine(Toolbox.ResourcesManager.LoadAssetGroupSync(loadList, null, false));
            bool isDone = false;
            __instance.CacheAsset("card_shader_common.unity3d", delegate
            {
                isDone = true;
            });
            while (!isDone)
            {
                yield return 0;
            }
            loadList.Sort();
            __instance.ClearManifestOfManifests();
            Toolbox.SavedataManager.Save();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Toolbox.QualityManager.GetFrameRate();
            completeCallback?.Invoke();
            yield break;
        }

        [HarmonyPatch(typeof(ResourceDownloader), nameof(ResourceDownloader.CheckAndStartNeedDownload))]
        [HarmonyPrefix]
        public static bool ResourceDownloader_CheckAndStartNeedDownload_Prefix(ResourceDownloader __instance)
        {
            __instance.IsFinished = true;
            return false;
        }

        [HarmonyPatch(typeof(SetUp), nameof(SetUp.StartTitleCheckTask))]
        [HarmonyPrefix]
        public static bool SetUp_StartTitleCheckTask(ref SetUp __instance)
        {
            Plugin.Logger.LogInfo($"[Offlinizer] Skipped api: check/special_title");
            return false;
        }

        [HarmonyPatch(typeof(SignUpTask), nameof(SignUpTask.Parse))]
        [HarmonyPrefix]
        public static bool SignUpTask_Parse_Prefix(SignUpTask __instance)
        {
            JsonData jsonData = __instance.ResponseData["data_headers"];
            Certification.udid = jsonData["udid"].ToString();
            return true;
        }

        [HarmonyPatch(typeof(Certification), nameof(Certification.Login))]
        [HarmonyPrefix]
        public static bool Certification_Login_Prefix(Certification __instance, ref IEnumerator __result)
        {
            int viewerId = P2PIdentity.ViewerId;
            if (Certification.ViewerId != viewerId)
            {
                Certification.ViewerId = viewerId;
            }
            return true;
        }

        private static IEnumerator SkipSignUpCoroutine(Certification __instance)
        {
            yield return __instance.StartCoroutine(__instance.GameStartCheckTaskExec());
            yield break;
        }

        [HarmonyPatch(typeof(Certification), nameof(Certification.GameStartCheckTaskExec))]
        [HarmonyPrefix]
        public static bool Certification_GameStartCheckTaskExec(Certification __instance, ref IEnumerator __result)
        {
            __result = SkipGameStartCheckCoroutine();
            Plugin.Logger.LogInfo($"[Offlinizer] Skipped api: check/game_start");
            return false;
        }
        private static IEnumerator SkipGameStartCheckCoroutine()
        {
            Toolbox.BootNetwork?.IsDoneGameStartCheck = true;
            URLScheme.ClearCampaignData();
            yield break;
        }
        #endregion

        #region AllResources
        [HarmonyPatch(typeof(Emblem), MethodType.Constructor, [typeof(string[])])]
        [HarmonyPostfix]
        public static void Emblem_Constructor_Postfix(Emblem __instance)
        {
            __instance.IsAcquired = true;
        }

        [HarmonyPatch(typeof(Sleeve), MethodType.Constructor, [typeof(string[])])]
        [HarmonyPostfix]
        public static void Sleeve_Constructor_Postfix(Sleeve __instance)
        {
            __instance.IsAcquired = true;
        }

        [HarmonyPatch(typeof(Degree), MethodType.Constructor, [typeof(string[])])]
        [HarmonyPostfix]
        public static void Degree_Constructor_Postfix(Degree __instance)
        {
            __instance.IsAcquired = true;
        }

        [HarmonyPatch(typeof(ClassCharacterMasterData), MethodType.Constructor, [typeof(string[])])]
        [HarmonyPostfix]
        public static void ClassCharacterMasterData_Constructor_Postfix(ClassCharacterMasterData __instance)
        {
            __instance.IsAcquired = true;
        }
        #endregion

        #region MyPageBackground
        private sealed class LocalMyPageBackgroundSettings
        {
            [JsonProperty("select_type")]
            public int SelectType { get; set; }

            [JsonProperty("mypage_id")]
            public string MyPageId { get; set; } = string.Empty;

            [JsonProperty("mypage_id_list")]
            public List<string> MyPageIdList { get; set; } = new List<string>();
        }

        [HarmonyPatch(typeof(Master), nameof(Master.StartLoadMyPageCustomBG))]
        [HarmonyPostfix]
        public static void Master_StartLoadMyPageCustomBG_Postfix(Master __instance)
        {
            UnlockAllMyPageBackgrounds(Data.Load?.data, __instance?.MyPageCustomBGMasterList);
        }

        [HarmonyPatch(typeof(MyPageBGCustomDialog), nameof(MyPageBGCustomDialog.Create))]
        [HarmonyPrefix]
        public static void MyPageBGCustomDialog_Create_Prefix()
        {
            UnlockAllMyPageBackgrounds(Data.Load?.data, Data.Master?.MyPageCustomBGMasterList);
        }

        [HarmonyPatch(typeof(MyPageSettingUpdateTask), nameof(MyPageSettingUpdateTask.SetParameter))]
        [HarmonyPostfix]
        public static void MyPageSettingUpdateTask_SetParameter_Postfix(
            MyPageDetail.BGType type,
            string id,
            List<string> randomList)
        {
            SaveMyPageBackgroundSettings(type, id, randomList);
        }

        [HarmonyPatch(typeof(MyPageTask), "Parse")]
        [HarmonyPostfix]
        public static void MyPageTask_Parse_Postfix(int __result)
        {
            if (__result == 1)
            {
                ApplySavedMyPageBackgroundSettings();
            }
        }

        [HarmonyPatch(typeof(MyPageItemHome), nameof(MyPageItemHome.DecideRandomBG))]
        [HarmonyPrefix]
        public static bool MyPageItemHome_DecideRandomBG_Prefix(List<string> randomIdList, ref string __result)
        {
            if (randomIdList != null && randomIdList.Count > 0)
            {
                return true;
            }

            List<string> allBackgroundIds = GetAllMyPageBackgroundIds();
            if (allBackgroundIds.Count == 0)
            {
                Plugin.Logger.LogWarning("[Offlinizer] Cannot select a random My Page background: the master list is empty.");
                __result = string.Empty;
                return false;
            }

            __result = allBackgroundIds[new System.Random().Next(0, allBackgroundIds.Count)];
            return false;
        }

        private static void UnlockAllMyPageBackgrounds(
            LoadDetail loadDetail,
            IEnumerable<MyPageCustomBGMasterData> masterBackgrounds = null)
        {
            if (loadDetail?.AcquiredMyPageBGList == null)
            {
                return;
            }

            List<string> allBackgroundIds = (masterBackgrounds ?? Data.Master?.MyPageCustomBGMasterList)
                ?.Where(background => background != null && !string.IsNullOrEmpty(background.Id))
                .Select(background => background.Id)
                .Distinct()
                .ToList();

            if (allBackgroundIds == null || allBackgroundIds.Count == 0)
            {
                return;
            }

            bool changed = loadDetail.AcquiredMyPageBGList.Count != allBackgroundIds.Count ||
                !loadDetail.AcquiredMyPageBGList.SequenceEqual(allBackgroundIds);
            loadDetail.AcquiredMyPageBGList.Clear();
            loadDetail.AcquiredMyPageBGList.AddRange(allBackgroundIds);
            if (changed)
            {
                Plugin.Logger.LogInfo($"[Offlinizer] Unlocked {allBackgroundIds.Count} My Page backgrounds.");
            }
        }

        private static List<string> GetAllMyPageBackgroundIds()
        {
            return Data.Master?.MyPageCustomBGMasterList?
                .Where(background => background != null && !string.IsNullOrEmpty(background.Id))
                .Select(background => background.Id)
                .Distinct()
                .ToList() ?? new List<string>();
        }

        private static void SaveMyPageBackgroundSettings(
            MyPageDetail.BGType type,
            string id,
            IEnumerable<string> randomList)
        {
            try
            {
                LocalMyPageBackgroundSettings settings = new LocalMyPageBackgroundSettings
                {
                    SelectType = (int)type,
                    MyPageId = type == MyPageDetail.BGType.CustomBG ? id ?? string.Empty : string.Empty,
                    MyPageIdList = randomList?
                        .Where(backgroundId => !string.IsNullOrEmpty(backgroundId))
                        .Distinct()
                        .ToList() ?? new List<string>()
                };

                string json = JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(PathHelper.MyPageBackgroundSettingsPath, json, Encoding.UTF8);
                Plugin.Logger.LogInfo(
                    $"[Offlinizer] Saved My Page background: type={settings.SelectType}, " +
                    $"id='{settings.MyPageId}', randomCount={settings.MyPageIdList.Count}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Offlinizer] Failed to save My Page background settings: {ex.Message}");
            }
        }

        private static void ApplySavedMyPageBackgroundSettings()
        {
            if (Data.MyPage?.data == null || !File.Exists(PathHelper.MyPageBackgroundSettingsPath))
            {
                return;
            }

            try
            {
                LocalMyPageBackgroundSettings settings = JsonConvert.DeserializeObject<LocalMyPageBackgroundSettings>(
                    File.ReadAllText(PathHelper.MyPageBackgroundSettingsPath, Encoding.UTF8));
                if (settings == null || !Enum.IsDefined(typeof(MyPageDetail.BGType), settings.SelectType))
                {
                    Plugin.Logger.LogWarning("[Offlinizer] Ignored invalid My Page background settings.");
                    return;
                }

                List<string> allBackgroundIds = GetAllMyPageBackgroundIds();
                HashSet<string> validBackgroundIds = new HashSet<string>(allBackgroundIds, StringComparer.Ordinal);
                MyPageDetail.BGType type = (MyPageDetail.BGType)settings.SelectType;
                if (type == MyPageDetail.BGType.CustomBG && !validBackgroundIds.Contains(settings.MyPageId ?? string.Empty))
                {
                    Plugin.Logger.LogWarning(
                        $"[Offlinizer] Saved My Page background '{settings.MyPageId}' no longer exists; using the offline response setting.");
                    return;
                }

                List<string> randomIds = (settings.MyPageIdList ?? new List<string>())
                    .Where(validBackgroundIds.Contains)
                    .Distinct()
                    .ToList();
                if (type == MyPageDetail.BGType.RandomBG && randomIds.Count == 0)
                {
                    randomIds.AddRange(allBackgroundIds);
                }

                Data.MyPage.data.BGInfo = new MyPageBGInfo
                {
                    BGType = type,
                    Id = type == MyPageDetail.BGType.CustomBG ? settings.MyPageId : string.Empty,
                    RandomIdList = randomIds
                };
                Plugin.Logger.LogInfo(
                    $"[Offlinizer] Restored My Page background: type={(int)type}, " +
                    $"id='{Data.MyPage.data.BGInfo.Id}', randomCount={randomIds.Count}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Offlinizer] Failed to load My Page background settings: {ex.Message}");
            }
        }
        #endregion

        [HarmonyPatch(typeof(LoadDetail), nameof(LoadDetail.ConvertJsonData))]
        [HarmonyPostfix]
        public static void LoadDetail_ConvertJsonData_Postfix(LoadDetail __instance)
        {
            ProfileOfflineData.ApplyAfterLoad(__instance);
            LoadLocalUnlimitedDecks(__instance);
            UnlockAllMyPageBackgrounds(__instance);
        }

        public static void LoadLocalUnlimitedDecks(LoadDetail __instance)
        {
            LoadLocalDecks(__instance);
        }

        internal static void LoadLocalDecks(LoadDetail loadDetail)
        {
            if (loadDetail == null)
            {
                return;
            }

            loadDetail.UserDeckListUnlimited = CustomDeckStore.LoadDeckList();
            if (Data.Master.isMasterDataLoaded)
            {
                DeckListUtility.SetDeckListDataWithLodeIndex();
            }
        }

        [HarmonyPatch(typeof(DeckMyListTask), nameof(DeckMyListTask.Parse))]
        [HarmonyPrefix]
        public static void DeckMyListTask_Parse_Prefix(DeckMyListTask __instance)
        {
            try
            {
                LoadDetail loadDetail = Data.Load?.data;
                JsonData response = __instance.ResponseData;
                if (loadDetail == null || response == null || !response.IsObject ||
                    !response.Keys.Contains("data"))
                {
                    return;
                }

                LoadLocalDecks(loadDetail);
                response["data"]["user_deck_list"] = loadDetail.UserDeckListUnlimited;
                Plugin.Logger.LogInfo(
                    "[CustomFormats] Loaded shared Unlimited decks for the deck list.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    $"[CustomFormats] Failed to inject the active deck list: {ex}");
            }
        }

        [HarmonyPatch(typeof(DeckInfoTask), nameof(DeckInfoTask.Parse))]
        [HarmonyPrefix]
        public static void DeckInfoTask_Parse_Prefix(DeckInfoTask __instance)
        {
            try
            {
                LoadDetail loadDetail = Data.Load?.data;
                JsonData responseData = __instance.ResponseData;
                if (loadDetail == null || responseData == null || !responseData.IsObject ||
                    !responseData.Keys.Contains("data"))
                {
                    return;
                }

                LoadLocalDecks(loadDetail);
                JsonData data = responseData["data"];
                if (__instance._format == Format.All)
                {
                    SetDeckListIfAvailable(data, "user_deck_rotation", loadDetail.UserDeckListRotation);
                    SetDeckListIfAvailable(data, "user_deck_unlimited", loadDetail.UserDeckListUnlimited);
                    SetDeckListIfAvailable(data, "user_deck_pre_rotation", loadDetail.UserDeckListPreRotation);
                    SetDeckListIfAvailable(data, "user_deck_crossover", loadDetail.UserDeckListCrossover);
                    SetDeckListIfAvailable(data, "user_deck_my_rotation", loadDetail.UserDeckListMyRotation);
                    return;
                }

                JsonData localDecks = GetLocalDeckList(loadDetail, __instance._format);
                if (localDecks == null)
                {
                    return;
                }

                // Single-format DeckInfo responses are parsed from user_deck_list.
                data["user_deck_list"] = localDecks;
                int selectableCount = localDecks.Cast<JsonData>()
                    .Count(deck => deck.Keys.Contains("card_id_array") &&
                        deck["card_id_array"].IsArray && deck["card_id_array"].Count > 0);
                Plugin.Logger.LogInfo(
                    $"[Offlinizer] Injected {selectableCount} local player deck(s) into " +
                    $"the {__instance._format} DeckInfo response.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    $"[Offlinizer] Failed to inject local decks into DeckInfoTask: {ex}");
            }
        }

        [HarmonyPatch(typeof(DeckInfoTask), nameof(DeckInfoTask.Parse))]
        [HarmonyPostfix]
        public static void DeckInfoTask_Parse_Postfix(
            DeckInfoTask __instance,
            int __result)
        {
            if (!P2PRuntime.IsActive || __result != 1 || Data.Load?.data == null)
            {
                return;
            }

            try
            {
                Format format = ResolveP2PDeckFormat(__instance._format);
                __instance.DeckGroupListData = MergeLocalCustomDeckGroup(
                    __instance.DeckGroupListData,
                    format,
                    out DeckGroup localGroup);
                GameMgr.GetIns().GetDataMgr().CurrentDeckListParamData =
                    __instance.DeckGroupListData;

                LogP2PDeckGroups(
                    "DeckInfoTask.Parse",
                    __instance.DeckGroupListData,
                    localGroup);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    $"[P2P] Failed to rebuild the parsed room deck list: {ex}");
            }
        }

        private static JsonData GetLocalDeckList(LoadDetail loadDetail, Format format)
        {
            switch (format)
            {
                case Format.Rotation:
                    return loadDetail.UserDeckListRotation;
                case Format.Unlimited:
                    return loadDetail.UserDeckListUnlimited;
                case Format.PreRotation:
                    return loadDetail.UserDeckListPreRotation;
                case Format.Crossover:
                    return loadDetail.UserDeckListCrossover;
                case Format.MyRotation:
                    return loadDetail.UserDeckListMyRotation;
                default:
                    return null;
            }
        }

        private static void SetDeckListIfAvailable(JsonData data, string key, JsonData decks)
        {
            if (decks != null)
            {
                data[key] = decks;
            }
        }

        [HarmonyPatch(typeof(PracticeDeckSelectConfirmDialog), "StartBattleAgain")]
        [HarmonyPrefix]
        public static void PracticeDeckSelectConfirmDialog_StartBattleAgain_Prefix()
        {
            DataMgr dataMgr = GameMgr.GetIns().GetDataMgr();
            IList<int> playerDeck = dataMgr.GetCurrentDeckData();
            if (playerDeck != null && playerDeck.Count > 0 && playerDeck.Count < 6)
            {
                int originalCount = playerDeck.Count;
                List<int> paddedDeck = playerDeck.ToList();
                for (int index = originalCount; index < 6; index++)
                {
                    paddedDeck.Add(playerDeck[index % originalCount]);
                }

                dataMgr.SetCurrentDeckData(paddedDeck);
                Plugin.Logger.LogWarning(
                    $"[Offlinizer] Padded the selected practice retry deck from {originalCount} to " +
                    "6 cards so the opening mulligan can be created.");
            }

            if (dataMgr.m_EnemyAIDeckId != int.MinValue)
            {
                return;
            }

            IList<int> enemyDeck = dataMgr.GetCurrentEnemyDeckData();
            if (enemyDeck == null || enemyDeck.Count == 0)
            {
                LocalPracticeRetryEnemyDeck = null;
                Plugin.Logger.LogError(
                    "[Offlinizer] Could not preserve the custom enemy deck before practice retry.");
                return;
            }

            LocalPracticeRetryEnemyDeck = enemyDeck.ToList();
            Plugin.Logger.LogInfo(
                $"[Offlinizer] Preserved {LocalPracticeRetryEnemyDeck.Count} custom enemy cards " +
                "for practice retry.");
        }

        [HarmonyPatch(
            typeof(DataMgr),
            nameof(DataMgr.SetCurrentEnemyDeckDataFromAIDeck),
            [
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(bool),
                typeof(int),
                typeof(List<int>)
            ])]
        [HarmonyPrefix]
        public static bool DataMgr_SetCurrentEnemyDeckDataFromAIDeck_Prefix(
            DataMgr __instance,
            int deckId)
        {
            if (deckId != int.MinValue)
            {
                return true;
            }

            if (LocalPracticeRetryEnemyDeck == null || LocalPracticeRetryEnemyDeck.Count == 0)
            {
                Plugin.Logger.LogError(
                    "[Offlinizer] The custom enemy deck snapshot is unavailable during practice retry.");
                return true;
            }

            __instance.SetCurrentEnemyDeckData(LocalPracticeRetryEnemyDeck.ToList());
            Plugin.Logger.LogInfo(
                $"[Offlinizer] Restored {LocalPracticeRetryEnemyDeck.Count} custom enemy cards " +
                "for practice retry without resolving an invalid AI deck ID.");
            return false;
        }

        [HarmonyPatch(typeof(PracticeDeckInfoTask), nameof(PracticeDeckInfoTask.Parse))]
        [HarmonyPrefix]
        public static void PracticeDeckInfoTask_Parse_Prefix(PracticeDeckInfoTask __instance)
        {
            try
            {
                LoadDetail loadDetail = Data.Load?.data;
                JsonData responseData = __instance.ResponseData;
                if (loadDetail == null || responseData == null || !responseData.IsObject ||
                    !responseData.Keys.Contains("data"))
                {
                    Plugin.Logger.LogWarning(
                        "[Offlinizer] Could not inject local decks into the practice retry deck list.");
                    return;
                }

                LoadLocalDecks(loadDetail);
                responseData["data"]["user_deck_unlimited"] = loadDetail.UserDeckListUnlimited;
                Plugin.Logger.LogInfo(
                    $"[Offlinizer] Injected {loadDetail.UserDeckListUnlimited.Count} local decks " +
                    "into the practice retry deck list.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    $"[Offlinizer] Failed to inject local decks into the practice retry deck list: {ex}");
            }
        }

        [HarmonyPatch(typeof(PracticeDeckInfoTask), nameof(PracticeDeckInfoTask.Parse))]
        [HarmonyPostfix]
        public static void PracticeDeckInfoTask_Parse_Postfix(
            PracticeDeckInfoTask __instance,
            int __result)
        {
            if (__result != 1 || Data.Load?.data == null || __instance.DeckGroupListData == null)
            {
                return;
            }

            try
            {
                DeckGroup localUnlimitedGroup = DeckListUtility.CreateDeckGroup(
                    Data.Load.data.UserDeckListUnlimited,
                    Format.Unlimited,
                    DeckAttributeType.CustomDeck);
                List<DeckGroup> mergedGroups = __instance.DeckGroupListData.DeckGroupList
                    .Where(group => group.DeckFormat != Format.Unlimited ||
                        group.AttributeType != DeckAttributeType.CustomDeck)
                    .Select(group => group.Clone())
                    .ToList();
                mergedGroups.Insert(0, localUnlimitedGroup);

                DeckGroupListData mergedDeckList = new DeckGroupListData(mergedGroups);
                __instance.DeckGroupListData = mergedDeckList;
                GameMgr.GetIns().GetDataMgr().CurrentDeckListParamData = mergedDeckList;
                ForceLocalPracticeDeckDialog = true;

                int nonEmptyDeckCount = localUnlimitedGroup.DeckDataList.Count(deck => !deck.IsNoCard());
                Plugin.Logger.LogInfo(
                    $"[Offlinizer] Practice retry deck list rebuilt: " +
                    $"unlimited={localUnlimitedGroup.DeckDataList.Count}, " +
                    $"nonEmpty={nonEmptyDeckCount}, groups={mergedGroups.Count}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    $"[Offlinizer] Failed to rebuild the practice retry deck list: {ex}");
            }
        }

        [HarmonyPatch(typeof(StoryDeckListTask), nameof(StoryDeckListTask.Parse))]
        [HarmonyPostfix]
        public static void StoryDeckListTask_Parse_Postfix(int __result)
        {
            if (__result == 1)
            {
                ForceLocalStoryDeckDialog = true;
            }
        }

        [HarmonyPatch(typeof(DeckSelectUIDialog), nameof(DeckSelectUIDialog.Create))]
        [HarmonyPrefix]
        public static void DeckSelectUIDialog_Create_Prefix(
            ref DeckGroupListData deckGroupListData,
            ref Format defaultFormat,
            ref DeckSelectUIDialog.eFormatChangeUIType formatChangeUIType,
            ref bool isVisibleCreateNew,
            ref DeckSelectUI.InitOptions initOptions)
        {
            CustomFormats.ReloadForUi("deck selection");
            bool isLocalPractice = ForceLocalPracticeDeckDialog;
            bool isP2PRoom = P2PRuntime.IsActive &&
                RoomBase.GetInstance() != null;
            bool isStory = !isP2PRoom && !isLocalPractice &&
                ForceLocalStoryDeckDialog;
            if (!isLocalPractice && !isP2PRoom && !isStory)
            {
                return;
            }
            if (isLocalPractice)
            {
                ForceLocalPracticeDeckDialog = false;
            }
            if (isStory)
            {
                ForceLocalStoryDeckDialog = false;
            }

            try
            {
                LoadDetail loadDetail = Data.Load?.data;
                if (loadDetail == null)
                {
                    Plugin.Logger.LogWarning(
                        isP2PRoom
                            ? "[P2P] Could not prepare the local room deck dialog."
                            : "[Offlinizer] Could not prepare the local practice deck dialog.");
                    return;
                }

                if (isP2PRoom)
                {
                    CustomFormatContext.RoomFormatId = P2PRuntime.Rules?.CustomFormatId;
                    CustomFormatDefinition roomDefinition = CustomFormatContext.RoomFormat;
                    Format format = ResolveP2PDeckFormat(defaultFormat);
                    deckGroupListData = MergeLocalCustomDeckGroup(
                        deckGroupListData,
                        format,
                        out DeckGroup localGroup);
                    if (roomDefinition.Id != CustomFormats.UnlimitedId)
                    {
                        deckGroupListData = new DeckGroupListData(localGroup);
                        isVisibleCreateNew = false;
                    }
                    AddRoomFormatDeckAvailabilityCallback(ref initOptions);
                    defaultFormat = format;
                    GameMgr.GetIns().GetDataMgr().CurrentDeckListParamData = deckGroupListData;
                    DeckData primaryLocalDeck = localGroup.DeckDataList
                        .FirstOrDefault(deck => !deck.IsNoCard() &&
                            P2PRuntime.IsDeckAllowed(deck, out _, out _)) ??
                        localGroup.DeckDataList.FirstOrDefault(deck => !deck.IsNoCard());
                    if (primaryLocalDeck != null)
                    {
                        initOptions = initOptions ?? new DeckSelectUI.InitOptions();
                        initOptions.PrimaryFirstDisplayDeck = primaryLocalDeck;
                    }
                    LogP2PDeckGroups(
                        "DeckSelectUIDialog.Create",
                        deckGroupListData,
                        localGroup);
                    Plugin.Logger.LogInfo(
                        $"[P2P] Room deck dialog primary page anchored to " +
                        $"'{primaryLocalDeck?.GetDeckName() ?? "<none>"}'.");
                }
                else if (isStory)
                {
                    // DeckSelectionDialog has already cloned the local story decks and
                    // replaced their skin IDs with the chapter-compatible overrides.
                    // Rebuilding the group here discards those overrides, so
                    // ChapterCharaDecider cannot map the selected deck to battle data.
                    defaultFormat = Format.Unlimited;
                    formatChangeUIType = DeckSelectUIDialog.eFormatChangeUIType.SingleFormat;
                    initOptions = initOptions ?? new DeckSelectUI.InitOptions();
                    if (initOptions.PrimaryFirstDisplayDeck == null)
                    {
                        initOptions.PrimaryFirstDisplayDeck = deckGroupListData?.DeckGroupList?
                            .Where(group => group.DeckFormat == Format.Unlimited &&
                                group.AttributeType == DeckAttributeType.CustomDeck)
                            .SelectMany(group => group.DeckDataList)
                            .FirstOrDefault(deck => !deck.IsNoCard());
                    }

                    int storyDeckCount = deckGroupListData?.DeckGroupList?
                        .SelectMany(group => group.DeckDataList)
                        .Count(deck => !deck.IsNoCard()) ?? 0;
                    Plugin.Logger.LogInfo(
                        $"[Offlinizer] Preserved {storyDeckCount} chapter-compatible " +
                        "local deck(s) for story selection.");
                }
                else
                {
                    LoadLocalDecks(loadDetail);
                    DeckGroup localUnlimitedGroup = DeckListUtility.CreateDeckGroup(
                        loadDetail.UserDeckListUnlimited,
                        Format.Unlimited,
                        DeckAttributeType.CustomDeck);
                    deckGroupListData = new DeckGroupListData(localUnlimitedGroup);
                    defaultFormat = Format.Unlimited;
                    formatChangeUIType = DeckSelectUIDialog.eFormatChangeUIType.SingleFormat;
                    if (isLocalPractice)
                    {
                        ForceLocalPracticeDeckUi = true;
                    }

                    Plugin.Logger.LogInfo(
                        "[CustomFormats] Opening shared Unlimited deck dialog with " +
                        $"{localUnlimitedGroup.DeckDataList.Count} decks.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    isP2PRoom
                        ? $"[P2P] Failed to prepare the local room deck dialog: {ex}"
                        : $"[Offlinizer] Failed to prepare the local practice deck dialog: {ex}");
            }
        }

        private static DeckGroupListData MergeLocalCustomDeckGroup(
            DeckGroupListData existing,
            Format format,
            out DeckGroup localGroup)
        {
            LoadDetail loadDetail = Data.Load.data;
            LoadLocalDecks(loadDetail);
            JsonData localDecks = GetLocalDeckList(loadDetail, format);
            if (localDecks == null)
            {
                throw new InvalidOperationException(
                    $"No local deck source is available for room format {format}.");
            }

            localGroup = DeckListUtility.CreateDeckGroup(
                localDecks,
                format,
                DeckAttributeType.CustomDeck);
            List<DeckGroup> groups = existing?.DeckGroupList?
                .Where(group => group.DeckFormat != format ||
                    group.AttributeType != DeckAttributeType.CustomDeck)
                .Select(group => group.Clone())
                .ToList() ?? new List<DeckGroup>();
            groups.Insert(0, localGroup);
            return new DeckGroupListData(groups);
        }

        private static void AddRoomFormatDeckAvailabilityCallback(
            ref DeckSelectUI.InitOptions initOptions)
        {
            initOptions = initOptions ?? new DeckSelectUI.InitOptions();
            Action<DeckUI> original = initOptions.OnUpdateDeckUICustomize;
            initOptions.OnUpdateDeckUICustomize = deckUI =>
            {
                original?.Invoke(deckUI);
                ApplyRoomFormatDeckAvailability(deckUI);
            };
        }

        private static void ApplyRoomFormatDeckAvailability(DeckUI deckUI)
        {
            DeckData deck = deckUI?.Deck;
            if (!P2PRuntime.IsActive || deck == null ||
                deckUI.ViewType != DeckUI.eViewType.Normal || deck.IsNoCard() ||
                P2PRuntime.IsDeckAllowed(
                    deck,
                    out CustomFormatDefinition definition,
                    out CustomFormatViolation violation))
            {
                return;
            }

            CardMaster cardMaster = CardMaster.GetInstanceForBattle();
            deckUI._warningSprite.gameObject.SetActive(true);
            deckUI._warningLabel.overflowMethod = UILabel.Overflow.ShrinkContent;
            deckUI._warningLabel.maxLineCount = 1;
            deckUI._warningLabel.text =
                $"{definition.DisplayName}：" +
                CustomFormatViolationText.Describe(violation, cardMaster);
            deckUI.SetSelectable(false);
        }

        private static Format ResolveP2PDeckFormat(Format candidate)
        {
            if (candidate != Format.All && candidate != Format.Max)
            {
                return candidate;
            }

            if (P2PRuntime.Rules != null)
            {
                Format ruleFormat = Data.ParseApiFormat(P2PRuntime.Rules.DeckFormat);
                if (ruleFormat != Format.All && ruleFormat != Format.Max)
                {
                    return ruleFormat;
                }
            }
            return Format.Unlimited;
        }

        private static void LogP2PDeckGroups(
            string stage,
            DeckGroupListData listData,
            DeckGroup localGroup)
        {
            string localDeckNames = string.Join(", ", localGroup.DeckDataList
                .Where(deck => !deck.IsNoCard())
                .Select(deck => $"'{deck.GetDeckName()}'({deck.GetCardIdList().Count})"));
            string groups = string.Join(", ", listData.DeckGroupList.Select(group =>
                $"{group.DeckFormat}/{group.AttributeType}:{group.DeckDataList.Count}"));
            int disabledByRoomFormat = localGroup.DeckDataList.Count(deck =>
                !deck.IsNoCard() &&
                !P2PRuntime.IsDeckAllowed(deck, out _, out _));
            Plugin.Logger.LogInfo(
                $"[P2P] {stage} received local decks [{localDeckNames}]; " +
                $"disabledByRoomFormat={disabledByRoomFormat}; final groups [{groups}].");
        }

        [HarmonyPatch(typeof(DeckSelectUI), nameof(DeckSelectUI.Initialize))]
        [HarmonyPrefix]
        public static void DeckSelectUI_Initialize_Prefix(
            DeckSelectUI __instance,
            ref List<DeckGroup> deckGroupList,
            ref Format format)
        {
            if (!ForceLocalPracticeDeckUi)
            {
                return;
            }
            ForceLocalPracticeDeckUi = false;

            DeckGroup localUnlimitedGroup = DeckListUtility.CreateDeckGroup(
                Data.Load.data.UserDeckListUnlimited,
                Format.Unlimited,
                DeckAttributeType.CustomDeck);
            deckGroupList = new List<DeckGroup> { localUnlimitedGroup };
            format = Format.Unlimited;
            LocalPracticeDeckUiIds.Add(__instance.GetInstanceID());

            Plugin.Logger.LogInfo(
                $"[Offlinizer] Local practice DeckSelectUI initialized with " +
                $"{localUnlimitedGroup.DeckDataList.Count} decks.");
        }

        [HarmonyPatch(typeof(DeckSelectUI), "CreateDeckGroupPages")]
        [HarmonyPrefix]
        public static void DeckSelectUI_CreateDeckGroupPages_Prefix(
            DeckSelectUI __instance,
            ref List<DeckGroup> deckGroupList,
            ref Format format)
        {
            if (!LocalPracticeDeckUiIds.Contains(__instance.GetInstanceID()))
            {
                return;
            }

            DeckGroup localUnlimitedGroup = DeckListUtility.CreateDeckGroup(
                Data.Load.data.UserDeckListUnlimited,
                Format.Unlimited,
                DeckAttributeType.CustomDeck);
            deckGroupList = new List<DeckGroup> { localUnlimitedGroup };
            format = Format.Unlimited;
            BuildLocalPracticeDeckPages = true;
        }

        [HarmonyPatch(typeof(DeckSelectUI.PageData), nameof(DeckSelectUI.PageData.CreatePageList))]
        [HarmonyPrefix]
        public static bool DeckSelectUI_PageData_CreatePageList_Prefix(
            List<DeckGroup> deckGroupList,
            ref List<DeckSelectUI.PageData> __result)
        {
            if (!BuildLocalPracticeDeckPages)
            {
                return true;
            }
            BuildLocalPracticeDeckPages = false;

            List<DeckData> localDecks = deckGroupList
                .Where(group => group.DeckFormat == Format.Unlimited &&
                    group.AttributeType == DeckAttributeType.CustomDeck)
                .SelectMany(group => group.DeckDataList)
                .Where(deck => !deck.IsNoCard())
                .ToList();
            List<DeckSelectUI.PageData> pages = new List<DeckSelectUI.PageData>();
            for (int offset = 0; offset < localDecks.Count; offset += 9)
            {
                List<DeckUI.DeckViewData> deckViews = localDecks
                    .Skip(offset)
                    .Take(9)
                    .Select(deck => new DeckUI.DeckViewData(DeckUI.eViewType.Normal, deck))
                    .ToList();
                pages.Add(new DeckSelectUI.PageData(
                    deckViews,
                    Format.Unlimited,
                    DeckAttributeType.CustomDeck,
                    DeckListUtility.DeckListHeader(DeckAttributeType.CustomDeck, pages.Count + 1)));
            }

            __result = pages;
            Plugin.Logger.LogInfo(
                $"[Offlinizer] Built {pages.Count} local practice deck page(s) " +
                $"containing {localDecks.Count} deck(s).");
            return false;
        }

        [HarmonyPatch(typeof(DeckSelectUI), "CreateDeckGroupPages")]
        [HarmonyPostfix]
        public static void DeckSelectUI_CreateDeckGroupPages_Postfix(DeckSelectUI __instance)
        {
            if (P2PRuntime.IsActive)
            {
                string pages = string.Join(", ", __instance._pageList.Select((page, index) =>
                    $"{index}:{page.Format}/{page.AttributeType}[" +
                    string.Join(", ", page.DeckViewList
                        .Where(view => view.ViewType != DeckUI.eViewType.Empty)
                        .Select(view => $"'{view.Deck.GetDeckName()}'")) + "]"));
                Plugin.Logger.LogInfo(
                    $"[P2P] Room deck pages built; selected={__instance._currentPageIndex}; " +
                    $"pages=[{pages}].");
            }

            if (!LocalPracticeDeckUiIds.Remove(__instance.GetInstanceID()))
            {
                return;
            }

            Plugin.Logger.LogInfo(
                $"[Offlinizer] Local practice DeckSelectUI finished with " +
                $"{__instance._pageList.Count} page(s).");
        }

        #region DeckEdit
        [HarmonyPatch(typeof(DeckDeleteTask), nameof(DeckDeleteTask.Parse))]
        [HarmonyPrefix]
        public static void DeckDeleteTask_Parse_Prefix(DeckDeleteTask __instance)
        {
            DeckDeleteTask.DeckDeleteTaskParam parameters =
                __instance.Params as DeckDeleteTask.DeckDeleteTaskParam;
            var deletedDeckNos = new HashSet<int>(
                parameters?.deck_no_list ?? Array.Empty<int>());

            foreach (string file in CustomDeckStore.EnumerateDeckFiles().ToList())
            {
                try
                {
                    JsonData deck = JsonMapper.ToObject(File.ReadAllText(file));
                    if (deletedDeckNos.Contains(deck["deck_no"].ToInt()))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        $"[CustomFormats] Failed to delete deck {file}: {ex.Message}");
                }
            }

            InjectEditedDeckListResponse(__instance.ResponseData);
        }

        [HarmonyPatch(typeof(DeckOrderTask), nameof(DeckOrderTask.Parse))]
        [HarmonyPrefix]
        public static void DeckOrderTask_Parse_Prefix(DeckOrderTask __instance)
        {
            DeckOrderTask.DeckOrderTaskParam parameters =
                __instance.Params as DeckOrderTask.DeckOrderTaskParam;
            var orderByDeckNo = (parameters?.deck_order ?? Array.Empty<int>())
                .Select((deckNo, index) => new { deckNo, order = index + 1 })
                .ToDictionary(item => item.deckNo, item => item.order);

            foreach (string file in CustomDeckStore.EnumerateDeckFiles().ToList())
            {
                try
                {
                    JsonData deck = JsonMapper.ToObject(File.ReadAllText(file));
                    if (orderByDeckNo.TryGetValue(deck["deck_no"].ToInt(), out int order))
                    {
                        deck["order_num"] = order;
                        CustomDeckStore.SaveDeck(deck, file);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        $"[CustomFormats] Failed to reorder deck {file}: {ex.Message}");
                }
            }

            InjectEditedDeckListResponse(__instance.ResponseData);
        }

        private static void InjectEditedDeckListResponse(JsonData response)
        {
            if (response == null || !response.IsObject || !response.Keys.Contains("data"))
            {
                return;
            }

            JsonData decks = CustomDeckStore.LoadDeckList();
            response["data"]["user_deck_list"] = decks;
            if (Data.Load?.data != null)
            {
                Data.Load.data.UserDeckListUnlimited = decks;
            }
        }

        /// <summary>
        /// Patch DeckLeaderSkinUpdateTask.Parse to update the local unlimited deck JSON files instead of sending a request to the server.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__result"></param>
        /// <returns></returns>
        [HarmonyPatch(typeof(DeckLeaderSkinUpdateTask), nameof(DeckLeaderSkinUpdateTask.Parse))]
        [HarmonyPrefix]
        public static bool DeckLeaderSkinUpdateTask_Parse_Prefix(DeckLeaderSkinUpdateTask __instance, ref int __result)
        {
            __result = __instance.resultCode = 1;
            CustomDeckStore.EnumerateDeckFiles().ToList().ForEach(file =>
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonMapper.ToObject(json);
                    var parameters = (DeckLeaderSkinUpdateTask.Param)__instance.Params;
                    if (data["deck_no"].ToInt() == parameters.deck_no)
                    {
                        data["leader_skin_id"] = parameters.leader_skin_id;
                        DeckListUtility.DeckUpdate(data, __instance._updateDeckFormat, DeckAttributeType.CustomDeck);
                        CustomDeckStore.SaveDeck(data, file);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[CustomFormats] Failed to update deck {file}: {ex.Message}");
                }
            });
            return false;
        }
        /// <summary>
        /// Patch DeckUpdateSleeveTask.Parse to update the local unlimited deck JSON files instead of sending a request to the server.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__result"></param>
        /// <returns></returns>
        [HarmonyPatch(typeof(DeckUpdateSleeveTask), nameof(DeckUpdateSleeveTask.Parse))]
        [HarmonyPrefix]
        public static bool DeckUpdateSleeveTask_Parse_Prefix(DeckUpdateSleeveTask __instance, ref int __result)
        {
            __result = __instance.resultCode = 1;
            CustomDeckStore.EnumerateDeckFiles().ToList().ForEach(file =>
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonMapper.ToObject(json);
                    var parameters = (DeckUpdateSleeveTask.SleeveSetTaskParam)__instance.Params;
                    if (data["deck_no"].ToInt() == parameters.deck_no)
                    {
                        data["sleeve_id"] = parameters.sleeve_id;
                        DeckListUtility.DeckUpdate(data, __instance._updateDeckFormat, DeckAttributeType.CustomDeck);
                        CustomDeckStore.SaveDeck(data, file);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[CustomFormats] Failed to update deck {file}: {ex.Message}");
                }
            });
            return false;
        }
        [HarmonyPatch(typeof(DeckNameUpdateTask), nameof(DeckNameUpdateTask.Parse))]
        [HarmonyPrefix]
        public static bool DeckNameUpdateTask_Parse_Prefix(DeckNameUpdateTask __instance, ref int __result)
        {
            __result = __instance.resultCode = 1;
            CustomDeckStore.EnumerateDeckFiles().ToList().ForEach(file =>
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonMapper.ToObject(json);
                    var parameters = (DeckNameUpdateTask.DeckNameUpdateTaskParam)__instance.Params;
                    if (data["deck_no"].ToInt() == parameters.deck_no)
                    {
                        data["deck_name"] = parameters.deck_name;
                        DeckListUtility.DeckUpdate(data, __instance._updateDeckFormat, DeckAttributeType.CustomDeck);
                        CustomDeckStore.SaveDeck(data, file);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[CustomFormats] Failed to update deck {file}: {ex.Message}");
                }
            });
            return false;
        }
        [HarmonyPatch(typeof(DeckUpdateTask), nameof(DeckUpdateTask.Parse))]
        [HarmonyPrefix]
        public static bool DeckUpdateTask_Parse_Prefix(DeckUpdateTask __instance, ref int __result)
        {
            __result = __instance.resultCode = 1;
            CustomDeckStore.EnumerateDeckFiles().ToList().ForEach(file =>
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonMapper.ToObject(json);
                    var parameters = (DeckUpdateTask.DeckUpdateTaskParam)__instance.Params;
                    if (data["deck_no"].ToInt() == parameters.deck_no)
                    {
                        JsonData cardIdArrayJson = new JsonData();
                        
                        if (parameters.is_delete == 1)
                        {
                            File.Delete(file);
                            LoadLocalDecks(Data.Load.data);
                        }
                        else if(parameters.card_id_array != null)
                        {
                            data["format_id"] = CustomFormatContext.DeckEditFormatId;
                            if (parameters.class_id > 0)
                            {
                                data["class_id"] = parameters.class_id;
                            }
                            foreach (int cardId in parameters.card_id_array)
                            {
                                cardIdArrayJson.Add(cardId);
                            }
                            data["card_id_array"] = cardIdArrayJson;
                            data["is_complete_deck"] = 1;
                            if (!string.IsNullOrEmpty(parameters.deck_name))
                            {
                                data["deck_name"] = parameters.deck_name;
                            }
                            DeckListUtility.DeckUpdate(data, __instance._updateDeckFormat, DeckAttributeType.CustomDeck);
                            __instance.AchievedInfo = new AchievedInfo();
                            CustomDeckStore.SaveDeck(data, file);
                        }
                        
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[CustomFormats] Failed to update deck {file}: {ex.Message}");
                }
            });
            return false;
        }

        #endregion
    }
}
