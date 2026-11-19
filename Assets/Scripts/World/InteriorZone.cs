using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// Keeps a building's interior cheap. The furniture and interior shell are drawn only while
    /// the camera is in or near the building; of the furniture, all of the camera's storey and
    /// the parts of the storeys above and below within neighbourRadius (seen up and down the
    /// stairwells and from the auditorium balcony). Only the room lights within lightRadius on
    /// those storeys are switched on. Colliders stay
    /// active either way, so zombies can still walk the corridors.
    /// </summary>
    public class InteriorZone : MonoBehaviour
    {
        public Vector2 min;               // world X, Z
        public Vector2 max;
        public float margin = 10f;        // start drawing this far outside the walls
        public float lightRadius = 24f;
        public float baseY;               // the building's base height
        public float levelHeight = 3.4f;  // storey height
        public float floorOffset = 0.6f;  // ground floor level above the base
        public int maxLevel = 4;
        public float neighbourRadius = 30f;
        public Renderer[] renderers;
        public int[] rendererLevels;      // storey of each renderer; -1 = every storey
        public Light[] lights;
        public int[] lightLevels;

        /// <summary>Hides every interior at once (the lift's cutaway shows the building as a frame instead).</summary>
        public static bool Hidden;

        bool drawn = true;
        bool wasHidden;
        Vector3[] centres;
        float timer;
        Transform viewer;

        void Start()
        {
            centres = new Vector3[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) centres[i] = renderers[i].bounds.center;
            SetDrawn(false, 0, Vector3.zero);
            foreach (var l in lights) if (l != null) l.enabled = false;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (Hidden != wasHidden) { wasHidden = Hidden; timer = 0f; }
            if (timer > 0f) return;
            timer = 0.25f;

            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                viewer = cam.transform;
            }
            Vector3 p = viewer.position;
            bool near = !Hidden && p.x > min.x - margin && p.x < max.x + margin && p.z > min.y - margin && p.z < max.y + margin;
            int level = CurrentLevel(p.y);
            if (near || drawn) SetDrawn(near, level, p);

            float r2 = lightRadius * lightRadius;
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                bool on = near && (l.transform.position - p).sqrMagnitude < r2 && Shows(LevelOf(lightLevels, i), level);
                if (l.enabled != on) l.enabled = on;
            }
        }

        int CurrentLevel(float y)
        {
            int k = Mathf.FloorToInt((y - baseY - floorOffset + 0.5f) / Mathf.Max(0.1f, levelHeight));
            return Mathf.Clamp(k, 0, maxLevel);
        }

        static int LevelOf(int[] levels, int i)
        {
            return levels != null && i < levels.Length ? levels[i] : -1;
        }

        static bool Shows(int itemLevel, int cameraLevel)
        {
            return itemLevel < 0 || Mathf.Abs(itemLevel - cameraLevel) <= 1;
        }

        void SetDrawn(bool on, int level, Vector3 camera)
        {
            drawn = on;
            float n2 = neighbourRadius * neighbourRadius;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                int l = LevelOf(rendererLevels, i);
                bool show = on && (l < 0 || l == level ||
                                   (Mathf.Abs(l - level) == 1 && (centres[i] - camera).sqrMagnitude < n2));
                if (r.enabled != show) r.enabled = show;
            }
        }
    }
}
