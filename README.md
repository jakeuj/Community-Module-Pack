# Events and Metas Observer 台灣繁體中文版

Guild Wars 2 的 Blish HUD 活動行程與通知模組，提供台灣繁體中文介面、英文事件名稱對照、核心世界王獎勵資訊，以及世界首領、大型活動、匯流與冒險通知。

![Events and Metas Observer 繁中版實機畫面，顯示中英文事件名稱、世界王獎勵摘要與每日限制說明](docs/assets/events-module-rewards-bilingual.png)

> 實機畫面：事件卡片同時顯示繁中與英文名稱；世界王卡片提供獎勵摘要，移至圖示上可查看每日限制、Wiki 來源與查核日期。

## 主要功能

- **繁中與英文名稱並列**：繁中名稱下方保留官方英文事件名稱，方便對照英文攻略；搜尋支援繁中、英文與既有別名。
- **13 隻核心世界王獎勵**：卡片顯示保底稀有／特異裝備最低件數與地面寶箱龍晶礦數量。
- **清楚標示每日限制**：滑鼠提示區分額外獎勵寶箱的每帳號每日限制，以及地面寶箱的每角色每日限制。
- **官方時程與傳送點**：活動時間、Wiki 連結與 waypoint 優先採用 Guild Wars 2 官方 Wiki Event Timer；連線失敗時依序使用成功快取與內建資料。
- **通知與事件追蹤**：可追蹤指定事件、調整通知位置、切換提示音，並快速複製附近傳送點。
- **專屬事件圖示**：晝夜循環、自動錦標賽、入侵與特殊事件使用穩定事件 ID 對應的辨識圖示。

## 獎勵資料來源

官方 Event Timer 本身不提供獎勵欄位。本模組的世界王獎勵資料逐項查核自目前的 [Guild Wars 2 Wiki](https://wiki.guildwars2.com/wiki/World_boss)，並連同來源頁面與查核日期內建於 [`event-rewards.json`](Events%20Module/ref/event-rewards.json)。執行時不會抓取 that_shaman 或其他第三方計時網站；未列入資料表的事件不會推測獎勵。

> [!IMPORTANT]
> 使用本繁中模組前，請先安裝 **1.0.0 以上**且支援中文的 Blish HUD。只安裝 `Events.Module.bhm` 而使用官方英文版 Blish HUD，主程式介面仍可能顯示英文。事件卡片第二行的英文名稱則是本模組刻意保留的攻略對照功能，不代表翻譯失效。

## 安裝前置：中文 Blish HUD

1. 先閱讀[巴哈姆特《Blish HUD 中文版》教學](https://forum.gamer.com.tw/Co.php?bsn=16901&sn=139691)。
2. 前往 [m21248074/Blish-HUD Releases](https://github.com/m21248074/Blish-HUD/releases) 下載最新的中文版 Blish HUD，並依照上述教學完成安裝。
3. 確認中文 Blish HUD 可正常啟動後，再安裝本頁的 Events Module 繁中模組。

## 安裝 Events Module

1. 完全關閉 Blish HUD。
2. 從[GitHub Releases](https://github.com/jakeuj/Community-Module-Pack/releases/latest)下載最新的 `Events.Module.bhm` 完整安裝包。
3. 將檔案放入：

   ```text
   %UserProfile%\Documents\Guild Wars 2\addons\blishhud\modules
   ```

   若「文件」資料夾由 OneDrive 同步，實際路徑可能位於 OneDrive 資料夾內。
4. 重新啟動 Blish HUD，在模組清單中找到 **Events and Metas Observer** 並啟用。

## 自動更新

- 「自動更新此模組」預設開啟。每次載入模組時會在背景檢查一次 [GitHub 最新穩定版](https://github.com/jakeuj/Community-Module-Pack/releases/latest)；測試版、預發行版、格式錯誤或較舊版本不會安裝。
- 下載檔案通過 GitHub Release 的 SHA-256 驗證後，模組會保留啟用狀態並立即重新啟動 Blish HUD。檢查、下載或驗證失敗時會繼續使用原版本，也不會重啟。
- 關閉自動更新後仍會檢查並通知新版；可在模組設定中按「重新檢查」或「立即更新」。重新勾選不會突然安裝或重啟，會等到按下「立即更新」或下次載入。

> [!NOTE]
> 第一個含更新器的穩定版仍需手動安裝一次。使用者安裝該版本後，後續由本專案合規發布的穩定版，才會在下次載入模組時自動檢查與更新；尚未內建更新器的舊版無法只靠新增 GitHub Release 自行升級。

### 發布版本必須符合

- Release 不是草稿或 prerelease，tag 精確符合 `events-zh-tw-vX.Y.Z-fork.N`。
- Release 中恰有一個名稱精確為 `Events.Module.bhm` 的資產，並由 GitHub 提供 `sha256:<64 hex>` digest。
- BHM 內的 manifest 版號與 tag 一致，且最低需求為 Blish HUD `>=1.0.0`。

專案的發行 workflow 會驗證上述條件及發布後的 SHA-256。只在 Releases 頁面隨意上傳或改名 BHM，不能保證會被更新器接受。

若自動更新無法使用，請完全關閉 Blish HUD，再依上方安裝步驟手動下載並替換 `Events.Module.bhm`。解壓縮載入的模組，以及透過 `--module`／`-M` 載入的偵錯 BHM，只會顯示更新狀態，不會自行替換開發成品。

## 相關連結

- [Events and Metas Observer 繁中版官網](https://gw.jakeuj.com/)
- [GW2 ArcDPS 繁體中文 UI](https://github.com/jakeuj/GW2-ArcDPS-TChineseUI)
- [回報問題](https://github.com/jakeuj/Community-Module-Pack/issues)

## 聲明

本專案是 Community-Module-Pack 的社群繁體中文 fork，非 ArenaNet、Blish HUD 或原始模組作者的官方發行版。
