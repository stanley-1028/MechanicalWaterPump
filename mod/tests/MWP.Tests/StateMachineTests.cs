using MWP.Core;

namespace MWP.Tests
{
    /// <summary>Unit tests for <see cref="PumpStateMachine"/>: the frozen priority order and the effective rate.</summary>
    internal static class StateMachineTests
    {
        private static readonly TierDefinition Basic = TierCatalog.Get(PumpTier.Basic);

        internal static void Run(TestRunner t)
        {
            PriorityTests(t);
            MaintenanceStateTests(t);
            EffectiveRateTests(t);
            MonotonicPowerTests(t);
        }

        private static void PriorityTests(TestRunner t)
        {
            UnfinishedPowerTests(t);
            WaterAccessStorageTests(t);
            MaintenancePriorityTests(t);
            BandPriorityTests(t);

        }

        private static void MaintenanceStateTests(TestRunner t)
        {
            t.Run("Blocked by materials phase", () =>
                Assert.Equal(PumpState.MissingMaintenanceMaterials, Evaluate(Input(phase: MaintenancePhase.MissingMaterials, condition: 40.0)).State, "missing materials"));
            t.Run("Blocked by access phase", () =>
                Assert.Equal(PumpState.NoAccessForMaintenance, Evaluate(Input(phase: MaintenancePhase.NoAccess, condition: 40.0)).State, "no access for maintenance"));
            t.Run("Due phase forces MaintenanceDue before the band", () =>
                Assert.Equal(PumpState.MaintenanceDue, Evaluate(Input(phase: MaintenancePhase.Due, condition: 60.0)).State, "due phase"));
            t.Run("Critical phase forces Critical", () =>
                Assert.Equal(PumpState.Critical, Evaluate(Input(phase: MaintenancePhase.Critical, condition: 60.0)).State, "critical phase"));
        }

        private static void EffectiveRateTests(TestRunner t)
        {
            t.Run("Rated output at full power", () =>
                Assert.Near(3.0, Evaluate(Input()).EffectiveWaterPerHour, 1e-12, "rated"));
            t.Run("Zero power yields zero water", () =>
                Assert.Near(0.0, Evaluate(Input(powered: false)).EffectiveWaterPerHour, 1e-12, "no power"));
            t.Run("Half power yields half water", () =>
                Assert.Near(1.5, Evaluate(Input(supply: 50.0, condition: 100.0)).EffectiveWaterPerHour, 1e-12, "half power"));
            t.Run("Condition 60 scales by 0.90", () =>
                Assert.Near(2.7, Evaluate(Input(condition: 60.0)).EffectiveWaterPerHour, 1e-12, "0.9"));
            t.Run("Condition 40 scales by 0.75", () =>
                Assert.Near(2.25, Evaluate(Input(condition: 40.0)).EffectiveWaterPerHour, 1e-12, "0.75"));
            t.Run("Condition 10 scales by 0.50", () =>
                Assert.Near(1.5, Evaluate(Input(condition: 10.0)).EffectiveWaterPerHour, 1e-12, "0.5"));
            t.Run("Condition 0 stops production", () =>
                Assert.Near(0.0, Evaluate(Input(condition: 0.0)).EffectiveWaterPerHour, 1e-12, "0"));
            t.Run("Half bad water halves output with default curve", () =>
                Assert.Near(1.5, Evaluate(Input(), contamination: 0.5).EffectiveWaterPerHour, 1e-12, "quality 0.5"));
            t.Run("Configured 0.75/0.25 curve floors at 25%", () =>
                Assert.Near(0.75, Evaluate(Input(), contamination: 1.0, penalty: 0.75, minQuality: 0.25).EffectiveWaterPerHour, 1e-12, "configured quality"));
            t.Run("StorageFull reports zero output", () =>
                Assert.Near(0.0, Evaluate(Input(free: 0)).EffectiveWaterPerHour, 1e-12, "full tank"));
            t.Run("NoWater reports zero output", () =>
                Assert.Near(0.0, Evaluate(Input(water: false), contamination: 0.0).EffectiveWaterPerHour, 1e-12, "no water"));
            t.Run("Paused reports zero output", () =>
                Assert.Near(0.0, Evaluate(Input(paused: true)).EffectiveWaterPerHour, 1e-12, "paused"));
            t.Run("LowPower still reports its scaled output", () =>
                Assert.Equal(PumpState.LowPower, Evaluate(Input(supply: 25.0)).State, "low power state"));
            t.Run("Required hp falls back to the tier value when input is zero", () =>
                Assert.Near(0.5, Evaluate(Input(supply: 50.0, required: 0)).PowerRatio, 1e-12, "tier demand"));
        }

        private static void MonotonicPowerTests(TestRunner t)
        {
            t.Run("Output is monotonic in supply", () =>
            {
                double previous = -1.0;
                for (int step = 1; step <= 10; step++)
                {
                    PumpStatus status = Evaluate(Input(supply: step * 10.0));
                    Assert.True(status.EffectiveWaterPerHour >= previous, "rate must not decrease at step " + step);
                    Assert.True(status.EffectiveWaterPerHour <= 3.0 + 1e-12, "rate must not exceed rated at step " + step);
                    previous = status.EffectiveWaterPerHour;
                }

                Assert.Near(3.0, previous, 1e-12, "100% supply reaches rated output");
            });
        }

        private static PumpStatus Evaluate(
            in PumpStatusInput input,
            double contamination = 0.0,
            double penalty = 1.0,
            double minQuality = 0.0)
        {
            return PumpStateMachine.Evaluate(in input, Basic, contamination, penalty, minQuality);
        }

        private static PumpStatusInput Input(
            bool finished = true,
            bool paused = false,
            bool powered = true,
            double supply = 100.0,
            int required = 100,
            bool water = true,
            int free = 10,
            bool accessible = true,
            MaintenancePhase phase = MaintenancePhase.None,
            double condition = 100.0)
        {
            return new PumpStatusInput(finished, paused, powered, supply, required, water, free, accessible, phase, condition);
        }
        /// <summary>Test group: UnfinishedPowerTests.</summary>
        private static void UnfinishedPowerTests(TestRunner t)
        {
            t.Run("Unfinished wins over everything", () =>
                Assert.Equal(PumpState.Unfinished, Evaluate(Input(finished: false, powered: false, water: false, accessible: false, free: 0, condition: 0)).State, "unfinished"));

            t.Run("NoPower outranks NoWater/NoAccess/StorageFull", () =>
                Assert.Equal(PumpState.NoPower, Evaluate(Input(powered: false, water: false, accessible: false, free: 0, condition: 0)).State, "NoPower"));
            t.Run("NoPower when supply is zero", () =>
                Assert.Equal(PumpState.NoPower, Evaluate(Input(supply: 0.0)).State, "supply 0"));
            t.Run("NoPower when supply is NaN", () =>
                Assert.Equal(PumpState.NoPower, Evaluate(Input(supply: double.NaN)).State, "supply NaN"));
        }

        /// <summary>Test group: WaterAccessStorageTests.</summary>
        private static void WaterAccessStorageTests(TestRunner t)
        {
            t.Run("NoWater outranks NoAccess/StorageFull", () =>
                Assert.Equal(PumpState.NoWater, Evaluate(Input(water: false, accessible: false, free: 0, condition: 0)).State, "NoWater"));
            t.Run("NoAccess outranks StorageFull", () =>
                Assert.Equal(PumpState.NoAccess, Evaluate(Input(accessible: false, free: 0, condition: 0)).State, "NoAccess"));
            t.Run("StorageFull outranks maintenance", () =>
                Assert.Equal(PumpState.StorageFull, Evaluate(Input(free: 0, condition: 0)).State, "StorageFull"));
        }

        /// <summary>Test group: MaintenancePriorityTests.</summary>
        private static void MaintenancePriorityTests(TestRunner t)
        {
            t.Run("NeedsMaintenance when condition is zero", () =>
                Assert.Equal(PumpState.NeedsMaintenance, Evaluate(Input(condition: 0.0)).State, "condition 0"));
            t.Run("Maintaining state is reported", () =>
                Assert.Equal(PumpState.Maintaining, Evaluate(Input(phase: MaintenancePhase.Maintaining)).State, "maintaining"));
            t.Run("Maintenance warning outranks LowPower", () =>
                Assert.Equal(PumpState.MissingMaintenanceMaterials, Evaluate(Input(supply: 50.0, phase: MaintenancePhase.MissingMaterials, condition: 40.0)).State, "materials > low power"));
            t.Run("Maintenance warning outranks Paused", () =>
                Assert.Equal(PumpState.Maintaining, Evaluate(Input(paused: true, phase: MaintenancePhase.Maintaining)).State, "maintaining > paused"));
            t.Run("Critical band maps to Critical", () =>
                Assert.Equal(PumpState.Critical, Evaluate(Input(condition: 10.0)).State, "critical"));
            t.Run("Wear band 25..50 maps to MaintenanceDue", () =>
                Assert.Equal(PumpState.MaintenanceDue, Evaluate(Input(condition: 40.0)).State, "due"));
        }

        /// <summary>Test group: BandPriorityTests.</summary>
        private static void BandPriorityTests(TestRunner t)
        {
            t.Run("LowPower outranks Paused", () =>
                Assert.Equal(PumpState.LowPower, Evaluate(Input(paused: true, supply: 50.0)).State, "low power > paused"));
            t.Run("Paused with full power", () =>
                Assert.Equal(PumpState.Paused, Evaluate(Input(paused: true)).State, "paused"));
            t.Run("Producing at full power, clean water, condition 100", () =>
                Assert.Equal(PumpState.Producing, Evaluate(Input()).State, "producing"));
            t.Run("Condition 75 is still the normal band", () =>
                Assert.Equal(PumpState.Producing, Evaluate(Input(condition: 75.0)).State, "75"));
            t.Run("Condition 74.9 enters the worn band without a maintenance state", () =>
                Assert.Equal(PumpState.Producing, Evaluate(Input(condition: 74.9)).State, "worn but no state"));
        }
    }
}
