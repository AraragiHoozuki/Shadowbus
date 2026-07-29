namespace Shadowbus
{
    internal sealed class P2PDeliverySequence
    {
        private int sequence;

        internal bool IsOpen { get; private set; }

        internal bool Open()
        {
            if (IsOpen)
            {
                return false;
            }

            sequence = 0;
            IsOpen = true;
            return true;
        }

        internal void Reset()
        {
            sequence = 0;
            IsOpen = false;
        }

        internal bool TryNext(out int value)
        {
            if (!IsOpen)
            {
                value = 0;
                return false;
            }

            value = ++sequence;
            return true;
        }
    }
}
