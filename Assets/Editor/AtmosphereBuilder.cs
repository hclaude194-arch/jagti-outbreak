using Jagti.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Jagti.EditorTools
{
    /// <summary>
    /// The look pass (Phase 6 polish): the sun, sky, ambient light and fog; a global post-processing
    /// volume (filmic tone mapping, colour grading, bloom, vignette); screen-space ambient occlusion
    /// on the renderer; a reflection probe; wind for the trees; drifting dust and leaves around the
    /// camera; and the materials for bullet marks, sparks and kicked-up dirt.
    /// The runtime side is VisualQuality (Low / Medium / High, picked in the settings menu), which
    /// switches the costlier parts on and off and sets render scale and shadow distance.
    /// Called from CampusGreyboxBuilder while the scene is built.
    /// </summary>
    public static class AtmosphereBuilder
    {
        const string Folder = ProceduralAssets.Folder + "/Look";

        /// <summary>Raise when the look changes, so JagtiProjectSetup rebuilds the scene on pull.</summary>
        public const int LookVersion = 1;
        public const string BuiltLookKey = "Jagti.BuiltLookVersion";

        // The sun: a clear Jammu afternoon, warm and fairly low, so walls and trees throw long
        // shadows and the terraces read. Brighter than before because the tone mapper rolls off
        // the highlights.
        static readonly Color SunColour = new Color(1f, 0.89f, 0.74f);
        const float SunIntensity = 1.5f;
        static readonly Vector3 SunAngles = new Vector3(36f, -38f, 0f);

        // Hazy horizon: the fog and the bottom of the sky share this colour.
        static readonly Color HazeColour = new Color(0.63f, 0.70f, 0.79f);

        public static void Build(Transform parent, Light sun, Terrain terrain)
        {
            ProceduralAssets.EnsureFolder(Folder);
            Lighting(sun);
            var ao = ConfigureRenderer();

            var root = new GameObject("Look and Atmosphere");
            root.transform.SetParent(parent, false);

            var volumeGo = new GameObject("Post Processing");
            volumeGo.transform.SetParent(root.transform, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = MakeProfile();

            ReflectionProbeAt(root.transform, terrain);

            var windGo = new GameObject("Wind");
            windGo.transform.SetParent(root.transform, false);
            windGo.transform.rotation = Quaternion.LookRotation(new Vector3(0.8f, 0f, 0.6f));
            var zone = windGo.AddComponent<WindZone>();
            zone.mode = WindZoneMode.Directional;
            zone.windMain = 0.5f;
            zone.windTurbulence = 0.4f;
            zone.windPulseMagnitude = 0.5f;
            zone.windPulseFrequency = 0.1f;
            var wind = windGo.AddComponent<WindDriver>();
            wind.direction = new Vector2(0.8f, 0.6f);
            wind.zone = zone;

            var soft = SoftDot();
            var ambientGo = new GameObject("Ambient Particles");
            ambientGo.transform.SetParent(root.transform, false);
            var ambient = ambientGo.AddComponent<AmbientParticles>();
            ambient.dustMaterial = ParticleMaterial("Ambient_Dust", soft, false);
            ambient.leafMaterial = ParticleMaterial("Ambient_Leaf", LeafTexture(), false);

            var quality = root.AddComponent<VisualQuality>();
            quality.volume = volume;
            quality.ambientOcclusion = ao;
            quality.terrain = terrain;
            quality.ambient = ambient;
            quality.wind = wind;
            quality.bulletHole = HoleMaterial();
            quality.spark = ParticleMaterial("Impact_Spark", soft, true);
            if (quality.spark.HasProperty("_BaseColor")) quality.spark.SetColor("_BaseColor", new Color(3.2f, 2.3f, 1.4f, 1f)); // bright enough to bloom
            quality.dirt = ParticleMaterial("Impact_Dirt", soft, false);

            // Grass bends a little more and catches the warm light.
            if (terrain != null)
            {
                var data = terrain.terrainData;
                data.wavingGrassStrength = 0.4f;
                data.wavingGrassAmount = 0.45f;
                data.wavingGrassSpeed = 0.55f;
                data.wavingGrassTint = new Color(0.85f, 0.82f, 0.62f);
            }
        }

        /// <summary>Post-processing on, with cheap anti-aliasing; VisualQuality changes it per tier.</summary>
        public static void ConfigureCamera(Camera cam)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            data.renderShadows = true;
            data.stopNaN = false;
            data.dithering = true;   // hides banding in the sky gradient after grading
        }

        // ------------------------------------------------------------------ light, sky, fog

        static void Lighting(Light sun)
        {
            sun.color = SunColour;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.35f;
            sun.transform.rotation = Quaternion.Euler(SunAngles);

            // Sky above, warm bounce from the sunlit ground at the horizon, earthy green below.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.60f, 0.76f);
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.53f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.23f, 0.17f);
            RenderSettings.ambientIntensity = 1f;

            // Exponential fog: a light haze even at 100 m (depth between near and far buildings)
            // that thickens towards the ridges; the far hills still show at 1 km.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0009f;
            RenderSettings.fogColor = HazeColour;

            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "Jagti_Sky" };
                sky.SetFloat("_SunDisk", 2f);               // high quality sun disc
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 6f);
                sky.SetFloat("_AtmosphereThickness", 0.95f);
                sky.SetColor("_SkyTint", new Color(0.46f, 0.55f, 0.68f));
                sky.SetColor("_GroundColor", new Color(0.52f, 0.52f, 0.48f));
                sky.SetFloat("_Exposure", 1.1f);
                AssetDatabase.CreateAsset(sky, Folder + "/Jagti_Sky.mat");
                RenderSettings.skybox = sky;
            }
            RenderSettings.sun = sun;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        }

        // ------------------------------------------------------------------ post-processing

        static VolumeProfile MakeProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Jagti_Look";

            // Filmic curve: bright sky and sunlit plaster roll off softly instead of clipping.
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;

            // A little more punch and colour than ACES gives on its own, kept gentle so plaster stays white.
            var colour = profile.Add<ColorAdjustments>(true);
            colour.postExposure.value = 0.1f;
            colour.contrast.value = 18f;
            colour.saturation.value = 8f;
            colour.colorFilter.value = Color.white;
            colour.hueShift.value = 0f;

            var white = profile.Add<WhiteBalance>(true);
            white.temperature.value = 3f;
            white.tint.value = 1f;

            // Cool shade, warm sun: the look of a clear afternoon in the hills.
            var split = profile.Add<SplitToning>(true);
            split.shadows.value = new Color(0.47f, 0.50f, 0.54f);
            split.highlights.value = new Color(0.54f, 0.51f, 0.47f);
            split.balance.value = -15f;

            // Only truly bright things glow: the sun, sky near it, muzzle flashes, lamps at night.
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 1.15f;
            bloom.intensity.value = 0.3f;
            bloom.scatter.value = 0.65f;
            bloom.tint.value = new Color(1f, 0.95f, 0.88f);
            bloom.highQualityFiltering.value = false;
            bloom.skipIterations.value = 2;

            var vignette = profile.Add<Vignette>(true);
            vignette.color.value = new Color(0.05f, 0.04f, 0.03f);
            vignette.intensity.value = 0.22f;
            vignette.smoothness.value = 0.45f;
            vignette.rounded.value = false;

            // A hint of lens fringe at the screen edges; High tier only (VisualQuality).
            var fringe = profile.Add<ChromaticAberration>(true);
            fringe.intensity.value = 0.06f;
            fringe.active = false;

            AssetDatabase.CreateAsset(profile, Folder + "/Jagti_Look.asset");
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------ renderer and pipeline

        /// <summary>
        /// Soft shadows and a 2K shadow map on the pipeline, and screen-space ambient occlusion on
        /// its renderer (added once, kept on later builds). The SSAO settings class is internal to
        /// URP, so they are written through the serialized fields; any field a URP version names
        /// differently is skipped. Returns the SSAO feature (or null) for VisualQuality.
        /// </summary>
        static ScriptableRendererFeature ConfigureRenderer()
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
            {
                JagtiProjectSetup.EnsurePipeline(true);
                pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
                if (pipeline == null) return null;
            }

            var so = new SerializedObject(pipeline);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 2048);
            SetBool(so, "m_SupportsHDR", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            pipeline.shadowCascadeCount = 2;
            pipeline.cascade2Split = 0.22f;
            EditorUtility.SetDirty(pipeline);

            var list = so.FindProperty("m_RendererDataList");
            ScriptableRendererData renderer = null;
            if (list != null && list.isArray && list.arraySize > 0)
                renderer = list.GetArrayElementAtIndex(0).objectReferenceValue as ScriptableRendererData;
            if (renderer == null) return null;

            ScriptableRendererFeature ao = null;
            foreach (var f in renderer.rendererFeatures)
                if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") ao = f;

            if (ao == null)
            {
                var type = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
                if (type == null)
                {
                    Debug.LogWarning("[Jagti] This URP has no SSAO feature; skipping ambient occlusion.");
                    return null;
                }
                ao = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                ao.name = "Ambient Occlusion (Jagti)";
                AssetDatabase.AddObjectToAsset(ao, renderer);
                renderer.rendererFeatures.Add(ao);
                AssetDatabase.SaveAssets();

                // The renderer keeps a map of its features' file ids next to the list.
                var rso = new SerializedObject(renderer);
                var map = rso.FindProperty("m_RendererFeatureMap");
                string guid;
                long localId;
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ao, out guid, out localId))
                {
                    map.arraySize = renderer.rendererFeatures.Count;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    rso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Cheap settings for integrated graphics: half resolution, depth only (no normals
            // pass), fewer samples, a soft short radius so corners and the bases of walls darken
            // without dark halos round the trees.
            var fso = new SerializedObject(ao);
            SetBool(fso, "m_Settings.Downsample", true);
            SetBool(fso, "m_Settings.AfterOpaque", false);
            SetEnum(fso, "m_Settings.Source", 0);           // Depth
            SetEnum(fso, "m_Settings.NormalSamples", 1);    // Medium
            SetEnum(fso, "m_Settings.Samples", 1);          // Medium
            SetEnum(fso, "m_Settings.BlurQuality", 1);      // Medium
            SetFloat(fso, "m_Settings.Intensity", 1.1f);
            SetFloat(fso, "m_Settings.DirectLightingStrength", 0.2f);
            SetFloat(fso, "m_Settings.Radius", 0.3f);
            SetFloat(fso, "m_Settings.Falloff", 80f);
            fso.ApplyModifiedPropertiesWithoutUndo();
            ao.SetActive(true);
            EditorUtility.SetDirty(ao);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            return ao;
        }

        static void SetBool(SerializedObject so, string path, bool v)
        {
            var p = so.FindProperty(path);
            if (p != null && p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v;
        }

        static void SetInt(SerializedObject so, string path, int v)
        {
            var p = so.FindProperty(path);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Integer) p.intValue = v;
            else if (p.propertyType == SerializedPropertyType.Enum) p.intValue = v;
        }

        static void SetEnum(SerializedObject so, string path, int index)
        {
            var p = so.FindProperty(path);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Enum && index < p.enumNames.Length) p.enumValueIndex = index;
            else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = index;
        }

        static void SetFloat(SerializedObject so, string path, float v)
        {
            var p = so.FindProperty(path);
            if (p != null && p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
        }

        // ------------------------------------------------------------------ reflections

        /// <summary>
        /// One reflection probe over the whole campus, drawn once when the match loads, so glass,
        /// water and polished floors reflect the real sky and hills rather than nothing.
        /// </summary>
        static void ReflectionProbeAt(Transform parent, Terrain terrain)
        {
            var go = new GameObject("Sky Reflections");
            go.transform.SetParent(parent, false);
            float y = CampusTerrainBuilder.GroundY(terrain, 0f, 0f) + 35f;
            go.transform.position = new Vector3(0f, y, 0f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 128;
            probe.size = new Vector3(3000f, 800f, 3000f);
            probe.boxProjection = false;
            probe.importance = 0;
            probe.hdr = true;
            probe.shadowDistance = 0f;
            probe.farClipPlane = 2000f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        }

        // ------------------------------------------------------------------ materials and textures

        static Material ParticleMaterial(string name, Texture2D tex, bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            var m = new Material(shader) { name = name, enableInstancing = true };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, Folder + "/" + name + ".mat");
            return m;
        }

        /// <summary>A bullet mark: a dark pit with a ring of chipped, lighter plaster round it.</summary>
        static Material HoleMaterial()
        {
            const int size = 64;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Atan2(v, u);
                    float jag = 1f + 0.18f * Mathf.Sin(a * 7f) + 0.1f * Mathf.Sin(a * 13f + 1f);
                    float r = Mathf.Sqrt(u * u + v * v) * jag;
                    float pit = Mathf.Clamp01((0.32f - r) / 0.08f);
                    float chip = Mathf.Clamp01((0.75f - r) / 0.2f) * (1f - pit);
                    float n = ProceduralAssets.Noise(x / (float)size * 8f, y / (float)size * 8f, 8, 77);
                    float shade = Mathf.Lerp(0.55f + 0.2f * n, 0.06f, pit);
                    float alpha = Mathf.Clamp01(pit + chip * (0.35f + 0.3f * n));
                    px[y * size + x] = new Color(shade, shade * 0.96f, shade * 0.9f, alpha);
                }
            var tex = ProceduralAssets.SaveTexture("Tex_Bullet_Hole", size, px, true);

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            var m = new Material(shader) { name = "Bullet_Hole", enableInstancing = true };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.75f, 0.72f, 0.68f, 1f));
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent - 50;   // before smoke and glass
            AssetDatabase.CreateAsset(m, Folder + "/Bullet_Hole.mat");
            return m;
        }

        static Texture2D SoftDot()
        {
            const int size = 32;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            return ProceduralAssets.SaveTexture("Tex_Soft_Dot", size, px, true);
        }

        /// <summary>A small leaf: pointed oval with a midrib, white so the particle colour tints it.</summary>
        static Texture2D LeafTexture()
        {
            const int size = 32;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float half = 0.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - v * v)) * (1f - 0.35f * v);
                    float edge = Mathf.Clamp01((half - Mathf.Abs(u)) * 12f);
                    float rib = Mathf.Abs(u) < 0.05f ? 0.8f : 1f;
                    float stem = (v < -0.85f && Mathf.Abs(u) < 0.06f) ? 1f : 0f;
                    float a = Mathf.Max(edge, stem);
                    px[y * size + x] = new Color(rib, rib, rib, a);
                }
            return ProceduralAssets.SaveTexture("Tex_Leaf", size, px, true);
        }
    }
}
