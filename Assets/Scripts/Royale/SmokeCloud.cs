using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A smoke screen from a smoke grenade: it billows out to about 6 m across in three seconds,
    /// hangs for twenty, then thins away. Nobody can see through it: the bots' line of sight checks
    /// it (Ballistics.LineOfSight), and the particles hide it from the player.
    /// </summary>
    public class SmokeCloud : MonoBehaviour
    {
        public static readonly List<SmokeCloud> Active = new List<SmokeCloud>();

        public Material material;
        public float maxRadius = 6.5f;
        public float life = 22f;

        public float Radius { get; private set; }

        float t;
        ParticleSystem ps;

        void OnEnable() { Active.Add(this); }
        void OnDisable() { Active.Remove(this); }

        void Start()
        {
            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(3f, 5.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.8f, 0.8f, 0.78f, 0.75f);
            main.gravityModifier = -0.004f;
            main.maxParticles = 70;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 12f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 1.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
            var r = GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sortMode = ParticleSystemSortMode.Distance;
            ps.Play();
            hiss = Jagti.Audio.SoundLoop.Create(gameObject, "smoke_hiss");
            if (hiss != null) hiss.VolumeEase = 0.5f;
        }

        Jagti.Audio.SoundLoop hiss;

        void Update()
        {
            t += Time.deltaTime;
            float grow = Mathf.Clamp01(t / 3f);
            float fade = Mathf.Clamp01((life - t) / 4f);
            Radius = maxRadius * Mathf.Sqrt(grow) * fade;
            // The canister hisses hard at first, then dies away as the cloud stops growing.
            if (hiss != null) hiss.Volume = t < 0.3f ? 1f : Mathf.Clamp01(1f - (t - 2f) / 10f);
            if (ps != null)
            {
                var shape = ps.shape;
                shape.radius = Mathf.Max(0.5f, Radius * 0.55f);
                if (t > life - 6f && ps.isEmitting) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (t > life + 6f) Destroy(gameObject);
        }

        /// <summary>True if the segment from a to b passes through the thick of any smoke.</summary>
        public static bool Blocks(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var c = Active[i];
                float r = c.Radius * 0.8f;
                if (r < 1f) continue;
                Vector3 centre = c.transform.position + Vector3.up * 1.2f;
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(centre - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                if ((a + ab * t - centre).sqrMagnitude < r * r) return true;
            }
            return false;
        }
    }
}
