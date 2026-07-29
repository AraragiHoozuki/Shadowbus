namespace Shadowbus
{
    internal readonly struct P2PBattleResultPair
    {
        internal P2PBattleResultPair(int host, int guest)
        {
            Host = host;
            Guest = guest;
        }

        internal int Host { get; }
        internal int Guest { get; }
    }

    internal static class P2PBattleResult
    {
        internal static P2PBattleResultPair FromHostLocalResult(int hostLocalResult)
        {
            return FromLocalResult(true, hostLocalResult);
        }

        internal static P2PBattleResultPair FromLocalResult(
            bool localIsHost,
            int localResult)
        {
            int opponentResult = Invert(localResult);
            return localIsHost
                ? new P2PBattleResultPair(localResult, opponentResult)
                : new P2PBattleResultPair(opponentResult, localResult);
        }

        internal static bool IsPairedResult(int result)
        {
            return (result >= 101 && result <= 108) ||
                (result >= 201 && result <= 208);
        }

        internal static int ResolveLocalResultAfterDisconnect(
            bool localRetired,
            int currentLocalResult)
        {
            if (localRetired)
            {
                return 106;
            }
            return IsPairedResult(currentLocalResult)
                ? currentLocalResult
                : 201;
        }

        internal static int Invert(int result)
        {
            if (IsPairedResult(result))
            {
                return (result & 1) == 1 ? result + 1 : result - 1;
            }
            return result;
        }
    }
}
