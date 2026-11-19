using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The loot stash: a painted steel trunk of the kind every hostel room has, with brass corners, side
    /// handles and a padlocked hasp. Built in code with MeshKit in the LootVisuals palette.
    /// Model space: origin on the floor under the middle, front +Z, 1 m along X. The lid is its own mesh
    /// with its origin on the hinge (back top edge, at LidHinge) so it can swing open about X.
    /// tools/royale/preview_stash.py translates this file to Python to render it (through
    /// preview_vehicles.py), so keep it to MeshKit calls, simple declarations, foreach over literal
    /// arrays and plain for loops.
    /// </summary>
    public static class StashModel
    {
        public const float Width = 1.0f, Height = 0.42f, Depth = 0.55f;
        public static readonly Vector3 LidHinge = new Vector3(0f, 0.42f, -0.275f);

        static Mesh body, lid;

        public static Mesh Body()
        {
            if (body == null) body = BuildBody();
            return body;
        }

        public static Mesh Lid()
        {
            if (lid == null) lid = BuildLid();
            return lid;
        }

        static Mesh BuildBody()
        {
            var k = new MeshKit(LootVisuals.PaletteSize);
            // The painted box: four walls round a dark lining (seen when the lid is up), a darker plinth,
            // two pressed ribs round it and a rim along the top edge.
            k.slot = LootVisuals.Blue;
            k.Box(new Vector3(0f, 0.22f, 0.26f), new Vector3(1.0f, 0.4f, 0.03f));
            k.Box(new Vector3(0f, 0.22f, -0.26f), new Vector3(1.0f, 0.4f, 0.03f));
            k.Box(new Vector3(0.485f, 0.22f, 0f), new Vector3(0.03f, 0.4f, 0.49f));
            k.Box(new Vector3(-0.485f, 0.22f, 0f), new Vector3(0.03f, 0.4f, 0.49f));
            k.slot = LootVisuals.Black;
            k.Box(new Vector3(0f, 0.08f, 0f), new Vector3(0.94f, 0.08f, 0.49f));
            k.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.98f, 0.04f, 0.53f));
            k.slot = LootVisuals.Metal;
            foreach (float x in new[] { -0.3f, 0.3f })
            {
                k.Box(new Vector3(x, 0.22f, 0.277f), new Vector3(0.04f, 0.4f, 0.012f));
                k.Box(new Vector3(x, 0.22f, -0.277f), new Vector3(0.04f, 0.4f, 0.012f));
            }
            k.Box(new Vector3(0f, 0.41f, 0.265f), new Vector3(1.01f, 0.025f, 0.035f));
            k.Box(new Vector3(0f, 0.41f, -0.265f), new Vector3(1.01f, 0.025f, 0.035f));
            k.Box(new Vector3(0.49f, 0.41f, 0f), new Vector3(0.035f, 0.025f, 0.5f));
            k.Box(new Vector3(-0.49f, 0.41f, 0f), new Vector3(0.035f, 0.025f, 0.5f));
            // Brass corner caps.
            k.slot = LootVisuals.Brass;
            foreach (float x in new[] { -0.485f, 0.485f })
                foreach (float z in new[] { -0.26f, 0.26f })
                {
                    k.Box(new Vector3(x, 0.06f, z), new Vector3(0.05f, 0.1f, 0.05f));
                    k.Box(new Vector3(x, 0.37f, z), new Vector3(0.05f, 0.08f, 0.05f));
                }
            // The hasp and its padlock, hanging open.
            k.Box(new Vector3(0f, 0.34f, 0.282f), new Vector3(0.1f, 0.1f, 0.016f));
            k.Box(new Vector3(0f, 0.25f, 0.3f), new Vector3(0.07f, 0.07f, 0.03f));
            k.slot = LootVisuals.Metal;
            k.Tube(new Vector3(0f, 0.3f, 0.3f), 0f, 0.05f, new Vector2(0.025f, 0.008f), new Vector2(0.025f, 0.008f), 6, false);
            // Side handles.
            k.slot = LootVisuals.Black;
            foreach (float s in new[] { -1f, 1f })
            {
                k.Box(new Vector3(s * 0.51f, 0.28f, 0f), new Vector3(0.025f, 0.03f, 0.2f));
                k.Box(new Vector3(s * 0.505f, 0.3f, -0.09f), new Vector3(0.02f, 0.05f, 0.02f));
                k.Box(new Vector3(s * 0.505f, 0.3f, 0.09f), new Vector3(0.02f, 0.05f, 0.02f));
            }
            // A pale stencilled band on the front, where owners paint their names.
            k.slot = LootVisuals.Khaki;
            k.Box(new Vector3(0f, 0.15f, 0.277f), new Vector3(0.52f, 0.09f, 0.006f));
            return k.Finish("Stash_Body", false);
        }

        static Mesh BuildLid()
        {
            var k = new MeshKit(LootVisuals.PaletteSize);
            // In hinge space: the lid runs forward (+Z) from the hinge line.
            k.slot = LootVisuals.Blue;
            k.Box(new Vector3(0f, 0.045f, 0.28f), new Vector3(1.02f, 0.09f, 0.57f), 0.97f, 0.94f);
            k.slot = LootVisuals.Metal;
            foreach (float x in new[] { -0.3f, 0.3f })
                k.Box(new Vector3(x, 0.05f, 0.28f), new Vector3(0.04f, 0.095f, 0.58f));
            k.slot = LootVisuals.Black;
            k.Box(new Vector3(0f, -0.003f, 0.28f), new Vector3(0.9f, 0.006f, 0.47f));
            k.slot = LootVisuals.Brass;
            foreach (float x in new[] { -0.49f, 0.49f })
                foreach (float z in new[] { 0.02f, 0.545f })
                    k.Box(new Vector3(x, 0.05f, z), new Vector3(0.05f, 0.09f, 0.05f));
            // The staple the hasp closes over, and a handle on top.
            k.Box(new Vector3(0f, 0.0f, 0.575f), new Vector3(0.06f, 0.06f, 0.02f));
            k.slot = LootVisuals.Black;
            k.Box(new Vector3(0f, 0.1f, 0.28f), new Vector3(0.22f, 0.025f, 0.03f));
            return k.Finish("Stash_Lid", false);
        }
    }
}
