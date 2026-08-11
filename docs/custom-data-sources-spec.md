# VibeDeck 自訂資料來源與即時卡片規格

狀態：Ready for implementation

規格版本：1.0

最後更新：2026-07-13

實作目標：交付可在單一 VibeDeck Host 上安全使用的 MVP

## 0. 給實作者的執行指令

- 開始修改前先完整讀完本規格，以及現有 `Startup.cs`、`DashboardEventHub.cs`、`index.js`、`modules/sideboard.js` 與安全服務。
- 本文件中的「必須」、「不得」與 Definition of Done 都是交付條件，不是選配建議。
- 從後端安全與 persistence 開始，完成 API 測試後再做 UI。
- 不得只完成 happy path、mock UI 或記憶體版本後宣稱完成。
- 遇到規格與現況衝突時，優先保留既有安全與配對行為，回報衝突後再決定；不得靜默弱化保護。
- 工作區如有使用者既存變更，必須保留並避開；不要重設或覆蓋。
- 除非使用者另行要求，不要自行 commit、push 或開 PR。

## 1. 目的

讓外部程式以 HTTP POST 將資料送進 VibeDeck，並讓已配對的手機在「資訊板」中近即時看到對應卡片，不需要重新整理頁面。

典型使用情境：

- Discord bot 推送最新訊息。
- CI/CD 推送建置狀態或進度。
- 本機腳本推送服務健康度、待辦或任意數值。
- Home Assistant、Node-RED、PowerShell、Python 等可連到 Host 的系統推送狀態。

本功能在產品中稱為「自訂資料來源」（Custom Sources）。外部系統只能提交資料；卡片類型、標題、順序、有效期限與呈現方式由 VibeDeck 的本機管理者設定。

## 2. 成功定義

完成後必須能走通以下流程：

1. 使用者在 Host PC 的資訊板建立一個名為「Discord 訊息」的資料來源。
2. VibeDeck 只在建立當下顯示一次寫入端點、Source Token 與 PowerShell/curl 範例。
3. 外部系統持該 Token POST 一筆 JSON。
4. Host 驗證、正規化、持久化資料，增加卡片 revision。
5. Host 透過既有 `/api/dashboard/events` SSE 發出 `custom-card` 通知。
6. 已配對且正在看資訊板的手機取得最新卡片快照並在一般手機上一秒內完成更新。
7. 手機中斷連線、重新整理或 Host 重啟後，仍能由快照恢復最後有效資料。

「任何系統都能送資料」的精確定義是：

> 任何能連到該 Host、持有有效 Source Token，且符合資料格式與傳輸安全要求的系統。

這不是未驗證的公開寫入 API，也不是多租戶雲端服務。

## 3. 已決定的架構

資料路徑固定如下：

```text
External producer
  -> POST /api/custom-sources/{sourceKey}/events
  -> Source Token authentication
  -> payload limit / rate limit / schema validation
  -> SQLite transaction (upsert + revision)
  -> DashboardEventHub.Publish("custom-card", metadata)
  -> existing SSE /api/dashboard/events
  -> browser GET /api/custom-cards
  -> safe declarative renderer
```

重要決策：

- 沿用現有 SSE，不新增自訂卡片 WebSocket。
- SSE 只傳變更通知，不傳完整外部 payload。
- 手機永遠可以用 `GET /api/custom-cards` 重新建立完整畫面。
- 外部資料不能包含或執行 HTML、JavaScript、CSS。
- Source Token 只能修改該 Source 擁有的預設 Card。
- MVP 的一個 Source 對應一張 `default` Card；資料庫保留 Source 與 Card 分離的結構，方便未來一個 Source 擁有多張 Card。
- MVP 使用 Host 共用的一套卡片順序，不做 per-device layout。
- 使用 SQLite；不得在未回報的情況下改成散落 JSON 檔。

## 4. MVP 範圍

### 4.1 必須完成

- PC 本機建立、編輯、啟用／停用、排序、輪替 Token、刪除資料來源。
- 四種固定卡片：`message-feed`、`status`、`metric`、`key-value`。
- Source Token 的產生、雜湊保存、固定時間比較、撤銷與輪替。
- HTTPS／localhost 傳輸政策。
- 每個 Source 的 payload 大小限制與 rate limit。
- SQLite 持久化、schema migration、過期清理、訊息保留上限。
- `GET /api/custom-cards` 快照。
- `custom-card` SSE 事件與前端合併更新。
- 資訊板中的「系統／自訂」子頁面。
- PC 專用管理 UI；手機只顯示卡片。
- 空狀態、未更新狀態、過期狀態、錯誤狀態。
- 自動化測試、端對端 PowerShell 驗證腳本與視覺驗證。

### 4.2 明確不做

- 公網 relay、NAT 穿透、Port Forwarding 自動設定。
- GitHub、Discord 等供應商原生 webhook payload 轉換器。
- 任意 JSONPath 欄位映射或視覺化卡片設計器。
- 一個 Source 對應多張 Card 的管理 UI 或 API。
- 每支手機不同的 layout。
- 拖拉排序；MVP 使用上移／下移。
- 任意 HTML、Markdown、JavaScript、CSS。
- 遠端圖片 URL、檔案上傳、附件。
- 卡片上的回呼按鈕或 PC 控制動作。
- 完整歷史查詢、圖表、統計聚合。
- 多使用者、帳號、組織或公開分享。
- HMAC 供應商簽章；MVP 的 VibeDeck 原生 Push API 使用 Bearer Source Token。

若實作者認為其中任何非目標是完成 MVP 的必要條件，必須先停下並回報，不得自行擴大範圍。

## 5. 名詞與資料所有權

### 5.1 Source

外部整合的身分與安全邊界，包含：

- 不可變的 `sourceKey`。
- 可修改的 `displayName`。
- 一個目前有效的 Source Token 雜湊。
- 啟用狀態。
- 最後收到資料時間。
- 一張 MVP 預設 Card。

### 5.2 Card

VibeDeck 管理的呈現設定，包含：

- `cardId`。
- `sourceId`。
- 固定為 `default` 的 `cardKey`。
- `type`。
- `title`。
- 全域 `position`。
- `staleAfterSeconds`。
- `defaultTtlSeconds`。
- 卡片類型專屬設定，例如 `maxItems`。
- 目前 `revision`。

外部 POST 不得修改上述設定，也不得指定其他 `cardId`。

### 5.3 Item / State

- `message-feed` 保存多個 Item，使用外部 `id` 作為 upsert key。
- `status`、`metric`、`key-value` 只保存一個目前 State，內部 key 固定為 `current`。
- 每次成功寫入都由 Host 產生 `receivedAt` 與遞增 `revision`。
- 外部 `timestamp` 只代表事件發生時間，不作為安全、TTL 或寫入順序的可信時間來源。

## 6. Source Key 與 Token

### 6.1 Source Key

- 長度 3 到 48。
- 僅允許小寫 ASCII、數字與 `-`。
- 必須符合：`^[a-z0-9][a-z0-9-]{1,46}[a-z0-9]$`。
- Host 內不分大小寫唯一；API 正規化成小寫。
- 建立後不可修改。顯示名稱可以修改。
- 保留名稱：`system`、`sideboard`、`quota`、`display`、`dashboard`、`api`。

### 6.2 Source Token

- 使用密碼學安全亂數產生至少 32 bytes。
- 對外格式以 `vds_` 開頭，後接無 padding 的 base64url。
- 資料庫只保存完整 Token 的 SHA-256 雜湊，不保存明文。
- 驗證雜湊時使用固定時間比較。
- 明文 Token 只在建立 Source 與輪替 Token 的回應中出現一次。
- Token 不得寫入 log、設定檔、資料庫明文字段、HTML source、localStorage 或 sessionStorage。
- 輪替立即使舊 Token 失效；MVP 不提供 grace period。
- Source Token 只接受 `Authorization: Bearer <token>`。
- 不接受 query string、cookie、Device Token、Action Token 或遠端登入 cookie 代替 Source Token。

## 7. 傳輸與安全政策

### 7.1 寫入端點的傳輸限制

- 來自 loopback／同一台 Host 的請求可以使用 HTTP。
- 非本機請求預設必須使用 HTTPS，否則回 `426 upgrade_required`。
- 可透過 `CustomSources:AllowInsecureLan=true` 明確允許非本機 HTTP；預設必須是 `false`。
- 不新增寬鬆 CORS。這是 server-to-server API，不需要 `Access-Control-Allow-Origin: *`。
- Host Remote Access 密碼登入不能取代 Source Token。

### 7.2 讀取與管理權限

- `GET /api/custom-cards` 使用既有 `RequireTrustedDeviceAsync`。
- `GET /api/custom-sources` 只允許 PC 本機請求。
- 所有新增、修改、排序、輪替與刪除管理操作都必須同時通過既有 Action Token 與 PC 本機檢查。
- 管理 API 永遠不得回傳 `tokenHash`。
- 手機 API 永遠不得回傳 Source Token、管理設定或完整原始 payload 中未使用的欄位。

### 7.3 輸入限制

預設設定加入 `appsettings.json`：

```json
"CustomSources": {
  "AllowInsecureLan": false,
  "MaxPayloadBytes": 65536,
  "RequestsPerSecond": 10,
  "Burst": 30,
  "CleanupIntervalSeconds": 30
}
```

要求：

- 僅接受 `application/json`，否則 `415 unsupported_media_type`。
- Request body 最大 64 KiB，超過回 `413 payload_too_large`，而且不能先完整載入記憶體後才判斷。
- JSON 最大深度 8。
- 每個 Source 獨立 rate limit，預設每秒補充 10 個 token、最多 burst 30。
- 超過限制回 `429 rate_limited` 並提供合理的 `Retry-After`。
- Host 全域最多 50 個 Source。
- 禁止 NaN、Infinity 或無法序列化的數值。
- 未知 JSON 欄位可以接受以保持 forward compatibility，但不得影響渲染、CSS class、DOM attribute 或 API 路由。

## 8. 卡片設定

建立 Source 時必須同時建立一張預設 Card。

共同設定：

| 欄位 | 限制 | 預設 |
|---|---:|---:|
| `title` | 1–80 字元 | `displayName` |
| `type` | 四種固定值之一，建立後不可修改 | 必填 |
| `position` | 0–10000 | 下一個可用位置 |
| `staleAfterSeconds` | 0 或 30–604800；0 表示不標示過期 | 300 |
| `defaultTtlSeconds` | 0 或 30–604800；0 表示不自動刪除 | 0 |
| `maxItems` | 1–50；只有 message-feed 使用 | 20 |

`type` 建立後不可修改，因為不同類型的 payload 與保存語意不同。需要改類型時，使用者刪除並重建 Source。

## 9. 外部寫入 API

### 9.1 寫入事件

```http
POST /api/custom-sources/{sourceKey}/events
Authorization: Bearer <source-token>
Content-Type: application/json
```

處理順序必須是：

1. 檢查傳輸安全。
2. 正規化 Source Key 並查詢 Source；若不存在，執行一次 dummy hash compare 後回傳與錯誤 Token 相同的 401。
3. 驗證 Bearer Source Token；外部寫入端點不得用回應差異透露 Source 是否存在。
4. Token 正確後才檢查 Source 是否啟用；停用時回 403。
5. 執行 per-source rate limit。
6. 檢查 Content-Type、長度、JSON 深度與 schema。
7. 以 SQLite transaction 寫入資料並增加 revision。
8. transaction 成功後才發出 SSE 通知。
9. 回傳成功結果。

不得在資料庫 commit 前先通知前端。

成功回應：

```json
{
  "accepted": true,
  "sourceKey": "chat",
  "cardId": "8bcfcfee260a4d7e8b89d140f1e73a58",
  "revision": 42,
  "receivedAt": "2026-07-13T10:00:01.123Z",
  "operation": "inserted"
}
```

`operation` 只能是：

- `inserted`
- `updated`
- `replaced`

同一筆資料重送仍回 `200`。`message-feed` 同一 `id` 為 upsert，不得增加重複 Item；內容相同也可以更新 `receivedAt` 與 revision，但測試與文件必須採一致行為。規格決定採「每次有效 POST 都更新 receivedAt 與 revision」。

### 9.2 刪除 message-feed item

```http
DELETE /api/custom-sources/{sourceKey}/items/{itemId}
Authorization: Bearer <source-token>
```

- 僅適用 `message-feed`。
- 找到並刪除時回 `200` 與 `{ "deleted": true, ... }`。
- Item 不存在時回 `404 item_not_found`。
- 成功刪除必須增加 Card revision 並發 SSE 通知。

### 9.3 清除目前 state

```http
DELETE /api/custom-sources/{sourceKey}/state
Authorization: Bearer <source-token>
```

- 僅適用 `status`、`metric`、`key-value`。
- 有 State 時刪除、增加 revision、發 SSE，回 `200`。
- 沒有 State 時仍回 `200`，`cleared` 為 `false`，不必增加 revision。

## 10. Payload Schema

所有時間字串必須是帶時區的 ISO 8601。未提供 `timestamp` 時，顯示時間使用 Host 產生的 `receivedAt`。

`ttlSeconds` 可省略。有效值為 30–604800；省略時使用 Card 的 `defaultTtlSeconds`。TTL 一律從 Host `receivedAt` 起算，不從外部 `timestamp` 起算。

`severity` 可省略，預設為 `info`，有效值：`info`、`success`、`warning`、`error`。

### 10.1 message-feed

```json
{
  "id": "msg-123",
  "from": "Discord",
  "text": "有人找你",
  "timestamp": "2026-07-13T18:00:00+08:00",
  "severity": "info",
  "ttlSeconds": 600
}
```

限制：

- `id`：必填，1–128 字元，必須符合 `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`，確保可安全作為 Item API 的單一路徑 segment。
- `from`：可選，最多 80 字元。
- `text`：必填，1–2000 字元。
- 同一 Source 內 `id` 唯一，重送為 upsert。
- 超過 `maxItems` 時，在同一 transaction 內依 revision 刪除最舊項目。

### 10.2 status

```json
{
  "status": "Build passed",
  "detail": "main · #184",
  "severity": "success",
  "timestamp": "2026-07-13T18:00:00+08:00",
  "ttlSeconds": 3600
}
```

限制：

- `status`：必填，1–120 字元。
- `detail`：可選，最多 500 字元。
- 每次有效 POST 取代目前 State。

### 10.3 metric

```json
{
  "value": 73.5,
  "unit": "%",
  "detail": "API quota used",
  "progress": 73.5,
  "severity": "warning",
  "timestamp": "2026-07-13T18:00:00+08:00"
}
```

限制：

- `value`：必填，有限 JSON number。
- `unit`：可選，最多 16 字元。
- `detail`：可選，最多 500 字元。
- `progress`：可選，0–100。
- 每次有效 POST 取代目前 State。

### 10.4 key-value

```json
{
  "items": [
    { "label": "Queue", "value": "12" },
    { "label": "Workers", "value": "4 online" }
  ],
  "timestamp": "2026-07-13T18:00:00+08:00",
  "ttlSeconds": 600
}
```

限制：

- `items`：必填，1–12 筆。
- `label`：必填，1–50 字元。
- `value`：必填，1–200 字元。
- 每次有效 POST 取代目前 State。

### 10.5 正規化

- 儲存前去除字串前後空白，但不得更改中間換行。
- `text` 與 `detail` 最多保留 8 行；超過回 `400 invalid_payload`，不自動截斷。
- API 不接受由 payload 指定 `title`、`type`、`position`、`cardId`、CSS class、URL、HTML 或 action。
- 前端只渲染正規化模型中的已知欄位。

## 11. 管理 API

所有 JSON 回應使用 camelCase，並加：

```http
Cache-Control: no-store
Content-Type: application/json; charset=utf-8
```

### 11.1 列出 Sources

```http
GET /api/custom-sources
```

權限：PC 本機限定。回傳 Source、Card 設定、最後接收時間、目前資料筆數與健康狀態，不回傳 Token 或 Token Hash。

```json
{
  "sources": [
    {
      "sourceKey": "chat",
      "displayName": "Discord 訊息",
      "enabled": true,
      "createdAt": "2026-07-13T09:00:00.000Z",
      "updatedAt": "2026-07-13T09:00:00.000Z",
      "lastReceivedAt": "2026-07-13T10:00:01.123Z",
      "itemCount": 1,
      "health": "active",
      "card": {
        "cardId": "8bcfcfee260a4d7e8b89d140f1e73a58",
        "cardKey": "default",
        "type": "message-feed",
        "title": "即時訊息",
        "position": 100,
        "staleAfterSeconds": 300,
        "defaultTtlSeconds": 600,
        "maxItems": 20,
        "revision": 42
      }
    }
  ]
}
```

`health` 只能是 `waiting`、`active`、`stale`、`disabled`。整個 Custom Sources store 不可用時，端點直接回 503，不回部分 Source 列表。

### 11.2 建立 Source

```http
POST /api/custom-sources
X-VibeDeck-Action-Token: <action-token>
```

```json
{
  "sourceKey": "chat",
  "displayName": "Discord 訊息",
  "card": {
    "title": "即時訊息",
    "type": "message-feed",
    "position": 100,
    "staleAfterSeconds": 300,
    "defaultTtlSeconds": 600,
    "maxItems": 20
  }
}
```

權限：Action Token + PC 本機限定。

成功回 `201`，且只有這次回應包含：

```json
{
  "source": {},
  "ingest": {
    "endpointPath": "/api/custom-sources/chat/events",
    "endpointUrl": "https://host:5443/api/custom-sources/chat/events",
    "localEndpointUrl": "http://127.0.0.1:5000/api/custom-sources/chat/events",
    "token": "vds_..."
  }
}
```

`endpointUrl` 使用既有 `ConnectInfoProvider` 的 preferred reachable Host URL 組成，不得因管理請求來自 `127.0.0.1` 而產生只能由本機使用的主要 endpoint。`localEndpointUrl` 則固定提供 localhost 用法。若 HTTPS 尚不可用，UI 必須清楚標示非本機寫入預設會被拒絕，不能假裝 LAN HTTP endpoint 可直接使用。

### 11.3 修改 Source / Card

```http
PATCH /api/custom-sources/{sourceKey}
X-VibeDeck-Action-Token: <action-token>
```

可修改：

- `displayName`
- `enabled`
- `card.title`
- `card.position`
- `card.staleAfterSeconds`
- `card.defaultTtlSeconds`
- `card.maxItems`

不可修改 `sourceKey`、`cardId`、`cardKey`、`type`。傳入不可修改欄位時回 `400 immutable_field`，不得靜默忽略。

變更會影響手機畫面時，增加 Card revision 並發 `custom-card` SSE。

### 11.4 輪替 Token

```http
POST /api/custom-sources/{sourceKey}/rotate-token
X-VibeDeck-Action-Token: <action-token>
```

權限：Action Token + PC 本機限定。

- 立即使舊 Token 失效。
- 新 Token 只在本次回應顯示。
- 不增加 Card revision，不需要發 SSE。

### 11.5 刪除 Source

```http
DELETE /api/custom-sources/{sourceKey}
X-VibeDeck-Action-Token: <action-token>
```

權限：Action Token + PC 本機限定。

- UI 必須二次確認並顯示 Source 名稱。
- SQLite transaction 內 cascade 刪除 Card 與 Items。
- 成功回 `200` 與 `{ "deleted": true, "sourceKey": "chat" }`。
- 成功後發 `custom-card` SSE，reason 為 `deleted`。

## 12. 手機快照 API

```http
GET /api/custom-cards
```

權限：既有 `RequireTrustedDeviceAsync`。

回應範例：

```json
{
  "generatedAt": "2026-07-13T10:00:01.200Z",
  "cards": [
    {
      "cardId": "8bcfcfee260a4d7e8b89d140f1e73a58",
      "sourceKey": "chat",
      "title": "即時訊息",
      "type": "message-feed",
      "position": 100,
      "revision": 42,
      "freshness": "fresh",
      "lastReceivedAt": "2026-07-13T10:00:01.123Z",
      "content": {
        "items": [
          {
            "id": "msg-123",
            "from": "Discord",
            "text": "有人找你",
            "severity": "info",
            "occurredAt": "2026-07-13T10:00:00.000Z",
            "receivedAt": "2026-07-13T10:00:01.123Z",
            "expiresAt": "2026-07-13T10:10:01.123Z"
          }
        ]
      }
    }
  ]
}
```

規則：

- 只回傳啟用 Source 的預設 Card。
- 不回傳已過期 Item。
- 沒有 State 的 `status`、`metric`、`key-value` Card 仍回傳，`content` 為 `null`，`freshness` 為 `empty`。
- `freshness` 只能是 `empty`、`fresh`、`stale`。
- `staleAfterSeconds=0` 時，有資料即為 `fresh`。
- 卡片依 `position`、`title`、`cardId` 穩定排序。
- message items 依 revision 新到舊排序。
- 回應不得包含原始 Token、Token Hash、未渲染未知欄位或管理專用設定。
- 加上 `Cache-Control: no-store`。

`freshness` 計算：

- 沒有有效 Item／State：`empty`。
- 有資料且 `staleAfterSeconds=0`：`fresh`。
- 有資料且 `now - lastReceivedAt <= staleAfterSeconds`：`fresh`。
- 有資料且超過 `staleAfterSeconds`：`stale`。
- message-feed 的 `lastReceivedAt` 取目前未過期 Items 中最新一筆；其他類型取 current State。

各 Card type 的 `content` 固定如下，不得回傳資料庫內部 envelope：

```json
{
  "type": "status",
  "content": {
    "status": "Build passed",
    "detail": "main · #184",
    "severity": "success",
    "occurredAt": "2026-07-13T10:00:00.000Z",
    "receivedAt": "2026-07-13T10:00:01.123Z",
    "expiresAt": "2026-07-13T11:00:01.123Z"
  }
}
```

```json
{
  "type": "metric",
  "content": {
    "value": 73.5,
    "unit": "%",
    "detail": "API quota used",
    "progress": 73.5,
    "severity": "warning",
    "occurredAt": "2026-07-13T10:00:00.000Z",
    "receivedAt": "2026-07-13T10:00:01.123Z",
    "expiresAt": null
  }
}
```

```json
{
  "type": "key-value",
  "content": {
    "items": [
      { "label": "Queue", "value": "12" },
      { "label": "Workers", "value": "4 online" }
    ],
    "occurredAt": "2026-07-13T10:00:00.000Z",
    "receivedAt": "2026-07-13T10:00:01.123Z",
    "expiresAt": "2026-07-13T10:10:01.123Z"
  }
}
```

最外層 Card 的 `type` 與 `content` 內可渲染欄位為唯一判斷依據；前端不得根據 payload 猜測 Card type。

## 13. 錯誤格式與狀態碼

所有新增 API 使用同一格式：

```json
{
  "error": {
    "code": "invalid_payload",
    "message": "text is required.",
    "fields": {
      "text": "required"
    }
  }
}
```

`fields` 沒有資料時可以省略。

| HTTP | code | 使用時機 |
|---:|---|---|
| 400 | `invalid_request` | JSON 無法解析或一般格式錯誤 |
| 400 | `invalid_payload` | 卡片 payload 驗證失敗 |
| 400 | `invalid_source_key` | 管理 API 收到不合法 Source Key；外部寫入一律使用 401 generic response |
| 400 | `immutable_field` | 嘗試修改不可修改欄位 |
| 401 | `invalid_source_token` | 缺少或錯誤 Source Token；不得透露 Source 是否存在 |
| 403 | `source_disabled` | Source 已停用 |
| 403 | `local_management_only` | 管理 API 非 PC 本機請求 |
| 404 | `source_not_found` | 已通過本機管理驗證後找不到 Source |
| 404 | `item_not_found` | 找不到待刪除 Item |
| 409 | `source_key_exists` | Source Key 已存在 |
| 409 | `source_limit_reached` | 已達 Host 50 個 Sources 上限 |
| 409 | `card_type_mismatch` | 對錯誤 Card type 呼叫 state/item 刪除 API |
| 413 | `payload_too_large` | 超過 MaxPayloadBytes |
| 415 | `unsupported_media_type` | 不是 application/json |
| 426 | `upgrade_required` | 遠端 HTTP 寫入未被允許 |
| 429 | `rate_limited` | Source 超過速率限制 |
| 503 | `custom_sources_unavailable` | SQLite 初始化或 migration 失敗 |

管理 API 的既有 Action Token 錯誤可以沿用現有 `RequireActionTokenAsync` 回應。

## 14. SQLite 儲存規格

### 14.1 位置與技術

資料庫固定放在：

```text
%LOCALAPPDATA%\VibeDeck\custom-sources\custom-sources.db
```

- 使用 `Microsoft.Data.Sqlite`。
- 不引入 Entity Framework Core。
- 使用 parameterized SQL，禁止字串串接輸入值。
- 啟用 foreign keys、WAL 與 5 秒 busy timeout。
- 每個寫入操作使用 transaction。
- 使用 `PRAGMA user_version` 管理 migration，第一版 schema version 為 1。

### 14.2 Schema v1

```sql
CREATE TABLE custom_sources (
  id TEXT PRIMARY KEY,
  source_key TEXT NOT NULL COLLATE NOCASE UNIQUE,
  display_name TEXT NOT NULL,
  token_hash TEXT NOT NULL,
  enabled INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  last_received_at TEXT NULL
);

CREATE TABLE custom_cards (
  id TEXT PRIMARY KEY,
  source_id TEXT NOT NULL,
  card_key TEXT NOT NULL,
  type TEXT NOT NULL,
  title TEXT NOT NULL,
  position INTEGER NOT NULL,
  stale_after_seconds INTEGER NOT NULL,
  default_ttl_seconds INTEGER NOT NULL,
  max_items INTEGER NOT NULL,
  revision INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(source_id, card_key),
  FOREIGN KEY(source_id) REFERENCES custom_sources(id) ON DELETE CASCADE
);

CREATE TABLE custom_items (
  card_id TEXT NOT NULL,
  item_key TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  occurred_at TEXT NULL,
  received_at TEXT NOT NULL,
  expires_at TEXT NULL,
  revision INTEGER NOT NULL,
  PRIMARY KEY(card_id, item_key),
  FOREIGN KEY(card_id) REFERENCES custom_cards(id) ON DELETE CASCADE
);

CREATE INDEX ix_custom_cards_position ON custom_cards(position, title);
CREATE INDEX ix_custom_items_expiry ON custom_items(expires_at);
CREATE INDEX ix_custom_items_revision ON custom_items(card_id, revision DESC);
```

所有時間以 UTC ISO 8601 round-trip 格式保存。`payload_json` 只保存已正規化、允許渲染的模型，不保存未使用未知欄位。

### 14.3 初始化失敗

- Custom Sources 初始化／migration 失敗時要記錄不含秘密的錯誤。
- 既有 Display、Sideboard、Quota 與配對功能仍必須能啟動。
- Custom Sources API 回 `503 custom_sources_unavailable`。
- 不得自動刪除、覆蓋或重建未知／損壞資料庫。

### 14.4 清理

新增 Hosted Service，每 `CleanupIntervalSeconds`：

- 刪除 `expires_at <= now` 的 Item。
- 再次保障每張 message-feed 不超過 `maxItems`。
- 若可見資料因此改變，增加相關 Card revision 並在 transaction commit 後發 `custom-card` SSE，reason 為 `expired`。
- 沒有任何 Source 或變更時不得產生持續磁碟寫入。

## 15. SSE 整合

目前 `DashboardEventHub` 使用 `Channel<string>`。實作需改成可攜帶 topic 與 data 的通知模型，但保持既有 topic 相容：

- `sideboard`
- `quota`
- `sync`
- 新增 `custom-card`

建議介面：

```csharp
Publish(string topic, object data = null)
```

SSE 格式：

```text
event: custom-card
data: {"cardId":"...","sourceKey":"chat","revision":42,"reason":"updated"}

```

`reason` 可為：`created`、`updated`、`deleted`、`cleared`、`expired`、`config`。

要求：

- data 必須使用 JSON serializer 產生，不能手動插入未跳脫字串。
- SSE 不傳外部訊息文字、Token 或完整 payload。
- 保留目前 bounded channel 與 DropOldest 行為。
- 保留初次連線的 `sync` 事件與自動重連。
- SSE 漏掉事件仍不影響正確性，因為 client 會抓完整快照。

## 16. 前端資訊架構

### 16.1 Sideboard 子頁面

不新增第四個頂層模式。現有「資訊板」中增加：

- `系統`：完全保留目前 CPU、RAM、GPU、天氣、工作脈搏頁面。
- `自訂`：顯示 Custom Cards。

使用小型 segmented control 或同等清楚的切換元件。選擇保存在：

```text
localStorage["vibeDeckSideboardPage"]
```

允許值只有 `system`、`custom`，預設 `system`。

不得在本次工作中將既有 CPU/RAM 等內建資訊重寫成 Custom Card。

### 16.2 自訂卡片顯示

- 使用新模組 `wwwroot/modules/custom-cards.js`。
- 不得把主要邏輯繼續堆入 `index.js`。
- Controller 至少提供 `refresh()`、`render(snapshot)`、`setVisible(isVisible)`。
- 卡片依 API 順序顯示。
- 空狀態顯示：「尚未建立自訂資料來源。請在 Host PC 建立來源。」
- 已建立但尚未收到資料的卡片仍顯示 title 與「等待第一筆資料」。
- `stale` 顯示低干擾的「資料可能已過期」標記，不刪除內容。
- `severity` 只能映射到固定 allowlist class，不能直接把外部字串設為 class。
- 所有外部字串使用 `textContent` 或等價安全 DOM API。
- 不使用 `innerHTML` 插入任何外部值。
- 長內容可換行但必須防止橫向 overflow；訊息不得撐破整個版面。
- message-feed 顯示新到舊；內容區可以垂直捲動。
- metric 的 `progress` 未提供時不顯示 progress bar。
- 三種既有 Sideboard skin 都必須保持文字可讀。

### 16.3 即時更新

在現有 `connectDashboardEvents()` 增加 `custom-card` listener。

- 一般瀏覽器 250ms 內合併連續通知後呼叫一次 `GET /api/custom-cards`。
- E-ink client 使用 8000ms 合併窗口。
- 頁面 hidden 時只標記 dirty，不發 request。
- 回到 visible 或切換到 Sideboard Custom 頁時立即 refresh。
- 保留 60 秒低頻 fallback refresh。
- 若所有 Card 的 `cardId + revision + freshness` signature 都未改變，不重建 DOM；避免不必要閃爍。

### 16.4 PC 本機管理 UI

管理 UI 只在 `deviceLocalRequest` 為真時顯示。手機與遠端登入使用者不顯示管理控制。

必須提供：

- Source 列表：名稱、key、type、enabled、last received、item count。
- 新增 Source 表單。
- 編輯名稱、標題、排序、stale、default TTL、maxItems、enabled。
- 上移／下移，不做拖拉。
- Token 輪替，需確認會使舊 Token 立即失效。
- 刪除，需二次確認並顯示 Source 名稱。
- 建立／輪替成功後的一次性 credential panel：endpoint、Token、Authorization header、PowerShell 與 curl 範例、複製按鈕。

一次性 credential panel 關閉後，不得從前端狀態、localStorage、DOM hidden field 或後端重新取得舊 Token。

PowerShell 範例至少包含：

```powershell
$headers = @{ Authorization = "Bearer <source-token>" }
$body = @{
  id = "msg-123"
  from = "Discord"
  text = "有人找你"
  timestamp = "2026-07-13T18:00:00+08:00"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "https://host:5443/api/custom-sources/chat/events" `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body
```

## 17. 建議程式檔案邊界

後端不得把所有新程式塞進既有 `Startup.cs`。

預期新增：

```text
src/VibeDeck.Host/CustomSources/
  CustomSourceModels.cs
  CustomSourceOptions.cs
  CustomSourceStore.cs
  CustomSourceService.cs
  CustomSourceCleanupService.cs
  CustomSourceValidation.cs
src/VibeDeck.Host/Startup.CustomSources.cs
src/VibeDeck.Host/wwwroot/modules/custom-cards.js
tests/VibeDeck.Host.Tests/
scripts/verify-custom-sources.ps1
```

責任：

- `CustomSourceStore`：SQLite、migration、transaction、query，不包含 HTTP。
- `CustomSourceService`：token、rate limit、正規化、業務規則、revision、通知協調。
- `CustomSourceValidation`：建立設定與四種 payload 的驗證。
- `CustomSourceCleanupService`：TTL、retention、過期通知。
- `Startup.CustomSources.cs`：endpoint mapping、HTTP status、request/response DTO；只保留薄層。
- `custom-cards.js`：快照、DOM renderer、PC 管理互動；不得接管既有 Sideboard stats controller。

可依實際責任微調檔名，但不得重新形成單一巨型檔案。

## 18. 服務註冊與設定

`ConfigureServices` 必須註冊：

- `CustomSourceOptions` 綁定 `CustomSources`。
- `CustomSourceStore` singleton。
- `CustomSourceService` singleton。
- `CustomSourceCleanupService` hosted service。

Custom Sources 不得依賴虛擬顯示驅動、FFmpeg、Glance Board 或 AI quota 可用性。

## 19. 自動化測試要求

新增 `tests/VibeDeck.Host.Tests` 並加入 `VibeDeck.sln`。測試可使用 xUnit；資料庫一律使用每個 test case 獨立的 temporary directory，不得碰使用者 `%LOCALAPPDATA%`。

至少覆蓋：

1. Source Key 正規化、保留字、長度與重複衝突。
2. Token 只保存 hash，正確 Token 通過，錯誤 Token 失敗。
3. Token 輪替後舊 Token 立即失效，新 Token 有效。
4. message-feed 首次 insert。
5. 同一 message id 重送為 update，Item 數量不增加，revision 增加。
6. message-feed 超過 maxItems 後移除最舊項目。
7. status／metric／key-value 每次 POST 取代 current state。
8. 四種 payload 的 required、長度、severity、timestamp、TTL、progress 驗證。
9. TTL 使用 receivedAt 而不是外部 timestamp。
10. cleanup 移除過期 Item 並產生通知。
11. disabled Source 不接受寫入且不出現在快照。
12. 刪除 Source cascade 清除 Card 與 Items。
13. Host／Store 重建後資料仍可讀，證明持久化有效。
14. 快照排序、fresh／stale／empty、過期過濾正確。
15. Rate limit 依 Source 隔離，一個 Source 超限不影響另一個。
16. Store 初始化失敗時 Custom Sources 回 unavailable，但既有 Host service 可建立。
17. SSE notification 不包含 payload 或 Token。
18. 外部 payload 中的 `<script>`、事件 handler 字串只被視為純文字。

時間相關測試應注入 clock 或等價可控時間來源，不得依賴長時間 `Task.Delay`。

## 20. 端對端驗證腳本

新增：

```text
scripts/verify-custom-sources.ps1
```

腳本假設 Host 已在 `http://127.0.0.1:5000` 執行，並必須：

1. 從 `/api/session` 取得 Action Token。
2. 建立唯一名稱的 message-feed 測試 Source。
3. 使用回傳 Source Token POST 一筆訊息。
4. 從 `/api/custom-cards` 找到該卡與該訊息。
5. 重送相同 id，確認只有一筆且 revision 增加。
6. 使用錯誤 Token，確認為 401。
7. 輪替 Token，確認舊 Token 401、新 Token 可寫入。
8. 刪除 Item，確認快照不再包含。
9. 在 `finally` 刪除測試 Source。
10. 成功時 exit code 0；任何 assertion 失敗時 exit code 非 0。

腳本不得把完整 Token 印到 console，也不得在失敗後遺留測試 Source。

## 21. 視覺與手動驗證

實作者完成後必須實際啟動 Host，檢查：

### PC 1440×900

- 能建立、編輯、排序、停用、輪替、刪除 Source。
- 一次性 Token panel 不超出畫面，長 endpoint 可複製。
- 管理 UI 不遮住既有 Display、Sideboard、Quota 操作。

### 手機橫向 812×375（iPhone XS 目標）

- `系統／自訂` 切換可操作。
- 四種卡片不產生水平 scrollbar。
- 訊息長文、中文、emoji、長英文單字不破版。
- POST 後不重整頁面即可更新。

### 手機直向 375×812

- 卡片改為可讀的單欄或等價布局。
- 管理 UI 不出現。
- 空狀態、等待資料、stale 都可辨認。

### 回歸

- Display Mode 的 WebRTC/JPEG 連線不受影響。
- 現有系統資訊板仍正常刷新。
- Quota 頁、裝置配對、遠端登入與 PWA shell 不受影響。
- 三種 Sideboard skin 仍可讀。

若有可用的瀏覽器自動化工具，應保留至少 PC 與 812×375 的驗證截圖供交付說明使用。

## 22. 效能與行為要求

- 一般可見手機：POST 成功到畫面更新目標小於 1 秒。
- E-ink：允許最多 8 秒合併更新。
- 連續 30 次 burst 不應造成 30 次完整 DOM rebuild。
- `GET /api/custom-cards` 在 50 Sources、每個 message-feed 50 Items 的上限資料量下仍須可用。
- 沒有 SSE subscriber 時，不因 Custom Sources 產生高頻 polling。
- 沒有過期資料時，cleanup 不持續寫 DB 或發通知。

## 23. 文件要求

完成實作時同步更新：

- `README.md`：新增「自訂資料來源」快速開始與安全提醒。
- `docs/protocol.md`：記錄新增 endpoints、認證與 SSE event。
- `docs/product-architecture.md`：將 Custom Sources 加入 Host-to-phone panel architecture。
- `docs/product-review.md`：列出驗收結果與已知限制。

文件必須明確說明：

- localhost/LAN/Tailscale 可達性與 HTTPS 要求。
- 一般雲端 SaaS 無法直接呼叫 NAT 後方 Host。
- 不建議直接把 VibeDeck port 暴露到公開 Internet。
- Source Token 洩漏時如何輪替。

## 24. 實作順序

請依序進行，避免先做 UI 後補安全與資料語意：

1. Models、Options、clock abstraction、validation。
2. SQLite store、migration、temporary DB tests。
3. Source Token、Source/Card CRUD、ingest、revision、retention。
4. Cleanup hosted service。
5. HTTP endpoints 與錯誤格式。
6. DashboardEventHub structured data 與 SSE 相容修改。
7. `GET /api/custom-cards` 與自動化測試。
8. `custom-cards.js` 安全 renderer 與即時 refresh。
9. PC 管理 UI 與一次性 credential panel。
10. PowerShell 端對端腳本。
11. 視覺驗證、既有功能回歸、文件更新。

## 25. 必跑驗證指令

從 repo root 執行：

```powershell
dotnet restore VibeDeck.sln
dotnet build VibeDeck.sln -c Debug
dotnet test VibeDeck.sln -c Debug --no-build
```

啟動 Host 後：

```powershell
scripts\verify-custom-sources.ps1
```

最後再執行：

```powershell
git status --short
git diff --check
```

不得提交：

- `bin/`
- `obj/`
- SQLite DB、WAL、SHM。
- Token、測試 credentials、LocalAppData 內容。
- 驗證截圖以外的臨時檔；若截圖不打算成為產品文件，也不要提交。

## 26. Definition of Done

以下全部成立才能宣稱完成：

- 四種卡片的建立、寫入、顯示、更新、清除／刪除、stale、TTL 行為均符合本規格。
- Source Token 不以明文持久化，輪替與撤銷可驗證。
- 非本機 HTTP 的安全政策與設定開關可驗證。
- SQLite migration、持久化、cleanup、retention 可驗證。
- SSE 漏失／重連不影響最終正確狀態。
- 手機 renderer 沒有外部資料 `innerHTML` 注入路徑。
- PC 管理 UI 不會出現在手機或非本機遠端頁面。
- 自動化測試全部通過。
- `verify-custom-sources.ps1` 通過。
- PC、812×375、375×812 完成視覺檢查。
- Display、既有 Sideboard、Quota、配對完成回歸檢查。
- 所有要求文件已更新。
- `git diff --check` 無錯誤，worktree 沒有 build artifact 或 secrets。

## 27. 交付回報格式

實作者最後回報必須包含：

1. 完成的功能摘要。
2. 實際修改／新增的主要檔案。
3. 資料庫位置與 migration 版本。
4. API 與安全決策是否完全符合本規格；若有偏差逐項列出原因。
5. 執行過的 build、test、PowerShell 驗證命令與結果。
6. 視覺驗證尺寸與結果。
7. 仍存在的已知問題或未完成項目。

不得只回報「已完成」或只附編譯成功。若任一 Definition of Done 未達成，狀態必須寫成「未完成」並列出剩餘工作。

## 28. 實作者不可自行變更的決策

- 不新增另一條 WebSocket；使用既有 SSE。
- 不讓外部 payload 控制 HTML、CSS、JS、URL、Card ID 或 layout。
- 不重用 Device Token、Action Token 或遠端登入 cookie 當 Source Token。
- 不把 Source Token 放在 query string 或瀏覽器 storage。
- 不把 SQLite 改成純記憶體或未同步的 JSON persistence。
- 不為了測試方便關閉 HTTPS、Source Token 或本機管理限制。
- 不把新後端 endpoint 全部塞進 `Startup.cs`。
- 不把新前端主要邏輯繼續塞進 `index.js`。
- 不順便重寫現有系統資訊板、Quota 或 Display Mode。

若其中一項確實無法實作，先回報阻礙與替代方案，取得決定後再繼續。
