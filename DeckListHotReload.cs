using HarmonyLib;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Wizard;

namespace Shadowbus
{
    public static class DeckListHotReload
    {
        [HarmonyPatch(typeof(DeckListUI), "onOpen")]
        [HarmonyPrefix]
        public static void DeckListUI_onOpen_Prefix()
        {
            var stopwatch = Stopwatch.StartNew();
            Plugin.Logger.LogInfo("[DeckListHotReload] Deck list opened; refreshing CardMaster and deck data.");

            RefreshCardMaster();
            RefreshDeckListData();

            stopwatch.Stop();
            Plugin.Logger.LogInfo($"[DeckListHotReload] Refresh finished in {stopwatch.ElapsedMilliseconds} ms.");
        }

        private static void RefreshCardMaster()
        {
            try
            {
                CardMaster master = CardMaster.GetInstanceForBattle();
                if (master == null)
                {
                    Plugin.Logger.LogWarning("[DeckListHotReload] CardMaster is not available; skipped CardMaster reload.");
                    return;
                }

                int patchFileCount = Directory.Exists(Plugin.CardMasterPath)
                    ? Directory.GetFiles(Plugin.CardMasterPath, "*.json", SearchOption.TopDirectoryOnly).Length
                    : 0;
                int cardCountBefore = master.GetAllCardIds().Count;

                CardMasterPatcher.ApplyCardMasterPatches(master);

                int cardCountAfter = master.GetAllCardIds().Count;
                Plugin.Logger.LogInfo(
                    $"[DeckListHotReload] CardMaster reloaded: files={patchFileCount}, " +
                    $"cards={cardCountBefore}->{cardCountAfter}.");
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError($"[DeckListHotReload] CardMaster reload failed; opening deck list with current data.\n{exception}");
            }
        }

        private static void RefreshDeckListData()
        {
            try
            {
                if (Data.Load == null || Data.Load.data == null)
                {
                    Plugin.Logger.LogWarning("[DeckListHotReload] LoadDetail is not available; skipped deck list refresh.");
                    return;
                }

                Directory.CreateDirectory(Plugin.UnlimitedDeckPath);
                int fileCountBefore = Directory.GetFiles(
                    Plugin.UnlimitedDeckPath,
                    "*.json",
                    SearchOption.TopDirectoryOnly).Length;

                Offlinizer.LoadLocalUnlimitedDecks(Data.Load.data);

                var deckGroups = DeckListUtility.DeckGroupDataBaseClone();
                var unlimitedGroup = deckGroups.FirstOrDefault(group =>
                    group.DeckFormat == Format.Unlimited &&
                    group.AttributeType == DeckAttributeType.CustomDeck);
                int deckCount = unlimitedGroup?.DeckDataList.Count ?? 0;
                int emptyDeckCount = unlimitedGroup?.DeckDataList.Count(deck => deck.IsNoCard()) ?? 0;

                GameMgr gameMgr = GameMgr.GetIns();
                if (gameMgr != null && gameMgr.GetDataMgr() != null)
                {
                    gameMgr.GetDataMgr().CurrentDeckListParamData = new DeckGroupListData(deckGroups);
                }

                int fileCountAfter = Directory.GetFiles(
                    Plugin.UnlimitedDeckPath,
                    "*.json",
                    SearchOption.TopDirectoryOnly).Length;
                Plugin.Logger.LogInfo(
                    $"[DeckListHotReload] Deck data refreshed: files={fileCountBefore}->{fileCountAfter}, " +
                    $"unlimitedDecks={deckCount}, emptyDecks={emptyDeckCount}.");
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogError($"[DeckListHotReload] Deck data refresh failed; opening deck list with current data.\n{exception}");
            }
        }
    }
}
