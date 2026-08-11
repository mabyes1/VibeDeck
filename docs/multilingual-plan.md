# VibeDeck 多國語系導入計畫（第一階段：English / 日本語）

## 目標

將目前的繁體中文瀏覽器／PWA 介面整理成可擴充的多國語系架構，第一階段提供：

- 繁體中文 `zh-Hant`：維持現有內容與預設行為。
- English `en`。
- 日本語 `ja`。

語系切換必須涵蓋 PC Host 頁面、手機瀏覽器／PWA、電子紙版面、離線頁與無障礙文字；不改變配對、串流、額度、版面保存或 API 資料格式。

## 現況與範圍

目前沒有前端 i18n 層，顯示文字分散在：

- 靜態頁面：`src/VibeDeck.Host/wwwroot/index.html`、`offline.html`、`manifest.json`。
- 主流程與狀態訊息：`wwwroot/index.js`。
- 動態模組：`modules/activity-feed.js`、`custom-cards.js`、`dashboard-layout.js`、`formatters.js`、`mobile-overview.js`、`quota-controller.js`、`quota-formatters.js`、`quota-mini-card.js`、`sideboard.js`、`stream-controller.js`。
- CSS 產生內容：`index.css` 中的 `content` 文案。
- 部分 API 回應：配對、登入、虛擬螢幕安裝、版面驗證、Windows 通知與 Deck 視窗流程的 C# 訊息。

第一階段納入所有使用者可見的 Web/PWA 文字。使用者自行輸入的自訂來源名稱、卡片標題、通知內容、主機名稱、裝置型號與第三方錯誤內容不翻譯，原樣保留。

## 語系選擇規則

使用 BCP 47 語系代碼，並固定以下優先順序：

1. URL `?lang=en|ja|zh-Hant`（選擇後寫入本機偏好）。
2. `localStorage` 的 `vibedeckLocale`。
3. `navigator.languages`／`navigator.language`：`en-*` → `en`、`ja-*` → `ja`、`zh-TW`／`zh-HK`／`zh-Hant` → `zh-Hant`。
4. 其他語系回退到 `zh-Hant`。

在 Header 的 utility controls 提供語系選單，選擇後不整頁跳轉，重新套用目前頁面與動態狀態文字。同步更新 `document.documentElement.lang`；語系切換不得清除配對、Wake Lock、e-ink 或版面偏好。

## 建議技術方案

### 1. 靜態 catalog + 輕量 runtime

新增 `src/VibeDeck.Host/wwwroot/locales/`：

```text
locales/
  zh-Hant.json   # 來源語系，先把現有繁中收斂到這裡
  en.json
  ja.json
```

以穩定 key 對應文字，不以繁中文字串作為 key。runtime 建議放在 `wwwroot/modules/i18n.js`，提供：

- `initLocale()`：解析 URL／本機／瀏覽器偏好並載入 catalog。
- `t(key, values)`：處理 `{name}`、`{count}` 等插值。
- `setLocale(locale)`：保存偏好、更新 `lang`、重新渲染頁面。
- `applyTranslations(root)`：套用 `data-i18n`、`data-i18n-attr` 到文字、`aria-*`、`title`、`alt`、`placeholder`。
- `tPlural(key, count)`：至少支援 English 的 `one`／`other`；日文與繁中可共用同一分支。

catalog 必須維持 key parity；後續新增語系只需新增 JSON，不需改動功能模組。禁止把來自 catalog 的 HTML 直接塞入 `innerHTML`；目前含 `<strong>` 的提示改成 DOM 節點或安全 token renderer。

### 2. 靜態 HTML 採 declarative key

將 `index.html` 的可見文案改成 `data-i18n="..."`，屬性則使用 `data-i18n-attr="aria-label:...;title:..."`。`index.js` 和各模組的動態訊息一律呼叫 `t()`，不再新增裸露的中文 fallback。

以下區域必須列入第一輪 catalog：

- Host 登入、HTTPS 憑證與手機配對流程。
- 顯示器、虛擬螢幕安裝／修復、旋轉、方向、串流與畫質設定。
- 資訊板、電子紙版面、卡片編輯器、活動動態與 Windows 通知。
- AI 額度、AGY／Codex 操作、錯誤／載入／空狀態。
- 手機總覽、詳細資訊、篩選器、按鈕、確認對話框與所有 ARIA 文案。
- `offline.html` 及 CSS `content` 產生的提示。

### 3. 格式化與資料邊界

`modules/formatters.js` 改為接收目前 locale：

- 天氣狀態名稱與 `WeatherLocation` 的固定詞（Weather、Current location、Taiwan、District）放入 catalog。
- 時間、日期、數字與複數使用 `Intl.NumberFormat`、`Intl.DateTimeFormat`、`Intl.RelativeTimeFormat`。
- 單位與協定名（GB、Mbps、FPS、Q、WebRTC、JPEG、H.264、HTTPS）維持產品術語，不翻譯數值格式之外的協定名。

儀表板保存資料只使用既有 stable key（例如 `system-load`、`quota-mini`）；`data-dashboard-title` 不得成為持久化資料。畫面標題由 `dashboard.title.<key>` 依 locale 產生，確保切換語系後同一份版面仍可使用。

### 4. API 錯誤與狀態訊息

不要依賴 API 回傳的中文 `message` 作為翻譯來源。第一階段在使用者可見的 API 回應補上穩定 `code`／`state`，保留既有 `message` 作為相容與除錯 fallback：

- 登入／配對：`auth.*`、`pairing.*`。
- 顯示器與安裝：`display.*`。
- 版面與自訂來源：`dashboard.*`、`customSource.*`。
- 額度與 Windows 通知：`quota.*`、`windowsNotification.*`。
- Deck 視窗與一般連線：`deck.*`、`connection.*`。

前端以 `code` 查 catalog；未知 code 或第三方／作業系統例外才顯示後端 `message`。這樣不會把內部錯誤字串或使用者自訂資料誤當成可翻譯文案，也不會破壞現有 API 呼叫端。

## 分階段工作

### Phase 0：字串盤點與規格凍結

- 建立 key 命名規則、術語表與英／日文翻譯 glossary。
- 從上述 HTML、JS、CSS、C# 清單建立完整 source inventory。
- 決定 `zh-Hant` 為 canonical source，明確標註不可翻譯的產品／協定名稱。
- 定義 API `code`／`state` 對照表與未知錯誤 fallback。

完成條件：每一個使用者可見字串都有 owner、key、source 檔案與翻譯狀態。

### Phase 1：前端 runtime 與繁中收斂

- 新增 `i18n.js` 與三份 catalog 骨架。
- 加入語系 selector、URL／本機／瀏覽器選擇規則。
- 先把 `index.html`、`offline.html`、`manifest` 可本地化欄位與 CSS 文案改成 key。
- 將 `index.js` 及各模組的裸字串改為 `t()`；初始化時先載入 locale 再 render。
- 抽離 formatter 的 weather／時間／數字 locale 行為。

完成條件：只載入 `zh-Hant` 時，畫面與目前版本的功能及文案等價；重新整理、PWA 啟動與 e-ink 模式都能保留語系。

### Phase 2：English / 日本語翻譯與 API 邊界

- 完成 `en.json`、`ja.json`，由 glossary 統一術語（Display、Sideboard、Pairing、Quota、Virtual Display 等）。
- 在 Host API 回應加入 `code`／`state`，前端將已知錯誤／狀態轉成語系文字。
- 檢查登入、配對、安裝、WebRTC fallback、額度、Windows 通知、Deck 視窗與自訂來源所有成功／失敗／空狀態。
- 對 plural、插值、長字串、換行與日文全形標點做人工校對。

完成條件：`?lang=en` 與 `?lang=ja` 從冷啟動開始不出現繁中文字串（使用者輸入／未知外部錯誤除外），API 錯誤不因語系切換而改變 status 或資料格式。

### Phase 3：PWA、打包與驗收

- 為 PWA metadata 決定實作方式：優先由 Host 依 `lang` 提供 localized manifest；若先不改 API，至少讓 `document.lang`、頁面 title 與 offline 頁正確，並保留品牌名 `VibeDeck`。
- 確認 Windows 安裝器、通知 Companion 的產品名稱與必要提示；OS／安裝器原生 UI 可另列後續工作，不阻擋 Web/PWA 首次發布。
- 加入 catalog key parity／缺漏檢查與 JavaScript syntax check。
- 依既有產品流程執行 `scripts/test-product-flow.ps1 -Installed`，再做瀏覽器矩陣驗收。

## 測試與驗收矩陣

### 自動檢查

- `en.json`、`ja.json`、`zh-Hant.json` key 集合完全一致；缺漏 key 讓 CI 失敗。
- `node --check` 檢查 `index.js`、`modules/*.js` 與 i18n runtime。
- 既有 .NET xUnit 測試全部通過；新增 API `code`／`state` 的序列化／相容性測試。
- 以 stub catalog 測試插值、plural、未知 key fallback、locale normalization。

### 手動／瀏覽器驗收

- Chrome／Safari：`zh-Hant`、`en`、`ja` 冷啟動、重新整理、PWA standalone 啟動。
- PC console、手機已配對、未配對、遠端登入、e-ink landscape／portrait。
- 顯示器不存在／安裝中／需要重開機、WebRTC 成功／fallback、API 離線與重新連線。
- 額度空資料／多帳號、活動篩選、自訂卡片、版面編輯與恢復預設。
- 英文長字串、日文字寬、按鈕換行、ARIA label、`lang` 屬性與鍵盤操作。
- 既有 `%ProgramData%\VibeDeck` 配對與版面資料在切換語系、更新與重新啟動後不變。

## 風險與決策

- **字串分散且動態路徑多**：先以 inventory 和 key parity 封住漏翻，再逐模組替換，避免一次改寫造成流程回歸。
- **API 直接回傳中文**：以 `code`／`state` 做長期邊界，未知訊息保留 fallback；不把每個 Windows／第三方例外硬編成三語。
- **PWA manifest 是安裝時讀取**：manifest 的語系 metadata 需在 Host 端依 locale 回應，不能只靠頁面切換後改 DOM。
- **版面資料不能綁語言**：只保存 stable key 與幾何資料，標題由目前語系即時產生。
- **翻譯品質**：English 先採產品技術語氣，日文採簡潔 UI 敬體／中性表達；所有配對與安全提示由人工覆核，不使用未審核的機器直譯直接發布。

## 首次發布的 Definition of Done

1. 預設 `zh-Hant` 行為與現有產品相容。
2. English／日本語可從 URL、瀏覽器偏好與 Header selector 選擇，且選擇可持久化。
3. Web/PWA 使用者可見文字、狀態、錯誤、ARIA 與 offline 頁均有對應翻譯或明確 fallback。
4. API 資料格式、配對 token、版面資料與產品安裝資料不受語系影響。
5. 三份 catalog 通過 key parity，Node／.NET 測試與既有 product flow 通過。
6. Chrome／Safari、PC／手機／BOOX 三種主要路徑完成英／日文人工驗收。
