using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static VibeDeck.Host.Quotas.QuotaPaths;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Reads Codex quota from the passive local CLI stream and, on explicit refresh,
    /// from the account usage endpoint using a stored Codex profile. VibeDeck never
    /// refreshes or rotates Codex credentials and never exposes tokens to clients.
    /// </summary>
    internal sealed class CodexQuotaProvider : IQuotaProvider
    {
        private const string DefaultUsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
        private const string UsageEndpointEnvVar = "VIBEDECK_CODEX_USAGE_ENDPOINT";
        private const string DefaultUserAgent = "VibeDeck/1.0";
        private const string UserAgentEnvVar = "VIBEDECK_CODEX_USER_AGENT";

        private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(4, 4);

        internal sealed class CodexRefreshResult
        {
            public bool Success { get; set; }
            public string AccountId { get; set; }
            public string Email { get; set; }
            public string Code { get; set; }
            public string Message { get; set; }
            public AiQuotaStatus Status { get; set; }
        }

        public string Id => "codex";
        public string Label => "Codex";

        public async Task<IReadOnlyList<AiQuotaStatus>> ReadAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (forceRefresh)
            {
                await RefreshStoredProfilesAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var statuses = CodexQuotaReader.ReadCodexQuotas().ToList();
            foreach (var status in statuses)
            {
                status.UsageLoggedLocally = true;
            }
            QuotaFreshness.MarkSourceHealth(statuses, null, DateTimeOffset.UtcNow);
            return statuses;
        }

        internal async Task<CodexRefreshResult> RefreshAccountAsync(
            string accountId,
            string email,
            CancellationToken cancellationToken)
        {
            var cached = CodexQuotaReader.ReadCachedQuotaForAccount(accountId, email);
            var credentials = CodexAccountStore.ReadCredentials(accountId, email);
            if (credentials == null)
            {
                return Failure(
                    accountId,
                    email,
                    "quota.codex_profile_credentials_unavailable",
                    "This Host does not have a saved Codex credential for that account.",
                    cached);
            }

            await refreshGate.WaitAsync(cancellationToken);
            try
            {
                var usage = await FetchUsageAsync(credentials, cancellationToken);
                var observedAt = DateTimeOffset.UtcNow;
                var status = CodexQuotaReader.BuildCodexQuotaFromUsage(
                    usage,
                    credentials,
                    observedAt,
                    UsageEndpoint());
                if (status == null)
                {
                    return Failure(
                        credentials.AccountId,
                        credentials.Email,
                        "quota.codex_usage_shape_unrecognized",
                        "Codex returned usage data in an unsupported format.",
                        cached);
                }

                CodexQuotaReader.WriteCodexQuotaCache(CodexQuotaCacheDirectory(), status);
                return new CodexRefreshResult
                {
                    Success = true,
                    AccountId = status.AccountId,
                    Email = status.AccountEmail,
                    Message = "Codex quota refreshed.",
                    Status = status
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CodexUsageException ex)
            {
                return Failure(credentials.AccountId, credentials.Email, ex.Code, ex.Message, cached);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is InvalidOperationException || ex is OperationCanceledException)
            {
                return Failure(
                    credentials.AccountId,
                    credentials.Email,
                    "quota.codex_usage_unavailable",
                    ex is OperationCanceledException
                        ? "The Codex usage endpoint did not answer in time."
                        : "The Codex usage endpoint could not be read.",
                    cached);
            }
            finally
            {
                refreshGate.Release();
            }
        }

        private async Task RefreshStoredProfilesAsync(CancellationToken cancellationToken)
        {
            var profiles = CodexAccountStore.ListProfiles()
                .Where(profile => !string.IsNullOrWhiteSpace(profile.AccountId) || !string.IsNullOrWhiteSpace(profile.Email))
                .GroupBy(profile => profile.AccountId ?? profile.Email, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (!profiles.Any())
            {
                return;
            }

            await Task.WhenAll(profiles.Select(profile =>
                RefreshAccountAsync(profile.AccountId, profile.Email, cancellationToken)));
        }

        private static async Task<JsonElement> FetchUsageAsync(
            CodexAccountStore.CodexCredentials credentials,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            if (!string.IsNullOrWhiteSpace(credentials.AccountId))
            {
                request.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", credentials.AccountId);
            }
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var expired = response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden;
                throw new CodexUsageException(
                    expired ? "quota.codex_profile_credentials_expired" : "quota.codex_usage_http_error",
                    expired
                        ? "This Host's saved Codex credential can no longer query that account."
                        : $"The Codex usage endpoint returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }

        private static string UsageEndpoint()
        {
            var configured = Environment.GetEnvironmentVariable(UsageEndpointEnvVar);
            return string.IsNullOrWhiteSpace(configured) ? DefaultUsageEndpoint : configured;
        }

        private static string UserAgent()
        {
            var configured = Environment.GetEnvironmentVariable(UserAgentEnvVar);
            return string.IsNullOrWhiteSpace(configured) ? DefaultUserAgent : configured;
        }

        private static CodexRefreshResult Failure(
            string accountId,
            string email,
            string code,
            string message,
            AiQuotaStatus cached)
        {
            return new CodexRefreshResult
            {
                Success = false,
                AccountId = accountId,
                Email = email,
                Code = code,
                Message = message,
                Status = cached
            };
        }

        private sealed class CodexUsageException : Exception
        {
            internal CodexUsageException(string code, string message) : base(message)
            {
                Code = code;
            }

            internal string Code { get; }
        }
    }
}
