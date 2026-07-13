# Events Module 繁中化與建置參考

## 目錄

- [檔案與執行路徑](#檔案與執行路徑)
- [資源鍵規則](#資源鍵規則)
- [激戰-2-術語來源](#激戰-2-術語來源)
- [單一 DLL 中文建置](#單一-dll-中文建置)
- [驗證與目前基準](#驗證與目前基準)
- [CI/CD 與 Release](#cicd-與-release)
- [常見問題](#常見問題)

## 檔案與執行路徑

| 用途 | 路徑 |
|---|---|
| 英文中性資源 | `Events Module/Properties/Resources.resx` |
| 繁中資源 | `Events Module/Properties/Resources.zh.resx` |
| 資源存取器 | `Events Module/Properties/Resources.Designer.cs` |
| 活動資料 | `Events Module/ref/events.json` |
| 模組專案 | `Events Module/Events Module.csproj` |
| 套件資訊 | `Events Module/manifest.json` |
| CI/Release | `.github/workflows/events-module-zh-tw.yml` |

`EventsModule.cs` 以 `Resources.ResourceManager.GetString(meta.Name) ?? meta.Name` 顯示名稱，分類也採相同模式。因此任何 event `name` 或 `category` 缺少完全相同的資源鍵時，畫面會直接回退成英文。

## 資源鍵規則

繁中資源必須涵蓋以下集合：

1. `Resources.resx` 的全部 UI 鍵。
2. `events.json` 中所有唯一的 `name`。
3. `events.json` 中所有唯一的 `category`。

只翻譯 `<value>`，不要翻譯 `<data name="...">`。即使來源含拼字錯誤也保留鍵，例如 `Moredremoth Invasion: Kessex Hils`；可在中文 value 修正顯示文字，但改動 key 會導致查找失敗。

保留 `{0}` 等格式化佔位符。`Wiki`、`events.json`、URL、檔名與產品名稱可保留；若目標是全中文事件清單，事件名稱和分類的 value 不得殘留英文字母。

不要手動改 `Resources.Designer.cs`。新增 `.resx` 鍵不需要新的強型別 property，因為活動名稱透過 `ResourceManager.GetString` 動態查找。

## 激戰 2 術語來源

翻譯事件名稱時依下列順序確認，不以英文逐字直譯作為定稿：

1. [星岬島完整事件時間表](https://gw2.wishingstarmoye.com/gw2timer)：主要來源，涵蓋大型事件、階段、地圖、匯聚與新版資料片。頁面內容由 JavaScript 動態產生，應讀取頁面當下引用的 `gw2timer_new_data_*.js`，不要固定使用某個日期版本的檔名。
2. [星岬島 BOSS 計時器](https://gw2.wishingstarmoye.com/gw2timerbox)：交叉確認核心世界王名稱；資料位於頁面引用的 `timerbox_bossdata.js`。
3. Guild Wars 2 API：用 `lang=zh` 查地圖與其他可取得的專有名詞，再轉為台灣繁體。
4. 星岬島資料庫與攻略頁：確認計時器沒列出的成就分類、地名、頭目及玩家慣稱。

轉換原則：

- 保留已建立的遊戲名稱，例如 `Ley-Line Anomaly` 使用「魔徑異常體」，不可另造「地脈異常體」。
- 對玩家通常以頭目或階段稱呼的事件，優先使用辨識度高的名稱，例如「吞噬托」、「四門（八爪藤）」、「翠玉之海之戰（淑雯）」。
- 簡體來源須人工轉成台灣繁體，並檢查異體字與地名，例如「翠玉」、「蒼翠邊界」、「納約斯外層」。
- `Group Event`、`Meta Event` 等介面詞彙採遊戲語境的「團隊事件」、「大型事件」，不要泛化成「活動」。
- 來源互相衝突時，優先採完整事件時間表目前顯示的名稱；必要時在括號補上另一個玩家常用稱呼，而不是自行音譯。

## 單一 DLL 中文建置

一般建置同時產生英文中性資源與 `zh` 衛星資源。專案的 `ChineseBuild` 條件則把 `Resources.zh.resx` 以 `Events_Module.Properties.Resources.resources` 嵌入主 DLL：

```powershell
/p:ChineseBuild=true
```

這是「只替換 DLL 也要顯示中文」的必要條件。正式安裝仍優先提供 BHM，因為 BHM 還包含 `manifest.json`、相依 DLL、`ref/events.json` 與 textures。

使用 repo 腳本建置：

```powershell
& .agents\skills\localize-build-events-module\scripts\build-events-zh-tw.ps1
```

預設輸出：

```text
artifacts/Events-and-Metas-Observer-zh-TW/Events Module.dll
artifacts/Events-and-Metas-Observer-zh-TW/Events Module.bhm
```

## 驗證與目前基準

執行：

```powershell
& .agents\skills\localize-build-events-module\scripts\validate-events-localization.ps1
```

腳本動態檢查缺漏、重複鍵、事件 value 英文殘留及 `{0}` 佔位符。建立本技能時的 repo 基準為：

- 104 筆活動資料。
- 95 個唯一活動名稱／分類。
- 114 個必要繁中資源鍵。

活動資料增加後，以腳本動態結果為準，並同步調整 CI 中硬編碼的最低資源數量檢查。

## CI/CD 與 Release

`.github/workflows/events-module-zh-tw.yml` 在以下情況執行：

- `master` 或 PR 變更 `Events Module/**`：驗證並建置 workflow artifact。
- 推送 `events-zh-tw-v*` 標籤：驗證、建置並建立 GitHub Release。

只有使用者明確要求發布時才建立標籤。發布前先讓 `master` CI 通過，再推送例如：

```text
events-zh-tw-v1.0.9
```

GitHub 可能把 Release 資產檔名中的空格正規化為點號，例如 `Events.Module.bhm`；副檔名與內容不受影響。

## 常見問題

### 畫面仍有英文

先執行 coverage 腳本。通常是 `events.json` 新增 `name` 或 `category`，但 `Resources.zh.resx` 尚未加入完全相同的 key。不要只看舊的中性資源檔。

### 找不到 .NET Framework 4.7.2 參考組件

建置腳本會使用 NuGet 的 `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3。手動建置時應傳入該套件的 `build/` 作為 `TargetFrameworkRootPath`。

### 缺少 netstandard 參考

不要只使用 `FrameworkPathOverride`；它可能漏掉 `Facades/netstandard.dll`。使用完整 `TargetFrameworkRootPath`，讓 MSBuild 解析 targeting pack 與 Facades。

### 只產生衛星資源 DLL

確認 MSBuild 參數包含 `/p:ChineseBuild=true`。一般本地化建置的 `zh/Events Module.resources.dll` 不能取代單一中文主 DLL。

### 沒有產生 BHM

先從 `Community Module Pack.sln` 還原 `packages.config`，確認 `packages/BlishHUD.0.11.0/build/BlishHUD.targets` 存在。這個舊式專案若直接對 csproj 執行 MSBuild restore，NuGet 會因缺少 `SolutionDir` 失敗。Blish HUD build targets 負責組合 BHM。

### 替換後仍看到舊內容

完全結束 Blish HUD，再替換 Modules 目錄內的 BHM 或已解壓 DLL；重新啟動後確認啟用的是新版本。
