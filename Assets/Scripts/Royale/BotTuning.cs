using UnityEngine;
using UnityEngine.AI;

namespace Jagti.Royale
{
    /// <summary>The three bot difficulty levels, chosen in the pause menu or on the results screen.</summary>
    public static class BotDifficulty
    {
        public enum Level { Recruit = 0, Regular = 1, Veteran = 2 }

        public struct Settings
        {
            public float skillMin, skillMax;   // spread of bot skill (0..1)
            public float reaction;             // multiplies reaction time
            public float aim;                  // multiplies aim error
            public float cover;                // chance to go for cover when a fight starts or turns bad
            public float grenades;             // chance to throw at someone hiding
            public float sight;                // multiplies spotting range
            public float hearing;              // multiplies how far gunfire draws them
        }

        const string Key = "Jagti.Royale.Difficulty";

        public static Level Current
        {
            get
            {
                try { return (Level)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Level.Regular), 0, 2); }
                catch { return Level.Regular; }
            }
            set
            {
                try { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); }
                catch { }
            }
        }

        public static string Name(Level l)
        {
            return l == Level.Recruit ? "Recruit" : l == Level.Veteran ? "Veteran" : "Regular";
        }

        public static string Describe(Level l)
        {
            switch (l)
            {
                case Level.Recruit: return "Slow to react, poor aim, rarely use cover";
                case Level.Veteran: return "Quick, accurate, fight from cover and throw grenades";
                default: return "A fair fight";
            }
        }

        public static Settings Get(Level l)
        {
            switch (l)
            {
                case Level.Recruit:
                    return new Settings { skillMin = 0.05f, skillMax = 0.45f, reaction = 1.35f, aim = 1.45f, cover = 0.2f, grenades = 0.1f, sight = 0.8f, hearing = 0.7f };
                case Level.Veteran:
                    return new Settings { skillMin = 0.55f, skillMax = 1f, reaction = 0.8f, aim = 0.75f, cover = 0.85f, grenades = 0.55f, sight = 1.15f, hearing = 1.25f };
                default:
                    return new Settings { skillMin = 0.25f, skillMax = 0.8f, reaction = 1f, aim = 1f, cover = 0.55f, grenades = 0.3f, sight = 1f, hearing = 1f };
            }
        }
    }

    /// <summary>
    /// Shares out the bots' thinking so a crowd never lands on one frame: a few thinks and a few
    /// cover searches per frame, the rest wait their turn (a frame or two at most).
    /// </summary>
    public static class BotScheduler
    {
        public static int thinksPerFrame = 6;
        public static int searchesPerFrame = 2;

        static int frame = -1;
        static int thinks, searches;

        static void Roll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            thinks = 0;
            searches = 0;
        }

        public static bool TryThink()
        {
            Roll();
            if (thinks >= thinksPerFrame) return false;
            thinks++;
            return true;
        }

        public static bool TrySearch()
        {
            Roll();
            if (searches >= searchesPerFrame) return false;
            searches++;
            return true;
        }
    }

    /// <summary>
    /// Finds cover from a threat near a bot. Candidates are the edges of the walkable area (which run
    /// along walls, low walls, parked buses and furniture) in a ring around the bot. A spot counts as
    /// cover when the threat cannot see a crouched chest there; it is "low" cover when a standing
    /// bot can shoot over it, and a corner when a short side-step gives a line of fire (the peek spot).
    /// </summary>
    public static class CoverFinder
    {
        public struct Spot
        {
            public Vector3 position;
            public Vector3 peek;      // where to step to shoot (equals position for low cover)
            public bool low;          // stand up to shoot, crouch to hide
            public bool canShoot;     // low cover or a corner with a peek spot
        }

        static readonly float[] Rings = { 3.5f, 7f, 11f };
        const int Directions = 10;
        const int MaxRays = 26;

        public static bool Find(Vector3 from, Vector3 threatEye, Combatant self, Combatant threat, out Spot best)
        {
            best = default(Spot);
            float bestScore = float.MaxValue;
            int rays = 0;
            Vector3 toThreat = threatEye - from;
            toThreat.y = 0f;
            float threatDist = toThreat.magnitude;
            Vector3 threatDir = threatDist > 0.1f ? toThreat / threatDist : Vector3.forward;
            float start = Random.value * 360f;

            foreach (float r in Rings)
            {
                for (int i = 0; i < Directions; i++)
                {
                    if (rays >= MaxRays) break;
                    float a = (start + i * 360f / Directions) * Mathf.Deg2Rad;
                    Vector3 c = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    NavMeshHit hit;
                    if (!NavMesh.SamplePosition(c, out hit, 2.5f, NavMesh.AllAreas)) continue;
                    NavMeshHit edge;
                    if (!NavMesh.FindClosestEdge(hit.position, out edge, NavMesh.AllAreas)) continue;
                    if (edge.distance > 2.5f) continue;
                    Vector3 p = edge.position + edge.normal * 0.35f;
                    if (Mathf.Abs(p.y - from.y) > 2.2f) continue;               // stay on this floor
                    // Not towards the threat, and not right next to it.
                    Vector3 rel = p - from;
                    rel.y = 0f;
                    if (Vector3.Dot(rel, threatDir) > threatDist * 0.5f) continue;
                    if ((p - threatEye).sqrMagnitude < 25f) continue;

                    rays++;
                    if (Ballistics.LineOfSight(threatEye, p + Vector3.up * 0.95f, threat, self)) continue;   // a crouched chest is seen
                    Spot s = new Spot { position = p, peek = p };
                    rays++;
                    s.low = Ballistics.LineOfSight(threatEye, p + Vector3.up * 1.55f, threat, self);
                    s.canShoot = s.low;
                    if (!s.low && rays < MaxRays)
                    {
                        // A corner: step sideways (across the threat's line) to see past it.
                        Vector3 side = Vector3.Cross(Vector3.up, (threatEye - p).normalized);
                        side.y = 0f;
                        side.Normalize();
                        for (int k = 0; k < 2 && rays < MaxRays; k++)
                        {
                            Vector3 q = p + side * (k == 0 ? 1.1f : -1.1f);
                            NavMeshHit qh;
                            if (!NavMesh.SamplePosition(q, out qh, 0.6f, NavMesh.AllAreas)) continue;
                            rays++;
                            if (Ballistics.LineOfSight(threatEye, qh.position + Vector3.up * 1.4f, threat, self))
                            {
                                s.peek = qh.position;
                                s.canShoot = true;
                                break;
                            }
                        }
                    }
                    float score = rel.magnitude + (s.canShoot ? 0f : 6f) + (s.low ? -1f : 0f);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = s;
                    }
                }
                if (bestScore < r) break;   // a good spot on the inner ring beats searching further
            }
            return bestScore < float.MaxValue;
        }

        /// <summary>Launch velocity that lobs a grenade from a to b at the given angle, or false if out of reach.</summary>
        public static bool Lob(Vector3 a, Vector3 b, float angleDeg, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            Vector3 flat = b - a;
            float h = flat.y;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d < 1f) return false;
            float g = -Physics.gravity.y;
            float th = angleDeg * Mathf.Deg2Rad;
            float denom = 2f * Mathf.Cos(th) * Mathf.Cos(th) * (d * Mathf.Tan(th) - h);
            if (denom <= 0.01f) return false;
            float v = Mathf.Sqrt(g * d * d / denom);
            if (v > 22f) return false;
            velocity = flat / d * (v * Mathf.Cos(th)) + Vector3.up * (v * Mathf.Sin(th));
            return true;
        }
    }
}
