using System;
using System.Drawing;

namespace VibeDeck.Host.Windows
{
    public sealed class DisplayBitmapFrame : IDisposable
    {
        /// <summary>
        /// When false the Bitmap is owned by the pool/source and must not be disposed here.
        /// Set to true (default) for frames whose Bitmap was allocated solely for this frame.
        /// </summary>
        public bool OwnsBitmap { get; set; } = true;

        public Bitmap Bitmap { get; set; }
        public DisplayFrameFingerprint Fingerprint { get; set; }
        public bool IsStatusFrame { get; set; }
        public bool HasCursor { get; set; }
        public int CursorX { get; set; }
        public int CursorY { get; set; }

        public void Dispose()
        {
            if (OwnsBitmap)
            {
                Bitmap?.Dispose();
                Bitmap = null;
            }
        }
    }
}
