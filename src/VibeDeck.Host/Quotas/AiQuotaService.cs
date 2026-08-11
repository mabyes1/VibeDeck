using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Composes provider-specific quota readers and exposes the quota actions used
    /// by the HTTP layer. Provider implementation lives in the focused modules.
    /// </summary>
    public sealed class AiQuotaService
    {
        private readonly AgyQuotaService agy = new AgyQuotaService();
        private readonly CodexQuotaProvider codex = new CodexQuotaProvider();
        private readonly ClaudeQuotaProvider claude = new ClaudeQuotaProvider();
        private readonly IReadOnlyList<IQuotaProvider> providers;

        public AiQuotaService()
        {
            providers = new IQuotaProvider[]
            {
                codex,
                agy,
                claude
            };
        }

        public Task<QuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            return BuildSnapshotAsync(false, cancellationToken);
        }

        public Task<QuotaSnapshot> RefreshSnapshotAsync(CancellationToken cancellationToken)
        {
            return BuildSnapshotAsync(true, cancellationToken);
        }

        private async Task<QuotaSnapshot> BuildSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            var snapshot = new QuotaSnapshot
            {
                Providers = new List<AiQuotaStatus>()
            };

            var reads = providers
                .Select(provider => ReadProviderAsync(provider, forceRefresh, cancellationToken))
                .ToArray();
            var results = await Task.WhenAll(reads);
            foreach (var statuses in results)
            {
                snapshot.Providers.AddRange(statuses);
            }

            QuotaFreshness.RollOverElapsedWindows(snapshot.Providers, DateTimeOffset.UtcNow);
            QuotaNormalizer.FillDerivedPercentages(snapshot.Providers);
            return snapshot;
        }

        private static async Task<IReadOnlyList<AiQuotaStatus>> ReadProviderAsync(
            IQuotaProvider provider,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            try
            {
                return await provider.ReadAsync(forceRefresh, cancellationToken) ?? Array.Empty<AiQuotaStatus>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var reason = ex is OperationCanceledException ? "the source timed out" : ex.Message;
                if (reason != null && reason.Length > 200) reason = reason.Substring(0, 200);
                return new[]
                {
                    QuotaShared.Unavailable(
                        provider.Id,
                        provider.Label,
                        $"{provider.Label} quota could not be read: {reason}",
                        string.Empty)
                };
            }
        }

        public AgyQuotaService.AgyImportResult ImportAgyAccountsFromAntigravity()
        {
            return agy.ImportAgyAccountsFromAntigravity();
        }

        public AgyQuotaService.AgyOAuthStartResult StartAgyOAuth(string redirectUri, bool openBrowser)
        {
            return agy.StartAgyOAuth(redirectUri, openBrowser);
        }

        public Task<AgyQuotaService.AgyOAuthCallbackResult> CompleteAgyOAuthAsync(
            string state,
            string code,
            string error,
            string errorDescription,
            CancellationToken cancellationToken)
        {
            return agy.CompleteAgyOAuthAsync(state, code, error, errorDescription, cancellationToken);
        }

        public AgyQuotaService.AgyAccountActionResult OpenAgyCli(bool openWindow = true)
        {
            return agy.OpenAgyCli(openWindow);
        }

        public AgyQuotaService.AgyAccountActionResult DeleteAgyAccount(string accountId, string email)
        {
            return agy.DeleteAgyAccount(accountId, email);
        }

        public AgyQuotaService.AgyAccountActionResult DeleteCodexAccount(string accountId, string email)
        {
            return agy.DeleteCodexAccount(accountId, email);
        }

        public AgyQuotaService.AgyAccountActionResult DeleteClaudeAccount(string accountId, string email)
        {
            return claude.DeleteAccount(accountId, email);
        }

        internal IReadOnlyList<CodexAccountStore.CodexProfile> ListCodexProfiles()
        {
            return CodexAccountStore.ListProfiles();
        }

        internal Task<CodexQuotaProvider.CodexRefreshResult> RefreshCodexAccountAsync(
            string accountId,
            string email,
            CancellationToken cancellationToken)
        {
            return codex.RefreshAccountAsync(accountId, email, cancellationToken);
        }

        internal CodexAccountStore.CodexActionResult SwitchCodexAccount(string accountId, string email)
        {
            return CodexAccountStore.SwitchTo(accountId, email);
        }

        internal CodexAccountStore.CodexActionResult StartCodexQuotaReAuth(string accountId, string email)
        {
            return CodexAccountStore.StartQuotaReAuth(accountId, email);
        }

        internal CodexAccountStore.CodexActionResult PollCodexQuotaReAuth(string sessionId)
        {
            return CodexAccountStore.PollQuotaReAuth(sessionId);
        }

        internal CodexAccountStore.CodexActionResult DeleteCodexProfile(string accountId, string email)
        {
            return CodexAccountStore.DeleteProfile(accountId, email);
        }
    }
}
