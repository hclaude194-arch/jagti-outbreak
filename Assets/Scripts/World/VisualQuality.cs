using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Jagti.World
{
    /// <summary>
    /// Graphics quality tiers for the look pass, picked in the settings menu and kept in PlayerPrefs.
    ///   Low:    70 % render scale, short shadows, no ambient occlusion or bloom, no falling leaves,
    ///           thinner grass. For when Medium drops under 30 fps.
    ///   Medium: the default for an Iris Xe laptop: 80 % scale, half-resolution SSAO, cheap bloom, FXAA.
    ///   High:   full scale, longer shadows, SMAA, a touch of lens fringe, denser and further grass.
    /// Tone mapping, colour grading and vignette stay on in every tier: they run in one pass that
    /// costs about the same with or without them.
    /// Also hands the impact materials to ImpactMarks when the scene loads.
    /// </summary>
    public class VisualQuality : MonoBehaviour
    {
        public enum Level { Low = 0, Medium = 1, High = 2 }

        public static readonly string[] Names = { "Low", "Medium", "High" };
        const string Key = "Jagti.Settings.Graphics";

        static bool loaded;
        static Level current = Level.Medium;
        static int version;

        /// <summary>The chosen tier; setting it saves and re-applies at once.</summary>
        public static Level Current
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    current = (Level)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Level.Medium), 0, 2);
                }
                return current;
            }
            set
            {
                var level = (Level)Mathf.Clamp((int)value, 0, 2);
                if (level == Current) return;
                current = level;
                PlayerPrefs.SetInt(Key, (int)level);
                PlayerPrefs.Save();
                version++;
            }
        }

        public Volume volume;
        public ScriptableRendererFeature ambientOcclusion;
        public Terrain terrain;
        public AmbientParticles ambient;
        public WindDriver wind;
        public Material bulletHole;
        public Material spark;
        public Material dirt;

        int applied = -1;
        Camera lastCamera;

        void Awake()
        {
            ImpactMarks.Setup(bulletHole, spark, dirt);
        }

        void Update()
        {
            var cam = Camera.main;
            if (applied != version || cam != lastCamera) Apply(cam);
        }

        void OnDestroy()
        {
            // Leave the shared renderer with SSAO on, as the editor expects it.
            if (ambientOcclusion != null) ambientOcclusion.SetActive(true);
        }

        void Apply(Camera cam)
        {
            applied = version;
            lastCamera = cam;
            var level = Current;
            bool low = level == Level.Low, high = level == Level.High;

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                pipeline.renderScale = low ? 0.7f : high ? 1f : 0.8f;
                pipeline.shadowDistance = low ? 45f : high ? 110f : 75f;
                pipeline.shadowCascadeCount = low ? 1 : 2;
            }
            if (ambientOcclusion != null) ambientOcclusion.SetActive(!low);

            if (volume != null && volume.profile != null)
            {
                Bloom bloom;
                if (volume.profile.TryGet(out bloom)) bloom.active = !low;
                ChromaticAberration fringe;
                if (volume.profile.TryGet(out fringe)) fringe.active = high;
            }

            if (cam != null)
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = low ? AntialiasingMode.None
                                  : high ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                                  : AntialiasingMode.FastApproximateAntialiasing;
            }

            if (terrain != null)
            {
                terrain.detailObjectDistance = low ? 30f : high ? 65f : 45f;
                terrain.detailObjectDensity = low ? 0.5f : high ? 1f : 0.8f;
            }
            QualitySettings.lodBias = low ? 0.7f : high ? 1.5f : 1f;
            if (ambient != null) ambient.SetLevel((int)level);
        }
    }
}
