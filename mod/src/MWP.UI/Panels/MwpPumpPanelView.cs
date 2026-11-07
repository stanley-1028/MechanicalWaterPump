using System;
using MWP.Core;
using Timberborn.Localization;
using UnityEngine.UIElements;

namespace MWP.UI.Panels
{
    /// <summary>
    /// The UIElements tree of the pump panel plus its one-way rendering path.
    ///
    /// The tree is built exactly once in the constructor (never per frame, REQ-PERF-04); the
    /// labels keep their instances and <see cref="Render"/> only assigns text/values. The
    /// auto-maintenance toggle is updated with <c>SetValueWithoutNotify</c> so a refresh can
    /// never re-trigger the command callbacks, and user input flows out through the delegates
    /// the fragment passes in (each command only affects the selected pump, REQ-UI-07).
    /// </summary>
    public sealed class MwpPumpPanelView
    {
        private readonly VisualElement _root;
        private readonly Label _title;
        private readonly Label _status;
        private readonly Label _hint;
        private readonly Label _rate;
        private readonly Label _powerDemand;
        private readonly Label _powerSupply;
        private readonly Label _waterQuality;
        private readonly Label _depth;
        private readonly Label _inventory;
        private readonly Label _condition;
        private readonly Label _maintenanceIn;
        private readonly Label _materials;
        private readonly Label _pipe;
        private readonly VisualElement _conditionBar;
        private readonly VisualElement _conditionFill;
        private readonly Toggle _autoMaintenance;
        private readonly VisualElement? _autoMaintenanceMark;
        private readonly Button _maintainButton;
        private readonly Button _pauseButton;

        /// <summary>Builds the panel once and wires the three commands (CONTRACT §10).</summary>
        /// <param name="loc">Localization service for the static control labels.</param>
        /// <param name="maintainRequested">Invoked by the "maintain now" button.</param>
        /// <param name="autoMaintenanceChanged">Invoked with the new auto-maintenance value.</param>
        /// <param name="pauseToggled">Invoked by the pause/resume button.</param>
        public MwpPumpPanelView(ILoc loc, Action maintainRequested, Action<bool> autoMaintenanceChanged,
            Action pauseToggled)
        {
            if (loc is null)
            {
                throw new ArgumentNullException(nameof(loc));
            }

            _root = new VisualElement();
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.marginTop = 4f;
            _root.style.display = DisplayStyle.None;

            _title = MwpPumpPanelStyles.AddRow(_root);
            _status = MwpPumpPanelStyles.AddRow(_root);
            _hint = MwpPumpPanelStyles.AddRow(_root);
            MwpPumpPanelStyles.ApplyHint(_hint);
            _rate = MwpPumpPanelStyles.AddRow(_root);
            _powerDemand = MwpPumpPanelStyles.AddRow(_root);
            _powerSupply = MwpPumpPanelStyles.AddRow(_root);
            _waterQuality = MwpPumpPanelStyles.AddRow(_root);
            _depth = MwpPumpPanelStyles.AddRow(_root);
            _inventory = MwpPumpPanelStyles.AddRow(_root);

            _condition = MwpPumpPanelStyles.AddRow(_root);
            _conditionBar = MwpPumpPanelStyles.CreateProgressBar(out VisualElement conditionFill);
            _conditionFill = conditionFill;
            _root.Add(_conditionBar);

            _maintenanceIn = MwpPumpPanelStyles.AddRow(_root);
            _materials = MwpPumpPanelStyles.AddRow(_root);

            _autoMaintenance = new Toggle(loc.T(MwpLocKeys.UiAutoMaintenance));
            MwpPumpPanelStyles.ApplyToggle(_autoMaintenance);
            _autoMaintenance.RegisterValueChangedCallback(evt => autoMaintenanceChanged(evt.newValue));
            // The checkmark is created by the UIElements Toggle constructor; the game theme is
            // expected to draw it, but we always give it a fixed size and a state colour so the
            // "on/off" cue survives a theme without a toggle style.
            _autoMaintenanceMark = _autoMaintenance.Q(null, Toggle.checkmarkUssClassName);
            MwpPumpPanelStyles.PrepareToggleMark(_autoMaintenanceMark);
            _root.Add(_autoMaintenance);

            _maintainButton = new Button(maintainRequested) { text = loc.T(MwpLocKeys.UiMaintainNow) };
            MwpPumpPanelStyles.ApplyButton(_maintainButton);
            _root.Add(_maintainButton);

            _pauseButton = new Button(pauseToggled);
            MwpPumpPanelStyles.ApplyButton(_pauseButton);
            _root.Add(_pauseButton);

            _pipe = MwpPumpPanelStyles.AddRow(_root);
        }

        /// <summary>Root element handed to the entity panel system.</summary>
        public VisualElement Root => _root;

        /// <summary>Shows or hides the whole fragment (other entity types add no rows).</summary>
        /// <param name="visible">True to show the pump rows.</param>
        public void SetVisible(bool visible) =>
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Applies one formatted snapshot to the existing elements.</summary>
        /// <param name="data">Panel payload from <see cref="MwpPumpPanelDataFactory"/>.</param>
        public void Render(in MwpPumpPanelData data)
        {
            if (!data.HasData)
            {
                SetVisible(false);
                return;
            }

            SetText(_title, data.Title);
            _title.tooltip = data.TitleTooltip;
            SetText(_status, data.Status);
            _status.style.color = MwpPumpPanelStyles.StatusColor(data.State);
            SetText(_hint, data.StatusHint);
            _hint.style.display = data.StatusHint.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            SetText(_rate, data.Rate);
            _rate.tooltip = data.TitleTooltip;
            SetText(_powerDemand, data.PowerDemand);
            SetText(_powerSupply, data.PowerSupply);
            _powerSupply.tooltip = data.PowerTooltip;
            SetText(_waterQuality, data.WaterQuality);
            SetText(_depth, data.Depth);
            SetText(_inventory, data.Inventory);
            SetText(_condition, data.Condition);
            MwpPumpPanelStyles.SetProgress(_conditionFill, data.ConditionPercent);
            SetText(_maintenanceIn, data.MaintenanceIn);
            SetText(_materials, data.Materials);
            SetMaintenanceVisible(data.MaintenanceVisible);
            SetText(_pipe, data.Pipe);
            _pipe.tooltip = data.PipeTooltip;
            _autoMaintenance.SetValueWithoutNotify(data.AutoMaintenance);
            MwpPumpPanelStyles.SetToggleMark(_autoMaintenanceMark, data.AutoMaintenance);
            SetText(_pauseButton, data.PauseLabel);
            SetVisible(true);
        }

        private static void SetText(Label label, string text)
        {
            if (!string.Equals(label.text, text, StringComparison.Ordinal))
            {
                label.text = text;
            }
        }

        /// <summary>
        /// Shows or hides the whole maintenance block. With the maintenance system disabled by
        /// config there is no wear and no countdown, so showing "condition 100 %" and a maintain
        /// button would be a half state (REQ-MAINT-13).
        /// </summary>
        private void SetMaintenanceVisible(bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _condition.style.display = display;
            _conditionBar.style.display = display;
            _maintenanceIn.style.display = display;
            _materials.style.display = display;
            _autoMaintenance.style.display = display;
            _maintainButton.style.display = display;
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
