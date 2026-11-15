using Jagti.Gameplay;
using Jagti.Player;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A thrown fragmentation grenade: bounces, then bursts after its fuse. Damage falls off with
    /// distance and needs a clear line from the blast (walls protect). Armour soaks part of it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grenade : MonoBehaviour
    {
        public Combatant thrower;
        public float fuse = 3.2f;
        public float radius = 7.5f;
        public float maxDamage = 115f;
        public Material blastMaterial;

        float t;
        float bounceCooldown;

        void Start()
        {
            if (thrower != null)
            {
                var mine = GetComponent<Collider>();
                foreach (var c in thrower.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, c);
            }
        }

        void Update()
        {
            t += Time.deltaTime;
            bounceCooldown -= Time.deltaTime;
            if (t >= fuse) Explode();
        }

        void OnCollisionEnter(Collision c)
        {
            if (bounceCooldown > 0f || c.relativeVelocity.magnitude < 2f) return;
            bounceCooldown = 0.15f;
            Ballistics.SoundAt(transform.position, "clink", 0.6f);
        }

        void Explode()
        {
            Vector3 p = transform.position + Vector3.up * 0.2f;
            foreach (var c in Combatant.All.ToArray())
            {
                if (c == null || !c.Alive || !c.Landed) continue;
                float d = (c.AimPoint - p).magnitude;
                if (d > radius) continue;
                if (!Ballistics.LineOfSight(p, c.AimPoint, null, c)) continue;
                float dmg = maxDamage * Mathf.Clamp01(1f - d / radius) + 10f;
                bool wasAlive = c.Alive;
                c.ReceiveHit(dmg, false, thrower, "grenade", p);
                if (thrower != null && thrower.isPlayer && c != thrower && Ballistics.PlayerHit != null)
                    Ballistics.PlayerHit(false, wasAlive && !c.Alive);
            }
            NoiseEvents.Emit(p, 140f);
            Ballistics.SoundAt(p, "explosion", 1f);
            Blast(p);

            var cam = Camera.main;
            if (cam != null)
            {
                float d = (cam.transform.position - p).magnitude;
                var fpc = cam.GetComponentInParent<FirstPersonController>();
                if (fpc != null && d < 30f) fpc.Shake(Mathf.Lerp(1f, 0.1f, d / 30f));
            }
            Destroy(gameObject);
        }

        void Blast(Vector3 p)
        {
            var go = new GameObject("Blast");
            go.transform.position = p;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 18f;
            light.intensity = 8f;
            light.color = new Color(1f, 0.7f, 0.35f);
            light.shadows = LightShadows.None;
            if (blastMaterial != null)
            {
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = false;
                main.duration = 0.5f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
                main.startColor = new Color(0.35f, 0.32f, 0.28f, 0.9f);
                main.gravityModifier = -0.05f;
                main.maxParticles = 40;
                var em = ps.emission;
                em.rateOverTime = 0f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.4f;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = blastMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ps.Emit(36);
            }
            go.AddComponent<FadeLight>();
            Destroy(go, 2.5f);
        }
    }
}
