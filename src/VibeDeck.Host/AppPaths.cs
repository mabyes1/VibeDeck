using System;
using System.IO;

namespace VibeDeck.Host
{
    /// <summary>
    /// Resolves product data directories for source development and Setup installs.
    /// </summary>
    public static class AppPaths
    {
        public const string ProductName = "VibeDeck";
        public const string InstallMarkerFileName = "product-install.json";
        public const string WebUiUrl = "http://127.0.0.1:5000";
        private static readonly Lazy<string> DataRootLazy = new Lazy<string>(ResolveDataRoot);
        private static readonly Lazy<bool> IsInstalledLazy = new Lazy<bool>(DetectInstalledLayout);

        public static string DataRoot => DataRootLazy.Value;

        public static bool IsInstalledLayout => IsInstalledLazy.Value;

        public static string CertsDirectory => Path.Combine(DataRoot, "certs");

        public static string DevicesDirectory => Path.Combine(DataRoot, "devices");

        public static string ConnectDirectory => Path.Combine(DataRoot, "connect");

        public static string CustomSourcesDirectory => Path.Combine(DataRoot, "custom-sources");

        public static string DecksDirectory => Path.Combine(DataRoot, "Decks");

        public static string WindowsNotificationsDirectory => Path.Combine(DataRoot, "windows-notifications");

        public static string DashboardDirectory => Path.Combine(DataRoot, "dashboard");

        public static string QuotasDirectory => Path.Combine(DataRoot, "quotas");

        public static string SecretsDirectory => Path.Combine(DataRoot, "secrets");

        public static string LogsDirectory => Path.Combine(DataRoot, "logs");

        public static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        private static string ResolveDataRoot()
        {
            var env = Environment.GetEnvironmentVariable("VIBEDECK_DATA");
            if (!string.IsNullOrWhiteSpace(env))
            {
                return Path.GetFullPath(env.Trim());
            }

            // Setup installs keep product state outside the replaceable app directory.
            if (IsInstalledLayout)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    ProductName);
            }

            // Source / portable runs use the same canonical product name.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductName);
        }

        private static bool DetectInstalledLayout()
        {
            try
            {
                var baseDirectory = AppContext.BaseDirectory;
                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    return false;
                }

                if (File.Exists(Path.Combine(baseDirectory, InstallMarkerFileName)))
                {
                    return true;
                }

                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if ((!string.IsNullOrWhiteSpace(programFiles) &&
                     baseDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(programFilesX86) &&
                     baseDirectory.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch
            {
                // Fall through to non-installed layout.
            }

            return false;
        }

    }
}
