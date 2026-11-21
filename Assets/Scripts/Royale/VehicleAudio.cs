using Jagti.Audio;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A vehicle's sound. Engines are three loops (idle, low revs under load, high revs) crossfaded by
    /// engine speed and pitched between, so the note climbs through the gears (the 4x4 shifts; the
    /// scooter and the auto-rickshaw have belt drives and just wind up; the utility cart is electric
    /// and whines). Tyres roll on road or rumble on dirt and grass, squeal when the vehicle slides, and
    /// thump over kerbs; the rider hears the wind at speed and a whoosh on boost. The helicopter is its
    /// rotor (pitched by the spool) and a turbine spooling up when someone takes off.
    /// </summary>
    public class VehicleAudio : MonoBehaviour
    {
        public Vehicle vehicle;

        SoundLoop idle, low, high, roll, dirt, skid, wind, rotor;
        float idleHz, lowHz, highHz, maxHz;
        float rpm;
        bool wasBoosting, wasDriven;
        Vector3 lastPos;
        float lastVy, surfaceTimer;
        bool onDirt;
        int gear;

        void Start()
        {
            if (vehicle == null) vehicle = GetComponent<Vehicle>();
            if (vehicle == null) { enabled = false; return; }
            lastPos = transform.position;
            if (vehicle.kind == Vehicle.Kind.Heli)
            {
                rotor = SoundLoop.Create(gameObject, "rotor");
                if (rotor != null) { rotor.VolumeEase = 1.5f; rotor.PitchEase = 2f; }
                return;
            }
            string n = vehicle.kind == Vehicle.Kind.Scooter ? "scooter" : vehicle.kind == Vehicle.Kind.Auto ? "auto" :
                       vehicle.kind == Vehicle.Kind.Jeep ? "jeep" : "cart";
            // The firing rates the loops were rendered at (tools/audio/vehicles.py), and how high the engine winds.
            switch (vehicle.kind)
            {
                case Vehicle.Kind.Scooter: idleHz = 14f; lowHz = 38f; highHz = 75f; maxHz = 95f; break;
                case Vehicle.Kind.Auto: idleHz = 28f; lowHz = 55f; highHz = 95f; maxHz = 115f; break;
                case Vehicle.Kind.Jeep: idleHz = 24f; lowHz = 60f; highHz = 120f; maxHz = 140f; break;
                default: idleHz = 220f; lowHz = 420f; highHz = 900f; maxHz = 1100f; break;
            }
            idle = SoundLoop.Create(gameObject, n + "_idle");
            low = SoundLoop.Create(gameObject, n + "_low");
            high = SoundLoop.Create(gameObject, n + "_high");
            roll = SoundLoop.Create(gameObject, "tyres_road");
            dirt = SoundLoop.Create(gameObject, "tyres_dirt");
            skid = SoundLoop.Create(gameObject, "tyres_skid");
            wind = SoundLoop.Create(gameObject, "wind_rush", false);
            foreach (var l in new[] { idle, low, high }) if (l != null) { l.PitchEase = 10f; l.VolumeEase = 4f; }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || vehicle == null) return;
            Vector3 vel = (transform.position - lastPos) / dt;
            lastPos = transform.position;
            bool driven = vehicle.Driver != null && !vehicle.Wrecked;
            bool mine = driven && vehicle.Driver.isPlayer;

            if (vehicle.Wrecked)
            {
                foreach (var l in new[] { idle, low, high, roll, dirt, skid, wind, rotor }) if (l != null) l.Volume = 0f;
                return;
            }

            if (driven && !wasDriven)
            {
                SoundSystem.Play("door_open", transform.position, 0.8f);
                if (vehicle.kind == Vehicle.Kind.Heli) SoundSystem.Play("turbine_start", transform.position);
                else if (vehicle.kind != Vehicle.Kind.Cart) SoundSystem.Play("engine_start", transform.position);
            }
            if (!driven && wasDriven && vehicle.kind != Vehicle.Kind.Heli)
            {
                if (vehicle.kind != Vehicle.Kind.Cart) SoundSystem.Play("engine_stop", transform.position);
                SoundSystem.Play("door_close", transform.position, 0.8f);
            }
            wasDriven = driven;

            if (vehicle.kind == Vehicle.Kind.Heli) { Heli(); return; }

            float speed = Mathf.Abs(vehicle.Speed);
            float s01 = Mathf.Clamp01(speed / Mathf.Max(1f, vehicle.maxSpeed));
            float throttle = Mathf.Abs(vehicle.Throttle);

            // Engine speed: the 4x4 has four gears; the others wind up continuously.
            float target;
            if (!driven) target = speed > 0.5f ? 0.15f : 0f;
            else if (vehicle.kind == Vehicle.Kind.Jeep)
            {
                float g = s01 * 4f;
                int newGear = Mathf.Clamp((int)g, 0, 3);
                if (newGear != gear && speed > 1f) SoundSystem.Play("bump", transform.position, 0.25f);
                gear = newGear;
                float inGear = gear == 3 ? Mathf.Clamp01(g - 3f) : g - gear;
                target = 0.25f + 0.65f * inGear * (0.7f + 0.3f * throttle) + (gear == 0 ? 0f : 0.08f);
            }
            else target = 0.18f + 0.8f * Mathf.Pow(s01, 0.8f) * (0.75f + 0.25f * throttle) + throttle * 0.08f;
            if (vehicle.Boosting) target += 0.12f;
            rpm = Mathf.MoveTowards(rpm, Mathf.Clamp01(target), dt * (target > rpm ? 1.6f : 1.1f));

            float hz = Mathf.Lerp(idleHz, maxHz, rpm);
            float load = driven ? Mathf.Lerp(0.55f, 1f, throttle) : 0.5f;
            float on = driven ? 1f : Mathf.Clamp01(speed / 3f) * 0.5f;
            // Weights: by how close the engine's note is to each loop's own (in octaves).
            float wi = Weight(hz, idleHz, 0.9f), wl = Weight(hz, lowHz, 0.8f), wh = Weight(hz, highHz, 0.8f);
            float sum = Mathf.Max(0.001f, wi + wl + wh);
            Layer(idle, wi / sum * on, hz / idleHz);
            Layer(low, wl / sum * on * load, hz / lowHz);
            Layer(high, wh / sum * on * load, hz / highHz);

            // Ground under the wheels, a few times a second.
            surfaceTimer -= dt;
            if (surfaceTimer <= 0f)
            {
                surfaceTimer = 0.3f;
                bool found;
                var surf = AcousticSpace.Under(transform.position + Vector3.up * 0.3f, out found);
                onDirt = surf == AcousticSpace.Surface.Grass || surf == AcousticSpace.Surface.Dirt || surf == AcousticSpace.Surface.Gravel;
            }
            float rollLevel = Mathf.Clamp01(speed / 14f);
            if (roll != null) { roll.Volume = onDirt ? 0f : rollLevel * 0.9f; roll.Pitch = 0.8f + rollLevel * 0.5f; }
            if (dirt != null) { dirt.Volume = onDirt ? rollLevel : 0f; dirt.Pitch = 0.8f + rollLevel * 0.4f; }

            // Sliding: the share of the velocity that is sideways.
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            float side = Mathf.Abs(Vector3.Dot(flat, transform.right));
            float slip = flat.magnitude > 4f ? Mathf.Clamp01((side - 2.5f) / 5f) : 0f;
            if (skid != null) { skid.Volume = onDirt ? slip * 0.3f : slip; skid.Pitch = 0.9f + slip * 0.2f; }

            // Thumps: a sudden change in vertical speed (kerbs, landing from a jump).
            float vy = vel.y;
            if (speed > 3f && Mathf.Abs(vy - lastVy) > 3.5f) SoundSystem.Play("bump", transform.position, Mathf.Clamp01(Mathf.Abs(vy - lastVy) / 9f));
            lastVy = vy;

            if (wind != null) { wind.Volume = mine ? Mathf.Clamp01((speed - 6f) / 20f) * 0.8f : 0f; wind.Pitch = 0.8f + s01 * 0.4f; }
            if (mine && vehicle.Boosting && !wasBoosting) SoundSystem.Play2D("boost", 0.9f);
            wasBoosting = vehicle.Boosting;
        }

        static float Weight(float hz, float layerHz, float width)
        {
            float oct = Mathf.Log(hz / layerHz, 2f);
            return Mathf.Exp(-(oct * oct) / (2f * width * width * 0.25f));
        }

        static void Layer(SoundLoop l, float vol, float pitch)
        {
            if (l == null) return;
            l.Volume = vol;
            l.Pitch = Mathf.Clamp(pitch, 0.5f, 2.2f);
        }

        void Heli()
        {
            if (rotor == null) return;
            float spool = vehicle.Spool;
            rotor.Volume = Mathf.Clamp01(spool * 1.2f);
            rotor.Pitch = 0.55f + spool * 0.45f + (vehicle.Boosting ? 0.08f : 0f);
        }
    }
}
