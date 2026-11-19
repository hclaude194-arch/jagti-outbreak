using System.Collections.Generic;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Flat white HUD icons drawn from simple shapes when first needed (tinted with a colour when
    /// drawn), so the HUD needs no image files. Each entry is "name|width x height|ops", the ops run
    /// in order, separated by ';': "r x y w h" a rectangle, "o x y r" a disc, "p x y x y ..." a
    /// polygon; a leading '-' cuts the shape out instead. Canvas y runs down.
    /// tools/royale/preview_hud.py reads this table, so keep the format.
    /// </summary>
    public static class HudIcons
    {
        static readonly string[] Table =
        {
            // Guns face right on a 200 x 100 canvas.
            "rifle|200x100|p 6 36 40 34 44 40 44 56 30 56 12 66 6 66;r 44 32 70 22;r 64 25 36 6;r 70 30 4 4;r 92 30 4 4;" +
                "r 114 34 44 16;r 158 39 34 6;p 150 34 155 25 160 25 160 34;p 88 54 102 54 108 82 94 85;p 58 54 70 54 66 76 54 74",
            "smg|200x100|r 12 36 32 22;-r 18 42 22 10;r 44 33 72 23;r 64 27 20 6;r 116 39 26 9;r 142 37 10 13;" +
                "p 86 56 98 56 100 94 88 94;p 54 56 66 56 62 80 50 78",
            "shotgun|200x100|p 4 41 46 36 50 42 50 54 40 56 10 71 3 67;r 50 34 48 20;r 98 36 96 7;r 98 45 80 7;" +
                "r 112 43 42 13;p 58 54 70 54 64 73 54 71",
            "pistol|200x100|r 40 28 112 22;r 152 31 6 14;r 44 50 84 10;p 44 54 70 54 78 92 46 94 40 74;" +
                "r 70 58 28 18;-r 76 60 16 10",
            "marksman|200x100|p 3 43 44 38 48 44 48 58 36 60 14 73 3 71;r 48 40 62 16;r 56 23 52 10;r 50 20 8 15;r 104 20 8 15;" +
                "r 64 32 6 8;r 94 32 6 8;r 110 44 84 6;r 80 56 14 18;p 52 56 62 56 58 75 48 73",
            "fist|100x100|r 22 36 52 40;o 30 38 9;o 44 34 9;o 58 34 9;o 70 38 8;p 22 60 10 52 14 44 26 48;r 30 74 38 14",

            // Kit on a 100 x 100 canvas.
            "bandage|100x100|p 8 66 66 8 92 34 34 92;-p 50 30 70 50 50 70 30 50;p 50 37 63 50 50 63 37 50;-o 24 62 3;-o 38 76 3;-o 62 24 3;-o 76 38 3",
            "medkit|100x100|r 12 30 76 56;r 36 16 28 18;-r 42 22 16 10;-r 44 44 12 32;-r 34 54 32 12",
            "chai|100x100|p 20 38 72 38 66 80 26 80;o 76 56 12;-o 76 56 6;r 12 82 70 7;r 34 12 5 18;r 48 8 5 22;r 62 14 5 16",
            "grenade|100x100|o 48 62 28;r 38 24 20 14;p 56 24 70 24 78 50 70 52 64 32;o 32 26 9;-o 32 26 5;-r 22 56 52 4",
            "smoke|100x100|r 26 32 44 60;-r 26 50 44 6;r 36 22 24 12;o 72 24 11;o 86 14 8;o 84 32 7",
            "helmet|100x100|o 50 60 36;-r 0 60 100 50;r 8 56 84 10;-r 30 30 40 5",
            "vest|100x100|p 24 14 40 14 50 30 60 14 76 14 86 30 86 88 14 88 14 30;-r 24 54 52 5;-r 24 70 52 5",
            "pack|100x100|o 50 34 28;r 22 34 56 56;-r 30 58 40 5;r 12 52 12 32;r 76 52 12 32;r 40 2 20 10;-r 45 4 10 8",
            "person|100x100|o 50 26 17;p 18 96 22 62 34 48 66 48 78 62 82 96",
            "target|100x100|o 50 50 40;-o 50 50 31;o 50 50 14;-o 50 50 7;r 47 2 6 22;r 47 76 6 22;r 2 47 22 6;r 76 47 22 6",
            "haze|100x100|o 32 58 18;o 54 46 24;o 74 60 16;r 30 60 46 16;-r 0 82 100 18;r 14 84 72 5;r 24 93 52 5",
            "ammo|100x100|p 36 46 50 8 64 46;r 36 46 28 40;-r 36 72 28 4;r 32 86 36 8",
            "skull|100x100|o 50 42 32;r 30 58 40 28;-o 38 44 9;-o 62 44 9;-r 41 72 5 14;-r 54 72 5 14;-p 50 54 45 64 55 64",
            "clock|100x100|o 50 54 38;-o 50 54 30;r 47 30 6 26;r 47 50 20 6;r 40 6 20 8",
            "heart|100x100|o 32 38 20;o 68 38 20;p 13 44 50 88 87 44 50 30",
            "shield|100x100|p 50 6 86 20 84 52 50 94 16 52 14 20;-p 50 18 74 28 72 50 50 80 28 50 26 28;p 50 26 66 32 64 49 50 70 36 49 34 32",
            "pin|100x100|o 50 38 28;p 26 50 50 96 74 50;-o 50 38 11",
            "star|100x100|p 61 38 68 59 50 72 32 59 39 38;p 39 38 50 6 61 38;p 61 38 95 38 68 59;p 68 59 78 91 50 72;p 50 72 22 91 32 59;p 32 59 5 38 39 38",
            "trunk|120x100|r 6 34 108 58;-r 14 44 92 4;r 4 22 112 14;-r 52 44 16 22;r 54 46 12 16;-r 57 50 6 8",
            "wheel|100x100|o 50 50 42;-o 50 50 32;o 50 50 10;r 47 12 6 30;r 47 58 6 30;r 12 47 30 6;r 58 47 30 6",
            "arrow|100x100|p 50 4 88 94 50 74 12 94",
            "drop|100x100|o 50 34 30;-r 0 34 100 70;r 22 60 56 34;p 22 34 48 60 44 62 20 36;p 78 34 52 60 56 62 80 36;r 48 34 4 26",
        };

        struct Op
        {
            public char kind;      // 'r', 'o' or 'p'
            public bool cut;
            public float[] v;
        }

        static Dictionary<string, string> specs;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>The icon as a white texture 64 pixels high (null if the name is unknown).</summary>
        public static Texture2D Get(string name)
        {
            Texture2D t;
            if (cache.TryGetValue(name, out t)) return t;
            if (specs == null)
            {
                specs = new Dictionary<string, string>();
                foreach (var s in Table) specs[s.Substring(0, s.IndexOf('|'))] = s;
            }
            string spec;
            t = specs.TryGetValue(name, out spec) ? Build(spec, 64) : null;
            cache[name] = t;
            return t;
        }

        /// <summary>Width over height of an icon's canvas (2 for guns).</summary>
        public static float Aspect(string name)
        {
            var t = Get(name);
            return t != null ? t.width / (float)t.height : 1f;
        }

        /// <summary>The icon for a gun class.</summary>
        public static string ForGun(GunClass c)
        {
            switch (c)
            {
                case GunClass.Sidearm: return "pistol";
                case GunClass.Smg: return "smg";
                case GunClass.Shotgun: return "shotgun";
                case GunClass.Marksman: return "marksman";
                default: return "rifle";
            }
        }

        static Texture2D Build(string spec, int height)
        {
            string[] parts = spec.Split('|');
            string[] size = parts[1].Split('x');
            float cw = float.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture);
            float ch = float.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture);
            var ops = new List<Op>();
            foreach (string raw in parts[2].Split(';'))
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                var op = new Op();
                if (s[0] == '-') { op.cut = true; s = s.Substring(1); }
                op.kind = s[0];
                string[] nums = s.Substring(1).Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                op.v = new float[nums.Length];
                for (int i = 0; i < nums.Length; i++) op.v[i] = float.Parse(nums[i], System.Globalization.CultureInfo.InvariantCulture);
                ops.Add(op);
            }

            int th = height, tw = Mathf.RoundToInt(height * cw / ch);
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            var px = new Color32[tw * th];
            const int ss = 4;     // 4 x 4 samples per pixel for smooth edges
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float u = (x + (sx + 0.5f) / ss) / tw * cw;
                            float v = (th - 1 - y + (sy + 0.5f) / ss) / th * ch;   // texture rows run up
                            bool on = false;
                            for (int k = 0; k < ops.Count; k++)
                                if (Inside(ops[k], u, v)) on = !ops[k].cut;
                            if (on) hits++;
                        }
                    px[y * tw + x] = new Color32(255, 255, 255, (byte)(hits * 255 / (ss * ss)));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static bool Inside(Op op, float x, float y)
        {
            var v = op.v;
            switch (op.kind)
            {
                case 'r':
                    return x >= v[0] && x <= v[0] + v[2] && y >= v[1] && y <= v[1] + v[3];
                case 'o':
                    return (x - v[0]) * (x - v[0]) + (y - v[1]) * (y - v[1]) <= v[2] * v[2];
                default:
                {
                    bool c = false;
                    int n = v.Length / 2;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        float xi = v[i * 2], yi = v[i * 2 + 1], xj = v[j * 2], yj = v[j * 2 + 1];
                        if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
                    }
                    return c;
                }
            }
        }
    }
}
