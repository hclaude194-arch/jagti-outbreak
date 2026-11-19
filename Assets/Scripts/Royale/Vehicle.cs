using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace Jagti.Royale
{
    /// <summary>
    /// A scooter, utility cart, auto-rickshaw or patrol 4x4 you can ride (E near it; E again to get
    /// off). W/S throttle and reverse, A/D steer, Space brakes, Shift boosts (from a tank that refills),
    /// H sounds the horn. Arcade handling like the campus bus: the body follows the
    /// ground under its wheels, leans into turns (scooter), slows on climbs, flies off terrace
    /// edges and lands hard. It stops at walls (and is damaged by a fast crash), knocks over
    /// anyone it hits at speed, and can be shot or blown up: at zero health it bursts into flames,
    /// hurting whoever is near. Engine noise carries, so bots hear you coming.
    /// </summary>
    public class Vehicle : MonoBehaviour
    {
        public enum Kind { Scooter, Cart, Auto, Jeep }

        public static readonly List<Vehicle> All = new List<Vehicle>();

        public Kind kind;
        public Material[] palette;
        public Material smokeMaterial;
        public int paint = LootVisuals.Blue;

        // Filled by VehicleModels.
        [HideInInspector] public Transform[] wheels;
        [HideInInspector] public Transform[] steeredWheels;
        [HideInInspector] public Transform steerPivot;
        [HideInInspector] public float wheelRadius, wheelBase, frontAxle, halfTrack;
        [HideInInspector] public Vector3 bodyHalf, seatLocal, handL, handR, footLocal;

        public float maxSpeed = 22f;
        public float maxReverse = 4f;
        public float acceleration = 7f;
        public float maxSteer = 34f;
        public float maxHealth = 260f;

        public float Health { get; private set; }
        /// <summary>The boost tank, 0 to 1. Shift spends it; it refills when not boosting.</summary>
        public float Boost { get; private set; }
        public bool Boosting { get; private set; }
        public float Speed { get; private set; }
        public bool Wrecked { get; private set; }
        public Combatant Driver { get; private set; }
        public string DisplayName
        {
            get
            {
                switch (kind)
                {
                    case Kind.Scooter: return "scooter";
                    case Kind.Auto: return "auto-rickshaw";
                    case Kind.Jeep: return "patrol 4x4";
                    default: return "utility cart";
                }
            }
        }
        /// <summary>The seat: a rider's root sits here (0.8 m below the cushion, so the head is at a sitting height).</summary>
        public Transform Seat { get; private set; }

        float steer, vy, lean, noiseT, crashCooldown, hornT;
        bool boostLocked;
        Combatant lastRunOver;
        float lastRunOverTime;
        bool airborne;
        AudioSource engine;
        BoxCollider box;
        NavMeshObstacle obstacle;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Start()
        {
            if (kind == Kind.Cart)
            {
                maxSpeed = 15f;
                acceleration = 5f;
                maxSteer = 32f;
                maxHealth = 420f;
            }
            else if (kind == Kind.Auto)
            {
                // Nippy and light: quick off the line, not fast, tips into tight turns.
                maxSpeed = 17f;
                acceleration = 6.5f;
                maxSteer = 38f;
                maxHealth = 340f;
            }
            else if (kind == Kind.Jeep)
            {
                // The fastest and toughest, but wide and slow to turn.
                maxSpeed = 27f;
                acceleration = 8f;
                maxSteer = 30f;
                maxHealth = 640f;
            }
            Health = maxHealth;
            Boost = 1f;
            VehicleModels.Build(this, palette, paint);

            box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, bodyHalf.y, 0f);
            box.size = bodyHalf * 2f;
            obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size + new Vector3(0.2f, 0f, 0.2f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            var seat = new GameObject("Seat").transform;
            seat.SetParent(transform, false);
            seat.localPosition = seatLocal - new Vector3(0f, 0.8f, 0f);
            Seat = seat;

            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = SynthAudio.Get(kind == Kind.Scooter ? "engine_scooter" : kind == Kind.Auto ? "engine_auto" : kind == Kind.Jeep ? "engine_jeep" : "engine_cart");
            engine.loop = true;
            engine.spatialBlend = 1f;
            engine.minDistance = 4f;
            engine.maxDistance = 90f;
            engine.rolloffMode = AudioRolloffMode.Linear;
            engine.volume = 0f;
            engine.playOnAwake = false;
            SettleOnGround();
        }

        // ------------------------------------------------------------------ riders

        public bool CanEnter { get { return !Wrecked && Driver == null; } }

        public void Enter(Combatant who)
        {
            Driver = who;
            obstacle.enabled = false;
            engine.Play();
            Ballistics.SoundAt(transform.position, "equip", 0.6f);
        }

        public void Exit()
        {
            Driver = null;
            obstacle.enabled = true;
        }

        /// <summary>A free spot beside the vehicle to step off to (left, right, behind, or on top as a last resort).</summary>
        public Vector3 ExitPoint()
        {
            Vector3[] tries =
            {
                transform.position - transform.right * (bodyHalf.x + 0.6f),
                transform.position + transform.right * (bodyHalf.x + 0.6f),
                transform.position - transform.forward * (bodyHalf.z + 0.7f),
                transform.position + transform.forward * (bodyHalf.z + 0.7f),
            };
            foreach (var t in tries)
            {
                Vector3 p = t + Vector3.up * 0.1f;
                if (!Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.5f, 0.32f, ~0, QueryTriggerInteraction.Ignore))
                    return GroundAt(p, p.y + 1f);
            }
            return transform.position + Vector3.up * (bodyHalf.y * 2f + 0.1f);
        }

        // ------------------------------------------------------------------ damage

        public void TakeHit(float damage, Combatant attacker)
        {
            if (Wrecked || damage <= 0f) return;
            Health -= damage;
            if (Health <= 0f) Wreck(attacker);
        }

        void Wreck(Combatant attacker)
        {
            Wrecked = true;
            Health = 0f;
            Speed = 0f;
            engine.Stop();
            Vector3 p = transform.position + Vector3.up * 0.6f;
            Ballistics.SoundAt(p, "explosion", 1f);
            NoiseEvents.Emit(p, 160f);
            // Whoever is aboard or close by gets hurt.
            foreach (var c in Combatant.All.ToArray())
            {
                if (c == null || !c.Alive) continue;
                float d = (c.AimPoint - p).magnitude;
                if (d > 6f) continue;
                if (c != Driver && !Ballistics.LineOfSight(p, c.AimPoint, null, c)) continue;
                c.ReceiveHit(80f * Mathf.Clamp01(1f - d / 6f) + 15f, false, attacker, DisplayName + " blast", p);
            }
            var mover = Driver != null ? Driver.GetComponent<PlayerMover>() : null;
            if (mover != null) mover.ForceExit();
            Driver = null;
            // Scorched: everything goes black, and it smokes for a while.
            if (palette != null && palette.Length > LootVisuals.Black)
            {
                var burnt = new Material[palette.Length];
                for (int i = 0; i < burnt.Length; i++) burnt[i] = palette[LootVisuals.Black];
                foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.sharedMaterials = burnt;
            }
            Smoke();
            obstacle.enabled = true;
        }

        void Smoke()
        {
            if (smokeMaterial == null) return;
            var go = new GameObject("Wreck Smoke");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, bodyHalf.y, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 30f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            main.startColor = new Color(0.12f, 0.11f, 0.1f, 0.7f);
            main.gravityModifier = -0.05f;
            main.maxParticles = 40;
            var em = ps.emission;
            em.rateOverTime = 6f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = smokeMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 6f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            Destroy(light, 8f);
        }

        // ------------------------------------------------------------------ driving

        void Update()
        {
            if (Wrecked) return;
            float dt = Time.deltaTime;
            crashCooldown -= dt;
            float throttle = 0f, steerIn = 0f;
            bool brake = false, boost = false;
            hornT -= dt;
            if (Driver != null && Driver.isPlayer && !GameState.Paused)
            {
                throttle = Input.GetAxisRaw("Vertical");
                steerIn = Input.GetAxisRaw("Horizontal");
                brake = Input.GetKey(KeyCode.Space);
                boost = Input.GetKey(KeyCode.LeftShift) && throttle > 0.1f;
                if (Input.GetKeyDown(KeyCode.H) && hornT <= 0f)
                {
                    hornT = 0.5f;
                    Ballistics.SoundAt(transform.position + transform.forward * bodyHalf.z, kind == Kind.Jeep || kind == Kind.Cart ? "horn_low" : "horn_high", 1f);
                    NoiseEvents.Emit(transform.position, 120f);
                }
            }
            // Boost: spends the tank while held; once empty it must refill a quarter before it works again.
            if (boostLocked && Boost > 0.25f) boostLocked = false;
            Boosting = boost && !boostLocked && Boost > 0f && !airborne;
            if (Boosting)
            {
                Boost = Mathf.Max(0f, Boost - dt / 4f);
                if (Boost <= 0f) boostLocked = true;
            }
            else Boost = Mathf.Min(1f, Boost + dt / 9f);
            if (Driver == null && Mathf.Abs(Speed) < 0.05f && !airborne) { engine.volume = 0f; return; }
            if (Driver != null && !Driver.Alive) Exit();
            Drive(throttle, steerIn, brake, dt);

            // Engine note and the noise bots hear.
            float s = Mathf.Abs(Speed) / maxSpeed;
            engine.pitch = 0.75f + s * 1.1f + Mathf.Abs(throttle) * 0.1f + (Boosting ? 0.15f : 0f);
            engine.volume = Driver != null ? 0.35f + s * 0.45f : Mathf.MoveTowards(engine.volume, 0f, dt);
            noiseT -= dt;
            if (Driver != null && noiseT <= 0f)
            {
                noiseT = 0.6f;
                NoiseEvents.Emit(transform.position, 45f + s * 70f);
            }
        }

        void Drive(float throttle, float steerIn, bool brake, float dt)
        {
            // Throttle, reverse, brakes and rolling resistance.
            float top = Boosting ? maxSpeed * 1.35f : maxSpeed;
            float push = Boosting ? acceleration * 1.7f : acceleration;
            if (throttle > 0.1f) Speed = Speed < -0.2f ? Mathf.MoveTowards(Speed, 0f, 14f * dt)
                                     : Speed > top ? Mathf.MoveTowards(Speed, top, 3f * dt)
                                     : Mathf.Min(top, Speed + push * throttle * dt * Mathf.Lerp(1f, 0.35f, Speed / top));
            else if (throttle < -0.1f) Speed = Speed > 0.3f ? Mathf.MoveTowards(Speed, 0f, 14f * dt) : Mathf.Max(-maxReverse, Speed - acceleration * 0.6f * dt);
            else Speed = Mathf.MoveTowards(Speed, 0f, (airborne ? 0.2f : 2.2f) * dt);
            if (brake && !airborne) Speed = Mathf.MoveTowards(Speed, 0f, 16f * dt);

            float grip = Mathf.Lerp(1f, 0.38f, Mathf.Clamp01(Mathf.Abs(Speed) / maxSpeed));
            steer = Mathf.MoveTowards(steer, steerIn * maxSteer * grip, 140f * dt);

            float step = Speed * dt;
            if (Mathf.Abs(step) > 0.0001f && Blocked(Mathf.Sign(step), Mathf.Abs(step)))
            {
                float impact = Mathf.Abs(Speed);
                if (impact > 7f && crashCooldown <= 0f)
                {
                    crashCooldown = 0.5f;
                    Ballistics.SoundAt(transform.position, "crash", 0.9f);
                    TakeHit((impact - 6f) * 9f, null);
                    if (Driver != null) Driver.ReceiveHit((impact - 7f) * 3f, false, null, "crash", transform.position + transform.forward, true);
                }
                Speed = -Speed * 0.15f;
                step = 0f;
            }

            // Bicycle model about the rear axle.
            float yawRate = airborne ? 0f : Speed / wheelBase * Mathf.Tan(steer * Mathf.Deg2Rad) * Mathf.Rad2Deg;
            float yaw = transform.eulerAngles.y + yawRate * dt;
            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            Vector3 fwd = flat * Vector3.forward;
            Vector3 right = flat * Vector3.right;
            Vector3 pos = transform.position + fwd * step;

            float rearAxle = frontAxle - wheelBase;
            float fl = GroundAt(pos + fwd * frontAxle - right * halfTrack, pos.y).y;
            float fr = GroundAt(pos + fwd * frontAxle + right * halfTrack, pos.y).y;
            float rl = GroundAt(pos + fwd * rearAxle - right * halfTrack, pos.y).y;
            float rr = GroundAt(pos + fwd * rearAxle + right * halfTrack, pos.y).y;
            float front = (fl + fr) * 0.5f, rear = (rl + rr) * 0.5f;
            float ground = Mathf.Lerp(rear, front, -rearAxle / wheelBase);

            // Hills: gravity along the slope.
            float grade = (front - rear) / wheelBase;
            if (!airborne) Speed -= 9.81f * grade / Mathf.Sqrt(1f + grade * grade) * dt * 0.7f;

            // Off an edge: fly, then land (hard landings hurt).
            if (!airborne && ground < pos.y - 0.45f && Mathf.Abs(Speed) > 4f)
            {
                airborne = true;
                vy = Mathf.Max(0f, grade) * Speed;
            }
            if (airborne)
            {
                vy -= 20f * dt;
                pos.y += vy * dt;
                if (pos.y <= ground)
                {
                    pos.y = ground;
                    airborne = false;
                    if (vy < -9f)
                    {
                        Ballistics.SoundAt(pos, "crash", 0.7f);
                        TakeHit((-vy - 9f) * 8f, null);
                        if (Driver != null) Driver.ReceiveHit((-vy - 9f) * 2.5f, false, null, "fall", pos, true);
                    }
                    vy = 0f;
                }
            }
            else pos.y = Mathf.Lerp(transform.position.y, ground, 1f - Mathf.Exp(-14f * dt));

            float pitch = airborne ? transform.eulerAngles.x : -Mathf.Atan2(front - rear, wheelBase) * Mathf.Rad2Deg;
            float roll = halfTrack > 0.3f ? Mathf.Atan2(rl + fl - rr - fr, 4f * halfTrack) * Mathf.Rad2Deg : 0f;
            if (kind == Kind.Scooter)
            {
                // Lean into the turn with speed.
                lean = Mathf.MoveTowards(lean, -steer / maxSteer * Mathf.Clamp01(Mathf.Abs(Speed) / 9f) * 24f, 90f * dt);
                roll += lean;
            }
            else if (kind == Kind.Auto)
            {
                // Three wheels: the body rolls out of a fast turn (and the inside wheel lifts a little).
                lean = Mathf.MoveTowards(lean, steer / maxSteer * Mathf.Clamp01((Mathf.Abs(Speed) - 6f) / 8f) * 9f, 60f * dt);
                roll += lean;
            }
            var target = Quaternion.Euler(pitch, yaw, roll);
            transform.SetPositionAndRotation(pos, Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-9f * dt)));

            // Wheels.
            float spin = Speed * dt / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg;
            if (wheels != null) foreach (var w in wheels) if (w != null) w.Rotate(spin, 0f, 0f, Space.Self);
            if (steerPivot != null) steerPivot.localRotation = Quaternion.Euler(0f, steer, 0f);
            if (steeredWheels != null) foreach (var w in steeredWheels) if (w != null) w.localRotation = Quaternion.Euler(0f, steer, 0f);
        }

        void SettleOnGround()
        {
            Vector3 p = GroundAt(transform.position, transform.position.y);
            transform.position = p;
        }

        /// <summary>The highest walkable surface under a point (not this vehicle, not people).</summary>
        Vector3 GroundAt(Vector3 at, float fallback)
        {
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 2.5f, Vector3.down, hits, 10f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c.transform.IsChildOf(transform) || hits[i].normal.y < 0.5f) continue;
                if (c.GetComponentInParent<Combatant>() != null) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            return new Vector3(at.x, float.IsNegativeInfinity(best) ? fallback : best, at.z);
        }

        bool Blocked(float direction, float distance)
        {
            Vector3 centre = transform.position + transform.up * (bodyHalf.y + 0.25f);
            Vector3 half = new Vector3(bodyHalf.x - 0.05f, Mathf.Max(0.15f, bodyHalf.y - 0.35f), 0.15f);
            Vector3 dir = transform.forward * direction;
            int n = Physics.BoxCastNonAlloc(centre, half, dir, hits, transform.rotation, bodyHalf.z + distance + 0.2f, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (Driver != null && h.collider.transform.IsChildOf(Driver.transform)) continue;
                if (h.collider is TerrainCollider || h.normal.y > 0.6f || h.distance <= 0f) continue;
                var victim = h.collider.GetComponentInParent<Combatant>();
                if (victim != null)
                {
                    if (!victim.Alive) continue;
                    if (victim == lastRunOver && Time.time - lastRunOverTime < 0.8f) continue;
                    if (Mathf.Abs(Speed) > 5f)
                    {
                        lastRunOver = victim;
                        lastRunOverTime = Time.time;
                        // Knocked down by the vehicle; the rider gets the credit.
                        victim.ReceiveHit(Mathf.Abs(Speed) * 7f, false, Driver, DisplayName, transform.position);
                        Speed *= 0.75f;
                        Ballistics.SoundAt(h.point, "hit_flesh", 0.9f);
                        continue;
                    }
                }
                var other = h.collider.GetComponentInParent<Vehicle>();
                if (other != null && Mathf.Abs(Speed) > 7f) other.TakeHit(Mathf.Abs(Speed) * 4f, Driver);
                blocked = true;
            }
            return blocked;
        }
    }
}
