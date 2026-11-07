using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MWP.Core
{
    /// <summary>
    /// Config schema 2 validation and clamping (requirements 20.6, REQ-SAFE-01). Every read is
    /// defensive: wrong types, NaN, infinities, negatives and out-of-range values degrade to the
    /// documented default or clamp to the allowed range and are reported through
    /// <see cref="MwpLogKeys.ConfigInvalid"/>. Internal helper of <see cref="MwpConfigCodec"/>.
    /// </summary>
    internal static class MwpConfigValidation
    {
        internal const double MaxWaterPerHour = 10000.0;
        internal const double MaxBadwaterPenalty = 3.0;
        internal const double MaxCubicMetersPerWater = 1000.0;
        internal const double MinTickIntervalSeconds = 0.05;
        internal const double MaxTickIntervalSeconds = 10.0;
        internal const int MaxPowerRequired = 1000000;
        internal const int MaxInternalCapacity = 100000;
        internal const int MaxDepth = 64;
        internal const double MinIntervalDays = 1.0;
        internal const double MaxIntervalDays = 3650.0;
        internal const double MaxDurationHours = 720.0;
        internal const int MaxMaterialAmount = 100000;
        internal const double MaxThrottleSeconds = 3600.0;

        /// <summary>Builds a validated config from an already-parsed JSON object.</summary>
        internal static MwpConfig Parse(IDictionary<string, object?> root, IMwpLog log)
        {
            int version = ReadSchemaVersion(root, log);
            IDictionary<string, object?>? features = Child(root, "features");
            IDictionary<string, object?>? maintenance = Child(root, "maintenance");
            IDictionary<string, object?>? compat = Child(root, "compat");
            BalanceSettings balance = ReadBalance(Child(root, "balance"), log);

            return new MwpConfig(
                version,
                ConfigFieldReader.ReadBool(features, "pipeAdapter", true, "features", log),
                ConfigFieldReader.ReadBool(features, "directPipeOutput", false, "features", log),
                ConfigFieldReader.ReadBool(features, "uiDiagnostics", false, "features", log),
                ConfigFieldReader.ReadBool(maintenance, "enabled", true, "maintenance", log),
                ConfigFieldReader.ReadBool(maintenance, "auto", true, "maintenance", log),
                ConfigFieldReader.ReadNumber(maintenance, "autoThreshold", MwpConfig.DefaultAutoThreshold, 0.0, 100.0, "maintenance", log),
                ConfigFieldReader.ReadNumber(maintenance, "durationHours", MwpConfig.DefaultMaintenanceDurationHours, 0.0, MaxDurationHours, "maintenance", log),
                ConfigFieldReader.ReadIntervals(ListChild(maintenance, "intervalsDays"), log),
                ConfigFieldReader.ReadMaterialSets(ListChild(maintenance, "materials"), log),
                balance,
                ConfigFieldReader.ReadTimberPipesVersion(compat, log),
                ConfigFieldReader.ReadNumber(compat, "logThrottleSeconds", MwpDefaults.LogThrottleSeconds, 0.0, MaxThrottleSeconds, "compat", log));
        }

        /// <summary>Case-insensitive child object lookup; null when absent or not an object.</summary>
        internal static IDictionary<string, object?>? Child(IDictionary<string, object?>? parent, string key)
        {
            return MiniJson.TryGet(parent, key, out object? value)
                && value is IDictionary<string, object?> map
                    ? map
                    : null;
        }

        /// <summary>Case-insensitive child array lookup; null when absent or not an array.</summary>
        internal static IList<object?>? ListChild(IDictionary<string, object?>? parent, string key)
        {
            return MiniJson.TryGet(parent, key, out object? value) && value is IList<object?> list ? list : null;
        }

        /// <summary>Reports a rejected/clamped config value; a broken log sink never breaks loading.</summary>
        internal static void Warn(IMwpLog? log, string path, string reason)
        {
            try
            {
                log?.Warn(MwpLogKeys.ConfigInvalid, path + ": " + reason);
            }
            catch (Exception)
            {
                // Logging is diagnostics only; config loading must continue.
            }
        }

        private static int ReadSchemaVersion(IDictionary<string, object?> root, IMwpLog log)
        {
            int version = MiniJson.GetInt(root, "configSchemaVersion", MwpDefaults.ConfigSchemaVersion);
            if (version < MwpDefaults.MinSupportedConfigSchemaVersion || version > MwpDefaults.ConfigSchemaVersion)
            {
                Warn(log, "configSchemaVersion", "unsupported value " + version);
                return MwpDefaults.ConfigSchemaVersion;
            }

            return version;
        }

        private static BalanceSettings ReadBalance(IDictionary<string, object?>? map, IMwpLog log)
        {
            bool legacy = ConfigFieldReader.ReadBool(map, "useLegacyRates", false, "balance", log);
            double penalty = ConfigFieldReader.ReadNumber(map, "badwaterPenalty", 1.0, 0.0, MaxBadwaterPenalty, "balance", log);
            double minQuality = ConfigFieldReader.ReadNumber(map, "minWaterQuality", 0.0, 0.0, 1.0, "balance", log);
            double cubic = ConfigFieldReader.ReadPositive(map, "cubicMetersPerWater", MwpDefaults.CubicMetersPerWater, MaxCubicMetersPerWater, "balance", log);
            double tick = ConfigFieldReader.ReadPositive(map, "tickIntervalSeconds", MwpDefaults.TickIntervalSeconds, MaxTickIntervalSeconds, "balance", log);
            if (tick < MinTickIntervalSeconds)
            {
                Warn(log, "balance.tickIntervalSeconds", "clamped to " + MinTickIntervalSeconds);
                tick = MinTickIntervalSeconds;
            }

            return new BalanceSettings(
                legacy,
                penalty,
                minQuality,
                cubic,
                tick,
                ReadTiers(ListChild(map, "tiers"), legacy, log));
        }

        private static IReadOnlyList<TierDefinition> ReadTiers(IList<object?>? raw, bool legacy, IMwpLog log)
        {
            var tiers = new List<TierDefinition>(TierCatalog.Default);
            if (raw != null)
            {
                foreach (object? entry in raw)
                {
                    ApplyTierEntry(entry, tiers, log);
                }
            }

            if (legacy)
            {
                ApplyLegacyRates(tiers);
            }

            return new ReadOnlyCollection<TierDefinition>(tiers);
        }

        private static void ApplyTierEntry(object? entry, List<TierDefinition> tiers, IMwpLog log)
        {
            if (entry is not IDictionary<string, object?> map)
            {
                Warn(log, "balance.tiers", "expected an object");
                return;
            }

            string name = MiniJson.GetString(map, "tier", string.Empty);
            TierDefinition? baseTier = TierCatalog.TryGet(name);
            if (baseTier == null)
            {
                Warn(log, "balance.tiers", "unknown tier '" + name + "'");
                return;
            }

            string path = "balance.tiers." + baseTier.TierName;
            double rate = ConfigFieldReader.ReadPositive(map, "waterPerHour", baseTier.WaterPerHour, MaxWaterPerHour, path, log);
            int power = ConfigFieldReader.ReadInt(map, "powerRequired", baseTier.PowerRequiredHp, 0, MaxPowerRequired, path, log);
            int capacity = ConfigFieldReader.ReadInt(map, "internalCapacity", baseTier.InternalCapacity, 1, MaxInternalCapacity, path, log);
            int depth = ConfigFieldReader.ReadInt(map, "maxDepth", baseTier.MaxDepth, 1, MaxDepth, path, log);
            tiers[(int)baseTier.Tier] = CopyTier(baseTier, rate, power, depth, capacity);
        }

        private static void ApplyLegacyRates(List<TierDefinition> tiers)
        {
            IReadOnlyList<TierDefinition> legacyTiers = BalanceSettings.WithLegacyRates().Tiers;
            for (int i = 0; i < tiers.Count; i++)
            {
                TierDefinition source = tiers[i];
                tiers[i] = CopyTier(source, legacyTiers[i].WaterPerHour, source.PowerRequiredHp, source.MaxDepth, source.InternalCapacity);
            }
        }

        private static TierDefinition CopyTier(TierDefinition source, double rate, int power, int depth, int capacity)
        {
            return new TierDefinition(
                source.Tier,
                source.TierName,
                source.ScienceCost,
                source.BuildCost,
                source.SizeX,
                source.SizeY,
                source.SizeZ,
                rate,
                power,
                depth,
                capacity,
                source.BuilderHours,
                source.MaintenanceIntervalDays,
                source.MaintenanceMaterials);
        }
    }
}
