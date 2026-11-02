using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>
    /// Every game sound is synthesized in code when first needed, so the project needs no audio files yet.
    /// They are placeholders with the right timing and loudness; real recordings can replace them later.
    /// </summary>
    public static class SynthAudio
    {
        const int Rate = 22050;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static AudioSource source2D;

        public static AudioClip Get(string name)
        {
            AudioClip clip;
            if (cache.TryGetValue(name, out clip) && clip != null) return clip;
            clip = Make(name);
            cache[name] = clip;
            return clip;
        }

        /// <summary>Plays a non-positional sound (player's own gun, hurt, pickups).</summary>
        public static void Play2D(string name, float volume = 1f)
        {
            if (source2D == null)
            {
                var go = new GameObject("Audio 2D");
                source2D = go.AddComponent<AudioSource>();
                source2D.spatialBlend = 0f;
                source2D.playOnAwake = false;
            }
            source2D.pitch = 1f;
            source2D.PlayOneShot(Get(name), volume);
        }

        /// <summary>Plays a positional one-shot through an existing source with a little pitch variety.</summary>
        public static void Play3D(AudioSource source, string name, float volume = 1f)
        {
            if (source == null) return;
            source.pitch = Random.Range(0.9f, 1.1f);
            source.PlayOneShot(Get(name), volume);
        }

        static AudioClip Make(string name)
        {
            switch (name)
            {
                case "pistol": return Shot(name, 0.45f, 0.055f, 70f, 1.0f);
                case "shotgun": return Shot(name, 0.8f, 0.11f, 48f, 1.0f);
                case "empty": return Click(name, 0.05f, 2500f);
                case "reload": return Reload(name);
                case "swing": return Swing(name);
                case "hit_flesh": return Thud(name, 0.18f, 90f, 0.7f);
                case "hit_solid": return Thud(name, 0.12f, 300f, 0.35f);
                case "footstep": return Step(name, 1);
                case "footstep2": return Step(name, 2);
                case "pickup": return Chime(name);
                case "hurt": return Hurt(name);
                case "zombie_attack": return Growl(name, 0.6f, 130f, 7);
                case "zombie_alert": return Growl(name, 0.9f, 95f, 11);
                case "zombie_death": return Growl(name, 1.1f, 70f, 13);
                case "groan1": return Groan(name, 1.6f, 78f, 21);
                case "groan2": return Groan(name, 2.0f, 66f, 22);
                case "groan3": return Groan(name, 1.3f, 92f, 23);
                default: return Click(name, 0.02f, 1000f);
            }
        }

        static AudioClip Clip(string name, float[] data)
        {
            // Normalize to a safe peak.
            float peak = 0.0001f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float gain = 0.9f / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float[] Buffer(float seconds)
        {
            return new float[Mathf.Max(1, Mathf.RoundToInt(seconds * Rate))];
        }

        static float White(System.Random r)
        {
            return (float)(r.NextDouble() * 2.0 - 1.0);
        }

        // Gunshot: a sharp crack of noise, a low body thump, and a long rumbling tail.
        static AudioClip Shot(string name, float seconds, float crackTau, float thumpHz, float tail)
        {
            var d = Buffer(seconds);
            var r = new System.Random(name.Length * 31);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = White(r);
                lp += (n - lp) * 0.35f;
                lp2 += (n - lp2) * 0.04f;
                float crack = n * Mathf.Exp(-t / (crackTau * 0.35f));
                float body = lp * Mathf.Exp(-t / crackTau);
                float thump = Mathf.Sin(2f * Mathf.PI * thumpHz * t * (1f - t)) * Mathf.Exp(-t / 0.07f);
                float rumble = lp2 * 3f * Mathf.Exp(-t / (seconds * 0.35f)) * tail;
                d[i] = crack * 0.6f + body * 1.2f + thump * 0.9f + rumble;
            }
            return Clip(name, d);
        }

        static AudioClip Click(string name, float seconds, float hz)
        {
            var d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-t / (seconds * 0.25f));
            }
            return Clip(name, d);
        }

        static AudioClip Reload(string name)
        {
            var d = Buffer(1.0f);
            var r = new System.Random(5);
            float[] clicks = { 0.05f, 0.12f, 0.62f, 0.7f, 0.9f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                foreach (float c in clicks)
                {
                    float dt = t - c;
                    if (dt >= 0f && dt < 0.06f) v += (White(r) * 0.6f + Mathf.Sin(2f * Mathf.PI * 1800f * dt)) * Mathf.Exp(-dt / 0.01f);
                }
                d[i] = v;
            }
            return Clip(name, d);
        }

        static AudioClip Swing(string name)
        {
            var d = Buffer(0.32f);
            var r = new System.Random(9);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.32f));
                float cutoff = 0.03f + 0.25f * env;
                lp += (White(r) - lp) * cutoff;
                d[i] = lp * env * env;
            }
            return Clip(name, d);
        }

        static AudioClip Thud(string name, float seconds, float hz, float noiseMix)
        {
            var d = Buffer(seconds);
            var r = new System.Random(name.Length * 7);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                lp += (White(r) - lp) * 0.2f;
                float env = Mathf.Exp(-t / (seconds * 0.25f));
                d[i] = (Mathf.Sin(2f * Mathf.PI * hz * t) * (1f - noiseMix) + lp * noiseMix * 2f) * env;
            }
            return Clip(name, d);
        }

        static AudioClip Step(string name, int seed)
        {
            var d = Buffer(0.14f);
            var r = new System.Random(seed * 13);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                lp += (White(r) - lp) * 0.12f;
                d[i] = lp * Mathf.Exp(-t / 0.025f) + Mathf.Sin(2f * Mathf.PI * 80f * t) * 0.3f * Mathf.Exp(-t / 0.02f);
            }
            return Clip(name, d);
        }

        static AudioClip Chime(string name)
        {
            var d = Buffer(0.35f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = t < 0.1f ? 880f : 1320f;
                d[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-(t % 0.1f) / 0.08f) * (t < 0.1f ? 0.7f : 1f) * Mathf.Exp(-t / 0.25f);
            }
            return Clip(name, d);
        }

        static AudioClip Hurt(string name)
        {
            var d = Buffer(0.35f);
            var r = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 220f - 120f * t;
                lp += (White(r) - lp) * 0.1f;
                float env = Mathf.Exp(-t / 0.12f) * Mathf.Clamp01(t / 0.01f);
                d[i] = (Saw(f * t) * 0.6f + lp * 0.8f) * env;
            }
            return Clip(name, d);
        }

        static float Saw(float phase)
        {
            return 2f * (phase - Mathf.Floor(phase + 0.5f));
        }

        // Zombie voice: a buzzy low saw through a wobbling low-pass, with breath noise.
        static AudioClip Groan(string name, float seconds, float hz, int seed)
        {
            var d = Buffer(seconds);
            var r = new System.Random(seed);
            float lp = 0f, breath = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float u = t / seconds;
                float f = hz * (1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.7f * t) + 0.04f * Mathf.Sin(2f * Mathf.PI * 6.5f * t) - 0.2f * u);
                phase += f / Rate;
                float src = Saw(phase) + 0.5f * Saw(phase * 2.01f);
                float cutoff = 0.05f + 0.06f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 1.3f * t + seed));
                lp += (src - lp) * cutoff;
                breath += (White(r) - breath) * 0.08f;
                float env = Mathf.Sin(Mathf.PI * u) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 3f * t));
                d[i] = (lp * 1.4f + breath * 0.35f) * env;
            }
            return Clip(name, d);
        }

        static AudioClip Growl(string name, float seconds, float hz, int seed)
        {
            var d = Buffer(seconds);
            var r = new System.Random(seed);
            float lp = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float u = t / seconds;
                float rough = 1f + 0.5f * White(r) * 0.3f;
                phase += hz * rough * (1.2f - 0.5f * u) / Rate;
                float src = Saw(phase) + White(r) * 0.4f;
                lp += (src - lp) * 0.12f;
                float env = Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-u * 2.2f);
                d[i] = lp * env;
            }
            return Clip(name, d);
        }
    }
}
