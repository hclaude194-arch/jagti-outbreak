using UnityEngine;

namespace Jagti.Gameplay
{
    public enum ItemType { PistolAmmo = 0, Shells = 1, Bandage = 2, Medkit = 3, Battery = 4, Shotgun = 5 }

    /// <summary>What the player carries. Counts only; there is no weight or grid yet.</summary>
    public class Inventory : MonoBehaviour
    {
        public int pistolAmmo = 24;
        public int shells = 0;
        public int bandages = 1;
        public int medkits = 0;
        public int batteries = 1;
        public bool hasShotgun = false;

        public static readonly int[] Limits = { 120, 40, 8, 4, 6, 1 };

        /// <summary>Short text for the HUD's pickup feed.</summary>
        public event System.Action<string> Message;

        public int Count(ItemType type)
        {
            switch (type)
            {
                case ItemType.PistolAmmo: return pistolAmmo;
                case ItemType.Shells: return shells;
                case ItemType.Bandage: return bandages;
                case ItemType.Medkit: return medkits;
                case ItemType.Battery: return batteries;
                case ItemType.Shotgun: return hasShotgun ? 1 : 0;
            }
            return 0;
        }

        void Set(ItemType type, int value)
        {
            switch (type)
            {
                case ItemType.PistolAmmo: pistolAmmo = value; break;
                case ItemType.Shells: shells = value; break;
                case ItemType.Bandage: bandages = value; break;
                case ItemType.Medkit: medkits = value; break;
                case ItemType.Battery: batteries = value; break;
                case ItemType.Shotgun: hasShotgun = value > 0; break;
            }
        }

        /// <summary>Adds up to the carry limit; returns how many were taken.</summary>
        public int Add(ItemType type, int amount)
        {
            int have = Count(type);
            int room = Limits[(int)type] - have;
            int taken = Mathf.Clamp(amount, 0, room);
            if (taken > 0) Set(type, have + taken);
            return taken;
        }

        public bool Take(ItemType type, int amount)
        {
            int have = Count(type);
            if (have < amount) return false;
            Set(type, have - amount);
            return true;
        }

        /// <summary>Takes up to amount and returns how many were available (used by reloading).</summary>
        public int TakeUpTo(ItemType type, int amount)
        {
            int n = Mathf.Min(Count(type), amount);
            if (n > 0) Set(type, Count(type) - n);
            return n;
        }

        public void Say(string text)
        {
            if (Message != null) Message(text);
        }

        public static string Name(ItemType type, int count)
        {
            switch (type)
            {
                case ItemType.PistolAmmo: return count + " pistol rounds";
                case ItemType.Shells: return count + " shotgun shells";
                case ItemType.Bandage: return count == 1 ? "bandage" : count + " bandages";
                case ItemType.Medkit: return count == 1 ? "medkit" : count + " medkits";
                case ItemType.Battery: return count == 1 ? "torch battery" : count + " torch batteries";
                case ItemType.Shotgun: return "12-bore shotgun";
            }
            return type.ToString();
        }
    }
}
