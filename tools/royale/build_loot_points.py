"""Loot spawn points for the battle royale mode.

Reads Assets/Data/interiors.json (rooms and furniture) and Assets/Data/campus_layout.json
(buildings and areas) and writes Assets/Data/loot_points.json:

  indoor points  - free floor spots in each furnished room, clear of furniture and walls,
                   with a loot table chosen from the room's kind (pharmacy -> medical, ...)
  outdoor points - spots just outside every building and on parking lots, courts and lawns

Stashes are lockable trunks (opened with E, three better items inside): one spot against a wall in
some rooms, chosen by STASH_ROOMS, and a few outside buildings. They are written under "stashes".

Each point carries a spawn chance; MatchDirector rolls every point at the start of a match,
so loot is different each time. Unity snaps each point onto the floor (or ground) when the
scene is built (RoyaleBuilder), so heights here are only the storey's floor line.

Run after tools/blender/build_interiors.py (and after layout changes):
    python3 tools/royale/build_loot_points.py [--preview]
--preview writes tools/royale/preview/loot_points.png (needs numpy and Pillow).

Prop footprints come from tools/royale/prop_sizes.json (measured from the furniture FBX files
with tools/royale/measure_props.py; rerun that when furniture models change).
"""
import json
import math
import os
import random
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
INTERIORS = os.path.join(ROOT, "Assets", "Data", "interiors.json")
LAYOUT = os.path.join(ROOT, "Assets", "Data", "campus_layout.json")
SIZES = os.path.join(ROOT, "tools", "royale", "prop_sizes.json")
OUT = os.path.join(ROOT, "Assets", "Data", "loot_points.json")

CELL = 0.5          # floor grid used to look for free spots
WALL_GAP = 0.45     # keep loot this far from the room's walls
PROP_GAP = 0.3      # and this far from furniture
MIN_SPACING = 2.2   # between two loot points in one room

# Room kind -> (loot table, points per 25 m2, spawn chance). Kinds not listed get no loot.
ROOM_LOOT = {
    # hostels
    "single": ("residential", 0.0, 0.32),   # one point per room, rolled at a low chance
    "double": ("residential", 0.0, 0.45),
    "common": ("residential", 1.0, 0.7),
    "study": ("residential", 1.0, 0.6),
    "laundry": ("residential", 0.8, 0.55),
    "lounge": ("residential", 1.0, 0.7),
    "warden": ("office", 1.4, 0.9),
    # academic block
    "classroom": ("academic", 0.6, 0.65),
    "seminar": ("academic", 0.7, 0.65),
    "syndicate": ("academic", 0.9, 0.6),
    "case": ("academic", 0.45, 0.65),
    "reading": ("academic", 0.6, 0.6),
    "stacks": ("academic", 0.5, 0.6),
    "complab": ("academic", 0.7, 0.7),
    "research": ("academic", 0.9, 0.65),
    "meeting": ("office", 1.0, 0.7),
    "cabin": ("office", 1.4, 0.55),
    "admin": ("office", 1.0, 0.8),
    "board": ("office", 0.8, 0.9),
    "director": ("office", 1.4, 1.0),
    "server": ("office", 1.4, 0.9),
    "staff": ("office", 1.0, 0.7),
    "lobby": ("academic", 0.35, 0.6),
    "foyer": ("academic", 0.35, 0.6),
    "gallery": ("academic", 0.35, 0.55),
    "activity": ("sports", 0.8, 0.7),
    "store": ("storage", 1.2, 0.85),
    # food
    "kitchen": ("food", 1.0, 0.9),
    "mess_hall": ("food", 0.35, 0.8),
    "pantry": ("food", 1.4, 0.7),
    "cafe": ("food", 0.8, 0.85),
    # medical centre
    "pharmacy": ("medical", 1.6, 1.0),
    "ward": ("medical", 0.8, 0.9),
    "consult": ("medical", 1.2, 0.9),
    "nurses": ("medical", 1.4, 0.9),
    "physio": ("medical", 1.0, 0.85),
    "observation": ("medical", 1.0, 0.85),
    "records": ("office", 1.0, 0.7),
    "linen": ("storage", 1.4, 0.8),
    "waiting": ("medical", 0.5, 0.7),
    "duty": ("office", 1.4, 0.9),
    "officer": ("office", 1.4, 0.9),
    # shops
    "stationery": ("academic", 1.0, 0.8),
    "repair": ("storage", 1.2, 0.8),
    "bookshop": ("academic", 0.8, 0.75),
    "atm": ("office", 1.4, 0.8),
    "bank": ("vault", 1.0, 1.0),
    "vault": ("vault", 2.0, 1.0),
    "dressing": ("residential", 1.0, 0.6),
    # sports complex
    "games": ("sports", 0.6, 0.7),
    "changing": ("sports", 0.9, 0.8),
}

# Room kind -> chance that the room gets a stash spot at all (the spot itself spawns at STASH_CHANCE).
STASH_ROOMS = {
    "single": 0.035, "double": 0.12, "common": 0.5, "study": 0.35, "lounge": 0.5, "laundry": 0.25,
    "warden": 0.8, "classroom": 0.25, "seminar": 0.35, "syndicate": 0.15, "case": 0.3, "reading": 0.4,
    "stacks": 0.6, "complab": 0.5, "research": 0.4, "meeting": 0.5, "cabin": 0.25, "admin": 0.7,
    "board": 1.0, "director": 1.0, "server": 0.8, "staff": 0.6, "activity": 0.6, "store": 0.45,
    "kitchen": 0.7, "pantry": 0.12, "cafe": 0.6, "mess_hall": 1.0, "pharmacy": 1.0, "ward": 0.8,
    "consult": 0.5, "nurses": 0.7, "physio": 0.6, "observation": 0.5, "records": 0.8, "linen": 0.8,
    "duty": 0.8, "officer": 0.8, "stationery": 0.8, "repair": 0.8, "bookshop": 0.8, "bank": 1.0,
    "vault": 1.0, "games": 0.25, "changing": 0.7,
}
STASH_CHANCE = 0.6          # each stash spot is filled in about this share of matches
STASH_HALF = (0.55, 0.32)   # trunk footprint half sizes (along its front, front to back), with a little room
STASH_WALL = 0.5            # gap between the trunk's back and the wall


# Outdoor loot table for the building, by zone and name.
def outdoor_table(b):
    n = b["name"].lower()
    if "gate" in n or "guard" in n or "security" in n:
        return "office"
    if "medical" in n:
        return "medical"
    if "mess" in n or "canteen" in n or "food" in n:
        return "food"
    if "sports" in n or "court" in n:
        return "sports"
    if "shed" in n or "substation" in n or "store" in n or "pump" in n:
        return "storage"
    return "outdoor"


def rect_of(room):
    x0, x1, z0, z1 = room["rect"]
    return min(x0, x1), max(x0, x1), min(z0, z1), max(z0, z1)


def footprint_blocks(prop, size, x, z):
    """True when the floor cell (x, z) lies under the prop or within PROP_GAP of it."""
    w, d = size[0] / 2.0 + PROP_GAP, size[1] / 2.0 + PROP_GAP
    a = math.radians(prop["yaw"])
    dx, dz = x - prop["x"], z - prop["z"]
    # The prop's yaw turns it clockwise seen from above (Unity), so undo that.
    lx = dx * math.cos(a) - dz * math.sin(a)
    lz = dx * math.sin(a) + dz * math.cos(a)
    return abs(lx) <= w and abs(lz) <= d


def free_cells(room, props, sizes, floor_y):
    x0, x1, z0, z1 = rect_of(room)
    cells = []
    nearby = [p for p in props
              if p["prop"] in sizes
              and p["y"] <= floor_y + 1.0          # not ceiling lights, fans or wall panels
              and x0 - 3 < p["x"] < x1 + 3 and z0 - 3 < p["z"] < z1 + 3]
    nx = int((x1 - x0) / CELL)
    nz = int((z1 - z0) / CELL)
    for i in range(nx + 1):
        for k in range(nz + 1):
            x = x0 + i * CELL
            z = z0 + k * CELL
            if x < x0 + WALL_GAP or x > x1 - WALL_GAP or z < z0 + WALL_GAP or z > z1 - WALL_GAP:
                continue
            if any(footprint_blocks(p, sizes[p["prop"]], x, z) for p in nearby):
                continue
            cells.append((x, z))
    return cells


def pick(cells, count, rng, rect):
    """Spread-out picks: prefer cells far from earlier picks and from the room's centre line."""
    chosen = []
    pool = cells[:]
    rng.shuffle(pool)
    for _ in range(count):
        best, best_score = None, -1.0
        for c in pool[:60]:
            if chosen:
                d = min(math.hypot(c[0] - q[0], c[1] - q[1]) for q in chosen)
                if d < MIN_SPACING:
                    continue
            else:
                d = 5.0
            score = min(d, 6.0) + rng.random() * 0.5
            if score > best_score:
                best, best_score = c, score
        if best is None:
            break
        chosen.append(best)
        pool.remove(best)
    return chosen


def indoor_points(interiors, sizes, rng):
    zones = interiors["zones"]
    level_h = interiors.get("levelHeight", 3.4)
    by_zone_level = {}
    for p in interiors["props"]:
        by_zone_level.setdefault((p["zone"], p["level"]), []).append(p)
    points = []
    for room in interiors["rooms"]:
        spec = ROOM_LOOT.get(room["kind"])
        if spec is None:
            continue
        table, density, chance = spec
        zone = zones[room["zone"]]
        floor_y = zone.get("floorOffset", 0.6) + level_h * room["level"]
        x0, x1, z0, z1 = rect_of(room)
        area = (x1 - x0) * (z1 - z0)
        count = 1 if density <= 0 else max(1, min(6, int(round(area / 25.0 * density))))
        props = by_zone_level.get((room["zone"], room["level"]), [])
        cells = free_cells(room, props, sizes, floor_y)
        if not cells:
            continue
        for (x, z) in pick(cells, count, rng, (x0, x1, z0, z1)):
            points.append({
                "x": round(x, 2), "y": round(floor_y, 2), "z": round(z, 2),
                "zone": room["zone"], "level": room["level"],
                "table": table, "chance": chance,
                "room": room["name"],
            })
    return points


def stash_fits(x, z, yaw, room_rect, props, sizes, taken):
    """True when a trunk at (x, z) facing yaw, and the floor in front where its items land, are clear."""
    x0, x1, z0, z1 = room_rect
    a = math.radians(yaw)
    fx, fz = math.sin(a), math.cos(a)          # front
    rx, rz = math.cos(a), -math.sin(a)         # right
    hw, hd = STASH_HALF
    samples = [(-hw, -hd), (hw, -hd), (-hw, hd), (hw, hd), (0, 0), (0, 1.1), (-0.6, 1.0), (0.6, 1.0)]
    for (u, v) in samples:
        px, pz = x + rx * u + fx * v, z + rz * u + fz * v
        if not (x0 + 0.12 < px < x1 - 0.12 and z0 + 0.12 < pz < z1 - 0.12):
            return False
        if any(footprint_blocks(p, sizes[p["prop"]], px, pz) for p in props):
            return False
    return all(math.hypot(x - q[0], z - q[1]) > 1.6 for q in taken)


def indoor_stashes(interiors, sizes, points, rng):
    zones = interiors["zones"]
    level_h = interiors.get("levelHeight", 3.4)
    by_zone_level = {}
    for p in interiors["props"]:
        by_zone_level.setdefault((p["zone"], p["level"]), []).append(p)
    taken_by_room = {}
    for p in points:
        taken_by_room.setdefault((p["zone"], p["level"], p["room"]), []).append((p["x"], p["z"]))
    stashes = []
    for room in interiors["rooms"]:
        want = STASH_ROOMS.get(room["kind"])
        spec = ROOM_LOOT.get(room["kind"])
        if want is None or spec is None or rng.random() > want:
            continue
        x0, x1, z0, z1 = rect_of(room)
        if min(x1 - x0, z1 - z0) < 2.6:
            continue
        zone = zones[room["zone"]]
        floor_y = zone.get("floorOffset", 0.6) + level_h * room["level"]
        props = [p for p in by_zone_level.get((room["zone"], room["level"]), [])
                 if p["prop"] in sizes and p["y"] <= floor_y + 1.0
                 and x0 - 3 < p["x"] < x1 + 3 and z0 - 3 < p["z"] < z1 + 3]
        taken = taken_by_room.get((room["zone"], room["level"], room["name"]), [])
        back = STASH_HALF[1] + STASH_WALL
        candidates = []
        # Along each wall, the trunk's back to the wall and its front facing into the room.
        for t in [i * 0.5 for i in range(int(max(x1 - x0, z1 - z0) / 0.5) + 1)]:
            if x0 + 0.8 + t < x1 - 0.8:
                candidates.append((x0 + 0.8 + t, z0 + back, 0.0))
                candidates.append((x0 + 0.8 + t, z1 - back, 180.0))
            if z0 + 0.8 + t < z1 - 0.8:
                candidates.append((x0 + back, z0 + 0.8 + t, 90.0))
                candidates.append((x1 - back, z0 + 0.8 + t, 270.0))
        rng.shuffle(candidates)
        for (x, z, yaw) in candidates:
            if stash_fits(x, z, yaw, (x0, x1, z0, z1), props, sizes, taken):
                stashes.append({"x": round(x, 2), "y": round(floor_y, 2), "z": round(z, 2), "yaw": yaw,
                                "zone": room["zone"], "level": room["level"], "table": spec[0],
                                "chance": STASH_CHANCE, "room": room["name"]})
                break
    return stashes


def outdoor_stashes(layout, rng):
    """A trunk against the wall of about a third of the buildings, facing out."""
    buildings = layout["buildings"]
    poly = list(zip(layout["boundary"][0::2], layout["boundary"][1::2]))
    stashes = []
    for b in buildings:
        if rng.random() > 0.35 or b["width"] * b["depth"] < 60:
            continue
        for _ in range(12):
            side = rng.randrange(4)
            along = rng.uniform(-0.35, 0.35)
            gap = 0.75
            x, z = b["x"], b["z"]
            if side == 0:
                x += along * b["width"]; z += b["depth"] / 2 + gap; yaw = 0.0
            elif side == 1:
                x += along * b["width"]; z -= b["depth"] / 2 + gap; yaw = 180.0
            elif side == 2:
                z += along * b["depth"]; x += b["width"] / 2 + gap; yaw = 90.0
            else:
                z += along * b["depth"]; x -= b["width"] / 2 + gap; yaw = 270.0
            a = math.radians(yaw)
            front = (x + math.sin(a) * 1.4, z + math.cos(a) * 1.4)
            if not inside_boundary(poly, x, z) or any(inside_box(o, px, pz, 0.4) for o in buildings
                                                       for (px, pz) in ((x, z), front)):
                continue
            stashes.append({"x": round(x, 2), "y": 0.0, "z": round(z, 2), "yaw": yaw, "zone": -1, "level": 0,
                            "table": outdoor_table(b), "chance": STASH_CHANCE, "room": b["name"] + " (outside)"})
            break
    return stashes


def inside_box(b, x, z, margin):
    return abs(x - b["x"]) < b["width"] / 2.0 + margin and abs(z - b["z"]) < b["depth"] / 2.0 + margin


def inside_boundary(poly, x, z):
    inside = False
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        if (az > z) != (bz > z):
            t = (z - az) / (bz - az)
            if x < ax + t * (bx - ax):
                inside = not inside
    return inside


def outdoor_points(layout, rng):
    buildings = layout["buildings"]
    poly = list(zip(layout["boundary"][0::2], layout["boundary"][1::2]))
    points = []

    # The amphitheatre's seating fan and stepped pond, round a centre east of the stage (the
    # Amphitheatre_Pond model in build_buildings.py): loot there goes on the stage only.
    fan_x, fan_z = 610.0 - 414.0, 996.0 - 711.0

    def on_stage(x, z):
        r = math.hypot(x - fan_x, z - fan_z)
        off = abs(abs(math.degrees(math.atan2(z - fan_z, x - fan_x))) - 180.0)
        return 18.0 < r < 26.0 and off < 34.0

    def in_amphitheatre(x, z):
        return math.hypot(x - fan_x, z - fan_z) < 48.0 or (fan_x - 50 < x < fan_x + 35 and fan_z - 92 < z < fan_z + 25)

    def clear(x, z):
        if not inside_boundary(poly, x, z):
            return False
        if in_amphitheatre(x, z) and not on_stage(x, z):
            return False
        # The clock tower's plinth and the steps of its walk.
        if abs(x - 72.0) < 4.5 and 380.0 < z < 471.0 and not (abs(z - 451.0) < 7.5 and abs(x - 72.0) > 4.0):
            return False
        return not any(inside_box(o, x, z, 0.9) for o in buildings)

    for b in buildings:
        table = outdoor_table(b)
        perimeter = 2 * (b["width"] + b["depth"])
        count = max(3, min(10, int(perimeter / 32)))
        made = 0
        for _ in range(count * 8):
            if made >= count:
                break
            side = rng.randrange(4)
            along = rng.uniform(-0.45, 0.45)
            gap = rng.uniform(1.4, 3.5)
            x, z = b["x"], b["z"]
            if side == 0:
                x += along * b["width"]; z += b["depth"] / 2 + gap
            elif side == 1:
                x += along * b["width"]; z -= b["depth"] / 2 + gap
            elif side == 2:
                z += along * b["depth"]; x += b["width"] / 2 + gap
            else:
                z += along * b["depth"]; x -= b["width"] / 2 + gap
            if not clear(x, z):
                continue
            if any(math.hypot(x - q["x"], z - q["z"]) < 6 for q in points[-made:] if made):
                continue
            points.append({"x": round(x, 2), "y": 0.0, "z": round(z, 2), "zone": -1, "level": 0,
                           "table": table, "chance": 0.55, "room": b["name"] + " (outside)"})
            made += 1

    for a in layout["areas"]:
        if a["type"] not in ("parking", "court", "lawn", "courtyard", "field", "amphitheatre", "track"):
            continue
        n = 1 + int(a["width"] * a["depth"] / 1500)
        n = min(n, 5)
        for _ in range(n * 4):
            if n <= 0:
                break
            x = a["x"] + rng.uniform(-0.4, 0.4) * a["width"]
            z = a["z"] + rng.uniform(-0.4, 0.4) * a["depth"]
            if a["type"] == "amphitheatre":          # somewhere on the stage
                r, ang = rng.uniform(19.0, 25.0), math.radians(180.0 + rng.uniform(-30.0, 30.0))
                x, z = fan_x + r * math.cos(ang), fan_z + r * math.sin(ang)
            if not clear(x, z):
                continue
            points.append({"x": round(x, 2), "y": 0.0, "z": round(z, 2), "zone": -1, "level": 0,
                           "table": "outdoor", "chance": 0.45, "room": a["type"]})
            n -= 1
    return points


def preview(points, stashes, layout, interiors):
    import numpy as np
    from PIL import Image, ImageDraw
    xs = layout["boundary"][0::2]
    zs = layout["boundary"][1::2]
    x0, x1, z0, z1 = min(xs) - 20, max(xs) + 20, min(zs) - 20, max(zs) + 20
    s = 2.0
    W, H = int((x1 - x0) * s), int((z1 - z0) * s)
    img = Image.new("RGB", (W, H), (34, 40, 34))
    dr = ImageDraw.Draw(img)

    def P(x, z):
        return ((x - x0) * s, (z1 - z) * s)

    for b in layout["buildings"]:
        a = P(b["x"] - b["width"] / 2, b["z"] + b["depth"] / 2)
        c = P(b["x"] + b["width"] / 2, b["z"] - b["depth"] / 2)
        dr.rectangle([a, c], outline=(130, 130, 120))
    colours = {"residential": (90, 160, 255), "academic": (230, 230, 230), "office": (255, 170, 60),
               "food": (250, 230, 90), "medical": (255, 80, 80), "sports": (120, 230, 120),
               "storage": (180, 140, 100), "vault": (220, 90, 255), "outdoor": (150, 200, 200)}
    for p in points:
        x, y = P(p["x"], p["z"])
        dr.ellipse([x - 1.5, y - 1.5, x + 1.5, y + 1.5], fill=colours.get(p["table"], (255, 255, 255)))
    for p in stashes:
        x, y = P(p["x"], p["z"])
        dr.rectangle([x - 3.5, y - 3.5, x + 3.5, y + 3.5], fill=(255, 168, 46), outline=(0, 0, 0))
    # Legend.
    ly = 10
    for name, col in list(colours.items()) + [("stash trunk", (255, 168, 46))]:
        if name == "stash trunk":
            dr.rectangle([10, ly, 18, ly + 8], fill=col, outline=(0, 0, 0))
        else:
            dr.ellipse([10, ly, 18, ly + 8], fill=col)
        dr.text((24, ly - 2), name, fill=(235, 235, 235))
        ly += 14
    dr.text((10, ly + 4), "%d loot points, %d stash spots" % (len(points), len(stashes)), fill=(235, 235, 235))
    out = os.path.join(ROOT, "tools", "royale", "preview")
    os.makedirs(out, exist_ok=True)
    img.save(os.path.join(out, "loot_points.png"))
    print("preview written to", os.path.join(out, "loot_points.png"))


def main():
    interiors = json.load(open(INTERIORS))
    layout = json.load(open(LAYOUT))
    sizes = {k: v for k, v in json.load(open(SIZES)).items()}
    rng = random.Random(4242)
    indoor = indoor_points(interiors, sizes, rng)
    outdoor = outdoor_points(layout, rng)
    points = indoor + outdoor
    srng = random.Random(777)   # its own stream, so adding stashes moved no loot point
    stashes = indoor_stashes(interiors, sizes, points, srng) + outdoor_stashes(layout, srng)
    tables = {}
    for p in points:
        tables[p["table"]] = tables.get(p["table"], 0) + 1
    expected = sum(p["chance"] for p in points)
    data = {
        "info": "Generated by tools/royale/build_loot_points.py. Do not edit by hand.",
        "layoutVersion": layout.get("version", 0),
        "points": points,
        "stashes": stashes,
    }
    with open(OUT, "w") as f:
        json.dump(data, f, separators=(",", ":"))
    print("%d loot points (%d indoor, %d outdoor), about %d filled per match" % (
        len(points), len(indoor), len(outdoor), expected))
    print("by table:", dict(sorted(tables.items())))
    print("%d stash spots (%d outdoor), about %d filled per match" % (
        len(stashes), sum(1 for p in stashes if p["zone"] < 0), sum(p["chance"] for p in stashes)))
    if "--preview" in sys.argv:
        preview(points, stashes, layout, interiors)


if __name__ == "__main__":
    main()
