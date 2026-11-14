# MWP.UI — entity panel for the mechanical water pump

This assembly owns everything the player sees on a selected pump: the right-hand entity panel
fragment (status, output, water quality, condition and maintenance rows, maintenance/pause
buttons, pipe-adapter state).

Dependency rule (CONTRACT §0/§10): `MWP.UI` references **MWP.Core only**, never
`MWP.Gameplay`. All data reaches the UI through
`MWP.Core.IMwpPumpQuery.TryGetSnapshot(Guid, out PumpSnapshot)`; all input goes back through
`MWP.Core.IMwpPumpCommands` (`RequestMaintenance`, `SetAutoMaintenance`, `SetPaused`). The two
services are implemented and DI-registered as `[Context("Game")]` singletons by `MWP.Gameplay`.

## Vanilla-like panel (原版化, 2026-11-13)

The panel does not paint its own look: it is assembled from the game's own UIElements controls and
USS classes, so the game's style sheets (`CoreStyle`, `CommonStyle`, `EntityPanelCommonStyle`,
`EntityPanelGameStyle`, `GameStyle` — attached to the GameUI/EntityPanel roots) style it exactly
like the native fragments. No UXML, USS, sprite, prefab or texture asset is shipped by the mod:
every icon is loaded from the installed game with `Resources.Load<Sprite>("UI/Images/...")`
(see `MwpPumpPanelIcons`), and a missing sprite simply removes that row's icon instead of throwing.

| Block | Native control | Native classes / template |
|---|---|---|
| Sub-panel card | `Timberborn.CoreUI.NineSliceVisualElement` (nine-slice background drawn by the game) | `entity-sub-panel bg-sub-box--green` — exactly `MechanicalNodeFragment`, `DeteriorableFragment`, `TimerFragment` |
| Alert card | same, class swapped while the status is a hard alert | `bg-sub-box--red-striped` — exactly the vanilla `StatusListFragment` warning frame |
| Status row | `Image` + `Label` inside a `VisualElement`, the vanilla `StatusListElement.uxml` structure | `status-list-element`, `status-list-element__icon` (34 px), `status-list-element__text`; text base `game-text-normal` + native `game-text--red` / `text--yellow` severity colour |
| "Next step" hint | `NineSliceVisualElement` + centred `Label`, the `TimerFragment` mode-description box | `entity-fragment__explanation bg-sub-box--blue`, `entity-panel__text text--centered` — the previous plain `entity-panel__sub-text` line is gone |
| Info rows | `VisualElement` + `Image` + `Label` | `entity-panel__row`, `inventory-row__image` (24 px), `entity-panel__text` — same row layout as the game's informational rows |
| Condition bar | `Timberborn.CoreUI.ProgressBar` (the control loads `UI/Views/Core/ProgressBar` itself) | `progress-bar` + inner label — exactly `DeteriorableFragment`'s durability row |
| Maintenance progress bar | same `ProgressBar` | `progress-bar progress-bar--green` — the variant `TimerFragment` uses for its countdown |
| Auto-maintenance toggle | `UnityEngine.UIElements.Toggle` | `game-toggle entity-panel__text entity-panel__toggle` — exactly `HaulCandidateFragment`/`FillValveFragment` |
| Buttons | a **real native nine-slice button** taken from `Game/EntityPanel/DemolishableFragment` via `VisualElementLoader.LoadVisualElement(...)`, then re-labelled (`Button` base type) | `entity-panel__text entity-fragment__button entity-fragment__button--red` (pause) / `--green` (resume, and "maintain now"); wrapper `entity-panel__button-wrapper` |

Why buttons are extracted instead of constructed: in this game build
`Timberborn.CoreUI.NineSliceButton`/`LocalizableButton` are **internal**, so a mod assembly cannot
`new` them (CS0122). Loading a vanilla template that contains one and keeping it typed as the
public `Button` base gives the genuine control — native skin, hover states, click sound — without
referencing an internal type. Only the colour-variant class is switched (`--red` ↔ `--green`), which
is the same USS rule the game's own templates use. If the template ever stops providing a button,
the panel falls back to a plain `Button` carrying the same native classes (the panel never breaks).

The panel keeps the vanilla entity-panel width (325 px) and has a transparent fragment root: only
the cards own a background, so the entity-panel frame/description above it looks untouched. It adds
no invented section headings: the vanilla fragments have none either, and the building name/icon/
description are the native header's job (REQ-UI-04 is satisfied by the blueprint's
`LabeledEntitySpec`).

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

Status card:

1. highest-priority status (`Status.MWP.*`) in the vanilla status-list row (game status icon + native
   severity colour class); the card switches to the red-striped alert frame for hard alerts
2. "next step" hint in the vanilla blue explanation box: NoPower / NoAccess reuse the base-game
   sentences; NoWater uses MWP's own `UI.MWP.HintNoWater` since 0.2.2 (the vanilla "needs deeper
   water" sentence no longer applies now that the intake depth is effectively unlimited)
3. pause/resume button (`UI.MWP.Pause` / `UI.MWP.Resume`), red while running, green while paused

Measurements card (values no vanilla fragment renders):

4. output `UI.MWP.WaterRate` (one decimal, tooltip = `Tooltip.MWP.WaterRate`) with the vanilla water
   drop sprite
5. water quality `Status.MWP.WaterQuality` with the vanilla contamination sprite
6. fallback stock row `UI.MWP.Inventory` (only when the vanilla inventory fragment is off)
7. fallback depth row `UI.MWP.MaxDepth` (only when the vanilla depth fragment is off)
8. pipe adapter row (`Status.MWP.PipeDirect` / `.PipeValve` / `.PipeUnavailable`, `None`/`Unknown`
   explained by `Tooltip.MWP.PipeCompat`) with the vanilla valve sprite

Maintenance card (hidden completely when `PumpSnapshot.MaintenanceEnabled` is false, REQ-MAINT-13):

9. condition bar `UI.MWP.Condition` (native `ProgressBar`, label inside the bar)
10. maintenance-downtime progress bar (green native bar, only while maintaining)
11. next maintenance `UI.MWP.MaintenanceIn` with the vanilla clock sprite
12. required materials `UI.MWP.MaintenanceMaterials` (tier table from `MWP.Core.TierCatalog`, good
    names from the game's `Good.<id>.DisplayName`) with the vanilla goods sprite
13. auto-maintenance toggle `UI.MWP.AutoMaintenance`
14. "maintain now" button `UI.MWP.MaintainNow` (native **green** fragment button — a positive action)

Buttons send the selected pump's entity id only (REQ-UI-07); the panel never touches another
pump's state.

## Build menu: the mod's own collapsible tool group

The four tiers are not appended to the vanilla Water group anymore; they live in their own
collapsible bottom-bar group (the game collapses a group into one icon and expands it on click,
exactly like the vanilla Water/Food groups). This is data, not UI code:
`mod/data/BlockObjectToolGroups/BlockObjectToolGroup.MWP.blueprint.json`
(`BlockObjectToolGroupSpec`: `Id="MWP"`, `Order=46`, `NameLocKey="ToolGroups.MWP"`,
`Icon="Icons/MWPBasicFolktails"`), plus `ToolGroupId="MWP"` / `ToolOrder=10/20/30/40` in the eight
building blueprints. The validator cross-checks all of it against the Core constants
(`MwpDefaults.ToolGroupId/ToolGroupOrder/ToolGroupIcon`, `MwpLocKeys.ToolGroupName`). No UGUI/IMGUI
code and no vanilla menu override is involved.

## Refresh and allocation behaviour (REQ-PERF-04, §20.3.3)

* `UpdateFragment` is called every frame by the panel system, but it returns immediately until
  `1 / MwpDefaults.UiRefreshHz` (250 ms) elapsed, so the snapshot is read at most 4×/s.
* The snapshot is fingerprinted; the panel only rebuilds its localized strings when the
  fingerprint changed (dirty marker). Steady state allocates nothing per frame.
* The element tree (including the two extracted native buttons and the vanilla sprite cache) is
  built once in `InitializeFragment`; a render only assigns `text`, class membership, one
  `ProgressBar.SetProgress` value, toggle values (with `SetValueWithoutNotify`) and display
  visibility — no element is ever re-created or re-styled at runtime.
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

1. **Row icons depend on vanilla sprite paths**: `MwpPumpPanelIcons` resolves the game's own
   `UI/Images/...` sprites; if a future game build renames one, that row loses only its icon
   (logged nowhere, never throws). The status/row text still states everything.
2. **Pause is the pump's own flag**: the blueprint has no vanilla `PausableBuildingSpec`, so the
   game's pause keybinding does not act on the pump; this panel's button is the only pause path
   and maps to `IMwpPumpCommands.SetPaused`. It is a fragment button, not the vanilla floating
   pause toggle (that toggle is only created for `PausableBuilding` entities).
3. **Fallback rows are plain text**: if the vanilla inventory/depth fragment cannot appear, the
   rows are native icon rows without the game's good/limit widget — acceptable for a fallback.
4. **Button extraction depends on a vanilla template**: `Game/EntityPanel/DemolishableFragment`
   must keep containing an `entity-fragment__button`; a renamed/removed template degrades to the
   plain-classes fallback button (silent fallback, never an error).

## In-game verification checklist (screenshots wanted)

> 2026-11-14 實機回合：世界成功載入、`MWP 0.2.4 ready`、log 0 Exception／0 MWP Error，底欄的
> 收合 MWP 群組圖示已在截圖中確認（`docs/evidence/ui-0.2.4-bottombar.png` /
> `ui-0.2.4-mwp-group-zoom.png`，位於原版 Water 與 Food 之間）。本機無法合成滑鼠／鍵盤輸入
> （`OpenInputDesktop` 拒絕），所以下列需要點擊的項目（展開群組、選取水泵看面板）仍待人工；
> 面板的原版 class／sprite 組成已逐項離線對照 `docs/api/ui/panel-vanilla-facts.md`。

1. Bottom build bar: one **MWP** group icon (Mk I pump render, order 46, next to Water). Click it:
   it expands to the four tiers (Mk I–IV, orders 10/20/30/40); click another group and it
   collapses again — identical behaviour to the vanilla Water group.
2. Select a pump: the panel uses the same nine-slice sub-panel background, 13 px text, native
   row/status layout, native button skin and native toggle as other buildings; compare side by
   side with a vanilla mechanical building panel.
3. Confirm the vanilla fragments above/below it are visible: mechanical power rows, "Intake
   depth", and the Water stock row (amount / limit) — and that the MWP panel does **not** repeat
   power or stock.
4. Disconnect power → the status row shows the vanilla power icon, red severity text and the
   red-striped alert card; reconnect → 250 ms.
5. Let the tank fill → `StorageFull`; drain → `Producing` with the actual Water/h and the vanilla
   productivity icon.
6. Contamination → water-quality row changes and output follows.
7. "Maintain now": green native progress bar while maintaining, condition bar back to 100 %;
   toggle the auto-maintenance toggle and verify the native checkmark; with
   `maintenance.enabled = false` the whole maintenance card disappears.
8. Pause/resume: the button flips between red "Pause" and green "Resume", only this pump stops.
9. Switch language (zhTW/zhCN/enUS): every row translates, including the MWP group name in the
   build bar; no raw keys, and the log contains no
   `TextLocalizationWrapper parameter types have changed` warning.
