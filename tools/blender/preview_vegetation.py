#!/usr/bin/env python3
"""Renders the game's plants (tools/blender/plants.py, the mirror of Vegetation.cs) in a row on
a patch of grass, for checking the trees, bushes and tall grass without Unity.

    python tools/blender/preview_vegetation.py [--lod1] [--out PATH] [--size 960x540]

Writes tools/blender/preview/vegetation.png (gitignored) unless --out is given.
"""
import math
import os
import sys

import bpy  # noqa: F401  (before plants and bmesh)

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import plants  # noqa: E402


def arg(name, default):
    if name in sys.argv:
        return sys.argv[sys.argv.index(name) + 1]
    return default


def main():
    coarse = "--lod1" in sys.argv
    out = arg("--out", os.path.join(HERE, "preview", "vegetation%s.png" % ("_lod1" if coarse else "")))
    w, h = (int(v) for v in arg("--size", "960x540").split("x"))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    # Plants left to right: shrub, flowering shrub, tall grass, broadleaf, eucalyptus, pine.
    order = [(3, -9.0, 4.0), (4, -6.0, 5.5), (5, -3.2, 3.0), (0, 1.5, -1.0), (1, 9.5, -2.0), (2, 17.0, -3.0)]
    for kind, x, y in order:
        ob = plants.plant_object(kind, coarse)
        ob.location = (x, y, 0.0)
        ob.rotation_euler = (0, 0, kind * 0.7)
    # Ground.
    bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 0, 0))
    g = bpy.context.active_object
    gm = bpy.data.materials.new("Ground")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.045, 0.07, 0.025, 1)
    g.data.materials.append(gm)
    # Light and sky.
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 5.0
    sun.data.angle = math.radians(1.5)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-150))
    sc.collection.objects.link(sun)
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.55, 0.68, 0.85, 1)
    bg.inputs["Strength"].default_value = 0.9
    sc.world = world
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.lens = 30
    if "--close" in sys.argv:          # the bushes and the grass up close
        cam.location = (-6.0, -6.5, 1.5)
        cam.rotation_euler = (math.radians(84), 0, 0)
    else:
        cam.location = (4.0, -30.0, 4.0)
        cam.rotation_euler = (math.radians(86), 0, 0)
    sc.collection.objects.link(cam)
    sc.camera = cam
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.filepath = out
    sc.view_settings.view_transform = "AgX" if "AgX" in [v.identifier for v in sc.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    print("Wrote", out)


if __name__ == "__main__":
    main()
