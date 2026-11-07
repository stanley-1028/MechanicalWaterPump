using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>Power input abstraction, implemented over the game's MechanicalNode (REQ-NFR-05).</summary>
    public interface IPumpPowerSource
    {
        /// <summary>Power actually delivered to this consumer, in hp.</summary>
        double SupplyHp { get; }

        /// <summary>Nominal demand, in hp.</summary>
        int RequiredHp { get; }

        /// <summary>Engine-provided efficiency/power ratio, clamped to 0..1.</summary>
        double Ratio { get; }

        /// <summary>Whether the building is finished, active and powered at all.</summary>
        bool Powered { get; }
    }

    /// <summary>Water intake abstraction, implemented over the vanilla WaterInput component.</summary>
    public interface IWaterSource
    {
        /// <summary>True when the intake cell is underwater and not blocked (vanilla WaterInput.IsUnderwater).</summary>
        bool HasWater { get; }

        /// <summary>Column contamination, 0..1 (vanilla WaterInput.ContaminationPercentage).</summary>
        double Contamination { get; }

        /// <summary>
        /// Requests <paramref name="cubicMeters"/> of clean water from the water body.
        /// Returns the amount actually granted (0 when the body is empty).
        /// </summary>
        /// <param name="cubicMeters">Requested water body volume in m³.</param>
        double Withdraw(double cubicMeters);
    }

    /// <summary>Maintenance material abstraction, implemented over the district inventory.</summary>
    public interface IMaintenanceStore
    {
        /// <summary>Whether the district currently holds every required material.</summary>
        /// <param name="materials">Required stacks.</param>
        bool HasMaterials(IReadOnlyList<GoodStack> materials);

        /// <summary>Consumes the materials. Returns false (and consumes nothing) if they are missing.</summary>
        /// <param name="materials">Required stacks.</param>
        bool TryConsume(IReadOnlyList<GoodStack> materials);

        /// <summary>Whether a road reaches this building's pickup point (REQ-MAINT-06).</summary>
        bool IsReachable();
    }

    /// <summary>Optional Timber Pipes integration exposed to the UI and diagnostics (REQ-COMPAT-08).</summary>
    public interface IPipeAdapter
    {
        /// <summary>Resolved adapter state for a pump tier.</summary>
        /// <param name="tierName">Tier token.</param>
        PipeAdapterState GetState(string tierName);

        /// <summary>Human-readable diagnostic text for the compat log.</summary>
        string Diagnostic { get; }

        /// <summary>Whether direct pipe output is enabled by config and resolved (SHOULD path).</summary>
        bool DirectOutputAvailable { get; }
    }
}
