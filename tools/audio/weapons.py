"""
Guns, reloads, casings, grenades and explosions.

A shot is built the way a real one sounds: the muzzle blast (a pressure spike and a burst of hot
gas), the body thump of the gun and the shooter's chest, the supersonic crack of rifle rounds, and
the action cycling. The blast is then played through the place it happens in: open ground among
buildings and hills (slapback echoes and a long rolling rumble), or a room (a dense, short tail).
Distant shots are the same blast heard through hundreds of metres of air: the highs gone, the crack
and the echoes left.
"""
import numpy as np
from dsp import *
from bank import sound

# Each gun: energy (loudness and weight), body (Hz of the thump), blast (s, how long the gas roars),
# crack (supersonic snap), bright (Hz, top of the blast), action (what cycles after the shot).
GUNS = {
    "pistol":   dict(energy=0.55, body=150, blast=0.009, crack=0.12, bright=7500, action="slide", tone=1.15),
    "smg":      dict(energy=0.50, body=170, blast=0.007, crack=0.08, bright=7000, action="smg", tone=1.25),
    "shotgun":  dict(energy=1.05, body=82,  blast=0.024, crack=0.0,  bright=5200, action="none", tone=0.85),
    "rifle":    dict(energy=0.80, body=112, blast=0.012, crack=0.75, bright=9500, action="rifle", tone=1.0),
    "dmr":      dict(energy=0.95, body=96,  blast=0.015, crack=1.0,  bright=9500, action="rifle_heavy", tone=0.92),
    "revolver": dict(energy=0.88, body=102, blast=0.019, crack=0.1,  bright=6500, action="cylinder", tone=0.95),
    "carbine":  dict(energy=0.70, body=126, blast=0.011, crack=0.6,  bright=9000, action="rifle", tone=1.08),
    "double":   dict(energy=1.15, body=74,  blast=0.030, crack=0.0,  bright=5000, action="none", tone=0.8),
    "lmg":      dict(energy=0.86, body=102, blast=0.013, crack=0.8,  bright=9000, action="belt", tone=0.95),
    "sniper":   dict(energy=1.30, body=68,  blast=0.022, crack=1.35, bright=10500, action="none", tone=0.78),
}

# Tails are shared by calibre group (the room hears the blast, not the gun's name).
GROUP = {"pistol": "light", "smg": "light", "revolver": "light", "shotgun": "shot", "double": "shot",
         "rifle": "rifle", "carbine": "rifle", "lmg": "rifle", "dmr": "heavy", "sniper": "heavy"}
GROUP_GUN = {"light": "pistol", "shot": "shotgun", "rifle": "rifle", "heavy": "dmr"}


# ---------------------------------------------------------------------------- parts

def metal_tick(r, base=2600, decay=0.035, seconds=0.12, gain=1.0, lo=1500, hi=12000):
    fs, ds, a = metal_modes(base, 6, r, spread=1.9, decay=decay)
    x = bp(white(seconds, r), lo, hi, 2) * env_exp(seconds, 0.0012) * 1.4
    x += modal(fs, ds, a, seconds, r) * 0.5
    return x * gain


def plastic_thunk(r, f=420, seconds=0.12, gain=1.0):
    x = modal([f, f * 1.9, f * 3.3, f * 5.1], [0.025, 0.015, 0.009, 0.006], [1, 0.6, 0.35, 0.2], seconds, r, 0.05)
    x += lp(white(seconds, r), 3000) * env_exp(seconds, 0.004) * 0.8
    return x * gain


def friction(r, seconds, lo=900, hi=6000, grit=0.5, swell=True):
    """Metal or polymer sliding on itself: band noise with a rough, uneven grain."""
    x = bp(white(seconds, r), lo, hi, 2)
    rough = 1 + grit * lp(r.standard_normal(n_of(seconds)), 60) * 3
    e = env_hann(seconds) if swell else env_exp(seconds, seconds / 3)
    return x * np.clip(rough, 0.1, 3) * e * 0.5


def spring(r, seconds=0.25, f=900):
    t = times(seconds)
    x = np.sin(2 * np.pi * (f * t + 40 * np.sin(2 * np.pi * 17 * t))) * np.exp(-t / 0.05)
    return bp(x + white(seconds, r) * 0.1 * np.exp(-t / 0.03), 500, 5000) * 0.3


def cloth(r, seconds=0.35, bright=3500, gain=1.0):
    """A rustle of jacket and webbing."""
    x = bp(pink(seconds, r), 400, bright, 2)
    mod = np.clip(lp(r.standard_normal(n_of(seconds)), 18) * 4 + 0.4, 0, None)
    return x * mod * env_hann(seconds) * gain * 0.6


def rattle(r, seconds=0.2, count=8, base=3200, gain=0.6):
    parts = []
    for k in range(count):
        parts.append((metal_tick(r, base * r.uniform(0.7, 1.4), 0.01, 0.05, r.uniform(0.2, 0.7)), r.uniform(0, seconds * 0.85), 1))
    return mix(parts, seconds) * gain


# ---------------------------------------------------------------------------- the shot

def blast(p, r, seconds=0.6, suppressed=False):
    """The dry shot at the gun: no room, no distance."""
    t = times(seconds)
    e = p["energy"]
    out = np.zeros_like(t)
    if not suppressed:
        # Pressure spike: an N-wave a millisecond or two long.
        nw = np.zeros_like(t)
        k1, k2 = n_of(0.0006 + 0.0006 * e), n_of(0.0016 + 0.0012 * e)
        nw[:k1] = np.linspace(1, 0.2, k1)
        nw[k1:k2] = -np.linspace(0.6, 0, k2 - k1)
        out += lp(nw, 9000) * 3.0 * e
        # Hot gas: a broadband roar with a falling top end.
        gas = white(seconds, r)
        cut = 600 + p["bright"] * np.exp(-t / (p["blast"] * 3))
        gas = tv_lp(gas, cut, 64) * env_exp(seconds, p["blast"], 0.0002)
        out += gas * 1.6
        # Body thump, falling in pitch.
        f = glide_exp(p["body"] * 2.4, p["body"], seconds, 0.012)
        out += np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(seconds, 0.045 + 0.06 * e, 0.0008) * (0.9 + 0.6 * e)
        # Sub weight (what the shooter feels).
        out += np.sin(2 * np.pi * np.cumsum(glide_exp(90, 48, seconds, 0.03)) / SR) * env_exp(seconds, 0.07 + 0.05 * e, 0.002) * 0.8 * e
        # Supersonic crack.
        if p["crack"] > 0:
            out += hp(white(seconds, r), 3500, 2) * env_exp(seconds, 0.0012, 0.0001) * 1.6 * p["crack"]
        out = saturate(out * (1.6 + e), 2.5)
        # Muzzle gas hiss after the bang.
        out += bp(white(seconds, r), 1500, 6000) * env_exp(seconds, 0.03 + 0.02 * e, 0.003) * 0.12 * e
    else:
        # A can on the muzzle: a dull thup, a short hiss, and the action becomes the loudest part.
        f = glide_exp(p["body"] * 3.2, p["body"] * 1.8, seconds, 0.01)
        out += np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(seconds, 0.03, 0.001) * 0.9
        out += lp(white(seconds, r), 2200) * env_exp(seconds, 0.012, 0.0005) * 1.1
        out += bp(white(seconds, r), 2000, 7000) * env_exp(seconds, 0.04, 0.002) * 0.25
        if p["crack"] > 0.5:
            out += hp(white(seconds, r), 4000, 2) * env_exp(seconds, 0.001, 0.0001) * 0.7 * p["crack"]
        out = saturate(out * 1.6, 1.8)
    return out + action(p, r, seconds, suppressed)


def action(p, r, seconds, suppressed):
    a = p["action"]
    g = 0.35 if not suppressed else 0.7
    parts = []
    if a == "slide":
        parts += [(metal_tick(r, 2900, 0.02, 0.08, 0.8), 0.004, g), (metal_tick(r, 2400, 0.025, 0.08, 1.0), 0.028, g)]
    elif a == "smg":
        parts += [(metal_tick(r, 2300, 0.02, 0.07, 0.9), 0.006, g), (plastic_thunk(r, 520, 0.07, 0.6), 0.03, g)]
    elif a == "rifle":
        parts += [(metal_tick(r, 2100, 0.03, 0.1, 0.9), 0.008, g), (metal_tick(r, 1700, 0.035, 0.1, 1.0), 0.05, g),
                  (spring(r, 0.08, 1200), 0.01, g * 0.5)]
    elif a == "rifle_heavy":
        parts += [(metal_tick(r, 1600, 0.04, 0.12, 1.0), 0.01, g), (metal_tick(r, 1300, 0.05, 0.12, 1.1), 0.065, g)]
    elif a == "cylinder":
        parts += [(metal_tick(r, 3400, 0.012, 0.05, 0.6), 0.03, g)]
    elif a == "belt":
        parts += [(metal_tick(r, 1800, 0.03, 0.1, 1.0), 0.008, g), (rattle(r, 0.07, 5, 3800, 0.5), 0.02, g)]
    if not parts:
        return np.zeros(n_of(seconds))
    return mix(parts, seconds)


def outdoor_tail(r, seconds=3.2, big=1.0):
    ir = outdoor_ir(seconds, r, roll=2.2 + big, roll_gain=0.45 + 0.15 * big, lp_hz=1800 + 600 * big)
    ir[0] = 0
    return ir


def indoor_tail(r, seconds=1.4, rt=0.85):
    ir = room_ir(seconds, rt, r, bright=6500, dark=1600,
                 early=[(0.004, 0.8), (0.009, 0.6), (0.014, 0.55), (0.021, 0.45), (0.029, 0.35), (0.038, 0.3)])
    return ir


def gun_events():
    for gun, p in GUNS.items():
        def near(r, v, p=p):
            x = blast(p, r, 0.55)
            return trim(x, -60)
        sound(gun, "weapons/" + gun, bus="Weapons", variants=4, level=-9, vol=1.0, pitch=(0.97, 1.03),
              min=6, max=140, curve=1.3, far=gun + "_far", farFrom=45, farFull=140, delay=True, priority=10)(near)

        def far(r, v, p=p, gun=gun):
            x = blast(p, r, 0.4)
            x = lp(x, 2600 if p["crack"] > 0.3 else 1700, 2)
            if p["crack"] > 0:
                x += hp(blast(dict(p, energy=0.1), r, 0.4), 4500) * 0.2 * p["crack"]
            ir = outdoor_ir(4.0, r, roll=2.8 + p["energy"], roll_gain=0.9, lp_hz=1400, stereo=False)
            y = convolve(x, ir)
            return trim(y, -55)
        sound(gun + "_far", "weapons/" + gun, bus="Weapons", variants=2, level=-16, vol=0.9, pitch=(0.95, 1.05),
              min=40, max=900 if p["energy"] > 0.9 else 650, curve=0.8, delay=True, priority=20)(far)

    for grp, gun in GROUP_GUN.items():
        p = GUNS[gun]

        def tail_out(r, v, p=p, grp=grp):
            dry = lp(blast(p, r, 0.4), 5000)
            y = convolve(dry, outdoor_tail(r, 3.4, 1.0 if grp != "light" else 0.6))
            return trim(y, -60)
        sound("tail_out_" + grp, "weapons/tails", bus="Weapons", variants=2, level=-22, vol=0.85, pitch=(0.95, 1.05),
              min=8, max=260, curve=1.0, stereo=True, delay=True, priority=30, reverb=0)(tail_out)

        def tail_in(r, v, p=p, grp=grp):
            dry = blast(p, r, 0.4)
            y = convolve(dry, indoor_tail(r, 1.5, 0.7 if grp == "light" else 0.95))
            return trim(y, -60)
        sound("tail_in_" + grp, "weapons/tails", bus="Weapons", variants=2, level=-19, vol=0.9, pitch=(0.95, 1.05),
              min=6, max=120, curve=1.2, stereo=True, priority=30, reverb=0)(tail_in)

    for grp, gun in (("light", "pistol"), ("rifle", "rifle"), ("heavy", "dmr")):
        p = GUNS[gun]

        def supp(r, v, p=p):
            return trim(blast(p, r, 0.35, suppressed=True), -60)
        sound("suppressed_" + grp, "weapons/suppressed", bus="Weapons", variants=3, level=-17, vol=0.9,
              min=3, max=70, curve=1.5, priority=15)(supp)
    # The old single name still works (zombie mode and anything not yet split by calibre).
    sound("suppressed", "weapons/suppressed", bus="Weapons", variants=2, level=-17, min=3, max=70, curve=1.5)(
        lambda r, v: trim(blast(GUNS["rifle"], r, 0.35, suppressed=True), -60))


gun_events()


# ---------------------------------------------------------------------------- handling

def _w(name, variants=3, level=-24, min=1.5, max=25, vol=1.0, bus="Weapons"):
    return sound(name, "weapons/handling", bus=bus, variants=variants, level=level, min=min, max=max, vol=vol, priority=60)


@_w("dry_fire")
def dry_fire(r, v):
    return mix([(metal_tick(r, 3100, 0.015, 0.08, 1.0), 0, 1), (metal_tick(r, 2500, 0.01, 0.05, 0.4), 0.012, 1)])


@_w("mag_out_light")
def mag_out_light(r, v):
    return mix([(metal_tick(r, 3300, 0.01, 0.05, 0.6), 0, 1), (friction(r, 0.09, 1500, 7000), 0.01, 0.8),
                (plastic_thunk(r, 600, 0.08, 0.4), 0.09, 1)])


@_w("mag_out_heavy")
def mag_out_heavy(r, v):
    return mix([(metal_tick(r, 2600, 0.015, 0.06, 0.8), 0, 1), (friction(r, 0.13, 1000, 6000), 0.015, 0.9),
                (plastic_thunk(r, 380, 0.1, 0.5), 0.13, 1)])


@_w("mag_in_light")
def mag_in_light(r, v):
    return mix([(friction(r, 0.07, 1500, 7000), 0, 0.7), (plastic_thunk(r, 520, 0.1, 1.0), 0.07, 1),
                (metal_tick(r, 2900, 0.03, 0.1, 1.0), 0.072, 1)])


@_w("mag_in_heavy")
def mag_in_heavy(r, v):
    return mix([(friction(r, 0.1, 1000, 6000), 0, 0.8), (plastic_thunk(r, 330, 0.12, 1.1), 0.1, 1),
                (metal_tick(r, 2100, 0.045, 0.14, 1.2), 0.103, 1), (rattle(r, 0.1, 3, 3500, 0.3), 0.11, 1)])


@_w("slide_release")
def slide_release(r, v):
    return mix([(metal_tick(r, 3000, 0.01, 0.04, 0.5), 0, 1), (friction(r, 0.03, 2000, 8000, swell=False), 0.004, 0.5),
                (metal_tick(r, 2300, 0.05, 0.15, 1.3), 0.035, 1)])


@_w("bolt_rack")
def bolt_rack(r, v):
    return mix([(metal_tick(r, 2500, 0.02, 0.06, 0.7), 0, 1), (friction(r, 0.07, 1200, 7000), 0.01, 0.6),
                (spring(r, 0.12, 1100), 0.02, 1), (metal_tick(r, 2000, 0.045, 0.15, 1.3), 0.11, 1)])


@_w("charge_rifle")
def charge_rifle(r, v):
    return mix([(metal_tick(r, 2200, 0.02, 0.06, 0.8), 0, 1), (friction(r, 0.09, 1000, 6500), 0.01, 0.7),
                (spring(r, 0.18, 950), 0.02, 1), (metal_tick(r, 1800, 0.05, 0.18, 1.4), 0.17, 1)])


@_w("shell_insert", variants=4)
def shell_insert(r, v):
    return mix([(friction(r, 0.05, 800, 5000), 0, 0.6), (plastic_thunk(r, 700, 0.06, 0.7), 0.045, 1),
                (metal_tick(r, 3600, 0.012, 0.05, 0.6), 0.05, 1)])


@_w("pump", level=-21)
def pump(r, v):
    return mix([(metal_tick(r, 1900, 0.03, 0.08, 0.9), 0, 1), (friction(r, 0.12, 700, 5000), 0.01, 1.0),
                (plastic_thunk(r, 300, 0.1, 0.8), 0.12, 1), (metal_tick(r, 1600, 0.04, 0.14, 1.3), 0.21, 1),
                (friction(r, 0.08, 700, 5000), 0.13, 0.8), (plastic_thunk(r, 280, 0.12, 1.0), 0.215, 1)])


@_w("cyl_open")
def cyl_open(r, v):
    return mix([(metal_tick(r, 3200, 0.015, 0.05, 0.7), 0, 1), (metal_tick(r, 2400, 0.04, 0.12, 1.0), 0.05, 1)])


@_w("cyl_eject")
def cyl_eject(r, v):
    parts = [(friction(r, 0.06, 1500, 7000), 0, 0.7)]
    for k in range(6):
        parts.append((metal_tick(r, r.uniform(3500, 6000), 0.06, 0.2, 0.4), 0.05 + r.uniform(0.05, 0.3), 1))
    return mix(parts)


@_w("speedloader")
def speedloader(r, v):
    return mix([(rattle(r, 0.08, 6, 4200, 0.6), 0, 1), (friction(r, 0.05, 1500, 6000), 0.06, 0.6),
                (metal_tick(r, 2800, 0.02, 0.08, 0.8), 0.11, 1)])


@_w("cyl_close")
def cyl_close(r, v):
    parts = [(metal_tick(r, 2200, 0.05, 0.15, 1.3), 0, 1)]
    for k in range(5):
        parts.append((metal_tick(r, 3500, 0.006, 0.03, 0.35 - k * 0.05), 0.05 + k * 0.035, 1))
    return mix(parts)


@_w("break_open")
def break_open(r, v):
    return mix([(metal_tick(r, 2000, 0.03, 0.08, 0.9), 0, 1), (spring(r, 0.15, 700), 0.02, 0.8),
                (metal_tick(r, 1500, 0.06, 0.18, 1.2), 0.09, 1)])


@_w("break_close")
def break_close(r, v):
    return mix([(friction(r, 0.05, 700, 4000), 0, 0.5), (metal_tick(r, 1300, 0.07, 0.2, 1.4), 0.05, 1),
                (plastic_thunk(r, 260, 0.1, 0.8), 0.052, 1)])


@_w("hulls_eject")
def hulls_eject(r, v):
    return mix([(plastic_thunk(r, 900, 0.06, 0.6), 0, 1), (plastic_thunk(r, 950, 0.06, 0.6), 0.03, 1),
                (plastic_thunk(r, 500, 0.08, 0.5), 0.28, 1), (plastic_thunk(r, 560, 0.08, 0.4), 0.34, 1)])


@_w("belt_open")
def belt_open(r, v):
    return mix([(metal_tick(r, 1700, 0.04, 0.1, 1.0), 0, 1), (spring(r, 0.2, 600), 0.02, 0.8),
                (metal_tick(r, 1200, 0.07, 0.2, 1.0), 0.12, 1)])


@_w("belt_rattle")
def belt_rattle(r, v):
    return mix([(rattle(r, 0.35, 18, 3600, 0.8), 0, 1), (friction(r, 0.3, 900, 5000), 0.02, 0.5)])


@_w("belt_close")
def belt_close(r, v):
    return mix([(metal_tick(r, 1300, 0.08, 0.22, 1.4), 0, 1), (plastic_thunk(r, 240, 0.12, 0.9), 0.002, 1)])


@_w("bolt_open")
def bolt_open(r, v):
    return mix([(metal_tick(r, 2700, 0.02, 0.06, 0.8), 0, 1), (friction(r, 0.11, 1100, 6500), 0.04, 0.9),
                (metal_tick(r, 2000, 0.03, 0.1, 0.8), 0.15, 1)])


@_w("bolt_close")
def bolt_close(r, v):
    return mix([(friction(r, 0.1, 1100, 6500), 0, 0.9), (metal_tick(r, 1900, 0.04, 0.12, 1.1), 0.1, 1),
                (metal_tick(r, 2900, 0.015, 0.05, 0.8), 0.17, 1)])


@_w("bolt", level=-21)
def bolt_cycle(r, v):
    """The sniper's bolt worked between shots: up, back (the case flies out), forward, down."""
    return mix([(bolt_open(r, v), 0, 1), (metal_tick(r, 5200, 0.08, 0.25, 0.3), 0.2, 1), (bolt_close(r, v), 0.3, 1)])


@_w("equip", variants=4, level=-25)
def equip(r, v):
    return mix([(cloth(r, 0.3, 3000, 1.0), 0, 1), (rattle(r, 0.12, 4, 2600, 0.5), 0.12, 1),
                (metal_tick(r, 2100, 0.03, 0.1, 0.5), 0.2, 1)])


@_w("holster", variants=2, level=-27)
def holster(r, v):
    return mix([(cloth(r, 0.25, 2500, 1.0), 0, 1), (plastic_thunk(r, 350, 0.08, 0.4), 0.18, 1)])


@_w("aim_in", variants=3, level=-30)
def aim_in(r, v):
    return mix([(cloth(r, 0.16, 4500, 1.0), 0, 1), (rattle(r, 0.05, 2, 3000, 0.25), 0.08, 1)])


@_w("aim_out", variants=3, level=-32)
def aim_out(r, v):
    return cloth(r, 0.14, 4000, 1.0)


@_w("reload", level=-22)
def reload_generic(r, v):
    """The old one-shot reload (zombie mode, bots far away): out, in, charge."""
    return mix([(mag_out_heavy(r, v), 0, 1), (mag_in_heavy(r, v), 0.55, 1), (charge_rifle(r, v), 0.95, 1)])


# Casings landing: brass rings on hard ground, dulls on soft; shotgun hulls bounce hollow.
@sound("shell_hard", "weapons/casings", bus="Weapons", variants=5, level=-30, min=1, max=12, priority=110)
def shell_hard(r, v):
    parts = []
    t = 0.0
    g = 1.0
    for k in range(3 + int(r.integers(0, 2))):
        parts.append((modal([r.uniform(4200, 5200), r.uniform(9000, 11000), r.uniform(13000, 15000)],
                            [0.05, 0.03, 0.02], [1, 0.5, 0.3], 0.2, r), t, g))
        t += r.uniform(0.04, 0.1) * g
        g *= 0.5
    return mix(parts)


@sound("shell_soft", "weapons/casings", bus="Weapons", variants=3, level=-34, min=1, max=8, priority=120)
def shell_soft(r, v):
    return lp(white(0.06, r), 2500) * env_exp(0.06, 0.008)


@sound("hull_hard", "weapons/casings", bus="Weapons", variants=3, level=-30, min=1, max=10, priority=115)
def hull_hard(r, v):
    return mix([(plastic_thunk(r, 950, 0.07, 1.0), 0, 1), (plastic_thunk(r, 1000, 0.06, 0.5), 0.09, 1),
                (plastic_thunk(r, 1050, 0.05, 0.25), 0.15, 1)])


# ---------------------------------------------------------------------------- bullets in the air

@sound("crack", "weapons/flyby", bus="Weapons", variants=4, level=-15, min=1, max=10, spatial=False, priority=12, reverb=0.3)
def crack(r, v):
    """A supersonic round passing close: a sharp snap, then a short tearing hiss."""
    s = 0.2
    nw = np.zeros(n_of(s))
    k1, k2 = n_of(0.0004), n_of(0.0011)
    nw[:k1] = np.linspace(1, 0.1, k1)
    nw[k1:k2] = -np.linspace(0.9, 0, k2 - k1)
    x = hp(nw, 800) * 4 + hp(white(s, r), 3000) * env_exp(s, 0.02, 0.0005) * 0.3
    return saturate(x, 1.5)


@sound("whiz", "weapons/flyby", bus="Weapons", variants=4, level=-20, min=1, max=10, spatial=False, priority=14)
def whiz(r, v):
    s = 0.22
    t = times(s)
    centre = np.interp(t, [0, s], [r.uniform(2500, 3500), r.uniform(700, 1000)])
    x = tv_bp(white(s, r), centre, 3.0, 64) * np.sin(np.pi * np.clip(t / s, 0, 1)) ** 2
    return x


@sound("ricochet", "weapons/flyby", bus="Weapons", variants=4, level=-22, min=2, max=60, priority=70)
def ricochet(r, v):
    s = 0.5
    f = sweep(r.uniform(3200, 4200), r.uniform(1400, 1900), s, 0.6)
    x = sine(f, s) * env_exp(s, 0.18, 0.002) * (0.6 + 0.4 * sine(r.uniform(25, 40), s))
    x += hp(white(s, r), 2000) * env_exp(s, 0.01)
    return x


# ---------------------------------------------------------------------------- melee

@sound("swing", "weapons/melee", bus="Weapons", variants=3, level=-26, min=1, max=12, priority=90)
def swing(r, v):
    s = 0.3
    t = times(s)
    centre = np.interp(t, [0, 0.12, s], [500, 2600, 900])
    return tv_bp(pink(s, r), centre, 1.2, 64) * np.sin(np.pi * t / s) ** 2


@sound("punch_hit", "weapons/melee", bus="Weapons", variants=3, level=-18, min=1.5, max=20, priority=40)
def punch_hit(r, v):
    s = 0.2
    return mix([(lp(white(s, r), 1500) * env_exp(s, 0.02), 0, 1),
                (sine(glide_exp(160, 70, s, 0.03), s) * env_exp(s, 0.05), 0, 1)])


# ---------------------------------------------------------------------------- grenades

@sound("pin_pull", "weapons/grenade", bus="Weapons", variants=2, level=-26, min=1, max=10, priority=60)
def pin_pull(r, v):
    return mix([(metal_tick(r, 3800, 0.02, 0.06, 0.8), 0, 1), (friction(r, 0.05, 2000, 8000), 0.02, 0.5),
                (metal_tick(r, 5200, 0.12, 0.3, 0.3), 0.08, 1)])


@sound("throw", "weapons/grenade", bus="Weapons", variants=2, level=-25, min=1, max=10, priority=80)
def throw(r, v):
    return mix([(cloth(r, 0.3, 3000), 0, 1), (swing(r, v), 0.08, 0.8)])


@sound("clink", "weapons/grenade", bus="World", variants=4, level=-24, min=2, max=30, priority=70)
def grenade_bounce(r, v):
    return mix([(metal_tick(r, r.uniform(1800, 2600), 0.05, 0.2, 1.0, 600, 9000), 0, 1),
                (plastic_thunk(r, 260, 0.08, 0.6), 0, 1)])


def explosion_core(r, s, size=1.0):
    t = times(s)
    # The shock: a long N-wave and a huge low thump.
    nw = np.zeros_like(t)
    k1, k2 = n_of(0.004 * size), n_of(0.012 * size)
    nw[:k1] = np.linspace(1, 0.1, k1)
    nw[k1:k2] = -np.linspace(0.5, 0, k2 - k1)
    x = lp(nw, 3000) * 3
    x += sine(glide_exp(120, 32, s, 0.08), s) * env_exp(s, 0.35 * size, 0.003) * 1.4
    # The fireball roar: brown noise with a falling top.
    roar = tv_lp(brown(s, r) * 0.6 + white(s, r) * 0.4, 300 + 6000 * np.exp(-t / 0.08), 128)
    x += roar * env_exp(s, 0.25 * size, 0.002) * 1.6
    x = saturate(x * 2.2, 2.5)
    # Debris pattering down.
    deb = grains(s, r, 90, 1200, 7000, 0.003, rate_env=np.clip((t - 0.15) / 0.2, 0, 1) * np.exp(-t / 0.8))
    x += deb * 0.6
    return x


@sound("explosion", "weapons/explosion", bus="Weapons", variants=3, level=-8, vol=1.0, min=10, max=260, curve=1.2,
       far="explosion_far", farFrom=70, farFull=220, delay=True, priority=5)
def explosion(r, v):
    x = explosion_core(r, 2.0)
    ir = outdoor_ir(2.5, r, roll=2.0, roll_gain=0.25, stereo=False)
    ir[0] = 1
    return trim(convolve(x, ir * 0.8), -60)


@sound("explosion_far", "weapons/explosion", bus="Weapons", variants=2, level=-14, min=60, max=1200, curve=0.8, delay=True, priority=8)
def explosion_far(r, v):
    x = lp(explosion_core(r, 1.5, 1.2), 900, 2)
    ir = outdoor_ir(5.0, r, roll=4.0, roll_gain=1.2, lp_hz=800, stereo=False)
    return trim(convolve(x, ir), -55)


@sound("smoke_pop", "weapons/grenade", bus="World", variants=2, level=-20, min=3, max=50, priority=50)
def smoke_pop(r, v):
    s = 2.2
    t = times(s)
    pop = mix([(plastic_thunk(r, 300, 0.1, 1.2), 0, 1), (metal_tick(r, 2000, 0.03, 0.08, 0.6), 0, 1)])
    hiss = bp(white(s, r), 1500, 9000) * np.clip(t / 0.05, 0, 1) * np.exp(-t / 1.0) * 0.6
    return mix([(pop, 0, 1), (hiss, 0.03, 1)])


@sound("smoke_hiss", "weapons/grenade", bus="World", level=-28, min=2, max=25, loop=True, priority=100)
def smoke_hiss(r, v):
    s = 3.0
    x = bp(white(s + 0.5, r), 1800, 8000) * (1 + 0.3 * lp(r.standard_normal(n_of(s + 0.5)), 6) * 4)
    return loopify(x, 0.5)


@sound("tinnitus", "weapons/explosion", bus="Weapons", level=-26, spatial=False, stereo=True, priority=3, reverb=0)
def tinnitus(r, v):
    s = 3.5
    t = times(s)
    e = np.clip(t / 0.05, 0, 1) * np.exp(-t / 1.1)
    left = sine(4100, s) * e + sine(4170, s) * e * 0.3
    right = sine(4130, s) * e + sine(4060, s) * e * 0.3
    return np.stack([left, right], 1)
