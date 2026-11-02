#!/usr/bin/env python3
"""Render the generated terrain from eye level, to check the relief without Unity.

Loads Assets/Data/terrain_height.bytes, terrain_splat.bytes, campus_terrain.json and
campus_layout.json, builds a coloured terrain mesh with grey building blocks and simple
trees, and renders a few views to tools/blender/preview/terrain_*.png.

Run with the bpy Python:  python tools/blender/render_terrain_views.py
"""
import json
import math
import os

import bpy  # must come first
import bmesh
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "Assets", "Data")
OUT = os.path.join(os.path.dirname(__file__), "preview")
STEP = 2  # use every 2nd heightmap sample (4 m)

info = json.load(open(os.path.join(DATA, "campus_terrain.json")))
layout = json.load(open(os.path.join(DATA, "campus_layout.json")))
res = info["heightmapResolution"]
h = np.fromfile(os.path.join(DATA, "terrain_height.bytes"), "<u2").reshape(res, res) / 65535.0
h = info["minHeight"] + h * info["heightRange"]
sres = info["splatResolution"]
splat = np.fromfile(os.path.join(DATA, "terrain_splat.bytes"), np.uint8).reshape(sres, sres, 4) / 255.0
cell = info["sizeX"] / (res - 1)


def ground(x, z):
    j = int(round((x - info["minX"]) / cell))
    i = int(round((z - info["minZ"]) / cell))
    return float(h[min(max(i, 0), res - 1), min(max(j, 0), res - 1)])


# Blender scene axes here: X = world X, Y = world Z (north), Z = up.
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

idx = np.arange(0, res, STEP)
n = len(idx)
mesh = bpy.data.meshes.new("Terrain")
verts = [(info["minX"] + j * cell, info["minZ"] + i * cell, h[i, j]) for i in idx for j in idx]
faces = [(r * n + c, r * n + c + 1, (r + 1) * n + c + 1, (r + 1) * n + c) for r in range(n - 1) for c in range(n - 1)]
mesh.from_pydata(verts, [], faces)
pal = np.array([[0.30, 0.42, 0.17], [0.62, 0.56, 0.34], [0.45, 0.33, 0.23], [0.45, 0.43, 0.40]])
col_layer = mesh.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
for k, (i, j) in enumerate((i, j) for i in idx for j in idx):
    w = splat[int(i * (sres - 1) / (res - 1)), int(j * (sres - 1) / (res - 1))]
    c = w @ pal
    col_layer.data[k].color = (c[0] ** 2.2, c[1] ** 2.2, c[2] ** 2.2, 1)
for p in mesh.polygons:
    p.use_smooth = True
terrain = bpy.data.objects.new("Terrain", mesh)
scene.collection.objects.link(terrain)
mat = bpy.data.materials.new("TerrainMat")
mat.use_nodes = True
nodes = mat.node_tree.nodes
attr = nodes.new("ShaderNodeVertexColor")
attr.layer_name = "Col"
mat.node_tree.links.new(attr.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.95
mesh.materials.append(mat)


def simple_mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    return m


bmat = simple_mat("Bld", (0.75, 0.70, 0.62))
for b in layout["buildings"]:
    g = ground(b["x"], b["z"])
    bpy.ops.mesh.primitive_cube_add(size=1, location=(b["x"], b["z"], g + b["height"] / 2 - 0.3))
    o = bpy.context.active_object
    o.scale = (b["width"], b["depth"], b["height"] + 0.6)
    o.data.materials.append(bmat)

# Trees: a cone + sphere per tree near the campus (instanced via linked duplicates).
leaf = simple_mat("Leaf", (0.12, 0.22, 0.07))
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=3.0, location=(0, 0, -1000))
proto = bpy.context.active_object
proto.data.materials.append(leaf)
t = info["trees"]
count = 0
for k in range(0, len(t), 4):
    x, z, kind, s = t[k], t[k + 1], t[k + 2], t[k + 3]
    if abs(x - 40) > 650 or abs(z) > 900:
        continue
    o = bpy.data.objects.new("T", proto.data)
    sc = s * (0.45 if kind == 3 else 1.0)
    o.scale = (sc, sc, sc * (1.6 if kind in (1, 2) else 1.0))
    o.location = (x, z, ground(x, z) + 3.0 * o.scale[2] + (1.5 if kind != 3 else -1.0))
    scene.collection.objects.link(o)
    count += 1
print("trees placed", count)

roadmat = simple_mat("Road", (0.08, 0.08, 0.09))
for r in layout["roads"]:
    p = r["points"]
    pts = [(p[i], p[i + 1]) for i in range(0, len(p), 2)]
    bm = bmesh.new()
    prev = None
    dense = []
    for a, b2 in zip(pts[:-1], pts[1:]):
        L = math.dist(a, b2)
        steps = max(1, int(L / 3))
        for s in range(steps):
            dense.append((a[0] + (b2[0] - a[0]) * s / steps, a[1] + (b2[1] - a[1]) * s / steps))
    dense.append(pts[-1])
    rows = []
    for i, (x, z) in enumerate(dense):
        q = dense[min(i + 1, len(dense) - 1)]
        pp = dense[max(i - 1, 0)]
        dx, dz = q[0] - pp[0], q[1] - pp[1]
        L = math.hypot(dx, dz) or 1
        nx, nz = -dz / L * r["width"] / 2, dx / L * r["width"] / 2
        rows.append((bm.verts.new((x + nx, z + nz, ground(x + nx, z + nz) + 0.25)),
                     bm.verts.new((x - nx, z - nz, ground(x - nx, z - nz) + 0.25))))
    for (a1, b1), (a2, b2_) in zip(rows[:-1], rows[1:]):
        bm.faces.new((a1, b1, b2_, a2))
    me = bpy.data.meshes.new("Road")
    bm.to_mesh(me)
    ob = bpy.data.objects.new("Road", me)
    me.materials.append(roadmat)
    scene.collection.objects.link(ob)

world = bpy.data.worlds.new("W")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.70, 0.80, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
scene.world = world
sun = bpy.data.lights.new("Sun", "SUN")
sun.energy = 3.2
sun_o = bpy.data.objects.new("Sun", sun)
sun_o.rotation_euler = (math.radians(48), 0, math.radians(150))
scene.collection.objects.link(sun_o)
# Haze like the in-game fog.
vol = bpy.data.materials.new("Haze")
vol.use_nodes = True
vn = vol.node_tree.nodes
vn.remove(vn["Principled BSDF"])
pv = vn.new("ShaderNodeVolumePrincipled")
pv.inputs["Density"].default_value = 0.0007
pv.inputs["Color"].default_value = (0.75, 0.80, 0.88, 1)
vol.node_tree.links.new(pv.outputs[0], vn["Material Output"].inputs["Volume"])
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 100))
box = bpy.context.active_object
box.scale = (2100, 2100, 400)
box.data.materials.append(vol)

scene.render.engine = "CYCLES"
scene.cycles.samples = 16
scene.cycles.volume_step_rate = 8
scene.render.resolution_x, scene.render.resolution_y = 960, 540
cam = bpy.data.cameras.new("Cam")
cam.lens = 24
cam.clip_end = 3000
cam_o = bpy.data.objects.new("Cam", cam)
scene.collection.objects.link(cam_o)
scene.camera = cam_o

spawn = layout["spawn"]
VIEWS = {
    # name: (eye x, z, height above ground, look-at x, z, height above ground there)
    "spine": (-9, 60, 1.7, 7, -350, 2),
    "campus_low": (300, -150, 70, 0, 20, 0),
    "track_north": (150, -110, 1.7, -40, 180, 5),
    "gate": (-118, 575, 1.7, 63, 440, 10),
    "edge_east": (215, 80, 1.7, 600, 60, 0),
    "ravine": (330, 60, 1.7, 150, 80, 15),
    "aerial": (-420, -260, 140, 40, 60, 20),
}
os.makedirs(OUT, exist_ok=True)
import sys
only = [a for a in sys.argv[1:] if a in VIEWS]
for name, (x, z, eh, lx, lz, lh) in VIEWS.items():
    if only and name not in only:
        continue
    eye = Vector((x, z, ground(x, z) + eh))
    tgt = Vector((lx, lz, ground(lx, lz) + lh))
    cam_o.location = eye
    cam_o.rotation_euler = (tgt - eye).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, "terrain_%s.png" % name)
    bpy.ops.render.render(write_still=True)
    print("rendered", name)
