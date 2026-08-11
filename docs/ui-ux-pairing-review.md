# VibeDeck UI / UX 與首次配對盤點

盤點日期：2026-07-18
基準版本：`master` / `012ad15` / 已安裝 Host `v0.1.27`

## 2026-07-19 實作結果

本文件的 P1 與主要 P2 項目已在隔離分支 `codex/ui-ux-first-run` 落地，沒有修改配對 protocol、token、Worker 或 Host 資料格式：

- 零配對 PC、沒有明確 mode 與既有偏好時，自動進入 Device setup；已有偏好的使用者不受影響。
- PC 連線卡移除第二顆「配對新手機」與等待前假百分比；pending approval、六位數碼與允許／拒絕移到 QR 同一卡。
- 未配對手機只保留「連接這台電腦」；申請送出後顯示六位數碼並鎖定按鈕，拒絕或逾時才提供重試。
- trusted public URL 啟用時不再強制展開進階連線資訊；已配對裝置改顯示最後使用時間，IP 改為提示，清空全部配對移入更多操作。
- PC header 隱藏電子紙、長亮與全螢幕；手機只保留 Display／Sideboard／Quota，Codex 帳號操作收進可展開管理區。
- Device Lab 可切換 paired／unpaired／pending／local，並檢查水平 overflow、手機 12px／36px、電子紙 16px／44px 門檻。

實測使用分支前端搭配目前 Host API；14 組 S23、iPhone XS、BOOX 的橫直向、三語系、配對與 Quota 高風險組合全部通過。另經安全網址建立一筆暫時測試申請，確認 PC 在目前 viewport 內顯示格式化驗證碼與允許／拒絕，隨後以拒絕清除，已配對裝置數維持不變。

## 盤點方式

- 實際開啟 PC 本機 Host 的「裝置設定」、已配對裝置、連接新裝置、QR、電子紙代碼與進階連線區。
- 以目前設定的 `https://vd-<installation-id>.vibedeck.pp.ua/` 開啟未配對瀏覽器頁，只檢查首屏，未建立新的配對申請。
- 使用 Device Lab 驗證 Galaxy S23、iPhone XS、BOOX Go Color 7 的主要頁面、橫直向與三語系高風險組合。
- 檢查配對前端狀態、PC approval polling、Host endpoint 與 `DeviceTrustService` 的狀態轉換。

本輪沒有刪除裝置、送出配對、核准請求或修改產品設定。

## 驗證結果摘要

### 已成立的產品行為

- PC 沒有任何已配對裝置時，`連接新裝置` details 會預設展開。
- 手機的配對請求只有在使用者按下 `連接這台電腦` 後才建立，不會因為只開啟網址就自動配對。
- PC 必須核對六位數驗證碼並明確按 `允許`；QR 與電子紙連線碼都不能繞過核准。
- 電子紙可輸入 `vibedeck.pp.ua` 與一次性八位碼，不需要相機；有效碼會建立同一套 PC approval 流程。
- 配對完成後 token 會持久化；同一瀏覽器重新配對會延續裝置記錄並輪替 credential。
- Device Lab 抽查的 13 個高風險組合都符合目標 viewport，未偵測到水平 overflow。

### Device Lab 抽查矩陣

| 裝置 | 方向 | 頁面 | 語言 | 結果 |
|---|---|---|---|---|
| Galaxy S23 | 直向 | Sideboard | 繁中 | 360 × 780，通過 |
| Galaxy S23 | 直向 | Quota | English | 360 × 780，通過 |
| Galaxy S23 | 橫向 | Display | 日本語 | 780 × 360，通過 |
| Galaxy S23 | 直向 | Setup | 繁中 | 360 × 780，通過 |
| iPhone XS | 直向 | Sideboard | 日本語 | 375 × 812，通過 |
| iPhone XS | 直向 | Quota | English | 375 × 812，通過 |
| iPhone XS | 橫向 | Display | 繁中 | 812 × 375，通過 |
| iPhone XS | 直向 | Setup | 日本語 | 375 × 812，通過 |
| BOOX Go Color 7 | 橫向 | Sideboard | 繁中／English | 1054 × 794，通過 |
| BOOX Go Color 7 | 橫向 | Quota | 日本語 | 1054 × 794，通過 |
| BOOX Go Color 7 | 直向 | Quota | 繁中 | 794 × 1054，通過 |
| BOOX Go Color 7 | 橫向 | Setup | English | 1054 × 794，通過 |

這個結果只證明 viewport 與水平邊界。它還沒有驗證最小字級、垂直裁切、遠端授權狀態或實際觸控。

## UX 發現與優先級

### P1：首次安裝仍可能先進入 Display，而不是連線引導

`getInitialMode()` 在沒有 query 與既有偏好時回傳 `display`。雖然零裝置會把 `連接新裝置` 設為展開，但 PC console 只有在 `mode-setup` 才顯示這個區塊。Setup 與捷徑目前開啟根網址，沒有帶 `?mode=setup`。

使用者結果：全新安裝可能先看到虛擬螢幕畫面，不知道第一步應該到 `裝置設定`。

建議：PC loopback、零配對裝置、沒有明確 `mode`、沒有已儲存 view mode 時，首次自動進入 `setup`。已有偏好的舊使用者維持最後頁面。

### P1：未配對手機存在兩個誤導性的次要動作

未配對 rescue card 下方仍可能出現 `前往裝置設定` 與 `改用資訊板`。但 remote unpaired 狀態會隱藏 setup、display、sideboard、quota views；按下後可能沒有可理解的結果。文案 `資訊板可直接使用` 也容易被理解成「不需配對」，實際上只是「配對後不需虛擬螢幕」。

建議：未配對手機只保留一個主動作 `連接這台電腦`。補充文案改為：`配對完成後，資訊板與 AI 額度不需安裝虛擬螢幕。`

### P1：PC 核准按鈕可能出現在目前視窗之外

待核准請求顯示在 `已配對裝置` 區塊頂端；使用者掃 QR 或產生電子紙代碼時，通常停留在較下方的 `連接新裝置` 內容。請求到達後雖然進度與狀態會更新，真正的 `允許／拒絕` 按鈕可能在上方不可見。

建議：pairing-active 時，把 pending approval 同步顯示在目前連線卡內，或自動將 PC 安全捲到 approval row。核准卡應固定包含裝置名稱、平台、六位數碼、允許與拒絕。

### P1：PC 端有重複的「開始配對」概念

按 `＋ 新增` 已經展開 QR 並呼叫 `startPhonePairing()`，但展開內容仍保留另一個 `配對新手機` 主按鈕。進度同時會在尚未收到手機請求前顯示 10–20%，讓使用者難以判斷現在是「QR 已準備」還是「配對已開始」。

建議：

- `＋ 新增` 只負責展開一次連線卡並準備 QR。
- 卡片不再放第二顆 `配對新手機`。
- 手機按下 `連接這台電腦` 前，PC 顯示狀態文字 `等待手機提出申請`，不要顯示百分比。
- 真正 request 到達後才進入 `等待 PC 核准`。

### P2：安全網址啟用後，步驟 2 與步驟 3 語意重複

目前 trusted public URL 的畫面會把第二步改成 `連接這台電腦`，第三步仍寫 `手機按「連接這台電腦」，再回 PC…`，形成重複指示。

建議固定成：

1. 手機掃描 QR Code。
2. 手機按 `連接這台電腦`，畫面顯示六位數碼。
3. PC 核對同一組碼並按 `允許`。

### P2：進階 Tunnel 資訊在配對時被強制打開

`setPairingUiActive(true)` 會強制展開 `進階連線資訊`。一般使用者因此看到完整 host URL、憑證入口、installation id、Tunnel 欄位與停用按鈕；這些不是成功配對所需資訊。

建議：使用 trusted public URL 時保持收合。只有 public endpoint 缺失、健康檢查失敗或使用者主動展開時才顯示；憑證 fallback 與 Tunnel 設定應標示為進階／維修用途。

### P2：已配對裝置區的資訊與破壞性操作過於突出

- 完整 IPv6 位址佔用主要視覺空間，但一般使用者只需要裝置名稱、連線狀態與最後使用時間。
- `清空` 與 `＋ 新增`、refresh 並列，破壞性層級不夠明確。

建議：IP 收入裝置詳細資訊或診斷；`清空所有裝置` 移到 overflow／進階區，保留 confirm 並使用危險樣式。

### P2：手機導覽與 Quota 工具列仍偏密

S23 360px 預覽沒有水平 overflow，但頂部四個頁面會換成兩列，Language 再佔一列；Quota 的 account switcher、狀態、分頁與三個帳號動作同時出現在窄卡內。English 尤其接近「看得下但不易讀」。

建議：

- 已配對手機使用固定三頁導覽：Display、Sideboard、Quota；Setup 收入選單。
- Quota 首屏優先顯示帳號、剩餘比例、重置時間；Switch／Re-authenticate／Delete 收入單一管理區。
- Device Lab 增加最小字級與主要 CTA 點按高度檢查，不只檢查 overflow。

### P2：Device Lab 尚未模擬真正的授權狀態

Device Lab iframe 由 loopback 載入，因此 Host 會把它視為 local console。它能準確驗證 viewport、CSS 與內容邊界，但不能代表遠端裝置的 `unpaired`、`pending`、`paired` 顯示條件。

建議新增只限 loopback 且只在 `devicePreview` 生效的狀態參數：

- `previewTrust=unpaired`
- `previewTrust=pending`
- `previewTrust=paired`
- `previewTrust=local`

這會讓首次配對 UI 不必每次刪除實機裝置或重跑 ADB，也能做穩定的視覺回歸。

### P3：PC 頂部工具列角色過多

PC header 同時放頁面切換、語言、電子書、長亮、全螢幕、重新整理、更新與連線狀態。1280px 尚可容納，窄視窗會快速換行；`電子書` 對一般 PC 使用者也可能被理解成「連接電子書」而不是切換目前 client 樣式。

建議保留主要頁面與狀態；長亮、全螢幕改為 Display context action；更新放設定；電子紙模式放 Device Lab／進階工具。

## 建議的首次使用流程

### PC：第一次安裝

```text
Setup 完成
  -> 自動開啟 Device setup
  -> 顯示「連接第一台裝置」
  -> QR 為主要入口，電子紙代碼為次要入口
  -> 等待手機提出申請
  -> 同一卡片顯示裝置 + 六位數碼 + 允許 / 拒絕
  -> 配對完成
  -> 選擇 Display / Sideboard / Quota
```

虛擬螢幕安裝應是 Display 的條件，不應阻擋 Sideboard 或 Quota 的首次體驗。

### iPhone / Android：掃 QR

```text
掃 QR
  -> 開啟 VibeDeck 安全網址
  -> 單一主按鈕「連接這台電腦」
  -> 顯示六位數驗證碼與「回 PC 核准」
  -> 核准成功，自動 reload
  -> 選擇主要模式
```

頁面不應再要求使用者理解 Tunnel、憑證、IP、Host 或配對 token。

### BOOX / 無相機電子紙

```text
輸入 vibedeck.pp.ua
  -> 輸入 PC 顯示的一次性八位碼
  -> 顯示六位數驗證碼與等待 PC 核准
  -> PC 同一卡片允許
  -> 自動進入 Sideboard
  -> 視需要加入主畫面
```

## 不可破壞的安全與產品條件

- QR 不包含可直接取得持久裝置權限的 token。
- Worker 一次性碼只解析目標 URL，不得自行配對或繞過 PC approval。
- 每次新裝置都要在 PC 顯示六位數碼並由使用者明確允許。
- local-only 管理動作不能暴露給手機或 public endpoint。
- 配對完成後重整、PWA、iOS Home Screen 與 Host 更新仍能恢復 credential。
- `%ProgramData%\VibeDeck` 的裝置與版面資料在 Setup 更新時保留。

## 建議實作批次

1. **文案與可見性**：移除未配對 dead-end actions、修正三步文案、不要自動展開進階連線資訊。
2. **首次路由**：零裝置的新 PC 預設進入 Setup；舊使用者保留最後頁面。
3. **核准位置**：pending approval 放入目前 pairing card，避免捲動尋找。
4. **Device Lab 狀態**：加入 unpaired/pending/paired/local preview 與 CTA/min-font assertions。
5. **手機資訊密度**：簡化導覽與 Quota account management，不改 API 或 pairing protocol。

每批都應獨立驗證並可單獨回退，避免再次把首次配對、電子紙與 Display 同時改動。
