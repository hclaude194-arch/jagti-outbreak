using Jagti.Player;
using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>
    /// The player's weapons: 1 cricket bat, 2 pistol, 3 shotgun (once found). Left click attacks,
    /// R reloads, the mouse wheel switches. Guns are hitscan with spread; headshots do 2.5x.
    /// Every attack makes noise that zombies hear. View models are built from primitives.
    /// </summary>
    [RequireComponent(typeof(Inventory))]
    public class WeaponController : MonoBehaviour
    {
        [System.Serializable]
        public class Weapon
        {
            public string name;
            public bool melee;
            public float damage;
            public float range;
            public float interval;      // seconds between attacks
            public int magazine;
            public ItemType ammo;
            public int pellets = 1;
            public float spread;        // degrees
            public float noise;         // metres
            public float reloadTime;
            public float recoil;        // degrees
            public float staminaCost;
            public string sound;
            [System.NonSerialized] public int loaded;
            [System.NonSerialized] public GameObject view;
            [System.NonSerialized] public Light flash;
        }

        public Transform cameraPivot;
        public Material woodMaterial;
        public Material metalMaterial;
        public Material gripMaterial;

        public Weapon[] weapons =
        {
            new Weapon { name = "Cricket bat", melee = true, damage = 40f, range = 2.4f, interval = 0.7f, noise = 6f, recoil = 0f, staminaCost = 12f, sound = "swing" },
            new Weapon { name = "Pistol", damage = 34f, range = 120f, interval = 0.22f, magazine = 12, ammo = ItemType.PistolAmmo, spread = 0.7f, noise = 65f, reloadTime = 1.5f, recoil = 2.2f, sound = "pistol" },
            new Weapon { name = "Shotgun", damage = 14f, range = 45f, interval = 0.95f, magazine = 2, ammo = ItemType.Shells, pellets = 9, spread = 4.5f, noise = 95f, reloadTime = 2.4f, recoil = 6f, sound = "shotgun" },
        };

        public int CurrentIndex { get { return current; } }
        public Weapon Current { get { return weapons[current]; } }
        public bool Reloading { get { return reloadTimer > 0f; } }
        public float HitMarker { get; private set; }
        public bool LastHitWasHead { get; private set; }

        Inventory inventory;
        PlayerVitals vitals;
        FirstPersonController fpc;
        int current = 1;
        float nextAttack;
        float reloadTimer;
        float swingT = -1f;
        float kick;
        float switchT;
        float bobPhase;

        void Awake()
        {
            inventory = GetComponent<Inventory>();
            vitals = GetComponent<PlayerVitals>();
            fpc = GetComponent<FirstPersonController>();
            if (cameraPivot == null && fpc != null) cameraPivot = fpc.cameraPivot;
        }

        void Start()
        {
            weapons[1].loaded = weapons[1].magazine;
            BuildViews();
            Select(1);
        }

        bool Owned(int i)
        {
            if (i == 2) return inventory.hasShotgun;
            return i >= 0 && i < weapons.Length;
        }

        void Select(int i)
        {
            if (!Owned(i)) return;
            current = i;
            reloadTimer = 0f;
            switchT = 0.25f;
            for (int k = 0; k < weapons.Length; k++)
                if (weapons[k].view != null) weapons[k].view.SetActive(k == current);
        }

        void Update()
        {
            HitMarker = Mathf.MoveTowards(HitMarker, 0f, Time.deltaTime * 4f);
            if (GameState.Paused || GameState.PlayerDead)
            {
                if (GameState.PlayerDead && Current.view != null) Current.view.SetActive(false);
                return;
            }
            bool locked = Cursor.lockState == CursorLockMode.Locked;

            if (Input.GetKeyDown(KeyCode.Alpha1)) Select(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Select(1);
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                if (inventory.hasShotgun) Select(2);
                else inventory.Say("No shotgun yet");
            }
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel != 0f) Cycle(wheel > 0f ? -1 : 1);

            if (reloadTimer > 0f)
            {
                reloadTimer -= Time.deltaTime;
                if (reloadTimer <= 0f) FinishReload();
            }

            var w = Current;
            if (!w.melee && Input.GetKeyDown(KeyCode.R)) StartReload();

            if (locked && switchT <= 0f && Input.GetMouseButtonDown(0) && Time.time >= nextAttack && reloadTimer <= 0f)
            {
                if (w.melee) Swing(w);
                else if (w.loaded > 0) Fire(w);
                else
                {
                    SynthAudio.Play2D("empty", 0.6f);
                    nextAttack = Time.time + 0.25f;
                    StartReload();
                }
            }

            switchT -= Time.deltaTime;
            AnimateView();
        }

        void Cycle(int dir)
        {
            int i = current;
            for (int n = 0; n < weapons.Length; n++)
            {
                i = (i + dir + weapons.Length) % weapons.Length;
                if (Owned(i)) break;
            }
            if (i != current) Select(i);
        }

        // ------------------------------------------------------------------ attacks

        void Swing(Weapon w)
        {
            if (vitals != null && !vitals.UseStamina(w.staminaCost))
            {
                inventory.Say("Too tired to swing");
                nextAttack = Time.time + 0.3f;
                return;
            }
            nextAttack = Time.time + w.interval;
            swingT = 0f;
            SynthAudio.Play2D(w.sound, 0.6f);
            NoiseEvents.Emit(transform.position, w.noise);
            Invoke("SwingHit", 0.16f); // the bat connects partway through the swing
        }

        void SwingHit()
        {
            var w = weapons[0];
            Vector3 origin = cameraPivot.position;
            Vector3 fwd = cameraPivot.forward;
            var hits = Physics.OverlapSphere(origin + fwd * (w.range * 0.55f), w.range * 0.55f, ~0, QueryTriggerInteraction.Ignore);
            Health best = null;
            float bestDot = 0.35f;
            Vector3 bestPoint = origin;
            foreach (var c in hits)
            {
                var h = c.GetComponentInParent<Health>();
                if (h == null || h.IsDead || h.transform == transform) continue;
                Vector3 to = c.bounds.center - origin;
                float dot = Vector3.Dot(fwd, to.normalized);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = h;
                    bestPoint = c.bounds.center;
                }
            }
            if (best != null)
            {
                best.Damage(w.damage, transform.position);
                HitMarker = 1f;
                LastHitWasHead = false;
                SynthAudio.Play2D("hit_flesh", 0.9f);
                fpc.Shake(0.25f);
            }
            else
            {
                RaycastHit hit;
                if (Physics.Raycast(origin, fwd, out hit, w.range, ~0, QueryTriggerInteraction.Ignore))
                    SynthAudio.Play2D("hit_solid", 0.6f);
            }
        }

        void Fire(Weapon w)
        {
            nextAttack = Time.time + w.interval;
            w.loaded--;
            kick = 1f;
            SynthAudio.Play2D(w.sound, w.pellets > 1 ? 1f : 0.8f);
            NoiseEvents.Emit(transform.position, w.noise);
            if (fpc != null) fpc.AddRecoil(w.recoil);
            if (w.flash != null)
            {
                w.flash.enabled = true;
                CancelInvoke("HideFlash");
                Invoke("HideFlash", 0.05f);
            }

            // Pellets add up per target so a zombie dies from one blast, not one pellet.
            var totals = new System.Collections.Generic.Dictionary<Health, float>();
            bool head = false;
            Vector3 origin = cameraPivot.position;
            bool hitSolid = false;
            for (int p = 0; p < w.pellets; p++)
            {
                Vector3 dir = Quaternion.Euler(Random.Range(-w.spread, w.spread) * 0.5f, Random.Range(-w.spread, w.spread) * 0.5f, 0f) * Vector3.forward;
                dir = cameraPivot.rotation * dir;
                RaycastHit hit;
                if (!Physics.Raycast(origin, dir, out hit, w.range, ~0, QueryTriggerInteraction.Ignore)) continue;
                var box = hit.collider.GetComponent<Hitbox>();
                Health h = box != null && box.owner != null ? box.owner : hit.collider.GetComponentInParent<Health>();
                if (h == null || h.IsDead) { hitSolid = true; continue; }
                float dmg = w.damage * (box != null ? box.multiplier : 1f);
                // Shotgun pellets fall off with distance.
                if (w.pellets > 1) dmg *= Mathf.Lerp(1f, 0.35f, hit.distance / w.range);
                if (box != null && box.isHead) head = true;
                float sum;
                totals.TryGetValue(h, out sum);
                totals[h] = sum + dmg;
            }
            foreach (var kv in totals) kv.Key.Damage(kv.Value, transform.position);
            if (totals.Count > 0)
            {
                HitMarker = 1f;
                LastHitWasHead = head;
                SynthAudio.Play2D("hit_flesh", 0.5f);
            }
            else if (hitSolid) SynthAudio.Play2D("hit_solid", 0.25f);

            if (w.loaded == 0 && inventory.Count(w.ammo) > 0) Invoke("StartReload", 0.35f);
        }

        void HideFlash()
        {
            foreach (var w in weapons) if (w.flash != null) w.flash.enabled = false;
        }

        void StartReload()
        {
            var w = Current;
            if (w.melee || reloadTimer > 0f || w.loaded >= w.magazine) return;
            if (inventory.Count(w.ammo) <= 0)
            {
                inventory.Say("No " + (w.ammo == ItemType.Shells ? "shells" : "pistol rounds") + " left");
                return;
            }
            reloadTimer = w.reloadTime;
            SynthAudio.Play2D("reload", 0.7f);
        }

        void FinishReload()
        {
            var w = Current;
            w.loaded += inventory.TakeUpTo(w.ammo, w.magazine - w.loaded);
        }

        /// <summary>Gives the shotgun loaded when it is first found.</summary>
        public void OnShotgunFound()
        {
            weapons[2].loaded = weapons[2].magazine;
            Select(2);
        }

        // ------------------------------------------------------------------ view models

        void BuildViews()
        {
            if (cameraPivot == null) return;
            // Bat: a long flat blade with a round handle.
            var bat = ViewRoot("Bat View", new Vector3(0.32f, -0.32f, 0.55f));
            var batPivot = new GameObject("Swing").transform;
            batPivot.SetParent(bat.transform, false);
            Part(batPivot, PrimitiveType.Cube, new Vector3(0f, 0.38f, 0f), new Vector3(0.1f, 0.5f, 0.035f), Vector3.zero, woodMaterial);
            Part(batPivot, PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.035f, 0.13f, 0.035f), Vector3.zero, gripMaterial);
            batPivot.localRotation = Quaternion.Euler(25f, -10f, -20f);
            weapons[0].view = bat;

            // Pistol: slide, frame and grip.
            var pistol = ViewRoot("Pistol View", new Vector3(0.2f, -0.2f, 0.42f));
            Part(pistol.transform, PrimitiveType.Cube, new Vector3(0f, 0.03f, 0.05f), new Vector3(0.035f, 0.04f, 0.2f), Vector3.zero, metalMaterial);
            Part(pistol.transform, PrimitiveType.Cube, new Vector3(0f, -0.04f, -0.02f), new Vector3(0.032f, 0.11f, 0.05f), new Vector3(-15f, 0f, 0f), gripMaterial);
            weapons[1].view = pistol;
            weapons[1].flash = Flash(pistol.transform, new Vector3(0f, 0.03f, 0.18f));

            // Shotgun: two barrels over a wooden stock.
            var shotgun = ViewRoot("Shotgun View", new Vector3(0.22f, -0.24f, 0.45f));
            Part(shotgun.transform, PrimitiveType.Cylinder, new Vector3(-0.016f, 0.02f, 0.22f), new Vector3(0.03f, 0.3f, 0.03f), new Vector3(90f, 0f, 0f), metalMaterial);
            Part(shotgun.transform, PrimitiveType.Cylinder, new Vector3(0.016f, 0.02f, 0.22f), new Vector3(0.03f, 0.3f, 0.03f), new Vector3(90f, 0f, 0f), metalMaterial);
            Part(shotgun.transform, PrimitiveType.Cube, new Vector3(0f, -0.01f, -0.12f), new Vector3(0.05f, 0.07f, 0.32f), new Vector3(8f, 0f, 0f), woodMaterial);
            weapons[2].view = shotgun;
            weapons[2].flash = Flash(shotgun.transform, new Vector3(0f, 0.02f, 0.55f));

            foreach (var w in weapons) if (w.view != null) w.view.SetActive(false);
        }

        GameObject ViewRoot(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(cameraPivot, false);
            go.transform.localPosition = pos;
            return go;
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(euler);
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Light Flash(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Muzzle Flash");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 12f;
            l.intensity = 6f;
            l.color = new Color(1f, 0.8f, 0.45f);
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        void AnimateView()
        {
            var w = Current;
            if (w.view == null) return;
            kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 8f);
            float speed = fpc != null ? fpc.HorizontalSpeed : 0f;
            bobPhase += Time.deltaTime * speed * 1.9f;
            float bob = Mathf.Clamp01(speed / 5f);
            Vector3 basePos = current == 0 ? new Vector3(0.32f, -0.32f, 0.55f) : current == 1 ? new Vector3(0.2f, -0.2f, 0.42f) : new Vector3(0.22f, -0.24f, 0.45f);
            Vector3 offset = new Vector3(Mathf.Cos(bobPhase * 0.5f) * 0.012f, -Mathf.Abs(Mathf.Sin(bobPhase * 0.5f)) * 0.016f, 0f) * bob;
            offset += new Vector3(0f, 0f, -0.06f) * kick;
            if (reloadTimer > 0f) offset += new Vector3(0f, -0.12f, 0f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(1f - reloadTimer / w.reloadTime));
            if (switchT > 0f) offset += new Vector3(0f, -0.3f * switchT / 0.25f, 0f);
            w.view.transform.localPosition = basePos + offset;
            w.view.transform.localRotation = Quaternion.Euler(-kick * 8f, 0f, 0f);

            if (current == 0 && w.view.transform.childCount > 0)
            {
                var pivot = w.view.transform.GetChild(0);
                Quaternion rest = Quaternion.Euler(25f, -10f, -20f);
                if (swingT >= 0f)
                {
                    swingT += Time.deltaTime / 0.45f;
                    // Wind up to the right, sweep across to the left, recover.
                    float t = swingT;
                    Quaternion wind = Quaternion.Euler(10f, 40f, -60f);
                    Quaternion follow = Quaternion.Euler(80f, -70f, 40f);
                    pivot.localRotation = t < 0.25f ? Quaternion.Slerp(rest, wind, t / 0.25f)
                        : t < 0.55f ? Quaternion.Slerp(wind, follow, (t - 0.25f) / 0.3f)
                        : Quaternion.Slerp(follow, rest, (t - 0.55f) / 0.45f);
                    if (swingT >= 1f) swingT = -1f;
                }
                else pivot.localRotation = rest;
            }
        }
    }
}
