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
using Wizard.Title;

namespace Shadowbus
{
    public class Offlinizer
    {
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
            if (Certification.ViewerId == 0)
            { Certification.ViewerId = 1; }
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
            JsonData unlimitedDeckList = new JsonData();
            HashSet<int> existingDeckNos = new HashSet<int>();
            bool hasEmptyDecks = false;
            Directory.GetFiles(Plugin.UnlimitedDeckPath, "*.json").ToList().ForEach(file =>
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonMapper.ToObject(json);
                    unlimitedDeckList.Add(data);
                    existingDeckNos.Add(data["deck_no"].ToInt());
                    var cards = data["card_id_array"];
                    if (cards.IsArray && cards.Count == 0)
                    {
                        hasEmptyDecks = true;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[Offlinizer] Failed to load unlimited deck from {file}: {ex.Message}");
                }
            });
            if (!hasEmptyDecks)
            {
                int smallestMissingDeckNo = 1;
                while (existingDeckNos.Contains(smallestMissingDeckNo))
                {
                    smallestMissingDeckNo++;
                }
                string json = $"{{\r\n  \"deck_no\": {smallestMissingDeckNo},\r\n  \"class_id\": 1,\r\n  \"sleeve_id\": 3000011,\r\n  \"leader_skin_id\": 0,\r\n  \"deck_name\": \"\",\r\n  \"card_id_array\": [],\r\n  \"is_complete_deck\": 0,\r\n  \"restricted_card_exists\": false,\r\n  \"is_available_deck\": 1,\r\n  \"maintenance_card_ids\": [],\r\n  \"is_include_un_possession_card\": false,\r\n  \"is_random_leader_skin\": 0,\r\n  \"leader_skin_id_list\": [0],\r\n  \"order_num\": 0,\r\n  \"create_deck_time\": null\r\n}}";
                File.WriteAllText(Path.Combine(Plugin.UnlimitedDeckPath, $"deck_{smallestMissingDeckNo}.json"), json);
                var emptyDeck = JsonMapper.ToObject(json);
                unlimitedDeckList.Add(emptyDeck);
            }
            __instance.UserDeckListUnlimited = unlimitedDeckList;
            if (Data.Master.isMasterDataLoaded)
            {
                DeckListUtility.SetDeckListDataWithLodeIndex();
            }
        }

        #region DeckEdit
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
            Directory.GetFiles(Plugin.UnlimitedDeckPath, "*.json").ToList().ForEach(file =>
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
                        File.WriteAllText(file, data.ToJson());
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[Offlinizer] Failed to load unlimited deck from {file}: {ex.Message}");
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
            Directory.GetFiles(Plugin.UnlimitedDeckPath, "*.json").ToList().ForEach(file =>
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
                        File.WriteAllText(file, data.ToJson());
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[Offlinizer] Failed to load unlimited deck from {file}: {ex.Message}");
                }
            });
            return false;
        }
        [HarmonyPatch(typeof(DeckNameUpdateTask), nameof(DeckNameUpdateTask.Parse))]
        [HarmonyPrefix]
        public static bool DeckNameUpdateTask_Parse_Prefix(DeckNameUpdateTask __instance, ref int __result)
        {
            __result = __instance.resultCode = 1;
            Directory.GetFiles(Plugin.UnlimitedDeckPath, "*.json").ToList().ForEach(file =>
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
                        File.WriteAllText(file, data.ToJson());
                        File.Move(file, Path.Combine(Plugin.UnlimitedDeckPath, $"{parameters.deck_name}.json"));
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[Offlinizer] Failed to load unlimited deck from {file}: {ex.Message}");
                }
            });
            return false;
        }
        [HarmonyPatch(typeof(DeckUpdateTask), nameof(DeckUpdateTask.Parse))]
        [HarmonyPrefix]
        public static bool DeckUpdateTask_Parse_Prefix(DeckUpdateTask __instance, ref int __result)
        {
            __result = __instance.resultCode = 1;
            Directory.GetFiles(Plugin.UnlimitedDeckPath, "*.json").ToList().ForEach(file =>
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
                            LoadLocalUnlimitedDecks(Data.Load.data);
                        }
                        else if(parameters.card_id_array != null)
                        {
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
                                File.Move(file, Path.Combine(Plugin.UnlimitedDeckPath, $"{parameters.deck_name}.json"));
                            }
                            DeckListUtility.DeckUpdate(data, __instance._updateDeckFormat, DeckAttributeType.CustomDeck);
                            __instance.AchievedInfo = new AchievedInfo();
                            File.WriteAllText(Path.Combine(Plugin.UnlimitedDeckPath, $"{parameters.deck_name}.json"), data.ToJson());
                        }
                        
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"[Offlinizer] Failed to load unlimited deck from {file}: {ex.Message}");
                }
            });
            return false;
        }
        #endregion
    }
}
