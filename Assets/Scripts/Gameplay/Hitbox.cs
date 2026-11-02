using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>A collider that passes damage to a Health with a multiplier (a zombie's head takes 2.5x).</summary>
    public class Hitbox : MonoBehaviour
    {
        public Health owner;
        public float multiplier = 1f;
        public bool isHead;
    }
}
