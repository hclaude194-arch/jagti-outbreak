using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// Life in the air around the camera: specks of dust that hang and drift (indoors and out), and
    /// a few leaves that tumble down outdoors, carried by the WindZone. Both emitters follow the
    /// camera while their particles stay put in the world, so the player walks through them.
    /// Indoors (something solid overhead) the leaves stop. Very few particles; Low quality turns
    /// the leaves off and thins the dust.
    /// </summary>
    public class AmbientParticles : MonoBehaviour
    {
        public Material dustMaterial;
        public Material leafMaterial;

        ParticleSystem dust, leaves;
        Transform viewer;
        float checkTimer;
        bool indoors;
        int level = 1;

        void Start()
        {
            if (dustMaterial != null) dust = MakeDust();
            if (leafMaterial != null) leaves = MakeLeaves();
            Refresh();
        }

        public void SetLevel(int quality)
        {
            level = quality;
            Refresh();
        }

        void LateUpdate()
        {
            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                viewer = cam.transform;
            }
            transform.position = viewer.position;

            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = 0.5f;
            bool under = Physics.Raycast(viewer.position + Vector3.up * 0.3f, Vector3.up, 25f, ~0, QueryTriggerInteraction.Ignore);
            // High above the ground (the sky bus, gliding down) there is nothing to see up close.
            bool aloft = !Physics.Raycast(viewer.position, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
            bool inside = under || aloft;
            if (inside != indoors)
            {
                indoors = inside;
                Refresh();
            }
        }

        void Refresh()
        {
            if (dust != null)
            {
                var em = dust.emission;
                em.rateOverTime = level == 0 ? 5f : level == 2 ? 18f : 12f;
            }
            if (leaves != null)
            {
                var em = leaves.emission;
                em.rateOverTime = (level == 0 || indoors) ? 0f : level == 2 ? 3.5f : 2.2f;
            }
        }

        ParticleSystem MakeSystem(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // Fine dust in a 16 m box round the head, floating and slowly turning over.
        ParticleSystem MakeDust()
        {
            var ps = MakeSystem("Dust Motes", dustMaterial);
            var main = ps.main;
            main.duration = 8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new Color(1f, 0.95f, 0.85f, 0.55f);
            main.maxParticles = 160;
            main.gravityModifier = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 5f, 16f);
            shape.position = new Vector3(0f, 0.5f, 0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.1f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;

            ps.Play();
            return ps;
        }

        // Leaves let go above and around the player and spin down on the wind.
        ParticleSystem MakeLeaves()
        {
            var ps = MakeSystem("Falling Leaves", leafMaterial);
            var main = ps.main;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.035f;
            main.maxParticles = 60;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.45f, 0.20f), new Color(0.38f, 0.48f, 0.18f));

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(36f, 2f, 36f);
            shape.position = new Vector3(0f, 9f, 0f);

            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var forces = ps.externalForces;
            forces.enabled = true;
            forces.multiplier = 0.6f;

            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 2.2f;
            drag.dampen = 0.15f;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;

            ps.Play();
            return ps;
        }
    }
}
