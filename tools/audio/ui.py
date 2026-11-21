"""
Menu and HUD sounds: soft wooden-and-glass taps for the menus (a nod to the santoor in the music),
crisp hit markers, the kill confirm, armour breaking, the heartbeat at low health, the map, markers,
notices and the lobby.
"""
import numpy as np
from dsp import *
from bank import sound


def ui(name, level=-24, variants=1, priority=40):
    return sound(name, "ui", bus="Interface", variants=variants, level=level, spatial=False, stereo=True,
                 pitch=(0.99, 1.01), priority=priority, reverb=0)


def tap(f, seconds=0.12, r=None, bright=1.0):
    """A small struck bar: a fundamental, a bright partial and a noise tick."""
    x = sine(f, seconds) * env_exp(seconds, 0.04, 0.0005) + sine(f * 2.76, seconds) * env_exp(seconds, 0.015, 0.0005) * 0.4 * bright
    if r is not None:
        x += hp(white(seconds, r), 3000) * env_exp(seconds, 0.002) * 0.3 * bright
    return x


def bell(f, seconds, r, decay=0.4):
    return modal([f, f * 2.0, f * 3.01, f * 4.17], [decay, decay * 0.5, decay * 0.3, decay * 0.15], [1, 0.35, 0.2, 0.1], seconds, r)


def roomy(x, r, wet=0.15, rt=0.5):
    return reverb(to_stereo(x), room_ir(0.9, rt, r), wet)


@ui("ui_hover", -34)
def ui_hover(r, v):
    return roomy(tap(2350, 0.05, r, 0.5), r, 0.08)


@ui("ui_click", -26)
def ui_click(r, v):
    return roomy(mix([(tap(1180, 0.15, r), 0, 1), (tap(1770, 0.12, r, 0.6), 0.03, 0.6)]), r)


@ui("ui_back", -27)
def ui_back(r, v):
    return roomy(mix([(tap(1180, 0.15, r), 0, 0.7), (tap(880, 0.18, r), 0.04, 1)]), r)


@ui("ui_tab", -28)
def ui_tab(r, v):
    return roomy(tap(1570, 0.1, r), r)


@ui("ui_toggle_on", -28)
def ui_toggle_on(r, v):
    return roomy(mix([(tap(1050, 0.1, r), 0, 0.8), (tap(1570, 0.12, r), 0.05, 1)]), r)


@ui("ui_toggle_off", -28)
def ui_toggle_off(r, v):
    return roomy(mix([(tap(1570, 0.1, r), 0, 0.8), (tap(1050, 0.12, r), 0.05, 1)]), r)


@ui("ui_slider", -36)
def ui_slider(r, v):
    return to_stereo(tap(3100, 0.03, r, 0.3))


@ui("ui_deny", -26)
def ui_deny(r, v):
    s = 0.3
    x = (square(220, s, 0.5) * 0.3 + square(233, s, 0.5) * 0.3)
    return roomy(lp(x, 1800) * env_lin(s, [(0, 0), (0.01, 1), (0.1, 0.6), (0.12, 0), (0.15, 0), (0.16, 1), (0.28, 0.5), (s, 0)]), r)


@ui("ui_notify", -25)
def ui_notify(r, v):
    s = 0.9
    return roomy(mix([(bell(988, s, r, 0.3), 0, 1), (bell(1318, s, r, 0.35), 0.09, 0.8)]), r, 0.2)


@ui("ui_start", -21)
def ui_start(r, v):
    """Match found or starting: a rising run up a bright scale and a soft low hit."""
    s = 1.6
    notes = [587.3, 659.3, 740.0, 880.0, 1174.7]
    parts = [(bell(f, 1.0, r, 0.45), k * 0.07, 0.8) for k, f in enumerate(notes)]
    parts.append((sine(glide_exp(110, 55, s, 0.05), s) * env_exp(s, 0.3, 0.002), 0.28, 1.0))
    return reverb(to_stereo(mix(parts, s)), hall(r, 1.6), 0.3)


@ui("map_open", -27)
def map_open(r, v):
    s = 0.35
    t = times(s)
    paper = bp(white(s, r), 1500, 7000) * env_hann(s) * (1 + 0.6 * lp(r.standard_normal(len(t)), 30) * 6)
    return roomy(mix([(paper, 0, 0.6), (tap(700, 0.15, r, 0.3), 0, 0.5)]), r, 0.1)


@ui("map_close", -28)
def map_close(r, v):
    s = 0.28
    t = times(s)
    paper = bp(white(s, r), 1200, 6000) * env_hann(s) * (1 + 0.6 * lp(r.standard_normal(len(t)), 30) * 6)
    return roomy(paper * 0.6, r, 0.1)


@ui("marker_place", -24)
def marker_place(r, v):
    s = 0.8
    return roomy(mix([(bell(1568, s, r, 0.25), 0, 1), (bell(2093, s, r, 0.2), 0.06, 0.6)]), r, 0.25)


@ui("marker_clear", -28)
def marker_clear(r, v):
    return roomy(mix([(tap(1568, 0.1, r), 0, 0.8), (tap(1175, 0.12, r), 0.05, 0.7)]), r)


@ui("hit_body", -24, variants=3, priority=6)
def hit_body(r, v):
    """Hit marker on the body: a short, dry tick that cuts through gunfire."""
    s = 0.09
    x = bp(white(s, r), 1800, 6000) * env_exp(s, 0.008, 0.0003) + sine(2200, s) * env_exp(s, 0.02, 0.0003) * 0.5
    return to_stereo(x)


# The old name the arsenal used for body hits.
sound("hit_flesh_ui", "ui", bus="Interface", level=-24, spatial=False, stereo=True, priority=6, reverb=0)(lambda r, v: hit_body(r, v))


@ui("hit_head", -21, variants=2, priority=5)
def hit_head(r, v):
    """Head hit: a bright, ringing ping."""
    s = 0.45
    x = bell(2637, s, r, 0.12) + hp(white(s, r), 4000) * env_exp(s, 0.003) * 0.5
    return roomy(x, r, 0.1)


@ui("hit_shield", -23, variants=2, priority=6)
def hit_shield(r, v):
    """Hit on armour: a metallic dink, duller than the head ping."""
    s = 0.25
    fs, ds, a = metal_modes(1400, 6, r, spread=1.5, decay=0.05)
    return to_stereo(modal(fs, ds, a, s, r) + bp(white(s, r), 1500, 6000) * env_exp(s, 0.006) * 0.6)


@ui("kill_confirm", -19, priority=4)
def kill_confirm(r, v):
    """Knocked out: a low thump and a two-note chime that settles."""
    s = 1.0
    thump = sine(glide_exp(140, 60, s, 0.03), s) * env_exp(s, 0.12, 0.001)
    chime = mix([(bell(1318.5, s, r, 0.3), 0.02, 1), (bell(1975.5, s, r, 0.35), 0.1, 0.9)], s)
    return roomy(thump * 0.9 + chime * 0.8, r, 0.2)


@ui("armour_break", -18, priority=5)
def armour_break(r, v):
    """The vest gives out: a crack and a shatter of plates."""
    s = 0.8
    t = times(s)
    crack = hp(white(s, r), 1500) * env_exp(s, 0.005)
    shards = np.zeros(n_of(s))
    for k in range(16):
        f = r.uniform(1500, 5000)
        shards = mix([(shards, 0, 1), (modal([f, f * 1.7], [0.04, 0.02], [1, 0.4], 0.15, r), r.uniform(0.01, 0.35), r.uniform(0.1, 0.3))], s)
    low = sine(glide_exp(300, 120, s, 0.05), s) * env_exp(s, 0.1) * 0.6
    return roomy(crack + shards + low, r, 0.15)


@ui("heartbeat", -22, priority=8)
def heartbeat(r, v):
    """Lub-dub, felt more than heard."""
    s = 0.9
    beat = lambda g: sine(glide_exp(70, 45, 0.25, 0.03), 0.25) * env_exp(0.25, 0.06, 0.004) * g
    return to_stereo(lp(mix([(beat(1.0), 0, 1), (beat(0.7), 0.22, 1)], s), 300))


@ui("hurt", -22, variants=3, priority=7)
def hurt(r, v):
    """Taking a hit: a body thud and a short sharp breath (no voice)."""
    s = 0.4
    thud = sine(glide_exp(120, 60, s, 0.02), s) * env_exp(s, 0.06, 0.001) + lp(white(s, r), 1500) * env_exp(s, 0.02) * 0.8
    breath = peak(bp(white(s, r), 500, 4000), 1200, 3, 8) * env_ad(s, 0.02, 0.07) * 0.4
    return to_stereo(thud + mix([(breath, 0.03, 1)], s))


@ui("alert", -22, priority=10)
def alert(r, v):
    """A notice on the HUD (supply drop on its way): three falling bell notes."""
    s = 1.4
    return roomy(mix([(bell(1174.7, s, r, 0.4), 0, 1), (bell(987.8, s, r, 0.4), 0.16, 0.9), (bell(1318.5, s, r, 0.5), 0.32, 1)], s), r, 0.25)


@ui("player_joined", -25)
def player_joined(r, v):
    s = 0.6
    return roomy(mix([(tap(880, 0.2, r), 0, 1), (tap(1320, 0.25, r), 0.08, 1)], s), r, 0.2)


@ui("player_left", -26)
def player_left(r, v):
    s = 0.6
    return roomy(mix([(tap(1320, 0.2, r), 0, 1), (tap(880, 0.25, r), 0.08, 1)], s), r, 0.2)


@ui("feed_kill", -30)
def feed_kill(r, v):
    """Someone else knocked out, in the kill feed: barely there."""
    return to_stereo(tap(660, 0.12, r, 0.3))


@ui("ui_pickup", -30, variants=2)
def ui_pickup(r, v):
    return to_stereo(mix([(tap(1760, 0.08, r, 0.5), 0, 1), (tap(2350, 0.08, r, 0.4), 0.04, 0.7)]))


@ui("ui_inventory", -31)
def ui_inventory(r, v):
    return to_stereo(tap(990, 0.1, r, 0.5))
