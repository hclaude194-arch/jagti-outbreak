using System.Collections.Generic;
using Jagti.Gameplay;
using Jagti.World;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Firing for everyone (player and bots): hitscan rays with spread, damage falloff with range,
    /// headshots through Hitbox, armour through Combatant.ReceiveHit. Draws tracers and
    /// impact puffs from small pools and plays positional gunshots, so far-away fights can be heard.
    /// </summary>
    public static class Ballistics
    {
        public static Material tracerMaterial;
        public static Material puffMaterial;

        /// <summary>Called when the player hits someone: (headshot, killed).</summary>
        public static System.Action<bool, bool> PlayerHit;
        /// <summary>A shot passed close to the player (the HUD shows where it came from).</summary>
        public static System.Action<Vector3> PlayerNearMiss;
        /// <summary>The collider the last Trace stopped at (vehicles take damage through it).</summary>
        public static Collider LastHitCollider;

        /// <summary>
        /// Online: a shot fired by someone this machine owns (shooter, gun, origin, pellet ends, and a
        /// bit per pellet that stopped at a wall or the ground rather than a person). NetMatch sends it
        /// so other machines can show it with ReplayShot.
        /// </summary>
        public static System.Action<Combatant, GunItem, Vector3, List<Vector3>, int> Fired;

        static readonly List<Vector3> shotEnds = new List<Vector3>();
        static readonly RaycastHit[] hitBuffer = new RaycastHit[24];
        static readonly Dictionary<Combatant, float> totals = new Dictionary<Combatant, float>();
        static readonly HashSet<Combatant> heads = new HashSet<Combatant>();

        const int MaskAll = ~0;

        /// <summary>
        /// Fires one shot (all pellets) of the gun from origin along dir. spreadScale multiplies the gun's
        /// spread (aiming, moving and bot skill change it). Returns true if anyone was hit.
        /// </summary>
        public static bool Fire(Combatant shooter, GunItem gun, Vector3 origin, Vector3 dir, float spreadDegrees)
        {
            var def = gun.def;
            totals.Clear();
            heads.Clear();
            float tierDamage = RoyaleItems.TierDamage(gun.tier);
            bool anyHit = false;
            shotEnds.Clear();
            int worldMask = 0;
            for (int p = 0; p < def.pellets; p++)
            {
                Vector3 d = Spread(dir, spreadDegrees);
                Vector3 end;
                Combatant victim;
                bool head;
                float distance;
                if (Trace(shooter, origin, d, def.range * 1.6f, out end, out victim, out head, out distance))
                {
                    if (victim == null && LastHitCollider != null)
                    {
                        var vehicle = LastHitCollider.GetComponentInParent<Vehicle>();
                        if (vehicle != null) vehicle.TakeHit(def.damage * tierDamage * 0.6f, shooter);
                    }
                    if (victim != null)
                    {
                        float fall = Mathf.Lerp(1f, def.falloffMin, Mathf.Clamp01(distance / def.range));
                        float dmg = def.damage * tierDamage * fall * (head ? def.headMultiplier : 1f);
                        float sum;
                        totals.TryGetValue(victim, out sum);
                        totals[victim] = sum + dmg;
                        if (head) heads.Add(victim);
                        anyHit = true;
                    }
                    else
                    {
                        Puff(end, d);
                        ImpactMarks.At(end, d);
                        if (p < 16) worldMask |= 1 << p;
                    }
                }
                shotEnds.Add(end);
                if ((p == 0 || p % 3 == 0) && !(gun.Suppressed && Random.value < 0.7f)) Tracer(origin + dir * 0.6f, end);
                NearMiss(shooter, origin, end);
            }

            foreach (var kv in totals)
            {
                bool head = heads.Contains(kv.Key);
                bool wasAlive = kv.Key.Alive;
                kv.Key.ReceiveHit(kv.Value, head, shooter, def.name, origin);
                if (shooter != null && shooter.isPlayer && PlayerHit != null) PlayerHit(head, wasAlive && !kv.Key.Alive);
            }

            NoiseEvents.Emit(origin, gun.Noise);
            if (shooter != null && !shooter.isPlayer) SoundAt(origin, gun.Suppressed ? "suppressed" : def.sound, 1f);
            if (Fired != null && shooter != null && shooter.owned) Fired(shooter, gun, origin, shotEnds, worldMask);
            return anyHit;
        }

        /// <summary>
        /// Shows a shot another machine fired (online): tracers, dust where pellets hit walls, the sound,
        /// and near misses (bots duck, the player hears the crack). Damage comes separately from the
        /// shooter's machine.
        /// </summary>
        public static void ReplayShot(Combatant shooter, int gunId, bool suppressed, Vector3 origin, List<Vector3> ends, int worldMask)
        {
            if (gunId < 0 || gunId >= RoyaleItems.Guns.Length) return;
            var def = RoyaleItems.Guns[gunId];
            for (int p = 0; p < ends.Count; p++)
            {
                Vector3 d = ends[p] - origin;
                if (d.sqrMagnitude < 1e-4f) continue;
                Vector3 dir = d.normalized;
                if (p < 16 && (worldMask & (1 << p)) != 0)
                {
                    Puff(ends[p], dir);
                    ImpactMarks.At(ends[p], dir);
                }
                if ((p == 0 || p % 3 == 0) && !(suppressed && Random.value < 0.7f)) Tracer(origin + dir * 0.6f, ends[p]);
                NearMiss(shooter, origin, ends[p]);
            }
            NoiseEvents.Emit(origin, suppressed ? def.noise * 0.35f : def.noise);
            SoundAt(origin, suppressed ? "suppressed" : def.sound, 1f);
        }

        static Vector3 Spread(Vector3 dir, float degrees)
        {
            if (degrees <= 0f) return dir;
            // Uniform within a cone, slightly weighted to the middle.
            Vector2 r = Random.insideUnitCircle * (degrees * 0.5f);
            Quaternion look = Quaternion.LookRotation(dir);
            return look * (Quaternion.Euler(r.y, r.x, 0f) * Vector3.forward);
        }

        /// <summary>
        /// The first thing a ray hits that isn't the shooter. victim is the Combatant hit (or null for
        /// walls and ground).
        /// </summary>
        public static bool Trace(Combatant shooter, Vector3 origin, Vector3 dir, float range,
                                 out Vector3 end, out Combatant victim, out bool head, out float distance)
        {
            victim = null;
            head = false;
            end = origin + dir * range;
            LastHitCollider = null;
            distance = range;
            int n = Physics.RaycastNonAlloc(origin, dir, hitBuffer, range, MaskAll, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < n; i++)
            {
                var h = hitBuffer[i];
                if (h.distance >= best) continue;
                if (shooter != null && h.collider.transform.IsChildOf(shooter.transform)) continue;
                best = h.distance;
                bestIndex = i;
            }
            if (bestIndex < 0) return false;
            var hit = hitBuffer[bestIndex];
            LastHitCollider = hit.collider;
            end = hit.point;
            distance = hit.distance;
            var box = hit.collider.GetComponent<Hitbox>();
            if (box != null && box.owner != null)
            {
                victim = box.owner.GetComponent<Combatant>();
                head = box.isHead;
            }
            if (victim == null) victim = hit.collider.GetComponentInParent<Combatant>();
            if (victim != null && !victim.Alive) victim = null;
            return true;
        }

        /// <summary>True if nothing solid stands between the two points (ignoring both combatants).</summary>
        public static bool LineOfSight(Vector3 from, Vector3 to, Combatant a, Combatant b)
        {
            if (SmokeCloud.Active.Count > 0 && SmokeCloud.Blocks(from, to)) return false;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            int n = Physics.RaycastNonAlloc(from, d / len, hitBuffer, len, MaskAll, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var t = hitBuffer[i].collider.transform;
                if (a != null && t.IsChildOf(a.transform)) continue;
                if (b != null && t.IsChildOf(b.transform)) continue;
                return false;
            }
            return true;
        }

        static void NearMiss(Combatant shooter, Vector3 a, Vector3 b)
        {
            // Bots near the line know they are being shot at (and roughly from where).
            Vector3 ab0 = b - a;
            float len2 = Mathf.Max(0.001f, ab0.sqrMagnitude);
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == shooter || c.isPlayer || c.UnderFire == null || !c.Alive) continue;
                Vector3 q = c.AimPoint;
                float t0 = Mathf.Clamp01(Vector3.Dot(q - a, ab0) / len2);
                if ((a + ab0 * t0 - q).sqrMagnitude < 3f * 3f) c.UnderFire(shooter);
            }
            if (shooter != null && shooter.isPlayer) return;
            var cam = Camera.main;
            if (cam == null || PlayerNearMiss == null) return;
            Vector3 p = cam.transform.position;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
            float d = (a + ab * t - p).magnitude;
            if (d < 2.5f && d > 0.3f)
            {
                PlayerNearMiss(a);
                SynthAudio.Play2D("whiz", Mathf.Lerp(0.6f, 0.15f, d / 2.5f));
            }
        }

        // ------------------------------------------------------------------ effects

        class TracerFx
        {
            public LineRenderer line;
            public float life;
        }

        static readonly List<TracerFx> tracers = new List<TracerFx>();
        static readonly List<ParticleSystem> puffs = new List<ParticleSystem>();
        static int nextPuff;
        static Transform fxRoot;
        static readonly List<AudioSource> voices = new List<AudioSource>();
        static int nextVoice;

        static Transform Root()
        {
            if (fxRoot == null)
            {
                fxRoot = new GameObject("Shot Effects").transform;
                fxRoot.gameObject.AddComponent<BallisticsTicker>();
            }
            return fxRoot;
        }

        static void Tracer(Vector3 a, Vector3 b)
        {
            if (tracerMaterial == null) return;
            TracerFx fx = null;
            foreach (var t in tracers)
            {
                if (t.life <= 0f) { fx = t; break; }
            }
            if (fx == null)
            {
                if (tracers.Count >= 48) return;
                var go = new GameObject("Tracer");
                go.transform.SetParent(Root(), false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = tracerMaterial;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.035f;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                fx = new TracerFx { line = lr };
                tracers.Add(fx);
            }
            // Only the leading part of the path shows, as a short bright streak.
            Vector3 d = b - a;
            float len = d.magnitude;
            Vector3 start = a + d * Random.Range(0.05f, 0.35f);
            Vector3 stop = start + d.normalized * Mathf.Min(len * 0.6f, 14f);
            fx.line.SetPosition(0, start);
            fx.line.SetPosition(1, stop);
            fx.line.enabled = true;
            fx.life = 0.06f;
        }

        static void Puff(Vector3 at, Vector3 dir)
        {
            if (puffMaterial == null) return;
            ParticleSystem ps;
            if (puffs.Count < 24)
            {
                var go = new GameObject("Impact");
                go.transform.SetParent(Root(), false);
                ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 0.3f;
                main.startLifetime = 0.45f;
                main.startSpeed = 1.6f;
                main.startSize = 0.18f;
                main.startColor = new Color(0.72f, 0.68f, 0.6f, 0.8f);
                main.gravityModifier = 0.3f;
                main.maxParticles = 12;
                var em = ps.emission;
                em.rateOverTime = 0f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
                shape.radius = 0.02f;
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = puffMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                puffs.Add(ps);
            }
            else
            {
                ps = puffs[nextPuff];
                nextPuff = (nextPuff + 1) % puffs.Count;
            }
            ps.transform.position = at - dir * 0.05f;
            ps.transform.rotation = Quaternion.LookRotation(-dir);
            ps.Emit(8);
        }

        /// <summary>A positional one-shot from a small pool of 3D voices (gunshots of other players, explosions).</summary>
        public static void SoundAt(Vector3 at, string clip, float volume)
        {
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - at).sqrMagnitude > 700f * 700f) return;
            AudioSource src;
            if (voices.Count < 16)
            {
                var go = new GameObject("Voice");
                go.transform.SetParent(Root(), false);
                src = go.AddComponent<AudioSource>();
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 8f;
                src.maxDistance = 600f;
                src.dopplerLevel = 0f;
                src.playOnAwake = false;
                voices.Add(src);
            }
            else
            {
                src = voices[nextVoice];
                nextVoice = (nextVoice + 1) % voices.Count;
            }
            src.transform.position = at;
            SynthAudio.Play3D(src, clip, volume);
        }

        public static void Tick(float dt)
        {
            foreach (var t in tracers)
            {
                if (t.life <= 0f) continue;
                t.life -= dt;
                if (t.life <= 0f) t.line.enabled = false;
            }
        }

        /// <summary>Clears the pools when a new scene loads.</summary>
        public static void Reset()
        {
            tracers.Clear();
            puffs.Clear();
            voices.Clear();
            nextPuff = 0;
            nextVoice = 0;
            fxRoot = null;
        }
    }
}
