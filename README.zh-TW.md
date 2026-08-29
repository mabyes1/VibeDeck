# VibeDeck

[English](README.md) · **繁體中文**

![VibeDeck 功能總覽](docs/assets/social-preview0.png)

> **讓你的閒置螢幕有一份新工作。**
>
> VibeDeck 可以把舊手機、平板、筆電、電子紙裝置，或其他能開瀏覽器的螢幕，變成**真正的 Windows 顯示器**，或**瀏覽器原生的自訂 Deck**。
>
> **Host：Windows 10/11 · Client：現代瀏覽器 · 副裝置不用安裝 App。**

**[下載最新版 Windows 安裝程式](https://github.com/mabyes1/VibeDeck/releases/latest)** · [從原始碼建置](#從原始碼建置)

## VibeDeck 可以做什麼

同一塊閒置螢幕，可以有兩種完全不同的工作方式：

| | |
|---|---|
| **真正的 Windows 顯示器** | 把一般 Windows App 移到閒置裝置上。VibeDeck 會擷取 Windows 顯示器、透過 WebRTC 串流，再把輸入操作送回 PC。 |
| **自訂 HTML Deck** | 直接在裝置瀏覽器裡執行專門設計的 HTML、CSS 和 JavaScript，使用裝置原生解析度顯示。 |
| **一眼就能看的內建介面** | 把 AI 額度、系統狀態、任務、活動與 Windows 通知放在旁邊，不必佔著桌面主螢幕。 |

同一台裝置可以在 Display 與 Deck 之間切換。需要真正桌面 App 時就用 Windows 畫面；小螢幕有更適合自己的介面時，就直接用瀏覽器原生渲染。

## 真實產品畫面

下面都是實際 VibeDeck 產品與硬體上的畫面，不是 UI mockup。帳號與通知內容等敏感資訊已視需要遮蔽。

<table>
  <tr>
    <td width="50%"><img src="docs/assets/realshots/real-display.png" alt="VibeDeck 在閒置 ASUS 平板上顯示真正的 Windows 畫面"></td>
    <td width="50%"><img src="docs/assets/realshots/real-quota.png" alt="VibeDeck AI 額度畫面"></td>
  </tr>
  <tr>
    <td align="center"><sub><b>閒置實機上的真正 Windows 顯示器</b></sub></td>
    <td align="center"><sub><b>即時 AI 額度頁</b></sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/assets/realshots/real-sideboard.png" alt="VibeDeck 即時系統資訊板"></td>
    <td width="50%"><img src="docs/assets/realshots/real-deck.png" alt="VibeDeck Coding Pet 自訂 Deck"></td>
  </tr>
  <tr>
    <td align="center"><sub><b>系統狀態、活動與 Windows 通知</b></sub></td>
    <td align="center"><sub><b>瀏覽器原生的自訂 Deck</b></sub></td>
  </tr>
</table>

## 把真正的 Windows App 放到閒置螢幕

Display 模式讓瀏覽器裝置顯示真正的 Windows 顯示器。一般 Windows 應用程式可以像移到第二螢幕一樣，直接移到 VibeDeck 顯示器上。

![VibeDeck 真正的 Windows 顯示器](docs/assets/social-preview2.png)

適合：

- 把一般桌面 App 固定放在專用小螢幕
- 已經有 Windows 版介面的 Dashboard
- 臨時需要第二螢幕的工作情境
- 不方便或根本不能安裝原生 Client App 的裝置

## 做一塊真正屬於自己的螢幕

Deck 就是一般的 HTML、CSS 和 JavaScript，直接由裝置瀏覽器渲染。

**只要它能做成網頁，就能做成 Deck。**

Deck 不需要影片編碼，可以直接以裝置原生解析度顯示，也能依手機、平板或電子紙尺寸重新排版。VibeDeck 會自動探索有效的 Deck 資料夾。

![VibeDeck 自訂 HTML Deck](docs/assets/social-preview1.png)

VibeDeck 內附幾個可以直接使用的例子：

- **Sideboard**：CPU、GPU、記憶體、活動、任務、AI 額度與 Windows 通知歷史
- **Quota**：專門查看 AI 使用量與帳號額度
- **Coding Pet**：一個很小的 Custom Deck 範例
- **Remote access**：在區域網路之外連回已信任的 Host

你可以直接用這些介面、自己做新的 Deck，或完全忽略內建畫面。

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

放進你要的網頁檔案後，VibeDeck 會自動探索這個 Deck。想看目前的延伸方式，可以參考 `docs/custom-data-sources-spec.md`，以及內附的 `src/VibeDeck.Host/DeckExamples/coding-pet/` 範例。

## 快速開始

### Windows 安裝

從 **[GitHub Releases](https://github.com/mabyes1/VibeDeck/releases/latest)** 下載最新版 Windows 安裝程式：

```text
VibeDeck-Setup-<version>.exe
```

請在已登入的 Windows 桌面環境執行安裝。持久化資料會存放在：

```text
C:\ProgramData\VibeDeck
```

安裝完成後，在 PC 上開啟 VibeDeck，再依畫面流程連接閒置裝置。區域網路探索一開始使用 HTTP，正常使用時會升級到 HTTPS。

> **Windows 簽章提醒：**目前公開版本尚未使用 Authenticode 簽章，所以 Windows 可能會顯示未知發行者或 SmartScreen 警告。Release 內會附 SHA-256 checksum 供完整性驗證。

### 從原始碼建置

需求：

- Windows 10/11
- .NET 8 SDK
- Node.js，用於 browser / Worker tests
- Inno Setup 6，用於建立 Windows 安裝程式

還原與驗證：

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

VibeDeck 有兩條渲染路徑：

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

Windows Host 負責顯示器探索、串流、配對、裝置信任、Deck 探索、本機資料服務，以及選用的遠端連線。副裝置只需要瀏覽器 UI。

更深入的架構說明：

- `docs/product-architecture.md`
- `docs/host-endpoint-architecture.md`
- `docs/windows-virtual-display.md`
- `docs/remote-desktop-streaming.md`
- `docs/https-onboarding.md`

## 安全性與隱私

Windows Host 仍然是本機權限中心。

- 裝置必須先配對，才能使用受信任 API。
- 裝置憑證只用於 VibeDeck，而且可以撤銷。
- 敏感本機狀態存放在 `%ProgramData%\VibeDeck`，並使用 Windows ACL 保護。
- 遠端路由本身不具備配對授權能力。
- 正常的手機 / PWA 使用流程會透過 HTTPS。

安全行為與信任邊界都有對應的自動化 product-flow 與 security tests，位於 `tests/VibeDeck.Host.Tests/`。

## 為什麼我做 VibeDeck

我常看到那種裝在電腦旁邊或機殼裡的小螢幕，用來顯示溫度和系統資訊。看久了才發現，我真正想要的其實不是再買一塊硬體，而是一個地方，放那些值得偶爾看一眼，但又不值得佔著主螢幕的東西。

Coding agent 有時候只是卡著等核准。AI 額度想知道，但不需要一直開著 App。打遊戲時 GPU 溫度偶爾要看，Windows 通知也最好待在旁邊，不要跳到眼前。

而我桌上本來就有一支幾乎沒在用的舊手機。

這就是 VibeDeck 最基本的想法：**把你已經有的螢幕重新拿來用，給它一份真正有用的工作。**

## 開發資訊

主要 solution：

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

正式安裝程式目前使用 upstream **Virtual Display Driver** 整合。`driver/VibeDeck.Idd/` 是另一條獨立開發線，不會偷偷取代 production Setup 的 driver path。

## 回饋與問題

遇到 bug、特定裝置問題，或想到新的閒置螢幕用途，都可以開 GitHub Issue。

VibeDeck 目前主要透過直接開發維護，不是以開放 PR queue 為主。如果你有實作想法，建議先開 Issue 討論使用情境。詳細流程請看 [`CONTRIBUTING.md`](CONTRIBUTING.md)。

## Roadmap

目前優先方向：

1. 降低 Display latency，並提升連線韌性
2. 讓 Deck 更容易製作、分享與組合
3. 讓同一台裝置在 Display 與 Deck 之間切換得更自然
4. 持續改善手機、平板與電子紙體驗，同時維持副裝置不用安裝平台專屬 App

## License

VibeDeck 採用 **PolyForm Shield License 1.0.0**。在授權允許的用途內，可以使用、修改與重新散布，但不能拿它來提供與 VibeDeck 競爭的產品。實際條款請以 [`LICENSE`](LICENSE) 為準。

第三方元件仍使用各自的授權，整理在 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。
