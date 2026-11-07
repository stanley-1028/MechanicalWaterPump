using MWP.Core;

namespace MWP.UI.Panels
{
    /// <summary>
    /// Everything the entity panel shows for one pump, already localized and formatted
    /// (CONTRACT §10). Built exclusively from <see cref="PumpSnapshot"/> plus the static tier
    /// catalogue; it holds no game components and no mutable state, so it can be produced without
    /// UIElements and re-used as the panel's dirty-marker payload.
    /// </summary>
    public sealed class MwpPumpPanelData
    {
        /// <summary>False when the selected entity is not a finished pump; the fragment then hides.</summary>
        public bool HasData { get; set; }

        /// <summary>Highest-priority state, used to colour the status row (REQ-UI-03, §12.2).</summary>
        public PumpState State { get; set; }

        /// <summary>Localized tier display name (REQ-UI-04).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Tooltip of the title/rate row: the rated-rate explanation.</summary>
        public string TitleTooltip { get; set; } = string.Empty;

        /// <summary>Highest-priority status text (reason).</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Optional "next step" hint; empty when no localized vanilla hint exists.</summary>
        public string StatusHint { get; set; } = string.Empty;

        /// <summary>Actual output in Water/h, one decimal (REQ-UI-01).</summary>
        public string Rate { get; set; } = string.Empty;

        /// <summary>Nominal power demand in hp.</summary>
        public string PowerDemand { get; set; } = string.Empty;

        /// <summary>Delivered power as a whole percentage.</summary>
        public string PowerSupply { get; set; } = string.Empty;

        /// <summary>Delivered/required hp plus the percentage (row tooltip).</summary>
        public string PowerTooltip { get; set; } = string.Empty;

        /// <summary>Water quality as a whole percentage.</summary>
        public string WaterQuality { get; set; } = string.Empty;

        /// <summary>Maximum intake depth of this tier in blocks (REQ-UI-01, §12.1).</summary>
        public string Depth { get; set; } = string.Empty;

        /// <summary>Internal inventory "current/capacity" (CONTRACT §10 item 3).</summary>
        public string Inventory { get; set; } = string.Empty;

        /// <summary>Condition as a whole percentage (REQ-MAINT-10).</summary>
        public string Condition { get; set; } = string.Empty;

        /// <summary>Estimated game days until the next maintenance.</summary>
        public string MaintenanceIn { get; set; } = string.Empty;

        /// <summary>Materials consumed by one maintenance cycle.</summary>
        public string Materials { get; set; } = string.Empty;

        /// <summary>Pipe adapter state text: not installed / valve / direct / degraded.</summary>
        public string Pipe { get; set; } = string.Empty;

        /// <summary>Pipe adapter tooltip.</summary>
        public string PipeTooltip { get; set; } = string.Empty;

        /// <summary>Auto-maintenance flag of this pump (drives the toggle without notify).</summary>
        public bool AutoMaintenance { get; set; }

        /// <summary>Per-pump pause flag (drives the pause/resume button label, REQ-UI-07).</summary>
        public bool Paused { get; set; }

        /// <summary>Localized label of the pause/resume button ("Pause" or "Resume").</summary>
        public string PauseLabel { get; set; } = string.Empty;

        /// <summary>Condition in 0..100, used as the fill of the condition progress bar.</summary>
        public double ConditionPercent { get; set; }

        /// <summary>
        /// False when the config disables the maintenance system (REQ-MAINT-13): the panel then
        /// hides the condition bar, countdown, materials, auto-maintenance toggle and maintain
        /// button instead of showing a permanently frozen 100 % condition.
        /// </summary>
        public bool MaintenanceVisible { get; set; }
    }
}
