using System;
using System.Collections.Generic;

namespace VibeDeck.Host.Streaming
{
    internal sealed class GpuStreamQualityProfile
    {
        public string Name { get; }
        public int Fps { get; }
        public int Quality { get; }

        public GpuStreamQualityProfile(string name, int fps, int quality)
        {
            Name = name;
            Fps = Math.Max(1, Math.Min(60, fps));
            Quality = Math.Max(25, Math.Min(85, quality));
        }
    }

    internal static class GpuStreamQualityLadder
    {
        private const int MinimumBitrateKbps = 700;
        private const int MaximumBitrateKbps = 10000;

        public static IReadOnlyList<GpuStreamQualityProfile> Create(int requestedFps, int requestedQuality)
        {
            requestedFps = Math.Max(1, Math.Min(60, requestedFps));
            requestedQuality = Math.Max(25, Math.Min(85, requestedQuality));

            var profiles = new List<GpuStreamQualityProfile>();
            AddUnique(profiles, new GpuStreamQualityProfile("full", requestedFps, requestedQuality));
            AddUnique(profiles, new GpuStreamQualityProfile(
                "balanced",
                Math.Min(45, requestedFps),
                Math.Min(60, Math.Max(25, requestedQuality - 4))));
            AddUnique(profiles, new GpuStreamQualityProfile(
                "safe",
                Math.Min(30, requestedFps),
                Math.Min(52, Math.Max(25, requestedQuality - 10))));
            return profiles;
        }

        public static GpuStreamQualityProfile CreateSoftwareFallback(int requestedFps, int requestedQuality)
        {
            return new GpuStreamQualityProfile(
                "software-safe",
                Math.Min(30, requestedFps),
                Math.Min(52, requestedQuality));
        }

        public static (int Width, int Height) FitWithin(int width, int height, int maxWidth = 2560, int maxHeight = 1440)
        {
            width = Math.Max(2, width);
            height = Math.Max(2, height);
            var scale = Math.Min(1d, Math.Min(maxWidth / (double)width, maxHeight / (double)height));
            var fittedWidth = MakeEven((int)Math.Floor(width * scale));
            var fittedHeight = MakeEven((int)Math.Floor(height * scale));
            return (Math.Max(2, fittedWidth), Math.Max(2, fittedHeight));
        }

        public static int EstimateBitrateKbps(int width, int height, int fps, int quality)
        {
            var normalizedQuality = (Math.Max(25, Math.Min(85, quality)) - 25) / 60.0;
            // Desktop UI needs substantially more bitrate than camera-like
            // content at the same resolution: text edges and thin strokes do
            // not hide compression errors. The quality ladder is intentionally
            // conservative for LAN/remote use, while the global cap still
            // prevents an extreme display mode from running away.
            var pixelsPerSecond = (double)Math.Max(2, width) *
                Math.Max(2, height) *
                Math.Max(1, Math.Min(60, fps));
            var bitsPerPixel = 0.090 + (normalizedQuality * 0.140);
            var bitrate = pixelsPerSecond * bitsPerPixel / 1000.0;
            if (bitrate <= MinimumBitrateKbps)
            {
                return MinimumBitrateKbps;
            }

            if (bitrate >= MaximumBitrateKbps)
            {
                return MaximumBitrateKbps;
            }

            return (int)Math.Round(bitrate);
        }

        private static void AddUnique(List<GpuStreamQualityProfile> profiles, GpuStreamQualityProfile profile)
        {
            foreach (var existing in profiles)
            {
                if (existing.Fps == profile.Fps && existing.Quality == profile.Quality)
                {
                    return;
                }
            }
            profiles.Add(profile);
        }

        private static int MakeEven(int value)
        {
            return value - (value % 2);
        }
    }
}
