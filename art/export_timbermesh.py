"""MWP - batch Timbermesh exporter (Blender 4.2.1 LTS, headless).

Reads the deterministic sources produced by ``art/gen_models.py``
(``art/models/MWP.Pump.<Tier>.<Faction>[.ConstructionStage0].Model.json``),
builds the scene with ``art/mwp_scene.py`` (static root mesh + one independent
node per animated part + the three ``#Port#`` marker empties; 0 ``#Slot#``),
exports one ``.timbermesh`` per model through the vendored official plugin
v1.2.0 (MIT) with ``single_animation=True`` / ``use_vertex_animations=False``,
then post-processes the protobuf:

  * stamps ``Model.Name`` = file stem and ``Version`` = 0 (the plugin leaves the
    name empty),
  * makes the exported node-animation quaternion track sign-continuous (the
    plugin writes Blender's canonical ``w >= 0`` quaternions; a 360 deg turn
    would otherwise contain a ``q -> -q`` jump that a slerp-based animator could
    read as a reversed frame).

OUTPUT
  art/timbermesh/MWP.Pump.<Tier>.<Faction>[.ConstructionStage0].Model.timbermesh
  mod/data/Buildings/Water/MWP/  (identical copies; content = art copy)
  art/export-manifest.json       (bytes/tris/verts/materials/bounds/nodes/
                                  animation channels/UV px-per-m/sha256)

Run:
  & 'C:\\Users\\user\\.hdbf\\sdk\\blender-4.2.1-windows-x64\\blender.exe'
      -b --factory-startup -noaudio --python art\\export_timbermesh.py [-- --smoke]
"""

import hashlib
import json
import math
import os
import shutil
import struct
import sys
import traceback
import zlib

import bpy
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
for path in (HERE, os.path.join(HERE, "tools")):
    if path not in sys.path:
        sys.path.insert(0, path)

import mwp_common as C  # noqa: E402
import mwp_scene as S  # noqa: E402

import timbermesh_blender_plugin  # noqa: E402,F401  (appends the plugin dir)
from timbermesh_blender_plugin import timbermesh_exporter  # noqa: E402
import model_pb2  # noqa: E402

BLENDER_VERSION = bpy.app.version_string
#: T10 diagnostic mode: env MWP_DISABLE_ANIMATIONS=1 exports the finished
#: models with the independent part nodes but WITHOUT node animation curves.
DISABLE_ANIMATIONS = os.environ.get("MWP_DISABLE_ANIMATIONS", "0") == "1"
SMOKE_STEM = "MWP.SmokeTest.AnimatedBox.Model"


# --------------------------------------------------------------------------- #
# plugin export + protobuf post processing
# --------------------------------------------------------------------------- #
def export_collection(collection, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    settings = timbermesh_exporter.ExportSettings(
        bpy.context,
        merge_meshes=True,
        single_animation=True,
        use_vertex_animations=False,
    )
    timbermesh_exporter.Exporter.export_collection(collection, path, settings)


def read_model(path):
    model = model_pb2.Model()
    model.ParseFromString(zlib.decompress(open(path, "rb").read()))
    return model


def write_model(model, path):
    with open(path, "wb") as handle:
        handle.write(zlib.compress(model.SerializeToString()))


def quaternion_continuity(model):
    """Negate quaternion frames so consecutive samples never flip sign (q ~ -q)."""
    flips = 0
    for node in model.nodes:
        for animation in node.nodeAnimations:
            previous = None
            for frame in animation.frames:
                current = (frame.rotation.x, frame.rotation.y,
                           frame.rotation.z, frame.rotation.w)
                if previous is not None:
                    dot = sum(current[i] * previous[i] for i in range(4))
                    if dot < 0.0:
                        current = tuple(-value for value in current)
                        frame.rotation.x = current[0]
                        frame.rotation.y = current[1]
                        frame.rotation.z = current[2]
                        frame.rotation.w = current[3]
                        flips += 1
                previous = current
    return flips


def postprocess_model(path, stem):
    model = read_model(path)
    model.version = 0
    model.name = stem
    flips = quaternion_continuity(model)
    write_model(model, path)
    return read_model(path), flips


def _floats(property_message):
    dim = property_message.scalarTypeDimension
    count = len(property_message.data) // (4 * dim)
    return [struct.unpack_from('<%df' % dim, property_message.data, i * 4 * dim)
            for i in range(count)]


# --------------------------------------------------------------------------- #
# statistics + contract checks
# --------------------------------------------------------------------------- #
def _transform(position, rotation, scale, point):
    vector = mathutils.Vector((point[0] * scale[0],
                               point[1] * scale[1],
                               point[2] * scale[2]))
    quaternion = mathutils.Quaternion((rotation[3], rotation[0],
                                       rotation[1], rotation[2]))
    vector = quaternion @ vector
    return (position[0] + vector.x, position[1] + vector.y, position[2] + vector.z)


def animation_summary(node):
    """Summary of one node's nodeAnimations (all channels are stored per frame)."""
    if not node.nodeAnimations:
        return None
    animation = node.nodeAnimations[0]
    frames = list(animation.frames)
    rotations = [(frame.rotation.x, frame.rotation.y, frame.rotation.z,
                  frame.rotation.w) for frame in frames]
    positions = [(frame.position.x, frame.position.y, frame.position.z)
                 for frame in frames]
    scales = [(frame.scale.x, frame.scale.y, frame.scale.z) for frame in frames]
    first_rotation = rotations[0]
    last_rotation = rotations[-1]
    dot = abs(sum(first_rotation[i] * last_rotation[i] for i in range(4)))
    rotation_delta = 0.0
    for rotation in rotations:
        dot_here = sum(rotation[k] * first_rotation[k] for k in range(4))
        if dot_here < 0.0:
            rotation = tuple(-value for value in rotation)
        delta = math.sqrt(sum((rotation[k] - first_rotation[k]) ** 2 for k in range(4)))
        rotation_delta = max(rotation_delta, delta)
    position_delta = max(math.sqrt(sum((p[k] - positions[0][k]) ** 2 for k in range(3)))
                         for p in positions)
    continuity = min(
        (sum(rotations[i][k] * rotations[i + 1][k] for k in range(4))
         for i in range(len(rotations) - 1)),
        default=1.0)
    return {
        "name": animation.name,
        "framerate": round(animation.framerate, 4),
        "frames": len(frames),
        "channels": {
            "rotation": round(rotation_delta, 6) > 1e-4,
            "translation": round(position_delta, 6) > 1e-4,
            "scale": max(max(abs(scales[i][k] - scales[0][k]) for k in range(3))
                         for i in range(len(scales))) > 1e-4,
        },
        "rotation_delta": round(rotation_delta, 6),
        "position_delta": round(position_delta, 6),
        "first_last_rotation_dot": round(dot, 6),
        "first_last_position_delta": round(
            math.sqrt(sum((positions[-1][k] - positions[0][k]) ** 2 for k in range(3))), 6),
        "min_consecutive_quaternion_dot": round(continuity, 6),
    }


def model_stats(model):
    bounds = [[math.inf, math.inf, math.inf], [-math.inf, -math.inf, -math.inf]]
    uv_bounds = {}
    triangles = 0
    materials = []
    nodes = []
    slots = {}
    ports = {}
    nan = False
    animations = {}
    for node in model.nodes:
        nodes.append(node.name)
        if node.name.startswith("#Slot#"):
            slots[node.name] = [node.position.x, node.position.y, node.position.z]
        elif node.name.startswith("#Port#"):
            ports[node.name] = [node.position.x, node.position.y, node.position.z]
        props = {prop.name: prop for prop in node.vertexProperties}
        if "position" in props and node.vertexCount:
            rotation = (node.rotation.x, node.rotation.y, node.rotation.z,
                        node.rotation.w)
            scale = (node.scale.x, node.scale.y, node.scale.z)
            position = (node.position.x, node.position.y, node.position.z)
            for point in _floats(props["position"]):
                world = _transform(position, rotation, scale, point)
                for axis in range(3):
                    bounds[0][axis] = min(bounds[0][axis], world[axis])
                    bounds[1][axis] = max(bounds[1][axis], world[axis])
                    if math.isnan(world[axis]) or math.isinf(world[axis]):
                        nan = True
        for prop in node.vertexProperties:
            dim = max(prop.scalarTypeDimension, 1)
            if len(prop.data) != node.vertexCount * dim * 4:
                continue
            for value in _floats(prop):
                for component in value:
                    if math.isnan(component) or math.isinf(component):
                        nan = True
        if "uv0" in props and node.vertexCount:
            uvs = _floats(props["uv0"])
            record = uv_bounds.setdefault(node.name, [1.0, 1.0, 0.0, 0.0])
            for value in uvs:
                record[0] = min(record[0], value[0])
                record[1] = min(record[1], value[1])
                record[2] = max(record[2], value[0])
                record[3] = max(record[3], value[1])
            uv_bounds[node.name] = [round(v, 6) for v in record]
        summary = animation_summary(node)
        if summary is not None:
            animations[node.name] = summary
        for mesh in node.meshes:
            triangles += len(mesh.indices) // 3
            if mesh.material not in materials:
                materials.append(mesh.material)

    return {
        "bounds_min": [round(v, 5) for v in bounds[0]],
        "bounds_max": [round(v, 5) for v in bounds[1]],
        "bounds_size": [round(bounds[1][i] - bounds[0][i], 5) for i in range(3)],
        "triangles": triangles,
        "vertices": sum(node.vertexCount for node in model.nodes),
        "materials": materials,
        "nodes": nodes,
        "slots": slots,
        "ports": ports,
        "uv_bounds": uv_bounds,
        "animations": animations,
        "animated_nodes": sorted(animations),
        "nan": nan,
        "version": model.version,
        "name": model.name,
    }


def check_contract(spec, stats, data, animations_enabled):
    problems = []
    if stats["name"] != spec["stem"]:
        problems.append("Model.Name %r != %r" % (stats["name"], spec["stem"]))
    if stats["version"] != 0:
        problems.append("Model.Version %r != 0" % (stats["version"],))
    if stats["nan"]:
        problems.append("NaN/Inf in vertex data")
    expected = spec["size"]
    for axis in range(3):
        if abs(stats["bounds_min"][axis]) > C.BOUNDS_TOLERANCE:
            problems.append("bounds min axis %d = %.5f (want 0)"
                            % (axis, stats["bounds_min"][axis]))
        if abs(stats["bounds_max"][axis] - expected[axis]) > C.BOUNDS_TOLERANCE:
            problems.append("bounds max axis %d = %.5f (want %.3f)"
                            % (axis, stats["bounds_max"][axis], expected[axis]))
    if stats["triangles"] > C.TRI_BUDGET_LOD0:
        problems.append("triangles %d > budget %d" % (stats["triangles"], C.TRI_BUDGET_LOD0))
    whitelist = C.MATERIAL_WHITELIST[spec["faction"]]
    for material in stats["materials"]:
        if material not in whitelist:
            problems.append("material %r not in %s whitelist" % (material, spec["faction"]))
    if sorted(stats["materials"]) != sorted(spec["materials"]):
        problems.append("materials %s != planned %s"
                        % (sorted(stats["materials"]), sorted(spec["materials"])))
    if stats["slots"]:
        problems.append("slot marker nodes present: %s" % sorted(stats["slots"]))
    expected_ports = {p["name"]: tuple(p["position"]) for p in spec["ports"]}
    for name, position in expected_ports.items():
        got = stats["ports"].get(name)
        if got is None:
            problems.append("missing empty %s" % name)
            continue
        if max(abs(got[i] - position[i]) for i in range(3)) > C.POSITION_TOLERANCE:
            problems.append("empty %s at %s, want %s" % (name, got, list(position)))
    if len(stats["ports"]) != 3:
        problems.append("port count %d (want 3)" % len(stats["ports"]))
    for node_name, rect in stats["uv_bounds"].items():
        if not (-1e-6 <= rect[0] and -1e-6 <= rect[1]
                and rect[2] <= 1.0 + 1e-6 and rect[3] <= 1.0 + 1e-6):
            problems.append("uv0 out of [0,1] on node %s: %s" % (node_name, rect))

    expected_parts = [part["name"] for part in data.get("parts", [])]
    animated_nodes = stats["animated_nodes"]
    if spec["construction"] or not animations_enabled:
        if not spec["construction"] and expected_parts and not animated_nodes:
            pass  # static diagnostic export: independent nodes, no curves - expected
        if spec["construction"] and expected_parts:
            problems.append("construction model has parts")
        if animated_nodes:
            problems.append("static export has node animations: %s" % animated_nodes)
    else:
        if not expected_parts:
            problems.append("finished model has no animated parts")
        if sorted(animated_nodes) != sorted(expected_parts):
            problems.append("animated nodes %s != expected parts %s"
                            % (sorted(animated_nodes), sorted(expected_parts)))
        for part in data.get("parts", []):
            name = part["name"]
            summary = stats["animations"].get(name)
            if summary is None:
                problems.append("part %s has no node animation" % name)
                continue
            if summary["name"] != "Default":
                problems.append("part %s animation name %r (want 'Default')"
                                % (name, summary["name"]))
            if abs(summary["framerate"] - C.ANIM_FPS) > 0.01:
                problems.append("part %s framerate %s (want %d)"
                                % (name, summary["framerate"], C.ANIM_FPS))
            if summary["frames"] != C.ANIM_FRAMES:
                problems.append("part %s frames %d (want %d)"
                                % (name, summary["frames"], C.ANIM_FRAMES))
            if summary["first_last_rotation_dot"] < 0.9999:
                problems.append("part %s first/last rotation differ (dot=%.6f)"
                                % (name, summary["first_last_rotation_dot"]))
            if summary["first_last_position_delta"] > 1e-3:
                problems.append("part %s first/last position differ (%s)"
                                % (name, summary["first_last_position_delta"]))
            if summary["min_consecutive_quaternion_dot"] < -1e-4:
                problems.append("part %s quaternion track not continuous (dot=%s)"
                                % (name, summary["min_consecutive_quaternion_dot"]))
            animation = part["animation"]
            if animation["type"] == "rotation":
                if not summary["channels"]["rotation"]:
                    problems.append("part %s rotation does not animate" % name)
            else:
                if not summary["channels"]["translation"]:
                    problems.append("part %s translation does not animate" % name)
        root_name = data["stem"]
        if root_name in stats["animations"]:
            problems.append("static root node %r has node animations" % (root_name,))
    expected_node_count = 1 + 3 + len(expected_parts)
    if len(stats["nodes"]) != expected_node_count:
        problems.append("node count %d (want %d = root + 3 ports + %d parts)"
                        % (len(stats["nodes"]), expected_node_count, len(expected_parts)))
    if problems:
        raise RuntimeError("%s: %s" % (spec["stem"], "; ".join(problems)))


# --------------------------------------------------------------------------- #
# one model
# --------------------------------------------------------------------------- #
def load_source(spec):
    path = spec["source"]
    if not os.path.isfile(path):
        raise FileNotFoundError("source json missing (run art/gen_models.py first): %s"
                                % path)
    with open(path, "r", encoding="utf-8") as handle:
        data = json.load(handle)
    if data["stem"] != spec["stem"]:
        raise RuntimeError("source stem %r != spec stem %r" % (data["stem"], spec["stem"]))
    return data


def export_one(spec):
    data = load_source(spec)
    animations_enabled = (not spec["construction"]) and not DISABLE_ANIMATIONS
    S.reset_scene()
    collection = S.build_model_scene(data, animate=animations_enabled)
    target = os.path.join(C.TIMBERMESH_DIR, spec["filename"])
    export_collection(collection, target)
    model, quaternion_flips = postprocess_model(target, spec["stem"])
    stats = model_stats(model)
    stats["stem"] = spec["stem"]
    stats["file"] = os.path.relpath(target, C.WORKSPACE).replace("\\", "/")
    stats["bytes"] = os.path.getsize(target)
    stats["sha256"] = hashlib.sha256(open(target, "rb").read()).hexdigest()
    stats["source"] = os.path.relpath(spec["source"], C.WORKSPACE).replace("\\", "/")
    stats["tier"] = spec["tier"]
    stats["faction"] = spec["faction"]
    stats["construction"] = spec["construction"]
    stats["planned_materials"] = list(spec["materials"])
    stats["quaternion_continuity_flips"] = quaternion_flips
    stats["uv_stats"] = data.get("uv_stats", {})
    stats["animations_enabled"] = animations_enabled
    stats["animation_frames"] = C.ANIM_FRAMES if animations_enabled else 0
    stats["parts"] = [
        {"name": part["name"], "pivot": part["pivot"],
         "animation": part["animation"],
         "triangles": len(part["triangles"])}
        for part in data.get("parts", [])
    ]
    check_contract(spec, stats, data, animations_enabled)
    return stats


def copy_to_mod(stats_list):
    os.makedirs(C.MODEL_OUT_DIR, exist_ok=True)
    copied = []
    for stats in stats_list:
        source = os.path.join(C.TIMBERMESH_DIR, os.path.basename(stats["file"]))
        target = os.path.join(C.MODEL_OUT_DIR, os.path.basename(stats["file"]))
        shutil.copyfile(source, target)
        if hashlib.sha256(open(target, "rb").read()).hexdigest() != stats["sha256"]:
            raise RuntimeError("copy mismatch for %s" % target)
        copied.append(os.path.relpath(target, C.WORKSPACE).replace("\\", "/"))
    return copied


# --------------------------------------------------------------------------- #
# smoke test (pipeline proof: 1x1x1 static box + one rotating animated node)
# --------------------------------------------------------------------------- #
SMOKE_MATERIALS = ["BaseWood_Brown.Folktails", "BaseMetal.Folktails"]


def smoke_spec():
    return {
        "tier": "Basic",
        "faction": "Folktails",
        "construction": False,
        "stem": SMOKE_STEM,
        "filename": SMOKE_STEM + ".timbermesh",
        "source": os.path.join(C.MODELS_DIR, "_smoke", SMOKE_STEM + ".json"),
        "size": [1.0, 1.0, 1.0],
        "materials": SMOKE_MATERIALS,
        "ports": [
            {"name": "#Port#Power", "position": [1.0, 0.5, 0.5]},
            {"name": "#Port#Pipe", "position": [0.5, 0.3, 1.0]},
            {"name": "#Port#Logistics", "position": [0.5, 0.5, 0.0]},
        ],
        "object_name": "SM_MWP_SmokeTest",
    }


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    smoke = "--smoke" in argv
    print("=" * 78)
    print("MWP timbermesh export - Blender %s" % BLENDER_VERSION)
    print("export dir: %s" % C.TIMBERMESH_DIR)
    print("mode: %s" % ("SMOKE TEST (static box + animated node)" if smoke
                        else "full batch (16 models)"))
    print("animations: single_animation=True frames %d..%d @ %d fps (node animations only)"
          % (C.ANIM_FRAME_START, C.ANIM_FRAME_END, C.ANIM_FPS))
    print("=" * 78)

    if smoke:
        spec = smoke_spec()
        data = load_source(spec)
        data["object_name"] = spec["object_name"]
        data["ports"] = spec["ports"]
        S.reset_scene()
        collection = S.build_model_scene(data)
        target = os.path.join(C.TIMBERMESH_DIR, spec["filename"])
        os.makedirs(C.TIMBERMESH_DIR, exist_ok=True)
        export_collection(collection, target)
        model, flips = postprocess_model(target, spec["stem"])
        stats = model_stats(model)
        stats["stem"] = spec["stem"]
        stats["sha256"] = hashlib.sha256(open(target, "rb").read()).hexdigest()
        stats["bytes"] = os.path.getsize(target)
        print("[smoke] bytes=%d tris=%d vtx=%d nodes=%d"
              % (stats["bytes"], stats["triangles"], stats["vertices"], len(stats["nodes"])))
        print("[smoke] bounds min=%s max=%s" % (stats["bounds_min"], stats["bounds_max"]))
        print("[smoke] bounds min=%s max=%s" % (stats["bounds_min"], stats["bounds_max"]))
        print("[smoke] animated nodes=%s" % stats["animated_nodes"])
        for name, summary in stats["animations"].items():
            print("[smoke]   %s: name=%r fps=%s frames=%d channels=%s first/last dot=%.6f"
                  % (name, summary["name"], summary["framerate"], summary["frames"],
                     summary["channels"], summary["first_last_rotation_dot"]))
        print("[smoke] quaternion flips fixed=%d" % flips)
        print("[smoke] materials=%s ports=%s"
              % (stats["materials"], sorted(stats["ports"])))
        print("[smoke] sha256=%s" % stats["sha256"])
        if not stats["animated_nodes"] or set(stats["ports"]) != {
                "#Port#Power", "#Port#Pipe", "#Port#Logistics"}:
            print("SMOKE RESULT: FAIL")
            sys.exit(1)
        print("SMOKE RESULT: PASS")
        print("=" * 78)
        return

    specs = C.all_model_specs()
    results = []
    failures = []
    for index, spec in enumerate(specs, start=1):
        print("\n[%02d/%02d] %s" % (index, len(specs), spec["stem"]))
        try:
            stats = export_one(spec)
        except Exception as error:              # noqa: BLE001 - report and continue
            traceback.print_exc()
            failures.append({"stem": spec["stem"],
                             "error": "%s: %s" % (type(error).__name__, error)})
            print("        FAILED: %s" % error)
            continue
        results.append(stats)
        print("        bytes=%d tris=%d vtx=%d nodes=%d"
              % (stats["bytes"], stats["triangles"], stats["vertices"], len(stats["nodes"])))
        print("        bounds min=%s max=%s" % (stats["bounds_min"], stats["bounds_max"]))
        print("        materials=%s" % stats["materials"])
        print("        parts=%d animated=%s"
              % (len(stats["parts"]), stats["animated_nodes"]))
        print("        sha256=%s" % stats["sha256"][:16])

    copied = copy_to_mod(results) if results else []
    manifest = {
        "generated_by": "art/export_timbermesh.py",
        "art_revision": "MWP-ART-v2",
        "blender_version": BLENDER_VERSION,
        "plugin": {
            "name": "timbermesh_blender_plugin",
            "version": "1.2.0",
            "vendored_at": "art/tools/timbermesh_blender_plugin",
            "license": "MIT",
        },
        "generator": "art/gen_models.py",
        "scene_builder": "art/mwp_scene.py",
        "source_format": "deterministic JSON geometry + animation in art/models/ (export space)",
        "coordinate_system": {
            "model_export_space": "block-corner origin, X=width [0,Size.X], "
                                  "Y=height/up [0,Size.Y], Z=depth [0,Size.Z], "
                                  "front (outlet/pickup) at Z=0, intake at Z=Size.Z",
            "plugin_map": "export = (-blender.x, blender.z, -blender.y); "
                          "scene builder authors blender = (-export.x, -export.z, export.y) "
                          "and the exporter reverses winding once so normals/winding stay outward",
        },
        "sizes": {tier: list(size) for tier, size in C.TIER_SIZE.items()},
        "tri_budget_lod0": C.TRI_BUDGET_LOD0,
        "lods_exported": C.LODS_EXPORTED,
        "animations_enabled": not DISABLE_ANIMATIONS,
        "animation_contract": {
            "kind": "node animations (TimbermeshAnimations.IAnimator); NOT vertex animations",
            "plugin_settings": {"single_animation": True,
                                "use_vertex_animations": False,
                                "merge_meshes": True},
            "fps": C.ANIM_FPS,
            "frame_start": C.ANIM_FRAME_START,
            "frame_end": C.ANIM_FRAME_END,
            "frames_per_clip": C.ANIM_FRAMES,
            "clip_name": "Default",
            "animated_node_prefix": C.PART_PREFIX,
            "game_driver": "blueprint MechanicalNodeAnimatorSpec -> MechanicalNodeAnimator "
                           "(speed = PowerEfficiency * NonlinearAnimationManager.SpeedMultiplier; "
                           "disabled at 0 power) - no C#/blueprint change needed",
            "loop": "first frame == last frame (integer turns / cycles); quaternion track made "
                    "sign-continuous in post-process",
        },
        "uv_policy": {
            "source": "art/material_texture_map.json + art/material_uv_policy.py "
                      "(copied unchanged from HDBF ART-07)",
            "policy": "world-scale planar projection per dominant face normal at the "
                      "material's ART-07 native texel density, capped by the material's "
                      "actual projected span and kept inside its verified canvas "
                      "(2 px guard); window centred on the material's own projected bbox; "
                      "no UV rect is invented",
            "texture_size": C.TEXTURE_SIZE,
            "uv_guard_px": C.UV_GUARD_PX,
        },
        "materials": {spec["stem"]: spec["materials"] for spec in specs},
        "model_count": len(results),
        "models": results,
        "copied_to_mod_data": copied,
        "failures": failures,
    }
    manifest_path = os.path.join(HERE, "export-manifest.json")
    with open(manifest_path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    print("\nmanifest: %s" % manifest_path)
    print("=" * 78)
    if failures:
        print("EXPORT RESULT: FAIL (%d/%d models failed)" % (len(failures), len(specs)))
        for failure in failures:
            print("  - %s: %s" % (failure["stem"], failure["error"]))
        sys.exit(1)
    print("EXPORT RESULT: PASS (%d models, %d bytes, %d animated nodes)"
          % (len(results), sum(r["bytes"] for r in results),
             sum(len(r["animated_nodes"]) for r in results)))
    print("=" * 78)


if __name__ == "__main__":
    main()
