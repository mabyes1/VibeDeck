using System;
using System.IO;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Central filesystem path resolution for VibeDeck quota storage.
    /// </summary>
    internal static class QuotaPaths
    {
        internal static string VibeDeckQuotaRoot()
        {
            return AppPaths.EnsureDirectory(AppPaths.QuotasDirectory);
        }

        internal static string AgyExecutablePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "agy",
                "bin",
                "agy.exe");
        }

        internal static string AgyAccountStoreDirectory()
        {
            return Path.Combine(VibeDeckQuotaRoot(), "agy", "accounts");
        }

        internal static string AgyQuotaCacheDirectory()
        {
            return Path.Combine(VibeDeckQuotaRoot(), "agy", "cache");
        }

        internal static string CodexQuotaCacheDirectory()
        {
            return Path.Combine(VibeDeckQuotaRoot(), "codex", "accounts");
        }

        internal static string CodexProfileDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppPaths.ProductName,
                "codex",
                "profiles");
        }

        internal static string CodexHome()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex");
        }

        internal static string CodexAuthFile()
        {
            return Path.Combine(CodexHome(), "auth.json");
        }

        internal static string ClaudeHome()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude");
        }

        internal static string ClaudeCredentialsFile()
        {
            return Path.Combine(ClaudeHome(), ".credentials.json");
        }

        internal static string[] ClaudeAccountFiles()
        {
            return new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
                Path.Combine(ClaudeHome(), ".claude.json")
            };
        }

        internal static string ClaudeQuotaCacheDirectory()
        {
            return Path.Combine(VibeDeckQuotaRoot(), "claude", "accounts");
        }

        internal static string AgyGoogleOAuthSecretsPath()
        {
            return Path.Combine(
                AppPaths.EnsureDirectory(AppPaths.SecretsDirectory),
                "agy-google-oauth.json");
        }
    }
}
