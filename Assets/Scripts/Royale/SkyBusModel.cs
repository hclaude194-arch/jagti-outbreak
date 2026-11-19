using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The sky bus that carries everyone over the campus at the start of a match: the campus
    /// shuttle (white body, navy window band, yellow waist line with red and green slashes) slung
    /// under a hot-air balloon in saffron and white bands, with a propeller pod on each side and the
    /// rear door open for jumping. Built in code with MeshKit from its own palette (Slot order below).
    /// Model space: origin in the middle of the bus, +Z forward, the balloon above.
    /// tools/royale/preview_skybus.py translates this file to Python to render it (through
    /// preview_vehicles.py), so keep it to MeshKit calls, simple declarations, foreach over literal
    /// arrays and plain for loops.
    /// </summary>
    public static class SkyBusModel
    {
        public const int White = 0, Navy = 1, Glass = 2, Dark = 3, Saffron = 4, Yellow = 5, Red = 6, Green = 7, Metal = 8;
        public const int Slots = 9;

        /// <summary>Where jumpers leave from: just behind the open rear door.</summary>
        public static readonly Vector3 Door = new Vector3(0f, -0.9f, -6.6f);
        /// <summary>The burner, in the mouth of the balloon.</summary>
        public static readonly Vector3 Burner = new Vector3(0f, 10.4f, 0f);

        static GameObject Part(string name, Transform parent, Vector3 pos, Mesh mesh, Material[] palette)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = palette;
            return go;
        }

        /// <summary>Builds the bus, rigging and balloon under root; the two propellers go into props (they spin).</summary>
        public static void Build(Transform root, Material[] palette, List<Transform> props)
        {
            Part("Bus", root, Vector3.zero, Bus(), palette);
            Part("Balloon", root, Vector3.zero, Balloon(), palette);
            foreach (float s in new[] { -1f, 1f })
            {
                var prop = Part("Propeller", root, new Vector3(s * 3.6f, 1.1f, -0.2f), Propeller(), palette);
                props.Add(prop.transform);
            }
        }

        static Mesh Bus()
        {
            const float w = 2.5f, len = 11f;
            var k = new MeshKit(Slots);
            // Body, window band, roof.
            k.slot = White;
            k.Box(new Vector3(0f, -0.35f, 0f), new Vector3(w, 2.1f, len));
            k.Box(new Vector3(0f, 1.15f, 0f), new Vector3(w, 0.5f, len - 0.1f), 0.96f, 0.99f);
            k.slot = Navy;
            k.Box(new Vector3(0f, 0.45f, -0.2f), new Vector3(w + 0.03f, 0.95f, len - 1.0f));
            k.slot = Glass;
            k.Box(new Vector3(0f, 0.35f, len / 2f), new Vector3(w - 0.2f, 1.35f, 0.06f));
            k.Box(new Vector3(0f, 0.55f, -len / 2f), new Vector3(w - 0.4f, 0.7f, 0.06f));
            // Waist line with the two slashes near the back.
            k.slot = Yellow;
            k.Box(new Vector3(0f, -0.18f, 0f), new Vector3(w + 0.04f, 0.12f, len - 0.2f));
            foreach (float s in new[] { -1f, 1f })
            {
                k.slot = Red;
                k.Box(new Vector3(s * (w / 2f + 0.02f), -0.5f, -3.6f), new Vector3(0.03f, 0.55f, 0.16f), 1f, 1f, new Vector3(28f, 0f, 0f));
                k.slot = Green;
                k.Box(new Vector3(s * (w / 2f + 0.02f), -0.5f, -3.95f), new Vector3(0.03f, 0.55f, 0.16f), 1f, 1f, new Vector3(28f, 0f, 0f));
            }
            // Bumpers, the open rear door, a step plate, the wheels.
            k.slot = Dark;
            k.Box(new Vector3(0f, -1.3f, len / 2f + 0.05f), new Vector3(w, 0.3f, 0.12f));
            k.Box(new Vector3(0f, -1.3f, -len / 2f - 0.05f), new Vector3(w, 0.3f, 0.12f));
            k.Box(new Vector3(0f, -0.25f, -len / 2f - 0.01f), new Vector3(1.3f, 2.0f, 0.04f));
            k.slot = Metal;
            k.Box(new Vector3(0f, -1.35f, -len / 2f - 0.5f), new Vector3(1.3f, 0.06f, 0.9f));
            k.Box(new Vector3(0f, 1.48f, 0f), new Vector3(1.4f, 0.12f, 3.2f));
            k.slot = Dark;
            foreach (float z in new[] { 3.6f, -2.6f })
                foreach (float s in new[] { -1f, 1f })
                    k.Tube(new Vector3(s * 1.05f, -1.4f, z), -0.16f, 0.16f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 12, true, new Vector3(0f, 0f, 90f));
            // Outriggers and the propeller pods.
            foreach (float s in new[] { -1f, 1f })
            {
                k.slot = Metal;
                k.Box(new Vector3(s * 2.2f, 1.1f, 0f), new Vector3(1.9f, 0.14f, 0.5f));
                k.Box(new Vector3(s * 2.0f, 0.4f, 0f), new Vector3(0.12f, 1.4f, 0.3f), 1f, 1f, new Vector3(0f, 0f, s * 40f));
                k.slot = Saffron;
                k.Ellipsoid(new Vector3(s * 3.6f, 1.1f, 0.7f), new Vector3(0.45f, 0.45f, 1.1f), 10, 6);
                k.slot = Dark;
                k.Tube(new Vector3(s * 3.6f, 1.1f, -0.25f), -0.1f, 0.1f, new Vector2(0.22f, 0.22f), new Vector2(0.22f, 0.22f), 8, true, new Vector3(90f, 0f, 0f));
            }
            return k.Finish("SkyBus_Body", false);
        }

        static Mesh Propeller()
        {
            var k = new MeshKit(Slots);
            k.slot = Dark;
            for (int i = 0; i < 3; i++)
                k.Box(Vector3.zero, new Vector3(0.22f, 2.6f, 0.05f), 1f, 1f, new Vector3(0f, 0f, i * 60f));
            return k.Finish("SkyBus_Propeller", false);
        }

        static Mesh Balloon()
        {
            var k = new MeshKit(Slots);
            // Rigging: four cables from the roof corners to the load ring, and the burner frame.
            k.slot = Metal;
            k.Tube(new Vector3(1.1f, 1.4f, 4.6f), 0f, 8.7f, new Vector2(0.04f, 0.04f), new Vector2(0.04f, 0.04f), 4, false, new Vector3(21.7f, 174.6f, 0f));
            k.Tube(new Vector3(-1.1f, 1.4f, 4.6f), 0f, 8.7f, new Vector2(0.04f, 0.04f), new Vector2(0.04f, 0.04f), 4, false, new Vector3(21.7f, -174.6f, 0f));
            k.Tube(new Vector3(1.1f, 1.4f, -4.6f), 0f, 8.7f, new Vector2(0.04f, 0.04f), new Vector2(0.04f, 0.04f), 4, false, new Vector3(21.7f, 5.4f, 0f));
            k.Tube(new Vector3(-1.1f, 1.4f, -4.6f), 0f, 8.7f, new Vector2(0.04f, 0.04f), new Vector2(0.04f, 0.04f), 4, false, new Vector3(21.7f, -5.4f, 0f));
            k.Tube(new Vector3(0f, 9.5f, 0f), -0.12f, 0.12f, new Vector2(1.6f, 1.6f), new Vector2(1.6f, 1.6f), 16, true);
            k.Tube(new Vector3(0f, 9.6f, 0f), 0f, 0.9f, new Vector2(0.35f, 0.35f), new Vector2(0.45f, 0.45f), 8, true);
            // Envelope: a teardrop of rings in saffron and white bands, a navy band at the widest.
            float[] ys = { 10.5f, 11.6f, 13.2f, 15.4f, 18.0f, 21.0f, 24.0f, 27.0f, 29.6f, 31.6f, 32.9f };
            float[] rs = { 2.2f, 4.2f, 6.8f, 9.4f, 11.4f, 12.4f, 12.3f, 11.0f, 8.6f, 5.4f, 2.2f };
            for (int i = 0; i < 10; i++)
            {
                k.slot = i % 2 == 0 ? Saffron : White;
                if (i == 5) k.slot = Navy;
                k.Tube(Vector3.zero, ys[i], ys[i + 1], new Vector2(rs[i], rs[i]), new Vector2(rs[i + 1], rs[i + 1]), 24, false);
            }
            k.slot = Saffron;
            k.Ellipsoid(new Vector3(0f, 32.9f, 0f), new Vector3(2.2f, 0.6f, 2.2f), 24, 4, 0f, 90f);
            return k.Finish("SkyBus_Balloon", false);
        }
    }
}
