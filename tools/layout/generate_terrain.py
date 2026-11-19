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
    elif name.startswith("Girls Hostel"):
        group = "Girls Hostels"   # A's north wing meets B's west wing: one floor level for both
    else:
        group = name.split(" - ")[0]
    pads.append((group, cx, cz, w / 2, d / 2, False))
FLAT_AREAS = {"parking", "plaza", "court", "track", "field", "amphitheatre", "substation", "courtyard"}
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
# residences a little higher again. Per the satellite (2026-11-19) the mess and shops sit at the
# level of the spine road, which is their only road and runs level past their fronts from the
# roundabout; the drop is on the road's other side, a steep grassy bank down to the girls'
# hostels, and the terrace's south end is a long bank the road runs down beside.
# (world x0, x1, z0, z1, height m, ramp east-west m, ramp north-south m), applied in order, so
# the faculty terrace's ramp makes the step down to the mess terrace.
MESS_LEVEL = 41.6
MESS_ROAD_LEVEL_Z = -158.0     # the spine road stays level from the roundabout to here
TERRACE_PADS = [
    (-64.0, 10.0, -162.0, -8.0, MESS_LEVEL, 7.0, 24.0),
    (-156.0, -64.0, -186.0, -4.0, 45.0, 8.0, 16.0),
]
for x0, x1, z0, z1, th, ramp, ramp_z in TERRACE_PADS:
    ddx = np.maximum(np.maximum(x0 - X, X - x1), 0.0)
    ddz = np.maximum(np.maximum(z0 - Z, Z - z1), 0.0) * (ramp / ramp_z)
    w = 1.0 - smoothstep(0.0, ramp, np.sqrt(ddx * ddx + ddz * ddz))
    plateau = plateau * (1 - w) + (th + 0.4 * n_mid) * w

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
# Plots shaped by their own code below rather than flattened to one level.
CARVED_PADS = {"Clock Tower", "area-565-688"}   # the clock tower walk, the sunken amphitheatre


def flatten_pads(h, margin, blend, heights=None, keep=None):
    """Levels each plot group to its mean height. keep (0..1 per cell) protects ground that
    something else has already shaped, such as the road surfaces."""
    if heights is None:
        groups = {}
        for g, cx, cz, hw, hd, ell in pads:
            rows, cols = window(cx, cz, max(hw, hd))
            d = pad_distance(X[rows, cols], Z[rows, cols], cx, cz, hw, hd, ell)
            groups.setdefault(g, []).append(h[rows, cols][d <= 0])
        heights = {g: float(np.mean(np.concatenate(v))) for g, v in groups.items()}
    for g, cx, cz, hw, hd, ell in pads:
        if g in CARVED_PADS:
            continue
        rows, cols = window(cx, cz, max(hw, hd) + margin + blend)
        d = pad_distance(X[rows, cols], Z[rows, cols], cx, cz, hw, hd, ell)
        w = 1.0 - smoothstep(margin, margin + blend, d)
        if keep is not None:
            w = w * (1 - keep[rows, cols])
        h[rows, cols] = h[rows, cols] * (1 - w) + heights[g] * w
    return heights


def smooth_profile(values, window_pts):
    v = np.asarray(values, dtype=float)
    k = np.ones(window_pts) / window_pts
    for _ in range(3):
        p = np.pad(v, window_pts // 2, mode="edge")
        v = np.convolve(p, k, mode="valid")[: len(values)]
    return v


def arc_lengths(p):
    return np.concatenate([[0.0], np.cumsum(np.hypot(*np.diff(p, axis=0).T))])


def nearest_on(p, prof, x, z):
    """Distance from (x, z) to the polyline p, and the profile height at the nearest point."""
    a, b_ = p[:-1], p[1:]
    e = b_ - a
    t = np.clip(((x - a[:, 0]) * e[:, 0] + (z - a[:, 1]) * e[:, 1]) / np.maximum((e * e).sum(1), 1e-9), 0, 1)
    d = np.hypot(x - (a[:, 0] + t * e[:, 0]), z - (a[:, 1] + t * e[:, 1]))
    k = int(np.argmin(d))
    return float(d[k]), float(prof[k] * (1 - t[k]) + prof[k + 1] * t[k])


def grade_limit(h, s, g, pinned):
    """Keeps the slope along a profile under g, moving only the points that are not pinned."""
    h = h.copy()
    ds = np.diff(s)
    for _ in range(4):
        for i in range(1, len(h)):
            if not pinned[i]:
                h[i] = min(max(h[i], h[i - 1] - g * ds[i - 1]), h[i - 1] + g * ds[i - 1])
        for i in range(len(h) - 2, -1, -1):
            if not pinned[i]:
                h[i] = min(max(h[i], h[i + 1] - g * ds[i]), h[i + 1] + g * ds[i])
    return h


# Steepest grade allowed on each road (rise over run). Drives stay drivable for the campus
# vehicles; the highway is gentler; footpaths may be a little steeper.
ROAD_GRADE = {"Highway": 0.06, "Serpentine Drive": 0.10}
DEFAULT_GRADE, PATH_GRADE = 0.09, 0.12


def is_footpath(name):
    return name.startswith("Courtyard") or name.startswith("Plaza")


def solve_road_profiles(h, fixed=None):
    """Heights along every road, solved as one network: wider roads first; where a road meets or
    crosses a road already solved (or a roundabout), it takes that road's height there, so
    junctions are level; then each profile is smoothed and held under its grade limit.
    fixed maps a road name to a profile decided elsewhere (the serpentine)."""
    order = sorted(range(len(roads)), key=lambda k: (roads[k][0] != "Highway", is_footpath(roads[k][0]),
                                                      roads[k][0] not in (fixed or {}), -roads[k][1]))
    rings = []   # roundabouts: [x, z, radius, height or None]
    for typ, shape, x, y, w, d in L.AREAS:
        if typ == "roundabout":
            cx, cz = L.to_world(x, y)
            rings.append([cx, cz, w / 2, None])
    solved = {}
    for k in order:
        name, width, pts = roads[k]
        p = np.array(pts)
        s = arc_lengths(p)
        if fixed and name in fixed:
            prof = np.asarray(fixed[name], float)
        else:
            pin = np.full(len(p), np.nan)
            for oname, (op, oprof, owidth) in solved.items():
                for i, (x, z) in enumerate(p):
                    d, oh = nearest_on(op, oprof, x, z)
                    if d < owidth / 2 + 0.5:
                        pin[i] = oh
            for cx, cz, r, rh in rings:
                if rh is None:
                    continue
                inside = np.hypot(p[:, 0] - cx, p[:, 1] - cz) < r + width / 2
                pin[inside] = rh
            window_pts = {"Highway": 25, "Serpentine Drive": 40}.get(name, 15)
            prof = smooth_profile(sample(h, p[:, 0], p[:, 1]), window_pts)
            if name == "Spine Road South":
                # Level past the mess and shop fronts, at the roundabout's height.
                first = pin[~np.isnan(pin)]
                if len(first):
                    pin[np.isnan(pin) & (p[:, 1] >= MESS_ROAD_LEVEL_Z)] = first[0]
            pinned = ~np.isnan(pin)
            g = ROAD_GRADE.get(name, PATH_GRADE if is_footpath(name) else DEFAULT_GRADE)
            for _ in range(3):
                prof[pinned] = pin[pinned]
                prof = grade_limit(prof, s, g, pinned)
                sm = smooth_profile(prof, 5)
                prof = np.where(pinned, prof, sm)
            prof[pinned] = pin[pinned]
            prof = grade_limit(prof, s, g, pinned)
        solved[name] = (p, prof, width)
        for ring in rings:
            if ring[3] is None:
                d, oh = nearest_on(p, prof, ring[0], ring[1])
                if d < ring[2] + width / 2:
                    ring[3] = oh
    return solved, rings


def flatten_roads(h, solved, rings, shoulder=9.0):
    """Lays the ground to the solved road heights: level across each road, then a shoulder that
    blends back into the land. Returns the distance from the nearest road edge and the weight
    (1 on the road surface, falling to 0 across the shoulder)."""
    best_d = np.full(h.shape, np.inf)
    best_h = np.zeros(h.shape)
    for name, (p, prof, width) in solved.items():
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
            sub_d, sub_h = best_d[rows, cols], best_h[rows, cols]
            closer = d < sub_d
            sub_d[closer], sub_h[closer] = d[closer], rh[closer]
    for cx, cz, r, rh in rings:
        if rh is None:
            continue
        rows, cols = window(cx, cz, r + 1.0 + shoulder)
        d = np.hypot(X[rows, cols] - cx, Z[rows, cols] - cz) - r
        sub_d, sub_h = best_d[rows, cols], best_h[rows, cols]
        closer = d < sub_d
        sub_d[closer], sub_h[closer] = d[closer], rh
    w = 1.0 - smoothstep(1.0, 1.0 + shoulder, best_d)
    h[:] = h * (1 - w) + best_h * w
    return best_d, 1.0 - smoothstep(1.0, 2.5, best_d)


pad_heights = flatten_pads(height, 3.0, 12.0)

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


# ------------------------------------------------------------------ the serpentine and the gate hillside
# The serpentine climbs from the gate roundabout to the academic block. Its heights are set
# along its length rather than read off the ground: a steady climb to the foot of the clock
# tower walk, which meets it CLOCK_STEPS_RISE below the porch, then a gentler one to the top.
# The hillside between its legs is then shaped from the road, like a slope the drive was cut into.
serp_name = "Serpentine Drive"
serp = np.array(next(r[2] for r in roads if r[0] == serp_name))
serp_s = arc_lengths(serp)
cs_x, cs_y0, cs_y1, cs_w, cs_ty, cs_land = L.CLOCK_STEPS
cs_X, cs_Z0 = L.to_world(cs_x, cs_y0)          # foot of the walk (on the serpentine's edge)
_, cs_Z1 = L.to_world(cs_x, cs_y1)             # top of the walk (fountain square)
_, cs_TZ = L.to_world(cs_x, cs_ty)             # tower landing centre
porch = np.array(next(r[2] for r in roads if r[0] == "Porch Drive"))
# The gate roundabout: no higher than the gate approach can reach from the highway at 7.5 %
# (the approach's first 7 m lie on the highway and its last 9 m on the roundabout).
gate_app = np.array(next(r[2] for r in roads if r[0] == "Gate Approach"))
gate_h = min(float(sample(height, serp[0, 0], serp[0, 1])),
             float(sample(height, gate_app[0, 0], gate_app[0, 1])) + 0.075 * (arc_lengths(gate_app)[-1] - 16.0))
top_h = pad_heights["academic"] - 0.6           # the porch apron, just below the block's floor plot
foot_i = int(np.argmin(np.hypot(serp[:, 0] - cs_X, serp[:, 1] - cs_Z0)))
foot_h = top_h - L.CLOCK_STEPS_RISE
serp_prof = np.interp(serp_s, [0.0, 9.0, serp_s[foot_i], serp_s[-1]], [gate_h, gate_h, foot_h, top_h])
serp_prof = np.where(serp_s <= 9.0, gate_h, smooth_profile(serp_prof, 9))   # level across the roundabout
serp_prof[-1] = top_h

CLOCK_X_Y = L.CLOCK_STEPS[:3]
# Hillside: inverse-distance blend of the drive's heights, over the slope between the gate and
# the academic plateau, fading out at the sides.
hx0, hx1, hz0, hz1 = -150.0, 125.0, 402.0, 530.0
rows, cols = window((hx0 + hx1) / 2, (hz0 + hz1) / 2, max(hx1 - hx0, hz1 - hz0) / 2 + 20)
px, pz = X[rows, cols], Z[rows, cols]
num = np.zeros(px.shape)
den = np.zeros(px.shape)
for (sx, sz), sh in zip(serp[::2], serp_prof[::2]):
    w = 1.0 / (np.hypot(px - sx, pz - sz) ** 3 + 8.0)
    num += w * sh
    den += w
hill = num / den
mask = 1 - smoothstep(0.0, 16.0, np.maximum.reduce([hx0 - px, px - hx1, hz0 - pz, pz - hz1]))
# Round the clock tower walk the slope carries on up to the porch: the walk climbs the hillside
# rather than a cutting in the plateau. Its heights rise evenly from the drive to the porch.
_, _cz0 = L.to_world(CLOCK_X_Y[0], CLOCK_X_Y[1])
_, _cz1 = L.to_world(CLOCK_X_Y[0], CLOCK_X_Y[2])
_cx, _ = L.to_world(CLOCK_X_Y[0], CLOCK_X_Y[1])
walk_line = foot_h + (top_h - foot_h) * np.clip((_cz0 - pz) / (_cz0 - _cz1), 0.0, 1.0)
wd = np.abs(px - _cx)
ww = 1.0 / (wd ** 2 + 8.0) * 40.0
hill = (num + ww * walk_line) / (den + ww)
walk_mask = (1 - smoothstep(18.0, 45.0, wd)) * (1 - smoothstep(0.0, 6.0, np.maximum(_cz1 - 4.0 - pz, pz - _cz0)))
mask = np.maximum(mask, walk_mask)
mask *= smoothstep(2.0, 12.0, -sd[rows, cols])
height[rows, cols] = height[rows, cols] * (1 - mask) + hill * mask

# ------------------------------------------------------------------ road network
solved, rings = solve_road_profiles(height, fixed={serp_name: serp_prof})
road_dist, road_core = flatten_roads(height, solved, rings)
# Plots again, sharp-edged, without touching the road surfaces; then each footprint exactly at
# its own level, so a neighbour's bank can never reach under a building's ground floor.
flatten_pads(height, 1.0, 3.0, pad_heights, keep=road_core)
flatten_pads(height, 0.6, 0.01, pad_heights, keep=road_core)

# ------------------------------------------------------------------ the clock tower walk
# A straight walk of flights and landings from the serpentine up to the fountain square, the
# tower on a level landing near the foot. Risers 0.15 m, treads 0.30 m, a 1.8 m landing after
# every 12 risers at most. The ground is cut to just below the steps; build_buildings.py builds
# them (with side walls) from the profile written to campus_terrain.json.
RISER, TREAD, MAX_RUN, LANDING = 0.15, 0.30, 12, 1.8
cs_bottom_h = float(np.interp(0.0, [0.0], [solved[serp_name][1][foot_i]]))
cs_top_h = nearest_on(solved["Porch Drive"][0], solved["Porch Drive"][1], cs_X, cs_Z1 - 10.0)[1]
land0, land1 = cs_TZ + cs_land / 2, cs_TZ - cs_land / 2     # tower landing (world Z falls going up)


def design_flights(z_start, z_end, h_start, h_end):
    """Steps from z_start (low) to z_end (high, smaller Z) rising h_end - h_start: as many risers as
    the rise needs, in runs of up to MAX_RUN, with the landings between the runs stretched to fill
    the length. Returns [(z, h)] corners: every step nose and landing end."""
    n = max(1, int(round((h_end - h_start) / RISER)))
    rise = (h_end - h_start) / n
    runs = [MAX_RUN] * (n // MAX_RUN) + ([n % MAX_RUN] if n % MAX_RUN else [])
    length = z_start - z_end
    stairs = n * TREAD
    gaps = len(runs) + 1                     # a landing before, between and after the runs
    land = max(0.6, (length - stairs) / gaps)
    if land * gaps + stairs > length + 1e-6:
        print("  clock walk: %.1f m of steps do not fit in %.1f m" % (stairs + gaps * land, length))
    pts = [(z_start, h_start)]
    z, hh = z_start - land, h_start
    pts.append((z, hh))
    for r in runs:
        for _ in range(r):
            hh += rise
            pts.append((z, hh))               # riser
            z -= TREAD
            pts.append((z, hh))               # tread
        z -= land - 0.0
        pts.append((z, hh))
    pts[-1] = (z_end, h_end)
    return pts, rise


lower_rise = min(4.2, max(0.6, (cs_top_h - cs_bottom_h) * (cs_Z0 - land0) / (cs_Z0 - cs_Z1 - cs_land)))
tower_h = cs_bottom_h + lower_rise
low_pts, r_low = design_flights(cs_Z0, land0, cs_bottom_h, tower_h)
up_pts, r_up = design_flights(land1, cs_Z1, tower_h, cs_top_h)
clock_profile = low_pts + up_pts
cz_ = np.array([p[0] for p in clock_profile])
ch_ = np.array([p[1] for p in clock_profile])
# The ground under the walk: the lowest line under the steps (through each riser's foot).
foot_line = ch_.copy()
foot_line[1:] = np.minimum(ch_[1:], ch_[:-1])
order_ = np.argsort(-cz_)
rows, cols = window(cs_X, (cs_Z0 + cs_Z1) / 2, (cs_Z0 - cs_Z1) / 2 + 14)
px, pz = X[rows, cols], Z[rows, cols]
walk_h = np.interp(-pz, -cz_, foot_line) - 0.12
half = np.where(np.abs(pz - cs_TZ) < cs_land / 2 + 1.0, 6.0, cs_w / 2 + 0.6)   # wider round the tower
across = np.abs(px - cs_X) - half
along = np.maximum(pz - cs_Z0 - 1.0, cs_Z1 - 2.0 - pz)
w = (1 - smoothstep(0.0, 10.0, across)) * (1 - smoothstep(0.0, 3.0, along)) * (1 - road_core[rows, cols])
height[rows, cols] = height[rows, cols] * (1 - w) + walk_h * w
road_dist[rows, cols] = np.minimum(road_dist[rows, cols], np.maximum(across, along))   # no trees on the walk
# The fountain square at the top, level with the porch drive.
fx0, fx1, fz0, fz1 = cs_X - 8.0, cs_X + 8.0, cs_Z1 - 12.0, cs_Z1
rows, cols = window(cs_X, (fz0 + fz1) / 2, 16.0)
px, pz = X[rows, cols], Z[rows, cols]
w = 1 - smoothstep(0.0, 3.0, np.maximum.reduce([fx0 - px, px - fx1, fz0 - pz, pz - fz1]))
height[rows, cols] = height[rows, cols] * (1 - w) + cs_top_h * w
print("Clock walk: %.1f m up from the serpentine (%.1f m) to the porch (%.1f m); tower landing %.1f m; risers %.3f / %.3f m" % (
    cs_top_h - cs_bottom_h, cs_bottom_h, cs_top_h, tower_h, r_low, r_up))

# ------------------------------------------------------------------ the sunken amphitheatre
# Its plot sits AMPHITHEATRE_DEPTH below the academic plot, so the top row of seats is level with
# the ring road outside the block and the seats step down to the stage and the pond. Grassy
# banks inside the plot's north, south and east edges lead down to the stage level; on the west
# the seating's own outer wall holds the ground up.
ax0, az0 = L.to_world(565, 688)
ax1, az1 = L.to_world(565 + 64, 688 + 114)
amph_h = pad_heights["academic"] - L.AMPHITHEATRE_DEPTH
rows, cols = window((ax0 + ax1) / 2, (az0 + az1) / 2, 90.0)
px, pz = X[rows, cols], Z[rows, cols]
fan_x, fan_z = L.to_world(610.0, 711.0)
fan_r = np.hypot(px - fan_x, pz - fan_z)
fan_a = np.degrees(np.arctan2(pz - fan_z, px - fan_x))
off_axis = np.abs(np.abs(fan_a) - 180.0)                                  # degrees from the fan's axis
in_fan = (fan_r < 45.2) & (off_axis < 39.3)
# Banks inside the plot on the west, north and south; on the east the low ground runs on out
# towards the boundary wall, where the land falls away to the ravine.
inner = np.minimum.reduce([px - ax0, az0 - pz, pz - az1])                # metres inside the plot
w = smoothstep(0.0, 9.0, inner) * (1 - smoothstep(0.0, 16.0, px - ax1))
target = np.full(px.shape, amph_h)
# Beside the seating the ground slopes down with the rows (the seats' side walls hold it), from
# the top walk's level at the back to the stage's at the front, so it never stands over the seats.
beside = fan_r * np.sin(np.radians(np.clip(off_axis - 39.0, 0.0, 90.0)))  # metres out from the side walls
tiers = np.clip((fan_r - 27.4) / (43.0 - 27.4), 0.0, 1.0) * L.AMPHITHEATRE_DEPTH
near = (1 - smoothstep(0.0, 10.0, beside)) * (fan_r < 47.0) * (off_axis < 90.0)
target = target + tiers * near
w = np.maximum(w, near)
w = np.where(in_fan, 1.0, w) * (1 - road_core[rows, cols])
height[rows, cols] = height[rows, cols] * (1 - w) + target * w
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
    "Southern East-West Road": (BROAD, 15), "Track Road": (EUCA, 16), "Oval Loop": (BROAD, 14),
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
    # The clock tower walk (see generate_layout.CLOCK_STEPS): its centre line X and width, the
    # tower landing's height, and the walk's outline from the serpentine up to the porch as
    # [Z, height] corners (every step nose and landing end).
    "clockSteps": {"x": round(cs_X, 3), "width": cs_w, "towerHeight": round(tower_h, 3),
                   "landing": [round(land1, 3), round(land0, 3)],
                   "profile": [[round(z, 3), round(hh, 3)] for z, hh in clock_profile]},
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
