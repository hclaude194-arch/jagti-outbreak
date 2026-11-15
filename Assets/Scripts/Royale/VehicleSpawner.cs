using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Parks scooters and utility carts at the start of each match. RoyaleBuilder fills the
    /// candidate spots at build time (road edges and car parks, already cleared of buildings
    /// and props); each match picks a fresh random set, spread apart, with random paint.
    /// </summary>
    public class VehicleSpawner : MonoBehaviour
    {
        /// <summary>Candidate spots: position, and yaw in degrees in w.</summary>
        public Vector4[] spots;
        /// <summary>True where a spot is in a car park (carts are likelier there).</summary>
        public bool[] parking;
        public int scooters = 9;
        public int carts = 5;
        public float minGap = 25f;
        public Material[] palette;
        public Material smokeMaterial;

        static readonly int[] Paints = { LootVisuals.Blue, LootVisuals.Red, LootVisuals.Olive, LootVisuals.Khaki, LootVisuals.White };

        void Start()
        {
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
            int wantCarts = carts, wantScooters = scooters;
            // Carts first, preferring car parks; then scooters anywhere.
            for (int pass = 0; pass < 2 && wantCarts > 0; pass++)
                foreach (int i in order)
                {
                    if (wantCarts == 0) break;
                    if (pass == 0 && (parking == null || i >= parking.Length || !parking[i])) continue;
                    if (Place(i, Vehicle.Kind.Cart, used, rng)) wantCarts--;
                }
            foreach (int i in order)
            {
                if (wantScooters == 0) break;
                if (Place(i, Vehicle.Kind.Scooter, used, rng)) wantScooters--;
            }
        }

        bool Place(int i, Vehicle.Kind kind, List<Vector3> used, System.Random rng)
        {
            var p = new Vector3(spots[i].x, spots[i].y, spots[i].z);
            foreach (var u in used)
                if ((u - p).sqrMagnitude < minGap * minGap) return false;
            used.Add(p);
            var go = new GameObject(kind == Vehicle.Kind.Scooter ? "Scooter" : "Utility Cart");
            go.transform.SetParent(transform, false);
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, spots[i].w, 0f);
            var v = go.AddComponent<Vehicle>();
            v.kind = kind;
            v.palette = palette;
            v.smokeMaterial = smokeMaterial;
            v.paint = Paints[rng.Next(Paints.Length)];
            return true;
        }
    }
}
