using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The transport helicopter that carries everyone over the campus at the start of a match.
    /// It flies a straight line at a fixed height, on a different heading each match, and everyone
    /// jumps out from its side door when they choose (or at the end of the line).
    /// The model is built from primitives: a fat cabin with glazed nose, tail boom and fin, skids,
    /// and turning main and tail rotors. The rotor thump is a looping synthesized sound.
    /// </summary>
    public class Helicopter : MonoBehaviour
    {
        public Material bodyMaterial;
        public Material stripeMaterial;
        public Material glassMaterial;
        public Material darkMaterial;

        public Vector3 From { get; private set; }
        public Vector3 To { get; private set; }
        public float Speed { get; private set; }
        public float Duration { get { return (To - From).magnitude / Mathf.Max(1f, Speed); } }
        public float Elapsed { get; private set; }
        public bool Flying { get; private set; }
        public float Progress { get { return Mathf.Clamp01(Elapsed / Mathf.Max(0.01f, Duration)); } }
        /// <summary>Where jumpers leave from (the side door, a little below the cabin floor).</summary>
        public Vector3 Door { get { return transform.TransformPoint(new Vector3(1.4f, -0.8f, 0.2f)); } }

        Transform rotor, tailRotor;
        AudioSource sound;

        void Awake()
        {
            BuildModel();
            sound = gameObject.AddComponent<AudioSource>();
            sound.clip = SynthAudio.Get("rotor");
            sound.loop = true;
            sound.spatialBlend = 1f;
            sound.minDistance = 25f;
            sound.maxDistance = 1500f;
            sound.rolloffMode = AudioRolloffMode.Linear;
            sound.dopplerLevel = 0f;
            sound.volume = 0.9f;
            gameObject.SetActive(false);
        }

        public void Fly(Vector3 from, Vector3 to, float speed)
        {
            From = from;
            To = to;
            Speed = speed;
            Elapsed = 0f;
            Flying = true;
            gameObject.SetActive(true);
            transform.position = from;
            Vector3 d = to - from;
            d.y = 0f;
            transform.rotation = Quaternion.LookRotation(d.normalized);
            if (sound != null) sound.Play();
        }

        public Vector3 PointAt(float time)
        {
            return Vector3.Lerp(From, To, Mathf.Clamp01(time / Mathf.Max(0.01f, Duration)));
        }

        /// <summary>When the helicopter passes nearest to a ground point (seconds from the start).</summary>
        public float TimeClosestTo(Vector3 p)
        {
            Vector3 a = From, b = To;
            a.y = b.y = p.y = 0f;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
            return t * Duration;
        }

        void Update()
        {
            if (rotor != null) rotor.Rotate(0f, 1400f * Time.deltaTime, 0f, Space.Self);
            if (tailRotor != null) tailRotor.Rotate(2600f * Time.deltaTime, 0f, 0f, Space.Self);
            if (!Flying) return;
            Elapsed += Time.deltaTime;
            transform.position = PointAt(Elapsed);
            // A slight nose-down cruise attitude with a gentle sway.
            Vector3 d = To - From;
            d.y = 0f;
            transform.rotation = Quaternion.LookRotation(d.normalized) * Quaternion.Euler(6f, 0f, Mathf.Sin(Time.time * 0.6f) * 2f);
            if (Elapsed > Duration + 20f)
            {
                Flying = false;
                gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ model

        void BuildModel()
        {
            var root = transform;
            // Cabin: a stretched sphere with a boxy belly; glazed nose.
            Part(root, PrimitiveType.Sphere, new Vector3(0f, 0f, 0.6f), new Vector3(3.2f, 2.8f, 6.4f), Vector3.zero, bodyMaterial);
            Part(root, PrimitiveType.Cube, new Vector3(0f, -0.7f, 0.2f), new Vector3(2.8f, 1.2f, 4.6f), Vector3.zero, bodyMaterial);
            Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.25f, 2.9f), new Vector3(2.5f, 1.9f, 2.4f), Vector3.zero, glassMaterial);
            // Side stripe and the open door frame.
            Part(root, PrimitiveType.Cube, new Vector3(0f, -0.2f, 0.4f), new Vector3(3.02f, 0.28f, 4.2f), Vector3.zero, stripeMaterial);
            Part(root, PrimitiveType.Cube, new Vector3(1.42f, -0.3f, 0.1f), new Vector3(0.05f, 1.5f, 1.6f), Vector3.zero, darkMaterial);
            // Engine housing and mast.
            Part(root, PrimitiveType.Cube, new Vector3(0f, 1.45f, -0.3f), new Vector3(1.6f, 0.7f, 3.0f), Vector3.zero, bodyMaterial);
            Part(root, PrimitiveType.Cylinder, new Vector3(0f, 2.0f, 0f), new Vector3(0.3f, 0.3f, 0.3f), Vector3.zero, darkMaterial);
            // Tail boom, fin and stabiliser.
            Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, -5.6f), new Vector3(0.7f, 3.4f, 0.7f), new Vector3(90f, 0f, 0f), bodyMaterial);
            Part(root, PrimitiveType.Cube, new Vector3(0f, 1.6f, -9.0f), new Vector3(0.18f, 2.2f, 1.2f), new Vector3(-20f, 0f, 0f), bodyMaterial);
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0.6f, -8.2f), new Vector3(2.6f, 0.12f, 0.7f), Vector3.zero, stripeMaterial);
            // Skids.
            foreach (float s in new[] { -1f, 1f })
            {
                Part(root, PrimitiveType.Cylinder, new Vector3(s * 1.3f, -2.1f, 0.4f), new Vector3(0.14f, 2.6f, 0.14f), new Vector3(90f, 0f, 0f), darkMaterial);
                Part(root, PrimitiveType.Cube, new Vector3(s * 1.15f, -1.65f, 1.6f), new Vector3(0.1f, 0.9f, 0.1f), new Vector3(0f, 0f, -s * 18f), darkMaterial);
                Part(root, PrimitiveType.Cube, new Vector3(s * 1.15f, -1.65f, -0.9f), new Vector3(0.1f, 0.9f, 0.1f), new Vector3(0f, 0f, -s * 18f), darkMaterial);
            }
            // Main rotor: four long, thin blades.
            rotor = new GameObject("Main Rotor").transform;
            rotor.SetParent(root, false);
            rotor.localPosition = new Vector3(0f, 2.35f, 0f);
            for (int i = 0; i < 4; i++)
                Part(rotor, PrimitiveType.Cube, Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, 0f, 4.6f), new Vector3(0.45f, 0.06f, 9.2f), new Vector3(0f, i * 90f, 0f), darkMaterial);
            Part(rotor, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.6f, 0.12f, 0.6f), Vector3.zero, darkMaterial);
            tailRotor = new GameObject("Tail Rotor").transform;
            tailRotor.SetParent(root, false);
            tailRotor.localPosition = new Vector3(0.25f, 1.7f, -9.3f);
            Part(tailRotor, PrimitiveType.Cube, Vector3.zero, new Vector3(0.05f, 2.2f, 0.18f), Vector3.zero, darkMaterial);
            Part(tailRotor, PrimitiveType.Cube, Vector3.zero, new Vector3(0.05f, 0.18f, 2.2f), Vector3.zero, darkMaterial);
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
