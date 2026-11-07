using MWP.Core;

namespace MWP.Tests
{
    // REQ-NFR-03 note: the 40-line cap is a soft guideline for logic. The group methods in
    // this file are declarative lists of independent t.Run cases; splitting them further
    // would only scatter the cases, so the documented reason for exceeding it is recorded here.
    /// <summary>Save codec roundtrip and clamp tests for one pump's persisted state.</summary>
    internal static class SaveRoundtripTests
    {
        internal static void Run(TestRunner t)
        {
            RoundtripTests(t);
            ClampTests(t);
        }

        private static void RoundtripTests(TestRunner t)
        {
            FieldRoundtripTests(t);
            AdapterStateTests(t);

        }

        private static void ClampTests(TestRunner t)
        {
            NumericClampTests(t);
            EnumNormalizationTests(t);

        }

        private static bool ContainsWarn(RecordingLog log, string key)
        {
            for (int i = 0; i < log.Entries.Count; i++)
            {
                if (log.Entries[i].StartsWith("Warn|" + key + "|", System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsInfo(RecordingLog log, string key)
        {
            for (int i = 0; i < log.Entries.Count; i++)
            {
                if (log.Entries[i].StartsWith("Info|" + key + "|", System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        /// <summary>Test group: FieldRoundtripTests.</summary>
        private static void FieldRoundtripTests(TestRunner t)
        {
            t.Run("Serialize then parse then sanitize preserves every field", () =>
            {
                var original = new PumpSaveState
                {
                    SchemaVersion = 2,
                    Tier = "Advanced",
                    Accumulator = 0.42,
                    Contamination = 0.1,
                    Condition = 77.5,
                    AutoMaintenance = false,
                    MaintenanceState = MaintenancePhase.Maintaining,
                    MaintenanceRemainingHours = 1.5,
                    State = PumpState.LowPower,
                    Paused = true,
                    PipeAdapterState = nameof(PipeAdapterState.ValveOnly),
                    ConsecutiveWaterWithdrawFailures = 3
                };

                var log = new RecordingLog();
                PumpSaveState parsed = MwpSaveCodec.Sanitize(
                    MwpSaveCodec.Parse(MwpSaveCodec.Serialize(original), log), log, "Basic");
                Assert.Equal("Advanced", parsed.Tier, "tier");
                Assert.Near(0.42, parsed.Accumulator, 1e-12, "accumulator");
                Assert.Near(0.1, parsed.Contamination, 1e-12, "contamination");
                Assert.Near(77.5, parsed.Condition, 1e-12, "condition");
                Assert.False(parsed.AutoMaintenance, "auto");
                Assert.Equal(MaintenancePhase.Maintaining, parsed.MaintenanceState, "phase");
                Assert.Near(1.5, parsed.MaintenanceRemainingHours, 1e-12, "remaining");
                Assert.Equal(PumpState.LowPower, parsed.State, "state");
                Assert.True(parsed.Paused, "paused");
                Assert.Equal("ValveOnly", parsed.PipeAdapterState, "adapter");
                Assert.Equal(3, parsed.ConsecutiveWaterWithdrawFailures, "failures");
            });

            t.Run("Serialized enums use their contract names", () =>
            {
                var state = new PumpSaveState
                {
                    SchemaVersion = 2,
                    Tier = "Basic",
                    MaintenanceState = MaintenancePhase.Maintaining,
                    State = PumpState.Producing
                };
                string json = MwpSaveCodec.Serialize(state);
                Assert.Contains(json, "\"maintenanceState\":\"Maintaining\"", "phase name");
                Assert.Contains(json, "\"state\":\"Producing\"", "state name");
                Assert.Contains(json, "\"schemaVersion\":2", "schema key");
                Assert.Contains(json, "\"waterWithdrawFailures\":0", "failure key");
            });
        }

        /// <summary>Test group: AdapterStateTests.</summary>
        private static void AdapterStateTests(TestRunner t)
        {
            t.Run("Adapter state names are canonicalized case-insensitively", () =>
            {
                PumpSaveState? parsed = MwpSaveCodec.Parse(
                    "{\"schemaVersion\":2,\"pipeAdapterState\":\"direct\"}", new RecordingLog());
                PumpSaveState state = MwpSaveCodec.Sanitize(parsed, new RecordingLog(), "Basic");
                Assert.Equal("Direct", state.PipeAdapterState, "canonical name");
            });

            t.Run("Empty adapter state becomes Unknown", () =>
            {
                PumpSaveState? parsed = MwpSaveCodec.Parse(
                    "{\"schemaVersion\":2,\"pipeAdapterState\":\"\"}", new RecordingLog());
                PumpSaveState state = MwpSaveCodec.Sanitize(parsed, new RecordingLog(), "Basic");
                Assert.Equal(nameof(PipeAdapterState.Unknown), state.PipeAdapterState, "unknown");
            });
        }
        /// <summary>Test group: NumericClampTests.</summary>
        private static void NumericClampTests(TestRunner t)
        {
            t.Run("Out-of-range numeric fields are clamped", () =>
            {
                var state = new PumpSaveState
                {
                    SchemaVersion = 2,
                    Tier = "Basic",
                    Accumulator = 5.0,
                    Contamination = -1.0,
                    Condition = 150.0,
                    MaintenanceRemainingHours = 999.0,
                    ConsecutiveWaterWithdrawFailures = -3
                };
                PumpSaveState sanitized = MwpSaveCodec.Sanitize(state, new RecordingLog(), "Basic");
                Assert.Near(1.0, sanitized.Accumulator, 1e-12, "accumulator clamp");
                Assert.Near(0.0, sanitized.Contamination, 1e-12, "contamination clamp");
                Assert.Near(100.0, sanitized.Condition, 1e-12, "condition clamp");
                Assert.Near(720.0, sanitized.MaintenanceRemainingHours, 1e-12, "remaining clamp");
                Assert.Equal(0, sanitized.ConsecutiveWaterWithdrawFailures, "failure clamp");
            });
        }

        /// <summary>Test group: EnumNormalizationTests.</summary>
        private static void EnumNormalizationTests(TestRunner t)
        {
            t.Run("Undefined enum values are normalized", () =>
            {
                var state = new PumpSaveState
                {
                    SchemaVersion = 2,
                    Tier = "Basic",
                    MaintenanceState = (MaintenancePhase)99,
                    State = (PumpState)99
                };
                PumpSaveState sanitized = MwpSaveCodec.Sanitize(state, new RecordingLog(), "Basic");
                Assert.Equal(MaintenancePhase.None, sanitized.MaintenanceState, "phase");
                Assert.Equal(PumpState.Producing, sanitized.State, "state");
            });

            t.Run("A valid v2 state keeps its condition and auto flag", () =>
            {
                var state = new PumpSaveState
                {
                    SchemaVersion = 2,
                    Tier = "Top",
                    Accumulator = 0.25,
                    Condition = 62.0,
                    AutoMaintenance = true,
                    MaintenanceState = MaintenancePhase.None,
                    State = PumpState.Producing
                };
                PumpSaveState sanitized = MwpSaveCodec.Sanitize(state, new RecordingLog(), "Basic");
                Assert.Equal("Top", sanitized.Tier, "tier");
                Assert.Near(62.0, sanitized.Condition, 1e-12, "condition kept");
                Assert.True(sanitized.AutoMaintenance, "auto kept");
                Assert.Near(0.25, sanitized.Accumulator, 1e-12, "accumulator kept");
            });
        }
    }
}
