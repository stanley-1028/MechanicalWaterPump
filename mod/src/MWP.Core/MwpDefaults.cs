using System;

namespace MWP.Core
{
    /// <summary>
    /// Stable identifiers shared by data, gameplay, UI and the validator. Changing a value here
    /// changes the shipped mod contract, so these are constants rather than configuration.
    /// </summary>
    public static class MwpDefaults
    {
        /// <summary>Mod id, must equal manifest.json "Id" (REQ-NFR-01).</summary>
        public const string ModId = "MechanicalWaterPump";

        /// <summary>Display version shipped in the manifest.</summary>
        public const string ModVersion = "0.2.2";

        /// <summary>Name of the JSON configuration file in the mod root (config schema 2).</summary>
        public const string ConfigFileName = "MWP_Config.json";

        /// <summary>Save node / component key for one pump component (REQ-SAVE-01).</summary>
        public const string SaveComponentKey = "MWP";

        /// <summary>Current save schema version (REQ-MAINT-11).</summary>
        public const int SaveSchemaVersion = 2;

        /// <summary>Current config schema version (requirements chapter 20.6).</summary>
        public const int ConfigSchemaVersion = 2;

        /// <summary>Minimum supported config schema version that still migrates.</summary>
        public const int MinSupportedConfigSchemaVersion = 1;

        /// <summary>Production/maintenance tick interval in seconds (REQ-PERF-01).</summary>
        public const double TickIntervalSeconds = 1.0;

        /// <summary>UI refresh rate limit in Hz (REQ-PERF-04).</summary>
        public const int UiRefreshHz = 4;

        /// <summary>Log throttle window in seconds, per key (REQ-OPS-03).</summary>
        public const double LogThrottleSeconds = 30.0;

        /// <summary>JSON spec key for the custom blueprint spec.</summary>
        public const string PumpSpecKey = "MWP.MechanicalWaterPumpSpec";

        /// <summary>Blueprint path prefix containing the four tiers and two factions.</summary>
        public const string BlueprintFolder = "Buildings/Water/MWP";

        /// <summary>Tool group the four tiers appear in (REQ-TECH-02).</summary>
        public const string ToolGroupId = "Water";

        /// <summary>Localization key namespace.</summary>
        public const string LocNamespace = "MWP";

        /// <summary>Maximum number of failed water-body withdrawals before reporting NoWater (§11.4).</summary>
        public const int MaxConsecutiveWaterWithdrawFailures = 5;

        /// <summary>Vanilla conversion: 1 Water item consumes 0.2 m³ of water body (verified in IL).</summary>
        public const double CubicMetersPerWater = 0.2;

        /// <summary>Days per game day (Timberborn day length).</summary>
        public const double HoursPerDay = 24.0;

        /// <summary>Formatter used for UI numbers (one decimal, invariant culture).</summary>
        public static string FormatRate(double value) => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Formatter used for UI percentages (whole percent, invariant culture).</summary>
        public static string FormatPercent(double fraction) => (fraction * 100.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Every localization key the mod ships. The validator checks that all of these exist in
    /// enUS/zhCN/zhTW with identical placeholders (REQ-UI-05).
    /// </summary>
    public static class MwpLocKeys
    {
        /// <summary>Display name key for a tier, for example "Building.MWP.Pump.Basic.DisplayName".</summary>
        /// <param name="tierName">Tier token.</param>
        public static string DisplayName(string tierName) => "Building.MWP.Pump." + tierName + ".DisplayName";

        /// <summary>Description key for a tier.</summary>
        /// <param name="tierName">Tier token.</param>
        public static string Description(string tierName) => "Building.MWP.Pump." + tierName + ".Description";

        /// <summary>Flavor text key shared by all tiers.</summary>
        public const string Flavor = "Building.MWP.Pump.Flavor";

        /// <summary>Product name with placeholders: status text and its tooltip.</summary>
        public const string StatusNoPower = "Status.MWP.NoPower";

        /// <summary>Low-power state with a percent placeholder.</summary>
        public const string StatusLowPower = "Status.MWP.LowPower";

        /// <summary>No water state.</summary>
        public const string StatusNoWater = "Status.MWP.NoWater";

        /// <summary>
        /// Next-step hint for the no-water state: the pump's intake is not in water. MWP's own
        /// sentence (0.2.2) instead of the vanilla "This building needs deeper water to function"
        /// hint: with an unlimited intake depth the pump works at any water level, so the vanilla
        /// sentence would be misleading.
        /// </summary>
        public const string HintNoWater = "UI.MWP.HintNoWater";

        /// <summary>Internal tank full state.</summary>
        public const string StatusStorageFull = "Status.MWP.StorageFull";

        /// <summary>No hauler path state.</summary>
        public const string StatusNoAccess = "Status.MWP.NoAccess";

        /// <summary>Producing state with a rate placeholder.</summary>
        public const string StatusProducing = "Status.MWP.Producing";

        /// <summary>Paused state.</summary>
        public const string StatusPaused = "Status.MWP.Paused";

        /// <summary>Direct pipe connection available.</summary>
        public const string StatusPipeDirect = "Status.MWP.PipeDirect";

        /// <summary>Pipe extraction requires a valve.</summary>
        public const string StatusPipeValve = "Status.MWP.PipeValve";

        /// <summary>Direct pipe adapter failed and degraded.</summary>
        public const string StatusPipeUnavailable = "Status.MWP.PipeUnavailable";

        /// <summary>Power demand row with a hp placeholder.</summary>
        public const string StatusPowerDemand = "Status.MWP.PowerDemand";

        /// <summary>Power supply row with a percent placeholder.</summary>
        public const string StatusPowerSupply = "Status.MWP.PowerSupply";

        /// <summary>Water quality row with a percent placeholder.</summary>
        public const string StatusWaterQuality = "Status.MWP.WaterQuality";

        /// <summary>Maintenance due state with a percent placeholder.</summary>
        public const string StatusMaintenanceDue = "Status.MWP.MaintenanceDue";

        /// <summary>Critical wear state with a percent placeholder.</summary>
        public const string StatusCritical = "Status.MWP.Critical";

        /// <summary>Condition zero state.</summary>
        public const string StatusNeedsMaintenance = "Status.MWP.NeedsMaintenance";

        /// <summary>Maintenance downtime state with a percent placeholder.</summary>
        public const string StatusMaintaining = "Status.MWP.Maintaining";

        /// <summary>Missing maintenance materials state.</summary>
        public const string StatusMissingMaintenanceMaterials = "Status.MWP.MissingMaintenanceMaterials";

        /// <summary>No path for maintenance delivery state.</summary>
        public const string StatusNoAccessForMaintenance = "Status.MWP.NoAccessForMaintenance";

        /// <summary>Condition row with a percent placeholder.</summary>
        public const string UiCondition = "UI.MWP.Condition";

        /// <summary>Next maintenance countdown row with a days placeholder.</summary>
        public const string UiMaintenanceIn = "UI.MWP.MaintenanceIn";

        /// <summary>Required maintenance materials row.</summary>
        public const string UiMaintenanceMaterials = "UI.MWP.MaintenanceMaterials";

        /// <summary>Manual maintenance button label.</summary>
        public const string UiMaintainNow = "UI.MWP.MaintainNow";

        /// <summary>Auto-maintenance toggle label.</summary>
        public const string UiAutoMaintenance = "UI.MWP.AutoMaintenance";

        /// <summary>Internal inventory row with current/capacity placeholders.</summary>
        public const string UiInventory = "UI.MWP.Inventory";

        /// <summary>Actual output row with a rate placeholder: "Output: {0} water/hour" (REQ-UI-01).</summary>
        public const string UiWaterRate = "UI.MWP.WaterRate";

        /// <summary>Intake depth row with a blocks placeholder (REQ-UI-01).</summary>
        public const string UiMaxDepth = "UI.MWP.MaxDepth";

        /// <summary>Pause button label (REQ-UI-07).</summary>
        public const string UiPause = "UI.MWP.Pause";

        /// <summary>Resume button label (REQ-UI-07).</summary>
        public const string UiResume = "UI.MWP.Resume";

        /// <summary>Pipe compatibility tooltip.</summary>
        public const string TooltipPipeCompat = "Tooltip.MWP.PipeCompat";

        /// <summary>Rated water rate tooltip.</summary>
        public const string TooltipWaterRate = "Tooltip.MWP.WaterRate";

        /// <summary>Citation line used in the flavor text.</summary>
        public const string FlavorCitation = "MWP.Flavor.Citation";
    }
}
