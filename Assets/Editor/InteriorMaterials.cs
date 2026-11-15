using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Procedural materials for the interior slots used by tools/blender/build_interiors.py
    /// (floors, walls, ceilings and furniture). Tile sizes must match MATERIALS in
    /// tools/blender/build_buildings.py.
    /// </summary>
    public static class InteriorMaterials
    {
        const int Size = 512;

        public static void AddTo(Dictionary<string, Material> mats)
        {
            mats["Floor_Tile"] = Lit("Int_Floor_Tile", FloorTile(), 0.45f);
            mats["Floor_Wood"] = Lit("Int_Floor_Wood", Planks(), 0.35f);
            mats["Carpet"] = Lit("Int_Carpet", Weave("Tex_Carpet", new Color(0.27f, 0.30f, 0.39f), 0.10f, 81, 128), 0.02f);
            mats["Wall_Paint"] = Lit("Int_Wall_Paint", WallPaint(), 0.08f);
            mats["Ceiling"] = Lit("Int_Ceiling", CeilingTiles(), 0.05f);
            mats["Wood"] = Lit("Int_Wood", Grain("Tex_Wood", new Color(0.50f, 0.33f, 0.19f), 82, true), 0.3f);
            mats["Laminate"] = Lit("Int_Laminate", Grain("Tex_Laminate", new Color(0.82f, 0.72f, 0.58f), 83, false), 0.35f);
            mats["Fabric"] = Lit("Int_Fabric", Weave("Tex_Fabric", new Color(0.15f, 0.22f, 0.40f), 0.18f, 84, 256), 0.05f);
            mats["Velvet"] = Lit("Int_Velvet", Weave("Tex_Velvet", new Color(0.42f, 0.07f, 0.09f), 0.12f, 95, 256), 0.12f);
            mats["Leather"] = Lit("Int_Leather", Noise("Tex_Leather", new Color(0.33f, 0.19f, 0.11f), 0.12f, 85, 12), 0.45f);
            mats["Plastic"] = Lit("Int_Plastic", Noise("Tex_Plastic", new Color(0.13f, 0.13f, 0.14f), 0.04f, 86, 8), 0.35f);
            mats["Steel"] = Lit("Int_Steel", Brushed(), 0.6f, 0.6f);
            mats["Whiteboard"] = Lit("Int_Whiteboard", Noise("Tex_Whiteboard", new Color(0.95f, 0.95f, 0.95f), 0.015f, 87, 4), 0.85f);
            mats["Screen"] = Lit("Int_Screen", Noise("Tex_Screen", new Color(0.03f, 0.035f, 0.05f), 0.05f, 88, 4), 0.9f);
            mats["Ceramic"] = Lit("Int_Ceramic", Noise("Tex_Ceramic", new Color(0.94f, 0.94f, 0.93f), 0.02f, 89, 4), 0.8f);
            mats["LightPanel"] = Emissive("Int_LightPanel", new Color(1f, 0.97f, 0.9f), 1.6f);
            mats["Books"] = Lit("Int_Books", Books(), 0.15f);
            mats["Notice"] = Lit("Int_Notice", Notice(), 0.05f);
            mats["Foliage"] = Lit("Int_Foliage", Noise("Tex_Foliage", new Color(0.20f, 0.40f, 0.15f), 0.45f, 90, 16), 0.3f);
            mats["Mirror"] = Lit("Int_Mirror", Noise("Tex_Mirror", new Color(0.70f, 0.75f, 0.78f), 0.02f, 91, 4), 0.97f, 0.8f);
            mats["Door"] = Lit("Int_Door", Door(), 0.3f);
            mats["Glass_Clear"] = Transparent("Int_Glass_Clear", new Color(0.72f, 0.84f, 0.88f, 0.16f), 0.95f);
            mats["Red_Paint"] = Lit("Int_Red_Paint", Noise("Tex_Red_Paint", new Color(0.70f, 0.07f, 0.05f), 0.05f, 92, 6), 0.6f);
            mats["Cardboard"] = Lit("Int_Cardboard", Cardboard(), 0.05f);
            mats["Bedsheet"] = Lit("Int_Bedsheet", Check(), 0.05f);
            mats["Linen"] = Lit("Int_Linen", Weave("Tex_Linen", new Color(0.91f, 0.90f, 0.86f), 0.06f, 96, 256), 0.05f);
            mats["Wall_Tile"] = Lit("Int_Wall_Tile", WallTile(), 0.75f);
            // Phase 4D: shops, the medical centre, the sports complex and outdoor props.
            mats["Sign_Board"] = Lit("Int_Sign_Board", Noise("Tex_Sign_Board", new Color(0.09f, 0.21f, 0.42f), 0.05f, 97, 6), 0.45f);
            mats["Sports_Floor"] = Lit("Int_Sports_Floor", Planks("Tex_Sports_Floor", new Color(0.80f, 0.61f, 0.39f), 24, 98), 0.55f);
            mats["Court_Line"] = Lit("Int_Court_Line", Noise("Tex_Court_Line", new Color(0.95f, 0.95f, 0.93f), 0.03f, 99, 4), 0.5f);
            mats["Rubber"] = Lit("Int_Rubber", Speckle("Tex_Rubber", new Color(0.12f, 0.12f, 0.13f), 100), 0.1f);
            mats["Goods"] = Lit("Int_Goods", Packets(), 0.3f);
            mats["Produce"] = Lit("Int_Produce", Noise("Tex_Produce", new Color(0.78f, 0.42f, 0.12f), 0.35f, 101, 16), 0.45f);
            mats["Net"] = Cutout("Int_Net", NetMesh());
            mats["Lamp_Glow"] = Emissive("Int_Lamp_Glow", new Color(1f, 0.9f, 0.72f), 2.2f);
            mats["Paint_Yellow"] = Lit("Int_Paint_Yellow", Noise("Tex_Paint_Yellow", new Color(0.90f, 0.70f, 0.10f), 0.06f, 102, 6), 0.5f);
        }

        // ------------------------------------------------------------------ material helpers

        static Material Lit(string name, Texture2D tex, float smooth, float metallic = 0f)
        {
            var m = ProceduralAssets.LitMaterial(name, Color.white, tex, smooth);
            if (metallic > 0f && m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            return m;
        }

        static Material Emissive(string name, Color c, float strength)
        {
            var m = ProceduralAssets.LitMaterial(name, c, null, 0.2f);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * strength);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Transparent(string name, Color c, float smooth)
        {
            var m = ProceduralAssets.LitMaterial(name, c, null, smooth);
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // seen from both sides
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Save(string name, Color[] px)
        {
            var c32 = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++) { var c = px[i]; c.a = 1f; c32[i] = c; }
            return ProceduralAssets.SaveTexture(name, Size, c32, false);
        }

        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            return ProceduralAssets.Fbm(u, v, period, octaves, seed);
        }

        static float Hash(int a, int b, int seed)
        {
            unchecked
            {
                int h = a * 374761393 + b * 668265263 + seed * 362437;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        // ------------------------------------------------------------------ textures

        // Plain colour with soft blotches.
        static Texture2D Noise(string name, Color c, float amount, int seed, int period)
        {
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float n = Fbm(u, v, period, 3, seed) - 0.5f;
                    px[y * Size + x] = c * (1f + n * amount * 2f);
                }
            return Save(name, px);
        }

        // Vitrified floor tiles, 0.6 m (two per 1.2 m tile), pale beige with grout and speckle.
        static Texture2D FloorTile()
        {
            var px = new Color[Size * Size];
            var baseC = new Color(0.84f, 0.81f, 0.76f);
            var grout = new Color(0.62f, 0.60f, 0.56f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int tx = (int)(u * 2f), ty = (int)(v * 2f);
                    float fu = u * 2f - tx, fv = v * 2f - ty;
                    float tile = 0.96f + 0.08f * Hash(tx, ty, 11);
                    float n = Fbm(u, v, 8, 3, 93) - 0.5f;
                    float speck = Hash(x, y, 12) > 0.985f ? 0.85f : 1f;
                    Color c = baseC * tile * (1f + n * 0.08f) * speck;
                    if (fu < 0.006f || fu > 0.994f || fv < 0.006f || fv > 0.994f) c = grout;
                    px[y * Size + x] = c;
                }
            return Save("Tex_Floor_Tile", px);
        }

        // Oak planks 0.2 m wide, staggered ends, grain along the plank.
        static Texture2D Planks()
        {
            return Planks("Tex_Floor_Wood", new Color(0.56f, 0.38f, 0.23f), 12, 13);
        }

        // Wooden floor boards: rows of planks with staggered joints (rows per texture tile).
        static Texture2D Planks(string name, Color baseC, int rows, int seed)
        {
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int row = (int)(v * rows);
                    float fv = v * rows - row;
                    float offset = Hash(row, 0, seed);
                    float along = Mathf.Repeat(u * 2f + offset, 1f);
                    int plank = (int)(u * 2f + offset);
                    float shade = 0.85f + 0.25f * Hash(row, plank, seed + 1);
                    float grain = Mathf.Sin((v * rows * 6f + Fbm(u * 4f, v, 4, 3, 94) * 6f) * Mathf.PI * 2f) * 0.06f;
                    Color c = baseC * shade * (1f + grain);
                    if (fv < 0.03f || along < 0.004f) c *= 0.55f;
                    px[y * Size + x] = c;
                }
            return Save(name, px);
        }

        // Fine woven texture (carpet, upholstery).
        static Texture2D Weave(string name, Color c, float amount, int seed, int threads)
        {
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float n = Fbm(u, v, 6, 3, seed) - 0.5f;
                    float wv = ((x * threads / Size + y * threads / Size) % 2 == 0) ? 1.04f : 0.96f;
                    float speck = (Hash(x, y, seed) - 0.5f) * amount;
                    px[y * Size + x] = c * (1f + n * amount + speck) * wv;
                }
            return Save(name, px);
        }

        // Checked cotton bedsheet: blue and white checks, 0.6 m tile (8 checks), woven.
        static Texture2D Check()
        {
            var px = new Color[Size * Size];
            var blue = new Color(0.28f, 0.40f, 0.66f);
            var white = new Color(0.90f, 0.91f, 0.92f);
            const int checks = 8;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int cx = x * checks / Size, cy = y * checks / Size;
                    bool bx = cx % 2 == 0, by = cy % 2 == 0;
                    Color c = bx && by ? blue : (bx || by ? Color.Lerp(blue, white, 0.55f) : white);
                    float wv = ((x * 128 / Size + y * 128 / Size) % 2 == 0) ? 1.03f : 0.97f;
                    float n = Fbm(x / (float)Size, y / (float)Size, 6, 3, 97) - 0.5f;
                    px[y * Size + x] = c * wv * (1f + n * 0.08f);
                }
            return Save("Tex_Bedsheet", px);
        }

        // White glazed wall tiles, 0.3 m (four per 1.2 m tile), thin grey grout, slight sheen variation.
        static Texture2D WallTile()
        {
            var px = new Color[Size * Size];
            var baseC = new Color(0.93f, 0.94f, 0.94f);
            var grout = new Color(0.70f, 0.71f, 0.70f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int tx = (int)(u * 4f), ty = (int)(v * 4f);
                    float fu = u * 4f - tx, fv = v * 4f - ty;
                    Color c = baseC * (0.98f + 0.04f * Hash(tx, ty, 15));
                    if (fu < 0.012f || fu > 0.988f || fv < 0.012f || fv > 0.988f) c = grout;
                    px[y * Size + x] = c;
                }
            return Save("Tex_Wall_Tile", px);
        }

        // Interior paint, one tile 3 m wide by one floor high: a dark skirting at floor level
        // (0.6-0.7 m above the building base, as every floor sits 0.6 m above its storey line).
        static Texture2D WallPaint()
        {
            var px = new Color[Size * Size];
            var paint = new Color(0.92f, 0.90f, 0.85f);
            var skirt = new Color(0.36f, 0.33f, 0.30f);
            float floorV = 0.6f / 3.4f, skirtTop = 0.7f / 3.4f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float n = Fbm(u, v, 4, 3, 95) - 0.5f;
                    Color c = paint * (1f + n * 0.04f);
                    if (v >= floorV - 0.02f && v < skirtTop) c = skirt * (1f + n * 0.1f);
                    else if (v >= skirtTop && v < skirtTop + 0.006f) c *= 0.8f;
                    px[y * Size + x] = c;
                }
            return Save("Tex_Wall_Paint", px);
        }

        // Acoustic ceiling tiles, 0.6 m grid on a grey T-bar.
        static Texture2D CeilingTiles()
        {
            var px = new Color[Size * Size];
            var tileC = new Color(0.93f, 0.93f, 0.91f);
            var bar = new Color(0.72f, 0.72f, 0.72f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float fu = Mathf.Repeat(u * 2f, 1f), fv = Mathf.Repeat(v * 2f, 1f);
                    Color c = tileC * (Hash(x, y, 15) > 0.97f ? 0.88f : 1f);
                    if (fu < 0.01f || fu > 0.99f || fv < 0.01f || fv > 0.99f) c = bar;
                    px[y * Size + x] = c;
                }
            return Save("Tex_Ceiling", px);
        }

        // Wood grain; vertical streaks across u.
        static Texture2D Grain(string name, Color c, int seed, bool dark)
        {
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float warp = Fbm(u, v, 3, 3, seed) * 3f;
                    float ring = Mathf.Sin((u * 18f + warp) * Mathf.PI * 2f);
                    float fine = Fbm(u * 8f, v * 0.5f, 8, 2, seed + 1) - 0.5f;
                    float k = 1f + ring * (dark ? 0.07f : 0.04f) + fine * 0.1f;
                    px[y * Size + x] = c * k;
                }
            return Save(name, px);
        }

        static Texture2D Brushed()
        {
            var px = new Color[Size * Size];
            var c = new Color(0.68f, 0.69f, 0.70f);
            for (int y = 0; y < Size; y++)
            {
                float line = 1f + (Hash(y, 0, 16) - 0.5f) * 0.08f;
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float n = Fbm(u, v, 4, 2, 96) - 0.5f;
                    px[y * Size + x] = c * line * (1f + n * 0.06f);
                }
            }
            return Save("Tex_Steel", px);
        }

        // Book spines: random colours in narrow vertical bands, a pale label near the top.
        static Texture2D Books()
        {
            var px = new Color[Size * Size];
            var palette = new[]
            {
                new Color(0.55f, 0.12f, 0.10f), new Color(0.12f, 0.22f, 0.45f), new Color(0.15f, 0.35f, 0.20f),
                new Color(0.70f, 0.55f, 0.25f), new Color(0.25f, 0.18f, 0.14f), new Color(0.80f, 0.78f, 0.70f),
                new Color(0.35f, 0.15f, 0.35f), new Color(0.10f, 0.10f, 0.12f),
            };
            for (int x = 0; x < Size; x++)
            {
                int band = x / 18;
                var col = palette[(int)(Hash(band, 1, 17) * palette.Length) % palette.Length];
                bool edge = x % 18 == 0;
                for (int y = 0; y < Size; y++)
                {
                    float v = y / (float)Size;
                    Color c = col * (0.9f + 0.2f * Hash(band, 2, 18));
                    if (v > 0.7f && v < 0.8f) c = Color.Lerp(c, new Color(0.9f, 0.85f, 0.7f), 0.6f);
                    if (v > 0.25f && v < 0.27f) c *= 0.6f;
                    if (edge) c *= 0.5f;
                    px[y * Size + x] = c;
                }
            }
            return Save("Tex_Books", px);
        }

        // Cork board with pinned notices.
        static Texture2D Notice()
        {
            var px = new Color[Size * Size];
            var cork = new Color(0.66f, 0.50f, 0.33f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float n = Hash(x, y, 19) - 0.5f;
                    px[y * Size + x] = cork * (1f + n * 0.25f);
                }
            var papers = new[] { new Color(0.95f, 0.95f, 0.92f), new Color(0.95f, 0.90f, 0.55f), new Color(0.75f, 0.88f, 0.95f), new Color(0.95f, 0.75f, 0.75f) };
            for (int k = 0; k < 9; k++)
            {
                float x0 = 0.05f + 0.3f * (k % 3) + 0.05f * Hash(k, 3, 20);
                float y0 = 0.05f + 0.3f * (k / 3) + 0.05f * Hash(k, 4, 20);
                float w = 0.18f + 0.06f * Hash(k, 5, 20), h = 0.2f + 0.05f * Hash(k, 6, 20);
                var col = papers[k % papers.Length];
                for (int y = (int)(y0 * Size); y < (int)((y0 + h) * Size) && y < Size; y++)
                    for (int x = (int)(x0 * Size); x < (int)((x0 + w) * Size) && x < Size; x++)
                    {
                        float fy = (y / (float)Size - y0) / h;
                        Color c = col;
                        // Lines of "text".
                        if (fy < 0.85f && Mathf.Repeat(fy * 14f, 1f) < 0.3f && Hash(x / 6, y / 4, 21) > 0.25f) c *= 0.55f;
                        px[y * Size + x] = c;
                    }
                // Pin.
                int px0 = (int)((x0 + w / 2) * Size), py0 = (int)((y0 + h - 0.02f) * Size);
                for (int y = py0 - 3; y < py0 + 3; y++)
                    for (int x = px0 - 3; x < px0 + 3; x++)
                        if (x >= 0 && x < Size && y >= 0 && y < Size) px[y * Size + x] = new Color(0.8f, 0.1f, 0.1f);
            }
            return Save("Tex_Notice", px);
        }

        // Veneered door, one tile per leaf: vertical grain and a thin edge band.
        static Texture2D Door()
        {
            var px = new Color[Size * Size];
            var c = new Color(0.52f, 0.34f, 0.20f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float warp = Fbm(u, v, 3, 3, 97) * 2f;
                    float ring = Mathf.Sin((u * 24f + warp) * Mathf.PI * 2f);
                    Color col = c * (1f + ring * 0.05f);
                    if (u > 0.94f || u < 0.02f) col *= 0.8f;
                    px[y * Size + x] = col;
                }
            return Save("Tex_Door", px);
        }

        static Texture2D Cardboard()
        {
            var px = new Color[Size * Size];
            var c = new Color(0.62f, 0.48f, 0.31f);
            var tape = new Color(0.72f, 0.62f, 0.45f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float n = Fbm(u, v, 6, 2, 98) - 0.5f;
                    Color col = c * (1f + n * 0.1f);
                    if (Mathf.Abs(u - 0.5f) < 0.04f) col = tape;
                    px[y * Size + x] = col;
                }
            return Save("Tex_Cardboard", px);
        }

        // ------------------------------------------------------------------ Phase 4D textures

        // Gym rubber: near black with fine coloured flecks.
        static Texture2D Speckle(string name, Color c, int seed)
        {
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float h = Hash(x, y, seed);
                    Color col = c * (0.9f + 0.2f * Hash(x / 3, y / 3, seed + 1));
                    if (h > 0.985f) col = new Color(0.55f, 0.55f, 0.58f);
                    else if (h > 0.975f) col = new Color(0.20f, 0.35f, 0.60f);
                    px[y * Size + x] = col;
                }
            return Save(name, px);
        }

        // Shop shelf stock: blocks of brightly printed packets with white labels.
        static Texture2D Packets()
        {
            var px = new Color[Size * Size];
            var palette = new[]
            {
                new Color(0.85f, 0.15f, 0.12f), new Color(0.95f, 0.75f, 0.10f), new Color(0.10f, 0.45f, 0.75f),
                new Color(0.15f, 0.60f, 0.25f), new Color(0.95f, 0.50f, 0.10f), new Color(0.55f, 0.20f, 0.55f),
                new Color(0.92f, 0.90f, 0.85f), new Color(0.20f, 0.20f, 0.22f),
            };
            const int cols = 16, rows = 8;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int cx = x * cols / Size, cy = y * rows / Size;
                    float fu = x * cols / (float)Size - cx, fv = y * rows / (float)Size - cy;
                    var col = palette[(int)(Hash(cx, cy, 103) * palette.Length) % palette.Length];
                    if (fv > 0.45f && fv < 0.7f && fu > 0.15f && fu < 0.85f) col = Color.Lerp(col, Color.white, 0.75f);
                    if (fu < 0.04f || fv < 0.03f) col *= 0.6f;
                    px[y * Size + x] = col;
                }
            return Save("Tex_Goods", px);
        }

        // Diamond mesh for nets and chain-link fencing: white or galvanised wire, see-through holes.
        static Texture2D NetMesh()
        {
            var c32 = new Color32[Size * Size];
            const int cells = 8;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x * cells / (float)Size, v = y * cells / (float)Size;
                    float a = Mathf.Abs(Mathf.Repeat(u + v, 1f) - 0.5f);
                    float b = Mathf.Abs(Mathf.Repeat(u - v, 1f) - 0.5f);
                    bool wire = a > 0.44f || b > 0.44f;
                    byte g = (byte)(205 + 30 * Hash(x, y, 104));
                    c32[y * Size + x] = new Color32(g, g, (byte)(g - 5), (byte)(wire ? 255 : 0));
                }
            var tex = ProceduralAssets.SaveTexture("Tex_Net", Size, c32, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            EditorUtility.SetDirty(tex);
            return tex;
        }

        static Material Cutout(string name, Texture2D tex)
        {
            var m = ProceduralAssets.LitMaterial(name, Color.white, tex, 0.2f);
            if (m.HasProperty("_AlphaClip"))
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.SetOverrideTag("RenderType", "TransparentCutout");
            }
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);    // both faces
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
