using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Pure, stateless helpers for reading JSON, encoding, and file enumeration.
    /// Extracted from AiQuotaService (refactor/quota-split step 1). No behavior change.
    /// </summary>
    internal static class QuotaJsonHelpers
    {
        internal static IEnumerable<string> FindJsonFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName);
        }

        internal static List<string> ReadTailLines(string path, int tailBytes)
        {
            var file = new FileInfo(path);
            var start = Math.Max(0, file.Length - tailBytes);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            return text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        internal static void TryDeleteFile(string path, ref int deleted)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        internal static int? ParseWindowMinutes(string window)
        {
            return window?.ToLowerInvariant() switch
            {
                "5h" => 300,
                "weekly" => 10080,
                _ => null
            };
        }

        internal static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        internal static string TryGetString(JsonElement element, string name)
        {
            return TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        internal static double? TryGetDouble(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed))
            {
                return parsed;
            }

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return null;
        }

        internal static bool? TryGetBoolean(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value) ||
                (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
            {
                return null;
            }

            return value.GetBoolean();
        }

        internal static int? TryGetInt(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
            {
                return parsed;
            }

            return null;
        }

        internal static DateTimeOffset? TryGetUnixTime(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }

            return null;
        }

        internal static DateTimeOffset? TryGetUnixTimeMilliseconds(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var milliseconds))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            }

            return null;
        }

        internal static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string name)
        {
            var value = TryGetString(element, name);
            if (value != null && DateTimeOffset.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        internal static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        internal static byte[] DecodeBase64Url(string value)
        {
            var padded = value
                .Replace('-', '+')
                .Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }

        internal static string SafeFileName(string value)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                builder.Append(invalid.Contains(ch) ? '_' : ch);
            }

            return builder.ToString();
        }

        internal static string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
    }
}
