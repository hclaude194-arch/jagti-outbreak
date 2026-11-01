using UnityEngine;

namespace Jagti.Player
{
    /// <summary>
    /// Simple first-person walker for exploring the campus.
    /// WASD to move, mouse to look, Shift to sprint, Space to jump, F for the flashlight.
    /// Esc frees the mouse; click to capture it again.
    /// Uses the classic Input Manager, which every new Unity project has by default.
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

        CharacterController controller;
        Vector3 velocity;
        Vector3 spawnPoint;
        float pitch;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraPivot == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) cameraPivot = cam.transform;
            }
        }

        void Start()
        {
            spawnPoint = transform.position;
            SetCursorLocked(true);
        }

        void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
                float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
                transform.Rotate(0f, mx, 0f);
                pitch = Mathf.Clamp(pitch - my, -85f, 85f);
                if (cameraPivot != null) cameraPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }

            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            input = Vector3.ClampMagnitude(input, 1f);
            float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
            Vector3 move = transform.TransformDirection(input) * speed;

            if (controller.isGrounded)
            {
                if (velocity.y < 0f) velocity.y = -2f;
                if (Input.GetButtonDown("Jump")) velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            velocity.y += gravity * Time.deltaTime;

            controller.Move((move + Vector3.up * velocity.y) * Time.deltaTime);

            if (flashlight != null && Input.GetKeyDown(KeyCode.F)) flashlight.enabled = !flashlight.enabled;

            // Safety net: if we ever fall through the world, go back to the start.
            if (transform.position.y < -50f) Teleport(spawnPoint);
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
