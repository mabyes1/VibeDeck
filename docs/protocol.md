# VibeDeck Protocol Notes

## Approval pairing (default)

Browser/PWA clients on iPhone, Android, and BOOX use one flow: scan the PC QR to open the HTTPS Host, submit a request on the phone, then approve or deny it on the PC.

- `POST /api/devices/pairing/request` — private-LAN client creates a 10-minute request and receives a private poll secret plus six-digit verification code.
- `POST /api/devices/pairing/pending` — PC-local console lists requests (action token required).
- `POST /api/devices/pairing/approve` or `/deny` — PC-local user decides (action token required).
- `POST /api/devices/pairing/poll` — requesting client exchanges its private secret for a persistent device token after approval.

The Host never sends an approved device token to the PC console; only the requesting phone can retrieve it. Requests are limited to private LAN addresses, require HTTPS, and expire automatically.

Last updated: 2026-07-20

This document records the current prototype protocol. Older experiments such as `/ws/video`, window capture, WebRTC DataChannel JPEG, and fragmented MP4 H.264 were useful for learning, but they are not the active product path.

VibeDeck is the canonical identity used by the Host, browser client, driver, certificates, and protocol fields.

## Active Web Prototype

The web prototype is intentionally simple:

- The phone opens the Host web page.
- The page enumerates Windows monitors and remembers the virtual or physical display selected by that paired browser.
- iOS Safari prefers H.264 over WebRTC when FFmpeg is available.
- JPEG frames over WebSocket remain the browser fallback.
- Stream FPS and JPEG quality are user-adjustable.
- Virtual display resolution and refresh rate are user-adjustable.
- HTTPS is preferred so Screen Wake Lock can keep the phone awake.

## Display Stream

Endpoint:

```text
GET /ws/display?deviceName=<display>&fps=<fps>&quality=<jpeg-quality>
```

Current payload:

- WebSocket binary message.
- One complete JPEG frame per message.
- Intended for prototype display, debugging, and fallback use.

## WebRTC H.264 Display

Endpoint:

```text
POST /api/stream/webrtc/offer
```

The browser sends its gathered SDP offer together with `deviceName`, `fps`,
and `quality`. The Host answers with SDP and sends Annex-B H.264 access units
as RTP/SRTP. This lets modern phone browsers use their hardware H.264 decoder
without an installed native app. The route requires a paired device token (or
a loopback request) and an FFmpeg executable on the Host.

The stream targets an enumerated Windows display selected by the product UI. Virtual displays use the GDI-compatible capture path; physical displays prefer DXGI Desktop Duplication. Arbitrary windows and debug capture sources are not exposed.

## Input

Endpoint:

```text
GET /ws/input
```

Payload: UTF-8 JSON.

```json
{
  "type": "pointermove",
  "deviceName": "\\\\.\\DISPLAY1",
  "x": 0.5,
  "y": 0.5,
  "buttons": 1
}
```

Coordinates are normalized from `0.0` to `1.0` so the PC Host can map them to the selected display bounds.

Committed mobile-IME text is sent as Unicode rather than replaying the phone's composition keystrokes:

```json
{
  "type": "text",
  "text": "遠端輸入"
}
```

Navigation keys and shortcuts use a key message:

```json
{
  "type": "key",
  "key": "ArrowLeft",
  "code": "ArrowLeft",
  "ctrlKey": false,
  "altKey": false,
  "shiftKey": false,
  "metaKey": false
}
```

The Host injects text and supported keys into the signed-in desktop session with Windows `SendInput`. Secure-desktop surfaces such as UAC and Ctrl+Alt+Delete are intentionally not part of this channel.

## Connect Info

Endpoint:

```text
GET /api/connect
```

Returns URLs and LAN addresses that the phone page can use for pairing/opening:

- HTTP URL.
- HTTPS URL.
- preferred URL.
- whether local HTTPS is configured.
- root and host certificate download URLs.
- whether Wake Lock needs HTTPS.

The QR endpoint points to the preferred phone URL. When the local HTTPS certificate is configured, this prefers port `5443`; otherwise it falls back to HTTP on port `5000`:

```text
GET /qr.svg
```

The phone can bootstrap local certificate trust from HTTP:

```text
GET /cert/vibedeck-root.cer
GET /cert/vibedeck-host.cer
```

The Host owns local certificate maintenance. On startup it creates missing certificate files and refreshes the Host certificate when the LAN IP list changes.

## Installable Web App Shell

The Host also serves the phone app shell:

```text
GET /manifest.json
GET /service-worker.js
GET /offline.html
GET /icons/vibedeck.svg
GET /icons/maskable-512.png
GET /icons/apple-touch-icon.png
```

Manifest shortcuts open the same app with mode query parameters:

```text
/index.html?mode=display
/index.html?mode=sideboard
/index.html?mode=quota
```

The JavaScript client treats these as initial mode requests and then continues using the normal Host API and WebSocket endpoints.

All supported phone clients use the same `?mode=` query parameters on the Host web URL.
