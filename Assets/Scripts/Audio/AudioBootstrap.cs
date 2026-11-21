using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jagti.Audio
{
    /// <summary>
    /// Puts the sound in every scene without a scene rebuild: after each scene loads it makes one
    /// "Jagti Audio" object with the sound system, footsteps, ambience, music and the announcer, plus
    /// the haze's sound and the hit sounds in a battle royale match. Before the first scene it raises
    /// the number of real voices (a gunfight with rain of casings, engines and birds needs more than
    /// Unity's default 32). Nothing happens if the sound library (Resources/Audio/sound_bank.json)
    /// is missing: the old synthesised sounds play as before.
    /// </summary>
    public static class AudioBootstrap
    {
        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeFirstScene()
        {
            var config = AudioSettings.GetConfiguration();
            if (config.numRealVoices < 64 || config.numVirtualVoices < 256)
            {
                config.numRealVoices = Mathf.Max(config.numRealVoices, 64);
                config.numVirtualVoices = Mathf.Max(config.numVirtualVoices, 256);
                AudioSettings.Reset(config);
            }
            if (!hooked)
            {
                hooked = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene()
        {
            Build();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) Build();
        }

        static void Build()
        {
            if (SoundSystem.Instance != null || !SoundBank.Loaded) return;
            var go = new GameObject("Jagti Audio");
            go.AddComponent<SoundSystem>();
            go.AddComponent<FootstepSystem>();
            go.AddComponent<AmbienceDirector>();
            go.AddComponent<MusicDirector>();
            go.AddComponent<Announcer>();
            var haze = Object.FindObjectOfType<Jagti.Royale.HazeZone>();
            if (haze != null)
            {
                go.AddComponent<Jagti.Royale.HazeAudio>().haze = haze;
                go.AddComponent<Jagti.Royale.CombatAudio>();
            }
            Jagti.Royale.GameSettings.ApplySound();
            AudioBuses.Apply();
        }
    }
}
