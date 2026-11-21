using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Audio
{
    /// <summary>
    /// The table of game sounds, read from Assets/Resources/Audio/sound_bank.json (written by
    /// tools/audio/build_sounds.py, which also renders the clips). Each event has one or more takes;
    /// a take is picked at random, never the same one twice running. Clips load on first use.
    /// </summary>
    public static class SoundBank
    {
        [Serializable]
        public class EventDef
        {
            public string name;
            public string bus;
            public int variants;
            public float level;
            public float vol = 1f;
            public float pitchMin = 1f, pitchMax = 1f;
            public float minDistance = 2f, maxDistance = 60f;
            public float curve = 1f;
            public string far;
            public float farFrom, farFull;
            public bool delay;
            public bool stereo;
            public bool loop;
            public int priority = 128;
            public float reverb = 1f;
            public bool spatial = true;
            public string[] files;

            [NonSerialized] public AudioClip[] clips;
            [NonSerialized] public int last = -1;
            [NonSerialized] public Bus busId;
            [NonSerialized] public AnimationCurve rolloff;
        }

        [Serializable]
        class Table
        {
            public EventDef[] events;
        }

        static Dictionary<string, EventDef> events;
        static readonly HashSet<string> warned = new HashSet<string>();

        public static bool Loaded { get { Load(); return events.Count > 0; } }

        static void Load()
        {
            if (events != null) return;
            events = new Dictionary<string, EventDef>();
            var text = Resources.Load<TextAsset>("Audio/sound_bank");
            if (text == null)
            {
                Debug.LogWarning("[Jagti] Audio: Resources/Audio/sound_bank.json is missing; using the old synthesised sounds.");
                return;
            }
            var table = JsonUtility.FromJson<Table>(text.text);
            if (table == null || table.events == null) return;
            foreach (var e in table.events)
            {
                if (string.IsNullOrEmpty(e.name)) continue;
                e.busId = AudioBuses.Parse(e.bus);
                events[e.name] = e;
            }
        }

        /// <summary>The event, or null if the bank has no such sound.</summary>
        public static EventDef Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Load();
            EventDef e;
            return events.TryGetValue(name, out e) ? e : null;
        }

        public static bool Has(string name)
        {
            return Find(name) != null;
        }

        /// <summary>A take of the event (random, not the last one played), or null.</summary>
        public static AudioClip Clip(EventDef e)
        {
            if (e == null || e.files == null || e.files.Length == 0) return null;
            if (e.clips == null) e.clips = new AudioClip[e.files.Length];
            int n = e.files.Length;
            int i = n == 1 ? 0 : UnityEngine.Random.Range(0, n);
            if (n > 1 && i == e.last) i = (i + 1 + UnityEngine.Random.Range(0, n - 1)) % n;
            e.last = i;
            if (e.clips[i] == null)
            {
                e.clips[i] = Resources.Load<AudioClip>(e.files[i]);
                if (e.clips[i] == null && warned.Add(e.files[i])) Debug.LogWarning("[Jagti] Audio: missing clip " + e.files[i]);
            }
            return e.clips[i];
        }

        /// <summary>The first take (loops are rendered as one take).</summary>
        public static AudioClip First(EventDef e)
        {
            if (e == null || e.files == null || e.files.Length == 0) return null;
            if (e.clips == null) e.clips = new AudioClip[e.files.Length];
            if (e.clips[0] == null) e.clips[0] = Resources.Load<AudioClip>(e.files[0]);
            return e.clips[0];
        }

        public static AudioClip Clip(string name)
        {
            return Clip(Find(name));
        }

        /// <summary>
        /// Loudness against distance for this event as a Unity custom rolloff curve (x is distance over
        /// maxDistance): full within minDistance, then (min / d) ^ curve, easing to silence at maxDistance.
        /// </summary>
        public static AnimationCurve Rolloff(EventDef e)
        {
            if (e.rolloff != null) return e.rolloff;
            const int keys = 24;
            var k = new Keyframe[keys];
            float max = Mathf.Max(e.maxDistance, e.minDistance + 0.1f);
            for (int i = 0; i < keys; i++)
            {
                float x = i / (float)(keys - 1);
                x = x * x;                              // more keys close in, where the curve bends
                float d = x * max;
                k[i] = new Keyframe(x, Attenuation(e, d));
            }
            var c = new AnimationCurve(k);
            for (int i = 0; i < keys; i++) c.SmoothTangents(i, 0f);
            e.rolloff = c;
            return c;
        }

        public static float Attenuation(EventDef e, float d)
        {
            float min = Mathf.Max(0.1f, e.minDistance);
            float max = Mathf.Max(e.maxDistance, min + 0.1f);
            if (d <= min) return 1f;
            float g = Mathf.Pow(min / d, Mathf.Max(0.2f, e.curve));
            float edge = Mathf.InverseLerp(max, max * 0.7f, d);   // 1 well inside, 0 at the edge
            return g * Mathf.SmoothStep(0f, 1f, edge);
        }
    }
}
