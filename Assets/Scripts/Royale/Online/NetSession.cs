using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Jagti.Net;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Online play's connection and lobby. Lives across scene loads (one match ends, the scene reloads,
    /// the same people stay connected). The host listens on a UDP port; others join by address or
    /// find it on the home network. The host starts the match when everyone has loaded in, and sends
    /// the seed and the list of players; NetMatch then keeps the match in step.
    /// </summary>
    public class NetSession : MonoBehaviour
    {
        public static NetSession Instance { get; private set; }

        public enum Mode { Offline, Host, Client }

        public class Member
        {
            public int id;
            public string name;
            public bool ready;
        }

        public class FoundGame
        {
            public string name;
            public int players;
            public string address;
            public float seen;
        }

        public Mode mode;
        public readonly NetPeer peer = new NetPeer();
        public readonly List<Member> members = new List<Member>();
        public readonly List<FoundGame> found = new List<FoundGame>();
        public bool InMatch { get; private set; }
        public string Status { get; private set; }
        public float StatusTime { get; private set; }
        public BotDifficulty.Level hostDifficulty;
        /// <summary>The in-match half (made when a match starts; goes with the scene).</summary>
        public NetMatch match;

        readonly NetPeer finder = new NetPeer();
        float searchTimer;

        public static bool Online { get { return Instance != null && Instance.mode != Mode.Offline && Instance.peer.IsRunning; } }
        public static bool IsHost { get { return Online && Instance.mode == Mode.Host; } }
        public static bool IsClient { get { return Online && Instance.mode == Mode.Client; } }
        /// <summary>A client that has asked to join and not heard back yet.</summary>
        public bool Joining { get { return mode == Mode.Client && peer.IsRunning && peer.LocalId == 0; } }
        public int LocalId { get { return peer.LocalId; } }

        public static NetSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Net Session");
            DontDestroyOnLoad(go);
            return go.AddComponent<NetSession>();
        }

        void Awake()
        {
            Instance = this;
            // A host that stops when its window loses focus would stall everyone's match.
            Application.runInBackground = true;
            peer.Connected += OnConnected;
            peer.Disconnected += OnDisconnected;
            peer.Message += OnMessage;
            peer.acceptingPlayers = () => !InMatch;
            finder.HostFound += OnHostFound;
        }

        void OnDestroy()
        {
            peer.Stop();
            finder.Stop();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit()
        {
            peer.Stop();   // tells the others straight away rather than after a timeout
            finder.Stop();
        }

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            peer.Update(now);
            if (finder.IsRunning)
            {
                finder.Update(now);
                searchTimer -= Time.unscaledDeltaTime;
                if (searchTimer <= 0f) finder.Stop();
            }
            for (int i = found.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - found[i].seen > 8f) found.RemoveAt(i);
        }

        void Say(string text)
        {
            Status = text;
            StatusTime = Time.unscaledTime;
        }

        // ------------------------------------------------------------------ starting and leaving

        public bool Host(string name, int port)
        {
            finder.Stop();
            if (!peer.StartHost(port, name))
            {
                Say("Couldn't open the game: " + peer.LastError);
                mode = Mode.Offline;
                return false;
            }
            mode = Mode.Host;
            InMatch = false;
            members.Clear();
            members.Add(new Member { id = 1, name = Clean(name), ready = true });
            Say("Hosting on port " + peer.Port + ". Friends join with one of the addresses below.");
            return true;
        }

        public bool Join(string address, int port, string name)
        {
            finder.Stop();
            if (!peer.StartClient(address, port, name))
            {
                Say(peer.LastError);
                mode = Mode.Offline;
                return false;
            }
            mode = Mode.Client;
            InMatch = false;
            members.Clear();
            Say("Joining " + address + "...");
            return true;
        }

        /// <summary>Leaves the game (or closes it, for the host). reason is shown on the menu.</summary>
        public void Leave(string reason)
        {
            peer.Stop();
            mode = Mode.Offline;
            InMatch = false;
            members.Clear();
            match = null;
            if (reason != null) Say(reason);
        }

        public void Search(int port)
        {
            if (!finder.StartSearch()) { Say("Couldn't search: " + finder.LastError); return; }
            finder.Search(port);
            searchTimer = 3f;
            Say("Looking for games on this network...");
        }

        public bool Searching { get { return finder.IsRunning; } }

        void OnHostFound(string name, int players, IPEndPoint at)
        {
            string address = at.Address.ToString();
            foreach (var f in found)
                if (f.address == address) { f.name = name; f.players = players; f.seen = Time.unscaledTime; return; }
            found.Add(new FoundGame { name = name, players = players, address = address, seen = Time.unscaledTime });
        }

        /// <summary>This machine's addresses on its networks, for friends to type in.</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) list.Add(a.ToString());
            }
            catch (System.Exception) { }
            if (list.Count == 0) list.Add("127.0.0.1 (this computer only)");
            return list;
        }

        // ------------------------------------------------------------------ lobby

        void OnConnected(NetConnection c)
        {
            if (mode == Mode.Host)
            {
                members.Add(new Member { id = c.id, name = c.name, ready = false });
                Say(c.name + " joined.");
                SendLobby();
            }
            else
            {
                Say("Joined. Waiting for the host to start the match.");
                SendReady();
            }
        }

        void OnDisconnected(NetConnection c, string reason)
        {
            if (mode == Mode.Host)
            {
                members.RemoveAll(m => m.id == c.id);
                Say(c.name + " left (" + reason + ").");
                if (InMatch && match != null) match.OnLeft(c.id);
                SendLobby();
                return;
            }
            // The link to the host is gone: back to the front menu.
            bool wasInMatch = InMatch;
            Leave(reason);
            if (wasInMatch)
            {
                MatchDirector.SkipMenu = false;
                MatchDirector.Reload();
            }
        }

        /// <summary>Client: the scene is loaded and the menu is up.</summary>
        public void SendReady()
        {
            if (mode != Mode.Client || peer.LocalId == 0) return;
            var w = peer.BeginMessage();
            w.Byte(NetMsg.Ready);
            peer.SendReliable(peer.Server, w);
        }

        void SendLobby()
        {
            if (mode != Mode.Host) return;
            var w = peer.BeginMessage();
            w.Byte(NetMsg.LobbyList);
            w.Byte((byte)BotDifficulty.Current);
            w.Byte((byte)members.Count);
            foreach (var m in members)
            {
                w.Byte((byte)m.id);
                w.String(m.name);
                w.Bool(m.ready);
            }
            peer.Broadcast(w, true);
        }

        public bool AllReady
        {
            get
            {
                foreach (var m in members) if (!m.ready) return false;
                return true;
            }
        }

        /// <summary>Host: start the match for everyone in the lobby.</summary>
        public void StartMatch(MatchDirector director)
        {
            if (mode != Mode.Host || InMatch || director == null || director.Begun) return;
            var humans = new List<Entrant>();
            foreach (var m in members) humans.Add(new Entrant { id = m.id, name = m.name, human = true });
            int seed = Random.Range(1, int.MaxValue);
            InMatch = true;
            director.BeginHost(seed, humans);

            var w = peer.BeginMessage();
            w.Byte(NetMsg.MatchStart);
            w.Int(seed);
            w.Byte((byte)director.Difficulty);
            w.Byte((byte)director.roster.Count);
            foreach (var e in director.roster)
            {
                w.Byte((byte)e.id);
                w.String(e.name);
                w.Bool(e.human);
                w.Byte((byte)e.skin);
                w.Byte((byte)e.shirt);
                w.Byte((byte)e.pants);
                w.Byte((byte)e.canopy);
            }
            peer.Broadcast(w, true);
            BeginSync(director);
        }

        void BeginSync(MatchDirector director)
        {
            match = director.gameObject.AddComponent<NetMatch>();
            match.Init(this, director);
        }

        /// <summary>Host: everyone back to the lobby (the scene reloads on every machine).</summary>
        public void BackToLobby()
        {
            if (mode != Mode.Host) return;
            var w = peer.BeginMessage();
            w.Byte(NetMsg.BackToLobby);
            peer.Broadcast(w, true);
            ReturnToLobby();
            SendLobby();
        }

        void ReturnToLobby()
        {
            InMatch = false;
            match = null;
            foreach (var m in members) m.ready = m.id == LocalId || (mode == Mode.Host && m.id == 1);
            MatchDirector.SkipMenu = false;
            MatchDirector.Reload();
        }

        // ------------------------------------------------------------------ messages

        void OnMessage(NetConnection from, NetReader r)
        {
            byte type = r.Byte();
            if (type >= NetMsg.State)
            {
                if (match != null && InMatch) match.Handle(from, type, r);
                return;
            }
            switch (type)
            {
                case NetMsg.Ready:
                    if (mode != Mode.Host) return;
                    foreach (var m in members) if (m.id == from.id) m.ready = true;
                    SendLobby();
                    break;
                case NetMsg.LobbyList:
                    if (mode != Mode.Client) return;
                    hostDifficulty = (BotDifficulty.Level)r.Byte();
                    members.Clear();
                    int n = r.Byte();
                    for (int i = 0; i < n && !r.Failed; i++)
                    {
                        int id = r.Byte();
                        string name = r.String();
                        bool ready = r.Bool();
                        members.Add(new Member { id = id, name = name, ready = ready });
                    }
                    break;
                case NetMsg.MatchStart:
                    if (mode != Mode.Client) return;
                    OnMatchStart(r);
                    break;
                case NetMsg.BackToLobby:
                    if (mode != Mode.Client) return;
                    ReturnToLobby();
                    Say("Back in the lobby. Waiting for the host to start the next match.");
                    break;
            }
        }

        void OnMatchStart(NetReader r)
        {
            int seed = r.Int();
            var level = (BotDifficulty.Level)r.Byte();
            int n = r.Byte();
            var roster = new List<Entrant>();
            for (int i = 0; i < n && !r.Failed; i++)
            {
                var e = new Entrant();
                e.id = r.Byte();
                e.name = r.String();
                e.human = r.Bool();
                e.skin = r.Byte();
                e.shirt = r.Byte();
                e.pants = r.Byte();
                e.canopy = r.Byte();
                roster.Add(e);
            }
            var director = MatchDirector.Instance;
            if (r.Failed || director == null || director.Begun)
            {
                Leave("Couldn't start the match (still loading?)");
                return;
            }
            InMatch = true;
            director.BeginClient(seed, roster, LocalId, level);
            BeginSync(director);
        }

        public static string Clean(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) s = "Player";
            if (s.Length > 16) s = s.Substring(0, 16);
            return s;
        }
    }
}
