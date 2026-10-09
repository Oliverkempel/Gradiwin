#!/usr/bin/env python3
"""Converts Gradia's symbolic SVG icons into a JSON file of XAML path layers.

Usage: python scripts/svg2icons.py <gradia>/data/icons/scalable/actions src/Gradiwin/Assets/Icons/icons.json

Each icon becomes {"size": [w, h], "layers": [{"d", "stroke", "width", "opacity", "matrix", "evenodd"}]}.
Layers are drawn with the control's Foreground so icons follow the Windows theme, like GNOME symbolic icons.
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

SVG = "{http://www.w3.org/2000/svg}"


def parse_transform(t):
    m = [1, 0, 0, 1, 0, 0]
    if not t:
        return m
    for name, args in re.findall(r"(\w+)\s*\(([^)]*)\)", t):
        v = [float(x) for x in re.split(r"[ ,]+", args.strip()) if x]
        if name == "matrix":
            n = v
        elif name == "translate":
            n = [1, 0, 0, 1, v[0], v[1] if len(v) > 1 else 0]
        elif name == "scale":
            n = [v[0], 0, 0, v[1] if len(v) > 1 else v[0], 0, 0]
        else:
            continue
        m = mul(m, n)
    return m


def mul(a, b):
    return [
        a[0] * b[0] + a[2] * b[1],
        a[1] * b[0] + a[3] * b[1],
        a[0] * b[2] + a[2] * b[3],
        a[1] * b[2] + a[3] * b[3],
        a[0] * b[4] + a[2] * b[5] + a[4],
        a[1] * b[4] + a[3] * b[5] + a[5],
    ]


def style_dict(el):
    d = {}
    for part in (el.get("style") or "").split(";"):
        if ":" in part:
            k, v = part.split(":", 1)
            d[k.strip()] = v.strip()
    for k in ("fill", "stroke", "stroke-width", "opacity", "fill-opacity", "stroke-opacity", "fill-rule"):
        if el.get(k) is not None:
            d[k] = el.get(k)
    return d


PARAMS = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}


def absolutize(d):
    """Rewrites an SVG path with absolute commands only.

    XAML's path mini-language resolves relative commands after 'Z' against the last point
    instead of the subpath start (SVG semantics), which breaks most GNOME symbolic icons.
    """
    tokens = re.findall(r"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", d)
    out = []
    i = 0
    cx = cy = sx = sy = 0.0
    cmd = None
    fmt = lambda v: f"{v:.6g}"
    while i < len(tokens):
        t = tokens[i]
        if re.match(r"[A-Za-z]", t):
            cmd = t
            i += 1
            if cmd in "Zz":
                out.append("Z")
                cx, cy = sx, sy
                continue
        elif cmd is None:
            i += 1
            continue
        up = cmd.upper()
        rel = cmd.islower()
        n = PARAMS[up]
        args = [float(x) for x in tokens[i:i + n]]
        if len(args) < n:
            break
        i += n
        if up == "M":
            x, y = args
            if rel:
                x, y = cx + x, cy + y
            out.append(f"M {fmt(x)},{fmt(y)}")
            cx, cy = sx, sy = x, y
            cmd = "l" if rel else "L"  # implicit lineto for following pairs
        elif up == "L" or up == "T":
            x, y = args
            if rel:
                x, y = cx + x, cy + y
            out.append(f"{up} {fmt(x)},{fmt(y)}")
            cx, cy = x, y
        elif up == "H":
            x = args[0] + (cx if rel else 0)
            out.append(f"H {fmt(x)}")
            cx = x
        elif up == "V":
            y = args[0] + (cy if rel else 0)
            out.append(f"V {fmt(y)}")
            cy = y
        elif up in "CSQ":
            pts = []
            for k in range(0, n, 2):
                x, y = args[k], args[k + 1]
                if rel:
                    x, y = cx + x, cy + y
                pts.append(f"{fmt(x)},{fmt(y)}")
            out.append(f"{up} " + " ".join(pts))
            cx, cy = (args[-2] + cx, args[-1] + cy) if rel else (args[-2], args[-1])
        elif up == "A":
            rx, ry, rot, large, sweep, x, y = args
            if rel:
                x, y = cx + x, cy + y
            out.append(f"A {fmt(rx)},{fmt(ry)} {fmt(rot)} {int(large)} {int(sweep)} {fmt(x)},{fmt(y)}")
            cx, cy = x, y
    return " ".join(out)


def ellipse_d(cx, cy, rx, ry):
    return (f"M {cx - rx},{cy} A {rx},{ry} 0 1 0 {cx + rx},{cy} "
            f"A {rx},{ry} 0 1 0 {cx - rx},{cy} Z")


def rect_d(x, y, w, h, rx=0):
    if rx <= 0:
        return f"M {x},{y} H {x + w} V {y + h} H {x} Z"
    r = min(rx, w / 2, h / 2)
    return (f"M {x + r},{y} H {x + w - r} A {r},{r} 0 0 1 {x + w},{y + r} V {y + h - r} "
            f"A {r},{r} 0 0 1 {x + w - r},{y + h} H {x + r} A {r},{r} 0 0 1 {x},{y + h - r} "
            f"V {y + r} A {r},{r} 0 0 1 {x + r},{y} Z")


def shape_d(el):
    tag = el.tag.replace(SVG, "")
    f = lambda k, default=0.0: float(el.get(k, default))
    if tag == "path":
        return absolutize(el.get("d") or "")
    if tag == "circle":
        return ellipse_d(f("cx"), f("cy"), f("r"), f("r"))
    if tag == "ellipse":
        return ellipse_d(f("cx"), f("cy"), f("rx"), f("ry"))
    if tag == "rect":
        return rect_d(f("x"), f("y"), f("width"), f("height"), f("rx"))
    return None


def walk(el, matrix, inherited, layers, masks):
    tag = el.tag.replace(SVG, "")
    if tag in ("defs", "mask", "clipPath", "namedview") or "sodipodi" in el.tag:
        return
    style = dict(inherited)
    style.update(style_dict(el))
    m = mul(matrix, parse_transform(el.get("transform")))

    d = shape_d(el)
    if d:
        evenodd = style.get("fill-rule") == "evenodd"
        mask_ref = el.get("mask")
        if mask_ref:
            mid = re.search(r"#([^)]+)", mask_ref).group(1)
            for cut in masks.get(mid, []):
                d += " " + cut
            evenodd = True
        stroke = style.get("stroke", "none") not in ("none", "")
        fill = style.get("fill", "#000") not in ("none",)
        opacity = float(style.get("opacity", 1))
        if stroke:
            layers.append({
                "d": d, "stroke": True, "width": float(style.get("stroke-width", 1)),
                "opacity": opacity * float(style.get("stroke-opacity", 1)), "matrix": m, "evenodd": False,
            })
        if fill:
            layers.append({
                "d": d, "stroke": False, "width": 0,
                "opacity": opacity * float(style.get("fill-opacity", 1)), "matrix": m, "evenodd": evenodd,
            })
    for child in el:
        walk(child, m, style, layers, masks)


def convert(path):
    root = ET.parse(path).getroot()
    vb = [float(x) for x in re.split(r"[ ,]+", (root.get("viewBox") or "0 0 16 16").strip())]
    masks = {}
    for mask in root.iter(SVG + "mask"):
        cuts = []
        for el in mask:
            fill = style_dict(el).get("fill", "")
            if fill == "black" or fill == "#000000":
                d = shape_d(el)
                if d:
                    cuts.append(d)
        masks[mask.get("id")] = cuts
    layers = []
    walk(root, [1, 0, 0, 1, -vb[0], -vb[1]], {}, layers, masks)
    return {"size": [vb[2], vb[3]], "layers": layers}


def main():
    src, out = sys.argv[1], sys.argv[2]
    icons = {}
    for name in sorted(os.listdir(src)):
        if name.endswith("-symbolic.svg"):
            icons[name[: -len("-symbolic.svg")]] = convert(os.path.join(src, name))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(icons, fh, separators=(",", ":"))
    print(f"wrote {len(icons)} icons to {out}")


if __name__ == "__main__":
    main()
