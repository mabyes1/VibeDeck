using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class GpuStreamQualityLadderTests
    {
        [Fact]
        public void Create_BuildsDistinctLowerTiers()
        {
            var profiles = GpuStreamQualityLadder.Create(60, 80);

            Assert.Collection(
                profiles,
                full => AssertProfile(full, "full", 60, 80),
                balanced => AssertProfile(balanced, "balanced", 45, 60),
                safe => AssertProfile(safe, "safe", 30, 52));
        }

        [Fact]
        public void SoftwareFallback_CapsCpuWork()
        {
            var profile = GpuStreamQualityLadder.CreateSoftwareFallback(60, 85);

            AssertProfile(profile, "software-safe", 30, 52);
        }

        [Fact]
        public void FitWithin_DownscalesAndKeepsEvenDimensions()
        {
            var fitted = GpuStreamQualityLadder.FitWithin(3840, 2160);

            Assert.Equal(2560, fitted.Width);
            Assert.Equal(1440, fitted.Height);
        }

        [Theory]
        [InlineData(320, 200, 1, 25, 700)]
        [InlineData(7680, 4320, 60, 85, 10000)]
        public void EstimateBitrateKbps_RespectsSafetyBounds(
            int width,
            int height,
            int fps,
            int quality,
            int expected)
        {
            Assert.Equal(expected, GpuStreamQualityLadder.EstimateBitrateKbps(width, height, fps, quality));
        }

        [Fact]
        public void EstimateBitrateKbps_P024QualityLadderIsUsefulForDesktopContent()
        {
            var low = GpuStreamQualityLadder.EstimateBitrateKbps(1024, 576, 30, 25);
            var medium = GpuStreamQualityLadder.EstimateBitrateKbps(1024, 576, 30, 48);
            var high = GpuStreamQualityLadder.EstimateBitrateKbps(1024, 576, 30, 85);

            Assert.Equal(1593, low);
            Assert.InRange(medium, 2400, 2600);
            Assert.Equal(2542, medium);
            Assert.Equal(4070, high);
            Assert.True(low < medium);
            Assert.True(medium < high);
        }

        [Fact]
        public void EstimateBitrateKbps_ClampsExtremeModesWithoutFlatteningP024Quality()
        {
            Assert.Equal(700, GpuStreamQualityLadder.EstimateBitrateKbps(2, 2, 1, 25));
            Assert.Equal(10000, GpuStreamQualityLadder.EstimateBitrateKbps(7680, 4320, 60, 85));

            var low = GpuStreamQualityLadder.EstimateBitrateKbps(1024, 576, 30, 25);
            var high = GpuStreamQualityLadder.EstimateBitrateKbps(1024, 576, 30, 85);
            Assert.NotEqual(low, high);
        }

        [Fact]
        public void ApplyReceiverLimit_ScalesEveryCongestionTierBelowReceiverCeiling()
        {
            Assert.Equal(1400, GpuStreamQualityLadder.ApplyReceiverLimit(2648, 1400, 1d));
            Assert.Equal(1092, GpuStreamQualityLadder.ApplyReceiverLimit(2648, 1400, 0.78d));
            Assert.Equal(812, GpuStreamQualityLadder.ApplyReceiverLimit(2648, 1400, 0.58d));
        }

        private static void AssertProfile(
            GpuStreamQualityProfile profile,
            string name,
            int fps,
            int quality)
        {
            Assert.Equal(name, profile.Name);
            Assert.Equal(fps, profile.Fps);
            Assert.Equal(quality, profile.Quality);
        }
    }
}
