# VibeDeck engineering guardrails

Read this before diagnosing, running, packaging, or installing the project.

## Canonical product paths

- Windows product release: `scripts/package-windows-setup.ps1` → `VibeDeck-Setup-<version>.exe`.
- Windows product update: run a newer Setup with the same AppId. It replaces app files and preserves `%ProgramData%\VibeDeck`.
- Installed Host: `C:\Program Files\VibeDeck\VibeDeck.Host.exe`, registered for sign-in auto-start by Setup and launched directly in the signed-in desktop session.
- Phone clients: Host-served Safari, Chrome, or PWA only. Android, iPhone, and BOOX share this path.
- Windows notifications: optional packaged companion `VibeDeck.Notifications.exe`; it is not the Host and must not own ports 5000/5443.
- Source development: `start.bat` / `scripts/dev-run.ps1`. Stop the installed Host first because both use the same ports.

## Paths that must not return

- Do not run or recreate an Android native shell/APK. `apps/android` was deliberately removed.
- Do not register Host as a Windows Service. Session 0 cannot enumerate or capture the user's virtual display.
- Do not restore a portable ZIP as a product release. It bypasses installation, updates, autostart, and product-state guarantees.
- Do not treat the notification companion as a second Host just because it shares managed code internally.
- Do not compile a personal Custom Deck backend into the Host. Stock Watch, Mitake DDE, TWSE MIS, portfolios, and other user-specific Deck logic belong in `%ProgramData%\VibeDeck\Decks` or a separately packaged extension.

## Debug order

1. Run `scripts/test-product-flow.ps1 -Installed` before changing drivers or pairing state.
2. Confirm port 5000 belongs to a process whose `SessionId` is greater than zero.
3. Query `http://127.0.0.1:5000/api/displays`. `WinDisc` means the wrong Session; it is not proof the virtual display driver is missing.
4. Check PnP state before reinstalling the virtual display driver.
5. Test phone UI in the actual browser/PWA. Never substitute an old native app.

## Data ownership

- Installed product data: `%ProgramData%\VibeDeck`.
- Source-development data: `%LocalAppData%\VibeDeck`.
- Updates may replace `C:\Program Files\VibeDeck`, but must not erase `%ProgramData%\VibeDeck`.
