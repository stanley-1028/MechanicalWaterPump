using System;
using MWP.Core;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace MWP.Gameplay.Components
{
    /// <summary>
    /// Partial part: the per-pump save bridge (CONTRACT §5, chapter 14, REQ-SAVE-01/02/03/05).
    ///
    /// The game already serializes components transactionally as part of the world save, so MWP
    /// writes one component named "MWP" on its own pump entity instead of adding a second save
    /// pipeline. That keeps entity identity and load order in the game's hands and confines MWP
    /// damage to MWP keys; a missing component simply means "a new pump" and a broken one resets
    /// only that pump (REQ-SAVE-03).
    ///
    /// Every field is written through a dedicated primitive <see cref="PropertyKey{T}"/>; the
    /// defensive read helpers exist because <c>IObjectLoader.Get</c> throws when a key is absent
    /// (verified in game IL), so each read is guarded by <c>Has</c> plus its own try/catch.
    /// </summary>
    public sealed partial class MwpPumpController
    {
        /// <inheritdoc />
        public void Save(IEntitySaver entitySaver)
        {
            if (!_awake)
            {
                return;
            }

            try
            {
                IObjectSaver? component = entitySaver.GetComponent(MwpSaveKeys.Component);
                if (component == null)
                {
                    return;
                }

                component.Set(MwpSaveKeys.SchemaVersion, MwpDefaults.SaveSchemaVersion);
                component.Set(MwpSaveKeys.ModVersion, MwpDefaults.ModVersion);
                component.Set(MwpSaveKeys.Tier, _tierName);
                component.Set(MwpSaveKeys.Accumulator, (float)SafeAccumulator());
                component.Set(MwpSaveKeys.Contamination,
                    (float)BalanceFormulas.Sanitize(_contamination, 0.0, 0.0, 1.0));
                component.Set(MwpSaveKeys.Condition,
                    (float)BalanceFormulas.Sanitize(_ledger.Condition, 100.0, 0.0, 100.0));
                component.Set(MwpSaveKeys.AutoMaintenance, _ledger.AutoMaintenance);
                component.Set(MwpSaveKeys.MaintenanceState, _ledger.Phase.ToString());
                component.Set(MwpSaveKeys.MaintenanceRemainingHours,
                    (float)BalanceFormulas.Sanitize(_ledger.RemainingHours, 0.0, 0.0, 720.0));
                component.Set(MwpSaveKeys.State, _state.ToString());
                component.Set(MwpSaveKeys.Paused, _paused);
                component.Set(MwpSaveKeys.PipeAdapterState, _pipeState.ToString());
                component.Set(MwpSaveKeys.WaterWithdrawFailures, _withdrawFailures > 0 ? _withdrawFailures : 0);
            }
            catch (Exception exception)
            {
                // One pump must never abort the world save (REQ-SAVE-01, REQ-SAFE-02/13).
                LogError("pump save failed; the previous data is kept", exception);
            }
        }

        /// <inheritdoc />
        public void Load(IEntityLoader entityLoader)
        {
            if (!_awake)
            {
                return;
            }

            try
            {
                string savedModVersion;
                PumpSaveState? raw = ReadSavedState(entityLoader, out savedModVersion);
                PumpSaveState safe = MwpSaveCodec.Sanitize(raw, _runtime.RawLog, _tierName);
                ApplySavedState(safe);
                _saveLoaded = true;

                if (raw != null)
                {
                    _runtime.Log.Info(MwpLogKeys.SaveMigrated,
                        "pump state loaded (schema " + safe.SchemaVersion + ", modVersion="
                        + (savedModVersion.Length > 0 ? savedModVersion : "unknown") + ")|" + _logContext);
                }
            }
            catch (Exception exception)
            {
                // Keep the safe defaults created in Awake; the pump stays playable (REQ-SAVE-03).
                _saveLoaded = true;
                LogError("pump load failed; safe defaults kept", exception);
            }
        }

        // ------------------------------------------------------------------ read helpers

        private PumpSaveState? ReadSavedState(IEntityLoader loader, out string savedModVersion)
        {
            savedModVersion = string.Empty;
            if (loader == null)
            {
                return null;
            }

            IObjectLoader? component;
            try
            {
                if (!loader.TryGetComponent(MwpSaveKeys.Component, out component) || component == null)
                {
                    // Fresh pump or a save written before MWP existed: "node missing" is not damage.
                    return null;
                }
            }
            catch (Exception exception)
            {
                LogError("pump save component unreadable; safe reset", exception);
                return null;
            }

            bool degraded = false;
            // REQ-SAVE-02: the writer's mod version is diagnostics only. A save without it (older
            // build) is not damage, so the missing-key fallback deliberately does not set degraded.
            savedModVersion = ReadString(component, MwpSaveKeys.ModVersion, string.Empty, ref degraded);
            var state = new PumpSaveState
            {
                SchemaVersion = ReadInt(component, MwpSaveKeys.SchemaVersion, 0, ref degraded),
                Tier = ReadString(component, MwpSaveKeys.Tier, _tierName, ref degraded),
                Accumulator = ReadFloat(component, MwpSaveKeys.Accumulator, 0f, ref degraded),
                Contamination = ReadFloat(component, MwpSaveKeys.Contamination, 0f, ref degraded),
                Condition = ReadFloat(component, MwpSaveKeys.Condition, 100f, ref degraded),
                AutoMaintenance = ReadBool(component, MwpSaveKeys.AutoMaintenance, false, ref degraded),
                MaintenanceState = ReadMaintenancePhase(component, ref degraded),
                MaintenanceRemainingHours = ReadFloat(component, MwpSaveKeys.MaintenanceRemainingHours, 0f, ref degraded),
                State = ReadPumpState(component, ref degraded),
                Paused = ReadBool(component, MwpSaveKeys.Paused, false, ref degraded),
                PipeAdapterState = ReadString(component, MwpSaveKeys.PipeAdapterState,
                    nameof(PipeAdapterState.Unknown), ref degraded),
                ConsecutiveWaterWithdrawFailures = ReadInt(component, MwpSaveKeys.WaterWithdrawFailures, 0, ref degraded)
            };

            if (degraded)
            {
                _runtime.Log.Warn(MwpLogKeys.SaveCorrupt,
                    "pump save had unreadable fields; documented defaults used for them|" + _logContext);
            }

            return state;
        }

        private static int ReadInt(IObjectLoader loader, PropertyKey<int> key, int fallback, ref bool degraded)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) : fallback;
            }
            catch (Exception)
            {
                degraded = true;
                return fallback;
            }
        }

        private static float ReadFloat(IObjectLoader loader, PropertyKey<float> key, float fallback, ref bool degraded)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) : fallback;
            }
            catch (Exception)
            {
                degraded = true;
                return fallback;
            }
        }

        private static bool ReadBool(IObjectLoader loader, PropertyKey<bool> key, bool fallback, ref bool degraded)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) : fallback;
            }
            catch (Exception)
            {
                degraded = true;
                return fallback;
            }
        }

        private static string ReadString(IObjectLoader loader, PropertyKey<string> key, string fallback, ref bool degraded)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) ?? fallback : fallback;
            }
            catch (Exception)
            {
                degraded = true;
                return fallback;
            }
        }

        private static MaintenancePhase ReadMaintenancePhase(IObjectLoader loader, ref bool degraded)
        {
            string text = ReadString(loader, MwpSaveKeys.MaintenanceState, string.Empty, ref degraded);
            if (text.Length > 0 && Enum.TryParse(text, true, out MaintenancePhase phase)
                && Enum.IsDefined(typeof(MaintenancePhase), phase))
            {
                return phase;
            }

            return MaintenancePhase.None;
        }

        private static PumpState ReadPumpState(IObjectLoader loader, ref bool degraded)
        {
            string text = ReadString(loader, MwpSaveKeys.State, string.Empty, ref degraded);
            if (text.Length > 0 && Enum.TryParse(text, true, out PumpState state)
                && Enum.IsDefined(typeof(PumpState), state))
            {
                return state;
            }

            return PumpState.Producing;
        }

        // ------------------------------------------------------------------ apply

        private void ApplySavedState(PumpSaveState state)
        {
            _pendingWater = state.Accumulator;
            _contamination = state.Contamination;
            _ledger.Reset(state.Condition, state.AutoMaintenance, state.MaintenanceState,
                state.MaintenanceRemainingHours);
            _paused = state.Paused;
            _withdrawFailures = state.ConsecutiveWaterWithdrawFailures;
            _state = Enum.IsDefined(typeof(PumpState), state.State) ? state.State : PumpState.Producing;
            _rules = _runtime.RulesFor(_tierId, _ledger.AutoMaintenance);

            // REQ-SAVE-05: the adapter state is rebuilt from the live detection on every load; the
            // saved value is diagnostic only and may describe a mod list that no longer exists.
            _pipeState = ResolvePipeState();
            UpdateConsumption();
        }

        /// <summary>Accumulator is persisted as 0..1 (CONTRACT §5); non-finite values become 0.</summary>
        private double SafeAccumulator()
        {
            double value = _pendingWater;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                return 0.0;
            }

            return value > 1.0 ? 1.0 : value;
        }
    }
}
