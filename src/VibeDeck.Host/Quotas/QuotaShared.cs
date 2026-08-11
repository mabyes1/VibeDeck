using System;
using System.Text.Json;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Cross-provider shared quota bits (JSON options + unavailable-status factory).
    /// Extracted from AiQuotaService (refactor/quota-split step 2). No behavior change.
    /// </summary>
    internal static class QuotaShared
    {
        internal static readonly JsonSerializerOptions CacheJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        internal static AiQuotaStatus Unavailable(string id, string label, string detail, string source)
        {
            return new AiQuotaStatus
            {
                Id = id,
                Label = label,
                Family = id.StartsWith("agy", StringComparison.OrdinalIgnoreCase)
                    ? "agy"
                    : id.StartsWith("claude", StringComparison.OrdinalIgnoreCase)
                        ? "claude-code"
                        : id,
                State = "unavailable",
                Source = source,
                Detail = detail
            };
        }
    }
}
