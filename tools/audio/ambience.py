"""
The campus around the player: beds for day, evening and night, outdoors and in, wind on the roofs and
the pine hill, calls of the local birds placed around the listener, night creatures, the far
highway, and point sources the layout places (water channels, flags, the substation, the mess
kitchen, shop fridges, street lamps at night). Also the haze: its wall, the inside, and its warnings.

The birds are the ones a visitor to Jagti hears: red-vented bulbuls, common mynas, house crows,
rose-ringed parakeets, sparrows, spotted doves, and in the hot months the Asian koel's rising "ku-oo".
All are synthesised from whistles and noise, not recorded.
"""
import numpy as np
from dsp import *
from bank import sound

# ---------------------------------------------------------------------------- bird calls


def whistle(f, seconds, r, harm=0.15, vib=0.0, vib_hz=30, att=0.01, rel=0.03):
    t = times(seconds)
    ff = f * (1 + vib * np.sin(2 * np.pi * vib_hz * t)) if np.ndim(f) == 0 else f * (1 + vib * np.sin(2 * np.pi * vib_hz * t))
    x = sine(ff, seconds) + harm * sine(np.asarray(ff) * 2, seconds)
    return x * env_lin(seconds, [(0, 0), (att, 1), (max(att, seconds - rel), 1), (seconds, 0)])


def bulbul(r):
    parts = []
    t = 0.0
    for k in range(int(r.integers(3, 6))):
        d = r.uniform(0.07, 0.16)
        f0 = r.uniform(1500, 2400)
        f1 = f0 * r.uniform(0.75, 1.5)
        parts.append((whistle(sweep(f0, f1, d), d, r, 0.2, 0.02, 40), t, r.uniform(0.6, 1)))
        t += d + r.uniform(0.02, 0.08)
    return mix(parts)


def myna(r):
    parts = []
    t = 0.0
    for k in range(int(r.integers(4, 9))):
        kind = r.integers(0, 3)
        if kind == 0:      # croak
            d = r.uniform(0.08, 0.15)
            x = bp(saw(r.uniform(500, 800), d) + white(d, r) * 0.5, 600, 4000) * env_hann(d)
        elif kind == 1:    # whistle
            d = r.uniform(0.06, 0.12)
            x = whistle(sweep(r.uniform(1800, 3000), r.uniform(1500, 3500), d), d, r, 0.3)
        else:              # click chatter
            d = 0.08
            x = grains(d, r, 400, 2000, 6000, 0.001)
        parts.append((x, t, r.uniform(0.5, 1)))
        t += d + r.uniform(0.03, 0.12)
    return mix(parts)


def koel(r):
    """Ku-oo, ku-oo, ku-oo, each a little higher than the last."""
    parts = []
    base = r.uniform(620, 700)
    t = 0.0
    for k in range(int(r.integers(3, 6))):
        f = base * (1.06 ** k)
        a = whistle(f, 0.13, r, 0.25, 0.0, 30, 0.02, 0.03)
        b = whistle(sweep(f * 1.12, f * 1.45, 0.32), 0.32, r, 0.25, 0.01, 6, 0.03, 0.1)
        parts += [(a, t, 0.7), (b, t + 0.15, 1.0)]
        t += r.uniform(0.75, 0.95)
    return mix(parts)


def crow(r):
    parts = []
    t = 0.0
    for k in range(int(r.integers(1, 4))):
        d = r.uniform(0.25, 0.4)
        f = sweep(r.uniform(650, 760), r.uniform(520, 600), d)
        x = saw(f, d) + 0.4 * white(d, r)
        x = peak(peak(bp(x, 400, 5000), 1300, 3, 10), 2400, 3, 6)
        x = x * env_lin(d, [(0, 0), (0.03, 1), (d * 0.6, 0.8), (d, 0)])
        parts.append((x, t, 1))
        t += d + r.uniform(0.15, 0.35)
    return mix(parts)


def parakeet(r):
    parts = []
    t = 0.0
    for k in range(int(r.integers(2, 6))):
        d = r.uniform(0.08, 0.18)
        f = r.uniform(2800, 3800) + 600 * sine(r.uniform(25, 45), d)
        x = sine(f, d) + 0.4 * sine(f * 2, d) + 0.15 * white(d, r)
        parts.append((x * env_hann(d), t, 1))
        t += d + r.uniform(0.04, 0.2)
    return mix(parts)


def sparrow(r):
    parts = []
    t = 0.0
    for k in range(int(r.integers(4, 10))):
        d = r.uniform(0.03, 0.07)
        f0 = r.uniform(3800, 5500)
        parts.append((whistle(sweep(f0, f0 * r.uniform(0.7, 1.2), d), d, r, 0.1, 0, 30, 0.004, 0.01), t, r.uniform(0.5, 1)))
        t += d + r.uniform(0.04, 0.15)
    return mix(parts)


def dove(r):
    """Spotted dove: a soft croo-croo-crooo."""
    parts = []
    t = 0.0
    for d in (0.18, 0.18, 0.42):
        f = r.uniform(440, 520)
        x = whistle(sweep(f * 1.05, f * 0.92, d), d, r, 0.4, 0.03, 18, 0.03, 0.08)
        parts.append((lp(x, 1500), t, 1))
        t += d + 0.1
    return mix(parts)


def owl(r):
    """Spotted owlet: a scolding chirr; or a barn owl's hiss... here a soft double hoot."""
    parts = []
    for k, at in enumerate((0, 0.5)):
        d = 0.35
        x = whistle(sweep(380, 340, d), d, r, 0.3, 0.01, 8, 0.04, 0.12)
        parts.append((lp(x, 1200), at, 1 if k == 0 else 0.8))
    return mix(parts)


def jackals(r):
    """A pack far off at night: wavering howls rising and breaking into yips, several voices at once."""
    s = 5.0
    out = np.zeros(n_of(s))
    for k in range(int(r.integers(3, 6))):
        d = r.uniform(1.5, 3.0)
        t = times(d)
        f0 = r.uniform(500, 700)
        f = f0 + r.uniform(300, 600) * np.sin(np.pi * t / d) + 40 * np.sin(2 * np.pi * r.uniform(5, 8) * t)
        x = sine(f, d) + 0.3 * sine(f * 2, d) + 0.1 * white(d, r)
        x = x * env_hann(d)
        yip = np.zeros(n_of(1.2))
        for j in range(6):
            y = whistle(sweep(1200, 800, 0.08), 0.08, r, 0.3)
            yip = mix([(yip, 0, 1), (y, j * 0.16 + r.uniform(0, 0.05), 1)], 1.2)
        voice = mix([(x, 0, 1), (yip, d * 0.8, 0.6)])
        out = mix([(out, 0, 1), (voice, r.uniform(0, 1.5), r.uniform(0.5, 1))], s)
    return lp(out, 3500)


def dog(r):
    parts = []
    t = 0.0
    for k in range(int(r.integers(2, 5))):
        d = r.uniform(0.12, 0.2)
        f = sweep(r.uniform(450, 600), r.uniform(300, 400), d)
        x = peak(bp(saw(f, d) + white(d, r) * 0.6, 300, 4000), 1000, 2, 8) * env_ad(d, 0.01, d / 3)
        parts.append((x, t, 1))
        t += d + r.uniform(0.15, 0.4)
    return lp(mix(parts), 2500)


def far(x, r, dist=1.0, seconds_extra=1.5):
    """Places a call at a distance: air takes the highs, a little ground echo and reverb."""
    x = lp(x, 9000 - 5000 * dist, 2)
    ir = room_ir(1.0 + seconds_extra, 0.9 + dist, r, bright=4000, dark=1000, stereo=False, early=[(0.05, 0.3), (0.12, 0.2)])
    return reverb(x, ir, 0.15 + 0.4 * dist)


BIRDS = {"bulbul": bulbul, "myna": myna, "koel": koel, "crow": crow, "parakeet": parakeet, "sparrow": sparrow, "dove": dove}

for name, fn in BIRDS.items():
    sound("bird_" + name, "ambience/birds", bus="Ambience", variants=4, level=-26, pitch=(0.94, 1.06), min=6, max=90,
          curve=1.0, priority=120)(lambda r, v, fn=fn: trim(far(fn(r), r, 0.15, 0.8), -55))

sound("owl", "ambience/night", bus="Ambience", variants=3, level=-27, min=8, max=120, priority=120)(
    lambda r, v: trim(far(owl(r), r, 0.3), -55))
sound("jackals", "ambience/night", bus="Ambience", variants=3, level=-26, min=60, max=800, curve=0.6, priority=110)(
    lambda r, v: trim(far(jackals(r), r, 0.9, 2.5), -55))
sound("dog_far", "ambience/night", bus="Ambience", variants=4, level=-27, min=40, max=500, curve=0.7, priority=115)(
    lambda r, v: trim(far(dog(r), r, 0.8), -55))


@sound("truck_horn_far", "ambience/distant", bus="Ambience", variants=3, level=-30, min=100, max=1500, curve=0.5, priority=115)
def truck_horn_far(r, v):
    """A truck on the highway below the campus: a multi-tone air horn, far away."""
    d = r.uniform(0.6, 1.4)
    f = r.uniform(280, 340)
    x = sum(saw(f * k, d) * g for k, g in ((1, 1), (1.26, 0.8), (1.5, 0.6)))
    x = x * env_lin(d, [(0, 0), (0.05, 1), (d - 0.1, 1), (d, 0)])
    return trim(far(lp(x, 2000), r, 1.0, 2.5), -55)


@sound("train_far", "ambience/distant", bus="Ambience", variants=2, level=-30, min=300, max=3000, curve=0.4, priority=118)
def train_far(r, v):
    s = 6.0
    d = 2.2
    x = (saw(330, d) + saw(415, d) * 0.8) * env_lin(d, [(0, 0), (0.2, 1), (d - 0.4, 1), (d, 0)])
    x2 = (saw(330, 1.0) + saw(415, 1.0) * 0.8) * env_hann(1.0)
    y = mix([(x, 0, 1), (x2, 2.6, 0.8)], s)
    return trim(far(lp(y, 1200), r, 1.0, 3.0), -55)


@sound("gust", "ambience/wind", bus="Ambience", variants=4, level=-24, spatial=False, stereo=True, priority=110)
def gust(r, v):
    s = r.uniform(3.0, 5.0)
    t = times(s)
    e = np.sin(np.pi * np.clip(t / s, 0, 1)) ** 2
    c = 400 + 1600 * e
    l = tv_lp(pink(s, r), c, 256) * e
    rr = tv_lp(pink(s, r), c * 0.9, 256) * np.roll(e, n_of(0.3))
    return np.stack([l, rr], 1)


# ---------------------------------------------------------------------------- beds

def wind_bed(r, s, strength=0.4, bright=900):
    t = times(s)
    g = 0.6 + 0.4 * np.clip(lp(r.standard_normal(len(t)), 0.4) * 40, -1, 1)
    left = tv_lp(pink(s, r), bright * (0.6 + 0.8 * g), 512) * g * strength
    right = tv_lp(pink(s, r), bright * (0.6 + 0.8 * g[::-1]), 512) * g[::-1] * strength
    return np.stack([left, right], 1)


def traffic_bed(r, s, gain=0.3):
    x = lp(brown(s, r), 250) * gain + bp(pink(s, r), 300, 900) * gain * 0.15
    y = lp(brown(s, r), 250) * gain + bp(pink(s, r), 300, 900) * gain * 0.15
    return np.stack([x, y], 1)


def scatter(r, s, makers, count, dist=(0.5, 1.0), gain=0.3):
    out = np.zeros((n_of(s), 2))
    for k in range(count):
        fn = makers[int(r.integers(0, len(makers)))]
        d = r.uniform(*dist)
        x = far(fn(r), r, d)
        x = to_stereo(to_mono(x), r.uniform(-0.9, 0.9))
        at = r.uniform(0, s - len(x) / SR - 0.1) if len(x) / SR < s - 0.2 else 0
        out = mix([(out, 0, 1), (x, at, gain * (1.2 - d))], s)
    return out


def crickets(r, s, voices=24, gain=0.3):
    out = np.zeros((n_of(s), 2))
    t = times(s)
    for k in range(voices):
        f = r.uniform(3800, 5200)
        trill = r.uniform(18, 35)
        burst = r.uniform(0.15, 0.6)
        period = r.uniform(0.5, 1.4)
        ph = r.uniform(0, period)
        on = ((t + ph) % period) < burst
        pulses = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * trill * t))
        x = sine(f, s) * on * pulses
        x = lp(x, 7000, 1)
        out += to_stereo(x, r.uniform(-1, 1)) * r.uniform(0.2, 1) * gain / voices * 3
    return out


def cicadas(r, s, gain=0.1):
    t = times(s)
    sw = 0.5 + 0.5 * np.sin(2 * np.pi * t / r.uniform(6, 10))
    x = bp(white(s, r), 5500, 7500) * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * 120 * t))) * sw
    return to_stereo(x, 0.3) * gain


BED_S = 40.0


@sound("bed_day", "ambience/beds", bus="Ambience", level=-27, spatial=False, stereo=True, loop=True, priority=5, reverb=0, quality=0.35)
def bed_day(r, v):
    s = BED_S + 2
    x = wind_bed(r, s, 0.5) + traffic_bed(r, s, 0.25) + cicadas(r, s, 0.04)
    x += scatter(r, s, [bulbul, sparrow, myna, dove], 26, (0.6, 1.0), 0.25)
    return loopify(x, 2.0)


@sound("bed_evening", "ambience/beds", bus="Ambience", level=-26, spatial=False, stereo=True, loop=True, priority=5, reverb=0, quality=0.35)
def bed_evening(r, v):
    s = BED_S + 2
    x = wind_bed(r, s, 0.4) + traffic_bed(r, s, 0.3)
    x += scatter(r, s, [myna, parakeet, crow, bulbul, sparrow], 50, (0.4, 1.0), 0.3)   # the evening roost chorus
    x += crickets(r, s, 10, 0.15)
    return loopify(x, 2.0)


@sound("bed_night", "ambience/beds", bus="Ambience", level=-27, spatial=False, stereo=True, loop=True, priority=5, reverb=0, quality=0.35)
def bed_night(r, v):
    s = BED_S + 2
    x = wind_bed(r, s, 0.25, 700) + traffic_bed(r, s, 0.15) + crickets(r, s, 36, 0.45)
    return loopify(x, 2.0)


@sound("bed_wind_high", "ambience/beds", bus="Ambience", level=-24, spatial=False, stereo=True, loop=True, priority=6, reverb=0, quality=0.35)
def bed_wind_high(r, v):
    """Up on the roofs and the hilltops: stronger, gustier wind with a whistle round edges."""
    s = BED_S + 2
    x = wind_bed(r, s, 1.0, 1400)
    t = times(s)
    g = 0.5 + 0.5 * np.clip(lp(r.standard_normal(len(t)), 0.3) * 50, -1, 1)
    whistle_ = tv_bp(white(s, r), 900 + 500 * g, 25, 512) * g * 0.4
    x += to_stereo(whistle_, 0.4)
    return loopify(x, 2.0)


@sound("bed_pines", "ambience/beds", bus="Ambience", level=-26, spatial=False, stereo=True, loop=True, priority=6, reverb=0, quality=0.35)
def bed_pines(r, v):
    """Wind through chir pine needles: a high, soft hiss that swells and falls."""
    s = BED_S + 2
    t = times(s)
    g = 0.4 + 0.6 * np.clip(lp(r.standard_normal(len(t)), 0.25) * 50, 0, 1)
    l = bp(white(s, r), 2000, 9000) * g * 0.6 + lp(pink(s, r), 400) * 0.2
    rr = bp(white(s, r), 2000, 9000) * np.roll(g, n_of(0.8)) * 0.6 + lp(pink(s, r), 400) * 0.2
    x = np.stack([l, rr], 1) + scatter(r, s, [bulbul, dove], 8, (0.5, 1.0), 0.2)
    return loopify(x, 2.0)


@sound("bed_indoor", "ambience/beds", bus="Ambience", level=-34, spatial=False, stereo=True, loop=True, priority=6, reverb=0, quality=0.35)
def bed_indoor(r, v):
    """Room tone: air in a closed space, mains hum from the lights, the outside far through the walls."""
    s = BED_S + 2
    hum = sine(100, s) * 0.06 + sine(200, s) * 0.02 + sine(50, s) * 0.03
    air = lp(pink(s, r), 600) * 0.5
    outside = lp(wind_bed(r, s, 0.4), 500)
    x = to_stereo(hum + air) * 0.7 + outside * 0.5
    x[:, 1] = np.roll(x[:, 1], 500)
    return loopify(x, 2.0)


@sound("fan", "ambience/indoor", bus="Ambience", level=-28, min=1.5, max=9, curve=1.5, loop=True, priority=100)
def ceiling_fan(r, v):
    """A hostel ceiling fan: the motor's hum and three blades swishing round (about 4 turns a second)."""
    blades = 3 * 4.0
    cycles = 48
    s = cycles / blades
    t = times(s)
    ph = (t * blades) % 1
    swish = bp(pink(s, r), 300, 2500) * (0.5 + 0.5 * np.exp(-ph / 0.3))
    motor = sine(100, s) * 0.05 + sine(150, s) * 0.02 + grains(s, r, 30, 1000, 4000, 0.002) * 0.05
    return swish * 0.6 + motor


# ---------------------------------------------------------------------------- point sources

@sound("water_trickle", "ambience/points", bus="Ambience", level=-27, min=3, max=26, curve=1.3, loop=True, priority=100)
def water_trickle(r, v):
    s = 6.0
    x = bp(pink(s + 1, r), 400, 5000) * 0.4
    for k in range(260):
        bs = 0.04
        f0 = r.uniform(700, 2600)
        x = mix([(x, 0, 1), (sine(sweep(f0, f0 * r.uniform(1.3, 2.0), bs), bs) * env_exp(bs, 0.01), r.uniform(0, s), r.uniform(0.05, 0.25))], s + 1)
    return loopify(x, 1.0)


@sound("flag_flap", "ambience/points", bus="Ambience", level=-30, min=2, max=18, curve=1.4, loop=True, priority=110)
def flag_flap(r, v):
    """Feather flags: fabric flutter that comes and goes with the wind, and a creak of the pole."""
    s = 8.0
    t = times(s + 1)
    gust = 0.3 + 0.7 * np.clip(lp(r.standard_normal(len(t)), 0.5) * 30 + 0.4, 0, 1)
    flut = np.abs(np.sin(2 * np.pi * (8 + 6 * gust) * t)) ** 3
    x = bp(white(s + 1, r), 300, 3500) * flut * gust
    x += grains(s + 1, r, 80, 1000, 5000, 0.002) * gust * 0.4
    return loopify(x, 1.0)


@sound("transformer_hum", "ambience/points", bus="Ambience", level=-27, min=4, max=45, curve=1.2, loop=True, priority=100)
def transformer_hum(r, v):
    """The power substation: mains hum at 100 Hz and its harmonics, with a faint crackle."""
    s = 2.0
    x = sine(100, s) * 1.0 + sine(200, s) * 0.5 + sine(300, s) * 0.35 + sine(400, s) * 0.15 + sine(600, s) * 0.08
    x += grains(s, r, 15, 3000, 9000, 0.0008) * 0.1
    return x


@sound("kitchen_fan", "ambience/points", bus="Ambience", level=-27, min=3, max=35, curve=1.2, loop=True, priority=100)
def kitchen_fan(r, v):
    """The mess kitchen's big exhaust fan on the wall, and the hiss of its duct."""
    blades = 6 * 15.0
    cycles = 270
    s = cycles / blades
    t = times(s)
    ph = (t * blades) % 1
    x = lp(pink(s, r), 1500) * (0.7 + 0.3 * np.exp(-ph / 0.2)) + sine(blades, s) * 0.2 + sine(50, s) * 0.08
    return x


@sound("fridge_hum", "ambience/points", bus="Ambience", level=-31, min=1.5, max=12, curve=1.4, loop=True, priority=110)
def fridge_hum(r, v):
    """A shop's drinks fridge: compressor hum and a fan."""
    s = 3.0
    return sine(50, s) * 0.3 + sine(100, s) * 0.6 + sine(150, s) * 0.2 + bp(pink(s, r), 400, 2000) * 0.2


@sound("lamp_buzz", "ambience/points", bus="Ambience", level=-34, min=1.5, max=10, curve=1.5, loop=True, priority=115)
def lamp_buzz(r, v):
    """A street lamp at night: the driver's faint buzz and insects ticking against the glass."""
    s = 5.0
    x = sine(100, s) * 0.05 + sine(300, s) * 0.02 + hp(sine(100, s) ** 9, 2000) * 0.05
    ticks = np.zeros(n_of(s))
    for k in range(25):
        ticks = mix([(ticks, 0, 1), (bp(white(0.01, r), 3000, 9000) * env_exp(0.01, 0.001), r.uniform(0, s - 0.1), r.uniform(0.1, 0.4))], s)
    # a moth's wing flutter now and then
    flut = bp(white(s, r), 100, 600) * (np.abs(np.sin(2 * np.pi * 40 * times(s))) ** 4) * env_lin(s, [(0, 0), (1.0, 0), (1.3, 0.3), (2.0, 0.3), (2.3, 0), (s, 0)])
    return x + ticks + flut * 0.5


@sound("crickets_spot", "ambience/points", bus="Ambience", level=-30, min=2, max=16, curve=1.4, loop=True, priority=110)
def crickets_spot(r, v):
    s = 4.0
    t = times(s)
    f = r.uniform(4300, 4900)
    on = (t % 0.9) < 0.45
    x = sine(f, s) * on * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 28 * t)))
    return lp(x, 7000, 1)


# ---------------------------------------------------------------------------- the haze

@sound("haze_wall", "ambience/haze", bus="Ambience", level=-22, min=6, max=120, curve=1.0, loop=True, priority=15)
def haze_wall(r, v):
    """The haze wall up close: a deep, uneasy drone, a churning roar, and a static sizzle."""
    s = 8.0
    t = times(s + 1)
    drone = sine(55 + 1.5 * np.sin(2 * np.pi * 0.11 * t), s + 1) * 0.5 + sine(82.4 * 1.003, s + 1) * 0.3 + sine(110 * 0.997, s + 1) * 0.2
    churn = tv_lp(brown(s + 1, r), 200 + 300 * (0.5 + 0.5 * np.sin(2 * np.pi * 0.23 * t)), 512) * 0.9
    sizzle = grains(s + 1, r, 900, 3000, 12000, 0.0006) * (0.5 + 0.5 * np.sin(2 * np.pi * 0.37 * t)) * 0.5
    return loopify(drone + churn + sizzle, 1.0)


@sound("haze_inside", "ambience/haze", bus="Ambience", level=-24, spatial=False, stereo=True, loop=True, priority=4, reverb=0)
def haze_inside(r, v):
    """In the haze: the world goes thick, a choking roar all round and a thin high ring."""
    s = 8.0
    t = times(s + 1)
    l = tv_lp(brown(s + 1, r), 300 + 200 * np.sin(2 * np.pi * 0.2 * t), 512)
    rr = tv_lp(brown(s + 1, r), 300 + 200 * np.cos(2 * np.pi * 0.17 * t), 512)
    ring = sine(3150, s + 1) * 0.02 * (0.6 + 0.4 * np.sin(2 * np.pi * 0.5 * t))
    pulse = sine(45, s + 1) * 0.3 * (0.5 + 0.5 * np.sin(2 * np.pi * 0.8 * t)) ** 4
    return loopify(np.stack([l + ring + pulse, rr + ring + pulse], 1), 1.0)


@sound("haze_tick", "ambience/haze", bus="Interface", level=-20, spatial=False, stereo=True, priority=8, reverb=0)
def haze_tick(r, v):
    """A tick of haze damage: a dull burning thud and a hiss on the skin."""
    s = 0.45
    x = sine(glide_exp(90, 45, s, 0.04), s) * env_exp(s, 0.08, 0.002) + bp(white(s, r), 2500, 8000) * env_ad(s, 0.01, 0.06) * 0.4
    return to_stereo(x)


@sound("haze_warning", "ambience/haze", bus="Interface", level=-18, spatial=False, stereo=True, priority=2, reverb=0)
def haze_warning(r, v):
    """The haze is about to move: a low, rising siren-like swell over a drum hit."""
    s = 3.2
    t = times(s)
    f = 110 * (1 + 0.5 * np.clip(t / 1.8, 0, 1))
    tone = (saw(f, s) * 0.5 + saw(f * 1.5, s) * 0.3 + saw(f * 2.005, s) * 0.2)
    tone = tv_lp(tone, 300 + 2500 * np.clip(t / 1.5, 0, 1), 256) * env_lin(s, [(0, 0), (0.4, 0.8), (1.8, 1), (s, 0)])
    hit = sine(glide_exp(120, 40, s, 0.06), s) * env_exp(s, 0.35, 0.002) * 1.5
    x = to_stereo(tone * 0.7 + hit)
    x = chorus(to_mono(x), r, 0.003, 0.5)
    return reverb(x, hall(r, 2.0), 0.35)


@sound("haze_moving", "ambience/haze", bus="Ambience", level=-21, spatial=False, stereo=True, priority=3, reverb=0)
def haze_moving(r, v):
    """The haze starting to move: a deep surge, like a long wave breaking far off."""
    s = 6.0
    t = times(s)
    e = env_lin(s, [(0, 0), (1.5, 1), (3.5, 0.8), (s, 0)])
    l = tv_lp(brown(s, r), 150 + 500 * e, 256) * e
    rr = tv_lp(brown(s, r), 150 + 500 * e, 256) * e
    sub = sine(38, s) * e * 0.5
    return np.stack([l + sub, rr + sub], 1)


@sound("countdown", "ambience/haze", bus="Interface", variants=1, level=-22, spatial=False, stereo=True, priority=10, reverb=0)
def countdown(r, v):
    s = 0.25
    x = sine(1320, s) * env_exp(s, 0.05, 0.002) + sine(2640, s) * env_exp(s, 0.03, 0.002) * 0.3
    return to_stereo(x)


@sound("countdown_last", "ambience/haze", bus="Interface", variants=1, level=-20, spatial=False, stereo=True, priority=10, reverb=0)
def countdown_last(r, v):
    s = 0.6
    x = sine(880, s) * env_exp(s, 0.2, 0.002) + sine(1760, s) * env_exp(s, 0.12, 0.002) * 0.4
    return to_stereo(x)
