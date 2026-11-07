using MWP.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace MWP.UI.Panels
{
    /// <summary>
    /// Code-only styling for the pump panel.
    ///
    /// The mod ships no UXML/USS asset (a mod assembly cannot reference the game's bundles), so
    /// every control that is not a plain label gets explicit inline colors and sizes here. That
    /// keeps the panel visible under the game's panel theme and costs nothing at runtime: all
    /// colors are static readonly values and no style is touched per frame (REQ-PERF-04).
    /// </summary>
    public static class MwpPumpPanelStyles
    {
        private static readonly Color BarTrack = new Color(0.13f, 0.15f, 0.17f, 1f);
        private static readonly Color BarHigh = new Color(0.42f, 0.78f, 0.38f, 1f);
        private static readonly Color BarMedium = new Color(0.93f, 0.78f, 0.28f, 1f);
        private static readonly Color BarLow = new Color(0.90f, 0.55f, 0.22f, 1f);
        private static readonly Color BarCritical = new Color(0.86f, 0.29f, 0.23f, 1f);
        private static readonly Color ButtonBackground = new Color(0.24f, 0.42f, 0.62f, 1f);
        private static readonly Color ButtonBorder = new Color(0.16f, 0.30f, 0.46f, 1f);
        private static readonly Color HintText = new Color(0.72f, 0.75f, 0.78f, 1f);
        private static readonly Color StatusGreen = new Color(0.42f, 0.78f, 0.38f, 1f);
        private static readonly Color StatusBlue = new Color(0.35f, 0.63f, 0.95f, 1f);
        private static readonly Color StatusOrange = new Color(0.93f, 0.60f, 0.24f, 1f);
        private static readonly Color StatusYellow = new Color(0.93f, 0.78f, 0.28f, 1f);
        private static readonly Color StatusRed = new Color(0.86f, 0.29f, 0.23f, 1f);
        private static readonly Color StatusGrey = new Color(0.65f, 0.67f, 0.70f, 1f);
        private static readonly Color ToggleMarkOn = new Color(0.42f, 0.78f, 0.38f, 1f);
        private static readonly Color ToggleMarkOff = new Color(0.20f, 0.22f, 0.24f, 1f);

        /// <summary>Creates one wrapping label row and adds it to the panel.</summary>
        /// <param name="parent">Panel root.</param>
        /// <returns>The created label.</returns>
        public static Label AddRow(VisualElement parent)
        {
            var label = new Label();
            label.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(label);
            return label;
        }

        /// <summary>Styles the grey "next step" hint row.</summary>
        /// <param name="label">Hint label.</param>
        public static void ApplyHint(Label label) => label.style.color = HintText;

        /// <summary>Styles a plain UIElements button so it stays readable without a themed USS asset.</summary>
        /// <param name="button">Button to style.</param>
        public static void ApplyButton(Button button)
        {
            button.style.height = 22f;
            button.style.marginTop = 2f;
            button.style.marginBottom = 2f;
            button.style.paddingLeft = 8f;
            button.style.paddingRight = 8f;
            button.style.backgroundColor = ButtonBackground;
            button.style.color = Color.white;
            button.style.borderTopWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderLeftWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderTopColor = ButtonBorder;
            button.style.borderBottomColor = ButtonBorder;
            button.style.borderLeftColor = ButtonBorder;
            button.style.borderRightColor = ButtonBorder;
            button.style.borderTopLeftRadius = 3f;
            button.style.borderTopRightRadius = 3f;
            button.style.borderBottomLeftRadius = 3f;
            button.style.borderBottomRightRadius = 3f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        /// <summary>Gives a UIElements toggle a predictable height inside the panel column.</summary>
        /// <param name="toggle">Toggle to style.</param>
        public static void ApplyToggle(Toggle toggle)
        {
            toggle.style.height = 20f;
            toggle.style.marginTop = 2f;
            toggle.style.marginBottom = 2f;
        }

        /// <summary>
        /// Gives the toggle's checkmark a fixed size and a dark "off" fill. The game's theme is
        /// expected to draw the checkmark image, but a mod panel cannot rely on it, so
        /// <see cref="SetToggleMark"/> also colours the box as a shape+colour state cue (§12.4).
        /// </summary>
        /// <param name="mark">Checkmark element found inside the toggle, or null when the theme does not create one.</param>
        public static void PrepareToggleMark(VisualElement? mark)
        {
            if (mark is null)
            {
                return;
            }

            mark.style.width = 12f;
            mark.style.height = 12f;
            mark.style.marginLeft = 2f;
            mark.style.marginRight = 2f;
            mark.style.borderTopLeftRadius = 2f;
            mark.style.borderTopRightRadius = 2f;
            mark.style.borderBottomLeftRadius = 2f;
            mark.style.borderBottomRightRadius = 2f;
            SetToggleMark(mark, false);
        }

        /// <summary>Colours the toggle checkmark green when on and dark when off.</summary>
        /// <param name="mark">Checkmark element found inside the toggle, or null.</param>
        /// <param name="on">Current toggle value.</param>
        public static void SetToggleMark(VisualElement? mark, bool on)
        {
            if (mark is not null)
            {
                mark.style.backgroundColor = on ? ToggleMarkOn : ToggleMarkOff;
            }
        }

        /// <summary>Creates the condition progress bar and returns its fill element.</summary>
        /// <param name="fill">The element whose width is the progress value.</param>
        /// <returns>The bar track, ready to be added to the panel.</returns>
        public static VisualElement CreateProgressBar(out VisualElement fill)
        {
            var track = new VisualElement();
            track.style.height = 8f;
            track.style.marginTop = 2f;
            track.style.marginBottom = 4f;
            track.style.backgroundColor = BarTrack;
            track.style.borderTopLeftRadius = 2f;
            track.style.borderTopRightRadius = 2f;
            track.style.borderBottomLeftRadius = 2f;
            track.style.borderBottomRightRadius = 2f;

            fill = new VisualElement();
            fill.style.height = Length.Percent(100f);
            fill.style.width = Length.Percent(0f);
            fill.style.backgroundColor = BarHigh;
            track.Add(fill);
            return track;
        }

        /// <summary>Applies a 0..100 condition value to the bar fill (width + colour).</summary>
        /// <param name="fill">Bar fill element from <see cref="CreateProgressBar"/>.</param>
        /// <param name="percent">Condition in percent.</param>
        public static void SetProgress(VisualElement fill, double percent)
        {
            double clamped = double.IsNaN(percent) || double.IsInfinity(percent)
                ? 0.0
                : Mathf.Clamp((float)percent, 0f, 100f);
            fill.style.width = Length.Percent((float)clamped);
            fill.style.backgroundColor = BarColor(clamped);
        }

        /// <summary>Maps a pump state to the status color required by requirements §12.2.</summary>
        /// <param name="state">Highest-priority pump state.</param>
        /// <returns>Text color for the status row.</returns>
        public static Color StatusColor(PumpState state)
        {
            switch (state)
            {
                case PumpState.Producing:
                    return StatusGreen;
                case PumpState.NoPower:
                case PumpState.NeedsMaintenance:
                    return StatusRed;
                case PumpState.NoWater:
                    return StatusBlue;
                case PumpState.NoAccess:
                case PumpState.Critical:
                case PumpState.MissingMaintenanceMaterials:
                case PumpState.NoAccessForMaintenance:
                    return StatusOrange;
                case PumpState.StorageFull:
                case PumpState.MaintenanceDue:
                case PumpState.LowPower:
                case PumpState.Maintaining:
                    return StatusYellow;
                case PumpState.Paused:
                    return StatusGrey;
                default:
                    return Color.white;
            }
        }

        private static Color BarColor(double percent)
        {
            if (percent >= 75.0)
            {
                return BarHigh;
            }

            if (percent >= 50.0)
            {
                return BarMedium;
            }

            return percent >= 25.0 ? BarLow : BarCritical;
        }
    }
}
