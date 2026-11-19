using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>Fades out a light on the same object over a fraction of a second (grenade blasts).</summary>
    public class FadeLight : MonoBehaviour
    {
        public float time = 0.35f;
        Light lightSource;
        float start;

        void Start()
        {
            lightSource = GetComponent<Light>();
            if (lightSource != null) start = lightSource.intensity;
        }

        void Update()
        {
            if (lightSource == null) return;
            lightSource.intensity = Mathf.MoveTowards(lightSource.intensity, 0f, start / time * Time.deltaTime);
            if (lightSource.intensity <= 0f) lightSource.enabled = false;
        }
    }
}
