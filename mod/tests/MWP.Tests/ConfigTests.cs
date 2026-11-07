using System.Collections.Generic;
using MWP.Core;

namespace MWP.Tests
{
    /// <summary>Unit tests for config schema 2 parsing, validation, clamping and roundtrip (REQ-SAFE-01, chapter 20.6).</summary>
    internal static class ConfigTests
    {
        internal static void Run(TestRunner t)
        {
            DefaultConfigTests(t);
            ConfigRoundtripTests(t);
            SerializeKeyTests(t);

        }

        private static void InvalidDocumentTests(TestRunner t)
        {
            FallbackDocumentTests(t);
            SchemaVersionTests(t);

        }

        private static bool ContainsWarn(RecordingLog log)
        {
            for (int i = 0; i < log.Entries.Count; i++)
            {
                if (log.Entries[i].StartsWith("Warn|", System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        /// <summary>Test group: DefaultConfigTests.</summary>
        private static void DefaultConfigTests(TestRunner t)
        {
            t.Run("Default config matches chapter 20.1.2 and 20.6", () =>
            {
                MwpConfig config = MwpConfig.Default;
                Assert.Equal(2, config.ConfigSchemaVersion, "schema version");
                Assert.True(config.PipeAdapterEnabled, "pipe adapter");
                Assert.False(config.DirectPipeOutputEnabled, "direct pipe");
                Assert.False(config.UiDiagnosticsEnabled, "ui diagnostics");
                Assert.True(config.MaintenanceEnabled, "maintenance enabled");
                Assert.True(config.MaintenanceAuto, "maintenance auto");
                Assert.Near(50.0, config.MaintenanceAutoThreshold, 1e-12, "threshold");
                Assert.Near(2.0, config.MaintenanceDurationHours, 1e-12, "duration");
                Assert.Equal("11.4.0", config.TimberPipesMinVersion, "timber pipes version");
                Assert.Near(30.0, config.LogThrottleSeconds, 1e-12, "log throttle");

                Assert.Near(1.0, config.Balance.BadwaterPenalty, 1e-12, "vanilla penalty DV-01");
                Assert.Near(0.0, config.Balance.MinWaterQuality, 1e-12, "vanilla quality floor");
                Assert.Near(0.2, config.Balance.CubicMetersPerWater, 1e-12, "m3 per water");
                Assert.False(config.Balance.UseLegacyRates, "legacy off by default");

                double[] rates = { 3.0, 6.0, 15.0, 20.0 };
                int[] powers = { 100, 200, 500, 700 };
                int[] capacities = { 15, 30, 75, 100 };
                int[] depths = { 2, 4, 6, 8 };
                for (int i = 0; i < rates.Length; i++)
                {
                    TierDefinition tier = config.Balance.Tiers[i];
                    Assert.Near(rates[i], tier.WaterPerHour, 1e-12, "rate " + tier.TierName);
                    Assert.Equal(powers[i], tier.PowerRequiredHp, "power " + tier.TierName);
                    Assert.Equal(capacities[i], tier.InternalCapacity, "capacity " + tier.TierName);
                    Assert.Equal(depths[i], tier.MaxDepth, "depth " + tier.TierName);
                }

                Assert.Near(8.0, config.MaintenanceIntervalsDays[0], 1e-12, "interval 0");
                Assert.Near(15.0, config.MaintenanceIntervalsDays[3], 1e-12, "interval 3");
                Assert.Equal(2, config.MaintenanceMaterials[2].Count, "advanced material count");
                Assert.Equal(GoodStacks.MetalBlock, config.MaintenanceMaterials[2][0].GoodId, "advanced first material");
            });
        }

        /// <summary>Test group: ConfigRoundtripTests.</summary>
        private static void ConfigRoundtripTests(TestRunner t)
        {
            t.Run("Serialize then parse preserves every documented value", () =>
            {
                var log = new RecordingLog();
                string json = MwpConfigCodec.Serialize(MwpConfig.Default);
                MwpConfig parsed = MwpConfigCodec.Parse(json, log);
                Assert.Equal(log.Entries.Count, 0, "no warnings for the shipped config");
                Assert.Equal(2, parsed.ConfigSchemaVersion, "schema");
                Assert.Near(20.0, parsed.Balance.Tiers[3].WaterPerHour, 1e-12, "top rate");
                Assert.Equal(100, parsed.Balance.Tiers[3].InternalCapacity, "top capacity");
                Assert.Equal(GoodStacks.MetalBlock, parsed.MaintenanceMaterials[3][0].GoodId, "top material");
                Assert.Near(30.0, parsed.LogThrottleSeconds, 1e-12, "throttle");
            });
        }

        /// <summary>Test group: SerializeKeyTests.</summary>
        private static void SerializeKeyTests(TestRunner t)
        {
            t.Run("Serialize emits the contract key names", () =>
            {
                string json = MwpConfigCodec.Serialize(MwpConfig.Default);
                Assert.Contains(json, "\"configSchemaVersion\"", "schema key");
                Assert.Contains(json, "\"pipeAdapter\"", "feature key");
                Assert.Contains(json, "\"badwaterPenalty\"", "penalty key");
                Assert.Contains(json, "\"waterPerHour\"", "rate key");
                Assert.Contains(json, "\"intervalsDays\"", "interval key");
                Assert.Contains(json, "\"timberPipesMinVersion\"", "compat key");
            });

            InvalidDocumentTests(t);
        }
        /// <summary>Test group: FallbackDocumentTests.</summary>
        private static void FallbackDocumentTests(TestRunner t)
        {
            t.Run("Invalid JSON falls back to defaults and warns", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse("not json", log);
                Assert.Near(3.0, config.Balance.Tiers[0].WaterPerHour, 1e-12, "default rate");
                Assert.True(ContainsWarn(log), "warning logged");
            });

            t.Run("Empty text falls back to defaults and warns", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse(string.Empty, log);
                Assert.Equal(2, config.ConfigSchemaVersion, "default schema");
                Assert.True(ContainsWarn(log), "warning logged");
            });

            t.Run("Non-object root falls back to defaults", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse("[]", log);
                Assert.Near(6.0, config.Balance.Tiers[1].WaterPerHour, 1e-12, "default rate");
                Assert.True(ContainsWarn(log), "warning logged");
            });
        }

        /// <summary>Test group: SchemaVersionTests.</summary>
        private static void SchemaVersionTests(TestRunner t)
        {
            t.Run("Unsupported schema version is clamped", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse("{\"configSchemaVersion\":99}", log);
                Assert.Equal(2, config.ConfigSchemaVersion, "clamped schema");
                Assert.True(ContainsWarn(log), "warning logged");
            });

            t.Run("Schema version 1 is accepted with defaults for missing chapters", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse("{\"configSchemaVersion\":1}", new RecordingLog());
                Assert.Equal(1, config.ConfigSchemaVersion, "kept version");
                Assert.True(config.MaintenanceEnabled, "default maintenance");
                Assert.Near(3.0, config.Balance.Tiers[0].WaterPerHour, 1e-12, "default rate");
            });

            t.Run("Feature switches are parsed", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"features\":{\"pipeAdapter\":false,\"directPipeOutput\":true,\"uiDiagnostics\":true}}",
                    new RecordingLog());
                Assert.False(config.PipeAdapterEnabled, "pipe adapter off");
                Assert.True(config.DirectPipeOutputEnabled, "direct pipe on");
                Assert.True(config.UiDiagnosticsEnabled, "diagnostics on");
            });
        }
    }
}
