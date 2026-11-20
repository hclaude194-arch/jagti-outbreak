#!/usr/bin/env python3
"""Generate Assets/Data/campus_layout.json from the traced vision-map geometry.

Map units come from the 2D vision map, which was traced from Google Maps
screenshots. One map unit is about 1 metre (scale set from the 400 m running
track). The origin is the central roundabout at map (414, 996). Map +x maps to
world +X, and map +y (down the screen) maps to world -Z, so "up" on the map is
+Z in Unity.

Run:  python3 tools/layout/generate_layout.py
"""
import json
import math
import os

ORIGIN = (414.0, 996.0)
SCALE = 1.0  # metres per map unit
FLOOR = 3.4  # metres per storey
# Bump when the layout or terrain changes: Unity rebuilds the scene on next open.
VERSION = 28


def to_world(x, y):
    return round((x - ORIGIN[0]) * SCALE, 2), round((ORIGIN[1] - y) * SCALE, 2)


def catmull(points, samples=6):
    """Smooth a polyline with a Catmull-Rom spline, like the 2D map does."""
    if len(points) < 3:
        return points
    out = []
    for i in range(len(points) - 1):
        p0 = points[i - 1] if i > 0 else points[i]
        p1, p2 = points[i], points[i + 1]
        p3 = points[i + 2] if i + 2 < len(points) else p2
        for s in range(samples):
            t = s / samples
            t2, t3 = t * t, t * t * t
            x = 0.5 * ((2 * p1[0]) + (-p0[0] + p2[0]) * t + (2 * p0[0] - 5 * p1[0] + 4 * p2[0] - p3[0]) * t2 + (-p0[0] + 3 * p1[0] - 3 * p2[0] + p3[0]) * t3)
            y = 0.5 * ((2 * p1[1]) + (-p0[1] + p2[1]) * t + (2 * p0[1] - 5 * p1[1] + 4 * p2[1] - p3[1]) * t2 + (-p0[1] + 3 * p1[1] - 3 * p2[1] + p3[1]) * t3)
            out.append((x, y))
    out.append(points[-1])
    return out


# ---------------------------------------------------------------- buildings
# (name, zone, map x, map y, width, depth, storeys, shape)
# x, y is the top-left corner on the map; shape is "box" or "cylinder".
BUILDINGS = [
    ("Main Gate", "gate", 286, 463, 22, 9, 2, "box"),
    ("Clock Tower", "gate", 484.5, 543.5, 3, 3, 9, "box"),
    # Academic block: four wings around the courtyard, entrance, two drums
    ("Academic Block - North Wing", "academic", 444, 646, 99, 32, 4, "box"),
    ("Academic Block - South Wing", "academic", 444, 752, 99, 38, 4, "box"),
    ("Academic Block - West Wing", "academic", 444, 678, 21, 74, 4, "box"),
    ("Academic Block - East Wing", "academic", 513, 678, 30, 74, 4, "box"),
    ("Academic Block - Entrance", "academic", 477, 634, 21, 13, 3, "box"),
    ("Mandapam Auditorium (west drum)", "academic", 423, 688, 40, 53, 3, "cylinder"),
    ("East Drum", "academic", 525, 696, 34, 34, 3, "cylinder"),
    # Wings of one hostel touch but never overlap: build_hostel_interiors.py joins their corridors.
    # Boys' hostels are U blocks like the girls' (owner, 2026-11-18). Brahmaputra opens west
    # to its badminton courtyard, its long east wing along the spine road.
    ("Brahmaputra Hostel - East Wing", "academic", 384, 770, 16, 68, 7, "box"),
    ("Brahmaputra Hostel - North Wing", "academic", 364, 770, 20, 22, 7, "box"),
    ("Brahmaputra Hostel - South Wing", "academic", 364, 816, 20, 22, 7, "box"),
    ("Walled Residence", "central", 348, 926, 22, 20, 2, "box"),
    ("Walled Residence - Wing", "central", 366, 936, 10, 16, 1, "box"),
    # The mess area follows the institute's scale model (owner, 2026-11-18): dining block 5
    # (Annapurna Mess), the shopping block 6 (Vipanan, below) and the medical block 7 (block 1)
    # on a terrace above the girls' hostels, and the professor residences 4 behind them on a
    # terrace a little higher again: a front row of two stepped towers and a back row of three.
    # The mess and the medical block keep the sizes their interiors were drawn for
    # (INTERIOR_DESIGN in build_buildings.py).
    ("Shopping & Medical 1 (ATM)", "central", 370, 1020, 16, 30, 3, "box"),
    # Vipanan, the shopping arcade (owner's video and photo, 2026-11-19; satellite view): a
    # single-storey L of narrow arms round a paved forecourt, the west arm's shops facing east and
    # the north arm's facing south, a covered passage between the arms at the corner.
    ("Vipanan - West Arm", "central", 370, 1055, 9, 37, 1, "box"),
    ("Vipanan - North Arm", "central", 382, 1055, 20, 9, 1, "box"),
    ("Annapurna Mess", "central", 363, 1102, 29, 26, 2, "box"),
    ("Annapurna Mess - Kitchen", "central", 378, 1118, 30, 29, 2, "box"),
    ("Faculty Residence 1 - South", "central", 324, 1104, 15, 22, 6, "box"),
    ("Faculty Residence 1 - North", "central", 328, 1082, 15, 22, 6, "box"),
    ("Faculty Residence 2 - South", "central", 324, 1050, 15, 24, 6, "box"),
    ("Faculty Residence 2 - North", "central", 328, 1026, 15, 24, 6, "box"),
    ("Faculty Residence 3 - South", "central", 266, 1148, 16, 26, 6, "box"),
    ("Faculty Residence 3 - North", "central", 270, 1122, 16, 26, 6, "box"),
    ("Faculty Residence 4 - South", "central", 266, 1094, 16, 24, 6, "box"),
    ("Faculty Residence 4 - North", "central", 270, 1070, 16, 24, 6, "box"),
    ("Faculty Residence 5 - South", "central", 266, 1042, 16, 24, 6, "box"),
    ("Faculty Residence 5 - North", "central", 270, 1018, 16, 24, 6, "box"),
    # Girls' hostels (label 9 on the institute's scale model; the owner's top view of 2026-11-18):
    # two U-shaped blocks round courtyards with badminton courts, and a pair of tennis courts
    # beside the running track. B is the U traced from the satellite view (opening south); A
    # replaces the stepped chain with the model's U opening east, and its north wing meets B's
    # west wing face to face so the corridors join.
    ("Girls Hostel B - West Wing", "hostels", 462, 1078, 16, 40, 7, "box"),
    ("Girls Hostel B - North Wing", "hostels", 478, 1078, 43, 18, 7, "box"),
    ("Girls Hostel B - East Wing", "hostels", 521, 1078, 14, 40, 7, "box"),
    ("Girls Hostel A - North Wing", "hostels", 432, 1104, 26, 26, 7, "box"),   # 4 m clear of Girls Hostel B (they touched, and a doorway opened into a B room)
    ("Girls Hostel A - West Wing", "hostels", 432, 1130, 14, 40, 7, "box"),
    ("Girls Hostel A - South Wing", "hostels", 432, 1170, 30, 28, 7, "box"),
    ("Track Pavilion", "hostels", 484, 1161, 50, 8, 1, "box"),
    ("Sports Complex", "hostels", 438, 1297, 25, 54, 3, "box"),
    ("East Block", "hostels", 564, 1098, 18, 28, 3, "box"),
    ("East Block 2", "hostels", 589, 1274, 34, 24, 2, "box"),   # clear of the track road (was across it)
    # Sindhu (boys): two U blocks opening east round badminton courtyards.
    ("Sindhu Hostel A - North Wing", "sindhu", 434, 1379, 40, 22, 7, "box"),
    ("Sindhu Hostel A - West Wing", "sindhu", 434, 1401, 16, 32, 7, "box"),
    ("Sindhu Hostel A - South Wing", "sindhu", 434, 1433, 40, 22, 7, "box"),
    ("Sindhu Hostel B - North Wing", "sindhu", 434, 1461, 40, 22, 7, "box"),
    ("Sindhu Hostel B - West Wing", "sindhu", 434, 1483, 16, 32, 7, "box"),
    ("Sindhu Hostel B - South Wing", "sindhu", 434, 1515, 40, 22, 7, "box"),
    ("Service Shed 1", "sindhu", 388, 1490, 26, 13, 1, "box"),
    ("Service Shed 2", "sindhu", 392, 1512, 24, 12, 1, "box"),
    ("Service Shed 3", "sindhu", 395, 1531, 22, 12, 1, "box"),
    ("West Building 1", "sindhu", 289, 1332, 30, 16, 2, "box"),
    ("West Building 2", "sindhu", 294, 1414, 28, 14, 2, "box"),
    ("West Building 3", "sindhu", 325, 1418, 22, 13, 2, "box"),
    ("West Building 4", "hostels", 300, 1232, 26, 16, 2, "box"),
    ("Substation Control Room", "gate", 250, 668, 24, 40, 1, "box"),
    # Overhead water tank on the hill behind the courts (seen on the institute's campus model).
    ("Water Tower", "hostels", 369, 1244, 12, 12, 8, "cylinder"),
]

# ---------------------------------------------------------------- roads
def rounded(points, radii, steps=6):
    """Replace corner i of a polyline with a circular arc of radius radii[i] (other corners stay sharp)."""
    out = []
    for i, p in enumerate(points):
        r = radii.get(i)
        if not r or i == 0 or i == len(points) - 1:
            out.append(list(p))
            continue
        ax, ay = points[i - 1]
        bx, by = points[i + 1]
        px, py = p
        d1 = math.hypot(ax - px, ay - py)
        d2 = math.hypot(bx - px, by - py)
        u1 = ((ax - px) / d1, (ay - py) / d1)
        u2 = ((bx - px) / d2, (by - py) / d2)
        cos_t = max(-1.0, min(1.0, u1[0] * u2[0] + u1[1] * u2[1]))
        half = math.acos(cos_t) / 2
        if half < 1e-3 or half > math.pi / 2 - 1e-3:
            out.append(list(p))
            continue
        t = min(r / math.tan(half), d1 * 0.45, d2 * 0.45)   # tangent distance from the corner
        rr = t * math.tan(half)
        s1 = (px + u1[0] * t, py + u1[1] * t)
        s2 = (px + u2[0] * t, py + u2[1] * t)
        bis = (u1[0] + u2[0], u1[1] + u2[1])
        bl = math.hypot(*bis)
        cdist = rr / math.sin(half)
        cx, cy = px + bis[0] / bl * cdist, py + bis[1] / bl * cdist
        a1 = math.atan2(s1[1] - cy, s1[0] - cx)
        a2 = math.atan2(s2[1] - cy, s2[0] - cx)
        da = (a2 - a1 + math.pi) % (2 * math.pi) - math.pi
        for k in range(steps + 1):
            a = a1 + da * k / steps
            out.append([round(cx + rr * math.cos(a), 2), round(cy + rr * math.sin(a), 2)])
    return out


# Roundabouts (satellite, 2026-11-19): a kerbed green island ringed by a carriageway, the
# roads meeting it square on. (name, map x, map y, island radius m, ring width m)
ROUNDABOUTS = [
    ("Gate", 296, 487, 3.6, 7.0),       # the island is the gate model's flower bed (build_buildings.main_gate)
    ("Central", 414, 996, 5.5, 7.5),
    ("South", 421, 1364, 4.5, 7.0),
]


def ring_at(name, side):
    """Map point where a road meets the ring's centre line on its N, S, E or W side."""
    _, x, y, island, ring = next(r for r in ROUNDABOUTS if r[0] == name)
    rc = island + ring / 2
    return [x + {"E": rc, "W": -rc}.get(side, 0.0), y + {"S": rc, "N": -rc}.get(side, 0.0)]


# (name, width m, points, smooth)
ROADS = [
    ("Serpentine Drive", 7, [ring_at("Gate", "E"), [380, 487], [480, 487], [505, 490], [513, 505], [503, 522], [400, 523], [318, 523], [302, 532], [304, 548], [322, 555], [420, 555], [440, 562], [446, 578], [438, 592], [418, 600], [408, 612], [405, 627]], True),
    ("Gate Approach", 8, [[296, 440], ring_at("Gate", "N")], False),
    # Round the academic block, as on the campus model: the west side and the north side up to the
    # porte-cochere are asphalt (the through route from the serpentine to the spine road); from the
    # porch round the east and south sides is a cream paved drive with rounded corners.
    ("Academic West Drive", 6, [[405, 627], [405, 812]], False),
    ("Porch Drive", 6, [[405, 627], [497, 627]], False),
    ("Academic Ring", 6, rounded([[497, 627], [555, 627], [555, 686], [563, 694], [563, 732], [555, 740], [555, 812], [405, 812]],
                                 {1: 14, 2: 5, 3: 5, 4: 5, 5: 5, 6: 14}), False),
    # The spine road meets the central roundabout square on from north and south (satellite):
    # its two halves swing in to the roundabout's axis over the last 40 m.
    ("Spine Road North", 8, rounded([[405, 812], [405, 950], [414, 972], ring_at("Central", "N")], {1: 30, 2: 30}), False),
    ("Spine Road South", 8, rounded([ring_at("Central", "S"), [414, 1016], [421, 1036], ring_at("South", "N")], {1: 30, 2: 30}), False),
    ("Spine Road Sindhu", 8, [ring_at("South", "S"), [421, 1545]], False),
    ("Central Road West", 6, [[285, 990], [350, 990], [385, 995], ring_at("Central", "W")], True),
    ("Central Road East", 6, [ring_at("Central", "E"), [440, 994], [500, 985], [600, 972], [690, 962]], True),
    ("Southern Road West", 7, [[250, 1366], [330, 1366], [380, 1364.5], ring_at("South", "W")], True),
    ("Southern Road East", 7, [ring_at("South", "E"), [500, 1362], [535, 1355], [562, 1338], [574, 1318]], True),
    ("Track Road", 5, [[600, 972], [582, 1030], [562, 1075], [545, 1105], [548, 1140], [572, 1165], [584, 1215], [584, 1280], [574, 1318]], True),
    ("Sindhu Back Road", 5, [[421, 1545], [468, 1546], [512, 1541]], True),
    # The oval green west of the mess (satellite, 2026-11-19): a loop road all round a long lawn,
    # open at its north end onto the central road, with the faculty rows on both sides, and a
    # lane on from its south end to the southern faculty towers.
    ("Oval Loop", 5, [[298, 991]] + [[309.5 + 11.5 * math.cos(math.radians(a)), 1099 + 11.5 * math.sin(math.radians(a))]
                                     for a in range(180, -1, -15)] + [[321, 991]], False),
    ("Faculty Lane", 4, [[309.5, 1110.5], [304, 1135], [302, 1160]], True),
    ("Walled Residence Drive", 4, [[405, 936], [392, 936]], False),
    ("Brahmaputra Drive", 4, [[405, 700], [392, 702], [384, 740], [388, 763]], True),
    # Cross-shaped garden paths in the academic courtyard (campus model).
    # Widened to the owner's photo of the courtyard (2026-11-18): wide paved cross paths, a loop
    # round the middle and a paved square round the central pool.
    ("Courtyard Path North-South", 4.0, [[489, 680], [489, 750]], False),
    ("Courtyard Path East-West", 4.0, [[467, 715], [511, 715]], False),
    ("Courtyard Path Loop", 3.0, [[475, 694], [503, 694], [503, 736], [475, 736], [475, 694]], False),
    ("Courtyard Centre Square", 9.0, [[489, 710], [489, 720]], False),
    # Paved walks (Phase 4D): the pavement along the shop fronts beside the spine road (the only
    # road to Annapurna Mess, per the satellite), and from the spine road to the sports complex lobby.
    ("Plaza Shops Walk", 4, [[405, 1004], [405, 1099]], False),
    ("Plaza Sports Walk", 4, [[425, 1342], [437, 1342]], False),
    # The turn to the girls' hostels (owner's photos 5 and 6 of 2026-11-19, satellite): a lane of
    # interlocking pavers forks off the spine road at a shallow angle by the sports complex and
    # runs north beside it, a long kerbed island between them, past the courts (fenced on a stone
    # wall to its east) to the hostels. The spine climbs to the mess while the lane stays at the
    # hostels' level, so the island becomes a grassy bank with a railing along the spine. At the
    # hostel the lane turns east past Girls Hostel A's south door, then north between the hostel
    # and the tennis courts.
    ("Paved Girls Hostel Lane", 4.5, rounded([[421, 1306], [435, 1280], [435, 1203.35], [470, 1203.35], [470, 1124]],
                                             {1: 30, 2: 10, 3: 8}), False),
    ("Highway", 13, [[-10, 575], [80, 545], [180, 500], [260, 455], [330, 430], [420, 412], [560, 400], [700, 392], [940, 385]], True),
]

# ---------------------------------------------------------------- flat areas
# (type, shape, map x, map y, width, depth)  x, y = top-left for box, centre for ellipse and pill
# (a pill: a rectangle whose short ends are half circles, as a running track)
AREAS = [
    ("lawn", "box", 322, 493, 175, 24),
    ("lawn", "box", 322, 529, 110, 21),
    ("lawn", "box", 430, 560, 40, 60),
    ("lawn", "box", 293, 908, 100, 72),
    ("lawn", "box", 478, 1409, 33, 88),
    # Badminton courtyards of the boys' hostels (Sindhu A and B, Brahmaputra).
    ("lawn", "box", 450, 1401, 24, 32),
    ("court", "box", 455, 1406, 13, 6),
    ("court", "box", 455, 1422, 13, 6),
    ("lawn", "box", 450, 1483, 24, 32),
    ("court", "box", 455, 1488, 13, 6),
    ("court", "box", 455, 1504, 13, 6),
    ("lawn", "box", 364, 792, 20, 24),
    ("court", "box", 368, 797, 13, 6),
    ("court", "box", 368, 805, 13, 6),
    # Girls' hostel courtyards: a lawn with two badminton courts in each (courts under 16 m).
    ("lawn", "box", 446, 1130, 16, 40),
    ("court", "box", 447, 1139, 13, 6),
    ("court", "box", 447, 1155, 13, 6),
    ("lawn", "box", 478, 1096, 43, 22),
    ("court", "box", 489, 1100, 6, 13),
    ("court", "box", 504, 1100, 6, 13),
    ("courtyard", "box", 465, 678, 48, 74),
    ("lawn", "ellipse", 309.5, 1047, 17, 106),
    ("parking", "box", 379, 840, 20, 50),
    ("plaza", "box", 383, 1068, 20, 24),   # Vipanan's paved forecourt, open to the spine road
    ("substation", "box", 247, 618, 30, 44),
    ("court", "box", 440, 1207, 26, 28),
    ("court", "box", 440, 1237, 26, 28),
    ("court", "box", 440, 1267, 26, 26),
    ("court", "box", 478, 1124, 28, 30),   # the girls' hostel tennis courts (a pair)
    # A 400 m running track is a pill (two straights joined by half circles), not an oval:
    # ends of radius width/2 along the long axis (owner, 2026-11-19).
    ("track", "pill", 517, 1261, 96, 170),
    ("field", "pill", 517, 1261, 74, 146),
    ("amphitheatre", "box", 565, 688, 64, 114),
] + [("roundabout", "ellipse", x, y, 2 * island, 2 * island) for _n, x, y, island, _w in ROUNDABOUTS]

# ---------------------------------------------------------------- places (labels, fast travel)
PLACES = [
    ("Main Gate", 296, 470),
    ("Clock Tower", 486, 545),
    ("Academic Block", 489, 708),
    ("Mandapam Auditorium", 440, 712),
    ("Amphitheatre & Stepped Pond", 598, 735),
    ("Water Terraces", 345, 740),
    ("Brahmaputra Hostel (MDP Residential?)", 386, 808),
    ("Car Park", 389, 865),
    ("Power Substation", 262, 640),
    ("Director's Residence", 359, 936),
    ("Medical Block", 378, 1035),
    ("Vipanan Shopping Arcade", 386, 1078),
    ("Annapurna Mess (Dining Block)", 386, 1124),
    ("Faculty Residences", 300, 1080),
    ("Girls' Hostels", 459, 1126),
    ("Sports Courts", 453, 1237),
    ("Indoor Games Block", 450, 1324),
    ("Athletics Track", 517, 1261),
    ("Water Tower", 375, 1250),
    ("Sindhu Hostel (Executive Hostel?)", 448, 1442),
    ("Service Yard", 401, 1515),
]

# ---------------------------------------------------------------- water (campus model)
# Terraced lawns west of the academic block, stepping down towards the west wall. Each step
# line is the foot of a 3.5 m ramp; the ground west of it is one step lower, and a
# water channel runs along the foot of the ramp. The lines jog like the channels on the
# institute's model (top view), fitted between the west wall and the MDP block. A step fades
# out over 8 m past the ends of its line.
TERRACE_LINES = [
    [[329.5, 604], [329.5, 653], [336, 660], [336, 708], [319, 743], [319, 784], [321, 791], [321, 850], [301, 864], [301, 872]],
    [[340, 604], [343.7, 710], [334, 726], [334, 784], [340, 791], [340, 860], [328.5, 872]],
    [[354, 604], [351, 722], [347.5, 740], [347.5, 784], [348.5, 791], [348.5, 821], [354, 829]],
    [[356, 604], [356, 648], [360, 655], [360, 729], [359, 733], [359, 872]],
    [[376, 608], [376, 740]],
]
# (map x0, y0, x1, y1, drop per step m, step lines)  highest along x1
TERRACES = [(298, 600, 384, 876, 1.1, TERRACE_LINES)]
# (name, width m, points)  water channels drawn on the ground
WATER = []
for _i, _pts in enumerate(TERRACE_LINES):
    WATER.append(("Terrace Channel %d" % (_i + 1), 1.6, [[x - 1.0, y] for x, y in _pts]))
WATER += [
    # Two cascades cut across the steps, each with a square basin at its foot.
    ("Terrace Cascade South", 3.0, [[357, 787.5], [306, 787.5]]),
    ("Terrace Cascade South Basin", 11.0, [[300, 787.5], [311, 787.5]]),
    ("Terrace Cascade North", 5.0, [[375, 738.5], [352, 728], [329, 713]]),
    ("Terrace Cascade North Basin", 8.0, [[324, 711], [332, 714]]),
]
# (name, map x, map y, radius m)  round pools
POOLS = [
    # On the entrance axis in front of the porch, inside a square frame (see the academic model).
    ("Entrance Fountain", 486, 619, 3.4),
]
# Trees planted in rows or spots that the scattered planting would miss
# (kind: 0 broadleaf, 1 eucalyptus, 2 pine, 3 shrub; spacing m; offset east of the line m).
# Sparse planted rows (the owner's photos show few trees on the campus, 2026-11-19).
TREE_ROWS = [(0, 18.0, 4.5, _pts) for _pts in TERRACE_LINES[:4]] + [(0, 18.0, -3.5, TERRACE_LINES[4])]
# Courtyard garden: open lawns now (owner's photo); its small palms are outdoor props.
TREE_SPOTS = []

# The clock tower walk (campus model, satellite): a straight stepped walk from the serpentine's
# second leg up to the fountain square in front of the academic porch, with the tower on a level
# landing near its foot. generate_terrain.py designs the flights and landings to fit the heights
# there and writes them to campus_terrain.json ("clockSteps"); build_buildings.py builds them.
# (map x, map y bottom, map y top, width m, map y of the tower landing's centre, landing length m)
CLOCK_STEPS = (486, 526.5, 613, 4.0, 545, 15.0)
# Height of the steps the walk climbs between the serpentine and the porch (m): the serpentine
# is pinned this far below the porch where the walk meets it.
CLOCK_STEPS_RISE = 15.0
# The amphitheatre is sunk into the slope east of the academic block: its top walk is level with
# the ring road and its stage and pond this far below (11 tiers of 0.36 m in build_buildings.py).
AMPHITHEATRE_DEPTH = 3.96

# Boundary wall polygon from the map
BOUNDARY = [[282, 468], [560, 468], [590, 600], [645, 690], [645, 830], [640, 1000], [640, 1200], [604, 1380], [560, 1565], [380, 1565], [332, 1440], [262, 1300], [262, 1000], [290, 860], [296, 700], [278, 560]]

SPAWN = (392, 845)  # outside Brahmaputra Hostel, facing the academic block


# ---------------------------------------------------------------- junctions
# Footpaths and the highway get no flared corners; drives get kerb returns of these radii.
def flare_radius(width, host_name):
    if host_name.startswith("Roundabout"):
        return 7.0
    return 6.0 if width >= 6 else (5.0 if width >= 5 else 3.5)


def is_footpath(name):
    return name.startswith("Courtyard") or name.startswith("Plaza")


def _seg_nearest(p, a, b):
    ex, ez = b[0] - a[0], b[1] - a[1]
    l2 = ex * ex + ez * ez
    t = 0.0 if l2 < 1e-12 else max(0.0, min(1.0, ((p[0] - a[0]) * ex + (p[1] - a[1]) * ez) / l2))
    q = (a[0] + t * ex, a[1] + t * ez)
    return math.hypot(p[0] - q[0], p[1] - q[1]), q, (ex, ez)


def _nearest(p, poly):
    """(distance, nearest point, unit tangent there) from p to a polyline."""
    best = None
    for a, b in zip(poly, poly[1:]):
        d, q, e = _seg_nearest(p, a, b)
        if best is None or d < best[0]:
            best = (d, q, e)
    d, q, (ex, ez) = best
    el = math.hypot(ex, ez) or 1.0
    return d, q, (ex / el, ez / el)


def _densify(poly, step):
    out = [poly[0]]
    for a, b in zip(poly, poly[1:]):
        n = max(1, int(math.ceil(math.dist(a, b) / step)))
        for k in range(1, n + 1):
            out.append((a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n))
    return out


def build_roads():
    """Roads in world space, with what happens where they meet:
    - each roundabout gets a closed ring road round its island;
    - a road that ends on another (a T, or onto a ring) is carried to the other road's centre
      line, so no grass shows between them, and gets flared corners: kerb returns, circular arcs
      tangent to both roads' edges (left out on the sharp side of a shallow fork);
    - where two roads end at one point at an angle (an L), the wider one runs on past the corner
      by half the other's width, so the outside of the bend is paved;
    - each road gets a layer, higher than every road that joins it, which Unity uses to lift the
      host road's surface just above the ends that run under it.
    Flared ends are sampled every 0.5 m and carry per-point half widths for each side ("left" is
    the side to the left looking along the points, i.e. +normal (-dz, dx))."""
    roads = []
    for name, width, pts, smooth in ROADS:
        p = catmull(pts) if smooth else pts
        roads.append({"name": name, "width": float(width), "pts": [to_world(x, y) for x, y in p]})
    for name, x, y, island, ring in ROUNDABOUTS:
        cx, cz = to_world(x, y)
        rc = island + ring / 2
        n = 48
        circle = [(round(cx + rc * math.cos(2 * math.pi * k / n), 2), round(cz + rc * math.sin(2 * math.pi * k / n), 2)) for k in range(n)]
        roads.append({"name": "Roundabout Ring - " + name, "width": float(ring), "pts": circle + [circle[0]], "closed": True})
    by_name = {r["name"]: r for r in roads}
    hosts = {}    # joiner name -> set of host names

    def ends_at(r, q, tol=0.6):
        return [k for k in (0, -1) if not r.get("closed") and math.dist(r["pts"][k], q) < tol]

    def out_dir(r, k):
        """Unit direction pointing away from the road's end k (0 start, -1 end)."""
        a, b = (r["pts"][0], r["pts"][1]) if k == 0 else (r["pts"][-1], r["pts"][-2])
        d = (a[0] - b[0], a[1] - b[1])
        L = math.hypot(*d) or 1.0
        return d[0] / L, d[1] / L

    flares = []   # (road, end, roads meeting there, [radius left, radius right])
    pending = []  # end extensions, applied once every junction has been read
    for r in roads:
        if r.get("closed") or r["name"] == "Highway":
            continue
        for k in (0, -1):
            e = r["pts"][k]
            cands = []
            for o in roads:
                if o is r:
                    continue
                d, q, t = _nearest(e, o["pts"])
                if d < o["width"] / 2 + 0.6:
                    cands.append((d, o, q, t))
            if not cands:
                continue
            cands.sort(key=lambda c: c[0])
            u = out_dir(r, k)            # on past the end, towards the host
            inward = (-u[0], -u[1])
            # Is this end simply carried on by another road (a continuation, not a junction)?
            cont = any(ends_at(o, e) and abs(inward[0] * out_dir(o, ends_at(o, e)[0])[0] +
                                             inward[1] * out_dir(o, ends_at(o, e)[0])[1]) > 0.9
                       for _d, o, _q, _t in cands)
            if cont:
                # Carry the end 1.5 m on under its partner, so a bend at the joint leaves no notch.
                pending.append((r, k, (round(e[0] + u[0] * 1.5, 2), round(e[1] + u[1] * 1.5, 2))))
                continue
            d, host, q, t = cands[0]
            if ends_at(host, e) and not any(ends_at(o, e) for _d, o, _q, _t in cands[1:]):
                # An L: two roads end at one corner. The wider is the host and runs on.
                if host["width"] < r["width"] or (host["width"] == r["width"] and roads.index(host) > roads.index(r)):
                    continue      # the other end of the pair handles it
                hk = ends_at(host, e)[0]
                hu = out_dir(host, hk)
                ext = (e[0] + hu[0] * (r["width"] / 2 + 0.3), e[1] + hu[1] * (r["width"] / 2 + 0.3))
                pending.append((host, hk, ext))
                hosts.setdefault(r["name"], set()).add(host["name"])
                continue
            hosts.setdefault(r["name"], set()).add(host["name"])
            # Carry the end on (straight) to the host's centre line.
            best = (d, 0.0)
            for i in range(1, int(host["width"] * 4) + 1):
                s_ = i * 0.25
                dd = _nearest((e[0] + u[0] * s_, e[1] + u[1] * s_), host["pts"])[0]
                if dd < best[0]:
                    best = (dd, s_)
            if best[1] > 0:
                ne = (round(e[0] + u[0] * best[1], 2), round(e[1] + u[1] * best[1], 2))
                pending.append((r, k, ne))
            if is_footpath(r["name"]) or is_footpath(host["name"]) or host["name"] == "Highway":
                continue
            # Flare each side whose corner angle is between 50 and 130 degrees.
            rad = flare_radius(r["width"], host["name"])
            radii = []
            for side in (1, -1):           # +1 left of the road's direction of travel, -1 right
                fwd = r["pts"][1] if k == 0 else r["pts"][-2]
                base = r["pts"][0] if k == 0 else r["pts"][-1]
                dirn = (fwd[0] - base[0], fwd[1] - base[1])
                L = math.hypot(*dirn) or 1.0
                dirn = (dirn[0] / L, dirn[1] / L)                   # away from the host
                nrm = (-dirn[1] * side, dirn[0] * side)               # this side, in world terms
                if k == -1:
                    nrm = (-nrm[0], -nrm[1])                          # the road runs into the host here
                # The host's direction on this side of the joining road.
                th = t if (t[0] * nrm[0] + t[1] * nrm[1]) >= 0 else (-t[0], -t[1])
                ang = math.degrees(math.acos(max(-1.0, min(1.0, th[0] * dirn[0] + th[1] * dirn[1]))))
                radii.append(rad if 50.0 <= ang <= 130.0 else 0.0)
            if any(radii):
                # The fillets keep clear of every road meeting at the junction (a host that ends
                # here is carried on by its partner).
                flares.append((r, k, [o for _d, o, _q, _t in cands], radii))

    for r, k, q in pending:
        if k == 0:
            r["pts"].insert(0, q)
        else:
            r["pts"].append(q)

    # Per-point half widths on flared ends.
    for r in roads:
        r["left"] = None
        r["junctions"] = [(k, [o["name"] for o in host], max(radii)) for rr, k, host, radii in flares if rr is r]
    out = []
    for r in roads:
        my = [f for f in flares if f[0] is r]
        pts = r["pts"]
        if my:
            # Sample the flared ends densely (0.5 m) out to where the flare finishes.
            reach = {0: 0.0, -1: 0.0}
            for _r, k, host, radii in my:
                reach[k] = max(o["width"] for o in host) / 2 + max(radii) * 2.2 + 2.0
            s = [0.0]
            for a, b in zip(pts, pts[1:]):
                s.append(s[-1] + math.dist(a, b))
            total = s[-1]
            dense = [pts[0]]
            lo, hi = reach[0], total - reach[-1]
            for i, (a, b) in enumerate(zip(pts, pts[1:])):
                seg = math.dist(a, b)
                if seg < 1e-9:
                    continue
                # Break points: 0.5 m apart inside the flared reaches, the segment's own ends elsewhere.
                cuts = {1.0}
                for f_ in [x * 0.5 for x in range(int(seg / 0.5) + 1)]:
                    if s[i] + f_ < lo or s[i] + f_ > hi:
                        cuts.add(f_ / seg)
                for c_ in (lo, hi):
                    if s[i] < c_ < s[i + 1]:
                        cuts.add((c_ - s[i]) / seg)
                for f_ in sorted(c for c in cuts if c > 1e-6):
                    dense.append((a[0] + (b[0] - a[0]) * f_, a[1] + (b[1] - a[1]) * f_))
            pts = dense
            half = r["width"] / 2
            left = [half] * len(pts)
            right = [half] * len(pts)
            for _r, k, host, radii in my:
                order = list(range(len(pts))) if k == 0 else list(range(len(pts) - 1, -1, -1))
                for side, rad in zip((1, -1), radii):
                    if rad <= 0:
                        continue
                    arr = left if side == 1 else right
                    # Walk out from the junction: find the fillet circle's centre C, where a point
                    # (half + rad) out from the centre line is rad clear of the host's edge.
                    sc = None
                    acc = 0.0
                    rows = []
                    for idx_n, i in enumerate(order):
                        if idx_n > 0:
                            acc += math.dist(pts[i], pts[order[idx_n - 1]])
                        a_i = order[max(idx_n - 1, 0)] if idx_n > 0 else order[1]
                        b_i = order[min(idx_n + 1, len(order) - 1)]
                        dx, dz = pts[b_i][0] - pts[a_i][0], pts[b_i][1] - pts[a_i][1]
                        if idx_n == 0:
                            dx, dz = pts[order[1]][0] - pts[i][0], pts[order[1]][1] - pts[i][1]
                        L = math.hypot(dx, dz) or 1.0
                        nrm = (-dz / L, dx / L)
                        # "left" is defined along the stored point order; walking from the end
                        # reverses it.
                        sgn = side if k == 0 else -side
                        nrm = (nrm[0] * sgn, nrm[1] * sgn)
                        rows.append((i, acc, nrm))
                        C = (pts[i][0] + nrm[0] * (half + rad), pts[i][1] + nrm[1] * (half + rad))
                        if min(_nearest(C, o["pts"])[0] - o["width"] / 2 for o in host) >= rad:
                            sc = (idx_n, acc, C)
                            break
                    if sc is None:
                        continue
                    ic, s_c, C = sc
                    _hd, H, _ht = min((_nearest(C, o["pts"]) for o in host), key=lambda n_: n_[0])
                    hl = math.dist(C, H) or 1.0
                    T = (C[0] + (H[0] - C[0]) / hl * rad, C[1] + (H[1] - C[1]) / hl * rad)
                    # T in the road's frame: distance along and out from the centre line.
                    i0, _a0, n0 = rows[0]
                    best = min(rows, key=lambda row: abs((T[0] - pts[row[0]][0]) * row[2][1] - (T[1] - pts[row[0]][1]) * row[2][0]))
                    s_t = best[1]
                    lat_t = (T[0] - pts[best[0]][0]) * best[2][0] + (T[1] - pts[best[0]][1]) * best[2][1]
                    lat_t = max(half, min(half + rad, lat_t))
                    run = half
                    for i, s_i, _n in reversed(rows):     # from the flare's far end in to the junction
                        if s_i <= s_t:
                            w = lat_t
                        else:
                            w = half + rad - math.sqrt(max(0.0, rad * rad - (s_i - s_c) ** 2))
                        run = max(run, min(half + rad, w))  # never narrower nearer the junction
                        arr[i] = max(arr[i], run)
            r["left"], r["right"] = left, right
        r["pts"] = pts
        out.append(r)

    # Layers: a host sits above every road that joins it.
    layer = {r["name"]: 0 for r in roads}
    for _ in range(len(roads)):
        changed = False
        for j, hs in hosts.items():
            for h in hs:
                if layer[h] < layer[j] + 1:
                    layer[h] = layer[j] + 1
                    changed = True
        if not changed:
            break
    for r in out:
        r["layer"] = min(layer[r["name"]], 6)
    return out


def flat(points):
    out = []
    for x, y in points:
        wx, wz = to_world(x, y)
        out += [wx, wz]
    return out


def main():
    layout = {
        "info": "Generated by tools/layout/generate_layout.py from the Jagti vision map. 1 unit = 1 m. Origin = central roundabout.",
        "version": VERSION,
        "floorHeight": FLOOR,
        "buildings": [],
        "roads": [],
        "areas": [],
        "places": [],
        "boundary": flat(BOUNDARY),
        "water": [],
        "pools": [],
        "spawn": {},
    }
    for name, zone, x, y, w, d, floors, shape in BUILDINGS:
        cx, cz = to_world(x + w / 2, y + d / 2)
        layout["buildings"].append({
            "name": name, "zone": zone, "x": cx, "z": cz,
            "width": w * SCALE, "depth": d * SCALE,
            "height": round(floors * FLOOR, 2), "floors": floors, "shape": shape,
        })
    for r in build_roads():
        rd = {"name": r["name"], "width": r["width"], "layer": r["layer"],
              "points": [round(v, 2) for q in r["pts"] for v in q]}
        if r["left"] is not None:
            rd["left"] = [round(v, 3) for v in r["left"]]
            rd["right"] = [round(v, 3) for v in r["right"]]
        layout["roads"].append(rd)
    for typ, shape, x, y, w, d in AREAS:
        if shape == "box":
            cx, cz = to_world(x + w / 2, y + d / 2)
        else:
            cx, cz = to_world(x, y)
        layout["areas"].append({"type": typ, "shape": shape, "x": cx, "z": cz, "width": w * SCALE, "depth": d * SCALE})
    for name, width, pts in WATER:
        layout["water"].append({"name": name, "width": width, "points": flat(pts)})
    for name, x, y, r in POOLS:
        wx, wz = to_world(x, y)
        layout["pools"].append({"name": name, "x": wx, "z": wz, "radius": r})
    for name, x, y in PLACES:
        wx, wz = to_world(x, y)
        layout["places"].append({"name": name, "x": wx, "z": wz})
    sx, sz = to_world(*SPAWN)
    layout["spawn"] = {"x": sx, "z": sz}

    root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    out = os.path.join(root, "Assets", "Data", "campus_layout.json")
    with open(out, "w") as f:
        json.dump(layout, f, indent=1)
    print(f"Wrote {out}: {len(layout['buildings'])} buildings, {len(layout['roads'])} roads, {len(layout['areas'])} areas")


if __name__ == "__main__":
    main()
