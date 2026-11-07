using System;
using MWP.Core;
using Timberborn.SingletonSystem;

namespace MWP.Integration
{
    /// <summary>
    /// Timber Pipes soft-dependency adapter (CONTRACT §11, REQ-COMPAT-03/07/08).
    ///
    /// v0.2 deliberately implements detection + degradation only:
    /// <list type="bullet">
    /// <item><c>None</c>: Timber Pipes is not loaded. Core gameplay is unaffected because the
    /// standard <c>SimpleOutputInventory</c> Water is still haulable (REQ-COMPAT-01).</item>
    /// <item><c>ValveOnly</c>: Timber Pipes is loaded. A One-way Valve can extract the pump's
    /// internal Water inventory; the MUST path needs no code and no DLL reference
    /// (REQ-COMPAT-02/04).</item>
    /// <item><c>Direct</c>: reserved for a future direct output port; v0.2 never reports it
    /// because no external blueprint is injected.</item>
    /// <item><c>Error</c>: detection itself failed. Logged once as <c>MWP/Compat</c> and the
    /// pump falls back to the valve path (REQ-COMPAT-03/07).</item>
    /// </list>
    ///
    /// The adapter is a session singleton registered by <see cref="MwpIntegrationConfigurator"/>;
    /// <see cref="Load"/> is the game's singleton load hook and performs the one startup scan that
    /// writes <c>MWP/Compat/Scan</c> (REQ-COMPAT-08).
    /// </summary>
    public sealed class MwpPipeAdapter : IPipeAdapter, ILoadableSingleton
    {
        private readonly TimberPipesProbe _probe = new TimberPipesProbe();
        private readonly ThrottledLog _log;

        private PipeAdapterState _state = PipeAdapterState.Unknown;
        private string _diagnostic = "compatibility scan has not run yet";
        private bool _scanned;
        private bool _versionTooOld;

        /// <summary>
        /// Sole constructor (every DI-bound type may have exactly one, API_FACTS §6). The adapter
        /// builds its own throttled logger because MWP.Integration must not depend on
        /// MWP.Gameplay's registrar.
        /// </summary>
        public MwpPipeAdapter()
        {
            _log = new ThrottledLog(new UnityCompatLog(), MwpDefaults.LogThrottleSeconds);
        }

        /// <inheritdoc />
        public PipeAdapterState GetState(string tierName)
        {
            EnsureScanned(logScan: false);
            return _state;
        }

        /// <inheritdoc />
        public string Diagnostic => _diagnostic;

        /// <inheritdoc />
        public bool DirectOutputAvailable => false;

        /// <summary>
        /// Session load hook: runs the one startup detection and writes the throttled
        /// <c>MWP/Compat/Scan</c> line (REQ-COMPAT-08).
        /// </summary>
        public void Load()
        {
            EnsureScanned(logScan: true);
        }

        private void EnsureScanned(bool logScan)
        {
            if (_scanned)
            {
                return;
            }

            _scanned = true;
            try
            {
                _probe.Scan();
                ApplyProbeResult(logScan);
            }
            catch (Exception exception)
            {
                // Last line of defence: a detection failure only degrades the optional feature
                // (REQ-COMPAT-03/07). It must never prevent the mod from starting.
                _state = PipeAdapterState.Error;
                _diagnostic = "compatibility scan threw " + exception.GetType().Name + "; valve path only";
                TryLog(logScan, _diagnostic, isError: true);
            }
        }

        private void ApplyProbeResult(bool logScan)
        {
            if (_probe.ScanFailed)
            {
                _state = PipeAdapterState.Error;
                _diagnostic = "Timber Pipes detection failed (" + _probe.Failure
                    + "); falling back to the valve path only";
                TryLog(logScan, _diagnostic, isError: true);
                return;
            }

            if (!_probe.Detected)
            {
                _state = PipeAdapterState.None;
                _diagnostic = "Timber Pipes not detected; standard Water inventory only, core gameplay unaffected";
                TryLog(logScan, _diagnostic, isError: false);
                return;
            }

            _versionTooOld = IsOlderThanVerified(_probe.Version, _probe.Source);
            _state = PipeAdapterState.ValveOnly;
            _diagnostic = "Timber Pipes " + (_probe.Version ?? "unknown version") + " detected via "
                + _probe.Source + "; one-way valve extraction available, direct port not implemented in v0.2"
                + (_versionTooOld ? " (older than the verified minimum" + ")" : string.Empty);
            TryLog(logScan, _diagnostic, isError: false);

            if (_versionTooOld && logScan)
            {
                _log.Warn(MwpLogKeys.CompatAdapterError,
                    "Timber Pipes version " + (_probe.Version ?? "unknown") + " is older than the verified minimum "
                    + MwpConfig.Default.TimberPipesMinVersion + "; the valve path should still work but is untested");
            }
        }

        private void TryLog(bool logScan, string message, bool isError)
        {
            if (!logScan)
            {
                return;
            }

            try
            {
                if (isError)
                {
                    _log.Compat(MwpLogKeys.CompatAdapterError, message);
                }
                else
                {
                    _log.Compat(MwpLogKeys.CompatScan, message);
                }
            }
            catch (Exception)
            {
                // Diagnostics only: logging must not break the mod (REQ-SAFE-01).
            }
        }

        /// <summary>
        /// Compares the detected version with the verified minimum. Unparsable versions are treated
        /// as "unknown, not older" so a build-number-only version never spams warnings.
        /// </summary>
        private static bool IsOlderThanVerified(string? detected, string source)
        {
            if (string.IsNullOrEmpty(detected))
            {
                return false;
            }

            Version? actual = TryParseVersion(detected!);
            Version? minimum = TryParseVersion(MwpConfig.Default.TimberPipesMinVersion);
            if (actual == null || minimum == null)
            {
                return false;
            }

            // Compare only the numeric triple; a fourth component of 0 is equivalent.
            return actual.Major < minimum.Major
                || (actual.Major == minimum.Major && actual.Minor < minimum.Minor)
                || (actual.Major == minimum.Major && actual.Minor == minimum.Minor && actual.Build < minimum.Build);
        }

        private static Version? TryParseVersion(string text)
        {
            try
            {
                // Informational versions may carry "+build" or "-prerelease" suffixes and a fourth
                // component; take the numeric prefix the version actually is.
                int cut = text.IndexOfAny(new[] { '+', '-', ' ' });
                string core = cut > 0 ? text.Substring(0, cut) : text;
                return Version.TryParse(core, out Version? parsed) ? parsed : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
