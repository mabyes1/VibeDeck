using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using static VibeDeck.Host.Quotas.QuotaJsonHelpers;
using static VibeDeck.Host.Quotas.QuotaPaths;
using static VibeDeck.Host.Quotas.QuotaShared;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Reads Codex quota state from disk: session rate-limit events, auth.json identities
    /// (incl. JWT parsing), and the VibeDeck quota cache. Stateless.
    /// </summary>
    internal static class CodexQuotaReader
    {
        private const int MaxSessionFiles = 120;
        private const int TailBytes = 768 * 1024;
        private static readonly object CacheFileLock = new object();

        internal static IEnumerable<AiQuotaStatus> ReadCodexQuotas()
        {
            var codexHomes = ResolveCodexHomes().ToList();
            var cacheDirectory = CodexQuotaCacheDirectory();
            var identities = codexHomes
                .SelectMany(ReadCodexAuthIdentities)
                .GroupBy(identity => CodexIdentityKey(identity.AccountId, identity.Email, identity.Source), StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(identity => string.Equals(Path.GetFileName(identity.Source), "auth.json", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(identity => File.GetLastWriteTimeUtc(identity.Source))
                    .First())
                .ToList();
            var activeIdentity = identities
                .FirstOrDefault(identity => string.Equals(Path.GetFileName(identity.Source), "auth.json", StringComparison.OrdinalIgnoreCase))
                ?? identities.FirstOrDefault()
                ?? new CodexAccountIdentity
                {
                    AccountId = "local",
                    Source = codexHomes.FirstOrDefault() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
                };

            var activeSource = activeIdentity.Source?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var activeHome = string.Equals(Path.GetFileName(activeSource), ".codex", StringComparison.OrdinalIgnoreCase)
                ? activeSource
                : Path.GetDirectoryName(activeSource);
            var activeQuota = !string.IsNullOrWhiteSpace(activeHome)
                ? ReadCodexQuota(activeHome)
                : Unavailable("codex", "Codex", "Codex session directory was not found.", activeHome);
            if (!IsUsableQuota(activeQuota))
            {
                activeQuota = codexHomes
                    .Select(ReadCodexQuota)
                    .Where(IsUsableQuota)
                    .OrderByDescending(status => status.ObservedAt ?? DateTimeOffset.MinValue)
                    .FirstOrDefault()
                    ?? activeQuota;
            }
            if (IsUsableQuota(activeQuota) && IsCodexQuotaCompatibleWithIdentity(activeQuota, activeIdentity))
            {
                ApplyCodexIdentity(activeQuota, activeIdentity);
                WriteCodexQuotaCache(cacheDirectory, activeQuota);
            }

            var statuses = ReadCodexQuotaCache(cacheDirectory).ToList();
            if (IsUsableQuota(activeQuota) &&
                !string.Equals(activeQuota.AccountId, "local", StringComparison.OrdinalIgnoreCase) &&
                !statuses.Any(status => SameCodexAccount(status, activeQuota)))
            {
                statuses.Add(activeQuota);
            }

            foreach (var identity in identities)
            {
                if (!statuses.Any(status => SameCodexAccount(status, identity)))
                {
                    statuses.Add(BuildCodexPlaceholder(identity));
                }
            }

            foreach (var status in statuses)
            {
                status.IsActive = SameCodexAccount(status, activeIdentity);
            }

            if (statuses.Any())
            {
                return statuses
                    .OrderByDescending(status => status.ObservedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(status => status.AccountEmail ?? status.AccountId ?? status.Id)
                    .ToList();
            }

            return new[] { activeQuota };
        }

        private static IReadOnlyList<string> ResolveCodexHomes()
        {
            var homes = new List<string>();

            void AddHome(string home)
            {
                if (string.IsNullOrWhiteSpace(home))
                {
                    return;
                }

                var fullPath = Path.GetFullPath(home);
                if (Directory.Exists(fullPath) && !homes.Any(item => string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    homes.Add(fullPath);
                }
            }

            AddHome(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));

            try
            {
                var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
                var usersRoot = string.IsNullOrWhiteSpace(systemRoot)
                    ? null
                    : Path.Combine(systemRoot, "Users");
                if (Directory.Exists(usersRoot))
                {
                    foreach (var profile in Directory.EnumerateDirectories(usersRoot))
                    {
                        AddHome(Path.Combine(profile, ".codex"));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
            }

            return homes;
        }

        /// <summary>
        /// Reads the account identity (id/email/tier) from a single Codex auth.json.
        /// Used by CodexAccountStore to label captured profiles. Returns nulls if the
        /// file is missing or not a logged-in account.
        /// </summary>
        internal static (string AccountId, string Email, string Tier) ReadAuthFileIdentity(string authFile)
        {
            var identity = TryReadCodexIdentityFromAuthFile(authFile);
            return identity == null
                ? (null, null, null)
                : (identity.AccountId, identity.Email, identity.Tier);
        }

        internal static (string AccountId, string Email, string Tier) ReadTokenIdentity(
            string idToken,
            string accessToken)
        {
            var identity = new CodexAccountIdentity();
            MergeCodexIdentity(identity, ReadCodexIdentityFromJwt(idToken));
            MergeCodexIdentity(identity, ReadCodexIdentityFromJwt(accessToken));
            return (identity.AccountId, identity.Email, identity.Tier);
        }

        private static AiQuotaStatus ReadCodexQuota(string codexHome)
        {
            var sessionsRoot = Path.Combine(codexHome, "sessions");

            if (!Directory.Exists(sessionsRoot))
            {
                return Unavailable("codex", "Codex", "Codex session directory was not found.", sessionsRoot);
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .Take(MaxSessionFiles))
                {
                    var status = TryReadCodexQuotaFromFile(file.FullName);
                    if (status != null)
                    {
                        return status;
                    }
                }

                return Unavailable("codex", "Codex", "No recent Codex rate_limits event was found.", sessionsRoot);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return Unavailable("codex", "Codex", ex.Message, sessionsRoot);
            }
        }

        internal static AiQuotaStatus TryReadCodexQuotaFromFile(string path)
        {
            var lines = ReadTailLines(path, TailBytes);
            for (var index = lines.Count - 1; index >= 0; index--)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"rate_limits\""))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!TryGetProperty(root, "payload", out var payload) ||
                    !TryGetProperty(payload, "rate_limits", out var limits) ||
                    limits.ValueKind == JsonValueKind.Null ||
                    limits.ValueKind == JsonValueKind.Undefined)
                {
                    continue;
                }

                var observedAt = TryGetDateTimeOffset(root, "timestamp");
                var credits = TryGetProperty(limits, "credits", out var creditInfo) &&
                    creditInfo.ValueKind == JsonValueKind.Object
                    ? creditInfo
                    : default;
                return new AiQuotaStatus
                {
                    Id = "codex",
                    Label = "Codex",
                    Family = "codex",
                    AccountId = "local",
                    State = "ok",
                    Source = path,
                    Detail = TryGetString(limits, "limit_name") ?? TryGetString(limits, "limit_id") ?? "Latest Codex rate limit event.",
                    ObservedAt = observedAt,
                    AccountTier = TryGetString(limits, "plan_type"),
                    CreditBalance = credits.ValueKind == JsonValueKind.Object ? TryGetDouble(credits, "balance") : null,
                    CreditUnlimited = credits.ValueKind == JsonValueKind.Object ? TryGetBoolean(credits, "unlimited") : null,
                    Primary = ReadWindow(limits, "primary", "5h"),
                    Secondary = ReadWindow(limits, "secondary", "Weekly")
                };
            }

            return null;
        }

        private static bool IsUsableQuota(AiQuotaStatus status)
        {
            return status != null &&
                string.Equals(status.State, "ok", StringComparison.OrdinalIgnoreCase) &&
                (status.Primary != null || status.Secondary != null);
        }

        private static void ApplyCodexIdentity(AiQuotaStatus status, CodexAccountIdentity identity)
        {
            if (status == null || identity == null)
            {
                return;
            }

            var accountId = FirstNonEmpty(identity.AccountId, status.AccountId, identity.Email, "local");
            status.Id = BuildCodexStatusId(CodexIdentityKey(accountId, identity.Email, status.AccountEmail ?? "local"));
            status.AccountId = accountId;
            status.AccountEmail = identity.Email;
            status.AccountTier = FirstNonEmpty(identity.Tier, status.AccountTier);
            status.Family = "codex";
            status.Label = "Codex";
        }

        private static AiQuotaStatus BuildCodexPlaceholder(CodexAccountIdentity identity)
        {
            var accountId = FirstNonEmpty(identity.AccountId, identity.Email, "local");
            return new AiQuotaStatus
            {
                Id = BuildCodexStatusId(CodexIdentityKey(accountId, identity.Email, "local")),
                Label = "Codex",
                Family = "codex",
                AccountId = accountId,
                AccountEmail = identity.Email,
                AccountTier = identity.Tier,
                State = "source-needed",
                Source = identity.Source,
                Detail = "Codex account was seen, but VibeDeck has not recorded a quota snapshot for it yet."
            };
        }

        private static bool IsCodexQuotaCompatibleWithIdentity(AiQuotaStatus status, CodexAccountIdentity identity)
        {
            if (status?.ObservedAt == null ||
                identity == null ||
                string.IsNullOrWhiteSpace(identity.Source) ||
                !File.Exists(identity.Source))
            {
                return true;
            }

            var authWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(identity.Source), TimeSpan.Zero);
            return status.ObservedAt.Value >= authWriteTime.AddMinutes(-2);
        }

        private static IEnumerable<AiQuotaStatus> ReadCodexQuotaCache(string cacheDirectory)
        {
            lock (CacheFileLock)
            {
                if (!Directory.Exists(cacheDirectory))
                {
                    return Enumerable.Empty<AiQuotaStatus>();
                }

                var statuses = new List<AiQuotaStatus>();
                foreach (var cacheFile in FindJsonFiles(cacheDirectory))
                {
                    try
                    {
                        var status = JsonSerializer.Deserialize<AiQuotaStatus>(File.ReadAllText(cacheFile), CacheJsonOptions);
                        if (status == null)
                        {
                            continue;
                        }

                        status.Id = string.IsNullOrWhiteSpace(status.Id)
                            ? BuildCodexStatusId(CodexIdentityKey(status.AccountId, status.AccountEmail, Path.GetFileNameWithoutExtension(cacheFile)))
                            : status.Id;
                        status.Label = string.IsNullOrWhiteSpace(status.Label) ? "Codex" : status.Label;
                        status.Family = "codex";
                        status.Source = string.IsNullOrWhiteSpace(status.Source) ? cacheFile : status.Source;
                        statuses.Add(status);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                    {
                    }
                }

                return statuses
                    .GroupBy(status => CodexIdentityKey(status.AccountId, status.AccountEmail, status.Id ?? Guid.NewGuid().ToString("N")), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderByDescending(status => status.ObservedAt ?? DateTimeOffset.MinValue)
                        .First())
                    .ToList();
            }
        }

        internal static AiQuotaStatus ReadCachedQuotaForAccount(string accountId, string email)
        {
            return ReadCodexQuotaCache(CodexQuotaCacheDirectory())
                .Where(status => AccountMatches(status, accountId, email))
                .OrderByDescending(status => status.ObservedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault();
        }

        internal static void WriteCodexQuotaCache(string cacheDirectory, AiQuotaStatus status)
        {
            if (!IsUsableQuota(status))
            {
                return;
            }

            lock (CacheFileLock)
            {
                Directory.CreateDirectory(cacheDirectory);
                var accountKey = CodexIdentityKey(status.AccountId, status.AccountEmail, status.Id ?? "local");
                var path = Path.Combine(cacheDirectory, $"{SafeFileName(accountKey)}.json");
                try
                {
                    if (File.Exists(path))
                    {
                        var current = JsonSerializer.Deserialize<AiQuotaStatus>(File.ReadAllText(path), CacheJsonOptions);
                        if (current?.ObservedAt != null && status.ObservedAt != null && current.ObservedAt > status.ObservedAt)
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
                File.WriteAllText(path, JsonSerializer.Serialize(status, CacheJsonOptions));
            }
        }

        private static bool AccountMatches(AiQuotaStatus status, string accountId, string email)
        {
            if (status == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(status.AccountEmail))
            {
                return string.Equals(status.AccountEmail, email, StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(accountId) &&
                string.Equals(status.AccountId, accountId, StringComparison.OrdinalIgnoreCase);
        }

        internal static AiQuotaStatus BuildCodexQuotaFromUsage(
            JsonElement usage,
            CodexAccountStore.CodexCredentials credentials,
            DateTimeOffset observedAt,
            string source)
        {
            if (credentials == null)
            {
                return null;
            }

            var limits = usage;
            if (TryGetProperty(usage, "rate_limits", out var rateLimits) && rateLimits.ValueKind == JsonValueKind.Object)
            {
                limits = rateLimits;
            }
            else if (TryGetProperty(usage, "rate_limit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
            {
                limits = rateLimit;
            }

            var primary = ReadUsageWindow(limits, observedAt, "5h", "primary", "primary_window", "primaryWindow");
            var secondary = ReadUsageWindow(limits, observedAt, "Weekly", "secondary", "secondary_window", "secondaryWindow");

            JsonElement credits = default;
            var hasCredits = TryGetProperty(limits, "credits", out credits) && credits.ValueKind == JsonValueKind.Object;
            if (!hasCredits)
            {
                hasCredits = TryGetProperty(usage, "credits", out credits) && credits.ValueKind == JsonValueKind.Object;
            }

            var creditBalance = hasCredits ? TryGetDouble(credits, "balance") : null;
            var creditUnlimited = hasCredits ? TryGetBoolean(credits, "unlimited") : null;
            if (primary == null && secondary == null && creditBalance == null && creditUnlimited == null)
            {
                return null;
            }

            var accountId = FirstNonEmpty(credentials.AccountId, credentials.Email, "local");
            return new AiQuotaStatus
            {
                Id = BuildCodexStatusId(CodexIdentityKey(accountId, credentials.Email, "local")),
                Label = "Codex",
                Family = "codex",
                AccountId = accountId,
                AccountEmail = credentials.Email,
                AccountTier = FirstNonEmpty(
                    credentials.Tier,
                    TryGetString(limits, "plan_type"),
                    TryGetString(usage, "plan_type")),
                State = "ok",
                Source = source,
                Detail = "Codex account usage endpoint.",
                ObservedAt = observedAt,
                CreditBalance = creditBalance,
                CreditUnlimited = creditUnlimited,
                Primary = primary,
                Secondary = secondary
            };
        }

        private static QuotaWindow ReadUsageWindow(
            JsonElement limits,
            DateTimeOffset observedAt,
            string label,
            params string[] names)
        {
            JsonElement bucket = default;
            var found = false;
            foreach (var name in names)
            {
                if (TryGetProperty(limits, name, out bucket) && bucket.ValueKind == JsonValueKind.Object)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return null;
            }

            var usedPercent = TryGetDouble(bucket, "used_percent") ?? TryGetDouble(bucket, "usedPercent");
            var windowMinutes = TryGetInt(bucket, "window_minutes") ?? TryGetInt(bucket, "windowMinutes");
            if (!windowMinutes.HasValue)
            {
                var seconds = TryGetDouble(bucket, "limit_window_seconds") ?? TryGetDouble(bucket, "window_seconds");
                if (seconds.HasValue)
                {
                    windowMinutes = Math.Max(1, (int)Math.Round(seconds.Value / 60d));
                }
            }

            var resetsAt = TryGetUnixTime(bucket, "resets_at")
                ?? TryGetUnixTime(bucket, "reset_at")
                ?? TryGetDateTimeOffset(bucket, "resets_at")
                ?? TryGetDateTimeOffset(bucket, "reset_at")
                ?? TryGetDateTimeOffset(bucket, "resetAt");
            if (!resetsAt.HasValue)
            {
                var resetAfterSeconds = TryGetDouble(bucket, "reset_after_seconds") ?? TryGetDouble(bucket, "resetAfterSeconds");
                if (resetAfterSeconds.HasValue)
                {
                    resetsAt = observedAt.AddSeconds(Math.Max(0d, resetAfterSeconds.Value));
                }
            }

            return new QuotaWindow
            {
                Label = label,
                UsedPercent = usedPercent,
                RemainingPercent = usedPercent.HasValue ? Math.Max(0d, 100d - usedPercent.Value) : null,
                WindowMinutes = windowMinutes,
                ResetsAt = resetsAt
            };
        }

        private static IReadOnlyList<CodexAccountIdentity> ReadCodexAuthIdentities(string codexHome)
        {
            if (!Directory.Exists(codexHome))
            {
                return Array.Empty<CodexAccountIdentity>();
            }

            var identities = new List<CodexAccountIdentity>();
            // Backups are retained for recovery, but they are not live Codex
            // profiles and must not appear as extra quota accounts.
            foreach (var authFile in Directory.EnumerateFiles(codexHome, "auth*.json", SearchOption.TopDirectoryOnly)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var identity = TryReadCodexIdentityFromAuthFile(authFile);
                if (identity != null)
                {
                    identities.Add(identity);
                }
            }

            return identities
                .GroupBy(identity => CodexIdentityKey(identity.AccountId, identity.Email, Path.GetFileNameWithoutExtension(identity.Source)), StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(identity => string.Equals(Path.GetFileName(identity.Source), "auth.json", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(identity => File.GetLastWriteTimeUtc(identity.Source))
                    .First())
                .ToList();
        }

        private static CodexAccountIdentity TryReadCodexIdentityFromAuthFile(string authFile)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(authFile));
                var root = doc.RootElement;
                if (!TryGetProperty(root, "tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var identity = new CodexAccountIdentity
                {
                    AccountId = TryGetString(tokens, "account_id"),
                    Source = authFile
                };

                MergeCodexIdentity(identity, ReadCodexIdentityFromJwt(TryGetString(tokens, "id_token")));
                MergeCodexIdentity(identity, ReadCodexIdentityFromJwt(TryGetString(tokens, "access_token")));

                if (string.IsNullOrWhiteSpace(identity.AccountId) && string.IsNullOrWhiteSpace(identity.Email))
                {
                    return null;
                }

                return identity;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is FormatException || ex is ArgumentException)
            {
                return null;
            }
        }

        private static CodexAccountIdentity ReadCodexIdentityFromJwt(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new CodexAccountIdentity();
            }

            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return new CodexAccountIdentity();
            }

            try
            {
                var payload = Encoding.UTF8.GetString(DecodeBase64Url(parts[1]));
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var identity = new CodexAccountIdentity
                {
                    AccountId = TryGetString(root, "sub"),
                    Email = TryGetString(root, "email")
                };

                if (TryGetProperty(root, "https://api.openai.com/auth", out var auth) && auth.ValueKind == JsonValueKind.Object)
                {
                    identity.AccountId = FirstNonEmpty(
                        TryGetString(auth, "chatgpt_account_id"),
                        TryGetString(auth, "account_id"),
                        TryGetString(auth, "chatgpt_user_id"),
                        TryGetString(auth, "user_id"),
                        identity.AccountId);
                    identity.Tier = TryGetString(auth, "chatgpt_plan_type");
                }

                if (TryGetProperty(root, "https://api.openai.com/profile", out var profile) && profile.ValueKind == JsonValueKind.Object)
                {
                    identity.Email = FirstNonEmpty(TryGetString(profile, "email"), identity.Email);
                }

                return identity;
            }
            catch (Exception ex) when (ex is FormatException || ex is JsonException || ex is ArgumentException)
            {
                return new CodexAccountIdentity();
            }
        }

        private static void MergeCodexIdentity(CodexAccountIdentity target, CodexAccountIdentity source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.AccountId = FirstNonEmpty(target.AccountId, source.AccountId);
            target.Email = FirstNonEmpty(target.Email, source.Email);
            target.Tier = FirstNonEmpty(target.Tier, source.Tier);
        }

        internal static bool SameCodexAccount(AiQuotaStatus status, AiQuotaStatus other)
        {
            return status != null && other != null &&
                string.Equals(
                    CodexIdentityKey(status.AccountId, status.AccountEmail, status.Id),
                    CodexIdentityKey(other.AccountId, other.AccountEmail, other.Id),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameCodexAccount(AiQuotaStatus status, CodexAccountIdentity identity)
        {
            return status != null && identity != null &&
                string.Equals(
                    CodexIdentityKey(status.AccountId, status.AccountEmail, status.Id),
                    CodexIdentityKey(identity.AccountId, identity.Email),
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static string CodexIdentityKey(string accountId, string email, string fallback = null)
        {
            return FirstNonEmpty(email, accountId, fallback);
        }

        private static string BuildCodexStatusId(string accountId)
        {
            return string.IsNullOrWhiteSpace(accountId) || string.Equals(accountId, "local", StringComparison.OrdinalIgnoreCase)
                ? "codex"
                : $"codex-{SafeFileName(accountId)}";
        }

        private static QuotaWindow ReadWindow(JsonElement limits, string propertyName, string label)
        {
            if (!TryGetProperty(limits, propertyName, out var window) ||
                window.ValueKind == JsonValueKind.Null ||
                window.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            return new QuotaWindow
            {
                Label = label,
                UsedPercent = TryGetDouble(window, "used_percent"),
                WindowMinutes = TryGetInt(window, "window_minutes"),
                ResetsAt = TryGetUnixTime(window, "resets_at")
            };
        }

        internal sealed class CodexAccountIdentity
        {
            public string AccountId { get; set; }
            public string Email { get; set; }
            public string Tier { get; set; }
            public string Source { get; set; }
        }
    }
}
