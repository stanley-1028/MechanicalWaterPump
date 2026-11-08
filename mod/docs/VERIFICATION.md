# 驗證報告（VERIFICATION）

> 本檔記錄**實際跑過**的驗證與其輸出，以及**無法離線執行**、必須遊戲內人工驗收的項目。
> 原則：沒有輸出證據的項目不得標 Pass。
>
> 最後更新：2026-11-08（v0.2.2：BUG-06／BUG-07）｜證據日誌：`docs/evidence/build-0.2.2.log`
> （0.2.1 搬運實機：A16／A18；v0.2.0 回歸：`docs/evidence/build-2026-11-06.log`；同日 08:00 回歸：`docs/evidence/build-2026-11-06-0800.log`；前次回歸：`docs/evidence/build-2026-11-05.log`；v0.2.0 原始建置：`docs/evidence/build-0.2.0.log`；art pass 建置：`docs/evidence/build-art-0.2.0.log`）

## 1. 自動化驗證（可重跑）

全部以 `pwsh -File mod/build.ps1`（本機 PowerShell 5.1 亦可）一次跑完，exit code = 0。

| # | 驗證 | 指令 | 實際輸出 | 狀態 |
|---|---|---|---|---|
| A1 | 4 個組件建置 0 warning / 0 error | `dotnet build ... -c Release -m:1`（build.ps1） | 4 次「0 个警告 / 0 个错误」（MWP.Core、MWP.Gameplay、MWP.Integration、MWP.UI） | **PASS** |
| A2 | L1 單元測試 | `dotnet run --project mod/tests/MWP.Tests -c Release -m:1` | `PASS 213/213`（exit 0） | **PASS** |
| A3 | 資料層＋組件契約驗證（對真實遊戲 metadata） | `tools/validate … mod/data --managed …` | `VALIDATION PASSED`、`WARN: none`（507 spec types、41 loc keys、1067 vanilla blueprints、10 blueprints） | **PASS** |
| A4 | 打包完整性 | `build.ps1` stage 檢查 | `mod/build/MWP`：4 DLL + 16 `.timbermesh`（8 成品 + 8 施工階段）+ 8 藍圖 + 2 TemplateCollection + 3 語系 CSV + MWP_Config.json + manifest.json + 8 圖示（含 meta）= 51 檔、1,107 KB（1,133,468 bytes；2026-11-06 22:14 回歸實測，動畫 art pass 後） | **PASS** |
| A5 | 自訂 Spec 可被 metadata 發現且欄位正確 | `tools/validate`（AssemblyChecks） | `MechanicalWaterPumpSpec`：public、非 abstract、繼承 `ComponentSpec`、`[SpecAlias("MWP.MechanicalWaterPumpSpec")]`、11 個 `[Serialize]` 欄位名稱/型別吻合（`MaintenanceMaterials` 含 `{Id,Amount}` 元素） | **PASS** |
| A6 | DI／入口型別 | `tools/validate`（AssemblyChecks） | 恰一個 `IModStarter`（public 無參數 ctor）、Configurator 子類存在、IL 找到 `TemplateModule/Builder.AddDecorator<MechanicalWaterPumpSpec, …>` 且 decorator 型別存在 | **PASS** |
| A7 | 無硬依賴／依賴方向 | `tools/validate`（AssemblyChecks） | `MWP.Core` 只引用 netstandard/System.*；無 TimberPipes 組件引用；無 NuGet 依賴；`MWP.UI` 只引用 `MWP.Core` | **PASS** |
| A8 | **遊戲載入測試（自動、主選單層級）** | 安裝到 `Documents\Timberborn\Mods\MechanicalWaterPump` → 啟動 Timberborn 1.1.2.8 → 讀 `Player.log` → 自動關閉 | mod 出現在已載入清單 `- Mechanical Water Pump (v0.2.0)`；`[MWP/Info][MWP/Info/Config] MWP 0.2.0 … schema=2 tiers=4 maintenance=on`；**全 log 0 個 Exception**。證據：`docs/evidence/loadtest-2026-11-04-Player.log`（此輪為暫代模型時期；自製模型版的載入測試受環境阻塞，見下） | **PASS（載入層）** |
| A9 | **自製模型驗證（16/16，含動畫，B 版）** | `python art/validate_timbermesh.py` | `FILES: 16/16 present; 16 PASS, 0 FAIL`、`CHECKS: 322 PASS, 0 FAIL`（C1–C17）、exit 0；bounds (2,3,3)/(2,3,3)/(3,4,3)/(3,4,4)、LOD0 tris 2,072/2,788/3,948/3,768（≤12k）；每檔 1 root + 3 `#Port#` + N 個 `#Animated*` 子節點（5/6/7/5）、0 `#Slot#`；**8 檔 finished 共 46 條 nodeAnimations**（120 frames @24fps、seamless、rest==frame0、C15 逐幀對照誤差 ≤1.5e-7）、construction 0 動畫；signed volume 全正（C9）、動畫軸 vs 幾何軸（C16）、未被 merge（C17）；UV 密度 wood 117–145 / metal 121–164 / planks 255–340 / glass 82 / plaster 340 px/m（≥80）；`mod/data` 與 `art/timbermesh` SHA-256 相同；gen/export 兩次 byte-identical。16 檔合計 653,318 B / 30,320 tris。Mk I/II 為全木材（FT: BaseWood_Brown/LightBrown + IrregularPlanks_Brown；IT: BaseWood_DarkBrown/Grey + IrregularPlanks_MetalForged） | **PASS** |
| A17 | **面板原生 UI（T12）** | `MWP.UI` 重寫 + `dotnet build`/全體 build | 使用遊戲 UXML 模板（`LoadVisualElement("Game/EntityPanel/...")`）與原生 class：`entity-sub-panel`、`entity-panel__text`、原生 `ProgressBar`、`entity-fragment__button` 九宮格按鈕、原生 toggle；移除全部 inline `style.`（grep 0 筆）；機械動力/庫存/深度改由原生 fragment 顯示（`SimpleOutputInventoryFragmentEnabler` decorator）；`TextLocalizationWrapper` 警告根因（共用 key 參數型別）已修 | **PASS（build）／待實機外觀** |
| A16 | **搬運 decorator 鏈（BUG-04，2026-11-08 實機通過）** | IL + `MwpGameConfigurator.ProvideTemplateModule()` + 自動遊戲內回合 | `AddDecorator<SimpleOutputInventory, EmptyOutputWorkplaceBehavior / SimpleOutputInventoryHaulBehaviorProvider / HaulCandidate>()`；遊戲內實測（注入一個原版 HaulingPost + 2 名搬運工到回報存檔的副本）：搬運工把水從**所有未暫停**的泵搬到水箱——Basic 15→0、Intermediate 30→0、Advanced 75→61，被清空的小水箱長到 30/30；暫停的 Mk III 維持 100（原版規則：暫停建築被 block、不清空）。證據：`docs/evidence/haul-in-game-2026-11-08.log` | **PASS（實機）** |
| A18 | **「無搬運站」狀態回饋（BUG-05，2026-11-08）** | IL 對照原版 + 自動遊戲內回合 | 原版輸出建築（`FireworkLauncherStatus`、`GoodConsumingBuildingStatusInitializer`）會加 `NoHaulingPostStatus` 並在 finished 時 `Initialize(Func<bool>)`；MWP 原本沒有 → 新增 `NoHaulingPostStatus` + `MwpHaulStatus` 兩個 decorator（`MwpHaulStatus` 需 `Bind<...>().AsTransient()`，否則載入拋 `BinditoException`）。實機驗證（原始回報存檔、無搬運站）：4 個泵 `candidate=True enabled=True providers=1 emptyOutput=True noHaulStatus=True haulStatusInit=True inRange=False`、未暫停的 Basic 提供 `[w=1.000 EmptyOutputWorkplaceBehavior]`、整段載入+執行 **0 個 Exception**。證據：`docs/evidence/haul-status-in-game-2026-11-08.log` | **PASS（實機；0.2.2 已依使用者要求移除，改由 A20 記錄）** |
| A19 | **0.2.2 離線驗證（2026-11-08）** | `build.ps1 -Clean` | exit 0；4 個組件「0 个警告 / 0 个错误」；單元測試 `PASS 213/213`；資料/組件驗證 `VALIDATION PASSED`（另有 4 個既有的 game-type `AddDecorator` WARN，非錯誤）；staged 套件 `mod/build/MWP`：10 blueprints、1,248 KB、manifest 0.2.2。證據：`docs/evidence/build-0.2.2.log` | **PASS（離線）** |
| A20 | **BUG-06：移除「需要搬運工」狀態（0.2.2，2026-11-08，使用者要求）** | 原始碼審查 + `build.ps1`／validator | `MwpGameConfigurator.ProvideTemplateModule()` 只剩搬運鏈三件套 decorator（`EmptyOutputWorkplaceBehavior` + `SimpleOutputInventoryHaulBehaviorProvider` + `HaulCandidate`）；`NoHaulingPostStatus`／`MwpHaulStatus` decorator 與 `Bind<MwpHaulStatus>()` 全部移除；`src/MWP.Gameplay/Components/MwpHaulStatus.cs` 已刪除；`MwpUiVanillaKeys` 不再保留原版 NoWater 提示 key。行為：有**已雇工搬運站**時原版搬運工照樣把泵內的水搬去水箱；沒有搬運工時泵只會裝滿內部水槽後停產（與原版一致），**不再顯示**「需要搬運工才能運轉」警告 | **PASS（原始碼/建置；實機回歸 T06/T07 待辦）** |
| A21 | **BUG-07：抽水深度不限（MaxDepth 64，0.2.2，2026-11-08，使用者要求）** | 8 藍圖 + `MWP_Config.json` + `TierCatalog`／自訂 spec 一致性檢查 + validator | 四檔 × 兩陣營的 `WaterInputPipeSpec.MaxDepth` 與 `MWP.MechanicalWaterPumpSpec.MaxDepth` 皆 64；`MWP_Config.json` 四檔 `maxDepth` 64；`TierCatalog` 64（`DataChecks`／`ConfigValidator` 會比對，`VALIDATION PASSED`）；NoWater 提示改 MWP 自己的 `UI.MWP.HintNoWater`（三語系新增 key；原版 `Status.Buildings.NeedsWater`「需要更深的水位」不再使用）；原版 `WaterInputPipeDepthFragment` 仍顯示 `Limit 64`（等效不限）。進水管可達河床，任何水位（含低水位／旱季退水）皆可抽 | **PASS（離線）／遊戲內 T04 待實測** |
| A15 | **實機 prefab 結構（ModelDiag，2026-11-07）** | 使用者載入世界後讀 `Player.log` 的 `MWP/Info/ModelDiag/*` | Mk III/IV/II/ I 四檔的 `#Finished` 下都有完整模型：root mesh（Advanced 6,633 verts）＋ `#Animated*` 子節點（Advanced：3×Piston 74、Flywheel 782、HeadPulley 698、GearboxGear 672、DriveShaft 392，全部 `renderer=on`）＋ 3 個 `#Port#`；施工模型 renderer=off。**證明「Mk III 空無一物」不是幾何缺失**，而是內裝密度/構圖（主齒輪在右外側、玻璃窗後為空）＋夜間光照；已開 T11 內裝強化 | **PASS（結構）／T11 美化中** |
| A14 | **winding／背面檢查（BUG-03 回歸）** | 以 protobuf 重建 mesh + backface culling 渲染；量測每 mesh signed volume | 修正前 Advanced TOTAL **−6.721104**（全部 mesh INVERTED，重現遊戲內「空心/揉皺」）；修正後同 magnitude **+6.721104**、8 finished＋8 construction 全 OUTWARD、0 inverted。validator 新增 signed-volume>0 絕對檢查；`art/reports/t10/winding-diagnosis-before.txt` / `-after.txt`、`backface_broken_advanced.png` / `backface_fixed_advanced.png` | **PASS（已修）** |
| A13 | **動畫 runtime 機制確認** | 遊戲 IL（`MechanicalSystemUIConfigurator.ProvideTemplateModule`、`MechanicalNodeAnimator`） | `AddDecorator<MechanicalNodeAnimatorSpec, MechanicalNodeAnimator>` 存在；`speed = PowerEfficiency × NonlinearAnimationManager.SpeedMultiplier`；`CanAnimate()` 在無動力時 `Enabled=false`。藍圖已含 `MechanicalNodeAnimatorSpec{MinSpeedMultiplier:0.0}`，因此動畫隨供電轉動、停電停止（**待實機目視確認**） | **PASS（IL）＋待目視** |
| A10 | **圖示（8 張）** | 檢查 `mod/data/Icons/MWP*.png` | 8 張皆 128×128 RGBA、四角 alpha=0、無文字、21,969–30,496 B（動畫版模型重渲染）；`.png.meta.json` 未變動；art validator 與 `tools/validate` 皆通過 | **PASS** |
| A11 | **自製模型材質可解析** | 對照遊戲 `MaterialCollection.{Folktails,IronTeeth}.blueprint.json` | 使用的 13 個材質名全部存在於對應陣營 MaterialCollection（無自創材質、無 `PaintedMetal` 大面積使用）；模型僅以名稱引用原版材質，不打包原版貼圖 | **PASS（靜態）** |
| A12 | **實機互動（2026-11-05 12:41–13:20，首次進世界）** | 使用者手動開新檔→建 Mk I/II 泵→截圖＋`Player.log` | ✅ mod 完整載入（`MWP 0.2.0 ready`）；✅ Timber Pipes 偵測（valve extraction available）；✅ 泵可建造/放置、面板完整顯示（狀態、產量、動力需求/供給、水質、抽水深度、庫存、機況、下次維護、材料、自動維護 toggle、立即維護/暫停、管道提示）；✅ zhCN 顯示名「机械饮水泵 Mk I/II」（與原版「机械水泵」區隔）、深度 widget 2/4 米、能量輪 100%/200hp；❌ **BUG-01** 海狸飲水 NRE（已修，重啟後 5+ 分鐘未再現）；❌ **BUG-02** 庫存上限顯示 `2147483647`（已修，改用 tier 設計容量）。證據：使用者截圖 12:59:24 / 12:59:32 ＋ `Player.log` | **PASS（建造/面板/本地化）＋2 bug 已修，待修後重測截圖** |

> A12 補充（BUG-02）：`SimpleOutputInventoryInitializer` 以 `CreateWithUnlimitedCapacity` 建立庫存，真正上限由 allowed-goods 表控制，因此 `Inventory.Capacity == int.MaxValue`；`PumpSnapshot` 舊版誤用該值。已改用 `TierCatalog.InternalCapacity`（15/30/75/100），重建後 `PASS 213/213`、`VALIDATION PASSED` 並重新安裝。

> A12 截圖判讀（正面驗收）：面板欄位與需求 §12.1/§20.2.5 對照——狀態列（內部水槽已滿）、實際產量 0.0（滿槽時正確）、動力需求 100/200 hp、目前供電 100%、水質 100%、抽水深度 2/4 格、庫存、機況 97/98%、下次維護 7.8/9.8 日、需要 10 原木/15 木板、自動維護、立即維護/暫停、管道「请用阀门抽取」——全部出現且數值合理。

> 2026-11-05 回歸重跑（08:00–08:01，Asia/Macau）：`powershell -NoProfile -ExecutionPolicy Bypass -File mod\build.ps1 -Clean`
> → exit 0；A1 4 次「0 个警告」、A2 `PASS 213/213`、A3 `WARN: none` / `VALIDATION PASSED`、A4 51 檔 / 1,033 KB。
> 本次僅重跑與記錄，未改程式、資料或平衡值。完整輸出：`docs/evidence/build-2026-11-05.log`。
>
> 2026-11-06 回歸重跑（08:00–08:01，Asia/Macau，定時任務自動觸發）：`powershell -NoProfile -ExecutionPolicy Bypass -File mod\build.ps1 -Clean`
> → exit 0；A1 4 次「0 个警告 / 0 个错误」、A2 `PASS 213/213`、A3 `507 spec types / 41 loc keys / 1067 vanilla blueprints / 10 blueprints` ＋ `WARN: none` / `VALIDATION PASSED`、A4 51 檔 / 1,040 KB（1,065,245 bytes）。
> 本次未改程式、資料或平衡值。A4 由 1,033 KB 增至 1,040 KB（+7,584 bytes）**不是本次造成的變更**：BUG-01／BUG-02（11-05 13:0x）與 DV-06（11-05 19:23）的修正都落在 11-05 08:00 那次建置之後，舊數字是修正前快照，故以本次實測為準。
> 完整輸出（已保留）：`docs/evidence/build-2026-11-06-0800.log`。
>
> 2026-11-06 回歸重跑（22:02–22:04，Asia/Macau）：`powershell -NoProfile -ExecutionPolicy Bypass -File mod\build.ps1 -Clean`
> → exit 0；A1 4 次「0 个警告 / 0 个错误」、A2 `PASS 213/213`、A3 `507 spec types / 41 loc keys / 1067 vanilla blueprints / 10 blueprints` ＋ `WARN: none` / `VALIDATION PASSED`、A4 51 檔 / 1,107 KB（1,133,468 bytes）。
> 本次未改程式、資料或平衡值；套件由 08:00 的 1,040 KB 增至 1,107 KB，來自 18:07 的動畫自製模型 art pass（8 個 finished 與 8 個施工階段 `.timbermesh` 更新），與程式／平衡無關；A4 已更新為本次實測。
> 完整輸出（該檔已於 22:14 同檔名回歸覆寫）：`docs/evidence/build-2026-11-06.log`。
>
> 2026-11-06 回歸重跑（22:13–22:14，Asia/Macau）：`powershell -NoProfile -ExecutionPolicy Bypass -File mod\build.ps1 -Clean`
> → exit 0；A1 4 次「0 个警告 / 0 个错误」、A2 `PASS 213/213`、A3 `507 spec types / 41 loc keys / 1067 vanilla blueprints / 10 blueprints` ＋ `WARN: none` / `VALIDATION PASSED`、A4 51 檔 / 1,107 KB（1,133,468 bytes）。
> 本次未改程式、資料或平衡值；輸出結構與 22:02–22:04 回歸相同（僅建置耗時秒數不同），暫存套件 51 檔 / 1,133,468 bytes 與 22:04 實測完全一致（內容未漂移）。
> 完整輸出：`docs/evidence/build-2026-11-06.log`（本次覆寫；無 BOM、75 行）。
>
> A8 補充（誠實）：2026-11-04 23:14 之後重試載入測試時，**Timberborn 啟動後立即退出**；把整個模組移出 Mods 目錄後同樣發生（`Player.log` 只到引擎初始化＋shutdown，無例外），且 `steam://rungameid` / `Steam -applaunch` 也無法啟動 → **環境問題（Steam/工作階段），不是本模組造成**。因此「含自製模型的遊戲內載入/外觀」尚未驗證，列入待實測；A9–A11 為其靜態替代證據。
> A8 說明：log 中的 `Invalid number of columns found in enUS (line 2..18)` 與大量 zhCN 缺鍵是**其他本地/工作坊模組**既有的問題（安裝我們的 mod 之前的 `Player-prev.log` 已有同樣錯誤；我們的 3 份 CSV 為合法 3 欄、無缺鍵）——不是 MWP。

> 2026-11-08 搬運實機驗證（BUG-04 / BUG-05）：以 `-skipModManager -settlementName MWPHaulTest -saveName mwptest/mwpnohaul` 自動載入回報存檔的副本並跑遊戲內回合；兩份證據見上方 A16/A18。
> 結論：0.2.0 的三件套 decorator 在**有搬運站+搬運工**時確實運作；回報者存檔裡 4 個泵的水都沒被搬，是因為該聚落**沒有任何 HaulingPost / 搬運工**（6 隻海狸全在 DistrictCenter/BoxFarm 任職）。另外原版規則下**暫停中的泵不會被清空**（回報存檔的 Mk II–IV 都是 PausableBuilding.Paused=true）。0.2.1 因此補上原版同樣的「No hauling post in range」狀態回饋。
> **0.2.2 更新（2026-11-08，使用者要求）**：上述「No hauling post in range」狀態已移除（BUG-06，見 A20）；BUG-04 的搬運鏈不受影響，A16 仍有效。BUG-05 的狀態回饋正式撤銷。

補充證據：
- `MWP.Gameplay` 的 `AddDecorator` IL 由 `ApiIndex il` 直接確認（`MwpGameConfigurator::ProvideTemplateModule`）。
- 資料驗證器做過**負向測試**（故意注入錯科學點／速率／Blocks 截斷／全實心 footprint／假模型路徑／缺 icon／
  缺 loc key／placeholder 不一致／config schema 錯／manifest Id 錯／未知 spec 欄位／enum 錯），全部正確報
  `ERROR:` 並 exit 1，證明不是空過。

## 2. 需求層硬性驗收（§15.1 + §20.4 追加）

| # | 條目 | 離線證據 | 遊戲內 | 狀態 |
|---|---|---|---|---|
| 1 | 四檔可研究、建造、拆除；無工人產水 | A3（8 藍圖、無 WorkplaceSpec） | T01/T02 | 部分（待 T01） |
| 2 | 100% 供電、乾淨水、24h 產量誤差 ≤ ±5% | A2（24h 公式閉環）、A3（數值） | T02/T27 | 部分（待實測） |
| 3 | 0 動力／0 水源產水 = 0；恢復自動繼續 | A2（狀態機/公式） | T03 | 部分（待實測） |
| 4 | 庫存滿停產；搬運可取貨 | A2（StorageFull） | T06/T07 | 部分（待實測） |
| 5 | 狀態與 UI 原因正確、無靜默停產 | A2（優先序）＋UI 已完成（A1） | T05 | 部分（待目視） |
| 6 | 未安裝 Timber Pipes：無錯誤、存檔正常 | A7（無硬引用）＋程式 try/catch | T11 | 部分（待實測） |
| 7 | Timber Pipes：閥門 Extract（MUST）；直連失敗降級 | 設計：標準 Water 庫存；直連為偵測降級 | T10 | **待實測（MUST 路徑最大不確定性）** |
| 8 | 與 Pump to Tank／Filter Pump／Barkimedes 共存 | 不覆寫原版藍圖、無 Harmony | T12 | 待實測 |
| 9 | 存檔／讀檔／移除模組／增刪管道後可載入 | A2（v1→v2、corrupt reset、未知欄位保留） | T09/T15 | 部分（待實測） |
| 10 | 100 泵幀率下降 ≤3 FPS、無記憶體暴增 | 設計：1s tick、O(1) | T13/T24 | 待實測 |
| 11 | 四檔維護系統完整 | A2（磨損/材料/停機/存檔）＋A3（數值）＋UI | T16–T21 | 部分（待實測） |
| 12 | 30 日平均 90–105% | A2：四檔模擬 93–94% | 實測佐證 T22 | **PASS（模擬）** |
| 13 | 0 warning、Core 覆蓋 ≥80%、100 泵效能 | A1、A2；覆蓋率未量測（無 coverlet 離線） | T24 | 部分 |
| 14 | 非法 Config／損壞存檔／缺模組安全降級 | A2（clamp/corrupt/migrate） | T25 | 部分（待抽樣） |
| 15 | 「不低於原版」判準 T27 | 數值設計表（README/CONTRACT） | T27 | **待實測** |

## 3. 遊戲內測試矩陣（T01–T27，需人工）

前置：把 `mod/build/MWP/*` 複製到 `Documents\Timberborn\Mods\MechanicalWaterPump\`
（或 `build.ps1 -Install`），遊戲 Mods 選單啟用、重啟。Folktails 與 IronTeeth 各建一次。
每項記錄截圖／log／數值，填回「結果」欄。

| 用例 | 前置 | 步驟 | 預期 | 結果 |
|---|---|---|---|---|
| T01 | 新檔 FT | 解鎖並建造四檔 | 皆成功；成本／研究正確 | 待實測 |
| T02 | 任意 | 建造後不派工、接電 | 持續產水；24h 產量 72/144/360/480 ±5% | 待實測 |
| T03 | 任意 | 供電 0/25/50/75/100% | 速率單調；0 = 0 | 待實測 |
| T04 | 水邊 | 抽至河床／低水位（含旱季退水） | 仍可抽水；進水口真的無水才 NoWater | 待實測 |
| T05 | 水邊 | 0/50/100% 壞水 | 速率下降；不產壞水；不優於原版水泵 | 待實測 |
| T06 | 任意 | 庫存滿、無搬運 | 停產 StorageFull | 待實測 |
| T07 | 有路 | 搬運工取貨 | 庫存下降、恢復產水 | 待實測 |
| T08 | 無路 | 移除道路 | 顯示 NoAccess；不崩潰 | 待實測 |
| T09 | 任意 | 產水中存檔→讀檔 | 小數、狀態、暫停保持 | 待實測 |
| T10 | Timber Pipes | 閥門 Extract 至管道→水箱 | 水進管道與水箱（MUST） | 待實測 |
| T11 | 不安裝 Timber Pipes | 建造、產水、搬運 | 全部正常；log 無錯誤 | 待實測 |
| T12 | 共存模組 | 同時載入 Pump to Tank 等 | 無崩潰；互不影響 | 待實測 |
| T13 | 100 泵 | 全部滿載 | FPS 下降 ≤2–3 | 待實測 |
| T14 | IT 陣營 | 四檔建造與本地化 | 名稱／材質正確 | 待實測 |
| T15 | 存檔 | 移除本模組後載入 | 可載入、無未知節點錯誤 | 待實測 |
| T16 | 關閉自動維護 | 滿載運轉至 condition 50/25/0 | 效率 0.90/0.75/0.50/停產 | 待實測（乘數已由 A2 覆蓋） |
| T17 | 材料充足、有路 | 觸發維護 | 扣材料、2h 停機、condition=100 | 待實測 |
| T18 | 材料不足 | 觸發維護 | MissingMaintenanceMaterials；不扣料 | 待實測 |
| T19 | 無路 | 觸發維護 | NoAccessForMaintenance；可恢復 | 待實測 |
| T20 | 任意 | 自動／手動／關閉三模式 | 行為一致；關閉後不自動維護 | 待實測 |
| T21 | v1 存檔 | 載入並升級 v2 | condition=100、auto=true；未知欄位保留 | 待實測（codec 已由 A2 覆蓋） |
| T22 | 任意 | 30 日平均產量（含維護） | 90–105% 表定值 | 待實測（模擬 93–94%） |
| T23 | 任意 | 靜態分析＋單元測試 | 0 warning；Core 覆蓋 ≥80% | 部分（A1/A2；覆蓋率未量測） |
| T24 | 100 泵 | 滿載＋維護運轉 | ≤2 ms/秒、GC≈0、FPS 下降 ≤2 | 待實測 |
| T25 | 任意 | 非法 Config／損壞存檔／NaN | 全部安全降級 | 待抽樣（A2 已覆蓋核心） |
| T26 | 任意 | useLegacyRates=false/true | 預設 3/6/15/20；舊值需明示 | 待實測（UI 標示缺口，見 traceability） |
| T27 | 原版對照 | Mk I–IV 與對照泵同條件比較 | 符合 §20.1.3 判準 | 待實測 |

## 4. 「不低於原版」對照表（T27 用）

| 檔位 | 本模組滿載 | 原版對照 | 原版滿載 | 判準 |
|---|---:|---|---:|---|
| Mk I | 3 Water/h | WaterPump ×1（1 工） | 3/h | ≥，且 0 工人 |
| Mk II | 6 Water/h | WaterPump ×2（2 工） | 6/h | ≥，且 0 工人 |
| Mk III | 15 Water/h | LargeWaterPump ×1 滿員（3 工） | ≈15/h | ≥，且深度不限（MaxDepth 64）>4、占地 3×3<3×4 |
| Mk IV | 20 Water/h | LargeWaterPump ×1.33 滿員 | ≈20/h | ≥，且 0 工人 |

實測時以 24 遊戲小時、100% 供電、乾淨水、庫存未滿量測，誤差 −5% 內視為達標（§20.1.3）。

## 5. 已知風險與未關閉項

| 項目 | 影響 | 緩解 |
|---|---|---|
| 含自製模型的**遊戲內載入/外觀驗收未完成** | 實機外觀、建造選單、端口位置未驗 | 環境問題（遊戲啟動即退出，移除模組後同樣）；A9–A11 靜態證據替代；環境恢復後跑 A8 + T01–T27 |
| LOD1/LOD2 未輸出（ART-02 部分） | 遠景效能未分階 | LOD0 僅 2.1k–4.0k tris（遠低於 12k），影響有限；可由遊戲 prefab optimizer 產生 |
| ART-07 三視圖簽核流程未走完 | 流程性項目 | 有 24 張預覽＋4×2 合圖；正式簽核列後續 |
| 遊戲內 T01–T27 未執行 | 無法宣稱完整驗收 | 本表 §3 作為交付後驗收腳本 |
| Timber Pipes 閥門與庫存識別（COMPAT-02） | MUST 驗收最大不確定性 | 標準 `SimpleOutputInventory` 語意；若失敗，文件改承諾並補直連端口 |
| 維護扣料依賴 `DistrictInventoryRegistry` / `DistrictCenter.AccessibleIsOnDistrictRoad` | 若 API 行為與預期不同，T17–T19 可能失敗 | 已以 IL 驗證；`docs/api/gameplay-findings.md` 記錄證據與降級 |
| 平衡未實測校正 | 產量誤差可能 >5% | `MWP_Config.json` 可調，不改程式 |
| 直連端口（COMPAT-03 SHOULD）未實作 | 少一個便利路徑 | MUST 閥門路徑不受影響 |
| UI 未標示 legacy rates（BAL-10 部分） | 僅影響測試模式觀感 | 後續版本可加 snapshot flag |

## 6. 重跑方式

```powershell
cd "C:\Users\user\Downloads\Mechanical Water Pump"
pwsh -File mod/build.ps1 -Clean        # A1–A4（本機若無 pwsh，用 powershell -File）
# 遊戲內：見 §3，逐項填回本表
```

## 發佈記錄（2026-11-08，0.2.0 首次公開發佈）

| 項目 | 值 |
|---|---|
| Steam Workshop item | **3835434644**（Timberborn app 1062090），URL https://steamcommunity.com/sharedfiles/filedetails/?id=3835434644 |
| Steam 內容 | 2.028 MB、tags: Update 1.1 / New content / Buildings；描述含 DeepSeek V4.1 Flash 署名；上傳工具 `mod/tools/steamupload`（Steamworks，eResult OK） |
| GitHub | https://github.com/stanley-1028/MechanicalWaterPump（public，main `ff16f32`） |
| GitHub release | `v0.2.0`，assets: `MechanicalWaterPump-0.2.0.zip`（1,814,944 B）+ `.sha256` |
| 建置 | 0 warning、`PASS 213/213`、`VALIDATION PASSED`；art 16/16 PASS（322 checks、62 動畫節點、37,232 tris） |
| 診斷 | `features.uiDiagnostics` 預設關閉；開啟後每個泵輸出 `MWP/Info/ModelDiag`（含 `hauling: HaulCandidate=… providers=…`） |
