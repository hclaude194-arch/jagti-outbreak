#!/usr/bin/env python3
"""Mock-ups of the time of day (look pass 2: DayCycle.cs, StreetLights.cs, JagtiSky.shader) without Unity.

Builds the map-tour scene (render_map_tour.py, everything before its render loop), then for each
shot:
  - paints the sky as an equirectangular image with a numpy port of Assets/Shaders/JagtiSky.shader
    (gradient, sun glow, cirrus clouds, stars, the two far ridges) and uses it as the world;
  - sets the directional light from DayCycle's Look (sunset sun or the moon);
  - at night, puts a downward spot light under every lamp head and makes the lamp heads glow,
    as StreetLights does in the game (there every lamp glows; the nearest few cast real light);
  - colours each feather flag's sail as OutdoorBuilder does;
then grades the linear render in numpy: exponential fog with DayCycle's colour and density,
post exposure, bloom, ACES, vignette. The Look numbers are copied from DayCycle.cs; keep them in
step. Materials are the map tour's flat colours (roads recoloured to pale concrete), so this
shows light, sky and mood, not the final textures; the flags can't wave in a still.

    python tools/blender/preview_sky.py                       # default shots
    python tools/blender/preview_sky.py porch_sunset          # chosen shots
Writes tools/blender/preview/sky_<shot>.png (800 x 450).
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TOUR = os.path.join(HERE, "render_map_tour.py")

SHOTS = {
    # name: (look, eye x, z, height, target x, z, height, lens)
    "porch_sunset": ("sunset", 72, 365, 1.7, 30, 480, 22, 24),
    "porch_night": ("night", 72, 365, 1.7, 30, 480, 22, 24),
    "spine_night": ("night", -3, 112, 1.7, -9, 10, 4, 24),
    "spine_sunset": ("sunset", -14, 40, 1.7, -8, 160, 5, 24),
    "road_day": ("day", -4, 120, 1.7, -9, 10, 4, 24),
}
DEFAULT = ["porch_sunset", "porch_night", "spine_night"]
wanted = [a for a in sys.argv[1:] if not a.startswith("--")] or DEFAULT

sys.argv = [TOUR, "--fast"]
src = open(TOUR).read()
cut = src.index("os.makedirs(OUT, exist_ok=True)")
g = {"__file__": TOUR, "__name__": "__tour__"}
exec(compile(src[:cut], TOUR, "exec"), g)

import bpy  # noqa: E402
import numpy as np  # noqa: E402
import OpenEXR  # noqa: E402
from mathutils import Vector  # noqa: E402
from PIL import Image  # noqa: E402

scene, cam, cam_o, sun_o, world = g["scene"], g["cam"], g["cam_o"], g["sun_o"], g["world"]
ground, OUT, ROOT = g["ground"], g["OUT"], g["ROOT"]
os.makedirs(OUT, exist_ok=True)
W, H = 800, 450


# ------------------------------------------------------------------ DayCycle's looks (linear colours)
def C(*v):
    return np.array(v, np.float32)


def day_look():
    return dict(light=(36.0, -38.0), light_colour=C(1, 0.89, 0.74), light_int=1.5, sky_sun=(36.0, -38.0),
                fog=C(0.63, 0.70, 0.79), fog_density=0.0009,
                zenith=C(0.05, 0.17, 0.50), horizon=C(0.46, 0.56, 0.70), below=C(0.40, 0.40, 0.38),
                glow=C(0.22, 0.18, 0.10), disc=C(9, 8, 6.5), cloud_lit=C(0.95, 0.93, 0.90),
                cloud_shade=C(0.52, 0.56, 0.64), hills=C(0.50, 0.57, 0.66), glow_height=5.0, gradient=0.55,
                cover=0.42, opacity=0.85, stars=0.0, lamps=0.0, exposure=0.1,
                amb_sky=C(0.50, 0.60, 0.76), amb_eq=C(0.56, 0.53, 0.46), amb_ground=C(0.24, 0.23, 0.17))


def sunset_look(elev=7.0):
    return dict(light=(max(elev, 4.0), 150.0), light_colour=C(1, 0.60, 0.32), light_int=1.35, sky_sun=(elev, 150.0),
                fog=C(0.82, 0.60, 0.44), fog_density=0.0012,
                zenith=C(0.12, 0.20, 0.38), horizon=C(0.70, 0.33, 0.10), below=C(0.36, 0.28, 0.24),
                glow=C(0.75, 0.26, 0.04), disc=C(22, 13, 4), cloud_lit=C(0.62, 0.36, 0.22),
                cloud_shade=C(0.27, 0.27, 0.33), hills=C(0.22, 0.16, 0.17), glow_height=6.0, gradient=0.35,
                cover=0.55, opacity=0.8, stars=0.0, lamps=0.1, exposure=-0.1,
                amb_sky=C(0.40, 0.42, 0.55), amb_eq=C(0.66, 0.47, 0.34), amb_ground=C(0.20, 0.16, 0.12))


def night_look():
    return dict(light=(38.0, -30.0), light_colour=C(0.55, 0.62, 0.95), light_int=0.22, sky_sun=(-9.0, 150.0),
                fog=C(0.20, 0.12, 0.22), fog_density=0.0013,
                zenith=C(0.03, 0.03, 0.11), horizon=C(0.24, 0.075, 0.13), below=C(0.04, 0.04, 0.06),
                glow=C(0.10, 0.02, 0.03), disc=C(0, 0, 0), cloud_lit=C(0.20, 0.10, 0.16),
                cloud_shade=C(0.05, 0.04, 0.09), hills=C(0.012, 0.012, 0.022), glow_height=5.0, gradient=0.32,
                cover=0.36, opacity=0.7, stars=0.9, lamps=1.0, exposure=0.3,
                amb_sky=C(0.10, 0.10, 0.22), amb_eq=C(0.16, 0.10, 0.16), amb_ground=C(0.03, 0.03, 0.045))


LOOKS = {"day": day_look, "sunset": sunset_look, "night": night_look}


def unity_forward(ex, ey):
    ax, ay = math.radians(ex), math.radians(ey)
    return np.array([math.sin(ay) * math.cos(ax), -math.sin(ax), math.cos(ay) * math.cos(ax)])


# ------------------------------------------------------------------ the sky shader, in numpy
def tile_noise(size, px, py, seed):
    """Tiling value noise on a px x py lattice, smoothstep-interpolated (ProceduralAssets.Noise)."""
    rng = np.random.default_rng(seed)
    lat = rng.random((py, px))
    ys, xs = np.mgrid[0:size, 0:size] / size
    fx, fy = xs * px, ys * py
    x0, y0 = np.floor(fx).astype(int), np.floor(fy).astype(int)
    tx, ty = fx - x0, fy - y0
    tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
    x1, y1 = (x0 + 1) % px, (y0 + 1) % py
    x0, y0 = x0 % px, y0 % py
    a = lat[y0, x0] * (1 - tx) + lat[y0, x1] * tx
    b = lat[y1, x0] * (1 - tx) + lat[y1, x1] * tx
    return a * (1 - ty) + b * ty


def cloud_texture(size=256):
    """AtmosphereBuilder.CloudTexture: R long cirrus streaks (8:1), G broken patches."""
    streak, amp, norm = np.zeros((size, size)), 1.0, 0.0
    for o in range(5):
        streak += amp * tile_noise(size, 2 << o, 16 << o, 300 + o * 17)
        norm += amp
        amp *= 0.55
    streak /= norm
    patch, amp, norm = np.zeros((size, size)), 1.0, 0.0
    for o in range(4):
        patch += amp * tile_noise(size, 3 << o, 3 << o, 320 + o * 31)
        norm += amp
        amp *= 0.5
    patch /= norm
    return np.clip((streak - 0.3) * 1.7, 0, 1), np.clip((patch - 0.25) * 1.8, 0, 1)


CLOUD_R, CLOUD_G = cloud_texture()
_rng = np.random.default_rng(77)
STARS = np.zeros((512, 512))
for _ in range(900):
    b = _rng.random()
    STARS[_rng.integers(512), _rng.integers(512)] = 0.25 + 0.75 * b ** 3


def sample(tex, u, v):
    n = tex.shape[0]
    return tex[(np.floor(v * n).astype(int)) % n, (np.floor(u * n).astype(int)) % n]


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def ridge(a, base, seed):
    return (base + 0.011 * np.sin(a * 3 + seed) + 0.007 * np.sin(a * 7 + seed * 2.3)
            + 0.0035 * np.sin(a * 17 + seed * 0.7) + 0.0015 * np.sin(a * 41 + seed * 3.1))


def sky_colour(d, L):
    """d: unit directions (..., 3) in Unity axes (x, y up, z). Mirrors JagtiSky.shader's Frag."""
    s = -unity_forward(*L["sky_sun"])
    y = d[..., 1]
    cs = d @ s
    up = np.clip(y, 0, 1)[..., None]
    gr = up ** L["gradient"]
    sky = L["horizon"] * (1 - gr) + L["zenith"] * gr
    dxz = d[..., [0, 2]] / np.maximum(np.linalg.norm(d[..., [0, 2]], axis=-1, keepdims=True), 1e-4)
    sxz = s[[0, 2]] / max(np.linalg.norm(s[[0, 2]]), 1e-4)
    side = np.clip((dxz @ sxz) * 0.5 + 0.5, 0, 1)
    glow = side ** 3 * np.exp(-np.maximum(y, 0) * L["glow_height"])
    sky = sky + L["glow"] * (glow + np.clip(cs, 0, 1) ** 24 * 0.8)[..., None]
    t = np.clip(-y * 6, 0, 1)[..., None]
    sky = sky * (1 - t) + L["below"] * t
    # clouds
    yy = np.maximum(y, 1e-3)
    uvx, uvz = d[..., 0] / (yy + 0.09), d[..., 2] / (yy + 0.09)
    c1 = sample(CLOUD_R, uvx * 0.18, uvz * 0.18)
    c2 = sample(CLOUD_G, uvz * 0.41, uvx * 0.41)
    shape = c1 * (0.55 + 0.9 * c2)
    dens = np.clip((shape - (1 - L["cover"])) * 3.2, 0, 1) * smoothstep(0, 0.22, y) * L["opacity"]
    dens = np.where(y > 0, dens, 0)[..., None]
    k = (np.clip(cs * 0.5 + 0.5, 0, 1) ** 3)[..., None]
    cloud = L["cloud_shade"] * (1 - k) + L["cloud_lit"] * k
    cloud = cloud + L["glow"] * (np.clip(cs, 0, 1) ** 5 * 1.6 + glow * 0.6)[..., None]
    sky = sky * (1 - dens) + cloud * dens
    # sun disc (0.022 rad)
    edge = math.cos(0.022)
    disc = smoothstep(edge - 0.00025, edge + 0.00025, cs) * float(smoothstep(-0.03, 0.0, np.array(s[1])))
    sky = sky + L["disc"] * (disc[..., None] * (1 - dens * 0.8))
    if L["stars"] > 0:
        a = np.arctan2(d[..., 0], d[..., 2])
        st = sample(STARS, a * 0.6366 * 1.5, y * 1.5)
        sky = sky + (st * st * L["stars"] * np.clip(y * 5, 0, 1))[..., None] * (1 - dens)
    a = np.arctan2(d[..., 0], d[..., 2])
    far, near = ridge(a, 0.022, 1.3), ridge(a, 0.012, 4.1)
    hf = smoothstep(far + 0.0015, far - 0.0015, y)[..., None]
    hn = smoothstep(near + 0.0015, near - 0.0015, y)[..., None]
    sky = sky * (1 - hf) + (sky * 0.45 + L["hills"] * 0.55) * hf
    sky = sky * (1 - hn * 0.85) + L["hills"] * 0.82 * hn * 0.85
    return sky


def equirect_sky(L, w=2048, h=1024):
    """The sky as a Blender equirectangular image (bottom row first)."""
    v, u = np.mgrid[0:h, 0:w]
    # Blender's mapping: u = 0.5 - atan2(y, x) / 2 pi, v = 0.5 + elevation / pi.
    lon = (0.5 - (u + 0.5) / w) * 2 * math.pi
    lat = ((v + 0.5) / h - 0.5) * math.pi
    bx = np.cos(lat) * np.cos(lon)
    by = np.cos(lat) * np.sin(lon)
    bz = np.sin(lat)
    d = np.stack([bx, bz, by], -1)            # Unity axes: x, up, z (= Blender y)
    return sky_colour(d, L).astype(np.float32)


# ------------------------------------------------------------------ scene setup
# Pale concrete roads (owner's photos) instead of the tour's dark asphalt.
road = bpy.data.materials.get("Road")
if road is not None:
    road.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.40, 0.39, 0.36, 1)

FLAGS = {"Yellow": (0.93, 0.76, 0.10), "Red": (0.78, 0.12, 0.13), "Purple": (0.42, 0.20, 0.62),
         "Pink": (0.88, 0.26, 0.58), "Orange": (0.95, 0.47, 0.10), "Lilac": (0.72, 0.60, 0.86),
         "Green": (0.45, 0.72, 0.20), "White": (0.92, 0.92, 0.90), "Blue": (0.16, 0.40, 0.80)}


def srgb_to_linear(c):
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


flag_mats = {}
for name, rgb in FLAGS.items():
    m = bpy.data.materials.new("Flag_" + name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*srgb_to_linear(rgb), 1)
    p.inputs["Roughness"].default_value = 0.8
    for key in ("Transmission Weight", "Subsurface Weight"):
        if key in p.inputs:
            p.inputs[key].default_value = 0.0
    flag_mats[name] = m

glow_mat = bpy.data.materials.new("Street_Lamp_Glow")
glow_mat.use_nodes = True
gp = glow_mat.node_tree.nodes["Principled BSDF"]
gp.inputs["Base Color"].default_value = (0.9, 0.9, 0.88, 1)
gp.inputs["Emission Color"].default_value = (1.0, 0.97, 0.92, 1)

lamps = []          # (head position Blender, kind)
KIND = {"Lamp_Post": 0, "Garden_Lamp": 1, "Bollard_Light": 2, "Flood_Mast": 3}
for o in list(scene.objects):
    base = o.name.split(".")[0]
    if base.startswith("Feather_Flag_") and o.type == "MESH":
        colour = base[len("Feather_Flag_"):]
        for i, slot in enumerate(o.material_slots):
            if slot.material and slot.material.name.startswith("Flag_Cloth"):
                slot.link = "OBJECT"
                slot.material = flag_mats.get(colour)
    elif base in KIND and o.type == "MESH":
        idx = [i for i, s_ in enumerate(o.data.materials) if s_ and "Lamp_Glow" in s_.name]
        if not idx:
            continue
        pts = [o.matrix_world @ o.data.vertices[v].co for poly in o.data.polygons if poly.material_index in idx
               for v in poly.vertices]
        if pts:
            c = sum(pts, Vector()) / len(pts)
            lamps.append((c, KIND[base]))
        for i in idx:
            o.material_slots[i].link = "OBJECT"
            o.material_slots[i].material = glow_mat
print("lamps", len(lamps))

lamp_lights = []


def set_lamps(level):
    for ob in lamp_lights:
        bpy.data.objects.remove(ob)
    lamp_lights.clear()
    gp.inputs["Emission Strength"].default_value = 0.6 + 40.0 * level
    if level < 0.05:
        return
    for pos, kind in lamps:
        if kind == 2:
            ld = bpy.data.lights.new("bollard", "POINT")
            ld.energy, ld.color = 25 * level, (1.0, 0.88, 0.70)
        elif kind == 1:
            ld = bpy.data.lights.new("garden", "POINT")
            ld.energy, ld.color = 250 * level, (1.0, 0.88, 0.70)
        else:
            ld = bpy.data.lights.new("lamp", "SPOT")
            ld.energy = (9000 if kind == 3 else 1500) * level
            ld.color = (1.0, 0.97, 0.92)
            ld.spot_size = math.radians(130 if kind == 0 else 110)
            ld.spot_blend = 0.6
        ld.shadow_soft_size = 0.25
        ob = bpy.data.objects.new(ld.name, ld)
        ob.location = pos - Vector((0, 0, 0.2))
        ob.rotation_euler = (0, 0, 0)     # spot lights point down -Z
        scene.collection.objects.link(ob)
        lamp_lights.append(ob)


sky_img = bpy.data.images.new("JagtiSky", 2048, 1024, float_buffer=True)
wn = world.node_tree.nodes
for n in list(wn):
    if n.type not in ("OUTPUT_WORLD",):
        wn.remove(n)
env = wn.new("ShaderNodeTexEnvironment")
env.image = sky_img
bgn = wn.new("ShaderNodeBackground")
world.node_tree.links.new(env.outputs[0], bgn.inputs[0])
# Unity lights the scene with the trilight ambient, not the sky picture: the camera sees the sky,
# every other ray sees a sky-to-ground gradient in the trilight colours.
geo = wn.new("ShaderNodeNewGeometry")
sep = wn.new("ShaderNodeSeparateXYZ")
world.node_tree.links.new(geo.outputs["Incoming"], sep.inputs[0])
ramp = wn.new("ShaderNodeValToRGB")
mapr = wn.new("ShaderNodeMapRange")
mapr.inputs[1].default_value, mapr.inputs[2].default_value = 1.0, -1.0   # incoming points away from the sky
world.node_tree.links.new(sep.outputs[2], mapr.inputs[0])
world.node_tree.links.new(mapr.outputs[0], ramp.inputs[0])
ramp.color_ramp.elements.new(0.5)
amb = wn.new("ShaderNodeBackground")
world.node_tree.links.new(ramp.outputs[0], amb.inputs[0])
lp = wn.new("ShaderNodeLightPath")
mix = wn.new("ShaderNodeMixShader")
world.node_tree.links.new(lp.outputs["Is Camera Ray"], mix.inputs[0])
world.node_tree.links.new(amb.outputs[0], mix.inputs[1])
world.node_tree.links.new(bgn.outputs[0], mix.inputs[2])
world.node_tree.links.new(mix.outputs[0], wn["World Output"].inputs[0])

scene.render.resolution_x, scene.render.resolution_y = W, H
scene.cycles.samples = 24
scene.cycles.max_bounces = 3
scene.cycles.use_denoising = True
scene.view_settings.view_transform = "Standard"
scene.view_settings.exposure = 0.0
vl = scene.view_layers[0]
vl.use_pass_z = True
if hasattr(scene.render.image_settings, "media_type"):
    scene.render.image_settings.media_type = "MULTI_LAYER_IMAGE"
scene.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
scene.render.image_settings.color_depth = "32"


def read_exr(path):
    f = OpenEXR.File(path)
    ch = {}
    for part in f.parts:
        for name, c in part.channels.items():
            ch[name] = np.asarray(c.pixels, np.float32)

    def get(pass_name, n):
        names = sorted(k for k in ch if k.split(".")[-2:-1] == [pass_name] or k.split(".")[-1] == pass_name)
        if not names:
            raise KeyError("%s not in %s" % (pass_name, sorted(ch)))
        arrs = [ch[k] for k in names]
        if arrs[0].ndim == 3:
            return arrs[0][..., :n]
        order = {"R": 0, "G": 1, "B": 2, "A": 3, "V": 0, "Z": 0}
        arrs = [a for _, a in sorted(zip(names, arrs), key=lambda t: order.get(t[0].split(".")[-1], 9))]
        return np.stack(arrs[:n], -1)
    return get("Combined", 3), get("Depth", 1)


def box_blur(img, r):
    out = img
    for _ in range(3):
        for axis in (0, 1):
            pad = [(0, 0)] * img.ndim
            pad[axis] = (r + 1, r)
            c = np.cumsum(np.pad(out, pad, mode="edge"), axis=axis)
            hi = np.take(c, range(2 * r + 1, c.shape[axis]), axis=axis)
            lo = np.take(c, range(0, c.shape[axis] - 2 * r - 1), axis=axis)
            out = (hi - lo) / (2 * r + 1)
    return out


def aces(x):
    a, b, c, d, e = 2.51, 0.03, 2.43, 0.59, 0.14
    return np.clip((x * (a * x + b)) / (x * (c * x + d) + e), 0, 1)


def to_srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def render(shot):
    look, x, z, eh, lx, lz, lh, lens = SHOTS[shot]
    L = LOOKS[look]()
    sky = equirect_sky(L)
    rgba = np.concatenate([sky, np.ones(sky.shape[:2] + (1,), np.float32)], -1)
    sky_img.pixels.foreach_set(rgba.ravel())
    sky_img.update()
    bgn.inputs[1].default_value = 1.0
    els = ramp.color_ramp.elements                   # ground, equator, sky
    for e, key in zip(els, ("amb_ground", "amb_eq", "amb_sky")):
        e.color = (*[float(v) for v in L[key]], 1.0)
    fwd = unity_forward(*L["light"])
    sun_o.rotation_euler = Vector((fwd[0], fwd[2], fwd[1])).to_track_quat("-Z", "Y").to_euler()
    sun_o.data.energy = 2.3 * L["light_int"]
    sun_o.data.color = tuple(float(v) for v in L["light_colour"])
    sun_o.data.angle = math.radians(1.0)
    set_lamps(L["lamps"])
    eye = Vector((x, z, ground(x, z) + eh))
    tgt = Vector((lx, lz, ground(lx, lz) + lh))
    cam.lens = lens
    cam_o.location = eye
    cam_o.rotation_euler = (tgt - eye).to_track_quat("-Z", "Y").to_euler()
    path = os.path.join(OUT, "_sky_%s.exr" % shot)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    col, depth = read_exr(path)
    os.remove(path)
    col = col[::-1] if col.shape[0] == H and False else col
    zz = depth[..., :1]
    bg = zz > 1e6
    f = np.exp(-L["fog_density"] * np.minimum(zz, 5000))
    c = np.where(bg, col, col * f + L["fog"] * (1 - f))
    c = c * 2 ** L["exposure"]
    lum = (c @ np.array([0.2126, 0.7152, 0.0722], np.float32))[..., None]
    c = np.maximum(lum + (c - lum) * 1.08, 0)
    bright = np.clip(c - 1.0, 0, None)
    c = c + box_blur(bright, 6) * 0.5 + box_blur(bright, 22) * 0.35
    c = aces(c)
    yy, xx = np.mgrid[0:H, 0:W]
    r = np.hypot((xx / W - 0.5) * 2 * (W / H) / 1.6, (yy / H - 0.5) * 2)
    vig = 1 - 0.22 * np.clip((r - 0.45) / 0.9, 0, 1) ** 1.6
    img = to_srgb(c * vig[..., None])
    out = os.path.join(OUT, "sky_%s.png" % shot)
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(out)
    print("wrote", out)


for shot in wanted:
    if shot in SHOTS:
        render(shot)
    else:
        print("unknown shot", shot)
