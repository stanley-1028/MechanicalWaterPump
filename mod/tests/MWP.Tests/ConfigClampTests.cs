using MWP.Core;

namespace MWP.Tests
{
    // REQ-NFR-03 note: the 40-line cap is a soft guideline for logic. The group methods in
    // this file are declarative lists of independent t.Run cases; splitting them further
    // would only scatter the cases, so the documented reason for exceeding it is recorded here.
    /// <summary>Config clamp/legacy tests: illegal values become safe defaults, legacy rates stay opt-in.</summary>
    internal static class ConfigClampTests
    {
        internal static void Run(TestRunner t)
        {
            BalanceClampTests(t);
            MaintenanceClampTests(t);
            LegacyTests(t);
        }

        private static void BalanceClampTests(TestRunner t)
        {
            TierClampTests(t);
            QualityClampTests(t);

        }

        private static void MaintenanceClampTests(TestRunner t)
        {
            TimingClampTests(t);
            MaterialClampTests(t);

        }

        private static void LegacyTests(TestRunner t)
        {
            t.Run("Legacy rates are off by default", () =>
            {
                Assert.False(MwpConfig.Default.Balance.UseLegacyRates, "legacy off");
            });

            t.Run("useLegacyRates reproduces 1/3/6/9", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"useLegacyRates\":true}}", new RecordingLog());
                Assert.True(config.Balance.UseLegacyRates, "flagged legacy");
                Assert.Near(1.0, config.Balance.Tiers[0].WaterPerHour, 1e-12, "legacy mk I");
                Assert.Near(3.0, config.Balance.Tiers[1].WaterPerHour, 1e-12, "legacy mk II");
                Assert.Near(6.0, config.Balance.Tiers[2].WaterPerHour, 1e-12, "legacy mk III");
                Assert.Near(9.0, config.Balance.Tiers[3].WaterPerHour, 1e-12, "legacy mk IV");
                Assert.Equal(700, config.Balance.Tiers[3].PowerRequiredHp, "power unchanged");
            });

            t.Run("WithLegacyRates keeps power and capacity", () =>
            {
                BalanceSettings legacy = BalanceSettings.WithLegacyRates();
                Assert.True(legacy.UseLegacyRates, "flagged");
                Assert.Equal(100, legacy.Tiers[0].PowerRequiredHp, "power");
                Assert.Equal(100, legacy.Tiers[3].InternalCapacity, "capacity");
                Assert.Near(9.0, legacy.Tiers[3].WaterPerHour, 1e-12, "rate");
            });

            t.Run("Legacy config survives a serialize/parse roundtrip", () =>
            {
                MwpConfig legacy = MwpConfigCodec.Parse(
                    "{\"balance\":{\"useLegacyRates\":true}}", new RecordingLog());
                MwpConfig reparsed = MwpConfigCodec.Parse(MwpConfigCodec.Serialize(legacy), new RecordingLog());
                Assert.True(reparsed.Balance.UseLegacyRates, "flag kept");
                Assert.Near(1.0, reparsed.Balance.Tiers[0].WaterPerHour, 1e-12, "rate kept");
            });
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
        /// <summary>Test group: TierClampTests.</summary>
        private static void TierClampTests(TestRunner t)
        {
            t.Run("Tier overrides are applied per name", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"tiers\":[{\"tier\":\"Basic\",\"waterPerHour\":9.0,\"powerRequired\":50,"
                    + "\"internalCapacity\":20,\"maxDepth\":3}]}}",
                    new RecordingLog());
                Assert.Near(9.0, config.Balance.Tiers[0].WaterPerHour, 1e-12, "basic rate");
                Assert.Equal(50, config.Balance.Tiers[0].PowerRequiredHp, "basic power");
                Assert.Equal(20, config.Balance.Tiers[0].InternalCapacity, "basic capacity");
                Assert.Equal(3, config.Balance.Tiers[0].MaxDepth, "basic depth");
                Assert.Near(6.0, config.Balance.Tiers[1].WaterPerHour, 1e-12, "other tier untouched");
            });

            t.Run("Non-positive water rate falls back to the tier default", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"tiers\":[{\"tier\":\"Basic\",\"waterPerHour\":0}]}}", log);
                Assert.Near(3.0, config.Balance.Tiers[0].WaterPerHour, 1e-12, "default rate");
                Assert.True(ContainsWarn(log), "warning logged");
            });

            t.Run("Negative water rate falls back to the tier default", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"tiers\":[{\"tier\":\"Basic\",\"waterPerHour\":-5}]}}", new RecordingLog());
                Assert.Near(3.0, config.Balance.Tiers[0].WaterPerHour, 1e-12, "default rate");
            });

            t.Run("Negative power requirement clamps to zero", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"tiers\":[{\"tier\":\"Basic\",\"powerRequired\":-10}]}}", new RecordingLog());
                Assert.Equal(0, config.Balance.Tiers[0].PowerRequiredHp, "clamped power");
            });

            t.Run("Zero capacity and depth clamp to one", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"tiers\":[{\"tier\":\"Basic\",\"internalCapacity\":0,\"maxDepth\":0}]}}",
                    new RecordingLog());
                Assert.Equal(1, config.Balance.Tiers[0].InternalCapacity, "capacity 1");
                Assert.Equal(1, config.Balance.Tiers[0].MaxDepth, "depth 1");
            });

            t.Run("Unknown tier name is ignored", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"tiers\":[{\"tier\":\"Mega\",\"waterPerHour\":99}]}}", log);
                Assert.Near(3.0, config.Balance.Tiers[0].WaterPerHour, 1e-12, "default rate");
                Assert.True(ContainsWarn(log), "warning logged");
            });

            t.Run("Malformed tier entries are ignored", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse("{\"balance\":{\"tiers\":[1,2]}}", log);
                Assert.Near(15.0, config.Balance.Tiers[2].WaterPerHour, 1e-12, "default rate");
                Assert.True(ContainsWarn(log), "warning logged");
            });
        }

        /// <summary>Test group: QualityClampTests.</summary>
        private static void QualityClampTests(TestRunner t)
        {
            t.Run("Wrong-typed penalty falls back to vanilla", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse("{\"balance\":{\"badwaterPenalty\":\"oops\"}}", log);
                Assert.Near(1.0, config.Balance.BadwaterPenalty, 1e-12, "vanilla penalty");
                Assert.True(ContainsWarn(log), "warning logged");
            });

            t.Run("Out-of-range quality settings clamp", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"minWaterQuality\":5,\"badwaterPenalty\":-2}}", new RecordingLog());
                Assert.Near(1.0, config.Balance.MinWaterQuality, 1e-12, "quality clamp");
                Assert.Near(0.0, config.Balance.BadwaterPenalty, 1e-12, "penalty clamp");
            });

            t.Run("Configured 0.75/0.25 curve is usable", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"badwaterPenalty\":0.75,\"minWaterQuality\":0.25}}", new RecordingLog());
                Assert.Near(0.75, config.Balance.BadwaterPenalty, 1e-12, "penalty");
                Assert.Near(0.25, config.Balance.MinWaterQuality, 1e-12, "floor");
            });

            t.Run("Non-positive cubic meters falls back to vanilla", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"cubicMetersPerWater\":0}}", new RecordingLog());
                Assert.Near(0.2, config.Balance.CubicMetersPerWater, 1e-12, "fallback");
            });

            t.Run("Non-positive tick interval falls back to one second", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"balance\":{\"tickIntervalSeconds\":-1}}", new RecordingLog());
                Assert.Near(1.0, config.Balance.TickIntervalSeconds, 1e-12, "fallback");
            });
        }
        /// <summary>Test group: TimingClampTests.</summary>
        private static void TimingClampTests(TestRunner t)
        {
            t.Run("Threshold clamps to 100 and duration clamps to zero", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"maintenance\":{\"autoThreshold\":150,\"durationHours\":-2}}", log);
                Assert.Near(100.0, config.MaintenanceAutoThreshold, 1e-12, "threshold clamp");
                Assert.Near(0.0, config.MaintenanceDurationHours, 1e-12, "duration clamp");
                Assert.True(ContainsWarn(log), "warnings logged");
            });

            t.Run("Intervals below one day clamp and gaps use tier defaults", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"maintenance\":{\"intervalsDays\":[0,-1,2]}}", new RecordingLog());
                Assert.Near(1.0, config.MaintenanceIntervalsDays[0], 1e-12, "interval 0");
                Assert.Near(1.0, config.MaintenanceIntervalsDays[1], 1e-12, "interval 1");
                Assert.Near(2.0, config.MaintenanceIntervalsDays[2], 1e-12, "interval 2");
                Assert.Near(15.0, config.MaintenanceIntervalsDays[3], 1e-12, "interval 3 default");
            });
        }

        /// <summary>Test group: MaterialClampTests.</summary>
        private static void MaterialClampTests(TestRunner t)
        {
            t.Run("Negative material amounts clamp to zero", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"maintenance\":{\"materials\":[[{\"id\":\"Log\",\"amount\":-5}]]}}", new RecordingLog());
                Assert.Equal(1, config.MaintenanceMaterials[0].Count, "basic stack count");
                Assert.Equal(0, config.MaintenanceMaterials[0][0].Amount, "clamped amount");
                Assert.Equal(2, config.MaintenanceMaterials[2].Count, "advanced defaults kept");
            });

            t.Run("Missing good id is rejected and the default set kept", () =>
            {
                var log = new RecordingLog();
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"maintenance\":{\"materials\":[[{\"amount\":5}]]}}", log);
                Assert.Equal(GoodStacks.Log, config.MaintenanceMaterials[0][0].GoodId, "default material");
                Assert.True(ContainsWarn(log), "warning logged");
            });

            t.Run("Maintenance master switch and auto switch are parsed", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"maintenance\":{\"enabled\":false,\"auto\":false}}", new RecordingLog());
                Assert.False(config.MaintenanceEnabled, "enabled");
                Assert.False(config.MaintenanceAuto, "auto");
            });

            t.Run("Log throttle clamps to zero", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"compat\":{\"logThrottleSeconds\":-5}}", new RecordingLog());
                Assert.Near(0.0, config.LogThrottleSeconds, 1e-12, "clamped throttle");
            });

            t.Run("Empty Timber Pipes version uses the verified default", () =>
            {
                MwpConfig config = MwpConfigCodec.Parse(
                    "{\"compat\":{\"timberPipesMinVersion\":\"\"}}", new RecordingLog());
                Assert.Equal("11.4.0", config.TimberPipesMinVersion, "default version");
            });
        }
    }
}
