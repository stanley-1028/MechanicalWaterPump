using MWP.Core;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace MWP.Gameplay
{
    /// <summary>
    /// The single, stable table of per-pump save keys (CONTRACT §5, REQ-SAVE-01/02).
    ///
    /// Field names are an external contract: the values below are exactly the JSON names frozen in
    /// the requirements (schema 2). A published save read by a newer build must keep every key
    /// unchanged; anything new must be added, never renamed. The key names travel inside the game's
    /// own world-save transaction as one component on the pump entity, so MWP never writes a second
    /// save pipeline and never touches vanilla data (REQ-SAVE-01/04).
    ///
    /// Types are limited to the persistence primitives the game actually serializes
    /// (<c>int</c>, <c>float</c>, <c>bool</c>, <c>string</c>); enums are stored by name as strings so
    /// adding an enum member later cannot silently reinterpret old data.
    /// </summary>
    internal static class MwpSaveKeys
    {
        /// <summary>One component per pump entity, named "MWP" (REQ-SAVE-01).</summary>
        internal static readonly ComponentKey Component = new ComponentKey(MwpDefaults.SaveComponentKey);

        /// <summary>Save schema version; 2 since v0.2 (REQ-MAINT-11).</summary>
        internal static readonly PropertyKey<int> SchemaVersion = new PropertyKey<int>("schemaVersion");

        /// <summary>
        /// Mod version that wrote this pump (REQ-SAVE-02). Additive key: old saves lack it, which is
        /// tolerated as "unknown" instead of being treated as corruption; the schema version stays 2.
        /// </summary>
        internal static readonly PropertyKey<string> ModVersion = new PropertyKey<string>("modVersion");

        /// <summary>Tier token of the blueprint, for diagnostics after load.</summary>
        internal static readonly PropertyKey<string> Tier = new PropertyKey<string>("tier");

        /// <summary>Fractional Water carry, 0..1.</summary>
        internal static readonly PropertyKey<float> Accumulator = new PropertyKey<float>("accumulator");

        /// <summary>Last sampled intake contamination, 0..1.</summary>
        internal static readonly PropertyKey<float> Contamination = new PropertyKey<float>("contamination");

        /// <summary>Wear 0..100 (REQ-MAINT-01).</summary>
        internal static readonly PropertyKey<float> Condition = new PropertyKey<float>("condition");

        /// <summary>Per-pump automatic maintenance flag (REQ-MAINT-05).</summary>
        internal static readonly PropertyKey<bool> AutoMaintenance = new PropertyKey<bool>("autoMaintenance");

        /// <summary>Maintenance sub-state, stored as the <c>MaintenancePhase</c> name.</summary>
        internal static readonly PropertyKey<string> MaintenanceState = new PropertyKey<string>("maintenanceState");

        /// <summary>Remaining maintenance downtime in game hours.</summary>
        internal static readonly PropertyKey<float> MaintenanceRemainingHours =
            new PropertyKey<float>("maintenanceRemainingHours");

        /// <summary>Last reported state name, for diagnostics after load.</summary>
        internal static readonly PropertyKey<string> State = new PropertyKey<string>("state");

        /// <summary>Whether the player paused this pump only (REQ-UI-07).</summary>
        internal static readonly PropertyKey<bool> Paused = new PropertyKey<bool>("paused");

        /// <summary>Resolved Timber Pipes adapter state name (rebuilt on load, REQ-SAVE-05).</summary>
        internal static readonly PropertyKey<string> PipeAdapterState =
            new PropertyKey<string>("pipeAdapterState");

        /// <summary>Consecutive failed water-body withdrawals (CONTRACT §5 / §11.4).</summary>
        internal static readonly PropertyKey<int> WaterWithdrawFailures =
            new PropertyKey<int>("waterWithdrawFailures");
    }
}
