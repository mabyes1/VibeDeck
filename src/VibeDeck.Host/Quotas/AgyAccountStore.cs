using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using static VibeDeck.Host.Quotas.QuotaJsonHelpers;
using static VibeDeck.Host.Quotas.QuotaShared;
using static VibeDeck.Host.Quotas.SecretProtector;

namespace VibeDeck.Host.Quotas
{
    internal static class AgyAccountStore
    {
        internal static IReadOnlyList<AgyAccountToken> Read(string accountStoreDir)
        {
            if (!Directory.Exists(accountStoreDir))
            {
                return Array.Empty<AgyAccountToken>();
            }

            var accounts = new List<AgyAccountToken>();
            foreach (var accountFile in FindJsonFiles(accountStoreDir))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(accountFile));
                    var root = doc.RootElement;
                    var refreshToken = ReadProtectedSecret(root, "refresh_token_protected") ??
                        TryGetString(root, "refresh_token");
                    if (string.IsNullOrWhiteSpace(refreshToken))
                    {
                        continue;
                    }

                    accounts.Add(new AgyAccountToken
                    {
                        AccountId = TryGetString(root, "account_id") ?? Path.GetFileNameWithoutExtension(accountFile),
                        Email = TryGetString(root, "email"),
                        Tier = TryGetString(root, "tier"),
                        RefreshToken = refreshToken,
                        Source = accountFile
                    });
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }

            return accounts
                .GroupBy(account => account.AccountId ?? account.Email ?? Guid.NewGuid().ToString("N"), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        internal static IEnumerable<string> FindMatchingFiles(string accountStoreDir, string accountId, string email)
        {
            var files = new List<string>();
            foreach (var accountFile in FindJsonFiles(accountStoreDir))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(accountFile));
                    if (QuotaAccountIdentity.Matches(
                        TryGetString(doc.RootElement, "account_id"),
                        TryGetString(doc.RootElement, "email"),
                        accountId,
                        email))
                    {
                        files.Add(accountFile);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }

            return files;
        }

        internal static int ImportFromAntigravity(string accountStoreDir, string cockpitAccountsDir = null)
        {
            cockpitAccountsDir ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".antigravity_cockpit",
                "accounts");
            var imported = 0;

            foreach (var accountFile in FindJsonFiles(cockpitAccountsDir))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(accountFile));
                    var root = doc.RootElement;
                    if (!TryGetProperty(root, "token", out var token))
                    {
                        continue;
                    }

                    var refreshToken = TryGetString(token, "refresh_token");
                    if (string.IsNullOrWhiteSpace(refreshToken))
                    {
                        continue;
                    }

                    var accountId = TryGetString(root, "id") ?? Path.GetFileNameWithoutExtension(accountFile);
                    var email = TryGetString(root, "email") ?? TryGetString(token, "email");
                    var tier = TryGetProperty(root, "quota", out var quota)
                        ? TryGetString(quota, "subscription_tier")
                        : null;

                    Write(accountStoreDir, new AgyAccountToken
                    {
                        AccountId = accountId,
                        Email = email,
                        Tier = tier,
                        RefreshToken = refreshToken
                    });
                    imported++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }

            return imported;
        }

        internal static void Write(string accountStoreDir, AgyAccountToken account)
        {
            Directory.CreateDirectory(accountStoreDir);
            var accountId = !string.IsNullOrWhiteSpace(account.AccountId) ? account.AccountId : Guid.NewGuid().ToString("N");
            var path = ResolveTokenFile(accountStoreDir, accountId, account.Email);
            var envelope = new Dictionary<string, object>
            {
                ["provider"] = "agy",
                ["account_id"] = accountId,
                ["email"] = account.Email,
                ["tier"] = account.Tier,
                ["refresh_token_protected"] = ProtectSecret(account.RefreshToken),
                ["protection"] = "windows-dpapi-current-user",
                ["imported_at"] = DateTimeOffset.UtcNow,
                ["source"] = "vibedeck"
            };
            File.WriteAllText(path, JsonSerializer.Serialize(envelope, CacheJsonOptions), Encoding.UTF8);
        }

        private static string ResolveTokenFile(string accountStoreDir, string accountId, string email)
        {
            foreach (var accountFile in FindJsonFiles(accountStoreDir))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(accountFile));
                    var root = doc.RootElement;
                    var existingAccountId = TryGetString(root, "account_id");
                    var existingEmail = TryGetString(root, "email");

                    if (!string.IsNullOrWhiteSpace(accountId) &&
                        string.Equals(existingAccountId, accountId, StringComparison.OrdinalIgnoreCase))
                    {
                        return accountFile;
                    }

                    if (!string.IsNullOrWhiteSpace(email) &&
                        string.Equals(existingEmail, email, StringComparison.OrdinalIgnoreCase))
                    {
                        return accountFile;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }

            return Path.Combine(accountStoreDir, $"{SafeFileName(accountId)}.json");
        }
    }

    internal sealed class AgyAccountToken
    {
        public string AccountId { get; set; }
        public string Email { get; set; }
        public string Tier { get; set; }
        public string RefreshToken { get; set; }
        public string Source { get; set; }
    }
}
