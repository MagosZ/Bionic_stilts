"""Draws the mod's textures (needs Pillow). Run from anywhere:

    py -3 Source/Art/make_textures.py

Writes, relative to the mod folder:
- Textures/StiltsBionics/Things/BionicStilts.png, 128 px: the item, a pair of retracted stilts lying side by side.
- Textures/StiltsBionics/Things/StiltTube.png, 32 x 128 px: one telescopic tube, upright. Drawn on screen stretched to
  each segment of a pole (code: Source/Core/Drawing.cs), so it has a collar at the top and plain shading below.
- Textures/StiltsBionics/Things/StiltFoot.png, 64 x 32 px: the rubber foot pad at the bottom of a pole.
- Textures/StiltsBionics/UI/{Extend,Retract}.png, 128 px gizmo icons: a figure on poles with an up or down arrow.

The poles are seen the way RimWorld draws pawns (from the front, height going up the screen), so a tube is a
cylinder standing upright: shaded across its width, lightest left of centre (light from the north-west).
Hand-drawn replacements can be dropped in with the same names and sizes; nothing else needs to change.
"""
import os
from PIL import Image, ImageDraw

MOD_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
THINGS = os.path.join(MOD_ROOT, "Textures", "StiltsBionics", "Things")
UI = os.path.join(MOD_ROOT, "Textures", "StiltsBionics", "UI")

STEEL_LIGHT = (214, 222, 230)
STEEL = (158, 168, 180)
STEEL_DARK = (84, 92, 104)
STEEL_EDGE = (52, 58, 66)
BIONIC = (189, 169, 118)       # vanilla's bionic tint
BIONIC_LIGHT = (226, 208, 160)
BIONIC_DARK = (128, 110, 70)
LINK = (96, 170, 220)          # nerve-link glow
RUBBER = (40, 40, 44)
RUBBER_LIGHT = (78, 78, 84)
ARROW = (250, 206, 70)
ARROW_EDGE = (120, 90, 20)
FIGURE = (236, 236, 236)
FIGURE_EDGE = (40, 40, 40)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def cylinder_color(u, light, mid, dark):
    """Colour across an upright cylinder, u = 0 (left edge) .. 1 (right edge); light from the upper left."""
    peak = 0.32
    if u < peak:
        return lerp(mid, light, u / peak)
    return lerp(light, dark, (u - peak) / (1 - peak))


def tube(width, height, collar):
    im = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    px = im.load()
    edge = 2
    for x in range(width):
        u = (x + 0.5) / width
        for y in range(height):
            if y < collar:
                c = cylinder_color(u, BIONIC_LIGHT, BIONIC, BIONIC_DARK)
                if y in (0, collar - 1):
                    c = lerp(c, BIONIC_DARK, 0.6)
            else:
                c = cylinder_color(u, STEEL_LIGHT, STEEL, STEEL_DARK)
            if x < edge or x >= width - edge:
                c = lerp(c, STEEL_EDGE, 0.7)
            px[x, y] = c + (255,)
    return im


def foot(width, height):
    im = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse([1, 4, width - 2, height - 2], fill=RUBBER + (255,), outline=(20, 20, 22, 255), width=2)
    d.ellipse([10, 7, width // 2, 13], fill=RUBBER_LIGHT + (255,))
    return im


def stilt_item_leg(d, x0, y0, x1, y1, w):
    """One retracted stilt from (x0, y0) (hip) to (x1, y1) (foot), drawn as nested tubes along that line."""
    import math
    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy)
    ux, uy = dx / length, dy / length
    nx, ny = -uy, ux

    def quad(t0, t1, half, fill, outline):
        a = (x0 + ux * t0 * length, y0 + uy * t0 * length)
        b = (x0 + ux * t1 * length, y0 + uy * t1 * length)
        pts = [(a[0] + nx * half, a[1] + ny * half), (b[0] + nx * half, b[1] + ny * half),
               (b[0] - nx * half, b[1] - ny * half), (a[0] - nx * half, a[1] - ny * half)]
        d.polygon(pts, fill=fill + (255,), outline=outline + (255,))
        # Highlight stripe on the lit side.
        hl = [(a[0] + nx * half * 0.55, a[1] + ny * half * 0.55), (b[0] + nx * half * 0.55, b[1] + ny * half * 0.55),
              (b[0] + nx * half * 0.2, b[1] + ny * half * 0.2), (a[0] + nx * half * 0.2, a[1] + ny * half * 0.2)]
        d.polygon(hl, fill=lerp(fill, (255, 255, 255), 0.35) + (255,))

    # Inner tubes peeking out below the outer one, then the outer tube, then the hip socket and the foot.
    quad(0.62, 0.92, w * 0.32, STEEL, STEEL_EDGE)
    quad(0.48, 0.70, w * 0.40, STEEL, STEEL_EDGE)
    quad(0.12, 0.52, w * 0.50, STEEL, STEEL_EDGE)
    quad(0.48, 0.53, w * 0.54, BIONIC, BIONIC_DARK)
    quad(0.66, 0.70, w * 0.44, BIONIC, BIONIC_DARK)
    hip = (x0 + ux * 0.1 * length, y0 + uy * 0.1 * length)
    r = w * 0.62
    d.ellipse([hip[0] - r, hip[1] - r, hip[0] + r, hip[1] + r], fill=BIONIC + (255,), outline=BIONIC_DARK + (255,), width=2)
    d.ellipse([hip[0] - r * 0.35, hip[1] - r * 0.35, hip[0] + r * 0.35, hip[1] + r * 0.35], fill=LINK + (255,))
    fx, fy = x0 + ux * 0.95 * length, y0 + uy * 0.95 * length
    d.ellipse([fx - w * 0.6, fy - w * 0.3, fx + w * 0.6, fy + w * 0.3], fill=RUBBER + (255,), outline=(20, 20, 22, 255))


def item():
    size = 128
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    for (x0, y0, x1, y1) in ((40, 18, 62, 114), (66, 14, 92, 110)):
        sd.line([(x0 + 4, y0 + 5), (x1 + 4, y1 + 5)], fill=(0, 0, 0, 70), width=20)
    im = Image.alpha_composite(im, shadow)
    d = ImageDraw.Draw(im)
    stilt_item_leg(d, 40, 18, 62, 114, 22)
    stilt_item_leg(d, 66, 14, 92, 110, 22)
    return im


def icon(up):
    size = 128
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    # Figure: head and torso high up, two long poles to the ground.
    hx, top = 52, 14 if up else 44
    pole_bottom = 118
    hip = top + 46
    for x in (hx - 9, hx + 9):
        d.rectangle([x - 4, hip, x + 4, pole_bottom], fill=STEEL + (255,), outline=STEEL_EDGE + (255,), width=2)
        d.rectangle([x - 5, hip + 4, x + 5, hip + 9], fill=BIONIC + (255,), outline=BIONIC_DARK + (255,))
        d.ellipse([x - 9, pole_bottom - 5, x + 9, pole_bottom + 3], fill=RUBBER + (255,))
    d.rounded_rectangle([hx - 17, top + 18, hx + 17, hip + 2], radius=7, fill=FIGURE + (255,), outline=FIGURE_EDGE + (255,), width=3)
    d.ellipse([hx - 12, top - 4, hx + 12, top + 20], fill=FIGURE + (255,), outline=FIGURE_EDGE + (255,), width=3)
    # Arrow on the right.
    ax = 100
    if up:
        pts = [(ax, 14), (ax + 20, 44), (ax + 8, 44), (ax + 8, 100), (ax - 8, 100), (ax - 8, 44), (ax - 20, 44)]
    else:
        pts = [(ax, 114), (ax + 20, 84), (ax + 8, 84), (ax + 8, 28), (ax - 8, 28), (ax - 8, 84), (ax - 20, 84)]
    d.polygon(pts, fill=ARROW + (255,), outline=ARROW_EDGE + (255,))
    return im


def main():
    os.makedirs(THINGS, exist_ok=True)
    os.makedirs(UI, exist_ok=True)
    item().save(os.path.join(THINGS, "BionicStilts.png"))
    tube(32, 128, 10).save(os.path.join(THINGS, "StiltTube.png"))
    foot(64, 32).save(os.path.join(THINGS, "StiltFoot.png"))
    icon(True).save(os.path.join(UI, "Extend.png"))
    icon(False).save(os.path.join(UI, "Retract.png"))
    print("textures written to", os.path.join(MOD_ROOT, "Textures", "StiltsBionics"))


if __name__ == "__main__":
    main()
