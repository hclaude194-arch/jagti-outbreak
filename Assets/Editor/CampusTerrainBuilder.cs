using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Jagti.EditorTools
{
    /// <summary>Terrain settings and tree list from Assets/Data/campus_terrain.json.</summary>
    [Serializable]
    public class TerrainInfo
    {
        public int heightmapResolution;
        public float sizeX;
        public float sizeZ;
        public float minX;
        public float minZ;
        public float minHeight;
        public float heightRange;
        public int splatResolution;
        public int grassResolution;
        public float[] trees; // flat list: x, z, type, scale
    }

    /// <summary>
    /// Builds the Unity Terrain from the files written by tools/layout/generate_terrain.py:
    /// heights, painted ground layers, grass tufts and trees.
    /// </summary>
    public static class CampusTerrainBuilder
    {
        const string DataFolder = "Assets/Data/";
        const string TerrainFolder = ProceduralAssets.Folder + "/Terrain";

        public static Terrain Build(Transform parent)
        {
            var infoAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(DataFolder + "campus_terrain.json");
            if (infoAsset == null)
            {
                Debug.LogError("[Jagti] Missing campus_terrain.json. Run tools/layout/generate_terrain.py.");
                return null;
            }
            var info = JsonUtility.FromJson<TerrainInfo>(infoAsset.text);
            ProceduralAssets.EnsureFolder(TerrainFolder);

            int res = info.heightmapResolution;
            var data = new TerrainData { name = "Campus_TerrainData" };
            data.heightmapResolution = res;           // resolution first, then size
            data.size = new Vector3(info.sizeX, info.heightRange, info.sizeZ);
            data.SetHeights(0, 0, ReadHeights(DataFolder + "terrain_height.bytes", res));

            // Ground paint: four layers, weights per texel from the generator.
            data.terrainLayers = MakeLayers();
            data.alphamapResolution = info.splatResolution;
            data.baseMapResolution = 512;
            data.SetAlphamaps(0, 0, ReadSplat(DataFolder + "terrain_splat.bytes", info.splatResolution, 4));

            // Grass, flowers and grass clumps close to the player only (terrain detail, as in the
            // terrain tutorial: billboard grass textures plus real grass meshes). The generator
            // writes one density layer per prototype; an older data set has the single grass map.
            data.SetDetailResolution(info.grassResolution, 32);
            UseInstanceCountGrass(data);
            string detailPath = DataFolder + "terrain_details.bytes";
            if (File.Exists(detailPath))
            {
                data.detailPrototypes = DetailPrototypes();
                for (int layer = 0; layer < DetailLayers.Length; layer++)
                    data.SetDetailLayer(0, 0, layer, ReadDetail(detailPath, info.grassResolution, DetailLayers.Length,
                                                                layer, DetailLayers[layer].maxPerCell));
            }
            else
            {
                data.detailPrototypes = new[] { GrassPrototype() };
                data.SetDetailLayer(0, 0, 0, ReadGrass(DataFolder + "terrain_grass.bytes", info.grassResolution));
            }
            data.wavingGrassStrength = 0.25f;
            data.wavingGrassAmount = 0.3f;
            data.wavingGrassSpeed = 0.4f;
            data.wavingGrassTint = new Color(0.75f, 0.78f, 0.6f);

            // Trees.
            var prefabs = ProceduralAssets.TreePrefabs();
            var prototypes = new TreePrototype[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++) prototypes[i] = new TreePrototype { prefab = prefabs[i] };
            data.treePrototypes = prototypes;
            data.SetTreeInstances(MakeTrees(info, data, prefabs.Length), true);

            AssetDatabase.CreateAsset(data, TerrainFolder + "/Campus_TerrainData.asset");

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(info.minX, info.minHeight, info.minZ);

            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 2f;   // 4 let distant slopes rise through the road surfaces
            terrain.basemapDistance = 300f;
            terrain.treeDistance = 400f;
            terrain.treeBillboardDistance = 400f;
            terrain.treeMaximumFullLODCount = 2000;
            terrain.detailObjectDistance = 120f;   // High; VisualQuality sets each tier
            terrain.detailObjectDensity = 1f;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
            return terrain;
        }

        /// <summary>World-space ground height at a point.</summary>
        public static float GroundY(Terrain terrain, float x, float z)
        {
            if (terrain == null) return 0f;
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        }

        // Grass counts below are "tufts per cell". Unity 2022.2+ also has a coverage mode, so ask
        // for instance-count mode; called by name so the script still compiles if the API moves.
        static void UseInstanceCountGrass(TerrainData data)
        {
            var method = typeof(TerrainData).GetMethod("SetDetailScatterMode");
            if (method == null) return;
            var modeType = method.GetParameters()[0].ParameterType;
            method.Invoke(data, new[] { Enum.Parse(modeType, "InstanceCountMode") });
        }

        // ------------------------------------------------------------------ data files

        static float[,] ReadHeights(string path, int res)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var heights = new float[res, res];
            int k = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int value = bytes[k] | (bytes[k + 1] << 8); // little-endian uint16
                    heights[z, x] = value / 65535f;
                    k += 2;
                }
            }
            return heights;
        }

        static float[,,] ReadSplat(string path, int res, int layers)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var maps = new float[res, res, layers];
            int k = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float sum = 0f;
                    for (int l = 0; l < layers; l++) sum += bytes[k + l];
                    for (int l = 0; l < layers; l++) maps[z, x, l] = sum > 0f ? bytes[k + l] / sum : (l == 1 ? 1f : 0f);
                    k += layers;
                }
            }
            return maps;
        }

        static int[,] ReadGrass(string path, int res)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var counts = new int[res, res];
            int k = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = bytes[k++] / 255f;
                    counts[z, x] = d < 0.2f ? 0 : Mathf.RoundToInt(d * 7f);
                }
            }
            return counts;
        }

        /// <summary>One density layer of terrain_details.bytes (layers interleaved per cell), as tufts per cell.</summary>
        static int[,] ReadDetail(string path, int res, int layers, int layer, float maxPerCell)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var counts = new int[res, res];
            if (bytes.Length < res * res * layers) return counts;
            // Deterministic dither, so a density of 0.3 tufts per cell gives a sparse scatter, not none.
            var random = new System.Random(31 + layer);
            int k = layer;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = bytes[k] / 255f;
                    k += layers;
                    float want = d < 0.08f ? 0f : d * maxPerCell;
                    int n = Mathf.FloorToInt(want);
                    if (random.NextDouble() < want - n) n++;
                    counts[z, x] = n;
                }
            }
            return counts;
        }

        static TreeInstance[] MakeTrees(TerrainInfo info, TerrainData data, int kinds)
        {
            int count = info.trees.Length / 4;
            var trees = new TreeInstance[count];
            var random = new System.Random(5);
            for (int i = 0; i < count; i++)
            {
                float x = info.trees[i * 4], z = info.trees[i * 4 + 1];
                int kind = Mathf.Clamp((int)info.trees[i * 4 + 2], 0, kinds - 1);
                float scale = info.trees[i * 4 + 3];
                float nx = (x - info.minX) / info.sizeX;
                float nz = (z - info.minZ) / info.sizeZ;
                float shade = 0.85f + 0.15f * (float)random.NextDouble();
                trees[i] = new TreeInstance
                {
                    position = new Vector3(nx, data.GetInterpolatedHeight(nx, nz) / data.size.y, nz),
                    prototypeIndex = kind,
                    widthScale = scale * (0.9f + 0.2f * (float)random.NextDouble()),
                    heightScale = scale,
                    rotation = (float)random.NextDouble() * Mathf.PI * 2f,
                    color = new Color(shade, shade, shade, 1f),
                    lightmapColor = Color.white,
                };
            }
            return trees;
        }

        // ------------------------------------------------------------------ looks

        static TerrainLayer[] MakeLayers()
        {
            var grass = ProceduralAssets.GroundTexture("Ground_Grass",
                new Color(0.17f, 0.33f, 0.09f), new Color(0.29f, 0.50f, 0.16f), new Color(0.43f, 0.62f, 0.24f), 4, 0.40f, 1); // rich lawn green, as on the campus model
            var dry = ProceduralAssets.GroundTexture("Ground_DryGrass",
                new Color(0.42f, 0.38f, 0.22f), new Color(0.60f, 0.54f, 0.33f), new Color(0.74f, 0.68f, 0.46f), 4, 0.5f, 2);
            var soil = ProceduralAssets.GroundTexture("Ground_Soil",
                new Color(0.32f, 0.23f, 0.16f), new Color(0.47f, 0.35f, 0.25f), new Color(0.60f, 0.50f, 0.40f), 6, 0.55f, 3);
            var rock = ProceduralAssets.GroundTexture("Ground_Rock",
                new Color(0.33f, 0.31f, 0.28f), new Color(0.50f, 0.47f, 0.43f), new Color(0.66f, 0.63f, 0.58f), 3, 0.35f, 4);
            // Height ranges for height blending (mask map blue, remapped): grass tufts stand
            // above soil, so soil shows in the gaps between them first; rock outcrops stand
            // proud of everything.
            return new[]
            {
                Layer("Grass", grass, 5f, 4f, 0.08f, 0.30f, 1.00f),
                Layer("DryGrass", dry, 6f, 5f, 0.06f, 0.20f, 0.90f),
                Layer("Soil", soil, 5f, 6f, 0.12f, 0.00f, 0.55f),
                Layer("Rock", rock, 9f, 9f, 0.22f, 0.25f, 1.00f),
            };
        }

        /// <summary>relief: slope of the layer's normal map (look pass 2), made from its own texture so
        /// clods, stones and tufts catch the low sun.</summary>
        /// smoothness: the layer's shine; heightMin/Max: its mask height remapped for height blending.
        static TerrainLayer Layer(string name, Texture2D texture, float tile, float relief,
                                  float smoothness, float heightMin, float heightMax)
        {
            var layer = new TerrainLayer
            {
                name = name,
                diffuseTexture = texture,
                tileSize = new Vector2(tile, tile),
                smoothness = 0.05f,
                metallic = 0f,
            };
            var normal = ProceduralAssets.NormalFor(texture, relief);
            if (normal != null)
            {
                layer.normalMapTexture = normal;
                layer.normalScale = 1f;   // the tutorial's advice: strong normals look plastic
            }
            // Channel-packed mask map (the realistic-graphics video): metallic, occlusion, height
            // and smoothness in one texture, so the terrain gets crevice shading and height blending
            // for one extra sample per layer.
            var mask = ProceduralAssets.MaskFor(texture, smoothness);
            if (mask != null)
            {
                layer.maskMapTexture = mask;
                layer.maskMapRemapMin = new Vector4(0f, 0f, heightMin, 0f);
                layer.maskMapRemapMax = new Vector4(0f, 1f, heightMax, 1f);
            }
            AssetDatabase.CreateAsset(layer, TerrainFolder + "/" + name + ".terrainlayer");
            return layer;
        }

        struct DetailLayer
        {
            public string name;
            public float maxPerCell;   // tufts per 2 x 2 m detail cell at full density
            public DetailLayer(string name, float maxPerCell) { this.name = name; this.maxPerCell = maxPerCell; }
        }

        // In the generator's order (generate_terrain.py, "detailLayers"). These are the High
        // tier's full counts (the owner asked for quality first, 2026-11-20); Medium and Low thin
        // them through Terrain.detailObjectDensity (VisualQuality), which each prototype obeys.
        static readonly DetailLayer[] DetailLayers =
        {
            new DetailLayer("Lawn grass", 20f),
            new DetailLayer("Wild grass", 14f),
            new DetailLayer("Yellow flowers", 5f),
            new DetailLayer("White flowers", 5f),
            new DetailLayer("Grass clumps", 6f),
        };

        /// <summary>
        /// The detail prototypes, sized against a 1.75 m player (the tutorial's 2 m cube): mown
        /// lawn grass 10-25 cm, wild grass 30-75 cm, flowers 20-40 cm, clumps about 0.45-0.7 m.
        /// Healthy and dry colours are matched to the terrain layers under them, so the grass
        /// reads as part of the ground rather than a separate green.
        /// Mirrored by tools/blender/preview_terrain_detail.py (DETAILS); change the two together.
        /// </summary>
        static DetailPrototype[] DetailPrototypes()
        {
            var lawn = Billboard(ProceduralAssets.GrassTexture("Grass_Lawn", 56, 0.55f, 2.0f, 16f, 12, false),
                                 new Color(0.50f, 0.68f, 0.30f), new Color(0.70f, 0.70f, 0.40f), 0.35f, 0.65f, 0.10f, 0.25f, 0.5f);
            var wild = Billboard(ProceduralAssets.GrassTexture("Grass_Wild", 34, 0.4f, 2.6f, 36f, 13, true),
                                 new Color(0.62f, 0.66f, 0.36f), new Color(0.86f, 0.76f, 0.50f), 0.55f, 1.1f, 0.30f, 0.75f, 0.6f);
            var yellow = Billboard(ProceduralAssets.FlowerTexture("Flowers_Yellow", new Color(1f, 0.72f, 0.12f), new Color(0.85f, 0.42f, 0.05f), 5, 14),
                                   new Color(1f, 1f, 1f), new Color(0.92f, 0.86f, 0.74f), 0.35f, 0.55f, 0.22f, 0.40f, 0.8f);
            var white = Billboard(ProceduralAssets.FlowerTexture("Flowers_White", new Color(0.97f, 0.96f, 0.92f), new Color(1f, 0.82f, 0.2f), 6, 15),
                                  new Color(1f, 1f, 1f), new Color(0.9f, 0.88f, 0.8f), 0.25f, 0.4f, 0.16f, 0.28f, 0.8f);
            var clump = new DetailPrototype
            {
                prototype = ProceduralAssets.GrassClumpPrefab(),
                usePrototypeMesh = true,
                renderMode = DetailRenderMode.Grass,   // waves in the wind and takes the colours below
                healthyColor = new Color(0.55f, 0.68f, 0.32f),
                dryColor = new Color(0.80f, 0.72f, 0.46f),
                minWidth = 0.8f,
                maxWidth = 1.3f,
                minHeight = 0.8f,
                maxHeight = 1.3f,
                noiseSpread = 0.5f,
            };
            var all = new[] { lawn, wild, yellow, white, clump };
            foreach (var p in all) Newer(p);
            return all;
        }

        static DetailPrototype Billboard(Texture2D texture, Color healthy, Color dry,
                                         float minW, float maxW, float minH, float maxH, float spread)
        {
            return new DetailPrototype
            {
                prototypeTexture = texture,
                renderMode = DetailRenderMode.GrassBillboard,
                usePrototypeMesh = false,
                healthyColor = healthy,
                dryColor = dry,
                minWidth = minW,
                maxWidth = maxW,
                minHeight = minH,
                maxHeight = maxH,
                noiseSpread = spread,
            };
        }

        // Settings added in Unity 2022.2 (instance-count scatter): random placement inside each cell,
        // and thinning by Terrain.detailObjectDensity (off by default in that mode, which would make
        // the quality tiers do nothing). Set by name so the script still compiles against older
        // UnityEngine builds (the cloud type check uses 2021).
        static void Newer(DetailPrototype p)
        {
            SetIfThere(p, "positionJitter", 1f);
            SetIfThere(p, "useDensityScaling", true);
            SetIfThere(p, "alignToGround", 0.6f);
            SetIfThere(p, "useInstancing", false);   // Grass mode needs the classic path; billboards ignore it
        }

        static void SetIfThere(object target, string name, object value)
        {
            var prop = target.GetType().GetProperty(name);
            if (prop == null || !prop.CanWrite) return;
            try
            {
                prop.SetValue(target, Convert.ChangeType(value, prop.PropertyType), null);
            }
            catch (Exception)
            {
                // A changed API: keep Unity's default.
            }
        }

        static DetailPrototype GrassPrototype()
        {
            return new DetailPrototype
            {
                prototypeTexture = ProceduralAssets.GrassBladesTexture(),
                renderMode = DetailRenderMode.GrassBillboard,
                usePrototypeMesh = false,
                healthyColor = new Color(0.55f, 0.66f, 0.32f),
                dryColor = new Color(0.78f, 0.70f, 0.45f),
                minWidth = 0.5f,
                maxWidth = 1.0f,
                minHeight = 0.3f,
                maxHeight = 0.7f,
                noiseSpread = 0.4f,
            };
        }

        static Material TerrainMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null) shader = Shader.Find("Nature/Terrain/Standard");
            if (shader == null) return null; // Unity falls back to the pipeline's default terrain material
            var mat = new Material(shader) { name = "Campus_Terrain", enableInstancing = true };
            mat.EnableKeyword("_NORMALMAP");   // the layers carry normal maps (look pass 2)
            // Mask maps and height-based blending (URP Terrain/Lit; works with up to four layers,
            // which is what the campus has). The editor's terrain GUI sets these when it is used by
            // hand; a material made in code has to set them itself.
            mat.EnableKeyword("_MASKMAP");
            for (int i = 0; i < 4; i++)
                if (mat.HasProperty("_LayerHasMask" + i)) mat.SetFloat("_LayerHasMask" + i, 1f);
            if (mat.HasProperty("_EnableHeightBlend"))
            {
                mat.SetFloat("_EnableHeightBlend", 1f);
                if (mat.HasProperty("_HeightTransition")) mat.SetFloat("_HeightTransition", 0.25f);
                mat.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            }
            AssetDatabase.CreateAsset(mat, TerrainFolder + "/Campus_Terrain.mat");
            return mat;
        }
    }
}
