using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using static VibeDeck.Host.CustomSources.CustomSourceValidation;

namespace VibeDeck.Host.CustomSources
{
    internal static class CustomSourcePayloadNormalizer
    {
        internal static CustomNormalizedPayload Normalize(CustomSourceRecord source, JsonElement root, DateTimeOffset receivedAt)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Problem(400, "invalid_payload", "The payload must be a JSON object.");
            }

            var severity = ReadOptionalString(root, "severity")?.Trim().ToLowerInvariant() ?? "info";
            if (!new[] { "info", "success", "warning", "error" }.Contains(severity, StringComparer.Ordinal))
            {
                Problem(400, "invalid_payload", "severity must be info, success, warning, or error.", "severity", "invalid");
            }

            var occurredAt = ReadTimestamp(root, "timestamp", receivedAt);
            var ttl = ReadTtl(root, source.Card.DefaultTtlSeconds);
            var expiresAt = ttl > 0 ? receivedAt.AddSeconds(ttl) : (DateTimeOffset?)null;

            switch (source.Card.Type)
            {
                case CustomSourceCardTypes.MessageFeed:
                    return NormalizeMessage(root, severity, occurredAt, receivedAt, expiresAt);
                case CustomSourceCardTypes.Status:
                    return NormalizeStatus(root, severity, occurredAt, receivedAt, expiresAt);
                case CustomSourceCardTypes.Metric:
                    return NormalizeMetric(root, severity, occurredAt, receivedAt, expiresAt);
                case CustomSourceCardTypes.KeyValue:
                    return NormalizeKeyValue(root, occurredAt, receivedAt, expiresAt);
                default:
                    Problem(503, "custom_sources_unavailable", "The custom source card type is not supported.");
                    return null;
            }
        }

        private static CustomNormalizedPayload NormalizeMessage(JsonElement root, string severity, DateTimeOffset occurredAt, DateTimeOffset receivedAt, DateTimeOffset? expiresAt)
        {
            var id = RequiredText(ReadOptionalString(root, "id"), "id", 128);
            if (!IsValidItemKey(id))
            {
                Problem(400, "invalid_payload", "id contains unsupported characters.", "id", "invalid");
            }
            var from = OptionalText(ReadOptionalString(root, "from"), "from", 80);
            var text = RequiredText(ReadOptionalString(root, "text"), "text", 2000);
            ValidateLines(text, "text");
            var content = new CustomMessageItem
            {
                Id = id,
                From = from,
                Text = text,
                Severity = severity,
                OccurredAt = CustomSourceDateTime.ToText(occurredAt),
                ReceivedAt = CustomSourceDateTime.ToText(receivedAt),
                ExpiresAt = CustomSourceDateTime.ToText(expiresAt)
            };
            return BuildPayload(id, content, occurredAt, receivedAt, expiresAt);
        }

        private static CustomNormalizedPayload NormalizeStatus(JsonElement root, string severity, DateTimeOffset occurredAt, DateTimeOffset receivedAt, DateTimeOffset? expiresAt)
        {
            var status = RequiredText(ReadOptionalString(root, "status"), "status", 120);
            var detail = OptionalText(ReadOptionalString(root, "detail"), "detail", 500);
            if (detail != null) ValidateLines(detail, "detail");
            var content = new CustomStatusContent
            {
                Status = status,
                Detail = detail,
                Severity = severity,
                OccurredAt = CustomSourceDateTime.ToText(occurredAt),
                ReceivedAt = CustomSourceDateTime.ToText(receivedAt),
                ExpiresAt = CustomSourceDateTime.ToText(expiresAt)
            };
            return BuildPayload("current", content, occurredAt, receivedAt, expiresAt);
        }

        private static CustomNormalizedPayload NormalizeMetric(JsonElement root, string severity, DateTimeOffset occurredAt, DateTimeOffset receivedAt, DateTimeOffset? expiresAt)
        {
            var value = RequiredNumber(root, "value");
            var unit = OptionalText(ReadOptionalString(root, "unit"), "unit", 16);
            var detail = OptionalText(ReadOptionalString(root, "detail"), "detail", 500);
            if (detail != null) ValidateLines(detail, "detail");
            var progress = ReadOptionalNumber(root, "progress");
            if (progress.HasValue && (progress.Value < 0 || progress.Value > 100))
            {
                Problem(400, "invalid_payload", "progress must be between 0 and 100.", "progress", "range");
            }
            var content = new CustomMetricContent
            {
                Value = value,
                Unit = unit,
                Detail = detail,
                Progress = progress,
                Severity = severity,
                OccurredAt = CustomSourceDateTime.ToText(occurredAt),
                ReceivedAt = CustomSourceDateTime.ToText(receivedAt),
                ExpiresAt = CustomSourceDateTime.ToText(expiresAt)
            };
            return BuildPayload("current", content, occurredAt, receivedAt, expiresAt);
        }

        private static CustomNormalizedPayload NormalizeKeyValue(JsonElement root, DateTimeOffset occurredAt, DateTimeOffset receivedAt, DateTimeOffset? expiresAt)
        {
            var itemsElement = ReadProperty(root, "items");
            if (!itemsElement.HasValue || itemsElement.Value.ValueKind != JsonValueKind.Array)
            {
                Problem(400, "invalid_payload", "items must be an array.", "items", "required");
            }

            var items = new List<CustomKeyValueItem>();
            foreach (var item in itemsElement.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    Problem(400, "invalid_payload", "Each item must be an object.", "items", "invalid");
                }
                var label = RequiredText(ReadOptionalString(item, "label"), "items.label", 50);
                var value = RequiredText(ReadOptionalString(item, "value"), "items.value", 200);
                items.Add(new CustomKeyValueItem { Label = label, Value = value });
            }
            if (items.Count < 1 || items.Count > 12)
            {
                Problem(400, "invalid_payload", "items must contain between 1 and 12 entries.", "items", "range");
            }

            var content = new CustomKeyValueContent
            {
                Items = items,
                OccurredAt = CustomSourceDateTime.ToText(occurredAt),
                ReceivedAt = CustomSourceDateTime.ToText(receivedAt),
                ExpiresAt = CustomSourceDateTime.ToText(expiresAt)
            };
            return BuildPayload("current", content, occurredAt, receivedAt, expiresAt);
        }

        private static CustomNormalizedPayload BuildPayload(string itemKey, object content, DateTimeOffset occurredAt, DateTimeOffset receivedAt, DateTimeOffset? expiresAt)
        {
            return new CustomNormalizedPayload
            {
                ItemKey = itemKey,
                ContentJson = JsonSerializer.Serialize(content, CustomSourceJson.Options),
                OccurredAt = occurredAt,
                ReceivedAt = receivedAt,
                ExpiresAt = expiresAt
            };
        }

        private static DateTimeOffset ReadTimestamp(JsonElement root, string name, DateTimeOffset fallback)
        {
            var raw = ReadOptionalString(root, name);
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var value = raw.Trim();
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) || !HasExplicitOffset(value))
            {
                Problem(400, "invalid_payload", $"{name} must be an ISO 8601 timestamp with timezone.", name, "timestamp");
            }
            return parsed.ToUniversalTime();
        }

        private static bool HasExplicitOffset(string value)
        {
            var separator = value.IndexOf('T');
            if (separator < 0) separator = value.IndexOf('t');
            if (separator < 0) return false;
            var time = value.Substring(separator + 1);
            return time.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(time, "[+-][0-9]{2}:[0-9]{2}$", RegexOptions.CultureInvariant);
        }

        private static int ReadTtl(JsonElement root, int defaultTtl)
        {
            var property = ReadProperty(root, "ttlSeconds");
            if (!property.HasValue) return defaultTtl;
            var ttl = 0;
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out ttl) || ttl < 30 || ttl > 604800)
            {
                Problem(400, "invalid_payload", "ttlSeconds must be between 30 and 604800.", "ttlSeconds", "range");
            }
            return ttl;
        }

        private static double RequiredNumber(JsonElement root, string name)
        {
            var property = ReadProperty(root, name);
            var value = 0d;
            if (!property.HasValue || property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                Problem(400, "invalid_payload", $"{name} must be a finite JSON number.", name, "number");
            }
            return value;
        }

        private static double? ReadOptionalNumber(JsonElement root, string name)
        {
            var property = ReadProperty(root, name);
            if (!property.HasValue) return null;
            var value = 0d;
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                Problem(400, "invalid_payload", $"{name} must be a finite JSON number.", name, "number");
            }
            return value;
        }

        private static string ReadOptionalString(JsonElement root, string name)
        {
            var property = ReadProperty(root, name);
            if (!property.HasValue || property.Value.ValueKind == JsonValueKind.Null) return null;
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                Problem(400, "invalid_payload", $"{name} must be a string.", name, "string");
            }
            return property.Value.GetString();
        }

        private static JsonElement? ReadProperty(JsonElement root, string name)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            }
            return null;
        }
    }
}
