# VibeDeck 產品更新

## 使用者流程

1. 在 **PC 本機** 開啟 VibeDeck，不在手機或遠端瀏覽器操作。
2. 按右上角「檢查更新」。
3. 有新版時，按「安裝 vX.Y.Z」。
4. 等待下載與 SHA-256 驗證完成；Windows Setup 會自行開啟。
5. 若 Windows 要求管理員權限，按「是」。Setup 會短暫停止 Host、更新檔案、保留 `%ProgramData%\VibeDeck` 的配對與版面資料，最後重新啟動 Host。

更新不是靜默安裝：使用者必須在 PC 上主動確認，且 Windows 仍會保有它應有的權限提示。

## 信任範圍

- 只查詢 `https://api.github.com/repos/mabyes1/VibeDeck/releases/latest`。
- 只接受 `github.com/mabyes1/VibeDeck` Release 的 `VibeDeck-Setup-X.Y.Z.exe`、同名 `.sha256` 與（若有發佈）同名 `.sig`。
- 安裝檔下載完成後，Host 會以「拒絕寫入」的檔案握把鎖住最終檔案，先驗證再啟動，驗證與啟動之間檔案無法被竄改（消除 TOCTOU）。
- 驗證層級（`src/VibeDeck.Host/Updates/ProductUpdateOptions.cs` 是唯一的政策開關位置）：
  1. **SHA-256 比對**（一律執行）— 只防傳輸損毀。`.sha256` 與安裝檔同源，無法防範發佈端被入侵。
  2. **離線金鑰分離簽章**（`.exe.sig`，ECDSA P-256）— 以 App 內釘選的公開金鑰驗證，可防範 GitHub 帳號 / CI token 被盜後發佈惡意安裝檔。`RequireSignedUpdates` 為 `true` 時缺簽章或簽章無效一律中止（錯誤碼 `signature_missing` / `signature_invalid` / `signature_key_missing`）；目前預設 `false`（舊版 Release 未簽章），但只要 Release 有 `.sig` 且金鑰已釘選，簽章無效仍會中止。
  3. **Authenticode（WinVerifyTrust）** — 安裝檔若帶有 Authenticode 簽章，簽章必須有效（`authenticode_invalid`），且若已釘選發行者指紋則必須相符（`authenticode_untrusted_publisher`）。未簽章的安裝檔目前仍接受（正式程式碼簽章尚未導入），並記入稽核日誌。
- 更新 API 只接受 `127.0.0.1` 本機請求與 VibeDeck 動作權杖；已配對手機、LAN 裝置與遠端登入不能觸發 PC 更新。

SHA-256 保護傳輸與發佈資產的一致性；正式商業發佈前仍應設定 Windows 程式碼簽章，降低 SmartScreen 的未知發行者提示。

### 啟用強制簽章驗證（發佈負責人待辦）

1. `pwsh scripts/sign-release.ps1 -GenerateKey -KeyPath <離線路徑>` 產生金鑰對；私鑰務必離線保存，絕不放進 repo 或 CI。
2. 把印出的公開金鑰 PEM 貼進 `ProductUpdateOptions.PinnedReleaseSigningPublicKeyPem`。
3. 之後每次發佈都以 `scripts/sign-release.ps1 -Sign`（或 `package-windows-setup.ps1 -SigningKeyPath ...`）產生 `.sig` 並隨 `.exe`、`.sha256` 一起上傳。
4. **至少一個已簽章的 Release 上線後**，把 `RequireSignedUpdatesDefault` 改為 `true`。太早翻開會讓已升級到強制版的使用者無法再更新。

## 發佈者流程

1. 更新 `src/VibeDeck.Host/VibeDeck.Host.csproj` 的版本與 `CHANGELOG.md`。
2. 提交後建立並推送符合版本的 tag，例如 `v0.1.18`。
3. GitHub Actions `Release Windows Setup` 會建置單一 `VibeDeck-Setup-X.Y.Z.exe`、生成 `.sha256`，並建立 GitHub Release。
   本機打包（`scripts/package-windows-setup.ps1`）也會自動生成 `.sha256`，並可用 `-SigningKeyPath` 直接產生 `.sig`。
   `.sig` 必須用離線私鑰產生，CI 沒有（也不該有）這把金鑰：CI Release 建立後，
   下載 `VibeDeck-Setup-X.Y.Z.exe`，在放有離線金鑰的機器執行
   `pwsh scripts/sign-release.ps1 -Sign -KeyPath <離線金鑰> -InstallerPath <下載的 exe>`，
   再以 `gh release upload vX.Y.Z <exe>.sig` 附加到同一個 Release。
4. 使用者之後按「檢查更新」即可取得該 Release；不必下載或執行任何 `.ps1`、`.vbs` 或 `.cmd`。

## 語言規則

- Windows Setup 依 Windows 顯示語言自動預選繁體中文、English 或日本語；不支援的語言回退英文。
- PC、手機與 BOOX 的網頁 App 各自依該瀏覽器語言選擇介面，仍可在右上角手動切換。
- 因此 PC 用繁中、手機用英文或日文是正常且預期的行為。
