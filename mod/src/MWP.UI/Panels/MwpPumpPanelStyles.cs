using MWP.Core;
using Timberborn.CoreUI;
using UnityEngine.UIElements;

namespace MWP.UI.Panels
{
    /// <summary>
    /// The vanilla building blocks the pump panel is composed of.
    ///
    /// Every element and every USS class here comes from the shipped game UI: the panel is built
    /// from the same controls and class names the vanilla entity-panel fragments use
    /// (<c>entity-sub-panel</c> + <c>bg-sub-box--green</c>/<c>--red-striped</c> cards,
    /// <c>entity-panel__row</c>/<c>entity-panel__text</c> rows with the 24 px
    /// <c>inventory-row__image</c> icon, the <c>status-list-element</c> icon+text row,
    /// <c>progress-bar</c> bars, <c>game-toggle</c> toggles, <c>entity-panel__button-wrapper</c>
    /// buttons). The game attaches its style sheets (CoreStyle, CommonStyle,
    /// EntityPanelCommonStyle, EntityPanelGameStyle, GameStyle) to the EntityPanel root, so these
    /// classes are styled exactly like the vanilla fragments without the mod shipping USS/UXML.
    /// No inline colour, font or size is ever set here; only class membership and display
    /// visibility are touched, which is what the vanilla UQuery/UXML code does.
    /// </summary>
    public static class MwpPumpPanelStyles
    {
        // ----- cards -------------------------------------------------------------------------
        /// <summary>Vanilla card padding class (EntityPanelCommonStyle.uss).</summary>
        private const string SectionClass = "entity-sub-panel";

        /// <summary>Green nine-slice sub-panel frame every normal vanilla fragment uses.</summary>
        private const string SectionGreenClass = "bg-sub-box--green";

        /// <summary>Red-striped nine-slice frame the vanilla status list uses for alerts.</summary>
        private const string SectionAlertClass = "bg-sub-box--red-striped";

        /// <summary>Blue nine-slice explanation box (TimerFragment mode description).</summary>
        private const string ExplanationClass = "entity-fragment__explanation";

        /// <summary>Blue sub-box frame of the explanation box.</summary>
        private const string ExplanationFrameClass = "bg-sub-box--blue";

        // ----- rows --------------------------------------------------------------------------
        /// <summary>Vanilla row container (EntityPanelCommonStyle.uss).</summary>
        private const string RowClass = "entity-panel__row";

        /// <summary>Vanilla 24 px row icon slot (EntityPanelGameStyle.uss).</summary>
        private const string RowIconClass = "inventory-row__image";

        /// <summary>Native body text (13 px, rgb(204,204,204)).</summary>
        private const string TextClass = "entity-panel__text";

        /// <summary>
        /// Native centred text used inside the explanation box — the exact pair the vanilla
        /// <c>TimerFragment</c>/<c>MemoryFragment</c> mode-description label carries.
        /// </summary>
        private const string CenteredTextClass = "text--centered";

        /// <summary>Native secondary text (12 px) used for explanations.</summary>
        private const string SubTextClass = "entity-panel__sub-text";

        /// <summary>Native yellow warning text class (CommonStyle.uss).</summary>
        private const string WarningTextClass = "text--yellow";

        /// <summary>Native red text class (CommonStyle.uss).</summary>
        private const string DangerTextClass = "game-text--red";

        // ----- status row --------------------------------------------------------------------
        /// <summary>Vanilla status list row (StatusListElement.uxml).</summary>
        private const string StatusElementClass = "status-list-element";

        /// <summary>Vanilla 34 px status row icon.</summary>
        private const string StatusIconClass = "status-list-element__icon";

        /// <summary>Vanilla status row text.</summary>
        private const string StatusTextClass = "status-list-element__text";

        /// <summary>
        /// Native 13 px grey text used as the status base. Deliberately not
        /// <c>entity-panel__text</c>: that rule is loaded after CommonStyle and would override the
        /// native red/yellow severity colour classes, so the status row keeps the vanilla status
        /// list's visual while still letting the severity colour win.
        /// </summary>
        private const string StatusTextBaseClass = "game-text-normal";

        // ----- controls ----------------------------------------------------------------------
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

        /// <summary>Creates a native nine-slice sub-panel card.</summary>
        /// <param name="alert">True for the red-striped alert frame, false for the normal green frame.</param>
        /// <returns>An empty card with the native background and padding.</returns>
        public static NineSliceVisualElement CreateSection(bool alert)
        {
            var section = new NineSliceVisualElement();
            section.AddToClassList(SectionClass);
            section.AddToClassList(alert ? SectionAlertClass : SectionGreenClass);
            return section;
        }

        /// <summary>
        /// Switches a card between the normal green frame and the red-striped alert frame, the same
        /// way the vanilla status list appears only when there is something to report.
        /// </summary>
        /// <param name="section">Card created with <see cref="CreateSection"/>.</param>
        /// <param name="alert">True for the red-striped alert frame.</param>
        public static void SetSectionTone(NineSliceVisualElement section, bool alert)
        {
            section.EnableInClassList(SectionAlertClass, alert);
            section.EnableInClassList(SectionGreenClass, !alert);
        }

        /// <summary>Creates the vanilla icon+text row used by the game's status list.</summary>
        /// <param name="parent">Card that receives the row.</param>
        /// <param name="icon">
        /// The 34 px status icon element. Like the vanilla <c>StatusListFragment</c>, the view sets
        /// its <c>style.backgroundImage</c> to the status sprite (the game does the same inline
        /// background-image assignment; no colour/size is set there).
        /// </param>
        /// <returns>The status text label.</returns>
        public static Label CreateStatusRow(VisualElement parent, out VisualElement icon)
        {
            var row = new VisualElement();
            row.AddToClassList(StatusElementClass);

            icon = new VisualElement();
            icon.AddToClassList(StatusIconClass);
            row.Add(icon);

            var label = new Label();
            label.AddToClassList(StatusTextBaseClass);
            label.AddToClassList(StatusTextClass);
            row.Add(label);

            parent.Add(row);
            return label;
        }

        /// <summary>Creates one native icon+text row.</summary>
        /// <param name="parent">Card that receives the row.</param>
        /// <param name="iconPath">Vanilla sprite resource path (see <see cref="MwpPumpPanelIcons"/>).</param>
        /// <param name="icon">The 24 px row icon element.</param>
        /// <returns>The row text label.</returns>
        public static Label CreateIconRow(VisualElement parent, string iconPath, out Image icon)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);

            icon = new Image();
            icon.AddToClassList(RowIconClass);
            icon.sprite = MwpPumpPanelIcons.Get(iconPath);
            icon.ToggleDisplayStyle(icon.sprite != null);
            row.Add(icon);

            var label = new Label();
            label.AddToClassList(TextClass);
            row.Add(label);

            parent.Add(row);
            return label;
        }

        /// <summary>Creates one native body-text row without an icon.</summary>
        /// <param name="parent">Card (or bar) that receives the label.</param>
        /// <returns>The created label.</returns>
        public static Label CreateTextRow(VisualElement parent)
        {
            var label = new Label();
            label.AddToClassList(TextClass);
            parent.Add(label);
            return label;
        }

        /// <summary>Creates one native secondary-text row (the "next step" hint).</summary>
        /// <param name="parent">Card that receives the label.</param>
        /// <returns>The created label.</returns>
        public static Label CreateSubTextRow(VisualElement parent)
        {
            var label = new Label();
            label.AddToClassList(SubTextClass);
            parent.Add(label);
            return label;
        }

        /// <summary>
        /// Creates the vanilla blue explanation box (TimerFragment's mode-description pattern) used
        /// for the "what to do next" hint.
        /// </summary>
        /// <param name="parent">Card that receives the box.</param>
        /// <returns>The centred label inside the box.</returns>
        public static Label CreateExplanationBox(VisualElement parent)
        {
            var box = new NineSliceVisualElement();
            box.AddToClassList(ExplanationClass);
            box.AddToClassList(ExplanationFrameClass);

            var label = new Label();
            label.AddToClassList(TextClass);
            label.AddToClassList(CenteredTextClass);
            box.Add(label);

            parent.Add(box);
            return label;
        }

        /// <summary>Creates a native progress bar whose label overlays the fill.</summary>
        /// <param name="parent">Card that receives the bar.</param>
        /// <param name="label">Label placed inside the bar (native templates always do this).</param>
        /// <param name="green">True for the native green variant, false for the default fill.</param>
        /// <returns>The created progress bar; call <see cref="SetProgress"/> to update it.</returns>
        public static global::Timberborn.CoreUI.ProgressBar CreateProgressBar(VisualElement parent, out Label label,
            bool green)
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
        /// <param name="parent">Card that receives the wrapper.</param>
        /// <returns>The created wrapper element.</returns>
        public static VisualElement CreateButtonWrapper(VisualElement parent)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList(ButtonWrapperClass);
            parent.Add(wrapper);
            return wrapper;
        }

        /// <summary>
        /// Applies the native fragment-button classes to a button. The game's own nine-slice button
        /// type is internal in this build, so the panel obtains a real native button from a vanilla
        /// UXML template (see <c>MwpPumpPanelView</c>) and keeps it typed as <see cref="Button"/>;
        /// this method only ensures the native classes are present.
        /// </summary>
        /// <param name="button">Native or fallback button.</param>
        /// <param name="green">True for the native green skin, false for the native red skin.</param>
        public static void ApplyFragmentButtonClasses(Button button, bool green)
        {
            button.AddToClassList(TextClass);
            button.AddToClassList(ButtonClass);
            button.EnableInClassList(ButtonPositiveClass, green);
            button.EnableInClassList(ButtonDangerClass, !green);
        }

        /// <summary>Creates a native game toggle with a localized label.</summary>
        /// <param name="parent">Card that receives the toggle.</param>
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
        /// <param name="label">Status label created with <see cref="CreateStatusRow"/>.</param>
        /// <param name="state">Highest-priority pump state.</param>
        public static void ApplyStatusColor(Label label, PumpState state)
        {
            bool danger = IsDanger(state);
            bool warning = !danger && IsWarning(state);
            label.EnableInClassList(DangerTextClass, danger);
            label.EnableInClassList(WarningTextClass, warning);
        }

        /// <summary>
        /// True when the status is a hard alert, so the card uses the vanilla red-striped alert
        /// frame (the frame the game's own status list uses for warnings).
        /// </summary>
        /// <param name="state">Highest-priority pump state.</param>
        /// <returns>True for alerts.</returns>
        public static bool IsDanger(PumpState state)
        {
            switch (state)
            {
                case PumpState.NoPower:
                case PumpState.NeedsMaintenance:
                case PumpState.Critical:
                case PumpState.MissingMaintenanceMaterials:
                    return true;
                default:
                    return false;
            }
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

        /// <summary>True for states that should read as a warning (yellow) rather than an error (red).</summary>
        /// <param name="state">Highest-priority pump state.</param>
        /// <returns>True for warning states.</returns>
        private static bool IsWarning(PumpState state)
        {
            switch (state)
            {
                case PumpState.NoWater:
                case PumpState.NoAccess:
                case PumpState.StorageFull:
                case PumpState.MaintenanceDue:
                case PumpState.Maintaining:
                case PumpState.NoAccessForMaintenance:
                case PumpState.LowPower:
                    return true;
                default:
                    return false;
            }
        }
    }
}
