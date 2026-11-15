using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Runs once each time the project opens in the Unity editor:
    /// 1. Sets up the Universal Render Pipeline with settings tuned for an Intel Iris Xe laptop.
    /// 2. Builds the campus scene if it does not exist yet or the layout data is newer, then opens it.
    /// Both steps can also be run from the Jagti menu.
    /// </summary>
    [InitializeOnLoad]
    public static class JagtiProjectSetup
    {
        const string SessionKey = "Jagti.SetupRan";
        const string SettingsFolder = "Assets/Settings";
        const string PipelinePath = SettingsFolder + "/Jagti_Laptop_URP.asset";
        const string RendererPath = SettingsFolder + "/Jagti_Laptop_Renderer.asset";

        static JagtiProjectSetup()
        {
            EditorApplication.delayCall += RunOnce;
        }

        static void RunOnce()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EnsurePipeline(false);

            // Build the scene the first time, and again whenever the layout data is newer
            // than the scene this computer last built (for example after pulling a new phase).
            bool stale = EditorPrefs.GetInt(CampusGreyboxBuilder.BuiltVersionKey, 0) < CampusGreyboxBuilder.LayoutVersion();
            if (!File.Exists(CampusGreyboxBuilder.ScenePath) || stale)
            {
                Debug.Log("[Jagti] Campus data changed; rebuilding the scene.");
                CampusGreyboxBuilder.Build();
            }
            else if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                EditorSceneManager.OpenScene(CampusGreyboxBuilder.ScenePath);
            }
        }

        [MenuItem("Jagti/Set Up Laptop Graphics (URP)", priority = 20)]
        public static void EnsurePipelineFromMenu()
        {
            EnsurePipeline(true);
        }

        /// <summary>
        /// Forward+ lifts URP's limit of eight lights per object, which the interiors need: one
        /// interior shell mesh is lit by every room light near the player.
        /// </summary>
        public static void EnsureForwardPlus()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null || rendererData.renderingMode == RenderingMode.ForwardPlus) return;
            rendererData.renderingMode = RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            Debug.Log("[Jagti] Renderer switched to Forward+ for the interior lights.");
        }

        public static void EnsurePipeline(bool force)
        {
            if (!force && GraphicsSettings.defaultRenderPipeline != null) return;

            Directory.CreateDirectory(SettingsFolder);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.renderingMode = RenderingMode.ForwardPlus;
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            // Tuned for integrated graphics: render below native resolution,
            // short and simple shadows, no MSAA. Fog hides the short draw distance.
            pipeline.renderScale = 0.8f;
            pipeline.shadowDistance = 70f;
            pipeline.shadowCascadeCount = 2;
            pipeline.msaaSampleCount = 1;
            pipeline.supportsHDR = true;
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;

            AssetDatabase.SaveAssets();
            Debug.Log("[Jagti] Universal Render Pipeline set up for laptop graphics (" + PipelinePath + ").");
        }
    }
}
