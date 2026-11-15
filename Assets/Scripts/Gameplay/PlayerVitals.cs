using Jagti.Player;
using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>
    /// The player's survival state: health, stamina, footstep noise, healing (Q),
    /// torch battery, damage feedback and death.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(Inventory))]
    [RequireComponent(typeof(FirstPersonController))]
    public class PlayerVitals : MonoBehaviour
    {
        [Header("Stamina")]
        public float maxStamina = 100f;
        public float sprintDrain = 16f;
        public float regenRate = 14f;
        public float regenDelay = 1.0f;
        public float restartSprintAt = 25f;

        [Header("Noise radius in metres")]
        public float walkNoise = 7f;
        public float sprintNoise = 18f;

        [Header("Healing")]
        public float bandageHeal = 25f;
        public float medkitHeal = 70f;
        public float healDuration = 3f;

        [Header("Torch")]
        public float batterySeconds = 300f;

        public float Stamina { get; private set; }
        public float StaminaFraction { get { return Stamina / maxStamina; } }
        public float Battery { get; private set; }   // charge of the battery in the torch, 0..1
        public float DamageFlash { get; private set; }
        public float HealingLeft { get { return healLeft; } }

        Health health;
        Inventory inventory;
        FirstPersonController fpc;
        float regenWait;
        bool exhausted;
        float stepDistance;
        bool stepAlt;
        Vector3 lastPos;
        float healLeft;
        float healRate;
        float baseFlashIntensity = 3f;

        void Awake()
        {
            health = GetComponent<Health>();
            inventory = GetComponent<Inventory>();
            fpc = GetComponent<FirstPersonController>();
            Stamina = maxStamina;
            Battery = 1f;
            if (fpc.flashlight != null) baseFlashIntensity = fpc.flashlight.intensity;
        }

        void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        void Start()
        {
            lastPos = transform.position;
        }

        public bool UseStamina(float amount)
        {
            if (Stamina < amount * 0.5f) return false;
            Stamina = Mathf.Max(0f, Stamina - amount);
            regenWait = regenDelay;
            return true;
        }

        void Update()
        {
            DamageFlash = Mathf.MoveTowards(DamageFlash, 0f, Time.deltaTime * 1.6f);
            if (GameState.PlayerDead || GameState.Paused) return;
            GameState.TimeSurvived += Time.deltaTime;

            // Stamina.
            if (fpc.IsSprinting && fpc.HorizontalSpeed > 1f)
            {
                Stamina = Mathf.Max(0f, Stamina - sprintDrain * Time.deltaTime);
                regenWait = regenDelay;
                if (Stamina <= 0f) exhausted = true;
            }
            else if (regenWait > 0f) regenWait -= Time.deltaTime;
            else Stamina = Mathf.Min(maxStamina, Stamina + regenRate * Time.deltaTime);
            if (exhausted && Stamina >= restartSprintAt) exhausted = false;
            fpc.sprintAllowed = !exhausted;

            // Footsteps make noise; sprinting carries much further.
            Vector3 delta = transform.position - lastPos;
            lastPos = transform.position;
            delta.y = 0f;
            if (fpc.IsGrounded && delta.magnitude < 2f) stepDistance += delta.magnitude;
            float stride = fpc.IsSprinting ? 2.6f : 2.0f;
            if (stepDistance >= stride)
            {
                stepDistance = 0f;
                stepAlt = !stepAlt;
                SynthAudio.Play2D(stepAlt ? "footstep" : "footstep2", fpc.IsSprinting ? 0.35f : 0.2f);
                NoiseEvents.Emit(transform.position, fpc.IsSprinting ? sprintNoise : walkNoise);
            }

            // Healing over time.
            if (healLeft > 0f)
            {
                float h = Mathf.Min(healLeft, healRate * Time.deltaTime);
                health.Heal(h);
                healLeft -= h;
            }
            if (Input.GetKeyDown(KeyCode.Q)) UseHealingItem();

            UpdateTorch();
        }

        void UseHealingItem()
        {
            if (health.current >= health.max - 1f || healLeft > 0f) return;
            float missing = health.max - health.current;
            // Prefer a bandage for small wounds and a medkit for big ones, but use whatever there is.
            bool useMedkit = inventory.medkits > 0 && (missing > 40f || inventory.bandages == 0);
            if (useMedkit && inventory.Take(ItemType.Medkit, 1)) StartHeal(medkitHeal, "Used a medkit");
            else if (inventory.Take(ItemType.Bandage, 1)) StartHeal(bandageHeal, "Used a bandage");
            else inventory.Say("No bandages or medkits");
        }

        void StartHeal(float amount, string text)
        {
            healLeft = amount;
            healRate = amount / healDuration;
            inventory.Say(text);
            SynthAudio.Play2D("reload", 0.4f);
        }

        void UpdateTorch()
        {
            var light = fpc.flashlight;
            if (light == null) return;
            if (light.enabled)
            {
                Battery -= Time.deltaTime / batterySeconds;
                if (Battery <= 0f)
                {
                    if (inventory.Take(ItemType.Battery, 1))
                    {
                        Battery = 1f;
                        inventory.Say("Changed the torch battery");
                    }
                    else
                    {
                        Battery = 0f;
                        light.enabled = false;
                        inventory.Say("Torch battery is dead");
                    }
                }
            }
            // A weak battery dims and flickers.
            float charge = Mathf.Clamp01(Battery / 0.2f);
            float flicker = Battery < 0.1f && Random.value < 0.08f ? 0.2f : 1f;
            light.intensity = baseFlashIntensity * Mathf.Lerp(0.35f, 1f, charge) * flicker;
        }

        void OnDamaged(float amount, Vector3 from)
        {
            DamageFlash = Mathf.Clamp01(DamageFlash + amount / 30f);
            fpc.Shake(Mathf.Clamp(amount / 15f, 0.3f, 1f));
            SynthAudio.Play2D("hurt", 0.7f);
            healLeft = 0f; // being hit interrupts healing
        }

        void OnDied()
        {
            GameState.PlayerDead = true;
            fpc.Collapse();
            GameState.SetCursorLocked(false);
            NoiseEvents.Emit(transform.position, 25f);
        }
    }
}
