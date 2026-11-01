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
VERSION = 2


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
    ("Main Gate", "gate", 286, 457, 22, 9, 2, "box"),
    ("Clock Tower", "gate", 475.5, 543.5, 3, 3, 7, "box"),
    # Academic block: four wings around the courtyard, entrance, two drums
    ("Academic Block - North Wing", "academic", 444, 646, 99, 32, 4, "box"),
    ("Academic Block - South Wing", "academic", 444, 752, 99, 38, 4, "box"),
    ("Academic Block - West Wing", "academic", 444, 678, 21, 74, 4, "box"),
    ("Academic Block - East Wing", "academic", 513, 678, 30, 74, 4, "box"),
    ("Academic Block - Entrance", "academic", 477, 634, 21, 13, 3, "box"),
    ("Mandapam Auditorium (west drum)", "academic", 423, 692, 40, 40, 3, "cylinder"),
    ("East Drum", "academic", 525, 696, 34, 34, 3, "cylinder"),
    ("Brahmaputra Hostel", "academic", 378, 782, 22, 26, 7, "box"),
    ("Brahmaputra Hostel - South", "academic", 371, 806, 25, 30, 7, "box"),
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
    ("Sindhu Hostel - North", "sindhu", 466, 1379, 36, 30, 7, "box"),
    ("Sindhu Hostel - Step 1", "sindhu", 433, 1410, 50, 27, 7, "box"),
    ("Sindhu Hostel - West", "sindhu", 434, 1437, 28, 40, 7, "box"),
    ("Sindhu Hostel - Step 2", "sindhu", 437, 1477, 46, 24, 7, "box"),
    ("Sindhu Hostel - South", "sindhu", 471, 1499, 38, 38, 7, "box"),
    ("Service Shed 1", "sindhu", 388, 1490, 26, 13, 1, "box"),
    ("Service Shed 2", "sindhu", 392, 1512, 24, 12, 1, "box"),
    ("Service Shed 3", "sindhu", 395, 1531, 22, 12, 1, "box"),
    ("West Building 1", "sindhu", 289, 1332, 30, 16, 2, "box"),
    ("West Building 2", "sindhu", 294, 1414, 28, 14, 2, "box"),
    ("West Building 3", "sindhu", 325, 1418, 22, 13, 2, "box"),
    ("West Building 4", "hostels", 300, 1232, 26, 16, 2, "box"),
    ("Substation Control Room", "gate", 250, 668, 24, 40, 1, "box"),
]

# ---------------------------------------------------------------- roads
# (name, width m, points, smooth)
ROADS = [
    ("Serpentine Drive", 7, [[296, 487], [380, 487], [480, 487], [505, 490], [513, 505], [503, 522], [400, 523], [318, 523], [302, 532], [304, 548], [322, 555], [420, 555], [440, 562], [446, 578], [438, 592], [418, 600], [408, 612], [405, 627]], True),
    ("Gate Approach", 8, [[296, 440], [296, 487]], False),
    ("Academic Ring", 6, [[405, 627], [405, 812], [555, 812], [555, 627], [405, 627]], False),
    ("Spine Road North", 8, [[405, 812], [405, 988]], False),
    ("Spine Road South", 8, [[421, 1004], [421, 1545]], False),
    ("Central East-West Road", 6, [[294, 990], [350, 990], [406, 995], [500, 985], [600, 972], [690, 962]], True),
    ("Southern East-West Road", 7, [[250, 1366], [330, 1366], [413, 1364], [500, 1362], [535, 1355], [562, 1338], [574, 1318]], True),
    ("Track Road", 5, [[600, 972], [582, 1030], [562, 1075], [545, 1105], [548, 1140], [572, 1165], [584, 1215], [584, 1280], [574, 1318]], True),
    ("Sindhu Back Road", 5, [[421, 1545], [468, 1546], [512, 1541]], True),
    ("Oval Road West", 5, [[294, 990], [291, 1060], [293, 1140], [300, 1160]], True),
    ("Walled Residence Drive", 4, [[405, 936], [392, 936]], False),
    ("Brahmaputra Drive", 4, [[405, 700], [392, 702], [384, 740], [388, 782]], True),
    ("Amphitheatre Path", 3, [[555, 738], [562, 738]], False),
    ("Clock Tower Walkway", 4, [[477, 523], [477, 630]], False),
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
    ("lawn", "box", 478, 1096, 43, 22),
    ("courtyard", "box", 465, 678, 48, 74),
    ("lawn", "ellipse", 312, 1056, 22, 100),
    ("parking", "box", 379, 840, 20, 50),
    ("substation", "box", 247, 618, 30, 44),
    ("court", "box", 440, 1207, 26, 28),
    ("court", "box", 440, 1237, 26, 28),
    ("court", "box", 440, 1267, 26, 26),
    ("track", "ellipse", 517, 1261, 96, 170),
    ("field", "ellipse", 517, 1261, 74, 146),
    ("amphitheatre", "ellipse", 566, 738, 70, 104),
    ("roundabout", "ellipse", 296, 487, 16, 16),
    ("roundabout", "ellipse", 414, 996, 18, 18),
    ("roundabout", "ellipse", 421, 1364, 14, 14),
]

# ---------------------------------------------------------------- places (labels, fast travel)
PLACES = [
    ("Main Gate", 296, 470),
    ("Clock Tower", 477, 545),
    ("Academic Block", 489, 708),
    ("Mandapam Auditorium", 440, 712),
    ("Amphitheatre", 588, 738),
    ("Brahmaputra Hostel", 386, 808),
    ("Car Park", 389, 865),
    ("Power Substation", 262, 640),
    ("Walled Residence", 359, 936),
    ("Shopping & Medical", 343, 1055),
    ("Annapurna Mess", 392, 1108),
    ("Staff Quarters", 278, 1080),
    ("Hostel Blocks", 498, 1098),
    ("Sports Courts", 453, 1237),
    ("Sports Complex", 450, 1324),
    ("Athletics Track", 517, 1261),
    ("Sindhu Hostel", 448, 1442),
    ("Service Yard", 401, 1515),
]

# Boundary wall polygon from the map
BOUNDARY = [[282, 468], [560, 468], [590, 600], [630, 700], [630, 830], [640, 1000], [640, 1200], [604, 1380], [560, 1565], [380, 1565], [332, 1440], [262, 1300], [262, 1000], [296, 850], [322, 700], [278, 560]]

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
