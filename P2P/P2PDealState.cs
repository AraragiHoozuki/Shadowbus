using System;

namespace Shadowbus
{
    internal sealed class P2PDealState
    {
        private int hostSeed = -1;
        private int guestSeed = -1;
        private bool hostClaimed;
        private bool guestClaimed;

        internal void Initialize(int hostIdxChangeSeed, int guestIdxChangeSeed)
        {
            if (hostIdxChangeSeed < 0 || guestIdxChangeSeed < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hostIdxChangeSeed),
                    "Index-change seeds must be non-negative.");
            }

            hostSeed = hostIdxChangeSeed;
            guestSeed = guestIdxChangeSeed;
            hostClaimed = false;
            guestClaimed = false;
        }

        internal bool TryClaim(
            bool forHost,
            out int idxChangeSeed,
            out int opponentIdxChangeSeed)
        {
            idxChangeSeed = forHost ? hostSeed : guestSeed;
            opponentIdxChangeSeed = forHost ? guestSeed : hostSeed;
            if (idxChangeSeed < 0 || opponentIdxChangeSeed < 0)
            {
                return false;
            }

            if (forHost)
            {
                if (hostClaimed)
                {
                    return false;
                }
                hostClaimed = true;
            }
            else
            {
                if (guestClaimed)
                {
                    return false;
                }
                guestClaimed = true;
            }

            return true;
        }

        internal void Reset()
        {
            hostSeed = -1;
            guestSeed = -1;
            hostClaimed = false;
            guestClaimed = false;
        }
    }
}
