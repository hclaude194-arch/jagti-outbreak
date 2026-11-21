using System;
using System.Collections.Generic;
using Jagti.World;
using UnityEngine;

namespace Jagti.Audio
{
    /// <summary>
    /// The campus around the player. Beds (long stereo loops) blend by the time of day (DayCycle's sun
    /// and lamps: day, the evening roost chorus, crickets at night), by shelter (room tone and a fan
    /// in the hostels indoors, the outside dulled through the walls), by height (wind on the roofs) and
    /// by the trees nearby (pine hiss on the hill). Single calls of the local birds are placed round the
    /// listener (bulbuls, mynas, crows, parakeets, sparrows, doves, the koel), and at night owls, far
    /// dogs, a jackal pack and the highway. Point sources from ambient_emitters.json (water channels,
    /// flags, the substation, the mess kitchen, shop fridges, lamps at night, crickets in the lawns)
    /// play as positional loops, the nearest dozen at a time.
    /// </summary>
    public class AmbienceDirector : MonoBehaviour
    {
        [Serializable]
        class Emitter
        {
            public string sound;
            public float x, z, y;
            public string when;
        }

        [Serializable]
        class EmitterTable
        {
            public Emitter[] emitters;
        }

        class Slot
        {
            public SoundLoop loop;
            public string sound;
            public int emitter = -1;
        }

        public int maxEmitters = 12;

        SoundLoop day, evening, night, windHigh, pines, indoor, fan;
        Emitter[] emitters;
        readonly List<Slot> slots = new List<Slot>();
        readonly HashSet<int> wanted = new HashSet<int>();
        readonly List<int> order = new List<int>();
        float indoorT, emitterTimer, spotTimer = 4f;
        float[,] trees;
        Vector2 treeOrigin;
        const float TreeCell = 20f;
        Terrain terrain;

        void Start()
        {
            day = Bed("bed_day");
            evening = Bed("bed_evening");
            night = Bed("bed_night");
            windHigh = Bed("bed_wind_high");
            pines = Bed("bed_pines");
            indoor = Bed("bed_indoor");
            fan = Bed("fan", false);
            var text = Resources.Load<TextAsset>("Audio/ambient_emitters");
            if (text != null)
            {
                var t = JsonUtility.FromJson<EmitterTable>(text.text);
                emitters = t != null ? t.emitters : null;
            }
            terrain = Terrain.activeTerrain;
            CountTrees();
        }

        SoundLoop Bed(string name, bool flat = true)
        {
            var l = SoundLoop.Create(gameObject, name, !flat);
            if (l != null) l.VolumeEase = 0.5f;
            return l;
        }

        static void Set(SoundLoop l, float v)
        {
            if (l != null) l.Volume = v;
        }

        float Ground(Vector3 p)
        {
            if (terrain == null) terrain = Terrain.activeTerrain;
            return terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 ear = SoundSystem.ListenerPosition;
            float night01 = DayCycle.NightAmount;
            float evening01 = (1f - night01) * Mathf.Clamp01((30f - DayCycle.SunElevation) / 22f);
            float day01 = Mathf.Max(0f, 1f - night01 - evening01);
            indoorT = Mathf.MoveTowards(indoorT, SoundSystem.ListenerIndoors ? 1f : 0f, dt / 1.2f);
            float height = ear.y - Ground(ear);
            float sky = Mathf.Clamp01((height - 40f) / 80f);        // up with the bus and the gliders, the beds give way
            float outdoor = (1f - 0.8f * indoorT) * (1f - sky);
            float cut = Mathf.Lerp(22000f, 650f, indoorT);

            Set(day, day01 * outdoor);
            Set(evening, evening01 * outdoor);
            Set(night, night01 * outdoor);
            foreach (var l in new[] { day, evening, night, pines, windHigh })
                if (l != null) l.Cutoff = cut;
            Set(windHigh, Mathf.Clamp01((height - 6f) / 14f) * outdoor + sky * 0.2f);
            Set(pines, TreesNear(ear) * outdoor * (1f - night01 * 0.4f));
            Set(indoor, indoorT * (1f - sky));
            var zone = indoorT > 0.5f ? AcousticSpace.ZoneAt(ear) : null;
            bool hostel = zone != null && zone.name.IndexOf("Hostel", StringComparison.OrdinalIgnoreCase) >= 0;
            Set(fan, hostel ? indoorT * 0.8f : 0f);
            if (fan != null) fan.Source.transform.position = ear + Vector3.up * 1.4f;

            spotTimer -= dt;
            if (spotTimer <= 0f)
            {
                spotTimer = Spot(day01, evening01, night01, outdoor) * (indoorT > 0.5f ? 2.5f : 1f);
            }

            emitterTimer -= dt;
            if (emitterTimer <= 0f)
            {
                emitterTimer = 0.5f;
                UpdateEmitters(ear, night01, evening01);
            }
        }

        // ------------------------------------------------------------------ calls round the listener

        struct Call
        {
            public string name;
            public float weight, near, far, height;
            public Call(string name, float weight, float near, float far, float height)
            {
                this.name = name; this.weight = weight; this.near = near; this.far = far; this.height = height;
            }
        }

        static readonly Call[] DayCalls =
        {
            new Call("bird_bulbul", 3f, 15f, 60f, 6f), new Call("bird_myna", 3f, 15f, 60f, 4f), new Call("bird_sparrow", 3f, 10f, 40f, 4f),
            new Call("bird_crow", 2f, 20f, 90f, 10f), new Call("bird_dove", 2f, 15f, 60f, 6f), new Call("bird_parakeet", 1.5f, 30f, 120f, 15f),
            new Call("bird_koel", 0.8f, 40f, 140f, 10f), new Call("truck_horn_far", 0.4f, 500f, 900f, 0f),
        };
        static readonly Call[] EveningCalls =
        {
            new Call("bird_crow", 3f, 20f, 100f, 10f), new Call("bird_parakeet", 3f, 30f, 120f, 15f), new Call("bird_myna", 3f, 15f, 70f, 5f),
            new Call("bird_koel", 1.5f, 40f, 140f, 10f), new Call("bird_bulbul", 1.5f, 15f, 60f, 6f), new Call("dog_far", 0.6f, 150f, 400f, 0f),
            new Call("truck_horn_far", 0.5f, 500f, 900f, 0f),
        };
        static readonly Call[] NightCalls =
        {
            new Call("owl", 1.5f, 25f, 90f, 10f), new Call("dog_far", 2f, 150f, 450f, 0f), new Call("jackals", 0.6f, 300f, 700f, 0f),
            new Call("truck_horn_far", 0.8f, 500f, 900f, 0f), new Call("train_far", 0.25f, 900f, 1500f, 0f),
        };

        /// <summary>Plays one call somewhere round the listener; returns the seconds until the next.</summary>
        float Spot(float day01, float evening01, float night01, float outdoor)
        {
            if (outdoor < 0.05f) return 3f;
            float roll = UnityEngine.Random.value;
            Call[] table = roll < night01 ? NightCalls : roll < night01 + evening01 ? EveningCalls : DayCalls;
            float total = 0f;
            foreach (var c in table) total += c.weight;
            float pick = UnityEngine.Random.value * total;
            Call call = table[0];
            foreach (var c in table)
            {
                pick -= c.weight;
                if (pick <= 0f) { call = c; break; }
            }
            float a = UnityEngine.Random.value * Mathf.PI * 2f;
            float d = UnityEngine.Random.Range(call.near, call.far);
            Vector3 at = SoundSystem.ListenerPosition + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
            at.y = Ground(at) + call.height + UnityEngine.Random.Range(0f, 4f);
            SoundSystem.Play(call.name, at, UnityEngine.Random.Range(0.6f, 1f));
            if (UnityEngine.Random.value < 0.07f * outdoor) SoundSystem.Play2D("gust", UnityEngine.Random.Range(0.4f, 0.9f));
            // Busy in the evening chorus, sparse at night.
            return UnityEngine.Random.Range(1.5f, 5f) * (1f + night01 * 2.5f) * (1f - evening01 * 0.45f);
        }

        // ------------------------------------------------------------------ trees

        void CountTrees()
        {
            if (terrain == null || terrain.terrainData == null) return;
            var td = terrain.terrainData;
            Vector3 size = td.size;
            int nx = Mathf.Max(1, Mathf.CeilToInt(size.x / TreeCell)), nz = Mathf.Max(1, Mathf.CeilToInt(size.z / TreeCell));
            trees = new float[nx, nz];
            treeOrigin = new Vector2(terrain.transform.position.x, terrain.transform.position.z);
            foreach (var t in td.treeInstances)
            {
                int x = Mathf.Clamp((int)(t.position.x * size.x / TreeCell), 0, nx - 1);
                int z = Mathf.Clamp((int)(t.position.z * size.z / TreeCell), 0, nz - 1);
                trees[x, z] += 1f;
            }
        }

        /// <summary>0 open ground .. 1 among many trees.</summary>
        float TreesNear(Vector3 p)
        {
            if (trees == null) return 0f;
            int cx = (int)((p.x - treeOrigin.x) / TreeCell), cz = (int)((p.z - treeOrigin.y) / TreeCell);
            float n = 0f;
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                    if (x >= 0 && z >= 0 && x < trees.GetLength(0) && z < trees.GetLength(1)) n += trees[x, z];
            return Mathf.Clamp01((n - 4f) / 30f);
        }

        // ------------------------------------------------------------------ point sources

        void UpdateEmitters(Vector3 ear, float night01, float evening01)
        {
            if (emitters == null) return;
            wanted.Clear();
            order.Clear();
            for (int i = 0; i < emitters.Length; i++)
            {
                var e = emitters[i];
                if (e.when == "night" && night01 < 0.5f) continue;
                if (e.when == "dusk" && night01 + evening01 < 0.45f) continue;
                var def = SoundBank.Find(e.sound);
                if (def == null) continue;
                float dx = e.x - ear.x, dz = e.z - ear.z;
                if (dx * dx + dz * dz < def.maxDistance * def.maxDistance) order.Add(i);
            }
            order.Sort((a, b) => Dist2(a, ear).CompareTo(Dist2(b, ear)));
            for (int k = 0; k < order.Count && k < maxEmitters; k++) wanted.Add(order[k]);

            foreach (var s in slots)
            {
                if (s.emitter >= 0 && !wanted.Contains(s.emitter))
                {
                    s.loop.Volume = 0f;
                    if (!s.loop.Source.isPlaying) s.emitter = -1;
                }
            }
            foreach (int i in wanted)
            {
                Slot slot = null;
                foreach (var s in slots) if (s.emitter == i) { slot = s; break; }
                if (slot == null)
                {
                    foreach (var s in slots) if (s.emitter < 0 && s.sound == emitters[i].sound) { slot = s; break; }
                    if (slot == null)
                    {
                        if (slots.Count >= maxEmitters + 6) continue;
                        var host = new GameObject("Ambient " + emitters[i].sound);
                        host.transform.SetParent(transform, false);
                        var loop = SoundLoop.Create(host, emitters[i].sound);
                        if (loop == null) { Destroy(host); continue; }
                        loop.VolumeEase = 0.7f;
                        slot = new Slot { loop = loop, sound = emitters[i].sound };
                        slots.Add(slot);
                    }
                    slot.emitter = i;
                    var e = emitters[i];
                    var pos = new Vector3(e.x, 0f, e.z);
                    pos.y = Ground(pos) + e.y;
                    slot.loop.Source.transform.parent.position = pos;
                }
                slot.loop.Volume = 1f;
            }
        }

        float Dist2(int i, Vector3 ear)
        {
            float dx = emitters[i].x - ear.x, dz = emitters[i].z - ear.z;
            return dx * dx + dz * dz;
        }
    }
}
