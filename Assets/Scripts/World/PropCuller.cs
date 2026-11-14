using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// Hides small outdoor things (bins, bollards, benches, sign text, cover pieces) beyond a set
    /// distance from the camera, so the far campus costs fewer draw calls. Each renderer has its own
    /// distance (filled in by the builder); a slice of them is checked each frame, with a few metres
    /// of give so nothing flickers at the edge.
    /// </summary>
    public class PropCuller : MonoBehaviour
    {
        public Renderer[] renderers;
        public float[] distances;
        public int perFrame = 400;
        public float give = 6f;

        Vector3[] centres;
        bool[] shown;
        int next;

        void Start()
        {
            if (renderers == null) renderers = new Renderer[0];
            centres = new Vector3[renderers.Length];
            shown = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                centres[i] = renderers[i].bounds.center;
                shown[i] = renderers[i].enabled;
            }
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || renderers.Length == 0) return;
            Vector3 eye = cam.transform.position;
            int n = Mathf.Min(perFrame, renderers.Length);
            for (int k = 0; k < n; k++)
            {
                int i = next;
                next = (next + 1) % renderers.Length;
                var r = renderers[i];
                if (r == null) continue;
                float limit = distances != null && i < distances.Length ? distances[i] : 150f;
                float d2 = (centres[i] - eye).sqrMagnitude;
                bool want = shown[i] ? d2 < (limit + give) * (limit + give) : d2 < limit * limit;
                if (want != shown[i])
                {
                    shown[i] = want;
                    r.enabled = want;
                }
            }
        }
    }
}
