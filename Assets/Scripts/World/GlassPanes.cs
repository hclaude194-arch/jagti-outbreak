using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// The breakable windows of one building mesh (made by GlassSplitter in the editor). All the
    /// panes share one mesh and one two-sided mesh collider; breaking a pane hides its triangles,
    /// rebuilds the collider without it (once a frame at most), throws a burst of shards from one
    /// shared particle system and plays the smash. Cheap on purpose: no object per pane, and the
    /// work happens only when something breaks.
    /// Online, a local break is announced through Broken (NetMatch sends it to everyone).
    /// </summary>
    public class GlassPanes : MonoBehaviour
    {
        public static readonly List<GlassPanes> All = new List<GlassPanes>();
        /// <summary>Raised when a pane breaks here (not when told about it from another machine).</summary>
        public static System.Action<GlassPanes, int> Broken;

        /// <summary>The same on every machine (handed out in build order).</summary>
        public int id;
        /// <summary>Collider thickness in this object's units.</summary>
        public float thickness = 0.08f;
        // Per pane, in this object's space.
        public Vector3[] centres;
        public Vector3[] normals;
        public Vector3[] uAxes;
        public Vector2[] sizes;
        public int[] triStart;
        public int[] triCount;

        Mesh mesh, colliderMesh;
        int[] tris;
        bool[] broken;
        MeshCollider meshCollider;
        bool colliderDirty;
        Material glassMaterial;

        static readonly Dictionary<Collider, GlassPanes> lookup = new Dictionary<Collider, GlassPanes>();
        static ParticleSystem shards;

        public int Count { get { return centres != null ? centres.Length : 0; } }

        void Awake()
        {
            All.Add(this);
            var filter = GetComponent<MeshFilter>();
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) glassMaterial = mr.sharedMaterial;
            if (filter == null || filter.sharedMesh == null || centres == null) return;
            // Our own copy, so breaking never touches the saved asset.
            mesh = Instantiate(filter.sharedMesh);
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            tris = mesh.GetTriangles(0);
            broken = new bool[centres.Length];
            // One collider for all the building's panes (both faces, so it stops you from inside too);
            // it is rebuilt without the broken ones, at most once a frame.
            var go = new GameObject("Glass Collider");
            go.transform.SetParent(transform, false);
            colliderMesh = new Mesh { name = "Glass Collider", indexFormat = mesh.indexFormat };
            colliderMesh.vertices = mesh.vertices;
            meshCollider = go.AddComponent<MeshCollider>();
            RebuildCollider();
            lookup[meshCollider] = this;
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (meshCollider != null) lookup.Remove(meshCollider);
        }

        void LateUpdate()
        {
            if (colliderDirty) RebuildCollider();
        }

        void RebuildCollider()
        {
            colliderDirty = false;
            var list = new List<int>(tris.Length * 2);
            for (int i = 0; i < centres.Length; i++)
            {
                if (broken[i]) continue;
                int s = triStart[i], e = Mathf.Min(tris.Length, s + triCount[i]);
                for (int k = s; k + 2 < e; k += 3)
                {
                    list.Add(tris[k]); list.Add(tris[k + 1]); list.Add(tris[k + 2]);
                    list.Add(tris[k]); list.Add(tris[k + 2]); list.Add(tris[k + 1]);
                }
            }
            colliderMesh.SetTriangles(list, 0, true);
            meshCollider.sharedMesh = null;
            if (list.Count > 0) meshCollider.sharedMesh = colliderMesh;
            else meshCollider.enabled = false;
        }

        /// <summary>
        /// True if this collider is a building's glass and the point is on an unbroken pane (which one
        /// is found from the point: nearest pane whose rectangle holds it).
        /// </summary>
        public static bool TryGet(Collider c, Vector3 point, out GlassPanes panes, out int index)
        {
            panes = null;
            index = -1;
            GlassPanes g;
            if (c == null || !lookup.TryGetValue(c, out g) || g == null) return false;
            int i = g.PaneAt(point);
            if (i < 0) return false;
            panes = g;
            index = i;
            return true;
        }

        /// <summary>True if this collider belongs to breakable glass at all.</summary>
        public static bool IsGlass(Collider c)
        {
            return c != null && lookup.ContainsKey(c);
        }

        /// <summary>The unbroken pane at a world point (-1 if none is within a few centimetres).</summary>
        public int PaneAt(Vector3 world)
        {
            if (broken == null) return -1;
            Vector3 p = transform.InverseTransformPoint(world);
            float scale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
            float slack = 0.06f / scale;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < centres.Length; i++)
            {
                if (broken[i]) continue;
                Vector3 d = p - centres[i];
                float off = Mathf.Abs(Vector3.Dot(d, normals[i]));
                if (off > thickness + slack) continue;
                float du = Mathf.Abs(Vector3.Dot(d, uAxes[i]));
                Vector3 vAxis = Vector3.Cross(normals[i], uAxes[i]);
                float dv = Mathf.Abs(Vector3.Dot(d, vAxis));
                if (du > sizes[i].x * 0.5f + slack || dv > sizes[i].y * 0.5f + slack) continue;
                float score = off + du * 0.01f + dv * 0.01f;
                if (score < bestD) { bestD = score; best = i; }
            }
            return best;
        }

        public static GlassPanes ById(int id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].id == id) return All[i];
            return null;
        }

        public bool IsBroken(int index)
        {
            return broken == null || index < 0 || index >= broken.Length || broken[index];
        }

        /// <summary>Smashes a pane: dir is the way the hit was going (shards fly that way).</summary>
        public void Break(int index, Vector3 dir, bool local)
        {
            if (IsBroken(index)) return;
            broken[index] = true;
            colliderDirty = true;
            int s = triStart[index], e = s + triCount[index];
            if (e <= tris.Length)
            {
                // Hidden by making its triangles degenerate (the shared triangle list is kept as it is).
                var drawn = (int[])tris.Clone();
                for (int i = 0; i < broken.Length; i++)
                {
                    if (!broken[i]) continue;
                    int a = triStart[i], b = Mathf.Min(drawn.Length, a + triCount[i]);
                    for (int k = a; k < b; k++) drawn[k] = drawn[a];
                }
                mesh.SetTriangles(drawn, 0, false);
            }
            Vector3 centre = transform.TransformPoint(centres[index]);
            Vector3 n = transform.TransformDirection(normals[index]).normalized;
            if (dir.sqrMagnitude < 1e-4f) dir = -n;
            Vector3 u = transform.TransformDirection(uAxes[index]);
            float scale = Mathf.Abs(transform.lossyScale.x);
            Shards(centre, u.normalized, Vector3.Cross(n, u).normalized, sizes[index].x * scale, sizes[index].y * scale, dir.normalized);
            var clip = SynthAudio.Get("glass_break");
            if (clip != null) AudioSource.PlayClipAtPoint(clip, centre, 0.9f);
            NoiseEvents.Emit(centre, 40f);
            if (local && Broken != null) Broken(this, index);
        }

        /// <summary>
        /// Breaks every pane within the radius (a blast). Every machine runs the same blast, so
        /// these breaks aren't announced. Returns how many broke.
        /// </summary>
        public static int BreakAround(Vector3 point, float radius)
        {
            int count = 0;
            float r2 = radius * radius;
            for (int g = 0; g < All.Count; g++)
            {
                var panes = All[g];
                if (panes == null || panes.broken == null || panes.mesh == null) continue;
                // Skip whole buildings that are out of reach.
                Bounds b = panes.GetComponent<Renderer>() != null ? panes.GetComponent<Renderer>().bounds : new Bounds(panes.transform.position, Vector3.one * 1e6f);
                if (b.SqrDistance(point) > r2) continue;
                for (int i = 0; i < panes.centres.Length; i++)
                {
                    if (panes.broken[i]) continue;
                    Vector3 c = panes.transform.TransformPoint(panes.centres[i]);
                    if ((c - point).sqrMagnitude > r2) continue;
                    panes.Break(i, c - point, false);
                    count++;
                }
            }
            return count;
        }

        // ------------------------------------------------------------------ shards

        void Shards(Vector3 centre, Vector3 u, Vector3 v, float width, float height, Vector3 dir)
        {
            var ps = ShardSystem(glassMaterial);
            if (ps == null) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(width * height * 14f), 10, 40);
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                p.position = centre + u * (Random.value - 0.5f) * width + v * (Random.value - 0.5f) * height;
                p.velocity = dir * Random.Range(1.5f, 5f) + Random.insideUnitSphere * 1.6f + Vector3.up * Random.Range(0f, 1.5f);
                p.startSize = Random.Range(0.04f, 0.16f);
                p.startLifetime = Random.Range(1.2f, 2.2f);
                p.rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f);
                p.angularVelocity3D = Random.insideUnitSphere * 600f;
                ps.Emit(p, 1);
            }
        }

        static ParticleSystem ShardSystem(Material mat)
        {
            if (shards != null) return shards;
            var go = new GameObject("Glass Shards");
            shards = go.AddComponent<ParticleSystem>();
            shards.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = shards.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 400;
            main.gravityModifier = 1.4f;
            main.startRotation3D = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = shards.emission;
            em.enabled = false;
            var shape = shards.shape;
            shape.enabled = false;
            var rot = shards.rotationOverLifetime;
            rot.enabled = false;
            // Shards don't collide with anything (that would cost too much); they fall and fade.
            var col = shards.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = ShardMesh();
            r.alignment = ParticleSystemRenderSpace.World;
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            shards.Play();
            return shards;
        }

        static Mesh ShardMesh()
        {
            // A thin sliver: two triangles back to back, so it shows from either side.
            var m = new Mesh { name = "Glass Shard" };
            m.vertices = new[] { new Vector3(-0.5f, -0.4f, 0f), new Vector3(0.45f, -0.5f, 0f), new Vector3(0.05f, 0.6f, 0f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
            m.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            m.triangles = new[] { 0, 2, 1, 0, 1, 2 };
            m.RecalculateBounds();
            return m;
        }
    }
}
