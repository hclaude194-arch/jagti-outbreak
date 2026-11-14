using UnityEngine;

namespace Jagti.Royale
{
    public enum Tier { Common = 0, Uncommon = 1, Rare = 2, Epic = 3 }

    public enum AmmoType { Light = 0, Shells = 1, Rifle = 2, Heavy = 3 }

    public enum GunClass { Sidearm, Smg, Shotgun, Rifle, Marksman }

    /// <summary>What a loot item is. Guns, ammo boxes, armour, packs, heals and throwables.</summary>
    public enum ItemKind
    {
        Gun = 0,
        Ammo = 1,
        Vest = 2,
        Helmet = 3,
        Backpack = 4,
        Bandage = 5,
        Medkit = 6,
        ChaiFlask = 7,
        Grenade = 8,
    }

    /// <summary>One gun model. Names are our own, after the rivers and passes of Jammu.</summary>
    public class GunDef
    {
        public int id;
        public string name;
        public GunClass gunClass;
        public AmmoType ammo;
        public float damage;          // per bullet or pellet, before tier and armour
        public float headMultiplier;
        public float interval;        // seconds between shots
        public bool automatic;
        public int magazine;
        public float reloadTime;
        public float hipSpread;       // degrees
        public float aimSpread;
        public float range;           // damage falls to falloffMin at this distance
        public float falloffMin;
        public float recoil;          // degrees of view kick per shot
        public int pellets = 1;
        public float zoomFov;         // field of view while aiming
        public float noise;           // metres the shot is heard
        public string sound;
    }

    /// <summary>
    /// The item tables for the battle royale: guns, how much ammo comes in a box, armour levels,
    /// pack sizes, heals, and which items each kind of place tends to hold.
    /// </summary>
    public static class RoyaleItems
    {
        public static readonly GunDef[] Guns =
        {
            new GunDef { id = 0, name = "Ravi P9", gunClass = GunClass.Sidearm, ammo = AmmoType.Light, damage = 22f, headMultiplier = 2f,
                interval = 0.2f, automatic = false, magazine = 15, reloadTime = 1.4f, hipSpread = 1.6f, aimSpread = 0.5f,
                range = 60f, falloffMin = 0.55f, recoil = 1.6f, zoomFov = 58f, noise = 70f, sound = "pistol" },
            new GunDef { id = 1, name = "Chenab SMG", gunClass = GunClass.Smg, ammo = AmmoType.Light, damage = 17f, headMultiplier = 1.8f,
                interval = 0.075f, automatic = true, magazine = 30, reloadTime = 2.0f, hipSpread = 2.4f, aimSpread = 1.0f,
                range = 55f, falloffMin = 0.5f, recoil = 0.9f, zoomFov = 56f, noise = 80f, sound = "smg" },
            new GunDef { id = 2, name = "Banihal 12", gunClass = GunClass.Shotgun, ammo = AmmoType.Shells, damage = 11f, headMultiplier = 1.5f,
                interval = 0.85f, automatic = false, magazine = 5, reloadTime = 2.6f, hipSpread = 5.5f, aimSpread = 4.2f,
                range = 30f, falloffMin = 0.25f, recoil = 5.5f, pellets = 9, zoomFov = 58f, noise = 100f, sound = "shotgun" },
            new GunDef { id = 3, name = "Tawi AR", gunClass = GunClass.Rifle, ammo = AmmoType.Rifle, damage = 24f, headMultiplier = 2f,
                interval = 0.11f, automatic = true, magazine = 30, reloadTime = 2.3f, hipSpread = 2.0f, aimSpread = 0.45f,
                range = 160f, falloffMin = 0.7f, recoil = 1.25f, zoomFov = 50f, noise = 120f, sound = "rifle" },
            new GunDef { id = 4, name = "Jhelum DMR", gunClass = GunClass.Marksman, ammo = AmmoType.Heavy, damage = 52f, headMultiplier = 2.3f,
                interval = 0.42f, automatic = false, magazine = 10, reloadTime = 2.8f, hipSpread = 3.0f, aimSpread = 0.08f,
                range = 450f, falloffMin = 0.85f, recoil = 3.4f, zoomFov = 22f, noise = 160f, sound = "dmr" },
        };

        /// <summary>Damage and handling get a little better with each tier.</summary>
        public static float TierDamage(Tier t) { return 1f + 0.07f * (int)t; }
        public static float TierSpread(Tier t) { return 1f - 0.1f * (int)t; }
        public static float TierRecoil(Tier t) { return 1f - 0.08f * (int)t; }

        public static readonly Color[] TierColours =
        {
            new Color(0.78f, 0.78f, 0.76f),   // common: light grey
            new Color(0.36f, 0.82f, 0.38f),   // uncommon: green
            new Color(0.25f, 0.6f, 1f),       // rare: blue
            new Color(0.95f, 0.55f, 0.15f),   // epic: saffron
        };

        public static readonly string[] TierNames = { "Common", "Uncommon", "Rare", "Epic" };

        public static readonly string[] AmmoNames = { "9 mm rounds", "12 gauge shells", "5.56 rounds", "7.62 rounds" };
        public static readonly int[] AmmoBox = { 30, 10, 30, 15 };              // how much a box holds
        public static readonly int[,] AmmoLimit = { { 90, 24, 90, 30 },       // carry limits per pack level 0..3
                                                    { 150, 36, 150, 50 },
                                                    { 210, 48, 210, 70 },
                                                    { 270, 60, 270, 90 } };

        // Armour: share of damage the piece absorbs, and how much it can absorb before it breaks.
        public static readonly float[] ArmourShare = { 0f, 0.3f, 0.42f, 0.55f };
        public static readonly float[] ArmourDurability = { 0f, 80f, 140f, 200f };

        public static readonly int[,] HealLimit = { { 5, 2, 2, 2 },            // bandage, medkit, chai, grenade per pack level
                                                    { 8, 3, 3, 3 },
                                                    { 12, 4, 4, 4 },
                                                    { 16, 5, 5, 5 } };

        public const float BandageHeal = 15f;
        public const float BandageCap = 75f;      // bandages only heal up to this
        public const float BandageTime = 3f;
        public const float MedkitTime = 7f;
        public const float ChaiTime = 2f;
        public const float ChaiBoost = 30f;       // health regained slowly over ChaiBoostTime
        public const float ChaiBoostTime = 20f;

        public static string ItemName(ItemKind kind, int value, Tier tier)
        {
            switch (kind)
            {
                case ItemKind.Gun: return Guns[Mathf.Clamp(value, 0, Guns.Length - 1)].name;
                case ItemKind.Ammo: return AmmoNames[Mathf.Clamp(value, 0, 3)];
                case ItemKind.Vest: return "Vest (level " + value + ")";
                case ItemKind.Helmet: return "Helmet (level " + value + ")";
                case ItemKind.Backpack: return "Backpack (level " + value + ")";
                case ItemKind.Bandage: return "Bandage";
                case ItemKind.Medkit: return "Medkit";
                case ItemKind.ChaiFlask: return "Chai flask";
                case ItemKind.Grenade: return "Grenade";
            }
            return kind.ToString();
        }

        /// <summary>The tier an item is shown with (guns carry their own; armour and packs follow their level).</summary>
        public static Tier DisplayTier(ItemKind kind, int value, Tier tier)
        {
            switch (kind)
            {
                case ItemKind.Gun: return tier;
                case ItemKind.Vest:
                case ItemKind.Helmet:
                case ItemKind.Backpack: return (Tier)Mathf.Clamp(value, 0, 3);
                case ItemKind.Medkit: return Tier.Rare;
                case ItemKind.ChaiFlask: return Tier.Uncommon;
                case ItemKind.Grenade: return Tier.Uncommon;
            }
            return Tier.Common;
        }

        // ------------------------------------------------------------------ loot tables

        /// <summary>A roll from a place's loot table: what kind of item, and how good.</summary>
        public static void Roll(string table, System.Random rng, out ItemKind kind, out int value, out Tier tier, out int count)
        {
            // Weights: gun, ammo, vest, helmet, pack, bandage, medkit, chai, grenade.
            float[] w;
            switch (table)
            {
                case "medical": w = new[] { 6f, 6f, 3f, 3f, 3f, 30f, 22f, 12f, 1f }; break;
                case "food": w = new[] { 10f, 12f, 4f, 4f, 4f, 14f, 4f, 30f, 3f }; break;
                case "office": w = new[] { 32f, 22f, 10f, 9f, 5f, 8f, 3f, 4f, 7f }; break;
                case "vault": w = new[] { 40f, 15f, 16f, 14f, 8f, 0f, 5f, 0f, 6f }; break;
                case "sports": w = new[] { 14f, 14f, 16f, 16f, 14f, 10f, 3f, 8f, 3f }; break;
                case "storage": w = new[] { 18f, 24f, 12f, 10f, 12f, 8f, 3f, 5f, 8f }; break;
                case "academic": w = new[] { 22f, 24f, 8f, 8f, 6f, 13f, 3f, 10f, 6f }; break;
                case "outdoor": w = new[] { 24f, 26f, 8f, 8f, 6f, 12f, 3f, 7f, 6f }; break;
                default: w = new[] { 22f, 25f, 9f, 9f, 7f, 13f, 3f, 8f, 4f }; break; // residential
            }
            float total = 0f;
            foreach (float x in w) total += x;
            float r = (float)rng.NextDouble() * total;
            int k = 0;
            for (; k < w.Length - 1; k++)
            {
                if (r < w[k]) break;
                r -= w[k];
            }
            kind = (ItemKind)k;
            tier = RollTier(table, rng);
            value = 0;
            count = 1;
            switch (kind)
            {
                case ItemKind.Gun:
                    // Sidearms and SMGs are common finds; rifles less so; the marksman rifle is rare.
                    float g = (float)rng.NextDouble();
                    value = g < 0.26f ? 0 : g < 0.5f ? 1 : g < 0.7f ? 2 : g < 0.9f ? 3 : 4;
                    break;
                case ItemKind.Ammo:
                    value = rng.Next(4);
                    if (value == 3 && rng.NextDouble() < 0.5) value = 2;   // heavy rounds are scarcer
                    count = AmmoBox[value];
                    break;
                case ItemKind.Vest:
                case ItemKind.Helmet:
                case ItemKind.Backpack:
                    value = tier == Tier.Epic ? 3 : tier == Tier.Rare ? 2 : 1;
                    tier = (Tier)value;
                    break;
                case ItemKind.Bandage:
                    count = rng.NextDouble() < 0.5 ? 2 : 3;
                    break;
                case ItemKind.Grenade:
                    count = 1;
                    break;
            }
        }

        static Tier RollTier(string table, System.Random rng)
        {
            float r = (float)rng.NextDouble();
            float epic = 0.04f, rare = 0.16f, uncommon = 0.42f;
            if (table == "vault") { epic = 0.3f; rare = 0.5f; uncommon = 0.2f; }
            else if (table == "office") { epic = 0.07f; rare = 0.22f; uncommon = 0.4f; }
            if (r < epic) return Tier.Epic;
            if (r < epic + rare) return Tier.Rare;
            if (r < epic + rare + uncommon) return Tier.Uncommon;
            return Tier.Common;
        }
    }
}
