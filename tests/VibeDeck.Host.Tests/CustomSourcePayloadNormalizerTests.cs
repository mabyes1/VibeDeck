using System;
using System.Text.Json;
using VibeDeck.Host.CustomSources;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class CustomSourcePayloadNormalizerTests
    {
        [Fact]
        public void Message_payload_normalizes_timestamp_severity_and_ttl()
        {
            var source = CreateSource(CustomSourceCardTypes.MessageFeed, defaultTtlSeconds: 30);
            var receivedAt = DateTimeOffset.Parse("2026-07-13T10:00:00+00:00");
            using var payload = JsonDocument.Parse(
                "{\"id\":\"msg-1\",\"text\":\"hello\",\"severity\":\"WARNING\",\"timestamp\":\"2026-07-13T18:00:00+08:00\",\"ttlSeconds\":60}");

            var normalized = CustomSourcePayloadNormalizer.Normalize(source, payload.RootElement, receivedAt);

            Assert.Equal("msg-1", normalized.ItemKey);
            Assert.Equal(receivedAt, normalized.OccurredAt);
            Assert.Equal(receivedAt.AddSeconds(60), normalized.ExpiresAt);
            using var content = JsonDocument.Parse(normalized.ContentJson);
            Assert.Equal("warning", content.RootElement.GetProperty("severity").GetString());
        }

        [Fact]
        public void Metric_payload_rejects_progress_outside_range()
        {
            var source = CreateSource(CustomSourceCardTypes.Metric);
            using var payload = JsonDocument.Parse("{\"value\":42,\"progress\":101}");

            var error = Assert.Throws<CustomSourceProblemException>(() =>
                CustomSourcePayloadNormalizer.Normalize(source, payload.RootElement, DateTimeOffset.UtcNow));

            Assert.Equal(400, error.StatusCode);
            Assert.Equal("invalid_payload", error.Code);
            Assert.Equal("range", error.Fields["progress"]);
        }

        [Fact]
        public void Timestamp_requires_explicit_timezone()
        {
            var source = CreateSource(CustomSourceCardTypes.Status);
            using var payload = JsonDocument.Parse("{\"status\":\"online\",\"timestamp\":\"2026-07-13T10:00:00\"}");

            var error = Assert.Throws<CustomSourceProblemException>(() =>
                CustomSourcePayloadNormalizer.Normalize(source, payload.RootElement, DateTimeOffset.UtcNow));

            Assert.Equal(400, error.StatusCode);
            Assert.Equal("timestamp", error.Fields["timestamp"]);
        }

        private static CustomSourceRecord CreateSource(string type, int defaultTtlSeconds = 0)
        {
            return new CustomSourceRecord
            {
                SourceKey = "test-source",
                Card = new CustomCardRecord
                {
                    Type = type,
                    DefaultTtlSeconds = defaultTtlSeconds
                }
            };
        }
    }
}
