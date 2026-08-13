using System;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Keeps the H.264 bitstream emitted by every FFmpeg path aligned with the
    /// profile advertised to WebRTC peers. A permissive Windows decoder can
    /// hide a Main-vs-Baseline mismatch that Linux Chromium exposes as corrupt
    /// (often solid purple) video.
    /// </summary>
    internal static class H264WebRtcCodecContract
    {
        internal const string SdpFormatParameters =
            "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f";

        internal const string AdvertisedProfile = "constrained-baseline";

        internal static int KeyFrameIntervalFrames(int fps)
        {
            // NACK repair and PLI-triggered encoder restart now provide loss
            // recovery. A two-second steady-state GOP removes the old 1 Hz IDR
            // burst that made low-bitrate tablet streams visibly hitch.
            return Math.Max(2, Math.Min(240, Math.Max(1, fps) * 2));
        }

        internal static string EncoderProfile(string encoderName)
        {
            return string.Equals(encoderName, "h264_amf", StringComparison.OrdinalIgnoreCase)
                ? "constrained_baseline"
                : "baseline";
        }
    }
}
