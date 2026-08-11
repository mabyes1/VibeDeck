# VibeDeck Product Vision

Last updated: 2026-07-04

## Core Positioning

VibeDeck is not meant to be a plain spacedesk clone. `PhoneMonitor` remains the internal compatibility name for the current driver, Host namespace, and deep links.

The product direction is:

> Turn an idle phone into an open-source smart side display for a PC.

The virtual monitor is a foundation, not the whole product. A phone is usually too small to be a comfortable generic PC monitor for arbitrary desktop apps. The stronger product is a phone-sized side workspace that can show focused panels, AI workflow state, quick controls, dashboards, and ambient companions.

## What We Are Building Toward

VibeDeck should eventually support two layers:

1. Native Display Mode
   - Windows sees a real `PhoneMonitor Display`.
   - The phone can show that virtual display.
   - PC keyboard and mouse remain native.
   - This is useful when the user really wants a small extended monitor.

2. Smart Sideboard Mode
   - Phone-first UI, not a shrunk desktop.
   - Owns core PC telemetry in PhoneMonitor Host.
   - Can optionally reuse `glance-board` skins and work-pulse ideas.
   - Shows Codex or AI task progress.
   - Offers quick controls and shortcuts.
   - Can host small ambient/pet experiences.

3. AI Quotas Mode
   - Dedicated page, not squeezed into the Sideboard.
   - Shows 5-hour remaining quota, weekly remaining quota, and reset timing.
   - Leaves clear space for account/OAuth actions.
   - May borrow interaction ideas from Cockpit Tools, but must not depend on Cockpit runtime state or caches.

4. Phone App Layer
   - Installable PWA entry for the current web UI.
   - One browser path across iPhone, Android, and BOOX.
   - **No native phone binaries**; the Host-served web UI is the product client.

The product should win by being more useful on a phone-sized screen, not by trying to out-feature mature virtual monitor products feature-by-feature.

## Comparison With spacedesk

spacedesk is a mature general-purpose virtual monitor product. It already covers Windows driver, Android/iOS viewers, Wi-Fi/LAN/USB, KVM input, audio, and video-wall scenarios.

Trying to beat it only as a generic second-monitor tool is not a good primary strategy.

PhoneMonitor should instead focus on:

- Open-source hackability.
- Phone-optimized panels.
- AI workflow awareness.
- Dashboard/control-board workflows.
- Optional bridges to local tools like `glance-board`, without making them runtime dependencies.
- A pleasant always-on desk companion experience.

## Current Product Decision

For the web prototype, the phone page should be product-focused:

- Automatically stream the `PhoneMonitor` virtual display only.
- Do not expose source selection.
- Do not expose experimental codecs.
- Do not expose fill/crop/stretch controls.
- Keep JPEG WebSocket as the current working transport.
- Keep stream FPS/quality and virtual display resolution controls.
- Keep HTTPS + Wake Lock status for zero-install phone use.

Removed or hidden from the product UI:

- Existing-window capture.
- Test pattern stream.
- H.264 fragmented MP4.
- WebRTC DataChannel JPEG.
- Fit / fill crop / stretch.

## Wake Lock Strategy

Do not make ADB part of the user-facing path.

ADB is useful for developer testing, but it is a bad general-user requirement because it requires USB debugging, can conflict with security-sensitive apps, and feels risky to normal users.

The zero-install path is:

- Host provides HTTP and HTTPS.
- Host startup maintains local HTTPS certificates automatically.
- The bootstrap HTTP page can serve the local PhoneMonitor root certificate.
- Phone opens HTTPS when possible.
- The web page uses the Screen Wake Lock API.
- If Wake Lock is unavailable or blocked, the page tells the user what to change manually.

The PWA is the only supported phone app layer for every phone. It provides the home-screen entry, cached shell, shortcuts, WebRTC H.264, and JPEG fallback.

## Current Test Phone

The active development phone includes an iPhone XS (web path).

Near-term iPhone strategy:

- Zero-install web client is the **only** iPhone product path.
- Prefer WebRTC H.264 in Safari/PWA, with JPEG as compatibility fallback.
- Use Safari over HTTPS when possible so Screen Wake Lock can work.
- If the local HTTPS certificate is blocked during development, use HTTP and manually set iPhone Auto-Lock to Never while testing.
- Keep iPhone-friendly virtual display presets and immersive CSS viewer (Safari Fullscreen API is unreliable).
- Do not reintroduce an Xcode / TestFlight / App Store client unless product goals change.

## Near-Term Next Steps

1. Keep the current web UI focused on the PhoneMonitor virtual display.
2. Integrate or embed `glance-board` as the first Smart Sideboard panel.
3. Define a simple panel API from Host to phone.
4. Finish the dedicated AI quota page and provider/account flows.
5. Keep JPEG as browser fallback while WebRTC H.264 is the low-latency web path (especially iPhone).
6. Polish the browser/PWA path across mobile and e-ink devices.

## Sideboard Design Decision

The phone Sideboard should not be a stripped-down toy version.

Required system information should remain visible:

- CPU usage, temperature, and fan availability.
- RAM usage.
- GPU usage, temperature, VRAM, and fan availability.
- Disk usage and disk IO.
- Network download/upload.
- Weather.
- Top memory processes.
- Work Pulse / AI workflow status.

The phone-specific design challenge is to fit these signals with hierarchy:

- One large system-load readout.
- Dense but readable metric tiles.
- Short one-line secondary metadata.
- Three lightweight skins using real `glance-board` PNG backgrounds: command, dial, and focus.

The original `glance-board` remains the richer PC display. PhoneMonitor Sideboard should own the phone-critical telemetry path and only consume optional richer signals when they are available.
