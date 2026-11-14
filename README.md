# 機械水泵（Mechanical Water Pump）— Timberborn 模組工作區

以**機械動力換取免人力飲用水**的 Timberborn 模組：四檔免工人飲用水泵（Mk I–IV），
保留水源、水質、動力網路、維護與搬運物流的挑戰，並以軟依賴方式支援 Timber Pipes。

- Steam 工作坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3835434644
- 封面 AI 提示詞：[`mod/workshop/COVER_PROMPTS.md`](mod/workshop/COVER_PROMPTS.md)
- GitHub：https://github.com/stanley-1028/MechanicalWaterPump
  （release v0.2.4：https://github.com/stanley-1028/MechanicalWaterPump/releases/tag/v0.2.4；v0.2.3：https://github.com/stanley-1028/MechanicalWaterPump/releases/tag/v0.2.3；v0.2.2：https://github.com/stanley-1028/MechanicalWaterPump/releases/tag/v0.2.2）
- 需求（權威）：[`機械水泵模組需求文檔.md`](機械水泵模組需求文檔.md)（v0.2，以 §20 為準）
- 遊戲版本：Timberborn `1.1.2.8-ffc2de0-sw`（本機已安裝，Managed 組件可直接引用）
- 交付狀態：**v0.2.4（2026-11-14）：UI／GUI 全面原版化＋四檔專屬可收縮建造選單群組**
  （實體面板改為全部由遊戲原版控制項與原版 USS class 組成、不新增 UXML/USS/貼圖；底欄多一個
  MWP 群組圖示，展開 Mk I–IV、點其他群組即收合，與原版 Water／Food 群組相同）。
  離線驗證全綠：`build.ps1` exit 0、4 個組件 0 warning、單元測試 `PASS 213/213`、資料/組件驗證
  `VALIDATION PASSED`（43 鍵、11 藍圖含工具群組；staged 套件 52 檔／1,250 KB）。
  **實機**（2026-11-14，回報存檔副本自動載入）：成功進世界、`MWP 0.2.4 ready`、全 log
  0 Exception／0 MWP Error，底欄可見收合狀態的 MWP 群組圖示位於原版 Water 與 Food 之間；
  「點擊展開」與「選取水泵後的面板外觀」因本機無法合成輸入仍待人工（A24）。
  **其餘遊戲內驗收 T01–T27 仍待執行**（見 `mod/docs/VERIFICATION.md`）。
  歷史：v0.2.3 BUG-08 修正低流量／淺水進水口的抽水失敗；v0.2.2 BUG-06 移除「需要搬運工才能運轉」
  狀態＋BUG-07 抽水深度不限（MaxDepth 64）；v0.2.1 搬運實機驗證通過（BUG-04）＋「無搬運站」狀態
  回饋（BUG-05，已於 0.2.2 移除）；v0.2.0 首次公開發佈。
- 美術：**自製四檔模型（16 個 `.timbermesh`，8 檔 finished 含齒輪/飛輪/活塞節點動畫、62 條動畫軌；Mk III/IV 內裝已強化）
  與 8 張 128×128 圖示已交付**；材質用世界尺度平鋪 UV（木 117–145、金屬 121–164 px/m）。
  管線在 `art/`，驗證 16/16 PASS（322 checks）；剩餘：遊戲內動畫/外觀確認、LOD1/2、ART-07 三視圖簽核

## 製作與致謝

本模組與 **DeepSeek V4.1 Flash** 共同完成：程式（Core/Gameplay/Integration/UI）、資料與平衡、
自製模型/動畫/貼圖管線、離線驗證與文件，皆為人機協作的產出；需求、方向與遊戲內驗收由專案擁有者負責。

## 目錄

```
.
├── 機械水泵模組需求文檔.md        # 需求與驗收基準（v0.2）
├── design/DESIGN.md              # 設計決策（v0.1 基線；數值以需求 §20 為準）
├── research/                     # 考察筆記（原版機制、管道模組）
├── art/reference/PROMPTS.md      # 3D 參考圖規範與 AI 提示詞
├── README.md                     # 本檔
└── mod/                          # 模組本體（可打包）
    ├── build.ps1                 # 建置＋測試＋資料驗證＋打包
    ├── src/MWP.Core/             # 純 C#：公式、狀態機、維護、Config/存檔編解碼
    ├── src/MWP.Gameplay/         # Spec、Controller、維護、存檔、DI、IModStarter
    ├── src/MWP.Integration/      # Timber Pipes 偵測與降級（零硬依賴）
    ├── src/MWP.UI/               # 實體面板（狀態/機況/維護按鈕）
    ├── tests/MWP.Tests/          # L1 單元測試（零 NuGet）
    ├── tools/ApiIndex/           # 離線遊戲 API metadata/IL 探勘工具
    ├── tools/validate/           # 資料層驗證器（對真實遊戲 metadata）
    ├── data/                     # 8 藍圖、2 TemplateCollection、MWP 工具群組、三語系、Config、圖示
    └── docs/                     # CONTRACT / ARCHITECTURE / API_FACTS / VERIFICATION / …
```

## 建置與安裝

```powershell
cd "C:\Users\user\Downloads\Mechanical Water Pump"

# 建置 + 單元測試 + 資料驗證 + 打包到 mod/build/MWP
pwsh -File mod/build.ps1 -Clean
# 本機若沒有 PowerShell 7，改用 Windows PowerShell：
powershell -NoProfile -ExecutionPolicy Bypass -File mod\build.ps1 -Clean

# 直接安裝到遊戲（Documents\Timberborn\Mods\MechanicalWaterPump）
powershell -NoProfile -ExecutionPolicy Bypass -File mod\build.ps1 -Install

# 也可手動複製 mod/build/MWP/* 到 Documents\Timberborn\Mods\MechanicalWaterPump\
# 然後在遊戲 Mods 選單啟用，重新啟動遊戲
```

需求：.NET SDK 9（本機已有）；建置必須 `-m:1`（本機並行 MSBuild 會靜默失敗）。
遊戲路徑非預設時：`-TimberbornDir "D:\Steam\...\Timberborn"`。

## 功能與數值（v0.2，§20.1.2）

| 檔位 | 研究點 | 材料 | 尺寸 X×Y×Z | 產水 Water/h | 動力 hp | 抽水深度 | 庫存 | 維護間隔／材料 |
|---|---:|---|---:|---:|---:|---:|---:|---|
| Mk I | 150 | 20 Log | 2×3×3 | 3 | 100 | 不限（64） | 15 | 8 日／10 Log |
| Mk II | 400 | 30 Plank+10 Log | 2×3×3 | 6 | 200 | 不限（64） | 30 | 10 日／15 Plank |
| Mk III | 700 | 40 MetalBlock+20 Plank | 3×4×3 | 15 | 500 | 不限（64） | 75 | 12 日／10 MetalBlock+10 Plank |
| Mk IV | 1500 | 60 MetalBlock | 3×4×4 | 20 | 700 | 不限（64） | 100 | 15 日／15 MetalBlock |

- 抽水深度自 v0.2.2 起四檔皆為 `MaxDepth = 64`（等效不限）：進水管可延伸到河床，
  任何水位（含低水位／旱季退水）都能抽水；原版深度 fragment 顯示 `Limit 64`。

- 產水 = `R_base × 供電比例 × 水質係數 × 機況乘數`；0 動力或 0 水源時必為 0。
- 1 Water 消耗 0.2 m³ 水體（與原版 `WaterGoodToWaterAmountConverter` 一致，IL 已證實）。
- 污染效率與原版一致（`1 − c`）；需求 §3.4 的 `1 − 0.75c` 保留為 config 選項（DV-01，見文件）。
- **UI／GUI 原版化（v0.2.4）**：實體面板全部由遊戲原版控制項與原版 USS class 組成
  （原版九宮格子面板、`status-list-element` 狀態列與原版狀態圖示、`entity-panel__row`＋
  `inventory-row__image` 24 px 資訊列、原版 `ProgressBar`、`game-toggle`、原版 UXML 取出的
  九宮格按鈕），模組不新增 UXML/USS/貼圖；四檔有**專屬可收縮建造群組**（`Order 46`，
  原版 Water 45 與 Food 50 之間），底欄收合為一個 MWP 圖示、展開 Mk I–IV，
  與原版 Water／Food 群組相同的收縮行為（實作契約 `mod/docs/CONTRACT.md` §4.1／§10.1）。

- **搬運**沿用原版規則：聚落要有**已雇工的搬運站（Hauling Post）**，海狸才會把泵內的水搬到水箱；
  **暫停中的泵不會被清空**（與原版一致）。沒有搬運站時，泵只會把內部水槽裝滿後停產（與原版一致），
  **不再顯示**原版「No hauling post in range」／「需要搬運工才能運轉」狀態（0.2.1 加入、0.2.2 依使用者要求移除）。
  搬運鏈實機證據（BUG-04）：`mod/docs/evidence/haul-in-game-2026-11-08.log`。

## 測試與驗證

| 層級 | 內容 | 指令 | 結果 |
|---|---|---|---|
| L1 單元測試 | 公式、狀態機、維護、Config、存檔遷移、JSON | `dotnet run --project mod/tests/MWP.Tests -m:1` | `PASS 213/213` |
| 資料驗證 | 藍圖 spec/欄位/enum/block/本地化/config/icons 對真實遊戲 metadata ＋ 組件契約 A5–A7 | `tools/validate`（build.ps1 會跑） | `VALIDATION PASSED` |
| 遊戲內驗收 | T01–T27（產量、維護、管道、100 泵效能）＋ UI/GUI 原版化 | 人工，見 `mod/docs/VERIFICATION.md` | **T01–T27 待實測**；0.2.4 實機載入＋MWP 群組圖示**已通過**（A24），群組展開／面板外觀待人工 |

## 已知限制（誠實揭露）

1. **本輪遊戲內視覺/行為驗收未完成**：模型/材質已完成靜態驗證（16/16、材質全在 MaterialCollection）；
   **搬水行為已於 2026-11-08 以自動化遊戲內回合實測通過**（見 `mod/docs/VERIFICATION.md` A16；
   A18 的「無搬運站」狀態已於 0.2.2 依使用者要求移除，見 A20）；**低流量進水口抽水已於 2026-11-09
   以回報存檔副本自動回合驗證**（BUG-08，A22/A23）；**0.2.4 的 UI/GUI 已於 2026-11-14 實機載入
   驗證（世界載入、0 Exception、底欄 MWP 收合群組圖示位於 Water 與 Food 之間，A24）**。
   仍未驗：**點 MWP 群組展開的當下畫面**與**選取水泵後的實體面板外觀**（本機無法合成滑鼠/鍵盤
   輸入，`OpenInputDesktop` 拒絕；面板的原版 class/sprite 組成已離線對照原版 UXML/USS），
   以及 T01–T27 完整人工驗收。
2. **LOD1/LOD2 未輸出**：目前只有 LOD0（1.9k–4.3k tris，遠低於 12k 預算）。
3. **ART-07 三視圖參考圖簽核**：有 24 張等角/50%/施工預覽，但需求要求的「建模前三視圖＋端口座標
   簽核」流程尚未走完（流程性項目）。
4. **平衡未經實測校正**：3／6／15／20 與 100–700 hp 為需求值；30 日維護模擬為 93–94%，
   實測 24h 誤差仍需 ≤±5%。
5. **Timber Pipes 直連端口未實作**（SHOULD）：v0.2 只做偵測與降級；MUST 的閥門抽取靠原版
   標準 Water 庫存（`SimpleOutputInventory`）。
6. **UI 未標示 legacy rates**（REQ-BAL-10 部分）：`useLegacyRates` 只在 config/log；預設 false。
7. **覆蓋率未量測**（REQ-NFR-07）：離線無 coverlet，213 個測試涵蓋全部公開 API 但無行覆蓋率數字。

## 文件導覽

- `mod/docs/CONTRACT.md` — 實作契約（凍結的 API/JSON/存檔/本地化 key）
- `mod/docs/ARCHITECTURE.md` — 分層、資料流、效能與安全
- `mod/docs/API_FACTS_MWP.md` — 用 ApiIndex 從遊戲組件證實的 API 事實（含 IL 證據）
- `mod/docs/REQ_TRACEABILITY.md` — REQ 編號 → 實作 → 驗證方式
- `mod/docs/VERIFICATION.md` — 自動驗證結果與遊戲內待驗清單
- `mod/docs/ART_PLACEHOLDER.md` — 暫代美術與替換步驟
- `mod/docs/api/` — ApiIndex 產出的 IL/metadata dump（證據）

## 發佈（GitHub／Steam 工作坊）

### 打包
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File mod\release.ps1              # build + 打包
powershell -NoProfile -ExecutionPolicy Bypass -File mod\release.ps1 -SkipBuild   # 重用現有 build
powershell -NoProfile -ExecutionPolicy Bypass -File mod\release.ps1 -Install    # 額外安裝 upload-ready 版
```
產物在 `dist/`：
- `MechanicalWaterPump-<version>/`：upload-ready 模組資料夾（含 `workshop_data.json`、`thumbnail.png`、`thumbnail.png.meta.json`）
- `MechanicalWaterPump-<version>.zip`：GitHub release／手動安裝用
- `.sha256`：校驗碼

### Steam 工作坊（需你的 Steam 帳號）
1. 先 `release.ps1 -Install`（或手動把 `dist/MechanicalWaterPump-<version>/*` 複製到 `Documents\Timberborn\Mods\MechanicalWaterPump\`）
2. 啟動 Timberborn → Main Menu → **Mods** → 選擇此模組 → **Upload**
3. 首次上傳後，Steam 會給一個 **Workshop Item ID**；把它寫進 `mod/workshop/workshop_data.json` 的 `ItemId`（之後更新用），再執行 `release.ps1 -Install` 重新安裝一次
4. 更新版本時：改 `mod/data/manifest.json` 的 `Version` 與 `mod/CHANGELOG.md`，重新 `release.ps1`

### GitHub
```powershell
git init
git add -A
git commit -m "Mechanical Water Pump 0.2.0"
git remote add origin <你的 repo URL>
git push -u origin main
```
`.gitignore` 已排除 `bin/obj`、`mod/build/`、`dist/`、`art/` 的大型生成物（保留 3 張合圖）。
授權：`LICENSE`（MIT，作者名可自行調整）。

### 發佈前檢查（目前尚未全部完成）
- [ ] 遊戲內 T01–T27 驗收（尤其 T02/T03 產量與供電曲線、T10 Timber Pipes 閥門、T16–T22 維護、T09/T15 存檔）
- [ ] Mk III／Mk IV 內部零件在遊戲內可見性（診斷日誌顯示節點存在；需實機截圖確認）
- [ ] Timber Pipes 版本：本機偵測到 1.0.0，與文件基準 11.4.0 不同 → T10 需重測
- [ ] 自動 LOD 是否保留節點動畫（遠距離確認）
- [ ] 版本號／CHANGELOG／thumbnail 更新
