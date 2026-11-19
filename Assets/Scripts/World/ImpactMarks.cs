using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// What a bullet leaves where it lands, on top of the dust puff Ballistics already shows:
    /// on walls, roads and props a few hot sparks and a bullet mark that stays (the oldest of 64
    /// is reused); on the terrain a spray of dirt instead. Nothing is marked on moving things
    /// (vehicles, people). Materials come from VisualQuality when the scene loads.
    /// </summary>
    public static class ImpactMarks
    {
        const int MaxMarks = 64;

        static Material holeMaterial, sparkMaterial, dirtMaterial;
        static Transform root;
        static Transform[] marks;
        static int nextMark;
        static ParticleSystem sparks, dirt;
        static Mesh quad;

        public static void Setup(Material hole, Material spark, Material soil)
        {
            holeMaterial = hole;
            sparkMaterial = spark;
            dirtMaterial = soil;
            root = null;
            marks = null;
            nextMark = 0;
            sparks = null;
            dirt = null;
        }

        /// <summary>A shot stopped at point, travelling along dir (not a person).</summary>
        public static void At(Vector3 point, Vector3 dir)
        {
            if (holeMaterial == null && sparkMaterial == null && dirtMaterial == null) return;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            RaycastHit hit;
            if (!Physics.Raycast(point - dir * 0.2f, dir, out hit, 0.4f, ~0, QueryTriggerInteraction.Ignore)) return;

            var cam = Camera.main;
            if (cam != null && (cam.transform.position - hit.point).sqrMagnitude > 160f * 160f) return;

            if (hit.collider is TerrainCollider)
            {
                Dirt(hit.point, hit.normal);
                return;
            }
            Sparks(hit.point, hit.normal, Vector3.Reflect(dir, hit.normal));
            if (hit.collider.attachedRigidbody != null || hit.collider is CharacterController) return;
            if (hit.collider.GetComponentInParent<Animator>() != null) return;
            if (hit.collider.GetComponent("Hitbox") != null) return;
            Mark(hit);
        }

        static Transform Root()
        {
            if (root == null) root = new GameObject("Impact Marks").transform;
            return root;
        }

        static void Mark(RaycastHit hit)
        {
            if (holeMaterial == null) return;
            if (marks == null) marks = new Transform[MaxMarks];
            var t = marks[nextMark];
            if (t == null)
            {
                var go = new GameObject("Bullet Mark");
                go.transform.SetParent(Root(), false);
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = holeMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                t = go.transform;
                marks[nextMark] = t;
            }
            nextMark = (nextMark + 1) % MaxMarks;
            t.position = hit.point + hit.normal * 0.008f;
            t.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = Random.Range(0.08f, 0.13f);
            t.localScale = new Vector3(s, s, s);
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "Bullet Mark" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.RecalculateBounds();
            return quad;
        }

        static ParticleSystem Burst(string name, Material material, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root(), false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.035f;
                r.lengthScale = 1f;
            }
            return ps;
        }

        static void Sparks(Vector3 at, Vector3 normal, Vector3 bounce)
        {
            if (sparkMaterial == null) return;
            if (sparks == null)
            {
                sparks = Burst("Sparks", sparkMaterial, true);
                var main = sparks.main;
                main.gravityModifier = 1.2f;
                var fade = sparks.colorOverLifetime;
                fade.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                fade.color = g;
            }
            var p = new ParticleSystem.EmitParams();
            int n = Random.Range(3, 7);
            for (int i = 0; i < n; i++)
            {
                Vector3 v = (bounce * 0.6f + normal * 0.5f + Random.insideUnitSphere * 0.7f).normalized * Random.Range(2.5f, 7f);
                p.position = at + normal * 0.02f;
                p.velocity = v;
                p.startLifetime = Random.Range(0.12f, 0.3f);
                p.startSize = Random.Range(0.012f, 0.025f);
                p.startColor = new Color(1f, 0.85f, 0.5f, 1f);
                sparks.Emit(p, 1);
            }
        }

        static void Dirt(Vector3 at, Vector3 normal)
        {
            if (dirtMaterial == null) return;
            if (dirt == null)
            {
                dirt = Burst("Dirt", dirtMaterial, false);
                var main = dirt.main;
                main.gravityModifier = 1.4f;
                var fade = dirt.colorOverLifetime;
                fade.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
                fade.color = g;
                var size = dirt.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            }
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < 7; i++)
            {
                Vector3 v = (normal * 1.2f + Random.insideUnitSphere * 0.6f).normalized * Random.Range(1.2f, 3.6f);
                p.position = at + normal * 0.03f;
                p.velocity = v;
                p.startLifetime = Random.Range(0.45f, 0.8f);
                p.startSize = Random.Range(0.06f, 0.16f);
                float shade = Random.Range(0.75f, 1f);
                p.startColor = new Color(0.42f * shade, 0.33f * shade, 0.22f * shade, 0.9f);
                dirt.Emit(p, 1);
            }
        }
    }
}
