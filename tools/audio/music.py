"""
The score, composed and played in code: an original soundtrack in a Hindustani-flavoured style for a
campus in the Jammu foothills. Instruments are synthesised: a tanpura drone (with the buzzing jawari
bridge), a santoor (hammered strings), a bansuri (bamboo flute, with glides between notes), tabla,
dhol, strings, pads and bass.

Pieces (all in D, Sa = D):
  music_menu     the front menu, in a Yaman-like mode (raised fourth), hopeful and open
  music_bus      the sky bus before the jump: airy, anticipating
  music_explore  the match's quiet layer, in a Bhairavi-like mode (flat second, third, sixth, seventh)
  music_combat   the match's fight layer: same tempo, length and mode as explore, so MusicDirector can
                 play both in step and raise this one with the fighting
  music_final    the last circles: faster and tense
  music_victory  sting when you win
  music_defeat   sting when you are out

Melodies are written in sargam: S r R g G m M P d D n N (upper case natural, lower case flat, M the
raised Ma), ' for the octave above and . for the one below, :n for a length in beats, - to hold, _ rest.
"""
import numpy as np
from dsp import *
from bank import sound

SA = 146.83  # D3
SEMI = {"S": 0, "r": 1, "R": 2, "g": 3, "G": 4, "m": 5, "M": 6, "P": 7, "d": 8, "D": 9, "n": 10, "N": 11}


def freq(semis, octave=0):
    return SA * 2 ** (semis / 12.0 + octave)


def parse(text, octave=1):
    """'N. R G:2 - _ P' -> list of (semitones from Sa or None for rest, beats)."""
    out = []
    for tok in text.split():
        if tok == "-":
            if out:
                out[-1] = (out[-1][0], out[-1][1] + 1)
            continue
        beats = 1.0
        if ":" in tok:
            tok, b = tok.split(":")
            beats = float(b)
        if tok == "_":
            out.append((None, beats))
            continue
        name = tok[0]
        o = octave + tok.count("'") - tok.count(".")
        out.append((SEMI[name] + 12 * o, beats))
    return out


# ---------------------------------------------------------------------------- instruments

def harmonics_tone(f, seconds, amps, decays, r, inharm=0.0, detune_cents=0.0):
    t = times(seconds)
    x = np.zeros_like(t)
    ff = f * 2 ** (detune_cents / 1200)
    for k, (a, d) in enumerate(zip(amps, decays), start=1):
        fk = ff * k * np.sqrt(1 + inharm * k * k)
        if fk > SR * 0.45:
            break
        x += a * np.sin(2 * np.pi * fk * t + r.uniform(0, 6.28)) * np.exp(-t / d)
    return x


def santoor(f, seconds, r, vel=1.0):
    n = 14
    amps = [vel ** (0.5 + k * 0.08) / k ** 0.85 for k in range(1, n + 1)]
    decays = [2.4 / (1 + 0.32 * k) for k in range(1, n + 1)]
    x = np.zeros(n_of(seconds))
    for c in (-2.5, 0.0, 2.8):            # three strings per course, slightly apart
        x += harmonics_tone(f, seconds, amps, decays, r, 0.00012, c)
    x += hp(white(seconds, r), 2500) * env_exp(seconds, 0.003) * 0.4 * vel       # the hammer
    return x * env_lin(seconds, [(0, 0), (0.002, 1), (seconds - 0.05, 1), (seconds, 0)]) * 0.35


def tanpura(f, seconds, r):
    """One pluck: rich harmonics whose bright band (the jawari's buzz) sweeps slowly down."""
    t = times(seconds)
    x = np.zeros_like(t)
    centre = 4 + 16 * np.exp(-t / 1.4)
    for k in range(1, 26):
        fk = f * k
        if fk > SR * 0.45:
            break
        base = 1.0 / k ** 0.7
        jaw = 1 + 2.0 * np.exp(-((k - centre) ** 2) / 10.0)
        x += base * jaw * np.sin(2 * np.pi * fk * t + r.uniform(0, 6.28)) * np.exp(-t / (5.0 / (1 + 0.05 * k)))
    return x * env_lin(seconds, [(0, 0), (0.01, 1), (seconds - 0.4, 0.8), (seconds, 0)]) * 0.08


def bansuri(notes, beat, r, start_octave=1, glide=0.06, vib=0.006, breath=0.12):
    """A legato phrase on the flute: one continuous breath that glides between notes."""
    total = sum(b for _, b in notes) * beat
    n = n_of(total + 0.3)
    fcurve = np.zeros(n)
    amp = np.zeros(n)
    i = 0
    prev = None
    for semis, b in notes:
        k = n_of(b * beat)
        if semis is None:
            amp[i: i + k] = 0
            if prev is not None:
                fcurve[i: i + k] = prev
            i += k
            continue
        f = freq(semis)
        if prev is None:
            fcurve[i: i + k] = f
        else:
            g = min(k, n_of(glide))
            fcurve[i: i + g] = np.geomspace(prev, f, g)
            fcurve[i + g: i + k] = f
        amp[i: i + k] = 1
        prev = f
        i += k
    fcurve[i:] = prev if prev else SA
    # Smooth breath: onsets swell, rests let go.
    amp = one_pole(amp, 12)
    t = np.arange(n) / SR
    vibrato = 1 + vib * np.sin(2 * np.pi * 5.2 * t) * np.clip(one_pole(amp, 1.5) * 1.2, 0, 1)
    fc = fcurve * vibrato
    ph = np.cumsum(fc) / SR
    tone = np.sin(2 * np.pi * ph) + 0.22 * np.sin(4 * np.pi * ph) + 0.06 * np.sin(6 * np.pi * ph)
    air = bp(r.standard_normal(n), 800, 6000) * breath
    x = (tone + air) * amp
    return lp(x, 7000) * 0.5


def pad(freqs, seconds, r, bright=1400, attack=1.2, release=1.5):
    x = np.zeros(n_of(seconds))
    for f in freqs:
        for d in (-7, -2, 3, 8):
            x += saw(f * 2 ** (d / 1200), seconds, r.uniform(0, 1))
    x = lp(x, bright, 2)
    x *= env_lin(seconds, [(0, 0), (attack, 1), (max(attack, seconds - release), 1), (seconds, 0)])
    return x / (len(freqs) * 4) * 0.8


def bass(f, seconds, r, bright=500):
    x = sine(f, seconds) + 0.35 * lp(saw(f, seconds), bright)
    return x * env_lin(seconds, [(0, 0), (0.008, 1), (seconds * 0.7, 0.7), (seconds, 0)]) * 0.5


def strings(f, seconds, r, bright=2600, stac=True):
    x = np.zeros(n_of(seconds))
    for d in (-9, -3, 4, 10):
        x += saw(f * 2 ** (d / 1200), seconds, r.uniform(0, 1))
    x = lp(x, bright, 2)
    if stac:
        x *= env_lin(seconds, [(0, 0), (0.01, 1), (min(seconds, 0.12), 0.5), (seconds, 0)])
    else:
        x *= env_lin(seconds, [(0, 0), (0.25, 1), (seconds - 0.2, 1), (seconds, 0)])
    return x * 0.1


def tabla_na(f, r, vel=1.0, s=0.9):
    x = modal([f, f * 2, f * 3, f * 4.02, f * 5.03], [0.35, 0.18, 0.12, 0.07, 0.05], [1, 0.6, 0.45, 0.25, 0.15], s, r)
    x += bp(white(s, r), 2000, 8000) * env_exp(s, 0.004) * 0.6
    return x * vel * 0.4


def tabla_tin(f, r, vel=1.0, s=0.6):
    x = modal([f, f * 2, f * 3], [0.18, 0.1, 0.06], [1, 0.4, 0.2], s, r)
    return (x + bp(white(s, r), 2500, 9000) * env_exp(s, 0.003) * 0.3) * vel * 0.3


def bayan_ge(r, vel=1.0, s=0.9):
    t = times(s)
    f = 78 + 30 * (1 - np.exp(-t / 0.15))
    x = sine(f, s) * env_exp(s, 0.4, 0.003) + lp(white(s, r), 800) * env_exp(s, 0.01) * 0.4
    return x * vel * 0.8


def bayan_ka(r, vel=1.0, s=0.15):
    return (lp(white(s, r), 1800) * env_exp(s, 0.015) + sine(120, s) * env_exp(s, 0.03) * 0.5) * vel * 0.6


def dhol_low(r, vel=1.0, s=0.8):
    x = sine(glide_exp(105, 58, s, 0.05), s) * env_exp(s, 0.28, 0.002) * 1.2
    x += lp(white(s, r), 1200) * env_exp(s, 0.012) * 0.6
    return saturate(x * vel, 1.4) * 0.8


def dhol_high(r, vel=1.0, s=0.3):
    x = bp(white(s, r), 700, 6000) * env_exp(s, 0.02) * 0.8 + sine(320, s) * env_exp(s, 0.06) * 0.5
    return x * vel * 0.5


def taiko(r, vel=1.0, s=1.8):
    x = sine(glide_exp(80, 42, s, 0.08), s) * env_exp(s, 0.6, 0.004) * 1.4
    x += lp(white(s, r), 900) * env_exp(s, 0.03) * 0.7
    return saturate(x * vel, 1.3) * 0.9


def shaker(r, vel=1.0, s=0.08):
    return bp(white(s, r), 5000, 12000) * env_ad(s, 0.008, 0.02) * vel * 0.25


def chime(f, r, s=2.5):
    return modal([f, f * 2.76, f * 5.4], [1.2, 0.6, 0.3], [1, 0.3, 0.12], s, r) * 0.15


# ---------------------------------------------------------------------------- arrangement

class Score:
    def __init__(self, bpm, bars, beats_per_bar=4, tail=4.0):
        self.beat = 60.0 / bpm
        self.length = bars * beats_per_bar * self.beat
        self.bpb = beats_per_bar
        self.tail = tail
        self.buses = {}

    def at(self, bar, beat=0.0):
        return (bar * self.bpb + beat) * self.beat

    def add(self, bus, x, t, gain=1.0, pan=0.0):
        if bus not in self.buses:
            self.buses[bus] = np.zeros((n_of(self.length + self.tail), 2))
        buf = self.buses[bus]
        x = to_stereo(x, pan) if x.ndim == 1 else x
        i = max(0, int(round(t * SR)))
        if i >= len(buf):
            return
        seg = x[: len(buf) - i] * gain
        buf[i: i + len(seg)] += seg

    def render(self, r, sends, loop=True, master_gain=1.0):
        """sends: bus -> reverb amount. Loops fold the tail back onto the start so they repeat seamlessly."""
        ir = hall(r, 2.4, 3.5)
        n = n_of(self.length + self.tail)
        out = np.zeros((n, 2))
        wet = np.zeros((n, 2))
        for bus, buf in self.buses.items():
            out += buf
            if sends.get(bus, 0) > 0:
                wet += buf * sends[bus]
        if np.any(wet):
            out += convolve(wet, ir)[:n] * 0.9
        out = compress(out, -16, 2.5, 0.01, 0.2) * master_gain
        if loop:
            L = n_of(self.length)
            head = out[:L].copy()
            spill = out[L:]
            head[: len(spill)] += spill
            return head
        return trim(out, -70)


def play_line(sc, bus, inst, notes, bar, r, beat_offset=0.0, gain=1.0, pan=0.0, vel=1.0, ring=1.6):
    t = sc.at(bar, beat_offset)
    for semis, b in notes:
        if semis is not None:
            sc.add(bus, inst(freq(semis), max(ring, b * sc.beat * 1.2), r, vel * r.uniform(0.85, 1.0)), t, gain, pan)
        t += b * sc.beat


def drone(sc, r, bars, start=0, gain=1.0, pa=True, pluck_beats=1.0):
    """The tanpura's cycle: Pa (or Ma), Sa, Sa, low Sa, round and round."""
    strings_ = [freq(7, 0) if pa else freq(5, 0), freq(12, 0), freq(12, 0), freq(0, 0)]
    t = sc.at(start)
    end = sc.at(start + bars)
    k = 0
    while t < end:
        f = strings_[k % 4]
        sc.add("drone", tanpura(f, 4.5, r), t + r.uniform(-0.01, 0.01), gain, [-0.3, 0.2, 0.3, -0.1][k % 4])
        t += pluck_beats * sc.beat * (1.6 if k % 4 == 3 else 1.0)
        k += 1


KEHARWA = [("dha", 0), ("ge", 1), ("na", 2), ("tin", 3), ("na", 4), ("ka", 5), ("dhi", 6), ("na", 7)]


def tabla(sc, r, bar0, bars, gain=1.0, eighths=True, fill_every=4):
    step = 0.5 if eighths else 1.0
    for b in range(bars):
        fill = fill_every and (b % fill_every == fill_every - 1)
        for bol, k in KEHARWA:
            t = sc.at(bar0 + b, k * step)
            v = r.uniform(0.75, 1.0)
            if bol in ("dha", "dhi"):
                sc.add("drums", tabla_na(SA * 2, r, v) + bayan_ge(r, v), t, gain)
            elif bol == "ge":
                sc.add("drums", bayan_ge(r, v * 0.8), t, gain)
            elif bol == "na":
                sc.add("drums", tabla_na(SA * 2, r, v * 0.8), t, gain, 0.1)
            elif bol == "tin":
                sc.add("drums", tabla_tin(SA * 2, r, v), t, gain, 0.1)
            elif bol == "ka":
                sc.add("drums", bayan_ka(r, v), t, gain, -0.1)
        if fill:
            for j in range(4):
                sc.add("drums", tabla_na(SA * 2, r, 0.6 + 0.1 * j), sc.at(bar0 + b, 3 + j * 0.25), gain * 0.8, 0.1)


# ---------------------------------------------------------------------------- pieces

# Yaman-like: S R G M P D N (raised Ma).
YAMAN_A = "N. R G:2 R G M P:2 R:2 S:2 _:2"
YAMAN_B = "G M D N S':2 - N D P:2 M G R:2 S:2 _:2"
YAMAN_C = "P M G R G M D N R':2 S':2 N D P:2 _:2"
YAMAN_D = "M D N S' R' G':2 R' S' N:2 D P M G R:2 S:4"


@sound("music_menu", "music", bus="Music", level=-19, spatial=False, stereo=True, loop=True, priority=0, reverb=0, quality=0.55)
def music_menu(r, v):
    sc = Score(84, 32)
    drone(sc, r, 32, 0, 1.0, pluck_beats=1.0)
    # Pads on the drone and a slow lift through the chords of the mode.
    chords = [[0, 7, 16], [2, 9, 18], [4, 11, 19], [0, 7, 14]]
    for b in range(0, 32, 4):
        ch = chords[(b // 4) % 4]
        sc.add("pad", pad([freq(c, -1) for c in ch], sc.at(4) + 1.0, r, 1200 if b < 16 else 1800), sc.at(b), 0.9)
    # Santoor arpeggios, the instrument's shimmering tremolo patterns.
    arp = parse("S P S' G' P S' G' S'", 1)
    for b in range(0, 32):
        if 8 <= b < 12 or b >= 28:
            continue
        play_line(sc, "keys", santoor, [(s - 12 if b < 4 else s - 12, 0.5) for s, _ in arp], b, r, gain=0.55, pan=0.25, vel=0.6 if b < 4 else 0.8)
    # The flute melody.
    melody = [YAMAN_A, YAMAN_B, YAMAN_C, YAMAN_D]
    for i, m in enumerate(melody):
        sc.add("lead", bansuri(parse(m, 1), sc.beat, r), sc.at(4 + i * 4), 0.75, -0.1)
    for i, m in enumerate([YAMAN_C, YAMAN_D]):
        play_line(sc, "keys", santoor, parse(m, 1), 20 + i * 4, r, gain=0.7, pan=-0.15)
    sc.add("lead", bansuri(parse(YAMAN_B, 1), sc.beat, r), sc.at(24), 0.7, 0.1)
    # Tabla from bar 8, resting in the breakdown.
    tabla(sc, r, 8, 20, 0.8)
    # Bass on the roots.
    for b in range(8, 28):
        root = chords[(b // 4) % 4][0]
        sc.add("bass", bass(freq(root, -1), sc.beat * 3.5, r), sc.at(b), 0.8)
    return sc.render(r, {"drone": 0.3, "pad": 0.4, "keys": 0.45, "lead": 0.5, "drums": 0.15, "bass": 0.0})


@sound("music_bus", "music", bus="Music", level=-21, spatial=False, stereo=True, loop=True, priority=0, reverb=0, quality=0.55)
def music_bus(r, v):
    sc = Score(90, 16)
    drone(sc, r, 16, 0, 0.8, pluck_beats=1.5)
    for b in range(0, 16, 2):
        root = [0, 2, 4, 2][(b // 2) % 4]
        sc.add("pad", pad([freq(root, -1), freq(root + 7, -1), freq(root + 16, -1)], sc.at(2) + 0.8, r, 900 + b * 80), sc.at(b), 1.0)
    # Wind chimes of santoor high up, and a heartbeat pulse below.
    for b in range(16):
        for k in range(4):
            if r.random() < 0.55:
                s = [12, 16, 19, 21, 23, 24, 26][int(r.integers(0, 7))]
                sc.add("keys", santoor(freq(s, 1), 1.8, r, 0.45), sc.at(b, k + r.choice([0, 0.5])), 0.35, r.uniform(-0.7, 0.7))
        sc.add("drums", dhol_low(r, 0.5), sc.at(b, 0), 0.6)
        sc.add("drums", dhol_low(r, 0.35), sc.at(b, 0.4), 0.6)
        if b >= 8:
            for k in range(8):
                sc.add("drums", shaker(r, 0.6 + 0.4 * (k % 2 == 0)), sc.at(b, k * 0.5), 0.5, 0.3)
    sc.add("lead", bansuri(parse("P:3 M G:2 R G:2 M P:4 _:4", 1), sc.beat, r), sc.at(8), 0.55)
    return sc.render(r, {"drone": 0.3, "pad": 0.45, "keys": 0.6, "lead": 0.55, "drums": 0.1})


# Bhairavi-like: S r g m P d n.
BHAIRAVI_1 = "P:2 d P m g:2 r S:4 _:8"
BHAIRAVI_2 = "S r g m:2 P d n:2 d P:2 _:2 m g r S:4"
EXPLORE_BARS = 24
EXPLORE_BPM = 96


def explore_score(r):
    sc = Score(EXPLORE_BPM, EXPLORE_BARS)
    drone(sc, r, EXPLORE_BARS, 0, 0.9, pa=True, pluck_beats=1.0)
    chords = [[0, 7, 15], [-4, 3, 12], [1, 8, 13], [0, 7, 15], [-2, 5, 10], [0, 7, 12]]
    for i, b in enumerate(range(0, EXPLORE_BARS, 4)):
        ch = chords[i % len(chords)]
        sc.add("pad", pad([freq(c, -1) for c in ch], sc.at(4) + 1.0, r, 800), sc.at(b), 0.9)
    for b in (4, 16):
        play_line(sc, "keys", santoor, parse(BHAIRAVI_1, 1), b, r, gain=0.55, pan=0.3, vel=0.7)
    sc.add("lead", bansuri(parse(BHAIRAVI_2, 1), sc.beat, r, breath=0.18), sc.at(10), 0.5, -0.2)
    for b in range(EXPLORE_BARS):
        sc.add("drums", dhol_low(r, 0.35), sc.at(b, 0), 0.5)
    return sc, chords


@sound("music_explore", "music", bus="Music", level=-24, spatial=False, stereo=True, loop=True, priority=0, reverb=0, quality=0.5)
def music_explore(r, v):
    sc, _ = explore_score(r)
    return sc.render(r, {"drone": 0.3, "pad": 0.45, "keys": 0.5, "lead": 0.55, "drums": 0.1})


@sound("music_combat", "music", bus="Music", level=-20, spatial=False, stereo=True, loop=True, priority=0, reverb=0, quality=0.5)
def music_combat(r, v):
    """Only the fight layer: drums, bass ostinato and stabbing strings (played over explore)."""
    sc = Score(EXPLORE_BPM, EXPLORE_BARS)
    _, chords = explore_score(np.random.default_rng(1))
    osti = [0, 0, 12, 0, 1, 0, 10, 0]
    for b in range(EXPLORE_BARS):
        ch = chords[(b // 4) % len(chords)]
        root = ch[0]
        for k in range(8):
            sc.add("bass", bass(freq(root + osti[k], -2), sc.beat * 0.45, r, 900), sc.at(b, k * 0.5), 0.9)
        # dhol: the bhangra-like kick-and-slap groove, heavier in the second half
        for k, v_ in ((0, 1.0), (1.5, 0.7), (2, 0.9), (3, 0.6), (3.5, 0.8)):
            sc.add("drums", dhol_low(r, v_), sc.at(b, k), 0.9)
        for k in (1, 3):
            sc.add("drums", dhol_high(r, 1.0), sc.at(b, k), 0.8, 0.15)
        for k in range(16):
            sc.add("drums", shaker(r, 0.5 + 0.5 * (k % 4 == 2)), sc.at(b, k * 0.25), 0.6, -0.3)
        if b % 2 == 1:
            for k in (0, 0.75, 1.5):
                sc.add("strings", strings(freq(root + 12, -1), 0.3, r), sc.at(b, 2 + k), 1.0, 0.2)
                sc.add("strings", strings(freq(root + 19, -1), 0.3, r), sc.at(b, 2 + k), 0.8, -0.2)
        if b % 4 == 3:
            sc.add("drums", taiko(r, 1.0), sc.at(b, 3.5), 0.9)
    return sc.render(r, {"drums": 0.12, "strings": 0.35, "bass": 0.0})


@sound("music_final", "music", bus="Music", level=-19, spatial=False, stereo=True, loop=True, priority=0, reverb=0, quality=0.55)
def music_final(r, v):
    """The last circles: phrygian-dominant (S r G m P d n), driving sixteenths and a crying flute."""
    sc = Score(112, 24)
    drone(sc, r, 24, 0, 0.7, pluck_beats=0.75)
    prog = [0, 0, 1, 0, -2, -4]
    for b in range(24):
        root = prog[(b // 4) % len(prog)]
        for k in range(16):
            semis = [0, 12, 7, 12][k % 4] + root
            sc.add("bass", bass(freq(semis, -2), sc.beat * 0.22, r, 1200), sc.at(b, k * 0.25), 0.7)
        sc.add("drums", taiko(r, 1.0), sc.at(b, 0), 1.0)
        sc.add("drums", taiko(r, 0.6), sc.at(b, 2.5), 0.8)
        for k in range(8):
            sc.add("drums", dhol_high(r, 0.6 + 0.4 * (k % 2 == 1)), sc.at(b, k * 0.5), 0.7, 0.1)
        for k in range(16):
            sc.add("drums", shaker(r, 0.6), sc.at(b, k * 0.25), 0.6, -0.3)
        sc.add("strings", strings(freq(root + 12, -1), sc.beat * 4, r, 3000, stac=False), sc.at(b), 0.8, 0.25)
        sc.add("strings", strings(freq(root + 16, -1), sc.beat * 4, r, 3000, stac=False), sc.at(b), 0.6, -0.25)
        if b % 4 == 3:
            for k in range(8):
                sc.add("drums", tabla_na(SA * 2, r, 0.5 + k * 0.06), sc.at(b, 2 + k * 0.25), 0.8)
    sc.add("lead", bansuri(parse("S':3 r' S' n d:2 P:2 _:2 G m P d:2 n S':4 _:2", 1), sc.beat, r, vib=0.009), sc.at(8), 0.6)
    sc.add("lead", bansuri(parse("d:2 P G m:2 r S:4 _:2 r G m P:2 d P:4", 1), sc.beat, r, vib=0.009), sc.at(16), 0.6)
    return sc.render(r, {"drone": 0.25, "strings": 0.4, "lead": 0.5, "drums": 0.1, "bass": 0.0})


@sound("victory", "music", bus="Music", level=-17, spatial=False, stereo=True, priority=0, reverb=0, quality=0.55)
def victory(r, v):
    sc = Score(96, 5, tail=4.0)
    for k in range(12):                                   # a tabla roll into the hit
        sc.add("drums", tabla_na(SA * 2, r, 0.4 + k * 0.05), sc.at(0, k * 0.125 + 2), 0.9)
    sc.add("drums", taiko(r, 1.2), sc.at(1), 1.0)
    sc.add("pad", pad([freq(0, -1), freq(7, -1), freq(16, -1), freq(23, -1)], sc.beat * 16, r, 2400, 0.1, 3.0), sc.at(1), 1.2)
    sc.add("strings", strings(freq(12, -1), sc.beat * 14, r, 3500, stac=False), sc.at(1), 1.2)
    sc.add("strings", strings(freq(19, -1), sc.beat * 14, r, 3500, stac=False), sc.at(1), 1.0)
    play_line(sc, "keys", santoor, parse("S P S' G' P' G' S' P", 1), 1, r, gain=0.8, vel=0.9)
    sc.add("lead", bansuri(parse("G M D N S':6", 1), sc.beat, r), sc.at(2), 0.8)
    for b in (1, 3):
        drone(sc, r, 2, b, 0.8)
    sc.add("keys", chime(freq(24, 1), r), sc.at(4), 0.6)
    return sc.render(r, {"drums": 0.2, "pad": 0.5, "strings": 0.5, "keys": 0.5, "lead": 0.5, "drone": 0.3}, loop=False)


@sound("music_defeat", "music", bus="Music", level=-20, spatial=False, stereo=True, priority=0, reverb=0, quality=0.55)
def music_defeat(r, v):
    sc = Score(70, 4, tail=4.0)
    sc.add("pad", pad([freq(0, -1), freq(3, -1), freq(8, -1)], sc.beat * 12, r, 900, 0.5, 4.0), sc.at(0), 1.0)
    sc.add("lead", bansuri(parse("d:2 P m g:2 r S:5", 1), sc.beat, r, breath=0.2), sc.at(0, 1), 0.8)
    sc.add("drums", taiko(r, 0.6), sc.at(0), 0.8)
    drone(sc, r, 3, 0, 0.7)
    return sc.render(r, {"pad": 0.5, "lead": 0.6, "drums": 0.2, "drone": 0.3}, loop=False)
