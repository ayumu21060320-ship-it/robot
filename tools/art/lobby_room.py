"""出撃前の「待機室」の絵（横から見た断面図）を描き出すスクリプト。

ロボット内部と同じ、太い輪郭線とベタ塗りのカートゥーン調。
足場・はしご・木箱などは Unity 側で generate_art.py の絵を上に置くので、
ここでは壁・窓・座席・扉・床など、動かない背景だけを描く。

    python3 tools/art/lobby_room.py            # robot/Assets/Resources/Art/robot_lobby.png に書き出す
    python3 tools/art/lobby_room.py --preview  # 足場の位置を重ねた見本 lobby_preview.png も作る

座標はメートル。InteriorMap.cs の LobbyRect / 待機室の足場（Deck）と合わせること。
"""
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "robot", "Assets", "Resources", "Art", "robot_lobby.png")

# 絵が覆う範囲（メートル）。InteriorMap.LobbyRect と同じ
X0, X1, Y0, Y1 = 98.0, 126.0, -2.0, 12.0
PPM = 32          # 1メートルあたりのピクセル数（InteriorMap.LayerPixelsPerUnit と同じ）
SS = 3            # なめらかに描くため3倍で描いて縮小する
W, H = int((X1 - X0) * PPM), int((Y1 - Y0) * PPM)

INK = (26, 30, 36, 255)
LW = 4            # 輪郭線の太さ（縮小後のピクセル）

# 部屋の内側（メートル）
RX0, RX1, RY0, RY1 = 100.0, 124.0, 0.0, 10.0
MEZZ_Y, MEZZ_X1 = 5.2, 109.2     # 左上の中二階


def P(x, y):
    return ((x - X0) * PPM * SS, (Y1 - y) * PPM * SS)


def box(d, x0, y0, x1, y1, fill, outline=INK, width=LW, r=0.0):
    a, b = P(x0, y1), P(x1, y0)
    if r > 0:
        d.rounded_rectangle([a, b], radius=r * PPM * SS, fill=fill, outline=outline, width=width * SS if outline else 0)
    else:
        d.rectangle([a, b], fill=fill, outline=outline, width=width * SS if outline else 0)


def poly(d, pts, fill, outline=INK, width=LW):
    q = [P(x, y) for x, y in pts]
    d.polygon(q, fill=fill)
    if outline:
        d.line(q + [q[0]], fill=outline, width=width * SS, joint="curve")


def line(d, pts, fill=INK, width=LW):
    d.line([P(x, y) for x, y in pts], fill=fill, width=int(width * SS), joint="curve")


def draw():
    img = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    HULL, HULL_HI = (58, 66, 74, 255), (84, 94, 104, 255)
    WALL, WALL_LO, SEAM = (150, 160, 168, 255), (118, 128, 138, 255), (104, 112, 122, 255)

    # ---- 外殻（角を落とした箱）
    poly(d, [(99.0, -1.2), (125.0, -1.2), (125.6, 0.4), (125.6, 9.6), (124.2, 11.2), (99.8, 11.2), (98.4, 9.6), (98.4, 0.4)], HULL)
    poly(d, [(99.4, 10.2), (124.6, 10.2), (124.0, 10.9), (100.0, 10.9)], HULL_HI, outline=None)
    for x in (101.0, 106.0, 112.0, 118.0, 123.0):          # 外殻のリベット
        dx, dy = P(x, 10.6)
        d.ellipse([dx - 5 * SS, dy - 5 * SS, dx + 5 * SS, dy + 5 * SS], fill=(120, 130, 140, 255), outline=INK, width=2 * SS)

    # ---- 奥の壁（パネル張り）
    box(d, RX0, RY0, RX1, RY1, WALL)
    box(d, RX0, RY0, RX1, 2.6, WALL_LO, outline=None)
    for x in range(int(RX0) + 2, int(RX1), 2):
        line(d, [(x, RY0), (x, RY1)], fill=SEAM, width=2)
    for y in (2.6, 7.6):
        line(d, [(RX0, y), (RX1, y)], fill=SEAM, width=2)
    box(d, RX0, RY0, RX1, RY1, None)

    # ---- 天井の照明
    for x in (102.0, 107.0, 113.0, 119.0):
        box(d, x, 9.45, x + 2.6, 9.8, (255, 226, 140, 255), r=0.1)

    # ---- 大きな窓：格納庫の中に立つタイタンの脚が見える
    WX0, WX1, WY0, WY1 = 110.4, 119.2, 3.0, 8.8
    box(d, WX0 - 0.35, WY0 - 0.35, WX1 + 0.35, WY1 + 0.35, HULL, r=0.5)
    box(d, WX0, WY0, WX1, WY1, (34, 52, 74, 255), r=0.35)
    # 格納庫の奥の柱と照明
    for x in (111.6, 114.8, 118.0):
        box(d, x, WY0, x + 0.5, WY1, (46, 66, 90, 255), outline=None)
    for x in (112.2, 116.4):
        box(d, x, 8.0, x + 1.4, 8.25, (150, 220, 255, 255), outline=None)
    # タイタンの脚（赤白の角ばった装甲）
    RED, WHITE_A, FRAME = (196, 62, 58, 255), (226, 230, 236, 255), (92, 98, 108, 255)
    for lx in (112.4, 116.0):
        poly(d, [(lx, WY1), (lx + 2.1, WY1), (lx + 2.3, 6.0), (lx + 0.1, 6.0)], WHITE_A, width=3)
        box(d, lx + 0.5, 5.2, lx + 1.8, 6.0, FRAME, width=3)
        poly(d, [(lx - 0.1, 5.2), (lx + 2.4, 5.2), (lx + 2.7, WY0), (lx - 0.4, WY0)], RED, width=3)
        poly(d, [(lx + 0.4, 4.9), (lx + 1.9, 4.9), (lx + 2.0, 3.9), (lx + 0.3, 3.9)], WHITE_A, width=2)
    # 窓ガラスの反射（細い斜めの線）
    GLARE = (120, 160, 200, 255)
    line(d, [(WX1 - 0.8, WY1 - 0.4), (WX1 - 1.9, WY0 + 1.6)], fill=GLARE, width=4)
    line(d, [(WX1 - 0.4, WY1 - 1.2), (WX1 - 1.0, WY0 + 2.6)], fill=GLARE, width=3)
    box(d, WX0, WY0, WX1, WY1, None, r=0.35)

    # ---- 右の出撃ゲート（2枚の扉と、黄色と黒の警告帯）
    GX0, GX1 = 120.0, 123.6
    box(d, GX0 - 0.3, 0.0, GX1 + 0.3, 5.2, HULL, r=0.6)
    box(d, GX0, 0.0, GX1, 4.8, (128, 138, 148, 255), r=0.45)
    gm = (GX0 + GX1) * 0.5
    line(d, [(gm, 0.0), (gm, 4.8)], width=3)
    for gx in (GX0 + 0.5, gm + 0.5):
        box(d, gx, 1.8, gx + 0.8, 3.4, (100, 110, 120, 255), width=2, r=0.2)
    for i in range(9):                     # 警告帯
        x = GX0 - 0.3 + i * 0.45
        poly(d, [(x, 5.2), (x + 0.22, 5.2), (x + 0.5, 5.65), (x + 0.28, 5.65)], (255, 196, 40, 255), outline=None)
    box(d, GX0 - 0.3, 5.2, GX1 + 0.3, 5.65, None, width=3)
    box(d, gm - 0.35, 6.0, gm + 0.35, 6.7, (90, 255, 140, 255), width=3, r=0.3)   # 出撃ランプ

    # ---- 左下：ロッカー（中二階の下）
    for i in range(4):
        x = 100.6 + i * 1.6
        box(d, x, 0.0, x + 1.45, 4.2, (98, 132, 150, 255), width=3, r=0.15)
        for k in range(3):
            line(d, [(x + 0.35, 3.5 - k * 0.22), (x + 1.1, 3.5 - k * 0.22)], fill=(60, 80, 92, 255), width=2)
        box(d, x + 1.05, 1.9, x + 1.2, 2.5, (220, 200, 120, 255), outline=None)

    # ---- 中二階の支柱
    for x in (101.0, 104.9, MEZZ_X1 - 0.5):
        box(d, x, 0.0, x + 0.35, MEZZ_Y, (82, 90, 100, 255), width=3)

    # ---- 座席（中二階の奥と、窓の下）
    SEAT, SEAT_HI = (58, 110, 160, 255), (92, 150, 200, 255)

    def seat(x, y):
        box(d, x, y + 0.45, x + 0.9, y + 1.55, SEAT, width=3, r=0.2)        # 背もたれ
        box(d, x + 0.15, y + 1.0, x + 0.75, y + 1.4, SEAT_HI, outline=None, r=0.12)
        box(d, x - 0.05, y + 0.3, x + 1.0, y + 0.6, SEAT, width=3, r=0.12)  # 座面
        box(d, x + 0.38, y, x + 0.52, y + 0.3, FRAME, outline=None)

    for i in range(6):
        seat(101.6 + i * 1.2, MEZZ_Y)
    for i in range(6):
        seat(111.0 + i * 1.3, 0.0)

    # ---- 床（グレーチングと、黄色の警告線）
    box(d, 99.4, -1.0, 124.6, 0.0, (70, 78, 86, 255))
    for x in range(100, 125):
        line(d, [(x, -0.15), (x, -0.85)], fill=(54, 60, 68, 255), width=2)
    for i in range(int((RX1 - RX0) / 0.6)):
        x = RX0 + i * 0.6
        poly(d, [(x, 0.0), (x + 0.3, 0.0), (x + 0.45, -0.22), (x + 0.15, -0.22)], (255, 196, 40, 255), outline=None)

    return img.resize((W, H), Image.LANCZOS)


def main():
    img = draw()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.save(OUT)
    print("書き出し:", OUT, img.size)
    if "--preview" in sys.argv:
        pv = Image.new("RGBA", img.size, (7, 9, 11, 255))
        pv.alpha_composite(img)
        d = ImageDraw.Draw(pv)
        for (a, b, y) in ((100.4, 123.6, 0.0), (100.4, MEZZ_X1, MEZZ_Y)):
            d.line([((a - X0) * PPM, (Y1 - y) * PPM), ((b - X0) * PPM, (Y1 - y) * PPM)], fill=(255, 0, 255, 255), width=3)
        d.line([((108.6 - X0) * PPM, (Y1 - 0) * PPM), ((108.6 - X0) * PPM, (Y1 - MEZZ_Y) * PPM)], fill=(255, 180, 0, 255), width=3)
        pv.save(os.path.join(ROOT, "lobby_preview.png"))


if __name__ == "__main__":
    main()
