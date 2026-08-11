#include <windows.h>

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    UNREFERENCED_PARAMETER(instance);
    UNREFERENCED_PARAMETER(reason);
    UNREFERENCED_PARAMETER(reserved);
    return TRUE;
}

// TODO: Implement UMDF/IddCx driver entry points once WDK tooling is installed.
// The production driver should:
// 1. Initialize WDF and IddCx.
// 2. Register the VibeDeck adapter.
// 3. Advertise one monitor target with stable EDID/modes.
// 4. Process host IOCTLs from VibeDeckIoctl.h.
// 5. Provide frame handoff for the host encoder.
