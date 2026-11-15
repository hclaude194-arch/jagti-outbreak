using System;
using System.Collections.Generic;
using Jagti.Vehicles;
using Jagti.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Builds the campus shuttle bus (from the owner's photos of the real one: a white
    /// 11 m bus with a navy window band, sliding lower windows under white upper panels,
    /// a front door behind the front wheel on the left, blue seats, and a yellow waist line
    /// with red and green slashes near the back) and its route along the campus roads:
    /// south roundabout, past the hostels and the mess, the central roundabout, the
    /// academic block, the serpentine drive and the gate, and back. Traffic keeps left.
    /// No operator names or logos are copied.
    /// </summary>
    public static class CampusBusBuilder
    {
        const float W = 1.25f;      // half width
        const float L = 5.5f;       // half length
        const float Lane = 1.8f;    // keep-left offset from the road centre line

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static CampusBus Build(CampusLayout layout, Func<float, float, float> ground, float roadTop)
        {
            mats.Clear();
            List<int> stops;
            List<string> stopNames;
            var route = MakeRoute(layout, out stops, out stopNames);
            var root = new GameObject("Campus Bus");
            var bus = root.AddComponent<CampusBus>();
            MakeBody(root.transform);
            var wheels = new List<Transform>();
            var steered = new List<Transform>();
            foreach (float z in new[] { 3.6f, -2.4f })
                foreach (float side in new[] { -1f, 1f })
                {
                    var w = MakeWheel(root.transform, new Vector3(side * 1.02f, 0.5f, z), side, z < 0f);
                    wheels.Add(w);
                    if (z > 0f) steered.Add(w);
                }

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.8f, 0f);
            col.size = new Vector3(2 * W, 2.8f, 2 * L);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            bus.driverSeat = Anchor(root.transform, "Driver Seat", new Vector3(0.72f, 0.55f, 4.1f));
            bus.passengerSeat = Anchor(root.transform, "Passenger Seat", new Vector3(-0.85f, 0.5f, -1.05f));
            bus.door = Anchor(root.transform, "Door (outside)", new Vector3(-W - 1.3f, 0.2f, 2.4f));
            var pivot = Anchor(root.transform, "Chase Pivot", Vector3.zero);
            bus.chaseView = Anchor(pivot, "Chase View", new Vector3(0f, 4.6f, -15f));
            bus.chaseView.localRotation = Quaternion.Euler(12f, 0f, 0f);
            bus.wheels = wheels.ToArray();
            bus.steeredWheels = steered.ToArray();

            bus.route = route.ToArray();
            bus.stopIndex = stops.ToArray();
            bus.stopName = stopNames.ToArray();
            int first = stopNames.IndexOf("Brahmaputra Hostel");   // the stop beside the player's spawn
            bus.startIndex = first >= 0 ? stops[first] : 0;

            // Start waiting at the first stop (beside the player's spawn), facing along the route.
            Vector3 p = route[bus.startIndex];
            Vector3 next = route[(bus.startIndex + 2) % route.Count];
            p.y = ground(p.x, p.z) + roadTop;
            root.transform.position = p;
            Vector3 dir = next - route[bus.startIndex];
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) root.transform.rotation = Quaternion.LookRotation(dir);
            return bus;
        }

        // ------------------------------------------------------------------ route

        static List<Vector3> MakeRoute(CampusLayout layout, out List<int> stops, out List<string> names)
        {

            var serpentine = new List<Vector3>();
            foreach (var r in layout.roads)
                if (r.name == "Serpentine Drive")
                    for (int i = 0; i + 1 < r.points.Length; i += 2) serpentine.Add(new Vector3(r.points[i], 0f, r.points[i + 1]));
            serpentine.Reverse();   // academic block -> gate

            var southRb = M(421, 1364);
            var centralRb = M(414, 996);
            var gateRb = M(296, 487);
            serpentine.RemoveAll(q => (q - gateRb).magnitude < 11f);   // the loop round the roundabout replaces its end

            var spineSouth = new List<Vector3> { M(421, 1352), M(421, 1012) };       // northbound
            var spineNorth = new List<Vector3> { M(405, 986), M(405, 812), M(405, 630) };

            var fwd = new List<Vector3>();
            fwd.AddRange(spineSouth);
            fwd.AddRange(Arc(centralRb, 12f, -70f, -210f));      // clockwise round the west side
            fwd.AddRange(spineNorth);
            fwd.AddRange(serpentine);

            var back = new List<Vector3>();
            var sN = new List<Vector3>(spineNorth); sN.Reverse();
            var sS = new List<Vector3>(spineSouth); sS.Reverse();
            var sp = new List<Vector3>(serpentine); sp.Reverse();
            back.AddRange(sp);
            back.AddRange(sN);
            back.AddRange(Arc(centralRb, 12f, 138f, -70f));      // clockwise round the east side
            back.AddRange(sS);

            var loop = new List<Vector3>();
            loop.AddRange(fwd);
            loop.AddRange(Arc(gateRb, 10.5f, -15f, -345f));      // once round the gate roundabout
            loop.AddRange(back);
            loop.AddRange(Arc(southRb, 9.5f, 75f, -255f));       // and round the south one
            var dense = Densify(loop, 3f);
            var route = KeepLeft(dense, Lane);

            // Stops, each served in both directions.
            var stopNamesAll = new[] { "Brahmaputra Hostel", "Academic Block", "Main Gate", "Annapurna Mess", "Hostels and Sports" };
            var stopAt = new[] { M(405, 850), M(405, 645), M(332, 487), M(421, 1108), M(421, 1240) };
            stops = new List<int>();
            names = new List<string>();
            int half = route.Count / 2;   // the return half starts about here
            for (int si = 0; si < stopAt.Length; si++)
                for (int h = 0; h < 2; h++)
                {
                    int a = h == 0 ? 0 : half, b = h == 0 ? half : route.Count;
                    int best = -1;
                    float bestD = 20f * 20f;
                    for (int i = a; i < b; i++)
                    {
                        float d = (Flat(route[i]) - stopAt[si]).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best >= 0) { stops.Add(best); names.Add(stopNamesAll[si]); }
                }
            // Order the stops along the route, starting with the first one.
            var order = new List<int>();
            for (int i = 0; i < stops.Count; i++) order.Add(i);
            var s0 = stops; var n0 = names;
            order.Sort((x, y) => s0[x].CompareTo(s0[y]));
            var sortedStops = new List<int>();
            var sortedNames = new List<string>();
            foreach (int i in order) { sortedStops.Add(s0[i]); sortedNames.Add(n0[i]); }
            stops = sortedStops;
            names = sortedNames;
            return route;
        }

        // Map units -> world: X = x - 414, Z = 996 - y.
        static Vector3 M(float x, float y)
        {
            return new Vector3(x - 414f, 0f, 996f - y);
        }

        static IEnumerable<Vector3> Arc(Vector3 centre, float r, float fromDeg, float toDeg)
        {
            int n = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 15f));
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)n) * Mathf.Deg2Rad;
                yield return centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
        }

        static List<Vector3> Densify(List<Vector3> pts, float step)
        {
            var o = new List<Vector3>();
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
                float d = (b - a).magnitude;
                int n = Mathf.Max(1, Mathf.CeilToInt(d / step));
                if (d < 0.5f) continue;
                for (int k = 0; k < n; k++) o.Add(Vector3.Lerp(a, b, k / (float)n));
            }
            return o;
        }

        static List<Vector3> KeepLeft(List<Vector3> pts, float offset)
        {
            var o = new List<Vector3>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 d = pts[(i + 1) % pts.Count] - pts[(i - 1 + pts.Count) % pts.Count];
                d.y = 0f;
                d.Normalize();
                o.Add(pts[i] + new Vector3(-d.z, 0f, d.x) * offset);
            }
            return o;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ------------------------------------------------------------------ body

        static Material Mat(string name, Color c, float smooth = 0.3f, bool transparent = false)
        {
            Material m;
            if (mats.TryGetValue(name, out m)) return m;
            m = ProceduralAssets.LitMaterial("Bus_" + name, c, null, smooth);
            if (transparent && m.HasProperty("_Surface"))
            {
                // URP Lit, transparent with alpha blending.
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
                EditorUtility.SetDirty(m);
            }
            mats[name] = m;
            return m;
        }

        static Material White => Mat("White", new Color(0.93f, 0.93f, 0.90f), 0.55f);
        static Material Navy => Mat("Navy", new Color(0.07f, 0.11f, 0.28f), 0.4f);
        static Material Glass => Mat("Glass", new Color(0.10f, 0.14f, 0.17f, 0.38f), 0.9f, true);
        static Material Panel => Mat("UpperPanel", new Color(0.86f, 0.89f, 0.90f), 0.8f);
        static Material Black => Mat("Black", new Color(0.04f, 0.04f, 0.05f), 0.3f);
        static Material Chrome => Mat("Chrome", new Color(0.72f, 0.73f, 0.74f), 0.85f);
        static Material Seat => Mat("Seat", new Color(0.13f, 0.24f, 0.55f), 0.15f);
        static Material Floor => Mat("Floor", new Color(0.22f, 0.24f, 0.30f), 0.1f);
        static Material Red => Mat("Red", new Color(0.78f, 0.13f, 0.10f), 0.4f);
        static Material Green => Mat("Green", new Color(0.08f, 0.42f, 0.20f), 0.4f);
        static Material Yellow => Mat("Yellow", new Color(0.95f, 0.74f, 0.10f), 0.4f);
        static Material Lamp => Mat("Lamp", new Color(0.95f, 0.95f, 0.88f), 0.9f);
        static Material Tyre => Mat("Tyre", new Color(0.07f, 0.07f, 0.07f), 0.1f);
        static Material Interior => Mat("Interior", new Color(0.80f, 0.80f, 0.76f), 0.2f);

        static GameObject Part(Transform parent, PrimitiveType type, Vector3 centre, Vector3 size, Material mat)
        {
            return Part(parent, type, centre, size, mat, Vector3.zero);
        }

        static GameObject Part(Transform parent, PrimitiveType type, Vector3 centre, Vector3 size, Material mat, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void Box(Transform p, float x0, float x1, float y0, float y1, float z0, float z1, Material m)
        {
            Part(p, PrimitiveType.Cube, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
                 new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), m);
        }

        static void MakeBody(Transform root)
        {
            var parts = new GameObject("parts").transform;
            parts.SetParent(root, false);
            const float t = 0.05f;          // panel thickness
            float[] sides = { -1f, 1f };

            // Floor, chassis and the box behind each wheel arch.
            Box(parts, -W + t, W - t, 0.86f, 0.95f, -L + t, L - t, Floor);
            Box(parts, -0.8f, 0.8f, 0.35f, 0.86f, -4.9f, 4.9f, Black);
            foreach (float s in sides)
                foreach (float z in new[] { 3.6f, -2.4f })
                    Box(parts, s * 0.55f, s * 1.18f, 0.55f, 1.0f, z - 0.7f, z + 0.7f, Black);

            foreach (float s in sides)
            {
                float xo = s * W, xi = s * (W - t);
                // Lower body: waist panel and skirts between the wheels.
                Box(parts, xi, xo, 0.95f, 1.47f, -L, L, White);
                float[][] skirts = { new[] { -L, -3.05f }, new[] { -1.75f, 3.0f }, new[] { 4.2f, L } };
                foreach (var sk in skirts)
                {
                    float z0 = sk[0], z1 = sk[1];
                    if (s < 0f && z0 > -2f)
                    {
                        // Left side: leave the doorway (z 1.9 to 2.95) open below the waist.
                        Box(parts, xi, xo, 0.42f, 0.95f, z0, 1.9f, White);
                        continue;
                    }
                    Box(parts, xi, xo, 0.42f, 0.95f, z0, z1, White);
                }
                Box(parts, xi, xo + s * 0.02f, 0.38f, 0.46f, -L, L, Black);              // rubbing strip

                // Window band: sill, mid and top rails, pillars, glass and upper panels.
                var edges = new List<float> { -5.4f, -4.2f, -3.0f, -1.8f, -0.6f, 0.6f, 1.8f };
                if (s > 0f) edges.Add(3.0f);
                edges.Add(4.95f);
                float[][] rails = { new[] { 1.47f, 1.56f }, new[] { 2.33f, 2.42f }, new[] { 2.88f, 2.98f } };
                foreach (var rail in rails)
                {
                    float y0 = rail[0], y1 = rail[1];
                    if (s < 0f)
                    {
                        Box(parts, xi, xo, y0, y1, -L, 1.85f, Navy);
                        Box(parts, xi, xo, y0, y1, 2.95f, L, Navy);
                    }
                    else Box(parts, xi, xo, y0, y1, -L, L, Navy);
                }
                for (int k = 0; k < edges.Count; k++)
                {
                    float z = edges[k];
                    Box(parts, xi, xo + s * 0.01f, 1.47f, 2.98f, z - 0.06f, z + 0.06f, Navy);
                    if (k + 1 >= edges.Count) continue;
                    float za = z + 0.06f, zb = edges[k + 1] - 0.06f;
                    if (s < 0f && za > 1.85f && zb < 2.95f) continue;
                    Box(parts, s * (W - 0.03f), s * (W - 0.01f), 1.56f, 2.33f, za, zb, Glass);
                    Box(parts, xi, xo - s * 0.005f, 2.42f, 2.88f, za, zb, Panel);
                    // A sliding sash bar in each lower window.
                    float zm = (za + zb) / 2f;
                    Box(parts, s * (W - 0.04f), s * W, 1.56f, 2.33f, zm - 0.025f, zm + 0.025f, Black);
                }
                Box(parts, xi, xo, 1.47f, 2.98f, -L, -5.4f, White);                       // rear corner
                Box(parts, xi, xo, 1.47f, 2.98f, 4.95f, L, Navy);                          // front corner pillar

                // Livery: yellow waist line, a red slash ahead of the rear wheel and a long
                // green slash behind it.
                float lx = s * (W + 0.006f);
                Part(parts, PrimitiveType.Cube, new Vector3(lx, 1.27f, s < 0f ? -1.8f : -0.25f), new Vector3(0.012f, 0.045f, s < 0f ? 7.3f : 10.4f), Yellow);
                Part(parts, PrimitiveType.Cube, new Vector3(lx, 1.36f, -1.55f), new Vector3(0.012f, 0.3f, 0.07f), Red, new Vector3(-22f, 0f, 0f));
                Part(parts, PrimitiveType.Cube, new Vector3(lx, 0.95f, -3.3f), new Vector3(0.012f, 1.1f, 0.07f), Green, new Vector3(-20f, 0f, 0f));
            }

            // Front door on the left: glass leaves in navy frames, a step and a grab pole.
            Box(parts, -W, -W + 0.04f, 0.42f, 2.98f, 1.85f, 1.92f, Navy);
            Box(parts, -W, -W + 0.04f, 0.42f, 2.98f, 2.88f, 2.95f, Navy);
            Box(parts, -W, -W + 0.04f, 2.88f, 2.98f, 1.85f, 2.95f, Navy);
            Box(parts, -W + 0.005f, -W + 0.025f, 0.95f, 2.85f, 1.92f, 2.88f, Glass);
            Box(parts, -W, -W + 0.04f, 0.42f, 0.95f, 1.92f, 2.88f, Navy);
            Box(parts, -W, -W + 0.04f, 1.85f, 1.9f, 1.92f, 2.88f, Navy);
            Box(parts, -W + 0.1f, -W + 0.6f, 0.55f, 0.62f, 1.95f, 2.85f, Seat);              // step
            Part(parts, PrimitiveType.Cylinder, new Vector3(-W + 0.3f, 1.95f, 1.95f), new Vector3(0.04f, 1.0f, 0.04f), Chrome);

            // Roof with a rounded edge and the air-conditioning pod.
            Box(parts, -W, W, 2.98f, 3.12f, -L, L, White);
            Box(parts, -W + 0.12f, W - 0.12f, 3.12f, 3.2f, -L + 0.12f, L - 0.12f, White);
            Box(parts, -0.95f, 0.95f, 3.2f, 3.5f, -3.6f, 1.2f, White);
            Box(parts, -0.85f, 0.85f, 3.5f, 3.55f, -3.5f, 1.1f, Chrome);
            Box(parts, -W + 0.06f, W - 0.06f, 2.92f, 2.98f, -L + 0.06f, L - 0.06f, Interior);   // ceiling

            // Front: lower panel, grille and bonnet flap, headlights, bumper, plate, windscreen.
            Box(parts, -W, W, 0.42f, 1.47f, L - t, L, White);
            Box(parts, -0.6f, 0.6f, 1.06f, 1.3f, L, L + 0.03f, Black);
            Box(parts, -0.62f, 0.62f, 1.3f, 1.44f, L, L + 0.08f, White);
            Box(parts, -0.6f, 0.6f, 0.62f, 0.98f, L, L + 0.03f, Black);
            foreach (float s in sides)
            {
                Box(parts, s * 0.72f, s * 1.18f, 0.58f, 0.86f, L, L + 0.04f, Black);
                foreach (float lxp in new[] { 0.84f, 1.05f })
                    Part(parts, PrimitiveType.Cylinder, new Vector3(s * lxp, 0.73f, L + 0.05f), new Vector3(0.15f, 0.02f, 0.15f), Lamp, new Vector3(90f, 0f, 0f));
                // Mirror on an arm reaching forward from the top corner.
                Part(parts, PrimitiveType.Cube, new Vector3(s * 1.36f, 2.92f, L + 0.15f), new Vector3(0.04f, 0.04f, 0.45f), Navy, new Vector3(0f, s * -25f, 0f));
                Part(parts, PrimitiveType.Cube, new Vector3(s * 1.46f, 2.7f, L + 0.35f), new Vector3(0.04f, 0.45f, 0.04f), Navy);
                Box(parts, s * 1.4f, s * 1.56f, 2.25f, 2.7f, L + 0.28f, L + 0.42f, Black);
                Box(parts, s * (W - 0.12f), s * W, 1.47f, 2.98f, L - 0.06f, L + 0.01f, Navy);   // windscreen pillar
            }
            Box(parts, -W, W, 0.38f, 0.56f, L, L + 0.14f, Black);                            // bumper
            Box(parts, -0.95f, -0.45f, 0.56f, 0.68f, L + 0.02f, L + 0.05f, Yellow);          // plate
            Box(parts, -W, W, 1.44f, 1.5f, L - 0.06f, L + 0.01f, Navy);                       // screen sill
            Box(parts, -W, W, 2.7f, 2.98f, L - 0.06f, L + 0.01f, Navy);                       // destination band
            Part(parts, PrimitiveType.Cube, new Vector3(0f, 2.1f, L - 0.06f), new Vector3(2 * W - 0.24f, 1.22f, 0.02f), Glass, new Vector3(-5f, 0f, 0f));
            Box(parts, -0.2f, 0.2f, 2.4f, 2.62f, L - 0.16f, L - 0.14f, Panel);               // "on duty" card
            foreach (float s in sides)
                Part(parts, PrimitiveType.Cube, new Vector3(s * 0.45f, 1.62f, L + 0.0f), new Vector3(0.75f, 0.03f, 0.03f), Black, new Vector3(0f, 0f, s * 12f));
            Box(parts, -W + t, W - t, 1.25f, 1.55f, 4.95f, L - t, Black);                    // dashboard

            // Rear: panel, rear window, tail lights, bumper, plate.
            Box(parts, -W, W, 0.42f, 2.98f, -L, -L + t, White);
            Box(parts, -1.0f, 1.0f, 1.75f, 2.7f, -L - 0.01f, -L + 0.01f, Glass);
            foreach (float s in sides)
            {
                Box(parts, s * 0.95f, s * 1.2f, 0.7f, 1.25f, -L - 0.03f, -L, Red);
                Box(parts, s * 0.95f, s * 1.2f, 0.55f, 0.7f, -L - 0.03f, -L, Yellow);
            }
            Box(parts, -W, W, 0.38f, 0.56f, -L - 0.12f, -L, Black);
            Box(parts, -0.25f, 0.25f, 0.6f, 0.72f, -L - 0.03f, -L - 0.01f, Yellow);

            // Inside: blue 2 + 2 seats, the driver's seat and wheel, grab rails.
            for (float z = -4.9f; z <= 1.2f; z += 0.82f)
                foreach (float x in new[] { -0.95f, -0.5f, 0.5f, 0.95f })
                {
                    Box(parts, x - 0.21f, x + 0.21f, 1.3f, 1.4f, z - 0.2f, z + 0.22f, Seat);
                    Box(parts, x - 0.21f, x + 0.21f, 1.4f, 2.0f, z - 0.24f, z - 0.16f, Seat);
                    Box(parts, x - 0.05f, x + 0.05f, 0.95f, 1.3f, z - 0.05f, z + 0.05f, Chrome);
                }
            Box(parts, 0.5f, 0.95f, 1.3f, 1.42f, 3.9f, 4.35f, Seat);
            Box(parts, 0.5f, 0.95f, 1.42f, 2.1f, 3.82f, 3.92f, Seat);
            Part(parts, PrimitiveType.Cylinder, new Vector3(0.72f, 1.72f, 4.72f), new Vector3(0.46f, 0.02f, 0.46f), Black, new Vector3(-55f, 0f, 0f));
            foreach (float x in new[] { -0.28f, 0.28f })
                Part(parts, PrimitiveType.Cylinder, new Vector3(x, 2.75f, -1.8f), new Vector3(0.035f, 3.4f, 0.035f), Chrome, new Vector3(90f, 0f, 0f));

            Combine(root, parts);
        }

        static Transform MakeWheel(Transform root, Vector3 at, float side, bool dual)
        {
            var steer = new GameObject(side < 0f ? "Wheel Pivot L" : "Wheel Pivot R").transform;
            steer.SetParent(root, false);
            steer.localPosition = at;
            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(steer, false);
            float width = dual ? 0.42f : 0.3f;
            Part(wheel, PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.0f, width / 2f, 1.0f), Tyre, new Vector3(0f, 0f, 90f));
            Part(wheel, PrimitiveType.Cylinder, new Vector3(side * (width / 2f), 0f, 0f), new Vector3(0.62f, 0.02f, 0.62f), Chrome, new Vector3(0f, 0f, 90f));
            Part(wheel, PrimitiveType.Cylinder, new Vector3(side * (width / 2f + 0.03f), 0f, 0f), new Vector3(0.2f, 0.03f, 0.2f), Black, new Vector3(0f, 0f, 90f));
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                Part(wheel, PrimitiveType.Cube, new Vector3(side * (width / 2f + 0.025f), Mathf.Sin(a) * 0.19f, Mathf.Cos(a) * 0.19f),
                     new Vector3(0.04f, 0.05f, 0.05f), Black);
            }
            return wheel;
        }

        static Transform Anchor(Transform parent, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        // Merges the body parts into one mesh per material, so the bus costs a few draw calls.
        static void Combine(Transform root, Transform parts)
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var toRoot = root.worldToLocalMatrix;
            foreach (var mf in parts.GetComponentsInChildren<MeshFilter>())
            {
                var mat = mf.GetComponent<MeshRenderer>().sharedMaterial;
                List<CombineInstance> list;
                if (!groups.TryGetValue(mat, out list)) { list = new List<CombineInstance>(); groups[mat] = list; }
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toRoot * mf.transform.localToWorldMatrix });
            }
            ProceduralAssets.EnsureFolder(ProceduralAssets.Folder + "/Meshes");
            foreach (var kv in groups)
            {
                var mesh = new Mesh();
                mesh.name = "Bus_" + kv.Key.name;
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, ProceduralAssets.Folder + "/Meshes/" + mesh.name + ".asset");
                var go = new GameObject("Body " + kv.Key.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                if (kv.Key.renderQueue >= (int)RenderQueue.Transparent) mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            UnityEngine.Object.DestroyImmediate(parts.gameObject);
        }
    }
}
