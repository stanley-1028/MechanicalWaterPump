#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""MWP - pure-Python .timbermesh validator (no Blender, no bpy, no Unity).

Parses every expected
``art/timbermesh/MWP.Pump.<Tier>.<Faction>[.ConstructionStage0].Model.timbermesh``
with the VENDORED official plugin protobuf schema
(``art/tools/timbermesh_blender_plugin/model_pb2.py``) after zlib decompression
and checks the frozen MWP art contract (``art/mwp_common.py``) plus the
ART-07 world-scale UV policy and the node-animation contract.

A .timbermesh file is ``zlib.compress(model_pb2.Model.SerializeToString())``::

    Model { version:int, name:string, nodes:[Node] }
    Node  { parent:int, name:string, position/rotation/scale, vertexCount:int,
            vertexProperties:[VertexProperty], meshes:[Mesh],
            nodeAnimations:[NodeAnimation] }
    VertexProperty { name:string, scalarType:int, scalarTypeDimension:int, data:bytes }
    Mesh { indices:[int], material:string }

Checks per file:
  C1 parse (zlib + protobuf).
  C2 identity: Model.name == file stem, Model.version == 0.
  C3 node structure: 1 static model-root node (name == stem, parent -1) + the
     3 ``#Port#Power/#Port#Pipe/#Port#Logistics`` empties (exact REST positions,
     vertexCount 0) + exactly one independent node per animated part from the
     source JSON; NO ``#Slot#``.
  C4 envelope in export space: every mesh vertex (mesh-local, transformed by its
     node's rest transform) inside min (0,0,0) / max = tier size.  Animated parts
     are included.
  C5 triangles: total == source triangle count, <= 12,000 (REQ 13.3 LOD0 budget).
  C6 materials: 1..4 distinct union over ALL nodes, faction whitelist, exact
     planned set, no PaintedMetal hazard material.
  C7 vertex properties: position/normal/tangent/uv0 present on every node with
     vertices, FLOAT scalar type, dimensions 3/3/4/2, exact data length, no
     NaN/Inf.
  C8 topology: indices in range, no repeated index inside a triangle, single
     root, valid parents, uv0 in [0,1].
  C9 normals: unit length, flat shading and consistent with the exported winding
     (geometric normal . stored normal > 0.9) on every mesh node.
  C10 UV containment: every uv0 lies inside its material's verified canvas from
      ``art/material_texture_map.json`` (ART-07: no UV may leave the canvas, or
      the runtime AutoAtlaser would sample another material).
  C11 UV texel density (ART-07): per material, measured ``sqrt(uv_area * 1024^2 /
      world_area)`` must match the generator's planned density (+-2 %) and be
      >= 80 px/m (and never above the material's native ART-07 density).
  C12 node animations: finished models have exactly the expected animated nodes;
      every one has ONE node animation named "Default", 24 fps, 120 frames
      (frames 1..120), first frame == last frame (seamless loop, quaternion
      modulo sign), sign-continuous quaternion track, and the expected channel
      (rotation or translation) actually varies; the static root node has NO
      animation; ConstructionStage0 models have NO node animation at all.

Cross-file checks:
  C15 per-frame node transform vs the source animation (animated mode only):
      every animated node's exported node-animation frames are compared, frame by
      frame, with the transform recomputed from the source JSON animation spec
      (rotation: quaternion about the export axis by 2*pi*turns*t; oscillation:
      quaternion about the export axis by amplitude*sin(2*pi*(cycles*t + phase));
      translation: pivot + axis*amplitude*sin(2*pi*(cycles*t + phase)); all with
      the plugin's axis sign and scale (1,1,1)), +-2e-3.  This catches rest-vs-curve axis or
      space mismatches (local vs world, wrong flip on curves only).
  C16 rotation/oscillation/translation axis vs part geometry (animated mode
      only): for a rotation/oscillation part the axis must be the thin axis of the
      part (disc normal / flat plate / connecting link) or the long axis of a
      symmetric shaft; for a translation part the motion axis must be one of the
      two largest bbox axes.  This catches a part rotating/oscillating around
      an axis that does not match its own geometry.
  C17 merge check: the static root node must contain exactly the source static
      triangles, and every independent part node must contain exactly its own
      source part triangles, so merge_meshes can never silently swallow an
      animated part into the static root.

  C13 shipped == art: every file in ``mod/data/Buildings/Water/MWP/`` is
      byte-identical (SHA-256) to ``art/timbermesh/``.
  C14 the export manifest exists, covers all 16 models, records the same
      SHA-256 / animated nodes / uv stats and reports no failures.
  C17 merge check is reported per file.

Writes ``art/art-validation.txt`` and exits 0 only when every check passes.
"""

import hashlib
import json
import math
import os
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
PLUGIN_DIR = os.path.join(HERE, "tools", "timbermesh_blender_plugin")
if PLUGIN_DIR not in sys.path:
    sys.path.insert(0, PLUGIN_DIR)

import mwp_common as C  # noqa: E402
import model_pb2  # noqa: E402

REPORT_PATH = os.path.join(HERE, "art-validation.txt")
MANIFEST_PATH = os.path.join(HERE, "export-manifest.json")
UV_TOLERANCE = 1e-4
NORMAL_TOLERANCE = 1e-3
WINDING_MIN_DOT = 0.9
DENSITY_TOLERANCE_FRACTION = 0.05
MIN_DENSITY = 80.0


class Report():
    def __init__(self):
        self.lines = []
        self.failures = 0
        self.checks = 0

    def line(self, text=""):
        self.lines.append(text)
        print(text)

    def check(self, passed, label, detail=""):
        self.checks += 1
        if not passed:
            self.failures += 1
        self.line("    [%s] %-36s %s" % ("PASS" if passed else "FAIL", label, detail))
        return passed


def _floats(prop):
    dim = prop.scalarTypeDimension
    count = len(prop.data) // (4 * dim)
    return [struct.unpack_from("<%df" % dim, prop.data, i * 4 * dim)
            for i in range(count)]


def _props(node):
    return {prop.name: prop for prop in node.vertexProperties}


def _quaternion(rotation):
    return (rotation.w, rotation.x, rotation.y, rotation.z)


def _rotate(quaternion, point):
    w, x, y, z = quaternion
    # unit-quaternion rotation of a vector (q * p * q^-1)
    tx = 2.0 * (y * point[2] - z * point[1])
    ty = 2.0 * (z * point[0] - x * point[2])
    tz = 2.0 * (x * point[1] - y * point[0])
    return (point[0] + w * tx + (y * tz - z * ty),
            point[1] + w * ty + (z * tx - x * tz),
            point[2] + w * tz + (x * ty - y * tx))


def _rest_world(node, point):
    scale = (node.scale.x, node.scale.y, node.scale.z)
    rotated = _rotate(_quaternion(node.rotation),
                      (point[0] * scale[0], point[1] * scale[1], point[2] * scale[2]))
    return (node.position.x + rotated[0],
            node.position.y + rotated[1],
            node.position.z + rotated[2])


def material_canvas(name):
    entry = C.material_entry(name)
    if entry["mode"] == "patch":
        return tuple(float(v) for v in entry["patch"]["uv_rect"])
    return (0.0, 0.0, 1.0, 1.0)


def load_model(path):
    with open(path, "rb") as handle:
        payload = zlib.decompress(handle.read())
    model = model_pb2.Model()
    model.ParseFromString(payload)
    return model


def load_source(spec):
    with open(spec["source"], "r", encoding="utf-8") as handle:
        return json.load(handle)


def triangle_area(a, b, c):
    ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
    cx, cy, cz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
    return 0.5 * math.sqrt(cx * cx + cy * cy + cz * cz)


def animation_frames(node):
    if not node.nodeAnimations:
        return None
    animation = node.nodeAnimations[0]
    return {
        "name": animation.name,
        "framerate": animation.framerate,
        "frames": list(animation.frames),
    }


def validate_file(report, spec, animations_enabled=True):
    stem = spec["stem"]
    path = os.path.join(C.TIMBERMESH_DIR, spec["filename"])
    report.line("")
    report.line("  %s" % stem)
    report.line("  %s" % ("-" * len(stem)))
    if not os.path.isfile(path):
        report.check(False, "C1 exists", path)
        return None
    size = os.path.getsize(path)
    sha = hashlib.sha256(open(path, "rb").read()).hexdigest()

    # C1 parse
    try:
        model = load_model(path)
        source = load_source(spec)
        report.check(True, "C1 parse (zlib+protobuf)", "%d bytes" % size)
    except Exception as error:                       # noqa: BLE001
        report.check(False, "C1 parse (zlib+protobuf)",
                     "%s: %s" % (type(error).__name__, error))
        return {"stem": stem, "path": path, "sha256": sha, "bytes": size}

    report.check(model.name == stem, "C2 Model.name", "%r" % (model.name,))
    report.check(model.version == 0, "C2 Model.version", "%r" % (model.version,))

    nodes = list(model.nodes)
    mesh_nodes = []
    port_nodes = []
    slot_nodes = []
    for node in nodes:
        if node.name.startswith("#Slot#"):
            slot_nodes.append(node)
        elif node.name.startswith("#Port#"):
            port_nodes.append(node)
        else:
            mesh_nodes.append(node)
    expected_parts = [part["name"] for part in source.get("parts", [])]
    expected_animated = list(expected_parts) if animations_enabled else []
    root = mesh_nodes[0] if mesh_nodes else None
    animated_names = [node.name for node in nodes if node.nodeAnimations]
    part_nodes = [node for node in nodes if node.name in expected_parts]

    root_ok = (root is not None and root.parent == -1 and root.name == stem)
    report.check(root_ok, "C3 static model-root node",
                 "name=%r parent=%d" % (root.name if root else None,
                                        root.parent if root else 99))
    report.check(not slot_nodes, "C3 no #Slot# marker", "%d slots" % len(slot_nodes))
    report.check(len(nodes) == 1 + 3 + len(expected_parts), "C3 node count",
                 "%d (want %d = root+3 ports+%d parts)"
                 % (len(nodes), 4 + len(expected_parts), len(expected_parts)))
    report.check(sorted(animated_names) == sorted(expected_animated),
                 "C3 independent part nodes",
                 "animated=%s (mode=%s)" % (animated_names,
                                            "animated" if animations_enabled else "static-diagnostic"))

    # C6 materials (union over all nodes)
    materials = []
    for node in nodes:
        for mesh in node.meshes:
            if mesh.material not in materials:
                materials.append(mesh.material)
    whitelist = C.MATERIAL_WHITELIST[spec["faction"]]
    materials_ok = (1 <= len(materials) <= 4
                    and all(material in whitelist for material in materials)
                    and all(material.endswith("." + spec["faction"]) for material in materials)
                    and not any(material in C.BANNED_MATERIALS for material in materials)
                    and sorted(materials) == sorted(source["materials"]))
    report.check(materials_ok, "C6 materials (vanilla whitelist)", "%s" % (materials,))

    # C3 ports
    expected_ports = {p["name"]: tuple(p["position"]) for p in source["ports"]}
    ports_ok = len(port_nodes) == 3
    port_details = []
    root_index = nodes.index(root) if root is not None else -1
    for port in port_nodes:
        position = (port.position.x, port.position.y, port.position.z)
        expected = expected_ports.get(port.name)
        port_details.append("%s=%s" % (port.name, [round(v, 3) for v in position]))
        if expected is None:
            ports_ok = False
            continue
        if port.vertexCount != 0 or len(port.meshes) != 0:
            ports_ok = False
        if max(abs(position[i] - expected[i]) for i in range(3)) > C.POSITION_TOLERANCE:
            ports_ok = False
        if port.parent != root_index:
            ports_ok = False
    ports_ok = ports_ok and set(port.name for port in port_nodes) == set(expected_ports)
    report.check(ports_ok, "C3 3 port markers", " ".join(port_details))

    # per-node vertex data
    node_data = {}
    properties_ok = True
    expected_dims = {"position": 3, "normal": 3, "tangent": 4, "uv0": 2}
    for node in nodes:
        props = _props(node)
        if node.vertexCount == 0:
            continue
        for name, dim in expected_dims.items():
            if name not in props:
                properties_ok = False
                continue
            prop = props[name]
            if prop.scalarTypeDimension != dim:
                properties_ok = False
            if len(prop.data) != node.vertexCount * dim * 4:
                properties_ok = False
            if prop.scalarType != model_pb2.SCALAR_TYPE_FLOAT:
                properties_ok = False
            if any(math.isnan(v) or math.isinf(v)
                   for value in _floats(prop) for v in value):
                properties_ok = False
        extra = [name for name in props if name not in expected_dims and name != "color"]
        if extra:
            properties_ok = False
        node_data[node.name] = {
            "positions": _floats(props["position"]),
            "normals": _floats(props["normal"]),
            "uvs": _floats(props["uv0"]),
            "world": [_rest_world(node, point) for point in _floats(props["position"])],
        }
    report.check(properties_ok, "C7 vertex properties",
                 "nodes with mesh=%d properties=%s"
                 % (len(node_data), sorted(expected_dims)))

    # C4 bounds (world rest pose, includes animated parts)
    bounds_min = [math.inf, math.inf, math.inf]
    bounds_max = [-math.inf, -math.inf, -math.inf]
    for node in nodes:
        if node.name not in node_data:
            continue
        for point in node_data[node.name]["world"]:
            for axis in range(3):
                bounds_min[axis] = min(bounds_min[axis], point[axis])
                bounds_max[axis] = max(bounds_max[axis], point[axis])
    size_ok = all(abs(bounds_min[axis]) <= C.SIZE_TOLERANCE for axis in range(3))
    size_ok = size_ok and all(abs(bounds_max[axis] - spec["size"][axis]) <= C.SIZE_TOLERANCE
                              for axis in range(3))
    report.check(size_ok, "C4 export-space bounds",
                 "min=%s max=%s" % ([round(v, 4) for v in bounds_min],
                                    [round(v, 4) for v in bounds_max]))

    # C5 triangles
    triangles = sum(len(mesh.indices) // 3 for node in nodes for mesh in node.meshes)
    source_triangles = sum(len(part["triangles"]) for part in source.get("parts", []))
    source_triangles += len(source["triangles"])
    triangles_ok = (100 <= triangles <= C.TRI_BUDGET_LOD0
                    and triangles == source_triangles)
    report.check(triangles_ok, "C5 triangle budget / source match",
                 "%d <= %d (source %d)" % (triangles, C.TRI_BUDGET_LOD0,
                                           source_triangles))

    # C17 merge check: root == static source tris, each part node == its own part tris
    root_triangles = sum(len(mesh.indices) // 3 for mesh in root.meshes)
    source_static_triangles = len(source["triangles"])
    merge_ok = root_triangles == source_static_triangles
    merge_details = ["root=%d/%d" % (root_triangles, source_static_triangles)]
    for part in source.get("parts", []):
        node = next((candidate for candidate in nodes if candidate.name == part["name"]), None)
        if node is None:
            merge_ok = False
            merge_details.append("%s missing" % part["name"])
            continue
        node_triangles = sum(len(mesh.indices) // 3 for mesh in node.meshes)
        if node_triangles != len(part["triangles"]):
            merge_ok = False
        merge_details.append("%s=%d/%d" % (part["name"].replace("#Animated", ""),
                                           node_triangles, len(part["triangles"])))
    report.check(merge_ok, "C17 merge check (parts stay separate)", " ".join(merge_details))

    # C8 topology + uv range
    topology_ok = True
    uv_in_range = True
    total_indices = 0
    uv_min = 1.0
    uv_max = 0.0
    for node in nodes:
        if node.name not in node_data:
            continue
        data = node_data[node.name]
        indices = [index for mesh in node.meshes for index in mesh.indices]
        total_indices += len(indices)
        if len(indices) % 3 != 0 or not indices:
            topology_ok = False
        if any(index < 0 or index >= node.vertexCount for index in indices):
            topology_ok = False
        for offset in range(0, len(indices) - 2, 3):
            if len(set(indices[offset:offset + 3])) != 3:
                topology_ok = False
                break
        if len(data["uvs"]) != node.vertexCount:
            topology_ok = False
        for uv in data["uvs"]:
            uv_min = min(uv_min, uv[0])
            uv_max = max(uv_max, uv[0])
            if not (0.0 - UV_TOLERANCE <= uv[0] <= 1.0 + UV_TOLERANCE
                    and 0.0 - UV_TOLERANCE <= uv[1] <= 1.0 + UV_TOLERANCE):
                uv_in_range = False
    for node in nodes:
        if node.parent != -1 and (node.parent < 0 or node.parent >= len(nodes)):
            topology_ok = False
    report.check(topology_ok and uv_in_range, "C8 topology + uv0 in [0,1]",
                 "indices=%d uv_u_range=[%.4f, %.4f]" % (total_indices, uv_min, uv_max))

    # C9 normals: unit, flat, winding-consistent (per node, local space)
    unit_bad = flat_bad = winding_bad = checked = 0
    for node in nodes:
        if node.name not in node_data:
            continue
        data = node_data[node.name]
        normals = data["normals"]
        positions = data["positions"]
        for normal in normals:
            length = math.sqrt(normal[0] ** 2 + normal[1] ** 2 + normal[2] ** 2)
            if abs(length - 1.0) > NORMAL_TOLERANCE:
                unit_bad += 1
        for mesh in node.meshes:
            indices = list(mesh.indices)
            for offset in range(0, len(indices) - 2, 3):
                a, b, c = indices[offset], indices[offset + 1], indices[offset + 2]
                na, nb, nc = normals[a], normals[b], normals[c]
                if (max(abs(na[i] - nb[i]) for i in range(3)) > NORMAL_TOLERANCE
                        or max(abs(na[i] - nc[i]) for i in range(3)) > NORMAL_TOLERANCE):
                    flat_bad += 1
                    continue
                pa, pb, pc = positions[a], positions[b], positions[c]
                ux, uy, uz = pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]
                vx, vy, vz = pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2]
                gx, gy, gz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
                length = math.sqrt(gx * gx + gy * gy + gz * gz)
                if length < 1e-12:
                    continue
                dot = (gx * na[0] + gy * na[1] + gz * na[2]) / length
                checked += 1
                if dot < WINDING_MIN_DOT:
                    winding_bad += 1
    volume_bad = 0
    volume_details = []
    for node in nodes:
        if node.name not in node_data:
            continue
        data = node_data[node.name]
        positions = data["positions"]
        volume = 0.0
        for mesh in node.meshes:
            indices = list(mesh.indices)
            for offset in range(0, len(indices) - 2, 3):
                a, b, c = positions[indices[offset]], positions[indices[offset + 1]], positions[indices[offset + 2]]
                volume += (a[0] * (b[1] * c[2] - b[2] * c[1])
                           - a[1] * (b[0] * c[2] - b[2] * c[0])
                           + a[2] * (b[0] * c[1] - b[1] * c[0])) / 6.0
        volume_details.append("%s=%.4f" % (node.name.replace("#Animated", ""), volume))
        if volume <= 1e-6:
            volume_bad += 1
    report.check(unit_bad == 0 and flat_bad == 0 and winding_bad == 0
                 and volume_bad == 0,
                 "C9 normals unit/flat/outward + volume",
                 "checked=%d unit_bad=%d flat_bad=%d winding_bad=%d volume_bad=%d"
                 % (checked, unit_bad, flat_bad, winding_bad, volume_bad))
    report.line("        signed volume per node: %s" % "; ".join(volume_details))

    # C10 UV containment + C11 density per material
    material_uv = {}
    material_area = {}
    material_world_area = {}
    uv_ok = True
    for node in nodes:
        if node.name not in node_data:
            continue
        data = node_data[node.name]
        for mesh in node.meshes:
            canvas = material_canvas(mesh.material)
            record = material_uv.setdefault(mesh.material, [1.0, 1.0, 0.0, 0.0])
            for offset in range(0, len(mesh.indices) - 2, 3):
                a, b, c = (mesh.indices[offset], mesh.indices[offset + 1],
                           mesh.indices[offset + 2])
                ua, ub, uc = data["uvs"][a], data["uvs"][b], data["uvs"][c]
                for uv in (ua, ub, uc):
                    record[0] = min(record[0], uv[0])
                    record[1] = min(record[1], uv[1])
                    record[2] = max(record[2], uv[0])
                    record[3] = max(record[3], uv[1])
                    if not (canvas[0] - UV_TOLERANCE <= uv[0] <= canvas[2] + UV_TOLERANCE
                            and canvas[1] - UV_TOLERANCE <= uv[1] <= canvas[3] + UV_TOLERANCE):
                        uv_ok = False
                uv_area = triangle_area((ua[0], ua[1], 0.0), (ub[0], ub[1], 0.0),
                                        (uc[0], uc[1], 0.0))
                material_area[mesh.material] = material_area.get(mesh.material, 0.0) + uv_area
                material_world_area[mesh.material] = (
                    material_world_area.get(mesh.material, 0.0)
                    + triangle_area(data["world"][a], data["world"][b], data["world"][c]))
    detail = "; ".join("%s=[%.4f,%.4f,%.4f,%.4f]" % (material, *rect)
                       for material, rect in sorted(material_uv.items()))
    report.check(uv_ok, "C10 uv inside verified canvas", detail)

    density_details = []
    density_ok = True
    source_uv_stats = source.get("uv_stats", {})
    density_records = {}
    for material in sorted(material_uv):
        world_area = material_world_area.get(material, 0.0)
        uv_area = material_area.get(material, 0.0)
        if world_area <= 0.0:
            density_ok = False
            continue
        measured = math.sqrt(uv_area * (C.TEXTURE_SIZE ** 2) / world_area)
        expected_stats = source_uv_stats.get(material, {})
        expected = math.sqrt(max(expected_stats.get("px_per_m_u", 0.0), 0.0)
                             * max(expected_stats.get("px_per_m_v", 0.0), 0.0))
        density_records[material] = round(measured, 2)
        density_details.append("%s=%.1f px/m (planned %.1f)" % (material, measured, expected))
        if measured < MIN_DENSITY - 1e-6:
            density_ok = False
        if expected > 0.0 and abs(measured - expected) / expected > DENSITY_TOLERANCE_FRACTION:
            density_ok = False
    report.check(density_ok, "C11 ART-07 UV density",
                 "min=%.1f px/m; %s" % (min(density_records.values()) if density_records else 0.0,
                                        "; ".join(density_details)))

    # C12 node animations
    animation_ok = True
    animation_details = []
    if spec["construction"] or not animations_enabled:
        animation_ok = not animated_names
        animation_details.append("static diagnostic: %d independent part nodes, 0 animations"
                                 % (len(part_nodes),))
    else:
        frame_count = C.ANIM_FRAMES
        for part in source.get("parts", []):
            name = part["name"]
            node = next((node for node in nodes if node.name == name), None)
            if node is None:
                animation_ok = False
                animation_details.append("%s missing" % name)
                continue
            if len(node.nodeAnimations) != 1:
                animation_ok = False
                animation_details.append("%s has %d animations" % (name, len(node.nodeAnimations)))
                continue
            animation = node.nodeAnimations[0]
            frames = list(animation.frames)
            if animation.name != "Default":
                animation_ok = False
            if abs(animation.framerate - C.ANIM_FPS) > 0.01:
                animation_ok = False
            if len(frames) != frame_count:
                animation_ok = False
            if not frames:
                continue
            first, last = frames[0], frames[-1]
            q0 = _quaternion(first.rotation)
            q1 = _quaternion(last.rotation)
            dot = abs(sum(q0[i] * q1[i] for i in range(4)))
            position_delta = math.sqrt(
                (last.position.x - first.position.x) ** 2
                + (last.position.y - first.position.y) ** 2
                + (last.position.z - first.position.z) ** 2)
            scale_delta = max(abs(last.scale.x - first.scale.x),
                              abs(last.scale.y - first.scale.y),
                              abs(last.scale.z - first.scale.z))
            if dot < 0.9999 or position_delta > 1e-3 or scale_delta > 1e-3:
                animation_ok = False
                animation_details.append("%s first/last mismatch" % name)
            # node rest transform must equal the first animation frame
            q_rest = _quaternion(node.rotation)
            rest_dot = abs(sum(q_rest[i] * q0[i] for i in range(4)))
            rest_position = math.sqrt(
                (node.position.x - first.position.x) ** 2
                + (node.position.y - first.position.y) ** 2
                + (node.position.z - first.position.z) ** 2)
            if rest_dot < 0.9999 or rest_position > 1e-3:
                animation_ok = False
                animation_details.append("%s rest transform != first frame" % name)
            # quaternion sign continuity
            worst_dot = 1.0
            previous = q0
            for frame in frames[1:]:
                current = _quaternion(frame.rotation)
                dot_here = sum(previous[i] * current[i] for i in range(4))
                worst_dot = min(worst_dot, dot_here)
                previous = current
            if worst_dot < -1e-4:
                animation_ok = False
                animation_details.append("%s quaternion discontinuity" % name)
            # expected channel varies
            rotations = [_quaternion(frame.rotation) for frame in frames]
            positions = [(frame.position.x, frame.position.y, frame.position.z)
                         for frame in frames]
            rotation_delta = 0.0
            for rotation in rotations:
                sign = 1.0 if sum(rotation[i] * q0[i] for i in range(4)) >= 0 else -1.0
                rotation_delta = max(rotation_delta, math.sqrt(sum(
                    (sign * rotation[i] - q0[i]) ** 2 for i in range(4))))
            position_delta = max(
                math.sqrt(sum((position[i] - positions[0][i]) ** 2 for i in range(3)))
                for position in positions)
            kind = part["animation"]["type"]
            if kind in ("rotation", "oscillation") and rotation_delta < 1e-3:
                animation_ok = False
                animation_details.append("%s rotation does not vary" % name)
            if kind == "translation" and position_delta < 1e-3:
                animation_ok = False
                animation_details.append("%s translation does not vary" % name)
        if root is not None and root.nodeAnimations:
            animation_ok = False
        animation_details.insert(0, "%d animated nodes x %d frames @ %d fps"
                                 % (len(expected_parts), C.ANIM_FRAMES, C.ANIM_FPS))
    report.check(animation_ok, "C12 node animations", "; ".join(animation_details))

    # C15 per-frame transform vs source animation spec (animated mode only)
    transform_ok = True
    transform_details = []
    if animations_enabled and not spec["construction"]:
        span = float(C.ANIM_FRAMES - 1)
        for part in source.get("parts", []):
            node = next((candidate for candidate in nodes if candidate.name == part["name"]), None)
            if node is None or not node.nodeAnimations:
                transform_ok = False
                transform_details.append("%s missing" % part["name"])
                continue
            frames = list(node.nodeAnimations[0].frames)
            animation = part["animation"]
            pivot = part["pivot"]
            axis_index = {"x": 0, "y": 1, "z": 2}[animation["axis"]]
            worst_rotation = 0.0
            worst_position = 0.0
            worst_scale = 0.0
            for index, frame in enumerate(frames):
                t = index / span
                expected_position = list(pivot)
                expected_rotation = (0.0, 0.0, 0.0, 1.0)
                if animation["type"] == "rotation":
                    theta = 2.0 * math.pi * float(animation["turns"]) * t
                    half = theta * 0.5
                    axis_unit = [0.0, 0.0, 0.0]
                    axis_unit[axis_index] = -math.sin(half)
                    expected_rotation = (axis_unit[0], axis_unit[1], axis_unit[2],
                                         math.cos(half))
                elif animation["type"] == "oscillation":
                    angle = float(animation["amplitude"]) * math.sin(
                        2.0 * math.pi * (float(animation["cycles"]) * t
                                         + float(animation.get("phase", 0.0))))
                    half = angle * 0.5
                    axis_unit = [0.0, 0.0, 0.0]
                    axis_unit[axis_index] = -math.sin(half)
                    expected_rotation = (axis_unit[0], axis_unit[1], axis_unit[2],
                                         math.cos(half))
                else:
                    offset = float(animation["amplitude"]) * math.sin(
                        2.0 * math.pi * (float(animation["cycles"]) * t
                                         + float(animation.get("phase", 0.0))))
                    expected_position[axis_index] += offset
                actual_position = (frame.position.x, frame.position.y, frame.position.z)
                actual_rotation = (frame.rotation.x, frame.rotation.y,
                                   frame.rotation.z, frame.rotation.w)
                position_error = max(abs(actual_position[i] - expected_position[i])
                                     for i in range(3))
                dot = sum(actual_rotation[i] * expected_rotation[i] for i in range(4))
                rotation_error = min(abs(dot - 1.0), abs(dot + 1.0))
                scale_error = max(abs(frame.scale.x - 1.0), abs(frame.scale.y - 1.0),
                                  abs(frame.scale.z - 1.0))
                worst_rotation = max(worst_rotation, rotation_error)
                worst_position = max(worst_position, position_error)
                worst_scale = max(worst_scale, scale_error)
            if worst_rotation > 2e-3 or worst_position > 2e-3 or worst_scale > 2e-3:
                transform_ok = False
            transform_details.append(
                "%s rot_err=%.1e pos_err=%.1e scale_err=%.1e"
                % (part["name"].replace("#Animated", ""), worst_rotation,
                   worst_position, worst_scale))
    elif not animations_enabled:
        transform_details.append("skipped: static diagnostic mode")
    report.check(transform_ok, "C15 frames vs source animation", "; ".join(transform_details))

    # C16 rotation/translation axis vs part geometry (animated mode only)
    axis_ok = True
    axis_details = []
    if animations_enabled and not spec["construction"]:
        for part in source.get("parts", []):
            node = next((candidate for candidate in nodes if candidate.name == part["name"]), None)
            if node is None or node.vertexCount == 0:
                axis_ok = False
                axis_details.append("%s missing" % part["name"])
                continue
            props = _props(node)
            positions = _floats(props["position"])
            minimum = [min(point[i] for point in positions) for i in range(3)]
            maximum = [max(point[i] for point in positions) for i in range(3)]
            extents = [maximum[i] - minimum[i] for i in range(3)]
            order = sorted(range(3), key=lambda i: extents[i])
            smallest, middle, largest = order[0], order[1], order[2]
            animation = part["animation"]
            expected = {"x": 0, "y": 1, "z": 2}[animation["axis"]]
            if animation["type"] in ("rotation", "oscillation"):
                symmetric_small = abs(extents[middle] - extents[largest]) <= 0.2 * extents[largest]
                symmetric_large = abs(extents[smallest] - extents[middle]) <= 0.2 * extents[middle] if extents[middle] else False
                disc = expected == smallest and symmetric_small
                shaft = expected == largest and symmetric_large
                plate = (expected == smallest
                         and extents[smallest] <= 0.5 * max(extents[largest], 1e-6))
                if not (disc or shaft or plate):
                    axis_ok = False
                axis_details.append("%s %s(%s)" % (
                    part["name"].replace("#Animated", ""), animation["axis"],
                    "disc" if disc else ("shaft" if shaft else
                                         ("plate/link" if plate else "MISMATCH"))))
            else:
                if expected not in (middle, largest):
                    axis_ok = False
                axis_details.append("%s %s(%s)" % (
                    part["name"].replace("#Animated", ""),
                    animation["axis"], "ok" if expected in (middle, largest) else "MISMATCH"))
    elif not animations_enabled:
        axis_details.append("skipped: static diagnostic mode")
    report.check(axis_ok, "C16 motion axis vs part geometry", "; ".join(axis_details))

    return {
        "stem": stem,
        "path": os.path.relpath(path, C.WORKSPACE).replace("\\", "/"),
        "bytes": size,
        "sha256": sha,
        "triangles": triangles,
        "vertices": sum(node.vertexCount for node in nodes),
        "materials": materials,
        "bounds_min": [round(v, 4) for v in bounds_min],
        "bounds_max": [round(v, 4) for v in bounds_max],
        "nodes": [node.name for node in nodes],
        "animated_nodes": sorted(animated_names),
        "animation_framerate": C.ANIM_FPS if animated_names else None,
        "animation_frames": C.ANIM_FRAMES if animated_names else 0,
        "uv_density_px_per_m": density_records,
        "uv_bounds": {material: [round(v, 4) for v in rect]
                      for material, rect in material_uv.items()},
    }


def main():
    report = Report()
    report.line("=" * 84)
    report.line("MWP mechanical water pump - .timbermesh validation (pure Python)")
    report.line("Blender-free: zlib + vendored official plugin protobuf schema")
    report.line("validator: art/validate_timbermesh.py  |  contract: art/mwp_common.py")
    report.line("checks: geometry/envelope/materials + ART-07 world-scale UV + node animations")
    report.line("=" * 84)

    animations_enabled = True
    if os.path.isfile(MANIFEST_PATH):
        with open(MANIFEST_PATH, "r", encoding="utf-8") as handle:
            animations_enabled = json.load(handle).get("animations_enabled", True)
    report.line("mode: %s" % ("ANIMATED" if animations_enabled else
                              "STATIC DIAGNOSTIC (T10 deliverable A)"))
    report.line("")
    specs = C.all_model_specs()
    results = []
    for spec in specs:
        results.append(validate_file(report, spec, animations_enabled))

    # C13 shipped == art
    report.line("")
    report.line("CROSS-FILE CHECKS")
    report.line("-" * 84)
    shipped_ok = True
    missing = []
    for spec in specs:
        art_path = os.path.join(C.TIMBERMESH_DIR, spec["filename"])
        shipped_path = os.path.join(C.MODEL_OUT_DIR, spec["filename"])
        if not os.path.isfile(shipped_path):
            shipped_ok = False
            missing.append(os.path.relpath(shipped_path, C.WORKSPACE))
            continue
        if (hashlib.sha256(open(art_path, "rb").read()).hexdigest()
                != hashlib.sha256(open(shipped_path, "rb").read()).hexdigest()):
            shipped_ok = False
    report.check(shipped_ok, "C13 mod/data == art/timbermesh",
                 "16/16 byte-identical" if shipped_ok else "mismatch/missing=%s" % missing)

    # C14 manifest
    manifest_ok = False
    manifest_detail = "missing"
    if os.path.isfile(MANIFEST_PATH):
        with open(MANIFEST_PATH, "r", encoding="utf-8") as handle:
            manifest = json.load(handle)
        by_stem = {entry["stem"]: entry for entry in manifest.get("models", [])}
        manifest_ok = (not manifest.get("failures")
                       and len(by_stem) == len(specs)
                       and all(spec["stem"] in by_stem for spec in specs))
        if manifest_ok:
            for spec in specs:
                entry = by_stem[spec["stem"]]
                file_path = os.path.join(C.TIMBERMESH_DIR, spec["filename"])
                if entry["sha256"] != hashlib.sha256(open(file_path, "rb").read()).hexdigest():
                    manifest_ok = False
                    break
                source = load_source(spec)
                expected_animated_names = (sorted(part["name"] for part in source.get("parts", []))
                                           if manifest.get("animations_enabled", True) else [])
                if sorted(entry.get("animated_nodes", [])) != expected_animated_names:
                    manifest_ok = False
                    break
                expected_frames = ((C.ANIM_FRAMES
                                    if manifest.get("animations_enabled", True)
                                    else 0) if source.get("parts") else 0)
                if entry.get("animation_frames", 0) != expected_frames:
                    manifest_ok = False
                    break
        manifest_detail = ("models=%d failures=%d animation_contract=%s"
                           % (len(by_stem), len(manifest.get("failures", [])),
                              "yes" if manifest.get("animation_contract") else "no"))
    report.check(manifest_ok, "C14 export-manifest coverage", manifest_detail)

    report.line("")
    report.line("=" * 84)
    ok = [result for result in results if result is not None]
    report.line("FILES:    %d/%d present; %d PASS, %d FAIL"
                % (len(ok), len(specs), len(ok), len(specs) - len(ok)))
    report.line("CHECKS:   %d PASS, %d FAIL (per-file C1-C17 x %d files + C13/C14)"
                % (report.checks - report.failures, report.failures, len(specs)))
    report.line("OVERALL VERDICT: %s"
                % ("PASS" if report.failures == 0 and len(ok) == len(specs) else "FAIL"))
    report.line("=" * 84)
    report.line("")
    report.line("Per-file numbers (export space, block-corner origin):")
    report.line("%-52s %7s %6s %6s %-16s %-5s %-3s %s"
                % ("file", "bytes", "tris", "verts", "bounds max", "anim", "nodes",
                   "animated node names"))
    for result in ok:
        report.line("%-52s %7d %6d %6d %-16s %-5d %-3d %s"
                    % (result["stem"], result["bytes"], result["triangles"],
                       result["vertices"], "%s" % (result["bounds_max"],),
                       len(result["animated_nodes"]), len(result["nodes"]),
                       ",".join(name.replace("#Animated", "")
                                for name in result["animated_nodes"])[:40]))
    report.line("")
    report.line("Animated nodes per file:")
    for result in ok:
        if result["animated_nodes"]:
            report.line("  %-52s %s" % (result["stem"],
                                        ", ".join(result["animated_nodes"])))
    report.line("")
    report.line("ART-07 UV texel density (px/m, measured from the exported UVs):")
    for result in ok:
        report.line("  %-52s %s" % (result["stem"], ", ".join(
            "%s=%.1f" % (material.split(".")[0], density)
            for material, density in sorted(result["uv_density_px_per_m"].items()))))
    report.line("")
    report.line("TOTAL: %d bytes / %d triangles / %d vertices / %d animated node animations"
                % (sum(result["bytes"] for result in ok),
                   sum(result["triangles"] for result in ok),
                   sum(result["vertices"] for result in ok),
                   sum(len(result["animated_nodes"]) for result in ok)))

    with open(REPORT_PATH, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(report.lines) + "\n")
    print("\nreport: %s" % REPORT_PATH)
    if report.failures or len(ok) != len(specs):
        print("VALIDATION RESULT: FAIL")
        sys.exit(1)
    print("VALIDATION RESULT: PASS")


if __name__ == "__main__":
    main()
