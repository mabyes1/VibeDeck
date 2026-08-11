# 遠端桌面影像技術筆記

> 狀態：2026-07-11 已在 Windows Host + Chromium 完整驗證；iOS Safari 已完成 WebRTC H.264 路徑，需以實機持續收集統計資料。

這份文件記錄 VibeDeck 的低延遲遠端影像方案，作為日後拆成獨立「遠端桌面」專案時的技術基線。這裡的目標不是影片播放，而是把 Windows 桌面當成互動式畫面傳到 Android、iOS 或電子紙裝置：寧可丟掉舊畫面，也不要排隊播放過時畫面。

## 目前架構

```text
Windows display
  └─ DisplayFrameSource
      ├─ DXGI Desktop Duplication（實體螢幕）
      └─ GDI fallback（虛擬顯示器 / DXGI 不可用）
          ↓ BGRA raw frames
H264AnnexBStreamer
  └─ ffmpeg
      ├─ h264_nvenc（NVIDIA，優先）
      ├─ h264_qsv（Intel）
      ├─ h264_amf（AMD）
      └─ libx264（CPU fallback）
          ↓ Annex-B access units
WebRtcH264Service / SIPSorcery
  └─ RTP 90 kHz → DTLS/SRTP → WebRTC
      ↓
Safari / Chrome / Android MediaCodec
```

JPEG WebSocket 仍是可靠 fallback，但不應作為主要互動式影像傳輸：逐張 JPEG 沒有連續時間軸、瀏覽器解碼與 Blob 管理成本高，網路稍有堆積就會看到舊畫面延遲播放。

## 已確認的關鍵原則

### 1. WebRTC H.264 必須使用正確的 RTP 時間軸

H.264 RTP 的 clock rate 是 90,000。`SendH264Frame` 的 duration 會直接推進 RTP timestamp，因此不能只用「目標 FPS 的固定 duration」假設每個 access unit 都準時送出。擷取、編碼或 pipe 寫入若發生停頓，固定 timestamp 會令接收端誤判抖動，Safari 的 jitter buffer 會開始囤積。

目前做法：第一幀使用 nominal duration，後續依前一個 access unit 實際送出的 wall-clock 間隔計算 duration，並限制在合理範圍內。

### 2. 擷取排程使用絕對 deadline

錯誤模式是：每幀做完擷取和寫入後，再等待一個 frame interval。這會把工作時間加進 interval；例如 60 FPS 的 16.7 ms，加上 25 ms 工作時間，實際只剩約 24 FPS。

目前做法是維持 `nextDue` 絕對時間軸，完成一幀後只推進下一個 deadline。若某次嚴重落後，跳過錯過的 slot，避免恢復時產生 burst。

### 3. 遠端桌面使用 latest-frame-wins

互動式桌面不需要保留所有影格。滑鼠或畫面狀態改變時，最新畫面比完整播放中間每個畫面更重要。任何未來的傳輸層都應具備：

- bounded queue（最多一個待送最新影格）
- 丟棄舊 frame，不讓延遲無限增長
- keep-alive 或 keyframe 機制，確保靜止畫面仍能恢復

### 4. 硬體編碼器探測必須使用有效桌面尺寸與 YUV420

極小的 16×16 RGB probe 會被 NVENC/QSV/AMF 拒絕，即使硬體編碼器本身完全正常。探測應使用至少 1280×720、30 FPS、`format=yuv420p` 的短測試。否則會錯誤 fallback 到 CPU，造成高延遲與低 FPS。

## 目前低延遲編碼設定

- 輸入：raw BGRA
- 輸出：Annex-B H.264、`yuv420p`
- 硬體 encoder：NVENC → QSV → AMF → libx264
- B-frame：關閉
- lookahead / async queue：盡量降低
- NVENC surfaces：2
- VBV buffer：大約 2 個 frame 的 bitrate，而非半秒以上的 buffer
- GOP：目前約 1 秒；日後可依封包遺失率調整到 0.5 秒
- WebRTC：單向 recvonly video track，使用標準 RTP/SRTP

畫面解析度、FPS、quality 仍由客戶端的串流設定控制。實際 bitrate 應依解析度、FPS、quality 和網路狀況估算，不能盲目追求 JPEG 等級的高峰值。

## iOS / Safari 注意事項

Apple WebKit 說明 Safari 以 H.264 作為 WebRTC 預設 codec，原因是 Apple 硬體支援與即時通訊功耗最佳化；因此 iOS H.264 卡頓時，優先檢查送端時間戳、編碼 queue、網路 jitter 和 receiver buffer，不要先假設是 Safari 解碼能力不足。

目前前端會在可用時嘗試：

- `RTCRtpReceiver.jitterBufferTarget = 0`
- `playoutDelayHint = 0`
- 每秒讀取 `getStats()`
- 顯示 decoded FPS、Mbps、jitter、jitter buffer、decode time、dropped frames

較新的 Safari 才開始提供更完整的 `targetLatency` / `jitterBufferTarget` 控制，因此必須 feature-detect，不能直接依賴這些欄位。

## 驗證方式

### Host build

```powershell
dotnet build src\VibeDeck.Host\VibeDeck.Host.csproj
node --check src\VibeDeck.Host\wwwroot\index.js
```

### WebRTC smoke test

```powershell
npx playwright screenshot `
  --viewport-size="1024,576" `
  --wait-for-timeout=12000 `
  'http://127.0.0.1:5000/index.html?mode=display&webrtc=1&viewer=1' `
  outputs\webrtc-h264-smoke.png

Invoke-RestMethod http://127.0.0.1:5000/api/stream/capabilities |
  ConvertTo-Json -Depth 8
```

驗證重點：`encoder` 應為 `ffmpeg/h264_nvenc`（若主機有 NVIDIA），`RecentQueuedFps` 應接近設定 FPS，`RecentSkippedFps` 應為 0，`LastError` 應為 null。

### iOS 實機驗證

在 iPhone 上開啟顯示模式，等待狀態列出現 H.264 統計。至少記錄以下資料：

1. decoded FPS
2. jitter 與平均 jitter buffer
3. decode time
4. dropped frames
5. 同一 Wi‑Fi 下與 JPEG 的主觀操作延遲

如果 Host 的 `RecentQueuedFps` 正常，但 iPhone FPS 低，問題在網路或 receiver；如果 Host FPS 已低，問題在擷取或編碼。

## 日後拆成獨立遠端桌面專案的邊界

目前 VibeDeck 特有的部分應留在上層：配對、裝置信任、顯示器模式、額度面板、電子紙 CSS、觸控映射。可抽出的核心模組是：

- `DesktopCapture`：DXGI / GDI source
- `FrameScheduler`：absolute cadence + latest-frame-wins
- `H264Encoder`：硬體選擇、低延遲參數、keyframe 控制
- `H264AccessUnit`：Annex-B parsing / AUD 分組
- `RtpVideoSender`：90 kHz timestamp、payload fragmentation、PLI/FIR 回應
- `WebRtcSession`：offer/answer、ICE、DTLS/SRTP、session lifecycle
- `ReceiverTelemetry`：getStats 與延遲診斷

拆分時應讓每個 WebRTC session 擁有自己的 metrics、encoder process 和 cancellation token；目前 metrics 還是 Host singleton，單一裝置使用沒問題，但多裝置遠端桌面化前必須改成 per-session。

## 已知待辦

- 加入 WebRTC PLI/FIR 後即時送出 IDR/keyframe。
- 將 metrics 從 singleton 改為 per-session。
- 實作多裝置在線清單與主螢幕接管。
- 研究 Windows Desktop Duplication 對虛擬顯示器的直接擷取，減少 GDI copy。
- 以實際 iPhone、Android、GO Color 7 各做一組延遲與耗電基準。
- 把 signalling、認證與遠端輸入權限獨立成遠端桌面協定。

## 參考資料

- [WebKit: On the Road to WebRTC 1.0, Including VP8](https://webkit.org/blog/8672/on-the-road-to-webrtc-1-0-including-vp8/)
- [WebKit: New WebKit Features in Safari 12.1](https://webkit.org/blog/8718/new-webkit-features-in-safari-12-1/)
- [WebKit: News from WWDC26 — WebKit in Safari 27 beta](https://webkit.org/blog/17967/news-from-wwdc26-webkit-in-safari-27-beta/)
- [SIPSorcery RTPSession source](https://raw.githubusercontent.com/sipsorcery-org/sipsorcery/v5.2.3/src/net/RTP/RTPSession.cs)
