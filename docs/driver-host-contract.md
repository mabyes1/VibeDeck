# Driver and Host Contract

VibeDeck is split into a Windows display driver and a user-mode host.

## Driver

The driver creates the real Windows monitor. It should be intentionally small:

- expose one or more virtual monitor targets
- report supported modes
- apply enable/disable/mode commands
- provide a frame handoff path

It should not own pairing, networking, phone sessions, tray UI, or product logic.

## Host

The host owns the product behavior:

- device pairing
- Wi-Fi and USB-C transports
- video encoding
- phone client session lifecycle
- sideboard telemetry and optional dashboard integrations
- tray UI and settings

## IOCTLs

The shared constants live in `driver/VibeDeck.Idd/include/VibeDeckIoctl.h`.

Initial commands:

- `IOCTL_VIBEDECK_GET_STATUS`
- `IOCTL_VIBEDECK_ENABLE_DISPLAY`
- `IOCTL_VIBEDECK_DISABLE_DISPLAY`
- `IOCTL_VIBEDECK_SET_MODE`

## First Mode

The first driver milestone used one stable mode:

- 1920x1080
- 60 Hz
- landscape

Phone-specific modes and rotation are product-level requirements after the first monitor is visible in Windows Settings.
