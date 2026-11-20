using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The settings and controls pages, shared by the front menu and the pause menu
    /// (drawn with HudKit, in reference units).
    /// </summary>
    public static class MenuPanels
    {
        static string[] botNames;

        static void Heading(float x, float y, float w, string text)
        {
            HudKit.Label(x, y, w, 26f, text, 15f, HudKit.Accent, TextAnchor.MiddleLeft, true, false);
            HudKit.Fill(x + HudKit.TextWidth(text, 15f, true) + 12f, y + 13f, w - HudKit.TextWidth(text, 15f, true) - 12f, 1f, HudKit.Line);
        }

        /// <summary>Which settings page is open: 0 game (controls, aim, bots), 1 screen and sound.</summary>
        public static int SettingsPage;

        /// <summary>The height of the panel the settings page sits in (title included), for the page now open.</summary>
        public static float SettingsPanelHeight { get { return SettingsPage == 0 ? 580f : 680f; } }

        /// <summary>
        /// Settings, in two pages picked by tabs at the top, with a reset button; returns its height.
        /// director (may be null) tells when a bot change waits for the next match.
        /// </summary>
        public static float Settings(float x, float y, float w, MatchDirector director)
        {
            float y0 = y;
            const float row = 44f;
            float tw = 150f;
            if (HudKit.Button(x, y, tw, 36f, "GAME", SettingsPage == 0 ? HudKit.ButtonKind.TabOn : HudKit.ButtonKind.Tab)) SettingsPage = 0;
            if (HudKit.Button(x + tw + 8f, y, tw, 36f, "SCREEN AND SOUND", SettingsPage == 1 ? HudKit.ButtonKind.TabOn : HudKit.ButtonKind.Tab)) SettingsPage = 1;
            if (HudKit.Button(x + w - 120f, y, 120f, 36f, "RESET ALL", HudKit.ButtonKind.Quiet)) GameSettings.ResetDefaults();
            y += 54f;

            if (SettingsPage == 1)
            {
                Heading(x, y, w, "SCREEN");
                y += 30f;
                GameSettings.Graphics = HudKit.Choice(x, y, w, "Graphics", GameSettings.GraphicsNames, GameSettings.Graphics);
                y += row;
                GameSettings.TimeOfDay = HudKit.Choice(x, y, w, "Time of day", GameSettings.TimeOfDayNames, GameSettings.TimeOfDay);
                y += row;
                GameSettings.Fov = HudKit.Slider(2, x, y, w, "Field of view", GameSettings.Fov, 50f, 90f, Mathf.RoundToInt(GameSettings.Fov) + "\u00b0");
                y += row;
                GameSettings.HudScale = HudKit.Slider(4, x, y, w, "HUD size", GameSettings.HudScale, 0.75f, 1.3f, Mathf.RoundToInt(GameSettings.HudScale * 100f) + "%");
                y += row;
                GameSettings.Crosshair = HudKit.Choice(x, y, w, "Crosshair", GameSettings.CrosshairNames, GameSettings.Crosshair);
                y += row;
                GameSettings.DamageNumbers = HudKit.Switch(x, y, w, "Damage numbers", GameSettings.DamageNumbers);
                y += row;
                GameSettings.BigMinimap = HudKit.Switch(x, y, w, "Large minimap", GameSettings.BigMinimap);
                y += row;
                GameSettings.ShowFps = HudKit.Switch(x, y, w, "Frame time (F3)", GameSettings.ShowFps);
                y += row + 8f;

                Heading(x, y, w, "SOUND");
                y += 30f;
                GameSettings.Volume = HudKit.Slider(3, x, y, w, "Volume", GameSettings.Volume, 0f, 1f, Mathf.RoundToInt(GameSettings.Volume * 100f) + "%");
                y += row;
                GameSettings.BackgroundPause = HudKit.Switch(x, y, w, "Pause and mute in background", GameSettings.BackgroundPause);
                y += row;
                return y - y0;
            }

            Heading(x, y, w, "MOUSE");
            y += 30f;
            GameSettings.Sensitivity = HudKit.Slider(1, x, y, w, "Mouse sensitivity", GameSettings.Sensitivity, 0.3f, 8f, GameSettings.Sensitivity.ToString("0.0"));
            y += row;
            GameSettings.AimSensitivity = HudKit.Slider(5, x, y, w, "Aim sensitivity", GameSettings.AimSensitivity, 0.3f, 1.5f, Mathf.RoundToInt(GameSettings.AimSensitivity * 100f) + "%");
            y += row;
            GameSettings.InvertY = HudKit.Switch(x, y, w, "Invert look", GameSettings.InvertY);
            y += row + 8f;

            Heading(x, y, w, "KEYS");
            y += 30f;
            GameSettings.ToggleAim = HudKit.Switch(x, y, w, "Toggle aim (right mouse)", GameSettings.ToggleAim);
            y += row;
            GameSettings.ToggleSprint = HudKit.Switch(x, y, w, "Toggle sprint (Shift)", GameSettings.ToggleSprint);
            y += row + 8f;

            Heading(x, y, w, "BOTS");
            y += 30f;
            if (botNames == null)
            {
                botNames = new string[3];
                for (int i = 0; i < 3; i++) botNames[i] = BotDifficulty.Name((BotDifficulty.Level)i);
            }
            bool canChange = !NetSession.IsClient;
            int picked = HudKit.Choice(x, y, w, "Difficulty", botNames, (int)BotDifficulty.Current, canChange);
            if (canChange && picked != (int)BotDifficulty.Current) BotDifficulty.Current = (BotDifficulty.Level)picked;
            y += row;
            string note = canChange ? BotDifficulty.Describe(BotDifficulty.Current) : "The host chooses the bots.";
            if (canChange && director != null && director.Begun && BotDifficulty.Current != director.Difficulty) note += " From the next match.";
            HudKit.Paragraph(x + w * 0.42f, y - 4f, w * 0.58f, 44f, note, 15f, HudKit.TextDim);
            y += 40f;
            return y - y0;
        }

        static readonly string[] Keys =
        {
            "W A S D", "Move", "SHIFT", "Sprint", "SPACE", "Jump, open the glider", "C", "Crouch",
            "LMB", "Fire", "RMB", "Aim down sights", "R", "Reload", "1 2 3", "Guns (or the wheel)",
            "E", "Pick up, ride, zip line", "X", "Drop the gun", "T", "Marker where you look", "Q", "Bandage or medkit", "Z", "Chai flask",
            "G", "Grenade", "4", "Smoke grenade", "V", "Shoulder or first person", "F", "Torch",
            "M", "Map", "ESC", "Menu", "F3", "Frame time",
        };

        /// <summary>The key list in two columns; returns its height.</summary>
        public static float Controls(float x, float y, float w)
        {
            const float row = 36f;
            int perColumn = (Keys.Length / 2 + 1) / 2;
            float colW = (w - 24f) / 2f;
            for (int i = 0; i < Keys.Length / 2; i++)
            {
                float cx = x + (i < perColumn ? 0f : colW + 24f);
                float cy = y + (i % perColumn) * row;
                float kw = HudKit.KeyCap(cx, cy + 5f, Keys[i * 2], 26f);
                HudKit.Label(cx + Mathf.Max(kw, 70f) + 12f, cy, colW - 90f, 36f, Keys[i * 2 + 1], 18f, HudKit.Text);
            }
            return perColumn * row;
        }
    }
}
