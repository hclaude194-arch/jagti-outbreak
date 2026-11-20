using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// What the camera does once the player is out. First a death camera: the view lifts out of the
    /// head to look down on the body, then turns to whoever took the player out (with a moment of slow
    /// motion offline). From the results screen the player can then spectate: the camera follows a
    /// living fighter over the shoulder, left and right mouse step through the others, the mouse orbits,
    /// and when the one being watched goes down it moves on to their killer. Online it only moves the
    /// local camera, so nothing here goes over the network.
    /// Runs after everything else (the walker, the gun and the vehicles also move the camera).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SpectatorCam : MonoBehaviour
    {
        public static SpectatorCam Instance { get; private set; }

        /// <summary>The fighter being watched (the killer during the death camera, if there is one).</summary>
        public Combatant Target { get; private set; }
        /// <summary>Following someone after the death camera (the HUD draws the spectator bar).</summary>
        public bool Spectating { get; private set; }
        /// <summary>Seconds since the player went down.</summary>
        public float DeathAge { get { return Time.unscaledTime - startTime; } }

        Camera cam;
        Combatant self, killer;
        float startTime;
        Vector3 startPos;
        Quaternion startRot;
        Vector3 pos;
        Quaternion rot;
        float orbitYaw, orbitPitch, idle;
        float switchAt = -1f;
        float slowUntil;
        readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>Starts the death camera on the player's camera (call before anything drops the camera to the ground).</summary>
        public static SpectatorCam Begin(Combatant self, Combatant killer)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("Spectator Camera");
            var s = go.AddComponent<SpectatorCam>();
            s.cam = cam;
            s.self = self;
            s.killer = killer != null && killer != self ? killer : null;
            s.Target = s.killer;
            s.startTime = Time.unscaledTime;
            s.startPos = cam.transform.position;
            s.startRot = cam.transform.rotation;
            s.pos = s.startPos;
            s.rot = s.startRot;
            // The camera leaves the player's head (and its gun view model) for good: the next match reloads the scene.
            cam.transform.SetParent(null, true);
            for (int i = 0; i < cam.transform.childCount; i++) cam.transform.GetChild(i).gameObject.SetActive(false);
            // A short slow-motion beat, offline only (online the match can't slow down for one player).
            if (!NetSession.Online)
            {
                Time.timeScale = 0.35f;
                s.slowUntil = Time.unscaledTime + 0.9f;
            }
            return s;
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (slowUntil > 0f && Time.timeScale > 0f && Time.timeScale < 1f) Time.timeScale = 1f;
        }

        /// <summary>Follow living fighters from the results screen.</summary>
        public void StartSpectating()
        {
            Spectating = true;
            if (Target == null || !Target.Alive) Target = Next(Target, 1);
            orbitYaw = 0f;
            orbitPitch = 0f;
        }

        public void StopSpectating()
        {
            Spectating = false;
        }

        /// <summary>Anyone besides the player still standing.</summary>
        public static bool AnyoneToWatch()
        {
            foreach (var c in Combatant.All) if (c != null && c.Alive && !c.isPlayer) return true;
            return false;
        }

        Combatant Next(Combatant from, int dir)
        {
            var all = Combatant.All;
            int n = all.Count;
            if (n == 0) return null;
            int start = from != null ? all.IndexOf(from) : -1;
            if (start < 0) start = dir > 0 ? -1 : 0;
            for (int k = 1; k <= n; k++)
            {
                var c = all[((start + dir * k) % n + n) % n];
                if (c != null && c.Alive && !c.isPlayer) return c;
            }
            return null;
        }

        void Update()
        {
            // Ease out of the slow motion (unless the pause menu has stopped time).
            if (slowUntil > 0f && Time.timeScale > 0f)
            {
                float left = slowUntil - Time.unscaledTime;
                Time.timeScale = left > 0.4f ? 0.35f : Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(left / 0.4f));
                if (left <= 0f) { Time.timeScale = 1f; slowUntil = 0f; }
            }
            if (!Spectating) return;

            // The one being watched went down: after a moment, follow whoever got them.
            if (Target == null || !Target.Alive)
            {
                if (switchAt < 0f) switchAt = Time.unscaledTime + 1.6f;
                if (Time.unscaledTime >= switchAt)
                {
                    var by = Target != null ? Target.LastAttacker : null;
                    Target = by != null && by.Alive && !by.isPlayer ? by : Next(Target, 1);
                    switchAt = -1f;
                }
            }
            else switchAt = -1f;

            if (!GameState.Paused || Time.timeScale > 0f)
            {
                if (Input.GetMouseButtonDown(0)) { Target = Next(Target, 1); switchAt = -1f; }
                if (Input.GetMouseButtonDown(1)) { Target = Next(Target, -1); switchAt = -1f; }
            }
            // Orbit with the mouse; the view drifts back behind them after a couple of seconds.
            float mx = Input.GetAxis("Mouse X"), my = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mx) + Mathf.Abs(my) > 0.01f)
            {
                orbitYaw += mx * GameSettings.Sensitivity;
                orbitPitch = Mathf.Clamp(orbitPitch - my * GameSettings.Sensitivity * (GameSettings.InvertY ? -1f : 1f), -25f, 50f);
                idle = 0f;
            }
            else if ((idle += Time.unscaledDeltaTime) > 2f)
            {
                float k = 1f - Mathf.Exp(-1.5f * Time.unscaledDeltaTime);
                orbitYaw = Mathf.Lerp(orbitYaw, 0f, k);
                orbitPitch = Mathf.Lerp(orbitPitch, 0f, k);
            }
        }

        void LateUpdate()
        {
            if (cam == null) { Destroy(gameObject); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Vector3 wantPos;
            Quaternion wantRot;
            if (Spectating && Target != null) Follow(out wantPos, out wantRot);
            else DeathView(out wantPos, out wantRot);

            float age = DeathAge;
            if (!Spectating && age < 1.4f)
            {
                // The first beat: rise smoothly out of the head.
                float t = Mathf.SmoothStep(0f, 1f, age / 1.4f);
                pos = Vector3.Lerp(startPos, wantPos, t);
                rot = Quaternion.Slerp(startRot, wantRot, t);
            }
            else
            {
                float k = 1f - Mathf.Exp(-(Spectating ? 7f : 3f) * dt);
                pos = Vector3.Lerp(pos, wantPos, k);
                rot = Quaternion.Slerp(rot, wantRot, 1f - Mathf.Exp(-(Spectating ? 10f : 3f) * dt));
            }
            cam.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Above and behind the body, looking down at it, then turning to the killer while they're in sight.</summary>
        void DeathView(out Vector3 wantPos, out Quaternion wantRot)
        {
            Vector3 body = self != null ? self.transform.position + Vector3.up * 0.4f : startPos;
            Vector3 away = startRot * Vector3.back;
            Vector3 toKiller = Vector3.zero;
            bool showKiller = killer != null && DeathAge > 1.6f;
            if (killer != null)
            {
                toKiller = killer.AimPoint - body;
                toKiller.y = 0f;
                if (toKiller.sqrMagnitude > 1f) away = -toKiller.normalized;   // stand on the far side, looking towards them
            }
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            away.Normalize();
            // A slow drift round the body keeps the shot alive.
            away = Quaternion.Euler(0f, Mathf.Sin(DeathAge * 0.35f) * 18f, 0f) * away;
            Vector3 want = body + away * 3.2f + Vector3.up * 2.6f;
            wantPos = Clear(body + Vector3.up * 0.6f, want);
            Vector3 look = body;
            if (showKiller)
            {
                // Frame the killer beyond the body (closer killers get a tighter look).
                Vector3 k = killer.Alive ? killer.AimPoint : killer.transform.position + Vector3.up * 0.5f;
                look = Vector3.Lerp(body, k, Mathf.Clamp01(12f / Mathf.Max(12f, (k - body).magnitude)) * 0.5f + 0.5f);
            }
            Vector3 dir = look - wantPos;
            wantRot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : rot;
        }

        /// <summary>Over the shoulder of the one being watched, turning with them, with the mouse orbit on top.</summary>
        void Follow(out Vector3 wantPos, out Quaternion wantRot)
        {
            var t = Target.transform;
            Vector3 pivot = t.position + Vector3.up * 1.65f;
            float yaw = t.eulerAngles.y + orbitYaw;
            var turn = Quaternion.Euler(14f + orbitPitch, yaw, 0f);
            Vector3 want = pivot + turn * new Vector3(0.55f, 0.35f, -3.6f);
            wantPos = Clear(pivot, want);
            wantRot = Quaternion.LookRotation(pivot + turn * new Vector3(0.3f, 0f, 8f) - wantPos);
        }

        /// <summary>Pulls the camera in front of walls between the subject and where it wants to be (people don't count).</summary>
        Vector3 Clear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return to;
            d /= len;
            int n = Physics.SphereCastNonAlloc(from, 0.25f, d, hits, len, ~0, QueryTriggerInteraction.Ignore);
            float best = len;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.distance <= 0f) continue;
                if (h.collider.GetComponentInParent<Combatant>() != null) continue;
                if (h.distance < best) best = h.distance;
            }
            return from + d * Mathf.Max(0.3f, best - 0.1f);
        }
    }
}
