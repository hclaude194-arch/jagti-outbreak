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
            return (int)kind * 16 + (kind == ItemKind.Gun || kind == ItemKind.Ammo || kind == ItemKind.Attachment ? value : 0);
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

        /// <summary>Unity's cube mesh (shell cases are drawn with it).</summary>
        public static Mesh Cube()
        {
            Primitives();
            return cube;
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
                case ItemKind.Gun: BuildGun(b, Mathf.Clamp(value, 0, RoyaleItems.Guns.Length - 1)); break;
                case ItemKind.Attachment: BuildAttachment(b, (AttachmentType)value); break;
                case ItemKind.Smoke:
                    b.Tube(new Vector3(0f, 0.07f, 0f), 0.066f, 0.12f, Metal, Vector3.zero);
                    b.Tube(new Vector3(0f, 0.07f, 0f), 0.068f, 0.03f, White, Vector3.zero);
                    b.Box(new Vector3(0f, 0.14f, 0f), new Vector3(0.03f, 0.025f, 0.03f), Black);
                    b.Box(new Vector3(0.02f, 0.125f, 0f), new Vector3(0.012f, 0.06f, 0.022f), Metal);
                    break;
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

        // ------------------------------------------------------------------ guns
        // Gun frame: the barrel runs along +X, the gun's top is +Z (magazine and grip hang towards -Z)
        // and Y is its thickness, centred on Yc. Lying on the floor a gun rests on its side; the view
        // models and held guns turn it upright (loot X -> forward, Z -> up, Y -> right).

        const float Yc = 0.04f;

        /// <summary>A part of a gun: centre (x along the barrel, z up), length, height, thickness, slot, tilt in the X-Z plane.</summary>
        static void P(Builder b, float x, float z, float len, float h, float th, int slot, float tilt = 0f, float side = 0f)
        {
            b.Box(new Vector3(x, Yc + side, z), new Vector3(len, th, h), slot, new Vector3(0f, tilt, 0f));
        }

        /// <summary>A round tube along the barrel.</summary>
        static void Barrel(Builder b, float x, float z, float diameter, float len, int slot, float side = 0f)
        {
            b.Tube(new Vector3(x, Yc + side, z), diameter, len, slot, AlongX);
        }

        public struct Mounts
        {
            public Vector3 sight, muzzle, grip, mag;
            public bool builtInScope;
        }

        static readonly Vector3 None = new Vector3(float.NaN, 0f, 0f);

        /// <summary>Where attachments sit on each gun (gun frame; NaN x where none fits).</summary>
        public static Mounts MountsFor(int id)
        {
            switch (id)
            {
                case 0: return new Mounts { sight = new Vector3(0.03f, 0f, 0.028f), muzzle = new Vector3(0.125f, 0f, 0.012f), grip = None, mag = new Vector3(-0.05f, 0f, -0.06f) };
                case 1: return new Mounts { sight = new Vector3(0.05f, 0f, 0.056f), muzzle = new Vector3(0.34f, 0f, 0.015f), grip = new Vector3(0.17f, 0f, -0.015f), mag = new Vector3(0.08f, 0f, -0.02f) };
                case 2: return new Mounts { sight = new Vector3(-0.07f, 0f, 0.053f), muzzle = new Vector3(0.55f, 0f, 0.03f), grip = None, mag = None };
                case 3: return new Mounts { sight = new Vector3(0.02f, 0f, 0.066f), muzzle = new Vector3(0.59f, 0f, 0.025f), grip = new Vector3(0.27f, 0f, -0.008f), mag = new Vector3(0.06f, 0f, -0.015f) };
                case 4: return new Mounts { sight = new Vector3(0f, 0f, 0.066f), muzzle = new Vector3(0.7f, 0f, 0.025f), grip = new Vector3(0.32f, 0f, -0.01f), mag = new Vector3(0.06f, 0f, -0.02f), builtInScope = true };
                case 5: return new Mounts { sight = new Vector3(0.06f, 0f, 0.036f), muzzle = new Vector3(0.17f, 0f, 0.015f), grip = None, mag = None };
                case 6: return new Mounts { sight = new Vector3(0f, 0f, 0.083f), muzzle = new Vector3(0.44f, 0f, 0.025f), grip = new Vector3(0.22f, 0f, -0.008f), mag = new Vector3(0.05f, 0f, -0.015f) };
                case 7: return new Mounts { sight = None, muzzle = None, grip = None, mag = None };
                case 8: return new Mounts { sight = new Vector3(0f, 0f, 0.083f), muzzle = new Vector3(0.73f, 0f, 0.03f), grip = new Vector3(0.32f, 0f, 0.005f), mag = None };
                default: return new Mounts { sight = new Vector3(0f, 0f, 0.045f), muzzle = new Vector3(0.85f, 0f, 0.025f), grip = None, mag = new Vector3(0.02f, 0f, -0.03f), builtInScope = true };
            }
        }

        static void BuildGun(Builder b, int id)
        {
            switch (id)
            {
                case 0: // Ravi P9: a compact pistol.
                    P(b, 0.03f, 0.012f, 0.19f, 0.032f, 0.03f, Metal);
                    P(b, 0.02f, -0.01f, 0.17f, 0.02f, 0.028f, Black);
                    P(b, -0.045f, -0.055f, 0.045f, 0.085f, 0.03f, Black, 12f);
                    P(b, 0.0f, -0.032f, 0.045f, 0.006f, 0.012f, Black);
                    P(b, 0.11f, 0.03f, 0.008f, 0.006f, 0.006f, Black);
                    break;
                case 1: // Chenab SMG: boxy, folding stock.
                    P(b, 0.05f, 0.015f, 0.3f, 0.06f, 0.05f, Black);
                    P(b, 0.05f, 0.05f, 0.16f, 0.012f, 0.025f, Black);
                    Barrel(b, 0.27f, 0.015f, 0.025f, 0.14f, Metal);
                    P(b, 0.08f, -0.09f, 0.035f, 0.14f, 0.03f, Metal);
                    P(b, -0.04f, -0.055f, 0.04f, 0.08f, 0.035f, Black, 12f);
                    P(b, -0.2f, 0.01f, 0.14f, 0.035f, 0.03f, Black);
                    P(b, -0.27f, -0.005f, 0.02f, 0.07f, 0.035f, Black);
                    break;
                case 2: // Banihal 12: pump shotgun with a wooden stock and pump.
                    Barrel(b, 0.25f, 0.03f, 0.035f, 0.6f, Metal);
                    Barrel(b, 0.2f, -0.005f, 0.035f, 0.42f, Metal);
                    P(b, 0.17f, -0.005f, 0.16f, 0.055f, 0.055f, Wood);
                    P(b, -0.07f, 0.015f, 0.2f, 0.075f, 0.05f, Metal);
                    P(b, -0.36f, -0.01f, 0.36f, 0.08f, 0.045f, Wood, -6f);
                    P(b, -0.12f, -0.035f, 0.05f, 0.01f, 0.012f, Black);
                    break;
                case 3: // Tawi AR: olive furniture, rail on top.
                    P(b, 0f, 0.02f, 0.34f, 0.07f, 0.055f, Black);
                    P(b, 0.02f, 0.06f, 0.22f, 0.012f, 0.03f, Black);
                    P(b, 0.27f, 0.02f, 0.22f, 0.055f, 0.055f, Olive);
                    Barrel(b, 0.48f, 0.025f, 0.02f, 0.22f, Metal);
                    P(b, 0.36f, 0.06f, 0.015f, 0.04f, 0.01f, Black);
                    P(b, 0.06f, -0.08f, 0.045f, 0.14f, 0.028f, Black, -10f);
                    P(b, -0.09f, -0.05f, 0.035f, 0.08f, 0.035f, Black, 15f);
                    b.Tube(new Vector3(-0.2f, Yc, 0.02f), 0.03f, 0.12f, Black, AlongX);
                    P(b, -0.3f, 0f, 0.24f, 0.07f, 0.045f, Olive);
                    break;
                case 4: // Jhelum DMR: long barrel, wooden stock, its own scope.
                    P(b, 0f, 0.02f, 0.4f, 0.075f, 0.055f, Black);
                    Barrel(b, 0.45f, 0.025f, 0.025f, 0.5f, Metal);
                    P(b, 0.3f, 0.02f, 0.26f, 0.06f, 0.05f, Black);
                    P(b, 0.06f, -0.07f, 0.06f, 0.1f, 0.03f, Black);
                    P(b, -0.1f, -0.05f, 0.035f, 0.08f, 0.035f, Black, 15f);
                    P(b, -0.38f, 0f, 0.34f, 0.09f, 0.045f, Wood);
                    P(b, -0.34f, 0.05f, 0.18f, 0.03f, 0.04f, Wood);
                    Barrel(b, 0f, 0.1f, 0.05f, 0.32f, Black);
                    Barrel(b, 0.15f, 0.1f, 0.06f, 0.05f, Black);
                    P(b, -0.06f, 0.07f, 0.02f, 0.04f, 0.02f, Black);
                    P(b, 0.07f, 0.07f, 0.02f, 0.04f, 0.02f, Black);
                    break;
                case 5: // Basohli .45: a heavy revolver.
                    Barrel(b, 0.1f, 0.015f, 0.026f, 0.14f, Metal);
                    P(b, 0.1f, 0.032f, 0.14f, 0.008f, 0.012f, Metal);
                    Barrel(b, 0f, 0f, 0.056f, 0.05f, Metal);
                    P(b, -0.01f, 0f, 0.07f, 0.06f, 0.03f, Metal);
                    P(b, -0.045f, 0.03f, 0.015f, 0.025f, 0.01f, Black);
                    P(b, -0.06f, -0.05f, 0.04f, 0.085f, 0.032f, Wood, 20f);
                    P(b, -0.005f, -0.035f, 0.04f, 0.006f, 0.01f, Metal);
                    break;
                case 6: // Ujh Carbine: short, carry handle, skeleton stock.
                    P(b, 0f, 0.02f, 0.3f, 0.07f, 0.055f, Metal);
                    P(b, 0f, 0.07f, 0.12f, 0.025f, 0.015f, Metal);
                    P(b, 0.22f, 0.02f, 0.16f, 0.055f, 0.055f, Khaki);
                    Barrel(b, 0.37f, 0.025f, 0.02f, 0.14f, Metal);
                    P(b, 0.05f, -0.075f, 0.04f, 0.12f, 0.028f, Black, -8f);
                    P(b, -0.09f, -0.05f, 0.035f, 0.08f, 0.035f, Black, 15f);
                    P(b, -0.24f, 0.035f, 0.18f, 0.012f, 0.02f, Metal);
                    P(b, -0.24f, -0.02f, 0.18f, 0.012f, 0.02f, Metal);
                    P(b, -0.33f, 0f, 0.02f, 0.08f, 0.035f, Black);
                    break;
                case 7: // Bhaderwah Double: side-by-side barrels, wood.
                    Barrel(b, 0.25f, 0.02f, 0.03f, 0.55f, Metal, -0.016f);
                    Barrel(b, 0.25f, 0.02f, 0.03f, 0.55f, Metal, 0.016f);
                    P(b, 0.25f, 0.038f, 0.5f, 0.006f, 0.012f, Metal);
                    P(b, 0.12f, 0f, 0.18f, 0.04f, 0.05f, Wood);
                    P(b, -0.06f, 0.015f, 0.1f, 0.06f, 0.05f, Metal);
                    P(b, -0.3f, -0.01f, 0.4f, 0.08f, 0.045f, Wood, -7f);
                    P(b, -0.07f, 0.05f, 0.02f, 0.02f, 0.01f, Black);
                    break;
                case 8: // Udhampur LMG: heavy, box magazine, folded bipod.
                    P(b, 0f, 0.025f, 0.42f, 0.09f, 0.065f, Black);
                    P(b, 0f, 0.075f, 0.3f, 0.015f, 0.06f, Black);
                    Barrel(b, 0.48f, 0.03f, 0.028f, 0.5f, Metal);
                    P(b, 0.36f, 0.03f, 0.24f, 0.05f, 0.05f, Metal);
                    P(b, 0.32f, 0.075f, 0.08f, 0.02f, 0.012f, Black);
                    P(b, 0.05f, -0.09f, 0.12f, 0.12f, 0.08f, Olive);
                    P(b, 0.55f, -0.01f, 0.2f, 0.012f, 0.012f, Black, 0f, -0.02f);
                    P(b, 0.55f, -0.01f, 0.2f, 0.012f, 0.012f, Black, 0f, 0.02f);
                    P(b, -0.12f, -0.06f, 0.035f, 0.08f, 0.035f, Black, 15f);
                    P(b, -0.38f, 0.01f, 0.3f, 0.1f, 0.05f, Black);
                    break;
                default: // Pir Panjal: bolt-action rifle with a big scope.
                    Barrel(b, 0.5f, 0.025f, 0.022f, 0.7f, Metal);
                    P(b, 0.2f, 0f, 0.5f, 0.05f, 0.05f, Wood);
                    P(b, -0.33f, -0.015f, 0.36f, 0.1f, 0.045f, Wood, -5f);
                    P(b, -0.12f, -0.02f, 0.12f, 0.05f, 0.04f, Wood, 15f);
                    P(b, 0f, 0.025f, 0.2f, 0.04f, 0.04f, Metal);
                    P(b, -0.05f, 0.02f, 0.015f, 0.015f, 0.07f, Metal, 0f, 0.04f);
                    b.Ball(new Vector3(-0.05f, Yc + 0.08f, 0.02f), new Vector3(0.022f, 0.022f, 0.022f), Black);
                    P(b, 0.02f, -0.04f, 0.06f, 0.03f, 0.03f, Black);
                    Barrel(b, 0f, 0.1f, 0.055f, 0.4f, Black);
                    Barrel(b, 0.18f, 0.1f, 0.065f, 0.06f, Black);
                    Barrel(b, -0.17f, 0.1f, 0.06f, 0.05f, Black);
                    P(b, -0.07f, 0.065f, 0.02f, 0.035f, 0.02f, Black);
                    P(b, 0.08f, 0.065f, 0.02f, 0.035f, 0.02f, Black);
                    P(b, 0f, 0.135f, 0.02f, 0.02f, 0.02f, Black);
                    break;
            }
        }

        /// <summary>
        /// Attachments, built in the gun frame around their mount point: sights sit on it, muzzle
        /// parts run forward from it, grips and magazines hang below it.
        /// </summary>
        static void BuildAttachment(Builder b, AttachmentType a)
        {
            switch (a)
            {
                case AttachmentType.RedDot:
                    P(b, 0f, 0.012f, 0.05f, 0.024f, 0.03f, Black);
                    P(b, 0.01f, 0.038f, 0.012f, 0.03f, 0.032f, Black);
                    P(b, -0.012f, 0.036f, 0.004f, 0.012f, 0.014f, Red);
                    break;
                case AttachmentType.Scope2x:
                    P(b, 0f, 0.008f, 0.04f, 0.016f, 0.02f, Black);
                    Barrel(b, 0f, 0.032f, 0.036f, 0.11f, Black);
                    Barrel(b, 0.05f, 0.032f, 0.042f, 0.02f, Black);
                    break;
                case AttachmentType.Scope4x:
                case AttachmentType.Scope8x:
                {
                    bool big = a == AttachmentType.Scope8x;
                    float len = big ? 0.34f : 0.24f, d = big ? 0.048f : 0.042f, z = big ? 0.048f : 0.04f;
                    P(b, -len * 0.25f, z * 0.4f, 0.02f, z * 0.8f, 0.02f, Black);
                    P(b, len * 0.25f, z * 0.4f, 0.02f, z * 0.8f, 0.02f, Black);
                    Barrel(b, 0f, z, d, len, Black);
                    Barrel(b, len * 0.42f, z, d * 1.35f, len * 0.18f, Black);
                    Barrel(b, -len * 0.42f, z, d * 1.2f, len * 0.14f, Black);
                    P(b, 0f, z + d * 0.6f, 0.02f, 0.02f, 0.02f, Black);
                    if (big) P(b, 0f, z, 0.02f, 0.02f, 0.02f, Black, 0f, d * 0.6f);
                    break;
                }
                case AttachmentType.Compensator:
                    Barrel(b, 0.035f, 0f, 0.032f, 0.07f, Black);
                    P(b, 0.045f, 0.012f, 0.03f, 0.012f, 0.012f, Metal);
                    break;
                case AttachmentType.Suppressor:
                    Barrel(b, 0.1f, 0f, 0.042f, 0.2f, Black);
                    break;
                case AttachmentType.Grip:
                    P(b, 0f, -0.005f, 0.04f, 0.012f, 0.03f, Black);
                    P(b, 0f, -0.045f, 0.03f, 0.08f, 0.03f, Black, -8f);
                    break;
                default:
                    P(b, 0.012f, -0.1f, 0.045f, 0.2f, 0.03f, Black, -6f);
                    P(b, 0.022f, -0.2f, 0.05f, 0.015f, 0.034f, Metal, -6f);
                    break;
            }
        }

        /// <summary>
        /// Puts the attachments fitted to a gun on a gun model (a child per part, in the gun frame);
        /// rebuilds only when the fitted parts change. Guns with their own scope keep it.
        /// </summary>
        public static void Dress(Transform gunModel, GunItem g, Material[] palette)
        {
            if (gunModel == null) return;
            int sig = g == null ? -1 : g.def.id * 10000 + (g.parts[0] + 1) * 1000 + (g.parts[1] + 1) * 100 + (g.parts[2] + 1) * 10 + (g.parts[3] + 1);
            var mark = gunModel.Find("Parts");
            if (mark != null)
            {
                var tag = mark.GetComponent<PartsTag>();
                if (tag != null && tag.signature == sig) return;
                mark.name = "Parts (old)";
                Object.Destroy(mark.gameObject);
            }
            var root = new GameObject("Parts").transform;
            root.SetParent(gunModel, false);
            root.gameObject.AddComponent<PartsTag>().signature = sig;
            if (g == null) return;
            var m = MountsFor(g.def.id);
            for (int place = 0; place < 4; place++)
            {
                int part = g.parts[place];
                if (part < 0) continue;
                if (place == 0 && m.builtInScope) continue;
                Vector3 at = place == 0 ? m.sight : place == 1 ? m.muzzle : place == 2 ? m.grip : m.mag;
                if (float.IsNaN(at.x)) continue;
                var model = Get(ItemKind.Attachment, part);
                var go = new GameObject(((AttachmentType)part).ToString());
                go.transform.SetParent(root, false);
                go.transform.localPosition = at;
                go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = palette;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
