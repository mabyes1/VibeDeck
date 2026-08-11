using System;
using System.Diagnostics;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Keeps the NVENC latency/quality trade-off deliberately narrow. The
    /// environment variable accepts only the known FFmpeg preset names; it
    /// is never interpolated into an arbitrary argument list.
    /// </summary>
    internal static class NvencEncoderOptions
    {
        internal const string PresetEnvironmentVariable = "VIBEDECK_NVENC_PRESET";
        internal const string DefaultPreset = "p4";

        public static string GetConfiguredPreset()
        {
            return ResolvePreset(Environment.GetEnvironmentVariable(PresetEnvironmentVariable));
        }

        internal static string ResolvePreset(string configuredPreset)
        {
            switch (configuredPreset?.Trim().ToLowerInvariant())
            {
                case "p1":
                    return "p1";
                case "p3":
                    return "p3";
                case "p4":
                    return "p4";
                default:
                    return DefaultPreset;
            }
        }

        internal static int CalculateOneFrameVbvBufferKbps(int bitrateKbps, int fps)
        {
            var safeBitrate = Math.Max(1, bitrateKbps);
            var safeFps = Math.Max(1, fps);
            // Keep approximately one frame of VBV. Retain the existing 120k
            // floor because FFmpeg/NVENC reject or behave poorly with tiny
            // buffers at very low rates.
            return Math.Max(120, (int)Math.Ceiling(safeBitrate / (double)safeFps));
        }

        internal static void AddArguments(ProcessStartInfo startInfo, string configuredPreset)
        {
            AddArgument(startInfo, "-preset");
            AddArgument(startInfo, ResolvePreset(configuredPreset));
            AddArgument(startInfo, "-tune");
            AddArgument(startInfo, "ull");
            AddArgument(startInfo, "-delay");
            AddArgument(startInfo, "0");
            AddArgument(startInfo, "-zerolatency");
            AddArgument(startInfo, "1");
            AddArgument(startInfo, "-bf");
            AddArgument(startInfo, "0");
            AddArgument(startInfo, "-forced-idr");
            AddArgument(startInfo, "1");
            AddArgument(startInfo, "-aud");
            AddArgument(startInfo, "1");
            AddArgument(startInfo, "-surfaces");
            AddArgument(startInfo, "2");
        }

        private static void AddArgument(ProcessStartInfo startInfo, string argument)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }
}
