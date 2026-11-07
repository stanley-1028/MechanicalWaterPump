using System;

namespace MWP.Core
{
    /// <summary>
    /// Everything <see cref="PumpStateMachine"/> needs to know about one pump for one tick.
    /// Gameplay fills it from its cached component references; no game type leaks into Core.
    /// </summary>
    public readonly struct PumpStatusInput
    {
        /// <summary>Creates the input for one evaluation.</summary>
        /// <param name="isFinished">True once construction finished (REQ-BLD-08).</param>
        /// <param name="isPaused">True when the player paused this pump only.</param>
        /// <param name="powered">True when the mechanical node is active and powered.</param>
        /// <param name="supplyHp">Power actually delivered, in hp.</param>
        /// <param name="requiredHp">Nominal demand of this tier, in hp.</param>
        /// <param name="hasWater">True when the intake cell is underwater.</param>
        /// <param name="freeCapacity">Free slots in the internal Water inventory.</param>
        /// <param name="isAccessible">True when a road reaches the pickup point.</param>
        /// <param name="maintenancePhase">Current maintenance sub-state from the ledger.</param>
        /// <param name="condition">Current wear value 0..100.</param>
        public PumpStatusInput(
            bool isFinished,
            bool isPaused,
            bool powered,
            double supplyHp,
            int requiredHp,
            bool hasWater,
            int freeCapacity,
            bool isAccessible,
            MaintenancePhase maintenancePhase,
            double condition)
        {
            IsFinished = isFinished;
            IsPaused = isPaused;
            Powered = powered;
            SupplyHp = supplyHp;
            RequiredHp = requiredHp;
            HasWater = hasWater;
            FreeCapacity = freeCapacity;
            IsAccessible = isAccessible;
            MaintenancePhase = maintenancePhase;
            Condition = condition;
        }

        /// <summary>True once construction finished (REQ-BLD-08).</summary>
        public bool IsFinished { get; }

        /// <summary>True when the player paused this pump only.</summary>
        public bool IsPaused { get; }

        /// <summary>True when the mechanical node is active and powered.</summary>
        public bool Powered { get; }

        /// <summary>Power actually delivered, in hp.</summary>
        public double SupplyHp { get; }

        /// <summary>Nominal demand of this tier, in hp.</summary>
        public int RequiredHp { get; }

        /// <summary>True when the intake cell is underwater.</summary>
        public bool HasWater { get; }

        /// <summary>Free slots in the internal Water inventory.</summary>
        public int FreeCapacity { get; }

        /// <summary>True when a road reaches the pickup point.</summary>
        public bool IsAccessible { get; }

        /// <summary>Current maintenance sub-state from the ledger.</summary>
        public MaintenancePhase MaintenancePhase { get; }

        /// <summary>Current wear value 0..100.</summary>
        public double Condition { get; }
    }

    /// <summary>Result of one state evaluation: the highest-priority state plus the factors the UI shows.</summary>
    public readonly struct PumpStatus
    {
        internal PumpStatus(
            PumpState state,
            double powerRatio,
            double waterQuality,
            double conditionMultiplier,
            double effectiveWaterPerHour)
        {
            State = state;
            PowerRatio = powerRatio;
            WaterQuality = waterQuality;
            ConditionMultiplier = conditionMultiplier;
            EffectiveWaterPerHour = effectiveWaterPerHour;
        }

        /// <summary>Highest-priority state per <see cref="StatusPriority"/>.</summary>
        public PumpState State { get; }

        /// <summary>Delivered power ratio, 0..1.</summary>
        public double PowerRatio { get; }

        /// <summary>Water-quality multiplier, 0..1.</summary>
        public double WaterQuality { get; }

        /// <summary>Condition multiplier, 0..1.</summary>
        public double ConditionMultiplier { get; }

        /// <summary>
        /// Water per hour this pump actually yields now. It is 0 whenever the reported state
        /// blocks production (paused, unfinished, no power/water/access, tank full, maintaining,
        /// condition 0); warning-only maintenance states still produce.
        /// </summary>
        public double EffectiveWaterPerHour { get; }
    }

    /// <summary>
    /// Pure state machine for one pump. The check order is exactly the frozen
    /// <see cref="StatusPriority"/> order: NoPower &gt; NoWater &gt; NoAccess &gt; StorageFull &gt;
    /// maintenance states &gt; LowPower &gt; Paused &gt; Producing (REQ-UI-03).
    /// </summary>
    public static class PumpStateMachine
    {
        /// <summary>
        /// Evaluates one pump. Formula factors are always computed (the UI shows them even while
        /// stopped), while <see cref="PumpStatus.EffectiveWaterPerHour"/> is zeroed when the
        /// winning state prevents production.
        /// </summary>
        /// <param name="input">Cached pump observations for this tick.</param>
        /// <param name="tier">Blueprint tier of this pump.</param>
        /// <param name="contamination">Intake contamination 0..1.</param>
        /// <param name="badwaterPenalty">Configured contamination penalty.</param>
        /// <param name="minQuality">Configured quality floor.</param>
        /// <returns>The evaluated status.</returns>
        public static PumpStatus Evaluate(
            in PumpStatusInput input,
            TierDefinition tier,
            double contamination,
            double badwaterPenalty,
            double minQuality)
        {
            if (tier == null)
            {
                throw new ArgumentNullException(nameof(tier));
            }

            double requiredHp = input.RequiredHp > 0 ? input.RequiredHp : tier.PowerRequiredHp;
            double powerRatio = BalanceFormulas.PowerRatio(input.SupplyHp, requiredHp);
            double quality = BalanceFormulas.WaterQuality(contamination, badwaterPenalty, minQuality);
            double conditionMultiplier = BalanceFormulas.ConditionMultiplier(input.Condition);
            double rate = BalanceFormulas.EffectiveWaterPerHour(
                tier.WaterPerHour,
                powerRatio,
                quality,
                conditionMultiplier);
            PumpState state = DetermineState(in input, powerRatio);
            if (!Produces(state))
            {
                rate = 0.0;
            }

            return new PumpStatus(state, powerRatio, quality, conditionMultiplier, rate);
        }

        /// <summary>True when the state still yields water; warning-only maintenance states do.</summary>
        private static bool Produces(PumpState state)
        {
            return state == PumpState.Producing
                || state == PumpState.LowPower
                || state == PumpState.MaintenanceDue
                || state == PumpState.Critical
                || state == PumpState.MissingMaintenanceMaterials
                || state == PumpState.NoAccessForMaintenance;
        }

        /// <summary>Applies the frozen priority order to pick the single state to display.</summary>
        private static PumpState DetermineState(in PumpStatusInput input, double powerRatio)
        {
            if (!input.IsFinished)
            {
                return PumpState.Unfinished;
            }

            if (!input.Powered || powerRatio <= 0.0)
            {
                return PumpState.NoPower;
            }

            if (!input.HasWater)
            {
                return PumpState.NoWater;
            }

            if (!input.IsAccessible)
            {
                return PumpState.NoAccess;
            }

            if (input.FreeCapacity <= 0)
            {
                return PumpState.StorageFull;
            }

            PumpState? maintenance = MaintenanceState(in input);
            if (maintenance.HasValue)
            {
                return maintenance.Value;
            }

            if (powerRatio < 1.0)
            {
                return PumpState.LowPower;
            }

            return input.IsPaused ? PumpState.Paused : PumpState.Producing;
        }

        /// <summary>
        /// Maps the maintenance phase and condition to the highest-priority maintenance state,
        /// or null when the pump is in a normal wear band. Bands are [75,100], [50,75), [25,50),
        /// (0,25), 0 so <see cref="BalanceFormulas.ConditionMultiplier"/> stays consistent.
        /// </summary>
        private static PumpState? MaintenanceState(in PumpStatusInput input)
        {
            MaintenancePhase phase = input.MaintenancePhase;
            if (input.Condition <= 0.0 || phase == MaintenancePhase.NeedsMaintenance)
            {
                return PumpState.NeedsMaintenance;
            }

            if (phase == MaintenancePhase.Maintaining)
            {
                return PumpState.Maintaining;
            }

            if (phase == MaintenancePhase.MissingMaterials)
            {
                return PumpState.MissingMaintenanceMaterials;
            }

            if (phase == MaintenancePhase.NoAccess)
            {
                return PumpState.NoAccessForMaintenance;
            }

            if (phase == MaintenancePhase.Critical || input.Condition < 25.0)
            {
                return PumpState.Critical;
            }

            if (phase == MaintenancePhase.Due || input.Condition < 50.0)
            {
                return PumpState.MaintenanceDue;
            }

            return null;
        }
    }
}
