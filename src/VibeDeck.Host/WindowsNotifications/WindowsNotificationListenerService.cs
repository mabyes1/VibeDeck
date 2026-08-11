using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VibeDeck.Host.CustomSources;

namespace VibeDeck.Host.WindowsNotifications
{
    /// <summary>
    /// Host-side half of the Windows notification bridge. Windows still requires a
    /// packaged identity for userNotificationListener, so the MSIX companion owns the
    /// WinRT listener and posts heartbeats/events back through loopback-only endpoints.
    /// </summary>
    public sealed class WindowsNotificationListenerService : BackgroundService
    {
        public const string BridgeHeaderName = "X-VibeDeck-Notification-Bridge";
        public const string ActivationUri = "vibedeck-notifications://start";

        private readonly object stateGate = new object();
        private readonly CustomSourceService customSources;
        private readonly ILogger<WindowsNotificationListenerService> logger;
        private readonly string settingsPath;
        private readonly string bridgeToken;

        private bool enabled;
        private string accessStatus = "NotChecked";
        private string message = "尚未啟用 Windows 通知。";
        private string lastCapturedAt;
        private int capturedCount;
        private DateTimeOffset? companionSeenAt;

        public WindowsNotificationListenerService(
            CustomSourceService customSources,
            ILogger<WindowsNotificationListenerService> logger)
        {
            this.customSources = customSources;
            this.logger = logger;
            var directory = AppPaths.EnsureDirectory(AppPaths.WindowsNotificationsDirectory);
            settingsPath = Path.Combine(directory, "settings.json");
            bridgeToken = LoadOrCreateBridgeToken(Path.Combine(directory, "bridge-token"));
            enabled = LoadEnabled();
            if (enabled)
            {
                message = "Windows 通知已啟用，等待使用者 Companion 連線。";
            }
        }

        public WindowsNotificationStatusResponse GetStatus()
        {
            CustomSourceRecord source = null;
            try
            {
                source = customSources.EnsureSystemSource(DateTimeOffset.UtcNow);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Windows notification source could not be initialized.");
            }

            lock (stateGate)
            {
                var connected = IsCompanionConnected();
                var statusMessage = message;
                if (enabled && !connected && accessStatus != "Denied")
                {
                    statusMessage = "已啟用；請開啟 VibeDeck Windows 通知 Companion 完成連線。";
                }

                return new WindowsNotificationStatusResponse
                {
                    Supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393),
                    Packaged = false,
                    CompanionRequired = true,
                    CompanionConnected = connected,
                    ActivationUri = ActivationUri,
                    Enabled = enabled,
                    Listening = enabled && connected && string.Equals(accessStatus, "Allowed", StringComparison.OrdinalIgnoreCase),
                    AccessStatus = accessStatus,
                    Message = statusMessage,
                    SourceKey = CustomSourceKeys.WindowsNotifications,
                    CardId = source?.Card?.Id,
                    LastCapturedAt = lastCapturedAt,
                    CapturedCount = capturedCount,
                    CompanionSeenAt = CustomSourceDateTime.ToText(companionSeenAt)
                };
            }
        }

        public Task<WindowsNotificationStatusResponse> EnableAsync()
        {
            lock (stateGate)
            {
                enabled = true;
                message = "正在開啟 Windows 通知 Companion…";
                SaveEnabled(true);
            }
            return Task.FromResult(GetStatus());
        }

        public Task<WindowsNotificationStatusResponse> DisableAsync()
        {
            lock (stateGate)
            {
                enabled = false;
                message = "Windows 通知已停用。";
                SaveEnabled(false);
            }
            return Task.FromResult(GetStatus());
        }

        public bool ValidateBridgeToken(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            var expected = Encoding.UTF8.GetBytes(bridgeToken);
            var actual = Encoding.UTF8.GetBytes(candidate.Trim());
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        public WindowsNotificationStatusResponse AcceptHeartbeat(WindowsNotificationCompanionHeartbeat heartbeat)
        {
            lock (stateGate)
            {
                companionSeenAt = DateTimeOffset.UtcNow;
                accessStatus = string.IsNullOrWhiteSpace(heartbeat?.AccessStatus)
                    ? "NotChecked"
                    : heartbeat.AccessStatus.Trim();
                if (!string.IsNullOrWhiteSpace(heartbeat?.Message)) message = heartbeat.Message.Trim();
            }
            return GetStatus();
        }

        public WindowsNotificationStatusResponse AcceptEvent(WindowsNotificationCompanionEvent notification)
        {
            if (notification == null || string.IsNullOrWhiteSpace(notification.Id) || string.IsNullOrWhiteSpace(notification.Text))
            {
                throw new ArgumentException("通知內容不完整。");
            }

            lock (stateGate)
            {
                companionSeenAt = DateTimeOffset.UtcNow;
                accessStatus = "Allowed";
                message = "Windows 通知監聽中；新通知會即時進入卡片。";
                if (!enabled) return GetStatus();
            }

            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                id = notification.Id.Trim(),
                from = string.IsNullOrWhiteSpace(notification.From) ? "Windows" : notification.From.Trim(),
                text = notification.Text.Trim(),
                timestamp = string.IsNullOrWhiteSpace(notification.Timestamp)
                    ? DateTimeOffset.UtcNow.ToString("O")
                    : notification.Timestamp,
                severity = "info"
            }));
            customSources.IngestSystem(CustomSourceKeys.WindowsNotifications, document.RootElement, DateTimeOffset.UtcNow);
            lock (stateGate)
            {
                lastCapturedAt = CustomSourceDateTime.ToText(DateTimeOffset.UtcNow);
                capturedCount++;
            }
            return GetStatus();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                customSources.EnsureSystemSource(DateTimeOffset.UtcNow);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Windows notification card could not be initialized.");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                lock (stateGate)
                {
                    if (enabled && companionSeenAt.HasValue && !IsCompanionConnected())
                    {
                        message = "Windows 通知 Companion 已中斷，請重新開啟。";
                    }
                }
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private bool IsCompanionConnected() => companionSeenAt.HasValue && DateTimeOffset.UtcNow - companionSeenAt.Value < TimeSpan.FromSeconds(12);

        private bool LoadEnabled()
        {
            try
            {
                if (!File.Exists(settingsPath)) return false;
                using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
                return document.RootElement.TryGetProperty("enabled", out var value) && value.GetBoolean();
            }
            catch (Exception error)
            {
                logger.LogDebug(error, "Windows notification settings could not be loaded.");
                return false;
            }
        }

        private void SaveEnabled(bool value)
        {
            try
            {
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(new { enabled = value }));
            }
            catch (Exception error)
            {
                logger.LogDebug(error, "Windows notification settings could not be saved.");
            }
        }

        private static string LoadOrCreateBridgeToken(string path)
        {
            if (File.Exists(path))
            {
                var saved = File.ReadAllText(path).Trim();
                if (saved.Length >= 32) return saved;
            }
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            File.WriteAllText(path, token);
            return token;
        }
    }
}
