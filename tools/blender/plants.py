#!/usr/bin/env python3
"""Plants for Blender: the Python mirror of Assets/Editor/Vegetation.cs (2026-11-21).

Vegetation.cs makes the game's trees, bushes and tall grass in Unity: branching woody frames and
crowns of alpha-cut leaf cards painted from code. This module repeats that in Blender, for
  - previews (render_map_tour.py, render_terrain_views.py, preview_vegetation.py), and
  - the leaf cards on the street planting props built by build_outdoor.py and the courtyard
    hedges in build_buildings.py (Leaf_* material slots that BuildingMaterials maps to
    Vegetation.LeafMaterial in Unity).
Change the two together: the leaf styles, the atlas layout (2 x 2 tiles of 256 px: two variants
in sunlight in the top row of uv, the same in shade in the bottom row, PAD margin), the card
shape (a quad with a raised middle, base at the twig) and each plant's proportions. The random
streams differ, so a preview tree is the same kind of tree, not the same twigs.

Import bpy before this module when the Blender parts are used; the texture painter needs only
numpy (textures are written as PNG through Blender's image API or returned as arrays).
"""
import math
import os
import random

import numpy as np

TILE = 256
PAD = 0.03
STYLES = ["Broad", "Euca", "Needle", "Shrub", "Red", "Flower", "Frond", "TallGrass", "Hedge"]
LEAF_SLOTS = {"Leaf_Broad": "Broad", "Leaf_Shrub": "Shrub", "Leaf_Red": "Red", "Leaf_Flower": "Flower",
              "Palm_Frond": "Frond", "Leaf_Hedge": "Hedge", "Leaf_Grass": "TallGrass"}
TWIG = (0.30, 0.24, 0.16)


def atlas_uv(tile, shade, u, v):
    u0, v0 = (tile & 1) * 0.5, (0.0 if shade else 0.5)
    return (u0 + (PAD + u * (1 - 2 * PAD)) * 0.5, v0 + (PAD + v * (1 - 2 * PAD)) * 0.5)


# ------------------------------------------------------------------ texture painter (numpy)
class Painter:
    def __init__(self, size, seed):
        self.size = size
        self.rgb = np.zeros((size, size, 3), np.float32)
        self.a = np.zeros((size, size), np.float32)
        self.rnd = random.Random(seed)
        ys, xs = np.mgrid[0:size, 0:size]
        self.X = xs + 0.5
        self.Y = ys + 0.5

    def R(self, a, b):
        return a + self.rnd.random() * (b - a)

    def _win(self, cx, cy, reach):
        s = self.size
        x0, x1 = max(0, int(cx - reach)), min(s, int(cx + reach) + 2)
        y0, y1 = max(0, int(cy - reach)), min(s, int(cy + reach) + 2)
        return slice(y0, y1), slice(x0, x1)

    def leaf(self, bx, by, angle, length, wid, colour, roundness=0.8, vein=0.18):
        ys, xs = self._win(bx, by, length + wid)
        if ys.start >= ys.stop or xs.start >= xs.stop:
            return
        dx, dy = self.X[ys, xs] - bx, self.Y[ys, xs] - by
        ca, sa = math.cos(angle), math.sin(angle)
        s = dx * ca + dy * sa
        q = -dx * sa + dy * ca
        t = np.clip(s / length, 0, 1)
        half = wid * 0.5 * np.sin(np.pi * t ** 0.85) ** roundness
        inside = (s >= 0) & (s <= length) & (np.abs(q) <= half)
        edge = np.where(half > 0.01, np.abs(q) / np.maximum(half, 1e-6), 1.0)
        shade = 0.78 + 0.3 * t - 0.18 * edge * edge
        shade = shade + np.where((edge < vein) & (t < 0.92), 0.16, 0.0)
        shade = shade * np.where(q > 0, 0.93, 1.0)
        c = np.array(colour, np.float32)[None, None, :] * shade[..., None]
        reg = self.rgb[ys, xs]
        reg[inside] = c[inside]
        self.a[ys, xs][inside] = 1.0

    def line(self, x0, y0, x1, y1, w0, w1, colour):
        length = math.hypot(x1 - x0, y1 - y0)
        steps = max(2, int(math.ceil(length * 0.7)))
        col = np.array(colour, np.float32)
        for k in range(steps + 1):
            t = k / steps
            cx, cy, r = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, (w0 + (w1 - w0) * t) * 0.5
            ys, xs = self._win(cx, cy, r + 1)
            if ys.start >= ys.stop or xs.start >= xs.stop:
                continue
            m = (self.X[ys, xs] - cx) ** 2 + (self.Y[ys, xs] - cy) ** 2 <= r * r + 0.25
            self.rgb[ys, xs][m] = col
            self.a[ys, xs][m] = 1.0

    def blob(self, cx, cy, r, colour):
        ys, xs = self._win(cx, cy, r + 1)
        d = np.sqrt((self.X[ys, xs] - cx) ** 2 + (self.Y[ys, xs] - cy) ** 2) / r
        m = d <= 1
        c = np.array(colour, np.float32)[None, None, :] * (1.05 - 0.25 * d)[..., None]
        self.rgb[ys, xs][m] = c[m]
        self.a[ys, xs][m] = 1.0

    def vary(self, c, amount):
        k = 1 + self.R(-amount, amount)
        return (min(1, c[0] * k * self.R(0.94, 1.06)), min(1, c[1] * k), min(1, c[2] * k * self.R(0.9, 1.1)))


def paint_tile(style, seed):
    p = Painter(TILE, seed)
    S = float(TILE)
    if style == "Broad":
        green = (0.28, 0.47, 0.16)
        stalks = 9
        for k in range(stalks):
            a = 0.35 + (math.pi - 0.7) * (k + p.R(0.2, 0.8)) / stalks
            ln = S * p.R(0.5, 0.78)
            bx, by = S * 0.5 + p.R(-10, 10), S * 0.06
            ex, ey = bx + math.cos(a) * ln, by + math.sin(a) * ln
            p.line(bx, by, ex, ey, 3.0, 1.2, TWIG)
            tone = p.vary(green, 0.15)
            pairs = 8
            for j in range(1, pairs + 1):
                t = j / (pairs + 0.6)
                lx, ly = bx + (ex - bx) * t, by + (ey - by) * t
                ll = S * 0.16 * (1.1 - 0.4 * t) * p.R(0.85, 1.1)
                for side in (-1, 1):
                    p.leaf(lx, ly, a + side * p.R(0.75, 1.05), ll, ll * 0.38, p.vary(tone, 0.08), 0.9)
            p.leaf(ex, ey, a + p.R(-0.1, 0.1), S * 0.11, S * 0.035, p.vary(tone, 0.08), 0.9)
    elif style == "Euca":
        green = (0.42, 0.50, 0.36)
        for k in range(8):
            bx, by = S * (0.1 + 0.8 * k / 7) + p.R(-8, 8), S * 0.05
            ex, ey = bx + p.R(-25, 25), S * p.R(0.75, 0.95)
            p.line(bx, by, ex, ey, 2.2, 1.0, TWIG)
            for j in range(9):
                t = 0.12 + 0.85 * j / 8
                lx, ly = bx + (ex - bx) * t, by + (ey - by) * t
                a = math.pi / 2 + (1 if j % 2 == 0 else -1) * p.R(0.35, 0.9)
                p.leaf(lx, ly, a, S * p.R(0.18, 0.26), S * p.R(0.035, 0.05), p.vary(green, 0.12), 0.7)
    elif style == "Needle":
        green = (0.27, 0.40, 0.18)
        bx, by, ex, ey = S * 0.5, S * 0.04, S * 0.5 + p.R(-10, 10), S * 0.55
        p.line(bx, by, ex, ey, 5.0, 3.0, TWIG)
        for f in range(60):
            t = p.R(0.2, 1.0)
            fx, fy = bx + (ex - bx) * t, by + (ey - by) * t
            a0 = math.pi / 2 + p.R(-1.3, 1.3)
            tone = p.vary(green, 0.18)
            for n in range(3):
                a = a0 + p.R(-0.12, 0.12)
                ln = S * p.R(0.25, 0.4)
                mx, my = fx + math.cos(a) * ln * 0.6, fy + math.sin(a) * ln * 0.6
                droop = 0.25 if a > math.pi / 2 else -0.25
                p.line(fx, fy, mx, my, 2.3, 2.0, tone)
                p.line(mx, my, mx + math.cos(a + droop) * ln * 0.4, min(S - 2, my + math.sin(a + droop) * ln * 0.4), 1.6, 1.0,
                       tuple(min(1, c * 1.1) for c in tone))
    elif style in ("Shrub", "Red", "Flower", "Hedge"):
        if style == "Red":
            palette = [(0.55, 0.15, 0.11), (0.66, 0.26, 0.12), (0.45, 0.12, 0.14), (0.36, 0.33, 0.14)]
        elif style == "Hedge":
            palette = [(0.21, 0.41, 0.14), (0.26, 0.48, 0.16), (0.32, 0.53, 0.19)]
        else:
            palette = [(0.27, 0.47, 0.16), (0.34, 0.54, 0.19), (0.39, 0.52, 0.17)]
        fill = style == "Hedge"
        for k in range(7):
            a = 0.5 + (math.pi - 1.0) * k / 6
            p.line(S * 0.5, S * 0.02, S * 0.5 + math.cos(a) * S * 0.36, S * 0.02 + math.sin(a) * S * 0.6, 2.5, 1.0, TWIG)
        for k in range(560 if fill else 460):
            if fill:
                x, y = p.R(0.04, 0.96) * S, p.R(0.04, 0.96) * S
            else:
                a, r = p.R(0, math.pi * 2), math.sqrt(p.R(0, 1))
                x, y = S * (0.5 + math.cos(a) * r * 0.44), S * (0.5 + math.sin(a) * r * 0.42)
            ln = S * p.R(0.065, 0.095)
            p.leaf(x, y, p.R(0, math.pi * 2), ln, ln * p.R(0.5, 0.65), p.vary(palette[p.rnd.randrange(len(palette))], 0.12), 0.65, 0.12)
        if style == "Flower":
            colours = [(0.86, 0.16, 0.55), (0.92, 0.30, 0.62), (0.80, 0.12, 0.45), (0.98, 0.70, 0.25)]
            for k in range(40):
                a, r = p.R(0, math.pi * 2), math.sqrt(p.R(0.1, 1))
                cx, cy = S * (0.5 + math.cos(a) * r * 0.4), S * (0.52 + math.sin(a) * r * 0.38)
                c = colours[p.rnd.randrange(len(colours))]
                for b in range(6):
                    p.leaf(cx, cy, b * 1.05 + p.R(-0.3, 0.3), S * p.R(0.045, 0.06), S * 0.045, p.vary(c, 0.1), 0.5, 0.0)
                p.blob(cx, cy, 2.2, (0.95, 0.92, 0.7))
    elif style == "Frond":
        green = (0.27, 0.44, 0.16)
        y0 = S * 0.5
        px_, py_ = S * 0.02, y0
        for j in range(1, 41):
            t = j / 40
            x, y = S * (0.02 + 0.95 * t), y0 + S * 0.08 * math.sin(t * 2.6) * 0.5
            p.line(px_, py_, x, y, 3.5 * (1.1 - t), 3.5 * (1.1 - t), (0.40, 0.42, 0.20))
            px_, py_ = x, y
            if t < 0.12:
                continue
            ln = S * 0.42 * math.sin(math.pi * min(1.0, (t - 0.05) * 1.05)) * p.R(0.85, 1.05)
            for side in (-1, 1):
                p.leaf(x, y, side * p.R(1.05, 1.3), ln * 0.62, S * 0.028, p.vary(green, 0.12), 0.6, 0.2)
    elif style == "TallGrass":
        blade = (0.56, 0.60, 0.31)
        for k in range(46):
            bx = S * 0.5 + p.R(-18, 18)
            h = S * p.R(0.45, 0.9)
            lean = p.R(-0.9, 0.9)
            tone = p.vary(blade, 0.2)
            px_, py_ = bx, 0.0
            for s2 in range(1, 11):
                t = s2 / 10
                x, y = bx + lean * S * 0.45 * t * t, h * (t - 0.25 * t * t * abs(lean))
                w = 4.5 * (1 - t * 0.85)
                p.line(px_, py_, x, y, w, w, tuple(c * (0.75 + 0.35 * t) for c in tone))
                px_, py_ = x, y
        plume = (0.93, 0.91, 0.85)
        for k in range(5):
            x = S * (0.3 + 0.4 * k / 4) + p.R(-6, 6)
            top = S * p.R(0.86, 0.97)
            p.line(S * 0.5, 0, x, top * 0.7, 2.0, 1.5, (0.62, 0.60, 0.42))
            for f in range(70):
                t = p.R(0, 1)
                fy, fx = top * 0.68 + (top - top * 0.68) * t, x + p.R(-1, 1) * (1 - t) * 3
                a = math.pi / 2 + p.R(-0.9, 0.9)
                p.line(fx, fy, fx + math.cos(a) * p.R(6, 14), min(S - 1, fy + math.sin(a) * p.R(4, 10)), 1.3, 0.8, p.vary(plume, 0.06))
    return p.rgb, p.a


def leaf_atlas(style):
    """RGBA float array (2*TILE square, row 0 = v 0) as Vegetation.LeafTexture: top row lit,
    bottom row shaded; transparent pixels carry the average leaf colour."""
    size = TILE * 2
    out = np.zeros((size, size, 4), np.float32)
    for tile in range(2):
        rgb, a = paint_tile(style, 101 + 17 * STYLES.index(style) + tile)
        for row in range(2):
            c = rgb * (np.array([0.68, 0.72, 0.76], np.float32) if row == 0 else 1.0)
            out[row * TILE:(row + 1) * TILE, tile * TILE:(tile + 1) * TILE, :3] = c
            out[row * TILE:(row + 1) * TILE, tile * TILE:(tile + 1) * TILE, 3] = a
    m = out[..., 3] > 0.5
    if m.any():
        avg = out[..., :3][m].mean(axis=0)
        out[..., :3][~m] = avg
    return out


# ------------------------------------------------------------------ Blender side
_IMAGES = {}
_MATS = {}


def leaf_image(style, folder=None):
    import bpy
    if style in _IMAGES and _IMAGES[style].name in bpy.data.images:
        return _IMAGES[style]
    arr = leaf_atlas(style)
    size = arr.shape[0]
    img = bpy.data.images.new("Leaves_" + style, size, size, alpha=True)
    img.pixels.foreach_set(arr.reshape(-1).astype(np.float32))
    img.alpha_mode = "STRAIGHT"
    if folder:
        os.makedirs(folder, exist_ok=True)
        img.filepath_raw = os.path.join(folder, "Leaves_%s.png" % style)
        img.file_format = "PNG"
        img.save()
    img.pack()
    _IMAGES[style] = img
    return img


def leaf_material(name, style, tint=(1, 1, 1)):
    """A Cycles/EEVEE material for leaf cards: alpha-cut, translucent a little, two-sided."""
    import bpy
    if name in _MATS and _MATS[name].name in bpy.data.materials:
        return _MATS[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = leaf_image(style)
    tex.interpolation = "Closest" if False else "Linear"
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.6
    mix_t = nt.nodes.new("ShaderNodeMixRGB")
    mix_t.blend_type = "MULTIPLY"
    mix_t.inputs[0].default_value = 1.0
    mix_t.inputs[2].default_value = (*tint, 1.0)
    nt.links.new(tex.outputs["Color"], mix_t.inputs[1])
    nt.links.new(mix_t.outputs[0], bsdf.inputs["Base Color"])
    try:
        bsdf.inputs["Subsurface Weight"].default_value = 0.0
        bsdf.inputs["Transmission Weight"].default_value = 0.12
    except KeyError:
        pass
    # Alpha cut at 0.45, as the Unity material.
    gt = nt.nodes.new("ShaderNodeMath")
    gt.operation = "GREATER_THAN"
    gt.inputs[1].default_value = 0.45
    nt.links.new(tex.outputs["Alpha"], gt.inputs[0])
    nt.links.new(gt.outputs[0], bsdf.inputs["Alpha"])
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    try:
        mat.blend_method = "CLIP"
    except (AttributeError, TypeError):
        pass
    mat.use_backface_culling = False
    _MATS[name] = mat
    return mat


class Plant:
    """Mirror of Vegetation.Plant: bark tubes and leaf cards in one mesh (Blender z up)."""

    def __init__(self, seed, coarse=False):
        self.rnd = random.Random(seed)
        self.coarse = coarse
        self.verts, self.faces, self.uvs, self.mats, self.normals = [], [], [], [], []
        self.crown = (0.0, 0.0, 0.0)
        self.radii = (1.0, 1.0, 1.0)

    def R(self, a, b):
        return a + self.rnd.random() * (b - a)

    def jitter(self, d, amount):
        v = np.array(d, float) + np.array([self.R(-amount, amount) for _ in range(3)])
        return v / max(1e-9, np.linalg.norm(v))

    # Unity's (x, y up, z) -> Blender (x, z, y): the plants are written in Unity's axes.
    @staticmethod
    def _b(p):
        return (float(p[0]), float(p[2]), float(p[1]))

    def tube(self, path, r0, r1, sides):
        if self.coarse:
            sides = max(4, sides // 2 + 1)
        rings = len(path)
        start = len(self.verts)
        prev = None
        for k in range(rings):
            t = k / (rings - 1)
            d = np.array(path[min(k + 1, rings - 1)]) - np.array(path[max(k - 1, 0)] if k == rings - 1 else path[k])
            d = d / max(1e-9, np.linalg.norm(d))
            if prev is None:
                ref = np.array([0, 1, 0]) if abs(d[1]) < 0.9 else np.array([1, 0, 0])
                side = np.cross(d, ref)
            else:
                side = np.cross(np.cross(d, prev), d)
            side = side / max(1e-9, np.linalg.norm(side))
            prev = side
            other = np.cross(d, side)
            r = r0 + (r1 - r0) * t
            for s in range(sides):
                a = s / sides * math.pi * 2
                n = side * math.cos(a) + other * math.sin(a)
                self.verts.append(self._b(np.array(path[k]) + n * r))
                self.normals.append(self._b(n))
        for k in range(rings - 1):
            for s in range(sides):
                a, b_ = start + k * sides + s, start + k * sides + (s + 1) % sides
                c, d_ = a + sides, b_ + sides
                self.faces.append((a, b_, d_, c))
                self.uvs.append([(s / sides, k * 0.5), ((s + 1) / sides, k * 0.5), ((s + 1) / sides, k * 0.5 + 0.5), (s / sides, k * 0.5 + 0.5)])
                self.mats.append(0)

    def limb(self, start, d, length, r0, r1, segments, wander, bend, sides):
        if self.coarse:
            segments = max(2, segments // 2 + 1)
        path = [np.array(start, float)]
        p, d = np.array(start, float), np.array(d, float) / np.linalg.norm(d)
        step = length / segments
        for k in range(segments):
            d = d + np.array(bend) / segments + np.array([self.R(-wander, wander), self.R(-wander, wander) * 0.5, self.R(-wander, wander)])
            d = d / np.linalg.norm(d)
            p = p + d * step
            path.append(p.copy())
        if r0 > 0.004:
            self.tube(path, r0, r1, sides)
        return path

    @staticmethod
    def at(path, t):
        f = min(max(t, 0.0), 1.0) * (len(path) - 1)
        i = min(len(path) - 2, int(math.floor(f)))
        return path[i] + (path[i + 1] - path[i]) * (f - i)

    @staticmethod
    def dir_at(path, t):
        f = min(max(t, 0.0), 1.0) * (len(path) - 1)
        i = min(len(path) - 2, int(math.floor(f)))
        d = path[i + 1] - path[i]
        return d / np.linalg.norm(d)

    def card(self, root, up, face, width, height, tile, shade, bend_out=0.75):
        up = np.array(up, float)
        up /= np.linalg.norm(up)
        face = np.array(face, float)
        n = face - np.dot(face, up) * up
        if np.dot(n, n) < 1e-8:
            n = np.cross(up, [1, 0, 0])
        n /= np.linalg.norm(n)
        right = np.cross(up, n)
        right /= np.linalg.norm(right)
        b = np.array(root, float) - up * (height * 0.12)
        corners = [b - right * width / 2, b + right * width / 2, b + up * height + right * width / 2,
                   b + up * height - right * width / 2, b + up * height / 2 + n * width * 0.12]
        cuv = [(0, 0), (1, 0), (1, 1), (0, 1), (0.5, 0.5)]
        start = len(self.verts)
        crown, radii = np.array(self.crown), np.array(self.radii)
        for k, p in enumerate(corners):
            o = (p - crown) / radii
            o = o / max(1e-9, np.linalg.norm(o))
            nn = n + (o - n) * bend_out
            nn = nn / max(1e-9, np.linalg.norm(nn)) + np.array([0, 0.15, 0])
            self.verts.append(self._b(p))
            self.normals.append(self._b(nn / np.linalg.norm(nn)))
        uv = [atlas_uv(tile, shade, u, v) for u, v in cuv]
        for k in range(4):
            a, c = start + k, start + (k + 1) % 4
            self.faces.append((a, c, start + 4))
            self.uvs.append([uv[k], uv[(k + 1) % 4], uv[4]])
            self.mats.append(1)

    def cluster(self, tip, d, count, size, spread, shade_below):
        crown, radii = np.array(self.crown), np.array(self.radii)
        for k in range(count):
            up = self.jitter(np.array(d) / np.linalg.norm(d) + np.array([0, 0.35, 0]), spread)
            face = self.jitter(np.cross(up, [self.R(-1, 1), self.R(-0.3, 0.3), self.R(-1, 1)]), 0.6)
            rel = np.array(tip) - crown
            shade = shade_below and (rel[1] < -0.25 * radii[1] or np.linalg.norm(rel / radii) < 0.55)
            s = size * self.R(0.8, 1.15)
            self.card(np.array(tip) - up * s * 0.1, up, face, s, s, self.rnd.randrange(2), shade)

    def to_mesh(self, name, bark_mat, leaf_mat):
        import bpy
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.verts, [], self.faces)
        uvl = me.uv_layers.new(name="UVMap")
        k = 0
        for poly, uvs in zip(me.polygons, self.uvs):
            for li, uv in zip(poly.loop_indices, uvs):
                uvl.data[li].uv = uv
        me.materials.append(bark_mat)
        me.materials.append(leaf_mat)
        me.polygons.foreach_set("material_index", self.mats)
        me.polygons.foreach_set("use_smooth", [True] * len(self.faces))
        try:
            me.normals_split_custom_set_from_vertices(self.normals)
        except Exception:
            pass
        me.update()
        return me


def broadleaf(coarse=False):
    p = Plant(11, coarse)
    p.crown, p.radii = (0, 5.4, 0), (3.6, 2.4, 3.6)
    trunk = p.limb((0, -0.4, 0), (0.08, 1, 0.03), 3.3, 0.32, 0.22, 4, 0.05, (0, 0, 0), 10)
    fork = trunk[-1]
    for i in range(6):
        a = i * math.pi * 2 / 6 + p.R(-0.3, 0.3)
        d = np.array([math.cos(a), p.R(0.75, 1.25), math.sin(a)])
        limb = p.limb(fork - np.array([0, p.R(0, 0.5), 0]), d, p.R(2.7, 3.5), 0.17, 0.06, 5, 0.12, (0, 0.25, 0), 7)
        if not coarse:
            for k in range(5):
                t = 0.4 + 0.6 * k / 4
                at = Plant.at(limb, t)
                dd = p.jitter(Plant.dir_at(limb, t) + np.array([p.R(-0.8, 0.8), p.R(0.1, 0.7), p.R(-0.8, 0.8)]), 0.2)
                twig = p.limb(at, dd, p.R(1.1, 1.8), 0.05, 0.015, 3, 0.2, (0, 0.15, 0), 5)
                p.cluster(twig[-1], dd, 5, p.R(1.7, 2.1), 0.5, True)
                p.cluster(Plant.at(twig, 0.5), dd, 2, 1.6, 0.6, True)
            p.cluster(limb[-1], d / np.linalg.norm(d), 3, 1.8, 0.4, True)
        else:
            for k in range(3):
                p.cluster(Plant.at(limb, 0.55 + 0.22 * k) + np.array([0, 0.6, 0]), d / np.linalg.norm(d), 3, 2.8, 0.6, True)
    for k in range(6 if coarse else 26):
        a, r = p.R(0, math.pi * 2), p.R(0.6, 2.2)
        at = np.array(p.crown) + np.array([math.cos(a) * r, p.R(-0.9, 0.6), math.sin(a) * r])
        s = 2.6 if coarse else 1.8
        p.card(at, p.jitter((0, 1, 0), 0.6), (math.cos(a), 0, math.sin(a)), s, s, k & 1, True)
    return p


def eucalyptus(coarse=False):
    p = Plant(12, coarse)
    p.crown, p.radii = (0.4, 11.0, 0), (2.4, 3.0, 2.4)
    trunk = p.limb((0, -0.4, 0), (0.05, 1, 0), 10.0, 0.27, 0.12, 7, 0.05, (0.05, 0, 0.02), 9)
    for i in range(6):
        t, a = 0.55 + 0.08 * i, i * 2.4
        at = Plant.at(trunk, t)
        d = np.array([math.cos(a) * 0.8, 1, math.sin(a) * 0.8])
        limb = p.limb(at, d, p.R(2.2, 3.4), 0.09, 0.03, 4, 0.12, (0, 0.2, 0), 6)
        sprays = 3 if coarse else 7
        for k in range(sprays):
            sp = Plant.at(limb, 0.35 + 0.65 * k / max(1, sprays - 1))
            hang = p.jitter((math.cos(a) * 0.5, -1, math.sin(a) * 0.5), 0.4)
            s = 2.4 if coarse else p.R(1.3, 1.7)
            p.card(sp + np.array([0, s * 0.85, 0]), hang, (math.cos(a + 1.5), 0, math.sin(a + 1.5)), s, s, k & 1, k == 0)
            if not coarse:
                p.cluster(sp, p.jitter(d, 0.3), 3, 1.5, 0.55, True)
    p.cluster(trunk[-1], (0, 1, 0), 2 if coarse else 4, 2.4 if coarse else 1.6, 0.5, False)
    return p


def pine(coarse=False):
    p = Plant(13, coarse)
    p.crown, p.radii = (0, 10.2, 0), (2.6, 1.8, 2.6)
    trunk = p.limb((0, -0.4, 0), (0.02, 1, 0.01), 12.3, 0.29, 0.07, 8, 0.03, (0, 0, 0), 10)
    whorls = 5
    for w in range(whorls):
        t = 0.58 + 0.4 * w / (whorls - 1)
        at = Plant.at(trunk, t)
        n = 3 if w == whorls - 1 else 4
        ln = 3.0 + (1.2 - 3.0) * w / (whorls - 1)
        for i in range(n):
            a = i * math.pi * 2 / n + w * 0.9 + p.R(-0.3, 0.3)
            d = np.array([math.cos(a), p.R(-0.15, 0.15), math.sin(a)])
            limb = p.limb(at, d, ln * p.R(0.85, 1.15), 0.08, 0.025, 4, 0.08, (0, 0.35, 0), 6)
            for k in range(2 if coarse else 4):
                tp = Plant.at(limb, 1 - 0.2 * k)
                s = 2.4 if coarse else p.R(1.6, 2.1)
                flat_up = p.jitter((d[0], 0.25, d[2]), 0.25)
                p.card(tp - flat_up * s * 0.3, flat_up, (0, 1, 0), s, s, k & 1, w < 2)
                if not coarse:
                    p.card(tp, p.jitter(np.array([0, 1, 0]) + d * 0.3, 0.3), (-d[2], 0, d[0]), s * 0.8, s * 0.8, (k + 1) & 1, w < 2)
    p.cluster(trunk[-1], (0, 1, 0), 2 if coarse else 4, 2.0 if coarse else 1.4, 0.5, False)
    return p


def dome(p, coarse, rx, ry, cy, cards, size):
    for i in range(3 if coarse else 7):
        a = i * 2.4
        d = (math.cos(a) * 0.6, 1, math.sin(a) * 0.6)
        p.limb((0, -0.15, 0), d, p.R(0.8, 1.2) * (ry + cy) * 0.8, 0.035, 0.01, 3, 0.25, (0, 0, 0), 5)
    for k in range(cards):
        f = (k + 0.5) / cards
        el = -0.45 + 1.45 * math.sqrt(f)
        az = k * 2.39996
        rr = math.sqrt(max(0.0, 1 - el * el))
        out = np.array([math.cos(az) * rr, el, math.sin(az) * rr])
        lump = p.R(0.72, 1.0)
        at = np.array([out[0] * rx * lump, cy + out[1] * ry * lump, out[2] * rx * lump])
        up = p.jitter(out * 0.6 + np.array([0, 0.5, 0]), 0.35)
        face = p.jitter(out, 0.5)
        s = size * p.R(0.85, 1.15)
        p.card(at - up * s * 0.45, up, face, s, s, k & 1, el < -0.1 or lump < 0.76, 0.8)


def shrub(coarse=False, seed=4):
    p = Plant(seed, coarse)
    p.crown, p.radii = (0, 0.7, 0), (1.15, 0.85, 1.15)
    dome(p, coarse, 1.15, 0.85, 0.75, 14 if coarse else 60, 1.35 if coarse else 0.95)
    return p


def flower_shrub(coarse=False):
    p = Plant(21, coarse)
    p.crown, p.radii = (0, 0.55, 0), (1.3, 0.7, 1.3)
    dome(p, coarse, 1.3, 0.7, 0.6, 14 if coarse else 62, 1.4 if coarse else 0.95)
    return p


def tall_grass(coarse=False):
    p = Plant(31, coarse)
    p.crown, p.radii = (0, 0.2, 0), (1, 1, 1)
    cards = 3 if coarse else 9
    for k in range(cards):
        a = k * math.pi / cards + p.R(-0.15, 0.15)
        lean = (math.cos(a * 2) * p.R(0.1, 0.35), 1, math.sin(a * 2) * p.R(0.1, 0.35))
        h = p.R(1.5, 2.0)
        p.card((p.R(-0.1, 0.1), -0.05 + h * 0.12, p.R(-0.1, 0.1)), lean, (math.cos(a), 0, math.sin(a)), h * 0.75, h, k & 1, False, 0.5)
    return p


# Tree kinds in generate_terrain.py's order, with the leaf style and bark colour of each.
KINDS = [("Broadleaf", broadleaf, "Broad", (0.33, 0.26, 0.20)),
         ("Eucalyptus", eucalyptus, "Euca", (0.78, 0.74, 0.66)),
         ("Pine", pine, "Needle", (0.42, 0.30, 0.22)),
         ("Shrub", shrub, "Shrub", (0.30, 0.25, 0.18)),
         ("FlowerShrub", flower_shrub, "Flower", (0.30, 0.25, 0.18)),
         ("TallGrass", tall_grass, "TallGrass", (0.30, 0.25, 0.18))]


def bark_material(colour):
    import bpy
    name = "Bark_%.2f_%.2f_%.2f" % colour
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*colour, 1)
    b.inputs["Roughness"].default_value = 0.9
    return mat


def plant_object(kind, coarse=False, name=None):
    """A Blender object of tree kind `kind` (0-5), at the origin, hidden from nothing."""
    import bpy
    label, fn, style, bark = KINDS[kind]
    p = fn(coarse)
    me = p.to_mesh(name or ("Plant_" + label), bark_material(bark), leaf_material("Leaves_" + style, style))
    ob = bpy.data.objects.new(name or ("Plant_" + label), me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# ------------------------------------------------------------------ cards for the Blender props
def prop_cards(m, cards, mat):
    """Adds leaf cards to a build_buildings.Model in prop coordinates. cards: list of
    (root (X, Z, y), up (x, y, z), face (x, y, z), width, height, tile, shade) in the prop's
    world-style axes (X east, Z north, y up)."""
    for root, up, face, w, h, tile, shade in cards:
        up = np.array(up, float)
        up /= np.linalg.norm(up)
        face = np.array(face, float)
        n = face - np.dot(face, up) * up
        if np.dot(n, n) < 1e-8:
            n = np.cross(up, [1, 0, 0])
        n /= np.linalg.norm(n)
        right = np.cross(up, n)
        right /= np.linalg.norm(right)
        r = np.array([root[0], root[2], root[1]], float)   # (X, y, Z)
        b = r - up * (h * 0.12)
        corners = [b - right * w / 2, b + right * w / 2, b + up * h + right * w / 2, b + up * h - right * w / 2]
        pts = []
        for c in corners:
            bx_, by_ = m.b(c[0], c[2])
            pts.append((bx_, by_, c[1]))
        uv = [atlas_uv(tile, shade, u, v) for u, v in ((0, 0), (1, 0), (1, 1), (0, 1))]
        m.quad(pts, mat, uv)


def bush_cards(rnd, cx, cz, y0, rx, ry, rz, count, size):
    """Cards over a dome (as Vegetation.Dome) for a prop bush centred at (cx, cz) on y0."""
    out = []
    for k in range(count):
        f = (k + 0.5) / count
        el = -0.15 + 1.15 * math.sqrt(f)
        az = k * 2.39996 + rnd.uniform(-0.2, 0.2)
        rr = math.sqrt(max(0.0, 1 - el * el))
        o = np.array([math.cos(az) * rr, el, math.sin(az) * rr])
        lump = rnd.uniform(0.75, 1.0)
        at = (cx + o[0] * rx * lump, cz + o[2] * rz * lump, y0 + max(0.0, o[1]) * ry * lump)
        up = o * 0.6 + np.array([0, 0.5, 0]) + np.array([rnd.uniform(-0.3, 0.3) for _ in range(3)])
        face = o + np.array([rnd.uniform(-0.5, 0.5) for _ in range(3)])
        s = size * rnd.uniform(0.85, 1.15)
        upn = up / np.linalg.norm(up)
        root = (at[0] - upn[0] * s * 0.45, at[1] - upn[2] * s * 0.45, at[2] - upn[1] * s * 0.45)
        out.append((root, up, face, s, s, k & 1, el < 0.1 or lump < 0.82))
    return out


def hedge_cards(rnd, X0, X1, Z0, Z1, y0, y1, step=0.55):
    """Leaf cards clothing a clipped hedge (a box from y0 to y1): cards lying on the top and
    standing out from the long sides, overlapping, a little ragged."""
    out = []
    long_x = (X1 - X0) >= (Z1 - Z0)
    L0, L1 = (X0, X1) if long_x else (Z0, Z1)
    W0, W1 = (Z0, Z1) if long_x else (X0, X1)
    n = max(1, int(round((L1 - L0) / step)))
    width = W1 - W0

    def pt(a, w):
        return (a, w) if long_x else (w, a)

    def vec(a, w, y):
        return (a, y, w) if long_x else (w, y, a)

    for k in range(n):
        a = L0 + (L1 - L0) * (k + 0.5) / n + rnd.uniform(-0.08, 0.08)
        # Top: one or two cards lying almost flat.
        for j in range(max(1, int(round(width / 0.6)))):
            w = W0 + width * (j + 0.5) / max(1, int(round(width / 0.6)))
            X, Z = pt(a - step * 0.65, w)
            out.append(((X, Z, y1 - 0.06), vec(1.0, rnd.uniform(-0.2, 0.2), rnd.uniform(0.15, 0.35)), (0, 1, 0),
                        max(0.55, width / max(1, int(round(width / 0.6))) + 0.2), step * 1.4, k & 1, False))
        # Sides: a card on each long face, standing up and leaning out a little.
        for sgn, wf in ((-1, W0), (1, W1)):
            X, Z = pt(a, wf + sgn * 0.04)
            out.append(((X, Z, y0 + 0.05), vec(rnd.uniform(-0.15, 0.15), sgn * 0.12, 1.0), vec(0, sgn, 0),
                        step * 1.35, (y1 - y0) * 1.05, (k + 1) & 1, False))
    # Ends: a card across each end so the dark core never shows.
    wm = (W0 + W1) / 2
    for sgn, a in ((-1, L0), (1, L1)):
        X, Z = pt(a + sgn * 0.04, wm)
        out.append(((X, Z, y0 + 0.05), vec(sgn * 0.12, 0.0, 1.0), vec(sgn, 0, 0),
                    width + 0.3, (y1 - y0) * 1.05, 0, False))
    return out
