# Events Module 繁中化、官方時程與發布參考

## 目錄

- [資料來源架構](#資料來源架構)
- [檔案與執行路徑](#檔案與執行路徑)
- [事件獎勵與雙語名稱](#事件獎勵與雙語名稱)
- [聊天訊息複製格式](#聊天訊息複製格式)
- [官方 Wiki MediaWiki API](#官方-wiki-mediawiki-api)
- [資源鍵與術語](#資源鍵與術語)
- [Parser、快取與 fallback](#parser快取與-fallback)
- [模組自動更新](#模組自動更新)
- [圖示與穩定 ID](#圖示與穩定-id)
- [驗證與建置](#驗證與建置)
- [本機安裝與載入驗證](#本機安裝與載入驗證)
- [CI、Release 與 GitHub Pages](#cirelease-與-github-pages)
- [常見問題](#常見問題)

## 資料來源架構

執行時的資料優先順序固定為：

1. 官方英文 Guild Wars 2 Wiki 的 `Widget:Event_timer/data.json` 最新有效 revision。
2. 本機 `events-cache/official-event-timer.json` 的 last-known-good 官方資料。
3. BHM 內建的 `ref/events.json`。

官方 Widget 是活動時間、循環、Wiki 連結與 waypoint chatlink 的權威來源。`events.json` 用來比對既有繁中名稱、分類、圖示、提醒與其他呈現資料；官方來源成功時，不得用本地時間或 waypoint 覆蓋它。

官方 Widget 不含獎勵欄位。13 隻核心世界王的保底稀有／特異裝備與地面寶箱龍晶礦，以及 Dragonstorm、扭曲木偶和兩個 Convergence 的每日保底裝備、龍晶礦與固定 2G，使用 BHM 內建且逐項附 Guild Wars 2 Wiki 來源的 `ref/event-rewards.json`；執行時不抓取 that_shaman 或其他第三方網站，也不估算掉落物市價。

ArenaNet 的 Guild Wars 2 API v2 可提供 world boss ID 與其他遊戲資源，但沒有完整 Event timers Widget 的循環與 waypoint。模組語系也不會把請求切換到中國版服務；官方時程請求明確送往 `wiki.guildwars2.com`。

## 檔案與執行路徑

| 用途 | 路徑 |
|---|---|
| 英文中性資源 | `Events Module/Properties/Resources.resx` |
| 繁中資源 | `Events Module/Properties/Resources.zh.resx` |
| 內建 fallback／呈現資料 | `Events Module/ref/events.json` |
| 已查核事件獎勵資料與 Wiki 來源 | `Events Module/ref/event-rewards.json` |
| 獎勵 schema、驗證與事件比對 | `Events Module/EventRewardData.cs` |
| 獎勵摘要控制項 | `Events Module/EventRewardSummaryControl.cs` |
| 官方 Widget schema 與 parser | `Events Module/OfficialEventTimerData.cs` |
| MediaWiki、快取與更新 | `Events Module/OfficialEventTimerService.cs` |
| 官方／內建圖示比對 | `Events Module/OfficialEventIconMatcher.cs` |
| 套用來源與顯示狀態 | `Events Module/EventsModule.cs`、`Events Module/Meta.cs` |
| 聊天訊息純格式化與安全 fallback | `Events Module/EventChatMessageFormatter.cs` |
| 設定頁格式編輯、驗證與預覽 | `Events Module/BasicSettingsView.cs` |
| 提醒通知的手動複製入口 | `Events Module/EventNotification.cs` |
| 自動更新版號、GitHub Release 檢查與驗證 | `Events Module/ModuleUpdateService.cs` |
| 發行建置自動更新閘門 | `Events Module/ModuleBuildInfo.cs` |
| Parser 與圖示測試 | `Events Module/Tests/` |
| 套件目錄宣告 | `Events Module/manifest.json` |
| CI／Release | `.github/workflows/events-module-zh-tw.yml` |
| GitHub Pages | `docs/index.html`、`docs/styles.css`、`docs/script.js`、`docs/assets/` |
| GitHub Pages 驗證 | `.agents/skills/localize-build-events-module/scripts/test-events-landing-page.ps1` |
| 本機建置成品 | `artifacts/Events-and-Metas-Observer-zh-TW/Events.Module.bhm` |
| Blish HUD 預設安裝檔 | `%Documents%/Guild Wars 2/addons/blishhud/modules/Events Module.bhm` |

`Resources.Designer.cs` 不需要為動態事件 key 產生 property；畫面使用 `ResourceManager.GetString(key) ?? fallback` 查找。

## 事件獎勵與雙語名稱

- Catalog v3 為稀有／特異、龍晶礦與固定金幣分別保存數值與帳號／角色每日限制，並以 `sources` 保留全部官方 Wiki 證據；13 隻核心世界王與 4 個公共事件都明列 stable ID，不再由龍晶礦推導稀有裝備數量。
- 比對先用官方 stable ID，再使用在完整官方／內建時程中唯一且英文別名相符的 waypoint，最後才用唯一的正規化英文別名。Outer Nayos 與巫師塔 Target Practice／Fly by Night 共用 `[&BB8OAAA=]`，不得用該 waypoint 推測獎勵；Dragonstorm 與扭曲木偶的共用北方之眼 waypoint 同理。
- 只對 catalog 中已查核的 13 隻核心世界王與 4 個固定 2G 事件顯示獎勵，未知或新增事件不得推測獎勵。
- 卡片依資料動態顯示稀有／特異裝備、地面寶箱龍晶礦或固定 G；tooltip 說明帳號／角色每日限制、特殊數量備註、Wiki 來源與查核日。
- 公共事件採每日保底口徑：Dragonstorm 為稀有／特異≥2＋2G、扭曲木偶為稀有／特異≥2＋龍晶礦15＋2G、Mount Balrior 為稀有／特異≥3＋2G、Outer Nayos 為 2G。排除 Convergence 可重複寶箱、參與度與挑戰模式；Dragonstorm 的 20–30 龍晶礦是機率分支，不列為保證。
- 固定金幣以整數銅幣保存，`20000` 精確格式化為 `2G`；不使用浮點數，也不納入 PvP 名次獎勵、Convergence 挑戰模式、事件基礎 Coin 或交易所估值。
- Catalog v3 必須拒絕重複事件 ID、stable ID、正規化別名、catalog waypoint 或來源 URL，以及空白／非官方 Wiki 來源。每個獎勵元件必須與 `account-daily`／`character-daily` 限制成對；稀有／特異或固定金幣不得為零或負數，限制不得脫離元件，也不得留下沒有任何獎勵元件的事件。
- `verifiedOn` 是查核者所在地的日曆日期；驗證允許 UTC 次日以涵蓋全球時區，但拒絕更晚的未來日期。
- Catalog 頂層 `verifiedOn` 是共用預設值；只有本次逐筆重新查核的事件才使用事件層級 override，不得為了新增少數事件而改寫其他既有資料的查核日。
- `Meta.EnglishName` 保留官方 Widget 的英文 segment 名稱。繁中顯示與英文不同時，卡片以第二行顯示英文；搜尋同時比對繁中、官方英文、模板 key 與 colloquial 名稱。
- 排序必須依 `Meta` 的本地化名稱與 `NextTime`，不可依包含換行英文副標的 `DetailsButton.Text`。

## 聊天訊息複製格式

設定存放於 `Managed Settings`：

- `useCustomCopyFormat` 預設 `false`。關閉時必須維持舊行為，只複製原始 waypoint chatlink。
- `customCopyFormat` 保存使用者輸入；繁中預設為 `{point} 【{category_zh}】 {event} {time} {reward}`。
- 關閉功能只停用編輯欄，不清除已保存的格式；重新開啟後沿用原值。
- 升級時只將完全等於舊繁中或英文預設的值換成新預設；任何自行編輯過的格式都不得覆寫。

格式欄位固定且大小寫有別：

| 欄位 | 值 |
|---|---|
| `{point}` | 已驗證的原始 waypoint chatlink |
| `{event}` | 智慧雙語事件名稱，格式為「繁中 / English」；相同時只顯示一次 |
| `{event_zh}` | 既有資源解析出的繁中事件名稱 |
| `{event_en}` | `Meta.EnglishName`，空白時 fallback 到 `Meta.Name` |
| `{category}` | 智慧雙語事件分類；相同時只顯示一次 |
| `{category_zh}` | 既有資源解析出的繁中事件分類 |
| `{category_en}` | 原始 `Meta.Category` |
| `{time}` | `Meta.NextTime.ToShortTimeString()`，與事件卡片相同的本地時間格式 |
| `{reward}` | 已查核獎勵的本地化短摘要，例如「保底：稀有/特異≥1、龍晶礦15–25」、「保底：稀有/特異≥2、龍晶礦15、2G（帳號每日）」或「保底：2G（帳號每日）」；未收錄時為空字串 |

任意內部文字、空白與標點原樣保留，完成替換後只清除訊息頭尾空白。字面 `{`、`}` 分別寫成 `{{`、`}}`。格式必須非空、含未跳脫的 `{point}`、大括號成對，且不得含未知欄位；`{{point}}` 只是字面文字，不符合必要欄位。設定頁使用下一個具有合法 waypoint 的活動即時驗證與預覽；格式含未跳脫的 `{reward}` 時，優先選擇下一個具有合法 waypoint 與 reward catalog match 的活動，無匹配者才 fallback 到一般下一個活動。無可用活動時顯示本地化的預覽不可用訊息。

`{reward}` 依序組合 `Meta.Reward.MinimumRareOrExoticItems`、`CompactDragoniteAmount` 與固定金幣，保留 `15–24*`、`1–15+` 等短標記，並讓固定金幣帶上精簡的「帳號每日」限制。不要把完整 tooltip、角色每日限制、特殊備註、Wiki 來源或查核日期塞入聊天；catalog 未載入或事件未收錄時輸出空字串，不推測獎勵，也不使格式失效。

`EventsModule.CopyEventToClipboard` 是唯一共用複製流程。事件卡片的 waypoint 圖示與提醒通知左鍵都呼叫它；只有合法 waypoint 才掛上複製動作。功能開啟且格式有效時複製完整訊息並顯示聊天訊息成功提示；格式無效時記錄 warning、複製原始 waypoint 並顯示 fallback 提示；剪貼簿失敗仍顯示紅色錯誤。提醒顯示本身不得自動改剪貼簿，也不得貼上、模擬按鍵或送出聊天。

格式化器保持純邏輯並由 `Events Module/Tests/OfficialEventTimerParser.Tests.csproj` 連結測試。至少覆蓋全部欄位、智慧雙語去重、本地時間、reward 有值／空值、compact 特殊標記、頭尾空白清除、內部文字與標點、`{{`／`}}`、reward 預覽偏好、舊預設精確遷移、功能關閉的原始輸出、功能開啟的完整輸出，以及空格式、缺少 `{point}`、未知欄位與不成對大括號的 waypoint fallback。

## 官方 Wiki MediaWiki API

端點：

```text
https://wiki.guildwars2.com/api.php
```

查詢 title `Widget:Event_timer/data.json` 的 revisions，至少取得 `ids|timestamp|sha1`；需要內容時再加 `content` 與 `rvslots=main`。使用 `format=json&formatversion=2&maxlag=5`。無快取首次啟動可直接抓完整 revision，避免 metadata 加 content 兩次往返。

要求：

- 使用可辨識且不會被 Wiki 阻擋的 User-Agent；目前常數位於 `OfficialEventTimerEndpoint.UserAgent`。
- 保持 15 秒 timeout，並區分模組卸載 cancellation 與 HTTP timeout。
- 一般六小時檢查一次；啟動時讀取有效快取，手動更新時繞過 HTTP cache。
- 只有 revision ID 或 SHA1 改變才重建事件；狀態列顯示 Widget 版本、revision、時間與 SHA1。
- 只接受 `https://wiki.guildwars2.com/` Wiki URL，以及可解碼且第一 byte 為 `0x04` 的 waypoint chatlink。
- Widget 的 `link` 可能是完整 URL，也可能是含 namespace 冒號的頁面標題，例如 `Convergence: Mount Balrior`。只有含 `://` 的值才按 URL 驗證，且必須是官方 Wiki HTTPS；其他非空值按頁面標題編碼成官方 URL。不可單獨用 `Uri.TryCreate(..., UriKind.Absolute)` 分流，因為 .NET 會把 `Convergence:` 誤認為自訂 URI scheme。

## 資源鍵與術語

必要繁中資源是以下集合：

1. `Resources.resx` 的全部 UI 與官方-only display keys。
2. `events.json` 中所有唯一 `name` 與 `category`。

官方事件轉成畫面資料時，實際 key 是 `template?.Name ?? definition.Name` 與 `template?.Category ?? definition.Category`。因此審核新 Widget 時要比對「模板匹配後的 runtime keys」，不能只看原始 segment 名稱。

規則：

- 只翻譯 `<value>`，不要改 `<data name="...">`。
- 新增、且 `events.json` 沒有對應模板的官方 key 時，同時加入 `Resources.resx` 與 `Resources.zh.resx`。中性 value 保持英文，zh value 使用台灣繁中。
- 即使來源 key 有拼字錯誤也原樣保留；修正顯示只改 zh value。
- 保留 `{0}` 等格式化 token。事件 value 不應殘留非必要英文字母。
- 不要手動修改 `Resources.Designer.cs`。

術語來源依序為：

1. [星岬島完整事件時間表](https://gw2.wishingstarmoye.com/gw2timer)：讀取頁面當下引用的 `gw2timer_new_data_*.js`，不要固定日期檔名。
2. [星岬島 BOSS 計時器](https://gw2.wishingstarmoye.com/gw2timerbox)：交叉確認核心世界王名稱。
3. Guild Wars 2 API `lang=zh`：查專有名詞，再人工轉成台灣繁體。
4. 星岬島資料庫、攻略與玩家慣稱：補計時器未涵蓋的階段、地名和頭目。

優先使用玩家能辨識的既有名稱，例如 `Ley-Line Anomaly` 使用「魔徑異常體」。簡體來源須人工檢查異體字、地名與遊戲內慣用譯名，不用英文逐字直譯定稿。

## Parser、快取與 fallback

`OfficialEventTimerParser` 必須：

- 驗證 config version、group／segment／sequence 完整性、合理數量、duration、segment reference 與唯一 stable ID。
- 先套 `partial`，再重複 `pattern` 展開完整 UTC 日循環。
- 略過官方 Event timers 頁本身不顯示的季節性 group。
- 產生 `wiki:{group}:{segment}` stable ID。
- 正確解析普通頁面標題、含 `:` 的 namespace 標題、`#anchor` 與官方絕對 HTTPS URL，並拒絕 malformed、非 HTTPS 或非官方網域的 URL 型輸入。
- 不替沒有 chatlink 的 segment 製造 waypoint。

快取必須在內容完整通過 parser 後才寫入，並以 temporary file 加 replace/copy 原子更新。壞掉或 schema 過期的快取直接忽略。網路、HTTP、MediaWiki、JSON 或 timeout 失敗時，有有效快取就使用快取；沒有才退回內建 `events.json`。

來源狀態 UI 不應只顯示原始 `TaskCanceledException`。timeout 應顯示本地化秒數與 fallback 來源，卸載 cancellation 則安靜結束。

## 模組自動更新

發行 BHM 每次載入時，會以不阻塞 `LoadAsync`／每幀更新的背景工作呼叫：

```text
https://api.github.com/repos/jakeuj/Community-Module-Pack/releases/latest
```

檢查 timeout 為 10 秒，卸載時必須取消 HTTP。只接受非 draft、非 prerelease、tag 精確符合 `events-zh-tw-vX.Y.Z-fork.N` 的穩定版；`-test`、無 fork、格式錯誤與降版都不安裝。版號以 `(major, minor, patch, fork)` 比較，來源 manifest 的 `X.Y.Z` 視為 `fork.0`。

Release 必須恰有一個名稱為 `Events.Module.bhm` 的資產、`https://github.com/jakeuj/Community-Module-Pack/releases/download/...` 下載網址，以及 `sha256:<64 hex>` digest。任何 HTTP、JSON、資產、網址或摘要驗證失敗都保留舊版運作且不得重啟。

`Managed Settings/autoUpdate` 預設 `true`。關閉後仍檢查與顯示新版本，但只在按「立即更新」時安裝；重新勾選不會對已完成的檢查突然執行更新，要等手動按鈕或下次載入。成功安裝才恢復 `bh.general.events` 啟用狀態、儲存設定，並由主執行緒呼叫 Blish HUD restart。

下載與 SHA-256 驗證交給 Blish HUD 1.0.0 公開的 `ModulePkgRepoHandler.InstallPackage`，不要使用會在下載前停用舊模組的 `ReplacePackage`。自動替換只允許一般安裝的 `.bhm`；解壓縮模組或由 `--module`／`-M` 載入的偵錯 BHM 只能通知，不能安裝。

一般本機、Debug、PR／push 建置不定義 `RELEASE_BUILD`，執行期自動更新會停用，避免開發成品自我刪除。只有建置腳本收到合法穩定 `PackageVersion` 才啟用；test 套件可寫入 test 版號，但仍不啟用自動更新。

自動更新只對「已經安裝含更新器的發行 BHM」生效。第一個含更新器的穩定版仍需使用者手動安裝一次；只在 GitHub Releases 上新增檔案，無法讓更舊、尚未包含更新器的 BHM 自動取得它。之後的版本只要由發行流程產生合規 tag、manifest、資產名稱與 digest，已啟用自動更新的使用者就會在下次載入模組時檢查並套用。

## 圖示與穩定 ID

官方事件優先沿用匹配的內建 template 圖示；找不到時，依 stable ID 套本地 64×64 透明 PNG，再嘗試名稱、地點或 waypoint matcher，最後使用安全的通用圖示。不可為了填空而套不相關頭目圖示。

驗證：

```powershell
& "Events Module\Tests\ValidateEventIcons.ps1"
& .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1
```

前者檢查 PNG 尺寸、alpha、透明角落、可見覆蓋率與 chroma-key；後者也測試 stable-ID 圖示 mapping。修改 `wiki:{group}:{segment}` 會影響已追蹤事件與本地圖示 mapping，必須保留 ID 或提供設定遷移。

## 驗證與建置

繁中 coverage：

```powershell
& .agents\skills\localize-build-events-module\scripts\validate-events-localization.ps1
```

Parser 離線測試與 live audit：

```powershell
& .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1
& .agents\skills\localize-build-events-module\scripts\test-official-event-timer.ps1 -Live
```

`-Live` 會查目前 Wiki revision，驗證已知 stable ID 的 waypoint、Mount Balrior／Outer Nayos 等 namespace Wiki 連結，以及精確的 17 組 `(stable ID, reward ID)`；不得先對 reward ID 去重，以免漏掉共用 waypoint 造成的額外事件誤配。測試明確拒絕 `wiki:soto-wt:1`、`:2`、`:3` 的獎勵；網路不可用時，離線測試仍須通過。

建立本參考版本時的基準為：

- 104 筆內建活動資料。
- 95 個唯一內建活動名稱／分類。
- 230 個必要繁中資源鍵。
- 官方 Widget v5.2 展開 104 個可顯示事件。
- 17 筆已查核獎勵可與官方 Widget 匹配（13 隻核心世界王＋4 個固定 2G 事件）。

官方與內建資料都可能增加，以腳本與 live audit 的動態輸出為準，不要把這些數字當永久 schema。

建置：

```powershell
& .agents\skills\localize-build-events-module\scripts\build-events-zh-tw.ps1

# 僅供發行流程；不會修改來源 manifest.json
& .agents\skills\localize-build-events-module\scripts\build-events-zh-tw.ps1 `
    -PackageVersion 1.0.9-fork.5
```

預設輸出：

```text
artifacts/Events-and-Metas-Observer-zh-TW/Events Module.dll
artifacts/Events-and-Metas-Observer-zh-TW/Events.Module.bhm
```

`ChineseBuild=true` 會把 `Resources.zh.resx` 以中性 resources 嵌入主 DLL。正式交付 BHM，因為它還包含 manifest、相依 DLL、`events.json`、`event-rewards.json` 與 textures。

來源 `manifest.json` 保留上游基礎版號 `X.Y.Z`。`-PackageVersion X.Y.Z-fork.N` 只改寫封裝內 manifest，並為穩定版定義 `RELEASE_BUILD`；BHM 不得包含主程式的 `Blish HUD.exe`、`SemVer.dll` 或任何巢狀 `.bhm`。腳本同時驗證最低需求 `bh.blishhud >=1.0.0`、fork 專案網址與封裝版號。

Blish HUD 的封裝 target 會把輸出目錄現有檔案納入套件。每次建置前，腳本必須先刪除該 `OutDir` 中舊的 `Events Module.bhm` 與 `Events.Module.bhm`，建置後再檢查 archive entries 不含 `.bhm`；同一輸出目錄連續建置不得讓套件遞迴包入上一版並持續變大。

建置腳本也會執行 `ValidateEventIcons.ps1`，確認來源事件圖示符合規格，並逐一檢查 BHM 內存在對應的 `ref/textures/events/*.png`。成功結果會回報 `IconCount`；不可只因 DLL 編譯成功就視為圖示已封裝。

## 本機安裝與載入驗證

建置成品不會自動成為 Blish HUD 正在使用的模組。先以唯讀模式比對建置檔和已安裝檔：

```powershell
& .agents\skills\localize-build-events-module\scripts\install-events-zh-tw.ps1 -CheckOnly
```

比對結果包含雙方 SHA-256、事件圖示數量、目的檔是否存在，以及 Blish HUD 是否仍在執行。預設目的地透過 `[Environment]::GetFolderPath('MyDocuments')` 取得，會正確跟隨 OneDrive 等 Windows Documents 重新導向；不要假設安裝路徑一定在 `$env:USERPROFILE\Documents`。

只有使用者明確要求安裝，而且 Blish HUD 已完全結束時才執行：

```powershell
& .agents\skills\localize-build-events-module\scripts\install-events-zh-tw.ps1
```

安裝腳本會：

1. 驗證來源 BHM 至少含 manifest、主 DLL、`ref/events.json`、`ref/event-rewards.json`，以及目前來源目錄中的全部事件圖示。
2. 偵測 Blish HUD process；仍在執行時立即拒絕，不會自動關閉程式，也不會修改檔案。
3. 為既有 BHM 建立帶時間戳的 `.bak` 備份。
4. 先複製到同目錄的唯一暫存檔並驗證雜湊，再替換目的檔。
5. 重新讀取目的檔，確認最終 SHA-256 與事件圖示數量。

實機驗收至少需要：建置與安裝檔 SHA-256 相同、安裝檔內可見預期圖示、替換後重新啟動 Blish HUD。若仍異常，再檢查最新的 `logs/blishhud.*.log`；舊程序的畫面不能證明新 BHM 已載入。

## CI、Release 與 GitHub Pages

`.github/workflows/events-module-zh-tw.yml` 會在 `master`／PR 的 Events Module 變更時跑 coverage、圖示、parser、建置與封裝檢查；這些一般建置不帶 `PackageVersion`，因此不啟用執行期自動更新。手動 dispatch 或 `events-zh-tw-v*` tag 才把 tag 去除 `events-zh-tw-v` 後寫入 BHM manifest、建立標籤、產生 release notes 並發布精確名稱 `Events.Module.bhm`。

Fork tag 格式：

```text
events-zh-tw-vX.Y.Z-fork.N
```

`X.Y.Z` 跟隨 manifest 的上游基礎版號；Fork 修改只遞增 `fork.N`。只有使用者明確要求發布才推 tag／Release。先推 `master` 並等一般 CI 通過，再手動 dispatch；留空版號可自動選下一個 Fork 版號。CI 會驗證 tag、BHM manifest 版號、Blish HUD 最低需求與資產名稱一致，發布後再從 GitHub Release API 讀回 digest，與本機 `Events.Module.bhm` SHA-256 比對。

GitHub Pages 使用 fork 的 `master:/docs`，網址為 `https://gw.jakeuj.com/`。網站定位為「Events 玩家下載頁＋工程案例＋jakeuj GW2 工具入口」，品牌使用 `jakeuj GW2 Tools`。工具箱至少清楚區分 Events 的 `Blish HUD Module`、Upgrade Value 的 `Nexus Addon` 與 ArcDPS 繁中 UI 的 `ArcDPS Plugin`；外部專案使用明確連結，不在本 repo 修改或依賴其線上圖片。

只有使用者要求更新網站時才修改；只有使用者明確要求發布時才推送部署。功能、Release 或 coverage 有重大變更時，同步更新頁面可見文案、HTML fallback、JSON-LD、Open Graph 圖文與可追溯的統計。下載按鈕固定使用不綁版號的：

```text
https://github.com/jakeuj/Community-Module-Pack/releases/latest/download/Events.Module.bhm
```

網站的 GitHub Release API 只是漸進增強：3 秒 timeout，只接受非 draft／非 prerelease、精確 `events-zh-tw-vX.Y.Z-fork.N` tag、唯一 `Events.Module.bhm`、受信任的 GitHub download URL 與 `sha256:<64 hex>` digest。任何失敗都保留 HTML 內建版本；停用 JavaScript 時，主要內容與下載仍可使用。

視覺可借用 GW2 的暗色手繪奇幻、地圖、金屬符文與魔法能量語彙，但只能使用原創背景、專案實機截圖與模組已有圖示，不複製官方 Logo、角色或宣傳構圖，也要保留非 ArenaNet 官方發行聲明。圖片優先產生多尺寸 AVIF／WebP，保留傳統 fallback；首屏以外 lazy-load。動畫不得依賴 WebGL、音效或捲動劫持，且須在 `prefers-reduced-motion` 關閉持續動態、在觸控／粗略指標裝置降低效果。

本機驗證：

```powershell
& .agents\skills\localize-build-events-module\scripts\test-events-landing-page.ps1
```

腳本會檢查必要品牌／工具／Release 契約、JSON-LD、1200×630 分享圖、圖片 alt、所有本機 `./` 引用、reduced-motion CSS、JavaScript 語法與工作樹 whitespace。視覺驗收另外在 1440px、768px、390px 檢查鍵盤、觸控、螢幕閱讀器語意、reduced-motion、API 失敗與停用 JavaScript；不得出現水平溢位或讓內容因 reveal JavaScript 失敗而永久隱藏。

發布前先重跑繁中 coverage 與 `test-official-event-timer.ps1 -Live`，以當下輸出更新頁面數字，不把資源鍵／事件／獎勵數量當永久常數。只提交預期檔案並推到 fork 的 `master`，不要推上游 `origin`，也不要為網站更新建立模組 tag 或 Release。推送後等待 Pages build 指向本次 commit 且狀態為 `built`，再從正式網域驗證品牌內容、Hero 資產、Upgrade Value、ArcDPS UI、最新版 Release 與 BHM 下載：

```powershell
& .agents\skills\localize-build-events-module\scripts\test-events-landing-page.ps1 `
    -Live -WaitForCommit (git rev-parse HEAD)
```

## 常見問題

### 畫面仍有英文

先跑 coverage，再確認該事件實際採用內建 template key 還是官方 segment key。新官方-only key 要同時加入 neutral 與 zh resx；只翻舊 `events.json` 不足以涵蓋 live Widget。

### 座標或時間跟官方 Wiki 不同

查看模組設定的資料來源狀態、Widget 版本、revision 與 SHA1。確認目前不是 last-known-good cache 或 bundled fallback；不要用中文第三方網站的時間／座標覆寫官方 Widget。

### HTTP 403

確認請求送到 `https://wiki.guildwars2.com/api.php`，並帶有非預設、可辨識的 User-Agent。不要把語系選擇實作成切換 Wiki 或 API host。

### 顯示 task was canceled

區分模組卸載 cancellation 與 15 秒 HTTP timeout。timeout 應回退快取／內建資料並顯示本地化狀態，不直接把例外文字放到 UI。

### 沒有 Wiki 按鈕

先確認卡片的 `Meta.Wiki` 是否為空，再比對官方快取原始 `segment.link`／`group.link` 與 `OfficialEventDefinition.Wiki`。若原始值像 `Convergence: Mount Balrior` 一樣含冒號但解析結果為空，檢查 parser 是否把它當成絕對 URI scheme；修正 `BuildWikiUrl` 的 URL／頁面標題分流，並加入離線 namespace-title 測試與 `wiki:public-con:1`、`:2` live assertions。官方來源成功時不要用 `events.json` 的 Wiki 欄位掩蓋 parser 錯誤。

### 沒有 waypoint 按鈕

先看官方 segment 是否有合法 chatlink。官方未提供時屬預期行為，不可從模糊名稱猜 waypoint。

### 找不到 .NET Framework 4.7.2 參考組件

使用 NuGet `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 的 `build/` 作為 `TargetFrameworkRootPath`。不要只設 `FrameworkPathOverride`，否則可能漏掉 `Facades/netstandard.dll`。技能內測試與建置腳本會自動處理。

### 沒有產生 BHM

先從 `Community Module Pack.sln` 還原 `packages.config`，確認 `packages/BlishHUD.1.0.0/build/BlishHUD.targets` 與 `packages/SemanticVersioning.1.2.2` 存在。舊式 csproj 單獨 restore 可能因缺少 `SolutionDir` 失敗。

### BHM 每次建置都變大或內含另一個 BHM

列出 archive entries，確認沒有副檔名為 `.bhm` 的項目。建置前必須從實際 `OutDir` 清除舊的 `Events Module.bhm` 與 `Events.Module.bhm`；不要只刪 artifacts 目錄外的同名檔。保留建置腳本的 nested-package gate，若命中就視為封裝失敗，不要交付該成品。

### 替換後仍看到舊內容

先執行安裝腳本的 `-CheckOnly`，比較建置與安裝檔的完整路徑、大小、最後修改時間、SHA-256 與 `ref/textures/events/` 項目。雜湊不同代表只完成建置、尚未安裝；安裝檔沒有事件圖示則代表仍是舊套件。完全結束 Blish HUD 後使用安全安裝腳本替換，再重新啟動並確認雜湊相同。不要在程序仍載入 DLL/BHM 時覆寫，也不要只靠檔名判斷版本。

### 發布後沒有自動更新

先確認使用者目前版本本身已包含更新器、Blish HUD 至少為 1.0.0，且設定中的「自動更新此模組」已開啟。再檢查最新 Release 是否為非 draft／非 prerelease、tag 精確符合 `events-zh-tw-vX.Y.Z-fork.N`、恰有一個 `Events.Module.bhm`，且 GitHub API 已回報 `sha256:<64 hex>` digest。解壓縮模組、一般開發建置、`-test` BHM，以及由 `--module`／`-M` 載入的檔案只允許檢查或顯示狀態，不會自行替換。

檢查或下載失敗時不應重啟。查看模組設定的更新狀態與最新 `blishhud.*.log`，區分 HTTP／JSON／tag／資產／digest 驗證失敗；不要改成跳過 SHA-256，亦不要以 `ReplacePackage` 重試。若必須備援，完全結束 Blish HUD 後手動下載最新 `Events.Module.bhm`，再使用安全安裝流程。
