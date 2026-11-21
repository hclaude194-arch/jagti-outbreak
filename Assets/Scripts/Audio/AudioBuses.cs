using UnityEngine;
using UnityEngine.Audio;

namespace Jagti.Audio
{
    /// <summary>The mixer groups every sound is routed through.</summary>
    public enum Bus { Master, Music, Ambience, Effects, Weapons, World, Vehicles, Interface, Voice }

    /// <summary>
    /// Routing to the Audio Mixer (Assets/Resources/Audio/JagtiMixer.mixer, made by tools/audio/mixer.py)
    /// and the volume sliders. Each group's fader is exposed as "&lt;Group&gt;Volume" in dB. If the mixer
    /// can't be loaded, sources play unrouted and Gain() applies the sliders instead, so nothing goes
    /// silent or ignores the settings.
    /// </summary>
    public static class AudioBuses
    {
        public static readonly string[] Names = { "Master", "Music", "Ambience", "Effects", "Weapons", "World", "Vehicles", "Interface", "Voice" };

        static AudioMixer mixer;
        static AudioMixerGroup[] groups;
        static bool tried;

        // Slider values (0..1) for the buses the menu offers; the rest follow their parent.
        public static readonly float[] Volume = { 1f, 0.6f, 0.8f, 0.9f, 1f, 1f, 1f, 0.8f, 0.9f };

        public static Bus Parse(string name)
        {
            for (int i = 0; i < Names.Length; i++)
                if (Names[i] == name) return (Bus)i;
            return Bus.World;
        }

        public static AudioMixer Mixer
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    mixer = Resources.Load<AudioMixer>("Audio/JagtiMixer");
                    if (mixer != null)
                    {
                        groups = new AudioMixerGroup[Names.Length];
                        for (int i = 0; i < Names.Length; i++)
                        {
                            var found = mixer.FindMatchingGroups(Names[i]);
                            foreach (var g in found)
                                if (g != null && g.name == Names[i]) { groups[i] = g; break; }
                        }
                    }
                    else Debug.LogWarning("[Jagti] Audio: the mixer (Resources/Audio/JagtiMixer) didn't load; volumes are applied per sound instead.");
                    Apply();
                }
                return mixer;
            }
        }

        public static AudioMixerGroup Group(Bus bus)
        {
            if (Mixer == null || groups == null) return null;
            return groups[(int)bus];
        }

        static Bus Parent(Bus b)
        {
            switch (b)
            {
                case Bus.Weapons:
                case Bus.World:
                case Bus.Vehicles:
                    return Bus.Effects;
                case Bus.Master:
                    return Bus.Master;
                default:
                    return Bus.Master;
            }
        }

        /// <summary>
        /// The slider gain to multiply into a source's volume: 1 when the mixer is doing the job,
        /// otherwise the product of the bus's sliders up to the master.
        /// </summary>
        public static float Gain(Bus bus)
        {
            if (Mixer != null && groups != null && groups[(int)bus] != null) return 1f;
            float g = Volume[(int)bus];
            Bus b = bus;
            while (b != Bus.Master)
            {
                b = Parent(b);
                g *= Volume[(int)b];
            }
            return g;
        }

        /// <summary>Puts the slider values on the mixer's exposed faders.</summary>
        public static void Apply()
        {
            if (mixer == null) return;
            for (int i = 0; i < Names.Length; i++)
                mixer.SetFloat(Names[i] + "Volume", ToDb(Volume[i]));
        }

        public static void Set(Bus bus, float value)
        {
            Volume[(int)bus] = Mathf.Clamp01(value);
            if (Mixer != null) Apply();
        }

        /// <summary>Slider (0..1) to decibels, on a curve that feels even across its travel.</summary>
        public static float ToDb(float v)
        {
            if (v <= 0.0001f) return -80f;
            return Mathf.Max(-80f, 20f * Mathf.Log10(v * v));
        }

        /// <summary>Routes a source to its group (and keeps the right reverb share).</summary>
        public static void Route(AudioSource src, Bus bus)
        {
            if (src == null) return;
            src.outputAudioMixerGroup = Group(bus);
            if (bus == Bus.Music || bus == Bus.Interface || bus == Bus.Voice) src.reverbZoneMix = 0f;
        }
    }
}
