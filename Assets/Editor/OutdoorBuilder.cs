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
            { "Bollard_Light", 90f }, { "Twin_Dustbin", 110f }, { "Cycle_Stand", 110f }, { "Planter_Box", 120f }, { "Kerbed_Bed", 130f },
            { "Park_Bench", 120f }, { "Sign_Arm", 120f }, { "Sign_Post", 140f }, { "Volleyball_Net", 160f },
            { "Badminton_Net", 120f }, { "Garden_Lamp", 140f }, { "Palm_Small", 140f }, { "Tennis_Net", 160f },
            { "Railing_Segment", 180f }, { "Fence_Panel", 200f }, { "Name_Board", 200f }, { "Basketball_Hoop", 200f },
            { "Lamp_Post", 260f },
            { "Shrub_Border", 110f }, { "Young_Tree", 140f },
            { "Map_Board", 160f }, { "Stone_Wall", 220f }, { "Stone_Wall_High", 260f }, { "Flood_Mast", 400f },
        };
        /// <summary>A box collider round the part of a prop the player can walk into: the
        /// footprint of everything up to 2.2 m above its base, taken up to the prop's full height.
        /// A box round the whole mesh reached out under a lamp post's arm, a hoop's backboard or a
        /// floodlight head, and players stuck on thin air there.</summary>
        static void FitBaseCollider(MeshRenderer r)
        {
            var filter = r.GetComponent<MeshFilter>();
            var box = r.gameObject.AddComponent<BoxCollider>();
            if (filter == null || filter.sharedMesh == null) return;
            var verts = filter.sharedMesh.vertices;
            if (verts.Length == 0) return;
            // Which local axis points up in the world (the FBX import may turn the axes).
            Vector3 up = r.transform.InverseTransformDirection(Vector3.up);
            int ax = 0;
            for (int k = 1; k < 3; k++) if (Mathf.Abs(up[k]) > Mathf.Abs(up[ax])) ax = k;
            float sign = up[ax] >= 0f ? 1f : -1f;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in verts) { lo = Mathf.Min(lo, v[ax] * sign); hi = Mathf.Max(hi, v[ax] * sign); }
            float scaleUp = Mathf.Max(1e-4f, Mathf.Abs(r.transform.lossyScale[ax]));
            float cut = lo + 2.2f / scaleUp;
            var bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var v in verts)
            {
                if (v[ax] * sign > cut) continue;
                bmin = Vector3.Min(bmin, v);
                bmax = Vector3.Max(bmax, v);
            }
            if (bmin.x > bmax.x) return;
            // Full height on the up axis, the base footprint on the other two.
            bmin[ax] = sign > 0f ? lo : -hi;
            bmax[ax] = sign > 0f ? hi : -lo;
            box.center = (bmin + bmax) / 2f;
            box.size = Vector3.Max(bmax - bmin, new Vector3(0.05f, 0.05f, 0.05f));
        }

        const float DefaultCull = 180f;
        const float FlagCull = 190f;

        /// <summary>Feather flags (owner's photos, 2026-11-20): the colour of each Feather_Flag_* prop's
        /// sail. The FBX gives every sail the one slot Flag_Cloth; here it gets a waving cloth
        /// material of its own colour (Jagti/Foliage with _Wave on).</summary>
        public static readonly Dictionary<string, Color> FlagColours = new Dictionary<string, Color>
        {
            { "Yellow", new Color(0.93f, 0.76f, 0.10f) }, { "Red", new Color(0.78f, 0.12f, 0.13f) },
            { "Purple", new Color(0.42f, 0.20f, 0.62f) }, { "Pink", new Color(0.88f, 0.26f, 0.58f) },
            { "Orange", new Color(0.95f, 0.47f, 0.10f) }, { "Lilac", new Color(0.72f, 0.60f, 0.86f) },
            { "Green", new Color(0.45f, 0.72f, 0.20f) }, { "White", new Color(0.92f, 0.92f, 0.90f) },
            { "Blue", new Color(0.16f, 0.40f, 0.80f) },
        };
        static Dictionary<string, Material> flagMaterials;
        static Texture2D flagWeave;
        static Material streetGlow;

        static Material FlagMaterial(string colour)
        {
            if (flagMaterials == null) flagMaterials = new Dictionary<string, Material>();
            Material m;
            if (flagMaterials.TryGetValue(colour, out m) && m != null) return m;
            Color c;
            if (!FlagColours.TryGetValue(colour, out c)) c = Color.white;
            if (flagWeave == null)
                flagWeave = ProceduralAssets.GroundTexture("Flag_Weave", new Color(0.86f, 0.86f, 0.86f), new Color(0.95f, 0.95f, 0.95f), Color.white, 16, 0.25f, 91, true);
            m = ProceduralAssets.FoliageMaterial("Flag_" + colour, c, flagWeave, 0f, 0f);
            if (m.HasProperty("_Wave"))
            {
                m.SetFloat("_Wave", 1f);
                m.SetFloat("_Cull", 0f);              // one sheet, seen from both sides
                m.SetFloat("_Translucency", 0.55f);   // the sun glows through the cloth
            }
            else if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m);
            flagMaterials[colour] = m;
            return m;
        }

        /// <summary>The outdoor lamps' heads: their own emissive material, which StreetLights turns up at night
        /// (the interiors keep Lamp_Glow).</summary>
        static Material StreetGlow()
        {
            if (streetGlow != null) return streetGlow;
            streetGlow = AssetDatabase.LoadAssetAtPath<Material>(ProceduralAssets.Folder + "/Materials/Street_Lamp_Glow.mat");
            if (streetGlow != null) return streetGlow;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            streetGlow = new Material(shader) { name = "Street_Lamp_Glow", enableInstancing = true };
            var c = new Color(0.96f, 0.96f, 0.94f);
            if (streetGlow.HasProperty("_BaseColor")) streetGlow.SetColor("_BaseColor", c);
            if (streetGlow.HasProperty("_Smoothness")) streetGlow.SetFloat("_Smoothness", 0.6f);
            streetGlow.EnableKeyword("_EMISSION");
            if (streetGlow.HasProperty("_EmissionColor")) streetGlow.SetColor("_EmissionColor", c * 0.6f);
            streetGlow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            ProceduralAssets.EnsureFolder(ProceduralAssets.Folder + "/Materials");
            AssetDatabase.CreateAsset(streetGlow, ProceduralAssets.Folder + "/Materials/Street_Lamp_Glow.mat");
            return streetGlow;
        }

        static int LampKind(string prop)
        {
            switch (prop)
            {
                case "Lamp_Post": return (int)StreetLights.Kind.Street;
                case "Garden_Lamp": return (int)StreetLights.Kind.Garden;
                case "Bollard_Light": return (int)StreetLights.Kind.Bollard;
                case "Flood_Mast": return (int)StreetLights.Kind.Flood;
            }
            return -1;
        }

        /// <summary>World centre of the parts of a renderer that use the given material, or false.</summary>
        static bool SubmeshCentre(MeshRenderer r, Material mat, out Vector3 centre)
        {
            centre = Vector3.zero;
            var filter = r.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            var mesh = filter.sharedMesh;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
            {
                if (mats[i] != mat) continue;
                centre = r.transform.TransformPoint(mesh.GetSubMesh(i).bounds.center);
                return true;
            }
            return false;
        }
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
            // Flag and lamp materials are made once per campus build and shared by both lists (a
            // rebuild deletes Assets/Generated, which turns the cached ones into null).
            var lampHeads = new List<Vector3>();
            var lampFeet = new List<Vector3>();
            var lampKinds = new List<int>();
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
                    bool flag = p.prop.StartsWith("Feather_Flag_");
                    int lampKind = LampKind(p.prop);
                    if (flag || lampKind >= 0)
                    {
                        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                        {
                            var slots = r.sharedMaterials;
                            for (int i = 0; i < slots.Length; i++)
                            {
                                if (slots[i] == null) continue;
                                string key = slots[i].name.Replace(" (Instance)", "");
                                if (flag && key == "Flag_Cloth") slots[i] = FlagMaterial(p.prop.Substring("Feather_Flag_".Length));
                                else if (lampKind >= 0 && (key == "Lamp_Glow" || key.EndsWith("Lamp_Glow"))) slots[i] = StreetGlow();
                            }
                            r.sharedMaterials = slots;
                        }
                    }
                    if (lampKind >= 0)
                    {
                        // Where the light comes from: the middle of the glowing parts (a floodlight's
                        // ring of lamps gives one point at the ring's centre).
                        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                        {
                            Vector3 head;
                            if (!SubmeshCentre(r, StreetGlow(), out head)) continue;
                            lampHeads.Add(head);
                            lampFeet.Add(new Vector3(head.x, ground(head.x, head.z) + 0.3f, head.z));
                            lampKinds.Add(lampKind);
                            break;
                        }
                    }
                    float cull;
                    if (!CullDistance.TryGetValue(p.prop, out cull)) cull = p.prop.StartsWith("Cover_") ? CoverCull : flag ? FlagCull : DefaultCull;
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    {
                        cullRenderers.Add(r);
                        cullDistances.Add(cull);
                        if (model.solid) FitBaseCollider(r);
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
            if (lampHeads.Count > 0)
            {
                var lights = parent.gameObject.AddComponent<StreetLights>();
                lights.heads = lampHeads.ToArray();
                lights.feet = lampFeet.ToArray();
                lights.kinds = lampKinds.ToArray();
                lights.glow = StreetGlow();
                lights.halo = AtmosphereBuilder.LampHaloMaterial();
                lights.pool = AtmosphereBuilder.LampPoolMaterial();
            }
            if (cullRenderers.Count > 0)
            {
                var culler = parent.gameObject.AddComponent<PropCuller>();
                culler.renderers = cullRenderers.ToArray();
                culler.distances = cullDistances.ToArray();
            }
            Debug.Log("[Jagti] " + listPath + ": " + placed + " props, " + signs + " signs, " + lampHeads.Count + " lamps.");
        }
    }
}
