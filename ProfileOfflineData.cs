using Cute;
using LitJson;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Wizard;

namespace Shadowbus
{
    internal static class ProfileOfflineData
    {
        private const int MaxClassLevel = 150;
        private const int MaxClassExperience = 199250;
        private const int MaxProfileStat = 999999;

        private static readonly object SettingsLock = new object();

        private sealed class LocalProfileSettings
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("emblem_id")]
            public long? EmblemId { get; set; }

            [JsonProperty("degree_id")]
            public int? DegreeId { get; set; }

            [JsonProperty("country_code")]
            public string CountryCode { get; set; }

            [JsonProperty("is_official_mark_displayed")]
            public bool? IsOfficialMarkDisplayed { get; set; }

            [JsonProperty("leader_skins")]
            public Dictionary<int, LocalLeaderSkinSetting> LeaderSkins { get; set; } =
                new Dictionary<int, LocalLeaderSkinSetting>();
        }

        private sealed class LocalLeaderSkinSetting
        {
            [JsonProperty("current_chara_id")]
            public int CurrentCharaId { get; set; }

            [JsonProperty("is_random")]
            public bool IsRandom { get; set; }

            [JsonProperty("skin_ids")]
            public List<int> SkinIds { get; set; } = new List<int>();
        }

        internal static bool CanHandle(string taskName)
        {
            return taskName == nameof(ProfileTask) ||
                taskName == nameof(NameUpdateTask) ||
                taskName == nameof(EmblemUpdateTask) ||
                taskName == nameof(DegreeUpdateTask) ||
                taskName == nameof(CountryCodeSetTask) ||
                taskName == nameof(OfficialMarkDisplayTask) ||
                taskName == nameof(LeaderSkinUpdateTask) ||
                taskName == nameof(RankingMasterMyHistoriesTask) ||
                taskName == nameof(GetGrandMasterTask) ||
                taskName == nameof(MasterResetMonthTask);
        }

        internal static bool TryCreateResponse(NetworkTask task, out JsonData response)
        {
            response = null;
            try
            {
                object data;
                if (task is ProfileTask)
                {
                    data = CreateProfileData();
                }
                else if (task is NameUpdateTask &&
                    task.Params is NameUpdateTask.NameUpdateTaskParam nameParam)
                {
                    UpdateSettings(settings => settings.Name = nameParam.name);
                    data = new Dictionary<string, object>();
                }
                else if (task is EmblemUpdateTask &&
                    task.Params is EmblemUpdateTask.EmblemUpdateTaskParam emblemParam)
                {
                    UpdateSettings(settings => settings.EmblemId = emblemParam.emblem_id);
                    data = new Dictionary<string, object>();
                }
                else if (task is DegreeUpdateTask &&
                    task.Params is DegreeUpdateTask.DegreeUpdateTaskParam degreeParam)
                {
                    UpdateSettings(settings => settings.DegreeId = degreeParam.degree_id);
                    data = new Dictionary<string, object>();
                }
                else if (task is CountryCodeSetTask &&
                    task.Params is CountryCodeSetTask.CountryCodeSetTaskParam countryParam)
                {
                    UpdateSettings(settings => settings.CountryCode = countryParam.country_code ?? string.Empty);
                    data = new Dictionary<string, object>();
                }
                else if (task is OfficialMarkDisplayTask &&
                    task.Params is OfficialMarkDisplayTask.OfficialMarkDisplayTaskParam officialParam)
                {
                    UpdateSettings(settings =>
                        settings.IsOfficialMarkDisplayed = officialParam.is_official_mark_displayed != 0);
                    data = new Dictionary<string, object>();
                }
                else if (task is LeaderSkinUpdateTask &&
                    task.Params is LeaderSkinUpdateTask.LeaderSkinUpdateTaskParam leaderParam)
                {
                    data = CreateLeaderSkinUpdateData(leaderParam);
                }
                else if (task is RankingMasterMyHistoriesTask)
                {
                    data = CreateMasterHistoryData(IsCrossoverTask(task));
                }
                else if (task is GetGrandMasterTask)
                {
                    data = CreateGrandMasterData(IsCrossoverTask(task));
                }
                else if (task is MasterResetMonthTask)
                {
                    data = CreateMasterResetData();
                }
                else
                {
                    return false;
                }

                response = JsonMapper.ToObject(JsonConvert.SerializeObject(CreateResponseEnvelope(data)));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    $"[ProfileOffline] Failed to create local data for {task.GetType().Name}: {ex}");
                return false;
            }
        }

        internal static void ApplyAfterLoad(LoadDetail loadDetail)
        {
            if (loadDetail == null)
            {
                return;
            }

            ApplyMaxProfileStats(loadDetail);
            LocalProfileSettings settings = LoadSettings();
            ApplySettings(loadDetail, settings);

            Plugin.Logger.LogInfo(
                $"[ProfileOffline] Applied full profile data and local settings for '{loadDetail._userInfo.name}'.");
        }

        internal static P2PProfile CreateP2PProfile(int viewerId)
        {
            P2PProfile profile = new P2PProfile
            {
                ViewerId = viewerId,
                UserName = "Player",
                CountryCode = string.Empty
            };

            try
            {
                profile.UserName = PlayerStaticData.UserName ?? "Player";
                profile.Rank = PlayerStaticData.UserRankHighAllFormat();
                profile.BattlePoint = PlayerStaticData.UserBattlePointHighFormat();
                profile.MasterPoint = PlayerStaticData.UserMasterPointHighAllFormat();
                profile.DegreeId = PlayerStaticData.UserDegreeID;
                profile.EmblemId = PlayerStaticData.UserEmblemID;
                profile.CountryCode = PlayerStaticData.UserCountryCode ?? string.Empty;
                profile.IsOfficial = PlayerStaticData.IsOfficialUserDisplay;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    "[ProfileOffline] Some live profile fields were unavailable for P2P: " + ex.Message);
            }

            LocalProfileSettings settings = LoadSettings();
            if (settings.Name != null)
            {
                profile.UserName = settings.Name;
            }
            if (settings.EmblemId.HasValue)
            {
                profile.EmblemId = settings.EmblemId.Value;
            }
            if (settings.DegreeId.HasValue)
            {
                profile.DegreeId = settings.DegreeId.Value;
            }
            if (settings.CountryCode != null)
            {
                profile.CountryCode = settings.CountryCode;
            }
            if (settings.IsOfficialMarkDisplayed.HasValue)
            {
                profile.IsOfficial = settings.IsOfficialMarkDisplayed.Value;
            }
            return profile;
        }

        private static object CreateProfileData()
        {
            LoadDetail loadDetail = Data.Load?.data;
            ApplyMaxProfileStats(loadDetail);
            LocalProfileSettings settings = LoadSettings();
            IDictionary<int, ClassCharaPrm> classParameters =
                GameMgr.GetIns().GetDataMgr().GetClassPrmDictionary();
            List<Dictionary<string, object>> classList = new List<Dictionary<string, object>>();

            for (int classId = 1; classId <= 8; classId++)
            {
                if (!classParameters.TryGetValue(classId, out ClassCharaPrm classParameter))
                {
                    continue;
                }

                int currentCharaId = classParameter.CurrentCharaData?.chara_id ?? classId;
                bool isRandom = classParameter.IsRandomLeaderSkin;
                List<int> skinIds = classParameter.LeaderSkinIdList.ToList();
                if (settings.LeaderSkins != null &&
                    settings.LeaderSkins.TryGetValue(classId, out LocalLeaderSkinSetting leaderSetting))
                {
                    currentCharaId = leaderSetting.CurrentCharaId > 0
                        ? leaderSetting.CurrentCharaId
                        : currentCharaId;
                    isRandom = leaderSetting.IsRandom;
                    skinIds = leaderSetting.SkinIds?.Where(id => id > 0).Distinct().ToList() ??
                        new List<int>();
                }
                if (skinIds.Count == 0)
                {
                    skinIds.Add(classParameter.CurrentCharaData?.skin_id ?? classId);
                }

                classList.Add(new Dictionary<string, object>
                {
                    ["class_id"] = classId,
                    ["is_available"] = 1,
                    ["level"] = MaxClassLevel,
                    ["exp"] = MaxClassExperience,
                    ["is_random_leader_skin"] = isRandom ? 1 : 0,
                    ["leader_skin_id"] = currentCharaId,
                    ["leader_skin_id_list"] = skinIds,
                    ["default_leader_skin_id"] = classParameter.DefaultCharaData?.chara_id ?? classId
                });
            }

            return new Dictionary<string, object>
            {
                ["user_rank_match_total_win"] = MaxProfileStat,
                ["user_class_list"] = classList
            };
        }

        private static object CreateLeaderSkinUpdateData(
            LeaderSkinUpdateTask.LeaderSkinUpdateTaskParam param)
        {
            List<int> skinIds = (param.is_random_leader_skin
                    ? param.leader_skin_id_list ?? Array.Empty<int>()
                    : new[] { param.leader_skin_id })
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (skinIds.Count == 0)
            {
                throw new InvalidOperationException("Leader skin selection did not contain a valid skin id.");
            }

            int selectedSkinId = skinIds[0];
            ClassCharacterMasterData selectedSkin =
                GameMgr.GetIns().GetDataMgr().GetCharaPrmBySkinId(selectedSkinId);
            int currentCharaId = selectedSkin?.chara_id ?? selectedSkinId;

            UpdateSettings(settings =>
            {
                if (settings.LeaderSkins == null)
                {
                    settings.LeaderSkins = new Dictionary<int, LocalLeaderSkinSetting>();
                }
                settings.LeaderSkins[param.class_id] = new LocalLeaderSkinSetting
                {
                    CurrentCharaId = currentCharaId,
                    IsRandom = param.is_random_leader_skin,
                    SkinIds = skinIds
                };
            });

            return new Dictionary<string, object>
            {
                ["is_random_leader_skin"] = param.is_random_leader_skin,
                ["leader_skin_id"] = selectedSkinId,
                ["leader_skin_id_list"] = skinIds
            };
        }

        private static object CreateMasterHistoryData(bool isCrossover)
        {
            int maxRankId = GetMaxRankId(Data.Load?.data);
            int periodId = 1;
            Dictionary<string, object> period = new Dictionary<string, object>
            {
                ["id"] = periodId,
                ["period_num"] = 1,
                ["begin_time"] = "2026-01-01 00:00:00",
                ["end_time"] = "2099-12-31 23:59:59",
                ["is_calculated"] = true
            };

            Dictionary<string, object> histories = new Dictionary<string, object>();
            IEnumerable<Format> formats = isCrossover
                ? new[] { Format.Crossover }
                : new[] { Format.Rotation, Format.Unlimited };
            foreach (Format format in formats)
            {
                string formatKey = Data.FormatConvertApi(format).ToString(CultureInfo.InvariantCulture);
                histories[formatKey] = new Dictionary<string, object>
                {
                    [periodId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object>
                    {
                        ["rank"] = 1,
                        ["score"] = MaxProfileStat,
                        ["rank_id"] = maxRankId
                    }
                };
            }

            return new Dictionary<string, object>
            {
                ["periods"] = new Dictionary<string, object>
                {
                    [isCrossover ? "crossover" : "normal"] = new[] { period }
                },
                ["histories"] = histories
            };
        }

        private static object CreateGrandMasterData(bool isCrossover)
        {
            int maxRankId = GetMaxRankId(Data.Load?.data);
            Dictionary<string, object> pointsByFormat = new Dictionary<string, object>();
            IEnumerable<Format> formats = isCrossover
                ? new[] { Format.Crossover }
                : new[] { Format.Rotation, Format.Unlimited };
            foreach (Format format in formats)
            {
                List<Dictionary<string, object>> periods = new List<Dictionary<string, object>>();
                for (int index = 0; index < UserRank.GRAND_MASTER_PERIOD; index++)
                {
                    periods.Add(new Dictionary<string, object>
                    {
                        ["ranking_period_id"] = index + 1,
                        ["ranking_period_num"] = index + 1,
                        ["master_point"] = MaxProfileStat,
                        ["rank"] = maxRankId
                    });
                }
                pointsByFormat[Data.FormatConvertApi(format).ToString(CultureInfo.InvariantCulture)] = periods;
            }

            return new Dictionary<string, object>
            {
                ["user_period_master_point"] = pointsByFormat
            };
        }

        private static bool IsCrossoverTask(NetworkTask task)
        {
            return task.Url.IndexOf("crossover/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object CreateMasterResetData()
        {
            int maxRankId = GetMaxRankId(Data.Load?.data);
            Dictionary<string, object> data = new Dictionary<string, object>();
            foreach (Format format in new[] { Format.Rotation, Format.Unlimited })
            {
                data[Data.FormatConvertApi(format).ToString(CultureInfo.InvariantCulture)] =
                    new Dictionary<string, object>
                    {
                        ["rank"] = maxRankId,
                        ["master_point"] = MaxProfileStat,
                        ["target_grand_master_point"] = MaxProfileStat,
                        ["current_grand_master_point"] = MaxProfileStat,
                        ["is_promotion"] = 0
                    };
            }
            return data;
        }

        private static void ApplyMaxProfileStats(LoadDetail loadDetail)
        {
            if (loadDetail == null)
            {
                return;
            }

            IDictionary<int, ClassCharaPrm> classParameters =
                GameMgr.GetIns().GetDataMgr().GetClassPrmDictionary();
            for (int classId = 1; classId <= 8; classId++)
            {
                if (!classParameters.TryGetValue(classId, out ClassCharaPrm classParameter))
                {
                    continue;
                }
                classParameter.SetClassCharaLv(MaxClassLevel);
                classParameter.SetClassCharaExp(MaxClassExperience);
                classParameter.SetClassCharaWin(MaxProfileStat);
                classParameter.SetClassCharaBattleCount(MaxProfileStat);
            }

            int maxRankId = GetMaxRankId(loadDetail);
            foreach (Format format in new[] { Format.Rotation, Format.Unlimited })
            {
                if (!loadDetail._userRank.TryGetValue((int)format, out UserRank userRank))
                {
                    continue;
                }
                userRank.rank = maxRankId;
                userRank.battle_point = MaxProfileStat;
                userRank.master_point = MaxProfileStat;
                userRank.is_master_rank = true;
                userRank.is_grand_master_rank = true;
                userRank.grandMasterData.targetMasterPoint = MaxProfileStat;
                userRank.grandMasterData.currentMasterPoint = MaxProfileStat;
                for (int index = 0; index < UserRank.GRAND_MASTER_PERIOD; index++)
                {
                    userRank.grandMasterData.id[index] = index + 1;
                    userRank.grandMasterData.periodNum[index] = index + 1;
                    userRank.grandMasterData.masterPoint[index] = MaxProfileStat;
                    userRank.grandMasterData.rankId[index] = maxRankId;
                }
            }
            UserRank.IsGrandMasterAvailability = true;
        }

        private static int GetMaxRankId(LoadDetail loadDetail)
        {
            if (loadDetail?.RankInfoList == null || loadDetail.RankInfoList.Count == 0)
            {
                return UserRank.MASTER_RANK_INDEX + 1;
            }
            return loadDetail.RankInfoList.Max(rank => rank.RankId);
        }

        private static Dictionary<string, object> CreateResponseEnvelope(object data)
        {
            return new Dictionary<string, object>
            {
                ["data_headers"] = new Dictionary<string, object>
                {
                    ["short_udid"] = Certification.ShortUdid,
                    ["viewer_id"] = Certification.ViewerId,
                    ["sid"] = Certification.SessionId ?? string.Empty,
                    ["servertime"] = 0L,
                    ["result_code"] = 1
                },
                ["data"] = data
            };
        }

        private static LocalProfileSettings LoadSettings()
        {
            lock (SettingsLock)
            {
                return LoadSettingsUnlocked();
            }
        }

        private static void UpdateSettings(Action<LocalProfileSettings> update)
        {
            lock (SettingsLock)
            {
                LocalProfileSettings settings = LoadSettingsUnlocked();
                update(settings);
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(PathHelper.ProfileSettingsPath, json, Encoding.UTF8);
                ApplySettings(Data.Load?.data, settings);
                Plugin.Logger.LogInfo(
                    $"[ProfileOffline] Saved local profile settings to {PathHelper.ProfileSettingsPath}.");
            }
        }

        private static void ApplySettings(LoadDetail loadDetail, LocalProfileSettings settings)
        {
            if (loadDetail?._userInfo == null || settings == null)
            {
                return;
            }
            if (settings.Name != null)
            {
                loadDetail._userInfo.name = settings.Name;
            }
            if (settings.EmblemId.HasValue)
            {
                loadDetail._userInfo.selected_emblem_id = settings.EmblemId.Value;
            }
            if (settings.DegreeId.HasValue)
            {
                loadDetail._userInfo.selected_degree_id = settings.DegreeId.Value;
            }
            if (settings.CountryCode != null)
            {
                loadDetail._userInfo.country_code = settings.CountryCode;
            }
            if (settings.IsOfficialMarkDisplayed.HasValue)
            {
                PlayerStaticData.IsOfficialUserDisplay = settings.IsOfficialMarkDisplayed.Value;
            }
        }

        private static LocalProfileSettings LoadSettingsUnlocked()
        {
            if (!File.Exists(PathHelper.ProfileSettingsPath))
            {
                return new LocalProfileSettings();
            }
            try
            {
                LocalProfileSettings settings = JsonConvert.DeserializeObject<LocalProfileSettings>(
                    File.ReadAllText(PathHelper.ProfileSettingsPath, Encoding.UTF8));
                if (settings == null)
                {
                    return new LocalProfileSettings();
                }
                settings.LeaderSkins = settings.LeaderSkins ??
                    new Dictionary<int, LocalLeaderSkinSetting>();
                return settings;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    $"[ProfileOffline] Ignored invalid local profile settings: {ex.Message}");
                return new LocalProfileSettings();
            }
        }
    }
}
