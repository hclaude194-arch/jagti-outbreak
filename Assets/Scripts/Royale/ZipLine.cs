using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A zip line between two steel towers on the campus's steep slopes (placed by RoyaleBuilder
    /// where the cable clears the ground and the buildings). Press E at either tower to clip on:
    /// downhill you pick up speed, uphill a small motor hauls you along; S slows you, Space lets go.
    /// The cable sags a little in the middle.
    /// </summary>
    public class ZipLine : MonoBehaviour
    {
        public static readonly List<ZipLine> All = new List<ZipLine>();

        /// <summary>Tower bases on the ground; a is the higher end.</summary>
        public Vector3 a, b;
        public float towerHeight = 4.4f;
        public Material steel;
        public Material cable;
        public Material paint;

        public float Length { get; private set; }
        public Vector3 TopA { get { return a + Vector3.up * towerHeight; } }
        public Vector3 TopB { get { return b + Vector3.up * towerHeight; } }

        float sag;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Awake()
        {
            Length = (TopB - TopA).magnitude;
            sag = Length * 0.018f;
        }

        void Start()
        {
            Build();
        }

        /// <summary>Point on the cable, s from 0 (end a) to 1 (end b).</summary>
        public Vector3 PointAt(float s)
        {
            return Vector3.Lerp(TopA, TopB, s) - Vector3.up * (4f * sag * s * (1f - s));
        }

        /// <summary>Which end a point is near enough to clip on at (0 = a, 1 = b, -1 = neither).</summary>
        public int EndNear(Vector3 p, float radius)
        {
            if ((Flat(p - a)).sqrMagnitude < radius * radius && Mathf.Abs(p.y - a.y) < 2.5f) return 0;
            if ((Flat(p - b)).sqrMagnitude < radius * radius && Mathf.Abs(p.y - b.y) < 2.5f) return 1;
            return -1;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        void Build()
        {
            var k = new MeshKit(3);
            Tower(k, a, b);
            Tower(k, b, a);
            // The cable as thin segments along the sag.
            k.slot = 1;
            k.bone = 0;
            const int seg = 28;
            for (int i = 0; i < seg; i++)
            {
                Vector3 p0 = PointAt(i / (float)seg), p1 = PointAt((i + 1) / (float)seg);
                Vector3 mid = (p0 + p1) * 0.5f - transform.position;
                Vector3 d = p1 - p0;
                var rot = Quaternion.LookRotation(d.normalized, Vector3.up);
                k.place = Matrix4x4.TRS(mid, rot, Vector3.one);
                k.Box(Vector3.zero, new Vector3(0.025f, 0.025f, d.magnitude + 0.01f));
            }
            k.place = Matrix4x4.identity;
            // Solid legs, so nobody walks through the towers.
            for (int e = 0; e < 2; e++)
            {
                Vector3 end = e == 0 ? a : b, toward = (e == 0 ? b : a) - end;
                toward.y = 0f;
                var face = Quaternion.LookRotation(toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward, Vector3.up);
                foreach (float x in new[] { -0.6f, 0.6f })
                    foreach (float z in new[] { -0.6f, 0.6f })
                    {
                        var leg = new GameObject("Leg");
                        leg.transform.SetParent(transform, false);
                        leg.transform.position = end + face * new Vector3(x, towerHeight / 2f, z);
                        var c = leg.AddComponent<CapsuleCollider>();
                        c.radius = 0.08f;
                        c.height = towerHeight;
                    }
            }
            var go = new GameObject("Zip Line Mesh");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.Finish("Zip Line", false);
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { steel, cable, paint };
        }

        /// <summary>A lattice tower: four legs, cross braces, a small deck with a rail, a top beam.</summary>
        void Tower(MeshKit k, Vector3 at, Vector3 other)
        {
            Vector3 dir = other - at;
            dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward, Vector3.up);
            k.place = Matrix4x4.TRS(at - transform.position, rot, Vector3.one);
            float h = towerHeight;
            k.slot = 0;
            foreach (float x in new[] { -0.7f, 0.7f })
                foreach (float z in new[] { -0.7f, 0.7f })
                    k.Box(new Vector3(x * 0.85f, h / 2f, z * 0.85f), new Vector3(0.1f, h + 0.3f, 0.1f), 1f, 1f, new Vector3(-z * 2.5f, 0f, x * 2.5f));
            for (int level = 1; level <= 3; level++)
            {
                float y = h * level / 4f;
                k.Box(new Vector3(0f, y, -0.6f), new Vector3(1.2f, 0.06f, 0.06f));
                k.Box(new Vector3(0f, y, 0.6f), new Vector3(1.2f, 0.06f, 0.06f));
                k.Box(new Vector3(-0.6f, y, 0f), new Vector3(0.06f, 0.06f, 1.2f));
                k.Box(new Vector3(0.6f, y, 0f), new Vector3(0.06f, 0.06f, 1.2f));
                k.Box(new Vector3(-0.6f, y - h / 8f, 0f), new Vector3(0.04f, h / 4f * 1.3f, 0.04f), 1f, 1f, new Vector3(40f, 0f, 0f));
                k.Box(new Vector3(0.6f, y - h / 8f, 0f), new Vector3(0.04f, h / 4f * 1.3f, 0.04f), 1f, 1f, new Vector3(-40f, 0f, 0f));
            }
            k.Box(new Vector3(0f, h + 0.1f, 0f), new Vector3(0.25f, 0.2f, 1.6f));                  // top beam
            k.slot = 2;
            k.Box(new Vector3(0f, 0.12f, 0f), new Vector3(2.0f, 0.12f, 2.0f));                     // deck
            k.Box(new Vector3(0f, h + 0.25f, 0.6f), new Vector3(0.3f, 0.12f, 0.3f));              // pulley housing
            k.slot = 0;
            k.Box(new Vector3(-0.98f, 0.62f, 0f), new Vector3(0.05f, 0.05f, 2.0f));               // rails
            k.Box(new Vector3(0.98f, 0.62f, 0f), new Vector3(0.05f, 0.05f, 2.0f));
            k.Box(new Vector3(-0.98f, 0.4f, -0.95f), new Vector3(0.05f, 0.5f, 0.05f));
            k.Box(new Vector3(0.98f, 0.4f, -0.95f), new Vector3(0.05f, 0.5f, 0.05f));
        }
    }
}
