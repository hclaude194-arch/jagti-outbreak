using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.Royale
{
    /// <summary>
    /// A person built in code as one skinned mesh over 16 bones (a few draw calls per body).
    /// Poses are driven from code: walking and running with the feet placed by leg IK, crouching
    /// and crouch-walking, holding the actual gun low or aimed with both hands on it (arm IK), a
    /// flinch when hit, free fall, gliding under a canopy, and falling over when killed.
    /// Vest, helmet and backpack show their level. Far bodies update less often and stop casting
    /// shadows, to stay within the Iris Xe budget.
    /// </summary>
    public class HumanBody : MonoBehaviour
    {
        public enum Pose { Stand, FreeFall, Glide, Dead, Seat, Hang }

        // Bone order in the skinned mesh.
        const int BHips = 0, BSpine = 1, BChest = 2, BHead = 3,
                  BUpperL = 4, BLowerL = 5, BHandL = 6, BUpperR = 7, BLowerR = 8, BHandR = 9,
                  BThighL = 10, BShinL = 11, BFootL = 12, BThighR = 13, BShinR = 14, BFootR = 15, BoneCount = 16;
        // Material slots of the body mesh.
        const int SSkin = 0, SShirt = 1, SPants = 2, SGear = 3, SlotCount = 4;

        const float HipHeight = 0.95f, Thigh = 0.45f, Shin = 0.44f, Ankle = 0.075f;
        const float UpperArm = 0.29f, Forearm = 0.27f;

        public Pose pose = Pose.Stand;
        /// <summary>Horizontal speed in m/s (drives the stride).</summary>
        public float speed;
        public bool armed;
        public bool aiming;
        /// <summary>0 standing .. 1 crouched.</summary>
        public float crouch;
        /// <summary>Up/down angle of the aim in degrees (positive = up).</summary>
        public float aimPitch;
        /// <summary>Seated: where the hands grip (handlebar or wheel) and the feet rest, in world space.</summary>
        [HideInInspector] public Vector3 handTargetL, handTargetR, footTargetL, footTargetR;

        Transform[] bones;
        SkinnedMeshRenderer skin;
        Transform canopy;
        Transform gunHolder;
        GameObject[] heldGuns;
        GameObject[] vests = new GameObject[4], helmets = new GameObject[4], packs = new GameObject[4];
        int gunShown = -2, vestShown = -1, helmetShown = -1, packShown = -1;
        Vector3 gripR, gripL;       // hand points on the shown gun (gun space)

        float phase;
        float deathT = -1f;
        float fallSide = 1f;
        float recoil;
        float flinch;
        Vector3 flinchDir;
        float crouchNow, aimNow, armedNow;
        float lodTimer;
        int lod = -1;

        public Transform Head { get { return bones != null ? bones[BHead] : transform; } }

        // ------------------------------------------------------------------ building

        public static HumanBody Build(Transform root, Material skinMat, Material shirt, Material pants, Material gear,
                                      Material gunMat, Material canopyMat)
        {
            var go = new GameObject("Body");
            go.transform.SetParent(root, false);
            var b = go.AddComponent<HumanBody>();
            b.MakeBones();
            b.MakeSkin(new[] { skinMat, shirt, pants, gear }, Random.value < 0.5f);
            b.MakeCanopy(canopyMat, gear);
            b.MakeGear();
            b.MakeGuns(gunMat);
            return b;
        }

        Transform Bone(int index, string name, int parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent < 0 ? transform : bones[parent], false);
            t.localPosition = local;
            bones[index] = t;
            return t;
        }

        void MakeBones()
        {
            bones = new Transform[BoneCount];
            Bone(BHips, "Hips", -1, new Vector3(0f, HipHeight, 0f));
            Bone(BSpine, "Spine", BHips, new Vector3(0f, 0.1f, 0f));
            Bone(BChest, "Chest", BSpine, new Vector3(0f, 0.2f, 0f));
            Bone(BHead, "Head", BChest, new Vector3(0f, 0.3f, 0.01f));
            Bone(BUpperL, "Upper Arm L", BChest, new Vector3(-0.2f, 0.24f, 0f));
            Bone(BLowerL, "Forearm L", BUpperL, new Vector3(0f, -UpperArm, 0f));
            Bone(BHandL, "Hand L", BLowerL, new Vector3(0f, -Forearm, 0f));
            Bone(BUpperR, "Upper Arm R", BChest, new Vector3(0.2f, 0.24f, 0f));
            Bone(BLowerR, "Forearm R", BUpperR, new Vector3(0f, -UpperArm, 0f));
            Bone(BHandR, "Hand R", BLowerR, new Vector3(0f, -Forearm, 0f));
            Bone(BThighL, "Thigh L", BHips, new Vector3(-0.1f, -0.03f, 0f));
            Bone(BShinL, "Shin L", BThighL, new Vector3(0f, -Thigh, 0f));
            Bone(BFootL, "Foot L", BShinL, new Vector3(0f, -Shin, 0f));
            Bone(BThighR, "Thigh R", BHips, new Vector3(0.1f, -0.03f, 0f));
            Bone(BShinR, "Shin R", BThighR, new Vector3(0f, -Thigh, 0f));
            Bone(BFootR, "Foot R", BShinR, new Vector3(0f, -Shin, 0f));
        }

        void MakeSkin(Material[] mats, bool longHair)
        {
            var k = new MeshKit(SlotCount);
            Matrix4x4 rootInv = transform.worldToLocalMatrix;
            System.Action<int, int> at = (bone, slot) =>
            {
                k.bone = bone;
                k.slot = slot;
                k.place = rootInv * bones[bone].localToWorldMatrix;
            };

            // Hips and seat.
            at(BHips, SPants);
            k.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.36f, 0.2f, 0.23f), 1.03f, 1f);
            at(BHips, SGear);
            k.Box(new Vector3(0f, 0.115f, 0f), new Vector3(0.355f, 0.045f, 0.225f));      // belt
            k.Box(new Vector3(0f, 0.115f, 0.115f), new Vector3(0.06f, 0.04f, 0.01f));      // buckle

            // Belly and chest: a tapered shirt, wider at the shoulders.
            at(BSpine, SShirt);
            k.Box(new Vector3(0f, 0.08f, 0f), new Vector3(0.33f, 0.18f, 0.2f), 1.06f, 1.05f);
            at(BChest, SShirt);
            k.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.36f, 0.24f, 0.22f), 1.12f, 0.95f);
            k.Ellipsoid(new Vector3(-0.19f, 0.23f, 0f), new Vector3(0.075f, 0.06f, 0.075f), 8, 4);   // shoulders
            k.Ellipsoid(new Vector3(0.19f, 0.23f, 0f), new Vector3(0.075f, 0.06f, 0.075f), 8, 4);
            k.Tube(new Vector3(0f, 0.22f, 0f), 0f, 0.035f, new Vector2(0.085f, 0.075f), new Vector2(0.075f, 0.07f), 8, false);  // collar
            at(BChest, SSkin);
            k.Tube(new Vector3(0f, 0.24f, 0.005f), 0f, 0.08f, new Vector2(0.055f, 0.055f), new Vector2(0.05f, 0.05f), 8, false); // neck

            // Head: face, nose, ears, eyes and brows, hair.
            at(BHead, SSkin);
            k.Ellipsoid(new Vector3(0f, 0.11f, 0.005f), new Vector3(0.095f, 0.12f, 0.105f), 12, 8);
            k.Box(new Vector3(0f, 0.095f, 0.105f), new Vector3(0.025f, 0.045f, 0.035f), 0.6f, 0.7f);   // nose
            k.Box(new Vector3(-0.097f, 0.11f, -0.005f), new Vector3(0.02f, 0.05f, 0.035f));
            k.Box(new Vector3(0.097f, 0.11f, -0.005f), new Vector3(0.02f, 0.05f, 0.035f));
            at(BHead, SGear);
            k.Box(new Vector3(-0.037f, 0.125f, 0.098f), new Vector3(0.024f, 0.012f, 0.01f));     // eyes
            k.Box(new Vector3(0.037f, 0.125f, 0.098f), new Vector3(0.024f, 0.012f, 0.01f));
            k.Box(new Vector3(-0.037f, 0.148f, 0.098f), new Vector3(0.034f, 0.008f, 0.012f));    // brows
            k.Box(new Vector3(0.037f, 0.148f, 0.098f), new Vector3(0.034f, 0.008f, 0.012f));
            k.Ellipsoid(new Vector3(0f, 0.13f, -0.008f), new Vector3(0.102f, 0.115f, 0.112f), 12, 4, 5f, 90f);  // hair cap
            if (longHair)
                k.Box(new Vector3(0f, 0.07f, -0.075f), new Vector3(0.17f, 0.13f, 0.06f), 1f, 1f);           // hair down the back
            else
                k.Box(new Vector3(0f, 0.105f, -0.085f), new Vector3(0.16f, 0.07f, 0.04f));

            // Arms: short sleeves over the upper arm, bare forearms, hands with a thumb.
            foreach (int side in new[] { -1, 1 })
            {
                int up = side < 0 ? BUpperL : BUpperR, lo = side < 0 ? BLowerL : BLowerR, hand = side < 0 ? BHandL : BHandR;
                at(up, SShirt);
                k.Tube(Vector3.zero, 0.02f, -0.16f, new Vector2(0.06f, 0.06f), new Vector2(0.056f, 0.056f), 8);
                at(up, SSkin);
                k.Tube(Vector3.zero, -0.15f, -UpperArm, new Vector2(0.045f, 0.045f), new Vector2(0.04f, 0.04f), 8, false);
                at(lo, SSkin);
                k.Ellipsoid(Vector3.zero, new Vector3(0.042f, 0.04f, 0.042f), 8, 4);
                k.Tube(Vector3.zero, 0f, -Forearm, new Vector2(0.04f, 0.04f), new Vector2(0.03f, 0.028f), 8, false);
                at(hand, SSkin);
                k.Box(new Vector3(0f, -0.045f, 0.005f), new Vector3(0.05f, 0.09f, 0.03f), 1f, 1f);
                k.Box(new Vector3(side * -0.02f, -0.03f, 0.02f), new Vector3(0.018f, 0.05f, 0.02f), 1f, 1f, new Vector3(-20f, 0f, side * 15f));
            }

            // Legs: tapered trousers, knees, shoes with a sole.
            foreach (int side in new[] { -1, 1 })
            {
                int th = side < 0 ? BThighL : BThighR, sh = side < 0 ? BShinL : BShinR, ft = side < 0 ? BFootL : BFootR;
                at(th, SPants);
                k.Tube(Vector3.zero, 0.02f, -Thigh, new Vector2(0.098f, 0.102f), new Vector2(0.068f, 0.07f), 8);
                at(sh, SPants);
                k.Ellipsoid(Vector3.zero, new Vector3(0.068f, 0.06f, 0.07f), 8, 4);
                k.Tube(Vector3.zero, 0f, -Shin + 0.04f, new Vector2(0.064f, 0.068f), new Vector2(0.05f, 0.052f), 8);
                at(ft, SGear);
                k.Box(new Vector3(0f, -0.03f, 0.045f), new Vector3(0.1f, 0.085f, 0.26f), 0.95f, 0.85f);
                k.Box(new Vector3(0f, -0.071f, 0.047f), new Vector3(0.106f, 0.018f, 0.27f));             // sole
            }

            var mesh = k.Finish("Body", true);
            var bind = new Matrix4x4[BoneCount];
            for (int i = 0; i < BoneCount; i++) bind[i] = bones[i].worldToLocalMatrix * transform.localToWorldMatrix;
            mesh.bindposes = bind;

            var go = new GameObject("Skin");
            go.transform.SetParent(transform, false);
            skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = bones[BHips];
            skin.sharedMaterials = mats;
            skin.quality = SkinQuality.Bone1;
            skin.updateWhenOffscreen = false;
            skin.localBounds = new Bounds(new Vector3(0f, -0.1f, 0f), new Vector3(2.4f, 2.6f, 2.4f));
        }

        void MakeCanopy(Material canopyMat, Material lineMat)
        {
            canopy = new GameObject("Canopy").transform;
            canopy.SetParent(transform, false);
            canopy.localPosition = new Vector3(0f, 1.8f, 0f);
            var k = new MeshKit(2);
            const int cells = 9;
            for (int i = 0; i < cells; i++)
            {
                float u = (i - (cells - 1) / 2f) / ((cells - 1) / 2f);   // -1..1
                float a = u * 58f;
                Vector3 pos = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 3.7f, Mathf.Cos(a * Mathf.Deg2Rad) * 3.7f - 0.8f, 0.1f);
                k.slot = 0;
                // An aerofoil cell: thick at the front, thin at the trailing edge.
                k.Box(pos, new Vector3(0.84f, 0.2f, 1.6f - Mathf.Abs(u) * 0.4f), 1f, 0.75f, new Vector3(-6f, 0f, -a));
                k.slot = 1;
                k.Box(pos + new Vector3(0f, 0f, 0f), new Vector3(0.86f, 0.03f, 0.04f), 1f, 1f, new Vector3(0f, 0f, -a));
            }
            k.slot = 1;
            foreach (float side in new[] { -1f, 1f })
                for (int j = 0; j < 3; j++)
                {
                    float x = side * (0.6f + j * 0.75f);
                    Vector3 top = new Vector3(x * 1.05f, 2.85f - j * 0.35f, 0.1f);
                    Vector3 bottom = new Vector3(side * 0.22f, -0.35f, 0f);
                    Vector3 mid = (top + bottom) / 2f;
                    float len = (top - bottom).magnitude;
                    float ang = Mathf.Atan2(top.x - bottom.x, top.y - bottom.y) * Mathf.Rad2Deg;
                    k.Box(mid, new Vector3(0.012f, len, 0.012f), 1f, 1f, new Vector3(0f, 0f, -ang));
                }
            var go = canopy.gameObject;
            go.AddComponent<MeshFilter>().sharedMesh = k.Finish("Canopy", false);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { canopyMat, lineMat };
            canopy.gameObject.SetActive(false);
        }

        Material Palette(int slot)
        {
            var loot = LootDirector.Instance;
            if (loot != null && loot.palette != null && slot < loot.palette.Length) return loot.palette[slot];
            return skin != null ? skin.sharedMaterials[SGear] : null;
        }

        GameObject GearPart(string name, int bone, MeshKit k, int[] slots)
        {
            var go = new GameObject(name);
            go.transform.SetParent(bones[bone], false);
            go.AddComponent<MeshFilter>().sharedMesh = k.Finish(name, false);
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++) mats[i] = Palette(slots[i]);
            mr.sharedMaterials = mats;
            go.SetActive(false);
            return go;
        }

        void MakeGear()
        {
            for (int level = 1; level <= 3; level++)
            {
                int main = level >= 3 ? LootVisuals.Black : level == 2 ? LootVisuals.Olive : LootVisuals.Khaki;
                int[] slots = { main, LootVisuals.Black };

                // Vest: front and back plates over the chest, pouches from level 2, collar at level 3.
                var k = new MeshKit(2);
                float thick = 0.03f + level * 0.012f;
                k.slot = 0;
                k.Box(new Vector3(0f, 0.08f, 0.115f + thick / 2f), new Vector3(0.34f, 0.3f, thick), 1.05f, 1f);
                k.Box(new Vector3(0f, 0.08f, -0.115f - thick / 2f), new Vector3(0.34f, 0.3f, thick), 1.05f, 1f);
                k.Box(new Vector3(-0.17f, 0.1f, 0f), new Vector3(0.03f, 0.22f, 0.25f));
                k.Box(new Vector3(0.17f, 0.1f, 0f), new Vector3(0.03f, 0.22f, 0.25f));
                k.Box(new Vector3(-0.12f, 0.25f, 0f), new Vector3(0.06f, 0.02f, 0.24f));     // shoulder straps
                k.Box(new Vector3(0.12f, 0.25f, 0f), new Vector3(0.06f, 0.02f, 0.24f));
                k.slot = 1;
                if (level >= 2)
                    for (int p = -1; p <= 1; p++)
                        k.Box(new Vector3(p * 0.1f, -0.015f, 0.14f + thick), new Vector3(0.08f, 0.1f, 0.05f));
                if (level >= 3)
                {
                    k.slot = 0;
                    k.Tube(new Vector3(0f, 0.22f, 0f), 0f, 0.06f, new Vector2(0.11f, 0.1f), new Vector2(0.1f, 0.095f), 10, false);
                    k.Box(new Vector3(-0.2f, 0.2f, 0f), new Vector3(0.07f, 0.05f, 0.2f), 1f, 1f, new Vector3(0f, 0f, 30f));
                    k.Box(new Vector3(0.2f, 0.2f, 0f), new Vector3(0.07f, 0.05f, 0.2f), 1f, 1f, new Vector3(0f, 0f, -30f));
                }
                vests[level] = GearPart("Vest " + level, BChest, k, slots);

                // Helmet: a shell over the hair; brim and mount from level 2, visor at level 3.
                k = new MeshKit(2);
                k.slot = 0;
                float grow = 0.012f + level * 0.006f;
                k.Ellipsoid(new Vector3(0f, 0.13f, -0.005f), new Vector3(0.105f + grow, 0.12f + grow, 0.115f + grow), 12, 5, level >= 2 ? -8f : 10f, 90f);
                if (level >= 2)
                {
                    k.Tube(new Vector3(0f, 0.115f, -0.005f), 0f, 0.012f, new Vector2(0.13f + grow, 0.14f + grow), new Vector2(0.125f + grow, 0.135f + grow), 12, true);
                    k.slot = 1;
                    k.Box(new Vector3(0f, 0.22f, 0.1f), new Vector3(0.045f, 0.03f, 0.03f));
                }
                if (level >= 3)
                {
                    k.slot = 1;
                    k.Box(new Vector3(0f, 0.11f, 0.125f), new Vector3(0.19f, 0.06f, 0.012f), 1f, 1f, new Vector3(-12f, 0f, 0f));
                }
                helmets[level] = GearPart("Helmet " + level, BHead, k, slots);

                // Backpack: bigger at each level.
                int packCol = level >= 3 ? LootVisuals.Black : level == 2 ? LootVisuals.Olive : LootVisuals.Blue;
                k = new MeshKit(2);
                float h = 0.3f + level * 0.07f, depth = 0.12f + level * 0.03f;
                k.slot = 0;
                k.Box(new Vector3(0f, 0.12f - h * 0.25f, -0.13f - depth / 2f), new Vector3(0.3f, h, depth), 0.92f, 0.85f);
                k.Box(new Vector3(0f, 0.04f - h * 0.25f, -0.13f - depth - 0.03f), new Vector3(0.22f, h * 0.45f, 0.06f));    // front pocket
                k.slot = 1;
                k.Box(new Vector3(-0.1f, 0.12f, -0.12f), new Vector3(0.04f, 0.3f, 0.02f));
                k.Box(new Vector3(0.1f, 0.12f, -0.12f), new Vector3(0.04f, 0.3f, 0.02f));
                if (level >= 2) k.Tube(new Vector3(0f, 0.15f + h * 0.25f, -0.13f - depth / 2f), -0.17f, 0.17f, new Vector2(0.05f, 0.05f), new Vector2(0.05f, 0.05f), 8, true, new Vector3(0f, 0f, 90f)); // rolled mat
                packs[level] = GearPart("Pack " + level, BChest, k, new[] { packCol, LootVisuals.Black });
            }
        }

        void MakeGuns(Material fallback)
        {
            gunHolder = new GameObject("Gun Holder").transform;
            gunHolder.SetParent(bones[BChest], false);
            heldGuns = new GameObject[RoyaleItems.Guns.Length];
            for (int i = 0; i < heldGuns.Length; i++)
            {
                var model = LootVisuals.Get(ItemKind.Gun, i);
                var go = new GameObject(RoyaleItems.Guns[i].name);
                go.transform.SetParent(gunHolder, false);
                // Loot guns lie on their side along +X; stand them up and point them forward.
                go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(-90f, 0f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var loot = LootDirector.Instance;
                if (loot != null && loot.palette != null && loot.palette.Length >= LootVisuals.PaletteSize) mr.sharedMaterials = loot.palette;
                else
                {
                    var mats = new Material[LootVisuals.PaletteSize];
                    for (int m = 0; m < mats.Length; m++) mats[m] = fallback;
                    mr.sharedMaterials = mats;
                }
                go.SetActive(false);
                heldGuns[i] = go;
            }
        }

        // ------------------------------------------------------------------ outside control

        /// <summary>Shows the gun with this id (-1 for none).</summary>
        public void ShowGun(int id)
        {
            if (id == gunShown || heldGuns == null) return;
            gunShown = id;
            for (int i = 0; i < heldGuns.Length; i++) heldGuns[i].SetActive(i == id);
            if (id < 0) return;
            // Hand points along the gun (grip behind the trigger, support hand under the fore-end).
            if (id == 5) { gripR = new Vector3(0f, -0.05f, -0.06f); gripL = new Vector3(-0.02f, -0.06f, -0.04f); return; }
            if (id == 7) { gripR = new Vector3(0f, -0.05f, -0.12f); gripL = new Vector3(0f, -0.03f, 0.12f); return; }
            if (id == 8) { gripR = new Vector3(0f, -0.06f, -0.12f); gripL = new Vector3(0f, -0.02f, 0.3f); return; }
            if (id == 9) { gripR = new Vector3(0f, -0.06f, -0.12f); gripL = new Vector3(0f, -0.04f, 0.22f); return; }
            switch (RoyaleItems.Guns[id].gunClass)
            {
                case GunClass.Sidearm: gripR = new Vector3(0f, -0.05f, -0.03f); gripL = new Vector3(-0.02f, -0.06f, -0.01f); break;
                case GunClass.Smg: gripR = new Vector3(0f, -0.06f, -0.03f); gripL = new Vector3(0f, -0.04f, 0.16f); break;
                case GunClass.Shotgun: gripR = new Vector3(0f, -0.05f, -0.15f); gripL = new Vector3(0f, -0.04f, 0.12f); break;
                case GunClass.Rifle: gripR = new Vector3(0f, -0.06f, -0.09f); gripL = new Vector3(0f, -0.03f, 0.26f); break;
                default: gripR = new Vector3(0f, -0.06f, -0.06f); gripL = new Vector3(0f, -0.03f, 0.22f); break;
            }
        }

        /// <summary>Fits the shown gun's attachments to the held model.</summary>
        public void Dress(GunItem g)
        {
            if (g == null || heldGuns == null || gunShown < 0 || g.def.id != gunShown) return;
            var loot = LootDirector.Instance;
            if (loot == null || loot.palette == null) return;
            LootVisuals.Dress(heldGuns[gunShown].transform, g, loot.palette);
        }

        public void ShowGear(int vest, int helmet, int pack)
        {
            Swap(vests, ref vestShown, vest);
            Swap(helmets, ref helmetShown, helmet);
            Swap(packs, ref packShown, pack);
        }

        static void Swap(GameObject[] set, ref int shown, int level)
        {
            level = Mathf.Clamp(level, 0, 3);
            if (level == shown) return;
            if (shown > 0 && set[shown] != null) set[shown].SetActive(false);
            shown = level;
            if (shown > 0 && set[shown] != null) set[shown].SetActive(true);
        }

        public void Kick()
        {
            recoil = 1f;
        }

        /// <summary>A short flinch away from a hit.</summary>
        public void Flinch(Vector3 from)
        {
            flinch = 1f;
            flinchDir = transform.InverseTransformDirection((transform.position - from).normalized);
        }

        public void Die(Vector3 from)
        {
            if (pose == Pose.Dead) return;
            pose = Pose.Dead;
            deathT = 0f;
            Vector3 local = transform.InverseTransformDirection(transform.position - from);
            fallSide = local.x >= 0f ? 1f : -1f;
            ShowGun(-1);
            if (canopy != null) canopy.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ animation

        void LateUpdate()
        {
            if (bones == null) return;
            float dt = Time.deltaTime;

            // Level of detail: near bodies animate every frame, middle ones every third, far ones rarely.
            var cam = Camera.main;
            float d = cam != null ? (cam.transform.position - transform.position).magnitude : 0f;
            int want = d < 45f ? 0 : d < 110f ? 1 : d < 320f ? 2 : 3;
            if (pose != Pose.Stand && pose != Pose.Dead && want > 1) want = 1;   // fliers are seen from far away
            if (want != lod) SetLod(want);
            lodTimer += dt;
            float step = lod == 0 ? 0f : lod == 1 ? 0.05f : lod == 2 ? 0.2f : 1f;
            if (lodTimer < step) return;
            dt = lodTimer;
            lodTimer = 0f;
            if (lod == 3) return;

            recoil = Mathf.MoveTowards(recoil, 0f, dt * 10f);
            flinch = Mathf.MoveTowards(flinch, 0f, dt * 5f);
            if (canopy != null && canopy.gameObject.activeSelf != (pose == Pose.Glide)) canopy.gameObject.SetActive(pose == Pose.Glide);
            if (gunHolder != null) gunHolder.gameObject.SetActive(pose == Pose.Stand && armed);

            switch (pose)
            {
                case Pose.Stand: Stand(dt); break;
                case Pose.FreeFall: FreeFall(dt); break;
                case Pose.Glide: Glide(dt); break;
                case Pose.Dead: Dead(dt); break;
                case Pose.Seat: Seat(dt); break;
                case Pose.Hang: Hang(dt); break;
            }
        }

        void SetLod(int level)
        {
            lod = level;
            var shadows = level == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (skin != null)
            {
                skin.shadowCastingMode = shadows;
                skin.enabled = level < 3 || pose != Pose.Stand;
            }
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = shadows;
                r.enabled = level < 3;
            }
        }

        void Stand(float dt)
        {
            Transform root = transform;
            crouchNow = Mathf.MoveTowards(crouchNow, Mathf.Clamp01(crouch), dt * 4f);
            aimNow = Mathf.MoveTowards(aimNow, aiming ? 1f : 0f, dt * 5f);
            armedNow = armed ? 1f : 0f;

            // Stride: a step every ~0.7 m walking, longer running; crouched steps are short.
            float run = Mathf.Clamp01((speed - 3.5f) / 2.5f);
            float stride = Mathf.Clamp(speed * 0.13f, 0f, 0.42f) * Mathf.Lerp(1f, 0.6f, crouchNow);
            float cadence = speed < 0.15f ? 0f : (1.6f + speed * 0.42f) * Mathf.PI;
            if (cadence > 0f) phase += dt * cadence;
            else phase = Mathf.MoveTowards(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, dt * 6f);
            float moving = Mathf.Clamp01(speed / 1.5f);

            // Hips: lower when crouched, bob with the steps, lean into a run.
            float bob = Mathf.Abs(Mathf.Cos(phase)) * 0.035f * moving;
            float hipY = HipHeight - crouchNow * 0.42f - bob * (1f + run) + 0.01f;
            Vector3 flinchOffset = flinchDir * (flinch * 0.06f);
            bones[BHips].localPosition = new Vector3(flinchOffset.x, hipY, flinchOffset.z - crouchNow * 0.08f);
            bones[BHips].localRotation = Quaternion.Euler(0f, Mathf.Sin(phase) * 6f * moving, 0f);
            float lean = run * 10f + crouchNow * 18f - flinch * 8f * Mathf.Sign(flinchDir.z + 0.001f);
            float aimLean = armedNow * -aimPitch * 0.35f;
            bones[BSpine].localRotation = Quaternion.Euler(lean * 0.5f + aimLean * 0.5f, -Mathf.Sin(phase) * 5f * moving, flinchDir.x * flinch * -10f);
            bones[BChest].localRotation = Quaternion.Euler(lean * 0.5f + aimLean * 0.5f - crouchNow * 8f, -Mathf.Sin(phase) * 5f * moving, 0f);
            // The head looks along the aim, undoing the lean of the back.
            bones[BHead].localRotation = Quaternion.Euler(-aimPitch - (lean + aimLean - crouchNow * 8f), 0f, 0f);

            // Feet: planted in turn, lifted on the swing; knees forward.
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                float ph = phase + (s == 0 ? 0f : Mathf.PI);
                float z = Mathf.Sin(ph) * stride;
                float lift = Mathf.Max(0f, Mathf.Cos(ph)) * Mathf.Lerp(0.07f, 0.17f, run) * moving;
                Vector3 local = new Vector3(side * (0.11f + crouchNow * 0.03f), Ankle + lift, z + crouchNow * (s == 0 ? 0.2f : -0.22f));
                Vector3 target = root.TransformPoint(local);
                Vector3 pole = root.forward + root.right * side * (0.12f + crouchNow * 0.08f);
                int th = s == 0 ? BThighL : BThighR, sh = s == 0 ? BShinL : BShinR, ft = s == 0 ? BFootL : BFootR;
                Solve(bones[th], bones[sh], Thigh, Shin, target, pole);
                float toe = Mathf.Max(0f, Mathf.Cos(ph)) * 25f * moving;
                bones[ft].rotation = Quaternion.LookRotation(root.forward, Vector3.up) * Quaternion.Euler(-toe * 0.5f, 0f, 0f);
            }

            if (armed && gunShown >= 0) HoldGun();
            else
            {
                // Arms swing against the legs.
                float swing = Mathf.Sin(phase) * Mathf.Lerp(8f, 38f, run) * moving;
                bones[BUpperL].localRotation = Quaternion.Euler(-swing - crouchNow * 20f, 0f, -6f - flinch * 12f);
                bones[BUpperR].localRotation = Quaternion.Euler(swing - crouchNow * 20f, 0f, 6f + flinch * 12f);
                bones[BLowerL].localRotation = Quaternion.Euler(-15f - run * 55f - Mathf.Max(0f, -swing) * 0.5f, 0f, 0f);
                bones[BLowerR].localRotation = Quaternion.Euler(-15f - run * 55f - Mathf.Max(0f, swing) * 0.5f, 0f, 0f);
                bones[BHandL].localRotation = Quaternion.identity;
                bones[BHandR].localRotation = Quaternion.identity;
            }
        }

        void HoldGun()
        {
            var g = RoyaleItems.Guns[gunShown];
            bool pistol = g.gunClass == GunClass.Sidearm;
            float k = recoil;
            // At the ready the gun sits low across the body, muzzle down; aimed it comes to the shoulder.
            Vector3 low = pistol ? new Vector3(0.06f, 0.06f, 0.3f) : new Vector3(0.1f, 0.06f, 0.24f);
            Vector3 high = pistol ? new Vector3(0.03f, 0.26f, 0.46f) : new Vector3(0.13f, 0.2f, 0.14f);
            Vector3 pos = Vector3.Lerp(low, high, aimNow) - new Vector3(0f, 0f, k * 0.04f);
            float pitchHeld = Mathf.Lerp(22f, 0f, aimNow) - aimPitch * 0.65f * aimNow - k * 6f;
            float yawHeld = Mathf.Lerp(pistol ? -5f : -22f, 0f, aimNow);
            gunHolder.localPosition = pos;
            gunHolder.localRotation = Quaternion.Euler(pitchHeld, yawHeld, 0f);

            Vector3 handR = gunHolder.TransformPoint(gripR);
            Vector3 handL = gunHolder.TransformPoint(gripL);
            Transform chest = bones[BChest];
            Vector3 down = -chest.up;
            Solve(bones[BUpperR], bones[BLowerR], UpperArm, Forearm, handR + gunHolder.up * 0.03f, down + chest.right * 0.8f - chest.forward * 0.3f);
            Solve(bones[BUpperL], bones[BLowerL], UpperArm, Forearm, handL + gunHolder.up * 0.03f, down - chest.right * 0.9f);
            // Hands wrap the gun: palms in, fingers forward along it.
            bones[BHandR].rotation = gunHolder.rotation * Quaternion.Euler(70f, 0f, 90f);
            bones[BHandL].rotation = gunHolder.rotation * Quaternion.Euler(80f, 0f, -90f);
        }

        void FreeFall(float dt)
        {
            phase += dt * 6f;
            float flutter = Mathf.Sin(phase) * 3f;
            bones[BHips].localPosition = new Vector3(0f, HipHeight, 0f);
            bones[BHips].localRotation = Quaternion.Euler(80f, 0f, 0f);           // face down
            bones[BSpine].localRotation = Quaternion.Euler(-8f, 0f, 0f);
            bones[BChest].localRotation = Quaternion.Euler(-8f, 0f, 0f);
            bones[BHead].localRotation = Quaternion.Euler(-45f, 0f, 0f);
            bones[BUpperL].localRotation = Quaternion.Euler(0f, 0f, -95f + flutter);
            bones[BUpperR].localRotation = Quaternion.Euler(0f, 0f, 95f - flutter);
            bones[BLowerL].localRotation = Quaternion.Euler(-70f, 0f, 0f);
            bones[BLowerR].localRotation = Quaternion.Euler(-70f, 0f, 0f);
            bones[BThighL].localRotation = Quaternion.Euler(10f, 0f, -16f - flutter);
            bones[BThighR].localRotation = Quaternion.Euler(10f, 0f, 16f + flutter);
            bones[BShinL].localRotation = Quaternion.Euler(50f, 0f, 0f);
            bones[BShinR].localRotation = Quaternion.Euler(50f, 0f, 0f);
            bones[BFootL].localRotation = Quaternion.identity;
            bones[BFootR].localRotation = Quaternion.identity;
        }

        void Glide(float dt)
        {
            phase += dt * 2f;
            bones[BHips].localPosition = new Vector3(0f, HipHeight, 0f);
            bones[BHips].localRotation = Quaternion.Euler(Mathf.Sin(phase) * 3f - 8f, 0f, 0f);
            bones[BSpine].localRotation = Quaternion.identity;
            bones[BChest].localRotation = Quaternion.identity;
            bones[BHead].localRotation = Quaternion.Euler(10f, 0f, 0f);
            // Hands up on the brake toggles.
            bones[BUpperL].localRotation = Quaternion.Euler(0f, 0f, -150f);
            bones[BUpperR].localRotation = Quaternion.Euler(0f, 0f, 150f);
            bones[BLowerL].localRotation = Quaternion.Euler(0f, 0f, 20f);
            bones[BLowerR].localRotation = Quaternion.Euler(0f, 0f, -20f);
            // Seated in the harness, legs dangling.
            float sw = Mathf.Sin(phase * 1.3f) * 6f;
            bones[BThighL].localRotation = Quaternion.Euler(-55f + sw, 0f, -4f);
            bones[BThighR].localRotation = Quaternion.Euler(-50f - sw, 0f, 4f);
            bones[BShinL].localRotation = Quaternion.Euler(50f, 0f, 0f);
            bones[BShinR].localRotation = Quaternion.Euler(45f, 0f, 0f);
            bones[BFootL].localRotation = Quaternion.identity;
            bones[BFootR].localRotation = Quaternion.identity;
        }

        /// <summary>Riding: sat on the seat (the root is 0.8 m below it), hands on the bars, feet on the boards.</summary>
        void Seat(float dt)
        {
            Transform root = transform;
            bones[BHips].localPosition = new Vector3(0f, 0.8f, -0.02f);
            bones[BHips].localRotation = Quaternion.identity;
            bones[BSpine].localRotation = Quaternion.Euler(10f, 0f, 0f);
            bones[BChest].localRotation = Quaternion.Euler(6f, 0f, 0f);
            bones[BHead].localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Solve(bones[BThighL], bones[BShinL], Thigh, Shin, footTargetL + root.up * Ankle, root.forward + root.up * 0.6f - root.right * 0.2f);
            Solve(bones[BThighR], bones[BShinR], Thigh, Shin, footTargetR + root.up * Ankle, root.forward + root.up * 0.6f + root.right * 0.2f);
            bones[BFootL].rotation = Quaternion.LookRotation(root.forward, root.up);
            bones[BFootR].rotation = Quaternion.LookRotation(root.forward, root.up);
            Transform chest = bones[BChest];
            Solve(bones[BUpperL], bones[BLowerL], UpperArm, Forearm, handTargetL + root.up * 0.04f, -chest.up - chest.right * 0.9f);
            Solve(bones[BUpperR], bones[BLowerR], UpperArm, Forearm, handTargetR + root.up * 0.04f, -chest.up + chest.right * 0.9f);
            bones[BHandL].rotation = Hang(root.forward * 0.4f - root.up, root.forward);
            bones[BHandR].rotation = Hang(root.forward * 0.4f - root.up, root.forward);
        }

        /// <summary>On the zip line: hands up on the trolley, legs swinging.</summary>
        void Hang(float dt)
        {
            phase += dt * 2.4f;
            float sway = Mathf.Sin(phase) * 6f;
            bones[BHips].localPosition = new Vector3(0f, HipHeight, 0f);
            bones[BHips].localRotation = Quaternion.Euler(sway * 0.5f, 0f, 0f);
            bones[BSpine].localRotation = Quaternion.identity;
            bones[BChest].localRotation = Quaternion.identity;
            bones[BHead].localRotation = Quaternion.Euler(-10f, 0f, 0f);
            bones[BUpperL].localRotation = Quaternion.Euler(-8f, 0f, -168f);
            bones[BUpperR].localRotation = Quaternion.Euler(-8f, 0f, 168f);
            bones[BLowerL].localRotation = Quaternion.Euler(-10f, 0f, 0f);
            bones[BLowerR].localRotation = Quaternion.Euler(-10f, 0f, 0f);
            bones[BHandL].localRotation = Quaternion.identity;
            bones[BHandR].localRotation = Quaternion.identity;
            bones[BThighL].localRotation = Quaternion.Euler(-25f + sway, 0f, -4f);
            bones[BThighR].localRotation = Quaternion.Euler(-15f - sway, 0f, 4f);
            bones[BShinL].localRotation = Quaternion.Euler(30f, 0f, 0f);
            bones[BShinR].localRotation = Quaternion.Euler(20f, 0f, 0f);
            bones[BFootL].localRotation = Quaternion.identity;
            bones[BFootR].localRotation = Quaternion.identity;
        }

        void Dead(float dt)
        {
            if (deathT < 0f || deathT >= 1f) return;
            deathT = Mathf.Min(1f, deathT + dt * 1.8f);
            float t = deathT * deathT;
            float c = crouchNow;
            bones[BHips].localPosition = new Vector3(0f, Mathf.Lerp(HipHeight - c * 0.42f, 0.14f, t), 0f);
            bones[BHips].localRotation = Quaternion.Euler(0f, 0f, -fallSide * 86f * t);
            bones[BSpine].localRotation = Quaternion.Euler(0f, 0f, -fallSide * 4f * t);
            bones[BChest].localRotation = Quaternion.identity;
            bones[BThighL].localRotation = Quaternion.Euler(-20f * t, 0f, -8f * t);
            bones[BThighR].localRotation = Quaternion.Euler(10f * t, 0f, 8f * t);
            bones[BShinL].localRotation = Quaternion.Euler(30f * t, 0f, 0f);
            bones[BShinR].localRotation = Quaternion.Euler(10f * t, 0f, 0f);
            bones[BUpperL].localRotation = Quaternion.Euler(-30f * t, 0f, -70f * t);
            bones[BUpperR].localRotation = Quaternion.Euler(20f * t, 0f, 80f * t);
            bones[BLowerL].localRotation = Quaternion.Euler(-30f * t, 0f, 0f);
            bones[BLowerR].localRotation = Quaternion.Euler(-10f * t, 0f, 0f);
            bones[BHead].localRotation = Quaternion.Euler(0f, 0f, fallSide * 25f * t);
        }

        // ------------------------------------------------------------------ two-bone IK

        /// <summary>
        /// Bends a two-bone limb (both bones hanging along their local -Y) so its end reaches the target,
        /// with the middle joint pushed towards the pole direction.
        /// </summary>
        static void Solve(Transform upper, Transform lower, float l1, float l2, Vector3 target, Vector3 pole)
        {
            Vector3 s = upper.position;
            Vector3 d = target - s;
            float len = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.002f);
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
            Vector3 bend = pole - dir * Vector3.Dot(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.Cross(dir, upper.parent != null ? upper.parent.right : Vector3.right);
            bend.Normalize();
            float cosA = Mathf.Clamp((l1 * l1 + len * len - l2 * l2) / (2f * l1 * len), -1f, 1f);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            Vector3 mid = s + dir * (l1 * cosA) + bend * (l1 * sinA);
            Vector3 end = s + dir * len;
            upper.rotation = Hang(mid - s, bend);
            lower.rotation = Hang(end - mid, bend);
        }

        /// <summary>A rotation whose -Y points along 'down' and whose +Z leans towards 'front'.</summary>
        static Quaternion Hang(Vector3 down, Vector3 front)
        {
            Vector3 y = -down.normalized;
            Vector3 z = front - y * Vector3.Dot(front, y);
            if (z.sqrMagnitude < 1e-6f) z = Vector3.Cross(y, Vector3.right);
            return Quaternion.LookRotation(z.normalized, y);
        }
    }
}
