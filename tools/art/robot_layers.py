"""ロボット全体の断面図（横から見た図）を、曲線のある人型の形で描き出す。

人体の解剖図のように、外側の装甲（輪郭）の内側を、装甲の厚みぶん内側に縮めた形がそのまま
「内部の空洞」になる。腕の中・手のひら・指の1本1本の中まで空洞がつながっている。

書き出す画像（どれもロボット全体と同じ範囲・同じ大きさ）:
    robot_armor.png  外側の装甲（関節・継ぎ目・角・目つき）
    robot_back.png   空洞の奥（骨組み・小さな歯車・シリンダー・フレームに沿ったケーブル。もやで薄くしてある）
    robot_front.png  空洞の手前に垂れる濃い色のケーブル（操縦士より手前に描く）

Unity 側（InteriorMap.cs）は、この3枚を同じ位置に重ねて置くだけ。
足場とはしごの位置は InteriorMap.cs に書いてあり、このスクリプトはそれが空洞の中に
収まっているかを確かめて、はみ出していたら知らせる。

座標は「設計マス」。1マス = 1.3m（InteriorMap.Scale）。x = 右、z = 上。
"""
import math
import os
import random
import re

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage  # pip install scipy

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "robot", "Assets", "Resources", "Art")
CS_MAP = os.path.join(ROOT, "robot", "Assets", "Scripts", "Map", "InteriorMap.cs")

SCALE = 1.3          # 1マスのメートル（InteriorMap.Scale と同じ）
PPM = 32             # 1メートルのピクセル数（Unity 側の取り込み設定 LayerPixelsPerUnit と同じ）
SS = 2               # なめらかにするため2倍で描いて縮小
# 画像が覆う範囲（設計マス）。InteriorMap.LayerRect と同じ
U_X0, U_X1, U_Z0, U_Z1 = -8.0, 48.0, -16.0, 66.0

PX = PPM * SCALE * SS          # 1マスの（拡大描画での）ピクセル数
W = int(round((U_X1 - U_X0) * PPM * SCALE)) * SS
H = int(round((U_Z1 - U_Z0) * PPM * SCALE)) * SS

INK = (22, 26, 31, 255)
WHITE, RED, BLUE, YELLOW, DARK = "#d9dee2", "#c8402f", "#3c6fb4", "#f2b630", "#4a5866"


def P(x, z):
    return ((x - U_X0) * PX, (U_Z1 - z) * PX)


def hexc(h, a=255):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def chaikin(pts, n=3):
    for _ in range(n):
        out = []
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            out += [(a[0] * 0.75 + b[0] * 0.25, a[1] * 0.75 + b[1] * 0.25), (a[0] * 0.25 + b[0] * 0.75, a[1] * 0.25 + b[1] * 0.75)]
        pts = out
    return pts


# ------------------------------------------------------------------ 体の形

class Part:
    """体の部品。kind: capsule（太さが変わる棒）/ ellipse / poly（なめらかな多角形）"""

    def __init__(self, name, kind, color, thick, **kw):
        self.name, self.kind, self.color, self.thick, self.kw = name, kind, color, thick, kw

    def mirrored(self):
        kw = dict(self.kw)
        if self.kind == "capsule":
            kw["a"] = (40 - kw["a"][0], kw["a"][1]); kw["b"] = (40 - kw["b"][0], kw["b"][1])
        elif self.kind == "ellipse":
            kw["c"] = (40 - kw["c"][0], kw["c"][1])
        else:
            kw["pts"] = [(40 - x, z) for x, z in kw["pts"]]
        for key in ("axis_a", "axis_b", "c"):
            if key in kw and self.kind != "ellipse":
                kw[key] = (40 - kw[key][0], kw[key][1])
        return Part(self.name.replace("左", "右"), self.kind, self.color, self.thick, **kw)

    def draw(self, d, fill, shrink=0.0):
        """shrink マスぶん内側に縮めて描く（空洞を作るとき）"""
        k = self.kw
        if self.kind == "capsule":
            (ax, az), (bx, bz) = k["a"], k["b"]
            r0, r1 = k["r0"] - shrink, k["r1"] - shrink
            if r0 <= 0.05 or r1 <= 0.05:
                return
            n = 24
            for i in range(n + 1):  # 太さの変わる棒を、円をたくさん並べて描く
                t = i / n
                x, z, r = ax + (bx - ax) * t, az + (bz - az) * t, r0 + (r1 - r0) * t
                cx, cy = P(x, z)
                d.ellipse([cx - r * PX, cy - r * PX, cx + r * PX, cy + r * PX], fill=fill)
        elif self.kind == "ellipse":
            (cx_, cz_) = k["c"]
            rx, rz = k["rx"] - shrink, k["rz"] - shrink
            if rx <= 0.05 or rz <= 0.05:
                return
            a, b = P(cx_ - rx, cz_ + rz), P(cx_ + rx, cz_ - rz)
            d.ellipse([a[0], a[1], b[0], b[1]], fill=fill)
        else:
            pts = chaikin(k["pts"], k.get("smooth", 0))
            if shrink > 0:
                # 多角形はマスクを削って縮める（呼び出し側で処理）
                pass
            d.polygon([P(*p) for p in pts], fill=fill)


def octagon(cx, cz, r):
    return [(cx + math.cos(math.radians(22.5 + i * 45)) * r, cz + math.sin(math.radians(22.5 + i * 45)) * r) for i in range(8)]


def limb(name, color, thick, pts, a, b, r):
    """角ばった手足の部品。中の機械（シリンダー・ケーブル）を通す芯 a→b と太さ r も持つ"""
    return Part(name, "poly", color, thick, pts=pts, axis_a=a, axis_b=b, axis_r=r)


def build_parts():
    """角ばったメカの体。脚が長く（全高の半分近く）、すねは太く長く、足は大きな箱型。
    肩は縦長のブロック、上腕は細く前腕は太い。腰は細く、胸は広く平ら、頭は小さい。"""
    L = []
    L.append(Part("頭", "poly", WHITE, 0.9, pts=[(16.2, 48.4), (23.8, 48.4), (24.6, 50.4), (24.4, 54.2), (22.8, 55.8), (17.2, 55.8), (15.6, 54.2), (15.4, 50.4)]))
    L.append(Part("首", "poly", DARK, 0.7, pts=[(18.2, 43.6), (21.8, 43.6), (21.8, 51.0), (18.2, 51.0)]))
    L.append(Part("胸", "poly", RED, 1.1, pts=[(10.2, 46.4), (29.8, 46.4), (31.0, 42.0), (29.6, 37.4), (26.6, 34.4), (25.0, 32.6),
                                                (15.0, 32.6), (13.4, 34.4), (10.4, 37.4), (9.0, 42.0)]))
    L.append(Part("腰", "poly", DARK, 0.9, pts=[(15.4, 35.0), (24.6, 35.0), (24.6, 29.2), (15.4, 29.2)]))
    L.append(Part("骨盤", "poly", DARK, 1.1, pts=[(12.4, 32.0), (27.6, 32.0), (28.8, 28.0), (27.6, 24.6), (22.4, 23.4), (20, 25.0),
                                                  (17.6, 23.4), (12.4, 24.6), (11.2, 28.0)]))
    left = [
        Part("左肩", "poly", YELLOW, 1.1, pts=[(-0.6, 48.2), (9.0, 48.6), (10.4, 46.2), (10.0, 37.0), (0.6, 36.8), (-1.2, 39.0)]),
        Part("左肩のつなぎ", "poly", DARK, 0.8, pts=[(6, 40.2), (12.6, 40.2), (12.6, 44.8), (6, 44.8)]),
        limb("左上腕", WHITE, 0.8, [(3.6, 39.0), (8.6, 39.0), (8.8, 30.8), (3.8, 30.8)], (6.1, 38.6), (6.3, 31.4), 2.5),
        Part("左ひじ", "poly", DARK, 0.8, pts=octagon(6.3, 31.0, 2.6), c=(6.3, 31.0)),
        limb("左前腕", WHITE, 1.0, [(1.8, 31.4), (10.4, 31.4), (10.6, 27.6), (10.0, 24.6), (2.4, 24.6), (1.6, 27.6)], (6.2, 31.2), (6.2, 24.8), 3.8),
        Part("左手首", "poly", DARK, 0.6, pts=[(4.4, 21.6), (8.0, 21.6), (8.0, 26.4), (4.4, 26.4)], c=(6.2, 24.0)),
        Part("左手のひら", "poly", WHITE, 0.7, pts=[(2.8, 23.6), (9.6, 23.6), (10.4, 18.6), (9.4, 16.6), (3.0, 16.6), (2.2, 18.6)]),
        limb("左親指", WHITE, 0.36, [(9.0, 21.6), (10.3, 22.0), (11.4, 19.0), (10.5, 17.8)], (9.7, 21.4), (11.0, 18.4), 0.7),
    ]
    for i in range(4):  # 指（関節ごとに角ばった形。真ん中の2本は長い）
        x = 3.5 + i * 1.65
        tip = 11.6 if i in (1, 2) else 12.4
        left.append(limb(f"左指{i + 1}", WHITE, 0.36, [(x - 0.74, 17.0), (x + 0.74, 17.0), (x + 0.66, tip + 0.6), (x + 0.28, tip), (x - 0.28, tip), (x - 0.66, tip + 0.6)],
                         (x, 17.0), (x, tip), 0.7))
    left += [
        Part("左股関節", "poly", DARK, 1.0, pts=octagon(15.4, 25.4, 3.0), c=(15.4, 25.4)),
        limb("左もも", BLUE, 1.0, [(12.0, 27.0), (18.8, 27.0), (18.6, 14.8), (12.4, 14.8)], (15.4, 26.6), (15.5, 15.2), 3.2),
        Part("左ひざ", "poly", DARK, 0.9, pts=[(11.6, 17.4), (19.2, 17.4), (19.4, 13.6), (15.4, 12.0), (11.4, 13.6)], c=(15.4, 15.0)),
        limb("左すね", BLUE, 1.0, [(10.8, 14.2), (19.4, 14.2), (19.6, 4.0), (18.8, -4.0), (17.6, -6.6), (13.2, -6.6), (11.6, -4.0), (10.6, 4.0)],
             (15.3, 14.0), (15.5, -6.2), 4.2),
        Part("左足首", "poly", DARK, 0.6, pts=[(13.6, -9.4), (17.4, -9.4), (17.4, -4.4), (13.6, -4.4)], c=(15.5, -6.6)),
        Part("左足", "poly", RED, 1.0, pts=[(9.4, -6.8), (19.4, -6.8), (19.8, -8.6), (19.6, -12.2), (8.2, -12.2), (7.4, -9.6)]),
    ]
    L += left + [p.mirrored() for p in left]
    return L


# 空洞には入れない飾り（角・胸のエンブレムなど）
def draw_decor(d):
    w = int(0.12 * PX)
    # 1本のアンテナ（左寄り）と、頭の上の小さな突起
    d.polygon([P(*p) for p in [(17.6, 55.4), (18.6, 55.4), (18.3, 62.0), (17.9, 62.0)]], fill=(200, 205, 210, 255), outline=INK, width=w)
    d.polygon([P(*p) for p in [(21.0, 55.4), (23.0, 55.4), (22.6, 57.2), (21.4, 57.2)]], fill=hexc(YELLOW), outline=INK, width=w)


def mask_of(parts, shrink=False):
    """部品の和集合のマスク。shrink=True なら部品ごとに厚みぶん縮めてから合わせる（= 空洞）"""
    m = Image.new("L", (W, H), 0)
    for p in parts:
        layer = Image.new("L", (W, H), 0)
        dd = ImageDraw.Draw(layer)
        if p.kind == "poly" and shrink:
            p.draw(dd, 255)
            layer = shrink_mask(layer, p.thick * PX)
        else:
            p.draw(dd, 255, p.thick if shrink else 0.0)
        m = Image.fromarray(np.maximum(np.array(m), np.array(layer)))
    return m


def shrink_mask(img, r):
    """マスクを r ピクセル内側に縮める"""
    a = np.array(img) > 0
    dist = ndimage.distance_transform_edt(a)
    return Image.fromarray(((dist > r) * 255).astype(np.uint8))


def grow_mask(img, r):
    """マスクを r ピクセル外側に太らせる"""
    a = np.array(img) > 0
    dist = ndimage.distance_transform_edt(~a)
    return Image.fromarray(((dist <= r) * 255).astype(np.uint8))


# ------------------------------------------------------------------ 装甲

def build_armor(parts):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    full = mask_of(parts)
    # 輪郭線：全体を少し太らせて黒く
    outline = grow_mask(full, 0.22 * PX)
    img.paste(INK, (0, 0), outline)
    d = ImageDraw.Draw(img)
    draw_decor(d)
    # 部品ごとに色を塗り、上側に明るいふち・下側に暗いふち
    for p in parts:
        m = Image.new("L", (W, H), 0)
        p.draw(ImageDraw.Draw(m), 255)
        a = np.array(m) > 0
        sh = int(0.35 * PX)
        top = a & ~np.roll(a, sh, axis=0)        # 上に何もないふち
        bottom = a & ~np.roll(a, -sh, axis=0)    # 下に何もないふち
        base = hexc(p.color)
        light = tuple(min(255, int(c + (255 - c) * 0.38)) for c in base[:3]) + (255,)
        darkc = tuple(int(c * 0.7) for c in base[:3]) + (255,)
        layer = np.zeros((H, W, 4), np.uint8)
        layer[a] = base
        layer[bottom] = darkc
        layer[top] = light
        img.alpha_composite(Image.fromarray(layer))
        # 部品の境目の細い線
        edge = Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.FIND_EDGES)
        img.paste((30, 34, 40, 200), (0, 0), edge.filter(ImageFilter.MaxFilter(3)))
    d = ImageDraw.Draw(img)
    # 装甲の継ぎ目（手足は軸に垂直な帯、胴は縦の線）
    seam = (40, 44, 50, 150)
    for p in parts:
        if "axis_a" not in p.kw or p.kw["axis_r"] < 2.0:
            continue
        (ax, az), (bx, bz) = p.kw["axis_a"], p.kw["axis_b"]
        L = math.hypot(bx - ax, bz - az)
        nx, nz = -(bz - az) / L, (bx - ax) / L
        for t in (0.33, 0.66):
            x, z = ax + (bx - ax) * t, az + (bz - az) * t
            r = p.kw["axis_r"] + 0.6
            d.line([P(x - nx * r, z - nz * r), P(x + nx * r, z + nz * r)], fill=seam, width=int(0.1 * PX))
    # 装甲板の内側の線（線画のような二重の輪郭で、板の厚みを見せる）
    for p in parts:
        if p.kind != "poly" or "指" in p.name:
            continue
        m = Image.new("L", (W, H), 0)
        p.draw(ImageDraw.Draw(m), 255)
        inner = shrink_mask(m, 0.42 * PX)
        e = inner.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(3))
        img.paste((40, 44, 50, 170), (0, 0), e)
    # 関節のボルト（関節の部品のまわり）
    for p in parts:
        if "c" in p.kw and p.color == DARK:
            (cx, cz) = p.kw["c"]
            rx = rz = 2.4
            for k in range(10):
                a = k / 10 * math.tau
                x, z = cx + math.cos(a) * (rx - 0.45), cz + math.sin(a) * (rz - 0.45)
                q = P(x, z)
                r = 0.16 * PX
                d.ellipse([q[0] - r, q[1] - r, q[0] + r, q[1] + r], fill=(180, 186, 192, 255), outline=INK, width=int(0.05 * PX))
    # 指の関節の線
    for p in parts:
        if "指" in p.name and "親" not in p.name:
            (ax, az), (bx, bz) = p.kw["axis_a"], p.kw["axis_b"]
            for t in (0.36, 0.68):
                x, z = ax + (bx - ax) * t, az + (bz - az) * t
                d.line([P(x - 0.75, z), P(x + 0.75, z)], fill=INK, width=int(0.12 * PX))
    # 目
    for x0, x1 in ((17.0, 19.4), (20.6, 23.0)):
        a, b = P(x0, 55.3), P(x1, 54.7)
        d.rectangle([a[0], a[1], b[0], b[1]], fill=(127, 244, 255, 255), outline=INK, width=int(0.08 * PX))
    return img


# ------------------------------------------------------------------ 奥の機械（空洞の中）

GEAR = None


def gear_img(sz, shade):
    global GEAR
    if GEAR is None:
        GEAR = Image.open(os.path.join(OUT, "gear.png")).convert("RGBA")
    t = GEAR.resize((max(4, sz), max(4, sz)), Image.LANCZOS).rotate(random.uniform(0, 40))
    a = np.array(t).astype(np.float32)
    a[..., :3] *= shade
    return Image.fromarray(a.astype(np.uint8))


def cable_line(d, pts, col, w, ink_w):
    d.line(pts, fill=INK, width=ink_w, joint="curve")
    d.line(pts, fill=col, width=w, joint="curve")


BACK_CABLES = [(70, 74, 80, 255), (200, 52, 44, 255), (150, 156, 162, 255), (46, 50, 56, 255), (210, 212, 214, 255)]
FRONT_CABLES = [(150, 30, 26, 255), (36, 40, 46, 255), (170, 130, 10, 255), (36, 64, 140, 255), (90, 92, 98, 255)]


def build_back(parts, cavity):
    rnd = random.Random(9)
    random.seed(9)
    img = Image.new("RGBA", (W, H), (30, 35, 41, 255))
    d = ImageDraw.Draw(img)
    steel, brace = (90, 100, 110, 255), (64, 72, 80, 255)
    # 骨組み：全体に縦の柱・横のはり・筋かい（空洞の形で切り抜くので、はみ出してよい）
    for x in np.arange(U_X0 + 0.6, U_X1, 3.0):
        a, b = P(x - 0.2, U_Z1), P(x + 0.2, U_Z0)
        d.rectangle([a[0], a[1], b[0], b[1]], fill=steel, outline=INK)
    for z in np.arange(U_Z0 + 1.5, U_Z1, 4.0):
        a, b = P(U_X0, z + 0.16), P(U_X1, z - 0.16)
        d.rectangle([a[0], a[1], b[0], b[1]], fill=steel, outline=INK)
    for x in np.arange(U_X0 + 0.6, U_X1, 3.0):
        for z in np.arange(U_Z0 + 1.5, U_Z1, 4.0):
            d.line([P(x, z), P(x + 3, z + 4)], fill=brace, width=int(0.14 * PX))
            d.line([P(x + 3, z), P(x, z + 4)], fill=brace, width=int(0.14 * PX))
    # 小さな歯車を散らす（空洞の中だけ）
    cav = np.array(cavity) > 0
    ys, xs = np.nonzero(cav[::8, ::8])
    pick = rnd.sample(range(len(xs)), min(len(xs), 1400))
    for i in pick:
        sz = int(rnd.uniform(0.35, 1.1) * PX)
        g = gear_img(sz, rnd.uniform(0.55, 0.85))
        img.alpha_composite(g, (int(xs[i] * 8 - sz / 2), int(ys[i] * 8 - sz / 2)))
    d = ImageDraw.Draw(img)
    # 手足の芯に沿って、油圧シリンダーとケーブルの束を通す（参考：フレームに沿って走るケーブル）
    for p in parts:
        if "axis_a" not in p.kw:
            continue
        (ax, az), (bx, bz) = p.kw["axis_a"], p.kw["axis_b"]
        L = math.hypot(bx - ax, bz - az)
        nx, nz = -(bz - az) / L, (bx - ax) / L
        r = p.kw["axis_r"] - p.thick
        if r > 1.0:  # 太い部品：両脇にシリンダー
            for s in (-1, 1):
                off = r * 0.62 * s
                a, b = (ax + nx * off, az + nz * off), (bx + nx * off, bz + nz * off)
                d.line([P(*a), P(*b)], fill=INK, width=int(0.62 * PX))
                d.line([P(*a), P(*b)], fill=(110, 122, 134, 255), width=int(0.48 * PX))
                mid = (a[0] + (b[0] - a[0]) * 0.55, a[1] + (b[1] - a[1]) * 0.55)
                d.line([P(*mid), P(*b)], fill=(214, 222, 228, 255), width=int(0.2 * PX))
                for e in (a, b):
                    q = P(*e); rr_ = 0.28 * PX
                    d.ellipse([q[0] - rr_, q[1] - rr_, q[0] + rr_, q[1] + rr_], fill=hexc(YELLOW), outline=INK, width=2)
        # ケーブルの束（芯に沿って、少しうねらせる）
        n = 5 if r > 1.0 else (3 if r > 0.3 else 2)
        for k in range(n):
            off = (k - (n - 1) / 2) * 0.17 + (r * 0.25 if r > 1.0 else 0)
            pts = []
            for i in range(31):
                t = i / 30
                w = math.sin(t * math.pi * 3 + k) * 0.06
                pts.append(P(ax + (bx - ax) * t + nx * (off + w), az + (bz - az) * t + nz * (off + w)))
            cable_line(d, pts, BACK_CABLES[k % len(BACK_CABLES)], int(0.1 * PX), int(0.17 * PX))
        # クランプ
        for t in (0.25, 0.5, 0.75):
            x, z = ax + (bx - ax) * t, az + (bz - az) * t
            o = (r * 0.25 if r > 1.0 else 0)
            a = P(x + nx * (o - 0.55), z + nz * (o - 0.55)); b = P(x + nx * (o + 0.55), z + nz * (o + 0.55))
            d.line([a, b], fill=INK, width=int(0.3 * PX)); d.line([a, b], fill=(160, 168, 176, 255), width=int(0.18 * PX))
    # 胴の中を横切るケーブルの束
    for z, x0, x1, sag in ((45.0, 11, 29, 2.0), (33.4, 16.5, 23.5, 1.0), (30.6, 13, 27, 1.4), (54.6, 17, 23, 1.0), (46.4, 1.0, 9.5, 1.2), (46.4, 30.5, 39, 1.2)):
        for k in range(4):
            pts = [P(x0 + (x1 - x0) * t, z - k * 0.16 - sag * math.sin(math.pi * t)) for t in [i / 30 for i in range(31)]]
            cable_line(d, pts, BACK_CABLES[(k * 2) % len(BACK_CABLES)], int(0.1 * PX), int(0.17 * PX))
    # 空洞の形で切り抜き、もやを掛けて薄くする（操縦士の層を見やすく）
    haze = Image.new("RGBA", (W, H), (52, 62, 72, 255))
    img = Image.blend(img, haze, 0.42)
    img.putalpha(cavity)
    # 空洞のふち（装甲の断面）
    rim = grow_mask(cavity, 0.2 * PX)
    rim = Image.fromarray(np.where((np.array(rim) > 0) & (np.array(cavity) == 0), 255, 0).astype(np.uint8))
    rim_img = Image.new("RGBA", (W, H), (120, 130, 140, 255)); rim_img.putalpha(rim)
    img.alpha_composite(rim_img)
    return img


def build_front(cavity):
    rnd = random.Random(21)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cav = np.array(cavity) > 0
    ys, xs = np.nonzero(cav[::16, ::16])
    for i in rnd.sample(range(len(xs)), min(len(xs), 70)):
        x0, y0 = xs[i] * 16, ys[i] * 16
        span = rnd.uniform(2.0, 4.0) * PX
        sag = rnd.uniform(0.6, 1.5) * PX
        for k in range(rnd.choice((1, 2, 2, 3))):
            pts = [(x0 + span * t, y0 + k * 6 + math.sin(math.pi * t) * sag) for t in [j / 24 for j in range(25)]]
            cable_line(d, pts, FRONT_CABLES[(i + k) % len(FRONT_CABLES)], int(0.13 * PX), int(0.22 * PX))
    for i in rnd.sample(range(len(xs)), min(len(xs), 40)):
        x0, y0 = xs[i] * 16, ys[i] * 16
        l = rnd.uniform(1.0, 2.4) * PX
        pts = [(x0 + math.sin(j * 0.7) * 0.2 * PX, y0 + j * l / 8) for j in range(9)]
        cable_line(d, pts, FRONT_CABLES[i % len(FRONT_CABLES)], int(0.13 * PX), int(0.22 * PX))
        q = pts[-1]; s = 0.14 * PX
        d.rectangle([q[0] - s, q[1], q[0] + s, q[1] + 0.3 * PX], fill=(150, 150, 156, 255), outline=INK, width=2)
    img.putalpha(Image.fromarray(np.minimum(np.array(img.split()[3]), np.array(cavity))))
    return img


# ------------------------------------------------------------------ 足場とはしごの確認

def check_layout(cavity):
    src = open(CS_MAP, encoding="utf-8").read()
    cav = np.array(cavity) > 0

    def inside(x, z):
        px, py = P(x, z)
        px, py = int(px), int(py)
        return 0 <= px < W and 0 <= py < H and cav[py, px]

    problems = []
    for m in re.finditer(r"C\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f\)", src):
        x0, x1, y = map(float, m.groups())
        for x in np.arange(x0 + 0.2, x1 - 0.2 + 1e-6, 0.25):
            if not inside(x, y + 0.5) or not inside(x, y + 1.3):
                problems.append(f"足場 C({x0}, {x1}, {y}) の x={x:.2f} が空洞の外"); break
    for m in re.finditer(r"L\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f\)", src):
        x, y0, y1 = map(float, m.groups())
        for z in np.arange(y0 + 0.3, y1, 0.25):
            if not inside(x, z):
                problems.append(f"はしご L({x}, {y0}, {y1}) の z={z:.2f} が空洞の外"); break
    return problems


def build():
    parts = build_parts()
    full = mask_of(parts)
    cavity = mask_of(parts, shrink=True)
    armor = build_armor(parts)
    back = build_back(parts, cavity)
    front = build_front(cavity)
    small = lambda im: im.resize((W // SS, H // SS), Image.LANCZOS)
    os.makedirs(OUT, exist_ok=True)
    small(armor).save(os.path.join(OUT, "robot_armor.png"))
    small(back).save(os.path.join(OUT, "robot_back.png"))
    small(front).save(os.path.join(OUT, "robot_front.png"))
    problems = check_layout(cavity)
    return problems


if __name__ == "__main__":
    probs = build()
    print("robot layers:", W // SS, "x", H // SS)
    for p in probs:
        print("  !", p)
    if not probs:
        print("  足場とはしごはすべて空洞の中に収まっています")
