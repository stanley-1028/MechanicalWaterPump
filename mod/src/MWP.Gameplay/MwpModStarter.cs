using System;
using System.IO;
using MWP.Core;
using MWP.Gameplay.Logging;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace MWP.Gameplay
{
    /// <summary>
    /// Mod entry point (API_FACTS_MWP §1): the game instantiates every <see cref="IModStarter"/>
    /// with a parameterless constructor and calls <see cref="StartMod"/> once, before the game
    /// context DI container exists. This method therefore only records the mod path and loads
    /// <c>MWP_Config.json</c>; the resolved values are handed to the DI singleton
    /// <see cref="MwpRuntime"/>, which is created later.
    ///
    /// It never throws: a broken config or a missing file must degrade to compiled defaults and
    /// must not break game startup (REQ-SAFE-01/02).
    /// </summary>
    public sealed class MwpModStarter : IModStarter
    {
        /// <inheritdoc />
        public void StartMod(IModEnvironment modEnvironment)
        {
            try
            {
                MwpStartup.Initialize(modEnvironment != null ? modEnvironment.ModPath : null);
            }
            catch (Exception exception)
            {
                // Never let the mod entry point break game startup (REQ-SAFE-01).
                TryLogStartupFailure(exception);
            }
        }

        private static void TryLogStartupFailure(Exception exception)
        {
            try
            {
                Debug.LogError("[MWP/Error][" + MwpLogKeys.Startup + "] MWP StartMod failed: "
                    + exception.GetType().Name + ": " + exception.Message);
            }
            catch
            {
                // Unity logging itself is unavailable: there is nothing else safe to do.
            }
        }
    }

    /// <summary>
    /// Write-once hand-off between <see cref="MwpModStarter"/> (runs before DI exists) and
    /// <see cref="MwpRuntime"/> (created by the game container).
    ///
    /// This is the same minimal seam the verified HDBF reference mod uses: the mod environment is
    /// not injectable, mod assemblies are loaded from bytes so <c>Assembly.Location</c> is empty,
    /// and the config file must be read exactly once before any gameplay runs. The state is written
    /// once during <c>StartMod</c> and is read-only afterwards, so REQ-NFR-09 (no global mutable
    /// game state) is preserved in spirit: nothing here changes after startup and no simulation
    /// state lives here.
    /// </summary>
    internal static class MwpStartup
    {
        private static readonly object Gate = new object();

        private static bool _initialized;
        private static string? _modPath;
        private static MwpConfig _config = MwpConfig.Default;
        private static ThrottledLog? _log;
        private static IMwpLog? _rawLog;

        /// <summary>Absolute mod root as supplied by the game; null before/without <c>StartMod</c>.</summary>
        internal static string? ModPath
        {
            get
            {
                EnsureInitialized();
                return _modPath;
            }
        }

        /// <summary>Validated configuration (compiled defaults when the file is absent/corrupt).</summary>
        internal static MwpConfig Config
        {
            get
            {
                EnsureInitialized();
                return _config;
            }
        }

        /// <summary>Session-wide throttled log created during startup.</summary>
        internal static ThrottledLog Log
        {
            get
            {
                EnsureInitialized();
                return _log ??= new ThrottledLog(new UnityMwpLog(), MwpDefaults.LogThrottleSeconds);
            }
        }

        /// <summary>Raw unthrottled sink behind <see cref="Log"/>, for one-shot diagnostics only.</summary>
        internal static IMwpLog RawLog
        {
            get
            {
                EnsureInitialized();
                return _rawLog ??= new UnityMwpLog();
            }
        }

        /// <summary>Reads the environment and the config file; idempotent so a hot reload cannot corrupt it.</summary>
        /// <param name="modPath">Mod root supplied by the game; may be null.</param>
        internal static void Initialize(string? modPath)
        {
            lock (Gate)
            {
                if (_initialized)
                {
                    return;
                }

                _initialized = true;
                _modPath = modPath;
                LoadConfiguration(modPath);
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            Initialize(null);
        }

        private static void LoadConfiguration(string? modPath)
        {
            var rawLog = new UnityMwpLog();
            MwpConfig config = MwpConfig.Default;
            string? configPath = null;

            try
            {
                configPath = FindConfigPath(modPath);
                if (configPath != null)
                {
                    string json = File.ReadAllText(configPath);
                    config = MwpConfigCodec.Parse(json, rawLog);
                }
                else
                {
                    rawLog.Warn(MwpLogKeys.ConfigInvalid,
                        "MWP_Config.json was not found next to the mod; compiled-in defaults are used");
                }
            }
            catch (Exception exception)
            {
                // A missing/unreadable/partially written config is not fatal (REQ-SAFE-01).
                TryWarn(rawLog, "MWP_Config.json could not be read (" + exception.GetType().Name
                    + "); compiled-in defaults are used");
                config = MwpConfig.Default;
                configPath = null;
            }

            _config = config;
            _rawLog = rawLog;
            _log = new ThrottledLog(rawLog, config.LogThrottleSeconds);
            try
            {
                _log.Info(MwpLogKeys.ConfigLoaded,
                    "MWP " + MwpDefaults.ModVersion + " config=" + (configPath ?? "<compiled defaults>")
                    + " schema=" + config.ConfigSchemaVersion + " tiers=" + config.Balance.Tiers.Count
                    + " maintenance=" + (config.MaintenanceEnabled ? "on" : "off")
                    + " modPath=" + (modPath ?? "<unknown>"));
            }
            catch (Exception)
            {
                // Diagnostics only.
            }
        }

        private static string? FindConfigPath(string? modPath)
        {
            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
            {
                return null;
            }

            string candidate = Path.Combine(modPath!, MwpDefaults.ConfigFileName);
            return File.Exists(candidate) ? candidate : null;
        }

        private static void TryWarn(IMwpLog log, string message)
        {
            try
            {
                log.Warn(MwpLogKeys.ConfigInvalid, message);
            }
            catch (Exception)
            {
                // Diagnostics only.
            }
        }
    }
}
