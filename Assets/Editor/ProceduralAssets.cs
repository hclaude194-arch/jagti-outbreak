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
        static float Noise(float x, float y, int period, int seed)
        {
            return Noise(x, y, period, period, seed);
        }

        static float Noise(float x, float y, int periodX, int periodY, int seed)
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

        static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
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
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 210, 170, 0);
            var random = new System.Random(11);
            for (int blade = 0; blade < 18; blade++)
            {
                float baseX = 10 + (float)random.NextDouble() * (size - 20);
                float height = size * (0.45f + 0.55f * (float)random.NextDouble());
                float lean = ((float)random.NextDouble() - 0.5f) * 30f;
                float width = 2.5f + (float)random.NextDouble() * 2.5f;
                for (int y = 0; y < height; y++)
                {
                    float t = y / height;
                    float cx = baseX + lean * t * t;
                    float half = width * (1f - t);
                    float shade = 0.55f + 0.45f * t;
                    for (int x = Mathf.FloorToInt(cx - half); x <= Mathf.CeilToInt(cx + half); x++)
                    {
                        if (x < 0 || x >= size) continue;
                        pixels[y * size + x] = new Color(0.85f * shade, 0.9f * shade, 0.7f * shade, 1f);
                    }
                }
            }
            return SaveTexture("Grass_Blades", size, pixels, true);
        }

        static Texture2D SaveTexture(string name, int size, Color32[] pixels, bool alpha)
        {
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

        // ------------------------------------------------------------------ trees

        public enum TreeKind { Broadleaf = 0, Eucalyptus = 1, Pine = 2, Shrub = 3 }

        /// <summary>Makes the four tree prefabs, in TreeKind order.</summary>
        public static GameObject[] TreePrefabs()
        {
            var leafTex = GroundTexture("Leaves", new Color(0.55f, 0.6f, 0.5f), new Color(0.85f, 0.88f, 0.8f), Color.white, 8, 0.6f, 40);
            var barkTex = GroundTexture("Bark", new Color(0.6f, 0.58f, 0.55f), new Color(0.85f, 0.83f, 0.8f), Color.white, 4, 0.5f, 41, true);

            var bark = LitMaterial("Tree_Bark", new Color(0.36f, 0.28f, 0.21f), barkTex);
            var paleBark = LitMaterial("Tree_Bark_Eucalyptus", new Color(0.76f, 0.72f, 0.64f), barkTex);
            var leavesBroad = LitMaterial("Tree_Leaves_Broadleaf", new Color(0.24f, 0.36f, 0.13f), leafTex);
            var leavesEuca = LitMaterial("Tree_Leaves_Eucalyptus", new Color(0.40f, 0.48f, 0.31f), leafTex);
            var leavesPine = LitMaterial("Tree_Leaves_Pine", new Color(0.17f, 0.28f, 0.15f), leafTex);
            var leavesShrub = LitMaterial("Tree_Leaves_Shrub", new Color(0.36f, 0.40f, 0.18f), leafTex);

            EnsureFolder(Folder + "/Trees");
            return new[]
            {
                SaveTree("Tree_Broadleaf", BuildBroadleaf(), bark, leavesBroad, 0.25f, 3.5f),
                SaveTree("Tree_Eucalyptus", BuildEucalyptus(), paleBark, leavesEuca, 0.2f, 10f),
                SaveTree("Tree_Pine", BuildPine(), bark, leavesPine, 0.22f, 8f),
                SaveTree("Tree_Shrub", BuildShrub(), bark, leavesShrub, 0f, 0f),
            };
        }

        static GameObject SaveTree(string name, Mesh mesh, Material bark, Material leaves, float trunkRadius, float trunkHeight)
        {
            AssetDatabase.CreateAsset(mesh, Folder + "/Trees/" + name + "_Mesh.asset");
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { bark, leaves };
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

            public MeshBuilder(int seed)
            {
                random = new System.Random(seed);
            }

            float Rand(float min, float max)
            {
                return min + (float)random.NextDouble() * (max - min);
            }

            public void Trunk(Vector3 bottom, float r0, float r1, float height, int sides = 7)
            {
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
        static Mesh BuildBroadleaf()
        {
            var b = new MeshBuilder(1);
            b.Trunk(Vector3.zero, 0.26f, 0.15f, 4.2f);
            b.Blob(new Vector3(0f, 5.2f, 0f), new Vector3(3.0f, 2.2f, 3.0f), 0.18f);
            b.Blob(new Vector3(1.6f, 4.5f, 0.7f), new Vector3(2.0f, 1.6f, 2.0f), 0.2f, 5, 8);
            b.Blob(new Vector3(-1.4f, 4.7f, -0.9f), new Vector3(2.1f, 1.7f, 2.0f), 0.2f, 5, 8);
            return b.ToMesh("Broadleaf");
        }

        // Eucalyptus: tall pale trunk, thin high crown, about 14 m.
        static Mesh BuildEucalyptus()
        {
            var b = new MeshBuilder(2);
            b.Trunk(Vector3.zero, 0.22f, 0.09f, 12f);
            b.Blob(new Vector3(0f, 11.5f, 0f), new Vector3(1.9f, 2.8f, 1.9f), 0.22f);
            b.Blob(new Vector3(0.8f, 9.2f, -0.4f), new Vector3(1.4f, 1.6f, 1.4f), 0.22f, 5, 8);
            b.Blob(new Vector3(-0.7f, 8.4f, 0.6f), new Vector3(1.1f, 1.3f, 1.1f), 0.22f, 5, 7);
            return b.ToMesh("Eucalyptus");
        }

        // Chir pine: stacked cones on a straight trunk, about 12 m.
        static Mesh BuildPine()
        {
            var b = new MeshBuilder(3);
            b.Trunk(Vector3.zero, 0.24f, 0.07f, 10f);
            b.Cone(new Vector3(0f, 3.4f, 0f), 2.5f, 3.6f);
            b.Cone(new Vector3(0f, 5.4f, 0f), 2.1f, 3.4f);
            b.Cone(new Vector3(0f, 7.3f, 0f), 1.6f, 3.2f);
            b.Cone(new Vector3(0f, 9.1f, 0f), 1.0f, 2.9f);
            return b.ToMesh("Pine");
        }

        // Lantana / scrub bush, about 1.8 m.
        static Mesh BuildShrub()
        {
            var b = new MeshBuilder(4);
            b.Trunk(Vector3.zero, 0.08f, 0.05f, 0.5f, 5);
            b.Blob(new Vector3(0f, 0.85f, 0f), new Vector3(1.3f, 0.95f, 1.3f), 0.25f, 5, 8);
            b.Blob(new Vector3(0.8f, 0.6f, 0.4f), new Vector3(0.8f, 0.65f, 0.8f), 0.25f, 4, 7);
            return b.ToMesh("Shrub");
        }
    }
}
