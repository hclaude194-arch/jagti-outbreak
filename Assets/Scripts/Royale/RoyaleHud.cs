using System.Collections.Generic;
using Jagti.Gameplay;
using Jagti.Player;
using Jagti.World;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The battle royale HUD (IMGUI drawn with HudKit, no Canvas setup). This file holds the state,
    /// input and the top of the screen: compass, kill feed, announcements, place names and the
    /// screen tints. RoyaleHud.Combat.cs draws the player card, hotbar, ammo, crosshair, prompts
    /// and the drop; RoyaleHud.Maps.cs the minimap, haze timer and full map; RoyaleHud.Menus.cs
    /// the pause menu, settings and results.
    /// </summary>
    public partial class RoyaleHud : MonoBehaviour
    {
        public static RoyaleHud Instance { get; private set; }

        public Combatant player;
        public PlayerArsenal arsenal;
        public SkyDiver diver;
        public FirstPersonController fpc;
        public MatchDirector director;
        public HazeZone haze;
        public Texture2D mapTexture;
        public Rect mapWorld;               // world X/Z covered by mapTexture (x, z, width, depth)
        public PlaceData[] places;
        public PlayerMover mover;
        /// <summary>Loot rating per place (0 to 2, from the loot spots around it), filled by RoyaleBuilder.</summary>
        public int[] placeLoot;

        const float M = 24f;                // screen margin in reference units

        bool hasWaypoint;
        Vector3 waypoint;
        int areaIndex = -1;
        float areaTime = -10f;

        bool fullMap;
        bool paused;
        bool results;
        bool spectating;            // following someone after the player is out (SpectatorCam)
        bool focused = true;        // the game window has the keyboard
        int hazeTick = -1;          // last whole second ticked in the haze countdown
        float pingTime = -10f;      // a marker was just dropped where the player looked
        float fps, fpsAcc;
        int fpsFrames;
        float damageFlash;
        float lastHealth = -1f;
        float trailHealth = 100f, trailHold;
        int appliedSettings = -1;
        float heartbeat;

        struct Note
        {
            public string text;
            public float time;
        }
        readonly List<Note> pickups = new List<Note>();
        readonly List<KeyValuePair<Vector3, float>> threats = new List<KeyValuePair<Vector3, float>>();

        struct Number
        {
            public Vector3 at;
            public float time;
            public string text;
            public Color colour;
            public bool shield;
        }
        readonly List<Number> numbers = new List<Number>();
        string eliminated;
        float eliminatedTime = -9f;

        void Awake()
        {
            Instance = this;
            useGUILayout = false;      // no GUILayout here: skips IMGUI's layout pass every frame
        }

        void OnEnable()
        {
            Ballistics.PlayerNearMiss += OnNearMiss;
            Combatant.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            Ballistics.PlayerNearMiss -= OnNearMiss;
            Combatant.Damaged -= OnDamaged;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnNearMiss(Vector3 from)
        {
            threats.Add(new KeyValuePair<Vector3, float>(from, Time.time));
        }

        /// <summary>Damage numbers over the people you hit; armour breaks and eliminations called out.</summary>
        void OnDamaged(Combatant.DamageInfo d)
        {
            if (player == null) return;
            if (d.victim == player)
            {
                if (d.armourBroke)
                {
                    Feed(d.head ? "Your helmet broke" : "Your vest broke");
                    SynthAudio.Play2D("armour_break", 0.7f);
                }
                return;
            }
            if (d.attacker != player) return;
            bool armoured = d.absorbed > d.amount * 0.25f;
            Color c = d.head ? new Color(1f, 0.82f, 0.2f) : armoured ? HudKit.Shield : Color.white;
            if (d.killing) c = new Color(1f, 0.35f, 0.25f);
            numbers.Add(new Number
            {
                at = d.victim.AimPoint + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.1f, 0.4f), Random.Range(-0.3f, 0.3f)),
                time = Time.time,
                text = Mathf.CeilToInt(d.amount).ToString(),
                colour = c,
            });
            if (d.armourBroke)
            {
                numbers.Add(new Number { at = d.victim.AimPoint + Vector3.up * 0.7f, time = Time.time, text = d.head ? "HELMET BROKEN" : "VEST BROKEN", colour = HudKit.Shield, shield = true });
                SynthAudio.Play2D("armour_break", 0.6f);
            }
            if (d.killing)
            {
                eliminated = d.victim.displayName;
                eliminatedTime = Time.time;
            }
            if (numbers.Count > 24) numbers.RemoveAt(0);
        }

        /// <summary>A short line in the pickup feed (lower left).</summary>
        public void Feed(string text)
        {
            pickups.Add(new Note { text = text, time = Time.time });
            if (pickups.Count > 5) pickups.RemoveAt(0);
        }

        public void ShowResults()
        {
            results = true;
            spectating = false;
            paused = false;
            fullMap = false;
            GameState.MapOpen = false;
            GameState.Paused = true;   // stops the walker from grabbing the mouse again
            Time.timeScale = 1f;
            GameState.SetCursorLocked(false);
        }

        void Update()
        {
            fpsAcc += Time.unscaledDeltaTime;
            fpsFrames++;
            if (fpsAcc >= 0.5f)
            {
                fps = fpsFrames / fpsAcc;
                fpsAcc = 0f;
                fpsFrames = 0;
            }
            if (appliedSettings != GameSettings.Version) ApplySettings();
            if (Input.GetKeyDown(KeyCode.F3)) GameSettings.ShowFps = !GameSettings.ShowFps;
            if (director != null && !director.Begun) return;   // the front menu is up

            if (spectating)
            {
                // Enter or Esc goes back to the results; the spectator camera reads the mouse itself.
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape)) SetSpectating(false);
                return;
            }
            if (results)
            {
                // Online the host's Enter would end the match for everyone still playing; only when it's over.
                bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                if (enter && !NetSession.IsClient && (!NetSession.IsHost || Combatant.AliveCount() <= 1)) director.Restart();
                return;
            }
            if (Input.GetKeyDown(KeyCode.M)) SetMap(!fullMap);
            TrackArea();
            HazeCountdown();
            if (!paused && !fullMap && player != null && player.Alive && Cursor.lockState == CursorLockMode.Locked &&
                (Input.GetKeyDown(KeyCode.T) || Input.GetMouseButtonDown(2)))
                PingWhereLooking();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (fullMap) SetMap(false);
                else SetPaused(!paused);
            }

            if (player != null)
            {
                float h = player.Health.current;
                if (lastHealth >= 0f && h < lastHealth - 0.5f)
                {
                    damageFlash = Mathf.Clamp01(damageFlash + (lastHealth - h) / 35f);
                    if (fpc != null) fpc.Shake(Mathf.Clamp((lastHealth - h) / 25f, 0.15f, 0.8f));
                    if (player.LastAttacker != null) threats.Add(new KeyValuePair<Vector3, float>(player.LastHitFrom, Time.time));
                    SynthAudio.Play2D("hurt", 0.5f);
                    trailHold = 0.6f;
                }
                lastHealth = h;
                // The chunk just lost lingers, then drains down to the new health.
                if (h >= trailHealth) trailHealth = h;
                else if ((trailHold -= Time.deltaTime) <= 0f) trailHealth = Mathf.MoveTowards(trailHealth, h, Time.deltaTime * 45f);
            }
            damageFlash = Mathf.MoveTowards(damageFlash, 0f, Time.deltaTime * 1.4f);
            if (player != null && player.Alive && player.Landed && player.Health.current < 25f)
            {
                heartbeat -= Time.deltaTime;
                if (heartbeat <= 0f)
                {
                    heartbeat = 0.9f;
                    SynthAudio.Play2D("heartbeat", 0.5f);
                }
            }
        }

        void ApplySettings()
        {
            appliedSettings = GameSettings.Version;
            if (fpc != null)
            {
                fpc.mouseSensitivity = GameSettings.Sensitivity;
                fpc.invertY = GameSettings.InvertY;
                fpc.toggleSprint = GameSettings.ToggleSprint;
            }
            if (arsenal != null) arsenal.SetBaseFov(GameSettings.Fov);
            else if (Camera.main != null) Camera.main.fieldOfView = GameSettings.Fov;
            AudioListener.volume = focused || !GameSettings.BackgroundPause ? GameSettings.Volume : 0f;
        }

        /// <summary>Clicking away from the game: open the menu (offline the match stops) and go quiet.</summary>
        void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            ApplySettings();
            if (hasFocus || !GameSettings.BackgroundPause) return;
            if (director == null || !director.Begun || results || spectating || paused || player == null || !player.Alive) return;
            if (fullMap) SetMap(false);
            SetPaused(true);
        }

        /// <summary>Drops the marker on whatever the crosshair is on (up to 800 m); doing it again on the marker clears it.</summary>
        void PingWhereLooking()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var hits = Physics.RaycastAll(ray, 800f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 at = Vector3.zero;
            foreach (var hit in hits)
            {
                if (hit.distance >= best || hit.collider.transform.IsChildOf(player.transform)) continue;
                best = hit.distance;
                at = hit.point;
            }
            if (best == float.MaxValue) return;
            if (hasWaypoint && (waypoint - at).sqrMagnitude < 8f * 8f)
            {
                hasWaypoint = false;
                SynthAudio.Play2D("marker_clear", 0.5f);
                return;
            }
            waypoint = at;
            hasWaypoint = true;
            pingTime = Time.time;
            SynthAudio.Play2D("marker_place", 0.55f);
        }

        /// <summary>Ticks for the last five seconds before the haze moves, and a warning as it starts.</summary>
        void HazeCountdown()
        {
            if (haze == null || !haze.Running || haze.RoundIndex >= haze.rounds.Length || player == null || !player.Alive)
            {
                hazeTick = -1;
                return;
            }
            int sec = haze.Closing ? -1 : Mathf.CeilToInt(haze.TimeLeft);
            if (sec == hazeTick) return;
            if (sec >= 1 && sec <= 5) SynthAudio.Play2D(sec == 1 ? "countdown_last" : "countdown", sec <= 3 ? 0.6f : 0.45f);
            // The haze's own surge plays as it moves (HazeAudio); without the sound library, an alert.
            else if (sec < 0 && hazeTick >= 0 && hazeTick <= 2 && Jagti.Audio.SoundSystem.Instance == null) SynthAudio.Play2D("alert", 0.45f);
            hazeTick = sec;
        }

        /// <summary>The results screen's Spectate button, and back again.</summary>
        void SetSpectating(bool on)
        {
            var spec = SpectatorCam.Instance;
            if (on && (spec == null || !SpectatorCam.AnyoneToWatch())) return;
            spectating = on;
            results = !on;
            fullMap = false;
            if (on) spec.StartSpectating();
            else if (spec != null) spec.StopSpectating();
            GameState.SetCursorLocked(on);
        }

        void SetMap(bool on)
        {
            if (on != fullMap) Jagti.Audio.SoundSystem.Play2D(on ? "map_open" : "map_close", 0.6f);
            fullMap = on;
            GameState.MapOpen = on;
            if (!paused && !results) GameState.SetCursorLocked(!on);
        }

        /// <summary>The named place the player is in (nearest within 110 m); a new one is announced
        /// once the player is out of the sky bus.</summary>
        void TrackArea()
        {
            if (places == null || player == null) return;
            Vector3 p = player.transform.position;
            int best = -1;
            float bestD = 110f * 110f;
            for (int i = 0; i < places.Length; i++)
            {
                float d = (places[i].x - p.x) * (places[i].x - p.x) + (places[i].z - p.z) * (places[i].z - p.z);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best == areaIndex) return;
            areaIndex = best;
            bool flying = diver != null && diver.Current == SkyDiver.State.Aboard;
            if (best >= 0 && !flying && player.Alive) areaTime = Time.time;
        }

        /// <summary>A place name as players should see it: guesses marked "?" in the layout lose their bracket.</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int b = name.IndexOf(" (");
            if (b > 0 && name.IndexOf('?') > b) return name.Substring(0, b);
            return name.Replace("?", "");
        }

        static readonly string[] LootWords = { "LIGHT LOOT", "GOOD LOOT", "RICH LOOT" };

        void SetPaused(bool on)
        {
            paused = on;
            GameState.Paused = on;
            // Online the match can't stop for one player: the menu opens over it.
            Time.timeScale = on && !NetSession.Online ? 0f : 1f;
            GameState.SetCursorLocked(!on && !fullMap);
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (player == null || (director != null && !director.Begun)) return;
            // Only the map, the menu and the results take clicks; the rest of the HUD draws on repaint.
            if (Event.current.type != EventType.Repaint && !paused && !results && !fullMap) return;
            HudKit.Begin(GameSettings.HudScale);
            float w = HudKit.W, h = HudKit.H;

            // Full-screen tints: the haze, damage, low health, and the scope.
            if (haze != null && haze.PlayerInHaze > 0.01f)
            {
                HudKit.Fill(0, 0, w, h, new Color(0.5f, 0.36f, 0.2f, 0.2f * haze.PlayerInHaze));
                HudKit.EdgeVignette(new Color(0.42f, 0.24f, 0.08f, 0.75f * haze.PlayerInHaze));
            }
            if (damageFlash > 0.01f) HudKit.EdgeVignette(new Color(0.75f, 0.02f, 0f, 0.85f * damageFlash));
            if (player.Alive && player.Landed && player.Health.Fraction < 0.3f)
                HudKit.EdgeVignette(new Color(0.55f, 0f, 0f, (0.3f - player.Health.Fraction) * 2.4f * (0.75f + 0.25f * Mathf.Sin(Time.time * 7f))));
            if (arsenal != null && arsenal.Scoped) DrawScope(w, h);

            if (spectating) { DrawSpectating(w, h); return; }
            if (results) { DrawResults(w, h); return; }
            if (!player.Alive && SpectatorCam.Instance != null)
            {
                DrawDeathCam(w, h);
                DrawKillFeed();
                DrawMatchInfo(w, false);
                if (paused) DrawPause(w, h);
                return;
            }

            bool dropping = diver != null && diver.Current != SkyDiver.State.Landed;
            DrawCompass(w);
            DrawKillFeed();
            DrawMinimap(w);
            DrawMatchInfo(w);
            DrawHazeWarning(w);
            DrawBanner(w, h);
            DrawArea(w, h);
            DrawWaypointMarker(w, h);
            if (player.Alive) DrawPlayerCard(h, dropping);
            if (dropping) DrawDrop(w, h);
            else if (player.Alive) DrawCombat(w, h);
            if (fullMap) DrawFullMap(w, h);
            if (paused) DrawPause(w, h);
            if (GameSettings.ShowFps)
                HudKit.Label(M, h - 22f, 300f, 20f, string.Format("{0:0} FPS   {1:0.0} MS", fps, 1000f / Mathf.Max(1f, fps)), 14f, HudKit.TextDim);
        }

        /// <summary>A heading strip across the top: ticks, bearings, the marker, the safe zone and supply drops.</summary>
        void DrawCompass(float w)
        {
            if (fpc == null) return;
            float yaw = fpc.transform.eulerAngles.y;
            const float width = 560f, span = 110f, ch = 30f;
            float cx = w / 2f, y = M;
            HudKit.FadeSidePanel(cx, y, width / 2f, ch, HudKit.Ink);
            HudKit.FadeSidePanel(cx - width / 2f, y, width / 2f, ch, HudKit.Ink, true);
            for (int deg = 0; deg < 360; deg += 5)
            {
                float d = Mathf.DeltaAngle(yaw, deg);
                if (Mathf.Abs(d) > span / 2f) continue;
                float x = cx + d / span * width;
                float fade = 1f - Mathf.Pow(Mathf.Abs(d) / (span / 2f), 3f);
                HudKit.Alpha = fade;
                if (deg % 45 == 0)
                {
                    string t = Cardinal(deg);
                    HudKit.Label(x - 20f, y, 40f, ch, t, t.Length == 1 ? 20f : 16f, deg == 0 ? HudKit.Accent : HudKit.Text, TextAnchor.MiddleCenter, true);
                }
                else if (deg % 15 == 0)
                {
                    HudKit.Label(x - 20f, y + 1f, 40f, 16f, deg.ToString(), 12f, HudKit.TextDim, TextAnchor.MiddleCenter);
                    HudKit.Fill(x - 0.75f, y + ch - 9f, 1.5f, 7f, new Color(1f, 1f, 1f, 0.7f));
                }
                else HudKit.Fill(x - 0.5f, y + ch - 6f, 1f, 4f, new Color(1f, 1f, 1f, 0.45f));
                HudKit.Alpha = 1f;
            }

            // Things to head for, pinned to the ends of the strip when behind.
            Vector3 me = player.transform.position;
            if (haze != null && haze.Running)
            {
                Vector2 safe = haze.RoundIndex < haze.rounds.Length ? haze.NextCentre : haze.Centre;
                Vector2 to = safe - new Vector2(me.x, me.z);
                if (to.magnitude > 15f) CompassMark(cx, y, width, span, yaw, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg, "target", Color.white, null);
            }
            foreach (var s in director.supplyDrops)
            {
                Vector3 to = s - me;
                CompassMark(cx, y, width, span, yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, "drop", new Color(1f, 0.35f, 0.25f), null);
            }
            if (hasWaypoint)
            {
                Vector3 to = waypoint - me;
                CompassMark(cx, y, width, span, yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, "pin", HudKit.Marker,
                            Mathf.RoundToInt(new Vector2(to.x, to.z).magnitude) + " M");
            }

            // The heading under the middle.
            HudKit.Fill(cx - 1f, y + ch - 2f, 2f, 8f, HudKit.Accent);
            HudKit.Panel(cx - 26f, y + ch + 6f, 52f, 22f, HudKit.InkDeep, 4f);
            HudKit.Label(cx - 26f, y + ch + 6f, 52f, 22f, Mathf.RoundToInt(yaw % 360f).ToString("000"), 16f, HudKit.Text, TextAnchor.MiddleCenter, true, false);
        }

        static string Cardinal(int deg)
        {
            switch (deg)
            {
                case 0: return "N";
                case 45: return "NE";
                case 90: return "E";
                case 135: return "SE";
                case 180: return "S";
                case 225: return "SW";
                case 270: return "W";
                default: return "NW";
            }
        }

        void CompassMark(float cx, float y, float width, float span, float yaw, float bearing, string icon, Color c, string label)
        {
            float d = Mathf.Clamp(Mathf.DeltaAngle(yaw, bearing), -span / 2f, span / 2f);
            float x = cx + d / span * width;
            HudKit.IconAt(icon, x, y - 9f, 16f, c);
            if (label != null) HudKit.Label(x - 40f, y + 30f, 80f, 16f, label, 13f, c, TextAnchor.MiddleCenter, true);
        }

        /// <summary>The kill feed (top left): who took out whom, with what; lines with you in them stand out.</summary>
        void DrawKillFeed()
        {
            if (director == null) return;
            float y = M;
            int shown = 0;
            const float lh = 30f, size = 17f;
            for (int i = director.feed.Count - 1; i >= 0 && shown < 5; i--)
            {
                var f = director.feed[i];
                float age = Time.time - f.time;
                if (age > 8f) break;
                HudKit.Alpha = Mathf.Clamp01(8f - age);
                float x = M - 24f * Mathf.Pow(Mathf.Clamp01(1f - age / 0.2f), 2f);

                // Measure, then draw the backing and the parts.
                string weapon = string.IsNullOrEmpty(f.weapon) ? "" : f.weapon.ToUpper();
                float width;
                if (f.killer != null)
                    width = HudKit.TextWidth(f.killer, size, true) + 12f + HudKit.TextWidth(weapon, 13f, true) + 16f + (f.head ? 22f : 0f) + 12f + HudKit.TextWidth(f.victim, size, true);
                else
                    width = 24f + HudKit.TextWidth(FeedRest(f), size);
                HudKit.Panel(x, y, width + 24f, lh, f.involvesPlayer ? new Color(0.25f, 0.13f, 0.04f, 0.75f) : HudKit.Ink, 4f);
                if (f.involvesPlayer) HudKit.Panel(x, y + 4f, 3f, lh - 8f, HudKit.Accent, 1.5f);
                float tx = x + 12f;
                if (f.killer != null)
                {
                    HudKit.Label(tx, y, 300f, lh, f.killer, size, f.byPlayer ? HudKit.Accent : HudKit.Text, TextAnchor.MiddleLeft, true);
                    tx += HudKit.TextWidth(f.killer, size, true) + 8f;
                    float ww = HudKit.TextWidth(weapon, 13f, true) + 12f;
                    HudKit.Panel(tx, y + 6f, ww, lh - 12f, new Color(1f, 1f, 1f, 0.1f), 3f);
                    HudKit.Label(tx, y + 6f, ww, lh - 12f, weapon, 13f, HudKit.TextDim, TextAnchor.MiddleCenter, true, false);
                    tx += ww + 4f;
                    if (f.head)
                    {
                        HudKit.IconAt("skull", tx + 9f, y + lh / 2f, 16f, HudKit.Accent);
                        tx += 22f;
                    }
                    tx += 4f;
                    HudKit.Label(tx, y, 300f, lh, f.victim, size, f.ofPlayer ? HudKit.Danger : HudKit.Text, TextAnchor.MiddleLeft, true);
                }
                else
                {
                    bool hazeDeath = f.weapon == "the haze";
                    HudKit.IconAt(hazeDeath ? "haze" : "skull", tx + 8f, y + lh / 2f, 16f, hazeDeath ? HudKit.HazeColour : HudKit.TextDim);
                    HudKit.Label(tx + 22f, y, 400f, lh, FeedRest(f), size, f.ofPlayer ? HudKit.Danger : HudKit.Text);
                }
                HudKit.Alpha = 1f;
                y += lh + 5f;
                shown++;
            }
        }

        static string FeedRest(MatchDirector.FeedLine f)
        {
            return string.IsNullOrEmpty(f.text) ? "" : f.text;
        }

        /// <summary>Match announcements, large in the upper middle, with a saffron rule under them.</summary>
        void DrawBanner(float w, float h)
        {
            if (director == null || string.IsNullOrEmpty(director.Banner)) return;
            float age = Time.time - director.BannerTime;
            if (age > 5f) return;
            HudKit.Alpha = Mathf.Clamp01(5f - age) * Mathf.Clamp01(age * 5f);
            float y = h * 0.22f;
            HudKit.Label(0, y, w, 40f, director.Banner.ToUpper(), 30f, HudKit.Text, TextAnchor.MiddleCenter, true);
            float bw = Mathf.Lerp(20f, 90f, Mathf.Clamp01(age * 3f));
            HudKit.Fill(w / 2f - bw / 2f, y + 44f, bw, 3f, HudKit.Accent);
            HudKit.Alpha = 1f;
        }

        /// <summary>The place name, large, for a few seconds after walking or landing into it.</summary>
        void DrawArea(float w, float h)
        {
            float age = Time.time - areaTime;
            if (areaIndex < 0 || age > 3.8f || places == null) return;
            HudKit.Alpha = Mathf.Clamp01(age * 3f) * Mathf.Clamp01(3.8f - age);
            float y = h * 0.125f + 34f;
            HudKit.Label(0, y, w, 20f, "NOW ENTERING", 14f, HudKit.Accent, TextAnchor.MiddleCenter, true);
            HudKit.Label(0, y + 18f, w, 50f, CleanName(places[areaIndex].name).ToUpper(), 42f, HudKit.Text, TextAnchor.MiddleCenter, true);
            int rating = Mathf.Clamp(placeLoot != null && areaIndex < placeLoot.Length ? placeLoot[areaIndex] : 0, 0, 2);
            float lw = HudKit.TextWidth(LootWords[rating], 15f, true);
            float x = w / 2f - (lw + 30f) / 2f;
            for (int k = 0; k < 3; k++) HudKit.Dot(x + k * 9f + 3f, y + 80f, 7f, k <= rating ? HudKit.Accent : new Color(1f, 1f, 1f, 0.25f));
            HudKit.Label(x + 30f, y + 70f, lw + 4f, 20f, LootWords[rating], 15f, HudKit.TextDim, TextAnchor.MiddleLeft, true);
            HudKit.Alpha = 1f;
        }

        /// <summary>When the player is outside the safe area: how far it is and which way.</summary>
        void DrawHazeWarning(float w)
        {
            if (haze == null || !haze.Running || player == null || !player.Alive || haze.IsInside(player.transform.position)) return;
            if (diver != null && diver.Current == SkyDiver.State.Aboard) return;
            Vector2 to = haze.Centre - new Vector2(player.transform.position.x, player.transform.position.z);
            float dist = Mathf.Max(0f, to.magnitude - haze.Radius);
            float y = M + 66f, pw = 380f, x = w / 2f - pw / 2f;
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 6f);
            HudKit.Panel(x, y, pw, 42f, new Color(0.3f, 0.14f, 0.03f, 0.85f), 6f);
            HudKit.Outline(x, y, pw, 42f, HudKit.WithAlpha(HudKit.HazeColour, pulse), 1.5f, 6f);
            HudKit.IconAt("haze", x + 26f, y + 21f, 24f, HudKit.HazeColour);
            HudKit.Label(x + 46f, y, 220f, 42f, "OUTSIDE THE SAFE AREA", 19f, HudKit.Text, TextAnchor.MiddleLeft, true);
            HudKit.Label(x + pw - 110f, y, 66f, 42f, Mathf.RoundToInt(dist) + " M", 19f, HudKit.HazeColour, TextAnchor.MiddleRight, true);
            float yaw = fpc != null ? fpc.transform.eulerAngles.y : player.transform.eulerAngles.y;
            float ang = Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
            var m = HudKit.Rotate(ang, x + pw - 22f, y + 21f);
            HudKit.IconAt("arrow", x + pw - 22f, y + 21f, 22f, HudKit.Text);
            GUI.matrix = m;
        }

        /// <summary>The marker drawn over the world where it sits, with its distance.</summary>
        void DrawWaypointMarker(float w, float h)
        {
            if (!hasWaypoint || fullMap) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(waypoint + Vector3.up * 2f);
            if (sp.z <= 0f) return;
            float x = sp.x / HudKit.S, y = (Screen.height - sp.y) / HudKit.S;
            float pa = Time.time - pingTime;
            if (pa < 0.7f)
            {
                // A ripple where the marker landed.
                HudKit.Alpha = 1f - pa / 0.7f;
                HudKit.Ring(x, y - 12f, 14f + pa * 70f, 2.5f, 0f, 1f, HudKit.Marker);
                HudKit.Alpha = 1f;
            }
            HudKit.IconAt("pin", x, y - 12f, 24f, HudKit.Marker);
            float dist = new Vector2(waypoint.x - player.transform.position.x, waypoint.z - player.transform.position.z).magnitude;
            HudKit.Label(x - 40f, y + 2f, 80f, 18f, Mathf.RoundToInt(dist) + " M", 14f, HudKit.Text, TextAnchor.MiddleCenter, true);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Online: who is still in, and the link to the host.</summary>
        static string OnlineLine()
        {
            var s = NetSession.Instance;
            if (s == null || !NetSession.Online) return "";
            int humans = 0;
            foreach (var c in Combatant.All) if (c.Alive && c.netId > 0 && c.netId < 100) humans++;
            string line = Combatant.AliveCount() + " still in (" + humans + (humans == 1 ? " player" : " players") + ")";
            if (NetSession.IsClient && s.peer.Server != null) line += ", " + Mathf.RoundToInt(s.peer.Server.rtt * 1000f) + " ms to the host";
            return line;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
