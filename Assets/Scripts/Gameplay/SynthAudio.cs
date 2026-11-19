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
                // Battle royale sounds.
                case "smg": return Shot(name, 0.3f, 0.035f, 95f, 0.6f);
                case "rifle": return Shot(name, 0.6f, 0.05f, 75f, 1.1f);
                case "dmr": return Shot(name, 1.1f, 0.07f, 55f, 1.5f);
                case "explosion": return Shot(name, 2.2f, 0.25f, 32f, 2.5f);
                case "whiz": return Whiz(name);
                case "haze_tick": return Thud(name, 0.25f, 60f, 0.5f);
                case "rotor": return Rotor(name);
                case "wind": return Wind(name, 2.0f, 0.06f);
                case "whoosh": return Swing(name);
                case "canopy": return Wind(name, 0.6f, 0.25f);
                case "land": return Thud(name, 0.3f, 70f, 0.6f);
                case "kill_confirm": return Chime(name);
                case "hit_head": return Click(name, 0.12f, 1900f);
                case "equip": return Reload(name);
                case "bandage": return Wind(name, 0.5f, 0.4f);
                case "clink": return Click(name, 0.08f, 2600f);
                case "death_grunt": return Hurt(name);
                case "alert": return Tones(name, new[] { 660f, 880f, 660f, 880f }, 0.18f);
                case "revolver": return Shot(name, 0.75f, 0.06f, 58f, 1.3f);
                case "carbine": return Shot(name, 0.45f, 0.045f, 85f, 0.9f);
                case "double": return Shot(name, 0.95f, 0.12f, 44f, 1.15f);
                case "lmg": return Shot(name, 0.55f, 0.05f, 68f, 1.05f);
                case "sniper": return Shot(name, 1.6f, 0.08f, 42f, 2.1f);
                case "suppressed": return Thud(name, 0.16f, 210f, 0.85f);
                case "bolt": return Reload(name);
                case "armour_break": return Tones(name, new[] { 1500f, 1100f, 700f }, 0.05f);
                case "smoke_pop": return Wind(name, 1.4f, 0.55f);
                case "heartbeat": return Thud(name, 0.5f, 48f, 0.1f);
                case "engine_scooter": return Engine(name, 31f, 0.55f, 41);
                case "engine_cart": return Engine(name, 22f, 0.35f, 42);
                case "crash": return Thud(name, 0.5f, 55f, 0.9f);
                case "splash": return Wind(name, 0.45f, 0.8f);
                case "zip": return Wind(name, 1.5f, 0.7f);
                case "skybus_props": return Engine(name, 24f, 0.25f, 44);
                case "burner": return Wind(name, 1.1f, 0.5f);
                case "stash_open": return StashOpen(name);
                case "engine_auto": return Engine(name, 27f, 0.75f, 45);
                case "engine_jeep": return Engine(name, 15f, 0.3f, 46);
                case "horn_high": return Horn(name, 520f, 0.38f);
                case "horn_low": return Horn(name, 330f, 0.5f);
                case "stash_hum": return Shimmer(name);
                case "vault": return Swing(name);
                case "lift_hum": return Engine(name, 12f, 0.12f, 47);
                case "lift_ding": return Tones(name, new[] { 1046f, 784f }, 0.32f);
                case "victory": return Tones(name, new[] { 523f, 659f, 784f, 1047f }, 0.22f);
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

        // A bullet passing close by: a short, falling hiss.
        static AudioClip Whiz(string name)
        {
            var d = Buffer(0.18f);
            var r = new System.Random(77);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float cutoff = Mathf.Lerp(0.6f, 0.08f, t / 0.18f);
                lp += (White(r) - lp) * cutoff;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.18f));
                d[i] = lp * env * env;
            }
            return Clip(name, d);
        }

        // Rotor thump: low noise bursts at the blade rate over a turbine whine. Loops seamlessly
        // because it is a whole number of blade passes.
        static AudioClip Rotor(string name)
        {
            const float bladeRate = 19f;
            int passes = 38;
            var d = Buffer(passes / bladeRate);
            var r = new System.Random(19);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = (t * bladeRate) % 1f;
                lp += (White(r) - lp) * 0.08f;
                float thump = lp * Mathf.Exp(-ph / 0.18f) * 2.2f + Mathf.Sin(2f * Mathf.PI * 2f * bladeRate * t) * Mathf.Exp(-ph / 0.1f) * 0.6f;
                float whine = Mathf.Sin(2f * Mathf.PI * 1520f * t) * 0.05f;
                d[i] = thump + whine;
            }
            return Clip(name, d);
        }

        // A small engine for looping: firing pulses at the base rate with a buzzy saw and exhaust noise.
        // Played with pitch rising with speed. Exactly a whole number of firing cycles, so it loops cleanly.
        static AudioClip Engine(string name, float fireHz, float buzz, int seed)
        {
            int cycles = Mathf.RoundToInt(fireHz * 1.0f);
            var d = Buffer(cycles / fireHz);
            var r = new System.Random(seed);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ph = (t * fireHz) % 1f;
                lp += (White(r) - lp) * 0.12f;
                float pulse = Mathf.Exp(-ph / 0.12f);
                float saw = Saw(t * fireHz * 2f) * buzz + Saw(t * fireHz * 4f) * buzz * 0.4f;
                d[i] = (saw * 0.5f + lp * 1.6f) * (0.35f + pulse) * 0.6f;
            }
            return Clip(name, d);
        }

        // Rushing air: low-passed noise with a slow swell (loops when it is used as a bed).
        static AudioClip Wind(string name, float seconds, float bright)
        {
            var d = Buffer(seconds);
            var r = new System.Random(name.Length * 5);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float u = t / seconds;
                lp += (White(r) - lp) * (bright + 0.04f * Mathf.Sin(2f * Mathf.PI * u * 3f));
                float env = name == "wind" ? 1f : Mathf.Sin(Mathf.PI * u);
                d[i] = lp * env;
            }
            // Fade the ends together so the loop has no click.
            int fade = Mathf.Min(d.Length / 4, Rate / 20);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                d[i] *= k;
                d[d.Length - 1 - i] *= k;
            }
            return Clip(name, d);
        }

        static AudioClip Tones(string name, float[] notes, float step)
        {
            var d = Buffer(notes.Length * step + 0.3f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                int n = Mathf.Min(notes.Length - 1, (int)(t / step));
                float local = t - n * step;
                d[i] = (Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[n] * t)) * Mathf.Exp(-local / 0.15f);
            }
            return Clip(name, d);
        }

        // A stash opening: a latch clack, a hinge creak, then a bright rising sparkle.
        static AudioClip StashOpen(string name)
        {
            var d = Buffer(1.1f);
            var r = new System.Random(57);
            float[] notes = { 784f, 988f, 1175f, 1568f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float v = White(r) * Mathf.Exp(-t / 0.012f) * 0.8f;                                  // clack
                if (t > 0.05f && t < 0.4f)                                                          // creak
                {
                    float c = t - 0.05f;
                    v += Saw(c * (180f + 60f * Mathf.Sin(c * 23f))) * 0.18f * Mathf.Sin(Mathf.PI * c / 0.35f);
                }
                for (int n = 0; n < notes.Length; n++)                                              // sparkle
                {
                    float s0 = 0.3f + n * 0.08f;
                    if (t < s0) continue;
                    float l = t - s0;
                    v += Mathf.Sin(2f * Mathf.PI * notes[n] * l) * Mathf.Exp(-l / 0.25f) * 0.35f;
                }
                d[i] = v;
            }
            return Clip(name, d);
        }

        // A horn: two detuned buzzy tones, held, with a quick swell and release.
        static AudioClip Horn(string name, float hz, float seconds)
        {
            var d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((seconds - t) / 0.05f);
                float v = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t)) * 0.5f + Mathf.Sin(2f * Mathf.PI * hz * 1.26f * t) * 0.5f
                        + Mathf.Sin(4f * Mathf.PI * hz * t) * 0.2f;
                d[i] = v * env;
            }
            return Clip(name, d);
        }

        // The hum a closed stash makes: two soft fifths with a slow shimmer, looping cleanly every 2 s.
        static AudioClip Shimmer(string name)
        {
            var d = Buffer(2f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float wob = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 1.5f * t);
                d[i] = (Mathf.Sin(2f * Mathf.PI * 440f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 660f * t) * 0.35f
                        + Mathf.Sin(2f * Mathf.PI * 1320f * t) * 0.12f * wob) * wob;
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
