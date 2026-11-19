using System.Collections.Generic;
using System.IO;
using Jagti.Player;
using Jagti.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Campus_Greybox.unity from the generated layout data:
    /// hilltop terrain with painted ground, grass and trees (CampusTerrainBuilder),
    /// roads following the ground, sports areas, every building as a grey block at its
    /// real footprint and storey count, the boundary wall, floating name labels,
    /// sky, sun, fog, a zombie navigation mesh, and a first-person player.
    /// Menu: Jagti > Build Campus.
    /// </summary>
    public static class CampusGreyboxBuilder
    {
        public const string LayoutPath = "Assets/Data/campus_layout.json";
        public const string ScenePath = "Assets/Scenes/Campus_Greybox.unity";
        const string MaterialFolder = "Assets/Materials/Greybox";
        public const string BuiltVersionKey = "Jagti.BuiltLayoutVersion";

        static Terrain terrain;

        // Layer heights so flat things don't flicker against each other.
        // Kept clear of the terrain, which simplifies its mesh slightly at a distance.
        const float AreaTop = 0.10f;
        const float RoadTop = 0.15f;
        const float FieldTop = 0.20f;

        [MenuItem("Jagti/Build Campus", priority = 1)]
        public static void BuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        // The game mode the scene is built for. Battle royale is the default; the zombie survival
        // mode from Phase 3 can still be built from the menu.
        const string ModeKey = "Jagti.GameMode";
        const string ModeRoyale = "royale";
        const string ModeZombies = "zombies";

        static string Mode { get { return EditorPrefs.GetString(ModeKey, ModeRoyale); } }

        [MenuItem("Jagti/Game Mode/Battle Royale", priority = 20)]
        public static void BuildRoyale()
        {
            EditorPrefs.SetString(ModeKey, ModeRoyale);
            BuildFromMenu();
        }

        [MenuItem("Jagti/Game Mode/Zombie Survival", priority = 21)]
        public static void BuildZombies()
        {
            EditorPrefs.SetString(ModeKey, ModeZombies);
            BuildFromMenu();
        }

        [MenuItem("Jagti/Game Mode/Battle Royale", true)]
        static bool RoyaleCheck()
        {
            Menu.SetChecked("Jagti/Game Mode/Battle Royale", Mode == ModeRoyale);
            return true;
        }

        [MenuItem("Jagti/Game Mode/Zombie Survival", true)]
        static bool ZombiesCheck()
        {
            Menu.SetChecked("Jagti/Game Mode/Zombie Survival", Mode == ModeZombies);
            return true;
        }

        /// <summary>The layout version in the JSON; the setup script rebuilds when it changes.</summary>
        public static int LayoutVersion()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
            return json == null ? 0 : CampusLayout.FromJson(json.text).version;
        }

        public static void Build()
        {
            try
            {
                BuildScene();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void Progress(string step, float amount)
        {
            EditorUtility.DisplayProgressBar("Building Jagti campus", step, amount);
        }

        static void BuildScene()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
            if (json == null)
            {
                Debug.LogError("[Jagti] Missing " + LayoutPath + ". Run tools/layout/generate_layout.py.");
                return;
            }
            var layout = CampusLayout.FromJson(json.text);

            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory(MaterialFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ProceduralAssets.ResetFolder();
            var materials = new MaterialLibrary(MaterialFolder);
            var root = new GameObject("Campus");

            // Terrain: hilltop plateau, ravines, painted ground, grass and trees.
            Progress("Terrain, ground paint, grass and trees", 0.1f);
            terrain = CampusTerrainBuilder.Build(root.transform);

            Progress("Roads, areas and buildings", 0.5f);
            var areaRoot = Child(root.transform, "Areas");
            foreach (var area in layout.areas) MakeArea(area, areaRoot, materials);

            var roadRoot = Child(root.transform, "Roads");
            asphaltRoad = null;
            asphaltLined = null;
            pavedRoad = null;
            kerbMaterial = null;
            foreach (var road in layout.roads) MakeRoad(road, layout.roads, roadRoot, materials);
            MakeWater(layout, Child(root.transform, "Water"));

            // Landmark buildings come from Blender models; the rest stay grey blocks for now.
            var models = BuildingModels.Available();
            var replaced = new HashSet<string>();
            foreach (var m in models)
                foreach (var name in m.replaces) replaced.Add(name);

            var buildingRoot = Child(root.transform, "Buildings");
            BuildingModels.Place(models, Child(buildingRoot, "Landmarks"), Ground);
            Progress("Building interiors and furniture", 0.6f);
            InteriorBuilder.Build(Child(buildingRoot, "Interiors"), Ground);
            Progress("Street lamps, benches, signs and railings", 0.65f);
            OutdoorBuilder.Build(Child(root.transform, "Outdoor"), Ground);
            // Battle royale cover on the open lawns (tools/royale/build_cover.py), before the bake so
            // the NavMesh and the bots' cover search see it.
            if (Mode == ModeRoyale) OutdoorBuilder.Build(Child(root.transform, "Cover"), Ground, OutdoorBuilder.CoverPath);
            var zones = new Dictionary<string, Transform>();
            foreach (var b in layout.buildings)
            {
                if (replaced.Contains(b.name)) continue;
                if (!zones.TryGetValue(b.zone, out var zone))
                {
                    zone = Child(buildingRoot, "Zone - " + b.zone);
                    zones[b.zone] = zone;
                }
                MakeBuilding(b, zone, materials);
            }

            MakeBoundaryWall(layout.boundary, layout.roads, Child(root.transform, "Boundary Wall"), materials);

            var labelRoot = Child(root.transform, "Labels");
            foreach (var place in layout.places) MakeLabel(place, labelRoot);

            MakeLighting();

            // Bake before the player and crates exist so they do not punch holes in the NavMesh.
            Progress("Zombie navigation mesh (this can take a minute)", 0.7f);
            MakeNavMesh(layout);
            var player = MakePlayer(layout);

            if (Mode == ModeRoyale)
            {
                Progress("Battle royale: haze, sky bus, loot and map", 0.9f);
                RoyaleBuilder.Build(layout, player, root.transform, Ground);
            }
            else
            {
                Progress("Zombies, weapons and supply crates", 0.9f);
                GameplayBuilder.Build(layout, player, root.transform, Ground);
                var hud = new GameObject("Debug HUD").AddComponent<DebugHud>();
                hud.player = player;
                hud.places = layout.places;
            }
            CampusBusBuilder.Build(layout, Ground, RoadTop);   // after the bake, so it doesn't cut the NavMesh

            Progress("Saving", 0.95f);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            EditorPrefs.SetInt(BuiltVersionKey, layout.version);
            int trees = terrain != null ? terrain.terrainData.treeInstanceCount : 0;
            Debug.Log("[Jagti] Built campus: terrain, " + trees + " trees, " + layout.buildings.Length + " buildings, " + layout.roads.Length + " roads. Press Play to walk it.");
        }

        // ------------------------------------------------------------------ pieces

        static void MakeBuilding(BuildingData b, Transform parent, MaterialLibrary materials)
        {
            bool cylinder = b.shape == "cylinder";
            var go = GameObject.CreatePrimitive(cylinder ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            go.name = b.name + " (" + b.floors + " floors)";
            go.transform.SetParent(parent);
            // Sink the base a little so the block meets the ground on a slight slope.
            const float sink = 0.6f;
            float ground = Ground(b.x, b.z);
            float h = b.height + sink;
            go.transform.position = new Vector3(b.x, ground - sink + h / 2f, b.z);
            go.transform.localScale = cylinder
                ? new Vector3(b.width, h / 2f, b.depth) // Unity's cylinder is 2 m tall
                : new Vector3(b.width, h, b.depth);
            SetMaterial(go, materials.Get(BuildingMaterialName(b), BuildingColor(b)));
            MarkStatic(go);
        }

        static string BuildingMaterialName(BuildingData b)
        {
            if (b.name.Contains("Hostel")) return "Building_Hostel";
            if (b.zone == "academic") return "Building_Academic";
            if (b.name.Contains("Shed") || b.name.Contains("Substation")) return "Building_Service";
            return "Building_Default";
        }

        static Color BuildingColor(BuildingData b)
        {
            if (b.name.Contains("Hostel")) return new Color(0.89f, 0.85f, 0.79f);
            if (b.zone == "academic") return new Color(0.90f, 0.84f, 0.77f); // pinkish cream, as on the campus model
            if (b.name.Contains("Shed") || b.name.Contains("Substation")) return new Color(0.55f, 0.55f, 0.52f);
            return new Color(0.86f, 0.82f, 0.76f);
        }

        static void MakeArea(AreaData a, Transform parent, MaterialLibrary materials)
        {
            // Lawns and the courtyard are painted on the terrain with grass.
            if (a.type == "lawn" || a.type == "courtyard") return;

            Color color;
            float top = AreaTop;
            switch (a.type)
            {
                case "parking": color = new Color(0.42f, 0.42f, 0.41f); top = RoadTop; break;
                case "plaza": color = new Color(0.76f, 0.74f, 0.70f); top = RoadTop; break;   // pale concrete forecourt
                case "court": color = new Color(0.26f, 0.43f, 0.36f); top = RoadTop; break;   // green hard courts (owner's photo)
                case "track": color = new Color(0.62f, 0.30f, 0.22f); top = RoadTop; break;
                case "field": color = new Color(0.32f, 0.50f, 0.20f); top = FieldTop; break;
                case "amphitheatre": color = new Color(0.82f, 0.77f, 0.68f); break;
                case "roundabout": color = new Color(0.28f, 0.48f, 0.18f); top = 0.35f; break;
                case "substation": color = new Color(0.52f, 0.50f, 0.44f); break;
                default: color = new Color(0.5f, 0.5f, 0.5f); break;
            }

            bool ellipse = a.shape == "ellipse";
            var go = GameObject.CreatePrimitive(ellipse ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            go.name = "Area - " + a.type;
            go.transform.SetParent(parent);
            // The generator flattened the ground under these areas; the slab reaches 0.5 m down.
            const float below = 0.5f;
            float h = top + below;
            go.transform.position = new Vector3(a.x, Ground(a.x, a.z) - below + h / 2f, a.z);
            go.transform.localScale = ellipse
                ? new Vector3(a.width, h / 2f, a.depth)
                : new Vector3(a.width, h, a.depth);
            if (a.type != "roundabout") Object.DestroyImmediate(go.GetComponent<Collider>()); // flat decal; the terrain carries the player
            SetMaterial(go, materials.Get("Area_" + a.type, color));
            MarkStatic(go);
        }

        static Material asphaltRoad, asphaltLined, pavedRoad, kerbMaterial;

        static void MakeRoad(RoadData road, RoadData[] allRoads, Transform parent, MaterialLibrary materials)
        {
            float[] points = Densify(road.points, 2f);
            int count = points.Length / 2;
            if (count < 2) return;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float half = road.width / 2f;
            float distance = 0f;
            bool paved = IsPaved(road.name);
            float top = RoadTop + RoadLift(road, allRoads);

            for (int i = 0; i < count; i++)
            {
                Vector2 p = Point(points, i);
                Vector2 dir = i == 0 ? Point(points, 1) - p
                            : i == count - 1 ? p - Point(points, i - 1)
                            : Point(points, i + 1) - Point(points, i - 1);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
                dir.Normalize();
                var normal = new Vector2(-dir.y, dir.x);
                if (i > 0) distance += Vector2.Distance(p, Point(points, i - 1));

                // Each edge follows the ground; the generator flattened the terrain across the road.
                Vector2 left = p + normal * half, right = p - normal * half;
                vertices.Add(new Vector3(left.x, Ground(left.x, left.y) + top, left.y));
                vertices.Add(new Vector3(right.x, Ground(right.x, right.y) + top, right.y));
                if (paved)
                {
                    // Paving tiles in metres so the slabs keep their size on every path width.
                    uvs.Add(new Vector2(0f, distance / PavingTile));
                    uvs.Add(new Vector2(road.width / PavingTile, distance / PavingTile));
                }
                else
                {
                    uvs.Add(new Vector2(0f, distance / road.width));
                    uvs.Add(new Vector2(1f, distance / road.width));
                }

                if (i > 0)
                {
                    int v = i * 2;
                    triangles.Add(v - 2); triangles.Add(v); triangles.Add(v - 1);
                    triangles.Add(v - 1); triangles.Add(v); triangles.Add(v + 1);
                }
            }

            var mesh = new Mesh { name = "Road - " + road.name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Road - " + road.name);
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RoadMaterial(road, paved);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            MarkStatic(go);

            if (road.name != "Highway" && road.name != "Clock Tower Walkway" && !road.name.StartsWith("Plaza ") && road.width >= 4f)
                MakeKerbs(road, points, allRoads, go.transform);
        }

        /// <summary>
        /// Where two roads overlap at a junction they would share one plane and flicker, so each
        /// road sits a little higher than the narrower ones (1 cm per metre of width), with a
        /// further centimetre step between roads of the same width. The wider road covers the end
        /// of the narrower one that runs into it.
        /// </summary>
        static float RoadLift(RoadData road, RoadData[] allRoads)
        {
            int index = System.Array.IndexOf(allRoads, road);
            return (Mathf.Clamp(road.width, 3f, 13f) - 3f) * 0.01f + (index < 0 ? 0 : index % 3) * 0.01f;
        }

        const float PavingTile = 3f; // metres per paving texture tile (5 x 5 slabs of 0.6 m)

        // Cream stone paving on the academic ring drive, the plazas and the garden paths, as on the campus model.
        static bool IsPaved(string name)
        {
            return name == "Academic Ring" || name == "Clock Tower Walkway" || name.StartsWith("Courtyard ") || name.StartsWith("Plaza ") || name.StartsWith("Paved ");
        }

        // Grey asphalt elsewhere (campus model); two-lane roads (6 m and wider) get a dashed white centre line.
        static Material RoadMaterial(RoadData road, bool paved)
        {
            if (paved)
            {
                if (pavedRoad == null)
                    pavedRoad = ProceduralAssets.LitMaterial("Road_Paving", Color.white, ProceduralAssets.PavingTexture(), 0.12f);
                return pavedRoad;
            }
            if (road.width >= 6f)
            {
                if (asphaltLined == null)
                    asphaltLined = ProceduralAssets.LitMaterial("Road_Asphalt_Lined", Color.white, ProceduralAssets.AsphaltTexture(true), 0.15f);
                return asphaltLined;
            }
            if (asphaltRoad == null)
                asphaltRoad = ProceduralAssets.LitMaterial("Road_Asphalt", Color.white, ProceduralAssets.AsphaltTexture(false), 0.15f);
            return asphaltRoad;
        }

        // Water channels on the terraces and round pools, from the campus model.
        static void MakeWater(CampusLayout layout, Transform parent)
        {
            if ((layout.water == null || layout.water.Length == 0) && (layout.pools == null || layout.pools.Length == 0)) return;
            var water = ProceduralAssets.LitMaterial("Water_Channel", new Color(0.20f, 0.56f, 0.58f), null, 0.92f); // turquoise, as on the campus model
            var stone = ProceduralAssets.LitMaterial("Water_Edge", new Color(0.88f, 0.83f, 0.76f), null, 0.15f);
            if (layout.water != null)
            {
                foreach (var ch in layout.water)
                {
                    float[] points = Densify(ch.points, 1.5f);
                    int count = points.Length / 2;
                    if (count < 2) continue;
                    var wv = new List<Vector3>(); var wu = new List<Vector2>(); var wt = new List<int>();
                    var ev = new List<Vector3>(); var eu = new List<Vector2>(); var et = new List<int>();
                    float half = ch.width / 2f, rim = 0.3f, distance = 0f;
                    for (int i = 0; i < count - 1; i++)
                    {
                        Vector2 p0 = Point(points, i), p1 = Point(points, i + 1);
                        Vector2 dir = (p1 - p0).normalized;
                        var n = new Vector2(-dir.y, dir.x);
                        float u0 = distance / ch.width;
                        distance += Vector2.Distance(p0, p1);
                        float u1 = distance / ch.width;
                        System.Func<Vector2, float, Vector3> at = (q, up) => new Vector3(q.x, Ground(q.x, q.y) + up, q.y);
                        // Water surface sits a little below the edging.
                        AddQuad(wv, wu, wt, at(p0 + n * half, 0.06f), at(p1 + n * half, 0.06f), at(p1 - n * half, 0.06f), at(p0 - n * half, 0.06f), u0, u1, Vector3.up);
                        foreach (float side in new[] { 1f, -1f })
                        {
                            Vector2 a0 = p0 + n * side * half, a1 = p1 + n * side * half;
                            Vector2 b0 = p0 + n * side * (half + rim), b1 = p1 + n * side * (half + rim);
                            AddQuad(ev, eu, et, at(a0, 0.22f), at(a1, 0.22f), at(b1, 0.22f), at(b0, 0.22f), u0, u1, Vector3.up);
                            var inward = new Vector3(-n.x * side, 0f, -n.y * side);
                            AddQuad(ev, eu, et, at(a0, -0.05f), at(a1, -0.05f), at(a1, 0.22f), at(a0, 0.22f), u0, u1, inward);
                            AddQuad(ev, eu, et, at(b0, -0.2f), at(b1, -0.2f), at(b1, 0.22f), at(b0, 0.22f), u0, u1, -inward);
                        }
                    }
                    MakeMeshObject("Water - " + ch.name, parent, wv, wu, wt, water, false);
                    MakeMeshObject("Water Edge - " + ch.name, parent, ev, eu, et, stone, true);
                }
            }
            if (layout.pools != null)
            {
                foreach (var pool in layout.pools)
                {
                    const int segs = 32;
                    float g = Ground(pool.x, pool.z);
                    var wv = new List<Vector3>(); var wu = new List<Vector2>(); var wt = new List<int>();
                    var ev = new List<Vector3>(); var eu = new List<Vector2>(); var et = new List<int>();
                    var c = new Vector3(pool.x, g + 0.35f, pool.z);
                    for (int k = 0; k < segs; k++)
                    {
                        float a0 = k * Mathf.PI * 2f / segs, a1 = (k + 1) * Mathf.PI * 2f / segs;
                        var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                        var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                        float r = pool.radius, ro = pool.radius + 0.45f;
                        // Water as a fan of thin quads (centre, edge, edge, centre).
                        AddQuad(wv, wu, wt, c, c + d0 * r, c + d1 * r, c, 0f, 1f, Vector3.up);
                        Vector3 top = Vector3.up * 0.2f;
                        AddQuad(ev, eu, et, c + d0 * r + top, c + d0 * ro + top, c + d1 * ro + top, c + d1 * r + top, 0f, 1f, Vector3.up);
                        AddQuad(ev, eu, et, c + d0 * ro + Vector3.down * 0.8f, c + d1 * ro + Vector3.down * 0.8f, c + d1 * ro + top, c + d0 * ro + top, 0f, 1f, (d0 + d1));
                        AddQuad(ev, eu, et, c + d0 * r + Vector3.down * 0.1f, c + d1 * r + Vector3.down * 0.1f, c + d1 * r + top, c + d0 * r + top, 0f, 1f, -(d0 + d1));
                    }
                    MakeMeshObject("Pool - " + pool.name, parent, wv, wu, wt, water, false);
                    MakeMeshObject("Pool Edge - " + pool.name, parent, ev, eu, et, stone, true);
                }
            }
        }

        static void MakeMeshObject(string name, Transform parent, List<Vector3> v, List<Vector2> uv, List<int> t, Material mat, bool collider)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            MarkStatic(go);
        }

        // Light stone kerbs along both edges, left open where another road joins.
        static void MakeKerbs(RoadData road, float[] points, RoadData[] allRoads, Transform parent)
        {
            if (kerbMaterial == null)
                kerbMaterial = ProceduralAssets.LitMaterial("Road_Kerb", Color.white, ProceduralAssets.KerbTexture(), 0.1f);
            const float kerbWidth = 0.3f, kerbHeight = 0.18f;
            float top = RoadTop + RoadLift(road, allRoads);
            int count = points.Length / 2;
            float half = road.width / 2f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 prevInnerLow = Vector3.zero, prevInnerTop = Vector3.zero, prevOuterTop = Vector3.zero;
                bool prevOk = false;
                float distance = 0f, prevDistance = 0f;
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = Point(points, i);
                    Vector2 dir = i == 0 ? Point(points, 1) - p
                                : i == count - 1 ? p - Point(points, i - 1)
                                : Point(points, i + 1) - Point(points, i - 1);
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
                    dir.Normalize();
                    var normal = new Vector2(-dir.y, dir.x) * side;
                    if (i > 0) distance += Vector2.Distance(p, Point(points, i - 1));

                    Vector2 inner = p + normal * half, outer = p + normal * (half + kerbWidth);
                    float gy = Ground(inner.x, inner.y) + top;
                    var innerLow = new Vector3(inner.x, gy, inner.y);
                    var innerTop = new Vector3(inner.x, gy + kerbHeight, inner.y);
                    var outerTop = new Vector3(outer.x, Mathf.Max(gy, Ground(outer.x, outer.y) + top) + kerbHeight, outer.y);
                    bool ok = !InsideOtherRoad(inner, road, allRoads);

                    if (i > 0 && ok && prevOk)
                    {
                        float u0 = prevDistance / 1.2f, u1 = distance / 1.2f;
                        // Top of the kerb, facing up.
                        AddQuad(vertices, uvs, triangles, prevInnerTop, innerTop, outerTop, prevOuterTop, u0, u1, Vector3.up);
                        // Face towards the road.
                        var towardRoad = new Vector3(-normal.x, 0f, -normal.y);
                        AddQuad(vertices, uvs, triangles, prevInnerLow, innerLow, innerTop, prevInnerTop, u0, u1, towardRoad);
                    }
                    prevInnerLow = innerLow; prevInnerTop = innerTop; prevOuterTop = outerTop;
                    prevOk = ok;
                    prevDistance = distance;
                }
            }
            if (triangles.Count == 0) return;

            var mesh = new Mesh { name = "Kerbs - " + road.name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Kerbs");
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = kerbMaterial;
            MarkStatic(go);
        }

        // Adds a quad a-b-c-d with its winding chosen so it faces `facing`.
        static void AddQuad(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
                            Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float u1, Vector3 facing)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uvs.Add(new Vector2(u0, 0f)); uvs.Add(new Vector2(u1, 0f)); uvs.Add(new Vector2(u1, 1f)); uvs.Add(new Vector2(u0, 1f));
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f;
            if (!flip)
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            else
            {
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
            }
        }

        static bool InsideOtherRoad(Vector2 p, RoadData self, RoadData[] roads)
        {
            foreach (var road in roads)
            {
                if (road == self) continue;
                float reach = road.width / 2f + 0.8f;
                for (int i = 0; i + 1 < road.points.Length / 2; i++)
                {
                    Vector2 a = Point(road.points, i), b = Point(road.points, i + 1);
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    if (Vector2.Distance(p, a + ab * t) < reach) return true;
                }
            }
            return false;
        }

        static Vector2 Point(float[] points, int i)
        {
            return new Vector2(points[i * 2], points[i * 2 + 1]);
        }

        /// <summary>Adds points so no gap is longer than maxStep, so roads can follow the ground.</summary>
        static float[] Densify(float[] points, float maxStep)
        {
            var output = new List<float>();
            int count = points.Length / 2;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = Point(points, i);
                if (i > 0)
                {
                    Vector2 prev = Point(points, i - 1);
                    int steps = Mathf.CeilToInt(Vector2.Distance(prev, p) / maxStep);
                    for (int s = 1; s < steps; s++)
                    {
                        Vector2 q = Vector2.Lerp(prev, p, s / (float)steps);
                        output.Add(q.x);
                        output.Add(q.y);
                    }
                }
                output.Add(p.x);
                output.Add(p.y);
            }
            return output.ToArray();
        }

        static float Ground(float x, float z)
        {
            return CampusTerrainBuilder.GroundY(terrain, x, z);
        }

        static void MakeBoundaryWall(float[] points, RoadData[] roads, Transform parent, MaterialLibrary materials)
        {
            int count = points.Length / 2;
            var mat = materials.Get("Boundary_Wall", new Color(0.62f, 0.58f, 0.52f));
            for (int i = 0; i < count; i++)
            {
                Vector2 a = Point(points, i);
                Vector2 b = Point(points, (i + 1) % count);
                // Short pieces so the wall steps with the slope; each reaches 1.5 m into the ground.
                float length = Vector2.Distance(a, b);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(length / 6f));
                for (int k = 0; k < pieces; k++)
                {
                    Vector2 p0 = Vector2.Lerp(a, b, k / (float)pieces);
                    Vector2 p1 = Vector2.Lerp(a, b, (k + 1) / (float)pieces);
                    Vector2 mid = (p0 + p1) / 2f;
                    if (CrossesRoad(mid, roads)) continue; // leave a gap where a road passes through
                    float ground = Mathf.Min(Ground(p0.x, p0.y), Ground(p1.x, p1.y));
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Wall " + i + "." + k;
                    go.transform.SetParent(parent);
                    go.transform.position = new Vector3(mid.x, ground - 1.5f + 4f / 2f, mid.y);
                    go.transform.rotation = Quaternion.LookRotation(new Vector3(b.x - a.x, 0f, b.y - a.y));
                    go.transform.localScale = new Vector3(0.4f, 4f, Vector2.Distance(p0, p1) + 0.05f);
                    SetMaterial(go, mat);
                    MarkStatic(go);
                }
            }
        }

        static bool CrossesRoad(Vector2 p, RoadData[] roads)
        {
            foreach (var road in roads)
            {
                if (road.name == "Highway") continue;
                float reach = road.width / 2f + 1.5f;
                for (int i = 0; i + 1 < road.points.Length / 2; i++)
                {
                    Vector2 a = Point(road.points, i), b = Point(road.points, i + 1);
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    if (Vector2.Distance(p, a + ab * t) < reach) return true;
                }
            }
            return false;
        }

        static void MakeLabel(PlaceData place, Transform parent)
        {
            var go = new GameObject("Label - " + place.name);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(place.x, Ground(place.x, place.z) + 30f, place.z);
            var text = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            text.text = place.name;
            text.fontSize = 64;
            text.characterSize = 0.6f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(1f, 0.92f, 0.7f);
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            go.AddComponent<Billboard>();
        }

        static void MakeLighting()
        {
            var sunObject = new GameObject("Sun");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.shadows = LightShadows.Soft;
            sunObject.transform.rotation = Quaternion.Euler(42f, -30f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.50f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.20f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 1100f; // far enough to see the ridges on the horizon
            RenderSettings.fogColor = new Color(0.72f, 0.76f, 0.79f);

            // Hazy Jammu sky; fog fades the far hills into its horizon colour.
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "Jagti_Sky" };
                sky.SetFloat("_SunSize", 0.04f);
                sky.SetFloat("_AtmosphereThickness", 1.1f);
                sky.SetColor("_SkyTint", new Color(0.52f, 0.56f, 0.62f));
                sky.SetColor("_GroundColor", new Color(0.45f, 0.43f, 0.38f));
                sky.SetFloat("_Exposure", 1.2f);
                AssetDatabase.CreateAsset(sky, ProceduralAssets.Folder + "/Jagti_Sky.mat");
                RenderSettings.skybox = sky;
            }
        }

        static void MakeNavMesh(CampusLayout layout)
        {
            // Covers the campus inside the wall plus a 40 m margin, where zombies will roam.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < layout.boundary.Length / 2; i++)
            {
                Vector2 p = Point(layout.boundary, i);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);
            }
            const float margin = 40f;
            var go = new GameObject("Zombie NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3((minX + maxX) / 2f, 30f, (minZ + maxZ) / 2f);
            surface.size = new Vector3(maxX - minX + margin * 2f, 140f, maxZ - minZ + margin * 2f);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.25f;
            try
            {
                surface.BuildNavMesh();
                if (surface.navMeshData != null)
                {
                    AssetDatabase.CreateAsset(surface.navMeshData, ProceduralAssets.Folder + "/Campus_NavMesh.asset");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Jagti] NavMesh bake failed; the campus still works without it. " + e.Message);
            }
        }

        static FirstPersonController MakePlayer(CampusLayout layout)
        {
            var player = new GameObject("Player");
            player.tag = "Player";
            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.35f;

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            var cam = cameraObject.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1800f;
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cameraObject.AddComponent<AudioListener>();

            var flashlightObject = new GameObject("Flashlight");
            flashlightObject.transform.SetParent(cameraObject.transform, false);
            flashlightObject.transform.localPosition = new Vector3(0.2f, -0.15f, 0.2f);
            var flashlight = flashlightObject.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.range = 30f;
            flashlight.spotAngle = 55f;
            flashlight.intensity = 3f;
            flashlight.shadows = LightShadows.Soft;
            flashlight.enabled = false;

            var fpc = player.AddComponent<FirstPersonController>();
            fpc.cameraPivot = cameraObject.transform;
            fpc.flashlight = flashlight;

            var spawn = new Vector3(layout.spawn.x, Ground(layout.spawn.x, layout.spawn.z) + 0.1f, layout.spawn.z);
            player.transform.position = spawn;
            // Face the academic block.
            Vector3 lookAt = new Vector3(75f, 0f, 288f);
            Vector3 dir = lookAt - spawn;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(dir);
            return fpc;
        }

        // ------------------------------------------------------------------ helpers

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            return go.transform;
        }

        static void SetMaterial(GameObject go, Material material)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        static void MarkStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == path)) scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Creates and reuses flat-colour materials saved under Assets/Materials/Greybox.</summary>
        class MaterialLibrary
        {
            readonly string folder;
            readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

            public MaterialLibrary(string folder)
            {
                this.folder = folder;
            }

            public Material Get(string name, Color color)
            {
                if (cache.TryGetValue(name, out var cached)) return cached;
                string path = folder + "/" + name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find("Standard");
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);
                EditorUtility.SetDirty(mat);
                cache[name] = mat;
                return mat;
            }
        }
    }
}
