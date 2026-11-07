# 上架前驗收清單（RELEASE CHECKLIST）

> 用途：Steam 工作坊／GitHub 發佈前，由人類在遊戲內逐項確認。每項通過就打勾並記下數值/截圖；
> 有失敗就把 `Player.log` 對應行貼給 Lead。需求門檻：`VERIFICATION.md` §2 與 `REQ-TEST-02`。

## 0. 安裝與啟用（5 分鐘）

```powershell
cd "C:\Users\user\Downloads\Mechanical Water Pump"
powershell -NoProfile -ExecutionPolicy Bypass -File mod\release.ps1 -Install
```
- 遊戲 Mods 畫面確認 **Mechanical Water Pump 0.2.0** 已啟用、無紅字
- 開新檔（Folktails；之後再開一次 IronTeeth）

## 1. 建造與 UI（T01/T14）

- [ ] Water 工具組出現 4 檔（Mk I–IV），圖示正確、無破圖
- [ ] 四檔都能建造、拆除；Mk I/II 全木外觀、Mk III/IV 金屬/木混合
- [ ] Mk III/IV 可放在岸邊（前排 3 格貼岸即可），不再被地形卡住
- [ ] 面板顯示：狀態、產量、動力需求/供給、水質、抽水深度、庫存、機況、下次維護、材料、自動維護、立即維護/暫停、管道狀態
- [ ] 四檔模型齒輪/飛輪/活塞會動；斷電停止；低供電變慢
- [ ] F12 截圖：Mk I、Mk III（白天、完成品）

## 2. 產水與供電（T02/T03/T27）

- [ ] 100% 供電、乾淨水、庫存空：24 遊戲小時產量 ≈ **72 / 144 / 360 / 480**
      （Mk I/II/III/IV，誤差 ≤5%；可用 3 倍速）
- [ ] 供水箱＋搬運工或 Timber Pipes 把水搬走後，庫存會下降並持續產水
- [ ] 供電 0% → 產水 0、動畫停止；25/50/75/100% → 速率單調上升
- [ ] 庫存滿 → 顯示「內部水槽已滿」、停止產水；庫存上限顯示 **15/30/75/100**（不是 2147483647）

## 3. 水質／水源（T04/T05）

- [ ] 抽水深度超過上限（Mk I=2、II=4、III=6、IV=8 格）→ 停機 NoWater
- [ ] 壞水污染 → 產量下降、水質%下降，但永不產出壞水

## 4. 維護（T16–T22）

- [ ] 關閉自動維護後，運轉至機況 75/50/25 → 效率 100/90/75/50%，0 停產
- [ ] 手動維護：扣材料（Mk I 10 Log / II 15 Plank / III 10 MetalBlock+10 Plank / IV 15 MetalBlock）、停機 2 遊戲小時、回到 100%
- [ ] 材料不足 → 顯示缺料、不扣料；無路 → 顯示無路可達
- [ ] 30 日平均產量 90–105% 表定值（可用加速或信任單元測試 93–94%）

## 5. 存檔與相容（T09/T10/T11/T15）

- [ ] 產水中存檔 → 讀檔：小數庫存、狀態、暫停、機況保持
- [ ] 未安裝 Timber Pipes：建造/產水/搬運正常、log 無 MWP/Error
- [ ] 安裝 Timber Pipes：One-way Valve 對準泵、選 Extract → 水進管道/水箱（MUST）
- [ ] 與 Pump to Tank／Mechanical Filter Pump／Barkimedes Screw 同時載入無崩潰（T12）
- [ ] 移除本模組後讀檔可載入、無未知節點錯誤

## 6. 效能（T13/T24）

- [ ] 100 台泵滿載：FPS 下降 ≤3、無記憶體暴增
- [ ] `MWP_Config.json` 把 `features.uiDiagnostics` 設 true → log 每 60 秒有 `MWP/Info/Diag`（版本/泵數/adapter/avgTickMs）

## 7. 上架步驟（人類帳號）

1. `mod/workshop/workshop_data.json` 的 `ItemId` 首次保持 `"0"`；上傳成功後把 Workshop ID 寫回 → 之後更新才不會重複開新項目
2. 遊戲 Mods 畫面 → 選擇本模組 → **Upload**（Steam 工作坊協定需你的帳號同意）
3. GitHub：`git push -u origin main`（本機 `0b2b054` 已 commit）；release 上傳 `dist/MechanicalWaterPump-<version>.zip`
4. 更新 `mod/CHANGELOG.md`、`mod/data/manifest.json` 版本、`thumbnail.png`（如外觀大改）

## 8. 發佈前最後一輪（Lead 執行）

- [ ] 移除 `MwpPumpController.ModelDiag.cs` 與 `Activate()` 內的呼叫
- [ ] `pwsh -File mod/release.ps1 -Install`（重新打包上傳版）
- [ ] 確認 `dist/*.zip` 的 sha256 與 GitHub release 一致
- [ ] `VERIFICATION.md` 的 T01–T27 結果填寫完成
