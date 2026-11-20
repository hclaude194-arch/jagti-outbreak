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
    "Facade_Academic": (3.6, FLOOR, (0.90, 0.84, 0.76)),
    "Facade_Hostel": (3.0, FLOOR, (0.90, 0.87, 0.80)),
    "Jali": (1.2, 1.2, (0.86, 0.83, 0.76)),
    "Facade_Ground": (4.0, FLOOR, (0.35, 0.42, 0.48)),
    "Brick": (2.0, 2.0, (0.60, 0.32, 0.23)),
    "Plaster": (4.0, 4.0, (0.90, 0.84, 0.76)),
    "Hostel_Plaster": (4.0, 4.0, (0.92, 0.89, 0.81)),
    "Solar": (1.0, 2.0, (0.10, 0.14, 0.24)),
    "Concrete": (3.0, 3.0, (0.86, 0.85, 0.82)),
    "Glass": (1.5, FLOOR, (0.25, 0.34, 0.42)),
    "Roof": (6.0, 6.0, (0.80, 0.76, 0.70)),
    "Metal": (2.0, 2.0, (0.22, 0.23, 0.24)),
    "ClockFace": (1.0, 1.0, (0.95, 0.94, 0.90)),
    "Emblem": (1.0, 1.0, (0.12, 0.30, 0.62)),
    "Water": (4.0, 4.0, (0.20, 0.58, 0.60)),
    "Stone": (3.0, 3.0, (0.87, 0.80, 0.71)),
    "Planter": (2.0, 2.0, (0.28, 0.46, 0.18)),
    # Interiors (tools/blender/build_interiors.py).
    "Floor_Tile": (1.2, 1.2, (0.84, 0.82, 0.78)),
    "Floor_Wood": (2.4, 2.4, (0.58, 0.40, 0.25)),
    "Carpet": (2.0, 2.0, (0.28, 0.31, 0.40)),
    "Wall_Paint": (3.0, FLOOR, (0.93, 0.91, 0.86)),
    "Ceiling": (1.2, 1.2, (0.94, 0.94, 0.92)),
    "Wood": (1.0, 1.0, (0.55, 0.37, 0.22)),
    "Laminate": (1.2, 1.2, (0.82, 0.73, 0.60)),
    "Fabric": (0.5, 0.5, (0.16, 0.24, 0.42)),
    "Velvet": (0.5, 0.5, (0.42, 0.07, 0.09)),
    "Leather": (0.6, 0.6, (0.36, 0.21, 0.13)),
    "Plastic": (1.0, 1.0, (0.13, 0.13, 0.14)),
    "Steel": (1.0, 1.0, (0.70, 0.71, 0.72)),
    "Whiteboard": (1.0, 1.0, (0.96, 0.96, 0.96)),
    "Screen": (1.0, 1.0, (0.04, 0.05, 0.07)),
    "Ceramic": (1.0, 1.0, (0.95, 0.95, 0.94)),
    "LightPanel": (0.6, 0.6, (1.0, 1.0, 0.96)),
    "Books": (0.6, 0.3, (0.50, 0.30, 0.25)),
    "Notice": (1.2, 0.9, (0.70, 0.55, 0.38)),
    "Foliage": (0.5, 0.5, (0.22, 0.42, 0.16)),
    "Mirror": (1.0, 1.0, (0.78, 0.82, 0.84)),
    "Door": (1.0, 2.1, (0.52, 0.34, 0.20)),
    "Glass_Clear": (1.0, 1.0, (0.70, 0.82, 0.86)),
    "Red_Paint": (1.0, 1.0, (0.72, 0.08, 0.06)),
    "Cardboard": (0.6, 0.6, (0.62, 0.48, 0.32)),
    "Bedsheet": (0.6, 0.6, (0.42, 0.52, 0.72)),
    "Linen": (0.5, 0.5, (0.92, 0.91, 0.87)),
    "Wall_Tile": (1.2, 1.2, (0.93, 0.94, 0.94)),
    # Phase 4D: shops, the medical centre, the sports complex and outdoor props.
    "Sign_Board": (1.0, 1.0, (0.10, 0.22, 0.42)),
    "Sports_Floor": (2.4, 2.4, (0.80, 0.62, 0.40)),
    "Court_Line": (1.0, 1.0, (0.96, 0.96, 0.94)),
    "Rubber": (1.0, 1.0, (0.13, 0.13, 0.14)),
    "Goods": (0.6, 0.3, (0.70, 0.45, 0.30)),
    "Produce": (0.5, 0.5, (0.75, 0.45, 0.15)),
    "Net": (0.2, 0.2, (0.92, 0.92, 0.90)),
    "Lamp_Glow": (1.0, 1.0, (1.0, 0.92, 0.75)),
    "Paint_Yellow": (1.0, 1.0, (0.92, 0.72, 0.10)),
    # Street planting and banners (owner's photos, 2026-11-19).
    "Flag_Pink": (1.0, 1.0, (0.86, 0.22, 0.58)),
    "Flag_Purple": (1.0, 1.0, (0.52, 0.30, 0.78)),
    # Feather flag sails (2026-11-20): one slot, coloured per flag in OutdoorBuilder.
    "Flag_Cloth": (1.0, 1.0, (0.88, 0.70, 0.20)),
    "Shrub_Red": (0.5, 0.5, (0.48, 0.20, 0.13)),
    "Rubble_Stone": (1.5, 1.5, (0.55, 0.52, 0.46)),
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
        self.hostel_heads = []  # (run, s) of each hostel stair core, for the stair heads on the roof

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
    def to_object(self, fixed=()):
        moved = 0
        for _ in range(4):   # a lifted face can land on another lifted one: go round again
            k = separate_coplanar(self.verts, self.faces, fixed)
            moved += k
            if k == 0:
                break
        if moved:
            print("  %s: %d coplanar faces lifted clear" % (self.name, moved))
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


# Faces that lie in one plane, face the same way and overlap flicker in Unity (z-fighting: the
# black and striped patches seen on walls, floors and roofs). Before a model becomes a mesh, one
# face of each such pair is lifted a few millimetres along its normal: the smaller one (a detail
# laid on a bigger face), or the later one when the two are the same size.
COPLANAR_TOL = 0.004
COPLANAR_LIFT = 0.008


def _clip_convex(subject, clipper):
    """Sutherland-Hodgman: the part of polygon subject inside convex polygon clipper (both CCW)."""
    out = subject
    for i in range(len(clipper)):
        a, b_ = clipper[i], clipper[(i + 1) % len(clipper)]
        inp, out = out, []
        if not inp:
            break

        def side(p):
            return (b_[0] - a[0]) * (p[1] - a[1]) - (b_[1] - a[1]) * (p[0] - a[0])

        s = inp[-1]
        for e in inp:
            se, ss = side(e), side(s)
            if se >= 0:
                if ss < 0:
                    t = ss / (ss - se)
                    out.append((s[0] + t * (e[0] - s[0]), s[1] + t * (e[1] - s[1])))
                out.append(e)
            elif ss >= 0:
                t = ss / (ss - se)
                out.append((s[0] + t * (e[0] - s[0]), s[1] + t * (e[1] - s[1])))
            s = e
    return out


def _area2(q):
    return 0.5 * sum(q[i][0] * q[(i + 1) % len(q)][1] - q[(i + 1) % len(q)][0] * q[i][1] for i in range(len(q)))


def separate_coplanar(verts, faces, fixed=()):
    """Lifts one face of every coplanar, same-facing overlapping pair (see COPLANAR_LIFT).
    verts/faces as in Model (each face has its own vertices, so a face moves on its own).
    fixed: point lists of faces of another model in this model's local frame (an interior
    shell's building); this model's faces are lifted off them. Returns the number lifted."""
    recs = []   # (normal key, d, index or -1, u-v polygon, bbox, area, normal)
    def add(pts, idx):
        nx = ny = nz = 0.0
        for k in range(len(pts)):
            p, q = pts[k], pts[(k + 1) % len(pts)]
            nx += (p[1] - q[1]) * (p[2] + q[2])
            ny += (p[2] - q[2]) * (p[0] + q[0])
            nz += (p[0] - q[0]) * (p[1] + q[1])
        L = math.sqrt(nx * nx + ny * ny + nz * nz)
        if L < 1e-6:
            return
        n = (nx / L, ny / L, nz / L)
        d = n[0] * pts[0][0] + n[1] * pts[0][1] + n[2] * pts[0][2]
        r = (0.0, 0.0, 1.0) if abs(n[2]) < 0.9 else (1.0, 0.0, 0.0)
        u = (n[1] * r[2] - n[2] * r[1], n[2] * r[0] - n[0] * r[2], n[0] * r[1] - n[1] * r[0])
        ul = math.sqrt(sum(c * c for c in u))
        u = tuple(c / ul for c in u)
        w = (n[1] * u[2] - n[2] * u[1], n[2] * u[0] - n[0] * u[2], n[0] * u[1] - n[1] * u[0])
        q = [(p[0] * u[0] + p[1] * u[1] + p[2] * u[2], p[0] * w[0] + p[1] * w[1] + p[2] * w[2]) for p in pts]
        a = _area2(q)
        if a < 0:
            q.reverse()
            a = -a
        if a < 0.005:
            return
        key = (round(n[0], 2) + 0.0, round(n[1], 2) + 0.0, round(n[2], 2) + 0.0)
        xs, ys = [p[0] for p in q], [p[1] for p in q]
        recs.append((key, d, idx, q, (min(xs), max(xs), min(ys), max(ys)), a, n))

    for i, (ids, _mat, _uvs) in enumerate(faces):
        add([verts[k] for k in ids], i)
    for pts in fixed:
        add(pts, -1)
    groups = {}
    for r in recs:
        groups.setdefault(r[0], []).append(r)
    moved = set()
    for rs in groups.values():
        if len(rs) < 2:
            continue
        rs.sort(key=lambda r: r[1])
        for a_i, A in enumerate(rs):
            for Bq in rs[a_i + 1:]:
                if Bq[1] - A[1] > COPLANAR_TOL:
                    break
                if A[2] < 0 and Bq[2] < 0:
                    continue
                if A[2] in moved or Bq[2] in moved:
                    continue
                ba, bb = A[4], Bq[4]
                if ba[0] >= bb[1] - 0.01 or bb[0] >= ba[1] - 0.01 or ba[2] >= bb[3] - 0.01 or bb[2] >= ba[3] - 0.01:
                    continue
                c = _clip_convex(A[3], Bq[3])
                if len(c) < 3 or _area2(c) < 0.01:
                    continue
                if A[2] < 0:
                    pick = Bq
                elif Bq[2] < 0:
                    pick = A
                elif abs(A[5] - Bq[5]) < 0.02 * max(A[5], Bq[5]):
                    pick = A if A[2] > Bq[2] else Bq
                else:
                    pick = A if A[5] < Bq[5] else Bq
                moved.add(pick[2])
                n = pick[6]
                for k in faces[pick[2]][0]:
                    v = verts[k]
                    verts[k] = (v[0] + n[0] * COPLANAR_LIFT, v[1] + n[1] * COPLANAR_LIFT, v[2] + n[2] * COPLANAR_LIFT)
    return len(moved)


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


# Interior wall thickness behind each facade kind (the interior shell's walls stand this far in).
WALL = {"outer": 0.45, "court": 0.3}
DOOR_TOP = 3.0   # ground-floor doorways: plinth top (0.6 m) to 3.0 m


def facade_bays(L):
    bays = max(1, round(L / BAY))
    return bays, L / bays


def facade_openings(L, kind, doors=()):
    """Ground-floor openings of academic_facade as (s0, s1, z0, z1, is_door), for the interior shell."""
    bays, bw = facade_bays(L)
    out = []
    for i in range(bays):
        s0, s1 = i * bw, (i + 1) * bw
        if i in doors:
            out.append((s0 + 0.5, s1 - 0.5, 0.6, DOOR_TOP, True))
        elif kind == "outer":
            mid = (s0 + s1) / 2
            out.append((s0 + 0.42, mid - 0.14, 0.95, FLOOR - 0.4, False))
            out.append((mid + 0.14, s1 - 0.42, 0.95, FLOOR - 0.4, False))
        else:
            out.append((s0 + 0.8, s1 - 0.8, 1.1, FLOOR - 0.4, False))
    return out


def facade_door(run, s0, s1, kind, steps=True):
    """A ground-floor doorway filling bay s0..s1: stone around the opening, reveals through the
    whole wall to the interior shell, a threshold, and steps down to the ground outside."""
    os0, os1 = s0 + 0.5, s1 - 0.5
    t = WALL[kind]
    run.rect(s0, os0, 0.6, FLOOR, 0, "Stone")
    run.rect(os1, s1, 0.6, FLOOR, 0, "Stone")
    run.rect(os0, os1, DOOR_TOP, FLOOR, 0, "Stone")
    run.side(os0, 0.6, DOOR_TOP, 0, -t, "Stone", 1)
    run.side(os1, 0.6, DOOR_TOP, 0, -t, "Stone", -1)
    run.hband(os0, os1, DOOR_TOP, 0, -t, "Stone", up=False)
    run.hband(os0, os1, 0.6, 0.18, -t, "Concrete", up=True)
    # Dark metal door frame inside the opening.
    run.box(os0, os0 + 0.08, 0.6, DOOR_TOP, -t * 0.5 - 0.04, -t * 0.5 + 0.04, "Metal")
    run.box(os1 - 0.08, os1, 0.6, DOOR_TOP, -t * 0.5 - 0.04, -t * 0.5 + 0.04, "Metal")
    run.box(os0, os1, DOOR_TOP - 0.08, DOOR_TOP, -t * 0.5 - 0.04, -t * 0.5 + 0.04, "Metal", top=False)
    if steps:
        for k in range(3):
            run.box(os0 - 0.6, os1 + 0.6, -0.8, 0.45 - 0.15 * k, 0.18, 0.18 + 0.35 * (k + 1), "Concrete", bottom=False)


def open_spans(bays, bw, skip):
    """The stretches of a run (s0, s1) not covered by skipped bays."""
    out, s = [], None
    for i in range(bays):
        if i in skip:
            if s is not None:
                out.append((s, i * bw))
                s = None
        elif s is None:
            s = i * bw
    if s is not None:
        out.append((s, bays * bw))
    return out


def hall_bays(A, B_):
    """Bays of an academic outer run (world X/Z endpoints) that lie inside the Mandapam
    auditorium hall, which is built against the west wing: the hall's interior opens there."""
    nw = BUILDINGS["Academic Block - North Wing"]
    ox0 = nw["x"] - nw["width"] / 2
    if abs(A[0] - ox0) > 1e-6 or abs(B_[0] - ox0) > 1e-6:
        return []
    au = BUILDINGS["Mandapam Auditorium (west drum)"]
    z0, z1 = au["z"] - au["depth"] / 2, au["z"] + au["depth"] / 2
    L = math.dist(A, B_)
    bays, bw = facade_bays(L)
    sgn = 1 if B_[1] > A[1] else -1
    return [i for i in range(bays) if z0 < A[1] + sgn * (i + 0.5) * bw < z1]


def court_galleries(run, floors, depth=2.2):
    """The academic courtyard's deep open galleries (owner's photo from the third floor, 2026-11-18):
    a projecting slab with a solid cream parapet at every upper floor, deep fins every two or
    three bays and a frame at roof level, all standing in front of the windowed wall."""
    L = run.L
    bays, bw = facade_bays(L)
    top = floors * FLOOR
    s0 = depth        # the corner square belongs to the run before (the runs go round in order)
    for f in range(1, floors):
        zf = f * FLOOR
        run.box(s0, L, zf - 0.25, zf + 0.05, 0.0, depth, "Concrete")
        run.box(s0, L, zf + 0.05, zf + 1.05, depth - 0.2, depth, "Plaster")
    run.box(s0, L, top - 0.35, top + 0.45, 0.0, depth, "Plaster")
    step = 3 if bays % 3 == 0 else 2
    for i in range(0, bays + 1, step):
        s_ = min(i * bw, L - 0.2)
        if s_ - 0.2 < s0:
            continue
        run.box(s_ - 0.2, s_ + 0.2, 0.0, top + 0.45, 0.0, depth + 0.08, "Plaster")


def academic_facade(run, z0, floors, kind="outer", ribbon=True, pilasters=True, inner=True, doors=(), steps=True, skip=(), shops=None,
                    arcade=False):
    """Sandstone facade from the campus model: tall recessed windows between pilasters,
    a blue glass ribbon on the top floor, string courses and a parapet with coping.
    Bays listed in doors get a ground-floor doorway instead of windows; bays listed in skip
    are left open (where another part of the building covers the facade, and the interior
    shell supplies the wall). shops maps bay index -> is_door for ground-floor shop fronts.
    arcade: the shop fronts open onto a raised colonnade (vipanan()), so they get no canopy and
    no steps of their own."""
    L = run.L
    shops = shops or {}
    bays, bw = facade_bays(L)
    top = z0 + floors * FLOOR
    lower = floors - 1 if ribbon else floors
    depth = 0.35 if kind == "outer" else 0.22
    # Plinth.
    run.box(0, L, -0.8, z0 + 0.6, 0.0, 0.18, "Concrete", bottom=False)
    for i in range(bays):
        if i in skip:
            continue
        s0, s1 = i * bw, (i + 1) * bw
        for f in range(lower):
            zf = z0 + f * FLOOR
            zb = z0 + 0.6 if f == 0 else zf
            if f == 0 and i in doors:
                facade_door(run, s0, s1, kind, steps)
                continue
            if f == 0 and i in shops:
                shop_front(run, s0, s1, shops[i], arcade)
                continue
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
            if i in skip and i - 1 in skip:
                continue
            s = i * bw
            run.box(max(0, s - 0.28), min(L, s + 0.28), z0 + 0.6, zp, 0.0, 0.32, "Stone", bottom=False)
    if ribbon:
        zc = z0 + lower * FLOOR
        for c0, c1 in open_spans(bays, bw, skip):
            run.box(c0, c1, zc - 0.1, zc + 0.15, 0.0, 0.42, "Concrete")
    if shops:
        shop_canopy(run, bw, shops, arcade)
    # Parapet with coping.
    run.rect(0, L, top, top + 1.1, 0, "Stone")
    if inner:
        run.rect(0, L, top, top + 1.1, -0.3, "Stone", facing=-1)
    run.box(-0.05, L + 0.05, top + 1.1, top + 1.3, -0.35, 0.12, "Concrete")


def fluted_lower(run, z0, fin_top, windows=True):
    """Plinth, stone fins and the row of square windows of a pavilion face."""
    L = run.L
    run.box(0, L, -0.8, z0 + 0.6, 0.0, 0.2, "Concrete", bottom=False)
    run.rect(0, L, z0 + 0.6, fin_top, 0, "Stone")
    n = max(2, int(L / 1.15))
    for i in range(n + 1):
        s = L * i / n
        run.box(max(0, s - 0.2), min(L, s + 0.2), z0 + 0.6, fin_top, 0.0, 0.45, "Stone", bottom=False)
    if not windows:
        run.rect(0, L, fin_top, fin_top + FLOOR, 0, "Stone")
        return
    # A row of square windows.
    bays = max(1, round(L / 3.0))
    bw = L / bays
    for i in range(bays):
        s0 = i * bw
        run.window(s0, s0 + bw, fin_top, fin_top + FLOOR, s0 + 0.7, s0 + bw - 0.7, fin_top + 0.9, fin_top + FLOOR - 0.6, 0.3)


def fluted_facade(run, z0, floors, glass_top=True, low=True, windows=True):
    """The corner pavilions: closely spaced stone fins on the lower floors, a band of
    windows, and a glazed top floor under a deep cornice. low=False draws only the glazed
    top floor and cornice (for faces that are inside the wings below that); windows=False
    leaves out the window row (for narrow slivers)."""
    L = run.L
    top = z0 + floors * FLOOR
    fin_top = z0 + (floors - 2) * FLOOR
    if low:
        fluted_lower(run, z0, fin_top, windows)
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


def rooftop_box(m, x, y, w, d, top, h=3.2, mat="Stone"):
    m.box_local(x - w / 2, x + w / 2, y - d / 2, y + d / 2, top, top + h, mat, "Roof")
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


# Doorways in the outer facade (world X, Z on the facade line): into the entrance lobby from the
# glass bay under the porch, and from the east wing into "block 1" on the east front.
OUTER_DOORS = [(73.5, 350.0), (129.0, 283.0)]
BAY_DOOR_SEGMENTS = (6, 7)   # of the 14 panes of the curved glass bay, the two facing the porch
EAST_DOOR_Z = 283.0          # the doors in block 1's curved front straddle this line


def east_door_side(blk, k, m):
    """For block 1's curved front: 1 if edge k is the pane just before the door axis, 2 if just after, else 0.
    The two door panes share the arc vertex nearest the axis (EAST_DOOR_Z)."""
    n = len(blk)

    def on_arc(i):  # both edges at vertex i are short arc panes
        return math.dist(blk[i], blk[(i + 1) % n]) < 6.0 and math.dist(blk[i - 1], blk[i]) < 6.0

    best = min((i for i in range(n) if on_arc(i)), key=lambda i: abs(m.pz - blk[i][1] - EAST_DOOR_Z))
    if (k + 1) % n == best:
        return 1
    if k == best:
        return 2
    return 0


def run_has(run, X, Z):
    """True if world point (X, Z) lies on this run's line, within its length."""
    p = run.m.b(X, Z)
    dx, dy = p[0] - run.a[0], p[1] - run.a[1]
    s = dx * run.t[0] + dy * run.t[1]
    off = abs(dx * run.n[0] + dy * run.n[1])
    return off < 0.6 and 0 <= s <= run.L


def bay_at(run, X, Z):
    p = run.m.b(X, Z)
    s = (p[0] - run.a[0]) * run.t[0] + (p[1] - run.a[1]) * run.t[1]
    bays, bw = facade_bays(run.L)
    return min(bays - 1, max(0, int(s / bw)))


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
        doors = [bay_at(r, *d) for d in OUTER_DOORS if run_has(r, *d)]
        academic_facade(r, 0.0, floors, "outer", doors=doors, steps=False, skip=hall_bays(A, Bp))
    court = [((IX0, IZ0), (IX1, IZ0)), ((IX1, IZ0), (IX1, IZ1)), ((IX1, IZ1), (IX0, IZ1)), ((IX0, IZ1), (IX0, IZ0))]
    cc = m.b((IX0 + IX1) / 2, (IZ0 + IZ1) / 2)
    for (A, Bp) in court:
        r = Run(m, m.b(*A), m.b(*Bp))
        mid = m.b((A[0] + Bp[0]) / 2, (A[1] + Bp[1]) / 2)
        if r.n[0] * (cc[0] - mid[0]) + r.n[1] * (cc[1] - mid[1]) < 0:
            r.n = (-r.n[0], -r.n[1])   # courtyard walls face into the courtyard
        # A doorway out to the garden in the middle of each courtyard side, on the path axes.
        academic_facade(r, 0.0, floors, "court", ribbon=False, pilasters=False, doors=[facade_bays(r.L)[0] // 2])
        court_galleries(r, floors)
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
        # The two inner faces run inside the wings below the wing roof: there only the
        # proud strip gets the full facade, the rest just the glazed top floor.
        inner_x = X0 + P if sx == OX0 else X0
        inner_z = Z0 + P if sz == OZ0 else Z0
        for run in outline_runs(m, poly):
            wa = (m.px - run.a[0], m.pz - run.a[1])
            wb = (m.px - run.b[0], m.pz - run.b[1])
            if abs(wa[0] - inner_x) < 0.01 and abs(wb[0] - inner_x) < 0.01:
                cut, axis = sz, 1
            elif abs(wa[1] - inner_z) < 0.01 and abs(wb[1] - inner_z) < 0.01:
                cut, axis = sx, 0
            else:
                fluted_facade(run, 0.0, PH_FLOORS)
                continue
            fluted_facade(run, 0.0, PH_FLOORS, low=False)
            s_cut = abs(cut - wa[axis])
            lo, hi = (OZ0, OZ1) if axis == 1 else (OX0, OX1)
            proud = (0.0, s_cut) if (wa[axis] < lo or wa[axis] > hi) else (s_cut, run.L)
            pa, pb = run.P(proud[0], 0, 0), run.P(proud[1], 0, 0)
            sub = Run(m, (pa[0], pa[1]), (pb[0], pb[1]), run.n)
            fluted_lower(sub, 0.0, (PH_FLOORS - 2) * FLOOR, windows=False)
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
        door = k in BAY_DOOR_SEGMENTS
        if door:
            # Glass entrance doors in the two middle panes; glass continues above them.
            r.rect(0, r.L, DOOR_TOP, 2 * FLOOR - 0.3, -0.05, "Glass")
            r.box(0, r.L, DOOR_TOP - 0.1, DOOR_TOP, -0.1, 0.15, "Metal")
            r.hband(0, r.L, 0.6, 0.3, -0.3, "Concrete", up=True)
        else:
            r.rect(0, r.L, 0.45, 0.9, 0.0, "Stone")
            r.rect(0, r.L, 0.9, 2 * FLOOR - 0.3, -0.05, "Glass")
        r.rect(0, r.L, 2 * FLOOR - 0.3, 3 * FLOOR + 0.6, 0.0, "Stone")
        if k != BAY_DOOR_SEGMENTS[1]:
            r.box(0, 0.25, 0.9 if not door else 0.6, 2 * FLOOR - 0.3, -0.05, 0.25, "Metal")
        r.box(0, r.L, FLOOR - 0.12, FLOOR + 0.12, -0.05, 0.3, "Concrete")
        r.box(0, r.L, 2 * FLOOR + 0.6, 2 * FLOOR + 2.3, 0.0, 0.05, "Glass", top=False, bottom=False)
    m.face([(bc[0], bc[1], 3 * FLOOR + 0.6)] + [(p[0], p[1], 3 * FLOOR + 0.6) for p in pts], "Roof", (0, 0, 1))
    m.cylinder_local(bc[0], bc[1], bay_r + 0.4, 3 * FLOOR + 0.6, 3 * FLOOR + 1.0, "Concrete", None, segments=28)
    # Porte-cochere: a deep square slab on stone columns, as on the model's top view, with
    # the curved glass bay tucked under its back edge.
    cw = 11.0
    cz0, cz1 = OZ1 + 1.0, OZ1 + 19.0
    zs0, zs1 = 7.0, 8.0
    m.box(ex - cw, ex + cw, cz0, cz1, zs0, zs1, "Stone", "Concrete", bottom=True)
    m.box(ex - cw - 0.3, ex + cw + 0.3, cz0, cz1 + 0.3, zs1, zs1 + 0.6, "Concrete", "Roof", bottom=True)
    # Coffers under the slab: a shallow beam grid.
    for k in range(1, 4):
        xb = ex - cw + 2 * cw * k / 4
        m.box(xb - 0.25, xb + 0.25, cz0, cz1, zs0 - 0.5, zs0, "Concrete", None, bottom=True)
    for k in range(1, 3):
        zb = cz0 + (cz1 - cz0) * k / 3
        m.box(ex - cw, ex + cw, zb - 0.25, zb + 0.25, zs0 - 0.5, zs0, "Concrete", None, bottom=True)
    for X in (ex - cw + 0.9, ex + cw - 0.9):
        for Z in (cz0 + (cz1 - cz0) / 2, cz1 - 0.9):
            m.box(X - 0.6, X + 0.6, Z - 0.6, Z + 0.6, -0.3, zs0, "Stone", None)
            m.box(X - 0.8, X + 0.8, Z - 0.8, Z + 0.8, -0.3, 0.9, "Concrete", "Concrete")
    # Entrance plaza under the porch, with steps down to the drive at its front.
    plaza = rect_poly(m, ex - cw - 2, ex + cw + 2, OZ1, cz1 + 1.0)
    m.prism(plaza, -0.6, 0.45, "Concrete", "Concrete")
    for k in range(3):
        z = 0.30 - k * 0.15
        m.box(ex - 8, ex + 8, cz1 + 1.0 + 0.45 * k, cz1 + 1.45 + 0.45 * k, -0.6, z, "Concrete", "Concrete")
    # Red flower beds with rounded ends on both sides of the porch.
    for side in (-1, 1):
        bx = ex + side * (cw + 6.5)
        bed = [m.b(bx + 3.0 * math.cos(t), OZ1 + 15.0 + 3.0 * math.sin(t)) for t in [math.pi * k / 8 for k in range(9)]]
        bed += [m.b(bx + 3.0 * math.cos(t), OZ1 + 5.0 + 3.0 * math.sin(t)) for t in [math.pi + math.pi * k / 8 for k in range(9)]]
        bed = anticlockwise(dedupe(bed))
        m.prism(bed, -0.3, 0.35, "Concrete", "Concrete")
        m.prism(inset_polygon(bed, 0.25), 0.35, 0.6, "Planter", "Planter")
    # Square frame round the fountain (the water itself is drawn by the scene builder), with
    # a quarter-round planter in each corner.
    fx, fz = ex - 2.0, OZ1 + 27.0
    hf = 5.5
    for (X0, X1, Z0, Z1) in ((fx - hf, fx + hf, fz - hf, fz - hf + 0.5), (fx - hf, fx + hf, fz + hf - 0.5, fz + hf),
                             (fx - hf, fx - hf + 0.5, fz - hf, fz + hf), (fx + hf - 0.5, fx + hf, fz - hf, fz + hf)):
        m.box(X0, X1, Z0, Z1, -0.3, 0.45, "Stone", "Concrete")
    for (cxs, czs) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        c0 = (fx + cxs * (hf - 0.5), fz + czs * (hf - 0.5))
        a0 = math.atan2(-czs, -cxs)
        q = [m.b(*c0)] + [m.b(c0[0] + 1.8 * math.cos(a0 + (k / 6 - 0.5) * math.pi / 2), c0[1] + 1.8 * math.sin(a0 + (k / 6 - 0.5) * math.pi / 2)) for k in range(7)]
        m.prism(anticlockwise(q), -0.2, 0.55, "Concrete", "Planter")

    # --- courtyard garden (owner's photo): lawns quartered by wide paved paths with a loop round
    # the middle, clipped hedges along the path edges and a small rectangular pool at the centre
    # (the paths are drawn by the scene; lamps and palms are outdoor props).
    gx, gz = (IX0 + IX1) / 2, (IZ0 + IZ1) / 2
    m.box(gx - 2.4, gx + 2.4, gz - 5.0, gz + 5.0, -0.3, 0.5, "Stone", "Concrete")
    m.box(gx - 2.05, gx + 2.05, gz - 4.65, gz + 4.65, -0.3, 0.38, "Water", "Water")
    HW = 0.35   # half the hedge width

    def hedge(X0, X1, Z0, Z1):
        if X1 - X0 > 0.3 and Z1 - Z0 > 0.3:
            m.box(X0, X1, Z0, Z1, -0.2, 0.65, "Foliage", "Foliage")

    def wx(mx):
        return mx - 414.0

    def wz(my):
        return 996.0 - my
    # The four beds inside the loop (map x 476.5-487, 491-501.5; y 695.5-713, 717-734.5), hedged
    # all round, with the pool's paving cut out of the inner corners.
    for (mx0, mx1) in ((476.5, 487.0), (491.0, 501.5)):
        for (my0, my1) in ((695.5, 713.0), (717.0, 734.5)):
            X0, X1, Z0, Z1 = wx(mx0), wx(mx1), wz(my1), wz(my0)
            hedge(X0, X1, Z0, Z0 + 2 * HW)
            hedge(X0, X1, Z1 - 2 * HW, Z1)
            hedge(X0, X0 + 2 * HW, Z0 + 2 * HW, Z1 - 2 * HW)    # the ends stop at the long hedges
            hedge(X1 - 2 * HW, X1, Z0 + 2 * HW, Z1 - 2 * HW)
    # The outer lawns: a hedge along the outside of the loop, open where the cross paths pass.
    lx0, lx1, lz0, lz1 = wx(473.5), wx(504.5), wz(737.5), wz(692.5)
    for (a0, a1) in ((lx0, wx(487.0)), (wx(491.0), lx1)):
        hedge(a0, a1, lz0 - 2 * HW, lz0)
        hedge(a0, a1, lz1, lz1 + 2 * HW)
    for (b0, b1) in ((lz0, wz(717.0)), (wz(713.0), lz1)):
        hedge(lx0 - 2 * HW, lx0, b0, b1)
        hedge(lx1, lx1 + 2 * HW, b0, b1)

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
            dk = east_door_side(blk, k, m)
            if dk:
                # Entrance doors in the two panes either side of the axis, glass above them.
                os0, os1 = (0.35, r.L) if dk == 1 else (0.0, r.L - 0.35)
                r.rect(0, os0, 0.6, DOOR_TOP, 0, "Stone")
                r.rect(os1, r.L, 0.6, DOOR_TOP, 0, "Stone")
                r.side(os0, 0.6, DOOR_TOP, 0, -WALL["outer"], "Stone", 1)
                r.side(os1, 0.6, DOOR_TOP, 0, -WALL["outer"], "Stone", -1)
                r.hband(os0, os1, DOOR_TOP, 0, -WALL["outer"], "Stone", up=False)
                r.hband(os0, os1, 0.6, 0.15, -WALL["outer"], "Concrete", up=True)
                r.window(0, r.L, DOOR_TOP, 2 * FLOOR, 0.35, r.L - 0.35, DOOR_TOP + 0.1, 2 * FLOOR - 0.4, 0.35, frame=False)
                for st in range(3):
                    r.box(os0 - (0.3 if dk == 1 else 0), os1 + (0.3 if dk == 2 else 0), -0.8, 0.45 - 0.15 * st,
                          0.15, 0.15 + 0.35 * (st + 1), "Concrete", bottom=False)
            else:
                r.window(0, r.L, 0.6, 2 * FLOOR, 0.35, r.L - 0.35, 1.0, 2 * FLOOR - 0.4, 0.35, frame=False)
            if dk != 2:
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
    """Slender white tower as on the campus model: a tall shaft with a recessed glazed channel
    up each face, a top stage with a blue emblem panel facing along the walkway and clock
    dials on the sides, standing on an elongated hexagonal plinth with steps."""
    name = "Clock Tower"
    m = Model("Clock_Tower", *centre(name))
    bd = BUILDINGS[name]
    m.replaces.append(name)
    w = bd["width"]
    H = bd["floors"] * FLOOR
    # Plinth: an elongated hexagon along the walkway (world Z), three steps high, with a skirt for the slope.
    for k, (hw, hl, z) in enumerate(((3.6, 6.0, 0.15), (3.2, 5.4, 0.30), (2.8, 4.8, 0.45))):
        hexa = [(0, -hl), (hw, -hl + hw * 0.6), (hw, hl - hw * 0.6), (0, hl), (-hw, hl - hw * 0.6), (-hw, -hl + hw * 0.6)]
        m.prism(hexa, -3.0 if k == 0 else 0.0, z, "Concrete", "Concrete")
    hw = w / 2
    m.box_local(-hw - 0.3, hw + 0.3, -hw - 0.3, hw + 0.3, 0.45, 1.6, "Plaster", "Concrete")      # base course
    shaft_top = H - 4.0
    for (nx, ny) in ((0, -1), (1, 0), (0, 1), (-1, 0)):
        tx, ty = -ny, nx
        a = (nx * hw - tx * hw, ny * hw - ty * hw)
        b_ = (nx * hw + tx * hw, ny * hw + ty * hw)
        r = Run(m, a, b_, (nx, ny))
        L = r.L
        # Corner piers and a deep vertical channel with a glass slit.
        r.rect(0, 0.9, 1.6, shaft_top, 0, "Plaster")
        r.rect(L - 0.9, L, 1.6, shaft_top, 0, "Plaster")
        r.rect(0.9, L - 0.9, 1.6, 2.6, 0, "Plaster")
        r.side(0.9, 2.6, shaft_top, 0, -0.45, "Plaster", 1)
        r.side(L - 0.9, 2.6, shaft_top, 0, -0.45, "Plaster", -1)
        r.hband(0.9, L - 0.9, 2.6, 0, -0.45, "Plaster", up=True)
        r.rect(0.9, L - 0.9, 2.6, shaft_top, -0.45, "Glass")
        for z in range(6, int(shaft_top) - 1, 4):
            r.box(0.9, L - 0.9, z, z + 0.25, -0.45, 0.0, "Plaster")
        # Clock stage.
        r.rect(-0.25, L + 0.25, shaft_top, H, 0.25, "Plaster")
        r.box(-0.4, L + 0.4, shaft_top - 0.3, shaft_top, 0.0, 0.5, "Concrete")
        r.box(-0.45, L + 0.45, H, H + 0.5, 0.0, 0.6, "Concrete")
        c = r.P(L / 2, 0.27, 0)
        # Blue emblem panel on the faces along the walkway, clock dials on the other two.
        emblem = ny != 0
        dial = 1.2 if emblem else 1.0
        zc = shaft_top + 2.0
        m.face([(c[0] - tx * dial, c[1] - ty * dial, zc - dial), (c[0] + tx * dial, c[1] + ty * dial, zc - dial),
                (c[0] + tx * dial, c[1] + ty * dial, zc + dial), (c[0] - tx * dial, c[1] - ty * dial, zc + dial)],
               "Emblem" if emblem else "ClockFace", (nx, ny, 0))
    # Side walls of the clock stage at its overhang, and the roof.
    m.box_local(-hw - 0.25, hw + 0.25, -hw - 0.25, hw + 0.25, H + 0.5, H + 0.5, "Concrete", "Roof")
    m.box_local(-0.5, 0.5, -0.5, 0.5, H + 0.5, H + 2.5, "Metal", "Metal")  # mast
    # The tower stands on the walk's paved landing, which is STEP_PAVING above the ground there.
    m.verts = [(x, y, z + STEP_PAVING) for x, y, z in m.verts]
    m.solid = []
    m.local_bounds_to_solid()
    return m


# The clock tower walk's steps and landings stand this far above the ground generate_terrain.py
# cut for them (it lowers the ground under the walk by the same amount).
STEP_PAVING = 0.12
TERRAIN_JSON = os.path.join(ROOT, "Assets", "Data", "campus_terrain.json")


def clock_steps():
    """The stepped walk from the serpentine up to the fountain square in front of the academic
    porch (campus model and satellite), with the clock tower on a wide level landing near its
    foot. Flights and landings come from generate_terrain.py ("clockSteps" in
    campus_terrain.json), which fits them to the heights of the drive below and the porch above
    and cuts the ground under them. Each tread and landing is a solid stone block; low side walls
    with a concrete coping run along both edges and round the tower landing."""
    with open(TERRAIN_JSON) as f:
        cs = json.load(f)["clockSteps"]
    X = cs["x"]
    half = cs["width"] / 2
    m = Model("Clock_Tower_Steps", *centre("Clock Tower"))
    base = cs["towerHeight"] - STEP_PAVING          # the ground at the pivot, on the tower landing
    prof = cs["profile"]
    land1, land0 = cs["landing"]
    wall = 0.3
    # Treads and landings: each level stretch of the profile becomes a block down into the ground.
    for (za, ha), (zb, hb) in zip(prof[:-1], prof[1:]):
        if abs(ha - hb) > 1e-6 or abs(za - zb) < 1e-4:
            continue
        z0, z1 = min(za, zb), max(za, zb)
        top = ha - base
        hw = 6.0 if land1 - 0.01 < (z0 + z1) / 2 < land0 + 0.01 else half
        m.box(X - hw, X + hw, z0, z1, top - 0.75, top, "Stone", "Stone", bottom=False)
    # Side walls: along each edge, a wall from below the steps to 0.6 m over the nosing line,
    # stepping with the flights (one segment per profile corner pair, sloped along the flights).
    noses = [(z, h) for z, h in prof]
    for side in (-1, 1):
        for (za, ha), (zb, hb) in zip(noses[:-1], noses[1:]):
            if abs(za - zb) < 1e-4:
                continue
            mid = (za + zb) / 2
            on_landing = land1 - 0.01 < mid < land0 + 0.01
            hw = 6.0 if on_landing else half
            x0, x1 = (X + side * hw, X + side * (hw + wall))
            xa, xb = min(x0, x1), max(x0, x1)
            lo = min(ha, hb) - base - 1.2
            hi_a, hi_b = ha - base + 0.6, hb - base + 0.6
            # A sloped-top wall segment: two side faces, a top and both ends.
            for xx, hint in ((xa, -1.0), (xb, 1.0)):
                lx, _ = m.b(xx, 0.0)
                _, ya = m.b(0.0, za)
                _, yb = m.b(0.0, zb)
                m.face([(lx, ya, lo), (lx, yb, lo), (lx, yb, hi_b), (lx, ya, hi_a)], "Stone", (-hint, 0, 0))
            lxa, _ = m.b(xa, 0.0)
            lxb, _ = m.b(xb, 0.0)
            _, ya = m.b(0.0, za)
            _, yb = m.b(0.0, zb)
            m.face([(lxa, ya, hi_a), (lxb, ya, hi_a), (lxb, yb, hi_b), (lxa, yb, hi_b)], "Concrete", (0, 0, 1))
    # Short return walls joining the narrow walls to the wide landing walls.
    for zz in (land0, land1):
        h_ = next(h for z, h in prof if abs(z - zz) < 0.01) - base
        for side in (-1, 1):
            xa, xb = sorted((X + side * (half + wall), X + side * (6.0 + wall)))
            m.box(xa, xb, zz - wall / 2, zz + wall / 2, h_ - 1.2, h_ + 0.6, "Stone", "Concrete")
    m.local_bounds_to_solid()
    return m


def main_gate():
    """The main gate from the campus model (view from the highway): one broad flat canopy with
    a coffered top spanning the drive, carried by a guard room on the west and slim piers on
    the east; low boundary walls either side; a sculpted curved wall rising round the small
    roundabout just inside; and a triangular planter island on the apron in front."""
    name = "Main Gate"
    bd = BUILDINGS[name]
    m = Model("Main_Gate", bd["x"], bd["z"])
    m.replaces.append(name)
    Z = bd["z"]
    road_x = -118.0  # Gate Approach centre line (map x 296)
    Z0, Z1 = Z - 4.5, Z + 4.5
    # Canopy: 24 m across the drive, 11 m deep, 1.1 m thick, coffered top in 4 x 2 panels.
    cx0, cx1 = road_x - 13.0, road_x + 11.0
    cz0, cz1 = Z0 - 1.0, Z1 + 1.0
    m.box(cx0, cx1, cz0, cz1, 6.0, 7.1, "Stone", "Concrete", bottom=True)
    for k in range(5):
        xb = cx0 + (cx1 - cx0) * k / 4
        m.box(max(cx0, xb - 0.35), min(cx1, xb + 0.35), cz0 + 0.7, cz1 - 0.7, 7.1, 7.6, "Concrete", "Concrete")
    for zb in (cz0 + 0.35, (cz0 + cz1) / 2, cz1 - 0.35):
        m.box(cx0, cx1, zb - 0.35, zb + 0.35, 7.1, 7.5, "Concrete", "Concrete")
    # Soffit grid under the slab.
    for k in range(1, 8):
        xb = cx0 + (cx1 - cx0) * k / 8
        m.box(xb - 0.15, xb + 0.15, cz0 + 0.5, cz1 - 0.5, 5.75, 6.0, "Concrete", None, bottom=True)
    # Guard room under the west end of the canopy, with a band of windows and a door.
    gx0, gx1 = cx0 + 0.8, road_x - 5.0
    poly = rect_poly(m, gx0, gx1, Z0 + 0.8, Z1 - 0.8)
    m.face([(p[0], p[1], 3.6) for p in poly], "Roof", (0, 0, 1))
    for run in outline_runs(m, poly):
        run.box(0, run.L, -0.8, 0.45, 0.0, 0.15, "Concrete", bottom=False)
        run.window(0, run.L, 0.45, 3.0, 0.9, run.L - 0.9, 1.0, 2.5, 0.2, wall="Stone")
        run.rect(0, run.L, 3.0, 3.4, 0.0, "Stone")
        run.box(-0.05, run.L + 0.05, 3.4, 3.6, 0.0, 0.25, "Concrete")
    # A solid stone pier rises from the guard room's outer end to the canopy.
    m.box(cx0, cx0 + 1.6, Z0 - 0.2, Z1 + 0.2, -0.6, 6.0, "Stone", None)
    # Slim paired piers on the east side of the drive.
    for pz in (cz0 + 1.0, cz1 - 1.8):
        m.box(cx1 - 2.2, cx1 - 1.4, pz, pz + 0.8, -0.6, 6.0, "Stone", None)
        m.box(cx1 - 2.4, cx1 - 1.2, pz - 0.2, pz + 1.0, -0.6, 0.6, "Concrete", "Concrete")
    # Boom barrier booth line: a kerbed island between the lanes.
    m.box(road_x - 0.6, road_x + 0.6, Z0, Z1, -0.3, 0.25, "Concrete", "Concrete")
    # Low boundary walls running out either side, stone with a pale coping.
    for (x0_, x1_) in ((cx0 - 22.0, cx0), (cx1, cx1 + 22.0)):
        m.box(x0_, x1_, Z - 0.25, Z + 0.25, -0.8, 1.3, "Stone", "Concrete")
        m.box(x0_, x1_, Z - 0.35, Z + 0.35, 1.3, 1.45, "Concrete", "Concrete")
    # Curved wall round the north-east of the little roundabout inside the gate, its top
    # rising from 1.2 m by the gate to 4.2 m at its far end.
    rcx, rcz, rr = road_x, 509.0, 15.0
    a0_, a1_ = math.radians(72.0), math.radians(20.0)
    n = 18
    pts = [(rcx + rr * math.cos(a0_ + (a1_ - a0_) * k / n), rcz + rr * math.sin(a0_ + (a1_ - a0_) * k / n)) for k in range(n + 1)]
    for k in range(n):
        (X0_, Z0_), (X1_, Z1_) = pts[k], pts[k + 1]
        h0 = 1.2 + 3.0 * (0.5 - 0.5 * math.cos(math.pi * k / n))
        h1 = 1.2 + 3.0 * (0.5 - 0.5 * math.cos(math.pi * (k + 1) / n))
        oa = (math.cos(a0_ + (a1_ - a0_) * (k + 0.5) / n), math.sin(a0_ + (a1_ - a0_) * (k + 0.5) / n))
        for t_, sgn in ((0.0, 1), (-0.4, -1)):
            pa = m.b(X0_ + oa[0] * t_, Z0_ + oa[1] * t_)
            pb = m.b(X1_ + oa[0] * t_, Z1_ + oa[1] * t_)
            hint = (-oa[0] * sgn, -oa[1] * sgn, 0)
            m.face([(pa[0], pa[1], -0.6), (pb[0], pb[1], -0.6), (pb[0], pb[1], h1), (pa[0], pa[1], h0)], "Stone", hint)
        qa, qb = m.b(X0_, Z0_), m.b(X1_, Z1_)
        ia, ib = m.b(X0_ - oa[0] * 0.4, Z0_ - oa[1] * 0.4), m.b(X1_ - oa[0] * 0.4, Z1_ - oa[1] * 0.4)
        m.face([(qa[0], qa[1], h0), (qb[0], qb[1], h1), (ib[0], ib[1], h1), (ia[0], ia[1], h0)], "Concrete", (0, 0, 1))
    # End caps of the curved wall.
    for k, (X_, Z_) in ((0, pts[0]), (n, pts[-1])):
        ang = a0_ + (a1_ - a0_) * k / n
        oa = (math.cos(ang), math.sin(ang))
        h = 1.2 + 3.0 * (0.5 - 0.5 * math.cos(math.pi * k / n))
        pa, pb = m.b(X_, Z_), m.b(X_ - oa[0] * 0.4, Z_ - oa[1] * 0.4)
        tang = (-oa[1], oa[0]) if k == 0 else (oa[1], -oa[0])
        m.face([(pa[0], pa[1], -0.6), (pb[0], pb[1], -0.6), (pb[0], pb[1], h), (pa[0], pa[1], h)], "Stone", (-tang[0], -tang[1], 0))
    # Flower bed in the middle of the roundabout.
    m.cylinder_local(*m.b(rcx, rcz), 3.6, -0.3, 0.35, "Concrete", "Concrete", segments=20)
    m.cylinder_local(*m.b(rcx, rcz), 3.3, 0.35, 0.6, "Planter", "Planter", segments=20)
    # Triangular planter island on the apron in front, pointing at the gate.
    tri = anticlockwise([m.b(road_x - 7.0, Z1 + 11.0), m.b(road_x + 7.0, Z1 + 11.0), m.b(road_x, Z1 + 3.5)])
    m.prism(tri, -0.4, 0.35, "Concrete", "Concrete")
    m.prism(inset_polygon(tri, 0.35), 0.35, 0.6, "Planter", "Planter")
    m.local_bounds_to_solid()  # canopy, walls and island reach beyond the layout footprint
    return m


# ------------------------------------------------------------------ hostels, residences, civic blocks
def hidden_intervals(m, run, others):
    """Parts of a run (0..L) whose outside face is buried inside another wing of the model."""
    cuts = [0.0, run.L]
    for (X0, X1, Z0, Z1) in others:
        for X in (X0, X1):
            for Z in (Z0, Z1):
                lx, ly = m.b(X, Z)
                s_ = (lx - run.a[0]) * run.t[0] + (ly - run.a[1]) * run.t[1]
                if 0 < s_ < run.L:
                    cuts.append(s_)
    cuts = sorted(set(round(c, 4) for c in cuts))
    visible = []
    for c0, c1 in zip(cuts[:-1], cuts[1:]):
        mid = run.P((c0 + c1) / 2, 0.15, 0)
        X, Z = m.px - mid[0], m.pz - mid[1]
        buried = any(X0 - 1e-3 < X < X1 + 1e-3 and Z0 - 1e-3 < Z < Z1 + 1e-3 for (X0, X1, Z0, Z1) in others)
        if not buried:
            if visible and abs(visible[-1][1] - c0) < 1e-3:
                visible[-1] = (visible[-1][0], c1)
            else:
                visible.append((c0, c1))
    return visible


class SubRun(Run):
    """A piece s0..s1 of a run, re-based to start at 0."""

    def __init__(self, run, s0, s1):
        a = run.P(s0, 0, 0)[:2]
        b_ = run.P(s1, 0, 0)[:2]
        Run.__init__(self, run.m, a, b_, run.n)


HOSTEL_WALL = 0.3     # the interior shell's outer walls stand this far inside the facade
HOSTEL_DOOR_TOP = 2.75
HOSTEL_FL = 0.6       # hostel floors sit 0.6 m above each storey line, as in the academic block
# The hostel walls, as in the owner's photos (2026-11) and the institute's scale model: plain
# cream plaster with each window sunk in its own deep niche (a steep sloped sill below the
# glass), the niches in pairs across the wall between two rooms, plain wall between pairs, and
# stair cores as tall jali screens between sand-coloured fins that rise above the roof.
HOSTEL_PROUD = 0.3    # the plain wall stands this far in front of the facade line
HOSTEL_GLASS = 0.18   # glass this far behind the facade line (the interior reveals end here)
HOSTEL_WIN_Z = (0.95, 2.6)    # glass sill and head above each storey line (interiors cut the same)
HOSTEL_NICHE_Z = (0.7, 2.95)  # niche bottom (front edge of the sloped sill) and soffit
HOSTEL_NICHE_W = 1.5          # niche width; the glass is 0.17 m in from each side
HOSTEL_PIER = 0.18            # half the pier between the two niches of a pair
HOSTEL_FIN = 0.55             # width of the sand-coloured fins either side of a stair core
HOSTEL_HEAD = 3.0             # stair heads and fins rise this far above the roof


def hostel_slots(L, slots=True):
    """Centres of the jali stair slots on a hostel wall of length L: one every ~24 m on long walls."""
    if not slots or L <= 20:
        return []
    n = int(L // 24) or 1
    return [L * (k + 0.5) / n for k in range(n)]


def hostel_pieces(L, slot_at):
    """The stretches of wall between the slots, each split into window bays: [(p0, p1, bays, bw)]."""
    pieces = []
    last = 0.0
    for c in slot_at:
        pieces.append((last, c - 1.6))
        last = c + 1.6
    pieces.append((last, L))
    out = []
    for p0, p1 in pieces:
        if p1 - p0 < 0.5:
            continue
        bays = max(1, round((p1 - p0) / 3.2))
        out.append((p0, p1, bays, (p1 - p0) / bays))
    return out


def hostel_niche(s0, s1, i, bays):
    """(n0, n1) of the window niche in bay i (s0..s1) of a stretch of `bays` bays. One room
    stands behind each bay; the windows of rooms 0-1, 2-3, ... pair up across the wall between
    them, and the odd last room has its window in the middle."""
    w = min(HOSTEL_NICHE_W, (s1 - s0) * 0.47)
    if i < bays - bays % 2:
        if i % 2 == 0:
            return (s1 - HOSTEL_PIER - w, s1 - HOSTEL_PIER)
        return (s0 + HOSTEL_PIER, s0 + HOSTEL_PIER + w)
    c = (s0 + s1) / 2
    return (c - w / 2, c + w / 2)


def hostel_glass(s0, s1, i, bays):
    """(g0, g1) of the glass in bay i: the opening the interior walls cut too."""
    n0, n1 = hostel_niche(s0, s1, i, bays)
    return (n0 + 0.17, n1 - 0.17)


def hostel_door_bay(L, slot_at):
    """(s0, s1) of the ground-floor entrance bay: the first bay after the first slot."""
    if not slot_at:
        return None
    for p0, p1, bays, bw in hostel_pieces(L, slot_at):
        if p0 > slot_at[0] - 1e-6 and bw >= 2.0:
            return (p0, p0 + bw)
    return None


def hostel_door(run, s0, s1):
    """A hostel entrance: a doorway through the wall, a concrete canopy and steps."""
    os0, os1 = s0 + 0.5, s1 - 0.5
    t, fl, P = HOSTEL_WALL, HOSTEL_FL, HOSTEL_PROUD
    mat = "Hostel_Plaster"
    run.rect(s0, os0, 0.5, FLOOR, P, mat)
    run.rect(os1, s1, 0.5, FLOOR, P, mat)
    run.rect(os0, os1, 0.5, fl, P, "Concrete")
    run.rect(os0, os1, HOSTEL_DOOR_TOP, FLOOR, P, mat)
    run.side(os0, fl, HOSTEL_DOOR_TOP, P, -t, "Concrete", 1)
    run.side(os1, fl, HOSTEL_DOOR_TOP, P, -t, "Concrete", -1)
    run.hband(os0, os1, HOSTEL_DOOR_TOP, P, -t, "Concrete", up=False)
    run.hband(os0, os1, fl, P + 0.15, -t, "Concrete", up=True)
    run.box(os0 - 0.6, os1 + 0.6, HOSTEL_DOOR_TOP + 0.15, HOSTEL_DOOR_TOP + 0.32, P, 1.5, "Concrete", bottom=True)
    # Dark metal door frame inside the opening.
    run.box(os0, os0 + 0.08, fl, HOSTEL_DOOR_TOP, -t * 0.5 - 0.04, -t * 0.5 + 0.04, "Metal")
    run.box(os1 - 0.08, os1, fl, HOSTEL_DOOR_TOP, -t * 0.5 - 0.04, -t * 0.5 + 0.04, "Metal")
    run.box(os0, os1, HOSTEL_DOOR_TOP - 0.08, HOSTEL_DOOR_TOP, -t * 0.5 - 0.04, -t * 0.5 + 0.04, "Metal", top=False)
    for k in range(3):
        run.box(os0 - 0.4, os1 + 0.4, -0.8, fl - 0.15 * k, P + 0.15, P + 0.15 + 0.35 * (k + 1), "Concrete", bottom=False)


def hostel_window(run, n0, n1, zf):
    """One window niche of a storey: the plain wall's reveals back to the glass, a flat soffit,
    a steep sloped sill, the glass in a white sliding frame."""
    P, G = HOSTEL_PROUD, HOSTEL_GLASS
    mat = "Hostel_Plaster"
    nz0, nz1 = zf + HOSTEL_NICHE_Z[0], zf + HOSTEL_NICHE_Z[1]
    gz0, gz1 = zf + HOSTEL_WIN_Z[0], zf + HOSTEL_WIN_Z[1]
    g0, g1 = n0 + 0.17, n1 - 0.17
    run.side(n0, nz0, nz1, P, -G, mat, 1)
    run.side(n1, nz0, nz1, P, -G, mat, -1)
    run.hband(n0, n1, nz1, P, -G, mat, up=False)
    n = run.n
    run.m.face([run.P(n0, P, nz0), run.P(n1, P, nz0), run.P(n1, -G, gz0), run.P(n0, -G, gz0)], mat,
               (n[0] * 0.5, n[1] * 0.5, 1.0))
    # The back of the niche round the glass.
    run.rect(n0, g0, gz0, nz1, -G, mat)
    run.rect(g1, n1, gz0, nz1, -G, mat)
    run.rect(g0, g1, gz1, nz1, -G, mat)
    run.rect(g0, g1, gz0, gz1, -G - 0.02, "Glass")
    # White uPVC frame and the meeting rail of the two sliding sashes.
    f = 0.06
    run.rect(g0, g1, gz1 - f, gz1, -G + 0.03, "Concrete")
    run.rect(g0, g1, gz0, gz0 + f, -G + 0.03, "Concrete")
    run.rect(g0, g0 + f, gz0 + f, gz1 - f, -G + 0.03, "Concrete")
    run.rect(g1 - f, g1, gz0 + f, gz1 - f, -G + 0.03, "Concrete")
    mid = (g0 + g1) / 2
    run.rect(mid - 0.04, mid + 0.04, gz0 + f, gz1 - f, -G + 0.04, "Concrete")


def hostel_end_shift(run, rects):
    """How far the proud wall runs on past each end of a run (negative: stops short), so it
    wraps an outside corner, meets the next wing's wall in an inside corner and runs straight on
    where a flush wing continues the wall."""
    m = run.m

    def inside(s, d):
        x, y, _ = run.P(s, d, 0)
        X, Z = m.px - x, m.pz - y
        return any(X0 + 1e-3 < X < X1 - 1e-3 and Z0 + 1e-3 < Z < Z1 - 1e-3 for X0, X1, Z0, Z1 in rects)

    out = []
    for s, sgn in ((0.0, -1.0), (run.L, 1.0)):
        if inside(s + sgn * 0.1, 0.1):
            out.append(-HOSTEL_PROUD)       # another wing stands in front: inside corner
        elif inside(s + sgn * 0.1, -0.1):
            out.append(0.0)                 # a flush wing carries the wall on
        else:
            out.append(HOSTEL_PROUD)        # outside corner
    return out


def hostel_facade(run, floors, top, slots=True, door=False, rects=()):
    """Off-white hostel walls as in the owner's photos: windows sunk in paired niches in a plain
    wall that stands proud of the facade line, and jali-screened stair cores on long walls, each
    between two sand-coloured fins that rise above the roof with a stair head behind them.
    door=True puts the block's entrance in the bay beside the first stair slot. rects are the
    model's wing footprints (world), used to finish the wall at corners."""
    L = run.L
    P = HOSTEL_PROUD
    mat = "Hostel_Plaster"
    e0, e1 = hostel_end_shift(run, rects) if rects else (0.0, 0.0)
    run.box(-e0, L + e1, -0.8, 0.5, 0.0, P + 0.12, "Concrete", bottom=False)
    slot_at = hostel_slots(L, slots)
    door_bay = hostel_door_bay(L, slot_at) if door else None
    for c in slot_at:
        s0, s1 = c - 1.6, c + 1.6
        run.rect(s0, s1, 0.5, top, -0.6, "Jali")
        run.side(s0, 0.5, top, P, -0.6, "Concrete", 1)
        run.side(s1, 0.5, top, P, -0.6, "Concrete", -1)
        run.hband(s0, s1, top, P, -0.6, "Concrete", up=False)
        # A slab edge across the screen at every floor, so it reads as stacked panels.
        for f in range(1, floors):
            run.box(s0, s1, f * FLOOR - 0.12, f * FLOOR + 0.22, -0.6, -0.05, "Hostel_Plaster")
        # Fins either side, up past the parapet to the stair head.
        for a0, a1 in ((s0 - HOSTEL_FIN, s0), (s1, s1 + HOSTEL_FIN)):
            run.box(a0, a1, -0.8, top + HOSTEL_HEAD + 0.3, P, P + 0.45, "Stone", bottom=False)
        run.m.hostel_heads.append((run, c))
    edges = [-e0] + [v for c in slot_at for v in (c - 1.6, c + 1.6)] + [L + e1]
    for p0, p1, bays, bw in hostel_pieces(L, slot_at):
        lo = -e0 if p0 < 1e-6 else p0
        hi = L + e1 if p1 > L - 1e-6 else p1
        for f in range(floors):
            zf = f * FLOOR
            zb = 0.5 if f == 0 else zf
            niches = []
            for i in range(bays):
                s0, s1 = p0 + i * bw, p0 + (i + 1) * bw
                if bw < 2.0:
                    continue
                if f == 0 and door_bay and abs(s0 - door_bay[0]) < 1e-6:
                    niches.append((s0, s1, None))
                    continue
                niches.append(hostel_niche(s0, s1, i, bays) + (zf,))
            # Plain wall round the niches (and the doorway bay), as strips between them.
            cuts = [lo]
            for n0, n1, z in niches:
                cuts += [n0, n1]
            cuts.append(hi)
            nz0, nz1 = zf + HOSTEL_NICHE_Z[0], zf + HOSTEL_NICHE_Z[1]
            for k in range(0, len(cuts), 2):
                run.rect(cuts[k], cuts[k + 1], zb, zf + FLOOR, P, mat)
            for n0, n1, z in niches:
                if z is None:
                    hostel_door(run, n0, n1)
                    continue
                run.rect(n0, n1, zb, nz0, P, mat)
                run.rect(n0, n1, nz1, zf + FLOOR, P, mat)
                hostel_window(run, n0, n1, zf)
    # Parapet with coping.
    run.rect(-e0, L + e1, top, top + 1.1, P, mat)
    run.rect(-e0, L + e1, top, top + 1.1, -0.25, mat, facing=-1)
    run.box(-e0 - 0.05, L + e1 + 0.05, top + 1.1, top + 1.28, -0.3, P + 0.1, "Concrete")


def hostel_roof(m, wing, top, rects):
    """Roof of one hostel wing: stair heads behind the cores, water tanks, a white steel canopy on
    the biggest wings and rows of solar panels over the rest, as on the satellite view."""
    X0, X1, Z0, Z1 = wing
    x0, x1, y0, y1 = m.rect(X0, X1, Z0, Z1)
    keep_out = []     # local rects with something on them already
    for run, c in m.hostel_heads:
        # Only heads whose wall belongs to this wing.
        p = run.P(c, -1.0, 0)
        if not (x0 - 1e-3 < p[0] < x1 + 1e-3 and y0 - 1e-3 < p[1] < y1 + 1e-3):
            continue
        hw = 1.6 + HOSTEL_FIN
        d = min(5.0, max(3.0, (min(x1 - x0, y1 - y0)) * 0.3))
        P = HOSTEL_PROUD
        run.box(c - hw, c + hw, top, top + HOSTEL_HEAD, -d, P, "Hostel_Plaster", bottom=False)
        run.rect(c - hw, c + hw, top, top + HOSTEL_HEAD, -d, "Hostel_Plaster", facing=-1)
        run.rect(c - 1.6, c + 1.6, top + 0.3, top + HOSTEL_HEAD - 0.4, P + 0.01, "Jali")
        run.box(c - hw - 0.1, c + hw + 0.1, top + HOSTEL_HEAD, top + HOSTEL_HEAD + 0.2, -d - 0.1, P + 0.5, "Concrete")
        pts = [run.P(c - hw, -d, 0), run.P(c + hw, -d, 0), run.P(c - hw, P, 0), run.P(c + hw, P, 0)]
        keep_out.append((min(q[0] for q in pts) - 1.0, max(q[0] for q in pts) + 1.0,
                         min(q[1] for q in pts) - 1.0, max(q[1] for q in pts) + 1.0))
    w, d = x1 - x0, y1 - y0
    if not keep_out:
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        rooftop_box(m, cx, cy, min(6.0, w * 0.4), min(4.5, d * 0.4), top, 3.0, "Hostel_Plaster")
        keep_out.append((cx - 4.0, cx + 4.0, cy - 3.3, cy + 3.3))
    # Two black water tanks on a plinth near one corner.
    tx, ty = x0 + 3.2, y1 - 3.2
    m.box_local(tx - 2.4, tx + 2.4, ty - 1.2, ty + 1.2, top, top + 0.5, "Concrete", "Concrete")
    for k in (-1, 1):
        m.cylinder_local(tx + k * 1.15, ty, 1.0, top + 0.5, top + 2.2, "Metal", "Metal", segments=10)
    keep_out.append((tx - 3.4, tx + 3.4, ty - 2.2, ty + 2.2))
    if w > 14 and d > 12 and len(rects) > 0 and wing == max(rects, key=lambda r: (r[1] - r[0]) * (r[3] - r[2])):
        cx0, cx1 = x1 - min(12.0, w * 0.45), x1 - 1.8
        cy0, cy1 = y0 + 1.8, y0 + min(9.0, d * 0.5)
        steel_canopy(m, cx0, cx1, cy0, cy1, top)
        keep_out.append((cx0 - 0.8, cx1 + 0.8, cy0 - 0.8, cy1 + 0.8))
    solar_rows(m, x0 + 1.8, x1 - 1.8, y0 + 1.8, y1 - 1.8, top, keep_out)


def steel_canopy(m, x0, x1, y0, y1, z):
    """The white steel roof canopy of the hostels (owner's photo): posts, a shallow gable of
    rafters and a light sheet roof, open on all sides."""
    h, ridge, eave = 3.0, 1.1, 0.4
    ym = (y0 + y1) / 2
    n = max(2, int((x1 - x0) / 3.0))
    for k in range(n + 1):
        px = x0 + (x1 - x0 - 0.2) * k / n
        for py in (y0, y1 - 0.2):
            m.box_local(px, px + 0.2, py, py + 0.2, z, z + h, "Steel", None)
        for ya in (y0, y1):
            m.face([(px, ya, z + h), (px + 0.2, ya, z + h), (px + 0.2, ym, z + h + ridge), (px, ym, z + h + ridge)],
                   "Steel", (0, 0, -1))
    slope = ridge / (ym - y0)
    for ya in (y0 - eave, y1 + eave):
        ze = z + h - eave * slope
        for dz, mat, up in ((0.12, "Concrete", 1), (0.08, "Steel", -1)):
            m.face([(x0 - 0.3, ya, ze + dz), (x1 + 0.3, ya, ze + dz), (x1 + 0.3, ym, z + h + ridge + dz),
                    (x0 - 0.3, ym, z + h + ridge + dz)], mat, (0, 0, up))
    m.box_local(x0, x1, y0, y0 + 0.2, z + h - 0.3, z + h, "Steel", "Steel", bottom=True)
    m.box_local(x0, x1, y1 - 0.2, y1, z + h - 0.3, z + h, "Steel", "Steel", bottom=True)


def solar_rows(m, x0, x1, y0, y1, z, keep_out, depth=2.0, gap=1.3, tilt=15.0):
    """Rows of solar panels tilted to the south (world -Z, local +y) over the free roof."""
    if x1 - x0 < 3 or y1 - y0 < 3:
        return
    rise = depth * math.sin(math.radians(tilt))
    run_y = depth * math.cos(math.radians(tilt))
    y = y1 - run_y
    while y >= y0 - 1e-6:
        ya, yb = y, y + run_y     # ya: low front edge (south), yb: high back edge
        # Split the row where it crosses something on the roof.
        spans = [(x0, x1)]
        for kx0, kx1, ky0, ky1 in keep_out:
            if ky1 <= ya or ky0 >= yb:
                continue
            nxt = []
            for a, b_ in spans:
                if kx1 <= a or kx0 >= b_:
                    nxt.append((a, b_))
                    continue
                if kx0 - a > 1.0:
                    nxt.append((a, kx0))
                if b_ - kx1 > 1.0:
                    nxt.append((kx1, b_))
            spans = nxt
        for a, b_ in spans:
            if b_ - a < 2.0:
                continue
            z0, z1 = z + 0.5, z + 0.5 + rise
            # Panel top, underside, and the edges.
            m.face([(a, yb, z1), (b_, yb, z1), (b_, ya, z0), (a, ya, z0)], "Solar", (0, 0.27, 1))
            m.face([(a, yb, z1 - 0.05), (b_, yb, z1 - 0.05), (b_, ya, z0 - 0.05), (a, ya, z0 - 0.05)], "Metal", (0, -0.27, -1))
            m.face([(a, ya, z0 - 0.05), (b_, ya, z0 - 0.05), (b_, ya, z0), (a, ya, z0)], "Metal", (0, 1, 0))
            # Legs: a low front rail and a taller back rail.
            m.box_local(a, b_, ya + 0.1, ya + 0.2, z, z0 - 0.05, "Metal", None)
            m.box_local(a, b_, yb - 0.2, yb - 0.1, z, z1 - 0.05, "Metal", None)
        y -= run_y + gap


def residential_facade(run, floors, top):
    """Staff and faculty housing: cream walls, a balcony with a railing in every other bay."""
    L = run.L
    run.box(0, L, -0.8, 0.5, 0.0, 0.15, "Concrete", bottom=False)
    bays = max(1, round(L / 3.4))
    bw = L / bays
    for i in range(bays):
        s0, s1 = i * bw, (i + 1) * bw
        balcony = (i % 3 == 1) and bw > 2.6
        for f in range(floors):
            zf = f * FLOOR
            zb = 0.5 if f == 0 else zf
            if balcony and f > 0:
                run.window(s0, s1, zb, zf + FLOOR, s0 + 0.4, s1 - 0.4, zf + 0.1, zf + FLOOR - 0.5, 0.2, wall="Plaster", reveal="Plaster", frame=False)
                run.box(s0 - 0.1, s1 + 0.1, zf - 0.15, zf + 0.05, 0.0, 1.3, "Concrete")          # slab
                run.box(s0 - 0.1, s1 + 0.1, zf + 0.95, zf + 1.02, 1.22, 1.3, "Metal")            # handrail
                n = max(3, int((s1 - s0) / 0.25))
                for k in range(n + 1):
                    sx = s0 + (s1 - s0) * k / n
                    run.box(sx - 0.02, sx + 0.02, zf + 0.05, zf + 0.95, 1.24, 1.28, "Metal", top=False, bottom=False)
            else:
                run.window(s0, s1, zb, zf + FLOOR, s0 + 0.75, s1 - 0.75, zf + 0.9, zf + FLOOR - 0.6, 0.18, wall="Plaster", reveal="Concrete")
        # Thin sunshade over every window.
        if not balcony:
            for f in range(floors):
                zf = f * FLOOR
                run.box(s0 + 0.55, s1 - 0.55, zf + FLOOR - 0.55, zf + FLOOR - 0.45, 0.0, 0.6, "Concrete")
    run.rect(0, L, top, top + 1.0, 0, "Plaster")
    run.rect(0, L, top, top + 1.0, -0.25, "Plaster", facing=-1)
    run.box(-0.05, L + 0.05, top + 1.0, top + 1.18, -0.3, 0.1, "Concrete")


def utility_facade(run, floors, top):
    """Sheds and plant rooms: plain plaster, a roller shutter or two, high vents."""
    L = run.L
    run.box(0, L, -0.8, 0.3, 0.0, 0.12, "Concrete", bottom=False)
    run.rect(0, L, 0.3, top, 0, "Plaster")
    n = max(1, int(L // 9))
    for k in range(n):
        c = L * (k + 0.5) / n
        run.rect(c - 1.6, c + 1.6, 0.31, 3.2, 0.02, "Metal")
        run.box(c - 1.8, c + 1.8, 3.2, 3.45, 0.0, 0.5, "Concrete")
        for v in (-3.2, 3.2):
            if 0 < c + v - 0.6 and c + v + 0.6 < L:
                run.rect(c + v - 0.6, c + v + 0.6, top - 1.2, top - 0.6, 0.02, "Jali")
    run.rect(0, L, top, top + 0.6, 0, "Plaster")
    run.box(-0.05, L + 0.05, top + 0.6, top + 0.75, -0.25, 0.1, "Concrete")


# ------------------------------------------------------------------ Phase 4D: shops, medical, sports
# Shop units on the ground floor of the medical block and the Vipanan arcade. Each unit is a run
# of bays on its building's shop side (SHOP_SIDE), counted from the south end on east and west
# sides and from the west end on north and south sides: (name, first bay, last bay, door bay).
# build_civic_interiors.py reads the same table, and build_outdoor.py puts each name on the
# fascia board over the unit. Vipanan follows the owner's video (2026-11-19): from the south
# end of the west arm a coffee house, a bank ATM and a dairy counter, and round the corner on
# the north arm a bank, a department store and the post office (names kept generic).
SHOP_UNITS = {
    "Shopping & Medical 1 (ATM)": [("ATM", 7, 7, 7), ("Pharmacy", 5, 6, 5)],
    "Vipanan - West Arm": [("Coffee House", 0, 2, 1), ("Bank ATM", 3, 3, 3), ("Dairy & Juice", 4, 5, 5),
                           ("Mobile Repair", 6, 6, 6), ("Stationery & Photocopy", 7, 9, 8)],
    "Vipanan - North Arm": [("Bank", 0, 1, 1), ("Department Store", 2, 4, 3), ("Post Office", 5, 5, 5)],
}
SHOP_SIDE = {"Vipanan - North Arm": "S"}     # the rest face east
VIPANAN = ["Vipanan - West Arm", "Vipanan - North Arm"]


def shop_side(name):
    return SHOP_SIDE.get(name, "E")
SHOP_TOP = 2.75     # head of the shop front openings
SHOP_SILL = 1.05    # top of the stall riser under the shop windows
SHOP_INSET = 0.3    # stone either side of a shop front, inside its bay
FASCIA = (2.85, 3.3, 0.33, 0.43)   # sign board over each unit: z0, z1, d0, d1 (in front of the pilasters)
# Ordinary doorways (facade_door) per side, as bay indices from the south or west end.
CIVIC_DOORS = {
    "Shopping & Medical 1 (ATM)": [("E", 2), ("W", 2)],     # clinic entrance; back door by the stair
    "Vipanan - West Arm": [("W", 4)],                       # back doors into the service lane
    "Vipanan - North Arm": [("N", 3)],
    "Sports Complex": [("W", 2), ("W", 13), ("E", 10)],    # lobby; hall exits
}


# Buildings whose interiors were drawn room by room in world coordinates at an earlier spot:
# name -> (map x, map y) of the top-left corner they were drawn at. The layout may move them
# (same size); the interior builders draw them at this spot and shift the result. The mess and
# the medical block moved to the scale model's places on 2026-11-18.
INTERIOR_DESIGN = {
    "Shopping & Medical 1 (ATM)": (335, 1008),
    "Annapurna Mess": (368, 1087),
    "Annapurna Mess - Kitchen": (383, 1103),
}


def design_centre(name):
    bd = BUILDINGS[name]
    mx, my = INTERIOR_DESIGN[name]
    return mx - 414.0 + bd["width"] / 2, 996.0 - my - bd["depth"] / 2


def design_shift(name):
    """(dx, dz) from the spot a building's interior was drawn at to its place in the layout."""
    if name not in INTERIOR_DESIGN:
        return 0.0, 0.0
    X, Z = design_centre(name)
    return BUILDINGS[name]["x"] - X, BUILDINGS[name]["z"] - Z


class at_design:
    """Context: the named buildings sit at their design spots in BUILDINGS (and the mess doors
    follow), so code with world coordinates drawn there works; restored afterwards."""

    def __init__(self, names):
        self.names = [n for n in names if n in INTERIOR_DESIGN]

    def __enter__(self):
        global MESS_DOORS
        self.saved = {n: (BUILDINGS[n]["x"], BUILDINGS[n]["z"]) for n in self.names}
        self.doors = MESS_DOORS
        for n in self.names:
            BUILDINGS[n]["x"], BUILDINGS[n]["z"] = design_centre(n)
        MESS_DOORS = mess_doors()
        return self

    def __exit__(self, *exc):
        global MESS_DOORS
        for n, (x, z) in self.saved.items():
            BUILDINGS[n]["x"], BUILDINGS[n]["z"] = x, z
        MESS_DOORS = self.doors
        return False


def box_rect(name):
    bd = BUILDINGS[name]
    return (bd["x"] - bd["width"] / 2, bd["x"] + bd["width"] / 2, bd["z"] - bd["depth"] / 2, bd["z"] + bd["depth"] / 2)


def side_bays(name, side):
    """(bays, bay width, world coordinate of bay 0's low end) along one side of a box building:
    bays run from the south end on east and west sides, from the west end on north and south."""
    X0, X1, Z0, Z1 = box_rect(name)
    L, lo = (Z1 - Z0, Z0) if side in "EW" else (X1 - X0, X0)
    bays, bw = facade_bays(L)
    return bays, bw, lo


def side_point(name, side, i):
    """World point (X, Z) on the facade line at the middle of bay i of a side."""
    X0, X1, Z0, Z1 = box_rect(name)
    bays, bw, lo = side_bays(name, side)
    a = lo + (i + 0.5) * bw
    return {"E": (X1, a), "W": (X0, a), "N": (a, Z1), "S": (a, Z0)}[side]


def shop_points(name):
    """[(X, Z, is_door)] at the middle of every shop-front bay of a building."""
    out = []
    for unit, b0, b1, door in SHOP_UNITS.get(name, ()):
        for i in range(b0, b1 + 1):
            out.append((*side_point(name, shop_side(name), i), i == door))
    return out


def door_points(name):
    return [side_point(name, sd, i) for sd, i in CIVIC_DOORS.get(name, ())]


def shop_front(run, s0, s1, door, arcade=False):
    """A shop front filling bay s0..s1 on the ground floor: a wide opening with stone either
    side, a stall riser and glass for a window, an open doorway with a steel frame and a
    rolled-up shutter for a door, reveals through the wall to the interior shell."""
    os0, os1 = s0 + SHOP_INSET, s1 - SHOP_INSET
    t = WALL["outer"]
    z_lo = 0.6 if door else SHOP_SILL
    run.rect(s0, os0, 0.6, FLOOR, 0, "Stone")
    run.rect(os1, s1, 0.6, FLOOR, 0, "Stone")
    run.rect(os0, os1, SHOP_TOP, FLOOR, 0, "Stone")
    run.side(os0, z_lo, SHOP_TOP, 0, -t, "Stone", 1)
    run.side(os1, z_lo, SHOP_TOP, 0, -t, "Stone", -1)
    run.hband(os0, os1, SHOP_TOP, 0, -t, "Stone", up=False)
    # Rolled-up shutter in its box under the head, guide channels down the jambs. Over a doorway
    # the box is slimmer, so the opening keeps 2 m of headroom above the floor (the player's head
    # caught on the deeper box).
    box_lo = SHOP_TOP - (0.14 if door else 0.28)
    run.box(os0, os1, box_lo, SHOP_TOP, -0.06, -0.3, "Metal", top=False)
    for a, b_ in ((os0, os0 + 0.06), (os1 - 0.06, os1)):
        run.box(a, b_, z_lo, box_lo, -0.06, -0.12, "Metal", top=False, bottom=False)
    if door:
        run.hband(os0, os1, 0.6, 0.18, -t, "Concrete", up=True)
        if arcade:
            return
        for k in range(3):
            run.box(os0 - 0.3, os1 + 0.3, -0.8, 0.45 - 0.15 * k, 0.18, 0.18 + 0.35 * (k + 1), "Concrete", bottom=False)
        return
    run.rect(os0, os1, 0.6, SHOP_SILL, 0, "Stone")
    run.hband(os0 - 0.05, os1 + 0.05, SHOP_SILL, 0.06, -t * 0.4, "Concrete", up=True)
    run.rect(os0, os1, SHOP_SILL, SHOP_TOP - 0.28, -0.2, "Glass")
    # Steel glazing frame: a transom and two mullions.
    run.box(os0, os1, SHOP_TOP - 0.62, SHOP_TOP - 0.56, -0.2, -0.15, "Metal")
    for k in (1, 2):
        sx = os0 + (os1 - os0) * k / 3
        run.box(sx - 0.03, sx + 0.03, SHOP_SILL, SHOP_TOP - 0.28, -0.2, -0.15, "Metal", top=False, bottom=False)


def shop_canopy(run, bw, shops, arcade=False):
    """A concrete canopy over the shop fronts and a sign board over each unit (the unit's name
    is set on the board in Unity, from outdoor_props.json)."""
    idx = sorted(shops)
    spans, cur = [], [idx[0], idx[0]]
    for i in idx[1:]:
        if i == cur[1] + 1:
            cur[1] = i
        else:
            spans.append(cur)
            cur = [i, i]
    spans.append(cur)
    for a, b_ in ([] if arcade else spans):
        s0, s1 = a * bw - 0.3, (b_ + 1) * bw + 0.3
        run.box(max(0.0, s0), min(run.L, s1), FLOOR - 0.1, FLOOR + 0.04, 0.0, 1.35, "Concrete")
    z0, z1, d0, d1 = FASCIA
    for (s0, s1) in shop_board_spans(run, bw, shops):
        run.box(s0, s1, z0, z1, d0, d1, "Sign_Board")


def shop_board_spans(run, bw, shops):
    """Run s-ranges of the fascia boards: one per shop unit (units are told apart by their door)."""
    idx = sorted(shops)
    groups, cur = [], [idx[0]]
    for i in idx[1:]:
        if i == cur[-1] + 1 and not (shops[i] and any(shops[j] for j in cur)):
            cur.append(i)
        else:
            groups.append(cur)
            cur = [i]
    groups.append(cur)
    return [(g[0] * bw + SHOP_INSET, (g[-1] + 1) * bw - SHOP_INSET) for g in groups]


def detailed_model(model_name, parts, style, doors=(), shops=(), arcade=False):
    """One model from layout footprints; each part is a wing in the given style. doors lists
    world points (X, Z) on civic facades where a ground-floor doorway replaces the windows."""
    m = Model(model_name, *centre(parts[0]))
    rects = []
    for name in parts:
        bd = BUILDINGS[name]
        m.replaces.append(name)
        r = (bd["x"] - bd["width"] / 2, bd["x"] + bd["width"] / 2, bd["z"] - bd["depth"] / 2, bd["z"] + bd["depth"] / 2)
        rects.append(r)
        m.solid.append(r)
    for k, name in enumerate(parts):
        bd = BUILDINGS[name]
        X0, X1, Z0, Z1 = rects[k]
        floors = bd["floors"]
        top = floors * FLOOR + 0.06 * k
        poly = rect_poly(m, X0, X1, Z0, Z1)
        m.face([(p[0], p[1], top) for p in poly], "Roof", (0, 0, 1))
        others = rects[:k] + rects[k + 1:]
        door_done = False
        for run in outline_runs(m, poly):
            for (s0, s1) in hidden_intervals(m, run, others):
                sub = SubRun(run, s0, s1)
                if style == "hostel":
                    door = not door_done and hostel_door_bay(sub.L, hostel_slots(sub.L)) is not None
                    door_done = door_done or door
                    hostel_facade(sub, floors, top, door=door, rects=rects)
                elif style == "residential":
                    residential_facade(sub, floors, top)
                elif style == "utility":
                    utility_facade(sub, floors, top)
                else:
                    door_bays = [bay_at(sub, X, Z) for X, Z in doors if run_has(sub, X, Z)]
                    shop_bays = {bay_at(sub, X, Z): d for X, Z, d in shops if run_has(sub, X, Z)}
                    academic_facade(sub, 0.0, floors, "outer", ribbon=floors >= 3, pilasters=True, doors=door_bays,
                                    shops=shop_bays, arcade=arcade)
        if style == "hostel":
            hostel_roof(m, rects[k], top, rects)
            continue
        # Roof: a stair and lift room, water tanks on housing.
        cx, cy = m.b((X0 + X1) / 2, (Z0 + Z1) / 2)
        w, d = abs(X1 - X0), abs(Z1 - Z0)
        if style != "utility" and floors >= 2:
            rooftop_box(m, cx, cy, min(6.0, w * 0.4), min(4.5, d * 0.4), top, 3.0,
                        "Stone" if style == "civic" else "Plaster")
        if style == "residential" and w > 10 and d > 10:
            for sx in (-1, 1):
                tx = cx + sx * min(w, d) * 0.28
                m.cylinder_local(tx, cy + d * 0.25, 0.9, top, top + 1.6, "Plaster", "Plaster", segments=10)
    return m


ARCADE_DEPTH = 3.0      # Vipanan's colonnade, from the shop fronts to the outer edge
ARCADE_SLAB = (FLOOR, FLOOR + 0.25)
FASCIA_BAND = (3.0, FLOOR + 1.1)   # the deep white band round the colonnade, level with the parapet


def remap_material(m, start, old, new):
    """Faces from index start on: material old becomes new, UVs rescaled to new's tile size."""
    ow, oh, _ = MATERIALS[old]
    nw, nh, _ = MATERIALS[new]
    for k in range(start, len(m.faces)):
        ids, mat, uvs = m.faces[k]
        if mat == old:
            m.faces[k] = (ids, new, [(u * ow / nw, v * oh / nh) for u, v in uvs])


def arcade_rects():
    """World rectangles of Vipanan's raised colonnade: along the west arm's east front (and
    through the covered passage between the arms) and along the north arm's south front."""
    W, N = box_rect("Vipanan - West Arm"), box_rect("Vipanan - North Arm")
    xw = W[1] + ARCADE_DEPTH
    zn = N[2] - ARCADE_DEPTH
    return [(W[1], xw, W[2], W[3]), (xw, N[1], zn, N[2])]


def vipanan():
    """Vipanan, the shopping arcade (owner's video and photo, 2026-11-19; satellite view): a
    single-storey L of narrow white arms round a paved forecourt. A colonnade of square piers
    runs along the shop fronts on a raised floor reached by three steps, under a slab with a
    deep white fascia band level with the parapet; a covered passage at the corner leads to
    the service lane behind. On the roof: a steel shelter with a metal roof on the west arm
    and rows of solar panels on the north arm."""
    doors = door_points(VIPANAN[0]) + door_points(VIPANAN[1])
    shops = shop_points(VIPANAN[0]) + shop_points(VIPANAN[1])
    m = detailed_model("Vipanan", VIPANAN, "civic", doors=doors, shops=shops, arcade=True)
    remap_material(m, 0, "Stone", "Hostel_Plaster")
    W, N = box_rect(VIPANAN[0]), box_rect(VIPANAN[1])
    (a0, a1, a2, a3), (b0, b1, b2, b3) = arcade_rects()
    z_floor = 0.6
    # Raised colonnade floor, paved, with three steps down on its open edges.
    m.box(a0, a1, a2, a3, -0.8, z_floor, "Concrete", top="Stone")
    m.box(b0, b1, b2, b3, -0.8, z_floor, "Concrete", top="Stone")
    for k in range(3):
        h = z_floor - 0.2 * (k + 1)
        e = 0.32 * (k + 1)
        m.box(a1, a1 + e, a2 - e, b2, -0.8, h, "Concrete", top="Concrete")     # west arm front, south of the corner
        m.box(a0, a1, a2 - e, a2, -0.8, h, "Concrete", top="Concrete")         # south end
        m.box(b0, b1 + e, b2 - e, b2, -0.8, h, "Concrete", top="Concrete")     # north arm front
        m.box(b1, b1 + e, b2, b3, -0.8, h, "Concrete", top="Concrete")         # east end
        m.box(a0, a1, a3, a3 + e, -0.8, h, "Concrete", top="Concrete")         # passage, north end
    # Slab over the colonnade and the passage.
    s0, s1 = ARCADE_SLAB
    m.box(a0, a1, a2, a3, s0, s1, "Concrete", top="Roof", bottom=True)
    m.box(b0, b1, b2, b3, s0, s1, "Concrete", top="Roof", bottom=True)
    # Square piers on the outer edge at every bay line, and at the corners.
    p = 0.25
    _bays, bw, lo = side_bays(VIPANAN[0], "E")
    zs = [lo + i * bw for i in range(_bays + 1) if lo + i * bw < b2 - 0.5] + [b2 + p]
    for Z in zs:
        Z = min(max(Z, a2 + p), b2 + p)
        m.box(a1 - 2 * p, a1, Z - p, Z + p, z_floor, s0, "Hostel_Plaster", top=None)
    _bays, bw, lo = side_bays(VIPANAN[1], "S")
    for i in range(1, _bays + 1):
        X = min(lo + i * bw, b1 - p)
        m.box(X - p, X + p, b2, b2 + 2 * p, z_floor, s0, "Hostel_Plaster", top=None)
    # The fascia band and its coping on the open edges.
    f0, f1 = FASCIA_BAND
    # The band stands a little proud of the piers and the slab edge, and the pieces only touch
    # where they meet, so no two faces share a plane.
    t, e = 0.3, 0.08
    bands = [(a0, a1 + e, a2 - e, a2 + t),            # south end
             (a1 - t, a1 + e, a2 + t, b2 - e),        # west arm front
             (a1 - t, b1 + e, b2 - e, b2 + t),        # north arm front
             (b1 - t, b1 + e, b2 + t, b3)]            # east end
    for k, (X0, X1, Z0, Z1) in enumerate(bands):
        m.box(X0, X1, Z0, Z1, f0, f1, "Hostel_Plaster", top="Concrete", bottom=True)
        c = f1 + 0.004 * k
        m.box(X0 - 0.06, X1 + 0.06, Z0 - 0.06, Z1 + 0.06, c, c + 0.2, "Concrete", top="Concrete", bottom=True)
    # The passage's north end: a lower beam, so the band reads as one piece from the forecourt.
    m.box(a0, a1 + e, a3 - t, a3 + e, f0 + 0.4, f1 - 0.01, "Hostel_Plaster", top="Concrete", bottom=True)
    # Steel shelter on the west arm's roof (the video): four posts and a shallow metal roof.
    top = FLOOR
    cx, cz = (W[0] + W[1]) / 2, W[3] - 7.0
    hx, hz = 3.2, 3.6
    for sx in (-1, 1):
        for sz in (-1, 1):
            X, Z = cx + sx * (hx - 0.3), cz + sz * (hz - 0.3)
            m.box(X - 0.08, X + 0.08, Z - 0.08, Z + 0.08, top, top + 2.6, "Metal", top=None)
    r0, r1 = top + 2.6, top + 3.2
    for sx in (-1, 1):
        A = m.b(cx, cz - hz)
        B_ = m.b(cx, cz + hz)
        C_ = m.b(cx + sx * hx, cz + hz)
        D_ = m.b(cx + sx * hx, cz - hz)
        m.face([(A[0], A[1], r1), (B_[0], B_[1], r1), (C_[0], C_[1], r0), (D_[0], D_[1], r0)], "Metal", (0, 0, 1))
        m.face([(A[0], A[1], r1 - 0.04), (B_[0], B_[1], r1 - 0.04), (C_[0], C_[1], r0 - 0.04), (D_[0], D_[1], r0 - 0.04)],
               "Metal", (0, 0, -1))
    # Solar panels on the north arm's roof (the satellite view): tilted rows on low frames.
    for row in range(3):
        Zr = N[2] + 1.3 + row * 2.3
        for X in [N[0] + 1.6 + 2.1 * k for k in range(int((N[1] - N[0] - 2.6) / 2.1))]:
            A, B_ = m.b(X, Zr), m.b(X + 1.9, Zr)
            C_, D_ = m.b(X + 1.9, Zr + 1.4), m.b(X, Zr + 1.4)
            m.face([(A[0], A[1], top + 0.5), (B_[0], B_[1], top + 0.5), (C_[0], C_[1], top + 1.2), (D_[0], D_[1], top + 1.2)],
                   "Solar", (0, 0, 1))
            m.box(X + 0.1, X + 1.8, Zr + 1.2, Zr + 1.3, top, top + 1.15, "Metal", top=None)
    m.local_bounds_to_solid()
    return m


def mess_doors():
    """Entrances of Annapurna Mess: the middle of the hall's west front and of the open stretch
    of its south front, and a service door in the kitchen's east wall."""
    h, k = BUILDINGS["Annapurna Mess"], BUILDINGS["Annapurna Mess - Kitchen"]
    hx0, hz0 = h["x"] - h["width"] / 2, h["z"] - h["depth"] / 2
    kx0, kx1 = k["x"] - k["width"] / 2, k["x"] + k["width"] / 2
    return [(hx0, h["z"]), ((hx0 + kx0) / 2, hz0), (kx1, k["z"] - 6.0)]


MESS_DOORS = mess_doors()


def mess():
    """Annapurna Mess (the model's dining block 5): sandstone hall with tall glazed bays."""
    m = detailed_model("Annapurna_Mess", ["Annapurna Mess", "Annapurna Mess - Kitchen"], "civic", doors=MESS_DOORS)
    # Double-height glazed bays on the hall's north front (the model's tall windows).
    bd = BUILDINGS["Annapurna Mess"]
    X0, X1, Z1 = bd["x"] - bd["width"] / 2, bd["x"] + bd["width"] / 2, bd["z"] + bd["depth"] / 2
    for k in range(3):
        cx = X0 + (k + 1) * (X1 - X0) / 4
        a, b_ = m.b(cx + 2.4, Z1 + 0.02), m.b(cx - 2.4, Z1 + 0.02)
        r = Run(m, a, b_)
        if r.n[1] > 0:
            r = Run(m, b_, a)
        r.rect(0, r.L, 0.6, 2 * FLOOR - 0.3, 0.25, "Glass")
        r.box(-0.4, 0.0, 0.5, 2 * FLOOR - 0.02, 0.0, 0.6, "Stone")
        r.box(r.L, r.L + 0.4, 0.5, 2 * FLOOR - 0.02, 0.0, 0.6, "Stone")
        r.box(-0.4, r.L + 0.4, 2 * FLOOR - 0.3, 2 * FLOOR - 0.02, 0.0, 0.6, "Stone")
    # The owner's video (2026-11-19): cream plaster rather than sandstone, deep vertical fins
    # between the tall windows of the north front, and a plain tower rising over the hall's
    # north-east corner beside the entrance.
    top = 2 * FLOOR
    _b, bw, lo = side_bays("Annapurna Mess", "N")
    glazed = [X0 + (k + 1) * (X1 - X0) / 4 for k in range(3)]
    for i in range(1, _b):
        X = lo + i * bw
        if min(abs(X - g) for g in glazed) < 3.0:
            continue
        m.box(X - 0.13, X + 0.13, Z1 + 0.01, Z1 + 0.75, 0.62, top + 0.03, "Stone", top="Concrete")
    remap_material(m, 0, "Stone", "Hostel_Plaster")
    # The tower stands a little proud of the facade and its parapet coping.
    tx0, tx1, tz0, tz1 = X1 - 5.5, X1 + 0.2, Z1 - 5.0, Z1 + 0.2
    m.box(tx0, tx1, tz0, tz1, top + 0.01, top + 4.6, "Hostel_Plaster", top="Roof")
    m.box(tx0 - 0.15, tx1 + 0.15, tz0 - 0.15, tz1 + 0.15, top + 4.6, top + 4.85, "Concrete", top="Concrete", bottom=True)
    for X in (tx0 + 1.8, tx1 - 1.8):
        a, b_ = m.b(X - 0.35, tz1 + 0.01), m.b(X + 0.35, tz1 + 0.01)
        rr = Run(m, a, b_)
        if rr.n[1] > 0:
            rr = Run(m, b_, a)
        rr.rect(0, rr.L, top + 0.8, top + 4.0, 0.02, "Glass")
    return m


def auto_models():
    """Every layout building not covered by a hand-made model gets a detailed model in a
    style chosen from its name: hostel, housing, civic (sandstone) or utility."""
    groups = [
        ("Walled_Residence", ["Walled Residence", "Walled Residence - Wing"], "residential"),
    ]
    # The professor residences (the scale model's 4): each tower is two stepped halves.
    for k in range(1, 6):
        parts = [n for n in BUILDINGS if n.startswith("Faculty Residence %d - " % k)]
        if parts:
            groups.append(("Faculty_Residence_%d" % k, parts, "residential"))
    named = {n for _, parts, _ in groups for n in parts}
    out = list(groups)
    skip = {"Main Gate", "Clock Tower", "Water Tower"}
    for name, bd in BUILDINGS.items():
        if name in named or name in skip or name in HAND_COVERED:
            continue
        n = name.lower()
        if "hostel" in n:
            style = "hostel"
        elif "staff" in n or "faculty" in n or "block near atm" in n or "west building" in n:
            style = "residential"
        elif "shed" in n or "substation" in n or "pavilion" in n:
            style = "utility"
        else:
            style = "civic"
        model_name = "".join(ch if ch.isalnum() else "_" for ch in name).strip("_")
        while "__" in model_name:
            model_name = model_name.replace("__", "_")
        out.append((model_name, [name], style))
    return out


def clip_half_plane(poly, p, q, keep_left=True):
    """Sutherland-Hodgman: the part of a polygon on one side of the line p -> q."""
    def side(r):
        v = (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])
        return v if keep_left else -v
    out = []
    for k in range(len(poly)):
        a, b_ = poly[k], poly[(k + 1) % len(poly)]
        sa, sb = side(a), side(b_)
        if sa >= 0:
            out.append(a)
        if (sa >= 0) != (sb >= 0):
            t = sa / (sa - sb)
            out.append((a[0] + (b_[0] - a[0]) * t, a[1] + (b_[1] - a[1]) * t))
    return out


def anticlockwise(poly):
    area = sum(poly[k][0] * poly[(k + 1) % len(poly)][1] - poly[(k + 1) % len(poly)][0] * poly[k][1] for k in range(len(poly)))
    return poly if area > 0 else poly[::-1]


def amphitheatre_pond():
    """Open-air amphitheatre and stepped pond east of the academic block's curved front, traced
    from the top view of the institute's model. The seating fan is a wide shallow sector whose
    rows rise west towards the building; a flat stage fills its narrow east end; the pond wraps
    the stage on the east and south, its basins split by zig-zag weirs and stepping down away
    from the stage, with a round lobe beside the stage.
    The whole fan is sunk into the slope (generate_terrain.py lowers its plot by
    AMPHITHEATRE_DEPTH, the height of the seating), so the top walk is level with the ring road
    outside the block: you walk straight onto it through a gap in the parapet and down the
    aisles to the stage; the grass banks beside the seating also lead down to the stage and pond."""
    def M(px, py):
        # Photo pixel (west up, north right) -> map units, scaled on the academic block.
        return (543.0 + (py - 860.0) / 4.8, 790.0 - (px - 720.0) / 4.0)

    def W(x, y):                      # map -> world
        return (x - 414.0, 996.0 - y)

    apex = W(610.0, 711.0)            # centre of the fan's arcs, out over the pond
    pivot = W(588.0, 711.0)           # middle of the stage
    m = Model("Amphitheatre_Pond", *pivot)
    ax, az = apex
    axis = math.pi                    # the fan opens west (world angle from +X towards +Z)
    half = math.radians(39.0)
    r_in, r_stage = 16.0, 27.4        # stage from r_in to r_stage, seats beyond
    tiers = 11
    tread = (43.0 - r_stage) / tiers
    rise = 0.36
    segs = 16
    angles = [axis - half + 2 * half * j / segs for j in range(segs + 1)]

    def P(r, a, z):
        x, y = m.b(ax + r * math.cos(a), az + r * math.sin(a))
        return (x, y, z)

    sx, sy = m.b(ax, az)
    for k in range(tiers):
        ri, ro = r_stage + k * tread, r_stage + (k + 1) * tread
        z = (k + 1) * rise
        for j in range(segs):
            a0, a1 = angles[j], angles[j + 1]
            m.face([P(ri, a0, z), P(ro, a0, z), P(ro, a1, z), P(ri, a1, z)], "Concrete", (0, 0, 1))
            inward = P(ri, (a0 + a1) / 2, 0)
            m.face([P(ri, a0, z - rise), P(ri, a1, z - rise), P(ri, a1, z), P(ri, a0, z)], "Concrete",
                   (sx - inward[0], sy - inward[1], 0))
        for a, sgn in ((angles[0], -1), (angles[-1], 1)):
            hint = P(1.0, a + sgn * math.pi / 2, 0)
            m.face([P(ri, a, -0.5), P(ro, a, -0.5), P(ro, a, z), P(ri, a, z)], "Stone",
                   (hint[0] - sx, hint[1] - sy, 0))
    rmax = r_stage + tiers * tread
    ztop = tiers * rise
    # Two aisles of half-height steps.
    for aisle in (axis - half / 2, axis + half / 2):
        for k in range(tiers):
            ri = r_stage + k * tread
            da = 0.8 / ri
            z = k * rise + rise / 2
            m.face([P(ri, aisle - da, z), P(ri + tread / 2, aisle - da, z), P(ri + tread / 2, aisle + da, z), P(ri, aisle + da, z)],
                   "Stone", (0, 0, 1))
    # Top walk, low parapet and the outer wall with piers.
    entrance = (segs // 2 - 1, segs // 2)          # the two segments either side of the axis
    for j in range(segs):
        a0, a1 = angles[j], angles[j + 1]
        out = P(rmax + 1, (a0 + a1) / 2, 0)
        o = (out[0] - sx, out[1] - sy, 0)
        i_ = (-o[0], -o[1], 0)
        m.face([P(rmax, a0, ztop), P(rmax + 2.0, a0, ztop), P(rmax + 2.0, a1, ztop), P(rmax, a1, ztop)], "Stone", (0, 0, 1))
        m.face([P(rmax + 2.0, a0, -0.8), P(rmax + 2.0, a1, -0.8), P(rmax + 2.0, a1, ztop), P(rmax + 2.0, a0, ztop)], "Stone", o)
        if j in entrance:
            continue                                 # the way in from the ring road: no parapet
        m.face([P(rmax + 1.75, a0, ztop + 0.9), P(rmax + 2.05, a0, ztop + 0.9), P(rmax + 2.05, a1, ztop + 0.9), P(rmax + 1.75, a1, ztop + 0.9)],
               "Concrete", (0, 0, 1))
        m.face([P(rmax + 1.75, a0, ztop), P(rmax + 1.75, a1, ztop), P(rmax + 1.75, a1, ztop + 0.9), P(rmax + 1.75, a0, ztop + 0.9)], "Concrete", i_)
        m.face([P(rmax + 2.05, a0, ztop), P(rmax + 2.05, a1, ztop), P(rmax + 2.05, a1, ztop + 0.9), P(rmax + 2.05, a0, ztop + 0.9)], "Concrete", o)
        r = Run(m, P(rmax + 2.0, a0 - 0.006, 0)[:2], P(rmax + 2.0, a0 + 0.006, 0)[:2], (o[0], o[1]))
        L0 = math.hypot(*r.n)
        r.n = (r.n[0] / L0, r.n[1] / L0)
        r.box(0, r.L, -0.8, ztop - 0.2, 0.0, 0.3, "Stone", bottom=False)
    # End caps on the parapet either side of the entrance.
    for a, sgn in ((angles[entrance[0]], 1), (angles[entrance[1] + 1], -1)):
        hint = P(1.0, a + sgn * math.pi / 2, 0)
        m.face([P(rmax + 1.75, a, ztop), P(rmax + 2.05, a, ztop), P(rmax + 2.05, a, ztop + 0.9), P(rmax + 1.75, a, ztop + 0.9)],
               "Concrete", (hint[0] - sx, hint[1] - sy, 0))
    # Stage: a flat paved trapezoid filling the narrow end of the fan.
    stage = [P(r_in, a, 0)[:2] for a in angles] + [P(r_stage, a, 0)[:2] for a in angles[::-1]]
    m.prism(anticlockwise(dedupe(stage)), -0.4, 0.3, "Stone", "Concrete")

    # The pond: four basins traced from the photo, each a step lower than the one before.
    basins = [
        [(705, 1062), (720, 1060), (850, 1040), (900, 1035), (950, 1070), (990, 1105), (1060, 1105), (1075, 1070),
         (1100, 1060), (1120, 1065), (1120, 1095), (1085, 1125), (1080, 1150), (1095, 1176), (865, 1064), (725, 1085)],
        [(705, 1062), (725, 1085), (865, 1064), (1095, 1176), (1040, 1200), (822, 1106), (735, 1133), (684, 1098), (690, 1072)],
        [(684, 1098), (735, 1133), (822, 1106), (1040, 1200), (980, 1205), (905, 1200), (745, 1213), (740, 1205),
         (700, 1180), (685, 1150)],
        [(745, 1213), (905, 1200), (900, 1235), (885, 1245), (760, 1260), (748, 1240)],
    ]
    for k, b_ in enumerate(basins):
        poly = anticlockwise(dedupe([m.b(*W(*M(px, py))) for px, py in b_]))
        rim = 1.2 - 0.3 * k
        m.pool(poly, rim, rim - 0.18, inset=0.35)
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


BRAHMAPUTRA = ["Brahmaputra Hostel - East Wing", "Brahmaputra Hostel - North Wing", "Brahmaputra Hostel - South Wing"]
# The stepped chain south of the track (owner's satellite shot, 2026-11-20).
YAMUNA = ["Yamuna Hostel - North Block", "Yamuna Hostel - Step Block", "Yamuna Hostel - West Block", "Yamuna Hostel - South Block"]
SINDHU = ["Sindhu Hostel - North Block", "Sindhu Hostel - Step Block", "Sindhu Hostel - South Block"]
GIRLS_A = ["Girls Hostel A - North Wing", "Girls Hostel A - West Wing", "Girls Hostel A - South Wing"]
GIRLS_B = ["Girls Hostel B - West Wing", "Girls Hostel B - North Wing", "Girls Hostel B - East Wing"]
HAND_COVERED = {
    "Academic Block - North Wing", "Academic Block - South Wing", "Academic Block - West Wing",
    "Academic Block - East Wing", "Academic Block - Entrance", "Mandapam Auditorium (west drum)", "East Drum",
    "Annapurna Mess", "Annapurna Mess - Kitchen",
    *BRAHMAPUTRA, *YAMUNA, *SINDHU,
    "Girls Hostel B - West Wing", "Girls Hostel B - North Wing", "Girls Hostel B - East Wing",
    "Girls Hostel A - North Wing", "Girls Hostel A - West Wing", "Girls Hostel A - South Wing",
    "Shopping & Medical 1 (ATM)", *VIPANAN, "Sports Complex",
}
CIVIC_4D = [("Shopping_Medical_1_ATM", "Shopping & Medical 1 (ATM)"), ("Sports_Complex", "Sports Complex")]


MODELS = [
    academic_block,
    clock_tower,
    clock_steps,
    main_gate,
    mess,
    amphitheatre_pond,
    water_tower,
    vipanan,
    # The boys' hostels are U blocks too (owner, 2026-11-18).
    lambda: detailed_model("Brahmaputra_Hostel", BRAHMAPUTRA, "hostel"),
    lambda: detailed_model("Yamuna_Hostel", YAMUNA, "hostel"),
    lambda: detailed_model("Sindhu_Hostel", SINDHU, "hostel"),
    # The girls' hostels: two U blocks round badminton courtyards (the owner's top view).
    lambda: detailed_model("Girls_Hostel_A", GIRLS_A, "hostel"),
    lambda: detailed_model("Girls_Hostel_B", GIRLS_B, "hostel"),
]
MODELS += [(lambda mn=mn, n=n: detailed_model(mn, [n], "civic", doors=door_points(n), shops=shop_points(n)))
           for mn, n in CIVIC_4D]
MODELS += [(lambda g=g: detailed_model(*g)) for g in auto_models()]


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


# ------------------------------------------------------------------ roof stairs
# Steel stairs up the outside of the big buildings to their roofs (the owner asked for access to
# the terraces): a switchback of two lanes against a wall that has no doorway, on level ground,
# clear of roads and other buildings (sites picked with the layout, doors and heightmap). The top
# landing reaches the parapet coping, so you step over it onto the roof.
# model name: [(layout part, side of that part, offset of the stair's middle from the side's middle)]
FIRE_ESCAPES = {
    "Academic_Block": [("Academic Block - South Wing", "E", -15.0)],
    "Annapurna_Mess": [("Annapurna Mess - Kitchen", "S", 6.0)],
    "Brahmaputra_Hostel": [("Brahmaputra Hostel - East Wing", "N", 4.0)],
    "Yamuna_Hostel": [("Yamuna Hostel - South Block", "W", 0.0)],
    "Sindhu_Hostel": [("Sindhu Hostel - South Block", "S", -9.0)],
    "Girls_Hostel_A": [("Girls Hostel A - South Wing", "E", 6.0)],
    "Girls_Hostel_B": [("Girls Hostel B - West Wing", "N", -4.0)],
    "Shopping_Medical_1_ATM": [("Shopping & Medical 1 (ATM)", "W", 11.0)],
    "Sports_Complex": [("Sports Complex", "E", -23.0)],
    "East_Block": [("East Block", "E", -10.0)],
    "Chenab_Hostel": [("Chenab Hostel", "E", 0.0)],
}
FE_W0, FE_LANE, FE_HALF = 0.9, 1.15, 2.6     # off the facade line; lane width; half length along the wall
FE_FLIGHT = 1.4                               # each flight runs from -1.4 to +1.4 along the wall


def fire_escape(m, part, side, off):
    """One roof stair against side ("N", "S", "E", "W") of a layout part, centred off metres
    from the middle of that side. u runs along the wall, w out from the facade line."""
    bd = BUILDINGS[part]
    X0, X1 = bd["x"] - bd["width"] / 2, bd["x"] + bd["width"] / 2
    Z0, Z1 = bd["z"] - bd["depth"] / 2, bd["z"] + bd["depth"] / 2
    (ax, az), (tx, tz), (nx, nz) = {
        "N": ((bd["x"], Z1), (1.0, 0.0), (0.0, 1.0)), "S": ((bd["x"], Z0), (1.0, 0.0), (0.0, -1.0)),
        "E": ((X1, bd["z"]), (0.0, 1.0), (1.0, 0.0)), "W": ((X0, bd["z"]), (0.0, 1.0), (-1.0, 0.0))}[side]
    cx, cz = ax + tx * off, az + tz * off

    def W(u, w):
        return (cx + tx * u + nx * w, cz + tz * u + nz * w)

    def plate(u0, u1, w0, w1, z0, z1, mat="Metal"):
        (Xa, Za), (Xb, Zb) = W(u0, w0), W(u1, w1)
        m.box(min(Xa, Xb), max(Xa, Xb), min(Za, Zb), max(Za, Zb), z0, z1, mat, mat)

    # The coping (or whatever stands on the wall top) right behind the top landing.
    lo_x, lo_y = m.b(*W(-FE_HALF, -0.6))
    hi_x, hi_y = m.b(*W(FE_HALF, 0.3))
    x0, x1, y0, y1 = min(lo_x, hi_x), max(lo_x, hi_x), min(lo_y, hi_y), max(lo_y, hi_y)
    tops = [v[2] for v in m.verts if x0 <= v[0] <= x1 and y0 <= v[1] <= y1]
    roof = bd["floors"] * FLOOR
    top = max([t for t in tops if t < roof + 2.0] or [roof + 1.3])
    if top < roof - 0.5:
        top = roof + 1.3
    n = max(2, int(math.ceil(top / 1.75)))   # half-storey flights of at most 1.75 m (risers under 0.18 m)
    R = top / n
    steps = 10
    r = R / steps
    w_in, w_mid, w_out = FE_W0, FE_W0 + FE_LANE, FE_W0 + 2 * FE_LANE + 0.1
    tread = 2 * FE_FLIGHT / (steps - 1)
    for k in range(n):
        z0 = k * R
        up = k % 2 == 0                      # even flights climb +u on the outer lane, odd ones -u on the inner
        lane = (w_mid + 0.1, w_out) if up else (w_in, w_mid)
        for i in range(steps - 1):
            zt = z0 + (i + 1) * r
            a = -FE_FLIGHT + i * tread if up else FE_FLIGHT - (i + 1) * tread
            plate(a, a + tread, lane[0], lane[1], zt - 0.06, zt)
        # Landing at the flight's top, across both lanes.
        zt = z0 + R
        u0, u1 = (FE_FLIGHT, FE_HALF) if up else (-FE_HALF, -FE_FLIGHT)
        plate(u0, u1, w_in, w_out, zt - 0.12, zt)
        # Guard rail along the outer edge and the end of the landing.
        plate(-FE_HALF, FE_HALF, w_out, w_out + 0.05, zt + 1.0, zt + 1.05)
        ue = FE_HALF if up else -FE_HALF
        plate(ue - 0.05 if up else ue, ue if up else ue + 0.05, w_in, w_out, zt + 1.0, zt + 1.05)
        if k < n - 1:   # and on the wall side, over the gap to the facade (the top one opens onto the bridge)
            plate(-FE_HALF, FE_HALF, w_in - 0.05, w_in, zt + 1.0, zt + 1.05)
    # The top landing reaches over the wall top: a bridge plate onto the coping.
    ue0, ue1 = (FE_FLIGHT, FE_HALF) if (n - 1) % 2 == 0 else (-FE_HALF, -FE_FLIGHT)
    plate(ue0, ue1, -0.6, w_in, top - 0.12, top + 0.01)
    # Corner posts and outer bars, the full height.
    for u in (-FE_HALF, FE_HALF):
        for w in (w_in, w_out):
            plate(u - 0.08, u + 0.08, w - 0.08, w + 0.08, -0.3, top + 1.05)
    for i in range(1, 9):
        u = -FE_HALF + 2 * FE_HALF * i / 9
        plate(u - 0.02, u + 0.02, w_out, w_out + 0.04, 0.0, top + 1.05)
    m.solid.append((min(W(-FE_HALF, 0)[0], W(FE_HALF, w_out)[0]), max(W(-FE_HALF, 0)[0], W(FE_HALF, w_out)[0]),
                    min(W(-FE_HALF, 0)[1], W(FE_HALF, w_out)[1]), max(W(-FE_HALF, 0)[1], W(FE_HALF, w_out)[1])))


def add_fire_escapes(m):
    for part, side, off in FIRE_ESCAPES.get(m.name, ()):
        fire_escape(m, part, side, off)


def main():
    render = "--render" in sys.argv
    os.makedirs(OUT_DIR, exist_ok=True)
    if render:
        os.makedirs(PREVIEW_DIR, exist_ok=True)
    entries = []
    for make in MODELS:
        clear_scene()
        m = make()
        add_fire_escapes(m)
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
