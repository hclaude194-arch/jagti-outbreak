using System.Collections.Generic;
using System.IO;
using Jagti.Player;
using Jagti.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Campus_Greybox.unity from Assets/Data/campus_layout.json:
    /// ground, roads, lawns and sports areas, every building as a grey block at its
    /// real footprint and storey count, the boundary wall, floating name labels,
    /// a sun, fog, and a first-person player standing outside Brahmaputra Hostel.
    /// Menu: Jagti > Build Grey-box Campus.
    /// </summary>
    public static class CampusGreyboxBuilder
    {
        public const string LayoutPath = "Assets/Data/campus_layout.json";
        public const string ScenePath = "Assets/Scenes/Campus_Greybox.unity";
        const string MaterialFolder = "Assets/Materials/Greybox";

        // Layer heights so flat things don't flicker against each other.
        const float AreaTop = 0.04f;
        const float RoadTop = 0.06f;
        const float FieldTop = 0.08f;

        [MenuItem("Jagti/Build Grey-box Campus", priority = 1)]
        public static void BuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        public static void Build()
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
            var materials = new MaterialLibrary(MaterialFolder);
            var root = new GameObject("Campus");

            // Ground: a 1.7 km square under the whole campus.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.position = new Vector3(80f, 0f, -40f);
            ground.transform.localScale = new Vector3(170f, 1f, 170f);
            SetMaterial(ground, materials.Get("Ground", new Color(0.45f, 0.42f, 0.31f)));
            MarkStatic(ground);

            var areaRoot = Child(root.transform, "Areas");
            foreach (var area in layout.areas) MakeArea(area, areaRoot, materials);

            var roadRoot = Child(root.transform, "Roads");
            foreach (var road in layout.roads) MakeRoad(road, roadRoot, materials);

            var buildingRoot = Child(root.transform, "Buildings");
            var zones = new Dictionary<string, Transform>();
            foreach (var b in layout.buildings)
            {
                if (!zones.TryGetValue(b.zone, out var zone))
                {
                    zone = Child(buildingRoot, "Zone - " + b.zone);
                    zones[b.zone] = zone;
                }
                MakeBuilding(b, zone, materials);
            }

            MakeBoundaryWall(layout.boundary, Child(root.transform, "Boundary Wall"), materials);

            var labelRoot = Child(root.transform, "Labels");
            foreach (var place in layout.places) MakeLabel(place, labelRoot);

            MakeLighting();
            var player = MakePlayer(layout);

            var hud = new GameObject("Debug HUD").AddComponent<DebugHud>();
            hud.player = player;
            hud.places = layout.places;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log("[Jagti] Built grey-box campus: " + layout.buildings.Length + " buildings, " + layout.roads.Length + " roads. Press Play to walk it.");
        }

        // ------------------------------------------------------------------ pieces

        static void MakeBuilding(BuildingData b, Transform parent, MaterialLibrary materials)
        {
            bool cylinder = b.shape == "cylinder";
            var go = GameObject.CreatePrimitive(cylinder ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            go.name = b.name + " (" + b.floors + " floors)";
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(b.x, b.height / 2f, b.z);
            go.transform.localScale = cylinder
                ? new Vector3(b.width, b.height / 2f, b.depth) // Unity's cylinder is 2 m tall
                : new Vector3(b.width, b.height, b.depth);
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
            if (b.name.Contains("Hostel")) return new Color(0.80f, 0.74f, 0.64f);
            if (b.zone == "academic") return new Color(0.86f, 0.82f, 0.74f);
            if (b.name.Contains("Shed") || b.name.Contains("Substation")) return new Color(0.55f, 0.55f, 0.52f);
            return new Color(0.74f, 0.72f, 0.68f);
        }

        static void MakeArea(AreaData a, Transform parent, MaterialLibrary materials)
        {
            Color color;
            float top = AreaTop;
            switch (a.type)
            {
                case "lawn": color = new Color(0.40f, 0.50f, 0.27f); break;
                case "courtyard": color = new Color(0.36f, 0.50f, 0.25f); break;
                case "parking": color = new Color(0.30f, 0.30f, 0.30f); top = RoadTop; break;
                case "court": color = new Color(0.30f, 0.42f, 0.48f); top = RoadTop; break;
                case "track": color = new Color(0.62f, 0.30f, 0.22f); top = RoadTop; break;
                case "field": color = new Color(0.38f, 0.48f, 0.26f); top = FieldTop; break;
                case "amphitheatre": color = new Color(0.62f, 0.58f, 0.50f); break;
                case "roundabout": color = new Color(0.35f, 0.47f, 0.25f); top = FieldTop; break;
                case "substation": color = new Color(0.52f, 0.50f, 0.44f); break;
                default: color = new Color(0.5f, 0.5f, 0.5f); break;
            }

            bool ellipse = a.shape == "ellipse";
            var go = GameObject.CreatePrimitive(ellipse ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            go.name = "Area - " + a.type;
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(a.x, top / 2f, a.z);
            go.transform.localScale = ellipse
                ? new Vector3(a.width, top / 2f, a.depth)
                : new Vector3(a.width, top, a.depth);
            Object.DestroyImmediate(go.GetComponent<Collider>()); // flat decal; the ground plane carries the player
            SetMaterial(go, materials.Get("Area_" + a.type, color));
            MarkStatic(go);
        }

        static void MakeRoad(RoadData road, Transform parent, MaterialLibrary materials)
        {
            int count = road.points.Length / 2;
            if (count < 2) return;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float half = road.width / 2f;
            float distance = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector2 p = Point(road.points, i);
                Vector2 dir = i == 0 ? Point(road.points, 1) - p
                            : i == count - 1 ? p - Point(road.points, i - 1)
                            : Point(road.points, i + 1) - Point(road.points, i - 1);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
                dir.Normalize();
                var normal = new Vector2(-dir.y, dir.x);
                if (i > 0) distance += Vector2.Distance(p, Point(road.points, i - 1));

                vertices.Add(new Vector3(p.x + normal.x * half, RoadTop, p.y + normal.y * half));
                vertices.Add(new Vector3(p.x - normal.x * half, RoadTop, p.y - normal.y * half));
                uvs.Add(new Vector2(0f, distance / road.width));
                uvs.Add(new Vector2(1f, distance / road.width));

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
            renderer.sharedMaterial = materials.Get(road.name == "Clock Tower Walkway" ? "Walkway" : "Road",
                road.name == "Clock Tower Walkway" ? new Color(0.72f, 0.68f, 0.60f) : new Color(0.24f, 0.24f, 0.25f));
            MarkStatic(go);
        }

        static Vector2 Point(float[] points, int i)
        {
            return new Vector2(points[i * 2], points[i * 2 + 1]);
        }

        static void MakeBoundaryWall(float[] points, Transform parent, MaterialLibrary materials)
        {
            int count = points.Length / 2;
            var mat = materials.Get("Boundary_Wall", new Color(0.62f, 0.58f, 0.52f));
            for (int i = 0; i < count; i++)
            {
                Vector2 a = Point(points, i);
                Vector2 b = Point(points, (i + 1) % count);
                Vector2 mid = (a + b) / 2f;
                float length = Vector2.Distance(a, b);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Wall " + i;
                go.transform.SetParent(parent);
                go.transform.position = new Vector3(mid.x, 1.25f, mid.y);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(b.x - a.x, 0f, b.y - a.y));
                go.transform.localScale = new Vector3(0.4f, 2.5f, length);
                SetMaterial(go, mat);
                MarkStatic(go);
            }
        }

        static void MakeLabel(PlaceData place, Transform parent)
        {
            var go = new GameObject("Label - " + place.name);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(place.x, 32f, place.z);
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
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 320f;
            RenderSettings.fogColor = new Color(0.70f, 0.72f, 0.72f);
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
            cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor;
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

            var spawn = new Vector3(layout.spawn.x, 0.1f, layout.spawn.z);
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
