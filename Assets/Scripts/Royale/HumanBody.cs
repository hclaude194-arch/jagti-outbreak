using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A blocky person made from primitives with code-driven poses: walking and running, holding a gun
    /// at the ready or aiming it, free fall (spread out, face down), gliding under a canopy, and falling
    /// over when killed. Used for the bots and for the player's own body during the drop.
    /// A stand-in until rigged characters arrive in the polish phase.
    /// </summary>
    public class HumanBody : MonoBehaviour
    {
        public enum Pose { Stand, FreeFall, Glide, Dead }

        public Transform hips, torso, head, leftArm, rightArm, leftLeg, rightLeg, gun, canopy;
        public Pose pose = Pose.Stand;
        /// <summary>Horizontal speed in m/s (drives the stride).</summary>
        public float speed;
        public bool armed;
        public bool aiming;
        /// <summary>Up/down angle of the aim in degrees (positive = up).</summary>
        public float aimPitch;

        float phase;
        float deathT = -1f;
        float fallSide = 1f;
        float recoil;

        public static HumanBody Build(Transform root, Material skin, Material shirt, Material pants, Material gear,
                                      Material gunMat, Material canopyMat)
        {
            var go = new GameObject("Body");
            go.transform.SetParent(root, false);
            var b = go.AddComponent<HumanBody>();

            b.hips = Pivot(go.transform, "Hips", new Vector3(0f, 0.92f, 0f));
            b.leftLeg = Limb(b.hips, "Left Leg", new Vector3(-0.11f, 0f, 0f), new Vector3(0.16f, 0.9f, 0.18f), pants);
            b.rightLeg = Limb(b.hips, "Right Leg", new Vector3(0.11f, 0f, 0f), new Vector3(0.16f, 0.9f, 0.18f), pants);
            Box(b.leftLeg, "Left Shoe", new Vector3(0f, -0.87f, 0.04f), new Vector3(0.15f, 0.08f, 0.26f), gear);
            Box(b.rightLeg, "Right Shoe", new Vector3(0f, -0.87f, 0.04f), new Vector3(0.15f, 0.08f, 0.26f), gear);

            b.torso = Pivot(b.hips, "Torso", Vector3.zero);
            Box(b.torso, "Chest", new Vector3(0f, 0.32f, 0f), new Vector3(0.44f, 0.62f, 0.25f), shirt);
            Box(b.torso, "Belt", new Vector3(0f, 0.03f, 0f), new Vector3(0.45f, 0.07f, 0.26f), gear);

            b.leftArm = Limb(b.torso, "Left Arm", new Vector3(-0.29f, 0.58f, 0f), new Vector3(0.12f, 0.62f, 0.13f), shirt);
            b.rightArm = Limb(b.torso, "Right Arm", new Vector3(0.29f, 0.58f, 0f), new Vector3(0.12f, 0.62f, 0.13f), shirt);
            Box(b.leftArm, "Left Hand", new Vector3(0f, -0.66f, 0f), new Vector3(0.09f, 0.11f, 0.09f), skin);
            Box(b.rightArm, "Right Hand", new Vector3(0f, -0.66f, 0f), new Vector3(0.09f, 0.11f, 0.09f), skin);

            b.head = Pivot(b.torso, "Head", new Vector3(0f, 0.66f, 0f));
            Box(b.head, "Neck", new Vector3(0f, 0.02f, 0f), new Vector3(0.1f, 0.08f, 0.1f), skin);
            Box(b.head, "Face", new Vector3(0f, 0.16f, 0.01f), new Vector3(0.21f, 0.25f, 0.23f), skin);
            Box(b.head, "Hair", new Vector3(0f, 0.27f, -0.02f), new Vector3(0.22f, 0.06f, 0.22f), gear);

            // A generic gun held in the right hand; hidden when unarmed.
            b.gun = Pivot(b.rightArm, "Held Gun", new Vector3(0f, -0.64f, 0.05f));
            Box(b.gun, "Receiver", new Vector3(0f, 0f, 0.18f), new Vector3(0.06f, 0.09f, 0.5f), gunMat);
            Box(b.gun, "Barrel", new Vector3(0f, 0.02f, 0.5f), new Vector3(0.03f, 0.03f, 0.22f), gunMat);
            Box(b.gun, "Grip", new Vector3(0f, -0.07f, 0.06f), new Vector3(0.04f, 0.1f, 0.05f), gunMat);
            b.gun.gameObject.SetActive(false);

            // The glider canopy: a shallow arc of cells above the shoulders, with lines down to the harness.
            b.canopy = Pivot(go.transform, "Canopy", new Vector3(0f, 1.8f, 0f));
            const int cells = 7;
            for (int i = 0; i < cells; i++)
            {
                float u = (i - (cells - 1) / 2f) / ((cells - 1) / 2f);   // -1..1
                float a = u * 55f;
                Vector3 pos = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 3.6f, Mathf.Cos(a * Mathf.Deg2Rad) * 3.6f - 0.8f, 0.1f);
                Box(b.canopy, "Cell " + i, pos, new Vector3(1.05f, 0.14f, 1.5f - Mathf.Abs(u) * 0.35f), canopyMat, new Vector3(0f, 0f, -a));
            }
            foreach (float side in new[] { -1f, 1f })
                Box(b.canopy, "Lines", new Vector3(side * 1.5f, 1.2f, 0.1f), new Vector3(0.015f, 2.9f, 0.015f), gear, new Vector3(0f, 0f, side * 28f));
            b.canopy.gameObject.SetActive(false);

            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            return b;
        }

        static Transform Pivot(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static Transform Limb(Transform parent, string name, Vector3 pivot, Vector3 size, Material mat)
        {
            var p = Pivot(parent, name, pivot);
            Box(p, name + " Mesh", new Vector3(0f, -size.y / 2f, 0f), size, mat);
            return p;
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default(Vector3))
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
        }

        public void Kick()
        {
            recoil = 1f;
        }

        public void Die(Vector3 from)
        {
            if (pose == Pose.Dead) return;
            pose = Pose.Dead;
            deathT = 0f;
            Vector3 local = transform.InverseTransformDirection(transform.position - from);
            fallSide = local.x >= 0f ? 1f : -1f;
            if (gun != null) gun.gameObject.SetActive(false);
            if (canopy != null) canopy.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 10f);
            if (gun != null && pose == Pose.Stand && gun.gameObject.activeSelf != armed) gun.gameObject.SetActive(armed);
            if (canopy != null && canopy.gameObject.activeSelf != (pose == Pose.Glide)) canopy.gameObject.SetActive(pose == Pose.Glide);

            switch (pose)
            {
                case Pose.Stand: Walk(dt); break;
                case Pose.FreeFall: FreeFall(dt); break;
                case Pose.Glide: Glide(dt); break;
                case Pose.Dead: Dead(dt); break;
            }
        }

        void Walk(float dt)
        {
            float run = Mathf.Clamp01(speed / 6f);
            phase += dt * (2.2f + speed * 1.15f);
            float swing = Mathf.Sin(phase) * Mathf.Lerp(0f, 42f, Mathf.Clamp01(speed / 4.5f));
            leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
            rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            hips.localPosition = new Vector3(0f, 0.92f - Mathf.Abs(Mathf.Cos(phase)) * 0.04f * run, 0f);
            hips.localRotation = Quaternion.identity;
            torso.localRotation = Quaternion.Euler(run * 8f, 0f, 0f);
            head.localRotation = Quaternion.Euler(-run * 6f - (armed ? aimPitch * 0.4f : 0f), 0f, 0f);

            if (armed)
            {
                // Both hands on the gun: right arm forward, left arm across to the fore-grip.
                float pitch = -aimPitch;
                float raise = aiming ? 88f : 62f;
                float k = recoil * 10f;
                rightArm.localRotation = Quaternion.Euler(-raise + pitch * 0.9f - k, 0f, 8f);
                leftArm.localRotation = Quaternion.Euler(-raise - 6f + pitch * 0.9f - k, 28f, -10f);
                // Undo the arm's angle so the gun points where the body aims.
                if (gun != null) gun.localRotation = Quaternion.Euler(raise - aimPitch * 0.1f + k, 0f, 0f);
            }
            else
            {
                leftArm.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, 4f);
                rightArm.localRotation = Quaternion.Euler(swing * 0.8f, 0f, -4f);
            }
        }

        void FreeFall(float dt)
        {
            phase += dt * 6f;
            float flutter = Mathf.Sin(phase) * 3f;
            hips.localPosition = new Vector3(0f, 0.92f, 0f);
            hips.localRotation = Quaternion.Euler(80f, 0f, 0f);           // face down
            torso.localRotation = Quaternion.identity;
            head.localRotation = Quaternion.Euler(-50f, 0f, 0f);
            leftArm.localRotation = Quaternion.Euler(0f, 0f, -100f + flutter);
            rightArm.localRotation = Quaternion.Euler(0f, 0f, 100f - flutter);
            leftLeg.localRotation = Quaternion.Euler(20f, 0f, -18f - flutter);
            rightLeg.localRotation = Quaternion.Euler(20f, 0f, 18f + flutter);
        }

        void Glide(float dt)
        {
            phase += dt * 2f;
            hips.localPosition = new Vector3(0f, 0.92f, 0f);
            hips.localRotation = Quaternion.Euler(Mathf.Sin(phase) * 3f, 0f, 0f);
            torso.localRotation = Quaternion.identity;
            head.localRotation = Quaternion.identity;
            // Hands up on the brake toggles.
            leftArm.localRotation = Quaternion.Euler(0f, 0f, -160f);
            rightArm.localRotation = Quaternion.Euler(0f, 0f, 160f);
            leftLeg.localRotation = Quaternion.Euler(10f + Mathf.Sin(phase * 1.3f) * 6f, 0f, 0f);
            rightLeg.localRotation = Quaternion.Euler(14f - Mathf.Sin(phase * 1.3f) * 6f, 0f, 0f);
        }

        void Dead(float dt)
        {
            if (deathT < 0f || deathT >= 1f) return;
            deathT = Mathf.Min(1f, deathT + dt * 1.8f);
            float t = deathT * deathT;
            hips.localPosition = new Vector3(0f, Mathf.Lerp(0.92f, 0.16f, t), 0f);
            hips.localRotation = Quaternion.Euler(0f, 0f, -fallSide * 88f * t);
            leftLeg.localRotation = Quaternion.Euler(0f, 0f, -10f * t);
            rightLeg.localRotation = Quaternion.Euler(0f, 0f, 10f * t);
            leftArm.localRotation = Quaternion.Euler(0f, 0f, -70f * t);
            rightArm.localRotation = Quaternion.Euler(0f, 0f, 70f * t);
            head.localRotation = Quaternion.Euler(0f, 0f, fallSide * 20f * t);
        }
    }
}
