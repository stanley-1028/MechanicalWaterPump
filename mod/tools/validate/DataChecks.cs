using System.Globalization;
using System.Text.Json;

namespace MWP.Tools.Validate;

/// <summary>
/// Data-layer checks for the eight MWP blueprints, the two template collections, the manifest and
/// the eight placeholder icons. CONTRACT sections 4-7 plus requirements 20.1.2 / 20.2.4 are the
/// authority; the tier numbers are read from <see cref="SourceFacts"/> (TierCatalog), never
/// duplicated here.
/// </summary>
internal sealed class DataChecks
{
    /// <summary>Prefix of mod-owned model paths (T7: the Blender-exported .timbermesh assets).</summary>
    private const string ModModelPrefix = "Buildings/Water/MWP/";

    /// <summary>Spec-only blueprint that defines the mod's own collapsible bottom-bar tool group.</summary>
    private const string ToolGroupRelativePath = "BlockObjectToolGroups/BlockObjectToolGroup.MWP.blueprint.json";

    /// <summary>CONTRACT section 6 pipe segments per faction.</summary>
    private static readonly Dictionary<string, string> PipeTable = new(StringComparer.Ordinal)
    {
        ["Folktails"] = "Buildings/Water/WaterPump/WaterPumpPipeSegment.Folktails",
        ["IronTeeth"] = "Buildings/Water/DeepWaterPump/DeepWaterPumpPipeSegment.IronTeeth",
    };

    private static readonly string[] ManifestKeys =
    {
        "Name", "Version", "Id", "MinimumGameVersion", "Description", "RequiredMods", "OptionalMods",
    };

    private static readonly HashSet<string> BlockMatterBelow =
        new(StringComparer.Ordinal) { "Any", "Air", "GroundOrStackable" };

    private readonly string _dataDir;
    private readonly string _modRoot;
    private readonly SourceFacts _facts;
    private readonly GameBlueprintCatalog _game;
    private readonly Report _report;
    private readonly Dictionary<string, string> _templateNames = new(StringComparer.Ordinal);

    public DataChecks(
        string dataDir,
        string modRoot,
        SourceFacts facts,
        GameBlueprintCatalog game,
        Report report)
    {
        _dataDir = dataDir;
        _modRoot = modRoot;
        _facts = facts;
        _game = game;
        _report = report;
    }

    public void Validate()
    {
        ValidateManifest();
        ValidateBlueprintSet();
        ValidateTemplateCollections();
        ValidateToolGroup();
        ValidateIcons();
    }

    // ------------------------------------------------------------------ manifest
    private void ValidateManifest()
    {
        var path = Path.Combine(_dataDir, "manifest.json");
        if (!File.Exists(path))
        {
            _report.Error("manifest", "data/manifest.json is missing");
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            _report.Error("manifest", "manifest.json is not valid JSON: " + ex.Message);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _report.Error("manifest", "manifest.json root must be an object");
                return;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!ManifestKeys.Contains(property.Name, StringComparer.Ordinal))
                {
                    _report.Warn("manifest", "unknown manifest key '" + property.Name + "'");
                }
            }

            var name = StrOf(root, "Name");
            Check(!string.IsNullOrWhiteSpace(name), "manifest", "manifest.json: 'Name' must be a non-empty string");

            var id = StrOf(root, "Id");
            Check(id == _facts.ModId, "manifest",
                "manifest.json: 'Id' must be '" + _facts.ModId + "' (MwpDefaults.ModId), found " + Show(id));

            var version = StrOf(root, "Version");
            Check(version == _facts.ModVersion, "manifest",
                "manifest.json: 'Version' must be '" + _facts.ModVersion + "' (MwpDefaults.ModVersion), found " + Show(version));

            var minimumVersion = StrOf(root, "MinimumGameVersion");
            Check(!string.IsNullOrWhiteSpace(minimumVersion) && Version.TryParse(minimumVersion, out _),
                "manifest", "manifest.json: 'MinimumGameVersion' must be a version string, found " + Show(minimumVersion));

            Check(!string.IsNullOrWhiteSpace(StrOf(root, "Description")), "manifest",
                "manifest.json: 'Description' must be a non-empty string");

            ValidateModList(root, "RequiredMods", mustBeEmpty: true);
            ValidateModList(root, "OptionalMods", mustBeEmpty: true);
        }
    }

    private void ValidateModList(JsonElement root, string key, bool mustBeEmpty)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            _report.Error("manifest", "manifest.json: missing '" + key + "' array");
            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            _report.Error("manifest", "manifest.json: '" + key + "' must be an array");
            return;
        }

        var count = value.GetArrayLength();
        if (mustBeEmpty && count != 0)
        {
            _report.Error("manifest", "manifest.json: '" + key + "' must be empty (DV-02: Timber Pipes is detected at runtime)");
        }
    }

    // ------------------------------------------------------------------ blueprints
    private void ValidateBlueprintSet()
    {
        var folder = Path.Combine(_dataDir, "Buildings", "Water", "MWP");
        var expected = new List<string>();

        foreach (var tier in SourceFacts.TierTokens)
        {
            foreach (var faction in SourceFacts.Factions)
            {
                var name = "MWP.Pump." + tier + "." + faction + ".blueprint.json";
                expected.Add(name);
                var path = Path.Combine(folder, name);
                var relative = "Buildings/Water/MWP/" + name;
                if (!File.Exists(path))
                {
                    _report.Error("blueprint", relative + ": missing");
                    continue;
                }

                ValidateBlueprintFile(path, relative, tier, faction);
            }
        }

        // No stray blueprint documents: every *.blueprint.json in data/ must be one of ours or a
        // template collection (validated separately).
        foreach (var file in Directory.GetFiles(_dataDir, "*.blueprint.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_dataDir, file).Replace('\\', '/');
            var fileName = Path.GetFileName(file);
            var isBlueprint = expected.Contains(fileName, StringComparer.Ordinal)
                              && relative.StartsWith("Buildings/Water/MWP/", StringComparison.Ordinal);
            var isCollection = relative.StartsWith("TemplateCollections/", StringComparison.Ordinal);
            var isToolGroup = relative == ToolGroupRelativePath;
            if (!isBlueprint && !isCollection && !isToolGroup)
            {
                _report.Error("blueprint", relative + ": unexpected blueprint file in data/");
            }
        }

        if (_templateNames.Count > 0)
        {
            var duplicates = _templateNames
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);
            foreach (var duplicate in duplicates)
            {
                _report.Error("blueprint", "TemplateName '" + duplicate + "' is declared by more than one blueprint");
            }

            foreach (var template in _templateNames.Keys)
            {
                if (_game.Available && _game.HasTemplate(template))
                {
                    _report.Error("blueprint", "TemplateName '" + template + "' collides with a vanilla template");
                }
            }
        }
    }

    private void ValidateBlueprintFile(string path, string relative, string tier, string faction)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            _report.Error("blueprint", relative + ": invalid JSON: " + ex.Message);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _report.Error("blueprint", relative + ": root must be an object");
                return;
            }

            if (!_facts.Tiers.TryGetValue(tier, out var expected))
            {
                _report.Error("blueprint", relative + ": TierCatalog has no tier '" + tier + "'");
                return;
            }

            ValidateTemplateSpec(root, relative, tier, faction, expected);
            ValidateBuildingSpec(root, relative, expected);
            ValidateBuildingModelSpec(root, relative);
            ValidateBlockObjectSpec(root, relative, expected);
            ValidateAccessSpec(root, relative, expected);
            ValidatePlaceableSpec(root, relative, tier, expected);
            ValidateLabeledEntitySpec(root, relative, tier, faction);
            ValidateConstructionSiteSpec(root, relative);
            ValidateMechanicalSpecs(root, relative, expected);
            ValidateWaterSpecs(root, relative, expected, faction);
            ValidateOutputSpec(root, relative, expected);
            ValidateCustomSpec(root, relative, tier, expected);
            ValidateChildren(root, relative, tier, faction, expected);
        }
    }

    private void ValidateTemplateSpec(JsonElement root, string file, string tier, string faction, TierExpectation expected)
    {
        if (!TrySpec(root, "TemplateSpec", file, out var spec))
        {
            return;
        }

        var name = StrOf(spec, "TemplateName");
        var wanted = "MechanicalWaterPump." + tier + "." + faction;
        Check(name == wanted, file, "TemplateSpec.TemplateName must be '" + wanted + "', found " + Show(name));
        if (name is not null)
        {
            if (_templateNames.TryGetValue(name, out var owner))
            {
                _report.Error("blueprint", file + ": TemplateName '" + name + "' is already used by " + owner);
            }
            else
            {
                _templateNames[name] = file;
            }
        }

        Check(ArrOf(spec, "BackwardCompatibleTemplateNames")?.GetArrayLength() == 0, file,
            "TemplateSpec.BackwardCompatibleTemplateNames must be empty");
        Check(StrOf(spec, "RequiredFeatureToggle") == string.Empty, file,
            "TemplateSpec.RequiredFeatureToggle must be empty");
        Check(StrOf(spec, "DisablingFeatureToggle") == string.Empty, file,
            "TemplateSpec.DisablingFeatureToggle must be empty");
    }

    private void ValidateBuildingSpec(JsonElement root, string file, TierExpectation expected)
    {
        if (!TrySpec(root, "BuildingSpec", file, out var spec))
        {
            return;
        }

        var science = IntOf(spec, "ScienceCost");
        Check(science == expected.ScienceCost, file,
            "BuildingSpec.ScienceCost must be " + expected.ScienceCost + ", found " + Show(science));

        var cost = GoodsOf(spec, "BuildingCost");
        Check(GoodsEqual(cost, expected.BuildCost), file,
            "BuildingSpec.BuildingCost must be " + FormatGoods(expected.BuildCost) + ", found " + FormatGoods(cost));

        Check(BoolOf(spec, "PlaceFinished") == false, file, "BuildingSpec.PlaceFinished must be false");
        Check(BoolOf(spec, "FinishableWithBeaversOnSite") == false, file,
            "BuildingSpec.FinishableWithBeaversOnSite must be false");
        Check(BoolOf(spec, "DrawRangeBoundsOnIt") == false, file, "BuildingSpec.DrawRangeBoundsOnIt must be false");
    }

    private void ValidateBuildingModelSpec(JsonElement root, string file)
    {
        if (!TrySpec(root, "BuildingModelSpec", file, out var spec))
        {
            return;
        }

        Check(StrOf(spec, "FinishedModelName") == "#Finished", file, "BuildingModelSpec.FinishedModelName must be '#Finished'");
        Check(StrOf(spec, "UnfinishedModelName") == "#Unfinished", file, "BuildingModelSpec.UnfinishedModelName must be '#Unfinished'");
        Check(StrOf(spec, "ConstructionModeModel") == "Finished", file, "BuildingModelSpec.ConstructionModeModel must be 'Finished'");
        Check(StrOf(spec, "FinishedUncoveredModelName") == string.Empty, file,
            "BuildingModelSpec.FinishedUncoveredModelName must be empty");
        Check(StrOf(spec, "UndergroundModelName") == string.Empty, file,
            "BuildingModelSpec.UndergroundModelName must be empty");
        Check(IntOf(spec, "UndergroundModelDepth") == 0, file, "BuildingModelSpec.UndergroundModelDepth must be 0");
    }

    private void ValidateBlockObjectSpec(JsonElement root, string file, TierExpectation expected)
    {
        if (!TrySpec(root, "BlockObjectSpec", file, out var spec))
        {
            return;
        }

        var x = IntOf(spec, "Size", "X") ?? int.MinValue;
        var y = IntOf(spec, "Size", "Y") ?? int.MinValue;
        var z = IntOf(spec, "Size", "Z") ?? int.MinValue;
        Check(x == expected.SizeX && y == expected.SizeY && z == expected.SizeZ, file,
            "BlockObjectSpec.Size must be {" + expected.SizeX + "," + expected.SizeY + "," + expected.SizeZ
            + "} (TierCatalog), found {" + Show(x) + "," + Show(y) + "," + Show(z) + "}");

        if (x <= 0 || y <= 0 || z <= 0)
        {
            return;
        }

        var blocks = ArrOf(spec, "Blocks");
        var total = x * y * z;
        var count = blocks?.GetArrayLength() ?? 0;
        Check(count == total, file,
            "BlockObjectSpec.Blocks length must be X*Y*Z = " + total + ", found " + count);

        var solid = 0;
        var walkable = 0;
        var index = 0;
        if (blocks is { } blockArray && count == total)
        {
            foreach (var block in blockArray.EnumerateArray())
            {
                index++;
                if (block.ValueKind != JsonValueKind.Object)
                {
                    _report.Error("blueprint", file + ": BlockObjectSpec.Blocks[" + (index - 1) + "] must be an object");
                    continue;
                }

                var matter = StrOf(block, "MatterBelow");
                if (matter is null || !BlockMatterBelow.Contains(matter))
                {
                    _report.Error("blueprint", file + ": Blocks[" + (index - 1) + "].MatterBelow must be one of "
                                                    + string.Join(", ", BlockMatterBelow) + ", found " + Show(matter));
                }
                else if (matter == "GroundOrStackable")
                {
                    solid++;
                }

                var occupations = StrOf(block, "Occupations");
                if (string.IsNullOrEmpty(occupations))
                {
                    _report.Error("blueprint", file + ": Blocks[" + (index - 1) + "].Occupations must not be empty");
                }
                else if (occupations != "None")
                {
                    walkable++;
                }
            }
        }

        Check(solid > 0, file, "BlockObjectSpec.Blocks has no solid (GroundOrStackable) block: the building needs a base");
        Check(solid < total, file,
            "BlockObjectSpec.Blocks must not be a fully solid footprint (API_FACTS section 5: no vanilla building is)");
        Check(walkable > 0, file, "BlockObjectSpec.Blocks has no walkable block");

        var baseZ = IntOf(spec, "BaseZ");
        Check(baseZ == 1, file, "BlockObjectSpec.BaseZ must be 1 (the pump sits one level above the water), found " + Show(baseZ));
        Check(BoolOf(spec, "Flippable") == true, file, "BlockObjectSpec.Flippable must be true (REQ-BLD-05)");
        Check(BoolOf(spec, "Overridable") == false, file, "BlockObjectSpec.Overridable must be false");

        if (!TryObject(spec, "Entrance", out var entrance))
        {
            _report.Error("blueprint", file + ": BlockObjectSpec.Entrance is missing");
            return;
        }

        Check(BoolOf(entrance, "HasEntrance") == true, file, "Entrance.HasEntrance must be true (REQ-BLD-04)");

        var ex = IntOf(entrance, "Coordinates", "X");
        var ey = IntOf(entrance, "Coordinates", "Y");
        var ez = IntOf(entrance, "Coordinates", "Z");
        Check(ex is >= 0 && ex < x, file, "Entrance.Coordinates.X must be inside [0," + (x - 1) + "], found " + Show(ex));
        Check(ey == -1, file, "Entrance.Coordinates.Y must be -1 (the tile just outside the front face), found " + Show(ey));
        Check(ez == baseZ, file, "Entrance.Coordinates.Z must equal BaseZ (" + Show(baseZ) + "), found " + Show(ez));

        if (ex is { } entranceX && ez is { } entranceZ && blocks is { } array && count == total)
        {
            var cell = (entranceZ * y * x) + (0 * x) + entranceX;
            if (cell >= 0 && cell < count)
            {
                var block = array[cell];
                var occupations = StrOf(block, "Occupations") ?? string.Empty;
                var passable = occupations == "All"
                               || occupations.Contains("Path", StringComparison.Ordinal)
                               || occupations.Contains("Floor", StringComparison.Ordinal);
                Check(passable, file,
                    "the entrance cell at (X=" + entranceX + ", Y=0, Z=" + entranceZ + ") must be walkable "
                    + "(Path/Floor/All), found '" + occupations + "'");
            }
        }
    }

    private void ValidateAccessSpec(JsonElement root, string file, TierExpectation expected)
    {
        if (!TrySpec(root, "BuildingAccessibleSpec", file, out var spec))
        {
            return;
        }

        if (!TryObject(spec, "LocalAccess", out var access))
        {
            _report.Error("blueprint", file + ": BuildingAccessibleSpec.LocalAccess is missing");
            return;
        }

        var ax = DblOf(access, "X");
        var ay = DblOf(access, "Y");
        var az = DblOf(access, "Z");
        var centre = expected.SizeX / 2.0;
        Check(ax is { } accessX && Math.Abs(accessX - centre) <= 0.51, file,
            "LocalAccess.X must be the pickup-face centre (" + Show(centre) + "), found " + Show(ax));
        Check(ay == 1.0, file, "LocalAccess.Y must be 1.0 (the deck level), found " + Show(ay));
        Check(az is { } accessZ && accessZ >= 0.0 && accessZ <= 1.0, file,
            "LocalAccess.Z must be inside the entrance depth cell [0,1], found " + Show(az));
        Check(BoolOf(spec, "ForceOneFinalAccess") == false, file, "BuildingAccessibleSpec.ForceOneFinalAccess must be false");
    }

    private void ValidatePlaceableSpec(JsonElement root, string file, string tier, TierExpectation expected)
    {
        if (!TrySpec(root, "PlaceableBlockObjectSpec", file, out var spec))
        {
            return;
        }

        var group = StrOf(spec, "ToolGroupId");
        Check(group == _facts.ToolGroupId, file,
            "PlaceableBlockObjectSpec.ToolGroupId must be '" + _facts.ToolGroupId + "', found " + Show(group));

        var order = IntOf(spec, "ToolOrder");
        var wantedOrder = 10 * (Array.IndexOf(SourceFacts.TierTokens, tier) + 1);
        Check(order == wantedOrder, file,
            "PlaceableBlockObjectSpec.ToolOrder must be " + wantedOrder + ", found " + Show(order));

        Check(StrOf(spec, "ToolShape") == "Square", file, "PlaceableBlockObjectSpec.ToolShape must be 'Square'");
        Check(StrOf(spec, "Layout") == "Single", file, "PlaceableBlockObjectSpec.Layout must be 'Single'");
        Check(BoolOf(spec, "CustomPivot", "HasCustomPivot") == false, file,
            "PlaceableBlockObjectSpec.CustomPivot.HasCustomPivot must be false");
        Check(BoolOf(spec, "CanBeAttachedToTerrainSide") == false, file,
            "PlaceableBlockObjectSpec.CanBeAttachedToTerrainSide must be false");
        Check(BoolOf(spec, "DevModeTool") == false, file, "PlaceableBlockObjectSpec.DevModeTool must be false");
        _ = expected;
    }

    private void ValidateLabeledEntitySpec(JsonElement root, string file, string tier, string faction)
    {
        if (!TrySpec(root, "LabeledEntitySpec", file, out var spec))
        {
            return;
        }

        Check(StrOf(spec, "DisplayNameLocKey") == "Building.MWP.Pump." + tier + ".DisplayName", file,
            "LabeledEntitySpec.DisplayNameLocKey must be 'Building.MWP.Pump." + tier + ".DisplayName'");
        Check(StrOf(spec, "DescriptionLocKey") == "Building.MWP.Pump." + tier + ".Description", file,
            "LabeledEntitySpec.DescriptionLocKey must be 'Building.MWP.Pump." + tier + ".Description'");
        Check(StrOf(spec, "FlavorDescriptionLocKey") == "Building.MWP.Pump.Flavor", file,
            "LabeledEntitySpec.FlavorDescriptionLocKey must be 'Building.MWP.Pump.Flavor'");

        var icon = StrOf(spec, "Icon");
        var wantedIcon = "Icons/MWP" + tier + faction;
        Check(icon == wantedIcon, file,
            "LabeledEntitySpec.Icon must be '" + wantedIcon + "' (CONTRACT section 7), found " + Show(icon));
    }

    private void ValidateConstructionSiteSpec(JsonElement root, string file)
    {
        if (!TrySpec(root, "ConstructionSiteProgressVisualizerSpec", file, out var spec))
        {
            return;
        }

        var thresholds = ArrOf(spec, "ProgressThresholds");
        Check(thresholds is { } array && array.GetArrayLength() == 1 && array[0].ValueKind == JsonValueKind.Number,
            file, "ConstructionSiteProgressVisualizerSpec.ProgressThresholds must be [0.0]");
    }

    private void ValidateMechanicalSpecs(JsonElement root, string file, TierExpectation expected)
    {
        Check(TrySpec(root, "MechanicalBuildingSpec", file, out _), file, "missing MechanicalBuildingSpec");

        if (TrySpec(root, "MechanicalNodeSpec", file, out var node))
        {
            Check(IntOf(node, "PowerOutput") == 0, file, "MechanicalNodeSpec.PowerOutput must be 0");
            var power = IntOf(node, "PowerInput");
            Check(power == expected.PowerRequiredHp, file,
                "MechanicalNodeSpec.PowerInput must be " + expected.PowerRequiredHp + " hp, found " + Show(power));
            Check(BoolOf(node, "IsShaft") == false, file, "MechanicalNodeSpec.IsShaft must be false");
        }

        if (TrySpec(root, "MechanicalNodeAnimatorSpec", file, out var animator))
        {
            Check(DblOf(animator, "MinSpeedMultiplier") == 0.0, file,
                "MechanicalNodeAnimatorSpec.MinSpeedMultiplier must be 0.0");
        }

        Check(TrySpec(root, "MechanicalConnectorTargetSpec", file, out _), file, "missing MechanicalConnectorTargetSpec");

        if (TrySpec(root, "TransputProviderSpec", file, out var provider))
        {
            Check(BoolOf(provider, "IgnoreRotation") == false, file, "TransputProviderSpec.IgnoreRotation must be false");
            var transputs = ArrOf(provider, "Transputs");
            Check(transputs is { } list && list.GetArrayLength() == 3, file,
                "TransputProviderSpec.Transputs must contain the three vanilla MechanicalPump ports (left, right, bottom)");
        }
    }

    private void ValidateWaterSpecs(JsonElement root, string file, TierExpectation expected, string faction)
    {
        if (TrySpec(root, "WaterInputSpec", file, out var input))
        {
            if (TryObject(input, "WaterInputCoordinates", out var coordinates))
            {
                var ix = IntOf(coordinates, "X");
                var iy = IntOf(coordinates, "Y");
                var iz = IntOf(coordinates, "Z");
                Check(ix is >= 0 && ix < expected.SizeX, file,
                    "WaterInputSpec.WaterInputCoordinates.X must be inside [0," + (expected.SizeX - 1) + "], found " + Show(ix));
                Check(iy == expected.SizeY - 1, file,
                    "WaterInputSpec.WaterInputCoordinates.Y must be the depth face opposite the entrance ("
                    + (expected.SizeY - 1) + "), found " + Show(iy));
                Check(iz == -1, file,
                    "WaterInputSpec.WaterInputCoordinates.Z must be -1 (water surface one level below the base), found " + Show(iz));
            }
            else
            {
                _report.Error("blueprint", file + ": WaterInputSpec.WaterInputCoordinates is missing");
            }
        }

        if (TrySpec(root, "WaterInputPipeSpec", file, out var pipe))
        {
            var path = StrOf(pipe, "PipeSegmentPrefabPath");
            var wanted = PipeTable[faction];
            Check(path == wanted, file,
                "WaterInputPipeSpec.PipeSegmentPrefabPath must be '" + wanted + "' (CONTRACT section 6), found " + Show(path));
            Check(StrOf(pipe, "PipeParentName") == "#Finished", file,
                "WaterInputPipeSpec.PipeParentName must be '#Finished'");
            Check(IntOf(pipe, "MaxDepth") == expected.MaxDepth, file,
                "WaterInputPipeSpec.MaxDepth must be " + expected.MaxDepth + " (TierCatalog), found " + Show(IntOf(pipe, "MaxDepth")));
            if (path is not null && _game.Available)
            {
                Check(_game.HasPipeSegment(path), file,
                    "PipeSegmentPrefabPath '" + path + "' does not exist in the game's Blueprints.zip");
                CheckFaction(path, faction, file, "PipeSegmentPrefabPath");
            }
        }

        if (TrySpec(root, "WaterObstacleSpec", file, out var obstacle))
        {
            var array = ArrOf(obstacle, "Coordinates");
            Check(array is { } coordinates && coordinates.GetArrayLength() == expected.SizeX, file,
                "WaterObstacleSpec.Coordinates must cover the whole intake row (" + expected.SizeX + " cells)");
            if (array is { } cells)
            {
                var seen = new HashSet<int>();
                foreach (var cell in cells.EnumerateArray())
                {
                    var ox = IntOf(cell, "X");
                    var oy = IntOf(cell, "Y");
                    if (ox is { } cellX && cellX >= 0 && cellX < expected.SizeX && oy == expected.SizeY - 1)
                    {
                        seen.Add(cellX);
                    }
                    else
                    {
                        _report.Error("blueprint", file + ": WaterObstacleSpec.Coordinates must use the intake row (X in [0,"
                                                        + (expected.SizeX - 1) + "], Y=" + (expected.SizeY - 1) + ")");
                    }
                }

                Check(seen.Count == expected.SizeX, file,
                    "WaterObstacleSpec.Coordinates must cover every X of the intake row");
            }
        }

        if (TrySpec(root, "FinishableWaterObstacleSpec", file, out var finishable))
        {
            Check(DblOf(finishable, "Height") == 1.0, file, "FinishableWaterObstacleSpec.Height must be 1.0");
        }
    }

    private void ValidateOutputSpec(JsonElement root, string file, TierExpectation expected)
    {
        if (!TrySpec(root, "SimpleOutputInventorySpec", file, out var spec))
        {
            return;
        }

        var capacity = IntOf(spec, "Capacity");
        Check(capacity == expected.InternalCapacity, file,
            "SimpleOutputInventorySpec.Capacity must be " + expected.InternalCapacity + " (TierCatalog), found " + Show(capacity));
        Check(BoolOf(spec, "IgnorableCapacity") == false, file, "SimpleOutputInventorySpec.IgnorableCapacity must be false");
    }

    private void ValidateCustomSpec(JsonElement root, string file, string tier, TierExpectation expected)
    {
        if (!TrySpec(root, _facts.PumpSpecKey, file, out var spec))
        {
            return;
        }

        Check(StrOf(spec, "Tier") == tier, file, _facts.PumpSpecKey + ".Tier must be '" + tier + "', found " + Show(StrOf(spec, "Tier")));

        var water = DblOf(spec, "WaterPerHour");
        Check(water is { } rate && Math.Abs(rate - expected.WaterPerHour) < 1e-9, file,
            _facts.PumpSpecKey + ".WaterPerHour must be " + Show(expected.WaterPerHour) + " (TierCatalog), found " + Show(water));

        Check(IntOf(spec, "PowerRequiredHp") == expected.PowerRequiredHp, file,
            _facts.PumpSpecKey + ".PowerRequiredHp must be " + expected.PowerRequiredHp + " (TierCatalog), found "
            + Show(IntOf(spec, "PowerRequiredHp")));

        Check(IntOf(spec, "InternalCapacity") == expected.InternalCapacity, file,
            _facts.PumpSpecKey + ".InternalCapacity must be " + expected.InternalCapacity + " (TierCatalog), found "
            + Show(IntOf(spec, "InternalCapacity")));

        Check(IntOf(spec, "MaxDepth") == expected.MaxDepth, file,
            _facts.PumpSpecKey + ".MaxDepth must be " + expected.MaxDepth + " (TierCatalog), found " + Show(IntOf(spec, "MaxDepth")));

        var badwater = DblOf(spec, "BadwaterPenalty");
        Check(badwater == 1.0, file, _facts.PumpSpecKey + ".BadwaterPenalty must default to 1.0 (DV-01), found " + Show(badwater));

        var minQuality = DblOf(spec, "MinWaterQuality");
        Check(minQuality == 0.0, file, _facts.PumpSpecKey + ".MinWaterQuality must default to 0.0 (DV-01), found " + Show(minQuality));

        var cubic = DblOf(spec, "CubicMetersPerWater");
        Check(cubic == _facts.CubicMetersPerWater, file,
            _facts.PumpSpecKey + ".CubicMetersPerWater must be " + Show(_facts.CubicMetersPerWater) + ", found " + Show(cubic));

        var interval = DblOf(spec, "MaintenanceIntervalDays");
        Check(interval is { } days && Math.Abs(days - expected.MaintenanceIntervalDays) < 1e-9, file,
            _facts.PumpSpecKey + ".MaintenanceIntervalDays must be " + Show(expected.MaintenanceIntervalDays)
            + " (TierCatalog), found " + Show(interval));

        var duration = DblOf(spec, "MaintenanceDurationHours");
        Check(duration == 2.0, file, _facts.PumpSpecKey + ".MaintenanceDurationHours must be 2.0 (REQ-MAINT-04), found " + Show(duration));

        var materials = GoodsOf(spec, "MaintenanceMaterials");
        Check(GoodsEqual(materials, expected.MaintenanceMaterials), file,
            _facts.PumpSpecKey + ".MaintenanceMaterials must be " + FormatGoods(expected.MaintenanceMaterials)
            + ", found " + FormatGoods(materials));
    }

    private void ValidateChildren(JsonElement root, string file, string tier, string faction, TierExpectation expected)
    {
        if (!TryObject(root, "Children", out var children))
        {
            _report.Error("blueprint", file + ": Children is missing");
            return;
        }

        // T7: each tier ships its own Blender assets under data/Buildings/Water/MWP/.
        var model = ModModelPrefix + "MWP.Pump." + tier + "." + faction + ".Model";
        var stageModel = ModModelPrefix + "MWP.Pump." + tier + "." + faction + ".ConstructionStage0.Model";

        if (TryObject(children, "#Finished", out var finished))
        {
            if (TryObject(finished, "TimbermeshSpec", out var mesh))
            {
                var path = StrOf(mesh, "Model");
                Check(path == model, file,
                    "Children.#Finished.TimbermeshSpec.Model must be the mod-local '" + model + "', found " + Show(path));
                if (path is not null)
                {
                    CheckModelPath(path, faction, file, "TimbermeshSpec.Model", allowVanilla: true);
                }
            }
            else
            {
                _report.Error("blueprint", file + ": Children.#Finished.TimbermeshSpec is missing");
            }

            if (TryObject(finished, "CollidersSpec", out var colliders))
            {
                ValidateColliders(colliders, file, "Children.#Finished.CollidersSpec", expected);
            }
            else
            {
                _report.Error("blueprint", file + ": Children.#Finished.CollidersSpec is missing");
            }
        }
        else
        {
            _report.Error("blueprint", file + ": Children.#Finished is missing");
        }

        if (!TryObject(children, "#Unfinished", out var unfinished)
            || !TryObject(unfinished, "Children", out var unfinishedChildren))
        {
            _report.Error("blueprint", file + ": Children.#Unfinished.Children is missing");
            return;
        }

        var baseName = "ConstructionBase" + expected.SizeX + "x" + expected.SizeY;
        var basePath = "ConstructionBases/" + baseName + "/" + baseName + ".blueprint";
        if (TryObject(unfinishedChildren, baseName + "#nested", out var constructionBase))
        {
            var path = StrOf(constructionBase, "BlueprintPath");
            Check(path == basePath, file,
                "ConstructionBase child '" + baseName + "#nested' must reference '" + basePath + "', found " + Show(path));
            if (path is not null && _game.Available)
            {
                Check(_game.HasBlueprint(path), file,
                    "ConstructionBase blueprint '" + path + "' does not exist in the game's Blueprints.zip");
            }
        }
        else
        {
            _report.Error("blueprint", file + ": missing ConstructionBase child '" + baseName + "#nested' (CONTRACT section 4)");
        }

        if (TryObject(unfinishedChildren, "ConstructionStage0", out var stage))
        {
            if (TryObject(stage, "TimbermeshSpec", out var stageMesh))
            {
                var path = StrOf(stageMesh, "Model");
                Check(path == stageModel, file,
                    "Children.#Unfinished ConstructionStage0 model must be the mod-local '" + stageModel + "', found " + Show(path));
                if (path is not null)
                {
                    CheckModelPath(path, faction, file, "ConstructionStage0.TimbermeshSpec.Model", allowVanilla: true);
                }
            }
            else
            {
                _report.Error("blueprint", file + ": ConstructionStage0.TimbermeshSpec is missing");
            }

            if (TryObject(stage, "CollidersSpec", out var stageColliders))
            {
                ValidateColliders(stageColliders, file, "ConstructionStage0.CollidersSpec", expected);
            }
            else
            {
                _report.Error("blueprint", file + ": ConstructionStage0.CollidersSpec is missing");
            }
        }
        else
        {
            _report.Error("blueprint", file + ": missing ConstructionStage0 child (CONTRACT section 4)");
        }

        ReportModelFiles(file, model, stageModel);
    }

    /// <summary>
    /// T7: model paths under <c>Buildings/Water/MWP/</c> are the mod's own .timbermesh files, so
    /// they are checked on disk (exists, non-empty) instead of against the vanilla archive.
    /// Vanilla paths (used before T7 and still legal) keep the archive check.
    /// </summary>
    private void CheckModelPath(string path, string faction, string file, string what, bool allowVanilla)
    {
        if (path.StartsWith(ModModelPrefix, StringComparison.Ordinal))
        {
            var timbermesh = LocalModelFile(path);
            if (!File.Exists(timbermesh))
            {
                _report.Error("models", file + ": " + what + " '" + path + "' has no model file "
                                        + "'" + RelativeToData(timbermesh) + "' (mod-local .timbermesh is missing)");
            }
            else
            {
                var length = new FileInfo(timbermesh).Length;
                Check(length > 0, "models", file + ": " + what + " model file '"
                                          + RelativeToData(timbermesh) + "' is empty");
            }

            CheckFaction(path, faction, file, what);
            return;
        }

        if (!allowVanilla)
        {
            _report.Error("models", file + ": " + what + " '" + path + "' is neither mod-local (" + ModModelPrefix
                                    + ") nor allowed here");
            return;
        }

        if (_game.Available)
        {
            Check(_game.HasModelPath(path), file,
                "model path '" + path + "' does not exist in the game's Blueprints.zip");
            CheckFaction(path, faction, file, what);
        }
    }

    /// <summary>Collider rule (T7): one box exactly covering the footprint, any extra box stays inside it.</summary>
    private void ValidateColliders(JsonElement colliders, string file, string location, TierExpectation expected)
    {
        var width = expected.SizeX;
        var height = expected.SizeZ;
        var depth = expected.SizeY;
        var boxes = ArrOf(colliders, "BoxColliders");
        var count = boxes?.GetArrayLength() ?? 0;
        Check(count >= 1, file, location + ".BoxColliders must not be empty");

        var primary = false;
        var index = 0;
        if (boxes is { } array)
        {
            foreach (var box in array.EnumerateArray())
            {
                var label = location + ".BoxColliders[" + index + "]";
                index++;
                if (!TryObject(box, "Center", out var center) || !TryObject(box, "Size", out var size))
                {
                    _report.Error("blueprint", file + ": " + label + " needs Center and Size");
                    continue;
                }

                var cx = DblOf(center, "X");
                var cy = DblOf(center, "Y");
                var cz = DblOf(center, "Z");
                var sx = DblOf(size, "X");
                var sy = DblOf(size, "Y");
                var sz = DblOf(size, "Z");
                if (cx is not { } centerX || cy is not { } centerY || cz is not { } centerZ
                    || sx is not { } sizeX || sy is not { } sizeY || sz is not { } sizeZ)
                {
                    _report.Error("blueprint", file + ": " + label + " Center/Size need numeric X/Y/Z");
                    continue;
                }

                Check(sizeX > 0.0 && sizeY > 0.0 && sizeZ > 0.0, file, label + " Size must be positive");
                var inside = centerX - (sizeX / 2.0) >= -0.01
                             && centerX + (sizeX / 2.0) <= width + 0.01
                             && centerY - (sizeY / 2.0) >= -0.01
                             && centerY + (sizeY / 2.0) <= height + 0.01
                             && centerZ - (sizeZ / 2.0) >= -0.01
                             && centerZ + (sizeZ / 2.0) <= depth + 0.01;
                Check(inside, file,
                    label + " must stay inside the footprint (width " + width + ", height " + height
                    + ", depth " + depth + ")");

                if (Near(sizeX, width) && Near(sizeY, height) && Near(sizeZ, depth)
                    && Near(centerX, width / 2.0) && Near(centerY, height / 2.0) && Near(centerZ, depth / 2.0))
                {
                    primary = true;
                }
            }
        }

        Check(primary, file,
            location + ".BoxColliders must contain the primary box Center={X:" + Show(width / 2.0)
            + ", Y:" + Show(height / 2.0) + ", Z:" + Show(depth / 2.0) + "} Size={X:" + Show((double)width)
            + ", Y:" + Show((double)height) + ", Z:" + Show((double)depth) + "} (T7 collider rule)");
    }

    private static bool Near(double value, double expected) => Math.Abs(value - expected) <= 0.011;

    private string LocalModelFile(string path) =>
        Path.Combine(_dataDir, path.Replace('/', Path.DirectorySeparatorChar) + ".timbermesh");

    private string RelativeToData(string path) => Path.GetRelativePath(_dataDir, path).Replace('\\', '/');

    private void ReportModelFiles(string blueprintFile, string model, string stageModel)
    {
        Console.WriteLine("  " + blueprintFile);
        Console.WriteLine("    finished: " + model + " -> " + DescribeModelFile(model));
        Console.WriteLine("    stage   : " + stageModel + " -> " + DescribeModelFile(stageModel));
    }

    private string DescribeModelFile(string path)
    {
        var file = LocalModelFile(path);
        if (!File.Exists(file))
        {
            return "MISSING (" + RelativeToData(file) + ")";
        }

        return RelativeToData(file) + " (" + new FileInfo(file).Length + " bytes)";
    }

    private void CheckFaction(string path, string faction, string file, string what)
    {
        var hasOwnFaction = path.Contains("." + faction, StringComparison.Ordinal);
        var hasOtherFaction = SourceFacts.Factions
            .Where(other => !string.Equals(other, faction, StringComparison.Ordinal))
            .Any(other => path.Contains("." + other, StringComparison.Ordinal));
        Check(hasOwnFaction && !hasOtherFaction, file,
            what + " '" + path + "' must belong to the " + faction + " faction");
    }

    // ------------------------------------------------------------------ template collections
    private void ValidateTemplateCollections()
    {
        var folder = Path.Combine(_dataDir, "TemplateCollections");
        foreach (var faction in SourceFacts.Factions)
        {
            var name = "TemplateCollection.Buildings." + faction + ".blueprint.json";
            var path = Path.Combine(folder, name);
            var relative = "TemplateCollections/" + name;
            if (!File.Exists(path))
            {
                _report.Error("collection", relative + ": missing");
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                _report.Error("collection", relative + ": invalid JSON: " + ex.Message);
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (!TrySpec(root, "TemplateCollectionSpec", relative, out var spec))
                {
                    continue;
                }

                var collectionId = StrOf(spec, "CollectionId");
                Check(collectionId == "Buildings." + faction, relative,
                    "TemplateCollectionSpec.CollectionId must be 'Buildings." + faction + "', found " + Show(collectionId));

                var append = ArrOf(spec, "Blueprints#append");
                if (append is not { } list)
                {
                    _report.Error("collection", relative + ": TemplateCollectionSpec.'Blueprints#append' must be an array");
                    continue;
                }

                var expected = SourceFacts.TierTokens
                    .Select(tier => "Buildings/Water/MWP/MWP.Pump." + tier + "." + faction + ".blueprint")
                    .ToList();
                var actual = list.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToList();

                Check(actual.Count == expected.Count, relative,
                    "'Blueprints#append' must list the 4 " + faction + " blueprints, found " + actual.Count);
                foreach (var wanted in expected)
                {
                    if (!actual.Contains(wanted, StringComparer.Ordinal))
                    {
                        _report.Error("collection", relative + ": 'Blueprints#append' is missing '" + wanted + "'");
                    }
                }

                foreach (var reference in actual)
                {
                    var file = Path.Combine(_dataDir, reference.Replace('/', Path.DirectorySeparatorChar) + ".json");
                    Check(File.Exists(file), relative,
                        "'Blueprints#append' references '" + reference + "' but that file does not exist");
                }
            }
        }

        foreach (var file in Directory.GetFiles(folder, "*.blueprint.json"))
        {
            var fileName = Path.GetFileName(file);
            var known = SourceFacts.Factions
                .Select(faction => "TemplateCollection.Buildings." + faction + ".blueprint.json")
                .Contains(fileName, StringComparer.Ordinal);
            if (!known)
            {
                _report.Error("collection", "TemplateCollections/" + fileName + ": unexpected template collection");
            }
        }
    }

    // ------------------------------------------------------------------ tool group
    /// <summary>
    /// Validates the spec-only <c>BlockObjectToolGroupSpec</c> blueprint that puts the four tiers in
    /// their own collapsible bottom-bar group (the vanilla mechanism: one icon that expands to the
    /// group's tools). Every value is read from the Core constants (<see cref="SourceFacts"/>), so
    /// the JSON and the code cannot drift.
    /// </summary>
    private void ValidateToolGroup()
    {
        var path = Path.Combine(_dataDir, ToolGroupRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            _report.Error("toolgroup", ToolGroupRelativePath + ": missing (the mod's collapsible build-menu group)");
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            _report.Error("toolgroup", ToolGroupRelativePath + ": invalid JSON: " + ex.Message);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!TrySpec(root, "BlockObjectToolGroupSpec", ToolGroupRelativePath, out var spec))
            {
                return;
            }

            Check(StrOf(spec, "Id") == _facts.ToolGroupId, ToolGroupRelativePath,
                "BlockObjectToolGroupSpec.Id must be '" + _facts.ToolGroupId + "' (MwpDefaults.ToolGroupId), found "
                + Show(StrOf(spec, "Id")));
            Check(IntOf(spec, "Order") == _facts.ToolGroupOrder, ToolGroupRelativePath,
                "BlockObjectToolGroupSpec.Order must be " + _facts.ToolGroupOrder
                + " (MwpDefaults.ToolGroupOrder), found " + Show(IntOf(spec, "Order")));
            Check(StrOf(spec, "NameLocKey") == _facts.ToolGroupNameLocKey, ToolGroupRelativePath,
                "BlockObjectToolGroupSpec.NameLocKey must be '" + _facts.ToolGroupNameLocKey
                + "' (MwpLocKeys.ToolGroupName), found " + Show(StrOf(spec, "NameLocKey")));
            Check(StrOf(spec, "Icon") == _facts.ToolGroupIcon, ToolGroupRelativePath,
                "BlockObjectToolGroupSpec.Icon must be '" + _facts.ToolGroupIcon
                + "' (MwpDefaults.ToolGroupIcon), found " + Show(StrOf(spec, "Icon")));
            Check(BoolOf(spec, "FallbackGroup") == false, ToolGroupRelativePath,
                "BlockObjectToolGroupSpec.FallbackGroup must be false (MWP is a real group, not the fallback)");

            var icon = StrOf(spec, "Icon");
            if (!string.IsNullOrEmpty(icon) && !icon.StartsWith("Sprites/", StringComparison.Ordinal))
            {
                var iconPath = Path.Combine(_dataDir, icon.Replace('/', Path.DirectorySeparatorChar) + ".png");
                Check(File.Exists(iconPath), ToolGroupRelativePath,
                    "BlockObjectToolGroupSpec.Icon '" + icon + "' must resolve to a shipped sprite (Icons/... .png)");
                if (File.Exists(iconPath))
                {
                    ValidatePng(iconPath, "Icons/" + Path.GetFileName(iconPath));
                }
            }
        }
    }

    // ------------------------------------------------------------------ icons
    private void ValidateIcons()
    {
        var folder = Path.Combine(_dataDir, "Icons");
        foreach (var tier in SourceFacts.TierTokens)
        {
            foreach (var faction in SourceFacts.Factions)
            {
                var name = "MWP" + tier + faction;
                var png = Path.Combine(folder, name + ".png");
                var meta = Path.Combine(folder, name + ".png.meta.json");
                if (!File.Exists(png))
                {
                    _report.Error("icons", "Icons/" + name + ".png: missing (CONTRACT section 7)");
                    continue;
                }

                ValidatePng(png, "Icons/" + name + ".png");

                if (!File.Exists(meta))
                {
                    _report.Error("icons", "Icons/" + name + ".png.meta.json: missing");
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(meta));
                    var root = document.RootElement;
                    Check(root.ValueKind == JsonValueKind.Object && BoolOf(root, "isSprite") == true,
                        "icons", "Icons/" + name + ".png.meta.json must be {\"isSprite\": true}");
                }
                catch (JsonException ex)
                {
                    _report.Error("icons", "Icons/" + name + ".png.meta.json is not valid JSON: " + ex.Message);
                }
            }
        }

        foreach (var file in Directory.GetFiles(folder, "*.png"))
        {
            var fileName = Path.GetFileName(file);
            var known = SourceFacts.TierTokens.Any(tier => SourceFacts.Factions.Any(
                faction => fileName == "MWP" + tier + faction + ".png"));
            if (!known)
            {
                _report.Error("icons", "Icons/" + fileName + ": unexpected icon file");
            }
        }
    }

    private void ValidatePng(string path, string relative)
    {
        var header = new byte[33];
        using (var stream = File.OpenRead(path))
        {
            var read = stream.Read(header, 0, header.Length);
            if (read < 33)
            {
                _report.Error("icons", relative + ": not a valid PNG (truncated)");
                return;
            }
        }

        var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (var i = 0; i < signature.Length; i++)
        {
            if (header[i] != signature[i])
            {
                _report.Error("icons", relative + ": not a PNG file");
                return;
            }
        }

        var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        Check(width == 128 && height == 128, "icons",
            relative + " must be 128x128, found " + width + "x" + height);
    }

    // ------------------------------------------------------------------ helpers
    private void Check(bool condition, string category, string message)
    {
        if (!condition)
        {
            _report.Error(category, message);
        }
    }

    private bool TrySpec(JsonElement root, string specName, string file, out JsonElement spec)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(specName, out spec)
            && spec.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        _report.Error("blueprint", file + ": missing or non-object spec '" + specName + "'");
        spec = default;
        return false;
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static JsonElement? ArrOf(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        return null;
    }

    private static string? StrOf(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static int? IntOf(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? IntOf(JsonElement parent, string first, string second)
    {
        if (TryObject(parent, first, out var nested))
        {
            return IntOf(nested, second);
        }

        return null;
    }

    private static bool? BoolOf(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return null;
    }

    private static bool? BoolOf(JsonElement parent, string first, string second)
    {
        if (TryObject(parent, first, out var nested))
        {
            return BoolOf(nested, second);
        }

        return null;
    }

    private static double? DblOf(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        return null;
    }

    private List<GoodPair> GoodsOf(JsonElement parent, string name)
    {
        var result = new List<GoodPair>();
        if (ArrOf(parent, name) is not { } array)
        {
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            var id = StrOf(item, "Id");
            var amount = IntOf(item, "Amount");
            if (id is not null && amount is { } parsed)
            {
                result.Add(new GoodPair(id, parsed));
            }
        }

        return result;
    }

    private static bool GoodsEqual(IReadOnlyList<GoodPair> actual, IReadOnlyList<GoodPair> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(actual[i].GoodId, expected[i].GoodId, StringComparison.Ordinal)
                || actual[i].Amount != expected[i].Amount)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatGoods(IReadOnlyList<GoodPair> goods) =>
        goods.Count == 0
            ? "(none)"
            : string.Join(" + ", goods.Select(good => good.Amount + " " + good.GoodId));

    private static string Show(string? value) => value is null ? "(missing)" : "'" + value + "'";

    private static string Show(int? value) => value is null ? "(missing)" : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string Show(double? value) =>
        value is null ? "(missing)" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
