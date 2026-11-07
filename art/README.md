# MWP 機械水泵 — 自製美術管線（`.timbermesh` ×16 ＋ 圖示 ×8） v2（T9：世界尺度 UV＋節點動畫）

本目錄是 Mechanical Water Pump（MWP）模組**自製四檔水泵模型**的完整、可重跑管線，
取代 `mod/docs/ART_PLACEHOLDER.md` 記錄的「暫代原版模型」。所有輸出都是**離線產生**，
**未在遊戲中驗證**（本輪沒有部署、沒有啟動 Timberborn）。

**T10 修正（2026-11-07，交付 A 靜態診斷版 + 交付 B 動畫修復版）**：
* **根因：winding / 法線內外翻**（T9 regression）。T9 把 mesh 建構搬進 `mwp_scene.py`
  時掉了 T6 exporter 的「單次 `reverse_faces`」；官方插件會透過 det=-1 的反射映射再
  反轉 loop，所以缺了那一次翻轉 → 匯出的每個 mesh signed volume 為負、法線朝內。
  遊戲是單面（backface-culled）材質，因此看到封閉框架的內面、薄齒輪/活塞盤幾乎消失，
  看起來就像「壓扁揉皺」；Cycles 預覽是雙面所以正常。修法：`mwp_scene._fill_mesh()`
  恢復單次翻轉；validator C9 新增 **signed volume > 0** 絕對檢查（舊的 C9 只驗
  winding 與 stored normal 一致，內外翻時兩者一致仍會通過）。
* **交付 A**：8 檔 finished 先以**靜態診斷模式**輸出（保留獨立零件節點、移除 animation
  curves；env `MWP_DISABLE_ANIMATIONS=1`），讓使用者單獨確認幾何；Mk I/II 改為
  **全木材構造**（FT：BaseWood_Brown/LightBrown + IrregularPlanks_Brown；IT：
  BaseWood_DarkBrown/Grey + IrregularPlanks_MetalForged；不用 BaseMetal/Windows/
  Plaster/Details）。validator 對 static 模式同樣全 PASS。
* 證據：`art/reports/t10/winding-diagnosis.txt`（before/after signed volume、T6 vs T9
  差異）、`backface_broken_advanced.png` vs `backface_fixed_advanced.png`（直接由
  .timbermesh 重建 + 單面材質渲染，重現/修正 in-game 症狀）、`uv-alpha-check.txt`
  （所有 canvas 完全不透明，排除透明 texel）。
* **交付 B（已完成）**：動畫 curves 加回（winding 修正後重新匯出）；validator 新增
  **C15 逐幀 node transform 對照來源動畫公式**（旋轉四元數/位置/縮放，誤差 <2e-3）、
  **C16 動作軸 vs 零件幾何軸**（圓盤→最薄對稱軸、軸件→最長對稱軸、往復→兩長軸之一）、
  **C17 merge 檢查**（static root 三角數 == 來源 static、每個零件節點三角數 == 來源零件，
  確認 merge_meshes 沒有把動畫件併進 root）；動畫相位預覽恢復。A 版檔案存檔於
  `art/reports/t10/deliverable-A/` 供比對。

**T9 升級（2026-11-06）**：
* **A. 材質/貼圖**：UV 從「小 flat-patch」改為 **ART-07 世界尺度投影、native texel
  density**（沿用 HDBF `material_uv_policy.py` + `material_texture_map.json`，
  每個材質用其已驗證 canvas、依實際投影跨度與 bbox 中心做最大密度平面投影）。
* **B. 節點動畫**：每檔 = 1 個靜態 root mesh + N 個**獨立 `#Animated*` 節點**
  （飛輪/大齒輪旋轉、小齒輪/傳動軸旋轉、活塞往復相位錯開），120 frames @ 24 fps、
  linear keyframes、首尾同值 seamless；ConstructionStage0 維持靜態。
  遊戲端**不需改 C#/藍圖**：現有 `MechanicalNodeAnimatorSpec` 會自動以
  `PowerEfficiency × NonlinearAnimationManager.SpeedMultiplier` 驅動，0 動力停用。
* **C. 驗證/預覽**：validator 加 UV 密度與動畫通道檢查（274 checks）；previews 加
  每檔 3 個動畫相位；icons 重新渲染（preview 材質加原版 `_N` normal map＋`_AO`）。

## 1. 交付物

```
mod/data/Buildings/Water/MWP/
    MWP.Pump.<Basic|Intermediate|Advanced|Top>.<Folktails|IronTeeth>.Model.timbermesh                ×8  成品 LOD0（含節點動畫）
    MWP.Pump.<...>.<...>.ConstructionStage0.Model.timbermesh                                         ×8  施工灰盒（靜態）
mod/data/Icons/
    MWP<Basic|Intermediate|Advanced|Top><Folktails|IronTeeth>.png                                   ×8  128×128 RGBA
    （*.png.meta.json 未改動）
art/
    mwp_common.py            凍結契約：尺寸/材質/埠/UV policy/動畫常數
    mwp_scene.py             Blender 場景建構器（static mesh + 動畫子節點 + ports；export 與 render 共用）
    gen_models.py            程序化幾何＋動畫來源生成器（純 Python、deterministic）→ art/models/*.json
    export_timbermesh.py     Blender 4.2.1 headless 匯出器（single_animation=True）→ .timbermesh + manifest + mod/data 同步
    validate_timbermesh.py   純 Python 驗證器（幾何/材質/UV 密度/節點動畫）→ art/art-validation.txt
    render_previews.py       離線預覽（含 3 動畫相位）與圖示渲染（Cycles）→ art/previews、art/icons、mod/data/Icons
    material_patches.py / material_texture_map.json / atlas_layout.py
                             自 HDBF 專案完整沿用（唯讀複本）：原版材質 → 已驗證 canvas
    material_uv_policy.py    HDBF ART-07 世界尺度 UV policy（唯讀複本，native px/m 上限）
    tools/timbermesh_blender_plugin/   官方插件 v1.2.0（MIT，vendored）
    models/*.json            幾何＋動畫來源（deterministic；16 檔）
    models/gen-manifest.json 來源三角數/頂點數/bounds/材質/parts/UV 密度
    export-manifest.json     每檔 bytes/tris/verts/材質/bounds/nodes/animated nodes/UV 密度/sha256
    timbermesh/*.timbermesh  art 端輸出（與 mod/data 逐位元相同）
    previews/*.png           iso、iso50、3 個動畫相位、施工灰盒預覽＋合圖
    icons/*.png              512 來源與 128×128 圖示
    render-manifest.json     渲染參數與輸出清單
    determinism.txt          gen/export 各跑兩次 SHA-256 一致性＋動畫/UV 統計
    art-validation.txt       驗證報告（逐檔逐項 PASS/FAIL）
    README.md                本檔
```

## 2. 尺寸 / 座標（已定案，勿改）

export space = 遊戲 block-object local space：**block 角落原點**，
X=寬、Y=高（向上）、Z=深；**正面（出水/取貨）在 Z=0、進水側在 Z=Size.Z**。
模型必須 `min=(0,0,0)`、`max=(寬,高,深)`（bound-corner，無置中補償）。

| 檔位 | export envelope（X,Y,Z） | 藍圖 `BlockObjectSpec.Size`（引擎欄位順序 X=寬,Y=深,Z=高） |
|---|---|---|
| Mk I Basic | (2, 3, 3) | {X:2, Y:3, Z:3} |
| Mk II Intermediate | (2, 3, 3) | {X:2, Y:3, Z:3} |
| Mk III Advanced | **(3, 4, 3)** | {X:3, Y:3, Z:4}（＝寬3 深3 高4；CollidersSpec 3×4×3 與模型一致） |
| Mk IV Top | (3, 4, 4) | {X:3, Y:4, Z:4} |

三個 `#Port#` 空節點（**0 個 `#Slot#`**）：

| 空節點 | 位置（export） | 朝外 | 語意 |
|---|---|---|---|
| `#Port#Power` | (寬, 高/2, 深/2) | +X | 側面中段動力軸接口 |
| `#Port#Pipe` | (寬/2, min(0.65, 高×0.22), 深) | +Z | 背面底部進水管法蘭 |
| `#Port#Logistics` | (寬/2, 1.0, 0) | −Z | 正面出水 / 取貨口 |

## 3. 節點動畫契約（`mwp_scene.py` + `export_timbermesh.py`）

* 每檔節點 = **1 static root mesh + 3 `#Port#` + N 個獨立 `#Animated*` 節點**；
  動畫節點**不 merge** 進 static mesh（官方插件以 `#` 前綴物件作為獨立 node）。
* **node animations**（`Model.nodes[i].nodeAnimations`），**不是 vertex animation**：
  插件設定 `merge_meshes=True, single_animation=True, use_vertex_animations=False`。
* Blender 端：scene frame range **1..120 @ 24 fps**，線性 per-frame keyframes
  （旋轉 `angle(t)=2π·turns·t`；活塞 `offset(t)=amplitude·sin(2π·(cycles·t+phase))`），
  `turns` / `cycles` 為整數 → **first frame == last frame**（seamless loop，官方
  ShantySpeaker 範例同慣例）。
* 匯出後 post-process：`Model.Name`=檔名 stem、`Version`=0、**quaternion 逐幀符號連續化**
  （消除 `q→-q` 跳變，避免 animator slerp 反向）。

| 檔位 | 動畫節點（名稱保留，`#Animated` 前綴） | 動作 |
|---|---|---|
| Basic | Flywheel / HeadPulley / HeadShaft / DriveShaft / Piston | 飛輪 2 圈、頭部皮帶輪＋軸 8 圈、傳動軸 2 圈、活塞 4 往復 |
| Intermediate | Flywheel / HeadPulley / GearboxGear / GearboxPinion / DriveShaft / Piston | 飛輪 2 圈、齒輪 4 圈／反向 4 圈、皮帶輪 6 圈、傳動軸 2 圈、雙缸活塞 4 往復 |
| Advanced | Flywheel / HeadPulley / GearboxGear / DriveShaft / Piston1-3 | 飛輪 2 圈、皮帶輪 6 圈、齒輪反向 4 圈、傳動軸 2 圈、三段活塞 4 往復相位 0/⅓/⅔ |
| Top | Flywheel / HeadPulley / DriveShaft / Piston1-2 | 飛輪 2 圈、皮帶輪 8 圈、傳動軸 2 圈、雙管活塞 4 往復相位 0/½ |

> 速度：120 frames @ 24 fps = 5 秒一循環；飛輪 2 圈（0.4 rev/s）、小皮帶輪 6–8 圈、
> 傳動軸 2 圈、活塞 4 往復（每轉 2 行程）。全部為整數圈/週期 → 首尾同值。
| ConstructionStage0 | 無 | 靜態 |

**T10 狀態**：winding 修正後動畫已恢復（manifest `animations_enabled=true`），
8 檔 finished 皆含 120 frames @24fps nodeAnimations；validator C12–C17 全 PASS。
需要純幾何診斷時可再用 `MWP_DISABLE_ANIMATIONS=1` 匯出 static 版（validator 同樣支援）。
動畫節點名稱/動作表：

遊戲端驅動（IL 已證實，本輪未改任何 C#/藍圖）：藍圖 `MechanicalNodeAnimatorSpec`
→ `MechanicalNodeAnimator`，`speed = PowerEfficiency × NonlinearAnimationManager.SpeedMultiplier`，
0 動力時 `Enabled=false`。

## 4. 材質（只用原版 MaterialCollection 名稱，分陣營）

| 檔位 | Folktails | IronTeeth |
|---|---|---|
| Mk I（T10 全木） | BaseWood_Brown, BaseWood_LightBrown, IrregularPlanks_Brown | BaseWood_DarkBrown, BaseWood_Grey, IrregularPlanks_MetalForged |
| Mk II（T10 全木） | BaseWood_LightBrown, BaseWood_Brown, IrregularPlanks_Brown | BaseWood_Grey, BaseWood_DarkBrown, IrregularPlanks_MetalForged |
| Mk III | BaseWood_LightBrown, BaseMetal, WindowsAtlas, Details | BaseMetal, BaseWood_Grey, WindowsAtlas, IrregularPlanks_MetalForged |
| Mk IV | IrregularPlanks_Brown, BaseMetal, WindowsAtlas, Details | BaseMetal, IrregularPlanks_MetalForged, Details, WindowsAtlas |
| ConstructionStage0 | BaseWood_Brown, BaseMetal | BaseWood_DarkBrown, BaseMetal |

**刻意偏離：** `PaintedMetal.*` 完全不用（albedo 是黃/黑、藍/黑警示條紋）；
`IrregularPlanks_DarkBrown.IronTeeth` 不用（HDBF 已驗證 map 無安全 canvas；改用
`IrregularPlanks_MetalForged.IronTeeth`，原版 IT 材質、已驗證 canvas）。

## 5. UV / 貼圖策略（ART-07 世界尺度，native texel density）

每個面依 dominant face normal 做**世界尺度平面投影**，投影到該材質在
`material_texture_map.json` 的**已驗證 canvas**（tile=整張 1024² `_D`；patch=策展安全
patch rect），density 上限 = `material_uv_policy.px_per_m`（ART-07 native）：

```
uv = canvas_center + (world - material_projected_bbox_center) * px_per_m / 1024
px_per_m = min(ART-07 native, usable_canvas_px / material projected span)
```

* 窗口以**該材質自己的投影 bbox 中心**對齊，小零件也能用 native 密度（不再被整棟跨度拖低）。
* UV 一律留在該材質 canvas 內（2 px guard）——runtime AutoAtlaser 會把 UV 重映射進
  材質 cell，UV>1 會取樣到別的材質。
* 沒有自創 UV rect、沒有自訂 PBR/AssetBundle/貼圖檔。
* 實測密度（validator 從匯出 UV 反算，px/m）：木 117–145、金屬 121–163、
  planks tile 255–340、plaster 340、glass 82（全部 ≥80）。

## 6. 重跑命令（全部 deterministic）

```powershell
cd "C:\Users\user\Downloads\Mechanical Water Pump"

# 0) 產生 16 個幾何＋動畫來源 JSON（純 Python）
python art\gen_models.py

# 1) 匯出 16 個 .timbermesh（含 nodeAnimations）＋ export-manifest.json，並同步 mod/data（exit 0 = PASS）
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\export_timbermesh.py

# 2) 純 Python 驗證（不需要 Blender；寫 art\art-validation.txt，exit 0 = PASS）
python art\validate_timbermesh.py

# 3) 離線預覽（iso/iso50/3 動畫相位/施工）＋圖示（Cycles CPU；寫 art\previews、art\icons、mod\data\Icons）
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\render_previews.py -- --mode all --samples 48

# （可選）1×1×1 動畫 smoke test（static box + 1 rotating node，驗證 nodeAnimations）
python art\gen_models.py --smoke
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\export_timbermesh.py -- --smoke
```

環境：Windows、Blender 4.2.1 LTS（`--factory-startup`）、CPython 3.14.7；不需要 Unity/AssetBundle。

## 7. 驗證摘要（`art/art-validation.txt`）

逐檔 C1–C17：解析、身分、節點結構（root＋3 ports＋N 獨立零件節點、0 `#Slot#`）、
envelope（含動畫節點 rest transform）、三角數（≤12k、與來源一致）、材質白名單/陣營、
頂點屬性、拓樸/uv 範圍、法線（單位/平面/與 winding 同向）＋**每節點 signed volume > 0**（內外翻會被抓到）、UV 落在 canvas 內、
**UV 密度**（≥80 px/m、與 plan 誤差 ≤5%）、**節點動畫**（名稱/24fps/120 frames/
首尾等價/rest==first frame/quaternion 連續/通道變動；construction 0 動畫）。
C15 逐幀對照來源動畫公式、C16 動作軸 vs 幾何軸、C17 merge 檢查。
C13 `mod/data` 與 `art/timbermesh` 16/16 SHA-256 相同；C14 manifest 覆蓋 16 檔、
sha256/動畫欄位相符、0 failures。

本輪實跑：**16/16 FILES PASS、322 checks PASS、OVERALL VERDICT: PASS（exit 0）**。

## 8. 確定性（本輪實測）

* `art/gen_models.py` 連跑兩次 → 16/16 來源 JSON **SHA-256 相同**。
* `art/export_timbermesh.py` 連跑兩次 → 16/16 `.timbermesh` **SHA-256 相同**（含動畫）。
* 詳見 `art/determinism.txt`。

## 9. 已知限制 / 降級（誠實清單）

1. **未實機驗證**：只有離線格式/幾何/動畫驗證與 Cycles 離線渲染；幾何內外翻的修正以
   重新匯入 + 單面材質渲染離線重現/驗證，**仍需實機確認**（本輪未部署、未啟動遊戲）。
   交付 B 已恢復動畫（winding 修正後重新匯出、validator C15/C16/C17 通過），仍待實機確認
   動畫速度、0 動力停用與自動 LOD 行為。
2. **只交付 LOD0**：LOD1/LOD2 未輸出（依 HDBF 慣例由遊戲 `TimbermeshPrefabOptimizer`
   自動產生）；自動 LOD 是否保留 node 動畫**未實機確認**。
3. **沒有自訂 PBR / AssetBundle / 貼圖**：UV 取樣原版材質貼圖的已驗證區域；預覽另外
   使用原版 `_N`/`_AO` 只是為了離線可看性（遊戲內由原版材質 shader 提供）。
4. 頂點屬性只寫 position/normal/tangent/uv0（無 color）。
5. 3 個 `#Port#` 空節點 rotation 為 identity。
6. 動畫節點以 `#Animated*` 命名（官方範例 `#SpeakersAnimated` 同慣例）；`TimbermeshPrefabOptimizer`
   如何把 nodeAnimations 轉成 `TimbermeshAnimations.IAnimator` 是遊戲端行為，**未實機確認**。
7. 活塞連桿/皮帶在動畫中僅由節點位移表現（皮帶本身靜態；工程上可接受）。
8. 施工階段 8 檔幾何依 tier 相同、材質分陣營（灰盒，靜態）。
