using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static VibeDeck.Host.Quotas.QuotaJsonHelpers;
using static VibeDeck.Host.Quotas.QuotaPaths;
using static VibeDeck.Host.Quotas.QuotaShared;
using static VibeDeck.Host.Quotas.CodexQuotaReader;

namespace VibeDeck.Host.Quotas
{
    public sealed class AgyQuotaService : IQuotaProvider
    {
        string IQuotaProvider.Id => "agy";

        string IQuotaProvider.Label => "AGY";

        async Task<IReadOnlyList<AiQuotaStatus>> IQuotaProvider.ReadAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            var statuses = (await ReadAgyQuotasAsync(forceRefresh, cancellationToken)).ToList();
            QuotaFreshness.MarkSourceHealth(statuses, AgyCacheTtl, DateTimeOffset.UtcNow);
            return statuses;
        }

        private static readonly AgyGoogleApiClient AgyGoogleClient = new AgyGoogleApiClient();
        private static readonly TimeSpan AgyCacheTtl = TimeSpan.FromMinutes(5);
        private readonly object agyOAuthLock = new object();
        private readonly Dictionary<string, AgyOAuthSession> agyOAuthSessions = new Dictionary<string, AgyOAuthSession>(StringComparer.Ordinal);

        public AgyImportResult ImportAgyAccountsFromAntigravity()
        {
            var accountStoreDir = AgyAccountStoreDirectory();
            var before = AgyAccountStore.Read(accountStoreDir).Count;
            var imported = AgyAccountStore.ImportFromAntigravity(accountStoreDir);
            var after = AgyAccountStore.Read(accountStoreDir).Count;

            return new AgyImportResult
            {
                Imported = imported,
                Accounts = after,
                StoreDirectory = accountStoreDir,
                CacheDirectory = AgyQuotaCacheDirectory(),
                Message = after > before
                    ? $"Imported {after - before} AGY account token(s) into VibeDeck."
                    : imported > 0
                        ? "AGY account token(s) were refreshed in the VibeDeck store."
                        : "No Antigravity account token was available to import."
            };
        }

        public AgyOAuthStartResult StartAgyOAuth(string redirectUri, bool openBrowser)
        {
            if (!AgyGoogleClient.TryGetOAuthClient(out var oauthClient, out var configError))
            {
                return new AgyOAuthStartResult
                {
                    Opened = false,
                    Message = configError
                };
            }

            var state = GenerateOAuthToken(32);
            var verifier = GenerateOAuthToken(64);
            string challenge;
            using (var sha256 = SHA256.Create())
            {
                challenge = Base64UrlEncode(sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            }
            var now = DateTimeOffset.UtcNow;

            lock (agyOAuthLock)
            {
                PruneAgyOAuthSessions(now);
                agyOAuthSessions[state] = new AgyOAuthSession
                {
                    State = state,
                    RedirectUri = redirectUri,
                    CodeVerifier = verifier,
                    ExpiresAt = now.AddMinutes(10)
                };
            }

            var authUrl = AgyGoogleClient.BuildAuthorizationUrl(oauthClient, redirectUri, state, challenge);
            var opened = openBrowser && TryOpenBrowser(authUrl);

            return new AgyOAuthStartResult
            {
                State = state,
                AuthUrl = authUrl,
                RedirectUri = redirectUri,
                Opened = opened,
                ExpiresAt = now.AddMinutes(10),
                Message = opened
                    ? "AGY OAuth was opened in the PC browser."
                    : "Open the AGY OAuth URL on this PC to continue."
            };
        }

        public async Task<AgyOAuthCallbackResult> CompleteAgyOAuthAsync(
            string state,
            string code,
            string error,
            string errorDescription,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                if (!string.IsNullOrWhiteSpace(state))
                {
                    lock (agyOAuthLock)
                    {
                        agyOAuthSessions.Remove(state);
                    }
                }

                return AgyOAuthCallbackResult.Fail(errorDescription ?? error);
            }

            if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
            {
                return AgyOAuthCallbackResult.Fail("Google OAuth callback was missing state or code.");
            }

            AgyOAuthSession session;
            lock (agyOAuthLock)
            {
                PruneAgyOAuthSessions(DateTimeOffset.UtcNow);
                if (!agyOAuthSessions.TryGetValue(state, out session))
                {
                    return AgyOAuthCallbackResult.Fail("OAuth session expired. Start AGY sign-in again.");
                }

                agyOAuthSessions.Remove(state);
            }

            try
            {
                var token = await AgyGoogleClient.ExchangeAuthorizationCodeAsync(code, session.RedirectUri, session.CodeVerifier, cancellationToken);
                if (string.IsNullOrWhiteSpace(token.RefreshToken))
                {
                    return AgyOAuthCallbackResult.Fail("Google did not return a refresh token. Start sign-in again and approve offline access.");
                }

                var account = new AgyAccountToken
                {
                    AccountId = token.Subject ?? token.Email ?? Guid.NewGuid().ToString("N"),
                    Email = token.Email,
                    RefreshToken = token.RefreshToken
                };
                AgyAccountStore.Write(AgyAccountStoreDirectory(), account);

                return new AgyOAuthCallbackResult
                {
                    Success = true,
                    AccountId = account.AccountId,
                    Email = account.Email,
                    StoreDirectory = AgyAccountStoreDirectory(),
                    Message = string.IsNullOrWhiteSpace(account.Email)
                        ? "AGY sign-in completed."
                        : $"AGY sign-in completed for {account.Email}."
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is JsonException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                return AgyOAuthCallbackResult.Fail(ex.Message);
            }
        }

        public AgyAccountActionResult OpenAgyCli(bool openWindow = true)
        {
            var agyExe = AgyExecutablePath();
            if (!File.Exists(agyExe))
            {
                return AgyAccountActionResult.Fail("AGY executable was not found.", agyExe);
            }

            if (!openWindow)
            {
                return new AgyAccountActionResult
                {
                    Success = true,
                    Path = agyExe,
                    Message = "AGY CLI is ready to open with its native signed-in account."
                };
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = agyExe,
                    UseShellExecute = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                });

                return new AgyAccountActionResult
                {
                    Success = true,
                    Path = agyExe,
                    Message = "AGY CLI opened with its native signed-in account."
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return AgyAccountActionResult.Fail(ex.Message, agyExe);
            }
        }

        public AgyAccountActionResult DeleteAgyAccount(string accountId, string email)
        {
            var accountStoreDir = AgyAccountStoreDirectory();
            var quotaCacheDir = AgyQuotaCacheDirectory();
            var deleted = 0;

            foreach (var accountFile in AgyAccountStore.FindMatchingFiles(accountStoreDir, accountId, email))
            {
                TryDeleteFile(accountFile, ref deleted);
            }

            foreach (var cacheFile in AgyQuotaCacheStore.FindMatchingFiles(quotaCacheDir, accountId, email))
            {
                TryDeleteFile(cacheFile, ref deleted);
            }

            return new AgyAccountActionResult
            {
                Success = deleted > 0,
                AccountId = accountId,
                Email = email,
                Path = accountStoreDir,
                Deleted = deleted,
                Message = deleted > 0
                    ? $"Deleted {deleted} AGY account/cache file(s)."
                    : "No matching AGY account/cache file was found."
            };
        }

        public AgyAccountActionResult DeleteCodexAccount(string accountId, string email)
        {
            var cacheDirectory = CodexQuotaCacheDirectory();
            var deleted = 0;
            foreach (var cacheFile in FindJsonFiles(cacheDirectory))
            {
                try
                {
                    var status = JsonSerializer.Deserialize<AiQuotaStatus>(File.ReadAllText(cacheFile), CacheJsonOptions);
                    if (status != null && QuotaAccountIdentity.Matches(status.AccountId, status.AccountEmail, accountId, email))
                    {
                        TryDeleteFile(cacheFile, ref deleted);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }

            return new AgyAccountActionResult
            {
                Success = deleted > 0,
                AccountId = accountId,
                Email = email,
                Path = cacheDirectory,
                Deleted = deleted,
                Message = deleted > 0
                    ? $"Deleted {deleted} Codex profile cache file(s)."
                    : "No matching Codex profile cache file was found."
            };
        }

        public async Task<IEnumerable<AiQuotaStatus>> ReadAgyQuotasAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            var agyExe = AgyExecutablePath();
            var accountStoreDir = AgyAccountStoreDirectory();
            var quotaCacheDir = AgyQuotaCacheDirectory();

            if (!File.Exists(agyExe))
            {
                return new[] { Unavailable("agy", "AGY", "AGY executable was not found.", agyExe) };
            }

            var accounts = AgyAccountStore.Read(accountStoreDir).ToList();
            if (!accounts.Any())
            {
                AgyAccountStore.ImportFromAntigravity(accountStoreDir);
                accounts = AgyAccountStore.Read(accountStoreDir).ToList();
            }

            if (!accounts.Any())
            {
                return new[]
                {
                    new AiQuotaStatus
                    {
                        Id = "agy",
                        Label = "AGY",
                        Family = "agy",
                        State = "source-needed",
                        Source = accountStoreDir,
                        Detail = "AGY is installed, but VibeDeck has no AGY account token yet. Import once from Antigravity or complete VibeDeck OAuth."
                    }
                };
            }

            var cacheStatuses = AgyQuotaCacheStore.ReadAuthorized(quotaCacheDir, accounts).ToList();
            if (!forceRefresh && cacheStatuses.Any() && AgyQuotaCacheStore.IsFresh(quotaCacheDir))
            {
                return cacheStatuses;
            }

            var refreshedStatuses = await RefreshAgyQuotasAsync(accounts, quotaCacheDir, cancellationToken);
            if (refreshedStatuses.Any())
            {
                return AgyQuotaCacheStore.MergeStatuses(cacheStatuses, refreshedStatuses);
            }

            if (cacheStatuses.Any())
            {
                return cacheStatuses;
            }

            return accounts.Select(account => new AiQuotaStatus
            {
                Id = string.IsNullOrWhiteSpace(account.AccountId) ? "agy" : $"agy-{account.AccountId}",
                Label = "AGY",
                Family = "agy",
                AccountId = account.AccountId,
                AccountEmail = account.Email,
                AccountTier = account.Tier,
                State = "source-needed",
                Source = account.Source,
                Detail = "VibeDeck has an AGY token, but no quota cache could be refreshed."
            }).ToList();
        }

        private async Task<List<AiQuotaStatus>> RefreshAgyQuotasAsync(
            IReadOnlyList<AgyAccountToken> accounts,
            string quotaCacheDir,
            CancellationToken cancellationToken)
        {
            var refreshedStatuses = new List<AiQuotaStatus>();
            if (accounts == null || !accounts.Any())
            {
                return refreshedStatuses;
            }

            Directory.CreateDirectory(quotaCacheDir);

            foreach (var account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (string.IsNullOrWhiteSpace(account.RefreshToken))
                    {
                        continue;
                    }

                    var accessToken = await AgyGoogleClient.RefreshAccessTokenAsync(account.RefreshToken, cancellationToken);
                    await AgyGoogleClient.WarmLoadCodeAssistAsync(accessToken, cancellationToken);

                    var quotaSummary = await AgyGoogleClient.RetrieveQuotaSummaryAsync(accessToken, cancellationToken);
                    var observedAt = DateTimeOffset.UtcNow;
                    var cacheFile = AgyQuotaCacheStore.ResolveFile(quotaCacheDir, account.Email, account.AccountId);
                    AgyQuotaCacheStore.Write(cacheFile, account.Email, account.AccountId, observedAt, quotaSummary);

                    var buckets = AgyQuotaCacheStore.ReadBuckets(quotaSummary);
                    var detail = string.Join(" · ", new[] { account.Email, account.Tier }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    refreshedStatuses.Add(AgyQuotaCacheStore.BuildStatus("agy-claude", "AGY Claude", account.AccountId, account.Email, account.Tier, cacheFile, detail, observedAt, buckets, "3p-5h", "3p-weekly"));
                    refreshedStatuses.Add(AgyQuotaCacheStore.BuildStatus("agy-gemini", "AGY Gemini", account.AccountId, account.Email, account.Tier, cacheFile, detail, observedAt, buckets, "gemini-5h", "gemini-weekly"));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is HttpRequestException || ex is InvalidOperationException)
                {
                }
            }

            return refreshedStatuses;
        }

        private static string FindExecutable(string fileName)
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                try
                {
                    var path = Path.Combine(directory.Trim(), fileName);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            return null;
        }

        private void PruneAgyOAuthSessions(DateTimeOffset now)
        {
            foreach (var state in agyOAuthSessions
                .Where(pair => pair.Value.ExpiresAt <= now)
                .Select(pair => pair.Key)
                .ToList())
            {
                agyOAuthSessions.Remove(state);
            }
        }

        private static bool TryOpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private static string GenerateOAuthToken(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Base64UrlEncode(bytes);
        }

        private sealed class AgyOAuthSession
        {
            public string State { get; set; }
            public string RedirectUri { get; set; }
            public string CodeVerifier { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
        }

        public sealed class AgyImportResult
        {
            public int Imported { get; set; }
            public int Accounts { get; set; }
            public string StoreDirectory { get; set; }
            public string CacheDirectory { get; set; }
            public string Message { get; set; }
        }

        public sealed class AgyOAuthStartResult
        {
            public string State { get; set; }
            public string AuthUrl { get; set; }
            public string RedirectUri { get; set; }
            public bool Opened { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public string Message { get; set; }
        }

        public sealed class AgyOAuthCallbackResult
        {
            public bool Success { get; set; }
            public string AccountId { get; set; }
            public string Email { get; set; }
            public string StoreDirectory { get; set; }
            public string Message { get; set; }

            public static AgyOAuthCallbackResult Fail(string message)
            {
                return new AgyOAuthCallbackResult
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(message) ? "AGY sign-in failed." : message
                };
            }
        }

        public sealed class AgyAccountActionResult
        {
            public bool Success { get; set; }
            public string AccountId { get; set; }
            public string Email { get; set; }
            public string Path { get; set; }
            public int Deleted { get; set; }
            public string Message { get; set; }

            public static AgyAccountActionResult Fail(string message, string path = null)
            {
                return new AgyAccountActionResult
                {
                    Success = false,
                    Path = path,
                    Message = string.IsNullOrWhiteSpace(message) ? "AGY account action failed." : message
                };
            }
        }

    }
}
