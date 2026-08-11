using System;
using System.Collections.Generic;

namespace VibeDeck.Host.Quotas
{
    internal static class QuotaNormalizer
    {
        internal static void FillDerivedPercentages(IEnumerable<AiQuotaStatus> providers)
        {
            if (providers == null) return;
            foreach (var provider in providers)
            {
                if (provider == null) continue;
                Fill(provider.Primary);
                Fill(provider.Secondary);
            }
        }

        private static void Fill(QuotaWindow window)
        {
            if (window == null) return;
            if (window.UsedPercent.HasValue && !window.RemainingPercent.HasValue)
            {
                window.RemainingPercent = 100d - window.UsedPercent.Value;
            }
            else if (window.RemainingPercent.HasValue && !window.UsedPercent.HasValue)
            {
                window.UsedPercent = 100d - window.RemainingPercent.Value;
            }
            window.UsedPercent = Clamp(window.UsedPercent);
            window.RemainingPercent = Clamp(window.RemainingPercent);
        }

        private static double? Clamp(double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value)) return null;
            return Math.Max(0d, Math.Min(100d, value.Value));
        }
    }
}
