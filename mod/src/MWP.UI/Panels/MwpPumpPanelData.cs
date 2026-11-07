using MWP.Core;

namespace MWP.UI.Panels
{
    /// <summary>
    /// Everything the pump panel shows for one selected entity, already localized and formatted
    /// (CONTRACT §10). Built from <see cref="PumpSnapshot"/> plus the static tier catalogue; it
    /// holds no game components and no mutable state, so it can be produced without UIElements
    /// and re-used as the panel's dirty-marker payload.
    ///
    /// The panel deliberately shows only what the game's own fragments do not: power
    /// (`MechanicalNodeFragment`), the internal Water stock (`SimpleOutputInventoryFragment`) and
    /// the intake depth (`WaterInputPipeDepthFragment`) are rendered by vanilla fragments that
    /// already appear on a finished pump. The two <c>Show*Row</c> flags are the fallback for the
    /// case where such a vanilla fragment cannot appear (missing component), so no information is
    /// ever lost.
    /// </summary>
    public sealed class MwpPumpPanelData
    {
        /// <summary>False when the selected entity is not a finished pump; the fragment then hides.</summary>
        public bool HasData { get; set; }

        /// <summary>Highest-priority state, used to pick the native text-colour class (REQ-UI-03).</summary>
        public PumpState State { get; set; }

        /// <summary>Highest-priority status text (reason).</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Optional "next step" hint; empty when no localized vanilla hint exists.</summary>
        public string StatusHint { get; set; } = string.Empty;

        /// <summary>Actual output in Water/h, one decimal (REQ-UI-01).</summary>
        public string Rate { get; set; } = string.Empty;

        /// <summary>Water quality as a whole percentage.</summary>
        public string WaterQuality { get; set; } = string.Empty;

        /// <summary>Internal inventory "current/capacity"; only when the vanilla inventory fragment cannot show it.</summary>
        public string Inventory { get; set; } = string.Empty;

        /// <summary>Maximum intake depth of the tier; only when the vanilla depth fragment cannot show it.</summary>
        public string Depth { get; set; } = string.Empty;

        /// <summary>Whether our fallback inventory row is visible.</summary>
        public bool ShowInventoryRow { get; set; }

        /// <summary>Whether our fallback intake-depth row is visible.</summary>
        public bool ShowDepthRow { get; set; }

        /// <summary>Condition as a whole percentage, for the label inside the condition bar (REQ-MAINT-10).</summary>
        public string Condition { get; set; } = string.Empty;

        /// <summary>Condition in 0..100, used as the fill of the condition progress bar.</summary>
        public double ConditionPercent { get; set; }

        /// <summary>Estimated game days until the next maintenance.</summary>
        public string MaintenanceIn { get; set; } = string.Empty;

        /// <summary>Materials consumed by one maintenance cycle.</summary>
        public string Materials { get; set; } = string.Empty;

        /// <summary>
        /// False when the config disables the maintenance system (REQ-MAINT-13): the maintenance
        /// sub-panel (condition bar, countdown, materials, toggle, maintain button) is hidden
        /// instead of showing a permanently frozen 100 % condition.
        /// </summary>
        public bool MaintenanceVisible { get; set; }

        /// <summary>True while the maintenance downtime is running; shows the maintenance progress bar.</summary>
        public bool IsMaintaining { get; set; }

        /// <summary>Maintenance downtime progress 0..100 (§20.2.5).</summary>
        public double MaintainingPercent { get; set; }

        /// <summary>Auto-maintenance flag of this pump (drives the toggle without notify).</summary>
        public bool AutoMaintenance { get; set; }

        /// <summary>Per-pump pause flag (drives the pause/resume button label and colour, REQ-UI-07).</summary>
        public bool Paused { get; set; }

        /// <summary>Localized label of the pause/resume button ("Pause" or "Resume").</summary>
        public string PauseLabel { get; set; } = string.Empty;

        /// <summary>Pipe adapter state text: not installed / valve / direct / degraded.</summary>
        public string Pipe { get; set; } = string.Empty;

        /// <summary>Pipe adapter tooltip.</summary>
        public string PipeTooltip { get; set; } = string.Empty;
    }
}
