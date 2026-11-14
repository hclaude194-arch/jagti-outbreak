using System.Collections.Generic;
using Jagti.Gameplay;
using Jagti.Player;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// The player's hands in a match. 1-3 pick a gun slot (the wheel cycles), left click fires
    /// (hold for automatic guns; burst guns fire three), right click aims down the sights, R reloads,
    /// E picks up what you look at, X drops the gun in hand, Q uses a bandage or medkit, Z drinks a
    /// chai flask, G throws a grenade, 4 a smoke grenade, V switches between the over-the-shoulder
    /// camera and first person (aiming always goes to first person). With no gun, left click punches.
    /// Ammo for the guns you carry is picked up by walking over it.
    /// Each gun has its own recoil pattern: the kick climbs over a spray and drifts to one side the
    /// same way every time, so it can be learnt and pulled against. Grips and compensators tame it.
    /// View models reuse the loot models, held upright, with their attachments fitted.
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    public class PlayerArsenal : MonoBehaviour
    {
        public Material[] palette;          // LootVisuals palette
        public Material flashMaterial;
        public GameObject grenadeTemplate;  // inactive template made by RoyaleBuilder
        public GameObject smokeTemplate;    // inactive smoke grenade template
        public bool thirdPerson = true;
        public float shoulderDistance = 2.4f;

        public string Prompt { get; private set; }
        public LootDrop Target { get; private set; }
        public bool Aiming { get; private set; }
        public float ReloadProgress { get { return reloadTime > 0f ? 1f - reloadLeft / reloadTime : 0f; } }
        public bool Reloading { get { return reloadLeft > 0f; } }
        public bool Scoped { get { var g = combatant.loadout.Current; return Aiming && g != null && g.AimFov < 30f && aimBlend > 0.9f; } }
        /// <summary>The red dot or 2x sight is up (the HUD draws its reticle).</summary>
        public bool DotSight { get { var g = combatant.loadout.Current; return Aiming && g != null && !Scoped && aimBlend > 0.8f && (g.Sight == 0 || g.Sight == 1); } }
        public bool InThirdPerson { get { return thirdPerson && aimBlend < 0.5f; } }
        public float AimBlend { get { return aimBlend; } }
        /// <summary>Recent firing, 0..1 (the HUD opens the crosshair with it).</summary>
        public float Bloom { get; private set; }
        public float HitMarker { get; private set; }
        public bool HitWasHead { get; private set; }
        public bool HitWasKill { get; private set; }

        Combatant combatant;
        FirstPersonController fpc;
        SkyDiver diver;
        Camera cam;
        float baseFov;
        float nextShot;
        float reloadLeft, reloadTime;
        float aimBlend;
        float kick;
        float switchT;
        float bob;
        float autoPickTimer;
        Transform viewRoot;
        GameObject[] views = new GameObject[RoyaleItems.Guns.Length];
        Light flash;
        float flashT;
        readonly List<LootDrop> near = new List<LootDrop>();
        int shownGun = -2;
        int shotIndex;
        float lastShotTime = -9f;
        int burstLeft;
        ParticleSystem shells;

        static readonly Vector3 HipPos = new Vector3(0.24f, -0.22f, 0.5f);
        static readonly Vector3 AimPos = new Vector3(0f, -0.105f, 0.36f);

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            fpc = GetComponent<FirstPersonController>();
            diver = GetComponent<SkyDiver>();
        }

        void OnEnable()
        {
            Ballistics.PlayerHit += OnPlayerHit;
        }

        void OnDisable()
        {
            Ballistics.PlayerHit -= OnPlayerHit;
        }

        void Start()
        {
            cam = fpc.cameraPivot != null ? fpc.cameraPivot.GetComponent<Camera>() : Camera.main;
            if (cam != null) baseFov = cam.fieldOfView;
            BuildViews();
        }

        void OnPlayerHit(bool head, bool kill)
        {
            HitMarker = 1f;
            HitWasHead = head;
            HitWasKill = kill;
            SynthAudio.Play2D(kill ? "kill_confirm" : head ? "hit_head" : "hit_flesh", kill ? 0.8f : 0.5f);
        }

        bool Active
        {
            get
            {
                return combatant.Alive && !GameState.Paused && fpc.enabled && !fpc.externalControl &&
                       (diver == null || diver.Current == SkyDiver.State.Landed);
            }
        }

        void Update()
        {
            HitMarker = Mathf.MoveTowards(HitMarker, 0f, Time.deltaTime * 3f);
            flashT -= Time.deltaTime;
            if (flash != null && flash.enabled && flashT <= 0f) flash.enabled = false;
            if (!Active)
            {
                Prompt = null;
                Target = null;
                SetAim(false);
                ShowView(-1);
                if (!combatant.Alive && diver != null && diver.body != null && diver.Current == SkyDiver.State.Landed)
                    diver.body.gameObject.SetActive(false);
                return;
            }
            var l = combatant.loadout;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Bloom = Mathf.MoveTowards(Bloom, 0f, Time.deltaTime * 2.5f);
            if (Input.GetKeyDown(KeyCode.V)) thirdPerson = !thirdPerson;

            // Slots.
            if (Input.GetKeyDown(KeyCode.Alpha1)) Select(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Select(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Select(2);
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel != 0f) Cycle(wheel > 0f ? -1 : 1);
            switchT -= Time.deltaTime;

            var gun = l.Current;
            SetAim(locked && gun != null && Input.GetMouseButton(1) && !Reloading);

            // Reloading.
            if (reloadLeft > 0f)
            {
                reloadLeft -= Time.deltaTime;
                if (reloadLeft <= 0f) l.Reload(gun);
            }
            if (gun != null && Input.GetKeyDown(KeyCode.R)) StartReload();

            // Shooting or punching. A burst gun finishes its burst once the trigger is pulled.
            if (burstLeft > 0 && (gun == null || gun.loaded <= 0 || Reloading)) burstLeft = 0;
            if (burstLeft > 0 && Time.time >= nextShot && switchT <= 0f)
            {
                Fire(gun);
                if (--burstLeft == 0) nextShot = Time.time + gun.def.burstGap;
            }
            else if (locked && switchT <= 0f && Time.time >= nextShot)
            {
                bool trigger = gun != null && gun.def.automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
                if (trigger)
                {
                    combatant.CancelHeal();
                    if (gun == null) Punch();
                    else if (Reloading) { }
                    else if (gun.loaded > 0)
                    {
                        Fire(gun);
                        if (gun.def.burst > 1) burstLeft = gun.def.burst - 1;
                    }
                    else
                    {
                        SynthAudio.Play2D("empty", 0.6f);
                        nextShot = Time.time + 0.25f;
                        StartReload();
                    }
                }
            }

            // Heals and grenades.
            if (Input.GetKeyDown(KeyCode.Q)) Heal();
            if (Input.GetKeyDown(KeyCode.Z) && !combatant.StartHeal(ItemKind.ChaiFlask)) Say(l.chai > 0 ? "Already healing" : "No chai flask");
            if (Input.GetKeyDown(KeyCode.G)) Throw(false);
            if (Input.GetKeyDown(KeyCode.Alpha4)) Throw(true);
            if (Input.GetKeyDown(KeyCode.X) && gun != null) DropCurrent();

            FindTarget();
            if (Input.GetKeyDown(KeyCode.E) && Target != null) PickUp(Target, true);
            AutoPickup();

            ShowView(gun != null && !InThirdPerson ? gun.def.id : -1);
            if (gun != null && shownGun >= 0 && views[shownGun] != null) LootVisuals.Dress(views[shownGun].transform, gun, palette);
            AnimateView();
            UpdateCamera(gun);
        }

        /// <summary>Over the shoulder normally; first person while aiming or when V is toggled off.</summary>
        void UpdateCamera(GunItem gun)
        {
            fpc.thirdPersonDistance = InThirdPerson ? shoulderDistance : 0f;
            fpc.shoulderOffset = 0.55f;
            fpc.crouchEnabled = true;
            var body = diver != null ? diver.body : null;
            if (body == null) return;
            bool show = InThirdPerson;
            if (body.gameObject.activeSelf != show) body.gameObject.SetActive(show);
            if (!show) return;
            var l = combatant.loadout;
            body.pose = HumanBody.Pose.Stand;
            body.speed = fpc.HorizontalSpeed;
            body.crouch = fpc.CrouchAmount;
            body.armed = gun != null;
            body.ShowGun(gun != null ? gun.def.id : -1);
            body.Dress(gun);
            body.ShowGear(l.vest, l.helmet, l.backpack);
            body.aiming = Input.GetMouseButton(0) || Time.time - lastShotTime < 1.5f;
            body.aimPitch = -fpc.Pitch;
        }

        // ------------------------------------------------------------------ guns

        void Select(int slot)
        {
            var l = combatant.loadout;
            if (slot < 0 || slot >= Loadout.Slots || l.guns[slot] == null || slot == l.selected) return;
            l.selected = slot;
            reloadLeft = 0f;
            switchT = 0.35f;
            combatant.CancelHeal();
            SynthAudio.Play2D("equip", 0.5f);
        }

        void Cycle(int dir)
        {
            var l = combatant.loadout;
            int s = l.selected;
            for (int i = 0; i < Loadout.Slots; i++)
            {
                s = (s + dir + Loadout.Slots) % Loadout.Slots;
                if (l.guns[s] != null) { Select(s); return; }
            }
        }

        void StartReload()
        {
            var gun = combatant.loadout.Current;
            if (gun == null || Reloading || gun.loaded >= gun.Magazine) return;
            if (combatant.loadout.CountFor(gun) <= 0)
            {
                Say("No " + RoyaleItems.AmmoNames[(int)gun.def.ammo]);
                return;
            }
            reloadTime = reloadLeft = gun.def.reloadTime;
            SynthAudio.Play2D("reload", 0.7f);
        }

        void Fire(GunItem gun)
        {
            var def = gun.def;
            nextShot = Time.time + def.interval;
            gun.loaded--;
            kick = 1f;
            Bloom = Mathf.Min(1f, Bloom + 0.25f);

            // The recoil pattern: count shots in this spray.
            shotIndex = Time.time - lastShotTime > Mathf.Max(0.35f, def.interval * 1.6f) ? 0 : shotIndex + 1;
            lastShotTime = Time.time;

            float spread = Mathf.Lerp(def.hipSpread, def.aimSpread, aimBlend) * gun.SpreadScale;
            if (fpc.HorizontalSpeed > 1f) spread *= 1.5f;
            if (!fpc.IsGrounded) spread *= 2f;
            if (fpc.Crouching) spread *= 0.8f;
            if (def.automatic && aimBlend < 0.5f) spread *= 1f + Mathf.Min(shotIndex, 10) * 0.04f;

            // Where the shot goes: in first person straight from the eye; over the shoulder, from the
            // head towards whatever is under the crosshair (so walls beside the camera don't matter).
            Transform eye = fpc.cameraPivot;
            Vector3 origin = eye.position, dir = eye.forward;
            if (InThirdPerson)
            {
                Vector3 aimPoint;
                Combatant ignoredVictim;
                bool ignoredHead;
                float ignoredDistance;
                Ballistics.Trace(combatant, eye.position, eye.forward, def.range * 1.6f, out aimPoint, out ignoredVictim, out ignoredHead, out ignoredDistance);
                origin = fpc.HeadPosition;
                dir = (aimPoint - origin).normalized;
            }
            Ballistics.Fire(combatant, gun, origin, dir, spread);

            float up = def.recoil * gun.RecoilScale * (Aiming ? 0.7f : 1f) * (fpc.Crouching ? 0.8f : 1f) * (1f + 0.04f * Mathf.Min(shotIndex, 12));
            float wander = Mathf.PerlinNoise(shotIndex * 0.37f, def.id * 3.1f + 0.5f) * 2f - 1f;
            float side = up * def.recoilSide * (wander + def.recoilBias);
            fpc.AddRecoil(up, side);
            fpc.Shake(def.gunClass == GunClass.Shotgun ? 0.35f : def.bolt ? 0.3f : 0.12f);
            bool loud = def.gunClass == GunClass.Marksman || def.gunClass == GunClass.Shotgun;
            SynthAudio.Play2D(gun.Suppressed ? "suppressed" : def.sound, loud ? 1f : 0.8f);
            if (def.bolt) Invoke("BoltSound", 0.45f);
            if (flash != null && !gun.Suppressed)
            {
                flash.enabled = true;
                flashT = 0.045f;
                flash.transform.position = InThirdPerson ? fpc.HeadPosition + dir * 0.9f - Vector3.up * 0.25f
                                                         : viewRoot.TransformPoint(new Vector3(0f, 0.05f, 0.6f));
            }
            if (shells != null && def.gunClass != GunClass.Shotgun && def.burst == 0 && !def.bolt && def.id != 5 && !InThirdPerson) shells.Emit(1);
            if (gun.loaded == 0 && combatant.loadout.CountFor(gun) > 0) Invoke("StartReload", 0.3f);
        }

        void BoltSound()
        {
            SynthAudio.Play2D("bolt", 0.5f);
        }

        void Punch()
        {
            nextShot = Time.time + 0.55f;
            kick = 1f;
            SynthAudio.Play2D("swing", 0.5f);
            Transform eye = fpc.cameraPivot;
            Vector3 end;
            Combatant victim;
            bool head;
            float distance;
            if (Ballistics.Trace(combatant, eye.position, eye.forward, 1.8f, out end, out victim, out head, out distance) && victim != null)
            {
                bool wasAlive = victim.Alive;
                victim.ReceiveHit(head ? 24f : 16f, head, combatant, "fists", transform.position);
                OnPlayerHit(head, wasAlive && !victim.Alive);
            }
        }

        void SetAim(bool on)
        {
            Aiming = on;
            float target = on ? 1f : 0f;
            var cur = combatant.loadout.Current;
            float rate = cur != null ? 1f / Mathf.Max(0.08f, cur.def.aimTime) : 6f;
            aimBlend = Mathf.MoveTowards(aimBlend, target, Time.deltaTime * rate);
            fpc.speedScale = Mathf.Lerp(1f, 0.55f, aimBlend);
            if (cam != null)
            {
                var g = cur;
                float zoom = g != null ? g.AimFov : baseFov;
                cam.fieldOfView = Mathf.Lerp(baseFov, zoom, aimBlend);
            }
        }

        // ------------------------------------------------------------------ items

        void Heal()
        {
            var l = combatant.loadout;
            var h = combatant.Health;
            bool bandage = l.bandages > 0 && h.current < RoyaleItems.BandageCap && (l.medkits == 0 || h.current > 45f);
            if (combatant.IsHealing) { Say("Already healing"); return; }
            if (bandage ? combatant.StartHeal(ItemKind.Bandage) : combatant.StartHeal(ItemKind.Medkit))
            {
                SynthAudio.Play2D("bandage", 0.6f);
                return;
            }
            if (h.current >= h.max - 1f) Say("Health is full");
            else if (l.bandages == 0 && l.medkits == 0) Say("No bandages or medkits");
            else if (l.medkits == 0) Say("Bandages only heal to " + (int)RoyaleItems.BandageCap);
        }

        void Throw(bool smoke)
        {
            var l = combatant.loadout;
            if (smoke ? l.smokes <= 0 : l.grenades <= 0) { Say(smoke ? "No smoke grenades" : "No grenades"); return; }
            var template = smoke ? smokeTemplate : grenadeTemplate;
            if (template == null) return;
            if (smoke) l.smokes--; else l.grenades--;
            Transform eye = fpc.cameraPivot;
            Vector3 from = InThirdPerson ? fpc.HeadPosition + eye.forward * 0.5f : eye.position + eye.forward * 0.6f;
            var go = Instantiate(template, from, Quaternion.identity);
            go.SetActive(true);
            var g = go.GetComponent<Grenade>();
            g.thrower = combatant;
            go.GetComponent<Rigidbody>().AddForce(eye.forward * 17f + Vector3.up * 3.5f, ForceMode.VelocityChange);
            SynthAudio.Play2D("swing", 0.5f);
        }

        void DropCurrent()
        {
            var l = combatant.loadout;
            var gun = l.Current;
            if (gun == null || LootDirector.Instance == null) return;
            // Rounds in the magazine go back to the carried ammo.
            l.ammo[(int)gun.def.ammo] += gun.loaded;
            gun.loaded = 0;
            LootDirector.Instance.DropGun(transform.position + transform.forward * 0.8f, gun);
            l.guns[l.selected] = null;
            reloadLeft = 0f;
            Cycle(1);
            if (l.Current == null) l.selected = -1;
            Say("Dropped " + gun.def.name);
        }

        void FindTarget()
        {
            Prompt = null;
            Target = null;
            var loot = LootDirector.Instance;
            if (loot == null) return;
            Transform eye = fpc.cameraPivot;
            loot.Near(transform.position, 2.6f, near);
            float best = -1f;
            foreach (var d in near)
            {
                Vector3 to = d.position + Vector3.up * 0.1f - eye.position;
                float dist = to.magnitude;
                float dot = Vector3.Dot(eye.forward, to / Mathf.Max(0.01f, dist));
                if (dot < 0.55f && dist > 1.3f) continue;
                float score = dot * 2f - dist * 0.4f;
                if (score > best)
                {
                    best = score;
                    Target = d;
                }
            }
            if (Target != null)
            {
                bool wants = combatant.loadout.Wants(Target.kind, Target.value, Target.tier);
                string verb = Target.kind == ItemKind.Gun && !wants ? "swap for " : "pick up ";
                if (!wants && Target.kind != ItemKind.Gun) verb = "full: ";
                Prompt = "[E] " + verb + Target.Label;
            }
        }

        void PickUp(LootDrop d, bool manual)
        {
            var l = combatant.loadout;
            var loot = LootDirector.Instance;
            GunItem swapped;
            int oldLevel;
            int left = l.Take(d.kind, d.value, d.tier, d.count, manual, out swapped, out oldLevel);
            if (left == d.count)
            {
                if (manual) Say(d.kind == ItemKind.Vest || d.kind == ItemKind.Helmet || d.kind == ItemKind.Backpack
                    ? "You already have one as good" : "No room for " + RoyaleItems.ItemName(d.kind, d.value, d.tier));
                return;
            }
            string label = RoyaleItems.ItemName(d.kind, d.value, d.tier) + (d.count - left > 1 ? " x" + (d.count - left) : "");
            if (left > 0) d.count = left;
            else loot.Remove(d);
            if (swapped != null)
            {
                l.ammo[(int)swapped.def.ammo] += swapped.loaded;
                swapped.loaded = 0;
                loot.DropGun(transform.position, swapped);
                reloadLeft = 0f;
            }
            if (oldLevel > 0) loot.DropReplaced(transform.position, d.kind, oldLevel);
            if (d.kind == ItemKind.Gun) switchT = 0.35f;
            Say("+ " + label);
            SynthAudio.Play2D(d.kind == ItemKind.Gun ? "equip" : "pickup", 0.5f);
        }

        void AutoPickup()
        {
            autoPickTimer -= Time.deltaTime;
            if (autoPickTimer > 0f) return;
            autoPickTimer = 0.2f;
            var loot = LootDirector.Instance;
            if (loot == null) return;
            loot.Near(transform.position, 1.3f, near);
            var l = combatant.loadout;
            foreach (var d in near)
            {
                if (d.kind != ItemKind.Ammo || !l.Wants(d.kind, d.value, d.tier)) continue;
                bool used = false;
                foreach (var g in l.guns) if (g != null && (int)g.def.ammo == d.value) used = true;
                if (used) PickUp(d, false);
            }
        }

        void Say(string text)
        {
            var hud = RoyaleHud.Instance;
            if (hud != null) hud.Feed(text);
        }

        // ------------------------------------------------------------------ view models

        void BuildViews()
        {
            if (fpc.cameraPivot == null) return;
            viewRoot = new GameObject("View Models").transform;
            viewRoot.SetParent(fpc.cameraPivot, false);
            viewRoot.localPosition = HipPos;
            var mats = palette;
            for (int i = 0; i < RoyaleItems.Guns.Length; i++)
            {
                var model = LootVisuals.Get(ItemKind.Gun, i);
                var go = new GameObject(RoyaleItems.Guns[i].name + " View");
                go.transform.SetParent(viewRoot, false);
                // Loot guns lie on their side along +X; stand them up and point them forward.
                go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(-90f, 0f, 0f);
                go.transform.localScale = Vector3.one * 0.9f;
                go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                views[i] = go;
            }
            // Spent cases: little brass bits flicked out to the right.
            var sh = new GameObject("Shell Cases");
            sh.transform.SetParent(viewRoot, false);
            sh.transform.localPosition = new Vector3(0.03f, 0.04f, 0.12f);
            sh.transform.localRotation = Quaternion.Euler(-35f, 70f, 0f);
            shells = sh.AddComponent<ParticleSystem>();
            shells.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = shells.main;
            sm.loop = false;
            sm.playOnAwake = false;
            sm.startLifetime = 0.8f;
            sm.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 2.6f);
            sm.startSize3D = true;
            sm.startSizeX = 0.012f;
            sm.startSizeY = 0.012f;
            sm.startSizeZ = 0.035f;
            sm.startRotation3D = true;
            sm.gravityModifier = 1.2f;
            sm.maxParticles = 12;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var sem = shells.emission;
            sem.rateOverTime = 0f;
            var sshape = shells.shape;
            sshape.shapeType = ParticleSystemShapeType.Cone;
            sshape.angle = 12f;
            sshape.radius = 0.005f;
            var srot = shells.rotationOverLifetime;
            srot.enabled = true;
            srot.separateAxes = true;
            srot.x = new ParticleSystem.MinMaxCurve(8f, 20f);
            var sr = sh.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Mesh;
            sr.mesh = LootVisuals.Cube();
            if (palette != null && palette.Length > LootVisuals.Brass) sr.sharedMaterial = palette[LootVisuals.Brass];
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var f = new GameObject("Muzzle Flash");
            f.transform.SetParent(viewRoot, false);
            f.transform.localPosition = new Vector3(0f, 0.05f, 0.6f);
            flash = f.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 10f;
            flash.intensity = 5f;
            flash.color = new Color(1f, 0.8f, 0.45f);
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        void ShowView(int id)
        {
            if (Scoped) id = -1;   // the scope overlay replaces the gun
            if (id == shownGun) return;
            shownGun = id;
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null) views[i].SetActive(i == id);
        }

        void AnimateView()
        {
            if (viewRoot == null) return;
            kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 9f);
            float speed = fpc.HorizontalSpeed;
            bob += Time.deltaTime * speed * 1.9f;
            float b = Mathf.Clamp01(speed / 5f) * (1f - aimBlend * 0.8f);
            Vector3 pos = Vector3.Lerp(HipPos, AimPos, aimBlend);
            pos += new Vector3(Mathf.Cos(bob * 0.5f) * 0.012f, -Mathf.Abs(Mathf.Sin(bob * 0.5f)) * 0.016f, 0f) * b;
            pos += new Vector3(0f, 0f, -0.07f) * kick;
            if (Reloading) pos += new Vector3(0f, -0.14f, 0f) * Mathf.Sin(Mathf.PI * ReloadProgress);
            if (switchT > 0f) pos += new Vector3(0f, -0.3f * switchT / 0.35f, 0f);
            viewRoot.localPosition = pos;
            viewRoot.localRotation = Quaternion.Euler(-kick * 7f, 0f, Reloading ? 25f * Mathf.Sin(Mathf.PI * ReloadProgress) : 0f);
        }
    }
}
