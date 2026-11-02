using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>Run-wide state shared by the player, zombies and HUD. Reset when the scene loads.</summary>
    public static class GameState
    {
        public static bool HudActive;   // a GameHud owns Esc and the pause menu
        public static bool Paused;
        public static bool PlayerDead;
        public static int Kills;
        public static float TimeSurvived;

        public static void Reset()
        {
            Paused = false;
            PlayerDead = false;
            Kills = 0;
            TimeSurvived = 0f;
            Time.timeScale = 1f;
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }

    /// <summary>
    /// Sounds the zombies can hear. Footsteps, gunshots, swings and searching all call Emit
    /// with how far the sound carries; zombies inside that radius come to look.
    /// </summary>
    public static class NoiseEvents
    {
        public static event System.Action<Vector3, float> Heard;

        public static void Emit(Vector3 position, float radius)
        {
            var handler = Heard;
            if (handler != null) handler(position, radius);
        }
    }
}
