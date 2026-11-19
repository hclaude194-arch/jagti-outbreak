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
        Attachment = 9,
        Smoke = 10,
    }

    /// <summary>Gun attachments. Each gun has four places: sight, muzzle, grip and magazine.</summary>
    public enum AttachmentType
    {
        RedDot = 0, Scope2x = 1, Scope4x = 2, Scope8x = 3,
        Compensator = 4, Suppressor = 5,
        Grip = 6,
        ExtendedMag = 7,
    }

    public enum AttachmentSlot { Sight = 0, Muzzle = 1, Grip = 2, Magazine = 3 }

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
        public int burst;             // rounds per trigger pull (0 = single or automatic)
        public float burstGap;        // pause after a burst
        public bool bolt;             // works the bolt after every shot
        public float recoilSide = 0.3f;   // sideways wander of the recoil pattern (share of the kick)
        public float recoilBias;          // the pattern's drift to one side (-1 left .. 1 right)
        public float aimTime = 0.17f;     // seconds to bring the sights up
        public bool takesScopes = true;   // 2x and up
        public bool takesGrip = true;
        public bool takesExtMag = true;
        public bool takesMuzzle = true;
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
                range = 60f, falloffMin = 0.55f, recoil = 1.6f, zoomFov = 58f, noise = 70f, sound = "pistol",
                recoilSide = 0.25f, recoilBias = 0.2f, aimTime = 0.12f, takesScopes = false, takesGrip = false },
            new GunDef { id = 1, name = "Chenab SMG", gunClass = GunClass.Smg, ammo = AmmoType.Light, damage = 17f, headMultiplier = 1.8f,
                interval = 0.075f, automatic = true, magazine = 30, reloadTime = 2.0f, hipSpread = 2.4f, aimSpread = 1.0f,
                range = 55f, falloffMin = 0.5f, recoil = 0.9f, zoomFov = 56f, noise = 80f, sound = "smg",
                recoilSide = 0.45f, recoilBias = -0.15f, aimTime = 0.14f },
            new GunDef { id = 2, name = "Banihal 12", gunClass = GunClass.Shotgun, ammo = AmmoType.Shells, damage = 11f, headMultiplier = 1.5f,
                interval = 0.85f, automatic = false, magazine = 5, reloadTime = 2.6f, hipSpread = 5.5f, aimSpread = 4.2f,
                range = 30f, falloffMin = 0.25f, recoil = 5.5f, pellets = 9, zoomFov = 58f, noise = 100f, sound = "shotgun",
                recoilSide = 0.15f, takesScopes = false, takesGrip = false },
            new GunDef { id = 3, name = "Tawi AR", gunClass = GunClass.Rifle, ammo = AmmoType.Rifle, damage = 24f, headMultiplier = 2f,
                interval = 0.11f, automatic = true, magazine = 30, reloadTime = 2.3f, hipSpread = 2.0f, aimSpread = 0.45f,
                range = 160f, falloffMin = 0.7f, recoil = 1.25f, zoomFov = 50f, noise = 120f, sound = "rifle",
                recoilSide = 0.35f, recoilBias = 0.35f },
            new GunDef { id = 4, name = "Jhelum DMR", gunClass = GunClass.Marksman, ammo = AmmoType.Heavy, damage = 52f, headMultiplier = 2.3f,
                interval = 0.42f, automatic = false, magazine = 10, reloadTime = 2.8f, hipSpread = 3.0f, aimSpread = 0.08f,
                range = 450f, falloffMin = 0.85f, recoil = 3.4f, zoomFov = 22f, noise = 160f, sound = "dmr",
                recoilSide = 0.2f, recoilBias = -0.1f, aimTime = 0.24f },
            new GunDef { id = 5, name = "Basohli .45", gunClass = GunClass.Sidearm, ammo = AmmoType.Light, damage = 44f, headMultiplier = 2.1f,
                interval = 0.5f, automatic = false, magazine = 6, reloadTime = 2.5f, hipSpread = 1.9f, aimSpread = 0.35f,
                range = 70f, falloffMin = 0.6f, recoil = 4.2f, zoomFov = 56f, noise = 95f, sound = "revolver",
                recoilSide = 0.2f, recoilBias = 0.3f, aimTime = 0.14f, takesScopes = false, takesGrip = false, takesExtMag = false },
            new GunDef { id = 6, name = "Ujh Carbine", gunClass = GunClass.Rifle, ammo = AmmoType.Rifle, damage = 27f, headMultiplier = 2f,
                interval = 0.065f, automatic = false, burst = 3, burstGap = 0.32f, magazine = 24, reloadTime = 2.1f, hipSpread = 2.2f, aimSpread = 0.35f,
                range = 170f, falloffMin = 0.72f, recoil = 1.5f, zoomFov = 50f, noise = 115f, sound = "carbine",
                recoilSide = 0.18f, recoilBias = -0.25f, aimTime = 0.17f },
            new GunDef { id = 7, name = "Bhaderwah Double", gunClass = GunClass.Shotgun, ammo = AmmoType.Shells, damage = 12f, headMultiplier = 1.5f,
                interval = 0.22f, automatic = false, magazine = 2, reloadTime = 2.2f, hipSpread = 6.5f, aimSpread = 5f,
                range = 24f, falloffMin = 0.2f, recoil = 7f, pellets = 10, zoomFov = 58f, noise = 110f, sound = "double",
                recoilSide = 0.1f, takesScopes = false, takesGrip = false, takesExtMag = false, takesMuzzle = false },
            new GunDef { id = 8, name = "Udhampur LMG", gunClass = GunClass.Rifle, ammo = AmmoType.Rifle, damage = 23f, headMultiplier = 1.9f,
                interval = 0.095f, automatic = true, magazine = 75, reloadTime = 5.2f, hipSpread = 3.2f, aimSpread = 0.7f,
                range = 150f, falloffMin = 0.68f, recoil = 1.05f, zoomFov = 50f, noise = 130f, sound = "lmg",
                recoilSide = 0.55f, recoilBias = 0.1f, aimTime = 0.32f, takesExtMag = false },
            new GunDef { id = 9, name = "Pir Panjal", gunClass = GunClass.Marksman, ammo = AmmoType.Heavy, damage = 90f, headMultiplier = 2.6f,
                interval = 1.35f, automatic = false, bolt = true, magazine = 5, reloadTime = 3.4f, hipSpread = 6f, aimSpread = 0.02f,
                range = 600f, falloffMin = 0.9f, recoil = 6f, zoomFov = 16f, noise = 190f, sound = "sniper",
                recoilSide = 0.1f, aimTime = 0.3f, takesGrip = false },
        };

        // ------------------------------------------------------------------ attachments

        public static readonly string[] AttachmentNames =
            { "Red dot sight", "2x sight", "4x scope", "8x scope", "Compensator", "Suppressor", "Fore grip", "Extended magazine" };

        public static AttachmentSlot SlotOf(AttachmentType a)
        {
            if ((int)a <= 3) return AttachmentSlot.Sight;
            if ((int)a <= 5) return AttachmentSlot.Muzzle;
            return a == AttachmentType.Grip ? AttachmentSlot.Grip : AttachmentSlot.Magazine;
        }

        public static bool Fits(GunDef g, AttachmentType a)
        {
            switch (SlotOf(a))
            {
                case AttachmentSlot.Sight:
                    // Marksman rifles have their own scope: only a stronger one helps.
                    if (g.gunClass == GunClass.Marksman) return a == AttachmentType.Scope4x || a == AttachmentType.Scope8x;
                    if (a == AttachmentType.RedDot) return g.gunClass != GunClass.Shotgun || g.takesMuzzle;
                    if (a == AttachmentType.Scope8x) return g.gunClass == GunClass.Marksman;
                    return g.takesScopes;
                case AttachmentSlot.Muzzle: return g.takesMuzzle;
                case AttachmentSlot.Grip: return g.takesGrip;
                default: return g.takesExtMag;
            }
        }

        /// <summary>Field of view through a sight; 0 when the gun's own sight is used.</summary>
        public static float SightFov(int sight)
        {
            switch (sight)
            {
                case 0: return 54f;
                case 1: return 34f;
                case 2: return 18f;
                case 3: return 9f;
            }
            return 0f;
        }

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

        public static readonly int[,] HealLimit = { { 5, 2, 2, 2, 2 },         // bandage, medkit, chai, grenade, smoke per pack level
                                                    { 8, 3, 3, 3, 3 },
                                                    { 12, 4, 4, 4, 3 },
                                                    { 16, 5, 5, 5, 4 } };

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
                case ItemKind.Smoke: return "Smoke grenade";
                case ItemKind.Attachment: return AttachmentNames[Mathf.Clamp(value, 0, AttachmentNames.Length - 1)];
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
                case ItemKind.Smoke: return Tier.Common;
                case ItemKind.Attachment: return value == 3 || value == 5 ? Tier.Epic : value == 2 || value == 7 ? Tier.Rare : Tier.Uncommon;
            }
            return Tier.Common;
        }

        // ------------------------------------------------------------------ loot tables

        /// <summary>A roll from a place's loot table: what kind of item, and how good.</summary>
        public static void Roll(string table, System.Random rng, out ItemKind kind, out int value, out Tier tier, out int count)
        {
            Roll(table, rng, false, out kind, out value, out tier, out count);
        }

        /// <summary>As Roll; better (stashes, rich-pickings places) shifts the tiers up.</summary>
        public static void Roll(string table, System.Random rng, bool better, out ItemKind kind, out int value, out Tier tier, out int count)
        {
            // Weights: gun, ammo, vest, helmet, pack, bandage, medkit, chai, grenade, attachment, smoke.
            float[] w;
            switch (table)
            {
                case "medical": w = new[] { 6f, 6f, 3f, 3f, 3f, 30f, 22f, 12f, 1f, 1f, 2f }; break;
                case "food": w = new[] { 10f, 12f, 4f, 4f, 4f, 14f, 4f, 30f, 3f, 3f, 3f }; break;
                case "office": w = new[] { 30f, 20f, 10f, 9f, 5f, 8f, 3f, 4f, 7f, 12f, 4f }; break;
                case "vault": w = new[] { 36f, 13f, 15f, 13f, 8f, 0f, 5f, 0f, 6f, 16f, 3f }; break;
                case "sports": w = new[] { 14f, 14f, 16f, 16f, 14f, 10f, 3f, 8f, 3f, 5f, 4f }; break;
                case "storage": w = new[] { 17f, 22f, 12f, 10f, 12f, 8f, 3f, 5f, 8f, 10f, 6f }; break;
                case "academic": w = new[] { 21f, 23f, 8f, 8f, 6f, 13f, 3f, 10f, 6f, 9f, 4f }; break;
                case "outdoor": w = new[] { 23f, 25f, 8f, 8f, 6f, 12f, 3f, 7f, 6f, 6f, 4f }; break;
                default: w = new[] { 21f, 24f, 9f, 9f, 7f, 13f, 3f, 8f, 4f, 8f, 3f }; break; // residential
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
            tier = RollTier(table, rng, better);
            value = 0;
            count = 1;
            switch (kind)
            {
                case ItemKind.Gun:
                    value = RollGun(rng, tier);
                    break;
                case ItemKind.Attachment:
                {
                    // Scopes and suppressors are the rare ones.
                    float a = (float)rng.NextDouble();
                    value = a < 0.2f ? 0 : a < 0.34f ? 1 : a < 0.44f ? 2 : a < 0.48f ? 3 : a < 0.64f ? 4 : a < 0.7f ? 5 : a < 0.86f ? 6 : 7;
                    tier = DisplayTier(ItemKind.Attachment, value, tier);
                    break;
                }
                case ItemKind.Smoke:
                    count = 1;
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

        /// <summary>
        /// Which gun: sidearms and SMGs are common finds, rifles and shotguns less so, the marksman
        /// rifle, the LMG and the bolt-action rifle are rare (more likely in better tiers).
        /// </summary>
        static int RollGun(System.Random rng, Tier tier)
        {
            float rare = 1f + (int)tier * 0.6f;
            float[] w = { 13f, 14f, 10f, 12f, 4f * rare, 7f, 7f, 6f, 2.5f * rare, 1.5f * rare };
            float total = 0f;
            foreach (float x in w) total += x;
            float r = (float)rng.NextDouble() * total;
            for (int i = 0; i < w.Length; i++)
            {
                if (r < w[i]) return i;
                r -= w[i];
            }
            return 0;
        }

        static Tier RollTier(string table, System.Random rng, bool better)
        {
            float r = (float)rng.NextDouble();
            float epic = 0.04f, rare = 0.16f, uncommon = 0.42f;
            if (table == "vault") { epic = 0.3f; rare = 0.5f; uncommon = 0.2f; }
            else if (table == "office") { epic = 0.07f; rare = 0.22f; uncommon = 0.4f; }
            if (better && table != "vault") { epic *= 2.5f; rare *= 1.6f; uncommon = 0.4f; }
            if (r < epic) return Tier.Epic;
            if (r < epic + rare) return Tier.Rare;
            if (r < epic + rare + uncommon) return Tier.Uncommon;
            return Tier.Common;
        }
    }
}
