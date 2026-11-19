#!/usr/bin/env python3
"""Before/after mock-ups of the look pass (AtmosphereBuilder.cs) without Unity.

Builds the map-tour scene (render_map_tour.py, everything before its render loop), renders each
view's lighting passes with Cycles (surface colour, direct sun with shadows, normals, depth,
ambient occlusion), then shades them in numpy the way Unity's forward renderer does:

  before: the old sun (1.1, 42 deg), trilight ambient with no occlusion, linear fog 150-1100 m,
          no tone mapping (values clip at 1);
  after:  the new sun (1.5, warm, 36 deg), the new trilight with SSAO-like occlusion, exponential
          fog, then the post-processing volume: exposure, white balance, ACES, contrast,
          saturation, split toning, bloom and vignette.

The materials are the map tour's flat colours, not Unity's textures, so this shows the change in
light and grade, not the final frame. Particles and wind can't show in a still.

    python tools/blender/preview_look.py                     # default views
    python tools/blender/preview_look.py gate shops_street   # chosen views
Writes tools/blender/preview/look_<view>.png (before | after side by side).
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TOUR = os.path.join(HERE, "render_map_tour.py")
DEFAULT_VIEWS = ["shops_street", "academic_courtyard", "gate"]

wanted = [a for a in sys.argv[1:] if not a.startswith("--")] or DEFAULT_VIEWS
sys.argv = [TOUR, "--fast"]
src = open(TOUR).read()
cut = src.index("os.makedirs(OUT, exist_ok=True)")
g = {"__file__": TOUR, "__name__": "__tour__"}
exec(compile(src[:cut], TOUR, "exec"), g)

import bpy  # noqa: E402  (already loaded by the tour)
import numpy as np  # noqa: E402
import OpenEXR  # noqa: E402
from mathutils import Vector  # noqa: E402
from PIL import Image  # noqa: E402

scene, cam, cam_o, sun_o, world = g["scene"], g["cam"], g["cam_o"], g["sun_o"], g["world"]
ground, VIEWS, OUT = g["ground"], g["VIEWS"], g["OUT"]
os.makedirs(OUT, exist_ok=True)

# Lighting passes only: the sun's direct light, no bounces and no sky light (Unity's realtime
# forward path has neither; ambient is added below from the trilight colours).
W, H = 800, 450
scene.render.resolution_x, scene.render.resolution_y = W, H
scene.cycles.samples = 24
scene.cycles.max_bounces = 0
scene.cycles.use_denoising = False
world.node_tree.nodes["Background"].inputs[1].default_value = 0.0
world.light_settings.distance = 0.9          # AO pass reach, close to SSAO's short radius
sun_o.data.energy = math.pi                  # so the direct pass is N.L x shadow (0-1)
sun_o.data.color = (1, 1, 1)
sun_o.data.angle = math.radians(0.6)
vl = scene.view_layers[0]
vl.use_pass_diffuse_color = True
vl.use_pass_diffuse_direct = True
vl.use_pass_normal = True
vl.use_pass_z = True
vl.use_pass_ambient_occlusion = True
if hasattr(scene.render.image_settings, "media_type"):    # Blender 5: multilayer is a media type
    scene.render.image_settings.media_type = "MULTI_LAYER_IMAGE"
    scene.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
else:
    scene.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
scene.render.image_settings.color_depth = "32"
scene.view_settings.view_transform = "Standard"


def unity_forward(euler_x, euler_y):
    """Unity's light forward vector for Euler(x, y, 0), as (x, y=up, z)."""
    ax, ay = math.radians(euler_x), math.radians(euler_y)
    return np.array([math.sin(ay) * math.cos(ax), -math.sin(ax), math.cos(ay) * math.cos(ax)])


def render_passes(view, sun_euler):
    fwd = unity_forward(*sun_euler)
    travel = Vector((fwd[0], fwd[2], fwd[1]))          # Blender: X, Y = world Z, Z = up
    sun_o.rotation_euler = travel.to_track_quat("-Z", "Y").to_euler()
    x, z, eh, lx, lz, lh, lens = VIEWS[view]
    eye = Vector((x, z, ground(x, z) + eh))
    tgt = Vector((lx, lz, ground(lx, lz) + lh))
    cam.lens = lens
    cam_o.location = eye
    cam_o.rotation_euler = (tgt - eye).to_track_quat("-Z", "Y").to_euler()
    path = os.path.join(OUT, "_look_%s_%d.exr" % (view, int(sun_euler[0])))
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    f = OpenEXR.File(path)
    ch = {}
    for part in f.parts:
        for name, c in part.channels.items():
            ch[name] = c.pixels

    def get(pass_name, n):
        # Multilayer EXR channels are named like "ViewLayer.DiffCol.R" (one array each), or
        # come grouped as "ViewLayer.DiffCol" (one H x W x C array), depending on the version.
        names = sorted(k for k in ch if k.split(".")[-2:-1] == [pass_name] or k.split(".")[-1] == pass_name)
        if not names:
            raise KeyError("%s not in %s" % (pass_name, sorted(ch)))
        arrs = [np.asarray(ch[k], np.float32) for k in names]
        if arrs[0].ndim == 3:
            return arrs[0][..., :n]
        order = {"R": 0, "G": 1, "B": 2, "X": 0, "Y": 1, "Z": 2, "V": 0}
        names_arrs = sorted(zip(names, arrs), key=lambda t: order.get(t[0].split(".")[-1], 9))
        stacked = np.stack([a for _, a in names_arrs][:n], -1)
        return np.repeat(stacked, n, -1)[..., :n] if stacked.shape[-1] < n else stacked

    data = {"col": get("Diffuse Color", 3), "dir": get("Diffuse Direct", 3), "nrm": get("Normal", 3),
            "z": get("Depth", 1), "ao": get("Ambient Occlusion", 3)}
    os.remove(path)
    # Per-pixel view rays (for the sky), Blender coordinates.
    sw = cam.sensor_width
    ys, xs = np.mgrid[0:H, 0:W]
    px = ((xs + 0.5) / W - 0.5) * sw
    py = (0.5 - (ys + 0.5) / H) * sw * H / W
    rays = np.stack([px, py, -np.full_like(px, lens)], -1)
    R = np.array(cam_o.matrix_world.to_3x3())
    rays = rays @ R.T
    rays /= np.linalg.norm(rays, axis=-1, keepdims=True)
    data["rays"] = rays
    data["sun"] = np.array([-fwd[0], -fwd[2], -fwd[1]])   # towards the sun, Blender axes
    return data


def trilight(n, sky, equator, groundc):
    up = n[..., 2:3]
    a = np.clip(up, 0, 1)
    b = np.clip(-up, 0, 1)
    return equator * (1 - a - b) + sky * a + groundc * b


def sky_colour(rays, sun_dir, zenith, horizon, sun_glow, disc):
    e = np.clip(rays[..., 2:3], -0.2, 1)
    t = np.clip(e, 0, 1) ** 0.45
    col = horizon * (1 - t) + zenith * t
    cosang = np.clip(rays @ sun_dir, -1, 1)[..., None]
    col = col + sun_glow * np.clip(cosang, 0, 1) ** 24
    col = col + disc * (cosang > math.cos(math.radians(1.2)))
    return col


def aces(x):
    a, b, c, d, e = 2.51, 0.03, 2.43, 0.59, 0.14
    return np.clip((x * (a * x + b)) / (x * (c * x + d) + e), 0, 1)


def to_srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def blur(img, r):
    out = img.copy()
    k = np.exp(-0.5 * (np.arange(-3 * r, 3 * r + 1) / r) ** 2)
    k /= k.sum()
    for axis in (0, 1):
        out = np.apply_along_axis(lambda m: np.convolve(m, k, mode="same"), axis, out)
    return out


def shade_before(d):
    col, n, z = d["col"], d["nrm"], d["z"][..., :1]
    bg = z > 1e6
    amb = trilight(n, np.array([0.62, 0.66, 0.72]), np.array([0.50, 0.50, 0.45]), np.array([0.25, 0.23, 0.20]))
    sun = np.array([1.0, 0.95, 0.85]) * 1.1
    c = col * (amb + d["dir"] * sun)
    fog = np.array([0.72, 0.76, 0.79])
    f = np.clip((1100 - z) / (1100 - 150), 0, 1)
    c = c * f + fog * (1 - f)
    sky = sky_colour(d["rays"], d["sun"], np.array([0.42, 0.55, 0.74]), fog, np.array([0.25, 0.2, 0.1]), 1.5)
    c = np.where(bg, sky, c)
    return to_srgb(c)


def shade_after(d):
    col, n, z = d["col"], d["nrm"], d["z"][..., :1]
    bg = z > 1e6
    ao = np.clip(d["ao"][..., :1], 0, 1)
    occ = 1 - (1 - ao) * 0.75
    amb = trilight(n, np.array([0.50, 0.60, 0.76]), np.array([0.56, 0.53, 0.46]), np.array([0.24, 0.23, 0.17]))
    sun = np.array([1.0, 0.89, 0.74]) * 1.5
    c = col * (amb * occ + d["dir"] * sun * (1 - 0.2 * (1 - ao)))
    fog = np.array([0.63, 0.70, 0.79])
    f = np.exp(-0.0009 * np.minimum(z, 5000))
    c = c * f + fog * (1 - f)
    sky = sky_colour(d["rays"], d["sun"], np.array([0.24, 0.42, 0.78]) * 1.1, fog * 1.0,
                     np.array([1.2, 0.95, 0.6]), 30.0)
    c = np.where(bg, sky, c)
    # Post-processing, roughly in URP's order: grading in linear/log space, then ACES, then the
    # vignette in the final pass.
    c = c * 2 ** 0.1                                    # post exposure
    c = c * np.array([1.015, 1.0, 0.975])               # white balance +3
    lc = np.log2(np.maximum(c, 1e-5) / 0.18)           # contrast +18 around middle grey
    c = 0.18 * 2 ** (lc * 1.18)
    lum = (c @ np.array([0.2126, 0.7152, 0.0722]))[..., None]
    c = np.maximum(lum + (c - lum) * 1.08, 0)            # saturation +8
    t = np.clip(np.log2(np.maximum(lum, 1e-5) / 0.18) / 4 + 0.5, 0, 1)
    tint = (np.array([0.47, 0.50, 0.54]) * (1 - t) + np.array([0.54, 0.51, 0.47]) * t) * 2
    c = c * (1 + (tint - 1) * 0.5)                      # split toning
    bright = np.clip(c - 1.15, 0, None)                  # bloom (threshold 1.15)
    c = c + blur(bright, 9) * 0.3 * np.array([1, 0.95, 0.88]) * 2.2
    c = aces(c)
    yy, xx = np.mgrid[0:H, 0:W]
    r = np.hypot((xx / W - 0.5) * 2 * (W / H) / 1.6, (yy / H - 0.5) * 2)
    vig = 1 - 0.22 * np.clip((r - 0.45) / 0.9, 0, 1) ** 1.6
    return to_srgb(c * vig[..., None])


for view in wanted:
    if view not in VIEWS:
        print("unknown view", view)
        continue
    before = shade_before(render_passes(view, (42, -30)))
    after = shade_after(render_passes(view, (36, -38)))
    gap = np.ones((H, 8, 3))
    sheet = np.concatenate([before, gap, after], 1)
    img = Image.fromarray((np.clip(sheet, 0, 1) * 255).astype(np.uint8))
    out = os.path.join(OUT, "look_%s.png" % view)
    img.save(out)
    print("wrote", out)
