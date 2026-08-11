using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using VibeDeck.Host.Windows;
// AppPaths lives in the root Host namespace.

namespace VibeDeck.Host.Display
{
    public sealed class VirtualDisplayInstaller
    {
        private const int UacCancelledError = 1223;
        private readonly VirtualDisplayController displayController;
        private readonly DisplayCatalog displayCatalog;
        private readonly string installerPath;
        private readonly string resultPath;
        private readonly object syncRoot = new object();
        private Process installProcess;

        public VirtualDisplayInstaller(VirtualDisplayController displayController, DisplayCatalog displayCatalog)
        {
            this.displayController = displayController;
            this.displayCatalog = displayCatalog;
            installerPath = Path.Combine(AppContext.BaseDirectory, "Installers", "install-virtual-display.ps1");

            // Product state lives under the canonical VibeDeck data root.
            var stateDirectory = AppPaths.EnsureDirectory(AppPaths.DataRoot);
            resultPath = Path.Combine(stateDirectory, "virtual-display-install-result.json");
        }

        public VirtualDisplayInstallStatus GetStatus()
        {
            lock (syncRoot)
            {
                if (displayCatalog.GetDisplays().Any(display => display.IsVibeDeckDisplay))
                {
                    return VirtualDisplayInstallStatus.Installed();
                }

                if (!File.Exists(installerPath))
                {
                    return VirtualDisplayInstallStatus.Unavailable("這個版本缺少虛擬螢幕安裝元件，請重新下載完整發佈包。");
                }

                if (displayController.GetStatus().DriverInstalled && IsRemoteDesktopSession())
                {
                    return VirtualDisplayInstallStatus.ConsoleRequired();
                }

                if (installProcess != null)
                {
                    try
                    {
                        if (!installProcess.HasExited)
                        {
                            return VirtualDisplayInstallStatus.Installing();
                        }

                        installProcess.Dispose();
                        installProcess = null;
                    }
                    catch (InvalidOperationException)
                    {
                        installProcess = null;
                    }
                }

                var result = ReadResult();
                if (result != null)
                {
                    var message = LocalizeResult(result);
                    if (result.Success && displayController.GetStatus().DriverInstalled)
                    {
                        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(resultPath);
                        return age < TimeSpan.FromSeconds(45)
                            ? new VirtualDisplayInstallStatus
                            {
                                CanInstall = false,
                                State = "finishing",
                                Code = "display.finishing",
                                Message = message
                            }
                            : VirtualDisplayInstallStatus.RepairReady();
                    }

                    return result.Success
                        ? new VirtualDisplayInstallStatus
                        {
                            CanInstall = false,
                            RestartRequired = result.RestartRequired,
                            State = result.RestartRequired ? "restart-required" : "finishing",
                            Code = result.RestartRequired ? "display.restart_required" : "display.finishing",
                            Message = message
                        }
                        : VirtualDisplayInstallStatus.Failed(message);
                }

                return VirtualDisplayInstallStatus.Ready();
            }
        }

        public VirtualDisplayInstallStatus StartInstall()
        {
            lock (syncRoot)
            {
                var current = GetStatus();
                if (current.State == "installed" || current.State == "installing" || current.State == "console-required")
                {
                    return current;
                }

                if (!File.Exists(installerPath))
                {
                    return VirtualDisplayInstallStatus.Unavailable("這個版本缺少虛擬螢幕安裝元件，請重新下載完整發佈包。");
                }

                try
                {
                    if (File.Exists(resultPath))
                    {
                        File.Delete(resultPath);
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(installerPath)} -ResultPath {Quote(resultPath)}",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    installProcess = Process.Start(startInfo);
                    return installProcess == null
                        ? VirtualDisplayInstallStatus.Failed("Windows 沒有啟動安裝程序，請再試一次。")
                        : VirtualDisplayInstallStatus.Installing();
                }
                catch (Win32Exception error) when (error.NativeErrorCode == UacCancelledError)
                {
                    return VirtualDisplayInstallStatus.Failed("你取消了 Windows 管理員確認，虛擬螢幕尚未建立。");
                }
                catch (Exception error)
                {
                    return VirtualDisplayInstallStatus.Failed($"無法啟動安裝：{error.Message}");
                }
            }
        }

        private VirtualDisplayInstallResult ReadResult()
        {
            if (!File.Exists(resultPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<VirtualDisplayInstallResult>(File.ReadAllText(resultPath));
            }
            catch (IOException)
            {
                return null;
            }
            catch (JsonException)
            {
                return new VirtualDisplayInstallResult
                {
                    Success = false,
                    Code = "invalid_result"
                };
            }
        }

        private static string Quote(string value)
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static bool IsRemoteDesktopSession()
        {
            var sessionName = Environment.GetEnvironmentVariable("SESSIONNAME") ?? string.Empty;
            return System.Windows.Forms.SystemInformation.TerminalServerSession
                || sessionName.StartsWith("RDP-", StringComparison.OrdinalIgnoreCase);
        }

        private static string LocalizeResult(VirtualDisplayInstallResult result)
        {
            switch (result.Code)
            {
                case "installed":
                    return "驅動已安裝，正在等待 Windows 建立虛擬螢幕。";
                case "administrator_required":
                    return "Windows 沒有授予管理員權限。";
                case "hash_mismatch":
                    return "下載檔案驗證失敗，已停止安裝。";
                case "package_incomplete":
                    return "下載的驅動套件內容不完整。";
                case "signature_invalid":
                    return "驅動簽章驗證失敗，已停止安裝。";
                case "driver_install_failed":
                    return $"Windows 驅動安裝失敗{(string.IsNullOrWhiteSpace(result.Detail) ? "。" : $"（{result.Detail}）。")}";
                default:
                    return "虛擬螢幕安裝失敗，請確認網路後再試一次。";
            }
        }
    }

    public sealed class VirtualDisplayInstallStatus
    {
        public bool CanInstall { get; set; }
        public bool RestartRequired { get; set; }
        public string State { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }

        public static VirtualDisplayInstallStatus Ready() => new VirtualDisplayInstallStatus
        {
            CanInstall = true,
            State = "ready",
            Code = "display.ready",
            Message = "按下建立後，Windows 會跳出一次管理員確認。"
        };

        public static VirtualDisplayInstallStatus Installing() => new VirtualDisplayInstallStatus
        {
            CanInstall = false,
            State = "installing",
            Code = "display.installing",
            Message = "正在下載並建立虛擬螢幕，通常需要一分鐘。"
        };

        public static VirtualDisplayInstallStatus Installed() => new VirtualDisplayInstallStatus
        {
            CanInstall = false,
            State = "installed",
            Code = "display.installed",
            Message = "虛擬螢幕已建立。"
        };

        public static VirtualDisplayInstallStatus Failed(string message) => new VirtualDisplayInstallStatus
        {
            CanInstall = true,
            State = "failed",
            Code = "display.failed",
            Message = message
        };

        public static VirtualDisplayInstallStatus RepairReady() => new VirtualDisplayInstallStatus
        {
            CanInstall = true,
            State = "repair-ready",
            Code = "display.repair_ready",
            Message = "驅動已裝好，但 Windows 尚未顯示虛擬螢幕。按一次修復會補齊設定並重新啟動驅動。"
        };

        public static VirtualDisplayInstallStatus ConsoleRequired() => new VirtualDisplayInstallStatus
        {
            CanInstall = false,
            State = "console-required",
            Code = "display.console_required",
            Message = "虛擬螢幕已安裝，但 VibeDeck 正在遠端桌面工作階段執行。請回到這台電腦的本機 Windows 桌面重新啟動 VibeDeck。"
        };

        public static VirtualDisplayInstallStatus Unavailable(string message) => new VirtualDisplayInstallStatus
        {
            CanInstall = false,
            State = "unavailable",
            Code = "display.unavailable",
            Message = message
        };
    }

    public sealed class VirtualDisplayInstallResult
    {
        public bool Success { get; set; }
        public bool RestartRequired { get; set; }
        public string Code { get; set; }
        public string Detail { get; set; }
    }
}
