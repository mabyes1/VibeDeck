# 更新日誌

本檔記錄每個可發佈版本的使用者可見變更、修正與產品化調整；後續改版須在打包前補入對應版本。

## 0.1.37 - 2026-07-22

- 修正手機首次連線實體螢幕時偶發黑畫面：DXGI 尚未取得新畫面且 bitmap 尚未初始化時，會先以 GDI 擷取目前桌面，不再送出全黑首幀。
- 移除高耗能的 Canvas 導光波浪動畫，保留純 CSS 背景，降低遠端桌面與低 GPU 裝置的負載。
- 將 Host 的顯示器、系統、導覽資產、資訊板、額度與串流 endpoint 拆分為獨立 partial class，路由與授權規則維持不變。
- 將前端裝置憑證、client instance、環境與電子紙偵測抽成獨立 JavaScript module；大型樣式表依既有 cascade 順序拆為六個 CSS partial，方便後續逐步整理。
- 新增 DeviceTrustService 配對狀態機測試，涵蓋允許、拒絕、輪詢、撤銷、清除與 token 信任流程。

## 0.1.36 - 2026-07-20

- 修正 PC Web 整頁無法載入：移除 `index.js` 重複宣告的 `displayView`，以及檔案中段的 BOM，避免 `SyntaxError` 讓整個前端掛掉。
- 顯示器工具列改為點 ⚙ 齒輪展開／收合（選螢幕＋鍵盤），取消易失效的 idle 三秒隱藏；收合時半透明小齒輪，不再用「畫面」兩字。
- Android 手機顯示器全螢幕改走與 iOS 相同的 CSS immersive 路徑，避免瀏覽器 Fullscreen API 與 CSS 強制橫向互搶造成跑版。
- 修正 Android 橫向資訊板／額度：Fullscreen 不再被拉成整條寬按鈕，header 改為「分頁 + 語言／全螢幕」單列，內容拉滿邏輯橫向寬度。
- WebRTC H.264：硬體編碼（NVENC）若因驅動／FFmpeg client-key 不相容而秒退，自動 fallback 到 libx264，避免 ICE 連上後立刻 `H.264 stream failed`。

## 0.1.35 - 2026-07-20

- 修正 0.1.34 在 PC Web 端整頁掛掉：`revealDisplayToolbar` 在 `displayInputController` 宣告前就被 `loadPhoneDisplay` 呼叫，觸發 TDZ `ReferenceError` 讓 `index.js` 中斷；控制器改為檔案開頭先宣告，並修正雙擊全螢幕的空引用。
- 修正顯示器工具列「約 3 秒隱藏」無效：鍵盤按鈕曾用 `hold` 清掉 idle timer 且不再排程，點過一次鍵盤後工具列會永遠卡住；改為僅在鍵盤／畫面選單真正 focused 時延後隱藏，否則固定重新計時淡出。
- 工具列自動隱藏僅用於手機／全螢幕 viewer；本機 PC 主控台（`pc-console`）保持常駐。

## 0.1.34 - 2026-07-20

- 顯示器模式虛擬鍵盤可正常收合：點串流畫面、旋轉螢幕時會 blur 隱藏輸入框；viewport 加上 `interactive-widget=resizes-content`，減少軟鍵盤與版面脫節。
- 顯示器頂部工具列（畫面來源／鍵盤）閒置約 2.6 秒自動淡出，觸控或滑鼠移動再顯示；鍵盤開啟或下拉選單操作中維持可見。
- 全螢幕串流改為 `object-fit: contain`（含 iOS 覆寫，不再 `fill`/`cover` 裁切），避免全螢幕時裁掉 Windows 畫面邊緣。

## 0.1.33 - 2026-07-20

- 修正 WebRTC 協商偶發失敗：Host 在 ICE gathering 提早完成時對尚未結束的 timeout Task 呼叫 `Dispose()`，會讓 `/api/stream/webrtc/offer` 回 500 並把畫面打回 JPEG；改為 `Task.WhenAny` 等待完成或逾時，不再 Dispose 未完成的 Task。

## 0.1.32 - 2026-07-20

- 遠端顯示串流改為分層傳輸：先嘗試直連 WebRTC，網路封鎖直連 UDP 時可自動改走 Cloudflare TURN 中繼，仍不通則回退 JPEG，讓行動網路（CGNAT）或跨網路情境下的畫面與遠端控制維持穩定。
- 修正遠端連線在 WebRTC 斷線時 JPEG 一抖就重新協商 WebRTC、造成畫面反覆重連的循環：WebRTC 斷線改為保留連線並在數秒後嘗試 ICE restart，失敗才切 JPEG 並進入冷卻期，JPEG 重連改為獨立指數退避，不再互相拉扯。
- 顯示器設定新增串流傳輸模式（自動／偏好 WebRTC／穩定 JPEG），並提供本機 TURN 診斷與連線狀態顯示。
- 新增可選的 Cloudflare TURN 設定：Key ID 與 API Token 僅在本機主控台設定，長效 API Token 以 Windows DPAPI 加密保存、不離開 Host，已配對瀏覽器只會取得短效 ICE 憑證。未設定時維持 STUN 直連與 JPEG 備援。

## 0.1.31 - 2026-07-20

- 顯示器頁籤新增 Windows 畫面來源切換：已配對裝置可在 VibeDeck 延伸螢幕、主螢幕與其他實體螢幕間切換，選擇會保存在該瀏覽器；實體螢幕沿用 DXGI 擷取、WebRTC H.264／JPEG fallback 與既有觸控滑鼠控制。手機端新增軟鍵盤入口，以 Windows Unicode `SendInput` 傳送 Android／iOS 輸入法完成後的文字，並支援 Enter、Backspace、方向鍵、Delete、Tab、功能鍵與 Ctrl／Alt／Shift／Meta 組合鍵。
- 修正 Windows Setup 在設定防火牆時可能跳出 `netsh.exe` 0xc0000142 應用程式錯誤並卡住安裝；安裝器改以 Windows Firewall COM API 建立／更新／移除規則，不再啟動 `netsh.exe`，失敗時只寫入 Setup log 而不阻塞產品安裝。開發用安裝與移除腳本也改用 NetSecurity Cmdlet。
- 修正一般手機直向版面誤將顯示器與額度共用的「全螢幕」按鈕隱藏；已配對的 Android／iPhone 現在不分直橫向與目前頁籤都保留全螢幕入口。
- 手機（iPhone／Android／PWA）資訊板恢復使用原本的「命令／儀表／專注」靜態皮膚；導光波浪只保留給 PC 主控台與 Deck 虛擬螢幕。手機不再建立波浪 Canvas、光柵節點或動畫監聽器，長亮功能維持不變，降低長時間看板時的 GPU 耗電與發熱。

## 0.1.30 - 2026-07-19

- Windows Setup 內建經 SHA-256 驗證的 Cloudflare connector；VibeDeck Host 會在登入桌面工作階段隱藏啟動、健康檢查、失敗重試並在結束時一併關閉，不再建立獨立排程、顯示命令視窗或要求使用者安裝／操作 cloudflared。新安裝會由 `vibedeck.pp.ua` Worker 自動建立每台 PC 專屬 Tunnel、ingress 與 DNS；Cloudflare API token 僅存在 Worker secret，PC 只以 DPAPI 保存自己的 Tunnel token。舊版 `VibeDeck Cloudflare Connector` 排程會自動停止並移除，既有本機 Tunnel 設定可無痛接管。
- 安全網址狀態新增自動配發、背景連線、已上線與區網 HTTPS 回退；一般安裝不再顯示手動網址欄位。一次性連線碼註冊／解析與新安裝配發均加入每 IP 節流，降低免費 Cloudflare Durable Object、Worker 與 Tunnel 額度被濫用的風險。
- 修正背景 Tunnel 啟動後同步等待空白輸出而卡住，導致狀態永遠停在等待、錯誤回退區網網址並讓既有手機誤以為需要重新配對的問題；Hosted Service 與連線資訊也明確共用同一實例。
- iOS／Android 直接開啟資訊板時啟用與 PC／Deck 相同的本機 Canvas 導光波浪；觸控滑動會驅動方向，閒置時持續呼吸。共通手機規則不依賴特定型號，電子紙與系統「減少動態效果」仍停用動畫。
- 手機共通控制項最小高度統一為 36px，修正 Device Lab 在 iPhone XS 與 Galaxy S23 回報的六個 33–34px 操作控制；型號只作代表性 viewport 驗證，不作產品版面分支。
- 修正 iOS 橫向全螢幕額度頁保留先前直向捲動位置、開啟時內容落在畫面外的問題；額度內容改由可用視窗高度捲動，卡片依內容展開，不再互相擠壓。使用者版面同時隱藏僅供診斷使用的資料來源說明卡，Device Lab 會偵測額度卡裁切或過窄。
- 修正動態資訊模組載入不同版本的 i18n module 而各自持有未初始化語系狀態；額度週期、資訊板、串流與自訂卡等動態文案現在共用同一份繁中／English／日本語 catalog，不再顯示 `ui.…` 原始 key 或殘留繁中。
- 修正同時讀取額度時競爭覆寫 Codex quota cache 的檔案鎖定；快取讀寫改為同程序同步，避免手機／桌機同步刷新時 `/api/quotas` 偶發失敗。
- 手機額度週期標籤最低字級固定為 12px，讓 iPhone XS 與 Galaxy S23 橫向 Device Lab 可讀性驗收不再因 11.77px 文字失敗。

## 0.1.28 - 2026-07-19

- 修正 iPhone 沉浸式資訊板的安全區被手機版 `padding: 0` 覆蓋：直向保留瀏海頂部與 Home Indicator，橫向保留左右瀏海邊界，避免左上控制與內容被裁切。
- 移除 Deck 視窗與手機顯示串流觀眾數的錯誤耦合：手動開到虛擬螢幕的資訊板不再因 12 秒內沒有遠端串流、或手機串流短暫斷線而被自動搬回主螢幕；既有 `/api/deck/return` 明確召回能力保留。
- 修正「在虛擬螢幕顯示資訊板」會與手機配對救援畫面重疊：Deck 視窗現在只允許由本機 loopback 啟動，並明確隱藏配對層；保留既有虛擬螢幕定位與全螢幕開啟功能。
- 一次性連線碼改為所有裝置共用，不再強制加入 `eink=1` 或把手機誤判為 BOOX；配對名稱優先採用瀏覽器回報的真實型號，未知電子紙／Android 裝置只顯示通用名稱，不再假冒 `BOOX Go Color 7`。電子紙仍會依 BOOX 硬體、既有偏好或手動電子紙模式套用專用版面，三語系入口與 PC 操作文字同步泛化。
- 完成首次使用 UI／UX 收斂：零配對 PC 會自動進入裝置設定並展開 QR；移除重複的開始配對按鈕與假百分比，手機只保留單一連線動作；待核准裝置、六位數碼與允許／拒絕改在 QR 同一卡顯示，安全網址啟用時保持進階連線資訊收合。已配對裝置改以最後使用時間為主，IP 收入提示，清空全部配對移至危險操作選單；PC header 隱藏不相關的電子紙、長亮與全螢幕控制。
- Device Lab 新增已配對／未配對／等待核准／零裝置本機四種狀態，以及手機 12px／36px、電子紙 16px／44px 的最小字級與點按高度驗收。手機 Quota 的帳號操作改為可收合管理區，並修正 S23、iPhone XS、BOOX 三語系配對與額度頁的小字／小按鈕；14 組高風險 viewport、可讀性與水平邊界回歸皆通過。
- 新增結構債與 UI／UX review 文件：依耦合度與修改難度排序 Startup、前端狀態、CSS cascade、配對信任、i18n、Quota 與 Custom Sources，列出重構前測試護欄、分批路線與不可破壞條件；另以實際 Host 與 Device Lab 盤點首次配對、PC 核准位置、未配對手機動作、進階連線資訊與多裝置版面，作為後續產品化驗收規格。
- 修正電子紙連線碼的 Host 依賴注入歧義：正式執行時改為唯一的服務建構子，避免 PC 已顯示安全網址但無法向 Cloudflare 建立一次性連線碼。
- 電子紙連線入口同步支援 `HEAD` 預檢，避免部分 Chromium／電子紙瀏覽器在輸入短網址時誤判入口不存在。
- 電子紙連線碼表單首次送出後會立即鎖定提交按鈕，避免電子紙慢刷新下重複送出而把已成功使用的碼誤顯示為過期。
- 電子紙輸入有效連線碼後會自動建立 PC 端待核准申請，移除電子紙第二次點擊；PC 仍須核對驗證碼後明確允許或拒絕。
- 電子紙以新的連線碼重試時會取代裝置端殘留的已逾時待核准資料，確保每次有效輸入都立即送達 PC 的核准清單。
- BOOX Chromium 對跨網域表單 `303` 導頁的相容性改為安全中繼頁：連線碼解析後自動跳往該台 PC，並保留單一備援連結，避免代碼已使用但畫面仍留在輸入頁。
- 新增電子紙連線碼流程：PC 可建立 10 分鐘、一次性的八位代碼；電子紙在 `https://vibedeck.pp.ua/` 輸入後才會前往該台 PC 的安全網址，仍必須完成既有 PC 端配對允許。Cloudflare Durable Object 以強一致方式處理代碼，並在解析後立即刪除。

- 補齊 OpenAI Build Week 送件包：根目錄 README 新增評審快速啟動、可驗證的 Codex + GPT-5.6 證據表與產品差異化；新增 `docs/build-week-submission.md`，收錄 Work & Productivity 的可直接貼用英文文案、資產清單與送件檢查表。
- 補齊 Build Week 評審證據：圖文介紹書新增第 `08 / 08` 頁「Build Notes」，具體說明 Codex + GPT-5.6 在規劃、實作、除錯與實機驗收的協作方式，以及模型分工與人類最終品質責任；另新增 V9 影片母檔，於 V8 後以旁白清楚交代這些工具如何參與產品化。
- 修正 V8 圖文介紹書的雙欄安全距離：縮短跨欄標題與副標、限制標題寬度，避免文字與右側產品截圖相互壓住。
- 新增 OpenAI Build Week 可自由附檔的 V8 圖文介紹書：以當前 Host 與 Device Lab 的隱私遮罩實拍畫面製作 8 頁 A4 橫式英／繁中 PDF，涵蓋產品定位、Display／Sideboard／Quota、受信任網址配對、三裝置版面、電子紙限制與產品化流程；附可重建來源與逐頁渲染 QA。
- 完成 OpenAI Build Week Demo V8 素材與影片流程：以目前已安裝 Host、受信任網址配對、Display、Sideboard、Codex／AGY 額度、BOOX 固定版面、Device Lab、診斷軌跡與三語系實際畫面重錄；輸出 99.2 秒 1080p H.264／AAC 主檔、乾淨版、字幕、素材稽核與黑幀／音量 QA 報告。
- 新增僅限本機使用的「裝置版面測試」：直接載入真正 VibeDeck 頁面，可切換 BOOX Go Color 7、Galaxy S23、iPhone XS、橫直向、三語系與主要頁面；依 BOOX 實機 PWA viewport 與手機 CSS viewport 校正尺寸，並自動標示尺寸偏差或水平溢出。BOOX 資訊板／額度預覽會直接進入與實機相同的全螢幕面板，iPhone XS 預覽同步模擬安全區；開發時可先用測試器查跑版，再以實機做最終觸控與電子紙刷新驗收。
- 電子紙資訊板的活動動態篩選改為單一下拉選單，保留全部／任務／通知狀態同步，不再讓三顆直排按鈕擠壓固定高度卡片。
- 修復首次配對與已配對手機／BOOX 在資訊板、額度頁看不到語言選單；動態提示改為完整字串翻譯，避免 English／日本語出現半中半外文，並新增遺漏語系字串的回歸檢查。
- 電子紙額度頁改用文件垂直捲動；資訊板維持單屏固定 Grid，長通知只在卡片內捲動，避免卡片被內容撐成上萬像素。BOOX 瀏覽器保留工具列時仍可垂直捲動額度內容。
- 電子紙 Codex 額度重整為單一卡總覽；帳號切換、重新登入與刪除收納在同卡可展開管理區，避免全螢幕模式出現堆疊小卡與裁切。
- 新增 `scripts/sync-installed-webroot.ps1`，供開發時以系統管理員身分只同步前端檔案；正式發佈仍使用 Windows Setup。
- 靜態 HTML、JavaScript、CSS 與 JSON 回應明確宣告 UTF-8，避免 BOOX Chromium 以 Big5 解碼繁體中文；新增 `scripts/sync-installed-host.ps1` 供開發時快速部署 Host 修正。

## 0.1.27 - 2026-07-17

### 修正

- 修正 480px 電子書寬度下 English Codex「重新登入」操作文字仍可能超出按鈕；額度操作列改為優先分兩列，三語系皆不裁字。

## 0.1.26 - 2026-07-17

### 修正

- 電子紙／BOOX 版面新增最後生效的窄視窗安全層：長文字、標頭與操作列會換行或收縮，不再把整頁橫向撐出紙張視窗。
- 額度頁在電子紙裝置改為直向資訊流；Codex 帳號選單與 AGY／Codex 操作按鈕保留可讀文字與 48px 點按區，不再被舊有圖示尺寸壓壞。
- 資訊板的小尺寸持久化卡片採容器寬度調整數字、篩選器與標頭，避免不同電子書解析度下卡片內容溢出。

## 0.1.25 - 2026-07-17

### 額度與帳號

- AI 額度讀取重構為 Codex reader、AGY provider、帳號檔案庫與共用安全工具；已保留已安裝版的 `%ProgramData%\VibeDeck` 資料路徑、跨 Windows 使用者的 Codex 掃描與 ChatGPT Credits 解析。
- Codex 額度頁新增目前 Windows 使用者專屬的本機帳號備份、切換、重新登入與刪除控制列；切換前會明確說明影響，僅關閉目前工作階段的 `codex` CLI，絕不關閉 ChatGPT 桌面程式。
- 修正 AGY 的「開啟」行為：不再把 VibeDeck 的額度帳號誤當成 AGY 原生登入帳號，也不會關閉既有 AGY 工作階段；原生帳號切換應由 Cockpit Tools／Antigravity 自身協調完成。

### 多語系與可稽核性

- 繁體中文、English、日本語同步補齊 Codex／AGY 帳號操作、確認提示與錯誤訊息。
- Codex 切換、重新登入與帳號備份刪除會寫入可稽核診斷軌跡；帳號備份路徑不會回傳到手機端。

## 0.1.24 - 2026-07-17

### 修正

- 已安裝的 VibeDeck Host 會在登入使用者桌面工作階段啟動時自動修復 Windows 自啟登錄，避免更新後偶發遺失自啟而導致下次開機無法連線。

## 0.1.23 - 2026-07-17

### 修正

- 修正 PC「裝置設定」展開「連接新裝置」QR Code 時，CSS Grid 列高被壓縮而與後續設定重疊。
- 顯示器模式改為在顯示器已就緒後重新建立串流，避免首次載入虛擬螢幕時需手動按「重新整理」才出現畫面。
- 重新封裝並更新 Windows Notifications Companion 為 GUI 子系統，通知同步不再顯示空白主控台視窗；後續 MSIX 打包會檢查此條件。

## 0.1.22 - 2026-07-17

### 新增

- 每台 VibeDeck Host 會建立並持久化一組安裝代號，可對應唯一的 `https://<installation-id>.vibedeck.pp.ua` 安全網址。
- PC 的「進階連線資訊」新增安全網址狀態與首次 Tunnel 設定欄位；設定成功後 QR Code 與手機推薦網址會自動切換到受信任 HTTPS。

### 安全與配對

- 公開配對僅接受由本機 loopback Tunnel 轉送、完全符合此電腦安裝代號的 HTTPS 主機名；LAN 直接配對與 PC 上的六位數核准流程維持不變。
- 僅本機 PC 可設定或清除安全網址，且設定檔採原子寫入與備份復原；Cloudflare Token 不會寫入 VibeDeck 或傳到手機。
- Host 只信任 loopback Connector 的 `X-Forwarded-*` 標頭，讓 Tunnel 連線正確使用 HTTPS Cookie、稽核來源 IP 與配對安全限制。

### 介面與多語系

- 安全網址啟用後，iPhone、Android 與 BOOX 的配對引導改為「掃碼 → 連接這台電腦 → PC 核准」，不再要求接受危險網頁或安裝憑證。
- 繁體中文、English、日本語同步補齊安全網址、Tunnel 設定與配對提示。

## 0.1.21 - 2026-07-17

### 介面

- 強化資訊板棱鏡波浪的核心亮度與玻璃穿透感，確保在資訊卡層下仍能看見持續流動的發光曲線。
- 更新前端資源版本，避免已開啟頁面沿用舊版波浪樣式或動畫程式。

## 0.1.20 - 2026-07-17

### 介面

- Windows PC 資訊板主畫面改為純黑發光波浪舞台：24 條玻璃直柵產生棱鏡色散，波形持續呼吸並跟隨滑鼠移動方向。
- 手機、BOOX／電子書、窄視窗與「減少動態」偏好自動維持既有介面，避免影響效能與閱讀性。

## 0.1.19 - 2026-07-17

### 修正

- 修正清理舊 Service Worker 時遺失網址語系參數，確保第一次以 `?lang=en` 或 `?lang=ja` 開啟仍會顯示正確語言。

## 0.1.18 - 2026-07-17

### 產品化

- Windows Host 改為原生背景應用程式；桌面捷徑與開機自啟不再需要顯示或依賴 CMD／VBS 啟動器。
- 開機自啟改由目前登入的 Windows 使用者建立，避免安裝提升權限時把 Host 登錄到錯誤帳號。
- Setup 改為依 Windows 顯示語言自動選擇繁體中文、English 或日本語的單一安裝精靈，完成後自動建立捷徑、設定背景 Host 並可直接開啟 App。
- 本機 PC 新增「檢查更新」：只從固定 GitHub Release 下載同版 Setup 與 SHA-256，驗證後再顯示 Windows 安裝器；手機與遠端裝置無法觸發更新。
- 推送 `vX.Y.Z` tag 時，GitHub Actions 會自動打包 Setup、建立雜湊並發布 GitHub Release，日後更新不再需要使用者執行開發用腳本。

## 0.1.17 - 2026-07-17

### 修正

- 將 Credits 字級規則提升為所有版面共用的最小 `17px`，修正 `v0.1.16` 僅套用手機與電子書 class、PC 資訊版仍維持小字的遺漏。

## 0.1.16 - 2026-07-17

### 修正

- 將完整額度頁與資訊版 AI 額度小卡的 Credits 行，統一提升至電子書可讀性基準 `17px`；BOOX 模式改為高對比黑字。

## 0.1.15 - 2026-07-17

### 新增

- 資訊版的 AI 額度小卡在選取 Codex 來源時，同步顯示最新「剩餘 ChatGPT Credits」；非 Codex 或尚無餘額時自動隱藏該行。

## 0.1.14 - 2026-07-17

### 修正

- 修正額度格式化模組遺漏語系函式造成的執行期錯誤，確保 Codex 額度卡與新增的 Credits 餘額可正常渲染。

## 0.1.13 - 2026-07-17

### 新增

- Codex 額度卡會讀取本機最新 `credits.balance` 快照，顯示「剩餘 ChatGPT Credits」與無限制狀態；不讀取或保存登入憑證。

## 0.1.12 - 2026-07-17

### 修正

- AI 額度頁與資訊板會依來源回傳的真實時間窗顯示 `5 小時`、`1 週`、`30 天` 等週期，不再把所有 Codex 額度誤標為 `5h`；三種語系同步更新。

## 0.1.11 - 2026-07-17

### 修正

- 診斷面板改用明確的展開按鈕與一般容器，不再受瀏覽器原生摺疊元件的高度限制；大量事件可在面板內捲動。

## 0.1.10 - 2026-07-17

### 修正

- 將診斷面板的內部佈局改為可縮放 Flex 區域，確保事件清單使用剩餘空間並可實際捲動。

## 0.1.9 - 2026-07-17

### 修正

- 診斷事件清單改為明確限高的面板，避免大量紀錄把下方設定推離畫面。

## 0.1.8 - 2026-07-17

### 修正

- 「診斷軌跡」移至裝置設定頁底部，避免展開時先把常用裝置設定推離畫面。

### 維護

- 建立持續維護的版本更新日誌，讓每次發布可追溯其功能、修正與影響範圍。

## 0.1.7 - 2026-07-17

### 修正

- 診斷預設不再塞入正常的心跳與輪詢事件；仍保留所有失敗、警告與重要操作，必要時可透過 API 查閱完整例行紀錄。

## 0.1.6 - 2026-07-17

### 可稽核性與穩定性

- 新增 30 天可稽核診斷軌跡：每筆重要操作都有追蹤碼、結果、耗時與必要上下文；敏感資訊會遮蔽，並提供標記與複製摘要。
- 資訊板版面配置改為自動儲存至產品資料目錄；儲存檔毀損時會備份、復原並留下可稽核事件。

### 使用流程

- 已配對裝置置頂；「連接新裝置」改為可收合區塊，沒有已配對裝置時預設展開。
- 新增的配對、版面儲存與診斷介面完整提供繁體中文、English 與日本語。

## 0.1.2 - 2026-07-16

### 產品化品牌與路徑統一（不破壞既有配對）

- 使用者可見字串、錯誤訊息、憑證下載名改為 VibeDeck；內部協定仍雙讀舊 `PhoneMonitor` / `X-PhoneMonitor-*` header 與 cookie。
- `/health` 與 `/api/session` 回報產品版本；PC UI 顯示 `vX.Y.Z`。
- 虛擬螢幕安裝結果與通知 bridge-token 走 `AppPaths`（安裝態 `%ProgramData%\VibeDeck`，開發態 `%LocalAppData%\PhoneMonitor`）。
- Open-VibeDeck 改為輪詢 `/health` 就緒後再開瀏覽器，失敗時提示 log 位置。
- 憑證下載：`/cert/vibedeck-root.cer`（保留 `phone-monitor-*` 別名）。

## 0.1.1 - 2026-07-15

### 正式安裝／更新路線收斂

- 移除 Host Windows Service 架構；Host 改在登入使用者桌面 Session 背景自動啟動，修正 Session 0 無法枚舉及擷取虛擬顯示器。
- Setup 成為唯一 Windows 產品發佈與覆蓋更新路線；更新時清除可替換程式／Web 資產，保留 `%ProgramData%\VibeDeck`。
- 移除 portable ZIP 發佈腳本、Android 原生殼與 APK／Gradle／簽章工具，手機只保留 Safari／Chrome／PWA。
- 通知 companion 程序改名為 `VibeDeck.Notifications.exe`，避免再與主 Host 混淆。
- 通知 companion 可跨 Host 覆蓋更新自動重連，且啟動時只建立通知基準、不重播通知中心歷史。
- 新增 `scripts\test-product-flow.ps1`，統一驗證來源、payload 與已安裝產品的 Session、連接埠、遺留 Service 和虛擬顯示器狀態。
- 新增 `AGENTS.md` 工程護欄並重寫 README／release checklist，明確禁止回到淘汰路線。

## 2026-07-14（歷史；部分敘述已廢止）

### 電子書版自動辨識 + 手動切換

- BOOX NeoBrowser 常送出一般 Android Chrome UA（沒有 `BOOX`／`ONYX`），導致 `isEinkClient()` 失敗、停在一般手機版。
- 電子書判定改為：`?eink=1` → localStorage 偏好 → cookie 備援 → UA（BOOX／ONYX／VibeDeck-EInk）→ 面板解析度啟發式。
- 自動／手動進入電子書後會**黏住偏好**（localStorage + cookie），PWA 主畫面啟動（沒有 `?eink=1`）仍開紙面版。
- 頂部 **電子書** 按鈕；開啟時預設進資訊板；未配對時仍顯示配對區塊。
- **全螢幕**：資訊板／額度也會呼叫 Fullscreen API（不再只限顯示器模式），並用 `viewer-immersive` 吃滿 100dvh。
- 電子書版隱藏 **顯示／串流／解析度** 三組設定（無虛擬螢幕不需要）。
- 電子書資訊板改**固定單欄紙面堆疊**（不再套桌面三欄），避免卡片擠在一起、底部被裁切；數字與內文可完整顯示並可捲動。

### 產品安裝一條龍（Setup）

- 新增 Windows Setup 打包：`scripts\package-windows-setup.ps1` → `VibeDeck-Setup-<version>.exe`（Inno Setup）。
- ~~安裝後註冊 Windows Service~~ **已廢止（見 0.1.1）**：正式路線改為登入桌面 Session 背景啟動，禁止 Host Windows Service。
- 桌面／開始功能表 **VibeDeck** 圖示啟動 Host（若需要）並開啟 PC 端 Web UI（`http://127.0.0.1:5000`）。
- 產品安裝資料目錄為 `%ProgramData%\VibeDeck`。
- 備援腳本：`scripts\install-windows-product.ps1` / `uninstall-windows-product.ps1`（無 Inno 時可直接裝 payload）。

### 發佈準備

- Host 與測試升級到 .NET 8 LTS，並同步更新 Microsoft.Data.Sqlite 與 Windows 系統套件。
- ~~portable ZIP 正式發佈~~ **已廢止（見 0.1.1）**：Setup 是唯一產品安裝包。
- 明確區分 Setup 與選用通知 MSIX companion。
- 補上瀏覽器基本防護標頭，並避免配對裝置名稱被當成 HTML 顯示。
- README 改為先說一般使用者怎麼啟動，再區分原始碼開發流程。

### 手機 UX

- 未配對手機只保留單一「開始配對」流程，不再先顯示一整套無法使用的控制。
- 資訊板與額度頁隱藏顯示器專用控制，手機可多留空間給真正內容。
- 顯示器設定收斂成單一入口，並把三組設定改成看得懂的名稱。
- 找不到虛擬螢幕時顯示可行動的空狀態，可直接改用資訊板，不再留下黑畫面。

### 虛擬螢幕建立流程

- PC 頁面偵測不到虛擬螢幕時，可直接按「建立虛擬螢幕」。
- 安裝流程會要求一次 Windows 管理員確認，並在 Web 顯示下載、安裝、失敗重試與完成狀態。
- 採用固定版本、已簽章的 Virtual Display Driver；下載檔會核對 SHA-256 與 Authenticode 簽章，不關閉 Secure Boot、不開測試模式。
- 已配對手機不能遠端觸發管理員安裝，避免把系統權限交給遠端 Web 操作。

## 2026-07-12

### 簡化手機使用流程

- 移除 APK 下載、原生 App 入口、原生 QR 與原生 H.264 WebSocket 等不必要流程。
- 以瀏覽器／PWA 作為 iPhone、Android 與 BOOX 的唯一正式手機入口。
- 行動瀏覽器預設優先使用 WebRTC H.264，無法使用時自動回退 JPEG。
- 使用 HTTPS Host QR → 手機提出申請 → PC 允許／拒絕的配對流程；保留 Tailscale 跨網路連線與可選的 Host 密碼登入。
- 簡化 HTTPS、配對與首次連線說明，讓使用者能更快完成設定並開始使用。
- Android 原生殼保留為 legacy 原始碼，不再列入 Host UI、文件或發行流程。

### 新手入口整理

- README 改成從「啟動 Host → 開啟手機網址 → 配對 → 先試資訊板」開始。
- `start.bat`／`dev-run.ps1` 會直接列出手機可用的 HTTP／HTTPS 網址。
- 明確區分必要設定與選用功能：虛擬顯示驅動、HTTPS、Tailscale 與 AI 額度不再混在第一次啟動流程。

### 穩定性

- AGY 額度刷新遇到單一帳號操作錯誤時，會繼續處理其他帳號。

持續方向：減少不必要的選項與設定成本，讓 VibeDeck 更快上手，並以最少步驟提供最好的使用體驗。
