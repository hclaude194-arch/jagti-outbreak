"""Preview of the loot stash trunk (Assets/Scripts/Royale/StashModel.cs) rendered in Blender.

Translates the model functions to Python with the translator in preview_vehicles.py and builds them
with the numpy MeshKit port, so keep StashModel.cs in the simple forms that file lists. Shows a
closed trunk on its glowing ring and an opened one with its items fanned out in front.
Run: python tools/royale/preview_stash.py  (writes tools/royale/preview/stash.png)
"""
import math
import os
import re
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_bodies as pb  # noqa: E402
import preview_vehicles as pv  # noqa: E402
import preview_guns as pg  # noqa: E402

SRC = os.path.join(pv.SRC, "StashModel.cs")
NAMES = ["Metal", "Black", "Wood", "Olive", "Red", "White", "Brass", "Khaki", "Blue"]
HINGE = np.array([0.0, 0.42, -0.275])


def kits():
    src = open(SRC).read().replace("LootVisuals.", "")
    env = dict(vars(pv))
    for i, n in enumerate(NAMES):
        env[n] = i
    env["PaletteSize"] = 9
    out = {}
    for fn in ("BuildBody", "BuildLid"):
        code = "def %s():\n" % fn + pv.translate(pv.body_of(src, "static Mesh %s()" % fn)) + "\n    return k"
        exec(code, env)
        out[fn] = env[fn]()
    return out


def put(kit, place, name):
    """place(v) maps model-space points (Unity) to world (Unity)."""
    for m, (_, slot, verts, faces) in enumerate(kit.parts):
        world = [pb.to_blender(place(v)) for v in verts]
        mesh = bpy.data.meshes.new("%s_%d" % (name, m))
        mesh.from_pydata(world, [], [tuple(reversed(f)) for f in faces])
        mesh.update()
        ob = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.scene.collection.objects.link(ob)
        ob.data.materials.append(pb.material("st_%d" % slot, pv.COLOURS[slot]))


def trunk(k, at, yaw, swing):
    R = pb.ry(yaw)
    L = pb.rx(-swing)
    at = np.asarray(at, float)
    put(k["BuildBody"], lambda v: at + R @ v, "body")
    put(k["BuildLid"], lambda v: at + R @ (HINGE + L @ v), "lid")


def glow_ring(at, radius):
    mat = bpy.data.materials.new("glow"); mat.use_nodes = True
    nt = mat.node_tree; nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission"); em.inputs[0].default_value = (1.0, 0.62, 0.15, 1); em.inputs[1].default_value = 6
    out = nt.nodes.new("ShaderNodeOutputMaterial"); nt.links.new(em.outputs[0], out.inputs[0])
    for r, w in ((radius, 0.05), (radius * 0.8, 0.02)):
        bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=w, location=pb.to_blender(at + np.array([0, 0.01, 0])))
        o = bpy.context.object; o.scale = (1, 1, 0.15)
        o.data.materials.append(mat)
    light = bpy.data.lights.new("gl", "POINT"); light.energy = 25; light.color = (1, 0.65, 0.25)
    lo = bpy.data.objects.new("gl", light); lo.location = pb.to_blender(at + np.array([0, 0.3, 0.3]))
    bpy.context.scene.collection.objects.link(lo)


def gun_on_floor(key, at, yaw):
    """A loot gun from preview_guns, laid on its side on the floor and turned to yaw."""
    before = set(bpy.data.objects)
    pg.emit(pg.parts(pg.parse_cases("BuildGun")[key], {"Yc": pg.YC}), (0, 0, 0))
    pivot = bpy.data.objects.new("gun", None); bpy.context.scene.collection.objects.link(pivot)
    for o in set(bpy.data.objects) - before - {pivot}:
        o.parent = pivot
    pivot.rotation_euler = (math.radians(90), 0, math.radians(-yaw))
    pivot.location = pb.to_blender(np.asarray(at, float) + np.array([0, 0.04, 0]))


def box(at, size, rgb, name):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pb.to_blender(at + np.array([0, size[1] / 2, 0])))
    o = bpy.context.object; o.scale = (size[0], size[2], size[1])
    o.data.materials.append(pb.material(name, rgb))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    k = kits()
    # A hostel room corner: tiled floor and two plaster walls.
    floor = pb.material("floor", (0.55, 0.5, 0.44))
    wall = pb.material("wall", (0.86, 0.82, 0.74))
    for loc, scale, mat in (((0, 1.5, -0.01), (6, 6, 0.02), floor), ((0, 4.5, 1.5), (6, 0.1, 3), wall), ((-3, 1.5, 1.5), (0.1, 6, 3), wall)):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.object; o.scale = scale; o.data.materials.append(mat)
    # Closed trunk on its glow ring, and an opened one with its items in front (Unity: +Z is into the room's back wall).
    closed = np.array([-1.4, 0.0, 4.05])
    trunk(k, closed, 180, 0)
    glow_ring(closed, 0.94)
    opened = np.array([0.9, 0.0, 4.05])
    trunk(k, opened, 180, 108)
    for n, a in enumerate((-38, 0, 38)):
        d = pb.ry(180 + a) @ np.array([0, 0, 0.95])
        p = opened + d
        if n == 0:
            gun_on_floor("3", p, 180 + a + 90)
        elif n == 1:
            box(p, (0.3, 0.18, 0.22), (0.6, 0.53, 0.36), "ammo")
        else:
            box(p, (0.32, 0.16, 0.24), (0.9, 0.9, 0.88), "medkit")
            box(p + np.array([0, 0.16, 0]), (0.14, 0.012, 0.04), (0.75, 0.1, 0.1), "cross1")
            box(p + np.array([0, 0.16, 0]), (0.04, 0.012, 0.14), (0.75, 0.1, 0.1), "cross2")

    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.0
    so = bpy.data.objects.new("sun", sun); so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-25))
    sc.collection.objects.link(so)
    world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.62, 0.66, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    cam = bpy.data.cameras.new("cam"); cam.lens = 30
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (-0.2, 0.3, 2.1)
    target = np.array([-0.25, 3.6, 0.3])
    d = target - np.array(co.location)
    co.rotation_euler = (math.pi / 2 + math.atan2(d[2], math.hypot(d[0], d[1])), 0, math.atan2(d[1], d[0]) - math.pi / 2)
    sc.render.engine = "CYCLES"; sc.cycles.samples = 64
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.view_settings.view_transform = "Standard"
    os.makedirs(pv.OUT, exist_ok=True)
    sc.render.filepath = os.path.join(pv.OUT, "stash.png")
    bpy.ops.render.render(write_still=True)
    print("wrote", sc.render.filepath)


if __name__ == "__main__":
    main()
