using System;

namespace VibeDeck.Host.Windows
{
    public sealed class DisplayFramePacket
    {
        public ArraySegment<byte> JpegBytes { get; set; }
        public DisplayFrameFingerprint Fingerprint { get; set; }
        public bool IsStatusFrame { get; set; }
    }
}
