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
            // JudgeResult carries the opponent's result to each client.
            return new P2PBattleResultPair(Invert(hostLocalResult), hostLocalResult);
        }

        internal static int Invert(int result)
        {
            if (result >= 101 && result <= 108)
            {
                return (result & 1) == 1 ? result + 1 : result - 1;
            }
            if (result >= 201 && result <= 208)
            {
                return (result & 1) == 1 ? result + 1 : result - 1;
            }
            return result;
        }
    }
}
