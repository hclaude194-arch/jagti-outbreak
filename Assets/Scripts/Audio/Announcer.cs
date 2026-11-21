using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Audio
{
    /// <summary>
    /// The match announcer: short voice lines (Assets/Resources/Audio/voice/&lt;key&gt;.ogg) for the
    /// big moments: the bus taking off, the haze closing and moving, the final circle, supply drops,
    /// how many are left, a win and a knock-out. Lines queue rather than talk over each other, the
    /// same line won't repeat within half a minute, and the music ducks while one plays. A missing
    /// line is simply skipped.
    /// </summary>
    public class Announcer : MonoBehaviour
    {
        public static Announcer Instance { get; private set; }
        public static bool Speaking { get { return Instance != null && Instance.src != null && Instance.src.isPlaying; } }

        AudioSource src;
        readonly Queue<string> queue = new Queue<string>();
        readonly Dictionary<string, float> lastSaid = new Dictionary<string, float>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        float gap;

        void Awake()
        {
            Instance = this;
            src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.priority = 0;
            src.bypassReverbZones = true;
            AudioBuses.Route(src, Bus.Voice);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Queues a line by key (see tools/audio/voice_lines.json).</summary>
        public static void Say(string key, bool urgent = false)
        {
            var a = Instance;
            if (a == null || string.IsNullOrEmpty(key)) return;
            float t;
            if (a.lastSaid.TryGetValue(key, out t) && Time.unscaledTime - t < 30f) return;
            if (a.Clip(key) == null) return;
            a.lastSaid[key] = Time.unscaledTime;
            if (urgent)
            {
                a.queue.Clear();
                a.src.Stop();
            }
            if (a.queue.Count < 3) a.queue.Enqueue(key);
        }

        AudioClip Clip(string key)
        {
            AudioClip c;
            if (!clips.TryGetValue(key, out c))
            {
                c = Resources.Load<AudioClip>("Audio/voice/" + key);
                clips[key] = c;
            }
            return c;
        }

        void Update()
        {
            if (src.isPlaying) { gap = 0.35f; return; }
            gap -= Time.unscaledDeltaTime;
            if (gap > 0f || queue.Count == 0) return;
            var clip = Clip(queue.Dequeue());
            if (clip == null) return;
            src.clip = clip;
            src.volume = AudioBuses.Gain(Bus.Voice);
            src.Play();
        }
    }
}
