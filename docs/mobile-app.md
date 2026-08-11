# Phone Web/PWA Layer

Last updated: 2026-07-12

VibeDeck uses one phone client path: the Host-served web UI, optionally installed as a Progressive Web App. This keeps iPhone, Android, and BOOX behavior on the same tested code path.

## Supported clients

| Platform | Client | Notes |
|----------|--------|-------|
| **iPhone / iOS** | Safari or Add to Home Screen PWA | WebRTC H.264 + JPEG fallback. No native iOS app. |
| **Android** | Chrome or PWA | WebRTC H.264 + JPEG fallback. |
| **BOOX / e-ink** | Browser / PWA | Same Host protocol and e-ink layout. |

The PWA provides the app-like parts that matter here:

- Home-screen entry, icons, theme, and shortcuts through `manifest.json`.
- Cached shell and offline page through `service-worker.js`.
- Full-screen display, touch input, Wake Lock, and WebRTC display streaming from the browser.

## Canonical setup

1. Open the Host URL from the phone browser.
2. Use the HTTPS URL when Wake Lock or iPhone Home Screen mode is needed.
3. Tap 「提出配對申請」 on the phone, then approve the six-digit request on the PC.
4. Add the page to the home screen if an app-like entry is wanted.

There is no native Android or iOS client in this repository. This is deliberate: keeping an unused shell created a second, untested product path and repeatedly caused debugging against the wrong client.
