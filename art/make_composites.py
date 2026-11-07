#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""MWP - contact sheets for the offline previews (optional tooling).

Composites the rendered PNGs into the contact sheets used for human review:

  art/previews/icons_contact_4x2.png        8 icons (128 px -> 256 px cells)
  art/previews/tiers_factions_iso_grid.png  4 tiers x 2 factions iso previews
  art/previews/animation_phases_grid.png    4 tiers (Folktails) x 3 animation phases

Requires Pillow (the CPython environment used for the art validators has it).
The individual renders are produced by ``art/render_previews.py`` (Blender);
this script only stitches existing PNGs, so it is safe to re-run.

Run:  python art/make_composites.py
"""

import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PREVIEWS = os.path.join(HERE, "previews")
ICONS = os.path.join(HERE, "icons")

TIERS = ["Basic", "Intermediate", "Advanced", "Top"]
FACTIONS = ["Folktails", "IronTeeth"]
ANIMATION_FRAMES = (1, 41, 81)
CELL = 256


def cell(path, size=CELL):
    return Image.open(path).convert("RGB").resize((size, size), Image.LANCZOS)


def icons_contact():
    background = Image.new("RGBA", (4 * CELL, 2 * CELL), (40, 44, 50, 255))
    order = [(tier, faction) for faction in FACTIONS for tier in TIERS]
    for index, (tier, faction) in enumerate(order):
        stem = "MWP%s%s" % (tier, faction)
        path = os.path.join(ICONS, stem + ".png")
        if not os.path.isfile(path):
            raise FileNotFoundError(path)
        image = Image.open(path).convert("RGBA").resize((CELL, CELL), Image.LANCZOS)
        background.alpha_composite(image, ((index % 4) * CELL, (index // 4) * CELL))
    target = os.path.join(PREVIEWS, "icons_contact_4x2.png")
    background.convert("RGB").save(target)
    return target


def tiers_factions_grid():
    background = Image.new("RGB", (4 * CELL, 2 * CELL), (38, 41, 46))
    order = [(tier, faction) for faction in FACTIONS for tier in TIERS]
    for index, (tier, faction) in enumerate(order):
        path = os.path.join(PREVIEWS, "%s.%s_iso.png" % (tier, faction))
        if not os.path.isfile(path):
            raise FileNotFoundError(path)
        background.paste(cell(path), ((index % 4) * CELL, (index // 4) * CELL))
    target = os.path.join(PREVIEWS, "tiers_factions_iso_grid.png")
    background.save(target)
    return target


def animation_phases_grid():
    background = Image.new("RGB", (3 * CELL, 4 * CELL), (38, 41, 46))
    for row, tier in enumerate(TIERS):
        for column, frame in enumerate(ANIMATION_FRAMES):
            path = os.path.join(PREVIEWS, "%s.Folktails_anim_f%03d.png" % (tier, frame))
            if not os.path.isfile(path):
                raise FileNotFoundError(path)
            background.paste(cell(path), (column * CELL, row * CELL))
    target = os.path.join(PREVIEWS, "animation_phases_grid.png")
    background.save(target)
    return target


def t11_interior_grid():
    """Mk III / Mk IV interior pass: 4 variants x {left_front, right_front,
    front_window, iso50} day-light views."""
    variants = [("Advanced", "Folktails"), ("Advanced", "IronTeeth"),
                ("Top", "Folktails"), ("Top", "IronTeeth")]
    views = ["left_front", "right_front", "front_window", "iso50"]
    background = Image.new("RGB", (len(views) * CELL, len(variants) * CELL),
                           (38, 41, 46))
    for row, (tier, faction) in enumerate(variants):
        for column, view in enumerate(views):
            path = os.path.join(PREVIEWS, "%s.%s_%s.png" % (tier, faction, view))
            if not os.path.isfile(path):
                raise FileNotFoundError(path)
            background.paste(cell(path), (column * CELL, row * CELL))
    target = os.path.join(PREVIEWS, "t11_mk3_mk4_angles_grid.png")
    background.save(target)
    return target


def main():
    producers = [icons_contact, tiers_factions_grid, animation_phases_grid]
    if os.path.isfile(os.path.join(PREVIEWS, "Advanced.Folktails_left_front.png")):
        producers.append(t11_interior_grid)
    for producer in producers:
        path = producer()
        print("wrote %s" % os.path.relpath(path, os.path.dirname(HERE)))


if __name__ == "__main__":
    main()
