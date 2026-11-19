#!/usr/bin/env python3
"""Generate the Jagti terrain: heightmap, ground paint, grass and trees.

Reads the campus geometry from generate_layout.py and writes:
  Assets/Data/terrain_height.bytes  1025 x 1025 uint16, row 0 = south edge (min Z)
  Assets/Data/terrain_splat.bytes   512 x 512 x 4 uint8 (grass, dry grass, soil, rock)
  Assets/Data/terrain_grass.bytes   1024 x 1024 uint8 grass density 0..255
  Assets/Data/campus_terrain.json   terrain size, offsets and tree list

The shape is a design, not survey data, modelled on the Shivalik foothills: the
campus sits on a rolling ridge 34 to 48 m above the highway, the gate is low and
the serpentine drive climbs about 30 m, steep wooded ravines drop 40 to 55 m
outside the boundary wall, and ridges of 80 to 140 m close the horizon. Roads
and building plots are cut into the ground as level terraces.

Run:  python3 tools/layout/generate_terrain.py   (after generate_layout.py)
Preview images go to tools/layout/preview/ (not used by Unity).
"""
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import generate_layout as L  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "Assets", "Data")

RES = 1025            # heightmap samples per side
SIZE = 2048.0         # metres per side (2 m per sample)
MIN_X, MIN_Z = -1000.0, -1044.0
CELL = SIZE / (RES - 1)
H_MIN, H_RANGE = -60.0, 240.0  # heights stored relative to this range
SPLAT_RES = 1024
GRASS_RES = 1024
SEED = 7

rng = np.random.default_rng(SEED)

xs = MIN_X + np.arange(RES) * CELL
zs = MIN_Z + np.arange(RES) * CELL
X, Z = np.meshgrid(xs, zs)  # [row = z, col = x]


# ------------------------------------------------------------------ helpers
def smoothstep(e0, e1, v):
    t = np.clip((v - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def value_noise(res, cells, seed):
    """Smooth value noise in [-1, 1] on a res x res grid with `cells` lattice cells."""
    r = np.random.default_rng(seed)
    lattice = r.uniform(-1, 1, (cells + 2, cells + 2))
    t = np.linspace(0, cells, res, endpoint=False)
    i = t.astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    a = lattice[np.ix_(i, i)]
    b = lattice[np.ix_(i, i + 1)]
    c = lattice[np.ix_(i + 1, i)]
    d = lattice[np.ix_(i + 1, i + 1)]
    fy, fx = f[:, None], f[None, :]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(res, base_cells, octaves, seed, gain=0.5):
    total, amp, norm = np.zeros((res, res)), 1.0, 0.0
    for o in range(octaves):
        total += amp * value_noise(res, base_cells * 2 ** o, seed + o * 101)
        norm += amp
        amp *= gain
    return total / norm


def blur(a, passes=1):
    for _ in range(passes):
        p = np.pad(a, 1, mode="edge")
        a = (p[:-2, :-2] + p[:-2, 1:-1] + p[:-2, 2:] + p[1:-1, :-2] + p[1:-1, 1:-1] +
             p[1:-1, 2:] + p[2:, :-2] + p[2:, 1:-1] + p[2:, 2:]) / 9.0
    return a


def world_poly(points):
    return [L.to_world(x, y) for x, y in points]


def polygon_signed_distance(px, pz, poly):
    """Distance to polygon edges; negative inside."""
    n = len(poly)
    dist = np.full(px.shape, np.inf)
    inside = np.zeros(px.shape, dtype=bool)
    for k in range(n):
        ax, az = poly[k]
        bx, bz = poly[(k + 1) % n]
        ex, ez = bx - ax, bz - az
        t = np.clip(((px - ax) * ex + (pz - az) * ez) / (ex * ex + ez * ez), 0, 1)
        dx, dz = px - (ax + t * ex), pz - (az + t * ez)
        dist = np.minimum(dist, np.sqrt(dx * dx + dz * dz))
        cond = ((az > pz) != (bz > pz)) & (px < (bx - ax) * (pz - az) / (bz - az + 1e-9) + ax)
        inside ^= cond
    return np.where(inside, -dist, dist)


def sample(h, x, z):
    """Bilinear height at world x, z (arrays or scalars)."""
    fx = np.clip((np.asarray(x) - MIN_X) / CELL, 0, RES - 1.001)
    fz = np.clip((np.asarray(z) - MIN_Z) / CELL, 0, RES - 1.001)
    i, j = fz.astype(int), fx.astype(int)
    u, v = fx - j, fz - i
    return (h[i, j] * (1 - u) * (1 - v) + h[i, j + 1] * u * (1 - v) +
            h[i + 1, j] * (1 - u) * v + h[i + 1, j + 1] * u * v)


def window(cx, cz, radius):
    """Row/column slice of the heightmap covering a square around a point."""
    j0 = max(0, int((cx - radius - MIN_X) / CELL))
    j1 = min(RES, int((cx + radius - MIN_X) / CELL) + 2)
    i0 = max(0, int((cz - radius - MIN_Z) / CELL))
    i1 = min(RES, int((cz + radius - MIN_Z) / CELL) + 2)
    return slice(i0, i1), slice(j0, j1)


def densify(points, step):
    out = [points[0]]
    for (ax, az), (bx, bz) in zip(points[:-1], points[1:]):
        n = max(1, int(math.hypot(bx - ax, bz - az) / step))
        for k in range(1, n + 1):
            out.append((ax + (bx - ax) * k / n, az + (bz - az) * k / n))
    return out


# ------------------------------------------------------------------ geometry in world space
boundary = world_poly(L.BOUNDARY)
roads = []
for name, width, pts, smooth in L.ROADS:
    p = L.catmull(pts) if smooth else pts
    roads.append((name, width, densify(world_poly(p), 2.0)))

pads = []  # (group, cx, cz, half_w, half_d, ellipse)
for name, zone, x, y, w, d, floors, shape in L.BUILDINGS:
    cx, cz = L.to_world(x + w / 2, y + d / 2)
    if zone == "academic" and not name.startswith("Brahmaputra"):
        group = "academic"
    else:
        group = name.split(" - ")[0]
    pads.append((group, cx, cz, w / 2, d / 2, False))
FLAT_AREAS = {"parking", "court", "track", "field", "amphitheatre", "substation", "courtyard"}
for typ, shape, x, y, w, d in L.AREAS:
    if typ not in FLAT_AREAS:
        continue
    if shape == "box":
        cx, cz = L.to_world(x + w / 2, y + d / 2)
    else:
        cx, cz = L.to_world(x, y)
    group = "sports" if typ in ("track", "field") else ("academic" if typ == "courtyard" else "area-%d-%d" % (x, y))
    pads.append((group, cx, cz, w / 2, d / 2, shape == "ellipse"))

highway = next(r for r in roads if r[0] == "Highway")[2]
hw_x = np.array([p[0] for p in highway])
hw_z = np.array([p[1] for p in highway])
order = np.argsort(hw_x)
hw_x, hw_z = hw_x[order], hw_z[order]

# ------------------------------------------------------------------ base shape
def pad_distance(px, pz, cx, cz, hw, hd, ellipse):
    if ellipse:
        nx, nz = (px - cx) / hw, (pz - cz) / hd
        r = np.sqrt(nx * nx + nz * nz)
        return np.maximum(r - 1.0, 0.0) * min(hw, hd)
    dx = np.maximum(np.abs(px - cx) - hw, 0.0)
    dz = np.maximum(np.abs(pz - cz) - hd, 0.0)
    return np.sqrt(dx * dx + dz * dz)


sd = polygon_signed_distance(X, Z, boundary)          # negative inside the wall


def polyline_distance(px, pz, pts, reach):
    """Distance from grid points to a polyline, only computed within `reach` of it."""
    best = np.full(px.shape, np.inf)
    p = np.array(pts)
    for k in range(len(p) - 1):
        (ax, az), (bx, bz) = p[k], p[k + 1]
        rows, cols = window((ax + bx) / 2, (az + bz) / 2, math.hypot(bx - ax, bz - az) / 2 + reach)
        qx, qz = px[rows, cols], pz[rows, cols]
        ex, ez = bx - ax, bz - az
        t = np.clip(((qx - ax) * ex + (qz - az) * ez) / max(ex * ex + ez * ez, 1e-6), 0, 1)
        d = np.hypot(qx - (ax + t * ex), qz - (az + t * ez))
        best[rows, cols] = np.minimum(best[rows, cols], d)
    return best


# Campus roads that run out past the wall keep a spur of high ground under them,
# so they leave along a ridge instead of plunging into a ravine.
campus_road_dist = np.full(X.shape, np.inf)
for name, width, pts in roads:
    if name != "Highway":
        campus_road_dist = np.minimum(campus_road_dist, polyline_distance(X, Z, pts, 120.0) - width / 2 - 6.0)
outside = np.maximum(np.minimum(np.maximum(sd, 0.0), np.maximum(campus_road_dist, 0.0)), 0.0)

n_low = fbm(RES, 8, 4, SEED + 1)
n_mid = fbm(RES, 24, 4, SEED + 2)
n_hills = fbm(RES, 5, 5, SEED + 3)
n_ravine = fbm(RES, 14, 3, SEED + 4)
n_ridge = fbm(RES, 7, 5, SEED + 9)

# Ridge top: 34 m at the academic block, climbing to about 48 m at Sindhu Hostel,
# with knolls and dips of up to about 8 m either way.
# Campus levels from the owner's description of the real site: the sports complex,
# courts and track are the low part; the ground climbs to the mess and shopping area;
# the academic block sits a little higher again. The boys' (Sindhu) and girls' hostels
# share the low level, and uphill ground rings the girls' hostel up to mess level.
# Anchors are (map x, map y, height m); heights blend smoothly between them.
LEVEL_ANCHORS = [
    # academic zone (highest)
    (490, 700, 46), (430, 640, 45), (560, 760, 46), (390, 800, 44),
    # central zone: car park, walled residence, central roundabout
    (405, 880, 43), (330, 930, 43), (414, 996, 42), (520, 950, 42), (300, 990, 42),
    # mess, shopping & medical, staff quarters (mess level)
    (385, 1090, 40), (345, 1050, 40), (280, 1060, 40), (280, 1130, 39),
    # girls' hostel blocks east of the mess (low, in a bowl)
    (495, 1100, 29), (455, 1150, 29), (450, 1180, 29),
    # uphill east of the girls' hostel, back up to mess level
    (600, 1080, 40), (620, 1020, 41), (590, 1150, 38),
    # sports courts, track, sports complex (low)
    (517, 1261, 29), (453, 1240, 29), (450, 1320, 29), (590, 1290, 30), (560, 1200, 30),
    # Sindhu Hostel (boys) and the service yard: same low level
    (460, 1450, 29), (400, 1515, 29), (330, 1420, 30), (300, 1340, 31), (470, 1540, 29),
]
level_num = np.zeros(X.shape)
level_den = np.zeros(X.shape)
for mx, my, lh in LEVEL_ANCHORS:
    ax, az = L.to_world(mx, my)
    w = np.exp(-((X - ax) ** 2 + (Z - az) ** 2) / (2 * 55.0 ** 2)) + 1e-9
    level_num += w * lh
    level_den += w
terraces = level_num / level_den
plateau = terraces + 3.0 * n_low + 1.5 * n_mid
# The steep wooded hill right behind the basketball courts, across the spine road.
hx, hz = L.to_world(375, 1250)
plateau += 26.0 * np.exp(-((X - hx) ** 2 + (Z - hz) ** 2) / (2 * 30.0 ** 2))
hill_mask = np.exp(-((X - hx) ** 2 + (Z - hz) ** 2) / (2 * 34.0 ** 2))
# Hillocks in the open ground between roads and buildings, up to about 9 m high.
dev_dist = campus_road_dist.copy()
for g, cx, cz, hw, hd, ell in pads:
    rows, cols = window(cx, cz, max(hw, hd) + 60)
    dd = pad_distance(X[rows, cols], Z[rows, cols], cx, cz, hw, hd, ell)
    dev_dist[rows, cols] = np.minimum(dev_dist[rows, cols], dd)
n_knoll = fbm(RES, 20, 3, SEED + 10)
plateau += 6.0 * np.clip(n_knoll + 0.2, 0, None) * smoothstep(12.0, 45.0, dev_dist) * (1 - hill_mask)
# The mess terrace and the faculty terrace above it (owner, 2026-11-18): the mess, shopping and
# medical blocks stand about four storeys (12 m) above the girls' hostels, and the faculty
# residences a little higher again. The rise starts at the spine road coming up from the sports
# complex: a steep bank along the road's west side, with the roundabout end level with the
# terrace. (world x0, x1, z0, z1, height m, ramp east-west m, ramp north-south m), applied in
# order, so the faculty terrace's ramp makes the step down to the mess terrace; the mess
# terrace's south end is a long grassy bank you can walk down.
TERRACE_PADS = [
    (-64.0, -5.0, -162.0, -8.0, 43.0, 7.0, 24.0),
    (-156.0, -64.0, -186.0, -4.0, 46.0, 8.0, 16.0),
]
for x0, x1, z0, z1, th, ramp, ramp_z in TERRACE_PADS:
    ddx = np.maximum(np.maximum(x0 - X, X - x1), 0.0)
    ddz = np.maximum(np.maximum(z0 - Z, Z - z1), 0.0) * (ramp / ramp_z)
    w = 1.0 - smoothstep(0.0, ramp, np.sqrt(ddx * ddx + ddz * ddz))
    plateau = plateau * (1 - w) + (th + 0.4 * n_mid) * w
# The mess terrace ends at a stone retaining wall beside the spine road (build_outdoor.py puts
# the wall on this line); the terrace is put back up to it after the road is flattened.
RETAINING_X = -2.0
mess_terrace = (X >= TERRACE_PADS[0][0]) & (X <= RETAINING_X) & (Z >= TERRACE_PADS[0][2]) & (Z <= TERRACE_PADS[0][3])
mess_terrace_h = TERRACE_PADS[0][4] + 0.4 * n_mid

# Gate slope: the serpentine drive climbs from the gate (about 3 m) up to the ridge.
gate_ramp = smoothstep(380.0, 528.0, Z)
plateau = plateau * (1 - gate_ramp) + 3.0 * gate_ramp

# Distance south of the highway, which runs along the valley floor.
hwz_at_x = np.interp(X, hw_x, hw_z)
north = Z - hwz_at_x
valley = smoothstep(260.0, 30.0, -north)  # 1 near the highway, 0 far south of it

# Outside the wall: a short shoulder, then a steep drop into ravines cut by stream channels.
channels = (1 - np.abs(n_ravine)) ** 6
ravine_floor = -2.0 + 6.0 * n_mid - 16.0 * channels * smoothstep(20, 90, outside)
drop = smoothstep(6.0, 80.0, outside) ** 0.8
height = plateau * (1 - drop) + ravine_floor * drop

# Across the ravines: wooded spurs a little below the campus ridge, then sharp
# foothill ridges rising to 80-140 m that close the horizon.
ridges = (1 - np.abs(n_ridge)) ** 2.2
near_hills = (14.0 + 22.0 * ridges + 10.0 * n_hills) * smoothstep(90.0, 260.0, outside)
far_ridges = (55.0 + 75.0 * ridges + 20.0 * n_hills) * smoothstep(380.0, 850.0, outside)
hills = near_hills + far_ridges
height += hills * (1 - 0.85 * valley)

# North of the highway: the flat valley floor with fields, and hills again in the distance.
farm = 0.5 + 1.0 * n_mid + (40.0 + 50.0 * ridges) * smoothstep(380.0, 800.0, north)
farm_mask = smoothstep(-30.0, 20.0, north)
height = height * (1 - farm_mask) + farm * farm_mask
height += 0.6 * fbm(RES, 80, 2, SEED + 5)  # small bumps everywhere
height = blur(height, 1)


# ------------------------------------------------------------------ flatten plots and roads
def flatten_pads(h, margin, blend, heights=None):
    if heights is None:
        groups = {}
        for g, cx, cz, hw, hd, ell in pads:
            rows, cols = window(cx, cz, max(hw, hd))
            d = pad_distance(X[rows, cols], Z[rows, cols], cx, cz, hw, hd, ell)
            groups.setdefault(g, []).append(h[rows, cols][d <= 0])
        heights = {g: float(np.mean(np.concatenate(v))) for g, v in groups.items()}
    for g, cx, cz, hw, hd, ell in pads:
        rows, cols = window(cx, cz, max(hw, hd) + margin + blend)
        d = pad_distance(X[rows, cols], Z[rows, cols], cx, cz, hw, hd, ell)
        w = 1.0 - smoothstep(margin, margin + blend, d)
        h[rows, cols] = h[rows, cols] * (1 - w) + heights[g] * w
    return heights


def smooth_profile(values, window_pts):
    v = np.asarray(values, dtype=float)
    k = np.ones(window_pts) / window_pts
    for _ in range(3):
        p = np.pad(v, window_pts // 2, mode="edge")
        v = np.convolve(p, k, mode="valid")[: len(values)]
    return v


def flatten_roads(h):
    best_d = np.full(h.shape, np.inf)
    best_h = np.zeros(h.shape)
    best_w = np.zeros(h.shape)
    shoulder = 9.0
    for name, width, pts in roads:
        p = np.array(pts)
        window_pts = {"Highway": 25, "Serpentine Drive": 40}.get(name, 15)
        prof = smooth_profile(sample(h, p[:, 0], p[:, 1]), window_pts)
        reach = width / 2 + 1.0 + shoulder
        for k in range(len(p) - 1):
            (ax, az), (bx, bz) = p[k], p[k + 1]
            rows, cols = window((ax + bx) / 2, (az + bz) / 2, math.hypot(bx - ax, bz - az) / 2 + reach)
            px, pz = X[rows, cols], Z[rows, cols]
            ex, ez = bx - ax, bz - az
            t = np.clip(((px - ax) * ex + (pz - az) * ez) / max(ex * ex + ez * ez, 1e-6), 0, 1)
            dx, dz = px - (ax + t * ex), pz - (az + t * ez)
            d = np.sqrt(dx * dx + dz * dz) - width / 2
            rh = prof[k] * (1 - t) + prof[k + 1] * t
            sub_d, sub_h, sub_w = best_d[rows, cols], best_h[rows, cols], best_w[rows, cols]
            closer = d < sub_d
            sub_d[closer], sub_h[closer], sub_w[closer] = d[closer], rh[closer], width
    w = 1.0 - smoothstep(1.0, 1.0 + shoulder, best_d)
    h[:] = h * (1 - w) + best_h * w
    return best_d


pad_heights = flatten_pads(height, 3.0, 12.0)
road_dist = flatten_roads(height)
flatten_pads(height, 1.0, 3.0, pad_heights)
height[mess_terrace] = np.maximum(height[mess_terrace], mess_terrace_h[mess_terrace])

# Terraced lawns from the campus model: level steps cut into the ground, each riser a
# 3.5 m ramp so the player can still walk up it. Kept clear of the wall's inner edge.
for x0, y0, x1, y1, drop, lines in L.TERRACES:
    wx0, wz1 = L.to_world(x0, y0)
    wx1, wz0 = L.to_world(x1, y1)
    rows, cols = window((wx0 + wx1) / 2, (wz0 + wz1) / 2, max(wx1 - wx0, wz1 - wz0) / 2 + 10)
    px, pz = X[rows, cols], Z[rows, cols]
    top = float(sample(height, np.array([wx1 + 4.0]), np.array([(wz0 + wz1) / 2]))[0])
    steps = np.zeros(px.shape)
    for pts in lines:
        w = np.array([L.to_world(x, y) for x, y in pts])      # world X, Z; Z falls along the line
        lx = np.interp(-pz, -w[:, 1], w[:, 0])               # the line's X on each grid row
        fade = 1 - smoothstep(0.0, 8.0, np.maximum(w[:, 1].min() - pz, pz - w[:, 1].max()))
        steps += smoothstep(0.0, 3.5, lx - px + 3.5) * fade  # one step down west of the line, after a 3.5 m ramp
    level = top - drop * steps
    inside = (1 - smoothstep(0.0, 6.0, np.maximum.reduce([wx0 - px, px - wx1, wz0 - pz, pz - wz1])))
    inside *= smoothstep(4.0, 10.0, -sd[rows, cols])
    # Keep building plots and hard areas at their own level.
    for g, cx, cz, hw, hd, ell in pads:
        if abs(cx - (wx0 + wx1) / 2) > (wx1 - wx0) / 2 + hw + 12 or abs(cz - (wz0 + wz1) / 2) > (wz1 - wz0) / 2 + hd + 12:
            continue
        d = pad_distance(px, pz, cx, cz, hw, hd, ell)
        inside *= smoothstep(2.0, 8.0, d)
    height[rows, cols] = height[rows, cols] * (1 - inside) + level * inside

height = np.clip(height, H_MIN + 0.5, H_MIN + H_RANGE - 0.5)

# ------------------------------------------------------------------ write heightmap
os.makedirs(DATA, exist_ok=True)
h16 = np.round((height - H_MIN) / H_RANGE * 65535).astype("<u2")
h16.tofile(os.path.join(DATA, "terrain_height.bytes"))


# ------------------------------------------------------------------ masks shared by paint, grass and trees
def resample(a, res):
    idx = np.linspace(0, RES - 1, res).round().astype(int)
    return a[np.ix_(idx, idx)]


gz, gx = np.gradient(height, CELL)
slope = np.degrees(np.arctan(np.sqrt(gx * gx + gz * gz)))

pad_mask = np.zeros(height.shape)     # 1 on buildings and hard areas
lawn_mask = np.zeros(height.shape)    # 1 on lawns, courtyard, field, roundabouts
for g, cx, cz, hw, hd, ell in pads:
    rows, cols = window(cx, cz, max(hw, hd) + 4)
    d = pad_distance(X[rows, cols], Z[rows, cols], cx, cz, hw, hd, ell)
    pad_mask[rows, cols] = np.maximum(pad_mask[rows, cols], 1.0 - smoothstep(0.0, 3.0, d))
for typ, shape, x, y, w, d in L.AREAS:
    if typ not in ("lawn", "courtyard", "field", "roundabout"):
        continue
    if shape == "box":
        cx, cz = L.to_world(x + w / 2, y + d / 2)
    else:
        cx, cz = L.to_world(x, y)
    rows, cols = window(cx, cz, max(w, d) / 2 + 4)
    dd = pad_distance(X[rows, cols], Z[rows, cols], cx, cz, w / 2, d / 2, shape == "ellipse")
    lawn_mask[rows, cols] = np.maximum(lawn_mask[rows, cols], 1.0 - smoothstep(0.0, 2.0, dd))
inside = sd < 0
farm_zone = farm_mask > 0.6
n_patch = fbm(RES, 30, 3, SEED + 6)
n_fine = fbm(RES, 90, 2, SEED + 7)

# ------------------------------------------------------------------ ground paint
grass = np.zeros(height.shape)
dry = np.zeros(height.shape)
soil = np.zeros(height.shape)
rock = np.zeros(height.shape)

# Wild ground: dry grass with green patches and bare soil.
dry += 0.65 + 0.35 * n_patch
grass += np.clip(0.25 - 0.6 * n_patch, 0, 1) + 0.15 * inside
# Inside the campus wall the ground is kept green, as on the campus model.
grass += 0.9 * inside
dry *= 1 - 0.6 * inside
soil += np.clip(0.3 * n_fine + 0.15 * (n_patch > 0.35), 0, 1)
# Farmland: a patchwork of crop, ploughed and fallow fields.
ang = math.radians(14)
rx, rz = X * math.cos(ang) - Z * math.sin(ang), X * math.sin(ang) + Z * math.cos(ang)
field_table = np.random.default_rng(SEED + 8).random((400, 400))
crop = field_table[(np.floor((rz + 3000) / 26) % 400).astype(int), (np.floor((rx + 3000) / 47) % 400).astype(int)]
fz = farm_zone * smoothstep(0.6, 0.9, farm_mask) * (1 - smoothstep(330.0, 520.0, north)) * (1 - smoothstep(8, 14, slope))
grass = grass * (1 - fz) + fz * (crop < 0.45) * 1.0
soil = soil * (1 - fz) + fz * ((crop >= 0.45) & (crop < 0.75)) * 1.0
dry = dry * (1 - fz) + fz * (crop >= 0.75) * 1.0
# Lawns are green and kept.
grass = grass * (1 - lawn_mask) + 1.6 * lawn_mask
dry *= 1 - 0.85 * lawn_mask
soil *= 1 - lawn_mask
# Bare soil on building plots and along road edges.
verge = 1.0 - smoothstep(0.0, 2.5, road_dist)
soil = np.maximum(soil, np.maximum(pad_mask * (1 - lawn_mask), verge))
grass *= 1 - 0.8 * verge
dry *= 1 - 0.8 * verge
# Steep ground: soil, then rock.
steep_soil = smoothstep(16, 26, slope)
steep_rock = smoothstep(26, 36, slope)
soil = soil * (1 - steep_soil) + steep_soil
rock = steep_rock * 1.5
grass *= 1 - steep_soil
dry *= 1 - 0.7 * steep_soil

stack = np.stack([grass, dry, soil, rock], axis=-1)
stack = np.maximum(stack, 0)
stack /= stack.sum(axis=-1, keepdims=True) + 1e-6
splat = np.stack([resample(stack[:, :, k], SPLAT_RES) for k in range(4)], axis=-1)
(np.round(splat * 255).astype(np.uint8)).tofile(os.path.join(DATA, "terrain_splat.bytes"))

# ------------------------------------------------------------------ grass tufts
grass_density = stack[:, :, 0] + 0.7 * stack[:, :, 1]
grass_density *= 1 - smoothstep(0.0, 0.5, pad_mask)
grass_density *= 1 - verge
grass_density *= 0.55 + 0.45 * n_fine
gd = resample(np.clip(grass_density, 0, 1), GRASS_RES)
np.round(gd * 255).astype(np.uint8).tofile(os.path.join(DATA, "terrain_grass.bytes"))

# ------------------------------------------------------------------ trees
# Types: 0 broadleaf (neem / sheesham), 1 eucalyptus, 2 chir pine, 3 shrub
BROAD, EUCA, PINE, SHRUB = 0, 1, 2, 3
trees = []


def road_dist_at(x, z):
    i = int(round((z - MIN_Z) / CELL))
    j = int(round((x - MIN_X) / CELL))
    if not (0 <= i < RES and 0 <= j < RES):
        return 0.0
    return float(road_dist[i, j])


def clear_at(x, z, gap):
    """True when the point is clear of roads (plus gap), plots and hard areas."""
    i = int(round((z - MIN_Z) / CELL))
    j = int(round((x - MIN_X) / CELL))
    if not (0 <= i < RES and 0 <= j < RES):
        return False
    return road_dist[i, j] > gap and pad_mask[i, j] < 0.05


# Avenue trees along the main campus roads.
AVENUES = {
    "Spine Road North": (EUCA, 13), "Spine Road South": (EUCA, 13), "Academic Ring": (BROAD, 15), "Academic West Drive": (BROAD, 15),
    "Central East-West Road": (BROAD, 14), "Serpentine Drive": (BROAD, 16), "Gate Approach": (EUCA, 10),
    "Southern East-West Road": (BROAD, 15), "Track Road": (EUCA, 16), "Oval Road West": (BROAD, 14),
}
for name, width, pts in roads:
    if name not in AVENUES:
        continue
    kind, spacing = AVENUES[name]
    p = np.array(pts)
    seg = np.hypot(np.diff(p[:, 0]), np.diff(p[:, 1]))
    along = np.concatenate([[0], np.cumsum(seg)])
    offset = width / 2 + 3.2
    for s in np.arange(spacing / 2, along[-1], spacing):
        k = min(np.searchsorted(along, s) - 1, len(p) - 2)
        k = max(k, 0)
        t = (s - along[k]) / max(seg[k], 1e-6)
        x = p[k, 0] + (p[k + 1, 0] - p[k, 0]) * t
        z = p[k, 1] + (p[k + 1, 1] - p[k, 1]) * t
        dx, dz = (p[k + 1] - p[k]) / max(seg[k], 1e-6)
        for side in (-1, 1):
            tx, tz = x - dz * offset * side, z + dx * offset * side
            if clear_at(tx, tz, 2.5) and rng.random() < 0.9:
                trees.append((tx + rng.normal(0, 0.4), tz + rng.normal(0, 0.4), kind, rng.uniform(0.85, 1.2)))

# Scattered trees: sparse inside the campus, dense scrub forest in the ravines.
step = 5.0
gxs = np.arange(MIN_X + step / 2, MIN_X + SIZE, step)
gzs = np.arange(MIN_Z + step / 2, MIN_Z + SIZE, step)
GX, GZ = np.meshgrid(gxs, gzs)
JX = GX + rng.uniform(-step / 2, step / 2, GX.shape)
JZ = GZ + rng.uniform(-step / 2, step / 2, GZ.shape)
fi = np.clip(((JZ - MIN_Z) / CELL).round().astype(int), 0, RES - 1)
fj = np.clip(((JX - MIN_X) / CELL).round().astype(int), 0, RES - 1)
o = outside[fi, fj]
density = np.where(sd[fi, fj] < 0, 0.035 + 0.32 * hill_mask[fi, fj],
                   (0.30 - 0.22 * smoothstep(200, 650, o)) * (1 - smoothstep(650, 900, o)) * smoothstep(3, 15, o))
density *= 0.5 + 0.8 * np.clip(0.5 - n_patch[fi, fj], 0, 1)   # clumps
density *= 1 - farm_mask[fi, fj] * 0.92                        # few trees in fields
density *= 1 - 0.7 * lawn_mask[fi, fj]
keep = rng.random(GX.shape) < density
keep &= road_dist[fi, fj] > 3.0
keep &= pad_mask[fi, fj] < 0.05
keep &= slope[fi, fj] < 38
for x, z, i, j in zip(JX[keep], JZ[keep], fi[keep], fj[keep]):
    r = rng.random()
    if sd[i, j] < 0 and hill_mask[i, j] > 0.3:
        kind = PINE if r < 0.6 else (BROAD if r < 0.8 else SHRUB)  # pine wood on the hill by the courts
    elif sd[i, j] < 0:
        kind = BROAD if r < 0.55 else (SHRUB if r < 0.8 else EUCA)
    elif slope[i, j] > 18:
        kind = PINE if r < 0.45 else (SHRUB if r < 0.8 else BROAD)
    else:
        kind = BROAD if r < 0.4 else (SHRUB if r < 0.75 else (PINE if r < 0.88 else EUCA))
    trees.append((float(x), float(z), kind, float(rng.uniform(0.75, 1.3))))

# Planted rows on the water terraces and trees in the courtyard garden beds.
water_lines = [np.array(world_poly(pts)) for name, width, pts in L.WATER]


def near_water(x, z, gap):
    for wl in water_lines:
        for k in range(len(wl) - 1):
            a_, b_ = wl[k], wl[k + 1]
            ab = b_ - a_
            t = np.clip(np.dot([x - a_[0], z - a_[1]], ab) / max(np.dot(ab, ab), 1e-9), 0, 1)
            if np.hypot(x - a_[0] - ab[0] * t, z - a_[1] - ab[1] * t) < gap:
                return True
    return False


for kind, spacing, offset, pts in L.TREE_ROWS:
    p = np.array(densify(world_poly(pts), 1.0))
    along = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(p, axis=0).T))])
    for s_ in np.arange(spacing / 2, along[-1], spacing):
        tx = np.interp(s_, along, p[:, 0]) + offset
        tz = np.interp(s_, along, p[:, 1])
        if road_dist_at(tx, tz) > 2.0 and not near_water(tx, tz, 2.2):
            trees.append((tx + rng.normal(0, 0.3), tz + rng.normal(0, 0.3), kind, rng.uniform(0.8, 1.1)))
for kind, x, y in L.TREE_SPOTS:
    tx, tz = L.to_world(x, y)
    trees.append((tx, tz, kind, rng.uniform(0.7, 0.9)))

# ------------------------------------------------------------------ json
terrain = {
    "info": "Generated by tools/layout/generate_terrain.py. Heights row 0 = min Z. Splat layers: grass, dry grass, soil, rock.",
    "heightmapResolution": RES,
    "sizeX": SIZE, "sizeZ": SIZE,
    "minX": MIN_X, "minZ": MIN_Z,
    "minHeight": H_MIN, "heightRange": H_RANGE,
    "splatResolution": SPLAT_RES,
    "grassResolution": GRASS_RES,
    "treeTypes": ["broadleaf", "eucalyptus", "pine", "shrub"],
    "trees": [round(v, 2) for t in trees for v in t],  # x, z, type, scale
}
with open(os.path.join(DATA, "campus_terrain.json"), "w") as f:
    json.dump(terrain, f, separators=(",", ":"))

counts = [sum(1 for t in trees if t[2] == k) for k in range(4)]
print("Heights %.1f..%.1f m, trees %d (broadleaf %d, eucalyptus %d, pine %d, shrub %d)" % (
    height.min(), height.max(), len(trees), *counts))
inside_h = height[sd < 0]
print("Campus ground %.1f..%.1f m" % (inside_h.min(), inside_h.max()))

# ------------------------------------------------------------------ preview (for checking only)
if "--preview" in sys.argv:
    from PIL import Image
    prev = os.path.join(os.path.dirname(__file__), "preview")
    os.makedirs(prev, exist_ok=True)
    light = np.array([-0.5, 0.6, 0.62])
    nx, nz = -gx, -gz
    ny = np.ones_like(nx)
    norm = np.sqrt(nx * nx + ny * ny + nz * nz)
    shade = np.clip((nx * light[0] + ny * light[1] + nz * light[2]) / norm, 0, 1)
    pal = np.array([[0.36, 0.48, 0.22], [0.66, 0.60, 0.38], [0.52, 0.40, 0.29], [0.50, 0.48, 0.45]])
    col = stack @ pal
    col *= (0.35 + 0.8 * shade)[..., None]
    col = np.clip(col, 0, 1)[::-1]  # north up
    img = Image.fromarray((col * 255).astype(np.uint8))
    from PIL import ImageDraw
    dr = ImageDraw.Draw(img)

    def px(x, z):
        return (x - MIN_X) / CELL, (RES - 1) - (z - MIN_Z) / CELL
    for name, width, pts in roads:
        dr.line([px(x, z) for x, z in pts], fill=(60, 60, 62), width=max(2, int(width / CELL)))
    for g, cx, cz, hw, hd, ell in pads[: len(L.BUILDINGS)]:
        a, b = px(cx - hw, cz + hd), px(cx + hw, cz - hd)
        dr.rectangle([a, b], fill=(225, 215, 195))
    dr.line([px(x, z) for x, z in boundary + [boundary[0]]], fill=(200, 40, 40), width=1)
    for x, z, kind, s in trees:
        a = px(x, z)
        c = [(40, 90, 30), (70, 110, 60), (25, 70, 40), (85, 105, 40)][kind]
        dr.ellipse([a[0] - 1.5, a[1] - 1.5, a[0] + 1.5, a[1] + 1.5], fill=c)
    img.save(os.path.join(prev, "terrain_preview.png"))
    hm = ((height - height.min()) / (height.max() - height.min()) * 255).astype(np.uint8)[::-1]
    Image.fromarray(hm).save(os.path.join(prev, "terrain_height.png"))
    print("Preview written to", prev)
