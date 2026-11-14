"""Measures the furniture FBX files in Assets/Models/Props and writes tools/royale/prop_sizes.json:
name -> [width (local X), depth (local Z), min height, max height] in metres.
Run with Blender's Python (pip install bpy): python tools/royale/measure_props.py
"""
import bpy  # noqa: F401  (import before mathutils)
import glob
import json
import os
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
out = {}
for f in sorted(glob.glob(os.path.join(ROOT, "Assets", "Models", "Props", "*.fbx"))):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f)
    xs, ys, zs = [], [], []
    for o in bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        for v in o.bound_box:
            w = o.matrix_world @ Vector(v)
            xs.append(w.x); ys.append(w.y); zs.append(w.z)
    if xs:
        out[os.path.basename(f)[:-4]] = [round(max(xs) - min(xs), 2), round(max(ys) - min(ys), 2),
                                         round(min(zs), 2), round(max(zs), 2)]
with open(os.path.join(ROOT, "tools", "royale", "prop_sizes.json"), "w") as fh:
    json.dump(out, fh, indent=0, sort_keys=True)
print(len(out), "props measured")
