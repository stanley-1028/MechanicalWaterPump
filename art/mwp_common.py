"""MWP - Mechanical Water Pump: frozen art contract (pure Python, no bpy).

Single source of truth shared by
  * art/gen_models.py          (deterministic geometry generator)
  * art/export_timbermesh.py   (Blender 4.2.1 headless exporter)
  * art/validate_timbermesh.py (pure-Python .timbermesh validator)
  * art/render_previews.py / art/render_icons.py (Blender headless renders)

Coordinate systems (measured on the real game, see HDBF art/README-timbermesh.md
section 3, reused verbatim here):
  * export space = game block-object local space = BLOCK-CORNER origin,
    X = width [0, Size.X], Y = height/up [0, Size.Y], Z = depth [0, Size.Z],
    front (outlet / pickup) at Z = 0, intake side at Z = Size.Z.
  * the official plugin v1.2.0 maps Blender space to export space as
        export = (-blender.x, blender.z, -blender.y)
    (a reflection, so it also reverses triangle winding).  This generator
    authors geometry directly in export space; the exporter writes Blender
    vertices as  blender = (-export.x, -export.z, export.y)  and flips the
    winding once, so the shipped file has outward normals AND outward winding.

Sizes are the blueprint BlockObjectSpec sizes (X/Y/Z = width/height/depth, one
block == one game unit).  Basic/Intermediate 2x3x3, Advanced 3x4x3 (requirement
doc section 4/13.3), Top 3x4x4.  NOTE: the shipped Advanced blueprints currently
carry Size (3,3,4) while their CollidersSpec says (3,4,3); this module follows
the requirement document + the user's art table (3,4,3) and the discrepancy is
reported to the Lead (blueprints are owned by data-dev).

UVs: every face of a material is projected in WORLD SCALE onto the material's
verified safe patch canvas taken from art/material_texture_map.json (generated
from the real vanilla _D textures by HDBF's material_texture_analysis.py; copied
here unchanged).  Nothing is invented: patch pixel/uv rects, px_per_m and the
tile/patch canvas all come from that JSON.  The projection is the ART-07 policy
(uv = uv_center + (world - world_center) * px_per_m / texture_size), with
px_per_m capped so that a 4 m model still stays strictly inside the canvas.
"""

from __future__ import annotations

import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in __import__("sys").path:
    __import__("sys").path.insert(0, HERE)
import material_uv_policy as UVP  # ART-07 world-scale UV policy (copied from HDBF)
WORKSPACE = os.path.dirname(HERE)

TIERS = ["Basic", "Intermediate", "Advanced", "Top"]
FACTIONS = ["Folktails", "IronTeeth"]

#: export-space envelope (block-corner origin), (width X, height Y, depth Z).
#: Advanced follows requirement doc section 4 / 13.3 (3 x 4 x 3).
TIER_SIZE = {
    "Basic": (2.0, 3.0, 3.0),
    "Intermediate": (2.0, 3.0, 3.0),
    "Advanced": (3.0, 4.0, 3.0),
    "Top": (3.0, 4.0, 4.0),
}

#: per-file triangle budget for LOD0 (requirement 13.3: <= 12k).
TRI_BUDGET_LOD0 = 12000
#: the exported file is LOD0 only; the game's TimbermeshPrefabOptimizer builds LOD1/2.
LODS_EXPORTED = [0]

# --------------------------------------------------------------------------- #
# node animations (REQ: gears/flywheel rotate, pistons reciprocate; the game's
# MechanicalNodeAnimator already drives any TimbermeshAnimations.IAnimator that
# TimbermeshPrefabOptimizer creates from nodeAnimations, scaled by PowerEfficiency,
# disabled at 0 power - no C# / blueprint change is needed).
# --------------------------------------------------------------------------- #
ANIM_FPS = 24
ANIM_FRAME_START = 1
ANIM_FRAME_END = 120
ANIM_FRAMES = ANIM_FRAME_END - ANIM_FRAME_START + 1
#: node names of animated parts must start with '#' (official plugin's "root
#: object" convention: only such nodes become separate nodes and get nodeAnimations).
PART_PREFIX = "#Animated"
#: animated node name must never collide with the marker prefixes.
MARKER_PREFIXES = ("#Port#", "#Slot#", "#Finished", "#Unfinished")

BOUNDS_TOLERANCE = 0.001   # model must fill the envelope exactly (generator is exact)
SIZE_TOLERANCE = 0.02
#: tolerance for empty-marker (port) positions, grid units.
POSITION_TOLERANCE = 0.02

# --------------------------------------------------------------------------- #
# file names / paths
# --------------------------------------------------------------------------- #
MODELS_DIR = os.path.join(HERE, "models")
TIMBERMESH_DIR = os.path.join(HERE, "timbermesh")
PREVIEWS_DIR = os.path.join(HERE, "previews")
ICONS_DIR = os.path.join(HERE, "icons")

#: shipped-mod output dirs (the only files art writes outside art/)
MOD_DATA_DIR = os.path.join(WORKSPACE, "mod", "data")
MODEL_OUT_DIR = os.path.join(MOD_DATA_DIR, "Buildings", "Water", "MWP")
ICON_OUT_DIR = os.path.join(MOD_DATA_DIR, "Icons")

VANILLA_MODEL_DIR = os.path.join(WORKSPACE, "mod", "build", "MWP", "Buildings", "Water", "MWP")


def model_stem(tier: str, faction: str, construction: bool = False) -> str:
    if construction:
        return "MWP.Pump.%s.%s.ConstructionStage0.Model" % (tier, faction)
    return "MWP.Pump.%s.%s.Model" % (tier, faction)


def model_filename(tier: str, faction: str, construction: bool = False) -> str:
    return model_stem(tier, faction, construction) + ".timbermesh"


def source_json(tier: str, faction: str, construction: bool = False) -> str:
    return os.path.join(MODELS_DIR, model_stem(tier, faction, construction) + ".json")


def all_model_specs() -> list[dict]:
    specs = []
    for construction in (False, True):
        for tier in TIERS:
            for faction in FACTIONS:
                size = TIER_SIZE[tier]
                specs.append({
                    "tier": tier,
                    "faction": faction,
                    "construction": construction,
                    "stem": model_stem(tier, faction, construction),
                    "filename": model_filename(tier, faction, construction),
                    "source": source_json(tier, faction, construction),
                    "size": list(size),
                    "materials": MATERIALS[(tier, faction, construction)],
                    "ports": ports(tier, faction, construction),
                })
    return specs


# --------------------------------------------------------------------------- #
# vanilla material set (exact vanilla MaterialCollection asset names)
#
# Only names from HDBF's verified whitelist are used, and every shipped material
# has a verified safe patch / canvas in art/material_texture_map.json.
# Deviations (reported to the Lead):
#   * PaintedMetal.* is NOT used at all: the whole vanilla albedo is a
#     yellow/black (FT) resp. blue/black (IT) hazard stripe texture (same bug the
#     HDBF project hit); art/README.md documents this.
#   * IrregularPlanks_DarkBrown.IronTeeth is NOT used either: HDBF's verified
#     material map has no validated safe patch for it, and inventing one would
#     break the "no self-invented UV rects" rule.  IronTeeth plank cladding uses
#     IrregularPlanks_MetalForged.IronTeeth (vanilla IT material, verified patch,
#     reads as dark forged planks).
# --------------------------------------------------------------------------- #
MATERIAL_WHITELIST = {
    "Folktails": [
        "BaseWood_Brown.Folktails",
        "BaseWood_LightBrown.Folktails",
        "BaseWood_White.Folktails",
        "IrregularPlanks_Brown.Folktails",
        "IrregularPlanks_LightBrown.Folktails",
        "PaintedMetal.Folktails",
        "BaseMetal.Folktails",
        "WindowsAtlas.Folktails",
        "ThatchedRoof.Folktails",
        "Plaster_White.Folktails",
        "Details.Folktails",
        "Moss.Folktails",
    ],
    "IronTeeth": [
        "BaseWood_DarkBrown.IronTeeth",
        "BaseWood_Grey.IronTeeth",
        "IrregularPlanks_DarkBrown.IronTeeth",
        "IrregularPlanks_MetalForged.IronTeeth",
        "PaintedMetal.IronTeeth",
        "BaseMetal.IronTeeth",
        "WindowsAtlas.IronTeeth",
        "RoofPlanks.IronTeeth",
        "Plaster_Orange.IronTeeth",
        "Details.IronTeeth",
    ],
}

BANNED_MATERIALS = ("PaintedMetal.Folktails", "PaintedMetal.IronTeeth")

#: 2-4 vanilla materials per model, faction-correct.
MATERIALS = {
    # T10: Mk I / Mk II are ALL-WOOD constructions (no BaseMetal / Windows /
    # Plaster / Details).  FT: BaseWood_Brown + BaseWood_LightBrown +
    # IrregularPlanks_Brown; IT: BaseWood_DarkBrown + BaseWood_Grey +
    # IrregularPlanks_MetalForged (the only IrregularPlanks_* with a verified
    # canvas for IronTeeth).
    ("Basic", "Folktails", False):
        ["BaseWood_Brown.Folktails", "BaseWood_LightBrown.Folktails",
         "IrregularPlanks_Brown.Folktails"],
    ("Basic", "IronTeeth", False):
        ["BaseWood_DarkBrown.IronTeeth", "BaseWood_Grey.IronTeeth",
         "IrregularPlanks_MetalForged.IronTeeth"],
    ("Intermediate", "Folktails", False):
        ["BaseWood_LightBrown.Folktails", "BaseWood_Brown.Folktails",
         "IrregularPlanks_Brown.Folktails"],
    ("Intermediate", "IronTeeth", False):
        ["BaseWood_Grey.IronTeeth", "BaseWood_DarkBrown.IronTeeth",
         "IrregularPlanks_MetalForged.IronTeeth"],
    # T11 interior pass: dark timber backboard + bright metal machinery + glass.
    ("Advanced", "Folktails", False):
        ["BaseWood_Brown.Folktails", "BaseMetal.Folktails",
         "WindowsAtlas.Folktails", "Details.Folktails"],
    ("Advanced", "IronTeeth", False):
        ["BaseWood_DarkBrown.IronTeeth", "BaseMetal.IronTeeth",
         "WindowsAtlas.IronTeeth", "Details.IronTeeth"],
    ("Top", "Folktails", False):
        ["BaseWood_Brown.Folktails", "BaseMetal.Folktails",
         "WindowsAtlas.Folktails", "Details.Folktails"],
    ("Top", "IronTeeth", False):
        ["IrregularPlanks_MetalForged.IronTeeth", "BaseMetal.IronTeeth",
         "Details.IronTeeth", "WindowsAtlas.IronTeeth"],
}
for _tier in TIERS:
    MATERIALS[(_tier, "Folktails", True)] = [
        "BaseWood_Brown.Folktails", "BaseMetal.Folktails"]
    MATERIALS[(_tier, "IronTeeth", True)] = [
        "BaseWood_DarkBrown.IronTeeth", "BaseMetal.IronTeeth"]

for (_tier, _faction, _const), _names in MATERIALS.items():
    _whitelist = MATERIAL_WHITELIST[_faction]
    assert 1 <= len(_names) <= 4, (_tier, _faction, _names)
    assert len(set(_names)) == len(_names), (_tier, _faction, _names)
    for _name in _names:
        assert _name in _whitelist, (_faction, _name)
        assert _name.endswith("." + _faction), (_faction, _name)
        assert _name not in BANNED_MATERIALS, _name


# --------------------------------------------------------------------------- #
# port markers (exactly three #Port# empties, no #Slot#; v1.2 plugin convention)
#   * Power      side, mid height  -> power shaft coupling (both sides are modelled)
#   * Pipe       back, low         -> intake pipe flange (Z = depth)
#   * Logistics  front, mid        -> water outlet / pickup (Z = 0)
# --------------------------------------------------------------------------- #
def ports(tier: str, faction: str, construction: bool = False) -> list[dict]:
    width, height, depth = TIER_SIZE[tier]
    return [
        {"name": "#Port#Power",
         "position": [round(width, 4), round(height * 0.5, 4), round(depth * 0.5, 4)],
         "outward": [1.0, 0.0, 0.0],
         "role": "side power shaft coupling, mid height"},
        {"name": "#Port#Pipe",
         "position": [round(width * 0.5, 4), round(min(0.65, height * 0.22), 4),
                      round(depth, 4)],
         "outward": [0.0, 0.0, 1.0],
         "role": "back-bottom intake pipe flange"},
        {"name": "#Port#Logistics",
         "position": [round(width * 0.5, 4), 1.0, 0.0],
         "outward": [0.0, 0.0, -1.0],
         "role": "front water outlet / pickup"},
    ]


#: expected port positions checked by the validator (export space).
def port_positions(tier: str) -> dict:
    return {p["name"]: tuple(p["position"]) for p in ports(tier, "Folktails")}


# --------------------------------------------------------------------------- #
# verified vanilla-material UV patches (copied from HDBF unchanged)
# --------------------------------------------------------------------------- #
MATERIAL_MAP_PATH = os.path.join(HERE, "material_texture_map.json")
TEXTURE_SIZE = 1024
UV_GUARD_PX = 2
_MAP = None


def material_map() -> dict:
    global _MAP
    if _MAP is None:
        with open(MATERIAL_MAP_PATH, "r", encoding="utf-8") as handle:
            _MAP = json.load(handle)
    return _MAP


def material_entry(name: str) -> dict:
    materials = material_map()["materials"]
    if name not in materials:
        raise KeyError("vanilla material %r is not in the verified material map" % (name,))
    return materials[name]


def material_texture_path(name: str) -> str:
    return material_entry(name)["texture_path"]


def material_mean_rgb(name: str) -> list:
    return list(material_entry(name)["patch"]["mean_rgb"])


def material_tint(name: str) -> list:
    return list(material_entry(name).get("material_color_tint") or [1.0, 1.0, 1.0])


def uv_canvas(name: str) -> tuple:
    """Verified canvas of a vanilla material: (u0, v0, u1, v1, cu, cv, native_px_per_m).

    The canvas and the ART-07 native density come from the verified
    ``material_texture_map.json`` + ``material_uv_policy.py`` (both copied from
    HDBF unchanged): tile materials use the whole 1024^2 albedo, patch materials
    use the curated safe patch rect.  ``native_px_per_m`` is the ART-07 density
    (canvas px / measured 3 m coverage).
    """
    entry = material_entry(name)
    if entry["mode"] == "patch":
        # curated safe patch canvas of an atlas-sheet texture
        u0, v0, u1, v1 = [float(value) for value in entry["patch"]["uv_rect"]]
    else:
        # ART-07 "tile" material: the whole 1024^2 _D texture is the canvas
        u0, v0, u1, v1 = 0.0, 0.0, 1.0, 1.0
    native = float(UVP.px_per_m(name))
    return u0, v0, u1, v1, (u0 + u1) * 0.5, (v0 + v1) * 0.5, native


def _native_canvas_pixels(name: str) -> tuple:
    entry = material_entry(name)
    if entry["mode"] == "patch":
        x0, y0, x1, y1 = entry["patch"]["pixel_rect"]
        return float(x1 - x0), float(y1 - y0)
    return float(TEXTURE_SIZE), float(TEXTURE_SIZE)


def uv_plan(name: str, extent_u: float, extent_v: float) -> dict:
    """World-scale UV window for one material on one model (ART-07 rule).

    The window keeps the material's verified canvas (no UV may leave it, because
    the runtime AutoAtlaser remaps UVs into the material's atlas cell), uses the
    ART-07 native texel density as the upper bound and stretches only as far as
    the material's ACTUAL projected world span requires.  The window is centred
    on the material's own projected bounding box, so small parts still sample at
    native density instead of inheriting the whole-model span.
    """
    u0, v0, u1, v1, _cu, _cv, native = uv_canvas(name)
    canvas_w_px, canvas_h_px = _native_canvas_pixels(name)
    usable_w = max(canvas_w_px - 2.0 * UV_GUARD_PX, 8.0)
    usable_h = max(canvas_h_px - 2.0 * UV_GUARD_PX, 8.0)
    extent_u = max(float(extent_u), 1.0 / 64.0)
    extent_v = max(float(extent_v), 1.0 / 64.0)
    pu = min(native, usable_w / extent_u)
    pv = min(native, usable_h / extent_v)
    return {
        "u0": u0, "v0": v0, "u1": u1, "v1": v1,
        "pu": pu, "pv": pv, "native": native,
    }


#: projection plane per dominant face normal in EXPORT space (Y up, Z depth).
#: (u_axis, v_axis) as indices into (x, y, z).
PROJECTION_AXES = {
    "-X": (2, 1), "+X": (2, 1),
    "-Y": (0, 2), "+Y": (0, 2),
    "-Z": (0, 1), "+Z": (0, 1),
}


def dominant_axis(normal) -> str:
    nx, ny, nz = normal
    ax, ay, az = abs(nx), abs(ny), abs(nz)
    if ax >= ay and ax >= az:
        return "+X" if nx >= 0 else "-X"
    if ay >= az:
        return "+Y" if ny >= 0 else "-Y"
    return "+Z" if nz >= 0 else "-Z"


def projection_axes(normal) -> tuple:
    u_axis, v_axis = PROJECTION_AXES[dominant_axis(normal)]
    return u_axis, v_axis


def uv_for_plan(plan: dict, u_world, v_world, centre_u, centre_v) -> tuple:
    """UV of a projected world coordinate inside a material's planned window."""
    u = (plan["u0"] + plan["u1"]) * 0.5 + (u_world - centre_u) * plan["pu"] / TEXTURE_SIZE
    v = (plan["v0"] + plan["v1"]) * 0.5 + (v_world - centre_v) * plan["pv"] / TEXTURE_SIZE
    guard_u = UV_GUARD_PX / TEXTURE_SIZE
    guard_v = UV_GUARD_PX / TEXTURE_SIZE
    u = min(max(u, plan["u0"] + guard_u), plan["u1"] - guard_u)
    v = min(max(v, plan["v0"] + guard_v), plan["v1"] - guard_v)
    return round(u, 6), round(v, 6)


# --------------------------------------------------------------------------- #
# icon / preview naming (blueprint LabeledEntitySpec.Icon values)
# --------------------------------------------------------------------------- #
ICON_STEM = {
    ("Basic", "Folktails"): "MWPBasicFolktails",
    ("Basic", "IronTeeth"): "MWPBasicIronTeeth",
    ("Intermediate", "Folktails"): "MWPIntermediateFolktails",
    ("Intermediate", "IronTeeth"): "MWPIntermediateIronTeeth",
    ("Advanced", "Folktails"): "MWPAdvancedFolktails",
    ("Advanced", "IronTeeth"): "MWPAdvancedIronTeeth",
    ("Top", "Folktails"): "MWPTopFolktails",
    ("Top", "IronTeeth"): "MWPTopIronTeeth",
}


if __name__ == "__main__":
    print("MWP art contract")
    for tier in TIERS:
        width, height, depth = TIER_SIZE[tier]
        print("  %-13s export envelope X[0,%.1f] Y[0,%.1f] Z[0,%.1f]  front Z=0"
              % (tier, width, height, depth))
        for port in ports(tier, "Folktails"):
            print("      %-16s %s  %s" % (port["name"], port["position"], port["role"]))
    print("\nmaterials (finished):")
    for tier in TIERS:
        for faction in FACTIONS:
            print("  %-13s %-10s %s" % (tier, faction, MATERIALS[(tier, faction, False)]))
    print("\nmaterials (construction):")
    for faction in FACTIONS:
        print("  %-10s %s" % (faction, MATERIALS[("Basic", faction, True)]))
    print("\nmodel specs: %d (%d finished + %d construction)"
          % (len(all_model_specs()), 8, 8))
