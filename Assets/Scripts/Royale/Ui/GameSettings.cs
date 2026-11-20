using Jagti.World;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Player settings kept in PlayerPrefs: mouse sensitivity, field of view, volume, HUD scale,
    /// crosshair colour, damage numbers, minimap size, the frame-time readout, look inversion, aim
    /// sensitivity, hold-or-toggle aim and sprint, pausing in the background, the graphics tier
    /// (kept by VisualQuality, which applies it itself) and the time of day (kept by DayCycle). The menus change
    /// them through the setters, which save at once and bump Version; RoyaleHud applies them to the
    /// walker, the camera and the audio listener whenever Version moves.
    /// </summary>
    public static class GameSettings
    {
        const string Prefix = "Jagti.Settings.";

        static bool loaded;
        static float sensitivity = 2f, fov = 70f, volume = 0.85f, hudScale = 1f;
        static int crosshair;
        static bool damageNumbers = true, showFps, bigMinimap;
        static bool invertY, toggleAim, toggleSprint, backgroundPause = true;
        static float aimSensitivity = 1f;

        /// <summary>Goes up by one on every change.</summary>
        public static int Version { get; private set; }

        public static readonly string[] CrosshairNames = { "White", "Green", "Yellow", "Cyan" };
        static readonly Color[] CrosshairColours =
        {
            new Color(1f, 1f, 1f), new Color(0.45f, 1f, 0.45f), new Color(1f, 0.92f, 0.3f), new Color(0.35f, 0.95f, 1f),
        };

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            sensitivity = PlayerPrefs.GetFloat(Prefix + "Sensitivity", sensitivity);
            fov = PlayerPrefs.GetFloat(Prefix + "Fov", fov);
            volume = PlayerPrefs.GetFloat(Prefix + "Volume", volume);
            hudScale = PlayerPrefs.GetFloat(Prefix + "HudScale", hudScale);
            crosshair = PlayerPrefs.GetInt(Prefix + "Crosshair", crosshair);
            damageNumbers = PlayerPrefs.GetInt(Prefix + "DamageNumbers", 1) == 1;
            showFps = PlayerPrefs.GetInt(Prefix + "ShowFps", 0) == 1;
            bigMinimap = PlayerPrefs.GetInt(Prefix + "BigMinimap", 0) == 1;
            invertY = PlayerPrefs.GetInt(Prefix + "InvertY", 0) == 1;
            toggleAim = PlayerPrefs.GetInt(Prefix + "ToggleAim", 0) == 1;
            toggleSprint = PlayerPrefs.GetInt(Prefix + "ToggleSprint", 0) == 1;
            backgroundPause = PlayerPrefs.GetInt(Prefix + "BackgroundPause", 1) == 1;
            aimSensitivity = PlayerPrefs.GetFloat(Prefix + "AimSensitivity", aimSensitivity);
        }

        static void Changed()
        {
            Version++;
            PlayerPrefs.Save();
        }

        public static float Sensitivity
        {
            get { Load(); return sensitivity; }
            set { Load(); value = Mathf.Clamp(value, 0.3f, 8f); if (value == sensitivity) return; sensitivity = value; PlayerPrefs.SetFloat(Prefix + "Sensitivity", value); Changed(); }
        }

        public static float Fov
        {
            get { Load(); return fov; }
            set { Load(); value = Mathf.Clamp(Mathf.Round(value), 50f, 90f); if (value == fov) return; fov = value; PlayerPrefs.SetFloat(Prefix + "Fov", value); Changed(); }
        }

        public static float Volume
        {
            get { Load(); return volume; }
            set { Load(); value = Mathf.Clamp01(value); if (value == volume) return; volume = value; PlayerPrefs.SetFloat(Prefix + "Volume", value); Changed(); }
        }

        public static float HudScale
        {
            get { Load(); return hudScale; }
            set { Load(); value = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, 0.75f, 1.3f); if (value == hudScale) return; hudScale = value; PlayerPrefs.SetFloat(Prefix + "HudScale", value); Changed(); }
        }

        public static int Crosshair
        {
            get { Load(); return crosshair; }
            set { Load(); value = Mathf.Clamp(value, 0, CrosshairColours.Length - 1); if (value == crosshair) return; crosshair = value; PlayerPrefs.SetInt(Prefix + "Crosshair", value); Changed(); }
        }

        public static Color CrosshairColour { get { return CrosshairColours[Crosshair]; } }

        public static bool DamageNumbers
        {
            get { Load(); return damageNumbers; }
            set { Load(); if (value == damageNumbers) return; damageNumbers = value; PlayerPrefs.SetInt(Prefix + "DamageNumbers", value ? 1 : 0); Changed(); }
        }

        public static bool ShowFps
        {
            get { Load(); return showFps; }
            set { Load(); if (value == showFps) return; showFps = value; PlayerPrefs.SetInt(Prefix + "ShowFps", value ? 1 : 0); Changed(); }
        }

        public static readonly string[] GraphicsNames = VisualQuality.Names;

        /// <summary>Graphics quality: 0 Low, 1 Medium, 2 High.</summary>
        public static int Graphics
        {
            get { return (int)VisualQuality.Current; }
            set { value = Mathf.Clamp(value, 0, 2); if (value == (int)VisualQuality.Current) return; VisualQuality.Current = (VisualQuality.Level)value; Changed(); }
        }

        public static readonly string[] TimeOfDayNames = DayCycle.Names;

        /// <summary>Time of day: 0 Afternoon, 1 Sunset, 2 Night, 3 Sunset to night (kept by DayCycle).</summary>
        public static int TimeOfDay
        {
            get { return (int)DayCycle.Current; }
            set { value = Mathf.Clamp(value, 0, DayCycle.Names.Length - 1); if (value == (int)DayCycle.Current) return; DayCycle.Current = (DayCycle.Mood)value; Changed(); }
        }

        public static bool BigMinimap
        {
            get { Load(); return bigMinimap; }
            set { Load(); if (value == bigMinimap) return; bigMinimap = value; PlayerPrefs.SetInt(Prefix + "BigMinimap", value ? 1 : 0); Changed(); }
        }
    
        /// <summary>Mouse up looks down.</summary>
        public static bool InvertY
        {
            get { Load(); return invertY; }
            set { Load(); if (value == invertY) return; invertY = value; PlayerPrefs.SetInt(Prefix + "InvertY", value ? 1 : 0); Changed(); }
        }

        /// <summary>
        /// Mouse speed while aiming, on top of the zoom: at 1 the view turns as far per inch of mouse
        /// on screen at every zoom (the turn is scaled by the zoom), lower is slower.
        /// </summary>
        public static float AimSensitivity
        {
            get { Load(); return aimSensitivity; }
            set { Load(); value = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, 0.3f, 1.5f); if (value == aimSensitivity) return; aimSensitivity = value; PlayerPrefs.SetFloat(Prefix + "AimSensitivity", value); Changed(); }
        }

        /// <summary>Right mouse toggles aiming instead of holding it.</summary>
        public static bool ToggleAim
        {
            get { Load(); return toggleAim; }
            set { Load(); if (value == toggleAim) return; toggleAim = value; PlayerPrefs.SetInt(Prefix + "ToggleAim", value ? 1 : 0); Changed(); }
        }

        /// <summary>Shift starts a sprint that lasts until you stop moving forward.</summary>
        public static bool ToggleSprint
        {
            get { Load(); return toggleSprint; }
            set { Load(); if (value == toggleSprint) return; toggleSprint = value; PlayerPrefs.SetInt(Prefix + "ToggleSprint", value ? 1 : 0); Changed(); }
        }

        /// <summary>Clicking away from the game window opens the menu (offline, the match stops) and mutes it.</summary>
        public static bool BackgroundPause
        {
            get { Load(); return backgroundPause; }
            set { Load(); if (value == backgroundPause) return; backgroundPause = value; PlayerPrefs.SetInt(Prefix + "BackgroundPause", value ? 1 : 0); Changed(); }
        }

        /// <summary>Everything on the settings pages back to how it ships (graphics and time of day included).</summary>
        public static void ResetDefaults()
        {
            Load();
            sensitivity = 2f; fov = 70f; volume = 0.85f; hudScale = 1f; crosshair = 0;
            damageNumbers = true; showFps = false; bigMinimap = false;
            invertY = false; toggleAim = false; toggleSprint = false; backgroundPause = true; aimSensitivity = 1f;
            foreach (var key in new[] { "Sensitivity", "Fov", "Volume", "HudScale", "Crosshair", "DamageNumbers", "ShowFps", "BigMinimap",
                                        "InvertY", "ToggleAim", "ToggleSprint", "BackgroundPause", "AimSensitivity" })
                PlayerPrefs.DeleteKey(Prefix + key);
            Graphics = 2;   // High is the default (VisualQuality)
            TimeOfDay = DayCycle.Names.Length - 1;
            Changed();
        }
    }
}
