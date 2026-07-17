# Events Module 繁中化、官方時程與發布參考

## 目錄

- [資料來源架構](#資料來源架構)
- [檔案與執行路徑](#檔案與執行路徑)
- [世界王獎勵與雙語名稱](#世界王獎勵與雙語名稱)
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

官方 Widget 不含獎勵欄位。13 隻核心世界王的保底稀有／特異裝備、地面寶箱龍晶礦與每日限制，使用 BHM 內建且逐項附 Guild Wars 2 Wiki 來源的 `ref/event-rewards.json`；執行時不抓取 that_shaman 或其他第三方網站。

ArenaNet 的 Guild Wars 2 API v2 可提供 world boss ID 與其他遊戲資源，但沒有完整 Event timers Widget 的循環與 waypoint。模組語系也不會把請求切換到中國版服務；官方時程請求明確送往 `wiki.guildwars2.com`。

## 檔案與執行路徑

| 用途 | 路徑 |
|---|---|
| 英文中性資源 | `Events Module/Properties/Resources.resx` |
| 繁中資源 | `Events Module/Properties/Resources.zh.resx` |
| 內建 fallback／呈現資料 | `Events Module/ref/events.json` |
| 核心世界王獎勵資料與 Wiki 來源 | `Events Module/ref/event-rewards.json` |
| 獎勵 schema、驗證與事件比對 | `Events Module/EventRewardData.cs` |
| 獎勵摘要控制項 | `Events Module/EventRewardSummaryControl.cs` |
| 官方 Widget schema 與 parser | `Events Module/OfficialEventTimerData.cs` |
| MediaWiki、快取與更新 | `Events Module/OfficialEventTimerService.cs` |
| 官方／內建圖示比對 | `Events Module/OfficialEventIconMatcher.cs` |
| 套用來源與顯示狀態 | `Events Module/EventsModule.cs`、`Events Module/Meta.cs` |
| 自動更新版號、GitHub Release 檢查與驗證 | `Events Module/ModuleUpdateService.cs` |
| 發行建置自動更新閘門 | `Events Module/ModuleBuildInfo.cs` |
| Parser 與圖示測試 | `Events Module/Tests/` |
| 套件目錄宣告 | `Events Module/manifest.json` |
| CI／Release | `.github/workflows/events-module-zh-tw.yml` |
| GitHub Pages | `docs/index.html`、`docs/styles.css` |
| 本機建置成品 | `artifacts/Events-and-Metas-Observer-zh-TW/Events.Module.bhm` |
| Blish HUD 預設安裝檔 | `%Documents%/Guild Wars 2/addons/blishhud/modules/Events Module.bhm` |

`Resources.Designer.cs` 不需要為動態事件 key 產生 property；畫面使用 `ResourceManager.GetString(key) ?? fallback` 查找。

## 世界王獎勵與雙語名稱

- 獎勵先用 waypoint chatlink 精確比對，再以正規化英文名稱 fallback；同名的 Great Jungle Wurm 與 Evolved Jungle Wurm 必須由 waypoint 區分。
- 只對 catalog 中已查核的 13 隻核心世界王顯示獎勵，未知或新增事件不得推測獎勵。
- 卡片顯示稀有／特異裝備最低件數與地面寶箱龍晶礦；tooltip 說明每帳號／每角色每日限制、特殊數量備註、Wiki 來源與查核日。
- `verifiedOn` 是查核者所在地的日曆日期；驗證允許 UTC 次日以涵蓋全球時區，但拒絕更晚的未來日期。
- `Meta.EnglishName` 保留官方 Widget 的英文 segment 名稱。繁中顯示與英文不同時，卡片以第二行顯示英文；搜尋同時比對繁中、官方英文、模板 key 與 colloquial 名稱。
- 排序必須依 `Meta` 的本地化名稱與 `NextTime`，不可依包含換行英文副標的 `DetailsButton.Text`。

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

`-Live` 會查目前 Wiki revision，並驗證幾個已知 stable ID 的 waypoint 與 13 筆世界王獎勵匹配；網路不可用時，離線測試仍須通過。

建立本參考版本時的基準為：

- 104 筆內建活動資料。
- 95 個唯一內建活動名稱／分類。
- 203 個必要繁中資源鍵。
- 官方 Widget v5.2 展開 104 個可顯示事件。
- 13 筆核心世界王獎勵可與官方 Widget 匹配。

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

來源 `manifest.json` 保留上游基礎版號 `X.Y.Z`。`-PackageVersion X.Y.Z-fork.N` 只改寫封裝內 manifest，並為穩定版定義 `RELEASE_BUILD`；BHM 不得包含主程式的 `Blish HUD.exe` 或 `SemVer.dll`。腳本同時驗證最低需求 `bh.blishhud >=1.0.0`、fork 專案網址與封裝版號。

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

GitHub Pages 使用 fork 的 `master:/docs`，網址為 `https://gw.jakeuj.com/`。只有使用者要求更新網站時才修改與部署。功能或 coverage 有重大變更時，更新 `docs/index.html` 文案與統計；下載按鈕使用不綁版號的：

```text
https://github.com/jakeuj/Community-Module-Pack/releases/latest/download/Events.Module.bhm
```

推送後等待 Pages build 指向本次 commit 且狀態為 `built`，再從正式網域驗證新內容與下載連結。

## 常見問題

### 畫面仍有英文

先跑 coverage，再確認該事件實際採用內建 template key 還是官方 segment key。新官方-only key 要同時加入 neutral 與 zh resx；只翻舊 `events.json` 不足以涵蓋 live Widget。

### 座標或時間跟官方 Wiki 不同

查看模組設定的資料來源狀態、Widget 版本、revision 與 SHA1。確認目前不是 last-known-good cache 或 bundled fallback；不要用中文第三方網站的時間／座標覆寫官方 Widget。

### HTTP 403

確認請求送到 `https://wiki.guildwars2.com/api.php`，並帶有非預設、可辨識的 User-Agent。不要把語系選擇實作成切換 Wiki 或 API host。

### 顯示 task was canceled

區分模組卸載 cancellation 與 15 秒 HTTP timeout。timeout 應回退快取／內建資料並顯示本地化狀態，不直接把例外文字放到 UI。

### 沒有 waypoint 按鈕

先看官方 segment 是否有合法 chatlink。官方未提供時屬預期行為，不可從模糊名稱猜 waypoint。

### 找不到 .NET Framework 4.7.2 參考組件

使用 NuGet `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 的 `build/` 作為 `TargetFrameworkRootPath`。不要只設 `FrameworkPathOverride`，否則可能漏掉 `Facades/netstandard.dll`。技能內測試與建置腳本會自動處理。

### 沒有產生 BHM

先從 `Community Module Pack.sln` 還原 `packages.config`，確認 `packages/BlishHUD.1.0.0/build/BlishHUD.targets` 與 `packages/SemanticVersioning.1.2.2` 存在。舊式 csproj 單獨 restore 可能因缺少 `SolutionDir` 失敗。

### 替換後仍看到舊內容

先執行安裝腳本的 `-CheckOnly`，比較建置與安裝檔的完整路徑、大小、最後修改時間、SHA-256 與 `ref/textures/events/` 項目。雜湊不同代表只完成建置、尚未安裝；安裝檔沒有事件圖示則代表仍是舊套件。完全結束 Blish HUD 後使用安全安裝腳本替換，再重新啟動並確認雜湊相同。不要在程序仍載入 DLL/BHM 時覆寫，也不要只靠檔名判斷版本。

### 發布後沒有自動更新

先確認使用者目前版本本身已包含更新器、Blish HUD 至少為 1.0.0，且設定中的「自動更新此模組」已開啟。再檢查最新 Release 是否為非 draft／非 prerelease、tag 精確符合 `events-zh-tw-vX.Y.Z-fork.N`、恰有一個 `Events.Module.bhm`，且 GitHub API 已回報 `sha256:<64 hex>` digest。解壓縮模組、一般開發建置、`-test` BHM，以及由 `--module`／`-M` 載入的檔案只允許檢查或顯示狀態，不會自行替換。

檢查或下載失敗時不應重啟。查看模組設定的更新狀態與最新 `blishhud.*.log`，區分 HTTP／JSON／tag／資產／digest 驗證失敗；不要改成跳過 SHA-256，亦不要以 `ReplacePackage` 重試。若必須備援，完全結束 Blish HUD 後手動下載最新 `Events.Module.bhm`，再使用安全安裝流程。
