using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MWP.Core
{
    /// <summary>
    /// Resolved balance values for one game session, loaded from <c>MWP_Config.json</c> by
    /// <see cref="MwpConfigCodec"/> (REQ-NFR-04). Defaults are the requirements chapter 20.1.2
    /// tiers plus the vanilla-equivalent contamination curve (DV-01).
    /// </summary>
    public sealed class BalanceSettings
    {
        /// <summary>The legacy baseline rates replaced by chapter 20.1.2; test/comparison only.</summary>
        private static readonly double[] LegacyRates = { 1.0, 3.0, 6.0, 9.0 };

        internal BalanceSettings(
            bool useLegacyRates,
            double badwaterPenalty,
            double minWaterQuality,
            double cubicMetersPerWater,
            double tickIntervalSeconds,
            IReadOnlyList<TierDefinition> tiers)
        {
            UseLegacyRates = useLegacyRates;
            BadwaterPenalty = badwaterPenalty;
            MinWaterQuality = minWaterQuality;
            CubicMetersPerWater = cubicMetersPerWater;
            TickIntervalSeconds = tickIntervalSeconds;
            Tiers = tiers ?? throw new ArgumentNullException(nameof(tiers));
        }

        /// <summary>Whether the pre-0.2 rates (1/3/6/9) are active; UI must mark this as non-default (REQ-BAL-10).</summary>
        public bool UseLegacyRates { get; }

        /// <summary>Contamination penalty; 1.0 reproduces vanilla <c>1 - c</c> (DV-01, REQ-BAL-04).</summary>
        public double BadwaterPenalty { get; }

        /// <summary>Lowest allowed quality multiplier; 0.0 reproduces vanilla (DV-01).</summary>
        public double MinWaterQuality { get; }

        /// <summary>Water-body volume consumed per Water item; vanilla is 0.2 m³.</summary>
        public double CubicMetersPerWater { get; }

        /// <summary>Production/maintenance tick interval in seconds (REQ-PERF-01).</summary>
        public double TickIntervalSeconds { get; }

        /// <summary>The four tier definitions in unlock order.</summary>
        public IReadOnlyList<TierDefinition> Tiers { get; }

        /// <summary>The shipped defaults: chapter 20.1.2 tiers and vanilla-equivalent water quality.</summary>
        public static BalanceSettings Default { get; } = new BalanceSettings(
            useLegacyRates: false,
            badwaterPenalty: 1.0,
            minWaterQuality: 0.0,
            cubicMetersPerWater: MwpDefaults.CubicMetersPerWater,
            tickIntervalSeconds: MwpDefaults.TickIntervalSeconds,
            tiers: TierCatalog.Default);

        /// <summary>
        /// Rebuilds the settings with the pre-0.2 rates (1/3/6/9) while keeping every other tier
        /// field. Used by the T26 comparison test and by <c>balance.useLegacyRates</c> only; never
        /// a release default (REQ-BAL-10).
        /// </summary>
        /// <returns>A settings instance flagged <see cref="UseLegacyRates"/> with legacy rates.</returns>
        public static BalanceSettings WithLegacyRates()
        {
            var tiers = new TierDefinition[TierCatalog.Default.Count];
            for (int i = 0; i < tiers.Length; i++)
            {
                TierDefinition source = TierCatalog.Default[i];
                tiers[i] = new TierDefinition(
                    source.Tier,
                    source.TierName,
                    source.ScienceCost,
                    source.BuildCost,
                    source.SizeX,
                    source.SizeY,
                    source.SizeZ,
                    LegacyRates[i],
                    source.PowerRequiredHp,
                    source.MaxDepth,
                    source.InternalCapacity,
                    source.BuilderHours,
                    source.MaintenanceIntervalDays,
                    source.MaintenanceMaterials);
            }

            return new BalanceSettings(
                useLegacyRates: true,
                badwaterPenalty: 1.0,
                minWaterQuality: 0.0,
                cubicMetersPerWater: MwpDefaults.CubicMetersPerWater,
                tickIntervalSeconds: MwpDefaults.TickIntervalSeconds,
                tiers: new ReadOnlyCollection<TierDefinition>(tiers));
        }
    }
}
