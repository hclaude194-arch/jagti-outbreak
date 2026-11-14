#!/usr/bin/env python3
"""Phase 5E: cover for fights in the open. Builds campus-style cover models in headless Blender
(pipe stacks, brick pallets, water tanks, cable drums, cement bags, low garden walls, road
barriers) and places them where the open ground has nothing to hide behind.

Run after build_outdoor.py (and build_loot_points.py), with the Python that has `bpy`:
    python tools/royale/build_cover.py [--preview]   (--render: Cycles picture of the models only)

Writes:
    Assets/Models/Props/Cover_<Name>.fbx   the models (same conventions and materials as the outdoor props)
    Assets/Data/cover_props.json           where each one stands (the outdoor_props.json format);
                                           Unity places them in battle royale builds, before the NavMesh bake
    tools/royale/preview/cover.png         with --preview: the open-ground map before and after

How it places them: every 4 m of open ground inside the campus wall gets the distance to the
nearest hard cover (buildings, tree trunks, solid street furniture). While some open ground is
more than GAP metres from cover, a small works site (two to four pieces) goes at the most exposed
spot that can take one (level, off roads, paths, water, courts and doorways). Long open stretches
of the main roads get a barrier or a cable drum on the verge. Everything is seeded, so a rerun
gives the same campus.
"""
import json
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "blender"))
import build_outdoor as O  # noqa: E402  (imports bpy first)
import build_interiors as I  # noqa: E402
from build_interiors import bx, cyl, obox, new, export_model  # noqa: E402

import numpy as np  # noqa: E402

layout = O.layout
OUT_JSON = os.path.join(O.DATA, "cover_props.json")
PREVIEW = os.path.join(HERE, "preview")
GAP = 20.0            # no open ground further than this from cover, where it can be helped
STEP = 4.0
MAX_SITES = 90
COVER = {}


def cover(name, shadows=True):
    def deco(fn):
        COVER[name] = (fn, shadows)
        return fn
    return deco


# ------------------------------------------------------------------ helpers
def hcyl(m, X, Z, ang, r, length, y, mat, cap="Concrete", seg=14, hollow=None):
    """Horizontal cylinder in prop coordinates: centre (X, y, Z), axis at ang (radians from +X
    towards +Z). hollow = inner radius shows a dark bore on each end."""
    ax, az = math.cos(ang), math.sin(ang)
    px, pz = -az, ax          # horizontal normal to the axis
    h = length / 2

    def pt(s, k, rr):
        a = 2 * math.pi * k / seg
        ox, oy = math.cos(a) * rr, math.sin(a) * rr
        WX, WZ = X + ax * s + px * ox, Z + az * s + pz * ox
        lx, ly = m.b(WX, WZ)
        return (lx, ly, y + oy)

    for k in range(seg):
        a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
        am = (a0 + a1) / 2
        nx, nz = px * math.cos(am), pz * math.cos(am)
        hint = (-nx, -nz, math.sin(am))   # m.b mirrors X and Z
        m.face([pt(-h, k, r), pt(h, k, r), pt(h, k + 1, r), pt(-h, k + 1, r)], mat, hint)
    for s, sgn in ((-h, -1), (h, 1)):
        hint = (-ax * sgn, -az * sgn, 0)
        if hollow:
            for k in range(seg):
                m.face([pt(s, k, r), pt(s, k + 1, r), pt(s, k + 1, hollow), pt(s, k, hollow)], cap, hint)
            m.face([pt(s * 0.92, k, hollow) for k in range(seg)], "Plastic", hint)
        else:
            m.face([pt(s, k, r) for k in range(seg)], cap, hint)


def sack(m, X, Z, ang, y, mat="Linen"):
    """A filled sack, 0.8 x 0.5 x 0.18 m, a little pinched at the ends."""
    obox(m, X, Z, 0.8, 0.5, ang, y, y + 0.05, mat)
    obox(m, X, Z, 0.84, 0.54, ang, y + 0.05, y + 0.13, mat)
    obox(m, X, Z, 0.78, 0.48, ang, y + 0.13, y + 0.18, mat)


def pallet(m, x0, x1, z0, z1):
    """A wooden pallet: three runners and a slatted deck, 0.14 m high."""
    for z in (z0, (z0 + z1) / 2 - 0.05, z1 - 0.1):
        bx(m, x0, x1, z, z + 0.1, 0.0, 0.1, "Wood")
    n = max(3, int((x1 - x0) / 0.18))
    for k in range(n):
        x = x0 + (x1 - x0) * k / n
        bx(m, x, x + (x1 - x0) / n * 0.75, z0, z1, 0.1, 0.14, "Wood")


# ------------------------------------------------------------------ models (front +Z, metres)
@cover("Cover_Pipe_Stack")
def c_pipe_stack():
    """Three 1.2 m concrete drain pipes, 2.5 m long, stacked two and one on timber chocks: full
    cover, 2.3 m high and 2.6 m wide."""
    m = new("Cover_Pipe_Stack")
    for x in (-1.0, 1.0):
        bx(m, x - 0.08, x + 0.08, -1.35, 1.35, 0.0, 0.12, "Wood")
        for z in (-1.3, 1.15):
            bx(m, x - 0.12, x + 0.12, z, z + 0.15, 0.12, 0.26, "Wood")
    for x in (-0.62, 0.62):
        hcyl(m, x, 0.0, math.pi / 2, 0.6, 2.5, 0.72, "Concrete", hollow=0.48)
    hcyl(m, 0.0, 0.0, math.pi / 2, 0.6, 2.5, 1.74, "Concrete", hollow=0.48)
    return m


@cover("Cover_Brick_Stack")
def c_brick_stack():
    """Two pallets of red bricks, 1.2 m high, side by side with a gap: 2.5 x 1.0 m."""
    m = new("Cover_Brick_Stack")
    for x0 in (-1.25, 0.1):
        pallet(m, x0, x0 + 1.15, -0.5, 0.5)
        bx(m, x0 + 0.04, x0 + 1.11, -0.46, 0.46, 0.14, 1.06, "Brick")
        # The top courses stepped down where bricks have been taken.
        bx(m, x0 + 0.04, x0 + 0.75, -0.46, 0.46, 1.06, 1.2, "Brick")
        bx(m, x0 + 0.04, x0 + 0.4, -0.46, 0.1, 1.2, 1.27, "Brick")
    return m


@cover("Cover_Water_Tanks")
def c_water_tanks():
    """Two black 2000-litre water tanks on a brick plinth (as on a hostel roof, waiting to go up):
    1.6 m high, 3.3 x 1.6 m."""
    m = new("Cover_Water_Tanks")
    bx(m, -1.7, 1.7, -0.85, 0.85, 0.0, 0.2, "Brick", top="Concrete")
    for x in (-0.82, 0.82):
        cyl(m, x, 0.0, 0.76, 0.2, 1.62, "Plastic", seg=20)
        for y in (0.45, 0.85, 1.25):
            cyl(m, x, 0.0, 0.785, y, y + 0.06, "Plastic", top=False, seg=20)
        cyl(m, x, 0.0, 0.3, 1.62, 1.7, "Plastic", seg=14)          # lid
        cyl(m, x + 0.45, 0.0, 0.04, 1.5, 1.75, "Steel", seg=8)     # vent pipe
    return m


@cover("Cover_Cable_Drum")
def c_cable_drum():
    """A wooden cable drum on its rim, 1.7 m across and 1.05 m wide, wound with black cable."""
    m = new("Cover_Cable_Drum")
    for z in (-0.47, 0.47):
        hcyl(m, 0.0, z, 0.0 + math.pi / 2, 0.85, 0.1, 0.86, "Wood", cap="Wood", seg=18)
    hcyl(m, 0.0, 0.0, math.pi / 2, 0.62, 0.84, 0.86, "Rubber", cap="Rubber", seg=18)
    hcyl(m, 0.0, 0.0, math.pi / 2, 0.12, 1.2, 0.86, "Steel", cap="Steel", seg=8)
    bx(m, -0.5, 0.5, -0.65, 0.65, 0.0, 0.08, "Wood")     # chocks under the rims
    return m


@cover("Cover_Cement_Bags")
def c_cement_bags():
    """Cement bags stacked on a pallet, about 1 m high, with two bags dropped beside it."""
    m = new("Cover_Cement_Bags")
    pallet(m, -0.85, 0.85, -0.6, 0.6)
    y = 0.14
    for layer in range(5):
        ang = 0.0 if layer % 2 == 0 else math.pi / 2
        offs = ((-0.42, -0.27), (0.42, -0.27), (-0.42, 0.27), (0.42, 0.27)) if ang == 0.0 else \
               ((-0.55, 0.0), (0.0, 0.0), (0.55, 0.0))
        for ox, oz in offs:
            if layer == 4 and ox > 0.3:
                continue
            sack(m, ox, oz, ang, y)
        y += 0.17
    sack(m, 1.3, 0.2, 0.4, 0.0)
    sack(m, 1.15, -0.45, -0.2, 0.0)
    return m


@cover("Cover_Low_Wall")
def c_low_wall():
    """A 4 m length of sandstone garden wall, 1.05 m high with a coping, its east end broken off:
    crouch to hide, stand to shoot."""
    m = new("Cover_Low_Wall")
    bx(m, -2.0, 2.0, -0.3, 0.3, 0.0, 0.12, "Concrete")
    bx(m, -1.95, 1.4, -0.2, 0.2, 0.12, 0.95, "Stone")
    bx(m, -2.02, 1.42, -0.25, 0.25, 0.95, 1.05, "Stone", top="Concrete")
    bx(m, 1.4, 1.95, -0.2, 0.2, 0.12, 0.6, "Stone")              # broken end
    bx(m, 1.4, 1.7, -0.2, 0.2, 0.6, 0.78, "Stone")
    for x in (-1.95, 1.65):
        bx(m, x, x + 0.3, -0.24, 0.24, 0.12, 0.95 if x < 0 else 0.6, "Stone")   # piers
    for x, z, a in ((2.3, 0.4, 0.3), (2.6, -0.2, 1.1), (2.1, -0.5, 0.7)):     # fallen blocks
        obox(m, x, z, 0.4, 0.25, a, 0.0, 0.22, "Stone")
    return m


@cover("Cover_Road_Barrier")
def c_road_barrier():
    """Two 2 m concrete road barriers end to end with yellow and black bands: 0.8 m high,
    a sloped profile, 4.1 m long."""
    m = new("Cover_Road_Barrier")
    for x0 in (-2.05, 0.05):
        x1 = x0 + 2.0
        bx(m, x0, x1, -0.3, 0.3, 0.0, 0.08, "Concrete")
        bx(m, x0, x1, -0.26, 0.26, 0.08, 0.3, "Concrete")
        bx(m, x0, x1, -0.16, 0.16, 0.3, 0.72, "Concrete")
        bx(m, x0, x1, -0.12, 0.12, 0.72, 0.8, "Concrete")
        for k in range(4):
            xa = x0 + 0.1 + k * 0.48
            bx(m, xa, xa + 0.24, -0.165, 0.165, 0.38, 0.62, "Paint_Yellow")
            bx(m, xa + 0.24, xa + 0.48, -0.165, 0.165, 0.38, 0.62, "Plastic")
    return m


# Footprint half-sizes (x, z) used when packing a site; heights for the map key.
SIZE = {"Cover_Pipe_Stack": (1.3, 1.35), "Cover_Brick_Stack": (1.3, 0.55), "Cover_Water_Tanks": (1.7, 0.85),
        "Cover_Cable_Drum": (0.5, 0.9), "Cover_Cement_Bags": (1.5, 0.65), "Cover_Low_Wall": (2.7, 0.6),
        "Cover_Road_Barrier": (2.1, 0.3)}

# Works sites: what goes together.
SITES = [
    ["Cover_Pipe_Stack", "Cover_Cement_Bags"],
    ["Cover_Pipe_Stack", "Cover_Pipe_Stack", "Cover_Cable_Drum"],
    ["Cover_Brick_Stack", "Cover_Brick_Stack", "Cover_Cement_Bags"],
    ["Cover_Brick_Stack", "Cover_Cable_Drum", "Cover_Cement_Bags"],
    ["Cover_Water_Tanks", "Cover_Low_Wall"],
    ["Cover_Low_Wall", "Cover_Low_Wall"],
    ["Cover_Low_Wall", "Cover_Brick_Stack"],
    ["Cover_Water_Tanks", "Cover_Pipe_Stack", "Cover_Cement_Bags"],
]


# ------------------------------------------------------------------ the open-ground map
def grid():
    xs = [p[0] for p in O.BOUNDARY]
    zs = [p[1] for p in O.BOUNDARY]
    gx = np.arange(min(xs) + STEP / 2, max(xs), STEP)
    gz = np.arange(min(zs) + STEP / 2, max(zs), STEP)
    return gx, gz


def hard_cover():
    """Points that stop bullets: building outlines (sampled), tree trunks, solid street furniture."""
    pts = []
    for X0, X1, Z0, Z1 in O.BOX:
        for t in np.arange(0, 1.0001, 2.0 / max(1.0, X1 - X0)):
            pts += [(X0 + (X1 - X0) * t, Z0), (X0 + (X1 - X0) * t, Z1)]
        for t in np.arange(0, 1.0001, 2.0 / max(1.0, Z1 - Z0)):
            pts += [(X0, Z0 + (Z1 - Z0) * t), (X1, Z0 + (Z1 - Z0) * t)]
    trees = O.TER["trees"]
    for k in range(0, len(trees), 4):
        if int(trees[k + 2]) != 3:                 # shrubs hide you but stop nothing
            pts.append((trees[k], trees[k + 1]))
    outdoor = json.load(open(os.path.join(O.DATA, "outdoor_props.json")))
    solid = {m["name"] for m in outdoor["propModels"] if m["solid"]} - {"Fence_Panel", "Sign_Post", "Lamp_Post", "Cycle_Stand"}
    for p in outdoor["props"]:
        if p["prop"] in solid:
            pts.append((p["x"], p["z"]))
    return np.array(pts)


def distance_field(gx, gz, cover_pts):
    """Distance from each grid point to the nearest cover point (brute force in blocks)."""
    X, Z = np.meshgrid(gx, gz)
    P = np.stack([X.ravel(), Z.ravel()], 1)
    lo, hi = P.min(0) - 40, P.max(0) + 40
    c_all = cover_pts[((cover_pts >= lo) & (cover_pts <= hi)).all(1)].astype(np.float32)
    P = P.astype(np.float32)
    d = np.full(len(P), 1e9, np.float32)
    for a in range(0, len(P), 2048):
        p = P[a:a + 2048]
        for k in range(0, len(c_all), 2048):
            c = c_all[k:k + 2048]
            dd = ((p[:, None, :] - c[None, :, :]) ** 2).sum(-1).min(1)
            d[a:a + 2048] = np.minimum(d[a:a + 2048], dd)
    return np.sqrt(d).reshape(X.shape).astype(np.float64)


def open_mask(gx, gz):
    """Grid points that are open ground a fighter can stand on (not buildings, roads, water,
    courts or tracks; inside the wall)."""
    m = np.zeros((len(gz), len(gx)), bool)
    for j, z in enumerate(gz):
        for i, x in enumerate(gx):
            m[j, i] = O.clear((x, z), pad=1.0, road_pad=0.3)
    return m


LOOT = [(p["x"], p["z"]) for p in json.load(open(os.path.join(O.DATA, "loot_points.json")))["points"]]


def site_ok(P, radius):
    """A works site of the given radius fits at P: open, level, off paths, away from doors and loot."""
    x, z = P
    if not O.clear(P, pad=4.0, road_pad=2.5):
        return False
    for a in layout["areas"]:
        if a["type"] in ("parking",) and O.in_area(P, a, 2.0):
            return False
    hs = [O.ground(x + dx, z + dz) for dx in (-radius, 0, radius) for dz in (-radius, 0, radius)]
    if max(hs) - min(hs) > 1.2:
        return False
    for k in range(8):
        a = k * math.pi / 4
        if not O.clear((x + math.cos(a) * radius, z + math.sin(a) * radius), pad=2.0, road_pad=1.5):
            return False
    if any(math.hypot(x - lx, z - lz) < radius + 2.0 for lx, lz in LOOT):
        return False
    return True


PLACED = []


def put(name, X, Z, yaw):
    PLACED.append(dict(prop=name, x=round(X, 3), z=round(Z, 3), y=0.0, yaw=round(yaw % 360.0, 2),
                       gx=round(X, 3), gz=round(Z, 3)))


def lay_site(P, kinds, rng):
    """Sets the pieces of a site round P, facing different ways so it covers several directions."""
    base = rng.uniform(0, 360)
    n = len(kinds)
    out = []
    for k, name in enumerate(kinds):
        if n == 1:
            off = (0.0, 0.0)
        else:
            a = math.radians(base + k * 360.0 / n + rng.uniform(-20, 20))
            r = 2.6 + rng.uniform(0, 1.2)
            off = (math.cos(a) * r, math.sin(a) * r)
        yaw = base + k * 360.0 / n + 90 + rng.uniform(-25, 25)
        out.append((name, P[0] + off[0], P[1] + off[1], yaw))
    # Keep the pieces from overlapping (circles from their footprints).
    for i in range(len(out)):
        for j in range(i):
            ri = math.hypot(*SIZE[out[i][0]])
            rj = math.hypot(*SIZE[out[j][0]])
            dx, dz = out[i][1] - out[j][1], out[i][2] - out[j][2]
            d = math.hypot(dx, dz)
            if d < (ri + rj) * 0.8:
                push = (ri + rj) * 0.8 - d + 0.1
                ux, uz = (dx / d, dz / d) if d > 1e-3 else (1.0, 0.0)
                out[i] = (out[i][0], out[i][1] + ux * push, out[i][2] + uz * push, out[i][3])
    return out


def place_sites(gx, gz, field, mask, rng):
    sites = 0
    cover_pts = []
    blocked = np.zeros_like(mask)
    while sites < MAX_SITES:
        d = np.where(mask & ~blocked, field, -1)
        j, i = np.unravel_index(np.argmax(d), d.shape)
        if d[j, i] < GAP:
            break
        P = (float(gx[i]), float(gz[j]))
        blocked[j, i] = True
        if site_ok(P, 5.5):
            kinds = rng.choice(SITES)
        elif site_ok(P, 2.8):          # no room for a works site: one piece on its own
            kinds = [rng.choice(["Cover_Low_Wall", "Cover_Cable_Drum", "Cover_Cement_Bags", "Cover_Brick_Stack"])]
        else:
            continue
        pieces = lay_site(P, kinds, rng)
        if not all(O.clear((x, z), pad=2.0, road_pad=1.5) for _, x, z, _ in pieces):
            continue
        for name, x, z, yaw in pieces:
            put(name, x, z, yaw)
            cover_pts.append((x, z))
        sites += 1
        # Update the distances near the new site.
        X, Z = np.meshgrid(gx, gz)
        field = np.minimum(field, np.sqrt((X - P[0]) ** 2 + (Z - P[1]) ** 2))
    return sites, field


def road_verges(rng):
    """Barriers or drums on the verges of long open stretches of the main roads."""
    count = 0
    cov = np.array([(p["x"], p["z"]) for p in PLACED] + [tuple(c) for c in HARD])
    for r, poly in O.ROAD_PTS:
        if O.is_path(r) or r["width"] < 5:
            continue
        side = 1
        for (P, t, dist) in O.walk(poly, 30.0, start=15.0):
            nx, nz = t[1], -t[0]
            off = r["width"] / 2 + 1.6
            Q = (P[0] + nx * off * side, P[1] + nz * off * side)
            if len(cov) and np.sqrt(((cov - np.array(Q)) ** 2).sum(1)).min() < 24.0:
                continue
            if not O.clear(Q, pad=3.0, road_pad=0.4) or any(math.hypot(Q[0] - lx, Q[1] - lz) < 4 for lx, lz in LOOT):
                continue
            # Not on the far side of a water channel beside the road, nor on a steep verge.
            if abs(O.ground(*Q) - O.ground(*P)) > 0.8:
                continue
            name = "Cover_Road_Barrier" if rng.random() < 0.65 else "Cover_Cable_Drum"
            yaw = O.yaw_to(t[0], t[1]) + 90.0 if name == "Cover_Road_Barrier" else rng.uniform(0, 360)
            put(name, Q[0], Q[1], yaw)
            cov = np.vstack([cov, np.array([Q])])
            count += 1
            side = -side
    return count


HARD = hard_cover()


def write_png(path, rgb):
    """Minimal PNG writer (numpy + zlib), as Blender's Python has no imaging library."""
    import struct
    import zlib
    h, w, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[y].astype(np.uint8).tobytes() for y in range(h))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))


def preview(gx, gz, before, after, mask):
    """Two maps side by side: left before, right after. Grey = not open ground, green = within GAP
    of cover, orange to red = further; yellow dots = placed cover, blue = buildings."""
    S = 3

    def heat(field):
        img = np.zeros((len(gz), len(gx), 3))
        far = np.clip((field - GAP) / 20.0, 0, 1)
        img[...] = (70, 70, 72)
        open_near = mask & (field <= GAP)
        open_far = mask & (field > GAP)
        img[open_near] = (70, 140, 70)
        img[open_far] = np.stack([235 + 0 * far[open_far], 150 - 110 * far[open_far], 50 + 0 * far[open_far]], 1)
        return img

    left, right = heat(before), heat(after)
    for X0, X1, Z0, Z1 in O.BOX:
        for img in (left, right):
            i0, i1 = np.searchsorted(gx, [X0, X1])
            j0, j1 = np.searchsorted(gz, [Z0, Z1])
            img[j0:j1, i0:i1] = (60, 90, 140)
    for p in PLACED:
        i, j = int(np.searchsorted(gx, p["x"])), int(np.searchsorted(gz, p["z"]))
        right[max(0, j - 1):j + 1, max(0, i - 1):i + 1] = (250, 225, 60)
    gap = np.full((len(gz), 4, 3), 25.0)
    both = np.concatenate([left, gap, right], 1)[::-1]          # north up
    both = np.repeat(np.repeat(both, S, 0), S, 1)
    os.makedirs(PREVIEW, exist_ok=True)
    write_png(os.path.join(PREVIEW, "cover.png"), both)


def render_models():
    """All the cover models in a row on the ground, rendered with Cycles to preview/cover_models.png."""
    import bpy
    O.B.clear_scene()
    names = sorted(COVER)
    x = 0.0
    for name in names:
        o = COVER[name][0]().to_object()
        w = SIZE[name][0]
        o.location = (x + w, 0.0, 0.0)
        x += 2 * w + 1.2
    bpy.ops.mesh.primitive_plane_add(size=80, location=(x / 2, 0, 0))
    g = bpy.context.object
    gm = bpy.data.materials.new("ground"); gm.diffuse_color = (0.42, 0.45, 0.3, 1)
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.36, 0.4, 0.24, 1)
    g.data.materials.append(gm)
    for o in bpy.context.scene.objects:
        if o.type == "MESH" and o is not g:
            for slot in o.material_slots:
                m = slot.material
                if m is None:
                    continue
                m.use_nodes = True
                m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = m.diffuse_color
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"; sc.cycles.samples = 48
    sc.render.resolution_x, sc.render.resolution_y = 2000, 560
    world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.7, 0.76, 0.82, 1)
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5; sun.angle = 0.1
    so = bpy.data.objects.new("sun", sun); so.rotation_euler = (math.radians(50), math.radians(8), math.radians(35))
    sc.collection.objects.link(so)
    cam = bpy.data.cameras.new("cam"); cam.lens = 30
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (x / 2 - 0.6, -21.0, 6.5); co.rotation_euler = (math.radians(75), 0, 0)
    os.makedirs(PREVIEW, exist_ok=True)
    sc.render.filepath = os.path.join(PREVIEW, "cover_models.png")
    bpy.ops.render.render(write_still=True)
    print("rendered", names)


def main():
    if "--render" in sys.argv:
        render_models()
        return
    rng = random.Random(51)
    gx, gz = grid()
    mask = open_mask(gx, gz)
    before = distance_field(gx, gz, HARD)
    sites, after = place_sites(gx, gz, before.copy(), mask, rng)
    verges = road_verges(rng)
    exposed_before = int(((before > GAP) & mask).sum())
    exposed_after = int(((after > GAP) & mask).sum())
    print("Open ground cells: %d; more than %d m from cover: %d before, %d after (%d sites, %d verge pieces)"
          % (int(mask.sum()), GAP, exposed_before, exposed_after, sites, verges))
    if "--preview" in sys.argv:
        preview(gx, gz, before, after, mask)
    if "--no-models" not in sys.argv:
        os.makedirs(I.PROP_DIR, exist_ok=True)
    models = []
    for name in sorted(COVER):
        fn, shadows = COVER[name]
        path = os.path.join(I.PROP_DIR, name + ".fbx")
        if "--no-models" not in sys.argv:
            o = export_model(fn(), path)
            print("  %-20s %4d faces" % (name, len(o.data.polygons)))
        models.append(dict(name=name, fbx="Assets/Models/Props/%s.fbx" % name, solid=True, shadows=shadows))
    counts = {}
    for p in PLACED:
        counts[p["prop"]] = counts.get(p["prop"], 0) + 1
    data = {
        "info": "Generated by tools/royale/build_cover.py. y is above the ground at (gx, gz); yaw in degrees clockwise from +Z.",
        "propModels": models,
        "props": PLACED,
        "signs": [],
    }
    with open(OUT_JSON, "w") as f:
        json.dump(data, f, separators=(",", ":"))
    print("Wrote %s: %d pieces" % (OUT_JSON, len(PLACED)), counts)


if __name__ == "__main__":
    main()
