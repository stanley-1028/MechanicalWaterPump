using System.Globalization;
using System.Text.Json;

namespace MWP.Tools.Validate;

/// <summary>
/// Validates <c>MWP_Config.json</c> against CONTRACT section 9 / requirements 20.6 and against the
/// Core tier table: schema 2, documented defaults, value ranges (REQ-SAFE-01) and no unknown keys.
/// </summary>
internal sealed class ConfigValidator
{
    private static readonly string[] Features =
    {
        "pipeAdapter", "directPipeOutput", "uiDiagnostics",
    };

    private readonly SourceFacts _facts;
    private readonly Report _report;

    public ConfigValidator(SourceFacts facts, Report report)
    {
        _facts = facts;
        _report = report;
    }

    public void Validate(string dataDir)
    {
        var path = Path.Combine(dataDir, _facts.ConfigFileName);
        if (!File.Exists(path))
        {
            _report.Error("config", "data/" + _facts.ConfigFileName + " is missing (CONTRACT section 9)");
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            _report.Error("config", _facts.ConfigFileName + " is not valid JSON: " + ex.Message);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _report.Error("config", _facts.ConfigFileName + " root must be an object");
                return;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!new[] { "configSchemaVersion", "features", "balance", "maintenance", "compat" }
                        .Contains(property.Name, StringComparer.Ordinal))
                {
                    _report.Warn("config", "unknown top-level key '" + property.Name + "' is ignored by the codec");
                }
            }

            ValidateSchemaVersion(root);
            ValidateFeatures(root);
            ValidateBalance(root);
            ValidateMaintenance(root);
            ValidateCompat(root);
        }
    }

    private void ValidateSchemaVersion(JsonElement root)
    {
        var version = IntOf(root, "configSchemaVersion");
        if (version != _facts.ConfigSchemaVersion)
        {
            _report.Error("config", "'configSchemaVersion' must be " + _facts.ConfigSchemaVersion
                                    + " (CONTRACT section 9), found " + Show(version));
        }
    }

    private void ValidateFeatures(JsonElement root)
    {
        if (!TryObject(root, "features", out var features))
        {
            _report.Error("config", "missing 'features' object");
            return;
        }

        foreach (var property in features.EnumerateObject())
        {
            if (!Features.Contains(property.Name, StringComparer.Ordinal))
            {
                _report.Warn("config", "unknown key 'features." + property.Name + "' is ignored by the codec");
            }
        }

        CheckBoolean(features, "pipeAdapter", documented: true);
        CheckBoolean(features, "directPipeOutput", documented: false);
        CheckBoolean(features, "uiDiagnostics", documented: false);
    }

    private void CheckBoolean(JsonElement parent, string name, bool documented)
    {
        var value = BoolOf(parent, name);
        if (value is null)
        {
            _report.Error("config", "features." + name + " must be a boolean");
            return;
        }

        if (value != documented)
        {
            _report.Warn("config", "features." + name + " is " + (value.Value ? "true" : "false")
                                   + " but CONTRACT section 9 documents " + (documented ? "true" : "false"));
        }
    }

    private void ValidateBalance(JsonElement root)
    {
        if (!TryObject(root, "balance", out var balance))
        {
            _report.Error("config", "missing 'balance' object");
            return;
        }

        CheckBoolValue(balance, "useLegacyRates", false);
        CheckDoubleValue(balance, "badwaterPenalty", 1.0);
        CheckDoubleValue(balance, "minWaterQuality", 0.0);
        CheckDoubleValue(balance, "cubicMetersPerWater", _facts.CubicMetersPerWater);
        CheckDoubleValue(balance, "tickIntervalSeconds", _facts.TickIntervalSeconds);

        if (!TryArray(balance, "tiers", out var tiers))
        {
            _report.Error("config", "missing 'balance.tiers' array (four entries)");
            return;
        }

        if (tiers.GetArrayLength() != SourceFacts.TierTokens.Length)
        {
            _report.Error("config", "'balance.tiers' must have exactly 4 entries, found " + tiers.GetArrayLength());
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var entry in tiers.EnumerateArray())
        {
            var location = "balance.tiers[" + index + "]";
            index++;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                _report.Error("config", location + " must be an object");
                continue;
            }

            var tier = StrOf(entry, "tier");
            if (tier is null)
            {
                _report.Error("config", location + ".tier must be one of Basic/Intermediate/Advanced/Top");
                continue;
            }

            if (!seen.Add(tier))
            {
                _report.Error("config", "duplicate tier '" + tier + "' in 'balance.tiers'");
                continue;
            }

            if (!_facts.Tiers.TryGetValue(tier, out var expected))
            {
                _report.Error("config", location + ".tier '" + tier + "' is not in TierCatalog");
                continue;
            }

            var water = DblOf(entry, "waterPerHour");
            if (water is not { } rate || rate <= 0.0)
            {
                _report.Error("config", location + ".waterPerHour must be > 0");
            }
            else if (Math.Abs(rate - expected.WaterPerHour) < 1e-9)
            {
                // matches TierCatalog
            }
            else
            {
                _report.Warn("config", location + ".waterPerHour (" + Show(rate)
                                       + ") differs from TierCatalog (" + Show(expected.WaterPerHour) + ")");
            }

            var power = IntOf(entry, "powerRequired");
            if (power is null || power.Value < 0)
            {
                _report.Error("config", location + ".powerRequired must be >= 0");
            }
            else if (power.Value != expected.PowerRequiredHp)
            {
                _report.Warn("config", location + ".powerRequired (" + power.Value
                                       + ") differs from TierCatalog (" + expected.PowerRequiredHp + ")");
            }

            var capacity = IntOf(entry, "internalCapacity");
            if (capacity is null || capacity.Value < 1)
            {
                _report.Error("config", location + ".internalCapacity must be >= 1");
            }
            else if (capacity.Value != expected.InternalCapacity)
            {
                _report.Warn("config", location + ".internalCapacity (" + capacity.Value
                                       + ") differs from TierCatalog (" + expected.InternalCapacity + ")");
            }

            var depth = IntOf(entry, "maxDepth");
            if (depth is null || depth.Value < 1)
            {
                _report.Error("config", location + ".maxDepth must be >= 1");
            }
            else if (depth.Value != expected.MaxDepth)
            {
                _report.Warn("config", location + ".maxDepth (" + depth.Value
                                       + ") differs from TierCatalog (" + expected.MaxDepth + ")");
            }
        }
    }

    private void ValidateMaintenance(JsonElement root)
    {
        if (!TryObject(root, "maintenance", out var maintenance))
        {
            _report.Error("config", "missing 'maintenance' object");
            return;
        }

        CheckBoolValue(maintenance, "enabled", true);
        CheckBoolValue(maintenance, "auto", true);

        var threshold = DblOf(maintenance, "autoThreshold");
        if (threshold != 50.0)
        {
            _report.Warn("config", "maintenance.autoThreshold is " + Show(threshold)
                                   + " but requirements 20.2.3 documents 50");
        }

        var duration = DblOf(maintenance, "durationHours");
        if (duration is not { } hours || hours < 0.0)
        {
            _report.Error("config", "maintenance.durationHours must be >= 0");
        }
        else if (hours != 2.0)
        {
            _report.Warn("config", "maintenance.durationHours is " + Show(hours) + " but REQ-MAINT-04 requires 2");
        }

        if (!TryArray(maintenance, "intervalsDays", out var intervals))
        {
            _report.Error("config", "missing 'maintenance.intervalsDays' array (four entries)");
        }
        else
        {
            var values = new List<double>();
            foreach (var item in intervals.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number)
                {
                    _report.Error("config", "'maintenance.intervalsDays' entries must be numbers");
                    break;
                }

                var value = item.GetDouble();
                if (value < 1.0)
                {
                    _report.Error("config", "'maintenance.intervalsDays' entries must be >= 1 (REQ-SAFE-01)");
                }

                values.Add(value);
            }

            var expectedIntervals = SourceFacts.TierTokens
                .Where(_facts.Tiers.ContainsKey)
                .Select(tier => _facts.Tiers[tier].MaintenanceIntervalDays)
                .ToList();
            if (values.Count != expectedIntervals.Count)
            {
                _report.Error("config", "'maintenance.intervalsDays' must have one entry per tier ("
                                        + expectedIntervals.Count + "), found " + values.Count);
            }
            else
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if (Math.Abs(values[i] - expectedIntervals[i]) >= 1e-9)
                    {
                        _report.Warn("config", "'maintenance.intervalsDays[" + i + "]' (" + Show(values[i])
                                               + ") differs from TierCatalog (" + Show(expectedIntervals[i]) + ")");
                    }
                }
            }
        }

        if (!TryArray(maintenance, "materials", out var materials))
        {
            _report.Error("config", "missing 'maintenance.materials' array (four entries)");
            return;
        }

        if (materials.GetArrayLength() != SourceFacts.TierTokens.Length)
        {
            _report.Error("config", "'maintenance.materials' must have one entry per tier, found " + materials.GetArrayLength());
        }

        var tierIndex = 0;
        foreach (var entry in materials.EnumerateArray())
        {
            var location = "maintenance.materials[" + tierIndex + "]";
            if (tierIndex < SourceFacts.TierTokens.Length
                && _facts.Tiers.TryGetValue(SourceFacts.TierTokens[tierIndex], out var expected)
                && entry.ValueKind == JsonValueKind.Array)
            {
                var actual = GoodsOf(entry);
                if (!GoodsEqual(actual, expected.MaintenanceMaterials))
                {
                    _report.Warn("config", location + " is " + FormatGoods(actual) + " but TierCatalog documents "
                                           + FormatGoods(expected.MaintenanceMaterials));
                }
            }

            if (entry.ValueKind == JsonValueKind.Array)
            {
                foreach (var good in entry.EnumerateArray())
                {
                    var amount = IntOf(good, "amount");
                    if (amount is null || amount.Value < 0)
                    {
                        _report.Error("config", location + " material amounts must be >= 0 (REQ-SAFE-01)");
                    }

                    if (string.IsNullOrWhiteSpace(StrOf(good, "id")))
                    {
                        _report.Error("config", location + " material entries need a non-empty 'id'");
                    }
                }
            }

            tierIndex++;
        }
    }

    private void ValidateCompat(JsonElement root)
    {
        if (!TryObject(root, "compat", out var compat))
        {
            _report.Error("config", "missing 'compat' object");
            return;
        }

        var version = StrOf(compat, "timberPipesMinVersion");
        if (string.IsNullOrWhiteSpace(version) || !Version.TryParse(version, out _))
        {
            _report.Error("config", "'compat.timberPipesMinVersion' must be a version string");
        }

        CheckDoubleValue(compat, "logThrottleSeconds", _facts.LogThrottleSeconds);
    }

    private void CheckBoolValue(JsonElement parent, string name, bool documented)
    {
        var value = BoolOf(parent, name);
        if (value is null)
        {
            _report.Error("config", name + " must be a boolean");
            return;
        }

        if (value != documented)
        {
            _report.Warn("config", name + " is " + (value.Value ? "true" : "false")
                                   + " but CONTRACT section 9 documents " + (documented ? "true" : "false"));
        }
    }

    private void CheckDoubleValue(JsonElement parent, string name, double documented)
    {
        var value = DblOf(parent, name);
        if (value is null)
        {
            _report.Error("config", "'" + name + "' must be a number");
            return;
        }

        if (Math.Abs(value.Value - documented) >= 1e-9)
        {
            _report.Warn("config", "'" + name + "' is " + Show(value)
                                   + " but CONTRACT section 9 documents " + Show(documented));
        }
    }

    private List<GoodPair> GoodsOf(JsonElement array)
    {
        var result = new List<GoodPair>();
        foreach (var item in array.EnumerateArray())
        {
            var id = StrOf(item, "id");
            var amount = IntOf(item, "amount");
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
        goods.Count == 0 ? "(none)" : string.Join(" + ", goods.Select(good => good.Amount + " " + good.GoodId));

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

    private static bool TryArray(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
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

    private static string Show(int? value) => value is null ? "(missing)" : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string Show(double? value) =>
        value is null ? "(missing)" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
