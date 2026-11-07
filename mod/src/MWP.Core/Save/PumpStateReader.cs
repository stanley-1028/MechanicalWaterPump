using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>
    /// Builds a raw <see cref="PumpSaveState"/> from parsed save JSON and captures properties this
    /// version does not know about, so they survive a load/save cycle (requirements 20.2.6,
    /// REQ-SAVE-05). Internal helper of <see cref="MwpSaveCodec"/>.
    /// </summary>
    internal static class PumpStateReader
    {
        /// <summary>Every property this codec owns; anything else is preserved as unknown data.</summary>
        internal static readonly string[] KnownKeys =
        {
            "schemaVersion",
            "tier",
            "accumulator",
            "contamination",
            "condition",
            "autoMaintenance",
            "maintenanceState",
            "maintenanceRemainingHours",
            "state",
            "paused",
            "pipeAdapterState",
            "waterWithdrawFailures"
        };

        /// <summary>Builds an unvalidated state; missing fields keep the documented safe defaults.</summary>
        internal static PumpSaveState Build(IDictionary<string, object?> root)
        {
            bool corrupt = false;
            var state = new PumpSaveState
            {
                SchemaVersion = SaveFieldReader.ReadInt(root, "schemaVersion", 1, ref corrupt),
                Tier = MiniJson.GetString(root, "tier", string.Empty),
                Accumulator = SaveFieldReader.ReadNumber(root, "accumulator", 0.0, ref corrupt),
                Contamination = SaveFieldReader.ReadNumber(root, "contamination", 0.0, ref corrupt),
                Condition = SaveFieldReader.ReadNumber(root, "condition", 100.0, ref corrupt),
                AutoMaintenance = SaveFieldReader.ReadBool(root, "autoMaintenance", true, ref corrupt),
                MaintenanceState = SaveFieldReader.ReadPhase(root, ref corrupt),
                MaintenanceRemainingHours = SaveFieldReader.ReadNumber(root, "maintenanceRemainingHours", 0.0, ref corrupt),
                State = SaveFieldReader.ReadState(root),
                Paused = SaveFieldReader.ReadBool(root, "paused", false, ref corrupt, markCorrupt: false),
                PipeAdapterState = MiniJson.GetString(root, "pipeAdapterState", nameof(PipeAdapterState.Unknown)),
                ConsecutiveWaterWithdrawFailures = SaveFieldReader.ReadInt(root, "waterWithdrawFailures", 0, ref corrupt)
            };
            state.CorruptInput = corrupt;
            return state;
        }

        /// <summary>Copies unknown properties of the JSON object into the state.</summary>
        internal static void CaptureUnknown(IDictionary<string, object?> root, PumpSaveState state)
        {
            foreach (KeyValuePair<string, object?> pair in root)
            {
                if (!IsKnownKey(pair.Key))
                {
                    state.UnknownFields[pair.Key] = pair.Value;
                }
            }
        }

        private static bool IsKnownKey(string key)
        {
            for (int i = 0; i < KnownKeys.Length; i++)
            {
                if (string.Equals(KnownKeys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
