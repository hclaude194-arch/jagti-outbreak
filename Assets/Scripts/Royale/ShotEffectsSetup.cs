using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>Hands the tracer and impact-dust materials (made by RoyaleBuilder) to Ballistics when a match loads.</summary>
    public class ShotEffectsSetup : MonoBehaviour
    {
        public Material tracer;
        public Material puff;

        void Awake()
        {
            Ballistics.Reset();
            Ballistics.tracerMaterial = tracer;
            Ballistics.puffMaterial = puff;
        }
    }
}
