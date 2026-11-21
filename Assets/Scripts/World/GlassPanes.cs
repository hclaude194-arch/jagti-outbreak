using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.World
{
    /// <summary>
    /// The breakable windows of one building mesh (made by GlassSplitter in the editor). All the
    /// panes share one mesh; each pane has its own thin box collider (made in the editor, so the
    /// NavMesh bake sees the glass, under one unscaled holder per building). Breaking a pane hides
    /// its triangles, switches its box off, throws a burst of shards from one shared particle
    /// system and plays the smash. A window can have two layers of glass (the facade's pane and the
    /// interior shell's pane behind it); breaking one breaks its twin too, on every machine, so a
    /// window never leaves an invisible pane behind.
    /// Online, a local break is announced through Broken (NetMatch sends it to everyone).
    /// </summary>
    public class GlassPanes : MonoBehaviour
    {
        public static readonly List<GlassPanes> All = new List<GlassPanes>();
        /// <summary>Raised when a pane breaks here (not when told about it from another machine).</summary>
        public static System.Action<GlassPanes, int> Broken;

        /// <summary>The same on every machine (handed out in build order).</summary>
        public int id;
        /// <summary>Collider thickness in metres.</summary>
        public float thickness = 0.08f;
        // Per pane, in this object's space.
        public Vector3[] centres;
        public Vector3[] normals;
        public Vector3[] uAxes;
        public Vector2[] sizes;
        public int[] triStart;
        public int[] triCount;
        /// <summary>One box per pane (made by MakeColliders; built at start if missing).</summary>
        public BoxCollider[] colliders;

        const float TwinGap = 0.9f;   // a window's two layers of glass are at most this far apart

        Mesh mesh;
        int[] tris;
        bool[] broken;
        Material glassMaterial;
        // Per pane, in world space (the buildings never move).
        Vector3[] wc, wn, wu;
        Vector2[] ws;
        Bounds worldBounds;

        struct PaneRef
        {
            public GlassPanes owner;
            public int index;
        }

        static readonly Dictionary<Collider, PaneRef> lookup = new Dictionary<Collider, PaneRef>();
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
            WorldPanes();
            // A scene built before the boxes were made in the editor: make them now.
            if (colliders == null || colliders.Length != centres.Length) MakeColliders(null);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                lookup[colliders[i]] = new PaneRef { owner = this, index = i };
                colliders[i].enabled = true;
            }
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (colliders == null) return;
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) lookup.Remove(colliders[i]);
        }

        /// <summary>The panes' centres, axes and sizes in world space (and the bounds round them).</summary>
        void WorldPanes()
        {
            int n = centres.Length;
            wc = new Vector3[n];
            wn = new Vector3[n];
            wu = new Vector3[n];
            ws = new Vector2[n];
            float scale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
            for (int i = 0; i < n; i++)
            {
                wc[i] = transform.TransformPoint(centres[i]);
                Vector3 nn = transform.TransformDirection(normals[i]).normalized;
                Vector3 u = transform.TransformDirection(uAxes[i]);
                u = (u - nn * Vector3.Dot(u, nn)).normalized;
                if (u.sqrMagnitude < 0.5f) u = Vector3.Cross(Vector3.up, nn).sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, nn).normalized : Vector3.right;
                wn[i] = nn;
                wu[i] = u;
                ws[i] = sizes[i] * scale;
                if (i == 0) worldBounds = new Bounds(wc[i], Vector3.zero);
                else worldBounds.Encapsulate(wc[i]);
            }
            worldBounds.Expand(4f);
        }

        /// <summary>
        /// Makes one thin box per pane, grouped by facing under an unscaled holder (boxes under a
        /// scaled or mirrored model would be skewed). The editor calls this when it splits the glass,
        /// so the boxes are saved with the scene and the NavMesh bake treats windows as walls.
        /// </summary>
        public void MakeColliders(Transform parent)
        {
            if (centres == null) return;
            if (wc == null || wc.Length != centres.Length) WorldPanes();
            var holder = new GameObject("Glass Colliders " + id).transform;
            if (parent != null) holder.SetParent(parent, false);
            var groups = new Dictionary<long, Transform>();
            colliders = new BoxCollider[centres.Length];
            for (int i = 0; i < centres.Length; i++)
            {
                Vector3 v = Vector3.Cross(wn[i], wu[i]);
                Quaternion rot = Quaternion.LookRotation(wn[i], v);
                Vector3 e = rot.eulerAngles;
                long key = (long)Mathf.RoundToInt(e.x * 2f) * 1000000L + (long)Mathf.RoundToInt(e.y * 2f) * 1000L + Mathf.RoundToInt(e.z * 2f);
                Transform g;
                if (!groups.TryGetValue(key, out g))
                {
                    g = new GameObject("Panes").transform;
                    g.SetParent(holder, false);
                    g.position = wc[i];
                    g.rotation = rot;
                    groups[key] = g;
                }
                var box = g.gameObject.AddComponent<BoxCollider>();
                box.center = g.InverseTransformPoint(wc[i]);
                box.size = new Vector3(Mathf.Max(0.02f, ws[i].x), Mathf.Max(0.02f, ws[i].y), thickness);
                colliders[i] = box;
            }
        }

        /// <summary>True if this collider is a building's glass and its pane is unbroken.</summary>
        public static bool TryGet(Collider c, Vector3 point, out GlassPanes panes, out int index)
        {
            panes = null;
            index = -1;
            PaneRef r;
            if (c == null || !lookup.TryGetValue(c, out r) || r.owner == null || r.owner.IsBroken(r.index)) return false;
            panes = r.owner;
            index = r.index;
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
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < wc.Length; i++)
            {
                if (broken[i]) continue;
                Vector3 d = world - wc[i];
                float off = Mathf.Abs(Vector3.Dot(d, wn[i]));
                if (off > thickness + 0.06f) continue;
                float du = Mathf.Abs(Vector3.Dot(d, wu[i]));
                float dv = Mathf.Abs(Vector3.Dot(d, Vector3.Cross(wn[i], wu[i])));
                if (du > ws[i].x * 0.5f + 0.06f || dv > ws[i].y * 0.5f + 0.06f) continue;
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

        /// <summary>Smashes a pane (and the other layer of the same window): dir is the way the hit was going.</summary>
        public void Break(int index, Vector3 dir, bool local)
        {
            if (IsBroken(index)) return;
            Smash(index, dir);
            BreakTwins(index, dir);
            if (local && Broken != null) Broken(this, index);
        }

        void Smash(int index, Vector3 dir)
        {
            broken[index] = true;
            if (colliders != null && index < colliders.Length && colliders[index] != null) colliders[index].enabled = false;
            int s = triStart[index], e = s + triCount[index];
            if (e <= tris.Length)
            {
                // Hidden by making its triangles degenerate.
                for (int k = s; k < e; k++) tris[k] = tris[s];
                mesh.SetTriangles(tris, 0, false);
            }
            Vector3 centre = wc[index];
            Vector3 n = wn[index];
            if (dir.sqrMagnitude < 1e-4f) dir = -n;
            Vector3 u = wu[index];
            Shards(centre, u, Vector3.Cross(n, u), ws[index].x, ws[index].y, dir.normalized);
            if (Jagti.Audio.SoundSystem.Play("glass_break", centre, 0.9f) == null && Jagti.Audio.SoundSystem.Instance == null)
            {
                var clip = SynthAudio.Get("glass_break");
                if (clip != null) AudioSource.PlayClipAtPoint(clip, centre, 0.9f);
            }
            NoiseEvents.Emit(centre, 40f);
        }

        /// <summary>
        /// Breaks the panes of other glass that stand in the same window: parallel, within TwinGap
        /// of this pane's plane, and overlapping it. Every machine does the same, so it isn't sent.
        /// </summary>
        void BreakTwins(int index, Vector3 dir)
        {
            Vector3 c = wc[index], n = wn[index];
            Vector3 v = Vector3.Cross(n, wu[index]);
            for (int g = 0; g < All.Count; g++)
            {
                var other = All[g];
                if (other == null || other.broken == null) continue;
                if (other.worldBounds.SqrDistance(c) > TwinGap * TwinGap) continue;
                for (int j = 0; j < other.wc.Length; j++)
                {
                    if (other.broken[j] || (other == this && j == index)) continue;
                    if (Mathf.Abs(Vector3.Dot(other.wn[j], n)) < 0.96f) continue;
                    Vector3 d = other.wc[j] - c;
                    if (Mathf.Abs(Vector3.Dot(d, n)) > TwinGap) continue;
                    // The same plane (another pane of this window) is left alone; only a layer behind counts.
                    if (Mathf.Abs(Vector3.Dot(d, n)) < 0.05f) continue;
                    float du = Mathf.Abs(Vector3.Dot(d, wu[index])), dv = Mathf.Abs(Vector3.Dot(d, v));
                    float otherU = Mathf.Abs(Vector3.Dot(other.wu[j], wu[index])) > 0.7f ? other.ws[j].x : other.ws[j].y;
                    float otherV = Mathf.Abs(Vector3.Dot(other.wu[j], wu[index])) > 0.7f ? other.ws[j].y : other.ws[j].x;
                    if (du > (ws[index].x + otherU) * 0.5f - 0.1f || dv > (ws[index].y + otherV) * 0.5f - 0.1f) continue;
                    other.Smash(j, dir);
                }
            }
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
                if (panes.worldBounds.SqrDistance(point) > r2) continue;   // the whole building is out of reach
                for (int i = 0; i < panes.wc.Length; i++)
                {
                    if (panes.broken[i]) continue;
                    if ((panes.wc[i] - point).sqrMagnitude > r2) continue;
                    panes.Smash(i, panes.wc[i] - point);
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
