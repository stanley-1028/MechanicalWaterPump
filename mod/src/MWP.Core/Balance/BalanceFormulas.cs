using System;

namespace MWP.Core
{
    /// <summary>
    /// Pure production formulas shared by gameplay, UI and the validator (REQ-BAL-01..04).
    /// Every member is defensive: NaN, infinities and negative inputs are rejected or clamped
    /// instead of propagating (REQ-SAFE-01), so a corrupt config or save can never produce more
    /// water than the rated maximum. All rates are Water per game hour.
    /// </summary>
    public static class BalanceFormulas
    {
        /// <summary>Upper bound for an unvalidated rate/scale coming from config or a save.</summary>
        private const double MaxRate = 100000.0;

        /// <summary>How many Water items one game hour of rated output yields at 100%.</summary>
        /// <param name="supplyHp">Power actually delivered, in hp.</param>
        /// <param name="requiredHp">Nominal demand, in hp; must be positive for production.</param>
        /// <returns><c>clamp(supply / required, 0, 1)</c>; 0 for a missing/zero demand so a pump
        /// can never spin for free (REQ-BAL-06/09).</returns>
        public static double PowerRatio(double supplyHp, double requiredHp)
        {
            if (double.IsNaN(requiredHp) || requiredHp <= 0.0)
            {
                return 0.0;
            }

            if (double.IsNaN(supplyHp) || supplyHp <= 0.0)
            {
                return 0.0;
            }

            double ratio = supplyHp / requiredHp;
            if (double.IsPositiveInfinity(ratio) || ratio > 1.0)
            {
                return 1.0;
            }

            return ratio;
        }

        /// <summary>
        /// Water-quality multiplier q = clamp(1 - penalty * contamination, minQuality, 1).
        /// The shipped defaults are penalty = 1.0 and minQuality = 0.0, which is exactly the
        /// vanilla <c>1 - ContaminationPercentage</c> curve (DV-01) so MWP never beats the
        /// vanilla pump under the same water (REQ-BAL-04). The requirements' 0.75 / 0.25 pair
        /// stays available through config.
        /// </summary>
        /// <param name="contamination">Bad-water fraction, 0..1.</param>
        /// <param name="badwaterPenalty">How strongly contamination hurts output, default 1.0.</param>
        /// <param name="minQuality">Quality floor, default 0.0.</param>
        /// <returns>Quality multiplier in 0..1.</returns>
        public static double WaterQuality(double contamination, double badwaterPenalty, double minQuality)
        {
            // A NaN contamination is treated as fully polluted: safety beats convenience here,
            // because the alternative would hand out free clean water from a corrupt reading.
            double c = Sanitize(contamination, 1.0, 0.0, 1.0);
            double penalty = Sanitize(badwaterPenalty, 1.0, 0.0, 3.0);
            double floor = Sanitize(minQuality, 0.0, 0.0, 1.0);
            double quality = 1.0 - (penalty * c);

            if (double.IsNaN(quality) || quality < floor)
            {
                return floor;
            }

            return quality > 1.0 ? 1.0 : quality;
        }

        /// <summary>
        /// Condition efficiency multiplier (REQ-MAINT-09): 100..75 -> 1.00, 75..50 -> 0.90,
        /// 50..25 -> 0.75, 25..0 -> 0.50, 0 -> 0. The lower bound of each band is inclusive,
        /// matching the maintenance bands in requirements 20.2.2.
        /// </summary>
        /// <param name="condition">Wear value 0..100.</param>
        /// <returns>Multiplier in 0..1; NaN and non-positive values stop production.</returns>
        public static double ConditionMultiplier(double condition)
        {
            if (double.IsNaN(condition) || condition <= 0.0)
            {
                return 0.0;
            }

            if (condition >= 75.0)
            {
                return 1.00;
            }

            if (condition >= 50.0)
            {
                return 0.90;
            }

            return condition >= 25.0 ? 0.75 : 0.50;
        }

        /// <summary>
        /// Wear per Producing game hour: <c>100 / (intervalDays * 24)</c> (REQ-MAINT-02).
        /// An invalid interval degrades to the shortest legal interval (1 day) so wear can never
        /// silently stop because of a corrupt config value.
        /// </summary>
        /// <param name="maintenanceIntervalDays">Days of Producing time a full condition lasts.</param>
        /// <returns>Condition points lost per hour.</returns>
        public static double WearPerHour(double maintenanceIntervalDays)
        {
            double days = Sanitize(maintenanceIntervalDays, 1.0, 1.0, MaxRate);
            return 100.0 / (days * MwpDefaults.HoursPerDay);
        }

        /// <summary>
        /// Actual Water per game hour: <c>baseRate * powerRatio * quality * conditionMultiplier</c>.
        /// Multipliers only, never additive bonuses (requirements 3.4).
        /// </summary>
        /// <param name="baseRate">Rated Water per hour at 100% power, clean water, condition 100.</param>
        /// <param name="powerRatio">Delivered power ratio 0..1.</param>
        /// <param name="quality">Water-quality multiplier 0..1.</param>
        /// <param name="conditionMultiplier">Condition multiplier 0..1.</param>
        /// <returns>Non-negative finite Water per hour.</returns>
        public static double EffectiveWaterPerHour(
            double baseRate,
            double powerRatio,
            double quality,
            double conditionMultiplier)
        {
            double rate = Sanitize(baseRate, 0.0, 0.0, MaxRate);
            double ratio = Sanitize(powerRatio, 0.0, 0.0, 1.0);
            double water = Sanitize(quality, 0.0, 0.0, 1.0);
            double condition = Sanitize(conditionMultiplier, 0.0, 0.0, 1.0);
            double result = rate * ratio * water * condition;
            return double.IsNaN(result) || double.IsPositiveInfinity(result) ? 0.0 : result;
        }

        /// <summary>
        /// Converts the fractional accumulator into whole Water items: <c>floor(accumulator)</c>,
        /// capped by the free inventory space and never negative (requirements 3.4).
        /// </summary>
        /// <param name="accumulator">Fractional Water accumulated so far.</param>
        /// <param name="freeCapacity">Free slots in the internal output inventory.</param>
        /// <returns>Whole items to grant; the caller subtracts the same amount from the accumulator.</returns>
        public static int WaterItemsProduced(double accumulator, int freeCapacity)
        {
            if (freeCapacity <= 0 || double.IsNaN(accumulator) || accumulator <= 0.0)
            {
                return 0;
            }

            double whole = Math.Floor(accumulator);
            if (whole > freeCapacity)
            {
                whole = freeCapacity;
            }

            return whole <= 0.0 ? 0 : (int)whole;
        }

        /// <summary>Water-body volume consumed by the given item count: <c>items * 0.2 m³</c>.</summary>
        /// <param name="waterItems">Whole Water items withdrawn.</param>
        /// <param name="cubicMetersPerWater">Vanilla conversion, 0.2 m³ per item.</param>
        /// <returns>Requested volume in m³, never negative.</returns>
        public static double CubicMetersFor(int waterItems, double cubicMetersPerWater)
        {
            if (waterItems <= 0)
            {
                return 0.0;
            }

            double perItem = Sanitize(cubicMetersPerWater, MwpDefaults.CubicMetersPerWater, 0.0, 1000.0);
            return waterItems * perItem;
        }

        /// <summary>
        /// Estimated game days until the pump reaches condition 0 at the current wear rate:
        /// <c>condition / 100 * intervalDays</c>. Used by the UI countdown (REQ-MAINT-10).
        /// </summary>
        /// <param name="condition">Wear value 0..100.</param>
        /// <param name="intervalDays">Days of Producing time a full condition lasts.</param>
        /// <returns>Non-negative days; 0 when already due.</returns>
        public static double MaintenanceDaysLeft(double condition, double intervalDays)
        {
            double wear = Sanitize(condition, 0.0, 0.0, 100.0);
            if (wear <= 0.0)
            {
                return 0.0;
            }

            double days = Sanitize(intervalDays, 1.0, 1.0, MaxRate);
            return (wear / 100.0) * days;
        }

        /// <summary>
        /// Clamps <paramref name="value"/> into <paramref name="min"/>..<paramref name="max"/> and
        /// replaces the non-finite cases: NaN becomes <paramref name="fallback"/>, +Infinity the
        /// maximum, -Infinity the minimum (REQ-SAFE-01). The fallback itself is clamped too.
        /// </summary>
        /// <param name="value">Value read from config, save or game API.</param>
        /// <param name="fallback">Value used when <paramref name="value"/> is NaN.</param>
        /// <param name="min">Inclusive lower bound.</param>
        /// <param name="max">Inclusive upper bound; must be greater than or equal to min.</param>
        /// <returns>A finite value inside the bounds.</returns>
        public static double Sanitize(double value, double fallback, double min, double max)
        {
            if (min > max)
            {
                // Defensive: a swapped range must not produce an unpredictable result.
                double swap = min;
                min = max;
                max = swap;
            }

            if (double.IsNaN(fallback))
            {
                fallback = min;
            }

            if (double.IsNaN(value))
            {
                value = fallback;
            }
            else if (double.IsPositiveInfinity(value))
            {
                value = max;
            }
            else if (double.IsNegativeInfinity(value))
            {
                value = min;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
