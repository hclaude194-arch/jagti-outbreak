using Jagti.Player;
using UnityEngine;

namespace Jagti.Vehicles
{
    /// <summary>
    /// The campus shuttle bus. It runs its route on its own, stopping at each bus stop.
    /// Walk to its door (on the left side, behind the front wheel) and press E to get on.
    /// Inside: E gets off, T swaps between riding and driving, C swaps the driving view.
    /// Driving: W/S accelerate and reverse, A/D steer, Space brakes, G sounds the horn.
    /// When you get off after driving, the bus stays parked; press E at the door to drive
    /// again, or T while inside to hand it back to its own driver.
    /// Movement is arcade style: the bus follows the ground by raycasts under its wheels and
    /// stops when something stands in its way, so it never needs wheel physics tuning.
    /// </summary>
    public class CampusBus : MonoBehaviour
    {
        public enum Mode { Route, Driving, Parked }

        [Header("Route (world positions, a closed loop)")]
        public Vector3[] route;
        public int[] stopIndex;      // route index of each stop
        public string[] stopName;
        public float routeSpeed = 9f;     // m/s, about 32 km/h
        public float stopSeconds = 9f;
        public int startIndex;

        [Header("Parts")]
        public Transform driverSeat;
        public Transform passengerSeat;
        public Transform door;           // a point just outside the door
        public Transform chaseView;
        public Transform[] wheels;
        public Transform[] steeredWheels;

        [Header("Handling")]
        public float maxSpeed = 15f;
        public float maxReverse = 4f;
        public float acceleration = 2.6f;
        public float brakeForce = 7f;
        public float maxSteer = 36f;
        public float wheelBase = 6.0f;   // front axle to rear axle
        public float frontAxle = 3.6f;   // local z of the front axle
        public float halfTrack = 1.05f;
        public float wheelRadius = 0.5f;
        public Vector3 bodyHalfSize = new Vector3(1.25f, 1.5f, 5.5f);

        public Mode mode = Mode.Route;
        public float Speed { get; private set; }

        FirstPersonController player;
        CharacterController playerController;
        Transform cameraTransform;
        Transform cameraHome;
        Vector3 cameraHomePos;
        Quaternion cameraHomeRot;
        bool aboard, atWheel, chase;
        float lookYaw, lookPitch;
        float steer;
        int target;
        int nextStop;
        float waitUntil = -1f;
        string message;
        float messageUntil;
        Collider[] ownColliders;
        AudioSource engine, horn;

        void Start()
        {
            player = FindObjectOfType<FirstPersonController>();
            if (player != null)
            {
                playerController = player.GetComponent<CharacterController>();
                cameraTransform = player.cameraPivot != null ? player.cameraPivot : player.GetComponentInChildren<Camera>().transform;
            }
            ownColliders = GetComponentsInChildren<Collider>();
            target = route != null && route.Length > 0 ? (startIndex + 1) % route.Length : 0;
            nextStop = FirstStopAfter(startIndex);
            if (mode == Mode.Route && route != null && route.Length > 0) waitUntil = Time.time + stopSeconds;
            MakeSounds();
        }

        // ------------------------------------------------------------------ boarding

        void Update()
        {
            if (player == null) return;
            bool nearDoor = !aboard && door != null &&
                            (player.transform.position - door.position).sqrMagnitude < 3.2f * 3.2f;

            if (Input.GetKeyDown(KeyCode.E))
            {
                if (aboard)
                {
                    if (Speed > 2.5f) Say("Wait for the bus to slow down");
                    else GetOff();
                }
                else if (nearDoor)
                {
                    if (mode == Mode.Route && Speed > 0.6f) Say("Wait for the bus to stop");
                    else GetOn(mode != Mode.Route);
                }
            }
            if (aboard && Input.GetKeyDown(KeyCode.T)) SwapDriver();
            if (aboard && atWheel && Input.GetKeyDown(KeyCode.C)) SetChase(!chase);
            if (Input.GetKeyDown(KeyCode.G) && atWheel && horn != null) horn.Play();
            if (aboard) Look();

            switch (mode)
            {
                case Mode.Route: RouteDriver(); break;
                case Mode.Driving: PlayerDriver(); break;
                default: Speed = Mathf.MoveTowards(Speed, 0f, brakeForce * Time.deltaTime); Move(0f); break;
            }
            SpinWheels();
            if (engine != null)
            {
                engine.pitch = 0.55f + Mathf.Abs(Speed) / maxSpeed * 0.9f;
                engine.volume = mode == Mode.Parked ? 0.05f : 0.22f;
            }
        }

        void GetOn(bool drive)
        {
            aboard = true;
            player.enabled = false;
            if (playerController != null) playerController.enabled = false;
            if (drive) { mode = Mode.Driving; atWheel = true; }
            else atWheel = false;
            SeatPlayer();
            Say(atWheel ? "Driving: W/S, A/D, Space brake, G horn, C view, E get off"
                        : "Riding the bus. E get off, T take the wheel");
        }

        void SeatPlayer()
        {
            var seat = atWheel ? driverSeat : passengerSeat;
            player.transform.SetParent(seat, false);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
            lookYaw = 0f;
            lookPitch = 0f;
        }

        void GetOff()
        {
            SetChase(false);
            aboard = false;
            if (mode == Mode.Driving) mode = Mode.Parked;
            atWheel = false;
            player.transform.SetParent(null, true);
            Vector3 p = door != null ? door.position : transform.position - transform.right * 2.5f;
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y + 0.05f;
            player.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y - 90f, 0f);
            if (playerController != null) playerController.enabled = true;
            player.enabled = true;
            player.Teleport(p);
            if (cameraTransform != null) cameraTransform.localRotation = Quaternion.identity;
        }

        void SwapDriver()
        {
            SetChase(false);
            if (atWheel)
            {
                // Hand back to the route: carry on from the nearest point ahead.
                atWheel = false;
                mode = Mode.Route;
                target = NearestAhead();
                nextStop = FirstStopAfter(target);
                Say("The driver takes over. E get off, T take the wheel");
            }
            else
            {
                atWheel = true;
                mode = Mode.Driving;
                Say("Driving: W/S, A/D, Space brake, G horn, C view, E get off");
            }
            SeatPlayer();
        }

        void SetChase(bool on)
        {
            if (cameraTransform == null || chaseView == null || on == chase) return;
            chase = on;
            if (on)
            {
                cameraHome = cameraTransform.parent;
                cameraHomePos = cameraTransform.localPosition;
                cameraHomeRot = cameraTransform.localRotation;
                cameraTransform.SetParent(chaseView, false);
                cameraTransform.localPosition = Vector3.zero;
                cameraTransform.localRotation = Quaternion.identity;
            }
            else
            {
                cameraTransform.SetParent(cameraHome, false);
                cameraTransform.localPosition = cameraHomePos;
                cameraTransform.localRotation = cameraHomeRot;
            }
        }

        void Look()
        {
            // The walking controller is off while aboard, so handle the cursor here.
            if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (Cursor.lockState != CursorLockMode.Locked) return;
            float s = player.mouseSensitivity;
            lookYaw += Input.GetAxis("Mouse X") * s;
            lookPitch = Mathf.Clamp(lookPitch - Input.GetAxis("Mouse Y") * s, -70f, 70f);
            if (atWheel) lookYaw = Mathf.Clamp(lookYaw, -120f, 120f);
            if (chase)
            {
                chaseView.parent.localRotation = Quaternion.Euler(0f, lookYaw, 0f);
                return;
            }
            player.transform.localRotation = Quaternion.Euler(0f, lookYaw, 0f);
            if (cameraTransform != null) cameraTransform.localRotation = Quaternion.Euler(lookPitch, 0f, 0f);
        }

        // ------------------------------------------------------------------ drivers

        void PlayerDriver()
        {
            float throttle = Input.GetAxisRaw("Vertical");
            float turn = Input.GetAxisRaw("Horizontal");
            bool brake = Input.GetKey(KeyCode.Space);
            float dt = Time.deltaTime;

            if (brake || (throttle != 0f && Mathf.Sign(throttle) != Mathf.Sign(Speed) && Mathf.Abs(Speed) > 0.3f))
                Speed = Mathf.MoveTowards(Speed, 0f, brakeForce * dt);
            else if (throttle != 0f)
                Speed = Mathf.Clamp(Speed + throttle * acceleration * dt, -maxReverse, maxSpeed);
            else
                Speed = Mathf.MoveTowards(Speed, 0f, 0.8f * dt);   // rolling resistance

            // Less steering lock at speed, like a heavy bus.
            float lockAtSpeed = Mathf.Lerp(maxSteer, maxSteer * 0.35f, Mathf.Abs(Speed) / maxSpeed);
            steer = Mathf.MoveTowards(steer, turn * lockAtSpeed, 70f * dt);
            Move(steer);
        }

        void RouteDriver()
        {
            if (route == null || route.Length < 2) { Move(0f); return; }
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;

            // Waiting at a stop.
            if (waitUntil > 0f)
            {
                Speed = Mathf.MoveTowards(Speed, 0f, brakeForce * dt);
                Move(0f);
                if (Time.time > waitUntil)
                {
                    waitUntil = -1f;
                    nextStop = FirstStopAfter(target);
                }
                return;
            }

            // Advance along the route; aim at a point about 9 m ahead.
            for (int guard = 0; guard < route.Length && Flat(route[target] - pos).magnitude < 7f; guard++)
            {
                if (stopIndex != null && nextStop >= 0 && target == stopIndex[nextStop])
                {
                    waitUntil = Time.time + stopSeconds;
                    if (aboard) Say("Stop: " + stopName[nextStop] + ". E to get off");
                }
                target = (target + 1) % route.Length;
            }
            Vector3 aim = route[target];
            Vector3 to = Flat(aim - pos);
            float angle = Vector3.SignedAngle(Flat(transform.forward), to, Vector3.up);
            float wanted = Mathf.Clamp(angle * 1.1f, -maxSteer, maxSteer);
            steer = Mathf.MoveTowards(steer, wanted, 60f * dt);

            // Slow for bends and when nearing the next stop.
            float speedCap = routeSpeed * Mathf.Clamp01(1f - Mathf.Abs(angle) / 75f) + 2f;
            if (stopIndex != null && nextStop >= 0)
            {
                float d = Flat(route[stopIndex[nextStop]] - pos).magnitude;
                speedCap = Mathf.Min(speedCap, 1.5f + d * 0.45f);
            }
            float rate = Speed < speedCap ? acceleration : brakeForce;
            Speed = Mathf.MoveTowards(Speed, speedCap, rate * dt);
            Move(steer);
        }

        // ------------------------------------------------------------------ motion

        void Move(float steerDegrees)
        {
            float dt = Time.deltaTime;
            float step = Speed * dt;
            if (Mathf.Abs(step) > 0.0001f && Blocked(Mathf.Sign(step), Mathf.Abs(step)))
            {
                if (mode == Mode.Route && Speed > 0f && horn != null && !horn.isPlaying && Random.value < 0.01f) horn.Play();
                Speed = 0f;
                step = 0f;
            }
            // Bicycle model about the rear axle.
            float yawRate = Speed / wheelBase * Mathf.Tan(steerDegrees * Mathf.Deg2Rad) * Mathf.Rad2Deg;
            float yaw = transform.eulerAngles.y + yawRate * dt;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 pos = transform.position + fwd * step;

            // Follow the ground under the four wheels.
            Quaternion flatRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = flatRot * Vector3.right;
            float rearAxle = frontAxle - wheelBase;
            float fl = GroundY(pos + fwd * frontAxle - right * halfTrack, pos.y);
            float fr = GroundY(pos + fwd * frontAxle + right * halfTrack, pos.y);
            float rl = GroundY(pos + fwd * rearAxle - right * halfTrack, pos.y);
            float rr = GroundY(pos + fwd * rearAxle + right * halfTrack, pos.y);
            float front = (fl + fr) * 0.5f, rear = (rl + rr) * 0.5f;
            float pitch = -Mathf.Atan2(front - rear, wheelBase) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(rl + fl - rr - fr, 4f * halfTrack) * Mathf.Rad2Deg;
            // Height at the body centre (between the axles), from the axle line.
            float t = (0f - rearAxle) / wheelBase;
            float y = Mathf.Lerp(rear, front, t);
            pos.y = Mathf.Lerp(transform.position.y, y, 1f - Mathf.Exp(-12f * dt));
            var targetRot = Quaternion.Euler(pitch, yaw, roll);
            transform.SetPositionAndRotation(pos, Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-8f * dt)));

            if (steeredWheels != null)
                foreach (var w in steeredWheels)
                    if (w != null) w.parent.localRotation = Quaternion.Euler(0f, steerDegrees, 0f);
        }

        float GroundY(Vector3 at, float fallback)
        {
            var hits = Physics.RaycastAll(at + Vector3.up * 4f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (IsOwn(h.collider) || IsPlayer(h.collider)) continue;
                if (h.normal.y < 0.5f) continue;            // walls, not ground
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? fallback : best;
        }

        bool Blocked(float direction, float distance)
        {
            Vector3 centre = transform.position + transform.up * (bodyHalfSize.y + 0.4f);
            Vector3 half = new Vector3(bodyHalfSize.x - 0.1f, bodyHalfSize.y - 0.5f, 0.2f);
            Vector3 dir = transform.forward * direction;
            var hits = Physics.BoxCastAll(centre, half, dir, transform.rotation, bodyHalfSize.z + distance + 0.3f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (IsOwn(h.collider)) continue;
                if (aboard && IsPlayer(h.collider)) continue;
                if (h.collider is TerrainCollider) continue;
                if (h.normal.y > 0.6f) continue;                 // ground, kerbs and ramps
                if (h.distance <= 0f) continue;                  // already overlapping: let it drive out
                return true;
            }
            return false;
        }

        bool IsOwn(Collider c)
        {
            foreach (var o in ownColliders) if (o == c) return true;
            return false;
        }

        bool IsPlayer(Collider c)
        {
            return player != null && c.transform.IsChildOf(player.transform);
        }

        void SpinWheels()
        {
            if (wheels == null) return;
            float deg = Speed * Time.deltaTime / wheelRadius * Mathf.Rad2Deg;
            foreach (var w in wheels) if (w != null) w.Rotate(deg, 0f, 0f, Space.Self);
        }

        // ------------------------------------------------------------------ route helpers

        int FirstStopAfter(int index)
        {
            if (stopIndex == null || stopIndex.Length == 0 || route == null) return -1;
            int best = -1, bestAhead = int.MaxValue;
            for (int s = 0; s < stopIndex.Length; s++)
            {
                int ahead = (stopIndex[s] - index + route.Length) % route.Length;
                if (ahead == 0) ahead = route.Length;
                if (ahead < bestAhead) { bestAhead = ahead; best = s; }
            }
            return best;
        }

        int NearestAhead()
        {
            int best = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < route.Length; i++)
            {
                Vector3 to = Flat(route[i] - transform.position);
                if (Vector3.Dot(to, transform.forward) < 0f) continue;
                if (to.sqrMagnitude < bestScore) { bestScore = to.sqrMagnitude; best = i; }
            }
            return best;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ------------------------------------------------------------------ sound and hints

        void MakeSounds()
        {
            const int rate = 22050;
            var data = new float[rate];
            var rng = new System.Random(7);
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                // A diesel idle: low thump at 30 Hz with harmonics and a little rumble noise.
                data[i] = 0.35f * Mathf.Sin(2f * Mathf.PI * 30f * t) + 0.2f * Mathf.Sin(2f * Mathf.PI * 60f * t)
                        + 0.1f * Mathf.Sin(2f * Mathf.PI * 90f * t) + 0.08f * (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] *= 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 7.5f * t);
            }
            var idle = AudioClip.Create("BusEngine", data.Length, 1, rate, false);
            idle.SetData(data, 0);
            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = idle;
            engine.loop = true;
            engine.spatialBlend = 1f;
            engine.minDistance = 4f;
            engine.maxDistance = 60f;
            engine.volume = 0.22f;
            engine.Play();

            var hdata = new float[rate * 7 / 10];
            for (int i = 0; i < hdata.Length; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t * 30f) * Mathf.Clamp01((0.7f - t) * 20f);
                hdata[i] = env * 0.5f * (Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 370f * t)) * 0.5f + Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 466f * t)) * 0.5f);
            }
            var hclip = AudioClip.Create("BusHorn", hdata.Length, 1, rate, false);
            hclip.SetData(hdata, 0);
            horn = gameObject.AddComponent<AudioSource>();
            horn.clip = hclip;
            horn.spatialBlend = 1f;
            horn.minDistance = 8f;
            horn.maxDistance = 120f;
            horn.volume = 0.5f;
        }

        void Say(string text)
        {
            message = text;
            messageUntil = Time.time + 5f;
        }

        void OnGUI()
        {
            string text = null;
            if (Time.time < messageUntil) text = message;
            else if (!aboard && player != null && door != null &&
                     (player.transform.position - door.position).sqrMagnitude < 3.2f * 3.2f)
                text = mode == Mode.Route ? (Speed > 0.6f ? "Wait for the bus to stop" : "E: get on the bus") : "E: drive the bus";
            else if (aboard && mode == Mode.Route && stopIndex != null && nextStop >= 0)
                text = "Next stop: " + stopName[nextStop];
            if (string.IsNullOrEmpty(text)) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(Screen.width / 2f - 260f, Screen.height - 90f, 520f, 34f);
            GUI.Box(r, text, style);
        }
    }
}
