using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace Jagti.Royale
{
    /// <summary>
    /// A computer opponent. It rides the sky bus, jumps near a building it picked, glides down,
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

        public BotDifficulty.Settings tune = BotDifficulty.Get(BotDifficulty.Level.Regular);

        public State Current { get; private set; }
        public bool Crouched { get { return crouch > 0.5f; } }

        enum Cover { None, Moving, Hiding, Peeking }

        Combatant me;
        NavMeshAgent agent;
        SkyBus heli;
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
        LootStash stashGoal;
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

        // Cover and stance.
        Cover cover;
        CoverFinder.Spot spot;
        float coverUntil, coverTimer, nextCoverTry;
        bool wantCover;
        float healthAtPeek, hideStart;
        float crouch, crouchTarget, hitboxCrouch = -1f;
        bool longShot;
        float suppression;
        float grenadeCooldown;
        float lastVisibleTime = -99f;
        BoxCollider bodyBox;
        Transform headBox;

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
            Jagti.Audio.AudioBuses.Route(voice, Jagti.Audio.Bus.World);
            bodyBox = GetComponent<BoxCollider>();
            headBox = transform.Find("Head Hitbox");
        }

        void OnEnable()
        {
            NoiseEvents.Heard += OnHeard;
            me.Health.Damaged += OnDamaged;
            me.Health.Died += OnDied;
            me.UnderFire = OnUnderFire;
        }

        void OnDisable()
        {
            NoiseEvents.Heard -= OnHeard;
            me.Health.Damaged -= OnDamaged;
            me.Health.Died -= OnDied;
            me.UnderFire = null;
        }

        /// <summary>Sets the bot aboard the sky bus with its chosen landing spot.</summary>
        public void Board(SkyBus h, HazeZone zone, Vector3 target, float jumpAt)
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
            radius *= tune.hearing;
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
            suppression = Mathf.Min(2f, suppression + amount / 30f);
            if (body != null) body.Flinch(from);
            // A fight going badly: get behind something.
            if (cover == Cover.None && me.Health.current < 75f && Random.value < tune.cover) wantCover = true;
            if (cover == Cover.Peeking && healthAtPeek - me.Health.current > 20f) EndPeek();
        }

        /// <summary>A shot passed close by: duck, look that way, and maybe find cover.</summary>
        void OnUnderFire(Combatant shooter)
        {
            if (Current == State.Dead || !me.Landed) return;
            suppression = Mathf.Min(2f, suppression + 0.15f);
            if (shooter == null || !shooter.Alive) return;
            if (enemy == null || !enemy.Alive || Time.time - lastSeenTime > 2f)
            {
                enemy = shooter;
                // Only a rough idea of where it came from.
                lastSeen = shooter.transform.position + Random.insideUnitSphere * 6f;
                lastSeenTime = Time.time - 0.5f;
            }
            if (cover == Cover.None && Time.time - lastVisibleTime > 0.5f && Random.value < tune.cover * 0.5f) wantCover = true;
            if (cover == Cover.Peeking && Random.value < 0.3f) EndPeek();
        }

        void OnDied()
        {
            Current = State.Dead;
            if (agent.enabled) agent.enabled = false;
            if (body != null) body.Die(me.LastHitFrom);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (LootDirector.Instance != null) LootDirector.Instance.DropLoadout(me.loadout, transform.position);
            SynthAudio.Play3D(voice, "death_grunt", 0.8f);
            Jagti.Audio.SoundSystem.CancelLater(GetInstanceID());
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
            if (GameState.Paused && Time.timeScale == 0f) return;   // online the match goes on behind the menu

            thinkTimer -= dt;
            grenadeCooldown -= dt;
            suppression = Mathf.MoveTowards(suppression, 0f, dt * 0.6f);
            if (thinkTimer <= 0f && BotScheduler.TryThink())
            {
                var cam = Camera.main;
                bool nearPlayer = cam == null || (cam.transform.position - transform.position).sqrMagnitude < 200f * 200f;
                thinkTimer = (nearPlayer ? 0.25f : 0.7f) * Random.Range(0.85f, 1.15f);
                var avoid = nearPlayer ? ObstacleAvoidanceType.LowQualityObstacleAvoidance : ObstacleAvoidanceType.NoObstacleAvoidance;
                if (agent.obstacleAvoidanceType != avoid) agent.obstacleAvoidanceType = avoid;
                Think();
            }
            Act(dt);
            Stance(dt);
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

            bool inCover = cover != Cover.None && Time.time < coverUntil;
            if (armed && enemy != null && enemy.Alive && (enemyVisible || (inCover && Time.time - lastSeenTime < 6f)))
            {
                if (Current != State.Fight)
                {
                    trackTime = 0f;
                    burstLeft = 0;
                    // Opening a fight: the careful ones head for cover first.
                    if (Random.value < tune.cover * (0.4f + skill * 0.6f)) wantCover = true;
                }
                Current = State.Fight;
                if (!enemyVisible) ConsiderGrenade();
                return;
            }
            if (cover != Cover.None) LeaveCover();
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
                ConsiderGrenade();
                // Close in carefully: crouch-walk the last stretch.
                GoTo(lastSeen, (lastSeen - transform.position).sqrMagnitude > 20f * 20f);
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
                GoTo(heardAt + Random.insideUnitSphere * 6f, (heardAt - transform.position).sqrMagnitude > 50f * 50f);
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
            float range = (70f + skill * 60f) * tune.sight;
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
                lastVisibleTime = Time.time;
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
            // A trunk within reach gets opened on the way past.
            var reach = loot.NearestClosedStash(transform.position, 1.8f);
            if (reach != null)
            {
                loot.Open(reach);
                if (reach == stashGoal) stashGoal = null;
            }
            if (stashGoal != null && !stashGoal.open && stashGoal.claimedBy == me && Time.time < stashGoal.claimUntil)
            {
                GoTo(stashGoal.position + stashGoal.Front * 0.8f, false);
                return true;
            }
            stashGoal = null;
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
            // A closed trunk nearby is worth about as much as a good gun.
            var st = loot.NearestClosedStash(transform.position, 25f);
            if (st != null && !(st.claimedBy != null && st.claimedBy != me && st.claimedBy.Alive && Time.time < st.claimUntil)
                && (haze == null || haze.IsInside(st.position)))
            {
                float score = 16f / (4f + (st.position - transform.position).magnitude);
                if (score > best)
                {
                    goal = null;
                    stashGoal = st;
                    st.claimedBy = me;
                    st.claimUntil = Time.time + 20f;
                    GoTo(st.position + st.Front * 0.8f, false);
                    return true;
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
                case ItemKind.Smoke: return 1f;
                case ItemKind.Attachment: return 2f + (d.value == 2 || d.value == 5 || d.value == 7 ? 2f : 0f);
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
            // Creep near a fight or a noise.
            bool careful = !hurry && (Current == State.Chase || Current == State.Investigate) &&
                           ((Current == State.Chase ? lastSeen : heardAt) - transform.position).sqrMagnitude < 25f * 25f;
            crouchTarget = careful ? 1f : 0f;
            if (careful) agent.speed = 2.2f;
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
                if (l.Current != null && l.Current.loaded < l.Current.Magazine && reloadLeft <= 0f && l.CountFor(l.Current) > 0)
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
            loot.SetCount(d, left);
            if (swapped != null) loot.DropGun(transform.position, swapped);
            if (old > 0) loot.DropReplaced(transform.position, d.kind, old);
            if (l.selected < 0 || l.Current == null) l.selected = Mathf.Max(0, l.BestSlot(20f));
            goal = null;
            SynthAudio.Play3D(voice, "pickup", 0.3f);
        }

        void StartReload()
        {
            var g = me.loadout.Current;
            if (g == null || reloadLeft > 0f || me.loadout.CountFor(g) <= 0) return;
            reloadLeft = g.def.reloadTime * Random.Range(1f, 1.3f);
            GunAudio.Reload(g.def, reloadLeft, g.Magazine - g.loaded, transform.position + Vector3.up * 1.2f, GetInstanceID(), 0.8f);
        }

        void Fight(float dt)
        {
            if (enemy == null || !enemy.Alive) { LeaveCover(); Current = State.Wander; return; }
            var l = me.loadout;
            Vector3 eye = me.eye != null ? me.eye.position : transform.position + Vector3.up * 1.6f;
            Vector3 target = enemy.AimPoint;
            bool visible = Time.time - lastSeenTime < 0.4f;
            Vector3 to = (visible ? target : lastSeen + Vector3.up * 1.2f) - eye;
            float dist = to.magnitude;

            // Face the enemy (or where it was last seen).
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
            var gun = l.Current;

            // Look for cover when asked to (shared out between bots: a few searches per frame).
            if (wantCover && cover == Cover.None && Time.time >= nextCoverTry && dist > 7f && BotScheduler.TrySearch())
            {
                wantCover = false;
                Vector3 threatEye = enemy.eye != null ? enemy.eye.position : enemy.transform.position + Vector3.up * 1.6f;
                CoverFinder.Spot found;
                if (CoverFinder.Find(transform.position, threatEye, me, enemy, out found))
                {
                    spot = found;
                    cover = Cover.Moving;
                    coverUntil = Time.time + Random.Range(9f, 16f);
                    longShot = false;
                    nextCoverTry = Time.time + 5f;
                }
                else
                {
                    nextCoverTry = Time.time + 2.5f;
                    // Caught in the open and hurt: hide in smoke instead.
                    if (me.Health.current < 55f) ThrowSmoke();
                }
            }
            if (cover != Cover.None && (Time.time > coverUntil || dist < 6f)) LeaveCover();

            switch (cover)
            {
                case Cover.None: Strafe(dt, flat, dist, gun); break;
                case Cover.Moving:
                    crouchTarget = 0f;
                    if (agent.isOnNavMesh)
                    {
                        agent.speed = 6f;
                        if ((agent.destination - spot.position).sqrMagnitude > 0.25f) agent.SetDestination(spot.position);
                    }
                    if ((transform.position - spot.position).sqrMagnitude < 0.7f * 0.7f) Hide(Random.Range(0.6f, 1.6f));
                    else if (stuckTimer > 3f) LeaveCover();
                    break;
                case Cover.Hiding:
                    crouchTarget = spot.low ? 1f : 0.55f;
                    coverTimer -= dt;
                    // Seen while hiding: the cover is no good from here.
                    if (visible && Time.time - lastVisibleTime < 0.3f && Time.time - hideStart > 0.8f && (!spot.low || crouch > 0.8f))
                    {
                        LeaveCover();
                        wantCover = true;
                        nextCoverTry = 0f;
                        break;
                    }
                    if (gun != null && gun.loaded < gun.Magazine && reloadLeft <= 0f && l.CountFor(gun) > 0) StartReload();
                    if (me.Health.current < 65f && !me.IsHealing && Time.time - me.LastHitTime > 1.2f)
                        TryHeal();
                    bool busy = reloadLeft > 0f || me.IsHealing;
                    if (coverTimer <= 0f && !busy && spot.canShoot) Peek();
                    break;
                case Cover.Peeking:
                    crouchTarget = 0f;
                    coverTimer -= dt;
                    if (!spot.low && agent.isOnNavMesh && (agent.destination - spot.peek).sqrMagnitude > 0.25f)
                    {
                        agent.speed = 3f;
                        agent.SetDestination(spot.peek);
                    }
                    if (coverTimer <= 0f || gun == null || gun.loaded <= 0) EndPeek();
                    break;
            }

            if (gun == null) return;
            if (gun.loaded <= 0)
            {
                StartReload();
                if (cover == Cover.None && Random.value < tune.cover * dt * 2f) wantCover = true;
                return;
            }
            if (reloadLeft > 0f || me.IsHealing || !visible || cover == Cover.Hiding) return;

            trackTime += dt;
            float reaction = Mathf.Lerp(0.95f, 0.3f, skill) * tune.reaction;
            if (trackTime < reaction || Time.time < nextShot) return;

            // Aim error shrinks as it keeps tracking; being shot or shot at makes it worse,
            // crouching steadies it, running wrecks it.
            float settle = Mathf.Clamp01((trackTime - reaction) / 2.5f);
            float error = Mathf.Lerp(Mathf.Lerp(7f, 3f, skill), Mathf.Lerp(2.4f, 0.6f, skill), settle) * tune.aim;
            error *= 1f + suppression * 0.6f;
            if (crouch > 0.7f) error *= 0.75f;
            if (cover == Cover.Moving) error *= 1.8f;
            float spread = gun.def.hipSpread * 0.6f * RoyaleItems.TierSpread(gun.tier) + error;
            if (gun.def.gunClass == GunClass.Marksman) spread = gun.def.aimSpread + error * 0.6f;
            // Aim for the head now and then.
            if (Random.value < 0.08f + skill * 0.12f) target = enemy.eye != null ? enemy.eye.position : target + Vector3.up * 0.35f;
            Vector3 dir = (target - eye).normalized;
            gun.loaded--;
            Ballistics.Fire(me, gun, eye, dir, spread);
            if (body != null) body.Kick();

            if (gun.def.burst > 1)
            {
                if (burstLeft <= 0) burstLeft = gun.def.burst;
                burstLeft--;
                nextShot = Time.time + (burstLeft == 0 ? gun.def.burstGap * Random.Range(1.2f, 2f) : gun.def.interval);
            }
            else if (gun.def.automatic)
            {
                if (burstLeft <= 0) burstLeft = Random.Range(3, 7);
                burstLeft--;
                nextShot = Time.time + gun.def.interval * Random.Range(1f, 1.4f) + (burstLeft == 0 ? Random.Range(0.25f, 0.6f) : 0f);
            }
            else nextShot = Time.time + gun.def.interval * Random.Range(1.3f, 2.2f);
        }

        /// <summary>The open-ground fight: strafe, keep the gun's range, crouch for long shots.</summary>
        void Strafe(float dt, Vector3 flat, float dist, GunItem g)
        {
            strafeTimer -= dt;
            if (strafeTimer > 0f) return;
            strafeTimer = Random.Range(0.8f, 2f);
            bool rifle = g != null && (g.def.gunClass == GunClass.Marksman || g.def.gunClass == GunClass.Rifle);
            // At long range a steady crouched shot beats dancing about.
            longShot = rifle && dist > 40f && Random.value < 0.4f + skill * 0.4f;
            crouchTarget = longShot ? 1f : (Random.value < 0.15f * skill ? 1f : 0f);
            if (longShot)
            {
                if (agent.isOnNavMesh) agent.ResetPath();
                return;
            }
            float ideal = g == null ? 10f : g.def.gunClass == GunClass.Shotgun ? 6f : g.def.gunClass == GunClass.Marksman ? 60f : 22f;
            Vector3 side = Vector3.Cross(Vector3.up, flat.normalized) * (Random.value < 0.5f ? -1f : 1f) * Random.Range(2f, 5f);
            Vector3 toward = flat.normalized * Mathf.Clamp(dist - ideal, -6f, 6f);
            Vector3 dest = transform.position + side + toward;
            NavMeshHit hit;
            if (agent.isOnNavMesh && NavMesh.SamplePosition(dest, out hit, 3f, NavMesh.AllAreas))
            {
                agent.speed = crouchTarget > 0.5f ? 2.2f : 3.6f;
                agent.SetDestination(hit.position);
            }
        }

        void Hide(float time)
        {
            cover = Cover.Hiding;
            coverTimer = time;
            hideStart = Time.time;
            if (agent.isOnNavMesh)
            {
                if (spot.low || (transform.position - spot.position).sqrMagnitude < 0.5f) agent.ResetPath();
                else agent.SetDestination(spot.position);
            }
        }

        void Peek()
        {
            cover = Cover.Peeking;
            coverTimer = Random.Range(1.1f, 2.6f) * Mathf.Lerp(1.2f, 0.8f, skill);
            healthAtPeek = me.Health.current;
            trackTime = Mathf.Min(trackTime, 0.2f);   // re-acquire after stepping out
        }

        void EndPeek()
        {
            if (cover != Cover.Peeking) return;
            Hide(Random.Range(0.7f, 2f));
        }

        void LeaveCover()
        {
            cover = Cover.None;
            crouchTarget = 0f;
        }

        void TryHeal()
        {
            var l = me.loadout;
            float health = me.Health.current;
            if (health < 45f && l.medkits > 0 && me.StartHeal(ItemKind.Medkit)) return;
            if (health < RoyaleItems.BandageCap && me.StartHeal(ItemKind.Bandage)) return;
            if (me.StartHeal(ItemKind.Medkit)) return;
            me.StartHeal(ItemKind.ChaiFlask);
        }

        /// <summary>Drops a smoke grenade between itself and the enemy to break line of sight.</summary>
        void ThrowSmoke()
        {
            var l = me.loadout;
            var director = MatchDirector.Instance;
            if (l.smokes <= 0 || director == null || director.smokeTemplate == null || enemy == null) return;
            if (Random.value > tune.cover) return;
            l.smokes--;
            Vector3 toward = (enemy.transform.position - transform.position);
            toward.y = 0f;
            Vector3 from = transform.position + Vector3.up * 1.4f + transform.forward * 0.4f;
            Grenade.Launch(director.smokeTemplate, from, toward.normalized * 5f + Vector3.up * 2.5f, me);
            SynthAudio.Play3D(voice, "swing", 0.5f);
            wantCover = false;
            nextCoverTry = Time.time + 1.5f;
        }

        /// <summary>Lobs a grenade at an enemy hiding nearby, now and then.</summary>
        void ConsiderGrenade()
        {
            var l = me.loadout;
            if (l.grenades <= 0 || grenadeCooldown > 0f || enemy == null) return;
            float hidden = Time.time - lastSeenTime;
            if (hidden < 1.2f || hidden > 7f) return;
            Vector3 eye = me.eye != null ? me.eye.position : transform.position + Vector3.up * 1.6f;
            float d = (lastSeen - transform.position).magnitude;
            if (d < 9f || d > 32f) return;
            grenadeCooldown = 4f;
            if (Random.value > tune.grenades * (0.5f + skill)) return;
            var director = MatchDirector.Instance;
            if (director == null || director.grenadeTemplate == null) return;

            // Indoors there is no room for a high lob.
            bool roof = Physics.Raycast(eye, Vector3.up, 5f, ~0, QueryTriggerInteraction.Ignore);
            Vector3 aim = lastSeen + Random.insideUnitSphere * Mathf.Lerp(3.5f, 1f, skill) * tune.aim;
            aim.y = lastSeen.y + 0.2f;
            Vector3 from = eye + transform.forward * 0.5f;
            Vector3 v;
            if (!CoverFinder.Lob(from, aim, roof ? 14f : 40f, out v) && !CoverFinder.Lob(from, aim, roof ? 25f : 55f, out v)) return;
            l.grenades--;
            grenadeCooldown = Random.Range(8f, 14f);
            Grenade.Launch(director.grenadeTemplate, from, v, me);
            SynthAudio.Play3D(voice, "swing", 0.5f);
            if (body != null) body.Kick();
        }

        /// <summary>Crouching moves the eye and the hit areas down with the body.</summary>
        void Stance(float dt)
        {
            if (Current == State.Heal) crouchTarget = 1f;
            crouch = Mathf.MoveTowards(crouch, crouchTarget, dt * 4f);
            if (Mathf.Abs(crouch - hitboxCrouch) < 0.04f) return;
            hitboxCrouch = crouch;
            float drop = crouch * 0.5f;
            if (me.eye != null) me.eye.localPosition = new Vector3(0f, 1.62f - drop, 0f);
            if (headBox != null) headBox.localPosition = new Vector3(0f, 1.72f - drop, 0.04f * crouch);
            if (bodyBox != null)
            {
                bodyBox.center = new Vector3(0f, 0.8f - drop * 0.5f, 0f);
                bodyBox.size = new Vector3(0.5f, 1.45f - drop, 0.32f + crouch * 0.12f);
            }
            if (agent.enabled && crouch > 0.5f && agent.speed > 2.4f) agent.speed = 2.4f;
        }

        void Animate()
        {
            if (body == null) return;
            var l = me.loadout;
            body.speed = agent.enabled ? new Vector3(agent.velocity.x, 0f, agent.velocity.z).magnitude : 0f;
            body.armed = l.Current != null;
            body.ShowGun(l.Current != null ? l.Current.def.id : -1);
            body.Dress(l.Current);
            body.ShowGear(l.vest, l.helmet, l.backpack);
            body.crouch = crouch;
            body.aiming = Current == State.Fight && cover != Cover.Hiding && cover != Cover.Moving && reloadLeft <= 0f && !me.IsHealing;
            if (Current == State.Fight && enemy != null)
            {
                Vector3 to = enemy.AimPoint - (me.eye != null ? me.eye.position : transform.position);
                body.aimPitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            }
            else body.aimPitch = 0f;
        }
    }
}
