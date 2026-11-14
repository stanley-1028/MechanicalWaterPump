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
    /// The visual language is the game's own and nothing else: vanilla nine-slice sub-panel cards
    /// (<see cref="NineSliceVisualElement"/> with <c>entity-sub-panel bg-sub-box--green</c> /
    /// <c>bg-sub-box--red-striped</c>), the vanilla <c>status-list-element</c> icon+text row for
    /// the status, vanilla <c>entity-panel__row</c> rows with the game's own 24 px
    /// <c>inventory-row__image</c> sprites, the vanilla blue explanation box for the "next step"
    /// hint, native <see cref="Timberborn.CoreUI.ProgressBar"/> bars, a native <c>game-toggle</c>
    /// toggle, and real native nine-slice buttons taken from a vanilla UXML template (the game's
    /// concrete button type is internal in this build). No inline colour, font or size is ever set.
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
        private readonly VisualElement _statusIcon;
        private readonly Label _status;
        private readonly VisualElement _hintBox;
        private readonly Label _hint;
        private readonly Button _pauseButton;

        private readonly NineSliceVisualElement _measurementSection;
        private readonly Label _rate;
        private readonly Label _waterQuality;
        private readonly Label _inventory;
        private readonly VisualElement _inventoryRow;
        private readonly Label _depth;
        private readonly VisualElement _depthRow;
        private readonly Label _pipe;

        private readonly NineSliceVisualElement _maintenanceSection;
        private readonly Timberborn.CoreUI.ProgressBar _conditionBar;
        private readonly Label _condition;
        private readonly Timberborn.CoreUI.ProgressBar _maintenanceProgressBar;
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

            // Status card: the vanilla status-list row (icon + text), the "next step" explanation
            // box and the pause/resume button.
            _statusSection = MwpPumpPanelStyles.CreateSection(alert: false);
            _root.Add(_statusSection);
            _status = MwpPumpPanelStyles.CreateStatusRow(_statusSection, out _statusIcon);
            _hint = MwpPumpPanelStyles.CreateExplanationBox(_statusSection);
            _hintBox = _hint.parent;
            _pauseButton = CreateNativeButton(MwpPumpPanelStyles.CreateButtonWrapper(_statusSection),
                loc.T(MwpLocKeys.UiPause), green: false, pauseToggled);

            // Measurements card: the values no vanilla fragment renders (actual output, water
            // quality, pipe adapter) plus the fallback stock/depth rows.
            _measurementSection = MwpPumpPanelStyles.CreateSection(alert: false);
            _root.Add(_measurementSection);
            _rate = MwpPumpPanelStyles.CreateIconRow(_measurementSection, MwpPumpPanelIcons.Water, out _);
            _rate.tooltip = loc.T(MwpLocKeys.TooltipWaterRate);
            _waterQuality = MwpPumpPanelStyles.CreateIconRow(_measurementSection,
                MwpPumpPanelIcons.WaterQuality, out _);
            _inventory = MwpPumpPanelStyles.CreateIconRow(_measurementSection, MwpPumpPanelIcons.Water, out _);
            _inventoryRow = _inventory.parent;
            MwpPumpPanelStyles.SetVisible(_inventoryRow, false);
            _depth = MwpPumpPanelStyles.CreateIconRow(_measurementSection, MwpPumpPanelIcons.Depth, out _);
            _depthRow = _depth.parent;
            MwpPumpPanelStyles.SetVisible(_depthRow, false);
            _pipe = MwpPumpPanelStyles.CreateIconRow(_measurementSection, MwpPumpPanelIcons.Pipe, out _);
            _pipe.tooltip = loc.T(MwpLocKeys.TooltipPipeCompat);

            // Maintenance card: condition bar (DeteriorableFragment pattern), countdown/material
            // rows, the downtime progress bar (TimerFragment pattern), the auto toggle and button.
            _maintenanceSection = MwpPumpPanelStyles.CreateSection(alert: false);
            _root.Add(_maintenanceSection);
            _conditionBar = MwpPumpPanelStyles.CreateProgressBar(_maintenanceSection, out Label condition,
                green: false);
            _condition = condition;
            _maintenanceProgressBar = MwpPumpPanelStyles.CreateProgressBar(_maintenanceSection, out _,
                green: true);
            MwpPumpPanelStyles.SetVisible(_maintenanceProgressBar, false);
            _maintenanceIn = MwpPumpPanelStyles.CreateIconRow(_maintenanceSection, MwpPumpPanelIcons.Clock,
                out _);
            _materials = MwpPumpPanelStyles.CreateIconRow(_maintenanceSection, MwpPumpPanelIcons.Goods, out _);
            _autoMaintenance = MwpPumpPanelStyles.CreateToggle(
                _maintenanceSection, loc.T(MwpLocKeys.UiAutoMaintenance));
            _autoMaintenance.RegisterValueChangedCallback(evt => autoMaintenanceChanged(evt.newValue));
            _maintainButton = CreateNativeButton(MwpPumpPanelStyles.CreateButtonWrapper(_maintenanceSection),
                loc.T(MwpLocKeys.UiMaintainNow), green: true, maintainRequested);
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

            ApplyStatus(in data);

            SetText(_rate, data.Rate);
            SetText(_waterQuality, data.WaterQuality);
            SetText(_inventory, data.Inventory);
            MwpPumpPanelStyles.SetVisible(_inventoryRow, data.ShowInventoryRow);
            SetText(_depth, data.Depth);
            MwpPumpPanelStyles.SetVisible(_depthRow, data.ShowDepthRow);
            SetText(_pipe, data.Pipe);

            SetText(_pauseButton, data.PauseLabel);
            MwpPumpPanelStyles.ApplyPauseColor(_pauseButton, data.Paused);

            MwpPumpPanelStyles.SetVisible(_maintenanceSection, data.MaintenanceVisible);
            SetText(_condition, data.Condition);
            MwpPumpPanelStyles.SetProgress(_conditionBar, data.ConditionPercent);
            MwpPumpPanelStyles.SetProgress(_maintenanceProgressBar, data.MaintainingPercent);
            MwpPumpPanelStyles.SetVisible(_maintenanceProgressBar, data.IsMaintaining);
            SetText(_maintenanceIn, data.MaintenanceIn);
            SetText(_materials, data.Materials);
            _autoMaintenance.SetValueWithoutNotify(data.AutoMaintenance);
            SetVisible(true);
        }

        /// <summary>
        /// Renders the vanilla status-list row: the game's own status sprite for the severity, the
        /// native text colour class, and the red-striped alert card frame for hard alerts. The icon
        /// is a background image on the status icon element, exactly how the vanilla
        /// <c>StatusListFragment</c> assigns <c>StatusInstance.IconSmall</c>.
        /// </summary>
        private void ApplyStatus(in MwpPumpPanelData data)
        {
            SetText(_status, data.Status);
            MwpPumpPanelStyles.ApplyStatusColor(_status, data.State);

            UnityEngine.Sprite? sprite = MwpPumpPanelIcons.Get(MwpPumpPanelIcons.StatusIcon(data.State));
            if (sprite is null)
            {
                MwpPumpPanelStyles.SetVisible(_statusIcon, false);
            }
            else
            {
                _statusIcon.style.backgroundImage = new StyleBackground(sprite);
                MwpPumpPanelStyles.SetVisible(_statusIcon, true);
            }

            MwpPumpPanelStyles.SetSectionTone(_statusSection, MwpPumpPanelStyles.IsDanger(data.State));

            SetText(_hint, data.StatusHint);
            MwpPumpPanelStyles.SetVisible(_hintBox, data.StatusHint.Length != 0);
        }

        /// <summary>
        /// Adds a native fragment button to the given wrapper. The vanilla templates already carry
        /// a fully styled nine-slice button (skin, hover states, click sound), so the button is
        /// taken from one of them and only re-labelled; a plain fallback button is used if the
        /// template ever stops providing one.
        /// </summary>
        private Button CreateNativeButton(VisualElement parent, string text, bool green, Action clicked)
        {
            Button button = TakeNativeButton() ?? new Button();
            MwpPumpPanelStyles.ApplyFragmentButtonClasses(button, green);
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
