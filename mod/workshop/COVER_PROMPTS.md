# Steam 工作坊封面 — AI 生成提示詞（Mechanical Water Pump）

> 交付物：**只給提示詞與壓字規格**，不產出圖片。
> 用途：替換 `mod/workshop/thumbnail.png`（目前是四檔 iso 合圖＋壓字版，能用但偏「模型展示」）。
> 目標尺寸：**1920×1080 或 2560×1440（16:9）**；上傳前縮成 **1280×720 PNG（< 1 MB）**。
> 遊戲內上傳器直接讀 `thumbnail.png` 當預覽；之後換封面只要覆蓋此檔＋重跑 `mod/release.ps1` 或 `SteamUpload.exe`。

---

## 0. 30 秒版

1. 用 **Variant A**（主封面）跑 2–4 張挑一張。
2. 選中的圖**保持無字**（AI 大字常拼錯），再用 Canva/Photoshop/GIMP 壓字（§6）。
3. 先縮到 **322×181** 看「木頭機房＋河流＋四檔泵」的輪廓還讀不讀得出來。
4. 檢查**中心 1:1 方裁**（Timberborn 工坊列表是方裁縮圖，標題不要放太邊緣）。
5. Variant B（剖面）不要當封面，放 Steam 頁面第二張圖，用來說明「裡面真的有齒輪/活塞」。

---

## 1. 工具與參數

| 工具 | 参数 |
|---|---|
| Seedream 5.0 Lite（豆包/即夢/火山方舟） | Landscape 16:9、2K、一次 2–4 張；**無負面詞欄位**，不要寫 masterpiece/8K/ultra-detailed |
| Midjourney v6/v7 | `--ar 16:9 --style raw --v 6.1`；用 `--no text, watermark, logo, UI` |
| DALL·E 3 / GPT-image | 直接貼 Variant A 全文；指定 `16:9, no text` |
| Stable Diffusion / SDXL | 16:9 1536×864 → 放大；CFG 5–7；負面詞用 §5；可用 img2img 參考我們的渲染（§8） |
| 圖生圖（推薦） | 以 `art/previews/t11_mk3_mk4_angles_grid.png` 或 `art/previews/tiers_factions_iso_grid.png` 當 composition reference，denoise 0.45–0.6，保留模型外形 |

**共通規則**：不要讓 AI 生成文字/Logo/UI；不要用「寫實照片」；要「手繪貼圖、低多邊形、等角視角、Timberborn 故事書感」。

---

## 2. Variant A — 主封面（推薦）

**目標**：一眼看出這不是風車，而是「木造/鐵造機房裡有齒輪的水泵」，且沿河岸有四種尺寸。
**參數**：16:9、2K。

```
Isometric game-art diorama of four mechanical water pumps on the bank of a teal river in Timberborn style. The smallest pump is a dark wooden frame with a round flywheel and one pipe running into the water; the second is a wooden machine shed with open sides showing a big gear; the third is a taller timber-and-metal machine room with glass windows, visible gears, pulleys and three pistons inside; the largest is a big metal-cage pump with two water pipes and a huge gear wheel, standing on a plank platform over the river. Tiny beavers carry water buckets between the pumps and a wooden water tank on the grass. Pine trees, stone terraces and a wooden district centre in the background, warm late-afternoon sunlight from the left, chunky low-poly shapes with hand-painted wood and scuffed metal textures, cheerful storybook mood, no text, no logos, no UI.
```

**必要檢查**
- [ ] 是**水泵/機房**，不是風車、水車、磨坊或鍋爐；
- [ ] 四檔尺寸有**明顯大小差**（小木架 → 大鐵籠）；
- [ ] 至少一顆**大齒輪/飛輪**看得見，且不是齒輪形狀的窗花；
- [ ] 有河流/水管/水塔其中兩項（一眼 Timberborn）；
- [ ] 沒有文字、Logo、UI、浮水印；
- [ ] 縮到 322×181 仍能讀出「木/鐵機房 + 河」。

**常見失敗與補丁**

| 失敗 | 補一句 |
|---|---|
| 變成風車 | `These are pump houses with pipes into the river, not windmills; no windmill blades.` |
| 齒輪像裝飾 | `Large functional gears and a flywheel are clearly visible inside the open frame.` |
| 四檔大小差不明顯 | `The four machines are clearly different sizes, from a small wooden frame to a huge metal cage.` |
| 太寫實 | `Hand-painted game textures, stylised illustration, soft brush strokes, not a photograph.` |

---

## 3. Variant B — 剖面／內裝（第二張圖用）

**目標**：解釋「裡面真的有在動」，展示 Mk III 內裝（齒輪、曲軸、活塞、銅管、壓力錶）。
**參數**：16:9、2K。

```
Cutaway view of a Timberborn mechanical water pump interior, timber-and-metal frame opened on one side, showing a big brass gear wheel, three steel pistons with connecting rods, a crankshaft, copper pipes, two pressure gauges and a small water tank, dark wooden back panels contrasting with bright scuffed metal, glass window frames on the front, warm workshop light, isometric three-quarter view, chunky low-poly hand-painted game art, no text, no logos.
```

---

## 4. Variant C — 四檔並排展示（可選）

```
Four mechanical water pumps displayed in a row on a wooden platform in a Timberborn meadow, ordered from a small open wooden frame pump with a flywheel, to a wooden shed pump with exposed gears, to a tall timber-and-metal machine room with glass windows and pistons, to a huge metal-cage pump with two thick pipes and a large gear wheel. Clean isometric game-art lineup, soft daylight, pine trees and a river behind, hand-painted stylised textures, no text, no logos.
```

---

## 5. 負面提示詞（SD/SDXL/Midjourney 用）

```
text, letters, words, watermark, signature, logo, UI, HUD, interface, windmill, wind turbine, water wheel, sawmill, photograph, photorealistic, hyperrealistic, 3D render noise, blurry, low contrast, dark night scene, extra gears floating, deformed machinery, duplicated pistons, cropped subject, frame border
```

---

## 6. 壓字規格（AI 圖保持無字）

**文字（英文，Steam 主要語言）**
- 主標題：`MECHANICAL WATER PUMP`
- 副標題：`FOUR WORKER-FREE TIERS · TIMBERBORN 1.1`
- 署名小字：`Made with DeepSeek V4.1 Flash`

**排版（1280×720 基準）**
- 主標題：88–110 px，奶油白 `#F7ECD0` + 深棕描邊 5–7 px + 1–2 px 投影
- 副標題：36–44 px，金色 `#FFE2A0`
- 署名：20–24 px，淡米色 `#D8CDB4`，放右下角或副標題下方
- 位置：**左上或左下**，距邊 48–64 px；**不可超過中心 1:1 方裁區**（左右各留 ~320 px 給方裁）
- 字型：`Rye`、`Alfa Slab One`、`Cabin Sketch`（木刻感）；沒有就用 `Impact` / `Arial Black`
- 版本號（可選）：`v0.2.0` 放署名旁，22 px

**不要**：在圖上放 Steam 訂閱按鈕、Discord/YouTube logo、二維碼、中文長句（縮圖不可讀）。

---

## 7. 上傳前 QA

- [ ] 1920×1080 或 2560×1440 → 縮成 1280×720 PNG（**< 1 MB**，Steam 預覽常見上限）
- [ ] 322×181 可讀：主標題至少一行可辨、泵的輪廓成立、河流/水塔可見
- [ ] 中心 1:1 方裁不切字、不切主體
- [ ] 無 AI 浮水印（豆包/即夢右下角常有；用裁切或 API 關閉）
- [ ] 無浮水印、無錯字、無多餘齒輪/斷管
- [ ] 署名 `Made with DeepSeek V4.1 Flash` 有出現（需求方要求）
- [ ] 與 Steam 頁面標題一致（Mechanical Water Pump — Four Worker-Free Drinking Water Tiers）
- [ ] 風格對照：和 Timberborn 官方截圖並排，木頭/金屬/水色調相近，不像其他遊戲

**上傳方式**：覆蓋 `mod/workshop/thumbnail.png` → `powershell -File mod\release.ps1 -SkipBuild -Install` → 遊戲 Mods 畫面按 Upload，或 `SteamUpload.exe dist\MechanicalWaterPump-0.2.0 <note> 3835434644`（會一併更新預覽）。

---

## 8. 用我們自己的渲染當圖生圖參考（最穩）

我們的模型是自製的，AI 純文字很容易畫成風車。最穩的做法：

1. 參考圖：`art/previews/t11_mk3_mk4_angles_grid.png`（Mk III/IV 四角度）或 `art/previews/tiers_factions_iso_grid.png`
2. 提示詞（img2img，denoise 0.45–0.6）：

```
Repaint this Timberborn-style mechanical water pump diorama with hand-painted game textures, warm late-afternoon light and a teal river around the platform. Keep the machine silhouettes, gears, pipes and wooden/metal frame layout. Add tiny beavers carrying water and a wooden water tank on the grass. Stylised low-poly illustration, no text, no logos.
```

3. 只用 AI 強化氛圍與海狸/環境，不要讓它改機器外形；比例與四檔大小以參考圖為準。
