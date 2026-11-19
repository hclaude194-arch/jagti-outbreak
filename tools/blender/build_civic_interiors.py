#!/usr/bin/env python3
"""Phase 4D: walk-in shopping and medical blocks and the sports complex. Imported by
build_interiors.py after the hostels (one shell and zone per building).

Each building is a civic box (build_buildings.academic_facade): the medical block and the
sports complex have three storeys, two floors of twin windows and the glass ribbon on top; the
two arms of Vipanan, the shopping arcade, are single storeys of shop units. Plans are written
out by hand here, room by room, in world coordinates (Vipanan's units are laid out from their
own shop fronts, UnitFrame); the outer walls copy the facade's openings, including the shop
fronts and doorways listed in build_buildings.SHOP_UNITS, SHOP_SIDE and CIVIC_DOORS, so change
the two files together.

    Shopping & Medical 1: the campus medical centre (waiting hall, consulting rooms, dressing
        room, a ward and an observation room upstairs, records and physiotherapy on top), a
        pharmacy and an ATM vestibule on the plaza front.
    Vipanan - West Arm: coffee house, bank ATM, dairy and juice counter, mobile repair, and
        stationery and photocopy, facing east onto the forecourt.
    Vipanan - North Arm: a bank, a department store and the post office, facing south.
    Sports Complex: a full-height hall with four badminton courts; a lobby, changing rooms,
        equipment store and first aid below a gym, and a games room and yoga room on top.

Every building of more than one storey has a dog-leg stair (build_interiors.stair_geometry) in a back corner.
Floors sit 0.6 m above each storey line, as everywhere else.
"""
import math
import random

import build_buildings as B
import build_interiors as I
from build_buildings import FLOOR, BUILDINGS, Model
from build_interiors import (PART, FL, LIGHTS, PLACED, WARM, COOL, Line, Frame, put, partition, wall_with_windows,
                             hrect, hrect_cut, prop, bx, cyl, obox, new, ST_L, ST_W, ST_LAND, level_floor, level_ceil)

MW = B.WALL["outer"]          # the civic facade's wall thickness (0.45)
TOP = 2                       # top storey of the three-storey civic blocks
TILED = ("toilet", "changing", "dressing")
DOOR_W = 1.1   # room doorways: the player capsule (0.7 m plus skin) caught on 0.9 m frames


def inner_rect(name):
    X0, X1, Z0, Z1 = B.box_rect(name)
    return (X0 + MW, X1 - MW, Z0 + MW, Z1 - MW)


# ------------------------------------------------------------------ facade openings
def civic_openings(name):
    """{side: {level: [(a0, a1, z0, z1, is_door)]}} in world coordinates along each side
    (X on the north and south sides, Z on the east and west), as academic_facade draws them."""
    floors = BUILDINGS[name]["floors"]
    doors = set(B.CIVIC_DOORS.get(name, ()))
    shops = {}
    for _unit, b0, b1, door in B.SHOP_UNITS.get(name, ()):
        for i in range(b0, b1 + 1):
            shops[i] = i == door
    out = {}
    for side in "NSEW":
        bays, bw, lo = B.side_bays(name, side)
        lv = {k: [] for k in range(floors)}
        lower = floors - 1 if floors >= 3 else floors
        for i in range(bays):
            a0, a1 = lo + i * bw, lo + (i + 1) * bw
            for f in range(lower):
                zf = f * FLOOR
                if f == 0 and (side, i) in doors:
                    lv[0].append((a0 + 0.5, a1 - 0.5, FL, B.DOOR_TOP, True))
                    continue
                if f == 0 and side == B.shop_side(name) and i in shops:
                    d = shops[i]
                    lv[0].append((a0 + B.SHOP_INSET, a1 - B.SHOP_INSET, FL if d else B.SHOP_SILL, B.SHOP_TOP, d))
                    continue
                wz0, wz1 = zf + (0.75 if f else 0.95), zf + FLOOR - 0.4
                mid = (a0 + a1) / 2
                lv[f].append((a0 + 0.42, mid - 0.14, wz0, wz1, False))
                lv[f].append((mid + 0.14, a1 - 0.42, wz0, wz1, False))
        if floors >= 3:
            zf = (floors - 1) * FLOOR
            lv[floors - 1].append((lo, lo + bays * bw, zf + 0.5, zf + FLOOR - 0.35, False))
        out[side] = lv
    return out


def side_lines(rect):
    """Inner wall lines of a box: side -> (A, B, inward normal, world axis along it)."""
    x0, x1, z0, z1 = rect
    return {"S": ((x0, z0), (x1, z0), (0.0, 1.0), 0), "N": ((x0, z1), (x1, z1), (0.0, -1.0), 0),
            "W": ((x0, z0), (x0, z1), (1.0, 0.0), 1), "E": ((x1, z0), (x1, z1), (-1.0, 0.0), 1)}


# ------------------------------------------------------------------ stairs
class CStair(I.Stair):
    """A dog-leg stair against an outer wall. corner is the inner corner of the building it
    stands in; U points from the floor landing towards the end wall, V from the side wall into
    the building (both world unit vectors)."""

    def __init__(self, corner, U, V, tag):
        self.tag = tag
        self.U, self.V = U, V
        self.o = (corner[0] - U[0] * ST_L, corner[1] - U[1] * ST_L)
        self.uz, self.vx = U[1], V[0]


# ------------------------------------------------------------------ the plan of a building
class Space:
    def __init__(self, name, kind, rect, level, front, floor="Floor_Tile"):
        self.name, self.kind, self.rect, self.level, self.front, self.floor = name, kind, rect, level, front, floor


class Civic:
    """One civic building: its rooms per level (they tile the inner rectangle), partitions
    with doorways, the stair, and optionally a full-height hall."""

    def __init__(self, name, model_name, label):
        self.name, self.label = name, label
        self.bd = BUILDINGS[name]
        self.inner = inner_rect(name)
        self.ops = civic_openings(name)
        self.m = Model(model_name + "_Interior", *B.centre(name))
        self.spaces = []
        self.walls = []        # (A, B, level, doors [(world axis centre, width)], z1 or None)
        self.stair = None
        self.hall = None       # (rect, Space) of a full-height hall
        self.top = self.bd["floors"] - 1   # top storey

    def space(self, name, kind, rect, levels, front, floor="Floor_Tile"):
        for k in levels:
            self.spaces.append(Space(name, kind, rect, k, front, floor))

    def wall(self, A, B_, levels, doors=()):
        for k in levels:
            self.walls.append((A, B_, k, list(doors)))

    def at(self, k):
        return [s for s in self.spaces if s.level == k]


ALL = (0, 1, 2)
UP = (1, 2)


def block1():
    """Shopping & Medical 1: the medical centre, a pharmacy and the ATM."""
    C = Civic("Shopping & Medical 1 (ATM)", "Shopping_Medical_1_ATM", "Medical Centre")
    x0, x1, z0, z1 = C.inner                       # -78.55, -63.45, -41.55, -12.45
    xc = -73.0                                     # west rooms | waiting hall and corridor
    xp = -70.5                                     # corridor | pharmacy
    C.stair = CStair((x0, z0), (0.0, -1.0), (1.0, 0.0), "SW")
    zs = -30.75                                    # north end of the stair hall (west door in it)
    # Ground floor.
    C.space("Stair Hall", "stair", (x0, xc, z0, zs), ALL, "W")
    C.space("Waiting Hall", "waiting", (xc, x1, z0, -23.25), [0], "E")
    C.space("Consulting Room 1", "consult", (x0, xc, zs, -25.5), [0], "W")
    C.space("Consulting Room 2", "consult", (x0, xc, -25.5, -20.25), [0], "W")
    C.space("Dressing Room", "dressing", (x0, xc, -20.25, -15.75), [0], "W")
    C.space("Toilet", "toilet", (x0, xc, -15.75, z1), ALL, "W")
    C.space("Corridor", "corridor", (xc, xp, -23.25, z1), [0], "N")
    C.space("Pharmacy", "pharmacy", (xp, x1, -23.25, -15.75), [0], "E")
    C.space("Medical Store", "store", (xp, -67.0, -15.75, z1), [0], "N")
    C.space("ATM", "atm", (-67.0, x1, -15.75, z1), [0], "E")
    C.wall((xc, z0), (xc, zs), ALL, [(-32.6, 1.4)])
    C.wall((xc, zs), (xc, z1), [0], [(-28.0, DOOR_W), (-21.6, DOOR_W), (-18.0, DOOR_W), (-14.1, DOOR_W)])
    for z in (zs, -25.5, -20.25, -15.75):
        C.wall((x0, z), (xc, z), [0])
    C.wall((xc, -23.25), (x1, -23.25), [0], [(-71.75, 2.0)])
    C.wall((xp, -23.25), (xp, z1), [0], [(-19.5, DOOR_W), (-14.1, DOOR_W)])
    C.wall((xp, -15.75), (x1, -15.75), [0])
    C.wall((-67.0, -15.75), (-67.0, z1), [0])
    # First floor: a ward, an observation room, the duty doctor and the nurses' station.
    C.space("Ward", "ward", (xc, x1, z0, -24.0), [1], "E")
    C.space("Observation Room", "observation", (xc, x1, -24.0, z1), [1], "E")
    C.space("Duty Doctor", "duty", (x0, xc, zs, -25.5), [1], "W")
    C.space("Nurses' Station", "nurses", (x0, xc, -25.5, -20.25), [1], "W")
    C.space("Linen Store", "linen", (x0, xc, -20.25, -15.75), [1], "W")
    C.wall((xc, zs), (xc, z1), [1], [(-28.0, DOOR_W), (-22.9, 1.6), (-18.0, DOOR_W), (-14.1, DOOR_W)])
    for z in (zs, -25.5, -20.25, -15.75):
        C.wall((x0, z), (xc, z), [1])
    C.wall((xc, -24.0), (x1, -24.0), [1], [(-71.6, 1.6)])
    # Top floor: medical records, the medical officer, physiotherapy.
    C.space("Medical Records", "records", (xc, x1, z0, -27.0), [2], "E")
    C.space("Physiotherapy", "physio", (xc, x1, -27.0, z1), [2], "E", "Floor_Wood")
    C.space("Medical Officer", "officer", (x0, xc, zs, -23.0), [2], "W")
    C.space("Staff Room", "staff", (x0, xc, -23.0, -15.75), [2], "W")
    C.wall((xc, zs), (xc, z1), [2], [(-29.5, DOOR_W), (-24.2, 1.6), (-17.0, DOOR_W), (-14.1, DOOR_W)])
    for z in (zs, -23.0, -15.75):
        C.wall((x0, z), (xc, z), [2])
    C.wall((xc, -27.0), (x1, -27.0), [2], [(-71.6, 1.6)])
    return C


def vipanan_arm(name, model_name, label, kinds):
    """One single-storey arm of Vipanan: its shop units side by side along the shop front
    (B.SHOP_UNITS, B.SHOP_SIDE), each the full depth of the arm, split by partitions on the
    bay lines. The back door (B.CIVIC_DOORS) opens from whichever unit it falls in."""
    C = Civic(name, model_name, label)
    side = B.shop_side(name)
    x0, x1, z0, z1 = C.inner
    _bays, bw, lo = B.side_bays(name, side)
    a_lo, a_hi = (z0, z1) if side in "EW" else (x0, x1)
    units = B.SHOP_UNITS[name]
    edges = [max(a_lo, lo + b0 * bw) for _n, b0, _b1, _d in units] + [a_hi]
    for k, (unit, _b0, _b1, _d) in enumerate(units):
        a, b_ = edges[k], edges[k + 1]
        rect = (x0, x1, a, b_) if side in "EW" else (a, b_, z0, z1)
        C.space(unit, kinds[unit], rect, [0], side)
        if k > 0:
            C.wall(*(((x0, a), (x1, a)) if side in "EW" else ((a, z0), (a, z1))), [0])
    return C


def vipanan_west():
    return vipanan_arm("Vipanan - West Arm", "Vipanan_West_Arm", "Vipanan West Arm",
                       {"Coffee House": "cafe", "Bank ATM": "atm", "Dairy & Juice": "juice",
                        "Mobile Repair": "repair", "Stationery & Photocopy": "stationery"})


def vipanan_north():
    return vipanan_arm("Vipanan - North Arm", "Vipanan_North_Arm", "Vipanan North Arm",
                       {"Bank": "bank", "Department Store": "store", "Post Office": "post"})


SC = BUILDINGS["Sports Complex"]
HALL_Z = -337.0          # the wall between the badminton hall (north) and the three-storey block


def sports():
    C = Civic("Sports Complex", "Sports_Complex", "Sports Complex")
    x0, x1, z0, z1 = C.inner                     # 24.45, 48.55, -354.55, -301.45
    zh = HALL_Z - PART / 2                       # south face line of the hall wall
    C.hall = (x0, x1, HALL_Z + PART / 2, z1)
    C.space("Badminton Hall", "hall", C.hall, [0], "W", "Sports_Floor")
    C.stair = CStair((x0, z0), (0.0, -1.0), (1.0, 0.0), "SW")
    xl, xm = 34.0, 41.25
    zc0, zc1 = -343.0, -341.0
    C.space("Lobby", "lobby", (x0, xl, z0, zh), [0], "W")
    C.space("Men's Changing Room", "changing", (xl, xm, z0, zc0), [0], "S")
    C.space("Women's Changing Room", "changing", (xm, x1, z0, zc0), [0], "S")
    C.space("Corridor", "corridor", (xl, x1, zc0, zc1), [0], "W")
    C.space("Equipment Store", "equipment", (xl, xm, zc1, zh), [0], "N")
    C.space("First Aid", "firstaid", (xm, x1, zc1, zh), [0], "N")
    C.wall((xl, z0), (xl, zh), [0], [(-342.0, 1.8)])
    C.wall((xm, z0), (xm, zc0), [0])
    C.wall((xm, zc1), (xm, zh), [0])
    C.wall((xl, zc0), (x1, zc0), [0], [((xl + xm) / 2, DOOR_W), ((xm + x1) / 2, DOOR_W)])
    C.wall((xl, zc1), (x1, zc1), [0], [((xl + xm) / 2, 1.4), ((xm + x1) / 2, DOOR_W)])
    C.space("Gym", "gym", (x0, x1, z0, zh), [1], "S", "Rubber")
    xg = 37.0
    C.space("Indoor Games Room", "games", (x0, xg, z0, zh), [2], "S", "Floor_Wood")
    C.space("Yoga Room", "yoga", (xg, x1, z0, zh), [2], "S", "Floor_Wood")
    C.wall((xg, z0), (xg, zh), [2], [(-340.0, 1.2)])
    return C


# Doorways and the viewing window in the hall wall (world X centre, width, level, sill, head).
HALL_WALL_OPS = [(29.0, 2.0, 0, 0.0, 2.4), ((34.0 + 41.25) / 2, 1.6, 0, 0.0, 2.4), (39.0, 15.0, 1, 0.95, 2.35)]


# ------------------------------------------------------------------ shell geometry
def outer_wall_spans(C, side, ax):
    """Pieces of an inner wall line: [(a0, a1, tall)] where tall marks the full-height hall."""
    A, B_, _n, _ax = side_lines(C.inner)[side]
    a0, a1 = A[ax], B_[ax]
    if C.hall is None:
        return [(a0, a1, False)]
    hx0, hx1, hz0, hz1 = C.hall
    h0, h1 = (hx0, hx1) if ax == 0 else (hz0, hz1)
    # The hall touches this side if the side's line lies on the hall's edge.
    line = A[1 - ax]
    lo, hi = (hz0, hz1) if ax == 0 else (hx0, hx1)
    if not (lo - 1e-6 <= line <= hi + 1e-6):
        return [(a0, a1, False)]
    out = []
    if h0 > a0 + 1e-6:
        out.append((a0, h0, False))
    out.append((max(a0, h0), min(a1, h1), True))
    if h1 < a1 - 1e-6:
        out.append((h1, a1, False))
    return out


def outer_walls(m, C):
    for side, (A, B_, n, ax) in side_lines(C.inner).items():
        base = A[ax]
        allops = [o for k in C.ops[side] for o in C.ops[side][k]]
        rel = [(o[0] - base, o[1] - base, o[2], o[3], o[4]) for o in allops]
        for a0, a1, tall in outer_wall_spans(C, side, ax):
            if tall:
                wall_with_windows(m, A, B_, n, rel, 0.35, MW, level_floor(0), level_ceil(C.top), a0 - base, a1 - base)
            else:
                for k in range(C.top + 1):
                    wall_with_windows(m, A, B_, n, rel, 0.35, MW, level_floor(k), level_ceil(k), a0 - base, a1 - base)


def wall_doors_rel(A, B_, doors):
    ax = 0 if abs(A[1] - B_[1]) < 1e-6 else 1
    sgn = 1.0 if B_[ax] >= A[ax] else -1.0
    return [((c - A[ax]) * sgn, w) for c, w in doors]


def hall_wall(m, C):
    """The wall between the hall and the block: full height, both faces, a doorway from the
    lobby and one from the equipment store, and the gym's viewing window."""
    x0, x1 = C.inner[0], C.inner[1]
    ln = Line(m, (x0, HALL_Z), (x1, HALL_Z), (0.0, 1.0))
    holes = []
    for c, w, k, sill, head in HALL_WALL_OPS:
        f = level_floor(k)
        holes.append((c - w / 2 - x0, c + w / 2 - x0, f + sill, f + head))
    t = PART / 2
    ln.wall(0, ln.L, level_floor(0), level_ceil(TOP), t, "Wall_Paint", holes, 1.0)
    ln.wall(0, ln.L, level_floor(0), level_ceil(TOP), -t, "Wall_Paint", holes, -1.0)
    for s0, s1, h0, h1 in holes:
        ln.side(s0, h0, h1, -t, t, "Wall_Paint", 1)
        ln.side(s1, h0, h1, -t, t, "Wall_Paint", -1)
        ln.hband(s0, s1, h1, -t, t, "Wall_Paint", up=False)
        if h0 > FL + 0.01:
            ln.hband(s0, s1, h0, -t, t, "Laminate", up=True)
            ln.rect(s0, s1, h0, h1, 0.0, "Glass_Clear")
            # A steel rail across the window on the gym side.
            ln.hband(s0, s1, h0 + 0.1, -t - 0.08, -t - 0.02, "Steel", up=True)
    # The hall's wall padding: a dark band to 2 m on the hall side, between the doorways.
    pads = [(0.0, holes[0][0]), (holes[0][1], holes[1][0]), (holes[1][1], ln.L)]
    for s0, s1 in pads:
        ln.rect(s0 + 0.05, s1 - 0.05, FL, FL + 2.0, t + 0.04, "Rubber")
        ln.side(s0 + 0.05, FL, FL + 2.0, t, t + 0.04, "Rubber", -1)
        ln.side(s1 - 0.05, FL, FL + 2.0, t, t + 0.04, "Rubber", 1)
        ln.hband(s0 + 0.05, s1 - 0.05, FL + 2.0, t, t + 0.04, "Rubber", up=True)


def tile_skin(m, C, sp):
    """White tiles to 2.1 m round a toilet, changing room or dressing room."""
    X0, X1, Z0, Z1 = sp.rect
    f = level_floor(sp.level)
    x0, x1, z0, z1 = C.inner
    eps = 0.008
    for side, (A, B_, n, ax) in side_lines(sp.rect).items():
        line = A[1 - ax]
        on_outer = abs(line - {"S": z0, "N": z1, "W": x0, "E": x1}[side]) < 1e-6
        off = eps if on_outer else PART / 2 + eps
        A2 = (A[0] + n[0] * off, A[1] + n[1] * off)
        B2 = (B_[0] + n[0] * off, B_[1] + n[1] * off)
        ln = Line(m, A2, B2, n)
        holes = []
        if on_outer:
            for o in C.ops[side].get(sp.level, []):
                holes.append((o[0] - A[ax], o[1] - A[ax], o[2], o[3]))
        for (WA, WB, k, doors) in C.walls:
            if k != sp.level:
                continue
            wax = 0 if abs(WA[1] - WB[1]) < 1e-6 else 1
            if wax != ax or abs(WA[1 - wax] - line) > 1e-6:
                continue
            for c, w in doors:
                holes.append((c - w / 2 - A[ax], c + w / 2 - A[ax], f, f + I.DOOR_H))
        ln.wall(0.0, ln.L, f, f + 2.1, 0.0, "Wall_Tile", holes, 1.0)


def court_lines(m, rect, y):
    """Badminton court markings (doubles court 6.1 x 13.4 m, lines 40 mm) centred in rect,
    long side along Z."""
    X0, X1, Z0, Z1 = rect
    cx, cz = (X0 + X1) / 2, (Z0 + Z1) / 2
    w, L, t = 6.1, 13.4, 0.04
    xs = [-w / 2, -w / 2 + 0.46, w / 2 - 0.46, w / 2]
    zs = [-L / 2, -L / 2 + 0.76, -1.98, 1.98, L / 2 - 0.76, L / 2]
    for x in xs:
        hrect(m, cx + x - t / 2, cx + x + t / 2, cz - L / 2, cz + L / 2, y, "Court_Line")
    for z in zs:
        hrect(m, cx - w / 2, cx + w / 2, cz + z - t / 2, cz + z + t / 2, y, "Court_Line")
    # Centre lines from the short service lines to the back.
    for za, zb in ((-L / 2, -1.98), (1.98, L / 2)):
        hrect(m, cx - t / 2, cx + t / 2, cz + za, cz + zb, y + 0.0005, "Court_Line")


COURTS = []   # (X, Z) centres of the badminton courts


def civic_shell(C):
    m = C.m
    st = C.stair
    st_holes = [st.hole()] if st is not None else []
    for sp in C.spaces:
        k = sp.level
        if sp.kind == "hall":
            hrect(m, *sp.rect, level_floor(0), sp.floor)
            hrect(m, *sp.rect, level_ceil(C.top), "Ceiling", up=False)
            continue
        holes = st_holes if k > 0 else []
        hrect_cut(m, *sp.rect, level_floor(k), sp.floor, holes)
        hrect_cut(m, *sp.rect, level_ceil(k), "Ceiling", st_holes if k < C.top else [], up=False)
        if sp.kind in TILED:
            tile_skin(m, C, sp)
    outer_walls(m, C)
    for A, B_, k, doors in C.walls:
        partition(m, A, B_, level_floor(k), level_ceil(k), wall_doors_rel(A, B_, doors))
    if st is not None:
        I.stair_geometry(m, [st], C.top, level_floor, level_ceil, side_walls=False)
    if C.hall is not None:
        hall_wall(m, C)
        hx0, hx1, hz0, hz1 = C.hall
        gx = (hx1 - hx0 - 2 * 6.1) / 3
        gz = (hz1 - hz0 - 2 * 13.4) / 3
        for i in range(2):
            for j in range(2):
                X = hx0 + gx * (i + 1) + 6.1 * (i + 0.5)
                Z = hz0 + gz * (j + 1) + 13.4 * (j + 0.5)
                COURTS.append((X, Z))
                court_lines(m, (X - 3.05, X + 3.05, Z - 6.7, Z + 6.7), level_floor(0) + 0.004)
    m.local_bounds_to_solid()
    return m


# ------------------------------------------------------------------ furniture models
# Prop space as in build_interiors: origin on the floor, front towards +Z; wall-mounted items
# have their back on the wall at z = 0.
def goods_rows(m, x0, x1, z0, z1, levels, rnd, mat="Goods", gap=0.03):
    """Packets and boxes standing on shelves at the given heights."""
    for y in levels:
        x = x0
        while x < x1 - 0.06:
            w = rnd.uniform(0.07, 0.2)
            h = rnd.uniform(0.12, 0.3)
            d = rnd.uniform(0.6, 1.0) * (z1 - z0)
            if x + w > x1:
                break
            bx(m, x, x + w, z0, z0 + d, y, y + h, mat if rnd.random() < 0.8 else "Cardboard")
            x += w + rnd.uniform(0.0, gap)


@prop("Shop_Gondola", solid=True)
def p_shop_gondola():
    """Double-sided shop shelving, 1.8 x 0.9 x 1.6 m, centred, stocked on both faces."""
    m = new("Shop_Gondola")
    rnd = random.Random(41)
    bx(m, -0.9, 0.9, -0.03, 0.03, 0.0, 1.6, "Steel")
    bx(m, -0.9, 0.9, -0.45, 0.45, 0.0, 0.1, "Steel")
    for x in (-0.9, 0.88):
        bx(m, x, x + 0.02, -0.45, 0.45, 0.0, 1.6, "Steel")
    for y in (0.1, 0.48, 0.86, 1.24):
        for z0, z1 in ((-0.45, -0.03), (0.03, 0.45)):
            bx(m, -0.88, 0.88, z0, z1, y - 0.02, y, "Steel")
    goods_rows(m, -0.86, 0.86, 0.05, 0.42, (0.1, 0.48, 0.86, 1.24), rnd)
    goods_rows(m, -0.86, 0.86, -0.42, -0.05, (0.1, 0.48, 0.86, 1.24), rnd)
    bx(m, -0.9, 0.9, -0.04, 0.04, 1.6, 1.85, "Sign_Board")
    return m


@prop("Wall_Shelf", solid=True)
def p_wall_shelf():
    """Shop wall shelving against a wall, 1.2 m wide, 0.45 m deep, 2.1 m high, fully stocked."""
    m = new("Wall_Shelf")
    rnd = random.Random(42)
    bx(m, -0.6, 0.6, 0.0, 0.03, 0.0, 2.1, "Laminate")
    for x in (-0.6, 0.58):
        bx(m, x, x + 0.02, 0.0, 0.45, 0.0, 2.1, "Laminate")
    for y in (0.08, 0.5, 0.9, 1.3, 1.7):
        bx(m, -0.58, 0.58, 0.03, 0.45, y - 0.025, y, "Laminate")
    goods_rows(m, -0.56, 0.56, 0.05, 0.42, (0.08, 0.5, 0.9, 1.3, 1.7), rnd)
    return m


@prop("Checkout_Counter", solid=True)
def p_checkout_counter():
    """Shop counter 2.0 x 0.7 m with a glass display front, a till and a card machine; the
    customer side is +Z."""
    m = new("Checkout_Counter")
    rnd = random.Random(43)
    bx(m, -1.0, 1.0, -0.35, -0.3, 0.0, 0.95, "Laminate")
    for x in (-1.0, 0.97):
        bx(m, x, x + 0.03, -0.35, 0.35, 0.0, 0.95, "Laminate")
    bx(m, -1.0, 1.0, -0.35, 0.35, 0.0, 0.08, "Laminate")
    bx(m, -0.97, 0.97, 0.33, 0.35, 0.08, 0.9, "Glass_Clear")
    bx(m, -0.97, 0.97, -0.3, 0.33, 0.45, 0.47, "Glass_Clear")
    goods_rows(m, -0.94, 0.94, -0.25, 0.25, (0.08, 0.47), rnd)
    bx(m, -1.03, 1.03, -0.38, 0.38, 0.95, 0.99, "Laminate")
    bx(m, 0.35, 0.75, -0.25, 0.05, 0.99, 1.07, "Plastic")              # till
    obox(m, 0.55, -0.2, 0.36, 0.04, 0.0, 1.07, 1.32, "Screen")
    bx(m, -0.2, -0.05, 0.05, 0.2, 0.99, 1.02, "Plastic")              # card machine
    bx(m, -0.85, -0.45, -0.1, 0.25, 0.99, 1.2, "Glass_Clear")         # sweets jar stand
    goods_rows(m, -0.84, -0.46, -0.08, 0.22, (0.99,), rnd, "Produce")
    return m


@prop("Drinks_Cooler", solid=True)
def p_drinks_cooler():
    """Glass-door drinks fridge against a wall, 0.7 x 0.65 x 2.0 m, lit inside."""
    m = new("Drinks_Cooler")
    rnd = random.Random(44)
    bx(m, -0.35, 0.35, 0.0, 0.65, 0.0, 0.12, "Plastic")
    bx(m, -0.35, 0.35, 0.0, 0.04, 0.12, 2.0, "Ceramic")
    for x in (-0.35, 0.31):
        bx(m, x, x + 0.04, 0.0, 0.65, 0.12, 2.0, "Ceramic")
    bx(m, -0.35, 0.35, 0.0, 0.65, 1.78, 2.0, "Red_Paint")
    for y in (0.15, 0.55, 0.95, 1.35):
        bx(m, -0.31, 0.31, 0.04, 0.6, y - 0.015, y, "Steel")
        x = -0.29
        while x < 0.26:
            cyl(m, x + 0.035, 0.32, 0.032, y, y + rnd.choice((0.2, 0.25, 0.3)), rnd.choice(("Red_Paint", "Produce", "Glass_Clear", "Goods")), seg=8)
            x += 0.08
    bx(m, -0.31, 0.31, 0.6, 0.62, 0.12, 1.76, "Glass_Clear")
    bx(m, -0.3, 0.3, 0.06, 0.07, 1.7, 1.74, "LightPanel")
    bx(m, 0.25, 0.27, 0.62, 0.66, 0.8, 1.2, "Steel")
    return m


@prop("Photocopier", solid=True)
def p_photocopier():
    """Floor-standing office copier, 0.6 x 0.65 m, with paper trays and a sorter."""
    m = new("Photocopier")
    bx(m, -0.3, 0.3, -0.32, 0.32, 0.05, 0.8, "Ceramic")
    bx(m, -0.28, 0.28, -0.3, 0.3, 0.0, 0.05, "Plastic")
    for y in (0.15, 0.35, 0.55):
        bx(m, -0.28, 0.28, 0.32, 0.33, y, y + 0.16, "Plastic")
    bx(m, -0.3, 0.3, -0.32, 0.32, 0.8, 1.0, "Ceramic")
    bx(m, -0.28, 0.28, -0.3, 0.3, 1.0, 1.03, "Plastic")
    bx(m, 0.0, 0.25, 0.2, 0.33, 1.03, 1.08, "Screen")
    bx(m, -0.45, -0.3, -0.2, 0.2, 0.75, 0.95, "Plastic")
    bx(m, -0.44, -0.31, -0.18, 0.18, 0.95, 0.96, "Linen")
    return m


@prop("Barber_Chair", solid=True)
def p_barber_chair():
    """Hydraulic barber chair on a round chrome base, facing +Z (towards the mirror)."""
    m = new("Barber_Chair")
    cyl(m, 0, 0, 0.3, 0.0, 0.04, "Steel", seg=16)
    cyl(m, 0, 0, 0.07, 0.04, 0.42, "Steel", seg=10)
    bx(m, -0.28, 0.28, -0.25, 0.25, 0.42, 0.55, "Leather")
    bx(m, -0.28, 0.28, -0.32, -0.22, 0.55, 1.2, "Leather")
    bx(m, -0.12, 0.12, -0.36, -0.28, 1.2, 1.38, "Leather")
    for x in (-0.33, 0.27):
        bx(m, x, x + 0.06, -0.2, 0.25, 0.68, 0.73, "Leather")
        bx(m, x + 0.02, x + 0.04, 0.15, 0.2, 0.55, 0.68, "Steel")
    obox(m, 0.0, 0.42, 0.3, 0.12, 0.0, 0.18, 0.48, "Steel")
    bx(m, -0.2, 0.2, 0.42, 0.55, 0.16, 0.19, "Steel")
    return m


@prop("Salon_Station", solid=True)
def p_salon_station():
    """Barber's mirror station on the wall: a 1.1 m mirror, a shelf with bottles, a drawer unit."""
    m = new("Salon_Station")
    rnd = random.Random(45)
    bx(m, -0.6, 0.6, 0.0, 0.02, 1.0, 2.1, "Wood")
    bx(m, -0.55, 0.55, 0.02, 0.025, 1.05, 2.05, "Mirror")
    bx(m, -0.6, 0.6, 0.0, 0.3, 0.86, 0.9, "Laminate")
    bx(m, -0.55, -0.05, 0.0, 0.28, 0.0, 0.86, "Laminate")
    for i in range(8):
        x = -0.5 + i * 0.13
        cyl(m, x, 0.15, 0.03, 0.9, 0.9 + rnd.uniform(0.12, 0.24), rnd.choice(("Red_Paint", "Glass_Clear", "Plastic", "Ceramic")), seg=8)
    bx(m, 0.2, 0.45, 0.05, 0.25, 0.9, 0.95, "Linen")
    return m


@prop("Fruit_Stand", solid=True)
def p_fruit_stand():
    """Three tiers of wooden fruit crates, 1.6 m wide, front at +Z."""
    m = new("Fruit_Stand")
    rnd = random.Random(46)
    for k in range(3):
        z0, z1 = -0.45 + k * 0.0, 0.45 - k * 0.28
        y0 = 0.3 + k * 0.32
        bx(m, -0.8, 0.8, z0 if k == 0 else 0.45 - (k + 1) * 0.28, z1, 0.0 if k == 0 else y0 - 0.32, y0, "Wood")
        for i in range(4):
            x0 = -0.78 + i * 0.395
            za = (0.45 - (k + 1) * 0.28) if k else 0.17
            zb = z1 - 0.02
            bx(m, x0, x0 + 0.38, za, zb, y0, y0 + 0.12, "Wood", top=False)
            for j in range(6):
                cx = x0 + 0.05 + (j % 3) * 0.13
                cz = za + 0.06 + (j // 3) * max(0.05, (zb - za - 0.12))
                cyl(m, cx, cz, 0.055, y0 + 0.06, y0 + 0.16, rnd.choice(("Produce", "Produce", "Red_Paint", "Foliage", "Paint_Yellow")), seg=8)
    return m


@prop("ATM_Machine", solid=True)
def p_atm_machine():
    """Through-the-wall cash machine against a wall: screen, keypad, card slot, a canopy."""
    m = new("ATM_Machine")
    bx(m, -0.45, 0.45, 0.0, 0.75, 0.0, 1.75, "Steel")
    bx(m, -0.45, 0.45, 0.0, 0.85, 1.75, 1.95, "Sign_Board")
    bx(m, -0.38, 0.38, 0.75, 0.77, 0.2, 0.8, "Plastic")
    obox(m, 0.0, 0.79, 0.4, 0.04, 0.0, 1.15, 1.45, "Screen")
    bx(m, -0.2, 0.2, 0.7, 0.95, 0.98, 1.0, "Plastic")
    for i in range(3):
        for j in range(4):
            bx(m, -0.15 + i * 0.1, -0.08 + i * 0.1, 0.74 + j * 0.05, 0.78 + j * 0.05, 1.0, 1.015, "Steel")
    bx(m, 0.25, 0.35, 0.76, 0.78, 1.05, 1.08, "LightPanel")
    bx(m, -0.3, 0.3, 0.76, 0.78, 0.88, 0.92, "Metal")
    return m


@prop("Bank_Counter", solid=True)
def p_bank_counter():
    """Teller counter 2.4 m long with a glass screen and a speaking slot, staff side -Z."""
    m = new("Bank_Counter")
    bx(m, -1.2, 1.2, 0.2, 0.3, 0.0, 1.05, "Wood")
    bx(m, -1.25, 1.25, -0.1, 0.42, 1.05, 1.1, "Laminate")
    bx(m, -1.2, 1.2, -0.5, 0.2, 0.72, 0.75, "Laminate")
    for x in (-1.2, 1.17):
        bx(m, x, x + 0.03, -0.5, 0.3, 0.0, 1.05, "Wood")
    bx(m, -1.2, 1.2, 0.18, 0.2, 1.3, 2.2, "Glass_Clear")
    bx(m, -1.2, 1.2, 0.18, 0.2, 1.1, 1.18, "Steel")
    for x in (-1.21, -0.02, 1.18):
        bx(m, x, x + 0.04, 0.17, 0.21, 1.1, 2.2, "Steel")
    bx(m, -1.2, 1.2, 0.16, 0.22, 2.2, 2.3, "Sign_Board")
    for x in (-0.6, 0.6):
        obox(m, x, -0.2, 0.42, 0.04, 0.0, 0.85, 1.15, "Screen")
        bx(m, x - 0.2, x + 0.2, -0.05, 0.1, 0.75, 0.77, "Plastic")
    return m


@prop("Clothes_Rail", solid=True)
def p_clothes_rail():
    """A wheeled rail of pressed clothes on hangers, 1.5 m long, centred."""
    m = new("Clothes_Rail")
    rnd = random.Random(47)
    for x in (-0.75, 0.73):
        bx(m, x, x + 0.03, -0.015, 0.015, 0.05, 1.7, "Steel")
        bx(m, x - 0.1, x + 0.13, -0.25, 0.25, 0.03, 0.06, "Steel")
    bx(m, -0.75, 0.76, -0.015, 0.015, 1.68, 1.71, "Steel")
    x = -0.68
    while x < 0.66:
        mat = rnd.choice(("Fabric", "Linen", "Bedsheet", "Red_Paint", "Velvet"))
        bx(m, x, x + 0.03, -0.24, 0.24, rnd.uniform(0.75, 1.05), 1.62, mat)
        x += rnd.uniform(0.045, 0.08)
    return m


@prop("Ironing_Table", solid=True)
def p_ironing_table():
    """The press-wallah's long padded table with an iron and stacks of folded clothes."""
    m = new("Ironing_Table")
    rnd = random.Random(48)
    for x in (-0.85, 0.82):
        for z in (-0.38, 0.35):
            bx(m, x, x + 0.03, z, z + 0.03, 0.0, 0.84, "Wood")
    bx(m, -0.9, 0.9, -0.42, 0.42, 0.84, 0.88, "Wood")
    bx(m, -0.88, 0.88, -0.4, 0.4, 0.88, 0.9, "Linen")
    bx(m, -0.1, 0.12, -0.05, 0.08, 0.9, 1.0, "Metal")
    bx(m, -0.04, 0.06, -0.03, 0.06, 1.0, 1.05, "Plastic")
    for k, x in enumerate((-0.7, -0.45, 0.45, 0.7)):
        h = rnd.uniform(0.1, 0.25)
        bx(m, x - 0.12, x + 0.12, -0.2, 0.1, 0.9, 0.9 + h, ("Fabric", "Linen", "Bedsheet", "Fabric")[k])
    bx(m, -0.8, 0.8, -0.38, 0.38, 0.2, 0.22, "Wood")
    for x in (-0.5, 0.0, 0.5):
        bx(m, x - 0.2, x + 0.2, -0.25, 0.25, 0.22, 0.45, "Cardboard")
    return m


@prop("Sewing_Table", solid=True)
def p_sewing_table():
    """Tailor's table with a treadle sewing machine and a stool, front +Z."""
    m = new("Sewing_Table")
    bx(m, -0.5, 0.5, -0.25, 0.25, 0.74, 0.78, "Wood")
    for x in (-0.48, 0.45):
        bx(m, x, x + 0.03, -0.23, 0.23, 0.0, 0.74, "Metal")
    obox(m, 0.0, 0.0, 0.5, 0.06, 0.25, 0.08, 0.12, "Metal")       # treadle
    bx(m, -0.25, 0.2, -0.08, 0.08, 0.78, 0.86, "Metal")
    bx(m, 0.1, 0.2, -0.08, 0.08, 0.86, 1.08, "Metal")
    bx(m, -0.25, 0.2, -0.06, 0.06, 1.0, 1.08, "Metal")
    bx(m, -0.22, -0.18, -0.02, 0.02, 0.86, 0.95, "Steel")
    cyl(m, 0.24, 0.0, 0.05, 0.92, 0.97, "Steel", seg=10)
    bx(m, -0.45, -0.3, -0.2, 0.0, 0.78, 0.8, "Fabric")
    cyl(m, 0.0, 0.55, 0.18, 0.44, 0.48, "Wood", seg=12)
    for a in range(3):
        ang = a * 2.0944
        bx(m, 0.13 * math.cos(ang) - 0.015, 0.13 * math.cos(ang) + 0.015,
           0.55 + 0.13 * math.sin(ang) - 0.015, 0.55 + 0.13 * math.sin(ang) + 0.015, 0.0, 0.44, "Wood")
    return m


# Medical.
@prop("Hospital_Bed", solid=True)
def p_hospital_bed():
    """Ward bed 0.95 x 2.05 m, head against the wall at z = 0: steel frame on castors, raised
    back rest, side rails, a folded blanket."""
    m = new("Hospital_Bed")
    for x in (-0.45, 0.42):
        for z in (0.1, 1.95):
            bx(m, x, x + 0.03, z - 0.015, z + 0.015, 0.08, 0.5, "Steel")
            cyl(m, x + 0.015, z, 0.05, 0.0, 0.08, "Plastic", seg=8)
    bx(m, -0.47, 0.47, 0.08, 2.0, 0.48, 0.52, "Steel")
    bx(m, -0.45, 0.45, 0.85, 2.0, 0.52, 0.66, "Linen")
    obox(m, 0.0, 0.47, 0.9, 0.8, math.pi / 2, 0.5, 0.56, "Linen")      # mattress under the back rest
    m2 = 0.0
    # Back rest raised 30 degrees: a sloping slab, and a pillow on it.
    pts = [(-0.45, 0.1, 0.62), (0.45, 0.1, 0.62), (0.45, 0.85, 0.62), (-0.45, 0.85, 0.62)]
    lift = [(x, z, y + (0.85 - z) * 0.55) for x, z, y in pts]
    m.face([(*m.b(x, z), y) for x, z, y in lift], "Linen", (0, 0, 1))
    bx(m, -0.3, 0.3, 0.15, 0.45, 0.95, 1.05, "Ceramic")
    bx(m, -0.44, 0.44, 1.5, 1.9, 0.66, 0.72, "Bedsheet")
    bx(m, -0.47, 0.47, 0.0, 0.05, 0.3, 1.15, "Steel")
    bx(m, -0.47, 0.47, 2.0, 2.05, 0.3, 0.9, "Steel")
    for x in (-0.5, 0.47):
        bx(m, x, x + 0.03, 0.6, 1.5, 0.82, 0.85, "Steel")
        for z in (0.65, 1.05, 1.45):
            bx(m, x, x + 0.03, z, z + 0.03, 0.52, 0.82, "Steel")
    del m2
    return m


@prop("Bedside_Locker", solid=True)
def p_bedside_locker():
    m = new("Bedside_Locker")
    bx(m, -0.22, 0.22, 0.0, 0.42, 0.05, 0.8, "Steel")
    bx(m, -0.2, 0.2, 0.42, 0.43, 0.35, 0.75, "Steel")
    bx(m, -0.05, 0.05, 0.43, 0.45, 0.6, 0.62, "Plastic")
    cyl(m, 0.1, 0.2, 0.035, 0.8, 0.98, "Glass_Clear", seg=8)
    return m


@prop("IV_Stand")
def p_iv_stand():
    m = new("IV_Stand")
    for a in range(5):
        ang = a * 2 * math.pi / 5
        obox(m, 0.13 * math.cos(ang), 0.13 * math.sin(ang), 0.27, 0.03, ang, 0.03, 0.06, "Steel")
        cyl(m, 0.26 * math.cos(ang), 0.26 * math.sin(ang), 0.025, 0.0, 0.04, "Plastic", seg=6)
    cyl(m, 0, 0, 0.012, 0.06, 1.95, "Steel", seg=6)
    bx(m, -0.18, 0.18, -0.01, 0.01, 1.92, 1.94, "Steel")
    bx(m, 0.08, 0.18, -0.03, 0.03, 1.62, 1.88, "Glass_Clear")
    return m


@prop("Exam_Couch", solid=True)
def p_exam_couch():
    """Doctor's examination couch 0.65 x 1.85 m with a paper roll and a step, centred, long
    side along X."""
    m = new("Exam_Couch")
    for x in (-0.88, 0.85):
        for z in (-0.3, 0.27):
            bx(m, x, x + 0.03, z, z + 0.03, 0.0, 0.62, "Steel")
    bx(m, -0.92, 0.92, -0.32, 0.32, 0.62, 0.74, "Leather")
    obox(m, -0.78, 0.0, 0.32, 0.64, 0.0, 0.74, 0.86, "Leather")
    bx(m, -0.6, 0.92, -0.3, 0.3, 0.74, 0.745, "Linen")
    cyl(m, 0.95, 0.0, 0.06, 0.66, 0.7, "Linen", seg=8)
    bx(m, -0.3, 0.2, 0.36, 0.66, 0.0, 0.2, "Steel")
    return m


@prop("Privacy_Screen")
def p_privacy_screen():
    """Three-panel folding hospital screen, 1.8 m wide, curtain fabric in steel frames."""
    m = new("Privacy_Screen")
    for k, ang in enumerate((0.35, 0.0, -0.35)):
        cx = (k - 1) * 0.6
        cz = 0.1 * abs(k - 1)
        obox(m, cx, cz, 0.58, 0.02, ang, 0.25, 1.75, "Linen")
        for e in (-0.29, 0.29):
            ex, ez = cx + e * math.cos(ang), cz + e * math.sin(ang)
            cyl(m, ex, ez, 0.012, 0.0, 1.8, "Steel", seg=6)
    return m


@prop("Wheelchair", solid=True)
def p_wheelchair():
    m = new("Wheelchair")
    for x in (-0.31, 0.28):
        cyl_x = x + 0.015
        for k in range(10):
            ang = k * math.pi / 5
            obox(m, cyl_x, -0.1 + 0.29 * math.cos(ang), 0.03, 0.18, ang + math.pi / 2, 0.3 + 0.29 * math.sin(ang) - 0.015,
                 0.3 + 0.29 * math.sin(ang) + 0.015, "Plastic")
        bx(m, x, x + 0.03, -0.25, 0.3, 0.45, 0.48, "Steel")
        bx(m, x, x + 0.03, -0.3, -0.27, 0.48, 0.95, "Steel")
        bx(m, x, x + 0.03, 0.27, 0.3, 0.05, 0.48, "Steel")
        cyl(m, x + 0.015, 0.3, 0.04, 0.0, 0.05, "Plastic", seg=8)
    bx(m, -0.28, 0.28, -0.25, 0.25, 0.48, 0.52, "Leather")
    bx(m, -0.28, 0.28, -0.3, -0.28, 0.52, 0.9, "Leather")
    bx(m, -0.22, 0.22, 0.32, 0.45, 0.08, 0.1, "Steel")
    return m


@prop("Medicine_Shelf", solid=True)
def p_medicine_shelf():
    """Pharmacy wall shelving packed with medicine boxes, 1.2 m wide, 0.35 m deep, 2.2 m high."""
    m = new("Medicine_Shelf")
    rnd = random.Random(49)
    bx(m, -0.6, 0.6, 0.0, 0.02, 0.0, 2.2, "Laminate")
    for x in (-0.6, 0.58):
        bx(m, x, x + 0.02, 0.0, 0.35, 0.0, 2.2, "Laminate")
    lv = [0.1 + 0.3 * i for i in range(7)]
    for y in lv:
        bx(m, -0.58, 0.58, 0.02, 0.35, y - 0.02, y, "Laminate")
    for y in lv:
        x = -0.56
        while x < 0.52:
            w = rnd.uniform(0.04, 0.1)
            h = rnd.uniform(0.08, 0.2)
            bx(m, x, x + w, 0.04, rnd.uniform(0.18, 0.32), y, y + h, rnd.choice(("Ceramic", "Goods", "Cardboard", "Ceramic")))
            x += w + 0.01
    return m


@prop("Medical_Cabinet", solid=True)
def p_medical_cabinet():
    """Glass-fronted steel cabinet of dressings and bottles, 0.9 x 0.4 x 1.8 m, against a wall."""
    m = new("Medical_Cabinet")
    rnd = random.Random(50)
    bx(m, -0.45, 0.45, 0.0, 0.4, 0.0, 0.1, "Ceramic")
    bx(m, -0.45, 0.45, 0.0, 0.02, 0.1, 1.8, "Ceramic")
    for x in (-0.45, 0.43):
        bx(m, x, x + 0.02, 0.0, 0.4, 0.1, 1.8, "Ceramic")
    bx(m, -0.45, 0.45, 0.0, 0.4, 1.78, 1.8, "Ceramic")
    for y in (0.5, 0.9, 1.3):
        bx(m, -0.43, 0.43, 0.02, 0.38, y - 0.01, y, "Glass_Clear")
        for i in range(7):
            cyl(m, -0.36 + i * 0.12, 0.2, 0.04, y, y + rnd.uniform(0.1, 0.22), rnd.choice(("Glass_Clear", "Ceramic", "Red_Paint")), seg=8)
    bx(m, -0.43, 0.43, 0.38, 0.39, 0.12, 1.76, "Glass_Clear")
    bx(m, -0.01, 0.01, 0.39, 0.4, 0.12, 1.76, "Steel")
    bx(m, -0.06, 0.06, 0.4, 0.41, 1.55, 1.67, "Red_Paint")
    return m


@prop("Waiting_Chairs", solid=True)
def p_waiting_chairs():
    """Row of four steel beam chairs, 2.2 m long, seats facing +Z."""
    m = new("Waiting_Chairs")
    bx(m, -1.1, 1.1, -0.05, 0.05, 0.38, 0.42, "Steel")
    for x in (-0.95, 0.92):
        bx(m, x, x + 0.04, -0.25, 0.25, 0.0, 0.03, "Steel")
        bx(m, x, x + 0.04, -0.02, 0.02, 0.03, 0.4, "Steel")
    for i in range(4):
        x = -0.825 + i * 0.55
        bx(m, x - 0.24, x + 0.24, -0.2, 0.25, 0.42, 0.46, "Steel")
        obox(m, x, -0.24, 0.48, 0.04, 0.0, 0.5, 0.88, "Steel")
        for xx in (x - 0.26, x + 0.24):
            bx(m, xx, xx + 0.02, -0.15, 0.2, 0.6, 0.62, "Plastic")
    return m


@prop("Eye_Chart")
def p_eye_chart():
    m = new("Eye_Chart")
    bx(m, -0.25, 0.25, 0.0, 0.015, 1.2, 2.0, "Whiteboard")
    for k in range(7):
        w = 0.2 - k * 0.025
        bx(m, -w, w, 0.015, 0.018, 1.9 - k * 0.1, 1.9 - k * 0.1 + 0.05 - k * 0.005, "Plastic")
    return m


# Sports.
@prop("Badminton_Net")
def p_badminton_net():
    """Posts on weighted bases and a 6.1 m net, centred, net along X."""
    m = new("Badminton_Net")
    for x in (-3.1, 3.1):
        cyl(m, x, 0.0, 0.2, 0.0, 0.08, "Metal", seg=12)
        cyl(m, x, 0.0, 0.025, 0.08, 1.55, "Steel", seg=8)
    bx(m, -3.05, 3.05, -0.005, 0.005, 0.79, 1.52, "Net")
    bx(m, -3.05, 3.05, -0.012, 0.012, 1.48, 1.55, "Linen")
    return m


@prop("High_Bay_Light")
def p_high_bay_light():
    """Sports-hall lamp hanging 1 m from the ceiling (origin at the ceiling)."""
    m = new("High_Bay_Light")
    cyl(m, 0, 0, 0.01, -1.0, 0.0, "Steel", seg=6)
    cyl(m, 0, 0, 0.14, -1.15, -1.0, "Metal", seg=12)
    cyl(m, 0, 0, 0.32, -1.35, -1.15, "Metal", top=None, seg=16)
    cyl(m, 0, 0, 0.3, -1.36, -1.35, "LightPanel", seg=16)
    return m


@prop("Scoreboard")
def p_scoreboard():
    m = new("Scoreboard")
    bx(m, -1.0, 1.0, 0.0, 0.12, 3.6, 4.6, "Plastic")
    bx(m, -0.95, 0.95, 0.12, 0.13, 3.65, 4.55, "Screen")
    for x in (-0.6, 0.35):
        for i in range(2):
            bx(m, x + i * 0.13, x + i * 0.13 + 0.1, 0.13, 0.14, 3.8, 4.15, "Lamp_Glow")
    bx(m, -0.15, 0.15, 0.13, 0.14, 4.3, 4.45, "Red_Paint")
    return m


@prop("Treadmill", solid=True)
def p_treadmill():
    """Treadmill 0.8 x 1.9 m, the console at the front (+Z), the runner faces +Z."""
    m = new("Treadmill")
    bx(m, -0.4, 0.4, -0.95, 0.75, 0.0, 0.18, "Plastic")
    bx(m, -0.25, 0.25, -0.92, 0.7, 0.18, 0.19, "Rubber")
    for x in (-0.38, 0.33):
        obox(m, x + 0.025, 0.78, 0.05, 0.3, math.pi / 2 - 0.2, 0.18, 1.2, "Steel")
        bx(m, x, x + 0.05, 0.2, 0.85, 0.95, 1.0, "Plastic")
    bx(m, -0.38, 0.38, 0.8, 0.95, 1.2, 1.45, "Plastic")
    obox(m, 0.0, 0.86, 0.5, 0.02, 0.0, 1.25, 1.42, "Screen")
    return m


@prop("Exercise_Bike", solid=True)
def p_exercise_bike():
    m = new("Exercise_Bike")
    bx(m, -0.3, 0.3, -0.55, -0.45, 0.0, 0.05, "Steel")
    bx(m, -0.3, 0.3, 0.45, 0.55, 0.0, 0.05, "Steel")
    bx(m, -0.04, 0.04, -0.5, 0.5, 0.05, 0.12, "Steel")
    cyl(m, 0.0, 0.3, 0.06, 0.12, 0.6, "Plastic", seg=10)
    obox(m, 0.0, 0.32, 0.4, 0.06, math.pi / 2, 0.15, 0.55, "Plastic")
    bx(m, -0.04, 0.04, -0.3, -0.24, 0.12, 0.85, "Steel")
    bx(m, -0.13, 0.13, -0.42, -0.18, 0.85, 0.92, "Leather")
    bx(m, -0.04, 0.04, 0.38, 0.44, 0.12, 1.05, "Steel")
    bx(m, -0.25, 0.25, 0.4, 0.47, 1.05, 1.09, "Rubber")
    bx(m, -0.1, 0.1, 0.44, 0.48, 1.1, 1.22, "Screen")
    for x in (-0.14, 0.12):
        bx(m, x, x + 0.02, -0.02, 0.02, 0.3, 0.45, "Steel")
    return m


@prop("Weight_Bench", solid=True)
def p_weight_bench():
    """Flat bench with a barbell on uprights, bench along Z."""
    m = new("Weight_Bench")
    bx(m, -0.15, 0.15, -0.6, 0.6, 0.38, 0.45, "Leather")
    for z in (-0.5, 0.45):
        bx(m, -0.12, 0.12, z, z + 0.05, 0.0, 0.38, "Steel")
        bx(m, -0.25, 0.25, z, z + 0.05, 0.0, 0.03, "Steel")
    for x in (-0.45, 0.42):
        bx(m, x, x + 0.04, -0.6, -0.56, 0.0, 1.15, "Steel")
        bx(m, x, x + 0.04, -0.66, -0.5, 0.0, 0.03, "Steel")
    cyl_bar = 1.12
    bx(m, -1.0, 1.0, -0.6, -0.57, cyl_bar - 0.015, cyl_bar + 0.015, "Steel")
    for x in (-0.85, 0.75):
        bx(m, x, x + 0.1, -0.79, -0.38, cyl_bar - 0.21, cyl_bar + 0.21, "Rubber")
    return m


@prop("Dumbbell_Rack", solid=True)
def p_dumbbell_rack():
    """Two-tier dumbbell rack 1.8 m long against a wall."""
    m = new("Dumbbell_Rack")
    for x in (-0.9, 0.86):
        bx(m, x, x + 0.04, 0.05, 0.55, 0.0, 0.85, "Steel")
    for y, z0, z1 in ((0.45, 0.05, 0.3), (0.8, 0.3, 0.55)):
        obox(m, 0.0, (z0 + z1) / 2, 1.8, z1 - z0, 0.0, y - 0.03, y, "Steel")
    for i in range(9):
        x = -0.8 + i * 0.2
        for y, zc in ((0.45, 0.17), (0.8, 0.42)):
            for e in (-0.09, 0.09):
                cyl(m, x, zc + e, 0.05 + i * 0.004, y, y + 0.06, "Rubber", seg=8)
            bx(m, x - 0.015, x + 0.015, zc - 0.07, zc + 0.07, y + 0.015, y + 0.045, "Steel")
    return m


@prop("Power_Rack", solid=True)
def p_power_rack():
    """Squat cage 1.2 x 1.2 x 2.2 m with a bar and plates on the pegs."""
    m = new("Power_Rack")
    for x in (-0.6, 0.55):
        for z in (-0.6, 0.55):
            bx(m, x, x + 0.05, z, z + 0.05, 0.0, 2.2, "Metal")
        bx(m, x, x + 0.05, -0.6, 0.6, 2.15, 2.2, "Metal")
        bx(m, x, x + 0.05, -0.6, 0.6, 0.0, 0.05, "Metal")
    for z in (-0.6, 0.55):
        bx(m, -0.6, 0.6, z, z + 0.05, 2.15, 2.2, "Metal")
    bx(m, -1.05, 1.05, -0.04, -0.01, 1.35, 1.38, "Steel")
    for x in (-0.95, 0.88):
        bx(m, x, x + 0.06, -0.25, 0.2, 1.15, 1.58, "Rubber")
    for x in (-0.66, 0.66):
        for k in range(3):
            bx(m, x - 0.03, x + 0.03, 0.6 + 0.06 * k, 0.65 + 0.06 * k, 0.1, 0.55 - k * 0.06, "Rubber")
    bx(m, -0.5, 0.5, -0.5, 0.5, 0.0, 0.02, "Wood")
    return m


@prop("Gym_Mat")
def p_gym_mat():
    m = new("Gym_Mat")
    bx(m, -0.9, 0.9, -0.5, 0.5, 0.0, 0.04, "Fabric")
    return m


@prop("Yoga_Mat")
def p_yoga_mat():
    m = new("Yoga_Mat")
    bx(m, -0.3, 0.3, -0.9, 0.9, 0.0, 0.008, "Velvet")
    return m


@prop("Locker_Bank", solid=True)
def p_locker_bank():
    """Six steel lockers, 1.8 m wide, 0.5 m deep, 1.9 m high, against a wall."""
    m = new("Locker_Bank")
    bx(m, -0.9, 0.9, 0.0, 0.5, 0.0, 1.9, "Steel", top=True)
    for i in range(6):
        x = -0.9 + i * 0.3
        for y0, y1 in ((0.1, 0.95), (1.0, 1.85)):
            bx(m, x + 0.01, x + 0.29, 0.5, 0.505, y0, y1, "Paint_Yellow" if (i + (y0 > 0.5)) % 4 == 0 else "Steel")
            bx(m, x + 0.24, x + 0.26, 0.505, 0.52, y0 + 0.35, y0 + 0.45, "Metal")
            for v in range(3):
                bx(m, x + 0.08, x + 0.2, 0.505, 0.51, y1 - 0.12 - v * 0.04, y1 - 0.1 - v * 0.04, "Metal")
    return m


@prop("Ball_Cart", solid=True)
def p_ball_cart():
    """Steel cage cart of footballs, volleyballs and basketballs."""
    m = new("Ball_Cart")
    rnd = random.Random(51)
    bx(m, -0.45, 0.45, -0.3, 0.3, 0.12, 0.15, "Steel")
    for x in (-0.45, 0.42):
        for z in (-0.3, 0.27):
            bx(m, x, x + 0.03, z, z + 0.03, 0.12, 0.95, "Steel")
            cyl(m, x + 0.015, z + 0.015, 0.04, 0.0, 0.08, "Plastic", seg=8)
    for y in (0.5, 0.95):
        for z in (-0.3, 0.27):
            bx(m, -0.45, 0.45, z, z + 0.03, y - 0.015, y + 0.015, "Steel")
        for x in (-0.45, 0.42):
            bx(m, x, x + 0.03, -0.3, 0.3, y - 0.015, y + 0.015, "Steel")
    for i in range(10):
        cx, cz = -0.32 + (i % 4) * 0.21, -0.16 + (i // 4 % 2) * 0.3
        cy = 0.27 + (i // 8) * 0.22
        cyl(m, cx, cz, 0.11, cy - 0.1, cy + 0.1, rnd.choice(("Paint_Yellow", "Red_Paint", "Linen", "Produce")), seg=10)
    return m


@prop("Trophy_Cabinet", solid=True)
def p_trophy_cabinet():
    """Glass trophy cabinet 1.6 m wide against a wall, cups and shields on three shelves."""
    m = new("Trophy_Cabinet")
    rnd = random.Random(52)
    bx(m, -0.8, 0.8, 0.0, 0.45, 0.0, 0.7, "Wood")
    bx(m, -0.8, 0.8, 0.0, 0.03, 0.7, 2.1, "Wood")
    for x in (-0.8, 0.77):
        bx(m, x, x + 0.03, 0.0, 0.45, 0.7, 2.1, "Wood")
    bx(m, -0.8, 0.8, 0.0, 0.45, 2.1, 2.16, "Wood")
    for y in (0.7, 1.15, 1.6):
        bx(m, -0.77, 0.77, 0.03, 0.43, y, y + 0.02, "Glass_Clear")
        for i in range(5):
            x = -0.6 + i * 0.3
            h = rnd.uniform(0.18, 0.35)
            if rnd.random() < 0.7:
                cyl(m, x, 0.2, 0.04, y + 0.02, y + 0.08, "Wood", seg=8)
                cyl(m, x, 0.2, 0.015, y + 0.08, y + h - 0.1, "Paint_Yellow", seg=6)
                cyl(m, x, 0.2, 0.07, y + h - 0.1, y + h, "Paint_Yellow", seg=10)
            else:
                bx(m, x - 0.1, x + 0.1, 0.06, 0.08, y + 0.02, y + h, "Wood")
                bx(m, x - 0.06, x + 0.06, 0.08, 0.085, y + 0.08, y + h - 0.06, "Steel")
    bx(m, -0.77, 0.77, 0.43, 0.44, 0.72, 2.08, "Glass_Clear")
    return m


@prop("Chess_Table", solid=True)
def p_chess_table():
    m = new("Chess_Table")
    bx(m, -0.35, 0.35, -0.35, 0.35, 0.7, 0.74, "Wood")
    bx(m, -0.04, 0.04, -0.04, 0.04, 0.0, 0.7, "Metal")
    bx(m, -0.3, 0.3, -0.3, 0.3, 0.0, 0.03, "Metal")
    for i in range(8):
        for j in range(8):
            if (i + j) % 2 == 0:
                bx(m, -0.24 + i * 0.06, -0.18 + i * 0.06, -0.24 + j * 0.06, -0.18 + j * 0.06, 0.74, 0.742, "Plastic")
    for i in range(8):
        for j in (0, 1, 6, 7):
            cyl(m, -0.21 + i * 0.06, -0.21 + j * 0.06, 0.018, 0.742, 0.742 + (0.06 if j in (1, 6) else 0.08),
                "Linen" if j < 2 else "Plastic", seg=6)
    return m


# ------------------------------------------------------------------ furnishing
DIRS = {"N": (0.0, 1.0), "S": (0.0, -1.0), "E": (1.0, 0.0), "W": (-1.0, 0.0)}
WO = PART / 2 + 0.01        # a wall item's offset from a partition's centre line
OO = 0.02                   # ... and from an outer wall's inside face


def P(name, X, Z, y, face="N"):
    """Put a prop at world (X, Z), its front facing the given compass direction."""
    d = DIRS[face]
    put(name, X, Z, y, d[0], d[1])


def row(name, X0, Z0, X1, Z1, n, y, face):
    """n props evenly spaced from (X0, Z0) to (X1, Z1)."""
    for i in range(n):
        t = 0.5 if n == 1 else i / (n - 1)
        P(name, X0 + (X1 - X0) * t, Z0 + (Z1 - Z0) * t, y, face)


def lights(rect, k, per=30.0, color=WARM, intensity=1.1):
    I.room_lights(rect, y=level_ceil(k) - 0.25, per=per, color=color, intensity=intensity)


def panels(rect, k, step=3.0, name="Light_Panel", margin=1.0):
    I.ceiling_grid(rect, step=step, prop_name=name, y=level_ceil(k), margin=margin)


def fans(rect, k, step=4.0):
    X0, X1, Z0, Z1 = rect
    nx_, nz_ = max(1, round((X1 - X0) / step)), max(1, round((Z1 - Z0) / step))
    for i in range(nx_):
        for j in range(nz_):
            P("Ceiling_Fan", X0 + (X1 - X0) * (i + 0.5) / nx_, Z0 + (Z1 - Z0) * (j + 0.5) / nz_, level_ceil(k))


def toilet(rect, k, door_side="E"):
    """Two WCs against the north wall and a basin counter on the south wall."""
    X0, X1, Z0, Z1 = rect
    y = level_floor(k)
    for X in (X0 + 0.6, X0 + 1.65):
        P("Toilet_Cubicle", X, Z1 - OO - 1.5, y, "S")
    P("Urinal", X0 + 2.8, Z1 - OO, y, "S")
    P("Basin_Counter", X0 + 1.55, Z0 + WO, y, "N")
    P("Dustbin", X1 - 0.6, Z0 + 0.35, y)
    P("Tube_Light", (X0 + X1) / 2, (Z0 + Z1) / 2, level_ceil(k), "E")
    lights(rect, k, per=40.0, color=COOL, intensity=0.8)


def wheel_corner(X, Z, y):
    P("Wheelchair", X, Z, y, "E")


def door_leaves(C, k):
    """Wooden door leaves standing open in the narrow doorways of level k."""
    for A, B_, lv, doors in C.walls:
        if lv != k:
            continue
        ax = 0 if abs(A[1] - B_[1]) < 1e-6 else 1
        for c, w in doors:
            if w > 1.3:
                continue
            t = (1.0, 0.0) if ax == 0 else (0.0, 1.0)
            n = (0.0, 1.0) if ax == 0 else (1.0, 0.0)
            pt = (c, A[1]) if ax == 0 else (A[0], c)
            hx = pt[0] - t[0] * (w / 2 - 0.03) + n[0] * (PART / 2)
            hz = pt[1] - t[1] * (w / 2 - 0.03) + n[1] * (PART / 2)
            put("Door_Leaf", hx, hz, level_floor(k), yaw=round(math.degrees(math.atan2(-n[1], n[0])), 2))


def entrance_leaves(C):
    """Glass leaves standing open in the facade doorways (shop fronts keep their shutters)."""
    x0, x1, z0, z1 = C.inner
    for side, lv in C.ops.items():
        for a0, a1, zz0, zz1, d in lv.get(0, []):
            if not d or abs(zz1 - B.DOOR_TOP) > 0.01:
                continue
            line = {"S": z0, "N": z1, "W": x0, "E": x1}[side]
            n = {"S": (0.0, 1.0), "N": (0.0, -1.0), "W": (1.0, 0.0), "E": (-1.0, 0.0)}[side]
            for a in (a0 + 0.06, a1 - 0.06):
                X, Z = (a, line) if side in "NS" else (line, a)
                put("Door_Glass", X, Z, FL, yaw=round(math.degrees(math.atan2(-n[1], n[0])), 2))


def exit_signs(C):
    x0, x1, z0, z1 = C.inner
    for side, lv in C.ops.items():
        for a0, a1, zz0, zz1, d in lv.get(0, []):
            if d:
                line = {"S": z0 + OO, "N": z1 - OO, "W": x0 + OO, "E": x1 - OO}[side]
                face = {"S": "N", "N": "S", "W": "E", "E": "W"}[side]
                X, Z = ((a0 + a1) / 2, line) if side in "NS" else (line, (a0 + a1) / 2)
                P("Exit_Sign", X, Z, zz1 + 0.05, face)


def furnish_block1(C):
    x0, x1, z0, z1 = C.inner
    xc, xp = -73.0, -70.5
    for k in ALL:
        y = level_floor(k)
        # Stair hall: wheelchairs parked beside the stair, an extinguisher, a light.
        P("Wheelchair", -74.3, -39.8, y, "N")
        if k == 0:
            P("Wheelchair", -74.3, -38.6, y, "N")
        P("Fire_Extinguisher", xc - WO, -36.5, y, "W")
        P("Notice_Board", xc - WO, -39.2, y, "W")
        lights((x0, xc, z0, -30.75), k, per=40.0)
        P("Tube_Light", -74.3, -33.0, level_ceil(k), "N")
        toilet((x0, xc, -15.75, z1), k)
    y = level_floor(0)
    # Waiting hall: reception by the entrance, rows of chairs facing a TV, water, plants.
    P("Reception_Counter", -71.3, -38.2, y, "E")
    P("Chair_Office", -72.4, -38.2, y, "E")
    P("Filing_Cabinet", xc + WO + 0.3, -40.5, y, "E")
    for Z in (-31.4, -29.0, -26.6):
        for X in (-70.0, -67.4):
            P("Waiting_Chairs", X, Z, y, "N")
    P("TV_Wall", -68.7, -23.25 - WO, y, "S")
    P("Water_Cooler", x1 - 0.4, -24.3, y, "W")
    P("Plant_Pot", x1 - 0.45, -36.0, y)
    P("Plant_Pot", x1 - 0.45, -29.4, y)
    P("Notice_Board", -72.3, -23.25 - WO, y, "S")
    P("Wall_Clock", -65.2, -23.25 - WO, y, "S")
    P("Wheelchair", -64.4, -40.6, y, "W")
    P("Dustbin", x1 - 0.35, -40.9, y)
    fans((xc, x1, z0, -23.25), 0, 4.5)
    panels((xc, x1, z0, -23.25), 0, 3.6)
    lights((xc, x1, z0, -23.25), 0, per=36.0)
    # Consulting rooms: the doctor's desk at the window, a couch behind a screen, an eye chart.
    for (za, zb) in ((-30.75, -25.5), (-25.5, -20.25)):
        zm = (za + zb) / 2
        P("Table_Faculty", x0 + 1.2, zm + 0.6, y, "E")
        P("Chair_Office", x0 + 0.5, zm + 0.6, y, "E")
        P("Chair_Visitor", x0 + 2.05, zm + 0.2, y, "W")
        P("Chair_Visitor", x0 + 2.05, zm + 1.0, y, "W")
        P("Exam_Couch", x0 + 2.4, za + 0.5, y, "N")
        P("Privacy_Screen", x0 + 2.4, za + 1.35, y, "S")
        P("Eye_Chart", x0 + OO, zm - 1.6, y, "E")
        P("Medical_Cabinet", xc - WO, zb - 0.6 if zb - za > 5 else zb - 0.6, y, "W")
        P("Dustbin", xc - 0.4, za + 0.4, y)
        P("Light_Panel", (x0 + xc) / 2, zm, level_ceil(0))
        P("Ceiling_Fan", (x0 + xc) / 2 + 0.6, zm, level_ceil(0))
        lights((x0, xc, za, zb), 0, per=40.0, intensity=0.9)
    # Dressing room.
    P("Exam_Couch", x0 + 1.4, -18.0, y, "E")
    P("IV_Stand", x0 + 0.5, -16.6, y)
    P("Medical_Cabinet", x0 + 2.6, -20.25 + WO, y, "N")
    P("Medical_Cabinet", x0 + 3.6, -20.25 + WO, y, "N")
    P("Storage_Rack", xc - WO, -16.6, y, "W")
    P("Dustbin", xc - 0.4, -19.6, y)
    P("Light_Panel", (x0 + xc) / 2, -18.0, level_ceil(0))
    lights((x0, xc, -20.25, -15.75), 0, per=40.0, color=COOL)
    # Corridor.
    P("Fire_Extinguisher", xc + WO, -16.4, y, "E")
    P("Water_Cooler", xp - WO - 0.3, -17.0, y, "W")
    lights((xc, xp, -23.25, z1), 0, per=30.0)
    panels((xc, xp, -23.25, z1), 0, 3.4, margin=0.8)
    # Pharmacy: a counter across the shop, medicine shelves on three walls, a vaccine fridge.
    for Z in (-21.6, -19.4):
        P("Checkout_Counter", -67.6, Z, y, "E")
    P("Chair_Office", -68.6, -20.5, y, "E")
    for Z in (-22.2, -20.9, -19.6, -18.3, -17.0):
        P("Medicine_Shelf", xp + WO, Z, y, "E")
    for X in (-69.3, -68.1):
        P("Medicine_Shelf", X, -15.75 - WO, y, "S")
        P("Medicine_Shelf", X, -23.25 + WO, y, "N")
    P("Fridge", -66.6, -15.75 - WO, y, "S")
    P("Display_Panel", x1 - 0.6, -22.4, y, "W")
    P("Light_Panel", -67.0, -21.5, level_ceil(0))
    P("Light_Panel", -67.0, -17.5, level_ceil(0))
    lights((xp, x1, -23.25, -15.75), 0, per=30.0, color=COOL, intensity=1.2)
    # Medical store and ATM.
    P("Storage_Rack", xp + WO + 0.25, -14.1, y, "E")
    P("Storage_Rack", -68.7, z1 - OO, y, "S")
    P("Tube_Light", -68.75, -14.1, level_ceil(0), "N")
    lights((xp, -67.0, -15.75, z1), 0, per=40.0, color=COOL, intensity=0.7)
    P("ATM_Machine", -67.0 + WO, -14.9, y, "E")
    P("ATM_Machine", -67.0 + WO, -13.3, y, "E")
    P("Dustbin", x1 - 0.4, -15.3, y)
    P("Light_Panel", -65.2, -14.1, level_ceil(0))
    lights((-67.0, x1, -15.75, z1), 0, per=30.0, color=COOL)

    y = level_floor(1)
    # Ward: beds along the east windows and the west wall, lockers, drips, screens.
    for i, Z in enumerate((-40.0, -36.7, -33.4, -30.1, -26.8)):
        P("Hospital_Bed", x1 - OO, Z, y, "W")
        P("Bedside_Locker", x1 - OO, Z + 0.85, y, "W")
        P("IV_Stand", x1 - 2.3, Z - 0.7, y)
        if i % 2 == 0:
            P("Privacy_Screen", x1 - 1.3, Z + 1.6, y, "S")
    for Z in (-40.0, -36.7):
        P("Hospital_Bed", xc + WO, Z, y, "E")
        P("Bedside_Locker", xc + WO, Z + 0.85, y, "E")
    P("Medical_Cabinet", xc + WO, -25.0, y, "E")
    P("Wheelchair", -69.5, -25.1, y, "S")
    P("Dustbin", -68.0, -24.5, y)
    P("Wall_Clock", -68.3, -24.0 - WO, y, "S")
    fans((xc, x1, z0, -24.0), 1, 4.0)
    panels((xc, x1, z0, -24.0), 1, 3.4)
    lights((xc, x1, z0, -24.0), 1, per=34.0)
    # Observation room.
    for Z in (-21.4, -18.1, -14.8):
        P("Hospital_Bed", x1 - OO, Z, y, "W")
        P("Bedside_Locker", x1 - OO, Z + 0.85, y, "W")
        P("IV_Stand", x1 - 2.3, Z - 0.7, y)
    P("Privacy_Screen", x1 - 1.3, -19.75, y, "S")
    P("Privacy_Screen", x1 - 1.3, -16.45, y, "S")
    P("Medical_Cabinet", -70.6, z1 - OO, y, "S")
    P("Desk_Computer", -71.5, -16.5, y, "E")
    P("Chair_Office", -72.3, -16.5, y, "E")
    panels((xc, x1, -24.0, z1), 1, 3.4)
    lights((xc, x1, -24.0, z1), 1, per=34.0)
    # Duty doctor, nurses' station, linen store.
    P("Hostel_Bed", x0 + OO, -27.0, y, "E")
    P("Table_Faculty", -74.6, -30.0, y, "S")
    P("Chair_Office", -74.6, -29.3, y, "S")
    P("Almirah", xc - WO, -26.4, y, "W")
    P("Light_Panel", -75.8, -28.1, level_ceil(1))
    lights((x0, xc, -30.75, -25.5), 1, per=40.0, intensity=0.8)
    P("Reception_Counter", -75.2, -23.3, y, "E")
    P("Chair_Office", -76.6, -23.6, y, "E")
    P("Chair_Office", -76.6, -22.6, y, "E")
    P("Filing_Cabinet", x0 + 0.35, -25.0, y, "E")
    P("Fridge", x0 + OO, -21.0, y, "E")
    P("Light_Panel", -75.8, -22.9, level_ceil(1))
    lights((x0, xc, -25.5, -20.25), 1, per=40.0, intensity=0.9)
    for X in (-77.6, -76.6, -75.6):
        P("Storage_Rack", X + 0.0, -20.25 + WO, y, "N")
    P("Almirah", x0 + OO, -17.2, y, "E")
    P("Tube_Light", -75.8, -18.0, level_ceil(1), "N")
    lights((x0, xc, -20.25, -15.75), 1, per=40.0, intensity=0.7)

    y = level_floor(2)
    # Medical records.
    for Z in (-40.2, -38.0, -35.8, -33.6):
        for X in (-71.0, -69.2):
            P("Storage_Rack", X, Z, y, "S")
    for X in (-66.5, -64.6):
        P("Desk_Computer", X, -31.0, y, "S")
        P("Chair_Office", X, -30.2, y, "S")
    for Z in (-29.0, -28.3):
        P("Filing_Cabinet", x1 - OO - 0.3, Z, y, "W")
    P("Filing_Cabinet", xc + WO + 0.3, -29.0, y, "E")
    panels((xc, x1, z0, -27.0), 2, 3.4)
    lights((xc, x1, z0, -27.0), 2, per=34.0, color=COOL)
    # Physiotherapy.
    P("Exam_Couch", -66.0, -25.5, y, "N")
    P("Exam_Couch", -66.0, -22.6, y, "N")
    P("Privacy_Screen", -66.0, -24.0, y, "S")
    P("Exercise_Bike", -70.5, -15.0, y, "E")
    P("Treadmill", -68.2, -14.0, y, "S")
    P("Gym_Mat", -65.6, -18.6, y)
    P("Gym_Mat", -65.6, -15.6, y)
    P("Medical_Cabinet", xc + WO, -21.0, y, "E")
    P("Plant_Pot", x1 - 0.45, -26.4, y)
    P("Wall_Clock", -69.0, -27.0 + WO, y, "N")
    panels((xc, x1, -27.0, z1), 2, 3.4)
    lights((xc, x1, -27.0, z1), 2, per=34.0)
    # Medical officer and staff room.
    P("Table_Faculty", -76.4, -27.0, y, "E")
    P("Chair_Office", -77.3, -27.0, y, "E")
    P("Chair_Visitor", -75.4, -27.4, y, "W")
    P("Chair_Visitor", -75.4, -26.5, y, "W")
    P("Bookshelf", -74.8, -30.75 + WO, y, "N")
    P("Sofa_1", -74.0, -24.0, y, "W")
    P("Plant_Pot", x0 + 0.4, -23.5, y)
    P("Light_Panel", -75.8, -26.9, level_ceil(2))
    lights((x0, xc, -30.75, -23.0), 2, per=40.0)
    P("Table_Meeting", -75.8, -19.4, y, "E")
    for Z in (-20.0, -18.8):
        P("Chair_Cafe", -77.4, Z, y, "E")
        P("Chair_Cafe", -74.2, Z, y, "W")
    P("Water_Cooler", xc - WO - 0.3, -22.3, y, "W")
    P("Notice_Board", -76.0, -23.0 + WO, y, "N")
    P("Almirah", -76.6, -15.75 - WO, y, "S")
    P("Light_Panel", -75.8, -19.4, level_ceil(2))
    lights((x0, xc, -23.0, -15.75), 2, per=40.0)


class UnitFrame:
    """A shop unit seen from its front: u runs along the shop front from the unit's low end
    (south or west), v runs inwards from the front wall's inside face to the back wall."""

    def __init__(self, rect, side):
        X0, X1, Z0, Z1 = rect
        self.side = side
        self.n = DIRS[side]
        if side == "E":
            self.o, self.t, self.U, self.D = (X1, Z0), (0.0, 1.0), Z1 - Z0, X1 - X0
        elif side == "S":
            self.o, self.t, self.U, self.D = (X0, Z0), (1.0, 0.0), X1 - X0, Z1 - Z0
        else:
            raise ValueError("Vipanan units face east or south")

    def w(self, u, v):
        return (self.o[0] + self.t[0] * u - self.n[0] * v, self.o[1] + self.t[1] * u - self.n[1] * v)

    def face(self, f):
        """'out' towards the shop front, 'in' towards the back, '+u' or '-u' along the front."""
        d = {"out": self.n, "in": (-self.n[0], -self.n[1]), "+u": self.t, "-u": (-self.t[0], -self.t[1])}[f]
        return next(k for k, v in DIRS.items() if v == d)

    def P(self, name, u, v, y, f="out"):
        X, Z = self.w(u, v)
        P(name, X, Z, y, self.face(f))


def furnish_vipanan(C):
    """The shop units of a Vipanan arm, each laid out from its own front (UnitFrame)."""
    y = FL
    for sp in C.at(0):
        F = UnitFrame(sp.rect, sp.front)
        U, D = F.U, F.D
        bk = D - OO              # against the back wall
        k = sp.kind
        if k == "cafe":
            F.P("Cafe_Counter", U / 2, D - 0.9, y, "out")
            F.P("Drinks_Cooler", 0.5, bk, y, "out")
            F.P("Fridge", U - 0.6, bk, y, "out")
            F.P("Notice_Board", U / 2 + 2.2, bk, y, "out")
            for u in (2.0, 4.7, 7.4, 10.1):
                if u > U - 1.4:
                    continue
                for v in (1.9, 4.5):
                    F.P("Table_Cafe", u, v, y)
                    for du, dv, f in ((0.0, 0.6, "out"), (0.0, -0.6, "in"), (0.6, 0.0, "-u"), (-0.6, 0.0, "+u")):
                        F.P("Chair_Cafe", u + du, v + dv, y, f)
            F.P("Water_Cooler", WO + 0.3, D - 2.2, y, "+u")
            F.P("Plant_Pot", 0.5, 0.5, y)
            F.P("Plant_Pot", U - 0.5, 0.5, y)
            F.P("Dustbin", U - 0.5, D - 2.0, y)
        elif k == "atm":
            F.P("ATM_Machine", U / 2 - 0.8, bk, y, "out")
            F.P("ATM_Machine", U / 2 + 0.8, bk, y, "out")
            F.P("Notice_Board", WO, D / 2, y, "+u")
            F.P("Dustbin", U - 0.5, 0.6, y)
            F.P("Plant_Pot", 0.5, 0.5, y)
        elif k == "juice":
            F.P("Fruit_Stand", 1.4, 1.3, y, "out")
            F.P("Fruit_Stand", 3.2, 1.3, y, "out")
            F.P("Cafe_Counter", U / 2, D - 2.4, y, "out")
            F.P("Drinks_Cooler", U - WO, 2.8, y, "-u")
            F.P("Fridge", U - WO, 3.8, y, "-u")
            F.P("Fridge", U - WO, 4.8, y, "-u")
            F.P("Storage_Rack", 1.0, bk, y, "out")
            F.P("Dustbin", U - 0.5, 0.5, y)
        elif k == "repair":
            F.P("Checkout_Counter", U / 2, 2.4, y, "out")
            F.P("Chair_Office", U / 2, 3.3, y, "out")
            for v in (4.4, 5.6, 6.8):
                F.P("Wall_Shelf", WO, v, y, "+u")
                F.P("Wall_Shelf", U - WO, v, y, "-u")
            F.P("Desk_Computer", U / 2, bk - 0.35, y, "out")
            F.P("Display_Panel", U - 0.7, 0.7, y, "out")
        elif k == "stationery":
            F.P("Checkout_Counter", U - 2.0, 1.8, y, "out")
            F.P("Chair_Office", U - 2.0, 2.7, y, "out")
            F.P("Photocopier", U - 1.2, bk - 0.4, y, "out")
            F.P("Photocopier", U - 2.4, bk - 0.4, y, "out")
            F.P("Desk_Computer", U - 4.0, bk - 0.35, y, "out")
            F.P("Chair_Office", U - 4.0, bk - 1.0, y, "in")
            u = 0.7
            while u < U - 5.2:
                F.P("Wall_Shelf", u, bk, y, "out")
                u += 1.2
            F.P("Shop_Gondola", 3.0, 3.6, y, "+u")
            F.P("Storage_Rack", WO + 0.3, 1.2, y, "+u")
            F.P("Display_Panel", 0.8, 0.7, y, "out")
        elif k == "bank":
            vc = D - 3.2
            u = 1.2
            while u < U - 0.9:
                F.P("Bank_Counter", u, vc, y, "out")
                F.P("Chair_Office", u - 0.6, vc + 0.75, y, "out")
                u += 2.3
            for u in (1.0, 1.8, 2.6):
                F.P("Filing_Cabinet", u, bk - 0.3, y, "out")
            F.P("Almirah", U - 1.0, bk, y, "out")
            for v in (1.0, 2.6):
                F.P("Waiting_Chairs", WO + 0.4, v, y, "+u")
            F.P("Display_Panel", U - 0.8, 0.7, y, "out")
            F.P("Water_Cooler", U - WO - 0.3, 2.4, y, "-u")
            F.P("Wall_Clock", U / 2, bk, y, "out")
        elif k == "store":
            F.P("Checkout_Counter", 1.4, 1.5, y, "+u")
            F.P("Chair_Office", 0.7, 1.5, y, "+u")
            for u in (3.4, 5.6, 7.8):
                if u < U - 1.2:
                    F.P("Shop_Gondola", u, D / 2 + 0.4, y, "+u")
            u = 0.8
            while u < U - 0.6:
                F.P("Wall_Shelf", u, bk, y, "out")
                u += 1.2
            F.P("Drinks_Cooler", U - WO, 1.6, y, "-u")
            F.P("Drinks_Cooler", U - WO, 2.4, y, "-u")
            F.P("Fridge", U - WO, 3.3, y, "-u")
            F.P("Plant_Pot", U - 0.5, 0.5, y)
        elif k == "post":
            F.P("Checkout_Counter", U / 2, 3.0, y, "out")
            F.P("Chair_Office", U / 2, 3.9, y, "out")
            F.P("Storage_Rack", U / 2, bk, y, "out")
            F.P("Filing_Cabinet", WO + 0.3, D - 1.6, y, "+u")
            F.P("Notice_Board", WO, 1.4, y, "+u")
            F.P("Dustbin", U - 0.4, 0.5, y)
        panels(sp.rect, 0, 3.4, margin=0.9)
        fans(sp.rect, 0, 4.2)
        lights(sp.rect, 0, per=30.0, intensity=1.1)


def furnish_sports(C):
    x0, x1, z0, z1 = C.inner
    zh = HALL_Z - PART / 2
    hx0, hx1, hz0, hz1 = C.hall
    xl, xm, zc0, zc1 = 34.0, 41.25, -343.0, -341.0
    y = FL
    # Hall: nets on the four courts, benches between them, a scoreboard, high-bay lamps.
    for X, Z in COURTS:
        P("Badminton_Net", X, Z, y)
    for Z in (-333.5, -319.5, -305.0):
        P("Bench", hx0 + 0.5, Z, y, "E")
        P("Bench", hx1 - 0.5, Z, y, "W")
    P("Scoreboard", (hx0 + hx1) / 2, hz1 - OO, y, "S")
    P("Wall_Clock", (hx0 + hx1) / 2 + 3.0, hz1 - OO, y + 3.6, "S")
    P("Ball_Cart", hx1 - 1.0, hz0 + 0.8, y, "W")
    P("Fire_Extinguisher", hx0 + OO, -310.0, y, "E")
    P("Fire_Extinguisher", hx1 - OO, -326.0, y, "W")
    P("Speaker", hx0 + 0.6, hz1 - 0.6, y, "E")
    P("Speaker", hx1 - 0.6, hz1 - 0.6, y, "W")
    for i in range(4):
        for j in range(6):
            X = hx0 + (hx1 - hx0) * (i + 0.5) / 4
            Z = hz0 + (hz1 - hz0) * (j + 0.5) / 6
            P("High_Bay_Light", X, Z, level_ceil(TOP))
    I.room_lights(C.hall, y=level_ceil(TOP) - 1.6, per=70.0, color=COOL, intensity=2.2)
    # Lobby: reception facing the door, a trophy cabinet, benches, water.
    P("Reception_Counter", 31.4, -350.4, y, "W")
    P("Chair_Office", 32.5, -350.4, y, "W")
    P("Filing_Cabinet", xl - WO - 0.3, -353.6, y, "W")
    P("Trophy_Cabinet", 31.8, zh - WO, y, "S")
    P("Notice_Board", 26.6, zh - WO, y, "S")
    P("Bench", 31.2, -340.2, y, "S")
    P("Bench", 31.2, -344.0, y, "N")
    P("Water_Cooler", xl - WO - 0.3, -338.2, y, "W")
    P("Plant_Pot", x0 + 0.5, -338.0, y)
    P("Plant_Pot", x0 + 0.5, -343.4, y)
    P("Display_Panel", 27.2, -341.6, y, "E")
    P("Fire_Extinguisher", x0 + OO, -341.0, y, "E")
    panels((x0, xl, z0, zh), 0, 3.4)
    lights((x0, xl, z0, zh), 0, per=34.0)
    # Changing rooms: lockers, benches, showers and WCs, basins under the windows.
    for (X0, X1, lockers_x, wet_x, lf, wf) in ((xl, xm, xl + WO, xm - WO, "E", "W"), (xm, x1, x1 - OO, xm + WO, "W", "E")):
        for Z in (-352.6, -350.6, -348.6, -346.6):
            P("Locker_Bank", lockers_x, Z, y, lf)
        cx = (X0 + X1) / 2
        put("Bench", cx - 0.2, -350.0, y, yaw=90.0)
        put("Bench", cx - 0.2, -347.4, y, yaw=90.0)
        sgn = -1.0 if wf == "W" else 1.0
        for Z in (-353.9, -352.8, -351.7):
            P("Shower_Cubicle", wet_x + sgn * 1.2, Z, y, wf)
        for Z in (-348.0, -346.9):
            P("Toilet_Cubicle", wet_x + sgn * 1.5, Z, y, wf)
        P("Basin_Counter", cx, -343.0 - WO, y, "S")
        P("Dustbin", cx + 1.6, -343.5, y)
        P("Tube_Light", cx, -352.0, level_ceil(0), "E")
        P("Tube_Light", cx, -347.0, level_ceil(0), "E")
        lights((X0, X1, z0, zc0), 0, per=36.0, color=COOL, intensity=0.9)
    # Corridor, equipment store, first aid.
    P("Fire_Extinguisher", 38.0, zc0 + WO, y, "N")
    P("Notice_Board", 45.0, zc1 - WO, y, "S")
    lights((xl, x1, zc0, zc1), 0, per=30.0)
    P("Light_Panel", 38.0, -342.0, level_ceil(0))
    P("Light_Panel", 45.0, -342.0, level_ceil(0))
    for X in (35.2, 36.4):
        P("Storage_Rack", X, zh - WO, y, "S")
    P("Ball_Cart", 39.8, -339.8, y, "W")
    P("Ball_Cart", 39.8, -338.6, y, "W")
    P("Storage_Rack", xl + WO + 0.25, -339.2, y, "E")
    P("Tube_Light", 37.6, -339.0, level_ceil(0), "E")
    lights((xl, xm, zc1, zh), 0, per=40.0, intensity=0.7)
    P("Exam_Couch", 45.2, -338.4, y, "S")
    P("Medical_Cabinet", x1 - OO, -339.2, y, "W")
    P("Table_Faculty", 42.6, -339.0, y, "E")
    P("Wheelchair", 47.6, -340.4, y, "W")
    P("Light_Panel", 44.9, -339.0, level_ceil(0))
    lights((xm, x1, zc1, zh), 0, per=40.0, color=COOL)
    # Gym (level 1): treadmills and bikes at the south windows, racks and benches, mats.
    y = level_floor(1)
    for X in (31.0, 33.0, 35.0, 37.0):
        P("Treadmill", X, z0 + 1.4, y, "S")
    for X in (39.6, 41.2, 42.8):
        P("Exercise_Bike", X, z0 + 1.0, y, "S")
    P("Power_Rack", 44.4, -347.0, y, "N")
    P("Power_Rack", 44.4, -343.6, y, "N")
    for X in (32.0, 35.5, 39.0):
        P("Weight_Bench", X, -345.4, y, "N")
    P("Dumbbell_Rack", x1 - OO, -351.0, y, "W")
    P("Dumbbell_Rack", x1 - OO, -339.6, y, "W")
    for X, Z in ((29.8, -340.4), (32.0, -340.4), (34.2, -340.4), (29.8, -342.4), (32.0, -342.4), (34.2, -342.4)):
        P("Gym_Mat", X, Z, y)
    for X in (39.0, 40.6, 42.2):
        P("Exercise_Bike", X, -349.6, y, "N")
    P("Weight_Bench", 37.6, -340.6, y, "E")
    P("Weight_Bench", 40.4, -340.6, y, "E")
    P("Dumbbell_Rack", x1 - OO, -345.4, y, "W")
    P("Bench", 27.6, zh - 0.5, y, "S")
    P("Water_Cooler", x0 + 0.4, -338.0, y, "E")
    P("Wall_Clock", 26.6, zh - WO, y, "S")
    P("Plant_Pot", x1 - 0.5, -337.7, y)
    P("Fire_Extinguisher", x0 + OO, -344.0, y, "E")
    for X in (27.0, 31.0, 35.0, 39.0, 43.0, 46.5):
        for Z in (-352.0, -346.0, -340.0):
            if X < 29.0 and Z < -347.0:
                continue
            P("Ceiling_Fan", X, Z, level_ceil(1)) if X in (31.0, 39.0, 46.5) else P("Light_Panel", X, Z, level_ceil(1))
    lights((x0, x1, z0, zh), 1, per=34.0)
    # Games room and yoga room (level 2).
    y = level_floor(2)
    put("TT_Table", 31.4, -350.6, y, yaw=0.0)
    put("TT_Table", 31.4, -345.6, y, yaw=0.0)
    P("Carrom_Table", 28.6, -340.4, y)
    P("Carrom_Table", 34.4, -340.4, y)
    for X, Z in ((28.6, -340.4), (34.4, -340.4)):
        for dx, dz, f in ((0.0, 0.8, "S"), (0.0, -0.8, "N"), (0.8, 0.0, "W"), (-0.8, 0.0, "E")):
            P("Chair_Cafe", X + dx, Z + dz, y, f)
    for Z in (-352.5, -349.5, -346.5):
        P("Chess_Table", 35.6, Z, y)
        P("Chair_Cafe", 35.6, Z + 0.6, y, "S")
        P("Chair_Cafe", 35.6, Z - 0.6, y, "N")
    P("Bench", 30.0, zh - 0.5, y, "S")
    P("Notice_Board", 33.2, zh - WO, y, "S")
    panels((x0, xg_(), z0, zh), 2, 3.4)
    lights((x0, xg_(), z0, zh), 2, per=34.0)
    for i in range(3):
        for j in range(5):
            P("Yoga_Mat", 39.6 + i * 3.2, -352.6 + j * 2.8, y)
    P("Speaker", x1 - 0.6, zh - 0.6, y, "W")
    P("Wall_Clock", 43.0, zh - WO, y, "S")
    P("Plant_Pot", 38.0, zh - 0.5, y)
    P("Plant_Pot", x1 - 0.5, z0 + 0.5, y)
    for X in (40.0, 44.0):
        for Z in (-351.0, -345.0, -339.5):
            P("Ceiling_Fan", X + 1.8, Z, level_ceil(2)) if X == 44.0 else P("Light_Panel", X, Z, level_ceil(2))
    lights((xg_(), x1, z0, zh), 2, per=34.0)


def xg_():
    return 37.0


# ------------------------------------------------------------------ entry point for build_interiors
BUILDERS = [(block1, furnish_block1), (vipanan_west, furnish_vipanan), (vipanan_north, furnish_vipanan),
            (sports, furnish_sports)]


def build_all(zone):
    """Shells to export and their zones, rooms and furniture, numbering zones from zone."""
    out = []
    for make, furnish in BUILDERS:
        n0, l0 = len(PLACED), len(LIGHTS)
        # Interiors drawn at a block's first spot are built there and then moved to the layout's
        # (B.INTERIOR_DESIGN); the others are drawn where they stand.
        with B.at_design(list(B.INTERIOR_DESIGN)):
            C = make()
            shell = civic_shell(C)
            furnish(C)
            for k in range(C.top + 1):
                door_leaves(C, k)
            entrance_leaves(C)
            exit_signs(C)
        dx, dz = B.design_shift(C.name)
        for sp in C.spaces:
            sp.rect = (sp.rect[0] + dx, sp.rect[1] + dx, sp.rect[2] + dz, sp.rect[3] + dz)
        if C.hall is not None:
            C.hall = (C.hall[0] + dx, C.hall[1] + dx, C.hall[2] + dz, C.hall[3] + dz)
        I.shift_output(shell, PLACED[n0:], LIGHTS[l0:], [], dx, dz)
        rooms = []
        for sp in C.spaces:
            if sp.kind.endswith("_back"):
                continue
            rooms.append(dict(name=sp.name, kind=sp.kind, rect=[round(v, 2) for v in sp.rect], level=sp.level, zone=zone))
        hall = C.hall is not None
        for p_ in PLACED[n0:]:
            p_["level"] = level_of(p_["y"], C.top) if not (hall and in_rect(p_, C.hall)) else 0
            p_["zone"] = zone
        for l_ in LIGHTS[l0:]:
            l_["level"] = level_of(l_["y"], C.top) if not (hall and in_rect(l_, C.hall)) else 0
            l_["zone"] = zone
        out.append(dict(shell=shell, name=C.label, rooms=rooms, floorOffset=FL, maxLevel=C.top,
                        hall=[round(v, 2) for v in C.hall] if hall else None))
        print("%-26s %d rooms on %d floors, %d props" % (C.label, len(rooms), C.top + 1, len(PLACED) - n0))
        zone += 1
    return out


def level_of(y, top=TOP):
    """Storey of a height above the base: floor-standing props sit on level_floor(k), ceiling
    props hang from level_ceil(k)."""
    return max(0, min(top, int(math.floor((y - FL + 0.5) / FLOOR))))


def in_rect(p_, r):
    return r[0] - 0.01 <= p_["x"] <= r[1] + 0.01 and r[2] - 0.01 <= p_["z"] <= r[3] + 0.01
