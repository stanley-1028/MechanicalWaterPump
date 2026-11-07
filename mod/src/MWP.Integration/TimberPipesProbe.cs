using System;
using System.Reflection;

namespace MWP.Integration
{
    /// <summary>
    /// One session-wide Timber Pipes detection pass (REQ-COMPAT-04/07/08).
    ///
    /// Detection is purely reflective: this assembly never references <c>TimberPipes.dll</c>
    /// (REQ-COMPAT-04). Two independent sources are tried:
    /// <list type="number">
    /// <item>loaded assembly simple names, normalised so "TimberPipes", "Timber Pipes" and
    /// "Timber_Pipes" all match;</item>
    /// <item>well-known Timber Pipes API anchor type names, resolved through
    /// <see cref="Assembly.GetType(string, bool)"/> so no type is ever loaded eagerly.</item>
    /// </list>
    /// Every reflection call is individually guarded: a hostile/unreadable assembly (for example
    /// a stale mod whose dependency is missing) must not break MWP, it only weakens detection
    /// (REQ-SAFE-04).
    /// </summary>
    internal sealed class TimberPipesProbe
    {
        /// <summary>Normalised assembly-name tokens that mean "Timber Pipes is loaded".</summary>
        private static readonly string[] AssemblyNameTokens =
        {
            "timberpipes",
            "timberpipescontinued",
            "timberpipestwo",
            "timberpipes2",
        };

        /// <summary>Candidate API anchors; any resolvable type means the mod is loaded.</summary>
        private static readonly string[] TypeAnchors =
        {
            "TimberPipes.TimberPipeNetwork",
            "TimberPipes.PipeNetwork",
            "TimberPipes.TimberPipe",
            "TimberPipes.TimberPipesConfigurator",
            "TimberPipes.ModStarter",
            "Timberborn.Pipes.TimberPipeNetwork",
        };

        /// <summary>True when a Timber Pipes assembly or API anchor was found.</summary>
        internal bool Detected { get; private set; }

        /// <summary>Best-effort assembly version string; null when it could not be read.</summary>
        internal string? Version { get; private set; }

        /// <summary>Where the positive (or negative) result came from, for the compat log.</summary>
        internal string Source { get; private set; } = "not-scanned";

        /// <summary>Non-null when the scan itself threw; treated as an adapter error, never fatal.</summary>
        internal string? Failure { get; private set; }

        /// <summary>True when the scan failed and the adapter must report <c>Error</c>.</summary>
        internal bool ScanFailed => Failure != null;

        /// <summary>
        /// Runs the session scan exactly once per probe instance. Deliberately not cached in a
        /// static: a static cache would survive a mod hot-reload and lie about the new AppDomain
        /// state (REQ-SAFE-04).
        /// </summary>
        internal void Scan()
        {
            try
            {
                ScanCore();
            }
            catch (Exception exception)
            {
                Detected = false;
                Failure = "scan-exception:" + exception.GetType().Name;
                Source = "assembly-scan";
            }
        }

        private void ScanCore()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (assemblies == null)
            {
                Failure = "assembly-list-unavailable";
                return;
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                string? name = SafeName(assemblies[i]);
                if (name == null)
                {
                    continue;
                }

                if (ContainsToken(name))
                {
                    Detected = true;
                    Source = "assembly-name:" + name;
                    Version = TryReadVersion(assemblies[i]);
                    return;
                }
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                string? anchor = ResolveAnchor(assemblies[i]);
                if (anchor != null)
                {
                    Detected = true;
                    Source = "api-anchor:" + anchor;
                    Version = TryReadVersion(assemblies[i]);
                    return;
                }
            }

            Source = "assembly-scan";
        }

        private static string? ResolveAnchor(Assembly assembly)
        {
            for (int i = 0; i < TypeAnchors.Length; i++)
            {
                try
                {
                    // throwOnError:false returns null instead of throwing for an absent type.
                    if (assembly.GetType(TypeAnchors[i], false) != null)
                    {
                        return TypeAnchors[i];
                    }
                }
                catch (Exception)
                {
                    // One broken assembly must not abort the anchor loop (REQ-SAFE-04).
                }
            }

            return null;
        }

        private static string? SafeName(Assembly assembly)
        {
            try
            {
                AssemblyName? name = assembly.GetName();
                return name?.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool ContainsToken(string assemblyName)
        {
            string normalized = Normalize(assemblyName);
            if (normalized.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < AssemblyNameTokens.Length; i++)
            {
                if (normalized.Contains(AssemblyNameTokens[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Lower-cases and strips separators so naming variants compare equal.</summary>
        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        private static string? TryReadVersion(Assembly assembly)
        {
            try
            {
                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (informational != null && !string.IsNullOrEmpty(informational.InformationalVersion))
                {
                    return informational.InformationalVersion.Trim();
                }

                return assembly.GetName().Version?.ToString();
            }
            catch (Exception)
            {
                // Version metadata is best effort: a miss only leaves the diagnostic field null.
                return null;
            }
        }
    }
}
