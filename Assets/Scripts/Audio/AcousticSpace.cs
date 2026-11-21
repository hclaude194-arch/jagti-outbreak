using System.Collections.Generic;
using Jagti.World;
using UnityEngine;

namespace Jagti.Audio
{
    /// <summary>
    /// Where a point is, acoustically: inside a building (one of the interior zones, with a roof over
    /// it) or out in the open, and how big the space round the listener is (from a ring of rays), which
    /// sets the room reverb. Also the surface under a point, for footsteps and bullet impacts.
    /// </summary>
    public static class AcousticSpace
    {
        static InteriorZone[] zones;
        static float zonesFound = -100f;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        static InteriorZone[] Zones()
        {
            if (zones == null || Time.unscaledTime - zonesFound > 20f)
            {
                zones = Object.FindObjectsOfType<InteriorZone>();
                zonesFound = Time.unscaledTime;
            }
            return zones;
        }

        /// <summary>The interior zone the point is inside (by its footprint and storeys), or null.</summary>
        public static InteriorZone ZoneAt(Vector3 p)
        {
            var zs = Zones();
            for (int i = 0; i < zs.Length; i++)
            {
                var z = zs[i];
                if (z == null) continue;
                if (p.x < z.min.x + 0.3f || p.x > z.max.x - 0.3f || p.z < z.min.y + 0.3f || p.z > z.max.y - 0.3f) continue;
                float top = z.baseY + z.floorOffset + (z.maxLevel + 1) * z.levelHeight;
                if (p.y < z.baseY - 1.5f || p.y > top) continue;
                return z;
            }
            return null;
        }

        /// <summary>True when something is overhead within 25 m (a ceiling, a porch, a roof).</summary>
        public static bool Covered(Vector3 p)
        {
            return Physics.Raycast(p + Vector3.up * 0.2f, Vector3.up, 25f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Inside a building: in an interior zone's footprint with a roof overhead.</summary>
        public static bool Indoors(Vector3 p)
        {
            return ZoneAt(p) != null && Covered(p);
        }

        /// <summary>
        /// Mean distance to walls round the point (rays out to `reach` in eight directions; misses count
        /// as `reach`), and how many of them hit something.
        /// </summary>
        public static float Openness(Vector3 p, float reach, out int walls)
        {
            walls = 0;
            float sum = 0f;
            Vector3 o = p + Vector3.up * 0.2f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f + 0.2f;
                var dir = new Vector3(Mathf.Cos(a), 0.08f, Mathf.Sin(a));
                RaycastHit h;
                if (Physics.Raycast(o, dir, out h, reach, ~0, QueryTriggerInteraction.Ignore) && !IsBody(h.collider))
                {
                    sum += h.distance;
                    walls++;
                }
                else sum += reach;
            }
            return sum / 8f;
        }

        static bool IsBody(Collider c)
        {
            return c is CharacterController || c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic;
        }

        // ------------------------------------------------------------------ surfaces

        public enum Surface { Grass, Dirt, Gravel, Concrete, Tile, Wood, Metal, Water, Glass }

        public static readonly string[] SurfaceNames = { "grass", "dirt", "gravel", "concrete", "tile", "wood", "metal", "water", "glass" };

        static readonly Dictionary<long, Surface> cache = new Dictionary<long, Surface>();
        static Terrain terrain;
        static float[,,] alpha;
        static Surface[] layerSurface;

        /// <summary>What the ground is made of under p (a ray straight down from just above it).</summary>
        public static Surface Under(Vector3 p, out bool found)
        {
            RaycastHit h;
            found = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out h, 1.6f, ~0, QueryTriggerInteraction.Ignore);
            if (!found) return Surface.Concrete;
            return Of(h);
        }

        public static Surface Of(RaycastHit h)
        {
            var c = h.collider;
            if (c == null) return Surface.Concrete;
            if (c is TerrainCollider) return OfTerrain(c.GetComponent<Terrain>(), h.point);
            int sub = 0;
            var mc = c as MeshCollider;
            if (mc != null && !mc.convex && mc.sharedMesh != null && h.triangleIndex >= 0) sub = SubmeshOf(mc.sharedMesh, h.triangleIndex);
            long key = (long)c.GetInstanceID() * 256L + (sub & 0xff);
            Surface s;
            if (cache.TryGetValue(key, out s)) return s;
            s = Surface.Concrete;
            var r = c.GetComponent<Renderer>();
            if (r == null) r = c.GetComponentInParent<Renderer>();
            string name = c.name;
            if (r != null)
            {
                var mats = r.sharedMaterials;
                if (mats != null && mats.Length > 0)
                {
                    var m = mats[Mathf.Clamp(sub, 0, mats.Length - 1)];
                    if (m != null) name = m.name + " " + name;
                }
            }
            s = FromName(name);
            if (Jagti.World.GlassPanes.IsGlass(c)) s = Surface.Glass;
            cache[key] = s;
            return s;
        }

        static readonly Dictionary<Mesh, int[]> subStarts = new Dictionary<Mesh, int[]>();

        static int SubmeshOf(Mesh mesh, int triangle)
        {
            if (mesh.subMeshCount <= 1) return 0;
            int[] starts;
            if (!subStarts.TryGetValue(mesh, out starts))
            {
                starts = new int[mesh.subMeshCount];
                for (int i = 0; i < mesh.subMeshCount; i++) starts[i] = (int)mesh.GetSubMesh(i).indexStart / 3;
                subStarts[mesh] = starts;
            }
            for (int i = starts.Length - 1; i >= 0; i--)
                if (triangle >= starts[i]) return i;
            return 0;
        }

        /// <summary>Material or object name to surface, by the words the project's materials use.</summary>
        public static Surface FromName(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("water")) return Surface.Water;
            if (n.Contains("glass") || n.Contains("mirror")) return Surface.Glass;
            if (n.Contains("metal") || n.Contains("steel") || n.Contains("rail") || n.Contains("solar") || n.Contains("iron") ||
                n.Contains("shutter") || n.Contains("escape") || n.Contains("fence") || n.Contains("lift") || n.Contains("vehicle") ||
                n.Contains("loot_metal") || n.Contains("crate") && !n.Contains("wood")) return Surface.Metal;
            if (n.Contains("wood") || n.Contains("door") || n.Contains("laminate") || n.Contains("bench") || n.Contains("desk") ||
                n.Contains("table") || n.Contains("shelf") || n.Contains("stage") || n.Contains("sports_floor")) return Surface.Wood;
            if (n.Contains("tile") || n.Contains("marble") || n.Contains("ceramic") || n.Contains("floor") || n.Contains("granite")) return Surface.Tile;
            if (n.Contains("carpet") || n.Contains("fabric") || n.Contains("rubber")) return Surface.Dirt;   // soft and dull
            if (n.Contains("grass") || n.Contains("lawn") || n.Contains("foliage") || n.Contains("shrub") || n.Contains("hedge")) return Surface.Grass;
            if (n.Contains("gravel") || n.Contains("rubble") || n.Contains("track")) return Surface.Gravel;
            if (n.Contains("soil") || n.Contains("dirt") || n.Contains("mud") || n.Contains("sand")) return Surface.Dirt;
            return Surface.Concrete;
        }

        static Surface OfTerrain(Terrain t, Vector3 p)
        {
            if (t == null || t.terrainData == null) return Surface.Grass;
            var td = t.terrainData;
            if (t != terrain)
            {
                terrain = t;
                alpha = td.alphamapLayers > 0 ? td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight) : null;
                var layers = td.terrainLayers;
                layerSurface = new Surface[layers != null ? layers.Length : 0];
                for (int i = 0; i < layerSurface.Length; i++)
                {
                    string n = layers[i] != null ? layers[i].name.ToLowerInvariant() : "";
                    layerSurface[i] = n.Contains("rock") || n.Contains("gravel") || n.Contains("stone") ? Surface.Gravel
                                    : n.Contains("soil") || n.Contains("dirt") || n.Contains("sand") || n.Contains("mud") ? Surface.Dirt
                                    : n.Contains("road") || n.Contains("paved") || n.Contains("concrete") ? Surface.Concrete
                                    : Surface.Grass;
                }
            }
            if (alpha == null || layerSurface.Length == 0) return Surface.Grass;
            Vector3 local = p - t.transform.position;
            int x = Mathf.Clamp(Mathf.RoundToInt(local.x / td.size.x * (td.alphamapWidth - 1)), 0, td.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(local.z / td.size.z * (td.alphamapHeight - 1)), 0, td.alphamapHeight - 1);
            int best = 0;
            float bw = -1f;
            int layersN = Mathf.Min(alpha.GetLength(2), layerSurface.Length);
            for (int i = 0; i < layersN; i++)
            {
                float w = alpha[z, x, i];
                if (w > bw) { bw = w; best = i; }
            }
            return layerSurface[best];
        }

        /// <summary>Clears what was learnt about the last scene.</summary>
        public static void Reset()
        {
            cache.Clear();
            subStarts.Clear();
            terrain = null;
            alpha = null;
            zones = null;
        }
    }
}
