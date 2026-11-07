"""MWP - deterministic procedural model generator (pure Python, no bpy).

Builds the eight finished pump-station models (4 tiers x 2 factions) and the
eight ConstructionStage0 greyboxes in game EXPORT space (block-corner origin,
X = width, Y = height/up, Z = depth, front/outlet at Z = 0, intake at Z = depth)
and writes them as plain JSON geometry under ``art/models/``.

Why JSON instead of FBX: the geometry is generated deterministically from code
(no DCC round-trip, no timestamps/UIDs), so re-running produces byte-identical
sources, and the Blender exporter (``art/export_timbermesh.py``) only has to
turn arrays into a mesh.  There is no animation, no ``#Slot#`` node and no text
in any model.

Every vertex UV is produced by ``mwp_common.uv_for`` (world-scale planar
projection onto the verified vanilla-material canvas from
``art/material_texture_map.json``), and every triangle stores the vanilla
material name.  Nothing here is hand-authored in a texture atlas: the same
generator supplies the geometry that the renderers use for previews/icons.

Run:
    python art/gen_models.py            # all 16 models + art/models/gen-manifest.json
    python art/gen_models.py --smoke    # 1x1x1 pipeline smoke-test box only
"""

import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import mwp_common as C  # noqa: E402

TAU = math.pi * 2.0


# --------------------------------------------------------------------------- #
# material roles per model (only verified vanilla names, see mwp_common)
# --------------------------------------------------------------------------- #
ROLE_MAP = {
    # T10: Mk I / Mk II are ALL-WOOD constructions.  The "metal" role (barrels,
    # gears, pipes, shaft, hoops) is mapped to a contrasting WOOD tone and the
    # "detail" role (belt) to the plank material; the "glass"/"plaster" roles are
    # intentionally absent so every glass window / plaster panel / gauge is
    # skipped by has_role().  Whitelist: only BaseWood_* and IrregularPlanks_*.
    ("Basic", "Folktails", False):
        {"wood": "BaseWood_Brown.Folktails",
         "metal": "BaseWood_LightBrown.Folktails",
         "detail": "IrregularPlanks_Brown.Folktails"},
    ("Basic", "IronTeeth", False):
        {"wood": "BaseWood_DarkBrown.IronTeeth",
         "metal": "BaseWood_Grey.IronTeeth",
         "detail": "IrregularPlanks_MetalForged.IronTeeth"},
    ("Intermediate", "Folktails", False):
        {"wood": "BaseWood_LightBrown.Folktails",
         "metal": "BaseWood_Brown.Folktails",
         "detail": "IrregularPlanks_Brown.Folktails"},
    ("Intermediate", "IronTeeth", False):
        {"wood": "BaseWood_Grey.IronTeeth",
         "metal": "BaseWood_DarkBrown.IronTeeth",
         "detail": "IrregularPlanks_MetalForged.IronTeeth"},
    ("Advanced", "Folktails", False):
        {"wood": "BaseWood_LightBrown.Folktails", "metal": "BaseMetal.Folktails",
         "glass": "WindowsAtlas.Folktails", "detail": "Details.Folktails"},
    ("Advanced", "IronTeeth", False):
        {"metal": "BaseMetal.IronTeeth", "wood": "BaseWood_Grey.IronTeeth",
         "planks": "IrregularPlanks_MetalForged.IronTeeth",
         "glass": "WindowsAtlas.IronTeeth"},
    ("Top", "Folktails", False):
        {"planks": "IrregularPlanks_Brown.Folktails", "metal": "BaseMetal.Folktails",
         "glass": "WindowsAtlas.Folktails", "detail": "Details.Folktails"},
    ("Top", "IronTeeth", False):
        {"metal": "BaseMetal.IronTeeth",
         "planks": "IrregularPlanks_MetalForged.IronTeeth",
         "detail": "Details.IronTeeth", "glass": "WindowsAtlas.IronTeeth"},
}
#: the construction greybox uses the same two vanilla materials for every tier.
for _tier in C.TIERS:
    ROLE_MAP[(_tier, "Folktails", True)] = {
        "wood": "BaseWood_Brown.Folktails", "metal": "BaseMetal.Folktails"}
    ROLE_MAP[(_tier, "IronTeeth", True)] = {
        "wood": "BaseWood_DarkBrown.IronTeeth", "metal": "BaseMetal.IronTeeth"}

ROLE_FALLBACK = {
    "wood": ("wood", "planks", "metal"),
    "planks": ("planks", "wood", "metal"),
    "metal": ("metal", "detail", "wood"),
    "detail": ("detail", "metal", "wood"),
    "glass": ("glass",),
    "plaster": ("plaster", "wood", "metal"),
}


def roles_for(tier, faction, construction):
    if construction:
        return dict(ROLE_MAP[(tier, faction, True)])
    return dict(ROLE_MAP[(tier, faction, False)])


# --------------------------------------------------------------------------- #
# builder: triangle soup with per-corner UVs, deduplicated positions
# --------------------------------------------------------------------------- #
class ModelBuilder():
    def __init__(self, size, roles, name):
        self.width, self.height, self.depth = [float(v) for v in size]
        self.size = (self.width, self.height, self.depth)
        self.roles = roles
        self.name = name
        self._static = {
            "vertices": [],
            "triangles": [],
            "triangle_materials": [],
            "triangle_uvs": [],
            "world_points": [],
            "materials": [],           # slot order
            "_material_index": {},
            "_vertex_index": {},
        }
        # convenience aliases for the static (non-animated) mesh
        self.vertices = self._static["vertices"]
        self.triangles = self._static["triangles"]
        self.triangle_materials = self._static["triangle_materials"]
        self.triangle_uvs = self._static["triangle_uvs"]
        self.world_points = self._static["world_points"]
        self.materials = self._static["materials"]
        self._material_index = self._static["_material_index"]
        self._vertex_index = self._static["_vertex_index"]
        self.parts = []
        self._part = None

    # -- materials ---------------------------------------------------------
    def material(self, role="wood"):
        for candidate in ROLE_FALLBACK.get(role, (role,)):
            if candidate in self.roles:
                return self.roles[candidate]
        raise KeyError("no material for role %r in %s" % (role, self.roles))

    def has_role(self, role):
        return any(candidate in self.roles for candidate in ROLE_FALLBACK.get(role, (role,)))

    def _slot(self, name):
        if name not in self._material_index:
            self._material_index[name] = len(self.materials)
            self.materials.append(name)
        return self._material_index[name]

    # -- animated parts ----------------------------------------------------
    def begin_part(self, name, pivot, animation):
        """Start an animated sub-mesh whose own node transform is animated.

        Geometry added between ``begin_part`` / ``end_part`` is authored in
        absolute export coordinates; the builder stores it relative to ``pivot``
        (the rotation / translation centre) so the exported node can rotate or
        translate around that pivot.  ``animation`` is one of::

            {"type": "rotation", "axis": "x"|"y"|"z", "turns": float}
            {"type": "translation", "axis": "x"|"y"|"z", "amplitude": float,
             "cycles": float, "phase": float}
        """
        if self._part is not None:
            raise RuntimeError("nested parts are not supported")
        if not name.startswith(C.PART_PREFIX):
            raise ValueError("part name must start with %r (got %r)"
                             % (C.PART_PREFIX, name))
        for prefix in C.MARKER_PREFIXES:
            if name.startswith(prefix):
                raise ValueError("part name %r collides with marker prefix %r"
                                 % (name, prefix))
        if not animation or animation.get("type") not in ("rotation", "translation"):
            raise ValueError("part %r needs a rotation/translation animation spec"
                             % (name,))
        if name in {part["name"] for part in self.parts}:
            raise RuntimeError("duplicate part name %r" % (name,))
        part = {
            "name": name,
            "pivot": [round(float(value), 6) for value in pivot],
            "animation": dict(animation),
            "vertices": [],
            "triangles": [],
            "triangle_materials": [],
            "triangle_uvs": [],
            "world_points": [],
            "materials": [],
            "_material_index": {},
            "_vertex_index": {},
        }
        self._part = part
        return part

    def end_part(self):
        part = self._part
        if part is None:
            raise RuntimeError("end_part() without begin_part()")
        if not part["triangles"]:
            raise RuntimeError("part %r has no geometry" % (part["name"],))
        self._part = None
        self.parts.append(part)
        return part

    # -- raw triangle ------------------------------------------------------
    def _owner(self):
        return self._part if self._part is not None else self._static

    def _vertex(self, point, material_name):
        owner = self._owner()
        key = (round(point[0], 6), round(point[1], 6), round(point[2], 6))
        index = owner["_vertex_index"].get(key)
        if index is None:
            index = len(owner["vertices"])
            owner["_vertex_index"][key] = index
            owner["vertices"].append([key[0], key[1], key[2]])
        return index

    def _slot_for(self, material_name):
        owner = self._owner()
        if material_name not in owner["_material_index"]:
            owner["_material_index"][material_name] = len(owner["materials"])
            owner["materials"].append(material_name)
        return owner["_material_index"][material_name]

    def add_triangle(self, p0, p1, p2, normal, material_name):
        # orientation: make the winding agree with the intended outward normal
        ux, uy, uz = p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]
        vx, vy, vz = p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]
        gx, gy, gz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
        if gx * normal[0] + gy * normal[1] + gz * normal[2] < 0.0:
            p1, p2 = p2, p1
            gx, gy, gz = -gx, -gy, -gz
        length = math.sqrt(gx * gx + gy * gy + gz * gz)
        if length < 1e-12:
            return
        owner = self._owner()
        owner["triangles"].append([self._vertex(p0, material_name),
                                   self._vertex(p1, material_name),
                                   self._vertex(p2, material_name)])
        owner["triangle_materials"].append(self._slot_for(material_name))
        owner["world_points"].append((p0, p1, p2, normal, material_name))

    def add_quad(self, p0, p1, p2, p3, normal, material_name):
        self.add_triangle(p0, p1, p2, normal, material_name)
        self.add_triangle(p0, p2, p3, normal, material_name)

    # -- primitives --------------------------------------------------------
    def add_box(self, low, high, role="wood"):
        material_name = self.material(role)
        x0, y0, z0 = low
        x1, y1, z1 = high
        if x1 < x0:
            x0, x1 = x1, x0
        if y1 < y0:
            y0, y1 = y1, y0
        if z1 < z0:
            z0, z1 = z1, z0
        corners = [
            (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
        ]
        self.add_hexa(corners, material_name)

    def add_hexa(self, corners, material_name):
        """Convex hexahedron: corners[0..3] bottom ring, [4..7] top ring (same order)."""
        cx = sum(p[0] for p in corners) / 8.0
        cy = sum(p[1] for p in corners) / 8.0
        cz = sum(p[2] for p in corners) / 8.0
        faces = (
            (0, 3, 2, 1),   # bottom (y lo)
            (4, 5, 6, 7),   # top (y hi)
            (0, 1, 5, 4),   # z lo
            (1, 2, 6, 5),   # x hi
            (2, 3, 7, 6),   # z hi
            (3, 0, 4, 7),   # x lo
        )
        for a, b, c, d in faces:
            pa, pb, pc, pd = corners[a], corners[b], corners[c], corners[d]
            normal = self._face_normal(pa, pb, pc, pd, (cx, cy, cz))
            if normal is None:
                continue
            self.add_quad(pa, pb, pc, pd, normal, material_name)

    @staticmethod
    def _face_normal(pa, pb, pc, pd, centre):
        ux, uy, uz = pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]
        vx, vy, vz = pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2]
        nx, ny, nz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
        length = math.sqrt(nx * nx + ny * ny + nz * nz)
        if length < 1e-12:
            return None
        nx, ny, nz = nx / length, ny / length, nz / length
        fx = (pa[0] + pb[0] + pc[0] + pd[0]) / 4.0 - centre[0]
        fy = (pa[1] + pb[1] + pc[1] + pd[1]) / 4.0 - centre[1]
        fz = (pa[2] + pb[2] + pc[2] + pd[2]) / 4.0 - centre[2]
        if nx * fx + ny * fy + nz * fz < 0:
            nx, ny, nz = -nx, -ny, -nz
        return (nx, ny, nz)

    def add_bar(self, p0, p1, width, thickness, role="metal", up=(0.0, 0.0, 1.0)):
        """Rectangular bar between two points (width across, thickness along up x dir)."""
        material_name = self.material(role)
        dx, dy, dz = p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]
        length = math.sqrt(dx * dx + dy * dy + dz * dz)
        if length < 1e-9:
            return
        dx, dy, dz = dx / length, dy / length, dz / length
        ux, uy, uz = up
        if abs(dx * ux + dy * uy + dz * uz) > 0.98:
            ux, uy, uz = (1.0, 0.0, 0.0)
        wx = dy * uz - dz * uy
        wy = dz * ux - dx * uz
        wz = dx * uy - dy * ux
        wl = math.sqrt(wx * wx + wy * wy + wz * wz)
        wx, wy, wz = wx / wl, wy / wl, wz / wl
        tx = wy * dz - wz * dy
        ty = wz * dx - wx * dz
        tz = wx * dy - wy * dx
        hw, ht = width * 0.5, thickness * 0.5
        corners = []
        for base in (p0, p1):
            for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                corners.append((base[0] + wx * hw * sx + tx * ht * sy,
                                base[1] + wy * hw * sx + ty * ht * sy,
                                base[2] + wz * hw * sx + tz * ht * sy))
        # corners 0..3 at p0, 4..7 at p1 -> reorder to bottom/top ring convention
        self.add_hexa([corners[0], corners[1], corners[2], corners[3],
                       corners[4], corners[5], corners[6], corners[7]], material_name)

    def add_cylinder(self, axis, centre, radius, length, segments, role="metal",
                     caps=True):
        material_name = self.material(role)
        cx, cy, cz = centre
        half = length * 0.5
        ring_lo, ring_hi = [], []
        for i in range(segments):
            angle = TAU * i / segments
            u, v = math.cos(angle) * radius, math.sin(angle) * radius
            if axis == "x":
                ring_lo.append((cx - half, cy + u, cz + v))
                ring_hi.append((cx + half, cy + u, cz + v))
            elif axis == "y":
                ring_lo.append((cx + u, cy - half, cz + v))
                ring_hi.append((cx + u, cy + half, cz + v))
            else:
                ring_lo.append((cx + u, cy + v, cz - half))
                ring_hi.append((cx + u, cy + v, cz + half))
        for i in range(segments):
            j = (i + 1) % segments
            mid_u = (i + 0.5) * TAU / segments
            radial = self._radial(axis, mid_u)
            self.add_quad(ring_lo[i], ring_hi[i], ring_hi[j], ring_lo[j], radial,
                          material_name)
        if caps:
            centre_lo = {"x": (cx - half, cy, cz), "y": (cx, cy - half, cz),
                         "z": (cx, cy, cz - half)}[axis]
            centre_hi = {"x": (cx + half, cy, cz), "y": (cx, cy + half, cz),
                         "z": (cx, cy, cz + half)}[axis]
            normal_lo = {"x": (-1.0, 0.0, 0.0), "y": (0.0, -1.0, 0.0),
                         "z": (0.0, 0.0, -1.0)}[axis]
            normal_hi = tuple(-v for v in normal_lo)
            for i in range(segments):
                j = (i + 1) % segments
                self.add_triangle(centre_lo, ring_lo[i], ring_lo[j], normal_lo,
                                  material_name)
                self.add_triangle(centre_hi, ring_hi[j], ring_hi[i], normal_hi,
                                  material_name)

    def add_tube(self, axis, centre, radius_outer, radius_inner, length, segments,
                 role="metal"):
        material_name = self.material(role)
        cx, cy, cz = centre
        half = length * 0.5
        outer_lo, outer_hi, inner_lo, inner_hi = [], [], [], []
        for i in range(segments):
            angle = TAU * i / segments
            ou, ov = math.cos(angle) * radius_outer, math.sin(angle) * radius_outer
            iu, iv = math.cos(angle) * radius_inner, math.sin(angle) * radius_inner
            if axis == "x":
                outer_lo.append((cx - half, cy + ou, cz + ov))
                outer_hi.append((cx + half, cy + ou, cz + ov))
                inner_lo.append((cx - half, cy + iu, cz + iv))
                inner_hi.append((cx + half, cy + iu, cz + iv))
            elif axis == "y":
                outer_lo.append((cx + ou, cy - half, cz + ov))
                outer_hi.append((cx + ou, cy + half, cz + ov))
                inner_lo.append((cx + iu, cy - half, cz + iv))
                inner_hi.append((cx + iu, cy + half, cz + iv))
            else:
                outer_lo.append((cx + ou, cy + ov, cz - half))
                outer_hi.append((cx + ou, cy + ov, cz + half))
                inner_lo.append((cx + iu, cy + iv, cz - half))
                inner_hi.append((cx + iu, cy + iv, cz + half))
        axis_normal = {"x": (1.0, 0.0, 0.0), "y": (0.0, 1.0, 0.0),
                       "z": (0.0, 0.0, 1.0)}[axis]
        for i in range(segments):
            j = (i + 1) % segments
            mid_u = (i + 0.5) * TAU / segments
            radial = self._radial(axis, mid_u)
            self.add_quad(outer_lo[i], outer_hi[i], outer_hi[j], outer_lo[j],
                          radial, material_name)
            self.add_quad(inner_lo[j], inner_hi[j], inner_hi[i], inner_lo[i],
                          tuple(-v for v in radial), material_name)
            self.add_quad(outer_lo[i], outer_lo[j], inner_lo[j], inner_lo[i],
                          tuple(-v for v in axis_normal), material_name)
            self.add_quad(outer_hi[j], outer_hi[i], inner_hi[i], inner_hi[j],
                          axis_normal, material_name)

    @staticmethod
    def _radial(axis, angle):
        u, v = math.cos(angle), math.sin(angle)
        if axis == "x":
            return (0.0, u, v)
        if axis == "y":
            return (u, 0.0, v)
        return (u, v, 0.0)

    def add_disc(self, axis, centre, radius, thickness, segments, role="metal"):
        self.add_cylinder(axis, centre, radius, thickness, segments, role)

    def add_gear(self, axis, centre, radius, thickness, teeth, role="metal",
                 spokes=0):
        """Disc + rectangular teeth + optional spokes (all in the plane normal to axis)."""
        self.add_cylinder(axis, centre, radius * 0.82, thickness, 16, role)
        cx, cy, cz = centre
        for tooth in range(teeth):
            angle = TAU * tooth / teeth
            u, v = math.cos(angle), math.sin(angle)
            inner = radius * 0.80
            outer = radius
            if axis == "x":
                p0 = (cx, cy + u * inner, cz + v * inner)
                p1 = (cx, cy + u * outer, cz + v * outer)
                self.add_bar(p0, p1, radius * 0.20, thickness, role,
                             up=(1.0, 0.0, 0.0))
            elif axis == "y":
                p0 = (cx + u * inner, cy, cz + v * inner)
                p1 = (cx + u * outer, cy, cz + v * outer)
                self.add_bar(p0, p1, radius * 0.20, thickness, role,
                             up=(0.0, 1.0, 0.0))
            else:
                p0 = (cx + u * inner, cy + v * inner, cz)
                p1 = (cx + u * outer, cy + v * outer, cz)
                self.add_bar(p0, p1, radius * 0.20, thickness, role,
                             up=(0.0, 0.0, 1.0))
        if spokes:
            for spoke in range(spokes):
                angle = TAU * spoke / spokes + 0.2
                u, v = math.cos(angle), math.sin(angle)
                inner = radius * 0.18
                outer = radius * 0.78
                if axis == "x":
                    p0 = (cx, cy + u * inner, cz + v * inner)
                    p1 = (cx, cy + u * outer, cz + v * outer)
                    self.add_bar(p0, p1, radius * 0.10, thickness * 0.6, role,
                                 up=(1.0, 0.0, 0.0))
                else:
                    p0 = (cx + u * inner, cy + v * inner, cz)
                    p1 = (cx + u * outer, cy + v * outer, cz)
                    self.add_bar(p0, p1, radius * 0.10, thickness * 0.6, role,
                                 up=(0.0, 0.0, 1.0))
        # hub
        self.add_cylinder(axis, centre, radius * 0.16, thickness, 10, role)

    def add_belt(self, axis_x, pulley_a, radius_a, pulley_b, radius_b, width,
                 role="detail"):
        """Two belt strands between two pulleys; pulleys are (y, z) in the YZ plane."""
        ya, za = pulley_a
        yb, zb = pulley_b
        for sign in (-1.0, 1.0):
            p0 = (axis_x, ya + 0.0, za + sign * radius_a)
            p1 = (axis_x, yb + 0.0, zb + sign * radius_b)
            self.add_bar(p0, p1, width, width * 0.4, role, up=(1.0, 0.0, 0.0))

    # -- assembly helpers --------------------------------------------------
    def add_base_frame(self, wood="wood", metal="metal"):
        w, d = self.width, self.depth
        beam = 0.16
        # feet / skids: touch y = 0 and both x and z extremes
        self.add_box((0.0, 0.0, 0.0), (w, 0.14, beam), wood)
        self.add_box((0.0, 0.0, d - beam), (w, 0.14, d), wood)
        self.add_box((0.0, 0.0, beam), (beam, 0.14, d - beam), wood)
        self.add_box((w - beam, 0.0, beam), (w, 0.14, d - beam), wood)
        # corner posts
        for x in (0.0, w - beam):
            for z in (0.0, d - beam):
                self.add_box((x, 0.14, z), (x + beam, 0.42, z + beam), wood)
        # deck beams
        self.add_box((beam, 0.30, beam), (w - beam, 0.40, beam + 0.10), wood)
        self.add_box((beam, 0.30, d - beam - 0.10), (w - beam, 0.40, d - beam), wood)
        self.add_box((beam, 0.30, beam + 0.10), (beam + 0.10, 0.40, d - beam - 0.10), wood)
        self.add_box((w - beam - 0.10, 0.30, beam + 0.10), (w - beam, 0.40, d - beam - 0.10), wood)
        # rivets / bolts on the feet
        for x in (0.10, w - 0.10):
            for z in (0.10, d - 0.10):
                self.add_box((x - 0.03, 0.14, z - 0.03), (x + 0.03, 0.18, z + 0.03), metal)

    def add_deck_planls(self, y0, y1, role="wood"):
        w, d = self.width, self.depth
        self.add_box((0.20, y0, 0.20), (w - 0.20, y1, d - 0.20), role)

    def add_power_shaft(self, y, z, radius=0.11, flange=0.22):
        w = self.width
        self.add_cylinder("x", (w * 0.5, y, z), radius, w, 12, "metal")
        # flanges: the cylinder is inset so it touches x = 0 / x = width exactly
        for x in (0.07, w - 0.07):
            self.add_cylinder("x", (x, y, z), flange, 0.14, 12, "metal")
        for x in (0.20, w - 0.20):
            self.add_box((x - 0.10, y - 0.16, z - 0.16),
                         (x + 0.10, y + 0.10, z + 0.16), "wood")
        # bearing blocks inside
        for x in (0.45, w - 0.45):
            self.add_box((x - 0.12, y - 0.20, z - 0.20), (x + 0.12, y + 0.10, z + 0.20),
                         "metal")

    def add_intake(self, y_centre=0.62, radius=0.30, flange_width=0.56):
        w, d = self.width, self.depth
        x = w * 0.5
        # flange plate touches z = depth
        self.add_box((x - flange_width, y_centre - flange_width, d - 0.14),
                     (x + flange_width, y_centre + flange_width, d), "metal")
        self.add_cylinder("z", (x, y_centre, d - 0.62), radius, 1.06, 14, "metal")
        self.add_tube("z", (x, y_centre, d - 0.62), radius + 0.10, radius + 0.03,
                      0.12, 14, "metal")
        # bolts on the flange
        for dx in (-0.42, 0.42):
            for dy in (-0.42, 0.42):
                self.add_box((x + dx - 0.04, y_centre + dy - 0.04, d - 0.17),
                             (x + dx + 0.04, y_centre + dy + 0.04, d - 0.10), "metal")
        # riser pipe going up (inside the station)
        self.add_cylinder("y", (x, 1.20, d - 0.62), radius * 0.85, 0.90, 12, "metal")

    def add_outlet(self, y_centre=1.05, radius=0.23):
        w = self.width
        x = w * 0.5
        self.add_box((x - 0.34, y_centre - 0.34, 0.0), (x + 0.34, y_centre + 0.34, 0.16),
                     "metal")
        self.add_cylinder("z", (x, y_centre, 0.44), radius, 0.84, 14, "metal")
        self.add_tube("z", (x, y_centre, 0.20), radius + 0.09, radius + 0.02, 0.10,
                      14, "metal")
        for dx in (-0.24, 0.24):
            for dy in (-0.24, 0.24):
                self.add_box((x + dx - 0.04, y_centre + dy - 0.04, 0.0),
                             (x + dx + 0.04, y_centre + dy + 0.04, 0.05), "metal")

    def add_gauge(self, x, y, z, kind="front", radius=0.17):
        if not self.has_role("glass"):
            return
        if kind == "front":
            self.add_cylinder("z", (x, y, z + 0.07), radius, 0.14, 12, "metal")
            self.add_tube("z", (x, y, z + 0.14), radius + 0.03, radius - 0.01, 0.05,
                          12, "metal")
            self.add_cylinder("z", (x, y, z + 0.17), radius * 0.82, 0.02, 12, "glass")
            self.add_bar((x - radius * 0.4, y - radius * 0.2, z + 0.19),
                         (x + radius * 0.3, y + radius * 0.35, z + 0.19),
                         0.02, 0.02, "metal", up=(0.0, 0.0, 1.0))
        else:
            self.add_cylinder("x", (x, y, z), radius, 0.10, 12, "metal")
            self.add_cylinder("x", (x + 0.07, y, z), radius * 0.8, 0.03, 12, "glass")

    def add_window(self, x0, x1, y0, y1, z, role_frame="metal", role_glass="glass"):
        if not self.has_role(role_glass):
            return
        t = 0.08
        self.add_box((x0, y0, z), (x1, y0 + t, z + 0.10), role_frame)
        self.add_box((x0, y1 - t, z), (x1, y1, z + 0.10), role_frame)
        self.add_box((x0, y0, z), (x0 + t, y1, z + 0.10), role_frame)
        self.add_box((x1 - t, y0, z), (x1, y1, z + 0.10), role_frame)
        self.add_box((x0 + 0.02, y0 + 0.02, z + 0.03),
                     (x1 - 0.02, y1 - 0.02, z + 0.05), role_glass)
        # mullions
        mx = (x0 + x1) * 0.5
        self.add_box((mx - 0.03, y0, z), (mx + 0.03, y1, z + 0.09), role_frame)

    def add_canopy(self, y, overhang=0.0, thickness=0.14, role="wood"):
        w, d = self.width, self.depth
        self.add_box((0.0 - overhang, y, 0.0 - overhang),
                     (w + overhang, y + thickness, d + overhang), role)
        # fascia band
        self.add_box((0.0, y + thickness, 0.0), (w, y + thickness + 0.05, 0.05), "wood")
        self.add_box((0.0, y + thickness, d - 0.05), (w, y + thickness + 0.05, d), "wood")
        self.add_box((0.0, y + thickness, 0.05), (0.05, y + thickness + 0.05, d - 0.05), "wood")
        self.add_box((w - 0.05, y + thickness, 0.05), (w, y + thickness + 0.05, d - 0.05), "wood")

    def add_flat_roof(self, top_y, thickness=0.14, role="wood", beam_role="wood"):
        """Flat roof slab whose TOP face is exactly top_y (the envelope top)."""
        w, d = self.width, self.depth
        self.add_box((0.0, top_y - thickness, 0.0), (w, top_y, d), role)
        y0 = top_y - thickness - 0.20
        self.add_box((0.0, y0, 0.0), (w, y0 + 0.20, 0.08), beam_role)
        self.add_box((0.0, y0, d - 0.08), (w, y0 + 0.20, d), beam_role)
        self.add_box((0.0, y0, 0.08), (0.08, y0 + 0.20, d - 0.08), beam_role)
        self.add_box((w - 0.08, y0, 0.08), (w, y0 + 0.20, d - 0.08), beam_role)

    def add_chimney(self, x, z, y0, y1, radius=0.16, role="metal"):
        self.add_cylinder("y", (x, (y0 + y1) * 0.5, z), radius, y1 - y0, 12, role)
        self.add_tube("y", (x, y1 - 0.05, z), radius + 0.06, radius, 0.10, 12, "metal")

    def add_ladder(self, x, z, y0, y1, role="metal"):
        self.add_bar((x, y0, z), (x, y1, z), 0.05, 0.05, role, up=(1.0, 0.0, 0.0))
        self.add_bar((x + 0.26, y0, z), (x + 0.26, y1, z), 0.05, 0.05, role,
                     up=(1.0, 0.0, 0.0))
        rungs = max(2, int((y1 - y0) / 0.24))
        for rung in range(rungs):
            y = y0 + (rung + 0.5) * (y1 - y0) / rungs
            self.add_bar((x, y, z), (x + 0.26, y, z), 0.04, 0.04, role,
                         up=(0.0, 0.0, 1.0))

    def add_rivets(self, p0, p1, count, role="metal", size=0.035):
        for index in range(count):
            t = (index + 0.5) / count
            x = p0[0] + (p1[0] - p0[0]) * t
            y = p0[1] + (p1[1] - p0[1]) * t
            z = p0[2] + (p1[2] - p0[2]) * t
            self.add_box((x - size, y - size, z - size), (x + size, y + size, z + size),
                         role)

    def add_fins(self, x, z0, z1, y0, y1, count, role="metal"):
        for index in range(count):
            z = z0 + (z1 - z0) * (index + 0.5) / count
            self.add_box((x, y0, z - 0.02), (x + 0.10, y1, z + 0.02), role)

    def add_power_shaft_parts(self, y, z, radius=0.11, flange=0.22, turns=1.0,
                              name="#AnimatedDriveShaft"):
        """Drive shaft = one rotating node (shaft + end flanges) + static bearings."""
        w = self.width
        self.begin_part(name, [w * 0.5, y, z],
                        {"type": "rotation", "axis": "x", "turns": turns})
        self.add_cylinder("x", (w * 0.5, y, z), radius, w, 12, "metal")
        for x in (0.07, w - 0.07):
            self.add_cylinder("x", (x, y, z), flange, 0.14, 12, "metal")
        self.end_part()
        for x in (0.45, w - 0.45):
            self.add_box((x - 0.12, y - 0.20, z - 0.20),
                         (x + 0.12, y + 0.10, z + 0.20), "metal")
        for x in (0.20, w - 0.20):
            self.add_box((x - 0.10, y - 0.16, z - 0.16),
                         (x + 0.10, y + 0.10, z + 0.16), "wood")

    # -- output ------------------------------------------------------------
    def finish(self):
        """Assign ART-07 world-scale UVs and emit the JSON data (static mesh + animated parts)."""
        owners = [self._static] + self.parts
        # pass 1: per-material projected world extents (projection axes depend on face normal)
        extents = {}
        for owner in owners:
            for points_x, points_y, points_z, normal, material_name in owner["world_points"]:
                u_axis, v_axis = C.projection_axes(normal)
                record = extents.get(material_name)
                if record is None:
                    record = {"u_min": math.inf, "u_max": -math.inf,
                              "v_min": math.inf, "v_max": -math.inf}
                    extents[material_name] = record
                for point in (points_x, points_y, points_z):
                    record["u_min"] = min(record["u_min"], point[u_axis])
                    record["u_max"] = max(record["u_max"], point[u_axis])
                    record["v_min"] = min(record["v_min"], point[v_axis])
                    record["v_max"] = max(record["v_max"], point[v_axis])
        plans = {}
        uv_stats = {}
        for material_name, record in extents.items():
            extent_u = record["u_max"] - record["u_min"]
            extent_v = record["v_max"] - record["v_min"]
            plan = C.uv_plan(material_name, extent_u, extent_v)
            plan["centre_u"] = (record["u_min"] + record["u_max"]) * 0.5
            plan["centre_v"] = (record["v_min"] + record["v_max"]) * 0.5
            plans[material_name] = plan
            uv_stats[material_name] = {
                "px_per_m_u": round(plan["pu"], 3),
                "px_per_m_v": round(plan["pv"], 3),
                "native_px_per_m": round(plan["native"], 3),
                "projected_span_m": [round(extent_u, 4), round(extent_v, 4)],
            }
        # pass 2: assign UVs
        for owner in owners:
            owner_uvs = []
            for points_x, points_y, points_z, normal, material_name in owner["world_points"]:
                u_axis, v_axis = C.projection_axes(normal)
                plan = plans[material_name]
                uv = []
                for point in (points_x, points_y, points_z):
                    u, v = C.uv_for_plan(plan, point[u_axis], point[v_axis],
                                         plan["centre_u"], plan["centre_v"])
                    uv.extend((u, v))
                owner_uvs.append(uv)
            owner["triangle_uvs"][:] = owner_uvs

        # bounds + envelope violations over the rest pose (animated parts included)
        bounds = [[math.inf] * 3, [-math.inf] * 3]
        violations = []
        for owner in owners:
            for vertex in owner["vertices"]:
                for axis in range(3):
                    bounds[0][axis] = min(bounds[0][axis], vertex[axis])
                    bounds[1][axis] = max(bounds[1][axis], vertex[axis])
                    if vertex[axis] < -1e-6 or vertex[axis] > self.size[axis] + 1e-6:
                        violations.append({"vertex": vertex, "axis": axis,
                                           "part": owner.get("name")})

        parts = []
        for part in self.parts:
            pivot = part["pivot"]
            local_vertices = [[round(vertex[0] - pivot[0], 6),
                               round(vertex[1] - pivot[1], 6),
                               round(vertex[2] - pivot[2], 6)]
                              for vertex in part["vertices"]]
            parts.append({
                "name": part["name"],
                "pivot": list(pivot),
                "animation": dict(part["animation"]),
                "vertices": local_vertices,
                "triangles": part["triangles"],
                "triangle_materials": part["triangle_materials"],
                "triangle_uvs": part["triangle_uvs"],
                "materials": part["materials"],
            })

        union_materials = []
        for owner in owners:
            for material_name in owner["materials"]:
                if material_name not in union_materials:
                    union_materials.append(material_name)

        return {
            "bounds_violations": violations[:20],
            "vertices": self.vertices,
            "triangles": self.triangles,
            "triangle_materials": self.triangle_materials,
            "triangle_uvs": self.triangle_uvs,
            "materials": union_materials,
            "parts": parts,
            "uv_stats": uv_stats,
            "bounds_min": [round(v, 6) for v in bounds[0]],
            "bounds_max": [round(v, 6) for v in bounds[1]],
            "triangles_count": sum(len(owner["triangles"]) for owner in owners),
            "vertices_count": sum(len(owner["vertices"]) for owner in owners),
            "parts_count": len(parts),
            "animated_node_names": [part["name"] for part in parts],
        }

# --------------------------------------------------------------------------- #
# tier builders
# --------------------------------------------------------------------------- #
def build_basic(b, data):
    """Mk I: open timber frame, ONE reciprocating cylinder, belt pulley, power shaft."""
    w, h, d = b.width, b.height, b.depth
    b.add_base_frame()
    b.add_box((0.20, 0.40, 0.20), (w - 0.20, 0.48, d - 0.20), "wood")   # deck
    # A-frame that carries the pump and the belt pulley
    for x in (0.26, w - 0.40):
        b.add_box((x, 0.48, 1.48), (x + 0.14, 2.42, 1.90), "wood")
    b.add_box((0.26, 2.42, 1.48), (w - 0.26, 2.56, 1.90), "wood")
    b.add_bar((0.40, 0.55, 1.95), (w - 0.54, 2.30, 1.95), 0.08, 0.08, "wood")
    b.add_bar((w - 0.54, 0.55, 1.48), (0.40, 2.30, 1.48), 0.08, 0.08, "wood")
    # single vertical pump barrel with iron hoops (static body)
    px, pz = 0.78, 1.70
    b.add_cylinder("y", (px, 1.15, pz), 0.36, 1.35, 14, "metal")
    b.add_tube("y", (px, 0.55, pz), 0.43, 0.34, 0.10, 14, "metal")
    b.add_tube("y", (px, 1.62, pz), 0.43, 0.34, 0.10, 14, "metal")
    b.add_cylinder("y", (px, 1.96, pz), 0.44, 0.30, 14, "metal")
    # ANIMATED piston (rod + crosshead moves up/down, 2 strokes per loop)
    b.begin_part("#AnimatedPiston", [px, 2.47, pz],
                 {"type": "translation", "axis": "y", "amplitude": 0.06,
                  "cycles": 4.0, "phase": 0.0})
    b.add_cylinder("y", (px, 2.47, pz), 0.07, 0.86, 8, "metal")
    b.add_box((px - 0.20, 2.52, pz - 0.14), (px + 0.20, 2.64, pz + 0.14), "wood")
    b.end_part()
    # static top finial reaches exactly y = height
    b.add_box((px - 0.10, h - 0.14, pz - 0.10), (px + 0.10, h, pz + 0.10), "metal")
    # wooden handle lever (static rest pose)
    b.add_bar((px, 2.58, pz), (0.34, 1.95, 0.62), 0.10, 0.10, "wood")
    b.add_bar((0.34, 1.95, 0.62), (0.22, 1.50, 0.35), 0.13, 0.13, "wood")
    # ANIMATED flywheel (right side, axis X) and head pulley + head shaft
    b.begin_part("#AnimatedFlywheel", [w - 0.38, 1.50, 1.55],
                 {"type": "rotation", "axis": "x", "turns": 2.0})
    b.add_gear("x", (w - 0.38, 1.50, 1.55), 0.92, 0.20, 8, "metal", spokes=6)
    b.end_part()
    b.begin_part("#AnimatedHeadPulley", [w - 0.38, 2.28, 1.55],
                 {"type": "rotation", "axis": "x", "turns": 8.0})
    b.add_gear("x", (w - 0.38, 2.28, 1.55), 0.24, 0.16, 8, "metal", spokes=4)
    b.end_part()
    shaft_x = (px + w - 0.38) * 0.5
    b.begin_part("#AnimatedHeadShaft", [shaft_x, 2.28, pz],
                 {"type": "rotation", "axis": "x", "turns": 8.0})
    b.add_cylinder("x", (shaft_x, 2.28, pz), 0.08, w - 0.38 - px, 8, "metal")
    b.end_part()
    # static belt
    b.add_belt(w - 0.38, (1.50, 1.55), 0.92, (2.28, 1.55), 0.24, 0.10, "detail")
    # ANIMATED drive shaft + static bearings / mounts
    b.add_power_shaft_parts(1.50, 1.55, radius=0.11, flange=0.22, turns=2.0)
    # intake (back-bottom) and outlet (front-middle)
    b.add_intake(0.65, 0.30, 0.48)
    b.add_outlet(1.02, 0.22)
    b.add_rivets((0.22, 0.28, 0.06), (w - 0.22, 0.28, 0.06), 5, "metal")
    b.add_rivets((0.22, 0.28, d - 0.06), (w - 0.22, 0.28, d - 0.06), 5, "metal")
    return {"kind": "single-cylinder timber pump", "tier": "Basic"}


def build_intermediate(b, data):
    """Mk II: plank machine house, TWIN barrels, gearbox, gauges, small flat roof."""
    w, h, d = b.width, b.height, b.depth
    b.add_base_frame()
    b.add_box((0.20, 0.40, 0.20), (w - 0.20, 0.50, d - 0.20), "wood")
    wall_top = h - 0.32
    # back wall + right/left lower walls + front corner posts
    b.add_box((0.0, 0.50, d - 0.16), (w, wall_top, d - 0.06), "wood")
    for index in range(5):
        x = 0.16 + index * (w - 0.32) / 5.0
        b.add_box((x, 0.54, d - 0.10), (x + 0.05, wall_top - 0.05, d - 0.04), "wood")
    for x in (0.06, w - 0.16):
        b.add_box((x, 0.50, 0.06), (x + 0.10, wall_top, 0.18), "wood")
    b.add_box((0.06, 0.50, 0.18), (0.16, 1.30, d - 0.16), "wood")
    b.add_box((w - 0.16, 0.50, 0.18), (w - 0.06, 1.30, d - 0.16), "wood")
    # upper front band with plaster panel + glass portholes
    b.add_box((0.0, wall_top - 0.30, 0.0), (w, wall_top, 0.10), "wood")
    if b.has_role("plaster"):
        b.add_box((0.22, wall_top - 0.58, 0.01), (w - 0.22, wall_top - 0.34, 0.06),
                  "plaster")
    b.add_window(0.62, 0.98, wall_top - 0.62, wall_top - 0.36, 0.0)
    b.add_window(w - 1.02, w - 0.66, wall_top - 0.62, wall_top - 0.36, 0.0)
    # twin-cylinder pump barrels (static)
    for px in (w * 0.5 - 0.30, w * 0.5 + 0.30):
        b.add_cylinder("y", (px, 1.10, d * 0.66), 0.26, 1.25, 12, "metal")
        b.add_tube("y", (px, 0.52, d * 0.66), 0.32, 0.24, 0.09, 12, "metal")
        b.add_tube("y", (px, 1.68, d * 0.66), 0.32, 0.24, 0.09, 12, "metal")
    # ANIMATED twin piston rods + shared crosshead
    b.begin_part("#AnimatedPiston", [w * 0.5, 2.00, d * 0.66],
                 {"type": "translation", "axis": "y", "amplitude": 0.06,
                  "cycles": 4.0, "phase": 0.0})
    for px in (w * 0.5 - 0.30, w * 0.5 + 0.30):
        b.add_cylinder("y", (px, 2.00, d * 0.66), 0.06, 0.56, 8, "metal")
    b.add_box((w * 0.5 - 0.56, 2.10, d * 0.66 - 0.10),
              (w * 0.5 + 0.56, 2.22, d * 0.66 + 0.10), "wood")
    b.end_part()
    b.add_cylinder("y", (w * 0.5, 1.10, d * 0.66), 0.09, 1.2, 8, "metal")
    # gearbox housing at the front bottom (static) + ANIMATED gear train
    b.add_box((w * 0.5 - 0.44, 0.55, 0.42), (w * 0.5 + 0.44, 1.05, 1.20), "wood")
    b.add_box((w * 0.5 - 0.48, 1.01, 0.42), (w * 0.5 + 0.48, 1.09, 1.20), "metal")
    b.begin_part("#AnimatedGearboxGear", [w - 0.44, 1.22, 0.82],
                 {"type": "rotation", "axis": "x", "turns": 4.0})
    b.add_gear("x", (w - 0.44, 1.22, 0.82), 0.40, 0.16, 10, "metal", spokes=4)
    b.end_part()
    b.begin_part("#AnimatedGearboxPinion", [w - 0.44, 1.22, 1.52],
                 {"type": "rotation", "axis": "x", "turns": -4.0})
    b.add_gear("x", (w - 0.44, 1.22, 1.52), 0.28, 0.16, 9, "metal", spokes=3)
    b.end_part()
    # ANIMATED flywheel (right side, outside the wall) + belt pulley
    b.begin_part("#AnimatedFlywheel", [w - 0.18, 1.40, 1.50],
                 {"type": "rotation", "axis": "x", "turns": 2.0})
    b.add_gear("x", (w - 0.18, 1.40, 1.50), 0.74, 0.18, 10, "metal", spokes=5)
    b.end_part()
    b.begin_part("#AnimatedHeadPulley", [w - 0.30, 2.16, d * 0.66],
                 {"type": "rotation", "axis": "x", "turns": 6.0})
    b.add_cylinder("x", (w - 0.30, 2.16, d * 0.66), 0.18, 0.16, 10, "metal")
    b.end_part()
    b.add_belt(w - 0.18, (1.40, 1.50), 0.74, (2.16, d * 0.66), 0.20, 0.08, "detail")
    # pressure gauges on the front band + ANIMATED power shaft
    b.add_gauge(0.46, wall_top - 0.52, 0.12, "front", 0.13)
    b.add_gauge(w - 0.46, wall_top - 0.52, 0.12, "front", 0.13)
    b.add_power_shaft_parts(1.40, 1.50, radius=0.10, flange=0.20, turns=2.0)
    # roof: top surface is exactly y = height
    b.add_flat_roof(h, thickness=0.14, role="wood", beam_role="wood")
    b.add_box((0.20, h - 0.30, 0.20), (0.32, h - 0.14, 0.32), "metal")
    b.add_box((w - 0.32, h - 0.30, 0.20), (w - 0.20, h - 0.14, 0.32), "metal")
    # intake / outlet / ladder
    b.add_intake(0.66, 0.28, 0.46)
    b.add_outlet(1.05, 0.22)
    b.add_ladder(w - 0.62, d - 0.06, 0.7, wall_top - 0.1, "metal")
    b.add_rivets((0.20, 0.54, d - 0.05), (w - 0.20, 0.54, d - 0.05), 6, "metal")
    return {"kind": "plank machine house, twin barrels, gearbox", "tier": "Intermediate"}


def build_advanced(b, data):
    """Mk III: metal frame, THREE-stage pump, glass window, treated planks, gauges."""
    w, h, d = b.width, b.height, b.depth
    b.add_base_frame(metal="metal")
    column = 0.14
    for x in (0.0, w - column):
        for z in (0.0, d - column):
            b.add_box((x, 0.42, z), (x + column, h - 0.28, z + column), "metal")
    for y in (1.30, 2.34):
        b.add_box((0.0, y, 0.0), (w, y + 0.10, 0.06), "metal")
        b.add_box((0.0, y, d - 0.06), (w, y + 0.10, d), "metal")
        b.add_box((0.0, y, 0.06), (0.06, y + 0.10, d - 0.06), "metal")
        b.add_box((w - 0.06, y, 0.06), (w, y + 0.10, d - 0.06), "metal")
    b.add_bar((0.10, 0.55, 0.10), (w - 0.10, 1.22, 0.10), 0.06, 0.06, "metal")
    b.add_bar((w - 0.10, 0.55, d - 0.10), (0.10, 1.22, d - 0.10), 0.06, 0.06, "metal")
    # treated plank cladding (lower side panels + back panel)
    for x in (0.12, w - 0.18):
        b.add_box((x, 0.50, 0.40), (x + 0.06, 1.20, d - 0.40), "planks")
    b.add_box((0.20, 0.50, d - 0.20), (w - 0.20, 1.18, d - 0.12), "planks")
    # three-stage pump barrels + manifold along X (static)
    for px in (w * 0.5 - 0.46, w * 0.5, w * 0.5 + 0.46):
        b.add_cylinder("y", (px, 1.22, d * 0.66), 0.29, 1.30, 12, "metal")
        b.add_tube("y", (px, 0.60, d * 0.66), 0.31, 0.23, 0.09, 12, "metal")
        b.add_tube("y", (px, 1.78, d * 0.66), 0.31, 0.23, 0.09, 12, "metal")
    b.add_cylinder("x", (w * 0.5, 2.28, d * 0.66), 0.11, w - 0.70, 12, "metal")
    b.add_tube("x", (w * 0.5, 2.28, d * 0.66), 0.16, 0.11, 0.10, 12, "metal")
    # ANIMATED piston rods (3 stages, phases 0 / 1/3 / 2/3)
    for index, px in enumerate((w * 0.5 - 0.46, w * 0.5, w * 0.5 + 0.46)):
        b.begin_part("#AnimatedPiston%d" % (index + 1), [px, 2.22, d * 0.66],
                     {"type": "translation", "axis": "y", "amplitude": 0.06,
                      "cycles": 4.0, "phase": index / 3.0})
        b.add_cylinder("y", (px, 2.22, d * 0.66), 0.06, 0.44, 8, "metal")
        b.add_box((px - 0.17, 2.30, d * 0.66 - 0.12),
                  (px + 0.17, 2.42, d * 0.66 + 0.12), "metal")
        b.end_part()
    # solid upper back + left panels (machine hall), front/right stay open
    b.add_box((0.18, 1.30, d - 0.16), (w - 0.18, 3.30, d - 0.06), "planks")
    b.add_box((0.06, 1.30, 0.20), (0.18, 3.30, d - 0.20), "metal")
    # glass inspection window on the front face
    b.add_window(0.52, w - 0.52, 1.42, 2.16, 0.0)
    # ANIMATED flywheel + gear train OUTSIDE the frame (the camera side)
    b.begin_part("#AnimatedFlywheel", [w - 0.10, 1.55, 1.50],
                 {"type": "rotation", "axis": "x", "turns": 2.0})
    b.add_gear("x", (w - 0.10, 1.55, 1.50), 1.00, 0.16, 12, "metal", spokes=6)
    b.end_part()
    b.begin_part("#AnimatedHeadPulley", [w - 0.10, 2.85, 1.50],
                 {"type": "rotation", "axis": "x", "turns": 6.0})
    b.add_gear("x", (w - 0.10, 2.85, 1.50), 0.30, 0.14, 9, "metal", spokes=4)
    b.end_part()
    b.begin_part("#AnimatedGearboxGear", [w - 0.10, 2.30, 2.55],
                 {"type": "rotation", "axis": "x", "turns": -4.0})
    b.add_gear("x", (w - 0.10, 2.30, 2.55), 0.35, 0.14, 10, "metal", spokes=4)
    b.end_part()
    b.add_belt(w - 0.10, (1.55, 1.50), 1.00, (2.85, 1.50), 0.30, 0.08, "detail")
    # gauges on the frame + ANIMATED power shaft
    b.add_gauge(0.56, 2.90, 0.16, "front", 0.14)
    b.add_gauge(w - 0.56, 2.90, 0.16, "front", 0.14)
    b.add_power_shaft_parts(1.55, 1.50, radius=0.12, flange=0.28, turns=2.0)
    # intake with riser + valve wheel, big outlet flange
    b.add_intake(0.68, 0.28, 0.48)
    b.add_cylinder("y", (w * 0.5, 1.00, d - 0.62), 0.32, 0.22, 12, "metal")
    for index in range(4):
        angle = TAU * index / 4.0
        b.add_bar((w * 0.5, 1.00, d - 0.62),
                  (w * 0.5 + math.cos(angle) * 0.42, 1.00,
                   d - 0.62 + math.sin(angle) * 0.42), 0.05, 0.05, "metal",
                  up=(0.0, 1.0, 0.0))
    b.add_outlet(1.10, 0.24)
    # roof: top face exactly y = height
    b.add_flat_roof(h, thickness=0.14, role="metal", beam_role="metal")
    b.add_ladder(w * 0.30, d - 0.05, 0.8, 2.2, "metal")
    b.add_fins(w - 0.22, 1.00, 2.10, 1.62, 2.10, 4, "metal")
    b.add_rivets((0.25, 0.56, 0.06), (w - 0.25, 0.56, 0.06), 8, "metal")
    b.add_rivets((0.25, 0.56, d - 0.06), (w - 0.25, 0.56, d - 0.06), 8, "metal")
    return {"kind": "metal frame, three-stage pump, glass window", "tier": "Advanced"}


def build_top(b, data):
    """Mk IV: heavy pump station, TWIN parallel barrels, heat fins, valves, big coupling."""
    w, h, d = b.width, b.height, b.depth
    b.add_base_frame(metal="metal")
    column = 0.18
    for x in (0.0, w * 0.5 - column * 0.5, w - column):
        for z in (0.0, d - column):
            b.add_box((x, 0.42, z), (x + column, h - 0.26, z + column), "metal")
    for y in (1.05, 2.10):
        b.add_box((0.0, y, 0.0), (w, y + 0.12, 0.07), "metal")
        b.add_box((0.0, y, d - 0.07), (w, y + 0.12, d), "metal")
        b.add_box((0.0, y, 0.07), (0.07, y + 0.12, d - 0.07), "metal")
        b.add_box((w - 0.07, y, 0.07), (w, y + 0.12, d - 0.07), "metal")
    # heavy plated cladding
    b.add_box((0.20, 0.50, d - 0.20), (w - 0.20, 1.95, d - 0.10), "planks")
    for x in (0.10, w - 0.22):
        b.add_box((x, 0.50, 0.45), (x + 0.10, 1.62, d - 0.45), "planks")
    # twin parallel barrels (horizontal, along Z) + saddles (static)
    for px in (w * 0.5 - 0.62, w * 0.5 + 0.62):
        b.add_cylinder("z", (px, 2.38, d * 0.55), 0.42, d * 0.62, 16, "metal")
        for z in (0.60, d * 0.86):
            b.add_tube("z", (px, 2.38, z), 0.50, 0.40, 0.14, 16, "metal")
        b.add_box((px - 0.50, 1.84, d * 0.28), (px + 0.50, 1.96, d * 0.92), "metal")
    # twin feed pipes from the intake manifold up into both barrels (static)
    for px in (w * 0.5 - 0.62, w * 0.5 + 0.62):
        b.add_cylinder("y", (px, 1.20, d * 0.86), 0.24, 2.10, 12, "metal")
    # ANIMATED piston rams at the barrel fronts (phases 0 / 0.5)
    for index, px in enumerate((w * 0.5 - 0.62, w * 0.5 + 0.62)):
        b.begin_part("#AnimatedPiston%d" % (index + 1), [px, 2.38, 0.62],
                     {"type": "translation", "axis": "z", "amplitude": 0.06,
                      "cycles": 4.0, "phase": index * 0.5})
        b.add_cylinder("z", (px, 2.38, 0.62), 0.30, 0.62, 12, "metal")
        b.add_cylinder("z", (px, 2.38, 0.34), 0.12, 0.20, 8, "metal")
        b.end_part()
    # front output manifold + outlet
    b.add_cylinder("x", (w * 0.5, 2.38, 0.52), 0.22, w - 0.70, 14, "metal")
    b.add_tube("x", (w * 0.5, 2.38, 0.52), 0.28, 0.22, 0.12, 14, "metal")
    b.add_box((w * 0.5 - 0.70, 2.06, 0.0), (w * 0.5 + 0.70, 2.16, 0.16), "metal")
    b.add_outlet(1.18, 0.28)
    # big valves with hand wheels on the feed pipes
    for px in (w * 0.5 - 0.62, w * 0.5 + 0.62):
        b.add_cylinder("y", (px, 2.66, d * 0.86), 0.30, 0.16, 12, "metal")
        for index in range(4):
            angle = TAU * index / 4.0
            b.add_bar((px, 2.74, d * 0.86),
                      (px + math.cos(angle) * 0.40, 2.74,
                       d * 0.86 + math.sin(angle) * 0.40), 0.06, 0.06, "detail",
                      up=(0.0, 1.0, 0.0))
    # heat fins on the right-hand saddle
    b.add_fins(w - 0.30, 1.10, 2.30, 1.65, 2.15, 7, "metal")
    # upper back plated wall + ANIMATED flywheel / head pulley OUTSIDE the frame
    b.add_box((0.18, 1.90, d - 0.16), (w - 0.18, 3.30, d - 0.06), "planks")
    b.begin_part("#AnimatedFlywheel", [w - 0.12, 1.70, 1.60],
                 {"type": "rotation", "axis": "x", "turns": 2.0})
    b.add_gear("x", (w - 0.12, 1.70, 1.60), 1.24, 0.20, 14, "metal", spokes=7)
    b.end_part()
    b.begin_part("#AnimatedHeadPulley", [w - 0.12, 3.02, 1.60],
                 {"type": "rotation", "axis": "x", "turns": 8.0})
    b.add_gear("x", (w - 0.12, 3.02, 1.60), 0.34, 0.16, 10, "metal", spokes=4)
    b.end_part()
    b.add_belt(w - 0.12, (1.70, 1.60), 1.24, (3.02, 1.60), 0.34, 0.10, "detail")
    b.add_power_shaft_parts(1.70, 1.60, radius=0.15, flange=0.38, turns=2.0)
    b.add_box((0.0, 1.40, d * 0.5 - 0.30), (0.22, 2.00, d * 0.5 + 0.30), "metal")
    b.add_box((w - 0.22, 1.40, d * 0.5 - 0.30), (w, 2.00, d * 0.5 + 0.30), "metal")
    # inspection window, gauges, roof (top face exactly y = height)
    b.add_window(0.55, w - 0.55, 1.38, 1.92, 0.0)
    b.add_gauge(0.60, 2.78, 0.16, "front", 0.15)
    b.add_gauge(w - 0.60, 2.78, 0.16, "front", 0.15)
    b.add_flat_roof(h, thickness=0.16, role="metal", beam_role="metal")
    b.add_box((w * 0.5 - 0.30, h - 0.34, d * 0.40), (w * 0.5 + 0.30, h - 0.16,
              d * 0.40 + 0.30), "metal")
    b.add_ladder(w * 0.74, d - 0.06, 0.8, 2.8, "metal")
    b.add_rivets((0.30, 1.20, d - 0.09), (w - 0.30, 1.20, d - 0.09), 9, "metal")
    b.add_rivets((0.30, 1.72, d - 0.09), (w - 0.30, 1.72, d - 0.09), 9, "metal")
    return {"kind": "twin-barrel pump station, heat fins, valves", "tier": "Top"}


def build_construction(b, data, tier):
    """Greybox construction stage: same envelope, base + partial frame + scaffolds."""
    w, h, d = b.width, b.height, b.depth
    b.add_base_frame()
    b.add_box((0.16, 0.14, 0.16), (w - 0.16, 0.26, d - 0.16), "wood")
    post_h = h * 0.42
    for x in (0.10, w - 0.26):
        for z in (0.10, d - 0.26):
            b.add_box((x, 0.42, z), (x + 0.16, post_h, z + 0.16), "wood")
    for course in range(3):
        y0 = 0.48 + course * 0.30
        b.add_box((0.20, y0, d - 0.22), (w - 0.20, y0 + 0.24, d - 0.12), "wood")
        if course < 2:
            b.add_box((0.20, y0, 0.55), (0.30, y0 + 0.24, d - 0.30), "wood")
            b.add_box((w - 0.30, y0, 0.55), (w - 0.20, y0 + 0.24, d - 0.30), "wood")
    # scaffolding: vertical poles + cross braces; one mast reaches exactly y = height
    for x in (0.28, w - 0.32):
        for z in (0.30, d - 0.36):
            b.add_box((x, 0.20, z), (x + 0.07, h * 0.78, z + 0.07), "metal")
    b.add_bar((0.30, 0.45, 0.34), (w - 0.34, h * 0.75, 0.34), 0.05, 0.05, "metal")
    b.add_bar((w - 0.34, 0.45, d - 0.40), (0.30, h * 0.75, d - 0.40), 0.05, 0.05,
              "metal")
    b.add_box((0.24, h * 0.78, 0.26), (w - 0.24, h * 0.84, 0.34), "metal")
    b.add_box((w * 0.5 - 0.05, h * 0.84, 0.27), (w * 0.5 + 0.05, h - 0.06, 0.37),
              "metal")
    b.add_box((w * 0.5 - 0.08, h - 0.06, 0.24), (w * 0.5 + 0.08, h, 0.40), "metal")
    # material stacks on the deck
    b.add_box((0.35, 0.44, 0.45), (0.80, 0.57, 0.90), "wood")
    b.add_box((0.35, 0.57, 0.45), (0.80, 0.64, 0.90), "wood")
    b.add_box((w - 0.90, 0.44, 0.45), (w - 0.55, 0.52, 0.80), "metal")
    b.add_box((w * 0.5 - 0.20, 0.44, 0.20), (w * 0.5 + 0.20, 0.54, 0.42), "metal")
    b.add_cylinder("x", (w * 0.5, 0.48, 0.22), 0.10, 0.16, 10, "metal")
    b.add_ladder(0.12, 0.50, 0.5, h * 0.72, "metal")
    return {"kind": "construction greybox", "tier": tier}


BUILDERS = {
    "Basic": build_basic,
    "Intermediate": build_intermediate,
    "Advanced": build_advanced,
    "Top": build_top,
}


# --------------------------------------------------------------------------- #
# smoke test model: 1x1x1 box with 6 faces, 3 materials, 3 ports
# --------------------------------------------------------------------------- #
def build_smoke():
    """Pipeline smoke test: 1x1x1 static box + one rotating animated node."""
    b = ModelBuilder((1.0, 1.0, 1.0),
                     {"wood": "BaseWood_Brown.Folktails",
                      "metal": "BaseMetal.Folktails"},
                     "SM_MWP_SmokeTest")
    b.add_box((0.0, 0.0, 0.0), (1.0, 0.3, 1.0), "wood")
    b.begin_part("#AnimatedSmokeRotor", [0.5, 0.5, 0.5],
                 {"type": "rotation", "axis": "x", "turns": 1.0})
    b.add_box((0.2, 0.3, 0.3), (0.8, 0.7, 0.7), "metal")
    b.add_box((0.4, 0.35, 0.35), (0.6, 0.65, 0.65), "wood")
    b.end_part()
    data = b.finish()
    data.update({
        "stem": "MWP.SmokeTest.AnimatedBox.Model",
        "object_name": "SM_MWP_SmokeTest",
        "tier": "Basic",
        "faction": "Folktails",
        "construction": False,
        "size": [1.0, 1.0, 1.0],
        "ports": [
            {"name": "#Port#Power", "position": [1.0, 0.5, 0.5]},
            {"name": "#Port#Pipe", "position": [0.5, 0.3, 1.0]},
            {"name": "#Port#Logistics", "position": [0.5, 0.5, 0.0]},
        ],
        "kind": "smoke test",
        "notes": ["pipeline smoke test"],
    })
    return data


# --------------------------------------------------------------------------- #
# driver
# --------------------------------------------------------------------------- #
def build_model(tier, faction, construction):
    size = C.TIER_SIZE[tier]
    roles = roles_for(tier, faction, construction)
    stem = C.model_stem(tier, faction, construction)
    b = ModelBuilder(size, roles, stem)
    if construction:
        info = build_construction(b, None, tier)
    else:
        info = BUILDERS[tier](b, None)
    data = b.finish()
    data.update({
        "stem": stem,
        "object_name": "SM_MWP_Pump_%s_%s%s" % (
            tier, faction, "_ConstructionStage0" if construction else ""),
        "tier": tier,
        "faction": faction,
        "construction": construction,
        "size": list(size),
        "ports": C.ports(tier, faction, construction),
        "kind": info["kind"],
        "notes": [
            "generated by art/gen_models.py (deterministic, no DCC round-trip)",
            "export space: block-corner origin, X=width, Y=height, Z=depth, front Z=0",
            "no animation, no #Slot# marker, no text/logo",
            "UVs: world-scale projection onto the verified vanilla-material canvas "
            "(art/material_texture_map.json)",
        ],
    })
    return data


def validate_model_data(data):
    size = data["size"]
    problems = []
    minimums = data["bounds_min"]
    maximums = data["bounds_max"]
    for axis in range(3):
        if abs(minimums[axis]) > C.BOUNDS_TOLERANCE:
            problems.append("bounds min axis %d = %.5f (want 0)" % (axis, minimums[axis]))
        if abs(maximums[axis] - size[axis]) > C.BOUNDS_TOLERANCE:
            problems.append("bounds max axis %d = %.5f (want %.3f)"
                            % (axis, maximums[axis], size[axis]))
    if data["triangles_count"] > C.TRI_BUDGET_LOD0:
        problems.append("triangles %d > budget %d"
                        % (data["triangles_count"], C.TRI_BUDGET_LOD0))
    for material in data["materials"]:
        if material not in C.MATERIAL_WHITELIST[data["faction"]]:
            problems.append("material %r not in whitelist" % material)
    if not 1 <= len(data["materials"]) <= 4:
        problems.append("material count %d" % len(data["materials"]))
    for uvs in data["triangle_uvs"]:
        for value in uvs:
            if value < 0.0 or value > 1.0:
                problems.append("uv outside [0,1]: %.6f" % value)
                break
    for group in [data] + data["parts"]:
        for triangle in group["triangles"]:
            if len(set(triangle)) != 3:
                problems.append("degenerate triangles in %r" % group.get("name", "static"))
                break
    # UV texel density / canvas checks
    for material, stats in data["uv_stats"].items():
        density = min(stats["px_per_m_u"], stats["px_per_m_v"])
        if density < 80.0 - 1e-6:
            problems.append("material %s UV density %.1f px/m < 80" % (material, density))
        if density > stats["native_px_per_m"] + 1e-3:
            problems.append("material %s UV density %.1f > native %.1f"
                            % (material, density, stats["native_px_per_m"]))
    # animated parts
    parts = data["parts"]
    if data["construction"]:
        if parts:
            problems.append("construction model must be static (has %d animated parts)"
                            % len(parts))
    else:
        if not parts:
            problems.append("finished model has no animated parts")
    names = set()
    for part in parts:
        name = part["name"]
        if not name.startswith(C.PART_PREFIX):
            problems.append("part name %r does not start with %r" % (name, C.PART_PREFIX))
        if name in names:
            problems.append("duplicate part name %r" % (name,))
        names.add(name)
        for prefix in C.MARKER_PREFIXES:
            if name.startswith(prefix):
                problems.append("part name %r collides with %r" % (name, prefix))
        pivot = part["pivot"]
        for axis in range(3):
            if not (0.0 <= pivot[axis] <= size[axis]):
                problems.append("part %s pivot axis %d outside envelope" % (name, axis))
        animation = part["animation"]
        kind = animation.get("type")
        if kind == "rotation":
            if animation.get("axis") not in ("x", "y", "z"):
                problems.append("part %s rotation axis %r" % (name, animation.get("axis")))
            turns = float(animation.get("turns", 0.0))
            if abs(turns) < 1e-6:
                problems.append("part %s has zero turns" % name)
            if abs(turns - round(turns)) > 1e-9:
                problems.append("part %s turns %.3f is not an integer (not seamless)"
                                % (name, turns))
        elif kind == "translation":
            if animation.get("axis") not in ("x", "y", "z"):
                problems.append("part %s translation axis %r" % (name, animation.get("axis")))
            amplitude = float(animation.get("amplitude", 0.0))
            cycles = float(animation.get("cycles", 0.0))
            if amplitude <= 0.0:
                problems.append("part %s amplitude <= 0" % name)
            if abs(cycles - round(cycles)) > 1e-9 or cycles < 1:
                problems.append("part %s cycles %.3f is not a positive integer"
                                % (name, cycles))
            if not 0.0 <= float(animation.get("phase", 0.0)) < 1.0:
                problems.append("part %s phase outside [0,1)" % name)
        else:
            problems.append("part %s has unknown animation type %r" % (name, kind))
    return problems


def write_json(path, payload):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, indent=1, ensure_ascii=False, sort_keys=False)
        handle.write("\n")


def main():
    smoke = "--smoke" in sys.argv
    if smoke:
        data = build_smoke()
        path = os.path.join(C.MODELS_DIR, "_smoke", "MWP.SmokeTest.AnimatedBox.Model.json")
        write_json(path, data)
        print("smoke source written: %s" % path)
        print("  tris=%d verts=%d materials=%s bounds=%s..%s"
              % (data["triangles_count"], data["vertices_count"], data["materials"],
                 data["bounds_min"], data["bounds_max"]))
        return

    specs = C.all_model_specs()
    manifest = {
        "generated_by": "art/gen_models.py",
        "art_revision": "MWP-ART-v1",
        "coordinate_system": "block-corner export space, X=width, Y=height, Z=depth, front Z=0",
        "deterministic": True,
        "model_count": len(specs),
        "models": [],
        "failures": [],
    }
    for index, spec in enumerate(specs, start=1):
        print("[%02d/%02d] %s" % (index, len(specs), spec["stem"]))
        data = build_model(spec["tier"], spec["faction"], spec["construction"])
        problems = validate_model_data(data)
        if problems:
            manifest["failures"].append({"stem": spec["stem"], "problems": problems,
                                         "bounds_violations": data.get("bounds_violations")})
            print("        FAILED: %s" % "; ".join(problems))
            print("        violations: %s" % (data.get("bounds_violations")[:4],))
            continue
        write_json(spec["source"], data)
        record = {
            "stem": spec["stem"],
            "tier": spec["tier"],
            "faction": spec["faction"],
            "construction": spec["construction"],
            "source": os.path.relpath(spec["source"], C.WORKSPACE).replace("\\", "/"),
            "kind": data["kind"],
            "size": list(spec["size"]),
            "triangles": data["triangles_count"],
            "vertices": data["vertices_count"],
            "materials": data["materials"],
            "bounds_min": data["bounds_min"],
            "bounds_max": data["bounds_max"],
        }
        manifest["models"].append(record)
        print("        tris=%4d verts=%5d materials=%s"
              % (record["triangles"], record["vertices"], record["materials"]))
        print("        bounds %s .. %s" % (record["bounds_min"], record["bounds_max"]))

    manifest_path = os.path.join(C.MODELS_DIR, "gen-manifest.json")
    write_json(manifest_path, manifest)
    print("\nmanifest: %s" % manifest_path)
    if manifest["failures"]:
        print("GENERATION RESULT: FAIL (%d/%d models)" % (len(manifest["failures"]), len(specs)))
        sys.exit(1)
    print("GENERATION RESULT: PASS (%d models, %d triangles total)"
          % (len(manifest["models"]),
             sum(model["triangles"] for model in manifest["models"])))


if __name__ == "__main__":
    main()
