using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Makes the textures, tree meshes and tree prefabs that the terrain uses, from code,
    /// so the repository needs no image files. Everything is saved under Assets/Generated
    /// and is rebuilt each time the campus is built.
    /// </summary>
    public static class ProceduralAssets
    {
        public const string Folder = "Assets/Generated";

        // ------------------------------------------------------------------ folders

        public static void ResetFolder()
        {
            heightSource.Clear();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            EnsureFolder(Folder);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        // ------------------------------------------------------------------ noise

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // Value noise that repeats every `period` cells, so textures tile without seams.
        public static float Noise(float x, float y, int period, int seed)
        {
            return Noise(x, y, period, period, seed);
        }

        public static float Noise(float x, float y, int periodX, int periodY, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            xf = xf * xf * (3f - 2f * xf);
            yf = yf * yf * (3f - 2f * yf);
            int x0 = ((xi % periodX) + periodX) % periodX, y0 = ((yi % periodY) + periodY) % periodY;
            int x1 = (x0 + 1) % periodX, y1 = (y0 + 1) % periodY;
            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), xf);
            float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), xf);
            return Mathf.Lerp(a, b, yf);
        }

        public static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int period = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(u * period, v * period, period, seed + o * 31);
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        // ------------------------------------------------------------------ textures

        /// <summary>A tiling ground texture: blotches between three colours plus fine speckle.</summary>
        public static Texture2D GroundTexture(string name, Color dark, Color mid, Color light,
                                              int basePeriod, float speckle, int seed, bool streaks = false)
        {
            const int size = 512;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = Mathf.Clamp01((Fbm(u, v, basePeriod, 5, seed) - 0.5f) * 1.8f + 0.5f);
                    // Bark gets long vertical streaks; ground gets round speckle.
                    float fine = streaks ? Noise(u * 96f, v * 6f, 96, 6, seed + 7)
                                         : Noise(u * 128f, v * 128f, 128, seed + 7);
                    Color c = n < 0.5f ? Color.Lerp(dark, mid, n * 2f) : Color.Lerp(mid, light, (n - 0.5f) * 2f);
                    c = Color.Lerp(c, fine > 0.5f ? light : dark, Mathf.Abs(fine - 0.5f) * 2f * speckle);
                    pixels[y * size + x] = c;
                }
            }
            return SaveTexture(name, size, pixels, false);
        }

        /// <summary>Grass tufts for terrain detail: pale blades on a transparent background (tinted by the terrain).</summary>
        public static Texture2D GrassBladesTexture()
        {
            return GrassTexture("Grass_Blades", 18, 0.45f, 2.5f, 30f, 11, false);
        }

        /// <summary>
        /// A billboard grass card (terrain detail, GrassBillboard mode): blades darker at the root
        /// and pale at the tip on a transparent background; the terrain tints it with the
        /// prototype's healthy and dry colours. minHeight is the shortest blade as a share of the
        /// card; seedHeads adds a few taller stems with seed tips (wild grass).
        /// Mirrored by tools/blender/preview_terrain_detail.py (grass_card).
        /// </summary>
        public static Texture2D GrassTexture(string name, int blades, float minHeight, float width, float lean,
                                             int seed, bool seedHeads)
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 210, 170, 0);
            var random = new System.Random(seed);
            for (int blade = 0; blade < blades; blade++)
            {
                float baseX = 8 + (float)random.NextDouble() * (size - 16);
                float height = size * (minHeight + (1f - minHeight) * (float)random.NextDouble());
                float tilt = ((float)random.NextDouble() - 0.5f) * lean;
                float w = width + (float)random.NextDouble() * width;
                float tone = 0.8f + 0.2f * (float)random.NextDouble();
                for (int y = 0; y < height; y++)
                {
                    float t = y / height;
                    float cx = baseX + tilt * t * t;
                    float half = w * (1f - t);
                    float shade = (0.5f + 0.5f * t) * tone;
                    for (int x = Mathf.FloorToInt(cx - half); x <= Mathf.CeilToInt(cx + half); x++)
                    {
                        if (x < 0 || x >= size) continue;
                        pixels[y * size + x] = new Color(0.85f * shade, 0.9f * shade, 0.7f * shade, 1f);
                    }
                }
            }
            if (seedHeads)
            {
                for (int stem = 0; stem < 4; stem++)
                {
                    int x = 16 + random.Next(size - 32);
                    int top = size - 4 - random.Next(10);
                    for (int y = 0; y < top; y++) pixels[y * size + x] = new Color(0.8f, 0.78f, 0.62f, 1f);
                    for (int y = top - 14; y < top; y++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (x + dx >= 0 && x + dx < size) pixels[y * size + x + dx] = new Color(0.95f, 0.88f, 0.7f, 1f);
                }
            }
            return SaveTexture(name, size, pixels, true);
        }

        /// <summary>
        /// A billboard card of small flowers on thin stems among a few blades. The terrain
        /// multiplies it by the prototype's colours, so those stay near white and the flower
        /// colour comes from here. Mirrored by preview_terrain_detail.py (flower_card).
        /// </summary>
        public static Texture2D FlowerTexture(string name, Color petal, Color centre, int flowers, int seed)
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(120, 150, 80, 0);
            var random = new System.Random(seed);
            var stemColour = new Color(0.32f, 0.45f, 0.18f, 1f);
            for (int blade = 0; blade < 8; blade++)
            {
                float baseX = 10 + (float)random.NextDouble() * (size - 20);
                float height = size * (0.25f + 0.3f * (float)random.NextDouble());
                for (int y = 0; y < height; y++)
                {
                    float t = y / height;
                    float half = 2.5f * (1f - t);
                    for (int x = Mathf.FloorToInt(baseX - half); x <= Mathf.CeilToInt(baseX + half); x++)
                        if (x >= 0 && x < size) pixels[y * size + x] = stemColour * (0.7f + 0.3f * t);
                }
            }
            for (int f = 0; f < flowers; f++)
            {
                int cx = 14 + random.Next(size - 28);
                int cy = (int)(size * (0.5f + 0.4f * (float)random.NextDouble()));
                float r = 6f + 4f * (float)random.NextDouble();
                for (int y = 0; y < cy; y++) pixels[y * size + cx] = stemColour;
                for (int y = Mathf.Max(0, (int)(cy - r)); y < Mathf.Min(size, (int)(cy + r + 1)); y++)
                {
                    for (int x = Mathf.Max(0, (int)(cx - r)); x < Mathf.Min(size, (int)(cx + r + 1)); x++)
                    {
                        float dx = x - cx, dy = y - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                        // Five soft petals: the edge pulls in between them.
                        float a = Mathf.Atan2(dy, dx);
                        float edge = r * (0.75f + 0.25f * Mathf.Cos(a * 5f));
                        if (d > edge) continue;
                        Color c = d < r * 0.3f ? centre : petal * (0.85f + 0.15f * (1f - d / r));
                        c.a = 1f;
                        pixels[y * size + x] = c;
                    }
                }
            }
            return SaveTexture(name, size, pixels, true);
        }

        /// <summary>
        /// A clump of real grass blades for terrain detail (mesh, Grass mode), about 0.55 m tall:
        /// curved, tapering strips leaning out from the root. Vertex alpha runs 0 at the root to 1 at
        /// the tip, which the terrain's grass shader uses to wave the tips only; normals lean
        /// upwards so the clump is lit softly like the ground under it.
        /// Mirrored by preview_terrain_detail.py (clump_mesh).
        /// </summary>
        public static GameObject GrassClumpPrefab()
        {
            const int blades = 11, rows = 4;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colours = new List<Color>();
            var tris = new List<int>();
            var random = new System.Random(21);
            for (int b = 0; b < blades; b++)
            {
                float yaw = b * 2.4f + (float)random.NextDouble() * 0.6f;        // golden-angle spread
                float height = 0.35f + 0.25f * (float)random.NextDouble();
                float lean = 0.12f + 0.22f * (float)random.NextDouble();
                float width = 0.025f + 0.015f * (float)random.NextDouble();
                var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                var side = new Vector3(-outward.z, 0f, outward.x);
                var root = outward * (0.03f * (float)random.NextDouble());
                int start = verts.Count;
                for (int r = 0; r <= rows; r++)
                {
                    float t = r / (float)rows;
                    var centre = root + outward * (lean * t * t);
                    centre.y = height * t * (1f - 0.3f * lean * t);      // a leaning blade is a little shorter
                    float half = width * (1f - t * 0.92f);
                    var normal = (Vector3.up * 0.75f + outward * 0.25f).normalized;
                    verts.Add(centre - side * half);
                    verts.Add(centre + side * half);
                    normals.Add(normal);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0f, t));
                    uvs.Add(new Vector2(1f, t));
                    colours.Add(new Color(1f, 1f, 1f, t));
                    colours.Add(new Color(1f, 1f, 1f, t));
                }
                for (int r = 0; r < rows; r++)
                {
                    int a = start + r * 2, c = a + 2;
                    tris.Add(a); tris.Add(c); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(c); tris.Add(c + 1);
                }
            }
            var mesh = new Mesh { name = "Grass_Clump" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            EnsureFolder(Folder + "/Terrain");
            AssetDatabase.CreateAsset(mesh, Folder + "/Terrain/Grass_Clump_Mesh.asset");

            // Root dark, tip pale; opaque, since the blades are real geometry.
            const int size = 32;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float t = y / (float)(size - 1);
                    float v = 0.45f + 0.55f * Mathf.Sqrt(t);
                    px[y * size + x] = new Color(v, v, v * 0.92f, 1f);
                }
            var tex = SaveTexture("Grass_Clump", size, px, true);
            var mat = LitMaterial("Grass_Clump", Color.white, tex, 0.1f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);   // blades are single sheets
            mat.doubleSidedGI = true;

            var go = new GameObject("Grass_Clump");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Terrain/Grass_Clump.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>
        /// A terrain layer's mask map, channel-packed the way URP's terrain reads it: R metallic
        /// (0), G ambient occlusion, B height, A smoothness. Height and occlusion come from the
        /// albedo's own brightness (light clods and tufts stand up, dark gaps sink), so with height
        /// blending a layer shows through another in its gaps instead of a soft smear. Linear.
        /// Returns null if the texture's pixels are not known (see NormalFor).
        /// </summary>
        public static Texture2D MaskFor(Texture2D albedo, float smoothness)
        {
            if (albedo == null) return null;
            Color32[] src;
            if (!heightSource.TryGetValue(albedo.name, out src)) return null;
            string name = albedo.name + "_Mask";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/" + name + ".asset");
            if (existing != null) return existing;
            int size = albedo.width;
            if (src.Length != size * size) return null;
            float lo = 1f, hi = 0f;
            var h = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                h[i] = (src[i].r * 0.3f + src[i].g * 0.59f + src[i].b * 0.11f) / 255f;
                lo = Mathf.Min(lo, h[i]);
                hi = Mathf.Max(hi, h[i]);
            }
            float span = Mathf.Max(hi - lo, 1e-3f);
            var px = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float height = (h[i] - lo) / span;
                float ao = 0.55f + 0.45f * Mathf.Sqrt(height);
                float smooth = smoothness * (0.6f + 0.4f * (1f - height));   // damp hollows shine a little more
                px[i] = new Color32(0, (byte)Mathf.RoundToInt(ao * 255f), (byte)Mathf.RoundToInt(height * 255f),
                                    (byte)Mathf.RoundToInt(Mathf.Clamp01(smooth) * 255f));
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name };
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            try
            {
                EditorUtility.CompressTexture(tex, TextureFormat.DXT5, TextureCompressionQuality.Normal);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Jagti] Texture compression skipped for " + name + ": " + e.Message);
            }
            EnsureFolder(Folder + "/Textures");
            AssetDatabase.CreateAsset(tex, Folder + "/Textures/" + name + ".asset");
            return tex;
        }

        // The pixels of each opaque texture saved during this build, so a normal map can be made
        // from it afterwards (NormalFor). Cleared when the generated folder is reset.
        static readonly Dictionary<string, Color32[]> heightSource = new Dictionary<string, Color32[]>();

        public static Texture2D SaveTexture(string name, int size, Color32[] pixels, bool alpha)
        {
            if (!alpha) heightSource[name] = pixels;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name };
            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = alpha ? 1 : 4;
            try
            {
                EditorUtility.CompressTexture(tex, alpha ? TextureFormat.DXT5 : TextureFormat.DXT1, TextureCompressionQuality.Normal);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Jagti] Texture compression skipped for " + name + ": " + e.Message);
            }
            EnsureFolder(Folder + "/Textures");
            AssetDatabase.CreateAsset(tex, Folder + "/Textures/" + name + ".asset");
            return tex;
        }

        /// <summary>
        /// The campus roads as in the owner's photos (2026-11-19): pale concrete slabs, not asphalt,
        /// with sawn joints (one down the middle, one across every half width), faint transverse
        /// brushing, darker tyre tracks and grime at the kerbs. One tile spans the road's width and
        /// the same length along it. With a centre line (two-lane roads), the middle joint carries
        /// a white dash 45 % of the road width long. (Names kept from the asphalt version.)
        /// </summary>
        public static Texture2D AsphaltTexture(bool centreLine)
        {
            const int size = 512;
            var pixels = new Color32[size * size];
            var baseColor = new Color(0.64f, 0.63f, 0.60f);
            var paint = new Color(0.92f, 0.92f, 0.89f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // Each slab (half the width by half the width) is poured a shade apart.
                    int sx = u < 0.5f ? 0 : 1, sy = v < 0.5f ? 0 : 1;
                    float slab = 1f + 0.05f * (Noise(sx * 3.7f + 0.5f, sy * 2.9f + 0.5f, 64, 50) - 0.5f) * 2f;
                    float n = Fbm(u, v, 4, 4, 54) - 0.5f;
                    float grit = Noise(u * 256f, v * 256f, 256, 55) - 0.5f;
                    float brush = Mathf.Sin(v * size * 0.9f + Noise(u * 8f, v * 64f, 8, 64, 51) * 6f) * 0.5f;
                    Color c = baseColor * slab * (1f + n * 0.12f + grit * 0.10f + brush * 0.025f);
                    // Rubber from the wheels, a quarter of the way in from each side.
                    float track = Mathf.Min(Mathf.Abs(u - 0.25f), Mathf.Abs(u - 0.75f));
                    float tyre = Mathf.Clamp01(1f - track / 0.09f) * (0.6f + 0.4f * Fbm(u, v * 2f, 8, 3, 52));
                    c *= 1f - 0.10f * tyre;
                    // Grime and dust washed against the kerbs.
                    float edge = Mathf.Min(u, 1f - u);
                    c = Color.Lerp(c, new Color(0.47f, 0.44f, 0.39f), Mathf.Clamp01(1f - edge / 0.05f) * 0.45f);
                    // Stains: a few darker oil and water marks.
                    float stain = Mathf.Clamp01((Fbm(u, v, 5, 3, 53) - 0.66f) * 5f);
                    c *= 1f - 0.12f * stain;
                    // Sawn joints, slightly dark and dirty.
                    float ju = Mathf.Abs(u - 0.5f), jv = Mathf.Min(Mathf.Min(v, 1f - v), Mathf.Abs(v - 0.5f));
                    if ((!centreLine && ju < 0.003f) || jv < 0.0025f) c *= 0.6f;
                    if (centreLine && ju < 0.012f && v < 0.45f)
                    {
                        float worn = Fbm(u * 3f, v, 8, 3, 56);
                        c = Color.Lerp(paint, c, Mathf.Clamp01((worn - 0.62f) * 4f)); // paint worn through in spots
                    }
                    else if (centreLine && ju < 0.003f) c *= 0.6f;
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }
            return SaveTexture(centreLine ? "Tex_Road_Concrete_Lined" : "Tex_Road_Concrete", size, pixels, false);
        }

        /// <summary>
        /// Cream stone paving for the academic ring drive, plazas and garden paths (campus model).
        /// One tile is 3 m square: 5 x 5 slabs of 0.6 m with thin dark joints and slight colour
        /// variation from slab to slab. Roads using it get UVs in metres / 3.
        /// </summary>
        public static Texture2D PavingTexture()
        {
            const int size = 512, slabs = 5;
            var pixels = new Color32[size * size];
            var cream = new Color(0.84f, 0.78f, 0.69f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    int sx = (int)(u * slabs), sy = (int)(v * slabs);
                    float slab = 0.94f + 0.10f * Noise(sx * 3.1f + 0.5f, sy * 5.3f + 0.5f, 64, 57);
                    float n = Fbm(u, v, 6, 3, 58) - 0.5f;
                    float grit = Noise(u * 256f, v * 256f, 256, 59) - 0.5f;
                    Color c = cream * slab * (1f + n * 0.10f + grit * 0.06f);
                    float fu = u * slabs - sx, fv = v * slabs - sy;
                    if (fu < 0.02f || fu > 0.98f || fv < 0.02f || fv > 0.98f) c *= 0.62f; // joints
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }
            return SaveTexture("Tex_Paving_Cream", size, pixels, false);
        }

        /// <summary>Precast kerb stones painted in black and white blocks of 0.6 m, as in the owner's
        /// photos of the campus roads: a joint every 0.6 m, the paint chipped and dusty.</summary>
        public static Texture2D KerbTexture()
        {
            const int size = 256;
            var pixels = new Color32[size * size];
            var stone = new Color(0.82f, 0.80f, 0.75f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float wear = Fbm(u, v, 8, 3, 53) - 0.5f;
                    float grit = Noise(u * 128f, v * 128f, 128, 60) - 0.5f;
                    Color c = stone * (1f + wear * 0.18f + grit * 0.08f);
                    // Paint: black on the first half of each 1.2 m tile, white on the second,
                    // worn back to the grey stone in spots and dusty along the bottom edge.
                    bool black = u < 0.5f;
                    Color paint = black ? new Color(0.07f, 0.07f, 0.07f) : new Color(0.90f, 0.90f, 0.87f);
                    float chip = Mathf.Clamp01((Fbm(u, v, 16, 3, 61) - 0.7f) * 6f);
                    c = Color.Lerp(paint * (1f + grit * 0.1f), c, chip * 0.8f);
                    c = Color.Lerp(c, new Color(0.55f, 0.50f, 0.42f), Mathf.Clamp01(0.25f - v) * 1.2f * (black ? 0.6f : 1f));
                    if (u < 0.012f || u > 0.988f || Mathf.Abs(u - 0.5f) < 0.006f) c *= 0.55f; // joints between stones
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }
            return SaveTexture("Tex_Kerb", size, pixels, false);
        }

        // ------------------------------------------------------------------ surface relief (look pass 2)

        /// <summary>
        /// A normal map for an opaque texture made earlier in this build: its brightness is taken as
        /// height (dark joints, pits and grit sit lower), so plaster, concrete, paving and ground
        /// catch the low sun with fine relief. strength is the slope per unit of brightness.
        /// Stored as plain RGB (alpha 1), which URP's normal unpacking reads correctly; linear.
        /// Returns null if the texture's pixels are not known.
        /// </summary>
        public static Texture2D NormalFor(Texture2D albedo, float strength)
        {
            if (albedo == null) return null;
            Color32[] src;
            if (!heightSource.TryGetValue(albedo.name, out src)) return null;
            string name = albedo.name + "_N";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/" + name + ".asset");
            if (existing != null) return existing;
            int size = albedo.width;
            if (src.Length != size * size) return null;
            var h = new float[src.Length];
            for (int i = 0; i < src.Length; i++) h[i] = (src[i].r * 0.3f + src[i].g * 0.59f + src[i].b * 0.11f) / 255f;
            var px = new Color32[src.Length];
            for (int y = 0; y < size; y++)
            {
                int yu = ((y + 1) % size) * size, yd = ((y + size - 1) % size) * size, yr = y * size;
                for (int x = 0; x < size; x++)
                {
                    int xr = (x + 1) % size, xl = (x + size - 1) % size;
                    float dx = (h[yr + xr] - h[yr + xl]) * strength;
                    float dy = (h[yu + x] - h[yd + x]) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    px[yr + x] = new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                                             (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                                             (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name };
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            try
            {
                EditorUtility.CompressTexture(tex, TextureFormat.DXT5, TextureCompressionQuality.Normal);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Jagti] Texture compression skipped for " + name + ": " + e.Message);
            }
            EnsureFolder(Folder + "/Textures");
            AssetDatabase.CreateAsset(tex, Folder + "/Textures/" + name + ".asset");
            return tex;
        }

        /// <summary>Gives a Lit material the relief of its own texture (see NormalFor).</summary>
        public static void AddRelief(Material mat, float strength, float bumpScale = 1f)
        {
            if (mat == null || !mat.HasProperty("_BumpMap")) return;
            var albedo = mat.GetTexture("_BaseMap") as Texture2D;
            var normal = NormalFor(albedo, strength);
            if (normal == null) return;
            mat.SetTexture("_BumpMap", normal);
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", bumpScale);
            mat.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(mat);
        }

        static Texture2D macro;

        /// <summary>
        /// Large soft patches of lighter and darker (weathering, damp, sun-bleaching) with faint
        /// vertical rain streaks, used as URP Lit's detail albedo at a low tiling so a wall or road
        /// stops repeating the same tile every few metres. Linear, centred on 0.5 (no change).
        /// </summary>
        public static Texture2D MacroVariation()
        {
            if (macro != null) return macro;
            macro = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/Tex_Macro_Variation.asset");
            if (macro != null) return macro;
            const int size = 256;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float blot = Fbm(u, v, 3, 4, 401) - 0.5f;
                    float streak = Noise(u * 48f, v * 3f, 48, 3, 402) - 0.5f;
                    float warm = Fbm(u, v, 2, 3, 403) - 0.5f;
                    float g = 0.5f + blot * 0.16f + streak * 0.05f;
                    px[y * size + x] = new Color(g + warm * 0.02f, g, g - warm * 0.025f, 1f);
                }
            macro = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Tex_Macro_Variation" };
            macro.SetPixels32(px);
            macro.Apply(true);
            macro.wrapMode = TextureWrapMode.Repeat;
            macro.filterMode = FilterMode.Trilinear;
            EnsureFolder(Folder + "/Textures");
            AssetDatabase.CreateAsset(macro, Folder + "/Textures/Tex_Macro_Variation.asset");
            return macro;
        }

        /// <summary>Lays MacroVariation over a Lit material at the given tiling (patches tiling x larger than
        /// one tile of its own texture).</summary>
        public static void AddMacroVariation(Material mat, float tiling)
        {
            if (mat == null || !mat.HasProperty("_DetailAlbedoMap")) return;
            mat.SetTexture("_DetailAlbedoMap", MacroVariation());
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(tiling, tiling));
            if (mat.HasProperty("_DetailAlbedoMapScale")) mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(mat);
        }

        // ------------------------------------------------------------------ materials

        public static Material LitMaterial(string name, Color color, Texture2D texture, float smoothness = 0.05f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name, enableInstancing = true };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (texture != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            EnsureFolder(Folder + "/Materials");
            AssetDatabase.CreateAsset(mat, Folder + "/Materials/" + name + ".mat");
            return mat;
        }

        /// <summary>
        /// A material on the wind shader (Assets/Shaders/JagtiFoliage.shader). Falls back to URP/Lit,
        /// still but drawn, if that shader is missing or failed to compile on this computer.
        /// flutter: leaf shake (0 for bark); bend: how far the plant leans (shrubs are short, so more).
        /// </summary>
        public static Material FoliageMaterial(string name, Color color, Texture2D texture, float flutter, float bend)
        {
            var shader = Shader.Find("Jagti/Foliage");
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            {
                if (shader != null) Debug.LogWarning("[Jagti] Jagti/Foliage shader did not compile; trees use URP/Lit and stay still.");
                return LitMaterial(name, color, texture);
            }
            var mat = new Material(shader) { name = name, enableInstancing = true };
            mat.SetColor("_BaseColor", color);
            if (texture != null) mat.SetTexture("_BaseMap", texture);
            mat.SetFloat("_Flutter", flutter);
            mat.SetFloat("_Bend", bend);
            mat.SetFloat("_Translucency", flutter > 0f ? 0.35f : 0f);
            EnsureFolder(Folder + "/Materials");
            AssetDatabase.CreateAsset(mat, Folder + "/Materials/" + name + ".mat");
            return mat;
        }

        // ------------------------------------------------------------------ trees

        public enum TreeKind { Broadleaf = 0, Eucalyptus = 1, Pine = 2, Shrub = 3 }

        /// <summary>Makes the four tree prefabs, in TreeKind order.</summary>
        public static GameObject[] TreePrefabs()
        {
            var leafTex = GroundTexture("Leaves", new Color(0.55f, 0.6f, 0.5f), new Color(0.85f, 0.88f, 0.8f), Color.white, 8, 0.6f, 40);
            var barkTex = GroundTexture("Bark", new Color(0.6f, 0.58f, 0.55f), new Color(0.85f, 0.83f, 0.8f), Color.white, 4, 0.5f, 41, true);

            // Bark and leaves share the wind shader (Jagti/Foliage), so the trunk bends with its crown.
            var bark = FoliageMaterial("Tree_Bark", new Color(0.36f, 0.28f, 0.21f), barkTex, 0f, 1f);
            var paleBark = FoliageMaterial("Tree_Bark_Eucalyptus", new Color(0.76f, 0.72f, 0.64f), barkTex, 0f, 1.2f);
            var leavesBroad = FoliageMaterial("Tree_Leaves_Broadleaf", new Color(0.24f, 0.36f, 0.13f), leafTex, 1f, 1f);
            var leavesEuca = FoliageMaterial("Tree_Leaves_Eucalyptus", new Color(0.40f, 0.48f, 0.31f), leafTex, 1f, 1.2f);
            var leavesPine = FoliageMaterial("Tree_Leaves_Pine", new Color(0.17f, 0.28f, 0.15f), leafTex, 0.6f, 0.8f);
            var leavesShrub = FoliageMaterial("Tree_Leaves_Shrub", new Color(0.36f, 0.40f, 0.18f), leafTex, 0.7f, 2.5f);

            EnsureFolder(Folder + "/Trees");
            return new[]
            {
                SaveTree("Tree_Broadleaf", BuildBroadleaf(false), BuildBroadleaf(true), bark, leavesBroad, 0.25f, 3.5f, 0.22f, 0.012f),
                SaveTree("Tree_Eucalyptus", BuildEucalyptus(false), BuildEucalyptus(true), paleBark, leavesEuca, 0.2f, 10f, 0.25f, 0.01f),
                SaveTree("Tree_Pine", BuildPine(false), BuildPine(true), bark, leavesPine, 0.22f, 11f, 0.25f, 0.01f),
                SaveTree("Tree_Shrub", BuildShrub(false), BuildShrub(true), bark, leavesShrub, 0f, 0f, 0.12f, 0.02f),
            };
        }

        /// <summary>
        /// A tree prefab with two levels of detail (the realistic-graphics video's LOD lesson): the
        /// full mesh close up, a coarse one (about a third of the triangles) once the tree is under
        /// `near` of the screen's height, and nothing under `cull`, so the thousands of scrub bushes
        /// past ~100 m and far trees cost nothing. The quality tiers scale both through lodBias.
        /// The LOD children sit at the root's origin, so the wind shader's pivot is unchanged.
        /// </summary>
        static GameObject SaveTree(string name, Mesh mesh, Mesh coarse, Material bark, Material leaves,
                                   float trunkRadius, float trunkHeight, float near, float cull)
        {
            AssetDatabase.CreateAsset(mesh, Folder + "/Trees/" + name + "_Mesh.asset");
            AssetDatabase.CreateAsset(coarse, Folder + "/Trees/" + name + "_Mesh_LOD1.asset");
            var go = new GameObject(name);
            var lods = new LOD[2];
            var meshes = new[] { mesh, coarse };
            for (int i = 0; i < 2; i++)
            {
                var child = new GameObject(name + "_LOD" + i);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var r = child.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { bark, leaves };
                lods[i] = new LOD(i == 0 ? near : cull, new Renderer[] { r });
            }
            var group = go.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(lods);
            group.RecalculateBounds();
            if (trunkRadius > 0f)
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = trunkRadius + 0.05f;
                capsule.height = trunkHeight;
                capsule.center = new Vector3(0f, trunkHeight / 2f, 0f);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Trees/" + name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        class MeshBuilder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> bark = new List<int>();
            public readonly List<int> leaves = new List<int>();
            readonly System.Random random;
            readonly bool coarse;   // LOD1: fewer sides and rings everywhere

            public MeshBuilder(int seed, bool coarse = false)
            {
                random = new System.Random(seed);
                this.coarse = coarse;
            }

            float Rand(float min, float max)
            {
                return min + (float)random.NextDouble() * (max - min);
            }

            public void Trunk(Vector3 bottom, float r0, float r1, float height, int sides = 7)
            {
                if (coarse) sides = Mathf.Max(4, sides / 2 + 1);
                int start = vertices.Count;
                for (int ring = 0; ring <= 1; ring++)
                {
                    float r = ring == 0 ? r0 : r1;
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s / (float)sides * Mathf.PI * 2f;
                        vertices.Add(bottom + new Vector3(Mathf.Cos(a) * r, ring * height, Mathf.Sin(a) * r));
                        uvs.Add(new Vector2(s / (float)sides, ring * height / 2f));
                    }
                }
                int row = sides + 1;
                for (int s = 0; s < sides; s++)
                {
                    int a = start + s, b = start + s + 1, c = start + row + s, d = start + row + s + 1;
                    bark.Add(a); bark.Add(c); bark.Add(b);
                    bark.Add(b); bark.Add(c); bark.Add(d);
                }
            }

            /// <summary>A lumpy low-poly sphere of foliage.</summary>
            public void Blob(Vector3 centre, Vector3 radii, float jitter, int lat = 6, int lon = 9)
            {
                if (coarse)
                {
                    lat = Mathf.Max(3, lat / 2 + 1);
                    lon = Mathf.Max(5, lon / 2 + 1);
                }
                int start = vertices.Count;
                for (int i = 0; i <= lat; i++)
                {
                    float v = i / (float)lat;
                    float theta = v * Mathf.PI;
                    for (int j = 0; j <= lon; j++)
                    {
                        float u = j / (float)lon;
                        float phi = u * Mathf.PI * 2f;
                        var dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), -Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                        float bump = (i == 0 || i == lat || j == lon) ? 1f : 1f + Rand(-jitter, jitter);
                        vertices.Add(centre + Vector3.Scale(dir, radii) * bump);
                        uvs.Add(new Vector2(u * 2f, v));
                    }
                }
                // Close the seam: the last column repeats the first column's position.
                for (int i = 0; i <= lat; i++)
                {
                    vertices[start + i * (lon + 1) + lon] = vertices[start + i * (lon + 1)];
                }
                for (int i = 0; i < lat; i++)
                {
                    for (int j = 0; j < lon; j++)
                    {
                        int a = start + i * (lon + 1) + j, b = a + 1, c = a + lon + 1, d = c + 1;
                        leaves.Add(a); leaves.Add(c); leaves.Add(b);
                        leaves.Add(b); leaves.Add(c); leaves.Add(d);
                    }
                }
            }

            /// <summary>An open cone of needles for the pine.</summary>
            public void Cone(Vector3 bottom, float radius, float height, int sides = 9)
            {
                int start = vertices.Count;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    float r = radius * (s == sides ? 1f : Rand(0.85f, 1.15f));
                    vertices.Add(bottom + new Vector3(Mathf.Cos(a) * r, Rand(-0.25f, 0.1f), Mathf.Sin(a) * r));
                    uvs.Add(new Vector2(s / (float)sides * 2f, 0f));
                }
                vertices[start + sides] = vertices[start];
                int tip = vertices.Count;
                vertices.Add(bottom + Vector3.up * height);
                uvs.Add(new Vector2(0.5f, 1f));
                int centre = vertices.Count;
                vertices.Add(bottom + Vector3.up * 0.3f);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int s = 0; s < sides; s++)
                {
                    leaves.Add(start + s); leaves.Add(tip); leaves.Add(start + s + 1);
                    leaves.Add(start + s); leaves.Add(start + s + 1); leaves.Add(centre);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = 2 };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(bark, 0);
                mesh.SetTriangles(leaves, 1);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // Neem / sheesham: short trunk, wide rounded crown, about 7.5 m.
        static Mesh BuildBroadleaf(bool coarse)
        {
            var b = new MeshBuilder(1, coarse);
            b.Trunk(Vector3.zero, 0.26f, 0.15f, 4.2f);
            b.Blob(new Vector3(0f, 5.2f, 0f), new Vector3(3.0f, 2.2f, 3.0f), 0.18f);
            b.Blob(new Vector3(1.6f, 4.5f, 0.7f), new Vector3(2.0f, 1.6f, 2.0f), 0.2f, 5, 8);
            b.Blob(new Vector3(-1.4f, 4.7f, -0.9f), new Vector3(2.1f, 1.7f, 2.0f), 0.2f, 5, 8);
            return b.ToMesh(coarse ? "Broadleaf_LOD1" : "Broadleaf");
        }

        // Eucalyptus: tall pale trunk, thin high crown, about 14 m.
        static Mesh BuildEucalyptus(bool coarse)
        {
            var b = new MeshBuilder(2, coarse);
            b.Trunk(Vector3.zero, 0.22f, 0.09f, 12f);
            b.Blob(new Vector3(0f, 11.5f, 0f), new Vector3(1.9f, 2.8f, 1.9f), 0.22f);
            b.Blob(new Vector3(0.8f, 9.2f, -0.4f), new Vector3(1.4f, 1.6f, 1.4f), 0.22f, 5, 8);
            b.Blob(new Vector3(-0.7f, 8.4f, 0.6f), new Vector3(1.1f, 1.3f, 1.1f), 0.22f, 5, 7);
            return b.ToMesh(coarse ? "Eucalyptus_LOD1" : "Eucalyptus");
        }

        // Chir pine as it grows on the hills here: a tall straight trunk bare for most of its
        // height and an open, uneven crown of flat clumps near the top (not a cone), about 13 m.
        static Mesh BuildPine(bool coarse)
        {
            var b = new MeshBuilder(3, coarse);
            b.Trunk(Vector3.zero, 0.24f, 0.08f, 11.5f);
            b.Blob(new Vector3(0f, 11.2f, 0f), new Vector3(1.7f, 1.0f, 1.6f), 0.25f, 5, 8);
            b.Blob(new Vector3(1.3f, 9.6f, 0.4f), new Vector3(1.5f, 0.75f, 1.2f), 0.25f, 4, 7);
            b.Blob(new Vector3(-1.1f, 8.8f, -0.6f), new Vector3(1.4f, 0.7f, 1.3f), 0.25f, 4, 7);
            b.Blob(new Vector3(0.3f, 7.6f, 1.2f), new Vector3(1.1f, 0.6f, 1.0f), 0.25f, 4, 7);
            return b.ToMesh(coarse ? "Pine_LOD1" : "Pine");
        }

        // Lantana / scrub bush, about 1.8 m.
        static Mesh BuildShrub(bool coarse)
        {
            var b = new MeshBuilder(4, coarse);
            b.Trunk(Vector3.zero, 0.08f, 0.05f, 0.5f, 5);
            b.Blob(new Vector3(0f, 0.85f, 0f), new Vector3(1.3f, 0.95f, 1.3f), 0.25f, 5, 8);
            b.Blob(new Vector3(0.8f, 0.6f, 0.4f), new Vector3(0.8f, 0.65f, 0.8f), 0.25f, 4, 7);
            return b.ToMesh(coarse ? "Shrub_LOD1" : "Shrub");
        }
    }
}
