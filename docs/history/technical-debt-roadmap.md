# VibeDeck 結構債與重構路線圖

盤點日期：2026-07-18
基準版本：`master` / `012ad15` / Host `v0.1.27`

本文件只整理結構風險、重構前置條件與建議順序，不代表目前產品不可交付，也不要求在 Build Week 送件前立即重寫。重構的首要目標是降低後續改動的回歸半徑，同時保留既有安裝、更新、配對、資料持久化與三裝置產品流程。

## 評分方式

- **耦合度 5**：跨安全、儲存、API、前端狀態或外部服務；改一處可能影響多條產品流程。
- **耦合度 4**：跨數個模組，但仍有可辨識的功能邊界。
- **耦合度 3**：主要集中在單一功能垂直切面，外部契約相對固定。
- **修改難度 5**：需要先補契約／整合／視覺測試，且不適合一次搬完。
- **修改難度 4**：可分批抽取，但每批都需要產品流程驗證。
- **修改難度 3**：已有邊界與測試，可在不改外部行為下逐步整理。

## 目前規模快照

| 檔案 | 行數 | 主要責任 |
|---|---:|---|
| `wwwroot/index.css` | 7,154 | PC、手機、BOOX、配對、Dashboard、Quota 與最終覆寫安全網 |
| `wwwroot/index.js` | 4,567 | 192 個函式；全域狀態、啟動、配對、裝置、額度、顯示、事件協調 |
| `Startup.cs` | 1,836 | DI、中介軟體、路由、授權條件、配對、顯示、額度與回應輸出 |
| `Quotas/AgyQuotaService.cs` | 1,258 | OAuth、帳號儲存、快取、遠端 API、CLI 與額度正規化 |
| `CustomSources/CustomSourceStore.cs` | 1,203 | SQLite 初始化、遷移、CRUD、交易、快照與損毀復原 |
| `wwwroot/modules/custom-cards.js` | 941 | 自訂卡片顯示、來源管理、設定表單與通知整合 |
| `CustomSources/CustomSourceService.cs` | 916 | 驗證、正規化、限流、認證、事件發布與商業規則 |
| `Security/DeviceTrustService.cs` | 780 | 待核准配對、信任資料、token、裝置辨識與持久化 |

目前 `Startup*.cs` 約有 75 個 endpoint mapping；前端已有 14 個模組，但 `index.js` 仍是所有模組的狀態中心與生命週期入口。`Startup` 已使用 partial files 分出少數端點，不過共用 static helper 與同一個 class 仍使它們保持編譯期與行為耦合。

## 結構債排序

下表依「耦合度高到低，再依修改難度高到低」排序。這不是實作優先順序；高風險項目通常應先補測試，再動程式。

| 排序 | 範圍 | 耦合度 | 修改難度 | 主要風險 | 建議 |
|---:|---|---:|---:|---|---|
| 1 | 配對與裝置信任垂直切面 | 5 | 5 | Host 路由、`DeviceTrustService`、前端 localStorage/cookie/poll、Cloudflare 連線碼與 PC 核准共同形成一條安全協定 | 先建立完整狀態機與端點整合測試，再分離 pending pairing、trusted device store 與 device identity resolver |
| 2 | 前端應用協調器 `index.js` | 5 | 5 | 大量共享 mutable state、DOM 參照與 callback 注入；模式切換、配對、串流、額度和啟動順序互相影響 | 先抽 `app-state` 與 `api-client`，再按 pairing、display、quota view、bootstrap 分批搬移 |
| 3 | CSS cascade `index.css` | 5 | 5 | 同一 selector 在手機、BOOX、窄視窗與檔尾 safety net 多次覆寫；來源順序就是隱藏契約 | 先建立 selector/cascade 地圖與視覺基線，再按原順序拆檔；第一階段不得改 selector 或提高 specificity |
| 4 | Host 路由與授權組合 `Startup.cs` | 5 | 4 | 路由 mapping、序列化、授權 guard、稽核、服務取得混在同一入口；安全條件容易在新增 endpoint 時漏套 | 保留 middleware 順序，改用 domain endpoint extension；將 `Require*` 收斂成可測的 request policy service |
| 5 | 動態多語系機制 | 4 | 5 | `tLegacy`、文字節點掃描與 MutationObserver 依賴原始中文文案；文案微調可能靜默失去翻譯 | 新 UI 一律使用 key；逐頁淘汰文字比對，加入動態 DOM 與三語系畫面測試，不做一次性全站替換 |
| 6 | Quota 垂直切面 | 4 | 4 | `AiQuotaService` 直接 new provider；AGY 服務五合一；前端 renderer/account actions 仍大量留在入口檔 | 先注入 provider 介面與 clock/HTTP 邊界，再拆 OAuth、account repository、quota client、cache、CLI launcher |
| 7 | 靜態環境與相容名稱 | 3 | 5 | `AppPaths`、`PhoneMonitor` 相容名稱、靜態 reader/store 與安裝環境判斷散布多處；改名或改資料根目錄可能破壞升級 | 除非有明確產品需求，維持相容名稱；先以介面包住路徑與環境，不直接全域 rename |
| 8 | Custom Sources 垂直切面 | 3 | 3 | Store 與 Service 都大，但功能邊界、SQLite 交易與測試相對完整；過度拆 query 反而會破壞一致性 | 先抽 schema/migration、row mapper、payload normalizer；保留交易型操作在同一 repository |

## 重要依賴與重構切點

### 1. 配對與裝置信任

目前協定跨越：

```text
PC Setup UI
  -> /api/connect + QR / e-paper code
  -> phone pending request + localStorage/cookie
  -> /api/devices/pairing/*
  -> DeviceTrustService pending state
  -> PC allow/deny
  -> persistent trusted-devices.json
  -> protected API / WebSocket access
```

建議切點：

- `PendingPairingService`：只管理 request、secret、verification code、TTL 與狀態轉換。
- `TrustedDeviceRepository`：只管理 `trusted-devices.json`、原子寫入、備份與 token rotation。
- `DeviceIdentityResolver`：集中 browser model、client instance 與顯示名稱正規化。
- `DeviceAccessPolicy`：集中 local、host login、trusted device 與 forwarded public endpoint 判斷。
- `PairingController`（前端）：只管理手機申請、poll、PC pending approvals 與完成後 reload。

不可在同一批同時變更 token 格式、cookie 名稱、Worker 參數、endpoint path 與 UI 流程。

### 2. 前端入口

建議先建立兩個穩定基礎：

- `app-state.js`：唯一保存 active mode、trust、display、stream、quota 與 client capability；透過明確事件通知 controller。
- `api-client.js`：唯一管理 action token、device token、trace id、JSON error 與 retry。

之後分批抽取：

- `pairing-controller.js`
- `device-management-controller.js`
- `display-controller.js`
- `quota-view.js`
- `quota-account-actions.js`
- `app-bootstrap.js`

既有 controller 目前接受大量 DOM element 與 callback，表示檔案已拆開，但狀態所有權尚未拆開。下一輪重構的判斷標準不是「index.js 少幾行」，而是 controller 是否能只依賴小型介面，不再回呼入口檔的內部函式。

### 3. Host 路由

建議目標：

```text
Startup / Program
  -> DI registration
  -> middleware order
  -> MapConnectEndpoints
  -> MapDeviceEndpoints
  -> MapDisplayEndpoints
  -> MapQuotaEndpoints
  -> MapDashboardEndpoints
  -> MapUpdateEndpoints
```

每組 endpoint mapping 應只負責 HTTP 契約與呼叫 application service。序列化、錯誤碼、授權與稽核共通邏輯應有一個可測的入口，不要在每組 routes 複製 `Require*` 判斷。

### 4. CSS

建議依目前 cascade 順序拆成：

1. `tokens-base.css`
2. `shell-setup.css`
3. `display.css`
4. `sideboard.css`
5. `quota.css`
6. `phone.css`
7. `eink.css`
8. `responsive-safety.css`

第一階段只搬檔並保持載入順序。第二階段才引入 cascade layers、刪除重複規則或調整 specificity。檔尾標示「Keep this block last」的規則就是目前最需要保留的隱藏契約。

## 重構前必補護欄

1. **Endpoint contract tests**：至少覆蓋 `/api/connect`、`/api/devices/status`、pairing request/poll/approve/deny、quota actions、WebSocket 未授權狀態與 local-only endpoints。
2. **Pairing state-machine tests**：fresh、pending、approved、denied、expired、re-pair、stale localStorage、connection-code auto-pair、Host 重啟。
3. **Frontend state previews**：Device Lab 增加 `unpaired`、`pending`、`paired`、`local-console` 四種只限 loopback 的預覽狀態。
4. **Visual assertions**：除水平 overflow 外，檢查固定高度、主要 CTA 可見、最小字級、控制列換行與 BOOX 單屏卡片邊界。
5. **Persistence compatibility fixtures**：保留舊版 `trusted-devices.json`、dashboard layout、quota account、certificate/public endpoint store 樣本，驗證新版可直接讀取。
6. **Installed-product flow**：每個跨 Host/Setup 的重構批次都必須跑 `scripts/test-product-flow.ps1 -Installed`；不能只驗證 source Host。

## 建議實作順序

### Phase 0：交件凍結

- 送件前只修阻斷性錯誤，不做 framework migration、全域 rename 或 pairing protocol 重寫。
- 固定目前 Setup、影片、Device Lab 與三語系基線。

### Phase 1：建立測試接縫

- 加 Host endpoint integration harness。
- 加 Device Lab trust-state simulation。
- 記錄 pairing API schema、status code、cookie/header 與持久化 fixture。

### Phase 2：低風險機械拆分

- 保持 middleware 順序，將非安全敏感 endpoint mapping 移出 `Startup.cs`。
- 保持 cascade 順序拆 `index.css`。
- 將 quota 純 renderer 與 account action wiring 從 `index.js` 抽出。

### Phase 3：狀態所有權

- 建立 `api-client` 與 `app-state`。
- 讓既有 modules 不再直接依賴入口檔的大量 callback。
- 抽出 pairing 與 device-management controller。

### Phase 4：領域內部整理

- 拆 Agy OAuth、account store、quota client、cache 與 CLI。
- 拆 Custom Source schema/mapping/normalization，但保留交易一致性。
- 最後才評估 compatibility name 與 static path abstraction。

## 每批重構完成條件

- 對外 endpoint、JSON 欄位、錯誤碼、header、cookie 與 WebSocket 行為不變，除非有獨立 migration 設計。
- `%ProgramData%\VibeDeck` 的已配對裝置、版面、帳號、憑證、診斷與自訂來源可直接沿用。
- Host 仍在登入使用者桌面 Session 執行，不轉成 Windows Service。
- Source、Installed 與 Worker 測試通過。
- Device Lab 的 S23、iPhone XS、BOOX、橫直向與三語系高風險矩陣通過。
- 至少一次實機驗證配對、串流、BOOX 固定高度與更新流程。

## 明確不建議

- 不做 `index.js`、`Startup.cs` 或 `index.css` 的一次性重寫。
- 不在沒有 integration tests 時搬動授權 helper 或 pairing persistence。
- 不因公開品牌已改為 VibeDeck 就全域刪除 `PhoneMonitor` 相容識別。
- 不在同一個 PR 同時重構配對、串流與 Setup。
- 不把「檔案變小」當成完成；要以依賴方向、狀態所有權與回歸半徑衡量。
