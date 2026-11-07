# MWP.UI — entity panel for the mechanical water pump

This assembly owns everything the player sees on a selected pump: the right-hand entity panel
fragment (status, output, power, water quality, inventory, condition progress bar, maintenance
rows and buttons, pipe-adapter state).

Dependency rule (CONTRACT §0/§10): `MWP.UI` references **MWP.Core only**, never
`MWP.Gameplay`. All data reaches the UI through
`MWP.Core.IMwpPumpQuery.TryGetSnapshot(Guid, out PumpSnapshot)`; all input goes back through
`MWP.Core.IMwpPumpCommands` (`RequestMaintenance`, `SetAutoMaintenance`, `SetPaused`). The two
services are implemented and DI-registered as `[Context("Game")]` singletons by `MWP.Gameplay`.

## What is wired (verified extension points)

The panel is code-only: no UXML, USS, sprite or prefab asset is shipped (a compiled mod assembly
cannot reference the game's asset bundles). Every API below was verified with
`tools/ApiIndex` against Timberborn **1.1.2.8-ffc2de0-sw**; dumps are in `docs/api/ui/`.

| Piece | Game API |
|---|---|
| Configuration | `[Bindito.Core.Context("Game")] MwpUiConfigurator : Bindito.Core.Configurator` |
| Fragment contract | `Timberborn.EntityPanelSystem.IEntityPanelFragment` (`InitializeFragment`, `ShowFragment`, `ClearFragment`, `UpdateFragment`) |
| Panel contribution | `MultiBind<EntityPanelModule>().ToProvider<MwpEntityPanelModuleProvider>().AsSingleton()` |
| Module building | `new EntityPanelModule.Builder().AddMiddleFragment(fragment, 1).Build()` |
| Localization | `Timberborn.Localization.ILoc.T(key[, arg1..arg3])` |
| Selected entity id | `Timberborn.EntitySystem.EntityComponent.EntityId` (`System.Guid`), found through `BaseComponent.AllComponents` |
| Controls | `UnityEngine.UIElements.{Label, Button, Toggle, VisualElement}` + inline `IStyle` |
| Refresh clock | `UnityEngine.Time.unscaledTime` |

The fragment renders nothing for an entity without a readable pump snapshot, so other buildings
get no extra rows.

## Rows (CONTRACT §10, REQ-UI-01/02/03, REQ-MAINT-10)

1. tier display name (`Building.MWP.Pump.<Tier>.DisplayName`), tooltip = rated-rate text
2. highest-priority status (`Status.MWP.*`) coloured per requirements §12.2 (+ a grey
   "next step" hint for NoPower / NoWater / NoAccess, reusing the base game's own sentences)
3. actual output `UI.MWP.WaterRate` (one decimal) and power demand/supply
   (`Status.MWP.PowerDemand`, `Status.MWP.PowerSupply`; delivered hp in the row tooltip via
   `Mechanical.PowerInputMaximum`)
4. water quality `Status.MWP.WaterQuality`
5. intake depth `UI.MWP.MaxDepth` (tier `MaxDepth` from `MWP.Core.TierCatalog`, requirements §12.1)
6. internal inventory `UI.MWP.Inventory` (current / capacity)
7. condition `UI.MWP.Condition` + progress bar (colour bands at 75/50/25); the whole maintenance
   block (condition bar, countdown, materials, auto toggle, maintain button) is hidden when config
   disables the maintenance system (`PumpSnapshot.MaintenanceEnabled`, REQ-MAINT-13)
8. next maintenance `UI.MWP.MaintenanceIn`, required materials `UI.MWP.MaintenanceMaterials`
   (tier table from `MWP.Core.TierCatalog`, good names from the game's `Good.<id>.DisplayName`)
9. auto-maintenance `Toggle` `UI.MWP.AutoMaintenance`, "maintain now" `Button`
   `UI.MWP.MaintainNow`, pause/resume `Button` `UI.MWP.Pause` / `UI.MWP.Resume`
10. pipe adapter row: `Status.MWP.PipeDirect` / `.PipeValve` / `.PipeUnavailable`, with the
    "Timber Pipes absent" case explained by `Tooltip.MWP.PipeCompat`

While the pump is maintaining, the status row uses `Status.MWP.Maintaining` with
`PumpSnapshot.MaintenanceProgress × 100` (elapsed/total downtime, §20.2.5), not the condition.

Buttons send the selected pump's entity id only (REQ-UI-07); the panel never touches another
pump's state.

## Refresh and allocation behaviour (REQ-PERF-04, §20.3.3)

* `UpdateFragment` is called every frame by the panel system, but it returns immediately until
  `1 / MwpDefaults.UiRefreshHz` (250 ms) elapsed, so the snapshot is read at most 4×/s.
* The snapshot is fingerprinted; the panel only rebuilds its localized strings when the
  fingerprint changed (dirty marker). Steady state therefore allocates nothing per frame and
  does no string formatting.
* The element tree is built once in `InitializeFragment`; a render only assigns `text`, colours,
  one bar width and two toggle values (with `SetValueWithoutNotify`, so a refresh can never
  re-enter the command callbacks).
* `ClearFragment` drops the entity id and hides the root, so a closed panel performs no queries.

## Localization

All MWP strings come from `MWP.Core.MwpLocKeys` (CONTRACT §8); the panel adds no keys of its
own. `Localization/MwpUiVanillaKeys.cs` only lists **base-game** keys the panel reuses (three
"next step" sentences, the power-detail tooltip and the `Good.<id>.DisplayName` pattern); every
one of them was verified to exist in the installed game's localization tables, so a raw key can
never reach the player (REQ-UI-05).

## Known gaps (honest list)

1. **Supply hp is a tooltip, not a row**: the MWP key set has `Status.MWP.PowerSupply` as a
   percentage only, so the delivered hp is attached as the row tooltip.
2. **No icons**: requirement §12.4 asks for shape+colour cues; status is text + colour only
   (no sprite assets are shipped). Colour is never the only signal because the text states the
   condition.
3. **Pause uses the pump's own flag**: the pump blueprint intentionally has no vanilla
   `PausableBuildingSpec`, so the game's own pause toggle/keybinding does not act on it; the
   panel's pause button is the only pause path and maps to `IMwpPumpCommands.SetPaused`.
4. **Depth is the tier maximum, not the live intake cell**: `PumpSnapshot` carries no intake
   coordinate, so the row shows the tier's rated `MaxDepth` (the only depth data a mod can read
   without touching the gameplay assembly).

## In-game verification checklist

1. Build the mod package, enable it, place a Mk I pump; select it: all rows appear (including
   `Intake depth: 2 blocks` for Mk I), other buildings show no MWP rows.
2. Disconnect the mechanical network → status becomes `NoPower` (red) + power hint; connect it
   and verify the supply percentage and delivered hp tooltip update within 250 ms.
3. Let the tank fill → `StorageFull`; drain it → `Producing` with the actual Water/h.
4. Wait for contamination → water-quality percentage drops and the output changes.
5. Force maintenance: click "maintain now" → materials row matches the tier table, the status
   row shows `Maintaining: n%` climbing from 0 to 100, then `Producing`, condition bar 100 %.
   With `maintenance.enabled = false` in `MWP_Config.json` the whole maintenance block
   (condition bar, countdown, materials, auto toggle, maintain button) disappears.
6. Toggle auto-maintenance off, click pause, verify only this pump pauses (other pumps keep
   running) and the button labels switch Pause ↔ Resume.
7. Switch UI language (zhTW/zhCN/enUS): all rows translate, no `MWP.` or `Status.` key text.
8. Open the same panel with Timber Pipes installed and absent: pipe row shows valve/direct vs
   the "requires Timber Pipes" tooltip.
