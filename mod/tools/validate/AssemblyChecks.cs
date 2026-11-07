using Mono.Cecil;

namespace MWP.Tools.Validate;

/// <summary>
/// Static contract checks on the built mod assemblies (Lead A5-A7), read with Mono.Cecil from
/// <c>mod/src/&lt;Project&gt;/bin/Release/netstandard2.1/&lt;Project&gt;.dll</c>:
///
/// <list type="bullet">
/// <item>A5: <c>MWP.MechanicalWaterPumpSpec</c> exists as a public, non-abstract ComponentSpec
/// subclass, carries the <c>[SpecAlias("MWP.MechanicalWaterPumpSpec")]</c> the blueprint JSON key
/// needs, and exposes exactly the eleven CONTRACT section 3 [Serialize] members.</item>
/// <item>A6: exactly one <c>IModStarter</c>, at least one <c>Configurator</c>, and an IL-visible
/// <c>TemplateModule</c> <c>AddDecorator</c> call that binds the spec.</item>
/// <item>A7: <c>MWP.Core</c> references only netstandard/System, no MWP assembly references
/// TimberPipes or a NuGet package, and <c>MWP.UI</c> references only <c>MWP.Core</c>.</item>
/// </list>
///
/// When the Release binaries do not exist yet (the data validator is often run before the mod is
/// built) the checks are skipped with a warning instead of failing.
/// </summary>
internal sealed class AssemblyChecks
{
    private static readonly string[] Projects = { "MWP.Core", "MWP.Gameplay", "MWP.Integration", "MWP.UI" };

    private static readonly string[] ContractSpecFields =
    {
        "Tier", "WaterPerHour", "PowerRequiredHp", "InternalCapacity", "MaxDepth", "BadwaterPenalty",
        "MinWaterQuality", "CubicMetersPerWater", "MaintenanceIntervalDays", "MaintenanceDurationHours",
        "MaintenanceMaterials",
    };

    private static readonly string[] NuGetAssemblies =
    {
        "Newtonsoft.Json", "Mono.Cecil", "NUnit", "nunit.framework", "xunit", "FluentAssertions", "Moq",
        "AutoFixture", "System.Text.Json",
    };

    private const string SpecNamespace = "MWP";
    private const string SpecTypeName = "MechanicalWaterPumpSpec";
    private const string ComponentSpec = "Timberborn.BlueprintSystem.ComponentSpec";
    private const string ModStarter = "Timberborn.ModManagerScene.IModStarter";
    private const string Configurator = "Bindito.Core.Configurator";

    private readonly string _modRoot;
    private readonly string _managedDir;
    private readonly SourceFacts _facts;
    private readonly GameMetadata _metadata;
    private readonly Report _report;
    private readonly Dictionary<string, AssemblyDefinition> _assemblies = new(StringComparer.Ordinal);

    public AssemblyChecks(string modRoot, string managedDir, SourceFacts facts, GameMetadata metadata, Report report)
    {
        _modRoot = modRoot;
        _managedDir = managedDir;
        _facts = facts;
        _metadata = metadata;
        _report = report;
    }

    public void Validate()
    {
        Discover();

        if (_assemblies.Count == 0)
        {
            _report.Warn("assemblies", "assemblies not built yet (no bin/Release/netstandard2.1 output under src/); A5-A7 skipped");
            return;
        }

        if (_assemblies.ContainsKey("MWP.Gameplay"))
        {
            ValidateSpec();
            ValidateWiring();
        }
        else
        {
            _report.Warn("assemblies", "MWP.Gameplay.dll is not built yet; A5 (spec shape) and A6 (wiring) skipped");
        }

        ValidateReferences();
    }

    private void Discover()
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(_managedDir);

        foreach (var project in Projects)
        {
            var path = FindAssembly(project);
            if (path is null)
            {
                continue;
            }

            try
            {
                _assemblies[project] = AssemblyDefinition.ReadAssembly(
                    path, new ReaderParameters { AssemblyResolver = resolver });
            }
            catch (BadImageFormatException ex)
            {
                _report.Error("assemblies", "could not read " + Path.GetFileName(path) + ": " + ex.Message);
            }
        }
    }

    private string? FindAssembly(string project)
    {
        var relative = Path.Combine("src", project, "bin");
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var candidate = Path.Combine(_modRoot, relative, configuration, "netstandard2.1", project + ".dll");
            if (File.Exists(candidate))
            {
                if (configuration == "Debug")
                {
                    _report.Warn("assemblies", project + ".dll was found only in Debug; A5-A7 should run on a Release build");
                }

                return candidate;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ A5
    private void ValidateSpec()
    {
        var gameplay = _assemblies["MWP.Gameplay"];
        var types = GameMetadata.EnumerateTypes(gameplay).ToList();

        var spec = types.FirstOrDefault(type =>
            string.Equals(type.Name, SpecTypeName, StringComparison.Ordinal)
            && string.Equals(type.Namespace, SpecNamespace, StringComparison.Ordinal));

        if (spec is null)
        {
            var candidates = types
                .Where(type => type.Name.EndsWith("PumpSpec", StringComparison.Ordinal)
                               || string.Equals(type.Name, SpecTypeName, StringComparison.Ordinal))
                .Select(type => type.FullName)
                .ToList();
            _report.Error("assemblies", "MWP.Gameplay.dll: public ComponentSpec '" + SpecNamespace + "." + SpecTypeName
                                        + "' was not found" + (candidates.Count > 0
                                            ? " (found: " + string.Join(", ", candidates) + ")"
                                            : string.Empty));
            return;
        }

        Check(spec.IsPublic || spec.IsNestedPublic, "MWP.Gameplay: " + spec.FullName + " must be public");
        Check(!spec.IsAbstract && !spec.IsInterface && !spec.IsValueType,
            "MWP.Gameplay: " + spec.FullName + " must be a concrete record class");
        Check(_metadata.IsAssignableTo(spec, ComponentSpec),
            "MWP.Gameplay: " + spec.FullName + " must derive from " + ComponentSpec);

        var aliases = GameMetadata.ReadAliases(spec);
        Check(aliases.Contains(_facts.PumpSpecKey, StringComparer.Ordinal),
            "MWP.Gameplay: " + spec.FullName + " must carry [SpecAlias(\"" + _facts.PumpSpecKey
            + "\")] so the blueprint JSON key resolves (SpecTypeCache indexes Type.Name and aliases only)");

        var serialized = _metadata.GetSerializedMemberNames(spec);
        var expected = ContractSpecFields.OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (!serialized.SequenceEqual(expected, StringComparer.Ordinal))
        {
            var missing = expected.Except(serialized, StringComparer.Ordinal).ToList();
            var extra = serialized.Except(expected, StringComparer.Ordinal).ToList();
            _report.Error("assemblies", "MWP.Gameplay: " + spec.FullName + " [Serialize] members must be exactly the "
                                        + "CONTRACT section 3 fields"
                                        + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : string.Empty)
                                        + (extra.Count > 0 ? "; unexpected: " + string.Join(", ", extra) : string.Empty));
        }

        var members = _metadata.GetMembers(spec);
        foreach (var field in ContractSpecFields)
        {
            if (!members.TryGetValue(field, out var member))
            {
                continue;
            }

            if (string.Equals(field, "Tier", StringComparison.Ordinal))
            {
                Check(member.Type.FullName == "System.String", "MWP.Gameplay: " + field + " must be string");
                continue;
            }

            if (string.Equals(field, "MaintenanceMaterials", StringComparison.Ordinal))
            {
                if (!GameMetadata.IsCollection(member.Type, out var element))
                {
                    _report.Error("assemblies", "MWP.Gameplay: MaintenanceMaterials must be a collection of {Id,Amount}");
                    continue;
                }

                var elementDefinition = _metadata.Resolve(element);
                if (elementDefinition is null)
                {
                    _report.Warn("assemblies", "MWP.Gameplay: could not resolve the MaintenanceMaterials element type "
                                               + element.FullName);
                    continue;
                }

                var elementMembers = _metadata.GetMembers(elementDefinition);
                Check(elementMembers.ContainsKey("Id") && elementMembers.ContainsKey("Amount"),
                    "MWP.Gameplay: MaintenanceMaterials element " + element.Name + " must expose Id and Amount");
                continue;
            }

            if (!GameMetadata.TryGetPrimitiveKind(member.Type, out var kind) || kind != PrimitiveKind.Number)
            {
                _report.Error("assemblies", "MWP.Gameplay: " + field + " must be numeric, found " + member.Type.FullName);
            }
        }
    }

    // ------------------------------------------------------------------ A6
    private void ValidateWiring()
    {
        var types = AllModTypes();

        var starters = types
            .Where(type => !type.IsAbstract && !type.IsInterface && _metadata.IsAssignableTo(type, ModStarter))
            .ToList();
        if (starters.Count != 1)
        {
            _report.Error("assemblies", "there must be exactly one " + ModStarter + " implementation, found "
                                        + starters.Count
                                        + (starters.Count > 0
                                            ? " (" + string.Join(", ", starters.Select(type => type.FullName)) + ")"
                                            : string.Empty));
        }
        else
        {
            var starter = starters[0];
            Check(starter.IsPublic || starter.IsNestedPublic, "assemblies: " + starter.FullName + " must be public");
            Check(starter.Methods.Any(method => method.IsConstructor && method.IsPublic && method.Parameters.Count == 0),
                "assemblies: " + starter.FullName + " needs a public parameterless constructor");
        }

        var configurators = types
            .Where(type => !type.IsAbstract && !type.IsInterface && _metadata.IsAssignableTo(type, Configurator))
            .ToList();
        Check(configurators.Count >= 1, "assemblies: no " + Configurator + " subclass found");
        foreach (var configurator in configurators)
        {
            Check(configurator.Methods.Any(method => method.Name == "Configure"),
                "assemblies: " + configurator.FullName + " does not declare Configure()");
        }

        var addDecoratorCalls = 0;
        var specBound = false;
        var decoratorArguments = new List<string>();
        foreach (var type in types)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference reference
                        || !string.Equals(reference.Name, "AddDecorator", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // The declaring type is the nested TemplateModule/Builder, whose Cecil Name is
                    // just "Builder", so match on the full name (or any generic AddDecorator).
                    var declaringFullName = reference.DeclaringType.FullName;
                    var isTemplateModule = declaringFullName.Contains("TemplateModule", StringComparison.Ordinal)
                                           || declaringFullName.Contains("Builder", StringComparison.Ordinal);
                    if (!isTemplateModule && reference is not IGenericInstance)
                    {
                        continue;
                    }

                    addDecoratorCalls++;
                    if (reference is not GenericInstanceMethod generic || generic.GenericArguments.Count == 0)
                    {
                        continue;
                    }

                    // AddDecorator<TSpec, TDecorator>: first argument is the spec, second the decorator.
                    var specArgument = generic.GenericArguments[0];
                    if (specArgument.Name.Contains(SpecTypeName, StringComparison.Ordinal))
                    {
                        specBound = true;
                    }

                    decoratorArguments.Add(specArgument.Name
                                           + (generic.GenericArguments.Count > 1
                                               ? " -> " + generic.GenericArguments[1].Name
                                               : string.Empty));
                }
            }
        }

        Check(addDecoratorCalls >= 1,
            "assemblies: no TemplateModule.AddDecorator call found (the spec must be bound to its controller)");
        if (addDecoratorCalls >= 1 && !specBound)
        {
            _report.Warn("assemblies", "TemplateModule.AddDecorator was found " + addDecoratorCalls
                                       + " time(s) but no call binds " + SpecTypeName
                                       + " (calls: " + string.Join(", ", decoratorArguments) + ")");
        }

        foreach (var argument in decoratorArguments.Where(argument => argument.Contains("-> ", StringComparison.Ordinal)))
        {
            var decorator = argument[(argument.IndexOf("-> ", StringComparison.Ordinal) + 3)..].Trim();
            if (decorator.Length == 0)
            {
                continue;
            }

            var found = types.Any(type => string.Equals(type.Name, decorator, StringComparison.Ordinal));
            if (!found)
            {
                _report.Warn("assemblies", "the AddDecorator target '" + decorator
                                           + "' is not declared in the built mod assemblies");
            }
        }
    }

    // ------------------------------------------------------------------ A7
    private void ValidateReferences()
    {
        if (_assemblies.TryGetValue("MWP.Core", out var core))
        {
            foreach (var reference in core.MainModule.AssemblyReferences)
            {
                var name = reference.Name;
                var allowed = string.Equals(name, "mscorlib", StringComparison.Ordinal)
                              || string.Equals(name, "netstandard", StringComparison.Ordinal)
                              || string.Equals(name, "System", StringComparison.Ordinal)
                              || name.StartsWith("System.", StringComparison.Ordinal);
                if (!allowed)
                {
                    _report.Error("assemblies", "MWP.Core must not reference '" + name
                                                + "' (Core has to stay game-independent: netstandard/System only)");
                }
            }
        }

        foreach (var pair in _assemblies)
        {
            foreach (var reference in pair.Value.MainModule.AssemblyReferences)
            {
                var name = reference.Name;
                if (name.Contains("TimberPipes", StringComparison.OrdinalIgnoreCase))
                {
                    _report.Error("assemblies", pair.Key + " must not reference '" + name
                                                + "' (Timber Pipes is a soft dependency detected at runtime)");
                }

                foreach (var nuget in NuGetAssemblies)
                {
                    if (string.Equals(name, nuget, StringComparison.Ordinal)
                        || name.StartsWith(nuget + ".", StringComparison.Ordinal))
                    {
                        _report.Error("assemblies", pair.Key + " must not reference the NuGet assembly '" + name
                                                    + "' (zero NuGet dependencies)");
                    }
                }
            }
        }

        if (_assemblies.TryGetValue("MWP.UI", out var ui))
        {
            var references = ui.MainModule.AssemblyReferences.Select(reference => reference.Name).ToList();
            Check(!references.Contains("MWP.Gameplay", StringComparer.Ordinal),
                "assemblies: MWP.UI must not reference MWP.Gameplay (UI only sees MWP.Core)");
            Check(!references.Contains("MWP.Integration", StringComparer.Ordinal),
                "assemblies: MWP.UI must not reference MWP.Integration (UI only sees MWP.Core)");
            if (!references.Contains("MWP.Core", StringComparer.Ordinal))
            {
                _report.Warn("assemblies", "MWP.UI does not reference MWP.Core; double-check the UI project layout");
            }
        }
    }

    private List<TypeDefinition> AllModTypes()
    {
        var result = new List<TypeDefinition>();
        foreach (var assembly in _assemblies.Values)
        {
            result.AddRange(GameMetadata.EnumerateTypes(assembly));
        }

        return result;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            _report.Error("assemblies", message);
        }
    }
}
