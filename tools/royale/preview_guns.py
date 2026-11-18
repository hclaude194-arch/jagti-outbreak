"""Preview of the gun and attachment models built in Assets/Scripts/Royale/LootVisuals.cs.

The script reads BuildGun, BuildAttachment and MountsFor straight from the C# source (the part
calls are simple enough to evaluate), builds the parts in Blender and renders every gun upright
with a typical set of attachments fitted. Run: python tools/royale/preview_guns.py
(writes tools/royale/preview/guns.png).
"""
import bpy  # noqa: F401
import math
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Assets", "Scripts", "Royale", "LootVisuals.cs")
OUT = os.path.join(ROOT, "tools", "royale", "preview")
YC = 0.04
COLOURS = {"Metal": (0.32, 0.33, 0.35), "Black": (0.04, 0.04, 0.045), "Wood": (0.36, 0.2, 0.09), "Olive": (0.25, 0.29, 0.15),
           "Red": (0.8, 0.08, 0.05), "White": (0.85, 0.85, 0.82), "Brass": (0.7, 0.5, 0.15), "Khaki": (0.55, 0.47, 0.3), "Blue": (0.12, 0.25, 0.6)}
NAMES = ["Ravi P9", "Chenab SMG", "Banihal 12", "Tawi AR", "Jhelum DMR", "Basohli .45", "Ujh Carbine", "Bhaderwah Double", "Udhampur LMG", "Pir Panjal"]


def clean(expr):
    expr = re.sub(r"(\d)f\b", r"\1", expr.replace("Mathf.", "math."))
    # C# "c ? a : b" -> Python "(a if c else b)" (only the simple forms used in the model code)
    m = re.match(r"^\s*([\w.]+) \? ([^:]+) : (.+)$", expr)
    if m:
        expr = "(%s if %s else %s)" % (m.group(2), m.group(1), m.group(3))
    return expr


def split_args(s):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if ch == "," and depth == 0:
            out.append(cur.strip()); cur = ""
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


def vec(expr, env):
    m = re.match(r"new Vector3\((.*)\)$", expr.strip())
    if m:
        return tuple(eval(clean(a), {"math": math}, env) for a in split_args(m.group(1)))
    if expr.strip() in ("AlongX",):
        return (0, 0, 90)
    if expr.strip() == "Vector3.zero":
        return (0, 0, 0)
    raise ValueError(expr)


def parse_cases(func):
    src = open(SRC).read()
    body = src[src.index("static void " + func):]
    body = body[:body.index("\n        }\n")]
    cases, current, fresh = {}, [], False
    for line in body.splitlines():
        m = re.match(r"\s*(case ([\w.]+)|default):(.*)", line)
        if m:
            if not fresh:
                current = []
            cases[m.group(2) or "default"] = current
            fresh = not m.group(3).strip()
            if m.group(3).strip():
                current.append(m.group(3))
            continue
        if line.strip() and line.strip() not in ("{", "}"):
            fresh = False
        current.append(line)
    return cases


def parts(lines, env):
    """Evaluates the P/Barrel/b.Box/b.Tube/b.Ball calls of one case into solids."""
    env = dict(env)
    solids = []
    for raw in lines:
        line = raw.strip()
        decl = re.match(r"(?:bool|float) (.+);", line)
        if decl:
            for part in decl.group(1).replace("a == AttachmentType.Scope8x", "big_flag").split(","):
                k, v = part.split("=", 1)
                env[k.strip()] = eval(clean(v.strip().replace("a == AttachmentType.Scope8x", "big_flag")), {"math": math}, env)
            continue
        cond = re.match(r"if \((.+?)\) (.+)$", line)
        if cond:
            if not eval(clean(cond.group(1)), {"math": math}, env):
                continue
            line = cond.group(2)
        m = re.match(r"(P|Barrel|b\.Box|b\.Tube|b\.Ball)\((.*)\);", line)
        if not m:
            continue
        kind, args = m.group(1), split_args(m.group(2))
        if args and args[0] == "b":
            args = args[1:]
        ev = lambda a: eval(clean(a), {"math": math}, env)
        slot = lambda a: a.strip()
        if kind == "P":
            x, z, ln, h, th = (ev(a) for a in args[:5])
            sl = slot(args[5]); tilt = ev(args[6]) if len(args) > 6 else 0; side = ev(args[7]) if len(args) > 7 else 0
            solids.append(("box", (x, side, z), (ln, th, h), sl, tilt))
        elif kind == "Barrel":
            x, z, d, ln = (ev(a) for a in args[:4]); sl = slot(args[4]); side = ev(args[5]) if len(args) > 5 else 0
            solids.append(("tube", (x, side, z), (d, ln), sl, 0))
        elif kind == "b.Tube":
            p = vec(args[0], env); d, ln = ev(args[1]), ev(args[2]); sl = slot(args[3])
            solids.append(("tube", (p[0], p[1] - YC, p[2]), (d, ln), sl, 0))
        elif kind == "b.Ball":
            p = vec(args[0], env); size = vec(args[1], env); sl = slot(args[2])
            solids.append(("ball", (p[0], p[1] - YC, p[2]), size, sl, 0))
        elif kind == "b.Box":
            p = vec(args[0], env); size = vec(args[1], env); sl = slot(args[2])
            solids.append(("box", (p[0], p[1] - YC, p[2]), (size[0], size[1], size[2]), sl, 0))
    return solids


def mounts():
    src = open(SRC).read()
    body = src[src.index("public static Mounts MountsFor"):]
    body = body[:body.index("\n        }\n")]
    out = {}
    for line in body.splitlines():
        m = re.match(r"\s*(case (\d+)|default):(.*)", line)
        if not m:
            continue
        idx = int(m.group(2)) if m.group(2) else 9
        d = {}
        for key in ("sight", "muzzle", "grip", "mag"):
            mm = re.search(key + r" = (None|new Vector3\(([^)]*)\))", m.group(3))
            d[key] = None if mm.group(1) == "None" else tuple(float(clean(a)) for a in mm.group(2).split(","))
        d["scope"] = "builtInScope = true" in m.group(3)
        out[idx] = d
    return out


MATS = {}


def material(name):
    if name not in MATS:
        m = bpy.data.materials.new(name); m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*COLOURS[name], 1)
        b.inputs["Roughness"].default_value = 0.45 if name in ("Metal", "Black") else 0.7
        b.inputs["Metallic"].default_value = 0.6 if name == "Metal" else 0.0
        MATS[name] = m
    return MATS[name]


def emit(solids, offset):
    ox, oy, oz = offset
    for kind, p, size, sl, tilt in solids:
        loc = (p[0] + ox, p[1] + oy, p[2] + oz)
        if kind == "box":
            bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
            o = bpy.context.object
            o.scale = size
            o.rotation_euler = (0, math.radians(tilt), 0)
        elif kind == "tube":
            bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=size[0] / 2, depth=size[1], location=loc)
            o = bpy.context.object
            o.rotation_euler = (0, math.radians(90), 0)
        else:
            bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.5, location=loc)
            o = bpy.context.object
            o.scale = size
        o.data.materials.append(material(sl))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    guns = parse_cases("BuildGun")
    att = parse_cases("BuildAttachment")
    mt = mounts()
    order = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "default"]
    # A fitted set per gun: sight, muzzle, grip, magazine (attachment case names).
    fit = {0: ["AttachmentType.RedDot", None, None, None], 1: ["AttachmentType.RedDot", "AttachmentType.Suppressor", "AttachmentType.Grip", "default"],
           2: ["AttachmentType.RedDot", None, None, None], 3: ["AttachmentType.Scope4x", "AttachmentType.Compensator", "AttachmentType.Grip", "default"],
           4: [None, "AttachmentType.Suppressor", "AttachmentType.Grip", None], 5: ["AttachmentType.RedDot", None, None, None],
           6: ["AttachmentType.Scope2x", "AttachmentType.Compensator", "AttachmentType.Grip", None], 7: [None, None, None, None],
           8: ["AttachmentType.RedDot", None, "AttachmentType.Grip", None], 9: [None, "AttachmentType.Suppressor", None, None]}
    for i, key in enumerate(order):
        col, row = i % 2, i // 2
        off = (col * 1.9 - 0.95, 0, -row * 0.42)
        emit(parts(guns[key], {"Yc": YC}), off)
        for place, name in zip(("sight", "muzzle", "grip", "mag"), fit[i]):
            if name is None or mt[i][place] is None or (place == "sight" and mt[i]["scope"]):
                continue
            env = {"Yc": YC, "big_flag": name == "AttachmentType.Scope8x"}
            m = mt[i][place]
            emit(parts(att[name], env), (off[0] + m[0], off[1] + m[1], off[2] + m[2]))
    sc = bpy.context.scene
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 4; so = bpy.data.objects.new("sun", sun)
    so.rotation_euler = (math.radians(55), 0, math.radians(30)); sc.collection.objects.link(so)
    world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.77, 0.8, 1)
    cam = bpy.data.cameras.new("cam"); cam.type = "ORTHO"; cam.ortho_scale = 4.1
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (0.0, -5, -0.88); co.rotation_euler = (math.radians(90), 0, 0)
    sc.render.engine = "CYCLES"; sc.cycles.samples = 32
    sc.render.resolution_x, sc.render.resolution_y = 1800, 1050
    sc.render.film_transparent = False
    os.makedirs(OUT, exist_ok=True)
    sc.render.filepath = os.path.join(OUT, "guns.png")
    bpy.ops.render.render(write_still=True)
    print("wrote", sc.render.filepath, [NAMES[i] for i in range(10)])


if __name__ == "__main__":
    main()
