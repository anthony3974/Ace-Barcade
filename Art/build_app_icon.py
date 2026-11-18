"""Renders the Android app icon from Art/AppIcon/ into Assets/AppIcon/.

The icon is two layers, as Android's adaptive icons want: background.svg (full bleed, no outlines)
and foreground.svg (the coin). Both are 216 x 216 px, which is the 108 dp adaptive canvas at 2 px
per dp. The launcher crops it to the middle 72 dp (144 px) in its own shape, and only the middle
66 dp circle (radius 66 px) is safe from every mask, so keep the foreground inside that.

Outputs (ProjectSetup.ApplyIcons hands them to Unity):
  background.png, foreground.png   432 x 432, the adaptive layers (Android 8+)
  round.png, legacy.png            192 x 192, both layers flattened into a circle and a rounded
                                   square, for Android 7 launchers

Usage: python3 Art/build_app_icon.py <project root> [--force]
"""
import os
import re
import subprocess
import sys

ROOT = sys.argv[1]
FORCE = "--force" in sys.argv[2:]
SRC = os.path.join(ROOT, "Art", "AppIcon")
DST = os.path.join(ROOT, "Assets", "AppIcon")
LAYERS = ("background", "foreground")
FLAT = {"round": '<circle cx="108" cy="108" r="72"/>',
        "legacy": '<rect x="36" y="36" width="144" height="144" rx="32"/>'}


def svg_path(name):
    return os.path.join(SRC, name + ".svg")


def png_path(name):
    return os.path.join(DST, name + ".png")


def inner(name):
    """The drawing inside a layer's <svg> element, to paste both layers into one file."""
    text = open(svg_path(name)).read()
    return re.sub(r"^.*?<svg[^>]*>|</svg>\s*$", "", text, flags=re.S)


def render(svg_text, out, zoom):
    subprocess.run(["rsvg-convert", f"--zoom={zoom}", "--format=png", f"--output={out}", "-"],
                   input=svg_text.encode(), check=True)
    print(f"  AppIcon -> {os.path.relpath(out, ROOT)}")


def main():
    if not os.path.isdir(SRC):
        return
    newest = max(os.path.getmtime(svg_path(n)) for n in LAYERS)
    outs = [png_path(n) for n in (*LAYERS, *FLAT)]
    if not FORCE and all(os.path.exists(o) and os.path.getmtime(o) >= newest for o in outs):
        return
    os.makedirs(DST, exist_ok=True)

    for name in LAYERS:
        render(open(svg_path(name)).read(), png_path(name), 2)

    both = inner("background") + inner("foreground")
    for name, shape in FLAT.items():
        flat = ('<svg xmlns="http://www.w3.org/2000/svg" width="144" height="144" viewBox="36 36 144 144">'
                f'<defs><clipPath id="launcher">{shape}</clipPath></defs>'
                f'<g clip-path="url(#launcher)">{both}</g></svg>')
        render(flat, png_path(name), 192 / 144)


main()
