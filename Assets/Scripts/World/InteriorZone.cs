using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// Keeps a building's interior cheap. The furniture and interior shell are drawn only while
    /// the camera is in or near the building; of the furniture, all of the camera's storey and
    /// the parts of the storeys above and below within neighbourRadius (seen up and down the
    /// stairwells and from the auditorium balcony). Only the room lights within lightRadius on
    /// those storeys are switched on, and they fade in and out over a fraction of a second rather
    /// than popping. The storey changes only once the camera is well past a floor line (so going
    /// up or down a stair doesn't flick the furniture of other storeys on and off). Colliders stay
    /// active either way, so zombies can still walk the corridors.
    /// </summary>
    public class InteriorZone : MonoBehaviour
    {
        public Vector2 min;               // world X, Z
        public Vector2 max;
        public float margin = 16f;        // start drawing the furniture this far outside the walls
        public float shellMargin = 60f;   // the shell (inner walls, floors, stairs) from further out,
                                          // so walls never pop in at the doorway or are missing when
                                          // the player arrives fast (landing, vehicle, zip line)
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

        public float lightFade = 0.45f;   // seconds for a room light to fade fully in or out

        bool drawn = true;
        float[] baseIntensity;
        float[] lightT;                   // 0 off .. 1 fully on, per light
        bool[] lightWant;
        int fading;                       // lights still on their way in or out
        int level = -1;
        bool wasHidden;
        bool shellDrawn = true;
        Vector3[] centres;
        float timer;
        Transform viewer;

        void Start()
        {
            centres = new Vector3[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) centres[i] = renderers[i].bounds.center;
            SetDrawn(false, false, 0, Vector3.zero);
            baseIntensity = new float[lights.Length];
            lightT = new float[lights.Length];
            lightWant = new bool[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                baseIntensity[i] = lights[i].intensity;
                lights[i].enabled = false;
            }
        }

        void LateUpdate()
        {
            if (fading == 0) return;
            float step = Time.deltaTime / Mathf.Max(0.01f, lightFade);
            int still = 0;
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                float target = lightWant[i] ? 1f : 0f;
                if (lightT[i] == target) continue;
                lightT[i] = Mathf.MoveTowards(lightT[i], target, step);
                // Ease in and out, so the change in brightness has no hard start.
                float t = lightT[i] * lightT[i] * (3f - 2f * lightT[i]);
                l.intensity = baseIntensity[i] * t;
                bool on = lightT[i] > 0f;
                if (l.enabled != on) l.enabled = on;
                if (lightT[i] != target) still++;
            }
            fading = still;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (Hidden != wasHidden) { wasHidden = Hidden; timer = 0f; }
            if (timer > 0f) return;
            timer = 0.1f;

            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                viewer = cam.transform;
            }
            Vector3 p = viewer.position;
            bool near = !Hidden && p.x > min.x - margin && p.x < max.x + margin && p.z > min.y - margin && p.z < max.y + margin;
            bool nearShell = !Hidden && p.x > min.x - shellMargin && p.x < max.x + shellMargin && p.z > min.y - shellMargin && p.z < max.y + shellMargin;
            level = CurrentLevel(p.y, level);
            if (near || drawn || nearShell != shellDrawn) SetDrawn(near, nearShell, level, p);

            float r2 = lightRadius * lightRadius;
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                bool on = near && (l.transform.position - p).sqrMagnitude < r2 && Shows(LevelOf(lightLevels, i), level);
                if (lightWant[i] != on) { lightWant[i] = on; fading++; }
            }
        }

        /// <summary>
        /// The camera's storey. It moves to the next storey only once the camera is 0.6 m past the
        /// line between them, so standing on a landing never flips it back and forth.
        /// </summary>
        int CurrentLevel(float y, int previous)
        {
            float h = Mathf.Max(0.1f, levelHeight);
            float f = (y - baseY - floorOffset + 0.5f) / h;
            int k = Mathf.Clamp(Mathf.FloorToInt(f), 0, maxLevel);
            if (previous >= 0 && k != previous && Mathf.Abs(k - previous) == 1)
            {
                float line = k > previous ? k : previous;   // the storey line crossed, in storeys
                if (Mathf.Abs(f - line) * h < 0.6f) return previous;
            }
            return k;
        }

        static int LevelOf(int[] levels, int i)
        {
            return levels != null && i < levels.Length ? levels[i] : -1;
        }

        static bool Shows(int itemLevel, int cameraLevel)
        {
            return itemLevel < 0 || Mathf.Abs(itemLevel - cameraLevel) <= 1;
        }

        void SetDrawn(bool on, bool shellOn, int level, Vector3 camera)
        {
            drawn = on;
            shellDrawn = shellOn;
            float n2 = neighbourRadius * neighbourRadius;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                int l = LevelOf(rendererLevels, i);
                bool show = l < 0 ? shellOn
                          : on && (l == level || (Mathf.Abs(l - level) == 1 && (centres[i] - camera).sqrMagnitude < n2));
                if (r.enabled != show) r.enabled = show;
            }
        }
    }
}
