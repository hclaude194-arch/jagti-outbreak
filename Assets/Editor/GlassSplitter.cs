using System.Collections.Generic;
using Jagti.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Makes the windows of a placed building breakable. The glass submesh of each building mesh is
    /// taken out into its own child mesh, cut into panes (big glass walls are divided into panes
    /// about 1.6 m wide), and a GlassPanes component on that child keeps the panes' positions and
    /// triangle ranges. The building keeps the rest of its mesh, which is also its collider, so a
    /// broken pane leaves a real opening; each pane gets its own thin box collider (saved with the
    /// scene under "Breakable Glass", so the NavMesh bake sees the windows as walls).
    /// The interior shells' glass behind the windows (Glass_Clear) is split the same way
    /// (InteriorBuilder), and GlassPanes breaks a window's two layers together.
    /// </summary>
    public static class GlassSplitter
    {
        const float MaxPaneWidth = 1.6f, MaxPaneHeight = 2.6f;

        static int nextId;
        static Transform colliderRoot;

        public static void Reset() { nextId = 0; colliderRoot = null; }

        static Transform ColliderRoot()
        {
            if (colliderRoot == null) colliderRoot = new GameObject("Breakable Glass").transform;
            return colliderRoot;
        }

        /// <summary>Splits every mesh under the building that uses the glass material. Returns the number of panes made.</summary>
        public static int Split(GameObject building, Material glass)
        {
            if (glass == null) return 0;
            int made = 0;
            foreach (var renderer in building.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var mats = renderer.sharedMaterials;
                int g = System.Array.IndexOf(mats, glass);
                var src = filter.sharedMesh;
                if (g < 0 || g >= src.subMeshCount) continue;
                made += SplitOne(building.name, renderer, filter, g, glass);
            }
            return made;
        }

        static int SplitOne(string buildingName, MeshRenderer renderer, MeshFilter filter, int g, Material glass)
        {
            var src = filter.sharedMesh;
            var verts = src.vertices;
            var normals = src.normals;
            var uvs = src.uv;
            bool hasNormals = normals != null && normals.Length == verts.Length;
            bool hasUv = uvs != null && uvs.Length == verts.Length;
            float scale = Mathf.Max(0.0001f, Mathf.Abs(filter.transform.lossyScale.x));

            // ---- the building without its glass (drawn and used as the collider)
            var rest = new Mesh { name = src.name + "_NoGlass", indexFormat = src.indexFormat };
            rest.vertices = verts;
            if (hasNormals) rest.normals = normals;
            if (hasUv) rest.uv = uvs;
            var uv2 = src.uv2;
            if (uv2 != null && uv2.Length == verts.Length) rest.uv2 = uv2;
            var tangents = src.tangents;
            if (tangents != null && tangents.Length == verts.Length) rest.tangents = tangents;
            rest.subMeshCount = src.subMeshCount - 1;
            var mats = renderer.sharedMaterials;
            var restMats = new List<Material>();
            for (int s = 0, d = 0; s < src.subMeshCount; s++)
            {
                if (s == g) continue;
                rest.SetTriangles(src.GetTriangles(s), d++, false);
                restMats.Add(s < mats.Length ? mats[s] : null);
            }
            rest.RecalculateBounds();

            // ---- the glass, grouped into panes
            var gt = src.GetTriangles(g);
            var panes = GroupPanes(verts, gt);
            var pv = new List<Vector3>();
            var pn = new List<Vector3>();
            var pu = new List<Vector2>();
            var pt = new List<int>();
            var centres = new List<Vector3>();
            var paneNormals = new List<Vector3>();
            var paneU = new List<Vector3>();
            var sizes = new List<Vector2>();
            var starts = new List<int>();
            var counts = new List<int>();
            foreach (var tris in panes)
            {
                // Plane basis: normal from the triangles, u along the ground, v up the pane.
                Vector3 n = Vector3.zero;
                for (int k = 0; k < tris.Count; k += 3)
                    n += Vector3.Cross(verts[tris[k + 1]] - verts[tris[k]], verts[tris[k + 2]] - verts[tris[k]]);
                if (n.sqrMagnitude < 1e-12f) continue;
                float area2 = n.magnitude;   // twice the summed area (all triangles face one way)
                n.Normalize();
                Vector3 u = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized;
                Vector3 v = Vector3.Cross(n, u).normalized;
                float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue, depth = 0f;
                foreach (int i in tris)
                {
                    float a = Vector3.Dot(verts[i], u), b = Vector3.Dot(verts[i], v);
                    a0 = Mathf.Min(a0, a); a1 = Mathf.Max(a1, a);
                    b0 = Mathf.Min(b0, b); b1 = Mathf.Max(b1, b);
                    depth += Vector3.Dot(verts[i], n);
                }
                depth /= tris.Count;
                float w = a1 - a0, h = b1 - b0;
                if (w <= 1e-4f || h <= 1e-4f) continue;
                Vector3 normal = hasNormals ? normals[tris[0]] : n;
                bool rectangle = area2 * 0.5f >= w * h * 0.97f;
                int nx = rectangle ? Mathf.Max(1, Mathf.CeilToInt(w * scale / MaxPaneWidth - 0.05f)) : 1;
                int ny = rectangle ? Mathf.Max(1, Mathf.CeilToInt(h * scale / MaxPaneHeight - 0.05f)) : 1;
                if (!rectangle)
                {
                    // Not a plain rectangle (a curved or odd piece): keep its triangles as one pane.
                    starts.Add(pt.Count);
                    foreach (int i in tris)
                    {
                        pt.Add(pv.Count);
                        pv.Add(verts[i]);
                        pn.Add(hasNormals ? normals[i] : n);
                        pu.Add(hasUv ? uvs[i] : Vector2.zero);
                    }
                    counts.Add(tris.Count);
                    AddPane(centres, paneNormals, paneU, sizes, u * ((a0 + a1) / 2f) + v * ((b0 + b1) / 2f) + n * depth, n, u, new Vector2(w, h));
                    continue;
                }
                // UVs: bilinear between the source vertices nearest the four corners.
                Vector2 uvA = NearestUv(verts, uvs, hasUv, tris, u, v, a0, b0), uvB = NearestUv(verts, uvs, hasUv, tris, u, v, a1, b0);
                Vector2 uvC = NearestUv(verts, uvs, hasUv, tris, u, v, a1, b1), uvD = NearestUv(verts, uvs, hasUv, tris, u, v, a0, b1);
                for (int cy = 0; cy < ny; cy++)
                    for (int cx = 0; cx < nx; cx++)
                    {
                        float s0 = cx / (float)nx, s1 = (cx + 1) / (float)nx, t0 = cy / (float)ny, t1 = (cy + 1) / (float)ny;
                        int baseV = pv.Count;
                        float[] ss = { s0, s1, s1, s0 };
                        float[] tt = { t0, t0, t1, t1 };
                        for (int c = 0; c < 4; c++)
                        {
                            pv.Add(u * Mathf.Lerp(a0, a1, ss[c]) + v * Mathf.Lerp(b0, b1, tt[c]) + n * depth);
                            pn.Add(normal);
                            Vector2 bottom = Vector2.Lerp(uvA, uvB, ss[c]), top = Vector2.Lerp(uvD, uvC, ss[c]);
                            pu.Add(Vector2.Lerp(bottom, top, tt[c]));
                        }
                        starts.Add(pt.Count);
                        // Wind the cell the same way as the source faces (front towards n).
                        if (Vector3.Dot(Vector3.Cross(pv[baseV + 1] - pv[baseV], pv[baseV + 2] - pv[baseV]), n) > 0f)
                            pt.AddRange(new[] { baseV, baseV + 1, baseV + 2, baseV, baseV + 2, baseV + 3 });
                        else
                            pt.AddRange(new[] { baseV, baseV + 2, baseV + 1, baseV, baseV + 3, baseV + 2 });
                        counts.Add(6);
                        float cw = w / nx, ch = h / ny;
                        Vector3 centre = u * (a0 + cw * (cx + 0.5f)) + v * (b0 + ch * (cy + 0.5f)) + n * depth;
                        AddPane(centres, paneNormals, paneU, sizes, centre, n, u, new Vector2(cw, ch));
                    }
            }
            if (starts.Count == 0) return 0;

            var glassMesh = new Mesh { name = src.name + "_Glass" };
            if (pv.Count > 65000) glassMesh.indexFormat = IndexFormat.UInt32;
            glassMesh.SetVertices(pv);
            glassMesh.SetNormals(pn);
            glassMesh.SetUVs(0, pu);
            glassMesh.SetTriangles(pt, 0);
            glassMesh.RecalculateBounds();

            ProceduralAssets.EnsureFolder(ProceduralAssets.Folder + "/Glass");
            string stem = ProceduralAssets.Folder + "/Glass/" + Safe(buildingName + "_" + filter.gameObject.name);
            AssetDatabase.CreateAsset(rest, stem + "_NoGlass.asset");
            AssetDatabase.CreateAsset(glassMesh, stem + "_Glass.asset");

            filter.sharedMesh = rest;
            renderer.sharedMaterials = restMats.ToArray();
            var col = renderer.GetComponent<MeshCollider>();
            if (col != null) col.sharedMesh = rest;

            var go = new GameObject("Glass");
            go.transform.SetParent(filter.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = glassMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = glass;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var gp = go.AddComponent<GlassPanes>();
            gp.id = nextId++;
            gp.thickness = 0.08f;
            gp.centres = centres.ToArray();
            gp.normals = paneNormals.ToArray();
            gp.uAxes = paneU.ToArray();
            gp.sizes = sizes.ToArray();
            gp.triStart = starts.ToArray();
            gp.triCount = counts.ToArray();
            gp.MakeColliders(ColliderRoot());
            return starts.Count;
        }

        static void AddPane(List<Vector3> c, List<Vector3> n, List<Vector3> u, List<Vector2> s, Vector3 centre, Vector3 normal, Vector3 axis, Vector2 size)
        {
            c.Add(centre);
            n.Add(normal);
            u.Add(axis);
            s.Add(size);
        }

        static Vector2 NearestUv(Vector3[] verts, Vector2[] uvs, bool hasUv, List<int> tris, Vector3 u, Vector3 v, float a, float b)
        {
            if (!hasUv) return Vector2.zero;
            float best = float.MaxValue;
            Vector2 result = Vector2.zero;
            foreach (int i in tris)
            {
                float da = Vector3.Dot(verts[i], u) - a, db = Vector3.Dot(verts[i], v) - b;
                float d = da * da + db * db;
                if (d < best) { best = d; result = uvs[i]; }
            }
            return result;
        }

        /// <summary>
        /// Groups glass triangles into panes: triangles on the same plane that share a corner (by
        /// position, since the importer may split vertices) belong together.
        /// </summary>
        static List<List<int>> GroupPanes(Vector3[] verts, int[] tris)
        {
            int count = tris.Length / 3;
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            var planeKey = new string[count];
            for (int t = 0; t < count; t++)
            {
                Vector3 a = verts[tris[t * 3]], b = verts[tris[t * 3 + 1]], c = verts[tris[t * 3 + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                planeKey[t] = Mathf.RoundToInt(n.x * 50f) + "," + Mathf.RoundToInt(n.y * 50f) + "," + Mathf.RoundToInt(n.z * 50f) + "," + Mathf.RoundToInt(Vector3.Dot(a, n) * 20f);
            }
            var byCorner = new Dictionary<string, int>();
            for (int t = 0; t < count; t++)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = verts[tris[t * 3 + k]];
                    string key = planeKey[t] + "|" + Mathf.RoundToInt(p.x * 1000f) + "," + Mathf.RoundToInt(p.y * 1000f) + "," + Mathf.RoundToInt(p.z * 1000f);
                    int other;
                    if (byCorner.TryGetValue(key, out other)) Union(parent, t, other);
                    else byCorner[key] = t;
                }
            var groups = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int t = 0; t < count; t++)
            {
                int r = Find(parent, t);
                List<int> list;
                if (!groups.TryGetValue(r, out list)) { list = new List<int>(); groups[r] = list; order.Add(r); }
                list.Add(tris[t * 3]); list.Add(tris[t * 3 + 1]); list.Add(tris[t * 3 + 2]);
            }
            var result = new List<List<int>>();
            foreach (int r in order) result.Add(groups[r]);
            return result;
        }

        static int Find(int[] p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        static void Union(int[] p, int a, int b)
        {
            a = Find(p, a); b = Find(p, b);
            if (a != b) p[a] = b;
        }

        static string Safe(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }
    }
}
