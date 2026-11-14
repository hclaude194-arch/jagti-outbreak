using System.Collections.Generic;
using Jagti.Gameplay;
using Jagti.Player;
using Jagti.World;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The battle royale HUD (IMGUI, no Canvas setup): minimap with the haze and next circle,
    /// full map (M), players left, kills and haze timer, kill feed, announcements, health with the
    /// chai boost, armour and pack, gun slots with ammo, heals and grenades, crosshair that widens
    /// with spread, hit markers, scope view, pickup prompt, where shots come from, the haze warning,
    /// drop instructions, pause menu (Esc), results screen, and a frame-time readout (F3).
    /// </summary>
    public class RoyaleHud : MonoBehaviour
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

        bool hasWaypoint;
        Vector3 waypoint;
        int areaIndex = -1;
        float areaTime = -10f;

        Texture2D white, dot, arrow, scope;
        GUIStyle small, normal, big, huge, centre;
        bool fullMap;
        bool paused;
        bool results;
        bool showFps;
        float fps, fpsAcc;
        int fpsFrames;
        float damageFlash;
        float lastHealth = -1f;
        readonly List<KeyValuePair<string, float>> pickups = new List<KeyValuePair<string, float>>();
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
        float heartbeat;
        static readonly string[] PartShort = { "Dot", "2x", "4x", "8x", "Comp", "Supp", "Grip", "Ext mag" };

        void Awake()
        {
            Instance = this;
            white = Solid(Color.white);
            dot = Disc(16, 0f);
            arrow = Arrow(32);
            scope = ScopeMask(256);
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
            Color c = d.head ? new Color(1f, 0.82f, 0.2f) : armoured ? new Color(0.55f, 0.8f, 1f) : Color.white;
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
                numbers.Add(new Number { at = d.victim.AimPoint + Vector3.up * 0.7f, time = Time.time, text = d.head ? "helmet broken" : "vest broken", colour = new Color(0.55f, 0.8f, 1f), shield = true });
                SynthAudio.Play2D("armour_break", 0.6f);
            }
            if (d.killing)
            {
                eliminated = d.victim.displayName;
                eliminatedTime = Time.time;
            }
            if (numbers.Count > 24) numbers.RemoveAt(0);
        }

        public void Feed(string text)
        {
            pickups.Add(new KeyValuePair<string, float>(text, Time.time));
            if (pickups.Count > 5) pickups.RemoveAt(0);
        }

        public void ShowResults()
        {
            results = true;
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
            if (Input.GetKeyDown(KeyCode.F3)) showFps = !showFps;

            if (results)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) director.Restart();
                return;
            }
            if (Input.GetKeyDown(KeyCode.M)) SetMap(!fullMap);
            TrackArea();
            if (Input.GetKeyDown(KeyCode.Escape)) SetPaused(!paused);

            if (player != null)
            {
                float h = player.Health.current;
                if (lastHealth >= 0f && h < lastHealth - 0.5f)
                {
                    damageFlash = Mathf.Clamp01(damageFlash + (lastHealth - h) / 35f);
                    if (fpc != null) fpc.Shake(Mathf.Clamp((lastHealth - h) / 25f, 0.15f, 0.8f));
                    if (player.LastAttacker != null) threats.Add(new KeyValuePair<Vector3, float>(player.LastHitFrom, Time.time));
                    SynthAudio.Play2D("hurt", 0.5f);
                }
                lastHealth = h;
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

        void SetMap(bool on)
        {
            fullMap = on;
            GameState.MapOpen = on;
            if (!paused && !results) GameState.SetCursorLocked(!on);
        }

        /// <summary>The named place the player is in (nearest within 110 m); a new one is announced
        /// once the player is out of the helicopter.</summary>
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
            bool flying = diver != null && diver.Current == SkyDiver.State.InHelicopter;
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

        static readonly string[] LootWords = { "light loot", "good loot", "rich loot" };

        void SetPaused(bool on)
        {
            paused = on;
            GameState.Paused = on;
            Time.timeScale = on ? 0f : 1f;
            GameState.SetCursorLocked(!on && !fullMap);
        }

        // ------------------------------------------------------------------ drawing

        void Styles()
        {
            if (small != null) return;
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            normal = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            huge = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            centre = new GUIStyle(normal) { alignment = TextAnchor.MiddleCenter };
        }

        void OnGUI()
        {
            Styles();
            float w = Screen.width, h = Screen.height;
            if (player == null) return;

            // Full-screen tints: the haze, damage, and the scope.
            if (haze != null && haze.PlayerInHaze > 0.01f) Fill(new Rect(0, 0, w, h), new Color(0.45f, 0.33f, 0.2f, 0.35f * haze.PlayerInHaze));
            if (damageFlash > 0.01f) Fill(new Rect(0, 0, w, h), new Color(0.7f, 0f, 0f, 0.3f * damageFlash));
            if (player.Alive && player.Landed && player.Health.Fraction < 0.3f) Edges(w, h, new Color(0.65f, 0f, 0f, (0.3f - player.Health.Fraction) * 1.6f * (0.75f + 0.25f * Mathf.Sin(Time.time * 7f))));
            if (arsenal != null && arsenal.Scoped) DrawScope(w, h);

            if (results) { DrawResults(w, h); return; }

            DrawTop(w);
            DrawCompass(w);
            DrawFeed();
            DrawBanner(w, h);
            DrawArea(w, h);
            DrawWaypointMarker(w, h);
            if (diver != null && diver.Current != SkyDiver.State.Landed) DrawDrop(w, h);
            else if (player.Alive) DrawCombat(w, h);
            DrawMinimap(w);
            if (fullMap) DrawFullMap(w, h);
            if (paused) DrawPause(w, h);
            if (showFps) Label(new Rect(10, h - 30, 300, 24), string.Format("{0:0} fps  {1:0.0} ms", fps, 1000f / Mathf.Max(1f, fps)), small);
        }

        void DrawTop(float w)
        {
            if (director == null) return;
            int alive = Combatant.AliveCount();
            string haze1 = "";
            if (haze != null && haze.Running && haze.RoundIndex < haze.rounds.Length)
                haze1 = (haze.Closing ? "Haze closing  " : "Haze holds  ") + Clock(haze.TimeLeft);
            else if (haze != null && haze.Running) haze1 = "Haze has closed";
            Fill(new Rect(w / 2 - 170, 8, 340, 30), new Color(0f, 0f, 0f, 0.45f));
            Label(new Rect(w / 2 - 165, 12, 120, 24), alive + " left", normal);
            Label(new Rect(w / 2 - 60, 12, 120, 24), player.Kills + " kills", normal);
            Label(new Rect(w / 2 + 20, 12, 150, 24), haze1, normal);
        }

        void DrawFeed()
        {
            if (director == null) return;
            float y = 12;
            int shown = 0;
            for (int i = director.feed.Count - 1; i >= 0 && shown < 5; i--)
            {
                var f = director.feed[i];
                float age = Time.time - f.time;
                if (age > 7f) break;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(7f - age));
                Fill(new Rect(10, y - 2, 380, 22), f.involvesPlayer ? new Color(0.5f, 0.15f, 0.05f, 0.55f) : new Color(0f, 0f, 0f, 0.4f));
                Label(new Rect(16, y, 380, 22), f.text, small);
                GUI.color = Color.white;
                y += 24;
                shown++;
            }
        }

        void DrawBanner(float w, float h)
        {
            if (director == null || string.IsNullOrEmpty(director.Banner)) return;
            float age = Time.time - director.BannerTime;
            if (age > 5f) return;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(5f - age));
            Label(new Rect(0, h * 0.2f, w, 34), director.Banner, new GUIStyle(big) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
        }

        void DrawDrop(float w, float h)
        {
            string text;
            switch (diver.Current)
            {
                case SkyDiver.State.InHelicopter:
                    text = "SPACE  jump      M  map      mouse  look around";
                    break;
                case SkyDiver.State.FreeFall:
                    text = string.Format("{0:0} m      SPACE  open glider      W  dive      S  slow", diver.HeightAboveGround);
                    break;
                case SkyDiver.State.Glide:
                    text = string.Format("{0:0} m      steer with the mouse      W  faster      S  float", diver.HeightAboveGround);
                    break;
                default:
                    text = "";
                    break;
            }
            Fill(new Rect(w / 2 - 260, h - 70, 520, 34), new Color(0f, 0f, 0f, 0.5f));
            Label(new Rect(w / 2 - 260, h - 66, 520, 28), text, centre);
        }

        void DrawCombat(float w, float h)
        {
            var l = player.loadout;

            // Crosshair: four ticks that spread with the gun's spread; a dot when aiming.
            var gun = l.Current;
            if (arsenal == null || !arsenal.Scoped)
            {
                float gap = 6f;
                if (gun != null)
                {
                    float blend = arsenal != null ? arsenal.AimBlend : 0f;
                    float spread = Mathf.Lerp(gun.def.hipSpread, gun.def.aimSpread, blend) * gun.SpreadScale;
                    if (fpc != null && fpc.HorizontalSpeed > 1f) spread *= 1.5f;
                    if (fpc != null && fpc.Crouching) spread *= 0.8f;
                    if (arsenal != null) spread *= 1f + arsenal.Bloom * 0.4f;
                    gap = 4f + spread * 6f;
                }
                Color cc = new Color(1f, 1f, 1f, 0.85f);
                float cx = w / 2, cy = h / 2;
                if (arsenal != null && arsenal.DotSight)
                {
                    // Red dot (and the 2x sight's ring) instead of the crosshair.
                    var red = new Color(1f, 0.15f, 0.1f, 0.95f);
                    GUI.color = red;
                    GUI.DrawTexture(new Rect(cx - 3, cy - 3, 6, 6), dot);
                    GUI.color = Color.white;
                    if (gun.Sight == 1)
                    {
                        Fill(new Rect(cx - 18, cy - 0.5f, 10, 1), red);
                        Fill(new Rect(cx + 8, cy - 0.5f, 10, 1), red);
                        Fill(new Rect(cx - 0.5f, cy + 8, 1, 10), red);
                    }
                }
                else
                {
                    Fill(new Rect(cx - 1, cy - 1, 2, 2), cc);
                    Fill(new Rect(cx - gap - 8, cy - 1, 8, 2), cc);
                    Fill(new Rect(cx + gap, cy - 1, 8, 2), cc);
                    Fill(new Rect(cx - 1, cy - gap - 8, 2, 8), cc);
                    Fill(new Rect(cx - 1, cy + gap, 2, 8), cc);
                }
            }
            if (arsenal != null && arsenal.HitMarker > 0.01f)
            {
                Color hc = arsenal.HitWasKill ? new Color(1f, 0.25f, 0.2f, arsenal.HitMarker) :
                           arsenal.HitWasHead ? new Color(1f, 0.85f, 0.2f, arsenal.HitMarker) : new Color(1f, 1f, 1f, arsenal.HitMarker);
                HitMark(w / 2, h / 2, hc);
            }
            DrawNumbers();
            float eAge = Time.time - eliminatedTime;
            if (eAge < 2.2f && !string.IsNullOrEmpty(eliminated))
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((2.2f - eAge) * 2f));
                float pop = 1f + Mathf.Max(0f, 0.25f - eAge) * 1.6f;
                var st = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(22 * pop) };
                Label(new Rect(0, h / 2 + 130, w, 34), "ELIMINATED  " + eliminated, st);
                Label(new Rect(0, h / 2 + 162, w, 24), player.Kills + (player.Kills == 1 ? " elimination" : " eliminations"), centre);
                GUI.color = Color.white;
            }

            // Threat marks around the crosshair.
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                float age = Time.time - threats[i].Value;
                if (age > 1.6f) { threats.RemoveAt(i); continue; }
                Vector3 to = threats[i].Key - player.transform.position;
                float ang = Vector3.SignedAngle(player.transform.forward, new Vector3(to.x, 0f, to.z), Vector3.up);
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, new Vector2(w / 2, h / 2));
                Fill(new Rect(w / 2 - 22, h / 2 - 110, 44, 7), new Color(1f, 0.2f, 0.1f, 0.8f * (1f - age / 1.6f)));
                GUI.matrix = m;
            }

            // Pickup prompt and healing.
            string prompt = arsenal != null ? arsenal.Prompt : null;
            if (string.IsNullOrEmpty(prompt) && mover != null) prompt = mover.Prompt;
            if (!string.IsNullOrEmpty(prompt))
            {
                Fill(new Rect(w / 2 - 190, h / 2 + 60, 380, 28), new Color(0f, 0f, 0f, 0.5f));
                Label(new Rect(w / 2 - 190, h / 2 + 62, 380, 24), prompt, centre);
            }
            DrawVehicle(w, h);
            if (player.IsHealing)
            {
                Fill(new Rect(w / 2 - 100, h / 2 + 96, 200, 10), new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(w / 2 - 100, h / 2 + 96, 200 * player.HealProgress, 10), new Color(0.4f, 0.9f, 0.4f, 0.9f));
                Label(new Rect(w / 2 - 100, h / 2 + 108, 200, 22), "Using " + RoyaleItems.ItemName(player.HealingKind, 0, Tier.Common).ToLower(), centre);
            }
            if (arsenal != null && arsenal.Reloading)
            {
                Fill(new Rect(w / 2 - 60, h / 2 + 30, 120, 6), new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(w / 2 - 60, h / 2 + 30, 120 * arsenal.ReloadProgress, 6), new Color(1f, 1f, 1f, 0.9f));
            }

            // The haze warning.
            if (haze != null && !haze.IsInside(player.transform.position))
            {
                Vector2 to = haze.Centre - new Vector2(player.transform.position.x, player.transform.position.z);
                float dist = to.magnitude - haze.Radius;
                Fill(new Rect(w / 2 - 170, 46, 340, 28), new Color(0.45f, 0.25f, 0.05f, 0.75f));
                Label(new Rect(w / 2 - 170, 48, 340, 24), string.Format("In the haze: safe ground {0:0} m away", dist), centre);
                float ang = Vector3.SignedAngle(player.transform.forward, new Vector3(to.x, 0f, to.y), Vector3.up);
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, new Vector2(w / 2, 96));
                GUI.DrawTexture(new Rect(w / 2 - 10, 84, 20, 24), arrow);
                GUI.matrix = m;
            }

            // Health, boost, armour (bottom centre).
            float bx = w / 2 - 180, by = h - 54;
            Fill(new Rect(bx - 4, by - 26, 368, 52), new Color(0f, 0f, 0f, 0.45f));
            float hp = player.Health.Fraction;
            Fill(new Rect(bx, by, 360, 14), new Color(0.15f, 0.15f, 0.15f, 0.9f));
            Fill(new Rect(bx, by, 360 * hp, 14), hp > 0.3f ? new Color(0.92f, 0.92f, 0.9f) : new Color(0.9f, 0.25f, 0.2f));
            if (player.BoostLeft > 0f)
                Fill(new Rect(bx + 360 * hp, by, 360 * Mathf.Min(1f - hp, player.BoostLeft / 100f), 14), new Color(0.95f, 0.75f, 0.35f, 0.7f));
            Label(new Rect(bx, by - 22, 360, 22), string.Format("{0:0}", player.Health.current), small);
            string gear = "";
            if (l.helmet > 0) gear += "Helmet " + l.helmet + " (" + Mathf.CeilToInt(l.helmetDurability) + ")   ";
            if (l.vest > 0) gear += "Vest " + l.vest + " (" + Mathf.CeilToInt(l.vestDurability) + ")   ";
            if (l.backpack > 0) gear += "Pack " + l.backpack;
            Label(new Rect(bx + 50, by - 22, 310, 22), gear, new GUIStyle(small) { alignment = TextAnchor.UpperRight });
            Label(new Rect(bx, by + 16, 360, 20),
                string.Format("Q bandage {0}  medkit {1}   Z chai {2}   G grenade {3}   4 smoke {4}", l.bandages, l.medkits, l.chai, l.grenades, l.smokes), small);
            if (fpc != null && fpc.Crouching) Label(new Rect(bx, by - 44, 120, 20), "Crouched", small);

            // Gun slots (bottom right).
            float sx = w - 250, sy = h - 210;
            for (int i = 0; i < Loadout.Slots; i++)
            {
                var g = l.guns[i];
                bool sel = i == l.selected && g != null;
                Rect r = new Rect(sx, sy + i * 52, 240, 46);
                Fill(r, sel ? new Color(0.1f, 0.1f, 0.1f, 0.75f) : new Color(0f, 0f, 0f, 0.4f));
                if (g == null)
                {
                    Label(new Rect(r.x + 10, r.y + 12, 220, 22), (i + 1) + "   empty", small);
                    continue;
                }
                Fill(new Rect(r.x, r.y, 5, r.height), RoyaleItems.TierColours[(int)g.tier]);
                Label(new Rect(r.x + 12, r.y + 4, 160, 22), (i + 1) + "  " + g.def.name, sel ? normal : small);
                string parts = "";
                foreach (int part in g.parts) if (part >= 0) parts += (parts.Length > 0 ? " " : "") + PartShort[part];
                Label(new Rect(r.x + 12, r.y + 24, 220, 20), parts.Length > 0 ? parts : RoyaleItems.TierNames[(int)g.tier] + "  " + RoyaleItems.AmmoNames[(int)g.def.ammo], small);
                Label(new Rect(r.x + 120, r.y + 4, 112, 26), g.loaded + " / " + l.CountFor(g), new GUIStyle(sel ? big : normal) { alignment = TextAnchor.UpperRight });
                if (g.def.burst > 1) Label(new Rect(r.x + 120, r.y + 26, 112, 18), "burst", new GUIStyle(small) { alignment = TextAnchor.UpperRight });
            }
            if (l.Current == null) Label(new Rect(sx, sy + 158, 240, 22), "Fists (find a gun!)", small);

            // Pickup feed (lower left).
            float py = h - 150;
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                float age = Time.time - pickups[i].Value;
                if (age > 4f) continue;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(4f - age));
                Label(new Rect(14, py, 400, 22), pickups[i].Key, normal);
                GUI.color = Color.white;
                py -= 22;
            }
        }

        void DrawNumbers()
        {
            var cam = Camera.main;
            if (cam == null) return;
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                var n = numbers[i];
                float age = Time.time - n.time;
                if (age > 0.9f) { numbers.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(n.at + Vector3.up * age * 0.6f);
                if (sp.z <= 0f) continue;
                float y = Screen.height - sp.y;
                Color c = n.colour;
                c.a = Mathf.Clamp01((0.9f - age) * 3f);
                var st = new GUIStyle(n.shield ? normal : big) { alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
                if (!n.shield) st.fontSize = Mathf.RoundToInt(20 + Mathf.Max(0f, 0.15f - age) * 60f);
                var shadow = new GUIStyle(st) { normal = { textColor = new Color(0f, 0f, 0f, c.a * 0.8f) } };
                GUI.Label(new Rect(sp.x - 80 + 1, y - 14 + 1, 160, 28), n.text, shadow);
                GUI.Label(new Rect(sp.x - 80, y - 14, 160, 28), n.text, st);
            }
        }

        /// <summary>A darkened frame round the screen (low health).</summary>
        void Edges(float w, float h, Color c)
        {
            const int bands = 6;
            for (int i = 0; i < bands; i++)
            {
                float t = 1f - i / (float)bands;
                Color k = new Color(c.r, c.g, c.b, c.a * t * t * 0.5f);
                float e = 14f + i * 14f;
                Fill(new Rect(0, 0, w, e), k);
                Fill(new Rect(0, h - e, w, e), k);
                Fill(new Rect(0, e, e, h - 2 * e), k);
                Fill(new Rect(w - e, e, e, h - 2 * e), k);
            }
        }

        // ------------------------------------------------------------------ maps

        Vector2 MapUV(Vector3 world)
        {
            return new Vector2((world.x - mapWorld.x) / mapWorld.width, (world.z - mapWorld.y) / mapWorld.height);
        }

        void DrawMinimap(float w)
        {
            if (mapTexture == null) return;
            const float size = 210f;
            const float span = 260f;   // metres across
            Rect r = new Rect(w - size - 12, 12, size, size);
            Vector3 p = player.transform.position;
            Vector2 uv = MapUV(p);
            float su = span / mapWorld.width, sv = span / mapWorld.height;
            Fill(new Rect(r.x - 3, r.y - 3, size + 6, size + 6), new Color(0f, 0f, 0f, 0.6f));
            GUI.DrawTextureWithTexCoords(r, mapTexture, new Rect(uv.x - su / 2, uv.y - sv / 2, su, sv));
            System.Func<Vector3, Vector2> toScreen = q =>
            {
                Vector2 d = new Vector2(q.x - p.x, q.z - p.z) / span * size;
                return new Vector2(r.center.x + d.x, r.center.y - d.y);
            };
            GUI.BeginClip(r);
            Vector2 off = new Vector2(r.x, r.y);
            DrawWorldCircles(v => toScreen(v) - off, size / span);
            foreach (var s in director.supplyDrops) Marker(toScreen(s) - off, new Color(0.95f, 0.2f, 0.15f), 8f);
            DrawGettingAround(v => toScreen(v) - off, false);
            if (hasWaypoint)
            {
                Vector2 wp = toScreen(waypoint) - off;
                Vector2 c = new Vector2(size / 2, size / 2), dv = wp - c;
                float lim = size / 2 - 8f;
                if (Mathf.Abs(dv.x) > lim || Mathf.Abs(dv.y) > lim) dv *= lim / Mathf.Max(Mathf.Abs(dv.x), Mathf.Abs(dv.y));
                WaypointPin(c + dv);
            }
            GUI.EndClip();
            PlayerArrow(r.center, 18f);
            Label(new Rect(r.x, r.y + size + 4, size, 20), NearestPlace(), new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }

        void DrawFullMap(float w, float h)
        {
            if (mapTexture == null) return;
            float size = Mathf.Min(w, h) - 120f;
            float aspect = mapWorld.width / mapWorld.height;
            float mw = aspect >= 1f ? size : size * aspect;
            float mh = aspect >= 1f ? size / aspect : size;
            Rect r = new Rect((w - mw) / 2, (h - mh) / 2, mw, mh);
            Fill(new Rect(r.x - 6, r.y - 6, mw + 12, mh + 12), new Color(0f, 0f, 0f, 0.8f));
            GUI.DrawTexture(r, mapTexture);
            System.Func<Vector3, Vector2> toScreen = q =>
            {
                Vector2 uv = MapUV(q);
                return new Vector2(r.x + uv.x * mw, r.y + (1f - uv.y) * mh);
            };
            float scale = mw / mapWorld.width;
            MapGrid(r, toScreen);
            if (places != null)
            {
                var nameStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
                for (int i = 0; i < places.Length; i++)
                {
                    var pl = places[i];
                    Vector2 s = toScreen(new Vector3(pl.x, 0f, pl.z));
                    int rating = placeLoot != null && i < placeLoot.Length ? placeLoot[i] : 0;
                    Color dotCol = rating == 2 ? new Color(1f, 0.8f, 0.2f) : rating == 1 ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.6f, 0.6f, 0.6f);
                    for (int k = 0; k <= rating; k++) Marker(new Vector2(s.x - rating * 4f + k * 8f, s.y + 10f), dotCol, 6f);
                    Fill(new Rect(s.x - 75, s.y - 10, 150, 17), new Color(0f, 0f, 0f, 0.35f));
                    Label(new Rect(s.x - 75, s.y - 10, 150, 18), CleanName(pl.name), nameStyle);
                }
            }
            // Click to set a marker, right-click to clear it.
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
            {
                if (ev.button == 0)
                {
                    float u = (ev.mousePosition.x - r.x) / mw, v = 1f - (ev.mousePosition.y - r.y) / mh;
                    float wx = mapWorld.x + u * mapWorld.width, wz = mapWorld.y + v * mapWorld.height;
                    waypoint = new Vector3(wx, HazeZone.GroundY(wx, wz), wz);
                    hasWaypoint = true;
                    SynthAudio.Play2D("equip", 0.4f);
                }
                else hasWaypoint = false;
                ev.Use();
            }
            if (hasWaypoint) WaypointPin(toScreen(waypoint));
            // The helicopter's line while people are still aboard.
            if (director.helicopter != null && director.helicopter.Flying && diver != null && diver.Current == SkyDiver.State.InHelicopter)
            {
                var heli = director.helicopter;
                for (int i = 0; i <= 40; i++)
                    Marker(toScreen(Vector3.Lerp(heli.From, heli.To, i / 40f)), new Color(1f, 1f, 1f, 0.6f), 3f);
                Marker(toScreen(heli.transform.position), new Color(1f, 0.9f, 0.3f), 10f);
            }
            DrawWorldCircles(toScreen, scale);
            foreach (var s in director.supplyDrops) Marker(toScreen(s), new Color(0.95f, 0.2f, 0.15f), 10f);
            DrawGettingAround(toScreen, true);
            PlayerArrow(toScreen(player.transform.position), 20f);
            Label(new Rect(r.x, r.yMax + 6, mw, 24), "M  close map      click  set a marker      right-click  clear it      dots  how much loot is there",
                  new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }

        /// <summary>A 100 m grid with lettered columns and numbered rows, for calling out positions.</summary>
        void MapGrid(Rect r, System.Func<Vector3, Vector2> toScreen)
        {
            var line = new Color(1f, 1f, 1f, 0.12f);
            var tag = new GUIStyle(small) { fontSize = 11, alignment = TextAnchor.UpperLeft, normal = { textColor = new Color(1f, 1f, 1f, 0.6f) } };
            float x0 = Mathf.Ceil(mapWorld.x / 100f) * 100f;
            int col = 0;
            for (float x = x0; x < mapWorld.xMax; x += 100f, col++)
            {
                float sx = toScreen(new Vector3(x, 0f, 0f)).x;
                Fill(new Rect(sx, r.y, 1f, r.height), line);
                if (col < 26) Label(new Rect(sx + 3, r.y + 2, 20, 16), ((char)('A' + col)).ToString(), tag);
            }
            int row = 1;
            for (float z = Mathf.Floor(mapWorld.yMax / 100f) * 100f; z > mapWorld.y; z -= 100f, row++)
            {
                float sy = toScreen(new Vector3(0f, 0f, z)).y;
                Fill(new Rect(r.x, sy, r.width, 1f), line);
                Label(new Rect(r.x + 3, sy + 2, 24, 16), row.ToString(), tag);
            }
        }

        void WaypointPin(Vector2 s)
        {
            Fill(new Rect(s.x - 1.5f, s.y - 14f, 3f, 14f), new Color(0f, 0f, 0f, 0.6f));
            Marker(new Vector2(s.x, s.y - 14f), new Color(0.1f, 0.1f, 0.1f), 13f);
            Marker(new Vector2(s.x, s.y - 14f), new Color(1f, 0.85f, 0.15f), 9f);
        }

        /// <summary>A heading strip under the top bar: N, E, S, W and every 15 degrees, with the marker.</summary>
        void DrawCompass(float w)
        {
            if (fpc == null) return;
            float yaw = fpc.transform.eulerAngles.y;
            const float width = 420f, span = 120f;          // degrees shown across the strip
            Rect r = new Rect(w / 2 - width / 2, 44, width, 22);
            Fill(r, new Color(0f, 0f, 0f, 0.35f));
            var tag = new GUIStyle(small) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            for (int deg = 0; deg < 360; deg += 15)
            {
                float d = Mathf.DeltaAngle(yaw, deg);
                if (Mathf.Abs(d) > span / 2) continue;
                float x = r.center.x + d / span * width;
                bool cardinal = deg % 90 == 0;
                string text = deg == 0 ? "N" : deg == 90 ? "E" : deg == 180 ? "S" : deg == 270 ? "W" : deg % 45 == 0 ? "" : "";
                if (text != "") Label(new Rect(x - 12, r.y + 1, 24, 20), text, new GUIStyle(tag) { fontSize = 13, fontStyle = FontStyle.Bold });
                else Fill(new Rect(x - 0.5f, r.y + (cardinal ? 3 : 13), 1f, cardinal ? 16 : 7), new Color(1f, 1f, 1f, 0.7f));
                if (deg % 45 == 0 && !cardinal) Label(new Rect(x - 15, r.y + 1, 30, 14), deg.ToString(), new GUIStyle(tag) { fontSize = 9 });
            }
            Fill(new Rect(r.center.x - 1, r.y - 3, 2, 6), new Color(1f, 0.85f, 0.2f));
            if (hasWaypoint)
            {
                Vector3 to = waypoint - player.transform.position;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float d = Mathf.Clamp(Mathf.DeltaAngle(yaw, bearing), -span / 2, span / 2);
                Marker(new Vector2(r.center.x + d / span * width, r.yMax + 4), new Color(1f, 0.85f, 0.15f), 8f);
                Label(new Rect(r.center.x + d / span * width - 40, r.yMax + 8, 80, 16),
                      Mathf.RoundToInt(new Vector2(to.x, to.z).magnitude) + " m", new GUIStyle(tag) { fontSize = 10 });
            }
        }

        /// <summary>The marker drawn over the world where it sits, with its distance.</summary>
        void DrawWaypointMarker(float w, float h)
        {
            if (!hasWaypoint || fullMap) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(waypoint + Vector3.up * 2f);
            if (sp.z <= 0f) return;
            Vector2 s = new Vector2(sp.x, h - sp.y);
            WaypointPin(new Vector2(s.x, s.y + 14f));
            float dist = new Vector2(waypoint.x - player.transform.position.x, waypoint.z - player.transform.position.z).magnitude;
            Label(new Rect(s.x - 40, s.y + 18, 80, 18), Mathf.RoundToInt(dist) + " m", new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontSize = 11 });
        }

        /// <summary>The place name, large, for a few seconds after walking or landing into it.</summary>
        void DrawArea(float w, float h)
        {
            float age = Time.time - areaTime;
            if (areaIndex < 0 || age > 3.5f || places == null) return;
            float a = Mathf.Clamp01(age * 3f) * Mathf.Clamp01(3.5f - age);
            GUI.color = new Color(1f, 1f, 1f, a);
            Label(new Rect(0, h * 0.12f, w, 34), CleanName(places[areaIndex].name), new GUIStyle(big) { alignment = TextAnchor.MiddleCenter });
            int rating = placeLoot != null && areaIndex < placeLoot.Length ? placeLoot[areaIndex] : 0;
            Label(new Rect(0, h * 0.12f + 34, w, 22), LootWords[Mathf.Clamp(rating, 0, 2)], new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
        }

        /// <summary>Zip lines as dotted orange lines (towers as squares); vehicles within 150 m as small marks.</summary>
        void DrawGettingAround(System.Func<Vector3, Vector2> toScreen, bool full)
        {
            var zipCol = new Color(1f, 0.6f, 0.15f, 0.9f);
            foreach (var z in ZipLine.All)
            {
                Vector2 a = toScreen(z.a), b = toScreen(z.b);
                int n = Mathf.Clamp(Mathf.RoundToInt((b - a).magnitude / 5f), 4, 60);
                for (int i = 1; i < n; i++) Marker(Vector2.Lerp(a, b, i / (float)n), zipCol, 2.5f);
                GUI.color = zipCol;
                GUI.DrawTexture(new Rect(a.x - 3, a.y - 3, 6, 6), white);
                GUI.DrawTexture(new Rect(b.x - 3, b.y - 3, 6, 6), white);
                GUI.color = Color.white;
            }
            Vector3 me = player.transform.position;
            foreach (var v in Vehicle.All)
            {
                if (v.Wrecked || (mover != null && mover.Riding == v)) continue;
                if (!full && (v.transform.position - me).sqrMagnitude > 150f * 150f) continue;
                if (full && (v.transform.position - me).sqrMagnitude > 300f * 300f) continue;
                Vector2 s = toScreen(v.transform.position);
                GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
                GUI.DrawTexture(new Rect(s.x - 4, s.y - 3, 8, 6), white);
                GUI.color = new Color(0.55f, 0.85f, 1f, 1f);
                GUI.DrawTexture(new Rect(s.x - 3, s.y - 2, 6, 4), white);
                GUI.color = Color.white;
            }
        }

        /// <summary>The vehicle panel while riding: health bar and speed.</summary>
        void DrawVehicle(float w, float h)
        {
            if (mover == null || mover.Riding == null) return;
            var v = mover.Riding;
            Rect r = new Rect(w / 2 - 130, h - 150, 260, 40);
            Fill(r, new Color(0f, 0f, 0f, 0.5f));
            float f = Mathf.Clamp01(v.Health / v.maxHealth);
            Color c = f > 0.5f ? new Color(0.85f, 0.85f, 0.85f) : f > 0.25f ? new Color(1f, 0.7f, 0.2f) : new Color(1f, 0.25f, 0.15f);
            Fill(new Rect(r.x + 8, r.y + 28, r.width - 16, 6), new Color(1f, 1f, 1f, 0.15f));
            Fill(new Rect(r.x + 8, r.y + 28, (r.width - 16) * f, 6), c);
            Label(new Rect(r.x + 8, r.y + 4, 160, 22), v.DisplayName.Substring(0, 1).ToUpper() + v.DisplayName.Substring(1), small);
            Label(new Rect(r.x + 100, r.y + 4, r.width - 108, 22), Mathf.RoundToInt(Mathf.Abs(v.Speed) * 3.6f) + " km/h",
                  new GUIStyle(small) { alignment = TextAnchor.UpperRight });
        }

        void DrawWorldCircles(System.Func<Vector3, Vector2> toScreen, float pixelsPerMetre)
        {
            if (haze == null) return;
            Circle(toScreen, haze.Centre, haze.Radius, new Color(0.85f, 0.45f, 0.15f, 0.95f), pixelsPerMetre);
            if (haze.Running && haze.RoundIndex < haze.rounds.Length)
                Circle(toScreen, haze.NextCentre, haze.NextRadius, new Color(1f, 1f, 1f, 0.95f), pixelsPerMetre);
        }

        void Circle(System.Func<Vector3, Vector2> toScreen, Vector2 c, float radius, Color col, float ppm)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(radius * ppm * 0.6f), 24, 160);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                Vector2 s = toScreen(new Vector3(c.x + Mathf.Cos(a) * radius, 0f, c.y + Mathf.Sin(a) * radius));
                GUI.color = col;
                GUI.DrawTexture(new Rect(s.x - 1.5f, s.y - 1.5f, 3f, 3f), white);
            }
            GUI.color = Color.white;
        }

        void Marker(Vector2 s, Color c, float size)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(s.x - size / 2, s.y - size / 2, size, size), dot);
            GUI.color = Color.white;
        }

        void PlayerArrow(Vector2 at, float size)
        {
            var m = GUI.matrix;
            float yaw = fpc != null ? fpc.transform.eulerAngles.y : player.transform.eulerAngles.y;
            GUIUtility.RotateAroundPivot(yaw, at);
            GUI.color = new Color(1f, 0.85f, 0.2f);
            GUI.DrawTexture(new Rect(at.x - size / 2, at.y - size / 2, size, size), arrow);
            GUI.color = Color.white;
            GUI.matrix = m;
        }

        string NearestPlace()
        {
            if (places == null || places.Length == 0) return "";
            Vector3 p = player.transform.position;
            float best = float.MaxValue;
            string name = "";
            foreach (var pl in places)
            {
                float d = (pl.x - p.x) * (pl.x - p.x) + (pl.z - p.z) * (pl.z - p.z);
                if (d < best) { best = d; name = CleanName(pl.name); }
            }
            return name;
        }

        // ------------------------------------------------------------------ overlays

        void DrawScope(float w, float h)
        {
            float s = h * 0.95f;
            Rect r = new Rect((w - s) / 2, (h - s) / 2, s, s);
            GUI.DrawTexture(r, scope);
            Fill(new Rect(0, 0, r.x + 1, h), Color.black);
            Fill(new Rect(r.xMax - 1, 0, w - r.xMax + 1, h), Color.black);
            Color line = new Color(0f, 0f, 0f, 0.9f);
            Fill(new Rect(r.x, h / 2 - 0.5f, s, 1f), line);
            Fill(new Rect(w / 2 - 0.5f, r.y, 1f, s), line);
            Fill(new Rect(w / 2 - 40, h / 2 - 2, 80, 4), line);
            Fill(new Rect(w / 2 - 2, h / 2, 4, 60), line);
        }

        void HitMark(float cx, float cy, Color c)
        {
            var m = GUI.matrix;
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
            {
                GUI.matrix = m;
                GUIUtility.RotateAroundPivot(a, new Vector2(cx, cy));
                Fill(new Rect(cx + 7, cy - 1, 9, 2), c);
            }
            GUI.matrix = m;
        }

        void DrawPause(float w, float h)
        {
            Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.55f));
            Label(new Rect(0, h * 0.25f, w, 60), "Paused", huge);
            float bw = 260;
            if (GUI.Button(new Rect(w / 2 - bw / 2, h * 0.42f, bw, 40), "Resume")) SetPaused(false);
            if (GUI.Button(new Rect(w / 2 - bw / 2, h * 0.42f + 50, bw, 40), "New match")) { SetPaused(false); director.Restart(); }
            if (GUI.Button(new Rect(w / 2 - bw / 2, h * 0.42f + 100, bw, 40), "Quit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            DifficultyPicker(w, h * 0.42f + 150);
            Label(new Rect(0, h * 0.42f + 230, w, 160),
                "WASD move   Shift sprint   Space jump   C or Ctrl crouch   mouse aim   left click fire   right click aim down sights\n" +
                "1-3 guns   R reload   E pick up   X drop gun   Q bandage or medkit   Z chai flask   G grenade   4 smoke\n" +
                "V shoulder or first-person camera   M map   F torch   F3 frame time   Esc pause", new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }

        /// <summary>Three buttons for the bot difficulty; the choice applies from the next match.</summary>
        void DifficultyPicker(float w, float y)
        {
            var current = BotDifficulty.Current;
            float bw = 120, gap = 8, total = bw * 3 + gap * 2;
            Label(new Rect(0, y, w, 22), "Bots", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            for (int i = 0; i < 3; i++)
            {
                var l = (BotDifficulty.Level)i;
                var r = new Rect(w / 2 - total / 2 + i * (bw + gap), y + 24, bw, 30);
                string text = l == current ? "[ " + BotDifficulty.Name(l) + " ]" : BotDifficulty.Name(l);
                if (GUI.Button(r, text)) BotDifficulty.Current = l;
            }
            string note = BotDifficulty.Describe(current);
            if (director != null && current != director.Difficulty) note += " (from the next match)";
            Label(new Rect(0, y + 58, w, 20), note, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawResults(float w, float h)
        {
            Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.65f));
            bool won = director.PlayerWon;
            Label(new Rect(0, h * 0.2f, w, 70), won ? "TOP OF THE HILL" : "#" + player.Placement + " of " + director.StartCount, huge);
            string sub = won ? "Last one standing on the Jagti campus." :
                         player.LastAttacker != null ? "Taken out by " + player.LastAttacker.displayName : "Lost in the haze";
            Label(new Rect(0, h * 0.2f + 70, w, 30), sub, new GUIStyle(big) { alignment = TextAnchor.MiddleCenter });
            string stats = string.Format("Kills  {0}        Damage  {1:0}        Survived  {2}",
                player.Kills, player.DamageDealt, Clock(won ? director.MatchTime : player.DiedAt - director.StartTime));
            Label(new Rect(0, h * 0.2f + 120, w, 30), stats, new GUIStyle(normal) { alignment = TextAnchor.MiddleCenter });
            float bw = 260;
            if (GUI.Button(new Rect(w / 2 - bw / 2, h * 0.55f, bw, 44), "Play again (Enter)")) director.Restart();
            DifficultyPicker(w, h * 0.55f + 104);
            if (GUI.Button(new Rect(w / 2 - bw / 2, h * 0.55f + 54, bw, 40), "Quit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        // ------------------------------------------------------------------ helpers

        static string Clock(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            return string.Format("{0}:{1:00}", (int)(seconds / 60f), (int)(seconds % 60f));
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void Label(Rect r, string text, GUIStyle style)
        {
            var s = new Rect(r.x + 1, r.y + 1, r.width, r.height);
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f * GUI.color.a);
            GUI.Label(s, text, style);
            style.normal.textColor = old;
            GUI.Label(r, text, style);
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static Texture2D Disc(int size, float hole)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude / r;
                    float a = Mathf.Clamp01((1f - d) * r) * (hole > 0f ? Mathf.Clamp01((d - hole) * r) : 1f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        // An arrow pointing up (screen), drawn white so GUI.color tints it.
        static Texture2D Arrow(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f;
                    float v = (y + 0.5f) / size;          // 0 bottom, 1 top
                    float half = (1f - v) * 0.45f;        // wide at the bottom, a point at the top
                    bool notch = v < 0.3f && Mathf.Abs(u) < (0.3f - v) * 0.9f;
                    float a = Mathf.Abs(u) <= half && !notch ? 1f : 0f;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static Texture2D ScopeMask(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude / r;
                    float a = Mathf.Clamp01((d - 0.93f) * 20f);
                    float vignette = Mathf.Clamp01((d - 0.7f) * 1.2f) * 0.5f;
                    t.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.Max(a, vignette)));
                }
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }
    }
}
