using System.Collections.Generic;
using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The sky bus that carries everyone over the campus at the start of a match: the campus
    /// shuttle under a hot-air balloon, pushed along by two propellers (model in SkyBusModel.cs).
    /// It flies a straight line at a fixed height, on a different heading each match, and everyone
    /// jumps out of its open rear door when they choose (or at the end of the line). The burner
    /// fires now and then with a flash and a roar; the propellers drone on a loop.
    /// </summary>
    public class SkyBus : MonoBehaviour
    {
        /// <summary>White, navy, glass, dark, saffron, yellow, red, green, metal (SkyBusModel slots).</summary>
        public Material[] palette;
        public Material flameMaterial;

        public Vector3 From { get; private set; }
        public Vector3 To { get; private set; }
        public float Speed { get; private set; }
        public float Duration { get { return (To - From).magnitude / Mathf.Max(1f, Speed); } }
        public float Elapsed { get; private set; }
        public bool Flying { get; private set; }
        public float Progress { get { return Mathf.Clamp01(Elapsed / Mathf.Max(0.01f, Duration)); } }
        /// <summary>Where jumpers leave from (just behind the open rear door).</summary>
        public Vector3 Door { get { return transform.TransformPoint(SkyBusModel.Door); } }

        readonly List<Transform> props = new List<Transform>();
        Transform flame;
        Light glow;
        AudioSource drone, roar;
        float burnLeft, nextBurn = 2f;

        void Awake()
        {
            SkyBusModel.Build(transform, palette, props);
            BuildFlame();
            drone = Loop("skybus_props", 0.75f, 0.8f);
            roar = gameObject.AddComponent<AudioSource>();
            roar.spatialBlend = 1f;
            roar.minDistance = 30f;
            roar.maxDistance = 1500f;
            roar.rolloffMode = AudioRolloffMode.Linear;
            roar.dopplerLevel = 0f;
            gameObject.SetActive(false);
        }

        AudioSource Loop(string clip, float volume, float pitch)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = SynthAudio.Get(clip);
            a.loop = true;
            a.spatialBlend = 1f;
            a.minDistance = 25f;
            a.maxDistance = 1500f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.dopplerLevel = 0f;
            a.volume = volume;
            a.pitch = pitch;
            return a;
        }

        void BuildFlame()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Burner Flame";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = SkyBusModel.Burner + new Vector3(0f, 0.9f, 0f);
            go.transform.localScale = new Vector3(0.6f, 1.6f, 0.6f);
            var r = go.GetComponent<Renderer>();
            if (flameMaterial != null) r.sharedMaterial = flameMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flame = go.transform;
            flame.gameObject.SetActive(false);
            glow = new GameObject("Burner Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = SkyBusModel.Burner + new Vector3(0f, 2f, 0f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.6f, 0.25f);
            glow.range = 22f;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
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
            if (drone != null) drone.Play();
        }

        public Vector3 PointAt(float time)
        {
            return Vector3.Lerp(From, To, Mathf.Clamp01(time / Mathf.Max(0.01f, Duration)));
        }

        /// <summary>When the sky bus passes nearest to a ground point (seconds from the start).</summary>
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
            float dt = Time.deltaTime;
            foreach (var p in props) p.Rotate(0f, 0f, 900f * dt, Space.Self);
            Burner(dt);
            if (!Flying) return;
            Elapsed += dt;
            transform.position = PointAt(Elapsed);
            // The bus swings a little under the balloon; the whole thing never pitches like a helicopter.
            Vector3 d = To - From;
            d.y = 0f;
            transform.rotation = Quaternion.LookRotation(d.normalized) *
                                 Quaternion.Euler(Mathf.Sin(Time.time * 0.45f) * 1.5f, 0f, Mathf.Sin(Time.time * 0.6f + 1f) * 2f);
            if (Elapsed > Duration + 20f)
            {
                Flying = false;
                gameObject.SetActive(false);
            }
        }

        /// <summary>A burst from the burner every few seconds: the flame, an orange glow and a roar.</summary>
        void Burner(float dt)
        {
            nextBurn -= dt;
            if (nextBurn <= 0f)
            {
                burnLeft = Random.Range(1.2f, 2.2f);
                nextBurn = burnLeft + Random.Range(3f, 7f);
                if (roar != null) roar.PlayOneShot(SynthAudio.Get("burner"), 0.9f);
            }
            bool on = burnLeft > 0f;
            burnLeft -= dt;
            if (flame != null)
            {
                flame.gameObject.SetActive(on);
                if (on) flame.localScale = new Vector3(0.6f, 1.4f + Mathf.PerlinNoise(Time.time * 9f, 0f) * 0.8f, 0.6f);
            }
            if (glow != null) glow.intensity = Mathf.MoveTowards(glow.intensity, on ? 3f : 0f, dt * 12f);
        }
    }
}
