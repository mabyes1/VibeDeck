# Windows 通知橋接

Windows Notification Listener 需要有套件身分並宣告 `userNotificationListener` capability。主程式 `VibeDeck.Host.exe` 維持一般桌面程式；只有通知橋接器 `VibeDeck.Notifications.exe` 放進 MSIX，兩者不要混成同一個啟動流程。

## 建置與安裝

在 PowerShell 執行：

```powershell
.\scripts\package-windows-notifications.ps1 -Install
```

腳本會：

1. 發布並精簡通知橋接器。
2. 建立及簽署 `artifacts\windows-notifications\VibeDeck.WindowsNotifications.msix`。
3. 自動遞增已安裝的套件版本並覆蓋更新。
4. 設定登入自啟，並立即啟動橋接器。

套件識別名稱使用 `VibeDeck.Dev`，與新的產品線一致。

若出現 `0x800B0109` 或憑證鏈不受信任，請用系統管理員 PowerShell 執行：

```powershell
.\scripts\package-windows-notifications.ps1 -RegisterOnly -InstallCertificateMachine
```

這只會把本專案開發憑證匯入本機的受信任憑證區。移除橋接器：

```powershell
.\scripts\package-windows-notifications.ps1 -Uninstall
```

第一次啟用時 Windows 會要求通知存取權。資訊面板應以 Host 的 `/api/windows-notifications/status` 顯示橋接器是否已連線及權限是否允許；不要用假通知判斷橋接是否正常。
