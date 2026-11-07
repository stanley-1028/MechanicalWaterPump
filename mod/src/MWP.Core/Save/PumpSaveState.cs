using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>
    /// Plain snapshot of one pump's persisted data (save schema 2, requirements 20.2.6). The
    /// gameplay layer copies it from <c>IObjectSaver</c>/<c>IObjectLoader</c> keys; Core only owns
    /// validation, migration and the JSON form used by tests and diagnostics (REQ-SAVE-01).
    /// </summary>
    public sealed class PumpSaveState
    {
        /// <summary>Schema version of the saved data; 2 since v0.2 (REQ-MAINT-11).</summary>
        public int SchemaVersion { get; set; }

        /// <summary>Tier token ("Basic", "Intermediate", "Advanced", "Top").</summary>
        public string Tier { get; set; } = string.Empty;

        /// <summary>Fractional Water accumulator, 0..1; the fraction is kept across save/load.</summary>
        public double Accumulator { get; set; }

        /// <summary>Last sampled intake contamination, 0..1.</summary>
        public double Contamination { get; set; }

        /// <summary>Wear value 0..100 (REQ-MAINT-01).</summary>
        public double Condition { get; set; }

        /// <summary>Per-pump automatic maintenance flag (REQ-MAINT-05).</summary>
        public bool AutoMaintenance { get; set; }

        /// <summary>Persisted maintenance sub-state (REQ-MAINT-11).</summary>
        public MaintenancePhase MaintenanceState { get; set; }

        /// <summary>Remaining maintenance downtime in game hours, 0 when not maintaining.</summary>
        public double MaintenanceRemainingHours { get; set; }

        /// <summary>Last reported state, used for diagnostics after load; recomputed on the next tick.</summary>
        public PumpState State { get; set; }

        /// <summary>Whether the player paused this pump only.</summary>
        public bool Paused { get; set; }

        /// <summary>Resolved Timber Pipes adapter state name (<see cref="PipeAdapterState"/>); rebuilt on load.</summary>
        public string PipeAdapterState { get; set; } = string.Empty;

        /// <summary>Consecutive failed water-body withdrawals; above 5 reports NoWater (§11.4).</summary>
        public int ConsecutiveWaterWithdrawFailures { get; set; }

        /// <summary>Set by <see cref="MwpSaveCodec.Parse"/> when a field had the wrong JSON type.</summary>
        internal bool CorruptInput { get; set; }

        /// <summary>Unknown JSON properties preserved verbatim across parse/serialize (REQ-SAVE-05).</summary>
        internal Dictionary<string, object?> UnknownFields { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>Creates the state of a freshly built pump: condition 100, schema 2, not yet finished.</summary>
        /// <param name="tier">Tier token; unknown values fall back to Basic.</param>
        /// <returns>A valid state with safe defaults.</returns>
        public static PumpSaveState NewGame(string tier)
        {
            string validTier = TierCatalog.TryGet(tier) != null
                ? tier
                : TierCatalog.Default[0].TierName;
            return new PumpSaveState
            {
                SchemaVersion = MwpDefaults.SaveSchemaVersion,
                Tier = validTier,
                Accumulator = 0.0,
                Contamination = 0.0,
                Condition = 100.0,
                AutoMaintenance = true,
                MaintenanceState = MaintenancePhase.None,
                MaintenanceRemainingHours = 0.0,
                State = PumpState.Unfinished,
                Paused = false,
                PipeAdapterState = nameof(MWP.Core.PipeAdapterState.Unknown),
                ConsecutiveWaterWithdrawFailures = 0
            };
        }

        /// <summary>Copies preserved unknown properties from another state (used on safe reset).</summary>
        /// <param name="other">State whose unknown properties should be preserved.</param>
        internal void CopyUnknownFrom(PumpSaveState other)
        {
            foreach (KeyValuePair<string, object?> pair in other.UnknownFields)
            {
                UnknownFields[pair.Key] = pair.Value;
            }
        }
    }
}
