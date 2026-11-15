using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Builds low-poly meshes from simple solids (boxes, tapered tubes, ellipsoids, domes) into
    /// submeshes, one per material. Each vertex can be bound rigidly to one bone, which makes a
    /// skinned mesh: a whole body becomes one renderer with a handful of draw calls.
    /// Parts are placed with a matrix (usually the bone's rest transform relative to the root).
    /// </summary>
    public class MeshKit
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<BoneWeight> w = new List<BoneWeight>();
        readonly List<int>[] tris;

        public Matrix4x4 place = Matrix4x4.identity;
        public int bone;
        public int slot;

        public MeshKit(int submeshes)
        {
            tris = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) tris[i] = new List<int>();
        }

        public int VertexCount { get { return v.Count; } }

        int Vert(Vector3 p, Vector3 normal, Vector2 t)
        {
            v.Add(place.MultiplyPoint3x4(p));
            n.Add(place.MultiplyVector(normal).normalized);
            uv.Add(t);
            w.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
            return v.Count - 1;
        }

        void Tri(int a, int b, int c)
        {
            var t = tris[slot];
            t.Add(a); t.Add(b); t.Add(c);
        }

        void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        /// <summary>A box centred on c; top can be narrower than the bottom (taper 0..1 of x and z).</summary>
        public void Box(Vector3 c, Vector3 size, float taperX = 1f, float taperZ = 1f, Vector3 euler = default(Vector3))
        {
            var r = Quaternion.Euler(euler);
            Vector3 h = size * 0.5f;
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                float sz = (i & 4) == 0 ? -1f : 1f;
                float tx = sy > 0f ? taperX : 1f;
                float tz = sy > 0f ? taperZ : 1f;
                corners[i] = c + r * new Vector3(sx * h.x * tx, sy * h.y, sz * h.z * tz);
            }
            // faces: each as 4 corner indices, counter-clockwise seen from outside (Unity: clockwise = front)
            Face(corners, 0, 2, 3, 1);   // -z
            Face(corners, 5, 7, 6, 4);   // +z
            Face(corners, 4, 6, 2, 0);   // -x
            Face(corners, 1, 3, 7, 5);   // +x
            Face(corners, 2, 6, 7, 3);   // +y
            Face(corners, 4, 0, 1, 5);   // -y
        }

        void Face(Vector3[] c, int a, int b, int d, int e)
        {
            Vector3 normal = Vector3.Cross(c[b] - c[a], c[d] - c[a]).normalized;
            int i0 = Vert(c[a], normal, new Vector2(0, 0));
            int i1 = Vert(c[b], normal, new Vector2(0, 1));
            int i2 = Vert(c[d], normal, new Vector2(1, 1));
            int i3 = Vert(c[e], normal, new Vector2(1, 0));
            Quad(i0, i1, i2, i3);
        }

        /// <summary>
        /// A tube along local Y from y0 to y1 with elliptical rings (radius x, radius z at each end)
        /// and flat caps. Good for limbs, necks, sleeves and barrels.
        /// </summary>
        public void Tube(Vector3 c, float y0, float y1, Vector2 r0, Vector2 r1, int sides = 8, bool caps = true, Vector3 euler = default(Vector3))
        {
            var rot = Quaternion.Euler(euler);
            int start = v.Count;
            for (int ring = 0; ring < 2; ring++)
            {
                float y = ring == 0 ? y0 : y1;
                Vector2 r = ring == 0 ? r0 : r1;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    Vector3 p = new Vector3(Mathf.Cos(a) * r.x, y, Mathf.Sin(a) * r.y);
                    Vector3 nn = new Vector3(Mathf.Cos(a) / Mathf.Max(0.001f, r.x), 0f, Mathf.Sin(a) / Mathf.Max(0.001f, r.y));
                    Vert(c + rot * p, rot * nn, new Vector2(s / (float)sides, ring));
                }
            }
            int row = sides + 1;
            bool up = y1 > y0;
            for (int s = 0; s < sides; s++)
            {
                int a = start + s, b = start + s + 1, d = start + row + s + 1, e = start + row + s;
                if (up) Quad(a, e, d, b); else Quad(a, b, d, e);
            }
            if (!caps) return;
            Cap(c, rot, y0, r0, sides, !up);
            Cap(c, rot, y1, r1, sides, up);
        }

        void Cap(Vector3 c, Quaternion rot, float y, Vector2 r, int sides, bool facingUp)
        {
            Vector3 normal = rot * (facingUp ? Vector3.up : Vector3.down);
            int centre = Vert(c + rot * new Vector3(0f, y, 0f), normal, new Vector2(0.5f, 0.5f));
            int first = v.Count;
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f;
                Vert(c + rot * new Vector3(Mathf.Cos(a) * r.x, y, Mathf.Sin(a) * r.y), normal, new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int s = 0; s < sides; s++)
            {
                if (facingUp) Tri(centre, first + s + 1, first + s);
                else Tri(centre, first + s, first + s + 1);
            }
        }

        /// <summary>An ellipsoid (or the top part of one when fromLat > -90) centred on c.</summary>
        public void Ellipsoid(Vector3 c, Vector3 radii, int sides = 10, int rings = 6, float fromLat = -90f, float toLat = 90f, Vector3 euler = default(Vector3))
        {
            var rot = Quaternion.Euler(euler);
            int start = v.Count;
            for (int ring = 0; ring <= rings; ring++)
            {
                float lat = Mathf.Lerp(fromLat, toLat, ring / (float)rings) * Mathf.Deg2Rad;
                for (int s = 0; s <= sides; s++)
                {
                    float lon = s / (float)sides * Mathf.PI * 2f;
                    Vector3 unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    Vector3 p = Vector3.Scale(unit, radii);
                    Vector3 nn = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z);
                    Vert(c + rot * p, rot * nn, new Vector2(s / (float)sides, ring / (float)rings));
                }
            }
            int row = sides + 1;
            for (int ring = 0; ring < rings; ring++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + ring * row + s, b = a + 1, d = a + row + 1, e = a + row;
                    Quad(a, e, d, b);
                }
        }

        public Mesh Finish(string name, bool skinned)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            if (skinned) mesh.boneWeights = w.ToArray();
            mesh.subMeshCount = tris.Length;
            for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
