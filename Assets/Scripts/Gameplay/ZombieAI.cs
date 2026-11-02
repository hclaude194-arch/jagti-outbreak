using UnityEngine;
using UnityEngine.AI;

namespace Jagti.Gameplay
{
    /// <summary>
    /// Zombie behaviour on the baked campus NavMesh.
    /// Wander → (hears a noise) Investigate → (sees you) Chase → Attack. Losing sight sends it
    /// to where it last saw you. Sight is a cone limited by range (longer when your torch is on);
    /// hearing comes from NoiseEvents. Without a NavMesh it falls back to walking straight over the terrain.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class ZombieAI : MonoBehaviour
    {
        public enum State { Wander, Investigate, Chase, Attack, Stagger, Dead }

        [Header("Movement")]
        public float wanderSpeed = 1.0f;
        public float chaseSpeed = 3.4f;
        public float wanderRadius = 14f;

        [Header("Senses")]
        public float sightRange = 26f;
        public float sightRangeTorch = 42f;
        public float fieldOfView = 120f;
        public float closeSense = 3.5f;      // notices you this close whatever the angle
        public float hearingScale = 1f;
        public float forgetTime = 6f;

        [Header("Attack")]
        public float attackRange = 1.6f;
        public float attackDamage = 12f;
        public float attackInterval = 1.4f;
        public float windup = 0.45f;

        public State CurrentState { get { return state; } }
        public System.Action<ZombieAI> OnDied;
        public System.Action<ZombieAI> OnFinished;  // body has sunk away; spawner can reuse it

        NavMeshAgent agent;
        Health health;
        ZombieBody body;
        AudioSource voice;
        Transform player;
        Health playerHealth;
        Light playerTorch;
        Collider[] colliders;

        State state;
        float senseTimer;
        float stateTimer;
        float attackTimer;
        float lastSeen;
        Vector3 lastKnown;
        float groanTimer;
        float deadTimer;
        bool useNavMesh;
        Vector3 fallbackTarget;
        bool attackPending;
        float wanderPause = -1f;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            body = GetComponentInChildren<ZombieBody>();
            voice = GetComponent<AudioSource>();
            colliders = GetComponentsInChildren<Collider>();
        }

        void OnEnable()
        {
            NoiseEvents.Heard += OnNoise;
            health.Died += Die;
            health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            NoiseEvents.Heard -= OnNoise;
            health.Died -= Die;
            health.Damaged -= OnDamaged;
        }

        /// <summary>Called by the spawner each time this zombie is (re)used.</summary>
        public void Activate(Vector3 position, Transform target, float maxHealth)
        {
            player = target;
            playerHealth = target != null ? target.GetComponent<Health>() : null;
            var fpc = target != null ? target.GetComponent<Jagti.Player.FirstPersonController>() : null;
            playerTorch = fpc != null ? fpc.flashlight : null;

            health.ResetHealth(maxHealth);
            foreach (var c in colliders) c.enabled = true;
            if (body != null) body.ResetPose();

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            // Enable the agent only on the NavMesh; enabling it off the mesh logs an error every time.
            NavMeshHit hit;
            useNavMesh = NavMesh.SamplePosition(position, out hit, 2f, NavMesh.AllAreas);
            if (useNavMesh)
            {
                transform.position = hit.position;
                agent.enabled = true;
                useNavMesh = agent.Warp(hit.position) && agent.isOnNavMesh;
            }
            if (!useNavMesh) agent.enabled = false;
            fallbackTarget = position;

            senseTimer = Random.value * 0.25f;
            groanTimer = Random.Range(2f, 10f);
            attackTimer = 0f;
            attackPending = false;
            lastSeen = -999f;
            SetState(State.Wander);
        }

        void SetState(State s)
        {
            state = s;
            stateTimer = 0f;
            if (!useNavMesh) return;
            switch (s)
            {
                case State.Wander:
                case State.Investigate:
                    agent.speed = wanderSpeed * (s == State.Investigate ? 1.4f : 1f);
                    agent.isStopped = false;
                    break;
                case State.Chase:
                    agent.speed = chaseSpeed;
                    agent.isStopped = false;
                    break;
                default:
                    agent.isStopped = true;
                    break;
            }
        }

        void Update()
        {
            if (state == State.Dead)
            {
                if (body != null) body.Animate(0f, false);
                deadTimer += Time.deltaTime;
                // Lie for a while, then sink into the ground and hand back to the pool.
                if (deadTimer > 12f) transform.position += Vector3.down * Time.deltaTime * 0.4f;
                if (deadTimer > 16f && OnFinished != null) OnFinished(this);
                return;
            }

            stateTimer += Time.deltaTime;
            attackTimer -= Time.deltaTime;
            senseTimer -= Time.deltaTime;
            if (senseTimer <= 0f)
            {
                senseTimer = 0.25f;
                Sense();
            }

            switch (state)
            {
                case State.Wander: UpdateWander(); break;
                case State.Investigate: UpdateInvestigate(); break;
                case State.Chase: UpdateChase(); break;
                case State.Attack: UpdateAttack(); break;
                case State.Stagger:
                    if (stateTimer > 0.6f) SetState(player != null && Time.time - lastSeen < forgetTime ? State.Chase : State.Wander);
                    break;
            }

            if (!useNavMesh) FallbackMove();

            float speed = useNavMesh ? agent.velocity.magnitude : fallbackSpeed;
            if (body != null) body.Animate(speed, state == State.Chase || state == State.Attack);

            groanTimer -= Time.deltaTime;
            if (groanTimer <= 0f)
            {
                groanTimer = state == State.Chase ? Random.Range(2f, 4f) : Random.Range(6f, 14f);
                SynthAudio.Play3D(voice, "groan" + Random.Range(1, 4), 0.8f);
            }
        }

        // ------------------------------------------------------------------ senses

        void Sense()
        {
            if (player == null || GameState.PlayerDead) return;
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 target = player.position + Vector3.up * 1.5f;
            Vector3 to = target - eye;
            float dist = to.magnitude;

            bool torch = playerTorch != null && playerTorch.enabled;
            float range = torch ? sightRangeTorch : sightRange;
            bool canSee = false;
            if (dist <= closeSense) canSee = true;
            else if (dist <= range && Vector3.Angle(transform.forward, to) <= fieldOfView * 0.5f) canSee = LineOfSight(eye, to, dist);

            if (canSee)
            {
                bool wasHunting = state == State.Chase || state == State.Attack;
                lastSeen = Time.time;
                lastKnown = player.position;
                if (!wasHunting && state != State.Stagger)
                {
                    SynthAudio.Play3D(voice, "zombie_alert", 1f);
                    SetState(State.Chase);
                }
            }
        }

        bool LineOfSight(Vector3 eye, Vector3 to, float dist)
        {
            var hits = Physics.RaycastAll(eye, to / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            float nearestBlock = float.MaxValue;
            float playerHit = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.transform.IsChildOf(transform)) continue;
                if (h.transform.IsChildOf(player)) playerHit = Mathf.Min(playerHit, h.distance);
                else if (h.collider.GetComponentInParent<ZombieAI>() == null) nearestBlock = Mathf.Min(nearestBlock, h.distance);
            }
            // Clear if nothing solid stands between us (the player's capsule may or may not be hit).
            return nearestBlock > Mathf.Min(playerHit, dist - 0.5f);
        }

        void OnNoise(Vector3 position, float radius)
        {
            if (state == State.Dead || state == State.Chase || state == State.Attack) return;
            float r = radius * hearingScale;
            if ((position - transform.position).sqrMagnitude > r * r) return;
            lastKnown = position;
            SetState(State.Investigate);
            MoveTo(position + Random.insideUnitSphere * 2f);
        }

        void OnDamaged(float amount, Vector3 from)
        {
            if (state == State.Dead) return;
            // Getting hurt always reveals where it came from.
            if (player != null)
            {
                lastSeen = Time.time;
                lastKnown = player.position;
            }
            if (amount >= 30f && state != State.Stagger)
            {
                SetState(State.Stagger);
                Vector3 push = transform.position - from;
                push.y = 0f;
                if (useNavMesh && push.sqrMagnitude > 0.01f) agent.Move(push.normalized * 0.35f);
            }
            else if (state != State.Chase && state != State.Attack && state != State.Stagger) SetState(State.Chase);
        }

        // ------------------------------------------------------------------ states

        void UpdateWander()
        {
            if (stateTimer > 0.5f && ArrivedOrStuck())
            {
                // Pause, then pick a new spot nearby.
                if (wanderPause < 0f) wanderPause = stateTimer + Random.Range(2f, 7f);
                if (stateTimer > wanderPause)
                {
                    wanderPause = -1f;
                    Vector3 p = transform.position + Random.insideUnitSphere * wanderRadius;
                    MoveTo(p);
                    stateTimer = 0f;
                }
            }
        }

        void UpdateInvestigate()
        {
            if (stateTimer > 1f && ArrivedOrStuck())
            {
                if (stateTimer > 5f) SetState(State.Wander);
            }
            if (stateTimer > 25f) SetState(State.Wander);
        }

        void UpdateChase()
        {
            if (player == null || GameState.PlayerDead)
            {
                SetState(State.Wander);
                return;
            }
            bool fresh = Time.time - lastSeen < 0.6f;
            Vector3 goal = fresh ? player.position : lastKnown;
            MoveTo(goal);

            Vector3 flat = player.position - transform.position;
            flat.y = 0f;
            if (fresh && flat.magnitude <= attackRange && Mathf.Abs(player.position.y - transform.position.y) < 1.8f)
            {
                SetState(State.Attack);
                return;
            }
            if (Time.time - lastSeen > forgetTime)
            {
                SetState(State.Investigate);
                MoveTo(lastKnown);
            }
        }

        void UpdateAttack()
        {
            if (player == null || GameState.PlayerDead)
            {
                SetState(State.Wander);
                return;
            }
            Vector3 flat = player.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), Time.deltaTime * 8f);

            if (!attackPending && attackTimer <= 0f)
            {
                attackPending = true;
                attackTimer = attackInterval;
                stateTimer = 0f;
                if (body != null) body.PlayAttack();
                SynthAudio.Play3D(voice, "zombie_attack", 1f);
            }
            if (attackPending && stateTimer >= windup)
            {
                attackPending = false;
                // The swing lands only if you are still in reach.
                if (flat.magnitude <= attackRange + 0.5f && playerHealth != null)
                    playerHealth.Damage(attackDamage * Random.Range(0.8f, 1.2f), transform.position);
            }
            if (!attackPending && flat.magnitude > attackRange + 0.4f) SetState(State.Chase);
        }

        void Die()
        {
            SetState(State.Dead);
            if (useNavMesh) agent.enabled = false;
            foreach (var c in colliders) c.enabled = false;
            if (body != null) body.PlayDeath();
            SynthAudio.Play3D(voice, "zombie_death", 1f);
            deadTimer = 0f;
            GameState.Kills++;
            if (OnDied != null) OnDied(this);
        }

        // ------------------------------------------------------------------ movement

        void MoveTo(Vector3 p)
        {
            if (useNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(p, out hit, 4f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
            }
            else fallbackTarget = p;
        }

        bool ArrivedOrStuck()
        {
            if (!useNavMesh) return (fallbackTarget - transform.position).sqrMagnitude < 1f;
            if (agent.pathPending) return false;
            return agent.remainingDistance <= agent.stoppingDistance + 0.3f || agent.velocity.sqrMagnitude < 0.01f;
        }

        float fallbackSpeed;

        // Straight-line walking over the terrain, used only if the NavMesh is missing.
        void FallbackMove()
        {
            fallbackSpeed = 0f;
            if (state == State.Dead || state == State.Attack || state == State.Stagger) return;
            Vector3 to = fallbackTarget - transform.position;
            to.y = 0f;
            if (to.magnitude < 0.8f) return;
            float speed = state == State.Chase ? chaseSpeed : wanderSpeed;
            Vector3 step = to.normalized * speed * Time.deltaTime;
            Vector3 next = transform.position + step;
            var terrain = Terrain.activeTerrain;
            if (terrain != null) next.y = terrain.SampleHeight(next) + terrain.transform.position.y;
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 5f);
            fallbackSpeed = speed;
        }
    }
}
