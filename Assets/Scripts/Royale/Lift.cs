using System.Collections.Generic;
using Jagti.Gameplay;
using Jagti.Player;
using Jagti.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jagti.Royale
{
    /// <summary>
    /// A lift from a building's ground floor straight to its roof (RoyaleBuilder finds a free stretch
    /// of wall in a lobby, foyer or corridor, and a clear spot on the roof above for the lift's head
    /// hut). E at the doors rides it, up or down. The ride is a short cutaway: the doors open, the
    /// camera pulls out of the building, the building turns to a see-through shell with its floors and
    /// columns drawn as a glowing frame, and the lit car rises up the shaft with you in it (the floor
    /// it passes lights up) until the camera swoops back to you at the roof doors. Space skips.
    /// Nothing can hit you on the way. Only one ride runs at a time (it's the local player's).
    /// </summary>
    public class Lift : MonoBehaviour
    {
        public static readonly List<Lift> All = new List<Lift>();
        /// <summary>The ride under way, if any (for the HUD).</summary>
        public static Lift Active { get; private set; }

        // Placed by RoyaleBuilder.
        public string building;
        /// <summary>Ground-floor doors: on the floor at the wall face; the yaw they face (into the room).</summary>
        public Vector3 bottom;
        public float bottomYaw;
        /// <summary>The roof hut: middle of its floor on the roof; the yaw its doors face.</summary>
        public Vector3 top;
        public float topYaw;
        public float baseY, floorOffset = 0.6f, levelHeight = 3.4f;
        /// <summary>The building's blocks for the frame: centre x, z, width, depth (round blocks in roundWings).</summary>
        public Vector4[] wings;
        public float[] wingTops;
        public bool[] roundWings;
        /// <summary>The building model and its interior shell (their renderers go see-through, their colliders are ignored by the camera).</summary>
        public Transform buildingRoot, interiorRoot;
        public Material[] palette;
        public Material ghost, glow, glowHot;
        /// <summary>False where the interior already has lift doors on that wall (the academic block's pavilions).</summary>
        public bool buildGroundDoors = true;
        /// <summary>Above 0 the hut stands on a steel deck this high over the roof (roofs covered in solar panels).</summary>
        public float deck;

        public const float HutW = 2.6f, HutD = 2.4f, HutH = 2.8f;
        const float CarW = 1.6f, CarH = 2.4f;

        // ------------------------------------------------------------------ the ride
        enum Phase { Idle, Doors, PullOut, Rise, Return }
        Phase phase;
        float t, travel;
        bool up;
        PlayerMover rider;
        FirstPersonController fpc;
        HumanBody body;
        Vector3 from, to;               // car floor at the start and end of the ride
        Vector3 eyeStart, exitAt;
        Quaternion eyeStartRot;
        float exitYaw, orbitYaw, orbitR;
        Renderer[] ghostRenderers;
        Material[][] savedMaterials;
        ShadowCastingMode[] savedShadows;
        AudioSource hum;

        Transform groundLeafL, groundLeafR, roofLeafL, roofLeafR;
        GameObject skeleton, car;
        MeshRenderer[] levelRenderers;
        float[] levelY;
        int hotLevel = -1;

        /// <summary>Floor shown on the HUD: G, 1, 2, ... or R at the roof.</summary>
        public string FloorLabel { get; private set; }
        /// <summary>0 to 1 along the ride.</summary>
        public float RideProgress { get; private set; }
        public bool GoingUp { get { return up; } }

        Vector3 Fwd(float yaw) { return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; }
        Vector3 GroundFront { get { return bottom + Fwd(bottomYaw) * 0.75f; } }
        Vector3 RoofFront { get { return top + Fwd(topYaw) * (HutD / 2f + 0.75f); } }
        /// <summary>The car's floor at the bottom of the shaft (behind the ground-floor wall).</summary>
        Vector3 ShaftBottom { get { return bottom - Fwd(bottomYaw) * (CarW / 2f + 0.25f); } }
        /// <summary>Where the car stops: straight above the shaft, level with the roof (the hut may stand a few metres off).</summary>
        Vector3 ShaftTop { get { var p = ShaftBottom; p.y = top.y; return p; } }

        void OnEnable()
        {
            All.Add(this);
            InteriorZone.Hidden = false;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (Active == this) Abort();
        }

        void Start()
        {
            BuildDoors();
            BuildHut();
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = SynthAudio.Get("lift_hum");
            hum.loop = true;
            hum.spatialBlend = 0f;
            hum.playOnAwake = false;
            hum.volume = 0.5f;
        }

        /// <summary>A lift whose doors are within reach of p, and whether the ride from there goes up.</summary>
        public static Lift Near(Vector3 p, out bool goingUp)
        {
            goingUp = true;
            if (Active != null) return null;
            foreach (var l in All)
            {
                if (l.Close(p, l.GroundFront)) { goingUp = true; return l; }
                if (l.Close(p, l.RoofFront)) { goingUp = false; return l; }
            }
            return null;
        }

        bool Close(Vector3 p, Vector3 door)
        {
            Vector3 d = p - door;
            return Mathf.Abs(d.y) < 1.3f && d.x * d.x + d.z * d.z < 1.25f * 1.25f;
        }

        /// <summary>Starts the ride; the mover hands over its player (walker and camera off).</summary>
        public void Begin(PlayerMover mover, bool goingUp)
        {
            if (Active != null) return;
            Active = this;
            rider = mover;
            fpc = mover.GetComponent<FirstPersonController>();
            var diver = mover.GetComponent<SkyDiver>();
            body = diver != null ? diver.body : null;
            up = goingUp;
            from = up ? ShaftBottom : ShaftTop;
            to = up ? ShaftTop : ShaftBottom;
            exitAt = up ? RoofFront : GroundFront;
            exitYaw = up ? topYaw : bottomYaw;
            travel = Mathf.Clamp(Mathf.Abs(to.y - from.y) / 7f, 2.2f, 4.5f);
            var cam = fpc != null ? fpc.cameraPivot : null;
            eyeStart = cam != null ? cam.position : mover.transform.position + Vector3.up * 1.6f;
            eyeStartRot = cam != null ? cam.rotation : mover.transform.rotation;
            if (fpc != null) fpc.cameraHeld = true;
            phase = Phase.Doors;
            t = 0f;
            RideProgress = 0f;
            FloorLabel = up ? "G" : "R";
            SynthAudio.Play2D("lift_ding", 0.7f);
        }

        void LateUpdate()
        {
            if (Active != this || phase == Phase.Idle) return;
            if (rider == null || fpc == null) { Abort(); return; }
            var me = rider.GetComponent<Combatant>();
            if (me != null && !me.Alive) { Abort(); return; }
            float dt = Time.deltaTime;
            t += dt;
            var cam = fpc.cameraPivot;
            bool skip = !GameState.Paused && t > 0.2f && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E));

            switch (phase)
            {
                case Phase.Doors:
                {
                    // The doors slide open in front of you.
                    SetLeaves(up, Mathf.Clamp01(t / 0.45f));
                    if (t >= 0.5f || skip) NextPhase(Phase.PullOut);
                    break;
                }
                case Phase.PullOut:
                {
                    // Into the car; the building turns to glass and the camera pulls out to the side.
                    float u = Smooth(t / 0.85f);
                    SetLeaves(up, 1f - Mathf.Clamp01(t / 0.4f));
                    PlaceRider(from, up ? bottomYaw : topYaw);
                    Vector3 p; Quaternion r;
                    Orbit(0f, out p, out r);
                    cam.SetPositionAndRotation(Vector3.Lerp(eyeStart, p, u), Quaternion.Slerp(eyeStartRot, r, u));
                    if (t >= 0.85f) NextPhase(Phase.Rise);
                    else if (skip) NextPhase(Phase.Return);
                    break;
                }
                case Phase.Rise:
                {
                    float u = Mathf.Clamp01(t / travel);
                    RideProgress = u;
                    float s = Smooth(u);
                    Vector3 c = Vector3.Lerp(from, to, s);
                    PlaceRider(c, up ? bottomYaw : topYaw);
                    if (car != null) car.transform.position = c;
                    Vector3 p; Quaternion r;
                    Orbit(u, out p, out r);
                    cam.SetPositionAndRotation(p, r);
                    UpdateFloor(c.y);
                    hum.pitch = 0.8f + 0.5f * Mathf.Sin(u * Mathf.PI);
                    if (u >= 1f || skip) NextPhase(Phase.Return);
                    break;
                }
                case Phase.Return:
                {
                    // Back to your own eyes at the doors at the other end, which slide open.
                    float u = Smooth(t / 0.75f);
                    SetLeaves(!up, Mathf.Clamp01((t - 0.25f) / 0.4f));
                    Vector3 eye = exitAt + Vector3.up * 1.6f;
                    Quaternion look = Quaternion.Euler(8f, exitYaw, 0f);
                    cam.SetPositionAndRotation(Vector3.Lerp(orbitEndPos, eye, u), Quaternion.Slerp(orbitEndRot, look, u));
                    if (t >= 0.75f) Finish();
                    break;
                }
            }
        }

        Vector3 orbitEndPos;
        Quaternion orbitEndRot;

        void NextPhase(Phase next)
        {
            if (next == Phase.PullOut)
            {
                PickOrbit();
                Ghost(true);
                if (body != null)
                {
                    body.gameObject.SetActive(true);
                    body.pose = HumanBody.Pose.Stand;
                    body.armed = false;
                    body.speed = 0f;
                    body.crouch = 0f;
                }
                hum.pitch = 0.8f;
                hum.Play();
            }
            if (next == Phase.Rise && car != null) car.transform.position = from;
            if (next == Phase.Return)
            {
                // The camera flies back from wherever it is now.
                var cam = fpc != null ? fpc.cameraPivot : null;
                orbitEndPos = cam != null ? cam.position : eyeStart;
                orbitEndRot = cam != null ? cam.rotation : eyeStartRot;
                Ghost(false);
                hum.Stop();
                PlaceRider(to, exitYaw);
                FloorLabel = up ? "R" : "G";
                RideProgress = 1f;
                SynthAudio.Play2D("lift_ding", 0.7f);
            }
            phase = next;
            t = 0f;
        }

        void Finish()
        {
            SetLeaves(true, 0f);
            SetLeaves(false, 0f);
            phase = Phase.Idle;
            Active = null;
            if (fpc != null) fpc.cameraHeld = false;
            var mover = rider;
            rider = null;
            if (mover != null) mover.LeaveLift(exitAt + Vector3.up * 0.05f, exitYaw);
        }

        /// <summary>Ends the ride at once (death, or the lift going away): everything back as it was.</summary>
        public void Abort()
        {
            if (Active != this) return;
            Ghost(false);
            if (hum != null) hum.Stop();
            SetLeaves(true, 0f);
            SetLeaves(false, 0f);
            phase = Phase.Idle;
            Active = null;
            if (fpc != null) fpc.cameraHeld = false;
            var mover = rider;
            rider = null;
            if (mover != null) mover.LeaveLift(up ? GroundFront : RoofFront, up ? bottomYaw : topYaw);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        void PlaceRider(Vector3 at, float yaw)
        {
            if (rider == null) return;
            rider.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            if (body != null) body.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void UpdateFloor(float y)
        {
            int f = Mathf.RoundToInt((y - baseY - floorOffset) / levelHeight);
            float roofY = Mathf.Max(from.y, to.y);
            FloorLabel = y > roofY - 0.8f ? "R" : f <= 0 ? "G" : f.ToString();
            // Light up the floor the car is passing.
            int hot = -1;
            if (levelY != null)
                for (int i = 0; i < levelY.Length; i++)
                    if (Mathf.Abs(levelY[i] - y) < levelHeight * 0.5f) hot = i;
            if (hot == hotLevel || levelRenderers == null) return;
            if (hotLevel >= 0 && hotLevel < levelRenderers.Length) levelRenderers[hotLevel].sharedMaterials = new[] { glow, ghost };
            if (hot >= 0) levelRenderers[hot].sharedMaterials = new[] { glowHot, ghost };
            if (hot >= 0) SynthAudio.Play2D("clink", 0.15f);
            hotLevel = hot;
        }

        // ------------------------------------------------------------------ the cutaway camera

        /// <summary>The side to watch from: the clearest of 16 directions round the shaft (other buildings, hills and trees counted against).</summary>
        void PickOrbit()
        {
            float h = Mathf.Abs(to.y - from.y);
            orbitR = Mathf.Clamp(h * 0.9f + 16f, 26f, 46f);
            Vector3 mid = Vector3.Lerp(from, to, 0.5f) + Vector3.up * 1.2f;
            float bestCost = float.MaxValue;
            orbitYaw = bottomYaw;
            for (int i = 0; i < 16; i++)
            {
                float yaw = bottomYaw + i * 22.5f;
                Vector3 p = OrbitPoint(mid, yaw, 0.5f);
                float cost = i * 0.01f;
                var hits = Physics.RaycastAll(mid, (p - mid).normalized, (p - mid).magnitude, ~0, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                    if (!Own(hit.collider)) cost += hit.collider is TerrainCollider ? 3f : 1f;
                if (cost < bestCost) { bestCost = cost; orbitYaw = yaw; }
            }
        }

        Vector3 OrbitPoint(Vector3 target, float yaw, float u)
        {
            Vector3 p = target + Fwd(yaw + u * 28f) * orbitR + Vector3.up * (4f + orbitR * 0.14f);
            float g = HazeZone.GroundY(p.x, p.z) + 2.5f;
            if (p.y < g) p.y = g;
            return p;
        }

        void Orbit(float u, out Vector3 pos, out Quaternion rot)
        {
            Vector3 c = Vector3.Lerp(from, to, Smooth(u)) + Vector3.up * 1.2f;
            pos = OrbitPoint(c, orbitYaw, u);
            rot = Quaternion.LookRotation(c - pos, Vector3.up);
        }

        bool Own(Collider c)
        {
            if (c == null) return false;
            var tr = c.transform;
            return (buildingRoot != null && tr.IsChildOf(buildingRoot)) || (interiorRoot != null && tr.IsChildOf(interiorRoot)) ||
                   tr.IsChildOf(transform) || (rider != null && tr.IsChildOf(rider.transform));
        }

        // ------------------------------------------------------------------ see-through building

        void Ghost(bool on)
        {
            InteriorZone.Hidden = on;
            if (on)
            {
                if (skeleton == null) BuildSkeleton();
                skeleton.SetActive(true);
                car.SetActive(true);
                if (ghostRenderers == null)
                    ghostRenderers = buildingRoot != null ? buildingRoot.GetComponentsInChildren<Renderer>() : new Renderer[0];
                savedMaterials = new Material[ghostRenderers.Length][];
                savedShadows = new ShadowCastingMode[ghostRenderers.Length];
                for (int i = 0; i < ghostRenderers.Length; i++)
                {
                    var r = ghostRenderers[i];
                    if (r == null) continue;
                    savedMaterials[i] = r.sharedMaterials;
                    savedShadows[i] = r.shadowCastingMode;
                    var g = new Material[savedMaterials[i].Length];
                    for (int m = 0; m < g.Length; m++) g[m] = ghost;
                    r.sharedMaterials = g;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            else
            {
                if (skeleton != null) skeleton.SetActive(false);
                if (car != null) car.SetActive(false);
                if (savedMaterials != null)
                    for (int i = 0; i < ghostRenderers.Length; i++)
                    {
                        var r = ghostRenderers[i];
                        if (r == null || savedMaterials[i] == null) continue;
                        r.sharedMaterials = savedMaterials[i];
                        r.shadowCastingMode = savedShadows[i];
                    }
                savedMaterials = null;
                if (hotLevel >= 0 && levelRenderers != null && hotLevel < levelRenderers.Length)
                    levelRenderers[hotLevel].sharedMaterials = new[] { glow, ghost };
                hotLevel = -1;
            }
        }

        /// <summary>
        /// The frame: for every storey a glowing outline of each block with a faint floor plate (one
        /// renderer per storey, so the one the car passes can light up), the columns, the shaft's rails,
        /// and the car. Built the first time the lift is used.
        /// </summary>
        void BuildSkeleton()
        {
            skeleton = new GameObject("Lift Frame - " + building);
            skeleton.transform.SetParent(transform, true);
            skeleton.transform.position = Vector3.zero;
            skeleton.transform.rotation = Quaternion.identity;

            int n = wings != null ? wings.Length : 0;
            float highest = 0f;
            for (int w = 0; w < n; w++) highest = Mathf.Max(highest, wingTops[w]);
            int storeys = Mathf.Max(1, Mathf.CeilToInt((highest - floorOffset) / levelHeight - 0.2f));
            levelY = new float[storeys + 1];
            levelRenderers = new MeshRenderer[storeys + 1];
            for (int L = 0; L <= storeys; L++)
            {
                var k = new MeshKit(2);
                float y = baseY + floorOffset + L * levelHeight;
                levelY[L] = y;
                for (int w = 0; w < n; w++)
                {
                    float roof = baseY + wingTops[w];
                    float wy = y;
                    if (wy > roof + 0.3f) continue;               // this block is lower
                    if (wy > roof - levelHeight * 0.6f) wy = roof; // its roof line
                    Outline(k, wings[w], roundWings != null && w < roundWings.Length && roundWings[w], wy, L == 0 ? 0.0f : 1f);
                }
                levelRenderers[L] = Piece("Storey " + L, k, new[] { glow, ghost });
            }

            // Columns at the corners and along the walls, and the shaft.
            var c = new MeshKit(1);
            c.slot = 0;
            for (int w = 0; w < n; w++)
            {
                float y0 = baseY, y1 = baseY + wingTops[w];
                var poly = Corners(wings[w], roundWings != null && w < roundWings.Length && roundWings[w]);
                for (int i = 0; i < poly.Length; i++)
                {
                    Vector3 a = poly[i], b = poly[(i + 1) % poly.Length];
                    int steps = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / 8f));
                    bool round = roundWings != null && w < roundWings.Length && roundWings[w];
                    if (round && (i % 2) == 1) continue;
                    for (int s = 0; s < (round ? 1 : steps); s++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                        Bar(c, new Vector3(p.x, y0, p.z), new Vector3(p.x, y1, p.z), 0.12f);
                    }
                }
            }
            Vector3 lo = ShaftBottom, hi = ShaftTop + Vector3.up * HutH;
            Vector3 fx = Quaternion.Euler(0f, bottomYaw, 0f) * Vector3.right * (CarW / 2f + 0.05f);
            Vector3 fz = Fwd(bottomYaw) * (CarW / 2f + 0.05f);
            foreach (var off in new[] { fx + fz, fx - fz, -fx + fz, -fx - fz })
                Bar(c, lo + off, hi + off, 0.07f);
            Piece("Columns and Shaft", c, new[] { glow });

            // The car: a glowing cage with glass sides and a light that travels with it.
            car = new GameObject("Lift Car");
            car.transform.SetParent(transform, true);
            car.transform.SetPositionAndRotation(ShaftBottom, Quaternion.Euler(0f, bottomYaw, 0f));
            var ck = new MeshKit(2);
            float hw = CarW / 2f;
            ck.slot = 0;
            foreach (float y in new[] { 0f, CarH })
            {
                Bar(ck, new Vector3(-hw, y, -hw), new Vector3(hw, y, -hw), 0.06f);
                Bar(ck, new Vector3(-hw, y, hw), new Vector3(hw, y, hw), 0.06f);
                Bar(ck, new Vector3(-hw, y, -hw), new Vector3(-hw, y, hw), 0.06f);
                Bar(ck, new Vector3(hw, y, -hw), new Vector3(hw, y, hw), 0.06f);
            }
            foreach (var p in new[] { new Vector3(-hw, 0f, -hw), new Vector3(hw, 0f, -hw), new Vector3(-hw, 0f, hw), new Vector3(hw, 0f, hw) })
                Bar(ck, p, p + Vector3.up * CarH, 0.06f);
            ck.slot = 1;
            ck.Box(new Vector3(0f, CarH / 2f, 0f), new Vector3(CarW - 0.04f, CarH - 0.04f, CarW - 0.04f));
            var cgo = new GameObject("Car Mesh");
            cgo.transform.SetParent(car.transform, false);
            cgo.AddComponent<MeshFilter>().sharedMesh = ck.Finish("Lift Car", false);
            var cr = cgo.AddComponent<MeshRenderer>();
            cr.sharedMaterials = new[] { glowHot, ghost };
            cr.shadowCastingMode = ShadowCastingMode.Off;
            var lgo = new GameObject("Car Light");
            lgo.transform.SetParent(car.transform, false);
            lgo.transform.localPosition = new Vector3(0f, CarH - 0.3f, 0f);
            var light = lgo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.75f, 0.92f, 1f);
            light.range = 9f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;

            skeleton.SetActive(false);
            car.SetActive(false);
        }

        MeshRenderer Piece(string name, MeshKit k, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(skeleton.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.Finish("Lift Frame " + name, false);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static Vector3[] Corners(Vector4 w, bool round)
        {
            if (!round)
                return new[]
                {
                    new Vector3(w.x - w.z / 2f, 0f, w.y - w.w / 2f), new Vector3(w.x + w.z / 2f, 0f, w.y - w.w / 2f),
                    new Vector3(w.x + w.z / 2f, 0f, w.y + w.w / 2f), new Vector3(w.x - w.z / 2f, 0f, w.y + w.w / 2f),
                };
            var pts = new Vector3[20];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = i * Mathf.PI * 2f / pts.Length;
                pts[i] = new Vector3(w.x + Mathf.Cos(a) * w.z / 2f, 0f, w.y + Mathf.Sin(a) * w.w / 2f);
            }
            return pts;
        }

        /// <summary>One block's outline at height y (slot 0) and its floor plate (slot 1, faint).</summary>
        static void Outline(MeshKit k, Vector4 w, bool round, float y, float plate)
        {
            var poly = Corners(w, round);
            k.slot = 0;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Length];
                a.y = y; b.y = y;
                Bar(k, a, b, 0.11f);
            }
            if (plate <= 0f) return;
            k.slot = 1;
            if (round) k.Tube(new Vector3(w.x, y - 0.02f, w.y), 0f, 0.02f, new Vector2(w.z / 2f, w.w / 2f), new Vector2(w.z / 2f, w.w / 2f), 20, true);
            else k.Box(new Vector3(w.x, y - 0.01f, w.y), new Vector3(w.z, 0.02f, w.w));
        }

        /// <summary>A thin square bar from a to b.</summary>
        static void Bar(MeshKit k, Vector3 a, Vector3 b, float thick)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Vector3 euler = Quaternion.FromToRotation(Vector3.forward, d / len).eulerAngles;
            k.Box((a + b) * 0.5f, new Vector3(thick, thick, len + thick), 1f, 1f, euler);
        }

        // ------------------------------------------------------------------ doors and the roof hut

        void SetLeaves(bool ground, float open)
        {
            var l = ground ? groundLeafL : roofLeafL;
            var r = ground ? groundLeafR : roofLeafR;
            if (l == null || r == null) return;
            // The leaves fold away towards the jambs (they can't slide into a wall that isn't there).
            float sx = 1f - open * 0.9f;
            l.localScale = new Vector3(sx, 1f, 1f);
            r.localScale = new Vector3(sx, 1f, 1f);
            l.localPosition = new Vector3(-(0.58f - 0.28f * sx), 0f, 0f);
            r.localPosition = new Vector3(0.58f - 0.28f * sx, 0f, 0f);
        }

        /// <summary>
        /// A steel door set flat on a wall: frame, a dark recess, two sliding leaves, a call panel and
        /// the floor display above. Origin on the floor at the wall face, +Z out of the wall.
        /// </summary>
        GameObject DoorSet(Transform parent, Vector3 at, float yaw, out Transform leafL, out Transform leafR)
        {
            const int Metal = LootVisuals.Metal, Black = LootVisuals.Black, Yellow = LootVisuals.Yellow, Red = LootVisuals.Red;
            var root = new GameObject("Lift Doors");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            var k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = Black;
            k.Box(new Vector3(0f, 1.08f, 0.02f), new Vector3(1.16f, 2.16f, 0.03f));            // the recess
            k.slot = Metal;
            k.Box(new Vector3(-0.66f, 1.12f, 0.06f), new Vector3(0.16f, 2.3f, 0.1f));          // jambs and head
            k.Box(new Vector3(0.66f, 1.12f, 0.06f), new Vector3(0.16f, 2.3f, 0.1f));
            k.Box(new Vector3(0f, 2.3f, 0.06f), new Vector3(1.48f, 0.16f, 0.1f));
            k.Box(new Vector3(0f, 0.01f, 0.12f), new Vector3(1.3f, 0.02f, 0.2f));              // sill
            k.Box(new Vector3(0.95f, 1.15f, 0.04f), new Vector3(0.14f, 0.3f, 0.05f));          // call panel
            k.slot = Yellow;
            k.Box(new Vector3(0.95f, 1.2f, 0.07f), new Vector3(0.05f, 0.05f, 0.02f));
            k.Box(new Vector3(0.95f, 1.1f, 0.07f), new Vector3(0.05f, 0.05f, 0.02f));
            k.slot = Black;
            k.Box(new Vector3(0f, 2.55f, 0.04f), new Vector3(0.5f, 0.2f, 0.05f));              // floor display
            k.slot = Red;
            k.Box(new Vector3(0f, 2.55f, 0.07f), new Vector3(0.3f, 0.1f, 0.01f));
            Visual("Frame", root.transform, k);

            var leaf = new MeshKit(LootVisuals.PaletteSize);
            leaf.slot = Metal;
            leaf.Box(new Vector3(0f, 1.06f, 0.05f), new Vector3(0.56f, 2.1f, 0.04f));
            leaf.slot = Black;
            leaf.Box(new Vector3(0f, 1.06f, 0.072f), new Vector3(0.02f, 2.0f, 0.005f));
            var mesh = leaf.Finish("Lift Leaf", false);
            leafL = Leaf("Leaf L", root.transform, mesh);
            leafR = Leaf("Leaf R", root.transform, mesh);
            return root;
        }

        Transform Leaf(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = palette;
            return go.transform;
        }

        void Visual(string name, Transform parent, MeshKit k)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.Finish("Lift " + name, false);
            go.AddComponent<MeshRenderer>().sharedMaterials = palette;
        }

        void BuildDoors()
        {
            if (!buildGroundDoors) return;
            DoorSet(transform, bottom, bottomYaw, out groundLeafL, out groundLeafR);
            SetLeaves(true, 0f);
        }

        /// <summary>The lift's head on the roof: a small plastered hut with a concrete cap and the doors.</summary>
        void BuildHut()
        {
            const int White = LootVisuals.White, Metal = LootVisuals.Metal, Khaki = LootVisuals.Khaki;
            var hut = new GameObject("Lift Head");
            hut.transform.SetParent(transform, true);
            hut.transform.SetPositionAndRotation(top, Quaternion.Euler(0f, topYaw, 0f));
            var k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = White;
            k.Box(new Vector3(0f, HutH / 2f, 0f), new Vector3(HutW, HutH, HutD));
            k.slot = Metal;
            k.Box(new Vector3(0f, HutH + 0.1f, 0f), new Vector3(HutW + 0.3f, 0.2f, HutD + 0.3f));
            k.slot = Khaki;
            k.Box(new Vector3(0f, 0.1f, 0f), new Vector3(HutW + 0.06f, 0.2f, HutD + 0.06f));     // plinth
            Visual("Hut", hut.transform, k);
            var col = hut.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, HutH / 2f, 0f);
            col.size = new Vector3(HutW, HutH, HutD);
            DoorSet(hut.transform, top + Fwd(topYaw) * (HutD / 2f), topYaw, out roofLeafL, out roofLeafR);
            SetLeaves(false, 0f);
            if (deck > 0f) BuildDeck(hut.transform);
        }

        /// <summary>A steel deck on legs over the solar panels: the hut at the back, room to step out at the front, rails on the sides.</summary>
        void BuildDeck(Transform hut)
        {
            const int Metal = LootVisuals.Metal, Yellow = LootVisuals.Yellow;
            var k = new MeshKit(LootVisuals.PaletteSize);
            k.slot = Metal;
            k.Box(new Vector3(0f, -0.06f, 1.1f), new Vector3(3.0f, 0.12f, 4.6f));
            foreach (float x in new[] { -1.4f, 1.4f })
                foreach (float z in new[] { -1.1f, 1.1f, 3.3f })
                    k.Box(new Vector3(x, -deck / 2f, z), new Vector3(0.1f, deck, 0.1f));
            foreach (float x in new[] { -1.46f, 1.46f })
            {
                k.Box(new Vector3(x, 1.0f, 2.3f), new Vector3(0.05f, 0.05f, 2.1f));
                k.Box(new Vector3(x, 0.5f, 3.3f), new Vector3(0.05f, 1.0f, 0.05f));
                k.Box(new Vector3(x, 0.5f, 1.3f), new Vector3(0.05f, 1.0f, 0.05f));
            }
            k.slot = Yellow;
            k.Box(new Vector3(0f, 0.005f, 3.36f), new Vector3(3.0f, 0.012f, 0.12f));      // the edge, marked
            Visual("Deck", hut, k);
            var col = hut.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -0.06f, 1.1f);
            col.size = new Vector3(3.0f, 0.12f, 4.6f);
        }
    }
}
