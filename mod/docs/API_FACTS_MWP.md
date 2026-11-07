# MWP — verified Timberborn API facts (1.1.2.8-ffc2de0-sw)

> Ground truth for this mod. Every row below was read from the installed game assemblies
> (`C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`)
> with `mod/tools/ApiIndex` (Mono.Cecil metadata + IL), not from documentation.
> Evidence dumps live in `mod/docs/api/`. If code and this file disagree, re-run ApiIndex.
>
> Verified 2026-11-04. Game version: `1.1.2.8-ffc2de0-sw`.

## 1. Mod loading (proven by HDBF, re-checked here)

| Fact | Detail |
|---|---|
| Manifest | `<ModDir>/manifest.json`; `{Name, Version, Id, MinimumGameVersion, Description, RequiredMods, OptionalMods}` (`Timberborn.Modding.ModManifest`) |
| Assembly loading | `ModManagerScene.ModCodeStarter.LoadAssemblies` loads **every `*.dll` recursively** under the mod dir via `Assembly.Load(File.ReadAllBytes(path))` |
| Entry point | `Timberborn.ModManagerScene.IModStarter.StartMod(IModEnvironment)`; parameterless ctor; all implementations instantiated |
| Environment | `IModEnvironment.ModPath` = mod root (used to read `MWP_Config.json`) |
| Blueprints | mod-local `*.blueprint.json` converted by `Timberborn.ModdingAssets`; localizations `Localizations/*.csv` |
| Faction rule | one blueprint per faction when borrowing vanilla models (materials are faction-scoped) |

## 2. Mechanical power (evidence: `docs/api/il-mechanical.txt`, `il-power2.txt`, `il-mechanical-body.txt`)

`Timberborn.MechanicalSystem.MechanicalBuilding` : `BaseComponent`, implements
`IAwakableComponent, IFinishedStateListener, IFinishedPausable, IBuildingEfficiencyProvider`.

| Member | Meaning |
|---|---|
| `bool ActiveAndPowered { get; }` | `_mechanicalNode.ActiveAndPowered` — finished + connected + powered |
| `bool CanUse { get; }` | alias of `ActiveAndPowered` |
| `float Efficiency { get; }` | returns `_mechanicalNode.PowerEfficiency` |
| `void SetConsumptionDisabled(bool)` | sets the node input multiplier to 0/1 (used while paused/maintaining) |

`MechanicalNode.PowerEfficiency` IL: `Graph == null ? 0 : Graph.PowerEfficiency`.
=> Power ratio r for the pump SHOULD use `MechanicalBuilding.Efficiency` (already 0..1, graph
distributes power) and `ActiveAndPowered` for the zero-power case.

`MechanicalNodeSpec` serialized fields: `PowerOutput:int`, `PowerInput:int`, `IsShaft:bool`.
`MechanicalBuildingSpec`: no serialized fields (marker).
`MechanicalNodeAnimatorSpec`: `MinSpeedMultiplier:float` (vanilla MechanicalPump uses 0.0).

## 3. Water intake and water-body consumption (evidence: `il-waterbuildings.txt`, `il-waterinput-body.txt`, `il-limiter.txt`, `il-mwc.txt`)

`Timberborn.WaterBuildings.WaterInput` (component added by `WaterInputSpec`):

| Member | Detail |
|---|---|
| `bool IsUnderwater { get; }` | `!coordinates.IsBlocked && waterMap.CellIsUnderwater(coordinates)` — **this is the "has water" test** |
| `float ContaminationPercentage { get; }` | `waterMap.ColumnContamination(coordinates)` (0..1) |
| `Vector3Int Coordinates { get; }` | intake cell (from `IWaterInputCoordinates`, usually `WaterInputPipeCoordinates`) |
| `void RemoveCleanWater(float amount)` | decrements the internal clean buffer only |
| `float DemandCleanWaterAmount(float needed)` | if `needed * 3 > buffer` calls `IWaterService.RemoveCleanWater(coords, needed)`; returns current buffer (>0) |
| `void AddWater(clean, contaminated)` | called by `WaterInputService.Tick` every tick with `IWaterRemovalService.GetWaterChangeUnsafe(coords)` |

`WaterInputSpec` serialized field: `WaterInputCoordinates:Vector3Int`.
`WaterInputPipeSpec`: `PipeSegmentPrefabPath:string`, `PipeParentName:string`, `MaxDepth:int`.

**Vanilla consumption chain** (proven by IL of `Timberborn.WaterWorkshops`):
1. `ManufactoryWaterConsumer.UpdateRecipe` sets `ConsumedWater = WaterGoodToWaterAmountConverter.GetWaterAmount(recipe.Products)`.
2. `WaterGoodToWaterAmountConverter` static ctor: `WaterAmountConversion = 0.2f` → **1 Water item = 0.2 m³**
   (confirms requirements §7.3 and §2.2).
3. `WaterInputManufactoryLimiter.MaxProductionProgressChange(delta)`:
   `consumed = ManufactoryWaterConsumer.ConsumedWater; if (consumed > 0) return WaterInput.DemandCleanWaterAmount(delta * consumed) / consumed;`
4. `ManufactoryWaterConsumer.OnProductionProgressed`:
   `WaterInput.RemoveCleanWater(ConsumedWater * ProductionProgressChange)`.
5. `WaterInputManufactoryLimiter.ProductionEfficiency`:
   `IsUnderwater ? (1 - ContaminationPercentage) : 0`, and `<= 0.0001` → 0.

**Consequences for MWP**
- MWP must reproduce this exactly: check `WaterInput.IsUnderwater`, quality `1 - contamination`,
  withdraw `0.2 m³` per Water item via `WaterInput.DemandCleanWaterAmount` + `RemoveCleanWater`.
- **Requirements conflict (DV-01)**: §3.4 says `q = clamp(1 − 0.75c, 0.25, 1)`, but vanilla is
  `q = 1 − c` (threshold 0.0001). Using 1 − 0.75c would make MWP *more* efficient than vanilla
  under contamination, violating `REQ-BAL-04`. **Default ships vanilla-equivalent values**
  (`badwaterPenalty = 1.0`, `minWaterQuality = 0.0`); the §3.4 values stay available in config.
  Validator + docs record this decision.

## 4. Output inventory and hauling (evidence: `il-inventory.txt`, `il-inventory2.txt`)

`Timberborn.SimpleOutputBuildings.SimpleOutputInventorySpec`: `Capacity:int`, `IgnorableCapacity:bool`.
Decorator chain (`SimpleOutputInventoryInitializer`) adds `Inventory` plus haul behaviour and
empty-output behaviour, so water is haulable by district haulers with **no extra code**.

`SimpleOutputInventory` component: `Inventory Inventory { get; }`.

`Timberborn.InventorySystem.Inventory` (component) relevant members:

| Member | Use |
|---|---|
| `int Capacity`, `int TotalAmountInStock`, `bool IsFull`, `bool IsEmpty` | state + StorageFull |
| `int AmountInStock(string goodId)`, `int UnreservedCapacity(string goodId)` | free space / stock |
| `void GiveProduced(GoodAmount good)` | insert produced Water |
| `bool HasUnreservedStock(string goodId)`, `void TakeExported(GoodAmount good)` | maintenance material withdrawal from district stock |
| `GoodAmount` is `readonly struct` with ctor `(string goodId, int amount)` | — |

## 5. Buildings, entrance, accessibility (evidence: `il-buildings-access.txt`)

| Spec | Fields actually serialized |
|---|---|
| `BuildingSpec` | `SelectionSoundName, LoopingSoundName, BuildingCost[], ScienceCost, PlaceFinished, FinishableWithBeaversOnSite, DrawRangeBoundsOnIt` |
| `BlockObjectSpec` | `Size{X,Y,Z}`, `Blocks[]` (length = X*Y*Z, row-major: `n -> (x=n%X, y=(n/X)%Y, z=n/(X*Y))`), `Entrance{HasEntrance,Coordinates}`, `BaseZ, Overridable, Flippable` |
| `PlaceableBlockObjectSpec` | `ToolGroupId, ToolOrder, ToolShape, Layout, CustomPivot{HasCustomPivot,Coordinates}, CanBeAttachedToTerrainSide, DevModeTool` |
| `BuildingAccessibleSpec` | `LocalAccess{X,Y,Z}, ForceOneFinalAccess` |
| `BlockObjectNavMeshSettingsSpec` | `NoAutoWalls, GenerateFloorsOnStackable, EdgeGroups[], UnblockedCoordinates[], BlockedEdges[]` |
| `TemplateSpec` | `TemplateName, BackwardCompatibleTemplateNames[], RequiredFeatureToggle, DisablingFeatureToggle` |
| `LabeledEntitySpec` | `DisplayNameLocKey, DescriptionLocKey, FlavorDescriptionLocKey, Icon` |
| `ConstructionSiteProgressVisualizerSpec` | `ProgressThresholds[]` |
| `WaterObstacleSpec` | `Coordinates[]{X,Y}` |
| `FinishableWaterObstacleSpec` | `Height:float` |

Block authoring rules (verified in HDBF, same game build): **no vanilla building has a fully solid
footprint**. The entrance row is solid (`MatterBelow: GroundOrStackable`, `Occupations:
"Bottom, Top, Corners, Path, Middle"`), interior rows are `MatterBelow: Any` + `Occupations:
"Floor, Bottom, Corners, Path, Middle"`; blocks that overhang must be `Any`.

## 6. Persistence (evidence: HDBF ch.6 + `il-*` dumps; re-verified by gameplay task)

| Fact | Consequence |
|---|---|
| `IPersistentEntity.Save(IEntitySaver) / Load(IEntityLoader)` | store pump state in component `MWP` |
| `IObjectLoader.Get(PropertyKey<T>)` **throws `ArgumentOutOfRangeException`** when absent | guard with `Has(...)` |
| `IObjectLoader.Get<T>` generic is constrained to `System.Enum` | read int/float/bool via dedicated overloads |
| `IEntityLoader.TryGetComponent(ComponentKey, out IObjectLoader)` | — |
| No Int64 primitive | persist ids as invariant strings |
| DI-bound types must have exactly **one** constructor | all components/configurators |
| `IObjectSaver` has no dictionary setter | parallel `ListKey<string>` lists |

## 7. Known build-environment quirks

1. `dotnet build` must run with `-m:1` (parallel restore fails silently on this machine).
2. A `.csproj` may declare at most **one** `ProjectReference`. Graph: `Core <- Gameplay / Integration / UI`.
3. No network access for NuGet; only the local package cache is usable. Tests ship **zero** NuGet
   dependencies and use a built-in runner. Mono.Cecil 0.11.5 comes from the local cache
   (`%UserProfile%\.nuget\packages\mono.cecil\0.11.5`).
4. `TreatWarningsAsErrors=true` applies to all MWP projects; staging build must be 0 warnings.

## 8. Pending in-game verification (cannot be automated offline)

These stay open until someone launches the game; the acceptance table in
`docs/VERIFICATION.md` tracks them.

1. Does `ScienceCost` alone place the four tiers in the unlock UI (no custom research node)?
2. Mechanical graph behaviour under partial supply: is `Efficiency` linear in supplied hp?
3. Timber Pipes recognition of MWP's `SimpleOutputInventory` Water for valve Extract (MUST path).
4. Actual 24h yields (T02/T03/T27), maintenance downtime effect (T16–T22), 100-pump FPS (T24).
5. Haulers pick up Water from the pump's inventory without a `WorkplaceSpec`.
