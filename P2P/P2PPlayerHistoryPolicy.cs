using System;
using System.Collections.Generic;

namespace Shadowbus
{
    internal static class P2PPlayerHistoryPolicy
    {
        // Native PlayActions orders own card movement between these zones. Copying
        // the same lists again as player history can leave one card object in two
        // zones, or apply only half of a zone snapshot while card references are
        // still being resolved. Hidden-card snapshots synchronize the identity and
        // mutable state of cards in these zones without taking over their topology.
        internal static IReadOnlyList<string> NativeCardZoneListNames { get; } =
            Array.AsReadOnly(new[]
            {
                "HandCardList",
                "DeckCardList",
                "ClassAndInPlayCardList",
                "CemeteryList",
                "BanishList",
                "FusionIngredientList",
                "NecromanceZoneList",
                "ReservedCardList"
            });

        internal static IReadOnlyList<string> SynchronizedListNames { get; } =
            Array.AsReadOnly(new[]
            {
                "BattleStartDeckCardList",
                "DeckSkillCardList",
                "TurnFusionCards",
                "DiscardedCardList",
                "FusionIngredientAndDiscardedCardList",
                "UniteList",
                "GetOnList",
                "BlackHole",
                "ChoiceBraveCardList",
                "TurnPlayCardCountInfo",
                "TurnFusionCountInfo",
                "TurnEvolveCardCountInfo",
                "TurnPlayCards",
                "TurnDrawCards",
                "TurnDrawTokenCardsWithId",
                "GameDrawCards",
                "GameDrawTokenCards",
                "GameAddUpdateDeckCards",
                "GameSummonCards",
                "GameSummonMomentTribe",
                "GamePlayMomentTribe",
                "GamePlayMomentSpellChargeCards",
                "GameUpdateDeckMomentTribe",
                "GamePlayCards",
                "GameTurnPlayCards",
                "GameEnhancePlayCards",
                "GameCrystallizedPlayCards",
                "GameLeftCards",
                "GameTurnLeftCards",
                "GameReturnedCards",
                "GameSuperSkyboundArtCards",
                "GameInplayMetamorphoseCards",
                "TurnDestroyCards",
                "TurnWhenHealingCount",
                "GameBurialRiteCards",
                "TurnBurialRiteCards",
                "BurialRiteOrDiscardCardHandIndexList",
                "GameReanimatedCards",
                "AddToDeckCardList",
                "TurnStartLifeList",
                "GameSkillReturnCardCountList",
                "GameSkillDiscardCountList",
                "GameSkillBuffCountList",
                "GameSkillMetamorphoseCountList",
                "GameQuickAttackCards"
            });

        internal static bool ShouldAttachPreActionHistory(string uri, int turn)
        {
            return !string.Equals(uri, "TurnStart", StringComparison.Ordinal) ||
                turn > 1;
        }
    }
}
