namespace Shadowbus
{
    internal sealed class P2PRoomRoundState
    {
        internal bool HostReady { get; private set; }
        internal bool GuestReady { get; private set; }
        internal bool ReadySent { get; private set; }

        internal bool MarkReady(bool isHost)
        {
            if (ReadySent)
            {
                return false;
            }

            if (isHost)
            {
                HostReady = true;
            }
            else
            {
                GuestReady = true;
            }

            if (!HostReady || !GuestReady)
            {
                return false;
            }

            ReadySent = true;
            HostReady = false;
            GuestReady = false;
            return true;
        }

        internal void CancelReady(bool isHost)
        {
            if (isHost)
            {
                HostReady = false;
            }
            else
            {
                GuestReady = false;
            }
        }

        internal void Reenter(bool isHost)
        {
            ReadySent = false;
            CancelReady(isHost);
        }

        internal void Reset()
        {
            HostReady = false;
            GuestReady = false;
            ReadySent = false;
        }
    }
}
