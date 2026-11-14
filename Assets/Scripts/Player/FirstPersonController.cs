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

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraPivot == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) cameraPivot = cam.transform;
            }
            if (cameraPivot != null) pivotBase = cameraPivot.localPosition;
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
            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) GameState.SetCursorLocked(true);

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

            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            input = Vector3.ClampMagnitude(input, 1f);
            IsSprinting = sprintAllowed && Input.GetKey(KeyCode.LeftShift) && input.z > 0.1f;
            float speed = (IsSprinting ? sprintSpeed : walkSpeed) * speedScale;
            Vector3 move = transform.TransformDirection(input) * speed;

            if (controller.isGrounded)
            {
                if (velocity.y < 0f) velocity.y = -2f;
                if (Input.GetButtonDown("Jump")) velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
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

        void UpdateCamera()
        {
            if (cameraPivot == null) return;
            recoilPitch = Mathf.Lerp(recoilPitch, 0f, Time.deltaTime * 9f);
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);

            float bob = 0f;
            if (controller.isGrounded && HorizontalSpeed > 0.5f)
            {
                bobPhase += Time.deltaTime * HorizontalSpeed * 1.9f;
                bob = Mathf.Sin(bobPhase) * bobAmount * Mathf.Clamp01(HorizontalSpeed / walkSpeed);
            }
            Vector3 jitter = shake > 0f ? Random.insideUnitSphere * shake * 0.08f : Vector3.zero;
            camDistance = Mathf.MoveTowards(camDistance, thirdPersonDistance, Time.deltaTime * 12f);
            Vector3 back = camDistance > 0.01f ? Quaternion.Euler(pitch, 0f, 0f) * new Vector3(0f, 0.4f, -camDistance) : Vector3.zero;
            cameraPivot.localPosition = pivotBase + new Vector3(0f, bob, 0f) + jitter + back;
            cameraPivot.localEulerAngles = new Vector3(pitch - recoilPitch, 0f, jitter.x * 20f);
        }

        /// <summary>Kicks the view up; it settles back over a fraction of a second.</summary>
        public void AddRecoil(float degrees)
        {
            recoilPitch += degrees;
            pitch = Mathf.Clamp(pitch - degrees * 0.35f, -85f, 85f); // part of the kick stays, like a real gun
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
