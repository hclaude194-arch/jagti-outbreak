using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Audio
{
    /// <summary>
    /// Plays the game's sounds. One per scene (AudioBootstrap makes it). It keeps pools of 3D and 2D
    /// voices, routes each sound to its mixer group, and for every positional sound works out:
    ///  - loudness against distance (the event's rolloff curve; see SoundBank.Rolloff),
    ///  - the distant version taking over far away (gunshots and blasts: the crack and the echo),
    ///  - arrival late with distance (sound travels 343 m a second, so a far shot is heard after it's seen),
    ///  - air soaking up the highs over hundreds of metres,
    ///  - walls in the way (up to two, each quieter and duller), checked again while the sound plays,
    ///  - a global muffle (in the haze, deafened by a blast).
    /// The listener's surroundings drive one reverb zone that follows it: a light open-air reverb
    /// outdoors, a room, a corridor or a hall indoors, judged from rays round the listener.
    /// When the voices run out, the least important and oldest sound is cut for a more important one.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SoundSystem : MonoBehaviour
    {
        public static SoundSystem Instance { get; private set; }

        public int voices3D = 44;
        public int voices2D = 14;
        public const float SpeedOfSound = 343f;

        class Voice
        {
            public AudioSource src;
            public AudioLowPassFilter lp;
            public SoundBank.EventDef def;
            public float baseVolume;
            public float started, ends;
            public bool spatial;
            public Vector3 at;
            public Transform follow;
            public float occGain = 1f, occCut = 22000f, occGainTarget = 1f, occCutTarget = 22000f;
            public float airCut = 22000f;
            public float nextCheck;
        }

        struct Pending
        {
            public string name;
            public Vector3 at;
            public float volume, pitch, time;
            public bool flat;
            public int tag;
        }

        readonly List<Voice> pool3D = new List<Voice>();
        readonly List<Voice> pool2D = new List<Voice>();
        readonly List<Pending> pending = new List<Pending>();
        readonly List<SoundLoop> loops = new List<SoundLoop>();
        static readonly RaycastHit[] rayHits = new RaycastHit[12];

        AudioListener listener;
        AudioReverbZone reverb;
        float envTimer;
        float roomSize = 40f;          // mean distance to walls round the listener
        float roomTarget = 40f;
        int roomWalls;
        bool covered;

        /// <summary>Where the ears are this frame.</summary>
        public static Vector3 ListenerPosition { get; private set; }
        public static bool ListenerIndoors { get; private set; }
        /// <summary>0 clear .. 1 thick: everything dulled (the haze, a blast nearby).</summary>
        public static float Muffle;
        static float deafUntil;
        /// <summary>How much shooting is going on near the listener, 0..1, decaying over a few seconds (for the music).</summary>
        public static float Heat { get; private set; }

        public static void AddHeat(float amount)
        {
            Heat = Mathf.Clamp01(Heat + amount);
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            AcousticSpace.Reset();
            var zoneGo = new GameObject("Listener Reverb");
            zoneGo.transform.SetParent(transform, false);
            reverb = zoneGo.AddComponent<AudioReverbZone>();
            reverb.minDistance = 5000f;
            reverb.maxDistance = 6000f;
            reverb.reverbPreset = AudioReverbPreset.User;
            SetReverb(OpenAir, 1f);
        }

        void Start()
        {
            AudioBuses.Apply();   // the mixer takes SetFloat only once it is running
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ playing

        /// <summary>A positional sound at a point (with its distant version, delay, walls and air).</summary>
        public static AudioSource Play(string name, Vector3 at, float volume = 1f, float pitch = 1f)
        {
            var s = Instance;
            var def = SoundBank.Find(name);
            if (s == null || def == null) return null;
            if (!def.spatial) return s.Start2D(def, volume, pitch);
            float d = Vector3.Distance(at, ListenerPosition);
            if (d > def.maxDistance && string.IsNullOrEmpty(def.far)) return null;
            if (def.delay && d > 30f)
            {
                s.pending.Add(new Pending { name = name, at = at, volume = volume, pitch = pitch, time = Time.time + d / SpeedOfSound });
                return null;
            }
            return s.PlayNow(def, at, volume, pitch, d);
        }

        AudioSource PlayNow(SoundBank.EventDef def, Vector3 at, float volume, float pitch, float d)
        {
            float near = 1f;
            if (!string.IsNullOrEmpty(def.far) && def.farFull > def.farFrom)
            {
                near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(def.farFrom, def.farFull, d));
                var farDef = SoundBank.Find(def.far);
                if (farDef != null && near < 0.99f && d < farDef.maxDistance) Start3D(farDef, at, volume * (1f - near), pitch, null, d);
            }
            if (near > 0.01f && d < def.maxDistance) return Start3D(def, at, volume * near, pitch, null, d);
            return null;
        }

        /// <summary>
        /// Plays a sound after `seconds` (a reload's steps, a casing landing, the pump after a shot).
        /// at null plays it in the head. A non-zero tag lets CancelLater drop the ones still waiting.
        /// </summary>
        public static void PlayLater(string name, float seconds, Vector3? at, float volume = 1f, int tag = 0)
        {
            var s = Instance;
            if (s == null || !SoundBank.Has(name)) return;
            s.pending.Add(new Pending { name = name, at = at ?? Vector3.zero, flat = !at.HasValue, volume = volume, pitch = 1f,
                                        time = Time.time + Mathf.Max(0f, seconds), tag = tag });
        }

        /// <summary>Drops the waiting sounds with this tag (a reload cut short).</summary>
        public static void CancelLater(int tag)
        {
            var s = Instance;
            if (s == null || tag == 0) return;
            s.pending.RemoveAll(p => p.tag == tag);
        }

        /// <summary>A positional sound that moves with a transform (a fighter's footsteps, a vehicle's crash).</summary>
        public static AudioSource PlayOn(string name, Transform follow, float volume = 1f, float pitch = 1f)
        {
            var s = Instance;
            var def = SoundBank.Find(name);
            if (s == null || def == null || follow == null) return null;
            float d = Vector3.Distance(follow.position, ListenerPosition);
            if (d > def.maxDistance) return null;
            return s.Start3D(def, follow.position, volume, pitch, follow, d);
        }

        /// <summary>A sound in the listener's head (own gun, HUD, menus).</summary>
        public static AudioSource Play2D(string name, float volume = 1f, float pitch = 1f)
        {
            var s = Instance;
            var def = SoundBank.Find(name);
            if (s == null || def == null) return null;
            return s.Start2D(def, volume, pitch);
        }

        AudioSource Start3D(SoundBank.EventDef def, Vector3 at, float volume, float pitch, Transform follow, float d)
        {
            var clip = SoundBank.Clip(def);
            if (clip == null) return null;
            var v = Take(pool3D, voices3D, def.priority, true);
            if (v == null) return null;
            var src = v.src;
            v.def = def;
            v.spatial = true;
            v.at = at;
            v.follow = follow;
            src.transform.position = at;
            src.clip = clip;
            src.loop = false;
            src.rolloffMode = AudioRolloffMode.Custom;
            src.maxDistance = Mathf.Max(def.maxDistance, def.minDistance + 0.1f);
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, SoundBank.Rolloff(def));
            src.spread = def.stereo ? 90f : Mathf.Lerp(60f, 0f, Mathf.InverseLerp(def.minDistance * 0.5f, def.minDistance * 2f, d));
            src.reverbZoneMix = def.reverb;
            src.pitch = pitch * Random.Range(def.pitchMin, def.pitchMax);
            src.priority = Mathf.Clamp(def.priority, 0, 255);
            AudioBuses.Route(src, def.busId);
            v.baseVolume = Mathf.Clamp01(def.vol * volume) * AudioBuses.Gain(def.busId);
            // Air: the highs fade over hundreds of metres (the far takes are already dark).
            v.airCut = string.IsNullOrEmpty(def.far) && def.maxDistance > 200f ? 22000f : Mathf.Lerp(22000f, 3500f, Mathf.InverseLerp(40f, 500f, d));
            Occlusion(v, true);
            v.occGain = v.occGainTarget;
            v.occCut = v.occCutTarget;
            Apply(v);
            v.started = Time.time;
            v.ends = Time.time + clip.length / Mathf.Max(0.1f, Mathf.Abs(src.pitch)) + 0.05f;
            src.Play();
            return src;
        }

        AudioSource Start2D(SoundBank.EventDef def, float volume, float pitch)
        {
            var clip = SoundBank.Clip(def);
            if (clip == null) return null;
            var v = Take(pool2D, voices2D, def.priority, false);
            if (v == null) return null;
            var src = v.src;
            v.def = def;
            v.spatial = false;
            v.follow = null;
            src.clip = clip;
            src.loop = false;
            src.reverbZoneMix = def.reverb;
            src.pitch = pitch * Random.Range(def.pitchMin, def.pitchMax);
            src.priority = Mathf.Clamp(def.priority, 0, 255);
            AudioBuses.Route(src, def.busId);
            v.baseVolume = Mathf.Clamp01(def.vol * volume) * AudioBuses.Gain(def.busId);
            v.occGain = v.occGainTarget = 1f;
            v.occCut = v.occCutTarget = 22000f;
            v.airCut = 22000f;
            Apply(v);
            v.started = Time.time;
            v.ends = Time.time + clip.length / Mathf.Max(0.1f, Mathf.Abs(src.pitch)) + 0.05f;
            src.Play();
            return src;
        }

        Voice Take(List<Voice> pool, int max, int priority, bool spatial)
        {
            for (int i = 0; i < pool.Count; i++)
                if (!pool[i].src.isPlaying || Time.time > pool[i].ends) return pool[i];
            if (pool.Count < max)
            {
                var go = new GameObject(spatial ? "Voice 3D" : "Voice 2D");
                go.transform.SetParent(transform, false);
                var v = new Voice { src = go.AddComponent<AudioSource>() };
                v.src.playOnAwake = false;
                v.src.spatialBlend = spatial ? 1f : 0f;
                v.src.dopplerLevel = 0f;
                v.lp = go.AddComponent<AudioLowPassFilter>();
                v.lp.cutoffFrequency = 22000f;
                v.lp.lowpassResonanceQ = 1f;
                pool.Add(v);
                return v;
            }
            // Steal: the least important, then the one closest to finishing.
            Voice worst = null;
            for (int i = 0; i < pool.Count; i++)
            {
                var v = pool[i];
                if (v.def == null) return v;
                if (worst == null || v.def.priority > worst.def.priority ||
                    (v.def.priority == worst.def.priority && v.ends < worst.ends)) worst = v;
            }
            if (worst != null && worst.def.priority >= priority)
            {
                worst.src.Stop();
                return worst;
            }
            return null;
        }

        void Apply(Voice v)
        {
            float muffle = Mathf.Clamp01(Mathf.Max(Muffle, Time.time < deafUntil ? 0.85f : 0f));
            bool exempt = v.def != null && (v.def.busId == Bus.Interface || v.def.busId == Bus.Music || v.def.busId == Bus.Voice);
            float muffleCut = exempt ? 22000f : Mathf.Lerp(22000f, 600f, muffle);
            v.lp.cutoffFrequency = Mathf.Max(200f, Mathf.Min(v.occCut, Mathf.Min(v.airCut, muffleCut)));
            float duck = exempt ? 1f : Mathf.Lerp(1f, 0.55f, muffle);
            v.src.volume = v.baseVolume * v.occGain * duck;
        }

        /// <summary>Walls between the sound and the listener: each solid one costs volume and highs; glass costs less.</summary>
        void Occlusion(Voice v, bool now)
        {
            v.occGainTarget = 1f;
            v.occCutTarget = 22000f;
            if (!v.spatial) return;
            Vector3 from = ListenerPosition;
            Vector3 to = (v.follow != null ? v.follow.position : v.at) + Vector3.up * 0.4f;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1.5f || len > 300f) return;
            int n = Physics.RaycastNonAlloc(from, d / len, rayHits, len - 0.6f, ~0, QueryTriggerInteraction.Ignore);
            float walls = 0f;
            for (int i = 0; i < n && walls < 2.5f; i++)
            {
                var c = rayHits[i].collider;
                if (c == null || c is CharacterController) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                if (v.follow != null && c.transform.IsChildOf(v.follow.root)) continue;
                if (c is TerrainCollider) walls += 1.2f;
                else if (Jagti.World.GlassPanes.IsGlass(c)) walls += 0.35f;
                else walls += 1f;
            }
            if (walls <= 0f) return;
            walls = Mathf.Min(walls, 2.5f);
            v.occGainTarget = Mathf.Pow(0.5f, walls);
            v.occCutTarget = 22000f * Mathf.Pow(0.18f, walls);
        }

        // ------------------------------------------------------------------ composites

        /// <summary>
        /// A gunshot: the gun itself (near), the place's tail (open ground or a room), and far away the
        /// distant crack and echo instead. `group` is the calibre group of the tails (light, shot, rifle,
        /// heavy). The player's own shots play in the head.
        /// </summary>
        public static void Gunshot(string coreEvent, string group, bool suppressed, Vector3 at, bool own, float volume = 1f)
        {
            if (Instance == null) return;
            bool shooterIndoors = AcousticSpace.Indoors(at);
            string tail = (shooterIndoors ? "tail_in_" : "tail_out_") + group;
            float d = Vector3.Distance(at, ListenerPosition);
            if (own)
            {
                Play2D(coreEvent, volume);
                Play2D(tail, volume * (suppressed ? 0.35f : 1f));
                AddHeat(0.12f);
                return;
            }
            Play(coreEvent, at, volume);
            if (!suppressed) Play(tail, at, volume * 0.9f);
            else if (d < 25f) Play(tail, at, volume * 0.3f);
            if (d < 120f) AddHeat(suppressed ? 0.06f : Mathf.Lerp(0.25f, 0.08f, d / 120f));
        }

        /// <summary>A bullet hitting the world: the material's impact, now and then a ricochet whine.</summary>
        public static void Impact(Vector3 point, Vector3 dir)
        {
            if (Instance == null) return;
            if ((point - ListenerPosition).sqrMagnitude > 90f * 90f) return;
            RaycastHit h;
            if (dir.sqrMagnitude < 1e-6f || !Physics.Raycast(point - dir.normalized * 0.25f, dir.normalized, out h, 0.6f, ~0, QueryTriggerInteraction.Ignore)) return;
            var s = AcousticSpace.Of(h);
            string evt;
            switch (s)
            {
                case AcousticSpace.Surface.Grass:
                case AcousticSpace.Surface.Dirt:
                case AcousticSpace.Surface.Gravel: evt = "impact_dirt"; break;
                case AcousticSpace.Surface.Metal: evt = "impact_metal"; break;
                case AcousticSpace.Surface.Wood: evt = "impact_wood"; break;
                case AcousticSpace.Surface.Water: evt = "impact_water"; break;
                case AcousticSpace.Surface.Glass: evt = "impact_glass"; break;
                default: evt = "impact_concrete"; break;
            }
            Play(evt, h.point);
            if ((s == AcousticSpace.Surface.Concrete || s == AcousticSpace.Surface.Metal || s == AcousticSpace.Surface.Tile) && Random.value < 0.12f)
                Play("ricochet", h.point, 0.8f);
        }

        /// <summary>A blast near the ears: everything goes dull and a ring sets in for a moment.</summary>
        public static void Deafen(Vector3 blast, float radius)
        {
            float d = Vector3.Distance(blast, ListenerPosition);
            if (d > radius) return;
            float k = 1f - d / radius;
            deafUntil = Mathf.Max(deafUntil, Time.time + 0.6f + 2.2f * k);
            Play2D("tinnitus", Mathf.Clamp01(0.3f + k));
        }

        // ------------------------------------------------------------------ loops

        public static void Register(SoundLoop loop)
        {
            if (Instance != null && !Instance.loops.Contains(loop)) Instance.loops.Add(loop);
        }

        public static void Unregister(SoundLoop loop)
        {
            if (Instance != null) Instance.loops.Remove(loop);
        }

        // ------------------------------------------------------------------ every frame

        void Update()
        {
            if (listener == null || !listener.isActiveAndEnabled)
            {
                var cam = Camera.main;
                listener = cam != null ? cam.GetComponent<AudioListener>() : null;
                if (listener == null) listener = FindObjectOfType<AudioListener>();
            }
            if (listener != null) ListenerPosition = listener.transform.position;
            reverb.transform.position = ListenerPosition;
            Heat = Mathf.MoveTowards(Heat, 0f, Time.deltaTime / 9f);

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < pending[i].time) continue;
                var p = pending[i];
                pending.RemoveAt(i);
                var def = SoundBank.Find(p.name);
                if (def == null) continue;
                if (p.flat || !def.spatial) Start2D(def, p.volume, p.pitch);
                else PlayNow(def, p.at, p.volume, p.pitch, Vector3.Distance(p.at, ListenerPosition));
            }

            float dt = Time.deltaTime;
            for (int i = 0; i < pool3D.Count; i++)
            {
                var v = pool3D[i];
                if (!v.src.isPlaying) continue;
                if (v.follow != null) v.src.transform.position = v.follow.position;
                if (Time.time >= v.nextCheck)
                {
                    v.nextCheck = Time.time + 0.2f + Random.value * 0.1f;
                    Occlusion(v, false);
                }
                v.occGain = Mathf.MoveTowards(v.occGain, v.occGainTarget, dt * 3f);
                v.occCut = Mathf.Lerp(v.occCut, v.occCutTarget, 1f - Mathf.Exp(-dt * 8f));
                Apply(v);
            }
            for (int i = 0; i < pool2D.Count; i++)
                if (pool2D[i].src.isPlaying) Apply(pool2D[i]);

            float muffle = Mathf.Clamp01(Mathf.Max(Muffle, Time.time < deafUntil ? 0.85f : 0f));
            for (int i = loops.Count - 1; i >= 0; i--)
            {
                var l = loops[i];
                if (l == null || l.Source == null) { loops.RemoveAt(i); continue; }
                l.Tick(dt, muffle);
            }

            envTimer -= dt;
            if (envTimer <= 0f)
            {
                envTimer = 0.25f;
                Listen();
            }
            roomSize = Mathf.Lerp(roomSize, roomTarget, 1f - Mathf.Exp(-dt * 3f));
            UpdateReverb(dt);
        }

        /// <summary>Indoors or out, and how big the space is, from rays round the ears.</summary>
        void Listen()
        {
            Vector3 p = ListenerPosition;
            covered = AcousticSpace.Covered(p);
            ListenerIndoors = covered && AcousticSpace.ZoneAt(p) != null;
            float open = AcousticSpace.Openness(p, 30f, out roomWalls);
            roomTarget = covered ? open : 40f;
        }

        // ------------------------------------------------------------------ reverb

        struct Room
        {
            public float room, roomHF, decay, decayHF, reflections, reflDelay, reverbLevel, reverbDelay, diffusion, density;
            public Room(float room, float roomHF, float decay, float decayHF, float reflections, float reflDelay, float reverbLevel, float reverbDelay, float diffusion, float density)
            {
                this.room = room; this.roomHF = roomHF; this.decay = decay; this.decayHF = decayHF; this.reflections = reflections;
                this.reflDelay = reflDelay; this.reverbLevel = reverbLevel; this.reverbDelay = reverbDelay; this.diffusion = diffusion; this.density = density;
            }
            public static Room Lerp(Room a, Room b, float t)
            {
                return new Room(Mathf.Lerp(a.room, b.room, t), Mathf.Lerp(a.roomHF, b.roomHF, t), Mathf.Lerp(a.decay, b.decay, t), Mathf.Lerp(a.decayHF, b.decayHF, t),
                                Mathf.Lerp(a.reflections, b.reflections, t), Mathf.Lerp(a.reflDelay, b.reflDelay, t), Mathf.Lerp(a.reverbLevel, b.reverbLevel, t),
                                Mathf.Lerp(a.reverbDelay, b.reverbDelay, t), Mathf.Lerp(a.diffusion, b.diffusion, t), Mathf.Lerp(a.density, b.density, t));
            }
        }

        // Levels in millibels, as Unity's reverb zone takes them.
        static readonly Room OpenAir = new Room(-1600f, -1800f, 1.6f, 0.4f, -2200f, 0.03f, -2400f, 0.06f, 40f, 30f);
        static readonly Room Street = new Room(-900f, -1200f, 1.4f, 0.5f, -900f, 0.015f, -1300f, 0.03f, 60f, 60f);
        static readonly Room SmallRoom = new Room(-500f, -500f, 0.55f, 0.75f, -350f, 0.003f, -400f, 0.006f, 100f, 100f);
        static readonly Room Corridor = new Room(-450f, -450f, 1.1f, 0.6f, -200f, 0.005f, -350f, 0.012f, 70f, 90f);
        static readonly Room Hall = new Room(-350f, -500f, 2.2f, 0.65f, -500f, 0.02f, -150f, 0.03f, 100f, 100f);
        Room current = OpenAir;

        void UpdateReverb(float dt)
        {
            Room target;
            if (!covered)
                target = Room.Lerp(OpenAir, Street, Mathf.Clamp01(roomWalls / 5f));    // walls round about give slapback
            else if (roomSize < 4.5f)
                target = roomWalls >= 6 ? SmallRoom : Corridor;
            else if (roomSize < 12f)
                target = Room.Lerp(SmallRoom, Hall, (roomSize - 4.5f) / 7.5f);
            else
                target = Hall;
            current = Room.Lerp(current, target, 1f - Mathf.Exp(-dt * 4f));
            SetReverb(current, 1f);
        }

        void SetReverb(Room r, float mix)
        {
            if (reverb == null) return;
            reverb.room = Mathf.RoundToInt(r.room);
            reverb.roomHF = Mathf.RoundToInt(r.roomHF);
            reverb.roomLF = 0;
            reverb.decayTime = r.decay;
            reverb.decayHFRatio = r.decayHF;
            reverb.reflections = Mathf.RoundToInt(r.reflections);
            reverb.reflectionsDelay = r.reflDelay;
            reverb.reverb = Mathf.RoundToInt(r.reverbLevel);
            reverb.reverbDelay = r.reverbDelay;
            reverb.diffusion = r.diffusion;
            reverb.density = r.density;
        }
    }
}
