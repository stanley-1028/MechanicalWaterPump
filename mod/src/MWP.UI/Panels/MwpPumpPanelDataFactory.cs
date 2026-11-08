using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MWP.Core;
using MWP.UI.Localization;
using Timberborn.Localization;

namespace MWP.UI.Panels
{
    /// <summary>
    /// Pure mapping from the Core read model to the localized panel strings (CONTRACT §10).
    /// Formatting rules follow the requirements: percentages are whole numbers, output and day
    /// counts have one decimal, NaN/Infinity/negative values degrade to the "unknown" glyph
    /// instead of leaking a number to the player.
    ///
    /// Every MWP text comes from <see cref="MwpLocKeys"/>; the NoPower and NoAccess "next step"
    /// hints and the good display names are base-game keys (see <see cref="MwpUiVanillaKeys"/>).
    /// The NoWater hint is an MWP sentence since 0.2.2: the vanilla "needs deeper water" text no
    /// longer applies because the pump reaches any water level. Only the rows that no vanilla
    /// fragment renders are produced here: power, internal inventory and intake depth come from
    /// the game's own middle/bottom fragments.
    /// </summary>
    public static class MwpPumpPanelDataFactory
    {
        /// <summary>Locale-neutral placeholder used when a value is unknown.</summary>
        private const string Unknown = "\u2014";

        /// <summary>Separator between two maintenance material stacks.</summary>
        private const string MaterialSeparator = ", ";

        /// <summary>Builds the panel payload for one snapshot.</summary>
        /// <param name="snapshot">Read model of the selected pump.</param>
        /// <param name="loc">Localization service used for every visible string.</param>
        /// <param name="showInventoryRow">True when the vanilla inventory fragment cannot render the Water stock, so the panel keeps its own row.</param>
        /// <param name="showDepthRow">True when the vanilla intake-depth fragment cannot render the depth, so the panel keeps its own row.</param>
        /// <returns>The formatted panel data; <see cref="MwpPumpPanelData.HasData"/> is false for unfinished buildings.</returns>
        public static MwpPumpPanelData Create(in PumpSnapshot snapshot, ILoc loc, bool showInventoryRow,
            bool showDepthRow)
        {
            if (loc is null)
            {
                throw new ArgumentNullException(nameof(loc));
            }

            // An unfinished pump has no panel yet: construction sites get their own vanilla panel
            // and the Core state machine reports Unfinished until the building is finished.
            if (!snapshot.IsFinished)
            {
                return new MwpPumpPanelData();
            }

            TierDefinition tier = TierCatalog.Get(SafeTier(snapshot.Tier));
            (string Text, string Hint) status = StatusRows(snapshot, loc);
            int conditionPercent = ConditionPercent(snapshot.Condition);
            return new MwpPumpPanelData
            {
                HasData = true,
                State = snapshot.State,
                Status = status.Text,
                StatusHint = status.Hint,
                Rate = loc.T(MwpLocKeys.UiWaterRate, MwpDefaults.FormatRate(Clamp(snapshot.EffectiveWaterPerHour))),
                WaterQuality = loc.T(MwpLocKeys.StatusWaterQuality, PercentPoint(snapshot.WaterQuality)),
                Inventory = showInventoryRow
                    ? loc.T(MwpLocKeys.UiInventory, snapshot.WaterInStock, snapshot.WaterCapacity)
                    : string.Empty,
                Depth = showDepthRow ? loc.T(MwpLocKeys.UiMaxDepth, tier.MaxDepth) : string.Empty,
                ShowInventoryRow = showInventoryRow,
                ShowDepthRow = showDepthRow,
                Condition = loc.T(MwpLocKeys.UiCondition, conditionPercent),
                ConditionPercent = conditionPercent,
                MaintenanceVisible = snapshot.MaintenanceEnabled,
                MaintenanceIn = loc.T(MwpLocKeys.UiMaintenanceIn, FormatDays(snapshot.MaintenanceDaysLeft)),
                Materials = loc.T(MwpLocKeys.UiMaintenanceMaterials, FormatMaterials(loc, tier.MaintenanceMaterials)),
                IsMaintaining = snapshot.State == PumpState.Maintaining,
                MaintainingPercent = PercentPoint(snapshot.MaintenanceProgress),
                AutoMaintenance = snapshot.AutoMaintenance,
                Paused = snapshot.Paused,
                PauseLabel = loc.T(snapshot.Paused ? MwpLocKeys.UiResume : MwpLocKeys.UiPause),
                Pipe = loc.T(PipeKey(snapshot.PipeAdapter)),
                PipeTooltip = loc.T(MwpLocKeys.TooltipPipeCompat),
            };
        }

        /// <summary>
        /// Localizes the highest-priority state plus its optional "next step" hint (REQ-UI-02).
        /// The state machine already picked the single highest-priority state, so this only maps.
        /// </summary>
        private static (string Text, string Hint) StatusRows(in PumpSnapshot snapshot, ILoc loc)
        {
            int conditionPercent = ConditionPercent(snapshot.Condition);
            switch (snapshot.State)
            {
                case PumpState.NoPower:
                    return (loc.T(MwpLocKeys.StatusNoPower), loc.T(MwpUiVanillaKeys.HintNoPower));
                case PumpState.NoWater:
                    return (loc.T(MwpLocKeys.StatusNoWater), loc.T(MwpLocKeys.HintNoWater));
                case PumpState.NoAccess:
                    return (loc.T(MwpLocKeys.StatusNoAccess), loc.T(MwpUiVanillaKeys.HintNoAccess));
                case PumpState.StorageFull:
                    return (loc.T(MwpLocKeys.StatusStorageFull), string.Empty);
                case PumpState.NeedsMaintenance:
                    return (loc.T(MwpLocKeys.StatusNeedsMaintenance), string.Empty);
                case PumpState.Maintaining:
                    // §20.2.5: "Maintaining: {0}%" is the progress of the ongoing downtime, not
                    // the condition; Core hands us elapsed/total hours as a 0..1 fraction.
                    return (loc.T(MwpLocKeys.StatusMaintaining, PercentPoint(snapshot.MaintenanceProgress)),
                        string.Empty);
                case PumpState.MissingMaintenanceMaterials:
                    return (loc.T(MwpLocKeys.StatusMissingMaintenanceMaterials), string.Empty);
                case PumpState.NoAccessForMaintenance:
                    return (loc.T(MwpLocKeys.StatusNoAccessForMaintenance), string.Empty);
                case PumpState.Critical:
                    return (loc.T(MwpLocKeys.StatusCritical, conditionPercent), string.Empty);
                case PumpState.MaintenanceDue:
                    return (loc.T(MwpLocKeys.StatusMaintenanceDue, conditionPercent), string.Empty);
                case PumpState.LowPower:
                    return (loc.T(MwpLocKeys.StatusLowPower, PercentPoint(snapshot.PowerRatio)), string.Empty);
                case PumpState.Paused:
                    return (loc.T(MwpLocKeys.StatusPaused), string.Empty);
                case PumpState.Producing:
                    return (loc.T(MwpLocKeys.StatusProducing, MwpDefaults.FormatRate(Clamp(snapshot.EffectiveWaterPerHour))),
                        string.Empty);
                default:
                    return (Unknown, string.Empty);
            }
        }

        /// <summary>Clamps an out-of-range tier value to the safest row instead of throwing (REQ-SAFE-01).</summary>
        private static PumpTier SafeTier(PumpTier tier)
        {
            int value = (int)tier;
            return value >= 0 && value < TierCatalog.Default.Count ? tier : PumpTier.Basic;
        }

        /// <summary>Maps the pipe adapter state to one of the four player-visible pipe states (CONTRACT §10 item 5).</summary>
        private static string PipeKey(PipeAdapterState state)
        {
            switch (state)
            {
                case PipeAdapterState.Direct:
                    return MwpLocKeys.StatusPipeDirect;
                case PipeAdapterState.ValveOnly:
                    return MwpLocKeys.StatusPipeValve;
                case PipeAdapterState.Error:
                    return MwpLocKeys.StatusPipeUnavailable;
                default:
                    // Unknown (detection did not run yet) and None (Timber Pipes absent) both mean
                    // "only the vanilla inventory path exists"; the compat tooltip says why.
                    return MwpLocKeys.TooltipPipeCompat;
            }
        }

        /// <summary>Formats the maintenance material list, for example "10 Log, 10 Plank".</summary>
        private static string FormatMaterials(ILoc loc, IReadOnlyList<GoodStack>? materials)
        {
            if (materials is null || materials.Count == 0)
            {
                return Unknown;
            }

            var builder = new StringBuilder(48);
            for (int i = 0; i < materials.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(MaterialSeparator);
                }

                builder.Append(materials[i].Amount.ToString(CultureInfo.InvariantCulture));
                builder.Append(' ');
                builder.Append(GoodName(loc, materials[i].GoodId));
            }

            return builder.ToString();
        }

        /// <summary>Localizes a maintenance good; unknown good ids fall back to the raw id (never a raw key).</summary>
        private static string GoodName(ILoc loc, string goodId)
        {
            switch (goodId)
            {
                case GoodStacks.Log:
                case GoodStacks.Plank:
                case GoodStacks.MetalBlock:
                case GoodStacks.Water:
                    return loc.T(MwpUiVanillaKeys.GoodDisplayNamePrefix + goodId + MwpUiVanillaKeys.GoodDisplayNameSuffix);
                default:
                    return goodId;
            }
        }

        /// <summary>Whole percentage of a 0..1 fraction (percent points, as the MWP templates expect).</summary>
        private static int PercentPoint(double fraction)
        {
            if (double.IsNaN(fraction) || double.IsInfinity(fraction))
            {
                return 0;
            }

            double clamped = Math.Max(0.0, Math.Min(1.0, fraction));
            return (int)Math.Round(clamped * 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Whole percentage of a 0..100 condition value.</summary>
        private static int ConditionPercent(double condition)
        {
            if (double.IsNaN(condition) || double.IsInfinity(condition))
            {
                return 0;
            }

            double clamped = Math.Max(0.0, Math.Min(100.0, condition));
            return (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
        }

        /// <summary>Formats a day count with one decimal, or the unknown glyph.</summary>
        private static string FormatDays(double days)
        {
            if (double.IsNaN(days) || double.IsInfinity(days) || days < 0.0)
            {
                return Unknown;
            }

            return MwpDefaults.FormatRate(days);
        }

        /// <summary>Rejects NaN/Infinity/negative values so no formatter ever prints "NaN" (REQ-SAFE-01).</summary>
        private static double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                return 0.0;
            }

            return value;
        }
    }
}
