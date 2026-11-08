using System.Globalization;
using System.Text.RegularExpressions;

namespace MWP.Tools.Validate;

/// <summary>A (good id, amount) pair read from the Core source.</summary>
internal sealed record GoodPair(string GoodId, int Amount);

/// <summary>One row of <c>TierCatalog.Default</c>, parsed from the Core source.</summary>
internal sealed record TierExpectation(
    string Tier,
    int ScienceCost,
    IReadOnlyList<GoodPair> BuildCost,
    int SizeX,
    int SizeY,
    int SizeZ,
    double WaterPerHour,
    int PowerRequiredHp,
    int MaxDepth,
    int InternalCapacity,
    int BuilderHours,
    double MaintenanceIntervalDays,
    IReadOnlyList<GoodPair> MaintenanceMaterials);

/// <summary>
/// Facts that live in C# rather than in the data folder, parsed from the Core sources so the
/// validator cannot drift from them: <c>MwpDefaults</c> / <c>MwpLocKeys</c> (mod id, file names,
/// localization key set) and <c>TierCatalog.Default</c> (the authoritative tier table).
/// </summary>
internal sealed class SourceFacts
{
    /// <summary>Tier tokens in unlock order; matches <see cref="PumpTier"/> and the blueprint ids.</summary>
    public static readonly string[] TierTokens = { "Basic", "Intermediate", "Advanced", "Top" };

    /// <summary>Faction tokens used in blueprint ids and template names.</summary>
    public static readonly string[] Factions = { "Folktails", "IronTeeth" };

    private static readonly Regex ConstPattern = new(
        "public\\s+const\\s+(?:string|int|long|double|float)\\s+(\\w+)\\s*=\\s*([^;]+);",
        RegexOptions.Compiled);

    private static readonly Regex LocTemplatePattern = new(
        "\"([^\"]+)\"\\s*\\+\\s*tierName\\s*\\+\\s*\"([^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex GoodStackPattern = new(
        "GoodStack\\(GoodStacks\\.(\\w+)\\s*,\\s*(\\d+)\\)",
        RegexOptions.Compiled);

    private readonly Dictionary<string, string> _defaults = new(StringComparer.Ordinal);

    private SourceFacts()
    {
    }

    /// <summary>Localization keys every shipped CSV must contain (parsed from MwpLocKeys).</summary>
    public IReadOnlyList<string> LocKeys { get; private set; } = Array.Empty<string>();

    /// <summary>Tier table parsed from TierCatalog.Default, keyed by tier token.</summary>
    public IReadOnlyDictionary<string, TierExpectation> Tiers { get; private set; } =
        new Dictionary<string, TierExpectation>(StringComparer.Ordinal);

    /// <summary>Mod id from MwpDefaults.ModId.</summary>
    public string ModId => GetString("ModId", "MechanicalWaterPump");

    /// <summary>Version from MwpDefaults.ModVersion.</summary>
    public string ModVersion => GetString("ModVersion", "0.2.2");

    /// <summary>Config file name from MwpDefaults.ConfigFileName.</summary>
    public string ConfigFileName => GetString("ConfigFileName", "MWP_Config.json");

    /// <summary>Custom blueprint spec JSON key from MwpDefaults.PumpSpecKey.</summary>
    public string PumpSpecKey => GetString("PumpSpecKey", "MWP.MechanicalWaterPumpSpec");

    /// <summary>Blueprint folder from MwpDefaults.BlueprintFolder.</summary>
    public string BlueprintFolder => GetString("BlueprintFolder", "Buildings/Water/MWP");

    /// <summary>Tool group from MwpDefaults.ToolGroupId.</summary>
    public string ToolGroupId => GetString("ToolGroupId", "Water");

    /// <summary>Config schema version from MwpDefaults.ConfigSchemaVersion.</summary>
    public int ConfigSchemaVersion => GetInt("ConfigSchemaVersion", 2);

    /// <summary>Tick interval from MwpDefaults.TickIntervalSeconds.</summary>
    public double TickIntervalSeconds => GetDouble("TickIntervalSeconds", 1.0);

    /// <summary>Water-body cubic metres per Water item from MwpDefaults.CubicMetersPerWater.</summary>
    public double CubicMetersPerWater => GetDouble("CubicMetersPerWater", 0.2);

    /// <summary>Log throttle from MwpDefaults.LogThrottleSeconds.</summary>
    public double LogThrottleSeconds => GetDouble("LogThrottleSeconds", 30.0);

    public static SourceFacts Load(string modRoot, Report report)
    {
        var facts = new SourceFacts();
        var defaultsPath = Path.Combine(modRoot, "src", "MWP.Core", "MwpDefaults.cs");
        var tierPath = Path.Combine(modRoot, "src", "MWP.Core", "Balance", "TierDefinition.cs");

        if (!File.Exists(defaultsPath))
        {
            report.Error("source", "cannot read " + Relative(modRoot, defaultsPath)
                                   + " (the localization key set and mod ids come from it)");
        }
        else
        {
            var text = File.ReadAllText(defaultsPath);
            var defaultsBody = ExtractBlock(text, "class MwpDefaults");
            if (defaultsBody is null)
            {
                report.Error("source", "could not find 'class MwpDefaults' in MwpDefaults.cs");
            }
            else
            {
                foreach (Match match in ConstPattern.Matches(defaultsBody))
                {
                    facts._defaults[match.Groups[1].Value] = Unquote(match.Groups[2].Value.Trim());
                }
            }

            var locBody = ExtractBlock(text, "class MwpLocKeys");
            if (locBody is null)
            {
                report.Error("source", "could not find 'class MwpLocKeys' in MwpDefaults.cs");
            }
            else
            {
                facts.LocKeys = ParseLocKeys(locBody, report);
            }
        }

        if (!File.Exists(tierPath))
        {
            report.Error("source", "cannot read " + Relative(modRoot, tierPath) + " (the authoritative tier table)");
        }
        else
        {
            facts.Tiers = ParseTierCatalog(File.ReadAllText(tierPath), report);
        }

        return facts;
    }

    public bool TryGetDefault(string name, out string value) => _defaults.TryGetValue(name, out value!);

    private string GetString(string name, string fallback) =>
        _defaults.TryGetValue(name, out var value) ? Unquote(value) : fallback;

    private int GetInt(string name, int fallback) =>
        _defaults.TryGetValue(name, out var value)
        && int.TryParse(Unquote(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private double GetDouble(string name, double fallback) =>
        _defaults.TryGetValue(name, out var value)
        && double.TryParse(Unquote(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static IReadOnlyList<string> ParseLocKeys(string body, Report report)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in ConstPattern.Matches(body))
        {
            var value = Unquote(match.Groups[2].Value.Trim());
            if (value.Length > 0)
            {
                keys.Add(value);
            }
        }

        foreach (Match match in LocTemplatePattern.Matches(body))
        {
            foreach (var tier in TierTokens)
            {
                keys.Add(match.Groups[1].Value + tier + match.Groups[2].Value);
            }
        }

        if (keys.Count < 20)
        {
            report.Error("source", "MwpLocKeys yields only " + keys.Count + " keys (REQ-UI-03 requires at least 20)");
        }

        foreach (var key in keys)
        {
            if (!IsLocKeyShape(key))
            {
                report.Error("source", "MwpLocKeys key '" + key + "' does not use the "
                                       + "Building.MWP./Status.MWP./UI.MWP./Tooltip.MWP./MWP. namespaces");
            }
        }

        return keys.ToList();
    }

    public static bool IsLocKeyShape(string key) =>
        key.StartsWith("Building.MWP.", StringComparison.Ordinal)
        || key.StartsWith("Status.MWP.", StringComparison.Ordinal)
        || key.StartsWith("UI.MWP.", StringComparison.Ordinal)
        || key.StartsWith("Tooltip.MWP.", StringComparison.Ordinal)
        || key.StartsWith("MWP.", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, TierExpectation> ParseTierCatalog(string text, Report report)
    {
        var tiers = new Dictionary<string, TierExpectation>(StringComparer.Ordinal);
        var anchor = text.IndexOf("IReadOnlyList<TierDefinition> Default", StringComparison.Ordinal);
        if (anchor < 0)
        {
            report.Error("source", "could not find 'IReadOnlyList<TierDefinition> Default' in TierDefinition.cs");
            return tiers;
        }

        var index = anchor;
        while ((index = text.IndexOf("new TierDefinition(", index, StringComparison.Ordinal)) >= 0)
        {
            var open = index + "new TierDefinition".Length;
            var body = ExtractParenBody(text, open);
            index = open + 1;
            if (body is null)
            {
                break;
            }

            var tierName = Regex.Match(body, "\"(\\w+)\"").Groups[1].Value;
            if (tierName.Length == 0)
            {
                report.Error("source", "TierCatalog entry without a tier name token: " + Shorten(body));
                continue;
            }

            var buildCost = ParseGoods(Section(body, "buildCost:", "sizeX:"));
            var maintenance = ParseGoods(Section(body, "maintenanceMaterials:", "end"));
            var expectation = new TierExpectation(
                tierName,
                IntNamed(body, "scienceCost"),
                buildCost,
                IntNamed(body, "sizeX"),
                IntNamed(body, "sizeY"),
                IntNamed(body, "sizeZ"),
                DoubleNamed(body, "waterPerHour"),
                IntNamed(body, "powerRequiredHp"),
                IntNamed(body, "maxDepth"),
                IntNamed(body, "internalCapacity"),
                IntNamed(body, "builderHours"),
                DoubleNamed(body, "maintenanceIntervalDays"),
                maintenance);

            if (!tiers.TryAdd(tierName, expectation))
            {
                report.Error("source", "TierCatalog declares tier '" + tierName + "' twice");
            }
        }

        foreach (var token in TierTokens)
        {
            if (!tiers.ContainsKey(token))
            {
                report.Error("source", "TierCatalog has no tier '" + token + "'");
            }
        }

        if (tiers.Count != TierTokens.Length)
        {
            report.Error("source", "TierCatalog must declare exactly 4 tiers (found " + tiers.Count + ")");
        }

        return tiers;
    }

    private static List<GoodPair> ParseGoods(string section)
    {
        var result = new List<GoodPair>();
        foreach (Match match in GoodStackPattern.Matches(section))
        {
            if (int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                result.Add(new GoodPair(match.Groups[1].Value, amount));
            }
        }

        return result;
    }

    private static string Section(string body, string startMarker, string endMarker)
    {
        var start = body.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += startMarker.Length;
        if (endMarker == "end")
        {
            return body[start..];
        }

        var end = body.IndexOf(endMarker, start, StringComparison.Ordinal);
        return end < 0 ? body[start..] : body[start..end];
    }

    private static int IntNamed(string body, string name)
    {
        var match = Regex.Match(body, Regex.Escape(name) + ":\\s*(-?\\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : int.MinValue;
    }

    private static double DoubleNamed(string body, string name)
    {
        var match = Regex.Match(body, Regex.Escape(name) + ":\\s*(-?\\d+(?:\\.\\d+)?)");
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.NaN;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }

    private static string Shorten(string body) =>
        body.Length <= 60 ? body.Replace('\n', ' ').Replace('\r', ' ') : body[..60].Replace('\n', ' ') + "...";

    /// <summary>Returns the body of a class/struct declaration (inside its outermost braces).</summary>
    private static string? ExtractBlock(string text, string declaration)
    {
        var index = text.IndexOf(declaration, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var open = text.IndexOf('{', index);
        return open < 0 ? null : ExtractParenBody(text, open);
    }

    /// <summary>Returns the text between a bracket and its matching close bracket.</summary>
    private static string? ExtractParenBody(string text, int openIndex)
    {
        if (openIndex < 0 || openIndex >= text.Length)
        {
            return null;
        }

        var open = text[openIndex];
        var close = open == '(' ? ')' : '}';
        var depth = 0;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = openIndex; i < text.Length; i++)
        {
            var ch = text[i];

            if (inLineComment)
            {
                if (ch == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (ch == '\\')
                {
                    i++;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (ch == '\\')
                {
                    i++;
                }
                else if (ch == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (ch == '/' && i + 1 < text.Length)
            {
                if (text[i + 1] == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (text[i + 1] == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '\'')
            {
                inChar = true;
                continue;
            }

            if (ch == open)
            {
                depth++;
            }
            else if (ch == close)
            {
                depth--;
                if (depth == 0)
                {
                    return text[(openIndex + 1)..i];
                }
            }
        }

        return null;
    }

    private static string Relative(string modRoot, string path)
    {
        try
        {
            return Path.GetRelativePath(modRoot, path).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
