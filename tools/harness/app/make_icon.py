"""Generates the NotchHub application icon (multi-size .ico, 16-256 px) plus a PNG preview.

Design: a warm gold squircle (Material You "tonal" gradient) with the black dynamic notch hanging from its top edge
(inverse "shoulder" fillets + rounded bottom corners, like the app itself). From 48 px up the notch carries a tiny
album-art dot and an equalizer, the live activity of the collapsed notch.

Usage: python -I tools/harness/app/make_icon.py [out.ico] [preview.png]
Defaults: src/NotchHub.App/Assets/app.ico and tools/harness/app/app-icon-preview.png
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT_ICO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "src", "NotchHub.App", "Assets", "app.ico")
OUT_PREVIEW = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "app-icon-preview.png")

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
SS = 8  # supersampling factor

TOP = (246, 222, 156)      # light gold
BOTTOM = (214, 150, 66)    # amber
NOTCH = (10, 10, 12)
ACCENT = (246, 214, 128)
ART_A = (150, 98, 196)
ART_B = (222, 120, 160)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def squircle(cx, cy, rx, ry, n=5.0, steps=720):
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        c, s = math.cos(t), math.sin(t)
        x = cx + rx * math.copysign(abs(c) ** (2 / n), c)
        y = cy + ry * math.copysign(abs(s) ** (2 / n), s)
        pts.append((x, y))
    return pts


def arc(cx, cy, r, a0, a1, steps=48):
    """Points on a circle from angle a0 to a1 (degrees, screen coordinates: 0 = right, 90 = down)."""
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / steps)),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / steps))) for i in range(steps + 1)]


def notch_polygon(left, right, top, height, rb, rs):
    """Body hanging from y=top with bottom radius rb and inverse top shoulders rs (same shape as NotchShape.cs)."""
    rs = min(rs, height / 2)
    rb = min(rb, height - rs, (right - left) / 2)
    bottom = top + height
    pts = [(left - rs, top)]
    pts += arc(left - rs, top + rs, rs, -90, 0)               # left shoulder (concave)
    pts += arc(left + rb, bottom - rb, rb, 180, 90)           # bottom-left corner
    pts += arc(right - rb, bottom - rb, rb, 90, 0)            # bottom-right corner
    pts += arc(right + rs, top + rs, rs, 180, 270)            # right shoulder (concave)
    pts.append((right + rs, top))
    return pts


def render(size):
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Padding similar to Windows 11 app icons; tiny sizes use the full canvas for legibility.
    pad = s * (0.02 if size <= 24 else 0.06)
    cx = cy = s / 2
    r = s / 2 - pad

    # Gradient fill clipped by the squircle mask.
    grad = Image.new("RGBA", (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        t = min(1.0, max(0.0, (y - pad) / (2 * r)))
        gd.line([(0, y), (s, y)], fill=lerp(TOP, BOTTOM, t) + (255,))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).polygon(squircle(cx, cy, r, r), fill=255)
    img.paste(grad, (0, 0), mask)

    # Soft top highlight.
    hl = Image.new("L", (s, s), 0)
    ImageDraw.Draw(hl).ellipse([cx - r * 1.4, cy - r * 2.1, cx + r * 1.4, cy + r * 0.05], fill=40)
    hl = hl.filter(ImageFilter.GaussianBlur(s * 0.06))
    hl = Image.composite(hl, Image.new("L", (s, s), 0), mask)
    img.paste(Image.new("RGBA", (s, s), (255, 255, 255, 255)), (0, 0), hl)

    # The notch.
    small = size <= 32
    width = 2 * r * (0.66 if small else 0.58)
    height = 2 * r * (0.30 if small else 0.25)
    rb = height * 0.5
    rs = height * 0.32
    top = cy - r - 1  # flush with the squircle's flat top edge
    left, right = cx - width / 2, cx + width / 2
    notch = Image.new("L", (s, s), 0)
    ImageDraw.Draw(notch).polygon(notch_polygon(left, right, top, height, rb, rs), fill=255)
    notch = Image.composite(notch, Image.new("L", (s, s), 0), mask)
    img.paste(Image.new("RGBA", (s, s), NOTCH + (255,)), (0, 0), notch)

    if size >= 48:
        d = ImageDraw.Draw(img)
        mid = top + height * 0.52
        # album art dot (rounded square with a purple gradient)
        art = height * 0.50
        ax = left + height * 0.42
        art_img = Image.new("RGBA", (int(art), int(art)))
        ad = ImageDraw.Draw(art_img)
        for i in range(int(art)):
            ad.line([(0, i), (int(art), i)], fill=lerp(ART_A, ART_B, i / max(1, art - 1)) + (255,))
        art_mask = Image.new("L", (int(art), int(art)), 0)
        ImageDraw.Draw(art_mask).rounded_rectangle([0, 0, int(art) - 1, int(art) - 1], radius=art * 0.28, fill=255)
        img.paste(art_img, (int(ax), int(mid - art / 2)), art_mask)
        # equalizer: four rounded bars
        bar_w = height * 0.085
        gap = height * 0.075
        heights = [0.34, 0.56, 0.42, 0.24]
        x = right - height * 0.42 - len(heights) * bar_w - (len(heights) - 1) * gap
        for h in heights:
            bh = height * h
            d.rounded_rectangle([x, mid - bh / 2, x + bar_w, mid + bh / 2], radius=bar_w / 2, fill=ACCENT + (255,))
            x += bar_w + gap

    return img.resize((size, size), Image.LANCZOS)


def main():
    frames = [render(sz) for sz in SIZES]
    os.makedirs(os.path.dirname(OUT_ICO), exist_ok=True)
    largest = frames[-1]
    largest.save(OUT_ICO, format="ICO", sizes=[(f.width, f.height) for f in frames], append_images=frames[:-1])
    print("wrote", OUT_ICO)

    # Preview strip: every size on a dark and on a light background.
    width = sum(SIZES) + 12 * (len(SIZES) + 1)
    preview = Image.new("RGBA", (width, 2 * (256 + 24)), (32, 32, 36, 255))
    ImageDraw.Draw(preview).rectangle([0, 256 + 24, width, 2 * (256 + 24)], fill=(236, 236, 240, 255))
    x = 12
    for f in frames:
        preview.paste(f, (x, 12), f)
        preview.paste(f, (x, 256 + 24 + 12), f)
        x += f.width + 12
    preview.save(OUT_PREVIEW)
    print("wrote", OUT_PREVIEW)


if __name__ == "__main__":
    main()
