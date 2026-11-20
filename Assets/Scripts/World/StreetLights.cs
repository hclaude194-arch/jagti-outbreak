using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.World
{
    /// <summary>
    /// The campus lamps at night (look pass 2), from the owner's dusk photo: bright white LED heads
    /// on tall single-arm poles, each throwing a pool of light on the road.
    ///
    /// OutdoorBuilder lists every lamp head (street lamps, garden lamps, bollards, floodlights)
    /// with the ground point under it. At run time, with DayCycle's lamp level above zero:
    ///   - the lamp heads' glow material is turned up past the bloom threshold;
    ///   - every lamp gets a soft halo round its head and a faint pool of light on the ground,
    ///     drawn as two fixed particle systems (two draw calls for the whole campus);
    ///   - the lamps nearest the camera get real spot lights (Low 4, Medium 8, High 14), which
    ///     light the road, walls, players and vehicles. They move to the nearest lamps a few
    ///     times a second and fade out with distance, so swapping is not seen.
    /// </summary>
    public class StreetLights : MonoBehaviour
    {
        public enum Kind { Street = 0, Garden = 1, Bollard = 2, Flood = 3 }

        public Vector3[] heads;
        public Vector3[] feet;
        public int[] kinds;
        public Material glow;          // the lamp heads' emissive material
        public Material halo;          // additive soft dot
        public Material pool;          // additive soft dot, laid flat

        static StreetLights active;
        static float night;
        static bool dirty = true;

        /// <summary>0 = lamps off (day), 1 = full night. Called by DayCycle.</summary>
        public static void SetNight(float level)
        {
            level = Mathf.Clamp01(level);
            if (Mathf.Abs(level - night) < 0.005f) return;
            night = level;
            dirty = true;
        }

        static readonly Color LedWhite = new Color(1f, 0.97f, 0.92f);
        static readonly Color WarmWhite = new Color(1f, 0.88f, 0.70f);

        readonly List<Light> lights = new List<Light>();
        ParticleSystem haloSystem, poolSystem;
        ParticleSystem.Particle[] haloParticles, poolParticles;
        float nextPick;
        int[] order;
        float[] dist;
        Color glowBase = Color.white;

        void OnEnable()
        {
            active = this;
            dirty = true;
        }

        void OnDisable()
        {
            if (active == this) active = null;
            if (glow != null && glow.HasProperty("_EmissionColor")) glow.SetColor("_EmissionColor", glowBase * 0.6f);
        }

        void Start()
        {
            if (heads == null) heads = new Vector3[0];
            if (glow != null && glow.HasProperty("_BaseColor")) glowBase = glow.GetColor("_BaseColor");
            order = new int[heads.Length];
            dist = new float[heads.Length];
            haloSystem = MakeSystem("Lamp Halos", halo, ParticleSystemRenderMode.Billboard);
            poolSystem = MakeSystem("Lamp Light Pools", pool, ParticleSystemRenderMode.HorizontalBillboard);
            haloParticles = new ParticleSystem.Particle[heads.Length];
            poolParticles = new ParticleSystem.Particle[heads.Length];
        }

        ParticleSystem MakeSystem(string name, Material mat, ParticleSystemRenderMode mode)
        {
            if (mat == null || heads.Length == 0) return null;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = heads.Length;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1e6f;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = mode == ParticleSystemRenderMode.HorizontalBillboard ? 20f : 0f;
            r.maxParticleSize = 4f;   // a fraction of the screen; keeps a halo from filling it up close
            return ps;
        }

        void Update()
        {
            if (active != this) return;
            if (dirty)
            {
                dirty = false;
                ApplyLevel();
            }
            if (night < 0.05f)
            {
                for (int i = 0; i < lights.Count; i++) lights[i].enabled = false;
                return;
            }
            if (Time.time >= nextPick)
            {
                nextPick = Time.time + 0.25f;
                PickNearest();
            }
        }

        int Budget()
        {
            var level = VisualQuality.Current;
            return level == VisualQuality.Level.Low ? 4 : level == VisualQuality.Level.High ? 14 : 8;
        }

        void ApplyLevel()
        {
            if (glow != null && glow.HasProperty("_EmissionColor"))
                glow.SetColor("_EmissionColor", glowBase * Mathf.Lerp(0.6f, 9f, night));

            bool on = night > 0.05f;
            for (int i = 0; i < heads.Length; i++)
            {
                var kind = KindOf(i);
                float size = kind == Kind.Flood ? 3.2f : kind == Kind.Bollard ? 0.7f : kind == Kind.Garden ? 1.6f : 2.2f;
                float bright = kind == Kind.Bollard ? 0.5f : 0.85f;
                Color c = (kind == Kind.Garden || kind == Kind.Bollard ? WarmWhite : LedWhite) * bright * night;
                c.a = 1f;
                haloParticles[i].position = heads[i];
                haloParticles[i].startSize = size;
                haloParticles[i].startColor = c;
                haloParticles[i].remainingLifetime = 1e6f;
                haloParticles[i].startLifetime = 1e6f;

                float reach = kind == Kind.Flood ? 26f : kind == Kind.Bollard ? 3f : kind == Kind.Garden ? 8f : 13f;
                Color p = (kind == Kind.Garden || kind == Kind.Bollard ? WarmWhite : LedWhite) * 0.22f * night;
                p.a = 1f;
                poolParticles[i].position = feet[i] + Vector3.up * 0.12f;
                poolParticles[i].startSize = reach;
                poolParticles[i].startColor = p;
                poolParticles[i].remainingLifetime = 1e6f;
                poolParticles[i].startLifetime = 1e6f;
            }
            Show(haloSystem, haloParticles, on);
            Show(poolSystem, poolParticles, on);
        }

        static void Show(ParticleSystem ps, ParticleSystem.Particle[] parts, bool on)
        {
            if (ps == null) return;
            if (!on)
            {
                ps.Clear();
                ps.gameObject.SetActive(false);
                return;
            }
            ps.gameObject.SetActive(true);
            ps.SetParticles(parts, parts.Length);
            ps.Play();
            ps.Pause();   // nothing moves: the particles only mark where to draw
        }

        Kind KindOf(int i)
        {
            return kinds != null && i < kinds.Length ? (Kind)kinds[i] : Kind.Street;
        }

        void PickNearest()
        {
            var cam = Camera.main;
            if (cam == null || heads.Length == 0) return;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < heads.Length; i++)
            {
                order[i] = i;
                dist[i] = (heads[i] - eye).sqrMagnitude;
            }
            int n = Mathf.Min(Budget(), heads.Length);
            // Partial selection sort: n is small.
            for (int a = 0; a < n; a++)
            {
                int best = a;
                for (int b = a + 1; b < heads.Length; b++) if (dist[order[b]] < dist[order[best]]) best = b;
                int tmp = order[a]; order[a] = order[best]; order[best] = tmp;
            }
            // The lamp just past the budget sets the fade-out distance.
            float cutoff = n < heads.Length ? Mathf.Sqrt(dist[order[n]]) : 1e4f;

            while (lights.Count < n) lights.Add(MakeLight(lights.Count));
            for (int k = 0; k < lights.Count; k++)
            {
                var light = lights[k];
                if (k >= n) { light.enabled = false; continue; }
                int i = order[k];
                var kind = KindOf(i);
                float d = Mathf.Sqrt(dist[i]);
                float fade = Mathf.Clamp01((cutoff - d) / 12f);
                light.enabled = fade > 0.01f;
                light.transform.position = heads[i] - Vector3.up * 0.15f;
                light.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                switch (kind)
                {
                    case Kind.Flood:
                        light.type = LightType.Spot; light.range = 48f; light.spotAngle = 110f; light.intensity = 90f; light.color = LedWhite; break;
                    case Kind.Garden:
                        light.type = LightType.Point; light.range = 9f; light.intensity = 5f; light.color = WarmWhite; break;
                    case Kind.Bollard:
                        light.type = LightType.Point; light.range = 4.5f; light.intensity = 1.5f; light.color = WarmWhite; break;
                    default:
                        light.type = LightType.Spot; light.range = 20f; light.spotAngle = 130f; light.intensity = 26f; light.color = LedWhite; break;
                }
                light.intensity *= fade * night;
            }
        }

        Light MakeLight(int k)
        {
            var go = new GameObject("Lamp Light " + k);
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.innerSpotAngle = 60f;
            light.enabled = false;
            return light;
        }
    }
}
