using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VibeDeck.Host.CustomSources
{
    internal static class CustomSourceValidation
    {
        private static readonly Regex ItemKeyPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static bool IsValidItemKey(string value)
        {
            return ItemKeyPattern.IsMatch(value ?? string.Empty);
        }

        internal static string NormalizeCardType(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (!CustomSourceCardTypes.IsSupported(normalized))
            {
                Problem(400, "invalid_payload", "card.type is not supported.", "card.type", "invalid");
            }
            return normalized;
        }

        internal static string RequiredText(string value, string field, int maxLength)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > maxLength)
            {
                Problem(400, "invalid_payload", $"{field} is required and must be at most {maxLength} characters.", field, "length");
            }
            return normalized;
        }

        internal static string OptionalText(string value, string field, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var normalized = value.Trim();
            if (normalized.Length > maxLength)
            {
                Problem(400, "invalid_payload", $"{field} must be at most {maxLength} characters.", field, "length");
            }
            return normalized;
        }

        internal static void ValidateLines(string value, string field)
        {
            if (value.Count(character => character == '\n') + 1 > 8)
            {
                Problem(400, "invalid_payload", $"{field} must contain at most 8 lines.", field, "lines");
            }
        }

        internal static void ValidatePosition(int position)
        {
            if (position < 0 || position > 10000)
            {
                Problem(400, "invalid_payload", "position must be between 0 and 10000.", "card.position", "range");
            }
        }

        internal static void ValidateDurations(int staleAfter, int defaultTtl)
        {
            if (!IsAllowedDuration(staleAfter) || !IsAllowedDuration(defaultTtl))
            {
                Problem(400, "invalid_payload", "durations must be 0 or between 30 and 604800 seconds.", "card", "duration");
            }
        }

        internal static void ValidateMaxItems(int maxItems)
        {
            if (maxItems < 1 || maxItems > 50)
            {
                Problem(400, "invalid_payload", "maxItems must be between 1 and 50.", "card.maxItems", "range");
            }
        }

        internal static void Problem(int statusCode, string code, string message, string field = null, string fieldValue = null)
        {
            throw new CustomSourceProblemException(
                statusCode,
                code,
                message,
                field == null ? null : new Dictionary<string, string> { [field] = fieldValue ?? "invalid" });
        }

        private static bool IsAllowedDuration(int value)
        {
            return value == 0 || value >= 30 && value <= 604800;
        }
    }
}
