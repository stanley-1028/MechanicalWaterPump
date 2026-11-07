"""MWP - offline previews and icons (Blender 4.2.1 LTS, headless).

Renders the generated models with the REAL vanilla material maps recorded in
``art/material_texture_map.json`` (albedo ``_D`` + optional ``_N`` normal map and
``_AO`` occlusion of the vanilla material), so the previews show what the shipped
world-scale UVs sample.  The scene is built by ``art/mwp_scene.py`` (the exact
same builder the exporter uses), which also means the animation previews show the
real exported node animation.

OUTPUT
  art/previews/<Tier>.<Faction>_iso.png                iso view, rest pose
  art/previews/<Tier>.<Faction>_iso50.png              iso view, model at 50 %
  art/previews/<Tier>.<Faction>_anim_f001.png          animation phase 0 (frame 1)
  art/previews/<Tier>.<Faction>_anim_f041.png          animation phase 1/3 (frame 41)
  art/previews/<Tier>.<Faction>_anim_f081.png          animation phase 2/3 (frame 81)
  art/previews/<Tier>.<Faction>_construction_iso.png   construction greybox (static)
  art/previews/icons_contact_4x2.png                   8 icons contact sheet
  art/previews/tiers_factions_iso_grid.png             4 tiers x 2 factions iso grid
  art/previews/animation_phases_grid.png               animation phases contact sheet
  art/icons/<IconStem>.512.png                         icon source render (transparent)
  art/icons/<IconStem>.png                             128x128 RGBA transparent icon
  mod/data/Icons/<IconStem>.png                        shipped icon (identical copy)
  art/render-manifest.json                             camera/material/output record

These are offline renders, NOT in-game screenshots.

Run:
  & 'C:\\Users\\user\\.hdbf\\sdk\\blender-4.2.1-windows-x64\\blender.exe'
      -b --factory-startup -noaudio --python art\\render_previews.py
          [-- --mode previews|icons|all] [--only <tier|stem>] [--samples 48]
"""

import json
import math
import os
import shutil
import sys

import bpy
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
for path in (HERE, os.path.join(HERE, "tools")):
    if path not in sys.path:
        sys.path.insert(0, path)

import mwp_common as C  # noqa: E402
import mwp_scene as S  # noqa: E402

MANIFEST_PATH = os.path.join(HERE, "render-manifest.json")
ICON_SIZE = 128
ICON_RENDER_SIZE = 512
PREVIEW_SIZE = 512
#: animation frames rendered for the phase previews (0 %, 33 %, 66 % of the loop)
ANIMATION_PREVIEW_FRAMES = (1, 41, 81)


# --------------------------------------------------------------------------- #
# scene setup
# --------------------------------------------------------------------------- #
def reset_scene():
    S.reset_scene()
    _MATERIAL_CACHE.clear()
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.cycles.seed = 0
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0.30
    return scene


def texture_roots():
    roots = []
    for entry in C.material_map()["materials"].values():
        directory = os.path.dirname(entry.get("texture_path", ""))
        if directory and directory not in roots:
            roots.append(directory)
    return roots


NORMAL_AO_CACHE = {}


def find_sibling_map(texture_name, suffix):
    """Find ``<stem>_N.png`` / ``<stem>_AO.png`` next to the albedo texture."""
    stem = os.path.basename(texture_name)
    for marker in ("_D1", "_D2", "_D_", "_D."):
        if marker in stem:
            stem = stem.split(marker)[0]
            break
    stem = stem.replace("_D", "")
    candidate = stem + suffix
    for root in texture_roots():
        path = os.path.join(root, candidate)
        if os.path.isfile(path):
            return path
    return None


def build_textured_material(name):
    """Vanilla material look: albedo x _Color tint x AO + normal map."""
    entry = C.material_entry(name)
    material = bpy.data.materials.new(name="R_" + name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    for node in list(nodes):
        nodes.remove(node)
    output = nodes.new("ShaderNodeOutputMaterial")
    output.location = (700, 0)
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.location = (380, 0)
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])

    texture_path = entry["texture_path"]
    tint = entry.get("material_color_tint") or [1.0, 1.0, 1.0]
    color_socket = shader.inputs["Base Color"]
    if os.path.isfile(texture_path):
        image = bpy.data.images.load(texture_path, check_existing=True)
        image.colorspace_settings.name = 'sRGB'
        tex = nodes.new("ShaderNodeTexImage")
        tex.location = (-520, 180)
        tex.image = image
        links.new(tex.outputs["Color"], color_socket)
        albedo_socket = tex.outputs["Color"]
    else:
        mean = entry["patch"]["mean_rgb"]
        color_socket.default_value = (mean[0] / 255.0, mean[1] / 255.0,
                                      mean[2] / 255.0, 1.0)
        albedo_socket = color_socket

    ao_path = find_sibling_map(entry["texture"], "_AO.png")
    if ao_path:
        ao_image = bpy.data.images.load(ao_path, check_existing=True)
        ao_image.colorspace_settings.name = 'Non-Color'
        ao_tex = nodes.new("ShaderNodeTexImage")
        ao_tex.location = (-520, -40)
        ao_tex.image = ao_image
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = 'RGBA'
        mix.blend_type = 'MULTIPLY'
        mix.location = (-60, 120)
        mix.inputs[0].default_value = 0.15
        links.new(albedo_socket, mix.inputs[6])
        links.new(ao_tex.outputs["Color"], mix.inputs[7])
        albedo_socket = mix.outputs[2]

    if any(abs(channel - 1.0) > 1e-3 for channel in tint):
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = 'RGBA'
        mix.blend_type = 'MULTIPLY'
        mix.location = (160, 120)
        mix.inputs[0].default_value = 1.0
        links.new(albedo_socket, mix.inputs[6])
        mix.inputs[7].default_value = (tint[0], tint[1], tint[2], 1.0)
        links.new(mix.outputs[2], color_socket)

    normal_path = find_sibling_map(entry["texture"], "_N.png")
    if normal_path:
        normal_image = bpy.data.images.load(normal_path, check_existing=True)
        normal_image.colorspace_settings.name = 'Non-Color'
        normal_tex = nodes.new("ShaderNodeTexImage")
        normal_tex.location = (-520, -260)
        normal_tex.image = normal_image
        normal_map = nodes.new("ShaderNodeNormalMap")
        normal_map.location = (160, -160)
        normal_map.inputs["Strength"].default_value = 0.5
        links.new(normal_tex.outputs["Color"], normal_map.inputs["Color"])
        links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])

    if "Metal" in name or "Details" in name:
        shader.inputs["Metallic"].default_value = 0.55
        shader.inputs["Roughness"].default_value = 0.45
    elif "Windows" in name or "Glass" in name:
        shader.inputs["Metallic"].default_value = 0.0
        shader.inputs["Roughness"].default_value = 0.18
    else:
        shader.inputs["Metallic"].default_value = 0.0
        shader.inputs["Roughness"].default_value = 0.8
    return material


_MATERIAL_CACHE = {}


def material_factory(name):
    if name not in _MATERIAL_CACHE:
        _MATERIAL_CACHE[name] = build_textured_material(name)
    return _MATERIAL_CACHE[name]


def add_lights(world_color=(0.32, 0.37, 0.45)):
    world = bpy.data.worlds.new("MWP_World")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (0.28, 0.33, 0.41, 1.0)
    background.inputs["Strength"].default_value = 1.05
    bpy.context.scene.world = world

    sun_data = bpy.data.lights.new("MWP_Sun", type='SUN')
    sun_data.energy = 3.7
    sun_data.angle = math.radians(12.0)
    sun = bpy.data.objects.new("MWP_Sun", sun_data)
    sun.rotation_euler = (math.radians(52.0), 0.0, math.radians(-38.0))
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("MWP_Fill", type='AREA')
    fill_data.energy = 520.0
    fill_data.size = 6.0
    fill = bpy.data.objects.new("MWP_Fill", fill_data)
    fill.location = (-5.0, 4.0, 3.0)
    fill.rotation_euler = (math.radians(60.0), 0.0, math.radians(-140.0))
    bpy.context.scene.collection.objects.link(fill)


def add_ground():
    material = bpy.data.materials.new("MWP_Ground")
    material.use_nodes = True
    shader = material.node_tree.nodes["Principled BSDF"]
    shader.inputs["Base Color"].default_value = (0.30, 0.31, 0.33, 1.0)
    shader.inputs["Roughness"].default_value = 0.95
    mesh = bpy.data.meshes.new("MWP_Ground")
    mesh.from_pydata([(-20.0, -20.0, -0.002), (20.0, -20.0, -0.002),
                      (20.0, 20.0, -0.002), (-20.0, 20.0, -0.002)], [],
                     [(0, 1, 2, 3)])
    mesh.update()
    mesh.materials.append(material)
    obj = bpy.data.objects.new("MWP_Ground", mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def model_bounds(data):
    minimum = [min(vertex[axis] for vertex in data["vertices"]) for axis in range(3)]
    maximum = [max(vertex[axis] for vertex in data["vertices"]) for axis in range(3)]
    return minimum, maximum


def add_camera(data, zoom=1.0, view="iso"):
    minimum, maximum = model_bounds(data)
    centre = [(minimum[axis] + maximum[axis]) * 0.5 for axis in range(3)]
    span = max(maximum[axis] - minimum[axis] for axis in range(3))
    centre_blender = mathutils.Vector(S.export_to_blender(centre))
    # iso from the front (+blender Y = export -Z side), camera side = export +X
    if view == "iso":
        direction = mathutils.Vector((-0.80, 1.0, 0.42)).normalized()
    elif view == "right_front":
        direction = mathutils.Vector((-0.88, 1.0, 0.24)).normalized()
    elif view == "left_front":
        direction = mathutils.Vector((0.88, 1.0, 0.24)).normalized()
    elif view == "front_window":
        direction = mathutils.Vector((0.05, 1.0, 0.13)).normalized()
    elif view == "top":
        direction = mathutils.Vector((-0.35, 0.7, 1.0)).normalized()
    else:
        direction = mathutils.Vector((0.0, 1.0, 0.25)).normalized()
    distance = span * 6.0
    location = centre_blender + direction * distance
    camera_data = bpy.data.cameras.new("MWP_Camera")
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = span * 1.36 * zoom
    camera = bpy.data.objects.new("MWP_Camera", camera_data)
    camera.location = location
    look = (centre_blender - location).normalized()
    camera.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera
    return camera


def render_to(path, size=PREVIEW_SIZE, transparent=False, samples=48):
    scene = bpy.context.scene
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = transparent
    scene.cycles.samples = samples
    scene.render.filepath = path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.render.render(write_still=True)


def downscale_png(source, target, size):
    image = bpy.data.images.load(source, check_existing=False)
    image.scale(size, size)
    image.save_render(target)
    bpy.data.images.remove(image)


# --------------------------------------------------------------------------- #
# render jobs
# --------------------------------------------------------------------------- #
def load_source(spec):
    with open(spec["source"], "r", encoding="utf-8") as handle:
        return json.load(handle)


def prepare_scene(spec, samples=48, ground=True, animate=True):
    data = load_source(spec)
    reset_scene()
    bpy.context.scene.cycles.samples = samples
    add_lights()
    if ground:
        add_ground()
    S.build_model_scene(data, material_factory=material_factory,
                        animate=animate and not spec["construction"])
    return data


#: extra interior views for the Mk III / Mk IV dense-interior pass (T11)
EXTRA_VIEWS = {
    "Advanced": (("left_front", 1.0), ("right_front", 1.0), ("front_window", 1.0)),
    "Top": (("left_front", 1.0), ("right_front", 1.0), ("front_window", 1.0)),
}


def render_model_previews(spec, samples=48, animate=True):
    data = prepare_scene(spec, samples=samples, animate=animate)
    outputs = []
    tier, faction = spec["tier"], spec["faction"]
    stem = "%s.%s" % (tier, faction)
    standard_views = [("iso", 1.0), ("iso50", 2.0)]
    for view, zoom in standard_views:
        camera = add_camera(data, zoom=zoom, view="iso")
        target = os.path.join(C.PREVIEWS_DIR, "%s_%s.png" % (stem, view))
        bpy.context.scene.frame_set(C.ANIM_FRAME_START)
        render_to(target, PREVIEW_SIZE, transparent=False, samples=samples)
        outputs.append(os.path.relpath(target, C.WORKSPACE).replace("\\", "/"))
        bpy.data.objects.remove(camera, do_unlink=True)
    for view, zoom in EXTRA_VIEWS.get(tier, ()):
        if view == "iso":
            continue
        camera = add_camera(data, zoom=zoom, view=view)
        bpy.context.scene.frame_set(C.ANIM_FRAME_START)
        target = os.path.join(C.PREVIEWS_DIR, "%s_%s.png" % (stem, view))
        render_to(target, PREVIEW_SIZE, transparent=False, samples=samples)
        outputs.append(os.path.relpath(target, C.WORKSPACE).replace("\\", "/"))
        bpy.data.objects.remove(camera, do_unlink=True)
    if not animate:
        return outputs
    for frame in ANIMATION_PREVIEW_FRAMES:
        camera = add_camera(data, zoom=1.0, view="iso")
        bpy.context.scene.frame_set(frame)
        target = os.path.join(C.PREVIEWS_DIR, "%s_anim_f%03d.png" % (stem, frame))
        render_to(target, PREVIEW_SIZE, transparent=False, samples=samples)
        outputs.append(os.path.relpath(target, C.WORKSPACE).replace("\\", "/"))
        bpy.data.objects.remove(camera, do_unlink=True)
    return outputs


def render_construction_preview(spec, samples=48):
    data = prepare_scene(spec, samples=samples)
    add_camera(data, zoom=1.0, view="iso")
    target = os.path.join(C.PREVIEWS_DIR,
                          "%s.%s_construction_iso.png" % (spec["tier"], spec["faction"]))
    render_to(target, PREVIEW_SIZE, transparent=False, samples=samples)
    return os.path.relpath(target, C.WORKSPACE).replace("\\", "/")


def render_icon(spec, samples=96, animate=True):
    data = load_source(spec)
    reset_scene()
    scene = bpy.context.scene
    scene.cycles.samples = samples
    world = bpy.data.worlds.new("MWP_IconWorld")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (0.35, 0.38, 0.42, 1.0)
    background.inputs["Strength"].default_value = 1.6
    scene.world = world
    add_lights()
    S.build_model_scene(data, material_factory=material_factory,
                        animate=animate and not spec["construction"])
    scene.frame_set(ANIMATION_PREVIEW_FRAMES[0])
    add_camera(data, zoom=1.12, view="iso")
    icon_stem = C.ICON_STEM[(spec["tier"], spec["faction"])]
    large = os.path.join(C.ICONS_DIR, icon_stem + ".512.png")
    small = os.path.join(C.ICONS_DIR, icon_stem + ".png")
    render_to(large, ICON_RENDER_SIZE, transparent=True, samples=samples)
    downscale_png(large, small, ICON_SIZE)
    os.makedirs(C.ICON_OUT_DIR, exist_ok=True)
    shipped = os.path.join(C.ICON_OUT_DIR, icon_stem + ".png")
    shutil.copyfile(small, shipped)
    return {
        "icon_stem": icon_stem,
        "source_render": os.path.relpath(large, C.WORKSPACE).replace("\\", "/"),
        "art_icon": os.path.relpath(small, C.WORKSPACE).replace("\\", "/"),
        "mod_icon": os.path.relpath(shipped, C.WORKSPACE).replace("\\", "/"),
        "size": [ICON_SIZE, ICON_SIZE],
    }


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    mode = "all"
    only = None
    samples = 48
    static_mode = os.environ.get("MWP_DISABLE_ANIMATIONS", "0") == "1"
    for index, value in enumerate(argv):
        if value == "--mode" and index + 1 < len(argv):
            mode = argv[index + 1]
        elif value == "--only" and index + 1 < len(argv):
            only = argv[index + 1]
        elif value == "--samples" and index + 1 < len(argv):
            samples = int(argv[index + 1])

    specs = C.all_model_specs()
    finished = [spec for spec in specs if not spec["construction"]]
    construction = [spec for spec in specs if spec["construction"]]
    if only:
        finished = [spec for spec in finished if only in (spec["stem"], spec["tier"])]
        construction = [spec for spec in construction
                        if only in (spec["stem"], spec["tier"])]

    manifest = {
        "generated_by": "art/render_previews.py",
        "blender_version": bpy.app.version_string,
        "renderer": "Cycles CPU, offline (not in-game screenshots)",
        "textures": "real vanilla _D albedo + _N normal + _AO occlusion maps "
                    "(paths from material_texture_map.json)",
        "preview_size": PREVIEW_SIZE,
        "icon_size": ICON_SIZE,
        "icon_render_size": ICON_RENDER_SIZE,
        "animation_preview_frames": list(ANIMATION_PREVIEW_FRAMES),
        "previews": [],
        "icons": [],
    }

    manifest["animations_enabled"] = not static_mode
    if mode in ("previews", "all"):
        for spec in finished:
            outputs = render_model_previews(spec, samples=samples,
                                            animate=not static_mode)
            manifest["previews"].extend(outputs)
            print("preview: %s -> %s" % (spec["stem"], outputs))
        for spec in construction:
            output = render_construction_preview(spec, samples=samples)
            manifest["previews"].append(output)
            print("preview: %s -> %s" % (spec["stem"], output))

    if mode in ("icons", "all"):
        for spec in finished:
            record = render_icon(spec, samples=max(samples, 96),
                                 animate=not static_mode)
            manifest["icons"].append(record)
            print("icon: %s -> %s" % (record["icon_stem"], record["art_icon"]))

    if os.path.isfile(MANIFEST_PATH):
        with open(MANIFEST_PATH, "r", encoding="utf-8") as handle:
            previous = json.load(handle)
        previous["previews"] = sorted(set(previous.get("previews", [])
                                          + manifest["previews"]))
        previous["icons"] = manifest["icons"] or previous.get("icons", [])
        previous.setdefault("animation_preview_frames",
                            manifest["animation_preview_frames"])
        previous["preview_size"] = manifest["preview_size"]
        previous["icon_size"] = manifest["icon_size"]
        manifest = previous
    with open(MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    print("render manifest: %s" % MANIFEST_PATH)


if __name__ == "__main__":
    main()
