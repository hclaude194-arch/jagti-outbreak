using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Jagti.World;

namespace Jagti.EditorTools
{
    [Serializable]
    public class OutdoorPropModel
    {
        public string name;
        public string fbx;
        public bool solid;      // gets box colliders (lamp posts, benches, fences, railings)
        public bool shadows;    // casts shadows
    }

    [Serializable]
    public class OutdoorProp
    {
        public string prop;
        public float x, y, z;   // y above the ground at (gx, gz)
        public float yaw;       // degrees clockwise from +Z
        public float gx, gz;
    }

    [Serializable]
    public class OutdoorSign
    {
        public string text;
        public float x, y, z;   // centre of the text; y above the ground at (gx, gz)
        public float yaw;       // the direction the text faces (where its readers stand)
        public float size;      // letter height in metres
        public float gx, gz;
        public float[] color;
    }

    [Serializable]
    public class OutdoorList
    {
        public OutdoorPropModel[] propModels;
        public OutdoorProp[] props;
        public OutdoorSign[] signs;
    }

    /// <summary>
    /// Sets out the street furniture listed in Assets/Data/outdoor_props.json (made by
    /// tools/blender/build_outdoor.py): lamp posts, benches, bins, name boards and signposts,
    /// railings, court fences and hoops, and the text on every sign and shop fascia.
    /// Runs before the NavMesh bake, so zombies walk round benches and fences.
    /// </summary>
    public static class OutdoorBuilder
    {
        const string ListPath = "Assets/Data/outdoor_props.json";
        const int FontSize = 64;

        public const string CoverPath = "Assets/Data/cover_props.json";

        /// <summary>How far away each kind of prop is still drawn (metres); cover stays visible far out
        /// because fights depend on it. Anything not listed uses DefaultCull.</summary>
        static readonly Dictionary<string, float> CullDistance = new Dictionary<string, float>
        {
            { "Bollard_Light", 90f }, { "Twin_Dustbin", 110f }, { "Cycle_Stand", 110f }, { "Planter_Box", 120f },
            { "Park_Bench", 120f }, { "Sign_Arm", 120f }, { "Sign_Post", 140f }, { "Volleyball_Net", 160f },
            { "Badminton_Net", 120f }, { "Garden_Lamp", 140f }, { "Palm_Small", 140f }, { "Tennis_Net", 160f },
            { "Railing_Segment", 180f }, { "Fence_Panel", 200f }, { "Name_Board", 200f }, { "Basketball_Hoop", 200f },
            { "Lamp_Post", 260f },
        };
        const float DefaultCull = 180f;
        const float CoverCull = 450f;
        const float SignTextCull = 60f;

        public static void Build(Transform parent, Func<float, float, float> ground)
        {
            Build(parent, ground, ListPath);
        }

        /// <summary>Places the props of one list (the street furniture, or the battle royale cover).</summary>
        public static void Build(Transform parent, Func<float, float, float> ground, string listPath)
        {
            var cullRenderers = new List<Renderer>();
            var cullDistances = new List<float>();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(listPath);
            if (json == null) return;
            var list = JsonUtility.FromJson<OutdoorList>(json.text);
            if (list == null) return;
            var materials = BuildingMaterials.Get();

            var assets = new Dictionary<string, GameObject>();
            var models = new Dictionary<string, OutdoorPropModel>();
            if (list.propModels != null)
            {
                foreach (var pm in list.propModels)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(pm.fbx);
                    if (asset == null)
                    {
                        Debug.LogWarning("[Jagti] Outdoor model missing: " + pm.fbx);
                        continue;
                    }
                    assets[pm.name] = asset;
                    models[pm.name] = pm;
                }
            }

            // The props were exported like the interior furniture, so they take the same import correction.
            Quaternion turn = InteriorBuilder.PropTurn;
            Vector3 scale = InteriorBuilder.PropScale;
            var groups = new Dictionary<string, Transform>();
            int placed = 0;
            if (list.props != null)
            {
                foreach (var p in list.props)
                {
                    GameObject asset;
                    if (!assets.TryGetValue(p.prop, out asset)) continue;
                    Transform group;
                    if (!groups.TryGetValue(p.prop, out group))
                    {
                        group = new GameObject(p.prop.Replace('_', ' ')).transform;
                        group.SetParent(parent, false);
                        groups[p.prop] = group;
                    }
                    var model = models[p.prop];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    go.transform.SetParent(group);
                    go.transform.position = new Vector3(p.x, ground(p.gx, p.gz) + p.y, p.z);
                    go.transform.rotation = Quaternion.Euler(0f, p.yaw, 0f) * turn;
                    go.transform.localScale = scale;
                    BuildingModels.ApplyMaterials(go, materials, false);
                    float cull;
                    if (!CullDistance.TryGetValue(p.prop, out cull)) cull = p.prop.StartsWith("Cover_") ? CoverCull : DefaultCull;
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    {
                        cullRenderers.Add(r);
                        cullDistances.Add(cull);
                        if (model.solid) r.gameObject.AddComponent<BoxCollider>();
                        r.shadowCastingMode = model.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                            StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
                    }
                    placed++;
                }
            }

            int signs = 0;
            if (list.signs != null && list.signs.Length > 0)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var root = new GameObject("Sign Text").transform;
                root.SetParent(parent, false);
                foreach (var s in list.signs)
                {
                    if (string.IsNullOrEmpty(s.text)) continue;
                    var go = new GameObject("Text - " + s.text);
                    go.transform.SetParent(root);
                    go.transform.position = new Vector3(s.x, ground(s.gx, s.gz) + s.y, s.z);
                    // TextMesh reads from its -Z side, so turn it to face the readers.
                    go.transform.rotation = Quaternion.Euler(0f, s.yaw + 180f, 0f);
                    var tm = go.AddComponent<TextMesh>();
                    tm.text = s.text;
                    tm.fontSize = FontSize;
                    tm.characterSize = s.size * 10f / FontSize;
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center;
                    tm.color = s.color != null && s.color.Length >= 3 ? new Color(s.color[0], s.color[1], s.color[2]) : Color.white;
                    var mr = go.GetComponent<MeshRenderer>();
                    if (font != null)
                    {
                        tm.font = font;
                        mr.sharedMaterial = font.material;
                    }
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    cullRenderers.Add(mr);
                    cullDistances.Add(SignTextCull);
                    signs++;
                }
            }
            if (cullRenderers.Count > 0)
            {
                var culler = parent.gameObject.AddComponent<PropCuller>();
                culler.renderers = cullRenderers.ToArray();
                culler.distances = cullDistances.ToArray();
            }
            Debug.Log("[Jagti] " + listPath + ": " + placed + " props, " + signs + " signs.");
        }
    }
}
