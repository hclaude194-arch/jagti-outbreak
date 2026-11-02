using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Jagti.EditorTools
{
    [Serializable]
    public class BuildingModelInfo
    {
        public string name;
        public string fbx;
        public float pivotX;
        public float pivotZ;
        public float[] boundsMin; // world x, z
        public float[] boundsMax;
        public string[] replaces; // layout building names this model stands in for
    }

    [Serializable]
    public class BuildingModelList
    {
        public BuildingModelInfo[] models;
    }

    /// <summary>
    /// Places the Blender-made landmark buildings (Assets/Models/Buildings/*.fbx) listed in
    /// Assets/Data/building_models.json. A model that is missing (for example when Git LFS
    /// files were not downloaded) is skipped, and its grey blocks stay in the scene.
    /// </summary>
    public static class BuildingModels
    {
        const string ListPath = "Assets/Data/building_models.json";
        const string ModelFolder = "Assets/Models/Buildings";

        /// <summary>Loads the list and returns only the models whose FBX exists.</summary>
        public static List<BuildingModelInfo> Available()
        {
            var result = new List<BuildingModelInfo>();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(ListPath);
            if (json == null) return result;
            var list = JsonUtility.FromJson<BuildingModelList>(json.text);
            if (list == null || list.models == null) return result;
            foreach (var m in list.models)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(m.fbx) != null) result.Add(m);
                else Debug.LogWarning("[Jagti] Building model missing, keeping grey blocks: " + m.fbx +
                                      ". If the file exists but is tiny, Git LFS did not download it.");
            }
            return result;
        }

        public static void Place(List<BuildingModelInfo> models, Transform parent, Func<float, float, float> ground)
        {
            if (models.Count == 0) return;
            var materials = BuildingMaterials.Get();
            foreach (var info in models) PlaceOne(info, parent, ground, materials);
        }

        /// <summary>Places one model at its pivot on the ground, fixes scale/turn, swaps materials, adds colliders.</summary>
        public static GameObject PlaceOne(BuildingModelInfo info, Transform parent, Func<float, float, float> ground,
                                          Dictionary<string, Material> materials)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(info.fbx);
            if (asset == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.name = info.name.Replace('_', ' ');
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(info.pivotX, ground(info.pivotX, info.pivotZ), info.pivotZ);
            go.transform.rotation = Quaternion.identity;
            FixScaleAndTurn(go, info);
            ApplyMaterials(go, materials, true);
            return go;
        }

        /// <summary>Swaps the FBX's material slots for the procedural ones by name; optionally adds mesh colliders.</summary>
        public static void ApplyMaterials(GameObject go, Dictionary<string, Material> materials, bool meshColliders)
        {
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>())
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    string key = slots[i].name.Replace(" (Instance)", "");
                    Material mat;
                    if (materials.TryGetValue(key, out mat)) slots[i] = mat;
                }
                renderer.sharedMaterials = slots;
                var filter = renderer.GetComponent<MeshFilter>();
                if (meshColliders && filter != null && filter.sharedMesh != null && renderer.GetComponent<Collider>() == null)
                    renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        // The layout says where the model's footprint must be. If the FBX came in 100x too big or
        // small, or turned or mirrored, fix it here so a different exporter setting can't misplace it.
        static void FixScaleAndTurn(GameObject go, BuildingModelInfo info)
        {
            var expectedMin = new Vector2(info.boundsMin[0], info.boundsMin[1]);
            var expectedMax = new Vector2(info.boundsMax[0], info.boundsMax[1]);
            Vector2 expectedSize = expectedMax - expectedMin;
            Vector2 expectedCentre = (expectedMin + expectedMax) / 2f;

            if (!TryBounds(go, out Bounds b)) return;
            float ratio = Mathf.Max(b.size.x, b.size.z) / Mathf.Max(0.01f, Mathf.Max(expectedSize.x, expectedSize.y));
            if (ratio > 20f || ratio < 0.05f)
            {
                float s = ratio > 20f ? 0.01f : 100f;
                go.transform.localScale = go.transform.localScale * s;
                Debug.LogWarning("[Jagti] " + info.name + " imported at the wrong scale; corrected by x" + s + ".");
            }

            Vector3 baseScale = go.transform.localScale;
            float bestError = float.MaxValue;
            float bestTurn = 0f;
            bool bestMirror = false;
            foreach (bool mirror in new[] { false, true })
            {
                foreach (float turn in new[] { 0f, 180f, 90f, 270f })
                {
                    go.transform.rotation = Quaternion.Euler(0f, turn, 0f);
                    go.transform.localScale = mirror ? new Vector3(-baseScale.x, baseScale.y, baseScale.z) : baseScale;
                    if (!TryBounds(go, out b)) continue;
                    var centre = new Vector2(b.center.x, b.center.z);
                    var size = new Vector2(b.size.x, b.size.z);
                    float error = (centre - expectedCentre).magnitude + (size - expectedSize).magnitude * 0.5f;
                    if (error < bestError - 0.5f)
                    {
                        bestError = error;
                        bestTurn = turn;
                        bestMirror = mirror;
                    }
                }
            }
            go.transform.rotation = Quaternion.Euler(0f, bestTurn, 0f);
            go.transform.localScale = bestMirror ? new Vector3(-baseScale.x, baseScale.y, baseScale.z) : baseScale;
            if (bestTurn != 0f || bestMirror)
                Debug.Log("[Jagti] " + info.name + ": turned " + bestTurn + " degrees" + (bestMirror ? " and mirrored" : "") + " to match the layout.");
        }

        static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }
    }

    /// <summary>Import settings for the building and furniture FBX files: metres, no animation, no cameras or lights.</summary>
    public class BuildingModelImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Models/Buildings/") && !assetPath.StartsWith("Assets/Models/Props/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }
    }
}
