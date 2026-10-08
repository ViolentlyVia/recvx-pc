"""Draw the program icons: pc/res/recvx.ico (game) and pc/res/recvx-config.ico (settings app).

usage: python pc/tools/make_icons.py   (needs Pillow; the .ico files are kept in the repository)

An original monogram, not game artwork: a red serif "CV" on a dark rounded square; the settings
app's icon adds a gear.
"""
import math
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "pc", "res")
SIZES = [16, 24, 32, 48, 64, 128, 256]
BIG = 1024   # drawn large, scaled down for each size


def font(px):
    for name in ("georgiab.ttf", "timesbd.ttf", "arialbd.ttf"):
        try:
            return ImageFont.truetype(name, px)
        except OSError:
            pass
    return ImageFont.load_default()


def base():
    im = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    m = BIG // 16
    d.rounded_rectangle((m, m, BIG - m, BIG - m), radius=BIG // 6, fill=(22, 20, 22, 255),
                        outline=(120, 16, 16, 255), width=BIG // 40)
    f = font(int(BIG * 0.52))
    text = "CV"
    box = d.textbbox((0, 0), text, font=f)
    x = (BIG - (box[2] - box[0])) / 2 - box[0]
    y = (BIG - (box[3] - box[1])) / 2 - box[1]
    d.text((x + BIG // 90, y + BIG // 90), text, font=f, fill=(0, 0, 0, 200))   # shadow
    d.text((x, y), text, font=f, fill=(196, 24, 24, 255))
    return im


def gear(im):
    d = ImageDraw.Draw(im)
    cx, cy, r = BIG * 0.70, BIG * 0.70, BIG * 0.18
    teeth = 8
    pts = []
    for i in range(teeth * 2):
        a = math.pi * i / teeth
        rr = r if i % 2 == 0 else r * 0.78
        for da in (-0.17, 0.17):
            pts.append((cx + rr * math.cos(a + da), cy + rr * math.sin(a + da)))
    d.polygon(pts, fill=(200, 200, 204, 255), outline=(30, 30, 34, 255))
    d.ellipse((cx - r * 0.62, cy - r * 0.62, cx + r * 0.62, cy + r * 0.62), fill=(200, 200, 204, 255),
              outline=(30, 30, 34, 255), width=BIG // 120)
    d.ellipse((cx - r * 0.3, cy - r * 0.3, cx + r * 0.3, cy + r * 0.3), fill=(22, 20, 22, 255))
    return im


def save(im, name):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    im.resize((256, 256), Image.LANCZOS).save(path, sizes=[(s, s) for s in SIZES])
    print("wrote " + path)


if __name__ == "__main__":
    save(base(), "recvx.ico")
    save(gear(base()), "recvx-config.ico")
