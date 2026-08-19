using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SharpDX.DXGI;

namespace VibeDeck.Host.Windows
{
    public sealed class DisplayCatalog
    {
        private const uint MonitorInfoPrimary = 1;

        public IReadOnlyList<DisplayInfo> GetDisplays()
        {
            var displays = new List<DisplayInfo>();

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data)
            {
                var info = new MonitorInfoEx
                {
                    Size = Marshal.SizeOf(typeof(MonitorInfoEx))
                };

                if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                {
                    return true;
                }

                var device = GetDisplayDevice(info.DeviceName);
                displays.Add(new DisplayInfo
                {
                    AdapterIndex = -1,
                    OutputIndex = -1,
                    DeviceName = info.DeviceName,
                    FriendlyName = device.DeviceString ?? info.DeviceName,
                    DeviceId = device.DeviceId,
                    Left = info.Monitor.Left,
                    Top = info.Monitor.Top,
                    Width = info.Monitor.Width,
                    Height = info.Monitor.Height,
                    IsPrimary = (info.Flags & MonitorInfoPrimary) == MonitorInfoPrimary,
                    IsVibeDeckDisplay = IsVibeDeckDisplayDevice(device)
                });

                return true;
            }, IntPtr.Zero);

            ApplyDxgiOutputMap(displays, ReadDxgiOutputMap());

            return displays
                .OrderByDescending(d => d.IsVibeDeckDisplay)
                .ThenBy(d => d.Left)
                .ThenBy(d => d.Top)
                .ToList();
        }

        internal static void ApplyDxgiOutputMap(
            IList<DisplayInfo> displays,
            IReadOnlyDictionary<string, DxgiOutputLocation> outputMap)
        {
            foreach (var display in displays)
            {
                if (outputMap != null &&
                    outputMap.TryGetValue(display.DeviceName ?? string.Empty, out var location))
                {
                    display.AdapterIndex = location.AdapterIndex;
                    display.OutputIndex = location.OutputIndex;
                }
                else
                {
                    display.AdapterIndex = -1;
                    display.OutputIndex = -1;
                }
            }
        }

        private static IReadOnlyDictionary<string, DxgiOutputLocation> ReadDxgiOutputMap()
        {
            var result = new Dictionary<string, DxgiOutputLocation>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var factory = new Factory1();
                var adapters = factory.Adapters1;
                for (var adapterIndex = 0; adapterIndex < adapters.Length; adapterIndex++)
                {
                    using var adapter = adapters[adapterIndex];
                    var outputs = adapter.Outputs;
                    for (var outputIndex = 0; outputIndex < outputs.Length; outputIndex++)
                    {
                        using var output = outputs[outputIndex];
                        var deviceName = output.Description.DeviceName;
                        if (!string.IsNullOrWhiteSpace(deviceName) && !result.ContainsKey(deviceName))
                        {
                            result[deviceName] = new DxgiOutputLocation(adapterIndex, outputIndex);
                        }
                    }
                }
            }
            catch
            {
                // A missing DXGI identity must fall back to the bitmap capture
                // path; guessing an index can silently stream another monitor.
            }
            return result;
        }

        private static DisplayDevice GetDisplayDevice(string deviceName)
        {
            var device = new DisplayDevice
            {
                Size = Marshal.SizeOf(typeof(DisplayDevice))
            };

            NativeMethods.EnumDisplayDevices(deviceName, 0, ref device, 0);
            return device;
        }

        private static bool IsVibeDeckDisplayDevice(DisplayDevice device)
        {
            return ContainsVibeDeckIdentity(device.DeviceString)
                || ContainsVibeDeckIdentity(device.DeviceId)
                || ContainsVibeDeckIdentity(device.DeviceKey)
                || ContainsVibeDeckDriver(device.DeviceString)
                || ContainsVibeDeckDriver(device.DeviceId)
                || ContainsVibeDeckDriver(device.DeviceKey)
                || ContainsMicrosoftSampleMonitor(device.DeviceString)
                || ContainsMicrosoftSampleMonitor(device.DeviceId);
        }

        private static bool ContainsVibeDeckIdentity(string value)
        {
            return value != null && value.IndexOf("VibeDeck", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsMicrosoftSampleMonitor(string value)
        {
            if (value == null)
            {
                return false;
            }

            return value.IndexOf("DELD0E6", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("S2719DGF", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsVibeDeckDriver(string value)
        {
            if (value == null)
            {
                return false;
            }

            return value.IndexOf("MttVDD", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("MTT1337", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("Virtual Display Driver", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class DisplayInfo
    {
        public string DeviceName { get; set; }
        public string FriendlyName { get; set; }
        public string DeviceId { get; set; }
        public int AdapterIndex { get; set; } = -1;
        public int OutputIndex { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsVibeDeckDisplay { get; set; }
    }

    internal readonly struct DxgiOutputLocation
    {
        public DxgiOutputLocation(int adapterIndex, int outputIndex)
        {
            AdapterIndex = adapterIndex;
            OutputIndex = outputIndex;
        }

        public int AdapterIndex { get; }
        public int OutputIndex { get; }
    }
}
