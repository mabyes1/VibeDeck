using System;
using System.Collections.Generic;
using System.IO;
using VibeDeck.Host.Diagnostics;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class AuditTrailServiceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "VibeDeckAuditTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void RecordsTraceableEntriesWithoutPersistingSecrets()
        {
            var service = new AuditTrailService(directory);
            service.Record(
                "information",
                "pairing",
                "approve",
                "completed",
                "trace-12345678",
                "127.0.0.1",
                "BOOX",
                new Dictionary<string, string>
                {
                    ["deviceToken"] = "top-secret-token",
                    ["attempt"] = "1"
                });
            service.RecordException(
                "browser",
                "client-network",
                new InvalidOperationException("token=top-secret-token"),
                "trace-12345678");

            var result = service.ReadRecent(20, "trace-12345678");
            var serialized = File.ReadAllText(Directory.GetFiles(directory, "audit-*.jsonl")[0]);

            Assert.Equal(2, result.Entries.Count);
            Assert.All(result.Entries, entry => Assert.Equal("trace-12345678", entry.TraceId));
            Assert.Contains(result.Entries, entry =>
                entry.Details.TryGetValue("deviceToken", out var value) && value == "[redacted]");
            Assert.DoesNotContain("top-secret-token", serialized, StringComparison.Ordinal);
            Assert.Contains("[redacted]", serialized, StringComparison.Ordinal);
        }

        [Fact]
        public void HidesRoutineTelemetryByDefaultButKeepsItAvailable()
        {
            var service = new AuditTrailService(directory);
            service.Record(
                "information",
                "http",
                "POST /api/windows-notifications/companion/heartbeat",
                "completed",
                "trace-10000001");
            service.Record(
                "information",
                "dashboard-layout",
                "save",
                "persisted",
                "trace-10000002");

            var actionable = service.ReadRecent(10);
            var complete = service.ReadRecent(10, includeRoutine: true);

            Assert.Single(actionable.Entries);
            Assert.Equal("save", actionable.Entries[0].Action);
            Assert.Equal(1, actionable.OmittedRoutineEntries);
            Assert.Equal(2, complete.Entries.Count);
        }

        public void Dispose()
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}
