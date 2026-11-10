"""ART-07 / T13 - orthographic three-view base renders (Blender 4.2.1, headless).

Renders the front / side / top ORTHOGRAPHIC views of every finished MWP model
(4 tiers x 2 factions x 3 views = 24 base images).  The annotation (1 m grid,
axis labels, boundary dimensions, port markers) is added afterwards by
``art/make_ortho_views.py`` (pure CPython + Pillow), which reads the layout JSON
written here; this script only renders the model with *technical* flat category
colours (wood / metal / glass) and transparent background.

Conventions (DV-03, verified by the Lead from game IL; see mod/docs/CONTRACT.md):
  * model export space used by every .timbermesh: X = width, Y = height, Z = depth,
    origin = block-corner, front face at Z = 0, intake side at Z = depth.
  * blueprint/TierDefinition engine order: X = width, Y = depth, Z = height.
  * front view looks along +Z from Z < 0 (sees the front face at Z = 0),
    side view looks along -X from X > width (right side),
    top view looks down -Y from above (screen up = +Z / depth).
  * fixed scale per model: ortho_scale = max(width, height, depth) + 1.0 m, so the
    1 m grid has a constant pixel size inside a model's three views
    (px_per_m = size_px / ortho_scale, recorded in the layout JSON).

Run:
  & 'C:\\Users\\user\\.hdbf\\sdk\\blender-4.2.1-windows-x64\\blender.exe'
      -b --factory-startup -noaudio --python art\\render_ortho_views.py
Outputs:
  art/previews/ortho/_base/<Tier>.<Faction>_<view>.png   (640x640 RGBA base)
  art/previews/ortho/ortho-layout.json
"""

import json
import math
import os
import sys

import bpy
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
for path in (HERE, os.path.join(HERE, "tools")):
    if path not in sys.path:
        sys.path.insert(0, path)

import mwp_common as C  # noqa: E402
import mwp_scene as S  # noqa: E402

SIZE_PX = 640
SAMPLES = 24
VIEWS = ("front", "side", "top")
BASE_DIR = os.path.join(C.PREVIEWS_DIR, "ortho", "_base")
LAYOUT_PATH = os.path.join(C.PREVIEWS_DIR, "ortho", "ortho-layout.json")

#: view definitions in EXPORT space: (forward axis, forward sign, right axis,
#: right sign, up axis, up sign, camera offset along forward)
VIEW_SPECS = {
    "front": {"forward": ("z", +1.0), "right": ("x", +1.0), "up": ("y", +1.0)},
    "side": {"forward": ("x", -1.0), "right": ("z", +1.0), "up": ("y", +1.0)},
    "top": {"forward": ("y", -1.0), "right": ("x", +1.0), "up": ("z", +1.0)},
}
AXIS_INDEX = {"x": 0, "y": 1, "z": 2}


def category_color(material_name):
    if "BaseWood" in material_name or "IrregularPlanks" in material_name \
            or "RoofPlanks" in material_name or "Thatched" in material_name:
        return (0.42, 0.29, 0.16, 1.0), 0.85
    if "BaseMetal" in material_name or "Details" in material_name:
        return (0.60, 0.63, 0.66, 1.0), 0.45
    if "WindowsAtlas" in material_name:
        return (0.52, 0.74, 0.86, 1.0), 0.20
    if "Plaster" in material_name:
        return (0.82, 0.78, 0.70, 1.0), 0.90
    return (0.62, 0.62, 0.62, 1.0), 0.80


def build_material(name):
    color, metallic = category_color(name)
    material = bpy.data.materials.new(name="ORTHO_" + name)
    material.use_nodes = True
    shader = material.node_tree.nodes["Principled BSDF"]
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Roughness"].default_value = 0.9
    shader.inputs["Metallic"].default_value = 0.0 if metallic > 0.5 else 0.35
    return material


def reset_scene():
    S.reset_scene()
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.seed = 0
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.film_transparent = True
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0.0
    world = bpy.data.worlds.new("OrthoWorld")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (0.72, 0.74, 0.76, 1.0)
    background.inputs["Strength"].default_value = 0.75
    scene.world = world
    sun_data = bpy.data.lights.new("OrthoSun", type='SUN')
    sun_data.energy = 3.0
    sun_data.angle = math.radians(8.0)
    sun = bpy.data.objects.new("OrthoSun", sun_data)
    sun.rotation_euler = (math.radians(48.0), math.radians(12.0), math.radians(-36.0))
    scene.collection.objects.link(sun)
    return scene


def axis_unit(axis, sign):
    vector = [0.0, 0.0, 0.0]
    vector[AXIS_INDEX[axis]] = sign
    return tuple(vector)


def render_view(data, view):
    spec = VIEW_SPECS[view]
    width, height, depth = data["size"]
    ortho_scale = max(width, height, depth) + 1.0
    center = (width * 0.5, height * 0.5, depth * 0.5)

    forward = axis_unit(*spec["forward"])
    right = axis_unit(*spec["right"])
    up = axis_unit(*spec["up"])

    # Blender directions (proper rotation export -> blender, see mwp_scene)
    forward_b = S.export_to_blender(forward)
    right_b = S.export_to_blender(right)
    up_b = S.export_to_blender(up)

    camera_location = center
    for axis, sign in (spec["forward"],):
        index = AXIS_INDEX[axis]
        offset = 10.0
        camera_location = tuple(
            camera_location[i] + (sign * -offset if i == index else 0.0)
            for i in range(3))
    location_b = S.export_to_blender(camera_location)

    camera_data = bpy.data.cameras.new("OrthoCamera_" + view)
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = ortho_scale
    camera_data.clip_start = 0.01
    camera_data.clip_end = 100.0
    camera = bpy.data.objects.new("OrthoCamera_" + view, camera_data)
    rotation = mathutils.Matrix.Identity(4)
    for row in range(3):
        rotation[row][0] = right_b[row]
        rotation[row][1] = up_b[row]
        rotation[row][2] = -forward_b[row]
    camera.matrix_world = (mathutils.Matrix.Translation(mathutils.Vector(location_b))
                           @ rotation)
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera
    bpy.context.scene.frame_set(C.ANIM_FRAME_START)

    target = os.path.join(BASE_DIR, "%s_%s_base.png" % (data["stem"], view))
    os.makedirs(os.path.dirname(target), exist_ok=True)
    scene = bpy.context.scene
    scene.render.resolution_x = SIZE_PX
    scene.render.resolution_y = SIZE_PX
    scene.render.resolution_percentage = 100
    scene.render.filepath = target
    bpy.ops.render.render(write_still=True)

    right_axis, right_sign = spec["right"]
    up_axis, up_sign = spec["up"]
    return {
        "base_png": os.path.relpath(target, C.WORKSPACE).replace("\\", "/"),
        "view": view,
        "size_px": SIZE_PX,
        "ortho_scale_m": round(ortho_scale, 6),
        "px_per_m": round(SIZE_PX / ortho_scale, 6),
        "right_axis": right_axis,
        "right_sign": right_sign,
        "right_center": center[AXIS_INDEX[right_axis]],
        "up_axis": up_axis,
        "up_sign": up_sign,
        "up_center": center[AXIS_INDEX[up_axis]],
        "forward_axis": spec["forward"][0],
        "forward_sign": spec["forward"][1],
        "camera_location_export": [round(value, 4) for value in camera_location],
        "camera_location_blender": [round(value, 4) for value in location_b],
    }


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only = None
    for index, value in enumerate(argv):
        if value == "--only" and index + 1 < len(argv):
            only = argv[index + 1]
    os.makedirs(BASE_DIR, exist_ok=True)
    specs = [spec for spec in C.all_model_specs() if not spec["construction"]]
    if only:
        specs = [spec for spec in specs if only in (spec["stem"], spec["tier"])]
    layout = {
        "generated_by": "art/render_ortho_views.py",
        "blender_version": bpy.app.version_string,
        "conventions": {
            "export_space": "X=width, Y=height, Z=depth; block-corner origin; "
                            "front face at Z=0, intake side at Z=depth (DV-03)",
            "engine_blueprint_space": "BlockObjectSpec.Size / TierDefinition: "
                                      "X=width, Y=depth, Z=height",
            "front_view": "camera on the -Z side looking +Z (front face visible)",
            "side_view": "camera on the +X side looking -X (right side)",
            "top_view": "camera above looking -Y; screen up = +Z (depth)",
            "scale": "ortho_scale = max(width,height,depth)+1.0 m, fixed per model "
                     "across its three views; 1 m grid is constant inside a model",
        },
        "size_px": SIZE_PX,
        "sampler": "Cycles CPU %d samples, flat material-category colours" % SAMPLES,
        "views": [],
    }
    for spec in specs:
        with open(spec["source"], "r", encoding="utf-8") as handle:
            data = json.load(handle)
        data["stem"] = spec["stem"]
        reset_scene()
        S.build_model_scene(data, material_factory=build_material, animate=False)
        record = {
            "stem": spec["stem"],
            "tier": spec["tier"],
            "faction": spec["faction"],
            "size": list(spec["size"]),
            "views": {},
        }
        for view in VIEWS:
            record["views"][view] = render_view(data, view)
            print("ortho base: %s %s" % (spec["stem"], view))
        layout["views"].append(record)
    with open(LAYOUT_PATH, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(layout, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    print("layout:", LAYOUT_PATH)
    print("rendered %d base views" % (len(layout["views"]) * len(VIEWS)))


if __name__ == "__main__":
    main()
