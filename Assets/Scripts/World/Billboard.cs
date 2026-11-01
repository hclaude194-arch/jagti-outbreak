using UnityEngine;

namespace Jagti.World
{
    /// <summary>Keeps a floating label turned towards the camera.</summary>
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 away = transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(away);
        }
    }
}
