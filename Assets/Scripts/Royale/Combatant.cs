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
        /// <summary>Where shots come from and where others aim at (head height).</summary>
        public Transform eye;

        public readonly Loadout loadout = new Loadout();
        public Health Health { get; private set; }
        public int Kills { get; set; }
        public float DamageDealt { get; set; }
        public int Placement { get; set; }
        public float DiedAt { get; private set; }
        /// <summary>False while still in the helicopter or gliding down.</summary>
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
            if (attacker != null && attacker != this)
            {
                LastAttacker = attacker;
                lastWeapon = weapon;
                lastHead = head;
                attacker.DamageDealt += Mathf.Min(damage, Health.current);
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
            Health.Damage(damage, from);
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
