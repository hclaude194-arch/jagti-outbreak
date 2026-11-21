using Jagti.Royale;
using UnityEngine;

namespace Jagti.Audio
{
    /// <summary>
    /// The score follows the match: the menu theme before it, the airy bus cue aboard and on the way
    /// down, then the quiet exploring layer on the ground with the fight layer (drums, bass, strings)
    /// rising over it, in step, as shooting breaks out near you and falling away after; the tense final
    /// cue for the last circles or the last five fighters; a sting when you win or are knocked out,
    /// then the menu theme softly under the results. In the zombie mode, exploring and fighting only.
    /// Music ducks under the announcer.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        class Deck
        {
            public AudioSource src;
            public string track;
            public float vol, target;
        }

        Deck main, other, combat, sting;
        float combatLevel;
        bool stingPlayed;
        float stingEnds;

        void Start()
        {
            main = MakeDeck("Music A");
            other = MakeDeck("Music B");
            combat = MakeDeck("Music Fight");
            sting = MakeDeck("Music Sting");
            sting.src.loop = false;
        }

        Deck MakeDeck(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.loop = true;
            src.playOnAwake = false;
            src.priority = 0;
            src.reverbZoneMix = 0f;
            src.bypassReverbZones = true;
            AudioBuses.Route(src, Bus.Music);
            return new Deck { src = src };
        }

        string Wanted(out float level)
        {
            level = 1f;
            var md = MatchDirector.Instance;
            if (md == null) return "music_explore";
            if (!md.Begun) return "music_menu";
            if (md.Over)
            {
                level = 0.55f;
                return Time.time < stingEnds ? null : "music_menu";
            }
            var diver = md.playerDiver;
            if (diver != null && (diver.Current == SkyDiver.State.Aboard || diver.Current == SkyDiver.State.FreeFall ||
                                  diver.Current == SkyDiver.State.Glide || diver.Current == SkyDiver.State.Waiting)) return "music_bus";
            var haze = md.haze;
            bool lateHaze = haze != null && haze.rounds != null && haze.RoundIndex >= haze.rounds.Length - 2;
            if (lateHaze || Combatant.AliveCount() <= 5) return "music_final";
            if (md.player != null && !md.player.Alive) level = 0.6f;
            return "music_explore";
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var md = MatchDirector.Instance;

            if (md != null && md.Over && !stingPlayed)
            {
                stingPlayed = true;
                var def = SoundBank.Find(md.PlayerWon ? "victory" : "music_defeat");
                var clip = SoundBank.Clip(def);
                if (clip != null)
                {
                    sting.src.clip = clip;
                    sting.src.volume = def.vol * AudioBuses.Gain(Bus.Music);
                    sting.src.Play();
                    stingEnds = Time.time + clip.length - 1.5f;
                }
            }
            if (md != null && !md.Over) stingPlayed = false;

            float level;
            string want = Wanted(out level);
            if (want != main.track)
            {
                var t = main; main = other; other = t;      // the old deck fades out, the new one comes in
                other.target = 0f;
                main.track = want;
                main.vol = 0f;
                if (want != null)
                {
                    var def = SoundBank.Find(want);
                    var clip = SoundBank.First(def);
                    if (clip != null)
                    {
                        main.src.clip = clip;
                        double at = AudioSettings.dspTime + 0.1;
                        main.src.PlayScheduled(at);
                        if (want == "music_explore")
                        {
                            var fight = SoundBank.First(SoundBank.Find("music_combat"));
                            if (fight != null)
                            {
                                combat.src.clip = fight;
                                combat.src.PlayScheduled(at);    // the two layers are the same length and tempo
                                combat.track = "music_combat";
                            }
                        }
                    }
                    else main.track = null;
                }
            }

            float duck = Announcer.Speaking ? 0.45f : 1f;
            float gain = AudioBuses.Gain(Bus.Music) * duck;
            main.target = main.track != null ? level : 0f;
            Fade(main, dt, 0.4f, gain, VolumeOf(main.track));
            Fade(other, dt, 0.35f, gain, VolumeOf(other.track));

            // The fight layer follows the shooting near you.
            float heat = main.track == "music_explore" ? SoundSystem.Heat : 0f;
            float targetFight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.6f, heat));
            combatLevel = Mathf.MoveTowards(combatLevel, targetFight, dt * (targetFight > combatLevel ? 0.6f : 0.12f));
            combat.src.volume = combatLevel * VolumeOf("music_combat") * gain;
            if (main.track != "music_explore" && combat.src.isPlaying && combatLevel <= 0.001f && other.vol <= 0.001f) combat.src.Stop();
            sting.src.volume = VolumeOf(md != null && md.PlayerWon ? "victory" : "music_defeat") * AudioBuses.Gain(Bus.Music) * duck;
        }

        static float VolumeOf(string track)
        {
            var d = SoundBank.Find(track);
            return d != null ? d.vol : 1f;
        }

        static void Fade(Deck d, float dt, float rate, float gain, float vol)
        {
            d.vol = Mathf.MoveTowards(d.vol, d.target, dt * rate);
            d.src.volume = d.vol * gain * vol;
            if (d.vol <= 0.001f && d.target <= 0f && d.src.isPlaying) d.src.Stop();
        }
    }
}
