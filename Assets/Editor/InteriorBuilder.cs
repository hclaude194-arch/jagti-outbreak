using System;
using System.Collections.Generic;
using Jagti.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.EditorTools
{
    [Serializable]
    public class InteriorZoneData
    {
        public string name;
        public string shell;
        public float[] min;     // world x, z
        public float[] max;
        public float baseX;     // the building's base sits on the ground here
        public float baseZ;
        public float floorOffset;   // ground floor level above the base (0.6 m)
        public int maxLevel;        // top storey (0 = single storey)
    }

    [Serializable]
    public class PropModelData
    {
        public string name;
        public string fbx;
        public bool solid;      // gets box colliders (desks, tables, shelves, counters)
    }

    [Serializable]
    public class PropData
    {
        public string prop;
        public float x, y, z;   // y above the building base
        public float yaw;
        public int zone;
        public int level;       // storey: 0 = ground floor
    }

    [Serializable]
    public class InteriorLightData
    {
        public float x, y, z;
        public float range;
        public float intensity;
        public float[] color;
        public int zone;
        public int level;
    }

    [Serializable]
    public class InteriorList
    {
        public BuildingModelInfo[] shells;
        public InteriorZoneData[] zones;
        public PropModelData[] propModels;
        public PropData[] props;
        public InteriorLightData[] lights;
        public float levelHeight;
    }

    /// <summary>
    /// Builds the walk-in interiors listed in Assets/Data/interiors.json (made by
    /// tools/blender/build_interiors.py): the interior shell models, the furniture and the room
    /// lights, grouped per building under an InteriorZone that hides them from outside.
    /// Runs before the NavMesh bake so zombies can follow the player indoors.
    /// </summary>
    public static class InteriorBuilder
    {
        const string ListPath = "Assets/Data/interiors.json";

        /// <summary>The import correction of the first interior shell (turn and scale); the
        /// furniture and the outdoor props were exported the same way, so they take it too.</summary>
        public static Quaternion PropTurn = Quaternion.identity;
        public static Vector3 PropScale = Vector3.one;

        public static void Build(Transform parent, Func<float, float, float> ground)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(ListPath);
            if (json == null) return;
            var list = JsonUtility.FromJson<InteriorList>(json.text);
            if (list == null || list.shells == null || list.zones == null) return;

            PropTurn = Quaternion.identity;
            PropScale = Vector3.one;
            bool correctionSet = false;
            JagtiProjectSetup.EnsureForwardPlus();
            var materials = BuildingMaterials.Get();

            var propAssets = new Dictionary<string, GameObject>();
            var propSolid = new Dictionary<string, bool>();
            if (list.propModels != null)
            {
                foreach (var pm in list.propModels)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(pm.fbx);
                    if (asset == null)
                    {
                        Debug.LogWarning("[Jagti] Furniture model missing: " + pm.fbx);
                        continue;
                    }
                    propAssets[pm.name] = asset;
                    propSolid[pm.name] = pm.solid;
                }
            }

            var shells = new Dictionary<string, BuildingModelInfo>();
            foreach (var s in list.shells) shells[s.name] = s;

            var zoneRoots = new List<InteriorZone>();
            var zoneRenderers = new List<List<Renderer>>();
            var zoneRendererLevels = new List<List<int>>();
            var zoneLights = new List<List<Light>>();
            var zoneLightLevels = new List<List<int>>();
            var zoneBase = new List<float>();
            var zoneTurn = new List<Quaternion>();
            var zoneScale = new List<Vector3>();
            var zoneFurniture = new List<Transform>();
            var zoneLightRoot = new List<Transform>();

            foreach (var z in list.zones)
            {
                var root = new GameObject("Interior - " + z.name);
                root.transform.SetParent(parent);
                var zone = root.AddComponent<InteriorZone>();
                zone.min = new Vector2(z.min[0], z.min[1]);
                zone.max = new Vector2(z.max[0], z.max[1]);
                var renderers = new List<Renderer>();
                Quaternion turn = Quaternion.identity;
                Vector3 scale = Vector3.one;

                BuildingModelInfo info;
                if (shells.TryGetValue(z.shell, out info) && AssetDatabase.LoadAssetAtPath<GameObject>(info.fbx) != null)
                {
                    var shell = BuildingModels.PlaceOne(info, root.transform, ground, materials);
                    if (shell != null)
                    {
                        // The clear glass behind the facade's windows breaks too (with the facade pane).
                        Material clear;
                        if (materials.TryGetValue("Glass_Clear", out clear)) GlassSplitter.Split(shell, clear);
                        // Furniture was modelled the same way as the shell, so it takes the same correction.
                        turn = shell.transform.rotation;
                        scale = shell.transform.localScale;
                        if (!correctionSet)
                        {
                            PropTurn = turn;
                            PropScale = scale;
                            correctionSet = true;
                        }
                        foreach (var r in shell.GetComponentsInChildren<Renderer>()) renderers.Add(r);
                    }
                }
                else
                {
                    Debug.LogWarning("[Jagti] Interior shell missing for " + z.name + "; skipping its furniture too.");
                }

                zoneRoots.Add(zone);
                zoneRenderers.Add(renderers);
                var shellLevels = new List<int>();
                for (int i = 0; i < renderers.Count; i++) shellLevels.Add(-1);   // the shell: drawn on every floor
                zoneRendererLevels.Add(shellLevels);
                zoneLights.Add(new List<Light>());
                zoneLightLevels.Add(new List<int>());
                zoneBase.Add(ground(z.baseX, z.baseZ));
                zone.baseY = zoneBase[zoneBase.Count - 1];
                if (list.levelHeight > 0f) zone.levelHeight = list.levelHeight;
                if (z.floorOffset > 0f) zone.floorOffset = z.floorOffset;
                zone.maxLevel = Mathf.Max(0, z.maxLevel);
                zoneTurn.Add(turn);
                zoneScale.Add(scale);
                zoneFurniture.Add(Child(root.transform, "Furniture"));
                zoneLightRoot.Add(Child(root.transform, "Lights"));
            }

            int placed = 0;
            if (list.props != null)
            {
                foreach (var p in list.props)
                {
                    if (p.zone < 0 || p.zone >= zoneRoots.Count) continue;
                    GameObject asset;
                    if (!propAssets.TryGetValue(p.prop, out asset)) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    go.transform.SetParent(zoneFurniture[p.zone]);
                    go.transform.position = new Vector3(p.x, zoneBase[p.zone] + p.y, p.z);
                    go.transform.rotation = Quaternion.Euler(0f, p.yaw, 0f) * zoneTurn[p.zone];
                    go.transform.localScale = zoneScale[p.zone];
                    BuildingModels.ApplyMaterials(go, materials, false);
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (propSolid[p.prop]) r.gameObject.AddComponent<BoxCollider>();
                        // Small things cast no shadows (the room lights cast none anyway).
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        zoneRenderers[p.zone].Add(r);
                        zoneRendererLevels[p.zone].Add(p.level);
                    }
                    placed++;
                }
            }

            if (list.lights != null)
            {
                foreach (var l in list.lights)
                {
                    if (l.zone < 0 || l.zone >= zoneRoots.Count) continue;
                    var go = new GameObject("Room Light");
                    go.transform.SetParent(zoneLightRoot[l.zone]);
                    go.transform.position = new Vector3(l.x, zoneBase[l.zone] + l.y, l.z);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = l.range;
                    light.intensity = l.intensity;
                    light.color = l.color != null && l.color.Length >= 3 ? new Color(l.color[0], l.color[1], l.color[2]) : Color.white;
                    light.shadows = LightShadows.None;
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                    zoneLights[l.zone].Add(light);
                    zoneLightLevels[l.zone].Add(l.level);
                }
            }

            for (int i = 0; i < zoneRoots.Count; i++)
            {
                zoneRoots[i].renderers = zoneRenderers[i].ToArray();
                zoneRoots[i].rendererLevels = zoneRendererLevels[i].ToArray();
                zoneRoots[i].lights = zoneLights[i].ToArray();
                zoneRoots[i].lightLevels = zoneLightLevels[i].ToArray();
            }
            Debug.Log("[Jagti] Interiors: " + zoneRoots.Count + " building(s), " + placed + " pieces of furniture, " +
                      (list.lights != null ? list.lights.Length : 0) + " room lights.");
        }

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
