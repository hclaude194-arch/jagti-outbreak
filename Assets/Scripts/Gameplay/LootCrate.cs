using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>
    /// A supply crate left outside a building. Hold E to search it (which makes a little noise);
    /// the loot depends on the building: medicine near the medical shops, ammo at the gate guard posts,
    /// batteries and bandages in the hostels, and so on.
    /// </summary>
    public class LootCrate : MonoBehaviour
    {
        public string table = "general";
        public string label = "Crate";
        public float searchTime = 1.2f;
        public bool searched;
        public Renderer lidRenderer;
        public Material searchedMaterial;

        public string Prompt { get { return searched ? label + " (empty)" : "Hold E to search " + label.ToLowerInvariant(); } }

        public void Search(GameObject who)
        {
            if (searched) return;
            searched = true;
            var inv = who.GetComponent<Inventory>();
            if (inv == null) return;

            var found = LootTables.Roll(table);
            var parts = new List<string>();
            foreach (var item in found)
            {
                if (item.Key == ItemType.Shotgun)
                {
                    if (inv.hasShotgun)
                    {
                        int s = inv.Add(ItemType.Shells, 6);
                        if (s > 0) parts.Add(Inventory.Name(ItemType.Shells, s));
                        continue;
                    }
                    inv.Add(ItemType.Shotgun, 1);
                    parts.Add(Inventory.Name(ItemType.Shotgun, 1));
                    var weapons = who.GetComponent<WeaponController>();
                    if (weapons != null) weapons.OnShotgunFound();
                    continue;
                }
                int taken = inv.Add(item.Key, item.Value);
                if (taken > 0) parts.Add(Inventory.Name(item.Key, taken));
                else parts.Add("(no room for " + Inventory.Name(item.Key, item.Value) + ")");
            }
            inv.Say(parts.Count > 0 ? "Found " + string.Join(", ", parts.ToArray()) : "Nothing useful");
            if (parts.Count > 0) SynthAudio.Play2D("pickup", 0.6f);
            if (lidRenderer != null && searchedMaterial != null) lidRenderer.sharedMaterial = searchedMaterial;
        }
    }

    /// <summary>Weighted loot rolls per building type. Each roll gives 1–3 entries.</summary>
    public static class LootTables
    {
        struct Entry
        {
            public ItemType item;
            public int min, max;
            public float weight;
            public Entry(ItemType item, int min, int max, float weight)
            {
                this.item = item; this.min = min; this.max = max; this.weight = weight;
            }
        }

        static readonly Dictionary<string, Entry[]> tables = new Dictionary<string, Entry[]>
        {
            { "general", new[] { E(ItemType.PistolAmmo, 4, 10, 3f), E(ItemType.Bandage, 1, 1, 3f), E(ItemType.Battery, 1, 1, 2f), E(ItemType.Medkit, 1, 1, 0.5f) } },
            { "medical", new[] { E(ItemType.Medkit, 1, 1, 3f), E(ItemType.Bandage, 1, 3, 4f), E(ItemType.Battery, 1, 1, 1f) } },
            { "hostel",  new[] { E(ItemType.Battery, 1, 2, 3f), E(ItemType.Bandage, 1, 2, 3f), E(ItemType.PistolAmmo, 4, 8, 2f), E(ItemType.Shells, 2, 4, 0.5f) } },
            { "mess",    new[] { E(ItemType.Bandage, 1, 2, 3f), E(ItemType.Battery, 1, 2, 3f), E(ItemType.PistolAmmo, 3, 6, 1f) } },
            { "security",new[] { E(ItemType.PistolAmmo, 8, 16, 4f), E(ItemType.Shells, 3, 6, 3f), E(ItemType.Battery, 1, 2, 2f) } },
            { "armory",  new[] { E(ItemType.Shotgun, 1, 1, 100f) } },
            { "academic",new[] { E(ItemType.Battery, 1, 2, 3f), E(ItemType.Medkit, 1, 1, 1f), E(ItemType.Bandage, 1, 2, 2f), E(ItemType.PistolAmmo, 4, 8, 1.5f) } },
            { "sports",  new[] { E(ItemType.Bandage, 1, 3, 4f), E(ItemType.Battery, 1, 1, 1f), E(ItemType.Shells, 2, 4, 1f) } },
        };

        static Entry E(ItemType item, int min, int max, float weight)
        {
            return new Entry(item, min, max, weight);
        }

        public static List<KeyValuePair<ItemType, int>> Roll(string table)
        {
            Entry[] entries;
            if (!tables.TryGetValue(table, out entries)) entries = tables["general"];
            var result = new List<KeyValuePair<ItemType, int>>();
            int rolls = table == "armory" ? 1 : Random.Range(1, 4);
            float total = 0f;
            foreach (var e in entries) total += e.weight;
            for (int r = 0; r < rolls; r++)
            {
                float pick = Random.value * total;
                foreach (var e in entries)
                {
                    pick -= e.weight;
                    if (pick > 0f) continue;
                    int n = Random.Range(e.min, e.max + 1);
                    bool merged = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (result[i].Key != e.item) continue;
                        result[i] = new KeyValuePair<ItemType, int>(e.item, result[i].Value + n);
                        merged = true;
                    }
                    if (!merged) result.Add(new KeyValuePair<ItemType, int>(e.item, n));
                    break;
                }
            }
            if (table == "armory") result.Add(new KeyValuePair<ItemType, int>(ItemType.Shells, 6));
            return result;
        }

        /// <summary>Picks a loot table from a building's name and zone (used by the scene builder).</summary>
        public static string ForBuilding(string name, string zone)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("medical")) return "medical";
            if (n.Contains("mess") || n.Contains("kitchen")) return "mess";
            if (n.Contains("gate") || n.Contains("clock tower") || n.Contains("substation")) return "security";
            if (n.Contains("hostel")) return "hostel";
            if (n.Contains("sports") || n.Contains("pavilion")) return "sports";
            if (zone == "academic") return "academic";
            return "general";
        }
    }
}
