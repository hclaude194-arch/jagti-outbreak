using UnityEngine;

namespace Jagti.Audio
{
    /// <summary>
    /// A looping sound on an object (an engine, a rotor, the haze wall, a fan): routed to its mixer
    /// group, falling off with distance by the event's curve, dulled by walls and the global muffle like
    /// the one-shots. The owner sets Volume and Pitch each frame; the loop eases towards them, starts
    /// itself when it becomes audible and stops when silent, so idle loops cost nothing.
    /// </summary>
    public class SoundLoop
    {
        public AudioSource Source { get; private set; }
        public SoundBank.EventDef Def { get; private set; }
        public float Volume;          // 0..1 (times the event's own gain)
        public float Pitch = 1f;
        public float VolumeEase = 6f; // per second
        public float PitchEase = 8f;
        /// <summary>An extra low-pass the owner can set (Hz), e.g. the outdoor beds heard from indoors.</summary>
        public float Cutoff = 22000f;
        public bool Spatial { get; private set; }

        AudioLowPassFilter lp;
        float vol, occGain = 1f, occCut = 22000f, occGainTarget = 1f, occCutTarget = 22000f, nextCheck;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        /// <summary>Makes a loop on host for the bank event; null if the bank has no such sound.</summary>
        public static SoundLoop Create(GameObject host, string name, bool spatial = true)
        {
            var def = SoundBank.Find(name);
            if (def == null || host == null) return null;
            var clip = SoundBank.First(def);
            if (clip == null) return null;
            var go = host;
            // One loop per child object, so several loops (and their filters) can sit on one thing.
            var child = new GameObject("Loop " + name);
            child.transform.SetParent(go.transform, false);
            var l = new SoundLoop { Def = def, Spatial = spatial && def.spatial };
            var src = child.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
            src.dopplerLevel = 0f;
            src.spatialBlend = l.Spatial ? 1f : 0f;
            if (l.Spatial)
            {
                src.rolloffMode = AudioRolloffMode.Custom;
                src.maxDistance = Mathf.Max(def.maxDistance, def.minDistance + 0.1f);
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, SoundBank.Rolloff(def));
                src.spread = def.stereo ? 120f : 30f;
            }
            src.reverbZoneMix = def.reverb;
            src.priority = Mathf.Clamp(def.priority, 0, 255);
            AudioBuses.Route(src, def.busId);
            l.lp = child.AddComponent<AudioLowPassFilter>();
            l.lp.cutoffFrequency = 22000f;
            l.Source = src;
            SoundSystem.Register(l);
            return l;
        }

        /// <summary>Called by SoundSystem every frame.</summary>
        public void Tick(float dt, float muffle)
        {
            var src = Source;
            vol = Mathf.MoveTowards(vol, Mathf.Clamp01(Volume), dt * VolumeEase);
            src.pitch = Mathf.Lerp(src.pitch, Pitch, 1f - Mathf.Exp(-dt * PitchEase));
            bool audible = vol > 0.001f;
            if (audible && Spatial)
            {
                float d = Vector3.Distance(src.transform.position, SoundSystem.ListenerPosition);
                audible = d < Def.maxDistance;
                if (audible && Time.time >= nextCheck)
                {
                    nextCheck = Time.time + 0.25f + Random.value * 0.1f;
                    Occlude(src.transform.position, d);
                }
            }
            occGain = Mathf.MoveTowards(occGain, occGainTarget, dt * 3f);
            occCut = Mathf.Lerp(occCut, occCutTarget, 1f - Mathf.Exp(-dt * 6f));
            bool exempt = Def.busId == Bus.Interface || Def.busId == Bus.Music || Def.busId == Bus.Voice;
            float m = exempt ? 0f : muffle;
            lp.cutoffFrequency = Mathf.Max(200f, Mathf.Min(Mathf.Min(occCut, Cutoff), Mathf.Lerp(22000f, 600f, m)));
            src.volume = vol * Def.vol * occGain * Mathf.Lerp(1f, 0.55f, m) * AudioBuses.Gain(Def.busId);
            if (audible && !src.isPlaying)
            {
                src.time = Random.Range(0f, src.clip.length * 0.9f);   // two of the same loop don't phase
                src.Play();
            }
            else if (!audible && src.isPlaying) src.Stop();
        }

        void Occlude(Vector3 at, float d)
        {
            occGainTarget = 1f;
            occCutTarget = 22000f;
            if (d < 1.5f || d > 250f) return;
            Vector3 from = SoundSystem.ListenerPosition;
            Vector3 dir = (at + Vector3.up * 0.3f - from) / d;
            int n = Physics.RaycastNonAlloc(from, dir, hits, d - 1f, ~0, QueryTriggerInteraction.Ignore);
            float walls = 0f;
            var root = Source.transform.root;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null || c is CharacterController || c.transform.IsChildOf(root)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                walls += Jagti.World.GlassPanes.IsGlass(c) ? 0.35f : 1f;
            }
            walls = Mathf.Min(walls, 2.5f);
            if (walls <= 0f) return;
            occGainTarget = Mathf.Pow(0.5f, walls);
            occCutTarget = 22000f * Mathf.Pow(0.18f, walls);
        }

        /// <summary>Cuts the sound now (wrecked vehicle, scene change).</summary>
        public void Stop()
        {
            Volume = 0f;
            vol = 0f;
            if (Source != null) Source.Stop();
        }

        public void Destroy()
        {
            SoundSystem.Unregister(this);
            if (Source != null) Object.Destroy(Source.gameObject);
        }
    }
}
