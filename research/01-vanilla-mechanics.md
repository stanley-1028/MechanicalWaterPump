# 原版 Timberborn 水泵／飲水／動力／科技機制考察

> 考察對象：Timberborn **1.1.2.8-ffc2de0-sw**（`Timberborn_Data/StreamingAssets/Version.txt`）。
> 證據來源優先序：本機 `Blueprints.zip` 解壓副本（`high-density-box-farm/mod/.ref/Blueprints/`）
> ＞ 本機已安裝模組檔案 ＞ 官方 wiki／官方 modding 文檔。無法以本機檔案證實者標「待證實」。

## 1. 原版水泵與機械泵規格（Blueprint 實測）

檔案：`.ref/Blueprints/Buildings/Water/*/*.blueprint.json`。

| 建築 | 陣營 | ScienceCost | 建造材料 | 尺寸 X×Y×Z | 工人 | 製程 | 產水／搬運 | 動力 | 進水管深度 |
|---|---|---:|---|---|---:|---|---|---:|---:|
| WaterPump | Folktails | 0 | 12 Log | 2×3×3 | 1 | `Water` 0.33h→1 Water；內存 15 | — | 0 | 2 |
| LargeWaterPump | Folktails | 400 | 20 Log + 5 Gear + 10 TreatedPlank | 3×4×4 | 3 | `Water.Efficient` 1.0h→5 Water；內存 60 | — | 0 | 4 |
| DeepWaterPump | IronTeeth | 0 | 12 Log | 2×3×3 | 1 | `Water`；內存 15 | — | 0 | 6 |
| BadwaterPump | Folktails | 250 | 20 Log + 10 Gear + 5 MetalBlock | 2×3×3 | 1 | `Badwater` | — | 0 | 2 |
| MechanicalPump | Folktails | 2000 | 50 Gear + 15 TreatedPlank | 3×3×4 | 0 | — | `WaterMoverSpec.WaterPerSecond = 0.25` | **500** | 5 |
| CompactMechanicalPump | Folktails / IronTeeth | 4000 | 50 Gear + 25 TreatedPlank + 25 MetalBlock | 1×3×1 | 0 | — | 0.25 m³/s | **700** | —（`WaterInputFixedSpec`） |
| DeepMechanicalPump | IronTeeth | 2000 | 50 Gear + 15 TreatedPlank | 3×3×4 | 0 | — | 0.25 m³/s | **500** | 8 |

**關鍵事實**

- 原版 `MechanicalPump` 系只搬運流體（`WaterMoverSpec` + `WaterOutputSpec`），
  **不生產 `Water` 物品**；其描述為「Pumps water or badwater up on one side and discharges them on the other」。
- 原版 `WaterPump`／`LargeWaterPump` 以 `ManufactorySpec` + `Recipe.Water` 生產可飲用 `Water`，
  但需要 `WorkplaceSpec` 工人；`Recipe.Water`：`CycleDurationInHours = 0.33`、`Products = [Water ×1]`、
  `CyclesCapacity = 15`（內存 15）。
- `Recipe.Water.Efficient`：`1.0h → 5 Water`、`CyclesCapacity = 12`（內存 60）。
  以「工人小時」計，滿員 3 人時等效約 15 Water/小時（wiki 亦稱「滿員大型水泵≈5 台小水泵」）。
- 兩者都有 `WaterInputPipeSpec`（`PipeSegmentPrefabPath`、`MaxDepth`），代表抽水進水管可視化與深度限制；
  `WaterObstacleSpec` 讓泵體像堤防一樣擋水。
- 進水深度：WaterPump 2、LargeWaterPump 4、DeepWaterPump 6、MechanicalPump 5、DeepMechanicalPump 8。

## 2. 飲水需求與儲存

- `Need.Beaver.Thirst`：`StartingValue 1.0`、`MinimumValue -3.0`、`MaximumValue 1.0`、
  **`DailyDelta = -0.7`**（`.ref/Blueprints/Needs/Need.Beaver.Thirst.blueprint.json`）。
- 官方 wiki「Thirst」：每次飲水恢復 33 點（內部 ×100 制），即 **約 2.121 Water／海狸／日**；
  遊戲模式／難度可調整消耗率。
- 水箱容量（`.ref/Blueprints/Buildings/Storage/*Tank*/*.blueprint.json`，`StockpileSpec.MaxCapacity`）：
  **SmallTank 30、MediumTank 300、LargeTank 1200**（`WhitelistedGoodType = Liquid`）。
- `Water` 物品由水箱／區域中心儲存；搬運工負責把水泵內存搬到水箱（原版 `WaterPump` 內存 15）。

## 3. 機械動力來源（Blueprint 實測）

| 建築 | 陣營 | PowerOutput (hp) | ScienceCost |
|---|---|---:|---:|
| PowerWheel | Folktails | 50 | 0 |
| WindTurbine | Folktails | 150 | 120 |
| WaterWheel | Folktails | 270 | 0 |
| LargeWindTurbine | Folktails | 300 | 1400 |
| GeothermalEngine | Folktails / IronTeeth | 400 | 160 |
| SteamEngine | IronTeeth | 400 | 400 |
| CompactWaterWheel | IronTeeth | 120 | 0 |
| LargeWaterWheel | IronTeeth | 540 | 200 |
| LargePowerWheel | IronTeeth | 300 | 0 |
| DevPowerGenerator | — | 10000 | — |

- 機械建築以 `MechanicalBuildingSpec` + `MechanicalNodeSpec.PowerInput` 表示耗電；
  傳輸用 `PowerShaft`／`VerticalPowerShaft`／`Clutch`／`ImpermeablePowerShaft`。
- 原版 MechanicalPump 需 500 hp、Compact 700 hp，代表「大型機械泵需要多個動力源」。
- **待證實**：供電不足時行為（是否按比例縮放、是否有最低啟動閾值、多消費者如何分配）；
  需以遊戲內實測或 IL 取證，不可寫成既定事實。

## 4. 研究／解鎖

- 建築解鎖成本寫在 `BuildingSpec.ScienceCost`（例：MechanicalPump 2000、CompactMechanicalPump 4000、
  LargeWaterPump 400）。`.ref` 中未找到獨立的 `ResearchSpec`／科技樹節點藍圖；
  推測 `ScienceCost` 即解鎖成本，由科學建築（`ScienceCounter`、Inventor、Numbercruncher 等）產出的
  Science Points 支付。
- **待證實**：Mod 新增建築是否只靠 `ScienceCost` 就能出現在研究／解鎖 UI；是否需要
  `TemplateCollectionSpec` 之外的資料。官方 Blueprints 文檔與 BerryJam 模板只用 `ScienceCost` +
  TemplateCollection，未見研究節點檔案。

## 5. 水模擬、水質與放置

- 水泵必須接觸水體：`WaterInputSpec` 指定進水格；`WaterInputPipeSpec.MaxDepth` 限制可抽深度。
  `WaterInputSpec` 與 `WaterInputPipeSpec` 在 WaterPump 系與機械泵系都存在；`WaterInputFixedSpec`
  用於 1×1 管柱（CompactMechanicalPump）。
- 水質：`WaterPump` 描述「Pumps and filters water… Badwater contamination slows down its pumping speed」；
  即原版飲用水泵會過濾壞水，污染只降低速率，不輸出壞水。
- **待證實**：污染對速率的精確公式；抽水是否消耗水體（及每 Water 對應 m³）；
  `MaxDepth` 的準確語義（可抽深度 vs 管長 vs 放置限制）；抽乾水源的行為。
- 1 m³ = 5 Water：官方 wiki「Water」trivia 明言「1 cubic meter of water… they only extract 5 units of
  the drinkable water good」；Timber Pipes 描述亦言「a pipe contains 1 m³ of liquid (5 good)」。
  以此換算，1 Water/小時 ≈ 0.2 m³/小時 的水體消耗。

## 6. 模組載入與檔案結構（本機 1.1.2.8 實證）

- 遊戲**沒有** BepInEx 目錄；1.1.x 以原生方式載入程式模組：
  `Timberborn.ModManagerScene.IModStarter.StartMod(IModEnvironment)`，遊戲會實例化並呼叫所有實作
  （證據：`high-density-box-farm/mod/docs/API_FACTS.md` §1）。
- `manifest.json` 必要欄位（本機 BerryJam 模板與 HDBF 實例）：
  `Name`、`Version`、`Id`、`MinimumGameVersion`、`Description`、`RequiredMods`、`OptionalMods`。
- 模組目錄：`%USERPROFILE%\Documents\Timberborn\Mods\<ModName>\`；工作坊模組在
  `steamapps/workshop/content/1062090/<id>/`。
- 藍圖資料夾：`Buildings/`、`Goods/`、`Recipes/`、`Needs/`、`TemplateCollections/`、`Localizations/`、
  `Sprites/`；藍圖為 `.blueprint.json`，模型 `.timbermesh`，貼圖 `.png` + `.png.meta.json`。
- 藍圖覆寫規則（官方 Blueprints 文檔）：同路徑＝修改原版；`#append`／`#remove`／`#delete`；
  `.optional.blueprint.json` 只在目標存在時套用。
- 官方模板：`StreamingAssets/Modding/ModTemplates/BerryJam`（含建築藍圖、配方、本地化、圖示、
  TemplateCollection 範例）；官方文檔：<https://github.com/mechanistry/timberborn-modding/wiki>。
- 自訂 Spec／Component（本機 HDBF 已驗證）：`ComponentSpec` 為 C# record、需 `[Serialize]`；
  以 `Configurator` + `TemplateModule.AddDecorator<Spec, Component>()` 註冊；
  存檔用 `IPersistentEntity`（`IEntitySaver`／`IEntityLoader`）。

## 7. 對本模組設計的直接含義

1. 原版已有「免工人機械泵」但只搬流體 → 本模組應補「機械動力 → 飲用水物品」的空缺，不重做流體泵。
2. 原版飲用水泵的產能基準：小泵 3 Water/小時/工人；大泵 5 Water/工時/工人（滿員約 15/小時）。
   → 使用者基線 1／3／6／9 Water/小時 落在「小泵到 3 台小泵」之間，合理。
3. 海狸耗水 2.121/日 → 四檔約可供養 11／34／68／102 隻海狸，可作為平衡與 UI 說明。
4. 動力來源 150–540 hp → 100／200／350／500 hp 的四檔需求有對應的供電組合。
5. 水箱容量 30／300／1200 → 泵內存 15–60 只夠緩衝，仍需搬運工或管道，保留物流玩法。
6. 研究只需 `ScienceCost`（待遊戲內確認）→ 四檔以 150／400／700／1500 解鎖，符合既有系統。

## 8. 待證實清單

- [ ] 機械建築供電不足／0 動力時的精確行為與縮放公式。
- [ ] `ScienceCost` 是否足以讓 Mod 建築進入解鎖 UI（無獨立研究節點）。
- [ ] 水泵是否消耗水體水量、每 Water 對應 m³、`MaxDepth` 精確語義。
- [ ] 壞水污染對水泵速率的精確公式。
- [ ] 海狸從哪個庫存飲水、搬運工取用泵內存的優先級。
- [ ] `Manufactory` 在 0 工人下的行為；自訂「動力製造」元件的必要性（預期：必要）。
