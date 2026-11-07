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
