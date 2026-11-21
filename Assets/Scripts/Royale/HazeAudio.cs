using Jagti.Audio;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// How the haze sounds: its wall drones and sizzles from the nearest point of the ring; a warning
    /// swell and the announcer when the next circle is shown, a deep surge when it starts to move; and
    /// inside it the world goes thick and dull (the global muffle) under a choking roar. Works online
    /// too, since it only watches HazeZone's state.
    /// </summary>
    public class HazeAudio : MonoBehaviour
    {
        public HazeZone haze;

        SoundLoop wall, inside;
        GameObject wallHost;
        int lastRound = -1;
        bool lastClosing, wasRunning;

        void Start()
        {
            if (haze == null) haze = FindObjectOfType<HazeZone>();
            wallHost = new GameObject("Haze Wall Sound");
            wallHost.transform.SetParent(transform, false);
            wall = SoundLoop.Create(wallHost, "haze_wall");
            inside = SoundLoop.Create(gameObject, "haze_inside", false);
            if (inside != null) inside.VolumeEase = 1.2f;
        }

        void OnDisable()
        {
            SoundSystem.Muffle = 0f;
        }

        void Update()
        {
            if (haze == null) return;
            Vector3 ear = SoundSystem.ListenerPosition;
            if (!haze.Running)
            {
                if (wall != null) wall.Volume = 0f;
                if (inside != null) inside.Volume = 0f;
                SoundSystem.Muffle = 0f;
                wasRunning = false;
                lastRound = -1;
                return;
            }

            // The wall: from the point of the ring nearest the ears, at ear height.
            Vector2 c = haze.Centre;
            Vector2 d = new Vector2(ear.x, ear.z) - c;
            if (d.sqrMagnitude < 0.01f) d = Vector2.right;
            Vector2 p = c + d.normalized * haze.Radius;
            wallHost.transform.position = new Vector3(p.x, ear.y, p.y);
            if (wall != null) wall.Volume = 1f;

            float thick = haze.PlayerInHaze;
            if (inside != null) inside.Volume = thick;
            SoundSystem.Muffle = thick * 0.6f;

            bool lastCircle = haze.rounds != null && haze.RoundIndex >= haze.rounds.Length - 1;
            if (!wasRunning)
            {
                wasRunning = true;
                lastRound = haze.RoundIndex;
                lastClosing = haze.Closing;
                return;
            }
            if (haze.RoundIndex != lastRound && haze.rounds != null && haze.RoundIndex < haze.rounds.Length)
            {
                SoundSystem.Play2D("haze_warning");
                Announcer.Say(lastCircle ? "haze_final" : "haze_next");
            }
            if (haze.Closing && !lastClosing)
            {
                SoundSystem.Play2D("haze_moving");
                Announcer.Say("haze_moving");
            }
            lastRound = haze.RoundIndex;
            lastClosing = haze.Closing;
        }
    }
}
