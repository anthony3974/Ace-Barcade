"""The art half of the house style, as code. See docs/STYLE.md for the why.

Rules, for every SVG under Art/ (files and folders starting with "_" are skipped: scratch work):
  - Colors only from Art/palette.txt (fill, stroke, stop-color, in attributes or style="").
  - No gradients, filters, masks, patterns, embedded images, <text> or <style> blocks.
  - No transparency: opacity, fill-opacity and stroke-opacity must be 1 (code does fades).
  - Strokes are 8 px (outline) or 5 px (detail), with round joins.
  - Canvas at most 1080 x 1920 (the full screen at 120 px per unit).
  - Each game folder Art/Games/G###_Name/ has an icon.svg of exactly 360 x 360.
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = sys.argv[1]
ART = os.path.join(ROOT, "Art")
TARGETS = sys.argv[2:] or [ART]

STROKES = (8.0, 5.0)
MAX_W, MAX_H = 1080, 1920
ICON = 360
BANNED = {"linearGradient", "radialGradient", "filter", "mask", "pattern", "image", "text", "style", "foreignObject"}
COLOR_PROPS = ("fill", "stroke", "stop-color", "color", "flood-color")
OPACITY_PROPS = ("opacity", "fill-opacity", "stroke-opacity")


def load_palette():
    hexes = set()
    with open(os.path.join(ART, "palette.txt")) as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#"):
                hexes.add(line.split()[1].upper())
    return hexes


PALETTE = load_palette()


def norm_hex(v):
    v = v.strip().upper()
    if re.fullmatch(r"#[0-9A-F]{3}", v):
        v = "#" + "".join(c * 2 for c in v[1:])
    return v


def props(el):
    p = {k: v for k, v in el.attrib.items()}
    for part in el.attrib.get("style", "").split(";"):
        if ":" in part:
            k, v = part.split(":", 1)
            p[k.strip()] = v.strip()
    return p


def px(v):
    m = re.match(r"\s*([0-9.]+)\s*(px)?\s*$", v or "")
    return float(m.group(1)) if m else None


def check_file(path):
    problems = []
    rel = os.path.relpath(path, ROOT)
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as e:
        return [f"{rel}: not valid XML ({e})"]

    w, h = px(root.get("width")), px(root.get("height"))
    if w is None or h is None:
        problems.append(f"{rel}: <svg> needs width and height in px")
    elif w > MAX_W or h > MAX_H:
        problems.append(f"{rel}: canvas {w:g}x{h:g} is bigger than {MAX_W}x{MAX_H}")
    if os.path.basename(path) == "icon.svg" and (w, h) != (ICON, ICON):
        problems.append(f"{rel}: icons are {ICON}x{ICON}")

    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag in BANNED:
            problems.append(f"{rel}: <{tag}> is not allowed")
            continue
        p = props(el)
        for k in COLOR_PROPS:
            v = p.get(k)
            if v is None or v in ("none", "currentColor", "inherit"):
                continue
            if norm_hex(v) not in PALETTE:
                problems.append(f"{rel}: <{tag}> {k}={v} is not a palette color")
        for k in OPACITY_PROPS:
            v = p.get(k)
            if v is not None and px(v) != 1.0:
                problems.append(f"{rel}: <{tag}> {k}={v}: no transparency in art")
        stroke = p.get("stroke")
        if stroke and stroke != "none":
            sw = px(p.get("stroke-width", "1"))
            if sw is None or not any(abs(sw - s) < 0.01 for s in STROKES):
                problems.append(f"{rel}: <{tag}> stroke-width {p.get('stroke-width', '1')} must be 8 or 5")
            if p.get("stroke-linejoin", "miter") != "round":
                problems.append(f"{rel}: <{tag}> strokes need stroke-linejoin=round")
    return problems


def svgs(target):
    if os.path.isfile(target):
        yield target
        return
    for d, dirs, files in os.walk(target):
        dirs[:] = [x for x in dirs if not x.startswith("_")]
        for f in files:
            if f.endswith(".svg") and not f.startswith("_"):
                yield os.path.join(d, f)


def game_folders():
    games = os.path.join(ART, "Games")
    if not os.path.isdir(games):
        return []
    return [os.path.join(games, d) for d in sorted(os.listdir(games)) if re.match(r"G\d{3}_", d)]


problems, count = [], 0
for t in TARGETS:
    for f in svgs(os.path.abspath(t)):
        count += 1
        problems += check_file(f)
for g in game_folders():
    if not os.path.isfile(os.path.join(g, "icon.svg")):
        problems.append(f"{os.path.relpath(g, ROOT)}: missing icon.svg")

for p in problems:
    print("STYLE", p)
print(f"Checked {count} SVG(s): {'OK' if not problems else f'{len(problems)} problem(s)'}")
sys.exit(1 if problems else 0)
