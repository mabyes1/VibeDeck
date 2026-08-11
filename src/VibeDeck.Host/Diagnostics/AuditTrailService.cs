using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VibeDeck.Host.Diagnostics
{
    public sealed class AuditTrailService
    {
        public const string TraceIdItemKey = "VibeDeck.AuditTraceId";

        private const int RetentionDays = 30;
        private const long MaximumFileBytes = 4 * 1024 * 1024;
        private static readonly Regex SensitiveValuePattern = new Regex(
            @"(?i)(\b(?:access[_-]?token|device[_-]?token|token|secret|password|authorization)\b\s*(?:=|:)\s*)([^&\s,;]+)",
            RegexOptions.Compiled);
        private readonly object gate = new object();
        private readonly string directory;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        private DateTime lastPrunedDate = DateTime.MinValue;
        private string lastStorageError = "";

        public AuditTrailService()
            : this(AppPaths.EnsureDirectory(AppPaths.LogsDirectory))
        {
        }

        public AuditTrailService(string directoryOverride)
        {
            if (string.IsNullOrWhiteSpace(directoryOverride))
            {
                throw new ArgumentException("Audit directory is required.", nameof(directoryOverride));
            }

            directory = Path.GetFullPath(directoryOverride);
            Directory.CreateDirectory(directory);
        }

        public static string CreateTraceId(string supplied = null)
        {
            var candidate = (supplied ?? "").Trim();
            if (candidate.Length >= 8 && candidate.Length <= 64 &&
                candidate.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_'))
            {
                return candidate;
            }

            return Guid.NewGuid().ToString("N").Substring(0, 16);
        }

        public void Record(
            string severity,
            string category,
            string action,
            string outcome,
            string traceId = "",
            string remoteAddress = "",
            string subject = "",
            IDictionary<string, string> details = null)
        {
            var timestamp = DateTimeOffset.UtcNow;
            var entry = new AuditTrailEntry
            {
                Timestamp = timestamp.ToString("O", CultureInfo.InvariantCulture),
                TraceId = CreateTraceId(traceId),
                Severity = NormalizeSeverity(severity),
                Category = Safe(category, 80),
                Action = Safe(action, 120),
                Outcome = Safe(outcome, 120),
                RemoteAddress = Safe(remoteAddress, 120),
                Subject = Safe(subject, 160),
                Details = SafeDetails(details)
            };

            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(directory);
                    Prune(timestamp);
                    var path = ResolveWritePath(timestamp);
                    File.AppendAllText(
                        path,
                        JsonSerializer.Serialize(entry, jsonOptions) + Environment.NewLine,
                        new UTF8Encoding(false));
                    lastStorageError = "";
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                lock (gate)
                {
                    lastStorageError = Safe(error.Message, 240);
                }
            }
        }

        public void RecordException(
            string category,
            string action,
            Exception error,
            string traceId = "",
            string remoteAddress = "",
            string subject = "",
            IDictionary<string, string> details = null)
        {
            var safeDetails = details == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(details, StringComparer.OrdinalIgnoreCase);
            if (error != null)
            {
                safeDetails["errorType"] = error.GetType().Name;
                safeDetails["errorMessage"] = error.Message;
            }

            Record("error", category, action, "failed", traceId, remoteAddress, subject, safeDetails);
        }

        public AuditTrailReadResult ReadRecent(int limit = 80, string traceId = "", bool includeRoutine = false)
        {
            var normalizedLimit = Math.Max(1, Math.Min(300, limit));
            var normalizedTraceId = (traceId ?? "").Trim();
            var entries = new List<AuditTrailEntry>();
            var omittedRoutineEntries = 0;
            string storageError;

            lock (gate)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        foreach (var path in Directory.GetFiles(directory, "audit-*.jsonl")
                            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                        {
                            foreach (var line in File.ReadLines(path).Reverse())
                            {
                                if (entries.Count >= normalizedLimit) break;
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                try
                                {
                                    var entry = JsonSerializer.Deserialize<AuditTrailEntry>(line, jsonOptions);
                                    if (entry == null) continue;
                                    if (!string.IsNullOrWhiteSpace(normalizedTraceId) &&
                                        !string.Equals(entry.TraceId, normalizedTraceId, StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }
                                    if (!includeRoutine && string.IsNullOrWhiteSpace(normalizedTraceId) && IsRoutineEntry(entry))
                                    {
                                        omittedRoutineEntries++;
                                        continue;
                                    }
                                    entries.Add(entry);
                                }
                                catch (JsonException)
                                {
                                }
                            }

                            if (entries.Count >= normalizedLimit) break;
                        }
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    lastStorageError = Safe(error.Message, 240);
                }

                storageError = lastStorageError;
            }

            var ordered = entries
                .OrderByDescending(entry => entry.Timestamp, StringComparer.Ordinal)
                .Take(normalizedLimit)
                .ToArray();
            return new AuditTrailReadResult
            {
                GeneratedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                RetentionDays = RetentionDays,
                StorageError = storageError,
                OmittedRoutineEntries = omittedRoutineEntries,
                Entries = ordered
            };
        }

        private string ResolveWritePath(DateTimeOffset timestamp)
        {
            var date = timestamp.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (var sequence = 0; sequence < 100; sequence++)
            {
                var suffix = sequence == 0 ? "" : $"-{sequence}";
                var path = Path.Combine(directory, $"audit-{date}{suffix}.jsonl");
                if (!File.Exists(path) || new FileInfo(path).Length < MaximumFileBytes)
                {
                    return path;
                }
            }

            return Path.Combine(directory, $"audit-{date}-{Guid.NewGuid():N}.jsonl");
        }

        private void Prune(DateTimeOffset timestamp)
        {
            var currentDate = timestamp.UtcDateTime.Date;
            if (lastPrunedDate == currentDate) return;
            lastPrunedDate = currentDate;
            var cutoff = currentDate.AddDays(-RetentionDays);
            foreach (var path in Directory.GetFiles(directory, "audit-*.jsonl"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff)
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static Dictionary<string, string> SafeDetails(IDictionary<string, string> details)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (details == null) return result;

            foreach (var pair in details.Take(24))
            {
                var key = Safe(pair.Key, 48);
                if (string.IsNullOrWhiteSpace(key)) continue;
                var sensitive = key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0;
                result[key] = sensitive ? "[redacted]" : Safe(pair.Value, 480);
            }

            return result;
        }

        private static string NormalizeSeverity(string severity)
        {
            var value = (severity ?? "").Trim().ToLowerInvariant();
            return value == "error" || value == "warning" || value == "information"
                ? value
                : "information";
        }

        private static bool IsRoutineEntry(AuditTrailEntry entry)
        {
            if (!string.Equals(entry?.Severity, "information", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry?.Outcome, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var action = entry.Action ?? "";
            return action.Equals("POST /api/windows-notifications/companion/heartbeat", StringComparison.OrdinalIgnoreCase) ||
                action.Equals("GET /api/sideboard/stats", StringComparison.OrdinalIgnoreCase) ||
                action.Equals("GET /api/sideboard/work-pulse", StringComparison.OrdinalIgnoreCase) ||
                action.Equals("GET /api/devices/status", StringComparison.OrdinalIgnoreCase);
        }

        private static string Safe(string value, int maximumLength)
        {
            var normalized = (value ?? "")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            normalized = SensitiveValuePattern.Replace(normalized, "$1[redacted]");
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength) + "…";
        }
    }

    public sealed class AuditTrailEntry
    {
        public string Timestamp { get; set; }
        public string TraceId { get; set; }
        public string Severity { get; set; }
        public string Category { get; set; }
        public string Action { get; set; }
        public string Outcome { get; set; }
        public string RemoteAddress { get; set; }
        public string Subject { get; set; }
        public Dictionary<string, string> Details { get; set; }
    }

    public sealed class AuditTrailReadResult
    {
        public string GeneratedAt { get; set; }
        public int RetentionDays { get; set; }
        public string StorageError { get; set; }
        public int OmittedRoutineEntries { get; set; }
        public IReadOnlyList<AuditTrailEntry> Entries { get; set; }
    }
}
