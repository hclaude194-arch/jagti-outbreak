"""Preview of the sky bus (Assets/Scripts/Royale/SkyBusModel.cs) rendered in Blender.

Translates the model functions to Python with the translator in preview_vehicles.py and builds
them with the numpy MeshKit port, so keep SkyBusModel.cs in the simple forms that file lists.
Run: python tools/royale/preview_skybus.py  (writes tools/royale/preview/skybus.png and skybus_drop.png)
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

SRC = os.path.join(pv.SRC, "SkyBusModel.cs")
COLOURS = [(0.93, 0.93, 0.9), (0.09, 0.13, 0.3), (0.08, 0.12, 0.16), (0.07, 0.07, 0.08), (0.98, 0.55, 0.12),
           (0.98, 0.8, 0.15), (0.8, 0.12, 0.1), (0.15, 0.6, 0.25), (0.42, 0.43, 0.45)]
NAMES = ["White", "Navy", "Glass", "Dark", "Saffron", "Yellow", "Red", "Green", "Metal"]


def model_kits():
    src = open(SRC).read()
    src = re.sub(r"float\[\] (\w+) = \{", r"var \1 = new[] {", src)
    env = dict(vars(pv))
    for i, n in enumerate(NAMES):
        env[n] = i
    env["Slots"] = 9
    kits = {}
    for fn in ("Bus", "Balloon", "Propeller"):
        code = "def %s():\n" % fn + pv.translate(pv.body_of(src, "static Mesh %s()" % fn)) + "\n    return k"
        exec(code, env)
        kits[fn] = env[fn]()
    return kits


def emit(kits, at, yaw, prop_spin=0.0):
    rot = pb.ry(yaw)
    pos = np.asarray(at, float)
    parts = [(kits["Bus"], np.zeros(3), np.eye(3)), (kits["Balloon"], np.zeros(3), np.eye(3))]
    for s in (-1, 1):
        parts.append((kits["Propeller"], np.array([s * 3.6, 1.1, -0.2]), pb.rz(prop_spin + s * 20)))
    for n, (kit, off, lr) in enumerate(parts):
        for m, (_, slot, verts, faces) in enumerate(kit.parts):
            world = [pb.to_blender(pos + rot @ (off + lr @ v)) for v in verts]
            mesh = bpy.data.meshes.new("s%d_%d" % (n, m))
            mesh.from_pydata(world, [], [tuple(reversed(f)) for f in faces])
            mesh.update()
            for poly in mesh.polygons:
                poly.use_smooth = slot in (0, 4, 1) and n == 1
            ob = bpy.data.objects.new(mesh.name, mesh)
            bpy.context.scene.collection.objects.link(ob)
            ob.data.materials.append(pb.material("sb_%d" % slot, COLOURS[slot]))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    kits = model_kits()
    emit(kits, (0, 0, 0), 0)
    # Ground far below: fields and a few blocks, hazy.
    g = bpy.data.meshes.new("ground")
    g.from_pydata([(-3000, -3000, -300), (3000, -3000, -300), (3000, 3000, -300), (-3000, 3000, -300)], [], [(0, 1, 2, 3)])
    go = bpy.data.objects.new("ground", g); sc.collection.objects.link(go)
    go.data.materials.append(pb.material("ground", (0.32, 0.4, 0.22)))
    rng = np.random.default_rng(3)
    for i in range(40):
        x, y = rng.uniform(-300, 300), rng.uniform(-100, 500)
        bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, -295))
        ob = bpy.context.active_object
        ob.scale = (rng.uniform(15, 40), rng.uniform(10, 30), rng.uniform(6, 14))
        ob.data.materials.append(pb.material("block", (0.85, 0.84, 0.8)))
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 5.0; sun.angle = 0.1
    so = bpy.data.objects.new("sun", sun); so.rotation_euler = (math.radians(40), math.radians(15), math.radians(-30)); sc.collection.objects.link(so)
    world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.6, 0.85, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.7
    cam = bpy.data.cameras.new("cam"); cam.clip_end = 5000
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    sc.render.engine = "CYCLES"; sc.cycles.samples = 48
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.view_settings.view_transform = "Standard"
    os.makedirs(pv.OUT, exist_ok=True)

    def aim(loc, target, lens):
        co.location = loc
        d = np.asarray(target, float) - np.asarray(loc, float)
        yaw = math.atan2(d[1], d[0]) - math.pi / 2
        pitch = math.atan2(d[2], math.hypot(d[0], d[1]))
        co.rotation_euler = (math.pi / 2 + pitch, 0, yaw)
        cam.lens = lens

    # Hero view: side-front, a little below the bus.
    aim((46, 40, -2), (0, 0, 15), 28)
    sc.render.filepath = os.path.join(pv.OUT, "skybus.png")
    bpy.ops.render.render(write_still=True)
    # The view aboard: behind and above the rear door, as the game's camera sits.
    aim((-6, -34, 14), (0, 10, 2), 26)
    sc.render.filepath = os.path.join(pv.OUT, "skybus_drop.png")
    bpy.ops.render.render(write_still=True)
    print("wrote", pv.OUT)


if __name__ == "__main__":
    main()
