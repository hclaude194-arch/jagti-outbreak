using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>Fades the pooled shot tracers each frame (lives on the "Shot Effects" object).</summary>
    public class BallisticsTicker : MonoBehaviour
    {
        void Update()
        {
            Ballistics.Tick(Time.deltaTime);
        }

        void OnDestroy()
        {
            Ballistics.Reset();
        }
    }
}
