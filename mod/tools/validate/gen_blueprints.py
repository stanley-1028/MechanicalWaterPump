#!/usr/bin/env python3
"""Generate the 8 MWP blueprints and the 2 template collections.

This is a developer script, not part of the validator build (it is not compiled). It documents
how data/ was produced and lets the blueprints be regenerated after a contract change.

Run from the project root:  python mod/tools/validate/gen_blueprints.py

Geometry decision (Lead, 2026-11-04): engine axes are Size{X=width, Y=footprint depth,
Z=height}; BaseZ=1 like the vanilla pumps; BlockObjectSpec.Blocks are copied cell-by-cell from
the vanilla blueprint of the same footprint; Entrance {X:width centre, Y:-1, Z:BaseZ};
LocalAccess {X:width centre, Y:1.0 height, Z:0.5 door depth}; WaterInput one level below the
base at the depth row opposite the entrance; WaterObstacle across that whole intake row.
T7: models are the mod's own Blender assets (Buildings/Water/MWP/MWP.Pump.<Tier>.<Faction>
.Model / ...ConstructionStage0.Model, shipped as sibling .timbermesh files under data/), and the
colliders are one box per part matching the engine footprint (width x height x depth).
The 8 x 128x128 icons are T6 art assets, not generated here: this script never writes Icons/ so it
cannot clobber an art delivery.

Sources of truth: mod/docs/CONTRACT.md sections 3-9, requirements 20.1.2 / 20.2.4, and the
installed game's StreamingAssets/Modding/Blueprints.zip (read-only; no vanilla asset ships).
"""
import json
import os
import sys
import zipfile

# This script lives in <project>/mod/tools/validate/, so the project root is four levels up.
ROOT = os.path.dirname(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
)
DATA = os.path.join(ROOT, "mod", "data")
if not os.path.isdir(DATA):
    sys.exit("mod/data not found next to this script: " + DATA)
GAME = os.environ.get(
    "TIMBERBORN_DIR", r"C:\Program Files (x86)\Steam\steamapps\common\Timberborn"
)
BLUEPRINTS_ZIP = os.path.join(
    GAME, "Timberborn_Data", "StreamingAssets", "Modding", "Blueprints.zip"
)

WATER_PUMP = "Buildings/Water/WaterPump/WaterPump.Folktails.blueprint.json"
DEEP_WATER_PUMP = "Buildings/Water/DeepWaterPump/DeepWaterPump.IronTeeth.blueprint.json"
MECH_PUMP = "Buildings/Water/MechanicalPump/MechanicalPump.Folktails.blueprint.json"
DEEP_MECH_PUMP = "Buildings/Water/DeepMechanicalPump/DeepMechanicalPump.IronTeeth.blueprint.json"
LARGE_PUMP = "Buildings/Water/LargeWaterPump/LargeWaterPump.Folktails.blueprint.json"

# CONTRACT section 6 placeholder-art table: tier -> (Folktails, IronTeeth) model blueprint.
# `blocks` is the vanilla blueprint the BlockObjectSpec.Blocks array is copied from (Lead:
# same footprint -> lowest placement risk); `art` is the blueprint the TimbermeshSpec /
# CollidersSpec / sound names are taken from (CONTRACT section 6).
TIERS = [
    dict(
        tier="Basic", camel="Basic", science=150,
        cost=[("Log", 20)],
        water=3.0, power=100, max_depth=2, capacity=15,
        maint_days=8.0, maint=[("Log", 10)], builder_hours=3,
        blocks={"Folktails": WATER_PUMP, "IronTeeth": DEEP_WATER_PUMP},
        art={"Folktails": WATER_PUMP, "IronTeeth": DEEP_WATER_PUMP},
        pipe={
            "Folktails": "Buildings/Water/WaterPump/WaterPumpPipeSegment.Folktails",
            "IronTeeth": "Buildings/Water/DeepWaterPump/DeepWaterPumpPipeSegment.IronTeeth",
        },
        base="ConstructionBase2x3",
    ),
    dict(
        tier="Intermediate", camel="Intermediate", science=400,
        cost=[("Plank", 30), ("Log", 10)],
        water=6.0, power=200, max_depth=4, capacity=30,
        maint_days=10.0, maint=[("Plank", 15)], builder_hours=5,
        blocks={"Folktails": WATER_PUMP, "IronTeeth": DEEP_WATER_PUMP},
        art={"Folktails": WATER_PUMP, "IronTeeth": DEEP_WATER_PUMP},
        pipe={
            "Folktails": "Buildings/Water/WaterPump/WaterPumpPipeSegment.Folktails",
            "IronTeeth": "Buildings/Water/DeepWaterPump/DeepWaterPumpPipeSegment.IronTeeth",
        },
        base="ConstructionBase2x3",
    ),
    dict(
        tier="Advanced", camel="Advanced", science=700,
        cost=[("MetalBlock", 40), ("Plank", 20)],
        water=15.0, power=500, max_depth=6, capacity=75,
        maint_days=12.0, maint=[("MetalBlock", 10), ("Plank", 10)], builder_hours=8,
        blocks={"Folktails": MECH_PUMP, "IronTeeth": DEEP_MECH_PUMP},
        art={"Folktails": MECH_PUMP, "IronTeeth": DEEP_MECH_PUMP},
        pipe={
            "Folktails": "Buildings/Water/WaterPump/WaterPumpPipeSegment.Folktails",
            "IronTeeth": "Buildings/Water/DeepWaterPump/DeepWaterPumpPipeSegment.IronTeeth",
        },
        base="ConstructionBase3x3",
    ),
    dict(
        tier="Top", camel="Top", science=1500,
        cost=[("MetalBlock", 60)],
        water=20.0, power=700, max_depth=8, capacity=100,
        maint_days=15.0, maint=[("MetalBlock", 15)], builder_hours=12,
        # TierCatalog keeps Mk IV at 3x4x4 for both factions, but the only IronTeeth pump
        # model is DeepMechanicalPump (3x3x4): occupancy data comes from the 3x4x4 vanilla
        # LargeWaterPump (blocks are faction neutral), the model/colliders stay IronTeeth.
        # The one-block model mismatch is the documented visual limitation (ART_PLACEHOLDER.md).
        blocks={"Folktails": LARGE_PUMP, "IronTeeth": LARGE_PUMP},
        art={"Folktails": LARGE_PUMP, "IronTeeth": DEEP_MECH_PUMP},
        pipe={
            "Folktails": "Buildings/Water/WaterPump/WaterPumpPipeSegment.Folktails",
            "IronTeeth": "Buildings/Water/DeepWaterPump/DeepWaterPumpPipeSegment.IronTeeth",
        },
        base="ConstructionBase3x4",
    ),
]

FACTIONS = ["Folktails", "IronTeeth"]

SOLID_OCCUPATIONS = "Bottom, Top, Corners, Path, Middle"


def read_vanilla():
    if not os.path.exists(BLUEPRINTS_ZIP):
        sys.exit("game Blueprints.zip not found: " + BLUEPRINTS_ZIP)
    with zipfile.ZipFile(BLUEPRINTS_ZIP) as archive:
        cache = {}

        def load(path):
            if path not in cache:
                cache[path] = json.loads(archive.read(path))
            return cache[path]

        return {path: load(path) for path in {p for t in TIERS for p in (*t["blocks"].values(), *t["art"].values())}}


def cost_array(cost):
    return [{"Id": good, "Amount": amount} for good, amount in cost]


def goods_array(goods):
    return [{"Id": good, "Amount": amount} for good, amount in goods]


def transputs(width):
    """CONTRACT section 4: the three vanilla MechanicalPump ports (left, right, bottom)."""
    return {
        "Transputs": [
            {
                "Coordinates": {"X": 0, "Y": 1, "Z": 1},
                "Directions": "Down, Left, Up, Bottom",
                "ReverseRotation": False,
            },
            {
                "Coordinates": {"X": width - 1, "Y": 1, "Z": 1},
                "Directions": "Down, Up, Right, Bottom",
                "ReverseRotation": False,
            },
            {
                "Coordinates": {"X": 1, "Y": 1, "Z": 1},
                "Directions": "Bottom",
                "ReverseRotation": False,
            },
        ],
        "IgnoreRotation": False,
    }


def colliders(width, depth, height):
    """One box that exactly covers the footprint.

    Local/engine space is (X = width, Y = height, Z = depth); the engine values are
    Size{X,Y,Z} = (width, depth, height), so height = Z size and depth = Y size."""
    return {
        "BoxColliders": [
            {
                "Center": {"X": width / 2.0, "Y": height / 2.0, "Z": depth / 2.0},
                "Size": {"X": float(width), "Y": float(height), "Z": float(depth)},
            }
        ],
        "SphereColliders": [],
        "CapsuleColliders": [],
    }


def easy_support_blocks(width, depth, height):
    """BlockObjectSpec.Blocks for the two big tiers (DV-06, 2026-11-05).

    Mk I/II copy the vanilla WaterPump layout. Mk III/IV originally copied the vanilla
    MechanicalPump/LargeWaterPump layouts, whose GroundOrStackable strip sits in the middle of
    the footprint and therefore forces the player to find flat ground under the middle row -
    in practice they could not be placed on the narrow banks where Mk I/II fit. This generator
    keeps the same envelope but moves the ONLY solid support cells to the front row (the
    entrance row), so a 3x1 flat strip on the bank is enough and everything behind it may
    overhang water, exactly like the early tiers.
    """
    blocks = []
    for z in range(height):
        for y in range(depth):
            for x in range(width):
                if z == 0:
                    block = {"MatterBelow": "Any", "Occupations": "None"}
                elif z == 1 and y == 0:
                    block = {"MatterBelow": "GroundOrStackable",
                             "Occupations": "Bottom, Top, Corners, Path, Middle"}
                else:
                    block = {"MatterBelow": "Any",
                             "Occupations": "Floor, Bottom, Corners, Path, Middle"}
                block.update({"Stackable": "None", "OccupyAllBelow": False, "Underground": False})
                blocks.append(block)
    return blocks


def build_blueprint(tier, faction, vanilla):
    source = vanilla[tier["blocks"][faction]]
    art = vanilla[tier["art"][faction]]
    size = source["BlockObjectSpec"]["Size"]
    x, y, z = size["X"], size["Y"], size["Z"]
    base_z = source["BlockObjectSpec"]["BaseZ"]
    model = "Buildings/Water/MWP/MWP.Pump.{}.{}.Model".format(tier["tier"], faction)
    stage_model = model.replace(".Model", ".ConstructionStage0.Model")
    box_colliders = colliders(x, y, z)
    template = "MechanicalWaterPump.{}.{}".format(tier["tier"], faction)

    return {
        "BuildingSpec": {
            "SelectionSoundName": art["BuildingSpec"]["SelectionSoundName"],
            "LoopingSoundName": art["BuildingSpec"]["LoopingSoundName"],
            "BuildingCost": cost_array(tier["cost"]),
            "ScienceCost": tier["science"],
            "PlaceFinished": False,
            "FinishableWithBeaversOnSite": False,
            "DrawRangeBoundsOnIt": False,
        },
        "BuildingModelSpec": art["BuildingModelSpec"],
        "TemplateSpec": {
            "TemplateName": template,
            "BackwardCompatibleTemplateNames": [],
            "RequiredFeatureToggle": "",
            "DisablingFeatureToggle": "",
        },
        "BlockObjectSpec": {
            "Size": {"X": x, "Y": y, "Z": z},
            # Mk I/II copy the vanilla WaterPump cells verbatim; Mk III/IV use the
            # front-row-support layout so they place on the same narrow banks
            # (DV-06, 2026-11-05).
            "Blocks": (easy_support_blocks(x, y, z)
                       if tier["tier"] in ("Advanced", "Top")
                       else source["BlockObjectSpec"]["Blocks"]),
            "Entrance": {
                "HasEntrance": True,
                "Coordinates": {"X": x // 2, "Y": -1, "Z": base_z},
            },
            "BaseZ": base_z,
            "Overridable": False,
            "Flippable": True,
        },
        "BlockObjectNavMeshSettingsSpec": {
            "NoAutoWalls": False,
            "GenerateFloorsOnStackable": False,
            "EdgeGroups": [],
            "UnblockedCoordinates": [],
            "BlockedEdges": [],
        },
        "BuildingAccessibleSpec": {
            "LocalAccess": {"X": x / 2.0, "Y": 1.0, "Z": 0.5},
            "ForceOneFinalAccess": False,
        },
        # Vanilla SimpleOutputInventoryInitializer always adds InventoryNeedBehavior; that
        # behaviour calls Enterable.CanReserveSlot when a beaver evaluates drink sources, so a
        # building with an output inventory but no Enterable crashes with a NullReferenceException
        # (BUG-01, 2026-11-05). A zero-capacity Enterable keeps ActionPosition null (safe) without
        # letting beavers enter the pump.
        "EnterableSpec": {
            "OperatingState": "Finished",
            "LimitedCapacityFinished": True,
            "CapacityFinished": 0,
            "LimitedCapacityUnfinished": True,
            "CapacityUnfinished": 0,
        },
        "PlaceableBlockObjectSpec": {
            "ToolGroupId": "Water",
            "ToolOrder": 60 + [t["tier"] for t in TIERS].index(tier["tier"]),
            "ToolShape": "Square",
            "Layout": "Single",
            "CustomPivot": {
                "HasCustomPivot": False,
                "Coordinates": {"X": 0.0, "Y": 0.0, "Z": 0.0},
            },
            "CanBeAttachedToTerrainSide": False,
            "DevModeTool": False,
        },
        "LabeledEntitySpec": {
            "DisplayNameLocKey": "Building.MWP.Pump.{}.DisplayName".format(tier["tier"]),
            "DescriptionLocKey": "Building.MWP.Pump.{}.Description".format(tier["tier"]),
            "FlavorDescriptionLocKey": "Building.MWP.Pump.Flavor",
            "Icon": "Icons/MWP{}{}".format(tier["camel"], faction),
        },
        "ConstructionSiteProgressVisualizerSpec": {"ProgressThresholds": [0.0]},
        "MechanicalBuildingSpec": {},
        "MechanicalNodeSpec": {
            "PowerOutput": 0,
            "PowerInput": tier["power"],
            "IsShaft": False,
        },
        "MechanicalNodeAnimatorSpec": {"MinSpeedMultiplier": 0.0},
        "MechanicalConnectorTargetSpec": {},
        "TransputProviderSpec": transputs(x),
        "WaterInputSpec": {
            # Intake on the depth face opposite the entrance, one level below the base
            # (= absolute water surface level, like vanilla WaterPump {X:0, Y:2, Z:-1}).
            "WaterInputCoordinates": {
                "X": 0 if x == 2 else 1,
                "Y": y - 1,
                "Z": -1,
            }
        },
        "WaterInputPipeSpec": {
            "PipeSegmentPrefabPath": tier["pipe"][faction],
            "PipeParentName": "#Finished",
            "MaxDepth": tier["max_depth"],
        },
        "WaterObstacleSpec": {
            "Coordinates": [{"X": xx, "Y": y - 1} for xx in range(x)]
        },
        "FinishableWaterObstacleSpec": {"Height": 1.0},
        "SimpleOutputInventorySpec": {
            "Capacity": tier["capacity"],
            "IgnorableCapacity": False,
        },
        "MWP.MechanicalWaterPumpSpec": {
            "Tier": tier["tier"],
            "WaterPerHour": tier["water"],
            "PowerRequiredHp": tier["power"],
            "InternalCapacity": tier["capacity"],
            "MaxDepth": tier["max_depth"],
            "BadwaterPenalty": 1.0,
            "MinWaterQuality": 0.0,
            "CubicMetersPerWater": 0.2,
            "MaintenanceIntervalDays": tier["maint_days"],
            "MaintenanceMaterials": goods_array(tier["maint"]),
            "MaintenanceDurationHours": 2.0,
        },
        "Children": {
            "#Finished": {
                "TimbermeshSpec": {"Model": model},
                "CollidersSpec": box_colliders,
            },
            "#Unfinished": {
                "Children": {
                    "{}#nested".format(tier["base"]): {
                        "BlueprintPath": "ConstructionBases/{0}/{0}.blueprint".format(
                            tier["base"]
                        )
                    },
                    "ConstructionStage0": {
                        "TimbermeshSpec": {"Model": stage_model},
                        "CollidersSpec": box_colliders,
                    },
                }
            },
        },
    }


def write_json(path, value):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(value, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


def write_blueprints(vanilla):
    directory = os.path.join(DATA, "Buildings", "Water", "MWP")
    os.makedirs(directory, exist_ok=True)
    written = []
    for tier in TIERS:
        for faction in FACTIONS:
            name = "MWP.Pump.{}.{}.blueprint.json".format(tier["tier"], faction)
            write_json(os.path.join(directory, name), build_blueprint(tier, faction, vanilla))
            written.append(name)
    return written


def write_template_collections():
    directory = os.path.join(DATA, "TemplateCollections")
    for faction in FACTIONS:
        paths = [
            "Buildings/Water/MWP/MWP.Pump.{}.{}.blueprint".format(tier["tier"], faction)
            for tier in TIERS
        ]
        write_json(
            os.path.join(
                directory,
                "TemplateCollection.Buildings.{}.blueprint.json".format(faction),
            ),
            {
                "TemplateCollectionSpec": {
                    "CollectionId": "Buildings.{}".format(faction),
                    "Blueprints#append": paths,
                }
            },
        )


def main():
    vanilla = read_vanilla()
    blueprints = write_blueprints(vanilla)
    write_template_collections()
    print("blueprints:", len(blueprints))
    for name in blueprints:
        print(" ", name)


if __name__ == "__main__":
    main()
