# Windows Virtual Display Plan

Windows does not provide a normal user-mode API that can create a real monitor. To make a phone appear as an extended display in Windows Settings, VibeDeck needs an Indirect Display Driver.

## Driver Module

Recommended implementation:

- Language: C++
- Driver model: Windows Indirect Display Driver Model
- Framework: IddCx with UMDF
- Output: a virtual monitor target with configurable modes such as 1920x1080 at 60 Hz

The PC host should not contain driver logic directly. It should control the driver through a narrow IPC or device interface:

- enumerate virtual monitors
- enable or disable a virtual monitor
- set resolution and refresh rate
- receive or capture frames for encoding

## MVP Path

1. Install a minimal test-signed Indirect Display Driver.
2. Make `VibeDeck Display` appear in Windows Settings without any physical monitor.
3. Start with one fixed virtual display mode.
4. Capture that display using Windows Desktop Duplication or Windows Graphics Capture.
5. Encode frames in the host process.
6. Stream to the phone client.
7. Add dynamic modes, rotation, DPI, and reconnect handling.

Window casting is only a debug source. It cannot solve the real product problem because it still depends on an existing visible display.

## Why This Split Matters

The driver requires signing, installation, and Windows-specific lifecycle handling. The host app can iterate much faster if transport, pairing, encoding, and the phone UI are developed independently first.
