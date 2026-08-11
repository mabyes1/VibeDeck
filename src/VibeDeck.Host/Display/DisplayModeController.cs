using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VibeDeck.Host.Windows;

namespace VibeDeck.Host.Display
{
    public sealed class DisplayModeController
    {
        private readonly DisplayCatalog displays;

        public DisplayModeController(DisplayCatalog displays)
        {
            this.displays = displays;
        }

        public IReadOnlyList<DisplayModePreset> GetPresets()
        {
            return new[]
            {
                new DisplayModePreset { Id = "iphonexs-css-812x375", Label = "iPhone XS exact 812x375", Width = 812, Height = 375, RefreshRate = 60, Kind = "iphone-xs" },
                new DisplayModePreset { Id = "iphonexs-large-974x450", Label = "iPhone XS large 974x450", Width = 974, Height = 450, RefreshRate = 60, Kind = "iphone-xs" },
                new DisplayModePreset { Id = "iphonexs-comfy-1082x500", Label = "iPhone XS comfy 1082x500", Width = 1082, Height = 500, RefreshRate = 60, Kind = "iphone-xs" },
                new DisplayModePreset { Id = "iphonexs-sharp-1218x562", Label = "iPhone XS sharp 1218x562", Width = 1218, Height = 562, RefreshRate = 60, Kind = "iphone-xs" },
                new DisplayModePreset { Id = "galaxys23-large-1170x540", Label = "Galaxy S23 large 1170x540", Width = 1170, Height = 540, RefreshRate = 60, Kind = "galaxy-s23" },
                new DisplayModePreset { Id = "galaxys23-comfy-1404x648", Label = "Galaxy S23 comfy 1404x648", Width = 1404, Height = 648, RefreshRate = 60, Kind = "galaxy-s23" },
                new DisplayModePreset { Id = "galaxys23-sharp-1560x720", Label = "Galaxy S23 sharp 1560x720", Width = 1560, Height = 720, RefreshRate = 60, Kind = "galaxy-s23" },
                new DisplayModePreset { Id = "htcuu-huge-854x480", Label = "HTC UU huge 854x480", Width = 854, Height = 480, RefreshRate = 60, Kind = "htc-u-ultra" },
                new DisplayModePreset { Id = "htcuu-large-960x540", Label = "HTC UU large 960x540", Width = 960, Height = 540, RefreshRate = 60, Kind = "htc-u-ultra" },
                new DisplayModePreset { Id = "htcuu-comfy-1024x576", Label = "HTC UU comfy 1024x576", Width = 1024, Height = 576, RefreshRate = 60, Kind = "htc-u-ultra" },
                new DisplayModePreset { Id = "htcuu-balanced-1152x648", Label = "HTC UU balanced 1152x648", Width = 1152, Height = 648, RefreshRate = 60, Kind = "htc-u-ultra" },
                new DisplayModePreset { Id = "htcuu-sharp-1280x720", Label = "HTC UU sharp 1280x720", Width = 1280, Height = 720, RefreshRate = 60, Kind = "htc-u-ultra" },
                new DisplayModePreset { Id = "tablet-1280x800", Label = "Tablet 16:10 1280x800", Width = 1280, Height = 800, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1440x900", Label = "Tablet 16:10 1440x900", Width = 1440, Height = 900, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1600x1000", Label = "Tablet 16:10 1600x1000", Width = 1600, Height = 1000, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1920x1200", Label = "Tablet 16:10 1920x1200", Width = 1920, Height = 1200, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1024x768", Label = "Tablet 4:3 1024x768", Width = 1024, Height = 768, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1280x960", Label = "Tablet 4:3 1280x960", Width = 1280, Height = 960, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1440x1080", Label = "Tablet 4:3 1440x1080", Width = 1440, Height = 1080, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1200x800", Label = "Tablet 3:2 1200x800", Width = 1200, Height = 800, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1440x960", Label = "Tablet 3:2 1440x960", Width = 1440, Height = 960, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "tablet-1536x1024", Label = "Tablet 3:2 1536x1024", Width = 1536, Height = 1024, RefreshRate = 60, Kind = "tablet" },
                new DisplayModePreset { Id = "standard-1366x768", Label = "Standard 1366x768", Width = 1366, Height = 768, RefreshRate = 60, Kind = "standard" },
                new DisplayModePreset { Id = "standard-1600x900", Label = "Standard 1600x900", Width = 1600, Height = 900, RefreshRate = 60, Kind = "standard" },
                new DisplayModePreset { Id = "standard-1920x1080", Label = "Standard 1920x1080", Width = 1920, Height = 1080, RefreshRate = 60, Kind = "standard" },
                new DisplayModePreset { Id = "portrait-1440x2560", Label = "HTC UU portrait 1440x2560", Width = 1440, Height = 2560, RefreshRate = 60, Kind = "phone-portrait" },
                new DisplayModePreset { Id = "portrait-1080x1920", Label = "Portrait 1080x1920", Width = 1080, Height = 1920, RefreshRate = 60, Kind = "phone-portrait" },
                new DisplayModePreset { Id = "tablet-portrait-800x1280", Label = "Tablet portrait 800x1280", Width = 800, Height = 1280, RefreshRate = 60, Kind = "tablet-portrait" },
                new DisplayModePreset { Id = "tablet-portrait-900x1440", Label = "Tablet portrait 900x1440", Width = 900, Height = 1440, RefreshRate = 60, Kind = "tablet-portrait" },
                new DisplayModePreset { Id = "tablet-portrait-1000x1600", Label = "Tablet portrait 1000x1600", Width = 1000, Height = 1600, RefreshRate = 60, Kind = "tablet-portrait" },
                new DisplayModePreset { Id = "tablet-portrait-768x1024", Label = "Tablet portrait 768x1024", Width = 768, Height = 1024, RefreshRate = 60, Kind = "tablet-portrait" },
                new DisplayModePreset { Id = "tablet-portrait-960x1280", Label = "Tablet portrait 960x1280", Width = 960, Height = 1280, RefreshRate = 60, Kind = "tablet-portrait" },
                new DisplayModePreset { Id = "tablet-portrait-800x1200", Label = "Tablet portrait 800x1200", Width = 800, Height = 1200, RefreshRate = 60, Kind = "tablet-portrait" },
                new DisplayModePreset { Id = "tablet-portrait-960x1440", Label = "Tablet portrait 960x1440", Width = 960, Height = 1440, RefreshRate = 60, Kind = "tablet-portrait" },
                new DisplayModePreset { Id = "tablet-portrait-1024x1536", Label = "Tablet portrait 1024x1536", Width = 1024, Height = 1536, RefreshRate = 60, Kind = "tablet-portrait" },
            };
        }

        public ApplyDisplayModeResult Apply(int width, int height, int refreshRate)
        {
            var display = FindPhoneDisplay();
            if (display == null)
            {
                return ApplyDisplayModeResult.Failed("VibeDeck virtual display was not found.");
            }

            var devMode = new DevMode
            {
                Size = (ushort)Marshal.SizeOf(typeof(DevMode)),
                Fields = 0x00080000 | 0x00100000 | 0x00400000,
                PelsWidth = (uint)width,
                PelsHeight = (uint)height,
                DisplayFrequency = (uint)refreshRate
            };

            const uint cdsUpdateRegistry = 0x00000001;
            var result = NativeMethods.ChangeDisplaySettingsEx(display.DeviceName, ref devMode, IntPtr.Zero, cdsUpdateRegistry, IntPtr.Zero);

            return new ApplyDisplayModeResult
            {
                Success = result == 0,
                ResultCode = result,
                DeviceName = display.DeviceName,
                Width = width,
                Height = height,
                RefreshRate = refreshRate,
                Message = result == 0
                    ? $"Applied {width}x{height}@{refreshRate} to {display.DeviceName}."
                    : $"Windows rejected {width}x{height}@{refreshRate} for {display.DeviceName}. Result code: {result}."
            };
        }

        private DisplayInfo FindPhoneDisplay()
        {
            foreach (var display in displays.GetDisplays())
            {
                if (display.IsVibeDeckDisplay)
                {
                    return display;
                }
            }

            return null;
        }
    }

    public sealed class ApplyDisplayModeResult
    {
        public bool Success { get; set; }
        public int ResultCode { get; set; }
        public string DeviceName { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
        public string Message { get; set; }

        public static ApplyDisplayModeResult Failed(string message)
        {
            return new ApplyDisplayModeResult
            {
                Success = false,
                ResultCode = int.MinValue,
                Message = message
            };
        }
    }
}
