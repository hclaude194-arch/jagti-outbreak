using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>Hit points for the player and the zombies.</summary>
    public class Health : MonoBehaviour
    {
        public float max = 100f;
        public float current = -1f;

        /// <summary>amount, the point the damage came from.</summary>
        public event System.Action<float, Vector3> Damaged;
        public event System.Action Died;

        public bool IsDead { get { return current <= 0f; } }
        public float Fraction { get { return max > 0f ? Mathf.Clamp01(current / max) : 0f; } }

        void Awake()
        {
            if (current < 0f) current = max;
        }

        public void ResetHealth(float newMax)
        {
            max = newMax;
            current = newMax;
        }

        public void Damage(float amount, Vector3 from)
        {
            if (IsDead || amount <= 0f) return;
            current = Mathf.Max(0f, current - amount);
            if (Damaged != null) Damaged(amount, from);
            if (current <= 0f && Died != null) Died();
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            current = Mathf.Min(max, current + amount);
        }
    }
}
