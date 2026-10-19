"""Draws the Bakkwo app icon: the format ring around 바꿔, on a purple macOS-style tile.

Run:  python3 build/icon/make_icon.py   (needs Pillow and a Korean font, e.g. Noto Sans CJK KR)
Writes icon-1024/512/256.png, Bakkwo.icns and Bakkwo.ico into src/Bakkwo.App/Assets/.
"""
import math, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "src", "Bakkwo.App", "Assets")
S = 2048                      # draw at 2x, downsample for smooth edges
FONT_CANDIDATES = [
    "/usr/share/fonts/opentype/noto/NotoSansCJK-Black.ttc",
    "/System/Library/Fonts/AppleSDGothicNeo.ttc",
]

def font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size, index=1 if "Noto" in path else 8)  # KR face / Heavy
            except OSError:
                return ImageFont.truetype(path, size)
    out = subprocess.run(["fc-match", "-f", "%{file}", "Noto Sans CJK KR:black"], capture_output=True, text=True).stdout
    return ImageFont.truetype(out, size)

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def tile():
    # Apple's icon grid: 824/1024 content tile, corner radius ~185/1024.
    size, margin, radius = S, S * 100 // 1024, S * 185 // 1024
    top, bottom = (138, 120, 255), (72, 52, 212)
    grad = Image.new("RGB", (size, size))
    gd = ImageDraw.Draw(grad)
    for y in range(size):
        gd.line([(0, y), (size, y)], fill=lerp(top, bottom, y / size))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([margin, margin, size - margin, size - margin], radius, fill=255)

    icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([margin, margin + 24, size - margin, size - margin + 24], radius, fill=(30, 20, 80, 110))
    icon.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(28)))
    icon.paste(grad, (0, 0), mask)
    return icon

def ring(icon):
    d = ImageDraw.Draw(icon)
    c = S / 2
    outer, inner = S * 0.335, S * 0.215
    slices, gap = 8, 5.0
    for i in range(slices):
        start = -90 + i * 360 / slices + gap / 2
        end = -90 + (i + 1) * 360 / slices - gap / 2
        colour = (255, 158, 112, 255) if i == 1 else (255, 255, 255, 235)   # one highlighted slice, like the app
        pts = [(c + outer * math.cos(math.radians(a)), c + outer * math.sin(math.radians(a)))
               for a in [start + (end - start) * k / 40 for k in range(41)]]
        pts += [(c + inner * math.cos(math.radians(a)), c + inner * math.sin(math.radians(a)))
                for a in [end - (end - start) * k / 40 for k in range(41)]]
        d.polygon(pts, fill=colour)

def label(icon):
    d = ImageDraw.Draw(icon)
    f = font(int(S * 0.165))
    text = "바꿔"
    l, t, r, b = d.textbbox((0, 0), text, font=f)
    x, y = (S - (r - l)) / 2 - l, (S - (b - t)) / 2 - t
    d.text((x + 6, y + 10), text, font=f, fill=(40, 25, 120, 120))   # soft drop shadow
    d.text((x, y), text, font=f, fill=(255, 255, 255, 255))

def main():
    os.makedirs(OUT, exist_ok=True)
    icon = tile(); ring(icon); label(icon)
    big = icon.resize((1024, 1024), Image.LANCZOS)
    big.save(os.path.join(OUT, "icon-1024.png"))
    big.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "icon-256.png"))
    big.resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, "icon-512.png"))
    big.save(os.path.join(OUT, "Bakkwo.icns"), sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)])
    big.save(os.path.join(OUT, "Bakkwo.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("icons written to", OUT)

if __name__ == "__main__":
    main()
