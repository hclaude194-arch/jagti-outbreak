using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Player settings kept in PlayerPrefs: mouse sensitivity, field of view, volume, HUD scale,
    /// crosshair colour, damage numbers, minimap size and the frame-time readout. The menus change
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

        public static bool BigMinimap
        {
            get { Load(); return bigMinimap; }
            set { Load(); if (value == bigMinimap) return; bigMinimap = value; PlayerPrefs.SetInt(Prefix + "BigMinimap", value ? 1 : 0); Changed(); }
        }
    }
}
