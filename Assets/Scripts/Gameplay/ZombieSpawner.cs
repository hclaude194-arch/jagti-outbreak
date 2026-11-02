using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Jagti.Gameplay
{
    /// <summary>
    /// Keeps a pool of zombies alive in a ring around the player: spawns out of sight on the NavMesh
    /// (never on a roof), retires ones left far behind, and slowly raises the count the longer you survive.
    /// </summary>
    public class ZombieSpawner : MonoBehaviour
    {
        public Transform player;

        [Header("Count")]
        public int startActive = 18;
        public int maxActive = 30;
        public float secondsPerExtra = 45f;   // one more zombie allowed every this many seconds

        [Header("Distances in metres")]
        public float spawnMin = 38f;
        public float spawnMax = 95f;
        public float despawnDistance = 150f;
        public float safeRadiusAtStart = 60f;

        [Header("Zombie")]
        public float health = 80f;
        [Range(0f, 1f)] public float runnerChance = 0.15f;

        [Header("Looks (set by the builder)")]
        public Material[] skins;
        public Material[] shirts;
        public Material[] pants;

        readonly List<ZombieAI> active = new List<ZombieAI>();
        readonly Stack<ZombieAI> pool = new Stack<ZombieAI>();
        GameObject template;
        float spawnTimer;
        Camera cam;
        bool hasNavMesh;

        public int ActiveCount { get { return active.Count; } }

        void Start()
        {
            if (player == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p != null) player = p.transform;
            }
            cam = Camera.main;
            NavMeshHit hit;
            hasNavMesh = player != null && NavMesh.SamplePosition(player.position, out hit, 50f, NavMesh.AllAreas);
            if (!hasNavMesh) Debug.LogWarning("[Jagti] No NavMesh near the player; zombies will walk straight over the terrain.");
            template = BuildTemplate();
            // Fill most of the starting horde straight away, away from the spawn point.
            for (int i = 0; i < startActive * 2 && active.Count < startActive; i++) TrySpawn(true);
        }

        GameObject BuildTemplate()
        {
            var go = new GameObject("Zombie Template");
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 1.45f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0.725f, 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.acceleration = 10f;
            agent.angularSpeed = 300f;
            agent.stoppingDistance = 1.1f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.enabled = false;

            go.AddComponent<Health>();

            var voice = go.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 2f;
            voice.maxDistance = 35f;
            voice.playOnAwake = false;
            voice.dopplerLevel = 0f;

            var body = ZombieBody.Build(go.transform, Pick(skins), Pick(shirts), Pick(pants));

            // The head is its own hitbox so headshots count extra; it rides on the animated head.
            var headCol = body.head.gameObject.AddComponent<SphereCollider>();
            headCol.radius = 0.15f;
            headCol.center = new Vector3(0f, 0.13f, 0.01f);
            var hb = body.head.gameObject.AddComponent<Hitbox>();
            hb.multiplier = 2.5f;
            hb.isHead = true;

            go.AddComponent<ZombieAI>();
            return go;
        }

        static Material Pick(Material[] list)
        {
            if (list == null || list.Length == 0) return null;
            return list[Random.Range(0, list.Length)];
        }

        void Update()
        {
            if (player == null || GameState.PlayerDead) return;

            int allowed = Mathf.Min(maxActive, startActive + Mathf.FloorToInt(GameState.TimeSurvived / secondsPerExtra));
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                spawnTimer = 1.2f;
                if (CountLiving() < allowed) TrySpawn(false);
            }

            // Retire zombies left far behind (alive ones only; corpses retire themselves).
            float d2 = despawnDistance * despawnDistance;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var z = active[i];
                if (z.CurrentState == ZombieAI.State.Dead) continue;
                if ((z.transform.position - player.position).sqrMagnitude > d2) Retire(z);
            }
        }

        int CountLiving()
        {
            int n = 0;
            foreach (var z in active) if (z.CurrentState != ZombieAI.State.Dead) n++;
            return n;
        }

        bool TrySpawn(bool initial)
        {
            Vector3 pos;
            if (!FindSpawnPoint(initial, out pos)) return false;

            ZombieAI z;
            if (pool.Count > 0) z = pool.Pop();
            else
            {
                var go = Instantiate(template, transform);
                go.name = "Zombie";
                z = go.GetComponent<ZombieAI>();
                z.OnFinished = Retire;
            }
            z.transform.position = pos;
            z.gameObject.SetActive(true);

            var body = z.GetComponentInChildren<ZombieBody>();
            if (body != null) body.Dress(Pick(skins), Pick(shirts), Pick(pants));
            float scale = Random.Range(0.92f, 1.08f);
            z.transform.localScale = new Vector3(scale, scale, scale);

            bool runner = Random.value < runnerChance;
            z.wanderSpeed = Random.Range(0.7f, 1.2f);
            z.chaseSpeed = runner ? Random.Range(5.2f, 6.2f) : Random.Range(2.6f, 3.8f);
            z.attackDamage = Random.Range(9f, 14f);
            z.Activate(pos, player, health);
            active.Add(z);
            return true;
        }

        void Retire(ZombieAI z)
        {
            active.Remove(z);
            z.gameObject.SetActive(false);
            pool.Push(z);
        }

        bool FindSpawnPoint(bool initial, out Vector3 result)
        {
            result = Vector3.zero;
            var terrain = Terrain.activeTerrain;
            Vector3 center = player.position;
            Vector3 back = -player.forward;
            back.y = 0f;
            back.Normalize();

            for (int attempt = 0; attempt < 12; attempt++)
            {
                // Mostly behind or beside the player, so they do not pop in on screen.
                float angle = Random.value < 0.7f ? Random.Range(-110f, 110f) : Random.Range(0f, 360f);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * back;
                float minR = initial ? safeRadiusAtStart : spawnMin;
                float dist = Random.Range(minR, Mathf.Max(minR + 5f, spawnMax));
                Vector3 p = center + dir * dist;
                float groundY = terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : center.y;
                p.y = groundY;

                if (hasNavMesh)
                {
                    NavMeshHit hit;
                    if (!NavMesh.SamplePosition(p, out hit, 5f, NavMesh.AllAreas)) continue;
                    // Reject roofs and anything well above the ground.
                    if (Mathf.Abs(hit.position.y - groundY) > 2.5f) continue;
                    p = hit.position;
                }
                if (!initial && Visible(p)) continue;
                result = p;
                return true;
            }
            return false;
        }

        bool Visible(Vector3 p)
        {
            if (cam == null) return false;
            Vector3 vp = cam.WorldToViewportPoint(p + Vector3.up * 1f);
            if (vp.z <= 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) return false;
            // In the view cone: visible unless something blocks the line.
            Vector3 from = cam.transform.position;
            Vector3 to = p + Vector3.up * 1.2f;
            return !Physics.Linecast(from, to, ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
