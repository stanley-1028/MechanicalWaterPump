# MWP Gameplay/Integration — verified API findings

> Verified 2026-11-04 against the installed game **1.1.2.8-ffc2de0-sw** with
> `mod/tools/ApiIndex` (Mono.Cecil metadata + IL), not from documentation. Re-run any row with:
>
> ```powershell
> dotnet mod/tools/ApiIndex/bin/Release/net9.0/ApiIndex.dll dump "<Managed>" out.txt "<type regex>"
> dotnet mod/tools/ApiIndex/bin/Release/net9.0/ApiIndex.dll  il  "<Managed>" out.txt "<type regex>" "<method regex>"
> ```
>
> `<Managed>` = `C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`.
> Existing dumps in this folder that back the rows below: `il-buildings-access.txt`,
> `il-inventory.txt`, `il-inventory2.txt`, `il-mechanical.txt`, `il-waterinput-body.txt`,
> `il-waterbuildings.txt`, `il-limiter.txt`. Rows marked **(new dump)** were verified this session
> with the command above; the dump text lives in the task report, not in this folder.

## 1. Identity, DI and persistence

| Fact | Evidence |
|---|---|
| `EntityComponent.EntityId` is `System.Guid`; `TickableEntity.EntityId` is `Guid`; there is **no int entity id**. Core interfaces therefore use `Guid` (Lead change). | **(new dump)** `Timberborn.EntitySystem.EntityComponent` / `TickableEntity` |
| `IPersistentEntity.Save(IEntitySaver)/Load(IEntityLoader)`; `IEntitySaver.GetComponent(ComponentKey) → IObjectSaver`; `IEntityLoader.TryGetComponent(ComponentKey, out IObjectLoader)`; absent component is the "new pump" case. | **(new dump)** `Timberborn.WorldPersistence.*` |
| `IObjectLoader.Get(PropertyKey<T>)` **throws `ArgumentOutOfRangeException`** when the key is absent (`SerializedObject.Get` → `TryGet` false → throw), so every read is guarded by `Has(...)` plus a per-field try/catch. No Int64 primitive exists → only `int/float/bool/string` keys. | **(new dump)** `Timberborn.Persistence.IObjectLoader`, `SerializationSystem.SerializedObject::Get` |
| `PropertyKey<T>.ctor(string)`, `ComponentKey.ctor(string)`, `IObjectSaver.Set(PropertyKey<T>, T)` for the four primitives. Keys are constructed once as statics (`MwpSaveKeys`). | **(new dump)** `Timberborn.Persistence.*`, `Timberborn.WorldPersistence.ComponentKey` |
| `ILoadableSingleton.Load()` runs after container creation: `SingletonSystemConfigurator` installs a `SingletonListener` injection/provision listener, so binding a concrete class `AsSingleton()` is enough for its `Load()` to run. `Bind<X>().AsSingleton(); Bind<IFace>().ToExisting<X>();` is the verified same-instance pattern (also used by HDBF). | **(new dump)** `SingletonSystemConfigurator::Configure`, HDBF `NavigationConfigurator` pattern |
| `TemplateModule.Builder.AddDecorator<TSubject,TDecorator>()` exists (generic, no runtime type binding); used by `WaterBuildingsConfigurator`/`FieldsConfigurator`. | **(new dump)** `Timberborn.TemplateInstantiation.TemplateModule+Builder`, `BuildingsConfigurator::ProvideTemplateModule` |

## 2. Tick and game time (why the pump measures `FixedDeltaTimeInHours`)

| Fact | Evidence |
|---|---|
| `TickableComponent.Tick()` is virtual (overridden by every tickable component). | **(new dump)** `TickableComponent::Tick` (no body), `MeteredTickableComponent::Tick` `callvirt` |
| `Ticker.Update` ticks `floor(accumulated / _secondsPerBucket)` buckets; `Ticker.Load` sets `_secondsPerBucket = TickIntervalInSeconds / TotalNumberOfBuckets`; `TickableBucketService` has 128 entity buckets + 1 singleton bucket. An entity therefore gets exactly **one `Tick()` per game tick**. | **(new dump)** `Ticker::Update/Load`, `TickableBucketService::.cctor` |
| `Configurations/TickTime.blueprint.json` sets `TickIntervalInSeconds: 0.6`; `Configurations/DayNightCycle.blueprint.json` sets `ConfiguredDayLengthInTicks: 768`. `DayNightCycle.InitializeConfigurationValues` sets `FixedDeltaTimeInHours = SecondsToHours(TickIntervalInSeconds)`, and `SecondsToHours(s) = s / DayLengthInSeconds * 24` → **one tick = 24/768 = 0.03125 game hours**. `DayNightCycle.Tick` advances one tick. | **(new dump)** `DayNightCycle::SecondsToHours/InitializeConfigurationValues/Tick`; blueprint files read from `StreamingAssets/Modding/Blueprints.zip` |
| Game speed is `Time.timeScale` (`SpeedManager.ChangeSpeed`), i.e. faster speeds run *more game ticks* per real second, each still worth 0.03125 game hours. | **(new dump)** `SpeedManager::ChangeSpeed` |
| Consequence: accumulate `IDayNightCycle.FixedDeltaTimeInHours` every tick (exact game hours, speed independent) and run the arithmetic once `ITickService.TickIntervalInSeconds * ticks >= balance.tickIntervalSeconds` (default 1 s). Water rate is Water **per game hour**, so the 24 game-hour yield check (T02/T27) is exact. | implementation: `MwpPumpController.Tick.cs` |

## 3. Production chain

| Fact | Evidence |
|---|---|
| `MechanicalBuilding.ActiveAndPowered`, `Efficiency` (float, `= MechanicalNode.PowerEfficiency`), `CanUse`, `SetConsumptionDisabled(bool)`; `MechanicalNode.PowerEfficiency = Graph == null ? 0 : Graph.PowerEfficiency`. Supply hp = `Efficiency * requiredHp`, ratio = `Efficiency` clamped 0..1. | `il-mechanical.txt`; `il-power-efficiency.txt` |
| `WaterInput.IsUnderwater = !coordinates.IsBlocked && waterMap.CellIsUnderwater(coords)`; `ContaminationPercentage = waterMap.ColumnContamination(coords)`; `DemandCleanWaterAmount(needed)`: if `needed*3 > buffer` calls `IWaterService.RemoveCleanWater(coords, needed)`, then returns the clean buffer (0 when empty); `RemoveCleanWater(amount)` subtracts from that buffer only; `BufferMultiplier = 3`. Vanilla consumes 0.2 m³ per Water (`WaterGoodToWaterAmountConverter`). | `il-waterinput-body.txt`, `il-waterbuildings.txt`, `il-limiter.txt`; `API_FACTS_MWP.md` §3 |
| MWP withdrawal therefore = `available = DemandCleanWaterAmount(m³)`; `take = min(available, m³)`; `RemoveCleanWater(take)`; granted items = `floor(take / 0.2)` (epsilon guarded). It can never create water that the body/buffer did not supply. | implementation: `MwpPumpController.Ports.cs::Withdraw`, `Tick.cs::ProduceWater` |
| `SimpleOutputInventory.Inventory`; its dedicated decorator adds the `Inventory` component plus hauling/empty-output behaviour, so haulers pick Water up with no extra code. `Inventory`: `GiveProduced(GoodAmount)`, `UnreservedCapacity(string)`, `AmountInStock(string)`, `UnreservedAmountInStock`, `HasUnreservedStock`, `TakeExported(GoodAmount)`, `IsFull`, `Capacity`. | `il-inventory.txt` (`SimpleOutputInventory`, `SimpleOutputInventoryInitializer`, `Inventory`) |
| Note: vanilla `MechanicalPump` is a **WaterMover** (`WaterMoverSpec`/`WaterOutputSpec`, no inventory). MWP intentionally uses the `SimpleOutputInventory` producer path (CONTRACT §4), which is also what the Timber Pipes valve must extract. | blueprint read from `Blueprints.zip` |

## 4. Maintenance materials and reachability

| Fact | Evidence |
|---|---|
| `DistrictInventoryRegistry : BaseComponent` on the district centre: `ActiveInventoriesWithStock(goodId)` → `InventoryRegistry.InventoriesWithStock` → a pre-built `Dictionary<string, HashSet<Inventory>>` lookup wrapped as `ReadOnlyHashSet<Inventory>` (no per-call allocation; all goods pre-seeded in `.ctor`). Only `PublicInput || PublicOutput` inventories are registered, which covers district stockpiles and the pump's output inventory. | **(new dump)** `DistrictInventoryRegistry`, `InventoryRegistry::InventoriesWithStock/RegisterInventory/.ctor` |
| Getting the registry is the verified vanilla pattern: `DistrictBuilding.District → DistrictCenter.GetComponent<DistrictInventoryRegistry>()` (`DistrictInventoryAssigner.AddRegisteredInventories` does exactly this after `GetComponent<DistrictBuilding>()`). | **(new dump)** `DistrictInventoryAssigner::Awake/AddRegisteredInventories` |
| Every building that has `BuildingAccessible` automatically gets `DistrictBuilding`: `GameDistrictsConfigurator.ProvideTemplateModule` calls `AddDecorator<BuildingAccessible, DistrictBuilding>()`. The pump blueprint only needs `BuildingAccessibleSpec`. | **(new dump)** `GameDistrictsConfigurator::ProvideTemplateModule` |
| Road reachability = `DistrictCenter.AccessibleIsOnDistrictRoad(Accessible)`: false unless `District != null && Accessible.UnblockedSingleAccess.HasValue && IDistrictService.IsOnDistrictRoad(District, position)`. This is the same test haulers use, so `NoAccess`/`NoAccessForMaintenance` match actual hauling. | **(new dump)** `DistrictCenter::AccessibleIsOnDistrictRoad` |
| Materials are checked with `UnreservedAmountInStock` (reservation-aware) and removed with `HasUnreservedStock(GoodAmount)` + `TakeExported`; the pump's own inventory is skipped. A failed pre-check consumes nothing (REQ-MAINT-06). | implementation: `MwpPumpController.Ports.cs`; `il-inventory.txt` signatures |

## 5. Custom spec / blueprint deserialization

| Fact | Evidence |
|---|---|
| `SpecTypeCache.GetSpecEntries(type)` yields **only** each `[SpecAlias]` string plus `type.Name` — namespaces are never part of the JSON key. `IsSpecClass` = class && !abstract && `ComponentSpec.IsAssignableFrom`. Hence `[SpecAlias("MWP.MechanicalWaterPumpSpec")]` is mandatory for the dotted CONTRACT §3 key. | **(new dump)** `SpecTypeCache::Create/IsSpecClass/GetSpecEntries`; `SpecAliasAttribute` |
| `BasicDeserializer` recurses element-by-element only for `ImmutableArray<T>` (`DeserializeArray` → `Deserialize(SerializedObject, elementType)`). `T[]`/`List<T>` fall through `SerializedObject.TryGetArray` → `PrimitiveTypeSerialization.Deserialize`, which corrupts custom objects. `AdvancedDeserializer` only runs registered custom deserializers (AssetRef/LocalizedText) and is not needed here. | **(new dump)** `BasicDeserializer::GetValue/DeserializeSingle/DeserializeArray`, `AdvancedDeserializer::Deserialize*` |
| `SerializableTypeExtensions.IsSerializable(type)` = `ComponentSpec` subclass **or** has at least one `[Serialize]` property. Vanilla `BuildingSpec.BuildingCost` is `ImmutableArray<GoodAmountSpec>` with `{Id,Amount}` exactly like MWP's `MaintenanceMaterials`, which is why that type was reused. | **(new dump)** `SerializableTypeExtensions::IsSerializable`; `BuildingSpec` dump |

## 6. Timber Pipes detection (MWP.Integration)

* No `TimberPipes.dll` reference (REQ-COMPAT-04). Detection is one reflective session scan:
  `AppDomain.CurrentDomain.GetAssemblies()` simple names normalised to letters+digits, matched
  against `timberpipes*`, then well-known anchor type names resolved with
  `Assembly.GetType(name, throwOnError: false)`. Every call is individually `try/catch`
  (REQ-SAFE-04). Mod assemblies are loaded from bytes into the same AppDomain
  (`API_FACTS_MWP.md` §1), so a mod assembly is visible to this scan.
* `IPipeAdapter.GetState` mapping (CONTRACT §11): `None` = not detected; `ValveOnly` = detected
  (one-way valve extraction works through the standard Water inventory; direct port not implemented
  in v0.2 → `Direct` never reported); `Error` = scan threw, adapter degrades to the valve path.
  One `MWP/Compat/Scan` line is written at singleton `Load()` (throttled).

## 7. Documented deviations / degradations

1. **`IPipeAdapter` DI binding lives in MWP.Integration** (`[Context("Game")]` configurator). Gameplay may only reference Core (one `ProjectReference`) so it cannot name the adapter type; Bindito scans all loaded configurators, so this is the only clean wiring. Consequence: MWP.Gameplay and MWP.Integration must ship together (they always do).
2. **Config tier table wins** over blueprint spec values for rate/power/capacity/interval/materials (one source of truth, `REQ-NFR-04`); a mismatch is logged once.
3. **Accumulator cap**: clamped to 1 only while production is blocked (pause/no power/no water/full/maintenance). While producing, the fractional carry is kept, otherwise the 20 Water/h tier would lose up to 20% per 1 s step and fail `REQ-BAL-07` (±5%).
4. **Wear** accrues for hours in any *producing* state (Producing, LowPower, MaintenanceDue/Critical, maintenance-warning states), not only literal `Producing`, so low-power play cannot skip maintenance forever (deliberate, documented reading of REQ-MAINT-01).
5. **NoAccess blocks production**: Core's frozen `PumpStateMachine` ranks `NoAccess` above `StorageFull` and zeroes the rate; gameplay follows it.
6. **Withdraw-failure recovery**: >5 consecutive failed withdrawals displays `NoWater` but the pump keeps one retry per step, so it recovers automatically when the water body refills (no permanent lock).
7. **Save**: one component `MWP` per pump entity (no separate pipeline); missing component = new pump; corrupt single pump = `MwpSaveCodec.Sanitize` safe reset (auto-maintenance off) + warning; adapter state is always rebuilt from live detection (`REQ-SAVE-05`). `modVersion` is an additive diagnostic key (absent in old saves is tolerated, not "degraded").
8. **REQ-OPS-04 equivalent** (no console command: the console-command API was not verified, so it was deliberately not implemented): with `features.uiDiagnostics=true`, `MwpRuntime` accumulates O(1) per-step timing and logs one `MWP/Info/Diag` line every 60 s (version, active pump count, adapter state, avg tick ms, samples, legacy-rate flag). Default `false` → zero overhead.
9. No Harmony, no `unsafe`/P-Invoke/`Process.Start`/threads, no external DLL reference, no writes outside the mod directory.

## 8. Open items — must be verified in-game

1. **Water withdrawal/conservation**: `DemandCleanWaterAmount` + `RemoveCleanWater` chain with a thin water body; 1 Water = 0.2 m³; 24 game-hour yield T02/T27 within ±5% (`REQ-BAL-07/08`); buffer behaviour on a fresh pump (first ticks may have an empty input buffer).
2. **District material withdrawal**: `DistrictInventoryRegistry.ActiveInventoriesWithStock` + `UnreservedAmountInStock` + `TakeExported` really deducts Log/Plank/MetalBlock and is visible to haulers (T17/T18).
3. **Reachability**: `DistrictCenter.AccessibleIsOnDistrictRoad` matches hauler reachability after navmesh/district settlement (T08/T19); transient `NoAccess` right after finishing/placement must clear.
4. **Power**: `MechanicalBuilding.Efficiency` linear in supplied hp, monotonic low-power scaling (T03); `SetConsumptionDisabled` really stops the power draw while paused/maintaining.
5. **Timber Pipes**: valve Extract drains the pump inventory into pipes/tanks (MUST, T10); detection transitions `None`/`ValveOnly`/`Error` with/without the mod (T11); direct output remains unimplemented (SHOULD).
6. **Accumulator vs 1 s throttle**: top-tier T02/T27 numbers; fractional accumulator survives save/load.
7. **Maintenance**: 8/10/12/15-day wear, 2 h downtime, blocked material/path states, condition curve, 30-day average (T16–T22).
8. **100 pumps**: tick cost/GC with `uiDiagnostics=true` instrumentation only (T24); steady-state allocation is code-reviewed, not profiled here.
9. **Save round-trip**: missing fields default, corrupt single pump resets, old save without `modVersion` loads, removing the mod still loads the save (T21/T25/T15).
10. **Haulers** pick Water up from `SimpleOutputInventory` without a `WorkplaceSpec`.
