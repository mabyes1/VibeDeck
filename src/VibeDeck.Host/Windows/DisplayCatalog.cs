using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

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
                    OutputIndex = displays.Count,
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

            return displays
                .OrderByDescending(d => d.IsVibeDeckDisplay)
                .ThenBy(d => d.Left)
                .ThenBy(d => d.Top)
                .ToList();
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
        public int OutputIndex { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsVibeDeckDisplay { get; set; }
    }
}
