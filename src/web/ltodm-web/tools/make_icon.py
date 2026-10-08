"""Render the LT ODM 3D app icon (beveled blue tile, extruded 'LT', stitched-label border)."""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

FONT = "C:/Windows/Fonts/seguibl.ttf"  # Segoe UI Black


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def vgrad(w, h, top, bottom):
    g = Image.new("RGBA", (1, h))
    for y in range(h):
        g.putpixel((0, y), lerp(top, bottom, y / max(1, h - 1)))
    return g.resize((w, h))


def rr_mask(size, box, r):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle(box, r, fill=255)
    return m


def render(S, margin=0.07, shadow=True, stitch=True, bleed=False):
    """Render at 4x then downsample. bleed=True -> maskable (full-square background)."""
    K = 4
    N = S * K
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))

    if bleed:
        # full-bleed face; platform applies its own mask shape
        x0 = y0 = 0
        x1 = y1 = N
        depth = 0
        r = 1
    else:
        x0 = y0 = N * margin
        x1 = N * (1 - margin)
        depth = N * 0.045  # visible slab thickness
        y1 = N * (1 - margin) - depth
        r = (x1 - x0) * 0.23

    face = (x0, y0, x1, y1)
    side = (x0, y0 + depth, x1, y1 + depth)

    # Soft drop shadow beneath the slab
    if shadow:
        sh = Image.new("RGBA", (N, N), (0, 0, 0, 0))
        ImageDraw.Draw(sh).rounded_rectangle(
            (x0 + N * 0.01, y0 + depth + N * 0.025, x1 - N * 0.01, y1 + depth + N * 0.03),
            r, fill=(5, 20, 60, 110))
        sh = sh.filter(ImageFilter.GaussianBlur(N * 0.025))
        img = Image.alpha_composite(img, sh)

    # Slab side (thickness), darker blue gradient
    side_layer = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    side_layer.paste(vgrad(N, N, (8, 40, 110, 255), (4, 24, 70, 255)), (0, 0),
                     rr_mask((N, N), side, r))
    img = Image.alpha_composite(img, side_layer)

    # Face gradient
    fmask = rr_mask((N, N), face, r)
    face_layer = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    fh = int(y1 - y0)
    grad = vgrad(N, fh, (52, 128, 240, 255), (13, 71, 161, 255))
    full = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    full.paste(grad, (0, int(y0)))
    face_layer.paste(full, (0, 0), fmask)
    img = Image.alpha_composite(img, face_layer)

    # Radial light from top-left
    light = Image.new("L", (N, N), 0)
    ld = ImageDraw.Draw(light)
    cx, cy, rad = x0 + (x1 - x0) * 0.25, y0 + (y1 - y0) * 0.15, (x1 - x0) * 0.75
    ld.ellipse((cx - rad, cy - rad, cx + rad, cy + rad), fill=70)
    light = light.filter(ImageFilter.GaussianBlur(N * 0.12))
    light = ImageChops.multiply(light, fmask)
    img = Image.alpha_composite(img, Image.merge("RGBA", (*[Image.new("L", (N, N), 255)] * 3, light)))

    # Glossy top sheen (clipped to face, upper ~45%)
    gloss = Image.new("L", (N, N), 0)
    gd = ImageDraw.Draw(gloss)
    gw = (x1 - x0)
    gd.ellipse((x0 - gw * 0.3, y0 - (y1 - y0) * 0.75, x1 + gw * 0.3, y0 + (y1 - y0) * 0.42), fill=38)
    gloss = gloss.filter(ImageFilter.GaussianBlur(N * 0.006))
    gloss = ImageChops.multiply(gloss, fmask)
    img = Image.alpha_composite(img, Image.merge("RGBA", (*[Image.new("L", (N, N), 255)] * 3, gloss)))

    # Bevel: bright rim on top edge, dark rim on bottom edge
    bw = max(2, int(N * 0.012))
    inner = rr_mask((N, N), (x0 + bw, y0 + bw, x1 - bw, y1 - bw), max(1, r - bw))
    rim = ImageChops.subtract(fmask, inner)
    topfade = vgrad(1, N, (255,), (0,)).convert("L").resize((N, N))
    topfade = Image.eval(topfade, lambda v: int(max(0, (v - 110)) * 1.6))
    hi = ImageChops.multiply(rim, topfade)
    img = Image.alpha_composite(img, Image.merge("RGBA", (*[Image.new("L", (N, N), 255)] * 3,
                                                          Image.eval(hi, lambda v: int(v * 0.75)))))
    lo = ImageChops.multiply(rim, Image.eval(topfade, lambda v: 255 - min(255, v * 3)))
    img = Image.alpha_composite(img, Image.merge("RGBA", (Image.new("L", (N, N), 4), Image.new("L", (N, N), 20),
                                                          Image.new("L", (N, N), 60),
                                                          Image.eval(lo, lambda v: int(v * 0.5)))))

    # Stitched-label border (garment cue)
    if stitch:
        st = Image.new("RGBA", (N, N), (0, 0, 0, 0))
        sd = ImageDraw.Draw(st)
        ins = (x1 - x0) * (0.17 if bleed else 0.075)
        bx0, by0, bx1, by1 = x0 + ins, y0 + ins, x1 - ins, y1 - ins
        br = (x1 - x0) * 0.2 if bleed else max(1, r - ins)
        sw = max(1, int(N * 0.0075))
        dash, gap = N * 0.022, N * 0.014
        pts = []  # walk the rounded rect perimeter
        steps = 2000
        straight_w, straight_h = (bx1 - bx0 - 2 * br), (by1 - by0 - 2 * br)
        per = 2 * (straight_w + straight_h) + 2 * math.pi * br
        for i in range(steps + 1):
            d = per * i / steps
            pts.append((d, _rr_point(d, bx0, by0, bx1, by1, br)))
        pos = 0.0
        while pos < per:
            seg = [p for dd, p in pts if pos <= dd <= pos + dash]
            if len(seg) > 1:
                sd.line(seg, fill=(255, 255, 255, 95), width=sw, joint="curve")
            pos += dash + gap
        img = Image.alpha_composite(img, st)

    # Extruded "LT"
    fs = int((x1 - x0) * (0.42 if bleed else 0.56))
    font = ImageFont.truetype(FONT, fs)
    text = "LT"
    l, t, rgt, b = font.getbbox(text)
    tw, th = rgt - l, b - t
    tx = (x0 + x1) / 2 - tw / 2 - l - (x1 - x0) * 0.01
    ty = (y0 + y1) / 2 - th / 2 - t - (y1 - y0) * 0.01
    ext = (x1 - x0) * 0.055
    nsteps = max(4, int(ext / (K * 0.5)))
    tmask = Image.new("L", (N, N), 0)
    ImageDraw.Draw(tmask).text((tx, ty), text, font=font, fill=255)

    # cast shadow of letters onto face
    cs = Image.new("L", (N, N), 0)
    cs.paste(tmask, (int(ext * 1.3), int(ext * 1.8)))
    cs = cs.filter(ImageFilter.GaussianBlur(N * 0.015))
    cs = ImageChops.multiply(Image.eval(cs, lambda v: int(v * 0.55)), fmask)
    img = Image.alpha_composite(img, Image.merge("RGBA", (Image.new("L", (N, N), 3), Image.new("L", (N, N), 18),
                                                          Image.new("L", (N, N), 55), cs)))

    # extrusion body
    for i in range(nsteps, 0, -1):
        f = i / nsteps
        off = Image.new("L", (N, N), 0)
        off.paste(tmask, (int(ext * f * 0.55), int(ext * f)))
        col = lerp((150, 180, 230), (90, 120, 185), f)
        layer = Image.new("RGBA", (N, N), col + (0,))
        layer.putalpha(off)
        img = Image.alpha_composite(img, layer)

    # letter face: white -> soft ice blue
    lf = vgrad(N, N, (255, 255, 255, 255), (214, 228, 252, 255)).copy()
    tb = (int(ty + t), int(ty + b))
    lf = Image.new("RGBA", (N, N), (255, 255, 255, 255))
    lf.paste(vgrad(N, tb[1] - tb[0], (255, 255, 255, 255), (212, 226, 250, 255)), (0, tb[0]))
    lf.putalpha(tmask)
    img = Image.alpha_composite(img, lf)

    return img.resize((S, S), Image.LANCZOS)


def _rr_point(d, x0, y0, x1, y1, r):
    w, h = x1 - x0 - 2 * r, y1 - y0 - 2 * r
    arc = math.pi * r / 2
    segs = [
        ("line", (x0 + r, y0), (1, 0), w),
        ("arc", (x1 - r, y0 + r), -math.pi / 2, arc),
        ("line", (x1, y0 + r), (0, 1), h),
        ("arc", (x1 - r, y1 - r), 0, arc),
        ("line", (x1 - r, y1), (-1, 0), w),
        ("arc", (x0 + r, y1 - r), math.pi / 2, arc),
        ("line", (x0, y1 - r), (0, -1), h),
        ("arc", (x0 + r, y0 + r), math.pi, arc),
    ]
    for kind, a, b, ln in segs:
        if d <= ln:
            if kind == "line":
                return (a[0] + b[0] * d, a[1] + b[1] * d)
            ang = b + d / r
            return (a[0] + r * math.cos(ang), a[1] + r * math.sin(ang))
        d -= ln
    return (x0 + r, y0)


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(os.path.join(out, "icons"), exist_ok=True)
    for s in (72, 96, 128, 144, 152, 192, 384, 512):
        render(s).save(os.path.join(out, "icons", f"icon-{s}x{s}.png"), optimize=True)
    for s in (192, 512):
        render(s, bleed=True, shadow=False).save(os.path.join(out, "icons", f"icon-maskable-{s}x{s}.png"), optimize=True)
    # Apple touch: iOS rounds corners itself, so use full-bleed
    render(180, bleed=True, shadow=False).save(os.path.join(out, "icons", "apple-touch-icon.png"), optimize=True)
    # Favicon: tight margins, no drop shadow / stitching so it stays crisp at 16-32px
    big = render(256, margin=0.02, shadow=False, stitch=True)
    small = {s: render(s, margin=0.0, shadow=False, stitch=False) for s in (16, 24, 32, 48)}
    big.save(os.path.join(out, "favicon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
             append_images=list(small.values()))
    render(1024).save(os.path.join(out, "preview-1024.png"))
    print("ok")
