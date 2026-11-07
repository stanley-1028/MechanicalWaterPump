# Steam 工作坊介紹文（Mechanical Water Pump）

> 用途：`mod/data/manifest.json` 的 `Description`（遊戲內／SteamUpload 上傳器直接讀取，支援 BBCode）
> 與 Steam 頁面 Edit 貼上；本檔同時保存中英雙語版本與短版文案。
> 封面：`mod/workshop/thumbnail.png`（AI 生成原圖 → 裁掉浮水印 → 壓字 1280×720，569 KB）。

---

## 0. 頁面短描述（Steam 搜尋結果 / GitHub repo description 用）

**EN**：Four worker-free mechanical drinking-water pump tiers for Timberborn 1.1 — animated gear
machinery, maintenance, hauling and native UI. Made in collaboration with DeepSeek V4.1 Flash.

**中文**：Timberborn 1.1 的四檔免工人機械抽水泵：齒輪/活塞動畫、維護系統、水搬運、原生 UI；與
DeepSeek V4.1 Flash 共同完成。

---

## 1. Steam 頁面完整文案（BBCode，貼進 `Description` 或網頁 Edit）

```bbcode
[h1]Mechanical Water Pump — four worker-free drinking-water tiers[/h1]

Four mechanical pumps for Timberborn 1.1 that make drinking water without a single beaver working the pump. You pay research, planks, metal blocks and mechanical power instead — and the water keeps flowing through droughts and badwater alike (at a lower rate when the source is contaminated).

[h2]What you get[/h2]
[list]
[*][b]Four tiers[/b] with distinct footprints and balance: Mk I (2×3), Mk II (2×3), Mk III (3×3), Mk IV (3×4).
[*][b]Rated output 3 / 6 / 15 / 20 Water per hour[/b] at 100% power; a 30-day average of 93–94% once maintenance is included.
[*][b]Animated machinery[/b]: flywheels, gears, pulleys, drive shafts, pistons and linkages spin with the mechanical network — speed follows power efficiency and stops at 0 hp.
[*][b]Maintenance system[/b]: condition wears from 100% to 0%, upkeep materials and a 2-hour downtime, with automatic or manual maintenance.
[*][b]Internal water storage[/b] (15 / 30 / 75 / 100) that your beavers haul to tanks — or extract with a Timber Pipes valve if you have that mod.
[*][b]Native Timberborn UI[/b]: same nine-slice panels, typography, progress bars, buttons and native power/water fragments as vanilla buildings.
[*]English, 简体中文 and 繁體中文 localization, plus a JSON config for balance tweaks. No dependencies.
[/list]

[h2]The four tiers[/h2]
[b]Mk I[/b] — 150 science · 3 Water/h · 100 hp · storage 15 · upkeep 10 Log · a small wooden frame with one flywheel, perfect for the first settlement.
[b]Mk II[/b] — 400 science · 6 Water/h · 200 hp · storage 30 · upkeep 15 Plank · a wooden machine shed with an exposed gear train and twin pistons.
[b]Mk III[/b] — 700 science · 15 Water/h · 500 hp · storage 75 · upkeep 10 MetalBlock + 10 Plank · a timber-and-metal machine room with glass inspection windows, a crankshaft and three phased pistons.
[b]Mk IV[/b] — 1,500 science · 20 Water/h · 700 hp · storage 100 · upkeep 15 MetalBlock · a large metal cage pump with twin flywheels, valve wheels and two thick intake pipes.

[h2]How it plays[/h2]
Place the pump on a river bank (only the front row needs solid ground), connect it to your mechanical network, and let it fill its internal tank. Beavers haul the water to your tanks, or pipe it out. When condition drops, send materials and let it rest for two hours — or switch on automatic maintenance and forget about it. The pump needs no workers, so it is ideal for remote river spots and for colonies that would rather spend beavers elsewhere.

[h2]Compatibility[/h2]
Requires Timberborn 1.1.x. Save-compatible; no other mods required. Timber Pipes is detected at runtime and used for valve extraction when present, but the mod works without it. Balance values can be changed in MWP_Config.json.

[h2]Credits[/h2]
Made in collaboration with [b]DeepSeek V4.1 Flash[/b] — AI pair-development of code, data, art pipeline, validation and documentation. All models, textures, animations and icons are self-made for this mod.

[h2]中文簡介[/h2]
四檔免工人機械抽水泵：不用海狸顧泵，用研究、木板、金屬與機械動力換取飲用水。含齒輪/飛輪/活塞動畫（轉速隨供電效率、停電即停）、機況磨損與材料維護（停機 2 小時）、內部儲水（15/30/75/100，海狸會搬去儲水罐，或接 Timber Pipes 閥門抽水）、原生 Timberborn 面板樣式、英/簡中/繁中三語與 JSON 平衡設定。Mk I–IV 產量 3/6/15/20 水/小時，30 日平均 93–94%。與 DeepSeek V4.1 Flash 共同完成。
```

---

## 2. Change note（更新時填）

**首次發佈**：First public release — four worker-free pump tiers, animated machinery, maintenance, native UI. Made in collaboration with DeepSeek V4.1 Flash.

**換封面／改介紹**：New cover art and rewritten description. Made in collaboration with DeepSeek V4.1 Flash.

---

## 3. 上傳檢查

- [x] `mod/workshop/thumbnail.png` = 1280×720 PNG、569 KB（< 1 MB）、已去「豆包 AI 生成」浮水印
- [x] 322×181 縮圖可讀：標題、四檔輪廓、河流與海狸都看得見（`mod/workshop/cover/qa-322x181.png`）
- [x] 文字在中心 1:1 方裁內（標題/副標題/署名皆置中）
- [x] 署名 `Made with DeepSeek V4.1 Flash` 同時出現在封面與介紹文
- [x] 原始 AI 圖保存在 `mod/workshop/cover/ai-raw-2732x1535.jpg`，無字版 `clean-1920x1080.jpg`
- 更新方式：覆蓋 `thumbnail.png` → `build.ps1` → `release.ps1 -SkipBuild -Install` →
  `SteamUpload.exe dist\MechanicalWaterPump-0.2.0 <note> 3835434644`
