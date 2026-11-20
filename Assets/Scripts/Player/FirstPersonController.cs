using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Player
{
    /// <summary>
    /// First-person walker. WASD to move, mouse to look, Shift to sprint, Space to jump, F for the flashlight.
    /// Uses the classic Input Manager, which every new Unity project has by default.
    /// When a GameHud is in the scene it owns Esc (pause); otherwise Esc just frees the mouse.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public Transform cameraPivot;
        public Light flashlight;

        [Header("Movement")]
        public float walkSpeed = 4.5f;
        public float sprintSpeed = 8f;
        public float jumpHeight = 1.1f;
        public float gravity = -20f;
        /// <summary>How fast walking speeds up and slows down on the ground (m/s²), and how much you can steer in the air.</summary>
        public float groundAccel = 60f, groundDecel = 45f, airAccel = 10f;
        /// <summary>A jump still works this long after running off an edge, and a press this early before landing counts.</summary>
        public float coyoteTime = 0.12f, jumpBuffer = 0.15f;

        [Header("Body")]
        /// <summary>
        /// The player's capsule, set here as well as by the scene builder, so a scene built before a
        /// change of size (the old 1.8 m, 0.35 m capsule jammed in doorways) still gets the new one.
        /// </summary>
        public float bodyHeight = 1.75f, bodyRadius = 0.28f, stepHeight = 0.42f, eyeHeight = 1.58f;
        /// <summary>Duck by itself under a low lintel or beam while walking, and stand up again after it.</summary>
        public bool autoDuck = true;

        [Header("Look")]
        public float mouseSensitivity = 2f;

        [Header("Feel")]
        public float bobAmount = 0.035f;

        /// <summary>Set by PlayerVitals: false while out of stamina.</summary>
        [HideInInspector] public bool sprintAllowed = true;

        /// <summary>
        /// When true, another script moves the player (the battle royale drop): this one only turns
        /// the view with the mouse and places the camera.
        /// </summary>
        [HideInInspector] public bool externalControl;
        /// <summary>Above zero, the camera sits this far behind the player (third person, used for the drop).</summary>
        [HideInInspector] public float thirdPersonDistance;
        /// <summary>Scales walking and sprinting (aiming down sights slows you).</summary>
        [HideInInspector] public float speedScale = 1f;
        /// <summary>Sideways offset of the third-person camera (over the right shoulder).</summary>
        [HideInInspector] public float shoulderOffset;
        /// <summary>Scales speed for the ground underfoot (wading in water).</summary>
        [HideInInspector] public float wadeScale = 1f;
        /// <summary>Asked first when Space is pressed on the ground; returning true takes the jump (a vault).</summary>
        public System.Func<bool> jumpOverride;
        /// <summary>Follows the head (standing or crouched), whatever the camera is doing; others aim at it.</summary>
        public Transform headMarker;
        /// <summary>Colliders under this are ignored when pulling the third-person camera in (the vehicle being ridden).</summary>
        [HideInInspector] public Transform cameraIgnore;
        /// <summary>Set while something else flies the camera (the lift's cutaway); the view is left alone.</summary>
        [HideInInspector] public bool cameraHeld;
        /// <summary>C or Left Ctrl toggles crouching (battle royale only).</summary>
        [HideInInspector] public bool crouchEnabled;

        public bool Crouching { get; private set; }
        /// <summary>0 standing .. 1 crouched, eased.</summary>
        public float CrouchAmount { get { return crouchT; } }
        /// <summary>Where the head is, whatever the camera is doing (third person pulls the camera back).</summary>
        public Vector3 HeadPosition { get { return transform.TransformPoint(pivotBase - new Vector3(0f, crouchT * CrouchDrop, 0f)); } }

        public float Pitch { get { return pitch; } }

        public bool IsSprinting { get; private set; }
        public bool IsGrounded { get { return controller != null && controller.isGrounded; } }
        public float HorizontalSpeed { get; private set; }

        CharacterController controller;
        Vector3 velocity;
        Vector3 planar;                 // horizontal velocity, eased towards what the keys ask for
        float lastGroundedTime = -10f, jumpPressedAt = -10f;
        bool wasGrounded;
        float stepY;                    // camera height eased over steps and kerbs (metres, settles to 0)
        Vector3 spawnPoint;
        Vector3 pivotBase;
        float pitch;
        float recoilPitch;
        float shake;
        float bobPhase;
        float camDistance;
        float recoilYaw;
        readonly RaycastHit[] camHits = new RaycastHit[12];
        float crouchT;
        float standHeight;
        Vector3 standCenter;
        const float CrouchDrop = 0.6f;
        bool autoDucked;                // crouching only because of a low opening (stands up by itself)
        float duckHold;
        readonly RaycastHit[] probeHits = new RaycastHit[16];

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            controller.height = bodyHeight;
            controller.radius = bodyRadius;
            controller.center = new Vector3(0f, bodyHeight * 0.5f, 0f);
            controller.stepOffset = Mathf.Min(stepHeight, bodyHeight * 0.5f);
            controller.skinWidth = 0.035f;
            controller.slopeLimit = 50f;
            controller.minMoveDistance = 0f;
            if (cameraPivot == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) cameraPivot = cam.transform;
            }
            if (cameraPivot != null)
            {
                pivotBase = cameraPivot.localPosition;
                pivotBase.y = eyeHeight;
            }
            standHeight = controller.height;
            standCenter = controller.center;
        }

        void Start()
        {
            spawnPoint = transform.position;
            GameState.SetCursorLocked(true);
        }

        void Update()
        {
            if (!GameState.HudActive && Input.GetKeyDown(KeyCode.Escape)) GameState.SetCursorLocked(false);
            if (GameState.Paused || GameState.PlayerDead) return;
            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && !GameState.MapOpen) GameState.SetCursorLocked(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
                float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
                transform.Rotate(0f, mx, 0f);
                pitch = Mathf.Clamp(pitch - my, -85f, 85f);
            }

            if (externalControl)
            {
                IsSprinting = false;
                HorizontalSpeed = 0f;
                planar = Vector3.zero;
                wasGrounded = false;
                stepY = 0f;
                UpdateCamera();
                return;
            }

            if (crouchEnabled && (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.LeftControl))) SetCrouch(!(Crouching && !autoDucked));
            if (autoDuck) AutoDuck(Time.deltaTime);
            UpdateCrouch();

            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            input = Vector3.ClampMagnitude(input, 1f);
            bool wantSprint = sprintAllowed && Input.GetKey(KeyCode.LeftShift) && input.z > 0.1f;
            if (wantSprint && Crouching && !autoDucked) SetCrouch(false);   // sprinting stands you up
            IsSprinting = wantSprint && !Crouching;
            // Ducking under a lintel only slows you a little; a real crouch is a creep.
            float speed = (IsSprinting ? sprintSpeed : walkSpeed) * speedScale * wadeScale * (Crouching ? (autoDucked ? 0.8f : 0.48f) : 1f);
            float dt = Time.deltaTime;

            // Speed builds and fades over a fraction of a second instead of snapping, so starts, stops
            // and turns feel weighted; in the air you can only nudge the way you are flying.
            bool grounded = controller.isGrounded;
            if (grounded) lastGroundedTime = Time.time;
            Vector3 wish = transform.TransformDirection(input) * speed;
            float accel = grounded ? (wish.sqrMagnitude >= planar.sqrMagnitude ? groundAccel : groundDecel) : airAccel;
            planar = Vector3.MoveTowards(planar, wish, accel * dt);

            if (grounded && velocity.y < 0f) velocity.y = -2f;
            if (Input.GetButtonDown("Jump")) jumpPressedAt = Time.time;
            bool jumped = false;
            if (Time.time - jumpPressedAt <= jumpBuffer && Time.time - lastGroundedTime <= coyoteTime && velocity.y <= 0.1f)
            {
                jumpPressedAt = -10f;
                if (jumpOverride != null && jumpOverride()) { }
                else if (Crouching) SetCrouch(false);
                else
                {
                    velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    lastGroundedTime = -10f;
                    jumped = true;
                }
            }
            if (!controller.enabled) { wasGrounded = false; UpdateCamera(); return; }   // a vault took over
            velocity.y += gravity * dt;

            Vector3 before = transform.position;
            var flags = controller.Move((planar + Vector3.up * velocity.y) * dt);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;   // head on a ceiling
            // Walked head first into something low that the look-ahead missed: duck under it.
            if (autoDuck && !Crouching && (flags & CollisionFlags.Above) != 0 && wasGrounded && !jumped && planar.sqrMagnitude > 0.2f && FitsCrouched())
            {
                Crouching = true;
                autoDucked = true;
                duckHold = 0.3f;
            }

            // Stay on the ground going down slopes and stairs instead of hopping off every step.
            if (wasGrounded && !controller.isGrounded && !jumped && velocity.y <= 0f)
            {
                float snap = controller.stepOffset + 0.15f + planar.magnitude * dt;
                Vector3 feet = transform.position + controller.center + Vector3.down * (controller.height * 0.5f - controller.radius);
                RaycastHit g;
                if (Physics.SphereCast(feet, controller.radius * 0.9f, Vector3.down, out g, snap + controller.skinWidth, ~0, QueryTriggerInteraction.Ignore)
                    && !g.collider.transform.IsChildOf(transform) && g.normal.y > 0.6f)
                    controller.Move(Vector3.down * (snap + controller.skinWidth));
            }
            wasGrounded = controller.isGrounded;

            Vector3 moved = transform.position - before;
            float rise = moved.y;
            moved.y = 0f;
            HorizontalSpeed = dt > 0f ? moved.magnitude / dt : 0f;
            // Walking into a wall bleeds off the speed you were carrying into it.
            if (dt > 0f && HorizontalSpeed < planar.magnitude * 0.5f) planar = Vector3.MoveTowards(planar, moved / dt, groundDecel * dt);

            // Steps and kerbs lift the body at once; the eye follows smoothly.
            // (Only real steps: a jump in height bigger than an ordinary slope would give this frame.)
            if (wasGrounded && Mathf.Abs(rise) > 0.07f && Mathf.Abs(rise) > moved.magnitude * 0.9f && Mathf.Abs(rise) < 0.6f) stepY = Mathf.Clamp(stepY - rise, -0.5f, 0.5f);
            stepY = Mathf.Lerp(stepY, 0f, 1f - Mathf.Exp(-14f * dt));

            if (flashlight != null && Input.GetKeyDown(KeyCode.F)) flashlight.enabled = !flashlight.enabled;

            UpdateCamera();

            // Safety net: if we ever fall through the world, go back to the start.
            if (transform.position.y < -150f) Teleport(spawnPoint); // below the lowest ravine
        }

        /// <summary>Crouches or stands (standing needs headroom). A crouch asked for this way stays until asked off.</summary>
        public void SetCrouch(bool on)
        {
            if (on == Crouching) { if (on) autoDucked = false; return; }
            if (!on && !Headroom()) return;
            Crouching = on;
            autoDucked = false;
        }

        /// <summary>The nearest hit of a sphere cast that isn't the player's own collider.</summary>
        bool Probe(Vector3 from, float r, Vector3 dir, float distance, out RaycastHit best)
        {
            best = new RaycastHit();
            int n = Physics.SphereCastNonAlloc(from, r, dir, probeHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = probeHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance <= 0f && h.point == Vector3.zero) continue;   // started inside it
                if (h.distance < nearest) { nearest = h.distance; best = h; }
            }
            return nearest < float.MaxValue;
        }

        /// <summary>Room to stand up here.</summary>
        bool Headroom()
        {
            float r = controller.radius * 0.9f;
            Vector3 bottom = transform.position + Vector3.up * (r + 0.05f);
            RaycastHit hit;
            return !Probe(bottom, r, Vector3.up, standHeight - r * 2f - 0.05f, out hit);
        }

        /// <summary>Room for the crouched body here.</summary>
        bool FitsCrouched()
        {
            float r = controller.radius * 0.9f;
            Vector3 bottom = transform.position + Vector3.up * (r + 0.05f);
            RaycastHit hit;
            return !Probe(bottom, r, Vector3.up, standHeight - CrouchDrop - r * 2f - 0.05f, out hit);
        }

        /// <summary>
        /// Something low ahead that a ducked body passes under: the standing head would hit it, but
        /// nothing at the crouched head's height or at the waist is in the way for a while after it
        /// (a wall stops all three, so walls never make you duck).
        /// </summary>
        bool LowOpeningAhead(Vector3 dir, float speed)
        {
            float r = controller.radius * 0.85f;
            float reach = controller.radius + 0.3f + speed * 0.22f;
            Vector3 feet = transform.position;
            RaycastHit hit;
            if (!Probe(feet + Vector3.up * (standHeight - r - 0.02f), r, dir, reach, out hit)) return false;
            if (hit.normal.y > 0.3f) return false;   // the ground rising ahead (a ramp, a step), not a beam
            float clear = hit.distance + 0.6f;
            if (Probe(feet + Vector3.up * (standHeight - CrouchDrop - r - 0.06f), r, dir, clear, out hit)) return false;
            if (Probe(feet + Vector3.up * Mathf.Max(controller.stepOffset + r + 0.05f, 0.8f), r, dir, clear, out hit)) return false;
            return true;
        }

        /// <summary>Ducks under low openings while walking and stands up once there is room again.</summary>
        void AutoDuck(float dt)
        {
            Vector3 flat = new Vector3(planar.x, 0f, planar.z);
            float speed = flat.magnitude;
            if (!Crouching)
            {
                if (controller.isGrounded && speed > 0.4f && LowOpeningAhead(flat / speed, speed))
                {
                    Crouching = true;
                    autoDucked = true;
                    duckHold = 0.3f;
                }
                return;
            }
            if (!autoDucked) return;
            duckHold -= dt;
            if (duckHold > 0f) return;
            if (speed > 0.4f && LowOpeningAhead(flat / speed, speed)) { duckHold = 0.15f; return; }
            if (Headroom())
            {
                Crouching = false;
                autoDucked = false;
            }
        }

        void UpdateCrouch()
        {
            float target = Crouching ? 1f : 0f;
            if (Mathf.Approximately(crouchT, target)) return;
            crouchT = Mathf.MoveTowards(crouchT, target, Time.deltaTime * (autoDucked ? 8f : 5f));
            float h = Mathf.Lerp(standHeight, standHeight - CrouchDrop, crouchT);
            controller.height = h;
            controller.center = new Vector3(standCenter.x, standCenter.y - (standHeight - h) * 0.5f, standCenter.z);
        }

        void UpdateCamera()
        {
            if (cameraPivot == null || cameraHeld) return;
            recoilPitch = Mathf.Lerp(recoilPitch, 0f, Time.deltaTime * 9f);
            recoilYaw = Mathf.Lerp(recoilYaw, 0f, Time.deltaTime * 9f);
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);

            float bob = 0f;
            if (controller.isGrounded && HorizontalSpeed > 0.5f)
            {
                bobPhase += Time.deltaTime * HorizontalSpeed * 1.9f;
                bob = Mathf.Sin(bobPhase) * bobAmount * Mathf.Clamp01(HorizontalSpeed / walkSpeed);
            }
            Vector3 jitter = shake > 0f ? Random.insideUnitSphere * shake * 0.08f : Vector3.zero;
            camDistance = Mathf.MoveTowards(camDistance, thirdPersonDistance, Time.deltaTime * 12f);
            Vector3 head = pivotBase - new Vector3(0f, crouchT * CrouchDrop, 0f);
            if (!externalControl) head.y += stepY;
            Vector3 back = Vector3.zero;
            if (camDistance > 0.01f)
            {
                float side = shoulderOffset * Mathf.Clamp01(camDistance / 1.5f);
                back = Quaternion.Euler(pitch, 0f, 0f) * new Vector3(side, 0.4f * Mathf.Clamp01(camDistance / 4f) + 0.15f, -camDistance);
                // Keep the camera out of walls: pull it in to whatever is behind the player.
                Vector3 from = transform.TransformPoint(head);
                Vector3 to = transform.TransformPoint(head + back);
                Vector3 d = to - from;
                float len = d.magnitude;
                if (len > 0.01f)
                {
                    int n = Physics.SphereCastNonAlloc(from, 0.22f, d / len, camHits, len, ~0, QueryTriggerInteraction.Ignore);
                    float nearest = len;
                    for (int i = 0; i < n; i++)
                    {
                        var t = camHits[i].collider.transform;
                        if (t.IsChildOf(transform) || (cameraIgnore != null && t.IsChildOf(cameraIgnore))) continue;
                        if (camHits[i].distance < nearest) nearest = camHits[i].distance;
                    }
                    if (nearest < len) back *= Mathf.Max(0.05f, (nearest - 0.05f) / len);
                }
            }
            cameraPivot.localPosition = head + new Vector3(0f, bob, 0f) + jitter + back;
            if (headMarker != null) headMarker.localPosition = head;
            cameraPivot.localEulerAngles = new Vector3(pitch - recoilPitch, -recoilYaw, jitter.x * 20f);
        }

        /// <summary>Kicks the view up; it settles back over a fraction of a second.</summary>
        public void AddRecoil(float degrees)
        {
            AddRecoil(degrees, 0f);
        }

        /// <summary>
        /// Kicks the view up and sideways. Part of it settles back, part stays (the gun climbs), so a
        /// held spray walks up and to one side unless the mouse pulls against it.
        /// </summary>
        public void AddRecoil(float up, float side)
        {
            recoilPitch += up;
            recoilYaw += side;
            pitch = Mathf.Clamp(pitch - up * 0.35f, -85f, 85f); // part of the kick stays, like a real gun
            transform.Rotate(0f, side * 0.4f, 0f);
        }

        public void Shake(float amount)
        {
            shake = Mathf.Max(shake, amount);
        }

        /// <summary>Drops the camera to the ground when the player dies.</summary>
        public void Collapse()
        {
            if (cameraPivot == null) return;
            cameraPivot.localPosition = new Vector3(0f, 0.35f, 0f);
            cameraPivot.localEulerAngles = new Vector3(-10f, 0f, 70f);
        }

        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            velocity = Vector3.zero;
            planar = Vector3.zero;
            stepY = 0f;
        }
    }
}
