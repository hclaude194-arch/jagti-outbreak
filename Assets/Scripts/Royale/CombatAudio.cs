using Jagti.Audio;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The sound of hits landing, heard from where they land: a dull thump into a body, a hard knock
    /// on armour, a punch's smack, and the shooter's own shield ping when a hit was mostly soaked up
    /// by armour. Listens to Combatant.Damaged, so it works for every machine's view of the match.
    /// </summary>
    public class CombatAudio : MonoBehaviour
    {
        void OnEnable()
        {
            Combatant.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            Combatant.Damaged -= OnDamaged;
        }

        void OnDamaged(Combatant.DamageInfo d)
        {
            if (d.victim == null) return;
            string w = d.weapon ?? "";
            if (w == "the haze" || w == "fall" || w == "a fall" || w == "a hard landing") return;
            bool armoured = d.absorbed > d.amount * 0.25f;
            Vector3 at = d.victim.AimPoint;
            float vol = Mathf.Clamp(0.5f + d.amount / 60f, 0.5f, 1f);
            if (w == "fists") SoundSystem.Play("punch_hit", at, vol);
            else if (w != "grenade" && w != " blast" && w != "crash")
                SoundSystem.Play(armoured ? "hit_armour" : "hit_flesh", at, vol);

            if (d.attacker != null && d.attacker.isPlayer && armoured && !d.killing)
                SoundSystem.Play2D(d.armourBroke ? "armour_break" : "hit_shield", 0.45f);
        }
    }
}
