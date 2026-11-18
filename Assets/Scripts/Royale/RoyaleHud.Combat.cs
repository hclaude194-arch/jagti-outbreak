using Jagti.Gameplay;
using UnityEngine;

namespace Jagti.Royale
{
    // The player card (health, armour, boost), the hotbar, the ammo panel, the crosshair and what
    // happens around it, prompts, the vehicle panel and the parachute drop.
    public partial class RoyaleHud
    {
        static readonly string[] PartShort = { "DOT", "2X", "4X", "8X", "COMP", "SUPP", "GRIP", "EXT MAG" };
        static readonly string[] AmmoShort = { "9MM", "12G", "5.56", "7.62" };

        // ------------------------------------------------------------------ player card

        /// <summary>Bottom left: big health number, name, armour and pack badges, vest bar, health bar.</summary>
        void DrawPlayerCard(float h, bool dropping)
        {
            var l = player.loadout;
            const float cw = 430f, chh = 98f;
            float x = M, y = h - M - chh;
            HudKit.Panel(x, y, cw, chh, HudKit.Ink, 8f);

            // Health number.
            float hp = player.Health.current, frac = player.Health.Fraction;
            bool low = frac < 0.3f;
            HudKit.Panel(x + 8f, y + 8f, 82f, chh - 16f, new Color(1f, 1f, 1f, 0.06f), 6f);
            HudKit.IconAt("heart", x + 49f, y + 22f, 15f, low ? HudKit.HealthLow : HudKit.TextDim);
            HudKit.Label(x + 8f, y + 28f, 82f, 46f, Mathf.CeilToInt(hp).ToString(), 44f, low ? HudKit.HealthLow : HudKit.Text, TextAnchor.MiddleCenter, true);
            if (player.BoostLeft > 0.5f)
                HudKit.Label(x + 8f, y + 70f, 82f, 18f, "+" + Mathf.CeilToInt(player.BoostLeft), 14f, HudKit.Boost, TextAnchor.MiddleCenter, true);

            float bx = x + 102f, bw = cw - 114f;
            HudKit.Label(bx, y + 6f, 180f, 26f, player.displayName.ToUpper(), 19f, HudKit.Text, TextAnchor.MiddleLeft, true);
            float gx = x + cw - 12f;
            gx = GearBadge(gx, y + 8f, "pack", l.backpack, -1f);
            gx = GearBadge(gx, y + 8f, "vest", l.vest, l.vest > 0 ? l.vestDurability / RoyaleItems.ArmourDurability[l.vest] : 0f);
            GearBadge(gx, y + 8f, "helmet", l.helmet, l.helmet > 0 ? l.helmetDurability / RoyaleItems.ArmourDurability[l.helmet] : 0f);

            // Vest (the shield bar): blue, in cells of 40.
            float vy = y + 42f;
            if (l.vest > 0)
            {
                float max = RoyaleItems.ArmourDurability[l.vest];
                HudKit.Bar(bx, vy, bw - 44f, 12f, l.vestDurability / max, HudKit.Shield, Mathf.RoundToInt(max / 40f), 2f);
                HudKit.Label(bx + bw - 40f, vy - 4f, 40f, 20f, Mathf.CeilToInt(l.vestDurability).ToString(), 15f, HudKit.Shield, TextAnchor.MiddleRight, true);
            }
            else
            {
                HudKit.Panel(bx, vy, bw - 44f, 12f, new Color(0f, 0f, 0f, 0.35f), 2f);
                HudKit.Label(bx + 8f, vy - 4f, 200f, 20f, "NO VEST", 12f, HudKit.TextDim, TextAnchor.MiddleLeft, true, false);
            }

            // Health: the bar, the chunk just lost (drains after a moment), the chai boost to come.
            float hy = y + 62f, hh = 24f;
            HudKit.Panel(bx, hy, bw, hh, new Color(0f, 0f, 0f, 0.5f), 3f);
            float tf = Mathf.Clamp01(trailHealth / player.Health.max);
            if (tf > frac) HudKit.Panel(bx + bw * frac, hy, bw * (tf - frac), hh, new Color(1f, 0.38f, 0.28f, 0.85f), 3f);
            if (frac > 0.001f) HudKit.Panel(bx, hy, Mathf.Max(6f, bw * frac), hh, low ? HudKit.HealthLow : HudKit.Health, 3f);
            if (player.BoostLeft > 0f)
            {
                float boost = Mathf.Min(1f - frac, player.BoostLeft / player.Health.max);
                float pulse = 0.45f + 0.2f * Mathf.Sin(Time.time * 4f);
                HudKit.Panel(bx + bw * frac, hy, bw * boost, hh, HudKit.WithAlpha(HudKit.Boost, pulse), 3f);
            }
            HudKit.Segments(bx, hy, bw, hh, 4);
            if (player.IsHealing)
                HudKit.Fill(bx, hy + hh + 2f, bw * player.HealProgress, 2f, new Color(0.45f, 0.95f, 0.5f));

            if (!dropping && fpc != null && fpc.Crouching)
            {
                HudKit.Panel(x, y - 34f, 104f, 26f, HudKit.Ink, 4f);
                HudKit.Label(x, y - 34f, 104f, 26f, "CROUCHED", 14f, HudKit.TextDim, TextAnchor.MiddleCenter, true);
            }
        }

        /// <summary>A small square with an armour or pack icon in its level colour; returns the next x to the left.</summary>
        float GearBadge(float right, float y, string icon, int level, float durability)
        {
            const float s = 30f;
            float x = right - s;
            HudKit.Panel(x, y, s, s, level > 0 ? HudKit.WithAlpha(HudKit.LevelColour(level), 0.22f) : new Color(1f, 1f, 1f, 0.06f), 4f);
            if (level > 0) HudKit.Outline(x, y, s, s, HudKit.WithAlpha(HudKit.LevelColour(level), 0.8f), 1f, 4f);
            HudKit.IconAt(icon, x + s / 2f, y + s / 2f - 1f, 20f, level > 0 ? HudKit.LevelColour(level) : new Color(1f, 1f, 1f, 0.25f));
            if (level > 0)
            {
                HudKit.Label(x + s - 11f, y + s - 13f, 10f, 12f, level.ToString(), 11f, HudKit.Text, TextAnchor.MiddleCenter, true);
                if (durability >= 0f) HudKit.Fill(x + 3f, y + s - 3f, (s - 6f) * Mathf.Clamp01(durability), 2f, HudKit.Text);
            }
            return x - 5f;
        }

        // ------------------------------------------------------------------ combat

        void DrawCombat(float w, float h)
        {
            var l = player.loadout;
            var gun = l.Current;
            float cx = w / 2f, cy = h / 2f;

            if (arsenal == null || !arsenal.Scoped) DrawCrosshair(cx, cy, gun);
            if (arsenal != null && arsenal.HitMarker > 0.01f)
            {
                Color hc = arsenal.HitWasKill ? new Color(1f, 0.25f, 0.2f) : arsenal.HitWasHead ? new Color(1f, 0.85f, 0.2f) : Color.white;
                HitMark(cx, cy, HudKit.WithAlpha(hc, arsenal.HitMarker), 1f + (1f - arsenal.HitMarker) * 0.3f);
            }
            if (GameSettings.DamageNumbers) DrawNumbers();
            DrawElimination(cx, cy);

            // Where shots come from: red bands round the crosshair.
            float yaw = fpc != null ? fpc.transform.eulerAngles.y : player.transform.eulerAngles.y;
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                float age = Time.time - threats[i].Value;
                if (age > 1.6f) { threats.RemoveAt(i); continue; }
                Vector3 to = threats[i].Key - player.transform.position;
                float ang = Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                HudKit.HitArc(cx, cy, 150f, ang, new Color(1f, 0.18f, 0.1f, 0.9f * (1f - age / 1.6f)));
            }

            // Ammo warnings under the crosshair.
            if (gun != null && arsenal != null && !arsenal.Reloading)
            {
                int reserve = l.CountFor(gun);
                if (gun.loaded == 0 && reserve == 0)
                    HudKit.Label(cx - 100f, cy + 34f, 200f, 22f, "NO AMMO", 17f, HudKit.Danger, TextAnchor.MiddleCenter, true);
                else if (gun.loaded <= Mathf.Max(1, gun.Magazine / 4) && reserve > 0)
                {
                    float kw = 24f, tw = HudKit.TextWidth("RELOAD", 17f, true);
                    float x = cx - (kw + 8f + tw) / 2f;
                    HudKit.KeyCap(x, cy + 34f, "R", 22f);
                    HudKit.Label(x + kw + 8f, cy + 34f, tw + 4f, 22f, "RELOAD", 17f, gun.loaded == 0 ? HudKit.Danger : HudKit.Text, TextAnchor.MiddleLeft, true);
                }
            }
            if (arsenal != null && arsenal.Reloading)
            {
                HudKit.Ring(cx, cy, 30f, 3.5f, 0f, 1f, new Color(0f, 0f, 0f, 0.35f));
                HudKit.Ring(cx, cy, 30f, 3.5f, 0f, arsenal.ReloadProgress, Color.white);
            }
            if (player.IsHealing) DrawHealing(cx, cy);

            DrawPrompt(cx, cy);
            DrawVehicle(w, h);
            DrawHotbar(w, h);
            DrawWeaponPanel(w, h);
            DrawPickups(h);
        }

        void DrawCrosshair(float cx, float cy, GunItem gun)
        {
            Color cc = HudKit.WithAlpha(GameSettings.CrosshairColour, 0.92f);
            var shade = new Color(0f, 0f, 0f, 0.5f);
            if (arsenal != null && arsenal.DotSight && gun != null)
            {
                // The red dot (and the 2x sight's ring) instead of the crosshair.
                var red = new Color(1f, 0.15f, 0.1f, 0.95f);
                HudKit.GlowAt(cx, cy, 14f, HudKit.WithAlpha(red, 0.5f));
                HudKit.Dot(cx, cy, 5f, red);
                if (gun.Sight == 1) HudKit.Ring(cx, cy, 16f, 1.6f, 0f, 1f, HudKit.WithAlpha(red, 0.8f));
                return;
            }
            float gap = 6f;
            if (gun != null)
            {
                float blend = arsenal != null ? arsenal.AimBlend : 0f;
                float spread = Mathf.Lerp(gun.def.hipSpread, gun.def.aimSpread, blend) * gun.SpreadScale;
                if (fpc != null && fpc.HorizontalSpeed > 1f) spread *= 1.5f;
                if (fpc != null && fpc.Crouching) spread *= 0.8f;
                if (arsenal != null) spread *= 1f + arsenal.Bloom * 0.4f;
                gap = 4f + spread * 6f;
            }
            if (gun != null && gun.def.pellets > 1)
            {
                // Shotguns: a ring the size of the spread.
                HudKit.Ring(cx, cy, gap + 6f, 2.6f, 0f, 1f, shade);
                HudKit.Ring(cx, cy, gap + 6f, 1.6f, 0f, 1f, cc);
                HudKit.Dot(cx, cy, 3f, cc);
                return;
            }
            const float len = 9f, t = 2f;
            HudKit.Fill(cx - 1.5f, cy - 1.5f, 3f, 3f, shade);
            HudKit.Fill(cx - gap - len - 0.5f, cy - t / 2f - 0.5f, len + 1f, t + 1f, shade);
            HudKit.Fill(cx + gap - 0.5f, cy - t / 2f - 0.5f, len + 1f, t + 1f, shade);
            HudKit.Fill(cx - t / 2f - 0.5f, cy - gap - len - 0.5f, t + 1f, len + 1f, shade);
            HudKit.Fill(cx - t / 2f - 0.5f, cy + gap - 0.5f, t + 1f, len + 1f, shade);
            HudKit.Fill(cx - 1f, cy - 1f, 2f, 2f, cc);
            HudKit.Fill(cx - gap - len, cy - t / 2f, len, t, cc);
            HudKit.Fill(cx + gap, cy - t / 2f, len, t, cc);
            HudKit.Fill(cx - t / 2f, cy - gap - len, t, len, cc);
            HudKit.Fill(cx - t / 2f, cy + gap, t, len, cc);
        }

        void HitMark(float cx, float cy, Color c, float scale)
        {
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
            {
                var m = HudKit.Rotate(a, cx, cy);
                HudKit.Fill(cx + 8f * scale, cy - 1.25f, 10f * scale, 2.5f, c);
                GUI.matrix = m;
            }
        }

        void DrawNumbers()
        {
            var cam = Camera.main;
            if (cam == null) return;
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                var n = numbers[i];
                float age = Time.time - n.time;
                if (age > 0.9f) { numbers.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(n.at + Vector3.up * age * 0.6f);
                if (sp.z <= 0f) continue;
                float x = sp.x / HudKit.S, y = (Screen.height - sp.y) / HudKit.S;
                HudKit.Alpha = Mathf.Clamp01((0.9f - age) * 3f);
                float size = n.shield ? 15f : 24f + Mathf.Max(0f, 0.15f - age) * 70f;
                HudKit.Label(x - 80f, y - 16f, 160f, 32f, n.text, size, n.colour, TextAnchor.MiddleCenter, true);
                HudKit.Alpha = 1f;
            }
        }

        void DrawElimination(float cx, float cy)
        {
            float age = Time.time - eliminatedTime;
            if (age > 2.4f || string.IsNullOrEmpty(eliminated)) return;
            HudKit.Alpha = Mathf.Clamp01((2.4f - age) * 2f);
            float pop = 1f + Mathf.Max(0f, 0.2f - age) * 2f;
            float y = cy + 120f;
            string name = eliminated.ToUpper();
            float nw = HudKit.TextWidth(name, 28f * pop, true);
            float total = 44f + 12f + nw;
            float x = cx - total / 2f;
            HudKit.Panel(x, y, 44f, 44f, HudKit.Accent, 22f);
            HudKit.IconAt("skull", x + 22f, y + 22f, 26f, new Color(0.1f, 0.07f, 0.04f));
            HudKit.Label(x + 56f, y - 6f, nw + 10f, 22f, "ELIMINATED", 14f, HudKit.Accent, TextAnchor.MiddleLeft, true);
            HudKit.Label(x + 56f, y + 12f, nw + 10f, 34f, name, 28f * pop, HudKit.Text, TextAnchor.MiddleLeft, true);
            string count = player.Kills + (player.Kills == 1 ? " ELIMINATION" : " ELIMINATIONS");
            HudKit.Label(cx - 150f, y + 50f, 300f, 20f, count, 14f, HudKit.TextDim, TextAnchor.MiddleCenter, true);
            HudKit.Alpha = 1f;
        }

        void DrawHealing(float cx, float cy)
        {
            var kind = player.HealingKind;
            float total = kind == ItemKind.Bandage ? RoyaleItems.BandageTime : kind == ItemKind.Medkit ? RoyaleItems.MedkitTime : RoyaleItems.ChaiTime;
            float p = player.HealProgress;
            var green = new Color(0.45f, 0.95f, 0.5f);
            HudKit.Ring(cx, cy, 46f, 5f, 0f, 1f, new Color(0f, 0f, 0f, 0.35f));
            HudKit.Ring(cx, cy, 46f, 5f, 0f, p, green);
            string icon = kind == ItemKind.Bandage ? "bandage" : kind == ItemKind.Medkit ? "medkit" : "chai";
            HudKit.IconAt(icon, cx - 34f, cy + 74f, 20f, green);
            HudKit.Label(cx - 20f, cy + 62f, 160f, 24f, RoyaleItems.ItemName(kind, 0, Tier.Common).ToUpper() + "  " + ((1f - p) * total).ToString("0.0") + " S",
                         16f, HudKit.Text, TextAnchor.MiddleLeft, true);
        }

        /// <summary>The pickup or ride prompt: a key cap for "[E] ..." prompts, then the words.</summary>
        void DrawPrompt(float cx, float cy)
        {
            string prompt = arsenal != null ? arsenal.Prompt : null;
            if (string.IsNullOrEmpty(prompt) && mover != null) prompt = mover.Prompt;
            if (string.IsNullOrEmpty(prompt)) return;
            string key = null, text = prompt;
            if (prompt.Length > 4 && prompt[0] == '[' && prompt[2] == ']')
            {
                key = prompt.Substring(1, 1);
                text = prompt.Substring(3).Trim();
            }
            text = text.Substring(0, 1).ToUpper() + text.Substring(1);
            float tw = HudKit.TextWidth(text, 19f), kw = key != null ? 34f : 0f;
            float pw = tw + kw + 28f, x = cx + 60f, y = cy + 26f;
            HudKit.Panel(x, y, pw, 36f, HudKit.Ink, 5f);
            if (key != null) HudKit.KeyCap(x + 8f, y + 6f, key, 24f);
            HudKit.Label(x + 14f + kw, y, tw + 4f, 36f, text, 19f, HudKit.Text);
        }

        // ------------------------------------------------------------------ hotbar and ammo

        static readonly string[] KitIcons = { "bandage", "medkit", "chai", "grenade", "smoke" };
        static readonly string[] KitKeys = { "Q", "Q", "Z", "G", "4" };
        static readonly ItemKind[] KitKinds = { ItemKind.Bandage, ItemKind.Medkit, ItemKind.ChaiFlask, ItemKind.Grenade, ItemKind.Smoke };

        /// <summary>Bottom centre: three gun slots and five kit slots, each with its key.</summary>
        void DrawHotbar(float w, float h)
        {
            var l = player.loadout;
            const float sw = 150f, sh = 78f, kw = 60f, gap = 8f, kitGap = 6f, split = 18f;
            float total = Loadout.Slots * sw + (Loadout.Slots - 1) * gap + split + 5 * kw + 4 * kitGap;
            float x = w / 2f - total / 2f, y = h - M - sh;

            for (int i = 0; i < Loadout.Slots; i++)
            {
                var g = l.guns[i];
                bool sel = i == l.selected && g != null;
                float sy = sel ? y - 8f : y;
                if (g == null)
                {
                    HudKit.Panel(x, sy, sw, sh, new Color(0.05f, 0.06f, 0.08f, 0.35f), 6f);
                    HudKit.Outline(x, sy, sw, sh, new Color(1f, 1f, 1f, 0.08f), 1f, 6f);
                    HudKit.Alpha = 0.5f;
                    HudKit.KeyCap(x + 6f, sy + 6f, (i + 1).ToString(), 20f);
                    HudKit.Alpha = 1f;
                    HudKit.Label(x, sy, sw, sh, "EMPTY", 14f, new Color(1f, 1f, 1f, 0.3f), TextAnchor.MiddleCenter, true, false);
                }
                else
                {
                    Color tc = RoyaleItems.TierColours[(int)g.tier];
                    HudKit.Panel(x, sy, sw, sh, sel ? HudKit.InkDeep : HudKit.Ink, 6f);
                    HudKit.FadePanel(x + 1f, sy + sh - 40f, sw - 2f, 40f, HudKit.WithAlpha(tc, sel ? 0.42f : 0.26f), false);
                    HudKit.Panel(x, sy + sh - 4f, sw, 4f, tc, 2f);
                    if (sel) HudKit.Outline(x - 1f, sy - 1f, sw + 2f, sh + 2f, HudKit.Accent, 2f, 7f);
                    HudKit.KeyCap(x + 6f, sy + 6f, (i + 1).ToString(), 20f);
                    HudKit.Label(x + 30f, sy + 4f, sw - 38f, 22f, g.def.name.ToUpper(), 14f, sel ? HudKit.Text : HudKit.TextDim, TextAnchor.MiddleRight, true);
                    HudKit.IconAt(HudIcons.ForGun(g.def.gunClass), x + sw / 2f - 4f, sy + 42f, 32f, sel ? HudKit.Text : HudKit.WithAlpha(HudKit.Text, 0.75f));
                    Color ammoCol = g.loaded == 0 ? HudKit.Danger : HudKit.Text;
                    HudKit.Label(x + 8f, sy + sh - 26f, sw - 16f, 20f, g.loaded + " / " + l.CountFor(g), 15f, ammoCol, TextAnchor.MiddleRight, true);
                }
                x += sw + gap;
            }
            x += split - gap;

            int[] counts = { l.bandages, l.medkits, l.chai, l.grenades, l.smokes };
            for (int k = 0; k < 5; k++)
            {
                bool has = counts[k] > 0;
                bool healing = player.IsHealing && player.HealingKind == KitKinds[k];
                HudKit.Panel(x, y, kw, sh, has ? HudKit.Ink : new Color(0.05f, 0.06f, 0.08f, 0.35f), 6f);
                if (healing)
                {
                    float p = player.HealProgress;
                    HudKit.Panel(x, y + sh * (1f - p), kw, sh * p, new Color(0.45f, 0.95f, 0.5f, 0.3f), 6f);
                    HudKit.Outline(x - 1f, y - 1f, kw + 2f, sh + 2f, new Color(0.45f, 0.95f, 0.5f), 2f, 7f);
                }
                HudKit.Alpha = has ? 1f : 0.45f;
                HudKit.KeyCap(x + 4f, y + 4f, KitKeys[k], 20f);
                HudKit.IconAt(KitIcons[k], x + kw / 2f, y + 40f, 30f, has ? HudKit.Text : new Color(1f, 1f, 1f, 0.5f));
                HudKit.Label(x, y + sh - 24f, kw - 7f, 20f, counts[k].ToString(), 17f, HudKit.Text, TextAnchor.MiddleRight, true);
                HudKit.Alpha = 1f;
                x += kw + kitGap;
            }
        }

        /// <summary>Bottom right: the gun in hand, its tier and fittings, rounds loaded and carried.</summary>
        void DrawWeaponPanel(float w, float h)
        {
            var l = player.loadout;
            var g = l.Current;
            float right = w - M, bottom = h - M;
            HudKit.FadeSidePanel(right - 440f, bottom - 150f, 440f + M, 150f + M, HudKit.WithAlpha(HudKit.Ink, 0.9f), true);

            if (g == null)
            {
                HudKit.Label(right - 300f, bottom - 72f, 300f, 60f, "FISTS", 48f, HudKit.Text, TextAnchor.MiddleRight, true);
                HudKit.Label(right - 300f, bottom - 22f, 300f, 20f, "FIND A GUN", 15f, HudKit.TextDim, TextAnchor.MiddleRight, true);
                return;
            }
            int reserve = l.CountFor(g);
            Color tc = RoyaleItems.TierColours[(int)g.tier];

            // Rounds: loaded large, carried after it, the calibre under.
            string carried = "/ " + reserve;
            float cw = Mathf.Max(70f, HudKit.TextWidth(carried, 28f, true) + 6f);
            bool low = g.loaded <= Mathf.Max(1, g.Magazine / 4);
            HudKit.Label(right - cw - 220f, bottom - 82f, 214f, 74f, g.loaded.ToString(), 70f, low ? HudKit.Danger : HudKit.Text, TextAnchor.MiddleRight, true);
            HudKit.Label(right - cw, bottom - 66f, cw, 36f, carried, 28f, reserve == 0 ? HudKit.Danger : HudKit.TextDim, TextAnchor.MiddleLeft, true);
            HudKit.Icon("ammo", right - cw, bottom - 28f, 16f, HudKit.TextDim);
            HudKit.Label(right - cw + 16f, bottom - 30f, cw, 20f, AmmoShort[(int)g.def.ammo], 14f, HudKit.TextDim, TextAnchor.MiddleLeft, true);

            // Chips, right to left: fire mode, tier, then each fitted part.
            float x = right;
            float cy = bottom - 112f;
            string mode = g.def.bolt ? "BOLT" : g.def.burst > 1 ? "BURST" : g.def.automatic ? "AUTO" : g.def.pellets > 1 ? "PUMP" : "SEMI";
            x = Chip(x, cy, mode, new Color(1f, 1f, 1f, 0.14f), HudKit.Text);
            x = Chip(x, cy, RoyaleItems.TierNames[(int)g.tier].ToUpper(), HudKit.WithAlpha(tc, 0.85f), new Color(0.06f, 0.06f, 0.07f));
            for (int i = g.parts.Length - 1; i >= 0; i--)
                if (g.parts[i] >= 0) x = Chip(x, cy, PartShort[g.parts[i]], new Color(0f, 0f, 0f, 0.4f), HudKit.Text);

            HudKit.Label(right - 400f, bottom - 150f, 400f, 34f, g.def.name.ToUpper(), 28f, Color.Lerp(tc, Color.white, 0.25f), TextAnchor.MiddleRight, true);
        }

        /// <summary>A small rounded tag ending at "right"; returns the x for the next one to its left.</summary>
        static float Chip(float right, float y, string text, Color back, Color fore)
        {
            float tw = HudKit.TextWidth(text, 13f, true) + 14f;
            HudKit.Panel(right - tw, y, tw, 22f, back, 4f);
            HudKit.Label(right - tw, y, tw, 22f, text, 13f, fore, TextAnchor.MiddleCenter, true, false);
            return right - tw - 6f;
        }

        /// <summary>Pickups and short notes, stacked above the player card.</summary>
        void DrawPickups(float h)
        {
            float y = h - M - 98f - 44f - 30f;
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                float age = Time.time - pickups[i].time;
                if (age > 4f) continue;
                HudKit.Alpha = Mathf.Clamp01(4f - age);
                string t = pickups[i].text;
                float tw = HudKit.TextWidth(t, 16f);
                float x = M - 16f * Mathf.Pow(Mathf.Clamp01(1f - age / 0.15f), 2f);
                HudKit.Panel(x, y, tw + 26f, 26f, HudKit.Ink, 4f);
                HudKit.Panel(x, y + 5f, 3f, 16f, HudKit.Accent, 1.5f);
                HudKit.Label(x + 14f, y, tw + 4f, 26f, t, 16f, HudKit.Text);
                HudKit.Alpha = 1f;
                y -= 31f;
            }
        }

        /// <summary>The vehicle panel while riding, above the hotbar: speed and the vehicle's health.</summary>
        void DrawVehicle(float w, float h)
        {
            if (mover == null || mover.Riding == null) return;
            var v = mover.Riding;
            float pw = 300f, x = w / 2f - pw / 2f, y = h - M - 78f - 18f - 74f;
            HudKit.Panel(x, y, pw, 64f, HudKit.Ink, 8f);
            HudKit.IconAt("wheel", x + 28f, y + 26f, 28f, HudKit.Text);
            string name = v.DisplayName.ToUpper();
            HudKit.Label(x + 50f, y + 6f, 150f, 22f, name, 16f, HudKit.TextDim, TextAnchor.MiddleLeft, true);
            HudKit.Label(x + 150f, y + 2f, 100f, 40f, Mathf.RoundToInt(Mathf.Abs(v.Speed) * 3.6f).ToString(), 36f, HudKit.Text, TextAnchor.MiddleRight, true);
            HudKit.Label(x + 254f, y + 14f, 40f, 22f, "KM/H", 13f, HudKit.TextDim, TextAnchor.MiddleLeft, true);
            float f = Mathf.Clamp01(v.Health / v.maxHealth);
            Color c = f > 0.5f ? HudKit.Health : f > 0.25f ? new Color(1f, 0.7f, 0.2f) : HudKit.Danger;
            HudKit.Bar(x + 50f, y + 40f, pw - 62f, 7f, f, c, 0, 2f);
            // The boost tank: saffron while it can be used, dim while it refills after running dry.
            Color bc = v.Boosting ? HudKit.Text : v.Boost < 0.25f ? HudKit.WithAlpha(HudKit.Accent, 0.45f) : HudKit.Accent;
            HudKit.Bar(x + 50f, y + 51f, pw - 62f, 5f, v.Boost, bc, 0, 2f);
            HudKit.Label(x + 8f, y + 44f, 40f, 16f, "BOOST", 10f, HudKit.TextDim, TextAnchor.MiddleLeft, true);
            HudKit.Hints(w / 2f, y - 24f, 22f, "SHIFT", "Boost", "H", "Horn", "SPACE", "Brake");
        }

        // ------------------------------------------------------------------ the drop

        void DrawDrop(float w, float h)
        {
            string title, sub = null;
            switch (diver.Current)
            {
                case SkyDiver.State.Aboard: title = "ON THE SKY BUS"; sub = "PICK A SPOT ON THE MAP, THEN JUMP"; break;
                case SkyDiver.State.FreeFall: title = "FREE FALL"; break;
                case SkyDiver.State.Glide: title = "GLIDING"; break;
                default: title = "GET READY"; break;
            }
            float y = h - M - 98f - 110f;
            HudKit.Label(0, y, w, 40f, title, 32f, HudKit.Text, TextAnchor.MiddleCenter, true);
            if (sub != null) HudKit.Label(0, y + 36f, w, 20f, sub, 15f, HudKit.TextDim, TextAnchor.MiddleCenter, true);

            if (diver.Current == SkyDiver.State.Aboard && director != null && director.helicopter != null)
            {
                // How far along its line the sky bus is: everyone is out by the end.
                float bw = 360f, bx = w / 2f - bw / 2f, by = y + 62f;
                HudKit.Bar(bx, by, bw, 6f, director.helicopter.Progress, HudKit.Accent, 0, 3f);
                HudKit.Label(bx, by + 8f, bw, 18f, "LAST CALL AT THE EDGE OF CAMPUS", 12f, HudKit.TextDim, TextAnchor.MiddleCenter, true);
                string rich = RichPickingsLine();
                if (rich != null)
                {
                    // Where the loot is best this match.
                    string line = "RICH PICKINGS:  " + rich;
                    float tw = HudKit.TextWidth(line, 15f, true) + 56f;
                    float ry = y - 52f;
                    HudKit.Panel(w / 2f - tw / 2f, ry, tw, 32f, new Color(0f, 0f, 0f, 0.55f), 6f);
                    HudKit.IconAt("star", w / 2f - tw / 2f + 20f, ry + 16f, 16f, HudKit.Accent);
                    HudKit.Label(w / 2f - tw / 2f + 34f, ry, tw - 44f, 32f, line, 15f, HudKit.Accent, TextAnchor.MiddleCenter, true);
                }
                HudKit.Hints(w / 2f, h - M - 30f, 26f, "SPACE", "Jump", "M", "Map", "MOUSE", "Look around");
                return;
            }
            if (diver.Current == SkyDiver.State.FreeFall)
                HudKit.Hints(w / 2f, h - M - 30f, 26f, "SPACE", "Open the glider", "W", "Dive", "S", "Slow");
            else
                HudKit.Hints(w / 2f, h - M - 30f, 26f, "MOUSE", "Steer", "W", "Faster", "S", "Float");
            DrawAltimeter(w, h);
        }

        /// <summary>A height gauge on the right while falling or gliding, with the glider's opening height marked.</summary>
        void DrawAltimeter(float w, float h)
        {
            const float top = 400f, gh = 300f;
            float x = w - M - 90f, y = h / 2f - gh / 2f;
            float alt = diver.HeightAboveGround;
            HudKit.Panel(x + 60f, y, 6f, gh, new Color(0f, 0f, 0f, 0.45f), 3f);
            for (int m = 0; m <= (int)top; m += 50)
            {
                float ty = y + gh - gh * m / top;
                HudKit.Fill(x + 52f, ty - 0.5f, m % 100 == 0 ? 8f : 5f, 1f, new Color(1f, 1f, 1f, 0.5f));
                if (m % 100 == 0) HudKit.Label(x + 70f, ty - 8f, 30f, 16f, m.ToString(), 11f, HudKit.TextDim);
            }
            float oy = y + gh - gh * diver.autoOpenHeight / top;
            HudKit.Fill(x + 48f, oy - 1f, 22f, 2f, HudKit.Accent);
            float f = Mathf.Clamp01(alt / top);
            HudKit.Panel(x + 60f, y + gh - gh * f, 6f, gh * f, HudKit.WithAlpha(HudKit.Accent, 0.8f), 3f);
            float my = y + gh - gh * f;
            HudKit.Panel(x - 30f, my - 15f, 82f, 30f, HudKit.InkDeep, 5f);
            HudKit.Label(x - 30f, my - 15f, 76f, 30f, Mathf.RoundToInt(alt) + " M", 19f, HudKit.Text, TextAnchor.MiddleRight, true);
        }

        // ------------------------------------------------------------------ scope

        void DrawScope(float w, float h)
        {
            if (!HudKit.Repaint) return;
            float s = h * 0.95f;
            float x = (w - s) / 2f, y = (h - s) / 2f;
            HudKit.Fill(0, 0, x + 1f, h, Color.black);
            HudKit.Fill(x + s - 1f, 0, w - x - s + 1f, h, Color.black);
            HudKit.Fill(x, 0, s, y + 1f, Color.black);
            HudKit.Fill(x, y + s - 1f, s, h - y - s + 1f, Color.black);
            GUI.DrawTexture(HudKit.R(x, y, s, s), HudKit.Scope, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 0f);
            Color line = new Color(0f, 0f, 0f, 0.9f);
            HudKit.Fill(x, h / 2f - 0.5f, s, 1f, line);
            HudKit.Fill(w / 2f - 0.5f, y, 1f, s, line);
            HudKit.Fill(w / 2f - 40f, h / 2f - 2f, 80f, 4f, line);
            HudKit.Fill(w / 2f - 2f, h / 2f, 4f, 60f, line);
        }
    }
}
