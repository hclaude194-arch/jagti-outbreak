using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A rooftop helipad (RoyaleBuilder puts one on the academic block's south wing): a low steel
    /// deck with a white edge line, the yellow touchdown ring and a white H, edge lights and a
    /// windsock. VehicleSpawner parks the helicopter on it at the start of each match. The deck is
    /// built in code from the loot palette, like the zip line towers.
    /// </summary>
    public class Helipad : MonoBehaviour
    {
        public static readonly List<Helipad> All = new List<Helipad>();

        /// <summary>Deck height above the roof: a step the walker can take (its step offset is 0.35 m).</summary>
        public const float Deck = 0.22f;

        public float size = 15f;
        public Material[] palette;

        /// <summary>The deck's top in the middle, where the helicopter sits.</summary>
        public Vector3 Top { get { return transform.position + Vector3.up * Deck; } }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Start()
        {
            Build();
        }

        void Build()
        {
            const int Metal = LootVisuals.Metal, Black = LootVisuals.Black, White = LootVisuals.White,
                      Yellow = LootVisuals.Yellow, Red = LootVisuals.Red;
            float h = size / 2f, top = Deck + 0.004f;
            var k = new MeshKit(LootVisuals.PaletteSize);
            // The deck, on a darker skirt so its edge reads from the air.
            k.slot = Metal;
            k.Box(new Vector3(0f, Deck / 2f, 0f), new Vector3(size, Deck, size));
            k.slot = Black;
            k.Box(new Vector3(0f, Deck * 0.3f, 0f), new Vector3(size + 0.12f, Deck * 0.6f, size + 0.12f));
            // White edge line.
            k.slot = White;
            k.Box(new Vector3(0f, top, h - 0.35f), new Vector3(size - 0.4f, 0.01f, 0.3f));
            k.Box(new Vector3(0f, top, -h + 0.35f), new Vector3(size - 0.4f, 0.01f, 0.3f));
            k.Box(new Vector3(h - 0.35f, top, 0f), new Vector3(0.3f, 0.01f, size - 0.4f));
            k.Box(new Vector3(-h + 0.35f, top, 0f), new Vector3(0.3f, 0.01f, size - 0.4f));
            // Yellow touchdown ring, in short segments.
            k.slot = Yellow;
            float ring = h * 0.62f;
            for (int i = 0; i < 36; i++)
            {
                float a = i * 10f;
                k.Box(new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * ring, top, Mathf.Cos(a * Mathf.Deg2Rad) * ring),
                      new Vector3(0.45f, 0.012f, ring * 0.175f + 0.02f), 1f, 1f, new Vector3(0f, a + 90f, 0f));
            }
            // The H.
            k.slot = White;
            k.Box(new Vector3(-1.2f, top + 0.002f, 0f), new Vector3(0.6f, 0.012f, 4.0f));
            k.Box(new Vector3(1.2f, top + 0.002f, 0f), new Vector3(0.6f, 0.012f, 4.0f));
            k.Box(new Vector3(0f, top + 0.002f, 0f), new Vector3(1.8f, 0.012f, 0.6f));
            // Edge lights round the deck.
            k.slot = Yellow;
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    float u = -h + 0.6f + j * (size - 1.2f) / 3f;
                    Vector3 p = i == 0 ? new Vector3(u, 0f, h - 0.15f) : i == 1 ? new Vector3(u, 0f, -h + 0.15f)
                              : i == 2 ? new Vector3(h - 0.15f, 0f, u) : new Vector3(-h + 0.15f, 0f, u);
                    p.y = Deck + 0.06f;
                    k.Box(p, new Vector3(0.14f, 0.12f, 0.14f));
                }
            // Windsock on a pole at one corner (off the deck so it is never in the rotor's way).
            k.slot = Metal;
            k.Tube(new Vector3(h + 0.6f, 0f, h + 0.6f), 0f, 3.2f, new Vector2(0.05f, 0.05f), new Vector2(0.04f, 0.04f), 8, false);
            k.slot = Red;
            k.Tube(new Vector3(h + 0.6f, 3.05f, h + 0.6f), 0f, 1.4f, new Vector2(0.22f, 0.22f), new Vector2(0.1f, 0.1f), 10, false, new Vector3(0f, 0f, -80f));
            k.slot = White;
            k.Tube(new Vector3(h + 0.6f + 0.47f, 3.13f, h + 0.6f), 0f, 0.3f, new Vector2(0.185f, 0.185f), new Vector2(0.16f, 0.16f), 10, false, new Vector3(0f, 0f, -80f));

            var go = new GameObject("Helipad Deck");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.Finish("Helipad", false);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = palette;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, Deck / 2f, 0f);
            col.size = new Vector3(size, Deck, size);
        }
    }
}
