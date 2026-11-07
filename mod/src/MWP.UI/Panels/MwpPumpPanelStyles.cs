using MWP.Core;
using Timberborn.CoreUI;
using UnityEngine.UIElements;

namespace MWP.UI.Panels
{
    /// <summary>
    /// Native-style building blocks for the pump panel.
    ///
    /// Every element here is the same control type the vanilla entity-panel fragments instantiate
    /// and carries the same USS classes as the shipped UXML templates (`entity-sub-panel`,
    /// `bg-sub-box--green`, `entity-panel__text`, `entity-panel__sub-text`,
    /// `entity-panel__button-wrapper`, `entity-fragment__button--red/--green`, `game-toggle`,
    /// `entity-panel__toggle`, `progress-bar`, `progress-bar--green`). The game's own style
    /// sheets (`CoreStyle`, `CommonStyle`, `EntityPanelCommonStyle`, `EntityPanelGameStyle`,
    /// `GameStyle`) are attached to the GameUI/EntityPanel roots, so these classes style the panel
    /// exactly like the native fragments. No inline colour, font or size is set anywhere: the
    /// only thing the code touches is class membership and display visibility (both are the same
    /// operations the vanilla UXML/UQuery code does).
    /// </summary>
    public static class MwpPumpPanelStyles
    {
        /// <summary>Nine-slice sub-panel background class used by every native entity sub-panel.</summary>
        private const string SubPanelClass = "bg-sub-box--green";

        /// <summary>Sub-panel padding class (8px 12px) from EntityPanelCommonStyle.uss.</summary>
        private const string SectionClass = "entity-sub-panel";

        /// <summary>Native body text (13px, rgb(204,204,204)).</summary>
        private const string TextClass = "entity-panel__text";

        /// <summary>Native secondary text (12px) used for explanations.</summary>
        private const string SubTextClass = "entity-panel__sub-text";

        /// <summary>Native 13px grey text; the base for the status row so a colour class can override it.</summary>
        private const string StatusTextClass = "game-text-normal";

        /// <summary>Native yellow warning text class (CommonStyle.uss).</summary>
        private const string WarningTextClass = "text--yellow";

        /// <summary>Native red text class (CommonStyle.uss).</summary>
        private const string DangerTextClass = "game-text--red";

        /// <summary>Native row that lays its buttons out centred (EntityPanelCommonStyle.uss).</summary>
        private const string ButtonWrapperClass = "entity-panel__button-wrapper";

        /// <summary>Native fragment button base class.</summary>
        private const string ButtonClass = "entity-fragment__button";

        /// <summary>Native red nine-slice button skin.</summary>
        private const string ButtonDangerClass = "entity-fragment__button--red";

        /// <summary>Native green nine-slice button skin.</summary>
        private const string ButtonPositiveClass = "entity-fragment__button--green";

        /// <summary>Native toggle class used by every entity-panel toggle.</summary>
        private const string ToggleClass = "game-toggle";

        /// <summary>Native entity-panel toggle spacing class.</summary>
        private const string PanelToggleClass = "entity-panel__toggle";

        /// <summary>Native progress-bar frame class.</summary>
        private const string ProgressBarClass = "progress-bar";

        /// <summary>Native green progress-bar fill variant.</summary>
        private const string ProgressBarGreenClass = "progress-bar--green";

        /// <summary>Creates a native nine-slice sub-panel section.</summary>
        /// <returns>An empty section with the native background and padding.</returns>
        public static NineSliceVisualElement CreateSection()
        {
            var section = new NineSliceVisualElement();
            section.AddToClassList(SectionClass);
            section.AddToClassList(SubPanelClass);
            return section;
        }

        /// <summary>Creates one native body-text row and adds it to the section.</summary>
        /// <param name="parent">Section (or wrapper) that receives the label.</param>
        /// <returns>The created label.</returns>
        public static Label CreateTextRow(VisualElement parent)
        {
            var label = new Label();
            label.AddToClassList(TextClass);
            parent.Add(label);
            return label;
        }

        /// <summary>Creates the status row with the native text class a colour variant can override.</summary>
        /// <param name="parent">Section that receives the label.</param>
        /// <returns>The created label.</returns>
        public static Label CreateStatusRow(VisualElement parent)
        {
            var label = new Label();
            label.AddToClassList(StatusTextClass);
            parent.Add(label);
            return label;
        }

        /// <summary>Creates one native secondary-text row (the "next step" hint).</summary>
        /// <param name="parent">Section that receives the label.</param>
        /// <returns>The created label.</returns>
        public static Label CreateSubTextRow(VisualElement parent)
        {
            var label = new Label();
            label.AddToClassList(SubTextClass);
            parent.Add(label);
            return label;
        }

        /// <summary>Creates a native progress bar whose label overlays the fill.</summary>
        /// <param name="parent">Section that receives the bar.</param>
        /// <param name="label">Label placed inside the bar (native templates always do this).</param>
        /// <param name="green">True for the native green variant, false for the default fill.</param>
        /// <returns>The created progress bar; call <see cref="SetProgress"/> to update it.</returns>
        public static global::Timberborn.CoreUI.ProgressBar CreateProgressBar(VisualElement parent, out Label label, bool green)
        {
            var bar = new global::Timberborn.CoreUI.ProgressBar();
            bar.AddToClassList(ProgressBarClass);
            if (green)
            {
                bar.AddToClassList(ProgressBarGreenClass);
            }

            label = CreateTextRow(bar);
            parent.Add(bar);
            return bar;
        }

        /// <summary>Creates the native centred button row.</summary>
        /// <param name="parent">Section that receives the wrapper.</param>
        /// <returns>The created wrapper element.</returns>
        public static VisualElement CreateButtonWrapper(VisualElement parent)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList(ButtonWrapperClass);
            parent.Add(wrapper);
            return wrapper;
        }

        /// <summary>
        /// Applies the native fragment-button classes to a button. The game's own nine-slice
        /// button element is internal in this build, so the panel obtains a real native button
        /// from the vanilla UXML templates (see <c>MwpPumpPanelView</c>) and keeps it typed as
        /// <see cref="Button"/>; this method only ensures the native classes/fallback are present.
        /// </summary>
        /// <param name="button">Native or fallback button.</param>
        public static void ApplyFragmentButtonClasses(Button button)
        {
            button.AddToClassList(TextClass);
            button.AddToClassList(ButtonClass);
            button.AddToClassList(ButtonDangerClass);
        }

        /// <summary>Creates a native game toggle with a localized label.</summary>
        /// <param name="parent">Section that receives the toggle.</param>
        /// <param name="label">Already localized toggle label.</param>
        /// <returns>The created toggle.</returns>
        public static Toggle CreateToggle(VisualElement parent, string label)
        {
            var toggle = new Toggle(label);
            toggle.AddToClassList(ToggleClass);
            toggle.AddToClassList(TextClass);
            toggle.AddToClassList(PanelToggleClass);
            parent.Add(toggle);
            return toggle;
        }

        /// <summary>
        /// Applies the native colour class that matches the status severity: red for "stopped hard",
        /// yellow for "working but degraded", the default grey otherwise (requirements §12.2).
        /// </summary>
        /// <param name="label">Status label created with <see cref="CreateTextRow"/>-equivalent classes.</param>
        /// <param name="state">Highest-priority pump state.</param>
        public static void ApplyStatusColor(Label label, PumpState state)
        {
            bool danger = state == PumpState.NoPower || state == PumpState.NeedsMaintenance;
            bool warning = !danger && IsWarning(state);
            label.EnableInClassList(DangerTextClass, danger);
            label.EnableInClassList(WarningTextClass, warning);
        }

        /// <summary>Switches the pause button between the native red (pause) and green (resume) skins.</summary>
        /// <param name="button">Pause/resume button.</param>
        /// <param name="paused">Current pause flag.</param>
        public static void ApplyPauseColor(Button button, bool paused)
        {
            button.EnableInClassList(ButtonDangerClass, !paused);
            button.EnableInClassList(ButtonPositiveClass, paused);
        }

        /// <summary>Shows or hides an element the same way the vanilla fragments do.</summary>
        /// <param name="element">Element to toggle.</param>
        /// <param name="visible">True to display it.</param>
        public static void SetVisible(VisualElement element, bool visible) => element.ToggleDisplayStyle(visible);

        /// <summary>Applies a 0..100 value to a native progress bar.</summary>
        /// <param name="bar">Bar created by <see cref="CreateProgressBar"/>.</param>
        /// <param name="percent">Progress in percent.</param>
        public static void SetProgress(global::Timberborn.CoreUI.ProgressBar bar, double percent)
        {
            double clamped = double.IsNaN(percent) || double.IsInfinity(percent)
                ? 0.0
                : System.Math.Max(0.0, System.Math.Min(100.0, percent));
            bar.SetProgress((float)(clamped / 100.0));
        }

        private static bool IsWarning(PumpState state)
        {
            switch (state)
            {
                case PumpState.NoWater:
                case PumpState.NoAccess:
                case PumpState.StorageFull:
                case PumpState.MaintenanceDue:
                case PumpState.Critical:
                case PumpState.Maintaining:
                case PumpState.MissingMaintenanceMaterials:
                case PumpState.NoAccessForMaintenance:
                case PumpState.LowPower:
                    return true;
                default:
                    return false;
            }
        }
    }
}
