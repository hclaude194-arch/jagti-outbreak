"""
Footsteps by surface, landings, bullet impacts by material, glass, bodies, healing items,
pickups, stashes, supply drops, the lift, zip lines, wading, and the drop from the sky.

Footsteps are a heel and a toe, a few tens of milliseconds apart, each shaped by what the shoe meets:
grass swishes and crackles, gravel is a crunch of many tiny grains, concrete is a short low knock
with a scuff, the tiled and stone floors indoors click, wood resonates hollow, steel stairs ring,
and water splashes.
"""
import numpy as np
from dsp import *
from bank import sound
from weapons import metal_tick, plastic_thunk, friction, cloth, rattle, spring

SURFACES = ["grass", "dirt", "gravel", "concrete", "tile", "wood", "metal", "water"]


def knock(r, f, seconds, tau, gain=1.0):
    """The low body of a foot landing (the leg, the shoe sole, the floor)."""
    return sine(glide_exp(f * 1.8, f, seconds, 0.008), seconds) * env_exp(seconds, tau, 0.001) * gain


def surface_hit(surface, r, weight=1.0, seconds=0.25):
    """One contact (heel or toe) on a surface; weight 1 walking, ~1.6 running, ~2.5 landing."""
    s = seconds
    w = weight
    if surface == "grass":
        swish = bp(white(s, r), 1500, 9000) * env_ad(s, 0.006, 0.035 * w)
        crackle = grains(s, r, 1400 * w, 2500, 11000, 0.0008, rate_env=env_exp(s, 0.05 * w))
        x = swish * 0.6 + crackle * 0.8 + knock(r, 70, s, 0.02, 0.35 * w)
    elif surface == "dirt":
        x = lp(white(s, r), 2500) * env_ad(s, 0.002, 0.02 * w) * 0.9
        x += grains(s, r, 700 * w, 1200, 6000, 0.001, rate_env=env_exp(s, 0.04)) * 0.6
        x += knock(r, 85, s, 0.025, 0.6 * w)
    elif surface == "gravel":
        x = grains(s, r, 3500 * w, 1200, 9000, 0.0012, rate_env=env_ad(s, 0.004, 0.05 * w)) * 1.3
        x += lp(white(s, r), 1500) * env_exp(s, 0.015) * 0.4 + knock(r, 80, s, 0.02, 0.4 * w)
    elif surface == "concrete":
        x = knock(r, 110, s, 0.018, 0.9 * w)
        x += bp(white(s, r), 800, 7000) * env_ad(s, 0.0008, 0.01) * 0.9
        x += bp(white(s, r), 2500, 9000) * env_ad(s, 0.012, 0.03) * 0.25   # the scuff
    elif surface == "tile":
        x = knock(r, 140, s, 0.014, 0.7 * w)
        x += modal([r.uniform(1800, 2400), r.uniform(3500, 4300)], [0.012, 0.008], [1, 0.5], s, r) * 0.6
        x += hp(white(s, r), 2500) * env_exp(s, 0.004) * 0.6
    elif surface == "wood":
        x = modal([r.uniform(130, 170), r.uniform(310, 360), r.uniform(520, 600), r.uniform(900, 1100)],
                  [0.05, 0.03, 0.02, 0.012], [1, 0.7, 0.4, 0.25], s, r) * 0.9 * w
        x += bp(white(s, r), 600, 5000) * env_exp(s, 0.006) * 0.6
    elif surface == "metal":
        fs, ds, a = metal_modes(r.uniform(260, 340), 8, r, spread=1.6, decay=0.12)
        x = modal(fs, ds, a, s * 1.6, r) * 0.6 * w
        x = pad_to(x, n_of(s * 1.6))
        x[: n_of(s)] += knock(r, 95, s, 0.02, 0.7 * w)
        x += pad_to(bp(white(s, r), 1500, 8000) * env_exp(s, 0.005) * 0.5, len(x))
        return x
    elif surface == "water":
        t = times(s * 1.6)
        x = bp(white(s * 1.6, r), 400, 6000) * env_ad(s * 1.6, 0.01, 0.06 * w) * 1.0
        for k in range(int(4 + 4 * w)):        # bubbles: little rising chirps
            at = r.uniform(0.01, 0.12 * w)
            bs = 0.05
            f0 = r.uniform(600, 1800)
            b = sine(sweep(f0, f0 * 1.8, bs), bs) * env_exp(bs, 0.015)
            x = mix([(x, 0, 1), (b, at, 0.3)], s * 1.6)
        return x + knock(r, 60, s * 1.6, 0.03, 0.3 * w)
    else:
        x = knock(r, 100, s, 0.02)
    return x


def step(surface, r, weight):
    gap = r.uniform(0.025, 0.05) / (1 + 0.3 * (weight - 1))
    heel = surface_hit(surface, r, weight)
    toe = surface_hit(surface, r, weight * 0.6)
    x = mix([(heel, 0, 1), (toe, gap, 0.55)])
    return x


def gear(r, seconds=0.2):
    """What a running fighter carries: webbing rustle and a faint rattle."""
    return mix([(cloth(r, seconds, 3200, 0.8), 0, 1), (rattle(r, 0.1, 2, 2800, 0.25), 0.04, 1)])


for surf in SURFACES:
    lvl = {"grass": -26, "dirt": -24, "gravel": -23, "concrete": -24, "tile": -23, "wood": -23, "metal": -22, "water": -22}[surf]
    sound("step_walk_" + surf, "foley/steps/" + surf, bus="World", variants=6, level=lvl, vol=0.75, pitch=(0.93, 1.07),
          min=1.5, max=22, curve=1.6, priority=95)(lambda r, v, s=surf: trim(step(s, r, 1.0), -55))
    sound("step_run_" + surf, "foley/steps/" + surf, bus="World", variants=6, level=lvl + 3, vol=0.9, pitch=(0.93, 1.07),
          min=2, max=38, curve=1.4, priority=85)(lambda r, v, s=surf: trim(mix([(step(s, r, 1.7), 0, 1), (gear(r), 0.01, 0.5)]), -55))
    sound("land_" + surf, "foley/steps/" + surf, bus="World", variants=2, level=lvl + 6, pitch=(0.95, 1.05),
          min=2, max=40, curve=1.3, priority=60)(lambda r, v, s=surf: trim(mix([(surface_hit(s, r, 2.6, 0.4), 0, 1),
                                                                                  (surface_hit(s, r, 1.6, 0.3), 0.05, 0.6),
                                                                                  (gear(r, 0.3), 0, 0.8),
                                                                                  (knock(r, 55, 0.3, 0.06, 0.8), 0, 1)]), -55))


@sound("land", "foley/body", bus="World", variants=2, level=-18, min=2, max=40)
def land(r, v):
    return mix([(surface_hit("dirt", r, 2.6, 0.4), 0, 1), (gear(r, 0.3), 0, 0.8), (knock(r, 55, 0.3, 0.06, 0.8), 0, 1)])


@sound("jump", "foley/body", bus="World", variants=3, level=-28, min=1.5, max=15, priority=100)
def jump(r, v):
    return mix([(gear(r, 0.25), 0, 1), (cloth(r, 0.2, 3000), 0.02, 0.6)])


@sound("crouch", "foley/body", bus="World", variants=2, level=-31, min=1, max=8, priority=110)
def crouch(r, v):
    return cloth(r, 0.3, 2800, 1.0)


@sound("vault", "foley/body", bus="World", variants=2, level=-22, min=2, max=20)
def vault(r, v):
    return mix([(cloth(r, 0.35, 3500, 1.0), 0, 1), (plastic_thunk(r, 180, 0.1, 0.8), 0.02, 1),
                (surface_hit("concrete", r, 1.3), 0.32, 0.7)])


@sound("splash", "foley/water", bus="World", variants=3, level=-20, min=2, max=30)
def splash(r, v):
    return trim(mix([(surface_hit("water", r, 2.5, 0.5), 0, 1), (surface_hit("water", r, 1.5, 0.4), 0.12, 0.6)]), -55)


@sound("wade", "foley/water", bus="World", level=-26, min=2, max=20, loop=True)
def wade(r, v):
    s = 4.0
    x = bp(pink(s + 0.5, r), 300, 4000) * (0.6 + 0.4 * np.clip(lp(r.standard_normal(n_of(s + 0.5)), 3) * 6, -1, 1))
    for k in range(40):
        bs = 0.05
        f0 = r.uniform(500, 1600)
        x = mix([(x, 0, 1), (sine(sweep(f0, f0 * 1.6, bs), bs) * env_exp(bs, 0.012), r.uniform(0, s), 0.15)], s + 0.5)
    return loopify(x, 0.5)


# ---------------------------------------------------------------------------- bodies

@sound("hit_flesh", "foley/body", bus="World", variants=4, level=-16, min=2, max=40, priority=25)
def hit_flesh(r, v):
    s = 0.25
    return mix([(lp(white(s, r), 1800) * env_exp(s, 0.018), 0, 1.0), (knock(r, 95, s, 0.04, 1.0), 0, 1),
                (bp(white(s, r), 2000, 6000) * env_ad(s, 0.003, 0.02), 0.003, 0.4)])


@sound("hit_armour", "foley/body", bus="World", variants=4, level=-16, min=2, max=40, priority=25)
def hit_armour(r, v):
    s = 0.35
    return mix([(knock(r, 120, s, 0.03, 0.8), 0, 1), (metal_tick(r, r.uniform(1400, 1900), 0.07, s, 0.9, 800, 9000), 0, 1),
                (plastic_thunk(r, 340, 0.1, 0.8), 0, 1)])


@sound("death_grunt", "foley/body", bus="World", variants=4, level=-20, min=2, max=35, priority=30)
def death_fall(r, v):
    """No voice: the body and kit hitting the ground."""
    return mix([(cloth(r, 0.3, 3000), 0, 0.8), (knock(r, 60, 0.4, 0.08, 1.0), 0.18, 1),
                (surface_hit("dirt", r, 2.0, 0.3), 0.19, 0.9), (rattle(r, 0.2, 5, 2600, 0.5), 0.2, 1),
                (surface_hit("dirt", r, 1.2, 0.3), 0.42, 0.5)])


# ---------------------------------------------------------------------------- bullet impacts

@sound("impact_concrete", "foley/impacts", bus="World", variants=5, level=-19, min=2, max=60, curve=1.3, priority=55)
def impact_concrete(r, v):
    s = 0.5
    x = bp(white(s, r), 1500, 10000) * env_exp(s, 0.004, 0.0001) * 1.4
    x += knock(r, 180, s, 0.012, 0.6)
    t = times(s)
    x += grains(s, r, 600, 2000, 9000, 0.001, rate_env=np.exp(-t / 0.12)) * 0.6      # chips falling
    return trim(x, -55)


@sound("impact_dirt", "foley/impacts", bus="World", variants=5, level=-21, min=2, max=50, curve=1.3, priority=60)
def impact_dirt(r, v):
    s = 0.45
    t = times(s)
    x = lp(white(s, r), 3000) * env_exp(s, 0.01, 0.0002) * 1.2 + knock(r, 90, s, 0.025, 0.9)
    x += grains(s, r, 900, 1500, 7000, 0.0015, rate_env=np.clip(t / 0.03, 0, 1) * np.exp(-t / 0.12)) * 0.5
    return trim(x, -55)


@sound("impact_metal", "foley/impacts", bus="World", variants=5, level=-18, min=2, max=70, curve=1.2, priority=50)
def impact_metal(r, v):
    s = 0.8
    fs, ds, a = metal_modes(r.uniform(700, 1300), 10, r, spread=1.5, decay=0.25)
    x = modal(fs, ds, a, s, r) * 0.7 + hp(white(s, r), 2500) * env_exp(s, 0.003) * 1.2
    return trim(x, -55)


@sound("impact_wood", "foley/impacts", bus="World", variants=4, level=-19, min=2, max=50, curve=1.3, priority=55)
def impact_wood(r, v):
    s = 0.3
    x = modal([r.uniform(380, 460), r.uniform(800, 950), r.uniform(1500, 1800), r.uniform(2600, 3000)],
              [0.03, 0.02, 0.012, 0.008], [1, 0.7, 0.4, 0.3], s, r)
    x += bp(white(s, r), 1500, 8000) * env_exp(s, 0.004) * 0.8
    x += grains(s, r, 400, 2000, 8000, 0.001, rate_env=env_exp(s, 0.05)) * 0.3
    return trim(x, -55)


@sound("impact_glass", "foley/impacts", bus="World", variants=3, level=-20, min=2, max=40, priority=55)
def impact_glass(r, v):
    s = 0.3
    x = hp(white(s, r), 3000) * env_exp(s, 0.003) * 1.2
    x += modal([r.uniform(3000, 4500), r.uniform(6000, 8000)], [0.04, 0.02], [1, 0.6], s, r) * 0.5
    return x


@sound("impact_water", "foley/impacts", bus="World", variants=3, level=-22, min=2, max=40, priority=65)
def impact_water(r, v):
    return trim(surface_hit("water", r, 1.4, 0.3), -55)


@sound("glass_break", "foley/glass", bus="World", variants=4, level=-15, min=3, max=70, curve=1.2, priority=35)
def glass_break(r, v):
    s = 1.6
    t = times(s)
    x = hp(white(s, r), 1200) * (np.exp(-t / 0.005) * 1.4 + np.exp(-t / 0.08) * 0.4)
    # The pane breaking up: a fast cluster of bright rings.
    for k in range(int(r.integers(45, 70))):
        at = 0.01 + r.random() * r.random() * 1.1
        d = 0.15
        f = r.uniform(2200, 9000)
        sh = modal([f, f * r.uniform(1.4, 2.3)], [r.uniform(0.01, 0.05), 0.01], [1, 0.4], d, r)
        x = mix([(x, 0, 1), (sh, at, r.uniform(0.08, 0.35))], s)
    # Shards hitting the floor later.
    x += grains(s, r, 250, 3000, 12000, 0.002, rate_env=np.clip((t - 0.25) / 0.1, 0, 1) * np.exp(-(t - 0.25) / 0.4)) * 0.5
    return trim(x, -60)


# ---------------------------------------------------------------------------- items

def tear(r, seconds, lo=1500, hi=8000):
    """Velcro or tape: a rapid rough crackle."""
    t = times(seconds)
    return grains(seconds, r, 9000, lo, hi, 0.0004) * env_hann(seconds) * 0.8


@sound("pickup", "foley/items", bus="World", variants=3, level=-24, min=1, max=12, priority=80)
def pickup(r, v):
    return mix([(cloth(r, 0.2, 3500), 0, 1), (plastic_thunk(r, 500, 0.08, 0.7), 0.1, 1)])


@sound("pickup_ammo", "foley/items", bus="World", variants=3, level=-23, min=1, max=12, priority=80)
def pickup_ammo(r, v):
    return mix([(cloth(r, 0.15, 3000), 0, 0.6), (rattle(r, 0.22, 14, 4200, 0.9), 0.04, 1), (plastic_thunk(r, 420, 0.08, 0.6), 0.25, 1)])


@sound("pickup_gun", "foley/items", bus="World", variants=3, level=-22, min=1, max=12, priority=80)
def pickup_gun(r, v):
    return mix([(cloth(r, 0.25, 3000), 0, 0.8), (metal_tick(r, 1900, 0.04, 0.12, 1.0), 0.12, 1),
                (rattle(r, 0.12, 4, 2700, 0.4), 0.14, 1), (charge_lite(r), 0.3, 0.5)])


def charge_lite(r):
    return mix([(friction(r, 0.06, 1000, 6000), 0, 0.6), (metal_tick(r, 2000, 0.03, 0.1, 0.9), 0.07, 1)])


@sound("pickup_armour", "foley/items", bus="World", variants=2, level=-22, min=1, max=12, priority=80)
def pickup_armour(r, v):
    return mix([(cloth(r, 0.3, 2800), 0, 1), (tear(r, 0.18), 0.12, 1), (plastic_thunk(r, 260, 0.12, 0.9), 0.32, 1)])


@sound("pickup_helmet", "foley/items", bus="World", variants=2, level=-22, min=1, max=12, priority=80)
def pickup_helmet(r, v):
    return mix([(cloth(r, 0.2, 3000), 0, 0.7), (plastic_thunk(r, 380, 0.15, 1.0), 0.1, 1), (tear(r, 0.08, 2000, 7000), 0.22, 0.6)])


@sound("pickup_pack", "foley/items", bus="World", variants=2, level=-23, min=1, max=12, priority=80)
def pickup_pack(r, v):
    s = 0.35
    t = times(s)
    zipper = grains(s, r, 2500, 2000, 9000, 0.0005) * env_hann(s) * (1 + 0.5 * sine(sweep(30, 60, s), s))
    return mix([(cloth(r, 0.3, 2500), 0, 1), (zipper, 0.1, 1)])


@sound("pickup_heal", "foley/items", bus="World", variants=2, level=-24, min=1, max=12, priority=80)
def pickup_heal(r, v):
    return mix([(cloth(r, 0.2, 3500), 0, 0.8), (plastic_thunk(r, 700, 0.06, 0.6), 0.1, 1), (plastic_thunk(r, 900, 0.05, 0.4), 0.16, 1)])


@sound("pickup_grenade", "foley/items", bus="World", variants=2, level=-23, min=1, max=12, priority=80)
def pickup_grenade(r, v):
    return mix([(cloth(r, 0.2, 3000), 0, 0.8), (metal_tick(r, 2300, 0.05, 0.12, 0.9, 800, 9000), 0.12, 1)])


@sound("bandage", "foley/items", bus="World", variants=2, level=-24, min=1, max=10, priority=80)
def bandage(r, v):
    s = 1.6
    parts = [(tear(r, 0.3, 1200, 6000), 0, 1)]
    for k in range(4):       # wraps round the arm
        parts.append((cloth(r, 0.28, 4500, 0.9), 0.35 + k * 0.28, 1))
    return mix(parts, s)


@sound("medkit", "foley/items", bus="World", variants=2, level=-23, min=1, max=10, priority=80)
def medkit(r, v):
    s = 1.8
    return mix([(plastic_thunk(r, 600, 0.06, 1.0), 0, 1), (plastic_thunk(r, 650, 0.06, 0.8), 0.05, 1),   # latches
                (tear(r, 0.25, 1500, 7000), 0.3, 1), (cloth(r, 0.4, 4000), 0.6, 1),
                (pfft(r), 1.1, 1)], s)


def pfft(r):
    """An injector's short gas puff."""
    s = 0.2
    return bp(white(s, r), 2000, 9000) * env_ad(s, 0.005, 0.05) * 0.8


@sound("chai", "foley/items", bus="World", variants=2, level=-24, min=1, max=10, priority=80)
def chai(r, v):
    """The chai flask: a screw cap, a pour into the cup, a sip."""
    s = 2.0
    t = times(1.0)
    cap = mix([(friction(r, 0.25, 1500, 7000, 0.9), 0, 0.8), (plastic_thunk(r, 900, 0.05, 0.6), 0.25, 1)])
    pour_noise = bp(pink(1.0, r), 300, 3000) * env_hann(1.0)
    # the cup filling: a resonance that rises as the air column shortens
    pour = tv_bp(pour_noise, np.linspace(500, 1500, n_of(1.0)), 4.0) * 2.5 + pour_noise * 0.25
    sip = bp(white(0.35, r), 1500, 6000) * env_hann(0.35) * (1 + 0.6 * sine(23, 0.35)) * 0.5
    return mix([(cap, 0, 1), (pour, 0.35, 1), (sip, 1.5, 1)], s)


@sound("heal_done", "foley/items", bus="Interface", level=-26, spatial=False, stereo=True, priority=50, reverb=0)
def heal_done(r, v):
    s = 0.7
    a = sine(784, s) * env_exp(s, 0.25, 0.004) + sine(1175, s) * env_exp(s, 0.2, 0.004) * 0.6
    b = sine(1568, s) * env_exp(s, 0.15, 0.004) * 0.3
    x = mix([(a, 0, 1), (b, 0.08, 1)])
    return reverb(x, room_ir(1.0, 0.6, r), 0.2)


@sound("stash_open", "foley/stash", bus="World", variants=2, level=-19, min=2, max=30, priority=40)
def stash_open(r, v):
    s = 1.3
    t = times(s)
    latch = mix([(metal_tick(r, 1600, 0.04, 0.12, 1.0), 0, 1), (metal_tick(r, 1900, 0.04, 0.12, 0.9), 0.06, 1)])
    hinge = bp(saw(180 + 50 * np.sin(2 * np.pi * 7 * t[: n_of(0.5)]), 0.5), 500, 3500) * env_hann(0.5) * 0.3
    lid = mix([(plastic_thunk(r, 220, 0.15, 1.0), 0, 1), (knock(r, 80, 0.3, 0.05, 0.8), 0, 1)])
    sparkle = np.zeros(n_of(s))
    for k, f in enumerate([784, 988, 1175, 1568, 1976]):
        sparkle = mix([(sparkle, 0, 1), (sine(f, 0.6) * env_exp(0.6, 0.25, 0.004), 0.45 + k * 0.06, 0.25)], s)
    return mix([(latch, 0, 1), (hinge, 0.12, 1), (lid, 0.6, 1), (sparkle, 0, 1)], s)


@sound("stash_hum", "foley/stash", bus="World", level=-30, min=1.5, max=14, loop=True, priority=110)
def stash_hum(r, v):
    s = 4.0
    t = times(s)
    wob = 0.6 + 0.4 * np.sin(2 * np.pi * 0.75 * t)
    x = (sine(220, s) * 0.5 + sine(330, s) * 0.35 + sine(440.7, s) * 0.2 * wob + sine(660, s) * 0.08 * wob) * wob
    x += hp(white(s, r), 5000) * 0.01 * wob
    return x


# ---------------------------------------------------------------------------- supply drop

@sound("drop_land", "foley/supply", bus="World", variants=1, level=-14, min=5, max=150, curve=1.1, delay=True, priority=30)
def drop_land(r, v):
    s = 1.5
    return mix([(knock(r, 50, s, 0.15, 1.5), 0, 1), (surface_hit("dirt", r, 3.0, 0.6), 0, 1.2),
                (metal_tick(r, 900, 0.15, 0.6, 1.0, 300, 6000), 0.01, 1), (plastic_thunk(r, 160, 0.3, 1.0), 0, 1),
                (cloth(r, 1.0, 2000, 0.8), 0.2, 1)])


@sound("drop_flare", "foley/supply", bus="World", level=-24, min=3, max=40, loop=True, priority=90)
def drop_flare(r, v):
    """The red smoke beacon: a fizzing hiss with little sputters."""
    s = 3.0
    x = bp(white(s + 0.5, r), 1200, 9000) * 0.6 + grains(s + 0.5, r, 300, 1500, 8000, 0.002) * 0.8
    return loopify(x, 0.5)


@sound("drop_whistle", "foley/supply", bus="World", level=-22, min=10, max=250, priority=40)
def drop_whistle(r, v):
    """The crate falling under its canopy: fabric flutter and creaking lines."""
    s = 4.0
    t = times(s)
    flutter = bp(white(s, r), 150, 1500) * (0.5 + 0.5 * np.abs(np.sin(2 * np.pi * 6.5 * t + lp(r.standard_normal(len(t)), 2) * 8)))
    return flutter * env_lin(s, [(0, 0), (0.5, 1), (s - 0.5, 1), (s, 0)])


# ---------------------------------------------------------------------------- the drop from the sky

@sound("wind", "foley/sky", bus="World", level=-20, min=1, max=10, spatial=False, stereo=True, loop=True, priority=20, reverb=0)
def freefall_wind(r, v):
    s = 6.0
    t = times(s + 1)
    gust = 0.7 + 0.3 * np.clip(lp(r.standard_normal(len(t)), 0.7) * 30, -1, 1)
    left = tv_lp(pink(s + 1, r), 900 + 1800 * gust, 256) * gust
    right = tv_lp(pink(s + 1, r), 900 + 1800 * gust[::-1], 256) * gust[::-1]
    roar = lp(brown(s + 1, r), 200) * 0.8
    x = np.stack([left + roar, right + roar], 1)
    return loopify(x, 1.0)


@sound("whoosh", "foley/sky", bus="World", variants=2, level=-20, spatial=False, stereo=True, priority=20, reverb=0)
def whoosh(r, v):
    s = 0.9
    t = times(s)
    c = np.interp(t, [0, 0.35, s], [300, 3500, 600])
    e = np.sin(np.pi * np.clip(t / s, 0, 1)) ** 2
    return np.stack([tv_bp(pink(s, r), c, 1.0, 64) * e, tv_bp(pink(s, r), c * 0.9, 1.0, 64) * e], 1)


@sound("canopy", "foley/sky", bus="World", variants=2, level=-16, spatial=False, stereo=True, priority=20, reverb=0)
def canopy(r, v):
    """The glider opening: a fabric whip and crack as it fills, then the lines tighten."""
    s = 1.2
    t = times(s)
    whip = bp(white(s, r), 400, 6000) * env_lin(s, [(0, 0), (0.15, 0.6), (0.28, 1), (0.32, 0.2), (s, 0)])
    snap = mix([(lp(white(0.1, r), 3000) * env_exp(0.1, 0.01) * 2, 0.28, 1), (knock(r, 90, 0.3, 0.05, 1.0), 0.28, 1)], s)
    lines = mix([(spring(r, 0.3, 500), 0.35, 0.8), (friction(r, 0.3, 500, 3000), 0.33, 0.5)], s)
    x = whip + snap + lines
    return chorus(x, r, 0.002, 0.3, 2)


@sound("glide", "foley/sky", bus="World", level=-24, spatial=False, stereo=True, loop=True, priority=20, reverb=0)
def glide(r, v):
    s = 5.0
    t = times(s + 1)
    flap = 0.75 + 0.25 * np.abs(np.sin(2 * np.pi * 4.2 * t + 3 * lp(r.standard_normal(len(t)), 1.5) * 10))
    x = bp(pink(s + 1, r), 200, 2500) * flap
    y = bp(pink(s + 1, r), 200, 2500) * flap[::-1]
    return loopify(np.stack([x, y], 1), 1.0)


@sound("zip", "foley/zip", bus="World", level=-21, min=2, max=30, loop=True, priority=50)
def zip_line(r, v):
    """The trolley running down the cable: a rolling whine with rumble and cable sing."""
    s = 3.0
    t = times(s + 0.5)
    roll = grains(s + 0.5, r, 1800, 400, 4000, 0.002) * 0.8
    whine = sine(1450 + 20 * np.sin(2 * np.pi * 0.8 * t), s + 0.5) * 0.12 + sine(2900, s + 0.5) * 0.05
    rum = lp(brown(s + 0.5, r), 300) * 0.4
    return loopify(roll + whine + rum, 0.5)


@sound("zip_attach", "foley/zip", bus="World", variants=2, level=-22, min=2, max=20)
def zip_attach(r, v):
    return mix([(metal_tick(r, 1500, 0.08, 0.25, 1.2, 500, 9000), 0, 1), (spring(r, 0.2, 700), 0.02, 0.6)])


# ---------------------------------------------------------------------------- the lift

@sound("lift_hum", "foley/lift", bus="World", level=-24, spatial=False, stereo=True, loop=True, priority=40, reverb=0)
def lift_hum(r, v):
    s = 4.0
    t = times(s)
    motor = sine(100, s) * 0.4 + sine(200, s) * 0.2 + sine(300, s) * 0.08 + saw(50, s) * 0.05
    cable = bp(white(s, r), 300, 1800) * (0.5 + 0.2 * np.sin(2 * np.pi * 0.5 * t)) * 0.5
    clatter = grains(s, r, 6, 800, 4000, 0.004) * 0.4
    x = motor * 0.6 + cable + clatter
    return np.stack([x, np.roll(x, 300)], 1)


@sound("lift_ding", "foley/lift", bus="World", level=-20, spatial=False, stereo=True, priority=30, reverb=0)
def lift_ding(r, v):
    s = 1.6
    a = modal([1046, 2093, 3140, 4200], [0.6, 0.3, 0.15, 0.08], [1, 0.4, 0.2, 0.1], s, r)
    b = modal([784, 1568, 2352, 3150], [0.7, 0.35, 0.18, 0.08], [1, 0.4, 0.2, 0.1], s, r)
    x = mix([(a, 0, 1), (b, 0.32, 1)], s)
    return reverb(x, room_ir(1.2, 0.5, r), 0.25)


@sound("lift_door", "foley/lift", bus="World", variants=2, level=-22, spatial=False, stereo=True, priority=40, reverb=0)
def lift_door(r, v):
    s = 1.3
    t = times(s)
    run = bp(white(s, r), 300, 2500) * env_lin(s, [(0, 0), (0.1, 1), (0.95, 1), (1.05, 0), (s, 0)]) * 0.5
    motor = sine(140 + 20 * t, s) * env_lin(s, [(0, 0), (0.1, 0.4), (0.95, 0.4), (1.0, 0)]) * 0.3
    stop = mix([(plastic_thunk(r, 200, 0.12, 1.0), 1.0, 1), (metal_tick(r, 1300, 0.05, 0.12, 0.6), 1.0, 1)], s)
    x = run + motor + stop
    return np.stack([x, np.roll(x, 200)], 1)


@sound("lift_start", "foley/lift", bus="World", variants=1, level=-22, spatial=False, stereo=True, priority=40, reverb=0)
def lift_start(r, v):
    s = 0.6
    x = mix([(knock(r, 70, s, 0.08, 1.0), 0, 1), (metal_tick(r, 900, 0.1, 0.4, 0.8, 300, 5000), 0, 1), (spring(r, 0.3, 400), 0.05, 0.5)])
    return to_stereo(x)


# ---------------------------------------------------------------------------- doors (vehicles, crates)

@sound("door_open", "foley/doors", bus="World", variants=3, level=-22, min=2, max=20, priority=70)
def door_open(r, v):
    return mix([(metal_tick(r, 1700, 0.03, 0.08, 0.9), 0, 1), (spring(r, 0.15, 600), 0.01, 0.5),
                (friction(r, 0.2, 400, 2500, 0.7), 0.05, 0.5)])


@sound("door_close", "foley/doors", bus="World", variants=3, level=-19, min=2, max=25, priority=70)
def door_close(r, v):
    return mix([(knock(r, 85, 0.3, 0.05, 1.2), 0, 1), (plastic_thunk(r, 220, 0.12, 1.0), 0, 1),
                (metal_tick(r, 1500, 0.04, 0.1, 0.6), 0.005, 1)])
