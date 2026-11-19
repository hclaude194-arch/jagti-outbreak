#!/usr/bin/env python3
"""Render a tour of the battle royale map without Unity: the real building, prop and cover models
on the generated terrain, with roads, water channels and trees, seen from named spots.

Builds the scene from Assets/Data (terrain, layout, building_models.json, outdoor_props.json,
cover_props.json) and the FBX files the other tools wrote, then renders each view in VIEWS to
tools/blender/preview/tour_<name>.png. The look is approximate: plain material colours instead of
Unity's textures, no grass, and simpler trees, so it shows layout and scale rather than final art.

Run with the bpy Python:
    python tools/blender/render_map_tour.py                 # every view
    python tools/blender/render_map_tour.py gate mess       # some views
    python tools/blender/render_map_tour.py --fast gate     # quick, low-quality check
    python tools/blender/render_map_tour.py --prefix=before_ girls_hostels_plan   # other file names
"""
import json
import math
import os
import sys

import bpy  # must come first
import bmesh
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "Assets", "Data")
OUT = os.path.join(os.path.dirname(__file__), "preview")
FAST = "--fast" in sys.argv

info = json.load(open(os.path.join(DATA, "campus_terrain.json")))
layout = json.load(open(os.path.join(DATA, "campus_layout.json")))
res = info["heightmapResolution"]
h = np.fromfile(os.path.join(DATA, "terrain_height.bytes"), "<u2").reshape(res, res) / 65535.0
h = info["minHeight"] + h * info["heightRange"]
sres = info["splatResolution"]
splat = np.fromfile(os.path.join(DATA, "terrain_splat.bytes"), np.uint8).reshape(sres, sres, 4) / 255.0
cell = info["sizeX"] / (res - 1)


def ground(x, z):
    """Bilinear height at world (x, z)."""
    fx = min(max((x - info["minX"]) / cell, 0), res - 1.001)
    fz = min(max((z - info["minZ"]) / cell, 0), res - 1.001)
    j, i = int(fx), int(fz)
    tx, tz = fx - j, fz - i
    a = h[i, j] * (1 - tx) + h[i, j + 1] * tx
    b = h[i + 1, j] * (1 - tx) + h[i + 1, j + 1] * tx
    return float(a * (1 - tz) + b * tz)


# Blender scene axes here: X = world X, Y = world Z (north), Z = up.
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def material(name, rgb, rough=0.85):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*rgb, 1)
    p.inputs["Roughness"].default_value = rough
    return m


def link(ob):
    scene.collection.objects.link(ob)
    return ob


# ------------------------------------------------------------------ terrain
# Full detail (2 m) over the campus, 8 m beyond it out to the horizon.
def terrain_patch(name, x0, x1, z0, z1, step):
    j0, j1 = int((x0 - info["minX"]) / cell), int((x1 - info["minX"]) / cell)
    i0, i1 = int((z0 - info["minZ"]) / cell), int((z1 - info["minZ"]) / cell)
    jj = np.arange(max(j0, 0), min(j1, res - 1) + 1, step)
    ii = np.arange(max(i0, 0), min(i1, res - 1) + 1, step)
    nx, nz = len(jj), len(ii)
    J, I = np.meshgrid(jj, ii)
    verts = np.stack([info["minX"] + J * cell, info["minZ"] + I * cell, h[I, J]], -1).reshape(-1, 3)
    r = np.arange(nz - 1)[:, None] * nx
    c = np.arange(nx - 1)[None, :]
    a = (r + c).ravel()
    faces = np.stack([a, a + 1, a + nx + 1, a + nx], -1)
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(verts))
    me.vertices.foreach_set("co", verts.astype(np.float32).ravel())
    me.loops.add(faces.size)
    me.loops.foreach_set("vertex_index", faces.astype(np.int32).ravel())
    me.polygons.add(len(faces))
    me.polygons.foreach_set("loop_start", np.arange(0, faces.size, 4, dtype=np.int32))
    me.update()
    me.validate()
    # Ground paint from the splat (grass, dry grass, soil, rock), with a little noise-free variation.
    pal = np.array([[0.30, 0.40, 0.16], [0.60, 0.54, 0.32], [0.46, 0.34, 0.24], [0.48, 0.46, 0.42]])
    si = np.clip((I * (sres - 1) / (res - 1)).astype(int), 0, sres - 1)
    sj = np.clip((J * (sres - 1) / (res - 1)).astype(int), 0, sres - 1)
    col = (splat[si, sj] @ pal).reshape(-1, 3) ** 2.2
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.concatenate([col, np.ones((len(col), 1))], 1).astype(np.float32).ravel())
    me.polygons.foreach_set("use_smooth", np.ones(len(faces), bool))
    ob = link(bpy.data.objects.new(name, me))
    m = bpy.data.materials.new("Ground")
    m.use_nodes = True
    nodes = m.node_tree.nodes
    vc = nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    m.node_tree.links.new(vc.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
    nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.95
    me.materials.append(m)
    return ob


CAMPUS = (-420, 470, -700, 720)
terrain_patch("Terrain", *CAMPUS, 1)
far = 8
for (x0, x1, z0, z1) in [(-1000, 1048, -1044, CAMPUS[2]), (-1000, 1048, CAMPUS[3], 1004),
                         (-1000, CAMPUS[0], CAMPUS[2], CAMPUS[3]), (CAMPUS[1], 1048, CAMPUS[2], CAMPUS[3])]:
    t = terrain_patch("Far", x0, x1, z0, z1, far // 2)
    t.location.z = -0.3   # tuck the seams under the near patch

# ------------------------------------------------------------------ strips: roads, paths, water


STRIPS = [0]


def strip(name, pts, width, lift, mat):
    dense = []
    for a, b in zip(pts[:-1], pts[1:]):
        L = math.dist(a, b)
        steps = max(1, int(L / 2))
        for s in range(steps):
            dense.append((a[0] + (b[0] - a[0]) * s / steps, a[1] + (b[1] - a[1]) * s / steps))
    dense.append(pts[-1])
    if len(dense) < 2:
        return
    bm = bmesh.new()
    rows = []
    STRIPS[0] += 1
    bias = 0.003 * (STRIPS[0] % 7)   # crossing strips never share a plane
    for i, (x, z) in enumerate(dense):
        q = dense[min(i + 1, len(dense) - 1)]
        p = dense[max(i - 1, 0)]
        dx, dz = q[0] - p[0], q[1] - p[1]
        L = math.hypot(dx, dz) or 1
        nx, nz = -dz / L * width / 2, dx / L * width / 2
        rows.append((bm.verts.new((x + nx, z + nz, ground(x + nx, z + nz) + lift + bias)),
                     bm.verts.new((x - nx, z - nz, ground(x - nx, z - nz) + lift + bias))))
    for (a1, b1), (a2, b2) in zip(rows[:-1], rows[1:]):
        f = bm.faces.new((a1, b1, b2, a2))
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()     # all strips face up (crossings rendered black otherwise)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    me.materials.append(mat)
    link(bpy.data.objects.new(name, me))


asphalt = material("Road", (0.11, 0.11, 0.12), 0.9)
paving = material("Paving", (0.62, 0.55, 0.45), 0.9)
water = material("Water", (0.10, 0.22, 0.26), 0.05)
for r in layout["roads"]:
    p = r["points"]
    pts = [(p[i], p[i + 1]) for i in range(0, len(p), 2)]
    mat = paving if r["width"] < 4 or "path" in r.get("name", "").lower() else asphalt
    strip("Road", pts, r["width"], 0.12, mat)
for w in layout.get("water", []):
    p = w["points"]
    strip("Water", [(p[i], p[i + 1]) for i in range(0, len(p), 2)], w["width"], 0.05, water)
for pool in layout.get("pools", []):
    bpy.ops.mesh.primitive_cylinder_add(radius=pool["radius"], depth=0.3,
                                        location=(pool["x"], pool["z"], ground(pool["x"], pool["z"]) + 0.2))
    bpy.context.active_object.data.materials.append(water)

# Courts and plazas from the layout's areas.
area_mats = {"court": material("Court", (0.36, 0.45, 0.55)), "plaza": paving, "field": material("Field", (0.22, 0.42, 0.18)),
             "track": material("Track", (0.55, 0.25, 0.18)), "parking": asphalt}
for a in layout["areas"]:
    m = area_mats.get(a.get("type"))
    if m is None:
        continue
    if a.get("shape") == "ellipse":
        # Track ring and field: flat discs laid on the ground (the field sits on top).
        k = 64
        rx, rz = a["width"] / 2, a["depth"] / 2
        bm = bmesh.new()
        lift = 0.10 if a["type"] == "track" else 0.14
        ring = [bm.verts.new((a["x"] + rx * math.cos(2 * math.pi * i / k), a["z"] + rz * math.sin(2 * math.pi * i / k),
                              ground(a["x"], a["z"]) + lift)) for i in range(k)]
        bm.faces.new(ring)
        me = bpy.data.meshes.new("Ellipse")
        bm.to_mesh(me)
        me.materials.append(m)
        link(bpy.data.objects.new("Ellipse", me))
        continue
    if a.get("shape") != "box":
        continue
    x, z, wd, dp = a["x"], a["z"], a["width"], a["depth"]
    strip("Area", [(x, z - dp / 2), (x, z + dp / 2)], wd, 0.08, m)

# ------------------------------------------------------------------ models


def import_fbx(path):
    """Imports an FBX and joins it into one object at the origin (Blender local axes as exported)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o)
    if not meshes:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for m in ob.data.materials:
        if m and m.node_tree and "Principled BSDF" in m.node_tree.nodes:
            p = m.node_tree.nodes["Principled BSDF"]
            if "Glass" in m.name:
                p.inputs["Roughness"].default_value = 0.08
                p.inputs["Metallic"].default_value = 0.4
            else:
                p.inputs["Roughness"].default_value = max(0.6, p.inputs["Roughness"].default_value)
    return ob


# Models are exported with local axes (-dX, -dZ, up) from their pivot: a half turn here.
models = json.load(open(os.path.join(DATA, "building_models.json")))["models"]
replaced = set()
for m in models:
    replaced.update(m.get("replaces", []))
    ob = import_fbx(os.path.join(ROOT, m["fbx"]))
    if ob is None:
        continue
    ob.rotation_euler = (0, 0, math.pi)
    ob.location = (m["pivotX"], m["pivotZ"], ground(m["pivotX"], m["pivotZ"]))
    print("building", m["name"])

# Layout buildings without a model yet: plain blocks.
block = material("Block", (0.78, 0.72, 0.62))
for b in layout["buildings"]:
    if b["name"] in replaced or any(b["name"] == mm["name"].replace("_", " ") for mm in models):
        continue
    g = ground(b["x"], b["z"])
    bpy.ops.mesh.primitive_cube_add(size=1, location=(b["x"], b["z"], g + b["height"] / 2 - 0.3))
    o = bpy.context.active_object
    o.scale = (b["width"], b["depth"], b["height"] + 0.6)
    o.rotation_euler.z = -math.radians(b.get("rotation", 0.0))
    o.data.materials.append(block)


def place_props(path):
    d = json.load(open(path))
    protos = {}
    for pm in d["propModels"]:
        ob = import_fbx(os.path.join(ROOT, pm["fbx"]))
        if ob is not None:
            ob.location = (0, 0, -500)
            protos[pm["name"]] = ob
    n = 0
    for p in d["props"]:
        proto = protos.get(p["prop"])
        if proto is None:
            continue
        o = bpy.data.objects.new(p["prop"], proto.data)
        o.location = (p["x"], p["z"], ground(p["gx"], p["gz"]) + p["y"])
        o.rotation_euler = (0, 0, math.pi - math.radians(p["yaw"]))
        link(o)
        n += 1
    return n


print("props", place_props(os.path.join(DATA, "outdoor_props.json")))
print("cover", place_props(os.path.join(DATA, "cover_props.json")))

# ------------------------------------------------------------------ trees (geometry-node instances)


def tree_proto(kind):
    bark = material("Bark", (0.22, 0.16, 0.11))
    leaf = material("Leaf%d" % kind, [(0.13, 0.25, 0.08), (0.10, 0.20, 0.09), (0.16, 0.26, 0.10), (0.20, 0.30, 0.10)][kind])
    bm = bmesh.new()
    M = __import__("mathutils").Matrix
    if kind in (1, 2):   # eucalyptus and chir pine (as ProceduralAssets.cs): tall trunk, crown high up
        h = 12.0 if kind == 1 else 11.5
        bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.24, radius2=0.08, depth=h,
                              matrix=M.Translation((0, 0, h / 2)))
        clumps = ([(0, 0, 11.5, 1.9, 2.8), (0.8, -0.4, 9.2, 1.4, 1.6), (-0.7, 0.6, 8.4, 1.1, 1.3)] if kind == 1 else
                  [(0, 0, 11.2, 1.7, 1.0), (1.3, 0.4, 9.6, 1.4, 0.75), (-1.1, -0.6, 8.8, 1.4, 0.7), (0.3, 1.2, 7.6, 1.1, 0.6)])
        for dx, dy, dz, r, rz in clumps:
            bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0,
                                       matrix=M.Translation((dx, dy, dz)) @ M.Diagonal((r, r, rz, 1.0)))
    elif kind == 3:      # shrubs
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.2,
                                   matrix=__import__("mathutils").Matrix.Translation((0, 0, 0.8)))
    else:                # broadleaf: trunk and a lumpy crown
        bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.3, radius2=0.2, depth=4,
                              matrix=__import__("mathutils").Matrix.Translation((0, 0, 2)))
        for (dx, dy, dz, r) in [(0, 0, 5.6, 2.6), (1.4, 0.5, 5.0, 1.8), (-1.2, -0.8, 5.2, 1.9), (0.3, -1.3, 6.4, 1.7)]:
            bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r,
                                       matrix=__import__("mathutils").Matrix.Translation((dx, dy, dz)))
    me = bpy.data.meshes.new("Tree%d" % kind)
    bm.to_mesh(me)
    me.materials.append(bark)
    me.materials.append(leaf)
    # Faces above the trunk get the leaf material.
    for p in me.polygons:
        if p.center.z > ({1: 6.5, 2: 6.5}.get(kind, 3.0) if kind != 3 else -1):
            p.material_index = 1
        p.use_smooth = True
    ob = bpy.data.objects.new("TreeProto%d" % kind, me)
    link(ob)
    ob.location = (0, 0, -800)
    ob.hide_render = True
    return ob


def instancer(name, proto, pts, scales):
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(pts))
    me.vertices.foreach_set("co", np.asarray(pts, np.float32).ravel())
    a = me.attributes.new("tscale", "FLOAT", "POINT")
    a.data.foreach_set("value", np.asarray(scales, np.float32))
    a2 = me.attributes.new("tyaw", "FLOAT", "POINT")
    a2.data.foreach_set("value", np.random.default_rng(len(pts)).uniform(0, 6.28, len(pts)).astype(np.float32))
    ob = link(bpy.data.objects.new(name, me))
    mod = ob.modifiers.new("Inst", "NODES")
    tree = bpy.data.node_groups.new(name, "GeometryNodeTree")
    tree.interface.new_socket("Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    tree.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    n = tree.nodes
    gi, go = n.new("NodeGroupInput"), n.new("NodeGroupOutput")
    m2p = n.new("GeometryNodeMeshToPoints")
    inst = n.new("GeometryNodeInstanceOnPoints")
    info_n = n.new("GeometryNodeObjectInfo")
    info_n.inputs["Object"].default_value = proto
    sc = n.new("GeometryNodeInputNamedAttribute")
    sc.data_type = "FLOAT"
    sc.inputs["Name"].default_value = "tscale"
    yw = n.new("GeometryNodeInputNamedAttribute")
    yw.data_type = "FLOAT"
    yw.inputs["Name"].default_value = "tyaw"
    comb = n.new("ShaderNodeCombineXYZ")
    L = tree.links
    L.new(gi.outputs[0], m2p.inputs["Mesh"])
    L.new(m2p.outputs[0], inst.inputs["Points"])
    L.new(info_n.outputs["Geometry"], inst.inputs["Instance"])
    L.new(sc.outputs["Attribute"], inst.inputs["Scale"])
    L.new(yw.outputs["Attribute"], comb.inputs["Z"])
    L.new(comb.outputs[0], inst.inputs["Rotation"])
    L.new(inst.outputs[0], go.inputs[0])
    mod.node_group = tree
    return ob


t = info["trees"]
per = {k: ([], []) for k in range(4)}
for k in range(0, len(t), 4):
    x, z, kind, s = t[k], t[k + 1], int(t[k + 2]), t[k + 3]
    if not (-900 < x < 950 and -1000 < z < 960):
        continue
    per[kind][0].append((x, z, ground(x, z) - 0.2))
    per[kind][1].append(s * (0.55 if kind == 3 else 1.0))
for kind, (pts, scales) in per.items():
    if pts:
        instancer("Trees%d" % kind, tree_proto(kind), pts, scales)
        print("trees", kind, len(pts))

# ------------------------------------------------------------------ light, sky, camera

world = bpy.data.worlds.new("W")
world.use_nodes = True
wn = world.node_tree.nodes
bg = wn["Background"]
try:
    sky = wn.new("ShaderNodeTexSky")
    for t_name in ("MULTIPLE_SCATTERING", "NISHITA"):
        try:
            sky.sky_type = t_name
            break
        except TypeError:
            continue
    sky.sun_elevation = math.radians(38)
    sky.sun_rotation = math.radians(150)
    world.node_tree.links.new(sky.outputs[0], bg.inputs[0])
    bg.inputs[1].default_value = 0.25
except Exception as e:   # older or newer sky node: a plain sky colour
    print("sky texture unavailable:", e)
    bg.inputs[0].default_value = (0.55, 0.66, 0.82, 1)
scene.world = world
sun = bpy.data.lights.new("Sun", "SUN")
sun.energy = 3.0
sun.angle = math.radians(1.5)
sun.color = (1.0, 0.95, 0.86)
sun_o = link(bpy.data.objects.new("Sun", sun))
sun_o.rotation_euler = (math.radians(52), 0, math.radians(150))

scene.render.engine = "CYCLES"
scene.cycles.samples = 8 if FAST else 64
scene.cycles.use_denoising = True
scene.cycles.max_bounces = 4
scene.render.resolution_x, scene.render.resolution_y = (800, 450) if FAST else (1600, 900)
scene.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in scene.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
scene.view_settings.look = "None"
scene.view_settings.exposure = -0.6
# Aerial perspective: a mist pass blended over the far distance in the compositor is overkill
# here; the sky's own light and the long lens views read fine without it.
cam = bpy.data.cameras.new("Cam")
cam.clip_end = 4000
cam_o = link(bpy.data.objects.new("Cam", cam))
scene.camera = cam_o

VIEWS = {
    # name: (eye x, z, height above ground, look-at x, z, height above ground there, lens mm)
    "overview": (420, -620, 330, 20, 40, 0, 24),
    "gate": (-150, 600, 1.8, -105, 515, 4, 28),
    "clock_tower": (72, 408, 1.8, 74, 470, 10, 24),
    "academic_block": (215, 250, 14, 80, 300, 8, 24),
    "academic_courtyard": (58, 248, 9.6, 92, 300, 0, 22),
    "amphitheatre": (170, 225, 6, 175, 300, 0, 22),
    "water_terraces": (-75, 395, 3, -95, 150, -6, 26),
    "brahmaputra_hostel": (5, 130, 1.8, -28, 195, 8, 24),
    "shops_street": (-6, 2, 1.8, -32, -80, 3, 24),
    "mess": (-8, -60, 1.8, -32, -125, 4, 24),
    "mess_bank": (9, -178, 1.8, -28, -128, 7, 22),
    "mess_area_aerial": (70, -20, 70, -70, -100, 0, 24),
    "hostels": (110, -190, 6, 50, -120, 4, 24),
    "sports": (120, -300, 10, 30, -280, 0, 22),
    "sindhu_hostel": (60, -330, 8, 55, -470, 0, 24),
    "water_tower_hill": (-80, -190, 2, -39, -254, 20, 26),
    "faculty_residences": (-112, 2, 1.8, -128, -110, 8, 24),
    "girls_hostels": (180, -40, 45, 75, -150, 0, 24),
    # Map fixes (2026-11-19): the clock tower walk from the serpentine, the sunken amphitheatre
    # seen from the ring road outside the academic block, and the mess road and oval loop.
    "clock_walk": (72, 478, 1.8, 72, 400, 6, 26),
    "clock_walk_aerial": (128, 500, 38, 70, 420, 0, 26),
    "amphitheatre_entry": (146, 285, 1.8, 185, 285, -2, 24),
    "amphitheatre_aerial": (158, 345, 26, 186, 278, -2, 24),
    "mess_road": (-48, 6, 1.8, -50, -90, 3, 26),
    "oval_loop": (-104, 12, 22, -106, -90, 0, 26),
    "spine_mess": (6, 6, 1.8, 2, -150, 1, 26),
    "mess_front_aerial": (60, -40, 40, -20, -120, 0, 26),
    # Vipanan and the mess as in the owner's video and photo (2026-11-19).
    "vipanan_forecourt": (-9, -97, 1.7, -36, -66, 2.5, 22),
    "mess_from_forecourt": (-6, -84, 1.7, -36, -118, 4, 22),
    "vipanan_aerial": (25, -128, 32, -30, -74, 0, 24),
}
# Straight-down plans: centre x, z, width shown (m), and which world direction is up in the image.
# The girls' hostels plan is turned like the scale-model photo (west at the top).
PLANS = {
    "girls_hostels_plan": (85, -150, 240, "-X"),
    "campus_plan": (20, -40, 1150, "+Z"),
    "mess_plan": (-70, -95, 280, "-X"),
    "brahmaputra_plan": (-20, 190, 160, "-X"),
    "sindhu_plan": (50, -460, 220, "-X"),
}
os.makedirs(OUT, exist_ok=True)
only = [a for a in sys.argv[1:] if a in VIEWS or a in PLANS]
prefix = next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--prefix=")), "tour_")
UP = {"+Z": 0.0, "-X": math.pi / 2, "-Z": math.pi, "+X": -math.pi / 2}
for name, (x, z, width, up) in PLANS.items():
    if only and name not in only:
        continue
    cam.type = "ORTHO"
    cam.ortho_scale = width
    cam_o.location = (x, z, 600)
    cam_o.rotation_euler = (0, 0, UP[up])
    keep = scene.render.resolution_x, scene.render.resolution_y
    scene.render.resolution_y = scene.render.resolution_x * 3 // 4
    scene.render.filepath = os.path.join(OUT, prefix + name + ".png")
    bpy.ops.render.render(write_still=True)
    scene.render.resolution_x, scene.render.resolution_y = keep
    print("rendered", name)
cam.type = "PERSP"
for name, (x, z, eh, lx, lz, lh, lens) in VIEWS.items():
    if only and name not in only:
        continue
    eye = Vector((x, z, ground(x, z) + eh))
    tgt = Vector((lx, lz, ground(lx, lz) + lh))
    cam.lens = lens
    cam_o.location = eye
    cam_o.rotation_euler = (tgt - eye).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, prefix + name + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", name)
