using System;
using MWP.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Buildings;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.InventorySystem;
using Timberborn.MechanicalSystem;
using Timberborn.SimpleOutputBuildings;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WaterBuildings;
using Timberborn.WorldPersistence;

namespace MWP.Gameplay.Components
{
    /// <summary>
    /// The single runtime component of all four Mechanical Water Pump tiers (CONTRACT §5).
    ///
    /// One controller serves every tier: the blueprint's <see cref="MechanicalWaterPumpSpec"/>
    /// selects the tier and the config-resolved tier table supplies every balance number, so there
    /// is no per-tier code path (REQ-NFR-04). Responsibilities:
    /// <list type="bullet">
    /// <item>bind the spec and the vanilla components (mechanical node, water intake, output
    /// inventory, building accessibility, district membership) on <c>Awake</c>;</item>
    /// <item>run one throttled production/maintenance step from <c>Tick</c> (CONTRACT §5);</item>
    /// <item>persist through <see cref="IPersistentEntity"/> under the "MWP" component key;</item>
    /// <item>expose a read-only <see cref="PumpSnapshot"/> to the entity panel and accept the three
    /// per-pump commands (REQ-UI-07);</item>
    /// <item>never let an exception escape into the game loop (REQ-SAFE-02).</item>
    /// </list>
    ///
    /// Performance shape: <c>Tick</c> is O(1) and allocation-free in steady state; it only adds the
    /// elapsed game hours to a pending counter and runs the real step once per
    /// <see cref="MwpRuntime.TickIntervalSeconds"/> (REQ-PERF-01/02).
    /// </summary>
    public sealed partial class MwpPumpController : TickableComponent, IAwakableComponent,
        IFinishedStateListener, IPersistentEntity, IPostLoadableEntity, IDeletableEntity,
        IPumpPowerSource, IWaterSource, IMaintenanceStore
    {
        /// <summary>Gameplay-local log key for a caught per-pump tick failure (REQ-SAFE-02).</summary>
        private const string LogKeyTick = "MWP/Error/Tick";

        /// <summary>Gameplay-local log key for pump lifecycle failures.</summary>
        private const string LogKeyEntity = "MWP/Error/Entity";

        /// <summary>Largest amount of game time one step may consume (a long hitch is caught up over several steps).</summary>
        private const double MaxSingleStepHours = 1.0;

        /// <summary>Defensive upper bound for a tier capacity read from a blueprint/config.</summary>
        private const int MaxInternalCapacity = 100000;

        private readonly MwpRuntime _runtime;
        private readonly IMwpPumpRegistry _registry;
        private readonly IPipeAdapter _pipeAdapter;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly ITickService _tickService;

        private readonly MaintenanceLedger _ledger = new MaintenanceLedger();

        private MechanicalWaterPumpSpec? _spec;
        private BlockObject? _blockObject;
        private EntityComponent? _entityComponent;
        private MechanicalBuilding? _mechanical;
        private WaterInput? _waterInput;
        private SimpleOutputInventory? _output;
        private Inventory? _inventory;
        private BuildingAccessible? _buildingAccessible;
        private DistrictBuilding? _districtBuilding;

        private TierDefinition _tier = TierCatalog.Default[0];
        private PumpTier _tierId;
        private string _tierName = "Basic";
        private MaintenanceRules _rules;
        private string _logContext = "Basic|unknown";

        private Guid _entityId;
        private bool _awake;
        private bool _activated;
        private bool _saveLoaded;
        private bool _paused;
        private bool _manualMaintenanceRequested;
        private bool _consumptionDisabled;
        private bool _powered;
        private bool _hasWater;
        private bool _reachable;
        private bool _canProduce;
        private bool _withdrawFailureReported;

        private double _pendingWater;
        private double _stepHours;
        private double _stepSeconds;
        private double _contamination;
        private double _powerRatio;
        private double _supplyHp;
        private double _effectiveRate;
        private double _quality;
        private double _cubicMetersPerWater = MwpDefaults.CubicMetersPerWater;
        private int _freeCapacity;
        private int _withdrawFailures;
        private PumpState _state = PumpState.Unfinished;
        private PipeAdapterState _pipeState = PipeAdapterState.Unknown;

        /// <summary>Last maintenance action, so blocked states log on transition instead of every step.</summary>
        private MaintenanceAction _lastMaintenanceAction = MaintenanceAction.None;

        /// <summary>
        /// Creates the controller. Every dependency is a DI singleton or a game singleton service
        /// (each DI-bound type has exactly one constructor, API_FACTS_MWP §6); the transient
        /// controller itself is created once per pump entity by the template decorator.
        /// </summary>
        /// <param name="runtime">Config/log singleton owned by this assembly.</param>
        /// <param name="registry">Pump registry that answers UI queries and commands.</param>
        /// <param name="pipeAdapter">Optional Timber Pipes adapter bound by MWP.Integration.</param>
        /// <param name="dayNightCycle">Game clock; supplies game hours per tick.</param>
        /// <param name="tickService">Game tick length; used only for the 1 s throttle.</param>
        public MwpPumpController(
            MwpRuntime runtime,
            IMwpPumpRegistry registry,
            IPipeAdapter pipeAdapter,
            IDayNightCycle dayNightCycle,
            ITickService tickService)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _pipeAdapter = pipeAdapter;
            _dayNightCycle = dayNightCycle ?? throw new ArgumentNullException(nameof(dayNightCycle));
            _tickService = tickService ?? throw new ArgumentNullException(nameof(tickService));
        }

        /// <summary>Entity id used as the save/query identity (Guid, verified game API).</summary>
        public Guid EntityId => _entityId;

        /// <summary>Current highest-priority state; exposed for diagnostics and tests.</summary>
        public PumpState State => _state;

        // ------------------------------------------------------------------ lifecycle

        /// <inheritdoc />
        public void Awake()
        {
            try
            {
                _spec = GetComponent<MechanicalWaterPumpSpec>();
                _blockObject = GetComponent<BlockObject>();
                _mechanical = HasComponent<MechanicalBuilding>() ? GetComponent<MechanicalBuilding>() : null;
                _waterInput = HasComponent<WaterInput>() ? GetComponent<WaterInput>() : null;
                _output = HasComponent<SimpleOutputInventory>() ? GetComponent<SimpleOutputInventory>() : null;
                _buildingAccessible = HasComponent<BuildingAccessible>() ? GetComponent<BuildingAccessible>() : null;
                _districtBuilding = HasComponent<DistrictBuilding>() ? GetComponent<DistrictBuilding>() : null;

                ResolveTier();
                _ledger.Reset(100.0, true, MaintenancePhase.None, 0.0);
                _rules = _runtime.RulesFor(_tierId, _ledger.AutoMaintenance);

                // Nothing may run before the building is finished (same lifecycle as vanilla
                // mechanical buildings): no ticks, no production, no maintenance.
                DisableComponent();
                _awake = true;
            }
            catch (Exception exception)
            {
                _awake = false;
                DisableComponent();
                LogError("pump failed to initialise; component disabled", exception);
            }
        }

        /// <inheritdoc />
        public void OnEnterFinishedState()
        {
            Activate();
        }

        /// <inheritdoc />
        public void OnExitFinishedState()
        {
            Deactivate();
        }

        /// <inheritdoc />
        public void PostLoadEntity()
        {
            // Loaded finished buildings never receive OnEnterFinishedState again.
            Activate();
        }

        /// <inheritdoc />
        public void DeleteEntity()
        {
            try
            {
                Deactivate();
            }
            catch (Exception exception)
            {
                LogError("pump delete failed", exception);
            }
        }

        // ------------------------------------------------------------------ queries / commands

        /// <summary>
        /// Builds the immutable read model used by the entity panel (REQ-UI-01). It only reads
        /// cached fields plus two cheap inventory counters, so a 4 Hz refresh is allocation-free.
        /// </summary>
        /// <returns>The current snapshot; never throws.</returns>
        public PumpSnapshot CreateSnapshot()
        {
            int stock = 0;
            int capacity = _tier.InternalCapacity;
            Inventory? inventory = _inventory;
            if (inventory != null)
            {
                try
                {
                    stock = inventory.AmountInStock(GoodStacks.Water);
                    // SimpleOutputInventory is created with UNLIMITED capacity and its real
                    // cap comes from the allowed-goods table, so Inventory.Capacity is
                    // int.MaxValue. The panel must show the tier's design capacity instead
                    // (BUG-02, found in the first in-game screenshots).
                    capacity = _tier.InternalCapacity;
                }
                catch (Exception)
                {
                    // A damaged inventory only costs the two displayed counters.
                }
            }

            double daysLeft = 0.0;
            if (_rules.Enabled && _ledger.Phase != MaintenancePhase.Maintaining && _ledger.Condition > 0.0)
            {
                daysLeft = BalanceFormulas.MaintenanceDaysLeft(_ledger.Condition, _rules.IntervalDays);
            }

            return new PumpSnapshot(
                _tierId,
                _state,
                _effectiveRate,
                _powerRatio,
                _supplyHp,
                _tier.PowerRequiredHp,
                _quality,
                _ledger.Condition,
                _ledger.AutoMaintenance,
                daysLeft,
                _rules.Enabled,
                MaintenanceProgress(),
                stock,
                capacity,
                _pipeState,
                _paused,
                _activated);
        }

        /// <summary>
        /// Downtime progress for the "Maintaining: {0}%" row: 0 outside maintenance, and clamped to
        /// 0..1 while the timer runs (a zero-length configured downtime reads as complete).
        /// </summary>
        private double MaintenanceProgress()
        {
            if (_ledger.Phase != MaintenancePhase.Maintaining)
            {
                return 0.0;
            }

            double duration = _rules.DurationHours;
            if (double.IsNaN(duration) || duration <= 0.0)
            {
                return 1.0;
            }

            double progress = 1.0 - (_ledger.RemainingHours / duration);
            if (double.IsNaN(progress))
            {
                return 0.0;
            }

            if (progress < 0.0)
            {
                return 0.0;
            }

            return progress > 1.0 ? 1.0 : progress;
        }

        /// <summary>Accepts "maintain now" for this pump only (REQ-MAINT-05/REQ-UI-07).</summary>
        /// <returns>True when the request was recorded; materials are re-checked on the next step.</returns>
        internal bool RequestMaintenance()
        {
            if (!_activated)
            {
                return false;
            }

            _manualMaintenanceRequested = true;
            return true;
        }

        /// <summary>Enables or disables automatic maintenance for this pump only (REQ-MAINT-05).</summary>
        /// <param name="enabled">New value of the per-pump flag.</param>
        /// <returns>True when accepted.</returns>
        internal bool SetAutoMaintenance(bool enabled)
        {
            if (!_activated)
            {
                return false;
            }

            _ledger.AutoMaintenance = enabled;
            return true;
        }

        /// <summary>Pauses or resumes this pump only; the effect is immediate (REQ-UI-07).</summary>
        /// <param name="paused">True to pause, false to resume.</param>
        /// <returns>True when accepted.</returns>
        internal bool SetPaused(bool paused)
        {
            if (!_activated)
            {
                return false;
            }

            if (_paused == paused)
            {
                return true;
            }

            _paused = paused;
            if (paused)
            {
                ClampPendingWater();
            }

            UpdateConsumption();
            return true;
        }

        // ------------------------------------------------------------------ activation

        private void Activate()
        {
            if (_activated || !_awake || _blockObject == null || !_blockObject.IsFinished)
            {
                return;
            }

            try
            {
                ResolveEntityId();
                _inventory = ResolveInventory();
                _pipeState = ResolvePipeState();
                _runtime.ReportAdapterState(_pipeState);
                _logContext = _tierName + "|" + _entityId.ToString("N");
                _rules = _runtime.RulesFor(_tierId, _ledger.AutoMaintenance);

                _registry.Register(_entityId, this);
                EnableComponent();
                _activated = true;
                if (_state == PumpState.Unfinished)
                {
                    // Construction just finished: report a live state until the first step runs
                    // instead of showing "unfinished" on a finished building for one tick.
                    _state = PumpState.Producing;
                }

                UpdateConsumption();

                if (!_saveLoaded)
                {
                    _runtime.Log.Info(MwpLogKeys.PumpCreated,
                        "pump active: tier=" + _tierName + " entity=" + _entityId.ToString("N"));
                }

                // TEMPORARY (2026-11-07): dump the instantiated model hierarchy once per pump so
                // the empty-interior report on Mk III/IV can be diagnosed from Player.log.
                LogModelHierarchyOnce();
            }
            catch (Exception exception)
            {
                _activated = false;
                try
                {
                    _registry.Unregister(_entityId);
                }
                catch (Exception)
                {
                    // The registry never throws for a missing key; ignore defensively.
                }

                DisableComponent();
                LogError("pump activation failed; component disabled", exception);
            }
        }

        private void Deactivate()
        {
            if (!_activated)
            {
                return;
            }

            _activated = false;
            try
            {
                _registry.Unregister(_entityId);
            }
            catch (Exception)
            {
                // A stale registry entry would only make the panel query return false.
            }

            RestoreConsumptionEnabled();
            DisableComponent();
        }

        // ------------------------------------------------------------------ helpers

        private void ResolveTier()
        {
            TierDefinition blueprintTier = _runtime.TierFor(_spec != null ? _spec.Tier : null);
            _tierId = blueprintTier.Tier;
            _tierName = blueprintTier.TierName;
            _tier = _runtime.ResolveTier(_tierId);
            _cubicMetersPerWater = _runtime.CubicMetersPerWater;

            if (_tier.InternalCapacity < 0 || _tier.InternalCapacity > MaxInternalCapacity)
            {
                // A corrupt config must not create an unbound inventory check; fall back to blueprint.
                _tier = blueprintTier;
            }

            if (_spec != null && _runtime.Log != null)
            {
                if (Math.Abs(_spec.WaterPerHour - _tier.WaterPerHour) > 0.01
                    || _spec.PowerRequiredHp != _tier.PowerRequiredHp
                    || _spec.InternalCapacity != _tier.InternalCapacity)
                {
                    _runtime.Log.Warn(MwpLogKeys.ConfigInvalid,
                        "blueprint spec differs from the resolved tier table; the config tier table wins (tier="
                        + _tierName + ")");
                }
            }
        }

        private void ResolveEntityId()
        {
            if (_entityComponent == null && HasComponent<EntityComponent>())
            {
                _entityComponent = GetComponent<EntityComponent>();
            }

            _entityId = _entityComponent != null ? _entityComponent.EntityId : Guid.Empty;
        }

        private Inventory? ResolveInventory()
        {
            if (_inventory != null)
            {
                return _inventory;
            }

            if (_output == null)
            {
                return null;
            }

            try
            {
                _inventory = _output.Inventory;
            }
            catch (Exception exception)
            {
                // The dedicated decorator may not have initialised the inventory yet; retry later.
                LogWarn("output inventory not available yet (" + exception.GetType().Name + ")");
            }

            return _inventory;
        }

        private PipeAdapterState ResolvePipeState()
        {
            try
            {
                if (_pipeAdapter == null)
                {
                    return PipeAdapterState.Unknown;
                }

                PipeAdapterState state = _pipeAdapter.GetState(_tierName);
                return Enum.IsDefined(typeof(PipeAdapterState), state) ? state : PipeAdapterState.Unknown;
            }
            catch (Exception exception)
            {
                // REQ-COMPAT-03: an adapter failure only degrades the optional feature.
                LogWarn("pipe adapter query failed (" + exception.GetType().Name + "); degraded");
                return PipeAdapterState.Error;
            }
        }

        private void ClampPendingWater()
        {
            if (double.IsNaN(_pendingWater) || _pendingWater < 0.0)
            {
                _pendingWater = 0.0;
            }
            else if (_pendingWater > 1.0)
            {
                _pendingWater = 1.0;
            }
        }

        private void LogError(string message, Exception exception)
        {
            try
            {
                _runtime.Log.Error(LogKeyEntity, message + " (" + exception.GetType().Name + ")|" + _logContext);
            }
            catch (Exception)
            {
                // Diagnostics must not escape (REQ-SAFE-02).
            }
        }

        private void LogWarn(string message)
        {
            try
            {
                _runtime.Log.Warn(LogKeyEntity, message + "|" + _logContext);
            }
            catch (Exception)
            {
                // Diagnostics must not escape.
            }
        }
    }
}
