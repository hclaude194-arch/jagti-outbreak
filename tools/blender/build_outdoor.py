#!/usr/bin/env python3
"""Phase 4D: outdoor detail. Builds street furniture models in headless Blender and a placement
list Unity uses to set them out round the campus (Assets/Editor/OutdoorBuilder.cs).

Run after build_buildings.py, generate_layout.py and generate_terrain.py, with the Python that
has `bpy`:
    python tools/blender/build_outdoor.py

Writes:
    Assets/Models/Props/<Prop>.fbx      lamp posts, benches, bins, signs, railings, fences, hoops
    Assets/Data/outdoor_props.json      where each one stands, and the text on every sign

What goes where (all from campus_layout.json, so it follows layout changes):
    - lamp posts along every road, alternating sides about every 30 m, arms over the road;
      low bollard lights along the garden paths and the paved walks;
    - benches with a twin dustbin along the paths and round the lawns;
    - a name board for every named place, beside the nearest road, and a finger signpost at
      the main junctions pointing to the nearest places;
    - steel railings along road edges where the ground drops away steeply (sampled from the
      terrain heightmap) and along the water channels beside roads;
    - chain-link fences round the sports courts, basketball hoops and volleyball nets;
    - the name of each shop on its fascia board (build_buildings.SHOP_UNITS).
A prop's front faces +Z before its yaw (degrees clockwise from +Z seen from above), as for the
furniture; heights are above the ground at (gx, gz).
"""
import json
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_buildings as B  # noqa: E402  (imports bpy first)
import build_interiors as I  # noqa: E402
from build_interiors import bx, cyl, obox, new, export_model  # noqa: E402

import numpy as np  # noqa: E402

ROOT = B.ROOT
DATA = os.path.join(ROOT, "Assets", "Data")
OUT_JSON = os.path.join(DATA, "outdoor_props.json")
layout = B.layout

OUT_PROPS = {}


def oprop(name, solid=False, shadows=True):
    def deco(fn):
        OUT_PROPS[name] = (fn, solid, shadows)
        return fn
    return deco


# ------------------------------------------------------------------ models
@oprop("Lamp_Post", solid=True)
def p_lamp_post():
    """Street lamp: a 6 m tapered steel pole on a concrete footing, an arm reaching 1.4 m out
    over the road (+Z) and a flat LED head."""
    m = new("Lamp_Post")
    bx(m, -0.22, 0.22, -0.22, 0.22, 0.0, 0.45, "Concrete")
    bx(m, -0.15, 0.15, -0.15, 0.15, 0.45, 0.5, "Metal")
    for k in range(6):
        r = 0.085 - k * 0.008
        cyl(m, 0, 0, r, 0.5 + k, 1.5 + k, "Metal", seg=8)
    obox(m, 0.0, 0.45, 1.0, 0.06, math.pi / 2, 6.3, 6.36, "Metal")
    obox(m, 0.0, 0.2, 0.55, 0.04, math.pi / 2 - 0.0, 5.95, 6.0, "Metal")
    for k in range(3):
        bx(m, -0.02, 0.02, 0.1 + k * 0.15, 0.13 + k * 0.15, 6.0, 6.3, "Metal")
    bx(m, -0.16, 0.16, 0.85, 1.45, 6.24, 6.4, "Metal")
    bx(m, -0.13, 0.13, 0.88, 1.42, 6.23, 6.24, "Lamp_Glow")
    cyl(m, 0, 0, 0.06, 6.5, 6.6, "Metal", seg=8)
    return m


@oprop("Bollard_Light")
def p_bollard_light():
    """Low garden light, 0.9 m, a glowing band under a cap."""
    m = new("Bollard_Light")
    cyl(m, 0, 0, 0.09, 0.0, 0.7, "Metal", seg=10)
    cyl(m, 0, 0, 0.085, 0.7, 0.82, "Lamp_Glow", seg=10)
    cyl(m, 0, 0, 0.11, 0.82, 0.9, "Metal", seg=10)
    return m


@oprop("Garden_Lamp")
def p_garden_lamp():
    """Slim courtyard lamp post, 4.2 m, a black pole with a flat disc lamp on top (owner's
    photo of the academic courtyard)."""
    m = new("Garden_Lamp")
    cyl(m, 0, 0, 0.12, 0.0, 0.35, "Concrete", seg=10)
    cyl(m, 0, 0, 0.045, 0.35, 4.0, "Metal", seg=8)
    cyl(m, 0, 0, 0.26, 4.0, 4.1, "Lamp_Glow", seg=14)
    cyl(m, 0, 0, 0.3, 4.1, 4.2, "Metal", seg=14)
    return m


@oprop("Palm_Small")
def p_palm_small():
    """A young palm about 3 m high: a ringed trunk, slightly leaning, and a crown of drooping
    fronds made of thin leaf blades."""
    m = new("Palm_Small")
    rnd = random.Random(31)
    lean = 0.0
    for k in range(6):
        z0, z1 = 0.45 * k, 0.45 * (k + 1)
        cyl(m, lean, 0.0, 0.11 - 0.008 * k, z0, z1, "Wood", seg=7)
        lean += 0.025
    top = 2.7
    cyl(m, lean, 0.0, 0.16, top - 0.3, top + 0.1, "Foliage", seg=7)
    for k in range(9):
        a = 2 * math.pi * k / 9 + rnd.uniform(-0.15, 0.15)
        ca, sa = math.cos(a), math.sin(a)
        # Each frond: three blades getting narrower, rising a little then drooping.
        for j, (r0, r1, dz, w) in enumerate(((0.05, 0.6, 0.12, 0.2), (0.55, 1.1, 0.02, 0.17), (1.05, 1.5, -0.3, 0.11))):
            x0, x1 = lean + ca * r0, lean + ca * r1
            y0, y1 = sa * r0, sa * r1
            obox(m, (x0 + x1) / 2, (y0 + y1) / 2, (r1 - r0) / 2 + 0.06, w, a, top + dz - 0.04, top + dz + 0.04, "Foliage")
    return m


@oprop("Park_Bench", solid=True)
def p_park_bench():
    """Campus bench: sandstone end blocks, five wooden slats on the seat, three on the back,
    1.9 m long, seat facing +Z."""
    m = new("Park_Bench")
    for x in (-0.9, 0.75):
        bx(m, x, x + 0.15, -0.25, 0.25, 0.0, 0.42, "Stone")
        bx(m, x + 0.02, x + 0.13, -0.3, -0.22, 0.42, 0.85, "Stone")
        bx(m, x, x + 0.15, -0.27, 0.27, 0.42, 0.45, "Concrete")
    for k in range(5):
        z = -0.22 + k * 0.095
        bx(m, -0.95, 0.95, z, z + 0.08, 0.42, 0.46, "Wood")
    for k in range(3):
        y = 0.55 + k * 0.1
        bx(m, -0.95, 0.95, -0.28, -0.25, y, y + 0.08, "Wood")
    return m


@oprop("Twin_Dustbin", solid=True)
def p_twin_dustbin():
    """Green and blue bins (wet and dry waste) on a steel stand under a small roof."""
    m = new("Twin_Dustbin")
    for x in (-0.55, 0.52):
        bx(m, x, x + 0.04, -0.02, 0.02, 0.0, 1.15, "Metal")
    bx(m, -0.6, 0.6, -0.25, 0.25, 1.15, 1.18, "Metal")
    obox(m, 0.0, 0.0, 1.3, 0.6, 0.0, 1.18, 1.22, "Planter")
    for x, mat in ((-0.25, "Planter"), (0.25, "Sign_Board")):
        cyl(m, x, 0.0, 0.2, 0.25, 0.85, mat, top="Metal", seg=12)
        bx(m, x - 0.2, x + 0.2, -0.02, 0.02, 0.75, 0.8, "Metal")
    bx(m, -0.55, 0.55, -0.02, 0.02, 0.75, 0.79, "Metal")
    return m


@oprop("Name_Board", solid=True)
def p_name_board():
    """Place-name board: a 2.4 x 0.9 m blue panel with a white border between two sandstone
    piers on a plinth; the name is set on the front (+Z) in Unity."""
    m = new("Name_Board")
    bx(m, -1.5, 1.5, -0.25, 0.25, 0.0, 0.3, "Stone")
    for x in (-1.45, 1.25):
        bx(m, x, x + 0.2, -0.15, 0.15, 0.3, 1.75, "Stone")
        bx(m, x - 0.02, x + 0.22, -0.17, 0.17, 1.75, 1.83, "Concrete")
    bx(m, -1.25, 1.25, -0.04, 0.04, 0.7, 1.6, "Concrete")
    bx(m, -1.2, 1.2, -0.05, 0.05, 0.75, 1.55, "Sign_Board")
    return m


@oprop("Sign_Post", solid=True)
def p_sign_post():
    """Finger signpost pole, 3 m, with a cap; arms (Sign_Arm) are set on it at 2.0-2.8 m."""
    m = new("Sign_Post")
    bx(m, -0.2, 0.2, -0.2, 0.2, 0.0, 0.25, "Concrete")
    cyl(m, 0, 0, 0.05, 0.25, 3.0, "Metal", seg=8)
    cyl(m, 0, 0, 0.08, 3.0, 3.08, "Metal", seg=8)
    return m


@oprop("Sign_Arm", shadows=False)
def p_sign_arm():
    """One finger of a signpost, 1.3 x 0.24 m, pointing along +X from the pole, with a pointed
    tip; the place name is set on both faces in Unity."""
    m = new("Sign_Arm")
    bx(m, 0.06, 1.2, -0.025, 0.025, -0.12, 0.12, "Sign_Board")
    obox(m, 1.25, 0.0, 0.14, 0.05, 0.0, -0.085, 0.085, "Sign_Board")
    bx(m, 0.04, 0.14, -0.06, 0.06, -0.09, 0.09, "Metal")
    return m


@oprop("Railing_Segment", solid=True)
def p_railing_segment():
    """3 m of steel pipe railing along X: posts at the ends and the middle, a handrail and a
    mid rail, painted in yellow and black bands at the posts."""
    m = new("Railing_Segment")
    for x in (-1.5, 0.0, 1.47):
        bx(m, x, x + 0.05, -0.025, 0.025, -0.3, 1.0, "Metal")
        bx(m, x - 0.01, x + 0.06, -0.035, 0.035, 0.75, 0.9, "Paint_Yellow")
    bx(m, -1.5, 1.52, -0.03, 0.03, 0.98, 1.05, "Steel")
    bx(m, -1.5, 1.52, -0.02, 0.02, 0.5, 0.54, "Steel")
    return m


@oprop("Fence_Panel", solid=True)
def p_fence_panel():
    """3 m panel of chain-link court fencing, 3.6 m high, between two steel posts."""
    m = new("Fence_Panel")
    for x in (-1.5, 1.45):
        bx(m, x, x + 0.06, -0.03, 0.03, -0.3, 3.6, "Metal")
    for y in (0.05, 1.8, 3.55):
        bx(m, -1.5, 1.5, -0.02, 0.02, y, y + 0.04, "Metal")
    bx(m, -1.47, 1.47, -0.004, 0.004, 0.05, 3.58, "Net")
    return m


@oprop("Basketball_Hoop", solid=True)
def p_basketball_hoop():
    """Basketball post: a padded pole set back 1.2 m, a glass backboard and an orange rim at
    3.05 m with a net, facing +Z (the court)."""
    m = new("Basketball_Hoop")
    bx(m, -0.3, 0.3, -1.5, -0.9, 0.0, 0.2, "Concrete")
    bx(m, -0.08, 0.08, -1.28, -1.12, 0.2, 3.3, "Metal")
    bx(m, -0.12, 0.12, -1.32, -1.08, 0.2, 2.0, "Sign_Board")
    obox(m, 0.0, -0.7, 1.0, 0.1, math.pi / 2, 3.25, 3.35, "Metal")
    bx(m, -0.9, 0.9, -0.22, -0.2, 2.9, 3.95, "Glass_Clear")
    bx(m, -0.92, 0.92, -0.24, -0.2, 2.88, 2.92, "Metal")
    bx(m, -0.92, 0.92, -0.24, -0.2, 3.93, 3.97, "Metal")
    bx(m, -0.3, 0.3, -0.2, -0.19, 3.05, 3.5, "Linen")
    for k in range(12):
        a = 2 * math.pi * k / 12
        obox(m, 0.23 * math.cos(a), 0.03 + 0.23 * math.sin(a), 0.13, 0.02, a + math.pi / 2, 3.04, 3.06, "Red_Paint")
    cyl(m, 0.0, 0.03, 0.2, 2.65, 3.04, "Net", top=None, seg=12)
    return m


@oprop("Volleyball_Net", solid=True)
def p_volleyball_net():
    """Volleyball posts and a 9.5 m net at 2.43 m, centred, net along X."""
    m = new("Volleyball_Net")
    for x in (-4.9, 4.9):
        cyl(m, x, 0.0, 0.05, 0.0, 2.55, "Metal", seg=8)
    bx(m, -4.75, 4.75, -0.005, 0.005, 1.43, 2.43, "Net")
    bx(m, -4.75, 4.75, -0.012, 0.012, 2.36, 2.43, "Linen")
    return m


@oprop("Badminton_Net", solid=True)
def p_badminton_net():
    """Badminton posts and a 6.1 m net, its top 1.55 m up, centred, net along X."""
    m = new("Badminton_Net")
    for x in (-3.1, 3.1):
        bx(m, x - 0.2, x + 0.2, -0.2, 0.2, 0.0, 0.06, "Concrete")
        cyl(m, x, 0.0, 0.035, 0.06, 1.58, "Metal", seg=8)
    bx(m, -3.05, 3.05, -0.004, 0.004, 0.79, 1.55, "Net")
    bx(m, -3.05, 3.05, -0.01, 0.01, 1.51, 1.55, "Linen")
    return m


@oprop("Tennis_Net", solid=True)
def p_tennis_net():
    """Tennis posts and a 12.8 m net along X, 1.07 m at the posts and pulled down to 0.91 m at
    the middle by a white centre strap."""
    m = new("Tennis_Net")
    for x in (-6.4, 6.4):
        cyl(m, x, 0.0, 0.05, 0.0, 1.1, "Metal", seg=8)
    n = 8
    for i in range(n):
        xa, xb = -6.35 + 12.7 * i / n, -6.35 + 12.7 * (i + 1) / n
        xm = abs((xa + xb) / 2) / 6.35
        top = 0.91 + 0.16 * xm
        bx(m, xa, xb, -0.004, 0.004, 0.05, top, "Net")
        bx(m, xa, xb, -0.01, 0.01, top - 0.05, top, "Linen")
    bx(m, -0.025, 0.025, -0.012, 0.012, 0.0, 0.91, "Linen")
    return m


@oprop("Cycle_Stand", solid=True)
def p_cycle_stand():
    """A steel cycle rack with five bicycles parked in it, 3.2 m long along X, bikes along Z."""
    m = new("Cycle_Stand")
    rnd = random.Random(8)
    bx(m, -1.6, 1.6, -0.03, 0.03, 0.0, 0.05, "Metal")
    for k in range(9):
        x = -1.6 + k * 0.4
        bx(m, x - 0.015, x + 0.015, -0.03, 0.03, 0.05, 0.42, "Steel")
    bx(m, -1.6, 1.6, -0.02, 0.02, 0.4, 0.43, "Steel")
    for k in range(5):
        if rnd.random() < 0.15:
            continue
        x = -1.4 + k * 0.7
        frame = rnd.choice(("Red_Paint", "Sign_Board", "Metal", "Planter", "Plastic"))
        for zc in (-0.55, 0.5):                          # two wheels, 0.33 m radius
            for j in range(14):
                a = 2 * math.pi * j / 14
                obox(m, x, zc + 0.33 * math.cos(a), 0.025, 0.16, math.pi / 2, 0.34 + 0.33 * math.sin(a) - 0.012,
                     0.34 + 0.33 * math.sin(a) + 0.012, "Rubber")
        obox(m, x, -0.02, 0.03, 0.95, math.pi / 2, 0.62, 0.66, frame)          # top tube
        obox(m, x, -0.2, 0.03, 0.7, math.pi / 2, 0.34, 0.38, frame)            # down tube (low)
        bx(m, x - 0.015, x + 0.015, -0.3, -0.26, 0.34, 0.8, frame)              # seat tube
        bx(m, x - 0.1, x + 0.1, -0.36, -0.2, 0.8, 0.84, "Leather")              # saddle
        bx(m, x - 0.015, x + 0.015, 0.44, 0.48, 0.34, 0.95, frame)              # fork and stem
        bx(m, x - 0.28, x + 0.28, 0.44, 0.48, 0.95, 0.98, "Metal")              # handlebar
        bx(m, x - 0.12, x + 0.12, 0.5, 0.72, 0.8, 0.95, "Steel")                # basket
    return m


@oprop("Planter_Box", solid=True)
def p_planter_box():
    """Sandstone planter with clipped shrubs, 1.6 x 0.6 m, for the paved walks."""
    m = new("Planter_Box")
    bx(m, -0.8, 0.8, -0.3, 0.3, 0.0, 0.55, "Stone")
    bx(m, -0.72, 0.72, -0.22, 0.22, 0.55, 0.56, "Planter")
    rnd = random.Random(7)
    for i in range(5):
        x = -0.6 + i * 0.3
        h = rnd.uniform(0.3, 0.5)
        bx(m, x - 0.14, x + 0.14, -0.17, 0.17, 0.56, 0.56 + h, "Foliage")
    return m


# ------------------------------------------------------------------ terrain and layout queries
TER = json.load(open(os.path.join(DATA, "campus_terrain.json")))
RES = TER["heightmapResolution"]
H = np.fromfile(os.path.join(DATA, "terrain_height.bytes"), dtype=np.uint16).reshape(RES, RES).astype(np.float64)
H = TER["minHeight"] + H / 65535.0 * TER["heightRange"]


def ground(X, Z):
    """Terrain height at world (X, Z), bilinear, as Unity's Terrain.SampleHeight."""
    u = (X - TER["minX"]) / TER["sizeX"] * (RES - 1)
    v = (Z - TER["minZ"]) / TER["sizeZ"] * (RES - 1)
    i, j = int(math.floor(u)), int(math.floor(v))
    i, j = max(0, min(RES - 2, i)), max(0, min(RES - 2, j))
    fu, fv = u - i, v - j
    h00, h10, h01, h11 = H[j, i], H[j, i + 1], H[j + 1, i], H[j + 1, i + 1]
    return (h00 * (1 - fu) + h10 * fu) * (1 - fv) + (h01 * (1 - fu) + h11 * fu) * fv


ROADS = [r for r in layout["roads"] if r["name"] != "Highway"]
BOX = [(b["x"] - b["width"] / 2, b["x"] + b["width"] / 2, b["z"] - b["depth"] / 2, b["z"] + b["depth"] / 2)
       for b in layout["buildings"]]
# The clock tower walk and its tower landing (generate_terrain.py) are kept clear too.
with open(B.TERRAIN_JSON) as _f:
    _cs = json.load(_f)["clockSteps"]
_cz = [z for z, _h in _cs["profile"]]
BOX.append((_cs["x"] - 6.5, _cs["x"] + 6.5, min(_cz), max(_cz) + 1.5))
BOUNDARY = list(zip(layout["boundary"][0::2], layout["boundary"][1::2]))
KEEP_OUT_AREAS = ("court", "track", "field", "amphitheatre", "roundabout", "substation", "courtyard")


def pts(road):
    p = road["points"]
    return list(zip(p[0::2], p[1::2]))


def seg_dist(P_, A, B_):
    dx, dz = B_[0] - A[0], B_[1] - A[1]
    L2 = dx * dx + dz * dz
    t = 0.0 if L2 < 1e-9 else max(0.0, min(1.0, ((P_[0] - A[0]) * dx + (P_[1] - A[1]) * dz) / L2))
    return math.hypot(P_[0] - A[0] - t * dx, P_[1] - A[1] - t * dz)


def line_dist(P_, poly):
    return min(seg_dist(P_, a, b_) for a, b_ in zip(poly, poly[1:])) if len(poly) > 1 else 1e9


ROAD_PTS = [(r, pts(r)) for r in ROADS]
WATER_PTS = [(w, list(zip(w["points"][0::2], w["points"][1::2]))) for w in layout["water"]]


def inside_boundary(P_):
    x, z = P_
    inside = False
    n = len(BOUNDARY)
    for k in range(n):
        (x1, z1), (x2, z2) = BOUNDARY[k], BOUNDARY[(k + 1) % n]
        if (z1 > z) != (z2 > z) and x < x1 + (z - z1) * (x2 - x1) / (z2 - z1):
            inside = not inside
    return inside


def in_area(P_, a, pad=0.0):
    x, z = P_
    if a["shape"] == "box":
        return abs(x - a["x"]) < a["width"] / 2 + pad and abs(z - a["z"]) < a["depth"] / 2 + pad
    rx, rz = a["width"] / 2 + pad, a["depth"] / 2 + pad
    return ((x - a["x"]) / rx) ** 2 + ((z - a["z"]) / rz) ** 2 < 1.0


def clear(P_, pad=1.2, road_pad=0.6, skip_road=None):
    """True if nothing stands at P: no building, carriageway, water, court or pond, and inside
    the campus wall."""
    if not inside_boundary(P_):
        return False
    x, z = P_
    for X0, X1, Z0, Z1 in BOX:
        if X0 - pad < x < X1 + pad and Z0 - pad < z < Z1 + pad:
            return False
    for r, poly in ROAD_PTS:
        if r is skip_road:
            continue
        if line_dist(P_, poly) < r["width"] / 2 + road_pad:
            return False
    for w, poly in WATER_PTS:
        if line_dist(P_, poly) < w["width"] / 2 + 0.8:
            return False
    for p_ in layout["pools"]:
        if math.hypot(x - p_["x"], z - p_["z"]) < p_["radius"] + 1.0:
            return False
    for a in layout["areas"]:
        if a["type"] in KEEP_OUT_AREAS and in_area(P_, a, 0.8):
            return False
    return True


def walk(poly, step, start=0.0):
    """(point, unit tangent, distance) every step metres along a polyline."""
    out = []
    d0 = start
    acc = 0.0
    for a, b_ in zip(poly, poly[1:]):
        L = math.dist(a, b_)
        if L < 1e-6:
            continue
        t = ((b_[0] - a[0]) / L, (b_[1] - a[1]) / L)
        while d0 <= acc + L:
            s = d0 - acc
            out.append(((a[0] + t[0] * s, a[1] + t[1] * s), t, d0))
            d0 += step
        acc += L
    return out


def yaw_to(dx, dz):
    return round(math.degrees(math.atan2(dx, dz)), 2)


PROPS_OUT = []
SIGNS = []


def place(name, X, Z, yaw, dy=0.0, gx=None, gz=None):
    PROPS_OUT.append(dict(prop=name, x=round(X, 3), z=round(Z, 3), y=round(dy, 3), yaw=round(yaw % 360.0, 2),
                          gx=round(X if gx is None else gx, 3), gz=round(Z if gz is None else gz, 3)))


def sign(text, X, Z, y, yaw, size=0.12, gx=None, gz=None, color=(1.0, 1.0, 1.0)):
    """Text facing yaw (the direction its readers look from), y above the ground at (gx, gz)."""
    SIGNS.append(dict(text=text, x=round(X, 3), z=round(Z, 3), y=round(y, 3), yaw=round(yaw % 360.0, 2), size=size,
                      gx=round(X if gx is None else gx, 3), gz=round(Z if gz is None else gz, 3), color=list(color)))


def fit(text, width, cap):
    """Letter height that fits text into width (about 0.6 of the height per letter)."""
    return round(min(cap, width / (0.6 * max(1, len(text)))), 3)


def taken(P_, r=2.0):
    return any(math.hypot(P_[0] - p_["x"], P_[1] - p_["z"]) < r for p_ in PROPS_OUT)


def is_path(r):
    return r["width"] <= 4.0 or r["name"].startswith(("Courtyard", "Plaza", "Clock Tower"))


# ------------------------------------------------------------------ placement
def lamp_posts():
    for r, poly in ROAD_PTS:
        if is_path(r):
            continue
        side = 1.0
        for P_, t, d in walk(poly, 26.0, 8.0):
            n = (-t[1] * side, t[0] * side)                # the side the lamp stands on
            off = r["width"] / 2 + 1.1
            Q = (P_[0] + n[0] * off, P_[1] + n[1] * off)
            if not clear(Q, pad=1.5, road_pad=0.8) or taken(Q, 7.0):
                side = -side
                n = (-n[0], -n[1])
                Q = (P_[0] + n[0] * off, P_[1] + n[1] * off)
                if not clear(Q, pad=1.5, road_pad=0.8) or taken(Q, 7.0):
                    continue
            place("Lamp_Post", Q[0], Q[1], yaw_to(-n[0], -n[1]))
            side = -side


def courtyard_lamps_and_palms(r, poly):
    """Academic courtyard (owner's photo): slim lamp posts and young palms just off the paved
    paths, beyond the hedges, alternating sides; nothing round the central pool."""
    if r["name"] == "Courtyard Centre Square":
        return
    side = 1.0
    for k, (P_, t, d) in enumerate(walk(poly, 9.0, 4.5)):
        n = (-t[1] * side, t[0] * side)
        Q = (P_[0] + n[0] * (r["width"] / 2 + 1.1), P_[1] + n[1] * (r["width"] / 2 + 1.1))
        if not taken(Q, 3.0) and not near_road_except(Q, r, 1.6):
            place("Garden_Lamp", Q[0], Q[1], 0.0)
        Qp = (P_[0] - n[0] * (r["width"] / 2 + 1.6), P_[1] - n[1] * (r["width"] / 2 + 1.6))
        if k % 2 == 1 and not taken(Qp, 3.0) and not near_road_except(Qp, r, 2.0):
            place("Palm_Small", Qp[0], Qp[1], (k * 47.0) % 360.0)
        side = -side


def near_road_except(Q, r, gap):
    """Is Q within gap of the edge of any road or path other than r?"""
    for r2, poly2 in ROAD_PTS:
        if r2 is r:
            continue
        if line_dist(Q, poly2) < r2["width"] / 2 + gap:
            return True
    return False


def path_lights_and_benches():
    for r, poly in ROAD_PTS:
        if not is_path(r):
            continue
        if r["name"].startswith("Courtyard"):
            courtyard_lamps_and_palms(r, poly)
            continue
        step = 12.0 if r["name"].startswith("Plaza") else 16.0
        side = 1.0
        for k, (P_, t, d) in enumerate(walk(poly, step, step / 2)):
            n = (-t[1] * side, t[0] * side)
            off = r["width"] / 2 + 0.6
            Q = (P_[0] + n[0] * off, P_[1] + n[1] * off)
            if clear(Q, pad=0.8, road_pad=0.3) and not taken(Q, 3.0):
                place("Bollard_Light", Q[0], Q[1], 0.0)
            # A bench on the other side every second step, a bin beside every second bench.
            if k % 2 == 0:
                m_ = (-n[0], -n[1])
                offb = r["width"] / 2 + 0.75
                Qb = (P_[0] + m_[0] * offb, P_[1] + m_[1] * offb)
                if clear(Qb, pad=1.0, road_pad=0.35) and not taken(Qb, 3.0):
                    place("Park_Bench", Qb[0], Qb[1], yaw_to(-m_[0], -m_[1]))
                    if k % 4 == 0:
                        Qd = (Qb[0] + t[0] * 1.6, Qb[1] + t[1] * 1.6)
                        if clear(Qd, pad=0.8, road_pad=0.3):
                            place("Twin_Dustbin", Qd[0], Qd[1], yaw_to(-m_[0], -m_[1]))
            side = -side


def lawn_benches():
    """Benches facing in along the long sides of the lawns, a bin at every other one."""
    for a in layout["areas"]:
        if a["type"] != "lawn" or a["shape"] != "box":
            continue
        x0, x1 = a["x"] - a["width"] / 2, a["x"] + a["width"] / 2
        z0, z1 = a["z"] - a["depth"] / 2, a["z"] + a["depth"] / 2
        long_x = a["width"] >= a["depth"]
        L = a["width"] if long_x else a["depth"]
        n_b = max(1, int(L // 22))
        for i in range(n_b):
            f = (i + 0.5) / n_b
            for s in (-1, 1):
                if long_x:
                    Q = (x0 + f * (x1 - x0), (z0 + 1.6) if s < 0 else (z1 - 1.6))
                    face = (0.0, 1.0) if s < 0 else (0.0, -1.0)
                else:
                    Q = ((x0 + 1.6) if s < 0 else (x1 - 1.6), z0 + f * (z1 - z0))
                    face = (1.0, 0.0) if s < 0 else (-1.0, 0.0)
                if clear(Q, pad=1.0, road_pad=0.8) and not taken(Q, 4.0):
                    place("Park_Bench", Q[0], Q[1], yaw_to(*face))
                    if i % 2 == 0:
                        tq = (face[1], -face[0])
                        Qd = (Q[0] + tq[0] * 1.6, Q[1] + tq[1] * 1.6)
                        if clear(Qd, pad=0.8, road_pad=0.8):
                            place("Twin_Dustbin", Qd[0], Qd[1], yaw_to(*face))


def shops_walk_furniture():
    """Planters between the benches on the walk along the shop fronts."""
    walk_ = next(r for r in ROADS if r["name"] == "Plaza Shops Walk")
    poly = pts(walk_)
    X = poly[0][0] + walk_["width"] / 2 + 0.7
    for k, Z in enumerate(range(-6, -92, -12)):
        Q = (X, float(Z))
        if clear(Q, pad=0.6, road_pad=0.2) and not taken(Q, 2.5):
            place("Planter_Box", Q[0], Q[1], 90.0)


def hostel_furniture():
    """Benches, bins and cycle stands outside the hostels: a few metres out from the middle of
    each open side of every hostel wing."""
    for b in layout["buildings"]:
        if "Hostel" not in b["name"]:
            continue
        x0, x1 = b["x"] - b["width"] / 2, b["x"] + b["width"] / 2
        z0, z1 = b["z"] - b["depth"] / 2, b["z"] + b["depth"] / 2
        sides = [((b["x"], z0 - 4.0), (0.0, -1.0), b["width"]), ((b["x"], z1 + 4.0), (0.0, 1.0), b["width"]),
                 ((x0 - 4.0, b["z"]), (-1.0, 0.0), b["depth"]), ((x1 + 4.0, b["z"]), (1.0, 0.0), b["depth"])]
        for k, (Q, n, L) in enumerate(sides):
            t = (n[1], -n[0])
            for j, off in enumerate((-L / 4, L / 4)):
                S = (Q[0] + t[0] * off, Q[1] + t[1] * off)
                if not clear(S, pad=2.5, road_pad=1.0) or taken(S, 4.0):
                    continue
                if j == 0 and k % 2 == 0:
                    place("Cycle_Stand", S[0], S[1], yaw_to(n[0], n[1]) + 90.0)
                else:
                    place("Park_Bench", S[0], S[1], yaw_to(n[0], n[1]))
                    D = (S[0] + t[0] * 1.6, S[1] + t[1] * 1.6)
                    if clear(D, pad=2.0, road_pad=1.0):
                        place("Twin_Dustbin", D[0], D[1], yaw_to(n[0], n[1]))


def nearest_on_roads(P_, paths=False):
    best = None
    for r, poly in ROAD_PTS:
        if is_path(r) and not paths:
            continue
        for a, b_ in zip(poly, poly[1:]):
            dx, dz = b_[0] - a[0], b_[1] - a[1]
            L2 = dx * dx + dz * dz
            if L2 < 1e-9:
                continue
            t = max(0.0, min(1.0, ((P_[0] - a[0]) * dx + (P_[1] - a[1]) * dz) / L2))
            Q = (a[0] + t * dx, a[1] + t * dz)
            d = math.dist(P_, Q)
            if best is None or d < best[0]:
                L = math.sqrt(L2)
                best = (d, Q, r, (dx / L, dz / L))
    return best


def clean_name(name):
    """Drop the guessed alternative names ("(... ?)") from a layout place name."""
    if "(" in name and "?" in name:
        name = name[:name.index("(")].strip()
    return name


def name_boards():
    for pl in layout["places"]:
        P_ = (pl["x"], pl["z"])
        d, Q, r, t = nearest_on_roads(P_)
        if d > 120:
            continue
        n = ((P_[0] - Q[0]) / max(d, 1e-6), (P_[1] - Q[1]) / max(d, 1e-6))
        placed = False
        for off in (r["width"] / 2 + 2.2, r["width"] / 2 + 3.5, r["width"] / 2 + 5.0):
            for slide in (0.0, 4.0, -4.0, 8.0, -8.0):
                S = (Q[0] + n[0] * off + t[0] * slide, Q[1] + n[1] * off + t[1] * slide)
                if clear(S, pad=1.6, road_pad=1.0) and not taken(S, 3.0):
                    yaw = yaw_to(-n[0], -n[1])
                    place("Name_Board", S[0], S[1], yaw)
                    a = math.radians(yaw)
                    fx, fz = math.sin(a), math.cos(a)
                    nm = clean_name(pl["name"])
                    sign(nm, S[0] + fx * 0.06, S[1] + fz * 0.06, 1.15, yaw, size=fit(nm, 2.2, 0.24), gx=S[0], gz=S[1])
                    placed = True
                    break
            if placed:
                break


def junctions():
    """Points where a road ends on another road (the bigger ones only), merged within 25 m."""
    out = []
    for r, poly in ROAD_PTS:
        if is_path(r):
            continue
        for end in (poly[0], poly[-1]):
            for r2, poly2 in ROAD_PTS:
                if r2 is r or is_path(r2):
                    continue
                if line_dist(end, poly2) < r2["width"] / 2 + 2.0:
                    if not any(math.dist(end, j) < 25.0 for j in out):
                        out.append(end)
                    break
    return out


def signposts():
    places = [(clean_name(p_["name"]), (p_["x"], p_["z"])) for p_ in layout["places"]]
    for J in junctions():
        spot = None
        for ang in range(0, 360, 30):
            for rad in (7.0, 9.0, 11.0):
                S = (J[0] + rad * math.cos(math.radians(ang)), J[1] + rad * math.sin(math.radians(ang)))
                if clear(S, pad=1.2, road_pad=0.9) and not taken(S, 3.0):
                    spot = S
                    break
            if spot:
                break
        if spot is None:
            continue
        near = sorted((math.dist(spot, P_), nm, P_) for nm, P_ in places if math.dist(spot, P_) > 40.0)[:3]
        if not near:
            continue
        place("Sign_Post", spot[0], spot[1], 0.0)
        for k, (_d, nm, P_) in enumerate(near):
            dx, dz = P_[0] - spot[0], P_[1] - spot[1]
            # The arm points along its +X: yaw so that +X turns towards the place.
            yaw = math.degrees(math.atan2(dx, dz)) - 90.0
            y = 2.75 - k * 0.32
            place("Sign_Arm", spot[0], spot[1], yaw, dy=y)
            a = math.radians(yaw)
            ux, uz = math.cos(a), -math.sin(a)          # the arm's +X in world X, Z
            fx, fz = math.sin(a), math.cos(a)           # its +Z face
            cxm, czm = spot[0] + ux * 0.66, spot[1] + uz * 0.66
            sz = fit(nm, 1.05, 0.12)
            sign(nm, cxm + fx * 0.03, czm + fz * 0.03, y, yaw, size=sz, gx=spot[0], gz=spot[1])
            sign(nm, cxm - fx * 0.03, czm - fz * 0.03, y, yaw + 180.0, size=sz, gx=spot[0], gz=spot[1])


def railings():
    """Railing segments along road edges where the ground falls 1.5 m or more within 4 m of
    the kerb, and where a water channel runs close beside a road."""
    for r, poly in ROAD_PTS:
        if r["width"] < 4.0:
            continue
        for P_, t, d in walk(poly, 3.0, 1.5):
            for side in (1.0, -1.0):
                n = (-t[1] * side, t[0] * side)
                e = r["width"] / 2 + 0.5
                Q = (P_[0] + n[0] * e, P_[1] + n[1] * e)
                F = (P_[0] + n[0] * (e + 4.0), P_[1] + n[1] * (e + 4.0))
                drop = ground(*Q) - ground(*F)
                near_water = any(line_dist(Q, wp) < w["width"] / 2 + 2.5 for w, wp in WATER_PTS)
                if drop < 1.5 and not near_water:
                    continue
                if not clear(Q, pad=0.3, road_pad=0.25) and not near_water:
                    continue
                if any(seg_dist(Q, a, b_) < 0.2 + w["width"] / 2 for w, wp in WATER_PTS for a, b_ in zip(wp, wp[1:])):
                    continue
                if not inside_boundary(Q):
                    continue
                place("Railing_Segment", Q[0], Q[1], yaw_to(t[0], t[1]) - 90.0)


def court_kind(a):
    """Courts are told apart by size: badminton courts (under 16 m) in the girls' hostel
    courtyards, the tennis pair beside the running track (27 m to 35 m), and the square sports
    courts by the hostels (the rest)."""
    lo, hi = min(a["width"], a["depth"]), max(a["width"], a["depth"])
    if hi < 16:
        return "badminton"
    if lo >= 27 and hi < 35:
        return "tennis"
    return "sports"


def fence_round(x0, x1, z0, z1):
    for (A, B_, yaw) in (((x0, z0), (x1, z0), 0.0), ((x1, z1), (x0, z1), 0.0),
                         ((x0, z1), (x0, z0), 90.0), ((x1, z0), (x1, z1), 90.0)):
        L = math.dist(A, B_)
        n = max(1, round(L / 3.0))
        for i in range(n):
            f = (i + 0.5) / n
            X, Z = A[0] + (B_[0] - A[0]) * f, A[1] + (B_[1] - A[1]) * f
            if yaw == 90.0 and A[0] == x0 and i == n // 2:
                continue                                # a gate on the west side
            place("Fence_Panel", X, Z, yaw)


def courts():
    """Fences round the sports and tennis courts, hoops on the first sports court and on the
    big courts, volleyball nets on the other sports courts, tennis nets on the tennis pair and
    open badminton courts (net only) in the girls' hostel courtyards."""
    cs = [a for a in layout["areas"] if a["type"] == "court"]
    sports = [c for c in cs if court_kind(c) == "sports"]
    for k, a in enumerate(cs):
        kind = court_kind(a)
        long_x = a["width"] > a["depth"]
        if kind == "badminton":
            # The net crosses the court's long axis.
            place("Badminton_Net", a["x"], a["z"], 90.0 if long_x else 0.0)
            continue
        x0, x1 = a["x"] - a["width"] / 2 - 1.0, a["x"] + a["width"] / 2 + 1.0
        z0, z1 = a["z"] - a["depth"] / 2 - 1.0, a["z"] + a["depth"] / 2 + 1.0
        if kind == "tennis":
            # Two courts side by side, each long across the pair's long side.
            fence_round(x0, x1, z0, z1)
            for s_ in (-1.0, 1.0):
                if long_x:
                    place("Tennis_Net", a["x"], a["z"] + s_ * a["depth"] / 4, 90.0)
                else:
                    place("Tennis_Net", a["x"] + s_ * a["width"] / 4, a["z"], 0.0)
        else:
            if a["width"] < 30:      # the three courts by the hostels: fenced all round
                fence_round(x0, x1, z0, z1)
            if (sports and a is sports[0]) or a["width"] >= 30:
                if long_x:
                    place("Basketball_Hoop", a["x"] - a["width"] / 2 + 0.4, a["z"], 90.0)
                    place("Basketball_Hoop", a["x"] + a["width"] / 2 - 0.4, a["z"], 270.0)
                else:
                    place("Basketball_Hoop", a["x"], a["z"] - a["depth"] / 2 + 0.4, 0.0)
                    place("Basketball_Hoop", a["x"], a["z"] + a["depth"] / 2 - 0.4, 180.0)
            else:
                place("Volleyball_Net", a["x"], a["z"], 90.0 if not long_x else 0.0)
        # Benches outside the fence on the east side.
        for dz in (-6.0, 6.0):
            Q = (x1 + 1.4, a["z"] + dz)
            if clear(Q, pad=0.6, road_pad=0.6):
                place("Park_Bench", Q[0], Q[1], 270.0)


def shop_signs():
    """Each unit's name on its fascia board, and the names over the clinic and the sports
    complex lobby."""
    z0, z1, d0, d1 = B.FASCIA
    for name, units in B.SHOP_UNITS.items():
        X0, X1, Z0, Z1 = B.box_rect(name)
        bays, bw, lo = B.side_bays(name, "E")
        cx, cz = B.centre(name)
        for unit, b0, b1, door in units:
            za, zb = lo + b0 * bw + B.SHOP_INSET, lo + (b1 + 1) * bw - B.SHOP_INSET
            sign(unit.upper(), X1 + d1 + 0.012, (za + zb) / 2, (z0 + z1) / 2, 90.0,
                 size=fit(unit, zb - za - 0.3, 0.26), gx=cx, gz=cz, color=(1.0, 0.95, 0.85))
    med = "Shopping & Medical 1 (ATM)"
    X0, X1, Z0, Z1 = B.box_rect(med)
    _X, Zd = B.side_point(med, "E", 2)
    cx, cz = B.centre(med)
    sign("MEDICAL CENTRE", X1 + 0.03, Zd, B.DOOR_TOP + 0.2, 90.0, size=0.2, gx=cx, gz=cz, color=(0.75, 0.1, 0.08))
    sc = "Sports Complex"
    X0, X1, Z0, Z1 = B.box_rect(sc)
    _X, Zd = B.side_point(sc, "W", 2)
    cx, cz = B.centre(sc)
    sign("SPORTS COMPLEX", X0 - 0.03, Zd, B.DOOR_TOP + 0.2, 270.0, size=0.2, gx=cx, gz=cz, color=(0.1, 0.18, 0.4))


def main():
    os.makedirs(I.PROP_DIR, exist_ok=True)
    courts()
    shops_walk_furniture()
    name_boards()
    signposts()
    lamp_posts()
    hostel_furniture()
    path_lights_and_benches()
    lawn_benches()
    railings()
    shop_signs()
    models = []
    for name in sorted(OUT_PROPS):
        fn, solid, shadows = OUT_PROPS[name]
        m = fn()
        o = export_model(m, os.path.join(I.PROP_DIR, name + ".fbx"))
        models.append(dict(name=name, fbx="Assets/Models/Props/%s.fbx" % name, solid=solid, shadows=shadows))
        print("  prop %-18s %4d faces" % (name, len(o.data.polygons)))
    counts = {}
    for p_ in PROPS_OUT:
        counts[p_["prop"]] = counts.get(p_["prop"], 0) + 1
    data = {
        "info": "Generated by tools/blender/build_outdoor.py. y is above the ground at (gx, gz); yaw in degrees clockwise from +Z.",
        "propModels": models,
        "props": PROPS_OUT,
        "signs": SIGNS,
    }
    with open(OUT_JSON, "w") as f:
        json.dump(data, f, separators=(",", ":"))
    print("Wrote %s: %d props, %d signs" % (OUT_JSON, len(PROPS_OUT), len(SIGNS)))
    for k in sorted(counts):
        print("  %-18s %d" % (k, counts[k]))


if __name__ == "__main__":
    sys.modules.setdefault("build_outdoor", sys.modules[__name__])
    main()
