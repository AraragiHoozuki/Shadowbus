using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shadowbus
{
    internal sealed class P2PTransport : IDisposable
    {
        // The initial trusted private-state exchange contains the complete hand,
        // deck, and mutable card metadata. Allow a larger single frame for that
        // one-time snapshot; ordinary action frames remain small.
        private const int MaxFrameLength = 8 * 1024 * 1024;

        private readonly object sendLock = new object();
        private readonly object stateLock = new object();
        private readonly AutoResetEvent sendSignal = new AutoResetEvent(false);
        private readonly Queue<byte[]> pendingSends = new Queue<byte[]>();
        private TcpListener listener;
        private TcpClient client;
        private NetworkStream stream;
        private byte[] expectedToken;
        private bool isHost;
        private bool handshakeComplete;
        private volatile bool stopped = true;
        private int connectionGeneration;

        internal event Action Connected;
        internal event Action<P2PWireMessage> MessageReceived;
        internal event Action<string> Disconnected;

        internal int BoundPort { get; private set; }

        internal void StartHost(IPAddress bindAddress, int port, byte[] roomToken)
        {
            Stop(false);
            if (bindAddress == null)
            {
                throw new ArgumentNullException(nameof(bindAddress));
            }
            if (roomToken == null || roomToken.Length != 16)
            {
                throw new ArgumentException("The room token must contain 16 bytes.", nameof(roomToken));
            }

            isHost = true;
            handshakeComplete = false;
            expectedToken = (byte[])roomToken.Clone();
            listener = new TcpListener(bindAddress, port);
            listener.Start(1);
            BoundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            stopped = false;
            Task.Run((Action)AcceptLoop);
        }

        internal void Connect(IPAddress address, int port, byte[] roomToken)
        {
            Stop(false);
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }
            if (port < 1 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }
            if (roomToken == null || roomToken.Length != 16)
            {
                throw new ArgumentException("The room token must contain 16 bytes.", nameof(roomToken));
            }
            isHost = false;
            handshakeComplete = false;
            expectedToken = (byte[])roomToken.Clone();
            stopped = false;
            Task.Run(async () =>
            {
                try
                {
                    TcpClient newClient = new TcpClient(address.AddressFamily);
                    await newClient.ConnectAsync(address, port).ConfigureAwait(false);
                    AttachClient(newClient);
                    Send(new P2PWireMessage
                    {
                        Type = "hello",
                        Data = new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["token"] = Convert.ToBase64String(expectedToken),
                            ["protocol"] = 1
                        }
                    });
                }
                catch (Exception ex)
                {
                    Fail("Unable to connect to the room host: " + ex.Message);
                }
            });
        }

        internal bool Send(P2PWireMessage message)
        {
            if (message == null)
            {
                return false;
            }
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(message, P2PJson.Settings));
                if (payload.Length > MaxFrameLength)
                {
                    throw new InvalidDataException("P2P frame is too large.");
                }

                lock (sendLock)
                {
                    if (stream == null || stopped)
                    {
                        return false;
                    }
                    pendingSends.Enqueue(payload);
                }
                sendSignal.Set();
                return true;
            }
            catch (Exception ex)
            {
                Fail("P2P send failed: " + ex.Message);
                return false;
            }
        }

        internal void Stop(bool notify = false)
        {
            bool shouldNotify;
            lock (stateLock)
            {
                shouldNotify = notify && !stopped;
                stopped = true;
                connectionGeneration++;
                try { stream?.Close(); } catch { }
                try { client?.Close(); } catch { }
                try { listener?.Stop(); } catch { }
                stream = null;
                client = null;
                listener = null;
                BoundPort = 0;
            }
            lock (sendLock)
            {
                pendingSends.Clear();
            }
            sendSignal.Set();
            if (shouldNotify)
            {
                Disconnected?.Invoke("P2P session closed.");
            }
        }

        public void Dispose()
        {
            Stop(false);
        }

        private void AcceptLoop()
        {
            try
            {
                TcpClient accepted = listener.AcceptTcpClient();
                if (stopped)
                {
                    accepted.Close();
                    return;
                }
                AttachClient(accepted);
            }
            catch (Exception ex)
            {
                if (!stopped)
                {
                    Fail("P2P host listener failed: " + ex.Message);
                }
            }
        }

        private void AttachClient(TcpClient newClient)
        {
            int generation;
            lock (stateLock)
            {
                if (stopped)
                {
                    newClient.Close();
                    return;
                }
                client = newClient;
                client.NoDelay = true;
                client.ReceiveTimeout = 10000;
                stream = client.GetStream();
                generation = ++connectionGeneration;
            }
            Task.Run(() => SendLoop(generation));
            Task.Run((Action)ReadLoop);
        }

        private void SendLoop(int generation)
        {
            try
            {
                while (!stopped && generation == connectionGeneration)
                {
                    byte[] payload = null;
                    lock (sendLock)
                    {
                        if (pendingSends.Count > 0)
                        {
                            payload = pendingSends.Dequeue();
                        }
                    }

                    if (payload == null)
                    {
                        sendSignal.WaitOne(1000);
                        continue;
                    }

                    NetworkStream currentStream;
                    lock (stateLock)
                    {
                        if (stopped || generation != connectionGeneration ||
                            stream == null)
                        {
                            continue;
                        }
                        currentStream = stream;
                    }

                    byte[] length =
                    {
                        (byte)(payload.Length >> 24),
                        (byte)(payload.Length >> 16),
                        (byte)(payload.Length >> 8),
                        (byte)payload.Length
                    };
                    currentStream.Write(length, 0, length.Length);
                    currentStream.Write(payload, 0, payload.Length);
                }
            }
            catch (Exception ex)
            {
                if (!stopped && generation == connectionGeneration)
                {
                    Fail("P2P send failed: " + ex.Message);
                }
            }
        }

        private void ReadLoop()
        {
            try
            {
                while (!stopped)
                {
                    byte[] lengthBytes = ReadExact(4);
                    int length = (lengthBytes[0] << 24) | (lengthBytes[1] << 16) |
                        (lengthBytes[2] << 8) | lengthBytes[3];
                    if (length < 1 || length > MaxFrameLength)
                    {
                        throw new InvalidDataException("Invalid P2P frame length.");
                    }
                    string json = Encoding.UTF8.GetString(ReadExact(length));
                    P2PWireMessage message = P2PJson.DeserializeMessage(json);
                    if (HandleHandshake(message))
                    {
                        continue;
                    }
                    MessageReceived?.Invoke(message);
                }
            }
            catch (HandshakeRejectedException ex)
            {
                if (isHost && !stopped)
                {
                    Plugin.Logger.LogWarning("[P2P] Rejected connection attempt: " + ex.Message);
                    ResetRejectedHostClient();
                    if (!stopped)
                    {
                        Task.Run((Action)AcceptLoop);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!stopped)
                {
                    if (isHost && !handshakeComplete)
                    {
                        Plugin.Logger.LogWarning(
                            "[P2P] Ignored incomplete connection attempt: " + ex.Message);
                        ResetRejectedHostClient();
                        if (!stopped)
                        {
                            Task.Run((Action)AcceptLoop);
                        }
                    }
                    else
                    {
                        Fail("P2P connection closed: " + ex.Message);
                    }
                }
            }
        }

        private bool HandleHandshake(P2PWireMessage message)
        {
            if (message == null)
            {
                throw new InvalidDataException("Invalid P2P message.");
            }
            if (handshakeComplete)
            {
                return false;
            }
            if (isHost)
            {
                if (message.Type != "hello")
                {
                    RejectHandshake("The client did not start with a P2P handshake.");
                }
                string tokenText = message.Data != null && message.Data.TryGetValue("token", out object value)
                    ? value?.ToString() : null;
                byte[] actual;
                try
                {
                    actual = string.IsNullOrEmpty(tokenText)
                        ? Array.Empty<byte>() : Convert.FromBase64String(tokenText);
                }
                catch (FormatException)
                {
                    actual = Array.Empty<byte>();
                }
                int protocol = 0;
                if (message.Data != null && message.Data.TryGetValue("protocol", out object protocolValue))
                {
                    int.TryParse(protocolValue?.ToString(), out protocol);
                }
                if (protocol != 1)
                {
                    RejectHandshake("Unsupported P2P protocol version.");
                }
                if (!TokenEquals(expectedToken, actual))
                {
                    RejectHandshake("Invalid room password.");
                }
                handshakeComplete = true;
                client.ReceiveTimeout = 0;
                try { listener?.Stop(); } catch { }
                listener = null;
                Send(new P2PWireMessage { Type = "hello_ok" });
                Connected?.Invoke();
                return true;
            }
            if (message.Type == "hello_ok")
            {
                handshakeComplete = true;
                client.ReceiveTimeout = 0;
                Connected?.Invoke();
                return true;
            }
            if (message.Type == "hello_reject")
            {
                throw new UnauthorizedAccessException(message.Error ?? "Room password was rejected.");
            }
            throw new InvalidDataException("The host did not complete the P2P handshake.");
        }

        private void RejectHandshake(string error)
        {
            Send(new P2PWireMessage { Type = "hello_reject", Error = error });
            throw new HandshakeRejectedException(error);
        }

        private void ResetRejectedHostClient()
        {
            lock (stateLock)
            {
                connectionGeneration++;
                try { stream?.Close(); } catch { }
                try { client?.Close(); } catch { }
                stream = null;
                client = null;
                handshakeComplete = false;
            }
            lock (sendLock)
            {
                pendingSends.Clear();
            }
            sendSignal.Set();
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException();
                }
                offset += read;
            }
            return buffer;
        }

        private void Fail(string error)
        {
            bool notify;
            lock (stateLock)
            {
                notify = !stopped;
                stopped = true;
                connectionGeneration++;
                try { stream?.Close(); } catch { }
                try { client?.Close(); } catch { }
                try { listener?.Stop(); } catch { }
                stream = null;
                client = null;
                listener = null;
                BoundPort = 0;
            }
            lock (sendLock)
            {
                pendingSends.Clear();
            }
            sendSignal.Set();
            if (notify)
            {
                Disconnected?.Invoke(error);
            }
        }

        private static bool TokenEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }
            return difference == 0;
        }

        private sealed class HandshakeRejectedException : Exception
        {
            internal HandshakeRejectedException(string message) : base(message)
            {
            }
        }
    }
}
