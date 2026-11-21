"""
Engines, tyres, horns and crashes; the helicopter; the sky bus (propellers and the balloon's burner).

An engine loop is a train of firing pulses (one per cylinder firing), each a short burst through the
exhaust's resonances, with a little cycle-to-cycle variation so it doesn't buzz like a synth. Each
vehicle has three loops (idle, low load, high revs) rendered at a fixed speed; VehicleAudio crossfades
them by engine speed and bends their pitch between. Every loop is a whole number of firing cycles,
so it repeats without a click.
"""
import numpy as np
from dsp import *
from bank import sound
from weapons import metal_tick, plastic_thunk, friction, rattle, spring


def engine_loop(r, fire_hz, seconds, resonances, bright, rough, mech, twostroke=False, burble=0.0):
    cycles = max(1, int(round(fire_hz * seconds)))
    seconds = cycles / fire_hz
    n = n_of(seconds)
    t = np.arange(n) / SR
    x = np.zeros(n)
    period = SR / fire_hz
    # Firing events with jitter in time and strength.
    for k in range(cycles):
        at = int(k * period + r.normal(0, rough * period * 0.04))
        at %= n
        g = 1 + r.normal(0, rough * 0.25)
        if twostroke and r.random() < 0.08:
            g *= 0.3                        # a missed beat, the two-stroke rattle
        x[at] += g
    pulse_len = n_of(min(0.02, 0.8 / fire_hz))
    kern = r.standard_normal(pulse_len) * np.exp(-np.arange(pulse_len) / (pulse_len / 4)) + \
        np.exp(-np.arange(pulse_len) / (pulse_len / 6)) * 3
    from scipy.signal import fftconvolve
    x = fftconvolve(np.concatenate([x, x[:pulse_len]]), kern)[:n + pulse_len]
    x[:pulse_len] += x[n:n + pulse_len]
    x = x[:n]
    body = np.zeros(n)
    for (f, q, g) in resonances:
        body += resonator(np.concatenate([x, x, x]), f, q)[n:2 * n] * g
    body = lp(np.concatenate([body, body]), bright, 2)[n:]
    # Mechanical noise: valves and chain, a gritty band.
    mechn = bp(np.concatenate([white(seconds, r)] * 2), 1500, 7000)[n:] * mech
    am = 1 + 0.5 * np.cos(2 * np.pi * fire_hz * t)
    body += mechn * am * 0.3
    if burble > 0:
        body += lp(np.concatenate([white(seconds, r)] * 2), 400)[n:] * burble
    return saturate(body / (np.std(body) + 1e-9) * 0.35, 1.6)


def rpm_set(name, cat, idle_hz, low_hz, high_hz, res, bright, rough, mech, level, two=False, burble=0.0, maxd=90):
    for tag, hz, br, lvl in (("idle", idle_hz, bright * 0.6, level - 3), ("low", low_hz, bright * 0.8, level), ("high", high_hz, bright, level + 1)):
        def fn(r, v, hz=hz, br=br):
            return engine_loop(r, hz, 3.0, res, br, rough, mech, two, burble)
        sound("%s_%s" % (name, tag), cat, bus="Vehicles", level=lvl, min=3, max=maxd, curve=1.2, loop=True, priority=20, pitch=(1, 1))(fn)


# 110 cc scooter: one cylinder, four stroke (fires every second turn), high-strung.
rpm_set("scooter", "vehicles/scooter", 14, 38, 75, [(180, 4, 1.0), (520, 6, 0.5), (1400, 5, 0.25)], 4500, 1.0, 0.25, -20)
# Auto-rickshaw: a small two-stroke single (fires every turn), the famous clattering putter.
rpm_set("auto", "vehicles/auto", 28, 55, 95, [(240, 3, 1.0), (700, 5, 0.7), (2100, 4, 0.4)], 6000, 1.6, 0.6, -19, two=True)
# Patrol 4x4: a four-cylinder diesel, low and lumpy.
rpm_set("jeep", "vehicles/jeep", 24, 60, 120, [(95, 3, 1.0), (260, 4, 0.6), (850, 4, 0.3)], 2800, 0.8, 0.35, -19, burble=0.3, maxd=110)


# Utility cart: electric. A motor whine that follows speed, gear mesh and a contactor hum.
def cart(r, whine_hz, seconds=3.0):
    t = times(seconds)
    x = sine(whine_hz, seconds) * 0.5 + sine(whine_hz * 2, seconds) * 0.25 + sine(whine_hz * 3.02, seconds) * 0.1
    x += sine(whine_hz * 0.25, seconds) * 0.2
    x += bp(white(seconds, r), whine_hz * 0.8, whine_hz * 1.3) * 0.3
    x += sine(100, seconds) * 0.15 + sine(50, seconds) * 0.1
    return x


for tag, hz, lvl in (("idle", 220, -30), ("low", 420, -24), ("high", 900, -22)):
    sound("cart_" + tag, "vehicles/cart", bus="Vehicles", level=lvl, min=3, max=60, curve=1.3, loop=True, priority=20, pitch=(1, 1))(
        lambda r, v, hz=hz: cart(r, hz))


@sound("tyres_road", "vehicles/tyres", bus="Vehicles", level=-26, min=3, max=50, curve=1.3, loop=True, priority=60, pitch=(1, 1))
def tyres_road(r, v):
    s = 3.0
    return loopify(bp(pink(s + 0.5, r), 300, 5000) * (1 + 0.1 * lp(r.standard_normal(n_of(s + 0.5)), 8) * 5), 0.5)


@sound("tyres_dirt", "vehicles/tyres", bus="Vehicles", level=-24, min=3, max=50, curve=1.3, loop=True, priority=60, pitch=(1, 1))
def tyres_dirt(r, v):
    s = 3.0
    x = grains(s + 0.5, r, 2500, 600, 7000, 0.0015) + lp(brown(s + 0.5, r), 300) * 0.5
    return loopify(x, 0.5)


@sound("tyres_skid", "vehicles/tyres", bus="Vehicles", level=-22, min=3, max=70, curve=1.2, loop=True, priority=50, pitch=(1, 1))
def tyres_skid(r, v):
    s = 2.0
    t = times(s + 0.5)
    f = 1050 + 90 * np.sin(2 * np.pi * 3.1 * t) + 40 * lp(r.standard_normal(len(t)), 10) * 20
    x = tv_bp(white(s + 0.5, r), f, 14, 64) * 1.5 + bp(white(s + 0.5, r), 2000, 6000) * 0.2
    return loopify(x, 0.5)


@sound("wind_rush", "vehicles/tyres", bus="Vehicles", level=-26, spatial=False, stereo=True, loop=True, priority=60, reverb=0, pitch=(1, 1))
def wind_rush(r, v):
    s = 4.0
    return loopify(np.stack([bp(pink(s + 1, r), 200, 3000), bp(pink(s + 1, r), 200, 3000)], 1), 1.0)


@sound("engine_start", "vehicles/common", bus="Vehicles", variants=2, level=-19, min=3, max=60, priority=40)
def engine_start(r, v):
    s = 1.2
    t = times(s)
    crank = np.zeros(n_of(s))
    for k in range(6):
        crank = mix([(crank, 0, 1), (plastic_thunk(r, 120, 0.08, 1.0), 0.05 + k * 0.09, 1)], s)
    catch = engine_loop(r, 30, 0.6, [(200, 3, 1.0), (600, 5, 0.5)], 4000, 1.5, 0.3)
    return mix([(crank * 0.8, 0, 1), (fade(catch, 0.02, 0.3), 0.6, 1.2)], s)


@sound("engine_stop", "vehicles/common", bus="Vehicles", variants=1, level=-24, min=3, max=50, priority=60)
def engine_stop(r, v):
    x = engine_loop(r, 20, 0.6, [(180, 3, 1.0), (500, 5, 0.4)], 3000, 1.6, 0.2)
    return x * np.linspace(1, 0, len(x)) ** 2


@sound("boost", "vehicles/common", bus="Vehicles", variants=2, level=-21, min=3, max=60, priority=50)
def boost(r, v):
    s = 0.8
    t = times(s)
    c = np.interp(t, [0, 0.2, s], [400, 3000, 1500])
    return tv_bp(white(s, r), c, 2.0, 64) * np.sin(np.pi * t / s) ** 1.5 + lp(brown(s, r), 200) * env_exp(s, 0.3) * 0.5


@sound("horn_high", "vehicles/horns", bus="Vehicles", level=-14, min=5, max=150, curve=1.0, priority=30, pitch=(0.99, 1.01))
def horn_high(r, v):
    """The scooter's electric beep: a buzzing vibrator horn, two short blasts."""
    s = 0.75
    def blast(d):
        t = times(d)
        x = square(415, d, 0.42) * 0.6 + square(515, d, 0.4) * 0.5
        x = bp(x, 350, 4000)
        return x * env_lin(d, [(0, 0), (0.01, 1), (d - 0.03, 1), (d, 0)])
    return mix([(blast(0.22), 0, 1), (blast(0.32), 0.33, 1)], s)


@sound("horn_low", "vehicles/horns", bus="Vehicles", level=-14, min=5, max=180, curve=1.0, priority=30, pitch=(0.99, 1.01))
def horn_low(r, v):
    s = 0.7
    x = square(330, s, 0.45) * 0.6 + square(412, s, 0.45) * 0.55
    x = bp(x, 250, 3500)
    return x * env_lin(s, [(0, 0), (0.015, 1), (s - 0.05, 1), (s, 0)])


@sound("horn_bulb", "vehicles/horns", bus="Vehicles", level=-15, min=5, max=140, curve=1.0, priority=30, pitch=(0.98, 1.02))
def horn_bulb(r, v):
    """The auto-rickshaw's squeeze horn: a reedy honk that rises as the bulb is pressed, twice."""
    s = 0.9
    def honk(d):
        t = times(d)
        f = np.interp(t, [0, 0.05, d], [260, 340, 320])
        x = saw(f, d) * 0.6 + square(f * 1.5, d, 0.3) * 0.25
        x = bp(x, 300, 3500)
        x = peak(x, 900, 2, 8)
        return x * env_lin(d, [(0, 0), (0.03, 1), (d - 0.05, 0.9), (d, 0)])
    return mix([(honk(0.26), 0, 1), (honk(0.34), 0.38, 1)], s)


@sound("crash", "vehicles/common", bus="Vehicles", variants=4, level=-14, min=4, max=100, curve=1.1, priority=25)
def crash(r, v):
    s = 1.2
    t = times(s)
    hitx = mix([(lp(white(s, r), 4000) * env_exp(s, 0.02, 0.0005) * 1.5, 0, 1),
                (sine(glide_exp(140, 50, s, 0.04), s) * env_exp(s, 0.12), 0, 1.2)])
    fs, ds, a = metal_modes(r.uniform(300, 500), 12, r, spread=1.4, decay=0.35)
    panel = modal(fs, ds, a, s, r) * 0.35
    deb = grains(s, r, 200, 1500, 9000, 0.002, rate_env=np.exp(-t / 0.3)) * 0.6
    return saturate(hitx + panel + deb, 1.5)


@sound("bump", "vehicles/common", bus="Vehicles", variants=3, level=-22, min=3, max=40, priority=70)
def bump(r, v):
    s = 0.35
    return mix([(sine(glide_exp(110, 55, s, 0.03), s) * env_exp(s, 0.06), 0, 1), (rattle(r, 0.15, 4, 2200, 0.4), 0.01, 1),
                (spring(r, 0.25, 350), 0.01, 0.4)])


# ---------------------------------------------------------------------------- helicopter

@sound("rotor", "vehicles/heli", bus="Vehicles", level=-16, min=10, max=600, curve=0.9, loop=True, priority=15, pitch=(1, 1))
def rotor(r, v):
    """Blade slap (four blades, 5 turns a second), the tail rotor's buzz and the turbine whine."""
    blade = 21.0
    cycles = 84
    s = cycles / blade
    t = times(s)
    ph = (t * blade) % 1.0
    slap = np.exp(-ph / 0.06) * lp(np.tile(white(1 / blade, r), cycles + 1)[: len(t)], 1500) * 1.5
    slap += np.exp(-ph / 0.12) * np.sin(2 * np.pi * blade * 2 * t) * 0.8
    tail = saw(blade * 5.3, s) * 0.12 * (0.7 + 0.3 * np.cos(2 * np.pi * blade * t))
    whine = sine(5200, s) * 0.03 + sine(7350, s) * 0.015 + bp(white(s, r), 4500, 7000) * 0.05
    wash = lp(brown(s, r), 250) * 0.6
    return slap + bp(tail, 80, 2000) + whine + wash


@sound("rotor_far", "vehicles/heli", bus="Vehicles", level=-20, min=60, max=1200, curve=0.8, loop=True, priority=18, pitch=(1, 1))
def rotor_far(r, v):
    blade = 21.0
    cycles = 84
    s = cycles / blade
    t = times(s)
    ph = (t * blade) % 1.0
    return lp(np.exp(-ph / 0.08) * white(s, r), 500, 2) * 2 + np.exp(-ph / 0.1) * np.sin(2 * np.pi * blade * t) * 0.5


@sound("turbine_start", "vehicles/heli", bus="Vehicles", level=-18, min=8, max=300, priority=20)
def turbine_start(r, v):
    s = 6.0
    t = times(s)
    f = 400 + 4800 * (1 - np.exp(-t / 2.0))
    x = sine(f, s) * 0.2 + bp(white(s, r), 2000, 7000) * 0.2 * (t / s)
    return x * env_lin(s, [(0, 0), (0.3, 1), (s - 0.5, 1), (s, 0)])


# ---------------------------------------------------------------------------- the sky bus

@sound("skybus_props", "vehicles/skybus", bus="Vehicles", level=-18, min=25, max=1500, curve=0.7, loop=True, priority=10, pitch=(1, 1))
def skybus_props(r, v):
    """Twin propellers (beating slightly against each other) and the little engines driving them."""
    s = 4.0
    t = times(s)
    a = 46.0
    b = 46.7
    p1 = np.exp(-((t * a) % 1) / 0.15)
    p2 = np.exp(-((t * b) % 1) / 0.15)
    x = lp(white(s, r) * (p1 + p2), 900, 2) * 1.2 + sine(a, s) * 0.3 + sine(b * 2, s) * 0.15
    x += engine_loop(r, 60, s, [(160, 3, 1.0), (480, 5, 0.4)], 2500, 0.8, 0.15) * 0.4
    return loopify(np.concatenate([x, x[: n_of(0.5)]]), 0.5)


@sound("burner", "vehicles/skybus", bus="Vehicles", variants=2, level=-14, min=30, max=1500, curve=0.7, priority=12)
def burner(r, v):
    """A propane burner: a fierce, fluttering roar with a click of the valve."""
    s = 2.2
    t = times(s)
    flutter = 1 + 0.35 * np.sin(2 * np.pi * 13 * t) * lp(r.standard_normal(len(t)), 5) * 6
    roar = (lp(brown(s, r), 700) * 1.2 + bp(white(s, r), 400, 3500) * 0.6) * flutter
    e = env_lin(s, [(0, 0), (0.08, 1), (s - 0.4, 0.9), (s, 0)])
    valve = metal_tick(r, 1800, 0.03, 0.1, 0.8)
    return mix([(valve, 0, 1), (roar * e, 0.02, 1)])


@sound("balloon_creak", "vehicles/skybus", bus="Vehicles", variants=3, level=-24, min=8, max=120, priority=80)
def balloon_creak(r, v):
    """Wicker and ropes taking the load."""
    s = 0.9
    t = times(s)
    f = 150 + 120 * lp(r.standard_normal(len(t)), 4) * 10
    x = bp(saw(np.abs(f) + 60, s), 300, 3000) * env_hann(s) * (0.5 + 0.5 * (r.random(len(t)) < 0.3))
    return x * 0.6 + grains(s, r, 120, 800, 5000, 0.003) * env_hann(s) * 0.5


@sound("bus_door_wind", "vehicles/skybus", bus="Vehicles", level=-20, spatial=False, stereo=True, loop=True, priority=10, reverb=0)
def bus_door_wind(r, v):
    """Inside the bus by the open rear door: buffeting air and the props through the floor."""
    s = 5.0
    t = times(s + 1)
    buff = 0.7 + 0.3 * np.abs(np.sin(2 * np.pi * 1.3 * t + lp(r.standard_normal(len(t)), 1) * 20))
    x = lp(pink(s + 1, r), 1400) * buff
    y = lp(pink(s + 1, r), 1400) * buff[::-1]
    return loopify(np.stack([x, y], 1), 1.0)
