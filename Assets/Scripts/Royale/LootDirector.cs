using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Jagti.Gameplay;

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
        /// <summary>The stash it lies sealed in, if any: unseen and untouchable until the stash opens.</summary>
        public LootStash stash;

        public bool Sealed { get { return stash != null && !stash.open; } }

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
    /// Each match also fills some stash spots with trunks of better loot (LootStash) and picks two
    /// named places for "rich pickings", where the floor loot is fuller and better.
    /// </summary>
    public class LootDirector : MonoBehaviour
    {
        public static LootDirector Instance { get; private set; }

        [Header("Points (filled by RoyaleBuilder)")]
        public Vector3[] points;
        public string[] pointTables;
        public float[] pointChances;
        public bool[] pointIndoor;

        [Header("Stashes (filled by RoyaleBuilder)")]
        public Vector3[] stashPoints;
        public float[] stashYaw;
        public string[] stashTables;
        public float[] stashChances;
        public bool[] stashIndoor;
        public const int StashItems = 3;

        [Header("Rich pickings")]
        public Jagti.World.PlaceData[] places;
        public int[] placeRating;            // 0 light, 1 good, 2 rich (RoyaleBuilder.RatePlaces)
        public float hotRadius = 75f;
        public const int HotCount = 2;

        [Header("Look")]
        public Material[] palette;          // LootVisuals palette order
        public Material[] tierRings;        // one per tier
        public float drawDistance = 55f;
        public float storeyHeight = 3.0f;   // items further above or below than this are on another storey

        public int Spawned { get; private set; }

        /// <summary>This match's stashes (filled spots only), in the same order on every machine.</summary>
        public readonly List<LootStash> Stashes = new List<LootStash>();
        /// <summary>This match's rich-pickings places (indices into places).</summary>
        public readonly List<int> HotPlaces = new List<int>();
        /// <summary>A stash opened on this machine (not raised for OpenRemote). NetMatch sends it on.</summary>
        public System.Action<LootStash> StashOpened;

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
        readonly List<LootStash> visibleStashes = new List<LootStash>();
        readonly List<Matrix4x4> stashBodies = new List<Matrix4x4>(), stashLids = new List<Matrix4x4>(), stashGlows = new List<Matrix4x4>();
        readonly List<GameObject> stashBlocks = new List<GameObject>();
        int[] bodySubs, lidSubs;
        AudioSource hum;

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < 4; i++) ringBatches[i] = new List<Matrix4x4>();
            var humGo = new GameObject("Stash Hum");
            humGo.transform.SetParent(transform, false);
            hum = humGo.AddComponent<AudioSource>();
            hum.clip = SynthAudio.Get("stash_hum");
            hum.loop = true;
            hum.spatialBlend = 1f;
            hum.rolloffMode = AudioRolloffMode.Linear;
            hum.minDistance = 1.5f;
            hum.maxDistance = 13f;
            hum.volume = 0.35f;
            hum.playOnAwake = false;
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

        /// <summary>
        /// Rolls every point and lays out this match's loot: picks the rich-pickings places, rolls the floor
        /// points (fuller and better inside those places), then fills stashes. Only seeded randomness and
        /// the scene's colliders are used, so every machine online lays out the same loot.
        /// </summary>
        public void SpawnAll(int seed)
        {
            cells.Clear();
            byId.Clear();
            Spawned = 0;
            Stashes.Clear();
            foreach (var b in stashBlocks) if (b != null) Destroy(b);
            stashBlocks.Clear();
            PickHotPlaces(seed);
            if (points == null) return;
            var rng = new System.Random(seed);
            BeginShared(1);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 p = points[i];
                bool hot = IsHot(p);
                float chance = pointChances != null && i < pointChances.Length ? pointChances[i] : 0.5f;
                if (hot) chance = Mathf.Min(1f, chance + 0.25f);
                if (rng.NextDouble() > chance) continue;
                string table = pointTables != null && i < pointTables.Length ? pointTables[i] : "residential";
                bool indoor = pointIndoor != null && i < pointIndoor.Length && pointIndoor[i];
                AddRolled(p, table, hot, indoor, null, rng);
            }
            if (stashPoints != null)
                for (int i = 0; i < stashPoints.Length; i++)
                {
                    float chance = stashChances != null && i < stashChances.Length ? stashChances[i] : 0.6f;
                    if (rng.NextDouble() > chance) continue;
                    var s = new LootStash
                    {
                        index = Stashes.Count,
                        position = stashPoints[i],
                        yaw = stashYaw != null && i < stashYaw.Length ? stashYaw[i] : 0f,
                        table = stashTables != null && i < stashTables.Length ? stashTables[i] : "residential",
                        indoor = stashIndoor != null && i < stashIndoor.Length && stashIndoor[i],
                    };
                    Stashes.Add(s);
                    // The items wait in a fan in front of the trunk.
                    for (int k = 0; k < StashItems; k++)
                    {
                        Vector3 dir = Quaternion.Euler(0f, s.yaw + (k - 1) * 38f, 0f) * Vector3.forward;
                        AddRolled(Snap(s.position + dir * 0.95f, s.position.y), s.table, true, s.indoor, s, rng);
                    }
                    Block(s);
                }
            EndShared();
        }

        /// <summary>One roll of a table at p; a gun comes with a box of its ammo beside it.</summary>
        void AddRolled(Vector3 p, string table, bool better, bool indoor, LootStash stash, System.Random rng)
        {
            ItemKind kind;
            int value, count;
            Tier tier;
            RoyaleItems.Roll(table, rng, better, out kind, out value, out tier, out count);
            var d = Add(p, (float)rng.NextDouble() * 360f, kind, value, tier, count);
            d.indoor = indoor;
            d.stash = stash;
            if (stash != null) stash.contents.Add(d);
            if (kind == ItemKind.Gun)
            {
                var def = RoyaleItems.Guns[value];
                int a = (int)def.ammo;
                Vector3 off = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * new Vector3(0.5f, 0f, 0f);
                var box = Add(Snap(p + off, p.y), (float)rng.NextDouble() * 360f, ItemKind.Ammo, a, Tier.Common, RoyaleItems.AmmoBox[a]);
                box.indoor = indoor;
                box.stash = stash;
                if (stash != null) stash.contents.Add(box);
            }
        }

        /// <summary>A collider for a trunk, so players stand on it or walk round it.</summary>
        void Block(LootStash s)
        {
            var go = new GameObject("Stash " + s.index);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(s.position, Quaternion.Euler(0f, s.yaw, 0f));
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, StashModel.Height / 2f, 0f);
            box.size = new Vector3(StashModel.Width, StashModel.Height, StashModel.Depth);
            stashBlocks.Add(go);
        }

        /// <summary>Two places with good or rich loot, from their own seeded stream.</summary>
        void PickHotPlaces(int seed)
        {
            HotPlaces.Clear();
            if (places == null || places.Length == 0) return;
            var pool = new List<int>();
            for (int i = 0; i < places.Length; i++)
                if (placeRating == null || i >= placeRating.Length || placeRating[i] >= 1) pool.Add(i);
            if (pool.Count < HotCount) { pool.Clear(); for (int i = 0; i < places.Length; i++) pool.Add(i); }
            var r = new System.Random(seed * 7919 + 13);
            while (HotPlaces.Count < HotCount && pool.Count > 0)
            {
                int k = r.Next(pool.Count);
                int pick = pool[k];
                pool.RemoveAt(k);
                // Not two neighbours: spread them out.
                bool close = false;
                foreach (int h in HotPlaces)
                {
                    float dx = places[h].x - places[pick].x, dz = places[h].z - places[pick].z;
                    if (dx * dx + dz * dz < (hotRadius * 2.5f) * (hotRadius * 2.5f)) close = true;
                }
                if (!close) HotPlaces.Add(pick);
            }
        }

        public bool IsHot(Vector3 p)
        {
            foreach (int h in HotPlaces)
            {
                float dx = places[h].x - p.x, dz = places[h].z - p.z;
                if (dx * dx + dz * dz < hotRadius * hotRadius) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ stashes

        /// <summary>The nearest closed stash within radius of p on p's storey (null if none).</summary>
        public LootStash NearestClosedStash(Vector3 p, float radius)
        {
            LootStash best = null;
            float bestD = radius * radius;
            foreach (var s in Stashes)
            {
                if (s.open || Mathf.Abs(s.position.y - p.y) > storeyHeight) continue;
                float dx = s.position.x - p.x, dz = s.position.z - p.z;
                float d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Opens a stash here: the lid swings up and its items are free to take.</summary>
        public void Open(LootStash s)
        {
            if (s == null || s.open) return;
            s.open = true;
            s.openedAt = Time.time;
            refreshTimer = 0f;
            var clip = SynthAudio.Get("stash_open");
            if (clip != null) AudioSource.PlayClipAtPoint(clip, s.position + Vector3.up * 0.4f, 0.9f);
            if (!quiet && StashOpened != null) StashOpened(s);
        }

        /// <summary>Someone else opened a stash.</summary>
        public void OpenRemote(int index)
        {
            if (index < 0 || index >= Stashes.Count) return;
            quiet = true;
            Open(Stashes[index]);
            quiet = false;
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
                        if (d.taken || d.Sealed) continue;
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
                FindVisibleStashes(cam.transform.position - Vector3.up * 1.4f);
            }
            Draw();
            DrawStashes();
        }

        void Draw()
        {
            foreach (var kv in batches) kv.Value.Clear();
            for (int i = 0; i < 4; i++) ringBatches[i].Clear();
            foreach (var d in visible)
            {
                if (d.taken || d.Sealed) continue;
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

        void FindVisibleStashes(Vector3 p)
        {
            visibleStashes.Clear();
            float r2 = (drawDistance + 10f) * (drawDistance + 10f);
            LootStash nearest = null;
            float nearestD = 14f * 14f;
            foreach (var s in Stashes)
            {
                Vector3 d = s.position - p;
                if (s.indoor && Mathf.Abs(d.y) > storeyHeight) continue;
                d.y = 0f;
                float d2 = d.sqrMagnitude;
                if (d2 > r2) continue;
                visibleStashes.Add(s);
                if (!s.open && d2 < nearestD)
                {
                    nearestD = d2;
                    nearest = s;
                }
            }
            // One humming source, parked on the nearest closed trunk.
            if (hum == null || hum.clip == null) return;
            if (nearest != null)
            {
                hum.transform.position = nearest.position + Vector3.up * 0.3f;
                if (!hum.isPlaying) hum.Play();
            }
            else if (hum.isPlaying) hum.Stop();
        }

        void DrawStashes()
        {
            if (palette == null) return;
            stashBodies.Clear();
            stashLids.Clear();
            stashGlows.Clear();
            float now = Time.time;
            foreach (var s in visibleStashes)
            {
                var m = Matrix4x4.TRS(s.position, Quaternion.Euler(0f, s.yaw, 0f), Vector3.one);
                stashBodies.Add(m);
                float swing = s.open ? Mathf.SmoothStep(0f, 1f, (now - s.openedAt) / 0.45f) * 108f : 0f;
                stashLids.Add(m * Matrix4x4.TRS(StashModel.LidHinge, Quaternion.Euler(-swing, 0f, 0f), Vector3.one));
                if (!s.open)
                {
                    float pulse = 3.4f + Mathf.Sin(now * 2.6f + s.index) * 0.15f;   // the ring is 0.55 m across, so about 1.9 m
                    stashGlows.Add(Matrix4x4.TRS(s.position + Vector3.up * 0.006f, Quaternion.Euler(0f, spin * 0.6f + s.yaw, 0f), Vector3.one * pulse));
                }
            }
            if (stashBodies.Count == 0) return;
            var body = StashModel.Body();
            var lid = StashModel.Lid();
            if (bodySubs == null) bodySubs = UsedSubmeshes(body);
            if (lidSubs == null) lidSubs = UsedSubmeshes(lid);
            foreach (int sub in bodySubs)
                if (sub < palette.Length && palette[sub] != null) DrawList(body, sub, palette[sub], stashBodies);
            foreach (int sub in lidSubs)
                if (sub < palette.Length && palette[sub] != null) DrawList(lid, sub, palette[sub], stashLids);
            if (stashGlows.Count > 0 && tierRings != null && tierRings.Length > 3 && tierRings[3] != null)
                DrawList(LootVisuals.Ring(), 0, tierRings[3], stashGlows);
        }

        static int[] UsedSubmeshes(Mesh mesh)
        {
            var used = new List<int>();
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetSubMesh(i).indexCount > 0) used.Add(i);
            return used.ToArray();
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
