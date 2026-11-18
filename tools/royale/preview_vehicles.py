"""Preview of the Phase 5D models built in code: the scooter and utility cart (VehicleModels.cs)
and a zip line tower pair (ZipLine.cs), rendered in Blender.

The script translates the C# model functions to Python line by line (the code uses a small set
of forms: MeshKit calls, const/var declarations, foreach over literal arrays, for loops, Part and
place matrices) and runs them with the numpy MeshKit port from preview_bodies.py. Keep the model
code in those simple forms so this keeps working.
Run: python tools/royale/preview_vehicles.py  (writes tools/royale/preview/vehicles.png)
"""
import bpy  # noqa: F401
import math
import os
import re
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_bodies as pb  # noqa: E402

ROOT = pb.ROOT
OUT = pb.OUT
SRC = os.path.join(ROOT, "Assets", "Scripts", "Royale")
V, UP = pb.V, pb.UP

METAL, BLACK, WOOD, OLIVE, RED, WHITE, BRASS, KHAKI, BLUE = range(9)
COLOURS = {METAL: (0.32, 0.33, 0.35), BLACK: (0.05, 0.05, 0.055), WOOD: (0.55, 0.36, 0.2), OLIVE: (0.3, 0.34, 0.2),
           RED: (0.75, 0.1, 0.1), WHITE: (0.9, 0.9, 0.88), BRASS: (0.75, 0.6, 0.25), KHAKI: (0.6, 0.53, 0.36), BLUE: (0.15, 0.25, 0.5)}


# ---------------------------------------------------------------- C# shims

class Kit(pb.Kit):
    def __init__(self, *_):
        super().__init__()
        self.place = None

    def add(self, verts, faces):
        verts = np.array(verts, float)
        if self.place is not None:
            pos, rot = self.place
            verts = verts @ rot.T + pos
        self.parts.append((None, self.slot, verts, faces))

    def Box(self, *a): self.box(*a)
    def Tube(self, *a): self.tube(*a)
    def Ellipsoid(self, *a): self.ellipsoid(*a)
    def Finish(self, *a): return self


class Xf:
    """A transform shim (position and rotation relative to a parent)."""
    def __init__(self, parent=None):
        self.t = pb.T(parent.t if parent else None)

    @property
    def transform(self): return self

    def SetParent(self, parent, _=False):
        self.t.parent = parent.t

    localPosition = property(lambda s: s.t.lp, lambda s, v: setattr(s.t, "lp", np.asarray(v, float)))
    localRotation = property(lambda s: s.t.lr, lambda s, v: setattr(s.t, "lr", v))
    position = property(lambda s: s.t.pos, lambda s, v: setattr(s.t, "lp", np.asarray(v, float)))

    def AddComponent(self, *_): return Dummy()


class Dummy:
    def __getattr__(self, _): return Dummy()
    def __setattr__(self, *_): pass
    def __call__(self, *a, **k): return Dummy()


PARTS = []   # (kit, transform)


def Part(name, parent, pos, mesh, palette):
    x = Xf(parent)
    x.localPosition = pos
    PARTS.append((mesh, x))
    return x


def TRS(pos, rot, _scale):
    return (np.asarray(pos, float), rot)


def mag(v): return float(np.linalg.norm(v))
def sqrmag(v): return float(np.dot(v, v))


# ---------------------------------------------------------------- C# -> Python

def body_of(src, signature):
    i = src.index(signature)
    i = src.index("{", i)
    depth, j = 0, i
    while True:
        if src[j] == "{": depth += 1
        elif src[j] == "}":
            depth -= 1
            if depth == 0: break
        j += 1
    return src[i + 1:j]


def split_top(s, sep=","):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch in "([{": depth += 1
        elif ch in ")]}": depth -= 1
        if ch == sep and depth == 0:
            out.append(cur); cur = ""
        else:
            cur += ch
    out.append(cur)
    return [o.strip() for o in out if o.strip()]


def expr(e):
    e = re.sub(r"(\d)f\b", r"\1", e)
    e = e.replace("new Vector3(", "V(").replace("new Vector2(", "(").replace("Vector3.zero", "V(0,0,0)")
    e = e.replace("Vector3.up", "UP").replace("Vector3.forward", "V(0,0,1)").replace("Vector3.one", "V(1,1,1)")
    e = e.replace("Quaternion.identity", "np.eye(3)").replace("Matrix4x4.identity", "None").replace("Matrix4x4.TRS", "TRS")
    e = re.sub(r"Quaternion\.LookRotation\(([^,]+), UP\)", r"pb.look(\1, UP)", e)
    e = re.sub(r"\(float\)", "", e)
    e = re.sub(r"new\[\] \{([^}]*)\}", r"(\1,)", e)
    e = re.sub(r"([\w\)]+(?:\.\w+)?)\.normalized", r"pb.norm(\1)", e)
    e = re.sub(r"\(([^()]+)\)\.sqrMagnitude", r"sqrmag(\1)", e)
    e = re.sub(r"(\w+)\.sqrMagnitude", r"sqrmag(\1)", e)
    e = re.sub(r"(\w+)\.magnitude", r"mag(\1)", e)
    e = re.sub(r"new GameObject\([^)]*\)", "Xf()", e).replace("new MeshKit(", "Kit(")
    e = e.replace("true", "True").replace("false", "False").replace("Mathf.", "math.").replace("LootVisuals.PaletteSize", "9")
    e = e.replace("Matrix4x4 ", "").replace("&&", " and ").replace("||", " or ")
    e = re.sub(r"\btransform\b", "self_transform", e)
    m = re.match(r"^(.+?) \? (.+?) : (.+)$", e)
    if m:
        e = "(%s if %s else %s)" % (m.group(2), m.group(1), m.group(3))
    return e


TYPES = r"(?:const float|const int|float|int|var|Vector3|Vector2|Transform|bool|GameObject|Quaternion)"


def statements(body):
    """Splits a C# body into (kind, text) tokens: 'stmt', 'open' ({), 'close' (}), keeping for/foreach headers."""
    toks, cur, depth, inner = [], "", 0, 0
    i = 0
    body = re.sub(r"//[^\n]*", "", body)
    while i < len(body):
        ch = body[i]
        if ch == "(": depth += 1
        if ch == ")": depth -= 1
        if depth == 0 and ch in "{};":
            text = cur.strip()
            if ch == ";":
                toks.append(("stmt", text))
            elif ch == "{":
                if re.match(r"(foreach|for|if)\b", text):
                    toks.append(("head", text)); toks.append(("open", ""))
                elif text == "":
                    toks.append(("open", ""))
                else:          # an array initialiser or similar inside a statement
                    cur += ch; i += 1; inner += 1
                    continue
            elif inner > 0:
                cur += ch; i += 1; inner -= 1
                continue
            else:
                if text: toks.append(("stmt", text))
                toks.append(("close", ""))
            cur = ""
        elif depth == 0 and re.match(r"(foreach|for|if) ?\(", cur.strip() + ch) is None and False:
            pass
        else:
            cur += ch
            # a header followed directly by a single statement: "foreach (...)\n stmt;"
            m = re.match(r"^\s*(foreach|for|if) \(", cur)
            if m and depth == 0 and ch == ")":
                toks.append(("head", cur.strip())); toks.append(("single", "")); cur = ""
        i += 1
    return toks


def translate(body, indent=1):
    out, level, singles = [], indent, []
    pend_single = False
    for kind, text in statements(body):
        pad = "    " * level
        if kind == "head":
            m = re.match(r"foreach \(\w+ (\w+) in (.+)\)$", text)
            if m:
                line = "for %s in %s:" % (m.group(1), expr(m.group(2)))
            else:
                m = re.match(r"for \(int (\w+) = (.+?); \1 (<=|<) (.+?); \1\+\+\)$", text)
                if m:
                    hi = expr(m.group(4)) + (" + 1" if m.group(3) == "<=" else "")
                    line = "for %s in range(%s, %s):" % (m.group(1), expr(m.group(2)), hi)
                else:
                    m = re.match(r"if \((.+)\)$", text)
                    line = "if %s:" % expr(m.group(1))
            out.append(pad + line)
            level += 1
            singles.append(True)   # until told it's a block
            continue
        if kind == "open":
            if singles and singles[-1] is True: singles[-1] = False
            else: singles.append(False)
            continue
        if kind == "single":
            continue
        if kind == "close":
            level -= 1
            singles.pop()
            while singles and singles[-1] is True:
                level -= 1; singles.pop()
            continue
        line = stmt(text)
        out.append(pad + line)
        while singles and singles[-1] is True:
            level -= 1; singles.pop()
    return "\n".join(out)


SKIP = re.compile(r"^(v\.|return|steered|var wheels = new Transform|var steered = new|Transform\[\])")


def stmt(t):
    t = t.strip()
    if SKIP.match(t) or t.startswith("if (sz > 0f) steered"):
        return "pass"
    t = re.sub(r"^\w+\[\w+\] = (Part\()", r"\1", t)
    t = re.sub(r"^(\w+)\+\+$", r"\1 += 1", t)
    m = re.match(r"^%s (.+)$" % TYPES, t)
    if m:
        parts = split_top(m.group(1))
        return "; ".join("%s = %s" % (p.split("=", 1)[0].strip(), expr(p.split("=", 1)[1].strip())) if "=" in p else "%s = None" % p for p in parts)
    if t.startswith("go.") or ".AddComponent" in t:
        return "pass"
    m = re.match(r"^(\w+)\.([xyz]) = (.+)$", t)
    if m:
        return "%s[%d] = %s" % (m.group(1), "xyz".index(m.group(2)), expr(m.group(3)))
    m = re.match(r"^([\w.\[\]]+) = (.+)$", t)
    if m:
        return "%s = %s" % (m.group(1), expr(m.group(2)))
    return expr(t)


def run_vehicle(src, fn, root, paint):
    code = "def %s(v, palette, paint):\n" % fn + translate(body_of(src, "static void %s(Vehicle v" % fn))
    code = code.replace("v.self_transform", "v").replace(".self_transform", ".transform")
    env = dict(globals())
    env["Wheel"] = env["Wheel_"]
    exec(code, env)
    env[fn](root, None, paint)


def build_vehicles(at, kind, paint):
    src = open(os.path.join(SRC, "VehicleModels.cs")).read()
    consts = re.search(r"const int (Metal.+?);", src, re.S).group(1)
    for c in split_top(consts):
        k, v = c.split("=")
        globals()[k.strip()] = globals()[v.strip().split(".")[1].upper()]
    wcode = "def Wheel_(radius, width, rim):\n" + translate(body_of(src, "static Mesh Wheel(")) + "\n    return k"
    exec(wcode, globals())
    root = Xf()
    root.t.lp = np.asarray(at, float)
    root.t.lr = pb.ry(-35 if kind == "Scooter" else 35)
    run_vehicle(src, kind, root, paint)


def build_zip(a, b):
    src = open(os.path.join(ROOT, "Assets", "Scripts", "Royale", "ZipLine.cs")).read()
    z = type("Zip", (), {})()
    z.a, z.b, z.h = np.asarray(a, float), np.asarray(b, float), 4.4
    length = mag((z.b + UP * z.h) - (z.a + UP * z.h))
    sag = length * 0.018

    def PointAt(s):
        return (z.a + UP * z.h) * (1 - s) + (z.b + UP * z.h) * s - UP * (4 * sag * s * (1 - s))

    tbody = body_of(src, "void Tower(MeshKit k").replace("dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward", "dir.normalized")
    bbody = body_of(src, "void Build()")
    bbody = bbody[:bbody.index("for (int e = 0")] + bbody[bbody.index("var go = new GameObject"):]   # colliders only
    tower = "def Tower(k, at, other):\n" + translate(tbody)
    build = "def Build():\n" + translate(bbody).replace("k = MeshKit(3)", "k = Kit(3)") + "\n    return k"
    env = dict(globals())
    env.update(dict(a=z.a, b=z.b, towerHeight=z.h, PointAt=PointAt, self_transform=Xf(), MeshKit=Kit))
    env["self_transform"].t.lp = z.a.copy()
    exec(tower.replace("towerHeight", "towerHeight"), env)
    exec(build, env)
    k = env["Build"]()
    holder = Xf()
    holder.t.lp = z.a.copy()
    PARTS.append((k, holder))


MeshKit = Kit


def emit():
    zip_cols = {0: (0.42, 0.43, 0.45), 1: (0.12, 0.12, 0.13), 2: (0.9, 0.55, 0.1)}
    for n, (kit, xf) in enumerate(PARTS):
        for m, (_, slot, verts, faces) in enumerate(kit.parts):
            world = [pb.to_blender(xf.t.point(v)) for v in verts]
            mesh = bpy.data.meshes.new("p%d_%d" % (n, m))
            mesh.from_pydata(world, [], [tuple(reversed(f)) for f in faces])
            mesh.update()
            ob = bpy.data.objects.new(mesh.name, mesh)
            bpy.context.scene.collection.objects.link(ob)
            rgb = zip_cols[slot] if getattr(kit, "zip", False) else COLOURS[slot]
            ob.data.materials.append(pb.material("c%s_%d" % ("z" if getattr(kit, "zip", False) else "v", slot), rgb))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    build_vehicles((-1.2, 0, 0), "Scooter", RED)
    build_vehicles((1.4, 0, 0.2), "Cart", OLIVE)
    build_vehicles((-3.2, 0, 1.5), "Scooter", BLUE)
    n0 = len(PARTS)
    build_zip((7.5, 3.0, 6.0), (2.0, 0.0, 30.0))
    for kit, _ in PARTS[n0:]:
        kit.zip = True
    emit()
    # ground: a slope under the towers
    bm = bpy.data.meshes.new("ground")
    g = [(-20, -10, 0), (20, -10, 0), (20, 4, 0), (-20, 4, 0), (20, 40, 0), (-20, 40, 0)]
    bm.from_pydata(g, [], [(0, 1, 2, 3), (3, 2, 4, 5)])
    go = bpy.data.objects.new("ground", bm); sc.collection.objects.link(go)
    go.data.materials.append(pb.material("ground", (0.4, 0.38, 0.33)))
    hill = bpy.data.meshes.new("hill")
    hv = [(3, -3, 0), (12, -3, 0), (12, 3, 3.0), (3, 3, 3.0), (3, 9, 3.0), (12, 9, 3.0), (3, 14, 0), (12, 14, 0)]
    hill.from_pydata(hv, [], [(0, 1, 2, 3), (3, 2, 5, 4), (4, 5, 7, 6)])
    ho = bpy.data.objects.new("hill", hill); sc.collection.objects.link(ho)
    ho.data.materials.append(pb.material("hill", (0.33, 0.4, 0.24)))
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5; sun.angle = 0.15
    so = bpy.data.objects.new("sun", sun); so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35)); sc.collection.objects.link(so)
    world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.72, 1)
    cam = bpy.data.cameras.new("cam"); cam.lens = 40
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    sc.render.engine = "CYCLES"; sc.cycles.samples = 48
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    os.makedirs(OUT, exist_ok=True)
    # Close view of the vehicles.
    co.location = (2.2, -6.2, 2.3); co.rotation_euler = (math.radians(73), 0, math.radians(14))
    sc.render.filepath = os.path.join(OUT, "vehicles.png")
    bpy.ops.render.render(write_still=True)
    # Wide view with the zip line.
    co.location = (-8, -9, 6); co.rotation_euler = (math.radians(68), 0, math.radians(-38)); cam.lens = 24
    sc.render.filepath = os.path.join(OUT, "zip_line.png")
    bpy.ops.render.render(write_still=True)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
