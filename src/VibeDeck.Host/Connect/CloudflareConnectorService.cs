using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32.SafeHandles;
using VibeDeck.Host.Diagnostics;

namespace VibeDeck.Host.Connect
{
    public sealed class CloudflareConnectorService : BackgroundService
    {
        private const string LegacyTaskName = "VibeDeck Cloudflare Connector";
        private readonly object gate = new object();
        private readonly PublicEndpointService publicEndpoint;
        private readonly CloudflareProvisioningClient provisioningClient;
        private readonly AuditTrailService audit;
        private readonly HttpClient healthClient;
        private readonly ManagedTunnelStateStore stateStore;
        private readonly KillOnCloseJob connectorJob = new KillOnCloseJob();
        private readonly bool enabled;
        private Process connectorProcess;
        private ManagedConnectorSnapshot snapshot;

        public CloudflareConnectorService(
            PublicEndpointService publicEndpoint,
            CloudflareProvisioningClient provisioningClient,
            AuditTrailService audit,
            IConfiguration configuration)
            : this(
                publicEndpoint,
                provisioningClient,
                audit,
                new HttpClient { Timeout = TimeSpan.FromSeconds(5) },
                new ManagedTunnelStateStore(),
                ResolveEnabled(configuration))
        {
        }

        internal CloudflareConnectorService(
            PublicEndpointService publicEndpoint,
            CloudflareProvisioningClient provisioningClient,
            AuditTrailService audit,
            HttpClient healthClient,
            ManagedTunnelStateStore stateStore,
            bool enabled)
        {
            this.publicEndpoint = publicEndpoint ?? throw new ArgumentNullException(nameof(publicEndpoint));
            this.provisioningClient = provisioningClient ?? throw new ArgumentNullException(nameof(provisioningClient));
            this.audit = audit;
            this.healthClient = healthClient ?? throw new ArgumentNullException(nameof(healthClient));
            this.stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            this.enabled = enabled;
            snapshot = new ManagedConnectorSnapshot
            {
                IsManaged = enabled,
                State = enabled ? "waiting" : "disabled"
            };
        }

        public ManagedConnectorSnapshot GetSnapshot()
        {
            lock (gate)
            {
                return snapshot.Clone();
            }
        }

        public bool ShouldAdvertise(string publicUrl)
        {
            if (!enabled) return true;
            lock (gate)
            {
                return !string.IsNullOrWhiteSpace(publicUrl) &&
                    string.Equals(snapshot.PublicUrl, publicUrl, StringComparison.OrdinalIgnoreCase) &&
                    (snapshot.IsRunning || snapshot.IsHealthy);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!enabled) return;

            await RetireLegacyScheduledTaskAsync(stoppingToken);
            var restartDelay = TimeSpan.FromSeconds(2);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var launch = await PrepareLaunchAsync(stoppingToken);
                    if (launch == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        continue;
                    }

                    await RunConnectorAsync(launch, stoppingToken);
                    restartDelay = TimeSpan.FromSeconds(2);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    UpdateSnapshot("error", false, false, null, error.Message);
                    audit?.RecordException("managed-connector", "run", error);
                    await Task.Delay(restartDelay, stoppingToken);
                    restartDelay = TimeSpan.FromSeconds(Math.Min(60, restartDelay.TotalSeconds * 2));
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            StopConnectorProcess();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            connectorJob.Dispose();
            base.Dispose();
        }

        private async Task<ConnectorLaunch> PrepareLaunchAsync(CancellationToken cancellationToken)
        {
            var executable = FindCloudflaredExecutable();
            if (string.IsNullOrWhiteSpace(executable))
            {
                throw new FileNotFoundException("VibeDeck connector component is missing.");
            }

            var endpoint = publicEndpoint.GetConfiguration();
            var legacyConfig = FindLegacyConfig();
            if (endpoint.IsConfigured && !string.IsNullOrWhiteSpace(legacyConfig))
            {
                return new ConnectorLaunch
                {
                    Executable = executable,
                    Arguments = $"--config \"{legacyConfig}\" tunnel --no-autoupdate run",
                    PublicUrl = endpoint.PublicUrl,
                    Mode = "legacy-migrated"
                };
            }

            var state = stateStore.LoadOrCreate(endpoint.InstallationId);
            if (!state.IsProvisioned)
            {
                UpdateSnapshot("provisioning", false, false, endpoint.PublicUrl, string.Empty);
                var provisioned = await provisioningClient.ProvisionAsync(
                    endpoint.InstallationId,
                    state.ProvisioningSecret,
                    ProductVersion.Current,
                    cancellationToken);
                state = stateStore.SaveProvisioned(
                    state,
                    provisioned.PublicUrl,
                    provisioned.TunnelId,
                    provisioned.TunnelToken);
            }

            var configured = publicEndpoint.Configure(state.PublicUrl);
            return new ConnectorLaunch
            {
                Executable = executable,
                Arguments = "tunnel --no-autoupdate --loglevel info run",
                TunnelToken = state.TunnelToken,
                PublicUrl = configured.PublicUrl,
                Mode = "managed"
            };
        }

        private async Task RunConnectorAsync(ConnectorLaunch launch, CancellationToken cancellationToken)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = launch.Executable,
                    Arguments = launch.Arguments,
                    WorkingDirectory = Path.GetDirectoryName(launch.Executable),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            if (!string.IsNullOrWhiteSpace(launch.TunnelToken))
            {
                process.StartInfo.Environment["TUNNEL_TOKEN"] = launch.TunnelToken;
            }

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("VibeDeck connector could not be started.");
            }
            connectorJob.Assign(process);

            connectorProcess = process;
            _ = DrainOutputAsync(process.StandardOutput, cancellationToken);
            _ = DrainOutputAsync(process.StandardError, cancellationToken);
            UpdateSnapshot("starting", true, false, launch.PublicUrl, string.Empty, process.Id, launch.Mode);
            audit?.Record(
                "information",
                "managed-connector",
                "start",
                "completed",
                subject: launch.PublicUrl,
                details: new Dictionary<string, string>
                {
                    ["mode"] = launch.Mode,
                    ["processId"] = process.Id.ToString()
                });

            try
            {
                while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    var healthy = await ProbeAsync(launch.PublicUrl, cancellationToken);
                    UpdateSnapshot(healthy ? "online" : "starting", true, healthy, launch.PublicUrl, string.Empty, process.Id, launch.Mode);
                    await Task.Delay(healthy ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(3), cancellationToken);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    var exitCode = process.HasExited ? process.ExitCode : -1;
                    throw new InvalidOperationException($"VibeDeck connector exited with code {exitCode}.");
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                connectorProcess = null;
                process.Dispose();
            }
        }

        private async Task<bool> ProbeAsync(string publicUrl, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var endpoint)) return false;
            try
            {
                using var response = await healthClient.GetAsync(new Uri(endpoint, "health"), cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        internal static async Task DrainOutputAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (await reader.ReadLineAsync(cancellationToken) == null)
                    {
                        break;
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidOperationException || error is OperationCanceledException)
            {
            }
        }

        private void UpdateSnapshot(
            string state,
            bool isRunning,
            bool isHealthy,
            string publicUrl,
            string lastError,
            int processId = 0,
            string mode = "")
        {
            lock (gate)
            {
                snapshot = new ManagedConnectorSnapshot
                {
                    IsManaged = enabled,
                    State = state ?? string.Empty,
                    IsRunning = isRunning,
                    IsHealthy = isHealthy,
                    PublicUrl = publicUrl ?? snapshot.PublicUrl ?? string.Empty,
                    LastError = lastError ?? string.Empty,
                    ProcessId = processId,
                    Mode = mode ?? snapshot.Mode ?? string.Empty,
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
                };
            }
        }

        private void StopConnectorProcess()
        {
            try
            {
                var process = connectorProcess;
                if (process != null && !process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private async Task RetireLegacyScheduledTaskAsync(CancellationToken cancellationToken)
        {
            foreach (var operation in new[] { "/End", "/Delete" })
            {
                try
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
                        Arguments = $"{operation} /TN \"{LegacyTaskName}\" /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    if (process != null)
                    {
                        await process.WaitForExitAsync(cancellationToken);
                    }
                }
                catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception)
                {
                }
            }
        }

        private static string FindCloudflaredExecutable()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "connectors", "cloudflared.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "cloudflared", "cloudflared.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared", "cloudflared.exe")
            };
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
            }
            return string.Empty;
        }

        private static string FindLegacyConfig()
        {
            var candidate = Path.Combine(AppPaths.ConnectDirectory, "cloudflared", "config.yml");
            return File.Exists(candidate) ? candidate : string.Empty;
        }

        private static bool ResolveEnabled(IConfiguration configuration)
        {
            var disabled = Environment.GetEnvironmentVariable("VIBEDECK_DISABLE_MANAGED_CONNECTOR");
            if (string.Equals(disabled, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(disabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var configured = configuration?["ManagedConnector:Enabled"];
            if (bool.TryParse(configured, out var configuredEnabled)) return configuredEnabled;
            return AppPaths.IsInstalledLayout;
        }

        private sealed class ConnectorLaunch
        {
            public string Executable { get; set; }
            public string Arguments { get; set; }
            public string TunnelToken { get; set; }
            public string PublicUrl { get; set; }
            public string Mode { get; set; }
        }
    }

    internal sealed class KillOnCloseJob : IDisposable
    {
        private readonly SafeFileHandle handle;

        public KillOnCloseJob()
        {
            handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (handle == null || handle.IsInvalid)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            var information = new NativeMethods.JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation
                {
                    LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose
                }
            };
            var length = Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
            var pointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(information, pointer, false);
                if (!NativeMethods.SetInformationJobObject(
                    handle,
                    NativeMethods.JobObjectInfoType.ExtendedLimitInformation,
                    pointer,
                    (uint)length))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        public void Assign(Process process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (NativeMethods.AssignProcessToJobObject(handle, process.Handle)) return;

            var error = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
            throw error;
        }

        public void Dispose()
        {
            handle?.Dispose();
        }

        private static class NativeMethods
        {
            public const uint JobObjectLimitKillOnJobClose = 0x00002000;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetInformationJobObject(
                SafeFileHandle job,
                JobObjectInfoType informationClass,
                IntPtr information,
                uint informationLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

            public enum JobObjectInfoType
            {
                ExtendedLimitInformation = 9
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct IoCounters
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JobObjectBasicLimitInformation
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public IntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JobObjectExtendedLimitInformation
            {
                public JobObjectBasicLimitInformation BasicLimitInformation;
                public IoCounters IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }
        }
    }

    public sealed class ManagedConnectorSnapshot
    {
        public bool IsManaged { get; set; }
        public string State { get; set; }
        public bool IsRunning { get; set; }
        public bool IsHealthy { get; set; }
        public string PublicUrl { get; set; }
        public string LastError { get; set; }
        public int ProcessId { get; set; }
        public string Mode { get; set; }
        public string UpdatedAt { get; set; }

        internal ManagedConnectorSnapshot Clone()
        {
            return (ManagedConnectorSnapshot)MemberwiseClone();
        }
    }
}
