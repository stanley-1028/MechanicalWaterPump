# MWP 架構（ARCHITECTURE）

> 對象：接手的開發者與審查者。需求編號見 `機械水泵模組需求文檔.md`；介面契約見
> `CONTRACT.md`；遊戲 API 證據見 `API_FACTS_MWP.md`。

## 1. 一句話

四個「機械動力 → 免人力飲用水」建築：用自訂 Component 把 `MechanicalBuilding` 的供電比例
轉成 Water 產出，寫進原版 `SimpleOutputInventory`，由原版搬運／閥門抽取；另加 condition
維護系統與可選的 Timber Pipes 偵測降級。

## 2. 分層（單向依賴，REQ-NFR-01）

```
MWP.Core       純 C#（netstandard2.1，零遊戲引用）
   ↑             公式、狀態機、維護帳本、Config/存檔編解碼、MiniJson、log 節流、DTO/介面
   ├── MWP.Gameplay   netstandard2.1，引用 Core + Timberborn 組件
   │     自訂 Spec、Controller（Tick）、維護扣料、存檔橋、DI、IModStarter、IMwpPumpQuery/Commands
   ├── MWP.Integration netstandard2.1，引用 Core + Timberborn 組件
   │     Timber Pipes 偵測、IPipeAdapter、降級與 Compat log；零硬依賴
   └── MWP.UI          netstandard2.1，引用 Core + Timberborn UI 組件
         實體面板 fragment：只讀 PumpSnapshot、呼叫 IMwpPumpCommands

MWP.Tests       net9.0 Exe，引用 Core（內建 runner，零 NuGet）
tools/ApiIndex  net9.0 + Mono.Cecil：離線 metadata/IL 探勘
tools/validate  net9.0 + Mono.Cecil：對真實遊戲 metadata 驗證資料層
```

為何 Core 必須無遊戲引用：公式、狀態機、config、存檔遷移可在沒有遊戲的機器上單元測試
（REQ-NFR-01/07、REQ-PERF-05 的可驗證性）。為何 UI 只引用 Core：UI 不得反向依賴
gameplay（REQ-NFR-05），資料透過 `IMwpPumpQuery`、操作透過 `IMwpPumpCommands`。

## 3. 執行期資料流（每泵，1 秒 Tick）

```
MechanicalBuilding.Efficiency ─┐
ActiveAndPowered ──────────────┤
WaterInput.IsUnderwater ───────┼─→ PumpStateMachine.Evaluate ─→ PumpState（UI 顯示）
WaterInput.ContaminationPercentage ┤        │
condition / maintenance phase ─┘           ↓
                              rate = WaterPerHour × r × q × conditionMult
                                           ↓
                         accumulator += rate × Δt；上限 1
                                           ↓
              floor(accumulator) → WaterInput.DemandCleanWaterAmount(0.2 m³/Water)
                              → RemoveCleanWater → Inventory.GiveProduced("Water")
                                           ↓
                    滿了 → StorageFull；扣不到水 → NoWater；無電 → NoPower
```

- 水體消耗沿用原版鏈路（`DemandCleanWaterAmount` + `RemoveCleanWater`），量測比例
  `WaterGoodToWaterAmountConverter = 0.2 m³/Water`（IL 已證實）。
- 污染效率與原版一致（`1 − c`，DV-01），避免「免費淨水器」。
- 維護：condition 100 → 每 Producing 小時扣 `100/(intervalDays×24)`；≤50% 自動維護
  （材料＋道路檢查 → `Maintaining` 2 遊戲小時 → 100）；0 停產。材料由 district 庫存扣。
- 存檔：每個泵元件一個 `IPersistentEntity`，`ComponentKey("MWP")`，schema v2，缺欄位安全
  預設，單泵損壞重置。

## 4. 資料驅動

- 四檔差異只在 `MWP.MechanicalWaterPumpSpec`（blueprint）＋ `MWP_Config.json`，程式沒有
  「四個 if/switch」（REQ-NFR-04）。
- 平衡預設集中 `TierCatalog`；執行期覆寫集中 `MwpConfigCodec`；blueprint 與 config 的
  一致性由 `tools/validate` 交叉檢查。
- 本地化 key 由 `MwpLocKeys` 產生，validator 檢查三語覆蓋（REQ-UI-05）。

## 5. 效能模型

- 每個泵每 Tick O(1)：無 LINQ、無字串插值、無 `GetComponent`（Awake 時快取）、無配置
  （REQ-PERF-01/02）。
- Tick 1 秒一次，由 controller 累積時間驅動（不是每幀 Update）。
- UI 4 Hz 髒標記；面板關閉不刷新（REQ-PERF-04）。
- 100 泵目標 ≤2 ms/秒；以 1 秒分攤計算，單泵預算 0.02 ms（REQ-PERF-03）。

## 6. 安全邊界（REQ-SAFE）

- Config/存檔/Spec 全部驗證＋clamp，拒絕 NaN/Inf/負數（Core `BalanceFormulas.Sanitize`）。
- 每泵 Tick、配接器呼叫、存檔讀取各自 try/catch，失敗只降級單泵。
- 不載入外部 DLL、不連網、不寫 mod 目錄外檔案、不 unsafe/P-Invoke/Process.Start。
- 只操作主執行緒；事件訂閱在 Delete/Destroy 解除。

## 7. 與原版的關係（紅線）

- 不覆寫原版藍圖／科技／水資源；四檔是新增 blueprint + TemplateCollection append。
- 不改原版 MechanicalPump；本模組只做「飲用水物品」，不與流體搬運泵競爭。
- 顯示名「機械飲水泵 Mk I–IV」，避免與原版「機械水泵」混淆（REQ-UI-04）。

## 8. 建置與打包

`mod/build.ps1`：依序 build 4 個組件（`-m:1`）→ 跑 Core 單元測試 → 跑資料驗證器（對真實
遊戲 metadata）→ 複製到 `mod/build/MWP/` 並做 manifest/檔案數檢查。`-Install` 會複製到
`Documents\Timberborn\Mods\MechanicalWaterPump`。
