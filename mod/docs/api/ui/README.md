# UI API evidence (MWP.UI)

Raw `mod/tools/ApiIndex` dumps collected while implementing the pump entity panel, so every
game API the UI uses can be re-checked without launching the game. Game build:
**Timberborn 1.1.2.8-ffc2de0-sw**, managed dir
`C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`.

Regenerate one file with:

```powershell
dotnet mod/tools/ApiIndex/bin/Release/net9.0/ApiIndex.dll dump "<ManagedDir>" docs/api/ui/<out>.txt "<type regex>" ...
dotnet mod/tools/ApiIndex/bin/Release/net9.0/ApiIndex.dll il   "<ManagedDir>" docs/api/ui/<out>.txt "<type regex>" "<method regex>"
```

| File | What it proves for the UI |
|---|---|
| `ui-api-dump.txt` | `EntityPanelModule`/`+Builder` (`AddMiddleFragment`), `IEntityPanelFragment` (4 methods), `ILoc.T` overloads, `Button` (`clicked`/`onClick`, ctors), `Toggle` (`label`, `value`), `Label`, `VisualElement` (style/tooltip/children) |
| `ui-style.txt` | `IStyle` properties used for inline styling, `Length`/`StyleLength`/`StyleColor`/`StyleFloat`/`StyleEnum<T>`, `Color` ctors and statics |
| `ui-enums.txt` | `DisplayStyle`, `FlexDirection`, `WhiteSpace`, `Align`, `Justify`, `LengthUnit` member names |
| `ui-events.txt` | `EventCallback<T>` is a delegate, `ChangeEvent<T>.newValue` (toggle callback signature) |
| `ui-query.txt` | `UQueryExtensions.Q(VisualElement, string name, string className)` used to find the toggle checkmark |
| `ui-entity.txt` | `EntityComponent.EntityId` is `System.Guid`; `BaseComponent.AllComponents`; `EntityService`/registry has no int entity id |
| `ui-coreui.txt` | `Timberborn.CoreUI` controls (`LocalizableButton/Toggle`, `ProgressBar`, `SimpleProgressBar`, `VisualElementLoader`); why the panel cannot reuse the vanilla UXML loader |
| `il-entitypanel.txt` | Panel lifecycle: fragments are initialised once (`AddFragments`), `UpdateFragments` is only called while an entity is shown (`UpdateSingleton`), `Hide` clears fragments → "closed panel does no work" (REQ-PERF-04) |
| `ui-vanilla.txt` | Vanilla fragments `PausableBuildingFragment`/`DeleteBuildingFragment`: they load UXML assets (`VisualElementLoader`), which a mod cannot ship — hence the code-only panel |
| `ui-mechtext.txt`, `il-mechtext.txt` | `MechanicalNodeTextFormatter` shows `Mechanical.PowerInputMaximum` = "{0}/{1} ({2}%)" with args (delivered hp, required hp, percent) |
| `ui-unitfmt.txt`, `il-unitfmt.txt` | `UnitFormatter.PowerUnitLocKey = "Unit.HorsePower.NumberAndUnit"`, the localized hp unit the power tooltip uses |
| `ui-status.txt` | Vanilla status-system types (context only; the panel does not register status icons) |

## Native entity-panel restyle evidence (T12 / v0.3)

| File | What it proves for the native restyle |
|---|---|
| `il-loader.txt` | `VisualElementLoader.LoadVisualElement(name)` resolves `UI/Views/<name>` through the game asset loader, clones the template and runs `VisualElementInitializer` — this is how the panel takes a native button. |
| `il-demolish2.txt` | `Timberborn.DemolishingUI.DemolishableFragment` loads `Game/EntityPanel/DemolishableFragment` and `Q<Button>("Button")` from it — the template the panel borrows its native nine-slice button from. |
| `ui-fragments.txt` | `SimpleOutputInventoryFragment` requires `SimpleOutputInventoryFragmentEnabler`; `MechanicalNodeFragment`/`WaterInputPipeDepthFragment` are the game fragments that render power/depth/inventory for the pump. |
| `il-soinv.txt` | `SimpleOutputInventoryFragment.InitializeFragment` loads its template and `ShowFragment` checks `SimpleOutputInventory` + `SimpleOutputInventoryFragmentEnabler` — hence the TemplateModule decorator in `MwpUiConfigurator`. |
| `il-tmpl.txt` | Vanilla `FieldsUIConfigurator` registers `MultiBind<TemplateModule>().ToProvider(Func<TemplateModule>)` with `AddDecorator<FarmHouse, SimpleOutputInventoryFragmentEnabler>()` — the pattern the UI configurator copies. |
| `il-tmbuilder.txt`, `ui-tmpl.txt` | `TemplateModule.Builder.AddDecorator<TSubject, TDecorator>()` signature. |
| `il-watertmpl.txt` | `WaterInputPipeSpec` → `WaterInputPipe` → `WaterInputPipeCoordinates` decorator chain, which is what makes the vanilla depth fragment appear on the pump. |
| `il-mechnode.txt` | `MechanicalNodeFragment` shows for any entity with a `MechanicalNode` (power rows appear automatically). |
| `il-progressbar.txt`, `ui-coreui.txt` | `Timberborn.CoreUI.ProgressBar` is public and loads `UI/Views/Core/ProgressBar` itself; `NineSliceButton`/`LocalizableButton` are internal (CS0122), which is why the panel extracts a native button instead of constructing one. |
| `ui-bindito.txt` | `IBindingBuilder<T>.ToProvider(Func<T>)` / `IMultiBindingBuilder<T>` used by the configurator. |
| `il-locbtn.txt` | `LocalizableButton` is a Button that draws the nine-slice skin from custom style properties (the same skin the borrowed button has). |
| `toolgroup-facts.md` | (2026-11-13) mod-defined collapsible build-menu group: `BlockObjectToolGroupSpec` fields, spec-only blueprint discovery, tool→group matching/order, icon `AssetRef<Sprite>` resolution for mod PNGs, loc key handling, and the in-game verification steps. |
| `panel-vanilla-facts.md` | (2026-11-13) vanilla entity-panel visual recipe for the MWP panel rewrite: exact class/control per element (status list row, icon rows, progress bars, fragment buttons, explanation box), verified sprite resource paths, recommended card layout and styling traps. |
