using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Mints short-lived Cloudflare TURN credentials. Long-lived API tokens
    /// stay inside CloudflareTurnSettingsStore; callers only receive WebRTC
    /// iceServers values that are safe to give to a paired browser.
    /// </summary>
    public sealed class CloudflareTurnCredentialService
    {
        private const string CredentialEndpointPrefix = "https://rtc.live.cloudflare.com/v1/turn/keys/";
        private const int CredentialTtlSeconds = 60 * 60;
        private static readonly string[] StunUrls = { "stun:stun.cloudflare.com:3478" };
        private readonly HttpClient httpClient;
        private readonly CloudflareTurnSettingsStore settingsStore;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public CloudflareTurnCredentialService(HttpClient httpClient, CloudflareTurnSettingsStore settingsStore)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        }

        public CloudflareTurnSettings GetSettings() => settingsStore.Get();

        public void Configure(string keyId, string apiToken) => settingsStore.Configure(keyId, apiToken);

        public void ClearSettings() => settingsStore.Clear();

        public IceServerConfiguration GetStunOnlyConfiguration()
        {
            return new IceServerConfiguration
            {
                TurnConfigured = false,
                TurnAvailable = false,
                IceServers = new List<WebRtcIceServer>
                {
                    new WebRtcIceServer { Urls = StunUrls.ToList() }
                }
            };
        }

        public async Task<IceServerConfiguration> CreateIceServersAsync(string purpose, CancellationToken cancellationToken)
        {
            var settings = settingsStore.Get();
            if (!settings.IsConfigured)
            {
                return GetStunOnlyConfiguration();
            }

            var endpoint = CredentialEndpointPrefix + Uri.EscapeDataString(settings.KeyId) + "/credentials/generate-ice-servers";
            var payload = JsonSerializer.Serialize(new
            {
                ttl = CredentialTtlSeconds,
                customIdentifier = "vibedeck-" + NormalizePurpose(purpose)
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiToken);
            request.Headers.UserAgent.ParseAdd("VibeDeck.Host/turn");

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new TurnCredentialException("turn.credentials_failed", response.StatusCode);
                }

                var parsed = JsonSerializer.Deserialize<CloudflareIceResponse>(body, jsonOptions);
                var servers = NormalizeServers(parsed?.IceServers);
                if (servers.Count == 0 || !servers.Any(server => !string.IsNullOrWhiteSpace(server.Credential)))
                {
                    throw new TurnCredentialException("turn.invalid_credentials_response", response.StatusCode);
                }

                return new IceServerConfiguration
                {
                    TurnConfigured = true,
                    TurnAvailable = true,
                    IceServers = servers
                };
            }
            catch (TurnCredentialException)
            {
                throw;
            }
            catch (Exception error) when (error is HttpRequestException || error is JsonException || error is TaskCanceledException)
            {
                throw new TurnCredentialException("turn.credentials_unavailable", null, error);
            }
        }

        private static List<WebRtcIceServer> NormalizeServers(IEnumerable<CloudflareIceServer> source)
        {
            var result = new List<WebRtcIceServer>();
            foreach (var server in source ?? Enumerable.Empty<CloudflareIceServer>())
            {
                var urls = (server.Urls ?? Array.Empty<string>())
                    .Where(IsAllowedIceUrl)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList();
                if (urls.Count == 0) continue;
                result.Add(new WebRtcIceServer
                {
                    Urls = urls,
                    Username = Safe(server.Username, 1024),
                    Credential = Safe(server.Credential, 1024)
                });
            }

            // Cloudflare returns STUN in the response. Keep an explicit STUN
            // candidate if a future response omits it, but never add port 53:
            // browsers frequently wait for that blocked port before signalling.
            if (!result.Any(server => server.Urls.Any(url => url.StartsWith("stun:", StringComparison.OrdinalIgnoreCase))))
            {
                result.Insert(0, new WebRtcIceServer { Urls = StunUrls.ToList() });
            }
            return result;
        }

        private static bool IsAllowedIceUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return false;
            var url = value.Trim();
            if (url.IndexOf(":53", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return url.StartsWith("stun:stun.cloudflare.com:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("turn:turn.cloudflare.com:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("turns:turn.cloudflare.com:", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePurpose(string purpose)
        {
            var normalized = new string((purpose ?? "session")
                .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
                .Take(72)
                .ToArray());
            return string.IsNullOrWhiteSpace(normalized) ? "session" : normalized;
        }

        private static string Safe(string value, int maximumLength)
        {
            var normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength ? normalized : normalized.Substring(0, maximumLength);
        }

        private sealed class CloudflareIceResponse
        {
            public List<CloudflareIceServer> IceServers { get; set; }
        }

        private sealed class CloudflareIceServer
        {
            public string[] Urls { get; set; }
            public string Username { get; set; }
            public string Credential { get; set; }
        }
    }

    public sealed class IceServerConfiguration
    {
        public bool TurnConfigured { get; set; }
        public bool TurnAvailable { get; set; }
        public string Warning { get; set; } = string.Empty;
        public List<WebRtcIceServer> IceServers { get; set; } = new List<WebRtcIceServer>();
    }

    public sealed class WebRtcIceServer
    {
        public List<string> Urls { get; set; } = new List<string>();
        public string Username { get; set; } = string.Empty;
        public string Credential { get; set; } = string.Empty;
    }

    public sealed class TurnCredentialException : Exception
    {
        public TurnCredentialException(string code, System.Net.HttpStatusCode? statusCode = null, Exception innerException = null)
            : base(code, innerException)
        {
            Code = code;
            StatusCode = statusCode;
        }

        public string Code { get; }
        public System.Net.HttpStatusCode? StatusCode { get; }
    }
}
