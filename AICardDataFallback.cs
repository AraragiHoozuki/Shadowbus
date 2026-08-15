using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Wizard;

namespace Shadowbus
{
    /// <summary>
    /// Gives every card AI data, even the ones the original AI files never described.
    ///
    /// AIVirtualCard.AIData comes from AIParamQuery.SearchAICardData, which returns null for
    /// any card that is missing from the deck CSV, ai_ally_common and ai_common. Most of the
    /// evaluation code null checks it, but EvaluatePlayValue does not:
    ///
    ///     return (card.AIData.PlayBonusExpr.EvalArg(...) + ...) * card.GetPlayBonusRate(...);
    ///
    /// So an undescribed card does not merely evaluate as worthless, it throws. That happens
    /// inside CalcMostValuableHandPtn on a worker thread, where the game's own empty catch
    /// block hides it, and the AI turn then hangs. See AITurnGuard for the other half.
    ///
    /// A second group needs the same treatment. About a quarter of the entries in
    /// ai_ally_common exist but describe nothing: all three expressions are empty strings and
    /// every tag column is blank padding, so TagList ends up empty and the expressions parse
    /// to null. Those cards evaluate to exactly 0, and a pattern worth 0 never beats playing
    /// nothing, so the AI holds them for the whole game. Tokens and the newest card sets are
    /// mostly in this group.
    ///
    /// The fallback keeps BattleBonus and Priority neutral, which is what the null checks
    /// already produced, and gives PlayBonus a small value so the AI considers the card worth
    /// playing at all. A follower still gets most of its value from its stats through
    /// EvaluateValueOnField; a spell's whole value is EvaluatePlayValue, so without this it
    /// scores zero and is never played.
    /// </summary>
    public static class AICardDataFallback
    {
        // Matches what the null checks used to yield for these two expressions.
        private const string NeutralExpression = "0";

        // Tags that actually reach EvaluatePlayValue. Everything else only drives the
        // simulation of the virtual field and contributes nothing to a card's score.
        // The summon tags are here because the value of what they put on the field is added
        // separately, through num8 in EnemyAI_Play.EstimateMaxPlayPtnWithToken. Only PlayToken
        // and PlayReanimate feed PlayTagCollection._summonTokenList; PlayTokenDraw goes to
        // _tokenDrawList and is worth nothing, so it gets priced like a draw instead.
        private static readonly HashSet<AIPlayTagType> ScoringTagTypes = new HashSet<AIPlayTagType>
        {
            AIPlayTagType.PlayBonus,
            AIPlayTagType.PlayBonusRate,
            AIPlayTagType.PlayBonusInSimulation,
            AIPlayTagType.FanfareBonus,
            AIPlayTagType.FanfareBonusInSimulation,
            AIPlayTagType.PlayToken,
            AIPlayTagType.FanfareToken,
            AIPlayTagType.PlayReanimate,
            AIPlayTagType.FanfareReanimate
        };

        private static float _playBonusMin;
        private static float _playBonusMax;
        private static bool _priceUnpricedCards;
        private static bool _respectPlayLimitLocks;
        private static int _lowLifeThreshold;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<int, AICardData> Cache = new Dictionary<int, AICardData>();
        private static readonly HashSet<int> ReportedCardIds = new HashSet<int>();
        private static readonly List<string> PendingReports = new List<string>();

        internal static void Configure(
            float playBonusMin,
            float playBonusMax,
            bool priceUnpricedCards,
            bool respectPlayLimitLocks,
            int lowLifeThreshold)
        {
            _playBonusMin = Math.Max(0f, Math.Min(playBonusMin, playBonusMax));
            _playBonusMax = Math.Max(0f, Math.Max(playBonusMin, playBonusMax));
            _priceUnpricedCards = priceUnpricedCards;
            _respectPlayLimitLocks = respectPlayLimitLocks;
            _lowLifeThreshold = Math.Max(0, lowLifeThreshold);

            Plugin.Logger.LogInfo(
                _playBonusMax > 0f
                    ? $"[AICardData] Cards with no AI data get a play bonus between " +
                      $"{_playBonusMin:0.##} and {_playBonusMax:0.##}."
                    : "[AICardData] Cards with no AI data get neutral data; the AI will not play them.");
            Plugin.Logger.LogInfo(
                _priceUnpricedCards
                    ? $"[AICardData] Spells and amulets whose tags carry no price are scored from " +
                      $"those tags; leader healing only counts at {_lowLifeThreshold} life or less."
                    : "[AICardData] Tag based scoring is off; spells and amulets with no price stay at 0.");
            if (_respectPlayLimitLocks)
            {
                Plugin.Logger.LogInfo(
                    "[AICardData] Cards the original data locked with a playLimit tag are left unpriced.");
            }
        }

        [HarmonyPatch(typeof(AIParamQuery), nameof(AIParamQuery.SearchAICardData))]
        [HarmonyPostfix]
        public static void AIParamQuery_SearchAICardData_Postfix(AIVirtualCard card, ref AICardData __result)
        {
            if (card == null)
            {
                return;
            }

            if (__result == null)
            {
                __result = GetFallback(card, "no entry");
                return;
            }

            // A quarter of the original entries exist but describe nothing: the three
            // expressions are empty strings and every tag column is blank padding. Such a card
            // evaluates to exactly 0, and 0 never beats playing nothing, so the AI holds it
            // forever. Treat it the same as a missing entry.
            if (IsBlank(__result))
            {
                __result = GetFallback(card, "blank entry");
                return;
            }

            // The remaining group has tags but no price. Its tags only tell the simulator what
            // happens to the virtual field; none of them reach EvaluatePlayValue. A follower
            // still earns its stats through EvaluateValueOnField, so only spells and amulets
            // are stranded at zero.
            if (_priceUnpricedCards && !IsFollowerId(card.BaseId) && !HasScoringPath(__result))
            {
                // A playLimit tag is the original authors telling the AI what a card has to be
                // worth before it may be played, and a value like 99 with no condition means
                // never. Dimension Shift is locked that way because it grants an extra turn,
                // which the simulator cannot model at all. Off by default: today every locked
                // card without a price sits far below its threshold, so pricing it changes
                // nothing, and leaving the behaviour as it was keeps this opt-in.
                if (_respectPlayLimitLocks && HasTag(__result, AIPlayTagType.PlayLimit))
                {
                    ReportOnce(card.BaseId, "playLimit lock, left alone");
                    return;
                }

                __result = GetPricedCopy(card, __result);
            }
        }

        private static bool HasTag(AICardData data, AIPlayTagType type)
        {
            if (data.TagList == null)
            {
                return false;
            }

            foreach (AIPlayTag tag in data.TagList)
            {
                if (tag != null && tag.Type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReportOnce(int cardId, string note)
        {
            lock (CacheLock)
            {
                if (ReportedCardIds.Add(cardId))
                {
                    PendingReports.Add($"{cardId} [{note}]");
                }
            }
        }

        /// <summary>
        /// Reads the card type out of the sixth digit of the base ID: 1 follower, 2 amulet,
        /// 3 countdown amulet, 4 spell. Verified against the original AI data, where none of
        /// the 1001 cards with a 4 there carries a follower-only tag.
        /// </summary>
        private static bool IsFollowerId(int baseId)
        {
            string id = baseId.ToString(CultureInfo.InvariantCulture);

            // Anything with an unexpected shape is left alone.
            return id.Length != 9 || id[5] == '1';
        }

        private static bool HasScoringPath(AICardData data)
        {
            if (!IsEmpty(data.PlayBonusExpr))
            {
                return true;
            }

            if (data.TagList == null)
            {
                return false;
            }

            foreach (AIPlayTag tag in data.TagList)
            {
                if (tag != null && ScoringTagTypes.Contains(tag.Type))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBlank(AICardData data)
        {
            // AIPlayTag.InitFromTextAsset drops the blank padding tags, so a described card
            // always keeps at least one here.
            if (data.TagList != null && data.TagList.Count > 0)
            {
                return false;
            }

            return IsEmpty(data.PlayBonusExpr)
                && IsEmpty(data.BattleBonusExpr)
                && IsEmpty(data.PriorityExpr);
        }

        // AIPolishConvertedExpression.CreateExpression returns null for an empty string.
        private static bool IsEmpty(AIPolishConvertedExpression expression)
        {
            return expression == null
                || expression.TokenList == null
                || expression.TokenList.Count == 0;
        }

        internal static void Update()
        {
            string[] reports;
            lock (CacheLock)
            {
                if (PendingReports.Count == 0)
                {
                    return;
                }

                reports = PendingReports.ToArray();
                PendingReports.Clear();
            }

            Plugin.Logger.LogInfo(
                $"[AICardData] {reports.Length} card(s) had no usable AI data; using fallback data " +
                $"so the AI will consider playing them: {string.Join(", ", reports)}.");
        }

        private static AICardData GetFallback(AIVirtualCard card, string reason)
        {
            int cardId = card.BaseId;
            bool isLeader = card.IsLeader;

            // Reached from AIVirtualCard's constructor, which runs on the simulation worker
            // thread as well as the main thread.
            lock (CacheLock)
            {
                if (Cache.TryGetValue(cardId, out AICardData cached))
                {
                    return cached;
                }

                // A separate instance: the blank entry belongs to the shared master data and
                // must not be modified.
                AICardData data = Create(cardId, isLeader);
                Cache[cardId] = data;

                if (!isLeader && ReportedCardIds.Add(cardId))
                {
                    PendingReports.Add($"{cardId} [{reason}] -> playBonus {GetPlayBonusExpression(cardId)}");
                }

                return data;
            }
        }

        /// <summary>
        /// Returns a copy of the card's data with a synthesized PlayBonus derived from the tags
        /// it already carries, so the AI has some reason to play it. The original object belongs
        /// to the shared master data and is never modified; its tags are carried over so the
        /// simulation keeps behaving exactly as before.
        /// </summary>
        private static AICardData GetPricedCopy(AIVirtualCard card, AICardData original)
        {
            int cardId = card.BaseId;

            lock (CacheLock)
            {
                if (Cache.TryGetValue(cardId, out AICardData cached))
                {
                    return cached;
                }

                // Tags the table has no price for, or none at all beyond a cost such as
                // playDiscard, leave the card at zero. Fall back to the flat bonus so it is
                // still worth more than doing nothing.
                string playBonus = BuildPriceExpression(original);
                if (playBonus == NeutralExpression)
                {
                    playBonus = GetPlayBonusExpression(cardId);
                }

                var columns = new[]
                {
                    cardId.ToString(CultureInfo.InvariantCulture),
                    string.Empty,
                    string.Empty,
                    "0",
                    NeutralExpression,
                    playBonus,
                    NeutralExpression,
                    string.Empty
                };

                var priced = new AICardData(new AICardDataAsset(columns));
                priced.BattleBonusExpr = original.BattleBonusExpr;
                priced.PriorityExpr = original.PriorityExpr;
                priced.MergeTagFromAnotherData(original);

                Cache[cardId] = priced;
                if (ReportedCardIds.Add(cardId))
                {
                    PendingReports.Add($"{cardId} [unpriced] -> playBonus {playBonus}");
                }

                return priced;
            }
        }

        /// <summary>
        /// Prices a card by what its tags say it does. The numbers stay inside the band the
        /// original data uses for literal play bonuses, whose median is 0 and 90th percentile 2.
        /// </summary>
        private static string BuildPriceExpression(AICardData original)
        {
            float flat = 0f;
            string conditional = null;

            foreach (AIPlayTag tag in original.TagList ?? new List<AIPlayTag>())
            {
                if (tag == null)
                {
                    continue;
                }

                string[] args = SplitArg(tag.Arg);
                switch (tag.Type)
                {
                    case AIPlayTagType.PlayDamage:
                    case AIPlayTagType.FanfareDamage:
                        // Damage aimed at the leader is deliberately worth almost nothing.
                        // CalculatePlayOutDamageProspected already spots these cards when they
                        // add up to lethal, and pricing them properly would make the AI dump
                        // its burn on turn one and lose that finish.
                        flat += Targets(args, "CLASS") ? 0.2f : 1.2f;
                        break;

                    case AIPlayTagType.PlayHeal:
                    case AIPlayTagType.FanfareHeal:
                        if (Targets(args, "CLASS") && Targets(args, "ALLY") && _lowLifeThreshold > 0)
                        {
                            // Only worth something once the leader is actually hurt.
                            conditional =
                                $"1.5 * ( LIFE ( ALLY_CLASS ) <= {_lowLifeThreshold.ToString(CultureInfo.InvariantCulture)} )";
                        }
                        else
                        {
                            flat += 0.4f;
                        }
                        break;

                    case AIPlayTagType.PlayDestroy:
                    case AIPlayTagType.FanfareDestroy:
                    case AIPlayTagType.PlayBanish:
                    case AIPlayTagType.FanfareBanish:
                        flat += 1.5f;
                        break;

                    case AIPlayTagType.PlayBounce:
                    case AIPlayTagType.FanfareBounce:
                        flat += 1.0f;
                        break;

                    case AIPlayTagType.PlayDraw:
                    case AIPlayTagType.PlayTokenDraw:
                    case AIPlayTagType.FanfareTokenDraw:
                        flat += 0.8f;
                        break;

                    case AIPlayTagType.PlayBuff:
                    case AIPlayTagType.FanfareBuff:
                    case AIPlayTagType.PlayHandBuff:
                    case AIPlayTagType.FanfareHandBuff:
                        flat += 0.6f;
                        break;

                    case AIPlayTagType.LastwordToken:
                    case AIPlayTagType.LastwordDraw:
                    case AIPlayTagType.Break:
                        // Amulets that pay off when they leave the field.
                        flat += 0.8f;
                        break;

                    case AIPlayTagType.PlayDiscard:
                    case AIPlayTagType.FanfareDiscard:
                        // A cost, not a benefit.
                        break;

                    default:
                        // Something happens that the AI cannot price. Enough to beat doing
                        // nothing, not enough to outrank a card the original data describes.
                        flat += 0.3f;
                        break;
                }
            }

            flat = Math.Min(flat, 3f);
            string flatText = ((float)Math.Round(flat, 2)).ToString("0.##", CultureInfo.InvariantCulture);

            if (conditional == null)
            {
                return flat > 0f ? flatText : NeutralExpression;
            }

            // The expression parser splits on spaces, so every operator needs them.
            return flat > 0f ? flatText + " + " + conditional : conditional;
        }

        private static string[] SplitArg(string arg)
        {
            return string.IsNullOrEmpty(arg)
                ? new string[0]
                : arg.Split(new[] { ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool Targets(string[] args, string keyword)
        {
            foreach (string part in args)
            {
                if (string.Equals(part, keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static AICardData Create(int cardId, bool isLeader)
        {
            // A leader is never played from hand, so it keeps the neutral data it had before.
            string playBonus = isLeader ? NeutralExpression : GetPlayBonusExpression(cardId);

            var columns = new[]
            {
                cardId.ToString(CultureInfo.InvariantCulture),
                string.Empty,       // UseCommon
                string.Empty,       // CardName
                "0",                // CardNum
                NeutralExpression,  // BattleBonus
                playBonus,          // PlayBonus
                NeutralExpression,  // Priority
                string.Empty        // the trailing column the parser expects after the tags
            };

            return new AICardData(new AICardDataAsset(columns));
        }

        /// <summary>
        /// A small play bonus that varies between cards but never for the same card.
        /// AIVirtualCard.GetHash folds PlayBonusExpr.Hash into the key of the AI's result
        /// caches, so a value that changed between calls would let one hash stand for two
        /// different evaluations, which is exactly the inconsistency the AI reports as an
        /// error and which can make it re-decide the same move forever.
        /// </summary>
        private static string GetPlayBonusExpression(int cardId)
        {
            if (_playBonusMax <= 0f)
            {
                return NeutralExpression;
            }

            unchecked
            {
                uint hash = (uint)cardId * 2654435761u;
                float position = ((hash >> 8) & 0xFFFF) / 65535f;
                float playBonus = _playBonusMin + (_playBonusMax - _playBonusMin) * position;

                // The AI expression parser splits on spaces and expects an invariant number.
                return Math.Round(playBonus, 2).ToString("0.##", CultureInfo.InvariantCulture);
            }
        }
    }
}
