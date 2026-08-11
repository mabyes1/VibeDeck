# VibeDeck HTTPS Onboarding

Last updated: 2026-07-19

The zero-install phone path still starts from the Host web page. HTTPS is required for real LAN pairing and is strongly preferred for Wake Lock / home-screen use.

## Preferred product path: VibeDeck secure URL

From `0.1.22`, the normal product connection can use one browser-trusted URL per Windows Host:

```text
https://<installation-id>.vibedeck.pp.ua/
```

The Host creates and persists its `installation-id` under the product data root. The VibeDeck Worker automatically creates a remotely managed Cloudflare Tunnel, publishes that exact first-level hostname, and forwards it to `http://127.0.0.1:5000`.

Normal users do not create a Cloudflare account, enter a hostname, install a certificate, or start a command. Windows Setup includes the pinned `cloudflared` connector; the signed-in VibeDeck Host starts it hidden, monitors it, retries failures, and stops it with the Host.

Credential boundaries:

- the Cloudflare API token exists only as the `CLOUDFLARE_API_TOKEN` Worker secret;
- the PC receives only its own remotely managed Tunnel token;
- the Tunnel token and per-installation provisioning secret are DPAPI-encrypted in `%ProgramData%\VibeDeck\connect\managed-tunnel.json`;
- phones receive neither credential and still require explicit PC approval.

When the connector becomes healthy:

1. The PC QR code switches to the trusted public HTTPS URL.
2. Safari, Chrome, Android, iPhone, and BOOX open without a certificate warning.
3. Phone pairing still requires the existing six-digit code and an explicit **Allow** action on the PC.

### E-paper readers without a camera

When a trusted public URL is configured, the PC can create an eight-character, one-time **e-paper connection code**. On the reader, open the short shared address `https://vibedeck.pp.ua/`, enter the code, and the browser is redirected once to that PC's installation-specific secure URL. The reader then completes the normal PC-approved pairing flow and can be added to its home screen.

The connection code lasts ten minutes and is deleted as soon as it resolves. It only reveals the already-configured VibeDeck URL; it cannot pair a device or bypass the PC's approval boundary.

The shared landing and provisioning control plane are implemented by `workers/vibedeck-connect-code`. Durable Objects provide one-time code consistency, installation ownership, and rate limits. Deploy it to the root route after authenticating Wrangler and setting the scoped Cloudflare API token:

```powershell
cd workers\vibedeck-connect-code
npx wrangler secret put CLOUDFLARE_API_TOKEN
npx wrangler deploy
```

The token requires only **Account → Cloudflare Tunnel → Edit** for the VibeDeck account and **Zone → DNS → Edit** for `vibedeck.pp.ua`. Account and Zone IDs are non-secret Wrangler variables. Never commit or place the API token in `wrangler.toml`.

Provisioning is idempotent per installation ID and bound to a 256-bit installation secret. New allocations are limited per source IP and globally per day so anonymous installers cannot cheaply exhaust the free account's Tunnel quota. The Worker also rate-limits one-time code registration and resolution.

The Worker verifies `https://vd-<installation-id>.vibedeck.pp.ua/` through `/api/connect` before accepting a code registration. No Cloudflare API credential or Worker secret is stored by VibeDeck Host or sent to the reader.

The Host accepts a public pairing request only when all of these are true:

- the request was forwarded by a connector on `127.0.0.1` or `::1`;
- forwarded HTTPS and hostname exactly match this Host's stored URL;
- the phone completes the usual PC approval flow.

This keeps router ports closed while preserving the local approval boundary. If provisioning, DNS, or the connector is unavailable, `ConnectInfoProvider` stops advertising the public route and falls back to local HTTPS on port `5443`.

## Where certificates live

Installed product (Setup):

```text
%ProgramData%\VibeDeck\certs
```

Source / dev runs:

```text
%LOCALAPPDATA%\VibeDeck\certs
```

The on-disk filenames and download routes use the canonical VibeDeck identity:

- `/cert/vibedeck-root.cer`
- `/cert/vibedeck-host.cer`

Managed files on disk:

- `vibedeck-root.pfx` / `vibedeck-root.cer`
- `vibedeck-host.pfx` / `vibedeck-host.cer`
- `vibedeck-certificate-state.json`

Roots are issued as `CN=VibeDeck Local Root CA`.

## PC Setup

Normal Host startup checks the local certificate automatically. If the set is missing, incomplete, close to renewal, or does not match the current LAN IP list, the Host generates or refreshes it before opening the HTTPS listener.

Manual repair (dev / recovery):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-https.ps1
```

After the certificate files exist, the Host listens on:

- `http://0.0.0.0:5000`
- `https://0.0.0.0:5443`

If you want to throw away the local root and start over:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-https.ps1 -Force
```

Phones must reinstall and re-trust the root after a forced reset.

## Phone Setup

Open the bootstrap HTTP page first (or scan the PC QR, which prefers HTTPS when ready):

```text
http://<pc-lan-ip>:5000/
```

Use **安裝 HTTPS 憑證** to download the root certificate. Install and fully trust it in the phone OS.

### Android / Chrome

- Install the `.cer` as a CA certificate from system settings.
- Open the HTTPS URL for WebRTC H.264 with JPEG fallback.
- Android never silently installs CAs from a normal web page.

### iPhone / Safari

- Install the downloaded certificate profile.
- Settings → General → About → Certificate Trust Settings → enable full trust.
- Open the HTTPS URL in Safari (or re-add to Home Screen from HTTPS).

Secure URL:

```text
https://<pc-lan-ip>:5443/
```

Pairing request and approval polling require HTTPS. `/api/connect` reports both HTTP and HTTPS URLs.

## Current Limits

This is local development HTTPS, not a public CA certificate. It is good enough for a trusted personal LAN. For remote access, use Tailscale or a reverse proxy with a publicly trusted certificate.
