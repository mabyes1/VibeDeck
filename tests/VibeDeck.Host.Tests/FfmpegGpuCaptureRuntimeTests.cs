using System;
using System.Diagnostics;
using System.Linq;
using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class FfmpegGpuCaptureRuntimeTests
    {
        [Fact]
        public void CreateStartInfo_UsesDirectD3d11CaptureAndStdoutOnly()
        {
            var startInfo = FfmpegGpuCaptureRuntime.CreateStartInfo(
                "ffmpeg.exe",
                "h264_nvenc",
                outputIndex: 2,
                width: 1920,
                height: 1080,
                fps: 60,
                bitrateKbps: 8000,
                nvencPreset: null);

            Assert.False(startInfo.UseShellExecute);
            Assert.False(startInfo.RedirectStandardInput);
            Assert.True(startInfo.RedirectStandardOutput);
            Assert.True(startInfo.RedirectStandardError);
            Assert.Contains(
                "ddagrab=output_idx=2:draw_mouse=1:framerate=60,scale_d3d11=width=1920:height=1080:format=nv12",
                startInfo.ArgumentList);
            Assert.Contains("-nostdin", startInfo.ArgumentList);
            Assert.Contains("h264_nvenc", startInfo.ArgumentList);
            Assert.Equal("p4", ArgumentValue(startInfo, "-preset"));
            Assert.Contains("ull", startInfo.ArgumentList);
            Assert.Equal("0", ArgumentValue(startInfo, "-delay"));
            Assert.Equal("1", ArgumentValue(startInfo, "-zerolatency"));
            Assert.Equal("0", ArgumentValue(startInfo, "-bf"));
            Assert.Equal("1", ArgumentValue(startInfo, "-refs"));
            Assert.Equal("baseline", ArgumentValue(startInfo, "-profile:v"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-profile:v"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-aud"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-surfaces"));
            Assert.Equal("120", ArgumentValue(startInfo, "-g"));
            Assert.Equal("120", ArgumentValue(startInfo, "-keyint_min"));
            Assert.Equal("134k", ArgumentValue(startInfo, "-bufsize"));
            Assert.Equal("pipe:1", startInfo.ArgumentList.Last());
        }

        [Theory]
        [InlineData("h264_nvenc", "baseline")]
        [InlineData("h264_qsv", "baseline")]
        [InlineData("h264_amf", "constrained_baseline")]
        public void CreateStartInfo_MatchesAdvertisedWebRtcProfile(string encoderName, string expectedProfile)
        {
            var startInfo = FfmpegGpuCaptureRuntime.CreateStartInfo(
                "ffmpeg.exe",
                encoderName,
                outputIndex: 0,
                width: 1280,
                height: 800,
                fps: 60,
                bitrateKbps: 10000,
                nvencPreset: null);

            Assert.Equal(expectedProfile, ArgumentValue(startInfo, "-profile:v"));
            Assert.Equal(1, ArgumentOccurrence(startInfo, "-profile:v"));
        }

        [Theory]
        [InlineData("h264_nvenc", 120)]
        [InlineData("h264_qsv", 167)]
        [InlineData("h264_amf", 167)]
        [InlineData("libx264", 167)]
        public void VbvBufferPlanner_UsesOneFrameOnlyForNvenc(string encoderName, int expectedKbps)
        {
            Assert.Equal(
                expectedKbps,
                FfmpegVbvBufferPlanner.CalculateBufferKbps(encoderName, bitrateKbps: 2500, fps: 30));
        }

        [Theory]
        [InlineData("p1", "p1")]
        [InlineData("P3", "p3")]
        [InlineData(" p4 ", "p4")]
        [InlineData("p2", "p4")]
        [InlineData("--preset=p1", "p4")]
        [InlineData(null, "p4")]
        public void NvencPresetOverride_IsWhitelisted(string configured, string expected)
        {
            Assert.Equal(expected, NvencEncoderOptions.ResolvePreset(configured));

            var startInfo = FfmpegGpuCaptureRuntime.CreateStartInfo(
                "ffmpeg.exe",
                "h264_nvenc",
                outputIndex: 0,
                width: 1024,
                height: 576,
                fps: 30,
                bitrateKbps: 2500,
                nvencPreset: configured);

            Assert.Equal(expected, ArgumentValue(startInfo, "-preset"));
            Assert.Equal("120k", ArgumentValue(startInfo, "-bufsize"));
        }

        [Fact]
        public void NvencPresetOverride_ReadsOnlyTheDedicatedEnvironmentVariable()
        {
            var original = Environment.GetEnvironmentVariable(NvencEncoderOptions.PresetEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(NvencEncoderOptions.PresetEnvironmentVariable, "p1");
                Assert.Equal("p1", NvencEncoderOptions.GetConfiguredPreset());

                Environment.SetEnvironmentVariable(NvencEncoderOptions.PresetEnvironmentVariable, "-preset p1");
                Assert.Equal("p4", NvencEncoderOptions.GetConfiguredPreset());
            }
            finally
            {
                Environment.SetEnvironmentVariable(NvencEncoderOptions.PresetEnvironmentVariable, original);
            }
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
