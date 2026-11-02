#!/usr/bin/env python3
"""Build walk-in interiors in headless Blender: the interior shell of each building (floors,
ceilings, walls with window openings, partitions with doorways, tiered classroom floors) and
a set of furniture models, plus a placement list Unity uses to furnish and light the rooms.

Phase A: the ground floor of the academic block.

Run after build_buildings.py, with the Python that has `bpy`:
    python tools/blender/build_interiors.py

Writes:
    Assets/Models/Buildings/Academic_Block_Interior.fbx   the interior shell
    Assets/Models/Props/*.fbx                             one model per kind of furniture
    Assets/Data/interiors.json                            shells, zones, props, lights

Coordinates are world X/Z like build_buildings.py. A prop's front faces +Z before its yaw;
yaw is degrees clockwise seen from above (Unity's Quaternion.Euler(0, yaw, 0)). Heights in
interiors.json are relative to the building's base, which Unity sets on the ground at the
building's pivot, exactly as for the outside model.
"""
import json
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_buildings as B  # noqa: E402  (imports bpy first)
from build_buildings import FLOOR, BUILDINGS, MATERIALS, Model, inset_polygon, arc_points  # noqa: E402

ROOT = B.ROOT
PROP_DIR = os.path.join(ROOT, "Assets", "Models", "Props")
OUT_JSON = os.path.join(ROOT, "Assets", "Data", "interiors.json")

FL = 0.6               # ground floor level (top of the plinth)
CEIL = FLOOR - 0.3     # ground floor ceiling (3.1 m)
HIGH_CEIL = 2 * FLOOR - 0.3   # double-height foyers (6.5 m)
PART = 0.15            # partition thickness
DOOR_H = 2.1


# ------------------------------------------------------------------ face helpers
def qface(m, pts, uvs, mat, hint):
    """Adds a planar face with explicit UVs, flipping it so its normal points along hint (local)."""
    a, b_, c = pts[0], pts[1], pts[2]
    u = (b_[0] - a[0], b_[1] - a[1], b_[2] - a[2])
    v = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    if n[0] * hint[0] + n[1] * hint[1] + n[2] * hint[2] < 0:
        pts, uvs = list(reversed(pts)), list(reversed(uvs))
    m.quad(pts, mat, uvs)


class Line:
    """A straight wall line in world coordinates, from A to B, with a unit normal (world X, Z)."""

    def __init__(self, m, A, B_, normal):
        self.m, self.A, self.B = m, A, B_
        self.L = math.dist(A, B_)
        self.t = ((B_[0] - A[0]) / self.L, (B_[1] - A[1]) / self.L)
        self.n = normal

    def P(self, s, d, z):
        X = self.A[0] + self.t[0] * s + self.n[0] * d
        Z = self.A[1] + self.t[1] * s + self.n[1] * d
        x, y = self.m.b(X, Z)
        return (x, y, z)

    def hint(self, k=1.0):
        return (-self.n[0] * k, -self.n[1] * k, 0.0)   # world -> local flips X and Z

    def thint(self, k=1.0):
        return (-self.t[0] * k, -self.t[1] * k, 0.0)

    def rect(self, s0, s1, z0, z1, d, mat, facing=1.0):
        if s1 - s0 < 1e-3 or z1 - z0 < 1e-3:
            return
        tw, th, _ = MATERIALS[mat]
        pts = [self.P(s0, d, z0), self.P(s1, d, z0), self.P(s1, d, z1), self.P(s0, d, z1)]
        uvs = [(s0 / tw, z0 / th), (s1 / tw, z0 / th), (s1 / tw, z1 / th), (s0 / tw, z1 / th)]
        qface(self.m, pts, uvs, mat, self.hint(facing))

    def side(self, s, z0, z1, d0, d1, mat, facing):
        if z1 - z0 < 1e-3 or abs(d1 - d0) < 1e-3:
            return
        tw, th, _ = MATERIALS[mat]
        pts = [self.P(s, d0, z0), self.P(s, d1, z0), self.P(s, d1, z1), self.P(s, d0, z1)]
        uvs = [(d0 / tw, z0 / th), (d1 / tw, z0 / th), (d1 / tw, z1 / th), (d0 / tw, z1 / th)]
        qface(self.m, pts, uvs, mat, self.thint(facing))

    def hband(self, s0, s1, z, d0, d1, mat, up=True):
        if s1 - s0 < 1e-3 or abs(d1 - d0) < 1e-3:
            return
        tw, th, _ = MATERIALS[mat]
        pts = [self.P(s0, d0, z), self.P(s1, d0, z), self.P(s1, d1, z), self.P(s0, d1, z)]
        uvs = [(s0 / tw, d0 / th), (s1 / tw, d0 / th), (s1 / tw, d1 / th), (s0 / tw, d1 / th)]
        qface(self.m, pts, uvs, mat, (0, 0, 1 if up else -1))

    def wall(self, s0, s1, z0, z1, d, mat, openings, facing=1.0):
        """Wall face s0..s1 x z0..z1 at depth d with rectangular holes (s0, s1, z0, z1)."""
        cuts = {s0, s1}
        for o in openings:
            for v in (o[0], o[1]):
                if s0 < v < s1:
                    cuts.add(v)
        cuts = sorted(cuts)
        for c0, c1 in zip(cuts, cuts[1:]):
            mid = (c0 + c1) / 2
            holes = sorted((max(z0, o[2]), min(z1, o[3])) for o in openings if o[0] <= mid <= o[1])
            z = z0
            for h0, h1 in holes:
                if h0 > z:
                    self.rect(c0, c1, z, h0, d, mat, facing)
                z = max(z, h1)
            if z < z1:
                self.rect(c0, c1, z, z1, d, mat, facing)


def hrect(m, X0, X1, Z0, Z1, z, mat, up=True):
    x0, x1, y0, y1 = m.rect(X0, X1, Z0, Z1)
    m.flat(x0, x1, y0, y1, z, mat, up)


def hpoly(m, world_pts, z, mat, up=True):
    m.face([(*m.b(X, Z), z) for X, Z in world_pts], mat, (0, 0, 1 if up else -1))


def partition(m, A, B_, z0, z1, doors=(), t=PART, mat="Wall_Paint", trim=True):
    """A partition wall from A to B (world) with doorways [(s_centre, width)], both faces,
    jambs, soffits and wooden architraves round each doorway."""
    dx, dz = B_[0] - A[0], B_[1] - A[1]
    L = math.hypot(dx, dz)
    n = (-dz / L, dx / L)
    ln = Line(m, A, B_, n)
    holes = [(c - w / 2, c + w / 2, z0, z0 + DOOR_H) for c, w in doors]
    ln.wall(0, L, z0, z1, t / 2, mat, holes, 1.0)
    ln.wall(0, L, z0, z1, -t / 2, mat, holes, -1.0)
    for s0, s1, h0, h1 in holes:
        ln.side(s0, h0, h1, -t / 2, t / 2, mat, 1)
        ln.side(s1, h0, h1, -t / 2, t / 2, mat, -1)
        ln.hband(s0, s1, h1, -t / 2, t / 2, mat, up=False)
        if trim:
            for d, f in ((t / 2, 1.0), (-t / 2, -1.0)):
                arch_box(ln, s0 - 0.07, s0, h0, h1 + 0.07, d, f)
                arch_box(ln, s1, s1 + 0.07, h0, h1 + 0.07, d, f)
                arch_box(ln, s0 - 0.07, s1 + 0.07, h1, h1 + 0.07, d, f)
    return ln


def arch_box(ln, s0, s1, z0, z1, d, f, depth=0.02, mat="Wood"):
    """A thin trim strip standing proud of a wall face (d, facing f)."""
    d1 = d + depth * f
    ln.rect(s0, s1, z0, z1, d1, mat, f)
    ln.side(s0, z0, z1, d, d1, mat, -1)
    ln.side(s1, z0, z1, d, d1, mat, 1)
    ln.hband(s0, s1, z1, d, d1, mat, up=True)


# ------------------------------------------------------------------ academic block frame
NW, SW = BUILDINGS["Academic Block - North Wing"], BUILDINGS["Academic Block - South Wing"]
WW, EW = BUILDINGS["Academic Block - West Wing"], BUILDINGS["Academic Block - East Wing"]
OX0, OX1 = NW["x"] - NW["width"] / 2, NW["x"] + NW["width"] / 2
OZ1, OZ0 = NW["z"] + NW["depth"] / 2, SW["z"] - SW["depth"] / 2
IX0, IX1 = WW["x"] + WW["width"] / 2, EW["x"] - EW["width"] / 2
IZ1, IZ0 = NW["z"] - NW["depth"] / 2, SW["z"] + SW["depth"] / 2
P, PO = 15.0, 1.6
E0, E1 = OX0 + P - PO, OX1 - P + PO
F0, F1 = OZ0 + P - PO, OZ1 - P + PO
EX = BUILDINGS["Academic Block - Entrance"]["x"]
WO, WC = B.WALL["outer"], B.WALL["court"]

OUTER_RUNS = [((E0, OZ0), (E1, OZ0)), ((OX1, F0), (OX1, F1)), ((E1, OZ1), (E0, OZ1)), ((OX0, F1), (OX0, F0))]
COURT_RUNS = [((IX0, IZ0), (IX1, IZ0)), ((IX1, IZ0), (IX1, IZ1)), ((IX1, IZ1), (IX0, IZ1)), ((IX0, IZ1), (IX0, IZ0))]


def outer_doors(A, B_):
    """Bays of an outer run that get doorways (same rule as build_buildings.OUTER_DOORS)."""
    L = math.dist(A, B_)
    t = ((B_[0] - A[0]) / L, (B_[1] - A[1]) / L)
    bays, bw = B.facade_bays(L)
    out = []
    for X, Z in B.OUTER_DOORS:
        dx, dz = X - A[0], Z - A[1]
        s = dx * t[0] + dz * t[1]
        off = abs(dx * t[1] - dz * t[0])
        if off < 0.6 and 0 <= s <= L:
            out.append(min(bays - 1, int(s / bw)))
    return out


def run_openings(A, B_, kind):
    L = math.dist(A, B_)
    if kind == "outer":
        return B.facade_openings(L, "outer", outer_doors(A, B_))
    bays, _ = B.facade_bays(L)
    return B.facade_openings(L, "court", [bays // 2])


def solid_spans(A, B_, kind, axis):
    """Where along a perimeter run (in world coordinate `axis`: 0 = X, 1 = Z) a partition may meet
    the wall without cutting a window: the complement of the openings, with a margin."""
    L = math.dist(A, B_)
    t = ((B_[0] - A[0]) / L, (B_[1] - A[1]) / L)
    ops = sorted((o[0] - 0.12, o[1] + 0.12) for o in run_openings(A, B_, kind))
    spans, s = [], 0.0
    for o0, o1 in ops:
        if o0 > s:
            spans.append((s, o0))
        s = max(s, o1)
    if s < L:
        spans.append((s, L))
    out = []
    for s0, s1 in spans:
        v0, v1 = A[axis] + t[axis] * s0, A[axis] + t[axis] * s1
        out.append((min(v0, v1), max(v0, v1)))
    return out


def snap(value, span_lists, reach=3.0):
    """Nearest value (in 0.05 m steps) that lies in a solid span on every listed wall."""
    def ok(v, spans):
        lo, hi = spans[0][0], spans[-1][1]
        if v < lo - 0.01 or v > hi + 0.01:
            return True   # beyond this wall's run (e.g. inside a pavilion): nothing to hit
        return any(a <= v <= b for a, b in spans)

    for k in range(int(reach / 0.05) + 1):
        for v in (value + k * 0.05, value - k * 0.05):
            if all(ok(v, spans) for spans in span_lists):
                return round(v, 3)
    return value


S_NORTH = solid_spans(*OUTER_RUNS[2], "outer", 0)
S_SOUTH = solid_spans(*OUTER_RUNS[0], "outer", 0)
S_WEST = solid_spans(*OUTER_RUNS[3], "outer", 1)
S_EAST = solid_spans(*OUTER_RUNS[1], "outer", 1)
S_CS = solid_spans(*COURT_RUNS[0], "court", 0)   # courtyard south side (X)
S_CE = solid_spans(*COURT_RUNS[1], "court", 1)   # courtyard east side (Z)
S_CN = solid_spans(*COURT_RUNS[2], "court", 0)
S_CW = solid_spans(*COURT_RUNS[3], "court", 1)

# Interior faces of the shell.
XW, XE = OX0 + WO, OX1 - WO          # west / east outer walls (inside face)
ZS, ZN = OZ0 + WO, OZ1 - WO          # south / north
CX0, CX1 = IX0 - WC, IX1 + WC        # courtyard walls (inside face)
CZ0, CZ1 = IZ0 - WC, IZ1 + WC

# Ring corridor round the courtyard band, 3 m wide, and a second corridor in the deep south wing.
RI = 8.0                              # depth of the rooms between the courtyard and the corridor
KX0, KX1 = IX0 - RI, IX1 + RI         # corridor walls on the courtyard-band side
KZ0, KZ1 = IZ0 - RI, IZ1 + RI
QX0, QX1 = KX0 - 3.0, KX1 + 3.0       # corridor walls on the outer side
QZ0 = snap(KZ0 - 3.0, [S_WEST, S_EAST])
QZ1 = snap(KZ1 + 3.0, [S_WEST, S_EAST])
SC1 = snap(QZ0 - 14.0, [S_WEST, S_EAST])   # second corridor, north wall
SC0 = snap(SC1 - 3.0, [S_WEST, S_EAST])    # second corridor, south wall

# Pavilion stair halls line up with the pavilions' inner faces.
PXW, PXE = E0 - WO, E1 + WO


def nx(v):
    return snap(v, [S_NORTH])


def sx(v):
    return snap(v, [S_SOUTH])


def wz(v):
    return snap(v, [S_WEST])


def ez(v):
    return snap(v, [S_EAST])


# ------------------------------------------------------------------ the plan
# Each room: name, kind, (X0, X1, Z0, Z1), front side ('N','S','E','W': where the board or
# counter is), floor material, and doors [(wall side, position along that wall, width)].
ROOMS = []
WALLS = []      # (A, B, doors) partitions in world coordinates
CORRIDORS = []  # (X0, X1, Z0, Z1) for corridor lights and furniture


def room(name, kind, X0, X1, Z0, Z1, front="W", floor=None, doors=()):
    ROOMS.append(dict(name=name, kind=kind, rect=(X0, X1, Z0, Z1), front=front, floor=floor, doors=list(doors)))


def plan():
    # North outer band, between the north corridor wall and the north facade.
    n1, n2, n3, n4 = nx(PXW), nx(65.0), nx(83.2), nx(104.8)
    n5 = nx(PXE)
    room("Student Lounge NW", "lounge", XW, n1, QZ1, ZN + 1.6, floor="Floor_Wood")
    room("Case Room N1", "case", n1, n2, QZ1, ZN, front="W", floor="Carpet")
    room("Entrance Lobby", "lobby", n2, n3, QZ1, ZN, front="N", floor="Floor_Tile")
    room("Case Room N2", "case", n3, n4, QZ1, ZN, front="E", floor="Carpet")
    room("Computer Lab N", "complab", n4, n5, QZ1, ZN, front="E", floor="Floor_Tile")
    room("Student Lounge NE", "lounge", n5, XE, QZ1, ZN + 1.6, floor="Floor_Wood")
    for x in (n1, n2, n3, n4, n5):
        WALLS.append(((x, QZ1), (x, ZN), []))
    # Doors in the north corridor wall (the lobby is open to the corridor).
    WALLS.append(((XW, QZ1), (n2, QZ1), [((QX0 + KX0) / 2 - XW, 1.0), (n1 + 3.0 - XW, 1.0), (n2 - 3.0 - XW, 1.0)]))
    WALLS.append(((n3, QZ1), (XE, QZ1), [(3.0, 1.0), (n4 - 3.0 - n3, 1.0), (n4 + 2.5 - n3, 1.0), ((QX1 + KX1) / 2 - n3, 1.0)]))

    # South outer band, beyond the second corridor.
    s1, s2, s3, s4, s5 = sx(PXW), sx(65.0), sx(83.2), sx(104.8), sx(PXE)
    room("Student Lounge SW", "lounge", XW, s1, ZS - 1.6, SC0, floor="Floor_Wood")
    room("Classroom S1", "classroom", s1, s2, ZS, SC0, front="W", floor="Floor_Tile")
    room("Classroom S2", "classroom", s2, s3, ZS, SC0, front="W", floor="Floor_Tile")
    room("Classroom S3", "classroom", s3, s4, ZS, SC0, front="E", floor="Floor_Tile")
    room("Research Room", "research", s4, s5, ZS, SC0, front="E", floor="Floor_Tile")
    room("Student Lounge SE", "lounge", s5, XE, ZS - 1.6, SC0, floor="Floor_Wood")
    for x in (s1, s2, s3, s4, s5):
        WALLS.append(((x, ZS), (x, SC0), []))
    WALLS.append(((XW, SC0), (XE, SC0), [(x - XW, 1.0) for x in
                  ((XW + s1) / 2, s1 + 2.0, s2 - 2.0, s2 + 2.0, s3 - 2.0, s3 + 2.0, s4 - 2.0, (s4 + s5) / 2, (s5 + XE) / 2)]))

    # Middle band of the south wing, between the two corridors, and the links between them.
    m1, m2 = 62.0, 88.0
    room("Store", "store", XW, QX0, SC1, QZ0, floor="Floor_Tile")
    room("Reading Room", "reading", KX0, m1, SC1, QZ0, front="S", floor="Floor_Wood")
    room("Cafeteria", "cafe", m1, m2, SC1, QZ0, front="S", floor="Floor_Tile")
    room("Computer Lab S", "complab", m2, KX1, SC1, QZ0, front="N", floor="Floor_Tile")
    room("Student Activity Room", "activity", QX1, XE, SC1, QZ0, front="E", floor="Floor_Wood")
    WALLS.append(((XW, SC1), (QX0, SC1), [((XW + QX0) / 2 - XW, 1.0)]))
    WALLS.append(((KX0, SC1), (KX1, SC1), [(m1 - 2.0 - KX0, 1.0), (m1 + 3.0 - KX0, 1.6), (m2 - 3.0 - KX0, 1.6), (m2 + 2.0 - KX0, 1.0)]))
    WALLS.append(((QX1, SC1), (XE, SC1), [(2.5, 1.0)]))
    for x in (QX0, KX0, KX1, QX1):
        WALLS.append(((x, SC1), (x, QZ0), []))
    WALLS.append(((m1, SC1), (m1, QZ0), []))
    WALLS.append(((m2, SC1), (m2, QZ0), []))
    CORRIDORS.append((XW, XE, SC0, SC1))
    CORRIDORS.append((QX0, KX0, SC1, QZ0))
    CORRIDORS.append((KX1, QX1, SC1, QZ0))

    # The ring corridor.
    CORRIDORS.extend([(QX0, QX1, QZ0, KZ0), (QX0, QX1, KZ1, QZ1), (QX0, KX0, KZ0, KZ1), (KX1, QX1, KZ0, KZ1)])
    # Outer corridor walls south of the courtyard band (doors into the middle band are above).
    WALLS.append(((XW, QZ0), (QX0, QZ0), [((XW + QX0) / 2 - XW, 1.0)]))
    WALLS.append(((QX1, QZ0), (XE, QZ0), [(2.0, 1.0)]))

    # West outer band: faculty cabins, an exhibition gallery (the auditorium foyer later), cabins.
    g0, g1 = wz(254.0), wz(309.0)
    cab_s = [wz(v) for v in (QZ0 + (g0 - QZ0) * k / 4 for k in range(1, 4))]
    cab_n = [wz(v) for v in (g1 + (QZ1 - g1) * k / 4 for k in range(1, 4))]
    zs = [QZ0] + cab_s + [g0]
    for k in range(len(zs) - 1):
        room("Faculty Cabin W%d" % (k + 1), "cabin", XW, QX0, zs[k], zs[k + 1], front="W", floor="Floor_Wood")
    room("Gallery", "gallery", XW, QX0, g0, g1, front="W", floor="Floor_Tile")
    zn = [g1] + cab_n + [QZ1]
    for k in range(len(zn) - 1):
        room("Faculty Cabin W%d" % (k + 5), "cabin", XW, QX0, zn[k], zn[k + 1], front="W", floor="Floor_Wood")
    for z in zs[1:] + zn[:-1]:
        WALLS.append(((XW, z), (QX0, z), []))
    doors = [((a + b_) / 2 - QZ0, 1.0) for a, b_ in zip(zs, zs[1:])]
    doors += [(g0 + 4.0 - QZ0, 1.8), ((g0 + g1) / 2 - QZ0, 1.8), (g1 - 4.0 - QZ0, 1.8)]
    doors += [((a + b_) / 2 - QZ0, 1.0) for a, b_ in zip(zn, zn[1:])]
    WALLS.append(((QX0, QZ0), (QX0, QZ1), doors))

    # East outer band: four case rooms and the passage to block 1 on the east front.
    door_z = B.EAST_DOOR_Z
    p0, p1 = ez(door_z - 1.55), ez(door_z + 1.55)
    e1z, e3z = ez(256.0), ez(306.0)
    room("Case Room E1", "case", QX1, XE, QZ0, e1z, front="S", floor="Carpet")
    room("Case Room E2", "case", QX1, XE, e1z, p0, front="N", floor="Carpet")
    room("Case Room E3", "case", QX1, XE, p1, e3z, front="S", floor="Carpet")
    room("Case Room E4", "case", QX1, XE, e3z, QZ1, front="N", floor="Carpet")
    for z in (e1z, p0, p1, e3z):
        WALLS.append(((QX1, z), (XE, z), []))
    CORRIDORS.append((QX1, XE, p0, p1))
    WALLS.append(((QX1, QZ0), (QX1, p0), [(QZ0 + 2.0 - QZ0, 1.0), (e1z - 2.0 - QZ0, 1.0), (e1z + 2.0 - QZ0, 1.0), (p0 - 2.0 - QZ0, 1.0)]))
    WALLS.append(((QX1, p1), (QX1, QZ1), [(2.0, 1.0), (e3z - 2.0 - p1, 1.0), (e3z + 2.0 - p1, 1.0), (QZ1 - 2.0 - p1, 1.0)]))

    # Courtyard band: rooms between the corridor and the courtyard, a passage out to the garden
    # in the middle of each side (on the garden path axes).
    mx = (IX0 + IX1) / 2
    mz = (IZ0 + IZ1) / 2
    # North band (X runs, between Z = CZ1 and KZ1).
    a0, a1 = snap(mx - 1.8, [S_CN]), snap(mx + 1.8, [S_CN])
    cn = [KX0] + [snap(v, [S_CN]) if CX0 < v < CX1 else v for v in (KX0 + 4.0, KX0 + 8.0, KX0 + 12.0, KX0 + 16.0)] + [a0]
    for k in range(len(cn) - 1):
        kind = "cabin" if k < 4 else "admin"
        room(("Faculty Cabin N%d" % (k + 1)) if k < 4 else "Admin Office", kind, cn[k], cn[k + 1], CZ1, KZ1,
             front="S", floor="Floor_Wood" if k < 4 else "Floor_Tile")
    ce = [a1, snap(a1 + 14.5, [S_CN])] + [snap(v, [S_CN]) if CX0 < v < CX1 else v for v in (KX1 - 12.0, KX1 - 8.0, KX1 - 4.0)] + [KX1]
    room("Meeting Room", "meeting", ce[0], ce[1], CZ1, KZ1, front="E", floor="Carpet")
    for k in range(1, len(ce) - 1):
        room("Faculty Cabin N%d" % (k + 4), "cabin", ce[k], ce[k + 1], CZ1, KZ1, front="S", floor="Floor_Wood")
    for x in cn[1:] + ce[:-1]:
        WALLS.append(((x, CZ1), (x, KZ1), []))
    CORRIDORS.append((a0, a1, CZ1, KZ1))
    dn = [((a + b_) / 2 - KX0, 1.0) for a, b_ in zip(cn, cn[1:])] + [((a + b_) / 2 - KX0, 1.0) for a, b_ in zip(ce, ce[1:])]
    WALLS.append(((KX0, KZ1), (a0, KZ1), [d for d in dn if d[0] < a0 - KX0]))
    WALLS.append(((a1, KZ1), (KX1, KZ1), [(d[0] - (a1 - KX0), d[1]) for d in dn if d[0] > a1 - KX0]))

    # South band.
    b0, b1 = snap(mx - 1.8, [S_CS]), snap(mx + 1.8, [S_CS])
    t1, t2 = snap(KX0 + 8.0, [S_CS]) if CX0 < KX0 + 8 else KX0 + 8.0, snap(KX0 + 16.0, [S_CS])
    t3, t4 = snap(KX1 - 16.0, [S_CS]), snap(KX1 - 8.0, [S_CS]) if KX1 - 8 < CX1 else KX1 - 8.0
    room("Toilets (Men)", "toilet_m", KX0, t1, KZ0, CZ0, front="N", floor="Floor_Tile")
    room("Toilets (Women)", "toilet_w", t1, t2, KZ0, CZ0, front="N", floor="Floor_Tile")
    room("Seminar Room 1", "seminar", t2, b0, KZ0, CZ0, front="W", floor="Carpet")
    room("Seminar Room 2", "seminar", b1, t3, KZ0, CZ0, front="E", floor="Carpet")
    room("Server Room", "server", t3, t4, KZ0, CZ0, front="N", floor="Floor_Tile")
    room("Housekeeping Store", "store", t4, KX1, KZ0, CZ0, front="N", floor="Floor_Tile")
    for x in (t1, t2, b0, b1, t3, t4):
        WALLS.append(((x, KZ0), (x, CZ0), []))
    CORRIDORS.append((b0, b1, KZ0, CZ0))
    WALLS.append(((KX0, KZ0), (b0, KZ0), [((KX0 + t1) / 2 - KX0, 1.0), ((t1 + t2) / 2 - KX0, 1.0), (b0 - 2.0 - KX0, 1.0)]))
    WALLS.append(((b1, KZ0), (KX1, KZ0), [(2.0, 1.0), ((t3 + t4) / 2 - b1, 1.0), ((t4 + KX1) / 2 - b1, 1.0)]))

    # West band (Z runs, between X = KX0 and CX0) and east band.
    for side, X0, X1, spans, kx in (("W", KX0, CX0, S_CW, KX0), ("E", CX1, KX1, S_CE, KX1)):
        c0, c1 = snap(mz - 1.8, [spans]), snap(mz + 1.8, [spans])
        q0, q1 = snap(IZ0 + 18.0, [spans]), snap(IZ1 - 18.0, [spans])
        zz = [CZ0, q0, c0]
        zz2 = [c1, q1, CZ1]
        names = ["Syndicate Room %s%d" % (side, k) for k in range(1, 5)]
        room(names[0], "syndicate", X0, X1, CZ0, q0, front="S")
        room(names[1], "syndicate", X0, X1, q0, c0, front="N")
        room(names[2], "syndicate", X0, X1, c1, q1, front="S")
        room(names[3], "syndicate", X0, X1, q1, CZ1, front="N")
        for z in (q0, c0, c1, q1):
            WALLS.append(((X0, z), (X1, z), []))
        CORRIDORS.append((X0, X1, c0, c1))
        WALLS.append(((kx, KZ0), (kx, c0), [((CZ0 + q0) / 2 - KZ0, 1.0), ((q0 + c0) / 2 - KZ0, 1.0)]))
        WALLS.append(((kx, c1), (kx, KZ1), [((c1 + q1) / 2 - c1, 1.0), ((q1 + CZ1) / 2 - c1, 1.0)]))
        # Short walls closing the corner rooms of the north and south bands.
        WALLS.append(((X0, CZ1), (X1, CZ1), []))
        WALLS.append(((X0, CZ0), (X1, CZ0), []))

    # Block 1 on the east front: a double-height exhibition foyer.
    room("Block 1 Foyer", "foyer", OX1, BUILDINGS["East Drum"]["x"] + BUILDINGS["East Drum"]["width"] / 2 - 1.0,
         BUILDINGS["East Drum"]["z"] - BUILDINGS["East Drum"]["depth"] / 2 + WO,
         BUILDINGS["East Drum"]["z"] + BUILDINGS["East Drum"]["depth"] / 2 - WO, front="E", floor="Floor_Tile")



# ------------------------------------------------------------------ floors above the ground
WING_LEVELS = 4          # levels 0-3 in the wings
PAV_LEVEL = 4            # the corner pavilions have a glazed fifth storey, level 4


def level_floor(k):
    return FL + k * FLOOR


def level_ceil(k):
    return CEIL + k * FLOOR


# A dog-leg stair in the outer corner of each corner pavilion, from the ground to level 4,
# with a lift beside it. Pavilion walls are solid stone below level 3, which suits a stair core.
# Stair frame: u runs from the floor landing towards the pavilion's outer north/south wall,
# v from its outer east/west wall into the building. Two 1.4 m flights with a 0.2 m spine
# wall between them; 20 risers of 0.17 m per storey.
ST_W, ST_L = 3.0, 5.8
ST_LAND = 1.5
TREAD, RISERS = 0.28, 10
ST_MID = ST_LAND + (RISERS - 1) * TREAD     # where the mid landing starts (4.02)


def pavilion_square(sx, sz):
    X0 = sx - PO if sx == OX0 else sx - P + PO
    Z0 = sz - PO if sz == OZ0 else sz - P + PO
    return X0, X0 + P, Z0, Z0 + P


class Stair:
    def __init__(self, sx, sz, tag):
        self.tag = tag
        xw = sx - PO + WO if sx == OX0 else sx + PO - WO
        zw = sz + PO - WO if sz == OZ1 else sz - PO + WO
        self.uz = 1.0 if sz == OZ1 else -1.0
        self.vx = 1.0 if sx == OX0 else -1.0
        self.o = (xw, zw - self.uz * ST_L)
        self.U, self.V = (0.0, self.uz), (self.vx, 0.0)

    def P(self, u, v):
        return (self.o[0] + self.U[0] * u + self.V[0] * v, self.o[1] + self.U[1] * u + self.V[1] * v)

    def rect(self, u0, u1, v0, v1):
        (xa, za), (xb, zb) = self.P(u0, v0), self.P(u1, v1)
        return (min(xa, xb), max(xa, xb), min(za, zb), max(za, zb))

    def hole(self):
        return self.rect(ST_LAND, ST_L, 0.0, ST_W)

    def keep_out(self):
        """Where furniture may not stand: the stair, its landing and the lift lobby."""
        return [self.rect(-1.4, ST_L + 0.1, -0.1, ST_W + 0.5), self.rect(ST_L - 2.2, ST_L + 0.1, ST_W, ST_W + 3.0)]


STAIRS = [Stair(OX0, OZ1, "NW"), Stair(OX1, OZ1, "NE"), Stair(OX0, OZ0, "SW"), Stair(OX1, OZ0, "SE")]


def upper_room(r, k):
    """What a ground-floor room becomes on level k (1-3): (name, kind, floor material)."""
    n, kind = r["name"], r["kind"]

    def pick(*opts):
        return opts[k - 1]

    if n.startswith("Student Lounge"):
        return ("Stair Hall " + n.split()[-1], "lounge", "Floor_Wood")
    if n in ("Case Room N1", "Case Room N2"):
        tag = n[-2:]
        return pick((n, "case", "Carpet"), ("Classroom " + tag, "classroom", "Floor_Tile"),
                    ("Seminar Room " + tag, "seminar", "Carpet"))
    table = {
        "Entrance Lobby": (("Faculty Lounge", "lounge", "Carpet"), ("Board Room", "meeting", "Carpet"),
                           ("Director's Office", "admin", "Carpet")),
        "Computer Lab N": (("Computer Lab N", "complab", "Floor_Tile"), ("Research Room N", "research", "Floor_Tile"),
                           ("Placement Office", "admin", "Floor_Tile")),
        "Reading Room": (("Library Reading Hall", "reading", "Floor_Wood"), ("Study Room", "reading", "Floor_Wood"),
                         ("Classroom M1", "classroom", "Floor_Tile")),
        "Cafeteria": (("Library Stacks", "stacks", "Floor_Wood"), ("Seminar Hall", "seminar", "Carpet"),
                      ("Classroom M2", "classroom", "Floor_Tile")),
        "Computer Lab S": (("Finance Lab", "complab", "Floor_Tile"), ("Computer Lab S", "complab", "Floor_Tile"),
                           ("Classroom M3", "classroom", "Floor_Tile")),
        "Student Activity Room": (("Club Room", "activity", "Floor_Wood"), ("Alumni Office", "admin", "Floor_Tile"),
                                  ("Music Room", "activity", "Floor_Wood")),
        "Gallery": (("Auditorium Foyer", "gallery", "Floor_Tile"), ("Exhibition Hall", "gallery", "Floor_Tile"),
                    ("Archive", "stacks", "Floor_Tile")),
        "Admin Office": (("Programme Office", "admin", "Floor_Tile"), ("Accounts Office", "admin", "Floor_Tile"),
                         ("Dean's Office", "admin", "Carpet")),
        "Server Room": (("Store Room", "store", "Floor_Tile"), ("Server Room 2", "server", "Floor_Tile"),
                        ("Store Room", "store", "Floor_Tile")),
    }
    if n in table:
        return pick(*table[n])
    return (n, kind, r["floor"])


ORDINAL = {1: "First Floor", 2: "Second Floor", 3: "Third Floor", 4: "Top Floor"}
LOBBY_ABOVE = {   # (room off the corridor, room by the north windows) over the entrance lobby
    1: (("Faculty Lounge", "lounge", "Carpet"), ("Faculty Dining", "cafe", "Floor_Tile")),
    2: (("Executive Lounge", "lounge", "Carpet"), ("Board Room", "board", "Carpet")),
    3: (("Director's Secretariat", "admin", "Floor_Tile"), ("Director's Office", "director", "Carpet")),
}


def make_level(k):
    """Room plan of level k. The wings stack the same partitions on every floor, so each level
    starts from the ground plan and renames the rooms."""
    global ROOMS, WALLS, CORRIDORS
    ROOMS, WALLS, CORRIDORS = [], [], []
    if k == PAV_LEVEL:
        for st, (sx, sz) in zip(STAIRS, ((OX0, OZ1), (OX1, OZ1), (OX0, OZ0), (OX1, OZ0))):
            X0, X1, Z0, Z1 = pavilion_square(sx, sz)
            room("Pavilion Lounge " + st.tag, "lounge", X0 + WO, X1 - WO, Z0 + WO, Z1 - WO,
                 front="N" if sz == OZ0 else "S", floor="Floor_Wood")
        for r in ROOMS:
            r["name"] += " (%s)" % ORDINAL[k]
        return dict(rooms=ROOMS, walls=WALLS, corridors=CORRIDORS)
    plan()
    if k > 0:
        ROOMS[:] = [r for r in ROOMS if r["kind"] != "foyer"]
        for r in list(ROOMS):
            if r["kind"] == "lobby":
                # Over the lobby: a room off the corridor, and a larger one through it by the windows.
                X0, X1, Z0, Z1 = r["rect"]
                zm = (Z0 + Z1) / 2
                WALLS.append(((X0, Z0), (X1, Z0), [((X1 - X0) / 2, 1.6)]))
                WALLS.append(((X0, zm), (X1, zm), [((X1 - X0) / 2 - 3.0, 1.0), ((X1 - X0) / 2 + 3.0, 1.0)]))
                (n0, k0, f0_), (n1, k1, f1_) = LOBBY_ABOVE[k]
                r["rect"], r["name"], r["kind"], r["floor"] = (X0, X1, Z0, zm), n0, k0, f0_
                room(n1, k1, X0, X1, zm, Z1, front="S", floor=f1_)
                ROOMS[-1]["name"] = "%s (%s)" % (n1, ORDINAL[k])
                r["name"] = "%s (%s)" % (n0, ORDINAL[k])
                continue
            name, kind, floor = upper_room(r, k)
            r["name"], r["kind"], r["floor"] = "%s (%s)" % (name, ORDINAL[k]), kind, floor
    return dict(rooms=ROOMS, walls=WALLS, corridors=CORRIDORS)


LEVEL_PLANS = [make_level(k) for k in range(PAV_LEVEL + 1)]


def use_level(k):
    """Points the module's ROOMS / WALLS / CORRIDORS at level k's plan."""
    global ROOMS, WALLS, CORRIDORS
    ROOMS, WALLS, CORRIDORS = LEVEL_PLANS[k]["rooms"], LEVEL_PLANS[k]["walls"], LEVEL_PLANS[k]["corridors"]


use_level(0)


# ------------------------------------------------------------------ the shell
def outer_outline():
    """Outline of the ground floor (wings plus proud corner pavilions), anticlockwise in world X/Z."""
    return [
        (OX0 - PO, OZ0 - PO), (E0, OZ0 - PO), (E0, OZ0), (E1, OZ0), (E1, OZ0 - PO), (OX1 + PO, OZ0 - PO),
        (OX1 + PO, F0), (OX1, F0), (OX1, F1), (OX1 + PO, F1), (OX1 + PO, OZ1 + PO), (E1, OZ1 + PO),
        (E1, OZ1), (E0, OZ1), (E0, OZ1 + PO), (OX0 - PO, OZ1 + PO), (OX0 - PO, F1), (OX0, F1),
        (OX0, F0), (OX0 - PO, F0),
    ]


def block1_outline():
    """Block 1's plan (as in build_buildings.academic_block), in world X/Z, from the east facade."""
    ed = BUILDINGS["East Drum"]
    EX1 = ed["x"] + ed["width"] / 2
    EZ0_, EZ1_ = ed["z"] - ed["depth"] / 2, ed["z"] + ed["depth"] / 2
    span = (EZ1_ - EZ0_) / 2
    bulge = 7.0
    R = (span ** 2 + bulge ** 2) / (2 * bulge)
    ang = math.asin(span / R)
    cx = EX1 - R
    base_x = EX1 - bulge
    arc = [(cx + R * math.cos(t), ed["z"] + R * math.sin(t)) for t in [-ang + 2 * ang * k / 16 for k in range(17)]]
    return [(OX1, EZ0_), (base_x, EZ0_)] + arc[1:-1] + [(base_x, EZ1_), (OX1, EZ1_)], (EZ0_, EZ1_)


def wall_with_windows(m, A, B_, normal, openings, depth, thick, z0, z1, s_lo=None, s_hi=None):
    """Inside face of an outer wall: holes where the facade has windows or doors, reveals back to
    the glass, a sill board, and a clear pane so the glass reads from inside. Openings are
    clipped to the storey (z0..z1), so a ribbon window that starts below the floor still works."""
    ln = Line(m, A, B_, normal)
    s_lo = 0.0 if s_lo is None else s_lo
    s_hi = ln.L if s_hi is None else s_hi
    used = []
    for o in openings:
        h = (max(s_lo, o[0]), min(s_hi, o[1]), max(z0, o[2]), min(z1, o[3]))
        if h[1] - h[0] > 1e-3 and h[3] - h[2] > 1e-3:
            used.append((o, h))
    ln.wall(s_lo, s_hi, z0, z1, 0.0, "Wall_Paint", [h for _, h in used])
    back = -(thick - depth)      # the glass sits this far behind the inside face
    for o, (s0, s1, h0, h1) in used:
        if len(o) > 4 and o[4]:
            continue  # doorways: the facade's own reveals reach the inside face
        ln.side(s0, h0, h1, back, 0.0, "Wall_Paint", 1)
        ln.side(s1, h0, h1, back, 0.0, "Wall_Paint", -1)
        if h1 < z1 - 1e-3:
            ln.hband(s0, s1, h1, back, 0.0, "Wall_Paint", up=False)
        if h0 > z0 + 0.05:
            # Sill board, standing a little proud of the wall.
            ln.hband(s0 - 0.05, s1 + 0.05, h0, back, 0.06, "Laminate", up=True)
            ln.rect(s0 - 0.05, s1 + 0.05, h0 - 0.04, h0, 0.06, "Laminate")
            ln.hband(s0 - 0.05, s1 + 0.05, h0 - 0.04, 0.0, 0.06, "Laminate", up=False)
            ln.side(s0 - 0.05, h0 - 0.04, h0, 0.0, 0.06, "Laminate", -1)
            ln.side(s1 + 0.05, h0 - 0.04, h0, 0.0, 0.06, "Laminate", 1)
        ln.rect(s0, s1, h0, h1, back + 0.01, "Glass_Clear")
    return ln


def opening_shift(A, B_, A2, t):
    return (A2[0] - A[0]) * t[0] + (A2[1] - A[1]) * t[1]


def hrect_cut(m, X0, X1, Z0, Z1, z, mat, holes, up=True):
    """hrect with rectangular holes (world X0, X1, Z0, Z1) cut out of it."""
    hs = [h for h in holes if h[0] < X1 and h[1] > X0 and h[2] < Z1 and h[3] > Z0]
    if not hs:
        hrect(m, X0, X1, Z0, Z1, z, mat, up)
        return
    xs, zs = {X0, X1}, {Z0, Z1}
    for h in hs:
        xs.update(v for v in (h[0], h[1]) if X0 < v < X1)
        zs.update(v for v in (h[2], h[3]) if Z0 < v < Z1)
    xs, zs = sorted(xs), sorted(zs)
    for a, b_ in zip(xs, xs[1:]):
        for c, d in zip(zs, zs[1:]):
            mx, mz = (a + b_) / 2, (c + d) / 2
            if not any(h[0] <= mx <= h[1] and h[2] <= mz <= h[3] for h in hs):
                hrect(m, a, b_, c, d, z, mat, up)


# Doorways from the west wing into the auditorium hall: at ground level into the side
# aisles, on the first floor onto the balcony at the top of the raked seating.
HALL_DOORS = {0: (260.0, 303.0), 1: (275.5, 287.5)}
HALL_DOOR_W, HALL_DOOR_H = 1.8, 2.4


def hall_doors(A, B_, k):
    sgn = 1 if B_[1] > A[1] else -1
    out = []
    for Z in HALL_DOORS.get(k, ()):
        s = (Z - A[1]) * sgn
        out.append((s - HALL_DOOR_W / 2, s + HALL_DOOR_W / 2, level_floor(k), level_floor(k) + HALL_DOOR_H, True))
    return out


def level_openings(A, B_, kind, k):
    """Openings (s0, s1, z0, z1, is_door) of a perimeter run on level k, as academic_facade draws
    them: two slender windows per bay on the outer walls, one wider one on the courtyard, the
    blue glass ribbon on the top floor outside; nothing where the hall covers the west wall."""
    L = math.dist(A, B_)
    bays, bw = B.facade_bays(L)
    skip = B.hall_bays(A, B_) if kind == "outer" else []
    zf = k * FLOOR
    out = []
    if k == 0:
        for o in run_openings(A, B_, kind):
            if not any(i * bw <= (o[0] + o[1]) / 2 <= (i + 1) * bw for i in skip):
                out.append(o)
    elif kind == "outer" and k == WING_LEVELS - 1:
        for c0, c1 in B.open_spans(bays, bw, skip):
            out.append((c0, c1, zf + 0.5, zf + FLOOR - 0.35, False))
    else:
        for i in range(bays):
            if i in skip:
                continue
            s0, s1 = i * bw, (i + 1) * bw
            if kind == "outer":
                mid = (s0 + s1) / 2
                out.append((s0 + 0.42, mid - 0.14, zf + 0.75, zf + FLOOR - 0.4, False))
                out.append((mid + 0.14, s1 - 0.42, zf + 0.75, zf + FLOOR - 0.4, False))
            else:
                out.append((s0 + 0.8, s1 - 0.8, zf + 0.9, zf + FLOOR - 0.4, False))
    if skip:
        out += hall_doors(A, B_, k)
    return out


def pavilion_openings(L, k):
    """The pavilions' row of square windows on level 3 (fluted_facade)."""
    if k != 3:
        return []
    bays = max(1, round(L / 3.0))
    bw = L / bays
    zt = 3 * FLOOR
    return [(i * bw + 0.7, (i + 1) * bw - 0.7, zt + 0.9, zt + FLOOR - 0.6, False) for i in range(bays)]


# The ground floor's plan strips: the four wings, plus the pavilions' proud strips.
WING_STRIPS = [(OX0, OX1, IZ1, OZ1), (OX0, OX1, OZ0, IZ0), (OX0, IX0, IZ0, IZ1), (IX1, OX1, IZ0, IZ1),
               (OX0 - PO, OX0, F1, OZ1 + PO), (OX0, E0, OZ1, OZ1 + PO), (E1, OX1, OZ1, OZ1 + PO),
               (OX1, OX1 + PO, F1, OZ1 + PO), (OX0 - PO, OX0, OZ0 - PO, F0), (OX0, E0, OZ0 - PO, OZ0),
               (E1, OX1, OZ0 - PO, OZ0), (OX1, OX1 + PO, OZ0 - PO, F0)]


def outer_walls(m, k):
    f0, c0 = level_floor(k), level_ceil(k)
    poly = outer_outline()
    inner = inset_polygon(poly, WO)
    n = len(poly)
    area = sum(poly[i][0] * poly[(i + 1) % n][1] - poly[(i + 1) % n][0] * poly[i][1] for i in range(n))
    for i in range(n):
        A, B_ = poly[i], poly[(i + 1) % n]
        A2, B2 = inner[i], inner[(i + 1) % n]
        L2 = math.dist(A2, B2)
        if L2 < 1e-3:
            continue
        t = ((B2[0] - A2[0]) / L2, (B2[1] - A2[1]) / L2)
        inward = (-t[1], t[0]) if area > 0 else (t[1], -t[0])
        shift = opening_shift(A, B_, A2, t)
        depth = 0.35
        ops = None
        for (RA, RB) in OUTER_RUNS:
            if math.dist(RA, A) < 1e-6 and math.dist(RB, B_) < 1e-6:
                shift = opening_shift(RA, RB, A2, t)
                ops = level_openings(RA, RB, "outer", k)
                if k == WING_LEVELS - 1:
                    depth = 0.44   # the ribbon glass is set back the full wall depth
        if ops is None:
            ops = pavilion_openings(math.dist(A, B_), k) if abs(math.dist(A, B_) - P) < 0.01 else []
            if ops:
                depth = 0.3
        ops = [(o[0] - shift, o[1] - shift, o[2], o[3], o[4]) for o in ops]
        wall_with_windows(m, A2, B2, inward, ops, depth, WO, f0, c0)


def court_walls(m, k):
    f0, c0 = level_floor(k), level_ceil(k)
    for (A, B_) in COURT_RUNS:
        L = math.dist(A, B_)
        t = ((B_[0] - A[0]) / L, (B_[1] - A[1]) / L)
        c = ((IX0 + IX1) / 2, (IZ0 + IZ1) / 2)
        nrm = (t[1], -t[0])
        mid = ((A[0] + B_[0]) / 2, (A[1] + B_[1]) / 2)
        if nrm[0] * (mid[0] - c[0]) + nrm[1] * (mid[1] - c[1]) < 0:
            nrm = (-nrm[0], -nrm[1])     # away from the courtyard: into the building
        A2 = (A[0] + nrm[0] * WC - t[0] * WC, A[1] + nrm[1] * WC - t[1] * WC)
        B2 = (B_[0] + nrm[0] * WC + t[0] * WC, B_[1] + nrm[1] * WC + t[1] * WC)
        openings = [(o[0] + WC, o[1] + WC, o[2], o[3], o[4]) for o in level_openings(A, B_, "court", k)]
        wall_with_windows(m, A2, B2, nrm, openings, 0.22, WC, f0, c0)


def wing_level(m, k):
    """One storey of the four wings: floor and ceiling (open over the stairs), the outer and
    courtyard walls with that storey's windows, partitions, tiered case rooms, floor finishes."""
    use_level(k)
    f0, c0 = level_floor(k), level_ceil(k)
    holes = [st.hole() for st in STAIRS]
    for X0, X1, Z0, Z1 in WING_STRIPS:
        hrect_cut(m, X0, X1, Z0, Z1, f0, "Floor_Tile", holes if k > 0 else [])
        hrect_cut(m, X0, X1, Z0, Z1, c0, "Ceiling", holes, up=False)
    outer_walls(m, k)
    court_walls(m, k)
    for A, B_, doors in WALLS:
        partition(m, A, B_, f0, c0, doors)
    for r in ROOMS:
        if r["kind"] == "case":
            r["tiers"] = case_tiers(m, r, f0)
    # Floor finishes inside rooms, a centimetre above the base floor.
    for r in ROOMS:
        if r["floor"] and r["floor"] != "Floor_Tile":
            X0, X1, Z0, Z1 = r["rect"]
            hrect_cut(m, X0 + PART / 2, X1 - PART / 2, Z0 + PART / 2, Z1 - PART / 2, f0 + 0.01, r["floor"],
                      holes if k > 0 else [])
    if k == 0:
        # The lobby: four stone-clad columns and a round planter in the middle.
        for r in ROOMS:
            if r["kind"] == "lobby":
                X0, X1, Z0, Z1 = r["rect"]
                for fx in (1 / 3, 2 / 3):
                    for fz in (1 / 3, 2 / 3):
                        cx, cz = X0 + (X1 - X0) * fx, Z0 + (Z1 - Z0) * fz
                        m.box(cx - 0.35, cx + 0.35, cz - 0.35, cz + 0.35, FL, CEIL, "Stone", None)
                        m.box(cx - 0.45, cx + 0.45, cz - 0.45, cz + 0.45, FL, FL + 0.12, "Wood", "Wood")
                cx, cz = (X0 + X1) / 2, (Z0 + Z1) / 2 - 1.0
                m.cylinder_local(*m.b(cx, cz), 1.9, FL, FL + 0.45, "Stone", "Stone", segments=24)
                m.cylinder_local(*m.b(cx, cz), 1.7, FL + 0.45, FL + 0.5, "Planter", "Planter", segments=24)
                r["planter"] = (cx, cz)


def pavilion_tops(m):
    """Level 4: the glazed top storey of each corner pavilion, above the wing roofs."""
    use_level(PAV_LEVEL)
    f0, c0 = level_floor(PAV_LEVEL), level_ceil(PAV_LEVEL)
    zg = 4 * FLOOR
    for st, (sx, sz) in zip(STAIRS, ((OX0, OZ1), (OX1, OZ1), (OX0, OZ0), (OX1, OZ0))):
        X0, X1, Z0, Z1 = pavilion_square(sx, sz)
        hrect_cut(m, X0, X1, Z0, Z1, f0, "Floor_Wood", [st.hole()])
        hrect(m, X0, X1, Z0, Z1, c0, "Ceiling", up=False)
        sq = [(X0, Z0), (X1, Z0), (X1, Z1), (X0, Z1)]
        inner = inset_polygon(sq, WO)
        area = sum(sq[i][0] * sq[(i + 1) % 4][1] - sq[(i + 1) % 4][0] * sq[i][1] for i in range(4))
        for i in range(4):
            A2, B2 = inner[i], inner[(i + 1) % 4]
            L2 = math.dist(A2, B2)
            t = ((B2[0] - A2[0]) / L2, (B2[1] - A2[1]) / L2)
            inward = (-t[1], t[0]) if area > 0 else (t[1], -t[0])
            wall_with_windows(m, A2, B2, inward, [(0.2, L2 - 0.2, zg + 0.3, 5 * FLOOR - 0.2, False)], 0.3, WO, f0, c0)


def ground_extras(m):
    """The double-height vestibule in the porch's glass bay, and block 1's foyer."""
    bc = m.b(EX, OZ1)
    pts = arc_points(bc[0], bc[1], 9.0, 0.0, -math.pi, 14)
    m.face([(p[0], p[1], FL) for p in pts], "Floor_Tile", (0, 0, 1))
    m.face([(p[0], p[1], HIGH_CEIL) for p in pts], "Ceiling", (0, 0, -1))
    for k in range(len(pts) - 1):
        a, b_ = pts[k], pts[k + 1]
        mid = ((a[0] + b_[0]) / 2 - bc[0], (a[1] + b_[1]) / 2 - bc[1])
        Lm = math.hypot(*mid)
        nloc = (mid[0] / Lm, mid[1] / Lm)       # outward, local
        r = B.Run(m, a, b_, nloc)
        door = k in B.BAY_DOOR_SEGMENTS
        if not door:
            r.rect(0, r.L, FL, 0.9, -0.3, "Stone", facing=-1)
            r.hband(0, r.L, 0.9, -0.3, 0.0, "Stone", up=True)
        r.rect(0, r.L, B.DOOR_TOP if door else 0.9, HIGH_CEIL, -0.07, "Glass_Clear", facing=-1)
    # Fill the gap between the bay floor and the wing floor (the facade wall's thickness).
    hrect(m, EX - 9.0, EX + 9.0, OZ1 - 0.01, OZ1 + 0.01, FL, "Floor_Tile")

    outline, (bz0, bz1) = block1_outline()
    hpoly(m, outline, FL, "Floor_Tile")
    hpoly(m, outline, HIGH_CEIL, "Ceiling", up=False)
    binner = inset_polygon(outline, WO)
    nb = len(outline)
    barea = sum(outline[k][0] * outline[(k + 1) % nb][1] - outline[(k + 1) % nb][0] * outline[k][1] for k in range(nb))
    for k in range(nb):
        A, B_ = outline[k], outline[(k + 1) % nb]
        if abs(A[0] - OX1) < 1e-6 and abs(B_[0] - OX1) < 1e-6:
            continue   # the east wing's facade
        A2, B2 = binner[k], binner[(k + 1) % nb]
        L2 = math.dist(A2, B2)
        t = ((B2[0] - A2[0]) / L2, (B2[1] - A2[1]) / L2)
        inward = (-t[1], t[0]) if barea > 0 else (t[1], -t[0])
        L = math.dist(A, B_)
        if L < 6.0:
            dk = curved_door_side(outline, k)
            if dk:
                os0, os1 = (0.35, L) if dk == 1 else (0.0, L - 0.35)
                ops = [(os0, os1, FL, B.DOOR_TOP, True), (0.35, L - 0.35, B.DOOR_TOP + 0.1, 2 * FLOOR - 0.4, False)]
            else:
                ops = [(0.35, L - 0.35, 1.0, 2 * FLOOR - 0.4, False)]
            shift = opening_shift(A, B_, A2, t)
            ops = [(o[0] - shift, o[1] - shift, o[2], o[3], o[4]) for o in ops]
            wall_with_windows(m, A2, B2, inward, ops, 0.35, WO, FL, HIGH_CEIL)
        else:
            Lr = abs(A[0] - B_[0]) + 0.5
            base = B.facade_openings(Lr, "outer")
            ops_r = base + [(o[0], o[1], o[2] + FLOOR - 0.2, o[3] + FLOOR, False) for o in base]
            ops = []
            for o in ops_r:
                sa = (OX1 - 0.5 + o[0] - A2[0]) / t[0]
                sb = (OX1 - 0.5 + o[1] - A2[0]) / t[0]
                ops.append((min(sa, sb), max(sa, sb), o[2], o[3], False))
            s_x = (OX1 - A2[0]) / t[0]
            s_lo, s_hi = (s_x, L2) if t[0] > 0 else (0.0, s_x)
            wall_with_windows(m, A2, B2, inward, ops, 0.35, WO, FL, HIGH_CEIL, s_lo=s_lo, s_hi=s_hi)


# ------------------------------------------------------------------ stairs
def st_box(m, st, u0, u1, v0, v1, z0, z1, side="Stone", top="Floor_Tile", bottom=False):
    X0, X1, Z0, Z1 = st.rect(u0, u1, v0, v1)
    m.box(X0, X1, Z0, Z1, z0, z1, side, top, bottom=bottom)


def st_face(m, st, corners, mat, hint_uv):
    """A face given as (u, v, height) corners; hint_uv = (du, dv, dz) in the stair frame."""
    pts = [(*m.b(*st.P(u, v)), h) for u, v, h in corners]
    du, dv, dz = hint_uv
    wx = st.U[0] * du + st.V[0] * dv
    wz = st.U[1] * du + st.V[1] * dv
    m.face(pts, mat, (-wx, -wz, dz))


def rail(m, st, u0, h0, u1, h1, v, w=0.05, mat="Steel"):
    """A sloping handrail bar from (u0, h0) to (u1, h1) along the wall at v."""
    a0, a1, hw = v - w / 2, v + w / 2, w / 2
    st_face(m, st, [(u0, a0, h0 + hw), (u1, a0, h1 + hw), (u1, a1, h1 + hw), (u0, a1, h0 + hw)], mat, (0, 0, 1))
    st_face(m, st, [(u0, a0, h0 - hw), (u1, a0, h1 - hw), (u1, a1, h1 - hw), (u0, a1, h0 - hw)], mat, (0, 0, -1))
    st_face(m, st, [(u0, a0, h0 - hw), (u1, a0, h1 - hw), (u1, a0, h1 + hw), (u0, a0, h0 + hw)], mat, (0, -1, 0))
    st_face(m, st, [(u0, a1, h0 - hw), (u1, a1, h1 - hw), (u1, a1, h1 + hw), (u0, a1, h0 + hw)], mat, (0, 1, 0))


def st_wall(m, st, u0, u1, v0, v1, z0, z1):
    X0, X1, Z0, Z1 = st.rect(u0, u1, v0, v1)
    m.box(X0, X1, Z0, Z1, z0, z1, "Wall_Paint", "Wall_Paint")


def stair_geometry(m, stairs=None, top_level=None, floor_fn=None, ceil_fn=None, side_walls=False):
    """Flights, landings, walls and handrails of the stairs (by default the four pavilion
    stairs). On the ground floor the flights are solid; above it each tread is a 0.3 m slab,
    which leaves headroom over the flight below."""
    stairs = STAIRS if stairs is None else stairs
    top_level = PAV_LEVEL if top_level is None else top_level
    floor_fn = floor_fn or level_floor
    ceil_fn = ceil_fn or level_ceil
    rise = FLOOR / (2 * RISERS)
    top = ceil_fn(top_level)
    bottom = floor_fn(0)
    for st in stairs:
        for k in range(top_level):
            h = floor_fn(k)

            def base(z):
                return h if k == 0 else z - 0.3

            for i in range(1, RISERS):                       # flight A, up towards the outer wall
                u0 = ST_LAND + (i - 1) * TREAD
                z = h + i * rise
                st_box(m, st, u0, u0 + TREAD, 0.0, 1.4, base(z), z, bottom=k > 0)
            zm = h + RISERS * rise                           # mid landing
            st_box(m, st, ST_MID, ST_L, 0.0, ST_W, h if k == 0 else zm - 0.25, zm, bottom=k > 0)
            for j in range(1, RISERS):                       # flight B, back towards the landing
                u1 = ST_MID - (j - 1) * TREAD
                z = zm + j * rise
                st_box(m, st, u1 - TREAD, u1, 1.6, ST_W, base(z), z, bottom=k > 0)
            # Handrails on the spine wall and the outer walls.
            ra0, ra1 = h + rise + 0.9, zm + 0.9
            rail(m, st, ST_LAND, ra0, ST_MID, ra1, 1.4 - 0.06)
            rail(m, st, ST_LAND, ra0, ST_MID, ra1, 0.06)
            rail(m, st, ST_MID, zm + rise + 0.9, ST_LAND, h + FLOOR + 0.9, 1.6 + 0.06)
            rail(m, st, ST_MID, zm + rise + 0.9, ST_LAND, h + FLOOR + 0.9, ST_W - 0.06)
            # The slab edge over the floor landing, seen from the stair.
            st_face(m, st, [(ST_LAND, 0.0, ceil_fn(k)), (ST_LAND, ST_W, ceil_fn(k)),
                            (ST_LAND, ST_W, floor_fn(k + 1)), (ST_LAND, 0.0, floor_fn(k + 1))], "Wall_Paint", (1, 0, 0))
        # Spine wall between the flights and the wall closing the stair off from the rooms.
        st_wall(m, st, ST_LAND, ST_MID, 1.4, 1.6, bottom, top)
        st_wall(m, st, ST_LAND, ST_L, ST_W, ST_W + PART, bottom, top)
        if side_walls:
            st_wall(m, st, ST_LAND, ST_L, -PART, 0.0, bottom, top)
        # On the top floor, a balustrade where flight A would have gone on up.
        zt = floor_fn(top_level)
        st_wall(m, st, ST_LAND, ST_LAND + 0.1, 0.0, 1.4, zt, zt + 1.05)


# ------------------------------------------------------------------ the Mandapam auditorium
# The hall stands against the west wing (build_buildings.academic_block): straight sides from the
# wing out to AX0 + 12, then a segmental curve bulging 12 m to the west. Inside: a stage in the
# curve, and seats in a fan of curved rows round it, raked up to a balcony at first-floor level
# that the west wing's first floor opens onto. Side aisles at ground level lead in from the
# ground-floor foyer (the gallery).
AU = BUILDINGS["Mandapam Auditorium (west drum)"]
AX0 = AU["x"] - AU["width"] / 2
AZ0, AZ1 = AU["z"] - AU["depth"] / 2, AU["z"] + AU["depth"] / 2
AZC = AU["z"]
AR = ((AZ1 - AZ0) / 2) ** 2 / 24.0 + 6.0     # circle through the corners with a 12 m bulge
HALL_E = OX0 - 0.1                            # the hall face of the wall it shares with the wing
HALL_S, HALL_N = AZ0 + 0.6, AZ1 - 0.6          # side linings, set in behind the facade's window glass
HALL_CEIL = 13.0
HC = (13.0, AZC)                              # centre of the seating fan and of the stage front
STAGE_R, STAGE_H = 4.5, 0.9
ROW_R0, ROW_PITCH, ROWS = 6.2, 0.88, 11
RAKE = FLOOR / ROWS                           # rise per row; the balcony is one storey up
SEAT_MAX = math.radians(77.0)                 # half-angle of the seating fan
SIDE_WALK = 1.0                               # walkway at both ends of each row
AISLES = (0.0, math.radians(-40.0), math.radians(40.0))
AISLE_W = 1.3
SEAT_W = 0.55


def hall_arc_x(Z, inset=WO):
    """X of the hall's curved west wall (inside face when inset = WO) at world Z."""
    return AX0 + AR - math.sqrt((AR - inset) ** 2 - (Z - AZC) ** 2)


def hall_outline():
    """Inside outline of the hall, anticlockwise in world X/Z (east wall first)."""
    a_end = math.asin((AZC - HALL_S) / (AR - WO))
    arc = []
    for i in range(25):
        t = -a_end + 2 * a_end * i / 24
        arc.append((AX0 + AR - (AR - WO) * math.cos(t), AZC + (AR - WO) * math.sin(t)))
    # The arc runs from south to north; the outline goes up the east wall, then back down the curve.
    return [(HALL_E, HALL_S), (HALL_E, HALL_N)] + arc[::-1]


def fan_point(r, a):
    return (HC[0] + r * math.cos(a), HC[1] + r * math.sin(a))


def row_radius(i):
    return ROW_R0 + i * ROW_PITCH


def row_end(i):
    return SEAT_MAX + SIDE_WALK / row_radius(i)


def sector_faces(m, r0, r1, a0, a1, z_lo, z_hi, top="Carpet", side="Wood", segs=None, inner=True, outer=False, ends=True):
    """A stepped annular sector of the fan: top, inner riser, and end faces down to z_lo."""
    segs = segs or max(2, int((a1 - a0) * r1 / 1.5))
    ring0 = [fan_point(r0, a0 + (a1 - a0) * s / segs) for s in range(segs + 1)]
    ring1 = [fan_point(r1, a0 + (a1 - a0) * s / segs) for s in range(segs + 1)]
    for s in range(segs):
        m.face([(*m.b(*ring0[s]), z_hi), (*m.b(*ring0[s + 1]), z_hi), (*m.b(*ring1[s + 1]), z_hi), (*m.b(*ring1[s]), z_hi)],
               top, (0, 0, 1))
        am = a0 + (a1 - a0) * (s + 0.5) / segs
        if inner:
            m.face([(*m.b(*ring0[s]), z_lo), (*m.b(*ring0[s + 1]), z_lo), (*m.b(*ring0[s + 1]), z_hi), (*m.b(*ring0[s]), z_hi)],
                   side, (math.cos(am), math.sin(am), 0.0))       # faces the stage (world -radial = local +radial)
        if outer:
            m.face([(*m.b(*ring1[s]), z_lo), (*m.b(*ring1[s + 1]), z_lo), (*m.b(*ring1[s + 1]), z_hi), (*m.b(*ring1[s]), z_hi)],
                   side, (-math.cos(am), -math.sin(am), 0.0))
    if ends:
        for j, a, sgn in ((0, a0, -1), (segs, a1, 1)):
            ang = a + sgn * math.pi / 2
            m.face([(*m.b(*ring0[j]), z_lo), (*m.b(*ring1[j]), z_lo), (*m.b(*ring1[j]), z_hi), (*m.b(*ring0[j]), z_hi)],
                   side, (-math.cos(ang), -math.sin(ang), 0.0))


def oriented_box(m, cx, cz, length, width, ang, z0, z1, mat, top=None):
    """Box of a length along angle ang (world, radians from +X towards +Z) and a width."""
    c, s = math.cos(ang), math.sin(ang)
    hl, hw = length / 2, width / 2
    corners = [(cx + c * u - s * v, cz + s * u + c * v) for u, v in ((-hl, -hw), (hl, -hw), (hl, hw), (-hl, hw))]
    m.prism([m.b(X, Z) for X, Z in corners], z0, z1, mat, top or mat)


def hall_walk_spans(i):
    """Angular stretches of row i that are walkways (aisles and the ends), not seats."""
    r = row_radius(i)
    out = [(-row_end(i), -SEAT_MAX), (SEAT_MAX, row_end(i))]
    for a in AISLES:
        half = AISLE_W / 2 / r
        out.append((a - half, a + half))
    return sorted(out)


def hall_seat_spans(i):
    walks = hall_walk_spans(i)
    out = []
    for (a0, a1), (b0, b1) in zip(walks, walks[1:]):
        if b0 > a1:
            out.append((a1, b0))
    return out


def auditorium(m):
    outline = hall_outline()
    f0 = FL
    hpoly(m, outline, f0, "Floor_Wood")
    hpoly(m, outline, HALL_CEIL, "Ceiling", up=False)
    # Straight side walls (their facades have windows; the hall is lined with panelling inside).
    for A, B_, nrm in (((HALL_E, HALL_S), (hall_arc_x(HALL_S), HALL_S), (0.0, 1.0)),
                       ((hall_arc_x(HALL_N), HALL_N), (HALL_E, HALL_N), (0.0, -1.0))):
        ln = Line(m, A, B_, nrm)
        ln.wall(0, math.dist(A, B_), f0, HALL_CEIL, 0.0, "Wall_Paint", [])
        # Dark blinds behind the side facades' windows, seen from outside.
        ln.rect(0, math.dist(A, B_), f0, 4 * FLOOR, -0.1, "Plastic", -1.0)
    # The curved west wall, with the clerestory glass band near the top.
    arc = outline[2:]
    zt = 4 * FLOOR + 1.0
    for a, b_ in zip(arc, arc[1:]):
        L = math.dist(a, b_)
        mid = ((a[0] + b_[0]) / 2, (a[1] + b_[1]) / 2)
        c = (AX0 + AR, AZC)
        nrm = ((c[0] - mid[0]), (c[1] - mid[1]))
        ln_ = math.hypot(*nrm)
        wall_with_windows(m, a, b_, (nrm[0] / ln_, nrm[1] / ln_), [(0.0, L, zt - 3.0, zt - 0.4, False)], 0.3, WO, f0, HALL_CEIL)
    # The wall shared with the wing, seen from the hall, with the doorways' reveals.
    east = Line(m, (HALL_E, HALL_S), (HALL_E, HALL_N), (-1.0, 0.0))
    holes = []
    for k, zs in HALL_DOORS.items():
        for Z in zs:
            s = Z - HALL_S
            holes.append((s - HALL_DOOR_W / 2, s + HALL_DOOR_W / 2, level_floor(k), level_floor(k) + HALL_DOOR_H))
    east.wall(0, east.L, f0, HALL_CEIL, 0.0, "Wall_Paint", holes)
    thick = (OX0 + WO) - HALL_E
    for s0, s1, h0, h1 in holes:
        east.side(s0, h0, h1, -thick, 0.0, "Wall_Paint", 1)
        east.side(s1, h0, h1, -thick, 0.0, "Wall_Paint", -1)
        east.hband(s0, s1, h1, -thick, 0.0, "Wall_Paint", up=False)
        east.hband(s0, s1, h0, -thick, 0.0, "Floor_Tile", up=True)
        for d, f in ((0.0, 1.0),):
            arch_box(east, s0 - 0.08, s0, h0, h1 + 0.08, d, f)
            arch_box(east, s1, s1 + 0.08, h0, h1 + 0.08, d, f)
            arch_box(east, s0 - 0.08, s1 + 0.08, h1, h1 + 0.08, d, f)

    # Stage: a curved front round the fan centre, splayed sides back to the curved wall.
    front = [fan_point(STAGE_R, math.radians(a)) for a in range(-100, 101, 10)]
    zb0, zb1 = AZC - 12.0, AZC + 12.0
    back = [(hall_arc_x(z), z) for z in [zb1 - (zb1 - zb0) * i / 12 for i in range(13)]]
    stage = [(hall_arc_x(zb0), zb0)] + front + back
    stage_local = B.anticlockwise(B.dedupe([m.b(X, Z) for X, Z in stage]))
    for k in range(len(stage_local)):
        a, b_ = stage_local[k], stage_local[(k + 1) % len(stage_local)]
        e = (b_[0] - a[0], b_[1] - a[1])
        m.face([(a[0], a[1], f0), (b_[0], b_[1], f0), (b_[0], b_[1], f0 + STAGE_H), (a[0], a[1], f0 + STAGE_H)], "Wood",
               (e[1], -e[0], 0.0))
    m.face([(p[0], p[1], f0 + STAGE_H) for p in stage_local], "Floor_Wood", (0, 0, 1))
    # Steps down the middle of the stage front, to the centre aisle.
    xf = HC[0] + math.sqrt(STAGE_R ** 2 - 1.5 ** 2)
    for i in range(3):
        m.box(xf - 0.3, xf + 0.3 * (3 - i), AZC - 1.5, AZC + 1.5, f0, f0 + STAGE_H * (i + 1) / 4, "Wood", "Floor_Wood")

    # Raked seating: row i stands RAKE * i above the floor; the walkways get a half step.
    for i in range(1, ROWS):
        r = row_radius(i)
        sector_faces(m, r - ROW_PITCH / 2, r + ROW_PITCH / 2, -row_end(i), row_end(i), f0, f0 + RAKE * i)
    for i in range(ROWS):
        r = row_radius(i)
        z = f0 + RAKE * i
        for a0, a1 in hall_walk_spans(i):
            sector_faces(m, r, r + ROW_PITCH / 2, a0, a1, z, z + RAKE / 2, segs=2)
        if i >= 2:
            # Parapets along the open ends of the rows.
            for a in (-row_end(i), row_end(i)):
                inw = -1 if a > 0 else 1
                aa = a + inw * 0.08 / r
                cx, cz = fan_point(r, aa)
                oriented_box(m, cx, cz, ROW_PITCH, 0.15, aa, z, z + RAKE / 2 + 1.0, "Wall_Paint")
    # The balcony: from the back row to the shared wall, at first-floor level.
    rb = row_radius(ROWS - 1) + ROW_PITCH / 2
    ab = row_end(ROWS - 1)
    zb = f0 + FLOOR
    segs = 24
    pts = [fan_point(rb, -ab + 2 * ab * s / segs) for s in range(segs + 1)]
    for s in range(segs):
        (xa, za), (xb, zb_) = pts[s], pts[s + 1]
        q = [(xa, za), (xb, zb_), (HALL_E, zb_), (HALL_E, za)]
        m.face([(*m.b(X, Z), zb) for X, Z in q], "Carpet", (0, 0, 1))
        am = -ab + 2 * ab * (s + 0.5) / segs
        m.face([(*m.b(xa, za), f0), (*m.b(xb, zb_), f0), (*m.b(xb, zb_), zb), (*m.b(xa, za), zb)], "Wood",
               (math.cos(am), math.sin(am), 0.0))
    for (X, Z), sgn in ((pts[0], -1), (pts[-1], 1)):
        m.face([(*m.b(X, Z), f0), (*m.b(HALL_E, Z), f0), (*m.b(HALL_E, Z), zb), (*m.b(X, Z), zb)], "Wall_Paint",
               (0.0, -sgn, 0.0))
        # Parapet along the balcony's open side.
        z_in = Z - sgn * 0.075
        m.box(X, HALL_E, z_in - 0.075, z_in + 0.075, zb, zb + 1.05, "Wall_Paint", "Wood")
    return outline


def academic_interior():
    m = Model("Academic_Block_Interior", NW["x"], NW["z"])
    for k in range(WING_LEVELS):
        wing_level(m, k)
    pavilion_tops(m)
    ground_extras(m)
    stair_geometry(m)
    auditorium(m)
    use_level(0)
    m.local_bounds_to_solid()
    return m


def curved_door_side(outline, k):
    """Same rule as build_buildings.east_door_side, in world coordinates."""
    n = len(outline)

    def on_arc(i):
        return math.dist(outline[i], outline[(i + 1) % n]) < 6.0 and math.dist(outline[i - 1], outline[i]) < 6.0

    best = min((i for i in range(n) if on_arc(i)), key=lambda i: abs(outline[i][1] - B.EAST_DOOR_Z))
    if (k + 1) % n == best:
        return 1
    if k == best:
        return 2
    return 0


# ------------------------------------------------------------------ tiered case rooms
TIER_W = 1.75      # depth of one row (desk + chair)
TIER_H = 0.22      # rise per row
TIER_R0 = 4.2      # radius of the first row from the lecturer's spot


def case_geometry(r):
    """Centre of the horseshoe (the lecturer's spot), direction towards the seats, and row radii."""
    X0, X1, Z0, Z1 = r["rect"]
    f = r["front"]
    if f == "W":
        c, d, depth, half = (X0 + 1.6, (Z0 + Z1) / 2), (1, 0), X1 - X0 - 1.6, (Z1 - Z0) / 2
    elif f == "E":
        c, d, depth, half = (X1 - 1.6, (Z0 + Z1) / 2), (-1, 0), X1 - X0 - 1.6, (Z1 - Z0) / 2
    elif f == "S":
        c, d, depth, half = ((X0 + X1) / 2, Z0 + 1.6), (0, 1), Z1 - Z0 - 1.6, (X1 - X0) / 2
    else:
        c, d, depth, half = ((X0 + X1) / 2, Z1 - 1.6), (0, -1), Z1 - Z0 - 1.6, (X1 - X0) / 2
    rows = max(2, min(6, int((depth - TIER_R0 - 1.0) / TIER_W)))
    return c, d, half, rows


def tier_angle(radius, half, depth_room):
    """Half-angle of a row at this radius that keeps it 0.9 m clear of the side walls."""
    lim = half - 0.9
    if radius <= lim:
        return math.radians(80)
    return math.asin(max(0.05, min(1.0, lim / radius)))


def case_tiers(m, r, fl=FL):
    """Stepped platforms for the horseshoe: each row a curved step TIER_H higher than the last."""
    c, d, half, rows = case_geometry(r)
    base = math.atan2(d[1], d[0])
    tiers = []
    for k in range(1, rows):
        r0, r1 = TIER_R0 + k * TIER_W, TIER_R0 + (k + 1) * TIER_W
        if k == rows - 1:
            r1 += 1.2   # back walkway behind the last row
        a = min(tier_angle(r1, half, 0), math.radians(80))
        segs = 16
        h = fl + TIER_H * k
        ring0 = [(c[0] + r0 * math.cos(base - a + 2 * a * i / segs), c[1] + r0 * math.sin(base - a + 2 * a * i / segs)) for i in range(segs + 1)]
        ring1 = [(c[0] + r1 * math.cos(base - a + 2 * a * i / segs), c[1] + r1 * math.sin(base - a + 2 * a * i / segs)) for i in range(segs + 1)]
        for i in range(segs):
            # Top of the step.
            pts = [(*m.b(*ring0[i]), h), (*m.b(*ring0[i + 1]), h), (*m.b(*ring1[i + 1]), h), (*m.b(*ring1[i]), h)]
            m.face(pts, "Carpet", (0, 0, 1))
            # Riser facing the lecturer (world -mid, which is local +mid), from the floor up.
            pts = [(*m.b(*ring0[i]), fl), (*m.b(*ring0[i + 1]), fl), (*m.b(*ring0[i + 1]), h), (*m.b(*ring0[i]), h)]
            mid = ((ring0[i][0] + ring0[i + 1][0]) / 2 - c[0], (ring0[i][1] + ring0[i + 1][1]) / 2 - c[1])
            m.face(pts, "Wood", (mid[0], mid[1], 0.0))
            # Back face at the outer edge, down to the floor (only the last row is seen from behind).
            if k == rows - 1:
                pts = [(*m.b(*ring1[i]), fl), (*m.b(*ring1[i + 1]), fl), (*m.b(*ring1[i + 1]), h), (*m.b(*ring1[i]), h)]
                m.face(pts, "Wood", (-mid[0], -mid[1], 0.0))
        # End faces, at both ends of the step.
        for j, sgn in ((0, -1), (segs, 1)):
            pts = [(*m.b(*ring0[j]), fl), (*m.b(*ring1[j]), fl), (*m.b(*ring1[j]), h), (*m.b(*ring0[j]), h)]
            ang = base - a + 2 * a * j / segs + sgn * math.pi / 2
            m.face(pts, "Wood", (-math.cos(ang), -math.sin(ang), 0.0))
        tiers.append(dict(k=k, r0=r0, r1=r1, a=a, h=h - fl))
    return tiers


# ------------------------------------------------------------------ furniture models
# Each prop is built round its own origin: x/z horizontal (world-like, +Z = front), y up from
# the floor (or down from the ceiling for ceiling fittings, whose origin is at the ceiling).
PROPS = {}


def prop(name, solid=False):
    def deco(fn):
        PROPS[name] = (fn, solid)
        return fn
    return deco


def bx(m, X0, X1, Z0, Z1, y0, y1, mat, top=True, bottom=True):
    """Box in prop coordinates; top/bottom take the side material unless given another name."""
    tmat = mat if top is True else top
    m.box(min(X0, X1), max(X0, X1), min(Z0, Z1), max(Z0, Z1), y0, y1, mat, tmat if tmat else None,
          bottom=bool(bottom))


def cyl(m, X, Z, r, y0, y1, mat, top=True, seg=12):
    m.cylinder_local(*m.b(X, Z), r, y0, y1, mat, mat if top is True else top, segments=seg)


def obox(m, cx, cz, length, width, ang, y0, y1, mat):
    """Box of given length (along angle ang, radians from +X towards +Z) and width."""
    c, s = math.cos(ang), math.sin(ang)
    hl, hw = length / 2, width / 2
    corners = [(cx + c * u - s * v, cz + s * u + c * v) for u, v in ((-hl, -hw), (hl, -hw), (hl, hw), (-hl, hw))]
    poly = [m.b(X, Z) for X, Z in corners]
    m.prism(poly, y0, y1, mat, mat)
    m.face([(p[0], p[1], y0) for p in poly], mat, (0, 0, -1))


def new(name):
    return Model(name, 0.0, 0.0)


@prop("Desk_Case", solid=True)
def p_desk_case():
    m = new("Desk_Case")
    bx(m, -0.9, 0.9, 0.0, 0.6, 0.72, 0.76, "Laminate")
    bx(m, -0.9, 0.9, 0.56, 0.6, 0.12, 0.72, "Wood")                 # modesty panel at the front
    for x in (-0.9, 0.87):
        bx(m, x, x + 0.03, 0.0, 0.6, 0.0, 0.72, "Wood")
    for x in (-0.45, 0.45):                                          # name-card holders and mic posts
        bx(m, x - 0.1, x + 0.1, 0.48, 0.52, 0.76, 0.84, "Plastic")
        cyl(m, x + 0.18, 0.5, 0.008, 0.76, 1.0, "Plastic", seg=6)
    return m


@prop("Chair_Office")
def p_chair_office():
    m = new("Chair_Office")
    for k in range(5):
        a = 2 * math.pi * k / 5 + math.pi / 2
        obox(m, 0.16 * math.cos(a), 0.16 * math.sin(a), 0.32, 0.04, a, 0.05, 0.09, "Plastic")
        bx(m, 0.31 * math.cos(a) - 0.025, 0.31 * math.cos(a) + 0.025, 0.31 * math.sin(a) - 0.025, 0.31 * math.sin(a) + 0.025, 0.0, 0.05, "Plastic")
    cyl(m, 0, 0, 0.03, 0.09, 0.42, "Steel", seg=8)
    bx(m, -0.25, 0.25, -0.24, 0.24, 0.42, 0.5, "Fabric")
    bx(m, -0.23, 0.23, -0.28, -0.22, 0.55, 1.05, "Fabric")
    bx(m, -0.04, 0.04, -0.26, -0.2, 0.44, 0.58, "Plastic")
    for x in (-0.27, 0.23):
        bx(m, x, x + 0.04, -0.12, 0.16, 0.62, 0.65, "Plastic")
        bx(m, x + 0.01, x + 0.03, 0.0, 0.03, 0.5, 0.62, "Plastic")
    return m


@prop("Chair_Visitor")
def p_chair_visitor():
    m = new("Chair_Visitor")
    for x in (-0.21, 0.19):
        for z in (-0.2, 0.19):
            bx(m, x, x + 0.02, z, z + 0.02, 0.0, 0.44, "Steel")
        bx(m, x, x + 0.02, -0.22, -0.2, 0.44, 0.88, "Steel")
    bx(m, -0.23, 0.23, -0.22, 0.22, 0.44, 0.5, "Fabric")
    bx(m, -0.22, 0.22, -0.24, -0.2, 0.58, 0.88, "Fabric")
    return m


@prop("Chair_Cafe")
def p_chair_cafe():
    m = new("Chair_Cafe")
    for x in (-0.19, 0.17):
        for z in (-0.19, 0.17):
            bx(m, x, x + 0.02, z, z + 0.02, 0.0, 0.45, "Steel")
    bx(m, -0.21, 0.21, -0.21, 0.21, 0.45, 0.48, "Wood")
    for x in (-0.19, 0.17):
        bx(m, x, x + 0.02, -0.21, -0.19, 0.48, 0.82, "Steel")
    bx(m, -0.2, 0.2, -0.22, -0.2, 0.62, 0.82, "Wood")
    return m


@prop("Table_Faculty", solid=True)
def p_table_faculty():
    m = new("Table_Faculty")
    bx(m, -0.8, 0.8, -0.4, 0.4, 0.73, 0.77, "Laminate")
    bx(m, 0.35, 0.78, -0.38, 0.38, 0.0, 0.73, "Wood")              # drawer pedestal
    for k in range(3):
        y = 0.12 + 0.2 * k
        bx(m, 0.4, 0.73, -0.385, -0.38, y, y + 0.17, "Laminate", top=True, bottom=True)
        bx(m, 0.52, 0.61, -0.4, -0.385, y + 0.13, y + 0.15, "Steel")
    bx(m, -0.78, -0.74, -0.38, 0.38, 0.0, 0.73, "Wood")
    bx(m, -0.74, 0.35, 0.34, 0.38, 0.25, 0.73, "Wood")              # modesty panel (visitor side)
    # Monitor, keyboard and a phone.
    bx(m, -0.3, 0.3, 0.12, 0.15, 0.95, 1.3, "Screen")
    bx(m, -0.03, 0.03, 0.15, 0.19, 0.77, 0.95, "Plastic")
    bx(m, -0.1, 0.1, 0.12, 0.25, 0.77, 0.78, "Plastic")
    bx(m, -0.22, 0.22, -0.18, -0.03, 0.77, 0.79, "Plastic")
    bx(m, 0.45, 0.62, -0.1, 0.08, 0.77, 0.84, "Plastic")
    return m


@prop("Lectern", solid=True)
def p_lectern():
    m = new("Lectern")
    bx(m, -0.3, 0.3, -0.22, 0.22, 0.0, 1.0, "Wood")
    bx(m, -0.33, 0.33, -0.26, 0.26, 0.0, 0.08, "Wood")
    # Sloping top, lower towards the speaker (-Z).
    pts = [(-0.34, -0.3, 1.0), (0.34, -0.3, 1.0), (0.34, 0.26, 1.14), (-0.34, 0.26, 1.14)]
    m.face([(*m.b(x, z), y) for x, z, y in pts], "Laminate", (0, 0, 1))
    for x, z0_, z1_ in ((-0.34, -0.3, 0.26), (0.34, -0.3, 0.26)):
        m.face([(*m.b(x, -0.3), 1.0), (*m.b(x, 0.26), 1.0), (*m.b(x, 0.26), 1.14)], "Wood", (-x, 0, 0))
    bx(m, -0.34, 0.34, 0.22, 0.26, 1.0, 1.14, "Wood")
    bx(m, -0.34, 0.34, -0.3, -0.22, 0.96, 1.0, "Wood")
    cyl(m, 0.18, 0.15, 0.008, 1.12, 1.45, "Plastic", seg=6)
    bx(m, 0.165, 0.195, 0.1, 0.13, 1.42, 1.47, "Plastic")
    bx(m, -0.15, 0.15, -0.1, 0.25, 0.42, 0.44, "Wood")     # inner shelf
    return m


@prop("Whiteboard")
def p_whiteboard():
    m = new("Whiteboard")
    bx(m, -1.8, 1.8, 0.0, 0.025, 0.9, 2.1, "Whiteboard")
    for x0, x1, y0, y1 in ((-1.83, 1.83, 0.87, 0.9), (-1.83, 1.83, 2.1, 2.13), (-1.83, -1.8, 0.9, 2.1), (1.8, 1.83, 0.9, 2.1)):
        bx(m, x0, x1, 0.0, 0.035, y0, y1, "Steel")
    bx(m, -1.6, 1.6, 0.0, 0.08, 0.86, 0.88, "Steel")
    for k, mat in enumerate(("Plastic", "Red_Paint", "Fabric")):
        bx(m, -1.2 + 0.18 * k, -1.08 + 0.18 * k, 0.03, 0.05, 0.88, 0.9, mat)
    return m


@prop("Projector_Screen")
def p_projector_screen():
    m = new("Projector_Screen")
    bx(m, -1.35, 1.35, 0.0, 0.12, 2.78, 2.9, "Steel")
    bx(m, -1.25, 1.25, 0.05, 0.06, 1.15, 2.78, "Whiteboard")
    bx(m, -1.25, 1.25, 0.049, 0.065, 1.12, 1.15, "Plastic")
    return m


@prop("Projector")
def p_projector():
    m = new("Projector")
    cyl(m, 0, 0, 0.025, -0.5, 0.0, "Steel", seg=8)
    bx(m, -0.12, 0.12, -0.12, 0.12, -0.03, 0.0, "Steel")
    bx(m, -0.18, 0.18, -0.15, 0.15, -0.64, -0.5, "Ceramic")
    bx(m, 0.06, 0.12, 0.15, 0.17, -0.6, -0.54, "Screen")
    return m


@prop("Ceiling_Fan")
def p_ceiling_fan():
    m = new("Ceiling_Fan")
    cyl(m, 0, 0, 0.02, -0.45, 0.0, "Steel", seg=8)
    cyl(m, 0, 0, 0.07, -0.08, 0.0, "Ceramic", seg=12)
    cyl(m, 0, 0, 0.13, -0.6, -0.45, "Ceramic", seg=16)
    for k in range(3):
        a = 2 * math.pi * k / 3
        obox(m, 0.42 * math.cos(a), 0.42 * math.sin(a), 0.62, 0.11, a, -0.57, -0.555, "Ceramic")
    return m


@prop("Light_Panel")
def p_light_panel():
    m = new("Light_Panel")
    bx(m, -0.3, 0.3, -0.3, 0.3, -0.035, 0.0, "Steel", top=None, bottom=None)
    m.flat(-0.28, 0.28, -0.28, 0.28, -0.036, "LightPanel", up=False)
    for x0, x1, z0, z1 in ((-0.3, 0.3, -0.3, -0.28), (-0.3, 0.3, 0.28, 0.3), (-0.3, -0.28, -0.28, 0.28), (0.28, 0.3, -0.28, 0.28)):
        hx0, hx1, hy0, hy1 = m.rect(x0, x1, z0, z1)
        m.flat(hx0, hx1, hy0, hy1, -0.035, "Steel", up=False)
    return m


@prop("Tube_Light")
def p_tube_light():
    m = new("Tube_Light")
    bx(m, -0.62, 0.62, -0.05, 0.05, -0.05, 0.0, "Ceramic", bottom=None)
    m.flat(*m.rect(-0.6, 0.6, -0.035, 0.035), -0.051, "LightPanel", up=False)
    return m


@prop("AC_Unit")
def p_ac_unit():
    m = new("AC_Unit")
    bx(m, -0.45, 0.45, 0.0, 0.22, 2.3, 2.6, "Ceramic")
    bx(m, -0.4, 0.4, 0.2, 0.225, 2.32, 2.36, "Plastic")
    bx(m, 0.3, 0.38, 0.22, 0.225, 2.5, 2.52, "Foliage")       # status light
    return m


@prop("Bookshelf", solid=True)
def p_bookshelf():
    m = new("Bookshelf")
    bx(m, -0.5, -0.48, 0.0, 0.35, 0.0, 2.0, "Wood")
    bx(m, 0.48, 0.5, 0.0, 0.35, 0.0, 2.0, "Wood")
    bx(m, -0.5, 0.5, 0.0, 0.02, 0.0, 2.0, "Wood")
    rnd = random.Random(7)
    for k in range(6):
        y = 0.05 + k * 0.38
        bx(m, -0.48, 0.48, 0.02, 0.35, y - 0.03, y, "Wood")
        if k == 5:
            break
        x = -0.47
        while x < 0.42:
            w = rnd.uniform(0.025, 0.06)
            h = rnd.uniform(0.2, 0.31)
            lean = rnd.random() < 0.05
            if rnd.random() < 0.08:
                x += w * 2
                continue
            bx(m, x, x + w, 0.04, 0.04 + rnd.uniform(0.17, 0.25), y, y + h, "Books")
            x += w + 0.002
    return m


@prop("Filing_Cabinet", solid=True)
def p_filing_cabinet():
    m = new("Filing_Cabinet")
    bx(m, -0.23, 0.23, -0.3, 0.3, 0.0, 1.32, "Steel")
    for k in range(4):
        y = 0.05 + 0.32 * k
        bx(m, -0.21, 0.21, 0.3, 0.305, y, y + 0.29, "Steel")
        bx(m, -0.06, 0.06, 0.305, 0.325, y + 0.22, y + 0.24, "Plastic")
    return m


@prop("Sofa_3", solid=True)
def p_sofa_3():
    m = new("Sofa_3")
    bx(m, -1.0, 1.0, -0.42, 0.42, 0.12, 0.42, "Leather")
    bx(m, -1.0, 1.0, -0.42, -0.2, 0.42, 0.85, "Leather")
    for x in (-1.0, 0.82):
        bx(m, x, x + 0.18, -0.42, 0.42, 0.12, 0.65, "Leather")
    for k in range(3):
        x = -0.82 + 0.547 * k
        bx(m, x + 0.01, x + 0.537, -0.2, 0.4, 0.42, 0.5, "Leather")
    for x in (-0.95, 0.9):
        for z in (-0.38, 0.33):
            bx(m, x, x + 0.05, z, z + 0.05, 0.0, 0.12, "Steel")
    return m


@prop("Sofa_1", solid=True)
def p_sofa_1():
    m = new("Sofa_1")
    bx(m, -0.45, 0.45, -0.42, 0.42, 0.12, 0.42, "Leather")
    bx(m, -0.45, 0.45, -0.42, -0.2, 0.42, 0.85, "Leather")
    for x in (-0.45, 0.29):
        bx(m, x, x + 0.16, -0.42, 0.42, 0.12, 0.65, "Leather")
    bx(m, -0.29, 0.29, -0.2, 0.4, 0.42, 0.5, "Leather")
    for x in (-0.4, 0.35):
        for z in (-0.38, 0.33):
            bx(m, x, x + 0.05, z, z + 0.05, 0.0, 0.12, "Steel")
    return m


@prop("Table_Coffee", solid=True)
def p_table_coffee():
    m = new("Table_Coffee")
    bx(m, -0.55, 0.55, -0.3, 0.3, 0.4, 0.44, "Wood")
    bx(m, -0.5, 0.5, -0.25, 0.25, 0.12, 0.14, "Wood")
    for x in (-0.52, 0.49):
        for z in (-0.27, 0.24):
            bx(m, x, x + 0.03, z, z + 0.03, 0.0, 0.4, "Steel")
    bx(m, -0.15, 0.1, -0.1, 0.12, 0.44, 0.46, "Notice")      # magazines
    return m


@prop("Reception_Counter", solid=True)
def p_reception():
    m = new("Reception_Counter")
    bx(m, -1.8, 1.8, 0.72, 0.8, 0.0, 1.1, "Wood")
    bx(m, -1.85, 1.85, 0.5, 0.85, 1.1, 1.14, "Laminate")
    bx(m, -1.8, 1.8, -0.1, 0.72, 0.72, 0.76, "Laminate")
    for x in (-1.8, 1.77):
        bx(m, x, x + 0.03, -0.1, 0.72, 0.0, 1.1, "Wood")
    bx(m, -1.6, 1.6, 0.8, 0.81, 0.1, 0.25, "Steel")          # kick plate
    for x in (-0.9, 0.7):
        bx(m, x - 0.25, x + 0.25, 0.42, 0.45, 0.9, 1.2, "Screen")
        bx(m, x - 0.03, x + 0.03, 0.45, 0.49, 0.76, 0.9, "Plastic")
        bx(m, x - 0.2, x + 0.2, 0.05, 0.2, 0.76, 0.78, "Plastic")
    return m


@prop("Desk_Computer", solid=True)
def p_desk_computer():
    m = new("Desk_Computer")
    bx(m, -0.6, 0.6, -0.3, 0.3, 0.72, 0.75, "Laminate")
    for x in (-0.58, 0.55):
        bx(m, x, x + 0.03, -0.28, 0.28, 0.0, 0.72, "Steel")
    bx(m, -0.55, 0.55, 0.26, 0.28, 0.3, 0.72, "Steel")
    bx(m, -0.25, 0.25, 0.12, 0.15, 0.9, 1.22, "Screen")
    bx(m, -0.03, 0.03, 0.15, 0.2, 0.75, 0.9, "Plastic")
    bx(m, -0.1, 0.1, 0.1, 0.24, 0.75, 0.76, "Plastic")
    bx(m, -0.22, 0.22, -0.2, -0.05, 0.75, 0.77, "Plastic")
    bx(m, 0.28, 0.34, -0.15, -0.07, 0.75, 0.77, "Plastic")
    bx(m, 0.3, 0.5, -0.2, 0.2, 0.0, 0.42, "Plastic")          # computer under the desk
    return m


@prop("Table_Meeting", solid=True)
def p_table_meeting():
    m = new("Table_Meeting")
    bx(m, -2.0, 2.0, -0.6, 0.6, 0.72, 0.77, "Laminate")
    for x in (-1.3, 1.1):
        bx(m, x, x + 0.2, -0.35, 0.35, 0.0, 0.72, "Wood")
    bx(m, -0.15, 0.15, -0.1, 0.1, 0.77, 0.8, "Plastic")      # conference phone
    return m


@prop("Table_Round", solid=True)
def p_table_round():
    m = new("Table_Round")
    cyl(m, 0, 0, 0.6, 0.72, 0.76, "Laminate", seg=16)
    m.face([(*m.b(0.6 * math.cos(2 * math.pi * k / 16), 0.6 * math.sin(2 * math.pi * k / 16)), 0.72) for k in range(16)], "Laminate", (0, 0, -1))
    cyl(m, 0, 0, 0.05, 0.03, 0.72, "Steel", seg=8)
    cyl(m, 0, 0, 0.3, 0.0, 0.03, "Steel", seg=12)
    return m


@prop("Table_Cafe", solid=True)
def p_table_cafe():
    m = new("Table_Cafe")
    bx(m, -0.4, 0.4, -0.4, 0.4, 0.72, 0.75, "Laminate")
    for x in (-0.37, 0.34):
        for z in (-0.37, 0.34):
            bx(m, x, x + 0.03, z, z + 0.03, 0.0, 0.72, "Steel")
    cyl(m, 0.15, -0.1, 0.035, 0.75, 0.85, "Ceramic", seg=8)    # a cup
    return m


@prop("Cafe_Counter", solid=True)
def p_cafe_counter():
    m = new("Cafe_Counter")
    bx(m, -2.0, 2.0, -0.35, 0.35, 0.0, 0.95, "Steel")
    bx(m, -2.05, 2.05, -0.4, 0.4, 0.95, 0.99, "Laminate")
    # Glass display case with trays.
    bx(m, -1.8, 0.2, -0.3, 0.3, 0.99, 1.0, "Steel")
    for x in (-1.8, -0.8, 0.2):
        bx(m, x - 0.01, x + 0.01, -0.3, 0.3, 1.0, 1.4, "Steel")
    m.face([(*m.b(-1.8, 0.3), 1.0), (*m.b(0.2, 0.3), 1.0), (*m.b(0.2, 0.3), 1.4), (*m.b(-1.8, 0.3), 1.4)], "Glass_Clear", (0, -1, 0))
    m.face([(*m.b(-1.8, 0.3), 1.4), (*m.b(0.2, 0.3), 1.4), (*m.b(0.2, -0.3), 1.4), (*m.b(-1.8, -0.3), 1.4)], "Glass_Clear", (0, 0, 1))
    for k in range(6):
        bx(m, -1.75 + 0.33 * k, -1.5 + 0.33 * k, -0.2, 0.2, 1.0, 1.05, "Cardboard")
    # Till and a tea urn.
    bx(m, 0.8, 1.2, -0.25, 0.05, 0.99, 1.15, "Plastic")
    cyl(m, 1.6, -0.1, 0.16, 0.99, 1.5, "Steel", seg=12)
    return m


@prop("Water_Cooler", solid=True)
def p_water_cooler():
    m = new("Water_Cooler")
    bx(m, -0.3, 0.3, -0.25, 0.25, 0.0, 1.3, "Steel")
    bx(m, -0.25, 0.25, 0.25, 0.3, 0.6, 0.65, "Steel")
    for x in (-0.12, 0.12):
        bx(m, x - 0.02, x + 0.02, 0.25, 0.3, 0.85, 0.9, "Plastic")
    bx(m, -0.27, 0.27, 0.25, 0.255, 1.0, 1.25, "Plastic")
    return m


@prop("Toilet_Cubicle", solid=True)
def p_toilet_cubicle():
    """1.0 m wide WC cubicle, back to the wall at z = -1.5, open door on the front at z = 0."""
    m = new("Toilet_Cubicle")
    for x in (-0.485, 0.47):
        bx(m, x, x + 0.015, -1.5, 0.0, 0.15, 2.0, "Laminate")
        bx(m, x, x + 0.015, -0.05, -0.0, 0.0, 0.15, "Steel")
    bx(m, -0.47, -0.37, -0.01, 0.0, 0.15, 2.0, "Laminate")
    bx(m, 0.37, 0.47, -0.01, 0.0, 0.15, 2.0, "Laminate")
    bx(m, -0.47, 0.47, -0.02, 0.0, 1.95, 2.0, "Steel")
    obox(m, -0.37 + 0.3 * math.cos(-1.9), 0.3 * math.sin(-1.9), 0.72, 0.02, -1.9, 0.15, 1.95, "Laminate")
    # WC and cistern.
    bx(m, -0.18, 0.18, -1.5, -1.3, 0.0, 0.75, "Ceramic")
    cyl(m, 0, -1.05, 0.2, 0.0, 0.4, "Ceramic", seg=14)
    bx(m, -0.19, 0.19, -1.32, -0.85, 0.4, 0.43, "Plastic")
    bx(m, 0.35, 0.45, -1.0, -0.9, 0.7, 0.8, "Steel")
    return m


@prop("Basin_Counter", solid=True)
def p_basin_counter():
    m = new("Basin_Counter")
    bx(m, -1.2, 1.2, 0.0, 0.55, 0.82, 0.88, "Laminate")
    bx(m, -1.2, 1.2, 0.5, 0.55, 0.6, 0.82, "Laminate")
    for x in (-0.6, 0.6):
        bx(m, x - 0.22, x + 0.22, 0.12, 0.45, 0.8, 0.89, "Ceramic")
        bx(m, x - 0.18, x + 0.18, 0.16, 0.41, 0.885, 0.886, "Steel")
        cyl(m, x, 0.06, 0.015, 0.88, 1.05, "Steel", seg=6)
        bx(m, x - 0.015, x + 0.015, 0.06, 0.16, 1.02, 1.04, "Steel")
    bx(m, -1.15, 1.15, 0.0, 0.015, 1.1, 2.0, "Mirror")
    bx(m, 1.25, 1.45, 0.0, 0.1, 1.1, 1.35, "Steel")             # hand dryer
    return m


@prop("Urinal")
def p_urinal():
    m = new("Urinal")
    bx(m, -0.18, 0.18, 0.0, 0.32, 0.55, 1.1, "Ceramic")
    bx(m, -0.14, 0.14, 0.05, 0.3, 0.6, 0.62, "Steel")
    cyl(m, 0, 0.04, 0.02, 1.1, 1.35, "Steel", seg=6)
    bx(m, -0.45, -0.43, 0.0, 0.45, 0.6, 1.5, "Laminate")       # privacy screen
    return m


@prop("Plant_Pot")
def p_plant_pot():
    m = new("Plant_Pot")
    cyl(m, 0, 0, 0.22, 0.0, 0.45, "Plastic", top="Cardboard", seg=12)
    rnd = random.Random(3)
    for k in range(7):
        a = 2 * math.pi * k / 7 + rnd.uniform(-0.3, 0.3)
        r = rnd.uniform(0.08, 0.2)
        y0 = 0.45 + rnd.uniform(0.1, 0.5)
        cyl(m, r * math.cos(a), r * math.sin(a), rnd.uniform(0.12, 0.2), y0, y0 + rnd.uniform(0.25, 0.45), "Foliage", seg=7)
    cyl(m, 0, 0, 0.17, 0.85, 1.35, "Foliage", seg=8)
    cyl(m, 0, 0, 0.012, 0.45, 0.9, "Wood", seg=5)
    return m


@prop("Notice_Board")
def p_notice_board():
    m = new("Notice_Board")
    bx(m, -0.6, 0.6, 0.0, 0.02, 1.0, 1.9, "Notice")
    for x0, x1, y0, y1 in ((-0.63, 0.63, 0.97, 1.0), (-0.63, 0.63, 1.9, 1.93), (-0.63, -0.6, 1.0, 1.9), (0.6, 0.63, 1.0, 1.9)):
        bx(m, x0, x1, 0.0, 0.035, y0, y1, "Wood")
    return m


@prop("Bench", solid=True)
def p_bench():
    m = new("Bench")
    for x in (-0.8, 0.76):
        bx(m, x, x + 0.04, -0.2, 0.2, 0.0, 0.42, "Steel")
        bx(m, x, x + 0.04, -0.24, -0.2, 0.0, 0.85, "Steel")
    for k in range(4):
        z = -0.2 + 0.1 * k
        bx(m, -0.9, 0.9, z, z + 0.085, 0.42, 0.45, "Wood")
    for k in range(2):
        y = 0.6 + 0.13 * k
        bx(m, -0.9, 0.9, -0.24, -0.22, y, y + 0.1, "Wood")
    return m


@prop("Dustbin")
def p_dustbin():
    m = new("Dustbin")
    cyl(m, 0, 0, 0.16, 0.0, 0.5, "Steel", top="Plastic", seg=12)
    cyl(m, 0, 0, 0.165, 0.46, 0.52, "Plastic", seg=12)
    return m


@prop("Fire_Extinguisher")
def p_fire_extinguisher():
    m = new("Fire_Extinguisher")
    bx(m, -0.12, 0.12, 0.0, 0.02, 0.9, 1.5, "Red_Paint")
    cyl(m, 0, 0.11, 0.08, 0.95, 1.45, "Red_Paint", seg=10)
    cyl(m, 0, 0.11, 0.03, 1.45, 1.55, "Plastic", seg=6)
    bx(m, -0.1, 0.1, 0.0, 0.015, 1.65, 1.85, "Red_Paint")
    return m


@prop("Door_Leaf")
def p_door_leaf():
    """Door leaf hinged at the origin, closing along +X, 0.95 x 2.08 m."""
    m = new("Door_Leaf")
    bx(m, 0.02, 0.95, -0.02, 0.02, 0.0, 2.08, "Door")
    bx(m, 0.4, 0.75, 0.02, 0.025, 1.45, 1.95, "Glass_Clear")
    for z in (0.02, -0.02):
        sgn = 1 if z > 0 else -1
        bx(m, 0.8, 0.82, z, z + 0.05 * sgn, 0.95, 1.1, "Steel")
        bx(m, 0.7, 0.82, z + 0.04 * sgn, z + 0.06 * sgn, 1.05, 1.07, "Steel")
    bx(m, 0.0, 0.03, -0.015, 0.015, 0.2, 0.32, "Steel")
    bx(m, 0.0, 0.03, -0.015, 0.015, 1.75, 1.87, "Steel")
    return m


@prop("Door_Glass")
def p_door_glass():
    """Glass entrance door leaf (aluminium frame), hinged at the origin, 1.15 x 2.35 m."""
    m = new("Door_Glass")
    for x0, x1, y0, y1 in ((0.0, 1.15, 0.0, 0.12), (0.0, 1.15, 2.27, 2.35), (0.0, 0.06, 0.0, 2.35), (1.09, 1.15, 0.0, 2.35)):
        bx(m, x0, x1, -0.025, 0.025, y0, y1, "Metal")
    bx(m, 0.06, 1.09, -0.006, 0.006, 0.12, 2.27, "Glass_Clear")
    bx(m, 0.95, 0.98, 0.03, 0.06, 0.8, 1.6, "Steel")
    bx(m, 0.95, 0.98, -0.06, -0.03, 0.8, 1.6, "Steel")
    return m


@prop("Server_Rack", solid=True)
def p_server_rack():
    m = new("Server_Rack")
    bx(m, -0.3, 0.3, -0.5, 0.5, 0.0, 2.0, "Plastic")
    for k in range(12):
        y = 0.15 + k * 0.14
        bx(m, -0.27, 0.27, 0.5, 0.51, y, y + 0.1, "Screen")
        bx(m, 0.18, 0.21, 0.51, 0.515, y + 0.04, y + 0.06, "Foliage")
    return m


@prop("Storage_Rack", solid=True)
def p_storage_rack():
    m = new("Storage_Rack")
    for x in (-0.9, 0.87):
        for z in (-0.25, 0.22):
            bx(m, x, x + 0.03, z, z + 0.03, 0.0, 2.0, "Steel")
    rnd = random.Random(11)
    for k in range(4):
        y = 0.1 + 0.6 * k
        bx(m, -0.9, 0.9, -0.25, 0.25, y, y + 0.03, "Steel")
        if k < 3:
            x = -0.85
            while x < 0.6:
                w = rnd.uniform(0.25, 0.5)
                bx(m, x, x + w, -0.2, 0.2, y + 0.03, y + 0.03 + rnd.uniform(0.2, 0.45), "Cardboard")
                x += w + 0.05
    return m


@prop("TV_Wall")
def p_tv_wall():
    m = new("TV_Wall")
    bx(m, -0.8, 0.8, 0.0, 0.05, 1.3, 2.2, "Screen")
    bx(m, -0.82, 0.82, 0.0, 0.04, 1.28, 1.3, "Plastic")
    return m


@prop("Display_Panel", solid=True)
def p_display_panel():
    m = new("Display_Panel")
    bx(m, -0.55, 0.55, -0.02, 0.02, 0.35, 2.1, "Notice")
    for x in (-0.58, 0.55):
        bx(m, x, x + 0.03, -0.03, 0.03, 0.0, 2.15, "Steel")
        bx(m, x - 0.05, x + 0.08, -0.25, 0.25, 0.0, 0.03, "Steel")
    return m


@prop("Wall_Clock")
def p_wall_clock():
    m = new("Wall_Clock")
    bx(m, -0.2, 0.2, 0.0, 0.04, 2.3, 2.7, "ClockFace")
    return m


@prop("Crest_Panel")
def p_crest_panel():
    m = new("Crest_Panel")
    bx(m, -0.9, 0.9, 0.0, 0.06, 1.3, 3.0, "Emblem")
    bx(m, -1.0, 1.0, 0.0, 0.04, 1.2, 3.1, "Wood")
    return m


@prop("Pin_Spot")
def p_pin_spot():
    """Small downlight for the double-height foyers."""
    m = new("Pin_Spot")
    cyl(m, 0, 0, 0.09, -0.04, 0.0, "Steel", seg=10)
    m.face([(*m.b(0.07 * math.cos(2 * math.pi * k / 10), 0.07 * math.sin(2 * math.pi * k / 10)), -0.041) for k in range(10)], "LightPanel", (0, 0, -1))
    return m


@prop("Auditorium_Seat", solid=True)
def p_auditorium_seat():
    """Upholstered auditorium seat, 0.55 m wide, with wooden armrests on steel standards."""
    m = new("Auditorium_Seat")
    for x in (-0.275, 0.245):
        bx(m, x, x + 0.03, -0.28, 0.18, 0.0, 0.62, "Steel")              # side standard
        bx(m, x - 0.01, x + 0.04, -0.26, 0.24, 0.62, 0.66, "Wood")       # armrest
    bx(m, -0.24, 0.24, -0.18, 0.26, 0.40, 0.50, "Velvet")               # seat cushion
    bx(m, -0.24, 0.24, 0.18, 0.26, 0.30, 0.40, "Plastic")               # seat pan front
    bx(m, -0.25, 0.25, -0.30, -0.18, 0.40, 1.02, "Plastic")             # back shell
    bx(m, -0.23, 0.23, -0.18, -0.12, 0.50, 0.98, "Velvet")              # back cushion
    bx(m, -0.25, 0.25, -0.31, -0.30, 0.86, 0.90, "Wood")                # writing-ledge strip on the back
    return m


def book_side(m, rnd, z0, z1, x0, x1, levels, y0, dy):
    """Rows of books on shelves between z0 and z1 (front at z1), x0..x1."""
    for k in range(levels):
        y = y0 + k * dy
        bx(m, x0, x1, z0, z1, y - 0.03, y, "Wood")
        if k == levels - 1:
            break
        x = x0 + 0.01
        while x < x1 - 0.06:
            w = rnd.uniform(0.025, 0.06)
            if rnd.random() < 0.07:
                x += w * 2
                continue
            depth = rnd.uniform(0.17, 0.25)
            front = z1 - 0.01 if z1 > z0 else z1 + 0.01
            bx(m, x, x + w, front, front - depth if z1 > z0 else front + depth, y, y + rnd.uniform(0.2, 0.31), "Books")
            x += w + 0.002


@prop("Library_Stack", solid=True)
def p_library_stack():
    """Double-sided library stack, 2 m long, books on both faces, with an end panel label."""
    m = new("Library_Stack")
    rnd = random.Random(11)
    for x in (-1.0, 0.97):
        bx(m, x, x + 0.03, -0.3, 0.3, 0.0, 2.1, "Wood")
    bx(m, -0.97, 0.97, -0.01, 0.01, 0.0, 2.1, "Wood")
    bx(m, -1.0, 1.0, -0.3, 0.3, 2.1, 2.14, "Wood")
    bx(m, -0.97, 0.97, -0.3, 0.3, 0.0, 0.08, "Wood")
    book_side(m, rnd, 0.01, 0.3, -0.97, 0.97, 6, 0.08 + 0.03, 0.39)
    book_side(m, rnd, -0.01, -0.3, -0.97, 0.97, 6, 0.08 + 0.03, 0.39)
    bx(m, 1.0, 1.005, -0.12, 0.12, 1.45, 1.65, "Notice")                # end panel label
    return m


@prop("Lift_Door")
def p_lift_door():
    """Wall-mounted lift doors: steel frame, two leaves, call buttons and a floor display."""
    m = new("Lift_Door")
    bx(m, -0.95, -0.8, 0.0, 0.08, 0.0, 2.35, "Steel")
    bx(m, 0.8, 0.95, 0.0, 0.08, 0.0, 2.35, "Steel")
    bx(m, -0.95, 0.95, 0.0, 0.08, 2.2, 2.35, "Steel")
    bx(m, -0.8, -0.01, 0.0, 0.03, 0.0, 2.2, "Steel")
    bx(m, 0.01, 0.8, 0.0, 0.03, 0.0, 2.2, "Steel")
    bx(m, -0.8, 0.8, 0.0, 0.06, -0.005, 0.0, "Plastic")
    bx(m, 1.1, 1.24, 0.0, 0.025, 1.0, 1.3, "Steel")                     # call panel
    cyl(m, 1.17, 0.03, 0.022, 1.12, 1.135, "Plastic", seg=8)
    cyl(m, 1.17, 0.03, 0.022, 1.2, 1.215, "Plastic", seg=8)
    bx(m, -0.25, 0.25, 0.0, 0.04, 2.45, 2.62, "Screen")                 # floor display
    return m


@prop("Stage_Curtain")
def p_stage_curtain():
    """A pleated velvet curtain panel, 2.6 m wide and 7 m tall."""
    m = new("Stage_Curtain")
    n = 13
    w = 2.6 / n
    for k in range(n):
        x = -1.3 + w * (k + 0.5)
        ang = math.radians(28 if k % 2 == 0 else -28)
        obox(m, x, 0.12, w / math.cos(ang) + 0.01, 0.025, ang, 0.0, 7.0, "Velvet")
    bx(m, -1.35, 1.35, 0.0, 0.28, 7.0, 7.12, "Velvet")
    return m


@prop("Acoustic_Panel")
def p_acoustic_panel():
    """Wall panel of vertical timber slats over dark fabric, 1.2 x 2.4 m."""
    m = new("Acoustic_Panel")
    bx(m, -0.6, 0.6, 0.0, 0.02, 0.0, 2.4, "Plastic")
    for k in range(12):
        x = -0.58 + k * 0.1
        bx(m, x, x + 0.06, 0.02, 0.07, 0.02, 2.38, "Wood")
    bx(m, -0.6, 0.6, 0.02, 0.07, 0.0, 0.02, "Wood")
    bx(m, -0.6, 0.6, 0.02, 0.07, 2.38, 2.4, "Wood")
    return m


@prop("Stage_Screen")
def p_stage_screen():
    """The auditorium's big projection screen, 8 x 4.5 m, its bottom edge 3 m above the origin."""
    m = new("Stage_Screen")
    bx(m, -4.2, 4.2, 0.0, 0.1, 2.8, 3.0, "Plastic")
    bx(m, -4.2, 4.2, 0.0, 0.1, 7.5, 7.7, "Plastic")
    bx(m, -4.2, -4.0, 0.0, 0.1, 3.0, 7.5, "Plastic")
    bx(m, 4.0, 4.2, 0.0, 0.1, 3.0, 7.5, "Plastic")
    bx(m, -4.0, 4.0, 0.06, 0.07, 3.0, 7.5, "Whiteboard")
    return m


@prop("Speaker", solid=True)
def p_speaker():
    """PA speaker cabinet on a low stand."""
    m = new("Speaker")
    bx(m, -0.3, 0.3, -0.25, 0.25, 0.0, 0.1, "Plastic")
    bx(m, -0.28, 0.28, -0.22, 0.22, 0.1, 1.15, "Plastic")
    bx(m, -0.25, 0.25, 0.22, 0.235, 0.15, 1.1, "Metal")
    cyl(m, 0.0, 0.236, 0.16, 0.35, 0.36, "Plastic", seg=12)
    return m


@prop("Exit_Sign")
def p_exit_sign():
    """Lit exit sign over a doorway; the origin is on the wall at the sign's bottom edge."""
    m = new("Exit_Sign")
    bx(m, -0.22, 0.22, 0.0, 0.05, 0.0, 0.16, "Plastic")
    bx(m, -0.19, 0.19, 0.05, 0.055, 0.02, 0.14, "Red_Paint")
    bx(m, -0.12, 0.12, 0.055, 0.058, 0.06, 0.1, "LightPanel")
    return m



# ------------------------------------------------------------------ furnishing
PLACED = []    # dict(prop, x, y, z, yaw)
LIGHTS = []    # dict(x, y, z, range, intensity, color)
WARM = (1.0, 0.95, 0.86)
COOL = (0.92, 0.96, 1.0)


def yaw_of(dx, dz):
    return round(math.degrees(math.atan2(dx, dz)), 2)


def put(name, X, Z, y, dx=0.0, dz=1.0, yaw=None):
    assert name in PROPS, name
    PLACED.append(dict(prop=name, x=round(X, 3), y=round(y, 3), z=round(Z, 3),
                       yaw=yaw if yaw is not None else yaw_of(dx, dz)))


class Frame:
    """Room frame: u runs along the front wall, v from the front wall into the room."""

    def __init__(self, rect, front):
        X0, X1, Z0, Z1 = rect
        self.rect = rect
        if front == "W":
            self.o, self.f, self.width, self.depth = (X0, (Z0 + Z1) / 2), (1.0, 0.0), Z1 - Z0, X1 - X0
        elif front == "E":
            self.o, self.f, self.width, self.depth = (X1, (Z0 + Z1) / 2), (-1.0, 0.0), Z1 - Z0, X1 - X0
        elif front == "S":
            self.o, self.f, self.width, self.depth = ((X0 + X1) / 2, Z0), (0.0, 1.0), X1 - X0, Z1 - Z0
        else:
            self.o, self.f, self.width, self.depth = ((X0 + X1) / 2, Z1), (0.0, -1.0), X1 - X0, Z1 - Z0
        self.r = (self.f[1], -self.f[0])

    def P(self, u, v):
        return (self.o[0] + self.r[0] * u + self.f[0] * v, self.o[1] + self.r[1] * u + self.f[1] * v)

    def put(self, name, u, v, y, face="in", yaw_extra=0.0):
        X, Z = self.P(u, v)
        d = {"in": self.f, "front": (-self.f[0], -self.f[1]), "right": self.r, "left": (-self.r[0], -self.r[1])}[face]
        put(name, X, Z, y, yaw=round(yaw_of(*d) + yaw_extra, 2))

    def wall_item(self, name, wall, pos, y=FL):
        """Wall-mounted item on 'front', 'back', 'left' or 'right' wall at position pos along it."""
        hw, dp = self.width / 2 - PART / 2, self.depth - PART / 2
        if wall == "front":
            self.put(name, pos, PART / 2 + 0.01, y, "in")
        elif wall == "back":
            self.put(name, pos, dp - 0.01, y, "front")
        elif wall == "left":
            self.put(name, -hw + 0.01, pos, y, "right")
        else:
            self.put(name, hw - 0.01, pos, y, "left")


def ceiling_grid(rect, step=3.0, prop_name="Light_Panel", y=CEIL, margin=1.2):
    X0, X1, Z0, Z1 = rect
    nxp = max(1, int((X1 - X0 - 2 * margin) / step) + 1)
    nzp = max(1, int((Z1 - Z0 - 2 * margin) / step) + 1)
    for i in range(nxp):
        for j in range(nzp):
            X = X0 + (X1 - X0) * (i + 0.5) / nxp
            Z = Z0 + (Z1 - Z0) * (j + 0.5) / nzp
            put(prop_name, X, Z, y)


def room_lights(rect, y=CEIL - 0.25, per=48.0, color=WARM, intensity=1.3):
    X0, X1, Z0, Z1 = rect
    w, d = X1 - X0, Z1 - Z0
    count = max(1, round(w * d / per))
    nxp = max(1, round(math.sqrt(count * w / d)))
    nzp = max(1, round(count / nxp))
    rng = 1.25 * math.sqrt((w / nxp) ** 2 + (d / nzp) ** 2) / 2 + 2.5
    for i in range(nxp):
        for j in range(nzp):
            LIGHTS.append(dict(x=round(X0 + w * (i + 0.5) / nxp, 2), y=round(y, 2), z=round(Z0 + d * (j + 0.5) / nzp, 2),
                               range=round(rng, 2), intensity=intensity, color=color))


def f_case(r):
    c, d, half, rows = case_geometry(r)
    base = math.atan2(d[1], d[0])
    fr = Frame(r["rect"], r["front"])
    tiers = {t["k"]: t for t in r.get("tiers", [])}
    for k in range(rows):
        r0 = TIER_R0 + k * TIER_W
        a = tiers[k]["a"] if k in tiers else tier_angle(r0 + TIER_W, half, 0)
        y = FL + TIER_H * k
        rd = r0 + 0.08 + 0.6
        n = max(1, int(2 * a * rd / 1.86))
        step = 1.86 / rd
        for i in range(n):
            ang = base + (i - (n - 1) / 2) * step
            X, Z = c[0] + rd * math.cos(ang), c[1] + rd * math.sin(ang)
            to_c = (-math.cos(ang), -math.sin(ang))
            put("Desk_Case", X, Z, y, *to_c)
            tang = (-math.sin(ang), math.cos(ang))
            for off in (-0.45, 0.45):
                cx, cz = X + math.cos(ang) * 0.5 + tang[0] * off, Z + math.sin(ang) * 0.5 + tang[1] * off
                put("Chair_Office", cx, cz, y, *to_c)
    # The front: lecturer's table and lectern, boards and screen, projector.
    fx, fz = c
    put("Table_Faculty", fx + d[0] * 0.4 + fr.r[0] * 2.2, fz + d[1] * 0.4 + fr.r[1] * 2.2, FL, -d[0], -d[1])
    put("Chair_Office", fx + d[0] * -0.4 + fr.r[0] * 2.2, fz + d[1] * -0.4 + fr.r[1] * 2.2, FL, d[0], d[1])
    put("Lectern", fx - fr.r[0] * 2.0, fz - fr.r[1] * 2.0, FL, d[0], d[1])
    fr.wall_item("Projector_Screen", "front", 0.0)
    for u in (-4.0, 4.0):
        if abs(u) + 1.9 < fr.width / 2:
            fr.wall_item("Whiteboard", "front", u)
    put("Projector", fx + d[0] * 6.0, fz + d[1] * 6.0, CEIL, -d[0], -d[1])
    fr.wall_item("Wall_Clock", "back", 0.0, y=FL + TIER_H * (rows - 1))
    for v in (fr.depth * 0.3, fr.depth * 0.7):
        fr.wall_item("AC_Unit", "left", v, y=FL + 0.2)
        fr.wall_item("AC_Unit", "right", v, y=FL + 0.2)
    fr.put("Plant_Pot", -fr.width / 2 + 0.6, 0.7, FL)
    fr.put("Dustbin", fr.width / 2 - 0.5, 0.6, FL)
    ceiling_grid(r["rect"], 3.2)
    room_lights(r["rect"], per=55.0)


def f_classroom(r):
    fr = Frame(r["rect"], r["front"])
    fr.wall_item("Whiteboard", "front", 0.0)
    fr.wall_item("Projector_Screen", "front", min(4.2, fr.width / 2 - 1.5) * -1 if fr.width > 9 else 0.0)
    fr.put("Lectern", fr.width / 2 - 1.4, 1.2, FL, "in")
    fr.put("Table_Faculty", 1.8, 1.5, FL, "front")
    fr.put("Chair_Office", 1.8, 0.75, FL, "in")
    v = 3.2
    while v < fr.depth - 1.6:
        for side in (-1, 1):
            u = side * 0.9
            while abs(u) + 0.95 < fr.width / 2 - 0.6:
                fr.put("Desk_Case", u + side * 0.9, v, FL, "front")
                fr.put("Chair_Visitor", u + side * 0.9 - 0.45, v + 0.55, FL, "front")
                fr.put("Chair_Visitor", u + side * 0.9 + 0.45, v + 0.55, FL, "front")
                u += side * 1.9
        v += 1.55
    for k in range(max(1, int(fr.depth / 4.5))):
        for u in ((-fr.width / 4, fr.width / 4) if fr.width > 7 else (0.0,)):
            fr.put("Ceiling_Fan", u, 2.5 + 4.5 * k, CEIL)
    fr.wall_item("Wall_Clock", "back", 0.0)
    fr.wall_item("Notice_Board", "back", 2.0)
    fr.wall_item("AC_Unit", "left", fr.depth * 0.5, y=FL + 0.2)
    ceiling_grid(r["rect"], 3.4, "Tube_Light")
    room_lights(r["rect"], per=50.0)


def f_cabin(r):
    fr = Frame(r["rect"], r["front"])
    w = fr.width
    fr.put("Table_Faculty", 0.0, 1.7, FL, "in")
    fr.put("Chair_Office", 0.0, 0.95, FL, "in")
    for u in (-0.45, 0.45):
        fr.put("Chair_Visitor", u, 2.75, FL, "front")
    fr.wall_item("Bookshelf", "left", 1.0)
    if fr.depth > 5.0:
        fr.wall_item("Bookshelf", "left", 2.05)
    fr.wall_item("Filing_Cabinet", "right", 1.0)
    fr.put("Plant_Pot", w / 2 - 0.45, fr.depth - 0.8, FL)
    fr.wall_item("AC_Unit", "right", fr.depth * 0.6, y=FL + 0.2)
    fr.wall_item("Notice_Board", "right", min(fr.depth - 1.5, 3.2))
    if fr.depth > 6.5:
        fr.put("Sofa_1", -w / 2 + 0.55, fr.depth - 1.6, FL, "right")
        fr.put("Table_Coffee", 0.4, fr.depth - 1.6, FL, "in", yaw_extra=90)
    ceiling_grid(r["rect"], 2.4)
    room_lights(r["rect"], per=40.0)


def f_admin(r):
    fr = Frame(r["rect"], r["front"])
    for i in range(3):
        for j in range(2):
            u = -fr.width / 2 + 2.2 + i * ((fr.width - 4.4) / 2)
            v = 1.6 + j * 2.6
            fr.put("Desk_Computer", u, v, FL, "front" if j else "in")
            fr.put("Chair_Office", u, v + (0.65 if j else -0.65), FL, "front" if j else "in")
    for k in range(5):
        fr.wall_item("Filing_Cabinet", "back", -fr.width / 2 + 0.6 + 0.5 * k)
    fr.wall_item("Notice_Board", "left", fr.depth / 2)
    fr.wall_item("Wall_Clock", "right", fr.depth / 2)
    fr.put("Water_Cooler", fr.width / 2 - 0.45, fr.depth - 0.5, FL, "front")
    fr.put("Plant_Pot", -fr.width / 2 + 0.5, 0.5, FL)
    ceiling_grid(r["rect"], 2.8)
    room_lights(r["rect"], per=40.0)


def f_meeting(r):
    fr = Frame(r["rect"], r["front"])
    v = fr.depth / 2 + 0.3
    fr.put("Table_Meeting", 0.0, v, FL, "right")
    for k in range(4):
        for side in (-1, 1):
            fr.put("Chair_Office", side * 1.0, v - 1.5 + k * 1.0, FL, "left" if side > 0 else "right")
    fr.put("Chair_Office", 0.0, v + 2.6, FL, "front")
    fr.wall_item("TV_Wall", "front", 0.0)
    fr.wall_item("Whiteboard", "left", v)
    fr.put("Plant_Pot", fr.width / 2 - 0.5, 0.6, FL)
    fr.put("Plant_Pot", -fr.width / 2 + 0.5, 0.6, FL)
    ceiling_grid(r["rect"], 2.6)
    room_lights(r["rect"], per=35.0)


def f_syndicate(r):
    fr = Frame(r["rect"], r["front"])
    fr.wall_item("Whiteboard", "front", 0.0)
    fr.wall_item("TV_Wall", "back", 0.0)
    n = max(1, int((fr.depth - 2.0) / 4.5))
    for k in range(n):
        v = 2.8 + k * ((fr.depth - 4.6) / max(1, n - 1)) if n > 1 else fr.depth / 2
        fr.put("Table_Round", 0.0, v, FL)
        for j in range(6):
            a = 2 * math.pi * j / 6
            fr.put("Chair_Office", 1.05 * math.cos(a), v + 1.05 * math.sin(a), FL, yaw_extra=0.0)
            PLACED[-1]["yaw"] = yaw_of(*[-q for q in _uv_dir(fr, math.cos(a), math.sin(a))])
    fr.wall_item("AC_Unit", "left", fr.depth / 2, y=FL + 0.2)
    fr.put("Plant_Pot", fr.width / 2 - 0.45, fr.depth - 0.6, FL)
    ceiling_grid(r["rect"], 2.6)
    room_lights(r["rect"], per=40.0)


def _uv_dir(fr, du, dv):
    return (fr.r[0] * du + fr.f[0] * dv, fr.r[1] * du + fr.f[1] * dv)


def f_seminar(r):
    fr = Frame(r["rect"], r["front"])
    fr.wall_item("Projector_Screen", "front", 0.0)
    fr.wall_item("Whiteboard", "left", fr.depth / 2) if fr.depth > 5 else None
    # Tables in a U facing the screen.
    v0 = 2.6
    fr.put("Table_Meeting", 0.0, fr.depth - 1.4, FL, "in")
    for u in (-2.6, 2.6):
        if abs(u) + 1 < fr.width / 2:
            fr.put("Table_Meeting", u, v0 + 1.0, FL, "right")
    for k in range(4):
        fr.put("Chair_Office", -1.0 + k * 0.7, fr.depth - 0.7, FL, "front")
    for u, face in ((-3.4, "right"), (3.4, "left")):
        if abs(u) + 0.5 < fr.width / 2:
            for k in range(4):
                fr.put("Chair_Office", u, v0 - 0.4 + k * 0.9, FL, face)
    fr.put("Lectern", -fr.width / 2 + 1.2, 1.0, FL, "in")
    fr.put("Projector", 0.0, fr.depth / 2, CEIL, "front")
    ceiling_grid(r["rect"], 2.8)
    room_lights(r["rect"], per=40.0)


def f_complab(r):
    fr = Frame(r["rect"], r["front"])
    fr.wall_item("Whiteboard", "front", -2.2 if fr.width > 8 else 0.0)
    if fr.width > 8:
        fr.wall_item("Projector_Screen", "front", 2.2)
    fr.put("Table_Faculty", fr.width / 2 - 1.6, 1.4, FL, "front")
    fr.put("Chair_Office", fr.width / 2 - 1.6, 0.7, FL, "in")
    v = 3.2
    while v < fr.depth - 1.0:
        u = -fr.width / 2 + 1.0
        while u + 1.2 < fr.width / 2 - 0.4:
            if abs(u + 0.6) > 0.7:
                fr.put("Desk_Computer", u + 0.6, v, FL, "front")
                fr.put("Chair_Office", u + 0.6, v + 0.6, FL, "front")
            u += 1.3
        v += 1.9
    fr.wall_item("AC_Unit", "left", fr.depth * 0.4, y=FL + 0.2)
    fr.wall_item("AC_Unit", "right", fr.depth * 0.6, y=FL + 0.2)
    fr.wall_item("Fire_Extinguisher", "back", fr.width / 2 - 0.8)
    ceiling_grid(r["rect"], 2.8)
    room_lights(r["rect"], per=40.0, color=COOL)


def f_reading(r):
    fr = Frame(r["rect"], r["front"])
    # Shelves along the side walls and the far wall, tables down the middle.
    for wall, length in (("left", fr.depth), ("right", fr.depth)):
        p_ = 0.9
        while p_ < length - 0.9:
            fr.wall_item("Bookshelf", wall, p_)
            p_ += 1.02
    p_ = -fr.width / 2 + 1.6
    while p_ < fr.width / 2 - 1.6:
        fr.wall_item("Bookshelf", "front", p_)
        p_ += 1.02
    v = 2.6
    while v < fr.depth - 2.4:
        for u in (-fr.width / 4, fr.width / 4):
            fr.put("Table_Meeting", u, v, FL, "in", yaw_extra=90)
            for k in range(4):
                for side in (-1, 1):
                    fr.put("Chair_Visitor", u - 1.5 + k * 1.0, v + side * 0.85, FL, "front" if side > 0 else "in")
        v += 3.2
    fr.put("Reception_Counter", 0.0, fr.depth - 1.3, FL, "in")
    fr.put("Chair_Office", -0.6, fr.depth - 1.75, FL, "in")
    fr.put("Plant_Pot", fr.width / 2 - 0.6, fr.depth - 0.6, FL)
    fr.put("Plant_Pot", -fr.width / 2 + 0.6, fr.depth - 0.6, FL)
    ceiling_grid(r["rect"], 2.8)
    room_lights(r["rect"], per=40.0)


def f_cafe(r):
    fr = Frame(r["rect"], r["front"])
    fr.put("Cafe_Counter", -2.2, 1.0, FL, "in")
    fr.put("Cafe_Counter", 2.2, 1.0, FL, "in")
    fr.wall_item("TV_Wall", "front", 0.0)
    fr.wall_item("Notice_Board", "front", -5.0)
    v = 3.6
    while v < fr.depth - 1.0:
        u = -fr.width / 2 + 1.6
        while u < fr.width / 2 - 1.4:
            fr.put("Table_Cafe", u, v, FL)
            for j, face in ((0, "front"), (1, "in"), (2, "left"), (3, "right")):
                du, dv = ((0, 0.62), (0, -0.62), (0.62, 0), (-0.62, 0))[j]
                fr.put("Chair_Cafe", u + du, v + dv, FL, face)
            u += 2.3
        v += 2.3
    fr.put("Water_Cooler", fr.width / 2 - 0.5, 0.5, FL, "in")
    fr.put("Dustbin", fr.width / 2 - 1.2, 0.4, FL)
    fr.put("Dustbin", -fr.width / 2 + 0.5, fr.depth - 0.5, FL)
    fr.put("Plant_Pot", -fr.width / 2 + 0.5, 0.5, FL)
    for k in range(3):
        fr.put("Ceiling_Fan", -fr.width / 3 + k * fr.width / 3, fr.depth / 2 + 1.0, CEIL)
    ceiling_grid(r["rect"], 3.0)
    room_lights(r["rect"], per=45.0)


def f_lounge(r):
    fr = Frame(r["rect"], r["front"])
    groups = max(1, min(3, int(fr.width * fr.depth / 70)))
    for g in range(groups):
        u = -fr.width / 2 + fr.width * (g + 0.5) / groups
        v = fr.depth / 2
        fr.put("Table_Coffee", u, v, FL)
        fr.put("Sofa_3", u, v - 1.25, FL, "in")
        fr.put("Sofa_1", u - 1.5, v + 0.2, FL, "right")
        fr.put("Sofa_1", u + 1.5, v + 0.2, FL, "left")
        fr.put("Plant_Pot", u + 1.6, v - 1.4, FL)
    fr.wall_item("Notice_Board", "back", 0.0)
    fr.wall_item("Bookshelf", "left", fr.depth / 2)
    fr.wall_item("TV_Wall", "right", fr.depth / 2)
    fr.put("Water_Cooler", fr.width / 2 - 0.5, fr.depth - 0.5, FL, "front")
    ceiling_grid(r["rect"], 3.0)
    room_lights(r["rect"], per=45.0)


def f_lobby(r):
    fr = Frame(r["rect"], r["front"])
    # Reception on the west side facing the entrance axis, the crest on the wall behind it.
    X0, X1, Z0, Z1 = r["rect"]
    zc = (Z0 + Z1) / 2 + 2.0
    put("Reception_Counter", X0 + 2.0, zc, FL, 1, 0)
    put("Chair_Office", X0 + 1.3, zc - 0.8, FL, 1, 0)
    put("Chair_Office", X0 + 1.3, zc + 0.8, FL, 1, 0)
    put("Crest_Panel", X0 + PART / 2 + 0.01, zc, FL, 1, 0)
    for k, dz in enumerate((-2.5, 2.5)):
        put("Plant_Pot", X0 + 0.6, zc + dz * 1.4, FL)
    # Seating on the east side.
    for zz in (Z0 + 5.0, Z0 + 12.0):
        put("Table_Coffee", X1 - 2.6, zz, FL, 0, 1)
        put("Sofa_3", X1 - 1.2, zz, FL, -1, 0)
        put("Sofa_1", X1 - 2.6, zz - 1.3, FL, 0, 1)
        put("Sofa_1", X1 - 2.6, zz + 1.3, FL, 0, -1)
    for zz in (Z0 + 8.5, Z0 + 15.5):
        put("Plant_Pot", X1 - 0.6, zz, FL)
    for xx in ((X0 + X1) / 2 - 4.0, (X0 + X1) / 2 + 4.0):
        put("Display_Panel", xx, Z0 + 4.0, FL, 0, 1)
    put("Notice_Board", X1 - PART / 2 - 0.01, Z0 + 2.0, FL, -1, 0)
    if "planter" in r:
        cx, cz = r["planter"]
        for k in range(5):
            a = 2 * math.pi * k / 5
            put("Plant_Pot", cx + 0.9 * math.cos(a), cz + 0.9 * math.sin(a), FL + 0.5)
        for k in range(4):
            a = 2 * math.pi * k / 4 + math.pi / 4
            put("Bench", cx + 2.7 * math.cos(a), cz + 2.7 * math.sin(a), FL, math.cos(a), math.sin(a))
    put("Wall_Clock", X0 + PART / 2 + 0.01, zc - 3.0, FL, 1, 0)
    ceiling_grid(r["rect"], 2.4, "Pin_Spot", margin=1.0)
    room_lights(r["rect"], per=40.0, intensity=1.6)


def f_toilet(r, men):
    fr = Frame(r["rect"], r["front"])
    hw = fr.width / 2
    # Cubicles along the left wall (backs to it), basins on the back wall, urinals on the right.
    n = max(2, int((fr.depth - 2.2) / 1.0))
    for k in range(n):
        v = 1.0 + k * 1.0
        X, Z = fr.P(-hw + PART / 2, v)
        put("Toilet_Cubicle", X, Z, FL, *fr.r)
    if men:
        for k in range(max(2, int((fr.depth - 3.0) / 0.9))):
            fr.wall_item("Urinal", "right", 1.2 + k * 0.9)
    else:
        for k in range(n):
            X, Z = fr.P(hw - PART / 2, 1.0 + k * 1.0)
            put("Toilet_Cubicle", X, Z, FL, -fr.r[0], -fr.r[1])
    fr.wall_item("Basin_Counter", "back", -0.6 if men else 0.0)
    fr.put("Dustbin", 0.0, 0.5, FL)
    ceiling_grid(r["rect"], 2.4)
    room_lights(r["rect"], per=30.0, color=COOL)


def f_server(r):
    fr = Frame(r["rect"], r["front"])
    for k in range(max(1, int((fr.width - 2) / 1.2))):
        fr.put("Server_Rack", -fr.width / 2 + 1.4 + k * 1.2, fr.depth / 2, FL, "front")
        fr.put("Server_Rack", -fr.width / 2 + 1.4 + k * 1.2, fr.depth / 2 + 1.0, FL, "in")
    fr.wall_item("AC_Unit", "left", fr.depth / 2, y=FL + 0.2)
    fr.wall_item("Fire_Extinguisher", "back", 0.0)
    ceiling_grid(r["rect"], 2.4)
    room_lights(r["rect"], per=30.0, color=COOL, intensity=0.9)


def f_store(r):
    fr = Frame(r["rect"], r["front"])
    v = 1.0
    while v < fr.depth - 1.2:
        fr.put("Storage_Rack", -fr.width / 4, v, FL, "in")
        if fr.width > 5:
            fr.put("Storage_Rack", fr.width / 4, v, FL, "in")
        v += 1.6
    ceiling_grid(r["rect"], 3.0, "Tube_Light")
    room_lights(r["rect"], per=40.0, intensity=0.9)


def f_research(r):
    fr = Frame(r["rect"], r["front"])
    for i in range(2):
        for j in range(3):
            u = -fr.width / 4 + i * fr.width / 2
            v = 2.0 + j * 2.4
            if v < fr.depth - 1.2:
                fr.put("Desk_Computer", u, v, FL, "front")
                fr.put("Chair_Office", u, v + 0.6, FL, "front")
    for k in range(3):
        fr.wall_item("Bookshelf", "front", -fr.width / 2 + 0.7 + k * 1.02)
    fr.wall_item("Whiteboard", "right", fr.depth / 2)
    ceiling_grid(r["rect"], 2.8)
    room_lights(r["rect"], per=40.0)


def f_activity(r):
    fr = Frame(r["rect"], r["front"])
    for k in range(3):
        u = -fr.width / 2 + fr.width * (k + 0.5) / 3
        v = fr.depth / 2
        fr.put("Table_Round", u, v, FL)
        for j in range(5):
            a = 2 * math.pi * j / 5
            fr.put("Chair_Visitor", u + 1.05 * math.cos(a), v + 1.05 * math.sin(a), FL)
            PLACED[-1]["yaw"] = yaw_of(*[-q for q in _uv_dir(fr, math.cos(a), math.sin(a))])
    for k in range(3):
        fr.wall_item("Notice_Board", "front", -4.0 + 4.0 * k)
    fr.put("Display_Panel", fr.width / 2 - 1.5, fr.depth - 1.2, FL, "front")
    ceiling_grid(r["rect"], 3.0)
    room_lights(r["rect"], per=45.0)


def f_gallery(r):
    X0, X1, Z0, Z1 = r["rect"]
    # Exhibition panels down the middle of the long axis, benches and plants between them.
    along_x = X1 - X0 > Z1 - Z0
    length = max(X1 - X0, Z1 - Z0)
    k = 0
    t = 4.0
    while t < length - 3.0:
        X = X0 + t if along_x else (X0 + X1) / 2
        Z = (Z0 + Z1) / 2 if along_x else Z0 + t
        if k % 2 == 0:
            put("Display_Panel", X, Z, FL, 0 if along_x else 1, 1 if along_x else 0)
        else:
            if along_x:
                put("Bench", X, Z - 1.2, FL, 0, 1)
                put("Plant_Pot", X, Z + 1.5, FL)
            else:
                put("Bench", X + 1.2, Z, FL, -1, 0)
                put("Plant_Pot", X - 1.5, Z, FL)
        t += 3.5
        k += 1
    fr = Frame(r["rect"], r["front"])
    for v in (2.0, fr.depth - 2.0):
        fr.put("Water_Cooler", fr.width / 2 - 0.5, v, FL, "left")
    fr.wall_item("Fire_Extinguisher", "right", fr.depth / 2)
    ceiling_grid(r["rect"], 3.0, "Pin_Spot")
    room_lights(r["rect"], per=50.0, intensity=1.5)


def f_board(r):
    fr = Frame(r["rect"], r["front"])
    vc = fr.depth / 2 + 0.2
    # A hollow square of tables, chairs all round the outside.
    for u in (-2.0, 2.0):
        for sgn in (-1, 1):
            fr.put("Table_Meeting", u, vc + sgn * 2.2, FL, "in")
    for u in (-4.6, 4.6):
        fr.put("Table_Meeting", u, vc, FL, "right")
    for k in range(8):
        u = -3.5 + k
        fr.put("Chair_Office", u, vc + 3.15, FL, "front")
        fr.put("Chair_Office", u, vc - 3.15, FL, "in")
    for dv in (-1.2, 0.0, 1.2):
        fr.put("Chair_Office", -5.55, vc + dv, FL, "right")
        fr.put("Chair_Office", 5.55, vc + dv, FL, "left")
    fr.wall_item("TV_Wall", "front", 0.0)
    fr.wall_item("Crest_Panel", "back", 0.0)
    if fr.width > 15:
        fr.put("Sofa_3", fr.width / 2 - 1.2, vc, FL, "left")
        fr.put("Table_Coffee", fr.width / 2 - 2.3, vc, FL, "in", yaw_extra=90)
    for u, v in ((-fr.width / 2 + 0.6, 0.6), (fr.width / 2 - 0.6, 0.6), (-fr.width / 2 + 0.6, fr.depth - 0.6),
                 (fr.width / 2 - 0.6, fr.depth - 0.6)):
        fr.put("Plant_Pot", u, v, FL)
    fr.wall_item("AC_Unit", "left", fr.depth / 2, y=FL + 0.2)
    fr.wall_item("Wall_Clock", "right", fr.depth / 2)
    ceiling_grid(r["rect"], 2.6)
    room_lights(r["rect"], per=35.0)


def f_director(r):
    fr = Frame(r["rect"], r["front"])
    # The desk near the front wall, a meeting table and a sofa set further in, shelves along the sides.
    fr.put("Table_Faculty", 0.0, 2.0, FL, "in")
    fr.put("Chair_Office", 0.0, 1.25, FL, "in")
    for u in (-0.5, 0.5):
        fr.put("Chair_Visitor", u, 3.1, FL, "front")
    fr.wall_item("Crest_Panel", "front", 0.0)
    for k in range(4):
        fr.wall_item("Bookshelf", "left", 1.0 + 1.02 * k)
    fr.wall_item("Filing_Cabinet", "right", 1.0)
    fr.wall_item("TV_Wall", "right", fr.depth / 2 + 1.0)
    v = fr.depth - 2.4
    fr.put("Table_Round", -fr.width / 4, v, FL)
    for k in range(4):
        a = math.pi / 2 * k + math.pi / 4
        fr.put("Chair_Visitor", -fr.width / 4 + 0.85 * math.cos(a), v + 0.85 * math.sin(a), FL, "in",
               yaw_extra=math.degrees(-a) - 90)
    fr.put("Sofa_3", fr.width / 4, v + 1.0, FL, "front")
    fr.put("Table_Coffee", fr.width / 4, v - 0.2, FL, "in")
    fr.put("Sofa_1", fr.width / 4 - 1.7, v - 0.2, FL, "right")
    fr.put("Sofa_1", fr.width / 4 + 1.7, v - 0.2, FL, "left")
    for u in (-fr.width / 2 + 0.6, fr.width / 2 - 0.6):
        fr.put("Plant_Pot", u, 0.6, FL)
    fr.wall_item("AC_Unit", "left", fr.depth * 0.7, y=FL + 0.2)
    ceiling_grid(r["rect"], 2.6)
    room_lights(r["rect"], per=35.0)


def f_foyer(r):
    fr = Frame(r["rect"], r["front"])
    X0, X1, Z0, Z1 = r["rect"]
    zc = (Z0 + Z1) / 2
    for dz in (-8.0, -4.5, 4.5, 8.0):
        put("Display_Panel", X0 + 6.0, zc + dz, FL, 1, 0)
    put("Table_Coffee", X0 + 3.0, Z0 + 4.0, FL, 1, 0)
    put("Sofa_3", X0 + 1.6, Z0 + 4.0, FL, 1, 0)
    put("Table_Coffee", X0 + 3.0, Z1 - 4.0, FL, 1, 0)
    put("Sofa_3", X0 + 1.6, Z1 - 4.0, FL, 1, 0)
    for zz in (Z0 + 1.2, Z1 - 1.2, zc - 2.6, zc + 2.6):
        put("Plant_Pot", X0 + 9.0, zz, FL)
    ceiling_grid((X0 + 1, X1 - 3, Z0 + 1, Z1 - 1), 3.0, "Pin_Spot", y=HIGH_CEIL)
    room_lights((X0, X1 - 3, Z0, Z1), y=HIGH_CEIL - 0.4, per=60.0, intensity=2.0)


FURNISH = {
    "case": f_case, "classroom": f_classroom, "cabin": f_cabin, "admin": f_admin, "meeting": f_meeting,
    "syndicate": f_syndicate, "seminar": f_seminar, "complab": f_complab, "reading": f_reading, "cafe": f_cafe,
    "lounge": f_lounge, "lobby": f_lobby, "toilet_m": lambda r: f_toilet(r, True), "toilet_w": lambda r: f_toilet(r, False),
    "server": f_server, "store": f_store, "research": f_research, "activity": f_activity, "gallery": f_gallery,
    "foyer": f_foyer,
}


def in_rects(X, Z, rects):
    return any(a <= X <= b and c <= Z <= d for a, b, c, d in rects)


def corridor_rects():
    rects = list(CORRIDORS)
    for r in ROOMS:
        if r["kind"] in ("lobby", "gallery", "foyer"):
            rects.append(r["rect"])
    return rects


def furnish_corridors():
    for X0, X1, Z0, Z1 in CORRIDORS:
        w, d = X1 - X0, Z1 - Z0
        along_x = w >= d
        length = max(w, d)
        n = max(1, int(length / 3.0))
        for k in range(n):
            t = (k + 0.5) / n
            X = X0 + w * t if along_x else (X0 + X1) / 2
            Z = (Z0 + Z1) / 2 if along_x else Z0 + d * t
            put("Light_Panel", X, Z, CEIL)
        nl = max(1, int(length / 9.0))
        for k in range(nl):
            t = (k + 0.5) / nl
            LIGHTS.append(dict(x=round(X0 + w * t if along_x else (X0 + X1) / 2, 2), y=CEIL - 0.25,
                               z=round((Z0 + Z1) / 2 if along_x else Z0 + d * t, 2), range=7.5, intensity=1.1, color=WARM))
        if length < 10:
            continue
        # Benches, notice boards, bins and extinguishers along the long corridors.
        nb = int(length / 14.0)
        for k in range(nb):
            t = (k + 0.7) / max(1, nb)
            if along_x:
                X = X0 + w * t
                put("Notice_Board", X, Z1 - PART / 2 - 0.01, FL, 0, -1)
                put("Bench", X + 2.4, Z1 - 0.35, FL, 0, -1)
                put("Dustbin", X - 1.6, Z0 + 0.3, FL)
                if k % 2 == 0:
                    put("Fire_Extinguisher", X + 4.5, Z0 + PART / 2 + 0.01, FL, 0, 1)
            else:
                Z = Z0 + d * t
                put("Notice_Board", X1 - PART / 2 - 0.01, Z, FL, -1, 0)
                put("Bench", X1 - 0.35, Z + 2.4, FL, -1, 0)
                put("Dustbin", X0 + 0.3, Z - 1.6, FL)
                if k % 2 == 0:
                    put("Fire_Extinguisher", X0 + PART / 2 + 0.01, Z + 4.5, FL, 1, 0)


def place_doors():
    """An open door leaf in every partition doorway, swung into the room (away from corridors)."""
    crs = corridor_rects()
    for A, B_, doors in WALLS:
        dx, dz = B_[0] - A[0], B_[1] - A[1]
        L = math.hypot(dx, dz)
        t = (dx / L, dz / L)
        n = (-t[1], t[0])
        for c, w in doors:
            mid = (A[0] + t[0] * c, A[1] + t[1] * c)
            plus = (mid[0] + n[0] * 0.6, mid[1] + n[1] * 0.6)
            room_side = n if not in_rects(*plus, crs) else (-n[0], -n[1])
            leaves = [(c - w / 2 + 0.03, 1.0)] if w < 1.4 else [(c - w / 2 + 0.03, 1.0), (c + w / 2 - 0.03, -1.0)]
            for s_h, sgn in leaves:
                hx = A[0] + t[0] * s_h + room_side[0] * (PART / 2)
                hz = A[1] + t[1] * s_h + room_side[1] * (PART / 2)
                # Leaf +X points into the room; Unity yaw maps local +X to (cos y, -sin y).
                yaw = math.degrees(math.atan2(-room_side[1], room_side[0]))
                put("Door_Leaf", hx, hz, FL, yaw=round(yaw, 2))
                if sgn < 0:
                    PLACED[-1]["yaw"] = round(yaw, 2)


def place_entrance_doors():
    """Glass door leaves standing open at the facade doorways."""
    for (A, B_) in COURT_RUNS + OUTER_RUNS:
        L = math.dist(A, B_)
        t = ((B_[0] - A[0]) / L, (B_[1] - A[1]) / L)
        bays, bw = B.facade_bays(L)
        if (A, B_) in COURT_RUNS:
            doors, depth = [bays // 2], WC
            c = ((IX0 + IX1) / 2, (IZ0 + IZ1) / 2)
            nrm = (t[1], -t[0])
            mid = ((A[0] + B_[0]) / 2, (A[1] + B_[1]) / 2)
            if nrm[0] * (mid[0] - c[0]) + nrm[1] * (mid[1] - c[1]) < 0:
                nrm = (-nrm[0], -nrm[1])
        else:
            doors, depth = outer_doors(A, B_), WO
            c = ((OX0 + OX1) / 2, (OZ0 + OZ1) / 2)
            nrm = (t[1], -t[0])
            mid = ((A[0] + B_[0]) / 2, (A[1] + B_[1]) / 2)
            if nrm[0] * (mid[0] - c[0]) + nrm[1] * (mid[1] - c[1]) > 0:
                nrm = (-nrm[0], -nrm[1])     # towards the inside
        for i in doors:
            s0, s1 = i * bw + 0.5 + 0.06, (i + 1) * bw - 0.5 - 0.06
            for s_h in (s0, s1):
                hx = A[0] + t[0] * s_h + nrm[0] * depth
                hz = A[1] + t[1] * s_h + nrm[1] * depth
                put("Door_Glass", hx, hz, FL, yaw=round(math.degrees(math.atan2(-nrm[1], nrm[0])), 2))


def f_stacks(r):
    fr = Frame(r["rect"], r["front"])
    # A issue desk and a catalogue computer by the front wall, then rows of double-sided stacks.
    fr.put("Reception_Counter", -fr.width / 4, 1.6, FL, "in")
    fr.put("Chair_Office", -fr.width / 4 - 0.6, 1.0, FL, "in")
    fr.put("Desk_Computer", fr.width / 4, 1.4, FL, "front")
    fr.put("Chair_Office", fr.width / 4, 2.0, FL, "front")
    fr.wall_item("Notice_Board", "front", 0.0)
    v = 4.0
    while v < fr.depth - 1.5:
        u = -fr.width / 2 + 1.5
        while u + 2.0 < fr.width / 2 - 1.3:
            fr.put("Library_Stack", u + 1.0, v, FL, "in")
            u += 2.05
        v += 1.95
    fr.wall_item("Fire_Extinguisher", "back", fr.width / 2 - 0.8)
    ceiling_grid(r["rect"], 2.6)
    room_lights(r["rect"], per=40.0)


FURNISH["stacks"] = f_stacks
FURNISH["board"] = f_board
FURNISH["director"] = f_director


def stair_extras():
    """Lift doors beside each stair, and lights over the landing and in the stairwell."""
    for st in STAIRS:
        X, Z = st.P(ST_L, ST_W + PART + 1.4)
        put("Lift_Door", X, Z, FL, 0.0, -st.uz)
        X, Z = st.P(ST_L, ST_W + PART + 2.9)
        put("Fire_Extinguisher", X, Z, FL, 0.0, -st.uz)
        lx, lz = st.P(0.75, 1.5)
        put("Light_Panel", lx, lz, CEIL)
        LIGHTS.append(dict(x=round(lx, 2), y=CEIL - 0.25, z=round(lz, 2), range=6.5, intensity=1.0, color=WARM))
        mx, mz = st.P((ST_MID + ST_L) / 2, 1.5)
        LIGHTS.append(dict(x=round(mx, 2), y=round(FL + FLOOR / 2 + 2.1, 2), z=round(mz, 2), range=6.0, intensity=0.9, color=WARM))


def furnish_auditorium():
    seats = 0
    for i in range(ROWS):
        r = row_radius(i)
        y = FL + RAKE * i
        for a0, a1 in hall_seat_spans(i):
            n = int((a1 - a0) * r / SEAT_W)
            for k in range(n):
                a = a0 + (a1 - a0) * (k + 0.5) / n
                X, Z = fan_point(r + 0.06, a)
                put("Auditorium_Seat", X, Z, y, HC[0] - X, HC[1] - Z)
                seats += 1
    ys = FL + STAGE_H
    # The stage: the dais table with its chairs, a lectern, the screen, curtains and speakers.
    tx = HC[0] - 1.8
    put("Table_Meeting", tx, HC[1], ys, 1, 0)
    for dz in (-1.5, -0.5, 0.5, 1.5):
        put("Chair_Office", tx - 0.85, HC[1] + dz, ys, 1, 0)
    lx, lz = fan_point(3.0, math.radians(-38))
    put("Lectern", lx, lz, ys, 1, 0.3)
    put("Plant_Pot", *fan_point(3.4, math.radians(38)), ys)
    put("Stage_Screen", hall_arc_x(AZC) + 0.05, AZC, ys, 1, 0)
    for dz in (-10.5, -7.9, -5.3, 5.3, 7.9, 10.5):
        Z = AZC + dz
        X = hall_arc_x(Z) + 0.08
        put("Stage_Curtain", X, Z, ys, AX0 + AR - X, AZC - Z)     # flat against the curved wall
    for a in (-72, 72):
        X, Z = fan_point(STAGE_R - 0.5, math.radians(a))
        put("Speaker", X, Z, ys, math.cos(math.radians(a)), math.sin(math.radians(a)))
    # Downlights, and the lights that matter.
    for rr, step in ((2.5, 45), (7.5, 22), (11.0, 16), (14.5, 12)):
        for a in range(-80, 81, step):
            X, Z = fan_point(rr, math.radians(a))
            put("Pin_Spot", X, Z, HALL_CEIL)
    for rr, angs, inten in ((2.0, (-40, 0, 40), 2.2), (8.5, (-55, -18, 18, 55), 1.9), (13.5, (-60, -30, 0, 30, 60), 1.9)):
        for a in angs:
            X, Z = fan_point(rr, math.radians(a))
            LIGHTS.append(dict(x=round(X, 2), y=round(HALL_CEIL - 0.8, 2), z=round(Z, 2), range=11.0, intensity=inten, color=WARM))
    for Z in (HALL_S + 5.0, HALL_N - 5.0):
        LIGHTS.append(dict(x=round(HALL_E - 4.0, 2), y=FL + 3.0, z=round(Z, 2), range=8.0, intensity=1.2, color=WARM))
    # Timber acoustic panels on the side walls, and high up on the back wall above the balcony.
    for Z, dz in ((HALL_S, 1.0), (HALL_N, -1.0)):
        X = hall_arc_x(Z) + 1.2
        while X < HALL_E - 1.0:
            for y in (FL + 1.0, FL + FLOOR + 1.6, FL + 2 * FLOOR + 1.6):
                put("Acoustic_Panel", X, Z + dz * 0.01, y, 0, dz)
            X += 1.45
    door_z = [Z for zs in HALL_DOORS.values() for Z in zs]
    Z = HALL_S + 1.0
    while Z < HALL_N - 1.0:
        put("Acoustic_Panel", HALL_E - 0.01, Z, FL + FLOOR + 3.0, -1, 0)
        if not any(abs(Z - dz) < 1.7 for dz in door_z) and not (266.0 < Z < 297.0):
            put("Acoustic_Panel", HALL_E - 0.01, Z, FL + 0.6, -1, 0)
        Z += 1.45
    # Panels and plants along the balcony's end walls, which face the side aisles.
    rb = row_radius(ROWS - 1) + ROW_PITCH / 2
    for sgn in (-1, 1):
        X0, Z = fan_point(rb, sgn * row_end(ROWS - 1))
        X = X0 + 1.2
        while X < HALL_E - 2.6:
            put("Acoustic_Panel", X, Z + sgn * 0.01, FL + 0.5, 0, sgn)
            X += 1.45
        put("Plant_Pot", X0 + 0.6, Z + sgn * 0.5, FL)
    # Exit signs over the doors on both sides, extinguishers beside them.
    for k, zs in HALL_DOORS.items():
        for Z in zs:
            top = level_floor(k) + HALL_DOOR_H + 0.15
            put("Exit_Sign", HALL_E - 0.01, Z, top, -1, 0)
            put("Exit_Sign", OX0 + WO + 0.01, Z, top, 1, 0)
            put("Fire_Extinguisher", HALL_E - 0.01, Z + 1.6, level_floor(k), -1, 0)
    # The sound and light desk at the back of the balcony, plants at its ends.
    yb = FL + FLOOR
    put("Desk_Computer", HALL_E - 1.5, AZC - 0.7, yb, -1, 0)
    put("Desk_Computer", HALL_E - 1.5, AZC + 0.7, yb, -1, 0)
    put("Chair_Office", HALL_E - 0.85, AZC - 0.7, yb, -1, 0)
    put("Chair_Office", HALL_E - 0.85, AZC + 0.7, yb, -1, 0)
    for Z in (HALL_S + 12.0, HALL_N - 12.0):
        put("Plant_Pot", HALL_E - 0.6, Z, yb)
    print("Auditorium: %d seats" % seats)


def furnish_level(k):
    use_level(k)
    n0, l0 = len(PLACED), len(LIGHTS)
    for r in ROOMS:
        FURNISH[r["kind"]](r)
    furnish_corridors()
    place_doors()
    if k == 0:
        place_entrance_doors()
        # The vestibule in the glass bay: plants and a pair of benches, a high light.
        put("Plant_Pot", EX - 6.0, OZ1 + 2.0, FL)
        put("Plant_Pot", EX + 6.0, OZ1 + 2.0, FL)
        LIGHTS.append(dict(x=EX, y=HIGH_CEIL - 0.5, z=OZ1 + 4.0, range=9.0, intensity=1.6, color=WARM))
        put("Pin_Spot", EX, OZ1 + 4.0, HIGH_CEIL)
    # Nothing may stand on the stairs or in front of the lifts.
    keep = [rc for st in STAIRS for rc in st.keep_out()]
    PLACED[n0:] = [p for p in PLACED[n0:] if not in_rects(p["x"], p["z"], keep)]
    stair_extras()
    if k == 0:
        furnish_auditorium()
    dy = k * FLOOR
    for p in PLACED[n0:]:
        p["y"] = round(p["y"] + dy, 3)
        p["level"] = k
    for l in LIGHTS[l0:]:
        l["y"] = round(l["y"] + dy, 2)
        l["level"] = k


def furnish():
    for k in range(PAV_LEVEL + 1):
        furnish_level(k)
    use_level(0)


# ------------------------------------------------------------------ output
def export_model(m, path):
    B.clear_scene()
    obj = m.to_object()
    B.export(obj, path)
    return obj


def main():
    os.makedirs(PROP_DIR, exist_ok=True)
    # The hostel module imports this one by name: make that this very module, not a second copy.
    sys.modules.setdefault("build_interiors", sys.modules[__name__])
    import build_hostel_interiors as HI   # registers the hostel and mess furniture too
    shell = academic_interior()
    furnish()
    rooms = [dict(name=r["name"], kind=r["kind"], rect=[round(v, 2) for v in r["rect"]], level=k, zone=0)
             for k, lv in enumerate(LEVEL_PLANS) for r in lv["rooms"]]
    buildings = [dict(shell=shell, name="Academic Block", floorOffset=FL, maxLevel=PAV_LEVEL)]
    for extra in HI.build_all():
        buildings.append(extra)
        rooms += extra["rooms"]
    shells, zones = [], []
    for bd in buildings:
        sh = bd["shell"]
        path = os.path.join(B.OUT_DIR, sh.name + ".fbx")
        obj = export_model(sh, path)
        print("%-30s %6d faces" % (sh.name, len(obj.data.polygons)))
        lo, hi = sh.world_bounds()
        shells.append({
            "name": sh.name, "fbx": "Assets/Models/Buildings/%s.fbx" % sh.name,
            "pivotX": round(sh.px, 3), "pivotZ": round(sh.pz, 3),
            "boundsMin": [round(v, 2) for v in lo], "boundsMax": [round(v, 2) for v in hi],
            "replaces": [],
        })
        zones.append({"name": bd["name"], "shell": sh.name,
                      "min": [round(lo[0] - 2, 2), round(lo[1] - 2, 2)], "max": [round(hi[0] + 2, 2), round(hi[1] + 2, 2)],
                      "baseX": round(sh.px, 3), "baseZ": round(sh.pz, 3),
                      "floorOffset": bd["floorOffset"], "maxLevel": bd["maxLevel"]})
    props_out = []
    used = sorted({p["prop"] for p in PLACED})
    for name in sorted(PROPS):
        fn, solid = PROPS[name]
        m = fn()
        o = export_model(m, os.path.join(PROP_DIR, name + ".fbx"))
        props_out.append(dict(name=name, fbx="Assets/Models/Props/%s.fbx" % name, solid=solid))
        print("  prop %-20s %4d faces%s" % (name, len(o.data.polygons), "" if name in used else "  (unused)"))
    data = {
        "info": "Generated by tools/blender/build_interiors.py. Heights are relative to the building base.",
        "shells": shells,
        "zones": zones,
        "propModels": props_out,
        "props": [dict(p, zone=p.get("zone", 0)) for p in PLACED],
        "lights": [dict(l, zone=l.get("zone", 0)) for l in LIGHTS],
        "levelHeight": FLOOR,
        "rooms": rooms,
    }
    with open(OUT_JSON, "w") as f:
        json.dump(data, f, separators=(",", ":"))
    print("Wrote %s: %d buildings, %d props, %d lights, %d rooms" % (OUT_JSON, len(zones), len(PLACED), len(LIGHTS), len(rooms)))


if __name__ == "__main__":
    main()
