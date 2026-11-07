using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>What the maintenance logic wants the gameplay layer to do this tick (REQ-MAINT-03..07).</summary>
    public enum MaintenanceAction
    {
        /// <summary>No state change; keep producing (or keep waiting).</summary>
        None = 0,

        /// <summary>Materials and path are fine: the caller must consume materials and call <see cref="MaintenanceLedger.BeginMaintenance"/>.</summary>
        Start = 1,

        /// <summary>Maintenance is wanted but the district inventory lacks the materials; nothing was consumed.</summary>
        BlockedByMaterials = 2,

        /// <summary>Materials exist but no road reaches the pickup point; nothing was consumed.</summary>
        BlockedByAccess = 3,

        /// <summary>The downtime timer reached zero; the caller should call <see cref="MaintenanceLedger.CompleteMaintenance"/>.</summary>
        Completed = 4
    }

    /// <summary>Outcome of <see cref="MaintenanceLedger.Decide"/>: the action plus the resulting phase.</summary>
    public readonly struct MaintenanceDecision
    {
        internal MaintenanceDecision(MaintenanceAction action, MaintenancePhase phase)
        {
            Action = action;
            Phase = phase;
        }

        /// <summary>What the caller must do next.</summary>
        public MaintenanceAction Action { get; }

        /// <summary>Maintenance phase after the decision.</summary>
        public MaintenancePhase Phase { get; }
    }

    /// <summary>
    /// Immutable maintenance parameters for one pump, built from <see cref="MwpConfig"/> or the
    /// blueprint spec (REQ-NFR-04). Values are validated by the caller; the ledger still clamps
    /// defensively.
    /// </summary>
    public readonly struct MaintenanceRules
    {
        /// <summary>Creates the rules for one pump.</summary>
        /// <param name="enabled">False disables the whole maintenance system for this pump (REQ-MAINT-13).</param>
        /// <param name="auto">False requires a manual request; the per-pump auto flag still applies.</param>
        /// <param name="autoThreshold">Condition at or below which automatic maintenance becomes due.</param>
        /// <param name="durationHours">Downtime of one maintenance cycle, in game hours (default 2).</param>
        /// <param name="intervalDays">Days of Producing time a full condition lasts (default 8/10/12/15).</param>
        /// <param name="materials">Materials consumed per maintenance; null is treated as empty.</param>
        public MaintenanceRules(
            bool enabled,
            bool auto,
            double autoThreshold,
            double durationHours,
            double intervalDays,
            IReadOnlyList<GoodStack> materials)
        {
            Enabled = enabled;
            Auto = auto;
            AutoThreshold = autoThreshold;
            DurationHours = durationHours;
            IntervalDays = intervalDays;
            Materials = materials ?? Array.Empty<GoodStack>();
        }

        /// <summary>False disables maintenance entirely for this pump (REQ-MAINT-13).</summary>
        public bool Enabled { get; }

        /// <summary>Whether automatic maintenance may trigger at all.</summary>
        public bool Auto { get; }

        /// <summary>Condition at or below which automatic maintenance becomes due, in 0..100.</summary>
        public double AutoThreshold { get; }

        /// <summary>Downtime of one maintenance cycle, in game hours.</summary>
        public double DurationHours { get; }

        /// <summary>Days of Producing time a full condition lasts.</summary>
        public double IntervalDays { get; }

        /// <summary>Materials consumed per maintenance.</summary>
        public IReadOnlyList<GoodStack> Materials { get; }
    }

    /// <summary>Environment facts the maintenance decision needs (REQ-MAINT-06).</summary>
    public readonly struct MaintenanceContext
    {
        /// <summary>Creates the context for one decision.</summary>
        /// <param name="hasMaterials">Whether the district holds every required material.</param>
        /// <param name="isReachable">Whether a road reaches the pump's pickup point.</param>
        /// <param name="manualRequest">True when the player pressed "maintain now".</param>
        public MaintenanceContext(bool hasMaterials, bool isReachable, bool manualRequest)
        {
            HasMaterials = hasMaterials;
            IsReachable = isReachable;
            ManualRequest = manualRequest;
        }

        /// <summary>Whether the district holds every required material.</summary>
        public bool HasMaterials { get; }

        /// <summary>Whether a road reaches the pump's pickup point.</summary>
        public bool IsReachable { get; }

        /// <summary>True when the player pressed "maintain now".</summary>
        public bool ManualRequest { get; }
    }

    /// <summary>
    /// Pure wear/maintenance state machine for one pump (REQ-MAINT-01..07, REQ-MAINT-11). It owns
    /// condition, the auto flag, the persisted phase and the downtime timer; it never touches game
    /// objects, so the whole flow is unit-testable.
    /// </summary>
    public sealed class MaintenanceLedger
    {
        private const double MaxIntervalDays = 100000.0;
        private const double MaxDurationHours = 720.0;

        private double _condition = 100.0;
        private bool _autoMaintenance = true;
        private MaintenancePhase _phase = MaintenancePhase.None;
        private double _remainingHours;

        /// <summary>Current wear value, 0..100 (REQ-MAINT-01).</summary>
        public double Condition => _condition;

        /// <summary>Per-pump automatic maintenance flag; persisted in the save file (REQ-MAINT-05).</summary>
        public bool AutoMaintenance
        {
            get => _autoMaintenance;
            set => _autoMaintenance = value;
        }

        /// <summary>Current maintenance sub-state (persisted in schema v2).</summary>
        public MaintenancePhase Phase => _phase;

        /// <summary>Remaining downtime in game hours; 0 unless <see cref="Phase"/> is Maintaining.</summary>
        public double RemainingHours => _remainingHours;

        /// <summary>
        /// Restores the ledger from save data (REQ-MAINT-11). Invalid values fall back to safe
        /// defaults instead of throwing; downtime is only kept while actually maintaining.
        /// </summary>
        /// <param name="condition">Stored condition 0..100.</param>
        /// <param name="auto">Stored automatic-maintenance flag.</param>
        /// <param name="phase">Stored maintenance phase.</param>
        /// <param name="remainingHours">Stored remaining downtime in game hours.</param>
        public void Reset(double condition, bool auto, MaintenancePhase phase, double remainingHours)
        {
            _condition = BalanceFormulas.Sanitize(condition, 100.0, 0.0, 100.0);
            _autoMaintenance = auto;
            _phase = Enum.IsDefined(typeof(MaintenancePhase), phase) ? phase : MaintenancePhase.None;
            _remainingHours = _phase == MaintenancePhase.Maintaining
                ? BalanceFormulas.Sanitize(remainingHours, 0.0, 0.0, MaxDurationHours)
                : 0.0;
        }

        /// <summary>
        /// Applies wear for Producing hours only (REQ-MAINT-01): <c>100 / (intervalDays * 24)</c>
        /// per hour. Negative, NaN or infinite hours are ignored; condition never drops below 0.
        /// </summary>
        /// <param name="producingHours">Game hours the pump spent in a producing state.</param>
        /// <param name="intervalDays">Configured maintenance interval for this tier.</param>
        public void AccumulateWear(double producingHours, double intervalDays)
        {
            if (double.IsNaN(producingHours) || producingHours <= 0.0 || _phase == MaintenancePhase.Maintaining)
            {
                return;
            }

            double hours = BalanceFormulas.Sanitize(producingHours, 0.0, 0.0, MaxIntervalDays * MwpDefaults.HoursPerDay);
            double wear = BalanceFormulas.WearPerHour(intervalDays) * hours;
            _condition = BalanceFormulas.Sanitize(_condition - wear, 0.0, 0.0, 100.0);
        }

        /// <summary>
        /// Decides this tick's maintenance action and updates <see cref="Phase"/> accordingly.
        /// Automatic maintenance triggers at or below <see cref="MaintenanceRules.AutoThreshold"/>
        /// while the per-pump flag is on (REQ-MAINT-05); a blocked request stays pending until the
        /// material/path problem is fixed (REQ-MAINT-06).
        /// </summary>
        /// <param name="context">Materials/path/manual-request facts.</param>
        /// <param name="rules">Maintenance parameters of this pump.</param>
        /// <returns>The action for the gameplay layer plus the resulting phase.</returns>
        public MaintenanceDecision Decide(in MaintenanceContext context, MaintenanceRules rules)
        {
            if (!rules.Enabled)
            {
                DisableMaintenance();
                return new MaintenanceDecision(MaintenanceAction.None, _phase);
            }

            if (_phase == MaintenancePhase.Maintaining)
            {
                return new MaintenanceDecision(_remainingHours <= 0.0 ? MaintenanceAction.Completed : MaintenanceAction.None, _phase);
            }

            double threshold = BalanceFormulas.Sanitize(rules.AutoThreshold, 50.0, 0.0, 100.0);
            bool pending = _phase == MaintenancePhase.MissingMaterials || _phase == MaintenancePhase.NoAccess;
            bool wanted = context.ManualRequest
                || pending
                || (rules.Auto && _autoMaintenance && _condition <= threshold);

            if (!wanted)
            {
                _phase = PhaseForCondition();
                return new MaintenanceDecision(MaintenanceAction.None, _phase);
            }

            return StartOrBlock(in context);
        }

        /// <summary>Starts maintenance, or reports why the materials/path are missing.</summary>
        private MaintenanceDecision StartOrBlock(in MaintenanceContext context)
        {
            if (!context.HasMaterials)
            {
                _phase = MaintenancePhase.MissingMaterials;
                return new MaintenanceDecision(MaintenanceAction.BlockedByMaterials, _phase);
            }

            if (!context.IsReachable)
            {
                _phase = MaintenancePhase.NoAccess;
                return new MaintenanceDecision(MaintenanceAction.BlockedByAccess, _phase);
            }

            _phase = PhaseForCondition();
            return new MaintenanceDecision(MaintenanceAction.Start, _phase);
        }

        /// <summary>
        /// Enters the downtime phase. The caller must consume the materials first (REQ-MAINT-04);
        /// repeated calls while already maintaining keep the running timer.
        /// </summary>
        /// <param name="rules">Rules providing the downtime duration.</param>
        public void BeginMaintenance(MaintenanceRules rules)
        {
            if (!rules.Enabled || _phase == MaintenancePhase.Maintaining)
            {
                return;
            }

            _phase = MaintenancePhase.Maintaining;
            _remainingHours = BalanceFormulas.Sanitize(rules.DurationHours, 0.0, 0.0, MaxDurationHours);
        }

        /// <summary>Advances the downtime timer; never below zero (REQ-MAINT-04).</summary>
        /// <param name="hours">Elapsed game hours since the previous advance.</param>
        public void AdvanceMaintenance(double hours)
        {
            if (_phase != MaintenancePhase.Maintaining)
            {
                return;
            }

            double delta = BalanceFormulas.Sanitize(hours, 0.0, 0.0, MaxDurationHours);
            _remainingHours = BalanceFormulas.Sanitize(_remainingHours - delta, 0.0, 0.0, MaxDurationHours);
        }

        /// <summary>Finishes maintenance: condition returns to 100 and the phase to None (REQ-MAINT-04).</summary>
        public void CompleteMaintenance()
        {
            _condition = 100.0;
            _phase = MaintenancePhase.None;
            _remainingHours = 0.0;
        }

        /// <summary>REQ-MAINT-13: disabling maintenance must not leave a half-applied state.</summary>
        private void DisableMaintenance()
        {
            _condition = 100.0;
            _phase = MaintenancePhase.None;
            _remainingHours = 0.0;
        }

        /// <summary>Maps condition to the reporting phase used while no maintenance is running.</summary>
        private MaintenancePhase PhaseForCondition()
        {
            if (_condition <= 0.0)
            {
                return MaintenancePhase.NeedsMaintenance;
            }

            if (_condition < 25.0)
            {
                return MaintenancePhase.Critical;
            }

            return _condition < 50.0 ? MaintenancePhase.Due : MaintenancePhase.None;
        }
    }
}
