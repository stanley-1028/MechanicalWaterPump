using System.Collections.Immutable;
using System.Collections.Generic;
using MWP.Core;
using Timberborn.BlueprintSystem;
using Timberborn.Goods;

namespace MWP
{
    /// <summary>
    /// The one custom blueprint spec of the mod (CONTRACT §3, REQ-IMPL-07): every per-tier
    /// difference of the four Mechanical Water Pump tiers is blueprint data, so one controller
    /// serves all tiers and balancing a tier never changes code (REQ-NFR-04).
    ///
    /// Blueprint JSON key: <c>"MWP.MechanicalWaterPumpSpec"</c>. The game's
    /// <c>SpecTypeCache.GetSpecEntries</c> indexes a spec type by each <see cref="SpecAliasAttribute"/>
    /// string plus the CLR <c>Type.Name</c> only — never by namespace-qualified name — so the alias
    /// below is what makes the dotted JSON key resolve (verified in game IL; without it the
    /// blueprint silently has no pump spec and the building fails to instantiate).
    ///
    /// <c>MaintenanceMaterials</c> is an <see cref="ImmutableArray{T}"/> of vanilla
    /// <see cref="GoodAmountSpec"/> on purpose: the blueprint deserializer only recurses element by
    /// element into <c>ImmutableArray&lt;T&gt;</c>; <c>T[]</c>/<c>List&lt;T&gt;</c> go through
    /// primitive serialization and would corrupt `{Id, Amount}` objects. Vanilla
    /// <c>BuildingSpec.BuildingCost</c> uses the exact same type/pattern.
    /// </summary>
    [SpecAlias("MWP.MechanicalWaterPumpSpec")]
    public record MechanicalWaterPumpSpec : ComponentSpec
    {
        /// <summary>Tier token: Basic / Intermediate / Advanced / Top (CONTRACT §3).</summary>
        [Serialize]
        public string Tier { get; set; } = "Basic";

        /// <summary>Rated Water per game hour at 100% power, clean water, condition 100 (3/6/15/20).</summary>
        [Serialize]
        public float WaterPerHour { get; set; } = 3f;

        /// <summary>Mechanical power demand in hp (100/200/500/700).</summary>
        [Serialize]
        public int PowerRequiredHp { get; set; } = 100;

        /// <summary>Internal Water capacity; must equal <c>SimpleOutputInventorySpec.Capacity</c>.</summary>
        [Serialize]
        public int InternalCapacity { get; set; } = 15;

        /// <summary>Maximum intake depth in blocks; must equal <c>WaterInputPipeSpec.MaxDepth</c>.</summary>
        [Serialize]
        public int MaxDepth { get; set; } = 2;

        /// <summary>Contamination penalty; 1.0 reproduces vanilla <c>1 - c</c> (DV-01).</summary>
        [Serialize]
        public float BadwaterPenalty { get; set; } = 1f;

        /// <summary>Lower bound of the water-quality multiplier; 0.0 reproduces vanilla.</summary>
        [Serialize]
        public float MinWaterQuality { get; set; } = 0f;

        /// <summary>Water-body volume consumed per produced Water item (vanilla: 0.2 m³).</summary>
        [Serialize]
        public float CubicMetersPerWater { get; set; } = 0.2f;

        /// <summary>Days of Producing time a full condition lasts (8/10/12/15, REQ-MAINT-02).</summary>
        [Serialize]
        public float MaintenanceIntervalDays { get; set; } = 8f;

        /// <summary>Maintenance downtime in game hours (default 2, REQ-MAINT-04).</summary>
        [Serialize]
        public float MaintenanceDurationHours { get; set; } = 2f;

        /// <summary>Materials consumed per maintenance cycle, e.g. <c>[{"Id":"Log","Amount":10}]</c>.</summary>
        [Serialize]
        public ImmutableArray<GoodAmountSpec> MaintenanceMaterials { get; set; } =
            ImmutableArray<GoodAmountSpec>.Empty;

        /// <summary>Resolves the tier id from <see cref="Tier"/>; unknown tokens fall back to Basic.</summary>
        /// <returns>A valid tier id.</returns>
        public PumpTier ResolveTierId()
        {
            TierDefinition? definition = TierCatalog.TryGet(Tier);
            return definition != null ? definition.Tier : PumpTier.Basic;
        }

        /// <summary>
        /// Converts the serialized maintenance materials into validated Core stacks.
        ///
        /// Runtime balance is config-driven (CONTRACT §9), so this is only the blueprint-declared
        /// fallback: empty/invalid arrays fall back to the tier default from <see cref="TierCatalog"/>,
        /// and every amount is clamped to a sane positive range (REQ-SAFE-01).
        /// </summary>
        /// <returns>Non-null, non-empty list of materials for this pump.</returns>
        public IReadOnlyList<GoodStack> ResolveMaintenanceMaterials()
        {
            if (MaintenanceMaterials.IsDefaultOrEmpty)
            {
                return TierCatalog.Get(ResolveTierId()).MaintenanceMaterials;
            }

            var stacks = new List<GoodStack>(MaintenanceMaterials.Length);
            for (int i = 0; i < MaintenanceMaterials.Length; i++)
            {
                GoodAmountSpec? material = MaintenanceMaterials[i];
                if (material == null || string.IsNullOrWhiteSpace(material.Id))
                {
                    continue;
                }

                int amount = material.Amount;
                if (amount <= 0)
                {
                    continue;
                }

                if (amount > 10000)
                {
                    amount = 10000;
                }

                stacks.Add(new GoodStack(material.Id, amount));
            }

            return stacks.Count > 0 ? stacks : TierCatalog.Get(ResolveTierId()).MaintenanceMaterials;
        }
    }
}
