"""MWP - shared Blender scene builder (Blender 4.2.1, headless or interactive).

Single place that turns the deterministic JSON sources produced by
``art/gen_models.py`` into a Blender scene:

  * the static root mesh (merged into the model-root node),
  * one independent child object per ANIMATED part (name ``#Animated...`` so the
    official plugin emits it as its own node with ``nodeAnimations`` and never
    merges it into the static mesh),
  * the three ``#Port#`` marker empties (no ``#Slot#``),
  * linear per-frame node animation (rotation / translation) for every part.

Both ``art/export_timbermesh.py`` and ``art/render_previews.py`` use this module,
so the exported file and the offline previews are guaranteed to be the same
geometry and the same animation.

Coordinate mapping (same as the exporter, see mwp_common)::

    blender = (-export.x, -export.z, export.y)        (proper rotation, det +1)

The official plugin then maps ``export = (-blender.x, blender.z, -blender.y)``
and reverses triangle winding for that reflection; ``build_model_scene`` keeps
the authored winding, and the exporter performs the single winding flip.
"""

import math

import bmesh
import bpy

import mwp_common as C


# --------------------------------------------------------------------------- #
# scene / coordinate helpers
# --------------------------------------------------------------------------- #
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup_scene():
    scene = bpy.context.scene
    scene.render.fps = C.ANIM_FPS
    scene.render.fps_base = 1.0
    scene.frame_start = C.ANIM_FRAME_START
    scene.frame_end = C.ANIM_FRAME_END
    scene.frame_set(C.ANIM_FRAME_START)
    return scene


def export_to_blender(point):
    x, y, z = point
    return (-x, -z, y)


#: export-space animation axis -> (Blender euler/location index, sign).
#: export X -> Blender -X, export Y (up) -> Blender +Z, export Z -> Blender -Y.
ROTATION_AXIS = {"x": (0, -1.0), "y": (2, 1.0), "z": (1, -1.0)}
TRANSLATION_AXIS = {"x": (0, -1.0), "y": (2, 1.0), "z": (1, -1.0)}


def default_material_factory(name):
    """Plain material that only carries the exact vanilla material asset name."""
    material = bpy.data.materials.get(name)
    if material is None:
        material = bpy.data.materials.new(name=name)
    material.use_nodes = False
    return material


# --------------------------------------------------------------------------- #
# mesh construction
# --------------------------------------------------------------------------- #
def _fill_mesh(mesh, vertices, triangles, triangle_materials, triangle_uvs,
               materials, material_factory):
    mesh.from_pydata(vertices, [], [tuple(triangle) for triangle in triangles])
    mesh.update()
    if len(mesh.polygons) != len(triangles):
        raise RuntimeError("polygon count %d != triangle count %d"
                           % (len(mesh.polygons), len(triangles)))
    material_index = {}
    for name in materials:
        material_index[name] = len(mesh.materials)
        mesh.materials.append(material_factory(name))
    uv_layer = mesh.uv_layers.new(name="UVMap")
    uv_data = uv_layer.data
    for polygon_index, polygon in enumerate(mesh.polygons):
        polygon.use_smooth = False
        polygon.material_index = material_index[
            materials[triangle_materials[polygon_index]]]
        uvs = triangle_uvs[polygon_index]
        for corner in range(3):
            loop_index = polygon.loop_indices[corner]
            uv_data[loop_index].uv = (uvs[corner * 2], uvs[corner * 2 + 1])
    # The official plugin maps blender -> export through a reflection
    # (det = -1) and reverses the triangle loops while exporting, so the
    # authored winding must be reversed exactly ONCE here.  T6 did this in the
    # old exporter; the T9 refactor into this module dropped it, which shipped
    # inside-out meshes (negative signed volume): the game's single-sided
    # shader then showed the inside of the closed frame and made the thin gear
    # / piston plates look "crumpled" in game while double-sided Cycles
    # previews looked fine.  Keep this single flip - it is the only one.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def build_static_object(data, material_factory, name=None):
    name = name or data.get("object_name") or ("SM_" + data["stem"])
    mesh = bpy.data.meshes.new(name)
    vertices = [export_to_blender(vertex) for vertex in data["vertices"]]
    _fill_mesh(mesh, vertices, data["triangles"], data["triangle_materials"],
               data["triangle_uvs"], data["materials"], material_factory)
    obj = bpy.data.objects.new(name, mesh)
    obj.location = (0.0, 0.0, 0.0)
    return obj


def build_part_object(part, material_factory):
    mesh = bpy.data.meshes.new(part["name"])
    vertices = [export_to_blender(vertex) for vertex in part["vertices"]]
    _fill_mesh(mesh, vertices, part["triangles"], part["triangle_materials"],
               part["triangle_uvs"], part["materials"], material_factory)
    obj = bpy.data.objects.new(part["name"], mesh)
    obj.location = export_to_blender(part["pivot"])
    obj.rotation_mode = 'XYZ'
    return obj


# --------------------------------------------------------------------------- #
# node animation (linear per-frame keys; plugin samples every integer frame)
# --------------------------------------------------------------------------- #
def apply_animation(obj, animation):
    """Keyframe the animated part object for frames ANIM_FRAME_START..END.

    ``rotation``: ``angle(t) = 2*pi*turns*t`` around the export axis.
    ``translation``: ``offset(t) = amplitude*sin(2*pi*(cycles*t + phase))``.
    ``t`` runs 0..1 over the 120 exported frames; integer ``turns``/``cycles``
    make the first and last frame equal (seamless loop, as in the official
    ShantySpeaker sample).
    """
    kind = animation["type"]
    axis = animation["axis"]
    if kind == "rotation":
        index, sign = ROTATION_AXIS[axis]
        total = 2.0 * math.pi * float(animation["turns"])
    elif kind == "translation":
        index, sign = TRANSLATION_AXIS[axis]
        amplitude = float(animation["amplitude"])
        cycles = float(animation["cycles"])
        phase = float(animation.get("phase", 0.0))
    else:                                          # pragma: no cover - guarded upstream
        raise ValueError("unknown animation type %r" % (kind,))

    base_location = obj.location.copy()
    obj.animation_data_create()
    action = bpy.data.actions.new("MWPAnim_" + obj.name)
    obj.animation_data.action = action
    span = float(C.ANIM_FRAME_END - C.ANIM_FRAME_START)
    for frame in range(C.ANIM_FRAME_START, C.ANIM_FRAME_END + 1):
        t = (frame - C.ANIM_FRAME_START) / span
        if kind == "rotation":
            euler = [0.0, 0.0, 0.0]
            euler[index] = sign * total * t
            obj.rotation_euler = euler
            obj.keyframe_insert("rotation_euler", frame=frame)
        else:
            offset = amplitude * math.sin(2.0 * math.pi * (cycles * t + phase))
            location = base_location.copy()
            location[index] += sign * offset
            obj.location = location
            obj.keyframe_insert("location", frame=frame)
    for fcurve in action.fcurves:
        for key in fcurve.keyframe_points:
            key.interpolation = 'LINEAR'
    # restore the rest pose (frame 1) after authoring
    bpy.context.scene.frame_set(C.ANIM_FRAME_START)
    return action


# --------------------------------------------------------------------------- #
# full model scene
# --------------------------------------------------------------------------- #
def build_model_scene(data, material_factory=None, animate=True, collection=None):
    """Create the collection (static mesh + animated part nodes + port empties).

    ``animate=False`` builds every part at its rest pose without keyframes (used
    for ConstructionStage0 and for static previews).
    """
    if material_factory is None:
        material_factory = default_material_factory
    setup_scene()
    if collection is None:
        collection = bpy.data.collections.new(data["stem"])
        bpy.context.scene.collection.children.link(collection)

    static = build_static_object(data, material_factory)
    collection.objects.link(static)

    for part in data.get("parts", []):
        obj = build_part_object(part, material_factory)
        collection.objects.link(obj)
        if animate and part.get("animation"):
            apply_animation(obj, part["animation"])
        else:
            obj.location = export_to_blender(part["pivot"])

    for port in data.get("ports", []):
        empty = bpy.data.objects.new(port["name"], None)
        empty.empty_display_type = 'PLAIN_AXES'
        empty.empty_display_size = 0.3
        empty.location = export_to_blender(port["position"])
        collection.objects.link(empty)

    bpy.context.scene.frame_set(C.ANIM_FRAME_START)
    return collection


def collection_objects(collection):
    return list(collection.all_objects)
