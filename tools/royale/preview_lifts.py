"""Lifts and the rooftop helipad, checked and previewed in Blender.

1. A port of RoyaleBuilder.PlaceLifts / PlaceHelipad (LiftWall, LiftHead, LiftDeck, FlatRoof) run on
   the building and interior FBX files with ray casts (the building's base at y = 0), so you can see
   where the lifts and the pad will go without Unity. Keep it in step with RoyaleBuilder.cs.
   The spots are written to tools/royale/preview/lift_spots.json.
2. Quick renders: the helicopter on the academic block's roof pad, and a lift cutaway (the building
   see-through with the glowing frame of floors and columns, as Lift.cs draws it).

Run: python tools/royale/preview_lifts.py [--check] [--heli] [--cutaway "Girls Hostel A"] [--full]
     (no flags: check if needed, then both renders at half size; --full renders at 1600 x 900)
"""
import bpy  # noqa: F401
import json
import math
import os
import sys

import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
R = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))) + "/"
OUT = os.path.join(R, "tools", "royale", "preview")
inter = json.load(open(R + "Assets/Data/interiors.json"))
models = {m["name"]: m for m in json.load(open(R + "Assets/Data/building_models.json"))["models"]}
layout = {b["name"]: b for b in json.load(open(R + "Assets/Data/campus_layout.json"))["buildings"]}
shells = {s["name"]: s for s in inter["shells"]}

def load(fbx, px, pz):
    """An FBX's faces in Unity world coordinates (base at y = 0)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=R + fbx)
    verts, polys = [], []
    for ob in bpy.context.scene.objects:
        if ob.type != 'MESH': continue
        mw = ob.matrix_world
        off = len(verts)
        for v in ob.data.vertices:
            w = mw @ v.co
            verts.append((px + w.x, w.z, pz - w.y))   # unity x, y, z
        for p in ob.data.polygons:
            polys.append([off + i for i in p.vertices])
    return verts, polys

def bvh_for(items):
    V, P = [], []
    for v, p in items:
        o = len(V); V += v; P += [[o + i for i in q] for q in p]
    return BVHTree.FromPolygons(V, P, all_triangles=False)

def ray(t, o, d, dist):
    """nearest hit: (point, normal, distance) or None. Normal flipped to face the ray."""
    o = Vector(o); d = Vector(d).normalized()
    loc, n, i, dd = t.ray_cast(o, d, dist)
    if loc is None: return None
    if n.dot(d) > 0: n = -n
    return loc, n, dd

def ray_all_top(t, x, z, top):
    """Topmost hit straight down from 'top'."""
    return ray(t, (x, top, z), (0, -1, 0), 200)

def box_clear(t, c, half):
    """Approximate CheckBox: rays along the box edges and a grid through it."""
    cx, cy, cz = c; hx, hy, hz = half
    for ax in range(3):
        for i in range(-2, 3):
            for j in range(-2, 3):
                o = [cx, cy, cz]; d = [0, 0, 0]
                a2 = [k for k in range(3) if k != ax]
                o[a2[0]] += [hx, hy, hz][a2[0]] * i / 2
                o[a2[1]] += [hx, hy, hz][a2[1]] * j / 2
                o[ax] -= [hx, hy, hz][ax]; d[ax] = 1
                h = ray(t, o, d, 2 * [hx, hy, hz][ax])
                if h: return False
    return True

def roof_at(t, x, z, lo, hi):
    h = ray_all_top(t, x, z, hi + 30)
    if not h or h[1].y < 0.95: return None
    y = h[0].y
    if y < lo or y > hi: return None
    return y

def flat_roof(t, x, z, hh, lo, hi):
    y = roof_at(t, x, z, lo, hi)
    if y is None: return None
    for c in range(4):
        yc = roof_at(t, x + (-hh if c & 1 == 0 else hh), z + (-hh if c & 2 == 0 else hh), lo, hi)
        if yc is None or abs(yc - y) > 0.12: return None
    return y

def in_wing(wings, x, z, m):
    best = -1
    for i, (wx, wz, w, d, rnd, top) in enumerate(wings):
        hx, hz = w / 2 - m, d / 2 - m
        if hx <= 0 or hz <= 0: continue
        dx, dz = x - wx, z - wz
        ins = (dx*dx)/(hx*hx) + (dz*dz)/(hz*hz) < 1 if rnd else abs(dx) < hx and abs(dz) < hz
        if ins and (best < 0 or w * d < wings[best][2] * wings[best][3]): best = i
    return best

W = {"lobby": 3, "foyer": 3, "waiting": 2.5, "stair": 2, "corridor": 1.5, "gallery": 1.5, "passage": 1, "common": 1}

def lift_wall(t, p, n, floor0, props):
    nn = Vector((n[0], 0, n[1])); side = Vector((0, 1, 0)).cross(nn)
    inside = Vector((p[0] + n[0] * 0.8, floor0, p[1] + n[1] * 0.8))
    h = ray(t, inside + Vector((0, 1.5, 0)), (0, -1, 0), 2.5)
    if not h: return None
    fy = h[0].y
    if abs(fy - floor0) > 0.5 or h[1].y < 0.9: return None
    d0 = -1
    for lat in (-0.8, 0, 0.8):
        for hh in (0.3, 1.2, 2.2):
            o = inside + side * lat; o.y = fy + hh
            r = ray(t, o, -nn, 1.6)
            if not r: return None
            if r[1].dot(nn) < 0.9: return None
            if d0 < 0: d0 = r[2]
            elif abs(r[2] - d0) > 0.06: return None
    face = inside - nn * d0; face.y = fy
    front = face + nn * 0.95
    # CheckBox in front (approx, axis aligned since walls are)
    if not box_clear(t, (front.x, fy + 1.1, front.z), (0.8 if abs(nn.z) > 0.5 else 0.75, 0.85, 0.75 if abs(nn.z) > 0.5 else 0.8)): return None
    for pr in props:
        if (pr["x"] - front.x) ** 2 + (pr["z"] - front.z) ** 2 < 1.44: return None
        if (pr["x"] - face.x) ** 2 + (pr["z"] - face.z) ** 2 < 1.0: return None
    return face, math.degrees(math.atan2(nn.x, nn.z))

def lift_head(t, shaft, bmin, bmax, lo, hi):
    inside = lambda x, z, m: bmin[0] + m < x < bmax[0] - m and bmin[1] + m < z < bmax[1] - m
    for r in (0, 1.5, 3, 4.5, 6, 7.5, 9, 10.5, 12):
        for a in range(1 if r == 0 else 16):
            ang = math.radians(a * 22.5)
            x, z = shaft.x + math.sin(ang) * r, shaft.z + math.cos(ang) * r
            if not inside(x, z, 1.6): continue
            y = flat_roof(t, x, z, 1.4, lo, hi)
            if y is None: continue
            if not box_clear(t, (x, y + 0.15 + 1.45, z), (1.4, 1.45, 1.3)): continue
            for face in (0, 90, 180, 270):
                f = Vector((math.sin(math.radians(face)), 0, math.cos(math.radians(face))))
                c = Vector((x, y, z)) + f * (2.4 / 2 + 0.9)
                if not inside(c.x, c.z, 0.5): continue
                yc = flat_roof(t, c.x, c.z, 0.5, lo, hi)
                if yc is None or abs(yc - y) > 0.15: continue
                if not box_clear(t, (c.x, y + 1.1, c.z), (0.6, 0.85, 0.6)): continue
                return Vector((x, y, z)), face
    return None

def roof_top_between(t, x, z, lo, hi):
    h = ray_all_top(t, x, z, hi + 30)
    return h is not None and lo <= h[0].y <= hi

def lift_deck(t, shaft, bmin, bmax, roofY):
    for r in (0, 1.5, 3, 4.5, 6, 7.5, 9):
        for a in range(1 if r == 0 else 16):
            ang = math.radians(a * 22.5)
            x, z = shaft.x + math.sin(ang) * r, shaft.z + math.cos(ang) * r
            for face in (0, 90, 180, 270):
                cs, sn = math.cos(math.radians(face)), math.sin(math.radians(face))
                ok = True
                for i in range(-2, 3):
                    for j in range(-2, 3):
                        lx, lz = i * 0.75, j * 1.15
                        qx, qz = x + lx * cs + lz * sn, z - lx * sn + lz * cs
                        if not (bmin[0] + 0.8 < qx < bmax[0] - 0.8 and bmin[1] + 0.8 < qz < bmax[1] - 0.8) or not roof_top_between(t, qx, qz, roofY - 0.3, roofY + 1.15):
                            ok = False; break
                    if not ok: break
                if not ok: continue
                hx, hz = (1.6, 2.4) if face in (0, 180) else (2.4, 1.6)
                if not box_clear(t, (x, roofY + 1.3 + 0.1 + 1.6, z), (hx, 1.6, hz)): continue
                f = Vector((sn, 0, cs))
                return Vector((x, roofY + 1.3, z)) - f * 1.1, face
    return None

def find_spots(only=()):
    """Runs the lift and helipad search per building; returns {zone name: [spots]} plus "helipad"."""
    out = {}
    for zi, zone in enumerate(inter["zones"]):
        if zone["maxLevel"] < 2: continue
        if only and zone["name"] not in only: continue
        mname = zone["shell"][:-len("_Interior")]
        m = models[mname]
        wings = []
        for nme in m["replaces"]:
            b = layout[nme]; wings.append((b["x"], b["z"], b["width"], b["depth"], b["shape"] == "cylinder", b["height"]))
        sh = shells[zone["shell"]]
        t = bvh_for([load(m["fbx"], m["pivotX"], m["pivotZ"]), load(sh["fbx"], sh["pivotX"], sh["pivotZ"])])
        floor0 = zone["floorOffset"]
        cands = []
        props = []
        existing = []
        for p in inter["props"]:
            if p["zone"] != zi or p["level"] != 0: continue
            if p["prop"] == "Lift_Door":
                f = Vector((p["x"], p["y"], p["z"])); existing.append(f)
                cands.append((10, f, p["yaw"], "existing lift door"))
            if p["y"] > floor0 + 2.3 or "Light" in p["prop"] or "Fan" in p["prop"] or p["prop"] == "Exit_Sign": continue
            props.append(p)
        for room in inter["rooms"]:
            if room["zone"] != zi or room["level"] != 0: continue
            w = W.get(room["kind"], 0)
            if w <= 0: continue
            x0, x1, z0, z1 = room["rect"]
            edges = [((x0, z0), (x1, z0), (0, 1)), ((x1, z1), (x0, z1), (0, -1)), ((x0, z1), (x0, z0), (1, 0)), ((x1, z0), (x1, z1), (-1, 0))]
            best = None
            for a, b, n in edges:
                L = math.dist(a, b)
                if L < 2.2: continue
                d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
                tt, tried = 1.0, 0
                while tt <= L - 1.0 and tried < 60:
                    p = (a[0] + d[0] * tt, a[1] + d[1] * tt)
                    r = lift_wall(t, p, n, floor0, props)
                    if r:
                        face, yaw = r
                        shaft = face - Vector((n[0], 0, n[1])) * 1.05
                        if in_wing(wings, shaft.x, shaft.z, 0.3) >= 0:
                            sc = w + 0.3 * (1 - abs(tt - L / 2) / (L / 2))
                            if best is None or sc > best[0]: best = (sc, face, yaw, room["name"])
                    tt += 0.5; tried += 1
            if best: cands.append(best)
        cands.sort(key=lambda c: -c[0])
        area = (zone["max"][0] - zone["min"][0]) * (zone["max"][1] - zone["min"][1])
        want = len(existing) if existing else (2 if area > 6000 else 1)
        chosen = []
        for sc, face, yaw, rn in cands:
            if len(chosen) >= want: break
            if any((o - face).length < 28 for o, *_ in chosen): continue
            f = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
            shaft = face - f * 1.05
            highest = max(w[5] for w in wings)
            hd = lift_head(t, shaft, m["boundsMin"], m["boundsMax"], 2 * 3.4, highest + 1.6)
            deck = 0
            if not hd:
                hd = lift_deck(t, shaft, m["boundsMin"], m["boundsMax"], highest); deck = 1.3 if hd else 0
            if not hd:
                hd = lift_head(t, shaft, m["boundsMin"], m["boundsMax"], 2 * 3.4, highest + 4.5)
            if not hd: continue
            chosen.append((face, yaw, rn, hd[0], hd[1], deck))
        print(zone["name"], "-", len(cands), "wall spots; lifts:", [(rn, tuple(round(v, 1) for v in face), round(yaw), "roof", tuple(round(v, 1) for v in hp), hy, "deck" if dk else "")
                                                                 for face, yaw, rn, hp, hy, dk in chosen], flush=True)
        out[zone["name"]] = [dict(face=list(face), yaw=yaw, room=rn, head=list(hp), headYaw=hy, deck=dk, wings=wings, model=mname)
                             for face, yaw, rn, hp, hy, dk in chosen]
        if zone["name"] == "Academic Block":
            b = layout["Academic Block - South Wing"]
            found = None
            for dx in (0, 1.5, -1.5, 3, -3, 4.5, -4.5, 6, -6):
                for dz in (0, 2, -2):
                    x, z = b["x"] + 21.5 + dx, b["z"] + dz
                    y = flat_roof(t, x, z, 7.8, b["height"] - 1.2, b["height"] + 1.6)
                    if y is None: continue
                    if not box_clear(t, (x, y + 3.3, z), (7.8, 3, 7.8)): continue
                    found = (x, y, z); break
                if found: break
            print("helipad", found, flush=True)
            out["helipad"] = list(found) if found else None

    return out


# ---------------------------------------------------------------- rendering

import preview_bodies as pb  # noqa: E402
import preview_vehicles as pv  # noqa: E402

PALETTE = pv.COLOURS
CREAM, GLASS = (0.86, 0.79, 0.68), (0.1, 0.14, 0.2)


def U(p):
    """Unity (x, y up, z) to Blender (x, y, z up), as the other previews do."""
    return (p[0], p[2], p[1])


def solid(name, rgb, rough=0.7):
    return pb.material(name, rgb)


def glow(name, rgb, strength, alpha=1.0):
    """An additive-looking glow: emission mixed with transparency (Lift.cs uses additive unlit materials)."""
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission"); em.inputs[0].default_value = (*rgb, 1); em.inputs[1].default_value = strength
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    add = nt.nodes.new("ShaderNodeAddShader")
    mix = nt.nodes.new("ShaderNodeMixShader"); mix.inputs[0].default_value = alpha
    nt.links.new(tr.outputs[0], mix.inputs[1]); nt.links.new(em.outputs[0], add.inputs[0]); nt.links.new(tr.outputs[0], add.inputs[1])
    nt.links.new(add.outputs[0], mix.inputs[2]); nt.links.new(mix.outputs[0], out.inputs[0])
    m.blend_method = "BLEND" if hasattr(m, "blend_method") else None
    return m


def place_building(fbx, px, pz, ghost=False):
    """Imports a building FBX into the scene at its pivot, in the Unity-to-Blender frame of the previews."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=R + fbx)
    new = [o for o in bpy.data.objects if o not in before]
    # Blender (x, y, z) of the FBX -> Unity (px + x, z, pz - y) -> preview frame (px + x, pz - y, z).
    M = Matrix.Translation((px, pz, 0.0)) @ Matrix.Scale(-1.0, 4, (0, 1, 0))
    for o in new:
        if o.parent is None: o.matrix_world = M @ o.matrix_world
    g = glow("ghost", (0.12, 0.42, 0.6), 0.22, 1.0)
    for o in new:
        if o.type != "MESH": continue
        mats = o.data.materials
        for i in range(len(mats)):
            name = mats[i].name if mats[i] else ""
            mats[i] = g if ghost else solid("glass" if "Glass" in name else "cream", GLASS if "Glass" in name else CREAM)
        if ghost:
            o.visible_shadow = False
    return new


def kit_object(name, kit, pos, yaw, colour):
    """A preview_bodies Kit placed at a Unity position and yaw; colour(slot) gives each part's material."""
    rot = pb.ry(yaw)
    for i, (_, slot, verts, faces) in enumerate(kit.parts):
        world = [U(np.asarray(pos, float) + rot @ v) for v in verts]
        mesh = bpy.data.meshes.new("%s_%d" % (name, i))
        mesh.from_pydata(world, [], [tuple(reversed(f)) for f in faces])
        mesh.update()
        ob = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.scene.collection.objects.link(ob)
        ob.data.materials.append(colour(slot))


def loot(slot):
    return solid("loot_%d" % slot, PALETTE[slot])


M_, B_, W_, Y_, R_, K_ = pv.METAL, pv.BLACK, pv.WHITE, pv.YELLOW, pv.RED, pv.KHAKI


def helipad_kit(size=15.0, deck=0.22):
    """Helipad.Build (Helipad.cs)."""
    h, top = size / 2, deck + 0.004
    k = pb.Kit()
    k.slot = M_; k.box((0, deck / 2, 0), (size, deck, size))
    k.slot = B_; k.box((0, deck * 0.3, 0), (size + 0.12, deck * 0.6, size + 0.12))
    k.slot = W_
    k.box((0, top, h - 0.35), (size - 0.4, 0.01, 0.3)); k.box((0, top, -h + 0.35), (size - 0.4, 0.01, 0.3))
    k.box((h - 0.35, top, 0), (0.3, 0.01, size - 0.4)); k.box((-h + 0.35, top, 0), (0.3, 0.01, size - 0.4))
    k.slot = Y_
    ring = h * 0.62
    for i in range(36):
        a = i * 10.0
        k.box((math.sin(math.radians(a)) * ring, top, math.cos(math.radians(a)) * ring), (0.45, 0.012, ring * 0.175 + 0.02), 1, 1, (0, a + 90, 0))
    k.slot = W_
    k.box((-1.2, top + 0.002, 0), (0.6, 0.012, 4.0)); k.box((1.2, top + 0.002, 0), (0.6, 0.012, 4.0)); k.box((0, top + 0.002, 0), (1.8, 0.012, 0.6))
    k.slot = Y_
    for i in range(4):
        for j in range(4):
            u = -h + 0.6 + j * (size - 1.2) / 3
            p = [(u, 0, h - 0.15), (u, 0, -h + 0.15), (h - 0.15, 0, u), (-h + 0.15, 0, u)][i]
            k.box((p[0], deck + 0.06, p[2]), (0.14, 0.12, 0.14))
    k.slot = M_; k.tube((h + 0.6, 0, h + 0.6), 0, 3.2, (0.05, 0.05), (0.04, 0.04), 8, False)
    k.slot = R_; k.tube((h + 0.6, 3.05, h + 0.6), 0, 1.4, (0.22, 0.22), (0.1, 0.1), 10, False, (0, 0, -80))
    return k


def hut_kit(deck):
    """Lift.BuildHut / DoorSet / BuildDeck (Lift.cs), the solid parts."""
    W, D, H = 2.6, 2.4, 2.8
    k = pb.Kit()
    k.slot = W_; k.box((0, H / 2, 0), (W, H, D))
    k.slot = M_; k.box((0, H + 0.1, 0), (W + 0.3, 0.2, D + 0.3))
    k.slot = K_; k.box((0, 0.1, 0), (W + 0.06, 0.2, D + 0.06))
    z = D / 2
    k.slot = B_; k.box((0, 1.08, z + 0.02), (1.16, 2.16, 0.03))
    k.slot = M_
    k.box((-0.66, 1.12, z + 0.06), (0.16, 2.3, 0.1)); k.box((0.66, 1.12, z + 0.06), (0.16, 2.3, 0.1)); k.box((0, 2.3, z + 0.06), (1.48, 0.16, 0.1))
    k.box((-0.3, 1.06, z + 0.05), (0.56, 2.1, 0.04)); k.box((0.3, 1.06, z + 0.05), (0.56, 2.1, 0.04))
    if deck > 0:
        k.box((0, -0.06, 1.1), (3.0, 0.12, 4.6))
        for x in (-1.4, 1.4):
            for zz in (-1.1, 1.1, 3.3):
                k.box((x, -deck / 2, zz), (0.1, deck, 0.1))
            k.box((x * 1.043, 1.0, 2.3), (0.05, 0.05, 2.1))
        k.slot = Y_; k.box((0, 0.005, 3.36), (3.0, 0.012, 0.12))
    return k


def bar(k, a, b, t):
    a, b = np.asarray(a, float), np.asarray(b, float)
    d = b - a; L = float(np.linalg.norm(d))
    if L < 0.01: return
    d /= L
    yaw = math.degrees(math.atan2(d[0], d[2])); pitch = -math.degrees(math.asin(max(-1, min(1, d[1]))))
    k.box(tuple((a + b) / 2), (t, t, L + t), 1, 1, (pitch, yaw, 0))


def corners(w, rnd):
    x, z, wd, dp = w[0], w[1], w[2], w[3]
    if not rnd:
        return [(x - wd / 2, z - dp / 2), (x + wd / 2, z - dp / 2), (x + wd / 2, z + dp / 2), (x - wd / 2, z + dp / 2)]
    return [(x + math.cos(i * math.tau / 20) * wd / 2, z + math.sin(i * math.tau / 20) * dp / 2) for i in range(20)]


def skeleton(wings, hot_level, shaft_lo, shaft_hi, yaw):
    """Lift.BuildSkeleton (Lift.cs): storey outlines and plates, columns, shaft rails; base y = 0."""
    lines, hot, plates = pb.Kit(), pb.Kit(), pb.Kit()
    highest = max(w[5] for w in wings)
    storeys = max(1, math.ceil((highest - 0.6) / 3.4 - 0.2))
    for L in range(storeys + 1):
        y = 0.6 + L * 3.4
        for w in wings:
            roof = w[5]; wy = y
            if wy > roof + 0.3: continue
            if wy > roof - 3.4 * 0.6: wy = roof
            poly = corners(w, w[4]); kk = hot if L == hot_level else lines; t = 0.2 if L == hot_level else 0.11
            for i in range(len(poly)):
                a, b = poly[i], poly[(i + 1) % len(poly)]
                bar(kk, (a[0], wy, a[1]), (b[0], wy, b[1]), t)
            if L > 0:
                if w[4]: plates.tube((w[0], wy - 0.02, w[1]), 0, 0.02, (w[2] / 2, w[3] / 2), (w[2] / 2, w[3] / 2), 20, True)
                else: plates.box((w[0], wy - 0.01, w[1]), (w[2], 0.02, w[3]))
    for w in wings:
        poly = corners(w, w[4])
        for i in range(len(poly)):
            a, b = np.array(poly[i]), np.array(poly[(i + 1) % len(poly)])
            if w[4]:
                if i % 2: continue
                steps = 1
            else:
                steps = max(1, round(float(np.linalg.norm(b - a)) / 8))
            for s_ in range(steps):
                p = a + (b - a) * s_ / steps
                bar(lines, (p[0], 0, p[1]), (p[0], w[5], p[1]), 0.12)
    fx = pb.ry(yaw) @ np.array([0.85, 0, 0]); fz = pb.ry(yaw) @ np.array([0, 0, 0.85])
    for off in (fx + fz, fx - fz, -fx + fz, -fx - fz):
        bar(lines, np.asarray(shaft_lo) + off, np.asarray(shaft_hi) + off, 0.07)
    return lines, hot, plates


def car_kit():
    k, g = pb.Kit(), pb.Kit()
    hw, H = 0.8, 2.4
    for y in (0, H):
        bar(k, (-hw, y, -hw), (hw, y, -hw), 0.06); bar(k, (-hw, y, hw), (hw, y, hw), 0.06)
        bar(k, (-hw, y, -hw), (-hw, y, hw), 0.06); bar(k, (hw, y, -hw), (hw, y, hw), 0.06)
    for p in ((-hw, 0, -hw), (hw, 0, -hw), (-hw, 0, hw), (hw, 0, hw)):
        bar(k, p, (p[0], H, p[2]), 0.06)
    g.box((0, H / 2, 0), (1.56, 2.36, 1.56))
    return k, g


def world_setup(sc, sky=(0.55, 0.62, 0.72), strength=1.0):
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5; sun.angle = 0.1
    so = bpy.data.objects.new("sun", sun); so.rotation_euler = (math.radians(48), math.radians(8), math.radians(-30)); sc.collection.objects.link(so)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (*sky, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = strength
    gm = bpy.data.meshes.new("ground")
    gm.from_pydata([(-600, -800, -0.3), (600, -800, -0.3), (600, 800, -0.3), (-600, 800, -0.3)], [], [(0, 1, 2, 3)])
    go = bpy.data.objects.new("ground", gm); sc.collection.objects.link(go)
    go.data.materials.append(solid("grass", (0.3, 0.36, 0.2)))


def camera(sc, at, look, lens, path, full):
    cam = bpy.data.cameras.new("cam"); cam.lens = lens; cam.clip_end = 2000
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = U(at)
    d = Vector(U(look)) - Vector(U(at))
    co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 48 if full else 16
    sc.render.resolution_x, sc.render.resolution_y = (1600, 900) if full else (800, 450)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("wrote", path)


def render_heli(spots, full):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    m = models["Academic_Block"]
    place_building(m["fbx"], m["pivotX"], m["pivotZ"])
    world_setup(sc)
    pad = spots.get("helipad") or [101.0, 13.6, 225.0]
    kit_object("pad", helipad_kit(), pad, 90, loot)
    for sp in spots.get("Academic Block", []):
        kit_object("hut", hut_kit(sp["deck"]), sp["head"], sp["headYaw"], loot)
    pv.PARTS.clear()
    pv.build_vehicles((pad[0], pad[1] + 0.22, pad[2]), "Heli", pv.WHITE, 90 + 25)
    pv.emit()
    camera(sc, (pad[0] + 19, pad[1] + 9, pad[2] - 17), (pad[0] - 2, pad[1] + 1.5, pad[2] + 3), 32, os.path.join(OUT, "helipad.png"), full)


def render_cutaway(spots, zone, full):
    sp = spots[zone][0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    m = models[sp["model"]]
    place_building(m["fbx"], m["pivotX"], m["pivotZ"], ghost=True)
    world_setup(sc, (0.42, 0.5, 0.62), 0.45)
    yaw = sp["yaw"]
    f = pb.ry(yaw) @ np.array([0, 0, 1.0])
    lo = np.array(sp["face"]) - f * 1.05
    hi = np.array([lo[0], sp["head"][1], lo[2]])
    car_y = 0.6 + 3 * 3.4
    lines, hot, plates = skeleton(sp["wings"], 3, lo, hi + np.array([0, 2.8, 0]), yaw)
    kit_object("frame", lines, (0, 0, 0), 0, lambda s: glow("frame", (0.18, 0.7, 1.0), 1.6))
    kit_object("hot", hot, (0, 0, 0), 0, lambda s: glow("hot", (1.0, 0.78, 0.35), 5.0))
    kit_object("plates", plates, (0, 0, 0), 0, lambda s: glow("plate", (0.12, 0.42, 0.6), 0.25))
    ck, cg = car_kit()
    at = (lo[0], car_y, lo[2])
    kit_object("car", ck, at, yaw, lambda s: glow("hot", (1.0, 0.78, 0.35), 5.0))
    kit_object("carglass", cg, at, yaw, lambda s: glow("carglass", (0.75, 0.92, 1.0), 1.5))
    lt = bpy.data.lights.new("carlight", "POINT"); lt.energy = 1500; lt.color = (0.75, 0.92, 1.0)
    lo_ = bpy.data.objects.new("carlight", lt); lo_.location = U((at[0], at[1] + 2.1, at[2])); sc.collection.objects.link(lo_)
    kit_object("hut", hut_kit(sp["deck"]), sp["head"], sp["headYaw"], loot)
    # The orbit camera as Lift.PickOrbit puts it, from the south-east.
    h = sp["head"][1]
    r = max(26, min(46, h * 0.9 + 16)) * 1.3
    cyaw = math.radians(150)
    tgt = np.array([lo[0], car_y + 1.2, lo[2]])
    cam = tgt + np.array([math.sin(cyaw), 0, math.cos(cyaw)]) * r + np.array([0, 4 + r * 0.14, 0])
    camera(sc, cam, tgt, 24, os.path.join(OUT, "lift_cutaway.png"), full)


def main():
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "lift_spots.json")
    full = "--full" in sys.argv
    if "--check" in sys.argv or not os.path.exists(path):
        json.dump(find_spots(), open(path, "w"), indent=1)
    spots = json.load(open(path))
    want_heli = "--heli" in sys.argv
    want_cut = "--cutaway" in sys.argv
    if not want_heli and not want_cut and "--check" in sys.argv: return
    if want_heli or not want_cut: render_heli(spots, full)
    if want_cut or not want_heli:
        zone = sys.argv[sys.argv.index("--cutaway") + 1] if want_cut and sys.argv.index("--cutaway") + 1 < len(sys.argv) else "Girls Hostel A"
        render_cutaway(spots, zone, full)


if __name__ == "__main__":
    main()
