using System;
using System.Diagnostics;
using System.Globalization;
using MWP.Core;
using MWP.Gameplay.Logging;
using Timberborn.SingletonSystem;

namespace MWP.Gameplay
{
    /// <summary>
    /// Game-context singleton that exposes the startup-resolved configuration and the session
    /// throttled log to every gameplay component (REQ-NFR-04/09).
    ///
    /// It is a singleton so one <see cref="ThrottledLog"/> instance owns all per-key log windows —
    /// two loggers would allow two messages per window (REQ-OPS-03). <see cref="Load"/> is the
    /// game's singleton lifecycle hook (API_FACTS_MWP §7) and only logs the resolved state; the
    /// values themselves were already parsed once in <see cref="MwpModStarter"/> so no gameplay
    /// tick ever touches the filesystem.
    /// </summary>
    public sealed class MwpRuntime : ILoadableSingleton
    {
        /// <summary>Gameplay-local diagnostics key for the REQ-OPS-04 equivalent output.</summary>
        private const string LogKeyDiag = "MWP/Info/Diag";

        /// <summary>Diagnostics window; REQ-OPS-04 asks for a periodic, low-cost status line.</summary>
        private const double DiagnosticsIntervalSeconds = 60.0;

        private MwpConfig _config = MwpConfig.Default;
        private ThrottledLog _log;
        private IMwpLog _rawLog;

        // REQ-OPS-04 equivalent: O(1) tick-time accounting, emitted every 60 s only when
        // features.uiDiagnostics is enabled (never on the default path).
        private long _diagWindowStartTimestamp;
        private long _measuredTickCount;
        private double _measuredTickMilliseconds;
        private int _reportedPumpCount;
        private PipeAdapterState _reportedAdapterState = PipeAdapterState.Unknown;

        /// <summary>Creates the runtime; the real config arrives in <see cref="Load"/>.</summary>
        public MwpRuntime()
        {
            _rawLog = new UnityMwpLog();
            _log = new ThrottledLog(_rawLog, MwpDefaults.LogThrottleSeconds);
        }

        /// <summary>
        /// Unthrottled sink used only for the one-shot save/config diagnostics that already carry
        /// their own throttle key semantics; the hot gameplay path always uses <see cref="Log"/>.
        /// </summary>
        public IMwpLog RawLog => _rawLog;

        /// <summary>Validated, immutable configuration for this session.</summary>
        public MwpConfig Config => _config;

        /// <summary>Session-wide throttled log shared by all pumps.</summary>
        public ThrottledLog Log => _log;

        /// <summary>Resolved balance values (vanilla-equivalent quality defaults, chapter 20.1.2).</summary>
        public BalanceSettings Balance => _config.Balance;

        /// <summary>Maintenance parameters for one tier, already validated/clamped by Core.</summary>
        /// <param name="tier">Tier id of the pump.</param>
        /// <param name="autoEnabled">Per-pump automatic-maintenance flag.</param>
        /// <returns>Immutable rules for the ledger.</returns>
        public MaintenanceRules RulesFor(PumpTier tier, bool autoEnabled) => _config.RulesFor(tier, autoEnabled);

        /// <summary>Water-body volume consumed per Water item, clamped to a usable positive value.</summary>
        public double CubicMetersPerWater => BalanceFormulas.Sanitize(
            _config.Balance.CubicMetersPerWater,
            MwpDefaults.CubicMetersPerWater,
            0.0001,
            1000.0);

        /// <summary>Contamination penalty coefficient (1.0 keeps parity with vanilla, DV-01).</summary>
        public double BadwaterPenalty => BalanceFormulas.Sanitize(
            _config.Balance.BadwaterPenalty,
            1.0,
            0.0,
            100.0);

        /// <summary>Lower bound of the water-quality multiplier (0.0 keeps parity with vanilla).</summary>
        public double MinWaterQuality => BalanceFormulas.Sanitize(
            _config.Balance.MinWaterQuality,
            0.0,
            0.0,
            1.0);

        /// <summary>Production step interval in seconds; clamped so a broken config cannot spin or stall.</summary>
        public double TickIntervalSeconds => BalanceFormulas.Sanitize(
            _config.Balance.TickIntervalSeconds,
            MwpDefaults.TickIntervalSeconds,
            0.1,
            60.0);

        /// <summary>Resolves the blueprint tier token, falling back to the Basic tier.</summary>
        /// <param name="tierName">Tier token from the custom spec.</param>
        /// <returns>A tier definition; never null.</returns>
        public TierDefinition TierFor(string? tierName) => TierCatalog.TryGet(tierName) ?? TierCatalog.Default[0];

        /// <summary>True when <c>features.uiDiagnostics</c> is on (REQ-OPS-04 output is opt-in).</summary>
        public bool DiagnosticsEnabled => _config.UiDiagnosticsEnabled;

        /// <summary>
        /// Records one measured pump step for the REQ-OPS-04 diagnostics. Called only when
        /// <see cref="DiagnosticsEnabled"/> is on, so the default tick path pays nothing. Every
        /// <see cref="DiagnosticsIntervalSeconds"/> it emits one <c>MWP/Info/Diag</c> line with the
        /// version, active pump count, adapter state and average measured tick cost.
        /// </summary>
        /// <param name="startTimestamp">Value from <see cref="Stopwatch.GetTimestamp"/> taken before the step.</param>
        public void RecordTick(long startTimestamp)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                long elapsed = now - startTimestamp;
                if (elapsed <= 0)
                {
                    return;
                }

                _measuredTickMilliseconds += elapsed * 1000.0 / Stopwatch.Frequency;
                _measuredTickCount++;
                if (_diagWindowStartTimestamp == 0L)
                {
                    _diagWindowStartTimestamp = now;
                    return;
                }

                double windowSeconds = (now - _diagWindowStartTimestamp) / (double)Stopwatch.Frequency;
                if (windowSeconds < DiagnosticsIntervalSeconds)
                {
                    return;
                }

                EmitDiagnostics();
                _diagWindowStartTimestamp = now;
                _measuredTickCount = 0L;
                _measuredTickMilliseconds = 0.0;
            }
            catch (Exception)
            {
                // Diagnostics must never break a tick (REQ-SAFE-02).
            }
        }

        /// <summary>Latest active pump count, reported by the registry (diagnostics only).</summary>
        /// <param name="count">Number of registered pumps.</param>
        public void ReportPumpCount(int count) => _reportedPumpCount = count < 0 ? 0 : count;

        /// <summary>Latest resolved Timber Pipes adapter state, reported by a pump (diagnostics only).</summary>
        /// <param name="state">Adapter state the pump resolved at activation.</param>
        public void ReportAdapterState(PipeAdapterState state) => _reportedAdapterState = state;

        private void EmitDiagnostics()
        {
            double averageMilliseconds = _measuredTickCount > 0L
                ? _measuredTickMilliseconds / _measuredTickCount
                : 0.0;
            try
            {
                _log.Info(LogKeyDiag,
                    "MWP " + MwpDefaults.ModVersion
                    + " pumps=" + _reportedPumpCount.ToString(CultureInfo.InvariantCulture)
                    + " adapter=" + _reportedAdapterState.ToString()
                    + " avgTickMs=" + averageMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)
                    + " samples=" + _measuredTickCount.ToString(CultureInfo.InvariantCulture)
                    + " legacyRates=" + (_config.Balance.UseLegacyRates ? "on" : "off"));
            }
            catch (Exception)
            {
                // Diagnostics only.
            }
        }

        /// <summary>
        /// Resolves the config-driven tier definition (chapter 20.1.2). The config tier table wins
        /// over a mismatching blueprint so one source of truth drives balance (REQ-NFR-04), exactly
        /// like the verified HDBF reference mod.
        /// </summary>
        /// <param name="tier">Tier id.</param>
        /// <returns>The resolved tier; falls back to the compiled catalogue when the table is short.</returns>
        public TierDefinition ResolveTier(PumpTier tier)
        {
            var tiers = _config.Balance.Tiers;
            int index = (int)tier;
            if (tiers != null && index >= 0 && index < tiers.Count)
            {
                return tiers[index];
            }

            return TierCatalog.Get(tier);
        }

        /// <inheritdoc />
        public void Load()
        {
            try
            {
                _config = MwpStartup.Config;
                _log = MwpStartup.Log;
                _rawLog = MwpStartup.RawLog;
                _log.Info(MwpLogKeys.Startup,
                    "MWP " + MwpDefaults.ModVersion + " ready; tiers=" + _config.Balance.Tiers.Count
                    + "; maintenance=" + (_config.MaintenanceEnabled ? "on" : "off")
                    + "; legacyRates=" + (_config.Balance.UseLegacyRates ? "on" : "off"));
            }
            catch (Exception exception)
            {
                // Configuration problems degrade to compiled defaults, never break loading.
                _config = MwpConfig.Default;
                try
                {
                    _log.Error(MwpLogKeys.Startup,
                        "runtime initialisation failed (" + exception.GetType().Name
                        + "); compiled defaults are used");
                }
                catch (Exception)
                {
                    // Diagnostics only.
                }
            }
        }
    }
}
