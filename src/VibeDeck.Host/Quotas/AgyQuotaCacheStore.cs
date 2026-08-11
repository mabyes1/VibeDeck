using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using static VibeDeck.Host.Quotas.QuotaJsonHelpers;
using static VibeDeck.Host.Quotas.QuotaShared;

namespace VibeDeck.Host.Quotas
{
    internal static class AgyQuotaCacheStore
    {
        internal static IEnumerable<AiQuotaStatus> ReadAuthorized(string cacheDirectory, IReadOnlyList<AgyAccountToken> accounts)
        {
            if (!Directory.Exists(cacheDirectory))
            {
                return Enumerable.Empty<AiQuotaStatus>();
            }

            var metadataByEmail = accounts
                .Where(meta => !string.IsNullOrWhiteSpace(meta.Email))
                .GroupBy(meta => meta.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var metadataByAccountId = accounts
                .Where(meta => !string.IsNullOrWhiteSpace(meta.AccountId))
                .GroupBy(meta => meta.AccountId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var statuses = new List<AiQuotaStatus>();

            foreach (var cacheFile in FindJsonFiles(cacheDirectory))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(cacheFile));
                    var root = doc.RootElement;
                    var email = TryGetString(root, "email");
                    if (!string.IsNullOrWhiteSpace(email) && !seenEmails.Add(email))
                    {
                        continue;
                    }

                    if (!TryGetProperty(root, "payload", out var payload) ||
                        !TryGetProperty(payload, "quota_summary", out var summary) ||
                        !TryGetProperty(summary, "groups", out var groups) ||
                        groups.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var buckets = ReadBuckets(summary);

                    metadataByEmail.TryGetValue(email ?? string.Empty, out var metadata);
                    if (metadata == null)
                    {
                        metadataByAccountId.TryGetValue(TryGetString(root, "accountId") ?? string.Empty, out metadata);
                    }

                    var accountId = metadata?.AccountId ?? TryGetString(root, "accountId") ?? email ?? Path.GetFileNameWithoutExtension(cacheFile);
                    var tier = metadata?.Tier;
                    var observedAt = TryGetUnixTimeMilliseconds(root, "updatedAt") ??
                        new DateTimeOffset(File.GetLastWriteTimeUtc(cacheFile), TimeSpan.Zero);
                    var detail = string.Join(" · ", new[] { email, tier }.Where(value => !string.IsNullOrWhiteSpace(value)));

                    statuses.Add(BuildStatus("agy-claude", "AGY Claude", accountId, email, tier, cacheFile, detail, observedAt, buckets, "3p-5h", "3p-weekly"));
                    statuses.Add(BuildStatus("agy-gemini", "AGY Gemini", accountId, email, tier, cacheFile, detail, observedAt, buckets, "gemini-5h", "gemini-weekly"));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }

            return statuses;
        }

        internal static IEnumerable<string> FindMatchingFiles(string cacheDirectory, string accountId, string email)
        {
            var files = new List<string>();
            foreach (var cacheFile in FindJsonFiles(cacheDirectory))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(cacheFile));
                    if (QuotaAccountIdentity.Matches(
                        TryGetString(doc.RootElement, "accountId"),
                        TryGetString(doc.RootElement, "email"),
                        accountId,
                        email))
                    {
                        files.Add(cacheFile);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }

            return files;
        }

        internal static bool IsFresh(string cacheDirectory, DateTime? nowUtc = null)
        {
            if (!Directory.Exists(cacheDirectory))
            {
                return false;
            }

            var newestCacheFile = Directory.EnumerateFiles(cacheDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newestCacheFile == null)
            {
                return false;
            }

            var now = nowUtc ?? DateTime.UtcNow;
            return newestCacheFile.LastWriteTimeUtc >= now.AddMinutes(-15);
        }

        internal static IEnumerable<AiQuotaStatus> MergeStatuses(
            IEnumerable<AiQuotaStatus> cachedStatuses,
            IEnumerable<AiQuotaStatus> refreshedStatuses)
        {
            var merged = new Dictionary<string, AiQuotaStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var status in cachedStatuses.Where(item => item != null))
            {
                merged[status.Id ?? Guid.NewGuid().ToString("N")] = status;
            }

            foreach (var status in refreshedStatuses.Where(item => item != null))
            {
                merged[status.Id ?? Guid.NewGuid().ToString("N")] = status;
            }

            return merged.Values
                .OrderBy(item => item.AccountEmail ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Label ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static IReadOnlyDictionary<string, QuotaBucketInfo> ReadBuckets(JsonElement quotaSummary)
        {
            var buckets = new Dictionary<string, QuotaBucketInfo>(StringComparer.OrdinalIgnoreCase);
            if (!TryGetProperty(quotaSummary, "groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            {
                return buckets;
            }

            foreach (var group in groups.EnumerateArray())
            {
                if (!TryGetProperty(group, "buckets", out var bucketArray) || bucketArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var bucket in bucketArray.EnumerateArray())
                {
                    var bucketId = TryGetString(bucket, "bucketId");
                    if (string.IsNullOrWhiteSpace(bucketId) || buckets.ContainsKey(bucketId))
                    {
                        continue;
                    }

                    buckets[bucketId] = ReadBucket(bucket);
                }
            }

            return buckets;
        }

        internal static string ResolveFile(string cacheDirectory, string email, string accountId)
        {
            foreach (var cacheFile in FindJsonFiles(cacheDirectory))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(cacheFile));
                    var cachedEmail = TryGetString(doc.RootElement, "email");
                    var cachedAccountId = TryGetString(doc.RootElement, "accountId");
                    if (!string.IsNullOrWhiteSpace(email) &&
                        string.Equals(email, cachedEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        return cacheFile;
                    }

                    if (!string.IsNullOrWhiteSpace(accountId) &&
                        string.Equals(accountId, cachedAccountId, StringComparison.OrdinalIgnoreCase))
                    {
                        return cacheFile;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                }
            }

            var fileName = !string.IsNullOrWhiteSpace(accountId) ? accountId : Guid.NewGuid().ToString("N");
            return Path.Combine(cacheDirectory, $"{SafeFileName(fileName)}.json");
        }

        internal static void Write(string cacheFile, string email, string accountId, DateTimeOffset observedAt, JsonElement quotaSummary)
        {
            var envelope = new Dictionary<string, object>
            {
                ["email"] = email,
                ["accountId"] = accountId,
                ["updatedAt"] = observedAt.ToUnixTimeMilliseconds(),
                ["source"] = "vibedeck-api",
                ["payload"] = new Dictionary<string, object>
                {
                    ["quota_summary"] = quotaSummary
                }
            };

            File.WriteAllText(cacheFile, JsonSerializer.Serialize(envelope, CacheJsonOptions), Encoding.UTF8);
        }

        internal static AiQuotaStatus BuildStatus(
            string id,
            string label,
            string accountId,
            string accountEmail,
            string accountTier,
            string source,
            string detail,
            DateTimeOffset? observedAt,
            IReadOnlyDictionary<string, QuotaBucketInfo> buckets,
            string fiveHourBucket,
            string weeklyBucket)
        {
            buckets.TryGetValue(fiveHourBucket, out var primary);
            buckets.TryGetValue(weeklyBucket, out var secondary);

            return new AiQuotaStatus
            {
                Id = string.IsNullOrWhiteSpace(accountId) ? id : $"{id}-{accountId}",
                Label = label,
                Family = "agy",
                AccountId = accountId,
                AccountEmail = accountEmail,
                AccountTier = accountTier,
                State = primary != null || secondary != null ? "ok" : "source-needed",
                Source = source,
                Detail = string.IsNullOrWhiteSpace(detail) ? "Antigravity desktop quota cache." : detail,
                ObservedAt = observedAt,
                Primary = ReadWindow(primary, "5h", 300),
                Secondary = ReadWindow(secondary, "Weekly", 10080)
            };
        }

        private static QuotaWindow ReadWindow(QuotaBucketInfo bucket, string label, int defaultWindowMinutes)
        {
            if (bucket == null)
            {
                return null;
            }

            return new QuotaWindow
            {
                Label = label,
                RemainingPercent = bucket.RemainingPercent,
                WindowMinutes = bucket.WindowMinutes ?? defaultWindowMinutes,
                ResetsAt = bucket.ResetsAt
            };
        }

        private static QuotaBucketInfo ReadBucket(JsonElement bucket)
        {
            var remainingFraction = TryGetDouble(bucket, "remainingFraction");
            return new QuotaBucketInfo
            {
                RemainingPercent = remainingFraction.HasValue ? remainingFraction.Value * 100d : (double?)null,
                ResetsAt = TryGetDateTimeOffset(bucket, "resetTime"),
                WindowMinutes = ParseWindowMinutes(TryGetString(bucket, "window"))
            };
        }
    }

    internal sealed class QuotaBucketInfo
    {
        public double? RemainingPercent { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
        public int? WindowMinutes { get; set; }
    }
}
