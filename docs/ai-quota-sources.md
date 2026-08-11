# AI Quota Sources

VibeDeck quota collection must be independent. Cockpit Tools can be used as a research reference, but VibeDeck must not depend on Cockpit processes, ports, caches, or private local storage.

## New-user paths (same text is shown on the Quotas page)

### Codex → VibeDeck

1. Install/sign in Codex on the **same Windows PC** as the Host.
2. Use Codex so `%USERPROFILE%\.codex\sessions\**\*.jsonl` gets a `rate_limits` event.
3. Open **額度 → Codex → ↻**.
4. No import button and no Codex OAuth — local scan only.

### AGY → VibeDeck

1. Configure Google OAuth on the Host (`AGY_GOOGLE_*` env or `%LOCALAPPDATA%\VibeDeck\secrets\agy-google-oauth.json`).
2. Open **額度 → AGY → +** and finish Google sign-in in the **PC** browser.
3. Press **↻** to refresh Antigravity quota APIs.
4. Optional one-time import from `%USERPROFILE%\.antigravity_cockpit\accounts\` via `POST /api/quotas/agy/import`.

## Current Providers

### Codex

Status: implemented.

Sources:

- `%USERPROFILE%\.codex\sessions\**\*.jsonl`
- `%USERPROFILE%\.codex\auth*.json`
- `%LOCALAPPDATA%\VibeDeck\quotas\codex\accounts\*.json`

Codex session logs include `event_msg` payloads with `rate_limits`. VibeDeck reads recent session files from newest to oldest and scans the tail of each JSONL file for the newest rate-limit snapshot.

Codex `rate_limits` events do not include the account email. VibeDeck uses Codex-owned `auth*.json` files only to identify seen accounts by non-token metadata such as account id, email, and plan type. It does not store Codex tokens.

When VibeDeck sees a usable Codex quota snapshot, it writes the normalized snapshot to:

```text
%LOCALAPPDATA%\VibeDeck\quotas\codex\accounts\*.json
```

This lets the quota page remember multiple Codex accounts after they have been used at least once. Accounts seen in Codex auth files but not yet associated with a quota snapshot are shown as `source-needed` until that account produces a fresh `rate_limits` event.

Known fields:

- `primary.used_percent`
- `primary.window_minutes`
- `primary.resets_at`
- `secondary.used_percent`
- `secondary.window_minutes`
- `secondary.resets_at`
- `credits.balance`
- `credits.unlimited`

This is not a direct account API call, but it is independent and good enough for the first quota page because it reflects the latest quota snapshot Codex itself received.

When present, VibeDeck shows the latest `credits.balance` on the Codex quota card, rounded to two decimals. This is a locally observed Codex session snapshot, not a billing API; the timestamp on the card identifies when it was last received.

Codex multi-account discovery does not read `.cockpit_codex_auth*`. VibeDeck only trusts Codex-owned session/auth data and its own normalized quota cache.

### Claude Code

Status: **removed from product surface**.

Earlier builds only detected local Claude install/config and showed a tab with no way to import real usage into VibeDeck. That dead-end path is no longer returned by `/api/quotas` and the Claude tab is gone.

AGY’s own “Claude” bucket (via Antigravity) is unrelated — that still comes from AGY OAuth/quota APIs.

If Claude Code quota is added later, it needs a real authenticated usage source plus store/import UI, not install detection alone.

### AGY

Status: implemented from VibeDeck's own token/cache store.

AGY CLI exists at `%LOCALAPPDATA%\agy\bin\agy.exe`.

Current source:

- Token store: `%LOCALAPPDATA%\VibeDeck\quotas\agy\accounts\*.json`
  - Refresh tokens are stored as `refresh_token_protected`, encrypted with Windows DPAPI `CurrentUser`.
  - Legacy plaintext `refresh_token` files are read only for migration and are rewritten encrypted on import.
- Quota cache: `%LOCALAPPDATA%\VibeDeck\quotas\agy\cache\*.json`
- Refreshes Google OAuth access tokens, then calls the Antigravity quota API directly.
- New accounts can be added through VibeDeck's OAuth loopback flow.
  - Start endpoint opens the PC browser, even when triggered from the phone UI.
  - Callback returns to `http://127.0.0.1:{port}` with the OAuth query string.
  - The flow uses PKCE and requests `openid email https://www.googleapis.com/auth/cloud-platform`.
  - Google OAuth client credentials are **not** shipped in source. Configure either:
    - Environment variables `AGY_GOOGLE_CLIENT_ID` and `AGY_GOOGLE_CLIENT_SECRET`, or
    - `%LOCALAPPDATA%\VibeDeck\secrets\agy-google-oauth.json` (see `docs/agy-google-oauth.example.json`).
- Maps `3p-5h` and `3p-weekly` to `AGY Claude`.
- Maps `gemini-5h` and `gemini-weekly` to `AGY Gemini`.
- These percentages are already remaining quota, not used quota.

Antigravity Cockpit data is now only a one-time import fallback. VibeDeck may import refresh tokens from:

```text
%USERPROFILE%\.antigravity_cockpit\accounts\*.json
```

After import, the formal quota source is the VibeDeck token store and cache above. VibeDeck no longer reads
Cockpit quota snapshots as the displayed quota source.

Manual import endpoint:

```text
POST /api/quotas/agy/import
```

OAuth endpoints:

```text
POST /api/quotas/agy/oauth/start
GET /?state=...&code=...
GET /api/quotas/agy/oauth/callback
```

The `/api/quotas/agy/oauth/callback` route is a debug/manual fallback. The normal Google loopback redirect uses `/`.

AGY account actions:

```text
POST /api/quotas/agy/cli/open
POST /api/quotas/agy/account/delete
```

`/api/quotas/agy/cli/open?open=false` validates the selected account and writes the launcher without opening a visible terminal.

The quota card toolbar uses:

- `+` for AGY OAuth sign-in / add account.
- `▶` to open AGY CLI with the selected account context.
- `↻` to refresh quotas.
- `⌫` to delete the selected AGY account from VibeDeck's token/cache store.

## Endpoint

VibeDeck exposes:

```text
GET /api/quotas
```

Refresh and import helpers:

```text
POST /api/quotas/refresh
POST /api/quotas/agy/import
POST /api/quotas/agy/oauth/start
POST /api/quotas/agy/cli/open
POST /api/quotas/agy/account/delete
```

The response is normalized into providers with `Primary` and `Secondary` quota windows when available. Providers that are installed but not wired return `State = "source-needed"` instead of pretending to have data.

The phone UI renders quota as a dedicated page, not inside the Glance Board sideboard. The page should reserve action space for OAuth/account switching even before those flows are implemented.
