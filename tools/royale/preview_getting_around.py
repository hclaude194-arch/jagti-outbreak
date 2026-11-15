"""Top-down check of the Phase 5D placement that RoyaleBuilder does at build time.

It repeats the zip line search (tower sites on level open ground, 70-220 m pairs with at least
8 m of drop, the sagging cable 2.5 m clear of the terrain) and the vehicle kerb spots along the
roads, using the terrain heightmap and the building footprints from campus_layout.json. Unity also
checks physics colliders (trees, props, walls), so it may drop a few of these; this preview only
shows whether the terrain gives enough relief for zip lines at all.
Run: python tools/royale/preview_getting_around.py  (writes tools/royale/preview/getting_around.png)
"""
import json
import math
import os

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.join(ROOT, "Assets", "Data")
OUT = os.path.join(ROOT, "tools", "royale", "preview")
layout = json.load(open(os.path.join(DATA, "campus_layout.json")))
TER = json.load(open(os.path.join(DATA, "campus_terrain.json")))
RES = TER["heightmapResolution"]
H = np.fromfile(os.path.join(DATA, "terrain_height.bytes"), dtype=np.uint16).reshape(RES, RES).astype(np.float64)
H = TER["minHeight"] + H / 65535.0 * TER["heightRange"]
TOWER, STEP = 4.4, 24.0


def ground(x, z):
    u = (x - TER["minX"]) / TER["sizeX"] * (RES - 1)
    v = (z - TER["minZ"]) / TER["sizeZ"] * (RES - 1)
    i, j = max(0, min(RES - 2, int(math.floor(u)))), max(0, min(RES - 2, int(math.floor(v))))
    fu, fv = u - i, v - j
    return (H[j, i] * (1 - fu) + H[j, i + 1] * fu) * (1 - fv) + (H[j + 1, i] * (1 - fu) + H[j + 1, i + 1] * fu) * fv


BOUND = list(zip(layout["boundary"][0::2], layout["boundary"][1::2]))
BOXES = [(b["x"] - b["width"] / 2 - 2, b["x"] + b["width"] / 2 + 2, b["z"] - b["depth"] / 2 - 2, b["z"] + b["depth"] / 2 + 2)
         for b in layout["buildings"]]


def inside(x, z):
    c, n = False, len(BOUND)
    for i in range(n):
        (xi, zi), (xj, zj) = BOUND[i], BOUND[i - 1]
        if (zi > z) != (zj > z) and x < (xj - xi) * (z - zi) / (zj - zi) + xi:
            c = not c
    return c


def in_building(x, z):
    return any(a <= x <= b and c <= z <= d for a, b, c, d in BOXES)


def zip_lines():
    xs, zs = [p[0] for p in BOUND], [p[1] for p in BOUND]
    sites = []
    for x in np.arange(min(xs) + STEP, max(xs) - STEP, STEP):
        for z in np.arange(min(zs) + STEP, max(zs) - STEP, STEP):
            if not inside(x, z) or in_building(x, z):
                continue
            g = ground(x, z)
            hs = [ground(x + dx, z + dz) for dx in (-1.2, 1.2) for dz in (-1.2, 1.2)] + [g]
            if max(hs) - min(hs) > 1.0:
                continue
            sites.append((x, g, z))
    pairs = []
    for a in sites:
        for b in sites:
            dh = a[1] - b[1]
            if dh < 8:
                continue
            ln = math.hypot(b[0] - a[0], b[2] - a[2])
            if ln < 70 or ln > 220 or dh / ln > 0.45:
                continue
            ta, tb = np.array(a) + (0, TOWER, 0), np.array(b) + (0, TOWER, 0)
            sag = np.linalg.norm(tb - ta) * 0.018
            ok = True
            for k in range(1, 24):
                s = k / 24
                p = ta + (tb - ta) * s - np.array((0, 4 * sag * s * (1 - s), 0))
                if p[1] - ground(p[0], p[2]) < 2.5 or in_building(p[0], p[2]) and p[1] - ground(p[0], p[2]) < 20:
                    ok = False
                    break
            if ok:
                pairs.append((dh * (1 - abs(ln - 150) / 250), a, b))
    pairs.sort(key=lambda t: -t[0])
    built, ends = [], []
    for score, a, b in pairs:
        if len(built) >= 5:
            break
        if any(math.hypot(e[0] - p[0], e[2] - p[2]) < 90 for e in ends for p in (a, b)):
            continue
        built.append((a, b))
        ends += [a, b]
    return sites, len(pairs), built


def vehicle_spots():
    spots = []
    for road in layout["roads"]:
        if road["width"] < 4 or road["name"] == "Highway" or road["name"].startswith("Courtyard"):
            continue
        p = list(zip(road["points"][0::2], road["points"][1::2]))
        carry, side = 45.0, 1
        for (ax, az), (bx, bz) in zip(p, p[1:]):
            ln = math.hypot(bx - ax, bz - az)
            if ln < 0.1:
                continue
            dx, dz = (bx - ax) / ln, (bz - az) / ln
            t = carry
            while t < ln:
                x = ax + dx * t + dz * side * (road["width"] / 2 - 1.1)
                z = az + dz * t - dx * side * (road["width"] / 2 - 1.1)
                if inside(x, z) and not in_building(x, z) and all(math.hypot(x - s[0], z - s[1]) > 15 for s in spots):
                    spots.append((x, z))
                    side = -side
                t += 90
            carry = t - ln
    return spots


def main():
    sites, npairs, lines = zip_lines()
    spots = vehicle_spots()
    xs, zs = [p[0] for p in BOUND], [p[1] for p in BOUND]
    x0, x1, z0, z1 = min(xs) - 30, max(xs) + 30, min(zs) - 30, max(zs) + 30
    scale = 1400 / max(x1 - x0, z1 - z0)
    W, Hh = int((x1 - x0) * scale), int((z1 - z0) * scale)
    # Hillshade of the terrain as the base.
    gx = np.linspace(x0, x1, W // 2)
    gz = np.linspace(z1, z0, Hh // 2)
    hm = np.array([[ground(x, z) for x in gx] for z in gz])
    dzdx = np.gradient(hm, axis=1)
    dzdy = np.gradient(hm, axis=0)
    shade = np.clip(0.6 + (dzdx - dzdy) * 0.25, 0, 1)
    tint = (hm - hm.min()) / max(1e-6, hm.max() - hm.min())
    rgb = np.stack([shade * (0.45 + 0.35 * tint), shade * (0.55 + 0.2 * tint), shade * (0.35 + 0.15 * tint)], -1)
    img = Image.fromarray((rgb * 255).astype(np.uint8)).resize((W, Hh))
    d = ImageDraw.Draw(img)
    S = lambda x, z: ((x - x0) * scale, (z1 - z) * scale)
    d.line([S(*p) for p in BOUND + BOUND[:1]], fill=(240, 240, 240), width=2)
    for road in layout["roads"]:
        d.line([S(x, z) for x, z in zip(road["points"][0::2], road["points"][1::2])], fill=(70, 70, 72), width=max(1, int(road["width"] * scale)))
    for a, b, c, e in BOXES:
        d.rectangle([S(a, e), S(b, c)], fill=(150, 130, 110), outline=(60, 50, 40))
    for x, g, z in sites:
        sx, sz = S(x, z)
        d.point((sx, sz), fill=(255, 255, 255))
    for a, b in lines:
        d.line([S(a[0], a[2]), S(b[0], b[2])], fill=(255, 150, 30), width=4)
        for p in (a, b):
            sx, sz = S(p[0], p[2])
            d.rectangle([sx - 5, sz - 5, sx + 5, sz + 5], fill=(255, 150, 30), outline=(0, 0, 0))
        sx, sz = S(a[0], a[2])
        d.text((sx + 8, sz - 6), "%.0f m drop" % (a[1] - b[1]), fill=(255, 255, 255))
    for x, z in spots:
        sx, sz = S(x, z)
        d.rectangle([sx - 3, sz - 3, sx + 3, sz + 3], fill=(120, 210, 255), outline=(0, 0, 0))
    d.text((10, 10), "Zip lines (orange): %d of %d clear pairs from %d tower sites.  Vehicle kerb spots (blue): %d"
           % (len(lines), npairs, len(sites), len(spots)), fill=(255, 255, 255))
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, "getting_around.png"))
    print(len(sites), npairs, len(lines), len(spots))
    for a, b in lines:
        print("line", [round(v) for v in a], [round(v) for v in b], "drop %.1f len %.0f" % (a[1] - b[1], math.hypot(b[0] - a[0], b[2] - a[2])))


main()
