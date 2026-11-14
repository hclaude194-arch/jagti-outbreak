using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The scooter and the utility cart, built in code from the loot palette (no new materials):
    /// one mesh for the body (submeshes by palette slot), separate wheels that spin, and on the
    /// scooter a steering head (fork, front wheel, handlebar, headlamp) that turns.
    /// Model space: origin on the ground between the axles, +Z forward.
    /// </summary>
    public static class VehicleModels
    {
        const int Metal = LootVisuals.Metal, Black = LootVisuals.Black, Red = LootVisuals.Red,
                  White = LootVisuals.White, Khaki = LootVisuals.Khaki;

        public static void Build(Vehicle v, Material[] palette, int paint)
        {
            if (v.kind == Vehicle.Kind.Scooter) Scooter(v, palette, paint);
            else Cart(v, palette, paint);
        }

        static GameObject Part(string name, Transform parent, Vector3 pos, Mesh mesh, Material[] palette)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = palette;
            return go;
        }

        static Mesh Wheel(float radius, float width, bool rim)
        {
            var k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = Black;
            k.Tube(Vector3.zero, -width / 2f, width / 2f, new Vector2(radius, radius), new Vector2(radius, radius), 14, true, new Vector3(0f, 0f, 90f));
            k.slot = rim ? White : Metal;
            k.Tube(Vector3.zero, -width / 2f - 0.004f, width / 2f + 0.004f, new Vector2(radius * 0.55f, radius * 0.55f), new Vector2(radius * 0.55f, radius * 0.55f), 10, true, new Vector3(0f, 0f, 90f));
            k.slot = Black;
            k.Box(Vector3.zero, new Vector3(width + 0.012f, radius * 0.18f, radius * 0.18f));   // hub (shows the spin)
            return k.Finish("Wheel", false);
        }

        // ------------------------------------------------------------------ scooter

        static void Scooter(Vehicle v, Material[] palette, int paint)
        {
            const float r = 0.24f, front = 0.62f, rear = -0.62f;
            var k = new MeshKit(LootVisuals.PaletteSize);
            // Rear cowl over the engine, the floorboard, the seat.
            k.slot = paint;
            k.Ellipsoid(new Vector3(0f, 0.47f, -0.42f), new Vector3(0.23f, 0.22f, 0.44f), 12, 7);
            k.Box(new Vector3(0f, 0.33f, -0.1f), new Vector3(0.34f, 0.16f, 0.5f), 0.9f, 1f);
            k.slot = Black;
            k.Box(new Vector3(0f, 0.27f, 0.15f), new Vector3(0.36f, 0.05f, 0.56f));                      // floorboard
            k.Box(new Vector3(0f, 0.22f, 0.15f), new Vector3(0.3f, 0.08f, 0.5f), 1f, 1f);
            k.Box(new Vector3(0f, 0.73f, -0.36f), new Vector3(0.3f, 0.1f, 0.62f), 0.92f, 0.95f);         // seat
            k.Box(new Vector3(0f, 0.68f, -0.36f), new Vector3(0.32f, 0.04f, 0.64f));
            k.slot = Metal;
            k.Box(new Vector3(0.12f, 0.24f, -0.55f), new Vector3(0.06f, 0.06f, 0.36f));                 // silencer
            k.Box(new Vector3(0f, 0.62f, -0.82f), new Vector3(0.22f, 0.03f, 0.12f));                    // grab rail
            k.slot = Red;
            k.Box(new Vector3(0f, 0.55f, -0.86f), new Vector3(0.14f, 0.05f, 0.03f));                    // tail lamp
            k.slot = paint;
            // Leg shield sweeping up to the steering head.
            k.Box(new Vector3(0f, 0.6f, 0.47f), new Vector3(0.42f, 0.62f, 0.07f), 0.7f, 1f, new Vector3(-14f, 0f, 0f));
            k.Box(new Vector3(0f, 0.32f, 0.44f), new Vector3(0.36f, 0.14f, 0.1f));
            k.slot = Khaki;
            k.Box(new Vector3(0f, 0.08f + 0.33f, -0.85f), new Vector3(0.18f, 0.12f, 0.02f));            // number plate
            Part("Scooter Body", v.transform, Vector3.zero, k.Finish("Scooter", false), palette);

            // Steering head: everything that turns with the bars.
            var steer = new GameObject("Steering").transform;
            steer.SetParent(v.transform, false);
            steer.localPosition = new Vector3(0f, 0f, front);
            steer.localRotation = Quaternion.identity;
            k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = paint;
            k.Box(new Vector3(0f, r + 0.1f, 0.02f), new Vector3(0.14f, 0.05f, 0.42f), 1f, 0.85f);      // mudguard
            k.Box(new Vector3(0f, 0.98f, -0.08f), new Vector3(0.24f, 0.12f, 0.16f), 0.85f, 0.9f);      // headset
            k.slot = Metal;
            k.Tube(new Vector3(0.06f, 0f, 0f), r * 0.6f, 0.92f, new Vector2(0.018f, 0.018f), new Vector2(0.02f, 0.02f), 6, false, new Vector3(-12f, 0f, 0f));
            k.Tube(new Vector3(-0.06f, 0f, 0f), r * 0.6f, 0.92f, new Vector2(0.018f, 0.018f), new Vector2(0.02f, 0.02f), 6, false, new Vector3(-12f, 0f, 0f));
            k.Tube(new Vector3(0f, 1.03f, -0.12f), -0.34f, 0.34f, new Vector2(0.014f, 0.014f), new Vector2(0.014f, 0.014f), 6, false, new Vector3(0f, 0f, 90f));   // bars
            k.Box(new Vector3(-0.22f, 1.13f, -0.08f), new Vector3(0.008f, 0.16f, 0.008f));             // mirror stems
            k.Box(new Vector3(0.22f, 1.13f, -0.08f), new Vector3(0.008f, 0.16f, 0.008f));
            k.slot = Black;
            k.Box(new Vector3(-0.31f, 1.03f, -0.12f), new Vector3(0.09f, 0.035f, 0.035f));             // grips
            k.Box(new Vector3(0.31f, 1.03f, -0.12f), new Vector3(0.09f, 0.035f, 0.035f));
            k.Ellipsoid(new Vector3(-0.22f, 1.22f, -0.08f), new Vector3(0.045f, 0.03f, 0.01f), 8, 4);
            k.Ellipsoid(new Vector3(0.22f, 1.22f, -0.08f), new Vector3(0.045f, 0.03f, 0.01f), 8, 4);
            k.slot = White;
            k.Ellipsoid(new Vector3(0f, 0.98f, 0.01f), new Vector3(0.06f, 0.05f, 0.03f), 10, 5);      // headlamp
            Part("Steering Head", steer, Vector3.zero, k.Finish("Scooter Steering", false), palette);

            var wheel = Wheel(r, 0.1f, false);
            var fw = Part("Front Wheel", steer, new Vector3(0f, r, 0f), wheel, palette).transform;
            var rw = Part("Rear Wheel", v.transform, new Vector3(0f, r, rear), wheel, palette).transform;

            v.wheels = new[] { fw, rw };
            v.steerPivot = steer;
            v.wheelRadius = r;
            v.wheelBase = front - rear;
            v.frontAxle = front;
            v.halfTrack = 0.18f;
            v.bodyHalf = new Vector3(0.24f, 0.55f, 0.9f);
            v.seatLocal = new Vector3(0f, 0.78f, -0.3f);
            v.handL = new Vector3(-0.3f, 1.04f, 0.5f);
            v.handR = new Vector3(0.3f, 1.04f, 0.5f);
            v.footLocal = new Vector3(0.13f, 0.32f, 0.2f);
        }

        // ------------------------------------------------------------------ utility cart

        static void Cart(Vehicle v, Material[] palette, int paint)
        {
            const float r = 0.27f, front = 0.95f, rear = -0.95f, track = 0.56f;
            var k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = Black;
            k.Box(new Vector3(0f, 0.4f, 0f), new Vector3(1.16f, 0.16f, 2.7f));                          // chassis
            k.slot = paint;
            k.Box(new Vector3(0f, 0.68f, 1.12f), new Vector3(1.2f, 0.42f, 0.56f), 0.95f, 0.75f);       // bonnet
            k.Box(new Vector3(0f, 0.52f, 0.45f), new Vector3(1.2f, 0.18f, 0.9f));                       // floor sides
            k.Box(new Vector3(-0.6f, 0.6f, 0.45f), new Vector3(0.04f, 0.22f, 0.9f));
            k.Box(new Vector3(0.6f, 0.6f, 0.45f), new Vector3(0.04f, 0.22f, 0.9f));
            foreach (float sx in new[] { -1f, 1f })
            {
                k.Box(new Vector3(sx * 0.56f, 0.56f, front), new Vector3(0.14f, 0.06f, 0.6f));          // mudguards
                k.Box(new Vector3(sx * 0.56f, 0.56f, rear), new Vector3(0.14f, 0.06f, 0.6f));
            }
            // Bench seat with a backrest.
            k.slot = Black;
            k.Box(new Vector3(0f, 0.74f, 0.05f), new Vector3(1.08f, 0.12f, 0.5f));
            k.Box(new Vector3(0f, 1.0f, -0.2f), new Vector3(1.08f, 0.4f, 0.1f), 1f, 1f, new Vector3(-8f, 0f, 0f));
            // Cargo bed with low sides.
            k.slot = Metal;
            k.Box(new Vector3(0f, 0.6f, -0.82f), new Vector3(1.18f, 0.05f, 1.0f));
            k.Box(new Vector3(-0.58f, 0.75f, -0.82f), new Vector3(0.03f, 0.28f, 1.0f));
            k.Box(new Vector3(0.58f, 0.75f, -0.82f), new Vector3(0.03f, 0.28f, 1.0f));
            k.Box(new Vector3(0f, 0.75f, -1.31f), new Vector3(1.18f, 0.28f, 0.03f));
            // Roof on four posts, and a windscreen frame.
            foreach (float sx in new[] { -0.55f, 0.55f })
                foreach (float sz in new[] { 0.72f, -0.28f })
                    k.Tube(new Vector3(sx, 0f, sz), 0.5f, 1.88f, new Vector2(0.022f, 0.022f), new Vector2(0.022f, 0.022f), 6, false);
            k.slot = paint;
            k.Box(new Vector3(0f, 1.9f, 0.22f), new Vector3(1.26f, 0.05f, 1.42f));
            k.slot = Black;
            k.Box(new Vector3(0f, 1.6f, 0.73f), new Vector3(1.1f, 0.025f, 0.025f));
            // Steering column and wheel (driver on the right, as on Indian roads).
            k.slot = Black;
            k.Tube(new Vector3(0.28f, 0.5f, 0.75f), 0f, 0.62f, new Vector2(0.02f, 0.02f), new Vector2(0.02f, 0.02f), 6, false, new Vector3(-35f, 0f, 0f));
            k.Tube(new Vector3(0.28f, 1.02f, 0.47f), -0.015f, 0.015f, new Vector2(0.17f, 0.17f), new Vector2(0.17f, 0.17f), 14, false, new Vector3(-55f, 0f, 0f));
            k.slot = White;
            k.Box(new Vector3(-0.42f, 0.7f, 1.39f), new Vector3(0.16f, 0.08f, 0.02f));                  // headlamps
            k.Box(new Vector3(0.42f, 0.7f, 1.39f), new Vector3(0.16f, 0.08f, 0.02f));
            k.slot = Red;
            k.Box(new Vector3(-0.5f, 0.6f, -1.34f), new Vector3(0.1f, 0.06f, 0.02f));
            k.Box(new Vector3(0.5f, 0.6f, -1.34f), new Vector3(0.1f, 0.06f, 0.02f));
            Part("Cart Body", v.transform, Vector3.zero, k.Finish("Cart", false), palette);

            var wheel = Wheel(r, 0.18f, true);
            var wheels = new Transform[4];
            var steered = new Transform[2];
            int n = 0;
            foreach (float sz in new[] { front, rear })
                foreach (float sx in new[] { -track, track })
                {
                    var pivot = new GameObject("Wheel Pivot").transform;
                    pivot.SetParent(v.transform, false);
                    pivot.localPosition = new Vector3(sx, r, sz);
                    wheels[n] = Part("Wheel", pivot, Vector3.zero, wheel, palette).transform;
                    if (sz > 0f) steered[n] = pivot;
                    n++;
                }
            v.wheels = wheels;
            v.steeredWheels = steered;
            v.wheelRadius = r;
            v.wheelBase = front - rear;
            v.frontAxle = front;
            v.halfTrack = track;
            v.bodyHalf = new Vector3(0.62f, 0.9f, 1.38f);
            v.seatLocal = new Vector3(0.28f, 0.8f, 0.05f);
            v.handL = new Vector3(0.16f, 1.06f, 0.5f);
            v.handR = new Vector3(0.4f, 1.06f, 0.5f);
            v.footLocal = new Vector3(0.13f, 0.5f, 0.55f);
        }
    }
}
