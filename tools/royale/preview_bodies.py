"""Preview of the code-built bodies (Assets/Scripts/Royale/HumanBody.cs) rendered in Blender.

Unity cannot run in the cloud container, so this is a line-by-line port of the body's bones,
mesh parts, gear and the Stand/HoldGun poses (leg and arm IK) to numpy, rendered with Cycles.
It checks proportions and the IK maths; keep it in step with HumanBody.cs when either changes.

Run: python tools/royale/preview_bodies.py   (writes tools/royale/preview/bodies.png)
"""
import bpy  # noqa: F401
import math
import os
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "tools", "royale", "preview")

# ---------------------------------------------------------------- Unity maths (left-handed, Y up)

def rx(a):
    a = math.radians(a); c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])

def ry(a):
    a = math.radians(a); c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])

def rz(a):
    a = math.radians(a); c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])

def euler(x, y, z):
    # Unity applies Z, then X, then Y.
    return ry(y) @ rx(x) @ rz(z)

def norm(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v

def look(fwd, up):
    z = norm(np.asarray(fwd, float))
    x = norm(np.cross(up, z))
    y = np.cross(z, x)
    return np.column_stack([x, y, z])

V = lambda *a: np.array(a, float)
UP, RIGHT, FWD = V(0, 1, 0), V(1, 0, 0), V(0, 0, 1)


class T:
    def __init__(self, parent=None, pos=(0, 0, 0)):
        self.parent, self.lp, self.lr = parent, V(*pos), np.eye(3)

    @property
    def rot(self):
        return self.lr if self.parent is None else self.parent.rot @ self.lr

    @rot.setter
    def rot(self, r):
        self.lr = r if self.parent is None else self.parent.rot.T @ r

    @property
    def pos(self):
        return self.lp if self.parent is None else self.parent.pos + self.parent.rot @ self.lp

    def point(self, p):
        return self.pos + self.rot @ np.asarray(p, float)

    up = property(lambda s: s.rot @ UP)
    right = property(lambda s: s.rot @ RIGHT)
    forward = property(lambda s: s.rot @ FWD)


# ---------------------------------------------------------------- mesh kit (port of MeshKit.cs)

class Kit:
    def __init__(self):
        self.parts = []   # (bone, slot, verts(local), faces)
        self.bone = None
        self.slot = 0

    def add(self, verts, faces):
        self.parts.append((self.bone, self.slot, np.array(verts, float), faces))

    def box(self, c, size, tx=1.0, tz=1.0, eul=(0, 0, 0)):
        r = euler(*eul); h = np.asarray(size, float) / 2
        vs = []
        for i in range(8):
            sx = 1 if i & 1 else -1; sy = 1 if i & 2 else -1; sz = 1 if i & 4 else -1
            fx = tx if sy > 0 else 1; fz = tz if sy > 0 else 1
            vs.append(np.asarray(c, float) + r @ V(sx * h[0] * fx, sy * h[1], sz * h[2] * fz))
        f = [(0, 2, 3, 1), (5, 7, 6, 4), (4, 6, 2, 0), (1, 3, 7, 5), (2, 6, 7, 3), (4, 0, 1, 5)]
        self.add(vs, f)

    def tube(self, c, y0, y1, r0, r1, sides=8, caps=True, eul=(0, 0, 0)):
        r = euler(*eul); vs = []
        for y, rr in ((y0, r0), (y1, r1)):
            for s in range(sides):
                a = s / sides * math.tau
                vs.append(np.asarray(c, float) + r @ V(math.cos(a) * rr[0], y, math.sin(a) * rr[1]))
        f = [(s, (s + 1) % sides, sides + (s + 1) % sides, sides + s) for s in range(sides)]
        if caps:
            f.append(tuple(range(sides)))
            f.append(tuple(range(sides, 2 * sides)))
        self.add(vs, f)

    def ellipsoid(self, c, radii, sides=10, rings=6, lat0=-90, lat1=90, eul=(0, 0, 0)):
        r = euler(*eul); vs = []
        for ring in range(rings + 1):
            lat = math.radians(lat0 + (lat1 - lat0) * ring / rings)
            for s in range(sides):
                lon = s / sides * math.tau
                u = V(math.cos(lat) * math.cos(lon), math.sin(lat), math.cos(lat) * math.sin(lon))
                vs.append(np.asarray(c, float) + r @ (u * np.asarray(radii, float)))
        f = []
        for ring in range(rings):
            for s in range(sides):
                a = ring * sides + s; b = ring * sides + (s + 1) % sides
                f.append((a, b, b + sides, a + sides))
        self.add(vs, f)


# ---------------------------------------------------------------- the body (port of HumanBody.cs)

HIP, THIGH, SHIN, ANKLE, UARM, FARM = 0.95, 0.45, 0.44, 0.075, 0.29, 0.27
SKIN, SHIRT, PANTS, GEAR = 0, 1, 2, 3
KHAKI, OLIVE, BLACK, BLUE, METAL = 10, 11, 12, 13, 14


class Body:
    def __init__(self, root_pos, yaw, long_hair=False, vest=0, helmet=0, pack=0):
        self.root = T(None, root_pos); self.root.lr = ry(yaw)
        b = {}
        def bone(n, parent, p): b[n] = T(b[parent] if parent else self.root, p)
        bone("hips", None, (0, HIP, 0)); bone("spine", "hips", (0, 0.1, 0)); bone("chest", "spine", (0, 0.2, 0))
        bone("head", "chest", (0, 0.3, 0.01))
        for s, x in (("L", -0.2), ("R", 0.2)):
            bone("up" + s, "chest", (x, 0.24, 0)); bone("lo" + s, "up" + s, (0, -UARM, 0)); bone("hand" + s, "lo" + s, (0, -FARM, 0))
        for s, x in (("L", -0.1), ("R", 0.1)):
            bone("th" + s, "hips", (x, -0.03, 0)); bone("sh" + s, "th" + s, (0, -THIGH, 0)); bone("ft" + s, "sh" + s, (0, -SHIN, 0))
        self.b = b
        k = self.kit = Kit()
        def at(n, slot): k.bone = b[n]; k.slot = slot
        at("hips", PANTS); k.box((0, 0.02, 0), (0.36, 0.2, 0.23), 1.03, 1)
        at("hips", GEAR); k.box((0, 0.115, 0), (0.355, 0.045, 0.225)); k.box((0, 0.115, 0.115), (0.06, 0.04, 0.01))
        at("spine", SHIRT); k.box((0, 0.08, 0), (0.33, 0.18, 0.2), 1.06, 1.05)
        at("chest", SHIRT); k.box((0, 0.1, 0), (0.36, 0.24, 0.22), 1.12, 0.95)
        k.ellipsoid((-0.19, 0.23, 0), (0.075, 0.06, 0.075), 8, 4); k.ellipsoid((0.19, 0.23, 0), (0.075, 0.06, 0.075), 8, 4)
        k.tube((0, 0.22, 0), 0, 0.035, (0.085, 0.075), (0.075, 0.07), 8, False)
        at("chest", SKIN); k.tube((0, 0.24, 0.005), 0, 0.08, (0.055, 0.055), (0.05, 0.05), 8, False)
        at("head", SKIN); k.ellipsoid((0, 0.11, 0.005), (0.095, 0.12, 0.105), 12, 8)
        k.box((0, 0.095, 0.105), (0.025, 0.045, 0.035), 0.6, 0.7)
        k.box((-0.097, 0.11, -0.005), (0.02, 0.05, 0.035)); k.box((0.097, 0.11, -0.005), (0.02, 0.05, 0.035))
        at("head", GEAR)
        for x in (-0.037, 0.037):
            k.box((x, 0.125, 0.098), (0.024, 0.012, 0.01)); k.box((x, 0.148, 0.098), (0.034, 0.008, 0.012))
        k.ellipsoid((0, 0.13, -0.008), (0.102, 0.115, 0.112), 12, 4, 5, 90)
        if long_hair: k.box((0, 0.07, -0.075), (0.17, 0.13, 0.06))
        else: k.box((0, 0.105, -0.085), (0.16, 0.07, 0.04))
        for side, s in ((-1, "L"), (1, "R")):
            at("up" + s, SHIRT); k.tube((0, 0, 0), 0.02, -0.16, (0.06, 0.06), (0.056, 0.056), 8)
            at("up" + s, SKIN); k.tube((0, 0, 0), -0.15, -UARM, (0.045, 0.045), (0.04, 0.04), 8, False)
            at("lo" + s, SKIN); k.ellipsoid((0, 0, 0), (0.042, 0.04, 0.042), 8, 4)
            k.tube((0, 0, 0), 0, -FARM, (0.04, 0.04), (0.03, 0.028), 8, False)
            at("hand" + s, SKIN); k.box((0, -0.045, 0.005), (0.05, 0.09, 0.03))
            k.box((side * -0.02, -0.03, 0.02), (0.018, 0.05, 0.02), 1, 1, (-20, 0, side * 15))
            at("th" + s, PANTS); k.tube((0, 0, 0), 0.02, -THIGH, (0.098, 0.102), (0.068, 0.07), 8)
            at("sh" + s, PANTS); k.ellipsoid((0, 0, 0), (0.068, 0.06, 0.07), 8, 4)
            k.tube((0, 0, 0), 0, -SHIN + 0.04, (0.064, 0.068), (0.05, 0.052), 8)
            at("ft" + s, GEAR); k.box((0, -0.03, 0.045), (0.1, 0.085, 0.26), 0.95, 0.85); k.box((0, -0.071, 0.047), (0.106, 0.018, 0.27))
        self.gear(vest, helmet, pack)

    def gear(self, vest, helmet, pack):
        k, b = self.kit, self.b
        if vest:
            main = BLACK if vest >= 3 else OLIVE if vest == 2 else KHAKI
            k.bone, k.slot = b["chest"], main
            th = 0.03 + vest * 0.012
            k.box((0, 0.08, 0.115 + th / 2), (0.34, 0.3, th), 1.05, 1); k.box((0, 0.08, -0.115 - th / 2), (0.34, 0.3, th), 1.05, 1)
            k.box((-0.17, 0.1, 0), (0.03, 0.22, 0.25)); k.box((0.17, 0.1, 0), (0.03, 0.22, 0.25))
            k.box((-0.12, 0.25, 0), (0.06, 0.02, 0.24)); k.box((0.12, 0.25, 0), (0.06, 0.02, 0.24))
            k.slot = BLACK
            if vest >= 2:
                for p in (-1, 0, 1): k.box((p * 0.1, -0.015, 0.14 + th), (0.08, 0.1, 0.05))
            if vest >= 3:
                k.slot = main
                k.tube((0, 0.22, 0), 0, 0.06, (0.11, 0.1), (0.1, 0.095), 10, False)
                k.box((-0.2, 0.2, 0), (0.07, 0.05, 0.2), 1, 1, (0, 0, 30)); k.box((0.2, 0.2, 0), (0.07, 0.05, 0.2), 1, 1, (0, 0, -30))
        if helmet:
            main = BLACK if helmet >= 3 else OLIVE if helmet == 2 else KHAKI
            k.bone, k.slot = b["head"], main
            g = 0.012 + helmet * 0.006
            k.ellipsoid((0, 0.13, -0.005), (0.105 + g, 0.12 + g, 0.115 + g), 12, 5, -8 if helmet >= 2 else 10, 90)
            if helmet >= 2:
                k.tube((0, 0.115, -0.005), 0, 0.012, (0.13 + g, 0.14 + g), (0.125 + g, 0.135 + g), 12, True)
                k.slot = BLACK; k.box((0, 0.22, 0.1), (0.045, 0.03, 0.03))
            if helmet >= 3:
                k.slot = BLACK; k.box((0, 0.11, 0.125), (0.19, 0.06, 0.012), 1, 1, (-12, 0, 0))
        if pack:
            col = BLACK if pack >= 3 else OLIVE if pack == 2 else BLUE
            k.bone, k.slot = b["chest"], col
            h = 0.3 + pack * 0.07; d = 0.12 + pack * 0.03
            k.box((0, 0.12 - h * 0.25, -0.13 - d / 2), (0.3, h, d), 0.92, 0.85)
            k.box((0, 0.04 - h * 0.25, -0.13 - d - 0.03), (0.22, h * 0.45, 0.06))
            k.slot = BLACK
            k.box((-0.1, 0.12, -0.12), (0.04, 0.3, 0.02)); k.box((0.1, 0.12, -0.12), (0.04, 0.3, 0.02))
            if pack >= 2:
                k.slot = col
                k.tube((0, 0.15 + h * 0.25, -0.13 - d / 2), -0.17, 0.17, (0.05, 0.05), (0.05, 0.05), 8, True, (0, 0, 90))

    # --- poses (Stand + HoldGun, as in HumanBody.cs)
    def stand(self, speed=0.0, phase=0.0, crouch=0.0, aim=0.0, aim_pitch=0.0, gun=None):
        b, root = self.b, self.root
        run = min(1, max(0, (speed - 3.5) / 2.5))
        stride = min(max(speed * 0.13, 0), 0.42) * (1 + (0.6 - 1) * crouch)
        moving = min(1, speed / 1.5)
        bob = abs(math.cos(phase)) * 0.035 * moving
        hip_y = HIP - crouch * 0.42 - bob * (1 + run) + 0.01
        b["hips"].lp = V(0, hip_y, -crouch * 0.08)
        b["hips"].lr = euler(0, math.sin(phase) * 6 * moving, 0)
        lean = run * 10 + crouch * 18
        aim_lean = (1 if gun else 0) * -aim_pitch * 0.35
        b["spine"].lr = euler(lean * 0.5 + aim_lean * 0.5, -math.sin(phase) * 5 * moving, 0)
        b["chest"].lr = euler(lean * 0.5 + aim_lean * 0.5 - crouch * 8, -math.sin(phase) * 5 * moving, 0)
        b["head"].lr = euler(-aim_pitch - (lean + aim_lean - crouch * 8), 0, 0)
        for s in range(2):
            side = -1 if s == 0 else 1
            ph = phase + (0 if s == 0 else math.pi)
            z = math.sin(ph) * stride
            lift = max(0, math.cos(ph)) * (0.07 + (0.17 - 0.07) * run) * moving
            local = V(side * (0.11 + crouch * 0.03), ANKLE + lift, z + crouch * (0.2 if s == 0 else -0.22))
            target = root.point(local)
            pole = root.forward + root.right * side * (0.12 + crouch * 0.08)
            n = "L" if s == 0 else "R"
            solve(b["th" + n], b["sh" + n], THIGH, SHIN, target, pole)
            toe = max(0, math.cos(ph)) * 25 * moving
            b["ft" + n].rot = look(root.forward, UP) @ euler(-toe * 0.5, 0, 0)
        if gun:
            self.hold(gun, aim, aim_pitch)
        else:
            swing = math.sin(phase) * (8 + 30 * run) * moving
            b["upL"].lr = euler(-swing - crouch * 20, 0, -6); b["upR"].lr = euler(swing - crouch * 20, 0, 6)
            b["loL"].lr = euler(-15 - run * 55 - max(0, -swing) * 0.5, 0, 0); b["loR"].lr = euler(-15 - run * 55 - max(0, swing) * 0.5, 0, 0)

    def hold(self, gun, aim, aim_pitch):
        b = self.b
        pistol = gun == "pistol"
        grips = {"pistol": ((0, -0.05, -0.03), (-0.02, -0.06, -0.01)), "rifle": ((0, -0.06, -0.09), (0, -0.03, 0.26))}
        gr, gl = grips[gun]
        low = V(0.06, 0.06, 0.3) if pistol else V(0.1, 0.06, 0.24)
        high = V(0.03, 0.26, 0.46) if pistol else V(0.13, 0.2, 0.14)
        holder = T(b["chest"], tuple(low + (high - low) * aim))
        holder.lr = euler(22 * (1 - aim) - aim_pitch * 0.65 * aim, (-5 if pistol else -22) * (1 - aim), 0)
        hr, hl = holder.point(gr), holder.point(gl)
        chest = b["chest"]
        down = -chest.up
        solve(b["upR"], b["loR"], UARM, FARM, hr + holder.up * 0.03, down + chest.right * 0.8 - chest.forward * 0.3)
        solve(b["upL"], b["loL"], UARM, FARM, hl + holder.up * 0.03, down - chest.right * 0.9)
        b["handR"].rot = holder.rot @ euler(70, 0, 90)
        b["handL"].rot = holder.rot @ euler(80, 0, -90)
        k = self.kit
        k.bone, k.slot = holder, METAL
        if pistol:
            k.box((0, 0.015, 0.04), (0.032, 0.035, 0.2)); k.box((0, -0.04, -0.035), (0.03, 0.1, 0.045))
        else:
            k.box((0, 0.03, 0.0), (0.06, 0.08, 0.36)); k.box((0, 0.03, 0.28), (0.06, 0.06, 0.22)); k.box((0, 0.035, 0.48), (0.02, 0.02, 0.2))
            k.box((0, -0.06, 0.06), (0.03, 0.17, 0.05), 1, 1, (10, 0, 0)); k.box((0, -0.05, -0.09), (0.03, 0.1, 0.04)); k.box((0, 0.02, -0.32), (0.05, 0.08, 0.26))


def hang(down, front):
    y = -norm(down)
    z = front - y * np.dot(front, y)
    if np.linalg.norm(z) < 1e-6: z = np.cross(y, RIGHT)
    return look(norm(z), y)


def solve(upper, lower, l1, l2, target, pole):
    s = upper.pos
    d = target - s
    ln = min(max(np.linalg.norm(d), abs(l1 - l2) + 0.01), l1 + l2 - 0.002)
    dr = norm(d)
    bend = pole - dr * np.dot(pole, dr)
    bend = norm(bend)
    ca = max(-1, min(1, (l1 * l1 + ln * ln - l2 * l2) / (2 * l1 * ln)))
    sa = math.sqrt(max(0, 1 - ca * ca))
    mid = s + dr * (l1 * ca) + bend * (l1 * sa)
    end = s + dr * ln
    upper.rot = hang(mid - s, bend)
    lower.rot = hang(end - mid, bend)


# ---------------------------------------------------------------- Blender scene

COLOURS = {SKIN: (0.55, 0.36, 0.25), SHIRT: (0.2, 0.32, 0.55), PANTS: (0.15, 0.15, 0.18), GEAR: (0.06, 0.05, 0.05),
           KHAKI: (0.55, 0.48, 0.32), OLIVE: (0.25, 0.3, 0.16), BLACK: (0.05, 0.05, 0.05), BLUE: (0.12, 0.25, 0.6), METAL: (0.25, 0.25, 0.27)}
SHIRTS = [(0.2, 0.32, 0.55), (0.6, 0.18, 0.15), (0.75, 0.72, 0.65), (0.3, 0.45, 0.3), (0.55, 0.45, 0.2)]
SKINS = [(0.55, 0.36, 0.25), (0.42, 0.27, 0.18), (0.68, 0.48, 0.35), (0.35, 0.22, 0.15), (0.6, 0.42, 0.3)]


def material(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = 0.7
    return m


def to_blender(p):
    # Unity (x, y up, z forward, left-handed) -> Blender (x, y forward, z up, right-handed): swap y and z.
    return (p[0], p[2], p[1])


def emit(body, idx):
    for n, (bone, slot, verts, faces) in enumerate(body.kit.parts):
        world = [to_blender(bone.point(v)) for v in verts]
        mesh = bpy.data.meshes.new(f"b{idx}_{n}")
        mesh.from_pydata(world, [], [tuple(reversed(f)) for f in faces])
        mesh.update()
        ob = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.scene.collection.objects.link(ob)
        rgb = COLOURS[slot]
        if slot == SHIRT: rgb = SHIRTS[idx % len(SHIRTS)]
        if slot == SKIN: rgb = SKINS[idx % len(SKINS)]
        ob.data.materials.append(material(f"m{idx}_{slot}", rgb))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    poses = [
        ("idle, unarmed", dict(), dict()),
        ("walking, rifle low", dict(speed=4.2, phase=0.9, gun="rifle"), dict(vest=1, pack=1)),
        ("running, rifle low", dict(speed=6.2, phase=2.2, gun="rifle"), dict(helmet=2, vest=2, pack=2)),
        ("aiming rifle", dict(aim=1, aim_pitch=4, gun="rifle"), dict(vest=3, helmet=3, pack=3)),
        ("crouched, aiming", dict(crouch=1, aim=1, aim_pitch=-6, gun="rifle"), dict(vest=2, helmet=1)),
        ("pistol aimed", dict(aim=1, gun="pistol"), dict(helmet=1, long_hair=True)),
        ("crouch-walking", dict(speed=2.2, phase=0.4, crouch=1, gun="rifle"), dict(vest=3, pack=3)),
    ]
    for i, (label, pose, gear) in enumerate(poses):
        x = (i - (len(poses) - 1) / 2) * 1.15
        body = Body((x, 0, 0), 180 + (25 if i % 2 else -25), long_hair=gear.pop("long_hair", False), **gear)
        body.stand(**pose)
        emit(body, i)
    # ground, lights, camera
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, 0))
    sc.objects[-1].data.materials.append(material("ground", (0.42, 0.4, 0.36)))
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5; sun.angle = 0.2
    so = bpy.data.objects.new("sun", sun); so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-30)); sc.collection.objects.link(so)
    world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.72, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    cam = bpy.data.cameras.new("cam"); cam.lens = 50
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (0, -11.5, 1.25); co.rotation_euler = (math.radians(88), 0, 0)
    sc.render.engine = "CYCLES"; sc.cycles.samples = 48; sc.cycles.device = "CPU"
    sc.render.resolution_x, sc.render.resolution_y = 1800, 700
    os.makedirs(OUT, exist_ok=True)
    sc.render.filepath = os.path.join(OUT, "bodies.png")
    bpy.ops.render.render(write_still=True)
    # a close side view of the aiming and crouched poses
    co.location = (7.5, -4.5, 1.1); co.rotation_euler = (math.radians(86), 0, math.radians(62))
    cam.lens = 60
    sc.render.filepath = os.path.join(OUT, "bodies_side.png")
    bpy.ops.render.render(write_still=True)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
