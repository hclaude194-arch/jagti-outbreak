using Jagti.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace Jagti.Royale
{
    /// <summary>
    /// The closing haze. A wall of grey-brown haze stands on a circle; outside it everyone loses
    /// health every second (armour does not help). In each of six rounds the next safe circle is
    /// announced, the haze holds for a while, then closes in to it. Later circles are always centred
    /// on walkable campus ground inside the boundary wall, never over a ravine.
    /// Outside the circle the fog thickens and turns brown, so you feel it as well as see it.
    /// </summary>
    public class HazeZone : MonoBehaviour
    {
        [System.Serializable]
        public class Round
        {
            public float hold;        // seconds the next circle is shown before the haze moves
            public float close;       // seconds the haze takes to reach it
            public float shrink;      // next radius as a share of the current one
            public float damage;      // health lost per second outside, during and after this round
        }

        public Round[] rounds =
        {
            new Round { hold = 120f, close = 80f, shrink = 0.58f, damage = 1f },
            new Round { hold = 90f, close = 65f, shrink = 0.55f, damage = 2f },
            new Round { hold = 75f, close = 50f, shrink = 0.5f, damage = 4f },
            new Round { hold = 60f, close = 40f, shrink = 0.5f, damage = 6f },
            new Round { hold = 45f, close = 35f, shrink = 0.45f, damage = 8f },
            new Round { hold = 35f, close = 35f, shrink = 0f, damage = 10f },
        };

        [Header("Set by RoyaleBuilder")]
        public Vector2 startCentre;
        public float startRadius = 600f;
        public float[] boundary;          // campus wall polygon, flat x0, z0, x1, z1...
        public Material wallMaterial;
        public float wallBottom = -60f;
        public float wallTop = 650f;
        public Color hazeFog = new Color(0.55f, 0.47f, 0.38f);

        public Vector2 Centre { get; private set; }
        public float Radius { get; private set; }
        public Vector2 NextCentre { get; private set; }
        public float NextRadius { get; private set; }
        public int RoundIndex { get; private set; }     // 0-based; equals rounds.Length when the haze has closed
        public bool Closing { get; private set; }
        public float TimeLeft { get; private set; }     // in the current hold or close
        public bool Running { get; private set; }
        /// <summary>Health lost per second outside the circle right now.</summary>
        public float Damage { get { return rounds[Mathf.Clamp(RoundIndex, 0, rounds.Length - 1)].damage; } }
        /// <summary>0 inside the circle, rising to 1 a few metres into the haze (for the HUD tint).</summary>
        public float PlayerInHaze { get; private set; }

        Transform wall;
        Vector2 fromCentre;
        float fromRadius;
        float tickTimer;
        float fogStart, fogEnd;
        Color fogColour;
        bool fogSaved;
        System.Random rng;

        void Start()
        {
            Centre = startCentre;
            Radius = startRadius;
            NextCentre = Centre;
            NextRadius = Radius;
            BuildWall();
            UpdateWall();
        }

        void OnDisable()
        {
            RestoreFog();
        }

        public void Begin(int seed)
        {
            rng = new System.Random(seed);
            RoundIndex = 0;
            StartHold();
            Running = true;
        }

        void StartHold()
        {
            if (RoundIndex >= rounds.Length)
            {
                Closing = false;
                return;
            }
            var r = rounds[RoundIndex];
            NextRadius = Radius * r.shrink;
            NextCentre = PickCentre(Centre, Radius, NextRadius, RoundIndex);
            Closing = false;
            TimeLeft = r.hold;
            if (MatchDirector.Instance != null)
                MatchDirector.Instance.Announce(RoundIndex == 0 ? "The haze is rising. Find the safe circle." : "New safe circle marked on the map.");
        }

        Vector2 PickCentre(Vector2 c, float r, float next, int round)
        {
            float room = Mathf.Max(0f, r - next);
            Vector2 best = c;
            for (int i = 0; i < 40; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = Mathf.Sqrt((float)rng.NextDouble()) * room;
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (!InsideBoundary(p.x, p.y)) continue;
                // Later circles must sit on ground people can reach (the NavMesh covers the campus).
                float y = GroundY(p.x, p.y);
                NavMeshHit hit;
                if (NavMesh.SamplePosition(new Vector3(p.x, y, p.y), out hit, round >= 2 ? 6f : 25f, NavMesh.AllAreas))
                {
                    best = p;
                    break;
                }
            }
            return best;
        }

        public static float GroundY(float x, float z)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : 0f;
        }

        public bool InsideBoundary(float x, float z)
        {
            if (boundary == null || boundary.Length < 6) return true;
            bool inside = false;
            int n = boundary.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = boundary[i * 2], zi = boundary[i * 2 + 1];
                float xj = boundary[j * 2], zj = boundary[j * 2 + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
            }
            return inside;
        }

        public bool IsInside(Vector3 p)
        {
            return (new Vector2(p.x, p.z) - Centre).sqrMagnitude <= Radius * Radius;
        }

        public float DistanceOutside(Vector3 p)
        {
            return (new Vector2(p.x, p.z) - Centre).magnitude - Radius;
        }

        void Update()
        {
            if (Running && RoundIndex < rounds.Length)
            {
                TimeLeft -= Time.deltaTime;
                if (!Closing && TimeLeft <= 0f)
                {
                    Closing = true;
                    fromCentre = Centre;
                    fromRadius = Radius;
                    TimeLeft = rounds[RoundIndex].close;
                    if (MatchDirector.Instance != null) MatchDirector.Instance.Announce("The haze is closing in.");
                }
                else if (Closing)
                {
                    float t = 1f - Mathf.Clamp01(TimeLeft / Mathf.Max(0.01f, rounds[RoundIndex].close));
                    Centre = Vector2.Lerp(fromCentre, NextCentre, t);
                    Radius = Mathf.Lerp(fromRadius, NextRadius, t);
                    if (TimeLeft <= 0f)
                    {
                        Centre = NextCentre;
                        Radius = NextRadius;
                        RoundIndex++;
                        StartHold();
                    }
                }
            }
            UpdateWall();
            if (Running) HurtOutside();
            UpdateFog();
        }

        void HurtOutside()
        {
            tickTimer -= Time.deltaTime;
            if (tickTimer > 0f) return;
            tickTimer = 1f;
            float dmg = Damage;
            if (dmg <= 0f) return;
            var all = Combatant.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var c = all[i];
                if (c == null || !c.Alive || !c.Landed) continue;
                if (IsInside(c.transform.position)) continue;
                c.ReceiveHit(dmg, false, null, "the haze", c.transform.position, true);
                if (c.isPlayer) SynthAudio.Play2D("haze_tick", 0.5f);
            }
        }

        // ------------------------------------------------------------------ look

        void BuildWall()
        {
            if (wallMaterial == null) return;
            const int seg = 96;
            var v = new Vector3[(seg + 1) * 2];
            var uv = new Vector2[v.Length];
            var tris = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = d;
                v[i * 2 + 1] = d + Vector3.up;
                uv[i * 2] = new Vector2(i / (float)seg, 0f);
                uv[i * 2 + 1] = new Vector2(i / (float)seg, 1f);
                if (i < seg)
                {
                    int k = i * 6, b = i * 2;
                    tris[k] = b; tris[k + 1] = b + 1; tris[k + 2] = b + 3;
                    tris[k + 3] = b; tris[k + 4] = b + 3; tris[k + 5] = b + 2;
                }
            }
            var mesh = new Mesh { name = "Haze Wall" };
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
            var go = new GameObject("Haze Wall");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = wallMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            wall = go.transform;
        }

        void UpdateWall()
        {
            if (wall == null) return;
            float r = Mathf.Max(1f, Radius);
            wall.position = new Vector3(Centre.x, wallBottom, Centre.y);
            wall.localScale = new Vector3(r, wallTop - wallBottom, r);
            if (wallMaterial != null)
            {
                // Keep the streaks about 25 m apart however big the circle is, and let them drift.
                float around = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * r / 25f));
                wallMaterial.mainTextureScale = new Vector2(around, 3f);
                wallMaterial.mainTextureOffset = new Vector2(Time.time * 0.01f, -Time.time * 0.02f);
            }
        }

        void UpdateFog()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (!fogSaved)
            {
                fogStart = RenderSettings.fogStartDistance;
                fogEnd = RenderSettings.fogEndDistance;
                fogColour = RenderSettings.fogColor;
                fogSaved = true;
            }
            float outside = DistanceOutside(cam.transform.position);
            float target = Mathf.Clamp01((outside + 2f) / 8f);
            PlayerInHaze = Mathf.MoveTowards(PlayerInHaze, target, Time.deltaTime * 1.5f);
            RenderSettings.fogStartDistance = Mathf.Lerp(fogStart, 2f, PlayerInHaze);
            RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd, 70f, PlayerInHaze);
            RenderSettings.fogColor = Color.Lerp(fogColour, hazeFog, PlayerInHaze);
            cam.backgroundColor = RenderSettings.fogColor;
        }

        void RestoreFog()
        {
            if (!fogSaved) return;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.fogColor = fogColour;
        }
    }
}
