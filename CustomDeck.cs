using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        //[HarmonyPatch(typeof(SBattleLoad), "LoadComplete")]
        //[HarmonyReversePatch]
        //public static IEnumerator SBattleLoad_LoadComplete(object instance)
        //{
        //    throw new NotImplementedException("SBattleLoad_LoadComplete");
        //}

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
                    Plugin.Logger.LogWarning($"card ${ids} in deck cannot be parsed and is skipped.");
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
