using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The shared look of the royale HUD and menus (IMGUI, no Canvas or image files).
    /// Layout is written in reference units of a 1080-pixel-high screen: Begin() sets the scale from
    /// the window height and the HUD scale setting, W and H give the screen in those units, and text
    /// is drawn at its real pixel size so it stays sharp. Holds the palette, the two font weights
    /// (Barlow Condensed, SIL Open Font License, in Resources/Fonts), generated textures, cached
    /// styles and the small widgets: panels, bars, rings, key caps, buttons, sliders, switches.
    /// Drawing calls do nothing outside the repaint event; the widgets also answer the mouse.
    /// tools/royale/preview_hud.py mirrors these helpers to draw the HUD mockups.
    /// </summary>
    public static class HudKit
    {
        // ------------------------------------------------------------------ palette

        public static readonly Color Ink = new Color(0.05f, 0.06f, 0.08f, 0.62f);
        public static readonly Color InkDeep = new Color(0.035f, 0.04f, 0.05f, 0.88f);
        public static readonly Color InkLight = new Color(0.16f, 0.18f, 0.21f, 0.72f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.14f);
        public static readonly Color Text = new Color(0.97f, 0.96f, 0.93f);
        public static readonly Color TextDim = new Color(0.68f, 0.71f, 0.75f);
        public static readonly Color Accent = new Color(1f, 0.66f, 0.18f);         // saffron
        public static readonly Color AccentDeep = new Color(0.86f, 0.42f, 0.08f);
        public static readonly Color Health = new Color(0.93f, 0.96f, 0.92f);
        public static readonly Color HealthLow = new Color(1f, 0.3f, 0.24f);
        public static readonly Color Shield = new Color(0.33f, 0.72f, 1f);
        public static readonly Color Boost = new Color(1f, 0.8f, 0.38f);
        public static readonly Color Danger = new Color(1f, 0.3f, 0.22f);
        public static readonly Color HazeColour = new Color(0.93f, 0.56f, 0.22f);
        public static readonly Color Marker = new Color(1f, 0.85f, 0.15f);

        /// <summary>Armour and pack levels 1 to 3 use the uncommon, rare and epic colours.</summary>
        public static Color LevelColour(int level)
        {
            return level <= 0 ? TextDim : RoyaleItems.TierColours[Mathf.Clamp(level, 1, 3)];
        }

        public static Color WithAlpha(Color c, float a) { return new Color(c.r, c.g, c.b, c.a * a); }

        // ------------------------------------------------------------------ scale

        public static float S = 1f;                     // pixels per reference unit
        public static float W = 1920f, H = 1080f;       // the screen in reference units
        /// <summary>Fade applied to everything drawn (set it, draw, set it back to 1).</summary>
        public static float Alpha = 1f;

        static float builtScale = -1f;

        /// <summary>Call at the top of OnGUI. userScale is the HUD scale setting (1 = normal).</summary>
        public static void Begin(float userScale)
        {
            Init();
            float s = Mathf.Max(0.5f, Screen.height / 1080f * Mathf.Clamp(userScale, 0.6f, 1.6f));
            if (Mathf.Abs(s - builtScale) > 0.0005f)
            {
                styles.Clear();
                field = null;
                builtScale = s;
            }
            S = s;
            W = Screen.width / s;
            H = Screen.height / s;
            Alpha = 1f;
            GUI.color = Color.white;
        }

        public static Rect R(float x, float y, float w, float h) { return new Rect(x * S, y * S, w * S, h * S); }

        public static bool Repaint { get { return Event.current.type == EventType.Repaint; } }

        /// <summary>The mouse in reference units.</summary>
        public static Vector2 Mouse { get { return Event.current.mousePosition / S; } }

        // ------------------------------------------------------------------ textures and fonts

        static bool ready;
        static Font medium, bold;
        public static Texture2D White, Disc, Glow, Vignette, FadeDown, FadeUp, FadeSide, FadeLeft, Arc, Scope;

        static void Init()
        {
            if (ready) return;
            ready = true;
            medium = Resources.Load<Font>("Fonts/BarlowCondensed-500");
            bold = Resources.Load<Font>("Fonts/BarlowCondensed-700");
            if (bold == null) bold = medium;
            White = Solid();
            Disc = Radial(64, false);
            Glow = Radial(64, true);
            Vignette = MakeVignette(128);
            FadeDown = Ramp(64, true, false);
            FadeUp = Ramp(64, true, true);
            FadeSide = Ramp(64, false, false);
            FadeLeft = Ramp(64, false, true);
            Arc = MakeArc(128, 48);
            Scope = ScopeMask(256);
        }

        /// <summary>Black outside a circle, darkening a little towards its rim.</summary>
        static Texture2D ScopeMask(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            float r = size / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude / r;
                    float a = Mathf.Max(Mathf.Clamp01((d - 0.93f) * 20f), Mathf.Clamp01((d - 0.7f) * 1.2f) * 0.5f);
                    px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Texture2D Solid()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>A disc with a one-pixel soft edge, or (glow) a soft blob that fades to nothing.</summary>
        static Texture2D Radial(int size, bool glow)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
            t.wrapMode = TextureWrapMode.Clamp;
            float r = size / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude / r;
                    float a = glow ? Mathf.Pow(Mathf.Clamp01(1f - d), 2f) : Mathf.Clamp01((1f - d) * r);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>Clear in the middle, dark towards the edges and corners.</summary>
        static Texture2D MakeVignette(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(u * u * 0.8f + v * v);
                    float a = Mathf.Pow(Mathf.Clamp01((d - 0.5f) / 0.75f), 1.6f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>Opaque at the top (or left) fading to clear; flip turns it round.</summary>
        static Texture2D Ramp(int n, bool vertical, bool flip)
        {
            int w = vertical ? 1 : n, h = vertical ? n : 1;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                float f = (i + 0.5f) / n;                  // texture rows run up
                float a = vertical ? f : 1f - f;
                if (flip) a = 1f - a;
                px[i] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>A short curved band (part of a ring centred below the texture) for hit directions.</summary>
        static Texture2D MakeArc(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            float cx = w / 2f, cy = -150f, radius = 180f, half = 7f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float band = Mathf.Clamp01(half - Mathf.Abs(d - radius));
                    float ang = Mathf.Abs(Mathf.Atan2(dx, dy)) * Mathf.Rad2Deg;
                    float ends = Mathf.Clamp01((19f - ang) / 6f);
                    float glow = Mathf.Clamp01(1f - Mathf.Abs(d - radius) / 20f) * 0.35f;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Max(band, glow) * ends * 255f));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ text

        static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        static readonly GUIContent content = new GUIContent();

        static GUIStyle Style(float size, bool heavy, TextAnchor anchor)
        {
            int px = Mathf.Max(7, Mathf.RoundToInt(size * S));
            int key = px * 64 + (heavy ? 32 : 0) + (int)anchor;
            GUIStyle st;
            if (styles.TryGetValue(key, out st)) return st;
            st = new GUIStyle
            {
                font = heavy ? bold : medium,
                fontSize = px,
                alignment = anchor,
                clipping = TextClipping.Overflow,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(0, 0, 0, 0),
            };
            if (st.font == null) st.fontStyle = heavy ? FontStyle.Bold : FontStyle.Normal;
            styles[key] = st;
            return st;
        }

        /// <summary>Text in a box, with a soft drop shadow so it reads over the world.</summary>
        public static void Label(float x, float y, float w, float h, string text, float size, Color c,
                                 TextAnchor anchor = TextAnchor.MiddleLeft, bool heavy = false, bool shadow = true)
        {
            if (!Repaint || string.IsNullOrEmpty(text)) return;
            var st = Style(size, heavy, anchor);
            Rect r = R(x, y, w, h);
            float a = c.a * Alpha;
            if (shadow)
            {
                float o = Mathf.Max(1f, S * 1.2f);
                st.normal.textColor = new Color(0f, 0f, 0f, 0.55f * a);
                GUI.Label(new Rect(r.x + o * 0.6f, r.y + o, r.width, r.height), text, st);
            }
            st.normal.textColor = new Color(c.r, c.g, c.b, a);
            GUI.Label(r, text, st);
        }

        /// <summary>Wrapped text in a box (no shadow), for notes in menus.</summary>
        public static void Paragraph(float x, float y, float w, float h, string text, float size, Color c)
        {
            if (!Repaint || string.IsNullOrEmpty(text)) return;
            var st = Style(size, false, TextAnchor.UpperLeft);
            st.wordWrap = true;
            st.normal.textColor = new Color(c.r, c.g, c.b, c.a * Alpha);
            GUI.Label(R(x, y, w, h), text, st);
            st.wordWrap = false;
        }

        /// <summary>Width of a line of text in reference units.</summary>
        public static float TextWidth(string text, float size, bool heavy = false)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            content.text = text;
            return Style(size, heavy, TextAnchor.MiddleLeft).CalcSize(content).x / S;
        }

        // ------------------------------------------------------------------ shapes

        static Color Tint(Color c) { return new Color(c.r, c.g, c.b, c.a * Alpha); }

        public static void Fill(float x, float y, float w, float h, Color c)
        {
            if (!Repaint || w <= 0f || h <= 0f) return;
            GUI.DrawTexture(R(x, y, w, h), White, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
        }

        /// <summary>A rounded panel.</summary>
        public static void Panel(float x, float y, float w, float h, Color c, float radius = 6f)
        {
            if (!Repaint || w <= 0f || h <= 0f) return;
            float rad = Mathf.Min(radius, Mathf.Min(w, h) * 0.5f) * S;
            GUI.DrawTexture(R(x, y, w, h), White, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, rad);
        }

        /// <summary>A rounded outline.</summary>
        public static void Outline(float x, float y, float w, float h, Color c, float thickness = 1.5f, float radius = 6f)
        {
            if (!Repaint || w <= 0f || h <= 0f) return;
            float rad = Mathf.Min(radius, Mathf.Min(w, h) * 0.5f) * S;
            GUI.DrawTexture(R(x, y, w, h), White, ScaleMode.StretchToFill, true, 0f, Tint(c), Mathf.Max(1f, thickness * S), rad);
        }

        /// <summary>A panel that fades out downwards (up = false fades upwards).</summary>
        public static void FadePanel(float x, float y, float w, float h, Color c, bool down = true)
        {
            if (!Repaint) return;
            GUI.DrawTexture(R(x, y, w, h), down ? FadeDown : FadeUp, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
        }

        /// <summary>A panel that fades out to the right (left = true fades to the left).</summary>
        public static void FadeSidePanel(float x, float y, float w, float h, Color c, bool left = false)
        {
            if (!Repaint) return;
            GUI.DrawTexture(R(x, y, w, h), left ? FadeLeft : FadeSide, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
        }

        public static void Dot(float cx, float cy, float size, Color c)
        {
            if (!Repaint) return;
            GUI.DrawTexture(R(cx - size / 2f, cy - size / 2f, size, size), Disc, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
        }

        public static void GlowAt(float cx, float cy, float size, Color c)
        {
            if (!Repaint) return;
            GUI.DrawTexture(R(cx - size / 2f, cy - size / 2f, size, size), Glow, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
        }

        public static void Icon(string name, float x, float y, float h, Color c)
        {
            if (!Repaint) return;
            var t = HudIcons.Get(name);
            if (t == null) return;
            float w = h * t.width / t.height;
            GUI.DrawTexture(R(x, y, w, h), t, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
        }

        /// <summary>An icon centred on a point.</summary>
        public static void IconAt(string name, float cx, float cy, float h, Color c)
        {
            Icon(name, cx - h * HudIcons.Aspect(name) / 2f, cy - h / 2f, h, c);
        }

        /// <summary>The whole screen darkened (or tinted) towards the edges.</summary>
        public static void EdgeVignette(Color c)
        {
            if (!Repaint || c.a <= 0.003f) return;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Vignette, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
        }

        /// <summary>A bar: dark track, fill, optional cuts every 1/segments, optional bright tip.</summary>
        public static void Bar(float x, float y, float w, float h, float frac, Color fill, int segments = 0, float radius = 2f)
        {
            Panel(x, y, w, h, new Color(0f, 0f, 0f, 0.5f), radius);
            frac = Mathf.Clamp01(frac);
            if (frac > 0.001f) Panel(x, y, Mathf.Max(h * 0.6f, w * frac), h, fill, radius);
            Segments(x, y, w, h, segments);
        }

        public static void Segments(float x, float y, float w, float h, int segments)
        {
            for (int i = 1; i < segments; i++)
                Fill(x + w * i / segments - 1f, y, 2f, h, new Color(0.02f, 0.03f, 0.04f, 0.8f));
        }

        /// <summary>
        /// Part of a ring of dots: from (degrees clockwise from straight up) over frac of a full turn.
        /// </summary>
        public static void Ring(float cx, float cy, float radius, float thickness, float from, float frac, Color c)
        {
            if (!Repaint || frac <= 0f) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius * frac / (thickness * 0.45f)), 2, 140);
            for (int i = 0; i <= n; i++)
            {
                float a = (from + 360f * frac * i / n) * Mathf.Deg2Rad;
                Dot(cx + Mathf.Sin(a) * radius, cy - Mathf.Cos(a) * radius, thickness, c);
            }
        }

        static Texture2D[] rings;

        /// <summary>
        /// A thin circle in one draw call: a ring texture picked by the drawn size so the line stays
        /// about two to three pixels wide.
        /// </summary>
        public static void Circle(float cx, float cy, float radius, Color c)
        {
            if (!Repaint || radius <= 0.5f) return;
            if (rings == null)
            {
                rings = new Texture2D[4];
                for (int i = 0; i < 4; i++) rings[i] = MakeRing(64 << i, 2.6f);
            }
            float d = radius * 2f * S;
            int k = d < 90f ? 0 : d < 180f ? 1 : d < 360f ? 2 : 3;
            GUI.DrawTexture(R(cx - radius, cy - radius, radius * 2f, radius * 2f), rings[k], ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
        }

        static Texture2D MakeRing(int size, float line)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            float r = size / 2f, mid = r - line;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude;
                    float a = Mathf.Clamp01(line / 2f + 0.5f - Mathf.Abs(d - mid));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>Rotates what is drawn next about a point (reference units); returns the old matrix.</summary>
        public static Matrix4x4 Rotate(float degrees, float cx, float cy)
        {
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(degrees, new Vector2(cx * S, cy * S));
            return m;
        }

        /// <summary>The hit-direction band, drawn at a distance from a centre and turned towards a bearing.</summary>
        public static void HitArc(float cx, float cy, float distance, float degrees, Color c)
        {
            if (!Repaint) return;
            var m = Rotate(degrees, cx, cy);
            float w = 150f, h = 56f;
            GUI.DrawTexture(R(cx - w / 2f, cy - distance - h * 0.6f, w, h), Arc, ScaleMode.StretchToFill, true, 0f, Tint(c), 0f, 0f);
            GUI.matrix = m;
        }

        /// <summary>A keyboard key: a small outlined box with its letter. Returns its width.</summary>
        public static float KeyCap(float x, float y, string key, float h = 24f)
        {
            float size = h * 0.68f;
            float w = Mathf.Max(h, TextWidth(key, size, true) + h * 0.55f);
            Panel(x, y, w, h, new Color(0.95f, 0.95f, 0.92f, 0.92f), 4f);
            Panel(x, y + h - 3f, w, 3f, new Color(0f, 0f, 0f, 0.18f), 2f);
            Label(x, y - 1f, w, h, key, size, new Color(0.08f, 0.09f, 0.11f), TextAnchor.MiddleCenter, true, false);
            return w;
        }

        /// <summary>A row of key caps and words, e.g. ("SPACE", "Jump", "M", "Map"), centred on cx.</summary>
        public static void Hints(float cx, float y, float h, params string[] pairs)
        {
            float size = h * 0.72f, total = 0f;
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                total += Mathf.Max(h, TextWidth(pairs[i], h * 0.68f, true) + h * 0.55f) + 8f + TextWidth(pairs[i + 1], size) + 26f;
            float x = cx - (total - 26f) / 2f;
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                x += KeyCap(x, y, pairs[i], h) + 8f;
                Label(x, y, 400f, h, pairs[i + 1], size, Text);
                x += TextWidth(pairs[i + 1], size) + 26f;
            }
        }

        // ------------------------------------------------------------------ menu widgets

        static int dragging;

        public enum ButtonKind { Normal, Primary, Tab, TabOn, Quiet }

        /// <summary>A button drawn in the HUD style. Returns true on the click.</summary>
        public static bool Button(float x, float y, float w, float h, string text, ButtonKind kind = ButtonKind.Normal, bool enabled = true, string sub = null)
        {
            var ev = Event.current;
            bool hover = enabled && new Rect(x, y, w, h).Contains(Mouse);
            if (Repaint)
            {
                float a = enabled ? 1f : 0.45f;
                Color back, edge, text1;
                switch (kind)
                {
                    case ButtonKind.Primary:
                        back = hover ? new Color(1f, 0.74f, 0.3f) : Accent;
                        edge = new Color(1f, 0.9f, 0.6f, hover ? 0.9f : 0f);
                        text1 = new Color(0.09f, 0.07f, 0.04f);
                        break;
                    case ButtonKind.TabOn:
                        back = new Color(1f, 1f, 1f, 0.12f);
                        edge = new Color(0, 0, 0, 0);
                        text1 = Text;
                        break;
                    case ButtonKind.Tab:
                    case ButtonKind.Quiet:
                        back = hover ? new Color(1f, 1f, 1f, 0.08f) : new Color(0, 0, 0, 0);
                        edge = new Color(0, 0, 0, 0);
                        text1 = hover ? Text : TextDim;
                        break;
                    default:
                        back = hover ? new Color(0.24f, 0.27f, 0.31f, 0.95f) : new Color(0.13f, 0.15f, 0.18f, 0.9f);
                        edge = hover ? WithAlpha(Accent, 0.9f) : Line;
                        text1 = Text;
                        break;
                }
                Alpha *= a;
                Panel(x, y, w, h, back, 5f);
                if (edge.a > 0f) Outline(x, y, w, h, edge, 1.5f, 5f);
                if (kind == ButtonKind.TabOn) Panel(x, y + 6f, 4f, h - 12f, Accent, 2f);
                bool left = kind == ButtonKind.Tab || kind == ButtonKind.TabOn;
                float size = Mathf.Min(h * 0.46f, 24f);
                if (kind == ButtonKind.Primary) size = Mathf.Min(h * 0.5f, 34f);
                if (string.IsNullOrEmpty(sub))
                    Label(x + (left ? 20f : 0f), y, w - (left ? 20f : 0f), h, text, size, text1, left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, true, false);
                else
                {
                    Label(x, y + h * 0.08f, w, h * 0.6f, text, size, text1, TextAnchor.MiddleCenter, true, false);
                    Label(x, y + h * 0.58f, w, h * 0.3f, sub, size * 0.5f, WithAlpha(text1, 0.75f), TextAnchor.MiddleCenter, false, false);
                }
                Alpha /= a;
            }
            if (enabled && ev.type == EventType.MouseDown && ev.button == 0 && hover)
            {
                ev.Use();
                SynthAudio.Play2D("equip", 0.35f);
                return true;
            }
            return false;
        }

        /// <summary>A labelled slider; id must be unique among the sliders on screen.</summary>
        public static float Slider(int id, float x, float y, float w, string label, float value, float min, float max, string shown)
        {
            const float h = 40f;
            float tx = x + w * 0.42f, tw = w * 0.42f, ty = y + h / 2f;
            Label(x, y, w * 0.4f, h, label, 19f, Text);
            float f = Mathf.InverseLerp(min, max, value);
            Panel(tx, ty - 3f, tw, 6f, new Color(1f, 1f, 1f, 0.15f), 3f);
            Panel(tx, ty - 3f, tw * f, 6f, Accent, 3f);
            Dot(tx + tw * f, ty, dragging == id ? 22f : 18f, Text);
            Label(x + w * 0.86f, y, w * 0.14f, h, shown, 19f, Accent, TextAnchor.MiddleRight, true);

            var ev = Event.current;
            Rect hit = new Rect(tx - 10f, y, tw + 20f, h);
            if (ev.type == EventType.MouseDown && ev.button == 0 && hit.Contains(Mouse)) dragging = id;
            if (dragging == id)
            {
                if (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag)
                {
                    value = Mathf.Lerp(min, max, Mathf.Clamp01((Mouse.x - tx) / tw));
                    ev.Use();
                }
                else if (ev.type == EventType.MouseUp || ev.rawType == EventType.MouseUp)
                {
                    dragging = 0;
                    ev.Use();
                }
            }
            return value;
        }

        /// <summary>A labelled on/off switch.</summary>
        public static bool Switch(float x, float y, float w, string label, bool on)
        {
            const float h = 40f;
            Label(x, y, w * 0.6f, h, label, 19f, Text);
            float sx = x + w - 52f, sy = y + h / 2f - 12f;
            Panel(sx, sy, 52f, 24f, on ? Accent : new Color(1f, 1f, 1f, 0.15f), 12f);
            Dot(on ? sx + 40f : sx + 12f, sy + 12f, 18f, on ? new Color(0.1f, 0.08f, 0.05f) : Text);
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && new Rect(x, y, w, h).Contains(Mouse))
            {
                ev.Use();
                SynthAudio.Play2D("equip", 0.3f);
                return !on;
            }
            return on;
        }

        /// <summary>A labelled row of choices; returns the chosen index.</summary>
        public static int Choice(float x, float y, float w, string label, string[] options, int index, bool enabled = true)
        {
            const float h = 40f;
            Label(x, y, w * 0.4f, h, label, 19f, Text);
            float cx = x + w * 0.42f, cw = w * 0.58f, gap = 4f;
            float bw = (cw - gap * (options.Length - 1)) / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                if (ChoiceButton(cx + i * (bw + gap), y + 4f, bw, h - 8f, options[i], i == index, enabled)) index = i;
            }
            return index;
        }

        static bool ChoiceButton(float x, float y, float w, float h, string text, bool on, bool enabled)
        {
            var ev = Event.current;
            bool hover = enabled && new Rect(x, y, w, h).Contains(Mouse);
            float a = enabled ? 1f : 0.45f;
            Alpha *= a;
            Panel(x, y, w, h, on ? Accent : hover ? new Color(1f, 1f, 1f, 0.16f) : new Color(1f, 1f, 1f, 0.07f), 4f);
            Label(x, y, w, h, text, 16f, on ? new Color(0.09f, 0.07f, 0.04f) : Text, TextAnchor.MiddleCenter, true, false);
            Alpha /= a;
            if (hover && ev.type == EventType.MouseDown && ev.button == 0)
            {
                ev.Use();
                SynthAudio.Play2D("equip", 0.3f);
                return true;
            }
            return false;
        }

        static GUIStyle field;
        static Texture2D fieldTex, fieldFocusTex;

        /// <summary>A text box in the HUD style.</summary>
        public static string TextField(float x, float y, float w, float h, string value, int max)
        {
            if (field == null)
            {
                if (fieldTex == null)
                {
                    fieldTex = FieldTexture(new Color(1f, 1f, 1f, 0.18f));
                    fieldFocusTex = FieldTexture(Accent);
                }
                field = new GUIStyle
                {
                    font = medium,
                    fontSize = Mathf.RoundToInt(19f * S),
                    alignment = TextAnchor.MiddleLeft,
                    border = new RectOffset(6, 6, 6, 6),
                    padding = new RectOffset(Mathf.RoundToInt(10f * S), Mathf.RoundToInt(10f * S), 0, 0),
                    clipping = TextClipping.Clip,
                };
                field.normal.background = fieldTex;
                field.hover.background = fieldTex;
                field.focused.background = fieldFocusTex;
                field.active.background = fieldFocusTex;
                field.normal.textColor = field.hover.textColor = field.focused.textColor = field.active.textColor = Text;
            }
            GUI.skin.settings.cursorColor = Accent;
            GUI.skin.settings.selectionColor = WithAlpha(Accent, 0.4f);
            return GUI.TextField(R(x, y, w, h), value ?? "", max, field);
        }

        /// <summary>A dark rounded box with a one-pixel edge, made to be nine-sliced.</summary>
        static Texture2D FieldTexture(Color edge)
        {
            const int n = 16;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n];
            var fill = new Color(0.06f, 0.07f, 0.09f, 0.92f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - n / 2f) - (n / 2f - 4f));
                    float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - n / 2f) - (n / 2f - 4f));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);          // 0 inside, up to 4 at the corner
                    float inside = Mathf.Clamp01(4f - d);
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 3.2f)) + (x == 0 || y == 0 || x == n - 1 || y == n - 1 ? 1f : 0f);
                    Color c = Color.Lerp(fill, edge, Mathf.Clamp01(rim) * edge.a);
                    c.a = inside * Mathf.Max(fill.a, Mathf.Clamp01(rim) * edge.a);
                    px[y * n + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ text helpers

        public static string Clock(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int s = Mathf.CeilToInt(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
