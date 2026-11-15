using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Online: someone whose own machine runs them (another player, or on a client every bot). It
    /// plays back the positions that machine sends a tenth of a second late, smoothing between them,
    /// and poses the body from the latest one. Its health only changes through results sent by the
    /// owner (Combatant.ApplyRemoteResult); a death here plays the fall and turns the hit areas off.
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    public class NetPuppet : MonoBehaviour
    {
        public HumanBody body;
        /// <summary>How far behind the newest sample to draw (seconds); covers a lost packet or two.</summary>
        public float delay = 0.12f;

        struct Sample
        {
            public float t;
            public Vector3 p;
            public float yaw;
        }

        const int Size = 12;
        readonly Sample[] samples = new Sample[Size];
        int count, newest = -1;
        float clock;
        FigureState latest;
        bool hasState;

        Combatant me;
        BoxCollider bodyBox;
        Transform headBox;
        Collider[] colliders;
        float hitboxCrouch = -1f;
        bool hiddenNow;

        public float LastHeard { get; private set; }
        /// <summary>The newest state (the host passes other players' states on in its snapshots).</summary>
        public FigureState Latest { get { return latest; } }
        public bool HasState { get { return hasState; } }

        void Awake()
        {
            me = GetComponent<Combatant>();
            bodyBox = GetComponent<BoxCollider>();
            headBox = transform.Find("Head Hitbox");
            colliders = GetComponentsInChildren<Collider>();
        }

        void OnEnable()
        {
            me.Health.Died += OnDied;
            me.Health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            me.Health.Died -= OnDied;
            me.Health.Damaged -= OnDamaged;
        }

        void Start()
        {
            // Aboard the helicopter until told otherwise.
            SetHidden(true);
            me.Landed = false;
        }

        /// <summary>A new state from the owner, stamped with the owner's clock.</summary>
        public void Push(float time, FigureState s)
        {
            LastHeard = Time.unscaledTime;
            if (newest >= 0 && time <= samples[newest].t) return;   // older than what we have
            newest = (newest + 1) % Size;
            samples[newest] = new Sample { t = time, p = s.position, yaw = s.yaw };
            if (count < Size) count++;
            if (count == 1 || Mathf.Abs(time - clock) > 0.6f) clock = time;
            else clock = Mathf.Lerp(clock, time, 0.05f);
            latest = s;
            hasState = true;
        }

        void Update()
        {
            if (!hasState) return;
            clock += Time.deltaTime;
            var s = latest;
            me.Landed = s.landed;
            if (me.Alive)
            {
                // Heals and the haze show up here; hits come as results.
                if (s.health > 0f && Mathf.Abs(me.Health.current - s.health) >= 1f) me.Health.current = Mathf.Min(me.Health.max, s.health);
                SetHidden(s.hidden);
            }
            Place();
            if (body == null || !me.Alive) return;
            if (s.pose != HumanBody.Pose.Dead) body.pose = s.pose;
            body.speed = s.speed;
            body.armed = s.armed;
            body.aiming = s.aiming;
            body.aimPitch = s.pitch;
            body.crouch = s.crouch;
            body.ShowGun(s.gun);
            body.ShowGear(s.vest, s.helmet, s.pack);
            Stance(s.crouch);
        }

        /// <summary>Draws the puppet where it was `delay` seconds ago, between the two samples around then.</summary>
        void Place()
        {
            if (count == 0) return;
            float t = clock - delay;
            int a = -1, b = -1;
            for (int k = 0; k < count; k++)
            {
                int i = (newest - k + Size) % Size;
                if (samples[i].t <= t) { a = i; break; }
                b = i;
            }
            Vector3 p;
            float yaw;
            if (a < 0)
            {
                // Everything is newer than the draw time: use the oldest.
                int oldest = (newest - (count - 1) + Size) % Size;
                p = samples[oldest].p;
                yaw = samples[oldest].yaw;
            }
            else if (b < 0)
            {
                // Nothing newer yet: hold the newest (a lost packet); a short glide forward hides a gap.
                p = samples[a].p;
                yaw = samples[a].yaw;
                int prev = (a - 1 + Size) % Size;
                if (count > 1 && samples[a].t - samples[prev].t > 0.001f)
                {
                    float over = Mathf.Min(t - samples[a].t, 0.15f);
                    Vector3 v = (samples[a].p - samples[prev].p) / (samples[a].t - samples[prev].t);
                    if (v.sqrMagnitude < 80f * 80f) p += v * over;
                }
            }
            else
            {
                float span = samples[b].t - samples[a].t;
                float k = span > 0.0001f ? Mathf.Clamp01((t - samples[a].t) / span) : 1f;
                // A jump of more than 25 m (out of the helicopter door) is a cut, not a slide.
                if ((samples[b].p - samples[a].p).sqrMagnitude > 25f * 25f) k = k < 0.5f ? 0f : 1f;
                p = Vector3.Lerp(samples[a].p, samples[b].p, k);
                yaw = Mathf.LerpAngle(samples[a].yaw, samples[b].yaw, k);
            }
            transform.position = p;
            if (me.Alive) transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void SetHidden(bool hidden)
        {
            if (hidden == hiddenNow && body != null && body.gameObject.activeSelf == !hidden) return;
            hiddenNow = hidden;
            if (body != null) body.gameObject.SetActive(!hidden);
            foreach (var c in colliders) if (c != null) c.enabled = !hidden;
        }

        /// <summary>Crouching moves the eye and the hit areas down with the body (as BotBrain does).</summary>
        void Stance(float crouch)
        {
            if (Mathf.Abs(crouch - hitboxCrouch) < 0.04f) return;
            hitboxCrouch = crouch;
            float drop = crouch * 0.5f;
            if (me.eye != null) me.eye.localPosition = new Vector3(0f, 1.62f - drop, 0f);
            if (headBox != null) headBox.localPosition = new Vector3(0f, 1.72f - drop, 0.04f * crouch);
            if (bodyBox != null)
            {
                bodyBox.center = new Vector3(0f, 0.8f - drop * 0.5f, 0f);
                bodyBox.size = new Vector3(0.5f, 1.45f - drop, 0.32f + crouch * 0.12f);
            }
        }

        void OnDamaged(float amount, Vector3 from)
        {
            if (body != null) body.Flinch(from);
        }

        void OnDied()
        {
            if (body != null)
            {
                body.gameObject.SetActive(true);
                body.Die(me.LastHitFrom);
            }
            foreach (var c in colliders) if (c != null) c.enabled = false;
            SynthAudio.Play3D(GetVoice(), "death_grunt", 0.8f);
        }

        AudioSource voice;

        AudioSource GetVoice()
        {
            if (voice != null) return voice;
            voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 4f;
            voice.maxDistance = 60f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.playOnAwake = false;
            return voice;
        }

        /// <summary>The shooter's arms kick when a replayed shot goes off.</summary>
        public void Kick()
        {
            if (body != null) body.Kick();
        }
    }
}
