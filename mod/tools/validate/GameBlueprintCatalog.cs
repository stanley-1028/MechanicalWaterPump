using System.IO.Compression;
using System.Text.Json;

namespace MWP.Tools.Validate;

/// <summary>
/// Read-only index of the installed game's <c>StreamingAssets/Modding/Blueprints.zip</c>: every
/// vanilla <c>TimbermeshSpec.Model</c> path, every <c>PipeSegmentPrefabPath</c>, every
/// <c>TemplateName</c> and every blueprint file path.
///
/// The MWP blueprints reference vanilla models/pipe segments by path instead of shipping the
/// assets (CONTRACT section 6), so "the path exists for this faction" can only be verified
/// against the game's own blueprint metadata. When the zip cannot be found the catalog is
/// unavailable and the model checks downgrade to a warning.
/// </summary>
internal sealed class GameBlueprintCatalog
{
    private readonly HashSet<string> _modelPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pipePaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _templateNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _blueprintFiles = new(StringComparer.Ordinal);

    private GameBlueprintCatalog(string zipPath)
    {
        ZipPath = zipPath;
    }

    public string ZipPath { get; }

    public bool Available { get; private set; }

    public int BlueprintCount => _blueprintFiles.Count;

    public static GameBlueprintCatalog Load(string managedDir, Report report)
    {
        var zipPath = Path.GetFullPath(Path.Combine(
            managedDir, "..", "StreamingAssets", "Modding", "Blueprints.zip"));
        var catalog = new GameBlueprintCatalog(zipPath);

        if (!File.Exists(zipPath))
        {
            report.Warn("models", "game blueprint archive not found at " + zipPath
                                  + "; TimbermeshSpec.Model / PipeSegmentPrefabPath existence is not verified");
            return catalog;
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".blueprint.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                catalog._blueprintFiles.Add(entry.FullName);
                using var stream = entry.Open();
                using var document = JsonDocument.Parse(stream);
                catalog.CollectMetadata(document.RootElement);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            report.Warn("models", "could not read the game blueprint archive: " + ex.GetType().Name + ": " + ex.Message);
            return catalog;
        }

        catalog.Available = true;
        return catalog;
    }

    public bool HasModelPath(string path) => _modelPaths.Contains(path);

    public bool HasPipeSegment(string path) => _pipePaths.Contains(path);

    public bool HasTemplate(string name) => _templateNames.Contains(name);

    /// <summary>True when <paramref name="pathWithoutJson"/> (a blueprint path without .json) exists.</summary>
    public bool HasBlueprint(string pathWithoutJson) =>
        _blueprintFiles.Contains(Normalize(pathWithoutJson) + ".json");

    private void CollectMetadata(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (string.Equals(property.Name, "TimbermeshSpec", StringComparison.Ordinal)
                            && property.Value.TryGetProperty("Model", out var model)
                            && model.ValueKind == JsonValueKind.String)
                        {
                            Add(_modelPaths, model.GetString());
                        }

                        if (string.Equals(property.Name, "TemplateSpec", StringComparison.Ordinal)
                            && property.Value.TryGetProperty("TemplateName", out var template)
                            && template.ValueKind == JsonValueKind.String)
                        {
                            Add(_templateNames, template.GetString());
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String
                             && string.Equals(property.Name, "PipeSegmentPrefabPath", StringComparison.Ordinal))
                    {
                        Add(_pipePaths, property.Value.GetString());
                    }

                    CollectMetadata(property.Value);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMetadata(item);
                }

                break;
        }
    }

    private static void Add(HashSet<string> target, string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length > 0)
        {
            target.Add(normalized);
        }
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Replace('\\', '/');
}
