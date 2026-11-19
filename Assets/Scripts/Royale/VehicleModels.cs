using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The scooter, the utility cart, the auto-rickshaw, the security patrol 4x4 and the rooftop helicopter, built in code from the loot palette:
    /// one mesh for the body (submeshes by palette slot), separate wheels that spin, and on the
    /// scooter a steering head (fork, front wheel, handlebar, headlamp) that turns.
    /// Model space: origin on the ground between the axles, +Z forward.
    /// </summary>
    public static class VehicleModels
    {
        const int Metal = LootVisuals.Metal, Black = LootVisuals.Black, Red = LootVisuals.Red,
                  White = LootVisuals.White, Khaki = LootVisuals.Khaki, Blue = LootVisuals.Blue, Yellow = LootVisuals.Yellow,
                  Green = LootVisuals.Green;

        public static void Build(Vehicle v, Material[] palette, int paint)
        {
            if (v.kind == Vehicle.Kind.Scooter) Scooter(v, palette, paint);
            else if (v.kind == Vehicle.Kind.Auto) Auto(v, palette, paint);
            else if (v.kind == Vehicle.Kind.Jeep) Jeep(v, palette, paint);
            else if (v.kind == Vehicle.Kind.Heli) Heli(v, palette, paint);
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

        // ------------------------------------------------------------------ auto-rickshaw

        static void Auto(Vehicle v, Material[] palette, int paint)
        {
            const float r = 0.2f, front = 0.95f, rear = -0.75f, track = 0.55f;
            var k = new MeshKit(LootVisuals.PaletteSize);
            // Floor pan, the nose, the lower sides and the engine box at the back.
            k.slot = Black;
            k.Box(new Vector3(0f, 0.36f, -0.1f), new Vector3(1.2f, 0.08f, 1.9f));
            k.slot = paint;
            k.Box(new Vector3(0f, 0.6f, 0.98f), new Vector3(0.72f, 0.5f, 0.32f), 0.85f, 0.7f);
            k.Box(new Vector3(0f, 0.6f, 0.66f), new Vector3(1.0f, 0.42f, 0.36f), 1f, 0.8f);
            k.Box(new Vector3(-0.6f, 0.6f, -0.25f), new Vector3(0.04f, 0.42f, 1.5f));
            k.Box(new Vector3(0.6f, 0.6f, -0.25f), new Vector3(0.04f, 0.42f, 1.5f));
            k.Box(new Vector3(0f, 0.72f, -0.85f), new Vector3(1.2f, 0.72f, 0.36f));
            k.Box(new Vector3(0f, 0.97f, 0.8f), new Vector3(1.02f, 0.08f, 0.1f));
            // Windscreen, wiper, and the yellow canopy on its posts with a domed roof.
            k.slot = Blue;
            k.Box(new Vector3(0f, 1.28f, 0.8f), new Vector3(0.94f, 0.52f, 0.03f), 1f, 1f, new Vector3(-8f, 0f, 0f));
            k.slot = Black;
            k.Box(new Vector3(0.1f, 1.12f, 0.835f), new Vector3(0.36f, 0.02f, 0.015f), 1f, 1f, new Vector3(0f, 0f, 20f));
            k.slot = Metal;
            foreach (float sx in new[] { -0.6f, 0.6f })
            {
                k.Tube(new Vector3(sx, 0f, 0.78f), 0.95f, 1.7f, new Vector2(0.02f, 0.02f), new Vector2(0.02f, 0.02f), 6, false);
                k.Tube(new Vector3(sx, 0f, -1.0f), 1.05f, 1.7f, new Vector2(0.02f, 0.02f), new Vector2(0.02f, 0.02f), 6, false);
                k.Box(new Vector3(sx, 1.15f, -0.4f), new Vector3(0.025f, 0.025f, 0.7f));
            }
            k.slot = Yellow;
            k.Box(new Vector3(0f, 1.72f, -0.1f), new Vector3(1.28f, 0.06f, 1.95f));
            k.Ellipsoid(new Vector3(0f, 1.74f, -0.1f), new Vector3(0.64f, 0.12f, 0.98f), 14, 4, 0f, 90f);
            k.Box(new Vector3(0f, 1.38f, -1.02f), new Vector3(1.24f, 0.7f, 0.04f));
            k.slot = Black;
            k.Box(new Vector3(0f, 1.38f, -1.045f), new Vector3(0.7f, 0.28f, 0.01f));
            k.Box(new Vector3(-0.64f, 1.69f, -0.1f), new Vector3(0.02f, 0.05f, 1.95f));
            k.Box(new Vector3(0.64f, 1.69f, -0.1f), new Vector3(0.02f, 0.05f, 1.95f));
            // Driver's saddle, the passenger bench and its back.
            k.Box(new Vector3(0f, 0.8f, 0.35f), new Vector3(0.42f, 0.12f, 0.36f), 0.9f, 0.9f);
            k.Box(new Vector3(0f, 0.68f, -0.4f), new Vector3(1.08f, 0.14f, 0.46f));
            k.Box(new Vector3(0f, 0.98f, -0.64f), new Vector3(1.08f, 0.42f, 0.1f), 1f, 1f, new Vector3(-6f, 0f, 0f));
            k.slot = Metal;
            k.Box(new Vector3(0f, 1.12f, -0.15f), new Vector3(1.0f, 0.025f, 0.025f));
            k.Box(new Vector3(0.36f, 0.32f, -0.98f), new Vector3(0.07f, 0.07f, 0.34f));
            // Lamps and the number plate.
            k.slot = White;
            k.Ellipsoid(new Vector3(0f, 0.86f, 1.15f), new Vector3(0.08f, 0.07f, 0.04f), 10, 5);
            k.slot = Yellow;
            k.Box(new Vector3(-0.42f, 0.86f, 0.86f), new Vector3(0.06f, 0.04f, 0.02f));
            k.Box(new Vector3(0.42f, 0.86f, 0.86f), new Vector3(0.06f, 0.04f, 0.02f));
            k.slot = Red;
            k.Box(new Vector3(-0.5f, 0.82f, -1.035f), new Vector3(0.1f, 0.07f, 0.02f));
            k.Box(new Vector3(0.5f, 0.82f, -1.035f), new Vector3(0.1f, 0.07f, 0.02f));
            k.slot = Khaki;
            k.Box(new Vector3(0f, 0.62f, -1.04f), new Vector3(0.3f, 0.12f, 0.02f));
            Part("Auto Body", v.transform, Vector3.zero, k.Finish("Auto", false), palette);

            // The single front wheel turns with the handlebar.
            var steer = new GameObject("Steering").transform;
            steer.SetParent(v.transform, false);
            steer.localPosition = new Vector3(0f, 0f, front);
            steer.localRotation = Quaternion.identity;
            k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = paint;
            k.Box(new Vector3(0f, r + 0.09f, 0.02f), new Vector3(0.14f, 0.05f, 0.36f), 1f, 0.85f);
            k.slot = Metal;
            k.Tube(new Vector3(0.05f, 0f, 0f), r * 0.6f, 0.56f, new Vector2(0.016f, 0.016f), new Vector2(0.018f, 0.018f), 6, false, new Vector3(-10f, 0f, 0f));
            k.Tube(new Vector3(-0.05f, 0f, 0f), r * 0.6f, 0.56f, new Vector2(0.016f, 0.016f), new Vector2(0.018f, 0.018f), 6, false, new Vector3(-10f, 0f, 0f));
            k.Tube(new Vector3(0f, 0.5f, -0.1f), 0f, 0.55f, new Vector2(0.02f, 0.02f), new Vector2(0.02f, 0.02f), 6, false, new Vector3(-30f, 0f, 0f));
            k.Tube(new Vector3(0f, 1.0f, -0.36f), -0.32f, 0.32f, new Vector2(0.014f, 0.014f), new Vector2(0.014f, 0.014f), 6, false, new Vector3(0f, 0f, 90f));
            k.slot = Black;
            k.Box(new Vector3(-0.3f, 1.0f, -0.36f), new Vector3(0.09f, 0.035f, 0.035f));
            k.Box(new Vector3(0.3f, 1.0f, -0.36f), new Vector3(0.09f, 0.035f, 0.035f));
            Part("Steering Head", steer, Vector3.zero, k.Finish("Auto Steering", false), palette);

            var wheel = Wheel(r, 0.12f, false);
            var fw = Part("Front Wheel", steer, new Vector3(0f, r, 0f), wheel, palette).transform;
            var rl = Part("Rear Wheel", v.transform, new Vector3(-track, r, rear), wheel, palette).transform;
            var rr = Part("Rear Wheel", v.transform, new Vector3(track, r, rear), wheel, palette).transform;
            v.wheels = new[] { fw, rl, rr };
            v.steerPivot = steer;
            v.wheelRadius = r;
            v.wheelBase = front - rear;
            v.frontAxle = front;
            v.halfTrack = track;
            v.bodyHalf = new Vector3(0.64f, 0.88f, 1.25f);
            v.seatLocal = new Vector3(0f, 0.88f, 0.33f);
            v.handL = new Vector3(-0.3f, 1.0f, 0.59f);
            v.handR = new Vector3(0.3f, 1.0f, 0.59f);
            v.footLocal = new Vector3(0.12f, 0.42f, 0.62f);
        }

        // ------------------------------------------------------------------ patrol 4x4 (Kind.Jeep)

        static void Jeep(Vehicle v, Material[] palette, int paint)
        {
            const float r = 0.36f, front = 1.3f, rear = -1.2f, track = 0.74f;
            var k = new MeshKit(LootVisuals.PaletteSize);
            // Chassis, the open tub, the bonnet and the wings.
            k.slot = Black;
            k.Box(new Vector3(0f, 0.52f, 0f), new Vector3(1.4f, 0.2f, 3.5f));
            k.slot = paint;
            k.Box(new Vector3(0f, 0.86f, -0.4f), new Vector3(1.66f, 0.56f, 2.5f));
            k.Box(new Vector3(0f, 0.96f, 1.28f), new Vector3(1.5f, 0.36f, 0.96f), 1f, 0.97f);
            foreach (float sx in new[] { -1f, 1f })
            {
                k.Box(new Vector3(sx * 0.8f, 1.0f, 1.3f), new Vector3(0.32f, 0.07f, 0.95f));
                k.Box(new Vector3(sx * 0.83f, 1.12f, -1.2f), new Vector3(0.26f, 0.06f, 0.9f));
            }
            // The tub's inside, seen from above.
            k.slot = Black;
            k.Box(new Vector3(0f, 1.11f, -0.4f), new Vector3(1.5f, 0.03f, 2.34f));
            // Grille with slats, headlamps, bumpers, a step on each side.
            k.Box(new Vector3(0f, 0.92f, 1.77f), new Vector3(1.1f, 0.4f, 0.04f));
            k.slot = Metal;
            foreach (float y in new[] { 0.8f, 0.88f, 0.96f, 1.04f })
                k.Box(new Vector3(0f, y, 1.795f), new Vector3(1.0f, 0.035f, 0.02f));
            k.Box(new Vector3(0f, 0.92f, 1.8f), new Vector3(0.05f, 0.38f, 0.02f));
            k.Box(new Vector3(0f, 0.56f, 1.85f), new Vector3(1.72f, 0.14f, 0.12f));
            k.Box(new Vector3(0f, 0.56f, -1.7f), new Vector3(1.72f, 0.14f, 0.1f));
            k.slot = White;
            k.Ellipsoid(new Vector3(-0.58f, 0.98f, 1.77f), new Vector3(0.1f, 0.1f, 0.04f), 12, 5);
            k.Ellipsoid(new Vector3(0.58f, 0.98f, 1.77f), new Vector3(0.1f, 0.1f, 0.04f), 12, 5);
            k.slot = Black;
            k.Box(new Vector3(-0.88f, 0.66f, 0.05f), new Vector3(0.14f, 0.04f, 1.1f));
            k.Box(new Vector3(0.88f, 0.66f, 0.05f), new Vector3(0.14f, 0.04f, 1.1f));
            // Windscreen in a frame, the dashboard and the wheel (driver on the right).
            k.slot = Blue;
            k.Box(new Vector3(0f, 1.48f, 0.66f), new Vector3(1.44f, 0.5f, 0.03f), 1f, 1f, new Vector3(-10f, 0f, 0f));
            k.slot = Black;
            k.Box(new Vector3(0f, 1.74f, 0.62f), new Vector3(1.56f, 0.05f, 0.06f));
            k.Box(new Vector3(-0.76f, 1.47f, 0.66f), new Vector3(0.05f, 0.56f, 0.06f), 1f, 1f, new Vector3(-10f, 0f, 0f));
            k.Box(new Vector3(0.76f, 1.47f, 0.66f), new Vector3(0.05f, 0.56f, 0.06f), 1f, 1f, new Vector3(-10f, 0f, 0f));
            k.Box(new Vector3(0f, 1.2f, 0.52f), new Vector3(1.5f, 0.14f, 0.22f));
            k.Tube(new Vector3(0.38f, 1.12f, 0.5f), 0f, 0.3f, new Vector2(0.02f, 0.02f), new Vector2(0.02f, 0.02f), 6, false, new Vector3(-50f, 0f, 0f));
            k.Tube(new Vector3(0.38f, 1.34f, 0.32f), -0.015f, 0.015f, new Vector2(0.18f, 0.18f), new Vector2(0.18f, 0.18f), 14, false, new Vector3(-60f, 0f, 0f));
            // Two bucket seats and a rear bench.
            foreach (float sx in new[] { -0.38f, 0.38f })
            {
                k.Box(new Vector3(sx, 1.2f, 0.0f), new Vector3(0.5f, 0.14f, 0.5f));
                k.Box(new Vector3(sx, 1.48f, -0.24f), new Vector3(0.48f, 0.5f, 0.1f), 0.9f, 1f, new Vector3(-8f, 0f, 0f));
            }
            k.Box(new Vector3(0f, 1.2f, -0.98f), new Vector3(1.3f, 0.14f, 0.5f));
            k.Box(new Vector3(0f, 1.48f, -1.24f), new Vector3(1.3f, 0.48f, 0.1f), 1f, 1f, new Vector3(-6f, 0f, 0f));
            // Roll bar over the back seats.
            k.slot = Metal;
            foreach (float sx in new[] { -0.76f, 0.76f })
            {
                k.Tube(new Vector3(sx, 0f, -0.62f), 1.1f, 2.02f, new Vector2(0.035f, 0.035f), new Vector2(0.035f, 0.035f), 8, false);
                k.Tube(new Vector3(sx, 2.0f, -0.62f), 0f, 0.98f, new Vector2(0.03f, 0.03f), new Vector2(0.03f, 0.03f), 8, false, new Vector3(-125f, 0f, 0f));
            }
            k.Tube(new Vector3(0f, 2.02f, -0.62f), -0.78f, 0.78f, new Vector2(0.035f, 0.035f), new Vector2(0.035f, 0.035f), 8, false, new Vector3(0f, 0f, 90f));
            // The spare wheel on the tailgate, lamps and the number plate.
            k.slot = Black;
            k.Tube(new Vector3(0f, 1.0f, -1.72f), -0.12f, 0.12f, new Vector2(0.34f, 0.34f), new Vector2(0.34f, 0.34f), 14, true, new Vector3(90f, 0f, 0f));
            k.slot = Metal;
            k.Tube(new Vector3(0f, 1.0f, -1.72f), -0.125f, 0.125f, new Vector2(0.18f, 0.18f), new Vector2(0.18f, 0.18f), 10, true, new Vector3(90f, 0f, 0f));
            k.slot = Red;
            k.Box(new Vector3(-0.7f, 0.92f, -1.66f), new Vector3(0.12f, 0.12f, 0.02f));
            k.Box(new Vector3(0.7f, 0.92f, -1.66f), new Vector3(0.12f, 0.12f, 0.02f));
            k.slot = Khaki;
            k.Box(new Vector3(0f, 0.7f, -1.66f), new Vector3(0.36f, 0.12f, 0.02f));
            k.Box(new Vector3(0f, 0.58f, 1.92f), new Vector3(0.36f, 0.1f, 0.02f));
            Part("Jeep Body", v.transform, Vector3.zero, k.Finish("Jeep", false), palette);

            var wheel = Wheel(r, 0.26f, false);
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
            v.bodyHalf = new Vector3(0.86f, 1.0f, 1.85f);
            v.seatLocal = new Vector3(0.38f, 1.28f, 0.0f);
            v.handL = new Vector3(0.22f, 1.42f, 0.36f);
            v.handR = new Vector3(0.54f, 1.42f, 0.36f);
            v.footLocal = new Vector3(0.15f, 0.72f, 0.62f);
        }

        // ------------------------------------------------------------------ helicopter (Kind.Heli)

        // A light utility helicopter of no particular make: a rounded cabin with a big glazed nose, an
        // engine hump, a tapering tail boom with a fin and tail rotor, skids, and a four-blade rotor.
        // The rotor and the tail rotor are separate parts that spin. Paint is the body colour; the
        // stripes are fixed red. About 9.6 m long, 9.4 m across the rotor.
        static void Heli(Vehicle v, Material[] palette, int paint)
        {
            const float rotorY = 2.95f, rotorZ = 0.15f, blade = 4.7f;   // blade: hub to tip
            var k = new MeshKit(LootVisuals.PaletteSize);
            // Cabin, engine hump, belly.
            k.slot = paint;
            k.Ellipsoid(new Vector3(0f, 1.5f, 0.35f), new Vector3(0.98f, 0.92f, 1.75f), 16, 9);
            k.Box(new Vector3(0f, 2.28f, -0.15f), new Vector3(0.95f, 0.5f, 1.9f), 0.8f, 0.85f);
            k.Box(new Vector3(0f, 0.86f, 0.2f), new Vector3(1.2f, 0.22f, 2.2f), 1f, 0.9f);
            // Glazed nose and side windows.
            k.slot = Blue;
            k.Ellipsoid(new Vector3(0f, 1.66f, 1.12f), new Vector3(0.9f, 0.74f, 1.08f), 16, 8, -20f, 90f);
            k.Box(new Vector3(-0.93f, 1.72f, 0.05f), new Vector3(0.06f, 0.56f, 1.0f), 1f, 0.94f);
            k.Box(new Vector3(0.93f, 1.72f, 0.05f), new Vector3(0.06f, 0.56f, 1.0f), 1f, 0.94f);
            // Red band round the cabin and down the boom.
            k.slot = Red;
            k.Ellipsoid(new Vector3(0f, 1.5f, 0.35f), new Vector3(0.995f, 0.935f, 1.765f), 16, 2, -24f, -12f);
            k.Box(new Vector3(0f, 1.76f, -2.6f), new Vector3(0.5f, 0.1f, 2.6f), 0.6f, 1f);
            // Tail boom, fin, tailplane.
            k.slot = paint;
            k.Tube(new Vector3(0f, 1.78f, -0.9f), 0f, 4.7f, new Vector2(0.36f, 0.36f), new Vector2(0.13f, 0.13f), 12, true, new Vector3(-90f, 0f, 0f));
            k.Box(new Vector3(0f, 2.35f, -5.4f), new Vector3(0.08f, 1.25f, 0.62f), 1f, 0.7f, new Vector3(-12f, 0f, 0f));
            k.Box(new Vector3(0f, 1.8f, -4.75f), new Vector3(1.5f, 0.06f, 0.42f), 1f, 0.8f);
            k.Box(new Vector3(-0.75f, 1.9f, -4.75f), new Vector3(0.04f, 0.26f, 0.36f));
            k.Box(new Vector3(0.75f, 1.9f, -4.75f), new Vector3(0.04f, 0.26f, 0.36f));
            // Exhaust, mast, landing light and the position lights.
            k.slot = Black;
            k.Tube(new Vector3(0.35f, 2.42f, -1.0f), 0f, 0.5f, new Vector2(0.12f, 0.12f), new Vector2(0.14f, 0.14f), 8, true, new Vector3(-100f, 0f, 0f));
            k.Tube(new Vector3(-0.35f, 2.42f, -1.0f), 0f, 0.5f, new Vector2(0.12f, 0.12f), new Vector2(0.14f, 0.14f), 8, true, new Vector3(-100f, 0f, 0f));
            k.slot = Metal;
            k.Tube(new Vector3(0f, 2.5f, rotorZ), 0f, rotorY - 2.5f, new Vector2(0.09f, 0.09f), new Vector2(0.07f, 0.07f), 8, false);
            k.slot = White;
            k.Ellipsoid(new Vector3(0f, 0.8f, 1.5f), new Vector3(0.1f, 0.06f, 0.1f), 8, 4);
            k.slot = Red;
            k.Box(new Vector3(-0.79f, 1.82f, -4.75f), new Vector3(0.06f, 0.06f, 0.08f));
            k.slot = Green;
            k.Box(new Vector3(0.79f, 1.82f, -4.75f), new Vector3(0.06f, 0.06f, 0.08f));
            // Skids on raked struts.
            k.slot = Metal;
            foreach (float sx in new[] { -1f, 1f })
            {
                k.Tube(new Vector3(sx * 0.95f, 0.06f, -1.25f), 0f, 2.75f, new Vector2(0.05f, 0.05f), new Vector2(0.05f, 0.05f), 8, true, new Vector3(90f, 0f, 0f));
                k.Tube(new Vector3(sx * 0.95f, 0.06f, 1.5f), 0f, 0.36f, new Vector2(0.05f, 0.05f), new Vector2(0.05f, 0.05f), 8, true, new Vector3(30f, 0f, 0f));
                k.Tube(new Vector3(sx * 0.95f, 0.06f, 0.95f), 0f, 0.86f, new Vector2(0.04f, 0.04f), new Vector2(0.04f, 0.04f), 6, false, new Vector3(0f, 0f, sx * 28f));
                k.Tube(new Vector3(sx * 0.95f, 0.06f, -0.6f), 0f, 0.86f, new Vector2(0.04f, 0.04f), new Vector2(0.04f, 0.04f), 6, false, new Vector3(0f, 0f, sx * 28f));
            }
            // Seats and the instrument panel (seen through the nose from close by).
            k.slot = Black;
            k.Box(new Vector3(0.38f, 1.08f, 0.95f), new Vector3(0.5f, 0.12f, 0.5f));
            k.Box(new Vector3(-0.38f, 1.08f, 0.95f), new Vector3(0.5f, 0.12f, 0.5f));
            k.Box(new Vector3(0f, 1.3f, 1.62f), new Vector3(1.4f, 0.3f, 0.3f));
            Part("Heli Body", v.transform, Vector3.zero, k.Finish("Helicopter", false), palette);

            // Main rotor: hub and four blades, turning about the mast.
            k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = Black;
            k.Box(Vector3.zero, new Vector3(0.42f, 0.16f, 0.42f));
            foreach (float a in new[] { 0f, 90f })
                k.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.26f, 0.03f, blade * 2f), 1f, 1f, new Vector3(0f, a, 0f));
            var rotor = Part("Main Rotor", v.transform, new Vector3(0f, rotorY, rotorZ), k.Finish("Helicopter Rotor", false), palette).transform;

            // Tail rotor on the left of the fin, turning about the sideways axis.
            k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = Black;
            k.Box(Vector3.zero, new Vector3(0.12f, 0.14f, 0.14f));
            k.Box(Vector3.zero, new Vector3(0.03f, 1.3f, 0.12f));
            k.Box(Vector3.zero, new Vector3(0.03f, 0.12f, 1.3f));
            var tail = Part("Tail Rotor", v.transform, new Vector3(-0.14f, 2.45f, -5.45f), k.Finish("Helicopter Tail Rotor", false), palette).transform;

            v.rotor = rotor;
            v.tailRotor = tail;
            v.wheels = null;
            v.wheelRadius = 0.3f;
            v.wheelBase = 2.6f;
            v.frontAxle = 1.3f;
            v.halfTrack = 0.95f;
            v.bodyHalf = new Vector3(1.0f, 1.3f, 2.2f);
            v.seatLocal = new Vector3(0.38f, 1.14f, 0.95f);
            v.handL = new Vector3(0.24f, 1.36f, 1.4f);
            v.handR = new Vector3(0.52f, 1.36f, 1.4f);
            v.footLocal = new Vector3(0.14f, 0.8f, 1.65f);
        }
    }
}
