using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Jagti.Net
{
    /// <summary>One remote end of a NetPeer: the host keeps one per client, a client keeps one for the host.</summary>
    public class NetConnection
    {
        public int id;                  // player number the host gave out (host itself is 1)
        public string name = "";
        public EndPoint endPoint;
        public bool connected;
        public double lastHeard;
        public float rtt = 0.1f;        // round trip in seconds, smoothed

        // Reliable messages are numbered per connection and delivered in order.
        internal ushort nextSendSeq;
        internal ushort nextRecvSeq;
        internal readonly List<Pending> pending = new List<Pending>();
        internal readonly Dictionary<ushort, byte[]> early = new Dictionary<ushort, byte[]>();
        internal double lastPing;

        public int PendingReliable { get { return pending.Count; } }

        internal class Pending
        {
            public ushort seq;
            public byte[] packet;
            public double sentAt;
            public double firstSent;
        }
    }

    /// <summary>
    /// A small UDP transport with no packages: a host listens on a port, clients connect to it by
    /// address. Messages are either unreliable (newest wins, for positions) or reliable and ordered
    /// (resent until acknowledged, for hits, deaths and loot). Everything happens inside Update, which
    /// the owner calls once a frame, so no threads touch game state. Plain C#, so the same code is
    /// tested by tools/net/net_test.cs outside Unity.
    /// </summary>
    public class NetPeer : IDisposable
    {
        public const int DefaultPort = 7777;
        public const byte Protocol = 1;
        public const int MaxPacket = 1200;
        public const int MaxPlayers = 8;
        /// <summary>Seconds of silence before a link counts as lost (long, because loading the campus stalls a machine).</summary>
        public float timeout = 30f;

        // Packet types (byte 3 of every packet).
        const byte Connect = 1, Accept = 2, Reject = 3, Unreliable = 4, Reliable = 5, Ack = 6,
                   Ping = 7, Pong = 8, Disconnect = 9, Discover = 10, Here = 11;

        public bool IsHost { get; private set; }
        public bool IsRunning { get { return socket != null; } }
        public int LocalId { get; private set; }
        public int Port { get; private set; }

        /// <summary>Fraction of outgoing packets thrown away on purpose (testing only).</summary>
        public float dropRate;

        /// <summary>What a host says when a client looks for games on the home network.</summary>
        public string hostName = "Host";
        public Func<bool> acceptingPlayers = () => true;

        public event Action<NetConnection> Connected;
        public event Action<NetConnection, string> Disconnected;
        public event Action<NetConnection, NetReader> Message;
        public event Action<string, int, IPEndPoint> HostFound;   // name, players, address

        public readonly List<NetConnection> connections = new List<NetConnection>();

        Socket socket;
        readonly Dictionary<EndPoint, NetConnection> byEnd = new Dictionary<EndPoint, NetConnection>();
        readonly byte[] recv = new byte[2048];
        readonly NetWriter packet = new NetWriter(MaxPacket);
        readonly NetReader reader = new NetReader();
        readonly Random dropRng = new Random(1234);
        double now;

        // Client side
        NetConnection server;
        string joinName;
        double connectSentAt = -10, connectStarted;
        public string LastError { get; private set; }

        public NetConnection Server { get { return server; } }

        // ---------------------------------------------------------------- start and stop

        public bool StartHost(int port, string name)
        {
            if (!Open(port)) return false;
            IsHost = true;
            LocalId = 1;
            hostName = name;
            return true;
        }

        public bool StartClient(string address, int port, string name)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(address.Trim(), out ip))
            {
                try
                {
                    var found = Dns.GetHostAddresses(address.Trim());
                    ip = null;
                    foreach (var a in found)
                        if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                }
                catch (Exception) { ip = null; }
                if (ip == null) { LastError = "Can't find " + address; return false; }
            }
            if (!Open(0)) return false;
            IsHost = false;
            LocalId = 0;
            joinName = name;
            server = new NetConnection { id = 1, endPoint = new IPEndPoint(ip, port), lastHeard = now };
            byEnd[server.endPoint] = server;
            connectStarted = now;
            connectSentAt = -10;
            return true;
        }

        /// <summary>Opens a socket only for finding hosts on the home network (no connection).</summary>
        public bool StartSearch()
        {
            if (socket == null && !Open(0)) return false;
            return true;
        }

        public void Search(int port)
        {
            if (socket == null) return;
            Begin(Discover);
            try { socket.SendTo(packet.data, 0, packet.Length, SocketFlags.None, new IPEndPoint(IPAddress.Broadcast, port)); }
            catch (SocketException) { }
            // Also ask this machine itself, for a second copy of the game on the same computer.
            try { socket.SendTo(packet.data, 0, packet.Length, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, port)); }
            catch (SocketException) { }
        }

        bool Open(int port)
        {
            Stop();
            try
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Blocking = false;
                s.EnableBroadcast = true;
                // On Windows an unreachable peer makes the next receive throw; turn that off.
                try { s.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception) { }
                s.Bind(new IPEndPoint(IPAddress.Any, port));
                socket = s;
                Port = ((IPEndPoint)s.LocalEndPoint).Port;
                LastError = null;
                return true;
            }
            catch (SocketException e)
            {
                LastError = port != 0 && e.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? "Port " + port + " is already in use" : e.Message;
                return false;
            }
        }

        public void Stop()
        {
            if (socket == null) return;
            foreach (var c in connections) if (c.connected) SendControl(c, Disconnect);
            if (server != null && server.connected) SendControl(server, Disconnect);
            try { socket.Close(); } catch (Exception) { }
            socket = null;
            connections.Clear();
            byEnd.Clear();
            server = null;
            LocalId = 0;
        }

        public void Dispose() { Stop(); }

        // ---------------------------------------------------------------- sending

        /// <summary>Starts a message; write the body into the returned writer, then call Send*.</summary>
        public NetWriter BeginMessage()
        {
            msg.Clear();
            return msg;
        }
        readonly NetWriter msg = new NetWriter(MaxPacket);

        public void SendUnreliable(NetConnection c, NetWriter body)
        {
            if (c == null || !c.connected) return;
            Begin(Unreliable);
            packet.Bytes(body.data, 0, body.Length);
            Raw(c.endPoint);
        }

        public void SendReliable(NetConnection c, NetWriter body)
        {
            if (c == null || !c.connected) return;
            if (body.Length + 6 > MaxPacket) throw new ArgumentException("Message too long: " + body.Length);
            Begin(Reliable);
            packet.UShort(c.nextSendSeq);
            packet.Bytes(body.data, 0, body.Length);
            var p = new NetConnection.Pending { seq = c.nextSendSeq, packet = packet.ToArray(), sentAt = now, firstSent = now };
            c.pending.Add(p);
            c.nextSendSeq++;
            Raw(c.endPoint, p.packet, p.packet.Length);
        }

        /// <summary>Host: to every connected client (optionally all but one). Client: to the host.</summary>
        public void Broadcast(NetWriter body, bool reliable, NetConnection except = null)
        {
            if (!IsHost)
            {
                if (reliable) SendReliable(server, body); else SendUnreliable(server, body);
                return;
            }
            for (int i = 0; i < connections.Count; i++)
            {
                var c = connections[i];
                if (c == except || !c.connected) continue;
                if (reliable) SendReliable(c, body); else SendUnreliable(c, body);
            }
        }

        public void Kick(NetConnection c, string reason)
        {
            Begin(Reject);
            packet.String(reason);
            Raw(c.endPoint);
            Drop(c, reason);
        }

        void Begin(byte type)
        {
            packet.Clear();
            packet.Byte((byte)'J');
            packet.Byte((byte)'G');
            packet.Byte(Protocol);
            packet.Byte(type);
        }

        void SendControl(NetConnection c, byte type)
        {
            Begin(type);
            Raw(c.endPoint);
        }

        void Raw(EndPoint to) { Raw(to, packet.data, packet.Length); }

        void Raw(EndPoint to, byte[] data, int length)
        {
            if (socket == null) return;
            if (dropRate > 0f && dropRng.NextDouble() < dropRate) return;
            try { socket.SendTo(data, 0, length, SocketFlags.None, to); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        // ---------------------------------------------------------------- the frame

        /// <summary>Reads everything that has arrived, resends what wasn't acknowledged, keeps links alive.</summary>
        public void Update(double time)
        {
            now = time;
            if (socket == null) return;
            Receive();
            if (socket == null) return;

            if (!IsHost && server != null && !server.connected)
            {
                if (now - connectSentAt > 0.5)
                {
                    connectSentAt = now;
                    Begin(Connect);
                    packet.String(joinName);
                    Raw(server.endPoint);
                }
                if (now - connectStarted > 8)
                {
                    LastError = "No answer from the host";
                    var s = server;
                    Drop(s, LastError);
                }
                return;
            }

            if (server != null) Tend(server);
            // A lost link calls Disconnected, whose handler may stop the peer altogether.
            for (int i = connections.Count - 1; i >= 0 && socket != null; i--)
                if (i < connections.Count) Tend(connections[i]);
        }

        void Tend(NetConnection c)
        {
            if (!c.connected) return;
            if (now - c.lastHeard > timeout) { Drop(c, "Connection lost"); return; }
            double wait = Math.Max(0.08, Math.Min(1.0, c.rtt * 1.5 + 0.03));
            for (int i = 0; i < c.pending.Count; i++)
            {
                var p = c.pending[i];
                if (now - p.sentAt < wait) continue;
                p.sentAt = now;
                Raw(c.endPoint, p.packet, p.packet.Length);
            }
            if (now - c.lastPing > 1.0)
            {
                c.lastPing = now;
                Begin(Ping);
                packet.Float((float)now);
                Raw(c.endPoint);
            }
        }

        void Receive()
        {
            while (socket != null)
            {
                int n;
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    if (socket.Available <= 0) return;
                    n = socket.ReceiveFrom(recv, 0, recv.Length, SocketFlags.None, ref from);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.WouldBlock) return;
                    continue;   // e.g. connection reset from an unreachable peer
                }
                catch (ObjectDisposedException) { return; }
                if (n < 4 || recv[0] != 'J' || recv[1] != 'G') continue;
                Handle(recv[3], recv[2], from, n);
            }
        }

        void Handle(byte type, byte version, EndPoint from, int n)
        {
            NetConnection c;
            byEnd.TryGetValue(from, out c);

            if (type == Discover)
            {
                if (!IsHost) return;
                Begin(Here);
                packet.String(hostName);
                packet.Byte((byte)(1 + ConnectedCount));
                Raw(from);
                return;
            }
            if (type == Here)
            {
                reader.Reset(recv, 4, n - 4);
                string name = reader.String();
                int players = reader.Byte();
                if (HostFound != null) HostFound(name, players, (IPEndPoint)from);
                return;
            }

            if (version != Protocol)
            {
                if (IsHost && type == Connect)
                {
                    Begin(Reject);
                    packet.String("Different game version (yours " + version + ", host " + Protocol + ")");
                    Raw(from);
                }
                return;
            }

            if (IsHost && type == Connect)
            {
                if (c != null)
                {
                    if (c.connected) SendAccept(c);   // our accept was lost; say it again
                    return;
                }
                reader.Reset(recv, 4, n - 4);
                string name = Clean(reader.String());
                int id = FreeId();
                if (id < 0 || !acceptingPlayers())
                {
                    Begin(Reject);
                    packet.String(id < 0 ? "The game is full" : "The match has already started");
                    Raw(from);
                    return;
                }
                c = new NetConnection { id = id, name = name, endPoint = from, connected = true, lastHeard = now, lastPing = now };
                connections.Add(c);
                byEnd[from] = c;
                SendAccept(c);
                if (Connected != null) Connected(c);
                return;
            }

            if (c == null) return;
            c.lastHeard = now;

            switch (type)
            {
                case Accept:
                    if (IsHost || c.connected) return;
                    reader.Reset(recv, 4, n - 4);
                    LocalId = reader.Byte();
                    c.connected = true;
                    c.lastPing = now;
                    if (Connected != null) Connected(c);
                    break;
                case Reject:
                    if (IsHost) return;
                    reader.Reset(recv, 4, n - 4);
                    LastError = reader.String();
                    Drop(c, LastError);
                    break;
                case Unreliable:
                    if (!c.connected) return;
                    reader.Reset(recv, 4, n - 4);
                    if (Message != null) Message(c, reader);
                    break;
                case Reliable:
                    if (!c.connected || n < 6) return;
                    OnReliable(c, n);
                    break;
                case Ack:
                    {
                        reader.Reset(recv, 4, n - 4);
                        ushort seq = reader.UShort();
                        for (int i = 0; i < c.pending.Count; i++)
                        {
                            if (c.pending[i].seq != seq) continue;
                            if (c.pending[i].sentAt == c.pending[i].firstSent)
                                c.rtt = c.rtt * 0.85f + (float)(now - c.pending[i].firstSent) * 0.15f;
                            c.pending.RemoveAt(i);
                            break;
                        }
                    }
                    break;
                case Ping:
                    Begin(Pong);
                    packet.Bytes(recv, 4, Math.Min(4, n - 4));
                    Raw(from);
                    break;
                case Pong:
                    {
                        reader.Reset(recv, 4, n - 4);
                        double sent = reader.Float();
                        float r = (float)(now - sent);
                        if (r >= 0f && r < 5f) c.rtt = c.rtt * 0.8f + r * 0.2f;
                    }
                    break;
                case Disconnect:
                    Drop(c, IsHost ? "Left the game" : "The host closed the game");
                    break;
            }
        }

        void OnReliable(NetConnection c, int n)
        {
            ushort seq = (ushort)(recv[4] | (recv[5] << 8));
            Begin(Ack);
            packet.UShort(seq);
            Raw(c.endPoint);

            short ahead = unchecked((short)(seq - c.nextRecvSeq));
            if (ahead < 0) return;                       // already had it
            if (ahead > 0)
            {
                if (!c.early.ContainsKey(seq))
                {
                    var copy = new byte[n - 6];
                    Buffer.BlockCopy(recv, 6, copy, 0, n - 6);
                    c.early[seq] = copy;
                }
                return;
            }
            c.nextRecvSeq++;
            reader.Reset(recv, 6, n - 6);
            if (Message != null) Message(c, reader);
            byte[] next;
            while (c.connected && c.early.TryGetValue(c.nextRecvSeq, out next))
            {
                c.early.Remove(c.nextRecvSeq);
                c.nextRecvSeq++;
                var r = new NetReader(next, 0, next.Length);
                if (Message != null) Message(c, r);
            }
        }

        void SendAccept(NetConnection c)
        {
            Begin(Accept);
            packet.Byte((byte)c.id);
            Raw(c.endPoint);
        }

        void Drop(NetConnection c, string reason)
        {
            bool was = c.connected;
            c.connected = false;
            c.pending.Clear();
            byEnd.Remove(c.endPoint);
            connections.Remove(c);
            if (c == server) server = null;
            if (Disconnected != null && (was || !IsHost)) Disconnected(c, reason);
        }

        int FreeId()
        {
            for (int id = 2; id <= MaxPlayers; id++)
            {
                bool used = false;
                foreach (var c in connections) if (c.id == id) { used = true; break; }
                if (!used) return id;
            }
            return -1;
        }

        public int ConnectedCount
        {
            get
            {
                int k = 0;
                foreach (var c in connections) if (c.connected) k++;
                return k;
            }
        }

        public NetConnection Find(int id)
        {
            if (server != null && server.id == id) return server;
            foreach (var c in connections) if (c.id == id) return c;
            return null;
        }

        static string Clean(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) s = "Player";
            if (s.Length > 16) s = s.Substring(0, 16);
            return s;
        }
    }
}
