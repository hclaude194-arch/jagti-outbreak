using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A player or bot taking part in a match: name, health, what they carry, kills and damage.
    /// All hits go through ReceiveHit so armour, kill credit and the kill feed work the same for everyone.
    /// Also runs healing (bandage, medkit, chai flask), which takes time and can be cancelled by switching.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Combatant : MonoBehaviour
    {
        public static readonly List<Combatant> All = new List<Combatant>();

        public string displayName = "Player";
        public bool isPlayer;
        /// <summary>Match-wide number: humans 1 to 8, bots from 100 (the same on every machine online).</summary>
        public int netId;
        /// <summary>
        /// True when this machine decides this combatant's health. Always true offline; online the host
        /// owns the bots and every player owns themselves, and the others are puppets (NetPuppet).
        /// </summary>
        public bool owned = true;

        /// <summary>
        /// Online: a hit by someone this machine owns on someone it doesn't (victim, damage, head,
        /// attacker, weapon, from, ignoreArmour). NetMatch sends it to the victim's machine.
        /// </summary>
        public static System.Action<Combatant, float, bool, Combatant, string, Vector3, bool> ForwardHit;
        /// <summary>Where shots come from and where others aim at (head height).</summary>
        public Transform eye;

        public readonly Loadout loadout = new Loadout();
        public Health Health { get; private set; }
        public int Kills { get; set; }
        public float DamageDealt { get; set; }
        public int Placement { get; set; }
        public float DiedAt { get; private set; }
        /// <summary>False while still in the sky bus or gliding down.</summary>
        public bool Landed { get; set; }
        public bool Alive { get { return Health != null && !Health.IsDead; } }

        public Combatant LastAttacker { get; private set; }
        public float LastHitTime { get; private set; }
        public Vector3 LastHitFrom { get; private set; }
        public struct DamageInfo
        {
            public Combatant victim, attacker;
            public float amount, absorbed;
            public bool head, armourBroke, killing;
            public int armourLevel;
            public string weapon;
        }

        /// <summary>Every hit on anyone (the HUD shows damage numbers and armour breaks from it).</summary>
        public static System.Action<DamageInfo> Damaged;

        /// <summary>Called when a shot passes close by (bots use it to take cover).</summary>
        public System.Action<Combatant> UnderFire;

        // Healing in progress.
        public ItemKind HealingKind { get; private set; }
        public float HealProgress { get { return healTime > 0f ? Mathf.Clamp01(healT / healTime) : 0f; } }
        public bool IsHealing { get { return healTime > 0f; } }
        public float BoostLeft { get; private set; }

        float healT, healTime;
        string lastWeapon = "";
        bool lastHead;

        public Vector3 AimPoint
        {
            get { return eye != null ? eye.position - Vector3.up * 0.35f : transform.position + Vector3.up * 1.2f; }
        }

        void Awake()
        {
            Health = GetComponent<Health>();
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            Health.Died += OnDied;
        }

        void OnDisable()
        {
            All.Remove(this);
            Health.Died -= OnDied;
        }

        /// <summary>
        /// A hit from a gun, grenade or the haze (attacker null). Armour soaks part of gun and grenade
        /// damage; the haze ignores armour.
        /// </summary>
        public void ReceiveHit(float damage, bool head, Combatant attacker, string weapon, Vector3 from, bool ignoreArmour = false)
        {
            if (!Alive || damage <= 0f) return;
            if (!owned)
            {
                // Someone else's machine decides; tell it about hits made here.
                if (attacker != null && attacker.owned && ForwardHit != null) ForwardHit(this, damage, head, attacker, weapon, from, ignoreArmour);
                return;
            }
            // A copy of another machine's shot or grenade: the real hit arrives over the network.
            if (attacker != null && !attacker.owned) return;
            ApplyHit(damage, head, attacker, weapon, from, ignoreArmour);
        }

        /// <summary>Applies a hit here without the online ownership checks (NetMatch calls it for hits sent to this machine).</summary>
        public void ApplyHit(float damage, bool head, Combatant attacker, string weapon, Vector3 from, bool ignoreArmour)
        {
            if (!Alive || damage <= 0f) return;
            float raw = damage;
            int armourBefore = head ? loadout.helmet : loadout.vest;
            if (!ignoreArmour) damage = loadout.Absorb(damage, head);
            bool broke = armourBefore > 0 && (head ? loadout.helmet : loadout.vest) == 0;
            if (Damaged != null) Damaged(new DamageInfo
            {
                victim = this, attacker = attacker, amount = Mathf.Min(damage, Health.current), absorbed = raw - damage,
                head = head, armourBroke = broke, armourLevel = armourBefore, weapon = weapon,
                killing = damage >= Health.current
            });
            NoteAttacker(attacker, weapon, head, Mathf.Min(damage, Health.current), from);
            Health.Damage(damage, from);
        }

        void NoteAttacker(Combatant attacker, string weapon, bool head, float dealt, Vector3 from)
        {
            if (attacker != null && attacker != this)
            {
                LastAttacker = attacker;
                lastWeapon = weapon;
                lastHead = head;
                attacker.DamageDealt += dealt;
            }
            else if (attacker == null && weapon != null)
            {
                lastWeapon = weapon;
                lastHead = false;
                // Keep credit with whoever hit last if they did so recently (finishing someone off in the haze).
                if (Time.time - LastHitTime > 10f) LastAttacker = null;
            }
            LastHitTime = Time.time;
            LastHitFrom = from;
        }

        /// <summary>
        /// A puppet's copy of a hit its owner worked out (online): the same feed, numbers and death as a
        /// local hit, ending at the owner's health.
        /// </summary>
        public void ApplyRemoteResult(DamageInfo info, Vector3 from, float healthAfter)
        {
            if (!Alive) return;
            info.victim = this;
            if (Damaged != null) Damaged(info);
            NoteAttacker(info.attacker, info.weapon, info.head, info.amount, from);
            float drop = info.killing ? Health.current : Health.current - healthAfter;
            if (drop > 0f) Health.Damage(drop, from);
            else Health.current = Mathf.Max(1f, healthAfter);
        }

        /// <summary>The combatant with this match number, or null.</summary>
        public static Combatant ById(int id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].netId == id) return All[i];
            return null;
        }

        void OnDied()
        {
            DiedAt = Time.time;
            healTime = 0f;
            var director = MatchDirector.Instance;
            if (director != null) director.OnCombatantDied(this, LastAttacker, lastWeapon, lastHead);
        }

        // ------------------------------------------------------------------ healing

        /// <summary>Starts using a heal. Returns false if there is none or it would do nothing.</summary>
        public bool StartHeal(ItemKind kind)
        {
            if (!Alive || IsHealing) return false;
            var h = Health;
            switch (kind)
            {
                case ItemKind.Bandage:
                    if (loadout.bandages <= 0 || h.current >= RoyaleItems.BandageCap) return false;
                    healTime = RoyaleItems.BandageTime;
                    break;
                case ItemKind.Medkit:
                    if (loadout.medkits <= 0 || h.current >= h.max - 1f) return false;
                    healTime = RoyaleItems.MedkitTime;
                    break;
                case ItemKind.ChaiFlask:
                    if (loadout.chai <= 0) return false;
                    healTime = RoyaleItems.ChaiTime;
                    break;
                default:
                    return false;
            }
            HealingKind = kind;
            healT = 0f;
            return true;
        }

        public void CancelHeal()
        {
            healTime = 0f;
        }

        void Update()
        {
            if (!Alive) return;
            if (healTime > 0f)
            {
                healT += Time.deltaTime;
                if (healT >= healTime)
                {
                    healTime = 0f;
                    FinishHeal();
                }
            }
            if (BoostLeft > 0f)
            {
                float rate = RoyaleItems.ChaiBoost / RoyaleItems.ChaiBoostTime;
                float h = Mathf.Min(BoostLeft, rate * Time.deltaTime);
                BoostLeft -= h;
                Health.Heal(h);
            }
        }

        void FinishHeal()
        {
            var h = Health;
            switch (HealingKind)
            {
                case ItemKind.Bandage:
                    if (loadout.bandages <= 0) return;
                    loadout.bandages--;
                    h.Heal(Mathf.Min(RoyaleItems.BandageHeal, Mathf.Max(0f, RoyaleItems.BandageCap - h.current)));
                    break;
                case ItemKind.Medkit:
                    if (loadout.medkits <= 0) return;
                    loadout.medkits--;
                    h.Heal(h.max);
                    break;
                case ItemKind.ChaiFlask:
                    if (loadout.chai <= 0) return;
                    loadout.chai--;
                    BoostLeft = Mathf.Min(100f, BoostLeft + RoyaleItems.ChaiBoost);
                    break;
            }
        }

        /// <summary>The nearest living opponent within range (no line-of-sight test).</summary>
        public static Combatant Nearest(Vector3 p, float range, Combatant except)
        {
            Combatant best = null;
            float bestD = range * range;
            foreach (var c in All)
            {
                if (c == except || !c.Alive || !c.Landed) continue;
                float d = (c.transform.position - p).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best;
        }

        public static int AliveCount()
        {
            int n = 0;
            foreach (var c in All) if (c.Alive) n++;
            return n;
        }
    }
}
