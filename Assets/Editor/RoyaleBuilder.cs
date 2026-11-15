using System;
using System.Collections.Generic;
using Jagti.Gameplay;
using Jagti.Player;
using Jagti.Royale;
using Jagti.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.EditorTools
{
    [Serializable]
    public class LootPointData
    {
        public float x, y, z;
        public int zone;
        public int level;
        public string table;
        public float chance;
        public string room;
    }

    [Serializable]
    public class LootPointList
    {
        public string info;
        public int layoutVersion;
        public LootPointData[] points;
    }

    /// <summary>
    /// Battle royale scene setup (Phase 5A), called by CampusGreyboxBuilder after the NavMesh bake
    /// in place of the zombie GameplayBuilder: the match referee, the haze, the helicopter, loot
    /// points snapped onto the floors, the player's match components, the map texture and the HUD.
    /// Materials are made here so the runtime code needs no Shader.Find (which fails in builds).
    /// </summary>
    public static class RoyaleBuilder
    {
        const string LootPath = "Assets/Data/loot_points.json";
        const string InteriorPath = "Assets/Data/interiors.json";

        public static void Build(CampusLayout layout, FirstPersonController player, Transform root, Func<float, float, float> ground)
        {
            var br = new GameObject("Battle Royale").transform;
            br.SetParent(root, false);

            // ---------------------------------------------------------------- materials
            var palette = new Material[LootVisuals.PaletteSize];
            palette[LootVisuals.Metal] = ProceduralAssets.LitMaterial("Loot_Metal", new Color(0.32f, 0.33f, 0.35f), null, 0.6f);
            palette[LootVisuals.Black] = ProceduralAssets.LitMaterial("Loot_Black", new Color(0.07f, 0.07f, 0.08f), null, 0.3f);
            palette[LootVisuals.Wood] = ProceduralAssets.LitMaterial("Loot_Wood", new Color(0.55f, 0.36f, 0.2f), null, 0.25f);
            palette[LootVisuals.Olive] = ProceduralAssets.LitMaterial("Loot_Olive", new Color(0.3f, 0.34f, 0.2f), null, 0.15f);
            palette[LootVisuals.Red] = ProceduralAssets.LitMaterial("Loot_Red", new Color(0.75f, 0.1f, 0.1f), null, 0.2f);
            palette[LootVisuals.White] = ProceduralAssets.LitMaterial("Loot_White", new Color(0.9f, 0.9f, 0.88f), null, 0.15f);
            palette[LootVisuals.Brass] = ProceduralAssets.LitMaterial("Loot_Brass", new Color(0.75f, 0.6f, 0.25f), null, 0.6f);
            palette[LootVisuals.Khaki] = ProceduralAssets.LitMaterial("Loot_Khaki", new Color(0.6f, 0.53f, 0.36f), null, 0.1f);
            palette[LootVisuals.Blue] = ProceduralAssets.LitMaterial("Loot_Blue", new Color(0.15f, 0.25f, 0.5f), null, 0.15f);

            var rings = new Material[4];
            for (int i = 0; i < 4; i++)
                rings[i] = UnlitMaterial("Loot_Ring_" + RoyaleItems.TierNames[i], RoyaleItems.TierColours[i] * 1.2f, null, Blend.Alpha, 0.8f, false);

            var streaks = HazeTexture();
            var hazeMat = UnlitMaterial("Haze_Wall", new Color(0.62f, 0.5f, 0.38f, 0.55f), streaks, Blend.Alpha, 1f, true);
            var tracer = UnlitMaterial("Tracer", new Color(1f, 0.85f, 0.5f, 0.9f), null, Blend.Additive, 1f, true);
            var puffTex = SoftDisc();
            var puff = ParticleMaterial("Impact_Puff", puffTex);
            Ballistics.tracerMaterial = tracer;   // also kept on the director for play mode (statics reset)

            var skins = new[]
            {
                ProceduralAssets.LitMaterial("Body_Skin_1", new Color(0.78f, 0.6f, 0.46f), null),
                ProceduralAssets.LitMaterial("Body_Skin_2", new Color(0.62f, 0.45f, 0.32f), null),
                ProceduralAssets.LitMaterial("Body_Skin_3", new Color(0.48f, 0.34f, 0.24f), null),
            };
            // Campus clothes: hoodies and tees in batch colours, kurtas, track tops.
            var shirts = new[]
            {
                ProceduralAssets.LitMaterial("Body_Shirt_Navy", new Color(0.14f, 0.18f, 0.34f), null),
                ProceduralAssets.LitMaterial("Body_Shirt_Maroon", new Color(0.45f, 0.1f, 0.12f), null),
                ProceduralAssets.LitMaterial("Body_Shirt_Olive", new Color(0.33f, 0.36f, 0.2f), null),
                ProceduralAssets.LitMaterial("Body_Shirt_White", new Color(0.82f, 0.8f, 0.75f), null),
                ProceduralAssets.LitMaterial("Body_Shirt_Mustard", new Color(0.7f, 0.55f, 0.15f), null),
                ProceduralAssets.LitMaterial("Body_Shirt_Teal", new Color(0.12f, 0.4f, 0.42f), null),
                ProceduralAssets.LitMaterial("Body_Shirt_Grey", new Color(0.4f, 0.4f, 0.42f), null),
            };
            var pants = new[]
            {
                ProceduralAssets.LitMaterial("Body_Pants_Jeans", new Color(0.2f, 0.25f, 0.38f), null),
                ProceduralAssets.LitMaterial("Body_Pants_Black", new Color(0.1f, 0.1f, 0.11f), null),
                ProceduralAssets.LitMaterial("Body_Pants_Khaki", new Color(0.5f, 0.45f, 0.32f), null),
                ProceduralAssets.LitMaterial("Body_Pants_Track", new Color(0.12f, 0.14f, 0.2f), null),
            };
            var gear = ProceduralAssets.LitMaterial("Body_Gear", new Color(0.1f, 0.09f, 0.08f), null, 0.2f);
            var gunMat = ProceduralAssets.LitMaterial("Body_Gun", new Color(0.08f, 0.08f, 0.09f), null, 0.5f);
            var canopies = new[]
            {
                ProceduralAssets.LitMaterial("Canopy_Saffron", new Color(0.95f, 0.5f, 0.1f), null, 0.3f),
                ProceduralAssets.LitMaterial("Canopy_Teal", new Color(0.1f, 0.6f, 0.6f), null, 0.3f),
                ProceduralAssets.LitMaterial("Canopy_Red", new Color(0.8f, 0.15f, 0.15f), null, 0.3f),
                ProceduralAssets.LitMaterial("Canopy_Yellow", new Color(0.95f, 0.8f, 0.2f), null, 0.3f),
                ProceduralAssets.LitMaterial("Canopy_Purple", new Color(0.45f, 0.25f, 0.65f), null, 0.3f),
            };

            // ---------------------------------------------------------------- haze
            var hazeGo = new GameObject("Haze");
            hazeGo.transform.SetParent(br, false);
            var haze = hazeGo.AddComponent<HazeZone>();
            haze.wallMaterial = hazeMat;
            haze.boundary = layout.boundary;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i + 1 < layout.boundary.Length; i += 2)
            {
                minX = Mathf.Min(minX, layout.boundary[i]); maxX = Mathf.Max(maxX, layout.boundary[i]);
                minZ = Mathf.Min(minZ, layout.boundary[i + 1]); maxZ = Mathf.Max(maxZ, layout.boundary[i + 1]);
            }
            var centre = new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f);
            float radius = 0f;
            for (int i = 0; i + 1 < layout.boundary.Length; i += 2)
                radius = Mathf.Max(radius, (new Vector2(layout.boundary[i], layout.boundary[i + 1]) - centre).magnitude);
            haze.startCentre = centre;
            haze.startRadius = radius + 40f;
            float low = float.MaxValue;
            for (float x = minX; x <= maxX; x += 25f)
                for (float z = minZ; z <= maxZ; z += 25f)
                    low = Mathf.Min(low, ground(x, z));
            haze.wallBottom = low - 80f;
            haze.wallTop = low + 700f;

            // ---------------------------------------------------------------- loot
            var lootGo = new GameObject("Loot");
            lootGo.transform.SetParent(br, false);
            var loot = lootGo.AddComponent<LootDirector>();
            loot.palette = palette;
            loot.tierRings = rings;
            int points = FillLootPoints(loot, ground);

            // ---------------------------------------------------------------- helicopter
            var heliGo = new GameObject("Helicopter");
            heliGo.transform.SetParent(br, false);
            var heli = heliGo.AddComponent<Helicopter>();
            heli.bodyMaterial = ProceduralAssets.LitMaterial("Heli_Body", new Color(0.16f, 0.27f, 0.26f), null, 0.45f);
            heli.stripeMaterial = ProceduralAssets.LitMaterial("Heli_Stripe", new Color(0.92f, 0.9f, 0.84f), null, 0.4f);
            heli.glassMaterial = ProceduralAssets.LitMaterial("Heli_Glass", new Color(0.08f, 0.12f, 0.16f), null, 0.92f);
            heli.darkMaterial = ProceduralAssets.LitMaterial("Heli_Dark", new Color(0.08f, 0.08f, 0.09f), null, 0.3f);

            // ---------------------------------------------------------------- the player
            var pgo = player.gameObject;
            var health = pgo.AddComponent<Health>();
            health.max = 100f;
            var me = pgo.AddComponent<Combatant>();
            me.isPlayer = true;
            me.displayName = "You";
            me.eye = player.cameraPivot;
            var diver = pgo.AddComponent<SkyDiver>();
            diver.helicopter = heli;
            diver.skin = skins[0];
            diver.shirt = shirts[0];
            diver.pants = pants[0];
            diver.gear = gear;
            diver.gun = gunMat;
            diver.canopy = canopies[0];
            var arsenal = pgo.AddComponent<PlayerArsenal>();
            arsenal.palette = palette;
            arsenal.grenadeTemplate = GrenadeTemplate(br, palette, ParticleMaterial("Blast_Smoke", puffTex));

            // ---------------------------------------------------------------- referee and HUD
            var dirGo = new GameObject("Match Director");
            dirGo.transform.SetParent(br, false);
            var director = dirGo.AddComponent<MatchDirector>();
            director.player = me;
            director.playerDiver = diver;
            director.helicopter = heli;
            director.haze = haze;
            director.loot = loot;
            director.skins = skins;
            director.shirts = shirts;
            director.pants = pants;
            director.gearMaterial = gear;
            director.gunMaterial = gunMat;
            director.canopies = canopies;
            director.crateMaterial = ProceduralAssets.LitMaterial("Supply_Crate", new Color(0.3f, 0.36f, 0.24f), null, 0.2f);
            director.chuteMaterial = ProceduralAssets.LitMaterial("Supply_Chute", new Color(0.85f, 0.25f, 0.15f), null, 0.2f);
            director.smokeMaterial = ParticleMaterial("Red_Smoke", puffTex);
            var fx = dirGo.AddComponent<ShotEffectsSetup>();
            fx.tracer = tracer;
            fx.puff = puff;

            Rect mapWorld;
            var map = MapTexture(layout, ground, out mapWorld);
            var hudGo = new GameObject("Royale HUD");
            hudGo.transform.SetParent(br, false);
            var hud = hudGo.AddComponent<RoyaleHud>();
            hud.player = me;
            hud.arsenal = arsenal;
            hud.diver = diver;
            hud.fpc = player;
            hud.director = director;
            hud.haze = haze;
            hud.mapTexture = map;
            hud.mapWorld = mapWorld;
            hud.places = layout.places;

            Debug.Log("[Jagti] Battle royale: " + points + " loot points, helicopter, haze over " + Mathf.RoundToInt(haze.startRadius) +
                      " m, " + director.botCount + " bots.");
        }

        // ------------------------------------------------------------------ loot points

        [Serializable]
        class ZoneBase
        {
            public string name;
            public float baseX, baseZ;
        }

        [Serializable]
        class ZoneList
        {
            public ZoneBase[] zones;
        }

        static int FillLootPoints(LootDirector loot, Func<float, float, float> ground)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(LootPath);
            if (json == null)
            {
                Debug.LogWarning("[Jagti] " + LootPath + " is missing; run tools/royale/build_loot_points.py. The match will have no loot.");
                return 0;
            }
            var list = JsonUtility.FromJson<LootPointList>(json.text);
            var zones = new ZoneList();
            var ij = AssetDatabase.LoadAssetAtPath<TextAsset>(InteriorPath);
            if (ij != null) zones = JsonUtility.FromJson<ZoneList>(ij.text);

            Physics.SyncTransforms();
            var pos = new List<Vector3>();
            var tables = new List<string>();
            var chances = new List<float>();
            var indoor = new List<bool>();
            int dropped = 0;
            foreach (var p in list.points)
            {
                Vector3 at;
                if (p.zone >= 0)
                {
                    if (zones.zones == null || p.zone >= zones.zones.Length) { dropped++; continue; }
                    var z = zones.zones[p.zone];
                    float floor = ground(z.baseX, z.baseZ) + p.y;
                    // Snap onto the floor (or a stair step or the raised rows of a case room).
                    RaycastHit hit;
                    if (!Physics.Raycast(new Vector3(p.x, floor + 1.5f, p.z), Vector3.down, out hit, 2.6f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        dropped++;
                        continue;
                    }
                    if (hit.point.y > floor + 0.9f) { dropped++; continue; }   // landed on furniture
                    at = hit.point + Vector3.up * 0.01f;
                }
                else
                {
                    float g = ground(p.x, p.z);
                    RaycastHit hit;
                    at = new Vector3(p.x, g, p.z);
                    if (Physics.Raycast(new Vector3(p.x, g + 3f, p.z), Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.point.y > g + 1.2f) { dropped++; continue; }   // on a wall, bench or car
                        at = hit.point + Vector3.up * 0.01f;
                    }
                }
                pos.Add(at);
                tables.Add(p.table);
                chances.Add(p.chance);
                indoor.Add(p.zone >= 0);
            }
            loot.points = pos.ToArray();
            loot.pointTables = tables.ToArray();
            loot.pointChances = chances.ToArray();
            loot.pointIndoor = indoor.ToArray();
            if (dropped > 0) Debug.Log("[Jagti] Loot: " + dropped + " points had no clear floor and were skipped.");
            return pos.Count;
        }

        // ------------------------------------------------------------------ grenade

        static GameObject GrenadeTemplate(Transform parent, Material[] palette, Material blast)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Grenade Template";
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(0.09f, 0.11f, 0.09f);
            go.GetComponent<Renderer>().sharedMaterial = palette[LootVisuals.Olive];
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var g = go.AddComponent<Grenade>();
            g.blastMaterial = blast;
            go.SetActive(false);
            return go;
        }

        // ------------------------------------------------------------------ materials

        enum Blend { Alpha, Additive }

        static Material UnlitMaterial(string name, Color color, Texture2D tex, Blend blend, float alpha, bool twoSided)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            var m = new Material(shader) { name = name, enableInstancing = true };
            color.a = Mathf.Min(color.a, alpha);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            // URP transparent surface.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", blend == Blend.Additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", blend == Blend.Additive ? (int)BlendMode.One : (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", blend == Blend.Additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            ProceduralAssets.EnsureFolder(ProceduralAssets.Folder + "/Materials");
            AssetDatabase.CreateAsset(m, ProceduralAssets.Folder + "/Materials/" + name + ".mat");
            return m;
        }

        static Material ParticleMaterial(string name, Texture2D tex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            ProceduralAssets.EnsureFolder(ProceduralAssets.Folder + "/Materials");
            AssetDatabase.CreateAsset(m, ProceduralAssets.Folder + "/Materials/" + name + ".mat");
            return m;
        }

        // Soft round puff for smoke and dust particles.
        static Texture2D SoftDisc()
        {
            const int size = 64;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                    float n = 0.75f + 0.25f * ProceduralAssets.Noise(x / (float)size * 8f, y / (float)size * 8f, 8, 5);
                    float a = Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * n;
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            return ProceduralAssets.SaveTexture("Tex_Soft_Puff", size, px, true);
        }

        // Vertical streaks of thicker and thinner haze; tiles around the wall.
        static Texture2D HazeTexture()
        {
            const int size = 128;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float streak = ProceduralAssets.Noise(u * 8f, v * 2f, 8, 2, 31);
                    float fine = ProceduralAssets.Fbm(u, v, 8, 3, 32);
                    float a = Mathf.Clamp01(0.45f + 0.4f * streak + 0.25f * (fine - 0.5f));
                    float shade = 0.85f + 0.15f * fine;
                    px[y * size + x] = new Color(shade, shade * 0.95f, shade * 0.88f, a);
                }
            var tex = ProceduralAssets.SaveTexture("Tex_Haze", size, px, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        // ------------------------------------------------------------------ map

        /// <summary>
        /// A top-down campus map: shaded hills, lawns, water, roads and buildings, north (+Z) up.
        /// </summary>
        static Texture2D MapTexture(CampusLayout layout, Func<float, float, float> ground, out Rect world)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i + 1 < layout.boundary.Length; i += 2)
            {
                minX = Mathf.Min(minX, layout.boundary[i]); maxX = Mathf.Max(maxX, layout.boundary[i]);
                minZ = Mathf.Min(minZ, layout.boundary[i + 1]); maxZ = Mathf.Max(maxZ, layout.boundary[i + 1]);
            }
            float span = Mathf.Max(maxX - minX, maxZ - minZ) + 160f;
            float cx = (minX + maxX) / 2f, cz = (minZ + maxZ) / 2f;
            world = new Rect(cx - span / 2f, cz - span / 2f, span, span);
            const int size = 1024;
            float mpp = span / size;
            var px = new Color[size * size];

            // Terrain: colour by height, with hill shading from the north-west.
            float lo = float.MaxValue, hi = float.MinValue;
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float g = ground(world.x + (x + 0.5f) * mpp, world.y + (y + 0.5f) * mpp);
                    h[y * size + x] = g;
                    lo = Mathf.Min(lo, g);
                    hi = Mathf.Max(hi, g);
                }
            var lowCol = new Color(0.3f, 0.38f, 0.24f);
            var highCol = new Color(0.62f, 0.6f, 0.48f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float g = h[y * size + x];
                    float gx = h[y * size + Mathf.Min(size - 1, x + 1)] - h[y * size + Mathf.Max(0, x - 1)];
                    float gz = h[Mathf.Min(size - 1, y + 1) * size + x] - h[Mathf.Max(0, y - 1) * size + x];
                    Vector3 n = new Vector3(-gx, 2f * mpp * 2f, -gz).normalized;
                    float shade = Mathf.Clamp01(Vector3.Dot(n, new Vector3(-0.5f, 0.75f, 0.45f).normalized)) * 0.6f + 0.55f;
                    Color c = Color.Lerp(lowCol, highCol, Mathf.InverseLerp(lo, hi, g)) * shade;
                    // Contour lines every 10 m.
                    if (Mathf.Abs(Mathf.Repeat(g, 10f) - 5f) > 4.6f) c *= 0.85f;
                    c.a = 1f;
                    px[y * size + x] = c;
                }

            float originX = world.x, originZ = world.y;
            Func<float, float, Vector2> P = (wx, wz) => new Vector2((wx - originX) / mpp, (wz - originZ) / mpp);
            Action<Vector2, Vector2, float, Color> Segment = (a, b, width, col) =>
            {
                int x0 = Mathf.Max(0, (int)(Mathf.Min(a.x, b.x) - width)), x1 = Mathf.Min(size - 1, (int)(Mathf.Max(a.x, b.x) + width));
                int y0 = Mathf.Max(0, (int)(Mathf.Min(a.y, b.y) - width)), y1 = Mathf.Min(size - 1, (int)(Mathf.Max(a.y, b.y) + width));
                Vector2 ab = b - a;
                float len2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        if ((a + ab * t - p).magnitude <= width / 2f) px[y * size + x] = col;
                    }
            };

            foreach (var area in layout.areas)
            {
                Color col;
                switch (area.type)
                {
                    case "lawn": case "field": col = new Color(0.36f, 0.52f, 0.28f); break;
                    case "court": col = new Color(0.55f, 0.4f, 0.3f); break;
                    case "track": col = new Color(0.62f, 0.32f, 0.24f); break;
                    case "parking": col = new Color(0.5f, 0.5f, 0.49f); break;
                    default: col = new Color(0.66f, 0.64f, 0.58f); break;
                }
                Vector2 a = P(area.x - area.width / 2f, area.z - area.depth / 2f), b = P(area.x + area.width / 2f, area.z + area.depth / 2f);
                bool ellipse = area.shape == "ellipse";
                for (int y = Mathf.Max(0, (int)a.y); y <= Mathf.Min(size - 1, (int)b.y); y++)
                    for (int x = Mathf.Max(0, (int)a.x); x <= Mathf.Min(size - 1, (int)b.x); x++)
                    {
                        if (ellipse)
                        {
                            float ex = (x - (a.x + b.x) / 2f) / Mathf.Max(1f, (b.x - a.x) / 2f);
                            float ey = (y - (a.y + b.y) / 2f) / Mathf.Max(1f, (b.y - a.y) / 2f);
                            if (ex * ex + ey * ey > 1f) continue;
                        }
                        px[y * size + x] = col;
                    }
            }
            var waterCol = new Color(0.3f, 0.5f, 0.66f);
            if (layout.water != null)
                foreach (var wtr in layout.water)
                    for (int i = 0; i + 3 < wtr.points.Length; i += 2)
                        Segment(P(wtr.points[i], wtr.points[i + 1]), P(wtr.points[i + 2], wtr.points[i + 3]), Mathf.Max(2f, wtr.width / mpp), waterCol);
            if (layout.pools != null)
                foreach (var pool in layout.pools)
                    Segment(P(pool.x, pool.z), P(pool.x + 0.01f, pool.z), pool.radius * 2f / mpp, waterCol);
            foreach (var road in layout.roads)
                for (int i = 0; i + 3 < road.points.Length; i += 2)
                    Segment(P(road.points[i], road.points[i + 1]), P(road.points[i + 2], road.points[i + 3]), Mathf.Max(2f, road.width / mpp), new Color(0.78f, 0.77f, 0.72f));
            var wall = new Color(0.25f, 0.22f, 0.2f);
            for (int i = 0; i + 1 < layout.boundary.Length; i += 2)
            {
                int j = (i + 2) % layout.boundary.Length;
                Segment(P(layout.boundary[i], layout.boundary[i + 1]), P(layout.boundary[j], layout.boundary[j + 1]), 2f, wall);
            }
            foreach (var b in layout.buildings)
            {
                Vector2 a = P(b.x - b.width / 2f, b.z - b.depth / 2f), c = P(b.x + b.width / 2f, b.z + b.depth / 2f);
                for (int y = Mathf.Max(0, (int)a.y); y <= Mathf.Min(size - 1, (int)c.y); y++)
                    for (int x = Mathf.Max(0, (int)a.x); x <= Mathf.Min(size - 1, (int)c.x); x++)
                    {
                        bool edge = x - (int)a.x < 2 || (int)c.x - x < 2 || y - (int)a.y < 2 || (int)c.y - y < 2;
                        px[y * size + x] = edge ? new Color(0.3f, 0.28f, 0.26f) : new Color(0.9f, 0.88f, 0.82f);
                    }
            }

            var c32 = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++) c32[i] = px[i];
            var tex = ProceduralAssets.SaveTexture("Tex_Campus_Map", size, c32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
