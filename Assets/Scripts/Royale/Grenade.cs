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
        /// <summary>A smoke grenade: pops into a SmokeCloud instead of bursting.</summary>
        public bool smoke;
        public Material smokeMaterial;

        float t;
        float bounceCooldown;

        /// <summary>Online: a grenade thrown by someone this machine owns (thrower, smoke, from, velocity).</summary>
        public static System.Action<Combatant, bool, Vector3, Vector3> Launched;

        /// <summary>
        /// Throws a copy of an inactive template. Every throw goes through here so online play can show
        /// it on the other machines (copies of someone else's grenade burst there too, but only the
        /// thrower's machine deals the damage; see Combatant.ReceiveHit).
        /// </summary>
        public static Grenade Launch(GameObject template, Vector3 from, Vector3 velocity, Combatant thrower)
        {
            if (template == null) return null;
            var go = Instantiate(template, from, Quaternion.identity);
            go.SetActive(true);
            var g = go.GetComponent<Grenade>();
            g.thrower = thrower;
            go.GetComponent<Rigidbody>().AddForce(velocity, ForceMode.VelocityChange);
            if (Launched != null && thrower != null && thrower.owned) Launched(thrower, g.smoke, from, velocity);
            return g;
        }

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
            if (t >= fuse)
            {
                if (smoke) Pop();
                else Explode();
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (bounceCooldown > 0f || c.relativeVelocity.magnitude < 2f) return;
            bounceCooldown = 0.15f;
            Ballistics.SoundAt(transform.position, "clink", 0.6f);
        }

        void Pop()
        {
            var go = new GameObject("Smoke Cloud");
            go.transform.position = transform.position;
            var cloud = go.AddComponent<SmokeCloud>();
            cloud.material = smokeMaterial != null ? smokeMaterial : blastMaterial;
            Ballistics.SoundAt(transform.position, "smoke_pop", 0.8f);
            NoiseEvents.Emit(transform.position, 40f);
            Destroy(gameObject);
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
            foreach (var v in Vehicle.All.ToArray())
            {
                float dv = (v.transform.position - p).magnitude;
                if (dv < radius + 1f) v.TakeHit(maxDamage * 1.6f * Mathf.Clamp01(1f - dv / (radius + 1f)), thrower);
            }
            Jagti.World.GlassPanes.BreakAround(p, radius * 1.2f);
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
