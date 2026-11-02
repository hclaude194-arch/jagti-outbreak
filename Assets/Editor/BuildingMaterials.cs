using System.Collections.Generic;
using UnityEngine;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Paints the building textures in code and makes one URP material per slot name used by
    /// tools/blender/build_buildings.py (Facade_Academic, Brick, Glass, ...). Each facade texture
    /// is one window bay; the Blender UVs repeat it once per bay and once per floor.
    /// The colours are a best guess at the campus look (warm plaster, terracotta brick, white trim);
    /// change them here.
    /// </summary>
    public static class BuildingMaterials
    {
        static readonly Color Plaster = new Color(0.86f, 0.80f, 0.68f);
        static readonly Color PlasterHostel = new Color(0.88f, 0.83f, 0.72f);
        static readonly Color Terracotta = new Color(0.62f, 0.33f, 0.23f);
        static readonly Color Trim = new Color(0.90f, 0.89f, 0.86f);
        static readonly Color GlassLow = new Color(0.16f, 0.21f, 0.26f);
        static readonly Color GlassHigh = new Color(0.45f, 0.54f, 0.62f);

        const int Size = 512;

        public static Dictionary<string, Material> Create()
        {
            var mats = new Dictionary<string, Material>
            {
                ["Facade_Academic"] = ProceduralAssets.LitMaterial("Bld_Facade_Academic", Color.white, AcademicBay(), 0.15f),
                ["Facade_Hostel"] = ProceduralAssets.LitMaterial("Bld_Facade_Hostel", Color.white, HostelBay(), 0.12f),
                ["Facade_Ground"] = ProceduralAssets.LitMaterial("Bld_Facade_Ground", Color.white, GroundBay(), 0.35f),
                ["Brick"] = ProceduralAssets.LitMaterial("Bld_Brick", Color.white, Brick(), 0.08f),
                ["Plaster"] = ProceduralAssets.LitMaterial("Bld_Plaster", Color.white, PlainWall("Tex_Plaster", Plaster, 0.06f, 21), 0.08f),
                ["Concrete"] = ProceduralAssets.LitMaterial("Bld_Concrete", Color.white, PlainWall("Tex_Concrete", Trim, 0.05f, 22), 0.12f),
                ["Glass"] = ProceduralAssets.LitMaterial("Bld_Glass", Color.white, CurtainGlass(), 0.8f),
                ["Roof"] = ProceduralAssets.LitMaterial("Bld_Roof", Color.white, PlainWall("Tex_Roof", new Color(0.40f, 0.39f, 0.37f), 0.12f, 23), 0.05f),
                ["Metal"] = ProceduralAssets.LitMaterial("Bld_Metal", Color.white, Metal(), 0.4f),
                ["ClockFace"] = ProceduralAssets.LitMaterial("Bld_ClockFace", Color.white, ClockFace(), 0.3f),
            };
            return mats;
        }

        // ------------------------------------------------------------------ canvas helpers

        class Canvas
        {
            public readonly Color[] px = new Color[Size * Size];

            public void Fill(Color c, float grain, int seed)
            {
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float n = ProceduralAssets.Fbm(x / (float)Size, y / (float)Size, 4, 4, seed) - 0.5f;
                        float f = ProceduralAssets.Noise(x / 2f, y / 2f, Size / 2, seed + 3) - 0.5f;
                        px[y * Size + x] = c * (1f + n * grain * 2f + f * grain);
                    }
            }

            /// <summary>Rectangle in 0..1 texture space (y up), optionally a vertical gradient.</summary>
            public void Rect(float x0, float y0, float x1, float y1, Color bottom, Color? top = null)
            {
                int ix0 = Mathf.RoundToInt(x0 * Size), ix1 = Mathf.RoundToInt(x1 * Size);
                int iy0 = Mathf.RoundToInt(y0 * Size), iy1 = Mathf.RoundToInt(y1 * Size);
                for (int y = Mathf.Max(0, iy0); y < Mathf.Min(Size, iy1); y++)
                {
                    float t = iy1 > iy0 ? (y - iy0) / (float)(iy1 - iy0) : 0f;
                    Color c = top.HasValue ? Color.Lerp(bottom, top.Value, t) : bottom;
                    for (int x = Mathf.Max(0, ix0); x < Mathf.Min(Size, ix1); x++) px[y * Size + x] = c;
                }
            }

            /// <summary>Darkens an existing area (shadow under a ledge or inside a reveal).</summary>
            public void Shade(float x0, float y0, float x1, float y1, float amount)
            {
                for (int y = Mathf.Max(0, (int)(y0 * Size)); y < Mathf.Min(Size, (int)(y1 * Size)); y++)
                    for (int x = Mathf.Max(0, (int)(x0 * Size)); x < Mathf.Min(Size, (int)(x1 * Size)); x++)
                        px[y * Size + x] *= 1f - amount;
            }

            /// <summary>A window: shadowed reveal, frame, glass with a sky reflection, mullions.</summary>
            public void Window(float x0, float y0, float x1, float y1, int columns, float transom)
            {
                const float reveal = 0.025f, frame = 0.012f;
                Rect(x0 - reveal, y0 - reveal, x1 + reveal, y1 + reveal, Trim * 0.72f);
                Rect(x0, y0, x1, y1, Trim);
                Rect(x0 + frame, y0 + frame, x1 - frame, y1 - frame, GlassLow, GlassHigh);
                // Diagonal reflection streak.
                for (int y = (int)(y0 * Size); y < (int)(y1 * Size); y++)
                    for (int x = (int)(x0 * Size); x < (int)(x1 * Size); x++)
                    {
                        float d = (x - y * 0.6f) / Size;
                        float s = Mathf.Repeat(d * 3f, 1f);
                        if (s < 0.12f) px[y * Size + x] = Color.Lerp(px[y * Size + x], GlassHigh * 1.2f, 0.25f);
                    }
                for (int c = 1; c < columns; c++)
                {
                    float mx = Mathf.Lerp(x0, x1, c / (float)columns);
                    Rect(mx - frame * 0.6f, y0, mx + frame * 0.6f, y1, Trim);
                }
                if (transom > 0f)
                {
                    float ty = Mathf.Lerp(y0, y1, transom);
                    Rect(x0, ty - frame * 0.6f, x1, ty + frame * 0.6f, Trim);
                }
                Shade(x0 + frame, y1 - frame - 0.06f, x1 - frame, y1 - frame, 0.35f); // lintel shadow on the glass
            }

            public Texture2D Save(string name)
            {
                var c32 = new Color32[px.Length];
                for (int i = 0; i < px.Length; i++) c32[i] = px[i];
                return ProceduralAssets.SaveTexture(name, Size, c32, false);
            }
        }

        // ------------------------------------------------------------------ textures

        // One bay of the academic block: terracotta spandrel band, sill, tall window.
        static Texture2D AcademicBay()
        {
            var c = new Canvas();
            c.Fill(Plaster, 0.05f, 31);
            c.Rect(0f, 0f, 1f, 0.13f, Terracotta * 0.95f, Terracotta * 1.05f);
            c.Shade(0f, 0.13f, 1f, 0.16f, 0.12f);
            c.Window(0.16f, 0.24f, 0.84f, 0.90f, 2, 0.72f);
            c.Rect(0.12f, 0.19f, 0.88f, 0.22f, Trim);
            c.Shade(0.12f, 0.17f, 0.88f, 0.19f, 0.25f);
            return c.Save("Tex_Facade_Academic");
        }

        // One hostel room: smaller window, sill, a little grime below it.
        static Texture2D HostelBay()
        {
            var c = new Canvas();
            c.Fill(PlasterHostel, 0.06f, 32);
            c.Window(0.26f, 0.32f, 0.74f, 0.82f, 2, 0f);
            c.Rect(0.22f, 0.28f, 0.78f, 0.31f, Trim);
            c.Shade(0.24f, 0.12f, 0.76f, 0.28f, 0.06f);
            c.Rect(0f, 0f, 1f, 0.04f, Trim * 0.95f);
            return c.Save("Tex_Facade_Hostel");
        }

        // Ground floor: glazed front between plaster piers.
        static Texture2D GroundBay()
        {
            var c = new Canvas();
            c.Fill(Plaster * 0.95f, 0.05f, 33);
            c.Window(0.08f, 0.10f, 0.92f, 0.86f, 3, 0.78f);
            c.Rect(0f, 0f, 1f, 0.06f, Trim * 0.8f);
            return c.Save("Tex_Facade_Ground");
        }

        // Curtain wall pane, 1.5 m x one floor.
        static Texture2D CurtainGlass()
        {
            var c = new Canvas();
            c.Rect(0f, 0f, 1f, 1f, GlassLow, GlassHigh);
            c.Window(0.03f, 0.03f, 0.97f, 0.97f, 1, 0.8f);
            c.Rect(0f, 0f, 1f, 0.03f, new Color(0.2f, 0.21f, 0.22f));
            c.Rect(0f, 0.97f, 1f, 1f, new Color(0.2f, 0.21f, 0.22f));
            c.Rect(0f, 0f, 0.03f, 1f, new Color(0.2f, 0.21f, 0.22f));
            c.Rect(0.97f, 0f, 1f, 1f, new Color(0.2f, 0.21f, 0.22f));
            return c.Save("Tex_Glass");
        }

        // Terracotta brick, running bond: 8 bricks across and 24 courses per 2 m tile.
        static Texture2D Brick()
        {
            var c = new Canvas();
            var mortar = new Color(0.72f, 0.68f, 0.62f);
            c.Fill(mortar, 0.08f, 34);
            const int across = 8, courses = 24;
            float bw = 1f / across, bh = 1f / courses, gap = 0.012f;
            for (int row = 0; row < courses; row++)
            {
                float offset = (row % 2) * bw * 0.5f;
                for (int col = -1; col < across; col++)
                {
                    float shade = 0.85f + 0.3f * ProceduralAssets.Noise(col * 7.3f + 0.5f, row * 3.1f + 0.5f, 64, 35);
                    var brick = Terracotta * shade;
                    brick.a = 1f;
                    float x0 = col * bw + offset + gap * 0.5f, x1 = (col + 1) * bw + offset - gap * 0.5f;
                    float y0 = row * bh + gap * 0.5f, y1 = (row + 1) * bh - gap * 0.5f;
                    c.Rect(Mathf.Max(0f, x0), y0, Mathf.Min(1f, x1), y1, brick * 0.97f, brick * 1.03f);
                    if (x1 > 1f) c.Rect(0f, y0, x1 - 1f, y1, brick * 0.97f, brick * 1.03f); // wrap so the tile repeats
                }
            }
            // Fine grain over everything.
            for (int i = 0; i < c.px.Length; i++)
            {
                float f = ProceduralAssets.Noise(i % Size, i / Size, Size, 36) - 0.5f;
                c.px[i] *= 1f + f * 0.12f;
            }
            return c.Save("Tex_Brick");
        }

        static Texture2D PlainWall(string name, Color color, float grain, int seed)
        {
            var c = new Canvas();
            c.Fill(color, grain, seed);
            return c.Save(name);
        }

        // Corrugated dark metal for tanks and the clock tower cap.
        static Texture2D Metal()
        {
            var c = new Canvas();
            c.Fill(new Color(0.24f, 0.25f, 0.26f), 0.05f, 37);
            for (int x = 0; x < Size; x++)
            {
                float rib = 0.9f + 0.1f * Mathf.Sin(x / (float)Size * Mathf.PI * 2f * 16f);
                for (int y = 0; y < Size; y++) c.px[y * Size + x] *= rib;
            }
            return c.Save("Tex_Metal");
        }

        static Texture2D ClockFace()
        {
            var c = new Canvas();
            c.Fill(new Color(0.80f, 0.76f, 0.66f), 0.03f, 38);
            var dial = new Color(0.95f, 0.94f, 0.90f);
            var ink = new Color(0.10f, 0.10f, 0.11f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = x / (float)Size - 0.5f, dy = y / (float)Size - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r < 0.47f) c.px[y * Size + x] = dial;
                    if (r >= 0.43f && r < 0.47f) c.px[y * Size + x] = ink;
                    if (r < 0.43f && r > 0.33f)
                    {
                        // Twelve hour marks.
                        float ang = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) * 12f;
                        if (Mathf.Abs(ang - Mathf.Round(ang)) < 0.06f && r > 0.36f) c.px[y * Size + x] = ink;
                    }
                }
            Hand(c, 0.5f, 0.5f, 60f, 0.26f, 0.018f, ink);   // hour hand at ten
            Hand(c, 0.5f, 0.5f, -60f, 0.38f, 0.012f, ink);  // minute hand at two
            return c.Save("Tex_ClockFace");
        }

        static void Hand(Canvas c, float cx, float cy, float degreesFromUp, float length, float width, Color color)
        {
            float a = degreesFromUp * Mathf.Deg2Rad;
            var dir = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2(x / (float)Size - cx, y / (float)Size - cy);
                    float along = Vector2.Dot(p, dir);
                    float across = Mathf.Abs(p.x * dir.y - p.y * dir.x);
                    if (along > -0.03f && along < length && across < width) c.px[y * Size + x] = color;
                }
        }
    }
}
