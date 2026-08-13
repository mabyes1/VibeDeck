using System.Diagnostics;
using System.Linq;
using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class H264AnnexBStreamerTests
    {
        [Fact]
        public void WebRtcCodecContract_AdvertisesConstrainedBaselineWithLevelAsymmetry()
        {
            Assert.Equal("constrained-baseline", H264WebRtcCodecContract.AdvertisedProfile);
            Assert.Contains("profile-level-id=42e01f", H264WebRtcCodecContract.SdpFormatParameters);
            Assert.Contains("packetization-mode=1", H264WebRtcCodecContract.SdpFormatParameters);
            Assert.Contains("level-asymmetry-allowed=1", H264WebRtcCodecContract.SdpFormatParameters);
            Assert.Equal(36, H264WebRtcCodecContract.KeyFrameIntervalFrames(18));
        }

        [Fact]
        public void CreateFfmpegStartInfo_BitmapNvencOptionsAppearOnce()
        {
            var startInfo = H264AnnexBStreamer.CreateFfmpegStartInfo(
                "ffmpeg.exe",
                "h264_nvenc",
                width: 1024,
                height: 576,
                fps: 30,
                bitrateKbps: 2500,
                nvencPreset: null);

            Assert.Equal("p4", ArgumentValue(startInfo, "-preset"));
            Assert.Equal("120k", ArgumentValue(startInfo, "-bufsize"));
            Assert.Equal("baseline", ArgumentValue(startInfo, "-profile:v"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-profile:v"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-aud"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-surfaces"));
        }

        [Fact]
        public void CreateFfmpegStartInfo_BitmapFallbackRetainsTwoFrameVbv()
        {
            var startInfo = H264AnnexBStreamer.CreateFfmpegStartInfo(
                "ffmpeg.exe",
                "libx264",
                width: 1024,
                height: 576,
                fps: 30,
                bitrateKbps: 2500,
                nvencPreset: null);

            Assert.Equal("167k", ArgumentValue(startInfo, "-bufsize"));
            Assert.Equal("baseline", ArgumentValue(startInfo, "-profile:v"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-profile:v"));
            Assert.DoesNotContain("-surfaces", startInfo.ArgumentList);
        }

        [Theory]
        [InlineData("h264_nvenc", "baseline")]
        [InlineData("h264_qsv", "baseline")]
        [InlineData("h264_amf", "constrained_baseline")]
        [InlineData("libx264", "baseline")]
        public void CreateFfmpegStartInfo_MatchesAdvertisedWebRtcProfile(string encoderName, string expectedProfile)
        {
            var startInfo = H264AnnexBStreamer.CreateFfmpegStartInfo(
                "ffmpeg.exe",
                encoderName,
                width: 1280,
                height: 800,
                fps: 60,
                bitrateKbps: 10000,
                nvencPreset: null);

            Assert.Equal(expectedProfile, ArgumentValue(startInfo, "-profile:v"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-profile:v"));
        }

        private static string ArgumentValue(ProcessStartInfo startInfo, string argument)
        {
            var index = startInfo.ArgumentList.IndexOf(argument);
            Assert.True(index >= 0, $"Missing FFmpeg argument: {argument}");
            Assert.True(index + 1 < startInfo.ArgumentList.Count, $"Missing value for FFmpeg argument: {argument}");
            return startInfo.ArgumentList[index + 1];
        }

        private static int ArgumentOccurrence(ProcessStartInfo startInfo, string argument)
        {
            return startInfo.ArgumentList.Count(item => item == argument);
        }
    }
}
