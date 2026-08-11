using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using static VibeDeck.Host.CustomSources.CustomSourceValidation;

namespace VibeDeck.Host.CustomSources
{
    internal sealed class CustomSourceWriteAccess
    {
        private static readonly Regex SourceKeyPattern = new Regex(
            "^[a-z0-9][a-z0-9-]{1,46}[a-z0-9]$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly string TimingDecoyHash =
            HashToken("vds_timing_decoy_" + Guid.NewGuid().ToString("N"));

        private readonly CustomSourceStore store;
        private readonly CustomSourceOptions options;
        private readonly ConcurrentDictionary<string, TokenBucket> rateBuckets =
            new ConcurrentDictionary<string, TokenBucket>(StringComparer.OrdinalIgnoreCase);

        internal CustomSourceWriteAccess(CustomSourceStore store, CustomSourceOptions options)
        {
            this.store = store;
            this.options = options;
        }

        internal CustomSourceRecord Authenticate(string sourceKey, string token)
        {
            var normalizedKey = NormalizeSourceKeyForLookup(sourceKey);
            EnsureNotSystemSource(normalizedKey);
            var source = normalizedKey == null ? null : store.GetSource(normalizedKey);
            var suppliedHash = HashToken(token ?? string.Empty);
            var valid = FixedTimeEquals(suppliedHash, source?.TokenHash ?? TimingDecoyHash);
            if (source == null || !valid)
            {
                throw new CustomSourceProblemException(401, "invalid_source_token", "The source token is invalid.");
            }
            if (!source.Enabled)
            {
                throw new CustomSourceProblemException(403, "source_disabled", "The custom source is disabled.");
            }
            return source;
        }

        internal void EnsureRateLimit(CustomSourceRecord source, DateTimeOffset now)
        {
            var bucket = rateBuckets.GetOrAdd(
                source.SourceKey,
                _ => new TokenBucket(options.RequestsPerSecond, options.Burst, now));
            if (bucket.TryConsume(now, out var retryAfter))
            {
                return;
            }

            throw new CustomSourceProblemException(
                429,
                "rate_limited",
                "The custom source is sending too quickly.",
                new Dictionary<string, string>
                {
                    ["retryAfterSeconds"] = retryAfter.ToString(CultureInfo.InvariantCulture)
                });
        }

        internal void RemoveRateState(string sourceKey)
        {
            rateBuckets.TryRemove(sourceKey, out _);
        }

        internal static bool IsSystemSource(string sourceKey)
        {
            return string.Equals(sourceKey, CustomSourceKeys.WindowsNotifications, StringComparison.OrdinalIgnoreCase);
        }

        internal static void EnsureNotSystemSource(string sourceKey)
        {
            if (IsSystemSource(sourceKey))
            {
                Problem(403, "system_source", "The Windows notification source is managed by VibeDeck.");
            }
        }

        internal static string NormalizeSourceKey(string value, bool validate)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (validate && !SourceKeyPattern.IsMatch(normalized))
            {
                Problem(400, "invalid_source_key", "sourceKey must use lowercase letters, numbers, and hyphens.", "sourceKey", "invalid");
            }
            return normalized;
        }

        internal static string GenerateToken()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return "vds_" + Convert.ToBase64String(bytes)
                .Replace("+", "-", StringComparison.Ordinal)
                .Replace("/", "_", StringComparison.Ordinal)
                .TrimEnd('=');
        }

        internal static string HashToken(string value)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        internal static string GenerateSystemSourceTokenHash()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }

        private static string NormalizeSourceKeyForLookup(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return SourceKeyPattern.IsMatch(normalized) ? normalized : null;
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
            var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
            return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private sealed class TokenBucket
        {
            private readonly double refillPerSecond;
            private readonly double capacity;
            private readonly object sync = new object();
            private double tokens;
            private DateTimeOffset last;

            internal TokenBucket(double refillPerSecond, double capacity, DateTimeOffset now)
            {
                this.refillPerSecond = refillPerSecond;
                this.capacity = capacity;
                tokens = capacity;
                last = now;
            }

            internal bool TryConsume(DateTimeOffset now, out int retryAfterSeconds)
            {
                lock (sync)
                {
                    var elapsed = Math.Max(0, (now - last).TotalSeconds);
                    tokens = Math.Min(capacity, tokens + elapsed * refillPerSecond);
                    last = now;
                    if (tokens >= 1)
                    {
                        tokens -= 1;
                        retryAfterSeconds = 0;
                        return true;
                    }

                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((1 - tokens) / refillPerSecond));
                    return false;
                }
            }
        }
    }
}
