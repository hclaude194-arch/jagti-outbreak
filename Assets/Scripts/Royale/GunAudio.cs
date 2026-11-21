using Jagti.Audio;
using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The sounds of the guns, on top of SoundSystem: each gun's own shot, the tail of the place it is
    /// fired in (open ground or a room) and the distant crack and echo far away; suppressed shots;
    /// casings landing on the ground underfoot; the pump and the bolt; and reloads played as their
    /// real steps (magazine out, magazine in, the charging handle; shells one by one; the revolver's
    /// cylinder; the double's break-open; the machine gun's belt) spread over the gun's reload time.
    /// </summary>
    public static class GunAudio
    {
        /// <summary>The calibre group the shot's tails come from.</summary>
        public static string Group(GunDef def)
        {
            switch (def.sound)
            {
                case "pistol": case "smg": case "revolver": return "light";
                case "shotgun": case "double": return "shot";
                case "dmr": case "sniper": return "heavy";
                default: return "rifle";
            }
        }

        /// <summary>Rifle-calibre rounds crack as they pass; pistol and buckshot whizz.</summary>
        public static bool Supersonic(GunDef def)
        {
            return def.gunClass == GunClass.Rifle || def.gunClass == GunClass.Marksman;
        }

        static string Core(GunDef def, bool suppressed)
        {
            if (!suppressed) return def.sound;
            string g = Group(def);
            return "suppressed_" + (g == "shot" ? "rifle" : g);
        }

        /// <summary>One shot. own: the local player's (played in the head, with its casing and action).</summary>
        public static void Shot(GunDef def, bool suppressed, Vector3 at, bool own)
        {
            string core = Core(def, suppressed);
            if (SoundSystem.Instance == null || !SoundBank.Has(core))
            {
                // No bank (or no sound system yet): the old synthesised sounds.
                if (own) SynthAudio.Play2D(suppressed ? "suppressed" : def.sound, 0.9f);
                else Ballistics.SoundAt(at, suppressed ? "suppressed" : def.sound, 1f);
                return;
            }
            SoundSystem.Gunshot(core, Group(def), suppressed, at, own);
            bool casing = def.gunClass != GunClass.Shotgun && def.id != 5 && !def.bolt;
            if (own)
            {
                if (def.sound == "shotgun") SoundSystem.PlayLater("pump", 0.32f, null, 0.8f);
                if (casing) Casing(at, false, 0.38f);
                else if (def.sound == "shotgun") Casing(at, true, 0.75f);
            }
            else if (casing && (at - SoundSystem.ListenerPosition).sqrMagnitude < 12f * 12f) Casing(at, false, 0.4f);
        }

        static void Casing(Vector3 shooterHead, bool hull, float delay)
        {
            bool found;
            Vector3 feet = shooterHead + Vector3.down * 1.4f + Random.insideUnitSphere * 0.5f;
            var s = AcousticSpace.Under(shooterHead + Vector3.down * 1f, out found);
            if (!found) return;
            bool soft = s == AcousticSpace.Surface.Grass || s == AcousticSpace.Surface.Dirt || s == AcousticSpace.Surface.Water;
            string evt = hull ? (soft ? "shell_soft" : "hull_hard") : (soft ? "shell_soft" : "shell_hard");
            SoundSystem.PlayLater(evt, delay + Random.Range(0f, 0.15f), feet, 0.8f);
        }

        struct Step
        {
            public float at;
            public string name;
            public Step(float at, string name) { this.at = at; this.name = name; }
        }

        /// <summary>
        /// Plays a reload's steps over `seconds`. at null: in the head (the player), else at that point.
        /// tag lets a reload cut short drop the rest (SoundSystem.CancelLater).
        /// </summary>
        public static void Reload(GunDef def, float seconds, int missing, Vector3? at, int tag, float volume = 1f)
        {
            if (SoundSystem.Instance == null || !SoundBank.Has("mag_out_light"))
            {
                if (at.HasValue) Ballistics.SoundAt(at.Value, "reload", 0.5f);
                else SynthAudio.Play2D("reload", 0.7f);
                return;
            }
            var steps = new System.Collections.Generic.List<Step>();
            switch (def.sound)
            {
                case "pistol":
                    steps.Add(new Step(0.12f, "mag_out_light")); steps.Add(new Step(0.55f, "mag_in_light")); steps.Add(new Step(0.8f, "slide_release"));
                    break;
                case "smg":
                    steps.Add(new Step(0.15f, "mag_out_light")); steps.Add(new Step(0.55f, "mag_in_light")); steps.Add(new Step(0.82f, "bolt_rack"));
                    break;
                case "revolver":
                    steps.Add(new Step(0.06f, "cyl_open")); steps.Add(new Step(0.2f, "cyl_eject")); steps.Add(new Step(0.55f, "speedloader")); steps.Add(new Step(0.86f, "cyl_close"));
                    break;
                case "shotgun":
                    {
                        int shells = Mathf.Clamp(missing, 1, def.magazine);
                        for (int i = 0; i < shells; i++) steps.Add(new Step(0.1f + 0.7f * i / Mathf.Max(1, shells), "shell_insert"));
                        steps.Add(new Step(0.9f, "pump"));
                        break;
                    }
                case "double":
                    steps.Add(new Step(0.06f, "break_open")); steps.Add(new Step(0.22f, "hulls_eject"));
                    steps.Add(new Step(0.5f, "shell_insert")); steps.Add(new Step(0.66f, "shell_insert")); steps.Add(new Step(0.86f, "break_close"));
                    break;
                case "lmg":
                    steps.Add(new Step(0.06f, "belt_open")); steps.Add(new Step(0.25f, "mag_out_heavy")); steps.Add(new Step(0.5f, "mag_in_heavy"));
                    steps.Add(new Step(0.62f, "belt_rattle")); steps.Add(new Step(0.78f, "belt_close")); steps.Add(new Step(0.9f, "charge_rifle"));
                    break;
                case "sniper":
                    steps.Add(new Step(0.08f, "bolt_open")); steps.Add(new Step(0.35f, "mag_out_heavy")); steps.Add(new Step(0.62f, "mag_in_heavy")); steps.Add(new Step(0.86f, "bolt_close"));
                    break;
                default:   // rifles, carbine, DMR
                    steps.Add(new Step(0.15f, def.sound == "dmr" ? "mag_out_heavy" : "mag_out_light"));
                    steps.Add(new Step(0.52f, def.sound == "dmr" ? "mag_in_heavy" : "mag_in_heavy"));
                    steps.Add(new Step(0.8f, "charge_rifle"));
                    break;
            }
            foreach (var s in steps) SoundSystem.PlayLater(s.name, s.at * seconds, at, volume, tag);
        }
    }
}
