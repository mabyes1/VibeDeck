# VibeDeck Windows Setup

Product installer assets for the one-click Windows install path.

## What the user gets

1. **Setup.exe** — the only supported Windows product release and update path.
2. **VibeDeck Host** native desktop-session background app — starts automatically after Windows sign-in so display capture sees the interactive desktop; no CMD or VBS launcher is exposed.
3. **Desktop / Start Menu icon** — runs `VibeDeck.Host.exe --open`, waits for the local Host, then opens the PC UI in the default browser.
4. **Firewall rule** — inbound allow for `VibeDeck.Host.exe` (LAN phone access).
5. **Data** — `%ProgramData%\VibeDeck` (certs, devices, quotas, custom sources).

## Which command should I use?

| Goal | Command |
|---|---|
| Web UI iteration | `scripts\build-and-install-windows.ps1 -FastLocal -WebOnly -SkipTests` |
| Host/C# iteration | `scripts\build-and-install-windows.ps1 -FastLocal -SkipTests` |
| Release or clean install artifact | `scripts\package-windows-setup.ps1` |

The two `FastLocal` commands require an existing canonical Setup installation.
They create a same-AppId overlay and are never release artifacts. Windows
notification packaging remains separate.

## Build

From repo root:

```powershell
scripts\package-windows-setup.ps1
```

Output:

```text
artifacts\windows-setup\VibeDeck-Setup-<version>.exe
artifacts\windows-setup\payload\          # published self-contained Host
```

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php). Optional:

```powershell
scripts\package-windows-setup.ps1 -InstallInno
scripts\package-windows-setup.ps1 -SkipInno   # payload only
```

For a complete local one-click build and installation, double-click `install.bat`. For an in-place update, double-click `update.bat`; it silently stops the old Host, runs Setup, preserves `%ProgramData%\VibeDeck`, restarts the Host, and verifies the installed product.

For rapid iteration against an existing canonical installation, build and run a
same-AppId changed-file overlay Setup:

```powershell
scripts\build-and-install-windows.ps1 -FastLocal -SkipTests
```

`-FastLocal` incrementally refreshes the full publish cache, compares it with
`C:\Program Files\VibeDeck`, and packages only changed Host/runtime files plus
the complete replaceable web trees. It does not include unchanged .NET runtime,
cloudflared, or the independently installed notification companion. The output
`VibeDeck-FastLocal-Setup-<version>.exe` is local-only and must never be
published or used for a clean install. Omit `-SkipTests` when the relevant source
checks have not already been run.

When every intended change is under `src\VibeDeck.Host\wwwroot`, use the
web-only path. It skips `dotnet publish` entirely:

```powershell
scripts\build-and-install-windows.ps1 -FastLocal -WebOnly -SkipTests
```

Do not use `-WebOnly` after changing C#, the project file, NuGet dependencies,
connectors, installers, or other non-web product files.

Fallback install without Setup.exe (elevated):

```powershell
scripts\package-windows-setup.ps1 -SkipInno
scripts\install-windows-product.ps1
```

This fallback is for local development/deployment only. Public distribution must use Setup so upgrades, app cleanup, autostart, shortcuts, and uninstall metadata stay consistent.

## Files

| File | Role |
|------|------|
| `VibeDeck.iss` | Inno Setup script |
| `VibeDeck.Host.exe --open` | Native icon entry → ensure Host → open Web UI |
| `product-install.json` | Marker so Host uses installed data paths |
| `vibedeck.ico` | Setup + shortcut icon |

## Upgrade behavior

- A newer Setup uses the same AppId and updates in place.
- The legacy `VibeDeckHost` service is removed before files are replaced.
- Replaceable web/runtime directories are cleared so deleted modules do not survive.
- `%ProgramData%\VibeDeck` is outside `{app}` and remains intact.
- The updated Host restarts in the signed-in desktop session.

## Dev vs product

| Mode | How | Data |
|------|-----|------|
| Dev | `start.bat` / `dev-run.ps1` | `%LocalAppData%\VibeDeck` |
| Product | Setup / signed-in desktop Host | `%ProgramData%\VibeDeck` |
