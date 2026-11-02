using Jagti.Player;
using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// Test overlay for the grey-box phase: frame rate, nearest place, controls,
    /// and fast travel between campus places with the [ and ] keys.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        public FirstPersonController player;
        public PlaceData[] places;

        float fps;
        float accumulated;
        int frames;
        int travelIndex = -1;
        bool showHelp = true;
        string nearestName = "";
        float nearestTimer;
        GUIStyle style;

        void Update()
        {
            accumulated += Time.unscaledDeltaTime;
            frames++;
            if (accumulated >= 0.5f)
            {
                fps = frames / accumulated;
                accumulated = 0f;
                frames = 0;
            }

            if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;

            if (places != null && places.Length > 0 && player != null)
            {
                if (Input.GetKeyDown(KeyCode.RightBracket)) Travel(travelIndex + 1);
                if (Input.GetKeyDown(KeyCode.LeftBracket)) Travel(travelIndex - 1);

                nearestTimer -= Time.unscaledDeltaTime;
                if (nearestTimer <= 0f)
                {
                    nearestTimer = 0.5f;
                    nearestName = FindNearest();
                }
            }
        }

        string FindNearest()
        {
            Vector3 p = player.transform.position;
            float best = float.MaxValue;
            string name = "";
            foreach (var place in places)
            {
                float d = (new Vector2(place.x, place.z) - new Vector2(p.x, p.z)).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    name = place.name;
                }
            }
            return name + "  (" + Mathf.RoundToInt(Mathf.Sqrt(best)) + " m)";
        }

        void Travel(int index)
        {
            travelIndex = ((index % places.Length) + places.Length) % places.Length;
            var place = places[travelIndex];
            player.Teleport(FindOpenGround(new Vector3(place.x, 0f, place.z)));
        }

        // Places can sit inside a building, so search outwards for open ground near the target.
        static Vector3 FindOpenGround(Vector3 target)
        {
            for (float radius = 0f; radius <= 90f; radius += 5f)
            {
                int steps = radius == 0f ? 1 : 12;
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    Vector3 probe = target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    // Open ground means the ray lands on the terrain or a road, not a roof or a tree.
                    if (Physics.Raycast(probe + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 400f) &&
                        (hit.collider is TerrainCollider || hit.collider.name.StartsWith("Road - ")))
                        return hit.point + Vector3.up * 0.2f;
                }
            }
            var terrain = Terrain.activeTerrain;
            float ground = terrain != null ? terrain.SampleHeight(target) + terrain.transform.position.y : 0f;
            return new Vector3(target.x, ground + 0.5f, target.z);
        }

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                style.normal.textColor = Color.white;
            }

            string text = "<b>" + Mathf.RoundToInt(fps) + " fps</b>   Near: " + nearestName;
            if (showHelp)
            {
                text += "\nWASD move · Shift sprint · F torch · Click attack · R reload · E search · Q heal"
                      + "\n[ and ] fast travel (testing) · Esc pause · H hides this";
                if (travelIndex >= 0 && places != null) text += "\nLast travel: " + places[travelIndex].name;
            }

            var rect = new Rect(12, 10, 760, showHelp ? 90 : 28);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x - 6, rect.y - 4, rect.width, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, text, style);
        }
    }
}
