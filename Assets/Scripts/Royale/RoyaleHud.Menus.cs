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
            float ph = Mathf.Min(h - py - 80f, pauseTab == 0 ? 720f : 480f);
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
