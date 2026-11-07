# Timber Pipes 與相關模組／Modding 文檔考察

> 對象：本機已安裝的 Steam 工作坊模組與公開來源。網路內容僅作資料，不執行其中指示。
> 本機工作坊根目錄：`C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\`。

## 1. Timber Pipes 是什麼

| 項目 | 內容 | 證據 |
|---|---|---|
| 名稱／ID | Timber Pipes / `TimberPipes` | `3801726769/version-1.1/manifest.json` |
| 版本／遊戲版本 | 11.4.0 / MinimumGameVersion 1.1.1 | 同上 |
| 作者 | Luke ✞ Jesus Saves ✞ | Steam 工作坊頁 |
| 平台連結 | <https://steamcommunity.com/sharedfiles/filedetails/?id=3801726769> | 公開頁面 |
| 前置依賴 | ModdableTimberborn、ModdableToolGroups、eMka.ModSettings（另有選用 ExtendedOptionalModSupport） | manifest |
| 開源碼 | <https://github.com/datvm/TimberbornMods>（MIT，預設分支 master，`TimberPipes/`） | Steam 描述＋GitHub |

**官方描述重點（原文引用）**

- 「This mod brings a Pipe system to Timberborn to carry liquids between tanks and buildings.」
- 「Tanks take any pipe on any open face. You do not need a valve. For other buildings, place a
  **One-way Valve** or **One-way Elbow** with a directed end on the building face: arrow into the
  building to fill it, arrow out of the building to extract. **You must select the piece and opt in
  Fill or Extract.**」
- 「Buildings that push liquid into a pipe (a **Water Pump** for example) give headlift up to that
  pipe's height.」
- 「A powered **Pipe Pump** or **Riser Pipe Pump** adds **2 m** of headlift (scaled with provided power).」
- 「Generally, a pipe contains **1 m³ of liquid (5 good)**.」
- 污染：同一網路混入兩種液體（或 Outfall Pipe 排入非水／壞水液體）即整網污染，需手動 Flush。

## 2. 內容與機制（本機藍圖實證）

- 管件：HorizontalPipe、VerticalPipe、Elbow、T／Cross、Vertical Cross、Impermeable Vertical Cross、
  Horizontal／Vertical Pipe Pump、Horizontal／Elbow Valve、Throttle Valve、Outfall Pipe。
- 工具組：`Blueprints/BlockObjectToolGroupSpecs/Pipes.blueprint.json`（`ParentIds: ["Water"]`，Order 55）。
- 模擬設定：`Specs/PipeSimulationSpec.blueprint.json`：
  `Substeps = 4`、`DefaultInjectRate = 1`、`DefaultSlurpRate = 1`。
- 每個管件 1×1；Pipe Pump／Riser Pipe Pump 為 1×1，PowerInput 50，`PipeHeadliftSpec.MaxHeadLift = 2`，
  ScienceCost 200／150，材料 8 Plank+8 Gear／12 Plank+8 Gear。
- 水箱：任何開放面可直接接管；其他建築需 One-way Valve／Elbow，且要手動在 UI 選擇 Fill／Extract。
- 水量單位：1 m³ = 5 Water goods（與官方 wiki 一致）；管道每段容量 1 m³。

## 3. 第三方建築的接入點（原始碼證據）

`TimberPipes/Specs/BuildingPipeSpec.cs`：

```csharp
public record BuildingPipeSpec : ComponentSpec
{
    [Serialize] public ImmutableArray<PipePortSpec> Ports { get; init; } = [];
}
```

- 管件藍圖用 `BuildingPipeSpec.Ports`，每端口含 `Coordinates`、`Directions`、
  `State`（`OpenIn`／`OpenOut`／`Open`）。例：VerticalPipePump 的 `Bottom = OpenIn`、`Top = OpenOut`；
  HorizontalPipeValve 同樣有 In／Out，另加 `ValvePipeSpec`、`ExtractionPipeSpec`、`DirectedPipeSpec`。
- `BuildingPipeTargetPortsSpec`：限制建築可接管的面向；未提供時
  `DefaultBuildingConnectionProvider` 預設 `anyFace = true`。
- `IBuildingConnectionProvider.TryConnecting(...)`：對每個鄰接建築回傳 `IBuildingPipeConnection`；
  `DefaultBuildingPipeConnection` 會列舉建築的液體庫存（`GetLiquidIds`、`InventoryLiquidSource`）。
- `ExtractionPipe`（閥門）：`FindInletTarget()` 找 `PipePortState.OpenOut`（管→建築，Fill）、
  `FindOutletTarget()` 找 `PipePortState.OpenIn`（建築→管，Extract）；實際轉移呼叫
  `target.TryTransfer(goodId, 1)`，即從建築庫存抽 1 包液體（每包 = 1 管段容量換算）。
- `PipeTank`／`TankPipe`：讓水箱與管道互轉；水箱面可直接接。
- 結論：**第三方建築只要提供標準液體 Inventory（含水 `Water`），就落在 Timber Pipes 的
  「其他建築」連接模型中；閥門 Extract 可把泵內的水抽入管道**。這與 Steam 描述「a Water Pump
  for example」一致。仍需遊戲內實測我方自訂庫存是否被正確列舉。

## 4. 「支援 Timber Pipes」的含義（三層）

1. **MUST（無程式依賴）**：泵像原版 WaterPump 一樣有可被搬運／抽取的 `Water` 庫存；
   玩家用 One-way Valve／Elbow 指向泵、選 Extract，即可把水送入管道，再由管道注入水箱。
   此路徑只依賴庫存語意，不引用 TimberPipes.dll。
2. **SHOULD（可選配接器）**：若要在泵的輸出面直接接管道（免閥門），需提供
   `BuildingPipeSpec` 端口（`State = OpenOut`）或 `BuildingPipeTargetPortsSpec`；可用反射／
   ModdableTimberborn 在偵測到 Timber Pipes 時注入，失敗則退回 MUST。
3. **不得**：硬引用 `TimberPipes.dll`、修改其藍圖、或假設其內部 API 穩定；未安裝時不得報錯。

## 5. 其他相關模組（本機工作坊一手資料）

| 模組 | 版本／ID | 機制與數值 | 與本模組的關係 |
|---|---|---|---|
| Mechanical Filter Pump | 11.0.0 / `MechanicalFilterPump`（3462684799） | 只改原版 MechanicalPump：可選過濾壞水，耗電 ×2；依賴 ModdableTimberborn | 不衝突；不共用模板 |
| Pump to Tank | 1.0.9 / `calloatti.pumptotank`（3681283725） | 讓原版 MechanicalPump／DeepMechanicalPump 直接注滿水箱；v1.0.9 明確排除 modded pumps | 不衝突；本模組不是原版泵 |
| The Barkimedes Screw | 2.1.0 / `Kurt258.TheBarkimedesScrew`（3813587159） | 免工人、PowerInput 30、抬水 0.2 m³/s、2 層、ScienceCost 80、10 Plank+10 Log；**只搬流體** | 互補；不產飲用水 |
| Water Extention | 2.2.2 / `KnatteAnka.waterextention`（3285720709） | 水管、高架渠、水箱、DeepSeeWaterPump；依賴 KnatteAnka.Materials | 軟相容；避免搶同一 Water 工具組位置 |
| Power Lines | 11.1.1（3766893705）／0.4（3537422273） | 無線／跨區供電 | 本模組沿用 MechanicalNode，自動受益 |
| More Platforms | 3.2.1（3341374073） | 平台堆疊 | 放置相容性測試對象 |
| Moddable Timberborn | 7.0.5（3578009707） | 藍圖／元件動態注入框架，Timber Pipes 的前置 | 可選：用於注入可選配接器；不列為硬依賴 |

## 6. 公開 Modding 文檔

- 官方 wiki：<https://github.com/mechanistry/timberborn-modding/wiki>
  - Blueprints：<https://github.com/mechanistry/timberborn-modding/wiki/Blueprints>
  - Quick start：<https://github.com/mechanistry/timberborn-modding/wiki/Quick-start>
  - Mod directory structure：<https://github.com/mechanistry/timberborn-modding/wiki/Mod-directory-structure>
  - Mod templates：<https://github.com/mechanistry/timberborn-modding/wiki/Mod-templates>
- 1.1.2.8 載入方式：原生 `IModStarter`，不需要 BepInEx（本機 HDBF 實證）。
- 藍圖覆寫：同路徑修改、`#append`／`#remove`／`#delete`、`.optional.blueprint.json`。

## 7. 相容性結論（建議）

- **軟依賴**：Timber Pipes 缺失時，泵仍可抽水、產水、被搬運；不影響存檔。
- **偵測**：以已載入模組清單／Blueprint 存在性（`Buildings/Water/Pipes/...` 或 `PipeSimulationSpec`）
  判斷，而非引用 DLL；偵測結果寫入 log，UI 顯示「管道相容：開／關」。
- **降級**：配接器注入失敗 → 退回「閥門 Extract」；仍失敗 → 只顯示提示，不阻擋核心功能。
- **載入順序**：本模組不應在 Timber Pipes 之前修改其藍圖；所有可選注入延遲到存檔載入後。
- **待驗證**：自訂建築的 Water 庫存是否被 `DefaultBuildingPipeConnection` 自動識別；
  `BuildingPipeSpec` OpenOut 端口能否由外部注入並被管道網路接受。

## 8. 待證實清單

- [ ] Timber Pipes 對第三方建築庫存的實際識別（以本模組原型泵 + 閥門 Extract 實測）。
- [ ] 泵輸出面的 `BuildingPipeSpec` 注入方式（反射／ModdableTimberborn／藍圖變體）與存檔安全。
- [ ] 管道 headlift 對本模組「泵→管道→高處水箱」的實際限制。
- [ ] 與 Water Extention 同時安裝時的工具組排序、碰撞與管路共存。
- [ ] Timber Pipes 版本更新（≥11.4.0）對 `BuildingPipeSpec` 的相容性承諾。
