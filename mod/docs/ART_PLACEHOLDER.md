# 美術狀態與剩餘項（ART STATUS）

> **2026-11-04 art pass：四檔 × 兩陣營的自製模型與圖示已交付**，取代原本「唯讀引用原版模型」
> 的暫代方案。本檔保留歷史說明與**尚未關閉的美術項**。

## 1. 現況（已交付）

| 項目 | 內容 |
|---|---|
| 模型 | `data/Buildings/Water/MWP/MWP.Pump.<Tier>.<Faction>.Model.timbermesh` × 8 |
| 施工模型 | `…ConstructionStage0.Model.timbermesh` × 8（同 envelope 灰盒） |
| 圖示 | `data/Icons/MWP<Tier><Faction>.png` × 8（128×128、透明底、無文字） |
| 管線 | `art/`：`mwp_common.py`（凍結契約）、`gen_models.py`（純 Python 幾何 → `art/models/*.json`）、`export_timbermesh.py`（Blender headless 4 節點＋3 ports＋原版材質名＋插件匯出）、`validate_timbermesh.py`（純 Python protobuf 驗證，16/16 PASS、226 checks）、`render_previews.py`（預覽＋圖示渲染） |
| 素材契約 | 只用原版 `MaterialCollection` 材質名（13 個，已靜態核對存在）；UV 用 HDBF `material_patches` 的安全 patch 投影；無自製 PBR/AssetBundle |
| 尺寸 | export-space block-corner：Mk I/II `(2,3,3)`、Mk III `(3,4,3)`、Mk IV `(3,4,4)`；tris 2,112 / 2,812 / 3,952 / 3,704（預算 12k） |
| 證據 | `art/art-validation.txt`（C1–C12 全 PASS）、`art/export-manifest.json`、`art/determinism.txt`（gen/export 各跑兩次 byte-identical）、`art/previews/`（24 張＋2 合圖）、`art/icons/` |

## 2. 尚未關閉（誠實列表）

1. **遊戲內視覺驗收**：本輪自動載入測試因環境問題（遊戲啟動即退出，移除本模組後同樣發生）無法完成；
   模型/材質的靜態驗證全過，但「進遊戲看外觀、建造選單圖示、端口位置」仍需人工確認。
2. **LOD1/LOD2**：目前只輸出 LOD0（1.9k–4.3k tris，遠低於 12k 預算）；REQ-ART-02 的 LOD 階層
   尚未交付（低優先，原版同尺寸建築也有單 LOD 情形）。
3. **ART-07 三視圖參考圖簽核**：`art/previews/` 有等角/50% 預覽，但「建模前四檔 × front/side/top
   正交三視圖＋端口座標圖簽核」流程尚未走完（屬於流程性驗收，不是資產缺失）。
4. **文字/標誌與風格細節**：預覽確認無文字/商標、FT 暖木 / IT 冷金屬可辨；細部材質拉伸
   （flat patch 造成的木紋方向）可在後續 art pass 用世界尺度 UV 再優化（非阻塞）。

## 3. 重跑方式（節錄）

```powershell
# 生成 + 匯出（Blender 4.2.1 headless）
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\gen_models.py
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\export_timbermesh.py
# 驗證（純 Python，不需 Blender）
python art\validate_timbermesh.py
# 圖示與預覽（Blender headless；預覽＋圖示同一支腳本）
& '...\blender.exe' -b --factory-startup -noaudio --python art\render_previews.py
# 整包驗證（資料 + 組件）
powershell -NoProfile -ExecutionPolicy Bypass -File mod\build.ps1
```

## 4. 資產授權

全部模型幾何、UV 配置與圖示為本專案程序化生成；材質僅以「名稱」引用遊戲原版 MaterialCollection
（不打包、不散布原版貼圖/模型）。`art/reference/` 僅為內部風格參考，不隨模組散布。
