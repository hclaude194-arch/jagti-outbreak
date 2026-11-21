#!/usr/bin/env python3
"""Before/after eye-level mock-ups of the terrain detail pass (2026-11-20) without Unity.

Builds the map-tour scene (render_map_tour.py, everything before its render loop), then renders
each view twice:

  before: the tour's flat splat colours and the old single grass card (terrain_grass.bytes,
          up to 7 tufts per 2 m cell, drawn to 45 m as on the old Medium default);
  after:  the ground painted from the four terrain layers the way CampusTerrainBuilder sets them
          up (tiled layer textures, mask-map occlusion, height blending between layers) and the
          five detail layers from terrain_details.bytes (lawn grass, wild grass, yellow and white
          flowers as camera-facing cards, mesh grass clumps), drawn to 120 m as on High.

The layer textures, grass cards, flower cards and the clump mesh follow the recipes in
Assets/Editor/ProceduralAssets.cs (GroundTexture, GrassTexture, FlowerTexture, GrassClumpPrefab)
and the prototype sizes and colours in CampusTerrainBuilder.DetailPrototypes (DETAILS below), so
change them together. Random draws differ from C#'s System.Random, so tufts land elsewhere; the
look and the density are what matter. Wind, Unity's lighting and the post-processing can't show.

    python tools/blender/preview_terrain_detail.py                 # default views
    python tools/blender/preview_terrain_detail.py mess_island     # chosen views
Writes tools/blender/preview/detail_<view>.png (before | after side by side).
"""
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TOUR = os.path.join(HERE, "render_map_tour.py")

# Eye x, z, eye height, look-at x, z, height, lens (as in render_map_tour VIEWS).
VIEWS = {
    "mess_island": (-4, -100, 1.7, -30, -112, 2.0, 24),
    "boys_hill": (60, -455, 1.7, 160, -470, 10, 24),
    "academic_lawn": (40, 330, 1.7, 100, 300, 4, 24),
    "west_meadow": (-250, -62, 1.6, -150, -75, -38, 24),
    # 2026-11-21: the bank below the retaining wall, long grass and leaf-card scrub.
    "drop_bank": (200, -175, 1.6, 176, -200, 2.0, 24),
}
AFTER_ONLY = "--after-only" in sys.argv     # just the new look, saved as detail_<view>.png
wanted = [a for a in sys.argv[1:] if a in VIEWS] or ["west_meadow", "mess_island", "boys_hill"]

sys.argv = [TOUR, "--fast"]
src = open(TOUR).read()
cut = src.index("os.makedirs(OUT, exist_ok=True)")
g = {"__file__": TOUR, "__name__": "__tour__"}
exec(compile(src[:cut], TOUR, "exec"), g)

import bpy  # noqa: E402  (already loaded by the tour)
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

info, ground, scene, link = g["info"], g["ground"], g["scene"], g["link"]
cam, cam_o, OUT, DATA = g["cam"], g["cam_o"], g["OUT"], g["DATA"]
terrain_patch = g["terrain_patch"]
TEX = os.path.join(OUT, "detail_textures")
os.makedirs(TEX, exist_ok=True)
scene.cycles.samples = 16
GRES = info["grassResolution"]
GCELL = info["sizeX"] / GRES


# ------------------------------------------------------------------ textures (ProceduralAssets ports)
def tile_noise(size, period, seed):
    """Tileable value noise in [0, 1] (ProceduralAssets.Noise)."""
    r = np.random.default_rng(seed)
    lat = r.random((period, period))
    t = np.arange(size) / size * period
    i = t.astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    i1 = (i + 1) % period
    a, b = lat[np.ix_(i, i)], lat[np.ix_(i, i1)]
    c, d = lat[np.ix_(i1, i)], lat[np.ix_(i1, i1)]
    fy, fx = f[:, None], f[None, :]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def ground_texture(dark, mid, light, period, speckle, seed, size=512):
    total, amp, norm = np.zeros((size, size)), 1.0, 0.0
    for o in range(5):
        total += amp * tile_noise(size, period * 2 ** o, seed + o)
        norm += amp
        amp *= 0.5
    n = np.clip((total / norm - 0.5) * 1.8 + 0.5, 0, 1)[..., None]
    dark, mid, light = (np.array(c, float) for c in (dark, mid, light))
    c = np.where(n < 0.5, dark + (mid - dark) * n * 2, mid + (light - mid) * (n - 0.5) * 2)
    fine = tile_noise(size, 128, seed + 7)[..., None]
    c = c + (np.where(fine > 0.5, light, dark) - c) * np.abs(fine - 0.5) * 2 * speckle
    return np.clip(c, 0, 1)


# CampusTerrainBuilder.MakeLayers: colours, base period, speckle, seed, tile (m), smoothness,
# mask height remap (min, max).
LAYERS = [
    ((0.17, 0.33, 0.09), (0.29, 0.50, 0.16), (0.43, 0.62, 0.24), 4, 0.40, 1, 5.0, 0.30, 1.00),
    ((0.42, 0.38, 0.22), (0.60, 0.54, 0.33), (0.74, 0.68, 0.46), 4, 0.50, 2, 6.0, 0.20, 0.90),
    ((0.32, 0.23, 0.16), (0.47, 0.35, 0.25), (0.60, 0.50, 0.40), 6, 0.55, 3, 5.0, 0.00, 0.55),
    ((0.33, 0.31, 0.28), (0.50, 0.47, 0.43), (0.66, 0.63, 0.58), 3, 0.35, 4, 9.0, 0.25, 1.00),
]
layer_tex, layer_h = [], []
for dark, mid, light, period, speckle, seed, tile, h0, h1 in LAYERS:
    t = ground_texture(dark, mid, light, period, speckle, seed)
    lum = t @ np.array([0.3, 0.59, 0.11])
    hh = (lum - lum.min()) / max(lum.max() - lum.min(), 1e-3)      # MaskFor: blue channel
    layer_tex.append(t)
    layer_h.append(h0 + (h1 - h0) * hh)


def grass_card(name, blades, min_h, width, lean, seed, seed_heads, size=128):
    """ProceduralAssets.GrassTexture."""
    px = np.zeros((size, size, 4))
    px[..., :3] = (200 / 255, 210 / 255, 170 / 255)
    r = random.Random(seed)
    for _ in range(blades):
        base = 8 + r.random() * (size - 16)
        height = size * (min_h + (1 - min_h) * r.random())
        tilt = (r.random() - 0.5) * lean
        w = width + r.random() * width
        tone = 0.8 + 0.2 * r.random()
        for y in range(int(math.ceil(height))):
            t = y / height
            cx = base + tilt * t * t
            half = w * (1 - t)
            shade = (0.5 + 0.5 * t) * tone
            x0, x1 = max(int(math.floor(cx - half)), 0), min(int(math.ceil(cx + half)), size - 1)
            px[y, x0:x1 + 1] = (0.85 * shade, 0.9 * shade, 0.7 * shade, 1)
    if seed_heads:
        for _ in range(4):
            x = 16 + r.randrange(size - 32)
            top = size - 4 - r.randrange(10)
            px[:top, x] = (0.8, 0.78, 0.62, 1)
            px[top - 14:top, max(x - 1, 0):x + 2] = (0.95, 0.88, 0.7, 1)
    return save_rgba(name, px)


def flower_card(name, petal, centre, flowers, seed, size=128):
    """ProceduralAssets.FlowerTexture."""
    px = np.zeros((size, size, 4))
    px[..., :3] = (120 / 255, 150 / 255, 80 / 255)
    r = random.Random(seed)
    stem = np.array([0.32, 0.45, 0.18])
    for _ in range(8):
        base = 10 + r.random() * (size - 20)
        height = size * (0.25 + 0.3 * r.random())
        for y in range(int(math.ceil(height))):
            t = y / height
            half = 2.5 * (1 - t)
            x0, x1 = max(int(math.floor(base - half)), 0), min(int(math.ceil(base + half)), size - 1)
            px[y, x0:x1 + 1, :3] = stem * (0.7 + 0.3 * t)
            px[y, x0:x1 + 1, 3] = 1
    yy, xx = np.mgrid[0:size, 0:size]
    for _ in range(flowers):
        cx = 14 + r.randrange(size - 28)
        cy = int(size * (0.5 + 0.4 * r.random()))
        rad = 6 + 4 * r.random()
        px[:cy, cx, :3] = stem
        px[:cy, cx, 3] = 1
        dx, dy = xx - cx, yy - cy
        d = np.hypot(dx, dy)
        edge = rad * (0.75 + 0.25 * np.cos(np.arctan2(dy, dx) * 5))
        m = d <= edge
        col = np.where((d < rad * 0.3)[..., None], np.array(centre),
                       np.array(petal) * (0.85 + 0.15 * (1 - d / rad))[..., None])
        px[m, :3] = col[m]
        px[m, 3] = 1
    return save_rgba(name, px)


def save_rgba(name, px):
    path = os.path.join(TEX, name + ".png")
    Image.fromarray((np.clip(px[::-1], 0, 1) * 255).astype(np.uint8), "RGBA").save(path)
    return path


# ------------------------------------------------------------------ detail prototypes
# CampusTerrainBuilder.DetailPrototypes / DetailLayers: card, healthy, dry, width, height,
# noise spread, tufts per cell at full density.
DETAILS = [
    (grass_card("Grass_Lawn", 56, 0.55, 2.0, 16, 12, False), (0.50, 0.68, 0.30), (0.70, 0.70, 0.40),
     (0.35, 0.65), (0.10, 0.25), 0.5, 20),
    (grass_card("Grass_Wild", 34, 0.4, 2.6, 36, 13, True), (0.62, 0.66, 0.36), (0.86, 0.76, 0.50),
     (0.55, 1.1), (0.30, 0.75), 0.6, 14),
    (flower_card("Flowers_Yellow", (1, 0.72, 0.12), (0.85, 0.42, 0.05), 5, 14), (1, 1, 1), (0.92, 0.86, 0.74),
     (0.35, 0.55), (0.22, 0.40), 0.8, 5),
    (flower_card("Flowers_White", (0.97, 0.96, 0.92), (1, 0.82, 0.2), 6, 15), (1, 1, 1), (0.9, 0.88, 0.8),
     (0.25, 0.4), (0.16, 0.28), 0.8, 5),
    ("clump", (0.55, 0.68, 0.32), (0.80, 0.72, 0.46), (0.8, 1.3), (0.8, 1.3), 0.5, 6),
]
OLD = (grass_card("Grass_Blades", 18, 0.45, 2.5, 30, 11, False), (0.55, 0.66, 0.32), (0.78, 0.70, 0.45),
       (0.5, 1.0), (0.3, 0.7), 0.4, 7)


def card_object(name, path):
    """A unit quad standing on its bottom edge, facing -Y, with the card's alpha."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([(-0.5, 0, 0), (0.5, 0, 0), (0.5, 0, 1), (-0.5, 0, 1)], [], [(0, 1, 2, 3)])
    uv = me.uv_layers.new()
    for k, c in enumerate([(0, 0), (1, 0), (1, 1), (0, 1)]):
        uv.data[k].uv = c
    me.materials.append(card_material(name, path))
    ob = link(bpy.data.objects.new(name, me))
    ob.location = (0, 0, -900)
    ob.hide_render = True
    return ob


def tinted_bsdf(m, base_socket):
    """Texture x instance tint, with a little light through the blades."""
    n, L = m.node_tree.nodes, m.node_tree.links
    p = n["Principled BSDF"]
    att = n.new("ShaderNodeAttribute")
    att.attribute_type = "INSTANCER"
    att.attribute_name = "tint"
    mul = n.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs["Factor"].default_value = 1.0
    L.new(base_socket, mul.inputs["A"])
    L.new(att.outputs["Color"], mul.inputs["B"])
    L.new(mul.outputs["Result"], p.inputs["Base Color"])
    p.inputs["Roughness"].default_value = 0.7
    tr = n.new("ShaderNodeBsdfTranslucent")
    L.new(mul.outputs["Result"], tr.inputs["Color"])
    mix = n.new("ShaderNodeMixShader")
    mix.inputs["Fac"].default_value = 0.25
    out = n["Material Output"]
    L.new(p.outputs[0], mix.inputs[1])
    L.new(tr.outputs[0], mix.inputs[2])
    L.new(mix.outputs[0], out.inputs["Surface"])
    return p


def card_material(name, path):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    n, L = m.node_tree.nodes, m.node_tree.links
    img = n.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(path)
    img.interpolation = "Closest"
    p = tinted_bsdf(m, img.outputs["Color"])
    # Unity's grass shader clips at alpha 0.5; do the same so the cards have hard edges.
    gt = n.new("ShaderNodeMath")
    gt.operation = "GREATER_THAN"
    gt.inputs[1].default_value = 0.5
    L.new(img.outputs["Alpha"], gt.inputs[0])
    L.new(gt.outputs[0], p.inputs["Alpha"])
    # The translucent lobe ignores alpha, so mask the whole shader instead.
    out = n["Material Output"]
    mix_out = out.inputs["Surface"].links[0].from_node
    hold = n.new("ShaderNodeMixShader")
    tr = n.new("ShaderNodeBsdfTransparent")
    L.new(gt.outputs[0], hold.inputs["Fac"])
    L.new(tr.outputs[0], hold.inputs[1])
    L.new(mix_out.outputs[0], hold.inputs[2])
    L.new(hold.outputs[0], out.inputs["Surface"])
    return m


def clump_object():
    """ProceduralAssets.GrassClumpPrefab: 20 tapering, leaning blades, about 0.55 m tall."""
    r = random.Random(21)
    verts, faces, ts = [], [], []
    for b in range(20):
        yaw = b * 2.4 + r.random() * 0.6
        height = 0.35 + 0.25 * r.random()
        lean = 0.12 + 0.22 * r.random()
        width = 0.025 + 0.015 * r.random()
        out = np.array([math.cos(yaw), math.sin(yaw), 0.0])     # Blender: X, Y(=Unity Z), Z up
        side = np.array([-out[1], out[0], 0.0])
        root = out * 0.03 * r.random()
        start = len(verts)
        for row in range(5):
            t = row / 4
            c = root + out * lean * t * t
            c[2] = height * t * (1 - 0.3 * lean * t)
            half = width * (1 - t * 0.92)
            verts += [tuple(c - side * half), tuple(c + side * half)]
            ts += [t, t]
        for row in range(4):
            a = start + row * 2
            faces.append((a, a + 1, a + 3, a + 2))
    me = bpy.data.meshes.new("Clump")
    me.from_pydata(verts, [], faces)
    at = me.attributes.new("t", "FLOAT", "POINT")
    at.data.foreach_set("value", np.array(ts, np.float32))
    m = bpy.data.materials.new("Clump")
    m.use_nodes = True
    n, L = m.node_tree.nodes, m.node_tree.links
    a = n.new("ShaderNodeAttribute")
    a.attribute_name = "t"
    # Root dark, tip pale (the clump texture: 0.45 + 0.55 sqrt(t)).
    sq = n.new("ShaderNodeMath")
    sq.operation = "SQRT"
    L.new(a.outputs["Fac"], sq.inputs[0])
    ma = n.new("ShaderNodeMapRange")
    ma.inputs["To Min"].default_value = 0.45
    L.new(sq.outputs[0], ma.inputs["Value"])
    comb = n.new("ShaderNodeCombineColor")
    L.new(ma.outputs[0], comb.inputs[0])
    L.new(ma.outputs[0], comb.inputs[1])
    L.new(ma.outputs[0], comb.inputs[2])
    tinted_bsdf(m, comb.outputs[0])
    me.materials.append(m)
    ob = link(bpy.data.objects.new("Clump", me))
    ob.location = (0, 0, -900)
    ob.hide_render = True
    return ob


def scatter(name, proto, pts, yaw, scale, tint):
    """Instances with per-point rotation, scale (x, y, z) and colour."""
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(pts))
    me.vertices.foreach_set("co", np.asarray(pts, np.float32).ravel())
    for attr, kind, vals in (("yaw", "FLOAT", yaw), ("scl", "FLOAT_VECTOR", scale), ("tint", "FLOAT_COLOR", tint)):
        a = me.attributes.new(attr, kind, "POINT")
        key = "value" if kind == "FLOAT" else ("color" if kind == "FLOAT_COLOR" else "vector")
        a.data.foreach_set(key, np.asarray(vals, np.float32).ravel())
    ob = link(bpy.data.objects.new(name, me))
    mod = ob.modifiers.new("Inst", "NODES")
    tree = bpy.data.node_groups.new(name, "GeometryNodeTree")
    tree.interface.new_socket("Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    tree.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    n, L = tree.nodes, tree.links
    gi, go = n.new("NodeGroupInput"), n.new("NodeGroupOutput")
    m2p = n.new("GeometryNodeMeshToPoints")
    inst = n.new("GeometryNodeInstanceOnPoints")
    oi = n.new("GeometryNodeObjectInfo")
    oi.inputs["Object"].default_value = proto
    sc = n.new("GeometryNodeInputNamedAttribute")
    sc.data_type = "FLOAT_VECTOR"
    sc.inputs["Name"].default_value = "scl"
    yw = n.new("GeometryNodeInputNamedAttribute")
    yw.data_type = "FLOAT"
    yw.inputs["Name"].default_value = "yaw"
    comb = n.new("ShaderNodeCombineXYZ")
    L.new(gi.outputs[0], m2p.inputs["Mesh"])
    L.new(m2p.outputs[0], inst.inputs["Points"])
    L.new(oi.outputs["Geometry"], inst.inputs["Instance"])
    L.new(sc.outputs["Attribute"], inst.inputs["Scale"])
    L.new(yw.outputs["Attribute"], comb.inputs["Z"])
    L.new(comb.outputs[0], inst.inputs["Rotation"])
    L.new(inst.outputs[0], go.inputs[0])
    mod.node_group = tree
    return ob


def colour_noise(x, z, spread):
    """Stands in for Unity's Perlin noise between healthy and dry colours."""
    s = spread * 0.35
    v = np.sin(x * s * 1.3 + 0.7) * np.sin(z * s * 1.7 + 1.9) + 0.5 * np.sin((x + z) * s * 3.1)
    return np.clip(v * 0.4 + 0.5, 0, 1)


def place(density, max_per_cell, eye, reach, dither_seed, old=False):
    """Tufts per detail cell as CampusTerrainBuilder.ReadDetail (or ReadGrass when old)."""
    j0 = max(int((eye.x - reach - info["minX"]) / GCELL), 0)
    j1 = min(int((eye.x + reach - info["minX"]) / GCELL) + 1, GRES)
    i0 = max(int((eye.y - reach - info["minZ"]) / GCELL), 0)
    i1 = min(int((eye.y + reach - info["minZ"]) / GCELL) + 1, GRES)
    d = density[i0:i1, j0:j1]
    if old:
        n = np.where(d < 0.2, 0, np.round(d * 7)).astype(int)
    else:
        want = np.where(d < 0.08, 0.0, d * max_per_cell)
        r = np.random.default_rng(dither_seed).random(want.shape)
        n = (np.floor(want) + (r < want - np.floor(want))).astype(int)
    ii, jj = np.nonzero(n)
    reps = n[ii, jj]
    ii, jj = np.repeat(ii, reps) + i0, np.repeat(jj, reps) + j0
    rng = np.random.default_rng(dither_seed + 99)
    x = info["minX"] + (jj + rng.random(len(jj))) * GCELL
    z = info["minZ"] + (ii + rng.random(len(ii))) * GCELL
    keep = np.hypot(x - eye.x, z - eye.y) < reach
    return x[keep], z[keep]


def heights(x, z):
    return np.array([ground(a, b) for a, b in zip(x, z)], np.float32)


old_grass = np.fromfile(os.path.join(DATA, "terrain_grass.bytes"), np.uint8).reshape(GRES, GRES) / 255.0
new_details = np.fromfile(os.path.join(DATA, "terrain_details.bytes"), np.uint8).reshape(GRES, GRES, 5) / 255.0
card_protos = [card_object("Card%d" % k, d[0]) for k, d in enumerate(DETAILS[:4])] + [clump_object()]
old_proto = card_object("CardOld", OLD[0])


def detail_set(tag, eye, specs, protos, reach, old=False):
    objs = []
    for k, (spec, proto) in enumerate(zip(specs, protos)):
        _, healthy, dry, wr, hr, spread, per = spec
        dens = old_grass if old else new_details[:, :, k]
        x, z = place(dens, per, eye, reach, 31 + k, old)
        if len(x) == 0:
            continue
        rng = np.random.default_rng(7 + k)
        y = heights(x, z)
        if proto.name == "Clump":
            yaw = rng.uniform(0, 6.28, len(x))
        else:   # camera-facing card: turn -Y towards the eye
            yaw = np.arctan2(eye.x - x, -(eye.y - z))
        w = rng.uniform(*wr, len(x))
        hgt = rng.uniform(*hr, len(x))
        scale = np.stack([w, w, hgt], 1) if proto.name != "Clump" else np.stack([w, w, hgt], 1)
        c = colour_noise(x, z, spread)[:, None]
        tint = np.array(healthy)[None] * (1 - c) + np.array(dry)[None] * c
        tint = np.concatenate([tint ** 2.2, np.ones((len(x), 1))], 1)   # linear for Blender
        objs.append(scatter("%s%d" % (tag, k), proto, np.stack([x, z, y - 0.02], 1), yaw, scale, tint))
        print("  %s layer %d: %d" % (tag, k, len(x)))
    return objs


def painted_ground(eye, fwd, size=170.0, px=2048):
    """The ground patch in front of the camera, painted from the layers (URP terrain, simplified):
    each layer's texture at its tile size, splat weights, height blending, and the mask's occlusion."""
    cx, cz = eye.x + fwd.x * size * 0.38, eye.y + fwd.y * size * 0.38
    x0, z0 = cx - size / 2, cz - size / 2
    ob = terrain_patch("Painted", x0, x0 + size, z0, z0 + size, 1)
    me = ob.data
    # Image: row 0 = south.
    u = x0 + (np.arange(px) + 0.5) / px * size
    v = z0 + (np.arange(px) + 0.5) / px * size
    U, V = np.meshgrid(u, v)
    sres = info["splatResolution"]
    sc = info["sizeX"] / sres
    fx = np.clip((U - info["minX"]) / sc - 0.5, 0, sres - 1.001)
    fz = np.clip((V - info["minZ"]) / sc - 0.5, 0, sres - 1.001)
    j, i = fx.astype(int), fz.astype(int)
    tx, tz = (fx - j)[..., None], (fz - i)[..., None]
    sp = g["splat"]
    w = (sp[i, j] * (1 - tx) + sp[i, j + 1] * tx) * (1 - tz) + (sp[i + 1, j] * (1 - tx) + sp[i + 1, j + 1] * tx) * tz
    cols, hs = [], []
    for k, (spec, tex, hh) in enumerate(zip(LAYERS, layer_tex, layer_h)):
        tile = spec[6]
        a = ((U / tile) % 1 * 512).astype(int) % 512
        b = ((V / tile) % 1 * 512).astype(int) % 512
        cols.append(tex[b, a])
        hs.append(hh[b, a])
    hs = np.stack(hs, -1)
    # URP's height blend: weights plus height, keep what is within the transition of the top.
    trans = 0.25
    hw = w + hs * (w > 0.01)
    top = hw.max(-1, keepdims=True)
    bw = np.maximum(hw - (top - trans), 0) * (w > 0.01)
    bw /= bw.sum(-1, keepdims=True) + 1e-6
    col = sum(bw[..., k:k + 1] * cols[k] for k in range(4))
    ao = 0.55 + 0.45 * np.sqrt(np.clip((hs * bw).sum(-1) / 1.0, 0, 1))
    col = col * ao[..., None]
    path = os.path.join(TEX, "painted_ground.png")
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8)).save(path)
    hpath = os.path.join(TEX, "painted_height.png")
    Image.fromarray((np.clip((hs * bw).sum(-1), 0, 1) * 255).astype(np.uint8)).save(hpath)
    # UVs from world position.
    co = np.zeros(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    loops = np.zeros(len(me.loops), np.int32)
    me.loops.foreach_get("vertex_index", loops)
    uvl = me.uv_layers.new()
    uvs = np.stack([(co[loops, 0] - x0) / size, (co[loops, 1] - z0) / size], 1)
    uvl.data.foreach_set("uv", uvs.astype(np.float32).ravel())
    m = bpy.data.materials.new("Painted")
    m.use_nodes = True
    n, L = m.node_tree.nodes, m.node_tree.links
    img = n.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(path)
    p = n["Principled BSDF"]
    L.new(img.outputs["Color"], p.inputs["Base Color"])
    p.inputs["Roughness"].default_value = 0.92
    himg = n.new("ShaderNodeTexImage")
    himg.image = bpy.data.images.load(hpath)
    himg.image.colorspace_settings.name = "Non-Color"
    bump = n.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.35
    bump.inputs["Distance"].default_value = 0.02
    L.new(himg.outputs["Color"], bump.inputs["Height"])
    L.new(bump.outputs["Normal"], p.inputs["Normal"])
    me.materials.clear()
    me.materials.append(m)
    ob.location.z = 0.01
    return ob


tour_ground = bpy.data.objects["Terrain"]
os.makedirs(OUT, exist_ok=True)
for name in wanted:
    x, z, eh, lx, lz, lh, lens = VIEWS[name]
    eye = Vector((x, z, ground(x, z) + eh))
    tgt = Vector((lx, lz, ground(lx, lz) + lh))
    cam.lens = lens
    cam_o.location = eye
    cam_o.rotation_euler = (tgt - eye).to_track_quat("-Z", "Y").to_euler()
    fwd = Vector((tgt.x - eye.x, tgt.y - eye.y)).normalized()
    shots = []
    print("view", name)
    if not AFTER_ONLY:
        before = detail_set("Old", eye, [OLD], [old_proto], 45.0, old=True)
        scene.render.filepath = os.path.join(OUT, "detail_%s_before.png" % name)
        bpy.ops.render.render(write_still=True)
        shots.append(scene.render.filepath)
        for o in before:
            bpy.data.objects.remove(o)
    after = detail_set("New", eye, DETAILS, card_protos, 120.0)
    painted = painted_ground(eye, fwd)
    scene.render.filepath = os.path.join(OUT, "detail_%s_after.png" % name)
    bpy.ops.render.render(write_still=True)
    shots.append(scene.render.filepath)
    for o in after + [painted]:
        bpy.data.objects.remove(o)
    if AFTER_ONLY:
        out = os.path.join(OUT, "detail_%s.png" % name)
        Image.open(shots[0]).save(out)
        print("wrote", out)
        continue
    a, b = Image.open(shots[0]).convert("RGB"), Image.open(shots[1]).convert("RGB")
    sheet = Image.new("RGB", (a.width + b.width + 6, a.height), (255, 255, 255))
    sheet.paste(a, (0, 0))
    sheet.paste(b, (a.width + 6, 0))
    d = ImageDraw.Draw(sheet)
    for label, ox in (("before", 12), ("after", a.width + 18)):
        d.rectangle([ox - 4, 8, ox + 64, 30], fill=(0, 0, 0))
        d.text((ox, 12), label, fill=(255, 255, 255))
    out = os.path.join(OUT, "detail_%s.png" % name)
    sheet.save(out)
    print("wrote", out)
