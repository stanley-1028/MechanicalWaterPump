using System;
using MWP.Core;
using Timberborn.CoreUI;
using Timberborn.Localization;
using UnityEngine.UIElements;

namespace MWP.UI.Panels
{
    /// <summary>
    /// The UIElements tree of the pump panel plus its one-way rendering path.
    ///
    /// The visual language is the game's own: native nine-slice sub-panels
    /// (<see cref="NineSliceVisualElement"/> + <c>entity-sub-panel bg-sub-box--green</c>), native
    /// text classes, native <see cref="Timberborn.CoreUI.ProgressBar"/> bars, native
    /// <c>game-toggle</c> toggles and native fragment buttons taken from the vanilla UXML
    /// templates (the game's nine-slice button class is internal in this build, so a real native
    /// button is extracted from a template and re-labelled instead of re-implementing its skin).
    /// No inline colour, font or size is ever set.
    ///
    /// The tree is built exactly once in the constructor (never per frame, REQ-PERF-04); the
    /// labels keep their instances and <see cref="Render"/> only assigns text/values/classes. The
    /// auto-maintenance toggle is updated with <c>SetValueWithoutNotify</c> so a refresh can never
    /// re-trigger the command callbacks, and user input flows out through the delegates the
    /// fragment passes in (each command only affects the selected pump, REQ-UI-07).
    /// </summary>
    public sealed class MwpPumpPanelView
    {
        /// <summary>
        /// Vanilla entity-panel template that contains exactly one <c>entity-fragment__button</c>
        /// (a native nine-slice button). Used as the button factory because the concrete
        /// nine-slice button type is internal to Timberborn.CoreUI.
        /// </summary>
        private const string ButtonTemplateName = "Game/EntityPanel/DemolishableFragment";

        /// <summary>USS class every native fragment button carries.</summary>
        private const string ButtonClass = "entity-fragment__button";

        private readonly VisualElementLoader _loader;
        private readonly VisualElement _root;
        private readonly NineSliceVisualElement _statusSection;
        private readonly Label _status;
        private readonly Label _hint;
        private readonly Label _rate;
        private readonly Label _waterQuality;
        private readonly Label _inventory;
        private readonly Label _depth;
        private readonly Label _pipe;
        private readonly Timberborn.CoreUI.ProgressBar _maintenanceProgressBar;
        private readonly Button _pauseButton;
        private readonly NineSliceVisualElement _maintenanceSection;
        private readonly Timberborn.CoreUI.ProgressBar _conditionBar;
        private readonly Label _condition;
        private readonly Label _maintenanceIn;
        private readonly Label _materials;
        private readonly Toggle _autoMaintenance;
        private readonly Button _maintainButton;

        /// <summary>Builds the panel once and wires the three commands (CONTRACT §10).</summary>
        /// <param name="loc">Localization service for the static control labels.</param>
        /// <param name="loader">Game service that loads the vanilla UXML templates (native buttons).</param>
        /// <param name="maintainRequested">Invoked by the "maintain now" button.</param>
        /// <param name="autoMaintenanceChanged">Invoked with the new auto-maintenance value.</param>
        /// <param name="pauseToggled">Invoked by the pause/resume button.</param>
        public MwpPumpPanelView(ILoc loc, VisualElementLoader loader, Action maintainRequested,
            Action<bool> autoMaintenanceChanged, Action pauseToggled)
        {
            if (loc is null)
            {
                throw new ArgumentNullException(nameof(loc));
            }

            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _root = new VisualElement();
            MwpPumpPanelStyles.SetVisible(_root, false);

            // Status block: the state machine output plus the MWP-only measurement rows.
            _statusSection = MwpPumpPanelStyles.CreateSection();
            _root.Add(_statusSection);
            _status = MwpPumpPanelStyles.CreateStatusRow(_statusSection);
            _hint = MwpPumpPanelStyles.CreateSubTextRow(_statusSection);
            _rate = MwpPumpPanelStyles.CreateTextRow(_statusSection);
            _rate.tooltip = loc.T(MwpLocKeys.TooltipWaterRate);
            _waterQuality = MwpPumpPanelStyles.CreateTextRow(_statusSection);
            _inventory = MwpPumpPanelStyles.CreateTextRow(_statusSection);
            _depth = MwpPumpPanelStyles.CreateTextRow(_statusSection);
            _pipe = MwpPumpPanelStyles.CreateTextRow(_statusSection);
            _pipe.tooltip = loc.T(MwpLocKeys.TooltipPipeCompat);
            _maintenanceProgressBar = MwpPumpPanelStyles.CreateProgressBar(_statusSection, out _, green: true);
            MwpPumpPanelStyles.SetVisible(_maintenanceProgressBar, false);
            _pauseButton = CreateNativeButton(MwpPumpPanelStyles.CreateButtonWrapper(_statusSection),
                loc.T(MwpLocKeys.UiPause), pauseToggled);

            // Maintenance block: only meaningful while the maintenance system is enabled.
            _maintenanceSection = MwpPumpPanelStyles.CreateSection();
            _root.Add(_maintenanceSection);
            _conditionBar = MwpPumpPanelStyles.CreateProgressBar(_maintenanceSection, out Label condition,
                green: false);
            _condition = condition;
            _maintenanceIn = MwpPumpPanelStyles.CreateTextRow(_maintenanceSection);
            _materials = MwpPumpPanelStyles.CreateTextRow(_maintenanceSection);
            _autoMaintenance = MwpPumpPanelStyles.CreateToggle(
                _maintenanceSection, loc.T(MwpLocKeys.UiAutoMaintenance));
            _autoMaintenance.RegisterValueChangedCallback(evt => autoMaintenanceChanged(evt.newValue));
            _maintainButton = CreateNativeButton(MwpPumpPanelStyles.CreateButtonWrapper(_maintenanceSection),
                loc.T(MwpLocKeys.UiMaintainNow), maintainRequested);
        }

        /// <summary>Root element handed to the entity panel system.</summary>
        public VisualElement Root => _root;

        /// <summary>Shows or hides the whole fragment (other entity types add no rows).</summary>
        /// <param name="visible">True to show the pump rows.</param>
        public void SetVisible(bool visible) => MwpPumpPanelStyles.SetVisible(_root, visible);

        /// <summary>Applies one formatted snapshot to the existing elements.</summary>
        /// <param name="data">Panel payload from <see cref="MwpPumpPanelDataFactory"/>.</param>
        public void Render(in MwpPumpPanelData data)
        {
            if (!data.HasData)
            {
                SetVisible(false);
                return;
            }

            SetText(_status, data.Status);
            MwpPumpPanelStyles.ApplyStatusColor(_status, data.State);
            SetText(_hint, data.StatusHint);
            MwpPumpPanelStyles.SetVisible(_hint, data.StatusHint.Length != 0);
            SetText(_rate, data.Rate);
            SetText(_waterQuality, data.WaterQuality);
            SetText(_inventory, data.Inventory);
            MwpPumpPanelStyles.SetVisible(_inventory, data.ShowInventoryRow);
            SetText(_depth, data.Depth);
            MwpPumpPanelStyles.SetVisible(_depth, data.ShowDepthRow);
            SetText(_pipe, data.Pipe);
            MwpPumpPanelStyles.SetProgress(_maintenanceProgressBar, data.MaintainingPercent);
            MwpPumpPanelStyles.SetVisible(_maintenanceProgressBar, data.IsMaintaining);
            SetText(_pauseButton, data.PauseLabel);
            MwpPumpPanelStyles.ApplyPauseColor(_pauseButton, data.Paused);

            MwpPumpPanelStyles.SetVisible(_maintenanceSection, data.MaintenanceVisible);
            SetText(_condition, data.Condition);
            MwpPumpPanelStyles.SetProgress(_conditionBar, data.ConditionPercent);
            SetText(_maintenanceIn, data.MaintenanceIn);
            SetText(_materials, data.Materials);
            _autoMaintenance.SetValueWithoutNotify(data.AutoMaintenance);
            SetVisible(true);
        }

        /// <summary>
        /// Adds a native fragment button to the given wrapper. The vanilla templates already carry
        /// a fully styled nine-slice button (skin, hover states, click sound), so the button is
        /// taken from one of them and only re-labelled; a plain fallback button is used if the
        /// template ever stops providing one.
        /// </summary>
        private Button CreateNativeButton(VisualElement parent, string text, Action clicked)
        {
            Button button = TakeNativeButton() ?? CreateFallbackButton();
            button.text = text;
            button.clicked += clicked;
            parent.Add(button);
            return button;
        }

        private Button? TakeNativeButton()
        {
            try
            {
                VisualElement template = _loader.LoadVisualElement(ButtonTemplateName);
                Button? button = template.Q<Button>(null, ButtonClass);
                if (button is not null)
                {
                    button.RemoveFromHierarchy();
                    return button;
                }
            }
            catch (Exception)
            {
                // A missing/renamed template must never break the panel: fall back to a plain
                // UIElements button with the native fragment-button classes.
            }

            return null;
        }

        private static Button CreateFallbackButton()
        {
            var button = new Button();
            MwpPumpPanelStyles.ApplyFragmentButtonClasses(button);
            return button;
        }

        private static void SetText(Label label, string text)
        {
            if (!string.Equals(label.text, text, StringComparison.Ordinal))
            {
                label.text = text;
            }
        }

        private static void SetText(Button button, string text)
        {
            if (!string.Equals(button.text, text, StringComparison.Ordinal))
            {
                button.text = text;
            }
        }
    }
}
