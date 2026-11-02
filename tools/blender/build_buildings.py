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
    "Facade_Hostel": (3.0, FLOOR, (0.84, 0.79, 0.68)),
    "Facade_Ground": (4.0, FLOOR, (0.35, 0.42, 0.48)),
    "Brick": (2.0, 2.0, (0.60, 0.32, 0.23)),
    "Plaster": (4.0, 4.0, (0.86, 0.81, 0.70)),
    "Concrete": (3.0, 3.0, (0.86, 0.85, 0.82)),
    "Glass": (1.5, FLOOR, (0.25, 0.34, 0.42)),
    "Roof": (6.0, 6.0, (0.38, 0.37, 0.35)),
    "Metal": (2.0, 2.0, (0.22, 0.23, 0.24)),
    "ClockFace": (1.0, 1.0, (0.95, 0.94, 0.90)),
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
            # Brick stair towers at both ends of the long side, rising above the roof.
            cw = min(4.0, (x1 - x0 if long_x else y1 - y0) / 4)
            if long_x:
                for xa in (x0 - 0.4, x1 - cw + 0.4):
                    self.box_local(xa, xa + cw, y0 - 0.4, y0 + 3.5, 0.45, H + 2.8, "Brick", "Roof")
            else:
                for ya in (y0 - 0.4, y1 - cw + 0.4):
                    self.box_local(x0 - 0.4, x0 + 3.5, ya, ya + cw, 0.45, H + 2.8, "Brick", "Roof")
        # Roof clutter: a stair cabin and a water tank.
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        if floors > 1:
            self.box_local(cx - 2.2, cx + 2.2, cy - 2.0, cy + 2.0, H, H + 3.0, "Plaster", "Roof")
            self.box_local(cx + 3.0, cx + 6.0, cy - 1.2, cy + 1.2, H + 0.6, H + 2.4, "Metal", "Metal")
        return x0, x1, y0, y1, H

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


def material(name):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (*MATERIALS[name][2], 1.0)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*MATERIALS[name][2], 1.0)
            bsdf.inputs["Roughness"].default_value = 0.2 if name == "Glass" else 0.8
    return mat


def centre(name):
    bd = BUILDINGS[name]
    return bd["x"], bd["z"]


# ------------------------------------------------------------------ the buildings
def academic_block():
    m = Model("Academic_Block", *centre("Academic Block - North Wing"))
    for wing in ("North", "South", "West", "East"):
        m.block("Academic Block - %s Wing" % wing, "academic")
    m.drum("Mandapam Auditorium (west drum)")
    m.drum("East Drum")

    # Entrance pavilion: a glass box under a deep portico facing the clock tower (+Z).
    e = BUILDINGS["Academic Block - Entrance"]
    m.replaces.append(e["name"])
    X0, X1, Z0, Z1 = e["x"] - e["width"] / 2, e["x"] + e["width"] / 2, e["z"] - e["depth"] / 2, e["z"] + e["depth"] / 2
    m.solid.append((X0, X1, Z0, Z1))
    H = e["floors"] * FLOOR
    m.box(X0 - 0.4, X1 + 0.4, Z0 - 0.4, Z1 + 0.4, -0.8, 0.45, "Concrete", "Concrete")
    m.box(X0, X1, Z0, Z1, 0.45, H, "Glass", "Roof", zbase=0.45)
    m.box(X0 - 0.6, X1 + 0.6, Z0 - 0.6, Z1 + 0.6, H, H + 1.2, "Concrete", "Concrete")
    # Portico canopy and columns.
    cz1 = Z1 + 6.0
    m.box(X0 - 1.0, X1 + 1.0, Z1, cz1, H - 0.4, H + 0.6, "Concrete", "Concrete", bottom=True)
    for X in (X0 + 1.0, X0 + (X1 - X0) / 3, X1 - (X1 - X0) / 3, X1 - 1.0):
        cx, cy = m.b(X, cz1 - 0.8)
        m.cylinder_local(cx, cy, 0.35, 0.0, H - 0.4, "Concrete", None, segments=12)
    # Steps up to the plinth.
    for k in range(3):
        m.box(X0 + 2, X1 - 2, Z1 + 0.4 + k * 0.4, Z1 + 0.8 + k * 0.4, -0.5, 0.45 - k * 0.15, "Concrete", "Concrete")
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


MODELS = [
    academic_block,
    clock_tower,
    main_gate,
    mess,
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
