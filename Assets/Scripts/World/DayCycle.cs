using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Jagti.World
{
    /// <summary>
    /// Time of day (look pass 2), picked in the settings menu and kept in PlayerPrefs:
    ///   Day:      the clear afternoon of the first look pass.
    ///   Sunset:   from the owner's photo off the academic porch (2026-11-20): a low orange sun
    ///             under streaky cirrus, pale blue overhead, hazy layered hills, long warm shadows.
    ///   Night:    from the owner's dusk photo: violet sky going pink at the horizon, dark hills,
    ///             a cool moon, and the street lamps on (bright white LED heads, pools of light).
    ///   Changing: each match starts at sunset, the sun sinks, and night falls over CycleMinutes.
    /// One Look holds every setting that changes (sun, ambient light, fog, the sky material's
    /// colours, cloud cover, stars, lamps, exposure); the moods are blended between Looks, so the
    /// changing sky is a smooth fade. Purely visual: nothing in the match depends on it, so online
    /// players may each pick their own.
    /// </summary>
    public class DayCycle : MonoBehaviour
    {
        public enum Mood { Day = 0, Sunset = 1, Night = 2, Changing = 3 }

        public static readonly string[] Names = { "Day", "Sunset", "Night", "Changing" };
        const string Key = "Jagti.Settings.TimeOfDay";

        /// <summary>Minutes from sunset to full night in the Changing mood.</summary>
        public const float CycleMinutes = 14f;

        static bool loaded;
        static Mood current = Mood.Changing;
        static int version;

        public static Mood Current
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    current = (Mood)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Mood.Changing), 0, Names.Length - 1);
                }
                return current;
            }
            set
            {
                var mood = (Mood)Mathf.Clamp((int)value, 0, Names.Length - 1);
                if (mood == Current) return;
                current = mood;
                PlayerPrefs.SetInt(Key, (int)mood);
                PlayerPrefs.Save();
                version++;
            }
        }

        /// <summary>Everything that changes with the time of day.</summary>
        public struct Look
        {
            public Vector2 light;          // the directional light: elevation and yaw (degrees, Unity Euler x, y)
            public Color lightColour;
            public float lightIntensity;
            public Vector2 skySun;         // where the sky draws the sun (elevation, yaw), may be below the horizon
            public Color ambientSky, ambientEquator, ambientGround;
            public Color fog;
            public float fogDensity;
            public Color zenith, horizon, below, sunGlow, sunDisc, cloudLit, cloudShade, hills;
            public float gradient, glowHeight, cloudCover, cloudOpacity, stars, lamps, exposure;

            public static Look Lerp(Look a, Look b, float t)
            {
                var o = new Look();
                o.light = Vector2.Lerp(a.light, b.light, t);
                o.lightColour = Color.Lerp(a.lightColour, b.lightColour, t);
                o.lightIntensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t);
                o.skySun = Vector2.Lerp(a.skySun, b.skySun, t);
                o.ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t);
                o.ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t);
                o.ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t);
                o.fog = Color.Lerp(a.fog, b.fog, t);
                o.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);
                o.zenith = Color.Lerp(a.zenith, b.zenith, t);
                o.horizon = Color.Lerp(a.horizon, b.horizon, t);
                o.below = Color.Lerp(a.below, b.below, t);
                o.sunGlow = Color.Lerp(a.sunGlow, b.sunGlow, t);
                o.sunDisc = Color.Lerp(a.sunDisc, b.sunDisc, t);
                o.cloudLit = Color.Lerp(a.cloudLit, b.cloudLit, t);
                o.cloudShade = Color.Lerp(a.cloudShade, b.cloudShade, t);
                o.hills = Color.Lerp(a.hills, b.hills, t);
                o.gradient = Mathf.Lerp(a.gradient, b.gradient, t);
                o.glowHeight = Mathf.Lerp(a.glowHeight, b.glowHeight, t);
                o.cloudCover = Mathf.Lerp(a.cloudCover, b.cloudCover, t);
                o.cloudOpacity = Mathf.Lerp(a.cloudOpacity, b.cloudOpacity, t);
                o.stars = Mathf.Lerp(a.stars, b.stars, t);
                o.lamps = Mathf.Lerp(a.lamps, b.lamps, t);
                o.exposure = Mathf.Lerp(a.exposure, b.exposure, t);
                return o;
            }
        }

        // Colours are linear (as the shader and the lights use them). tools/blender/preview_sky.py
        // repeats these numbers for its mock-ups, so change the two together.

        /// <summary>The clear afternoon of the first look pass.</summary>
        public static Look DayLook()
        {
            var l = new Look();
            l.light = new Vector2(36f, -38f);
            l.lightColour = new Color(1f, 0.89f, 0.74f);
            l.lightIntensity = 1.5f;
            l.skySun = l.light;
            l.ambientSky = new Color(0.50f, 0.60f, 0.76f);
            l.ambientEquator = new Color(0.56f, 0.53f, 0.46f);
            l.ambientGround = new Color(0.24f, 0.23f, 0.17f);
            l.fog = new Color(0.63f, 0.70f, 0.79f);
            l.fogDensity = 0.0009f;
            l.zenith = new Color(0.05f, 0.17f, 0.50f);
            l.horizon = new Color(0.46f, 0.56f, 0.70f);
            l.below = new Color(0.40f, 0.40f, 0.38f);
            l.sunGlow = new Color(0.22f, 0.18f, 0.10f);
            l.sunDisc = new Color(9f, 8f, 6.5f);
            l.cloudLit = new Color(0.95f, 0.93f, 0.90f);
            l.cloudShade = new Color(0.52f, 0.56f, 0.64f);
            l.hills = new Color(0.50f, 0.57f, 0.66f);
            l.gradient = 0.55f;
            l.glowHeight = 5f;
            l.cloudCover = 0.42f;
            l.cloudOpacity = 0.85f;
            l.stars = 0f;
            l.lamps = 0f;
            l.exposure = 0.1f;
            return l;
        }

        /// <summary>The owner's sunset photo. elevation: the sun's height in degrees (it sinks in Changing).</summary>
        public static Look SunsetLook(float elevation)
        {
            var l = new Look();
            l.light = new Vector2(Mathf.Max(elevation, 4f), 150f);   // setting beyond the porch drive, as in the photo
            l.lightColour = new Color(1f, 0.60f, 0.32f);
            l.lightIntensity = 1.35f;
            l.skySun = new Vector2(elevation, 150f);
            l.ambientSky = new Color(0.40f, 0.42f, 0.55f);
            l.ambientEquator = new Color(0.66f, 0.47f, 0.34f);
            l.ambientGround = new Color(0.20f, 0.16f, 0.12f);
            l.fog = new Color(0.82f, 0.60f, 0.44f);
            l.fogDensity = 0.0012f;
            l.zenith = new Color(0.12f, 0.20f, 0.38f);
            l.horizon = new Color(0.70f, 0.33f, 0.10f);
            l.below = new Color(0.36f, 0.28f, 0.24f);
            l.sunGlow = new Color(0.75f, 0.26f, 0.04f);
            l.sunDisc = new Color(22f, 13f, 4f);
            l.cloudLit = new Color(0.62f, 0.36f, 0.22f);
            l.cloudShade = new Color(0.27f, 0.27f, 0.33f);
            l.hills = new Color(0.22f, 0.16f, 0.17f);
            l.gradient = 0.35f;
            l.glowHeight = 6f;
            l.cloudCover = 0.55f;
            l.cloudOpacity = 0.8f;
            l.stars = 0f;
            l.lamps = 0.1f;
            l.exposure = -0.1f;
            return l;
        }

        /// <summary>The owner's dusk photo: violet sky, pink horizon, moonlight, lamps on.</summary>
        public static Look NightLook()
        {
            var l = new Look();
            l.light = new Vector2(38f, -30f);                // the moon, opposite the set sun
            l.lightColour = new Color(0.55f, 0.62f, 0.95f);
            l.lightIntensity = 0.22f;
            l.skySun = new Vector2(-9f, 150f);              // the set sun still tints that side of the sky
            l.ambientSky = new Color(0.10f, 0.10f, 0.22f);
            l.ambientEquator = new Color(0.16f, 0.10f, 0.16f);
            l.ambientGround = new Color(0.03f, 0.03f, 0.045f);
            l.fog = new Color(0.20f, 0.12f, 0.22f);
            l.fogDensity = 0.0013f;
            l.zenith = new Color(0.03f, 0.03f, 0.11f);
            l.horizon = new Color(0.24f, 0.075f, 0.13f);
            l.below = new Color(0.04f, 0.04f, 0.06f);
            l.sunGlow = new Color(0.10f, 0.02f, 0.03f);
            l.sunDisc = Color.black;
            l.cloudLit = new Color(0.20f, 0.10f, 0.16f);
            l.cloudShade = new Color(0.05f, 0.04f, 0.09f);
            l.hills = new Color(0.012f, 0.012f, 0.022f);
            l.gradient = 0.32f;
            l.glowHeight = 5f;
            l.cloudCover = 0.36f;
            l.cloudOpacity = 0.7f;
            l.stars = 0.9f;
            l.lamps = 1f;
            l.exposure = 0.3f;
            return l;
        }

        /// <summary>The look for a mood; t is 0-1 through the Changing cycle.</summary>
        public static Look LookFor(Mood mood, float t)
        {
            switch (mood)
            {
                case Mood.Day: return DayLook();
                case Mood.Sunset: return SunsetLook(7f);
                case Mood.Night: return NightLook();
            }
            // Changing: the sun sinks from 12 to 2 degrees over the first 45 %, then dusk deepens
            // into night by 85 %.
            if (t < 0.45f) return SunsetLook(Mathf.Lerp(12f, 2f, t / 0.45f));
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.85f, t));
            return Look.Lerp(SunsetLook(2f), NightLook(), k);
        }

        public Light sun;
        public Material sky;
        public Volume volume;
        public ReflectionProbe probe;

        int applied = -1;
        float startTime;
        float nextChange;
        float lastProbe = -100f;
        float probeLamps = -1f;

        void Start()
        {
            startTime = Time.time;
            if (sky != null) RenderSettings.skybox = sky;
        }

        void Update()
        {
            var mood = Current;
            if (applied == version && (mood != Mood.Changing || Time.time < nextChange)) return;
            applied = version;
            nextChange = Time.time + 0.5f;    // the changing sky moves slowly; twice a second is plenty
            float t = Mathf.Clamp01((Time.time - startTime) / (CycleMinutes * 60f));
            Apply(LookFor(mood, t));
        }

        void Apply(Look l)
        {
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(l.light.x, l.light.y, 0f);
                sun.color = l.lightColour;
                sun.intensity = l.lightIntensity;
                sun.shadowStrength = Mathf.Lerp(0.6f, 0.92f, Mathf.Clamp01(l.lightIntensity));
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = l.ambientSky;
            RenderSettings.ambientEquatorColor = l.ambientEquator;
            RenderSettings.ambientGroundColor = l.ambientGround;
            RenderSettings.fogColor = l.fog;
            RenderSettings.fogDensity = l.fogDensity;

            if (sky != null)
            {
                Vector3 toSun = Quaternion.Euler(l.skySun.x, l.skySun.y, 0f) * Vector3.back;
                sky.SetVector("_SunDir", new Vector4(toSun.x, toSun.y, toSun.z, 0f));
                sky.SetColor("_ZenithColor", l.zenith);
                sky.SetColor("_HorizonColor", l.horizon);
                sky.SetColor("_GroundColor", l.below);
                sky.SetColor("_SunGlowColor", l.sunGlow);
                sky.SetColor("_SunColor", l.sunDisc);
                sky.SetColor("_CloudLit", l.cloudLit);
                sky.SetColor("_CloudShade", l.cloudShade);
                sky.SetColor("_HillColor", l.hills);
                sky.SetFloat("_Gradient", l.gradient);
                sky.SetFloat("_GlowHeight", l.glowHeight);
                sky.SetFloat("_CloudCover", l.cloudCover);
                sky.SetFloat("_CloudOpacity", l.cloudOpacity);
                sky.SetFloat("_Stars", l.stars);
            }

            if (volume != null && volume.profile != null)
            {
                ColorAdjustments colour;
                if (volume.profile.TryGet(out colour)) colour.postExposure.value = l.exposure;
            }

            StreetLights.SetNight(l.lamps);

            // Glass and water reflect the sky, so redraw the probe when the sky has moved on.
            if (probe != null && (Time.time - lastProbe > 30f || Mathf.Abs(l.lamps - probeLamps) > 0.3f || lastProbe < 0f))
            {
                lastProbe = Time.time;
                probeLamps = l.lamps;
                probe.RenderProbe();
            }
        }
    }
}
