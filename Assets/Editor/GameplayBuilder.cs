using System.Collections.Generic;
using Jagti.Gameplay;
using Jagti.Player;
using Jagti.World;
using UnityEditor;
using UnityEngine;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Phase 3 scene setup, called by CampusGreyboxBuilder after the NavMesh bake:
    /// player survival components and weapons, the zombie spawner with its clothing materials,
    /// supply crates outside every building, and the survival HUD.
    /// </summary>
    public static class GameplayBuilder
    {
        public delegate float GroundFunc(float x, float z);

        public static void Build(CampusLayout layout, FirstPersonController player, Transform root, GroundFunc ground)
        {
            var go = player.gameObject;
            var health = go.AddComponent<Health>();
            health.max = 100f;
            var inventory = go.AddComponent<Inventory>();
            var vitals = go.AddComponent<PlayerVitals>();
            var interactor = go.AddComponent<Interactor>();
            interactor.cameraPivot = player.cameraPivot;

            var wood = ProceduralAssets.LitMaterial("Weapon_Wood", new Color(0.78f, 0.62f, 0.42f), null, 0.25f);
            var metal = ProceduralAssets.LitMaterial("Weapon_Metal", new Color(0.12f, 0.12f, 0.13f), null, 0.55f);
            var grip = ProceduralAssets.LitMaterial("Weapon_Grip", new Color(0.05f, 0.05f, 0.05f), null, 0.1f);
            var weapons = go.AddComponent<WeaponController>();
            weapons.cameraPivot = player.cameraPivot;
            weapons.woodMaterial = wood;
            weapons.metalMaterial = metal;
            weapons.gripMaterial = grip;

            var spawnerGo = new GameObject("Zombie Spawner");
            var spawner = spawnerGo.AddComponent<ZombieSpawner>();
            spawner.player = go.transform;
            spawner.skins = new[]
            {
                ProceduralAssets.LitMaterial("Zombie_Skin_1", new Color(0.55f, 0.6f, 0.48f), null),
                ProceduralAssets.LitMaterial("Zombie_Skin_2", new Color(0.62f, 0.55f, 0.5f), null),
                ProceduralAssets.LitMaterial("Zombie_Skin_3", new Color(0.45f, 0.42f, 0.38f), null),
            };
            // Campus clothes: white kurtas, college tees, a mess worker's apron grey, a guard's khaki.
            spawner.shirts = new[]
            {
                ProceduralAssets.LitMaterial("Zombie_Shirt_White", new Color(0.78f, 0.76f, 0.7f), BloodTexture("Shirt_White", 1)),
                ProceduralAssets.LitMaterial("Zombie_Shirt_Navy", new Color(0.16f, 0.2f, 0.36f), BloodTexture("Shirt_Navy", 2)),
                ProceduralAssets.LitMaterial("Zombie_Shirt_Maroon", new Color(0.45f, 0.12f, 0.14f), BloodTexture("Shirt_Maroon", 3)),
                ProceduralAssets.LitMaterial("Zombie_Shirt_Khaki", new Color(0.55f, 0.5f, 0.35f), BloodTexture("Shirt_Khaki", 4)),
                ProceduralAssets.LitMaterial("Zombie_Shirt_Grey", new Color(0.45f, 0.45f, 0.45f), BloodTexture("Shirt_Grey", 5)),
            };
            spawner.pants = new[]
            {
                ProceduralAssets.LitMaterial("Zombie_Pants_Jeans", new Color(0.2f, 0.25f, 0.38f), null),
                ProceduralAssets.LitMaterial("Zombie_Pants_Black", new Color(0.1f, 0.1f, 0.1f), null),
                ProceduralAssets.LitMaterial("Zombie_Pants_Khaki", new Color(0.5f, 0.45f, 0.32f), null),
            };

            int crates = PlaceCrates(layout, Child(root, "Supply Crates"), ground);

            var hudGo = new GameObject("Game HUD");
            var hud = hudGo.AddComponent<GameHud>();
            hud.playerHealth = health;
            hud.vitals = vitals;
            hud.inventory = inventory;
            hud.weapons = weapons;
            hud.interactor = interactor;
            hud.spawner = spawner;

            Debug.Log("[Jagti] Survival setup: zombie spawner, " + crates + " supply crates, weapons and HUD.");
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // Light blood spatter over the cloth colour (white base so the material colour tints it).
        static Texture2D BloodTexture(string name, int seed)
        {
            const int size = 128;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float cloth = 0.9f + 0.1f * ProceduralAssets.Noise(u * 32f, v * 32f, 32, seed);
                    float stain = ProceduralAssets.Fbm(u, v, 4, 4, seed * 17);
                    Color c = new Color(cloth, cloth, cloth);
                    if (stain > 0.62f) c = Color.Lerp(c, new Color(0.35f, 0.05f, 0.04f), Mathf.Clamp01((stain - 0.62f) * 8f));
                    else if (stain < 0.3f) c *= 0.8f; // grime
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            }
            return ProceduralAssets.SaveTexture("Tex_Zombie_" + name, size, px, false);
        }

        static Texture2D CrateTexture(string name, float shade)
        {
            const int size = 256;
            var px = new Color32[size * size];
            var baseWood = new Color(0.62f, 0.47f, 0.3f) * shade;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float grain = ProceduralAssets.Noise(u * 4f, v * 64f, 4, 64, 71);
                    Color c = baseWood * (0.8f + 0.35f * grain);
                    float plank = (v * 4f) % 1f;
                    if (plank < 0.03f) c *= 0.45f;                    // gaps between planks
                    if (u < 0.08f || u > 0.92f) c *= 0.8f;            // corner battens
                    if (Mathf.Abs(u - v) < 0.04f) c *= 0.85f;         // diagonal brace
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            }
            return ProceduralAssets.SaveTexture(name, size, px, false);
        }

        static int PlaceCrates(CampusLayout layout, Transform parent, GroundFunc ground)
        {
            var fresh = ProceduralAssets.LitMaterial("Crate_Wood", Color.white, CrateTexture("Tex_Crate", 1f), 0.1f);
            var empty = ProceduralAssets.LitMaterial("Crate_Searched", new Color(0.55f, 0.55f, 0.55f), CrateTexture("Tex_Crate_Searched", 0.7f), 0.05f);
            var rng = new System.Random(3301);
            Physics.SyncTransforms(); // roads were just created; make them visible to raycasts
            int count = 0;

            foreach (var b in layout.buildings)
            {
                string table = LootTables.ForBuilding(b.name, b.zone);
                int n = Mathf.Clamp(1 + b.floors / 3, 1, 3);
                for (int k = 0; k < n; k++)
                {
                    Vector3 p;
                    if (!FindCrateSpot(layout, b, rng, ground, out p)) continue;
                    MakeCrate(parent, p, (float)rng.NextDouble() * 360f, table, Label(table), fresh, empty);
                    count++;
                }
                // The guard post at the main gate keeps a shotgun.
                if (b.name == "Main Gate")
                {
                    Vector3 p;
                    if (FindCrateSpot(layout, b, rng, ground, out p))
                    {
                        MakeCrate(parent, p, 0f, "armory", "Guard's locker", fresh, empty);
                        count++;
                    }
                }
            }

            // A starter crate a few steps in front of the spawn point.
            var spawn = new Vector3(layout.spawn.x, 0f, layout.spawn.z);
            Vector3 dir = new Vector3(75f, 0f, 288f) - spawn;
            dir.y = 0f;
            Vector3 s = spawn + dir.normalized * 4f + Vector3.Cross(Vector3.up, dir.normalized) * 1.5f;
            s.y = SurfaceY(s.x, s.z, ground);
            MakeCrate(parent, s, 20f, "general", "Crate", fresh, empty);
            return count + 1;
        }

        static string Label(string table)
        {
            switch (table)
            {
                case "medical": return "Medical supplies";
                case "security": return "Guard's box";
                case "mess": return "Kitchen crate";
                case "hostel": return "Student's trunk";
                case "sports": return "Sports kit box";
                case "academic": return "Office supplies";
            }
            return "Crate";
        }

        // A spot just outside one of the building's walls, on open ground and not inside any other building.
        static bool FindCrateSpot(CampusLayout layout, BuildingData b, System.Random rng, GroundFunc ground, out Vector3 result)
        {
            result = Vector3.zero;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int side = rng.Next(4);
                float along = (float)rng.NextDouble() - 0.5f;
                float hx = b.width / 2f, hz = b.depth / 2f;
                float x = b.x, z = b.z;
                const float gap = 1.3f;
                switch (side)
                {
                    case 0: x += along * b.width * 0.8f; z += hz + gap; break;
                    case 1: x += along * b.width * 0.8f; z -= hz + gap; break;
                    case 2: z += along * b.depth * 0.8f; x += hx + gap; break;
                    default: z += along * b.depth * 0.8f; x -= hx + gap; break;
                }
                if (InsideAnyBuilding(layout, x, z, 0.8f)) continue;
                float y = SurfaceY(x, z, ground);
                // Skip steep spots (the crate would float or sink).
                float slope = Mathf.Abs(ground(x + 1f, z) - ground(x - 1f, z)) + Mathf.Abs(ground(x, z + 1f) - ground(x, z - 1f));
                if (slope > 1.2f) continue;
                result = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        static bool InsideAnyBuilding(CampusLayout layout, float x, float z, float margin)
        {
            foreach (var o in layout.buildings)
            {
                if (Mathf.Abs(x - o.x) < o.width / 2f + margin && Mathf.Abs(z - o.z) < o.depth / 2f + margin) return true;
            }
            return false;
        }

        // Top of whatever is underfoot (road or terrain), ignoring roofs well above the ground.
        static float SurfaceY(float x, float z, GroundFunc ground)
        {
            float g = ground(x, z);
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(x, g + 2f, z), Vector3.down, out hit, 4f)) return hit.point.y;
            return g;
        }

        static void MakeCrate(Transform parent, Vector3 p, float yaw, string table, string label, Material fresh, Material empty)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Crate - " + label;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(0.9f, 0.55f, 0.6f);
            go.transform.position = p + Vector3.up * 0.26f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = fresh;
            var crate = go.AddComponent<LootCrate>();
            crate.table = table;
            crate.label = label;
            crate.lidRenderer = r;
            crate.searchedMaterial = empty;
        }
    }
}
