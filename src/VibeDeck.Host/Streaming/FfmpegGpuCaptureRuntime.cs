using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Owns only FFmpeg's D3D11 desktop-capture fast path. The existing bitmap
    /// pipeline remains independent and is the compatibility fallback.
    /// </summary>
    internal sealed class FfmpegGpuCaptureRuntime
    {
        private readonly object syncRoot = new object();
        private string ffmpegPath;
        private bool capabilitiesChecked;
        private bool captureFiltersAvailable;
        private string[] hardwareEncoders = Array.Empty<string>();

        public bool IsAvailable
        {
            get
            {
                EnsureCapabilities();
                return captureFiltersAvailable && hardwareEncoders.Length > 0;
            }
        }

        public IReadOnlyList<string> HardwareEncoders
        {
            get
            {
                EnsureCapabilities();
                return hardwareEncoders;
            }
        }

        public Process Start(
            string encoderName,
            int outputIndex,
            int width,
            int height,
            int fps,
            int bitrateKbps)
        {
            EnsureCapabilities();
            if (!captureFiltersAvailable || string.IsNullOrWhiteSpace(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg D3D11 capture filters are unavailable.");
            }

            var startInfo = CreateStartInfo(
                ffmpegPath,
                encoderName,
                outputIndex,
                width,
                height,
                fps,
                bitrateKbps);
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = false
            };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("Unable to start FFmpeg GPU capture.");
            }
            return process;
        }

        internal static ProcessStartInfo CreateStartInfo(
            string executable,
            string encoderName,
            int outputIndex,
            int width,
            int height,
            int fps,
            int bitrateKbps)
        {
            return CreateStartInfo(
                executable,
                encoderName,
                outputIndex,
                width,
                height,
                fps,
                bitrateKbps,
                NvencEncoderOptions.GetConfiguredPreset());
        }

        internal static ProcessStartInfo CreateStartInfo(
            string executable,
            string encoderName,
            int outputIndex,
            int width,
            int height,
            int fps,
            int bitrateKbps,
            string nvencPreset)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var bufferKbps = FfmpegVbvBufferPlanner.CalculateBufferKbps(encoderName, bitrateKbps, fps);
            var filter = $"ddagrab=output_idx={Math.Max(0, outputIndex)}:draw_mouse=1:framerate={fps}," +
                $"scale_d3d11=width={width}:height={height}:format=nv12";

            AddArgs(startInfo,
                "-hide_banner",
                "-loglevel", "warning",
                "-nostdin",
                "-fflags", "nobuffer",
                "-flags", "low_delay",
                "-filter_complex", filter,
                "-an",
                "-c:v", encoderName);

            AddEncoderOptions(startInfo, encoderName, nvencPreset);
            AddArgs(startInfo,
                "-g", fps.ToString(),
                "-keyint_min", fps.ToString(),
                "-refs", "1",
                "-b:v", $"{bitrateKbps}k",
                "-maxrate", $"{bitrateKbps}k",
                "-bufsize", $"{bufferKbps}k",
                "-flush_packets", "1",
                "-f", "h264",
                "pipe:1");
            return startInfo;
        }

        public static async Task<string> ReadErrorTailAsync(Process process)
        {
            var tail = new StringBuilder();
            var buffer = new char[2048];
            int read;
            while ((read = await process.StandardError.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                tail.Append(buffer, 0, read);
                if (tail.Length > 16 * 1024)
                {
                    tail.Remove(0, tail.Length - 16 * 1024);
                }
            }
            return tail.ToString().Trim();
        }

        private void EnsureCapabilities()
        {
            lock (syncRoot)
            {
                if (capabilitiesChecked)
                {
                    return;
                }
                capabilitiesChecked = true;
                ffmpegPath = FfmpegExecutableLocator.Resolve();
                if (string.IsNullOrWhiteSpace(ffmpegPath))
                {
                    return;
                }

                captureFiltersAvailable = ProbeCaptureFilters(ffmpegPath);
                if (!captureFiltersAvailable)
                {
                    return;
                }

                var available = new List<string>();
                foreach (var encoder in new[] { "h264_nvenc", "h264_qsv", "h264_amf" })
                {
                    if (ProbeEncoder(ffmpegPath, encoder))
                    {
                        available.Add(encoder);
                    }
                }
                hardwareEncoders = available.ToArray();
            }
        }

        private static bool ProbeCaptureFilters(string executable)
        {
            try
            {
                var startInfo = CreateProbeStartInfo(executable, "-filters");
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }
                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 &&
                    output.IndexOf("ddagrab", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    output.IndexOf("scale_d3d11", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool ProbeEncoder(string executable, string encoderName)
        {
            try
            {
                var startInfo = CreateProbeStartInfo(executable,
                    "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi",
                    "-i", "testsrc=duration=0.12:size=1280x720:rate=30",
                    "-vf", "format=yuv420p",
                    "-vcodec", encoderName,
                    "-f", "null", "-");
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static ProcessStartInfo CreateProbeStartInfo(string executable, params string[] args)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            AddArgs(startInfo, args);
            return startInfo;
        }

        private static void AddEncoderOptions(ProcessStartInfo startInfo, string encoderName, string nvencPreset)
        {
            if (string.Equals(encoderName, "h264_nvenc", StringComparison.OrdinalIgnoreCase))
            {
                NvencEncoderOptions.AddArguments(startInfo, nvencPreset);
            }
            else if (string.Equals(encoderName, "h264_qsv", StringComparison.OrdinalIgnoreCase))
            {
                AddArgs(startInfo,
                    "-preset", "veryfast",
                    "-async_depth", "1",
                    "-bf", "0",
                    "-aud", "1");
            }
            else if (string.Equals(encoderName, "h264_amf", StringComparison.OrdinalIgnoreCase))
            {
                AddArgs(startInfo,
                    "-usage", "lowlatency",
                    "-bf", "0",
                    "-aud", "1");
            }
        }

        private static void AddArgs(ProcessStartInfo startInfo, params string[] args)
        {
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }
    }
}
