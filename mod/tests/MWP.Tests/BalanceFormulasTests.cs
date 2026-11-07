using MWP.Core;

namespace MWP.Tests
{
    /// <summary>Unit tests for <see cref="BalanceFormulas"/>: clamping, NaN/Inf/negative safety and the balance curves.</summary>
    internal static class BalanceFormulasTests
    {
        internal static void Run(TestRunner t)
        {
            SanitizeTests(t);
            PowerRatioTests(t);
            WaterQualityTests(t);
            ConditionTests(t);
            ProductionTests(t);
            MaintenanceFormulaTests(t);
        }

        private static void SanitizeTests(TestRunner t)
        {
            t.Run("Sanitize NaN uses fallback", () =>
                Assert.Near(50.0, BalanceFormulas.Sanitize(double.NaN, 50.0, 0.0, 100.0), 1e-12, "NaN fallback"));
            t.Run("Sanitize +Inf clamps to max", () =>
                Assert.Near(100.0, BalanceFormulas.Sanitize(double.PositiveInfinity, 50.0, 0.0, 100.0), 1e-12, "+Inf"));
            t.Run("Sanitize -Inf clamps to min", () =>
                Assert.Near(0.0, BalanceFormulas.Sanitize(double.NegativeInfinity, 50.0, 0.0, 100.0), 1e-12, "-Inf"));
            t.Run("Sanitize negative clamps to min", () =>
                Assert.Near(0.0, BalanceFormulas.Sanitize(-5.0, 10.0, 0.0, 100.0), 1e-12, "negative"));
            t.Run("Sanitize above max clamps", () =>
                Assert.Near(100.0, BalanceFormulas.Sanitize(250.0, 10.0, 0.0, 100.0), 1e-12, "above max"));
            t.Run("Sanitize keeps in-range value", () =>
                Assert.Near(42.5, BalanceFormulas.Sanitize(42.5, 10.0, 0.0, 100.0), 1e-12, "in range"));
            t.Run("Sanitize clamps an out-of-range fallback", () =>
                Assert.Near(100.0, BalanceFormulas.Sanitize(double.NaN, 500.0, 0.0, 100.0), 1e-12, "fallback clamp"));
            t.Run("Sanitize swaps an inverted range", () =>
                Assert.Near(5.0, BalanceFormulas.Sanitize(5.0, 0.0, 100.0, 0.0), 1e-12, "swapped range"));
        }

        private static void PowerRatioTests(TestRunner t)
        {
            t.Run("PowerRatio full supply is 1", () =>
                Assert.Near(1.0, BalanceFormulas.PowerRatio(100.0, 100.0), 1e-12, "full"));
            t.Run("PowerRatio half supply is 0.5", () =>
                Assert.Near(0.5, BalanceFormulas.PowerRatio(50.0, 100.0), 1e-12, "half"));
            t.Run("PowerRatio zero supply is 0", () =>
                Assert.Near(0.0, BalanceFormulas.PowerRatio(0.0, 100.0), 1e-12, "zero"));
            t.Run("PowerRatio negative supply is 0", () =>
                Assert.Near(0.0, BalanceFormulas.PowerRatio(-10.0, 100.0), 1e-12, "negative supply"));
            t.Run("PowerRatio over-supply clamps to 1", () =>
                Assert.Near(1.0, BalanceFormulas.PowerRatio(300.0, 100.0), 1e-12, "over"));
            t.Run("PowerRatio zero demand is 0 (no free power)", () =>
                Assert.Near(0.0, BalanceFormulas.PowerRatio(100.0, 0.0), 1e-12, "zero demand"));
            t.Run("PowerRatio NaN supply is 0", () =>
                Assert.Near(0.0, BalanceFormulas.PowerRatio(double.NaN, 100.0), 1e-12, "NaN supply"));
            t.Run("PowerRatio +Inf supply clamps to 1", () =>
                Assert.Near(1.0, BalanceFormulas.PowerRatio(double.PositiveInfinity, 100.0), 1e-12, "Inf supply"));
            t.Run("PowerRatio NaN demand is 0", () =>
                Assert.Near(0.0, BalanceFormulas.PowerRatio(100.0, double.NaN), 1e-12, "NaN demand"));
        }

        private static void WaterQualityTests(TestRunner t)
        {
            t.Run("WaterQuality vanilla curve: clean is 1", () =>
                Assert.Near(1.0, BalanceFormulas.WaterQuality(0.0, 1.0, 0.0), 1e-12, "clean"));
            t.Run("WaterQuality vanilla curve: half bad is 0.5", () =>
                Assert.Near(0.5, BalanceFormulas.WaterQuality(0.5, 1.0, 0.0), 1e-12, "half"));
            t.Run("WaterQuality vanilla curve: fully bad is 0", () =>
                Assert.Near(0.0, BalanceFormulas.WaterQuality(1.0, 1.0, 0.0), 1e-12, "bad"));
            t.Run("WaterQuality configurable 0.75/0.25 floor at 0.25", () =>
                Assert.Near(0.25, BalanceFormulas.WaterQuality(1.0, 0.75, 0.25), 1e-12, "configured floor"));
            t.Run("WaterQuality configurable 0.75 curve at half", () =>
                Assert.Near(0.625, BalanceFormulas.WaterQuality(0.5, 0.75, 0.25), 1e-12, "configured half"));
            t.Run("WaterQuality negative contamination is clean", () =>
                Assert.Near(1.0, BalanceFormulas.WaterQuality(-0.5, 1.0, 0.0), 1e-12, "negative"));
            t.Run("WaterQuality NaN contamination is treated as fully bad", () =>
                Assert.Near(0.0, BalanceFormulas.WaterQuality(double.NaN, 1.0, 0.0), 1e-12, "NaN"));
            t.Run("WaterQuality above-one contamination clamps to worst", () =>
                Assert.Near(0.0, BalanceFormulas.WaterQuality(7.0, 1.0, 0.0), 1e-12, "over bad"));
        }

        private static void ConditionTests(TestRunner t)
        {
            t.Run("ConditionMultiplier 100 is 1.00", () =>
                Assert.Near(1.00, BalanceFormulas.ConditionMultiplier(100.0), 1e-12, "100"));
            t.Run("ConditionMultiplier 75 is 1.00", () =>
                Assert.Near(1.00, BalanceFormulas.ConditionMultiplier(75.0), 1e-12, "75"));
            t.Run("ConditionMultiplier 60 is 0.90", () =>
                Assert.Near(0.90, BalanceFormulas.ConditionMultiplier(60.0), 1e-12, "60"));
            t.Run("ConditionMultiplier 50 is 0.90", () =>
                Assert.Near(0.90, BalanceFormulas.ConditionMultiplier(50.0), 1e-12, "50"));
            t.Run("ConditionMultiplier 40 is 0.75", () =>
                Assert.Near(0.75, BalanceFormulas.ConditionMultiplier(40.0), 1e-12, "40"));
            t.Run("ConditionMultiplier 25 is 0.75", () =>
                Assert.Near(0.75, BalanceFormulas.ConditionMultiplier(25.0), 1e-12, "25"));
            t.Run("ConditionMultiplier 10 is 0.50", () =>
                Assert.Near(0.50, BalanceFormulas.ConditionMultiplier(10.0), 1e-12, "10"));
            t.Run("ConditionMultiplier 1 is 0.50", () =>
                Assert.Near(0.50, BalanceFormulas.ConditionMultiplier(1.0), 1e-12, "1"));
            t.Run("ConditionMultiplier 0 is 0", () =>
                Assert.Near(0.0, BalanceFormulas.ConditionMultiplier(0.0), 1e-12, "0"));
            t.Run("ConditionMultiplier negative is 0", () =>
                Assert.Near(0.0, BalanceFormulas.ConditionMultiplier(-5.0), 1e-12, "negative"));
            t.Run("ConditionMultiplier NaN is 0", () =>
                Assert.Near(0.0, BalanceFormulas.ConditionMultiplier(double.NaN), 1e-12, "NaN"));
        }

        private static void ProductionTests(TestRunner t)
        {
            t.Run("EffectiveWaterPerHour rated result", () =>
                Assert.Near(3.0, BalanceFormulas.EffectiveWaterPerHour(3.0, 1.0, 1.0, 1.0), 1e-12, "rated"));
            t.Run("EffectiveWaterPerHour zero power is zero water", () =>
                Assert.Near(0.0, BalanceFormulas.EffectiveWaterPerHour(6.0, 0.0, 1.0, 1.0), 1e-12, "0 power"));
            t.Run("EffectiveWaterPerHour multiplies all factors", () =>
                Assert.Near(6.0 * 0.5 * 0.8 * 0.9, BalanceFormulas.EffectiveWaterPerHour(6.0, 0.5, 0.8, 0.9), 1e-12, "product"));
            t.Run("EffectiveWaterPerHour NaN factor yields 0", () =>
                Assert.Near(0.0, BalanceFormulas.EffectiveWaterPerHour(6.0, double.NaN, 1.0, 1.0), 1e-12, "NaN"));
            t.Run("EffectiveWaterPerHour negative base yields 0", () =>
                Assert.Near(0.0, BalanceFormulas.EffectiveWaterPerHour(-6.0, 1.0, 1.0, 1.0), 1e-12, "negative"));
            t.Run("WaterItemsProduced floors the accumulator", () =>
                Assert.Equal(2, BalanceFormulas.WaterItemsProduced(2.7, 10), "2.7 -> 2"));
            t.Run("WaterItemsProduced below one yields 0", () =>
                Assert.Equal(0, BalanceFormulas.WaterItemsProduced(0.9, 10), "0.9 -> 0"));
            t.Run("WaterItemsProduced caps at free capacity", () =>
                Assert.Equal(3, BalanceFormulas.WaterItemsProduced(5.0, 3), "cap"));
            t.Run("WaterItemsProduced negative accumulator is 0", () =>
                Assert.Equal(0, BalanceFormulas.WaterItemsProduced(-1.0, 5), "negative"));
            t.Run("WaterItemsProduced NaN accumulator is 0", () =>
                Assert.Equal(0, BalanceFormulas.WaterItemsProduced(double.NaN, 5), "NaN"));
            t.Run("WaterItemsProduced +Inf accumulator caps at capacity", () =>
                Assert.Equal(7, BalanceFormulas.WaterItemsProduced(double.PositiveInfinity, 7), "Inf"));
            t.Run("WaterItemsProduced full inventory is 0", () =>
                Assert.Equal(0, BalanceFormulas.WaterItemsProduced(1.5, 0), "full"));
        }

        private static void MaintenanceFormulaTests(TestRunner t)
        {
            t.Run("WearPerHour 8-day interval", () =>
                Assert.Near(100.0 / 192.0, BalanceFormulas.WearPerHour(8.0), 1e-12, "8 days"));
            t.Run("WearPerHour 15-day interval", () =>
                Assert.Near(100.0 / 360.0, BalanceFormulas.WearPerHour(15.0), 1e-12, "15 days"));
            t.Run("WearPerHour invalid interval uses shortest legal day", () =>
                Assert.Near(100.0 / 24.0, BalanceFormulas.WearPerHour(0.0), 1e-12, "0 days"));
            t.Run("WearPerHour NaN interval uses shortest legal day", () =>
                Assert.Near(100.0 / 24.0, BalanceFormulas.WearPerHour(double.NaN), 1e-12, "NaN days"));
            t.Run("MaintenanceDaysLeft full condition", () =>
                Assert.Near(8.0, BalanceFormulas.MaintenanceDaysLeft(100.0, 8.0), 1e-12, "100/8"));
            t.Run("MaintenanceDaysLeft half condition", () =>
                Assert.Near(4.0, BalanceFormulas.MaintenanceDaysLeft(50.0, 8.0), 1e-12, "50/8"));
            t.Run("MaintenanceDaysLeft zero condition", () =>
                Assert.Near(0.0, BalanceFormulas.MaintenanceDaysLeft(0.0, 8.0), 1e-12, "0/8"));
            t.Run("MaintenanceDaysLeft NaN condition", () =>
                Assert.Near(0.0, BalanceFormulas.MaintenanceDaysLeft(double.NaN, 8.0), 1e-12, "NaN"));
            t.Run("CubicMetersFor five items", () =>
                Assert.Near(1.0, BalanceFormulas.CubicMetersFor(5, 0.2), 1e-12, "5 items"));
            t.Run("CubicMetersFor no items is 0", () =>
                Assert.Near(0.0, BalanceFormulas.CubicMetersFor(0, 0.2), 1e-12, "0 items"));
            t.Run("CubicMetersFor negative items is 0", () =>
                Assert.Near(0.0, BalanceFormulas.CubicMetersFor(-3, 0.2), 1e-12, "negative items"));
        }
    }
}
