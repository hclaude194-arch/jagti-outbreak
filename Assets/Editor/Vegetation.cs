using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Trees, bushes and grass clumps made from code (2026-11-21: the owner said the old trees and
    /// shrubs were "a giant horrible cube"; their crowns were a few lumpy spheres). Every plant
    /// now has a tapered, branching woody frame and a crown of leaf cards: quads cut out of a
    /// painted leaf texture (alpha cutout, drawn from both sides), placed at the branch tips the
    /// way leaves grow, with normals bent out from the crown's centre so a crown shades as one
    /// soft mass in the sun instead of as flat cards.
    ///
    /// Leaf textures are 2 x 2 atlases of 256 px tiles: two variants of the foliage in sunlight
    /// (top row in uv) and the same two in shade (bottom row), so cards deep in a crown or under
    /// it use the shaded tiles (cheap ambient occlusion). Their mip levels keep the share of
    /// opaque pixels (alpha-tested leaves otherwise thin out and vanish in the distance).
    ///
    /// The same leaf slots (LeafSlots) are used by the Blender street planting (Shrub_Border,
    /// Kerbed_Bed, Planter_Box, Young_Tree, Palm_Small, courtyard hedges): BuildingMaterials maps
    /// those slot names to LeafMaterial. tools/blender/plants.py mirrors the textures, card
    /// layout and plant shapes for Blender previews and the props, so change the two together.
    /// </summary>
    public static class Vegetation
    {
        const int Tile = 256;
        const float Pad = 0.03f;   // uv margin inside each atlas tile

        public enum Style { Broad, Euca, Needle, Shrub, Red, Flower, Frond, TallGrass, Hedge }

        /// <summary>Material slot names (Blender props) and the leaf style each one draws.</summary>
        public static readonly Dictionary<string, Style> LeafSlots = new Dictionary<string, Style>
        {
            { "Leaf_Broad", Style.Broad }, { "Leaf_Shrub", Style.Shrub }, { "Leaf_Red", Style.Red },
            { "Leaf_Flower", Style.Flower }, { "Palm_Frond", Style.Frond }, { "Leaf_Hedge", Style.Hedge },
            { "Leaf_Grass", Style.TallGrass },
        };

        static readonly Dictionary<Style, Texture2D> textures = new Dictionary<Style, Texture2D>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        // ------------------------------------------------------------------ materials

        /// <summary>Wind: (flutter, bend) per style; tall grass and shrubs bend more for their size.</summary>
        static Vector2 Wind(Style s)
        {
            switch (s)
            {
                case Style.Needle: return new Vector2(0.6f, 0.8f);
                case Style.Euca: return new Vector2(1f, 1.2f);
                case Style.Broad: return new Vector2(1f, 1f);
                case Style.TallGrass: return new Vector2(1f, 3f);
                case Style.Hedge: return new Vector2(0.3f, 0.3f);
                default: return new Vector2(0.8f, 2.5f);
            }
        }

        public static Material LeafMaterial(string slot)
        {
            Material m;
            if (materials.TryGetValue(slot, out m) && m != null) return m;
            Style style;
            if (!LeafSlots.TryGetValue(slot, out style)) style = Style.Shrub;
            var w = Wind(style);
            m = ProceduralAssets.FoliageMaterial("Leaves_" + slot, Color.white, LeafTexture(style), w.x, w.y, 0.45f);
            materials[slot] = m;
            return m;
        }

        static Material TreeLeaves(string name, Style style)
        {
            Material m;
            if (materials.TryGetValue(name, out m) && m != null) return m;
            var w = Wind(style);
            m = ProceduralAssets.FoliageMaterial(name, Color.white, LeafTexture(style), w.x, w.y, 0.45f);
            materials[name] = m;
            return m;
        }

        // ------------------------------------------------------------------ prefabs

        public static GameObject[] TreePrefabs()
        {
            var barkTex = ProceduralAssets.GroundTexture("Bark", new Color(0.6f, 0.58f, 0.55f), new Color(0.85f, 0.83f, 0.8f), Color.white, 4, 0.5f, 41, true);
            var bark = ProceduralAssets.FoliageMaterial("Tree_Bark", new Color(0.33f, 0.26f, 0.20f), barkTex, 0f, 1f);
            var paleBark = ProceduralAssets.FoliageMaterial("Tree_Bark_Eucalyptus", new Color(0.78f, 0.74f, 0.66f), barkTex, 0f, 1.2f);
            var pineBark = ProceduralAssets.FoliageMaterial("Tree_Bark_Pine", new Color(0.42f, 0.30f, 0.22f), barkTex, 0f, 0.8f);
            var stems = ProceduralAssets.FoliageMaterial("Shrub_Stems", new Color(0.30f, 0.25f, 0.18f), barkTex, 0f, 2.5f);

            ProceduralAssets.EnsureFolder(ProceduralAssets.Folder + "/Trees");
            return new[]
            {
                SaveTree("Tree_Broadleaf", Broadleaf(false), Broadleaf(true), bark, TreeLeaves("Tree_Leaves_Broadleaf", Style.Broad), 0.28f, 3.0f, 0.22f, 0.012f),
                SaveTree("Tree_Eucalyptus", Eucalyptus(false), Eucalyptus(true), paleBark, TreeLeaves("Tree_Leaves_Eucalyptus", Style.Euca), 0.24f, 9f, 0.25f, 0.01f),
                SaveTree("Tree_Pine", Pine(false), Pine(true), pineBark, TreeLeaves("Tree_Leaves_Pine", Style.Needle), 0.26f, 11f, 0.25f, 0.01f),
                SaveTree("Tree_Shrub", Shrub(false, 4), Shrub(true, 4), stems, TreeLeaves("Tree_Leaves_Shrub", Style.Shrub), 0f, 0f, 0.12f, 0.02f),
                SaveTree("Tree_Flower_Shrub", FlowerShrub(false), FlowerShrub(true), stems, TreeLeaves("Tree_Leaves_Flower", Style.Flower), 0f, 0f, 0.12f, 0.02f),
                SaveTree("Tree_Tall_Grass", TallGrass(false), TallGrass(true), stems, TreeLeaves("Tree_Leaves_Grass", Style.TallGrass), 0f, 0f, 0.1f, 0.025f),
            };
        }

        /// <summary>A prefab with two levels of detail (the realistic-graphics video's LOD lesson):
        /// the full plant close up, a coarse one once it is under `near` of the screen's height, and
        /// nothing under `cull`. The LOD children sit at the root, so the wind shader's pivot holds.</summary>
        static GameObject SaveTree(string name, Mesh mesh, Mesh coarse, Material bark, Material leaves,
                                   float trunkRadius, float trunkHeight, float near, float cull)
        {
            string folder = ProceduralAssets.Folder + "/Trees/";
            AssetDatabase.CreateAsset(mesh, folder + name + "_Mesh.asset");
            AssetDatabase.CreateAsset(coarse, folder + name + "_Mesh_LOD1.asset");
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
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, folder + name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ the plant builder

        class Plant
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> bark = new List<int>();
            readonly List<int> leaves = new List<int>();
            readonly System.Random random;
            public readonly bool coarse;
            public Vector3 crown;          // centre the leaf normals bend out from
            public Vector3 crownRadii = Vector3.one;

            public Plant(int seed, bool coarse)
            {
                random = new System.Random(seed);
                this.coarse = coarse;
            }

            public float R(float a, float b) { return a + (float)random.NextDouble() * (b - a); }

            public Vector3 Jitter(Vector3 dir, float amount)
            {
                return (dir + new Vector3(R(-amount, amount), R(-amount, amount), R(-amount, amount))).normalized;
            }

            /// <summary>A tapered tube along a path (bark), ring by ring.</summary>
            public void Tube(List<Vector3> path, float r0, float r1, int sides)
            {
                if (coarse) sides = Mathf.Max(4, sides / 2 + 1);
                int rings = path.Count;
                int start = verts.Count;
                float len = 0f;
                Vector3 prevSide = Vector3.zero;
                for (int k = 0; k < rings; k++)
                {
                    float t = k / (float)(rings - 1);
                    Vector3 dir = k < rings - 1 ? path[k + 1] - path[k] : path[k] - path[k - 1];
                    dir.Normalize();
                    // A side vector carried along the path so the rings don't twist.
                    Vector3 side = k == 0 ? Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized
                                          : Vector3.Cross(Vector3.Cross(dir, prevSide), dir).normalized;
                    prevSide = side;
                    Vector3 other = Vector3.Cross(dir, side);
                    float r = Mathf.Lerp(r0, r1, t);
                    if (k > 0) len += Vector3.Distance(path[k], path[k - 1]);
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s / (float)sides * Mathf.PI * 2f;
                        Vector3 n = side * Mathf.Cos(a) + other * Mathf.Sin(a);
                        verts.Add(path[k] + n * r);
                        normals.Add(n);
                        uvs.Add(new Vector2(s / (float)sides * Mathf.Max(1f, Mathf.Round(r * 12f)) * 0.5f, len * 0.6f));
                    }
                }
                int row = sides + 1;
                for (int k = 0; k < rings - 1; k++)
                {
                    for (int s = 0; s < sides; s++)
                    {
                        int a = start + k * row + s, b = a + 1, c = a + row, d = c + 1;
                        bark.Add(a); bark.Add(c); bark.Add(b);
                        bark.Add(b); bark.Add(c); bark.Add(d);
                    }
                }
            }

            /// <summary>A limb from start along dir: it wanders a little and bends towards `bend`
            /// (up for trees reaching for light, down for drooping ones). Returns its path.</summary>
            public List<Vector3> Limb(Vector3 start, Vector3 dir, float length, float r0, float r1, int segments,
                                      float wander, Vector3 bend, int sides)
            {
                if (coarse) segments = Mathf.Max(2, segments / 2 + 1);
                var path = new List<Vector3> { start };
                Vector3 p = start, d = dir.normalized;
                float step = length / segments;
                for (int k = 0; k < segments; k++)
                {
                    d = (d + bend * (1f / segments) + new Vector3(R(-wander, wander), R(-wander, wander) * 0.5f, R(-wander, wander))).normalized;
                    p += d * step;
                    path.Add(p);
                }
                if (r0 > 0.004f) Tube(path, r0, r1, sides);
                return path;
            }

            public static Vector3 At(List<Vector3> path, float t)
            {
                float f = Mathf.Clamp01(t) * (path.Count - 1);
                int i = Mathf.Min(path.Count - 2, Mathf.FloorToInt(f));
                return Vector3.Lerp(path[i], path[i + 1], f - i);
            }

            public static Vector3 DirAt(List<Vector3> path, float t)
            {
                float f = Mathf.Clamp01(t) * (path.Count - 1);
                int i = Mathf.Min(path.Count - 2, Mathf.FloorToInt(f));
                return (path[i + 1] - path[i]).normalized;
            }

            /// <summary>
            /// One leaf card: its base sits at `root` (a twig tip or a point on a branch) and it grows
            /// along `up`, facing roughly `face`. tile 0-1 picks the texture variant; shade picks
            /// the shaded row. Normals blend the card's own with the direction out of the crown.
            /// </summary>
            public void Card(Vector3 root, Vector3 up, Vector3 face, float width, float height, int tile, bool shade,
                             float bendOut = 0.75f)
            {
                up.Normalize();
                Vector3 n = (face - Vector3.Dot(face, up) * up);
                if (n.sqrMagnitude < 1e-4f) n = Vector3.Cross(up, Vector3.right);
                n.Normalize();
                Vector3 right = Vector3.Cross(up, n).normalized;
                // A slight fold down the middle (the card's midline sits proud), so cards seen edge-on
                // still have some body.
                Vector3 b = root - up * (height * 0.12f);
                var corners = new[]
                {
                    b - right * (width * 0.5f), b + right * (width * 0.5f),
                    b + up * height + right * (width * 0.5f), b + up * height - right * (width * 0.5f),
                    b + up * (height * 0.5f) + n * (width * 0.12f),
                };
                var cuv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f) };
                int start = verts.Count;
                for (int k = 0; k < 5; k++)
                {
                    Vector3 p = corners[k];
                    Vector3 outDir = Vector3.Scale(p - crown, new Vector3(1f / crownRadii.x, 1f / crownRadii.y, 1f / crownRadii.z));
                    if (outDir.sqrMagnitude < 1e-4f) outDir = Vector3.up;
                    outDir.Normalize();
                    Vector3 nn = Vector3.Lerp(n, outDir, bendOut).normalized;
                    nn = (nn + Vector3.up * 0.15f).normalized;    // skylight: leaves face the sky a little
                    verts.Add(p);
                    normals.Add(nn);
                    uvs.Add(AtlasUV(tile, shade, cuv[k]));
                }
                // Four triangles round the raised midpoint.
                int c = start + 4;
                int[] ring = { start, start + 1, start + 2, start + 3 };
                for (int k = 0; k < 4; k++)
                {
                    leaves.Add(ring[k]); leaves.Add(ring[(k + 1) % 4]); leaves.Add(c);
                }
            }

            /// <summary>A small spray of cards round a twig tip, fanned about the twig's direction.</summary>
            public void Cluster(Vector3 tip, Vector3 dir, int count, float size, float spread, bool shadeBelow)
            {
                for (int k = 0; k < count; k++)
                {
                    Vector3 up = Jitter((dir + Vector3.up * 0.35f).normalized, spread);
                    Vector3 face = Jitter(Vector3.Cross(up, new Vector3(R(-1f, 1f), R(-0.3f, 0.3f), R(-1f, 1f))).normalized, 0.6f);
                    Vector3 rel = tip - crown;
                    bool shade = shadeBelow && (rel.y < -0.25f * crownRadii.y
                                 || Vector3.Scale(rel, new Vector3(1f / crownRadii.x, 1f / crownRadii.y, 1f / crownRadii.z)).magnitude < 0.55f);
                    float s = size * R(0.8f, 1.15f);
                    Card(tip - up * (s * 0.1f), up, face, s, s, random.Next(2), shade);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = 2 };
                if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(bark, 0);
                mesh.SetTriangles(leaves, 1);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static Vector2 AtlasUV(int tile, bool shade, Vector2 uv)
        {
            float u0 = (tile & 1) * 0.5f, v0 = shade ? 0f : 0.5f;
            return new Vector2(u0 + (Pad + uv.x * (1f - 2f * Pad)) * 0.5f, v0 + (Pad + uv.y * (1f - 2f * Pad)) * 0.5f);
        }

        // ------------------------------------------------------------------ the plants

        /// <summary>Neem / sheesham, about 8 m: a short trunk forking into four or five limbs
        /// that spread into a broad, rounded, open crown of leaf sprays.</summary>
        static Mesh Broadleaf(bool coarse)
        {
            var p = new Plant(11, coarse);
            p.crown = new Vector3(0f, 5.4f, 0f);
            p.crownRadii = new Vector3(3.6f, 2.4f, 3.6f);
            var trunk = p.Limb(new Vector3(0f, -0.4f, 0f), new Vector3(0.08f, 1f, 0.03f), 3.3f, 0.32f, 0.22f, 4, 0.05f, Vector3.zero, 10);
            Vector3 fork = trunk[trunk.Count - 1];
            int limbs = 6;
            for (int i = 0; i < limbs; i++)
            {
                float a = i * Mathf.PI * 2f / limbs + p.R(-0.3f, 0.3f);
                float rise = p.R(0.75f, 1.25f);
                var dir = new Vector3(Mathf.Cos(a), rise, Mathf.Sin(a));
                float len = p.R(2.7f, 3.5f);
                var limb = p.Limb(fork - Vector3.up * p.R(0f, 0.5f), dir, len, 0.17f, 0.06f, 5, 0.12f, new Vector3(0f, 0.25f, 0f), 7);
                int twigs = coarse ? 0 : 5;
                for (int k = 0; k < twigs; k++)
                {
                    float t = 0.4f + 0.6f * k / Mathf.Max(1, twigs - 1);
                    Vector3 at = Plant.At(limb, t);
                    Vector3 d = p.Jitter(Plant.DirAt(limb, t) + new Vector3(p.R(-0.8f, 0.8f), p.R(0.1f, 0.7f), p.R(-0.8f, 0.8f)), 0.2f);
                    var twig = p.Limb(at, d, p.R(1.1f, 1.8f), 0.05f, 0.015f, 3, 0.2f, new Vector3(0f, 0.15f, 0f), 5);
                    Vector3 tip = twig[twig.Count - 1];
                    p.Cluster(tip, d, 5, p.R(1.7f, 2.1f), 0.5f, true);
                    p.Cluster(Plant.At(twig, 0.5f), d, 2, 1.6f, 0.6f, true);
                }
                if (coarse)
                {
                    for (int k = 0; k < 3; k++)
                        p.Cluster(Plant.At(limb, 0.55f + 0.22f * k) + Vector3.up * 0.6f, dir.normalized, 3, 2.8f, 0.6f, true);
                }
                else
                {
                    p.Cluster(limb[limb.Count - 1], dir.normalized, 3, 1.8f, 0.4f, true);
                }
            }
            // Shaded fill deep in the crown so it doesn't look hollow from below.
            int fill = coarse ? 6 : 26;
            for (int k = 0; k < fill; k++)
            {
                float a = p.R(0f, Mathf.PI * 2f);
                float r = p.R(0.6f, 2.2f);
                var at = p.crown + new Vector3(Mathf.Cos(a) * r, p.R(-0.9f, 0.6f), Mathf.Sin(a) * r);
                p.Card(at, p.Jitter(Vector3.up, 0.6f), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), coarse ? 2.6f : 1.8f, coarse ? 2.6f : 1.8f, k & 1, true);
            }
            return p.ToMesh(coarse ? "Broadleaf_LOD1" : "Broadleaf");
        }

        /// <summary>Eucalyptus, about 14 m: a tall pale trunk with a gentle sway, a few limbs
        /// reaching up and a thin, high crown of hanging leaf sprays.</summary>
        static Mesh Eucalyptus(bool coarse)
        {
            var p = new Plant(12, coarse);
            p.crown = new Vector3(0.4f, 11.0f, 0f);
            p.crownRadii = new Vector3(2.4f, 3.0f, 2.4f);
            var trunk = p.Limb(new Vector3(0f, -0.4f, 0f), new Vector3(0.05f, 1f, 0f), 10f, 0.27f, 0.12f, 7, 0.05f, new Vector3(0.05f, 0f, 0.02f), 9);
            for (int i = 0; i < 6; i++)
            {
                float t = 0.55f + 0.08f * i;
                float a = i * 2.4f;
                Vector3 at = Plant.At(trunk, t);
                var dir = new Vector3(Mathf.Cos(a) * 0.8f, 1f, Mathf.Sin(a) * 0.8f);
                var limb = p.Limb(at, dir, p.R(2.2f, 3.4f), 0.09f, 0.03f, 4, 0.12f, new Vector3(0f, 0.2f, 0f), 6);
                int sprays = coarse ? 3 : 7;
                for (int k = 0; k < sprays; k++)
                {
                    Vector3 sp = Plant.At(limb, 0.35f + 0.65f * k / Mathf.Max(1, sprays - 1));
                    // Hanging sprays: the card grows downwards from its twig.
                    var hang = p.Jitter(new Vector3(Mathf.Cos(a) * 0.5f, -1f, Mathf.Sin(a) * 0.5f), 0.4f);
                    float s = coarse ? 2.4f : p.R(1.3f, 1.7f);
                    p.Card(sp + Vector3.up * (s * 0.85f), hang, new Vector3(Mathf.Cos(a + 1.5f), 0f, Mathf.Sin(a + 1.5f)), s, s, k & 1, k == 0);
                    if (!coarse) p.Cluster(sp, p.Jitter(dir, 0.3f), 3, 1.5f, 0.55f, true);
                }
            }
            p.Cluster(trunk[trunk.Count - 1], Vector3.up, coarse ? 2 : 4, coarse ? 2.4f : 1.6f, 0.5f, false);
            return p.ToMesh(coarse ? "Eucalyptus_LOD1" : "Eucalyptus");
        }

        /// <summary>Chir pine as it grows on the hills here, about 13 m: a tall straight trunk bare
        /// for most of its height, then a few whorls of near-level branches ending in tufts of long
        /// needles, an open, flat-topped crown (never stacked cones).</summary>
        static Mesh Pine(bool coarse)
        {
            var p = new Plant(13, coarse);
            p.crown = new Vector3(0f, 10.2f, 0f);
            p.crownRadii = new Vector3(2.6f, 1.8f, 2.6f);
            var trunk = p.Limb(new Vector3(0f, -0.4f, 0f), new Vector3(0.02f, 1f, 0.01f), 12.3f, 0.29f, 0.07f, 8, 0.03f, Vector3.zero, 10);
            int whorls = 5;
            for (int w = 0; w < whorls; w++)
            {
                float t = 0.58f + 0.4f * w / (whorls - 1);
                Vector3 at = Plant.At(trunk, t);
                int n = w == whorls - 1 ? 3 : 4;
                float len = Mathf.Lerp(3.0f, 1.2f, w / (float)(whorls - 1));
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n + w * 0.9f + p.R(-0.3f, 0.3f);
                    var dir = new Vector3(Mathf.Cos(a), p.R(-0.15f, 0.15f), Mathf.Sin(a));
                    var limb = p.Limb(at, dir, len * p.R(0.85f, 1.15f), 0.08f, 0.025f, 4, 0.08f, new Vector3(0f, 0.35f, 0f), 6);
                    int tufts = coarse ? 2 : 4;
                    for (int k = 0; k < tufts; k++)
                    {
                        Vector3 tp = Plant.At(limb, 1f - 0.2f * k);
                        // Needle tufts: cards lying almost flat (the flat open crown) and one upright.
                        float s = coarse ? 2.4f : p.R(1.6f, 2.1f);
                        var flatUp = p.Jitter(new Vector3(dir.x, 0.25f, dir.z), 0.25f);
                        p.Card(tp - flatUp * (s * 0.3f), flatUp, Vector3.up, s, s, k & 1, w < 2);
                        if (!coarse) p.Card(tp, p.Jitter(Vector3.up + dir * 0.3f, 0.3f), new Vector3(-dir.z, 0f, dir.x), s * 0.8f, s * 0.8f, (k + 1) & 1, w < 2);
                    }
                }
            }
            p.Cluster(trunk[trunk.Count - 1], Vector3.up, coarse ? 2 : 4, coarse ? 2.0f : 1.4f, 0.5f, false);
            return p.ToMesh(coarse ? "Pine_LOD1" : "Pine");
        }

        /// <summary>Lantana / scrub bush, about 1.6 m: thin stems fanning from the root and a dense,
        /// lumpy dome of leaf sprays, a little open at the bottom so the stems show.</summary>
        static Mesh Shrub(bool coarse, int seed)
        {
            var p = new Plant(seed, coarse);
            p.crown = new Vector3(0f, 0.7f, 0f);
            p.crownRadii = new Vector3(1.15f, 0.85f, 1.15f);
            Dome(p, coarse, 1.15f, 0.85f, 0.75f, coarse ? 14 : 60, coarse ? 1.35f : 0.95f);
            return p.ToMesh((coarse ? "Shrub_LOD1_" : "Shrub_") + seed);
        }

        /// <summary>Flowering bush (bougainvillea and flowering lantana, magenta and pink in the
        /// campus beds), lower and wider than the scrub bush.</summary>
        static Mesh FlowerShrub(bool coarse)
        {
            var p = new Plant(21, coarse);
            p.crown = new Vector3(0f, 0.55f, 0f);
            p.crownRadii = new Vector3(1.3f, 0.7f, 1.3f);
            Dome(p, coarse, 1.3f, 0.7f, 0.6f, coarse ? 14 : 62, coarse ? 1.4f : 0.95f);
            return p.ToMesh(coarse ? "FlowerShrub_LOD1" : "FlowerShrub");
        }

        static void Dome(Plant p, bool coarse, float rx, float ry, float cy, int cards, float size)
        {
            int stems = coarse ? 3 : 7;
            var tips = new List<Vector3>();
            for (int i = 0; i < stems; i++)
            {
                float a = i * 2.4f;
                var dir = new Vector3(Mathf.Cos(a) * 0.6f, 1f, Mathf.Sin(a) * 0.6f);
                var stem = p.Limb(new Vector3(0f, -0.15f, 0f), dir, p.R(0.8f, 1.2f) * (ry + cy) * 0.8f, 0.035f, 0.01f, 3, 0.25f, Vector3.zero, 5);
                tips.Add(stem[stem.Count - 1]);
            }
            for (int k = 0; k < cards; k++)
            {
                // Points over the dome (golden-angle spiral), pushed in and out so it is lumpy.
                float f = (k + 0.5f) / cards;
                float el = Mathf.Lerp(-0.45f, 1f, Mathf.Sqrt(f));             // more cards high up
                float az = k * 2.39996f;
                float rr = Mathf.Sqrt(Mathf.Max(0f, 1f - el * el));
                var dirOut = new Vector3(Mathf.Cos(az) * rr, el, Mathf.Sin(az) * rr);
                float lump = p.R(0.72f, 1.0f);
                var at = new Vector3(dirOut.x * rx * lump, cy + dirOut.y * ry * lump, dirOut.z * rx * lump);
                var up = p.Jitter(dirOut * 0.6f + Vector3.up * 0.5f, 0.35f);
                var face = p.Jitter(dirOut, 0.5f);
                bool shade = el < -0.1f || lump < 0.76f;
                float s = size * p.R(0.85f, 1.15f);
                p.Card(at - up * (s * 0.45f), up, face, s, s, k & 1, shade, 0.8f);
            }
        }

        /// <summary>Kans grass (Saccharum spontaneum), the tall grass of the banks and ravines round
        /// Jammu, about 1.8 m: a fountain of arching blades with silvery plumes.</summary>
        static Mesh TallGrass(bool coarse)
        {
            var p = new Plant(31, coarse);
            p.crown = new Vector3(0f, 0.2f, 0f);
            p.crownRadii = new Vector3(1f, 1f, 1f);
            int cards = coarse ? 3 : 9;
            for (int k = 0; k < cards; k++)
            {
                float a = k * Mathf.PI / cards + p.R(-0.15f, 0.15f);
                var lean = new Vector3(Mathf.Cos(a * 2f) * p.R(0.1f, 0.35f), 1f, Mathf.Sin(a * 2f) * p.R(0.1f, 0.35f));
                float h = p.R(1.5f, 2.0f);
                p.Card(new Vector3(p.R(-0.1f, 0.1f), -0.05f + h * 0.12f, p.R(-0.1f, 0.1f)), lean,
                       new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), h * 0.75f, h, k & 1, false, 0.5f);
            }
            return p.ToMesh(coarse ? "TallGrass_LOD1" : "TallGrass");
        }

        // ------------------------------------------------------------------ leaf textures

        public static Texture2D LeafTexture(Style style)
        {
            Texture2D t;
            if (textures.TryGetValue(style, out t) && t != null) return t;
            int size = Tile * 2;
            var px = new Color[size * size];
            for (int tile = 0; tile < 2; tile++)
            {
                var tilepx = new Color[Tile * Tile];
                PaintTile(style, tilepx, 101 + 17 * (int)style + tile);
                for (int row = 0; row < 2; row++)
                {
                    bool shade = row == 0;   // uv v 0-0.5 is the shaded row
                    for (int y = 0; y < Tile; y++)
                        for (int x = 0; x < Tile; x++)
                        {
                            var c = tilepx[y * Tile + x];
                            if (shade) c = new Color(c.r * 0.68f, c.g * 0.72f, c.b * 0.76f, c.a);
                            px[(row * Tile + y) * size + tile * Tile + x] = c;
                        }
                }
            }
            t = SaveCutout("Leaves_" + style, size, px, 0.45f);
            textures[style] = t;
            return t;
        }

        // A tiny raster painter: each leaf is a lens-shaped blade in its own frame, shaded lighter
        // along the midrib and towards the tip, darker at the base and edges.
        class Painter
        {
            public readonly Color[] px;
            public readonly int size;
            public readonly System.Random rnd;

            public Painter(Color[] px, int size, int seed)
            {
                this.px = px;
                this.size = size;
                rnd = new System.Random(seed);
            }

            public float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

            public void Leaf(float bx, float by, float angle, float len, float wid, Color colour, float roundness = 0.8f,
                             float vein = 0.18f)
            {
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                float reach = len + wid;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(bx - reach)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(bx + reach));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(by - reach)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(by + reach));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - bx, dy = y + 0.5f - by;
                        float s = dx * ca + dy * sa;          // along the leaf
                        float q = -dx * sa + dy * ca;         // across
                        if (s < 0f || s > len) continue;
                        float t = s / len;
                        float half = wid * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.85f)), roundness);
                        if (Mathf.Abs(q) > half) continue;
                        float edge = half > 0.01f ? Mathf.Abs(q) / half : 1f;
                        float shade = 0.78f + 0.3f * t - 0.18f * edge * edge;
                        if (edge < vein && t < 0.92f) shade += 0.16f;          // midrib
                        if (q > 0f) shade *= 0.93f;                              // one half catches less light
                        var c = colour * shade;
                        c.a = 1f;
                        px[y * size + x] = c;
                    }
                }
            }

            public void Line(float x0, float y0, float x1, float y1, float w0, float w1, Color colour)
            {
                float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                int steps = Mathf.Max(2, Mathf.CeilToInt(len * 1.5f));
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    float cx = Mathf.Lerp(x0, x1, t), cy = Mathf.Lerp(y0, y1, t), r = Mathf.Lerp(w0, w1, t) * 0.5f;
                    for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                        for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                        {
                            if (x < 0 || y < 0 || x >= size || y >= size) continue;
                            float ddx = x + 0.5f - cx, ddy = y + 0.5f - cy;
                            if (ddx * ddx + ddy * ddy > r * r + 0.25f) continue;
                            var c = colour;
                            c.a = 1f;
                            px[y * size + x] = c;
                        }
                }
            }

            public void Blob(float cx, float cy, float r, Color colour)
            {
                for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                    for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                    {
                        if (x < 0 || y < 0 || x >= size || y >= size) continue;
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                        if (d > 1f) continue;
                        var c = colour * (1.05f - 0.25f * d);
                        c.a = 1f;
                        px[y * size + x] = c;
                    }
            }

            public Color Vary(Color c, float amount)
            {
                float k = 1f + R(-amount, amount);
                return new Color(Mathf.Clamp01(c.r * k * R(0.94f, 1.06f)), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k * R(0.9f, 1.1f)), 1f);
            }
        }

        static void PaintTile(Style style, Color[] px, int seed)
        {
            var p = new Painter(px, Tile, seed);
            float S = Tile;
            var twig = new Color(0.30f, 0.24f, 0.16f);
            switch (style)
            {
                case Style.Broad:
                {
                    // Neem: compound leaves of narrow, pointed leaflets in pairs along each stalk,
                    // fanning up and out from the twig at the bottom of the card.
                    var green = new Color(0.28f, 0.47f, 0.16f);
                    int stalks = 9;
                    for (int k = 0; k < stalks; k++)
                    {
                        float a = Mathf.Lerp(0.35f, Mathf.PI - 0.35f, (k + p.R(0.2f, 0.8f)) / stalks);
                        float len = S * p.R(0.5f, 0.78f);
                        float bx = S * 0.5f + p.R(-10f, 10f), by = S * 0.06f;
                        float ex = bx + Mathf.Cos(a) * len, ey = by + Mathf.Sin(a) * len;
                        p.Line(bx, by, ex, ey, 3f, 1.2f, twig);
                        var tone = p.Vary(green, 0.15f);
                        int pairs = 8;
                        for (int j = 1; j <= pairs; j++)
                        {
                            float t = j / (pairs + 0.6f);
                            float lx = Mathf.Lerp(bx, ex, t), ly = Mathf.Lerp(by, ey, t);
                            float ll = S * 0.16f * (1.1f - 0.4f * t) * p.R(0.85f, 1.1f);
                            for (int side = -1; side <= 1; side += 2)
                                p.Leaf(lx, ly, a + side * p.R(0.75f, 1.05f), ll, ll * 0.38f, p.Vary(tone, 0.08f), 0.9f);
                        }
                        p.Leaf(ex, ey, a + p.R(-0.1f, 0.1f), S * 0.11f, S * 0.035f, p.Vary(tone, 0.08f), 0.9f);
                    }
                    break;
                }
                case Style.Euca:
                {
                    // Eucalyptus: long, narrow, sickle-shaped grey-green leaves hanging from thin twigs.
                    var green = new Color(0.42f, 0.50f, 0.36f);
                    for (int k = 0; k < 8; k++)
                    {
                        float bx = S * (0.1f + 0.8f * k / 7f) + p.R(-8f, 8f), by = S * 0.05f;
                        float ex = bx + p.R(-25f, 25f), ey = S * p.R(0.75f, 0.95f);
                        p.Line(bx, by, ex, ey, 2.2f, 1f, twig);
                        for (int j = 0; j < 9; j++)
                        {
                            float t = 0.12f + 0.85f * j / 8f;
                            float lx = Mathf.Lerp(bx, ex, t), ly = Mathf.Lerp(by, ey, t);
                            float a = Mathf.PI / 2f + (j % 2 == 0 ? 1f : -1f) * p.R(0.35f, 0.9f);
                            p.Leaf(lx, ly, a, S * p.R(0.18f, 0.26f), S * p.R(0.035f, 0.05f), p.Vary(green, 0.12f), 0.7f);
                        }
                    }
                    break;
                }
                case Style.Needle:
                {
                    // Chir pine: bunches of long, thin needles (in threes) bursting from a stout
                    // branchlet, drooping a little at their ends.
                    var green = new Color(0.27f, 0.40f, 0.18f);
                    float bx = S * 0.5f, by = S * 0.04f, ex = S * 0.5f + p.R(-10f, 10f), ey = S * 0.55f;
                    p.Line(bx, by, ex, ey, 5f, 3f, twig);
                    for (int f = 0; f < 60; f++)
                    {
                        float t = p.R(0.2f, 1f);
                        float fx = Mathf.Lerp(bx, ex, t), fy = Mathf.Lerp(by, ey, t);
                        float a0 = Mathf.PI / 2f + p.R(-1.3f, 1.3f);
                        var tone = p.Vary(green, 0.18f);
                        for (int n = 0; n < 3; n++)
                        {
                            float a = a0 + p.R(-0.12f, 0.12f);
                            float len = S * p.R(0.25f, 0.4f);
                            float mx = fx + Mathf.Cos(a) * len * 0.6f, my = fy + Mathf.Sin(a) * len * 0.6f;
                            float droop = a > Mathf.PI / 2f ? 0.25f : -0.25f;
                            p.Line(fx, fy, mx, my, 2.3f, 2f, tone);
                            p.Line(mx, my, mx + Mathf.Cos(a + droop) * len * 0.4f, Mathf.Min(S - 2, my + Mathf.Sin(a + droop) * len * 0.4f), 1.6f, 1f, tone * 1.1f);
                        }
                    }
                    break;
                }
                case Style.Shrub:
                case Style.Red:
                case Style.Flower:
                case Style.Hedge:
                {
                    // Small rounded leaves packed into a rough mound: lantana, acalypha, duranta.
                    Color[] palette;
                    if (style == Style.Red)
                        palette = new[] { new Color(0.55f, 0.15f, 0.11f), new Color(0.66f, 0.26f, 0.12f), new Color(0.45f, 0.12f, 0.14f), new Color(0.36f, 0.33f, 0.14f) };
                    else if (style == Style.Hedge)
                        palette = new[] { new Color(0.21f, 0.41f, 0.14f), new Color(0.26f, 0.48f, 0.16f), new Color(0.32f, 0.53f, 0.19f) };
                    else
                        palette = new[] { new Color(0.27f, 0.47f, 0.16f), new Color(0.34f, 0.54f, 0.19f), new Color(0.39f, 0.52f, 0.17f) };
                    bool fill = style == Style.Hedge;
                    for (int k = 0; k < 7; k++)
                    {
                        float a = Mathf.Lerp(0.5f, Mathf.PI - 0.5f, k / 6f);
                        p.Line(S * 0.5f, S * 0.02f, S * 0.5f + Mathf.Cos(a) * S * 0.36f, S * 0.02f + Mathf.Sin(a) * S * 0.6f, 2.5f, 1f, twig);
                    }
                    int count = fill ? 560 : 460;
                    for (int k = 0; k < count; k++)
                    {
                        float x, y;
                        if (fill)
                        {
                            x = p.R(0.04f, 0.96f) * S;
                            y = p.R(0.04f, 0.96f) * S;
                        }
                        else
                        {
                            // A mound: wide at the bottom third, round on top, ragged at the edge.
                            float a = p.R(0f, Mathf.PI * 2f), r = Mathf.Sqrt(p.R(0f, 1f));
                            x = S * (0.5f + Mathf.Cos(a) * r * 0.44f);
                            y = S * (0.5f + Mathf.Sin(a) * r * 0.42f);
                        }
                        float len = S * p.R(0.065f, 0.095f);
                        p.Leaf(x, y, p.R(0f, Mathf.PI * 2f), len, len * p.R(0.5f, 0.65f), p.Vary(palette[p.rnd.Next(palette.Length)], 0.12f), 0.65f, 0.12f);
                    }
                    if (style == Style.Flower)
                    {
                        // Bougainvillea: bunches of papery magenta bracts; lantana heads in pink and yellow.
                        var colours = new[] { new Color(0.86f, 0.16f, 0.55f), new Color(0.92f, 0.30f, 0.62f), new Color(0.80f, 0.12f, 0.45f), new Color(0.98f, 0.70f, 0.25f) };
                        for (int k = 0; k < 40; k++)
                        {
                            float a = p.R(0f, Mathf.PI * 2f), r = Mathf.Sqrt(p.R(0.1f, 1f));
                            float cx = S * (0.5f + Mathf.Cos(a) * r * 0.4f), cy = S * (0.52f + Mathf.Sin(a) * r * 0.38f);
                            var c = colours[p.rnd.Next(colours.Length)];
                            for (int b = 0; b < 6; b++)
                            {
                                float ba = b * 1.05f + p.R(-0.3f, 0.3f);
                                p.Leaf(cx, cy, ba, S * p.R(0.045f, 0.06f), S * 0.045f, p.Vary(c, 0.1f), 0.5f, 0f);
                            }
                            p.Blob(cx, cy, 2.2f, new Color(0.95f, 0.92f, 0.7f));
                        }
                    }
                    break;
                }
                case Style.Frond:
                {
                    // A palm frond lying along the card (u 0 at the stalk to 1 at the tip): a curved
                    // midrib with long narrow leaflets on both sides, shorter towards the tip.
                    var green = new Color(0.27f, 0.44f, 0.16f);
                    float y0 = S * 0.5f;
                    float prevX = S * 0.02f, prevY = y0;
                    for (int j = 1; j <= 40; j++)
                    {
                        float t = j / 40f;
                        float x = S * (0.02f + 0.95f * t), y = y0 + S * 0.08f * Mathf.Sin(t * 2.6f) * 0.5f;
                        p.Line(prevX, prevY, x, y, 3.5f * (1.1f - t), 3.5f * (1.1f - t), new Color(0.40f, 0.42f, 0.20f));
                        prevX = x; prevY = y;
                        if (t < 0.12f) continue;
                        float len = S * 0.42f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, (t - 0.05f) * 1.05f)) * p.R(0.85f, 1.05f);
                        for (int side = -1; side <= 1; side += 2)
                            p.Leaf(x, y, side * p.R(1.05f, 1.3f), len * 0.62f, S * 0.028f, p.Vary(green, 0.12f), 0.6f, 0.2f);
                    }
                    break;
                }
                case Style.TallGrass:
                {
                    // Kans grass: arching blades from the root at the bottom middle, and feathery
                    // silver-white plumes on tall stems.
                    var blade = new Color(0.56f, 0.60f, 0.31f);
                    for (int k = 0; k < 46; k++)
                    {
                        float bx = S * 0.5f + p.R(-18f, 18f);
                        float h = S * p.R(0.45f, 0.9f);
                        float lean = p.R(-0.9f, 0.9f);
                        var tone = p.Vary(blade, 0.2f);
                        float px0 = bx, py0 = 0f;
                        for (int s2 = 1; s2 <= 10; s2++)
                        {
                            float t = s2 / 10f;
                            float x = bx + lean * S * 0.45f * t * t, y = h * (t - 0.25f * t * t * Mathf.Abs(lean));
                            float w = 4.5f * (1f - t * 0.85f);
                            p.Line(px0, py0, x, y, w, w, tone * (0.75f + 0.35f * t));
                            px0 = x; py0 = y;
                        }
                    }
                    var plume = new Color(0.93f, 0.91f, 0.85f);
                    for (int k = 0; k < 5; k++)
                    {
                        float x = S * (0.3f + 0.4f * k / 4f) + p.R(-6f, 6f);
                        float top = S * p.R(0.86f, 0.97f);
                        p.Line(S * 0.5f, 0f, x, top * 0.7f, 2f, 1.5f, new Color(0.62f, 0.60f, 0.42f));
                        for (int f = 0; f < 70; f++)
                        {
                            float t = p.R(0f, 1f);
                            float fy = Mathf.Lerp(top * 0.68f, top, t), fx = x + p.R(-1f, 1f) * (1f - t) * 3f;
                            float a = Mathf.PI / 2f + p.R(-0.9f, 0.9f);
                            p.Line(fx, fy, fx + Mathf.Cos(a) * p.R(6f, 14f), Mathf.Min(S - 1, fy + Mathf.Sin(a) * p.R(4f, 10f)), 1.3f, 0.8f, p.Vary(plume, 0.06f));
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Saves an alpha-cut texture with hand-made mip levels: colour averaged over the opaque
        /// pixels only (no dark fringe from the transparent background), and alpha scaled so each
        /// level keeps the share of pixels above the cutoff that the full size has.
        /// </summary>
        static Texture2D SaveCutout(string name, int size, Color[] px, float cutoff)
        {
            // Fill the transparent pixels with the average leaf colour (bilinear filtering blends them in).
            Color sum = Color.clear;
            int n = 0;
            foreach (var c in px) if (c.a > 0.5f) { sum += c; n++; }
            var fillColour = n > 0 ? sum / n : new Color(0.3f, 0.4f, 0.2f);
            fillColour.a = 0f;
            for (int i = 0; i < px.Length; i++) if (px[i].a <= 0.5f) px[i] = fillColour;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name };
            float coverage = Coverage(px, cutoff, 1f);
            var level = px;
            int s = size;
            for (int mip = 0; mip < tex.mipmapCount; mip++)
            {
                if (mip > 0)
                {
                    int ns = Mathf.Max(1, s / 2);
                    var next = new Color[ns * ns];
                    for (int y = 0; y < ns; y++)
                        for (int x = 0; x < ns; x++)
                        {
                            Color c = Color.clear;
                            float a = 0f, wsum = 0f;
                            for (int dy = 0; dy < 2; dy++)
                                for (int dx = 0; dx < 2; dx++)
                                {
                                    var q = level[Mathf.Min(s - 1, y * 2 + dy) * s + Mathf.Min(s - 1, x * 2 + dx)];
                                    float w = 0.05f + q.a;
                                    c += q * w;
                                    wsum += w;
                                    a += q.a;
                                }
                            c /= wsum;
                            c.a = a / 4f;
                            next[y * ns + x] = c;
                        }
                    level = next;
                    s = ns;
                    // Scale alpha so this level keeps the full-size coverage.
                    float lo = 0.5f, hi = 4f;
                    for (int it = 0; it < 12; it++)
                    {
                        float mid = (lo + hi) / 2f;
                        if (Coverage(level, cutoff, mid) < coverage) lo = mid; else hi = mid;
                    }
                    float k = (lo + hi) / 2f;
                    for (int i = 0; i < level.Length; i++) level[i].a = Mathf.Clamp01(level[i].a * k);
                }
                tex.SetPixels(level, mip);
            }
            tex.Apply(false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 2;
            try
            {
                EditorUtility.CompressTexture(tex, TextureFormat.DXT5, TextureCompressionQuality.Normal);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Jagti] Texture compression skipped for " + name + ": " + e.Message);
            }
            ProceduralAssets.EnsureFolder(ProceduralAssets.Folder + "/Textures");
            AssetDatabase.CreateAsset(tex, ProceduralAssets.Folder + "/Textures/" + name + ".asset");
            return tex;
        }

        static float Coverage(Color[] px, float cutoff, float scale)
        {
            int n = 0;
            foreach (var c in px) if (c.a * scale > cutoff) n++;
            return n / (float)px.Length;
        }

        /// <summary>Forget the cached materials and textures (the generated folder was reset).</summary>
        public static void Reset()
        {
            textures.Clear();
            materials.Clear();
        }
    }
}
