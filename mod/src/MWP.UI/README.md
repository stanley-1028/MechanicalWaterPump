# MWP.UI — entity panel for the mechanical water pump

This assembly owns everything the player sees on a selected pump: the right-hand entity panel
fragment (status, output, water quality, condition and maintenance rows, maintenance/pause
buttons, pipe-adapter state).

Dependency rule (CONTRACT §0/§10): `MWP.UI` references **MWP.Core only**, never
`MWP.Gameplay`. All data reaches the UI through
`MWP.Core.IMwpPumpQuery.TryGetSnapshot(Guid, out PumpSnapshot)`; all input goes back through
`MWP.Core.IMwpPumpCommands` (`RequestMaintenance`, `SetAutoMaintenance`, `SetPaused`). The two
services are implemented and DI-registered as `[Context("Game")]` singletons by `MWP.Gameplay`.

## Native look & feel (v0.3, T12)

The panel no longer paints its own colours or progress bar. It is built from the game's own
UIElements controls and USS classes, so the game's style sheets (`CoreStyle.uss`,
`CommonStyle.uss`, `EntityPanelCommonStyle.uss`, `EntityPanelGameStyle.uss`, `GameStyle.uss` —
attached to the GameUI/EntityPanel roots) style it exactly like the native fragments. No UXML,
USS, sprite or prefab asset is shipped by the mod.

| Block | Native control | Native classes / template |
|---|---|---|
| Sub-panel frame | `Timberborn.CoreUI.NineSliceVisualElement` (nine-slice background drawn by the game) | `entity-sub-panel bg-sub-box--green` — same as `MechanicalNodeFragment`, `BatteryFragment`, `DeteriorableFragment`, `HaulCandidateFragment` |
| Body text rows | `Label` | `entity-panel__text` (13 px, rgb(204,204,204)) |
| "Next step" hint | `Label` | `entity-panel__sub-text` (12 px) |
| Status colour | native text colour classes only | `game-text-normal` + `text--yellow` (warning) / `game-text--red` (stopped) — the game ships no green/blue/orange text classes, so none are invented |
| Condition bar | `Timberborn.CoreUI.ProgressBar` (the control itself loads `UI/Views/Core/ProgressBar` and draws the native frame/background/fill) | `progress-bar` + label inside — exactly `DeteriorableFragment`'s durability row |
| Maintenance progress bar | same `ProgressBar` | `progress-bar progress-bar--green` — same variant `TimerFragment` uses for its countdown |
| Buttons | a **real native nine-slice button** taken from `Game/EntityPanel/DemolishableFragment` with `VisualElementLoader.LoadVisualElement(...)`, then re-labelled (`Button` base type) | `entity-panel__text entity-fragment__button entity-fragment__button--red` (pause) / `--green` (resume); wrapper `entity-panel__button-wrapper` |
| Auto-maintenance toggle | `UnityEngine.UIElements.Toggle` | `game-toggle entity-panel__text entity-panel__toggle` — exactly `HaulCandidateFragment`/`FillValveFragment` |

Why buttons are extracted instead of constructed: in this game build
`Timberborn.CoreUI.NineSliceButton`/`LocalizableButton` are **internal**, so a mod assembly cannot
`new` them (CS0122). Loading a vanilla template that contains one and keeping it typed as the
public `Button` base gives the genuine control — native skin, hover states, click sound — without
referencing an internal type. If the template ever stops providing a button, the panel falls back
to a plain `Button` carrying the same native classes (the panel never breaks).

## Rows delegated to the game's own fragments

The pump gets the vanilla fragments automatically, so the panel no longer duplicates them:

| Information | Rendered by | How it appears |
|---|---|---|
| Mechanical power (consumer/network rows) | `Timberborn.MechanicalSystemUI.MechanicalNodeFragment` | automatic for any entity with a `MechanicalNode` (the pump blueprint has `MechanicalNodeSpec`) |
| Internal Water stock (amount / limit) | `Timberborn.SimpleOutputBuildingsUI.SimpleOutputInventoryFragment` | needs `SimpleOutputInventoryFragmentEnabler`; the UI configurator contributes it (see below) |
| Intake depth + depth limit controls | `Timberborn.WaterBuildingsUI.WaterInputPipeDepthFragment` | automatic for any entity with `WaterInputPipeSpec` + `WaterInputPipeCoordinates` (the pump blueprint has `WaterInputPipeSpec`) |
| Max depth in the description | `Timberborn.WaterBuildingsUI.WaterInputPipeSpecDescriber` | automatic (`IEntityDescriber`) |

The UI configurator contributes the enabler the same way the vanilla configurators do:

```csharp
MultiBind<TemplateModule>().ToProvider(CreateTemplateModule).AsSingleton();
// ...
builder.AddDecorator<SimpleOutputInventory, SimpleOutputInventoryFragmentEnabler>();
```

If one of those components is ever missing (e.g. a template instantiated before this module was
installed), the pump panel keeps **fallback rows** for the stock (`UI.MWP.Inventory`) and the tier
depth (`UI.MWP.MaxDepth`) so no information is lost. `Power`, `WaterInputPipeCoordinates` and
`SimpleOutputInventoryFragmentEnabler` presence is checked once per selection.

## Rows the MWP panel renders (CONTRACT §10, REQ-UI-01/02/03, REQ-MAINT-10)

Status sub-panel:

1. highest-priority status (`Status.MWP.*`) with the native colour class for its severity
2. grey "next step" hint for NoPower / NoWater / NoAccess (base-game sentences)
3. actual output `UI.MWP.WaterRate` (one decimal, tooltip = `Tooltip.MWP.WaterRate`)
4. water quality `Status.MWP.WaterQuality`
5. fallback stock row `UI.MWP.Inventory` (only when the vanilla inventory fragment is off)
6. fallback depth row `UI.MWP.MaxDepth` (only when the vanilla depth fragment is off)
7. pipe adapter row (`Status.MWP.PipeDirect` / `.PipeValve` / `.PipeUnavailable`, `None`/`Unknown`
   explained by `Tooltip.MWP.PipeCompat`)
8. maintenance-downtime progress bar (green native bar, only while maintaining; the percentage
   itself is in the status text from `PumpSnapshot.MaintenanceProgress`)
9. pause/resume button (`UI.MWP.Pause` / `UI.MWP.Resume`), red while running, green while paused

Maintenance sub-panel (hidden completely when `PumpSnapshot.MaintenanceEnabled` is false,
REQ-MAINT-13):

10. condition bar `UI.MWP.Condition` (native `ProgressBar`, label inside the bar)
11. next maintenance `UI.MWP.MaintenanceIn`
12. required materials `UI.MWP.MaintenanceMaterials` (tier table from `MWP.Core.TierCatalog`, good
    names from the game's `Good.<id>.DisplayName`)
13. auto-maintenance toggle `UI.MWP.AutoMaintenance`
14. "maintain now" button `UI.MWP.MaintainNow`

The building name/icon are not repeated: the native entity-panel header already shows them
(REQ-UI-04 is satisfied by the blueprint's `LabeledEntitySpec`).

Buttons send the selected pump's entity id only (REQ-UI-07); the panel never touches another
pump's state.

## Refresh and allocation behaviour (REQ-PERF-04, §20.3.3)

* `UpdateFragment` is called every frame by the panel system, but it returns immediately until
  `1 / MwpDefaults.UiRefreshHz` (250 ms) elapsed, so the snapshot is read at most 4×/s.
* The snapshot is fingerprinted; the panel only rebuilds its localized strings when the
  fingerprint changed (dirty marker). Steady state allocates nothing per frame.
* The element tree (including the two extracted native buttons) is built once in
  `InitializeFragment`; a render only assigns `text`, class membership, one `ProgressBar.SetProgress`
  value, toggle values (with `SetValueWithoutNotify`) and display visibility — no element is ever
  re-created or re-styled at runtime.
* `ClearFragment` drops the entity id and hides the root, so a closed panel performs no queries.

## Localization

All MWP strings come from `MWP.Core.MwpLocKeys` (CONTRACT §8); the panel adds no keys of its own.
`Localization/MwpUiVanillaKeys.cs` only lists **base-game** keys the panel reuses (three "next
step" sentences and the `Good.<id>.DisplayName` pattern); every one was verified to exist in the
installed game's localization tables, so a raw key can never reach the player (REQ-UI-05).

The previous `TextLocalizationWrapper parameter types have changed` log warning came from using
the shared game key `Unit.HorsePower.NumberAndUnit` once with a `string` argument (our old power
tooltip) while the game's `UnitFormatter` formats it with numeric arguments. That tooltip is gone
(power is rendered by the native mechanical fragment), and every key the panel now calls is called
with one fixed argument-type list, so the wrapper never re-parameterises a key.

## Known gaps (honest list)

1. **No icons / status badges**: no sprite assets are shipped; the status is text + native colour
   class. Colour is never the only signal because the text states the condition.
2. **Pause is the pump's own flag**: the blueprint has no vanilla `PausableBuildingSpec`, so the
   game's pause keybinding does not act on the pump; this panel's button is the only pause path
   and maps to `IMwpPumpCommands.SetPaused`.
3. **Fallback rows are plain text**: if the vanilla inventory/depth fragment cannot appear, the
   rows are `entity-panel__text` lines (no good icon / limit widget) — acceptable for a fallback.
4. **Button extraction depends on a vanilla template**: `Game/EntityPanel/DemolishableFragment`
   must keep containing an `entity-fragment__button`; a renamed/removed template degrades to the
   plain-classes fallback button (logged by the panel only as a silent fallback, never an error).

## In-game verification checklist (screenshots wanted)

1. Select a pump: the panel uses the same nine-slice sub-panel background, 13 px text, native
   button skin and native toggle as other buildings; compare side by side with a vanilla
   mechanical building panel.
2. Confirm the vanilla fragments above it/below it are visible: mechanical power rows, "Intake
   depth", and the Water stock row (amount / limit) — and that the MWP panel does **not** repeat
   power or stock.
3. Disconnect power → status turns red with the native "needs power" hint; reconnect → 250 ms.
4. Let the tank fill → `StorageFull`; drain → `Producing` with the actual Water/h.
5. Contamination → water-quality row changes and output follows.
6. "Maintain now": green native progress bar while maintaining, condition bar back to 100 %;
   toggle the auto-maintenance toggle and verify the native checkmark; with
   `maintenance.enabled = false` the whole maintenance sub-panel disappears.
7. Pause/resume: the button flips between red "Pause" and green "Resume", only this pump stops.
8. Switch language (zhTW/zhCN/enUS): every row translates, no raw keys, and the log contains no
   `TextLocalizationWrapper parameter types have changed` warning.
