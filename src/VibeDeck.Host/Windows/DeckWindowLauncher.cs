using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace VibeDeck.Host.Windows
{
    public sealed class DeckWindowLauncher
    {
        private const int ShowWindowRestore = 9;
        private const uint WindowMessageClose = 0x0010;
        private const uint SetWindowPosShowWindow = 0x0040;
        private static readonly IntPtr HwndTop = IntPtr.Zero;

        private readonly DisplayCatalog displays;

        public DeckWindowLauncher(DisplayCatalog displays)
        {
            this.displays = displays;
        }

        public DeckLaunchResult Launch(string url, string mode)
        {
            var display = displays.GetDisplays().FirstOrDefault(item => item.IsVibeDeckDisplay);
            if (display == null)
            {
                return DeckLaunchResult.Fail("找不到 VibeDeck 虛擬螢幕。請先在 Windows 顯示設定確認虛擬顯示器已安裝。");
            }

            var browser = FindBrowser();
            if (string.IsNullOrWhiteSpace(browser))
            {
                return DeckLaunchResult.Fail("找不到 Edge 或 Chrome，無法開啟 Deck 視窗。");
            }

            var profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VibeDeck",
                "deck-browser");
            Directory.CreateDirectory(profile);
            CloseExistingDeckWindows(display);

            var startInfo = new ProcessStartInfo(browser)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(browser)
            };
            startInfo.ArgumentList.Add($"--user-data-dir={profile}");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--disable-translate");
            startInfo.ArgumentList.Add("--new-window");
            startInfo.ArgumentList.Add($"--window-position={display.Left},{display.Top}");
            startInfo.ArgumentList.Add($"--window-size={display.Width},{display.Height}");
            startInfo.ArgumentList.Add("--start-fullscreen");
            startInfo.ArgumentList.Add($"--app={url}");

            try
            {
                var process = Process.Start(startInfo);
                var positioned = TryPositionWindow(process, display);
                return new DeckLaunchResult
                {
                    Success = true,
                    Message = positioned
                        ? $"Deck 視窗已開到 {display.FriendlyName}。"
                        : $"Deck 視窗已啟動，但 Windows 尚未回報視窗控制代碼；若沒有出現在 {display.FriendlyName}，請再按一次。",
                    Url = url,
                    Mode = mode,
                    DisplayName = display.FriendlyName,
                    DisplayDeviceName = display.DeviceName,
                    BrowserPath = browser,
                    WindowPositioned = positioned
                };
            }
            catch (Exception ex)
            {
                return DeckLaunchResult.Fail($"Deck 視窗啟動失敗：{ex.Message}");
            }
        }

        public DeckLaunchResult ReturnToPrimary()
        {
            var vibeDeckDisplay = displays.GetDisplays().FirstOrDefault(item => item.IsVibeDeckDisplay);
            var handle = FindDeckWindowHandle(vibeDeckDisplay);
            if (handle == IntPtr.Zero)
            {
                return DeckLaunchResult.Fail("找不到需要召回的 Deck 視窗。");
            }

            var display = displays.GetDisplays()
                .FirstOrDefault(item => item.IsPrimary)
                ?? displays.GetDisplays().FirstOrDefault(item => !item.IsVibeDeckDisplay)
                ?? displays.GetDisplays().FirstOrDefault();
            if (display == null)
            {
                return DeckLaunchResult.Fail("找不到主螢幕，無法召回 Deck 視窗。");
            }

            ReturnWindowToDisplay(handle, display);
            return new DeckLaunchResult
            {
                Success = true,
                Message = $"Deck 視窗已移回 {display.FriendlyName}。",
                Mode = "return",
                DisplayName = display.FriendlyName,
                DisplayDeviceName = display.DeviceName,
                WindowPositioned = true
            };
        }

        private static bool TryPositionWindow(Process process, DisplayInfo display)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var handle = GetProcessWindowHandle(process);
                if (handle == IntPtr.Zero)
                {
                    handle = FindDeckWindowHandle(display);
                }

                if (handle != IntPtr.Zero)
                {
                    PositionWindow(handle, display);
                    return true;
                }

                Thread.Sleep(120);
            }

            return false;
        }

        private static void CloseExistingDeckWindows(DisplayInfo display)
        {
            for (var i = 0; i < 8; i++)
            {
                var handle = FindDeckWindowHandle(display);
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                NativeMethods.PostMessage(handle, WindowMessageClose, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(180);
            }
        }

        private static IntPtr GetProcessWindowHandle(Process process)
        {
            if (process == null)
            {
                return IntPtr.Zero;
            }

            try
            {
                process.Refresh();
                return process.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static IntPtr FindDeckWindowHandle(DisplayInfo display = null)
        {
            var foundByTitle = IntPtr.Zero;
            var foundByDisplay = IntPtr.Zero;
            NativeMethods.EnumWindows((handle, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(handle))
                {
                    return true;
                }

                var className = ReadClassName(handle);
                if (className.IndexOf("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }

                var title = ReadWindowText(handle);
                if (title.IndexOf("VibeDeck Deck", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foundByTitle = handle;
                    return false;
                }

                if (display != null && IsWindowOnDisplay(handle, display))
                {
                    foundByDisplay = handle;
                }

                return true;
            }, IntPtr.Zero);
            return foundByTitle != IntPtr.Zero ? foundByTitle : foundByDisplay;
        }

        private static void PositionWindow(IntPtr handle, DisplayInfo display)
        {
            if (!IsWindowOnDisplay(handle, display))
            {
                NativeMethods.SetWindowPos(
                    handle,
                    HwndTop,
                    display.Left,
                    display.Top,
                    Math.Max(320, display.Width),
                    Math.Max(240, display.Height),
                    SetWindowPosShowWindow);
            }

            NativeMethods.SetForegroundWindow(handle);
        }

        private static bool IsWindowOnDisplay(IntPtr handle, DisplayInfo display)
        {
            if (!NativeMethods.GetWindowRect(handle, out var rect))
            {
                return false;
            }

            var overlapLeft = Math.Max(rect.Left, display.Left);
            var overlapTop = Math.Max(rect.Top, display.Top);
            var overlapRight = Math.Min(rect.Right, display.Left + display.Width);
            var overlapBottom = Math.Min(rect.Bottom, display.Top + display.Height);
            var overlapWidth = Math.Max(0, overlapRight - overlapLeft);
            var overlapHeight = Math.Max(0, overlapBottom - overlapTop);
            return overlapWidth >= Math.Min(160, display.Width / 3) &&
                overlapHeight >= Math.Min(120, display.Height / 3);
        }

        private static void ReturnWindowToDisplay(IntPtr handle, DisplayInfo display)
        {
            var margin = 64;
            var x = display.Left + margin;
            var y = display.Top + margin;
            var width = Math.Max(640, Math.Min(1180, display.Width - margin * 2));
            var height = Math.Max(420, Math.Min(760, display.Height - margin * 2));

            NativeMethods.ShowWindow(handle, ShowWindowRestore);
            NativeMethods.SetWindowPos(
                handle,
                HwndTop,
                x,
                y,
                width,
                height,
                SetWindowPosShowWindow);
            NativeMethods.SetForegroundWindow(handle);
        }


        private static string ReadWindowText(IntPtr handle)
        {
            var text = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(handle, text, text.Capacity);
            return text.ToString();
        }

        private static string ReadClassName(IntPtr handle)
        {
            var text = new System.Text.StringBuilder(128);
            NativeMethods.GetClassName(handle, text, text.Capacity);
            return text.ToString();
        }

        private static string FindBrowser()
        {
            var configured = Environment.GetEnvironmentVariable("VIBEDECK_BROWSER");
            var candidates = new[]
            {
                configured,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            };

            return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        }
    }

    public sealed class DeckLaunchResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Url { get; set; }
        public string Mode { get; set; }
        public string DisplayName { get; set; }
        public string DisplayDeviceName { get; set; }
        public string BrowserPath { get; set; }
        public bool WindowPositioned { get; set; }

        public static DeckLaunchResult Fail(string message)
        {
            return new DeckLaunchResult
            {
                Success = false,
                Message = message
            };
        }
    }
}
