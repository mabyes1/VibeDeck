using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static VibeDeck.Host.Quotas.QuotaJsonHelpers;
using static VibeDeck.Host.Quotas.QuotaPaths;
using static VibeDeck.Host.Quotas.QuotaShared;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Reads the same account usage endpoint that backs Claude Code /usage.
    /// The CLI remains the only token writer: VibeDeck never refreshes credentials
    /// and never serializes a token into its cache or client-facing status fields.
    /// </summary>
    internal sealed class ClaudeQuotaProvider : IQuotaProvider
    {
        private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
        private const string OAuthBeta = "oauth-2025-04-20";
        private const string DefaultUserAgent = "claude-code/2.0.0";
        private const string UserAgentEnvVar = "VIBEDECK_CLAUDE_USER_AGENT";
        private const string SuppressedAccountFileName = "_suppressed-account.json";

        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
        private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);

        public string Id => "claude-code";
        public string Label => "Claude";

        public async Task<IReadOnlyList<AiQuotaStatus>> ReadAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            var statuses = await ReadCoreAsync(forceRefresh, cancellationToken);
            QuotaFreshness.MarkSourceHealth(statuses, CacheTtl, DateTimeOffset.UtcNow);
            return statuses;
        }

        private async Task<IReadOnlyList<AiQuotaStatus>> ReadCoreAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(ClaudeHome()))
            {
                return Array.Empty<AiQuotaStatus>();
            }

            var identity = ReadIdentity();
            if (IsSuppressed(identity))
            {
                return Array.Empty<AiQuotaStatus>();
            }
            var cacheFile = Path.Combine(ClaudeQuotaCacheDirectory(), $"{SafeFileName(identity.AccountKey)}.json");
            var cached = ReadCachedStatus(cacheFile, identity);
            var credentials = ReadCredentials();

            if (credentials == null)
            {
                return new[]
                {
                    cached ?? SourceNeeded(identity, "Claude Code is installed but not signed in, so VibeDeck has no usage source yet.")
                };
            }

            if (credentials.ExpiresAt.HasValue && credentials.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            {
                return new[]
                {
                    Stale(cached, identity, "The Claude Code sign-in has expired. Run Claude Code once to renew it.")
                };
            }

            if (!forceRefresh && cached != null && IsFresh(cacheFile))
            {
                return new[] { cached };
            }

            if (!await refreshGate.WaitAsync(forceRefresh ? 10000 : 0, cancellationToken))
            {
                return new[] { cached ?? SourceNeeded(identity, "A usage refresh is already running.") };
            }

            try
            {
                var usage = await FetchUsageAsync(credentials.AccessToken, cancellationToken);
                var observedAt = DateTimeOffset.UtcNow;
                WriteCache(cacheFile, identity, observedAt, usage);
                return new[] { BuildStatus(identity, credentials.Tier, observedAt, usage, cacheFile) };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is IOException ||
                                       ex is UnauthorizedAccessException || ex is InvalidOperationException ||
                                       ex is OperationCanceledException)
            {
                return new[] { Stale(cached, identity, DescribeFetchFailure(ex)) };
            }
            finally
            {
                refreshGate.Release();
            }
        }

        public AgyQuotaService.AgyAccountActionResult DeleteAccount(string accountId, string email)
        {
            var identity = ReadIdentity();
            if (!QuotaAccountIdentity.Matches(identity.AccountId, identity.Email, accountId, email))
            {
                return new AgyQuotaService.AgyAccountActionResult
                {
                    Success = false,
                    AccountId = accountId,
                    Email = email,
                    Path = ClaudeQuotaCacheDirectory(),
                    Message = "No matching Claude account was found."
                };
            }

            var cacheDirectory = ClaudeQuotaCacheDirectory();
            var cacheFile = Path.Combine(cacheDirectory, $"{SafeFileName(identity.AccountKey)}.json");
            var deleted = 0;
            try
            {
                Directory.CreateDirectory(cacheDirectory);
                if (File.Exists(cacheFile))
                {
                    File.Delete(cacheFile);
                    deleted++;
                }

                var marker = new ClaudeSuppressedAccount
                {
                    AccountId = identity.AccountId,
                    Email = identity.Email,
                    CredentialsVersion = ReadCredentialsVersion()
                };
                File.WriteAllText(
                    Path.Combine(cacheDirectory, SuppressedAccountFileName),
                    JsonSerializer.Serialize(marker, CacheJsonOptions));

                return new AgyQuotaService.AgyAccountActionResult
                {
                    Success = true,
                    AccountId = identity.AccountId,
                    Email = identity.Email,
                    Path = cacheDirectory,
                    Deleted = deleted,
                    Message = "Claude account removed from VibeDeck. Claude Code credentials were left untouched."
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return new AgyQuotaService.AgyAccountActionResult
                {
                    Success = false,
                    AccountId = accountId,
                    Email = email,
                    Path = cacheDirectory,
                    Deleted = deleted,
                    Message = $"Unable to remove the Claude account from VibeDeck: {ex.Message}"
                };
            }
        }

        private static bool IsSuppressed(ClaudeIdentity identity)
        {
            var markerFile = Path.Combine(ClaudeQuotaCacheDirectory(), SuppressedAccountFileName);
            try
            {
                if (!File.Exists(markerFile)) return false;
                var marker = JsonSerializer.Deserialize<ClaudeSuppressedAccount>(
                    File.ReadAllText(markerFile),
                    CacheJsonOptions);
                if (marker == null ||
                    !QuotaAccountIdentity.Matches(marker.AccountId, marker.Email, identity.AccountId, identity.Email) ||
                    marker.CredentialsVersion != ReadCredentialsVersion())
                {
                    File.Delete(markerFile);
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return false;
            }
        }

        private static long ReadCredentialsVersion()
        {
            try
            {
                var path = ClaudeCredentialsFile();
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return 0L;
            }
        }

        private static string DescribeFetchFailure(Exception ex)
        {
            return ex is OperationCanceledException
                ? "The Claude usage endpoint did not answer in time."
                : $"The Claude usage endpoint could not be read: {ex.Message}";
        }

        private async Task<JsonElement> FetchUsageAsync(string accessToken, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBeta);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }

        private static string UserAgent()
        {
            var configured = Environment.GetEnvironmentVariable(UserAgentEnvVar);
            return string.IsNullOrWhiteSpace(configured) ? DefaultUserAgent : configured;
        }

        private static bool IsFresh(string cacheFile)
        {
            try
            {
                return File.Exists(cacheFile) && File.GetLastWriteTimeUtc(cacheFile) >= DateTime.UtcNow - CacheTtl;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static AiQuotaStatus BuildStatus(
            ClaudeIdentity identity,
            string tier,
            DateTimeOffset observedAt,
            JsonElement usage,
            string source)
        {
            var primary = ReadNamedWindow(usage, "5h", 300, "five_hour", "fiveHour")
                ?? ReadLimitsWindow(usage, "5h", 300, "session");
            var secondary = ReadNamedWindow(usage, "Weekly", 10080, "seven_day", "sevenDay")
                ?? ReadLimitsWindow(usage, "Weekly", 10080, "weekly");

            return new AiQuotaStatus
            {
                Id = $"claude-code-{identity.AccountKey}",
                Label = "Claude Code",
                Family = "claude-code",
                AccountId = identity.AccountId,
                AccountEmail = identity.Email,
                AccountTier = FirstNonEmpty(tier, identity.Tier),
                IsActive = true,
                State = primary != null || secondary != null ? "ok" : "source-needed",
                Source = source,
                Detail = identity.Email,
                ObservedAt = observedAt,
                Primary = primary,
                Secondary = secondary
            };
        }

        internal static QuotaWindow ReadNamedWindow(JsonElement usage, string label, int windowMinutes, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetProperty(usage, name, out var bucket) || bucket.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var utilization = TryGetDouble(bucket, "utilization") ?? TryGetDouble(bucket, "utilization_percent");
                var resetsAt = ReadResetTime(bucket);
                if (!utilization.HasValue && !resetsAt.HasValue)
                {
                    continue;
                }

                return new QuotaWindow
                {
                    Label = label,
                    UsedPercent = utilization,
                    WindowMinutes = windowMinutes,
                    ResetsAt = resetsAt
                };
            }

            return null;
        }

        internal static QuotaWindow ReadLimitsWindow(JsonElement usage, string label, int windowMinutes, string group)
        {
            if (!TryGetProperty(usage, "limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            QuotaWindow tightest = null;
            foreach (var entry in limits.EnumerateArray())
            {
                if (!string.Equals(TryGetString(entry, "group"), group, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var percent = TryGetDouble(entry, "percent");
                if (!percent.HasValue || tightest?.UsedPercent >= percent.Value)
                {
                    continue;
                }

                tightest = new QuotaWindow
                {
                    Label = label,
                    UsedPercent = percent,
                    WindowMinutes = windowMinutes,
                    ResetsAt = ReadResetTime(entry)
                };
            }

            return tightest;
        }

        private static DateTimeOffset? ReadResetTime(JsonElement element)
        {
            return TryGetDateTimeOffset(element, "resets_at")
                ?? TryGetDateTimeOffset(element, "resetsAt")
                ?? TryGetUnixTime(element, "resets_at");
        }

        private static AiQuotaStatus SourceNeeded(ClaudeIdentity identity, string detail)
        {
            return new AiQuotaStatus
            {
                Id = $"claude-code-{identity.AccountKey}",
                Label = "Claude Code",
                Family = "claude-code",
                AccountId = identity.AccountId,
                AccountEmail = identity.Email,
                AccountTier = identity.Tier,
                State = "source-needed",
                Source = ClaudeCredentialsFile(),
                Detail = detail
            };
        }

        private static AiQuotaStatus Stale(AiQuotaStatus cached, ClaudeIdentity identity, string detail)
        {
            if (cached == null)
            {
                var unavailable = Unavailable($"claude-code-{identity.AccountKey}", "Claude Code", detail, ClaudeCredentialsFile());
                unavailable.AccountEmail = identity.Email;
                unavailable.AccountId = identity.AccountId;
                return unavailable;
            }

            cached.Detail = detail;
            cached.Freshness = QuotaFreshness.Stale;
            return cached;
        }

        private static AiQuotaStatus ReadCachedStatus(string cacheFile, ClaudeIdentity identity)
        {
            try
            {
                if (!File.Exists(cacheFile)) return null;
                using var document = JsonDocument.Parse(File.ReadAllText(cacheFile));
                var root = document.RootElement;
                if (!TryGetProperty(root, "payload", out var usage)) return null;
                var observedAt = TryGetUnixTimeMilliseconds(root, "updatedAt")
                    ?? new DateTimeOffset(File.GetLastWriteTimeUtc(cacheFile), TimeSpan.Zero);
                return BuildStatus(identity, TryGetString(root, "tier"), observedAt, usage, cacheFile);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return null;
            }
        }

        private static void WriteCache(string cacheFile, ClaudeIdentity identity, DateTimeOffset observedAt, JsonElement usage)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                var envelope = new Dictionary<string, object>
                {
                    ["email"] = identity.Email,
                    ["accountId"] = identity.AccountId,
                    ["tier"] = identity.Tier,
                    ["updatedAt"] = observedAt.ToUnixTimeMilliseconds(),
                    ["payload"] = JsonSerializer.Deserialize<JsonElement>(usage.GetRawText())
                };
                File.WriteAllText(cacheFile, JsonSerializer.Serialize(envelope, CacheJsonOptions));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
            }
        }

        private static ClaudeIdentity ReadIdentity()
        {
            foreach (var accountFile in ClaudeAccountFiles())
            {
                try
                {
                    if (!File.Exists(accountFile)) continue;
                    using var document = JsonDocument.Parse(File.ReadAllText(accountFile));
                    if (!TryGetProperty(document.RootElement, "oauthAccount", out var account)) continue;
                    var email = TryGetString(account, "emailAddress") ?? TryGetString(account, "email");
                    var accountId = TryGetString(account, "accountUuid") ?? TryGetString(account, "accountId");
                    if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(accountId)) continue;
                    return new ClaudeIdentity
                    {
                        Email = email,
                        AccountId = accountId,
                        Tier = NormalizeTier(TryGetString(account, "organizationType"))
                    };
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }
            return new ClaudeIdentity();
        }

        private static string NormalizeTier(string organizationType)
        {
            if (string.IsNullOrWhiteSpace(organizationType)) return null;
            var value = organizationType.Replace("claude_", string.Empty).Replace("_", " ").Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value.ToUpperInvariant();
        }

        private static ClaudeCredentials ReadCredentials()
        {
            var path = ClaudeCredentialsFile();
            try
            {
                if (!File.Exists(path)) return null;
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (!TryGetProperty(root, "claudeAiOauth", out var oauth)) oauth = root;
                var accessToken = TryGetString(oauth, "accessToken") ?? TryGetString(oauth, "access_token");
                if (string.IsNullOrWhiteSpace(accessToken)) return null;
                return new ClaudeCredentials
                {
                    AccessToken = accessToken,
                    ExpiresAt = ReadEpoch(oauth, "expiresAt") ?? ReadEpoch(oauth, "expires_at"),
                    Tier = NormalizeTier(TryGetString(oauth, "subscriptionType") ?? TryGetString(oauth, "subscription_type"))
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return null;
            }
        }

        private static DateTimeOffset? ReadEpoch(JsonElement element, string name)
        {
            var value = TryGetDouble(element, name);
            if (!value.HasValue || value.Value <= 0d) return null;
            return value.Value > 1e11d
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)value.Value)
                : DateTimeOffset.FromUnixTimeSeconds((long)value.Value);
        }

        private sealed class ClaudeIdentity
        {
            public string Email { get; set; }
            public string AccountId { get; set; }
            public string Tier { get; set; }
            public string AccountKey => FirstNonEmpty(AccountId, Email) ?? "default";
        }

        private sealed class ClaudeCredentials
        {
            public string AccessToken { get; set; }
            public DateTimeOffset? ExpiresAt { get; set; }
            public string Tier { get; set; }
        }

        private sealed class ClaudeSuppressedAccount
        {
            public string AccountId { get; set; }
            public string Email { get; set; }
            public long CredentialsVersion { get; set; }
        }
    }
}
