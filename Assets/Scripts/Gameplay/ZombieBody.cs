using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>
    /// A blocky zombie made from primitives, with a code-driven shamble: legs swing, arms reach
    /// forward, the torso lurches, and on death the body topples over. A stand-in until real
    /// rigged models arrive in the polish phase.
    /// </summary>
    public class ZombieBody : MonoBehaviour
    {
        public Transform torso, head, leftArm, rightArm, leftLeg, rightLeg;
        public Renderer[] skinParts, shirtParts, pantsParts;

        float phase;
        float attackT = -1f;
        float deathT = -1f;
        float lean;
        float headTilt;
        Vector3 bodyRestPos;
        float fallSide = 1f;

        /// <summary>Builds the limb hierarchy under root. Called once for the spawner's template.</summary>
        public static ZombieBody Build(Transform root, Material skin, Material shirt, Material pants)
        {
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(root, false);
            var body = bodyGo.AddComponent<ZombieBody>();

            body.leftLeg = Limb(bodyGo.transform, "Left Leg", new Vector3(-0.11f, 0.86f, 0f), new Vector3(0.15f, 0.86f, 0.17f), pants);
            body.rightLeg = Limb(bodyGo.transform, "Right Leg", new Vector3(0.11f, 0.86f, 0f), new Vector3(0.15f, 0.86f, 0.17f), pants);

            var torsoPivot = new GameObject("Torso").transform;
            torsoPivot.SetParent(bodyGo.transform, false);
            torsoPivot.localPosition = new Vector3(0f, 0.86f, 0f);
            body.torso = torsoPivot;
            Box(torsoPivot, "Chest", new Vector3(0f, 0.31f, 0f), new Vector3(0.42f, 0.6f, 0.24f), shirt);

            body.leftArm = Limb(torsoPivot, "Left Arm", new Vector3(-0.28f, 0.56f, 0f), new Vector3(0.12f, 0.66f, 0.13f), shirt);
            body.rightArm = Limb(torsoPivot, "Right Arm", new Vector3(0.28f, 0.56f, 0f), new Vector3(0.12f, 0.66f, 0.13f), shirt);
            // Bare forearms and hands in skin colour.
            Box(body.leftArm, "Left Hand", new Vector3(0f, -0.58f, 0f), new Vector3(0.1f, 0.2f, 0.1f), skin);
            Box(body.rightArm, "Right Hand", new Vector3(0f, -0.58f, 0f), new Vector3(0.1f, 0.2f, 0.1f), skin);

            var headPivot = new GameObject("Head").transform;
            headPivot.SetParent(torsoPivot, false);
            headPivot.localPosition = new Vector3(0f, 0.62f, 0f);
            body.head = headPivot;
            Box(headPivot, "Skull", new Vector3(0f, 0.13f, 0.01f), new Vector3(0.22f, 0.26f, 0.24f), skin);
            Box(headPivot, "Neck", new Vector3(0f, 0.0f, 0f), new Vector3(0.1f, 0.08f, 0.1f), skin);

            body.skinParts = new[]
            {
                body.leftArm.Find("Left Hand").GetComponent<Renderer>(),
                body.rightArm.Find("Right Hand").GetComponent<Renderer>(),
                headPivot.Find("Skull").GetComponent<Renderer>(),
                headPivot.Find("Neck").GetComponent<Renderer>(),
            };
            body.shirtParts = new[]
            {
                torsoPivot.Find("Chest").GetComponent<Renderer>(),
                body.leftArm.GetChild(0).GetComponent<Renderer>(),
                body.rightArm.GetChild(0).GetComponent<Renderer>(),
            };
            body.pantsParts = new[]
            {
                body.leftLeg.GetChild(0).GetComponent<Renderer>(),
                body.rightLeg.GetChild(0).GetComponent<Renderer>(),
            };
            return body;
        }

        // A limb pivots at its top (shoulder or hip) and hangs down by its length.
        static Transform Limb(Transform parent, string name, Vector3 pivot, Vector3 size, Material mat)
        {
            var p = new GameObject(name).transform;
            p.SetParent(parent, false);
            p.localPosition = pivot;
            Box(p, name + " Mesh", new Vector3(0f, -size.y / 2f, 0f), size, mat);
            return p;
        }

        static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        public void Dress(Material skin, Material shirt, Material pants)
        {
            if (skin != null) foreach (var r in skinParts) r.sharedMaterial = skin;
            if (shirt != null) foreach (var r in shirtParts) r.sharedMaterial = shirt;
            if (pants != null) foreach (var r in pantsParts) r.sharedMaterial = pants;
        }

        public void ResetPose()
        {
            deathT = -1f;
            attackT = -1f;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            phase = Random.value * 10f;
            headTilt = Random.Range(-25f, 25f);
            bodyRestPos = Vector3.zero;
        }

        public void PlayAttack()
        {
            attackT = 0f;
        }

        public void PlayDeath()
        {
            deathT = 0f;
            fallSide = Random.value < 0.5f ? -1f : 1f;
        }

        /// <summary>speed in m/s; called every frame by ZombieAI.</summary>
        public void Animate(float speed, bool alert)
        {
            float dt = Time.deltaTime;
            if (deathT >= 0f)
            {
                // Topple backwards (or sideways) over 0.7 s, then lie still.
                deathT += dt;
                float k = Mathf.Clamp01(deathT / 0.7f);
                k = k * k;
                transform.localRotation = Quaternion.Euler(-85f * k, 0f, 12f * fallSide * k);
                transform.localPosition = bodyRestPos + new Vector3(0f, 0.12f * k, 0f);
                leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, Quaternion.Euler(-150f, 0f, -20f), dt * 4f);
                rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, Quaternion.Euler(-150f, 0f, 20f), dt * 4f);
                return;
            }

            float gait = Mathf.Clamp01(speed / 3f);
            phase += dt * (2.2f + speed * 2.4f);
            float swing = Mathf.Sin(phase) * Mathf.Lerp(8f, 38f, gait) * (speed > 0.1f ? 1f : 0.15f);
            leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
            rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);

            // Arms reach forward, higher when it has seen you, with a slow sway.
            float reach = alert ? -85f : -55f;
            float sway = Mathf.Sin(phase * 0.5f) * 8f;
            float armL = reach + sway, armR = reach - sway;
            if (attackT >= 0f)
            {
                attackT += dt;
                float a = attackT / 0.55f;
                float strike = a < 0.45f ? Mathf.Lerp(0f, -50f, a / 0.45f) : Mathf.Lerp(-50f, 40f, (a - 0.45f) / 0.55f);
                armL += strike;
                armR += strike;
                if (a >= 1f) attackT = -1f;
            }
            leftArm.localRotation = Quaternion.Euler(armL, 0f, -4f);
            rightArm.localRotation = Quaternion.Euler(armR, 0f, 4f);

            lean = Mathf.Lerp(lean, 8f + gait * 18f, dt * 3f);
            torso.localRotation = Quaternion.Euler(lean, Mathf.Sin(phase) * 6f * gait, Mathf.Sin(phase * 0.5f) * 5f);
            head.localRotation = Quaternion.Euler(-lean * 0.5f + Mathf.Sin(phase * 0.7f) * 6f, 0f, headTilt);
            transform.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Cos(phase)) * 0.04f * gait, 0f);
        }
    }
}
