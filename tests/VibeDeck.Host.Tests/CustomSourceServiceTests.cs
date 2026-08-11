using System;
using System.IO;
using System.Text.Json;
using VibeDeck.Host.CustomSources;
using VibeDeck.Host.Dashboard;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class CustomSourceServiceTests
    {
        [Fact]
        public void CreateAndIngestMessageFeedProducesFreshSnapshot()
        {
            using var fixture = new Fixture();
            var now = DateTimeOffset.Parse("2026-07-13T10:00:00+00:00");
            var created = fixture.Service.Create(
                new CustomSourceCreateRequest
                {
                    SourceKey = "test-chat",
                    DisplayName = "Test Chat",
                    Card = new CustomCardCreateRequest
                    {
                        Type = CustomSourceCardTypes.MessageFeed,
                        Title = "Incoming messages",
                        MaxItems = 5
                    }
                },
                "https://host.example/api/custom-sources/test-chat/events",
                "http://127.0.0.1:5000/api/custom-sources/test-chat/events",
                now);

            Assert.StartsWith("vds_", created.Ingest.Token, StringComparison.Ordinal);
            Assert.Equal("message-feed", created.Source.Card.Type);

            using var payload = JsonDocument.Parse("{\"id\":\"msg-123\",\"from\":\"Discord\",\"text\":\"有人找你\",\"timestamp\":\"2026-07-13T18:00:00+08:00\"}");
            var result = fixture.Service.Ingest("test-chat", created.Ingest.Token, payload.RootElement, now);
            var snapshot = fixture.Service.GetCardSnapshot(now);

            Assert.Equal("inserted", result.Operation);
            Assert.Single(snapshot.Cards);
            Assert.Equal("fresh", snapshot.Cards[0].Freshness);
            Assert.Contains("msg-123", JsonSerializer.Serialize(snapshot.Cards[0].Content));
        }

        [Fact]
        public void ReusingMessageIdUpdatesOneItemAndAdvancesRevision()
        {
            using var fixture = new Fixture();
            var now = DateTimeOffset.UtcNow;
            var created = fixture.Create("message-feed");

            using var first = JsonDocument.Parse("{\"id\":\"same\",\"text\":\"first\"}");
            using var second = JsonDocument.Parse("{\"id\":\"same\",\"text\":\"second\"}");
            var firstResult = fixture.Service.Ingest("test-feed", created.Ingest.Token, first.RootElement, now);
            var secondResult = fixture.Service.Ingest("test-feed", created.Ingest.Token, second.RootElement, now.AddSeconds(1));

            Assert.Equal("inserted", firstResult.Operation);
            Assert.Equal("updated", secondResult.Operation);
            Assert.Equal(firstResult.Revision + 1, secondResult.Revision);
            Assert.Equal(1, fixture.Service.GetSources(now.AddSeconds(1))[0].ItemCount);
        }

        [Fact]
        public void InvalidTokenUsesStableUnauthorizedError()
        {
            using var fixture = new Fixture();
            var created = fixture.Create("status");
            using var payload = JsonDocument.Parse("{\"status\":\"online\"}");

            var error = Assert.Throws<CustomSourceProblemException>(() =>
                fixture.Service.Ingest("test-status", "vds_invalid", payload.RootElement, DateTimeOffset.UtcNow));

            Assert.Equal(401, error.StatusCode);
            Assert.Equal("invalid_source_token", error.Code);
        }

        [Fact]
        public void ExpiredStateIsRemovedByCleanup()
        {
            using var fixture = new Fixture();
            var created = fixture.Create("status", defaultTtlSeconds: 30);
            var receivedAt = DateTimeOffset.Parse("2026-07-13T10:00:00+00:00");
            using var payload = JsonDocument.Parse("{\"status\":\"online\",\"ttlSeconds\":30}");

            fixture.Service.Ingest("test-status", created.Ingest.Token, payload.RootElement, receivedAt);
            fixture.Service.CleanupExpired(receivedAt.AddSeconds(31));
            var snapshot = fixture.Service.GetCardSnapshot(receivedAt.AddSeconds(31));

            Assert.Equal("empty", snapshot.Cards[0].Freshness);
            Assert.Null(snapshot.Cards[0].Content);
        }

        [Fact]
        public void WindowsNotificationSystemSourceIsHiddenFromManagementAndAcceptsNormalizedEvents()
        {
            using var fixture = new Fixture();
            var now = DateTimeOffset.Parse("2026-07-13T10:00:00+00:00");
            var source = fixture.Service.EnsureSystemSource(now);

            using var payload = JsonDocument.Parse("{\"id\":\"win-123\",\"from\":\"Teams\",\"text\":\"有人找你\",\"timestamp\":\"2026-07-13T18:00:00+08:00\"}");
            var result = fixture.Service.IngestSystem(CustomSourceKeys.WindowsNotifications, payload.RootElement, now);
            var snapshot = fixture.Service.GetCardSnapshot(now);

            Assert.Equal("windows-notifications", source.SourceKey);
            Assert.Empty(fixture.Service.GetSources(now));
            Assert.Equal("inserted", result.Operation);
            Assert.Single(snapshot.Cards);
            Assert.Equal("Windows 通知", snapshot.Cards[0].Title);
            Assert.Contains("win-123", JsonSerializer.Serialize(snapshot.Cards[0].Content));
        }

        [Fact]
        public void SystemSourceRejectsTokenAuthenticatedWritesFromPublicPath()
        {
            using var fixture = new Fixture();
            var now = DateTimeOffset.Parse("2026-07-13T10:00:00+00:00");

            // The internally-managed Windows notification source exists on disk...
            fixture.Service.EnsureSystemSource(now);

            using var payload = JsonDocument.Parse("{\"id\":\"spoof-1\",\"from\":\"Bank\",\"text\":\"fake alert\"}");

            // ...but the formerly-constant system token — and any token, in any casing — must
            // be refused on the public token-authenticated write path with 403 system_source,
            // not merely a 401 hash mismatch. A 403 proves the guard fired, which is the exact
            // remote-spoof backdoor being closed.
            foreach (var (key, token) in new[]
            {
                (CustomSourceKeys.WindowsNotifications, "vds_dummy_custom_source_token"),
                (CustomSourceKeys.WindowsNotifications, "vds_anything_else"),
                ("Windows-Notifications", "vds_dummy_custom_source_token"),
            })
            {
                var ingest = Assert.Throws<CustomSourceProblemException>(() =>
                    fixture.Service.Ingest(key, token, payload.RootElement, now));
                Assert.Equal(403, ingest.StatusCode);
                Assert.Equal("system_source", ingest.Code);
            }

            // Item deletion on the system feed is likewise blocked from the public path.
            var delete = Assert.Throws<CustomSourceProblemException>(() =>
                fixture.Service.DeleteItem(
                    CustomSourceKeys.WindowsNotifications, "vds_dummy_custom_source_token", "spoof-1", now));
            Assert.Equal(403, delete.StatusCode);
            Assert.Equal("system_source", delete.Code);

            // The internal ingest path still works — we only closed the client-token door.
            var ok = fixture.Service.IngestSystem(CustomSourceKeys.WindowsNotifications, payload.RootElement, now);
            Assert.Equal("inserted", ok.Operation);
        }

        [Fact]
        public void CardSettingsPersistVisibleLimitAndClearFeedItems()
        {
            using var fixture = new Fixture();
            var now = DateTimeOffset.Parse("2026-07-13T10:00:00+00:00");
            var source = fixture.Service.EnsureSystemSource(now);

            var updated = fixture.Service.UpdateCardSettings(
                source.Card.Id,
                new CustomCardSettingsUpdateRequest
                {
                    MaxItems = 5,
                    StreamEnabled = false,
                    StreamCharDelayMs = 46
                },
                now.AddSeconds(1));

            Assert.Equal(5, updated.MaxItems);
            Assert.False(updated.StreamEnabled);
            Assert.Equal(46, updated.StreamCharDelayMs);

            for (var index = 0; index < 6; index++)
            {
                using var payload = JsonDocument.Parse($"{{\"id\":\"win-{index}\",\"text\":\"通知 {index}\"}}");
                fixture.Service.IngestSystem(CustomSourceKeys.WindowsNotifications, payload.RootElement, now.AddSeconds(index + 2));
            }

            var limited = fixture.Service.GetCardSnapshot(now.AddMinutes(1));
            var content = JsonSerializer.Serialize(limited.Cards[0].Content);
            Assert.Equal(5, JsonDocument.Parse(content).RootElement.GetProperty("items").GetArrayLength());

            var cleared = fixture.Service.ClearCard(source.Card.Id, now.AddMinutes(2));
            Assert.True(cleared.Cleared);
            var empty = fixture.Service.GetCardSnapshot(now.AddMinutes(2));
            Assert.Null(empty.Cards[0].Content);
        }

        [Fact]
        public void CorruptDatabaseIsQuarantinedAndRecreated()
        {
            var directory = Path.Combine(Path.GetTempPath(), "VibeDeckTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "custom-sources.db");
            File.WriteAllBytes(path, new byte[] { 0x56, 0x49, 0x42, 0x45, 0x44, 0x45, 0x43, 0x4b });
            try
            {
                var store = new CustomSourceStore(path);
                Assert.True(store.IsAvailable, store.InitializationError?.ToString());
                Assert.Empty(store.GetSources());
                Assert.Single(Directory.GetFiles(directory, "custom-sources.db.corrupt-*"));
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }

        [Fact]
        public void StoreReopensWithoutChangingSystemCardIdentity()
        {
            var directory = Path.Combine(Path.GetTempPath(), "VibeDeckTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "custom-sources.db");
            try
            {
                var firstStore = new CustomSourceStore(path);
                var firstService = new CustomSourceService(firstStore, new CustomSourceOptions(), new DashboardEventHub());
                var original = firstService.EnsureSystemSource(DateTimeOffset.UtcNow);

                var reopenedStore = new CustomSourceStore(path);
                var reopenedService = new CustomSourceService(reopenedStore, new CustomSourceOptions(), new DashboardEventHub());
                var reopened = reopenedService.EnsureSystemSource(DateTimeOffset.UtcNow.AddSeconds(1));

                Assert.True(reopenedStore.IsAvailable, reopenedStore.InitializationError?.ToString());
                Assert.Equal(original.Id, reopened.Id);
                Assert.Equal(original.Card.Id, reopened.Card.Id);
                Assert.Empty(Directory.GetFiles(directory, "custom-sources.db.corrupt-*"));
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string directory = Path.Combine(Path.GetTempPath(), "VibeDeckTests", Guid.NewGuid().ToString("N"));

            public Fixture()
            {
                Directory.CreateDirectory(directory);
                Store = new CustomSourceStore(Path.Combine(directory, "custom-sources.db"));
                Service = new CustomSourceService(Store, new CustomSourceOptions(), new DashboardEventHub());
            }

            public CustomSourceStore Store { get; }
            public CustomSourceService Service { get; }

            public CustomSourceCreateResponse Create(string type, int? defaultTtlSeconds = null)
            {
                return Service.Create(
                    new CustomSourceCreateRequest
                    {
                        SourceKey = type == "message-feed" ? "test-feed" : "test-status",
                        DisplayName = "Test source",
                        Card = new CustomCardCreateRequest
                        {
                            Type = type,
                            DefaultTtlSeconds = defaultTtlSeconds
                        }
                    },
                    "https://host.example/events",
                    "http://127.0.0.1:5000/events",
                    DateTimeOffset.UtcNow);
            }

            public void Dispose()
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
    }
}
