using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MWP.Core
{
    /// <summary>
    /// Validated in-memory view of <c>MWP_Config.json</c> (config schema 2, requirements chapter
    /// 20.6). <see cref="MwpConfigCodec.Parse"/> produces it; gameplay reads it, it is never
    /// mutated after construction (REQ-NFR-09).
    /// </summary>
    public sealed class MwpConfig
    {
        /// <summary>Default condition at or below which automatic maintenance becomes due.</summary>
        internal const double DefaultAutoThreshold = 50.0;

        /// <summary>Default maintenance downtime in game hours (REQ-MAINT-04).</summary>
        internal const double DefaultMaintenanceDurationHours = 2.0;

        /// <summary>Timber Pipes build this mod was verified against (requirements 2.4).</summary>
        internal const string DefaultTimberPipesMinVersion = "11.4.0";

        internal MwpConfig(
            int configSchemaVersion,
            bool pipeAdapterEnabled,
            bool directPipeOutputEnabled,
            bool uiDiagnosticsEnabled,
            bool maintenanceEnabled,
            bool maintenanceAuto,
            double maintenanceAutoThreshold,
            double maintenanceDurationHours,
            IReadOnlyList<double> maintenanceIntervalsDays,
            IReadOnlyList<IReadOnlyList<GoodStack>> maintenanceMaterials,
            BalanceSettings balance,
            string timberPipesMinVersion,
            double logThrottleSeconds)
        {
            ConfigSchemaVersion = configSchemaVersion;
            PipeAdapterEnabled = pipeAdapterEnabled;
            DirectPipeOutputEnabled = directPipeOutputEnabled;
            UiDiagnosticsEnabled = uiDiagnosticsEnabled;
            MaintenanceEnabled = maintenanceEnabled;
            MaintenanceAuto = maintenanceAuto;
            MaintenanceAutoThreshold = maintenanceAutoThreshold;
            MaintenanceDurationHours = maintenanceDurationHours;
            MaintenanceIntervalsDays = maintenanceIntervalsDays ?? throw new ArgumentNullException(nameof(maintenanceIntervalsDays));
            MaintenanceMaterials = maintenanceMaterials ?? throw new ArgumentNullException(nameof(maintenanceMaterials));
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
            TimberPipesMinVersion = timberPipesMinVersion ?? throw new ArgumentNullException(nameof(timberPipesMinVersion));
            LogThrottleSeconds = logThrottleSeconds;
        }

        /// <summary>Schema version the file was written with; migration fills missing chapters.</summary>
        public int ConfigSchemaVersion { get; }

        /// <summary>Whether the optional Timber Pipes valve adapter is enabled (REQ-COMPAT-02).</summary>
        public bool PipeAdapterEnabled { get; }

        /// <summary>Whether the optional direct-pipe output path may be reported (SHOULD path).</summary>
        public bool DirectPipeOutputEnabled { get; }

        /// <summary>Whether diagnostic rows are shown in the entity panel.</summary>
        public bool UiDiagnosticsEnabled { get; }

        /// <summary>Master switch for the maintenance system (REQ-MAINT-13).</summary>
        public bool MaintenanceEnabled { get; }

        /// <summary>Whether automatic maintenance triggers at all (per-pump flag still applies).</summary>
        public bool MaintenanceAuto { get; }

        /// <summary>Condition at or below which automatic maintenance becomes due, 0..100.</summary>
        public double MaintenanceAutoThreshold { get; }

        /// <summary>Downtime of one maintenance cycle, in game hours (default 2).</summary>
        public double MaintenanceDurationHours { get; }

        /// <summary>Maintenance interval per tier in game days, index = <see cref="PumpTier"/>.</summary>
        public IReadOnlyList<double> MaintenanceIntervalsDays { get; }

        /// <summary>Maintenance materials per tier, index = <see cref="PumpTier"/>.</summary>
        public IReadOnlyList<IReadOnlyList<GoodStack>> MaintenanceMaterials { get; }

        /// <summary>Resolved production balance (chapter 20.1.2 defaults).</summary>
        public BalanceSettings Balance { get; }

        /// <summary>Oldest Timber Pipes version this mod is verified against.</summary>
        public string TimberPipesMinVersion { get; }

        /// <summary>Log throttle window in seconds, per key (REQ-OPS-03).</summary>
        public double LogThrottleSeconds { get; }

        /// <summary>The shipped defaults matching <c>MWP_Config.json</c> (schema 2).</summary>
        public static MwpConfig Default { get; } = new MwpConfig(
            MwpDefaults.ConfigSchemaVersion,
            pipeAdapterEnabled: true,
            directPipeOutputEnabled: false,
            uiDiagnosticsEnabled: false,
            maintenanceEnabled: true,
            maintenanceAuto: true,
            maintenanceAutoThreshold: DefaultAutoThreshold,
            maintenanceDurationHours: DefaultMaintenanceDurationHours,
            maintenanceIntervalsDays: DefaultIntervals(),
            maintenanceMaterials: DefaultMaterials(),
            balance: BalanceSettings.Default,
            timberPipesMinVersion: DefaultTimberPipesMinVersion,
            logThrottleSeconds: MwpDefaults.LogThrottleSeconds);

        /// <summary>Returns the maintenance rules of one tier, ready for <see cref="MaintenanceLedger"/>.</summary>
        /// <param name="tier">Tier id; out-of-range values fall back to Basic.</param>
        /// <param name="autoEnabled">Value of the per-pump auto-maintenance flag.</param>
        /// <returns>Immutable rules for that tier.</returns>
        public MaintenanceRules RulesFor(PumpTier tier, bool autoEnabled)
        {
            int index = (int)tier;
            if (index < 0 || index >= MaintenanceIntervalsDays.Count)
            {
                index = 0;
            }

            return new MaintenanceRules(
                MaintenanceEnabled,
                MaintenanceAuto,
                MaintenanceAutoThreshold,
                MaintenanceDurationHours,
                MaintenanceIntervalsDays[index],
                MaintenanceMaterials[index]);
        }

        /// <summary>Default intervals are the tier table's maintenance intervals (8/10/12/15).</summary>
        private static IReadOnlyList<double> DefaultIntervals()
        {
            var intervals = new double[TierCatalog.Default.Count];
            for (int i = 0; i < intervals.Length; i++)
            {
                intervals[i] = TierCatalog.Default[i].MaintenanceIntervalDays;
            }

            return new ReadOnlyCollection<double>(intervals);
        }

        /// <summary>Default materials are the tier table's maintenance materials.</summary>
        private static IReadOnlyList<IReadOnlyList<GoodStack>> DefaultMaterials()
        {
            var sets = new IReadOnlyList<GoodStack>[TierCatalog.Default.Count];
            for (int i = 0; i < sets.Length; i++)
            {
                sets[i] = TierCatalog.Default[i].MaintenanceMaterials;
            }

            return new ReadOnlyCollection<IReadOnlyList<GoodStack>>(sets);
        }
    }
}
