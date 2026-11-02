using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// Keeps a building's interior cheap when nobody is inside: the furniture and interior shell
    /// are drawn only while the camera is in or near the building, and only the room lights
    /// within lightRadius of the camera are switched on. Colliders stay active either way, so
    /// zombies can still walk the corridors.
    /// </summary>
    public class InteriorZone : MonoBehaviour
    {
        public Vector2 min;               // world X, Z
        public Vector2 max;
        public float margin = 10f;        // start drawing this far outside the walls
        public float lightRadius = 24f;
        public Renderer[] renderers;
        public Light[] lights;

        bool drawn = true;
        float timer;
        Transform viewer;

        void Start()
        {
            SetDrawn(false);
            foreach (var l in lights) if (l != null) l.enabled = false;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.25f;

            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                viewer = cam.transform;
            }
            Vector3 p = viewer.position;
            bool near = p.x > min.x - margin && p.x < max.x + margin && p.z > min.y - margin && p.z < max.y + margin;
            if (near != drawn) SetDrawn(near);

            float r2 = lightRadius * lightRadius;
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                bool on = near && (l.transform.position - p).sqrMagnitude < r2;
                if (l.enabled != on) l.enabled = on;
            }
        }

        void SetDrawn(bool on)
        {
            drawn = on;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = on;
        }
    }
}
