using System;
using System.Collections.Generic;

namespace VibeDeck.Host.Quotas
{
    public sealed class QuotaSnapshot
    {
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<AiQuotaStatus> Providers { get; set; } = new List<AiQuotaStatus>();
    }

    public sealed class AiQuotaStatus
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Family { get; set; }
        public string AccountId { get; set; }
        public string AccountEmail { get; set; }
        public string AccountTier { get; set; }
        public bool IsActive { get; set; }
        public string State { get; set; }
        public string Source { get; set; }
        public string Detail { get; set; }
        public string Freshness { get; set; }
        public DateTimeOffset? ObservedAt { get; set; }
        public double? CreditBalance { get; set; }
        public bool? CreditUnlimited { get; set; }
        public QuotaWindow Primary { get; set; }
        public QuotaWindow Secondary { get; set; }
        internal bool UsageLoggedLocally { get; set; }
    }

    public sealed class QuotaWindow
    {
        public string Label { get; set; }
        public double? UsedPercent { get; set; }
        public double? RemainingPercent { get; set; }
        public int? WindowMinutes { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
        public bool Estimated { get; set; }
    }
}
