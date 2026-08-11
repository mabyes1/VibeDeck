using System;
using System.Collections.Generic;

namespace VibeDeck.Host.Quotas
{
    internal static class QuotaFreshness
    {
        private const double MaxRolloverSteps = 100000d;
        internal const string Live = "live";
        internal const string Stale = "stale";

        internal static void MarkSourceHealth(IEnumerable<AiQuotaStatus> statuses, TimeSpan? refreshInterval, DateTimeOffset now)
        {
            if (statuses == null) return;
            foreach (var status in statuses)
            {
                if (status == null || !string.Equals(status.State, "ok", StringComparison.OrdinalIgnoreCase)) continue;
                if (!refreshInterval.HasValue)
                {
                    status.Freshness = Live;
                    continue;
                }
                var cutoff = now - refreshInterval.Value;
                status.Freshness = status.ObservedAt.HasValue && status.ObservedAt.Value >= cutoff ? Live : Stale;
            }
        }

        internal static void RollOverElapsedWindows(IEnumerable<AiQuotaStatus> providers, DateTimeOffset now)
        {
            if (providers == null) return;
            foreach (var provider in providers)
            {
                if (provider == null || !provider.UsageLoggedLocally) continue;
                RollOver(provider.Primary, now);
                RollOver(provider.Secondary, now);
            }
        }

        private static void RollOver(QuotaWindow window, DateTimeOffset now)
        {
            if (window?.ResetsAt == null || window.ResetsAt.Value > now) return;
            if (window.UsedPercent.HasValue) window.UsedPercent = 0d;
            if (window.RemainingPercent.HasValue) window.RemainingPercent = 100d;
            window.ResetsAt = NextBoundary(window.ResetsAt.Value, window.WindowMinutes, now);
            window.Estimated = true;
        }

        private static DateTimeOffset NextBoundary(DateTimeOffset resetsAt, int? windowMinutes, DateTimeOffset now)
        {
            if (!windowMinutes.HasValue || windowMinutes.Value <= 0) return resetsAt;
            var elapsedMinutes = (now - resetsAt).TotalMinutes;
            var steps = Math.Floor(elapsedMinutes / windowMinutes.Value) + 1d;
            if (steps <= 0d || steps > MaxRolloverSteps) return now.AddMinutes(windowMinutes.Value);
            return resetsAt.AddMinutes(steps * windowMinutes.Value);
        }
    }
}
