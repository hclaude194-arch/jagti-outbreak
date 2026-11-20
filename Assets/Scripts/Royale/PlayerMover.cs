using Jagti.Gameplay;
using Jagti.Player;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// How the player gets around beyond walking:
    /// Space at a low wall, railing, ledge or window sill vaults over it (or climbs onto it when it is
    /// chest high); E next to a scooter or cart gets on (E again gets off); E at a zip line tower clips
    /// on (S slows, Space lets go); E at a lift's doors rides it to the roof or back down (Lift.cs runs
    /// the cutaway). The rooftop helicopter is ridden like the other vehicles (see Vehicle). Wading through the terrace channels and basins slows you and
    /// splashes (bots hear it). While riding or on the zip line the camera stays behind you and your
    /// guns are put away; a hit area travels with you so you can still be shot.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public class PlayerMover : MonoBehaviour
    {
        public enum Mode { OnFoot, Vaulting, Riding, Zipping, Lift }

        /// <summary>Water channel segments (x0, z0, x1, z1) with their widths, and round pools (x, z, radius).</summary>
        public Vector4[] channels;
        public float[] channelWidths;
        public Vector3[] pools;
        /// <summary>A capsule hit area turned on while the walker's own collider is off.</summary>
        public Collider riderHitbox;

        public Mode State { get; private set; }
        public string Prompt { get; private set; }
        public Vehicle Riding { get; private set; }
        /// <summary>The lift being ridden, if any.</summary>
        public Lift InLift { get; private set; }
        public bool Wading { get; private set; }

        FirstPersonController fpc;
        CharacterController controller;
        Combatant me;
        SkyDiver diver;
        PlayerArsenal arsenal;

        // Vault.
        Vector3 v0, v1, v2;
        float vaultT, vaultTime;

        // Zip line.
        ZipLine zip;
        float zipS, zipDir, zipSpeed;
        AudioSource zipSound;

        // Riding: the view's offset from the vehicle's heading (mouse look), kept level.
        float rideLook, rideSetYaw, rideIdle;
        // The chase view's own heading and height: they trail the vehicle's, so steering, leaning
        // and bumps don't throw the camera about.
        float rideHeading, rideHeadingVel, rideY, rideYVel;
        /// <summary>How long the chase view takes to come round after the vehicle turns (seconds).</summary>
        public float rideTurnLag = 0.32f;
        /// <summary>How long it takes to follow the vehicle up and down over bumps (seconds).</summary>
        public float rideBumpLag = 0.12f;

        float splashT;
        float fallFrom = float.NaN;   // height let go of a zip line at, for landing damage

        void Awake()
        {
            fpc = GetComponent<FirstPersonController>();
            controller = GetComponent<CharacterController>();
            me = GetComponent<Combatant>();
            diver = GetComponent<SkyDiver>();
            arsenal = GetComponent<PlayerArsenal>();
            fpc.jumpOverride = TryVault;
            zipSound = gameObject.AddComponent<AudioSource>();
            zipSound.clip = SynthAudio.Get("zip");
            zipSound.loop = true;
            zipSound.spatialBlend = 0f;
            zipSound.playOnAwake = false;
            if (riderHitbox != null) riderHitbox.enabled = false;
        }

        bool Landed { get { return diver == null || diver.Current == SkyDiver.State.Landed; } }

        /// <summary>
        /// Running, jumping or falling into a window smashes it (walking into one doesn't), so you
        /// can sprint out through the glass or drop out of an upper storey.
        /// </summary>
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Jagti.World.GlassPanes pane;
            int index;
            if (!Jagti.World.GlassPanes.TryGet(hit.collider, hit.point, out pane, out index)) return;
            Vector3 v = controller.velocity;
            float flat = new Vector2(v.x, v.z).magnitude;
            bool airborne = !controller.isGrounded;
            if (flat > 3.2f || (airborne && flat > 1.2f) || v.y < -5f)
            {
                Vector3 dir = hit.moveDirection.sqrMagnitude > 1e-4f ? hit.moveDirection : transform.forward;
                pane.Break(index, dir, true);
                if (fpc != null) fpc.Shake(0.25f);
            }
        }

        void Update()
        {
            Prompt = null;
            if (me != null && !me.Alive)
            {
                if (State != Mode.OnFoot) ForceExit();
                return;
            }
            if (GameState.Paused || !Landed) return;
            float dt = Time.deltaTime;
            switch (State)
            {
                case Mode.OnFoot: OnFoot(dt); break;
                case Mode.Vaulting: Vault(dt); break;
                case Mode.Riding: Ride(dt); break;
                case Mode.Zipping: Zip(dt); break;
                case Mode.Lift: if (InLift == null) ForceExit(); break;   // the lift runs the ride
            }
        }

        // ------------------------------------------------------------------ on foot

        void OnFoot(float dt)
        {
            Wading = transform.position.y - HazeZone.GroundY(transform.position.x, transform.position.z) < 1.0f && InWater(transform.position);
            fpc.wadeScale = Wading ? 0.6f : 1f;
            if (Wading && fpc.HorizontalSpeed > 0.8f)
            {
                splashT -= dt;
                if (splashT <= 0f)
                {
                    splashT = 0.45f;
                    SynthAudio.Play2D("splash", 0.35f);
                    NoiseEvents.Emit(transform.position, 25f);
                }
            }
            if (!float.IsNaN(fallFrom) && controller.isGrounded)
            {
                // Letting go high up hurts: nothing below 4 m, then 9 per metre.
                float drop = fallFrom - transform.position.y;
                fallFrom = float.NaN;
                if (drop > 4f && me != null) me.ReceiveHit((drop - 4f) * 9f, false, null, "a fall", transform.position, true);
            }
            if (fpc.externalControl) return;

            // What can be used here: a vehicle or a zip line (loot pickups take E first).
            Vehicle near = null;
            float best = float.MaxValue;
            foreach (var v in Vehicle.All)
            {
                if (!v.CanEnter) continue;
                float d = (v.transform.position - transform.position).magnitude - v.bodyHalf.z;
                if (d < 1.4f && d < best) { best = d; near = v; }
            }
            ZipLine line = null;
            int end = -1;
            if (near == null)
                foreach (var z in ZipLine.All)
                {
                    end = z.EndNear(transform.position, 2.4f);
                    if (end >= 0) { line = z; break; }
                }
            Lift lift = null;
            bool liftUp = true;
            if (near == null && line == null) lift = Lift.Near(transform.position, out liftUp);
            bool lootFirst = arsenal != null && arsenal.HasTarget;
            if (near != null) Prompt = near.kind == Vehicle.Kind.Heli ? "[E] fly the helicopter" : "[E] ride the " + near.DisplayName;
            else if (line != null) Prompt = "[E] zip line" + (end == 0 ? " (downhill)" : " (uphill)");
            else if (lift != null) Prompt = liftUp ? "[E] take the lift to the roof" : "[E] take the lift down";
            if (lootFirst || !Input.GetKeyDown(KeyCode.E)) return;
            if (near != null) GetOn(near);
            else if (line != null) ClipOn(line, end);
            else if (lift != null) EnterLift(lift, liftUp);
        }

        bool InWater(Vector3 p)
        {
            if (channels != null)
                for (int i = 0; i < channels.Length; i++)
                {
                    var c = channels[i];
                    Vector2 a = new Vector2(c.x, c.y), b = new Vector2(c.z, c.w), q = new Vector2(p.x, p.z);
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                    float w = channelWidths[i] * 0.5f;
                    if ((a + ab * t - q).sqrMagnitude < w * w) return true;
                }
            if (pools != null)
                foreach (var pool in pools)
                    if (new Vector2(p.x - pool.x, p.z - pool.y).sqrMagnitude < pool.z * pool.z) return true;
            return false;
        }

        // ------------------------------------------------------------------ vaulting

        /// <summary>
        /// Called by the walker when Space is pressed on the ground. Looks for something 0.35 to 1.9 m
        /// high right in front: vaults over it if it is thin with room beyond, else climbs on top.
        /// Returns true if it took the jump.
        /// </summary>
        bool TryVault()
        {
            if (State != Mode.OnFoot || !Landed) return false;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 feet = transform.position;
            RaycastHit hit;
            bool found = false;
            foreach (float h in new[] { 0.45f, 0.9f, 1.3f })
            {
                if (Physics.Raycast(feet + Vector3.up * h, fwd, out hit, 0.95f, ~0, QueryTriggerInteraction.Ignore) && !Mine(hit.collider)
                    && Mathf.Abs(hit.normal.y) < 0.45f && hit.collider.GetComponentInParent<Combatant>() == null)
                {
                    found = TryOver(feet, fwd, hit.point);
                    if (found) break;
                }
            }
            return found;
        }

        bool TryOver(Vector3 feet, Vector3 fwd, Vector3 face)
        {
            // The top: look down just beyond the face.
            RaycastHit top;
            Vector3 probe = face + fwd * 0.12f;
            probe.y = feet.y + 2.3f;
            if (!Physics.Raycast(probe, Vector3.down, out top, 2.3f, ~0, QueryTriggerInteraction.Ignore) || Mine(top.collider)) return false;
            float height = top.point.y - feet.y;
            if (height < 0.35f || height > 1.9f || top.normal.y < 0.6f) return false;
            // Room to pass over the top, crouched.
            Vector3 over = top.point + Vector3.up * 0.08f;
            if (Physics.CheckCapsule(over + Vector3.up * 0.35f, over + Vector3.up * 0.75f, 0.26f, ~0, QueryTriggerInteraction.Ignore)) return false;

            // How deep is it: find where the ground drops away again beyond the top.
            Vector3 land = Vector3.zero;
            bool beyond = false;
            for (float d = 0.3f; d <= 1.5f; d += 0.3f)
            {
                Vector3 q = face + fwd * d;
                q.y = top.point.y + 0.3f;
                RaycastHit g;
                if (!Physics.Raycast(q, Vector3.down, out g, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (g.point.y < top.point.y - 0.4f)
                {
                    land = g.point + fwd * 0.45f;
                    land.y = g.point.y + 0.05f;
                    beyond = true;
                    break;
                }
            }
            bool climb = height > 1.2f;
            if (!beyond || climb)
            {
                // Climb onto it (a ledge, a low roof, a high sill).
                land = top.point + fwd * 0.45f + Vector3.up * 0.05f;
            }
            float standH = climb ? 1.0f : 1.7f;
            if (Physics.CheckCapsule(land + Vector3.up * 0.4f, land + Vector3.up * standH, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!beyond) return false;
                land = top.point + fwd * 0.45f + Vector3.up * 0.05f;   // no room beyond: stay on top
                if (Physics.CheckCapsule(land + Vector3.up * 0.4f, land + Vector3.up * 1.0f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) return false;
            }

            // The path itself must be open: across the top and down to the landing spot. Without
            // this, a sill or ledge in a facade (whose face the probes reach past) let the vault
            // carry the player through the window glass or the wall into the building.
            Vector3 lift = Vector3.up * 0.3f;
            if (Blocked(feet + Vector3.up * (height + 0.3f), over + lift) || Blocked(over + lift, land + lift)
                || Blocked(land + Vector3.up * 1.2f, over + Vector3.up * 0.9f))
                return false;

            State = Mode.Vaulting;
            v0 = transform.position;
            v1 = over + fwd * 0.05f + Vector3.up * 0.25f;
            v2 = land;
            vaultT = 0f;
            vaultTime = climb ? 0.85f : 0.42f;
            controller.enabled = false;
            fpc.externalControl = true;
            if (fpc.Crouching) fpc.SetCrouch(false);
            SynthAudio.Play2D("vault", 0.45f);
            ShowBody(HumanBody.Pose.Stand, 0.75f);
            return true;
        }

        bool Mine(Collider c)
        {
            return c.transform.IsChildOf(transform);
        }

        /// <summary>True if anything solid (not the player's own colliders) lies between a and b.</summary>
        bool Blocked(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return false;
            // Both ways: walls and glass are single-sided meshes, and a ray only hits a face from its front.
            foreach (var h in Physics.RaycastAll(a, d / len, len, ~0, QueryTriggerInteraction.Ignore))
                if (!Mine(h.collider)) return true;
            foreach (var h in Physics.RaycastAll(b, -d / len, len, ~0, QueryTriggerInteraction.Ignore))
                if (!Mine(h.collider)) return true;
            return false;
        }

        void Vault(float dt)
        {
            vaultT += dt / vaultTime;
            float t = Mathf.Clamp01(vaultT);
            // A quadratic arc: up onto the top, then over and down.
            Vector3 p = (1f - t) * (1f - t) * v0 + 2f * (1f - t) * t * v1 + t * t * v2;
            transform.position = p;
            if (t >= 1f)
            {
                State = Mode.OnFoot;
                controller.enabled = true;
                fpc.externalControl = false;
                fpc.Teleport(v2);
            }
        }

        // ------------------------------------------------------------------ riding

        void GetOn(Vehicle v)
        {
            Riding = v;
            State = Mode.Riding;
            if (fpc.Crouching) fpc.SetCrouch(false);
            controller.enabled = false;
            fpc.externalControl = true;
            fpc.thirdPersonDistance = v.kind == Vehicle.Kind.Scooter ? 4.6f : v.kind == Vehicle.Kind.Jeep ? 7f : v.kind == Vehicle.Kind.Heli ? 12f : 6f;
            fpc.cameraIgnore = v.transform;
            transform.SetParent(v.Seat, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            rideLook = 0f;
            rideIdle = 0f;
            rideSetYaw = transform.eulerAngles.y;
            rideHeading = v.Heading;
            rideHeadingVel = 0f;
            rideY = LevelSeat(v).y;
            rideYVel = 0f;
            if (riderHitbox != null) riderHitbox.enabled = true;
            v.Enter(me);
            ShowBody(HumanBody.Pose.Seat, 0f);
        }

        void Ride(float dt)
        {
            if (Riding == null || Riding.Wrecked) { ForceExit(); return; }
            if (Riding.kind == Vehicle.Kind.Heli)
                Prompt = Riding.Spool < 0.8f ? "Rotor spinning up..." : Riding.CanExit ? "[E] get out      W/S fly   A/D turn   Space up   Ctrl down   Shift boost"
                       : "W/S fly   A/D turn   Space up   Ctrl down   Shift boost      (get low to get out)";
            else Prompt = "[E] get off      W/S throttle   A/D steer   Space brake   Shift boost   H horn";
            if (Input.GetKeyDown(KeyCode.E) && Riding.CanExit) GetOff();
        }

        /// <summary>The seat's position as if the vehicle stood level (no lean, no pitch).</summary>
        static Vector3 LevelSeat(Vehicle v)
        {
            Vector3 local = v.transform.InverseTransformPoint(v.Seat != null ? v.Seat.position : v.transform.position);
            return v.transform.position + Quaternion.Euler(0f, v.Heading, 0f) * local;
        }

        /// <summary>
        /// After the vehicle has moved this frame: the view follows the vehicle's heading plus whatever the
        /// mouse added, stays level (no rolling with the scooter's lean or pitching on bumps) and swings
        /// back behind the vehicle a moment after the mouse is left alone. The body faces the bars.
        /// The view's heading trails the vehicle's by a fraction of a second and its pivot is the level
        /// seat, smoothed up and down, so steering wobbles, the scooter's lean and kerbs barely move it;
        /// the mouse still turns it at once.
        /// </summary>
        void LateUpdate()
        {
            if (State != Mode.Riding || Riding == null) return;
            float dt = Time.deltaTime;
            float mouse = Mathf.DeltaAngle(rideSetYaw, transform.eulerAngles.y);
            rideLook = Mathf.DeltaAngle(0f, rideLook + mouse);
            if (Mathf.Abs(mouse) > 0.05f) rideIdle = 0f;
            else rideIdle += dt;
            if (rideIdle > 1.2f && Mathf.Abs(Riding.Speed) > 2f)
                rideLook = Mathf.MoveTowardsAngle(rideLook, 0f, (40f + Mathf.Abs(rideLook) * 2f) * dt);
            if (dt > 0f)
            {
                rideHeading = Mathf.SmoothDampAngle(rideHeading, Riding.Heading, ref rideHeadingVel, rideTurnLag, 400f, dt);
                // Never let it fall far behind in a tight spin.
                float behind = Mathf.DeltaAngle(rideHeading, Riding.Heading);
                if (Mathf.Abs(behind) > 50f) rideHeading = Riding.Heading - Mathf.Sign(behind) * 50f;
            }
            transform.rotation = Quaternion.Euler(0f, rideHeading + rideLook, 0f);
            rideSetYaw = transform.eulerAngles.y;
            // The pivot: the level seat, with bumps smoothed (the helicopter is followed directly).
            Vector3 seat = LevelSeat(Riding);
            if (Riding.kind == Vehicle.Kind.Heli || dt <= 0f || Mathf.Abs(seat.y - rideY) > 3f) { rideY = seat.y; rideYVel = 0f; }
            else rideY = Mathf.SmoothDamp(rideY, seat.y, ref rideYVel, rideBumpLag, 30f, dt);
            transform.position = new Vector3(seat.x, rideY, seat.z);

            var body = diver != null ? diver.body : null;
            if (body != null)
            {
                body.pose = HumanBody.Pose.Seat;
                // The body sits on the real (leaning) seat, wherever the view's pivot is.
                if (Riding.Seat != null) body.transform.position = Riding.Seat.position;
                body.transform.rotation = Riding.transform.rotation;
                body.handTargetL = Riding.transform.TransformPoint(Riding.handL);
                body.handTargetR = Riding.transform.TransformPoint(Riding.handR);
                Vector3 f = Riding.footLocal;
                body.footTargetL = Riding.transform.TransformPoint(new Vector3(-f.x + Riding.seatLocal.x, f.y, f.z));
                body.footTargetR = Riding.transform.TransformPoint(new Vector3(f.x + Riding.seatLocal.x, f.y, f.z));
            }
        }

        void GetOff()
        {
            var v = Riding;
            Vector3 p = v != null ? v.ExitPoint() : transform.position;
            if (v != null) v.Exit();
            Riding = null;
            transform.SetParent(null, true);
            float yaw = transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Release(p);
        }

        /// <summary>Puts the player back on foot right away (wrecked vehicle, death).</summary>
        public void ForceExit()
        {
            if (State == Mode.Lift)
            {
                var l = InLift;
                InLift = null;
                if (l != null) l.Abort();   // calls LeaveLift, which is harmless now
                if (State == Mode.OnFoot) return;
            }
            if (State == Mode.Riding)
            {
                // Thrown out of a helicopter high up: the fall hurts.
                if (Riding != null && Riding.kind == Vehicle.Kind.Heli && Riding.Altitude > 4f) fallFrom = transform.position.y;
                if (Riding != null) Riding.Exit();
                Riding = null;
                transform.SetParent(null, true);
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            }
            zipSound.Stop();
            Release(transform.position);
        }

        void Release(Vector3 at)
        {
            State = Mode.OnFoot;
            zip = null;
            var body = diver != null ? diver.body : null;
            if (body != null)
            {
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                body.pose = HumanBody.Pose.Stand;
            }
            if (riderHitbox != null) riderHitbox.enabled = false;
            fpc.cameraIgnore = null;
            controller.enabled = true;
            fpc.externalControl = false;
            fpc.thirdPersonDistance = 0f;
            fpc.Teleport(at);
        }

        // ------------------------------------------------------------------ lifts

        void EnterLift(Lift lift, bool goingUp)
        {
            InLift = lift;
            State = Mode.Lift;
            if (fpc.Crouching) fpc.SetCrouch(false);
            controller.enabled = false;
            fpc.externalControl = true;
            lift.Begin(this, goingUp);
        }

        /// <summary>Called by the lift at the end of the ride (or when it is cut short): back on foot there.</summary>
        public void LeaveLift(Vector3 at, float yaw)
        {
            InLift = null;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Release(at);
        }

        // ------------------------------------------------------------------ zip line

        void ClipOn(ZipLine line, int end)
        {
            zip = line;
            zipS = end == 0 ? 0f : 1f;
            zipDir = end == 0 ? 1f : -1f;
            zipSpeed = 1.5f;
            State = Mode.Zipping;
            if (fpc.Crouching) fpc.SetCrouch(false);
            controller.enabled = false;
            fpc.externalControl = true;
            fpc.thirdPersonDistance = 3.6f;
            if (riderHitbox != null) riderHitbox.enabled = true;
            zipSound.Play();
            SynthAudio.Play2D("clink", 0.6f);
            ShowBody(HumanBody.Pose.Hang, 0f);
        }

        void Zip(float dt)
        {
            if (zip == null) { ForceExit(); return; }
            Vector3 here = zip.PointAt(zipS);
            Vector3 ahead = zip.PointAt(Mathf.Clamp01(zipS + zipDir * 0.01f));
            bool downhill = ahead.y < here.y;
            float target = downhill ? 17f : 7f;
            if (Input.GetKey(KeyCode.S)) target = 3f;
            zipSpeed = Mathf.MoveTowards(zipSpeed, target, (downhill ? 5f : 3f) * dt);
            zipS = Mathf.Clamp01(zipS + zipDir * zipSpeed * dt / Mathf.Max(1f, zip.Length));
            // Hang below the trolley (hands about 2.15 m above the feet).
            transform.position = zip.PointAt(zipS) - Vector3.up * 2.2f;
            zipSound.volume = 0.15f + zipSpeed / 17f * 0.45f;
            zipSound.pitch = 0.8f + zipSpeed / 17f * 0.6f;
            var body = diver != null ? diver.body : null;
            if (body != null)
            {
                body.pose = HumanBody.Pose.Hang;
                Vector3 along = (ahead - here);
                along.y = 0f;
                if (along.sqrMagnitude > 1e-6f) body.transform.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            }
            Prompt = "S slow down      Space let go";
            bool done = zipS <= 0f || zipS >= 1f;
            if (done || Input.GetKeyDown(KeyCode.Space))
            {
                zipSound.Stop();
                Vector3 drop = transform.position;
                if (done)
                {
                    Vector3 baseAt = zipS >= 1f ? zip.b : zip.a;
                    Vector3 away = (zipS >= 1f ? zip.b - zip.a : zip.a - zip.b);
                    away.y = 0f;
                    drop = baseAt + away.normalized * 1.8f + Vector3.up * 0.3f;
                }
                Release(drop);
                if (!done) fallFrom = drop.y;
            }
        }

        void ShowBody(HumanBody.Pose pose, float crouch)
        {
            var body = diver != null ? diver.body : null;
            if (body == null) return;
            if (pose != HumanBody.Pose.Stand) body.gameObject.SetActive(true);   // a vault keeps the current camera's choice
            body.pose = pose;
            body.crouch = crouch;
            body.armed = false;
            body.speed = 0f;
        }
    }
}
