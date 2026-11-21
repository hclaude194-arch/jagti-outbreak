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
    /// The helicopter (Kind.Heli, on the academic block's roof pad) shares the riding, damage and HUD
    /// but flies instead: the rotor spools up once you're aboard, W/S fly forward and back, A/D turn,
    /// Space climbs, Ctrl descends, Shift boosts. Let go of the controls and it hovers; leave it and it
    /// settles to the ground. You can get out when it is within a few metres of whatever is below.
    /// </summary>
    public class Vehicle : MonoBehaviour
    {
        public enum Kind { Scooter, Cart, Auto, Jeep, Heli }

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
        // Helicopter only: the rotors that spin.
        [HideInInspector] public Transform rotor, tailRotor;

        public float maxSpeed = 22f;
        public float maxReverse = 4f;
        public float acceleration = 7f;
        public float maxSteer = 34f;
        public float maxHealth = 260f;
        /// <summary>How hard it can corner before running wide (sideways acceleration, m/s²).</summary>
        public float cornerGrip = 10f;

        public float Health { get; private set; }
        /// <summary>The boost tank, 0 to 1. Shift spends it; it refills when not boosting.</summary>
        public float Boost { get; private set; }
        public bool Boosting { get; private set; }
        public float Speed { get; private set; }
        /// <summary>The driver's throttle this frame (-1 reverse .. 1 full), for the engine sound.</summary>
        public float Throttle { get; private set; }
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
                    case Kind.Heli: return "helicopter";
                    default: return "utility cart";
                }
            }
        }
        /// <summary>The seat: a rider's root sits here (0.8 m below the cushion, so the head is at a sitting height).</summary>
        public Transform Seat { get; private set; }
        /// <summary>Which way the vehicle points on the map, in degrees (no pitch or roll).</summary>
        public float Heading { get { return heading; } }
        /// <summary>Helicopter: height above whatever is below it (0 when sitting on the ground or a roof).</summary>
        public float Altitude { get; private set; }
        /// <summary>Helicopter: rotor speed, 0 (stopped) to 1 (flying speed).</summary>
        public float Spool { get { return spool; } }
        /// <summary>Whether the rider may step off now: slow on the ground, or a helicopter low and slow.</summary>
        public bool CanExit
        {
            get { return kind == Kind.Heli ? Altitude < 3.5f && Mathf.Abs(Speed) < 6f : Mathf.Abs(Speed) < 9f; }
        }

        float steer, vy, lean, noiseT, crashCooldown, hornT;
        // The heading is kept here, not read back from the transform: the body's tilt is smoothed, but
        // the turn itself must land in full every frame or the steering feels dead and the body slides.
        float heading, tiltPitch, tiltRoll;
        bool boostLocked;
        Combatant lastRunOver;
        float lastRunOverTime;
        bool airborne;
        // Helicopter state.
        Vector3 vel;
        float spool, groundBelow, ceiling;
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
                cornerGrip = 8f;
            }
            else if (kind == Kind.Auto)
            {
                // Nippy and light: quick off the line, not fast, tips into tight turns.
                maxSpeed = 17f;
                acceleration = 6.5f;
                maxSteer = 38f;
                maxHealth = 340f;
                cornerGrip = 7.5f;
            }
            else if (kind == Kind.Jeep)
            {
                // The fastest and toughest, but wide and slow to turn.
                maxSpeed = 27f;
                acceleration = 8f;
                maxSteer = 30f;
                maxHealth = 640f;
                cornerGrip = 9f;
            }
            else if (kind == Kind.Heli)
            {
                // A light utility helicopter: quick, tough-ish, but loud and easy to spot.
                maxSpeed = 32f;
                maxReverse = 9f;
                acceleration = 7f;
                maxHealth = 900f;
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

            // Engine, tyres, rotor: VehicleAudio (the sound bank's layered loops).
            gameObject.AddComponent<VehicleAudio>().vehicle = this;
            heading = transform.eulerAngles.y;
            SettleOnGround();
            // The helicopter's ceiling: well above the highest roofs, below the sky bus.
            ceiling = transform.position.y + 140f;
        }

        // ------------------------------------------------------------------ riders

        public bool CanEnter { get { return !Wrecked && Driver == null; } }

        public void Enter(Combatant who)
        {
            Driver = who;
            obstacle.enabled = false;
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
            float dt = Time.deltaTime;
            if (Wrecked)
            {
                if (kind == Kind.Heli && Altitude > 0.01f) Fly(0f, 0f, 0f, dt);   // a shot-down helicopter falls
                return;
            }
            if (kind == Kind.Heli) { UpdateHeli(dt); return; }
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
                    Ballistics.SoundAt(transform.position + transform.forward * bodyHalf.z, kind == Kind.Jeep || kind == Kind.Cart ? "horn_low" : kind == Kind.Auto ? "horn_bulb" : "horn_high", 1f);
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
            Throttle = throttle;
            if (Driver == null && Mathf.Abs(Speed) < 0.05f && !airborne) return;
            if (Driver != null && !Driver.Alive) Exit();
            Drive(throttle, steerIn, brake, dt);

            // Engine note and the noise bots hear.
            float s = Mathf.Abs(Speed) / maxSpeed;
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

            // Less lock at speed: the tightest turn is the one the tyres can hold (cornering grip, m/s²),
            // so slow it turns on the spot and fast it sweeps through a bend instead of spinning round.
            float v2 = Speed * Speed;
            float steerLock = v2 > 0.01f ? Mathf.Min(maxSteer, Mathf.Atan(wheelBase * cornerGrip / v2) * Mathf.Rad2Deg) : maxSteer;
            float steerTarget = steerIn * steerLock;
            // The wheel turns in briskly and comes back to centre faster still when the key is let go.
            steer = Mathf.MoveTowards(steer, steerTarget, (Mathf.Abs(steerIn) < 0.1f ? 260f : 170f) * dt);

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
            heading = Mathf.Repeat(heading + yawRate * dt, 360f);
            float yaw = heading;
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

            float pitch = airborne ? tiltPitch : -Mathf.Atan2(front - rear, wheelBase) * Mathf.Rad2Deg;
            float roll = halfTrack > 0.3f ? Mathf.Atan2(rl + fl - rr - fr, 4f * halfTrack) * Mathf.Rad2Deg : 0f;
            if (kind == Kind.Scooter)
            {
                // Lean into the turn with speed.
                lean = Mathf.MoveTowards(lean, -Corner() * 24f, 90f * dt);
                roll += lean;
            }
            else if (kind == Kind.Auto)
            {
                // Three wheels: the body rolls out of a fast turn (and the inside wheel lifts a little).
                lean = Mathf.MoveTowards(lean, Corner() * Mathf.Clamp01((Mathf.Abs(Speed) - 6f) / 8f) * 9f, 60f * dt);
                roll += lean;
            }
            // Only the tilt is smoothed (so bumps don't jolt); the heading is applied as it is.
            float k = 1f - Mathf.Exp(-9f * dt);
            tiltPitch = Mathf.Lerp(tiltPitch, pitch, k);
            tiltRoll = Mathf.Lerp(tiltRoll, roll, k);
            transform.SetPositionAndRotation(pos, Quaternion.Euler(tiltPitch, yaw, tiltRoll));

            // Wheels.
            float spin = Speed * dt / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg;
            if (wheels != null) foreach (var w in wheels) if (w != null) w.Rotate(spin, 0f, 0f, Space.Self);
            if (steerPivot != null) steerPivot.localRotation = Quaternion.Euler(0f, steer, 0f);
            if (steeredWheels != null) foreach (var w in steeredWheels) if (w != null) w.localRotation = Quaternion.Euler(0f, steer, 0f);
        }

        // ------------------------------------------------------------------ flying (Kind.Heli)

        void UpdateHeli(float dt)
        {
            float fwd = 0f, turn = 0f, climb = 0f;
            bool boost = false;
            if (Driver != null && !Driver.Alive) Exit();
            if (Driver != null && Driver.isPlayer && !GameState.Paused)
            {
                fwd = Input.GetAxisRaw("Vertical");
                turn = Input.GetAxisRaw("Horizontal");
                if (Input.GetKey(KeyCode.Space)) climb += 1f;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) climb -= 1f;
                boost = Input.GetKey(KeyCode.LeftShift) && fwd > 0.1f;
            }
            if (boostLocked && Boost > 0.25f) boostLocked = false;
            Boosting = boost && !boostLocked && Boost > 0f && Altitude > 1f;
            if (Boosting)
            {
                Boost = Mathf.Max(0f, Boost - dt / 5f);
                if (Boost <= 0f) boostLocked = true;
            }
            else Boost = Mathf.Min(1f, Boost + dt / 8f);
            // Parked with the rotor stopped: nothing to do.
            if (Driver == null && spool <= 0f && Altitude <= 0.01f) return;
            Fly(fwd, turn, climb, dt);

            noiseT -= dt;
            if (spool > 0.2f && noiseT <= 0f)
            {
                noiseT = 0.6f;
                NoiseEvents.Emit(transform.position, 80f + spool * 120f);
            }
        }

        /// <summary>
        /// Arcade flight. Velocity eases toward what the stick asks for (forward speed along the heading,
        /// climb rate from Space/Ctrl), so letting go hovers. Without a pilot (or once shot down) the rotor
        /// winds down and it sinks, then drops. The body tilts with the speed and banks into turns; a
        /// sphere round the cabin and a wider one at rotor height stop it at walls, and fast knocks hurt.
        /// </summary>
        void Fly(float fwdIn, float turnIn, float climbIn, float dt)
        {
            bool powered = Driver != null && Driver.Alive && !Wrecked;
            crashCooldown -= dt;
            spool = Mathf.MoveTowards(spool, powered ? 1f : 0f, dt / (powered ? 2.2f : 6f));
            bool flying = spool > 0.8f;
            bool onGround = Altitude <= 0.05f;

            // Turning: a slow pivot on the skids once the rotor is up to speed, quicker in the air.
            float yawRate = flying ? turnIn * (onGround ? 35f : 75f) : 0f;
            heading = Mathf.Repeat(heading + yawRate * dt, 360f);
            Quaternion flat = Quaternion.Euler(0f, heading, 0f);
            Vector3 f = flat * Vector3.forward;

            float top = Boosting ? maxSpeed * 1.45f : maxSpeed;
            Vector3 hv = new Vector3(vel.x, 0f, vel.z);
            Vector3 want = flying && !onGround ? f * (fwdIn > 0f ? fwdIn * top : fwdIn * maxReverse) : Vector3.zero;
            float accel = Mathf.Abs(fwdIn) > 0.1f ? (Boosting ? acceleration * 1.6f : acceleration) : acceleration * 0.55f;
            if (onGround) accel = 12f;   // skids grip
            hv = Vector3.MoveTowards(hv, want, accel * dt);

            if (flying)
            {
                float wantVy = climbIn * (climbIn > 0f ? 7f : 6f);
                if (transform.position.y > ceiling && wantVy > 0f) wantVy = 0f;
                vel.y = Mathf.MoveTowards(vel.y, wantVy, 9f * dt);
            }
            else
            {
                // Sinking as the rotor slows; a free fall once it has stopped.
                float hold = Mathf.Clamp01(spool / 0.8f);
                vel.y = Mathf.Max(-28f, vel.y - 9.81f * (1f - hold * 0.85f) * dt);
                if (hold > 0.5f) vel.y = Mathf.Max(vel.y, -3f);
            }
            vel = new Vector3(hv.x, vel.y, hv.z);

            Vector3 pos = transform.position;
            Vector3 step = vel * dt;
            if (step.sqrMagnitude > 1e-8f) step = Collide(pos, step);
            pos += step;

            // Ground and roofs: settle onto the highest surface below the skids.
            groundBelow = SurfaceBelow(pos);
            if (pos.y <= groundBelow + 0.02f)
            {
                if (vel.y < -7f)
                {
                    Ballistics.SoundAt(pos, "crash", 0.9f);
                    TakeHit((-vel.y - 7f) * 14f, null);
                    if (Driver != null) Driver.ReceiveHit((-vel.y - 7f) * 3f, false, null, "a hard landing", pos, true);
                }
                pos.y = groundBelow;
                if (vel.y < 0f) vel.y = 0f;
            }
            Altitude = Mathf.Max(0f, pos.y - groundBelow);
            Speed = new Vector2(vel.x, vel.z).magnitude * Mathf.Sign(Vector3.Dot(vel, f) + 0.001f);

            // Tilt: nose down with forward speed and while speeding up, bank into turns; level on the ground.
            float pitch = 0f, roll = 0f;
            if (Altitude > 0.3f)
            {
                float along = Vector3.Dot(hv, f);
                pitch = Mathf.Clamp(along / maxSpeed * 11f + fwdIn * 6f, -12f, 18f);
                roll = -yawRate / 75f * 14f;
            }
            float k = 1f - Mathf.Exp(-4f * dt);
            tiltPitch = Mathf.Lerp(tiltPitch, pitch, k);
            tiltRoll = Mathf.Lerp(tiltRoll, roll, k);
            transform.SetPositionAndRotation(pos, Quaternion.Euler(tiltPitch, heading, tiltRoll));

            if (rotor != null) rotor.Rotate(0f, spool * 1300f * dt, 0f, Space.Self);
            if (tailRotor != null) tailRotor.Rotate(spool * 2400f * dt, 0f, 0f, Space.Self);
        }

        /// <summary>Moves as far along the step as the cabin and the rotor disc allow; knocks at speed hurt.</summary>
        Vector3 Collide(Vector3 pos, Vector3 step)
        {
            RaycastHit hit;
            if (!Cast(pos, step, out hit)) return step;
            float into = -Vector3.Dot(vel, hit.normal);
            if (into > 9f && crashCooldown <= 0f)
            {
                crashCooldown = 0.6f;
                Ballistics.SoundAt(hit.point, "crash", 1f);
                TakeHit((into - 7f) * 16f, null);
                if (Driver != null) Driver.ReceiveHit((into - 9f) * 2.5f, false, null, "crash", hit.point, true);
            }
            // Stop at the surface, bounce off a little and slide along it with what is left.
            Vector3 upto = step.normalized * Mathf.Max(0f, hit.distance - 0.05f);
            if (into > 0f) vel += hit.normal * into * 1.25f;
            Vector3 rest = step - upto;
            rest -= hit.normal * Vector3.Dot(rest, hit.normal);
            if (Cast(pos + upto, rest, out hit)) rest = Vector3.zero;
            return upto + rest;
        }

        /// <summary>Sweeps the cabin (a sphere) and the rotor disc (a wide sphere) against walls and overhangs.</summary>
        bool Cast(Vector3 pos, Vector3 step, out RaycastHit best)
        {
            best = new RaycastHit();
            float len = step.magnitude;
            if (len < 1e-5f) return false;
            Vector3 dir = step / len;
            bool any = false;
            for (int s = 0; s < 2; s++)
            {
                Vector3 c = pos + Vector3.up * (s == 0 ? 1.5f : 2.95f);
                float r = s == 0 ? 1.15f : 3.4f;
                int n = Physics.SphereCastNonAlloc(c, r, dir, hits, len + 0.05f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = hits[i];
                    if (h.distance <= 0f || h.collider.transform.IsChildOf(transform)) continue;
                    if (Driver != null && h.collider.transform.IsChildOf(Driver.transform)) continue;
                    if (h.normal.y > 0.6f) continue;   // floors and roofs: the landing below handles those
                    if (h.collider.GetComponentInParent<Combatant>() != null) continue;
                    if (!any || h.distance < best.distance) { best = h; any = true; }
                }
            }
            return any;
        }

        /// <summary>The highest surface under the helicopter that is not above its skids (ground, roof, pad).</summary>
        float SurfaceBelow(Vector3 at)
        {
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 1.0f, Vector3.down, hits, 800f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c.transform.IsChildOf(transform) || hits[i].normal.y < 0.3f) continue;
                if (c.GetComponentInParent<Combatant>() != null) continue;
                float y = hits[i].point.y;
                if (y > at.y + 0.6f) continue;
                if (y > best) best = y;
            }
            return float.IsNegativeInfinity(best) ? at.y - 800f : best;
        }

        /// <summary>How hard it is cornering, -1 to 1 (sideways acceleration over the grip; + is a right turn).</summary>
        float Corner()
        {
            if (airborne || wheelBase <= 0.01f) return 0f;
            float lat = Speed * Mathf.Abs(Speed) / wheelBase * Mathf.Tan(steer * Mathf.Deg2Rad);
            return Mathf.Clamp(lat / Mathf.Max(1f, cornerGrip), -1f, 1f);
        }

        void SettleOnGround()
        {
            Vector3 p = GroundAt(transform.position, transform.position.y, 3f);
            transform.position = p;
        }

        /// <summary>
        /// The highest walkable surface under a point (not this vehicle, not people) that is no more than
        /// a kerb's height above where the wheels are now, so benches, low walls and parked vehicles beside
        /// the road don't lift a wheel onto them.
        /// </summary>
        Vector3 GroundAt(Vector3 at, float fallback)
        {
            return GroundAt(at, fallback, 0.55f);
        }

        Vector3 GroundAt(Vector3 at, float fallback, float maxRise)
        {
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 2.5f, Vector3.down, hits, 10f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c.transform.IsChildOf(transform) || hits[i].normal.y < 0.5f) continue;
                if (!(c is TerrainCollider) && hits[i].point.y > fallback + maxRise) continue;
                if (c.GetComponentInParent<Combatant>() != null) continue;
                if (c.GetComponentInParent<Vehicle>() != null) continue;
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
                if (Jagti.World.GlassPanes.IsGlass(h.collider))
                {
                    // Glass gives way to anything moving; at a crawl it just stops you.
                    Jagti.World.GlassPanes pane;
                    int paneIndex;
                    if (Mathf.Abs(Speed) > 2f)
                    {
                        if (Jagti.World.GlassPanes.TryGet(h.collider, h.point, out pane, out paneIndex)) pane.Break(paneIndex, transform.forward * direction, true);
                        continue;
                    }
                }
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
