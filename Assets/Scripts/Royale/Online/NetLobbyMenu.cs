using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The front menu before a match (IMGUI drawn with HudKit): play against bots on this machine, host an online game,
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

        const string NameKey = "Jagti.PlayerName", AddressKey = "Jagti.JoinAddress", PortKey = "Jagti.Port";

        void Awake()
        {
            useGUILayout = false;
        }

        void Start()
        {
            playerName = PlayerPrefs.GetString(NameKey, "Player" + Random.Range(10, 99));
            address = PlayerPrefs.GetString(AddressKey, "192.168.1.");
            portText = PlayerPrefs.GetInt(PortKey, Jagti.Net.NetPeer.DefaultPort).ToString();

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

        // ------------------------------------------------------------------ drawing (HudKit, reference units)

        int page = -1;          // the page on the right: -1 none, 0 settings, 1 controls

        void OnGUI()
        {
            if (!Showing) return;
            HudKit.Begin(GameSettings.HudScale);
            float w = HudKit.W, h = HudKit.H;
            HudKit.Fill(0, 0, w, h, new Color(0.02f, 0.025f, 0.03f, 0.45f));
            HudKit.FadeSidePanel(0, 0, Mathf.Min(900f, w * 0.7f), h, new Color(0.02f, 0.025f, 0.03f, 0.94f));
            HudKit.FadePanel(0, h - 220f, w, 220f, new Color(0.02f, 0.025f, 0.03f, 0.6f), false);

            float x = 80f, y = 56f;
            HudKit.Label(x, y, 600f, 22f, "IIM JAMMU  \u00b7  JAGTI CAMPUS", 16f, HudKit.Accent, TextAnchor.MiddleLeft, true);
            HudKit.Label(x - 4f, y + 14f, 600f, 104f, "JAGTI", 104f, HudKit.Text, TextAnchor.MiddleLeft, true);
            HudKit.Label(x - 4f, y + 98f, 600f, 104f, "ROYALE", 104f, HudKit.Accent, TextAnchor.MiddleLeft, true);
            HudKit.Label(x, y + 200f, 600f, 26f, "Last one standing on the IIM Jammu campus.", 20f, HudKit.TextDim);
            y += 252f;

            const float cw = 440f;
            if (NetSession.Online) Lobby(x, y, cw);
            else Front(x, y, cw);

            // Settings and controls along the bottom, with their page on the right.
            float by = h - 76f;
            if (HudKit.Button(x, by, 140f, 44f, "SETTINGS", page == 0 ? HudKit.ButtonKind.TabOn : HudKit.ButtonKind.Tab)) page = page == 0 ? -1 : 0;
            if (HudKit.Button(x + 150f, by, 140f, 44f, "CONTROLS", page == 1 ? HudKit.ButtonKind.TabOn : HudKit.ButtonKind.Tab)) page = page == 1 ? -1 : 1;
            if (!NetSession.Online && HudKit.Button(x + 300f, by, 140f, 44f, "QUIT", HudKit.ButtonKind.Quiet))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            if (page >= 0)
            {
                float px = x + cw + 80f, pw = Mathf.Min(640f, w - px - 60f), py = 80f;
                if (pw >= 360f)
                {
                    float ph = Mathf.Min(h - py - 120f, page == 0 ? 720f : 480f);
                    HudKit.Panel(px, py, pw, ph, new Color(0.06f, 0.07f, 0.09f, 0.9f), 10f);
                    HudKit.Outline(px, py, pw, ph, HudKit.Line, 1f, 10f);
                    HudKit.Label(px + 32f, py + 20f, pw - 64f, 40f, page == 0 ? "SETTINGS" : "CONTROLS", 30f, HudKit.Text, TextAnchor.MiddleLeft, true);
                    if (page == 0) MenuPanels.Settings(px + 32f, py + 76f, pw - 64f, null);
                    else MenuPanels.Controls(px + 32f, py + 80f, pw - 64f);
                }
            }
        }

        static void Heading(float x, float y, float w, string text)
        {
            HudKit.Label(x, y, w, 24f, text, 15f, HudKit.Accent, TextAnchor.MiddleLeft, true);
            float tw = HudKit.TextWidth(text, 15f, true);
            HudKit.Fill(x + tw + 12f, y + 12f, w - tw - 12f, 1f, HudKit.Line);
        }

        void Front(float x, float y, float pw)
        {
            HudKit.Label(x, y, 120f, 40f, "Your name", 18f, HudKit.TextDim);
            playerName = HudKit.TextField(x + 110f, y, pw - 110f, 40f, playerName, 16);
            y += 54f;

            if (HudKit.Button(x, y, pw, 78f, "PLAY", HudKit.ButtonKind.Primary, true, "AGAINST " + director.botCount + " BOTS  \u00b7  ENTER")) PlayOffline();
            y += 90f;
            y = Difficulty(x, y, pw, true);
            y += 22f;

            Heading(x, y, pw, "PLAY WITH FRIENDS");
            y += 28f;
            HudKit.Paragraph(x, y, pw, 40f, "Up to 8 players on the same network; bots fill the rest of the 30 places. One of you hosts, the others join.", 15f, HudKit.TextDim);
            y += 44f;
            if (HudKit.Button(x, y, pw * 0.5f - 4f, 42f, "HOST A GAME"))
            {
                Save();
                NetSession.Ensure().Host(playerName, Port);
            }
            if (HudKit.Button(x + pw * 0.5f + 4f, y, pw * 0.5f - 4f, 42f, "FIND GAMES"))
                NetSession.Ensure().Search(Port);
            y += 52f;

            HudKit.Label(x, y, 110f, 38f, "Host address", 16f, HudKit.TextDim);
            address = HudKit.TextField(x + 110f, y, pw - 110f - 170f, 38f, address, 64);
            portText = HudKit.TextField(x + pw - 164f, y, 70f, 38f, portText, 5);
            if (HudKit.Button(x + pw - 88f, y, 88f, 38f, "JOIN")) Join(address);
            y += 48f;

            var s = NetSession.Instance;
            if (s != null)
            {
                foreach (var f in s.found)
                {
                    string text = f.name.ToUpper() + "'S GAME";
                    string sub = f.players + (f.players == 1 ? " player" : " players") + "  \u00b7  " + f.address;
                    if (HudKit.Button(x, y, pw, 48f, text, HudKit.ButtonKind.Normal, true, sub))
                    {
                        address = f.address;
                        Join(f.address);
                    }
                    y += 54f;
                }
                if (s.Searching && s.found.Count == 0) { HudKit.Label(x, y, pw, 22f, "Looking...", 16f, HudKit.TextDim); y += 24f; }
                StatusLine(s, x, y, pw);
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
                Heading(x, y, pw, "YOU ARE HOSTING ON PORT " + s.peer.Port);
                y += 28f;
                HudKit.Label(x, y, pw, 22f, "Friends type one of these addresses:", 16f, HudKit.TextDim);
                y += 24f;
                HudKit.Label(x, y, pw, 28f, string.Join("    ", NetSession.LocalAddresses().ToArray()), 22f, HudKit.Text, TextAnchor.MiddleLeft, true);
                y += 32f;
                HudKit.Paragraph(x, y, pw, 40f, "Over the internet: everyone joins one VPN network (for example a free virtual LAN app), or forward UDP port " + s.peer.Port + " on your router.", 14f, HudKit.TextDim);
                y += 46f;
            }
            else
            {
                Heading(x, y, pw, s.Joining ? "JOINING" : "IN THE LOBBY");
                y += 28f;
                HudKit.Label(x, y, pw, 22f, s.Joining ? "Asking the host to let you in..." : "The host starts the match.", 17f, HudKit.Text);
                y += 34f;
            }

            Heading(x, y, pw, "PLAYERS  " + s.members.Count + " / " + Jagti.Net.NetPeer.MaxPlayers);
            y += 30f;
            foreach (var m in s.members)
            {
                bool me = m.id == s.LocalId;
                HudKit.Panel(x, y, pw, 34f, me ? new Color(0.25f, 0.13f, 0.04f, 0.75f) : HudKit.Ink, 5f);
                HudKit.Label(x + 12f, y, pw * 0.6f, 34f, m.name, 18f, me ? HudKit.Accent : HudKit.Text, TextAnchor.MiddleLeft, true);
                string tag = m.id == 1 ? "HOST" : m.ready ? "READY" : "LOADING";
                if (host && m.id != 1)
                {
                    var c = s.peer.Find(m.id);
                    if (c != null) tag += "  \u00b7  " + Mathf.RoundToInt(c.rtt * 1000f) + " MS";
                }
                HudKit.Label(x, y, pw - 12f, 34f, tag, 14f, m.ready || m.id == 1 ? HudKit.TextDim : HudKit.HazeColour, TextAnchor.MiddleRight, true);
                y += 38f;
            }
            int bots = Mathf.Max(0, director.botCount + 1 - Mathf.Max(1, s.members.Count));
            HudKit.Label(x, y, pw, 22f, "Plus " + bots + " bots.", 15f, HudKit.TextDim);
            y += 32f;

            if (host)
            {
                y = Difficulty(x, y, pw, true);
                y += 10f;
                bool ready = s.AllReady;
                if (HudKit.Button(x, y, pw, 70f, ready ? "START THE MATCH" : "WAITING FOR EVERYONE...", HudKit.ButtonKind.Primary, ready)) s.StartMatch(director);
                y += 82f;
            }
            else
            {
                HudKit.Label(x, y, pw, 22f, "Bots: " + BotDifficulty.Name(s.hostDifficulty) + " (the host chooses)", 16f, HudKit.TextDim);
                y += 30f;
            }

            StatusLine(s, x, y, pw);
            y += 44f;
            if (HudKit.Button(x, y, 200f, 42f, host ? "CLOSE THE GAME" : "LEAVE")) s.Leave(host ? "Game closed." : "You left the game.");
        }

        static string[] botNames;

        float Difficulty(float x, float y, float pw, bool editable)
        {
            if (botNames == null)
            {
                botNames = new string[3];
                for (int i = 0; i < 3; i++) botNames[i] = BotDifficulty.Name((BotDifficulty.Level)i);
            }
            int picked = HudKit.Choice(x, y, pw, "Bots", botNames, (int)BotDifficulty.Current, editable);
            if (editable && picked != (int)BotDifficulty.Current) BotDifficulty.Current = (BotDifficulty.Level)picked;
            y += 40f;
            HudKit.Label(x + pw * 0.42f, y, pw * 0.58f, 20f, BotDifficulty.Describe(BotDifficulty.Current), 14f, HudKit.TextDim);
            return y + 22f;
        }

        void StatusLine(NetSession s, float x, float y, float pw)
        {
            if (string.IsNullOrEmpty(s.Status) || Time.unscaledTime - s.StatusTime > 20f) return;
            HudKit.Paragraph(x, y, pw, 40f, s.Status, 15f, HudKit.Text);
        }
    }
}
