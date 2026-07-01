using HarmonyLib;
using LitJson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                    foreach(var cardId in deck.GetCardIdList())
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
            catch (JsonException jsonEx)
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
    }
}
