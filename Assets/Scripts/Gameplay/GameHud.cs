using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jagti.Gameplay
{
    /// <summary>
    /// The survival HUD (IMGUI, so it needs no Canvas setup): health, stamina, torch battery, weapon and ammo,
    /// supplies, crosshair with hit marker, search prompt, pickup feed, damage flash, pause menu (Esc)
    /// and the death screen.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        public Health playerHealth;
        public PlayerVitals vitals;
        public Inventory inventory;
        public WeaponController weapons;
        public Interactor interactor;
        public ZombieSpawner spawner;

        readonly List<string> feed = new List<string>();
        readonly List<float> feedTimes = new List<float>();
        GUIStyle label, big, small, right;
        float deathFade;

        void Awake()
        {
            GameState.Reset();
            GameState.HudActive = true;
        }

        void OnEnable()
        {
            if (inventory != null) inventory.Message += Push;
        }

        void OnDisable()
        {
            if (inventory != null) inventory.Message -= Push;
            GameState.HudActive = false;
        }

        void Push(string text)
        {
            feed.Add(text);
            feedTimes.Add(Time.unscaledTime);
            if (feed.Count > 5)
            {
                feed.RemoveAt(0);
                feedTimes.RemoveAt(0);
            }
        }

        void Update()
        {
            if (GameState.PlayerDead)
            {
                deathFade = Mathf.MoveTowards(deathFade, 1f, Time.unscaledDeltaTime * 0.5f);
                if (deathFade >= 0.6f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) Restart();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) SetPaused(!GameState.Paused);
            for (int i = feedTimes.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime - feedTimes[i] > 5f)
                {
                    feed.RemoveAt(i);
                    feedTimes.RemoveAt(i);
                }
            }
        }

        void SetPaused(bool paused)
        {
            GameState.Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            GameState.SetCursorLocked(!paused);
        }

        void Restart()
        {
            AudioListener.pause = false;
            GameState.Reset();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = 13 };
            right = new GUIStyle(label) { alignment = TextAnchor.UpperRight };
            big = new GUIStyle(label) { fontSize = 40, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        static void Rect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Bar(float x, float y, float w, float h, float fraction, Color fill, string text, GUIStyle style)
        {
            Rect(new Rect(x - 2, y - 2, w + 4, h + 4), new Color(0f, 0f, 0f, 0.55f));
            Rect(new Rect(x, y, w * Mathf.Clamp01(fraction), h), fill);
            GUI.Label(new Rect(x + 6, y - 1, w, h + 4), text, style);
        }

        void OnGUI()
        {
            Styles();
            float W = Screen.width, H = Screen.height;

            // Damage flash: red at the screen edges, stronger at low health.
            float hurt = vitals != null ? vitals.DamageFlash : 0f;
            float low = playerHealth != null && !playerHealth.IsDead ? Mathf.Clamp01(1f - playerHealth.Fraction / 0.3f) * (0.25f + 0.1f * Mathf.Sin(Time.time * 4f)) : 0f;
            float red = Mathf.Max(hurt * 0.45f, low);
            if (red > 0.01f)
            {
                float t = 60f;
                Rect(new Rect(0, 0, W, t), new Color(0.6f, 0f, 0f, red));
                Rect(new Rect(0, H - t, W, t), new Color(0.6f, 0f, 0f, red));
                Rect(new Rect(0, t, t, H - 2 * t), new Color(0.6f, 0f, 0f, red));
                Rect(new Rect(W - t, t, t, H - 2 * t), new Color(0.6f, 0f, 0f, red));
                Rect(new Rect(0, 0, W, H), new Color(0.5f, 0f, 0f, red * 0.25f));
            }

            if (GameState.PlayerDead)
            {
                DrawDeath(W, H);
                return;
            }

            // Crosshair and hit marker.
            float cx = W / 2f, cy = H / 2f;
            Color ch = new Color(1f, 1f, 1f, 0.8f);
            Rect(new Rect(cx - 1, cy - 9, 2, 6), ch);
            Rect(new Rect(cx - 1, cy + 3, 2, 6), ch);
            Rect(new Rect(cx - 9, cy - 1, 6, 2), ch);
            Rect(new Rect(cx + 3, cy - 1, 6, 2), ch);
            if (weapons != null && weapons.HitMarker > 0f)
            {
                Color hm = weapons.LastHitWasHead ? new Color(1f, 0.25f, 0.2f, weapons.HitMarker) : new Color(1f, 1f, 1f, weapons.HitMarker);
                for (int i = 4; i < 10; i++)
                {
                    Rect(new Rect(cx - i - 1, cy - i - 1, 2, 2), hm);
                    Rect(new Rect(cx + i - 1, cy - i - 1, 2, 2), hm);
                    Rect(new Rect(cx - i - 1, cy + i - 1, 2, 2), hm);
                    Rect(new Rect(cx + i - 1, cy + i - 1, 2, 2), hm);
                }
            }

            // Bottom left: health, stamina, torch.
            float x = 20f, y = H - 92f;
            if (playerHealth != null)
            {
                string healing = vitals != null && vitals.HealingLeft > 0f ? "  (healing)" : "";
                Bar(x, y, 260f, 20f, playerHealth.Fraction, new Color(0.75f, 0.12f, 0.1f, 0.9f), "Health " + Mathf.CeilToInt(playerHealth.current) + healing, small);
            }
            if (vitals != null)
            {
                Bar(x, y + 28f, 260f, 14f, vitals.StaminaFraction, new Color(0.85f, 0.7f, 0.15f, 0.9f), "", small);
                Bar(x, y + 50f, 260f, 14f, vitals.Battery, new Color(0.6f, 0.75f, 0.95f, 0.9f), "", small);
                GUI.Label(new Rect(x + 266f, y + 24f, 120f, 20f), "Stamina", small);
                GUI.Label(new Rect(x + 266f, y + 46f, 120f, 20f), "Torch (F)", small);
            }

            // Bottom right: weapon, ammo, supplies.
            if (weapons != null && inventory != null)
            {
                var w = weapons.Current;
                string ammo = w.melee ? "" : "   <b>" + w.loaded + "</b> / " + inventory.Count(w.ammo);
                if (weapons.Reloading) ammo += "   reloading…";
                Rect(new Rect(W - 330f, H - 96f, 316f, 82f), new Color(0f, 0f, 0f, 0.5f));
                GUI.Label(new Rect(W - 320f, H - 92f, 300f, 26f), "<b>" + w.name + "</b>" + ammo, label);
                GUI.Label(new Rect(W - 320f, H - 66f, 300f, 22f),
                    "Bandages " + inventory.bandages + "   Medkits " + inventory.medkits + "   Batteries " + inventory.batteries, small);
                GUI.Label(new Rect(W - 320f, H - 44f, 300f, 22f),
                    "1 bat · 2 pistol" + (inventory.hasShotgun ? " · 3 shotgun" : "") + " · R reload · Q heal", small);
            }

            // Top right: survival stats.
            int secs = Mathf.FloorToInt(GameState.TimeSurvived);
            string stats = "Survived " + (secs / 60) + ":" + (secs % 60).ToString("00") + "   Kills " + GameState.Kills;
            if (spawner != null) stats += "\n<size=12>Zombies nearby " + spawner.ActiveCount + "</size>";
            GUI.Label(new Rect(W - 320f, 12f, 306f, 44f), stats, right);

            // Pickup feed under the stats.
            for (int i = 0; i < feed.Count; i++)
            {
                float age = Time.unscaledTime - feedTimes[i];
                var c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(5f - age));
                GUI.Label(new Rect(W - 520f, 58f + i * 22f, 506f, 22f), feed[i], right);
                GUI.color = c;
            }

            // Search prompt.
            if (interactor != null && interactor.Target != null)
            {
                GUI.Label(new Rect(cx - 160f, cy + 40f, 320f, 24f), "<b>" + interactor.Target.Prompt + "</b>", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
                if (interactor.Progress > 0f) Bar(cx - 100f, cy + 68f, 200f, 8f, interactor.Progress, new Color(1f, 1f, 1f, 0.85f), "", small);
            }

            if (GameState.Paused) DrawPause(W, H);
        }

        void DrawPause(float W, float H)
        {
            Rect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(0, H * 0.25f, W, 60f), "Paused", big);
            float bx = W / 2f - 110f, by = H * 0.25f + 90f;
            if (GUI.Button(new Rect(bx, by, 220f, 40f), "Resume (Esc)")) SetPaused(false);
            if (GUI.Button(new Rect(bx, by + 50f, 220f, 40f), "Restart")) { SetPaused(false); Restart(); }
            if (GUI.Button(new Rect(bx, by + 100f, 220f, 40f), "Quit")) Application.Quit();
            GUI.Label(new Rect(W / 2f - 260f, by + 160f, 520f, 120f),
                "WASD move · Shift sprint · Space jump · F torch\nLeft click attack · R reload · 1/2/3 or wheel switch weapon\n" +
                "E (hold) search crates · Q bandage or medkit\nGunshots carry far: every zombie nearby will come.", new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }

        void DrawDeath(float W, float H)
        {
            Rect(new Rect(0, 0, W, H), new Color(0.15f, 0f, 0f, deathFade * 0.75f));
            if (deathFade < 0.3f) return;
            GUI.Label(new Rect(0, H * 0.32f, W, 60f), "You were bitten", big);
            int secs = Mathf.FloorToInt(GameState.TimeSurvived);
            GUI.Label(new Rect(0, H * 0.32f + 64f, W, 30f), "Survived " + (secs / 60) + " min " + (secs % 60) + " s · " + GameState.Kills + " zombies put down",
                new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            if (deathFade >= 0.6f)
            {
                if (GUI.Button(new Rect(W / 2f - 110f, H * 0.32f + 110f, 220f, 40f), "Try again (Enter)")) Restart();
            }
        }
    }
}
