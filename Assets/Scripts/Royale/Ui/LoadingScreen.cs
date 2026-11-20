using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jagti.Royale
{
    /// <summary>
    /// Fades to black, reloads the campus behind a loading card (title, progress bar and a tip), and
    /// fades back into the new scene, instead of the game freezing on the last frame while the campus
    /// loads. Offline the load runs in the background (LoadSceneAsync); online it loads in one go
    /// after the card is up, as before, so network messages never reach a half-loaded scene.
    /// Lives on its own object that survives the load and removes itself once faded out.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        static LoadingScreen current;

        string title;
        float progress;
        float cover;            // 0 clear, 1 black
        string tip;

        static readonly string[] Tips =
        {
            "Gun sights zoom the view and slow the mouse with it. Aim sensitivity is in the settings.",
            "Middle mouse or T drops a marker where you look. Press it again on the marker to clear it.",
            "The sky bus crosses the whole campus. Pick a spot on the map (M) before you jump.",
            "Trunks hum softly when you're close. Press E to open them for sealed loot.",
            "A chai flask gives a boost that heals you slowly over the next while.",
            "The haze hurts more every round. Watch the timer under the minimap.",
            "Glass breaks. Sprint or jump through a window if a door is too far.",
            "Lifts take you to the roofs. The academic block's roof has a helicopter.",
            "Supply drops come down on red smoke at the start of the second and fourth rounds.",
            "Out of the match? Spectate from the results screen and watch how the others play.",
            "Toggle aim and toggle sprint are in the settings if holding keys tires your hand.",
            "Zip lines are the quick way over the slopes. Press E at either end.",
        };

        /// <summary>Reloads scene buildIndex behind the loading card.</summary>
        public static void Load(int buildIndex, string title)
        {
            Time.timeScale = 1f;
            if (current != null) return;   // already loading
            var go = new GameObject("Loading Screen");
            DontDestroyOnLoad(go);
            current = go.AddComponent<LoadingScreen>();
            current.title = title;
            current.tip = Tips[Random.Range(0, Tips.Length)];
            current.StartCoroutine(current.Run(buildIndex));
        }

        void Awake()
        {
            useGUILayout = false;
        }

        void OnDestroy()
        {
            if (current == this) current = null;
        }

        IEnumerator Run(int buildIndex)
        {
            // Fade out over the old scene.
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                cover = t / 0.3f;
                yield return null;
            }
            cover = 1f;
            yield return null;   // one frame with the card fully up
            Combatant.All.Clear();   // the old scene's fighters go with it

            if (NetSession.Online)
            {
                progress = 0.5f;
                yield return null;
                SceneManager.LoadScene(buildIndex);
            }
            else
            {
                var op = SceneManager.LoadSceneAsync(buildIndex);
                while (op != null && !op.isDone)
                {
                    // LoadSceneAsync reports up to 0.9 while loading and the rest while switching over.
                    progress = Mathf.Clamp01(op.progress / 0.9f) * 0.95f;
                    yield return null;
                }
            }
            progress = 1f;
            // Let the new scene settle (its first frames build materials, the sky and the HUD), then fade in.
            for (int i = 0; i < 4; i++) yield return null;
            for (float t = 0f; t < 0.7f; t += Mathf.Min(Time.unscaledDeltaTime, 0.05f))
            {
                cover = 1f - Mathf.SmoothStep(0f, 1f, t / 0.7f);
                yield return null;
            }
            Destroy(gameObject);
        }

        void OnGUI()
        {
            if (!HudKit.Repaint || cover <= 0.001f) return;
            GUI.depth = -1000;   // over the HUD and the menus
            HudKit.Begin(GameSettings.HudScale);
            float w = HudKit.W, h = HudKit.H;
            HudKit.Alpha = cover;
            HudKit.Fill(0, 0, w, h, new Color(0.02f, 0.025f, 0.03f, 1f));
            // The card shows once the screen is mostly dark.
            HudKit.Alpha = Mathf.Clamp01((cover - 0.6f) / 0.4f);
            if (HudKit.Alpha > 0.01f)
            {
                float x = 80f, y = h - 230f;
                HudKit.Label(x, y, 600f, 22f, "IIM JAMMU  ·  JAGTI CAMPUS", 16f, HudKit.Accent, TextAnchor.MiddleLeft, true);
                HudKit.Label(x - 3f, y + 16f, 900f, 70f, title, 64f, HudKit.Text, TextAnchor.MiddleLeft, true);
                float bw = Mathf.Min(720f, w - 160f);
                HudKit.Panel(x, y + 98f, bw, 6f, new Color(1f, 1f, 1f, 0.12f), 3f);
                HudKit.Panel(x, y + 98f, Mathf.Max(6f, bw * progress), 6f, HudKit.Accent, 3f);
                HudKit.Label(x, y + 108f, bw, 22f, progress >= 1f ? "READY" : "LOADING THE CAMPUS  " + Mathf.RoundToInt(progress * 100f) + "%", 14f, HudKit.TextDim, TextAnchor.MiddleLeft, true);
                HudKit.Label(x, y + 140f, 80f, 22f, "TIP", 14f, HudKit.Accent, TextAnchor.MiddleLeft, true);
                HudKit.Paragraph(x + 40f, y + 140f, bw - 40f, 50f, tip, 18f, HudKit.Text);
            }
            HudKit.Alpha = 1f;
        }
    }
}
