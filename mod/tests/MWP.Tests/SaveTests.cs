using MWP.Core;

namespace MWP.Tests
{
    // REQ-NFR-03 note: the 40-line cap is a soft guideline for logic. The group methods in
    // this file are declarative lists of independent t.Run cases; splitting them further
    // would only scatter the cases, so the documented reason for exceeding it is recorded here.
    /// <summary>Unit tests for the pump save codec: schema v1 to v2 migration, corrupt reset and unknown-field preservation.</summary>
    internal static class SaveTests
    {
        /// <summary>A schema v1 pump document used by the migration tests.</summary>
        private const string V1Json = "{\"schemaVersion\":1,\"tier\":\"Basic\",\"accumulator\":0.42,"
            + "\"contamination\":0.1,\"state\":\"Producing\",\"paused\":false,"
            + "\"pipeAdapterState\":\"valve\",\"waterWithdrawFailures\":2,"
            + "\"futureField\":\"keep-me\",\"futureNested\":{\"a\":1}}";

        internal static void Run(TestRunner t)
        {
            NewGameTests(t);
            MigrationTests(t);
            CorruptTests(t);
        }

        private static void NewGameTests(TestRunner t)
        {
            t.Run("New game state has safe defaults", () =>
            {
                PumpSaveState state = PumpSaveState.NewGame("Advanced");
                Assert.Equal(2, state.SchemaVersion, "schema");
                Assert.Equal("Advanced", state.Tier, "tier");
                Assert.Near(100.0, state.Condition, 1e-12, "condition");
                Assert.True(state.AutoMaintenance, "auto on");
                Assert.Equal(MaintenancePhase.None, state.MaintenanceState, "phase");
                Assert.Near(0.0, state.MaintenanceRemainingHours, 1e-12, "timer");
                Assert.Near(0.0, state.Accumulator, 1e-12, "accumulator");
                Assert.Near(0.0, state.Contamination, 1e-12, "contamination");
                Assert.False(state.Paused, "paused");
                Assert.Equal(nameof(PipeAdapterState.Unknown), state.PipeAdapterState, "adapter");
                Assert.Equal(PumpState.Unfinished, state.State, "unfinished");
                Assert.Equal(0, state.ConsecutiveWaterWithdrawFailures, "failures");
            });

            t.Run("New game with an unknown tier uses Basic", () =>
            {
                Assert.Equal("Basic", PumpSaveState.NewGame("Bogus").Tier, "fallback tier");
            });
        }

        private static void MigrationTests(TestRunner t)
        {
            MigrationDefaultsTests(t);
            MigrationEdgeTests(t);

        }

        private static void CorruptTests(TestRunner t)
        {
            CorruptResetTests(t);
            MissingDataTests(t);

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
        /// <summary>Test group: MigrationDefaultsTests.</summary>
        private static void MigrationDefaultsTests(TestRunner t)
        {
            t.Run("v1 state migrates to v2 with maintenance defaults", () =>
            {
                var log = new RecordingLog();
                PumpSaveState? parsed = MwpSaveCodec.Parse(V1Json, log);
                Assert.NotNull(parsed, "parsed v1");
                PumpSaveState state = MwpSaveCodec.Sanitize(parsed, log, "Basic");
                Assert.Equal(2, state.SchemaVersion, "schema 2");
                Assert.Near(100.0, state.Condition, 1e-12, "condition 100");
                Assert.True(state.AutoMaintenance, "auto true");
                Assert.Equal(MaintenancePhase.None, state.MaintenanceState, "phase none");
                Assert.Near(0.0, state.MaintenanceRemainingHours, 1e-12, "timer zero");
                Assert.Near(0.42, state.Accumulator, 1e-12, "accumulator kept");
                Assert.Near(0.1, state.Contamination, 1e-12, "contamination kept");
                Assert.Equal(PumpState.Producing, state.State, "state kept");
                Assert.Equal("ValveOnly", state.PipeAdapterState, "adapter canonicalized");
                Assert.Equal(2, state.ConsecutiveWaterWithdrawFailures, "failures kept");
                Assert.True(ContainsInfo(log, MwpLogKeys.SaveMigrated), "migration logged");
            });

            t.Run("Migration is idempotent", () =>
            {
                var log = new RecordingLog();
                PumpSaveState state = MwpSaveCodec.Sanitize(MwpSaveCodec.Parse(V1Json, log), log, "Basic");
                double condition = state.Condition;
                bool auto = state.AutoMaintenance;
                MaintenancePhase phase = state.MaintenanceState;
                double remaining = state.MaintenanceRemainingHours;

                MwpSaveCodec.Migrate(state, log);
                MwpSaveCodec.Migrate(state, log);
                Assert.Equal(2, state.SchemaVersion, "schema stays 2");
                Assert.Near(condition, state.Condition, 1e-12, "condition unchanged");
                Assert.Equal(auto, state.AutoMaintenance, "auto unchanged");
                Assert.Equal(phase, state.MaintenanceState, "phase unchanged");
                Assert.Near(remaining, state.MaintenanceRemainingHours, 1e-12, "timer unchanged");
            });

            t.Run("Migrating a v2 state keeps its maintenance values", () =>
            {
                var state = new PumpSaveState
                {
                    SchemaVersion = 2,
                    Tier = "Basic",
                    Condition = 40.0,
                    AutoMaintenance = false,
                    MaintenanceState = MaintenancePhase.Critical,
                    MaintenanceRemainingHours = 0.0
                };
                MwpSaveCodec.Migrate(state, new RecordingLog());
                Assert.Near(40.0, state.Condition, 1e-12, "condition kept");
                Assert.False(state.AutoMaintenance, "auto kept");
                Assert.Equal(MaintenancePhase.Critical, state.MaintenanceState, "phase kept");
            });
        }

        /// <summary>Test group: MigrationEdgeTests.</summary>
        private static void MigrationEdgeTests(TestRunner t)
        {
            t.Run("Newer schema version is clamped to v2 with a save error", () =>
            {
                var log = new RecordingLog();
                var state = new PumpSaveState { SchemaVersion = 9, Tier = "Basic", Condition = 80.0 };
                PumpSaveState migrated = MwpSaveCodec.Migrate(state, log);
                Assert.Equal(2, migrated.SchemaVersion, "clamped schema");
                Assert.Near(80.0, migrated.Condition, 1e-12, "known fields kept");
                Assert.True(ContainsWarn(log, MwpLogKeys.SaveError), "save version warning logged");
            });

            t.Run("v1 state without a tier gets Basic", () =>
            {
                PumpSaveState? parsed = MwpSaveCodec.Parse("{\"schemaVersion\":1}", new RecordingLog());
                PumpSaveState state = MwpSaveCodec.Sanitize(parsed, new RecordingLog(), "Basic");
                Assert.Equal("Basic", state.Tier, "tier filled");
            });

            t.Run("Unknown fields survive a v1 to v2 migration", () =>
            {
                var log = new RecordingLog();
                PumpSaveState state = MwpSaveCodec.Sanitize(MwpSaveCodec.Parse(V1Json, log), log, "Basic");
                string json = MwpSaveCodec.Serialize(state);
                Assert.Contains(json, "\"futureField\":\"keep-me\"", "unknown string kept");
                Assert.Contains(json, "\"futureNested\":{\"a\":1}", "unknown object kept");
            });
        }
        /// <summary>Test group: CorruptResetTests.</summary>
        private static void CorruptResetTests(TestRunner t)
        {
            t.Run("Wrong-typed field safely resets the pump", () =>
            {
                var log = new RecordingLog();
                PumpSaveState? parsed = MwpSaveCodec.Parse(
                    "{\"schemaVersion\":2,\"tier\":\"Basic\",\"accumulator\":\"oops\",\"condition\":40}",
                    log);
                PumpSaveState state = MwpSaveCodec.Sanitize(parsed, log, "Basic");
                Assert.Equal(2, state.SchemaVersion, "schema");
                Assert.Near(100.0, state.Condition, 1e-12, "condition reset");
                Assert.False(state.AutoMaintenance, "auto maintenance disabled on reset");
                Assert.Equal(MaintenancePhase.None, state.MaintenanceState, "phase reset");
                Assert.Near(0.0, state.Accumulator, 1e-12, "accumulator reset");
                Assert.True(ContainsWarn(log, MwpLogKeys.SaveCorrupt), "corruption warning");
            });

            t.Run("NaN field safely resets the pump", () =>
            {
                var log = new RecordingLog();
                var state = new PumpSaveState
                {
                    SchemaVersion = 2,
                    Tier = "Basic",
                    Condition = double.NaN,
                    AutoMaintenance = true
                };
                PumpSaveState sanitized = MwpSaveCodec.Sanitize(state, log, "Basic");
                Assert.Near(100.0, sanitized.Condition, 1e-12, "condition reset");
                Assert.False(sanitized.AutoMaintenance, "auto disabled");
                Assert.True(ContainsWarn(log, MwpLogKeys.SaveCorrupt), "warning logged");
            });

            t.Run("Invalid maintenance phase string is corruption", () =>
            {
                var log = new RecordingLog();
                PumpSaveState? parsed = MwpSaveCodec.Parse(
                    "{\"schemaVersion\":2,\"tier\":\"Basic\",\"maintenanceState\":\"Garbage\"}", log);
                PumpSaveState state = MwpSaveCodec.Sanitize(parsed, log, "Basic");
                Assert.Near(100.0, state.Condition, 1e-12, "reset condition");
                Assert.False(state.AutoMaintenance, "reset auto");
                Assert.Equal(MaintenancePhase.None, state.MaintenanceState, "reset phase");
            });

            t.Run("Corrupt reset keeps unknown fields", () =>
            {
                var log = new RecordingLog();
                PumpSaveState? parsed = MwpSaveCodec.Parse(
                    "{\"schemaVersion\":2,\"accumulator\":\"bad\",\"future\":\"x\"}", log);
                PumpSaveState state = MwpSaveCodec.Sanitize(parsed, log, "Basic");
                Assert.Contains(MwpSaveCodec.Serialize(state), "\"future\":\"x\"", "unknown field kept");
            });

            t.Run("Missing pump data uses the tier fallback", () =>
            {
                var log = new RecordingLog();
                PumpSaveState state = MwpSaveCodec.Sanitize(null, log, "Top");
                Assert.Equal("Top", state.Tier, "fallback tier");
                Assert.Near(100.0, state.Condition, 1e-12, "condition");
                Assert.True(ContainsWarn(log, MwpLogKeys.SaveCorrupt), "warning logged");
            });

            t.Run("Unknown tier falls back without a full reset", () =>
            {
                var log = new RecordingLog();
                PumpSaveState? parsed = MwpSaveCodec.Parse(
                    "{\"schemaVersion\":2,\"tier\":\"Mega\",\"accumulator\":0.5,\"condition\":70}", log);
                PumpSaveState state = MwpSaveCodec.Sanitize(parsed, log, "Intermediate");
                Assert.Equal("Intermediate", state.Tier, "tier fallback");
                Assert.Near(0.5, state.Accumulator, 1e-12, "accumulator kept");
                Assert.Near(70.0, state.Condition, 1e-12, "condition kept");
            });
        }

        /// <summary>Test group: MissingDataTests.</summary>
        private static void MissingDataTests(TestRunner t)
        {
            t.Run("Parse returns null for invalid documents", () =>
            {
                Assert.True(MwpSaveCodec.Parse("not json", new RecordingLog()) == null, "garbage");
                Assert.True(MwpSaveCodec.Parse(string.Empty, new RecordingLog()) == null, "empty");
                Assert.True(MwpSaveCodec.Parse("[]", new RecordingLog()) == null, "array root");
            });
        }
    }
}
