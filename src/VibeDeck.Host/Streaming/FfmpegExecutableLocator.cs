using System;
using System.Diagnostics;
using System.IO;

namespace VibeDeck.Host.Streaming
{
    internal static class FfmpegExecutableLocator
    {
        public static string Resolve()
        {
            return ResolveConfiguredPath()
                ?? ResolveFromPath("ffmpeg.exe")
                ?? ResolveFromPath("ffmpeg");
        }

        private static string ResolveConfiguredPath()
        {
            var configured = Environment.GetEnvironmentVariable("VIBEDECK_FFMPEG")
                ?? Environment.GetEnvironmentVariable("PHONE_MONITOR_FFMPEG");
            return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured)
                ? configured
                : null;
        }

        private static string ResolveFromPath(string executable)
        {
            try
            {
                var startInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-version");
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return null;
                }

                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 ? executable : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
