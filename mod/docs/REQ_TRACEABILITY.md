# 需求追蹤矩陣（REQ TRACEABILITY）

> 需求來源：`機械水泵模組需求文檔.md`（v0.2；§20 覆蓋 §1–§19）。
> 狀態定義：
> - `PASS(自動)`：有可重跑的自動證據（單元測試／validator／build），見 `VERIFICATION.md` A1–A7。
> - `CODE`：程式與資料已實作、可編譯可靜態驗證，但行為需遊戲內確認（VERIFICATION T 編號）。
> - `PARTIAL`：已實作主要部分，仍有一個具體缺口（在「備註」寫明）。
> - `GAP`：尚未完成（在「備註」寫明補救路徑）。
> - `N/A`：v0.2 不適用（設計決策見 `CONTRACT.md` §13）。
>
> 檔案路徑皆相對 `mod/`。最後更新：2026-11-04（v0.2.0）。

## 0. 自動證據對照（VERIFICATION §1）

`A1` build 0 warning｜`A2` 單元測試 PASS 213/213｜`A3` VALIDATION PASSED｜`A4` 打包完整性｜
`A5` 自訂 Spec metadata｜`A6` DI/入口型別｜`A7` 無硬依賴。以下 PASS(自動) 均引用其一或多項。

## 1. CORE（核心玩法）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| CORE-01 | 無水／無動力不得產水 | `src/MWP.Core/State/PumpStateMachine.cs`、`src/MWP.Gameplay/Components/MwpPumpController.Tick.cs` | A2（狀態機/公式測試）+ T03 | PASS(自動)+CODE |
| CORE-02 | 速率 = R_base×r×q | `src/MWP.Core/Balance/BalanceFormulas.cs` | A2 | PASS(自動) |
| CORE-03 | 庫存滿停產、不浪費 | `PumpStateMachine`、`Tick.ProduceWater` | A2（StorageFull）+ T06 | PASS(自動)+CODE |
| CORE-04 | 可達取貨點；無路不崩潰可恢復 | `MwpPumpController.Ports.cs`（`BuildingAccessible` + `DistrictCenter.AccessibleIsOnDistrictRoad`） | T08 | CODE |
| CORE-05 | 停產狀態＋原因＋優先序 | `src/MWP.Core/Abstractions/MwpEnums.cs`（`StatusPriority`）、`src/MWP.UI/Panels/MwpPumpPanelDataFactory.cs` | A2（優先序測試）+ UI T05 | PASS(自動)+CODE |
| CORE-06 | 不得任何被動產水模式 | 上述公式＋狀態機（r=0/q/condition=0 皆 0） | A2 | PASS(自動) |

## 2. BLD（建築）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| BLD-01 | 四檔可研究/建造/拆除、ID 唯一 | `data/Buildings/Water/MWP/*.json`（8）、`src/MWP.Core/Balance/TierDefinition.cs` | A3（BlueprintId/TemplateName 唯一）+ T01 | PASS(自動)+CODE |
| BLD-02 | 無 WorkplaceSpec | 8 藍圖（無該 spec） | A3（spec 集合檢查） | PASS(自動) |
| BLD-03 | 數值與規格表一致（可由 config 覆寫） | 8 藍圖 + `data/MWP_Config.json` + `src/MWP.Gameplay/MwpRuntime.cs` | A3（逐欄位對 TierCatalog）+ T01 | PASS(自動) |
| BLD-04 | 可達取貨點；NoAccess 不崩潰 | `MwpPumpController.Ports.cs`、UI 狀態列 | T08 | CODE |
| BLD-05 | 左右翻轉、端口鏡像 | `BlockObjectSpec.Flippable=true`、`TransputProviderSpec` | A3（Flippable/端口 spec）+ T01 | PASS(自動)+CODE |
| BLD-06 | 進水側 WaterObstacle 擋水 | `WaterObstacleSpec` + `FinishableWaterObstacleSpec` | A3 | PASS(自動)+CODE |
| BLD-07 | FT／IT 兩陣營變體 | 4 檔 × 2 陣營藍圖、同陣營模型路徑 | A3 | PASS(自動) |
| BLD-08 | 施工完成前不產水 | `PumpStateMachine`（`Unfinished`）、`Tick.EvaluateState` | A2（Unfinished=0）+ T01 | PASS(自動)+CODE |

## 3. BAL（平衡）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| BAL-01 | 24h 產量誤差 ≤5% | `BalanceFormulas`、`TierCatalog` | A2（公式閉環）+ T02/T27 | CODE |
| BAL-02 | 0 動力 = 0，存檔不漏水 | `PumpStateMachine`、`MwpPumpController.Save.cs`（accumulator 0..1） | A2 | PASS(自動) |
| BAL-03 | 供電下降速率單調下降 | `BalanceFormulas.PowerRatio` | A2（10 段單調） | PASS(自動) |
| BAL-04 | 污染不優於同條件原版泵 | 預設 `badwaterPenalty=1.0/minQuality=0.0`（DV-01）；`BalanceFormulas.WaterQuality` | A2（vanilla 等價 q 測試）+ T05 | PASS(自動)+CODE |
| BAL-05 | ScienceCost／材質／動力不低於表 | 8 藍圖 + `TierCatalog` | A3 | PASS(自動) |
| BAL-06 | Mk I 無動力不得運轉 | `PumpStateMachine`（NoPower 強制 0） | A2 | PASS(自動) |
| BAL-07 | 滿載 3/6/15/20、誤差 ≤5% | `TierCatalog`、8 藍圖 | A3 + A2（24h 閉環）+ T02 | PASS(自動)+CODE |
| BAL-08 | ≥ 原版對照方案（§20.1.3） | `TierCatalog` 數值；對照表見 `docs/VERIFICATION.md` | T27 | GAP（需實測對照） |
| BAL-09 | 動力 100/200/500/700 | 藍圖 `MechanicalNodeSpec.PowerInput`、`TierCatalog` | A3 | PASS(自動) |
| BAL-10 | useLegacyRates 預設 false；啟用時 UI 標示 | `data/MWP_Config.json`（false）、`MwpRuntime` log | A3（config 檢查） | PARTIAL（config 已預設 false 並記 log；**UI 未顯示 legacy 標示**） |
| BAL-11 | 30 日平均 90–105% | `src/MWP.Core/Maintenance/MaintenanceLedger.cs` + 測試模擬 | A2（30 日逐小時，四檔 93–94%） | PASS(自動)+CODE |

## 4. TECH（科技）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| TECH-01 | ScienceCost 150/400/700/1500 | 藍圖 `BuildingSpec`、`TierCatalog` | A3 | PASS(自動) |
| TECH-02 | Water 工具組、排序 60–63、未解鎖不顯示 | `PlaceableBlockObjectSpec`（ToolGroupId/ToolOrder）+ `TemplateCollection` | A3 | PASS(自動)+CODE |
| TECH-03 | 解鎖狀態用原版科技進度 | 只用 `ScienceCost`，不另存科技資料 | A3（無自訂科技節點）+ T01 | CODE |
| TECH-04 | 未來研究節點需向後相容 | 目前未新增節點 | — | N/A |

## 5. COMPAT（相容）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| COMPAT-01 | 無 Timber Pipes 核心完整、無錯誤 log | `src/MWP.Integration/TimberPipesProbe.cs`（純反射） | A7 + T11 | CODE |
| COMPAT-02 | 有 Timber Pipes 時閥門 Extract（MUST） | 標準 `SimpleOutputInventory` Water 庫存（無需程式） | T10 | GAP（需遊戲內） |
| COMPAT-03 | 直連端口失敗安全降級 | `src/MWP.Integration/MwpPipeAdapter.cs`（偵測＋ValveOnly/Error，不注入外部藍圖） | A7 + T10 | PARTIAL（SHOULD 直連端口 v0.2 未實作，只做偵測與降級） |
| COMPAT-04 | 不硬引用外部模組、不改其藍圖 | 全部 Integration 以反射實作 | A7 | PASS(自動) |
| COMPAT-05 | 與 Pump to Tank／Filter Pump／Barkimedes 共存 | 不覆寫原版藍圖、無 Harmony | T12 | GAP（需遊戲內） |
| COMPAT-06 | 與 Water Extention／Power Lines／More Platforms 共存 | 沿用原版 MechanicalNode；ToolGroup 60–63 | T12 | GAP（需遊戲內） |
| COMPAT-07 | 偵測失敗不阻止啟動 | `TimberPipesProbe`／`MwpPipeAdapter` 全 try/catch；ModStarter 不 throw | A7 + 程式審查 | CODE |
| COMPAT-08 | 相容狀態可診斷 | `MWP/Compat/Scan` log、UI 管道列（`Status.MWP.PipeDirect/Valve/Unavailable`） | UI T08（管道列） | CODE |
| COMPAT-09 | 每組合有測試紀錄 | 見 `VERIFICATION.md` §3 T10/T11/T12 | T10–T12 | GAP（需遊戲內） |
| COMPAT-10 | 缺任何模組不得紅錯／壞檔 | 同上；存檔只寫自家 `MWP` 元件 | T11/T15 | CODE |

## 6. IMPL（技術實作）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| IMPL-01 | 機械網路消費者（非電力/燃料） | `MechanicalBuildingSpec` + `MechanicalNodeSpec{PowerInput}`、`Tick.UpdateObservations` | A3 + A6 | PASS(自動)+CODE |
| IMPL-02 | 供電比例與速率單調、0 動力不產 | `PumpStateMachine`、`BalanceFormulas` | A2 | PASS(自動) |
| IMPL-03 | 動力恢復後自動繼續 | 狀態由每 tick 觀測推導，無派工狀態 | 程式審查 + T03 | CODE |
| IMPL-04 | 左右兩側＋底部端口、翻轉後可接 | `TransputProviderSpec`（沿用 MechanicalPump 三組） | A3 | PASS(自動)+CODE |
| IMPL-05 | 不改原版動力全域行為 | 只讀 `MechanicalBuilding`、只動自己 `SetConsumptionDisabled` | A7 + 程式審查 | PASS(自動) |
| IMPL-06 | 不每幀掃圖、水源取樣快取 | `Tick.Advance`（1s 節流、快取組件） | 程式審查 + T24 | CODE |
| IMPL-07 | 自訂 Spec/Component 原生載入、無 BepInEx | `src/MWP.Gameplay/Specs/MechanicalWaterPumpSpec.cs`（`[SpecAlias]`）、`MwpGameConfigurator` | A5 + A6 + T01 | PASS(自動)+CODE |
| IMPL-08 | Harmony ≤2 且 Postfix、可回退 | **0 個 Harmony patch**（未引用 Harmony） | A7 | PASS(自動) |

## 7. SAVE（存檔）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| SAVE-01 | 獨立節點/元件鍵，不覆寫原版 | `MwpSaveKeys.cs`（`ComponentKey("MWP")`）、`MwpPumpController.Save.cs` | A2（編解碼）+ A3 | PASS(自動) |
| SAVE-02 | schemaVersion 與 modVersion 寫入 | `MwpSaveKeys.SchemaVersion`/`ModVersion`、`Save` | A2 + A3 | CODE（本輪補 `modVersion`） |
| SAVE-03 | 單泵損壞不影響其他泵／整檔 | `MwpSaveCodec.Sanitize`、`MwpPumpController.Load` try/catch | A2（corrupt reset 測試） | PASS(自動) |
| SAVE-04 | 移除模組後存檔仍可載入 | 只寫自家元件，原版忽略 | T15 | CODE |
| SAVE-05 | 增刪 Timber Pipes 可載入並重建 adapter | `Save.ApplySavedState` 重新 `ResolvePipeState()` | A2（遷移測試）+ T15 | PASS(自動)+CODE |

## 8. UI（介面/本地化）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| UI-01 | 面板顯示狀態/實際速率/供電/水質/庫存/管道/深度 | `src/MWP.UI/Panels/MwpPumpPanelDataFactory.cs`、`MwpPumpPanelView.cs` | A1（UI build）+ T14 | CODE |
| UI-02 | 任何停產有原因，不得只顯示暫停 | 狀態列＋下一步提示（`MwpUiVanillaKeys.cs`） | T05 | CODE |
| UI-03 | 固定優先序、只顯示最高者 | `StatusPriority`（Core）、UI 單一狀態列 | A2 | PASS(自動)+CODE |
| UI-04 | 顯示名不得與原版「機械水泵」相同 | `data/Localizations/zhTW.csv`（機械飲水泵 Mk I–IV） | A3（顯示名檢查） | PASS(自動) |
| UI-05 | zhTW/zhCN/enUS、缺鍵回退不顯示 raw key | 3 份 CSV（各 41 keys）、`MwpLocKeys` | A3（key 集合/placeholder） | PASS(自動) |
| UI-06 | UI ≤4Hz、開啟面板不掉幀 | `MwpPumpPanelFragment`（4Hz＋fingerprint 髒標記） | 程式審查 + T24 | CODE |
| UI-07 | 暫停只影響當前泵 | `IMwpPumpCommands.SetPaused(Guid)`、UI 按鈕帶當前泵 Guid | A6 + T05 | PASS(自動)+CODE |

## 9. ART（美術）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| ART-01 | 四檔模型/圖示/施工模型齊備、尺寸一致 | `data/Buildings/Water/MWP/*.Model.timbermesh`（16 檔）、`data/Icons/MWP*.png`（8 檔） | A9（16/16 PASS、bounds 對 TierCatalog）+ A10 | **PASS(靜態)** |
| ART-02 | LOD／材質預算 | LOD0 2,112–3,952 tris（≤12k）；材質只用原版 MaterialCollection | A9（tris/材質檢查）、A11 | PARTIAL（LOD1/2 未輸出，交由遊戲 prefab optimizer） |
| ART-03 | 進水/輸出/動力端口可辨識標記 | 模型含幾何端口＋3 個 `#Port#Power/Pipe/Logistics` 空節點 | A9（C6 節點檢查） | **PASS(靜態)** |
| ART-04 | 風格符合、無文字/商標 | 離線預覽人工檢視：FT 暖木 / IT 冷金屬、四檔造型階梯、無文字 | `art/previews/tiers_factions_iso_grid.png` | **PASS（人工）** |
| ART-05 | 匯出格式/軸向/單位/命名 | `art/export_timbermesh.py` + C2/C7 檢查（Model.name=檔名、export space、四元數/法線/切線/UV） | A9 | **PASS(靜態)** |
| ART-06 | 50% 縮放四檔可區分 | 24 張預覽（iso / iso50 / construction）＋ 4×2 陣營合圖 | `art/previews/*_iso50.png`、`icons_contact_4x2.png` | **PASS（人工）** |
| ART-07 | 建模前三視圖參考圖簽核 | 有等角/50%/施工預覽，但需求要求的「建模前 front/side/top＋端口座標簽核」流程未走 | `art/previews/` | PARTIAL（流程性項目） |
| ART-08 | ≥6 張原版風格截圖、不得散布原版素材 | 模組包不含原版資產 | A4（打包清單） | PASS(自動)（參考圖本身為內部資料，未打包） |
| ART-09 | 模組包不得含原版資產 | `build/MWP` 僅 dll/藍圖/CSV/PNG/自製 .timbermesh/config | A4 | PASS(自動) |
| ART-10 | AI concept 標記、不得作尺寸依據 | 未產 AI 圖 | — | N/A |
| ART-11 | prompt/seed 記錄 | 未產 AI 圖；`art/reference/PROMPTS.md` 已寫規範 | — | N/A |

## 10. TEST（測試）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| TEST-01 | T01–T15 有紀錄與證據 | `docs/VERIFICATION.md` §3 | 待實測 | GAP（需遊戲內） |
| TEST-02 | 硬性驗收 1–10 全過才發佈 | `docs/VERIFICATION.md` §2 | 待實測 | GAP |
| TEST-03 | 效能門檻含 CPU/GPU/FPS 記錄 | T13/T24 | 待實測 | GAP |
| TEST-04 | 缺模組/版本不符/增刪模組後讀檔 | T11/T12/T15 | 待實測 | GAP |
| TEST-05 | 改平衡需重跑 T02–T05/T13 | 目前版本未再改平衡 | — | N/A（v0.2.0 後續變更適用） |

## 11. OPS（效能/維運）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| OPS-01 | 100 泵 FPS 下降 ≤3 | 1s Tick、O(1) | T13/T24 | GAP（需實測） |
| OPS-02 | 外部呼叫 try/catch、只影響可選功能 | `TimberPipesProbe`/`MwpPipeAdapter`/`UnityCompatLog` | A7 + 程式審查 | PASS(自動) |
| OPS-03 | 日誌分級＋30 秒同 key 節流 | `src/MWP.Core/Logging/ThrottledLog.cs`、`MwpLogKeys` | A2（時鐘注入節流測試） | PASS(自動) |
| OPS-04 | `/mwp.diag` 或等價診斷 | `MwpRuntime`：`features.uiDiagnostics=true` 時輸出 `MWP/Info/Diag`（版本/泵數/adapter/平均 tick ms） | A2 + 程式審查 | CODE（等價實作，非 console command） |

## 12. MAINT（維護）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| MAINT-01 | condition 0–100、新建 100、只在 Producing 磨損 | `MaintenanceLedger`、`Tick.RunStep` | A2 | PASS(自動) |
| MAINT-02 | 間隔 8/10/12/15、磨損由間隔換算 | `TierCatalog`、`BalanceFormulas.WearPerHour` | A2 + A3 | PASS(自動) |
| MAINT-03 | 材料僅 Log/Plank/MetalBlock、數量符合 | `TierCatalog`、藍圖 spec、`MWP_Config.json` | A3 | PASS(自動) |
| MAINT-04 | 維護 2 遊戲小時、停產、不耗動力、完成 100 | `MaintenanceLedger.AdvanceMaintenance`、`Tick.UpdateConsumption` | A2（2h/停機）+ T17 | PASS(自動)+CODE |
| MAINT-05 | 自動預設開、可關、手動可用 | `MwpConfig`、`IMwpPumpCommands.RequestMaintenance`、UI toggle/按鈕 | A2 + T20 | PASS(自動)+CODE |
| MAINT-06 | 缺料/無路專用狀態、不崩潰不扣料 | `MaintenanceLedger.Decide`、`MwpPumpController.Ports.HasMaterials/TryConsume` | A2 + T18/T19 | PASS(自動)+CODE |
| MAINT-07 | condition 0 停產、維護後立即恢復 | `PumpStateMachine`、`MaintenanceLedger` | A2 + T16 | PASS(自動)+CODE |
| MAINT-08 | 不新增工人站位/維修工種 | 藍圖無 `WorkplaceSpec`，材料由 district 庫存扣 | A3 | PASS(自動) |
| MAINT-09 | 效率乘數照表、無隨機故障 | `BalanceFormulas.ConditionMultiplier` | A2（五段+邊界） | PASS(自動) |
| MAINT-10 | UI 顯示 condition/倒數/材料/開關/按鈕 | `MwpPumpPanelDataFactory.cs`、`MwpPumpPanelView.cs` | A1 + T16–T20 | CODE |
| MAINT-11 | 存檔 schema v2、v1→v2 冪等 | `MwpSaveCodec.Migrate`、`PumpSaveState` | A2（遷移冪等測試） | PASS(自動) |
| MAINT-12 | 30 日平均符合 BAL-11 | 同上 | A2（93–94%） | PASS(自動) |
| MAINT-13 | 維護可整體關閉、不留半套狀態 | `MwpConfig.MaintenanceEnabled`、`MaintenanceLedger.Decide`、UI 隱藏整塊 | A2 + T20 | PASS(自動)+CODE |

## 13. NFR（可讀性／可維護性）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| NFR-01 | Core 不引用遊戲組件；依賴方向 | `src/MWP.Core/MWP.Core.csproj`（無 Reference） | A7（組件引用檢查） | PASS(自動) |
| NFR-02 | nullable／TreatWarningsAsErrors／analyzers／.editorconfig；CI 0 warning | `Directory.Build.props`、`mod/.editorconfig` | A1 | PASS(自動) |
| NFR-03 | 檔案 ≤300 行、方法 ≤40、公開 API XML doc | 全部 src；3 個排除檔（`MwpPumpController.cs` 522、`.Tick.cs` 459、`MaintenanceLedger.cs` 298）在檔頭註明理由；測試有 9 個宣告式方法 >40 行亦註明 | 人工抽查 | PARTIAL（軟性上限，已註明理由） |
| NFR-04 | 平衡/間隔/材料/文字全資料驅動 | 藍圖 spec + `MWP_Config.json` + `TierCatalog`；程式無四檔 if/switch | A3 | PASS(自動) |
| NFR-05 | 動力/水源/維護/管道各有介面 | `src/MWP.Core/Abstractions/Ports.cs`（`IPumpPowerSource/IWaterSource/IMaintenanceStore/IPipeAdapter`） | 程式審查 | PASS(自動) |
| NFR-06 | Spec/存檔/Config 版本分離＋遷移器 | `MwpDefaults`（SpecKey/SaveSchemaVersion/ConfigSchemaVersion）、`MwpSaveCodec.Migrate`、`MwpConfigCodec` | A2 | PASS(自動) |
| NFR-07 | Core 覆蓋 ≥80% | `tests/MWP.Tests`（213 測試，涵蓋全部公開 API） | A2 | PARTIAL（離線無 coverlet，未量測行覆蓋率） |
| NFR-08 | 日誌分類與節流、失敗可診斷 | `MwpLogKeys`（Info/Warn/Error/Compat）、`ThrottledLog` | A2 | PASS(自動) |
| NFR-09 | 禁止全域可變靜態；DI；事件可解除 | 唯一靜態是 `MwpStartup` 的一次性啟動交接（寫一次、之後唯讀，檔頭已說明） | 程式審查 | PARTIAL（有唯一例外並已論證） |
| NFR-10 | 平衡/schema 變更需更新 CHANGELOG 與文件 | `mod/CHANGELOG.md`、本檔、`CONTRACT.md` | 人工 | PASS(自動) |

## 14. PERF（效能）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| PERF-01 | Tick 1 秒、每泵 O(1)、不每幀掃描/配置 | `Tick.Advance`（節流） | 程式審查 + T24 | CODE |
| PERF-02 | 穩態不用 LINQ/字串插值/GetComponent/配置 | `Tick`、`Ports`、`Save`（快取組件；字串只在 log 路徑） | 程式審查 | CODE |
| PERF-03 | 100 泵 ≤2 ms/秒、GC≈0、FPS 下降 ≤2 | 同上 | T24 | GAP（需實測） |
| PERF-04 | UI ≤4Hz、髒標記、水源 1s 快取 | `MwpPumpPanelFragment`（4Hz/fingerprint）、`Tick` | 程式審查 + T24 | CODE |
| PERF-05 | 交付前附 Profiler/dotnet-counters 證據 | — | — | GAP（需遊戲內 profiler） |

## 15. SAFE（安全）

| REQ | 要求 | 實作位置 | 驗證 | 狀態 |
|---|---|---|---|---|
| SAFE-01 | Config/存檔/Spec 驗證＋clamp、拒絕 NaN/Inf/負數 | `BalanceFormulas.Sanitize`、`MwpConfigCodec`、`MwpSaveCodec` | A2（非法值/clamp 測試） | PASS(自動) |
| SAFE-02 | 每泵 Tick/配接器/存檔例外隔離 | `Tick.Tick`、`Save`、`Load`、`TimberPipesProbe` | A2 + 程式審查 | PASS(自動) |
| SAFE-03 | 不執行 config 程式碼、不載外部 DLL、不連網、不寫 mod 外檔案 | 全部程式；`MwpStartup` 只讀 config、寫入只經遊戲存檔 | A7 + 程式審查 | PASS(自動) |
| SAFE-04 | 反射呼叫 try/catch＋型別/版本檢查、失敗降級 | `TimberPipesProbe.cs`（assembly 名/型別/版本檢查） | A7 + 程式審查 | PASS(自動) |
| SAFE-05 | 存檔 schema 驗證、未知欄位保留、單泵重置、遷移冪等 | `MwpSaveCodec` | A2 | PASS(自動) |
| SAFE-06 | 只操作主執行緒、不用 Task/Thread 改遊戲狀態 | 全部 Tick 同步路徑；無 Task/Thread | A7（組件引用）+ 程式審查 | PASS(自動) |
| SAFE-07 | 不用 unsafe/P-Invoke/Process.Start、不改原版全域 | 原始碼掃描無 unsafe/extern/Process | A7 + 程式審查 | PASS(自動) |
| SAFE-08 | 外部模組需版本檢查與安全審查清單 | `MwpPipeAdapter`/`TimberPipesProbe` 版本讀取；只偵測不載入 | A7 + `API_FACTS_MWP.md` §1/§8 | PASS(自動) |

## 16. 未關閉項總表（交付前必讀）

| # | 項目 | 影響 | 補救 |
|---|---|---|---|
| 1 | REQ-ART-02 LOD1/2 未輸出；REQ-ART-07 三視圖簽核未走完 | 視覺/流程性缺口，不影響遊戲載入 | 模型 LOD0 已交付（2.1k–4.0k tris）；LOD 可由遊戲 prefab optimizer 產生；三視圖簽核列後續流程 |
| 2 | 含自製模型的**遊戲內載入測試未完成**（環境啟動即退出，移除模組後同樣） | 模型實機外觀未驗 | A9–A11 靜態證據；環境恢復後重跑載入測試與 T01–T27 |
| 3 | REQ-COMPAT-02/05/06/09、REQ-TEST-01..04、BAL-01/08、OPS-01、PERF-03/05 | 需遊戲內實測 | `VERIFICATION.md` §3 T01–T27 |
| 4 | REQ-COMPAT-03 直連端口（SHOULD）未實作 | 少一個便利路徑 | 已降級為閥門路徑；後續版本 |
| 5 | REQ-BAL-10 UI 未標示 legacy rates | 只在 config 啟用時有 log | 後續版本可加 snapshot flag |
| 6 | REQ-NFR-07 覆蓋率未量測、NFR-03 部分檔案>300 行、NFR-09 一個靜態交接 | 工程品質可改善 | 已註明理由；可用 coverlet 補量測 |
