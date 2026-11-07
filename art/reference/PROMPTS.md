# 機械水泵 — AI 概念參考圖提示詞（Seedream 5.0 Lite 等）

> 用途：只用於**風格、材質、剪影、moodboard**。不得作為尺寸、端口座標、碰撞體或 LOD 的依據；
> 尺寸與端口參考圖仍必須用 Blender blockout 的 front／side／top 正交三視圖（見需求文檔 §13.1）。
> 生成前請確認工具授權條款；不要上傳原版 Timberborn 截圖做 image-to-image，除非條款允許。
> 不要生成 Timberborn 商標、原版模型或文字。

## 建議生成參數

| 項目 | 建議 |
|---|---|
| 模型 | Seedream 5.0 Lite（或同級圖像模型） |
| 比例 | 1:1（單體概念）或 3:2（三視圖／場景） |
| 張數 | 每檔 4–8 張，選 2–3 張入 moodboard |
| 背景 | 中性淺灰或白色，乾淨剪影 |
| 後處理 | 只做裁切／調色；不得用 AI 圖標尺寸或座標 |
| 存檔 | `art/reference/ai/<tier>-concept-<seed>.png` |

## 共用風格提示詞（4 檔都先加這段）

```text
Timberborn-style game asset concept art, chunky beaver engineering aesthetic, thick timber beams,
exposed wooden gears, leather belts, copper pipes, riveted rough metal, visible wear and patina,
muted warm wood palette with cool metal accents, readable silhouette, orthographic-like front view,
neutral light gray background, no text, no logo, no watermark, clean edges, stylized low-poly-friendly
forms, high detail, concept sheet, not photorealistic, no characters, no environment.
```

## 四檔追加提示詞

### Mk I — 基礎

```text
Small 2x3x3 mechanical drinking water pump converted from a hand pump: compact wooden frame,
single reciprocating cylinder, one large belt pulley, simple iron bands, short intake pipe at the
bottom side, power shaft socket on the left side, no roof, humble early-game look, 1:1.
```

### Mk II — 中級

```text
Medium 2x3x3 mechanical drinking water pump: plank shed with a small roof, double-cylinder pump,
enclosed gearbox, copper pipes with a pressure gauge, power shaft sockets on both sides and bottom,
deeper intake pipe, more refined but still frontier-built, 1:1.
```

### Mk III — 高級

```text
Large 3x4x3 industrial mechanical drinking water pump: metal frame, three-stage pump, glass
inspection window, treated plank panels, copper manifold, pressure gauges, deeper intake pipe,
pipe connection port on the output side, power input on the side and bottom, 1:1.
```

### Mk IV — 頂級

```text
Massive 3x4x4 mechanical drinking water pump station: heavy metal frame, twin parallel pump
barrels, large copper valves, heat fins, treated plank cladding, prominent power input coupling,
large output pipe port, industrial endgame look, complex but readable silhouette, 1:1.
```

## 三視圖概念提示詞（只作 concept，不取代 blockout）

```text
Orthographic turnaround concept sheet of a Timberborn-style mechanical drinking water pump:
front view, side view, top view, all at the same scale, aligned on a light gray background with a
subtle grid, clean silhouette, no perspective distortion, no text, no labels, no characters.
```

> 注意：AI 對「同一比例、同一細節」的三視圖通常不穩定；此圖只可用來討論造型，
> 不可直接當成建模尺寸圖。真正要量尺寸請用 Blender blockout。

## Negative prompt（若工具支援）

```text
text, logo, watermark, signature, photorealistic, modern factory, sci-fi, neon, people, beavers,
extra buildings, perspective distortion, inconsistent scale, floating parts, duplicate parts,
blurry, low contrast, busy background, environment, sky, trees
```

## 使用流程

1. 先讀需求文檔 §13.1／§13.2，確認四檔尺寸、端口位置、材質方向。
2. 用上方 prompt 每檔生成 4–8 張 concept，挑 2–3 張放入 moodboard。
3. 在 Blender 依 §4／§20.1.2 的尺寸做 blockout；把 concept 的造型特徵轉成幾何。
4. 從 blockout 輸出 front／side／top 正交三視圖，標註格數、尺寸、端口座標。
5. 用遊戲內原版水泵截圖核對木構比例與材質；不要把原版貼圖打包進模組。
6. 依 §13.5 驗收：concept 已標記、尺寸圖來自 blockout、端口與 Blueprint 一致。

## 檔案命名

```text
art/reference/
├── ai/
│   ├── mk1-concept-<seed>.png
│   ├── mk2-concept-<seed>.png
│   ├── mk3-concept-<seed>.png
│   └── mk4-concept-<seed>.png
├── ortho/
│   ├── mk1-front.png / mk1-side.png / mk1-top.png
│   ├── mk2-front.png / mk2-side.png / mk2-top.png
│   ├── mk3-front.png / mk3-side.png / mk3-top.png
│   └── mk4-front.png / mk4-side.png / mk4-top.png
└── PROMPTS.md   ← 本檔（含 prompt、seed、模型版本記錄）
```
