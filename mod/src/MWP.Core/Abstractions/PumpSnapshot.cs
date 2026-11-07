using System;

namespace MWP.Core
{
    /// <summary>Immutable read model of one pump, used by the entity panel (REQ-UI-01).</summary>
    public readonly struct PumpSnapshot
    {
        /// <summary>Creates a snapshot.</summary>
        public PumpSnapshot(
            PumpTier tier,
            PumpState state,
            double effectiveWaterPerHour,
            double powerRatio,
            double powerSupplyHp,
            int powerRequiredHp,
            double waterQuality,
            double condition,
            bool autoMaintenance,
            double maintenanceDaysLeft,
            bool maintenanceEnabled,
            double maintenanceProgress,
            int waterInStock,
            int waterCapacity,
            PipeAdapterState pipeAdapter,
            bool paused,
            bool isFinished)
        {
            Tier = tier;
            State = state;
            EffectiveWaterPerHour = effectiveWaterPerHour;
            PowerRatio = powerRatio;
            PowerSupplyHp = powerSupplyHp;
            PowerRequiredHp = powerRequiredHp;
            WaterQuality = waterQuality;
            Condition = condition;
            AutoMaintenance = autoMaintenance;
            MaintenanceDaysLeft = maintenanceDaysLeft;
            MaintenanceEnabled = maintenanceEnabled;
            MaintenanceProgress = maintenanceProgress;
            WaterInStock = waterInStock;
            WaterCapacity = waterCapacity;
            PipeAdapter = pipeAdapter;
            Paused = paused;
            IsFinished = isFinished;
        }

        /// <summary>Tier of this pump.</summary>
        public PumpTier Tier { get; }

        /// <summary>Highest-priority state.</summary>
        public PumpState State { get; }

        /// <summary>Actual Water per hour after power, quality, condition and maintenance multipliers.</summary>
        public double EffectiveWaterPerHour { get; }

        /// <summary>Delivered power divided by required power, clamped to 0..1.</summary>
        public double PowerRatio { get; }

        /// <summary>Power currently delivered to this pump, in hp.</summary>
        public double PowerSupplyHp { get; }

        /// <summary>Nominal power requirement, in hp.</summary>
        public int PowerRequiredHp { get; }

        /// <summary>Water quality multiplier, 0..1.</summary>
        public double WaterQuality { get; }

        /// <summary>Maintenance condition, 0..100.</summary>
        public double Condition { get; }

        /// <summary>Whether automatic maintenance is enabled for this pump.</summary>
        public bool AutoMaintenance { get; }

        /// <summary>Estimated game days until maintenance is due; 0 when due or in progress.</summary>
        public double MaintenanceDaysLeft { get; }

        /// <summary>
        /// Whether the maintenance system is enabled by config (REQ-MAINT-13). When false the panel
        /// hides the condition/material rows instead of showing a frozen 100% forever.
        /// </summary>
        public bool MaintenanceEnabled { get; }

        /// <summary>
        /// Progress of the ongoing maintenance downtime, 0..1 (0 when not maintaining). Used for
        /// the "Maintaining: {0}%" row required by requirements 20.2.5.
        /// </summary>
        public double MaintenanceProgress { get; }

        /// <summary>Water items currently in the internal output inventory.</summary>
        public int WaterInStock { get; }

        /// <summary>Internal output inventory capacity.</summary>
        public int WaterCapacity { get; }

        /// <summary>How the optional Timber Pipes integration resolved.</summary>
        public PipeAdapterState PipeAdapter { get; }

        /// <summary>Whether the player paused this pump only.</summary>
        public bool Paused { get; }

        /// <summary>Whether construction finished.</summary>
        public bool IsFinished { get; }
    }

    /// <summary>
    /// Read-only query service the UI assembly consumes without referencing the gameplay assembly
    /// (REQ-NFR-05). Implemented and registered by MWP.Gameplay.
    /// Entity ids are <see cref="Guid"/> because that is what
    /// <c>Timberborn.EntitySystem.EntityComponent.EntityId</c> exposes (verified in game IL);
    /// the game has no integer entity id.
    /// </summary>
    public interface IMwpPumpQuery
    {
        /// <summary>Tries to read the snapshot of the pump on the given entity.</summary>
        /// <param name="entityId">Entity id of the pump (<c>EntityComponent.EntityId</c>).</param>
        /// <param name="snapshot">Snapshot when the entity carries a pump controller.</param>
        bool TryGetSnapshot(Guid entityId, out PumpSnapshot snapshot);
    }
}
