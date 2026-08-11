# VibeDeck

[English](README.md) · **繁體中文**

![VibeDeck 功能總覽](docs/assets/social-preview0.png)

> **讓你的閒置螢幕有一份新工作。**
>
> VibeDeck 是 Windows Host，能把另一塊可開瀏覽器的螢幕變成**真正的 Windows 顯示器**，或**瀏覽器原生、可自由改造的 Deck**。
>
> **Host：Windows 10/11 · Client：現代瀏覽器 · 副裝置不用安裝 App。**

VibeDeck 是一個 Windows Host，讓閒置的手機、平板、電子紙裝置、筆電，以及其他能跑瀏覽器的螢幕重新派上用場。同一台裝置可以在兩條完全不同的路徑之間切換：

- **Display**：透過網路顯示真正的 Windows 顯示器畫面。
- **Deck**：直接在裝置瀏覽器裡渲染專門設計的 HTML/CSS/JS 介面。

這兩條渲染路徑可以拿來做完全不同的工作。VibeDeck 內建就能把閒置螢幕變成真正的 Windows 顯示器、自訂 HTML Deck、AI 額度監看器，或整合 Windows 通知的系統資訊板。

## 一塊閒置螢幕，四種實際用途

| | 可以做什麼 |
|---|---|
| **真正的 Windows 顯示器** | 把真正的 Windows 應用程式放到閒置螢幕上。VibeDeck 使用真實 Windows 顯示器、透過 WebRTC 串流，並把輸入操作送回 Windows。 |
| **可自由改造的 HTML Deck** | 放入 `index.html`、CSS 與 JavaScript，就能做出針對這塊螢幕量身打造的介面，直接由裝置瀏覽器原生渲染。 |
| **AI 額度一眼看完** | 不用另外開 App，就能常駐查看 Codex、Claude、AGY 的百分比、重置時間與剩餘 Credits。 |
| **資訊板 + Windows 通知** | CPU、RAM、GPU、VRAM、活動、任務與 Windows 桌面通知放在同一塊一眼就能讀完的螢幕上。 |

所以 VibeDeck 不只是螢幕鏡像，也不只是一個 Dashboard。它比較像是一個小螢幕 Runtime，讓 Windows 畫素串流和瀏覽器原生介面可以共存在同一台裝置上。

## 真正跑 Windows App，不是假螢幕

把閒置裝置當成真正的 Windows 顯示器。VibeDeck 會建立或找到 Windows 顯示器、擷取畫面、串流到瀏覽器，也能把輸入操作送回 Windows。一般 Windows 應用程式可以像移到普通第二螢幕一樣，直接移到 VibeDeck 顯示器上。

![VibeDeck 真正的 Windows 顯示器](docs/assets/social-preview2.png)

適合：

- 把一般 Windows App 固定放在一個專用小螢幕上
- 已經有桌面版軟體的 Dashboard
- 臨時需要第二螢幕的工作情境
- 不方便或根本不能安裝原生 Client App 的裝置

## 額度、系統狀態、Windows 通知，一眼看完

內建資訊板就是為了那些「一直看得到才有價值」的資訊：AI 額度、系統遙測、活動、任務，以及由選用通知 Companion 整合進來的 Windows 通知。

![VibeDeck 資訊板、AI 額度、系統狀態與 Windows 通知](docs/assets/social-preview3.png)

它不是把桌面 Dashboard 硬縮到小螢幕。Browser-native 版面可以依手機、平板與電子紙尺寸重新排列，讓真正重要的數字保持可讀。

## 做一塊真正屬於自己的螢幕

直接在閒置螢幕上執行瀏覽器原生體驗。一個 Deck 本質上就是一個包含 manifest 和一般網頁檔案的資料夾。

**只要它能做成網頁，就能做成 Deck。**

Deck 完全不需要影片編碼，可以直接以裝置原生解析度顯示，也很容易檢查、修改和替換。放入 `index.html`，加上想要的 CSS 與 JavaScript，VibeDeck 就會自動探索這個 Deck。

這個 repository 內附了一個很小的 **Coding Pet** 範例 Deck。

![VibeDeck 自訂 HTML Deck](docs/assets/social-preview1.png)

## 為什麼是 VibeDeck

- **Windows Host、瀏覽器 Client。** Host 跑在 Windows 10/11；手機、平板、BOOX、macOS/Linux 電腦和其他能跑瀏覽器的螢幕都共用同一套 Web Client。
- **真正的 Windows 顯示器。** Display 模式使用 Windows 顯示器列舉，不是拿一塊假 Canvas 冒充螢幕。
- **天生可改。** Deck 就是普通的 HTML、CSS 和 JavaScript。
- **一台裝置，多種工作。** 同一台裝置可以切換 Display、Sideboard、Quota、Custom Deck，以及其他專門用途的介面。
- **本機掌握信任權限。** 即使啟用遠端連線，配對與裝置信任仍由 Windows Host 控制。
- **副裝置不用安裝程式。** 瀏覽器就是 Client。

## 內建 Deck 與介面

VibeDeck 目前包含幾種針對小螢幕設計的體驗：

- **Display**：Windows 畫面串流與遠端輸入
- **Sideboard**：系統狀態、活動、任務、AI 額度與 Windows 通知歷史
- **Quota**：專門查看 AI 使用量與帳號額度資訊
- **Custom Decks**：使用者自行製作的瀏覽器原生介面
- **Remote access**：在區域網路之外連回已信任的 Host

這些都是建立在 VibeDeck 上的應用，而不是 VibeDeck 本身的定義。真正的平台是 Windows Host、Display path、Browser Runtime、Trust Model 與 Deck Model 的組合。

## 建立自己的 Deck

Custom Deck 放在：

```text
C:\ProgramData\VibeDeck\Decks\<deck-id>\
```

最小的 Deck 結構如下：

```text
my-deck/
├─ deck.json
├─ index.html
├─ style.css
└─ app.js
```

`deck.json` 範例：

```json
{
  "name": "My Deck",
  "entry": "index.html",
  "icon": "✨"
}
```

VibeDeck 會自動探索有效的 Deck 資料夾。目前的擴充模型可參考 `docs/custom-data-sources-spec.md`，以及內附的 `src/VibeDeck.Host/DeckExamples/coding-pet/` 範例。

## 快速開始

### Windows 安裝

一般使用者的正式安裝路徑是 Windows Setup。Release build 會產生：

```text
VibeDeck-Setup-<version>.exe
```

請從已登入的 Windows 桌面執行安裝程式，這樣 Host 才會註冊並啟動在目前互動使用者的 Session。VibeDeck 的持久化資料會放在：

```text
C:\ProgramData\VibeDeck
```

安裝完成後，在 Windows PC 上開啟 VibeDeck，依照連線流程讓閒置裝置加入。本機探索會先從 HTTP 開始，正常使用時再把裝置導向 HTTPS。

### 從原始碼建置

需求：

- Windows 10/11
- .NET 8 SDK
- Node.js，用於 Browser / Worker 測試
- Inno Setup 6，用於建立 Windows Installer

Restore 並驗證：

```powershell
dotnet restore VibeDeck.sln
dotnet build VibeDeck.sln -c Release --no-restore
pwsh scripts/test-product-flow.ps1 -Source
```

建立 Windows Setup：

```powershell
pwsh scripts/package-windows-setup.ps1
```

## 運作方式

VibeDeck 有兩條渲染路徑。

```text
Display / Pixel Path
Windows app
   ↓
Windows display
   ↓
capture + H.264/WebRTC
   ↓
browser

Deck / DOM Path
Deck HTML/CSS/JS
   + Host data/events
   ↓
browser-native rendering
```

Windows Host 負責顯示器探索、串流、配對、裝置信任、Deck 探索、本機資料服務，以及可選的遠端連線。副裝置只負責瀏覽器 UI。

更深入的架構文件：

- `docs/product-architecture.md`
- `docs/host-endpoint-architecture.md`
- `docs/windows-virtual-display.md`
- `docs/remote-desktop-streaming.md`
- `docs/https-onboarding.md`

## 安全性與隱私

VibeDeck 把 Windows Host 視為本機權限的最終來源。

- 裝置必須完成配對後才能使用受信任 API。
- Device credentials 僅限 VibeDeck 使用，且可以撤銷。
- 敏感的本機狀態儲存在 `%ProgramData%\VibeDeck`，並使用 Windows ACL 保護。
- 遠端路由不會把配對核准權交給雲端路徑。
- 一般手機 / PWA 使用情境會透過 HTTPS 連線。

安全行為與 trust boundary 由 `tests/VibeDeck.Host.Tests/` 中的自動化 product-flow 與 security tests 覆蓋。

## 開發

主要 Solution：

```text
VibeDeck.sln
```

重要路徑：

```text
src/VibeDeck.Host/          Windows Host + browser UI
tests/VibeDeck.Host.Tests/ .NET tests
tests/wwwroot/              browser module tests
driver/VibeDeck.Idd/        experimental VibeDeck IDD development line
packaging/windows-setup/    Windows Setup packaging
scripts/                    build, install, validation, and release tooling
docs/                       architecture and product documentation
```

目前正式 Installer 使用的是上游 **Virtual Display Driver** 整合。`driver/VibeDeck.Idd/` 是另一條實驗中的 VibeDeck IDD 開發線，不會偷偷取代 production Setup 的 driver path。

## 問題回報與建議

遇到 Bug、特定裝置相容性問題，或想到一個適合閒置螢幕的新用途，都歡迎開 GitHub Issue。

VibeDeck 目前主要採直接維護開發，而不是以公開 Pull Request queue 作為主要開發流程。如果你有實作想法，建議先開 Issue 討論實際使用情境。詳細方式請見 [`CONTRIBUTING.md`](CONTRIBUTING.md)。

## Roadmap

核心方向很簡單：

1. 讓 Display path 延遲更低、連線更穩
2. 讓 Deck 更容易製作、分享與組合
3. 讓同一台裝置在 Display 與 Deck 之間的切換更像原生體驗
4. 持續改善手機、平板和電子紙裝置體驗，同時避免依賴平台專屬 Client App

VibeDeck 刻意比「手機當副螢幕」更廣。**閒置螢幕只是硬體，真正的產品是你讓它去做什麼。**

## 歷史

VibeDeck 是從早期的 PhoneMonitor prototype 演化而來。舊 hackathon 記錄與產品快照會保留在 `docs/history/`，作為歷史資料，而不是拿來定義現在的產品。

## 授權

VibeDeck 以 **PolyForm Shield License 1.0.0** 公開原始碼。你可以在授權允許的用途下使用、修改與散布，但不能拿它來提供與 VibeDeck 競爭的產品。實際法律條款請以 [`LICENSE`](LICENSE) 為準。

第三方元件仍依各自的授權條款使用，相關聲明請見 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。
