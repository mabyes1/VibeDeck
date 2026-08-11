using System.Diagnostics;
using System.Linq;
using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class H264AnnexBStreamerTests
    {
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
            Assert.DoesNotContain("-surfaces", startInfo.ArgumentList);
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
