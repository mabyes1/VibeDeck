using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VibeDeck.Host.Sideboard
{
    public sealed class GlanceBoardProxy
    {
        private static readonly Uri BaseUri = new Uri("http://127.0.0.1:45371/");
        private static readonly TimeSpan LocalStatsCacheTtl = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan LocalCollectorTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan WorkPulseRetryDelay = TimeSpan.FromSeconds(15);
        private readonly object localStatsGate = new object();
        private readonly HttpClient client;
        private readonly string collectorPath;
        private string localStatsJson;
        private DateTimeOffset localStatsCollectedAt = DateTimeOffset.MinValue;
        private DateTimeOffset workPulseRetryAfter = DateTimeOffset.MinValue;

        public GlanceBoardProxy()
        {
            client = new HttpClient
            {
                BaseAddress = BaseUri,
                Timeout = TimeSpan.FromMilliseconds(500)
            };
            collectorPath = ResolveCollectorPath();
        }

        public Task<GlanceBoardResponse> GetStatsAsync(CancellationToken cancellationToken)
        {
            return GetLocalStatsAsync(false, cancellationToken);
        }

        public async Task<GlanceBoardResponse> GetWorkPulseAsync(CancellationToken cancellationToken)
        {
            if (DateTimeOffset.UtcNow < workPulseRetryAfter)
            {
                return GlanceBoardResponse.Success(BuildEmptyWorkPulseJson());
            }

            var upstream = await GetJsonAsync("api/work-pulse", cancellationToken);
            if (upstream.IsAvailable)
            {
                return upstream;
            }

            workPulseRetryAfter = DateTimeOffset.UtcNow + WorkPulseRetryDelay;
            return GlanceBoardResponse.Success(BuildEmptyWorkPulseJson());
        }

        public Task<GlanceBoardResponse> RefreshStatsAsync(CancellationToken cancellationToken)
        {
            return GetLocalStatsAsync(true, cancellationToken);
        }

        private async Task<GlanceBoardResponse> GetLocalStatsAsync(bool force, CancellationToken cancellationToken)
        {
            if (!force)
            {
                lock (localStatsGate)
                {
                    if (!string.IsNullOrWhiteSpace(localStatsJson) &&
                        DateTimeOffset.UtcNow - localStatsCollectedAt <= LocalStatsCacheTtl)
                    {
                        return GlanceBoardResponse.Success(localStatsJson);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(collectorPath))
            {
                return GlanceBoardResponse.Failed("VibeDeck sideboard collector was not found.", null);
            }

            var response = await RunLocalCollectorAsync(cancellationToken);
            if (response.IsAvailable)
            {
                lock (localStatsGate)
                {
                    localStatsJson = response.Json;
                    localStatsCollectedAt = DateTimeOffset.UtcNow;
                }
            }

            return response;
        }

        private async Task<GlanceBoardResponse> RunLocalCollectorAsync(CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{collectorPath}\"",
                WorkingDirectory = Path.GetDirectoryName(collectorPath) ?? Directory.GetCurrentDirectory(),
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return GlanceBoardResponse.Failed("VibeDeck sideboard collector could not start.", null);
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                var waitTask = Task.Run(() => process.WaitForExit((int)LocalCollectorTimeout.TotalMilliseconds));
                var completedTask = await Task.WhenAny(waitTask, Task.Delay(LocalCollectorTimeout, cancellationToken));

                if (completedTask != waitTask || !waitTask.Result)
                {
                    TryKill(process);
                    return GlanceBoardResponse.Failed("VibeDeck sideboard collector timed out.", null);
                }

                var stdout = (await stdoutTask).Trim();
                var stderr = (await stderrTask).Trim();

                if (process.ExitCode != 0)
                {
                    return GlanceBoardResponse.Failed($"VibeDeck sideboard collector exited with code {process.ExitCode}.", stderr);
                }

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    return GlanceBoardResponse.Failed("VibeDeck sideboard collector returned no data.", stderr);
                }

                return GlanceBoardResponse.Success(stdout);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TaskCanceledException || ex is Win32Exception)
            {
                return GlanceBoardResponse.Failed($"VibeDeck sideboard collector failed: {ex.Message}", null);
            }
        }

        private async Task<GlanceBoardResponse> GetJsonAsync(string path, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await client.GetAsync(path, cancellationToken);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return GlanceBoardResponse.Failed($"Glance Board returned HTTP {(int)response.StatusCode}.", json);
                }

                return GlanceBoardResponse.Success(json);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                return GlanceBoardResponse.Failed("Glance Board is not running on http://127.0.0.1:45371.", null);
            }
        }

        private static string ResolveCollectorPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Sideboard", "sideboard-stats.ps1"),
                Path.Combine(Directory.GetCurrentDirectory(), "Sideboard", "sideboard-stats.ps1"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "VibeDeck.Host", "Sideboard", "sideboard-stats.ps1")
            };

            foreach (var candidate in candidates)
            {
                var path = Path.GetFullPath(candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static string BuildEmptyWorkPulseJson()
        {
            return JsonSerializer.Serialize(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                sources = new
                {
                    memory = (string)null,
                    log = (string)null
                },
                summary = new
                {
                    title = "工作脈搏",
                    session = (string)null,
                    headline = "目前沒有工作脈搏。",
                    status = "VibeDeck 本機收集器"
                },
                todos = new
                {
                    open = Array.Empty<string>(),
                    done = Array.Empty<string>()
                },
                focus = Array.Empty<object>(),
                radar = new
                {
                    agents = Array.Empty<object>()
                },
                timeline = Array.Empty<object>(),
                dynamics = Array.Empty<string>(),
                soWhat = Array.Empty<string>(),
                recent = Array.Empty<object>()
            });
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }
    }

    public sealed class GlanceBoardResponse
    {
        public bool IsAvailable { get; set; }
        public string Json { get; set; }
        public string Error { get; set; }

        public static GlanceBoardResponse Success(string json)
        {
            return new GlanceBoardResponse
            {
                IsAvailable = true,
                Json = json
            };
        }

        public static GlanceBoardResponse Failed(string error, string json)
        {
            return new GlanceBoardResponse
            {
                IsAvailable = false,
                Error = error,
                Json = json
            };
        }
    }
}
