using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Jagti.Royale
{
    /// <summary>
    /// The match referee. At the start it lays out the loot, picks the helicopter's line over the
    /// campus, creates the bots and gives each a landing spot, and starts the haze. During the match
    /// it keeps the kill feed and placements, sends supply drops, and ends the match when one player
    /// is left (or when the player falls). Everything that decides the outcome goes through here, so
    /// online play can later run it on a host.
    /// </summary>
    public class MatchDirector : MonoBehaviour
    {
        public static MatchDirector Instance { get; private set; }

        public class FeedLine
        {
            public string text;
            public float time;
            public bool involvesPlayer;
        }

        [Header("Scene parts (set by RoyaleBuilder)")]
        public Combatant player;
        public SkyDiver playerDiver;
        public Helicopter helicopter;
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

        [Header("Supply drop looks")]
        public Material crateMaterial;
        public Material chuteMaterial;
        public Material smokeMaterial;

        public readonly List<FeedLine> feed = new List<FeedLine>();
        public bool Over { get; private set; }
        public bool PlayerWon { get; private set; }
        public float MatchTime { get; private set; }
        public float StartTime { get; private set; }
        public int StartCount { get; private set; }
        public string Banner { get; private set; }
        public float BannerTime { get; private set; }
        public readonly List<Vector3> supplyDrops = new List<Vector3>();

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
            int s = seed != 0 ? seed : System.Environment.TickCount;
            rng = new System.Random(s);
            if (loot != null) loot.SpawnAll(s);

            PlanFlight();
            CreateBots();
            StartCount = Combatant.All.Count;
            if (playerDiver != null) playerDiver.Board();
            if (haze != null) haze.Begin(s + 7);
            Announce("Board the helicopter. Press Space to jump.");
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

        void CreateBots()
        {
            var used = new HashSet<string>();
            var targets = new List<Vector3>();
            var root = new GameObject("Bots").transform;
            for (int i = 0; i < botCount; i++)
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
                var bot = MakeBot(root, name, i);
                float jumpAt = helicopter != null ? Mathf.Max(3f, helicopter.TimeClosestTo(target) - (float)rng.NextDouble() * 6f) : 0f;
                bot.skill = Mathf.Lerp(0.2f, 0.85f, (float)rng.NextDouble());
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
                // Prefer spots the helicopter passes close to (fewer long glides).
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
                // Land in the open (bots glide straight down and must not pass through a roof).
                if (Physics.Raycast(hit.position + Vector3.up, Vector3.up, 120f) && attempt < 28) continue;
                return hit.position;
            }
            return fallback;
        }

        BotBrain MakeBot(Transform parent, string name, int index)
        {
            var go = new GameObject("Bot - " + name);
            go.transform.SetParent(parent, false);
            go.SetActive(false);   // finish setting up before Awake/OnEnable run
            var health = go.AddComponent<Health>();
            health.max = 100f;
            var c = go.AddComponent<Combatant>();
            c.displayName = name;

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

            var brain = go.AddComponent<BotBrain>();
            brain.body = HumanBody.Build(go.transform, Pick(skins), Pick(shirts), Pick(pants), gearMaterial, gunMaterial, Pick(canopies));
            go.SetActive(true);
            return brain;
        }

        Material Pick(Material[] list)
        {
            return list != null && list.Length > 0 ? list[rng.Next(list.Length)] : null;
        }

        // ------------------------------------------------------------------ during the match

        void Update()
        {
            if (!GameState.Paused && !Over) MatchTime += Time.deltaTime;

            // Supply drops at the start of the second and fourth rounds.
            if (haze != null && haze.Running && !Over)
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
                SupplyDrop.Create(hit.position, 230f, crateMaterial, chuteMaterial, smokeMaterial);
                supplyDrops.Add(hit.position);
                Announce("Supply drop incoming. Watch for red smoke.");
                SynthAudio.Play2D("alert", 0.7f);
                return;
            }
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
            else text = victim.displayName + " is out";
            bool mine = victim.isPlayer || (killer != null && killer.isPlayer);
            feed.Add(new FeedLine { text = text, time = Time.time, involvesPlayer = mine });
            if (feed.Count > 30) feed.RemoveAt(0);
            if (killer != null && killer.isPlayer) GameState.Kills = killer.Kills;

            if (victim.isPlayer)
            {
                GameState.PlayerDead = true;
                var walker = victim.GetComponent<Jagti.Player.FirstPersonController>();
                if (walker != null) walker.Collapse();
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
            overTimer = won ? 2f : 3f;
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

        public void Restart()
        {
            Time.timeScale = 1f;
            Combatant.All.Clear();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
