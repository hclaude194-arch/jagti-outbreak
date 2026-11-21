using System.Collections.Generic;
using Jagti.Player;
using Jagti.Royale;
using UnityEngine;

namespace Jagti.Audio
{
    /// <summary>
    /// Footsteps for everyone on foot: the player, the bots and other players online, by the surface
    /// under each foot (grass, dirt, gravel, concrete, tiled floors, wood, steel stairs, water), walking
    /// or running, plus jumps and landings. A step falls every stride of distance actually covered, so
    /// the rhythm follows the real speed. Bots' running steps carry about 38 m, walking about 22 m and
    /// crouching hardly at all: you can hear someone coming round the corner.
    /// </summary>
    public class FootstepSystem : MonoBehaviour
    {
        class Walker
        {
            public Transform t;
            public Combatant c;
            public FirstPersonController fpc;
            public PlayerMover mover;
            public Vector3 last;
            public float stride;
            public bool grounded = true;
            public float airTime;
            public float fallSpeed;
            public float nextGroundCheck;
            public bool left;
        }

        readonly Dictionary<Transform, Walker> walkers = new Dictionary<Transform, Walker>();
        readonly List<Transform> gone = new List<Transform>();
        float scan;

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            scan -= dt;
            if (scan <= 0f)
            {
                scan = 1f;
                Scan();
            }
            gone.Clear();
            foreach (var kv in walkers)
            {
                var w = kv.Value;
                if (w.t == null) { gone.Add(kv.Key); continue; }
                Step(w, dt);
            }
            foreach (var t in gone) walkers.Remove(t);
        }

        void Scan()
        {
            foreach (var c in Combatant.All)
                if (c != null && !walkers.ContainsKey(c.transform)) Add(c.transform, c);
            // The zombie mode's player has no Combatant.
            var fpc = FindObjectOfType<FirstPersonController>();
            if (fpc != null && !walkers.ContainsKey(fpc.transform)) Add(fpc.transform, fpc.GetComponent<Combatant>());
        }

        void Add(Transform t, Combatant c)
        {
            walkers[t] = new Walker
            {
                t = t, c = c, last = t.position,
                fpc = t.GetComponent<FirstPersonController>(),
                mover = t.GetComponent<PlayerMover>(),
            };
        }

        void Step(Walker w, float dt)
        {
            Vector3 p = w.t.position;
            Vector3 delta = p - w.last;
            w.last = p;
            float vy = delta.y / dt;
            delta.y = 0f;
            float moved = delta.magnitude;
            float speed = moved / dt;
            bool isPlayer = w.fpc != null && (w.c == null || w.c.isPlayer);

            if (w.c != null && (!w.c.Alive || !w.c.Landed)) { w.grounded = true; w.airTime = 0f; return; }
            if (w.fpc != null && (w.fpc.externalControl || !w.fpc.enabled)) { w.airTime = 0f; return; }
            if (speed > 12f) { w.stride = 0f; return; }               // riding, zipping, teleported
            float far2 = (p - SoundSystem.ListenerPosition).sqrMagnitude;
            if (!isPlayer && far2 > 45f * 45f) return;

            bool grounded;
            if (w.fpc != null) grounded = w.fpc.IsGrounded;
            else if (Time.time >= w.nextGroundCheck)
            {
                w.nextGroundCheck = Time.time + 0.08f;
                grounded = Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 0.6f, ~0, QueryTriggerInteraction.Ignore);
            }
            else grounded = w.grounded;

            if (!grounded)
            {
                if (w.grounded && vy > 2.5f) Foot(w, p, "jump", isPlayer ? 0.6f : 0.8f, null);
                w.airTime += dt;
                w.fallSpeed = Mathf.Max(w.fallSpeed, -vy);
                w.grounded = false;
                return;
            }
            if (!w.grounded)
            {
                if (w.airTime > 0.3f && w.fallSpeed > 3.5f)
                    Foot(w, p, "land_", Mathf.Clamp01(0.45f + w.fallSpeed / 14f) * (isPlayer ? 0.8f : 1f), Surface(w, p));
                w.airTime = 0f;
                w.fallSpeed = 0f;
                w.stride = 0f;
                w.grounded = true;
            }

            if (speed < 0.4f) { w.stride = Mathf.Min(w.stride, 0.3f); return; }
            bool running = speed > 5.2f;
            bool crouched = w.fpc != null && w.fpc.Crouching;
            float strideLength = running ? 1.25f : speed > 2.6f ? 0.85f : 0.62f;
            w.stride += moved;
            if (w.stride < strideLength) return;
            w.stride = 0f;
            w.left = !w.left;
            float vol = crouched ? 0.3f : running ? 1f : 0.75f;
            if (isPlayer) vol *= 0.55f;    // your own feet sit under everything else
            Foot(w, p, running && !crouched ? "step_run_" : "step_walk_", vol, Surface(w, p));
        }

        string Surface(Walker w, Vector3 p)
        {
            if (w.mover != null && w.mover.Wading) return "water";
            bool found;
            var s = AcousticSpace.Under(p, out found);
            if (s == AcousticSpace.Surface.Glass) s = AcousticSpace.Surface.Tile;
            return AcousticSpace.SurfaceNames[(int)s];
        }

        void Foot(Walker w, Vector3 p, string evt, float vol, string surface)
        {
            string name = surface != null ? evt + surface : evt;
            if (!SoundBank.Has(name)) name = surface != null ? evt + "concrete" : evt;
            Vector3 at = p + w.t.right * (w.left ? -0.12f : 0.12f) + Vector3.up * 0.05f;
            SoundSystem.Play(name, at, vol);
        }
    }
}
