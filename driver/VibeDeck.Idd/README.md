# VibeDeck Indirect Display Driver

This directory is the production-critical module: a Windows Indirect Display Driver that exposes a real monitor named `VibeDeck Display`.

This machine does not currently have WDK/MSBuild driver tooling installed. The project is now shaped as a WDK Visual Studio driver project, but local build requires the toolchain.

The first implementation intentionally uses Microsoft's official Indirect Display sample as a base. Fetch it with:

```powershell
scripts\fetch-idd-sample.ps1
```

Then build with:

```powershell
scripts\build-driver.ps1
```

## Target

- Driver model: UMDF 2 + IddCx
- Language: C++
- Device class: Display
- First supported mode: 1920x1080 at 60 Hz
- Later modes: 1280x720, 1600x900, 2340x1080 portrait-aware phone modes

## Responsibilities

- Advertise one virtual monitor target to Windows.
- Accept enable/disable/mode commands from `VibeDeck.Host`.
- Present swap-chain frames or expose a capture path that the host can encode.
- Keep Windows mouse and keyboard behavior native.

## Build Requirements

- Visual Studio with Desktop development with C++
- Windows Driver Kit
- Windows SDK
- Test signing enabled for local development

Install helper:

```powershell
scripts\install-driver-toolchain.ps1
```

## Host Contract

The host should talk to the driver through a narrow device interface. See:

- `include/VibeDeckIoctl.h`
- `../../docs/driver-host-contract.md`

## Attribution

The first driver project links against Microsoft's Windows driver sample source under `third_party/Windows-driver-samples/video/IndirectDisplay`. That repository is licensed under the Microsoft Public License.
