using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A loot stash: a steel trunk that holds a few better-than-floor items until someone opens it (E).
    /// The items are rolled with the match's loot (so every machine has the same ones) and lie sealed,
    /// unseen and untouchable, in front of the trunk until it opens. Online the opener's machine sends
    /// NetMsg.Stash and everyone else opens it too.
    /// </summary>
    public class LootStash
    {
        /// <summary>Index in LootDirector.Stashes, the same on every machine.</summary>
        public int index;
        public Vector3 position;
        public float yaw;
        public string table;
        public bool indoor;
        public bool open;
        /// <summary>Time.time when it opened (for the lid swing).</summary>
        public float openedAt;
        public readonly List<LootDrop> contents = new List<LootDrop>();
        /// <summary>Set by a bot heading for it.</summary>
        public Combatant claimedBy;
        public float claimUntil;

        public Vector3 Front { get { return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; } }

        public string Name { get { return NameFor(table); } }

        public static string NameFor(string table)
        {
            switch (table)
            {
                case "medical": return "Medicine trunk";
                case "food": return "Mess trunk";
                case "office": return "Office trunk";
                case "vault": return "Bank strongbox";
                case "sports": return "Kit trunk";
                case "storage": return "Store trunk";
                case "academic": return "Department trunk";
                case "outdoor": return "Supply trunk";
                default: return "Hostel trunk";
            }
        }
    }
}
