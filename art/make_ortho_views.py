"""ART-07 / T13 - orthographic three-view annotation + sign-off package.

Takes the 24 transparent orthographic base renders from
``art/render_ortho_views.py`` and produces the ART-07 deliverable:

  * ``art/previews/ortho/<Tier>.<Faction>_<front|side|top>.png``  24 annotated views
     (1 m grid, DV-03 axis labels, envelope boundary + dimensions, port markers
      with (x,y,z) and view visibility, model title + legend)
  * ``art/previews/art07/<Tier>.<Faction>_three_views.png``       8 three-view sheets
     (front / side / top side by side + a bottom spec table)
  * ``art/previews/art07/art07_overview_grid.png``                4 tiers x 3 views (FT)
  * ``art/reports/art07/three-view-signoff.md``                   per-file consistency
     tables (export bounds, TierDefinition.cs SizeX/SizeY/SizeZ, blueprint Size /
     BaseZ / Entrance / LocalAccess, the 3 #Port# coordinates from the shipped
     .timbermesh files) + PASS/FAIL + user sign-off column
  * ``art/render-manifest.json``                                  ART-07 section

Only Pillow is needed (no Blender); the base renders are deterministic Cycles
orthographic images.  This script exists because render_ortho_views.py is
Blender-side and this annotation step is plain Python.

Run (after art/render_ortho_views.py):
    python art/make_ortho_views.py
"""

import hashlib
import json
import os
import re
import sys
import zlib

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
PLUGIN_DIR = os.path.join(HERE, "tools", "timbermesh_blender_plugin")
if PLUGIN_DIR not in sys.path:
    sys.path.insert(0, PLUGIN_DIR)

import mwp_common as C  # noqa: E402
import model_pb2  # noqa: E402

WORKSPACE = C.WORKSPACE
ORTHO_DIR = os.path.join(C.PREVIEWS_DIR, "ortho")
ART07_DIR = os.path.join(C.PREVIEWS_DIR, "art07")
REPORT_DIR = os.path.join(WORKSPACE, "art", "reports", "art07")
LAYOUT_PATH = os.path.join(ORTHO_DIR, "ortho-layout.json")
MANIFEST_PATH = os.path.join(HERE, "render-manifest.json")
TIER_DEFINITION_PATH = os.path.join(WORKSPACE, "mod", "src", "MWP.Core",
                                    "Balance", "TierDefinition.cs")
BLUEPRINT_DIR = os.path.join(WORKSPACE, "mod", "data", "Buildings", "Water", "MWP")

BASE_SIZE = 640
BASE_X = 98
BASE_TOP = 118
CANVAS_W = BASE_X * 2 + BASE_SIZE          # 836
CANVAS_H = BASE_TOP + BASE_SIZE + 180      # 938

PAPER = (246, 243, 235)
GRID = (196, 190, 176)
GRID_MAJOR = (150, 143, 128)
BOUNDARY = (52, 56, 62)
INK = (38, 42, 48)
MUTED = (104, 108, 116)
PORT_COLORS = {
    "#Port#Power": (192, 57, 43),
    "#Port#Pipe": (41, 128, 185),
    "#Port#Logistics": (39, 174, 96),
}
VIEW_TITLES = {"front": "FRONT", "side": "SIDE", "top": "TOP"}
VIEW_HINTS = {
    "front": "camera at -Z, looking +Z  (face normal -Z, front face at Z = 0)",
    "side": "camera at +X, looking -X  (right side)",
    "top": "camera above, looking -Y  (screen up = +Z depth)",
}


# --------------------------------------------------------------------------- #
# small helpers
# --------------------------------------------------------------------------- #
def font(size, bold=False):
    win = os.environ.get("WINDIR", "C:/Windows")
    names = (("arialbd.ttf", "segoeuib.ttf", "consolab.ttf")
             if bold else ("arial.ttf", "segoeui.ttf", "consola.ttf", "DejaVuSans.ttf"))
    for name in names:
        path = os.path.join(win, "Fonts", name)
        if os.path.isfile(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    try:
        return ImageFont.load_default(size)
    except TypeError:                                   # older Pillow
        return ImageFont.load_default()


def font_cjk(size, bold=False):
    win = os.environ.get("WINDIR", "C:/Windows")
    names = (("msjhbd.ttc", "msyhbd.ttc", "mingliub.ttc")
             if bold else ("msjh.ttc", "msyh.ttc", "mingliu.ttc"))
    for name in names:
        path = os.path.join(win, "Fonts", name)
        if os.path.isfile(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return font(size, bold)


def text_size(draw, text, fnt):
    box = draw.textbbox((0, 0), text, font=fnt)
    return box[2] - box[0], box[3] - box[1]


def draw_label(draw, xy, text, fnt, fill=INK, bg=(255, 255, 255, 232), pad=4):
    w, h = text_size(draw, text, fnt)
    x, y = xy
    draw.rounded_rectangle((x - pad, y - pad, x + w + pad, y + h + 2 * pad), radius=4,
                           fill=bg, outline=None)
    draw.text((x, y), text, font=fnt, fill=fill)
    return w, h


def parse_tier_definitions():
    with open(TIER_DEFINITION_PATH, "r", encoding="utf-8") as handle:
        text = handle.read()
    pattern = re.compile(
        r'PumpTier\.(\w+),\s*"(\w+)"[\s\S]*?sizeX:\s*(\d+),\s*sizeY:\s*(\d+),\s*sizeZ:\s*(\d+)')
    table = {}
    for match in pattern.finditer(text):
        tier = match.group(2)
        table[tier] = {
            "SizeX": int(match.group(3)),
            "SizeY_depth": int(match.group(4)),
            "SizeZ_height": int(match.group(5)),
        }
    expected = set(C.TIERS)
    if set(table) != expected:
        raise RuntimeError("TierDefinition.cs parse mismatch: %s" % sorted(table))
    return table


def load_blueprint(tier, faction):
    path = os.path.join(BLUEPRINT_DIR, "MWP.Pump.%s.%s.blueprint.json" % (tier, faction))
    with open(path, "r", encoding="utf-8") as handle:
        blueprint = json.load(handle)
    block = blueprint["BlockObjectSpec"]
    return {
        "path": os.path.relpath(path, WORKSPACE).replace("\\", "/"),
        "size": block["Size"],
        "base_z": block.get("BaseZ"),
        "entrance": block.get("Entrance"),
        "local_access": (blueprint.get("BuildingAccessibleSpec") or {}).get("LocalAccess"),
    }


def load_ports(stem):
    path = os.path.join(C.TIMBERMESH_DIR, stem + ".timbermesh")
    model = model_pb2.Model()
    with open(path, "rb") as handle:
        model.ParseFromString(zlib.decompress(handle.read()))
    ports = {}
    for node in model.nodes:
        if node.name.startswith("#Port#"):
            ports[node.name] = [round(node.position.x, 4), round(node.position.y, 4),
                                round(node.position.z, 4)]
    return ports


def project(layout_view, point):
    px_per_m = layout_view["px_per_m"]
    u = (point[AXIS_INDEX[layout_view["right_axis"]]] * layout_view["right_sign"]
         - layout_view["right_center"]) * px_per_m
    v = (point[AXIS_INDEX[layout_view["up_axis"]]] * layout_view["up_sign"]
         - layout_view["up_center"]) * px_per_m
    return (BASE_X + BASE_SIZE / 2.0 + u, BASE_TOP + BASE_SIZE / 2.0 - v)


def port_visible(view, point, size):
    width, height, depth = size
    if view == "front":
        return abs(point[2]) < 1e-3
    if view == "side":
        return abs(point[0] - width) < 1e-3
    return abs(point[1] - height) < 1e-3


def dashed_circle(draw, center, radius, color, width=2, segments=14):
    cx, cy = center
    for index in range(segments):
        if index % 2:
            continue
        start = 360.0 * index / segments
        end = 360.0 * (index + 1) / segments
        draw.arc((cx - radius, cy - radius, cx + radius, cy + radius),
                 start=start, end=end, fill=color, width=width)


AXIS_INDEX = {"x": 0, "y": 1, "z": 2}


# --------------------------------------------------------------------------- #
# annotated single view
# --------------------------------------------------------------------------- #
def draw_grid(draw, layout_view, world_ranges):
    px_per_m = layout_view["px_per_m"]
    x0, x1 = BASE_X, BASE_X + BASE_SIZE
    y0, y1 = BASE_TOP, BASE_TOP + BASE_SIZE
    right_axis = layout_view["right_axis"]
    up_axis = layout_view["up_axis"]
    right_min, right_max = world_ranges["right"]
    up_min, up_max = world_ranges["up"]
    for value in range(int(right_min) - 1, int(right_max) + 2):
        point = [0.0, 0.0, 0.0]
        point[AXIS_INDEX[right_axis]] = value / layout_view["right_sign"]
        px = project(layout_view, point)[0]
        if x0 <= px <= x1:
            draw.line((px, y0, px, y1), fill=GRID, width=1)
            label = "%d" % value
            draw.text((px - 5, y1 + 6), label, font=font(12), fill=MUTED)
    for value in range(int(up_min) - 1, int(up_max) + 2):
        point = [0.0, 0.0, 0.0]
        point[AXIS_INDEX[up_axis]] = value / layout_view["up_sign"]
        py = project(layout_view, point)[1]
        if y0 <= py <= y1:
            draw.line((x0, py, x1, py), fill=GRID, width=1)
            label = "%d" % value
            text_w, _ = text_size(draw, label, font(12))
            draw.text((x0 - text_w - 6, py - 7), label, font=font(12), fill=MUTED)


def draw_boundary_and_dimensions(draw, canvas, layout_view, size):
    width, height, depth = size
    if layout_view["view"] in ("front", "side"):
        right_extent, up_extent = ((width, height) if layout_view["view"] == "front"
                                   else (depth, height))
        right_axis_label = "X (width)" if layout_view["view"] == "front" else "Z (depth)"
    else:
        right_extent, up_extent = width, depth
        right_axis_label = "X (width)"
    corners = [(0.0, 0.0), (right_extent, 0.0), (right_extent, up_extent),
               (0.0, up_extent)]
    pixels = []
    for right_value, up_value in corners:
        point = [0.0, 0.0, 0.0]
        point[AXIS_INDEX[layout_view["right_axis"]]] = right_value / layout_view["right_sign"]
        point[AXIS_INDEX[layout_view["up_axis"]]] = up_value / layout_view["up_sign"]
        pixels.append(project(layout_view, point))
    draw.line(pixels + [pixels[0]], fill=BOUNDARY, width=2)

    # horizontal dimension below the envelope
    y = pixels[0][1] + 26
    draw.line((pixels[0][0], y, pixels[1][0], y), fill=INK, width=2)
    for px in (pixels[0][0], pixels[1][0]):
        draw.line((px, y - 6, px, y + 6), fill=INK, width=2)
    label = "%s = %.2f m" % (right_axis_label,
                             right_extent)
    text_w, text_h = text_size(draw, label, font(20, bold=True))
    draw_label(draw, ((pixels[0][0] + pixels[1][0] - text_w) / 2.0, y + 8), label,
               font(20, bold=True))

    # vertical dimension left of the envelope
    x = pixels[3][0] - 34
    draw.line((x, pixels[3][1], x, pixels[0][1]), fill=INK, width=2)
    for py in (pixels[3][1], pixels[0][1]):
        draw.line((x - 6, py, x + 6, py), fill=INK, width=2)
    vertical_axis_label = ("Z (depth)" if layout_view["view"] == "top"
                           else "Y (height)")
    label = "%s = %.2f m" % (vertical_axis_label, up_extent)
    text_w, text_h = text_size(draw, label, font(20, bold=True))
    vertical = Image.new("RGBA", (text_w + 14, text_h + 14), (255, 255, 255, 0))
    vertical_draw = ImageDraw.Draw(vertical)
    vertical_draw.rounded_rectangle((0, 0, vertical.width - 1, vertical.height - 1),
                                    radius=4, fill=(255, 255, 255, 238))
    vertical_draw.text((7, 6), label, font=font(20, bold=True), fill=INK)
    vertical = vertical.rotate(90, expand=True)
    canvas.paste(vertical,
                 (int(x - vertical.width / 2),
                  int((pixels[0][1] + pixels[3][1]) / 2 - vertical.height / 2)), vertical)

    return right_axis_label


def draw_ports(draw, layout_view, ports, size):
    for name, point in ports.items():
        px, py = project(layout_view, point)
        color = PORT_COLORS.get(name, (0, 0, 0))
        visible = port_visible(layout_view["view"], point, size)
        if visible:
            draw.ellipse((px - 9, py - 9, px + 9, py + 9), outline=color, width=3,
                         fill=(255, 255, 255, 235))
            draw.line((px - 13, py, px + 13, py), fill=color, width=1)
            draw.line((px, py - 13, px, py + 13), fill=color, width=1)
        else:
            dashed_circle(draw, (px, py), 9, color, width=2)
        label = "%s (%.2f, %.2f, %.2f)%s" % (name, point[0], point[1], point[2],
                                             "" if visible else "  hidden")
        text_w, text_h = text_size(draw, label, font(15, bold=True))
        center_x = BASE_X + BASE_SIZE / 2.0
        place_right = px >= center_x
        label_x = px + 16 if place_right else px - 16 - text_w - 8
        label_y = py - text_h - 16 if py > BASE_TOP + BASE_SIZE * 0.55 else py + 14
        label_x = min(max(label_x, BASE_X - 40), BASE_X + BASE_SIZE + 40 - text_w - 8)
        label_y = min(max(label_y, BASE_TOP - 10), BASE_TOP + BASE_SIZE - text_h - 12)
        draw.line((px, py, label_x + (0 if place_right else text_w + 8), label_y + text_h / 2),
                  fill=color, width=1)
        draw_label(draw, (label_x, label_y), label, font(15, bold=True))


def annotate_view(record, layout_view, data, ports):
    view = layout_view["view"]
    canvas = Image.new("RGB", (CANVAS_W, CANVAS_H), PAPER)
    draw = ImageDraw.Draw(canvas, "RGBA")

    # world ranges covered by the image (for the grid)
    half = (BASE_SIZE / 2.0) / layout_view["px_per_m"]
    right_center = layout_view["right_center"] * layout_view["right_sign"]
    up_center = layout_view["up_center"] * layout_view["up_sign"]
    world_ranges = {
        "right": (right_center - half, right_center + half),
        "up": (up_center - half, up_center + half),
    }
    draw_grid(draw, layout_view, world_ranges)

    base = Image.open(os.path.join(WORKSPACE, layout_view["base_png"])).convert("RGBA")
    canvas.paste(base, (BASE_X, BASE_TOP), base)

    # faint grid on top of the model so the 1 m spacing is readable over surfaces
    grid_overlay = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    overlay_draw = ImageDraw.Draw(grid_overlay)
    x0, x1 = BASE_X, BASE_X + BASE_SIZE
    y0, y1 = BASE_TOP, BASE_TOP + BASE_SIZE
    for value in range(int(world_ranges["right"][0]) - 1,
                       int(world_ranges["right"][1]) + 2):
        point = [0.0, 0.0, 0.0]
        point[AXIS_INDEX[layout_view["right_axis"]]] = value / layout_view["right_sign"]
        px = project(layout_view, point)[0]
        if x0 <= px <= x1:
            overlay_draw.line((px, y0, px, y1), fill=(255, 255, 255, 38), width=1)
    for value in range(int(world_ranges["up"][0]) - 1, int(world_ranges["up"][1]) + 2):
        point = [0.0, 0.0, 0.0]
        point[AXIS_INDEX[layout_view["up_axis"]]] = value / layout_view["up_sign"]
        py = project(layout_view, point)[1]
        if y0 <= py <= y1:
            overlay_draw.line((x0, py, x1, py), fill=(255, 255, 255, 38), width=1)
    canvas.paste(Image.alpha_composite(canvas.convert("RGBA"), grid_overlay).convert("RGB"),
                 (0, 0))

    draw = ImageDraw.Draw(canvas, "RGBA")

    draw_boundary_and_dimensions(draw, canvas, layout_view, data["size"])
    draw_ports(draw, layout_view, ports, data["size"])

    # title + hints + legend
    tier = record["tier"]
    faction = record["faction"]
    title = "MWP pump  ·  Mk %s (%s)  ·  %s  ·  %s" % (
        {"Basic": "I", "Intermediate": "II", "Advanced": "III", "Top": "IV"}[tier],
        tier, faction, VIEW_TITLES[view])
    draw.text((BASE_X, 42), title, font=font(26, bold=True), fill=INK)
    if view == "side":
        envelope_dims = (data["size"][2], data["size"][1])
    else:
        envelope_dims = (data["size"][0], data["size"][1])
    draw.text((BASE_X, 76), "orthographic · %s · 1 m grid · envelope %.2f × %.2f m"
              % (VIEW_HINTS[view], envelope_dims[0], envelope_dims[1]),
              font=font(16), fill=MUTED)
    legend_y = BASE_TOP + BASE_SIZE + 62
    draw.text((BASE_X, legend_y),
              "DV-03 axes: X = width, Y = height, Z = depth (export space; block-corner "
              "origin, front face Z = 0).",
              font=font(15), fill=MUTED)
    draw.text((BASE_X, legend_y + 24),
              "Ports from the shipped .timbermesh #Port# nodes: solid = on the near "
              "face, dashed = hidden behind the model.",
              font=font(15), fill=MUTED)
    draw.text((BASE_X, legend_y + 48),
              "Blueprints/TierDefinition use the engine field order X = width, "
              "Y = depth, Z = height.",
              font=font(15), fill=MUTED)
    # port colour legend on its own row
    legend_items = [("#Port#Power", "#C0392B"), ("#Port#Pipe", "#2980B9"),
                    ("#Port#Logistics", "#27AE60")]
    legend_x = BASE_X
    for index, (name, color) in enumerate(legend_items):
        x = legend_x + index * 180
        draw.ellipse((x, legend_y + 76, x + 12, legend_y + 88),
                     fill=PORT_COLORS[name])
        draw.text((x + 18, legend_y + 73), name, font=font(13), fill=MUTED)

    target = os.path.join(ORTHO_DIR, "%s_%s.png" % (record["stem"], view))
    canvas.save(target)
    return target


# --------------------------------------------------------------------------- #
# three-view sheet + overview + sign-off markdown
# --------------------------------------------------------------------------- #
def three_view_sheet(record, view_paths, data, ports, tier_defs):
    tier = record["tier"]
    faction = record["faction"]
    width, height, depth = data["size"]
    tier_def = tier_defs[tier]
    blueprint = record["blueprint"]
    block = blueprint["size"]

    cell_w = 560
    header_h = 92
    table_h = 300
    sheet = Image.new("RGB", (3 * cell_w + 40, header_h + cell_w + table_h), PAPER)
    draw = ImageDraw.Draw(sheet)

    roman = {"Basic": "I", "Intermediate": "II", "Advanced": "III", "Top": "IV"}[tier]
    draw.text((24, 22), "MWP pump · Mk %s (%s) · %s — front / side / top orthographic "
              "three-view sign-off" % (roman, tier, faction),
              font=font(24, bold=True), fill=INK)
    draw.text((24, 56), "export space X=width, Y=height, Z=depth · scale fixed per "
              "model · 1 m grid · ports from the shipped .timbermesh",
              font=font(15), fill=MUTED)
    for index, view in enumerate(("front", "side", "top")):
        image = Image.open(view_paths[view]).convert("RGB")
        scale = min(cell_w / float(image.width), cell_w / float(image.height))
        resized = image.resize((int(image.width * scale), int(image.height * scale)),
                               Image.LANCZOS)
        x = 20 + index * cell_w + (cell_w - resized.width) // 2
        y = header_h + (cell_w - resized.height) // 2
        sheet.paste(resized, (x, y))

    table_y = header_h + cell_w + 12
    draw.line((20, table_y - 6, sheet.width - 20, table_y - 6), fill=MUTED, width=1)
    lines = [
        "Export bounds:  min (0, 0, 0)  →  max (%.2f, %.2f, %.2f)   "
        "[X width, Y height, Z depth]   =  %.2f × %.2f × %.2f m"
        % (width, height, depth, width, height, depth),
        "TierDefinition.cs:  SizeX=%-2d (width)   SizeY=%-2d (depth)   SizeZ=%-2d (height)"
        % (tier_def["SizeX"], tier_def["SizeY_depth"], tier_def["SizeZ_height"]),
        "Blueprint BlockObjectSpec.Size:  {X:%d, Y:%d, Z:%d}   BaseZ=%s   "
        "Entrance=(%s)   LocalAccess=(%s)"
        % (block["X"], block["Y"], block["Z"], blueprint["base_z"],
           ", ".join(str(blueprint["entrance"]["Coordinates"][key]) for key in "XYZ"),
           ", ".join("%.2f" % blueprint["local_access"][key] for key in "XYZ")),
        "Ports:  " + "   ".join("%s (%.2f, %.2f, %.2f)" % (name, *point)
                                for name, point in sorted(ports.items())),
        "Model:  %d triangles · %d animated nodes · materials: %s"
        % (data["triangles_count"], len(data.get("parts", [])),
           ", ".join(data["materials"])),
        "Consistency (export ⇄ TierDefinition ⇄ blueprint ⇄ .timbermesh ports): %s"
        % record["consistency"],
    ]
    for index, line in enumerate(lines):
        draw.text((24, table_y + 4 + index * 26), line, font=font(16), fill=INK)

    sign_y = table_y + 4 + len(lines) * 26 + 12
    draw.rectangle((20, sign_y, sheet.width - 20, sign_y + 38), outline=MUTED, width=1)
    draw.text((30, sign_y + 10),
              "使用者簽核 USER SIGN-OFF:   [ ] 三視圖尺寸/軸向 OK    "
              "[ ] 3 個端口座標 OK       日期/簽名 date/initials: ______________",
              font=font_cjk(16, bold=True), fill=INK)

    target = os.path.join(ART07_DIR, "%s_three_views.png" % record["stem"])
    sheet.save(target)
    return target


def overview_grid(records):
    cell = 400
    label_w = 150
    header_h = 104
    footer_h = 50
    rows = len(C.TIERS)
    grid = Image.new("RGB", (label_w + 3 * cell + 30,
                              header_h + rows * cell + footer_h), PAPER)
    draw = ImageDraw.Draw(grid)
    draw.text((20, 18), "ART-07 · MWP pump orthographic three-view overview (Folktails)",
              font=font(26, bold=True), fill=INK)
    draw.text((20, 50), "1 m grid · fixed per-model scale · ports from the shipped "
              ".timbermesh · IronTeeth variants in art07/<Tier>.IronTeeth_three_views.png",
              font=font(14), fill=MUTED)
    for column, view in enumerate(("front", "side", "top")):
        draw.text((label_w + column * cell + 12, header_h - 28),
                  VIEW_TITLES[view], font=font(18, bold=True), fill=INK)
    by_tier = {}
    for record in records:
        if record["faction"] == "Folktails":
            by_tier[record["tier"]] = record
    for row, tier in enumerate(C.TIERS):
        record = by_tier[tier]
        roman = {"Basic": "I", "Intermediate": "II", "Advanced": "III", "Top": "IV"}[tier]
        y = header_h + row * cell
        draw.text((18, y + cell // 2 - 26), "Mk %s" % roman, font=font(22, bold=True),
                  fill=INK)
        draw.text((18, y + cell // 2), tier, font=font(15), fill=MUTED)
        draw.text((18, y + cell // 2 + 20), "%.2f × %.2f × %.2f m"
                  % tuple(record["size"]), font=font(13), fill=MUTED)
        for column, view in enumerate(("front", "side", "top")):
            path = os.path.join(ORTHO_DIR, "%s_%s.png" % (record["stem"], view))
            image = Image.open(path).convert("RGB")
            scale = min((cell - 8) / float(image.width), (cell - 8) / float(image.height))
            resized = image.resize((int(image.width * scale), int(image.height * scale)),
                                   Image.LANCZOS)
            grid.paste(resized, (label_w + column * cell + (cell - resized.width) // 2,
                                 y + (cell - resized.height) // 2))
    draw.text((20, header_h + rows * cell + 14),
              "Generated offline (Blender Cycles orthographic render + Pillow annotation). "
              "Full per-model sheets: art/previews/art07/ · sign-off report: "
              "art/reports/art07/three-view-signoff.md",
              font=font(14), fill=MUTED)
    target = os.path.join(ART07_DIR, "art07_overview_grid.png")
    grid.save(target)
    return target


def consistency(record, data, ports, tier_defs, blueprint):
    width, height, depth = data["size"]
    tier_def = tier_defs[record["tier"]]
    block = blueprint["size"]
    checks = {
        "bounds_min_zero": all(abs(value) < 1e-6 for value in data["bounds_min"]),
        "export_size_wdh": [round(width, 6), round(height, 6), round(depth, 6)]
        == [round(tier_def["SizeX"], 6), round(tier_def["SizeZ_height"], 6),
            round(tier_def["SizeY_depth"], 6)],
        "export_eq_blueprint": (round(width, 6) == block["X"]
                                and round(height, 6) == block["Z"]
                                and round(depth, 6) == block["Y"]),
        "tierdef_eq_blueprint": (tier_def["SizeX"] == block["X"]
                                 and tier_def["SizeY_depth"] == block["Y"]
                                 and tier_def["SizeZ_height"] == block["Z"]),
        "ports_match_contract": ports == {
            name: [round(value, 4) for value in position]
            for name, position in C.port_positions(record["tier"]).items()},
    }
    return checks


def write_signoff_md(records, tier_defs):
    lines = []
    lines.append("# ART-07 — 正交三視圖＋端口座標簽核（MWP 四檔 × 兩陣營）")
    lines.append("")
    lines.append("> 本檔由 `art/make_ortho_views.py` 生成；三視圖是**離線正交渲染**（Blender Cycles"
                 " 平面類別色 + Pillow 標註），不是遊戲截圖。")
    lines.append("> 只新增簽核包，未修改任何模型／blueprint／`src`。")
    lines.append("")
    lines.append("## 0. 座標系慣例（DV-03，已由遊戲 IL 證實）")
    lines.append("")
    lines.append("| 空間 | 欄位順序 | 說明 |")
    lines.append("|---|---|---|")
    lines.append("| 模型 export space（`.timbermesh`） | **X=寬、Y=高、Z=深** | block-corner 原點，"
                 "min=(0,0,0)、max=(寬,高,深)；正面在 Z=0、進水側在 Z=深 |")
    lines.append("| 引擎 `BlockObjectSpec.Size` / `TierDefinition` | **X=寬、Y=深、Z=高** | "
                 "需求文件的「X×高×Z」需換算成引擎欄位順序 |")
    lines.append("| `Entrance.Coordinates` | (X=寬度中心, Y=−1=前方門面外一格, Z=BaseZ=底面高度層) | "
                 "`BaseZ=1`（泵體離水面一格） |")
    lines.append("")
    lines.append("換算：`export_max = (Tier.SizeX, Tier.SizeZ, Tier.SizeY)`；"
                 "`export_size.Y(高) = blueprint.Size.Z`、`export_size.Z(深) = blueprint.Size.Y`。")
    lines.append("")
    lines.append("## 1. 檔案清單")
    lines.append("")
    lines.append("| 類型 | 路徑 | 數量 |")
    lines.append("|---|---|---|")
    lines.append("| 標註正交三視圖 | `art/previews/ortho/<Tier>.<Faction>_<front\\|side\\|top>.png` | 24 |")
    lines.append("| 每檔三視圖合圖＋規格表 | `art/previews/art07/<Tier>.<Faction>_three_views.png` | 8 |")
    lines.append("| 總覽 | `art/previews/art07/art07_overview_grid.png` | 1 |")
    lines.append("| 基礎渲染（中間產物） | `art/previews/ortho/_base/*.png` | 24 |")
    lines.append("| 版面 JSON | `art/previews/ortho/ortho-layout.json` | 1 |")
    lines.append("")
    lines.append("## 2. 逐檔一致性表")
    lines.append("")
    lines.append("每檔列出：export bounds、三視圖尺寸（X 寬／Y 高／Z 深）、`TierDefinition.cs` "
                 "SizeX/SizeY(Depth)/SizeZ(Height)、blueprint `Size`／入口／3 個 port 座標，"
                 "以及一致性結果。")
    lines.append("")
    overall_all = True
    for record in records:
        data = record["data"]
        ports = record["ports"]
        tier_def = tier_defs[record["tier"]]
        blueprint = record["blueprint"]
        block = blueprint["size"]
        checks = record["checks"]
        passed = all(checks.values())
        overall_all = overall_all and passed
        width, height, depth = data["size"]
        lines.append("### %s" % record["stem"])
        lines.append("")
        lines.append("- 三視圖：`art/previews/ortho/%s_front.png`、`_side.png`、`_top.png`；"
                     "合圖 `art/previews/art07/%s_three_views.png`"
                     % (record["stem"], record["stem"]))
        lines.append("- 檔案：`art/timbermesh/%s.timbermesh`（%d bytes，sha256 `%s`）"
                     % (record["stem"], record["manifest"]["bytes"],
                        record["manifest"]["sha256"]))
        lines.append("")
        lines.append("| 項目 | 值 |")
        lines.append("|---|---|")
        lines.append("| export bounds（最小） | (%.3f, %.3f, %.3f) |" % tuple(data["bounds_min"]))
        lines.append("| export bounds（最大） | (%.3f, %.3f, %.3f) |" % tuple(data["bounds_max"]))
        lines.append("| 三視圖尺寸（X 寬／Y 高／Z 深） | **%.2f × %.2f × %.2f m** |"
                     % (width, height, depth))
        lines.append("| `TierDefinition.cs` | SizeX=%d（寬）、SizeY=%d（深）、SizeZ=%d（高） |"
                     % (tier_def["SizeX"], tier_def["SizeY_depth"], tier_def["SizeZ_height"]))
        lines.append("| blueprint `BlockObjectSpec.Size` | {X:%d（寬）, Y:%d（深）, Z:%d（高）} |"
                     % (block["X"], block["Y"], block["Z"]))
        lines.append("| blueprint `BaseZ` | %s |" % blueprint["base_z"])
        lines.append("| blueprint `Entrance.Coordinates` | X=%s, Y=%s, Z=%s |"
                     % tuple(blueprint["entrance"]["Coordinates"][key] for key in "XYZ"))
        lines.append("| blueprint `BuildingAccessibleSpec.LocalAccess` | (%.2f, %.2f, %.2f) |"
                     % tuple(blueprint["local_access"][key] for key in "XYZ"))
        for name in ("#Port#Power", "#Port#Pipe", "#Port#Logistics"):
            point = ports[name]
            lines.append("| `%s`（.timbermesh） | (%.2f, %.2f, %.2f) |"
                         % (name, point[0], point[1], point[2]))
        lines.append("| triangles / animated nodes | %d / %d |"
                     % (data["triangles_count"], len(data.get("parts", []))))
        lines.append("| materials | %s |" % ", ".join("`%s`" % name for name in data["materials"]))
        lines.append("")
        lines.append("| 一致性檢查 | 結果 |")
        lines.append("|---|---|")
        labels = {
            "bounds_min_zero": "bounds min == (0,0,0)",
            "export_size_wdh": "export size == (Tier.SizeX, Tier.SizeZ, Tier.SizeY)",
            "export_eq_blueprint": "export (X,Y,Z) == blueprint (X, Z, Y) 換算",
            "tierdef_eq_blueprint": "TierDefinition == blueprint Size 欄位",
            "ports_match_contract": "3 個 #Port# 座標 == 契約",
        }
        for key, label in labels.items():
            lines.append("| %s | **%s** |" % (label, "PASS" if checks[key] else "FAIL"))
        lines.append("| **整體** | **%s** |" % ("PASS" if passed else "FAIL"))
        lines.append("")
        lines.append("**使用者簽核 USER SIGN-OFF**："
                     "☐ 三視圖尺寸/軸向 OK　☐ 3 個端口座標 OK　"
                     "日期/簽名 date/initials：________________")
        lines.append("")
    lines.append("## 3. 總表（8 檔）")
    lines.append("")
    lines.append("| # | 檔 | export（X寬,Y高,Z深） | TierDef (SizeX,SizeY深,SizeZ高) | "
                 "blueprint Size | 一致性 | 簽核 |")
    lines.append("|---:|---|---|---|---|---|---|")
    for index, record in enumerate(records, start=1):
        data = record["data"]
        tier_def = tier_defs[record["tier"]]
        block = record["blueprint"]["size"]
        width, height, depth = data["size"]
        passed = all(record["checks"].values())
        lines.append("| %d | `%s` | %.0f × %.0f × %.0f | (%d, %d, %d) | "
                     "{%d, %d, %d} | **%s** | ☐ |"
                     % (index, record["stem"], width, height, depth,
                        tier_def["SizeX"], tier_def["SizeY_depth"], tier_def["SizeZ_height"],
                        block["X"], block["Y"], block["Z"],
                        "PASS" if passed else "FAIL"))
    lines.append("")
    lines.append("**整體結果：%s**" % ("全部 PASS" if overall_all else "有 FAIL"))
    lines.append("")
    lines.append("## 4. 重跑方式")
    lines.append("")
    lines.append("```powershell")
    lines.append("cd \"C:\\Users\\user\\Downloads\\Mechanical Water Pump\"")
    lines.append("# 1) 正交基礎渲染（Blender 4.2.1 headless；24 張 → art/previews/ortho/_base/）")
    lines.append("& 'C:\\Users\\user\\.hdbf\\sdk\\blender-4.2.1-windows-x64\\blender.exe' "
                 "-b --factory-startup -noaudio --python art\\render_ortho_views.py")
    lines.append("# 2) 網格/軸向/尺寸/端口標註 + 三視圖合圖 + 總覽 + 本簽核表（純 Python + Pillow）")
    lines.append("python art\\make_ortho_views.py")
    lines.append("```")
    lines.append("")
    lines.append("## 5. 已知限制")
    lines.append("")
    lines.append("1. 三視圖是**離線正交渲染**，不是遊戲內截圖；幾何/端口座標直接取自 shipped "
                 "`.timbermesh`。")
    lines.append("2. 平面類別色只用來分辨木/金屬/玻璃，不代表遊戲內最終材質外觀。")
    lines.append("3. 遮擋關係：front 只看到 Z=0 面、side 只看到 X=寬 面、top 只看到 Y=高 面；"
                 "其餘 port 以虛線標記 `hidden`。")
    lines.append("")
    os.makedirs(REPORT_DIR, exist_ok=True)
    target = os.path.join(REPORT_DIR, "three-view-signoff.md")
    with open(target, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")
    return target


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def main():
    if not os.path.isfile(LAYOUT_PATH):
        raise SystemExit("missing %s - run art/render_ortho_views.py first" % LAYOUT_PATH)
    with open(LAYOUT_PATH, "r", encoding="utf-8") as handle:
        layout = json.load(handle)
    with open(MANIFEST_PATH, "r", encoding="utf-8") as handle:
        render_manifest = json.load(handle)
    export_manifest = json.loads(open(os.path.join(HERE, "export-manifest.json"),
                                      encoding="utf-8").read())
    manifest_by_stem = {entry["stem"]: entry for entry in export_manifest["models"]}
    tier_defs = parse_tier_definitions()

    os.makedirs(ORTHO_DIR, exist_ok=True)
    os.makedirs(ART07_DIR, exist_ok=True)

    records = []
    annotated_paths = []
    base_renders = [os.path.relpath(os.path.join(WORKSPACE, layout_view["base_png"]),
                                    WORKSPACE).replace("\\", "/")
                    for record in layout["views"]
                    for layout_view in record["views"].values()]
    for record in layout["views"]:
        spec = next(item for item in C.all_model_specs()
                    if item["stem"] == record["stem"])
        with open(spec["source"], "r", encoding="utf-8") as handle:
            data = json.load(handle)
        data["stem"] = record["stem"]
        ports = load_ports(record["stem"])
        blueprint = load_blueprint(record["tier"], record["faction"])
        view_paths = {}
        for view, layout_view in record["views"].items():
            path = annotate_view(record, layout_view, data, ports)
            view_paths[view] = path
            annotated_paths.append(os.path.relpath(path, WORKSPACE).replace("\\", "/"))
        checks = consistency(record, data, ports, tier_defs, blueprint)
        record.update({
            "data": data, "ports": ports, "blueprint": blueprint, "checks": checks,
            "consistency": "PASS" if all(checks.values()) else "FAIL",
            "manifest": manifest_by_stem[record["stem"]],
            "view_paths": view_paths,
        })
        record.pop("views", None)
        records.append(record)
        print("annotated %s %s" % (record["stem"], record["consistency"]))

    sheet_paths = []
    for record in records:
        path = three_view_sheet(record, record["view_paths"], record["data"],
                                record["ports"], tier_defs)
        sheet_paths.append(os.path.relpath(path, WORKSPACE).replace("\\", "/"))
        print("sheet:", os.path.basename(path))

    overview = overview_grid(records)
    report = write_signoff_md(records, tier_defs)

    def sha256_of(path):
        with open(path, "rb") as handle:
            return hashlib.sha256(handle.read()).hexdigest()

    checksums = {}
    for directory in (ORTHO_DIR, ART07_DIR):
        for name in sorted(os.listdir(directory)):
            if name.endswith(".png"):
                path = os.path.join(directory, name)
                checksums[os.path.relpath(path, WORKSPACE).replace("\\", "/")] = sha256_of(path)
    checksums[os.path.relpath(report, WORKSPACE).replace("\\", "/")] = sha256_of(report)
    checksums[os.path.relpath(LAYOUT_PATH, WORKSPACE).replace("\\", "/")] = sha256_of(LAYOUT_PATH)

    art07 = {
        "generated_by": ["art/render_ortho_views.py", "art/make_ortho_views.py"],
        "sha256": checksums,
        "conventions": layout["conventions"],
        "base_renders": base_renders,
        "annotated_views": annotated_paths,
        "three_view_sheets": sheet_paths,
        "overview_grid": os.path.relpath(overview, WORKSPACE).replace("\\", "/"),
        "signoff_report": os.path.relpath(report, WORKSPACE).replace("\\", "/"),
        "layout_json": os.path.relpath(LAYOUT_PATH, WORKSPACE).replace("\\", "/"),
        "counts": {"annotated_views": len(annotated_paths),
                   "three_view_sheets": len(sheet_paths),
                   "models": len(records)},
        "port_legend": {"#Port#Power": "#C0392B", "#Port#Pipe": "#2980B9",
                        "#Port#Logistics": "#27AE60"},
        "scale": "ortho_scale = max(width,height,depth)+1.0 m per model; "
                 "px_per_m recorded in ortho-layout.json",
        "models": [
            {"stem": record["stem"], "tier": record["tier"],
             "faction": record["faction"], "size_wdh": record["data"]["size"],
             "consistency": record["consistency"], "checks": record["checks"],
             "ports": record["ports"], "views": record["view_paths"],
             "sheet": sheet_paths[index]}
            for index, record in enumerate(records)
        ],
    }
    render_manifest["art07_three_view_signoff"] = art07
    with open(MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(render_manifest, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    print("render manifest updated")
    print("sign-off report:", os.path.relpath(report, WORKSPACE))


if __name__ == "__main__":
    main()
