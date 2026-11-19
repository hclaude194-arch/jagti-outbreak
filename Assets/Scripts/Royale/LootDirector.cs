using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.Royale
{
    /// <summary>One item lying on a floor or the ground.</summary>
    public class LootDrop
    {
        /// <summary>The same number on every machine online: spawned loot counts up from 1 in roll order.</summary>
        public int id;
        public Vector3 position;
        public float yaw;
        public ItemKind kind;
        public int value;
        public Tier tier;
        public int count;
        public bool taken;
        /// <summary>Inside a building: hidden from other storeys.</summary>
        public bool indoor;
        /// <summary>Set by a bot heading for it, so two bots don't chase the same item.</summary>
        public Combatant claimedBy;
        public float claimUntil;

        public string Label
        {
            get
            {
                string name = RoyaleItems.ItemName(kind, value, tier);
                if (kind == ItemKind.Gun) name += " (" + RoyaleItems.TierNames[(int)tier] + ")";
                if (count > 1) name += " x" + count;
                return name;
            }
        }
    }

    /// <summary>
    /// All loot in the match. At the start it rolls every loot point (made by tools/royale/build_loot_points.py
    /// and snapped to the floors by RoyaleBuilder) against the point's chance and its room's loot table.
    /// Items are plain data in a grid of cells, drawn with GPU instancing only near the camera and on its
    /// storey, so thousands of items cost a handful of draw calls. Also takes drops: swapped guns,
    /// spare armour, and everything a player carried when they fall.
    /// </summary>
    public class LootDirector : MonoBehaviour
    {
        public static LootDirector Instance { get; private set; }

        [Header("Points (filled by RoyaleBuilder)")]
        public Vector3[] points;
        public string[] pointTables;
        public float[] pointChances;
        public bool[] pointIndoor;

        [Header("Look")]
        public Material[] palette;          // LootVisuals palette order
        public Material[] tierRings;        // one per tier
        public float drawDistance = 55f;
        public float storeyHeight = 3.0f;   // items further above or below than this are on another storey

        public int Spawned { get; private set; }

        /// <summary>
        /// Online: loot made here (drops) and changes made here (pickups; count 0 = gone). NetMatch sends
        /// them on. Not raised for the match's own roll, supply crates (every machine makes the same
        /// ones), or changes that came over the network.
        /// </summary>
        public System.Action<LootDrop> Added, Changed;
        /// <summary>Ids for drops made on this machine start here (NetMatch sets player number x 2^20).</summary>
        public int idBase = 1 << 20;

        readonly Dictionary<int, LootDrop> byId = new Dictionary<int, LootDrop>();
        int nextOwn, nextShared;
        bool quiet;
        public const int SupplyIdBase = 15 << 20;

        const float CellSize = 16f;
        readonly Dictionary<long, List<LootDrop>> cells = new Dictionary<long, List<LootDrop>>();
        readonly List<LootDrop> visible = new List<LootDrop>();
        readonly Dictionary<int, List<Matrix4x4>> batches = new Dictionary<int, List<Matrix4x4>>();
        readonly List<Matrix4x4>[] ringBatches = new List<Matrix4x4>[4];
        readonly Matrix4x4[] chunk = new Matrix4x4[1023];
        float refreshTimer;
        float spin;

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < 4; i++) ringBatches[i] = new List<Matrix4x4>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static long CellKey(float x, float z)
        {
            long cx = Mathf.FloorToInt(x / CellSize);
            long cz = Mathf.FloorToInt(z / CellSize);
            return (cx << 32) ^ (cz & 0xffffffffL);
        }

        /// <summary>Rolls every point and lays out this match's loot.</summary>
        public void SpawnAll(int seed)
        {
            cells.Clear();
            byId.Clear();
            Spawned = 0;
            if (points == null) return;
            var rng = new System.Random(seed);
            BeginShared(1);
            for (int i = 0; i < points.Length; i++)
            {
                float chance = pointChances != null && i < pointChances.Length ? pointChances[i] : 0.5f;
                if (rng.NextDouble() > chance) continue;
                string table = pointTables != null && i < pointTables.Length ? pointTables[i] : "residential";
                ItemKind kind;
                int value, count;
                Tier tier;
                RoyaleItems.Roll(table, rng, out kind, out value, out tier, out count);
                Vector3 p = points[i];
                bool indoor = pointIndoor != null && i < pointIndoor.Length && pointIndoor[i];
                Add(p, (float)rng.NextDouble() * 360f, kind, value, tier, count).indoor = indoor;
                // A gun comes with a box of its ammo beside it.
                if (kind == ItemKind.Gun)
                {
                    var def = RoyaleItems.Guns[value];
                    int a = (int)def.ammo;
                    Vector3 off = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * new Vector3(0.5f, 0f, 0f);
                    Add(Snap(p + off, p.y), (float)rng.NextDouble() * 360f, ItemKind.Ammo, a, Tier.Common, RoyaleItems.AmmoBox[a]).indoor = indoor;
                }
            }
            EndShared();
        }

        /// <summary>
        /// Loot added until EndShared gets ids counted from firstId and is not announced: every machine
        /// makes the same items in the same order (the match roll, supply crates).
        /// </summary>
        public void BeginShared(int firstId)
        {
            nextShared = firstId;
            quiet = true;
        }

        public void EndShared()
        {
            nextShared = 0;
            quiet = false;
        }

        public LootDrop Add(Vector3 p, float yaw, ItemKind kind, int value, Tier tier, int count)
        {
            int id = nextShared > 0 ? nextShared++ : idBase + ++nextOwn;
            var d = Insert(id, p, yaw, kind, value, tier, count);
            if (!quiet && Added != null) Added(d);
            return d;
        }

        LootDrop Insert(int id, Vector3 p, float yaw, ItemKind kind, int value, Tier tier, int count)
        {
            var d = new LootDrop { id = id, position = p, yaw = yaw, kind = kind, value = value, tier = tier, count = count };
            byId[id] = d;
            long key = CellKey(p.x, p.z);
            List<LootDrop> list;
            if (!cells.TryGetValue(key, out list))
            {
                list = new List<LootDrop>();
                cells[key] = list;
            }
            list.Add(d);
            Spawned++;
            refreshTimer = 0f;
            return d;
        }

        /// <summary>Puts a dropped item on the floor near a spot (the floor is found with a short ray down).</summary>
        public LootDrop Drop(Vector3 near, ItemKind kind, int value, Tier tier, int count)
        {
            Vector3 off = Random.insideUnitCircle * 0.7f;
            Vector3 p = Snap(near + new Vector3(off.x, 0f, off.y), near.y);
            bool was = quiet;
            quiet = true;
            var d = Add(p, Random.Range(0f, 360f), kind, value, tier, count);
            quiet = was;
            d.indoor = UnderRoof(p);
            if (!quiet && Added != null) Added(d);
            return d;
        }

        static readonly List<int> stripped = new List<int>();

        /// <summary>Drops a gun; its attachments come off and land beside it.</summary>
        public void DropGun(Vector3 at, GunItem g)
        {
            stripped.Clear();
            g.Strip(stripped);
            Drop(at, ItemKind.Gun, g.def.id, g.tier, 1);
            for (int i = 0; i < stripped.Count; i++)
            {
                float a = (i + 1) * 1.9f;
                Drop(at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.4f, ItemKind.Attachment, stripped[i],
                     RoyaleItems.DisplayTier(ItemKind.Attachment, stripped[i], Tier.Common), 1);
            }
        }

        /// <summary>Drops what a pickup replaced: older armour or pack (level), or a swapped-out attachment (type + 1).</summary>
        public void DropReplaced(Vector3 at, ItemKind kind, int old)
        {
            if (kind == ItemKind.Attachment)
                Drop(at, kind, old - 1, RoyaleItems.DisplayTier(kind, old - 1, Tier.Common), 1);
            else
                Drop(at, kind, old, (Tier)old, 1);
        }

        /// <summary>Everything a fallen player carried, spread in a small circle.</summary>
        public void DropLoadout(Loadout l, Vector3 at)
        {
            var items = new List<LootDrop>();
            var parts = new List<int>();
            foreach (var g in l.guns)
                if (g != null)
                {
                    items.Add(new LootDrop { kind = ItemKind.Gun, value = g.def.id, tier = g.tier, count = 1 });
                    g.Strip(parts);
                }
            foreach (int a in parts)
                items.Add(new LootDrop { kind = ItemKind.Attachment, value = a, tier = RoyaleItems.DisplayTier(ItemKind.Attachment, a, Tier.Common), count = 1 });
            if (l.smokes > 0) items.Add(new LootDrop { kind = ItemKind.Smoke, count = l.smokes });
            for (int t = 0; t < 4; t++)
                if (l.ammo[t] > 0) items.Add(new LootDrop { kind = ItemKind.Ammo, value = t, count = l.ammo[t] });
            if (l.vest > 0 && l.vestDurability > 10f) items.Add(new LootDrop { kind = ItemKind.Vest, value = l.vest, tier = (Tier)l.vest, count = 1 });
            if (l.helmet > 0 && l.helmetDurability > 10f) items.Add(new LootDrop { kind = ItemKind.Helmet, value = l.helmet, tier = (Tier)l.helmet, count = 1 });
            if (l.backpack > 0) items.Add(new LootDrop { kind = ItemKind.Backpack, value = l.backpack, tier = (Tier)l.backpack, count = 1 });
            if (l.bandages > 0) items.Add(new LootDrop { kind = ItemKind.Bandage, count = l.bandages });
            if (l.medkits > 0) items.Add(new LootDrop { kind = ItemKind.Medkit, count = l.medkits });
            if (l.chai > 0) items.Add(new LootDrop { kind = ItemKind.ChaiFlask, count = l.chai });
            if (l.grenades > 0) items.Add(new LootDrop { kind = ItemKind.Grenade, count = l.grenades });
            for (int i = 0; i < items.Count; i++)
            {
                float a = i / (float)Mathf.Max(1, items.Count) * Mathf.PI * 2f;
                float r = items.Count > 1 ? 0.55f + 0.1f * (i % 2) : 0f;
                Vector3 p = Snap(at + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), at.y);
                var it = items[i];
                bool was = quiet;
                quiet = true;
                var d = Add(p, a * Mathf.Rad2Deg + 90f, it.kind, it.value, it.tier, it.count);
                quiet = was;
                d.indoor = UnderRoof(p);
                if (!quiet && Added != null) Added(d);
            }
        }

        /// <summary>The top of whatever is under a spot (floor, stair, ground), searched near height y.</summary>
        public static Vector3 Snap(Vector3 p, float y)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, y + 1.2f, p.z), Vector3.down, out hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.01f;
            return new Vector3(p.x, y, p.z);
        }

        static bool UnderRoof(Vector3 p)
        {
            return Physics.Raycast(p + Vector3.up * 0.5f, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore);
        }

        public void Remove(LootDrop d)
        {
            if (d == null || d.taken) return;
            d.taken = true;
            d.count = 0;
            byId.Remove(d.id);
            List<LootDrop> list;
            if (cells.TryGetValue(CellKey(d.position.x, d.position.z), out list)) list.Remove(d);
            refreshTimer = 0f;
            if (!quiet && Changed != null) Changed(d);
        }

        /// <summary>Part of a stack was taken; the rest stays on the floor.</summary>
        public void SetCount(LootDrop d, int count)
        {
            if (d == null || d.taken) return;
            if (count <= 0) { Remove(d); return; }
            d.count = count;
            if (!quiet && Changed != null) Changed(d);
        }

        // ------------------------------------------------------------------ online

        public LootDrop Find(int id)
        {
            LootDrop d;
            return byId.TryGetValue(id, out d) ? d : null;
        }

        /// <summary>An item someone else's machine dropped.</summary>
        public void AddRemote(int id, Vector3 p, float yaw, ItemKind kind, int value, Tier tier, int count, bool indoor)
        {
            if (byId.ContainsKey(id)) return;
            Insert(id, p, yaw, kind, value, tier, count).indoor = indoor;
        }

        /// <summary>Someone else took some or all of an item.</summary>
        public void SetRemote(int id, int count)
        {
            var d = Find(id);
            if (d == null) return;
            quiet = true;
            SetCount(d, count);
            quiet = false;
        }

        /// <summary>
        /// Items within radius of p. Indoor items more than a storey above or below are left out (and outdoor
        /// ones too unless outdoorAnyHeight, used for drawing across slopes). The order is not sorted.
        /// </summary>
        public void Near(Vector3 p, float radius, List<LootDrop> result, bool outdoorAnyHeight = false)
        {
            result.Clear();
            int r = Mathf.CeilToInt(radius / CellSize);
            int cx = Mathf.FloorToInt(p.x / CellSize), cz = Mathf.FloorToInt(p.z / CellSize);
            float r2 = radius * radius;
            for (int x = cx - r; x <= cx + r; x++)
            {
                for (int z = cz - r; z <= cz + r; z++)
                {
                    List<LootDrop> list;
                    long key = ((long)x << 32) ^ ((long)z & 0xffffffffL);
                    if (!cells.TryGetValue(key, out list)) continue;
                    foreach (var d in list)
                    {
                        if (d.taken) continue;
                        Vector3 dp = d.position - p;
                        if (Mathf.Abs(dp.y) > storeyHeight && (d.indoor || !outdoorAnyHeight)) continue;
                        dp.y = 0f;
                        if (dp.sqrMagnitude <= r2) result.Add(d);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ drawing

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            spin += Time.deltaTime * 25f;
            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 0.2f;
                Near(cam.transform.position - Vector3.up * 1.4f, drawDistance, visible, true);
            }
            Draw();
        }

        void Draw()
        {
            foreach (var kv in batches) kv.Value.Clear();
            for (int i = 0; i < 4; i++) ringBatches[i].Clear();
            foreach (var d in visible)
            {
                if (d.taken) continue;
                int key = LootVisuals.Key(d.kind, d.value);
                List<Matrix4x4> list;
                if (!batches.TryGetValue(key, out list))
                {
                    list = new List<Matrix4x4>();
                    batches[key] = list;
                }
                list.Add(Matrix4x4.TRS(d.position, Quaternion.Euler(0f, d.yaw, 0f), Vector3.one));
                int tier = (int)RoyaleItems.DisplayTier(d.kind, d.value, d.tier);
                ringBatches[tier].Add(Matrix4x4.TRS(d.position + Vector3.up * 0.005f, Quaternion.Euler(0f, spin + d.yaw, 0f), Vector3.one * 1.3f));
            }
            foreach (var kv in batches)
            {
                if (kv.Value.Count == 0) continue;
                ItemKind kind = (ItemKind)(kv.Key / 16);
                int value = kv.Key % 16;
                var model = LootVisuals.Get(kind, value);
                foreach (int sub in model.submeshes)
                {
                    if (palette == null || sub >= palette.Length || palette[sub] == null) continue;
                    DrawList(model.mesh, sub, palette[sub], kv.Value);
                }
            }
            if (tierRings != null)
            {
                var ring = LootVisuals.Ring();
                for (int i = 0; i < 4 && i < tierRings.Length; i++)
                    if (ringBatches[i].Count > 0 && tierRings[i] != null) DrawList(ring, 0, tierRings[i], ringBatches[i]);
            }
        }

        void DrawList(Mesh mesh, int sub, Material mat, List<Matrix4x4> list)
        {
            int i = 0;
            while (i < list.Count)
            {
                int n = Mathf.Min(1023, list.Count - i);
                list.CopyTo(i, chunk, 0, n);
                Graphics.DrawMeshInstanced(mesh, sub, mat, chunk, n, null, ShadowCastingMode.Off, false);
                i += n;
            }
        }
    }
}
