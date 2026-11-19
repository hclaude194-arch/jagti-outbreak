using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// The breeze. Sets the global _JagtiWind that the tree shader (Jagti/Foliage) bends to, with
    /// slow gusts and a direction that wanders a little, and drives a WindZone so the falling
    /// leaves drift the same way.
    /// </summary>
    public class WindDriver : MonoBehaviour
    {
        public Vector2 direction = new Vector2(0.8f, 0.6f);
        [Range(0f, 1f)] public float strength = 0.35f;
        public WindZone zone;

        static readonly int WindId = Shader.PropertyToID("_JagtiWind");

        /// <summary>Gust strength now, 0 (calm) to 1.</summary>
        public float Gust { get; private set; }

        /// <summary>The current wind direction on the ground plane (x, z).</summary>
        public Vector2 Direction { get; private set; }

        void Update()
        {
            float t = Time.time;
            float g = Mathf.PerlinNoise(t * 0.07f, 0.31f);
            Gust = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((g - 0.3f) / 0.5f));

            float turn = (Mathf.PerlinNoise(0.77f, t * 0.02f) - 0.5f) * 50f;
            Vector2 d = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector2.right;
            float rad = turn * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Direction = new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);

            Shader.SetGlobalVector(WindId, new Vector4(Direction.x, strength, Direction.y, Gust));

            if (zone != null)
            {
                zone.transform.rotation = Quaternion.LookRotation(new Vector3(Direction.x, 0f, Direction.y));
                zone.windMain = 0.3f + Gust * 0.9f;
            }
        }

        void OnDisable()
        {
            Shader.SetGlobalVector(WindId, Vector4.zero);
        }
    }
}
