using System.Text.RegularExpressions;

namespace MWP.Tools.Validate;

/// <summary>
/// MWP data validator. Usage (from the project root):
///
///   dotnet run --project mod\tools\validate -c Release -m:1 -- mod\data --managed "&lt;game&gt;\Timberborn_Data\Managed"
///
/// Exit codes: 0 = data/ is valid and the report ends with VALIDATION PASSED, 1 = problems,
/// 2 = cannot even start (bad paths).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? dataArgument = null;
        string? managedArgument = null;
        string? modArgument = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                case "--managed":
                    managedArgument = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--mod":
                    modArgument = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--":
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        break;
                    }

                    if (dataArgument is null)
                    {
                        dataArgument = args[i];
                    }
                    else
                    {
                        Console.Error.WriteLine("unexpected argument: " + args[i]);
                        PrintUsage();
                        return 2;
                    }

                    break;
            }
        }

        var dataDir = Path.GetFullPath(dataArgument ?? "data");
        if (!Directory.Exists(dataDir))
        {
            Console.Error.WriteLine("data directory not found: " + dataDir);
            return 2;
        }

        var modRoot = Path.GetFullPath(modArgument ?? GuessModRoot(dataDir));
        var managedDir = ResolveManagedDir(modRoot, managedArgument);
        if (managedDir is null || !Directory.Exists(managedDir))
        {
            Console.Error.WriteLine(
                "could not find the Timberborn managed assemblies. Pass --managed <game>\\Timberborn_Data\\Managed "
                + "or set TIMBERBORN_MANAGED_DIR. Tried: " + (managedDir ?? "(none)"));
            return 2;
        }

        var report = new Report();
        var modAssemblies = DiscoverModAssemblies(modRoot);
        var metadata = GameMetadata.Load(managedDir, modAssemblies, report);
        var facts = SourceFacts.Load(modRoot, report);
        var game = GameBlueprintCatalog.Load(managedDir, report);

        Console.WriteLine("MWP data validator");
        Console.WriteLine("  data dir        : " + dataDir);
        Console.WriteLine("  mod root        : " + modRoot);
        Console.WriteLine("  managed dir     : " + managedDir);
        Console.WriteLine("  game assemblies : " + metadata.GameAssemblyCount);
        Console.WriteLine("  spec types      : " + metadata.SpecTypeCount + " (ComponentSpec subclasses + aliases)");
        Console.WriteLine("  mod assemblies  : " + metadata.ModAssemblyCount
                          + (modAssemblies.Count > 0
                              ? " (" + string.Join(", ", modAssemblies.Select(Path.GetFileName)) + ")"
                              : " (none built yet)"));
        Console.WriteLine("  tier table      : " + facts.Tiers.Count + " tiers from TierCatalog");
        Console.WriteLine("  loc keys        : " + facts.LocKeys.Count + " keys from MwpLocKeys");
        Console.WriteLine("  vanilla models  : " + (game.Available
            ? game.BlueprintCount + " blueprints indexed from Blueprints.zip"
            : "NOT INDEXED (model path checks downgraded to warnings)"));

        var blueprintFiles = Directory
            .GetFiles(dataDir, "*.blueprint.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Console.WriteLine("  blueprints      : " + blueprintFiles.Count
                          + " (8 buildings + 2 collections + 1 tool group expected)");

        new DataChecks(dataDir, modRoot, facts, game, report).Validate();

        var blueprintValidator = new BlueprintValidator(metadata, report, facts.PumpSpecKey, ContractSpecFields);
        foreach (var file in blueprintFiles)
        {
            var relative = Path.GetRelativePath(dataDir, file).Replace('\\', '/');
            blueprintValidator.ValidateFile(file, relative);
        }

        new Localizations(facts, report).Validate(dataDir, blueprintValidator.UsedLocKeys);
        new ConfigValidator(facts, report).Validate(dataDir);
        new AssemblyChecks(modRoot, managedDir, facts, metadata, report).Validate();

        return report.Finish(Console.Out);
    }

    /// <summary>CONTRACT section 3 fields of MWP.MechanicalWaterPumpSpec (fallback before the build).</summary>
    private static readonly string[] ContractSpecFields =
    {
        "Tier", "WaterPerHour", "PowerRequiredHp", "InternalCapacity", "MaxDepth", "BadwaterPenalty",
        "MinWaterQuality", "CubicMetersPerWater", "MaintenanceIntervalDays", "MaintenanceDurationHours",
        "MaintenanceMaterials",
    };

    private static void PrintUsage()
    {
        Console.WriteLine("usage: MwpValidate [dataDir] [--managed <Timberborn_Data\\Managed>] [--mod <modRoot>]");
        Console.WriteLine();
        Console.WriteLine("Validates the shippable mod data folder against the installed Timberborn metadata;");
        Console.WriteLine("defaults: dataDir = 'data', modRoot = parent of dataDir.");
    }

    private static string GuessModRoot(string dataDir)
    {
        var parent = Directory.GetParent(dataDir);
        if (parent is not null && string.Equals(Path.GetFileName(dataDir), "data", StringComparison.OrdinalIgnoreCase))
        {
            return parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? ResolveManagedDir(string modRoot, string? explicitManagedDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitManagedDir))
        {
            return Path.GetFullPath(explicitManagedDir);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("TIMBERBORN_MANAGED_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var propsPath = Path.Combine(modRoot, "Directory.Build.props");
        if (File.Exists(propsPath))
        {
            var match = Regex.Match(File.ReadAllText(propsPath),
                @"<TimberbornDir[^>]*>([^<]+)</TimberbornDir>", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var candidate = Path.Combine(match.Groups[1].Value.Trim(), "Timberborn_Data", "Managed");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", "Timberborn", "Timberborn_Data", "Managed");
    }

    private static List<string> DiscoverModAssemblies(string modRoot)
    {
        var sourceDir = Path.Combine(modRoot, "src");
        if (!Directory.Exists(sourceDir))
        {
            return new List<string>();
        }

        var assemblies = new List<string>();
        foreach (var projectDir in Directory.GetDirectories(sourceDir).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var assemblyName = Path.GetFileName(projectDir) + ".dll";
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                foreach (var framework in new[] { "netstandard2.1", "net9.0" })
                {
                    var candidate = Path.Combine(projectDir, "bin", configuration, framework, assemblyName);
                    if (File.Exists(candidate))
                    {
                        assemblies.Add(candidate);
                        break;
                    }
                }

                if (assemblies.Any(path => string.Equals(Path.GetFileName(path), assemblyName, StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }
            }
        }

        return assemblies.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
