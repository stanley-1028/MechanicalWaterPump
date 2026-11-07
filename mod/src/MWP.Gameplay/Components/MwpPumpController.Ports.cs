using System;
using System.Collections.Generic;
using MWP.Core;
using Timberborn.Buildings;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.WaterBuildings;

namespace MWP.Gameplay.Components
{
    /// <summary>
    /// Partial part: the game-component implementations of the frozen Core ports
    /// (<see cref="IPumpPowerSource"/>, <see cref="IWaterSource"/>, <see cref="IMaintenanceStore"/>).
    ///
    /// Keeping the ports as explicit controller members means Core never sees a game type
    /// (REQ-NFR-01/05) while the ledger/state machine stay pure and unit-testable. Every game call
    /// here is defensive: a missing or half-initialised vanilla component degrades to a safe value
    /// instead of throwing into the tick loop (REQ-SAFE-02).
    /// </summary>
    public sealed partial class MwpPumpController
    {
        // ------------------------------------------------------------------ IPumpPowerSource

        /// <inheritdoc />
        public double SupplyHp => _supplyHp;

        /// <inheritdoc />
        public int RequiredHp => _tier.PowerRequiredHp;

        /// <inheritdoc />
        public double Ratio => _powerRatio;

        /// <inheritdoc />
        public bool Powered => _powered;

        // ------------------------------------------------------------------ IWaterSource

        /// <inheritdoc />
        public bool HasWater => _hasWater;

        /// <inheritdoc />
        public double Contamination => _contamination;

        /// <inheritdoc />
        /// <remarks>
        /// Reproduces the vanilla consumption chain exactly (API_FACTS_MWP §3):
        /// <c>WaterInput.DemandCleanWaterAmount</c> asks the water body for the requested volume
        /// (and returns what the input buffer can offer), then <c>RemoveCleanWater</c> actually
        /// removes the granted part. 0.2 m³ per Water item is enforced by the caller.
        /// </remarks>
        public double Withdraw(double cubicMeters)
        {
            if (double.IsNaN(cubicMeters) || double.IsInfinity(cubicMeters) || cubicMeters <= 0.0)
            {
                return 0.0;
            }

            WaterInput? waterInput = _waterInput;
            if (waterInput == null)
            {
                return 0.0;
            }

            try
            {
                float needed = cubicMeters > float.MaxValue ? float.MaxValue : (float)cubicMeters;
                float available = waterInput.DemandCleanWaterAmount(needed);
                if (float.IsNaN(available) || available <= 0f)
                {
                    return 0.0;
                }

                float take = available < needed ? available : needed;
                waterInput.RemoveCleanWater(take);
                return take;
            }
            catch (Exception exception)
            {
                LogWarn("water-body withdrawal failed (" + exception.GetType().Name + ")");
                return 0.0;
            }
        }

        // ------------------------------------------------------------------ IMaintenanceStore

        /// <inheritdoc />
        public bool HasMaterials(IReadOnlyList<GoodStack> materials)
        {
            if (materials == null || materials.Count == 0)
            {
                return true;
            }

            DistrictInventoryRegistry? registry = ResolveInventoryRegistry();
            if (registry == null)
            {
                return false;
            }

            try
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    GoodStack stack = materials[i];
                    if (stack.Amount <= 0)
                    {
                        continue;
                    }

                    int available = CountUnreserved(registry, stack.GoodId);
                    if (available < stack.Amount)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                LogWarn("district inventory query failed (" + exception.GetType().Name + ")");
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryConsume(IReadOnlyList<GoodStack> materials)
        {
            if (materials == null || materials.Count == 0)
            {
                return true;
            }

            DistrictInventoryRegistry? registry = ResolveInventoryRegistry();
            if (registry == null || !HasMaterials(materials))
            {
                // REQ-MAINT-06: missing materials must consume nothing.
                return false;
            }

            try
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    GoodStack stack = materials[i];
                    if (stack.Amount <= 0)
                    {
                        continue;
                    }

                    int remaining = stack.Amount;
                    foreach (Inventory candidate in registry.ActiveInventoriesWithStock(stack.GoodId))
                    {
                        if (candidate == null || ReferenceEquals(candidate, _inventory))
                        {
                            continue;
                        }

                        int take = candidate.UnreservedAmountInStock(stack.GoodId);
                        if (take <= 0)
                        {
                            continue;
                        }

                        if (take > remaining)
                        {
                            take = remaining;
                        }

                        var good = new GoodAmount(stack.GoodId, take);
                        if (!candidate.HasUnreservedStock(good))
                        {
                            continue;
                        }

                        candidate.TakeExported(good);
                        remaining -= take;
                        if (remaining <= 0)
                        {
                            break;
                        }
                    }

                    if (remaining > 0)
                    {
                        // Should be unreachable on the main thread right after the HasMaterials
                        // pre-check; treat it as "no maintenance" and report it once (throttled).
                        LogWarn("maintenance material stock changed during withdrawal");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                LogWarn("maintenance material withdrawal failed (" + exception.GetType().Name + ")");
                return false;
            }
        }

        /// <inheritdoc />
        public bool IsReachable() => ComputeReachable();

        // ------------------------------------------------------------------ reachability / district

        /// <summary>
        /// True when the pump's pickup access lies on the district road network. This is the same
        /// test vanilla uses for haulers (<c>DistrictCenter.AccessibleIsOnDistrictRoad</c>),
        /// verified in IL, so the displayed <see cref="PumpState.NoAccess"/> matches whether water
        /// can actually be hauled out (REQ-CORE-04/REQ-MAINT-06).
        /// </summary>
        private bool ComputeReachable()
        {
            try
            {
                BuildingAccessible? buildingAccessible = _buildingAccessible;
                if (buildingAccessible == null)
                {
                    return false;
                }

                var accessible = buildingAccessible.Accessible;
                if (accessible == null)
                {
                    return false;
                }

                DistrictCenter? districtCenter = ResolveDistrictCenter();
                if (districtCenter == null)
                {
                    return false;
                }

                return districtCenter.AccessibleIsOnDistrictRoad(accessible);
            }
            catch (Exception exception)
            {
                LogWarn("accessibility query failed (" + exception.GetType().Name + ")");
                return false;
            }
        }

        private DistrictCenter? ResolveDistrictCenter()
        {
            DistrictBuilding? districtBuilding = _districtBuilding;
            if (districtBuilding == null)
            {
                return null;
            }

            DistrictCenter? center = districtBuilding.District;
            if (center == null)
            {
                center = districtBuilding.InstantDistrict;
            }

            return center;
        }

        private DistrictInventoryRegistry? ResolveInventoryRegistry()
        {
            try
            {
                DistrictCenter? center = ResolveDistrictCenter();
                return center != null ? center.GetComponent<DistrictInventoryRegistry>() : null;
            }
            catch (Exception exception)
            {
                LogWarn("district inventory registry unavailable (" + exception.GetType().Name + ")");
                return null;
            }
        }

        private int CountUnreserved(DistrictInventoryRegistry registry, string goodId)
        {
            int total = 0;
            foreach (Inventory candidate in registry.ActiveInventoriesWithStock(goodId))
            {
                if (candidate == null || ReferenceEquals(candidate, _inventory))
                {
                    continue;
                }

                total += candidate.UnreservedAmountInStock(goodId);
            }

            return total;
        }
    }
}
