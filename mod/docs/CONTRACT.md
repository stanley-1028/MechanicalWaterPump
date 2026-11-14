# MWP 實作契約（Implementation Contract）

> 本檔是團隊唯一介面契約。**未經 Lead 同意不得更改公開簽名、JSON key、存檔 key、本地化 key 或
> Blueprint 規格**；需要變更時先通知 Lead，由 Lead 更新本檔與 `MwpDefaults`。
> 需求來源：`機械水泵模組需求文檔.md`（以 §20 為準）。API 事實：`mod/docs/API_FACTS_MWP.md`。

## 0. 寫入範圍（互斥）

| 角色 | 擁有路徑 | 不得修改 |
|---|---|---|
| Lead | `mod/Directory.Build.*`, `mod/*.csproj` 以外建置檔、`mod/build.ps1`, `mod/docs/**`, `mod/data/manifest.json`, `mod/src/MWP.Core/Abstractions/**`, `MwpDefaults.cs`, `TierDefinition.cs`, `Logging/IMwpLog.cs` | 隊友實作檔（衝突時先協調） |
| core-dev | `mod/src/MWP.Core/**`（除上述 Lead 檔）、`mod/tests/MWP.Tests/**` | Gameplay/Integration/UI/data |
| gameplay-dev | `mod/src/MWP.Gameplay/**`, `mod/src/MWP.Integration/**` | Core/UI/data/tools |
| data-dev | `mod/data/**`（manifest 除外）, `mod/tools/validate/**` | 程式碼 project、build.ps1 |
| ui-dev | `mod/src/MWP.UI/**` | Core/Gameplay/data |

## 1. 建置與驗證指令

```powershell
# 全部建置 + 測試 + 資料驗證 + 打包（Lead 執行；隊友可只建自己專案）
pwsh -File mod/build.ps1 -Clean

# 單一專案（注意 -m:1）
dotnet build mod/src/MWP.Gameplay/MWP.Gameplay.csproj -c Release -m:1
dotnet run --project mod/tests/MWP.Tests/MWP.Tests.csproj -c Release -m:1
dotnet run --project mod/tools/validate/validate.csproj -c Release -m:1 -- mod/data --managed "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed"
```

規則：`.csproj` 最多一個 `ProjectReference`；`TreatWarningsAsErrors=true`（0 warning）；
Gameplay/Integration/UI 目標 `netstandard2.1` 並引用遊戲組件（`Directory.Build.targets`）；
Core 不得引用遊戲組件；工具與測試 `net9.0`、零 NuGet 依賴（Mono.Cecil 例外，走本機快取）。

## 2. Core 公開 API（已凍結）

已由 Lead 寫入：`Abstractions/MwpEnums.cs`、`Abstractions/GoodStack.cs`、
`Abstractions/PumpSnapshot.cs`、`Abstractions/Ports.cs`、`Abstractions/IMwpPumpCommands.cs`、
`Balance/TierDefinition.cs`（含 `TierCatalog.Default`）、`MwpDefaults.cs`。

**Entity id 型別（已用遊戲 IL 證實）**：`Timberborn.EntitySystem.EntityComponent.EntityId` 是
`System.Guid`，遊戲沒有 int entity id。因此 `IMwpPumpQuery.TryGetSnapshot(Guid, out PumpSnapshot)`
與 `IMwpPumpCommands.RequestMaintenance/SetAutoMaintenance/SetPaused(Guid, …)` 全部使用
`System.Guid`；Gameplay 以 Guid 建索引，UI 傳 `EntityComponent.EntityId`。

core-dev 必須新增（簽名不可改；實作＋XML doc＋測試）：

```csharp
namespace MWP.Core
{
    // Balance/BalanceFormulas.cs
    public static class BalanceFormulas
    {
        public static double PowerRatio(double supplyHp, double requiredHp);
        public static double WaterQuality(double contamination, double badwaterPenalty, double minQuality);
        public static double ConditionMultiplier(double condition);            // 75..100→1, 50..75→.9, 25..50→.75, 1..25→.5, 0→0
        public static double WearPerHour(double maintenanceIntervalDays);      // 100/(days*24)
        public static double EffectiveWaterPerHour(double baseRate, double powerRatio, double quality, double conditionMultiplier);
        public static int WaterItemsProduced(double accumulator, int freeCapacity); // floor, capped, >=0
        public static double CubicMetersFor(int waterItems, double cubicMetersPerWater);
        public static double MaintenanceDaysLeft(double condition, double intervalDays);
        public static double Sanitize(double value, double fallback, double min, double max); // NaN/Inf/負→clamp
    }

    // Balance/BalanceDefaults.cs
    public sealed class BalanceSettings   // from config; defaults = TierCatalog + 0.2 m³ + vanilla quality
    {
        public bool UseLegacyRates { get; }
        public double BadwaterPenalty { get; }      // default 1.0 (vanilla-equivalent, DV-01)
        public double MinWaterQuality { get; }      // default 0.0
        public double CubicMetersPerWater { get; }  // default 0.2
        public double TickIntervalSeconds { get; }  // default 1.0
        public IReadOnlyList<TierDefinition> Tiers { get; }
        public static BalanceSettings Default { get; }
        public static BalanceSettings WithLegacyRates(); // 1/3/6/9 for T26 comparison only
    }

    // State/PumpStateMachine.cs
    public readonly struct PumpStatusInput
    {
        public PumpStatusInput(bool isFinished, bool isPaused, bool powered, double supplyHp, int requiredHp,
            bool hasWater, int freeCapacity, bool isAccessible, MaintenancePhase maintenancePhase, double condition);
        // properties for all of the above
    }
    public readonly struct PumpStatus
    {
        public PumpState State { get; }
        public double PowerRatio { get; }
        public double WaterQuality { get; }
        public double ConditionMultiplier { get; }
        public double EffectiveWaterPerHour { get; }
    }
    public static class PumpStateMachine
    {
        public static PumpStatus Evaluate(in PumpStatusInput input, TierDefinition tier, double contamination,
            double badwaterPenalty, double minQuality);
    }

    // Maintenance/MaintenanceLedger.cs
    public sealed class MaintenanceLedger
    {
        public double Condition { get; }
        public bool AutoMaintenance { get; set; }
        public MaintenancePhase Phase { get; }
        public double RemainingHours { get; }
        public void Reset(double condition, bool auto, MaintenancePhase phase, double remainingHours);
        public void AccumulateWear(double producingHours, double intervalDays);
        public MaintenanceDecision Decide(in MaintenanceContext context, MaintenanceRules rules);
        public void BeginMaintenance(MaintenanceRules rules);
        public void AdvanceMaintenance(double hours);
        public void CompleteMaintenance();
    }
    public readonly struct MaintenanceRules
    {
        public MaintenanceRules(bool enabled, bool auto, double autoThreshold, double durationHours,
            double intervalDays, IReadOnlyList<GoodStack> materials);
        // all properties
    }
    public readonly struct MaintenanceContext
    {
        public MaintenanceContext(bool hasMaterials, bool isReachable, bool manualRequest);
        public bool HasMaterials { get; } public bool IsReachable { get; } public bool ManualRequest { get; }
    }
    public enum MaintenanceAction { None, Start, BlockedByMaterials, BlockedByAccess, Completed }
    public readonly struct MaintenanceDecision { public MaintenanceAction Action { get; } public MaintenancePhase Phase { get; } }

    // Config/MwpConfig.cs + Config/MwpConfigCodec.cs + Config/MwpConfigValidation.cs
    public sealed class MwpConfig
    {
        public int ConfigSchemaVersion { get; }
        public bool PipeAdapterEnabled { get; }
        public bool DirectPipeOutputEnabled { get; }
        public bool UiDiagnosticsEnabled { get; }
        public bool MaintenanceEnabled { get; }
        public bool MaintenanceAuto { get; }
        public double MaintenanceAutoThreshold { get; }
        public double MaintenanceDurationHours { get; }
        public IReadOnlyList<double> MaintenanceIntervalsDays { get; }
        public IReadOnlyList<IReadOnlyList<GoodStack>> MaintenanceMaterials { get; }
        public BalanceSettings Balance { get; }
        public string TimberPipesMinVersion { get; }
        public double LogThrottleSeconds { get; }
        public static MwpConfig Default { get; }
    }
    public static class MwpConfigCodec
    {
        public static MwpConfig Parse(string json, IMwpLog log);        // never throws; invalid → clamp + Warn
        public static string Serialize(MwpConfig config);
    }

    // Save/PumpSaveState.cs + Save/MwpSaveCodec.cs
    public sealed class PumpSaveState
    {
        public int SchemaVersion { get; set; }
        public string Tier { get; set; }
        public double Accumulator { get; set; }
        public double Contamination { get; set; }
        public double Condition { get; set; }
        public bool AutoMaintenance { get; set; }
        public MaintenancePhase MaintenanceState { get; set; }
        public double MaintenanceRemainingHours { get; set; }
        public PumpState State { get; set; }
        public bool Paused { get; set; }
        public string PipeAdapterState { get; set; }
        public int ConsecutiveWaterWithdrawFailures { get; set; }
        public static PumpSaveState NewGame(string tier);
    }
    public static class MwpSaveCodec
    {
        public static PumpSaveState Sanitize(PumpSaveState? raw, IMwpLog log, string tierFallback); // corrupt → safe reset (REQ-SAVE-03)
        public static PumpSaveState Migrate(PumpSaveState raw, IMwpLog log);                         // v1→v2 idempotent (REQ-MAINT-11)
        public static string Serialize(PumpSaveState state);
        public static PumpSaveState? Parse(string json, IMwpLog log);
    }

    // Json/MiniJson.cs  — minimal UTF-8 JSON reader/writer (no Newtonsoft in Core)
    public static class MiniJson
    {
        public static object? Parse(string text);                 // Dictionary<string,object?>, List<object?>, string, double, bool, null
        public static string Write(object? value);
        public static double GetNumber(IDictionary<string, object?> obj, string key, double fallback);
        public static int GetInt(IDictionary<string, object?> obj, string key, int fallback);
        public static bool GetBool(IDictionary<string, object?> obj, string key, bool fallback);
        public static string GetString(IDictionary<string, object?> obj, string key, string fallback);
    }

    // Logging/ThrottledLog.cs
    public sealed class ThrottledLog
    {
        public ThrottledLog(IMwpLog inner, double throttleSeconds);
        public void Info(string key, string message); public void Warn(...); public void Error(...); public void Compat(...);
        public bool ShouldLog(string key, double nowSeconds);   // test hook
    }
}
```

## 3. 自訂 Spec（JSON key `MWP.MechanicalWaterPumpSpec`）

`MWP.Gameplay` 的 `MWP.MechanicalWaterPumpSpec : ComponentSpec`（record，屬性 `[Serialize]`）：

| JSON 欄位 | 型別 | 說明 |
|---|---|---|
| `Tier` | string | `Basic` / `Intermediate` / `Advanced` / `Top` |
| `WaterPerHour` | float | 3 / 6 / 15 / 20 |
| `PowerRequiredHp` | int | 100 / 200 / 500 / 700 |
| `InternalCapacity` | int | 15 / 30 / 75 / 100（須與 `SimpleOutputInventorySpec.Capacity` 相同） |
| `MaxDepth` | int | 64（等效不限；v0.2.2 起；須與 `WaterInputPipeSpec.MaxDepth` 相同） |
| `BadwaterPenalty` | float | 預設 1.0（DV-01） |
| `MinWaterQuality` | float | 預設 0.0（DV-01） |
| `CubicMetersPerWater` | float | 0.2 |
| `MaintenanceIntervalDays` | float | 8 / 10 / 12 / 15 |
| `MaintenanceMaterials` | array `{Id,Amount}` | 10 Log；15 Plank；10 MetalBlock+10 Plank；15 MetalBlock |
| `MaintenanceDurationHours` | float | 2 |

Spec 必須可被 `SpecTypeCache` 找到（public record、非抽象、繼承 `ComponentSpec`），由
`TemplateModule.Builder().AddDecorator<MechanicalWaterPumpSpec, MwpPumpController>()` 綁定。

## 4. Blueprint 契約（8 檔：4 檔位 × 2 陣營）

路徑：`mod/data/Buildings/Water/MWP/<Tier>.MWP.<Faction>.blueprint.json`
（檔名 `MWP.Pump.<Tier>.<Faction>.blueprint.json`）。`TemplateName = MechanicalWaterPump.<Tier>.<Faction>`。

必要 Spec（root，依 §4/§20.1.2 與原版 WaterPump/MechanicalPump 為模板）：

- `BuildingSpec`：`BuildingCost` = §20.1.2 材料、`ScienceCost` = 150/400/700/1500、
  `PlaceFinished=false`、`FinishableWithBeaversOnSite=false`、`DrawRangeBoundsOnIt=false`
- `BuildingModelSpec`：`FinishedModelName="#Finished"`、`UnfinishedModelName="#Unfinished"`、
  `ConstructionModeModel="Finished"`
- `TemplateSpec`：如上；其餘空/default
- `BlockObjectSpec`：`Size{X,Y,Z}` = **引擎軸向（已用遊戲 IL 證實）X=寬、Y=深度、Z=高度**；
  `Blocks` 長度必須 = X*Y*Z，row-major（`x=n%X, y=(n/X)%Y, z=n/(X*Y)`）；
  `Entrance.HasEntrance=true`，`Coordinates = {X:寬度中心, Y:-1, Z:BaseZ}`（Y=-1 = 前方門面外一格，
  Z=BaseZ = 底面高度層）；`Flippable=true`；**不得整塊 footprint 全實心**（見 API_FACTS §5）

  > 需求文檔 §4 以「X×Y×Z、Y 為高度」書寫；引擎 JSON 的 `Size` 是 (X=寬, Y=深, Z=高)。
  > 兩者換算後本模組必須送出：Mk I/II `{2,3,3}`、Mk III `{3,3,4}`（占地 3×3、高 4）、
  > Mk IV `{3,4,4}`（占地 3×4、高 4）。`TierCatalog` 的 SizeX/SizeY/SizeZ 已是引擎欄位。
  > 佈局最小風險做法：Mk I/II 直接沿用原版 `WaterPump.Folktails`（同為 2×3×3）的 `Blocks`，
  > Mk III 沿用 `MechanicalPump`（3×3×4）、Mk IV 沿用 `LargeWaterPump`（3×4×4），
  > 再依需求調整 entrance / water intake / output 面。泵類建築用 `BaseZ=1`（同原版水泵，
  > 讓泵體離水面一格、進水口 `Z=-1` 落在水面層）；`LocalAccess` 用區域座標（y=高度）。
- `BlockObjectNavMeshSettingsSpec`：全空陣列/default
- `BuildingAccessibleSpec`：`LocalAccess` 對齊取貨面
- `PlaceableBlockObjectSpec`：`ToolGroupId="MWP"`、`ToolOrder=10/20/30/40`（Basic/Intermediate/Advanced/Top）、
  `ToolShape="Square"`、`Layout="Single"`、`CustomPivot.HasCustomPivot=false`（工具群組見 §4.1）
- `LabeledEntitySpec`：`Building.MWP.Pump.<Tier>.DisplayName` / `.Description` / `Building.MWP.Pump.Flavor`；
  `Icon = "Icons/MWP<CamelTier><Faction>"`（見 §7）
- `ConstructionSiteProgressVisualizerSpec`：`ProgressThresholds=[0.0]`
- `MechanicalBuildingSpec: {}`、`MechanicalNodeSpec {PowerOutput:0, PowerInput:<hp>, IsShaft:false}`、
  `MechanicalNodeAnimatorSpec {MinSpeedMultiplier:0.0}`
- `TransputProviderSpec`：沿用原版 MechanicalPump 三組端口（左、右、底），`IgnoreRotation=false`
- `MechanicalConnectorTargetSpec: {}`
- `WaterInputSpec { WaterInputCoordinates }`：進水側（Z 負向），`Y=0`
- `WaterInputPipeSpec`：`PipeSegmentPrefabPath` = 同陣營原版管段（見 §6）、`PipeParentName="#Finished"`、
  `MaxDepth` = 64（等效不限；v0.2.2 起四檔一致，須與 `MWP.MechanicalWaterPumpSpec.MaxDepth` 相同）
- `WaterObstacleSpec` + `FinishableWaterObstacleSpec { Height: 1.0 }`：進水側整排
- `SimpleOutputInventorySpec { Capacity:<capacity>, IgnorableCapacity:false }`
- `MWP.MechanicalWaterPumpSpec`：見 §3
- `Children`：
  - `#Finished { TimbermeshSpec.Model, CollidersSpec }`
  - `#Unfinished { Children { ConstructionBase<XxZ>#nested { BlueprintPath }, ConstructionStage0 { TimbermeshSpec, CollidersSpec } } }`
  - `ConstructionBase` 路徑：`ConstructionBases/ConstructionBase<X>x<Z>/ConstructionBase<X>x<Z>.blueprint`
    （例：`ConstructionBase2x3`、`ConstructionBase3x4`；若對應尺寸不存在，改用最接近且可包覆者並記錄）

`TemplateCollection`：`mod/data/TemplateCollections/TemplateCollection.Buildings.Folktails.blueprint.json`
與 `.IronTeeth.blueprint.json`，`Blueprints#append` 列出該陣營 4 檔路徑。

### 4.1 可收縮建造選單群組（`BlockObjectToolGroupSpec`）

四檔不再塞進原版 `Water` 工具組，而是放進模組自己的**可收縮工具群組**：原版機制
`BlockObjectToolGroups/BlockObjectToolGroup.MWP.blueprint.json`（spec-only，無 `TemplateSpec`）：

```json
{
  "BlockObjectToolGroupSpec": {
    "Id": "MWP",
    "Order": 46,
    "NameLocKey": "ToolGroups.MWP",
    "Icon": "Icons/MWPBasicFolktails",
    "FallbackGroup": false
  }
}
```

- 位置：`mod/data/BlockObjectToolGroups/`，由遊戲的 blueprint 掃描直接載入（不需加入 TemplateCollection）。
- `Order=46`：原版 Water=45、Food=50，MWP 緊接 Water；`NameLocKey` 由三語 CSV 提供（`ToolGroups.MWP`）。
- `Icon` 重用已出貨的 Mk I 圖示（`Icons/MWPBasicFolktails`，AssetRef<Sprite>），不需額外圖檔。
- 行為：底欄顯示一個 MWP 圖示，點擊後展開四個檔位，再點其他群組即收合——與原版 Water/Food 等群組完全相同。
- 驗證：`tools/validate` 的 `ValidateToolGroup` 會比對 `MwpDefaults.ToolGroupId/ToolGroupOrder/ToolGroupIcon`、
  `MwpLocKeys.ToolGroupName` 與實際 JSON，並要求圖示檔存在；8 份藍圖的 `ToolGroupId`/`ToolOrder` 也逐一比對。


## 5. 執行期行為（Gameplay 必須照此實作）

Tick：`TickableComponent` 以 `MwpDefaults.TickIntervalSeconds` 節流（累積 `Time.deltaTime`，
不是每幀運算）；每 Tick 每泵 O(1)，零 LINQ/字串插值/配置（REQ-PERF-01/02）。

```text
if (!finished || paused) → 不做產水（Paused 狀態仍要顯示）
r = clamp01(MechanicalBuilding.Efficiency)          // 或 ActiveAndPowered ? ... : 0
hasWater = WaterInput.IsUnderwater
q = clamp(1 - BadwaterPenalty * WaterInput.ContaminationPercentage, MinWaterQuality, 1)
conditionMult = BalanceFormulas.ConditionMultiplier(ledger.Condition)
if (!hasWater) → 不累積、嘗試不扣水
rate = WaterPerHour * r * q * conditionMult          // Water/遊戲小時
accumulator += rate * (tickSeconds/3600)
items = min(floor(accumulator), inventory.UnreservedCapacity("Water"), internalCapacity - stock)
if (items > 0):
    withdrawn = water.Withdraw(items * CubicMetersPerWater)   // 實作 = WaterInput.DemandCleanWaterAmount + RemoveCleanWater；
                                                              // 0.2.3（DV-09）：緩衝未滿一個完整單位時不得消耗
    grantedItems = min(items, floor(withdrawn / CubicMetersPerWater))  // 不得無中生有
    if (grantedItems > 0) inventory.GiveProduced(new GoodAmount("Water", grantedItems))
    accumulator -= grantedItems; 失敗計數 → NoWater（>5 次）
ledger.AccumulateWear(producingHours)               // 只有 Producing 時磨損
維護見 §20.2：condition<=AutoThreshold 且 auto → 檢查材料/道路 → Maintaining 2h → condition=100
狀態以 PumpStateMachine.Evaluate + StatusPriority 顯示
```

`IPumpPowerSource`/`IWaterSource`/`IMaintenanceStore` 由 Gameplay 以遊戲組件實作；不得讓
Core 引用遊戲型別（REQ-NFR-01）。

維護材料：用 `Timberborn.InventorySystem.DistrictInventoryRegistry`
（`ActiveInventoriesWithStock(goodId)` → `Inventory.HasUnreservedStock` / `TakeExported`）扣料；
無路時用 `BuildingAccessible`/`Navigation` 判定 `NoAccessForMaintenance`（gameplay-dev 以
ApiIndex 驗證實際 API 後實作；**若 8 小時內無法驗證，降級方案**：材料檢查照做、道路檢查以
`BuildingAccessible` 是否存在通過，並在 VERIFICATION 標「in-game 待驗」）。

存檔：`IPersistentEntity`，`ComponentKey("MWP")`；key 名稱（不得改）：
`schemaVersion,int`、`modVersion,string`、`tier,string`、`accumulator,float`、`contamination,float`、
`condition,float`、`autoMaintenance,bool`、`maintenanceState,string`、
`maintenanceRemainingHours,float`、`state,string`、`paused,bool`、`pipeAdapterState,string`、
`waterWithdrawFailures,int`（`modVersion` 為 additive key：缺失只記 log、不觸發 degraded）。
缺欄位 → 安全預設；單泵損壞 → 重置該泵（REQ-SAVE-03）。

## 6. 美術（自製模型，v0.2 art pass）

8 份 blueprint 引用 **mod-local 自製模型**（不再引用原版模型）：

- `#Finished.TimbermeshSpec.Model` = `Buildings/Water/MWP/MWP.Pump.<Tier>.<Faction>.Model`
- `#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` = `...<Tier>.<Faction>.ConstructionStage0.Model`
- 檔案：`data/Buildings/Water/MWP/*.Model.timbermesh`（16 檔：8 finished + 8 construction）
- 管線：`art/`（Blender 4.2.1 headless → 官方 Timbermesh 插件 v1.2.0 → 純 Python 驗證）
- 材質：只用原版 `MaterialCollection` 名稱（分陣營；已靜態核對 13 個材質全部存在）
- 尺寸（export space block-corner）：Mk I/II `(2,3,3)`、Mk III `(3,4,3)`、Mk IV `(3,4,4)`；
  LOD0 三角 2,112 / 2,812 / 3,952 / 3,704（預算 12k，16 檔合計 30,328 tris）；
  **LOD1/2 尚未輸出**（見 `ART_PLACEHOLDER.md`）
- 圖示：`data/Icons/MWP<Tier><Faction>.png`（128×128，自製渲染）
- 舊的原版模型暫代已移除；`docs/ART_PLACEHOLDER.md` 保留歷史與剩餘項。

## 7. 圖示（Icons）

`mod/data/Icons/MWP<CamelTier><Faction>.png`（128×128，簡潔可辨、藍水桶／泵輪廓，不含文字）
＋同名 `.png.meta.json`（格式同原版/HDBF：`{"SpriteMode":...}` 相關 minimal；抄 HDBF 的 meta 內容即可）。
8 張：`MWPBasicFolktails`、`MWPBasicIronTeeth`、`MWPIntermediateFolktails`、…、`MWPTopIronTeeth`。

## 8. 本地化契約

`mod/data/Localizations/{enUS,zhCN,zhTW}.csv`，格式 `ID,Text,Comment`（header 必須一致）。
所有 key **必須**由 `MwpLocKeys` 產生（見 Core）；三語 key 集合完全相同、placeholder 相同。
不得出現 raw key（REQ-UI-05）。zhTW 顯示名「機械飲水泵 Mk I–IV」，刻意與原版「機械水泵」區隔（REQ-UI-04）。
key 命名空間沿用 `Building.MWP.*`／`Status.MWP.*`／`UI.MWP.*`／`Tooltip.MWP.*`／`MWP.*`；工具群組名
使用原版慣例 `ToolGroups.MWP`（`MwpLocKeys.ToolGroupName`，建造選單群組名稱，非建築顯示名）。

## 9. 設定檔契約（`mod/data/MWP_Config.json`，schema 2）

```json
{
  "configSchemaVersion": 2,
  "features": { "pipeAdapter": true, "directPipeOutput": false, "uiDiagnostics": false },
  "balance": {
    "useLegacyRates": false,
    "badwaterPenalty": 1.0,
    "minWaterQuality": 0.0,
    "cubicMetersPerWater": 0.2,
    "tickIntervalSeconds": 1.0,
    "tiers": [
      { "tier": "Basic", "waterPerHour": 3.0, "powerRequired": 100, "internalCapacity": 15, "maxDepth": 64 },
      { "tier": "Intermediate", "waterPerHour": 6.0, "powerRequired": 200, "internalCapacity": 30, "maxDepth": 64 },
      { "tier": "Advanced", "waterPerHour": 15.0, "powerRequired": 500, "internalCapacity": 75, "maxDepth": 64 },
      { "tier": "Top", "waterPerHour": 20.0, "powerRequired": 700, "internalCapacity": 100, "maxDepth": 64 }
    ]
  },
  "maintenance": {
    "enabled": true, "auto": true, "autoThreshold": 50, "durationHours": 2,
    "intervalsDays": [8, 10, 12, 15],
    "materials": [
      [{ "id": "Log", "amount": 10 }],
      [{ "id": "Plank", "amount": 15 }],
      [{ "id": "MetalBlock", "amount": 10 }, { "id": "Plank", "amount": 10 }],
      [{ "id": "MetalBlock", "amount": 15 }]
    ]
  },
  "compat": { "timberPipesMinVersion": "11.4.0", "logThrottleSeconds": 30 }
}
```

載入時全部驗證＋clamp（`REQ-SAFE-01`）：`waterPerHour>0`、`powerRequired>=0`、`intervalDays>=1`、
`durationHours>=0`、`amount>=0`、拒絕 NaN/Inf/負數；非法值記 `MWP/Warn`。
`maintenance.enabled=false` 時不得留 `Maintaining`，condition 回復 100（REQ-MAINT-13）。
`useLegacyRates=true` 僅測試用（1/3/6/9），UI 需標示非預設（REQ-BAL-10）。
`balance.tiers[].maxDepth` 四檔皆為 **64（等效不限）**（v0.2.2 起，原 2/4/6/8）：必須與
`TierCatalog` 及 8 份藍圖的 `WaterInputPipeSpec.MaxDepth`／`MWP.MechanicalWaterPumpSpec.MaxDepth`
一致（validator 會比對）；驗證規則要求 `maxDepth>=1`。

## 10. UI 契約

`MWP.UI` 只引用 Core；面板以 `IMwpPumpQuery`（Gameplay 註冊的 singleton）讀 `PumpSnapshot`，
不得反向引用 Gameplay。Entity id 一律用 `System.Guid`（`EntityComponent.EntityId`），
不是 int（見 §2）。面板內容（REQ-UI-01/02/03、REQ-MAINT-10）：

1. 狀態列（最高優先狀態＋原因＋下一步提示）
2. 實際產水（Water/h，一位小數）、水質 %、管道狀態（未安裝/閥門/直連/降級）
3. 庫存 目前/上限、抽水深度（fallback：僅當原版庫存/深度 fragment 無法顯示時）
4. 機況 %、下次維護（日）、所需材料、自動維護 toggle、立即維護按鈕；
   維護進行中顯示「維護中：{0}%」進度（資料來源 `PumpSnapshot.MaintenanceProgress` 0..1）；
   `PumpSnapshot.MaintenanceEnabled=false` 時隱藏機況/材料列（REQ-MAINT-13）

### 10.1 「原版化」面板組成（2026-11-13 UI 輪）

面板**不自行發明外觀**：所有元素都是遊戲原版控制項＋原版 USS class，靠 `EntityPanel` 根節點已掛載的
`CoreStyle`／`CommonStyle`／`EntityPanelCommonStyle`／`EntityPanelGameStyle` 上色；不載入 UXML/USS，
不設定任何 inline 顏色/字級/尺寸。具體對照：

| 區塊 | 原版控制項／class | 說明 |
|---|---|---|
| 卡片框 | `NineSliceVisualElement` + `entity-sub-panel` + `bg-sub-box--green`；硬警報改用 `bg-sub-box--red-striped` | 與 `MechanicalNodeFragment`／`DeteriorableFragment` 相同；紅條紋正是原版 `StatusListFragment` 的警報框 |
| 狀態列 | `status-list-element` + `status-list-element__icon`（34 px 原版 sprite）+ `status-list-element__text` | 完全照 `StatusListElement.uxml`；狀態圖示用原版 sprite（`UI/Images/Game/ico-*`），文字底色沿用原版 `game-text-normal`＋原生 `game-text--red`／`text--yellow` 嚴重度色 |
| 「下一步」提示 | `entity-fragment__explanation` + `bg-sub-box--blue` + `entity-panel__text text--centered` | 與 `TimerFragment` 的模式說明盒相同 |
| 資訊列 | `entity-panel__row` + `inventory-row__image`（24 px 原版 sprite）+ `entity-panel__text` | 與原版 icon row 相同；sprite 以 `Resources.Load<Sprite>("UI/Images/...")` 取得，缺失時該列不顯示圖示、不丟例外 |
| 機況條 | `Timberborn.CoreUI.ProgressBar` + `progress-bar` + 內嵌 `entity-panel__text` label | 與 `DeteriorableFragment` 相同 |
| 維護進度條 | 同上 + `progress-bar--green` | 與 `TimerFragment` 的倒數條相同 |
| 自動維護 | `Toggle` + `game-toggle` + `entity-panel__text` + `entity-panel__toggle` | 與 `HaulCandidateFragment`／`FillValveFragment` 相同 |
| 按鈕 | 從原版 UXML 範本取出的真實 `NineSliceButton`，套 `entity-panel__text entity-fragment__button`；暫停紅／恢復綠、立即維護綠 | 遊戲的九宮格按鈕型別是 internal，故借 `Game/EntityPanel/DemolishableFragment` 的原版按鈕再換色與文字；取不到時退回同 class 的 `Button` |
| 標題 | 不使用自創標題列／常駐版面文字 | 原版 entity-panel fragment 沒有自訂標題列；建築名稱/圖示/說明仍由原版 header 與描述區顯示（REQ-UI-04） |

原版 fragment 已自動提供的資訊**不重複**：動力列（`MechanicalNodeFragment`）、內部水庫存
（`SimpleOutputInventoryFragment` + `SimpleOutputInventoryFragmentEnabler`）、抽水深度控制
（`WaterInputPipeDepthFragment`）。電源/庫存/深度不會出現在本模組面板中，只有 fallback 列
（`ShowInventoryRow`／`ShowDepthRow`）在原版 fragment 缺席時補位。

建置選單另有 `BlockObjectToolGroupSpec` 群組（見 §4.1）：「GUI 原版化」＝使用原版可收縮工具群組，
不自繪選單或覆寫原版 UI。

刷新 ≤ 4 Hz、髒標記、面板關閉不刷新（REQ-PERF-04）。按鈕命令需只影響當前泵（REQ-UI-07）。
UI 不得在 `Tick`/`Update` 每幀重建元素或配置字串。

## 11. Integration 契約（Timber Pipes，軟依賴）

- MUST 路徑不需程式：標準 `SimpleOutputInventory` 的 Water 即為閥門 Extract 來源（REQ-COMPAT-02）。
- SHOULD 直連端口：v0.2 僅做**偵測＋降級**，不注入外部藍圖；`IPipeAdapter.GetState` 回
  `None`/`ValveOnly`，偵測到 Timber Pipes 且 `directPipeOutput=false` → `ValveOnly`；
  任何例外 → `Error` + `MWP/Compat` log，核心照常（REQ-COMPAT-03/04/07）。
- 不得引用 `TimberPipes.dll`（只可用反射／模組清單偵測，全部 try/catch）。
- 啟動時寫一次 `MWP/Compat/Scan`（節流），記錄偵測到的模組與狀態（REQ-COMPAT-08）。
- 診斷（REQ-OPS-04 等價實作）：不註冊 console command；`features.uiDiagnostics=true` 時
  每 60 秒輸出 `MWP/Info/Diag`（版本、泵數、adapter 狀態、平均 tick ms）。預設 false、零額外成本。

## 12. 驗收對照（Lead 最終檢查）

`mod/docs/VERIFICATION.md` 必須逐條列出：REQ 編號 → 實作位置 → 驗證方式 → 結果（Pass/待遊戲內）。
硬性門檻：build 0 warning、tests 全綠、validator 全綠、8 藍圖 + 3 語系 + config 齊備、
100 泵設計預算（Tick 1s、O(1)）與 docs 中的 in-game 待驗清單。

## 13. 設計決策與需求衝突處置（Decision log）

| # | 決策 | 理由 | 影響 |
|---|---|---|---|
| DV-01 | 污染效率預設 `badwaterPenalty=1.0`、`minWaterQuality=0.0`（= 原版 `1-c`）；需求 §3.4 的 0.75／0.25 改由 config 啟用 | 遊戲 IL 證實原版 `q = 1 - ContaminationPercentage`（`WaterInputManufactoryLimiter`）；用 0.75 會讓本模組比原版更省污染，違反 REQ-BAL-04 | 預設值與原版一致；T05 對照可通過 |
| DV-02 | `manifest.json` 的 `OptionalMods` 保持 `[]`，Timber Pipes 只在執行期偵測 | 遊戲 manifest 由 `SerializedObjectReaderWriter` 解析，`VersionedMod` 需要 `{Id, MinimumVersion}`；格式若不對會讓整個模組載入失敗。軟依賴只要求「缺模組時核心完整」，不需 manifest 宣告 | 少一個 UI 提示，換取載入安全 |
| DV-03 | 引擎 `BlockObjectSpec.Size` 軸向 = X 寬 / Y 深 / Z 高；四檔送出 `{2,3,3}`、`{2,3,3}`、`{3,3,4}`、`{3,4,4}` | IL 證實 `CoordinatesAtBaseZ`、`Entrance.Z == BaseZ`、`WaterObstacleSpec` 2D(X,Y)、vanilla Size 與 Collider 的 (寬,高,深) 對應；需求 §4 的「Y=高度」是文件慣例，不是 JSON 欄位順序 | Mk III 的 JSON 是 3×3×4（占地 3×3、高 4），語意不變 |
| DV-04 | 模型/圖示以唯讀路徑引用同陣營原版資產暫代 | 無自製 3D 資產；同陣營可避免 faction-scoped material 例外；不打包原版資產 | REQ-ART-01/02/03/06/07/08 未關閉，見 `ART_PLACEHOLDER.md` |
| DV-05 | Entity id 介面用 `System.Guid` 而非 int | 遊戲 `EntityComponent.EntityId` 是 Guid，全 API 無 int id | UI/Gameplay 直接以 Guid 為索引 |
| DV-06 | Mk III/IV 的 `Blocks` 不再逐格照抄原版 MechanicalPump/LargeWaterPump，改為「只有前排 3 格 GroundOrStackable 支撐、其餘 Any 可懸空」 | 原支撐帶在佔地中段/大面積，窄岸放不下（使用者實測）；改成與 Mk I/II 同款的前排貼岸手感 | 放置手感一致；envelope/入口/水面/碰撞/平衡不變 |
| DV-07 | 四檔 finished 模型加入**節點動畫**（飛輪/齒輪旋轉、活塞往復），供電時由遊戲既有 `MechanicalNodeAnimatorSpec` 驅動、0 動力停止 | 使用者明確要求動畫；與需求 §13.3「不可有動畫（皮帶可做靜態）」衝突，以使用者指示為準 | 不需改 C#／藍圖；動畫速度 ∝ 供電效率；施工模型保持靜態 |
| DV-08 | **抽水深度四檔一律 `MaxDepth = 64`（等效不限）**；同時移除 0.2.1 加入的原版 `NoHaulingPostStatus` + `MwpHaulStatus`「需要搬運工才能運轉」狀態 | 使用者要求（2026-11-08）：原 2/4/6/8 深度在低水位／旱季退水時讓泵無故停機，進水管到不了河床；泵本身免工人，原版「需要搬運工」狀態是誤導。搬運鏈（`HaulCandidate` + `SimpleOutputInventoryHaulBehaviorProvider` + `EmptyOutputWorkplaceBehavior`）保留，無搬運工時滿槽停產與原版一致 | 8 藍圖／`MWP_Config.json`／`TierCatalog`／自訂 spec 全部 64；NoWater 提示改用 `UI.MWP.HintNoWater`（三語系新增），不再用 `Status.Buildings.NeedsWater`；原版深度 fragment 仍顯示 `Limit 64`；`MwpHaulStatus.cs` 刪除 |
| DV-09 | **抽水只消耗完整水單位（BUG-08 修正）**：`Withdraw()` 僅在 `WaterInput` 緩衝已累積到完整請求量（`items × 0.2 m³`，含 1e-4 容差）時才 `RemoveCleanWater`；不足時保留緩衝繼續累積 | 使用者回報（2026-11-09）：湖邊淺水／低流量進水口水體每步只補 0.05–0.1 m³，舊碼把不足 1 個 Water 的零頭取走再丟棄 → 緩衝永遠到不了 0.2 m³、泵卡 `NoWater`（存檔累積 1600+ 次失敗）；修正後同存檔副本實機恢復 `Producing` | `MwpPumpController.Ports.cs::Withdraw`；`UI.MWP.HintNoWater` 改為「進水口沒有水或水量不足」；`docs/VERIFICATION.md` A22／A23 |
