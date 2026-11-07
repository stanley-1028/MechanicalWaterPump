using System;
using System.Collections.Generic;
using MWP.Core;

namespace MWP.Gameplay.Components
{
    /// <summary>
    /// Gameplay-side registration surface the pump controller uses itself. Kept separate from the
    /// frozen Core interfaces so the controller never depends on the UI contract for lifecycle
    /// bookkeeping (REQ-NFR-05).
    /// </summary>
    public interface IMwpPumpRegistry
    {
        /// <summary>Registers a finished pump under its entity id.</summary>
        /// <param name="entityId">Entity id from <c>EntityComponent</c>.</param>
        /// <param name="controller">Controller to register.</param>
        void Register(Guid entityId, MwpPumpController controller);

        /// <summary>Removes a pump (deactivation/deletion).</summary>
        /// <param name="entityId">Entity id to remove.</param>
        void Unregister(Guid entityId);
    }

    /// <summary>
    /// Session singleton that answers both UI surfaces: <see cref="IMwpPumpQuery"/> (read snapshots)
    /// and <see cref="IMwpPumpCommands"/> (act on one pump only, REQ-UI-07).
    ///
    /// One instance implements all three interfaces and is bound with <c>ToExisting</c> so the query
    /// and the commands observe the same registry (two independent bindings would create two
    /// dictionaries and the panel buttons would silently target nothing).
    ///
    /// Contract: every public entry point is isolated — a broken pump can never stop the UI from
    /// rendering or another pump from being controlled (REQ-SAFE-02); lookups are O(1); no snapshot
    /// is ever stored in the save.
    /// </summary>
    public sealed class MwpPumpRegistry : IMwpPumpQuery, IMwpPumpCommands, IMwpPumpRegistry
    {
        private const string LogKeyQuery = "MWP/Error/Query";

        private readonly Dictionary<Guid, MwpPumpController> _pumps =
            new Dictionary<Guid, MwpPumpController>(64);

        private readonly MwpRuntime _runtime;

        /// <summary>Creates the registry; the logger is used only on the rare failure path.</summary>
        /// <param name="runtime">Gameplay runtime singleton.</param>
        public MwpPumpRegistry(MwpRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>Number of currently active pumps; diagnostics only.</summary>
        public int RegisteredPumpCount => _pumps.Count;

        /// <inheritdoc />
        public void Register(Guid entityId, MwpPumpController controller)
        {
            if (controller == null || entityId == Guid.Empty)
            {
                TryLogWarn("pump registration skipped: entity id is empty");
                return;
            }

            _pumps[entityId] = controller;
            _runtime.ReportPumpCount(_pumps.Count);
        }

        /// <inheritdoc />
        public void Unregister(Guid entityId)
        {
            if (entityId == Guid.Empty)
            {
                return;
            }

            _pumps.Remove(entityId);
            _runtime.ReportPumpCount(_pumps.Count);
        }

        // ------------------------------------------------------------------ IMwpPumpQuery

        /// <inheritdoc />
        public bool TryGetSnapshot(Guid entityId, out PumpSnapshot snapshot)
        {
            snapshot = default;
            if (!TryFind(entityId, out MwpPumpController controller))
            {
                return false;
            }

            try
            {
                snapshot = controller.CreateSnapshot();
                return true;
            }
            catch (Exception exception)
            {
                // A broken pump must only blank its own panel, never throw into the UI loop.
                TryLogError("pump snapshot failed; panel hides this pump", exception);
                snapshot = default;
                return false;
            }
        }

        // ------------------------------------------------------------------ IMwpPumpCommands

        /// <inheritdoc />
        public bool RequestMaintenance(Guid entityId)
        {
            return TryFind(entityId, out MwpPumpController controller) && controller.RequestMaintenance();
        }

        /// <inheritdoc />
        public bool SetAutoMaintenance(Guid entityId, bool enabled)
        {
            return TryFind(entityId, out MwpPumpController controller) && controller.SetAutoMaintenance(enabled);
        }

        /// <inheritdoc />
        public bool SetPaused(Guid entityId, bool paused)
        {
            return TryFind(entityId, out MwpPumpController controller) && controller.SetPaused(paused);
        }

        // ------------------------------------------------------------------ helpers

        private bool TryFind(Guid entityId, out MwpPumpController controller)
        {
            if (entityId != Guid.Empty
                && _pumps.TryGetValue(entityId, out MwpPumpController? found)
                && found != null)
            {
                controller = found;
                return true;
            }

            controller = null!;
            return false;
        }

        private void TryLogWarn(string message)
        {
            try
            {
                _runtime.Log.Warn(LogKeyQuery, message);
            }
            catch (Exception)
            {
                // Diagnostics only.
            }
        }

        private void TryLogError(string message, Exception exception)
        {
            try
            {
                _runtime.Log.Error(LogKeyQuery, message + " (" + exception.GetType().Name + ")");
            }
            catch (Exception)
            {
                // Diagnostics only.
            }
        }
    }
}
