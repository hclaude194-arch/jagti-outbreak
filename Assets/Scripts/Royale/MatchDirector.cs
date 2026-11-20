using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Jagti.Royale
{
    /// <summary>
    /// The match referee. At the start it lays out the loot, picks the sky bus's line over the
    /// campus, creates the bots and gives each a landing spot, and starts the haze. During the match
    /// it keeps the kill feed and placements, sends supply drops, and ends the match when one player
    /// is left (or when the player falls). The match waits for the front menu (NetLobbyMenu): offline
    /// it begins at once, online the host begins it and sends the seed and the list of players to the
    /// others, whose machines lay out the same loot, flight and haze and show everyone else as puppets.
    /// </summary>
    public class MatchDirector : MonoBehaviour
    {
        public static MatchDirector Instance { get; private set; }

        public class FeedLine
        {
            public string text;
            public float time;
            public bool involvesPlayer;
            // The parts, for the HUD's kill feed: killer is null when nobody got the kill.
            public string killer, victim, weapon;
            public bool head, byPlayer, ofPlayer;
        }

        [Header("Scene parts (set by RoyaleBuilder)")]
        public Combatant player;
        public SkyDiver playerDiver;
        public SkyBus helicopter;
        public HazeZone haze;
        public LootDirector loot;

        [Header("Match")]
        public int botCount = 29;
        public float helicopterHeight = 300f;
        public float helicopterSpeed = 42f;
        public int seed;                      // 0 = a new match every time

        [Header("Bot looks (set by RoyaleBuilder)")]
        public Material[] skins;
        public Material[] shirts;
        public Material[] pants;
        public Material gearMaterial;
        public Material gunMaterial;
        public Material[] canopies;
        public GameObject grenadeTemplate;
        public GameObject smokeTemplate;   // the player's inactive grenade template, shared with bots

        [Header("Supply drop looks")]
        public Material crateMaterial;
        public Material chuteMaterial;
        public Material smokeMaterial;

        public readonly List<FeedLine> feed = new List<FeedLine>();
        public bool Over { get; private set; }
        /// <summary>The bot difficulty this match was started with.</summary>
        public BotDifficulty.Level Difficulty { get; private set; }
        public bool PlayerWon { get; private set; }
        public float MatchTime { get; private set; }
        public float StartTime { get; private set; }
        public int StartCount { get; private set; }
        public string Banner { get; private set; }
        public float BannerTime { get; private set; }
        public readonly List<Vector3> supplyDrops = new List<Vector3>();
        /// <summary>False until the front menu starts the match.</summary>
        public bool Begun { get; private set; }
        /// <summary>Everyone in the match (filled at the start; online it is what the host sends).</summary>
        public readonly List<Entrant> roster = new List<Entrant>();
        /// <summary>The host sent a supply drop (position, seed); NetMatch passes it on.</summary>
        public System.Action<Vector3, int> SupplyDropSent;
        /// <summary>Set by Restart offline, so "Play again" goes straight into the next match.</summary>
        public static bool SkipMenu;

        readonly List<BotBrain> bots = new List<BotBrain>();
        int dropsSent;
        float overTimer = -1f;
        System.Random rng;

        static readonly string[] FirstNames =
        {
            "Aarav", "Ishaan", "Kabir", "Vihaan", "Rohan", "Arjun", "Dev", "Nikhil", "Sahil", "Kunal",
            "Ananya", "Diya", "Meera", "Saanvi", "Ira", "Kavya", "Nisha", "Riya", "Tara", "Zoya",
            "Aditya", "Yash", "Pranav", "Rahul", "Tanvi", "Aditi", "Simran", "Harleen", "Omar", "Faiz",
            "Manav", "Varun", "Neha", "Pooja", "Gauri", "Jasleen", "Rehan", "Siddharth", "Ayaan", "Mehak",
        };

        static readonly string[] Tags = { "PGP", "IPM", "EPGP", "MBA", "Batch", "Mess", "Hostel", "Tawi", "Chenab", "Jagti" };

        void Awake()
        {
            Instance = this;
            GameState.Reset();
            GameState.HudActive = true;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            StartTime = Time.time;
            GameState.Paused = true;   // until the menu starts the match
            var menu = gameObject.AddComponent<NetLobbyMenu>();
            menu.director = this;
        }

        /// <summary>A match against bots on this machine alone.</summary>
        public void BeginOffline()
        {
            int s = seed != 0 ? seed : System.Environment.TickCount;
            Begin(s, null, 1, true, BotDifficulty.Current);
        }

        /// <summary>Online host: starts with these people (looks are filled in) plus bots; roster then lists everyone.</summary>
        public void BeginHost(int s, List<Entrant> humans)
        {
            Begin(s, humans, 1, true, BotDifficulty.Current);
        }

        /// <summary>Online client: the host's seed and roster; everyone but this player is a puppet.</summary>
        public void BeginClient(int s, List<Entrant> everyone, int myId, BotDifficulty.Level level)
        {
            Begin(s, everyone, myId, false, level);
        }

        void Begin(int s, List<Entrant> given, int myId, bool makeBots, BotDifficulty.Level level)
        {
            if (Begun) return;
            Begun = true;
            GameState.Paused = false;
            GameState.SetCursorLocked(true);
            StartTime = Time.time;
            Difficulty = level;
            rng = new System.Random(s);
            if (loot != null) loot.SpawnAll(s);
            PlanFlight();   // first use of rng, so every machine flies the same line

            roster.Clear();
            if (given != null) roster.AddRange(given);
            // Scooters and carts aren't kept in step online yet, so online matches go without them.
            if (given != null)
                foreach (var v in Vehicle.All.ToArray()) if (v != null) Destroy(v.gameObject);
            if (player != null)
            {
                player.netId = myId;
                player.owned = true;
            }
            var root = new GameObject("Bots").transform;
            if (makeBots)
            {
                foreach (var e in roster) RollLook(e);
                CreateBots(root, Mathf.Max(0, botCount + 1 - Mathf.Max(1, roster.Count)));
            }
            foreach (var e in roster)
            {
                if (e.id == myId)
                {
                    if (given != null && playerDiver != null)
                        playerDiver.Redress(Look(skins, e.skin), Look(shirts, e.shirt), Look(pants, e.pants), Look(canopies, e.canopy));
                    continue;
                }
                if (makeBots && !e.human) continue;   // made by CreateBots
                MakePuppet(root, e);
            }
            StartCount = Combatant.All.Count;
            if (playerDiver != null) playerDiver.Board();
            if (haze != null) haze.Begin(s + 7);
            Announce("Board the sky bus. Press Space to jump.");
        }

        // ------------------------------------------------------------------ setup

        void PlanFlight()
        {
            if (helicopter == null || haze == null) return;
            Vector2 c = haze.startCentre;
            float r = haze.startRadius;
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 side = new Vector2(-dir.y, dir.x) * ((float)rng.NextDouble() - 0.5f) * r * 0.6f;
            Vector2 a = c + side - dir * (r + 200f);
            Vector2 b = c + side + dir * (r + 200f);
            // Fly well above the highest ground on the line.
            float top = HazeZone.GroundY(c.x, c.y);
            for (int i = 0; i <= 20; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / 20f);
                top = Mathf.Max(top, HazeZone.GroundY(p.x, p.y));
            }
            float y = Mathf.Max(HazeZone.GroundY(c.x, c.y) + helicopterHeight, top + 160f);
            helicopter.Fly(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), helicopterSpeed);
        }

        void RollLook(Entrant e)
        {
            e.skin = Roll(skins);
            e.shirt = Roll(shirts);
            e.pants = Roll(pants);
            e.canopy = Roll(canopies);
        }

        int Roll(Material[] list)
        {
            return list != null && list.Length > 0 ? rng.Next(list.Length) : 0;
        }

        static Material Look(Material[] list, int i)
        {
            return list != null && list.Length > 0 ? list[Mathf.Clamp(i, 0, list.Length - 1)] : null;
        }

        void CreateBots(Transform root, int count)
        {
            var tune = BotDifficulty.Get(Difficulty);
            var used = new HashSet<string>();
            foreach (var e in roster) used.Add(e.name);
            var targets = new List<Vector3>();
            for (int i = 0; i < count; i++)
            {
                string name;
                int guard = 0;
                do
                {
                    name = FirstNames[rng.Next(FirstNames.Length)] + "_" + Tags[rng.Next(Tags.Length)] + rng.Next(10, 99);
                } while (used.Contains(name) && ++guard < 20);
                used.Add(name);

                Vector3 target = PickLanding(targets);
                targets.Add(target);
                var e = new Entrant { id = 100 + i, name = name, human = false };
                RollLook(e);
                roster.Add(e);
                var bot = MakeBot(root, e);
                float jumpAt = helicopter != null ? Mathf.Max(3f, helicopter.TimeClosestTo(target) - (float)rng.NextDouble() * 6f) : 0f;
                bot.tune = tune;
                bot.skill = Mathf.Lerp(tune.skillMin, tune.skillMax, (float)rng.NextDouble());
                bot.Board(helicopter, haze, target, jumpAt);
                bots.Add(bot);
            }
        }

        /// <summary>A landing spot near loot, not too close to the line's far ends, spread out from the others.</summary>
        Vector3 PickLanding(List<Vector3> taken)
        {
            var pts = loot != null ? loot.points : null;
            Vector3 fallback = haze != null ? new Vector3(haze.startCentre.x, 0f, haze.startCentre.y) : Vector3.zero;
            fallback.y = HazeZone.GroundY(fallback.x, fallback.z);
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector3 p = pts != null && pts.Length > 0 ? pts[rng.Next(pts.Length)] : fallback;
                p.y = HazeZone.GroundY(p.x, p.z);
                // Prefer spots the sky bus passes close to (fewer long glides).
                if (helicopter != null)
                {
                    Vector3 near = helicopter.PointAt(helicopter.TimeClosestTo(p));
                    float off = new Vector2(near.x - p.x, near.z - p.z).magnitude;
                    if (off > 380f && attempt < 20) continue;
                }
                bool crowded = false;
                foreach (var t in taken)
                    if ((t - p).sqrMagnitude < 35f * 35f && attempt < 25) { crowded = true; break; }
                if (crowded) continue;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(p, out hit, 12f, NavMesh.AllAreas)) continue;
                // Land in the open (bots glide straight down and must not pass through a roof). Look down
                // from high above: a ray cast upward from inside a building misses one-sided roof meshes.
                RaycastHit top;
                if (Physics.Raycast(hit.position + Vector3.up * 150f, Vector3.down, out top, 160f, ~0, QueryTriggerInteraction.Ignore)
                    && top.point.y > hit.position.y + 1.5f && attempt < 28) continue;
                return hit.position;
            }
            return fallback;
        }

        BotBrain MakeBot(Transform parent, Entrant e)
        {
            HumanBody body;
            var go = MakeFigure(parent, "Bot - " + e.name, e, out body);

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.32f;
            agent.height = 1.8f;
            agent.speed = 4.2f;
            agent.angularSpeed = 540f;
            agent.acceleration = 14f;
            agent.stoppingDistance = 0.4f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.enabled = false;

            var brain = go.AddComponent<BotBrain>();
            brain.body = body;
            go.SetActive(true);
            return brain;
        }

        /// <summary>Someone whose machine is elsewhere: moved and posed by what that machine sends.</summary>
        void MakePuppet(Transform parent, Entrant e)
        {
            HumanBody body;
            var go = MakeFigure(parent, (e.human ? "Player - " : "Bot - ") + e.name, e, out body);
            go.GetComponent<Combatant>().owned = false;
            var puppet = go.AddComponent<NetPuppet>();
            puppet.body = body;
            go.SetActive(true);
        }

        /// <summary>The parts bots and puppets share: health, name, eye, hit areas and a body. Left inactive.</summary>
        GameObject MakeFigure(Transform parent, string objectName, Entrant e, out HumanBody body)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.SetActive(false);   // finish setting up before Awake/OnEnable run
            var health = go.AddComponent<Health>();
            health.max = 100f;
            var c = go.AddComponent<Combatant>();
            c.displayName = e.name;
            c.netId = e.id;

            var eye = new GameObject("Eye").transform;
            eye.SetParent(go.transform, false);
            eye.localPosition = new Vector3(0f, 1.62f, 0f);
            c.eye = eye;

            // Hit areas: the body and a smaller head (headshots do more).
            var bodyBox = go.AddComponent<BoxCollider>();
            bodyBox.center = new Vector3(0f, 0.8f, 0f);
            bodyBox.size = new Vector3(0.5f, 1.45f, 0.32f);
            var hb = go.AddComponent<Hitbox>();
            hb.owner = health;
            var headGo = new GameObject("Head Hitbox");
            headGo.transform.SetParent(go.transform, false);
            headGo.transform.localPosition = new Vector3(0f, 1.72f, 0f);
            var headCol = headGo.AddComponent<SphereCollider>();
            headCol.radius = 0.14f;
            var hh = headGo.AddComponent<Hitbox>();
            hh.owner = health;
            hh.isHead = true;

            body = HumanBody.Build(go.transform, Look(skins, e.skin), Look(shirts, e.shirt), Look(pants, e.pants), gearMaterial, gunMaterial, Look(canopies, e.canopy));
            return go;
        }

        // ------------------------------------------------------------------ during the match

        void Update()
        {
            if (Begun && !Over) MatchTime += Time.deltaTime;

            // Supply drops at the start of the second and fourth rounds (online the host sends them).
            if (haze != null && haze.Running && !NetSession.IsClient && dropsSent < 2)
            {
                if ((dropsSent == 0 && haze.RoundIndex >= 1) || (dropsSent == 1 && haze.RoundIndex >= 3))
                {
                    dropsSent++;
                    SendSupplyDrop();
                }
            }

            if (overTimer >= 0f)
            {
                overTimer -= Time.unscaledDeltaTime;
                if (overTimer < 0f) ShowResults();
            }
        }

        void SendSupplyDrop()
        {
            for (int i = 0; i < 20; i++)
            {
                Vector2 r = haze.NextCentre + Random.insideUnitCircle * haze.NextRadius * 0.7f;
                Vector3 p = new Vector3(r.x, HazeZone.GroundY(r.x, r.y), r.y);
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(p, out hit, 10f, NavMesh.AllAreas)) continue;
                // Keep it out in the open, not inside a building.
                if (Physics.Raycast(hit.position + Vector3.up * 0.5f, Vector3.up, 60f)) continue;
                int dropSeed = Random.Range(1, int.MaxValue);
                ArriveSupplyDrop(hit.position, dropSeed);
                if (SupplyDropSent != null) SupplyDropSent(hit.position, dropSeed);
                return;
            }
        }

        /// <summary>A supply drop on its way down here (the host's own, or one it sent).</summary>
        public void ArriveSupplyDrop(Vector3 at, int dropSeed)
        {
            SupplyDrop.Create(at, 230f, crateMaterial, chuteMaterial, smokeMaterial, dropSeed, supplyDrops.Count);
            supplyDrops.Add(at);
            Announce("Supply drop incoming. Watch for red smoke.");
            SynthAudio.Play2D("alert", 0.7f);
        }

        public void Announce(string text)
        {
            Banner = text;
            BannerTime = Time.time;
        }

        public void OnPlayerJumped()
        {
            Announce("Glide with the mouse. Space opens the glider early.");
        }

        public void OnCombatantDied(Combatant victim, Combatant killer, string weapon, bool head)
        {
            int alive = Combatant.AliveCount();
            victim.Placement = alive + 1;
            string text;
            if (killer != null && killer != victim)
            {
                killer.Kills++;
                text = killer.displayName + "  [" + weapon + (head ? ", head" : "") + "]  " + victim.displayName;
            }
            else if (weapon == "the haze") text = victim.displayName + " was lost in the haze";
            else if (weapon == NetMatch.LeftWeapon) text = victim.displayName + " left the match";
            else text = victim.displayName + " is out";
            bool mine = victim.isPlayer || (killer != null && killer.isPlayer);
            bool credited = killer != null && killer != victim;
            feed.Add(new FeedLine
            {
                text = text, time = Time.time, involvesPlayer = mine,
                killer = credited ? killer.displayName : null, victim = victim.displayName, weapon = weapon, head = head,
                byPlayer = credited && killer.isPlayer, ofPlayer = victim.isPlayer,
            });
            if (feed.Count > 30) feed.RemoveAt(0);
            if (killer != null && killer.isPlayer) GameState.Kills = killer.Kills;

            if (victim.isPlayer)
            {
                GameState.PlayerDead = true;
                // The death camera lifts out of the head and turns to the killer; without a camera, drop the view to the ground.
                var walker = victim.GetComponent<Jagti.Player.FirstPersonController>();
                if (SpectatorCam.Begin(victim, killer) == null && walker != null) walker.Collapse();
                if (LootDirector.Instance != null) LootDirector.Instance.DropLoadout(victim.loadout, victim.transform.position);
                EndMatch(false);
                return;
            }
            if (alive <= 1 && player != null && player.Alive)
            {
                player.Placement = 1;
                EndMatch(true);
            }
            else if (alive <= 1)
            {
                // The player is already out; credit the last bot standing.
                foreach (var c in Combatant.All) if (c.Alive) c.Placement = 1;
            }
        }

        void EndMatch(bool won)
        {
            if (Over) return;
            Over = true;
            PlayerWon = won;
            overTimer = won ? 2.5f : 4.5f;   // time for the death camera to show who did it
            if (won)
            {
                Announce("Top of the hill!");
                SynthAudio.Play2D("victory", 0.9f);
            }
        }

        void ShowResults()
        {
            GameState.SetCursorLocked(false);
            var hud = RoyaleHud.Instance;
            if (hud != null) hud.ShowResults();
        }

        /// <summary>
        /// Play again. Offline: a new match straight away. Online only the host can, and it takes
        /// everyone back to the lobby for the next match.
        /// </summary>
        public void Restart()
        {
            if (NetSession.IsClient) return;
            if (NetSession.IsHost)
            {
                NetSession.Instance.BackToLobby();
                return;
            }
            SkipMenu = true;
            Reload();
        }

        /// <summary>Back to the front menu (offline), or out of an online game.</summary>
        public void ToMenu()
        {
            if (NetSession.Online) NetSession.Instance.Leave(null);
            SkipMenu = false;
            Reload();
        }

        public static void Reload()
        {
            // Fade out, load the campus behind a loading screen, fade back in.
            LoadingScreen.Load(SceneManager.GetActiveScene().buildIndex, SkipMenu ? "NEXT MATCH" : "JAGTI ROYALE");
        }
    }
}
