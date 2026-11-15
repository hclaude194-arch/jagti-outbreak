using Jagti.Gameplay;
using Jagti.Player;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The player's drop: ride the helicopter (look around, check the map), jump with Space,
    /// free-fall (W dives faster, S slows), open the glider with Space or automatically low down,
    /// then steer it with the mouse (W for speed, S to float) onto a roof, lawn or doorway.
    /// The camera pulls back behind the player's body for the drop and returns to first person on landing.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public class SkyDiver : MonoBehaviour
    {
        public enum State { Waiting, InHelicopter, FreeFall, Glide, Landed }

        public Helicopter helicopter;
        public HumanBody body;

        [Header("Body looks (the body is built at runtime)")]
        public Material skin;
        public Material shirt;
        public Material pants;
        public Material gear;
        public Material gun;
        public Material canopy;

        [Header("Free fall")]
        public float fallSpeed = 44f;
        public float diveSpeed = 60f;
        public float fallForward = 16f;
        public float autoOpenHeight = 130f;
        public float manualOpenBelow = 500f;

        [Header("Glider")]
        public float glideSpeed = 12f;
        public float glideFast = 17f;
        public float glideSlow = 6.5f;
        public float glideSink = 4.6f;
        public float glideSinkFast = 7f;
        public float glideSinkSlow = 3.2f;

        public State Current { get; private set; }
        public float HeightAboveGround { get; private set; }
        public float VerticalSpeed { get; private set; }

        FirstPersonController fpc;
        CharacterController controller;
        Combatant combatant;
        AudioSource wind;
        Vector3 velocity;
        float jumpLock;

        void Awake()
        {
            fpc = GetComponent<FirstPersonController>();
            controller = GetComponent<CharacterController>();
            combatant = GetComponent<Combatant>();
            wind = gameObject.AddComponent<AudioSource>();
            wind.clip = SynthAudio.Get("wind");
            wind.loop = true;
            wind.spatialBlend = 0f;
            wind.volume = 0f;
            wind.playOnAwake = false;
        }

        void Start()
        {
            if (body == null)
            {
                body = HumanBody.Build(transform, skin, shirt, pants, gear, gun, canopy);
                body.gameObject.SetActive(Current != State.Landed && Current != State.Waiting);
            }
        }

        /// <summary>Puts the player aboard; called by MatchDirector when the helicopter sets off.</summary>
        public void Board()
        {
            Current = State.InHelicopter;
            jumpLock = 2f;   // no accidental jump in the first seconds
            fpc.externalControl = true;
            fpc.thirdPersonDistance = 16f;
            controller.enabled = false;
            if (combatant != null) combatant.Landed = false;
            ShowBody(true, HumanBody.Pose.Stand);
        }

        public void Jump()
        {
            if (Current != State.InHelicopter) return;
            Current = State.FreeFall;
            transform.position = helicopter.Door;
            controller.enabled = true;
            velocity = helicopter.transform.forward * 20f;   // carried along by the helicopter at first
            fpc.thirdPersonDistance = 5f;
            ShowBody(true, HumanBody.Pose.FreeFall);
            wind.Play();
            SynthAudio.Play2D("whoosh", 0.7f);
            var director = MatchDirector.Instance;
            if (director != null) director.OnPlayerJumped();
        }

        void Open()
        {
            if (Current != State.FreeFall) return;
            Current = State.Glide;
            fpc.thirdPersonDistance = 6.5f;
            ShowBody(true, HumanBody.Pose.Glide);
            SynthAudio.Play2D("canopy", 0.8f);
        }

        void Land()
        {
            Current = State.Landed;
            fpc.externalControl = false;
            fpc.thirdPersonDistance = 0f;
            ShowBody(false, HumanBody.Pose.Stand);
            wind.Stop();
            SynthAudio.Play2D("land", 0.8f);
            if (combatant != null) combatant.Landed = true;
        }

        void ShowBody(bool on, HumanBody.Pose pose)
        {
            if (body == null) return;
            body.gameObject.SetActive(on);
            body.pose = pose;
            body.armed = false;
        }

        void Update()
        {
            // Offline the pause menu stops time; online the fall goes on behind it, without input.
            if (GameState.Paused && Time.timeScale == 0f) return;
            live = !GameState.Paused;
            float dt = Time.deltaTime;
            switch (Current)
            {
                case State.InHelicopter:
                    if (helicopter == null) { Land(); break; }
                    transform.position = helicopter.Door;
                    jumpLock -= dt;
                    if ((jumpLock <= 0f && live && Input.GetKeyDown(KeyCode.Space)) || helicopter.Progress >= 0.985f) Jump();
                    break;
                case State.FreeFall:
                    MeasureHeight();
                    if (HeightAboveGround < autoOpenHeight || (HeightAboveGround < manualOpenBelow && live && Input.GetKeyDown(KeyCode.Space))) Open();
                    else MoveFall(dt);
                    break;
                case State.Glide:
                    MeasureHeight();
                    MoveGlide(dt);
                    break;
            }
            if (Current == State.FreeFall || Current == State.Glide)
            {
                float target = Current == State.FreeFall ? 0.55f : 0.22f;
                wind.volume = Mathf.MoveTowards(wind.volume, target, dt);
            }
        }

        bool live = true;

        /// <summary>Rebuilds the body in other colours (online every player gets their own look).</summary>
        public void Redress(Material newSkin, Material newShirt, Material newPants, Material newCanopy)
        {
            if (newSkin != null) skin = newSkin;
            if (newShirt != null) shirt = newShirt;
            if (newPants != null) pants = newPants;
            if (newCanopy != null) canopy = newCanopy;
            if (body == null) return;
            bool on = body.gameObject.activeSelf;
            var pose = body.pose;
            Destroy(body.gameObject);
            body = HumanBody.Build(transform, skin, shirt, pants, gear, gun, canopy);
            body.pose = pose;
            body.gameObject.SetActive(on);
        }

        void MeasureHeight()
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                HeightAboveGround = hit.distance - 0.2f;
            else
                HeightAboveGround = transform.position.y - HazeZone.GroundY(transform.position.x, transform.position.z);
        }

        void MoveFall(float dt)
        {
            float v = live ? Input.GetAxisRaw("Vertical") : 0f;
            float h = live ? Input.GetAxisRaw("Horizontal") : 0f;
            float down = v > 0.1f ? diveSpeed : v < -0.1f ? fallSpeed * 0.8f : fallSpeed;
            Vector3 wish = transform.forward * (v > 0.1f ? fallForward * 0.6f : v < -0.1f ? fallForward * 1.2f : fallForward)
                         + transform.right * h * 8f;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            flat = Vector3.MoveTowards(flat, wish, 12f * dt);
            velocity = new Vector3(flat.x, Mathf.MoveTowards(velocity.y, -down, 30f * dt), flat.z);
            Move(dt);
        }

        void MoveGlide(float dt)
        {
            float v = live ? Input.GetAxisRaw("Vertical") : 0f;
            float h = live ? Input.GetAxisRaw("Horizontal") : 0f;
            float speed = v > 0.1f ? glideFast : v < -0.1f ? glideSlow : glideSpeed;
            float sink = v > 0.1f ? glideSinkFast : v < -0.1f ? glideSinkSlow : glideSink;
            Vector3 wish = transform.forward * speed + transform.right * h * 3f;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            flat = Vector3.MoveTowards(flat, wish, 8f * dt);
            velocity = new Vector3(flat.x, Mathf.MoveTowards(velocity.y, -sink, 25f * dt), flat.z);
            Move(dt);
            if (controller.isGrounded || HeightAboveGround < 0.15f) Land();
        }

        void Move(float dt)
        {
            VerticalSpeed = velocity.y;
            var flags = controller.Move(velocity * dt);
            if ((flags & CollisionFlags.Below) != 0 && Current == State.FreeFall)
            {
                // Hit a roof before the canopy opened: open now and settle.
                Open();
            }
            if ((flags & CollisionFlags.Sides) != 0)
            {
                // Bumping a wall stops the sideways drift.
                velocity.x *= 0.3f;
                velocity.z *= 0.3f;
            }
        }
    }
}
