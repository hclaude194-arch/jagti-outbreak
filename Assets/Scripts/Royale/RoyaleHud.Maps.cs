using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    // The minimap with the match panel under it (players left, eliminations, haze timer) and the
    // full campus map (M).
    public partial class RoyaleHud
    {
        Vector2 MapUV(Vector3 world)
        {
            return new Vector2((world.x - mapWorld.x) / mapWorld.width, (world.z - mapWorld.y) / mapWorld.height);
        }

        float MinimapSize { get { return GameSettings.BigMinimap ? 300f : 232f; } }

        /// <summary>Top right: a north-up map that follows the player.</summary>
        void DrawMinimap(float w)
        {
            if (mapTexture == null) return;
            float size = MinimapSize;
            const float span = 260f;   // metres across
            float rx = w - M - size, ry = M;
            HudKit.Panel(rx - 4f, ry - 4f, size + 8f, size + 8f, HudKit.InkDeep, 8f);

            Vector3 p = player.transform.position;
            Vector2 uv = MapUV(p);
            float su = span / mapWorld.width, sv = span / mapWorld.height;
            float ppm = size / span;
            System.Func<Vector3, Vector2> toLocal = q => new Vector2(size / 2f + (q.x - p.x) * ppm, size / 2f - (q.z - p.z) * ppm);

            GUI.BeginClip(HudKit.R(rx, ry, size, size));
            if (HudKit.Repaint)
                GUI.DrawTextureWithTexCoords(new Rect(0, 0, size * HudKit.S, size * HudKit.S), mapTexture, new Rect(uv.x - su / 2f, uv.y - sv / 2f, su, sv));
            DrawWorldCircles(toLocal, ppm);
            foreach (var s in director.supplyDrops)
            {
                Vector2 d = toLocal(s);
                HudKit.IconAt("drop", d.x, d.y, 16f, new Color(1f, 0.32f, 0.22f));
            }
            DrawGettingAround(toLocal, false);
            if (hasWaypoint)
            {
                Vector2 wp = toLocal(waypoint);
                Vector2 c = new Vector2(size / 2f, size / 2f), dv = wp - c;
                float lim = size / 2f - 10f;
                if (Mathf.Abs(dv.x) > lim || Mathf.Abs(dv.y) > lim) dv *= lim / Mathf.Max(Mathf.Abs(dv.x), Mathf.Abs(dv.y));
                HudKit.IconAt("pin", c.x + dv.x, c.y + dv.y - 9f, 18f, HudKit.Marker);
            }
            // The place name over a dark fade along the bottom edge.
            HudKit.FadePanel(0f, size - 34f, size, 34f, new Color(0f, 0f, 0f, 0.7f), false);
            HudKit.Label(0f, size - 26f, size, 22f, NearestPlace().ToUpper(), 15f, HudKit.Text, TextAnchor.MiddleCenter, true);
            GUI.EndClip();

            PlayerArrow(rx + size / 2f, ry + size / 2f, 20f);
            HudKit.Outline(rx - 4f, ry - 4f, size + 8f, size + 8f, HudKit.Line, 1.5f, 8f);
            HudKit.Panel(rx + size / 2f - 11f, ry - 9f, 22f, 18f, HudKit.InkDeep, 4f);
            HudKit.Label(rx + size / 2f - 11f, ry - 9f, 22f, 18f, "N", 14f, HudKit.Accent, TextAnchor.MiddleCenter, true, false);
        }

        /// <summary>Under the minimap: players left and eliminations, then the haze round and its timer.</summary>
        void DrawMatchInfo(float w)
        {
            if (director == null) return;
            float size = MinimapSize;
            float x = w - M - size - 4f, y = M + size + 12f, bw = size + 8f;
            float half = (bw - 6f) / 2f;

            HudKit.Panel(x, y, half, 38f, HudKit.Ink, 6f);
            HudKit.IconAt("person", x + 20f, y + 19f, 20f, HudKit.TextDim);
            HudKit.Label(x + 36f, y, half - 40f, 38f, Combatant.AliveCount().ToString(), 24f, HudKit.Text, TextAnchor.MiddleLeft, true);
            HudKit.Label(x + 36f, y, half - 46f, 38f, "LEFT", 12f, HudKit.TextDim, TextAnchor.MiddleRight, true);
            float kx = x + half + 6f;
            HudKit.Panel(kx, y, half, 38f, HudKit.Ink, 6f);
            HudKit.IconAt("target", kx + 20f, y + 19f, 20f, HudKit.Accent);
            HudKit.Label(kx + 36f, y, half - 40f, 38f, player.Kills.ToString(), 24f, HudKit.Text, TextAnchor.MiddleLeft, true);
            HudKit.Label(kx + 36f, y, half - 46f, 38f, "ELIMS", 12f, HudKit.TextDim, TextAnchor.MiddleRight, true);

            if (haze == null || !haze.Running) return;
            y += 44f;
            int rounds = haze.rounds.Length;
            bool over = haze.RoundIndex >= rounds;
            string label = over ? "HAZE HAS CLOSED" : haze.Closing ? "HAZE CLOSING" : "HAZE MOVES IN";
            Color c = haze.Closing && !over ? HudKit.HazeColour : HudKit.Text;
            HudKit.Panel(x, y, bw, 54f, haze.Closing && !over ? new Color(0.26f, 0.12f, 0.03f, 0.75f) : HudKit.Ink, 6f);
            HudKit.IconAt("haze", x + 22f, y + 24f, 24f, c);
            HudKit.Label(x + 42f, y + 5f, 160f, 20f, label, 14f, c, TextAnchor.MiddleLeft, true);
            HudKit.Label(x + 42f, y + 24f, 160f, 18f, over ? "FINAL AREA" : "ROUND " + (haze.RoundIndex + 1) + " OF " + rounds, 13f, HudKit.TextDim, TextAnchor.MiddleLeft, true);
            if (!over)
            {
                HudKit.Label(x + bw - 110f, y + 2f, 100f, 42f, HudKit.Clock(haze.TimeLeft), 30f, HudKit.Text, TextAnchor.MiddleRight, true);
                var r = haze.rounds[haze.RoundIndex];
                float total = haze.Closing ? r.close : r.hold;
                float f = 1f - Mathf.Clamp01(haze.TimeLeft / Mathf.Max(1f, total));
                HudKit.Fill(x + 8f, y + 47f, bw - 16f, 3f, new Color(1f, 1f, 1f, 0.12f));
                HudKit.Fill(x + 8f, y + 47f, (bw - 16f) * f, 3f, haze.Closing ? HudKit.HazeColour : HudKit.Text);
            }
        }

        /// <summary>The whole campus (M): places with their loot, the haze, the helicopter's line, and a marker to click.</summary>
        void DrawFullMap(float w, float h)
        {
            if (mapTexture == null) return;
            HudKit.Fill(0, 0, w, h, new Color(0.02f, 0.025f, 0.03f, 0.88f));
            const float legendW = 300f, gap = 28f;
            float size = Mathf.Min(h - 120f, w - legendW - gap - 80f);
            float aspect = mapWorld.width / mapWorld.height;
            float mw = aspect >= 1f ? size : size * aspect;
            float mh = aspect >= 1f ? size / aspect : size;
            float mx = (w - (mw + gap + legendW)) / 2f, my = (h - mh) / 2f;
            HudKit.Panel(mx - 6f, my - 6f, mw + 12f, mh + 12f, HudKit.InkDeep, 8f);
            if (HudKit.Repaint) GUI.DrawTexture(HudKit.R(mx, my, mw, mh), mapTexture);
            System.Func<Vector3, Vector2> toScreen = q =>
            {
                Vector2 uv = MapUV(q);
                return new Vector2(mx + uv.x * mw, my + (1f - uv.y) * mh);
            };
            float scale = mw / mapWorld.width;
            string gridRef = MapGrid(mx, my, mw, mh, toScreen);

            if (places != null)
            {
                for (int i = 0; i < places.Length; i++)
                {
                    var pl = places[i];
                    Vector2 s = toScreen(new Vector3(pl.x, 0f, pl.z));
                    string name = CleanName(pl.name).ToUpper();
                    float tw = HudKit.TextWidth(name, 12f, true) + 12f;
                    HudKit.Panel(s.x - tw / 2f, s.y - 10f, tw, 17f, new Color(0f, 0f, 0f, 0.5f), 3f);
                    HudKit.Label(s.x - tw / 2f, s.y - 10f, tw, 17f, name, 12f, HudKit.Text, TextAnchor.MiddleCenter, true, false);
                    int rating = Mathf.Clamp(placeLoot != null && i < placeLoot.Length ? placeLoot[i] : 0, 0, 2);
                    for (int k = 0; k <= rating; k++)
                        HudKit.Dot(s.x - rating * 4f + k * 8f, s.y + 13f, 6f, rating == 2 ? HudKit.Accent : new Color(0.85f, 0.85f, 0.85f));
                }
            }

            // Click to set a marker, right-click to clear it.
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && new Rect(mx, my, mw, mh).Contains(HudKit.Mouse))
            {
                if (ev.button == 0)
                {
                    float u = (HudKit.Mouse.x - mx) / mw, v = 1f - (HudKit.Mouse.y - my) / mh;
                    float wx = mapWorld.x + u * mapWorld.width, wz = mapWorld.y + v * mapWorld.height;
                    waypoint = new Vector3(wx, HazeZone.GroundY(wx, wz), wz);
                    hasWaypoint = true;
                    SynthAudio.Play2D("equip", 0.4f);
                }
                else hasWaypoint = false;
                ev.Use();
            }

            // The helicopter's line while people are still aboard.
            if (director.helicopter != null && director.helicopter.Flying && diver != null && diver.Current == SkyDiver.State.InHelicopter)
            {
                var heli = director.helicopter;
                for (int i = 0; i <= 40; i++)
                {
                    Vector2 d = toScreen(Vector3.Lerp(heli.From, heli.To, i / 40f));
                    HudKit.Dot(d.x, d.y, 3.5f, new Color(1f, 1f, 1f, 0.7f));
                }
                Vector2 hp = toScreen(heli.transform.position);
                HudKit.Dot(hp.x, hp.y, 12f, HudKit.Accent);
            }
            DrawWorldCircles(toScreen, scale);
            foreach (var s in director.supplyDrops)
            {
                Vector2 d = toScreen(s);
                HudKit.IconAt("drop", d.x, d.y, 20f, new Color(1f, 0.32f, 0.22f));
            }
            DrawGettingAround(toScreen, true);
            if (hasWaypoint)
            {
                Vector2 wp = toScreen(waypoint);
                HudKit.IconAt("pin", wp.x, wp.y - 12f, 24f, HudKit.Marker);
            }
            Vector2 me = toScreen(player.transform.position);
            PlayerArrow(me.x, me.y, 22f);
            HudKit.Outline(mx - 6f, my - 6f, mw + 12f, mh + 12f, HudKit.Line, 1.5f, 8f);

            MapLegend(mx + mw + gap, my, legendW, mh, gridRef);
        }

        void MapLegend(float x, float y, float lw, float lh, string gridRef)
        {
            HudKit.Panel(x, y - 6f, lw, lh + 12f, HudKit.InkDeep, 8f);
            float px = x + 20f, pw = lw - 40f;
            float cy = y + 14f;
            HudKit.Label(px, cy, pw, 20f, "JAGTI CAMPUS", 14f, HudKit.Accent, TextAnchor.MiddleLeft, true);
            HudKit.Label(px, cy + 18f, pw, 44f, "MAP", 40f, HudKit.Text, TextAnchor.MiddleLeft, true);
            cy += 72f;
            HudKit.Label(px, cy, pw, 22f, "YOU ARE IN " + gridRef + ",  " + NearestPlace().ToUpper(), 15f, HudKit.Text, TextAnchor.MiddleLeft, true);
            cy += 26f;
            if (haze != null && haze.Running)
            {
                bool over = haze.RoundIndex >= haze.rounds.Length;
                string t = over ? "The haze has closed." : (haze.Closing ? "Haze closing: " : "Haze moves in ") + HudKit.Clock(haze.TimeLeft);
                HudKit.Label(px, cy, pw, 22f, t, 15f, haze.Closing ? HudKit.HazeColour : HudKit.TextDim);
                cy += 26f;
            }
            cy += 10f;
            HudKit.Fill(px, cy, pw, 1f, HudKit.Line);
            cy += 14f;

            LegendRow(px, ref cy, pw, "circle", Color.white, "Next safe area");
            LegendRow(px, ref cy, pw, "circle", HudKit.HazeColour, "Haze edge");
            LegendRow(px, ref cy, pw, "arrow", HudKit.Marker, "You");
            LegendRow(px, ref cy, pw, "pin", HudKit.Marker, "Your marker");
            LegendRow(px, ref cy, pw, "drop", new Color(1f, 0.32f, 0.22f), "Supply drop");
            LegendRow(px, ref cy, pw, "zip", new Color(1f, 0.6f, 0.15f), "Zip line");
            LegendRow(px, ref cy, pw, "car", new Color(0.55f, 0.85f, 1f), "Vehicle (near you)");
            LegendRow(px, ref cy, pw, "loot", HudKit.Accent, "Loot: one to three dots");

            float hy = y + lh - 104f;
            HudKit.Fill(px, hy - 14f, pw, 1f, HudKit.Line);
            KeyRow(px, hy, "LMB", "Set a marker");
            KeyRow(px, hy + 32f, "RMB", "Clear the marker");
            KeyRow(px, hy + 64f, "M", "Close the map");
        }

        static void KeyRow(float x, float y, string key, string text)
        {
            float kw = HudKit.KeyCap(x, y, key, 24f);
            HudKit.Label(x + Mathf.Max(kw, 44f) + 10f, y, 200f, 24f, text, 16f, HudKit.Text);
        }

        static void LegendRow(float x, ref float y, float w, string kind, Color c, string text)
        {
            float cx = x + 12f, cy = y + 12f;
            switch (kind)
            {
                case "circle": HudKit.Circle(cx, cy, 9f, c); break;
                case "zip":
                    for (int i = 0; i < 4; i++) HudKit.Dot(cx - 9f + i * 6f, cy, 3f, c);
                    break;
                case "car":
                    HudKit.Fill(cx - 5f, cy - 4f, 10f, 8f, new Color(0.1f, 0.1f, 0.1f));
                    HudKit.Fill(cx - 4f, cy - 3f, 8f, 6f, c);
                    break;
                case "loot":
                    for (int i = 0; i < 3; i++) HudKit.Dot(cx - 8f + i * 8f, cy, 6f, c);
                    break;
                default: HudKit.IconAt(kind, cx, cy, 20f, c); break;
            }
            HudKit.Label(x + 34f, y, w - 34f, 24f, text, 16f, HudKit.Text);
            y += 30f;
        }

        /// <summary>A 100 m grid with lettered columns and numbered rows; returns the player's square, e.g. "C4".</summary>
        string MapGrid(float mx, float my, float mw, float mh, System.Func<Vector3, Vector2> toScreen)
        {
            var line = new Color(1f, 1f, 1f, 0.1f);
            var tag = new Color(1f, 1f, 1f, 0.55f);
            Vector3 p = player.transform.position;
            string colName = "?", rowName = "?";
            float x0 = Mathf.Ceil(mapWorld.x / 100f) * 100f;
            int col = 0;
            for (float x = x0; x < mapWorld.xMax; x += 100f, col++)
            {
                float sx = toScreen(new Vector3(x, 0f, 0f)).x;
                HudKit.Fill(sx, my, 1f, mh, line);
                string name = col < 26 ? ((char)('A' + col)).ToString() : "";
                HudKit.Label(sx + 4f, my + 2f, 20f, 16f, name, 12f, tag, TextAnchor.MiddleLeft, true, false);
                if (p.x >= x && p.x < x + 100f) colName = name;
            }
            int row = 1;
            for (float z = Mathf.Floor(mapWorld.yMax / 100f) * 100f; z > mapWorld.y; z -= 100f, row++)
            {
                float sy = toScreen(new Vector3(0f, 0f, z)).y;
                HudKit.Fill(mx, sy, mw, 1f, line);
                HudKit.Label(mx + 4f, sy + 2f, 24f, 16f, row.ToString(), 12f, tag, TextAnchor.MiddleLeft, true, false);
                if (p.z <= z && p.z > z - 100f) rowName = row.ToString();
            }
            return colName + rowName;
        }

        /// <summary>Zip lines as dotted orange lines (towers as squares); vehicles nearby as small marks.</summary>
        void DrawGettingAround(System.Func<Vector3, Vector2> toScreen, bool full)
        {
            var zipCol = new Color(1f, 0.6f, 0.15f, 0.9f);
            foreach (var z in ZipLine.All)
            {
                Vector2 a = toScreen(z.a), b = toScreen(z.b);
                int n = Mathf.Clamp(Mathf.RoundToInt((b - a).magnitude / 6f), 3, 50);
                for (int i = 1; i < n; i++)
                {
                    Vector2 d = Vector2.Lerp(a, b, i / (float)n);
                    HudKit.Dot(d.x, d.y, 2.6f, zipCol);
                }
                HudKit.Fill(a.x - 3f, a.y - 3f, 6f, 6f, zipCol);
                HudKit.Fill(b.x - 3f, b.y - 3f, 6f, 6f, zipCol);
            }
            Vector3 me = player.transform.position;
            float range = full ? 300f : 150f;
            foreach (var v in Vehicle.All)
            {
                if (v.Wrecked || (mover != null && mover.Riding == v)) continue;
                if ((v.transform.position - me).sqrMagnitude > range * range) continue;
                Vector2 s = toScreen(v.transform.position);
                HudKit.Fill(s.x - 5f, s.y - 4f, 10f, 8f, new Color(0.08f, 0.08f, 0.08f, 0.9f));
                HudKit.Fill(s.x - 4f, s.y - 3f, 8f, 6f, new Color(0.55f, 0.85f, 1f));
            }
        }

        void DrawWorldCircles(System.Func<Vector3, Vector2> toScreen, float pixelsPerMetre)
        {
            if (haze == null) return;
            Vector2 c = toScreen(new Vector3(haze.Centre.x, 0f, haze.Centre.y));
            HudKit.Circle(c.x, c.y, haze.Radius * pixelsPerMetre, HudKit.WithAlpha(HudKit.HazeColour, 0.95f));
            if (haze.Running && haze.RoundIndex < haze.rounds.Length)
            {
                Vector2 n = toScreen(new Vector3(haze.NextCentre.x, 0f, haze.NextCentre.y));
                HudKit.Circle(n.x, n.y, haze.NextRadius * pixelsPerMetre, Color.white);
            }
        }

        void PlayerArrow(float x, float y, float size)
        {
            float yaw = fpc != null ? fpc.transform.eulerAngles.y : player.transform.eulerAngles.y;
            var m = HudKit.Rotate(yaw, x, y);
            HudKit.IconAt("arrow", x, y, size + 4f, new Color(0f, 0f, 0f, 0.6f));
            HudKit.IconAt("arrow", x, y, size, HudKit.Marker);
            GUI.matrix = m;
        }

        string NearestPlace()
        {
            if (places == null || places.Length == 0) return "";
            Vector3 p = player.transform.position;
            float best = float.MaxValue;
            string name = "";
            foreach (var pl in places)
            {
                float d = (pl.x - p.x) * (pl.x - p.x) + (pl.z - p.z) * (pl.z - p.z);
                if (d < best) { best = d; name = CleanName(pl.name); }
            }
            return name;
        }
    }
}
