using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MWP.Core
{
    /// <summary>
    /// One tier of the mechanical water pump: everything the blueprint, the config validator and
    /// the UI need in one immutable record. Defaults come from the requirements document chapter
    /// 20.1.2 (authoritative) and 20.2.4 (maintenance).
    /// </summary>
    public sealed class TierDefinition
    {
        /// <summary>Creates a tier definition. Prefer <see cref="TierCatalog"/> defaults.</summary>
        /// <param name="tier">Tier id.</param>
        /// <param name="tierName">Short tier token used in blueprint ids and template names.</param>
        /// <param name="scienceCost">Research points required to unlock.</param>
        /// <param name="buildCost">Construction materials.</param>
        /// <param name="sizeX">Width (engine X).</param>
        /// <param name="sizeY">Depth (engine Y; the requirements' middle "height" value is not this).</param>
        /// <param name="sizeZ">Height (engine Z).</param>
        /// <param name="waterPerHour">Rated output at 100% power, clean water, condition 100.</param>
        /// <param name="powerRequiredHp">Mechanical power demand in hp.</param>
        /// <param name="maxDepth">Maximum intake reach in blocks; 64 = effectively unlimited (all tiers reach any water level - user request 2026-11-08).</param>
        /// <param name="internalCapacity">Internal Water capacity.</param>
        /// <param name="builderHours">Construction builder-hours.</param>
        /// <param name="maintenanceIntervalDays">Days of Producing time between maintenance.</param>
        /// <param name="maintenanceMaterials">Materials consumed per maintenance.</param>
        public TierDefinition(
            PumpTier tier,
            string tierName,
            int scienceCost,
            IReadOnlyList<GoodStack> buildCost,
            int sizeX,
            int sizeY,
            int sizeZ,
            double waterPerHour,
            int powerRequiredHp,
            int maxDepth,
            int internalCapacity,
            int builderHours,
            double maintenanceIntervalDays,
            IReadOnlyList<GoodStack> maintenanceMaterials)
        {
            Tier = tier;
            TierName = tierName ?? throw new ArgumentNullException(nameof(tierName));
            ScienceCost = scienceCost;
            BuildCost = buildCost ?? throw new ArgumentNullException(nameof(buildCost));
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            WaterPerHour = waterPerHour;
            PowerRequiredHp = powerRequiredHp;
            MaxDepth = maxDepth;
            InternalCapacity = internalCapacity;
            BuilderHours = builderHours;
            MaintenanceIntervalDays = maintenanceIntervalDays;
            MaintenanceMaterials = maintenanceMaterials ?? throw new ArgumentNullException(nameof(maintenanceMaterials));
        }

        /// <summary>Tier id.</summary>
        public PumpTier Tier { get; }

        /// <summary>Short token used in blueprint ids (Basic/Intermediate/Advanced/Top).</summary>
        public string TierName { get; }

        /// <summary>Research points required to unlock (REQ-TECH-01).</summary>
        public int ScienceCost { get; }

        /// <summary>Construction materials (REQ-BLD-03).</summary>
        public IReadOnlyList<GoodStack> BuildCost { get; }

        /// <summary>
        /// Width (engine <c>BlockObjectSpec.Size.X</c>, the "X" of the requirements' footprint).
        /// </summary>
        public int SizeX { get; }

        /// <summary>
        /// Depth (engine <c>BlockObjectSpec.Size.Y</c>). Verified from the game's own convention:
        /// entrance Z equals BaseZ, 2D water-obstacle coordinates are (width, depth), and vanilla
        /// colliders match Size in the order (width, height, depth). Requirements chapter 4 writes
        /// sizes as "X x height x Z" - this property holds the engine's Y, i.e. the depth.
        /// </summary>
        public int SizeY { get; }

        /// <summary>
        /// Height (engine <c>BlockObjectSpec.Size.Z</c>, the "Y" of the requirements document).
        /// </summary>
        public int SizeZ { get; }

        /// <summary>Rated Water per game hour (REQ-BAL-07).</summary>
        public double WaterPerHour { get; }

        /// <summary>Mechanical power demand in hp (REQ-BAL-09).</summary>
        public int PowerRequiredHp { get; }

        /// <summary>Maximum intake reach in blocks; 64 = effectively unlimited (all tiers reach any water level - user request 2026-11-08).</summary>
        public int MaxDepth { get; }

        /// <summary>Internal Water inventory capacity.</summary>
        public int InternalCapacity { get; }

        /// <summary>Construction builder-hours.</summary>
        public int BuilderHours { get; }

        /// <summary>Days of Producing time between maintenance (REQ-MAINT-02).</summary>
        public double MaintenanceIntervalDays { get; }

        /// <summary>Materials consumed per maintenance (REQ-MAINT-03).</summary>
        public IReadOnlyList<GoodStack> MaintenanceMaterials { get; }

        /// <summary>Blueprint id, for example "MWP.Pump.Basic" (REQ-BLD-01).</summary>
        public string BlueprintId => "MWP.Pump." + TierName;

        /// <summary>
        /// Order inside the mod's own MWP tool group (10/20/30/40, the vanilla group convention:
        /// multiples of 10 leave room for future tiers).
        /// </summary>
        public int ToolOrder => 10 * ((int)Tier + 1);

        /// <summary>Approximate beavers fed per day, at 2.121 Water/day/beaver (documented conversion).</summary>
        public double BeaversFedPerDay => WaterPerHour * 24.0 / 2.121;

        /// <summary>Rated Water per 24 game hours.</summary>
        public double WaterPerDay => WaterPerHour * 24.0;
    }

    /// <summary>The authoritative default tier table (requirements chapter 20.1.2).</summary>
    public static class TierCatalog
    {
        /// <summary>All four tiers in unlock order.</summary>
        public static IReadOnlyList<TierDefinition> Default { get; } = new ReadOnlyCollection<TierDefinition>(new[]
        {
            new TierDefinition(
                PumpTier.Basic,
                "Basic",
                scienceCost: 150,
                buildCost: GoodStacks.Freeze(new GoodStack(GoodStacks.Log, 20)),
                sizeX: 2,
                sizeY: 3,
                sizeZ: 3,
                waterPerHour: 3.0,
                powerRequiredHp: 100,
                maxDepth: 64,
                internalCapacity: 15,
                builderHours: 3,
                maintenanceIntervalDays: 8.0,
                maintenanceMaterials: GoodStacks.Freeze(new GoodStack(GoodStacks.Log, 10))),
            new TierDefinition(
                PumpTier.Intermediate,
                "Intermediate",
                scienceCost: 400,
                buildCost: GoodStacks.Freeze(new GoodStack(GoodStacks.Plank, 30), new GoodStack(GoodStacks.Log, 10)),
                sizeX: 2,
                sizeY: 3,
                sizeZ: 3,
                waterPerHour: 6.0,
                powerRequiredHp: 200,
                maxDepth: 64,
                internalCapacity: 30,
                builderHours: 5,
                maintenanceIntervalDays: 10.0,
                maintenanceMaterials: GoodStacks.Freeze(new GoodStack(GoodStacks.Plank, 15))),
            new TierDefinition(
                PumpTier.Advanced,
                "Advanced",
                scienceCost: 700,
                buildCost: GoodStacks.Freeze(new GoodStack(GoodStacks.MetalBlock, 40), new GoodStack(GoodStacks.Plank, 20)),
                sizeX: 3,
                sizeY: 3,
                sizeZ: 4,
                waterPerHour: 15.0,
                powerRequiredHp: 500,
                maxDepth: 64,
                internalCapacity: 75,
                builderHours: 8,
                maintenanceIntervalDays: 12.0,
                maintenanceMaterials: GoodStacks.Freeze(new GoodStack(GoodStacks.MetalBlock, 10), new GoodStack(GoodStacks.Plank, 10))),
            new TierDefinition(
                PumpTier.Top,
                "Top",
                scienceCost: 1500,
                buildCost: GoodStacks.Freeze(new GoodStack(GoodStacks.MetalBlock, 60)),
                sizeX: 3,
                sizeY: 4,
                sizeZ: 4,
                waterPerHour: 20.0,
                powerRequiredHp: 700,
                maxDepth: 64,
                internalCapacity: 100,
                builderHours: 12,
                maintenanceIntervalDays: 15.0,
                maintenanceMaterials: GoodStacks.Freeze(new GoodStack(GoodStacks.MetalBlock, 15)))
        });

        /// <summary>Looks a tier up by name token ("Basic", "Intermediate", "Advanced", "Top"), case-insensitive.</summary>
        /// <param name="tierName">Tier token from the blueprint spec.</param>
        public static TierDefinition? TryGet(string? tierName)
        {
            if (string.IsNullOrWhiteSpace(tierName))
            {
                return null;
            }

            for (int i = 0; i < Default.Count; i++)
            {
                if (string.Equals(Default[i].TierName, tierName, StringComparison.OrdinalIgnoreCase))
                {
                    return Default[i];
                }
            }

            return null;
        }

        /// <summary>Looks a tier up by enum value.</summary>
        /// <param name="tier">Tier id.</param>
        public static TierDefinition Get(PumpTier tier) => Default[(int)tier];
    }
}
