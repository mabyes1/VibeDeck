using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace VibeDeck.Host.WindowsNotifications
{
    public static class WindowsNotificationCompanion
    {
        private static readonly Uri HostBaseUri = new Uri("http://127.0.0.1:5000/");

        public static bool ShouldRun(string[] args)
        {
            if (args?.Any(arg => arg.StartsWith("vibedeck-notifications:", StringComparison.OrdinalIgnoreCase)) == true)
                return true;
            try { return Package.Current?.Id != null; }
            catch { return false; }
        }

        public static async Task RunAsync()
        {
            using var singleInstance = new Mutex(true, "VibeDeck.WindowsNotifications.Companion", out var ownsMutex);
            if (!ownsMutex) return;

            var token = await WaitForBridgeTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show(
                    "找不到 VibeDeck Host 的通知橋接資料。請先確認 VibeDeck Host 服務正在執行。",
                    "VibeDeck Windows 通知",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var client = new HttpClient { BaseAddress = HostBaseUri, Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.TryAddWithoutValidation(WindowsNotificationListenerService.BridgeHeaderName, token);

            UserNotificationListener listener;
            try
            {
                listener = UserNotificationListener.Current;
            }
            catch (Exception error)
            {
                await TryHeartbeatAsync(client, "Unavailable", error.Message).ConfigureAwait(false);
                return;
            }

            UserNotificationListenerAccessStatus access;
            try
            {
                access = listener.GetAccessStatus();
                if (access != UserNotificationListenerAccessStatus.Allowed)
                    access = await StaUiInvoker.InvokeAsync(() => listener.RequestAccessAsync().AsTask()).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                await TryHeartbeatAsync(client, "Unavailable", error.Message).ConfigureAwait(false);
                return;
            }

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                var deniedMessage = access == UserNotificationListenerAccessStatus.Denied
                    ? "Windows 已拒絕通知存取，請到 Windows 設定允許 VibeDeck Windows 通知。"
                    : "尚未取得 Windows 通知存取權限。";
                await TryHeartbeatAsync(client, access.ToString(), deniedMessage).ConfigureAwait(false);
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var initial = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var notification in initial)
            {
                var key = GetNotificationKey(notification);
                if (key == null || !seen.Add(key)) continue;
            }

            var syncRequested = 1;
            void Changed(UserNotificationListener sender, UserNotificationChangedEventArgs args) => Interlocked.Exchange(ref syncRequested, 1);
            listener.NotificationChanged += Changed;
            try
            {
                while (true)
                {
                    if (Interlocked.Exchange(ref syncRequested, 0) == 1)
                    {
                        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                        foreach (var notification in notifications)
                        {
                            var key = GetNotificationKey(notification);
                            if (key == null || !seen.Add(key)) continue;
                            if (seen.Count > 5000) seen.Remove(seen.First());
                            var payload = ConvertNotification(notification, key);
                            if (payload != null) await PostJsonAsync(client, "api/windows-notifications/companion/events", payload).ConfigureAwait(false);
                        }
                    }

                    var heartbeat = await TryHeartbeatAsync(
                        client,
                        "Allowed",
                        "Windows 通知監聽中；新通知會即時進入卡片。").ConfigureAwait(false);
                    // Host is intentionally stopped during an in-place update. Keep the
                    // packaged listener alive and reconnect when the desktop Host returns.
                    if (heartbeat != null && !heartbeat.Enabled) break;

                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    Interlocked.Exchange(ref syncRequested, 1);
                }
            }
            finally
            {
                listener.NotificationChanged -= Changed;
            }
        }

        private static WindowsNotificationCompanionEvent ConvertNotification(UserNotification notification, string key)
        {
            var appName = notification.AppInfo?.DisplayInfo?.DisplayName;
            if (string.IsNullOrWhiteSpace(appName)) appName = "Windows";

            var title = string.Empty;
            var body = string.Empty;
            var binding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                var textElements = binding.GetTextElements();
                title = textElements.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
                body = string.Join("\n", textElements.Skip(1)
                    .Select(element => element.Text?.Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
            }

            var text = string.Join("\n", new[] { title, body }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (string.IsNullOrWhiteSpace(text)) return null;
            return new WindowsNotificationCompanionEvent
            {
                Id = key,
                From = appName,
                Text = text,
                Timestamp = notification.CreationTime.ToUniversalTime().ToString("O")
            };
        }

        private static string GetNotificationKey(UserNotification notification)
        {
            try
            {
                var appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "Windows";
                var binding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                var text = binding == null
                    ? string.Empty
                    : string.Join("\n", binding.GetTextElements().Select(element => element.Text?.Trim() ?? string.Empty));
                // UserNotification.Id is not stable across every listener snapshot on
                // all Windows builds. Content + source + creation time is stable enough
                // to seed the current tray without replaying it after each poll/restart.
                var raw = $"{appName}|{notification.CreationTime.ToUniversalTime():O}|{text}";
                return "win-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            }
            catch { return null; }
        }

        private static async Task<string> WaitForBridgeTokenAsync()
        {
            var candidates = new[]
            {
                Path.Combine(AppPaths.WindowsNotificationsDirectory, "bridge-token"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VibeDeck", "windows-notifications", "bridge-token")
            };
            for (var attempt = 0; attempt < 20; attempt++)
            {
                foreach (var path in candidates)
                {
                    try
                    {
                        if (!File.Exists(path)) continue;
                        var token = (await File.ReadAllTextAsync(path).ConfigureAwait(false)).Trim();
                        if (token.Length >= 32) return token;
                    }
                    catch { }
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            return null;
        }

        private static async Task<CompanionHeartbeatResponse> TryHeartbeatAsync(HttpClient client, string accessStatus, string message)
        {
            try
            {
                var response = await PostJsonAsync(client, "api/windows-notifications/companion/heartbeat", new
                {
                    accessStatus,
                    message
                }).ConfigureAwait(false);
                return JsonSerializer.Deserialize<CompanionHeartbeatResponse>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch { return null; }
        }

        private static async Task<string> PostJsonAsync(HttpClient client, string path, object value)
        {
            using var content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(path, content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private sealed class CompanionHeartbeatResponse
        {
            public bool Enabled { get; set; }
        }

        private static class StaUiInvoker
        {
            public static Task<T> InvokeAsync<T>(Func<Task<T>> action)
            {
                var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                var ready = new ManualResetEventSlim(false);
                SynchronizationContext context = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        context = new WindowsFormsSynchronizationContext();
                        SynchronizationContext.SetSynchronizationContext(context);
                        ready.Set();
                        Application.Run(new ApplicationContext());
                    }
                    catch (Exception error)
                    {
                        completion.TrySetException(error);
                        ready.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "VibeDeck.WindowsNotificationPermission"
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait();
                if (completion.Task.IsCompleted) return completion.Task;

                context.Post(async _ =>
                {
                    try { completion.TrySetResult(await action().ConfigureAwait(true)); }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { Application.ExitThread(); }
                }, null);
                return completion.Task;
            }
        }
    }
}
