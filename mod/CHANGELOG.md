# Changelog

## 0.2.4 — 2026-11-13

### Changed
- **實體面板全面原版化（UI）**：不再有「一大塊自繪文字面板」的觀感。面板改為全部由遊戲原版控制項
  與原版 USS class 組成：原版綠色九宮格子面板（硬警報改用原版 `StatusListFragment` 的紅條紋框）、
  原版 `status-list-element` 狀態列（34 px 遊戲狀態圖示＋原版 `status-list-element__text`）、
  原版藍色 `entity-fragment__explanation` 說明盒（下一步提示；標籤用原版
  `entity-panel__text text--centered` 組合）、原版 `entity-panel__row`＋
  `inventory-row__image` 24 px 圖示資訊列、原版 `ProgressBar`（機況／維護進度）、原版 `game-toggle`
  開關，以及從原版 UXML 範本取出的真實九宮格按鈕（暫停紅／恢復綠、「立即維護」綠）。
  模組不新增 UXML/USS/貼圖；列圖示全部用 `Resources.Load<Sprite>("UI/Images/...")` 讀遊戲原版
  sprite，缺圖只少一個圖示、不拋例外。原版 fragment 已提供的動力／內部水庫存／抽水深度維持由
  遊戲顯示，面板不重複。
- **建造選單改為專屬可收縮群組（GUI）**：四檔不再塞在原版 `Water` 工具組，改由 spec-only 藍圖
  `data/BlockObjectToolGroups/BlockObjectToolGroup.MWP.blueprint.json`
  （`BlockObjectToolGroupSpec`：Id `MWP`、Order 46、NameLocKey `ToolGroups.MWP`、
  Icon `Icons/MWPBasicFolktails`）定義自己的群組。底欄顯示一個 MWP 圖示，點擊展開 Mk I–IV、
  點其他群組即收合——與原版 Water／Food 等群組相同的收縮行為。8 份藍圖的
  `ToolGroupId` 改 `MWP`、`ToolOrder` 改 10/20/30/40；三語新增 `ToolGroups.MWP`
  （Mechanical Water Pumps／机械水泵／機械水泵）。

### Tooling / docs
- `tools/validate` 新增 `ValidateToolGroup`：比對群組 JSON 與 Core 常數
  （`MwpDefaults.ToolGroupId/ToolGroupOrder/ToolGroupIcon`、`MwpLocKeys.ToolGroupName`）、
  圖示檔存在，並逐檔比對 8 份藍圖的新 ToolGroupId/ToolOrder；`gen_blueprints.py` 同步產生群組檔。
- 新增 UI 取證：`docs/api/ui/toolgroup-facts.md`（群組載入／圖示／排序／風險）、
  `docs/api/ui/panel-vanilla-facts.md`（原版面板 class／sprite／佈局對照）；
  `src/MWP.UI/README.md`、`docs/CONTRACT.md` §4.1/§8/§10、`docs/REQ_TRACEABILITY.md` 同步更新。

### Notes
- 版本 0.2.4；存檔 schema 2、config schema 2 不變（0.2.3 存檔可直接載入）。群組歸屬由藍圖在
  載入時決定、不寫存檔，舊存檔的既有泵會直接出現在新群組。
- 離線驗證（2026-11-14 最終）：`build.ps1 -Clean` exit 0、4 組件 0 warning、單元測試
  `PASS 213/213`、`VALIDATION PASSED`（43 鍵、11 藍圖含工具群組）、staged 套件 52 檔／
  1,279,558 bytes（1,250 KB）。證據：`docs/evidence/build-0.2.4.log`。
- **實機驗證（2026-11-14）**：以回報存檔副本自動載入世界（`-skipModManager -saveName 1
  -settlementName MWPDiag`）通過——`Load time 36,512 ms`、`MWP 0.2.4 ready`、`SaveMigrate schema 2`，
  全 log **0 Exception／0 `MWP/Error`**，無 `unknown BlockObjectToolGroupSpec`、無
  `Failed to load asset at Icons/MWPBasicFolktails`、無 `No localization for ToolGroups.MWP`；
  底欄截圖可見到**一個收合狀態的 MWP 群組圖示（泵渲染圖）位於原版 Water（水滴）與 Food（胡蘿蔔）
  之間**（`Order 46`），即原版可收縮群組的預設呈現。證據：
  `docs/evidence/ui-in-game-2026-11-14-Player.log`、`ui-in-game-2026-11-14-checks.txt`、
  `ui-0.2.4-bottombar.png`、`ui-0.2.4-mwp-group-zoom.png`。
- **實機待辦（誠實）**：本機無法合成滑鼠／鍵盤輸入（`OpenInputDesktop` 拒絕、probe
  `form_clicks=0`；與 HDBF 專案 §16.2 同一環境事實），所以「點擊群組展開」與「點選水泵看面板外觀」
  這兩步只能人工驗收；面板的原版 class／sprite 構成已離線逐項對照原版 UXML/USS
  （`docs/api/ui/panel-vanilla-facts.md`）。

## 0.2.3 — 2026-11-09

### Fixed
- **BUG-08（2026-11-09，使用者回報：水泵放在水邊仍顯示「沒有可抽取的水」、庫存一直是 0）**：
  舊的 `Withdraw()` 只要緩衝裡有任何一點水就取走（`take = min(available, needed)`），之後
  `GrantedItems()` 以整數 Water 單位（0.2 m³）換算，不足一個單位的零頭會被**丟棄**。低流量／淺水
  進水口（例如湖邊淺灘）水體每步只補進約 0.05–0.1 m³，緩衝永遠累積不到 0.2 m³ → 每次抽水都失敗，
  回報存檔中累積 1600+ 次連續失敗、狀態卡在 `NoWater`，即使 `WaterInput.IsUnderwater = true`
  （進水口確實浸在水中）。
  **修法**：只有當緩衝已累積到完整請求量（`items × 0.2 m³`，含 `1e-4` 容差）時才呼叫
  `RemoveCleanWater` 並扣除；不足時保留緩衝讓它繼續累積，等水體補足後下一次成功。
  - 遊戲內實證（回報存檔副本 + 臨時探針自動回合）：修前 `uw=True`、水柱 `floor=2 / ceiling=3`、
    水緩衝 0 且連續失敗 1000+；修後泵在 3–4 個步驟內自動恢復 `Producing`、失敗計數歸零、
    內部庫存開始上升。證據：`docs/evidence/water-withdraw-in-game-2026-11-09.log`。
- **NoWater 提示語**：`UI.MWP.HintNoWater` 由「進水口沒有水」改為「進水口沒有水或水量不足：
  請確認進水管浸在水中。」——修後此狀態也可能是進水正常但水流太慢、正在補水（暫態）。
- `mod/docs/RELEASE_CHECKLIST.md` 的 T04 驗收項補上「低流量湖邊淺水」案例。

### Notes
- 版本 0.2.3；存檔 schema 2、config schema 2 不變（0.2.2 存檔可直接載入）。
- 深水／高流量進水口行為不變（緩衝本來就 ≥ 1 個水單位，立即成功）。
- 離線驗證：`build.ps1` exit 0、4 組件 0 warning、單元測試 `PASS 213/213`、`VALIDATION PASSED`。

## 0.2.2 — 2026-11-08

### Fixed
- **BUG-06 (2026-11-08, user request: remove the "requires haulers to function" status added in
  0.2.1)**: the pump no longer carries the vanilla `NoHaulingPostStatus` or its initializer
  decorator `MwpHaulStatus`; both decorators and the `Bind<MwpHaulStatus>()` registration are gone,
  and `MwpHaulStatus.cs` is deleted. The hauler chain itself is intentionally unchanged
  (`HaulCandidate` + `SimpleOutputInventoryHaulBehaviorProvider` + `EmptyOutputWorkplaceBehavior`):
  with a **staffed vanilla Hauling Post** beavers still carry the pump's water to the tanks, while
  without haulers the pump simply fills its internal tank and stops producing, exactly like a
  vanilla output building. The misleading "cannot work" warning is gone; the pump was never meant
  to require haulers, it only needs a haul destination.
- **BUG-07 (2026-11-08, user request: the pump could not reach the river bed / retreated water)**:
  `MaxDepth` is now **64 (effectively unlimited)** for all four tiers, up from 2/4/6/8, in both
  `WaterInputPipeSpec.MaxDepth` and `MWP.MechanicalWaterPumpSpec.MaxDepth`, in `MWP_Config.json`
  and in `TierCatalog`. The intake pipe reaches the river bed, so the pump keeps working at any
  water level, including low water and drought retreat. The NoWater hint no longer uses the
  vanilla `Status.Buildings.NeedsWater` sentence ("this building needs deeper water to function");
  it uses the new MWP key `UI.MWP.HintNoWater` ("the intake is not in water"). The vanilla depth
  fragment still shows `Limit 64` (effectively unlimited).

### Notes
- Hauling still requires a **staffed vanilla Hauling Post** in the district, and paused pumps are
  still not emptied (vanilla rules). See `docs/VERIFICATION.md` A20 for the removal evidence.
- The 0.2.1 "No hauling post in range" feedback is intentionally withdrawn at the user's request;
  a district without haulers now sees the pump fill up and stop with no warning — same as vanilla.
- Version: `manifest.json` 0.2.1 → 0.2.2; save schema 2 and config schema 2 are unchanged.
- Offline verification (2026-11-08): `build.ps1` exit 0, four assemblies 0 warning, unit tests
  `PASS 213/213`, data/assembly checks `VALIDATION PASSED`. In-game acceptance (T04 intake depth,
  T06 full-tank, T01–T27) is **still pending**.

## 0.2.1 — 2026-11-08

### Fixed
- **BUG-04 in-game confirmation (2026-11-08)**: the hauler-chain decorator trio from 0.2.0
  (`HaulCandidate` + `SimpleOutputInventoryHaulBehaviorProvider` + `EmptyOutputWorkplaceBehavior`)
  was verified in a live game. A copy of the reporter settlement got one finished vanilla
  HaulingPost and two employed haulers; within about one in-game hour the haulers moved Water out
  of every unpaused pump into the water tanks (Basic 15→0, Intermediate 30→0, Advanced 75→61, the
  emptied Small Tanks grew to 30/30). The paused Mk III pump stayed at 100, matching vanilla
  hauling rules (paused buildings are blocked and not emptied). Evidence:
  `docs/evidence/haul-in-game-2026-11-08.log`.
- **BUG-05 (2026-11-08, in-game: pump silently keeps its water when the district has no hauling
  infrastructure)**: the pump carried the whole hauler chain but not the vanilla feedback status,
  so a settlement without a Hauling Post saw its pumps fill up and stop with no explanation.
  Added the vanilla pattern: `NoHaulingPostStatus` plus `MwpHaulStatus` (the initializer half that
  calls `NoHaulingPostStatus.Initialize(...)`, mirroring `FireworkLauncherStatus` /
  `GoodConsumingBuildingStatusInitializer`). The pump now shows the game's own "No hauling post in
  range" status whenever its district cannot haul. Verified in-game on the unmodified settlement
  (no Hauling Post): zero exceptions, `candidate=True enabled=True providers=1 emptyOutput=True
  noHaulStatus=True inRange=False`. Evidence:
  `docs/evidence/haul-status-in-game-2026-11-08.log`.
- `MwpHaulStatus` is a decorator component and therefore had to be bound in the Game context
  (`Bind<MwpHaulStatus>().AsTransient()`); an unbound decorator aborts the world load with
  `BinditoException: No binding exists for type ...` (caught by the in-game round).

### Notes
- Hauling is vanilla hauling: a **staffed Hauling Post** must exist in the district, and a paused
  building is intentionally not emptied (same as vanilla water pumps/gatherer flags).

## 0.2.0 — 首次公開發佈（2026-11-08）
- Steam Workshop item **3835434644**（Timberborn 1.1）已上傳：https://steamcommunity.com/sharedfiles/filedetails/?id=3835434644
- GitHub: https://github.com/stanley-1028/MechanicalWaterPump
- 與 **DeepSeek V4.1 Flash** 共同完成。

> Made in collaboration with **DeepSeek V4.1 Flash**. — Mechanical Water Pump (MWP)

All notable changes are recorded here. Version numbers follow `manifest.json`;
save/config/spec schema versions are separate (see `docs/CONTRACT.md` §13).

## 0.2.0 — 2026-11-04

Mod version `0.2.0`, save schema **2**, config schema **2**.

### Added
- Maintenance system for all four tiers: `condition` 0–100, wear only while producing,
  intervals 8/10/12/15 days, materials 10 Log / 15 Plank / 10 MetalBlock+10 Plank / 15 MetalBlock,
  2 game-hour downtime, automatic maintenance (on by default, can be disabled per pump), manual
  "maintain now", missing-material and no-path states, efficiency multipliers 1.00/0.90/0.75/0.50/0.
- Save schema v2 with `condition`, `autoMaintenance`, `maintenanceState`,
  `maintenanceRemainingHours`; idempotent v1→v2 migration and per-pump corrupt reset.
- Entity panel (`MWP.UI`): highest-priority state with next-step hint, actual Water/h, power %,
  water quality, internal stock, intake depth, pipe state, condition bar, next-maintenance
  countdown, required materials, auto-maintenance toggle and "maintain now" button (≤4 Hz).
- Runtime diagnostics equivalent of `/mwp.diag` (`features.uiDiagnostics=true` logs version,
  pump count, adapter state and average tick time).
- Blueprint data for 4 tiers × 2 factions (8 blueprints), 2 template collections, 3 localizations
  (enUS/zhCN/zhTW), `MWP_Config.json` schema 2, 8 generated placeholder icons.
- Offline tooling: `tools/ApiIndex` (game metadata/IL dumper) and `tools/validate` (validates data
  and shipped-assembly contracts against the real 1.1.2.8 game metadata).
- Documentation: `docs/CONTRACT.md`, `docs/API_FACTS_MWP.md`, `docs/ARCHITECTURE.md`,
  `docs/REQ_TRACEABILITY.md`, `docs/VERIFICATION.md`, `docs/ART_PLACEHOLDER.md`.

### Changed (native UI, 2026-11-07)
- The pump panel is now built from the game's own UI resources instead of code-only inline styles:
  nine-slice `entity-sub-panel` sections, `entity-panel__text` typography, native `ProgressBar`
  controls, native nine-slice buttons taken from a vanilla fragment template
  (`entity-fragment__button`), and the native toggle classes.
- Rows that vanilla fragments already render are no longer duplicated: mechanical power
  (`MechanicalNodeFragment`), water stock (`SimpleOutputInventoryFragment`, enabled through the
  `SimpleOutputInventoryFragmentEnabler` decorator) and intake depth
  (`WaterInputPipeDepthFragment`). Fallback rows remain only when the component is absent.
- Fixed the `TextLocalizationWrapper parameter types have changed` log warning: the shared
  `Unit.HorsePower.NumberAndUnit` key was called with a string while the game formatter calls it
  with a number; the old power tooltip was removed.

### Added (visual upgrade, 2026-11-06)
- **Node animations** on all eight finished pump models: flywheel, head pulley, drive shaft and
  piston child nodes are animated (120 frames @24 fps, seamless loop, 46 animated channels total,
  5-7 per model). They are driven by the game's own `MechanicalNodeAnimator`
  (`AddDecorator<MechanicalNodeAnimatorSpec, MechanicalNodeAnimator>`, IL verified): speed scales
  with power efficiency and the animation stops at 0 hp. Construction-stage models stay static.
  This intentionally overrides requirements §13.3 ("no animation") per the user's request (DV-07).
- **World-scale tiled UVs** (HDBF ART-07 policy) replace the old flat-patch UVs: wood 117-145 px/m,
  metal 121-163 px/m, planks 255-340 px/m, glass 82 px/m, plaster 340 px/m (all above the 80 px/m
  target, no more stretched 3 m faces). UVs stay inside each material's verified canvas so the
  runtime auto-atlas cannot sample a neighbouring material.
- Art validator extended to 274 checks (C1-C14): animation tracks, FPS/frame count, seamless loop,
  rest pose == frame 0, UV canvas containment and px/m density. `16/16 PASS`, determinism re-run.

### Changed
- Rated output raised to **3 / 6 / 15 / 20 Water per hour** (was the 1/3/6/9 baseline) and power
  demand to **100 / 200 / 500 / 700 hp** so no tier is weaker than its vanilla comparison.
- Internal Water capacity raised to 15 / 30 / 75 / 100 (about 5 hours of output).
- Contamination curve defaults to the vanilla-equivalent `q = 1 − c`
  (`badwaterPenalty = 1.0`, `minWaterQuality = 0.0`) to satisfy "never better than vanilla under
  pollution"; the requirements' 0.75/0.25 pair remains configurable (DV-01).
- Engine `BlockObjectSpec.Size` axis convention resolved from game IL: X = width, Y = depth,
  Z = height; shipped sizes are {2,3,3}, {2,3,3}, {3,3,4}, {3,4,4} (DV-03).
- Entity ids in the UI contracts use `System.Guid` (`EntityComponent.EntityId`), not int (DV-05).
- `manifest.json` keeps `OptionalMods: []`; Timber Pipes is detected at runtime instead of being
  declared as a manifest dependency (DV-02).

### Changed
- **DV-06 (2026-11-05, placement feel)**: Mk III / Mk IV no longer copy the vanilla
  MechanicalPump/LargeWaterPump support layout (whose `GroundOrStackable` strip sits in the middle
  of the footprint and needs a large flat bank). Their `Blocks` are now generated with the only
  solid support cells on the **front row** (3 cells), so a 3×1 flat strip is enough and the rest may
  overhang water - the same placement feel as Mk I/II. Envelopes, entrance/water side, colliders and
  every balance spec are unchanged; `gen_blueprints.py` reproduces the layout.

### Fixed
- **BUG-04 (2026-11-07, in-game: beavers never hauled the pump water)**: a building with a
  `SimpleOutputInventory` only becomes a haul destination when it carries the decorator trio
  vanilla output buildings use (`HaulCandidate` + `EmptyOutputWorkplaceBehavior` +
  `SimpleOutputInventoryHaulBehaviorProvider`); the mod had none, so hauler AI could not even see
  the pump inventory. Added the trio to `MwpGameConfigurator.ProvideTemplateModule()`, ordered per
  IL (`HaulCandidate.Awake` collects `GetComponents<IHaulBehaviorProvider>()`, so the provider must
  be decorated first). One-time `MWP/Info/ModelDiag` now also logs
  `hauling: HaulCandidate=... providers=...` for verification. **In-game confirmation pending.**

- **BUG-03 (2026-11-07, in-game)**: models looked inside-out/crumpled in game (hollow frames,
  fragmented gears) although Blender previews were fine. Root cause: the T9 refactor moved mesh
  construction into `mwp_scene._fill_mesh()` and dropped the single `bmesh.ops.reverse_faces` call
  that compensates the plugin's mirrored export; every mesh had a negative signed volume and
  inward normals, which single-sided in-game materials render as "inside". Fix: restore the flip,
  rendered a backface-culling reproduction (`art/reports/t10/backface_broken_advanced.png` vs
  `backface_fixed_advanced.png`), and the art validator now enforces **signed volume > 0** per mesh
  (the old normals-vs-winding check passed even when everything was inverted).
- A static diagnostic build (animation curves temporarily removed) was shipped first; the final
  build restores the animations after the winding fix, with the new validator checks C15 (per-frame
  transform vs source), C16 (rotation axis vs part geometry), C17 (nodes not merged) and signed
  volume C9. Final numbers: 16 files / 653,318 B / 30,320 tris / 46 animated nodes.
- **Mk I/II are now all-wood constructions** (user request): FT uses BaseWood_Brown/LightBrown +
  IrregularPlanks_Brown, IT uses BaseWood_DarkBrown/Grey + IrregularPlanks_MetalForged; no metal,
  glass or plaster on these two tiers. Mk III/IV keep the wood/metal look.

- **BUG-02 (2026-11-05, in-game screenshots)**: the panel showed the internal stock as
  `15/2147483647` (Mk II `30/2147483647`) because `SimpleOutputInventory` is created with unlimited
  capacity and the snapshot used `Inventory.Capacity`. The panel now shows the tier design capacity
  (`TierCatalog.InternalCapacity`); production was already correctly capped by the tier capacity.
- **BUG-01 (2026-11-05, in-game)**: a beaver's drink-need evaluation threw
  `NullReferenceException` in `InventoryNeedBehavior.ActionPosition` and aborted the simulation
  whenever a pump existed. Root cause: vanilla `SimpleOutputInventoryInitializer` unconditionally
  adds `InventoryNeedBehavior`, which dereferences `Enterable.CanReserveSlot`, and the MWP
  blueprints had no `EnterableSpec`. Fix: every blueprint now ships a zero-capacity `EnterableSpec`
  (safe `null` action position, no beaver can enter the pump). Validator + build stay green.
- Thermal/power consistency: pausing or maintaining a pump disables its power draw through
  `MechanicalBuilding.SetConsumptionDisabled` without touching the shared mechanical graph.
- Water-body accounting cannot create water: production is granted only for the volume actually
  withdrawn through the vanilla `WaterInput` chain (0.2 m³ per Water item, verified in IL).

### 2026-11-04 art pass (same 0.2.0)
- Four tiers × two factions now ship **self-made `.timbermesh` models** (8 finished + 8 construction
  stage, `data/Buildings/Water/MWP/`), replacing the placeholder vanilla-model references.
  Envelopes (block-corner export space): Mk I/II `(2,3,3)`, Mk III `(3,4,3)`, Mk IV `(3,4,4)`;
  LOD0 triangles 2,112 / 2,812 / 3,952 / 3,704 per tier (budget 12k, 30,328 total for all 16 files).
- Custom 128×128 icons replace the generated placeholders.
- Art pipeline lives in `art/` (Blender 4.2.1 headless generator → official Timbermesh plugin v1.2.0
  exporter → pure-Python protobuf validator; 16/16 files PASS) with `art/export-manifest.json`
  and 24 preview renders.
- Materials are vanilla `MaterialCollection` names only (13 checked present per faction); no custom
  PBR/AssetBundle and no vanilla assets are packaged.
- Blueprints' `TimbermeshSpec.Model` / `ConstructionStage0.Model` now point at the mod-local models
  and the colliders were re-authored as footprint boxes.

### Known limitations
- Art is placeholder: blueprints reference same-faction vanilla models read-only (see
  `docs/ART_PLACEHOLDER.md`); REQ-ART-01/02/03/05/06/07 remain open.
- In-game acceptance T01–T27 has not been executed on a live game; see `docs/VERIFICATION.md`.
- The optional direct pipe output port (REQ-COMPAT-03, SHOULD) is detection-only in v0.2; the
  valve extraction path (MUST) relies on the standard Water inventory.

## 0.1.0 — 2026-11-03

- Requirements/design baseline only (v0.1 of `機械水泵模組需求文檔.md`): four tiers at
  1/3/6/9 Water per hour, 100/200/350/500 hp, no maintenance system.
