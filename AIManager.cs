using Cute;
using HarmonyLib;
using LitJson;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Wizard;

namespace Shadowbus
{
    public class AIManager
    {
        [HarmonyPatch(typeof(DataMgr), nameof(DataMgr.SetCurrentEnemyDeckDataFromAIDeck), new Type[] { typeof(string) })]
        [HarmonyPrefix]
        public static bool DataMgr_SetCurrentEnemyDeckDataFromAIDeck_Patch(DataMgr __instance)
        {
            try
            {
                if (!File.Exists(Plugin.AISettingsPath))
                {
                    return true;
                }

                var json = File.ReadAllText(Plugin.AISettingsPath);
                var data = JsonMapper.ToObject(json);
                if (data == null || !data.IsObject || !data.Keys.Contains("deckName"))
                {
                    return true;
                }
                if (data["enable"].ToBoolean() != true)
                {
                    return true;
                }
                string deckName = data["deckName"]?.ToString();
                if (string.IsNullOrEmpty(deckName))
                {
                    return true;
                }
                if (DeckListUtility.DeckGroupDataBase == null)
                {
                    Plugin.Logger.LogError("[AIManager] 游戏原版 DeckGroupDataBase 尚未初始化。");
                    return true;
                }

                DeckGroup deckGroup = DeckListUtility.DeckGroupDataBase.FirstOrDefault(d => d != null && d.DeckFormat == Format.Unlimited && d.AttributeType == DeckAttributeType.CustomDeck);
                if (deckGroup == null || deckGroup.DeckDataList == null)
                {
                    Plugin.Logger.LogWarning("[AIManager] 无法在数据库中找到符合条件的无限模式自定义卡组列表。");
                    return true;
                }

                var deck = deckGroup.DeckDataList.FirstOrDefault(d => d != null && d.GetDeckName() == deckName);
                if (deck == null)
                {
                    Plugin.Logger.LogWarning($"[AIManager] 在本地无限卡组中找不到名为 '{deckName}' 的卡组，请检查拼写。");
                    return true;
                }

                if (__instance._currentEnemyDeckData != null)
                {
                    __instance._currentEnemyDeckData.Clear();
                    foreach (var cardId in deck.GetCardIdList())
                    {
                        __instance._currentEnemyDeckData.Add(cardId);
                    }
                }
                else
                {
                    __instance._currentEnemyDeckData = deck.GetCardIdList().ToList();
                }

                Plugin.Logger.LogInfo($"[AIManager] 成功将敌人 AI 卡组替换为: {deckName}");
                return false;
            }
            catch (Newtonsoft.Json.JsonException jsonEx)
            {
                Plugin.Logger.LogError($"[DataMgr_Patch] JSON 解析失败，请检查配置文件格式: {jsonEx.Message}");
            }
            catch (IOException ioEx)
            {
                Plugin.Logger.LogError($"[DataMgr_Patch] 文件读取异常 (可能被其他程序占用): {ioEx.Message}");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[DataMgr_Patch] 发生未预料的严重错误:\n{ex.Message}\n{ex.StackTrace}");
            }
            return true;
        }

        [HarmonyPatch(typeof(ClassSelectionPage), nameof(ClassSelectionPage.ShowSelectDifficultyDialog))]
        [HarmonyPrefix]
        public static bool ClassSelectionPage_ShowSelectDifficultyDialog_Prefix(ClassSelectionPage __instance)
        {

            if (!File.Exists(Plugin.AISettingsPath))
            {
                return true;
            }
            var json = File.ReadAllText(Plugin.AISettingsPath);
            var data = JsonMapper.ToObject(json);
            if (data == null || !data.IsObject)
            {
                return true;
            }
            if (data["enable"].ToBoolean() != true)
            {
                return true;
            }
            int AIMaxLife = data["maxLife"]?.ToInt() ?? -1;
            int AILogicLevel = data["logic"]?.ToInt() ?? -1;
            int AIDifficulty = data["difficulty"]?.ToInt() ?? -1;

            int enemyClassId = __instance._selectCharaMasterData.class_id;
            List<PracticeData> practiceDataList = Data.PracticeDataMgr.GetClassDataList(enemyClassId);
            if (practiceDataList.Count <= 0)
            {
                return false;
            }

            int num = -1;
            List<string> list = new List<string>();
            for (int i = 0; i < practiceDataList.Count; i++)
            {
                list.Add(practiceDataList[i].Text);
                if (num < 0 && !practiceDataList[i].IsMaintenance)
                {
                    num = i;
                }
            }
            if (num < 0)
            {
                num = 0;
            }
            DialogBase dia = null;
            int selectIndex = num;
            Action<int> action = delegate (int selectIdx)
            {
                selectIndex = selectIdx;
                UIManager.SetObjectToGrey(dia.button1.gameObject, practiceDataList[selectIndex].IsMaintenance, null, null);
            };

            dia = DrumrollDialog.Create(list, num, action, null, null, "");
            dia.SetTitleLabel(Data.SystemText.Get("Story_0022"));
            dia.SetButtonLayout(DialogBase.ButtonLayout.DecisionBtn);

            dia.onPushButton1 = delegate
            {
                UIManager.GetInstance().createInSceneCenterLoading(false, false, true, null);
                DataMgr dataMgr = GameMgr.GetIns().GetDataMgr();
                dataMgr.Load();
                dataMgr.SetEnemyCharaId(enemyClassId);

                PracticeData practiceData = practiceDataList[selectIndex];
                PracticeAISettingData settingData = Data.Master.PracticeAISettingList.GetSettingData(enemyClassId, practiceData.AIDeckLevel);
                if (AIDifficulty >= 0) { settingData.Difficulty = AIDifficulty; }
                if (AILogicLevel >= 0) { settingData.LogicLevel = AILogicLevel; }
                if (AIMaxLife >= 0) { settingData.MaxLife = AIMaxLife; }

                Data.Master.LoadAICsv(new AICsvLoadingInfo(settingData.DeckId, settingData.StyleId, settingData.EmoteId), delegate
                {
                    UIManager.GetInstance().closeInSceneCenterLoading(true, false);
                    dataMgr.SetCurrentEnemyDeckDataFromAIDeck(enemyClassId, settingData.Difficulty, settingData.LogicLevel, settingData.MaxLife, settingData.DeckId, settingData.StyleId, settingData.EmoteId, true, -1, null);
                    dataMgr.LoadEnemyClassData();
                    dataMgr.PracticeDifficultyDegreeId = practiceData.DegreeId;
                    dataMgr.SetSoroPlay3DFieldID(practiceData.Battle3dFieldId);
                    GameMgr.GetIns().GetDataMgr().Practice3DfieldId = practiceData.Battle3dFieldId;
                    dia.CloseWithoutSelect();
                    PracticeStartTask practiceStartTask = new PracticeStartTask();
                    __instance.StartCoroutine(Toolbox.NetworkManager.Connect(practiceStartTask, delegate (NetworkTask.ResultCode ret)
                    {
                        UIManager.ChangeViewSceneParam changeViewSceneParam = new UIManager.ChangeViewSceneParam();
                        changeViewSceneParam.IsShow_CardIntroduction = true;
                        UIManager.GetInstance().ChangeViewScene(UIManager.ViewScene.Battle, changeViewSceneParam, null);
                    }, null, null, true, false, true, true));
                });
            };

            dia.ClickSe_Btn1 = Se.TYPE.SYS_BTN_DECIDE_TRANS;
            return false;
        }

        [HarmonyPatch(typeof(Master), nameof(Master.StartLoadAIIndividualData))]
        [HarmonyPostfix]
        public static void Master_StartLoadAIIndividualData_Postfix(Master __instance)
        {
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_basic.json"), JsonConvert.SerializeObject(__instance.AIBasicDataList));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_common.json"), JsonConvert.SerializeObject(__instance.AICommonDataList));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_ally_common.json"), JsonConvert.SerializeObject(__instance.AIAllyCommonDataList));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_deck.json"), JsonConvert.SerializeObject(__instance.AIDeckDic));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_emote.json"), JsonConvert.SerializeObject(__instance.AIEmoteDic));
            File.WriteAllText(Path.Combine(PathHelper.AIDataPath, "ai_style.json"), JsonConvert.SerializeObject(__instance.AIStyleDic));
        }
        [HarmonyPatch(typeof(Master), nameof(Master.StartLoadAIDeckData))]
        [HarmonyPrefix]
        public static bool Master_StartLoadAIDeckData_Prefix(Master __instance, ref int deckID)
        {
            var json = File.ReadAllText(Plugin.AISettingsPath);
            var data = JsonMapper.ToObject(json);
            if (data == null || !data.IsObject || !data.Keys.Contains("deckAI") || string.IsNullOrEmpty(data["deckAI"].ToString()))
            {
                return true;
            }
            string path = Path.Combine(PathHelper.AIDataPath, $"{data["deckAI"]}.json");
            if (!File.Exists(path)) {
                return true;
            }
            string aiEntriesJson = File.ReadAllText(path);
            __instance.AIDeckDic ??= new Dictionary<string, AICardDataAssetSet>();
            List<AICardDataAsset> entries = JsonConvert.DeserializeObject<List<AICardDataAsset>>(aiEntriesJson);
            string text = "ai/" + __instance.AIDeckFileNameList.GetFileName(deckID);
            __instance.LoadAIDeckData(text);
            if (__instance.AIDeckDic.TryGetValue(text, out AICardDataAssetSet existingDeckAI)) {
                existingDeckAI.Set.AddRange(entries);
            }
            return false;
        }
    }
}
