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
VERSION = 15


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
    ("Brahmaputra Hostel", "academic", 378, 782, 22, 26, 7, "box"),
    ("Brahmaputra Hostel - South", "academic", 371, 808, 25, 28, 7, "box"),
    ("Walled Residence", "central", 348, 926, 22, 20, 2, "box"),
    ("Walled Residence - Wing", "central", 366, 936, 10, 16, 1, "box"),
    ("Block near ATM", "central", 376, 1017, 17, 22, 3, "box"),
    ("Block near ATM 2", "central", 381, 1049, 22, 16, 3, "box"),
    ("Block near ATM 3", "central", 395, 1064, 10, 16, 2, "box"),
    ("Staff Quarters 1", "central", 268, 1008, 18, 30, 5, "box"),
    ("Staff Quarters 2", "central", 269, 1052, 17, 22, 5, "box"),
    ("Staff Quarters 3", "central", 269, 1076, 17, 24, 5, "box"),
    ("Staff Quarters 4", "central", 270, 1106, 17, 22, 5, "box"),
    ("Staff Quarters 5", "central", 271, 1130, 16, 20, 5, "box"),
    ("Shopping & Medical 1 (ATM)", "central", 335, 1008, 16, 30, 3, "box"),
    ("Shopping & Medical 2", "central", 336, 1043, 15, 22, 3, "box"),
    ("Shopping & Medical 3", "central", 336, 1069, 15, 22, 3, "box"),
    ("Annapurna Mess", "central", 368, 1087, 29, 26, 2, "box"),
    ("Annapurna Mess - Kitchen", "central", 383, 1103, 30, 29, 2, "box"),
    ("Hostel U - West", "hostels", 462, 1078, 16, 40, 7, "box"),
    ("Hostel U - North", "hostels", 478, 1078, 43, 18, 7, "box"),
    ("Hostel U - East", "hostels", 521, 1078, 14, 40, 7, "box"),
    ("Hostel Chain 1", "hostels", 440, 1118, 26, 22, 7, "box"),
    ("Hostel Chain 2", "hostels", 434, 1144, 26, 22, 7, "box"),
    ("Hostel Chain 3", "hostels", 437, 1170, 28, 22, 7, "box"),
    ("Track Pavilion", "hostels", 484, 1161, 50, 8, 1, "box"),
    ("Sports Complex", "hostels", 438, 1297, 25, 54, 3, "box"),
    ("East Block", "hostels", 564, 1098, 18, 28, 3, "box"),
    ("East Block 2", "hostels", 580, 1274, 34, 24, 2, "box"),
    ("Sindhu Hostel - North", "sindhu", 466, 1379, 36, 31, 7, "box"),
    ("Sindhu Hostel - Step 1", "sindhu", 433, 1410, 50, 27, 7, "box"),
    ("Sindhu Hostel - West", "sindhu", 434, 1437, 28, 40, 7, "box"),
    ("Sindhu Hostel - Step 2", "sindhu", 437, 1477, 46, 22, 7, "box"),
    ("Sindhu Hostel - South", "sindhu", 471, 1499, 38, 38, 7, "box"),
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


# (name, width m, points, smooth)
ROADS = [
    ("Serpentine Drive", 7, [[296, 487], [380, 487], [480, 487], [505, 490], [513, 505], [503, 522], [400, 523], [318, 523], [302, 532], [304, 548], [322, 555], [420, 555], [440, 562], [446, 578], [438, 592], [418, 600], [408, 612], [405, 627]], True),
    ("Gate Approach", 8, [[296, 440], [296, 487]], False),
    # Round the academic block, as on the campus model: the west side and the north side up to the
    # porte-cochere are asphalt (the through route from the serpentine to the spine road); from the
    # porch round the east and south sides is a cream paved drive with rounded corners.
    ("Academic West Drive", 6, [[405, 627], [405, 812]], False),
    ("Porch Drive", 6, [[405, 627], [497, 627]], False),
    ("Academic Ring", 6, rounded([[497, 627], [555, 627], [555, 686], [563, 694], [563, 732], [555, 740], [555, 812], [405, 812]],
                                 {1: 14, 2: 5, 3: 5, 4: 5, 5: 5, 6: 14}), False),
    ("Spine Road North", 8, [[405, 812], [405, 988]], False),
    ("Spine Road South", 8, [[421, 1004], [421, 1545]], False),
    ("Central East-West Road", 6, [[294, 990], [350, 990], [406, 995], [500, 985], [600, 972], [690, 962]], True),
    ("Southern East-West Road", 7, [[250, 1366], [330, 1366], [413, 1364], [500, 1362], [535, 1355], [562, 1338], [574, 1318]], True),
    ("Track Road", 5, [[600, 972], [582, 1030], [562, 1075], [545, 1105], [548, 1140], [572, 1165], [584, 1215], [584, 1280], [574, 1318]], True),
    ("Sindhu Back Road", 5, [[421, 1545], [468, 1546], [512, 1541]], True),
    ("Oval Road West", 5, [[294, 990], [291, 1060], [293, 1140], [300, 1160]], True),
    ("Walled Residence Drive", 4, [[405, 936], [392, 936]], False),
    ("Brahmaputra Drive", 4, [[405, 700], [392, 702], [384, 740], [388, 782]], True),
        ("Clock Tower Walkway", 4, [[486, 523], [486, 613]], False),
    # Cross-shaped garden paths in the academic courtyard (campus model).
    ("Courtyard Path North-South", 2.5, [[489, 680], [489, 750]], False),
    ("Courtyard Path East-West", 2.5, [[467, 715], [511, 715]], False),
    ("Courtyard Path Loop", 2.0, [[475, 694], [503, 694], [503, 736], [475, 736], [475, 694]], False),
    ("Courtyard Centre Square", 8.0, [[485, 715], [493, 715]], False),
    # Paved walks (Phase 4D): along the shop fronts of the shopping and medical blocks and on to
    # Annapurna Mess's west door, and from the spine road to the sports complex lobby.
    ("Plaza Shops Walk", 5, [[356, 992], [356, 1100], [367, 1100]], False),
    ("Plaza Sports Walk", 4, [[425, 1342], [437, 1342]], False),
    ("Highway", 13, [[-10, 575], [80, 545], [180, 500], [260, 455], [330, 430], [420, 412], [560, 400], [700, 392], [940, 385]], True),
]

# ---------------------------------------------------------------- flat areas
# (type, shape, map x, map y, width, depth)  x, y = top-left for box, centre for ellipse
AREAS = [
    ("lawn", "box", 322, 493, 175, 24),
    ("lawn", "box", 322, 529, 110, 21),
    ("lawn", "box", 430, 560, 40, 60),
    ("lawn", "box", 293, 908, 100, 72),
    ("lawn", "box", 460, 1409, 51, 88),
    ("court", "box", 480, 1097, 39, 20),
    ("courtyard", "box", 465, 678, 48, 74),
    ("lawn", "ellipse", 312, 1056, 22, 100),
    ("parking", "box", 379, 840, 20, 50),
    ("substation", "box", 247, 618, 30, 44),
    ("court", "box", 440, 1207, 26, 28),
    ("court", "box", 440, 1237, 26, 28),
    ("court", "box", 440, 1267, 26, 26),
    ("track", "ellipse", 517, 1261, 96, 170),
    ("field", "ellipse", 517, 1261, 74, 146),
    ("amphitheatre", "box", 565, 688, 64, 114),
    ("roundabout", "ellipse", 296, 487, 16, 16),
    ("roundabout", "ellipse", 414, 996, 18, 18),
    ("roundabout", "ellipse", 421, 1364, 14, 14),
]

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
    ("Medical Block", 343, 1023),
    ("Shopping Block", 343, 1068),
    ("Annapurna Mess (Dining Block)", 392, 1108),
    ("Staff Residences", 278, 1080),
    ("Hostel Blocks", 498, 1098),
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
TREE_ROWS = [(0, 7.5, 4.5, _pts) for _pts in TERRACE_LINES[:4]] + [(0, 7.5, -3.5, TERRACE_LINES[4])]
# Courtyard garden: a tree in each corner of the eight planted beds round the centre square.
TREE_SPOTS = [(0, x, y) for x in (471, 480, 498, 507) for y in (686, 702, 728, 744)]

# Boundary wall polygon from the map
BOUNDARY = [[282, 468], [560, 468], [590, 600], [645, 690], [645, 830], [640, 1000], [640, 1200], [604, 1380], [560, 1565], [380, 1565], [332, 1440], [262, 1300], [262, 1000], [290, 860], [296, 700], [278, 560]]

SPAWN = (392, 845)  # outside Brahmaputra Hostel, facing the academic block


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
    for name, width, pts, smooth in ROADS:
        p = catmull(pts) if smooth else pts
        layout["roads"].append({"name": name, "width": width, "points": flat(p)})
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
