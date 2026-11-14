using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Builds the small loot models from primitive shapes at runtime: a mesh per item (guns by class,
    /// ammo boxes by calibre, armour, packs, heals, grenades) whose submeshes index a shared palette of
    /// materials, plus the flat ring that shows an item's tier. LootDirector draws them instanced.
    /// </summary>
    public static class LootVisuals
    {
        // Palette slots (LootDirector.palette holds a material for each, in this order).
        public const int Metal = 0, Black = 1, Wood = 2, Olive = 3, Red = 4, White = 5, Brass = 6, Khaki = 7, Blue = 8;
        public const int PaletteSize = 9;

        public class Model
        {
            public Mesh mesh;
            public int[] submeshes;      // which submeshes have triangles
        }

        static readonly Dictionary<int, Model> models = new Dictionary<int, Model>();
        static Mesh cube, cylinder, sphere, ring;

        public static int Key(ItemKind kind, int value)
        {
            return (int)kind * 16 + (kind == ItemKind.Gun || kind == ItemKind.Ammo ? value : 0);
        }

        public static Model Get(ItemKind kind, int value)
        {
            int key = Key(kind, value);
            Model m;
            if (models.TryGetValue(key, out m) && m.mesh != null) return m;
            m = Build(kind, value);
            models[key] = m;
            return m;
        }

        public static Mesh Ring()
        {
            if (ring != null) return ring;
            // A flat annulus lying on the floor, 0.55 m across.
            const int seg = 24;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * 0.2f); v.Add(d * 0.3f);
                n.Add(Vector3.up); n.Add(Vector3.up);
                uv.Add(new Vector2(i / (float)seg, 0f)); uv.Add(new Vector2(i / (float)seg, 1f));
                if (i < seg)
                {
                    int k = i * 2;
                    t.Add(k); t.Add(k + 1); t.Add(k + 3);
                    t.Add(k); t.Add(k + 3); t.Add(k + 2);
                }
            }
            ring = new Mesh { name = "Loot Ring" };
            ring.SetVertices(v);
            ring.SetNormals(n);
            ring.SetUVs(0, uv);
            ring.SetTriangles(t, 0);
            ring.RecalculateBounds();
            return ring;
        }

        static void Primitives()
        {
            if (cube != null) return;
            cube = Grab(PrimitiveType.Cube);
            cylinder = Grab(PrimitiveType.Cylinder);
            sphere = Grab(PrimitiveType.Sphere);
        }

        static Mesh Grab(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return mesh;
        }

        class Builder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] tris = new List<int>[PaletteSize];

            public Builder()
            {
                for (int i = 0; i < PaletteSize; i++) tris[i] = new List<int>();
            }

            public void Add(Mesh src, Vector3 pos, Vector3 size, Vector3 euler, int slot)
            {
                var m = Matrix4x4.TRS(pos, Quaternion.Euler(euler), size);
                var rot = Quaternion.Euler(euler);
                int start = v.Count;
                var sv = src.vertices;
                var sn = src.normals;
                var su = src.uv;
                for (int i = 0; i < sv.Length; i++)
                {
                    v.Add(m.MultiplyPoint3x4(sv[i]));
                    n.Add(rot * sn[i]);
                    uv.Add(i < su.Length ? su[i] : Vector2.zero);
                }
                foreach (int i in src.triangles) tris[slot].Add(start + i);
            }

            public void Box(Vector3 pos, Vector3 size, int slot, Vector3 euler = default(Vector3)) { Add(cube, pos, size, euler, slot); }

            // Cylinder along local X when lying (length = size.y * 2 for Unity's cylinder).
            public void Tube(Vector3 pos, float diameter, float length, int slot, Vector3 euler)
            {
                Add(cylinder, pos, new Vector3(diameter, length / 2f, diameter), euler, slot);
            }

            public void Ball(Vector3 pos, Vector3 size, int slot) { Add(sphere, pos, size, Vector3.zero, slot); }

            public Model Finish(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = PaletteSize;
                var used = new List<int>();
                for (int i = 0; i < PaletteSize; i++)
                {
                    mesh.SetTriangles(tris[i], i);
                    if (tris[i].Count > 0) used.Add(i);
                }
                mesh.RecalculateBounds();
                return new Model { mesh = mesh, submeshes = used.ToArray() };
            }
        }

        static readonly Vector3 AlongX = new Vector3(0f, 0f, 90f);   // turns a cylinder to lie along X

        static Model Build(ItemKind kind, int value)
        {
            Primitives();
            var b = new Builder();
            switch (kind)
            {
                case ItemKind.Gun: BuildGun(b, (GunClass)RoyaleItems.Guns[Mathf.Clamp(value, 0, RoyaleItems.Guns.Length - 1)].gunClass); break;
                case ItemKind.Ammo:
                {
                    int lid = value == 0 ? Khaki : value == 1 ? Red : value == 2 ? Olive : Black;
                    b.Box(new Vector3(0f, 0.08f, 0f), new Vector3(0.3f, 0.16f, 0.16f), Olive);
                    b.Box(new Vector3(0f, 0.165f, 0f), new Vector3(0.31f, 0.02f, 0.17f), lid);
                    b.Box(new Vector3(0f, 0.19f, 0f), new Vector3(0.1f, 0.025f, 0.03f), Black);       // handle
                    b.Box(new Vector3(0f, 0.09f, 0.081f), new Vector3(0.12f, 0.05f, 0.002f), Brass); // label plate
                    break;
                }
                case ItemKind.Vest:
                {
                    int col = value >= 3 ? Black : value == 2 ? Olive : Khaki;
                    b.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.46f, 0.08f, 0.55f), col);
                    b.Box(new Vector3(0f, 0.09f, 0.05f), new Vector3(0.32f, 0.04f, 0.2f), col);       // front plate
                    b.Box(new Vector3(-0.15f, 0.085f, -0.2f), new Vector3(0.08f, 0.03f, 0.12f), Black);
                    b.Box(new Vector3(0.15f, 0.085f, -0.2f), new Vector3(0.08f, 0.03f, 0.12f), Black);
                    break;
                }
                case ItemKind.Helmet:
                {
                    int col = value >= 3 ? Black : value == 2 ? Olive : Khaki;
                    b.Ball(new Vector3(0f, 0.08f, 0f), new Vector3(0.27f, 0.2f, 0.3f), col);
                    b.Box(new Vector3(0f, 0.01f, 0f), new Vector3(0.28f, 0.02f, 0.31f), col);
                    b.Box(new Vector3(0f, 0.12f, 0.13f), new Vector3(0.06f, 0.04f, 0.03f), Black);    // mount
                    break;
                }
                case ItemKind.Backpack:
                {
                    int col = value >= 3 ? Black : value == 2 ? Olive : Blue;
                    float h = 0.35f + 0.08f * value;
                    b.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.36f, 0.2f, h), col);
                    b.Box(new Vector3(0f, 0.1f, h / 2f + 0.05f), new Vector3(0.28f, 0.14f, 0.1f), col);
                    b.Box(new Vector3(-0.1f, 0.21f, 0f), new Vector3(0.05f, 0.02f, h * 0.8f), Black);
                    b.Box(new Vector3(0.1f, 0.21f, 0f), new Vector3(0.05f, 0.02f, h * 0.8f), Black);
                    break;
                }
                case ItemKind.Bandage:
                    b.Tube(new Vector3(0f, 0.05f, 0f), 0.1f, 0.12f, White, AlongX);
                    b.Tube(new Vector3(0.12f, 0.05f, 0.04f), 0.1f, 0.12f, White, AlongX);
                    break;
                case ItemKind.Medkit:
                    b.Box(new Vector3(0f, 0.07f, 0f), new Vector3(0.4f, 0.14f, 0.26f), White);
                    b.Box(new Vector3(0f, 0.141f, 0f), new Vector3(0.18f, 0.004f, 0.05f), Red);
                    b.Box(new Vector3(0f, 0.141f, 0f), new Vector3(0.05f, 0.004f, 0.18f), Red);
                    b.Box(new Vector3(0f, 0.16f, 0f), new Vector3(0.12f, 0.03f, 0.03f), Black);
                    break;
                case ItemKind.ChaiFlask:
                    b.Tube(new Vector3(0f, 0.13f, 0f), 0.09f, 0.24f, Metal, Vector3.zero);
                    b.Tube(new Vector3(0f, 0.27f, 0f), 0.07f, 0.05f, Red, Vector3.zero);           // cap
                    b.Tube(new Vector3(0f, 0.12f, 0f), 0.092f, 0.06f, Red, Vector3.zero);          // band
                    break;
                case ItemKind.Grenade:
                    b.Ball(new Vector3(0f, 0.06f, 0f), new Vector3(0.09f, 0.11f, 0.09f), Olive);
                    b.Box(new Vector3(0f, 0.125f, 0f), new Vector3(0.03f, 0.03f, 0.03f), Metal);
                    b.Box(new Vector3(0.025f, 0.11f, 0f), new Vector3(0.015f, 0.06f, 0.025f), Metal);
                    b.Ball(new Vector3(0.18f, 0.06f, 0.03f), new Vector3(0.09f, 0.11f, 0.09f), Olive);
                    break;
            }
            return b.Finish("Loot " + kind + " " + value);
        }

        // Guns lie on their side along X, muzzle towards +X.
        static void BuildGun(Builder b, GunClass c)
        {
            float y = 0.04f;
            switch (c)
            {
                case GunClass.Sidearm:
                    b.Box(new Vector3(0.04f, y + 0.015f, 0f), new Vector3(0.2f, 0.035f, 0.032f), Metal);
                    b.Box(new Vector3(-0.04f, y - 0.02f, 0f), new Vector3(0.05f, 0.04f, 0.11f), Black, new Vector3(0f, 0f, 0f));
                    b.Box(new Vector3(-0.045f, y - 0.01f, -0.06f), new Vector3(0.045f, 0.03f, 0.06f), Black);
                    break;
                case GunClass.Smg:
                    b.Box(new Vector3(0.05f, y + 0.02f, 0f), new Vector3(0.32f, 0.07f, 0.05f), Black);
                    b.Tube(new Vector3(0.27f, y + 0.03f, 0f), 0.025f, 0.14f, Metal, AlongX);
                    b.Box(new Vector3(0.08f, y, -0.1f), new Vector3(0.035f, 0.03f, 0.16f), Metal);     // magazine
                    b.Box(new Vector3(-0.03f, y, -0.07f), new Vector3(0.04f, 0.03f, 0.1f), Black);     // grip
                    b.Box(new Vector3(-0.2f, y + 0.02f, 0f), new Vector3(0.14f, 0.04f, 0.03f), Black); // folding stock
                    break;
                case GunClass.Shotgun:
                    b.Tube(new Vector3(0.22f, y + 0.03f, 0f), 0.035f, 0.62f, Metal, AlongX);
                    b.Tube(new Vector3(0.18f, y, 0f), 0.04f, 0.4f, Metal, AlongX);                    // tube magazine
                    b.Box(new Vector3(0.12f, y + 0.0f, 0f), new Vector3(0.18f, 0.06f, 0.06f), Wood);   // pump
                    b.Box(new Vector3(-0.12f, y + 0.02f, 0f), new Vector3(0.2f, 0.08f, 0.05f), Metal);
                    b.Box(new Vector3(-0.38f, y, 0f), new Vector3(0.34f, 0.08f, 0.05f), Wood, new Vector3(0f, 0f, -6f));
                    break;
                case GunClass.Rifle:
                    b.Box(new Vector3(0.0f, y + 0.03f, 0f), new Vector3(0.36f, 0.08f, 0.06f), Black);
                    b.Box(new Vector3(0.28f, y + 0.03f, 0f), new Vector3(0.22f, 0.06f, 0.06f), Olive); // handguard
                    b.Tube(new Vector3(0.48f, y + 0.035f, 0f), 0.02f, 0.2f, Metal, AlongX);
                    b.Box(new Vector3(0.06f, y, -0.11f), new Vector3(0.05f, 0.03f, 0.17f), Black, new Vector3(0f, 10f, 0f)); // curved magazine
                    b.Box(new Vector3(-0.09f, y, -0.08f), new Vector3(0.04f, 0.03f, 0.1f), Black);
                    b.Box(new Vector3(-0.32f, y + 0.02f, 0f), new Vector3(0.26f, 0.08f, 0.05f), Olive);
                    b.Box(new Vector3(0.02f, y + 0.085f, 0f), new Vector3(0.16f, 0.025f, 0.025f), Black);  // rail
                    break;
                case GunClass.Marksman:
                    b.Box(new Vector3(0.0f, y + 0.03f, 0f), new Vector3(0.4f, 0.08f, 0.06f), Black);
                    b.Tube(new Vector3(0.45f, y + 0.035f, 0f), 0.025f, 0.5f, Metal, AlongX);
                    b.Tube(new Vector3(0.0f, y + 0.12f, 0f), 0.05f, 0.32f, Black, AlongX);             // scope
                    b.Box(new Vector3(0.05f, y, -0.1f), new Vector3(0.06f, 0.03f, 0.13f), Black);
                    b.Box(new Vector3(-0.36f, y + 0.02f, 0f), new Vector3(0.34f, 0.09f, 0.05f), Wood);
                    break;
            }
        }
    }
}
