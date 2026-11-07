using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>One of the four pump tiers. The numeric value is stable and used by config arrays.</summary>
    public enum PumpTier
    {
        /// <summary>Mk I, the early-game tier (150 science, 3 Water/h).</summary>
        Basic = 0,

        /// <summary>Mk II, the mid-game mainstay (400 science, 6 Water/h).</summary>
        Intermediate = 1,

        /// <summary>Mk III, the late-game metal tier (700 science, 15 Water/h).</summary>
        Advanced = 2,

        /// <summary>Mk IV, the end-game tier (1500 science, 20 Water/h).</summary>
        Top = 3
    }

    /// <summary>
    /// Machine-readable state of one pump. The declaration order is deliberately the reverse of
    /// the UI priority order (see <see cref="StatusPriority"/>) so that a lower enum value is not
    /// mistaken for "more important".
    /// </summary>
    public enum PumpState
    {
        /// <summary>Construction is not finished yet; no water, no power draw.</summary>
        Unfinished = 0,

        /// <summary>Running normally at (nearly) full rated output.</summary>
        Producing = 1,

        /// <summary>Player paused this pump only.</summary>
        Paused = 2,

        /// <summary>Power supplied is above zero but below the requirement; output is scaled.</summary>
        LowPower = 3,

        /// <summary>Condition in 25..50: warning, output scaled to 0.75 (REQ-MAINT-09).</summary>
        MaintenanceDue = 4,

        /// <summary>Condition in 1..25: warning, output scaled to 0.50 (REQ-MAINT-09).</summary>
        Critical = 5,

        /// <summary>Maintenance was requested but no road can deliver the materials.</summary>
        NoAccessForMaintenance = 6,

        /// <summary>Maintenance was requested but the district lacks the materials.</summary>
        MissingMaintenanceMaterials = 7,

        /// <summary>Maintenance downtime in progress (2 game hours by default); production stopped.</summary>
        Maintaining = 8,

        /// <summary>Condition reached 0; production is stopped until maintenance completes.</summary>
        NeedsMaintenance = 9,

        /// <summary>Internal tank is full; production stopped until haulers or a pipe drain it.</summary>
        StorageFull = 10,

        /// <summary>No road reaches the pickup point (REQ-CORE-04).</summary>
        NoAccess = 11,

        /// <summary>No extractable water at the intake (REQ-CORE-01).</summary>
        NoWater = 12,

        /// <summary>No mechanical power at all (REQ-CORE-01).</summary>
        NoPower = 13
    }

    /// <summary>Maintenance sub-state kept in the save file (schema v2).</summary>
    public enum MaintenancePhase
    {
        /// <summary>Normal wear accumulation.</summary>
        None = 0,

        /// <summary>Condition at or below the auto-maintenance threshold, waiting for the next tick.</summary>
        Due = 1,

        /// <summary>Condition in the critical band.</summary>
        Critical = 2,

        /// <summary>Downtime timer running.</summary>
        Maintaining = 3,

        /// <summary>Condition hit zero; production stopped (REQ-MAINT-07).</summary>
        NeedsMaintenance = 4,

        /// <summary>Materials missing, maintenance skipped (REQ-MAINT-06).</summary>
        MissingMaterials = 5,

        /// <summary>Materials present but no reachable road (REQ-MAINT-06).</summary>
        NoAccess = 6
    }

    /// <summary>How the optional Timber Pipes integration resolved for this pump (REQ-COMPAT-08).</summary>
    public enum PipeAdapterState
    {
        /// <summary>Detection did not run yet (safe default after load).</summary>
        Unknown = 0,

        /// <summary>Timber Pipes is not loaded; the valve path is unavailable but the core works.</summary>
        None = 1,

        /// <summary>Timber Pipes loaded; a one-way valve can extract the internal Water inventory (MUST path).</summary>
        ValveOnly = 2,

        /// <summary>Timber Pipes loaded and the direct output port resolved (optional SHOULD path).</summary>
        Direct = 3,

        /// <summary>An adapter call failed; the pump fell back to the valve path (REQ-COMPAT-03).</summary>
        Error = 4
    }

    /// <summary>
    /// The single source of truth for "which status do we show". NoPower is the highest priority,
    /// Producing the lowest; maintenance states sit between StorageFull and LowPower (REQ-UI-03).
    /// </summary>
    public static class StatusPriority
    {
        private static readonly PumpState[] Order =
        {
            PumpState.NoPower,
            PumpState.NoWater,
            PumpState.NoAccess,
            PumpState.StorageFull,
            PumpState.NeedsMaintenance,
            PumpState.Maintaining,
            PumpState.MissingMaintenanceMaterials,
            PumpState.NoAccessForMaintenance,
            PumpState.Critical,
            PumpState.MaintenanceDue,
            PumpState.LowPower,
            PumpState.Paused,
            PumpState.Unfinished,
            PumpState.Producing
        };

        /// <summary>Returns the display priority of a state; 0 is the highest priority.</summary>
        /// <param name="state">State to rank.</param>
        public static int Rank(PumpState state)
        {
            for (int i = 0; i < Order.Length; i++)
            {
                if (Order[i] == state)
                {
                    return i;
                }
            }

            return Order.Length;
        }

        /// <summary>Returns the highest-priority state among the candidates.</summary>
        /// <param name="states">Candidate states; must not be null.</param>
        public static PumpState Highest(IReadOnlyList<PumpState> states)
        {
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            PumpState best = PumpState.Producing;
            int bestRank = int.MaxValue;
            for (int i = 0; i < states.Count; i++)
            {
                int rank = Rank(states[i]);
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = states[i];
                }
            }

            return best;
        }
    }
}
