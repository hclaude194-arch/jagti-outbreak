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

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraPivot == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) cameraPivot = cam.transform;
            }
            if (cameraPivot != null) pivotBase = cameraPivot.localPosition;
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
                UpdateCamera();
                return;
            }

            if (crouchEnabled && (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.LeftControl))) SetCrouch(!Crouching);
            UpdateCrouch();

            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            input = Vector3.ClampMagnitude(input, 1f);
            bool wantSprint = sprintAllowed && Input.GetKey(KeyCode.LeftShift) && input.z > 0.1f;
            if (wantSprint && Crouching) SetCrouch(false);   // sprinting stands you up
            IsSprinting = wantSprint && !Crouching;
            float speed = (IsSprinting ? sprintSpeed : walkSpeed) * speedScale * wadeScale * (Crouching ? 0.48f : 1f);
            Vector3 move = transform.TransformDirection(input) * speed;

            if (controller.isGrounded)
            {
                if (velocity.y < 0f) velocity.y = -2f;
                if (Input.GetButtonDown("Jump"))
                {
                    if (jumpOverride != null && jumpOverride()) { }
                    else if (Crouching) SetCrouch(false);
                    else velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                }
            }
            velocity.y += gravity * Time.deltaTime;

            Vector3 before = transform.position;
            controller.Move((move + Vector3.up * velocity.y) * Time.deltaTime);
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            HorizontalSpeed = Time.deltaTime > 0f ? moved.magnitude / Time.deltaTime : 0f;

            if (flashlight != null && Input.GetKeyDown(KeyCode.F)) flashlight.enabled = !flashlight.enabled;

            UpdateCamera();

            // Safety net: if we ever fall through the world, go back to the start.
            if (transform.position.y < -150f) Teleport(spawnPoint); // below the lowest ravine
        }

        /// <summary>Crouches or stands (standing needs headroom).</summary>
        public void SetCrouch(bool on)
        {
            if (on == Crouching) return;
            if (!on)
            {
                float r = controller.radius * 0.9f;
                Vector3 bottom = transform.position + Vector3.up * (r + 0.05f);
                if (Physics.SphereCast(bottom, r, Vector3.up, out RaycastHit hit, standHeight - r * 2f - 0.05f, ~0, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(transform)) return;
            }
            Crouching = on;
        }

        void UpdateCrouch()
        {
            float target = Crouching ? 1f : 0f;
            if (Mathf.Approximately(crouchT, target)) return;
            crouchT = Mathf.MoveTowards(crouchT, target, Time.deltaTime * 5f);
            float h = Mathf.Lerp(standHeight, standHeight - CrouchDrop, crouchT);
            controller.height = h;
            controller.center = new Vector3(standCenter.x, standCenter.y - (standHeight - h) * 0.5f, standCenter.z);
        }

        void UpdateCamera()
        {
            if (cameraPivot == null) return;
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
        }
    }
}
