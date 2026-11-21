using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// A crate of top-tier gear that floats down under a parachute into the safe circle, trailing
    /// red smoke so everyone can see where it lands. On landing it sets out three epic items.
    /// </summary>
    public class SupplyDrop : MonoBehaviour
    {
        public Material crateMaterial;
        public Material chuteMaterial;
        public Material smokeMaterial;
        public float fallSpeed = 7f;
        public Vector3 landing;
        /// <summary>Picks the contents; the same on every machine online.</summary>
        public int seed;
        /// <summary>Which drop of the match this is (gives its items the same ids everywhere).</summary>
        public int index;

        public bool Landed { get; private set; }

        Transform chute;
        ParticleSystem smoke;

        public static SupplyDrop Create(Vector3 landing, float height, Material crate, Material chute, Material smoke, int seed, int index)
        {
            var go = new GameObject("Supply Drop");
            go.transform.position = landing + Vector3.up * height;
            var d = go.AddComponent<SupplyDrop>();
            d.landing = landing;
            d.seed = seed;
            d.index = index;
            d.crateMaterial = crate;
            d.chuteMaterial = chute;
            d.smokeMaterial = smoke;
            d.Build();
            return d;
        }

        void Build()
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Crate";
            box.transform.SetParent(transform, false);
            box.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            box.transform.localScale = new Vector3(1.3f, 1f, 1.3f);
            box.GetComponent<Renderer>().sharedMaterial = crateMaterial;
            foreach (float s in new[] { -0.5f, 0.5f })
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(band.GetComponent<Collider>());
                band.transform.SetParent(transform, false);
                band.transform.localPosition = new Vector3(s, 0.5f, 0f);
                band.transform.localScale = new Vector3(0.12f, 1.02f, 1.32f);
                band.GetComponent<Renderer>().sharedMaterial = chuteMaterial;
            }

            chute = new GameObject("Parachute").transform;
            chute.SetParent(transform, false);
            chute.localPosition = new Vector3(0f, 7f, 0f);
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dome.GetComponent<Collider>());
            dome.transform.SetParent(chute, false);
            dome.transform.localScale = new Vector3(7f, 2.4f, 7f);
            dome.GetComponent<Renderer>().sharedMaterial = chuteMaterial;
            for (int i = 0; i < 4; i++)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(line.GetComponent<Collider>());
                line.transform.SetParent(chute, false);
                Vector3 corner = Quaternion.Euler(0f, 45f + i * 90f, 0f) * new Vector3(0f, 0f, 1.8f);
                line.transform.localPosition = corner * 0.5f + new Vector3(0f, -3.1f, 0f);
                line.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (corner - new Vector3(0f, -6.2f, 0f)).normalized);
                line.transform.localScale = new Vector3(0.03f, 6.4f, 0.03f);
                line.GetComponent<Renderer>().sharedMaterial = crateMaterial;
            }

            var sgo = new GameObject("Red Smoke");
            sgo.transform.SetParent(transform, false);
            sgo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            smoke = sgo.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 2.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.startColor = new Color(0.85f, 0.18f, 0.12f, 0.75f);
            main.gravityModifier = -0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var em = smoke.emission;
            em.rateOverTime = 10f;
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.3f;
            var col = smoke.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.6f, 0.55f), 1f) },
                      new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = sgo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = smokeMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smoke.Play();
        }

        void Update()
        {
            if (Landed) return;
            Vector3 p = transform.position;
            p.y = Mathf.MoveTowards(p.y, landing.y, fallSpeed * Time.deltaTime);
            p.x = landing.x + Mathf.Sin(Time.time * 0.4f) * 0.6f;
            p.z = landing.z + Mathf.Cos(Time.time * 0.33f) * 0.6f;
            transform.position = p;
            if (p.y <= landing.y + 0.01f) Land();
        }

        void Land()
        {
            Landed = true;
            transform.position = landing;
            if (chute != null) Destroy(chute.gameObject);
            if (Jagti.Audio.SoundSystem.Play("drop_land", landing, 1f) == null && Jagti.Audio.SoundSystem.Instance == null)
                Ballistics.SoundAt(landing, "land", 1f);
            Jagti.Audio.SoundSystem.PlayLater("drop_flare", 0.4f, landing + Vector3.up * 0.5f, 0.9f);
            var loot = LootDirector.Instance;
            if (loot == null) return;
            var rng = new System.Random(seed);
            loot.BeginShared(LootDirector.SupplyIdBase + index * 16);
            // An epic gun with ammo (the crate is where the bolt-action rifle and LMG turn up most),
            // a scope or suppressor, plus epic armour or a pack.
            double roll = rng.NextDouble();
            int gunId = roll < 0.3 ? 9 : roll < 0.55 ? 8 : roll < 0.8 ? 4 : 3;
            var def = RoyaleItems.Guns[gunId];
            Vector3 front = landing + new Vector3(0f, 0f, 1.2f);
            loot.Add(LootDirector.Snap(front, landing.y), 0f, ItemKind.Gun, gunId, Tier.Epic, 1);
            loot.Add(LootDirector.Snap(front + new Vector3(0.7f, 0f, 0f), landing.y), 30f, ItemKind.Ammo, (int)def.ammo, Tier.Common, RoyaleItems.AmmoBox[(int)def.ammo] * 2);
            ItemKind extra = rng.NextDouble() < 0.5 ? ItemKind.Vest : rng.NextDouble() < 0.5 ? ItemKind.Helmet : ItemKind.Backpack;
            loot.Add(LootDirector.Snap(landing + new Vector3(-1.2f, 0f, 0.2f), landing.y), 90f, extra, 3, Tier.Epic, 1);
            loot.Add(LootDirector.Snap(landing + new Vector3(0.2f, 0f, -1.2f), landing.y), 0f, ItemKind.Medkit, 0, Tier.Rare, 2);
            int part = gunId == 9 || gunId == 4 ? (int)AttachmentType.Suppressor : (rng.NextDouble() < 0.5 ? (int)AttachmentType.Scope4x : (int)AttachmentType.Suppressor);
            loot.Add(LootDirector.Snap(landing + new Vector3(1.1f, 0f, -0.6f), landing.y), 45f, ItemKind.Attachment, part, Tier.Epic, 1);
            loot.EndShared();
        }
    }
}
