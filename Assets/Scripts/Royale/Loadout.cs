using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>A gun someone carries: its model, tier and the rounds in its magazine.</summary>
    public class GunItem
    {
        public GunDef def;
        public Tier tier;
        public int loaded;

        public GunItem(GunDef def, Tier tier, int loaded)
        {
            this.def = def;
            this.tier = tier;
            this.loaded = loaded;
        }

        public string Label { get { return def.name + " (" + RoyaleItems.TierNames[(int)tier] + ")"; } }
    }

    /// <summary>
    /// Everything a player or bot carries in a match: three gun slots, ammo by type, armour,
    /// a backpack that raises carry limits, heals and grenades. Shared by the player and the bots
    /// so both follow the same rules. Pickups return what they could not take (it stays on the floor).
    /// </summary>
    public class Loadout
    {
        public const int Slots = 3;
        public readonly GunItem[] guns = new GunItem[Slots];
        public readonly int[] ammo = new int[4];
        public int vest, helmet, backpack;
        public float vestDurability, helmetDurability;
        public int bandages, medkits, chai, grenades;
        public int selected;            // gun slot in hand; -1 = fists

        public GunItem Current { get { return selected >= 0 && selected < Slots ? guns[selected] : null; } }

        public bool HasGun
        {
            get
            {
                foreach (var g in guns) if (g != null) return true;
                return false;
            }
        }

        public int AmmoLimit(AmmoType t) { return RoyaleItems.AmmoLimit[backpack, (int)t]; }

        int HealLimit(int index) { return RoyaleItems.HealLimit[backpack, index]; }

        public int CountFor(GunItem g) { return g == null ? 0 : ammo[(int)g.def.ammo]; }

        /// <summary>
        /// Takes an item. Returns the amount left over (0 when all of it was taken). For a gun with no
        /// free slot, swapOut receives the gun it replaced (in the slot in hand) so the caller can drop it.
        /// </summary>
        public int Take(ItemKind kind, int value, Tier tier, int count, bool allowSwap, out GunItem swapOut,
                        out int droppedArmourLevel)
        {
            swapOut = null;
            droppedArmourLevel = 0;
            switch (kind)
            {
                case ItemKind.Gun:
                {
                    var def = RoyaleItems.Guns[Mathf.Clamp(value, 0, RoyaleItems.Guns.Length - 1)];
                    var gun = new GunItem(def, tier, def.magazine);
                    for (int i = 0; i < Slots; i++)
                    {
                        if (guns[i] == null)
                        {
                            guns[i] = gun;
                            if (selected < 0) selected = i;
                            return 0;
                        }
                    }
                    if (!allowSwap) return count;
                    int slot = selected >= 0 ? selected : 0;
                    swapOut = guns[slot];
                    guns[slot] = gun;
                    selected = slot;
                    return 0;
                }
                case ItemKind.Ammo:
                {
                    int t = Mathf.Clamp(value, 0, 3);
                    int room = AmmoLimit((AmmoType)t) - ammo[t];
                    int taken = Mathf.Clamp(count, 0, room);
                    ammo[t] += taken;
                    return count - taken;
                }
                case ItemKind.Vest:
                    if (value < vest || (value == vest && vestDurability >= RoyaleItems.ArmourDurability[vest] - 1f)) return count;
                    droppedArmourLevel = vestDurability > 20f ? vest : 0;
                    vest = value;
                    vestDurability = RoyaleItems.ArmourDurability[value];
                    return 0;
                case ItemKind.Helmet:
                    if (value < helmet || (value == helmet && helmetDurability >= RoyaleItems.ArmourDurability[helmet] - 1f)) return count;
                    droppedArmourLevel = helmetDurability > 20f ? helmet : 0;
                    helmet = value;
                    helmetDurability = RoyaleItems.ArmourDurability[value];
                    return 0;
                case ItemKind.Backpack:
                    if (value <= backpack) return count;
                    droppedArmourLevel = backpack;
                    backpack = value;
                    return 0;
                case ItemKind.Bandage: return AddCapped(ref bandages, count, HealLimit(0));
                case ItemKind.Medkit: return AddCapped(ref medkits, count, HealLimit(1));
                case ItemKind.ChaiFlask: return AddCapped(ref chai, count, HealLimit(2));
                case ItemKind.Grenade: return AddCapped(ref grenades, count, HealLimit(3));
            }
            return count;
        }

        static int AddCapped(ref int have, int count, int limit)
        {
            int taken = Mathf.Clamp(count, 0, limit - have);
            have += taken;
            return count - taken;
        }

        /// <summary>Would picking this up help at all? (Used by bots and for the HUD prompt.)</summary>
        public bool Wants(ItemKind kind, int value, Tier tier)
        {
            switch (kind)
            {
                case ItemKind.Gun:
                    foreach (var g in guns) if (g == null) return true;
                    foreach (var g in guns)
                        if (g != null && (int)g.tier < (int)tier && g.def.gunClass == RoyaleItems.Guns[value].gunClass) return true;
                    return false;
                case ItemKind.Ammo: return ammo[value] < AmmoLimit((AmmoType)value);
                case ItemKind.Vest: return value > vest || (value == vest && vestDurability < RoyaleItems.ArmourDurability[vest] * 0.6f);
                case ItemKind.Helmet: return value > helmet || (value == helmet && helmetDurability < RoyaleItems.ArmourDurability[helmet] * 0.6f);
                case ItemKind.Backpack: return value > backpack;
                case ItemKind.Bandage: return bandages < HealLimit(0);
                case ItemKind.Medkit: return medkits < HealLimit(1);
                case ItemKind.ChaiFlask: return chai < HealLimit(2);
                case ItemKind.Grenade: return grenades < HealLimit(3);
            }
            return false;
        }

        /// <summary>Reloads the gun in hand from the carried ammo; returns rounds loaded.</summary>
        public int Reload(GunItem g)
        {
            if (g == null) return 0;
            int t = (int)g.def.ammo;
            int n = Mathf.Min(ammo[t], g.def.magazine - g.loaded);
            if (n <= 0) return 0;
            ammo[t] -= n;
            g.loaded += n;
            return n;
        }

        /// <summary>
        /// Lets the armour soak up part of a hit. Returns the damage that gets through.
        /// Helmets guard headshots, vests everything else; each piece wears out as it absorbs.
        /// </summary>
        public float Absorb(float damage, bool head)
        {
            if (head && helmet > 0 && helmetDurability > 0f)
            {
                float soak = Mathf.Min(damage * RoyaleItems.ArmourShare[helmet], helmetDurability);
                helmetDurability -= soak;
                if (helmetDurability <= 0f) helmet = 0;
                return damage - soak;
            }
            if (!head && vest > 0 && vestDurability > 0f)
            {
                float soak = Mathf.Min(damage * RoyaleItems.ArmourShare[vest], vestDurability);
                vestDurability -= soak;
                if (vestDurability <= 0f) vest = 0;
                return damage - soak;
            }
            return damage;
        }

        /// <summary>The slot holding the best gun with ammo for it (bots pick their weapon with this).</summary>
        public int BestSlot(float distance)
        {
            int best = -1;
            float bestScore = -1f;
            for (int i = 0; i < Slots; i++)
            {
                var g = guns[i];
                if (g == null) continue;
                if (g.loaded <= 0 && CountFor(g) <= 0) continue;
                float score = g.def.damage * g.def.pellets / g.def.interval * RoyaleItems.TierDamage(g.tier);
                // Prefer guns suited to the range: shotguns close, the marksman rifle far.
                float ideal = g.def.gunClass == GunClass.Shotgun ? 8f : g.def.gunClass == GunClass.Smg ? 18f :
                              g.def.gunClass == GunClass.Sidearm ? 20f : g.def.gunClass == GunClass.Rifle ? 45f : 110f;
                score *= 1f / (1f + Mathf.Abs(distance - ideal) / ideal);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }
    }
}
