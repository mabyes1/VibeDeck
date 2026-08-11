using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeDeck.Host.CustomSources
{
    public static class CustomSourceCardTypes
    {
        public const string MessageFeed = "message-feed";
        public const string Status = "status";
        public const string Metric = "metric";
        public const string KeyValue = "key-value";

        public static bool IsSupported(string value)
        {
            return string.Equals(value, MessageFeed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, Status, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, Metric, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, KeyValue, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFeed(string value) => string.Equals(value, MessageFeed, StringComparison.Ordinal);
    }

    public static class CustomSourceKeys
    {
        public const string WindowsNotifications = "windows-notifications";
    }

    public static class CustomCardSettingsDefaults
    {
        public const int MinVisibleItems = 5;
        public const int MaxVisibleItems = 30;
        public const bool StreamEnabled = true;
        public const int StreamCharDelayMs = 28;
        public const int MinStreamCharDelayMs = 10;
        public const int MaxStreamCharDelayMs = 100;
    }

    public static class CustomSourceJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            MaxDepth = 8,
            WriteIndented = false
        };

        public static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions
        {
            MaxDepth = 8,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        };
    }

    public sealed class CustomSourceOptions
    {
        public bool AllowInsecureLan { get; set; }
        public int MaxPayloadBytes { get; set; } = 65536;
        public double RequestsPerSecond { get; set; } = 10;
        public int Burst { get; set; } = 30;
        public int CleanupIntervalSeconds { get; set; } = 30;
        public int MaxSources { get; set; } = 50;

        public void Normalize()
        {
            MaxPayloadBytes = Math.Max(4096, Math.Min(1024 * 1024, MaxPayloadBytes));
            RequestsPerSecond = Math.Max(0.1, Math.Min(1000, RequestsPerSecond));
            Burst = Math.Max(1, Math.Min(10000, Burst));
            CleanupIntervalSeconds = Math.Max(5, Math.Min(3600, CleanupIntervalSeconds));
            MaxSources = Math.Max(1, Math.Min(500, MaxSources));
        }
    }

    public sealed class CustomSourceRecord
    {
        public string Id { get; set; }
        public string SourceKey { get; set; }
        public string DisplayName { get; set; }
        public string TokenHash { get; set; }
        public bool Enabled { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? LastReceivedAt { get; set; }
        public CustomCardRecord Card { get; set; }
    }

    public sealed class CustomCardRecord
    {
        public string Id { get; set; }
        public string SourceId { get; set; }
        public string CardKey { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public int Position { get; set; }
        public int StaleAfterSeconds { get; set; }
        public int DefaultTtlSeconds { get; set; }
        public int MaxItems { get; set; }
        public long Revision { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public sealed class CustomSourceCreateRequest
    {
        public string SourceKey { get; set; }
        public string DisplayName { get; set; }
        public CustomCardCreateRequest Card { get; set; }
    }

    public sealed class CustomCardCreateRequest
    {
        public string Title { get; set; }
        public string Type { get; set; }
        public int? Position { get; set; }
        public int? StaleAfterSeconds { get; set; }
        public int? DefaultTtlSeconds { get; set; }
        public int? MaxItems { get; set; }
    }

    public sealed class CustomSourceUpdateRequest
    {
        public string DisplayName { get; set; }
        public bool? Enabled { get; set; }
        public CustomCardUpdateRequest Card { get; set; }
        public string SourceKey { get; set; }
        public string CardId { get; set; }
        public string CardKey { get; set; }
        public string Type { get; set; }
    }

    public sealed class CustomCardUpdateRequest
    {
        public string Title { get; set; }
        public string Type { get; set; }
        public string CardId { get; set; }
        public string CardKey { get; set; }
        public int? Position { get; set; }
        public int? StaleAfterSeconds { get; set; }
        public int? DefaultTtlSeconds { get; set; }
        public int? MaxItems { get; set; }
    }

    public sealed class CustomCardSettingsUpdateRequest
    {
        public int? MaxItems { get; set; }
        public bool? StreamEnabled { get; set; }
        public int? StreamCharDelayMs { get; set; }
    }

    public sealed class CustomCardSettingsRecord
    {
        public string CardId { get; set; }
        public bool StreamEnabled { get; set; }
        public int StreamCharDelayMs { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public sealed class CustomCardSettingsResponse
    {
        public string CardId { get; set; }
        public string SourceKey { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public int MaxItems { get; set; }
        public bool StreamEnabled { get; set; }
        public int StreamCharDelayMs { get; set; }
        public string UpdatedAt { get; set; }
    }

    public sealed class CustomSourceManagementView
    {
        public string SourceKey { get; set; }
        public string DisplayName { get; set; }
        public bool Enabled { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public string LastReceivedAt { get; set; }
        public int ItemCount { get; set; }
        public string Health { get; set; }
        public CustomCardManagementView Card { get; set; }
    }

    public sealed class CustomCardManagementView
    {
        public string CardId { get; set; }
        public string CardKey { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public int Position { get; set; }
        public int StaleAfterSeconds { get; set; }
        public int DefaultTtlSeconds { get; set; }
        public int MaxItems { get; set; }
        public long Revision { get; set; }
    }

    public sealed class CustomSourceCreateResponse
    {
        public CustomSourceManagementView Source { get; set; }
        public CustomSourceIngestInfo Ingest { get; set; }
    }

    public sealed class CustomSourceIngestInfo
    {
        public string EndpointPath { get; set; }
        public string EndpointUrl { get; set; }
        public string LocalEndpointUrl { get; set; }
        public string Token { get; set; }
    }

    public sealed class CustomSourcesResponse
    {
        public IReadOnlyList<CustomSourceManagementView> Sources { get; set; }
    }

    public sealed class CustomCardSnapshotResponse
    {
        public string CardId { get; set; }
        public string SourceKey { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public int Position { get; set; }
        public long Revision { get; set; }
        public string Freshness { get; set; }
        public string LastReceivedAt { get; set; }
        public int MaxItems { get; set; }
        public bool StreamEnabled { get; set; }
        public int StreamCharDelayMs { get; set; }
        public object Content { get; set; }
    }

    public sealed class CustomCardsResponse
    {
        public string GeneratedAt { get; set; }
        public IReadOnlyList<CustomCardSnapshotResponse> Cards { get; set; }
    }

    public sealed class CustomIngestResult
    {
        public string SourceKey { get; set; }
        public string CardId { get; set; }
        public long Revision { get; set; }
        public string ReceivedAt { get; set; }
        public string Operation { get; set; }
    }

    public sealed class CustomDeleteResult
    {
        public bool Deleted { get; set; }
        public string SourceKey { get; set; }
        public string CardId { get; set; }
        public long Revision { get; set; }
    }

    public sealed class CustomClearResult
    {
        public bool Cleared { get; set; }
        public string SourceKey { get; set; }
        public string CardId { get; set; }
        public long Revision { get; set; }
    }

    public sealed class CustomCardChange
    {
        public string SourceKey { get; set; }
        public string CardId { get; set; }
        public long Revision { get; set; }
        public string Reason { get; set; }
    }

    public sealed class CustomNormalizedPayload
    {
        public string ItemKey { get; set; }
        public string ContentJson { get; set; }
        public DateTimeOffset? OccurredAt { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    public sealed class CustomErrorResponse
    {
        public CustomError Error { get; set; }
    }

    public sealed class CustomError
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public IReadOnlyDictionary<string, string> Fields { get; set; }
    }

    public sealed class CustomSourceProblemException : Exception
    {
        public int StatusCode { get; }
        public string Code { get; }
        public IReadOnlyDictionary<string, string> Fields { get; }

        public CustomSourceProblemException(int statusCode, string code, string message, IReadOnlyDictionary<string, string> fields = null)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code;
            Fields = fields;
        }
    }

    public sealed class CustomSourceStoreUnavailableException : Exception
    {
        public CustomSourceStoreUnavailableException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    public static class CustomSourceDateTime
    {
        public static string ToText(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
        public static string ToText(DateTimeOffset? value) => value.HasValue ? ToText(value.Value) : null;
    }

    public sealed class CustomMessageItem
    {
        public string Id { get; set; }
        public string From { get; set; }
        public string Text { get; set; }
        public string Severity { get; set; }
        public string OccurredAt { get; set; }
        public string ReceivedAt { get; set; }
        public string ExpiresAt { get; set; }
    }

    public sealed class CustomStatusContent
    {
        public string Status { get; set; }
        public string Detail { get; set; }
        public string Severity { get; set; }
        public string OccurredAt { get; set; }
        public string ReceivedAt { get; set; }
        public string ExpiresAt { get; set; }
    }

    public sealed class CustomMetricContent
    {
        public double Value { get; set; }
        public string Unit { get; set; }
        public string Detail { get; set; }
        public double? Progress { get; set; }
        public string Severity { get; set; }
        public string OccurredAt { get; set; }
        public string ReceivedAt { get; set; }
        public string ExpiresAt { get; set; }
    }

    public sealed class CustomKeyValueContent
    {
        public IReadOnlyList<CustomKeyValueItem> Items { get; set; }
        public string OccurredAt { get; set; }
        public string ReceivedAt { get; set; }
        public string ExpiresAt { get; set; }
    }

    public sealed class CustomKeyValueItem
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }
}
