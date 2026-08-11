using System;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Selects the validated VBV depth for each encoder family. NVENC uses
    /// the low-latency one-frame buffer; fallback encoders retain the previous
    /// two-frame behavior until their one-frame behavior is validated.
    /// </summary>
    internal static class FfmpegVbvBufferPlanner
    {
        internal static int CalculateBufferKbps(string encoderName, int bitrateKbps, int fps)
        {
            if (string.Equals(encoderName, "h264_nvenc", StringComparison.OrdinalIgnoreCase))
            {
                return NvencEncoderOptions.CalculateOneFrameVbvBufferKbps(bitrateKbps, fps);
            }

            var safeBitrate = Math.Max(1, bitrateKbps);
            var safeFps = Math.Max(1, fps);
            return Math.Max(120, (int)Math.Ceiling(safeBitrate * 2.0 / safeFps));
        }
    }
}
