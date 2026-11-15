using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The front menu before a match (IMGUI): play against bots on this machine, host an online game,
    /// or join one by address or from the games found on this network. Once in an online game it
    /// shows the lobby: who is in, who is still loading, and (for the host) the start button.
    /// MatchDirector adds it when the scene loads; it goes away when the match begins.
    /// </summary>
    public class NetLobbyMenu : MonoBehaviour
    {
        public MatchDirector director;

        string playerName;
        string address;
        string portText;
        GUIStyle title, sub, label, small, field;
        Texture2D white;

        const string NameKey = "Jagti.PlayerName", AddressKey = "Jagti.JoinAddress", PortKey = "Jagti.Port";

        void Start()
        {
            playerName = PlayerPrefs.GetString(NameKey, "Player" + Random.Range(10, 99));
            address = PlayerPrefs.GetString(AddressKey, "192.168.1.");
            portText = PlayerPrefs.GetInt(PortKey, Jagti.Net.NetPeer.DefaultPort).ToString();
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();

            if (NetSession.Online)
            {
                if (NetSession.IsClient) NetSession.Instance.SendReady();
            }
            else if (MatchDirector.SkipMenu)
            {
                MatchDirector.SkipMenu = false;
                director.BeginOffline();
            }
        }

        bool Showing { get { return director != null && !director.Begun; } }

        void Update()
        {
            if (!Showing) return;
            GameState.Paused = true;
            if (Cursor.lockState != CursorLockMode.None) GameState.SetCursorLocked(false);
            if (!NetSession.Online && GUIUtility.keyboardControl == 0 &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                PlayOffline();
        }

        void PlayOffline()
        {
            Save();
            director.BeginOffline();
        }

        void Save()
        {
            playerName = NetSession.Clean(playerName);
            PlayerPrefs.SetString(NameKey, playerName);
            PlayerPrefs.SetString(AddressKey, address.Trim());
            PlayerPrefs.SetInt(PortKey, Port);
            PlayerPrefs.Save();
        }

        int Port
        {
            get
            {
                int p;
                return int.TryParse(portText, out p) && p > 0 && p < 65536 ? p : Jagti.Net.NetPeer.DefaultPort;
            }
        }

        // ------------------------------------------------------------------ drawing

        void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            sub = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.86f, 0.78f) } };
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            field = new GUIStyle(GUI.skin.textField) { fontSize = 16 };
        }

        void OnGUI()
        {
            if (!Showing) return;
            Styles();
            float w = Screen.width, h = Screen.height;
            var old = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.07f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, w, h), white);
            GUI.color = old;

            float pw = Mathf.Min(560f, w - 32f);
            float x = (w - pw) / 2f;
            float y = Mathf.Max(20f, h * 0.08f);
            GUI.Label(new Rect(0, y, w, 56), "JAGTI ROYALE", title);
            y += 58;
            GUI.Label(new Rect(0, y, w, 26), "Last one standing on the IIM Jammu campus", sub);
            y += 44;

            if (NetSession.Online) Lobby(x, y, pw);
            else Front(x, y, pw);
        }

        void Front(float x, float y, float pw)
        {
            GUI.Label(new Rect(x, y, 120, 28), "Your name", label);
            playerName = GUI.TextField(new Rect(x + 120, y, pw - 120, 28), playerName ?? "", 16, field);
            y += 42;

            if (GUI.Button(new Rect(x, y, pw, 44), "Play against bots  (Enter)")) PlayOffline();
            y += 52;
            y = Difficulty(x, y, pw, true);
            y += 18;

            GUI.Label(new Rect(x, y, pw, 24), "Online, with friends on the same network", label);
            y += 26;
            GUI.Label(new Rect(x, y, pw, 40), "Up to 8 players; bots fill the rest of the 30 places. One of you hosts, the others join.", small);
            y += 40;
            if (GUI.Button(new Rect(x, y, pw * 0.5f - 4, 36), "Host a game"))
            {
                Save();
                NetSession.Ensure().Host(playerName, Port);
            }
            if (GUI.Button(new Rect(x + pw * 0.5f + 4, y, pw * 0.5f - 4, 36), "Find games on this network"))
                NetSession.Ensure().Search(Port);
            y += 44;

            GUI.Label(new Rect(x, y + 4, 130, 28), "Host's address", label);
            address = GUI.TextField(new Rect(x + 130, y, pw - 130 - 170, 30), address ?? "", 64, field);
            portText = GUI.TextField(new Rect(x + pw - 164, y, 70, 30), portText ?? "", 5, field);
            if (GUI.Button(new Rect(x + pw - 88, y, 88, 30), "Join")) Join(address);
            y += 40;

            var s = NetSession.Instance;
            if (s != null)
            {
                foreach (var f in s.found)
                {
                    string text = "Join " + f.name + "'s game  (" + f.players + (f.players == 1 ? " player" : " players") + ", " + f.address + ")";
                    if (GUI.Button(new Rect(x, y, pw, 30), text))
                    {
                        address = f.address;
                        Join(f.address);
                    }
                    y += 34;
                }
                if (s.Searching && s.found.Count == 0) { GUI.Label(new Rect(x, y, pw, 22), "Looking...", small); y += 24; }
                StatusLine(s, x, y, pw);
                y += 44;
            }

            if (GUI.Button(new Rect(x + pw / 2 - 80, y, 160, 32), "Quit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        void Join(string to)
        {
            Save();
            NetSession.Ensure().Join(to, Port, playerName);
        }

        void Lobby(float x, float y, float pw)
        {
            var s = NetSession.Instance;
            bool host = NetSession.IsHost;
            if (host)
            {
                GUI.Label(new Rect(x, y, pw, 24), "You are hosting on port " + s.peer.Port + ". Friends type one of these addresses:", label);
                y += 26;
                GUI.Label(new Rect(x + 16, y, pw - 16, 24), string.Join("    ", NetSession.LocalAddresses().ToArray()), label);
                y += 26;
                GUI.Label(new Rect(x, y, pw, 36), "Over the internet: everyone joins one VPN network (for example a free virtual LAN app), or forward UDP port " + s.peer.Port + " on your router.", small);
                y += 40;
            }
            else
            {
                GUI.Label(new Rect(x, y, pw, 24), s.Joining ? "Asking the host to let you in..." : "In the host's lobby. The host starts the match.", label);
                y += 32;
            }

            GUI.Label(new Rect(x, y, pw, 24), "Players (" + s.members.Count + " of " + Jagti.Net.NetPeer.MaxPlayers + ")", label);
            y += 26;
            foreach (var m in s.members)
            {
                string tag = m.id == 1 ? "host" : m.ready ? "ready" : "loading";
                if (m.id == s.LocalId) tag += ", you";
                string ping = "";
                if (host && m.id != 1)
                {
                    var c = s.peer.Find(m.id);
                    if (c != null) ping = "   " + Mathf.RoundToInt(c.rtt * 1000f) + " ms";
                }
                GUI.Label(new Rect(x + 16, y, pw - 16, 22), m.name + "   (" + tag + ")" + ping, label);
                y += 24;
            }
            int bots = Mathf.Max(0, director.botCount + 1 - Mathf.Max(1, s.members.Count));
            GUI.Label(new Rect(x, y + 4, pw, 22), "Plus " + bots + " bots.", small);
            y += 30;

            if (host)
            {
                y = Difficulty(x, y, pw, true);
                y += 10;
                bool ready = s.AllReady;
                GUI.enabled = ready;
                if (GUI.Button(new Rect(x, y, pw, 44), ready ? "Start the match" : "Waiting for everyone to load...")) s.StartMatch(director);
                GUI.enabled = true;
                y += 52;
            }
            else
            {
                GUI.Label(new Rect(x, y, pw, 22), "Bots: " + BotDifficulty.Name(s.hostDifficulty) + " (the host chooses)", small);
                y += 30;
            }

            StatusLine(s, x, y, pw);
            y += 44;
            if (GUI.Button(new Rect(x + pw / 2 - 90, y, 180, 34), host ? "Close the game" : "Leave")) s.Leave(host ? "Game closed." : "You left the game.");
        }

        float Difficulty(float x, float y, float pw, bool editable)
        {
            var current = BotDifficulty.Current;
            float gap = 8f, bw = (pw - 80f - gap * 2f) / 3f;
            GUI.Label(new Rect(x, y + 4, 80, 24), "Bots", label);
            for (int i = 0; i < 3; i++)
            {
                var l = (BotDifficulty.Level)i;
                string text = l == current ? "[ " + BotDifficulty.Name(l) + " ]" : BotDifficulty.Name(l);
                GUI.enabled = editable;
                if (GUI.Button(new Rect(x + 80 + i * (bw + gap), y, bw, 30), text)) BotDifficulty.Current = l;
                GUI.enabled = true;
            }
            y += 34;
            GUI.Label(new Rect(x + 80, y, pw - 80, 22), BotDifficulty.Describe(current), small);
            return y + 24;
        }

        void StatusLine(NetSession s, float x, float y, float pw)
        {
            if (string.IsNullOrEmpty(s.Status) || Time.unscaledTime - s.StatusTime > 20f) return;
            GUI.Label(new Rect(x, y, pw, 40), s.Status, small);
        }
    }
}
