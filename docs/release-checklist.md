# VibeDeck 發佈與更新檢查表

這份文件只描述目前正式產品路線。Windows 產品只發布 Setup；手機只使用瀏覽器／PWA。

## 1. 來源檢查

```powershell
scripts\test-product-flow.ps1 -Source
```

必須通過：

- Release 單元測試（命令會先 restore，乾淨 runner 不得以零測試假綠）。
- Managed connector Worker 測試。
- 所有 Web JavaScript 語法檢查。
- 安裝／更新與 payload 內 PowerShell 語法檢查。
- 不存在原生手機 App、portable ZIP 或 Host Windows Service 產品路徑。

## 2. 建立唯一正式安裝包

版本預設來自 Host csproj：

```powershell
scripts\package-windows-setup.ps1
```

或指定新版本：

```powershell
scripts\package-windows-setup.ps1 -Version 0.1.31
```

確認 `artifacts\windows-setup\VibeDeck-Setup-<version>.exe` 已建立。打包腳本會再次驗證 staged payload。

## 3. 全新安裝驗收

在沒有 VibeDeck 的 Windows 使用者帳號執行 Setup：

1. 安裝後 `C:\Program Files\VibeDeck` 存在。
2. `%ProgramData%\VibeDeck` 建立且一般使用者可寫入產品狀態。
3. `VibeDeckHost` Windows Service 不存在。
4. Host 在登入使用者 Session 執行，沒有可見 console。
5. 開始功能表／桌面入口能啟動 Host 並開啟 `http://127.0.0.1:5000`。
6. 登出再登入後 Host 自動啟動。
7. 執行：

```powershell
scripts\test-product-flow.ps1 -Installed
```

## 4. 覆蓋更新驗收

先用舊版建立以下狀態：

- 至少一台已配對手機。
- 已建立 HTTPS 根憑證。
- 至少一個 Codex／AGY 帳號狀態。
- 自訂卡片與 dashboard layout。
- Windows 通知已啟用。

直接執行新版 Setup，不先解除安裝。確認：

1. 舊 Host 被關閉，新 Host 正常取得 5000／5443。
2. 舊 `VibeDeckHost` Service 若存在會在覆蓋檔案前移除。
3. 已刪除的 `wwwroot` 模組不會殘留。
4. `%ProgramData%\VibeDeck` 的配對、憑證、額度、自訂卡片與 layout 均保留。
5. Host SessionId 大於 0，API 不回傳 `WinDisc`。
6. 若已安裝虛擬顯示器：

```powershell
scripts\test-product-flow.ps1 -Installed -RequireVirtualDisplay
```

## 5. Windows 通知 companion

通知 MSIX 是選用、獨立更新的 companion：

```powershell
scripts\package-windows-notifications.ps1 -Install
```

確認：

- 程序名稱為 `VibeDeck.Notifications.exe`。
- companion 不監聽 5000／5443。
- `/api/windows-notifications/status` 顯示 `CompanionConnected=true`、`AccessStatus=Allowed`。
- 發出真實 Windows toast 後，「活動動態」收到通知。

## 6. 虛擬顯示器與串流

1. 無驅動乾淨機測試：取消 UAC、正常安裝、下載／驗證失敗。
2. 驅動已安裝機器確認 Windows 顯示設定出現延伸桌面。
3. `/api/displays` 的 VibeDeck output 有實際解析度，且沒有 `WinDisc`。
4. Android／iPhone 實測 WebRTC H.264；阻擋 H.264 時確認 JPEG fallback。
5. 斷網再恢復，串流能重連且空狀態不殘留。

## 7. 手機 UI

全部使用 Host URL，不安裝手機 App：

- Android Chrome／PWA：直向、橫向、全螢幕、輸入、長亮。
- iPhone Safari／主畫面：HTTPS、憑證、全螢幕替代流程、長亮。
- BOOX：電子書模式、高對比、大字、旋轉、資訊板／額度切換。
- 手機資訊板：第一屏、活動內卷軸、自動捲到最新、額度切換、自訂順序。

## 8. 交付前

- 更新 `CHANGELOG.md`。
- 確認 Setup 版本與 Host assembly 版本一致。
- 確認 README 的新安裝／更新命令仍可直接複製執行。
- 保留一份全新安裝與覆蓋更新的測試紀錄。
