using System;
using System.Collections.Generic;
using Jagti.Gameplay;
using Jagti.Player;
using Jagti.Royale;
using Jagti.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
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
        public float yaw;           // stashes only
    }

    [Serializable]
    public class LootPointList
    {
        public string info;
        public int layoutVersion;
        public LootPointData[] points;
        public LootPointData[] stashes;
    }

    /// <summary>
    /// Battle royale scene setup (Phase 5A), called by CampusGreyboxBuilder after the NavMesh bake
    /// in place of the zombie GameplayBuilder: the match referee, the haze, the sky bus, loot
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
            palette[LootVisuals.Yellow] = ProceduralAssets.LitMaterial("Loot_Yellow", new Color(0.95f, 0.75f, 0.12f), null, 0.3f);
            palette[LootVisuals.Green] = ProceduralAssets.LitMaterial("Loot_Green", new Color(0.12f, 0.42f, 0.2f), null, 0.3f);

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

            // ---------------------------------------------------------------- sky bus
            var heliGo = new GameObject("Sky Bus");
            heliGo.transform.SetParent(br, false);
            var heli = heliGo.AddComponent<SkyBus>();
            // Palette in SkyBusModel slot order: white, navy, glass, dark, saffron, yellow, red, green, metal.
            heli.palette = new[]
            {
                ProceduralAssets.LitMaterial("SkyBus_White", new Color(0.93f, 0.93f, 0.9f), null, 0.35f),
                ProceduralAssets.LitMaterial("SkyBus_Navy", new Color(0.09f, 0.13f, 0.3f), null, 0.4f),
                ProceduralAssets.LitMaterial("SkyBus_Glass", new Color(0.08f, 0.12f, 0.16f), null, 0.92f),
                ProceduralAssets.LitMaterial("SkyBus_Dark", new Color(0.07f, 0.07f, 0.08f), null, 0.3f),
                ProceduralAssets.LitMaterial("SkyBus_Saffron", new Color(0.98f, 0.55f, 0.12f), null, 0.15f),
                ProceduralAssets.LitMaterial("SkyBus_Yellow", new Color(0.98f, 0.8f, 0.15f), null, 0.3f),
                ProceduralAssets.LitMaterial("SkyBus_Red", new Color(0.8f, 0.12f, 0.1f), null, 0.3f),
                ProceduralAssets.LitMaterial("SkyBus_Green", new Color(0.15f, 0.6f, 0.25f), null, 0.3f),
                ProceduralAssets.LitMaterial("SkyBus_Metal", new Color(0.42f, 0.43f, 0.45f), null, 0.5f),
            };
            var flame = ProceduralAssets.LitMaterial("SkyBus_Flame", new Color(1f, 0.6f, 0.2f), null, 0.1f);
            flame.EnableKeyword("_EMISSION");
            if (flame.HasProperty("_EmissionColor")) flame.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.12f) * 4f);
            heli.flameMaterial = flame;

            // ---------------------------------------------------------------- the player
            var pgo = player.gameObject;
            var health = pgo.AddComponent<Health>();
            health.max = 100f;
            var me = pgo.AddComponent<Combatant>();
            me.isPlayer = true;
            me.displayName = "You";
            // Bots aim at the head, which the controller keeps over the body in first and third person
            // (the camera itself pulls back behind the shoulder in third person).
            var headMark = new GameObject("Head").transform;
            headMark.SetParent(pgo.transform, false);
            headMark.localPosition = player.cameraPivot.localPosition;
            player.headMarker = headMark;
            me.eye = headMark;
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
            arsenal.flashMaterial = UnlitMaterial("Muzzle_Flash", new Color(1f, 0.78f, 0.42f), FlashTexture(), Blend.Additive, 1f, true);
            arsenal.grenadeTemplate = GrenadeTemplate(br, palette, ParticleMaterial("Blast_Smoke", puffTex));
            arsenal.smokeTemplate = SmokeTemplate(br, palette, ParticleMaterial("Smoke_Screen", puffTex));

            // Getting around (5D): vaulting, wading, riding and zip lines. The walker's capsule is off while
            // riding or zipping, so a separate hit area stands in for it.
            var riderGo = new GameObject("Rider Hitbox");
            riderGo.transform.SetParent(pgo.transform, false);
            var riderCol = riderGo.AddComponent<CapsuleCollider>();
            riderCol.center = new Vector3(0f, 1.05f, 0f);
            riderCol.height = 1.5f;
            riderCol.radius = 0.3f;
            riderGo.AddComponent<Hitbox>().owner = health;
            riderCol.enabled = false;
            var mover = pgo.AddComponent<PlayerMover>();
            mover.riderHitbox = riderCol;
            FillWater(mover, layout);

            var blastSmoke = ParticleMaterial("Wreck_Smoke", puffTex);
            int vehicleSpots = PlaceVehicles(layout, br, palette, blastSmoke, ground);
            int zipLines = PlaceZipLines(layout, br, ground);

            // ---------------------------------------------------------------- referee and HUD
            var dirGo = new GameObject("Match Director");
            dirGo.transform.SetParent(br, false);
            var director = dirGo.AddComponent<MatchDirector>();
            director.player = me;
            director.playerDiver = diver;
            director.helicopter = heli;
            director.haze = haze;
            director.loot = loot;
            director.grenadeTemplate = arsenal.grenadeTemplate;
            director.smokeTemplate = arsenal.smokeTemplate;
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
            hud.mover = mover;
            hud.placeLoot = RatePlaces(layout, loot);
            loot.places = layout.places;
            loot.placeRating = hud.placeLoot;

            Debug.Log("[Jagti] Battle royale: " + points + " loot points, " + loot.stashPoints.Length + " stash spots, sky bus, haze over " + Mathf.RoundToInt(haze.startRadius) +
                      " m, " + director.botCount + " bots, " + vehicleSpots + " vehicle spots, " + zipLines + " zip lines.");
        }

        // ------------------------------------------------------------------ landing spots

        /// <summary>
        /// How much loot each named place has (loot spots within 90 m, weighted by their chance),
        /// split into thirds: 0 light, 1 good, 2 rich. The map shows it so players can pick a busy or a
        /// quiet landing.
        /// </summary>
        static int[] RatePlaces(CampusLayout layout, LootDirector loot)
        {
            int n = layout.places != null ? layout.places.Length : 0;
            var score = new float[n];
            if (loot.points != null)
                for (int i = 0; i < n; i++)
                {
                    var pl = layout.places[i];
                    for (int k = 0; k < loot.points.Length; k++)
                    {
                        float dx = loot.points[k].x - pl.x, dz = loot.points[k].z - pl.z;
                        if (dx * dx + dz * dz < 90f * 90f)
                            score[i] += loot.pointChances != null && k < loot.pointChances.Length ? loot.pointChances[k] : 1f;
                    }
                }
            var sorted = (float[])score.Clone();
            Array.Sort(sorted);
            float lo = n > 0 ? sorted[n / 3] : 0f, hi = n > 0 ? sorted[(2 * n) / 3] : 0f;
            var rating = new int[n];
            for (int i = 0; i < n; i++) rating[i] = score[i] >= hi ? 2 : score[i] >= lo ? 1 : 0;
            return rating;
        }

        // ------------------------------------------------------------------ getting around

        /// <summary>Water the player can wade through: the channel segments and the round basins.</summary>
        static void FillWater(PlayerMover mover, CampusLayout layout)
        {
            var segs = new List<Vector4>();
            var widths = new List<float>();
            if (layout.water != null)
                foreach (var w in layout.water)
                {
                    if (w.points == null) continue;
                    for (int i = 0; i + 3 < w.points.Length; i += 2)
                    {
                        segs.Add(new Vector4(w.points[i], w.points[i + 1], w.points[i + 2], w.points[i + 3]));
                        widths.Add(Mathf.Max(1f, w.width));
                    }
                }
            mover.channels = segs.ToArray();
            mover.channelWidths = widths.ToArray();
            var pools = new List<Vector3>();
            if (layout.pools != null)
                foreach (var p in layout.pools)
                    pools.Add(new Vector3(p.x, p.z, p.radius));
            mover.pools = pools.ToArray();
        }

        static bool InsidePolygon(float[] poly, float x, float z)
        {
            if (poly == null || poly.Length < 6) return true;
            bool inside = false;
            int n = poly.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = poly[i * 2], zi = poly[i * 2 + 1], xj = poly[j * 2], zj = poly[j * 2 + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Candidate parking spots for every kind of vehicle: along the kerb of every road at least 4 m wide
        /// (about one per 90 m) and a few bays in each car park, each on solid ground with nothing in the
        /// way. VehicleSpawner picks a random spread of them each match.
        /// </summary>
        static int PlaceVehicles(CampusLayout layout, Transform br, Material[] palette, Material smoke, Func<float, float, float> ground)
        {
            Physics.SyncTransforms();
            var spots = new List<Vector4>();
            var parking = new List<bool>();
            var half = new Vector3(1.0f, 0.65f, 1.95f);   // room for the jeep, the biggest
            Func<float, float, float, bool, bool> tryAdd = (x, z, yaw, park) =>
            {
                if (!InsidePolygon(layout.boundary, x, z)) return false;
                float g = ground(x, z);
                RaycastHit hit;
                float y = g;
                if (Physics.Raycast(new Vector3(x, g + 6f, z), Vector3.down, out hit, 9f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.point.y > g + 1.2f) return false;       // under a roof, a canopy or a bridge deck
                    y = hit.point.y;
                }
                // Level enough to park on.
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 f = rot * Vector3.forward, r = rot * Vector3.right;
                float gf = ground(x + f.x * 1.8f, z + f.z * 1.8f), gb = ground(x - f.x * 1.8f, z - f.z * 1.8f);
                float gl = ground(x - r.x, z - r.z), gr = ground(x + r.x, z + r.z);
                if (Mathf.Abs(gf - gb) > 0.9f || Mathf.Abs(gl - gr) > 0.5f) return false;
                float top = Mathf.Max(Mathf.Max(gf, gb), Mathf.Max(gl, gr), y);
                if (Physics.CheckBox(new Vector3(x, top + 0.35f + half.y, z), half, rot, ~0, QueryTriggerInteraction.Ignore)) return false;
                NavMeshHit nh;
                if (!NavMesh.SamplePosition(new Vector3(x, y, z), out nh, 2f, NavMesh.AllAreas)) return false;
                foreach (var s in spots)
                    if ((new Vector3(s.x, s.y, s.z) - new Vector3(x, y, z)).sqrMagnitude < 15f * 15f) return false;
                spots.Add(new Vector4(x, y + 0.05f, z, yaw));
                parking.Add(park);
                return true;
            };

            if (layout.roads != null)
                foreach (var road in layout.roads)
                {
                    if (road.points == null || road.width < 4f) continue;
                    if (road.name == "Highway" || road.name.StartsWith("Courtyard")) continue;   // off campus / inside a building's court
                    float carry = 45f;          // the first spot a little way in from the road's start
                    int side = 1;
                    for (int i = 0; i + 3 < road.points.Length; i += 2)
                    {
                        var a = new Vector2(road.points[i], road.points[i + 1]);
                        var b = new Vector2(road.points[i + 2], road.points[i + 3]);
                        float len = (b - a).magnitude;
                        if (len < 0.1f) continue;
                        Vector2 dir = (b - a) / len, perp = new Vector2(dir.y, -dir.x);
                        float t = carry;
                        while (t < len)
                        {
                            Vector2 p = a + dir * t + perp * side * (road.width / 2f - 1.1f);
                            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + (side < 0 ? 180f : 0f);
                            if (tryAdd(p.x, p.y, yaw, false)) side = -side;
                            t += 90f;
                        }
                        carry = t - len;
                    }
                }
            if (layout.areas != null)
                foreach (var area in layout.areas)
                {
                    if (area.type != "parking") continue;
                    bool wide = area.width >= area.depth;
                    float along = wide ? area.width : area.depth;
                    int n = Mathf.Clamp(Mathf.FloorToInt(along / 16f), 1, 4);
                    for (int k = 0; k < n; k++)
                    {
                        float u = (k + 0.5f) / n - 0.5f;
                        float x = area.x + (wide ? u * area.width : 0f);
                        float z = area.z + (wide ? 0f : u * area.depth);
                        tryAdd(x, z, wide ? 0f : 90f, true);   // nose in across the long side
                    }
                }

            var go = new GameObject("Vehicles");
            go.transform.SetParent(br, false);
            var spawner = go.AddComponent<VehicleSpawner>();
            spawner.spots = spots.ToArray();
            spawner.parking = parking.ToArray();
            spawner.palette = palette;
            spawner.smokeMaterial = smoke;
            return spots.Count;
        }

        /// <summary>
        /// Zip lines on the steep slopes: tower pairs 70-220 m apart with at least 8 m of drop, both bases on
        /// walkable ground inside the boundary, the sagging cable at least 2.5 m above the ground the whole way
        /// and nothing (buildings, trees) between the tower tops. The best few, kept well apart, are built.
        /// </summary>
        static int PlaceZipLines(CampusLayout layout, Transform br, Func<float, float, float> ground)
        {
            Physics.SyncTransforms();
            const float tower = 4.4f, step = 24f;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i + 1 < layout.boundary.Length; i += 2)
            {
                minX = Mathf.Min(minX, layout.boundary[i]); maxX = Mathf.Max(maxX, layout.boundary[i]);
                minZ = Mathf.Min(minZ, layout.boundary[i + 1]); maxZ = Mathf.Max(maxZ, layout.boundary[i + 1]);
            }
            // Tower sites: level-ish, open ground on the NavMesh.
            var sites = new List<Vector3>();
            for (float x = minX + step; x < maxX - step; x += step)
                for (float z = minZ + step; z < maxZ - step; z += step)
                {
                    if (!InsidePolygon(layout.boundary, x, z)) continue;
                    float g = ground(x, z);
                    float hi = g, lo = g;
                    for (int c = 0; c < 4; c++)
                    {
                        float h = ground(x + ((c & 1) == 0 ? -1.2f : 1.2f), z + ((c & 2) == 0 ? -1.2f : 1.2f));
                        hi = Mathf.Max(hi, h); lo = Mathf.Min(lo, h);
                    }
                    if (hi - lo > 1.0f) continue;
                    NavMeshHit nh;
                    if (!NavMesh.SamplePosition(new Vector3(x, g, z), out nh, 1.5f, NavMesh.AllAreas) || Mathf.Abs(nh.position.y - g) > 0.6f) continue;
                    if (Physics.CheckBox(new Vector3(x, hi + 0.3f + tower / 2f, z), new Vector3(1.3f, tower / 2f, 1.3f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    sites.Add(new Vector3(x, g, z));
                }

            var pairs = new List<KeyValuePair<float, Vector3[]>>();
            for (int i = 0; i < sites.Count; i++)
                for (int j = 0; j < sites.Count; j++)
                {
                    Vector3 a = sites[i], b = sites[j];
                    float dh = a.y - b.y;
                    if (dh < 8f) continue;
                    float len = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                    if (len < 70f || len > 220f || dh / len > 0.45f) continue;
                    Vector3 ta = a + Vector3.up * tower, tb = b + Vector3.up * tower;
                    float sag = (tb - ta).magnitude * 0.018f;
                    bool clear = true;
                    for (int k = 1; k < 24 && clear; k++)
                    {
                        float s = k / 24f;
                        Vector3 p = Vector3.Lerp(ta, tb, s) - Vector3.up * (4f * sag * s * (1f - s));
                        if (p.y - ground(p.x, p.z) < 2.5f) clear = false;
                    }
                    if (!clear) continue;
                    float score = dh * (1f - Mathf.Abs(len - 150f) / 250f);
                    pairs.Add(new KeyValuePair<float, Vector3[]>(score, new[] { a, b }));
                }
            pairs.Sort((p, q) => q.Key.CompareTo(p.Key));

            var root = new GameObject("Zip Lines").transform;
            root.SetParent(br, false);
            var steel = ProceduralAssets.LitMaterial("Zip_Steel", new Color(0.42f, 0.43f, 0.45f), null, 0.55f);
            var cable = ProceduralAssets.LitMaterial("Zip_Cable", new Color(0.12f, 0.12f, 0.13f), null, 0.6f);
            var paint = ProceduralAssets.LitMaterial("Zip_Paint", new Color(0.9f, 0.55f, 0.1f), null, 0.3f);
            var ends = new List<Vector3>();
            int built = 0;
            foreach (var pr in pairs)
            {
                if (built >= 5) break;
                Vector3 a = pr.Value[0], b = pr.Value[1];
                bool far = true;
                foreach (var e in ends)
                    if ((e - a).sqrMagnitude < 90f * 90f || (e - b).sqrMagnitude < 90f * 90f) { far = false; break; }
                if (!far) continue;
                // Nothing between the tops (start and stop just short of the towers themselves).
                Vector3 ta = a + Vector3.up * (tower - 0.3f), tb = b + Vector3.up * (tower - 0.3f);
                Vector3 d = (tb - ta).normalized;
                if (Physics.Linecast(ta + d * 2f, tb - d * 2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var go = new GameObject("Zip Line " + (built + 1));
                go.transform.SetParent(root, false);
                go.transform.position = a;
                var z = go.AddComponent<ZipLine>();
                z.a = a;
                z.b = b;
                z.towerHeight = tower;
                z.steel = steel;
                z.cable = cable;
                z.paint = paint;
                ends.Add(a);
                ends.Add(b);
                built++;
            }
            if (built == 0) Debug.LogWarning("[Jagti] No zip line sites found (" + sites.Count + " tower sites, " + pairs.Count + " pairs).");
            return built;
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
                if (!SnapPoint(p, zones, ground, out at)) { dropped++; continue; }
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

            // Stash spots: the same snapping, plus the trunk's facing.
            var sPos = new List<Vector3>();
            var sYaw = new List<float>();
            var sTables = new List<string>();
            var sChances = new List<float>();
            var sIndoor = new List<bool>();
            int sDropped = 0;
            if (list.stashes != null)
                foreach (var p in list.stashes)
                {
                    Vector3 at;
                    if (!SnapPoint(p, zones, ground, out at)) { sDropped++; continue; }
                    sPos.Add(at);
                    sYaw.Add(p.yaw);
                    sTables.Add(p.table);
                    sChances.Add(p.chance);
                    sIndoor.Add(p.zone >= 0);
                }
            else
                Debug.LogWarning("[Jagti] " + LootPath + " has no stashes; rerun tools/royale/build_loot_points.py.");
            loot.stashPoints = sPos.ToArray();
            loot.stashYaw = sYaw.ToArray();
            loot.stashTables = sTables.ToArray();
            loot.stashChances = sChances.ToArray();
            loot.stashIndoor = sIndoor.ToArray();
            if (sDropped > 0) Debug.Log("[Jagti] Loot: " + sDropped + " stash spots had no clear floor and were skipped.");
            return pos.Count;
        }

        /// <summary>Puts a loot point on the floor (indoors) or the ground; false when it lands on furniture or a wall.</summary>
        static bool SnapPoint(LootPointData p, ZoneList zones, Func<float, float, float> ground, out Vector3 at)
        {
            at = Vector3.zero;
            RaycastHit hit;
            if (p.zone >= 0)
            {
                if (zones.zones == null || p.zone >= zones.zones.Length) return false;
                var z = zones.zones[p.zone];
                float floor = ground(z.baseX, z.baseZ) + p.y;
                // Snap onto the floor (or a stair step or the raised rows of a case room).
                if (!Physics.Raycast(new Vector3(p.x, floor + 1.5f, p.z), Vector3.down, out hit, 2.6f, ~0, QueryTriggerInteraction.Ignore))
                    return false;
                if (hit.point.y > floor + 0.9f) return false;   // landed on furniture
                at = hit.point + Vector3.up * 0.01f;
                return true;
            }
            float g = ground(p.x, p.z);
            at = new Vector3(p.x, g, p.z);
            if (Physics.Raycast(new Vector3(p.x, g + 3f, p.z), Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.point.y > g + 1.2f) return false;   // on a wall, bench or car
                at = hit.point + Vector3.up * 0.01f;
            }
            return true;
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

        static GameObject SmokeTemplate(Transform parent, Material[] palette, Material smoke)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Smoke Grenade Template";
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(0.066f, 0.06f, 0.066f);
            go.GetComponent<Renderer>().sharedMaterial = palette[LootVisuals.Metal];
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.5f;
            col.height = 2f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var g = go.AddComponent<Grenade>();
            g.smoke = true;
            g.fuse = 1.6f;
            g.smokeMaterial = smoke;
            g.blastMaterial = smoke;
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

        // A muzzle flash: a hot core with four spikes and a softer glow, white in the middle.
        static Texture2D FlashTexture()
        {
            const int size = 64;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float spikes = Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(u) * 9f) * Mathf.Clamp01(1f - Mathf.Abs(v)),
                                             Mathf.Clamp01(1f - Mathf.Abs(v) * 9f) * Mathf.Clamp01(1f - Mathf.Abs(u)));
                    float core = Mathf.Clamp01(1f - r * 2.2f);
                    float glow = Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r) * 0.5f;
                    float a = Mathf.Clamp01(core + spikes * 0.8f + glow);
                    float white = Mathf.Clamp01(core * 1.5f);
                    // Premultiplied: the additive blend (One, One) ignores alpha.
                    px[y * size + x] = new Color(a, Mathf.Lerp(0.85f, 1f, white) * a, Mathf.Lerp(0.6f, 1f, white) * a, a);
                }
            return ProceduralAssets.SaveTexture("Tex_Muzzle_Flash", size, px, true);
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
                    case "plaza": col = new Color(0.72f, 0.70f, 0.66f); break;
                    default: col = new Color(0.66f, 0.64f, 0.58f); break;
                }
                Vector2 a = P(area.x - area.width / 2f, area.z - area.depth / 2f), b = P(area.x + area.width / 2f, area.z + area.depth / 2f);
                bool ellipse = area.shape == "ellipse";
                for (int y = Mathf.Max(0, (int)a.y); y <= Mathf.Min(size - 1, (int)b.y); y++)
                    for (int x = Mathf.Max(0, (int)a.x); x <= Mathf.Min(size - 1, (int)b.x); x++)
                    {
                        if (area.shape == "pill")
                        {
                            // Running track: straights joined by half circles.
                            float pcx = (a.x + b.x) / 2f, pcy = (a.y + b.y) / 2f;
                            float phw = Mathf.Max(1f, (b.x - a.x) / 2f), phh = Mathf.Max(1f, (b.y - a.y) / 2f);
                            float rr = Mathf.Min(phw, phh);
                            float qx = Mathf.Max(0f, Mathf.Abs(x - pcx) - (phw - rr)), qy = Mathf.Max(0f, Mathf.Abs(y - pcy) - (phh - rr));
                            if (qx * qx + qy * qy > rr * rr) continue;
                        }
                        else if (ellipse)
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
