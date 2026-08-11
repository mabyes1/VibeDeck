using System;
using System.IO;

using VibeDeck.Host.CustomSources;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class CustomSourceWriteAccessTests
    {
        [Fact]
        public void NormalizeSourceKeyPreservesValidationContract()
        {
            Assert.Equal("my-source", CustomSourceWriteAccess.NormalizeSourceKey(" My-Source ", validate: true));

            var error = Assert.Throws<CustomSourceProblemException>(() =>
                CustomSourceWriteAccess.NormalizeSourceKey("bad key", validate: true));
            Assert.Equal(400, error.StatusCode);
            Assert.Equal("invalid_source_key", error.Code);
        }

        [Fact]
        public void GeneratedClientTokensKeepPmsPrefixAndHashDeterminism()
        {
            var token = CustomSourceWriteAccess.GenerateToken();

            Assert.StartsWith("vds_", token, StringComparison.Ordinal);
            Assert.DoesNotContain("+", token, StringComparison.Ordinal);
            Assert.DoesNotContain("/", token, StringComparison.Ordinal);
            Assert.Equal(
                CustomSourceWriteAccess.HashToken(token),
                CustomSourceWriteAccess.HashToken(token));
        }

        [Fact]
        public void RateLimiterRejectsBurstOverflowAndRefills()
        {
            var directory = CreateTempDirectory();
            try
            {
                var options = new CustomSourceOptions { RequestsPerSecond = 1, Burst = 1 };
                options.Normalize();
                var access = new CustomSourceWriteAccess(
                    new CustomSourceStore(Path.Combine(directory, "custom-sources.db")),
                    options);
                var source = new CustomSourceRecord { SourceKey = "rate-test" };
                var now = DateTimeOffset.Parse("2026-08-09T00:00:00Z");

                access.EnsureRateLimit(source, now);
                var error = Assert.Throws<CustomSourceProblemException>(() => access.EnsureRateLimit(source, now));
                Assert.Equal(429, error.StatusCode);
                Assert.Equal("rate_limited", error.Code);

                access.EnsureRateLimit(source, now.AddSeconds(1));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "VibeDeck-CustomSourceAccessTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
