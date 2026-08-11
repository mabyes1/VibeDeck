using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using VibeDeck.Host.Security;
using VibeDeck.Host.WindowsNotifications;

namespace VibeDeck.Host
{
    public class Program
    {
        private const string OpenUiArgument = "--open";
        private const string RegisterAutostartArgument = "--register-autostart";
        private const string UnregisterAutostartArgument = "--unregister-autostart";
        private const string HealthUrl = "http://127.0.0.1:5000/health";
        private static readonly HttpClient HealthClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        [STAThread]
        public static void Main(string[] args)
        {
            if (HandleAutostartCommand(args))
            {
                return;
            }

            var openUi = args?.Any(value => string.Equals(value, OpenUiArgument, StringComparison.OrdinalIgnoreCase)) == true;
            if (WindowsNotificationCompanion.ShouldRun(args))
            {
                RunNotificationCompanionOnStaThread();
                return;
            }

            if (!Environment.UserInteractive || Process.GetCurrentProcess().SessionId == 0)
            {
                if (openUi)
                {
                    ShowLaunchError(GetLocalizedText(
                        "VibeDeck 必須在已登入的 Windows 桌面工作階段執行。請登入 Windows 後再開啟。",
                        "VibeDeck must run in the signed-in Windows desktop session. Sign in to Windows, then open it again.",
                        "VibeDeck はサインインしている Windows デスクトップ セッションで実行する必要があります。Windows にサインインしてから、もう一度開いてください。"));
                }
                Environment.ExitCode = 2;
                return;
            }

            EnsureInstalledAutostart();
            AppPaths.EnsureDirectory(AppPaths.DataRoot);
            AppPaths.EnsureDirectory(AppPaths.LogsDirectory);

            using var singleInstance = new Mutex(true, "VibeDeck.Host.SingleInstance", out var ownsMutex);
            if (!ownsMutex)
            {
                if (openUi)
                {
                    OpenBrowserWhenReady();
                }
                return;
            }

            try
            {
                var host = CreateHostBuilder(args).Build();
                if (openUi)
                {
                    if (host.Services.GetService(typeof(IHostApplicationLifetime)) is IHostApplicationLifetime lifetime)
                    {
                        lifetime.ApplicationStarted.Register(() =>
                            ThreadPool.QueueUserWorkItem(_ => OpenBrowserWhenReady()));
                    }
                }
                host.Run();
            }
            catch (Exception error)
            {
                Trace.TraceError($"VibeDeck Host startup failed: {error}");
                if (openUi)
                {
                    ShowLaunchError(GetLocalizedText(
                        "VibeDeck 無法啟動。請確認沒有其他程式佔用連接埠 5000，然後再試一次。",
                        "VibeDeck could not start. Check that another app is not using port 5000, then try again.",
                        "VibeDeck を起動できませんでした。別のアプリがポート 5000 を使用していないことを確認して、もう一度お試しください。"));
                }
                Environment.ExitCode = 1;
            }
        }

        private static void OpenBrowserWhenReady()
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using var response = HealthClient.GetAsync(HealthUrl).GetAwaiter().GetResult();
                    if (response.IsSuccessStatusCode)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = AppPaths.WebUiUrl,
                            UseShellExecute = true
                        });
                        return;
                    }
                }
                catch
                {
                }

                Thread.Sleep(500);
            }

            ShowLaunchError(GetLocalizedText(
                "VibeDeck Host 尚未就緒。請確認沒有其他程式佔用連接埠 5000，然後再試一次。",
                "VibeDeck Host did not become ready. Check that another app is not using port 5000, then try again.",
                "VibeDeck Host の準備が完了しませんでした。別のアプリがポート 5000 を使用していないことを確認して、もう一度お試しください。"));
        }

        private static bool HandleAutostartCommand(string[] args)
        {
            var register = args?.Any(value => string.Equals(value, RegisterAutostartArgument, StringComparison.OrdinalIgnoreCase)) == true;
            var unregister = args?.Any(value => string.Equals(value, UnregisterAutostartArgument, StringComparison.OrdinalIgnoreCase)) == true;
            if (!register && !unregister)
            {
                return false;
            }

            try
            {
                UpdateAutostartRegistration(register);
            }
            catch
            {
                Environment.ExitCode = 1;
            }

            return true;
        }

        private static void EnsureInstalledAutostart()
        {
            if (!AppPaths.IsInstalledLayout)
            {
                return;
            }

            try
            {
                UpdateAutostartRegistration(true);
            }
            catch (Exception error)
            {
                Trace.TraceWarning($"VibeDeck auto-start registration could not be repaired: {error.Message}");
            }
        }

        private static void UpdateAutostartRegistration(bool register)
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                ?? throw new InvalidOperationException("The current user's Run registry key is unavailable.");
            if (!register)
            {
                runKey.DeleteValue("VibeDeckHost", throwOnMissingValue: false);
                return;
            }

            var executable = Path.Combine(AppContext.BaseDirectory, "VibeDeck.Host.exe");
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("VibeDeck Host executable was not found.", executable);
            }

            runKey.SetValue("VibeDeckHost", $"\"{executable}\"", RegistryValueKind.String);
        }

        private static void ShowLaunchError(string message)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    message,
                    "VibeDeck",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        private static string GetLocalizedText(string traditionalChinese, string english, string japanese)
        {
            var language = CultureInfo.CurrentUICulture?.Name ?? "";
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return japanese;
            }

            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return traditionalChinese;
            }

            return english;
        }

        private static void RunNotificationCompanionOnStaThread()
        {
            Exception companionError = null;
            var companionThread = new Thread(() =>
            {
                try
                {
                    WindowsNotificationCompanion.RunAsync().GetAwaiter().GetResult();
                }
                catch (Exception error)
                {
                    companionError = error;
                }
            });
            companionThread.SetApartmentState(ApartmentState.STA);
            companionThread.Start();
            companionThread.Join();

            if (companionError != null)
            {
                ExceptionDispatchInfo.Capture(companionError).Throw();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            global::Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .UseContentRoot(ResolveContentRoot())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.ListenAnyIP(5000);
                        var httpsStatus = LocalHttpsCertificate.EnsureCurrent();
                        if (!httpsStatus.Success)
                        {
                            Console.Error.WriteLine($"VibeDeck HTTPS certificate check failed: {httpsStatus.Error}");
                        }
                        else if (httpsStatus.RootCreated || httpsStatus.HostCreated)
                        {
                            var action = httpsStatus.RootCreated
                                ? "created"
                                : "updated";
                            Console.WriteLine($"VibeDeck HTTPS certificate {action} for {string.Join(", ", httpsStatus.IpAddresses)}.");
                        }

                        if (LocalHttpsCertificate.IsConfigured)
                        {
                            if (LocalHttpsCertificate.TryLoadServerCertificate(out var certificate, out var certificateError))
                            {
                                options.ListenAnyIP(LocalHttpsCertificate.HttpsPort, listenOptions =>
                                {
                                    listenOptions.UseHttps(certificate);
                                });
                            }
                            else
                            {
                                Console.Error.WriteLine($"VibeDeck HTTPS disabled: {certificateError}");
                            }
                        }
                    });
                    webBuilder.UseStartup<Startup>();
                });

        private static string ResolveContentRoot()
        {
            var current = Directory.GetCurrentDirectory();
            if (Directory.Exists(Path.Combine(current, "wwwroot")))
            {
                return current;
            }

            var projectFromRepositoryRoot = Path.Combine(current, "src", "VibeDeck.Host");
            if (Directory.Exists(Path.Combine(projectFromRepositoryRoot, "wwwroot")))
            {
                return projectFromRepositoryRoot;
            }

            var baseDirectory = AppContext.BaseDirectory;
            if (Directory.Exists(Path.Combine(baseDirectory, "wwwroot")))
            {
                return baseDirectory;
            }

            return current;
        }
    }
}
