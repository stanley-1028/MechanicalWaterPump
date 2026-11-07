using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>
    /// Save schema validation, migration and the JSON form of one pump (REQ-SAVE-03/05,
    /// REQ-MAINT-11). Loading is defensive: a single damaged pump resets itself and reports a
    /// warning instead of failing the whole save. Unknown properties are preserved so a file
    /// written by a newer/older version can round-trip (requirements 20.2.6).
    /// </summary>
    public static class MwpSaveCodec
    {
        /// <summary>Upper bound for a persisted maintenance timer, in game hours.</summary>
        private const double MaxMaintenanceRemainingHours = 720.0;

        /// <summary>Upper bound for the consecutive-withdraw-failure counter.</summary>
        private const int MaxWithdrawFailures = 1000;

        /// <summary>Parses the JSON form of one pump; null when the text is not a pump object.</summary>
        /// <param name="json">JSON text produced by <see cref="Serialize"/> or an older version.</param>
        /// <param name="log">Diagnostics sink; null is tolerated.</param>
        /// <returns>The parsed state (still unvalidated), or null when unparsable.</returns>
        public static PumpSaveState? Parse(string json, IMwpLog log)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Warn(log, MwpLogKeys.SaveCorrupt, "empty pump save data");
                return null;
            }

            try
            {
                if (MiniJson.Parse(json) is not IDictionary<string, object?> root)
                {
                    Warn(log, MwpLogKeys.SaveCorrupt, "pump save data is not a JSON object");
                    return null;
                }

                PumpSaveState state = PumpStateReader.Build(root);
                PumpStateReader.CaptureUnknown(root, state);
                return state;
            }
            catch (Exception ex)
            {
                Warn(log, MwpLogKeys.SaveCorrupt, "unparsable pump save data (" + ex.GetType().Name + ")");
                return null;
            }
        }

        /// <summary>
        /// Validates and migrates one pump's state. A corrupt pump is safely reset (condition 100,
        /// automatic maintenance off, warning logged) and never affects other pumps (REQ-SAVE-03).
        /// </summary>
        /// <param name="raw">Parsed state; null means "no data stored for this pump".</param>
        /// <param name="log">Diagnostics sink; null is tolerated.</param>
        /// <param name="tierFallback">Tier token used when the saved tier is missing/unknown.</param>
        /// <returns>A valid state with the current schema version.</returns>
        public static PumpSaveState Sanitize(PumpSaveState? raw, IMwpLog log, string tierFallback)
        {
            string fallbackTier = ResolveTier(tierFallback);
            if (raw == null)
            {
                Warn(log, MwpLogKeys.SaveCorrupt, "no pump data; using safe defaults for " + fallbackTier);
                return PumpSaveState.NewGame(fallbackTier);
            }

            PumpSaveState state = Migrate(raw, log);
            if (state.CorruptInput || !SaveFieldReader.IsFinite(state.Accumulator)
                || !SaveFieldReader.IsFinite(state.Contamination) || !SaveFieldReader.IsFinite(state.Condition)
                || !SaveFieldReader.IsFinite(state.MaintenanceRemainingHours))
            {
                Warn(log, MwpLogKeys.SaveCorrupt, "corrupt pump data; safe reset to " + fallbackTier);
                return SafeReset(state, fallbackTier);
            }

            ClampKnownFields(state, fallbackTier, log);
            return state;
        }

        /// <summary>
        /// Migrates an older schema to v2: v1 gets condition 100, auto maintenance on, phase None
        /// and a zero timer, while every other field (including unknown properties) is kept.
        /// Idempotent: running it twice produces the same result (REQ-MAINT-11).
        /// </summary>
        /// <param name="raw">State to migrate in place.</param>
        /// <param name="log">Diagnostics sink; null is tolerated.</param>
        /// <returns>The migrated state (the same instance).</returns>
        public static PumpSaveState Migrate(PumpSaveState raw, IMwpLog log)
        {
            if (raw == null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            if (raw.SchemaVersion > MwpDefaults.SaveSchemaVersion)
            {
                Warn(log, MwpLogKeys.SaveError, "save schema " + raw.SchemaVersion + " is newer than supported; keeping known fields");
                raw.SchemaVersion = MwpDefaults.SaveSchemaVersion;
                return raw;
            }

            if (raw.SchemaVersion >= MwpDefaults.SaveSchemaVersion)
            {
                return raw;
            }

            raw.Condition = 100.0;
            raw.AutoMaintenance = true;
            raw.MaintenanceState = MaintenancePhase.None;
            raw.MaintenanceRemainingHours = 0.0;
            if (string.IsNullOrWhiteSpace(raw.Tier))
            {
                raw.Tier = TierCatalog.Default[0].TierName;
            }

            raw.SchemaVersion = MwpDefaults.SaveSchemaVersion;
            Info(log, MwpLogKeys.SaveMigrated, "pump save migrated to schema " + MwpDefaults.SaveSchemaVersion);
            return raw;
        }

        /// <summary>Serializes one pump to compact JSON, re-emitting preserved unknown properties.</summary>
        /// <param name="state">State to write; must not be null.</param>
        /// <returns>JSON text.</returns>
        public static string Serialize(PumpSaveState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var root = new Dictionary<string, object?>
            {
                ["schemaVersion"] = state.SchemaVersion,
                ["tier"] = state.Tier,
                ["accumulator"] = state.Accumulator,
                ["contamination"] = state.Contamination,
                ["condition"] = state.Condition,
                ["autoMaintenance"] = state.AutoMaintenance,
                ["maintenanceState"] = state.MaintenanceState.ToString(),
                ["maintenanceRemainingHours"] = state.MaintenanceRemainingHours,
                ["state"] = state.State.ToString(),
                ["paused"] = state.Paused,
                ["pipeAdapterState"] = state.PipeAdapterState,
                ["waterWithdrawFailures"] = state.ConsecutiveWaterWithdrawFailures
            };

            foreach (KeyValuePair<string, object?> pair in state.UnknownFields)
            {
                if (!root.ContainsKey(pair.Key))
                {
                    root[pair.Key] = pair.Value;
                }
            }

            return MiniJson.Write(root);
        }

        /// <summary>Clamps every known field into its contract range and normalizes enum strings.</summary>
        private static void ClampKnownFields(PumpSaveState state, string fallbackTier, IMwpLog log)
        {
            TierDefinition? definition = TierCatalog.TryGet(state.Tier);
            if (definition == null)
            {
                Warn(log, MwpLogKeys.SaveCorrupt, "unknown tier '" + state.Tier + "'; using " + fallbackTier);
            }

            state.Tier = definition != null ? definition.TierName : fallbackTier;
            state.Accumulator = BalanceFormulas.Sanitize(state.Accumulator, 0.0, 0.0, 1.0);
            state.Contamination = BalanceFormulas.Sanitize(state.Contamination, 0.0, 0.0, 1.0);
            state.Condition = BalanceFormulas.Sanitize(state.Condition, 100.0, 0.0, 100.0);
            state.MaintenanceRemainingHours = BalanceFormulas.Sanitize(
                state.MaintenanceRemainingHours,
                0.0,
                0.0,
                MaxMaintenanceRemainingHours);
            state.ConsecutiveWaterWithdrawFailures = SaveFieldReader.ClampInt(
                state.ConsecutiveWaterWithdrawFailures,
                0,
                MaxWithdrawFailures);
            state.MaintenanceState = Enum.IsDefined(typeof(MaintenancePhase), state.MaintenanceState)
                ? state.MaintenanceState
                : MaintenancePhase.None;
            state.State = Enum.IsDefined(typeof(PumpState), state.State) ? state.State : PumpState.Producing;
            state.PipeAdapterState = NormalizePipe(state.PipeAdapterState);
            state.SchemaVersion = MwpDefaults.SaveSchemaVersion;
        }

        /// <summary>Safe reset of one corrupt pump: valid defaults, automatic maintenance off (20.2.6).</summary>
        private static PumpSaveState SafeReset(PumpSaveState source, string tier)
        {
            PumpSaveState reset = PumpSaveState.NewGame(tier);
            reset.State = PumpState.Producing;
            reset.AutoMaintenance = false;
            reset.Condition = 100.0;
            reset.MaintenanceState = MaintenancePhase.None;
            reset.MaintenanceRemainingHours = 0.0;
            reset.Accumulator = 0.0;
            reset.Contamination = 0.0;
            reset.PipeAdapterState = nameof(PipeAdapterState.Unknown);
            reset.CopyUnknownFrom(source);
            return reset;
        }

        /// <summary>Resolves a tier token or falls back to the Basic tier.</summary>
        private static string ResolveTier(string? tier)
        {
            TierDefinition? definition = TierCatalog.TryGet(tier);
            return definition != null ? definition.TierName : TierCatalog.Default[0].TierName;
        }

        /// <summary>
        /// Normalizes the persisted adapter state name. Save files written before v0.2 used the
        /// tokens none/valve/direct/error (requirements 14.1); the enum names are the canonical
        /// form. Empty becomes Unknown; an unrecognized future value is kept as-is because the
        /// adapter is re-detected on load anyway (REQ-SAVE-05).
        /// </summary>
        private static string NormalizePipe(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return nameof(PipeAdapterState.Unknown);
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "unknown": return nameof(PipeAdapterState.Unknown);
                case "none": return nameof(PipeAdapterState.None);
                case "valve":
                case "valveonly": return nameof(PipeAdapterState.ValveOnly);
                case "direct": return nameof(PipeAdapterState.Direct);
                case "error": return nameof(PipeAdapterState.Error);
                default: return value.Trim();
            }
        }

        private static void Warn(IMwpLog? log, string key, string message)
        {
            try
            {
                log?.Warn(key, message);
            }
            catch (Exception)
            {
                // Diagnostics only: a broken log sink must not break save loading.
            }
        }

        private static void Info(IMwpLog? log, string key, string message)
        {
            try
            {
                log?.Info(key, message);
            }
            catch (Exception)
            {
                // Diagnostics only.
            }
        }
    }
}
