#!/usr/bin/env python3
"""Phase 4C: walk-in hostels and Annapurna Mess. Imported by build_interiors.py, which writes
the shells and furniture of every building into interiors.json (one zone per building).

Hostels (Brahmaputra, Hostel U, the three chain blocks and Sindhu) have seven floors. Each
wing of a hostel model is planned on its own from its facade (build_buildings.hostel_facade):
    - one room behind each window bay, so every room has its window pair;
    - wide wings (20 m or more) get a ring corridor round a core of washrooms, baths, a pantry
      and a common room; narrow ones a central corridor with rooms on both sides;
    - a dog-leg stair behind one or two of the jali-screened slots, next to the entrance;
    - rooms where the facade is buried in the next wing (no windows) become washrooms,
      laundries and stores, and a passage leads through to the next wing's corridor.
Floors sit 0.6 m above each storey line and ceilings 2.5 m above the floor (as in the academic
block, so the storey arithmetic in InteriorZone is shared).

Annapurna Mess: a double-height dining hall with long steel tables, serving counters along
the kitchen wall with hatches through it, hand-wash troughs and water coolers; the kitchen
behind has cooking ranges under hoods, prep tables, sinks, racks and fridges.
"""
import math
import random

import build_buildings as B
import build_interiors as I
from build_buildings import FLOOR, BUILDINGS, Model
from build_interiors import (PART, DOOR_H, FL, CEIL, HIGH_CEIL, WARM, COOL, LIGHTS, PLACED, Line, Frame,
                             put, partition, wall_with_windows, hrect, hrect_cut, prop, bx, cyl, obox, new,
                             ST_L, ST_W, ST_LAND, level_floor, level_ceil)

HW = B.HOSTEL_WALL          # interior face of the outer walls, inside the facade line (0.3)
JALI = 0.6                  # depth of the jali screen behind the facade line
SLOT_HALF = 1.6
CW = 2.0                    # corridor width
BAND = 5.8                  # target depth of a band of rooms
TOP = 6                     # top storey of the seven-floor hostels
EPS = 1e-6

HOSTELS = [
    ("Brahmaputra_Hostel", "Brahmaputra", ["Brahmaputra Hostel", "Brahmaputra Hostel - South"]),
    ("Hostel_U", "Hostel U", ["Hostel U - West", "Hostel U - North", "Hostel U - East"]),
    ("Hostel_Chain", "Chain Block", ["Hostel Chain 1", "Hostel Chain 2", "Hostel Chain 3"]),
    ("Sindhu_Hostel", "Sindhu", ["Sindhu Hostel - North", "Sindhu Hostel - Step 1", "Sindhu Hostel - West",
                                 "Sindhu Hostel - Step 2", "Sindhu Hostel - South"]),
]

INWARD = {"S": (0.0, 1.0), "N": (0.0, -1.0), "W": (1.0, 0.0), "E": (-1.0, 0.0)}
AXIS = {"S": 0, "N": 0, "W": 1, "E": 1}      # world axis that runs along the side (0 = X, 1 = Z)


def overlap(a0, a1, b0, b1):
    return min(a1, b1) - max(a0, b0)


# ------------------------------------------------------------------ facade features
class Side:
    """What the facade puts on one side of a wing, in world coordinates along the side."""

    def __init__(self, name):
        self.name = name
        self.windows = []      # (a0, a1) window openings
        self.door = None       # (a0, a1) ground-floor entrance opening
        self.door_bay = None   # (a0, a1) the whole bay of the entrance
        self.door_window = None  # (a0, a1) the window over the entrance on the upper floors
        self.slots = []        # (a0, a1, centre) jali stair slots
        self.cuts = set()      # bay boundaries, slot edges and the ends of the visible stretches
        self.visible = []      # (a0, a1) stretches with a facade (not buried in another wing)

    def has_window(self, a0, a1):
        return any(overlap(a0, a1, w0, w1) > 0.2 for w0, w1 in self.windows) or (
            self.door is not None and overlap(a0, a1, *self.door) > 0.2)

    def hits_window(self, a, margin=0.12):
        return any(w0 - margin < a < w1 + margin for w0, w1 in self.windows) or (
            self.door is not None and self.door[0] - margin < a < self.door[1] + margin)

    def in_slot(self, a):
        return any(s0 - 0.05 < a < s1 + 0.05 for s0, s1, _ in self.slots)


def facade_sides(m, rects, k):
    """The four Sides of wing k, replaying build_buildings.detailed_model for the hostel style."""
    X0, X1, Z0, Z1 = rects[k]
    others = rects[:k] + rects[k + 1:]
    sides = {s: Side(s) for s in "NSEW"}
    poly = B.rect_poly(m, X0, X1, Z0, Z1)
    door_done = False
    for run in B.outline_runs(m, poly):
        for (s0, s1) in B.hidden_intervals(m, run, others):
            sub = B.SubRun(run, s0, s1)
            L = sub.L
            slot_at = B.hostel_slots(L)
            door = not door_done and B.hostel_door_bay(L, slot_at) is not None
            door_done = door_done or door
            door_bay = B.hostel_door_bay(L, slot_at) if door else None

            def W(s):
                p = sub.P(s, 0, 0)
                return (m.px - p[0], m.pz - p[1])

            A, Bw = W(0.0), W(L)
            if abs(A[1] - Z0) < 1e-3 and abs(Bw[1] - Z0) < 1e-3:
                sd = sides["S"]
            elif abs(A[1] - Z1) < 1e-3 and abs(Bw[1] - Z1) < 1e-3:
                sd = sides["N"]
            elif abs(A[0] - X0) < 1e-3:
                sd = sides["W"]
            else:
                sd = sides["E"]
            ax = AXIS[sd.name]

            def a(s):
                return W(s)[ax]

            def span(s_a, s_b):
                return (min(a(s_a), a(s_b)), max(a(s_a), a(s_b)))

            sd.visible.append(span(0.0, L))
            sd.cuts.update((a(0.0), a(L)))
            for c in slot_at:
                lo, hi = span(c - SLOT_HALF, c + SLOT_HALF)
                sd.slots.append((lo, hi, a(c)))
                sd.cuts.update((lo, hi))
            for p0, p1, bays, bw in B.hostel_pieces(L, slot_at):
                for i in range(bays):
                    b0, b1 = p0 + i * bw, p0 + (i + 1) * bw
                    sd.cuts.update((a(b0), a(b1)))
                    if bw < 2.0:
                        continue
                    glass = span(*B.hostel_glass(b0, b1, i, bays))
                    if door_bay and abs(b0 - door_bay[0]) < 1e-6:
                        sd.door = span(b0 + 0.5, b1 - 0.5)
                        sd.door_bay = span(b0, b1)
                        sd.door_window = glass      # the bay's window on the upper floors
                        continue
                    sd.windows.append(glass)
    for sd in sides.values():
        sd.cuts = sorted(sd.cuts)
        sd.windows.sort()
    return sides


# ------------------------------------------------------------------ wing plans
class Seg:
    """A room-sized piece of a band of rooms, between two partitions."""

    def __init__(self, band, a0, a1, kind="room"):
        self.band, self.a0, self.a1, self.kind = band, a0, a1, kind
        self.corner = False
        self.window = True
        self.slot = None      # (a0, a1, centre) when the segment is a jali slot
        self.stair = None
        self.lobby = False
        self.number = 0

    @property
    def width(self):
        return self.a1 - self.a0

    def rect(self):
        return self.band.rect(self.a0, self.a1)


class Band:
    """Rooms along one side of a wing, between the outer wall and a corridor wall."""

    def __init__(self, plan, side, lo, hi, depth):
        self.plan, self.side, self.lo, self.hi, self.depth = plan, side, lo, hi, depth
        x0, x1, z0, z1 = plan.inner
        self.outer = {"S": z0, "N": z1, "W": x0, "E": x1}[side]
        self.n = INWARD[side]
        self.inner = self.outer + (self.n[1] if AXIS[side] == 0 else self.n[0]) * depth
        self.segs = []

    def rect(self, a0, a1):
        c0, c1 = sorted((self.outer, self.inner))
        if AXIS[self.side] == 0:
            return (a0, a1, c0, c1)
        return (c0, c1, a0, a1)

    def pt(self, a, c):
        """World point at a along the side and at cross coordinate c."""
        return (a, c) if AXIS[self.side] == 0 else (c, a)


class Plan:
    def __init__(self, hostel, k, name, rect, sides):
        self.hostel, self.k, self.name = hostel, k, name
        self.rect = rect
        X0, X1, Z0, Z1 = rect
        self.inner = (X0 + HW, X1 - HW, Z0 + HW, Z1 - HW)
        self.sides = sides
        self.bands = []
        self.corridors = []       # corridor rectangles
        self.core = None
        self.core_rooms = []      # dict(rect, kind, door=(A, B, s))
        self.core_walls = []      # (A, B, [(segment index, ...)])
        self.stairs = []
        self.connectors = []      # (side, a) doorways through to the next wing
        self.passages = []        # (side, a)
        self.ring = False

    # Facade coordinate of a side line, and the jali plane behind a slot.
    def facade_line(self, side):
        X0, X1, Z0, Z1 = self.rect
        return {"S": Z0, "N": Z1, "W": X0, "E": X1}[side]


def snap_depth(target, lo, hi, cut_sides, line, sign, prefer=()):
    """A band depth near target whose corridor wall (at line + sign * depth) misses the windows
    of the perpendicular sides; prefers landing on one of their bay boundaries."""
    best, best_cost = target, 1e9
    d = lo
    while d <= hi + 1e-9:
        pos = line + sign * d
        cost = abs(d - target)
        for sd in cut_sides:
            if sd.hits_window(pos):
                cost += 10.0
            if sd.in_slot(pos):
                cost += 10.0
        if prefer and min(abs(pos - c) for c in prefer) < 0.05:
            cost -= 0.3
        if cost < best_cost:
            best, best_cost = d, cost
        d += 0.05
    return round(best, 3)


def segments(band, cuts, fixed, first_min=None, last_max=None):
    """Splits band.lo..band.hi at the side's bay boundaries into rooms. fixed: [(a0, a1, tag)]
    pieces that must stay whole (stairs, slots, passages). first_min / last_max: the first
    partition may not come before first_min, nor the last after last_max (corner rooms)."""
    lo, hi = band.lo, band.hi
    fixed = sorted(fixed)
    pts = {lo, hi}
    for f0, f1, _ in fixed:
        pts.update((max(lo, f0), min(hi, f1)))
    for c in cuts:
        if lo + 1e-3 < c < hi - 1e-3 and not any(f0 + 1e-3 < c < f1 - 1e-3 for f0, f1, _ in fixed):
            pts.add(c)
    if first_min is not None:
        pts = {p for p in pts if p <= lo + 1e-6 or p >= first_min - 1e-6 or any(abs(p - f0) < 1e-6 or abs(p - f1) < 1e-6 for f0, f1, _ in fixed)}
    if last_max is not None:
        pts = {p for p in pts if p >= hi - 1e-6 or p <= last_max + 1e-6 or any(abs(p - f0) < 1e-6 or abs(p - f1) < 1e-6 for f0, f1, _ in fixed)}
    pts = sorted(pts)
    segs = []
    for a0, a1 in zip(pts, pts[1:]):
        if a1 - a0 < 1e-3:
            continue
        tag = next((t for f0, f1, t in fixed if f0 - 1e-6 <= a0 and a1 <= f1 + 1e-6), None)
        s = Seg(band, a0, a1)
        s.fixed = tag
        segs.append(s)
    # Merge slivers into a neighbour that is not fixed.
    changed = True
    while changed:
        changed = False
        for i, s in enumerate(segs):
            if s.fixed is None and s.width < 2.3 and len(segs) > 1:
                cand = [j for j in (i - 1, i + 1) if 0 <= j < len(segs) and segs[j].fixed is None]
                if not cand:
                    continue
                j = min(cand, key=lambda j: segs[j].width)
                a, b_ = sorted((i, j))
                segs[a].a1 = segs[b_].a1
                del segs[b_]
                changed = True
                break
    return segs


def plan_wing(hostel, k, name, rect, sides, shared):
    """shared: {side: [(a0, a1)]} stretches of this wing's walls against another wing."""
    P = Plan(hostel, k, name, rect, sides)
    x0, x1, z0, z1 = P.inner
    W, D = x1 - x0, z1 - z0
    buried = {s: not sides[s].visible for s in "NSEW"}
    P.ring = min(W, D) >= 19.0
    if P.ring:
        dep = {}
        # Sides with no facade at all get no rooms: the corridor runs along the wall.
        for s in "NSEW":
            dep[s] = 0.0 if buried[s] else None
        perp = {"N": ("W", "E"), "S": ("W", "E"), "W": ("N", "S"), "E": ("N", "S")}
        lines = {"N": (z1, -1), "S": (z0, 1), "W": (x0, 1), "E": (x1, -1)}
        for s in "NSEW":
            if dep[s] is None:
                line, sign = lines[s]
                a_sides = [sides[p] for p in perp[s]]
                prefer = [c for sd in a_sides for c in sd.cuts]
                dep[s] = snap_depth(BAND, 4.9, 7.0, a_sides, line, sign, prefer)
        cx0, cx1 = x0 + dep["W"], x1 - dep["E"]
        cz0, cz1 = z0 + dep["S"], z1 - dep["N"]
        P.dep = dep
        P.corr_box = (cx0, cx1, cz0, cz1)       # outer edge of the corridor ring
        # Bands: N and S run the full width, W and E between them.
        spans = {"N": (x0, x1), "S": (x0, x1),
                 "W": (cz0 if dep["S"] > 0 else z0, cz1 if dep["N"] > 0 else z1),
                 "E": (cz0 if dep["S"] > 0 else z0, cz1 if dep["N"] > 0 else z1)}
        for s in "NSEW":
            if dep[s] > 0:
                P.bands.append(Band(P, s, spans[s][0], spans[s][1], dep[s]))
        # Corridor ring and core.
        P.corridors.append((cx0, cx1, cz1 - CW, cz1))
        P.corridors.append((cx0, cx1, cz0, cz0 + CW))
        P.corridors.append((cx0, cx0 + CW, cz0 + CW, cz1 - CW))
        P.corridors.append((cx1 - CW, cx1, cz0 + CW, cz1 - CW))
        core = (cx0 + CW, cx1 - CW, cz0 + CW, cz1 - CW)
        if min(core[1] - core[0], core[3] - core[2]) >= 3.0:
            P.core = core
        else:
            P.corridors = [(cx0, cx1, cz0, cz1)]
    else:
        long_x = W >= D
        ends = ("W", "E") if long_x else ("S", "N")
        lo_c, hi_c = (z0, z1) if long_x else (x0, x1)
        mid = (lo_c + hi_c) / 2
        best, best_cost = (mid - 1.0, mid + 1.0), 1e9
        c = mid - 1.5
        while c <= mid + 1.5 + 1e-9:
            for w in (2.0, 2.2, 2.4, 1.8, 2.6):
                a, b_ = c - w / 2, c + w / 2
                cost = abs(c - mid) * 0.5 + abs(w - 2.2) * 0.2
                for e in ends:
                    for pos in (a, b_):
                        if sides[e].hits_window(pos, 0.1):
                            cost += 10.0
                if cost < best_cost:
                    best, best_cost = (a, b_), cost
            c += 0.05
        a, b_ = best
        P.corr_line = best
        if long_x:
            P.bands.append(Band(P, "S", x0, x1, a - z0))
            P.bands.append(Band(P, "N", x0, x1, z1 - b_))
            P.corridors.append((x0, x1, a, b_))
        else:
            P.bands.append(Band(P, "W", z0, z1, a - x0))
            P.bands.append(Band(P, "E", z0, z1, x1 - b_))
            P.corridors.append((a, b_, z0, z1))
        P.long_x = long_x
    return P


def choose_stairs(P):
    """Stairs behind one or two jali slots: the one beside the entrance first."""
    cand = []
    for band in P.bands:
        sd = P.sides[band.side]
        lo, hi = band.lo, band.hi
        if P.ring:
            perp_lo = {"N": "W", "S": "W", "W": "S", "E": "S"}[band.side]
            perp_hi = {"N": "E", "S": "E", "W": "N", "E": "N"}[band.side]
            lo += P.dep[perp_lo] + 1.3 if band.side in "NS" else 0.3
            hi -= P.dep[perp_hi] + 1.3 if band.side in "NS" else 0.3
        for s0, s1, c in sd.slots:
            if s0 >= lo + 0.3 and s1 <= hi - 0.3 and band.depth >= 4.75:
                near_door = sd.door_bay is not None and (abs(sd.door_bay[0] - s1) < 0.05 or abs(sd.door_bay[1] - s0) < 0.05)
                cand.append((0 if near_door else 1, band, (s0, s1, c)))
    if not cand:
        return []
    cand.sort(key=lambda t: t[0])
    x0, x1, z0, z1 = P.inner
    area = (x1 - x0) * (z1 - z0)
    chosen = [cand[0]]
    if area > 800 and len(cand) > 1:
        def pos(t):
            return t[1].pt(t[2][2], t[1].outer)
        p0 = pos(cand[0])
        far = max(cand[1:], key=lambda t: math.dist(pos(t), p0))
        if math.dist(pos(far), p0) > 15:
            chosen.append(far)
    return [(band, slot) for _, band, slot in chosen]


class HStair(I.Stair):
    """A pavilion-style dog-leg stair set behind a hostel jali slot: u runs from the floor
    landing towards the jali, v along the wall."""

    def __init__(self, band, slot, facade):
        self.tag = band.side
        n_in = band.n
        U = (-n_in[0], -n_in[1])
        V = (1.0, 0.0) if AXIS[band.side] == 0 else (0.0, 1.0)
        c = slot[2]
        jali = facade + (n_in[1] if AXIS[band.side] == 0 else n_in[0]) * (JALI + 0.04)
        a0 = c - ST_W / 2
        ux, uz = (a0, jali) if AXIS[band.side] == 0 else (jali, a0)
        self.o = (ux - U[0] * ST_L, uz - U[1] * ST_L)
        self.U, self.V = U, V
        self.uz = U[1]
        self.vx = V[0]


def finish_plan(P, connectors):
    """Cuts every band into rooms, fixes stairs, slots, passages and the lobby, then the core."""
    stair_slots = {id(b): [] for b in P.bands}
    for band, slot in choose_stairs(P):
        stair_slots[id(band)].append(slot)
        st = HStair(band, slot, P.facade_line(band.side))
        st.band = band
        P.stairs.append(st)
    for band in P.bands:
        sd = P.sides[band.side]
        fixed = []
        for s0, s1, c in sd.slots:
            if s0 > band.lo - 0.05 and s1 < band.hi + 0.05:
                tag = "stair" if any(abs(c - t[2]) < 1e-6 for t in stair_slots[id(band)]) else "slot"
                fixed.append((s0, s1, tag))
        for side, a in connectors:
            if side == band.side:
                fixed.append((a - 1.1, a + 1.1, "passage"))
        if sd.door_bay is not None and band.lo - 0.05 < sd.door_bay[0] and sd.door_bay[1] < band.hi + 0.05:
            fixed.append((sd.door_bay[0], sd.door_bay[1], "lobby"))
        first_min = last_max = None
        if P.ring and band.side in "NS":
            first_min = band.lo + P.dep["W"] + 1.3 if P.dep["W"] > 0 else None
            last_max = band.hi - P.dep["E"] - 1.3 if P.dep["E"] > 0 else None
        # Fixed pieces inside the corner rooms are dropped (the corner room keeps the slot).
        if first_min is not None:
            fixed = [f for f in fixed if f[0] >= first_min - 1e-6 or f[2] == "stair"]
        if last_max is not None:
            fixed = [f for f in fixed if f[1] <= last_max + 1e-6 or f[2] == "stair"]
        band.segs = segments(band, sd.cuts, fixed, first_min, last_max)
        for s in band.segs:
            s.window = sd.has_window(s.a0, s.a1)
            if s.fixed == "stair":
                s.kind = "stair"
                s.stair = next(st for st in P.stairs if st.band is band and s.a0 - 0.05 < st.P(0, ST_W / 2)[AXIS[band.side]] < s.a1 + 0.05)
            elif s.fixed == "slot":
                s.kind = "slot"
            elif s.fixed == "passage":
                s.kind = "passage"
            elif s.fixed == "lobby":
                s.lobby = True
        if P.ring and band.side in "NS":
            if P.dep["W"] > 0:
                band.segs[0].corner = True
            if P.dep["E"] > 0:
                band.segs[-1].corner = True
            # Corner rooms also see the windows of the side round the corner.
            for s in (band.segs[0], band.segs[-1]):
                if s.corner:
                    other = P.sides["W" if s is band.segs[0] else "E"]
                    c0, c1 = sorted((band.outer, band.inner))
                    s.window = s.window or other.has_window(c0, c1)
    # Core: split along its long side into rooms of about 6.5 m.
    if P.core:
        cx0, cx1, cz0, cz1 = P.core
        long_x = cx1 - cx0 >= cz1 - cz0
        L = (cx1 - cx0) if long_x else (cz1 - cz0)
        n = max(1, round(L / 6.5))
        kinds = {1: ["washroom"], 2: ["washroom", "bath"], 3: ["washroom", "common", "bath"],
                 4: ["washroom", "pantry", "common", "bath"]}.get(n)
        if kinds is None:
            base = ["washroom", "common", "bath", "pantry", "laundry", "washroom", "bath", "store"]
            kinds = (base * 3)[:n]
        P.core_long_x = long_x
        for i in range(n):
            a0 = (cx0 if long_x else cz0) + L * i / n
            a1 = (cx0 if long_x else cz0) + L * (i + 1) / n
            r = (a0, a1, cz0, cz1) if long_x else (cx0, cx1, a0, a1)
            P.core_rooms.append(dict(rect=r, kind=kinds[i]))


# ------------------------------------------------------------------ hostel model
class Hostel:
    def __init__(self, model_name, label, parts):
        self.model_name, self.label, self.parts = model_name, label, parts
        self.m = Model(model_name + "_Interior", *B.centre(parts[0]))
        self.rects = []
        for nm in parts:
            bd = BUILDINGS[nm]
            self.rects.append((bd["x"] - bd["width"] / 2, bd["x"] + bd["width"] / 2,
                               bd["z"] - bd["depth"] / 2, bd["z"] + bd["depth"] / 2))
        self.sides = [facade_sides(self.m, self.rects, k) for k in range(len(parts))]
        for k, sides in enumerate(self.sides):
            blind_cuts(self.rects[k], sides)
        self.links = self.find_links()
        self.plans = []
        for k in range(len(parts)):
            P = plan_wing(self, k, label_of(label, k, len(parts)), self.rects[k], self.sides[k], {})
            self.plans.append(P)
        # Connector positions: where a narrow wing's corridor meets the wall, its centre;
        # otherwise the middle of the shared wall, kept clear of corner rooms.
        self.connect = []
        for (i, si, j, sj, lo, hi) in self.links:
            a = (lo + hi) / 2
            for idx, sd in ((i, si), (j, sj)):
                Pk = self.plans[idx]
                r_lo, r_hi = connector_range(Pk, sd)
                lo, hi = max(lo, r_lo), min(hi, r_hi)
            for idx, sd in ((i, si), (j, sj)):
                Pk = self.plans[idx]
                if not Pk.ring and sd in (("W", "E") if Pk.long_x else ("S", "N")):
                    a = sum(Pk.corr_line) / 2
            if lo > hi:
                continue      # the shared wall only meets corner rooms: no way through
            a = min(max(a, lo), hi)
            self.connect.append((i, si, j, sj, a))
        for k, P in enumerate(self.plans):
            conns = []
            for (i, si, j, sj, a) in self.connect:
                for idx, sd in ((i, si), (j, sj)):
                    if idx == k:
                        P.connectors.append((sd, a))
                        if any(b.side == sd for b in P.bands):
                            conns.append((sd, a))
            finish_plan(P, conns)
        self.assign_rooms()

    def find_links(self):
        """Pairs of wings whose walls touch: (i, side_i, j, side_j, a0, a1)."""
        out = []
        R = self.rects
        for i in range(len(R)):
            for j in range(i + 1, len(R)):
                a, b_ = R[i], R[j]
                for si, sj, ci, cj, ax in (("N", "S", a[3], b_[2], 0), ("S", "N", a[2], b_[3], 0),
                                           ("E", "W", a[1], b_[0], 1), ("W", "E", a[0], b_[1], 1)):
                    if abs(ci - cj) > 1e-3:
                        continue
                    lo = max(a[0], b_[0]) if ax == 0 else max(a[2], b_[2])
                    hi = min(a[1], b_[1]) if ax == 0 else min(a[3], b_[3])
                    if hi - lo > 3.0:
                        out.append((i, si, j, sj, lo + HW, hi - HW))
        return out

    # -------------------------------------------------------------- room kinds per floor
    def assign_rooms(self):
        """kinds[(wing, band index, segment index)][level] and core room kinds."""
        self.kinds = {}
        for k, P in enumerate(self.plans):
            corner_i = 0
            for bi, band in enumerate(P.bands):
                for si, s in enumerate(band.segs):
                    ks = []
                    for lv in range(TOP + 1):
                        if s.kind in ("stair", "slot", "passage"):
                            ks.append(s.kind)
                        elif s.lobby and lv == 0:
                            ks.append("lobby")
                        elif not s.window:
                            ks.append(["store", "laundry", "washroom", "pantry", "bath"][si % 5] if P.core else
                                      ["washroom", "bath", "laundry", "store", "pantry"][si % 5])
                        elif s.corner or s.width > 4.6:
                            if lv == 0:
                                ks.append(["common", "games", "reading", "double"][corner_i % 4])
                            elif s.width > 7.6:
                                ks.append(["study", "common", "games", "study"][(corner_i + lv) % 4])
                            else:
                                ks.append(["double", "study", "double", "common"][(corner_i + lv) % 4])
                        else:
                            ks.append("single")
                    if s.corner or s.width > 4.6:
                        corner_i += 1
                    self.kinds[(k, bi, si)] = ks
            # The ground-floor rooms either side of the lobby: the warden's office and security.
            for bi, band in enumerate(P.bands):
                for si, s in enumerate(band.segs):
                    if s.lobby:
                        for sj, kind in ((si + 1, "warden"), (si - 1, "warden")):
                            if 0 <= sj < len(band.segs) and self.kinds[(k, bi, sj)][0] == "single":
                                self.kinds[(k, bi, sj)][0] = kind
                                break
            # Narrow wings without a core: washrooms and baths in the middle of the band away
            # from the stairs, if nothing windowless already serves.
            if not P.core:
                for lv in range(TOP + 1):
                    have = [key for key, ks in self.kinds.items() if key[0] == k and ks[lv] in ("washroom", "bath")]
                    if have:
                        continue
                    stair_bands = {id(st.band) for st in P.stairs}
                    bands = sorted(range(len(P.bands)), key=lambda bi: id(P.bands[bi]) in stair_bands)
                    bi = bands[0]
                    band = P.bands[bi]
                    mid = (band.lo + band.hi) / 2
                    order = sorted(range(len(band.segs)), key=lambda si: abs((band.segs[si].a0 + band.segs[si].a1) / 2 - mid))
                    picked = [si for si in order if self.kinds[(k, bi, si)][lv] == "single"][:3]
                    for si, kind in zip(sorted(picked), ("washroom", "bath", "pantry")):
                        self.kinds[(k, bi, si)][lv] = kind
        # Room numbers: wing letter, storey, then position.
        for k, P in enumerate(self.plans):
            n = 0
            for bi, band in enumerate(P.bands):
                for si, s in enumerate(band.segs):
                    n += 1
                    s.number = n


def blind_cuts(rect, sides):
    """Bay boundaries every ~3.2 m along the stretches of a wall buried in the next wing, so the
    rooms there (windowless: washrooms, stores) are room-sized too."""
    X0, X1, Z0, Z1 = rect
    for s, sd in sides.items():
        lo, hi = (X0, X1) if AXIS[s] == 0 else (Z0, Z1)
        gaps, a = [], lo
        for v0, v1 in sorted(sd.visible):
            if v0 - a > 1.0:
                gaps.append((a, v0))
            a = max(a, v1)
        if hi - a > 1.0:
            gaps.append((a, hi))
        cuts = set(sd.cuts)
        for g0, g1 in gaps:
            n = max(1, round((g1 - g0) / 3.2))
            cuts.update(g0 + (g1 - g0) * i / n for i in range(n + 1))
        sd.cuts = sorted(cuts)


def connector_range(P, side):
    """Where along a wall a doorway to the next wing may go, so that it opens on a corridor."""
    x0, x1, z0, z1 = P.inner
    if P.ring:
        cx0, cx1, cz0, cz1 = P.corr_box
        if side in "NS":
            return (cx0 + 2.4, cx1 - 2.4) if P.dep[side] > 0 else (cx0 + 1.0, cx1 - 1.0)
        return (cz0 + 1.2, cz1 - 1.2) if P.dep[side] > 0 else (cz0 + 1.0, cz1 - 1.0)
    lo, hi = (x0, x1) if AXIS[side] == 0 else (z0, z1)
    return (lo + 1.2, hi - 1.2)


def label_of(label, k, n):
    return label if n == 1 else "%s %s" % (label, "ABCDEFGH"[k])


# ------------------------------------------------------------------ shell geometry
def side_line(P, side):
    """Interior face of a side: A, B (world, B further along the axis), inward normal."""
    x0, x1, z0, z1 = P.inner
    if side == "S":
        return (x0, z0), (x1, z0), INWARD[side]
    if side == "N":
        return (x0, z1), (x1, z1), INWARD[side]
    if side == "W":
        return (x0, z0), (x0, z1), INWARD[side]
    return (x1, z0), (x1, z1), INWARD[side]


def recess_holes(P):
    """Floor and ceiling cut-outs where a jali slot's recess reaches past the interior face."""
    out = []
    X0, X1, Z0, Z1 = P.rect
    for s, sd in P.sides.items():
        for a0, a1, _ in sd.slots:
            if s == "S":
                out.append((a0, a1, Z0, Z0 + JALI))
            elif s == "N":
                out.append((a0, a1, Z1 - JALI, Z1))
            elif s == "W":
                out.append((X0, X0 + JALI, a0, a1))
            else:
                out.append((X1 - JALI, X1, a0, a1))
    return out


def outer_walls(m, H, P, lv):
    f, c = level_floor(lv), level_ceil(lv)
    zf = lv * FLOOR
    for s in "NSEW":
        sd = P.sides[s]
        A, B_, n = side_line(P, s)
        ax = AXIS[s]
        base = A[ax]
        ops = []
        wz0, wz1 = zf + B.HOSTEL_WIN_Z[0], zf + B.HOSTEL_WIN_Z[1]
        for w0, w1 in sd.windows:
            ops.append((w0 - base, w1 - base, wz0, wz1, False))
        if sd.door is not None:
            if lv == 0:
                ops.append((sd.door[0] - base, sd.door[1] - base, f, B.HOSTEL_DOOR_TOP, True))
            else:
                ops.append((sd.door_window[0] - base, sd.door_window[1] - base, wz0, wz1, False))
        for a0, a1, _ in sd.slots:
            ops.append((a0 - base, a1 - base, f - 0.01, c + 0.01, True))
        for side, a in P.connectors:
            if side == s:
                ops.append((a - 0.8 - base, a + 0.8 - base, f, f + DOOR_H + 0.1, True))
        wall_with_windows(m, A, B_, n, ops, 0.18, HW, f, c)
        # Jali screens seen from inside, with the slot's sides.
        fac = P.facade_line(s)
        for a0, a1, _ in sd.slots:
            sgn = n[1] if ax == 0 else n[0]
            jl = fac + sgn * (JALI + 0.02)      # just inside the facade's own screen
            face = fac + sgn * HW
            Aj = (a0, jl) if ax == 0 else (jl, a0)
            Bj = (a1, jl) if ax == 0 else (jl, a1)
            ln = Line(m, Aj, Bj, n)
            ln.rect(0, a1 - a0, f, c, 0.0, "Jali")
            for a, fac_dir in ((a0, -1), (a1, 1)):
                pa = (a, face) if ax == 0 else (face, a)
                pb = (a, jl) if ax == 0 else (jl, a)
                t = (1.0, 0.0) if ax == 0 else (0.0, 1.0)
                lnj = Line(m, pa, pb, (t[0] * fac_dir, t[1] * fac_dir))
                lnj.rect(0, JALI + 0.02 - HW, f, c, 0.0, "Wall_Paint")


def connector_gaps(m, H, lv):
    """The doorway through the double wall where two wings meet: jambs, head and floor."""
    f = level_floor(lv)
    for (i, si, j, sj, a) in H.connect:
        P = H.plans[i]
        fac = P.facade_line(si)
        ax = AXIS[si]
        for b0, b1, z in ((a - 0.8, a + 0.8, None),):
            if ax == 0:
                hrect(m, b0, b1, fac - HW, fac + HW, f, "Floor_Tile")
                hrect(m, b0, b1, fac - HW, fac + HW, f + DOOR_H + 0.1, "Wall_Paint", up=False)
                for x, d in ((b0, 1), (b1, -1)):
                    Line(m, (x, fac - HW), (x, fac + HW), (d, 0.0)).rect(0, 2 * HW, f, f + DOOR_H + 0.1, 0.0, "Wall_Paint")
            else:
                hrect(m, fac - HW, fac + HW, b0, b1, f, "Floor_Tile")
                hrect(m, fac - HW, fac + HW, b0, b1, f + DOOR_H + 0.1, "Wall_Paint", up=False)
                for z_, d in ((b0, 1), (b1, -1)):
                    Line(m, (fac - HW, z_), (fac + HW, z_), (0.0, d)).rect(0, 2 * HW, f, f + DOOR_H + 0.1, 0.0, "Wall_Paint")


DOOR_W = 0.9
OPEN_KINDS = ("stair", "slot", "passage", "lobby")
TILED = ("washroom", "bath", "pantry", "laundry")


def seg_door(s, si):
    """Where a room's door sits on its corridor wall (axis coordinate): near one end, the ends
    alternating so pairs of doors face each other; corner rooms open onto the corridor."""
    if s.width < 2.6:
        return (s.a0 + s.a1) / 2
    if s.corner:
        first = s is s.band.segs[0]
        return s.a1 - 0.25 - DOOR_W / 2 if first else s.a0 + 0.25 + DOOR_W / 2
    if si % 2 == 0:
        return s.a1 - 0.25 - DOOR_W / 2
    return s.a0 + 0.25 + DOOR_W / 2


def band_walls(m, H, P, lv, doors_out):
    f, c = level_floor(lv), level_ceil(lv)
    k = H.plans.index(P)
    for bi, band in enumerate(P.bands):
        ax = AXIS[band.side]
        # Corridor wall along the band.
        A, B_ = band.pt(band.lo, band.inner), band.pt(band.hi, band.inner)
        doors = []
        for si, s in enumerate(band.segs):
            kind = H.kinds[(k, bi, si)][lv]
            if not corridor_touch(P, band, s):
                continue
            if kind in OPEN_KINDS:
                w = max(1.4, s.width - 0.5)
                doors.append(((s.a0 + s.a1) / 2 - band.lo, w))
            else:
                a = seg_door(s, si)
                doors.append((a - band.lo, DOOR_W))
                t = (1.0, 0.0) if ax == 0 else (0.0, 1.0)
                hinge = 1.0 if (s.a1 - a) < (a - s.a0) else -1.0
                doors_out.append((band.pt(a, band.inner), (-band.n[0], -band.n[1]), kind, t, hinge, DOOR_W))
        if band.depth > 0:
            partition(m, A, B_, f, c, doors)
        # Partitions between rooms.
        sd = P.sides[band.side]
        fac = P.facade_line(band.side)
        sgn = band.n[1] if ax == 0 else band.n[0]
        for s0, s1 in zip(band.segs, band.segs[1:]):
            a = s0.a1
            start = band.outer
            if sd.in_slot(a):
                start = fac + sgn * (JALI + 0.02)
            partition(m, band.pt(a, start), band.pt(a, band.inner), f, c)
        # Tile skins in washrooms and baths (walls other than the outer one).
        for si, s in enumerate(band.segs):
            kind = H.kinds[(k, bi, si)][lv]
            if kind in TILED:
                tile_skin(m, s.rect(), f, P.inner, skip_side=band.side, band=band, seg=s, si=si)


def corridor_touch(P, band, s):
    """True if the segment's corridor-side wall borders a corridor (corner rooms only in part)."""
    if not P.ring or band.side in "WE":
        return True
    cx0, cx1, cz0, cz1 = P.corr_box
    return overlap(s.a0, s.a1, cx0, cx1) >= DOOR_W + 0.3


def tile_skin(m, rect, f, inner, skip_side=None, band=None, seg=None, si=0):
    """White wall tiles to 2.1 m on the inside of a wet room's walls (not the outer wall)."""
    X0, X1, Z0, Z1 = rect
    ix0, ix1, iz0, iz1 = inner
    t = PART / 2 + 0.006

    def tt(v, face):
        return 0.006 if abs(v - face) < 1e-3 else t

    top = f + 2.1
    walls = {"S": ((X0, Z0 + tt(Z0, iz0)), (X1, Z0 + tt(Z0, iz0)), (0.0, 1.0)),
             "N": ((X1, Z1 - tt(Z1, iz1)), (X0, Z1 - tt(Z1, iz1)), (0.0, -1.0)),
             "W": ((X0 + tt(X0, ix0), Z1), (X0 + tt(X0, ix0), Z0), (1.0, 0.0)),
             "E": ((X1 - tt(X1, ix1), Z0), (X1 - tt(X1, ix1), Z1), (-1.0, 0.0))}
    opp = {"N": "S", "S": "N", "W": "E", "E": "W"}
    for s, (A, B_, n) in walls.items():
        if s == skip_side:
            continue
        ln = Line(m, A, B_, n)
        holes = []
        if band is not None and s == opp[band.side]:
            a = seg_door(seg, si) if seg.kind not in OPEN_KINDS else None
            if a is not None:
                ax = AXIS[band.side]
                s_mid = abs(a - A[ax])
                holes.append((s_mid - DOOR_W / 2, s_mid + DOOR_W / 2, f, f + DOOR_H))
        ln.wall(0.0, ln.L, f, top, 0.0, "Wall_Tile", holes)
        ln.hband(0.0, ln.L, top, 0.0, -0.006, "Wall_Tile", up=True)



def core_walls(m, H, P, lv, doors_out):
    if not P.core:
        return
    f, c = level_floor(lv), level_ceil(lv)
    cx0, cx1, cz0, cz1 = P.core
    rooms = P.core_rooms
    long_x = P.core_long_x
    # Doors on the long side facing the entrance-side corridor... both long sides get one each
    # alternately so the rooms open to both corridors.
    sideA = ((cx0, cz0), (cx1, cz0), (0.0, -1.0)) if long_x else ((cx0, cz0), (cx0, cz1), (-1.0, 0.0))
    sideB = ((cx0, cz1), (cx1, cz1), (0.0, 1.0)) if long_x else ((cx1, cz0), (cx1, cz1), (1.0, 0.0))
    dA, dB = [], []
    for i, r in enumerate(rooms):
        X0, X1, Z0, Z1 = r["rect"]
        a0, a1 = (X0, X1) if long_x else (Z0, Z1)
        a = (a0 + a1) / 2
        w = 1.6 if r["kind"] in ("common", "pantry") else 1.0
        (dA if i % 2 == 0 else dB).append(a - (cx0 if long_x else cz0))
        r["door_side"] = "A" if i % 2 == 0 else "B"
        r["door_w"] = w
        n = sideA[2] if i % 2 == 0 else sideB[2]
        pt = ((a, cz0) if i % 2 == 0 else (a, cz1)) if long_x else ((cx0, a) if i % 2 == 0 else (cx1, a))
        doors_out.append((pt, (-n[0], -n[1]), r["kind"], (1.0, 0.0) if long_x else (0.0, 1.0), -1.0, w))
    wA = [(s, rooms[2 * i]["door_w"]) for i, s in enumerate(dA)]
    wB = [(s, rooms[2 * i + 1]["door_w"]) for i, s in enumerate(dB)]
    partition(m, sideA[0], sideA[1], f, c, wA)
    partition(m, sideB[0], sideB[1], f, c, wB)
    if long_x:
        partition(m, (cx0, cz0), (cx0, cz1), f, c)
        partition(m, (cx1, cz0), (cx1, cz1), f, c)
    else:
        partition(m, (cx0, cz0), (cx1, cz0), f, c)
        partition(m, (cx0, cz1), (cx1, cz1), f, c)
    for r in rooms[:-1]:
        X0, X1, Z0, Z1 = r["rect"]
        if long_x:
            partition(m, (X1, cz0), (X1, cz1), f, c)
        else:
            partition(m, (cx0, Z1), (cx1, Z1), f, c)
    for i, r in enumerate(rooms):
        if r["kind"] in TILED:
            X0, X1, Z0, Z1 = r["rect"]
            t = PART / 2 + 0.006
            top = f + 2.1
            walls = [((X0, Z0 + t), (X1, Z0 + t), (0.0, 1.0)), ((X1, Z1 - t), (X0, Z1 - t), (0.0, -1.0)),
                     ((X0 + t, Z1), (X0 + t, Z0), (1.0, 0.0)), ((X1 - t, Z0), (X1 - t, Z1), (-1.0, 0.0))]
            for A, B_, n in walls:
                ln = Line(m, A, B_, n)
                holes = []
                # The door wall: the long side the room opens on.
                on_A = r["door_side"] == "A"
                if long_x and abs(A[1] - (cz0 + t if on_A else cz1 - t)) < 1e-6 or \
                        (not long_x and abs(A[0] - (cx0 + t if on_A else cx1 - t)) < 1e-6):
                    a = ((X0 + X1) / 2) if long_x else ((Z0 + Z1) / 2)
                    ax = 0 if long_x else 1
                    s_mid = abs(a - A[ax])
                    holes.append((s_mid - r["door_w"] / 2, s_mid + r["door_w"] / 2, f, f + DOOR_H))
                ln.wall(0.0, ln.L, f, top, 0.0, "Wall_Tile", holes)
                ln.hband(0.0, ln.L, top, 0.0, -0.006, "Wall_Tile", up=True)


def hostel_shell(H):
    m = H.m
    H.doors = {lv: [] for lv in range(TOP + 1)}
    for P in H.plans:
        rec = recess_holes(P)
        st_holes = [st.hole() for st in P.stairs]
        for X0, X1, Z0, Z1 in rec:          # the bottom of each jali recess, at plinth level
            hrect(m, X0, X1, Z0, Z1, 0.5, "Concrete")
        for lv in range(TOP + 1):
            f, c = level_floor(lv), level_ceil(lv)
            hrect_cut(m, *P.inner, f, "Floor_Tile", rec + (st_holes if lv > 0 else []))
            hrect_cut(m, *P.inner, c, "Ceiling", rec + (st_holes if lv < TOP else []), up=False)
            outer_walls(m, H, P, lv)
            band_walls(m, H, P, lv, H.doors[lv])
            core_walls(m, H, P, lv, H.doors[lv])
        if P.stairs:
            I.stair_geometry(m, P.stairs, TOP, level_floor, level_ceil, side_walls=False)
    for lv in range(TOP + 1):
        connector_gaps(m, H, lv)
    m.local_bounds_to_solid()
    return m


# ------------------------------------------------------------------ furniture models
# Prop space as in build_interiors: origin on the floor, front towards +Z; wall-mounted items
# have their back on the wall at z = 0.
@prop("Hostel_Bed", solid=True)
def p_hostel_bed():
    """Single cot, head at z = 0, foot at z = 2.0: wooden frame, checked sheet, pillow, folded
    blanket, and the steel trunk every hostel bed has underneath."""
    m = new("Hostel_Bed")
    for x in (-0.475, 0.425):
        for z in (0.0, 1.95):
            bx(m, x, x + 0.05, z, z + 0.05, 0.0, 0.4, "Wood")
    bx(m, -0.475, 0.475, 0.0, 2.0, 0.3, 0.4, "Wood")
    bx(m, -0.475, 0.475, 0.0, 0.05, 0.4, 0.88, "Wood")
    bx(m, -0.42, 0.42, 0.05, 0.065, 0.55, 0.82, "Wood")
    bx(m, -0.475, 0.475, 1.95, 2.0, 0.4, 0.58, "Wood")
    bx(m, -0.45, 0.45, 0.06, 1.94, 0.4, 0.56, "Bedsheet")
    bx(m, -0.46, 0.46, 0.4, 1.94, 0.33, 0.48, "Bedsheet")          # the sheet hanging over the sides
    bx(m, -0.3, 0.3, 0.12, 0.5, 0.56, 0.67, "Linen")
    bx(m, -0.44, 0.44, 1.42, 1.86, 0.56, 0.63, "Fabric")
    bx(m, -0.44, 0.44, 1.42, 1.86, 0.63, 0.66, "Fabric")
    bx(m, -0.34, 0.34, 0.65, 1.15, 0.0, 0.26, "Metal")              # trunk under the bed
    bx(m, -0.35, 0.35, 0.64, 1.16, 0.22, 0.24, "Steel")
    bx(m, -0.05, 0.05, 1.15, 1.17, 0.12, 0.2, "Steel")
    return m


@prop("Study_Table", solid=True)
def p_study_table():
    """Study table against a wall (back at z = 0), 1.2 m long, with a drawer, a shelf of books
    on the wall above, a table lamp, a laptop and a few books."""
    m = new("Study_Table")
    bx(m, -0.6, 0.6, 0.0, 0.6, 0.72, 0.75, "Laminate")
    for x in (-0.6, 0.57):
        bx(m, x, x + 0.03, 0.02, 0.6, 0.0, 0.72, "Wood")
    bx(m, -0.57, 0.57, 0.0, 0.02, 0.3, 0.72, "Wood")
    bx(m, 0.15, 0.57, 0.05, 0.58, 0.56, 0.72, "Wood")
    bx(m, 0.32, 0.4, 0.58, 0.6, 0.63, 0.65, "Steel")
    # Wall shelf with books.
    for y in (1.3, 1.72):
        bx(m, -0.6, 0.6, 0.0, 0.24, y, y + 0.025, "Wood")
    for x in (-0.6, 0.575):
        bx(m, x, x + 0.025, 0.0, 0.24, 1.3, 1.745, "Wood")
    rnd = random.Random(11)
    x = -0.57
    while x < 0.5:
        w = rnd.uniform(0.025, 0.055)
        if rnd.random() < 0.12:
            x += 0.08
            continue
        bx(m, x, x + w, 0.02, rnd.uniform(0.15, 0.22), 1.325, 1.325 + rnd.uniform(0.18, 0.3), "Books")
        x += w + 0.003
    # Table lamp.
    cyl(m, -0.45, 0.15, 0.07, 0.75, 0.77, "Plastic", seg=10)
    obox(m, -0.45, 0.2, 0.02, 0.02, 0.0, 0.77, 1.12, "Steel")
    obox(m, -0.45, 0.3, 0.02, 0.22, math.pi / 2, 1.1, 1.12, "Steel")
    cyl(m, -0.45, 0.4, 0.07, 1.0, 1.1, "Plastic", seg=10)
    bx(m, -0.49, -0.41, 0.36, 0.44, 0.99, 1.0, "LightPanel")
    # Laptop, open, and a stack of books.
    bx(m, -0.12, 0.22, 0.28, 0.52, 0.75, 0.77, "Plastic")
    obox(m, 0.05, 0.27, 0.34, 0.015, 0.0, 0.77, 1.0, "Plastic")
    bx(m, -0.105, 0.205, 0.285, 0.29, 0.79, 0.98, "Screen")
    for i, (x0, y0) in enumerate(((0.28, 0.75), (0.29, 0.79), (0.27, 0.82))):
        bx(m, x0, x0 + 0.22, 0.12, 0.42, y0, y0 + 0.04, "Books")
    return m


@prop("Almirah", solid=True)
def p_almirah():
    """Steel almirah (the classic two-door cupboard), back at z = 0: 0.9 x 0.5 x 1.9 m."""
    m = new("Almirah")
    bx(m, -0.45, 0.45, 0.0, 0.5, 0.05, 1.9, "Steel")
    bx(m, -0.43, 0.43, 0.02, 0.48, 0.0, 0.05, "Metal")
    bx(m, -0.005, 0.005, 0.5, 0.505, 0.1, 1.85, "Metal")             # door seam
    for x in (-0.06, 0.04):
        bx(m, x, x + 0.02, 0.5, 0.53, 0.95, 1.15, "Metal")            # handles
    bx(m, 0.1, 0.16, 0.5, 0.51, 1.2, 1.26, "Steel")                   # lock plate
    for y in (0.4, 1.6):
        bx(m, -0.44, 0.44, 0.5, 0.503, y, y + 0.01, "Metal")
    bx(m, -0.3, 0.3, 0.05, 0.45, 1.9, 2.15, "Cardboard")             # suitcase kept on top
    return m


@prop("Curtain")
def p_curtain():
    """Curtains drawn back either side of a 2.1 m window, on a rod 2.35 m above the floor."""
    m = new("Curtain")
    bx(m, -1.3, 1.3, 0.07, 0.09, 2.33, 2.36, "Steel")
    for sx in (-1, 1):
        for i in range(5):
            x0 = sx * (1.28 - i * 0.08)
            z0 = 0.04 if i % 2 == 0 else 0.09
            bx(m, min(x0, x0 - sx * 0.085), max(x0, x0 - sx * 0.085), z0, z0 + 0.03, 0.35, 2.33, "Fabric")
    return m


@prop("Shower_Cubicle")
def p_shower_cubicle():
    """1.0 m shower stall, back to the wall at z = -1.2, open front at z = 0: tiled partitions,
    a curtain rail, a shower rose, a geyser above, and a bucket and mug."""
    m = new("Shower_Cubicle")
    for x in (-0.5, 0.485):
        bx(m, x, x + 0.015, -1.2, 0.0, 0.0, 2.0, "Wall_Tile")
    bx(m, -0.485, 0.485, -1.2, -0.02, 0.0, 0.04, "Ceramic")
    bx(m, -0.485, 0.485, -0.03, 0.0, 1.95, 1.98, "Steel")
    for i in range(4):
        x0 = 0.25 + i * 0.06
        bx(m, x0, x0 + 0.05, -0.06 if i % 2 else -0.02, (-0.06 if i % 2 else -0.02) + 0.02, 0.3, 1.95, "Fabric")
    bx(m, -0.02, 0.02, -1.2, -1.17, 0.9, 1.9, "Steel")
    bx(m, -0.02, 0.02, -1.2, -0.95, 1.88, 1.92, "Steel")
    cyl(m, 0.0, -0.95, 0.08, 1.84, 1.88, "Steel", seg=10)
    bx(m, -0.1, 0.1, -1.2, -1.14, 0.95, 1.0, "Steel")                 # taps
    cyl(m, 0.0, -1.0, 0.22, 2.15, 2.65, "Ceramic", seg=12)            # geyser
    cyl(m, 0.25, -0.9, 0.16, 0.04, 0.4, "Plastic", seg=10)            # bucket
    cyl(m, 0.25, -0.9, 0.165, 0.38, 0.41, "Plastic", seg=10)
    cyl(m, 0.05, -0.7, 0.06, 0.04, 0.15, "Plastic", seg=8)            # mug
    return m


@prop("TT_Table", solid=True)
def p_tt_table():
    """Table tennis table, 2.74 x 1.525 m, net across the middle (along x)."""
    m = new("TT_Table")
    bx(m, -1.37, 1.37, -0.7625, 0.7625, 0.735, 0.76, "Carpet")
    for x0, x1, z0, z1 in ((-1.37, 1.37, -0.7625, -0.7425), (-1.37, 1.37, 0.7425, 0.7625),
                           (-1.37, -1.35, -0.7625, 0.7625), (1.35, 1.37, -0.7625, 0.7625), (-1.37, 1.37, -0.0015, 0.0015)):
        bx(m, x0, x1, z0, z1, 0.76, 0.762, "Ceramic")
    for x in (-1.1, 1.05):
        for z in (-0.6, 0.55):
            bx(m, x, x + 0.05, z, z + 0.05, 0.0, 0.735, "Steel")
    bx(m, -0.01, 0.01, -0.84, 0.84, 0.76, 0.912, "Linen")
    for z in (-0.86, 0.84):
        bx(m, -0.015, 0.015, z, z + 0.02, 0.73, 0.92, "Steel")
    cyl(m, 0.6, 0.3, 0.02, 0.76, 0.8, "Ceramic", seg=8)
    return m


@prop("Carrom_Table", solid=True)
def p_carrom_table():
    """Carrom board on a stand: 0.9 m square, pockets in the corners, a few coins."""
    m = new("Carrom_Table")
    for x in (-0.3, 0.26):
        for z in (-0.3, 0.26):
            bx(m, x, x + 0.04, z, z + 0.04, 0.0, 0.62, "Wood")
    bx(m, -0.45, 0.45, -0.45, 0.45, 0.62, 0.66, "Laminate")
    for x0, x1, z0, z1 in ((-0.45, 0.45, -0.45, -0.39), (-0.45, 0.45, 0.39, 0.45), (-0.45, -0.39, -0.39, 0.39), (0.39, 0.45, -0.39, 0.39)):
        bx(m, x0, x1, z0, z1, 0.66, 0.69, "Wood")
    for x in (-0.36, 0.36):
        for z in (-0.36, 0.36):
            cyl(m, x, z, 0.025, 0.659, 0.661, "Plastic", seg=8)
    rnd = random.Random(5)
    for i in range(9):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0.0, 0.25)
        cyl(m, r * math.cos(a), r * math.sin(a), 0.015, 0.66, 0.67, "Wood" if i % 2 else "Plastic", seg=6)
    return m


@prop("Pantry_Counter", solid=True)
def p_pantry_counter():
    """2.4 m kitchenette against a wall: cabinets, a granite top with a sink, a kettle and an
    induction plate, a microwave, wall cupboards above."""
    m = new("Pantry_Counter")
    bx(m, -1.2, 1.2, 0.0, 0.58, 0.1, 0.86, "Laminate")
    bx(m, -1.18, 1.18, 0.02, 0.55, 0.0, 0.1, "Plastic")
    bx(m, -1.22, 1.22, 0.0, 0.62, 0.86, 0.9, "Stone")
    for x in (-0.6, 0.0, 0.6):
        bx(m, x - 0.005, x + 0.005, 0.58, 0.585, 0.12, 0.84, "Plastic")
    bx(m, -0.95, -0.45, 0.12, 0.5, 0.88, 0.901, "Steel")              # sink
    cyl(m, -0.7, 0.07, 0.015, 0.9, 1.15, "Steel", seg=6)
    bx(m, -0.715, -0.685, 0.07, 0.22, 1.12, 1.15, "Steel")
    cyl(m, 0.1, 0.3, 0.08, 0.9, 1.12, "Steel", seg=10)                 # kettle
    bx(m, 0.35, 0.75, 0.12, 0.5, 0.9, 0.92, "Plastic")                # induction plate
    bx(m, 0.62, 1.15, 0.05, 0.45, 0.9, 1.2, "Plastic")                # microwave
    bx(m, 0.65, 1.0, 0.45, 0.452, 0.94, 1.16, "Screen")
    bx(m, -1.2, 1.2, 0.0, 0.35, 1.55, 2.25, "Laminate")               # wall cupboards
    for x in (-0.6, 0.0, 0.6):
        bx(m, x - 0.005, x + 0.005, 0.35, 0.355, 1.57, 2.23, "Plastic")
    bx(m, -1.2, 1.2, 0.0, 0.01, 0.9, 1.55, "Wall_Tile")
    return m


@prop("Fridge", solid=True)
def p_fridge():
    m = new("Fridge")
    bx(m, -0.3, 0.3, 0.02, 0.65, 0.0, 1.7, "Ceramic")
    bx(m, -0.3, 0.3, 0.65, 0.66, 1.2, 1.21, "Plastic")
    bx(m, 0.2, 0.23, 0.66, 0.69, 1.25, 1.55, "Steel")
    bx(m, 0.2, 0.23, 0.66, 0.69, 0.6, 1.1, "Steel")
    return m


@prop("Washing_Machine", solid=True)
def p_washing_machine():
    """Top-loading washing machine against a wall, 0.6 x 0.62 x 0.95 m."""
    m = new("Washing_Machine")
    bx(m, -0.3, 0.3, 0.02, 0.64, 0.05, 0.9, "Ceramic")
    bx(m, -0.28, 0.28, 0.04, 0.62, 0.0, 0.05, "Plastic")
    bx(m, -0.3, 0.3, 0.02, 0.16, 0.9, 1.05, "Ceramic")
    bx(m, -0.25, 0.25, 0.16, 0.161, 0.93, 1.02, "Screen")
    bx(m, -0.26, 0.26, 0.2, 0.6, 0.9, 0.915, "Glass_Clear")
    cyl(m, 0.0, 0.4, 0.2, 0.86, 0.9, "Steel", seg=14)
    return m


@prop("Drying_Rack")
def p_drying_rack():
    """Folding clothes horse with washing hung on it, 1.6 m long."""
    m = new("Drying_Rack")
    for x in (-0.78, 0.76):
        obox(m, x + 0.01, -0.15, 0.02, 0.7, 1.35, 0.0, 1.2, "Steel")
        obox(m, x + 0.01, 0.15, 0.02, 0.7, -1.35, 0.0, 1.2, "Steel")
    for z in (-0.25, -0.12, 0.0, 0.12, 0.25):
        y = 1.2 - abs(z) * 1.2
        bx(m, -0.78, 0.78, z - 0.008, z + 0.008, y - 0.016, y, "Steel")
    rnd = random.Random(9)
    for i, mat in enumerate(("Bedsheet", "Fabric", "Linen", "Red_Paint", "Fabric", "Linen")):
        x0 = -0.72 + i * 0.25
        w = rnd.uniform(0.15, 0.22)
        z = (-0.12, 0.0, 0.12)[i % 3]
        y = 1.2 - abs(z) * 1.2
        bx(m, x0, x0 + w, z - 0.006, z + 0.006, y - rnd.uniform(0.4, 0.7), y, mat)
    return m


@prop("Pendant_Lamp")
def p_pendant_lamp():
    """Hangs 2.4 m from a high ceiling (origin at the ceiling): rod and a metal shade."""
    m = new("Pendant_Lamp")
    cyl(m, 0, 0, 0.1, -0.05, 0.0, "Metal", seg=10)
    cyl(m, 0, 0, 0.01, -2.4, -0.05, "Metal", seg=6)
    cyl(m, 0, 0, 0.12, -2.45, -2.4, "Metal", seg=12)
    cyl(m, 0, 0, 0.26, -2.7, -2.45, "Metal", top=None, seg=14)
    bx(m, -0.17, 0.17, -0.17, 0.17, -2.7, -2.69, "LightPanel")
    return m


# ------------------------------------------------------------------ furnishing
class Room:
    """What a furnishing function needs: the frame (front = the window wall for rooms in a
    band, the door wall for core rooms), where the door is, and the windows."""

    def __init__(self, name, kind, rect, front, door_u, door_wall, windows_u, rng):
        self.name, self.kind, self.rect = name, kind, rect
        self.fr = Frame(rect, front)
        self.door_u, self.door_wall = door_u, door_wall
        self.windows_u = windows_u
        self.rng = rng

    @property
    def hw(self):
        return self.fr.width / 2

    @property
    def D(self):
        return self.fr.depth


def room_light(R, intensity=0.95, per=26.0):
    X0, X1, Z0, Z1 = R.rect
    if (X1 - X0) * (Z1 - Z0) > 1.6 * per:
        I.room_lights(R.rect, y=CEIL - 0.3, per=per, intensity=intensity)
        return
    LIGHTS.append(dict(x=round((X0 + X1) / 2, 2), y=round(CEIL - 0.3, 2), z=round((Z0 + Z1) / 2, 2),
                       range=round(max(3.8, 0.8 * max(X1 - X0, Z1 - Z0)), 2), intensity=intensity, color=WARM))


def fans(R, step=3.6):
    fr = R.fr
    nu = max(1, round(fr.width / step))
    nv = max(1, round(fr.depth / step))
    for i in range(nu):
        for j in range(nv):
            fr.put("Ceiling_Fan", -fr.width / 2 + fr.width * (i + 0.5) / nu, fr.depth * (j + 0.5) / nv, CEIL)


def curtains(R):
    for u in R.windows_u:
        R.fr.wall_item("Curtain", "front", u)


def side_of(sign):
    return "right" if sign > 0 else "left"


def f_single(R):
    fr, hw, D = R.fr, R.hw, R.D
    ds = 1.0 if R.door_u >= 0 else -1.0
    bs = -ds
    fr.put("Hostel_Bed", bs * (hw - PART / 2 - 0.5), 0.1, FL, "in")
    fr.wall_item("Study_Table", side_of(ds), 0.85)
    fr.put("Chair_Cafe", ds * (hw - PART / 2 - 0.85), 0.85, FL, side_of(ds))
    if D > 4.4:
        fr.wall_item("Almirah", side_of(ds), D - 1.62)
    pick = R.rng.random()
    if D > 4.6 and pick < 0.55:
        fr.wall_item("Bookshelf", side_of(bs), 2.85)
    elif D > 4.6 and pick < 0.85:
        fr.wall_item("Notice_Board", side_of(bs), 2.9)
    if R.rng.random() < 0.6:
        fr.put("Dustbin", ds * (hw - 0.3), 1.75, FL)
    curtains(R)
    fr.put("Ceiling_Fan", 0.0, min(D / 2, 2.6), CEIL)
    fr.put("Tube_Light", bs * (hw - 0.3), D * 0.6, CEIL, "right")
    room_light(R, 0.8)


def f_double(R):
    fr, hw, D = R.fr, R.hw, R.D
    ds = 1.0 if R.door_u >= 0 else -1.0
    for sg in (-1.0, 1.0):
        fr.put("Hostel_Bed", sg * (hw - PART / 2 - 0.5), 0.1, FL, "in")
        fr.wall_item("Study_Table", side_of(sg), 2.9)
        fr.put("Chair_Cafe", sg * (hw - PART / 2 - 0.85), 2.9, FL, side_of(sg))
    if fr.width > 4.0:
        fr.wall_item("Almirah", "back", -ds * (hw - 0.65))
        fr.wall_item("Almirah", "back", -ds * (hw - 1.62))
    if fr.width > 6.5:
        fr.put("Table_Round", 0.0, D * 0.5, FL)
        for a in (0.0, math.pi):
            fr.put("Chair_Cafe", 0.85 * math.cos(a), D * 0.5 + 0.85 * math.sin(a), FL,
                   "left" if a == 0.0 else "right")
        fr.wall_item("Bookshelf", "back", ds * (hw - 2.0) if abs(ds * (hw - 2.0) - R.door_u) > 1.2 else 0.0)
    curtains(R)
    fans(R, 4.0)
    for sg in (-1.0, 1.0):
        fr.put("Tube_Light", sg * (hw - 0.3), D * 0.6, CEIL, "right")
    room_light(R, 0.85)


def f_common(R):
    fr, hw, D = R.fr, R.hw, R.D
    ds = 1.0 if R.door_u >= 0 else -1.0
    fr.wall_item("TV_Wall", side_of(-ds), D * 0.45)
    u = -ds * (hw - 3.0)
    fr.put("Sofa_3", u + ds * 0.0, D * 0.45, FL, side_of(-ds))
    fr.put("Table_Coffee", -ds * (hw - 1.9), D * 0.45, FL, "in", yaw_extra=90)
    fr.put("Sofa_1", -ds * (hw - 1.9), D * 0.45 - 1.2, FL, "in")
    fr.put("Sofa_1", -ds * (hw - 1.9), D * 0.45 + 1.2, FL, "front")
    if fr.width > 6.0:
        fr.put("Carrom_Table", ds * (hw - 2.0), D * 0.35, FL)
        for du, dv, face in ((-0.75, 0, "right"), (0.75, 0, "left")):
            fr.put("Chair_Cafe", ds * (hw - 2.0) + du, D * 0.35 + dv, FL, face)
    fr.wall_item("Notice_Board", "back", -ds * (hw - 1.8))
    fr.put("Water_Cooler", ds * (hw - 0.45), D - 1.6, FL, side_of(-ds))
    fr.put("Plant_Pot", -ds * (hw - 0.45), 0.5, FL)
    fr.wall_item("Wall_Clock", "back", 0.0)
    curtains(R)
    fans(R)
    I.ceiling_grid(R.rect, 3.2, "Tube_Light", y=CEIL)
    room_light(R, 1.0)


def f_games(R):
    fr, hw, D = R.fr, R.hw, R.D
    if fr.width >= 5.2 and D >= 4.2:
        fr.put("TT_Table", 0.0, D * 0.5, FL, "in", yaw_extra=0 if fr.width >= D else 90)
    for u in (-hw + 0.9, hw - 0.9):
        if fr.width > 7.5:
            fr.put("Carrom_Table", u, 1.0, FL)
    fr.put("Bench", 0.0, D - 0.45, FL, "front")
    fr.wall_item("Notice_Board", "left", D * 0.5)
    curtains(R)
    I.ceiling_grid(R.rect, 3.0, "Tube_Light", y=CEIL)
    room_light(R, 1.0)


def f_reading(R):
    fr, hw, D = R.fr, R.hw, R.D
    ds = 1.0 if R.door_u >= 0 else -1.0
    v = 1.0
    while v < D - 2.2:
        fr.wall_item("Study_Table", side_of(-ds), v + 0.6)
        fr.put("Chair_Cafe", -ds * (hw - PART / 2 - 0.85), v + 0.6, FL, side_of(-ds))
        v += 1.4
    if fr.width > 5.0:
        fr.put("Table_Meeting", ds * 0.4, D * 0.5, FL, "in", yaw_extra=90 if D > fr.width else 0)
    k = 0
    u = -hw + 0.7
    while u < hw - 0.7:
        if abs(u - R.door_u) > 1.1:
            fr.wall_item("Bookshelf", "back", u)
        u += 1.05
        k += 1
    curtains(R)
    fans(R)
    I.ceiling_grid(R.rect, 2.8, "Tube_Light", y=CEIL)
    room_light(R, 1.0)


def f_warden(R):
    fr, hw, D = R.fr, R.hw, R.D
    ds = 1.0 if R.door_u >= 0 else -1.0
    fr.put("Table_Faculty", 0.0, 1.5, FL, "in")
    fr.put("Chair_Office", 0.0, 0.8, FL, "in")
    for u in (-0.45, 0.45):
        fr.put("Chair_Visitor", u, 2.4, FL, "front")
    fr.wall_item("Filing_Cabinet", side_of(-ds), 0.6)
    fr.wall_item("Almirah", side_of(-ds), 2.0)
    if D > 4.6:
        fr.wall_item("Bookshelf", side_of(-ds), 3.1)
    fr.wall_item("Notice_Board", side_of(ds), 2.6)
    curtains(R)
    fr.put("Ceiling_Fan", 0.0, D * 0.45, CEIL)
    fr.put("Tube_Light", 0.0, D - 1.0, CEIL)
    room_light(R, 1.0)


def f_lobby(R):
    fr, hw, D = R.fr, R.hw, R.D
    # The entrance is in the front (facade) wall; the back opens onto the corridor.
    fr.put("Table_Faculty", -hw + 1.0, D * 0.45, FL, "right")
    fr.put("Chair_Office", -hw + 0.32, D * 0.45, FL, "right")
    fr.wall_item("Notice_Board", "right", D * 0.35)
    fr.put("Plant_Pot", hw - 0.4, 0.55, FL)
    fr.put("Plant_Pot", -hw + 0.4, 0.55, FL)
    fr.wall_item("Wall_Clock", "right", D * 0.6)
    fr.wall_item("Fire_Extinguisher", "right", D - 0.9)
    fr.put("Ceiling_Fan", 0.0, D * 0.5, CEIL)
    I.ceiling_grid(R.rect, 2.6, "Light_Panel", y=CEIL)
    room_light(R, 1.2)


def wet_rows(R):
    """v range for fittings along the side walls, leaving the doorway clear."""
    v_lo = 0.15 + (1.2 if R.door_wall == "front" else 0.0)
    v_hi = R.D - 0.15 - (1.2 if R.door_wall == "back" else 0.0)
    return v_lo, v_hi


def f_washroom(R, showers=False):
    fr, hw, D = R.fr, R.hw, R.D
    v_lo, v_hi = wet_rows(R)
    sc = -1.0 if R.door_u >= -0.3 else 1.0      # fittings on the side away from the door
    n = int((v_hi - v_lo) / 1.0)
    for i in range(n):
        v = v_lo + 0.5 + i * 1.0
        fr.put("Toilet_Cubicle", sc * (hw - PART / 2 - 1.5), v, FL, side_of(-sc))
    if showers and fr.width >= 3.6:
        for i in range(int((v_hi - v_lo) / 1.0)):
            v = v_lo + 0.5 + i * 1.0
            fr.put("Shower_Cubicle", -sc * (hw - PART / 2 - 1.2), v, FL, side_of(sc))
    elif fr.width >= 2.9:
        L = v_hi - v_lo
        k = max(1, int((L - 0.6) / 2.9))
        for i in range(k):
            fr.wall_item("Basin_Counter", side_of(-sc), v_lo + 1.4 + i * 2.9 if L > 2.9 else (v_lo + v_hi) / 2)
    if fr.width >= 5.4 and R.door_wall == "front":
        u = -hw + 2.2
        while u < hw - 2.2:
            fr.put("Toilet_Cubicle", u, D - PART / 2 - 1.5, FL, "front")
            u += 1.0
    fr.put("Dustbin", -sc * (hw - 0.35), D * 0.5 if R.door_wall == "front" else 0.4, FL)
    I.ceiling_grid(R.rect, 2.4, "Light_Panel", y=CEIL, margin=0.8)
    room_light(R, 0.9)


def f_bath(R):
    fr, hw, D = R.fr, R.hw, R.D
    v_lo, v_hi = wet_rows(R)
    sides = (-1.0, 1.0) if fr.width >= 3.4 else ((-1.0 if R.door_u >= 0 else 1.0),)
    for sg in sides:
        for i in range(int((v_hi - v_lo) / 1.0)):
            fr.put("Shower_Cubicle", sg * (hw - PART / 2 - 1.2), v_lo + 0.5 + i * 1.0, FL, side_of(-sg))
    if len(sides) == 1:
        fr.put("Bench", -sides[0] * (hw - 0.45), (v_lo + v_hi) / 2, FL, side_of(sides[0]), yaw_extra=0)
    I.ceiling_grid(R.rect, 2.4, "Light_Panel", y=CEIL, margin=0.8)
    room_light(R, 0.9)


def f_pantry(R):
    fr, hw, D = R.fr, R.hw, R.D
    v_lo, v_hi = wet_rows(R)
    sc = -1.0 if R.door_u >= -0.3 else 1.0
    fr.wall_item("Pantry_Counter", side_of(sc), v_lo + 1.3)
    if v_hi - v_lo > 3.4:
        fr.wall_item("Fridge", side_of(sc), v_lo + 2.95)
    fr.put("Water_Cooler", -sc * (hw - 0.45), v_lo + 0.4, FL, side_of(sc))
    if fr.width >= 3.4 and v_hi - v_lo > 3.6:
        v = (v_lo + v_hi) / 2 + 0.6
        fr.put("Table_Round", -sc * 0.3, v, FL)
        for a in (0.0, math.pi / 2, math.pi, 1.5 * math.pi):
            X, Z = fr.P(-sc * 0.3 + 0.9 * math.cos(a), v + 0.9 * math.sin(a))
            c = fr.P(-sc * 0.3, v)
            put("Chair_Cafe", X, Z, FL, c[0] - X, c[1] - Z)
    fr.put("Dustbin", sc * (hw - 0.35), v_hi - 0.3, FL)
    I.ceiling_grid(R.rect, 2.6, "Tube_Light", y=CEIL, margin=0.8)
    room_light(R, 0.9)


def f_laundry(R):
    fr, hw, D = R.fr, R.hw, R.D
    v_lo, v_hi = wet_rows(R)
    sc = -1.0 if R.door_u >= -0.3 else 1.0
    v = v_lo + 0.4
    while v < v_hi - 0.35:
        fr.wall_item("Washing_Machine", side_of(sc), v)
        v += 0.72
    if fr.width >= 2.9:
        fr.put("Drying_Rack", -sc * (hw - 1.0), (v_lo + v_hi) / 2, FL, "right", yaw_extra=90)
    I.ceiling_grid(R.rect, 2.6, "Tube_Light", y=CEIL, margin=0.8)
    room_light(R, 0.8)


def f_store(R):
    fr, hw, D = R.fr, R.hw, R.D
    v_lo, v_hi = wet_rows(R)
    for sg in ((-1.0, 1.0) if fr.width >= 2.9 else (-1.0,)):
        v = v_lo + 0.95
        while v < v_hi - 0.9:
            fr.put("Storage_Rack", sg * (hw - PART / 2 - 0.3), v, FL, side_of(-sg))
            v += 1.9
    fr.put("Tube_Light", 0.0, D / 2, CEIL)
    room_light(R, 0.6)


def f_sitout(R, lv):
    fr, hw, D = R.fr, R.hw, R.D
    # The jali screen is the front wall; seats look out through it.
    fr.put("Bench", 0.0, 1.0, FL, "front")
    fr.put("Plant_Pot", -hw + 0.4, 0.3, FL)
    fr.put("Plant_Pot", hw - 0.4, 0.3, FL)
    if lv % 2 == 0:
        fr.put("Water_Cooler", hw - 0.45, D - 1.4, FL, "left")
    else:
        fr.put("Table_Cafe", 0.0, 2.4, FL)
        for du in (-0.65, 0.65):
            fr.put("Chair_Cafe", du, 2.4, FL, side_of(-du))
    fr.put("Tube_Light", 0.0, D * 0.5, CEIL)
    room_light(R, 0.7)


def f_passage(R):
    R.fr.put("Tube_Light", 0.0, R.D * 0.5, CEIL)
    room_light(R, 0.6)


def f_stair(R):
    pass


FURNISH = {"single": f_single, "double": f_double, "common": f_common, "games": f_games,
           "reading": f_reading, "study": f_reading, "warden": f_warden, "lobby": f_lobby,
           "washroom": f_washroom, "bath": f_bath, "pantry": f_pantry, "laundry": f_laundry,
           "store": f_store, "passage": f_passage, "stair": f_stair}

NAMES = {"single": "Room", "double": "Double Room", "common": "Common Room", "games": "Games Room",
         "reading": "Reading Room", "study": "Study Room", "warden": "Warden's Office", "lobby": "Entrance Lobby",
         "washroom": "Washroom", "bath": "Bathing Area", "pantry": "Pantry", "laundry": "Laundry",
         "store": "Store", "passage": "Passage", "stair": "Stair", "slot": "Sit-out"}


def furnish_hostel(H, zone):
    rooms_out = []
    for lv in range(TOP + 1):
        n0, l0 = len(PLACED), len(LIGHTS)
        for k, P in enumerate(H.plans):
            for bi, band in enumerate(P.bands):
                sd = P.sides[band.side]
                for si, s in enumerate(band.segs):
                    kind = H.kinds[(k, bi, si)][lv]
                    fr = Frame(s.rect(), band.side)
                    if kind in OPEN_KINDS:
                        door_u = 0.0
                    else:
                        a = seg_door(s, si)
                        X, Z = band.pt(a, band.inner)
                        door_u = (X - fr.o[0]) * fr.r[0] + (Z - fr.o[1]) * fr.r[1]
                    wins = []
                    for w0, w1 in sd.windows:
                        if overlap(w0, w1, s.a0, s.a1) > 0.5:
                            c = (w0 + w1) / 2
                            X, Z = band.pt(c, band.outer)
                            wins.append((X - fr.o[0]) * fr.r[0] + (Z - fr.o[1]) * fr.r[1])
                    name = "%s %s %d%02d" % (P.name, NAMES.get(kind, kind), lv, s.number)
                    R = Room(name, kind, s.rect(), band.side, door_u, "back", wins, random.Random(name))
                    if kind == "slot":
                        f_sitout(R, lv)
                    else:
                        FURNISH[kind](R)
                    rooms_out.append(dict(name=name, kind=kind, rect=[round(v, 2) for v in s.rect()], level=lv, zone=zone))
            for i, r in enumerate(P.core_rooms):
                X0, X1, Z0, Z1 = r["rect"]
                if P.core_long_x:
                    front = "S" if r.get("door_side", "A") == "A" else "N"
                else:
                    front = "W" if r.get("door_side", "A") == "A" else "E"
                name = "%s %s %d%02d" % (P.name, NAMES[r["kind"]], lv, 50 + i)
                R = Room(name, r["kind"], r["rect"], front, 0.0, "front", [], random.Random(name))
                if r["kind"] == "washroom":
                    f_washroom(R, showers=len(P.core_rooms) == 1)
                else:
                    FURNISH[r["kind"]](R)
                rooms_out.append(dict(name=name, kind=r["kind"], rect=[round(v, 2) for v in r["rect"]], level=lv, zone=zone))
            corridors(P)
            for st in P.stairs:
                lx, lz = st.P(0.75, 1.5)
                put("Tube_Light", lx, lz, CEIL)
                LIGHTS.append(dict(x=round(lx, 2), y=CEIL - 0.3, z=round(lz, 2), range=5.5, intensity=0.9, color=WARM))
                if lv < TOP:
                    mx, mz = st.P((I.ST_MID + ST_L) / 2, 1.5)
                    LIGHTS.append(dict(x=round(mx, 2), y=round(FL + FLOOR / 2 + 2.0, 2), z=round(mz, 2), range=5.0,
                                       intensity=0.8, color=WARM))
        door_leaves(H.doors[lv])
        if lv == 0:
            entrance_doors(H)
        dy = lv * FLOOR
        for p_ in PLACED[n0:]:
            p_["y"] = round(p_["y"] + dy, 3)
            p_["level"] = lv
            p_["zone"] = zone
        for l_ in LIGHTS[l0:]:
            l_["y"] = round(l_["y"] + dy, 2)
            l_["level"] = lv
            l_["zone"] = zone
    return rooms_out


def corridors(P):
    for X0, X1, Z0, Z1 in P.corridors:
        w, d = X1 - X0, Z1 - Z0
        along_x = w >= d
        length = max(w, d)
        n = max(1, int(length / 3.2))
        for i in range(n):
            t = (i + 0.5) / n
            X = X0 + w * t if along_x else (X0 + X1) / 2
            Z = (Z0 + Z1) / 2 if along_x else Z0 + d * t
            put("Tube_Light", X, Z, CEIL, *((1.0, 0.0) if along_x else (0.0, 1.0)))
        nl = max(1, round(length / 8.0))
        for i in range(nl):
            t = (i + 0.5) / nl
            LIGHTS.append(dict(x=round(X0 + w * t if along_x else (X0 + X1) / 2, 2), y=CEIL - 0.3,
                               z=round((Z0 + Z1) / 2 if along_x else Z0 + d * t, 2), range=6.5, intensity=0.9, color=WARM))
    if P.core:
        cx0, cx1, cz0, cz1 = P.core
        # Fire extinguishers and dustbins on the core walls, mid-way along each side.
        for X, Z, dx, dz in (((cx0 + cx1) / 2 + 1.5, cz0 - PART / 2 - 0.01, 0, -1), ((cx0 + cx1) / 2 - 1.5, cz1 + PART / 2 + 0.01, 0, 1),
                             (cx0 - PART / 2 - 0.01, (cz0 + cz1) / 2 + 1.5, -1, 0), (cx1 + PART / 2 + 0.01, (cz0 + cz1) / 2 - 1.5, 1, 0)):
            put("Fire_Extinguisher", X, Z, FL, dx, dz)
            put("Dustbin", X + dx * 0.25 + dz * 0.0 - (0.6 if dz else 0.0), Z + dz * 0.25 - (0.6 if dx else 0.0), FL)


def door_leaves(doors):
    for pt, room, kind, t, hinge, w in doors:
        leaves = [hinge] if w < 1.4 else [-1.0, 1.0]
        for hs in leaves:
            s_h = hs * (w / 2 - 0.03)
            hx = pt[0] + t[0] * s_h + room[0] * (PART / 2)
            hz = pt[1] + t[1] * s_h + room[1] * (PART / 2)
            yaw = math.degrees(math.atan2(-room[1], room[0]))
            put("Door_Leaf", hx, hz, FL, yaw=round(yaw, 2))


def entrance_doors(H):
    """Glass door leaves standing open in each wing's entrance."""
    for P in H.plans:
        for s, sd in P.sides.items():
            if sd.door is None:
                continue
            A, B_, n = side_line(P, s)
            ax = AXIS[s]
            for a in (sd.door[0] + 0.06, sd.door[1] - 0.06):
                X, Z = (a, A[1]) if ax == 0 else (A[0], a)
                put("Door_Glass", X, Z, FL, yaw=round(math.degrees(math.atan2(-n[1], n[0])), 2))


# ------------------------------------------------------------------ Annapurna Mess
MESS_HALL = BUILDINGS["Annapurna Mess"]
MESS_KITCHEN = BUILDINGS["Annapurna Mess - Kitchen"]
MW = B.WALL["outer"]          # the civic facade's wall thickness (0.45)


def brect(bd):
    return (bd["x"] - bd["width"] / 2, bd["x"] + bd["width"] / 2, bd["z"] - bd["depth"] / 2, bd["z"] + bd["depth"] / 2)


HALL_R = brect(MESS_HALL)
KIT_R = brect(MESS_KITCHEN)
# Hatches and a doorway through the wall between the hall and the kitchen: (side of the hall,
# centre, width, sill above the floor, head above the floor).
MESS_HATCHES = [("S", -27.6, 2.6, 0.95, 2.0), ("S", -24.0, 2.6, 0.95, 2.0), ("S", -20.4, 2.6, 0.95, 2.0),
                ("E", -114.5, 2.6, 0.95, 2.0), ("E", -109.6, 1.6, 0.0, DOOR_H)]


def civic_openings(m, rects, k):
    """Per side of part k: [(a0, a1, z0, z1, is_door)] as academic_facade draws them for a
    two-storey civic building, in world coordinates along the side."""
    X0, X1, Z0, Z1 = rects[k]
    others = rects[:k] + rects[k + 1:]
    out = {s: [] for s in "NSEW"}
    poly = B.rect_poly(m, X0, X1, Z0, Z1)
    for run in B.outline_runs(m, poly):
        for (s0, s1) in B.hidden_intervals(m, run, others):
            sub = B.SubRun(run, s0, s1)
            L = sub.L

            def W(s):
                p = sub.P(s, 0, 0)
                return (m.px - p[0], m.pz - p[1])

            A, Bw = W(0.0), W(L)
            if abs(A[1] - Z0) < 1e-3 and abs(Bw[1] - Z0) < 1e-3:
                side = "S"
            elif abs(A[1] - Z1) < 1e-3 and abs(Bw[1] - Z1) < 1e-3:
                side = "N"
            elif abs(A[0] - X0) < 1e-3:
                side = "W"
            else:
                side = "E"
            ax = AXIS[side]

            def span(sa, sb):
                va, vb = W(sa)[ax], W(sb)[ax]
                return min(va, vb), max(va, vb)

            bays, bw = B.facade_bays(L)
            doors = [B.bay_at(sub, X, Z) for X, Z in B.MESS_DOORS if B.run_has(sub, X, Z)]
            for i in range(bays):
                b0, b1 = i * bw, (i + 1) * bw
                for f in range(2):
                    zf = f * FLOOR
                    if f == 0 and i in doors:
                        out[side].append((*span(b0 + 0.5, b1 - 0.5), FL, B.DOOR_TOP, True))
                        continue
                    wz0, wz1 = zf + (0.75 if f else 0.95), zf + FLOOR - 0.4
                    mid = (b0 + b1) / 2
                    out[side].append((*span(b0 + 0.42, mid - 0.14), wz0, wz1, False))
                    out[side].append((*span(mid + 0.14, b1 - 0.42), wz0, wz1, False))
    return out


def mess_wall(m, A, B_, n, ops_world, ax):
    """An interior wall face from A to B (B further along the axis) with openings given in
    world axis coordinates."""
    base = A[ax]
    ops = [(a0 - base, a1 - base, z0, z1, d) for a0, a1, z0, z1, d in ops_world]
    wall_with_windows(m, A, B_, n, ops, 0.35, MW, FL, HIGH_CEIL)


def mess_shell():
    m = Model("Annapurna_Mess_Interior", *B.centre("Annapurna Mess"))
    rects = [HALL_R, KIT_R]
    hall_ops = civic_openings(m, rects, 0)
    kit_ops = civic_openings(m, rects, 1)
    hx0, hx1, hz0, hz1 = HALL_R[0] + MW, HALL_R[1] - MW, HALL_R[2] + MW, HALL_R[3] - MW
    kx0, kx1, kz0, kz1 = KIT_R[0] + MW, KIT_R[1] - MW, KIT_R[2] + MW, KIT_R[3] - MW
    kn = HALL_R[2] - MW            # the kitchen's face of the wall it shares with the hall (south)
    ke = HALL_R[1] + MW            # ... and of the east stretch
    # Floors and ceilings: the hall, and the kitchen as two rectangles (it wraps round the hall's corner).
    for X0, X1, Z0, Z1, mat in ((hx0, hx1, hz0, hz1, "Floor_Tile"), (kx0, kx1, kz0, kn, "Floor_Tile"), (ke, kx1, kn, kz1, "Floor_Tile")):
        hrect(m, X0, X1, Z0, Z1, FL, mat)
        hrect(m, X0, X1, Z0, Z1, HIGH_CEIL, "Ceiling", up=False)
    hatch_ops = {"S": [], "E": []}
    for side, c, w, sill, head in MESS_HATCHES:
        hatch_ops[side].append((c - w / 2, c + w / 2, FL + sill, FL + head, sill < 0.01))
    # Hall walls.
    mess_wall(m, (hx0, hz0), (hx0, hz1), (1.0, 0.0), hall_ops["W"], 1)
    mess_wall(m, (hx0, hz1), (hx1, hz1), (0.0, -1.0), hall_ops["N"], 0)
    mess_wall(m, (hx1, hz0), (hx1, hz1), (-1.0, 0.0), hall_ops["E"] + hatch_ops["E"], 1)
    mess_wall(m, (hx0, hz0), (hx1, hz0), (0.0, 1.0), hall_ops["S"] + hatch_ops["S"], 0)
    # Kitchen walls.
    mess_wall(m, (kx0, kz0), (kx1, kz0), (0.0, 1.0), kit_ops["S"], 0)
    mess_wall(m, (kx1, kz0), (kx1, kz1), (-1.0, 0.0), kit_ops["E"], 1)
    mess_wall(m, (ke, kz1), (kx1, kz1), (0.0, -1.0), kit_ops["N"], 0)
    mess_wall(m, (kx0, kz0), (kx0, kn), (1.0, 0.0), kit_ops["W"], 1)
    mess_wall(m, (kx0, kn), (ke, kn), (0.0, -1.0), hatch_ops["S"], 0)
    mess_wall(m, (ke, kn), (ke, kz1), (1.0, 0.0), hatch_ops["E"], 1)
    # Reveals of the hatches and the doorway through the shared wall, a steel sill on each hatch.
    for side, c, w, sill, head in MESS_HATCHES:
        z0, z1 = FL + sill, FL + head
        a0, a1 = c - w / 2, c + w / 2
        if side == "S":
            f0, f1 = kn, hz0
            Line(m, (a0, f0), (a0, f1), (1.0, 0.0)).rect(0, f1 - f0, z0, z1, 0.0, "Wall_Paint")
            Line(m, (a1, f0), (a1, f1), (-1.0, 0.0)).rect(0, f1 - f0, z0, z1, 0.0, "Wall_Paint")
            hrect(m, a0, a1, f0, f1, z1, "Wall_Paint", up=False)
            hrect(m, a0, a1, f0, f1, z0, "Steel" if sill > 0 else "Floor_Tile")
        else:
            f0, f1 = hx1, ke
            Line(m, (f0, a0), (f1, a0), (0.0, 1.0)).rect(0, f1 - f0, z0, z1, 0.0, "Wall_Paint")
            Line(m, (f0, a1), (f1, a1), (0.0, -1.0)).rect(0, f1 - f0, z0, z1, 0.0, "Wall_Paint")
            hrect(m, f0, f1, a0, a1, z1, "Wall_Paint", up=False)
            hrect(m, f0, f1, a0, a1, z0, "Steel" if sill > 0 else "Floor_Tile")
    # White tiles to 2.1 m round the kitchen.
    t = 0.006
    for A, B_, n in (((kx0, kz0 + t), (kx1, kz0 + t), (0.0, 1.0)), ((kx1 - t, kz0), (kx1 - t, kz1), (-1.0, 0.0)),
                     ((ke, kz1 - t), (kx1, kz1 - t), (0.0, -1.0)), ((kx0 + t, kz0), (kx0 + t, kn), (1.0, 0.0)),
                     ((kx0, kn - t), (ke, kn - t), (0.0, -1.0)), ((ke + t, kn), (ke + t, kz1), (1.0, 0.0))):
        ln = Line(m, A, B_, n)
        ax = 0 if abs(A[1] - B_[1]) < 1e-6 else 1
        holes = []
        allops = kit_ops["S"] if (ax == 0 and abs(A[1] - kz0) < 0.01) else kit_ops["E"] if (ax == 1 and abs(A[0] - kx1) < 0.01) else \
            kit_ops["N"] if (ax == 0 and abs(A[1] - kz1) < 0.01) else kit_ops["W"] if (ax == 1 and abs(A[0] - kx0) < 0.01) else \
            hatch_ops["S"] if ax == 0 else hatch_ops["E"]
        for a0, a1, z0, z1, d in allops:
            holes.append((a0 - A[ax], a1 - A[ax], z0, z1))
        ln.wall(0.0, ln.L, FL, FL + 2.1, 0.0, "Wall_Tile", holes)
    m.local_bounds_to_solid()
    return m, hall_ops, kit_ops


@prop("Mess_Table", solid=True)
def p_mess_table():
    """Long dining table with a steel top and a bench either side, thalis and tumblers laid."""
    m = new("Mess_Table")
    bx(m, -1.2, 1.2, -0.4, 0.4, 0.72, 0.75, "Steel")
    for x in (-1.1, 1.06):
        bx(m, x, x + 0.04, -0.3, 0.3, 0.0, 0.72, "Metal")
        bx(m, x, x + 0.04, -0.75, 0.75, 0.02, 0.06, "Metal")
    for z in (-0.65, 0.65):
        bx(m, -1.15, 1.15, z - 0.14, z + 0.14, 0.43, 0.46, "Wood")
        for x in (-1.0, 0.96):
            bx(m, x, x + 0.04, z - 0.03, z + 0.03, 0.0, 0.43, "Metal")
    for i in range(4):
        x = -0.9 + i * 0.6
        for z in (-0.22, 0.22):
            cyl(m, x, z, 0.15, 0.75, 0.765, "Steel", seg=14)
            cyl(m, x + 0.2, z + (0.12 if z > 0 else -0.12), 0.035, 0.75, 0.86, "Steel", seg=8)
    return m


@prop("Serving_Counter", solid=True)
def p_serving_counter():
    """Bain-marie serving counter against a wall (back at z = 0), 3 m long: steel wells of rice,
    dal, sabzi and curry under a sneeze guard, a tray slide, and stacks of thalis."""
    m = new("Serving_Counter")
    bx(m, -1.5, 1.5, 0.0, 0.8, 0.0, 0.88, "Steel")
    bx(m, -1.5, 1.5, 0.0, 0.8, 0.88, 0.9, "Steel")
    for i, food in enumerate(("Ceramic", "Cardboard", "Foliage", "Red_Paint")):
        x = -1.2 + i * 0.62
        bx(m, x, x + 0.5, 0.2, 0.62, 0.9, 0.93, "Steel")
        bx(m, x + 0.03, x + 0.47, 0.23, 0.59, 0.925, 0.935, food)
    bx(m, -1.5, 1.5, 0.66, 0.68, 0.9, 1.35, "Steel")
    obox(m, 0.0, 0.62, 3.0, 0.01, 0.0, 1.35, 1.38, "Steel")
    bx(m, -1.5, 1.5, 0.45, 0.7, 1.36, 1.37, "Glass_Clear")
    bx(m, -1.5, 1.5, 0.8, 1.1, 0.84, 0.86, "Steel")                   # tray slide
    for x in (-1.3, 1.3):
        bx(m, x, x + 0.03, 0.8, 1.08, 0.3, 0.84, "Steel")
    for x in (1.1, 1.3):
        cyl(m, x, 0.4, 0.15, 0.93, 1.05, "Steel", seg=14)              # stacked thalis
    return m


@prop("Wash_Trough", solid=True)
def p_wash_trough():
    """Free-standing double-sided hand-wash trough with six taps a side, 3 m long."""
    m = new("Wash_Trough")
    bx(m, -1.5, 1.5, -0.35, 0.35, 0.72, 0.82, "Steel")
    bx(m, -1.45, 1.45, -0.3, 0.3, 0.74, 0.745, "Metal")
    for x in (-1.4, 1.36):
        bx(m, x, x + 0.04, -0.25, 0.25, 0.0, 0.72, "Steel")
    bx(m, -1.5, 1.5, -0.03, 0.03, 0.82, 1.05, "Steel")
    for i in range(6):
        x = -1.25 + i * 0.5
        for z in (-0.04, 0.04):
            sgn = 1 if z > 0 else -1
            bx(m, x - 0.015, x + 0.015, z, z + sgn * 0.12, 1.0, 1.03, "Steel")
    bx(m, -0.1, 0.1, -0.05, 0.05, 1.05, 1.2, "Plastic")              # soap
    return m


@prop("Cooking_Range", solid=True)
def p_cooking_range():
    """Commercial three-burner range against a wall, 2.4 m long, with a big patila, a kadhai
    and a pressure cooker on it."""
    m = new("Cooking_Range")
    bx(m, -1.2, 1.2, 0.0, 0.9, 0.0, 0.8, "Steel")
    bx(m, -1.2, 1.2, 0.0, 0.1, 0.8, 1.2, "Steel")
    for i in range(3):
        x = -0.8 + i * 0.8
        bx(m, x - 0.3, x + 0.3, 0.2, 0.8, 0.8, 0.84, "Metal")
        cyl(m, x, 0.5, 0.12, 0.84, 0.86, "Plastic", seg=10)
        bx(m, x - 0.05, x + 0.05, 0.9, 0.95, 0.5, 0.6, "Plastic")       # knob
    cyl(m, -0.8, 0.5, 0.32, 0.86, 1.3, "Steel", seg=16)
    cyl(m, -0.8, 0.5, 0.33, 1.3, 1.33, "Steel", seg=16)
    cyl(m, 0.0, 0.5, 0.36, 0.86, 1.0, "Metal", seg=16)
    cyl(m, 0.8, 0.5, 0.18, 0.86, 1.15, "Steel", seg=12)
    cyl(m, 0.8, 0.5, 0.05, 1.15, 1.25, "Plastic", seg=8)
    return m


@prop("Exhaust_Hood")
def p_exhaust_hood():
    """Steel canopy hood over a range (back on the wall at z = 0), with its duct to the ceiling
    of the double-height kitchen."""
    m = new("Exhaust_Hood")
    bx(m, -1.35, 1.35, 0.0, 1.05, 2.0, 2.55, "Steel")
    bx(m, -1.3, 1.3, 0.05, 1.0, 1.99, 2.0, "Metal")
    bx(m, -0.35, 0.35, 0.0, 0.55, 2.55, HIGH_CEIL - FL, "Steel")
    return m


@prop("Prep_Table", solid=True)
def p_prep_table():
    """Steel preparation table, 2 x 0.9 m, with an undershelf of vessels, a chopping board,
    vegetables and a stack of trays."""
    m = new("Prep_Table")
    bx(m, -1.0, 1.0, -0.45, 0.45, 0.86, 0.9, "Steel")
    bx(m, -0.95, 0.95, -0.4, 0.4, 0.2, 0.22, "Steel")
    for x in (-0.95, 0.92):
        for z in (-0.4, 0.37):
            bx(m, x, x + 0.03, z, z + 0.03, 0.0, 0.86, "Steel")
    cyl(m, -0.5, 0.0, 0.25, 0.22, 0.55, "Steel", seg=14)
    cyl(m, 0.3, 0.0, 0.2, 0.22, 0.45, "Steel", seg=12)
    bx(m, -0.7, -0.1, -0.3, 0.1, 0.9, 0.93, "Wood")
    rnd = random.Random(21)
    for i in range(10):
        mat = ("Red_Paint", "Foliage", "Cardboard")[i % 3]
        x, z = rnd.uniform(0.1, 0.8), rnd.uniform(-0.3, 0.3)
        cyl(m, x, z, rnd.uniform(0.03, 0.05), 0.9, 0.97, mat, seg=6)
    cyl(m, -0.4, 0.25, 0.04, 0.93, 0.96, "Steel", seg=6)
    return m


@prop("Sink_Unit", solid=True)
def p_sink_unit():
    """Double steel sink with a draining board and a splashback, against a wall, 2 m long."""
    m = new("Sink_Unit")
    bx(m, -1.0, 1.0, 0.0, 0.7, 0.0, 0.82, "Steel")
    bx(m, -1.0, 1.0, 0.0, 0.7, 0.82, 0.9, "Steel")
    for x in (-0.75, -0.15):
        bx(m, x, x + 0.5, 0.12, 0.58, 0.895, 0.901, "Metal")
        cyl(m, x + 0.25, 0.06, 0.02, 0.9, 1.2, "Steel", seg=6)
        bx(m, x + 0.23, x + 0.27, 0.06, 0.25, 1.17, 1.2, "Steel")
    for i in range(6):
        bx(m, 0.45 + i * 0.09, 0.47 + i * 0.09, 0.15, 0.55, 0.9, 1.15, "Steel")     # plates draining
    bx(m, -1.0, 1.0, 0.0, 0.02, 0.9, 1.5, "Steel")
    return m


def furnish_mess(zone, hall_ops):
    n0, l0 = len(PLACED), len(LIGHTS)
    hx0, hx1, hz0, hz1 = HALL_R[0] + MW, HALL_R[1] - MW, HALL_R[2] + MW, HALL_R[3] - MW
    kx0, kx1, kz0, kz1 = KIT_R[0] + MW, KIT_R[1] - MW, KIT_R[2] + MW, KIT_R[3] - MW
    kn, ke = HALL_R[2] - MW, HALL_R[1] + MW
    rooms = [dict(name="Annapurna Mess Dining Hall", kind="mess_hall", rect=[hx0, hx1, hz0, hz1], level=0, zone=zone),
             dict(name="Annapurna Mess Kitchen", kind="kitchen", rect=[kx0, kx1, kz0, kn], level=0, zone=zone),
             dict(name="Annapurna Mess Kitchen (store)", kind="kitchen", rect=[ke, kx1, kn, kz1], level=0, zone=zone)]
    # Serving counters under the hatches, facing the hall; tumbler racks and water coolers.
    for side, c, w, sill, head in MESS_HATCHES:
        if sill < 0.01:
            continue
        if side == "S":
            put("Serving_Counter", c, hz0 + 0.01, FL, 0, 1)
        else:
            put("Serving_Counter", hx1 - 0.01, c, FL, -1, 0)
    put("Water_Cooler", hx1 - 0.4, -103.0, FL, -1, 0)
    put("Water_Cooler", hx1 - 0.4, -101.5, FL, -1, 0)
    put("Cafe_Counter", -31.1, hz0 + 0.45, FL, 0, 1)           # plate return by the kitchen
    put("Notice_Board", hx1 - PART / 2 + 0.07, -111.9, FL, -1, 0)
    put("Wall_Clock", (hx0 + hx1) / 2, hz1 - 0.01, FL, 0, -1)
    # Rows of long tables, with an aisle down the middle and in front of the counters.
    for i in range(6):
        X = hx0 + 4.2 + i * 3.4 + (1.6 if i >= 3 else 0.0)
        if X > hx1 - 4.6:
            continue
        for j in range(7):
            Z = hz0 + 4.6 + j * 2.6
            if Z > hz1 - 1.6:
                continue
            put("Mess_Table", X, Z, FL, 0, 1)
    # Hand-wash troughs by the west door, a dustbin at each end.
    for Z in (-97.0, -111.0):
        put("Wash_Trough", hx0 + 1.6, Z, FL, 1, 0, yaw=90.0)
        put("Dustbin", hx0 + 0.45, Z + 2.0, FL)
    put("Plant_Pot", hx0 + 0.5, MESS_HALL["z"] + 2.2, FL)
    put("Plant_Pot", hx0 + 0.5, MESS_HALL["z"] - 2.2, FL)
    for X in [hx0 + 3.0 + 4.6 * i for i in range(6)]:
        for Z in [hz0 + 3.2 + 4.6 * j for j in range(5)]:
            if X < hx1 - 1.0 and Z < hz1 - 1.0:
                put("Pendant_Lamp", X, Z, HIGH_CEIL)
                if X + 2.3 < hx1 - 1 and Z + 2.3 < hz1 - 1:
                    put("Ceiling_Fan", X + 2.3, Z + 2.3, HIGH_CEIL)
    I.room_lights((hx0, hx1, hz0, hz1), y=3.5, per=40.0, intensity=1.6)
    # Kitchen: ranges under hoods along the south wall, prep tables, sinks, racks, fridges.
    # Cooking line: six ranges under hoods along the south wall.
    for i in range(6):
        X = kx0 + 2.6 + i * 3.3
        put("Cooking_Range", X, kz0 + 0.01, FL, 0, 1)
        put("Exhaust_Hood", X, kz0 + 0.01, FL, 0, 1)
    # Two rows of prep tables down the middle, and a row under the hatches for plating.
    for Z in (kz0 + 4.6, kz0 + 7.4):
        for i in range(5):
            put("Prep_Table", kx0 + 4.0 + i * 3.0, Z, FL, 1, 0, yaw=0.0)
    for side, c, w, sill, head in MESS_HATCHES:
        if side == "S" and sill > 0:
            put("Prep_Table", c, kn - 0.55, FL, 1, 0, yaw=0.0)
    put("Prep_Table", ke + 0.55, -114.5, FL, 1, 0, yaw=90.0)
    # Wash-up along the east wall: three sinks with racks of clean plates between them.
    for Z in (kz0 + 1.6, kz0 + 4.0, kz0 + 6.4):
        put("Sink_Unit", kx1 - 0.01, Z, FL, -1, 0)
    put("Storage_Rack", kx1 - 0.3, kz0 + 10.5, FL, -1, 0)
    put("Storage_Rack", kx1 - 0.3, kz0 + 12.5, FL, -1, 0)
    # Dry store along the west wall and in the kitchen's north-east wing.
    for Z in (kz0 + 1.5, kz0 + 3.5, kz0 + 5.5, kz0 + 7.5, kz0 + 9.5, kz0 + 11.5):
        put("Storage_Rack", kx0 + 0.3, Z, FL, 1, 0)
    for X in (ke + 1.4, ke + 3.4, ke + 5.4, ke + 7.4, ke + 9.4):
        put("Storage_Rack", X, kz1 - 0.3, FL, 0, -1)
        put("Storage_Rack", X, kz1 - 3.2, FL, 0, 1)
    for i in range(3):
        put("Fridge", kx1 - 0.01, kn + 1.0 + i * 0.7, FL, -1, 0)
    put("Prep_Table", ke + 6.0, kn + 2.0, FL, 1, 0, yaw=0.0)
    put("Fire_Extinguisher", kx0 + 0.01, kz0 + 11.5, FL, 1, 0)
    put("Fire_Extinguisher", kx1 - 0.01, kn - 1.5, FL, -1, 0)
    put("Dustbin", kx0 + 16.5, kz0 + 0.5, FL)
    put("Dustbin", kx1 - 0.5, kz0 + 6.0, FL)
    for X in [kx0 + 2.5 + 4.0 * i for i in range(7)]:
        for Z in (kz0 + 3.5, kz0 + 8.5, kz0 + 13.5):
            if X < kx1 - 1.0 and Z < kn - 1.0:
                put("Pendant_Lamp", X, Z, HIGH_CEIL)
    put("Pendant_Lamp", ke + 3.0, kn + 5.0, HIGH_CEIL)
    put("Pendant_Lamp", ke + 9.0, kn + 5.0, HIGH_CEIL)
    I.room_lights((kx0, kx1, kz0, kn), y=3.5, per=45.0, color=COOL, intensity=1.5)
    I.room_lights((ke, kx1, kn, kz1), y=3.5, per=45.0, color=COOL, intensity=1.3)
    # Glass doors standing open in the three entrances.
    for s, ops in hall_ops.items():
        for a0, a1, z0, z1, d in ops:
            if d:
                mess_entrance(s, a0, a1, HALL_R)
    for s, ops in civic_openings(Model("tmp", *B.centre("Annapurna Mess")), [HALL_R, KIT_R], 1).items():
        for a0, a1, z0, z1, d in ops:
            if d:
                mess_entrance(s, a0, a1, KIT_R)
    put("Door_Leaf", ke - 0.03, -109.6 - 0.8 + 0.03, FL, yaw=0.0)
    for p_ in PLACED[n0:]:
        p_["level"] = 0
        p_["zone"] = zone
    for l_ in LIGHTS[l0:]:
        l_["level"] = 0
        l_["zone"] = zone
    return rooms


def mess_entrance(side, a0, a1, rect):
    X0, X1, Z0, Z1 = rect
    line = {"S": Z0 + MW, "N": Z1 - MW, "W": X0 + MW, "E": X1 - MW}[side]
    n = INWARD[side]
    for a in (a0 + 0.06, a1 - 0.06):
        X, Z = (a, line) if AXIS[side] == 0 else (line, a)
        put("Door_Glass", X, Z, FL, yaw=round(math.degrees(math.atan2(-n[1], n[0])), 2))


# ------------------------------------------------------------------ entry point for build_interiors
def build_all():
    """Shells to export and their zones, rooms and furniture (furniture goes into PLACED and
    LIGHTS with its zone index set, the academic block being zone 0)."""
    out = []
    zone = 1
    for model_name, label, parts in HOSTELS:
        H = Hostel(model_name, label, parts)
        shell = hostel_shell(H)
        rooms = furnish_hostel(H, zone)
        out.append(dict(shell=shell, name=model_name.replace("_", " "), rooms=rooms,
                        floorOffset=FL, maxLevel=TOP))
        print("%-22s %d wings, %d rooms on %d floors" % (model_name, len(parts), len(rooms) // (TOP + 1), TOP + 1))
        zone += 1
    shell, hall_ops, kit_ops = mess_shell()
    rooms = furnish_mess(zone, hall_ops)
    out.append(dict(shell=shell, name="Annapurna Mess", rooms=rooms, floorOffset=FL, maxLevel=0))
    return out
