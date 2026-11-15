using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace Jagti.Royale
{
    /// <summary>
    /// A computer opponent. It rides the helicopter, jumps near a building it picked, glides down,
    /// then loots rooms for a gun, ammo, armour and heals, keeps ahead of the haze, heads for
    /// gunfire it hears when it feels strong, and fights whoever it sees: it picks the gun that suits
    /// the range, strafes, fires in bursts with aim that settles the longer it tracks you, reloads and
    /// heals when it gets a break. Skill (0..1) sets its reaction time, aim and nerve.
    /// Its thinking runs a few times a second (less often far from the player) to stay cheap.
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class BotBrain : MonoBehaviour
    {
        public enum State { Aboard, Falling, Gliding, Loot, Wander, Rotate, Fight, Chase, Investigate, Heal, Dead }

        public HumanBody body;
        public float skill = 0.5f;
        public Vector3 landingTarget;
        public float jumpTime;

        public State Current { get; private set; }

        Combatant me;
        NavMeshAgent agent;
        Helicopter heli;
        HazeZone haze;
        Combatant enemy;
        Vector3 lastSeen;
        float lastSeenTime = -99f;
        float trackTime;
        float thinkTimer;
        float nextShot;
        int burstLeft;
        float reloadLeft;
        float strafeTimer;
        float healCooldown;
        LootDrop goal;
        Vector3 wanderGoal;
        bool hasWander;
        Vector3 heardAt;
        float heardTime = -99f;
        Vector3 rotateGoal;
        int rotateRound = -1;
        float stuckTimer;
        Vector3 lastPos;
        float glideSink;
        readonly List<LootDrop> nearLoot = new List<LootDrop>();
        AudioSource voice;

        void Awake()
        {
            me = GetComponent<Combatant>();
            agent = GetComponent<NavMeshAgent>();
            voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 4f;
            voice.maxDistance = 60f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.playOnAwake = false;
        }

        void OnEnable()
        {
            NoiseEvents.Heard += OnHeard;
            me.Health.Damaged += OnDamaged;
            me.Health.Died += OnDied;
        }

        void OnDisable()
        {
            NoiseEvents.Heard -= OnHeard;
            me.Health.Damaged -= OnDamaged;
            me.Health.Died -= OnDied;
        }

        /// <summary>Sets the bot aboard the helicopter with its chosen landing spot.</summary>
        public void Board(Helicopter h, HazeZone zone, Vector3 target, float jumpAt)
        {
            heli = h;
            haze = zone;
            landingTarget = target;
            jumpTime = jumpAt;
            Current = State.Aboard;
            me.Landed = false;
            agent.enabled = false;
            if (body != null) body.gameObject.SetActive(false);
        }

        void OnHeard(Vector3 at, float radius)
        {
            if (Current == State.Dead || Current == State.Aboard || !me.Landed) return;
            if ((at - transform.position).sqrMagnitude > radius * radius) return;
            if ((at - transform.position).sqrMagnitude < 4f) return;   // its own shots
            heardAt = at;
            heardTime = Time.time;
        }

        void OnDamaged(float amount, Vector3 from)
        {
            // Being shot reveals where it came from.
            var a = me.LastAttacker;
            if (a != null && a.Alive && Time.time - me.LastHitTime < 0.1f)
            {
                if (enemy == null || !enemy.Alive) enemy = a;
                lastSeen = a.transform.position;
                lastSeenTime = Time.time;
            }
            me.CancelHeal();
        }

        void OnDied()
        {
            Current = State.Dead;
            if (agent.enabled) agent.enabled = false;
            if (body != null) body.Die(me.LastHitFrom);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (LootDirector.Instance != null) LootDirector.Instance.DropLoadout(me.loadout, transform.position);
            SynthAudio.Play3D(voice, "death_grunt", 0.8f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (Current)
            {
                case State.Dead: return;
                case State.Aboard: Aboard(); return;
                case State.Falling:
                case State.Gliding: Fly(dt); return;
            }
            if (GameState.Paused) return;

            thinkTimer -= dt;
            if (thinkTimer <= 0f)
            {
                var cam = Camera.main;
                bool nearPlayer = cam == null || (cam.transform.position - transform.position).sqrMagnitude < 200f * 200f;
                thinkTimer = (nearPlayer ? 0.25f : 0.6f) * Random.Range(0.85f, 1.15f);
                Think();
            }
            Act(dt);
            Animate();
        }

        // ------------------------------------------------------------------ the drop

        void Aboard()
        {
            if (heli == null) return;
            transform.position = heli.Door;
            if (heli.Elapsed >= jumpTime || heli.Progress >= 0.985f)
            {
                Current = State.Falling;
                transform.position = heli.Door + Random.insideUnitSphere * 2f;
                if (body != null)
                {
                    body.gameObject.SetActive(true);
                    body.pose = HumanBody.Pose.FreeFall;
                }
            }
        }

        void Fly(float dt)
        {
            Vector3 p = transform.position;
            Vector3 flat = landingTarget - p;
            flat.y = 0f;
            float dist = flat.magnitude;
            float height = p.y - landingTarget.y;
            Vector3 dir = dist > 0.5f ? flat / dist : Vector3.zero;
            if (Current == State.Falling)
            {
                p += dir * Mathf.Min(dist, 16f * dt) + Vector3.down * 44f * dt;
                if (height < 120f + Random.Range(0f, 30f))
                {
                    Current = State.Gliding;
                    if (body != null) body.pose = HumanBody.Pose.Glide;
                    float time = Mathf.Max(4f, dist / 12f);
                    glideSink = Mathf.Clamp(height / time, 2.5f, 9f);
                }
            }
            else
            {
                // Steer for the spot; spiral down over it if there is height to lose.
                Vector3 move = dist > 3f ? dir * 12f : Quaternion.Euler(0f, 90f, 0f) * (dist > 0.1f ? dir : transform.forward) * 6f;
                p += move * dt + Vector3.down * glideSink * dt;
                if (dist <= 3f) glideSink = Mathf.Max(glideSink, 5f);
            }
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), dt * 2f);
            transform.position = p;
            if (p.y <= landingTarget.y + 0.05f) Touchdown();
        }

        void Touchdown()
        {
            NavMeshHit hit;
            Vector3 spot = landingTarget;
            if (NavMesh.SamplePosition(landingTarget, out hit, 8f, NavMesh.AllAreas)) spot = hit.position;
            transform.position = spot;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            agent.enabled = true;
            agent.Warp(spot);
            agent.updateRotation = true;
            me.Landed = true;
            Current = State.Loot;
            if (body != null) body.pose = HumanBody.Pose.Stand;
            lastPos = spot;
        }

        // ------------------------------------------------------------------ thinking

        void Think()
        {
            var l = me.loadout;
            Look();

            bool armed = l.BestSlot(20f) >= 0;
            float health = me.Health.current;
            bool enemyVisible = enemy != null && enemy.Alive && Time.time - lastSeenTime < 0.6f;

            // The haze comes first unless a fight is right here.
            if (haze != null && NeedToMove() && !(enemyVisible && armed && (enemy.transform.position - transform.position).sqrMagnitude < 30f * 30f))
            {
                Current = State.Rotate;
                GoTo(RotateGoal(), true);
                return;
            }

            if (enemyVisible && armed)
            {
                if (Current != State.Fight) { trackTime = 0f; burstLeft = 0; }
                Current = State.Fight;
                return;
            }
            if (enemyVisible && !armed)
            {
                // Run away from an armed enemy while unarmed; keep looking for a gun.
                Current = State.Loot;
                Vector3 away = transform.position + (transform.position - enemy.transform.position).normalized * 20f;
                GoTo(away, true);
                return;
            }

            if (armed && enemy != null && enemy.Alive && Time.time - lastSeenTime < 6f + skill * 6f)
            {
                Current = State.Chase;
                GoTo(lastSeen, true);
                return;
            }

            if (health < 70f && healCooldown <= 0f && (l.bandages > 0 || l.medkits > 0 || l.chai > 0) && Time.time - me.LastHitTime > 3f)
            {
                Current = State.Heal;
                if (agent.isOnNavMesh) agent.ResetPath();
                if (!me.IsHealing)
                {
                    bool ok = (health < 45f && me.StartHeal(ItemKind.Medkit)) || me.StartHeal(ItemKind.Bandage) ||
                              me.StartHeal(ItemKind.Medkit) || me.StartHeal(ItemKind.ChaiFlask);
                    if (!ok) healCooldown = 5f;
                }
                return;
            }
            healCooldown -= 0.25f;

            if (armed && Time.time - heardTime < 8f && health > 55f && Random.value < 0.25f + skill * 0.5f &&
                (heardAt - transform.position).sqrMagnitude < 140f * 140f)
            {
                Current = State.Investigate;
                GoTo(heardAt + Random.insideUnitSphere * 6f, false);
                heardTime = -99f;
                return;
            }

            if (PickLoot())
            {
                Current = State.Loot;
                return;
            }

            Current = State.Wander;
            if (!hasWander || (wanderGoal - transform.position).sqrMagnitude < 9f || stuckTimer > 4f)
            {
                wanderGoal = PickWander();
                hasWander = true;
                stuckTimer = 0f;
            }
            GoTo(wanderGoal, false);
        }

        /// <summary>Looks for the nearest opponent it can actually see.</summary>
        void Look()
        {
            float range = 70f + skill * 60f;
            var l = me.loadout;
            foreach (var g in l.guns)
                if (g != null && (g.def.gunClass == GunClass.Marksman || g.def.gunClass == GunClass.Rifle)) range += 40f;
            Vector3 eye = me.eye != null ? me.eye.position : transform.position + Vector3.up * 1.6f;
            Combatant best = null;
            float bestD = range * range;
            int checks = 0;
            foreach (var c in Combatant.All)
            {
                if (c == me || !c.Alive || !c.Landed) continue;
                Vector3 to = c.AimPoint - eye;
                float d2 = to.sqrMagnitude;
                if (d2 > bestD) continue;
                // A wide field of view, but anyone within 12 m is noticed whichever way the bot faces.
                if (d2 > 144f && Vector3.Dot(transform.forward, to.normalized) < -0.2f) continue;
                if (++checks > 4) break;
                if (!Ballistics.LineOfSight(eye, c.AimPoint, me, c)) continue;
                best = c;
                bestD = d2;
            }
            if (best != null)
            {
                if (best != enemy) trackTime = 0f;
                enemy = best;
                lastSeen = best.transform.position;
                lastSeenTime = Time.time;
            }
        }

        bool NeedToMove()
        {
            Vector3 p = transform.position;
            if (!haze.IsInside(p)) return true;
            if (!haze.Running) return false;
            // Head for the next circle once the haze is moving, or a little before if it is far.
            Vector2 d = new Vector2(p.x, p.z) - haze.NextCentre;
            if (d.magnitude <= haze.NextRadius * 0.9f) return false;
            float distance = d.magnitude - haze.NextRadius * 0.7f;
            float travel = distance / 4.5f;
            return haze.Closing || haze.TimeLeft < travel + 15f;
        }

        Vector3 RotateGoal()
        {
            if (rotateRound != haze.RoundIndex)
            {
                rotateRound = haze.RoundIndex;
                for (int i = 0; i < 12; i++)
                {
                    Vector2 r = haze.NextCentre + Random.insideUnitCircle * haze.NextRadius * 0.6f;
                    Vector3 p = new Vector3(r.x, HazeZone.GroundY(r.x, r.y), r.y);
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(p, out hit, 15f, NavMesh.AllAreas))
                    {
                        rotateGoal = hit.position;
                        return rotateGoal;
                    }
                }
                rotateGoal = new Vector3(haze.NextCentre.x, HazeZone.GroundY(haze.NextCentre.x, haze.NextCentre.y), haze.NextCentre.y);
            }
            return rotateGoal;
        }

        bool PickLoot()
        {
            var loot = LootDirector.Instance;
            if (loot == null) return false;
            if (goal != null && !goal.taken && goal.claimedBy == me && Time.time < goal.claimUntil)
            {
                GoTo(goal.position, false);
                return true;
            }
            goal = null;
            loot.Near(transform.position, 30f, nearLoot);
            float best = 0f;
            var l = me.loadout;
            foreach (var d in nearLoot)
            {
                if (d.claimedBy != null && d.claimedBy != me && d.claimedBy.Alive && Time.time < d.claimUntil) continue;
                if (haze != null && !haze.IsInside(d.position)) continue;
                if (!l.Wants(d.kind, d.value, d.tier)) continue;
                float worth = Worth(d);
                float score = worth / (4f + (d.position - transform.position).magnitude);
                if (score > best)
                {
                    best = score;
                    goal = d;
                }
            }
            if (goal == null) return false;
            goal.claimedBy = me;
            goal.claimUntil = Time.time + 20f;
            GoTo(goal.position, false);
            return true;
        }

        float Worth(LootDrop d)
        {
            var l = me.loadout;
            switch (d.kind)
            {
                case ItemKind.Gun: return l.HasGun ? 4f + (int)d.tier * 2f : 30f;
                case ItemKind.Ammo:
                    foreach (var g in l.guns) if (g != null && (int)g.def.ammo == d.value) return 6f;
                    return 1f;
                case ItemKind.Vest:
                case ItemKind.Helmet: return 6f + d.value * 3f;
                case ItemKind.Backpack: return 4f + d.value * 2f;
                case ItemKind.Medkit: return 6f;
                case ItemKind.Bandage: return 3f;
                case ItemKind.ChaiFlask: return 3f;
                case ItemKind.Grenade: return 1.5f;
            }
            return 1f;
        }

        Vector3 PickWander()
        {
            // A spot near a building: the loot points make good destinations; stay in the circle.
            var loot = LootDirector.Instance;
            for (int i = 0; i < 10; i++)
            {
                Vector3 p;
                if (loot != null && loot.points != null && loot.points.Length > 0)
                {
                    p = loot.points[Random.Range(0, loot.points.Length)];
                    if ((p - transform.position).sqrMagnitude > 160f * 160f) continue;
                }
                else
                {
                    Vector2 r = Random.insideUnitCircle * 80f;
                    p = transform.position + new Vector3(r.x, 0f, r.y);
                }
                if (haze != null && haze.Running && (new Vector2(p.x, p.z) - haze.NextCentre).magnitude > haze.NextRadius) continue;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(p, out hit, 4f, NavMesh.AllAreas)) return hit.position;
            }
            return transform.position + Random.insideUnitSphere * 20f;
        }

        void GoTo(Vector3 p, bool hurry)
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.speed = hurry ? 6.2f : 4.2f;
            if (!agent.hasPath || (agent.destination - p).sqrMagnitude > 1f) agent.SetDestination(p);
        }

        // ------------------------------------------------------------------ acting

        void Act(float dt)
        {
            if (!agent.enabled) return;
            var l = me.loadout;

            // Unstick: if it barely moves while it has somewhere to go, pick something else.
            float moved = (transform.position - lastPos).magnitude;
            lastPos = transform.position;
            if (agent.hasPath && moved < 0.3f * dt) stuckTimer += dt;
            else stuckTimer = Mathf.Max(0f, stuckTimer - dt);
            if (stuckTimer > 5f)
            {
                if (goal != null) { goal.claimUntil = 0f; goal = null; }
                hasWander = false;
                if (agent.isOnNavMesh) agent.ResetPath();
                stuckTimer = 0f;
            }

            if (reloadLeft > 0f)
            {
                reloadLeft -= dt;
                if (reloadLeft <= 0f) l.Reload(l.Current);
            }

            // Pick things up when standing on them.
            if (goal != null && !goal.taken)
            {
                Vector3 d = goal.position - transform.position;
                if (Mathf.Abs(d.y) < 1.6f && new Vector2(d.x, d.z).sqrMagnitude < 1.6f * 1.6f) Take(goal);
            }

            if (Current == State.Fight) Fight(dt);
            else
            {
                agent.updateRotation = true;
                if (l.Current != null && l.Current.loaded < l.Current.def.magazine && reloadLeft <= 0f && l.CountFor(l.Current) > 0)
                    StartReload();
            }
        }

        void Take(LootDrop d)
        {
            var loot = LootDirector.Instance;
            var l = me.loadout;
            GunItem swapped;
            int old;
            int left = l.Take(d.kind, d.value, d.tier, d.count, l.Wants(d.kind, d.value, d.tier), out swapped, out old);
            if (left == d.count) { goal = null; return; }
            if (left > 0) d.count = left; else loot.Remove(d);
            if (swapped != null) loot.Drop(transform.position, ItemKind.Gun, swapped.def.id, swapped.tier, 1);
            if (old > 0) loot.Drop(transform.position, d.kind, old, (Tier)old, 1);
            if (l.selected < 0 || l.Current == null) l.selected = Mathf.Max(0, l.BestSlot(20f));
            goal = null;
            SynthAudio.Play3D(voice, "pickup", 0.3f);
        }

        void StartReload()
        {
            var g = me.loadout.Current;
            if (g == null || reloadLeft > 0f || me.loadout.CountFor(g) <= 0) return;
            reloadLeft = g.def.reloadTime * Random.Range(1f, 1.3f);
            SynthAudio.Play3D(voice, "reload", 0.5f);
        }

        void Fight(float dt)
        {
            if (enemy == null || !enemy.Alive) { Current = State.Wander; return; }
            var l = me.loadout;
            Vector3 eye = me.eye != null ? me.eye.position : transform.position + Vector3.up * 1.6f;
            Vector3 target = enemy.AimPoint;
            Vector3 to = target - eye;
            float dist = to.magnitude;

            // Face the enemy.
            agent.updateRotation = false;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), dt * (4f + skill * 6f));

            // Choose the gun for the range (not while reloading).
            if (reloadLeft <= 0f)
            {
                int best = l.BestSlot(dist);
                if (best >= 0 && best != l.selected)
                {
                    l.selected = best;
                    nextShot = Time.time + 0.4f;
                }
            }

            // Strafe sideways now and then; close in with a shotgun, keep distance with a rifle.
            strafeTimer -= dt;
            if (strafeTimer <= 0f)
            {
                strafeTimer = Random.Range(0.8f, 2f);
                var g = l.Current;
                float ideal = g == null ? 10f : g.def.gunClass == GunClass.Shotgun ? 6f : g.def.gunClass == GunClass.Marksman ? 60f : 22f;
                Vector3 side = Vector3.Cross(Vector3.up, flat.normalized) * (Random.value < 0.5f ? -1f : 1f) * Random.Range(2f, 5f);
                Vector3 toward = flat.normalized * Mathf.Clamp(dist - ideal, -6f, 6f);
                Vector3 dest = transform.position + side + toward;
                NavMeshHit hit;
                if (agent.isOnNavMesh && NavMesh.SamplePosition(dest, out hit, 3f, NavMesh.AllAreas))
                {
                    agent.speed = 3.6f;
                    agent.SetDestination(hit.position);
                }
            }

            var gun = l.Current;
            if (gun == null) return;
            if (gun.loaded <= 0)
            {
                StartReload();
                return;
            }
            if (reloadLeft > 0f) return;
            bool visible = Time.time - lastSeenTime < 0.4f;
            if (!visible) return;

            trackTime += dt;
            float reaction = Mathf.Lerp(0.95f, 0.3f, skill);
            if (trackTime < reaction || Time.time < nextShot) return;

            // Aim error shrinks as it keeps tracking; moving targets are harder.
            float settle = Mathf.Clamp01((trackTime - reaction) / 2.5f);
            float error = Mathf.Lerp(Mathf.Lerp(7f, 3f, skill), Mathf.Lerp(2.4f, 0.6f, skill), settle);
            float spread = gun.def.hipSpread * 0.6f * RoyaleItems.TierSpread(gun.tier) + error;
            if (gun.def.gunClass == GunClass.Marksman) spread = gun.def.aimSpread + error * 0.6f;
            // Aim for the head now and then.
            if (Random.value < 0.08f + skill * 0.12f) target = enemy.eye != null ? enemy.eye.position : target + Vector3.up * 0.35f;
            Vector3 dir = (target - eye).normalized;
            gun.loaded--;
            Ballistics.Fire(me, gun, eye, dir, spread);
            if (body != null) body.Kick();

            if (gun.def.automatic)
            {
                if (burstLeft <= 0) burstLeft = Random.Range(3, 7);
                burstLeft--;
                nextShot = Time.time + gun.def.interval * Random.Range(1f, 1.4f) + (burstLeft == 0 ? Random.Range(0.25f, 0.6f) : 0f);
            }
            else nextShot = Time.time + gun.def.interval * Random.Range(1.3f, 2.2f);
        }

        void Animate()
        {
            if (body == null) return;
            var l = me.loadout;
            body.speed = agent.enabled ? agent.velocity.magnitude : 0f;
            body.armed = l.Current != null;
            body.aiming = Current == State.Fight;
            if (Current == State.Fight && enemy != null)
            {
                Vector3 to = enemy.AimPoint - (me.eye != null ? me.eye.position : transform.position);
                body.aimPitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            }
            else body.aimPitch = 0f;
        }
    }
}
