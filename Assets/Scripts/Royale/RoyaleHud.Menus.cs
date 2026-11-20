using UnityEngine;

namespace Jagti.Royale
{
    // The pause menu (Esc) with its settings and controls pages, and the results screen.
    public partial class RoyaleHud
    {
        int pauseTab;           // 0 settings, 1 controls

        void DrawPause(float w, float h)
        {
            HudKit.Fill(0, 0, w, h, new Color(0.02f, 0.025f, 0.03f, 0.55f));
            HudKit.FadeSidePanel(0, 0, Mathf.Min(760f, w * 0.6f), h, new Color(0.02f, 0.025f, 0.03f, 0.9f));
            bool online = NetSession.Online;
            float x = 80f, y = Mathf.Max(60f, h * 0.12f);
            HudKit.Label(x, y, 400f, 24f, "JAGTI ROYALE", 16f, HudKit.Accent, TextAnchor.MiddleLeft, true);
            HudKit.Label(x, y + 18f, 400f, 70f, online ? "MENU" : "PAUSED", 64f, HudKit.Text, TextAnchor.MiddleLeft, true);
            if (online) HudKit.Paragraph(x, y + 90f, 320f, 60f, "Online the match goes on while this menu is open. " + OnlineLine(), 15f, HudKit.TextDim);
            y += 160f;

            const float bw = 300f;
            if (HudKit.Button(x, y, bw, 56f, "RESUME", HudKit.ButtonKind.Primary)) SetPaused(false);
            y += 76f;
            if (HudKit.Button(x, y, bw, 46f, "SETTINGS", pauseTab == 0 ? HudKit.ButtonKind.TabOn : HudKit.ButtonKind.Tab)) pauseTab = 0;
            y += 50f;
            if (HudKit.Button(x, y, bw, 46f, "CONTROLS", pauseTab == 1 ? HudKit.ButtonKind.TabOn : HudKit.ButtonKind.Tab)) pauseTab = 1;
            y += 70f;
            if (online)
            {
                if (HudKit.Button(x, y, bw, 46f, NetSession.IsHost ? "CLOSE THE GAME" : "LEAVE THE GAME")) { SetPaused(false); director.ToMenu(); }
            }
            else if (HudKit.Button(x, y, bw, 46f, "NEW MATCH")) { SetPaused(false); director.Restart(); }
            y += 54f;
            if (HudKit.Button(x, y, bw, 46f, "QUIT TO DESKTOP", HudKit.ButtonKind.Quiet)) Quit();

            // The page on the right.
            float px = x + bw + 60f, pw = Mathf.Min(640f, w - px - 60f), py = Mathf.Max(60f, h * 0.12f) + 20f;
            if (pw < 360f) return;
            float ph = Mathf.Min(h - py - 80f, pauseTab == 0 ? MenuPanels.SettingsPanelHeight : 480f);
            HudKit.Panel(px, py, pw, ph, new Color(0.06f, 0.07f, 0.09f, 0.85f), 10f);
            HudKit.Outline(px, py, pw, ph, HudKit.Line, 1f, 10f);
            HudKit.Label(px + 32f, py + 20f, pw - 64f, 40f, pauseTab == 0 ? "SETTINGS" : "CONTROLS", 30f, HudKit.Text, TextAnchor.MiddleLeft, true);
            if (pauseTab == 0) MenuPanels.Settings(px + 32f, py + 76f, pw - 64f, director);
            else MenuPanels.Controls(px + 32f, py + 80f, pw - 64f);
            HudKit.Hints(px + pw / 2f, h - 56f, 24f, "ESC", "Back to the match");
        }

        void DrawResults(float w, float h)
        {
            bool won = director.PlayerWon;
            HudKit.Fill(0, 0, w, h, new Color(0.02f, 0.025f, 0.03f, 0.8f));
            HudKit.FadePanel(0, 0, w, h * 0.5f, HudKit.WithAlpha(won ? HudKit.Accent : new Color(0.5f, 0.1f, 0.06f), 0.28f));

            float y = h * 0.1f;
            HudKit.Label(0, y, w, 24f, won ? "VICTORY ON THE JAGTI CAMPUS" : "MATCH OVER", 18f, HudKit.Accent, TextAnchor.MiddleCenter, true);
            int place = won ? 1 : Mathf.Max(1, player.Placement);
            HudKit.Label(0, y + 14f, w, 150f, "#" + place, 140f, won ? HudKit.Accent : HudKit.Text, TextAnchor.MiddleCenter, true);
            HudKit.Label(0, y + 150f, w, 26f, "OF " + director.StartCount, 20f, HudKit.TextDim, TextAnchor.MiddleCenter, true);
            string head = won ? "TOP OF THE HILL" : player.LastAttacker != null ? "TAKEN OUT BY " + player.LastAttacker.displayName.ToUpper() : "LOST IN THE HAZE";
            HudKit.Label(0, y + 186f, w, 44f, head, 36f, HudKit.Text, TextAnchor.MiddleCenter, true);

            // Stat cards.
            float survived = won ? director.MatchTime : player.DiedAt - director.StartTime;
            const float cw = 210f, ch = 112f, gap = 16f;
            float cx = w / 2f - (cw * 4f + gap * 3f) / 2f, cy = y + 256f;
            StatCard(cx, cy, cw, ch, "target", "ELIMINATIONS", player.Kills.ToString());
            StatCard(cx + (cw + gap), cy, cw, ch, "ammo", "DAMAGE DEALT", Mathf.RoundToInt(player.DamageDealt).ToString());
            StatCard(cx + (cw + gap) * 2f, cy, cw, ch, "clock", "SURVIVED", HudKit.Clock(survived));
            StatCard(cx + (cw + gap) * 3f, cy, cw, ch, "person", "PLACED", "#" + place + " / " + director.StartCount);

            float by = cy + ch + 44f;
            // Watch the rest of the match (only after losing, while someone is still fighting).
            if (!won && SpectatorCam.Instance != null && SpectatorCam.AnyoneToWatch() && Combatant.AliveCount() > 1)
            {
                if (HudKit.Button(w / 2f - 130f, by - 4f, 260f, 46f, "SPECTATE")) SetSpectating(true);
                by += 58f;
            }
            if (NetSession.IsClient)
            {
                HudKit.Label(0, by, w, 24f, OnlineLine(), 16f, HudKit.TextDim, TextAnchor.MiddleCenter);
                HudKit.Label(0, by + 26f, w, 30f, "The host starts the next match.", 20f, HudKit.Text, TextAnchor.MiddleCenter);
                if (HudKit.Button(w / 2f - 130f, by + 76f, 260f, 56f, "LEAVE THE GAME")) director.ToMenu();
                return;
            }
            if (NetSession.IsHost) HudKit.Label(0, by - 30f, w, 24f, OnlineLine(), 16f, HudKit.TextDim, TextAnchor.MiddleCenter);
            string again = !NetSession.IsHost ? "PLAY AGAIN" : Combatant.AliveCount() > 1 ? "END IT, BACK TO THE LOBBY" : "BACK TO THE LOBBY";
            string sub = NetSession.IsHost && Combatant.AliveCount() > 1 ? null : "ENTER";
            const float pw = 320f, sw = 220f, qw = 150f, bg = 14f;
            float bx = w / 2f - (pw + sw + qw + bg * 2f) / 2f;
            if (HudKit.Button(bx, by, pw, 64f, again, HudKit.ButtonKind.Primary, true, sub)) director.Restart();
            if (HudKit.Button(bx + pw + bg, by, sw, 64f, NetSession.IsHost ? "CLOSE THE GAME" : "MAIN MENU")) director.ToMenu();
            if (HudKit.Button(bx + pw + sw + bg * 2f, by, qw, 64f, "QUIT", HudKit.ButtonKind.Quiet)) Quit();

            float dw = 560f;
            float dy = by + 90f;
            if (botNamesResults == null)
            {
                botNamesResults = new string[3];
                for (int i = 0; i < 3; i++) botNamesResults[i] = BotDifficulty.Name((BotDifficulty.Level)i);
            }
            int picked = HudKit.Choice(w / 2f - dw / 2f, dy, dw, "Bots next match", botNamesResults, (int)BotDifficulty.Current);
            if (picked != (int)BotDifficulty.Current) BotDifficulty.Current = (BotDifficulty.Level)picked;
            HudKit.Label(0, dy + 42f, w, 22f, BotDifficulty.Describe(BotDifficulty.Current), 15f, HudKit.TextDim, TextAnchor.MiddleCenter);
        }

        static string[] botNamesResults;

        /// <summary>While the death camera plays: who did it, with what, from how far, and the place.</summary>
        void DrawDeathCam(float w, float h)
        {
            var spec = SpectatorCam.Instance;
            float age = spec.DeathAge;
            HudKit.EdgeVignette(new Color(0f, 0f, 0f, 0.6f));
            // Letterbox bars slide in.
            float bar = 70f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.6f));
            HudKit.Fill(0, 0, w, bar, Color.black);
            HudKit.Fill(0, h - bar, w, bar, Color.black);

            HudKit.Alpha = Mathf.Clamp01((age - 0.4f) * 3f);
            float y = h - 250f;
            var by = player.LastAttacker;
            string head = by != null && by != player ? "TAKEN OUT BY" : "YOU ARE OUT";
            HudKit.Label(0, y, w, 24f, head, 18f, HudKit.Danger, TextAnchor.MiddleCenter, true);
            if (by != null && by != player)
            {
                HudKit.Label(0, y + 20f, w, 60f, by.displayName.ToUpper(), 52f, HudKit.Text, TextAnchor.MiddleCenter, true);
                string weapon = "";
                for (int i = director.feed.Count - 1; i >= 0; i--)
                    if (director.feed[i].ofPlayer) { weapon = director.feed[i].weapon; break; }
                float dist = Vector3.Distance(by.transform.position, player.transform.position);
                string line = (string.IsNullOrEmpty(weapon) ? "" : weapon.ToUpper() + "   \u00b7   ") + Mathf.RoundToInt(dist) + " M AWAY";
                if (by.Alive) line += "   \u00b7   " + Mathf.CeilToInt(by.Health.current) + " HEALTH LEFT";
                HudKit.Label(0, y + 80f, w, 24f, line, 17f, HudKit.TextDim, TextAnchor.MiddleCenter, true);
            }
            else HudKit.Label(0, y + 20f, w, 60f, "LOST IN THE HAZE", 52f, HudKit.Text, TextAnchor.MiddleCenter, true);
            HudKit.Label(0, y + 112f, w, 26f, "PLACED #" + Mathf.Max(1, player.Placement) + " OF " + director.StartCount, 20f, HudKit.Accent, TextAnchor.MiddleCenter, true);
            HudKit.Alpha = 1f;
        }

        /// <summary>Spectating: the name bar of the one being watched, their health, and the keys.</summary>
        void DrawSpectating(float w, float h)
        {
            DrawKillFeed();
            DrawMatchInfo(w, false);
            DrawBanner(w, h);
            var spec = SpectatorCam.Instance;
            var t = spec != null ? spec.Target : null;
            float y = h - M - 150f;
            if (Combatant.AliveCount() <= 1)
            {
                // Someone has won.
                Combatant last = null;
                foreach (var c in Combatant.All) if (c.Alive) last = c;
                HudKit.Label(0, h * 0.2f, w, 24f, "MATCH OVER", 18f, HudKit.Accent, TextAnchor.MiddleCenter, true);
                if (last != null) HudKit.Label(0, h * 0.2f + 22f, w, 56f, last.displayName.ToUpper() + " IS THE LAST ONE STANDING", 40f, HudKit.Text, TextAnchor.MiddleCenter, true);
            }
            if (t != null)
            {
                const float pw = 460f;
                float x = w / 2f - pw / 2f;
                HudKit.Panel(x, y, pw, 92f, HudKit.Ink, 8f);
                HudKit.Label(x + 18f, y + 8f, 200f, 20f, "SPECTATING", 14f, HudKit.Accent, TextAnchor.MiddleLeft, true);
                HudKit.Label(x + 18f, y + 26f, pw - 140f, 36f, t.displayName.ToUpper(), 30f, HudKit.Text, TextAnchor.MiddleLeft, true);
                HudKit.IconAt("target", x + pw - 90f, y + 32f, 20f, HudKit.Accent);
                HudKit.Label(x + pw - 76f, y + 16f, 60f, 32f, t.Kills.ToString(), 26f, HudKit.Text, TextAnchor.MiddleLeft, true);
                float f = t.Alive ? t.Health.Fraction : 0f;
                HudKit.Bar(x + 18f, y + 68f, pw - 36f, 10f, f, f < 0.3f ? HudKit.HealthLow : HudKit.Health, 4, 2f);
                if (t.loadout.vest > 0)
                    HudKit.Bar(x + 18f, y + 62f, pw - 36f, 4f, t.loadout.vestDurability / RoyaleItems.ArmourDurability[t.loadout.vest], HudKit.Shield, 0, 2f);
                if (!t.Alive) HudKit.Label(x, y + 26f, pw - 18f, 36f, "OUT", 22f, HudKit.Danger, TextAnchor.MiddleRight, true);
            }
            HudKit.Hints(w / 2f, h - M - 30f, 26f, "LMB", "Next", "RMB", "Previous", "MOUSE", "Look around", "ENTER", "Results");
        }

        static void StatCard(float x, float y, float w, float h, string icon, string label, string value)
        {
            HudKit.Panel(x, y, w, h, HudKit.Ink, 8f);
            HudKit.Outline(x, y, w, h, HudKit.Line, 1f, 8f);
            HudKit.IconAt(icon, x + 26f, y + 28f, 22f, HudKit.Accent);
            HudKit.Label(x + 46f, y + 16f, w - 56f, 24f, label, 14f, HudKit.TextDim, TextAnchor.MiddleLeft, true);
            HudKit.Label(x + 18f, y + 46f, w - 36f, 56f, value, 46f, HudKit.Text, TextAnchor.MiddleLeft, true);
        }
    }
}
