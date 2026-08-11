using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VibeDeck.Host.Quotas;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class AgyQuotaCacheStoreTests
    {
        [Fact]
        public void WriteAndReadAuthorized_PreserveAccountMetadataAndQuotaWindows()
        {
            var root = CreateTempDirectory();
            try
            {
                using var summaryDocument = JsonDocument.Parse("""
                {
                  "groups": [
                    {
                      "buckets": [
                        { "bucketId": "3p-5h", "remainingFraction": 0.75, "window": "5h" },
                        { "bucketId": "3p-weekly", "remainingFraction": 0.40, "window": "7d" },
                        { "bucketId": "gemini-5h", "remainingFraction": 0.60, "window": "5h" },
                        { "bucketId": "gemini-weekly", "remainingFraction": 0.20, "window": "7d" }
                      ]
                    }
                  ]
                }
                """);
                var observedAt = new DateTimeOffset(2026, 8, 8, 12, 30, 0, TimeSpan.Zero);
                var cacheFile = AgyQuotaCacheStore.ResolveFile(root, "ken@example.com", "agy-1");
                AgyQuotaCacheStore.Write(cacheFile, "ken@example.com", "agy-1", observedAt, summaryDocument.RootElement);

                var statuses = AgyQuotaCacheStore.ReadAuthorized(root, new[]
                {
                    new AgyAccountToken { AccountId = "agy-1", Email = "ken@example.com", Tier = "PRO" }
                }).ToList();

                Assert.Equal(2, statuses.Count);
                var claude = Assert.Single(statuses, status => status.Label == "AGY Claude");
                Assert.Equal("agy-claude-agy-1", claude.Id);
                Assert.Equal("ken@example.com", claude.AccountEmail);
                Assert.Equal("PRO", claude.AccountTier);
                Assert.Equal("ok", claude.State);
                Assert.Equal(75d, claude.Primary.RemainingPercent);
                Assert.Equal(300, claude.Primary.WindowMinutes);
                Assert.Equal(40d, claude.Secondary.RemainingPercent);
                Assert.Equal(10080, claude.Secondary.WindowMinutes);
                Assert.Equal(observedAt, claude.ObservedAt);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ResolveAndFindMatchingFiles_ReuseExistingIdentity()
        {
            var root = CreateTempDirectory();
            try
            {
                using var summaryDocument = JsonDocument.Parse("{\"groups\":[]}");
                var cacheFile = AgyQuotaCacheStore.ResolveFile(root, "ken@example.com", "agy-1");
                AgyQuotaCacheStore.Write(cacheFile, "ken@example.com", "agy-1", DateTimeOffset.UtcNow, summaryDocument.RootElement);

                Assert.Equal(cacheFile, AgyQuotaCacheStore.ResolveFile(root, "KEN@example.com", "other"));
                Assert.Equal(cacheFile, AgyQuotaCacheStore.ResolveFile(root, null, "AGY-1"));
                Assert.Equal(cacheFile, Assert.Single(AgyQuotaCacheStore.FindMatchingFiles(root, "agy-1", null)));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void MergeStatuses_RefreshedRowsReplaceCachedRowsAndRemainSorted()
        {
            var cached = new[]
            {
                new AiQuotaStatus { Id = "same", AccountEmail = "z@example.com", Label = "Cached", State = "offline" },
                new AiQuotaStatus { Id = "other", AccountEmail = "a@example.com", Label = "Other", State = "ok" }
            };
            var refreshed = new[]
            {
                new AiQuotaStatus { Id = "same", AccountEmail = "z@example.com", Label = "Refreshed", State = "ok" }
            };

            var merged = AgyQuotaCacheStore.MergeStatuses(cached, refreshed).ToList();

            Assert.Equal(new[] { "other", "same" }, merged.Select(item => item.Id));
            Assert.Equal("Refreshed", merged.Single(item => item.Id == "same").Label);
        }

        [Fact]
        public void IsFresh_UsesNewestCacheFileWithinFifteenMinutes()
        {
            var root = CreateTempDirectory();
            try
            {
                var cacheFile = Path.Combine(root, "cache.json");
                File.WriteAllText(cacheFile, "{}");
                var now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

                File.SetLastWriteTimeUtc(cacheFile, now.AddMinutes(-14));
                Assert.True(AgyQuotaCacheStore.IsFresh(root, now));

                File.SetLastWriteTimeUtc(cacheFile, now.AddMinutes(-16));
                Assert.False(AgyQuotaCacheStore.IsFresh(root, now));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), $"vibedeck-agy-cache-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
