using System;
using MWP.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.CoreUI;
using Timberborn.EntityPanelSystem;
using Timberborn.EntitySystem;
using Timberborn.Localization;
using Timberborn.SimpleOutputBuildingsUI;
using Timberborn.WaterBuildings;
using UnityEngine;
using UnityEngine.UIElements;

namespace MWP.UI.Panels
{
    /// <summary>
    /// Entity panel fragment for the four pump tiers (CONTRACT §10, REQ-UI-01/02/03/07,
    /// REQ-MAINT-10).
    ///
    /// The fragment is authored in code with the game's own UIElements controls and USS classes
    /// (see <see cref="MwpPumpPanelStyles"/>), so it looks like the native entity-panel fragments
    /// without shipping a UXML/USS asset. It is added to the vanilla panel through the game's own
    /// <see cref="EntityPanelModule"/> multi-binding (see <see cref="MwpEntityPanelModuleProvider"/>).
    ///
    /// Behaviour:
    /// * invisible for every entity that is not a finished pump, so it adds no rows elsewhere;
    /// * reads the read model through <see cref="IMwpPumpQuery"/> only (never a gameplay type);
    /// * re-queries at most <see cref="MwpDefaults.UiRefreshHz"/> times per second and re-renders
    ///   only when the value fingerprint changed (dirty marker, REQ-PERF-04);
    /// * never throws: a failing query hides the fragment instead of breaking the panel;
    /// * commands are bound to <see cref="IMwpPumpCommands"/> and carry this pump's entity id only.
    ///
    /// Power, internal Water stock and intake depth are rendered by the game's own
    /// <c>MechanicalNodeFragment</c>, <c>SimpleOutputInventoryFragment</c> and
    /// <c>WaterInputPipeDepthFragment</c>, so the pump panel only keeps a fallback row for the
    /// stock/depth when the matching vanilla component is missing.
    /// </summary>
    public sealed class MwpPumpPanelFragment : IEntityPanelFragment
    {
        /// <summary>Refresh cap: 1/4 s, matching <see cref="MwpDefaults.UiRefreshHz"/>.</summary>
        private const float RefreshIntervalSeconds = 1f / MwpDefaults.UiRefreshHz;

        private readonly IMwpPumpQuery _query;
        private readonly IMwpPumpCommands _commands;
        private readonly ILoc _loc;
        private readonly VisualElementLoader _visualElementLoader;

        private MwpPumpPanelView? _view;
        private BaseComponent? _entity;
        private Guid _entityId;
        private long _lastFingerprint;
        private bool _hasSnapshot;
        private bool _forceNextRender;
        private bool _lastPaused;
        private bool _showInventoryRow;
        private bool _showDepthRow;
        private float _nextRefreshTime;

        /// <summary>Creates the fragment; Bindito injects the query, command and localization services.</summary>
        /// <param name="query">Read-only pump query registered by the gameplay assembly.</param>
        /// <param name="commands">Per-pump command surface registered by the gameplay assembly.</param>
        /// <param name="loc">Localization service.</param>
        /// <param name="visualElementLoader">Game service used to take a native button from a vanilla UXML template.</param>
        public MwpPumpPanelFragment(IMwpPumpQuery query, IMwpPumpCommands commands, ILoc loc,
            VisualElementLoader visualElementLoader)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _visualElementLoader = visualElementLoader ?? throw new ArgumentNullException(nameof(visualElementLoader));
        }

        /// <inheritdoc />
        public VisualElement InitializeFragment()
        {
            _view = new MwpPumpPanelView(_loc, _visualElementLoader, OnMaintainRequested,
                OnAutoMaintenanceChanged, OnPauseToggled);
            return _view.Root;
        }

        /// <inheritdoc />
        public void ShowFragment(BaseComponent entity)
        {
            _entity = entity;
            _entityId = ResolveEntityId(entity);
            ResolveNativeSections(entity);
            _hasSnapshot = false;
            _forceNextRender = true;
            _lastFingerprint = 0L;
            _nextRefreshTime = 0f;
            Refresh(force: true);
        }

        /// <summary>
        /// Detects whether the vanilla fragments will render the inventory stock and the intake
        /// depth for this entity. Both are shown by the game's own bottom/top fragments; our rows
        /// exist only as a fallback for the (unexpected) case where the enabling component is
        /// missing, so the player never loses information.
        /// </summary>
        private void ResolveNativeSections(BaseComponent entity)
        {
            _showInventoryRow = entity.GetComponent<SimpleOutputInventoryFragmentEnabler>() == null;
            _showDepthRow = entity.GetComponent<WaterInputPipeCoordinates>() == null;
        }

        /// <inheritdoc />
        public void UpdateFragment()
        {
            // The panel system calls this every frame; keep the 4 Hz cap and the early exit for a
            // closed panel (ClearFragment drops the entity, so a hidden panel does no work at all).
            if (_view is null || _entity is null || _entityId == Guid.Empty)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = now + RefreshIntervalSeconds;
            Refresh(force: _forceNextRender);
        }

        /// <inheritdoc />
        public void ClearFragment()
        {
            _entity = null;
            _entityId = Guid.Empty;
            _hasSnapshot = false;
            _forceNextRender = false;
            _lastFingerprint = 0L;
            _view?.SetVisible(false);
        }

        private void OnMaintainRequested()
        {
            if (_entityId == Guid.Empty) { return; }

            _commands.RequestMaintenance(_entityId);
            RequestQuickResync();
        }

        private void OnAutoMaintenanceChanged(bool enabled)
        {
            if (_entityId == Guid.Empty) { return; }

            _commands.SetAutoMaintenance(_entityId, enabled);
            RequestQuickResync();
        }

        private void OnPauseToggled()
        {
            if (_entityId == Guid.Empty) { return; }

            _commands.SetPaused(_entityId, !_lastPaused);
            RequestQuickResync();
        }

        /// <summary>Lets the next frame re-read the snapshot so a button click is reflected immediately.</summary>
        private void RequestQuickResync()
        {
            _forceNextRender = true;
            _nextRefreshTime = 0f;
        }

        private void Refresh(bool force)
        {
            MwpPumpPanelView? view = _view;
            if (view is null)
            {
                return;
            }

            if (_entity is null || _entityId == Guid.Empty)
            {
                Hide(view);
                return;
            }

            PumpSnapshot snapshot;
            try
            {
                if (!_query.TryGetSnapshot(_entityId, out snapshot))
                {
                    Hide(view);
                    return;
                }
            }
            catch (Exception)
            {
                // A broken pump or adapter must never break the entity panel (REQ-SAFE-01).
                Hide(view);
                return;
            }

            long fingerprint = ComputeFingerprint(in snapshot);
            if (!force && _hasSnapshot && fingerprint == _lastFingerprint)
            {
                return;
            }

            _lastFingerprint = fingerprint;
            _hasSnapshot = true;
            _forceNextRender = false;
            MwpPumpPanelData data = MwpPumpPanelDataFactory.Create(in snapshot, _loc, _showInventoryRow, _showDepthRow);
            if (!data.HasData)
            {
                Hide(view);
                return;
            }

            _lastPaused = data.Paused;
            view.Render(in data);
        }

        private void Hide(MwpPumpPanelView view)
        {
            _hasSnapshot = false;
            _forceNextRender = false;
            view.SetVisible(false);
        }

        private static Guid ResolveEntityId(BaseComponent entity)
        {
            var components = entity.AllComponents;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] is EntityComponent entityComponent)
                {
                    return entityComponent.EntityId;
                }
            }

            return Guid.Empty;
        }

        /// <summary>
        /// Cheap value fingerprint of every displayed quantity. It only has to change whenever a
        /// rendered value changes, so a collision just costs one skipped refresh (REQ-PERF-04).
        /// Each component is rounded exactly like the formatter rounds it (percentages whole,
        /// rates/days one decimal), otherwise a displayed change could hide inside the
        /// fingerprint's coarser grid.
        /// </summary>
        private static long ComputeFingerprint(in PumpSnapshot snapshot)
        {
            unchecked
            {
                long hash = 17L;
                hash = hash * 31 + (int)snapshot.Tier;
                hash = hash * 31 + (int)snapshot.State;
                hash = hash * 31 + RoundTo(snapshot.EffectiveWaterPerHour, 10.0);
                hash = hash * 31 + RoundTo(Clamp01(snapshot.WaterQuality), 100.0);
                hash = hash * 31 + RoundTo(Clamp100(snapshot.Condition), 1.0);
                hash = hash * 31 + (snapshot.AutoMaintenance ? 1 : 0);
                hash = hash * 31 + (snapshot.MaintenanceEnabled ? 1 : 0);
                hash = hash * 31 + RoundTo(Clamp01(snapshot.MaintenanceProgress), 100.0);
                hash = hash * 31 + RoundTo(snapshot.MaintenanceDaysLeft, 10.0);
                hash = hash * 31 + snapshot.WaterInStock;
                hash = hash * 31 + snapshot.WaterCapacity;
                hash = hash * 31 + (int)snapshot.PipeAdapter;
                hash = hash * 31 + (snapshot.Paused ? 1 : 0);
                hash = hash * 31 + (snapshot.IsFinished ? 1 : 0);
                return hash;
            }
        }

        private static long RoundTo(double value, double scale)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0L;
            }

            return (long)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }

            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private static double Clamp100(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }

            return Math.Max(0.0, Math.Min(100.0, value));
        }
    }
}
