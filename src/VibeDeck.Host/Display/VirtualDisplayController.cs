using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VibeDeck.Host.Display
{
    public sealed class VirtualDisplayController
    {
        private readonly string statePath;
        private readonly object syncRoot = new object();

        public VirtualDisplayController()
        {
            var directory = AppPaths.EnsureDirectory(AppPaths.DataRoot);
            statePath = Path.Combine(directory, "virtual-display-state.json");
        }

        public VirtualDisplayStatus GetStatus()
        {
            lock (syncRoot)
            {
                var persisted = ReadPersistedState();
                var device = DetectInstalledDevice();
                return new VirtualDisplayStatus
                {
                    DriverInstalled = device.Installed,
                    DisplayEnabled = device.Started || persisted.DisplayEnabled,
                    DevicePath = device.InstanceId ?? persisted.DevicePath,
                    State = device.Installed ? (device.Started ? "driver-started" : "driver-installed") : "driver-not-installed",
                    Detail = device.Installed
                        ? "VibeDeck virtual display is installed. Check Windows Settings > System > Display for the virtual monitor."
                        : "Install the VibeDeck virtual display driver to expose a real Windows monitor."
                };
            }
        }

        public VirtualDisplayStatus Enable(int width, int height, int refreshRate)
        {
            lock (syncRoot)
            {
                var state = new PersistedVirtualDisplayState
                {
                    DisplayEnabled = true,
                    Width = width,
                    Height = height,
                    RefreshRate = refreshRate,
                    DevicePath = null
                };
                WritePersistedState(state);

                var status = GetStatus();
                status.Detail = $"Requested virtual display {width}x{height}@{refreshRate}. Driver bridge is ready; driver install is still required.";
                return status;
            }
        }

        public VirtualDisplayStatus Disable()
        {
            lock (syncRoot)
            {
                var state = ReadPersistedState();
                state.DisplayEnabled = false;
                WritePersistedState(state);

                var status = GetStatus();
                status.Detail = "Requested virtual display disable. Driver bridge is ready; driver install is still required.";
                return status;
            }
        }

        private PersistedVirtualDisplayState ReadPersistedState()
        {
            if (!File.Exists(statePath))
            {
                return new PersistedVirtualDisplayState();
            }

            try
            {
                var json = File.ReadAllText(statePath);
                return JsonSerializer.Deserialize<PersistedVirtualDisplayState>(json) ?? new PersistedVirtualDisplayState();
            }
            catch
            {
                return new PersistedVirtualDisplayState();
            }
        }

        private void WritePersistedState(PersistedVirtualDisplayState state)
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(statePath, json);
        }

        private static DetectedDevice DetectInstalledDevice()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pnputil",
                    Arguments = "/enum-devices /class Display",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                var isVibeDeckDisplay = output.IndexOf("VibeDeck Display", StringComparison.OrdinalIgnoreCase) >= 0;
                var isVirtualDisplayDriver = output.IndexOf("Virtual Display Driver", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isVibeDeckDisplay && !isVirtualDisplayDriver)
                {
                    return new DetectedDevice();
                }

                return new DetectedDevice
                {
                    Installed = true,
                    Started = output.IndexOf("Started", StringComparison.OrdinalIgnoreCase) >= 0
                        || output.IndexOf("已啟動", StringComparison.OrdinalIgnoreCase) >= 0,
                    InstanceId = isVirtualDisplayDriver ? @"ROOT\MTTVDD" : @"ROOT\VIBEDECKIDD"
                };
            }
            catch
            {
                return new DetectedDevice();
            }
        }

        private sealed class PersistedVirtualDisplayState
        {
            public bool DisplayEnabled { get; set; }
            public int Width { get; set; } = 1920;
            public int Height { get; set; } = 1080;
            public int RefreshRate { get; set; } = 60;
            public string DevicePath { get; set; }
        }

        private sealed class DetectedDevice
        {
            public bool Installed { get; set; }
            public bool Started { get; set; }
            public string InstanceId { get; set; }
        }
    }
}
