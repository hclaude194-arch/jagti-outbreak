#!/usr/bin/env python3
"""Build the landmark buildings in headless Blender and export them as FBX.

Footprints, storeys and positions come from Assets/Data/campus_layout.json, so the
models always match the layout. Each model is one mesh with named material slots
(Facade_Academic, Brick, Glass, ...). Unity swaps those slots for its own
procedural materials (see Assets/Editor/BuildingMaterials.cs), so Blender only
needs flat preview colours.

Run with the Python that has the `bpy` module (pip install bpy, Python 3.13):
    python tools/blender/build_buildings.py            # export FBX + building_models.json
    python tools/blender/build_buildings.py --render   # also render previews to tools/blender/preview/

Coordinates: world X/Z are the Unity layout coordinates (metres, +Z = map up).
Blender is Z-up; a model's local Blender axes are (-dX, -dZ, up) from its pivot,
because Blender's FBX export (-Z forward, Y up) plus Unity's import turn the model
180 degrees. Unity checks each model's bounds against the layout on import and
corrects the turn if this ever comes out differently.
"""
import json
import math
import os
import sys

import bpy  # must come first: it makes bmesh and mathutils importable
import bmesh

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
LAYOUT = os.path.join(ROOT, "Assets", "Data", "campus_layout.json")
OUT_DIR = os.path.join(ROOT, "Assets", "Models", "Buildings")
OUT_JSON = os.path.join(ROOT, "Assets", "Data", "building_models.json")
PREVIEW_DIR = os.path.join(os.path.dirname(__file__), "preview")

with open(LAYOUT) as f:
    layout = json.load(f)
FLOOR = layout["floorHeight"]
BUILDINGS = {b["name"]: b for b in layout["buildings"]}

# name: (tile width m, tile height m, preview colour). Tile sizes must match BuildingMaterials.cs.
MATERIALS = {
    "Facade_Academic": (3.6, FLOOR, (0.80, 0.74, 0.62)),
    "Facade_Hostel": (3.0, FLOOR, (0.90, 0.87, 0.80)),
    "Jali": (1.2, 1.2, (0.86, 0.83, 0.76)),
    "Facade_Ground": (4.0, FLOOR, (0.35, 0.42, 0.48)),
    "Brick": (2.0, 2.0, (0.60, 0.32, 0.23)),
    "Plaster": (4.0, 4.0, (0.86, 0.81, 0.70)),
    "Concrete": (3.0, 3.0, (0.86, 0.85, 0.82)),
    "Glass": (1.5, FLOOR, (0.25, 0.34, 0.42)),
    "Roof": (6.0, 6.0, (0.38, 0.37, 0.35)),
    "Metal": (2.0, 2.0, (0.22, 0.23, 0.24)),
    "ClockFace": (1.0, 1.0, (0.95, 0.94, 0.90)),
    "Water": (4.0, 4.0, (0.20, 0.45, 0.50)),
    "Stone": (3.0, 3.0, (0.84, 0.77, 0.64)),
}
FACADES = {"Facade_Academic", "Facade_Hostel", "Facade_Ground", "Glass"}  # whole bays per wall


# ------------------------------------------------------------------ mesh builder
class Model:
    """Collects faces with material slots and UVs, then becomes one Blender mesh."""

    def __init__(self, name, pivot_x, pivot_z):
        self.name = name
        self.px, self.pz = pivot_x, pivot_z
        self.verts, self.faces = [], []  # faces: (vertex ids, material, uvs)
        self.replaces = []
        self.solid = []  # world rects for the bounds check
        self.blocks = 0  # wings built so far; each later wing's roof sits a little higher

    # world (X, Z) -> local Blender (x, y)
    def b(self, X, Z):
        return (-(X - self.px), -(Z - self.pz))

    def rect(self, X0, X1, Z0, Z1):
        xa, ya = self.b(X0, Z0)
        xb, yb = self.b(X1, Z1)
        return min(xa, xb), max(xa, xb), min(ya, yb), max(ya, yb)

    def quad(self, pts, mat, uvs):
        base = len(self.verts)
        self.verts.extend(pts)
        self.faces.append((list(range(base, base + len(pts))), mat, uvs))

    def wall(self, a, bpt, z0, z1, mat, zbase=0.0):
        """Vertical quad from a to b (Blender xy), outside on the right-hand normal."""
        length = math.dist(a, bpt)
        if length < 1e-4 or z1 - z0 < 1e-4:
            return
        tw, th, _ = MATERIALS[mat]
        if mat in FACADES:
            bays = max(1, round(length / tw))
            u1 = float(bays)
        else:
            u1 = length / tw
        v0, v1 = (z0 - zbase) / th, (z1 - zbase) / th
        self.quad([(a[0], a[1], z0), (bpt[0], bpt[1], z0), (bpt[0], bpt[1], z1), (a[0], a[1], z1)],
                  mat, [(0, v0), (u1, v0), (u1, v1), (0, v1)])

    def flat(self, x0, x1, y0, y1, z, mat, up=True):
        tw, th, _ = MATERIALS[mat]
        pts = [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
        uvs = [(x / tw, y / th) for x, y, _ in pts]
        if not up:
            pts.reverse()
            uvs.reverse()
        self.quad(pts, mat, uvs)

    def box_local(self, x0, x1, y0, y1, z0, z1, side, top="Roof", bottom=False, zbase=None, sides="NESW"):
        zb = z0 if zbase is None else zbase
        if "S" in sides:
            self.wall((x0, y0), (x1, y0), z0, z1, side, zb)
        if "E" in sides:
            self.wall((x1, y0), (x1, y1), z0, z1, side, zb)
        if "N" in sides:
            self.wall((x1, y1), (x0, y1), z0, z1, side, zb)
        if "W" in sides:
            self.wall((x0, y1), (x0, y0), z0, z1, side, zb)
        if top:
            self.flat(x0, x1, y0, y1, z1, top)
        if bottom:
            self.flat(x0, x1, y0, y1, z0, top or side, up=False)

    def box(self, X0, X1, Z0, Z1, z0, z1, side, top="Roof", **kw):
        x0, x1, y0, y1 = self.rect(X0, X1, Z0, Z1)
        self.box_local(x0, x1, y0, y1, z0, z1, side, top, **kw)

    def cylinder_local(self, cx, cy, r, z0, z1, side, top="Roof", segments=28, zbase=None):
        zb = z0 if zbase is None else zbase
        tw, th, _ = MATERIALS[side]
        circ = 2 * math.pi * r
        total_u = max(1, round(circ / tw)) if side in FACADES else circ / tw
        ring = [(cx + r * math.cos(2 * math.pi * k / segments), cy + r * math.sin(2 * math.pi * k / segments))
                for k in range(segments)]
        for k in range(segments):
            a, bpt = ring[k], ring[(k + 1) % segments]
            u0, u1 = total_u * k / segments, total_u * (k + 1) / segments
            v0, v1 = (z0 - zb) / th, (z1 - zb) / th
            self.quad([(a[0], a[1], z0), (bpt[0], bpt[1], z0), (bpt[0], bpt[1], z1), (a[0], a[1], z1)],
                      side, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
        if top:
            ttw, tth, _ = MATERIALS[top]
            pts = [(x, y, z1) for x, y in ring]
            self.quad(pts, top, [(x / ttw, y / tth) for x, y, _ in pts])

    def pyramid_local(self, x0, x1, y0, y1, z0, z1, mat):
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        for k in range(4):
            a, bpt = corners[k], corners[(k + 1) % 4]
            self.quad([(a[0], a[1], z0), (bpt[0], bpt[1], z0), (cx, cy, z1)], mat, [(0, 0), (1, 0), (0.5, 1)])

    def face(self, pts, mat, hint):
        """Any planar face; vertex order is flipped if needed so the normal points along hint."""
        a, b_, c = pts[0], pts[1], pts[2]
        u = (b_[0] - a[0], b_[1] - a[1], b_[2] - a[2])
        v = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
        n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        if n[0] * hint[0] + n[1] * hint[1] + n[2] * hint[2] < 0:
            pts = list(reversed(pts))
            n = (-n[0], -n[1], -n[2])
        tw, th, _ = MATERIALS[mat]
        ax = max(range(3), key=lambda i: abs(n[i]))  # project onto the plane facing the normal
        if ax == 2:
            uvs = [(p[0] / tw, p[1] / th) for p in pts]
        elif ax == 0:
            uvs = [(p[1] / tw, p[2] / th) for p in pts]
        else:
            uvs = [(p[0] / tw, p[2] / th) for p in pts]
        self.quad(pts, mat, uvs)

    def prism(self, poly, z0, z1, side, top):
        """Vertical prism over a convex polygon given in local (x, y); walls face outwards."""
        cx = sum(p[0] for p in poly) / len(poly)
        cy = sum(p[1] for p in poly) / len(poly)
        for k in range(len(poly)):
            a, b_ = poly[k], poly[(k + 1) % len(poly)]
            mx, my = (a[0] + b_[0]) / 2 - cx, (a[1] + b_[1]) / 2 - cy
            self.face([(a[0], a[1], z0), (b_[0], b_[1], z0), (b_[0], b_[1], z1), (a[0], a[1], z1)], side, (mx, my, 0))
        if top:
            self.face([(p[0], p[1], z1) for p in poly], top, (0, 0, 1))

    def pool(self, poly, rim, water, inset=0.4):
        """A raised basin: concrete walls to the rim, a rim band, and the water surface a little below it."""
        inner = inset_polygon(poly, inset)
        self.prism(poly, -0.6, rim, "Concrete", None)
        cx = sum(p[0] for p in poly) / len(poly)
        cy = sum(p[1] for p in poly) / len(poly)
        n = len(poly)
        for k in range(n):
            a, b_ = poly[k], poly[(k + 1) % n]
            ia, ib = inner[k], inner[(k + 1) % n]
            self.face([(a[0], a[1], rim), (b_[0], b_[1], rim), (ib[0], ib[1], rim), (ia[0], ia[1], rim)], "Concrete", (0, 0, 1))
            mx, my = (ia[0] + ib[0]) / 2 - cx, (ia[1] + ib[1]) / 2 - cy
            self.face([(ia[0], ia[1], water), (ib[0], ib[1], water), (ib[0], ib[1], rim), (ia[0], ia[1], rim)], "Concrete", (-mx, -my, 0))
        self.face([(p[0], p[1], water) for p in inner], "Water", (0, 0, 1))

    def local_bounds_to_solid(self):
        """Adds the world rectangle covering every vertex (for models without layout footprints)."""
        xs = [self.px - v[0] for v in self.verts]
        zs = [self.pz - v[1] for v in self.verts]
        self.solid.append((min(xs), max(xs), min(zs), max(zs)))

    # ---------------------------------------------------------------- composite parts
    def block(self, name, style):
        """A building wing from the layout: plinth, facade floors, floor bands, parapet, roof clutter."""
        bd = BUILDINGS[name]
        self.replaces.append(name)
        X, Z, w, d, floors = bd["x"], bd["z"], bd["width"], bd["depth"], bd["floors"]
        body = floors * FLOOR
        # Overlapping wings would share a roof plane and flicker, so each later wing
        # gets a slightly taller roof band.
        lift = 0.35 * self.blocks
        self.blocks += 1
        H = body + lift
        X0, X1, Z0, Z1 = X - w / 2, X + w / 2, Z - d / 2, Z + d / 2
        self.solid.append((X0, X1, Z0, Z1))
        x0, x1, y0, y1 = self.rect(X0, X1, Z0, Z1)
        facade = {"academic": "Facade_Academic", "hostel": "Facade_Hostel", "mess": "Facade_Academic"}[style]
        ground = "Facade_Ground" if style in ("academic", "mess") else facade

        # Plinth reaches below ground so the block sits on a slight slope.
        self.box_local(x0 - 0.3, x1 + 0.3, y0 - 0.3, y1 + 0.3, -0.8, 0.45, "Concrete", "Concrete")
        self.box_local(x0, x1, y0, y1, 0.45, FLOOR, ground, None, zbase=0.0)
        if floors > 1:
            self.box_local(x0, x1, y0, y1, FLOOR, body, facade, None, zbase=0.0)
        if lift > 0:
            self.box_local(x0, x1, y0, y1, body, H, "Concrete", None)
        self.flat(x0, x1, y0, y1, H, "Roof")

        # Floor bands / sunshade ledges.
        ledge = 0.45 if style == "hostel" else 0.15
        for k in range(1, floors):
            z = k * FLOOR
            self.box_local(x0 - ledge, x1 + ledge, y0 - ledge, y1 + ledge, z - 0.12, z + 0.12, "Concrete", "Concrete", bottom=True)

        # Parapet as four thin walls so the roof shows inside.
        t, ph = 0.25, 1.0
        self.box_local(x0 - 0.1, x1 + 0.1, y0 - 0.1, y0 + t, H, H + ph, "Concrete", "Concrete")
        self.box_local(x0 - 0.1, x1 + 0.1, y1 - t, y1 + 0.1, H, H + ph, "Concrete", "Concrete")
        self.box_local(x0 - 0.1, x0 + t, y0 + t, y1 - t, H, H + ph, "Concrete", "Concrete")
        self.box_local(x1 - t, x1 + 0.1, y0 + t, y1 - t, H, H + ph, "Concrete", "Concrete")

        long_x = (x1 - x0) >= (y1 - y0)
        if style == "academic" and floors > 1:
            # Vertical sun fins every two bays on the long faces (green-building shading).
            length = (x1 - x0) if long_x else (y1 - y0)
            bays = max(1, round(length / 3.6))
            step = length / bays
            for k in range(0, bays + 1, 2):
                s = (x0 if long_x else y0) + k * step
                for edge in ((y0, -1), (y1, 1)) if long_x else ((x0, -1), (x1, 1)):
                    e, sign = edge
                    if long_x:
                        self.box_local(s - 0.15, s + 0.15, min(e, e + sign * 0.7), max(e, e + sign * 0.7), FLOOR, body, "Concrete", "Concrete")
                    else:
                        self.box_local(min(e, e + sign * 0.7), max(e, e + sign * 0.7), s - 0.15, s + 0.15, FLOOR, body, "Concrete", "Concrete")
        if style == "hostel":
            # Stair towers at both ends of the long side, faced with jali (lattice) panels
            # as in the owner's photos, rising above the roof.
            cw = min(4.0, (x1 - x0 if long_x else y1 - y0) / 4)
            if long_x:
                for xa in (x0 - 0.4, x1 - cw + 0.4):
                    self.box_local(xa, xa + cw, y0 - 0.4, y0 + 3.5, 0.45, H + 2.8, "Jali", "Roof")
            else:
                for ya in (y0 - 0.4, y1 - cw + 0.4):
                    self.box_local(x0 - 0.4, x0 + 3.5, ya, ya + cw, 0.45, H + 2.8, "Jali", "Roof")
        # Roof clutter: a stair cabin and a water tank.
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        if floors > 1:
            self.box_local(cx - 2.2, cx + 2.2, cy - 2.0, cy + 2.0, H, H + 3.0, "Plaster", "Roof")
            self.box_local(cx + 3.0, cx + 6.0, cy - 1.2, cy + 1.2, H + 0.6, H + 2.4, "Metal", "Metal")
        if style == "hostel" and floors >= 6:
            self.pergola(x0 + 1.5, x0 + min(13.0, (x1 - x0) * 0.45), y0 + 1.5, y1 - 1.5, H)
        return x0, x1, y0, y1, H

    def pergola(self, x0, x1, y0, y1, z):
        """White roof pergola: four posts, edge beams and cross slats."""
        if x1 - x0 < 3 or y1 - y0 < 3:
            return
        top = z + 3.2
        for px in (x0, x1 - 0.3):
            for py in (y0, y1 - 0.3):
                self.box_local(px, px + 0.3, py, py + 0.3, z, top, "Concrete", None)
        self.box_local(x0, x1, y0, y0 + 0.3, top - 0.35, top, "Concrete", "Concrete", bottom=True)
        self.box_local(x0, x1, y1 - 0.3, y1, top - 0.35, top, "Concrete", "Concrete", bottom=True)
        n = max(2, int((x1 - x0) / 1.2))
        for k in range(n + 1):
            sx = x0 + (x1 - x0 - 0.15) * k / n
            self.box_local(sx, sx + 0.15, y0, y1, top - 0.2, top + 0.05, "Concrete", "Concrete", bottom=True)

    def drum(self, name, glass_band=(6.6, 8.8)):
        bd = BUILDINGS[name]
        self.replaces.append(name)
        X, Z, r, floors = bd["x"], bd["z"], bd["width"] / 2, bd["floors"]
        H = floors * FLOOR + 1.2
        self.solid.append((X - r, X + r, Z - r, Z + r))
        cx, cy = self.b(X, Z)
        g0, g1 = glass_band
        self.cylinder_local(cx, cy, r + 0.3, -0.8, 0.45, "Concrete", "Concrete")
        self.cylinder_local(cx, cy, r, 0.45, g0, "Brick", None)
        self.cylinder_local(cx, cy, r - 0.35, g0, g1, "Glass", None, zbase=g0)
        self.cylinder_local(cx, cy, r, g1, H, "Brick", "Roof")
        self.cylinder_local(cx, cy, r + 0.15, g0 - 0.25, g0, "Concrete", "Concrete")  # sill ring
        self.cylinder_local(cx, cy, r + 0.2, H, H + 0.5, "Concrete", "Concrete")       # coping
        return cx, cy, r, H

    # ---------------------------------------------------------------- output
    def to_object(self):
        mesh = bpy.data.meshes.new(self.name)
        bm = bmesh.new()
        bverts = [bm.verts.new(v) for v in self.verts]
        uv_layer = bm.loops.layers.uv.new("UVMap")
        names = []
        for ids, mat, uvs in self.faces:
            if mat not in names:
                names.append(mat)
            try:
                face = bm.faces.new([bverts[i] for i in ids])
            except ValueError:
                continue
            face.material_index = names.index(mat)
            for loop, uv in zip(face.loops, uvs):
                loop[uv_layer].uv = uv
        bm.to_mesh(mesh)
        bm.free()
        for n in names:
            mesh.materials.append(material(n))
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj

    def world_bounds(self):
        xs = [v for r in self.solid for v in r[:2]]
        zs = [v for r in self.solid for v in r[2:]]
        return [min(xs), min(zs)], [max(xs), max(zs)]


def inset_polygon(poly, d):
    """Offsets a convex polygon inwards by d (works for either winding)."""
    n = len(poly)
    area = sum(poly[k][0] * poly[(k + 1) % n][1] - poly[(k + 1) % n][0] * poly[k][1] for k in range(n))
    sign = 1.0 if area > 0 else -1.0
    lines = []
    for k in range(n):
        a, b_ = poly[k], poly[(k + 1) % n]
        ex, ey = b_[0] - a[0], b_[1] - a[1]
        L = math.hypot(ex, ey)
        nx, ny = -ey / L * sign, ex / L * sign  # left normal of a CCW polygon points inside
        lines.append(((a[0] + nx * d, a[1] + ny * d), (ex, ey)))
    out = []
    for k in range(n):
        (p1, d1), (p2, d2) = lines[k - 1], lines[k]
        den = d1[0] * d2[1] - d1[1] * d2[0]
        if abs(den) < 0.03 * math.hypot(*d1) * math.hypot(*d2):
            out.append(p2)  # nearly straight corner: the offset start point is good enough
            continue
        t = ((p2[0] - p1[0]) * d2[1] - (p2[1] - p1[1]) * d2[0]) / den
        out.append((p1[0] + d1[0] * t, p1[1] + d1[1] * t))
    return out


def material(name):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (*MATERIALS[name][2], 1.0)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*MATERIALS[name][2], 1.0)
            bsdf.inputs["Roughness"].default_value = 0.1 if name in ("Glass", "Water") else 0.8
    return mat


def centre(name):
    bd = BUILDINGS[name]
    return bd["x"], bd["z"]


# ------------------------------------------------------------------ the buildings
# ------------------------------------------------------------------ detailed facades
BAY = 3.6  # academic window bay width (m)


class Run:
    """One straight facade from a to b (local Blender xy) with outward unit normal n."""

    def __init__(self, m, a, b, n=None):
        self.m = m
        self.a, self.b = a, b
        self.L = math.dist(a, b)
        self.t = ((b[0] - a[0]) / self.L, (b[1] - a[1]) / self.L)
        # Right-hand normal of a->b; callers pass the outline anticlockwise (seen from above).
        self.n = n if n is not None else (self.t[1], -self.t[0])

    def P(self, s, d, z):
        return (self.a[0] + self.t[0] * s + self.n[0] * d, self.a[1] + self.t[1] * s + self.n[1] * d, z)

    def N(self, k=1.0):
        return (self.n[0] * k, self.n[1] * k, 0)

    def rect(self, s0, s1, z0, z1, d, mat, facing=1.0):
        if s1 - s0 < 1e-3 or z1 - z0 < 1e-3:
            return
        self.m.face([self.P(s0, d, z0), self.P(s1, d, z0), self.P(s1, d, z1), self.P(s0, d, z1)], mat, self.N(facing))

    def hband(self, s0, s1, z, d0, d1, mat, up=True):
        """Horizontal strip between depths d0 and d1 (a sill, soffit or ledge top)."""
        self.m.face([self.P(s0, d0, z), self.P(s1, d0, z), self.P(s1, d1, z), self.P(s0, d1, z)], mat, (0, 0, 1 if up else -1))

    def side(self, s, z0, z1, d0, d1, mat, facing):
        """Vertical strip perpendicular to the facade at position s (reveals, pilaster sides)."""
        t = self.t
        self.m.face([self.P(s, d0, z0), self.P(s, d1, z0), self.P(s, d1, z1), self.P(s, d0, z1)], mat,
                    (t[0] * facing, t[1] * facing, 0))

    def window(self, s0, s1, z0, z1, ws0, ws1, wz0, wz1, depth, wall="Stone", reveal="Stone", frame=True):
        """Wall panel s0..s1 x z0..z1 with a recessed window ws0..ws1 x wz0..wz1."""
        self.rect(s0, s1, z0, wz0, 0, wall)
        self.rect(s0, s1, wz1, z1, 0, wall)
        self.rect(s0, ws0, wz0, wz1, 0, wall)
        self.rect(ws1, s1, wz0, wz1, 0, wall)
        self.hband(ws0, ws1, wz0, 0, -depth, reveal, up=True)
        self.hband(ws0, ws1, wz1, 0, -depth, reveal, up=False)
        self.side(ws0, wz0, wz1, 0, -depth, reveal, 1)
        self.side(ws1, wz0, wz1, 0, -depth, reveal, -1)
        self.rect(ws0, ws1, wz0, wz1, -depth, "Glass")
        if frame:  # projecting sill
            self.box(ws0 - 0.1, ws1 + 0.1, wz0 - 0.12, wz0, 0.0, 0.12, "Concrete")

    def box(self, s0, s1, z0, z1, d0, d1, mat, top=True, bottom=True):
        """A projecting box on the facade (pilaster, fin, ledge)."""
        self.rect(s0, s1, z0, z1, d1, mat)
        self.side(s0, z0, z1, d0, d1, mat, -1)
        self.side(s1, z0, z1, d0, d1, mat, 1)
        if top:
            self.hband(s0, s1, z1, d0, d1, mat, up=True)
        if bottom:
            self.hband(s0, s1, z0, d0, d1, mat, up=False)


def academic_facade(run, z0, floors, kind="outer", ribbon=True, pilasters=True, inner=True):
    """Sandstone facade from the campus model: tall recessed windows between pilasters,
    a blue glass ribbon on the top floor, string courses and a parapet with coping."""
    L = run.L
    bays = max(1, round(L / BAY))
    bw = L / bays
    top = z0 + floors * FLOOR
    lower = floors - 1 if ribbon else floors
    depth = 0.35 if kind == "outer" else 0.22
    # Plinth.
    run.box(0, L, -0.8, z0 + 0.6, 0.0, 0.18, "Concrete", bottom=False)
    for i in range(bays):
        s0, s1 = i * bw, (i + 1) * bw
        for f in range(lower):
            zf = z0 + f * FLOOR
            zb = z0 + 0.6 if f == 0 else zf
            wz0 = zf + (0.75 if f else 0.95)
            wz1 = zf + FLOOR - 0.4
            if kind == "outer":
                # Two tall, slender windows per bay with a narrow stone pier between them.
                mid = (s0 + s1) / 2
                run.window(s0, mid, zb, zf + FLOOR, s0 + 0.42, mid - 0.14, wz0, wz1, depth)
                run.window(mid, s1, zb, zf + FLOOR, mid + 0.14, s1 - 0.42, wz0, wz1, depth)
            else:
                run.window(s0, s1, zb, zf + FLOOR, s0 + 0.8, s1 - 0.8, wz0 + 0.15, wz1, depth)
        if ribbon:
            zf = z0 + lower * FLOOR
            run.rect(s0, s1, zf, zf + 0.5, 0, "Stone")
            run.rect(s0, s1, zf + FLOOR - 0.35, top, 0, "Stone")
            run.hband(s0, s1, zf + 0.5, 0, -0.45, "Stone", up=True)
            run.hband(s0, s1, zf + FLOOR - 0.35, 0, -0.45, "Stone", up=False)
            run.rect(s0, s1, zf + 0.5, zf + FLOOR - 0.35, -0.45, "Glass")
    if ribbon:
        zf = z0 + lower * FLOOR
        run.side(0, zf + 0.5, zf + FLOOR - 0.35, 0, -0.45, "Stone", 1)
        run.side(L, zf + 0.5, zf + FLOOR - 0.35, 0, -0.45, "Stone", -1)
    # Pilasters between the bays on the lower floors, and a string course under the ribbon.
    if pilasters:
        zp = z0 + lower * FLOOR
        for i in range(bays + 1):
            s = i * bw
            run.box(max(0, s - 0.28), min(L, s + 0.28), z0 + 0.6, zp, 0.0, 0.32, "Stone", bottom=False)
    if ribbon:
        zc = z0 + lower * FLOOR
        run.box(0, L, zc - 0.1, zc + 0.15, 0.0, 0.42, "Concrete")
    # Parapet with coping.
    run.rect(0, L, top, top + 1.1, 0, "Stone")
    if inner:
        run.rect(0, L, top, top + 1.1, -0.3, "Stone", facing=-1)
    run.box(-0.05, L + 0.05, top + 1.1, top + 1.3, -0.35, 0.12, "Concrete")


def fluted_facade(run, z0, floors, glass_top=True):
    """The corner pavilions: closely spaced stone fins on the lower floors, a band of
    windows, and a glazed top floor under a deep cornice."""
    L = run.L
    top = z0 + floors * FLOOR
    fin_top = z0 + (floors - 2) * FLOOR
    run.box(0, L, -0.8, z0 + 0.6, 0.0, 0.2, "Concrete", bottom=False)
    run.rect(0, L, z0 + 0.6, fin_top, 0, "Stone")
    n = max(2, int(L / 1.15))
    for i in range(n + 1):
        s = L * i / n
        run.box(max(0, s - 0.2), min(L, s + 0.2), z0 + 0.6, fin_top, 0.0, 0.45, "Stone", bottom=False)
    # A row of square windows.
    bays = max(1, round(L / 3.0))
    bw = L / bays
    for i in range(bays):
        s0 = i * bw
        run.window(s0, s0 + bw, fin_top, fin_top + FLOOR, s0 + 0.7, s0 + bw - 0.7, fin_top + 0.9, fin_top + FLOOR - 0.6, 0.3)
    zg = fin_top + FLOOR
    if glass_top:
        run.rect(0, L, zg, zg + 0.3, 0, "Stone")
        run.rect(0, L, zg + 0.3, top - 0.2, -0.3, "Glass")
        run.hband(0, L, zg + 0.3, 0, -0.3, "Stone", up=True)
        run.side(0, zg + 0.3, top - 0.2, 0, -0.3, "Stone", 1)
        run.side(L, zg + 0.3, top - 0.2, 0, -0.3, "Stone", -1)
        for i in range(1, int(L / 2.4)):
            s = L * i / int(L / 2.4)
            run.box(s - 0.06, s + 0.06, zg + 0.3, top - 0.2, -0.3, -0.18, "Metal", top=False, bottom=False)
    else:
        run.rect(0, L, zg, top - 0.2, 0, "Stone")
    # Deep cornice and parapet.
    run.box(-0.6, L + 0.6, top - 0.2, top + 0.4, 0.0, 0.8, "Concrete")
    run.rect(0, L, top + 0.4, top + 1.2, 0, "Stone")
    run.rect(0, L, top + 0.4, top + 1.2, -0.3, "Stone", facing=-1)
    run.box(-0.05, L + 0.05, top + 1.2, top + 1.4, -0.3, 0.1, "Concrete")


def roof_with_parapet(m, poly, top, parapet=1.1, inner=0.3):
    """Flat roof over a convex outline plus the inside face of its parapet."""
    m.face([(p[0], p[1], top) for p in poly], "Roof", (0, 0, 1))
    ins = inset_polygon(poly, inner)
    n = len(poly)
    cx = sum(p[0] for p in poly) / n
    cy = sum(p[1] for p in poly) / n
    for k in range(n):
        a, b_ = ins[k], ins[(k + 1) % n]
        mx, my = (a[0] + b_[0]) / 2 - cx, (a[1] + b_[1]) / 2 - cy
        m.face([(a[0], a[1], top), (b_[0], b_[1], top), (b_[0], b_[1], top + parapet), (a[0], a[1], top + parapet)],
               "Stone", (-mx, -my, 0))


def rect_poly(m, X0, X1, Z0, Z1):
    """Anticlockwise (seen from above) outline of a world rectangle in local coordinates."""
    pts = [m.b(X0, Z0), m.b(X1, Z0), m.b(X1, Z1), m.b(X0, Z1)]
    area = sum(pts[k][0] * pts[(k + 1) % 4][1] - pts[(k + 1) % 4][0] * pts[k][1] for k in range(4))
    return pts if area > 0 else pts[::-1]


def outline_runs(m, poly, skip=()):
    """Runs for each edge of an anticlockwise outline (right-hand normal points outwards)."""
    runs = []
    n = len(poly)
    for k in range(n):
        if k in skip:
            continue
        runs.append(Run(m, poly[k], poly[(k + 1) % n]))
    return runs


def rooftop_box(m, x, y, w, d, top, h=3.2):
    m.box_local(x - w / 2, x + w / 2, y - d / 2, y + d / 2, top, top + h, "Stone", "Roof")
    m.box_local(x - w / 2 - 0.15, x + w / 2 + 0.15, y - d / 2 - 0.15, y + d / 2 + 0.15, top + h, top + h + 0.25, "Concrete", "Concrete", bottom=True)


def dedupe(poly, eps=1e-3):
    out = []
    for p in poly:
        if not out or math.dist(p, out[-1]) > eps:
            out.append(p)
    if len(out) > 1 and math.dist(out[0], out[-1]) <= eps:
        out.pop()
    return out


def arc_points(cx, cy, r, a0, a1, segs):
    return [(cx + r * math.cos(a0 + (a1 - a0) * k / segs), cy + r * math.sin(a0 + (a1 - a0) * k / segs)) for k in range(segs + 1)]


def academic_block():
    """The academic block as on the institute's campus model: four sandstone wings round a
    formal courtyard garden, fluted corner pavilions, a porte-cochere and curved glass bay on
    the north front facing the clock tower, the auditorium hall with its curved west end,
    and the curved 'block 1' front facing the amphitheatre on the east."""
    m = Model("Academic_Block", *centre("Academic Block - North Wing"))
    for name in ("Academic Block - North Wing", "Academic Block - South Wing", "Academic Block - West Wing",
                 "Academic Block - East Wing", "Academic Block - Entrance", "Mandapam Auditorium (west drum)", "East Drum"):
        m.replaces.append(name)
        bd = BUILDINGS[name]
        m.solid.append((bd["x"] - bd["width"] / 2, bd["x"] + bd["width"] / 2, bd["z"] - bd["depth"] / 2, bd["z"] + bd["depth"] / 2))

    nw, sw = BUILDINGS["Academic Block - North Wing"], BUILDINGS["Academic Block - South Wing"]
    OX0, OX1 = nw["x"] - nw["width"] / 2, nw["x"] + nw["width"] / 2          # outer west / east faces
    OZ1 = nw["z"] + nw["depth"] / 2                                           # north face
    OZ0 = sw["z"] - sw["depth"] / 2                                           # south face
    ww, ew = BUILDINGS["Academic Block - West Wing"], BUILDINGS["Academic Block - East Wing"]
    IX0, IX1 = ww["x"] + ww["width"] / 2, ew["x"] - ew["width"] / 2           # courtyard west / east
    IZ1, IZ0 = nw["z"] - nw["depth"] / 2, sw["z"] + sw["depth"] / 2           # courtyard north / south
    floors = 4
    H = floors * FLOOR
    P = 15.0       # pavilion size
    PO = 1.6       # pavilions stand proud of the wings
    PH_FLOORS = 5

    # --- the ring of wings: one roof in four strips, facades only on the outside and the courtyard
    strips = [
        (OX0, OX1, IZ1, OZ1),   # north
        (OX0, OX1, OZ0, IZ0),   # south
        (OX0, IX0, IZ0, IZ1),   # west
        (IX1, OX1, IZ0, IZ1),   # east
    ]
    for (X0, X1, Z0, Z1) in strips:
        m.face([(p[0], p[1], H) for p in rect_poly(m, X0, X1, Z0, Z1)], "Roof", (0, 0, 1))
    e0, e1 = OX0 + P - PO, OX1 - P + PO     # outer runs stop at the pavilions
    f0, f1 = OZ0 + P - PO, OZ1 - P + PO
    outer = [((e0, OZ0), (e1, OZ0)), ((OX1, f0), (OX1, f1)), ((e1, OZ1), (e0, OZ1)), ((OX0, f1), (OX0, f0))]
    for (A, Bp) in outer:
        r = Run(m, m.b(*A), m.b(*Bp))
        mid = m.b((A[0] + Bp[0]) / 2, (A[1] + Bp[1]) / 2)
        c = m.b((OX0 + OX1) / 2, (OZ0 + OZ1) / 2)
        if r.n[0] * (mid[0] - c[0]) + r.n[1] * (mid[1] - c[1]) < 0:
            r.n = (-r.n[0], -r.n[1])
        academic_facade(r, 0.0, floors, "outer")
    court = [((IX0, IZ0), (IX1, IZ0)), ((IX1, IZ0), (IX1, IZ1)), ((IX1, IZ1), (IX0, IZ1)), ((IX0, IZ1), (IX0, IZ0))]
    cc = m.b((IX0 + IX1) / 2, (IZ0 + IZ1) / 2)
    for (A, Bp) in court:
        r = Run(m, m.b(*A), m.b(*Bp))
        mid = m.b((A[0] + Bp[0]) / 2, (A[1] + Bp[1]) / 2)
        if r.n[0] * (cc[0] - mid[0]) + r.n[1] * (cc[1] - mid[1]) < 0:
            r.n = (-r.n[0], -r.n[1])   # courtyard walls face into the courtyard
        academic_facade(r, 0.0, floors, "court", ribbon=False, pilasters=False)
    # Stair and lift heads on the roof.
    for (X, Z) in (((OX0 + OX1) / 2 - 22, (IZ1 + OZ1) / 2), ((OX0 + OX1) / 2 + 22, (IZ1 + OZ1) / 2),
                   ((OX0 + OX1) / 2, (OZ0 + IZ0) / 2), ((OX0 + IX0) / 2, (IZ0 + IZ1) / 2), ((IX1 + OX1) / 2, (IZ0 + IZ1) / 2)):
        lx, ly = m.b(X, Z)
        rooftop_box(m, lx, ly, 6.0, 4.5, H)

    # --- fluted corner pavilions, one storey taller
    for (sx, sz) in ((OX0, OZ0), (OX1, OZ0), (OX0, OZ1), (OX1, OZ1)):
        X0 = sx - PO if sx == OX0 else sx - P + PO
        Z0 = sz - PO if sz == OZ0 else sz - P + PO
        poly = rect_poly(m, X0, X0 + P, Z0, Z0 + P)
        top = PH_FLOORS * FLOOR
        m.face([(p[0], p[1], top + 0.4) for p in poly], "Roof", (0, 0, 1))
        for run in outline_runs(m, poly):
            fluted_facade(run, 0.0, PH_FLOORS)
        c = m.b(X0 + P / 2, Z0 + P / 2)
        rooftop_box(m, c[0], c[1], 4.0, 4.0, top + 0.4, 2.6)

    # --- north entrance: curved two-storey glass bay, flanking piers, porte-cochere, fountain
    e = BUILDINGS["Academic Block - Entrance"]
    ex = e["x"]
    bay_r = 9.0
    bc = m.b(ex, OZ1)
    # Bay bulges north (+Z world = -y local).
    pts = arc_points(bc[0], bc[1], bay_r, 0.0, -math.pi, 14)
    for k in range(len(pts) - 1):
        a, b_ = pts[k], pts[k + 1]
        r = Run(m, a, b_)
        mid = ((a[0] + b_[0]) / 2 - bc[0], (a[1] + b_[1]) / 2 - bc[1])
        L = math.hypot(*mid)
        r.n = (mid[0] / L, mid[1] / L)
        r.rect(0, r.L, 0.45, 0.9, 0.0, "Stone")
        r.rect(0, r.L, 0.9, 2 * FLOOR - 0.3, -0.05, "Glass")
        r.rect(0, r.L, 2 * FLOOR - 0.3, 3 * FLOOR + 0.6, 0.0, "Stone")
        r.box(0, 0.25, 0.9, 2 * FLOOR - 0.3, -0.05, 0.25, "Metal")
        r.box(0, r.L, FLOOR - 0.12, FLOOR + 0.12, -0.05, 0.3, "Concrete")
        r.box(0, r.L, 2 * FLOOR + 0.6, 2 * FLOOR + 2.3, 0.0, 0.05, "Glass", top=False, bottom=False)
    m.face([(bc[0], bc[1], 3 * FLOOR + 0.6)] + [(p[0], p[1], 3 * FLOOR + 0.6) for p in pts], "Roof", (0, 0, 1))
    m.cylinder_local(bc[0], bc[1], bay_r + 0.4, 3 * FLOOR + 0.6, 3 * FLOOR + 1.0, "Concrete", None, segments=28)
    # Porte-cochere: a flat slab with a curved front edge on six square columns.
    cz0 = OZ1 + bay_r - 2.0
    cz1 = cz0 + 11.0
    cw = 12.0
    slab = [m.b(ex - cw, cz0), m.b(ex + cw, cz0)]
    front = [m.b(ex + cw * math.cos(t), cz1 - 3.0 + 3.0 * math.sin(t)) for t in [math.pi * k / 10 for k in range(11)]]
    outline = slab + front[::1]
    zs0, zs1 = 6.8, 7.6
    # Slab top, soffit and edge.
    m.face([(p[0], p[1], zs1) for p in outline], "Concrete", (0, 0, 1))
    m.face([(p[0], p[1], zs0) for p in outline], "Concrete", (0, 0, -1))
    cxl = sum(p[0] for p in outline) / len(outline)
    cyl = sum(p[1] for p in outline) / len(outline)
    for k in range(len(outline)):
        a, b_ = outline[k], outline[(k + 1) % len(outline)]
        mx, my = (a[0] + b_[0]) / 2 - cxl, (a[1] + b_[1]) / 2 - cyl
        m.face([(a[0], a[1], zs0), (b_[0], b_[1], zs0), (b_[0], b_[1], zs1), (a[0], a[1], zs1)], "Concrete", (mx, my, 0))
    for X in (ex - cw + 1.2, ex - cw / 3, ex + cw / 3, ex + cw - 1.2):
        for Z in (cz1 - 2.2,):
            c = m.b(X, Z)
            m.box_local(c[0] - 0.4, c[0] + 0.4, c[1] - 0.4, c[1] + 0.4, -0.3, zs0, "Stone", None)
    # Entrance plaza steps and the round fountain pool.
    for k in range(4):
        z = 0.45 - k * 0.15
        a = m.b(ex - 9, cz1 + 0.6 * k)
        b_ = m.b(ex + 9, cz1 + 0.6 * (k + 1))
        m.box_local(min(a[0], b_[0]), max(a[0], b_[0]), min(a[1], b_[1]), max(a[1], b_[1]), -0.6, z, "Concrete", "Concrete")
    plaza = rect_poly(m, ex - cw - 2, ex + cw + 2, OZ1, cz1)
    m.prism(plaza, -0.6, 0.45, "Concrete", "Concrete")

    # --- the auditorium hall on the west, its far end curved
    au = BUILDINGS["Mandapam Auditorium (west drum)"]
    AX0, AZ0, AZ1 = au["x"] - au["width"] / 2, au["z"] - au["depth"] / 2, au["z"] + au["depth"] / 2
    ah = 4 * FLOOR + 1.0
    # Plan: a rectangle from the west wing out to x = AX0 + 10, then a segmental arc to the west.
    span = (AZ1 - AZ0) / 2
    R = (span ** 2 + 12.0 ** 2) / (2 * 12.0)  # circle through the corners with 12 m bulge
    ang = math.asin(span / R)
    cen = m.b(AX0 + R, au["z"])
    # In local coords the world -X direction is +x; arc around cen towards +x.
    arc = [(cen[0] + R * math.cos(t), cen[1] + R * math.sin(t)) for t in [-ang + 2 * ang * k / 16 for k in range(17)]]
    hall = dedupe([m.b(OX0 + 0.5, AZ0)] + [m.b(AX0 + 12.0, AZ0)] + arc[::-1] + [m.b(AX0 + 12.0, AZ1), m.b(OX0 + 0.5, AZ1)])
    # Keep it anticlockwise.
    area = sum(hall[k][0] * hall[(k + 1) % len(hall)][1] - hall[(k + 1) % len(hall)][0] * hall[k][1] for k in range(len(hall)))
    if area < 0:
        hall = hall[::-1]
    roof_with_parapet(m, hall, ah, parapet=1.0)
    hcx = sum(p[0] for p in hall) / len(hall)
    hcy = sum(p[1] for p in hall) / len(hall)
    for k in range(len(hall)):
        a, b_ = hall[k], hall[(k + 1) % len(hall)]
        r = Run(m, a, b_)
        mid = ((a[0] + b_[0]) / 2 - hcx, (a[1] + b_[1]) / 2 - hcy)
        if r.n[0] * mid[0] + r.n[1] * mid[1] < 0:
            r.n = (-r.n[0], -r.n[1])
        Xm = m.px - (a[0] + b_[0]) / 2
        if Xm > OX0 + 0.2:
            continue  # the edge inside the west wing
        if r.L < 6.0:
            # Curved end: tall stone piers between slit windows, glass band at the top.
            r.rect(0, r.L, -0.8, 0.6, 0.15, "Concrete")
            r.rect(0, r.L, 0.6, ah - 3.0, 0, "Stone")
            r.box(r.L / 2 - 0.5, r.L / 2 + 0.5, 0.6, ah - 3.0, 0.0, 0.5, "Stone", bottom=False)
            r.rect(0, r.L, ah - 3.0, ah - 0.4, -0.3, "Glass")
            r.hband(0, r.L, ah - 3.0, 0, -0.3, "Stone", up=True)
            r.rect(0, r.L, ah - 0.4, ah + 1.0, 0, "Stone")
            r.box(-0.02, r.L + 0.02, ah + 1.0, ah + 1.2, -0.3, 0.6, "Concrete")
        else:
            academic_facade(r, 0.0, 4, "outer", inner=False)

    # --- block 1 on the east: a curved front facing the amphitheatre
    ed = BUILDINGS["East Drum"]
    EX1 = ed["x"] + ed["width"] / 2
    EZ0, EZ1 = ed["z"] - ed["depth"] / 2, ed["z"] + ed["depth"] / 2
    eh = 3 * FLOOR
    span = (EZ1 - EZ0) / 2
    bulge = 7.0
    R = (span ** 2 + bulge ** 2) / (2 * bulge)
    ang = math.asin(span / R)
    cen = m.b(EX1 - R, ed["z"])
    # World +X is local -x; the arc bulges towards local -x.
    arc = [(cen[0] - R * math.cos(t), cen[1] + R * math.sin(t)) for t in [-ang + 2 * ang * k / 16 for k in range(17)]]
    base_x = EX1 - bulge
    blk = dedupe([m.b(OX1 - 0.5, EZ0), m.b(base_x, EZ0)] + arc[::-1] + [m.b(base_x, EZ1), m.b(OX1 - 0.5, EZ1)])
    area = sum(blk[k][0] * blk[(k + 1) % len(blk)][1] - blk[(k + 1) % len(blk)][0] * blk[k][1] for k in range(len(blk)))
    if area < 0:
        blk = blk[::-1]
    roof_with_parapet(m, blk, eh, parapet=1.0)
    bcx = sum(p[0] for p in blk) / len(blk)
    bcy = sum(p[1] for p in blk) / len(blk)
    for k in range(len(blk)):
        a, b_ = blk[k], blk[(k + 1) % len(blk)]
        r = Run(m, a, b_)
        mid = ((a[0] + b_[0]) / 2 - bcx, (a[1] + b_[1]) / 2 - bcy)
        if r.n[0] * mid[0] + r.n[1] * mid[1] < 0:
            r.n = (-r.n[0], -r.n[1])
        Xm = m.px - (a[0] + b_[0]) / 2
        if Xm < OX1 - 0.2:
            continue
        if r.L < 6.0:
            # Curved front: stone piers framing two storeys of tall windows and a glass ribbon.
            r.rect(0, r.L, -0.8, 0.6, 0.15, "Concrete")
            r.window(0, r.L, 0.6, 2 * FLOOR, 0.35, r.L - 0.35, 1.0, 2 * FLOOR - 0.4, 0.35, frame=False)
            r.box(0, 0.35, 0.6, 2 * FLOOR, 0.0, 0.4, "Stone", bottom=False)
            r.rect(0, r.L, 2 * FLOOR, 2 * FLOOR + 0.5, 0, "Stone")
            r.rect(0, r.L, 2 * FLOOR + 0.5, eh - 0.3, -0.35, "Glass")
            r.hband(0, r.L, 2 * FLOOR + 0.5, 0, -0.35, "Stone", up=True)
            r.hband(0, r.L, eh - 0.3, 0, -0.35, "Stone", up=False)
            r.rect(0, r.L, eh - 0.3, eh + 1.0, 0, "Stone")
            r.box(-0.02, r.L + 0.02, eh + 1.0, eh + 1.2, -0.3, 0.6, "Concrete")
        else:
            academic_facade(r, 0.0, 3, "outer", inner=False)
    return m


def clock_tower():
    name = "Clock Tower"
    m = Model("Clock_Tower", *centre(name))
    bd = BUILDINGS[name]
    m.replaces.append(name)
    w = bd["width"]
    H = bd["floors"] * FLOOR
    m.solid.append((bd["x"] - w / 2, bd["x"] + w / 2, bd["z"] - w / 2, bd["z"] + w / 2))
    hw = w / 2
    m.box_local(-hw - 1.4, hw + 1.4, -hw - 1.4, hw + 1.4, -0.8, 0.5, "Concrete", "Concrete")  # base
    m.box_local(-hw - 0.8, hw + 0.8, -hw - 0.8, hw + 0.8, 0.5, 1.0, "Concrete", "Concrete")
    m.box_local(-hw, hw, -hw, hw, 1.0, H - 4.5, "Brick", None)
    # Pilasters on the corners.
    for sx in (-1, 1):
        for sy in (-1, 1):
            m.box_local(sx * hw - 0.25, sx * hw + 0.25, sy * hw - 0.25, sy * hw + 0.25, 1.0, H - 4.5, "Plaster", None)
    # Clock head: wider plaster block with a dial on each side.
    ch = hw + 0.35
    z0, z1 = H - 4.5, H
    m.box_local(-ch, ch, -ch, ch, z0, z1, "Plaster", None)
    m.box_local(-ch - 0.15, ch + 0.15, -ch - 0.15, ch + 0.15, z0 - 0.25, z0, "Concrete", "Concrete", bottom=True)
    m.box_local(-ch - 0.2, ch + 0.2, -ch - 0.2, ch + 0.2, z1, z1 + 0.3, "Concrete", "Concrete", bottom=True)
    dial = 1.25
    zc = (z0 + z1) / 2
    off = ch + 0.03
    for (nx, ny) in ((0, -1), (1, 0), (0, 1), (-1, 0)):
        # Right-hand direction seen from outside.
        rx, ry = -ny, nx
        c = (nx * off, ny * off)
        a = (c[0] - rx * dial, c[1] - ry * dial)
        bpt = (c[0] + rx * dial, c[1] + ry * dial)
        m.quad([(a[0], a[1], zc - dial), (bpt[0], bpt[1], zc - dial), (bpt[0], bpt[1], zc + dial), (a[0], a[1], zc + dial)],
               "ClockFace", [(0, 0), (1, 0), (1, 1), (0, 1)])
    m.pyramid_local(-ch - 0.2, ch + 0.2, -ch - 0.2, ch + 0.2, z1 + 0.3, z1 + 3.2, "Metal")
    m.box_local(-0.06, 0.06, -0.06, 0.06, z1 + 3.0, z1 + 4.6, "Metal", "Metal")  # finial
    return m


def main_gate():
    name = "Main Gate"
    bd = BUILDINGS[name]
    m = Model("Main_Gate", bd["x"], bd["z"])
    m.replaces.append(name)
    X, Z, w, d = bd["x"], bd["z"], bd["width"], bd["depth"]
    X0, X1, Z0, Z1 = X - w / 2, X + w / 2, Z - d / 2, Z + d / 2
    m.solid.append((X0, X1, Z0, Z1))
    road_x = -118.0  # Gate Approach centre line (map x 296)
    gap = 6.0        # half opening; the road is 8 m wide
    # Guard rooms on both sides of the opening.
    m.box(X0, road_x - gap - 1.2, Z0 + 1.5, Z1 - 1.5, -0.6, FLOOR, "Facade_Hostel", "Roof", zbase=0.0)
    m.box(road_x + gap + 1.2, X1, Z0 + 1.5, Z1 - 1.5, -0.6, FLOOR, "Facade_Hostel", "Roof", zbase=0.0)
    for (a, b_) in ((X0 - 0.3, road_x - gap - 0.9), (road_x + gap + 0.9, X1 + 0.3)):
        m.box(a, b_, Z0 + 1.2, Z1 - 1.2, FLOOR, FLOOR + 0.5, "Concrete", "Concrete", bottom=True)
    # Brick pylons and the overhead beam.
    for px in (road_x - gap - 1.2, road_x + gap):
        m.box(px, px + 1.2, Z - 1.0, Z + 1.0, -0.6, 9.0, "Brick", "Concrete")
    m.box(road_x - gap - 1.6, road_x + gap + 1.6, Z - 1.2, Z + 1.2, 6.6, 8.2, "Plaster", "Concrete", bottom=True)
    m.box(road_x - gap - 1.8, road_x + gap + 1.8, Z - 1.3, Z + 1.3, 8.2, 8.5, "Concrete", "Concrete", bottom=True)
    return m


def hostel(model_name, parts):
    m = Model(model_name, *centre(parts[0]))
    for p in parts:
        m.block(p, "hostel")
    return m


def mess():
    m = Model("Annapurna_Mess", *centre("Annapurna Mess"))
    m.block("Annapurna Mess", "mess")
    m.block("Annapurna Mess - Kitchen", "hostel")
    return m


def amphitheatre_pond():
    """Open-air amphitheatre beside the academic block's east front, with the stepped
    cascade pond beside it, as on the institute's campus model. Sits on the flat
    'amphitheatre' pad from the layout."""
    ax, az = 158.0, 284.0  # stage centre: world X, Z (map 572, 712), across the ring road from block 1
    m = Model("Amphitheatre_Pond", ax, az)

    def L(X, Z):
        return m.b(X, Z)

    # Seating fan: tiers rise away from the stage, facing back towards the building.
    axis = math.radians(-5.0)  # opens east (world angle from +X towards +Z)
    half = math.radians(52.0)
    r0, tread, rise, tiers = 6.0, 1.35, 0.38, 10
    segs = 12
    angles = [axis - half + 2 * half * j / segs for j in range(segs + 1)]

    def P(r, a, z):
        x, y = L(ax + r * math.cos(a), az + r * math.sin(a))
        return (x, y, z)

    sx, sy = L(ax, az)
    for k in range(tiers):
        ri, ro = r0 + k * tread, r0 + (k + 1) * tread
        z = (k + 1) * rise
        for j in range(segs):
            a0, a1 = angles[j], angles[j + 1]
            m.face([P(ri, a0, z), P(ro, a0, z), P(ro, a1, z), P(ri, a1, z)], "Concrete", (0, 0, 1))
            inward = P(ri, (a0 + a1) / 2, 0)
            m.face([P(ri, a0, z - rise), P(ri, a1, z - rise), P(ri, a1, z), P(ri, a0, z)], "Concrete",
                   (sx - inward[0], sy - inward[1], 0))
        # Stepped side walls at both ends of the fan.
        for a, sgn in ((angles[0], -1), (angles[-1], 1)):
            na = a + sgn * math.pi / 2
            hint = P(1.0, na, 0)
            m.face([P(ri, a, -0.5), P(ro, a, -0.5), P(ro, a, z), P(ri, a, z)], "Concrete",
                   (hint[0] - sx, hint[1] - sy, 0))
    rmax = r0 + tiers * tread
    ztop = tiers * rise
    for j in range(segs):
        a0, a1 = angles[j], angles[j + 1]
        out = P(rmax + 1, (a0 + a1) / 2, 0)
        m.face([P(rmax, a0, -0.5), P(rmax, a1, -0.5), P(rmax, a1, ztop + 1.0), P(rmax, a0, ztop + 1.0)], "Stone",
               (out[0] - sx, out[1] - sy, 0))
        # A stone pier at every segment joint breaks up the back wall.
        ca, sa = math.cos(a0), math.sin(a0)
        r = Run(m, P(rmax, a0 - 0.012, 0)[:2], P(rmax, a0 + 0.012, 0)[:2], (out[0] - sx, out[1] - sy))
        L0 = math.hypot(*r.n)
        r.n = (r.n[0] / L0, r.n[1] / L0)
        r.box(0, r.L, -0.5, ztop + 1.0, 0.0, 0.35, "Stone", bottom=False)
        m.face([P(rmax - 0.01, a0, ztop + 1.0), P(rmax + 0.4, a0, ztop + 1.0), P(rmax + 0.4, a1, ztop + 1.0), P(rmax - 0.01, a1, ztop + 1.0)],
               "Concrete", (0, 0, 1))
    # Stage: a low half-round platform at the focus.
    arc = [P(r0 - 0.2, axis - math.pi / 2 + math.pi * j / 14, 0)[:2] for j in range(15)]
    m.prism(arc + [(sx, sy)], -0.4, 0.4, "Concrete", "Concrete")

    # Stepped cascade pond south of the fan: three slanting basins, each lower than the
    # last, spilling into a broad lower pool.
    def poly(pts):
        return [L(X, Z) for X, Z in pts]
    # The cascade: five long slanting basins side by side, each 30 cm lower than the one
    # before, so the water steps down towards the broad lower pool.
    d = (0.94, -0.34)          # along the basins (world X, Z)
    q = (-0.34, -0.94)         # across them, stepping south
    for k in range(5):
        cx_, cz_ = 176 + q[0] * 4.0 * k, 258 + q[1] * 4.0 * k
        hl, hw = 15.0 - 1.2 * k, 2.0
        corners = [(cx_ - d[0] * hl - q[0] * hw, cz_ - d[1] * hl - q[1] * hw),
                   (cx_ + d[0] * hl - q[0] * hw, cz_ + d[1] * hl - q[1] * hw),
                   (cx_ + d[0] * hl + q[0] * hw, cz_ + d[1] * hl + q[1] * hw),
                   (cx_ - d[0] * hl + q[0] * hw, cz_ - d[1] * hl + q[1] * hw)]
        rim = 1.8 - 0.3 * k
        m.pool(poly(corners), rim, rim - 0.15, inset=0.3)
    # The broad lower pool with a rounded outline.
    lower = [(175 + 26 * math.cos(t) * (1.0 if math.cos(t) > 0 else 0.9), 228 + 12 * math.sin(t))
             for t in [2 * math.pi * k / 24 for k in range(24)]]
    m.pool(poly(lower), 0.45, 0.3)
    m.local_bounds_to_solid()
    return m


def water_tower():
    name = "Water Tower"
    bd = BUILDINGS[name]
    m = Model("Water_Tower", bd["x"], bd["z"])
    m.replaces.append(name)
    r = bd["width"] / 2
    m.solid.append((bd["x"] - r, bd["x"] + r, bd["z"] - r, bd["z"] + r))
    m.cylinder_local(0, 0, 3.2, -2.0, 0.6, "Concrete", "Concrete", segments=16)
    m.cylinder_local(0, 0, 1.8, 0.6, 19.5, "Concrete", None, segments=16)
    # Funnel from the shaft out to the tank.
    seg = 24
    for k in range(seg):
        a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
        p = lambda rr, a, z: (rr * math.cos(a), rr * math.sin(a), z)
        mid = (a0 + a1) / 2
        m.face([p(1.8, a0, 19.5), p(1.8, a1, 19.5), p(r, a1, 22.5), p(r, a0, 22.5)], "Concrete",
               (math.cos(mid), math.sin(mid), -0.8))
    m.cylinder_local(0, 0, r, 22.5, 28.0, "Plaster", None, segments=seg)
    m.cylinder_local(0, 0, r + 0.15, 25.0, 25.4, "Concrete", None, segments=seg)  # band
    for k in range(seg):  # shallow conical roof
        a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
        m.face([(r * math.cos(a0), r * math.sin(a0), 28.0), (r * math.cos(a1), r * math.sin(a1), 28.0), (0, 0, 29.6)],
               "Roof", (0, 0, 1))
    m.box_local(-0.4, 0.4, -0.4, 0.4, 29.4, 30.6, "Metal", "Metal")  # vent
    return m


MODELS = [
    academic_block,
    clock_tower,
    main_gate,
    mess,
    amphitheatre_pond,
    water_tower,
    lambda: hostel("Brahmaputra_Hostel", ["Brahmaputra Hostel", "Brahmaputra Hostel - South"]),
    lambda: hostel("Sindhu_Hostel", ["Sindhu Hostel - North", "Sindhu Hostel - Step 1", "Sindhu Hostel - West",
                                     "Sindhu Hostel - Step 2", "Sindhu Hostel - South"]),
    lambda: hostel("Hostel_U", ["Hostel U - West", "Hostel U - North", "Hostel U - East"]),
    lambda: hostel("Hostel_Chain_1", ["Hostel Chain 1"]),
    lambda: hostel("Hostel_Chain_2", ["Hostel Chain 2"]),
    lambda: hostel("Hostel_Chain_3", ["Hostel Chain 3"]),
]


# ------------------------------------------------------------------ export and preview
def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)


def render_preview(obj, path):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.device = "CPU"
    scene.render.resolution_x, scene.render.resolution_y = 900, 560
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.70, 0.76, 0.82, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    scene.world = world
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.5
    sun_obj = bpy.data.objects.new("Sun", sun)
    sun_obj.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun_obj)
    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=400)
    bm.to_mesh(ground)
    bm.free()
    gobj = bpy.data.objects.new("Ground", ground)
    gmat = bpy.data.materials.new("GroundPreview")
    gmat.use_nodes = True
    gmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.45, 0.28, 1)
    ground.materials.append(gmat)
    scene.collection.objects.link(gobj)

    corners = [obj.matrix_world @ v.co for v in obj.data.vertices]
    xs, ys, zs = [c.x for c in corners], [c.y for c in corners], [c.z for c in corners]
    cx, cy, cz = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, (min(zs) + max(zs)) / 2
    size = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    cam = bpy.data.cameras.new("Cam")
    cam.lens = 35
    cam_obj = bpy.data.objects.new("Cam", cam)
    dist = size * 1.25 + 8
    # Camera to the world south-east; local Blender axes are (-dX, -dZ), so world +X/-Z is local (-x, +y).
    cam_obj.location = (cx - dist * 0.65, cy + dist * 0.75, cz + dist * 0.45)
    direction = (cx - cam_obj.location[0], cy - cam_obj.location[1], cz - cam_obj.location[2])
    from mathutils import Vector
    cam_obj.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    render = "--render" in sys.argv
    os.makedirs(OUT_DIR, exist_ok=True)
    if render:
        os.makedirs(PREVIEW_DIR, exist_ok=True)
    entries = []
    for make in MODELS:
        clear_scene()
        m = make()
        obj = m.to_object()
        path = os.path.join(OUT_DIR, m.name + ".fbx")
        export(obj, path)
        lo, hi = m.world_bounds()
        entries.append({
            "name": m.name,
            "fbx": "Assets/Models/Buildings/%s.fbx" % m.name,
            "pivotX": round(m.px, 3), "pivotZ": round(m.pz, 3),
            "boundsMin": [round(v, 2) for v in lo], "boundsMax": [round(v, 2) for v in hi],
            "replaces": m.replaces,
        })
        print("%-20s %5d faces  replaces %s" % (m.name, len(obj.data.polygons), ", ".join(m.replaces)))
        if render:
            render_preview(obj, os.path.join(PREVIEW_DIR, m.name + ".png"))
    with open(OUT_JSON, "w") as f:
        json.dump({"info": "Generated by tools/blender/build_buildings.py", "models": entries}, f, indent=1)
    print("Wrote", OUT_JSON)


if __name__ == "__main__":
    main()
