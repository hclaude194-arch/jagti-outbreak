using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Parks scooters, auto-rickshaws, utility carts and patrol 4x4s at the start of each match. RoyaleBuilder fills the
    /// candidate spots at build time (road edges and car parks, already cleared of buildings
    /// and props); each match picks a fresh random set, spread apart, with random paint.
    /// A helicopter waits on every rooftop helipad.
    /// </summary>
    public class VehicleSpawner : MonoBehaviour
    {
        /// <summary>Candidate spots: position, and yaw in degrees in w.</summary>
        public Vector4[] spots;
        /// <summary>True where a spot is in a car park (carts are likelier there).</summary>
        public bool[] parking;
        public int scooters = 8;
        public int autos = 5;
        public int carts = 4;
        public int jeeps = 3;
        public float minGap = 25f;
        public Material[] palette;
        public Material smokeMaterial;
        /// <summary>Rooftop pads that each get a helicopter.</summary>
        public Helipad[] helipads;

        static readonly int[] Paints = { LootVisuals.Blue, LootVisuals.Red, LootVisuals.Olive, LootVisuals.Khaki, LootVisuals.White };
        static readonly int[] AutoPaints = { LootVisuals.Green, LootVisuals.Green, LootVisuals.Black, LootVisuals.Yellow };
        static readonly int[] JeepPaints = { LootVisuals.Olive, LootVisuals.White, LootVisuals.Black, LootVisuals.Red, LootVisuals.Blue };

        void Start()
        {
            if (helipads != null)
                foreach (var pad in helipads)
                {
                    if (pad == null) continue;
                    var go = new GameObject("Helicopter");
                    go.transform.SetParent(transform, false);
                    go.transform.position = pad.Top + Vector3.up * 0.05f;
                    go.transform.rotation = pad.transform.rotation;
                    var heli = go.AddComponent<Vehicle>();
                    heli.kind = Vehicle.Kind.Heli;
                    heli.palette = palette;
                    heli.smokeMaterial = smokeMaterial;
                    heli.paint = LootVisuals.White;
                }
            if (spots == null || spots.Length == 0) return;
            var rng = new System.Random(System.Environment.TickCount ^ 0x5bd1);
            var order = new List<int>();
            for (int i = 0; i < spots.Length; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }
            var used = new List<Vector3>();
            // The big ones first, preferring car parks; then the small ones anywhere.
            PlaceSome(Vehicle.Kind.Jeep, jeeps, true, order, used, rng);
            PlaceSome(Vehicle.Kind.Cart, carts, true, order, used, rng);
            PlaceSome(Vehicle.Kind.Auto, autos, false, order, used, rng);
            PlaceSome(Vehicle.Kind.Scooter, scooters, false, order, used, rng);
        }

        void PlaceSome(Vehicle.Kind kind, int want, bool parkingFirst, List<int> order, List<Vector3> used, System.Random rng)
        {
            for (int pass = parkingFirst ? 0 : 1; pass < 2 && want > 0; pass++)
                foreach (int i in order)
                {
                    if (want == 0) break;
                    if (pass == 0 && (parking == null || i >= parking.Length || !parking[i])) continue;
                    if (Place(i, kind, used, rng)) want--;
                }
        }

        bool Place(int i, Vehicle.Kind kind, List<Vector3> used, System.Random rng)
        {
            var p = new Vector3(spots[i].x, spots[i].y, spots[i].z);
            foreach (var u in used)
                if ((u - p).sqrMagnitude < minGap * minGap) return false;
            used.Add(p);
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(transform, false);
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, spots[i].w, 0f);
            var v = go.AddComponent<Vehicle>();
            v.kind = kind;
            v.palette = palette;
            v.smokeMaterial = smokeMaterial;
            var paints = kind == Vehicle.Kind.Auto ? AutoPaints : kind == Vehicle.Kind.Jeep ? JeepPaints : Paints;
            v.paint = paints[rng.Next(paints.Length)];
            return true;
        }
    }
}
