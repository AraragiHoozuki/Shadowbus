namespace Shadowbus
{
    internal enum P2PDisconnectAction
    {
        None,
        BattleResult,
        RoomRelease,
        ForceRoomExit
    }

    internal static class P2PDisconnectPolicy
    {
        internal static P2PDisconnectAction Evaluate(
            bool peerDisconnected,
            bool finishResultSent,
            bool roomExitHandled,
            bool hasBattleManager,
            bool isBattleScene,
            bool hasInitializedRoom,
            bool roomReadyComplete,
            bool canInjectRoomRelease)
        {
            if (!peerDisconnected)
            {
                return P2PDisconnectAction.None;
            }
            if (hasBattleManager)
            {
                return finishResultSent
                    ? P2PDisconnectAction.None
                    : P2PDisconnectAction.BattleResult;
            }
            if (roomExitHandled || isBattleScene || !hasInitializedRoom)
            {
                return P2PDisconnectAction.None;
            }
            if (roomReadyComplete)
            {
                return P2PDisconnectAction.ForceRoomExit;
            }
            return canInjectRoomRelease
                ? P2PDisconnectAction.RoomRelease
                : P2PDisconnectAction.None;
        }
    }
}
