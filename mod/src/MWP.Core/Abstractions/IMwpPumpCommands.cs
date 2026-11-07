using System;

namespace MWP.Core
{
    /// <summary>
    /// Command surface the entity panel binds its buttons to. Implemented and registered as a
    /// singleton by MWP.Gameplay so MWP.UI never references the gameplay assembly (REQ-UI-07).
    /// Entity ids are <see cref="Guid"/> because the game exposes
    /// <c>EntityComponent.EntityId</c> as a <see cref="Guid"/> (verified in game IL).
    /// </summary>
    public interface IMwpPumpCommands
    {
        /// <summary>Requests maintenance now for one pump (REQ-MAINT-05).</summary>
        /// <param name="entityId">Entity id of the pump.</param>
        /// <returns>True when the request was accepted (materials/path are re-checked by the controller).</returns>
        bool RequestMaintenance(Guid entityId);

        /// <summary>Enables or disables automatic maintenance for one pump (REQ-MAINT-05).</summary>
        /// <param name="entityId">Entity id of the pump.</param>
        /// <param name="enabled">New value of the auto-maintenance flag.</param>
        /// <returns>True when the pump was found and updated.</returns>
        bool SetAutoMaintenance(Guid entityId, bool enabled);

        /// <summary>Pauses or resumes one pump only (REQ-UI-07).</summary>
        /// <param name="entityId">Entity id of the pump.</param>
        /// <param name="paused">True to pause, false to resume.</param>
        /// <returns>True when the pump was found and updated.</returns>
        bool SetPaused(Guid entityId, bool paused);
    }
}
