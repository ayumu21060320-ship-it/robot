"""ロボット内部（画面の左半分）用の2Dイラストを描き出すスクリプト。

太い輪郭線とベタ塗りのカートゥーン調。すべてコードで描いているので、
色や形を変えたいときはここを直して実行し直せばよい。

    python3 tools/art/generate_art.py            # robot/Assets/Resources/Art/ に書き出す
    python3 tools/art/generate_art.py --preview  # 見本画像 art_preview.png も作る

操縦士は「色を付ける層（*_body）」と「輪郭や顔の層（*_detail）」に分けてあり、
Unity 側で body 層に乗組員ごとの色を掛ける。
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "robot", "Assets", "Resources", "Art")

SS = 4            # なめらかに描くため4倍で描いて縮小する
PPM = 64          # 1メートルあたりのピクセル数（Unity 側の Pixels Per Unit と合わせる）

INK = (28, 32, 38, 255)        # 輪郭線
INK_W = 5                      # 輪郭線の太さ（縮小後のピクセル）


def canvas(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))


def finish(img):
    w, h = img.size
    return img.resize((w // SS, h // SS), Image.LANCZOS)


def px(v):
    return int(round(v * SS))


def rr(d, box, r, fill=None, outline=None, width=INK_W):
    d.rounded_rectangle([px(box[0]), px(box[1]), px(box[2]), px(box[3])], radius=px(r),
                        fill=fill, outline=outline, width=px(width) if outline else 0)


def ell(d, box, fill=None, outline=None, width=INK_W):
    d.ellipse([px(box[0]), px(box[1]), px(box[2]), px(box[3])], fill=fill,
              outline=outline, width=px(width) if outline else 0)


def poly(d, pts, fill=None, outline=None, width=INK_W):
    p = [(px(x), px(y)) for x, y in pts]
    if fill:
        d.polygon(p, fill=fill)
    if outline:
        d.line(p + [p[0]], fill=outline, width=px(width), joint="curve")


def line(d, pts, fill=INK, width=INK_W):
    d.line([(px(x), px(y)) for x, y in pts], fill=fill, width=px(width), joint="curve")


# ------------------------------------------------------------------ 操縦士

def pilot(frame):
    """横向き（右向き）の操縦士。frame: 0=立ち, 1/2=歩き。128x128 で足元が下端から 8px。"""
    S = 128
    body, detail = canvas(S, S), canvas(S, S)
    b, d = ImageDraw.Draw(body), ImageDraw.Draw(detail)

    WHITE, SHADE = (255, 255, 255, 255), (196, 196, 204, 255)
    BOOT, BOOT_HI = (52, 58, 66, 255), (86, 94, 104, 255)

    stride = {0: 0, 1: 9, 2: -9}[frame]
    bob = 0 if frame == 0 else -3
    cx = 62

    # 足（ブーツ）: 奥の足 → 手前の足
    for i, off in enumerate((-stride, stride)):
        x0 = cx - 15 + off + (i * 8)
        rr(d, (x0, 96 + bob * 0.3, x0 + 20, 120), 6, fill=BOOT if i == 1 else (40, 45, 52, 255), outline=INK)
        rr(d, (x0 + 3, 100 + bob * 0.3, x0 + 12, 104 + bob * 0.3), 2, fill=BOOT_HI)

    # 胴体（つなぎ）
    torso = (cx - 22, 60 + bob, cx + 22, 104 + bob)
    rr(b, torso, 14, fill=WHITE)
    rr(b, (cx - 22, 84 + bob, cx + 22, 104 + bob), 12, fill=SHADE)
    rr(d, torso, 14, outline=INK)
    # ベルトと胸のバッジ（色を付けない）
    rr(d, (cx - 21, 86 + bob, cx + 21, 92 + bob), 2, fill=(52, 58, 66, 255))
    rr(d, (cx + 2, 66 + bob, cx + 14, 76 + bob), 3, fill=(255, 190, 40, 255), outline=INK, width=3)

    # 背中のボンベ
    rr(d, (cx - 32, 62 + bob, cx - 18, 94 + bob), 6, fill=(150, 160, 170, 255), outline=INK)
    rr(d, (cx - 29, 66 + bob, cx - 25, 88 + bob), 2, fill=(205, 212, 218, 255))

    # 腕（前後に振る）
    swing = {0: 0, 1: -8, 2: 8}[frame]
    rr(b, (cx - 6 + swing * 0.5, 70 + bob, cx + 8 + swing * 0.5, 96 + bob), 7, fill=SHADE)
    rr(d, (cx - 6 + swing * 0.5, 70 + bob, cx + 8 + swing * 0.5, 96 + bob), 7, outline=INK)
    ell(b, (cx - 5 + swing * 0.5, 90 + bob, cx + 9 + swing * 0.5, 103 + bob), fill=WHITE)
    ell(d, (cx - 5 + swing * 0.5, 90 + bob, cx + 9 + swing * 0.5, 103 + bob), outline=INK, width=3)

    # ヘルメット（大きな丸）
    head = (cx - 26, 14 + bob, cx + 26, 66 + bob)
    ell(b, head, fill=WHITE)
    ell(b, (cx - 26, 30 + bob, cx + 22, 66 + bob), fill=SHADE)
    ell(b, (cx - 24, 16 + bob, cx + 20, 56 + bob), fill=WHITE)
    ell(d, head, outline=INK)
    # 耳のヘッドセット
    ell(d, (cx - 22, 34 + bob, cx - 8, 50 + bob), fill=(52, 58, 66, 255), outline=INK, width=3)
    # ゴーグル型のバイザー（前向き）
    rr(d, (cx - 2, 26 + bob, cx + 30, 46 + bob), 10, fill=(64, 200, 236, 255), outline=INK)
    rr(d, (cx + 3, 29 + bob, cx + 26, 35 + bob), 3, fill=(196, 246, 255, 255))
    # アンテナ
    line(d, [(cx - 6, 15 + bob), (cx - 12, 3 + bob)], width=4)
    ell(d, (cx - 18, -2 + bob, cx - 6, 10 + bob), fill=(255, 92, 92, 255), outline=INK, width=3)

    return finish(body), finish(detail)


# ------------------------------------------------------------------ 床・壁

def floor_tile():
    """2m x 2m の床。白っぽく描き、Unity 側で部屋ごとの色を掛ける。"""
    S = PPM * 2
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    rr(d, (0, 0, S, S), 0, fill=(232, 236, 240, 255))
    for x0, y0 in ((0, 0), (S / 2, 0), (0, S / 2), (S / 2, S / 2)):
        rr(d, (x0 + 2, y0 + 2, x0 + S / 2 - 2, y0 + S / 2 - 2), 4, fill=(246, 248, 250, 255))
        rr(d, (x0 + 2, y0 + S / 2 - 9, x0 + S / 2 - 2, y0 + S / 2 - 2), 3, fill=(214, 220, 226, 255))
        for rx, ry in ((7, 7), (S / 2 - 11, 7), (7, S / 2 - 15), (S / 2 - 11, S / 2 - 15)):
            ell(d, (x0 + rx, y0 + ry, x0 + rx + 4, y0 + ry + 4), fill=(180, 188, 196, 255))
    # 目地
    line(d, [(0, 0), (S, 0)], fill=(150, 158, 166, 255), width=3)
    line(d, [(0, 0), (0, S)], fill=(150, 158, 166, 255), width=3)
    line(d, [(S / 2, 0), (S / 2, S)], fill=(176, 184, 192, 255), width=2)
    line(d, [(0, S / 2), (S, S / 2)], fill=(176, 184, 192, 255), width=2)
    return finish(img)


def wall_face():
    """壁の手前側の面（横に繰り返す）。幅2m x 高さ1m。"""
    W, H = PPM * 2, PPM
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    rr(d, (0, 0, W, H), 0, fill=(92, 106, 118, 255))
    rr(d, (0, 0, W, 10), 0, fill=(122, 138, 150, 255))
    for x0 in (0, W / 2):
        rr(d, (x0 + 4, 16, x0 + W / 2 - 4, H - 12), 5, fill=(104, 120, 132, 255), outline=(66, 76, 86, 255), width=2)
        rr(d, (x0 + 12, 24, x0 + 26, 30), 2, fill=(255, 196, 64, 255))
    rr(d, (0, H - 8, W, H), 0, fill=(56, 64, 72, 255))
    line(d, [(0, 0), (0, H)], fill=(60, 70, 80, 255), width=3)
    return finish(img)


def wall_cap():
    """壁の上面（9スライスで伸ばす）。"""
    S = 32
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    rr(d, (0, 0, S, S), 4, fill=(48, 56, 64, 255))
    rr(d, (3, 3, S - 3, S - 3), 3, fill=(64, 74, 84, 255))
    return finish(img)


# ------------------------------------------------------------------ 操作盤

def console(kind):
    """斜め上から見た操作盤。kind: leg / arm / head。160x128（2.5m x 2m）。"""
    W, H = 160, 128
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    accent = {"leg": (79, 210, 255, 255), "arm": (255, 154, 60, 255), "head": (184, 255, 90, 255)}[kind]
    dark_accent = tuple(int(c * 0.45) for c in accent[:3]) + (255,)

    # 本体（手前の面と上面）
    rr(d, (14, 50, W - 14, H - 10), 12, fill=(70, 80, 92, 255), outline=INK)
    rr(d, (14, 22, W - 14, 78), 12, fill=(122, 136, 148, 255), outline=INK)
    # 画面
    rr(d, (30, 28, W - 30, 62), 7, fill=dark_accent, outline=INK, width=4)
    if kind == "leg":
        for i, x in enumerate((50, 104)):
            rr(d, (x, 34, x + 8, 56), 3, fill=(20, 40, 52, 255))
            rr(d, (x, 44 - i * 6, x + 8, 56), 3, fill=accent)
    elif kind == "arm":
        ell(d, (64, 32, 96, 58), outline=accent, width=4)
        rr(d, (72, 38, 88, 52), 4, fill=accent)
    else:
        ell(d, (58, 30, 102, 60), outline=accent, width=3)
        line(d, [(80, 30), (80, 60)], fill=accent, width=3)
        line(d, [(58, 45), (102, 45)], fill=accent, width=3)
        ell(d, (88, 36, 94, 42), fill=accent)
    # 手前のボタン
    for i, col in enumerate(((255, 92, 92, 255), (255, 210, 64, 255), accent)):
        ell(d, (40 + i * 30, 88, 56 + i * 30, 102), fill=col, outline=INK, width=3)
    # レバー
    line(d, [(W - 34, 96), (W - 30, 72)], width=6)
    ell(d, (W - 40, 62, W - 22, 80), fill=(255, 92, 92, 255), outline=INK, width=4)
    return finish(img)


def reactor():
    """動力炉（3m x 3m）。光る芯は別の層にして Unity 側で明るさを変える。"""
    S = 192
    base, core = canvas(S, S), canvas(S, S)
    d, c = ImageDraw.Draw(base), ImageDraw.Draw(core)
    # 床の台座
    ell(d, (16, 96, S - 16, S - 12), fill=(56, 64, 72, 255), outline=INK)
    # 配管
    for x in (24, S - 44):
        rr(d, (x, 40, x + 20, 150), 8, fill=(132, 146, 158, 255), outline=INK)
        for y in (60, 100):
            rr(d, (x - 3, y, x + 23, y + 8), 3, fill=(255, 190, 40, 255), outline=INK, width=3)
    # 円筒
    rr(d, (52, 30, S - 52, 160), 26, fill=(96, 108, 120, 255), outline=INK)
    rr(c, (64, 44, S - 64, 146), 18, fill=(255, 255, 255, 255))
    rr(c, (74, 52, 88, 138), 7, fill=(255, 255, 255, 255))
    rr(d, (64, 44, S - 64, 146), 18, outline=INK, width=4)
    for y in (70, 96, 122):
        line(d, [(64, y), (S - 64, y)], fill=(28, 32, 38, 140), width=3)
    ell(d, (52, 18, S - 52, 46), fill=(140, 154, 166, 255), outline=INK)
    ell(d, (70, 24, S - 70, 40), fill=(80, 92, 104, 255))
    return finish(base), finish(core)


def fire(frame):
    S = 96
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    sway = 5 if frame == 0 else -5
    for scale, col in ((1.0, (255, 96, 40, 255)), (0.62, (255, 206, 60, 255)), (0.3, (255, 250, 210, 255))):
        cx, base = 48, 88
        h, w = 76 * scale, 34 * scale
        pts = [(cx - w, base), (cx - w * 0.9, base - h * 0.45), (cx - w * 0.3 + sway * 0.5, base - h * 0.62),
               (cx - w * 0.15 + sway, base - h), (cx + w * 0.25, base - h * 0.55), (cx + w * 0.55 + sway * 0.6, base - h * 0.8),
               (cx + w, base - h * 0.35), (cx + w * 0.8, base)]
        poly(d, pts, fill=col, outline=INK if scale == 1.0 else None, width=4)
    return finish(img)


def pad():
    """操作盤の前の立ち位置（1.5m）。Unity 側で席の色を掛ける。"""
    S = 96
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    ell(d, (4, 4, S - 4, S - 4), fill=(150, 150, 150, 255), outline=INK, width=4)
    ell(d, (14, 14, S - 14, S - 14), fill=(255, 255, 255, 255))
    ell(d, (24, 24, S - 24, S - 24), fill=(120, 120, 120, 255))
    for a in range(0, 360, 45):
        x, y = 48 + math.cos(math.radians(a)) * 38, 48 + math.sin(math.radians(a)) * 38
        ell(d, (x - 2.5, y - 2.5, x + 2.5, y + 2.5), fill=(60, 66, 74, 255))
    return finish(img)


def shadow():
    W, H = 96, 32
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(img).ellipse([10, 6, W - 10, H - 6], fill=(0, 0, 0, 110))
    return img.filter(ImageFilter.GaussianBlur(2))


def gear():
    """関節の歯車（3m）。"""
    S = 192
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    c = S / 2
    teeth = 12
    pts = []
    for i in range(teeth * 4):
        a = i / (teeth * 4) * math.tau
        r = 88 if (i % 4) in (1, 2) else 72
        pts.append((c + math.cos(a) * r, c + math.sin(a) * r))
    poly(d, pts, fill=(120, 132, 144, 255), outline=INK, width=5)
    ell(d, (c - 58, c - 58, c + 58, c + 58), fill=(150, 162, 174, 255), outline=(70, 80, 90, 255), width=3)
    for i in range(6):
        a = i / 6 * math.tau
        x, y = c + math.cos(a) * 36, c + math.sin(a) * 36
        ell(d, (x - 9, y - 9, x + 9, y + 9), fill=(70, 80, 90, 255))
    ell(d, (c - 20, c - 20, c + 20, c + 20), fill=(255, 190, 40, 255), outline=INK, width=4)
    ell(d, (c - 7, c - 7, c + 7, c + 7), fill=INK)
    return finish(img)


def piston():
    """縦向きの油圧ピストン（幅1m）。上下 24px は伸ばさない（Unity 側で9スライス）。"""
    W, H = 64, 192
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    rr(d, (14, 4, W - 14, 22), 5, fill=(255, 190, 40, 255), outline=INK, width=4)
    rr(d, (24, 18, W - 24, 100), 4, fill=(214, 222, 228, 255), outline=INK, width=4)
    rr(d, (12, 92, W - 12, H - 20), 8, fill=(96, 108, 120, 255), outline=INK, width=5)
    rr(d, (18, 100, 26, H - 30), 3, fill=(140, 152, 164, 255))
    rr(d, (14, H - 22, W - 14, H - 4), 5, fill=(255, 190, 40, 255), outline=INK, width=4)
    return finish(img)


def white():
    return Image.new("RGBA", (8, 8), (255, 255, 255, 255))


# ------------------------------------------------------------------ 障害物と床の飾り

def crate():
    """木箱（1.3m）。"""
    S = 84
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    rr(d, (6, 22, S - 6, S - 4), 6, fill=(150, 104, 60, 255), outline=INK)
    rr(d, (6, 6, S - 6, 46), 6, fill=(196, 146, 88, 255), outline=INK)
    line(d, [(12, 12), (S - 12, 40)], fill=(150, 104, 60, 255), width=5)
    line(d, [(S - 12, 12), (12, 40)], fill=(150, 104, 60, 255), width=5)
    rr(d, (30, 56, S - 30, 70), 3, fill=(255, 190, 40, 255), outline=INK, width=3)
    return finish(img)


def tanks():
    """ガスタンクの束（2.6m）。"""
    S = 168
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    ell(d, (6, 120, S - 6, S - 6), fill=(40, 46, 54, 255))
    for i, (x, col) in enumerate(((14, (90, 150, 210, 255)), (62, (210, 80, 70, 255)), (110, (90, 150, 210, 255)))):
        rr(d, (x, 34, x + 46, 150), 20, fill=col, outline=INK)
        rr(d, (x + 8, 44, x + 16, 132), 4, fill=(255, 255, 255, 90))
        ell(d, (x, 20, x + 46, 52), fill=tuple(min(255, c + 30) for c in col[:3]) + (255,), outline=INK)
        rr(d, (x + 16, 10, x + 30, 26), 3, fill=(170, 180, 190, 255), outline=INK, width=3)
        rr(d, (x, 96, x + 46, 104), 2, fill=(255, 190, 40, 255))
    return finish(img)


def machine():
    """計器の付いた機械のかたまり（3.9m x 2.6m）。"""
    W, H = 250, 168
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    rr(d, (6, 40, W - 6, H - 6), 10, fill=(70, 80, 92, 255), outline=INK)
    rr(d, (6, 8, W - 6, 96), 10, fill=(122, 136, 148, 255), outline=INK)
    for i in range(3):
        cx = 50 + i * 75
        ell(d, (cx - 24, 22, cx + 24, 70), fill=(232, 236, 240, 255), outline=INK, width=4)
        a = math.radians(200 + i * 55)
        line(d, [(cx, 46), (cx + math.cos(a) * 18, 46 + math.sin(a) * 18)], fill=(220, 60, 50, 255), width=4)
        ell(d, (cx - 4, 42, cx + 4, 50), fill=INK)
    for i in range(6):
        x = 26 + i * 34
        rr(d, (x, 112, x + 22, 150), 3, fill=(40, 48, 56, 255))
        rr(d, (x + 4, 118 + (i * 7) % 18, x + 18, 146), 2, fill=(255, 190, 40, 255) if i % 2 else (90, 220, 120, 255))
    return finish(img)


def monitors():
    """モニターの並んだ机（2.6m x 1.6m）。"""
    W, H = 168, 104
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    rr(d, (4, 50, W - 4, H - 4), 8, fill=(70, 80, 92, 255), outline=INK)
    for i in range(3):
        x = 10 + i * 52
        rr(d, (x, 6, x + 46, 52), 6, fill=(40, 48, 56, 255), outline=INK, width=4)
        rr(d, (x + 6, 12, x + 40, 44), 3, fill=((70, 200, 236, 255), (90, 220, 120, 255), (255, 190, 40, 255))[i])
        for k in range(3):
            line(d, [(x + 10, 18 + k * 8), (x + 30 - k * 6, 18 + k * 8)], fill=(255, 255, 255, 140), width=2)
    return finish(img)


def pipes_h():
    """床をはう配管（横に繰り返す。2m x 0.75m）。"""
    W, H = 128, 48
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    for y, col in ((6, (196, 120, 70, 255)), (26, (120, 160, 190, 255))):
        rr(d, (-10, y, W + 10, y + 16), 7, fill=col, outline=INK, width=3)
        rr(d, (-10, y + 3, W + 10, y + 6), 2, fill=(255, 255, 255, 80))
    for x in (30, 94):
        rr(d, (x, 2, x + 10, 46), 3, fill=(90, 100, 110, 255), outline=INK, width=3)
    return finish(img)


def grate():
    """床の排気口の網（1.3m）。"""
    S = 84
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    rr(d, (4, 4, S - 4, S - 4), 6, fill=(60, 68, 76, 255), outline=INK, width=4)
    for y in range(14, S - 10, 10):
        rr(d, (12, y, S - 12, y + 5), 2, fill=(24, 28, 32, 255))
    return finish(img)


def stripes():
    """警告の縞模様（2m x 0.5m。横に繰り返す）。"""
    W, H = 128, 32
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    rr(d, (0, 0, W, H), 0, fill=(255, 190, 40, 255))
    for x in range(-H, W + H, 32):
        poly(d, [(x, H), (x + 16, H), (x + 16 + H, 0), (x + H, 0)], fill=(30, 34, 40, 255))
    return finish(img)


# ------------------------------------------------------------------ 整備タスクの装置とミニゲーム

def task_panel(kind):
    """壁ぎわの整備装置（1.6m x 1.3m）。"""
    W, H = 104, 84
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    rr(d, (4, 30, W - 4, H - 4), 8, fill=(70, 80, 92, 255), outline=INK)
    rr(d, (4, 4, W - 4, 52), 8, fill=(160, 170, 180, 255), outline=INK)
    rr(d, (10, 56, W - 10, 64), 2, fill=(255, 190, 40, 255))
    cx, cy = W / 2, 28
    if kind == "valve":
        ell(d, (cx - 20, cy - 20, cx + 20, cy + 20), outline=(200, 60, 50, 255), width=6)
        line(d, [(cx - 20, cy), (cx + 20, cy)], fill=(200, 60, 50, 255), width=5)
        line(d, [(cx, cy - 20), (cx, cy + 20)], fill=(200, 60, 50, 255), width=5)
    elif kind == "wires":
        for i, col in enumerate(((220, 60, 50), (60, 120, 220), (240, 200, 40), (80, 200, 90))):
            line(d, [(cx - 30, cy - 14 + i * 9), (cx - 8, cy - 14 + ((i * 3) % 4) * 9), (cx + 30, cy - 14 + ((i * 3) % 4) * 9)], fill=col + (255,), width=4)
    elif kind == "gauge":
        ell(d, (cx - 20, cy - 20, cx + 20, cy + 20), fill=(240, 240, 236, 255), outline=INK, width=4)
        line(d, [(cx, cy), (cx + 12, cy - 12)], fill=(220, 60, 50, 255), width=4)
    elif kind == "fuse":
        for i in range(4):
            rr(d, (cx - 32 + i * 17, cy - 16, cx - 22 + i * 17, cy + 16), 3, fill=(230, 230, 220, 255), outline=INK, width=3)
    elif kind == "scope":
        ell(d, (cx - 20, cy - 20, cx + 20, cy + 20), fill=(30, 60, 40, 255), outline=INK, width=4)
        ell(d, (cx - 10, cy - 10, cx + 10, cy + 10), outline=(150, 255, 90, 255), width=3)
        line(d, [(cx - 20, cy), (cx + 20, cy)], fill=(150, 255, 90, 255), width=2)
    elif kind == "vent":
        for y in range(cy - 16, cy + 18, 8):
            rr(d, (cx - 30, y, cx + 30, y + 4), 2, fill=(30, 34, 40, 255))
    elif kind == "gyro":
        ell(d, (cx - 20, cy - 20, cx + 20, cy + 20), outline=(70, 200, 236, 255), width=4)
        ell(d, (cx - 20, cy - 8, cx + 20, cy + 8), outline=(70, 200, 236, 255), width=3)
        ell(d, (cx - 5, cy - 5, cx + 5, cy + 5), fill=(255, 190, 40, 255))
    return finish(img)


def alert():
    """壊れた装置の上に出す「！」（0.9m）。"""
    S = 58
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    ell(d, (4, 4, S - 4, S - 4), fill=(235, 60, 50, 255), outline=INK, width=4)
    rr(d, (S / 2 - 4, 13, S / 2 + 4, 34), 3, fill=(255, 255, 255, 255))
    ell(d, (S / 2 - 4.5, 38, S / 2 + 4.5, 47), fill=(255, 255, 255, 255))
    return finish(img)


def valve_wheel():
    """ミニゲーム用の大きなバルブ（256px）。"""
    S = 256
    img = canvas(S, S)
    d = ImageDraw.Draw(img)
    c = S / 2
    ell(d, (14, 14, S - 14, S - 14), outline=(200, 60, 50, 255), width=26)
    ell(d, (14, 14, S - 14, S - 14), outline=INK, width=5)
    ell(d, (40, 40, S - 40, S - 40), outline=INK, width=5)
    for a in (0, 60, 120):
        r = math.radians(a)
        dx, dy = math.cos(r) * 100, math.sin(r) * 100
        line(d, [(c - dx, c - dy), (c + dx, c + dy)], fill=INK, width=20)
        line(d, [(c - dx, c - dy), (c + dx, c + dy)], fill=(200, 60, 50, 255), width=12)
    ell(d, (c - 26, c - 26, c + 26, c + 26), fill=(160, 170, 180, 255), outline=INK, width=5)
    return finish(img)


# ------------------------------------------------------------------ 書き出し

def build():
    os.makedirs(OUT, exist_ok=True)
    out = {}
    for f in range(3):
        b, dt = pilot(f)
        out[f"pilot_body_{f}"] = b
        out[f"pilot_detail_{f}"] = dt
    out["floor"] = floor_tile()
    out["wall_face"] = wall_face()
    out["wall_cap"] = wall_cap()
    for k in ("leg", "arm", "head"):
        out[f"console_{k}"] = console(k)
    out["reactor_base"], out["reactor_core"] = reactor()
    out["fire_0"], out["fire_1"] = fire(0), fire(1)
    out["pad"] = pad()
    out["shadow"] = shadow()
    out["white"] = white()
    out["gear"] = gear()
    out["piston"] = piston()
    out["crate"] = crate()
    out["tanks"] = tanks()
    out["machine"] = machine()
    out["monitors"] = monitors()
    out["pipes_h"] = pipes_h()
    out["grate"] = grate()
    out["stripes"] = stripes()
    for k in ("valve", "wires", "gauge", "fuse", "scope", "vent", "gyro"):
        out["task_" + k] = task_panel(k)
    out["alert"] = alert()
    out["valve_wheel"] = valve_wheel()
    for name, img in out.items():
        img.save(os.path.join(OUT, name + ".png"))
    return out


def tint(img, rgb):
    r, g, b = rgb
    px_ = img.load()
    out = img.copy()
    po = out.load()
    for y in range(img.size[1]):
        for x in range(img.size[0]):
            pr, pg, pb, pa = px_[x, y]
            po[x, y] = (pr * r // 255, pg * g // 255, pb * b // 255, pa)
    return out


def preview(art, path):
    """胸部ホールと右腕室あたりを、Unity と同じ考え方（床→壁の面→壁の上面→物→人）で並べた見本。"""
    W, H = 1100, 700
    img = Image.new("RGBA", (W, H), (11, 15, 18, 255))
    floor = art["floor"]

    def fill_floor(x0, y0, x1, y1, col):
        t = tint(floor, col)
        for y in range(y0, y1, t.size[1]):
            for x in range(x0, x1, t.size[0]):
                tile = t.crop((0, 0, min(t.size[0], x1 - x), min(t.size[1], y1 - y)))
                img.alpha_composite(tile, (x, y))

    rooms = [  # 画面上のピクセル座標で、胸部ホール・右肩通路・右腕室・首の通路
        (120, 250, 700, 600, (176, 186, 196)),
        (700, 360, 860, 460, (150, 160, 170)),
        (860, 180, 1060, 640, (214, 190, 160)),
        (330, 60, 490, 250, (150, 160, 170)),
    ]
    for x0, y0, x1, y1, col in rooms:
        fill_floor(x0, y0, x1, y1, col)

    d = ImageDraw.Draw(img)
    cap = (48, 56, 64, 255)
    face = art["wall_face"]

    def hwall(x0, x1, y, with_face):
        if with_face:
            for x in range(x0, x1, face.size[0]):
                img.alpha_composite(face.crop((0, 0, min(face.size[0], x1 - x), face.size[1])), (x, y))
        d.rectangle([x0 - 10, y - 14, x1 + 10, y + 2], fill=cap)
        d.rectangle([x0 - 10, y - 14, x1 + 10, y - 11], fill=(86, 98, 110, 255))

    def vwall(x, y0, y1):
        d.rectangle([x - 10, y0 - 14, x + 10, y1 + 2], fill=cap)
        d.rectangle([x - 10, y0 - 14, x + 10, y0 - 11], fill=(86, 98, 110, 255))

    hwall(120, 330, 250, True); hwall(490, 700, 250, True)
    hwall(700, 860, 360, True); hwall(860, 1060, 180, True)
    hwall(120, 700, 600, False); hwall(700, 860, 460, False); hwall(860, 1060, 640, False)
    vwall(120, 250, 600); vwall(700, 250, 360); vwall(700, 460, 600)
    vwall(860, 180, 360); vwall(860, 460, 640); vwall(1060, 180, 640)
    vwall(330, 60, 250); vwall(490, 60, 250)

    img.alpha_composite(art["console_arm"], (900, 380))
    img.alpha_composite(art["console_head"], (330, 110))
    rc = tint(art["reactor_core"], (255, 210, 63))
    img.alpha_composite(art["reactor_base"], (520, 360)); img.alpha_composite(rc, (520, 360))
    img.alpha_composite(art["fire_0"], (960, 520))

    for (x, y), col, f, flip in (((240, 400), (63, 182, 255), 1, False), ((420, 470), (255, 90, 138), 0, True),
                                 ((900, 480), (125, 255, 106), 2, False)):
        b = tint(art[f"pilot_body_{f}"], col)
        dt = art[f"pilot_detail_{f}"]
        if flip:
            b, dt = b.transpose(Image.FLIP_LEFT_RIGHT), dt.transpose(Image.FLIP_LEFT_RIGHT)
        shadow = Image.new("RGBA", b.size, (0, 0, 0, 0))
        ImageDraw.Draw(shadow).ellipse([30, 112, 98, 126], fill=(0, 0, 0, 90))
        img.alpha_composite(shadow, (x, y)); img.alpha_composite(b, (x, y)); img.alpha_composite(dt, (x, y))

    img.convert("RGB").save(path)


if __name__ == "__main__":
    art = build()
    print("wrote", len(art), "images to", OUT)
    if "--preview" in sys.argv:
        p = sys.argv[sys.argv.index("--preview") + 1] if len(sys.argv) > sys.argv.index("--preview") + 1 else "art_preview.png"
        preview(art, p)
        print("preview", p)
