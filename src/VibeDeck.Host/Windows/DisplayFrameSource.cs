using System;
using System.Drawing;
using System.Linq;
using VibeDeck.Host.Streaming;

namespace VibeDeck.Host.Windows
{
    /// <summary>
    /// Holds a reusable Bitmap reference to allow passing it across async methods
    /// without using 'ref' parameters (which are forbidden in C# async methods).
    /// </summary>
    public sealed class ReusableBitmapHolder : IDisposable
    {
        public Bitmap Value { get; set; }

        public void Dispose()
        {
            Value?.Dispose();
            Value = null;
        }
    }

    /// <summary>
    /// Captures frames from a Windows display device.
    /// Manages DXGI Desktop Duplication API resources.
    /// </summary>
    public sealed class DisplayFrameSource : IDisposable
    {
        private readonly DisplayCatalog catalog;
        private readonly object syncRoot = new object();
        private bool _disposed;

        // DXGI Capturer for hardware-accelerated screen capture.
        private DxgiFrameCapturer _dxgiCapturer;
        private string _dxgiDeviceName;
        private bool _hasEverCaptured;

        public DisplayFrameSource(DisplayCatalog catalog)
        {
            this.catalog = catalog;
        }

        public DisplayFramePacket CaptureFrame(string deviceName, long quality)
        {
            using var holder = new ReusableBitmapHolder();
            using var frame = CaptureBitmapFrame(deviceName, holder);
            return new DisplayFramePacket
            {
                IsStatusFrame = frame.IsStatusFrame,
                Fingerprint = frame.Fingerprint,
                JpegBytes = JpegFrameEncoder.Encode(frame.Bitmap, quality)
            };
        }

        public DisplayBitmapFrame CaptureBitmapFrame(string deviceName, ReusableBitmapHolder holder)
        {
            lock (syncRoot)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(DisplayFrameSource));

                var display = catalog.GetDisplays()
                    .FirstOrDefault(d => string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                    ?? catalog.GetDisplays().FirstOrDefault(d => d.IsVibeDeckDisplay);

                if (display == null || display.Width <= 0 || display.Height <= 0)
                {
                    return CreateStatusBitmapFrame("Display is not available.");
                }

                var width = MakeEven(Math.Min(display.Width, 2560));
                var height = MakeEven(Math.Min(display.Height, 1440));

                // If the holder's bitmap is null or size is incorrect, allocate/reallocate it.
                if (holder.Value == null || holder.Value.Width != width || holder.Value.Height != height)
                {
                    holder.Value?.Dispose();
                    holder.Value = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    _hasEverCaptured = false; // New bitmap is all-black; force GDI on next frame.
                }

                var bitmap = holder.Value;

                bool dxgiSuccess = false;
                bool hasNewFrame = false;

                // Virtual displays (Indirect Display Driver) do not support DXGI Desktop Duplication properly,
                // often timing out indefinitely. We only use DXGI on physical monitors (IsVibeDeckDisplay == false).
                if (!display.IsVibeDeckDisplay)
                {
                    // Manage the life cycle of the DXGI Capturer
                    if (_dxgiCapturer == null || _dxgiDeviceName != display.DeviceName)
                    {
                        _dxgiCapturer?.Dispose();
                        _dxgiCapturer = new DxgiFrameCapturer(display.DeviceName);
                        _dxgiDeviceName = display.DeviceName;
                    }

                    try
                    {
                        // Try hardware-accelerated DXGI capture first
                        dxgiSuccess = _dxgiCapturer.TryCapture(bitmap, out hasNewFrame);
                    }
                    catch
                    {
                        dxgiSuccess = false;
                    }
                }

                // Fallback to GDI capture if DXGI is unsupported or fails (e.g. UAC prompts, device resets)
                // Also falls through when DXGI succeeded but had no new frame AND the bitmap
                // has never been populated (all-black after allocation). This prevents sending
                // black frames on initial connection or after a DXGI reset.
                if (!dxgiSuccess || (dxgiSuccess && !hasNewFrame && !_hasEverCaptured))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(display.Left, display.Top, 0, 0, bitmap.Size);
                    }
                }

                _hasEverCaptured = true;

                // Render the cursor on top of the captured frame
                bool hasCursor = false;
                int cursorX = 0, cursorY = 0;
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    hasCursor = TryDrawCursorIfInsideDisplay(graphics, display, out cursorX, out cursorY);
                }

                return new DisplayBitmapFrame
                {
                    OwnsBitmap = false, // The holder manages the bitmap lifecycle
                    Fingerprint = DisplayFrameFingerprint.Create(bitmap),
                    Bitmap = bitmap,
                    HasCursor = hasCursor,
                    CursorX = cursorX,
                    CursorY = cursorY
                };
            }
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (_disposed) return;
                _disposed = true;

                _dxgiCapturer?.Dispose();
                _dxgiCapturer = null;
            }
        }

        private static int MakeEven(int value)
        {
            return Math.Max(2, value - (value % 2));
        }

        private static bool TryDrawCursorIfInsideDisplay(Graphics graphics, DisplayInfo display, out int cursorX, out int cursorY)
        {
            cursorX = 0;
            cursorY = 0;
            var cursorInfo = new CursorInfo
            {
                Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(CursorInfo))
            };

            const int cursorShowing = 1;
            const int diNormal = 3;

            if (!NativeMethods.GetCursorInfo(ref cursorInfo) || (cursorInfo.Flags & cursorShowing) != cursorShowing)
            {
                return false;
            }

            var localX = cursorInfo.ScreenPosition.X - display.Left;
            var localY = cursorInfo.ScreenPosition.Y - display.Top;

            if (localX < 0 || localY < 0 || localX >= display.Width || localY >= display.Height)
            {
                return false;
            }

            var hdc = graphics.GetHdc();
            try
            {
                NativeMethods.DrawIconEx(hdc, localX, localY, cursorInfo.CursorHandle, 0, 0, 0, IntPtr.Zero, diNormal);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            cursorX = localX;
            cursorY = localY;
            return true;
        }

        private static DisplayBitmapFrame CreateStatusBitmapFrame(string message)
        {
            var bitmap = new Bitmap(1280, 720, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(19, 24, 32));
            using var font = new Font("Segoe UI", 28, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            graphics.DrawString(message, font, brush, 56, 56);
            return new DisplayBitmapFrame
            {
                // Status frames own their own bitmap.
                OwnsBitmap = true,
                IsStatusFrame = true,
                Fingerprint = DisplayFrameFingerprint.Create(bitmap),
                Bitmap = bitmap
            };
        }
    }
}
