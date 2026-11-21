"""Mockups of the royale HUD and menus, drawn with Pillow.

Mirrors the drawing helpers in Assets/Scripts/Royale/Ui/HudKit.cs and the layouts in RoyaleHud*.cs
and NetLobbyMenu.cs (positions and sizes in the same 1080-high reference units), uses the same
fonts from Assets/Resources/Fonts and reads the icon shapes straight from HudIcons.cs. The match
state shown is made up. Unity is the real check: this is for judging the look without it.

Run: python tools/royale/preview_hud.py BACKGROUND.png [MAP.png]
Writes tools/royale/preview/hud_match.png, hud_menu.png, hud_results.png, hud_deathcam.png,
hud_spectate.png and hud_settings_game.png.
"""
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FONTS = os.path.join(ROOT, "Assets", "Resources", "Fonts")
ICONS_CS = os.path.join(ROOT, "Assets", "Scripts", "Royale", "Ui", "HudIcons.cs")
OUT = os.path.join(ROOT, "tools", "royale", "preview")

# Palette (HudKit.cs)
INK = (0.05, 0.06, 0.08, 0.62)
INK_DEEP = (0.035, 0.04, 0.05, 0.88)
LINE = (1, 1, 1, 0.14)
TEXT = (0.97, 0.96, 0.93, 1)
TEXT_DIM = (0.68, 0.71, 0.75, 1)
ACCENT = (1, 0.66, 0.18, 1)
HEALTH = (0.93, 0.96, 0.92, 1)
HEALTH_LOW = (1, 0.3, 0.24, 1)
SHIELD = (0.33, 0.72, 1, 1)
BOOST = (1, 0.8, 0.38, 1)
DANGER = (1, 0.3, 0.22, 1)
HAZE = (0.93, 0.56, 0.22, 1)
MARKER = (1, 0.85, 0.15, 1)
TIERS = [(0.78, 0.78, 0.76, 1), (0.36, 0.82, 0.38, 1), (0.25, 0.6, 1, 1), (0.95, 0.55, 0.15, 1)]
TIER_NAMES = ["COMMON", "UNCOMMON", "RARE", "EPIC"]


def wa(c, a):
    return (c[0], c[1], c[2], c[3] * a)


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(4))


# ---------------------------------------------------------------------------- icons (HudIcons.cs)

def icon_specs():
    src = open(ICONS_CS).read()
    block = src[src.index("Table ="):src.index("};", src.index("Table ="))]
    out = {}
    for m in re.finditer(r'((?:"[^"]*"\s*\+?\s*)+),', block):
        spec = "".join(re.findall(r'"([^"]*)"', m.group(1)))
        out[spec.split("|")[0]] = spec
    return out


SPECS = icon_specs()
_icon_cache = {}


def icon_mask(name, h):
    key = (name, h)
    if key in _icon_cache:
        return _icon_cache[key]
    _, size, ops = SPECS[name].split("|")
    cw, ch = map(float, size.split("x"))
    w = max(1, round(h * cw / ch))
    ss = 3
    ys, xs = np.mgrid[0:h * ss, 0:w * ss]
    u = (xs + 0.5) / (w * ss) * cw
    v = (ys + 0.5) / (h * ss) * ch
    on = np.zeros_like(u, bool)
    for s in ops.split(";"):
        s = s.strip()
        if not s:
            continue
        cut = s[0] == "-"
        if cut:
            s = s[1:]
        k, n = s[0], list(map(float, s[1:].split()))
        if k == "r":
            m = (u >= n[0]) & (u <= n[0] + n[2]) & (v >= n[1]) & (v <= n[1] + n[3])
        elif k == "o":
            m = (u - n[0]) ** 2 + (v - n[1]) ** 2 <= n[2] ** 2
        else:
            m = np.zeros_like(on)
            pts = list(zip(n[0::2], n[1::2]))
            for i in range(len(pts)):
                xi, yi = pts[i]
                xj, yj = pts[i - 1]
                with np.errstate(divide="ignore", invalid="ignore"):
                    m ^= ((yi > v) != (yj > v)) & (u < (xj - xi) * (v - yi) / (yj - yi + 1e-12) + xi)
        on = np.where(m, not cut, on)
    a = on.reshape(h, ss, w, ss).mean(axis=(1, 3))
    img = Image.fromarray((a * 255).astype("uint8"), "L")
    _icon_cache[key] = img
    return img


# ---------------------------------------------------------------------------- the kit

class Kit:
    def __init__(self, img, s):
        self.img = img
        self.S = s
        self.W = img.width / s
        self.H = img.height / s
        self.alpha = 1.0
        self._fonts = {}

    def col(self, c):
        return tuple(int(round(max(0, min(1, v)) * 255)) for v in c[:3]) + (int(round(max(0, min(1, c[3] * self.alpha)) * 255)),)

    def font(self, size, heavy):
        px = max(7, round(size * self.S))
        key = (px, heavy)
        if key not in self._fonts:
            self._fonts[key] = ImageFont.truetype(os.path.join(FONTS, "BarlowCondensed-%s.ttf" % ("700" if heavy else "500")), px)
        return self._fonts[key]

    def comp(self, layer, x, y):
        x, y = int(round(x)), int(round(y))
        # Clip to the canvas.
        if x >= self.img.width or y >= self.img.height or x + layer.width <= 0 or y + layer.height <= 0:
            return
        cx0, cy0 = max(0, -x), max(0, -y)
        layer = layer.crop((cx0, cy0, min(layer.width, self.img.width - x), min(layer.height, self.img.height - y)))
        self.img.alpha_composite(layer, (x + cx0, y + cy0))

    def shape(self, x, y, w, h, draw_fn):
        S = self.S
        px, py, pw, ph = x * S, y * S, max(1, int(math.ceil(w * S)) + 2), max(1, int(math.ceil(h * S)) + 2)
        layer = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
        draw_fn(ImageDraw.Draw(layer), w * S, h * S)
        self.comp(layer, px, py)

    def fill(self, x, y, w, h, c):
        if w <= 0 or h <= 0:
            return
        self.shape(x, y, w, h, lambda d, pw, ph: d.rectangle([0, 0, pw - 1, ph - 1], fill=self.col(c)))

    def panel(self, x, y, w, h, c, r=6):
        if w <= 0 or h <= 0:
            return
        rad = min(r, min(w, h) / 2) * self.S
        self.shape(x, y, w, h, lambda d, pw, ph: d.rounded_rectangle([0, 0, pw - 1, ph - 1], radius=rad, fill=self.col(c)))

    def outline(self, x, y, w, h, c, t=1.5, r=6):
        rad = min(r, min(w, h) / 2) * self.S
        self.shape(x, y, w, h, lambda d, pw, ph: d.rounded_rectangle([0, 0, pw - 1, ph - 1], radius=rad, outline=self.col(c), width=max(1, round(t * self.S))))

    def fade_panel(self, x, y, w, h, c, down=True):
        S = self.S
        pw, ph = max(1, int(w * S)), max(1, int(h * S))
        t = np.linspace(0, 1, ph)[:, None]
        a = (1 - t) ** 2 if down else t ** 2
        arr = np.zeros((ph, pw, 4), np.uint8)
        cc = self.col(c)
        arr[..., 0], arr[..., 1], arr[..., 2] = cc[0], cc[1], cc[2]
        arr[..., 3] = (a * cc[3]).astype(np.uint8)
        self.comp(Image.fromarray(arr, "RGBA"), x * S, y * S)

    def fade_side(self, x, y, w, h, c, left=False):
        S = self.S
        pw, ph = max(1, int(w * S)), max(1, int(h * S))
        t = np.linspace(0, 1, pw)[None, :]
        a = t ** 2 if left else (1 - t) ** 2
        arr = np.zeros((ph, pw, 4), np.uint8)
        cc = self.col(c)
        arr[..., 0], arr[..., 1], arr[..., 2] = cc[0], cc[1], cc[2]
        arr[..., 3] = np.repeat(a * cc[3], ph, axis=0).astype(np.uint8)
        self.comp(Image.fromarray(arr, "RGBA"), x * S, y * S)

    def dot(self, cx, cy, size, c):
        self.shape(cx - size / 2, cy - size / 2, size, size, lambda d, pw, ph: d.ellipse([0, 0, pw - 1, ph - 1], fill=self.col(c)))

    def circle(self, cx, cy, r, c, line=2.6):
        self.shape(cx - r, cy - r, 2 * r, 2 * r, lambda d, pw, ph: d.ellipse([0, 0, pw - 1, ph - 1], outline=self.col(c), width=max(1, round(line * self.S / 2))))

    def icon(self, name, x, y, h, c, angle=0.0):
        hp = max(2, int(round(h * self.S)))
        m = icon_mask(name, hp)
        cc = self.col(c)
        layer = Image.new("RGBA", m.size, cc[:3] + (0,))
        layer.putalpha(m.point(lambda v: v * cc[3] // 255))
        if angle:
            cxp, cyp = x * self.S + m.width / 2, y * self.S + m.height / 2
            layer = layer.rotate(-angle, resample=Image.BICUBIC, expand=True)
            self.comp(layer, cxp - layer.width / 2, cyp - layer.height / 2)
        else:
            self.comp(layer, x * self.S, y * self.S)

    def icon_at(self, name, cx, cy, h, c, angle=0.0):
        _, size, _ = SPECS[name].split("|")
        cw, ch = map(float, size.split("x"))
        self.icon(name, cx - h * cw / ch / 2, cy - h / 2, h, c, angle)

    def text_width(self, text, size, heavy=False):
        if not text:
            return 0
        return self.font(size, heavy).getlength(text) / self.S

    def label(self, x, y, w, h, text, size, c, anchor="L", heavy=False, shadow=True):
        if not text:
            return
        f = self.font(size, heavy)
        S = self.S
        tx = {"L": x, "C": x + w / 2, "R": x + w}[anchor] * S
        ty = (y + h / 2) * S
        a = {"L": "lm", "C": "mm", "R": "rm"}[anchor]
        tw = int(f.getlength(text)) + 8
        pad = int(size * S * 1.4) + 6
        layer = Image.new("RGBA", (tw + 16, pad * 2), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        ox = {"L": 4, "C": (tw + 16) / 2, "R": tw + 12}[anchor]
        if shadow:
            o = max(1, S * 1.2)
            d.text((ox + o * 0.6, pad + o), text, font=f, anchor=a, fill=(0, 0, 0, int(140 * c[3] * self.alpha)))
        d.text((ox, pad), text, font=f, anchor=a, fill=self.col(c))
        self.comp(layer, tx - ox, ty - pad)

    def paragraph(self, x, y, w, h, text, size, c):
        f = self.font(size, False)
        words, lines, cur = text.split(), [], ""
        for wd in words:
            t = (cur + " " + wd).strip()
            if f.getlength(t) / self.S > w and cur:
                lines.append(cur)
                cur = wd
            else:
                cur = t
        lines.append(cur)
        for i, ln in enumerate(lines):
            self.label(x, y + i * size * 1.2, w, size * 1.2, ln, size, c, "L", False, False)

    def segments(self, x, y, w, h, n):
        for i in range(1, n):
            self.fill(x + w * i / n - 1, y, 2, h, (0.02, 0.03, 0.04, 0.8))

    def bar(self, x, y, w, h, frac, c, segs=0, r=2):
        self.panel(x, y, w, h, (0, 0, 0, 0.5), r)
        if frac > 0.001:
            self.panel(x, y, max(h * 0.6, w * frac), h, c, r)
        self.segments(x, y, w, h, segs)

    def ring(self, cx, cy, radius, thick, frm, frac, c):
        n = max(2, min(140, int(math.ceil(2 * math.pi * radius * frac / (thick * 0.45)))))
        for i in range(n + 1):
            a = math.radians(frm + 360 * frac * i / n)
            self.dot(cx + math.sin(a) * radius, cy - math.cos(a) * radius, thick, c)

    def keycap(self, x, y, key, h=24):
        size = h * 0.68
        w = max(h, self.text_width(key, size, True) + h * 0.55)
        self.panel(x, y, w, h, (0.95, 0.95, 0.92, 0.92), 4)
        self.panel(x, y + h - 3, w, 3, (0, 0, 0, 0.18), 2)
        self.label(x, y - 1, w, h, key, size, (0.08, 0.09, 0.11, 1), "C", True, False)
        return w

    def hints(self, cx, y, h, *pairs):
        size = h * 0.72
        total = 0
        for i in range(0, len(pairs) - 1, 2):
            total += max(h, self.text_width(pairs[i], h * 0.68, True) + h * 0.55) + 8 + self.text_width(pairs[i + 1], size) + 26
        x = cx - (total - 26) / 2
        for i in range(0, len(pairs) - 1, 2):
            x += self.keycap(x, y, pairs[i], h) + 8
            self.label(x, y, 400, h, pairs[i + 1], size, TEXT)
            x += self.text_width(pairs[i + 1], size) + 26

    def hit_arc(self, cx, cy, dist, deg, c):
        S = self.S
        w, h = 150, 56
        pw, ph = int(w * S), int(h * S)
        ys, xs = np.mgrid[0:ph, 0:pw]
        # texture rows run up in Unity; here y runs down, so the ring centre sits below the band
        tx = xs / pw * 128
        ty = (ph - 1 - ys) / ph * 48
        dx, dy = tx - 64, ty + 150
        d = np.sqrt(dx * dx + dy * dy)
        band = np.clip(7 - np.abs(d - 180), 0, 1)
        ang = np.abs(np.degrees(np.arctan2(dx, dy)))
        ends = np.clip((19 - ang) / 6, 0, 1)
        glow = np.clip(1 - np.abs(d - 180) / 20, 0, 1) * 0.35
        a = np.maximum(band, glow) * ends
        cc = self.col(c)
        arr = np.zeros((ph, pw, 4), np.uint8)
        arr[..., 0], arr[..., 1], arr[..., 2] = cc[0], cc[1], cc[2]
        arr[..., 3] = (a * cc[3]).astype(np.uint8)
        layer = Image.fromarray(arr, "RGBA")
        big = Image.new("RGBA", (int(2 * (dist + h) * S), int(2 * (dist + h) * S)), (0, 0, 0, 0))
        big.alpha_composite(layer, (int(big.width / 2 - pw / 2), int(big.height / 2 - (dist + h * 0.6) * S)))
        big = big.rotate(-deg, resample=Image.BICUBIC)
        self.comp(big, cx * S - big.width / 2, cy * S - big.height / 2)

    def edge_vignette(self, c):
        S = self.S
        pw, ph = self.img.width, self.img.height
        ys, xs = np.mgrid[0:ph, 0:pw]
        u, v = xs / pw * 2 - 1, ys / ph * 2 - 1
        d = np.sqrt(u * u * 0.8 + v * v)
        a = np.clip((d - 0.5) / 0.75, 0, 1) ** 1.6
        cc = self.col(c)
        arr = np.zeros((ph, pw, 4), np.uint8)
        arr[..., 0], arr[..., 1], arr[..., 2] = cc[0], cc[1], cc[2]
        arr[..., 3] = (a * cc[3]).astype(np.uint8)
        self.comp(Image.fromarray(arr, "RGBA"), 0, 0)

    # menu widgets
    def button(self, x, y, w, h, text, kind="normal", enabled=True, sub=None, hover=False):
        a = 1 if enabled else 0.45
        if kind == "primary":
            back, edge, t1 = ((1, 0.74, 0.3, 1) if hover else ACCENT), (1, 0.9, 0.6, 0.9 if hover else 0), (0.09, 0.07, 0.04, 1)
        elif kind == "tabon":
            back, edge, t1 = (1, 1, 1, 0.12), (0, 0, 0, 0), TEXT
        elif kind in ("tab", "quiet"):
            back, edge, t1 = ((1, 1, 1, 0.08) if hover else (0, 0, 0, 0)), (0, 0, 0, 0), (TEXT if hover else TEXT_DIM)
        else:
            back = (0.24, 0.27, 0.31, 0.95) if hover else (0.13, 0.15, 0.18, 0.9)
            edge, t1 = (wa(ACCENT, 0.9) if hover else LINE), TEXT
        self.alpha *= a
        self.panel(x, y, w, h, back, 5)
        if edge[3] > 0:
            self.outline(x, y, w, h, edge, 1.5, 5)
        if kind == "tabon":
            self.panel(x, y + 6, 4, h - 12, ACCENT, 2)
        left = kind in ("tab", "tabon")
        size = min(h * 0.46, 24)
        if kind == "primary":
            size = min(h * 0.5, 34)
        if not sub:
            self.label(x + (20 if left else 0), y, w - (20 if left else 0), h, text, size, t1, "L" if left else "C", True, False)
        else:
            self.label(x, y + h * 0.08, w, h * 0.6, text, size, t1, "C", True, False)
            self.label(x, y + h * 0.58, w, h * 0.3, sub, size * 0.5, wa(t1, 0.75), "C", False, False)
        self.alpha /= a

    def slider(self, x, y, w, label, f, shown):
        h = 40
        tx, tw, ty = x + w * 0.42, w * 0.42, y + h / 2
        self.label(x, y, w * 0.4, h, label, 19, TEXT)
        self.panel(tx, ty - 3, tw, 6, (1, 1, 1, 0.15), 3)
        self.panel(tx, ty - 3, tw * f, 6, ACCENT, 3)
        self.dot(tx + tw * f, ty, 18, TEXT)
        self.label(x + w * 0.86, y, w * 0.14, h, shown, 19, ACCENT, "R", True)

    def switch(self, x, y, w, label, on):
        h = 40
        self.label(x, y, w * 0.6, h, label, 19, TEXT)
        sx, sy = x + w - 52, y + h / 2 - 12
        self.panel(sx, sy, 52, 24, ACCENT if on else (1, 1, 1, 0.15), 12)
        self.dot(sx + 40 if on else sx + 12, sy + 12, 18, (0.1, 0.08, 0.05, 1) if on else TEXT)

    def choice(self, x, y, w, label, options, index, enabled=True):
        h = 40
        self.label(x, y, w * 0.4, h, label, 19, TEXT)
        cx, cw, gap = x + w * 0.42, w * 0.58, 4
        bw = (cw - gap * (len(options) - 1)) / len(options)
        a = 1 if enabled else 0.45
        self.alpha *= a
        for i, o in enumerate(options):
            on = i == index
            bx = cx + i * (bw + gap)
            self.panel(bx, y + 4, bw, h - 8, ACCENT if on else (1, 1, 1, 0.07), 4)
            self.label(bx, y + 4, bw, h - 8, o, 16, (0.09, 0.07, 0.04, 1) if on else TEXT, "C", True, False)
        self.alpha /= a

    def textfield(self, x, y, w, h, value, focused=False):
        self.panel(x, y, w, h, (0.06, 0.07, 0.09, 0.92), 4)
        self.outline(x, y, w, h, ACCENT if focused else (1, 1, 1, 0.18), 1, 4)
        self.label(x + 10, y, w - 20, h, value, 19, TEXT, "L", False, False)


def clock(sec):
    s = int(math.ceil(max(0, sec)))
    return "%d:%02d" % (s // 60, s % 60)


# ---------------------------------------------------------------------------- the match HUD (RoyaleHud*.cs)

M = 24


def compass(k, yaw, marks):
    width, span, ch = 560, 110, 30
    cx, y = k.W / 2, M
    k.fade_side(cx, y, width / 2, ch, INK)
    k.fade_side(cx - width / 2, y, width / 2, ch, INK, True)
    names = {0: "N", 45: "NE", 90: "E", 135: "SE", 180: "S", 225: "SW", 270: "W", 315: "NW"}
    for deg in range(0, 360, 5):
        d = (deg - yaw + 540) % 360 - 180
        if abs(d) > span / 2:
            continue
        x = cx + d / span * width
        k.alpha = 1 - (abs(d) / (span / 2)) ** 3
        if deg % 45 == 0:
            t = names[deg]
            k.label(x - 20, y, 40, ch, t, 20 if len(t) == 1 else 16, ACCENT if deg == 0 else TEXT, "C", True)
        elif deg % 15 == 0:
            k.label(x - 20, y + 1, 40, 16, str(deg), 12, TEXT_DIM, "C")
            k.fill(x - 0.75, y + ch - 9, 1.5, 7, (1, 1, 1, 0.7))
        else:
            k.fill(x - 0.5, y + ch - 6, 1, 4, (1, 1, 1, 0.45))
        k.alpha = 1
    for bearing, icon, c, lab in marks:
        d = max(-span / 2, min(span / 2, (bearing - yaw + 540) % 360 - 180))
        x = cx + d / span * width
        k.icon_at(icon, x, y - 9, 16, c)
        if lab:
            k.label(x - 40, y + 30, 80, 16, lab, 13, c, "C", True)
    k.fill(cx - 1, y + ch - 2, 2, 8, ACCENT)
    k.panel(cx - 26, y + ch + 6, 52, 22, INK_DEEP, 4)
    k.label(cx - 26, y + ch + 6, 52, 22, "%03d" % (round(yaw) % 360), 16, TEXT, "C", True, False)


def kill_feed(k, lines):
    y = M
    lh, size = 30, 17
    for killer, weapon, head, victim, mine, by_player, of_player in lines:
        weapon = weapon.upper()
        width = k.text_width(killer, size, True) + 12 + k.text_width(weapon, 13, True) + 16 + (22 if head else 0) + 12 + k.text_width(victim, size, True)
        x = M
        k.panel(x, y, width + 24, lh, (0.25, 0.13, 0.04, 0.75) if mine else INK, 4)
        if mine:
            k.panel(x, y + 4, 3, lh - 8, ACCENT, 1.5)
        tx = x + 12
        k.label(tx, y, 300, lh, killer, size, ACCENT if by_player else TEXT, "L", True)
        tx += k.text_width(killer, size, True) + 8
        ww = k.text_width(weapon, 13, True) + 12
        k.panel(tx, y + 6, ww, lh - 12, (1, 1, 1, 0.1), 3)
        k.label(tx, y + 6, ww, lh - 12, weapon, 13, TEXT_DIM, "C", True, False)
        tx += ww + 4
        if head:
            k.icon_at("skull", tx + 9, y + lh / 2, 16, ACCENT)
            tx += 22
        tx += 4
        k.label(tx, y, 300, lh, victim, size, DANGER if of_player else TEXT, "L", True)
        y += lh + 5


def minimap(k, mapimg, place, alive, kills, haze):
    size = 232
    rx, ry = k.W - M - size, M
    k.panel(rx - 4, ry - 4, size + 8, size + 8, INK_DEEP, 8)
    # Everything on the map is drawn on its own layer, clipped to the square (GUI.BeginClip in C#).
    S = k.S
    sub = Kit(mapimg.resize((int(size * S), int(size * S)), Image.LANCZOS).convert("RGBA"), S)
    sub.circle(size * 0.2, size * 0.95, size * 0.9, wa(HAZE, 0.95))
    sub.circle(size * 0.72, size * 0.28, size * 0.36, (1, 1, 1, 1))
    sub.icon_at("drop", 40, 60, 16, (1, 0.32, 0.22, 1))
    for i in range(1, 12):
        sub.dot(150 + i * 6, 170 - i * 4, 2.6, (1, 0.6, 0.15, 0.9))
    sub.fill(120, 150, 10, 8, (0.08, 0.08, 0.08, 0.9))
    sub.fill(121, 151, 8, 6, (0.55, 0.85, 1, 1))
    sub.icon_at("pin", 190, 50 - 9, 18, MARKER)
    sub.fade_panel(0, size - 34, size, 34, (0, 0, 0, 0.7), False)
    sub.label(0, size - 26, size, 22, place, 15, TEXT, "C", True)
    k.comp(sub.img, rx * S, ry * S)
    k.icon_at("arrow", rx + size / 2, ry + size / 2, 24, (0, 0, 0, 0.6), 35)
    k.icon_at("arrow", rx + size / 2, ry + size / 2, 20, MARKER, 35)
    k.outline(rx - 4, ry - 4, size + 8, size + 8, LINE, 1.5, 8)
    k.panel(rx + size / 2 - 11, ry - 9, 22, 18, INK_DEEP, 4)
    k.label(rx + size / 2 - 11, ry - 9, 22, 18, "N", 14, ACCENT, "C", True, False)

    match_info(k, alive, kills, haze, True)


def match_info(k, alive, kills, haze, under_minimap=True):
    size = 232
    x, y, bw = k.W - M - size - 4, (M + size + 12) if under_minimap else M, size + 8
    half = (bw - 6) / 2
    k.panel(x, y, half, 38, INK, 6)
    k.icon_at("person", x + 20, y + 19, 20, TEXT_DIM)
    k.label(x + 36, y, half - 40, 38, str(alive), 24, TEXT, "L", True)
    k.label(x + 36, y, half - 46, 38, "LEFT", 12, TEXT_DIM, "R", True)
    kx = x + half + 6
    k.panel(kx, y, half, 38, INK, 6)
    k.icon_at("target", kx + 20, y + 19, 20, ACCENT)
    k.label(kx + 36, y, half - 40, 38, str(kills), 24, TEXT, "L", True)
    k.label(kx + 36, y, half - 46, 38, "ELIMS", 12, TEXT_DIM, "R", True)
    closing, rnd, rounds, left, frac = haze
    y += 44
    c = HAZE if closing else TEXT
    k.panel(x, y, bw, 54, (0.26, 0.12, 0.03, 0.75) if closing else INK, 6)
    k.icon_at("haze", x + 22, y + 24, 24, c)
    k.label(x + 42, y + 5, 160, 20, "HAZE CLOSING" if closing else "HAZE MOVES IN", 14, c, "L", True)
    k.label(x + 42, y + 24, 160, 18, "ROUND %d OF %d" % (rnd, rounds), 13, TEXT_DIM, "L", True)
    k.label(x + bw - 110, y + 2, 100, 42, clock(left), 30, TEXT, "R", True)
    k.fill(x + 8, y + 47, bw - 16, 3, (1, 1, 1, 0.12))
    k.fill(x + 8, y + 47, (bw - 16) * frac, 3, HAZE if closing else TEXT)


def gear_badge(k, right, y, icon, level, dur):
    s = 30
    x = right - s
    lc = TIERS[min(3, max(1, level))] if level > 0 else TEXT_DIM
    k.panel(x, y, s, s, wa(lc, 0.22) if level > 0 else (1, 1, 1, 0.06), 4)
    if level > 0:
        k.outline(x, y, s, s, wa(lc, 0.8), 1, 4)
    k.icon_at(icon, x + s / 2, y + s / 2 - 1, 20, lc if level > 0 else (1, 1, 1, 0.25))
    if level > 0:
        k.label(x + s - 11, y + s - 13, 10, 12, str(level), 11, TEXT, "C", True)
        if dur >= 0:
            k.fill(x + 3, y + s - 3, (s - 6) * dur, 2, TEXT)
    return x - 5


def player_card(k, name, hp, trail, boost, vest, vest_dur, helmet, helmet_dur, pack):
    cw, chh = 430, 98
    x, y = M, k.H - M - chh
    k.panel(x, y, cw, chh, INK, 8)
    frac = hp / 100
    low = frac < 0.3
    k.panel(x + 8, y + 8, 82, chh - 16, (1, 1, 1, 0.06), 6)
    k.icon_at("heart", x + 49, y + 22, 15, HEALTH_LOW if low else TEXT_DIM)
    k.label(x + 8, y + 28, 82, 46, str(int(math.ceil(hp))), 44, HEALTH_LOW if low else TEXT, "C", True)
    if boost > 0.5:
        k.label(x + 8, y + 70, 82, 18, "+%d" % math.ceil(boost), 14, BOOST, "C", True)
    bx, bw = x + 102, cw - 114
    k.label(bx, y + 6, 180, 26, name.upper(), 19, TEXT, "L", True)
    gx = x + cw - 12
    durs = [0, 80, 140, 200]
    gx = gear_badge(k, gx, y + 8, "pack", pack, -1)
    gx = gear_badge(k, gx, y + 8, "vest", vest, vest_dur / durs[vest] if vest else 0)
    gear_badge(k, gx, y + 8, "helmet", helmet, helmet_dur / durs[helmet] if helmet else 0)
    vy = y + 42
    mx = durs[vest]
    k.bar(bx, vy, bw - 44, 12, vest_dur / mx, SHIELD, round(mx / 40), 2)
    k.label(bx + bw - 40, vy - 4, 40, 20, str(int(math.ceil(vest_dur))), 15, SHIELD, "R", True)
    hy, hh = y + 62, 24
    k.panel(bx, hy, bw, hh, (0, 0, 0, 0.5), 3)
    tf = trail / 100
    if tf > frac:
        k.panel(bx + bw * frac, hy, bw * (tf - frac), hh, (1, 0.38, 0.28, 0.85), 3)
    k.panel(bx, hy, max(6, bw * frac), hh, HEALTH_LOW if low else HEALTH, 3)
    if boost > 0:
        b = min(1 - frac, boost / 100)
        k.panel(bx + bw * frac, hy, bw * b, hh, wa(BOOST, 0.55), 3)
    k.segments(bx, hy, bw, hh, 4)


def hotbar(k, guns, selected, kit):
    sw, sh, kw, gap, kit_gap, split = 150, 78, 60, 8, 6, 18
    total = 3 * sw + 2 * gap + split + 5 * kw + 4 * kit_gap
    x, y = k.W / 2 - total / 2, k.H - M - sh
    for i, g in enumerate(guns):
        sel = i == selected and g is not None
        sy = y - 8 if sel else y
        if g is None:
            k.panel(x, sy, sw, sh, (0.05, 0.06, 0.08, 0.35), 6)
            k.outline(x, sy, sw, sh, (1, 1, 1, 0.08), 1, 6)
            k.alpha = 0.5
            k.keycap(x + 6, sy + 6, str(i + 1), 20)
            k.alpha = 1
            k.label(x, sy, sw, sh, "EMPTY", 14, (1, 1, 1, 0.3), "C", True, False)
        else:
            name, cls, tier, loaded, reserve = g
            tc = TIERS[tier]
            k.panel(x, sy, sw, sh, INK_DEEP if sel else INK, 6)
            k.fade_panel(x + 1, sy + sh - 40, sw - 2, 40, wa(tc, 0.42 if sel else 0.26), False)
            k.panel(x, sy + sh - 4, sw, 4, tc, 2)
            if sel:
                k.outline(x - 1, sy - 1, sw + 2, sh + 2, ACCENT, 2, 7)
            k.keycap(x + 6, sy + 6, str(i + 1), 20)
            k.label(x + 30, sy + 4, sw - 38, 22, name.upper(), 14, TEXT if sel else TEXT_DIM, "R", True)
            k.icon_at(cls, x + sw / 2 - 4, sy + 42, 32, TEXT if sel else wa(TEXT, 0.75))
            k.label(x + 8, sy + sh - 26, sw - 16, 20, "%d / %d" % (loaded, reserve), 15, DANGER if loaded == 0 else TEXT, "R", True)
        x += sw + gap
    x += split - gap
    icons = ["bandage", "medkit", "chai", "grenade", "smoke"]
    keys = ["Q", "Q", "Z", "G", "4"]
    for j in range(5):
        has = kit[j] > 0
        k.panel(x, y, kw, sh, INK if has else (0.05, 0.06, 0.08, 0.35), 6)
        k.alpha = 1 if has else 0.45
        k.keycap(x + 4, y + 4, keys[j], 20)
        k.icon_at(icons[j], x + kw / 2, y + 40, 30, TEXT if has else (1, 1, 1, 0.5))
        k.label(x, y + sh - 24, kw - 7, 20, str(kit[j]), 17, TEXT, "R", True)
        k.alpha = 1
        x += kw + kit_gap


def chip(k, right, y, text, back, fore):
    tw = k.text_width(text, 13, True) + 14
    k.panel(right - tw, y, tw, 22, back, 4)
    k.label(right - tw, y, tw, 22, text, 13, fore, "C", True, False)
    return right - tw - 6


def weapon_panel(k, name, tier, loaded, mag, reserve, calibre, mode, parts):
    right, bottom = k.W - M, k.H - M
    k.fade_side(right - 440, bottom - 150, 440 + M, 150 + M, wa(INK, 0.9), True)
    tc = TIERS[tier]
    carried = "/ %d" % reserve
    cw = max(70, k.text_width(carried, 28, True) + 6)
    low = loaded <= max(1, mag // 4)
    k.label(right - cw - 220, bottom - 82, 214, 74, str(loaded), 70, DANGER if low else TEXT, "R", True)
    k.label(right - cw, bottom - 66, cw, 36, carried, 28, TEXT_DIM, "L", True)
    k.icon("ammo", right - cw, bottom - 28, 16, TEXT_DIM)
    k.label(right - cw + 16, bottom - 30, cw, 20, calibre, 14, TEXT_DIM, "L", True)
    x, cy = right, bottom - 112
    x = chip(k, x, cy, mode, (1, 1, 1, 0.14), TEXT)
    x = chip(k, x, cy, TIER_NAMES[tier], wa(tc, 0.85), (0.06, 0.06, 0.07, 1))
    for p in reversed(parts):
        x = chip(k, x, cy, p, (0, 0, 0, 0.4), TEXT)
    k.label(right - 400, bottom - 150, 400, 34, name.upper(), 28, lerp(tc, (1, 1, 1, 1), 0.25), "R", True)


def crosshair(k, cx, cy, gap):
    cc = (1, 1, 1, 0.92)
    shade = (0, 0, 0, 0.5)
    ln, t = 9, 2
    k.fill(cx - 1.5, cy - 1.5, 3, 3, shade)
    for (x, y, w, h) in [(cx - gap - ln, cy - t / 2, ln, t), (cx + gap, cy - t / 2, ln, t), (cx - t / 2, cy - gap - ln, t, ln), (cx - t / 2, cy + gap, t, ln)]:
        k.fill(x - 0.5, y - 0.5, w + 1, h + 1, shade)
        k.fill(x, y, w, h, cc)
    k.fill(cx - 1, cy - 1, 2, 2, cc)


def elimination(k, cx, cy, name, kills):
    y = cy + 120
    name = name.upper()
    nw = k.text_width(name, 28, True)
    total = 44 + 12 + nw
    x = cx - total / 2
    k.panel(x, y, 44, 44, ACCENT, 22)
    k.icon_at("skull", x + 22, y + 22, 26, (0.1, 0.07, 0.04, 1))
    k.label(x + 56, y - 6, nw + 10, 22, "ELIMINATED", 14, ACCENT, "L", True)
    k.label(x + 56, y + 12, nw + 10, 34, name, 28, TEXT, "L", True)
    k.label(cx - 150, y + 50, 300, 20, "%d ELIMINATIONS" % kills, 14, TEXT_DIM, "C", True)


def prompt(k, cx, cy, key, text):
    tw, kw = k.text_width(text, 19), 34
    pw, x, y = tw + kw + 28, cx + 60, cy + 26
    k.panel(x, y, pw, 36, INK, 5)
    k.keycap(x + 8, y + 6, key, 24)
    k.label(x + 14 + kw, y, tw + 4, 36, text, 19, TEXT)


def pickups(k, notes):
    y = k.H - M - 98 - 44 - 30
    for t in reversed(notes):
        tw = k.text_width(t, 16)
        k.panel(M, y, tw + 26, 26, INK, 4)
        k.panel(M, y + 5, 3, 16, ACCENT, 1.5)
        k.label(M + 14, y, tw + 4, 26, t, 16, TEXT)
        y -= 31


def match_shot(bg, mapimg, path):
    S = 2
    img = bg.convert("RGBA").resize((1920 * S, 1080 * S), Image.LANCZOS)
    k = Kit(img, S)
    W, H = k.W, k.H
    k.edge_vignette((0.75, 0.02, 0, 0.85 * 0.12))
    compass(k, 52, [(80, "target", (1, 1, 1, 1), None), (20, "pin", MARKER, "184 M"), (-120, "drop", (1, 0.35, 0.25, 1), None)])
    kill_feed(k, [("Honey", "Tawi AR", True, "Meera", True, True, False),
                  ("Kabir", "Banihal 12", False, "Ishaan", False, False, False),
                  ("Zoya", "Grenade", False, "Arjun", False, False, False)])
    minimap(k, mapimg, "SHOPPING COMPLEX", 17, 3, (True, 3, 6, 42, 0.48))
    cx, cy = W / 2, H / 2
    crosshair(k, cx, cy, 12)
    k.label(cx + 120 - 80, cy - 70 - 16, 160, 32, "47", 24, (1, 0.82, 0.2, 1), "C", True)
    k.label(cx + 150 - 80, cy - 30 - 16, 160, 32, "21", 24, SHIELD, "C", True)
    k.hit_arc(cx, cy, 150, -70, (1, 0.18, 0.1, 0.8))
    elimination(k, cx, cy, "Meera", 3)
    prompt(k, cx, cy, "E", "Pick up Medkit")
    player_card(k, "Honey", 64, 82, 18, 2, 96, 1, 50, 2)
    hotbar(k, [("Tawi AR", "rifle", 3, 7, 112), ("Banihal 12", "shotgun", 2, 5, 18), None], 0, [4, 1, 2, 1, 0])
    weapon_panel(k, "Tawi AR", 3, 7, 30, 112, "5.56", "AUTO", ["2X", "COMP", "GRIP"])
    pickups(k, ["Picked up 30 5.56 rounds", "Picked up Medkit"])
    img.resize((1920, 1080), Image.LANCZOS).convert("RGB").save(path)


# ---------------------------------------------------------------------------- the front menu (NetLobbyMenu.cs)

def settings_page(k, x, y, w, page=1):
    """MenuPanels.Settings: tabs and reset along the top, then the game (0), screen (1) or sound (2) page."""
    def heading(yy, t):
        k.label(x, yy, w, 26, t, 15, ACCENT, "L", True, False)
        tw = k.text_width(t, 15, True)
        k.fill(x + tw + 12, yy + 13, w - tw - 12, 1, LINE)
    row = 44
    k.button(x, y, 120, 36, "GAME", "tabon" if page == 0 else "tab")
    k.button(x + 128, y, 120, 36, "SCREEN", "tabon" if page == 1 else "tab")
    k.button(x + 256, y, 120, 36, "SOUND", "tabon" if page == 2 else "tab")
    k.button(x + w - 120, y, 120, 36, "RESET ALL", "quiet")
    y += 54
    if page == 2:
        heading(y, "VOLUME"); y += 30
        k.slider(x, y, w, "Master", 0.85, "85%"); y += row
        for name, v in (("Music", 0.6), ("Effects", 0.9), ("Ambience", 0.8), ("Interface", 0.8), ("Announcer", 0.9)):
            k.slider(x, y, w, name, v, "%d%%" % round(v * 100)); y += row
        y += 8
        heading(y, "WINDOW"); y += 30
        k.switch(x, y, w, "Pause and mute in background", True); y += row
        return
    if page == 1:
        heading(y, "SCREEN"); y += 30
        k.choice(x, y, w, "Graphics", ["Low", "Medium", "High"], 1); y += row
        k.choice(x, y, w, "Time of day", ["Day", "Sunset", "Night", "Changing"], 3); y += row
        k.slider(x, y, w, "Field of view", (70 - 50) / 40, "70°"); y += row
        k.slider(x, y, w, "HUD size", (1 - 0.75) / 0.55, "100%"); y += row
        k.choice(x, y, w, "Crosshair", ["White", "Green", "Yellow", "Cyan"], 0); y += row
        k.switch(x, y, w, "Damage numbers", True); y += row
        k.switch(x, y, w, "Large minimap", False); y += row
        k.switch(x, y, w, "Frame time (F3)", False); y += row
        return
    heading(y, "MOUSE"); y += 30
    k.slider(x, y, w, "Mouse sensitivity", (2 - 0.3) / 7.7, "2.0"); y += row
    k.slider(x, y, w, "Aim sensitivity", (1 - 0.3) / 1.2, "100%"); y += row
    k.switch(x, y, w, "Invert look", False); y += row + 8
    heading(y, "KEYS"); y += 30
    k.switch(x, y, w, "Toggle aim (right mouse)", True); y += row
    k.switch(x, y, w, "Toggle sprint (Shift)", False); y += row + 8
    heading(y, "BOTS"); y += 30
    k.choice(x, y, w, "Difficulty", ["Recruit", "Regular", "Veteran"], 1); y += row
    k.paragraph(x + w * 0.42, y - 4, w * 0.58, 44, "Bots aim and react like average players.", 15, TEXT_DIM)


def menu_shot(bg, path, page=1):
    S = 2
    img = bg.convert("RGBA").resize((1920 * S, 1080 * S), Image.LANCZOS)
    k = Kit(img, S)
    W, H = k.W, k.H
    k.fill(0, 0, W, H, (0.02, 0.025, 0.03, 0.45))
    k.fade_side(0, 0, min(900, W * 0.7), H, (0.02, 0.025, 0.03, 0.94))
    k.fade_panel(0, H - 220, W, 220, (0.02, 0.025, 0.03, 0.6), False)
    x, y = 80, 56
    k.label(x, y, 600, 22, "IIM JAMMU  ·  JAGTI CAMPUS", 16, ACCENT, "L", True)
    k.label(x - 4, y + 14, 600, 104, "JAGTI", 104, TEXT, "L", True)
    k.label(x - 4, y + 98, 600, 104, "ROYALE", 104, ACCENT, "L", True)
    k.label(x, y + 200, 600, 26, "Last one standing on the IIM Jammu campus.", 20, TEXT_DIM)
    y += 252
    pw = 440
    k.label(x, y, 120, 40, "Your name", 18, TEXT_DIM)
    k.textfield(x + 110, y, pw - 110, 40, "Honey")
    y += 54
    k.button(x, y, pw, 78, "PLAY", "primary", True, "AGAINST 29 BOTS  ·  ENTER")
    y += 90
    k.choice(x, y, pw, "Bots", ["Recruit", "Regular", "Veteran"], 1)
    y += 40
    k.label(x + pw * 0.42, y, pw * 0.58, 20, "Bots aim and react like average players.", 14, TEXT_DIM)
    y += 22 + 22
    k.label(x, y, pw, 24, "PLAY WITH FRIENDS", 15, ACCENT, "L", True)
    tw = k.text_width("PLAY WITH FRIENDS", 15, True)
    k.fill(x + tw + 12, y + 12, pw - tw - 12, 1, LINE)
    y += 28
    k.paragraph(x, y, pw, 40, "Up to 8 players on the same network; bots fill the rest of the 30 places. One of you hosts, the others join.", 15, TEXT_DIM)
    y += 44
    k.button(x, y, pw * 0.5 - 4, 42, "HOST A GAME", hover=True)
    k.button(x + pw * 0.5 + 4, y, pw * 0.5 - 4, 42, "FIND GAMES")
    y += 52
    k.label(x, y, 110, 38, "Host address", 16, TEXT_DIM)
    k.textfield(x + 110, y, pw - 110 - 170, 38, "192.168.1.")
    k.textfield(x + pw - 164, y, 70, 38, "7777")
    k.button(x + pw - 88, y, 88, 38, "JOIN")
    by = H - 76
    k.button(x, by, 140, 44, "SETTINGS", "tabon")
    k.button(x + 150, by, 140, 44, "CONTROLS", "tab")
    k.button(x + 300, by, 140, 44, "QUIT", "quiet")
    px = x + pw + 80
    pwid = min(640, W - px - 60)
    py = 80
    ph = min(H - py - 120, 580 if page == 0 else 560)  # MenuPanels.SettingsPanelHeight
    k.panel(px, py, pwid, ph, (0.06, 0.07, 0.09, 0.9), 10)
    k.outline(px, py, pwid, ph, LINE, 1, 10)
    k.label(px + 32, py + 20, pwid - 64, 40, "SETTINGS", 30, TEXT, "L", True)
    settings_page(k, px + 32, py + 76, pwid - 64, page)
    img.resize((1920, 1080), Image.LANCZOS).convert("RGB").save(path)


# ---------------------------------------------------------------------------- the results (RoyaleHud.Menus.cs)

def stat_card(k, x, y, w, h, icon, label, value):
    k.panel(x, y, w, h, INK, 8)
    k.outline(x, y, w, h, LINE, 1, 8)
    k.icon_at(icon, x + 26, y + 28, 22, ACCENT)
    k.label(x + 46, y + 16, w - 56, 24, label, 14, TEXT_DIM, "L", True)
    k.label(x + 18, y + 46, w - 36, 56, value, 46, TEXT, "L", True)


def results_shot(bg, path):
    S = 2
    img = bg.convert("RGBA").resize((1920 * S, 1080 * S), Image.LANCZOS)
    k = Kit(img, S)
    W, H = k.W, k.H
    k.fill(0, 0, W, H, (0.02, 0.025, 0.03, 0.8))
    k.fade_panel(0, 0, W, H * 0.5, wa(ACCENT, 0.28))
    y = H * 0.1
    k.label(0, y, W, 24, "VICTORY ON THE JAGTI CAMPUS", 18, ACCENT, "C", True)
    k.label(0, y + 14, W, 150, "#1", 140, ACCENT, "C", True)
    k.label(0, y + 150, W, 26, "OF 30", 20, TEXT_DIM, "C", True)
    k.label(0, y + 186, W, 44, "TOP OF THE HILL", 36, TEXT, "C", True)
    cw, ch, gap = 210, 112, 16
    cx, cy = W / 2 - (cw * 4 + gap * 3) / 2, y + 256
    stat_card(k, cx, cy, cw, ch, "target", "ELIMINATIONS", "7")
    stat_card(k, cx + (cw + gap), cy, cw, ch, "ammo", "DAMAGE DEALT", "1284")
    stat_card(k, cx + (cw + gap) * 2, cy, cw, ch, "clock", "SURVIVED", "14:08")
    stat_card(k, cx + (cw + gap) * 3, cy, cw, ch, "person", "PLACED", "#1 / 30")
    by = cy + ch + 44
    pw, sw, qw, bg_ = 320, 220, 150, 14
    bx = W / 2 - (pw + sw + qw + bg_ * 2) / 2
    k.button(bx, by, pw, 64, "PLAY AGAIN", "primary", True, "ENTER")
    k.button(bx + pw + bg_, by, sw, 64, "MAIN MENU")
    k.button(bx + pw + sw + bg_ * 2, by, qw, 64, "QUIT", "quiet")
    dw, dy = 560, by + 90
    k.choice(W / 2 - dw / 2, dy, dw, "Bots next match", ["Recruit", "Regular", "Veteran"], 2)
    k.label(0, dy + 42, W, 22, "Bots aim well and use cover.", 15, TEXT_DIM, "C")
    img.resize((1920, 1080), Image.LANCZOS).convert("RGB").save(path)


# ---------------------------------------------------------------------------- after the player is out (RoyaleHud.Menus.cs)

def death_shot(bg, path):
    """DrawDeathCam about 2.5 s in: letterbox, who did it, with what, from how far."""
    S = 2
    img = bg.convert("RGBA").resize((1920 * S, 1080 * S), Image.LANCZOS)
    k = Kit(img, S)
    W, H = k.W, k.H
    k.edge_vignette((0, 0, 0, 0.6))
    k.fill(0, 0, W, 70, (0, 0, 0, 1))
    k.fill(0, H - 70, W, 70, (0, 0, 0, 1))
    kill_feed(k, [("Meera", "Tawi AR", True, "Honey", True, False, True),
                  ("Kabir", "Banihal 12", False, "Ishaan", False, False, False)])
    match_info(k, 11, 3, (False, 4, 6, 37, 0.6), False)
    y = H - 250
    k.label(0, y, W, 24, "TAKEN OUT BY", 18, DANGER, "C", True)
    k.label(0, y + 20, W, 60, "MEERA_TAWI42", 52, TEXT, "C", True)
    k.label(0, y + 80, W, 24, "TAWI AR   ·   38 M AWAY   ·   64 HEALTH LEFT", 17, TEXT_DIM, "C", True)
    k.label(0, y + 112, W, 26, "PLACED #12 OF 30", 20, ACCENT, "C", True)
    img.resize((1920, 1080), Image.LANCZOS).convert("RGB").save(path)


def spectate_shot(bg, path):
    """DrawSpectating: kill feed, match panel, the watched fighter's bar and the keys."""
    S = 2
    img = bg.convert("RGBA").resize((1920 * S, 1080 * S), Image.LANCZOS)
    k = Kit(img, S)
    W, H = k.W, k.H
    kill_feed(k, [("Meera", "Tawi AR", False, "Rohan", False, False, False),
                  ("Zoya", "Grenade", False, "Arjun", False, False, False)])
    match_info(k, 6, 3, (True, 5, 6, 21, 0.3), False)
    pw = 460
    x, y = W / 2 - pw / 2, H - M - 150
    k.panel(x, y, pw, 92, INK, 8)
    k.label(x + 18, y + 8, 200, 20, "SPECTATING", 14, ACCENT, "L", True)
    k.label(x + 18, y + 26, pw - 140, 36, "MEERA_TAWI42", 30, TEXT, "L", True)
    k.icon_at("target", x + pw - 90, y + 32, 20, ACCENT)
    k.label(x + pw - 76, y + 16, 60, 32, "5", 26, TEXT, "L", True)
    k.bar(x + 18, y + 68, pw - 36, 10, 0.64, HEALTH, 4, 2)
    k.bar(x + 18, y + 62, pw - 36, 4, 0.7, SHIELD, 0, 2)
    k.hints(W / 2, H - M - 30, 26, "LMB", "Next", "RMB", "Previous", "MOUSE", "Look around", "ENTER", "Results")
    img.resize((1920, 1080), Image.LANCZOS).convert("RGB").save(path)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    os.makedirs(OUT, exist_ok=True)
    bg = Image.open(sys.argv[1])
    mapimg = Image.open(sys.argv[2]) if len(sys.argv) > 2 else Image.new("RGB", (512, 512), (70, 90, 60))
    match_shot(bg, mapimg, os.path.join(OUT, "hud_match.png"))
    menu_shot(bg, os.path.join(OUT, "hud_menu.png"))
    results_shot(bg, os.path.join(OUT, "hud_results.png"))
    death_shot(bg, os.path.join(OUT, "hud_deathcam.png"))
    spectate_shot(bg, os.path.join(OUT, "hud_spectate.png"))
    menu_shot(bg, os.path.join(OUT, "hud_settings_game.png"), 0)
    menu_shot(bg, os.path.join(OUT, "hud_settings_sound.png"), 2)
    print("wrote", OUT)
