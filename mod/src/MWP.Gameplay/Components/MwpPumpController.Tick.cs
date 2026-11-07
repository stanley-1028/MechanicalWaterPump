using System;
using System.Diagnostics;
using MWP.Core;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.TickSystem;

namespace MWP.Gameplay.Components
{
    /// <summary>
    /// Partial part: the throttled production and maintenance step (CONTRACT §5, chapter 20.2).
    ///
    /// Timing model (verified game API): the game ticks each entity once per fixed game tick, and
    /// <c>IDayNightCycle.FixedDeltaTimeInHours</c> is exactly the amount of game time one tick
    /// represents. The component therefore accumulates those game hours every tick and runs the
    /// real arithmetic only when at least <see cref="MwpRuntime.TickIntervalSeconds"/> of real time
    /// has elapsed, which keeps 100 pumps far inside the performance budget without losing game
    /// time (REQ-PERF-01/02).
    /// </summary>
    public sealed partial class MwpPumpController
    {
        /// <inheritdoc />
        public override void Tick()
        {
            if (!_activated)
            {
                return;
            }

            // REQ-OPS-04 equivalent: timing is measured only when the opt-in diagnostics feature
            // is on, so the default tick path stays allocation- and clock-free.
            bool measure = _runtime.DiagnosticsEnabled;
            long startTimestamp = measure ? Stopwatch.GetTimestamp() : 0L;
            try
            {
                Advance();
            }
            catch (Exception exception)
            {
                // A single damaged pump must not stop the other pumps or the game loop
                // (REQ-SAFE-02); the throttled log bounds the noise.
                LogError("pump tick failed; keeping the previous state", exception);
            }
            finally
            {
                if (measure)
                {
                    _runtime.RecordTick(startTimestamp);
                }
            }
        }

        private void Advance()
        {
            double hoursPerTick = _dayNightCycle.FixedDeltaTimeInHours;
            if (double.IsNaN(hoursPerTick) || double.IsInfinity(hoursPerTick) || hoursPerTick <= 0.0)
            {
                return;
            }

            _stepHours += hoursPerTick;
            _stepSeconds += _tickService.TickIntervalInSeconds;
            if (_stepSeconds < _runtime.TickIntervalSeconds)
            {
                return;
            }

            _stepSeconds = 0.0;
            double hours = _stepHours;
            _stepHours = 0.0;
            if (hours > MaxSingleStepHours)
            {
                // A long hitch is caught up over several future steps instead of producing a burst.
                _stepHours = hours - MaxSingleStepHours;
                hours = MaxSingleStepHours;
            }

            if (hours <= 0.0)
            {
                return;
            }

            RunStep(hours);
        }

        private void RunStep(double hours)
        {
            UpdateObservations();
            UpdateMaintenance(hours);
            EvaluateState();

            if (ProducesWater(_state))
            {
                _pendingWater += _effectiveRate * hours;
                ProduceWater();
                if (_rules.Enabled && _effectiveRate > 0.0)
                {
                    // REQ-MAINT-01: only hours spent producing cause wear. Low-power and
                    // maintenance-warning states still produce, so they still wear.
                    _ledger.AccumulateWear(hours, _rules.IntervalDays);
                }
            }
            else if (_canProduce)
            {
                // Recovery path: the physical intake still has water but the previous withdrawal
                // attempts failed, so the state is shown as NoWater while we retry every step. A
                // successful retry resets the failure counter and restores the real state.
                _pendingWater += _effectiveRate * hours;
                ProduceWater();
            }
            else
            {
                // CONTRACT §5: the accumulator is capped at 1 while production is blocked so a
                // resume after a long stop cannot burst a whole tank at once.
                ClampPendingWater();
            }

            UpdateConsumption();
        }

        // ------------------------------------------------------------------ observations

        private void UpdateObservations()
        {
            if (_mechanical != null)
            {
                _powered = _mechanical.ActiveAndPowered;
                double efficiency = _powered
                    ? BalanceFormulas.Sanitize(_mechanical.Efficiency, 0.0, 0.0, 1.0)
                    : 0.0;
                _powerRatio = BalanceFormulas.PowerRatio(efficiency * _tier.PowerRequiredHp, _tier.PowerRequiredHp);
                _supplyHp = efficiency * _tier.PowerRequiredHp;
            }
            else
            {
                _powered = false;
                _powerRatio = 0.0;
                _supplyHp = 0.0;
            }

            if (_waterInput != null)
            {
                _hasWater = _waterInput.IsUnderwater;
                _contamination = BalanceFormulas.Sanitize(_waterInput.ContaminationPercentage, 0.0, 0.0, 1.0);
                if (!_hasWater && _withdrawFailures != 0)
                {
                    // Water is gone entirely: restart the consecutive-failure window.
                    _withdrawFailures = 0;
                    _withdrawFailureReported = false;
                }
            }
            else
            {
                _hasWater = false;
                _contamination = 0.0;
            }

            _reachable = ComputeReachable();

            Inventory? inventory = ResolveInventory();
            if (inventory != null)
            {
                int free = inventory.UnreservedCapacity(GoodStacks.Water);
                int capacityLeft = _tier.InternalCapacity - inventory.AmountInStock(GoodStacks.Water);
                if (capacityLeft < free)
                {
                    free = capacityLeft;
                }

                _freeCapacity = free > 0 ? free : 0;
            }
            else
            {
                // Not-yet-initialised inventory: report free space so production is attempted; the
                // carry is clamped instead, and the real capacity governs from the next step on.
                _freeCapacity = _tier.InternalCapacity;
            }
        }

        // ------------------------------------------------------------------ maintenance

        private void UpdateMaintenance(double hours)
        {
            _rules = _runtime.RulesFor(_tierId, _ledger.AutoMaintenance);

            if (_ledger.Phase == MaintenancePhase.Maintaining)
            {
                _ledger.AdvanceMaintenance(hours);
            }

            bool manual = _manualMaintenanceRequested;
            if (WantsMaintenance(manual))
            {
                bool hasMaterials = HasMaterials(_rules.Materials);
                MaintenanceDecision decision = _ledger.Decide(
                    new MaintenanceContext(hasMaterials, _reachable, manual), _rules);
                _manualMaintenanceRequested = false;
                ApplyMaintenanceDecision(decision);
                return;
            }

            // Cheap path: no maintenance is wanted, so the district inventory is not queried. The
            // ledger still runs so a disabled system resets itself (REQ-MAINT-13).
            MaintenanceDecision idle = _ledger.Decide(new MaintenanceContext(true, true, false), _rules);
            ApplyMaintenanceDecision(idle);
        }

        /// <summary>
        /// Mirrors <see cref="MaintenanceLedger.Decide"/>'s trigger conditions so expensive district
        /// lookups only happen when a decision could actually start maintenance.
        /// </summary>
        private bool WantsMaintenance(bool manual)
        {
            if (!_rules.Enabled)
            {
                return false;
            }

            if (manual || _ledger.Phase == MaintenancePhase.MissingMaterials
                || _ledger.Phase == MaintenancePhase.NoAccess)
            {
                return true;
            }

            return _rules.Auto && _ledger.AutoMaintenance
                && _ledger.Condition <= _rules.AutoThreshold;
        }

        private void ApplyMaintenanceDecision(in MaintenanceDecision decision)
        {
            MaintenanceAction action = decision.Action;
            bool transition = action != _lastMaintenanceAction;
            _lastMaintenanceAction = action;

            switch (action)
            {
                case MaintenanceAction.Start:
                    if (TryConsume(_rules.Materials))
                    {
                        _ledger.BeginMaintenance(_rules);
                        _runtime.Log.Info(MwpLogKeys.MaintenanceStarted,
                            "maintenance started|" + _logContext);
                    }

                    break;

                case MaintenanceAction.Completed:
                    _ledger.CompleteMaintenance();
                    _runtime.Log.Info(MwpLogKeys.MaintenanceFinished,
                        "maintenance finished; condition restored|" + _logContext);
                    UpdateConsumption();
                    break;

                case MaintenanceAction.BlockedByMaterials:
                    if (transition)
                    {
                        _runtime.Log.Warn(MwpLogKeys.MaintenanceBlocked,
                            "maintenance blocked: missing materials|" + _logContext);
                    }

                    break;

                case MaintenanceAction.BlockedByAccess:
                    if (transition)
                    {
                        _runtime.Log.Warn(MwpLogKeys.MaintenanceBlocked,
                            "maintenance blocked: no road for materials|" + _logContext);
                    }

                    break;
            }
        }

        // ------------------------------------------------------------------ state

        private void EvaluateState()
        {
            // Pass the physical intake state (not the failure-suppressed one): RunStep needs the
            // real production capability so it can keep retrying while the panel shows NoWater.
            bool hasWater = _hasWater;
            var input = new PumpStatusInput(
                isFinished: true,
                isPaused: _paused,
                powered: _powered,
                supplyHp: _supplyHp,
                requiredHp: _tier.PowerRequiredHp,
                hasWater: hasWater,
                freeCapacity: _freeCapacity,
                isAccessible: _reachable,
                maintenancePhase: _ledger.Phase,
                condition: _ledger.Condition);

            PumpStatus status = PumpStateMachine.Evaluate(
                in input,
                _tier,
                _contamination,
                _runtime.BadwaterPenalty,
                _runtime.MinWaterQuality);

            _state = status.State;
            _powerRatio = status.PowerRatio;
            _quality = status.WaterQuality;
            _effectiveRate = status.EffectiveWaterPerHour;

            // Physical intake is fine, but the water body could not supply the last
            // MaxConsecutiveWaterWithdrawFailures withdrawals: report NoWater (CONTRACT §5/§11.4)
            // while RunStep keeps retrying, so the pump recovers by itself when water returns.
            _canProduce = ProducesWater(_state);
            if (_canProduce && _hasWater
                && _withdrawFailures > MwpDefaults.MaxConsecutiveWaterWithdrawFailures)
            {
                _state = PumpState.NoWater;
            }
        }

        /// <summary>
        /// True for the states that still yield water; kept in sync with
        /// <see cref="PumpStateMachine"/>'s internal production set (maintenance warnings still
        /// produce until condition reaches zero, REQ-MAINT-09).
        /// </summary>
        private static bool ProducesWater(PumpState state)
        {
            return state == PumpState.Producing
                || state == PumpState.LowPower
                || state == PumpState.MaintenanceDue
                || state == PumpState.Critical
                || state == PumpState.MissingMaintenanceMaterials
                || state == PumpState.NoAccessForMaintenance;
        }

        // ------------------------------------------------------------------ production

        private void ProduceWater()
        {
            Inventory? inventory = ResolveInventory();
            if (inventory == null)
            {
                ClampPendingWater();
                return;
            }

            int free = inventory.UnreservedCapacity(GoodStacks.Water);
            int capacityLeft = _tier.InternalCapacity - inventory.AmountInStock(GoodStacks.Water);
            if (capacityLeft < free)
            {
                free = capacityLeft;
            }

            if (free <= 0)
            {
                return;
            }

            int items = BalanceFormulas.WaterItemsProduced(_pendingWater, free);
            if (items <= 0)
            {
                return;
            }

            double requested = BalanceFormulas.CubicMetersFor(items, _cubicMetersPerWater);
            double withdrawn = Withdraw(requested);
            int granted = GrantedItems(withdrawn, items);

            if (granted > 0)
            {
                _pendingWater -= granted;
                if (_pendingWater < 0.0)
                {
                    _pendingWater = 0.0;
                }

                inventory.GiveProduced(new GoodAmount(GoodStacks.Water, granted));
                _withdrawFailures = 0;
                _withdrawFailureReported = false;
            }

            if (granted < items)
            {
                _withdrawFailures++;
                if (!_withdrawFailureReported)
                {
                    // Report the first failure only (plus the state change to NoWater): this runs
                    // in the per-pump tick hot path, so no message is built on later attempts.
                    _withdrawFailureReported = true;
                    _runtime.Log.Warn(MwpLogKeys.WaterWithdrawFailed,
                        "water body could not supply the requested volume|" + _logContext);
                }

                if (granted == 0)
                {
                    // Nothing could be produced from the carry: prevent an unbounded burst later.
                    ClampPendingWater();
                }
            }
        }

        private int GrantedItems(double withdrawn, int requested)
        {
            if (double.IsNaN(withdrawn) || withdrawn <= 0.0 || requested <= 0 || _cubicMetersPerWater <= 0.0)
            {
                return 0;
            }

            double items = withdrawn / _cubicMetersPerWater;
            if (items >= requested)
            {
                return requested;
            }

            // Tiny epsilon so exact multiples (1.0 m³ for 5 Water) do not lose one item to
            // floating point division (0.2 is not representable in binary).
            int granted = (int)Math.Floor(items + 1e-9);
            return granted > 0 ? granted : 0;
        }

        // ------------------------------------------------------------------ power consumption

        private void UpdateConsumption()
        {
            bool disabled = _paused
                || _ledger.Phase == MaintenancePhase.Maintaining
                || _state == PumpState.NeedsMaintenance
                || _ledger.Condition <= 0.0;

            if (disabled == _consumptionDisabled)
            {
                return;
            }

            _consumptionDisabled = disabled;
            if (_mechanical == null)
            {
                return;
            }

            try
            {
                // CONTRACT §5 / §20.2.3: pausing or maintaining this pump stops its power draw
                // without touching the shared mechanical graph (REQ-IMPL-05).
                _mechanical.SetConsumptionDisabled(disabled);
            }
            catch (Exception exception)
            {
                LogWarn("could not change power consumption (" + exception.GetType().Name + ")");
            }
        }

        private void RestoreConsumptionEnabled()
        {
            if (!_consumptionDisabled)
            {
                return;
            }

            _consumptionDisabled = false;
            if (_mechanical == null)
            {
                return;
            }

            try
            {
                _mechanical.SetConsumptionDisabled(false);
            }
            catch (Exception)
            {
                // The node is being torn down; nothing else to restore.
            }
        }
    }
}
