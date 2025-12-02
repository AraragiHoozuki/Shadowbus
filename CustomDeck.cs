using BepInEx.Logging;
using Cute;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Wizard;
using Wizard.Battle.View;
using Wizard.DeckCardEdit;

namespace Shadowbus
{
    public class CustomDeck
    {
        [HarmonyPatch(typeof(SBattleLoad), "InitPlayer")]
        [HarmonyPrefix]
        public static bool SBattleLoad_InitPlayer(SBattleLoad __instance)
        {
            var f_gameMgr = AccessTools.Field(typeof(SBattleLoad), "_gameMgr");
            GameMgr gameMgr = f_gameMgr.GetValue(__instance) as GameMgr;
            var f_btlMgr = AccessTools.Field(typeof(SBattleLoad), "_btlMgr");
            BattleManagerBase btlMgr = f_btlMgr.GetValue(__instance) as BattleManagerBase;
            if (Plugin.Instance.UseCustomDeckSelf)
            {
                var path = Path.Combine("Mods", "Decks", Plugin.Instance.CustomDeckSelf);
                if (File.Exists(path))
                {
                    Plugin.Logger.LogMessage($"Loading deck: {path}");
                    BattlePlayer player = btlMgr.BattlePlayer;
                    btlMgr.BattleResourceMgr.LoadSleeveMaterial(gameMgr.GetDataMgr().GetPlayerSleeveId(), true).Play();

                    List<int> deck = LoadDeck(path);
                    if (deck.Count < 6)
                    {
                        Plugin.Logger.LogWarning($"custom deck has less then 6 cards, original deck will be used.");
                        return true;
                    }
                    CardVoiceInfoCache.CacheCardVoiceInfoForBattle(deck);
                    for (int i = 0; i < deck.Count; i++)
                    {
                        BattleCardBase card = CardCreatorBase.CreateCard(deck[i], true, i + 1, __instance, btlMgr, btlMgr.BattleResourceMgr, btlMgr.CreatePlayerInnerOptionsBuilder(), false);
                        player.AddToDeck(card, false, null);
                        Plugin.Logger.LogInfo($"{card.BaseParameter.CardName}(id:{card.BaseParameter.BaseCardId}, foil:{card.BaseParameter.FoilCardId}) is added to deck");
                    }
                    player.BattleStartDeckCardList = [.. player.DeckCardList];
                    player.cardTotalNum = deck.Count + 1;
                    return false;
                }
            }
            return true;
        }
        //[HarmonyPatch(typeof(SBattleLoad), nameof(SBattleLoad.InitEnemy))]
        //[HarmonyPrefix]
        //public static bool InitEnemy(SBattleLoad __instance)
        //{
        //    var f_gameMgr = AccessTools.Field(typeof(SBattleLoad), "_gameMgr");
        //    GameMgr gameMgr = f_gameMgr.GetValue(__instance) as GameMgr;
        //    var f_btlMgr = AccessTools.Field(typeof(SBattleLoad), "_btlMgr");
        //    BattleManagerBase btlMgr = f_btlMgr.GetValue(__instance) as BattleManagerBase;
        //    if (Plugin.Instance.UseCustomDeckOpponent)
        //    {
        //        var path = Path.Combine("Mods", "Decks", Plugin.Instance.CustomDeckOpponent);
        //        if (File.Exists(path))
        //        {
        //            Plugin.Logger.LogMessage($"Loading opponent's deck: {path}");
        //            BattleEnemy enemy = btlMgr.BattleEnemy;
        //            btlMgr.BattleResourceMgr.LoadSleeveMaterial(gameMgr.GetDataMgr().GetEnemySleeveId(), true).Play();
        //            List<int> deck = LoadDeck(path);
        //            if (deck.Count < 6)
        //            {
        //                Plugin.Logger.LogWarning($"custom deck has less then 6 cards, original deck will be used.");
        //                return true;
        //            }
        //            CardVoiceInfoCache.CacheCardVoiceInfoForBattle(deck);
        //            for (int i = 0; i < deck.Count; i++)
        //            {
        //                BattleCardBase card = CardCreatorBase.CreateCard(deck[i], true, i + 1, __instance, btlMgr, btlMgr.BattleResourceMgr, btlMgr.CreateEnemyInnerOptionsBuilder(), false);
        //                enemy.AddToDeck(card, false, null);
        //                Plugin.Logger.LogInfo($"{card.BaseParameter.CardName}(id:{card.BaseParameter.BaseCardId}, foil:{card.BaseParameter.FoilCardId}) is added to opponents deck");
        //            }
        //            enemy.BattleStartDeckCardList = [.. enemy.DeckCardList];
        //            enemy.cardTotalNum = deck.Count + 1;
        //            __instance.StartCoroutine(SBattleLoad_LoadComplete(__instance));
        //            return false;
        //        }
        //    }
        //    return true;
        //}

        [HarmonyPatch(typeof(DataMgr), nameof(DataMgr.SetCurrentEnemyDeckDataFromAIDeck))]
        [HarmonyPatch([typeof(string)])]
        [HarmonyPrefix]
        public static bool DataMgr_SetCurrentEnemyDeckDataFromAIDeck_Patch(DataMgr __instance)
        {
            if (Plugin.Instance.UseCustomDeckOpponent)
            {
                var path = Path.Combine("Mods", "Decks", Plugin.Instance.CustomDeckOpponent);
                if (File.Exists(path))
                {
                    List<int> deck = LoadDeck(path);
                    AccessTools.Field(typeof(DataMgr), "_currentEnemyDeckData").SetValue(__instance, deck);
                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(DataMgr), nameof(DataMgr.SetCurrentEnemyDeckData))]
        [HarmonyPrefix]
        public static bool DataMgr_SetCurrentEnemyDeckData_Patch(ref IList<int> deckdata)
        {
            if (Plugin.Instance.UseCustomDeckOpponent)
            {
                var path = Path.Combine("Mods", "Decks", Plugin.Instance.CustomDeckOpponent);
                if (File.Exists(path))
                {
                    List<int> deck = LoadDeck(path);
                    deckdata = deck;
                }
            }
            return true;
        }
        [HarmonyPatch(typeof(FilterController), nameof(FilterController.InitializeFilterForDeckEdit))]
        [HarmonyPrefix]
        public static bool FilterController_InitializeFilterForDeckEdit_Patch(FilterController __instance, ref ClassSet classSet)
        {
            if (Plugin.Instance.CustomDeckSave)  classSet = new ClassSet(CardBasePrm.ClanType.ALL);
            return true;
        }

        [HarmonyPatch(typeof(FilterController), "RemoveTokenCard")]
        [HarmonyPostfix]
        public static void FilterController_RemoveTokenCard_Patch(FilterController __instance, List<int> cardPool, ref List<int> __result)
        {
            if (Plugin.Instance.CustomDeckSave)
            {
                __result.Clear();
                __result.AddRange(cardPool);
            }
        }
        [HarmonyPatch(typeof(UIBase_CardManager), nameof(UIBase_CardManager.SelectCardIDInConditionMask))]
        [HarmonyPrefix]
        public static bool UIBase_CardManager_SelectCardIDInConditionMask_Patch(ref UIBase_CardManager.FilterParameter filterParam)
        {
            if (Plugin.Instance.CustomDeckSave) { 
                filterParam.Craftable = 0;
                filterParam.IsEnableResurgentCard = true;
                filterParam.DisableCardSetidList = [];
            }
            return true;
        }
        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.IsAvailableFormat))]
        [HarmonyPrefix]
        public static bool CardParameter_IsAvailableFormat_Patch(ref bool __result)
        {
            if (Plugin.Instance.CustomDeckSave)
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CardParameter), nameof(CardParameter.GetSameKindNumMaxInFormat))]
        [HarmonyPrefix]
        public static bool CardParameter_GetSameKindNumMaxInFormat_Patch(ref int __result)
        {
            if (Plugin.Instance.CustomDeckSave)
            {
                __result = 999;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CardObject), nameof(CardObject.AttachGrayShader))]
        [HarmonyPrefix]
        public static bool CardObject_AttachGrayShader_Patch()
        {
            if (Plugin.Instance.CustomDeckSave)
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CardSelectListUIBase), nameof(CardSelectListUIBase.IsRemainingAddableCardToSelectionArea))]
        [HarmonyPostfix]
        public static void CardSelectListUIBase_IsRemainingAddableCardToSelectionArea_Patch(ref bool __result)
        {
            if (Plugin.Instance.CustomDeckSave) __result = true;
        }

        [HarmonyPatch(typeof(CardSelectListUIBase), nameof(CardSelectListUIBase.IsAddableByBaseCardId))]
        [HarmonyPrefix]
        public static bool CardSelectListUIBase_IsAddableByBaseCardId_Patch(ref int cardId, out int addCardId, ref bool __result)
        {
            if (Plugin.Instance.CustomDeckSave)
            {
                addCardId = cardId;
                __result = true;
                return false;
            }
            addCardId = 0;
            return true;
        }

        [HarmonyPatch(typeof(CardSelectListUIBase), "IsMaxCardNumInSelectionArea")]
        [HarmonyPrefix]
        public static bool CardSelectListUIBase_IsMaxCardNumInSelectionArea_Patch(ref bool __result)
        {
            if (Plugin.Instance.CustomDeckSave) { 
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CardBundleControllerBase), MethodType.Constructor,
            [typeof(Transform), typeof(Transform), typeof(UITexture), typeof(GameObject), typeof(IFormatBehavior), typeof(bool),
        typeof(bool), typeof(bool), typeof(bool)])]
        [HarmonyPrefix]
        public static bool CardBundleControllerBase_Constructor_Patch(ref bool canUseNonPossessionCard)
        {
            if (Plugin.Instance.CustomDeckSave) canUseNonPossessionCard = true;
            return true;
        }

        [HarmonyPatch(typeof(CardBundleController), nameof(CardBundleController.SaveDeck))]
        [HarmonyPrefix]
        public static bool CardBundleController_SaveDeck_Patch(CardBundleController __instance)
        {
            if (Plugin.Instance.CustomDeckSave)
            {
                var cards = __instance.SelectionAreaList.IdList.ToArray();
                var path = Path.Combine("Mods", "Decks", Plugin.Instance.CustomDeckName);
                if (path.EndsWith(".svd") == false)
                {
                    path += ".svd";
                }
                using (StreamWriter writer = new StreamWriter(path, false))
                {
                    foreach (int cardId in cards)
                    {
                        writer.WriteLine($"{cardId} #{CardMaster.GetInstanceForBattle().GetCardParameterFromId(cardId)?.CardName}");
                    }
                }
                Plugin.Logger.LogInfo($"Custom deck saved to {path}");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CardBundleController), "GetAutoCreateDeckCards")]
        [HarmonyPrefix]
        public static bool CardBundleController_GetAutoCreateDeckCards_Patch(Action<List<int>> onFinish)
        {
            if (Plugin.Instance.CustomDeckSave)
            {
                var path = Path.Combine("Mods", "Decks", Plugin.Instance.CustomDeckName);
                if (File.Exists(path))
                {
                    List<int> deck = LoadDeck(path);
                    onFinish.Call(deck);
                    return false;
                }
            }
            return true;
        }

        public static List<int> LoadDeck(string path)
        {
            string[] lines = File.ReadAllLines(path);
            List<int> deck = [];
            foreach (string line in lines)
            {
                string[] splitted = line.Split(['#'], StringSplitOptions.RemoveEmptyEntries);
                if (splitted.Length < 1) continue;
                string ids = splitted[0];
                if (int.TryParse(ids, out int card_id))
                {
                    deck.Add(card_id);
                }
                else
                {
                    Plugin.Logger.LogWarning($"card ${ids} in deck ${path} cannot be parsed and is skipped.");
                }
            }
            return deck;
        }

        public static IEnumerable<string> GetDeckNames()
        {
            var mods_folder = Directory.CreateDirectory("Mods");
            var card_master_folder = mods_folder.CreateSubdirectory("Decks");
            var patches = card_master_folder.GetFiles("*.svd");
            return patches.Select(f => f.Name);
        }
    }
}
