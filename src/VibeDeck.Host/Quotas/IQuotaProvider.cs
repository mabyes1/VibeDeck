using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// One isolated source of AI quota. A provider failure is degraded on its own
    /// so the remaining sources can still be returned to the dashboard.
    /// </summary>
    internal interface IQuotaProvider
    {
        string Id { get; }
        string Label { get; }
        Task<IReadOnlyList<AiQuotaStatus>> ReadAsync(bool forceRefresh, CancellationToken cancellationToken);
    }
}
