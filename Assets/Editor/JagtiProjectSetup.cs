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
    /// 2. Builds the grey-box campus scene if it does not exist yet, then opens it.
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

            if (!File.Exists(CampusGreyboxBuilder.ScenePath))
            {
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

        public static void EnsurePipeline(bool force)
        {
            if (!force && GraphicsSettings.defaultRenderPipeline != null) return;

            Directory.CreateDirectory(SettingsFolder);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
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
