using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace VibeDeck.Host.Connect
{
    public sealed class CloudflareProvisioningClient
    {
        private readonly HttpClient httpClient;
        private readonly Uri provisioningUri;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public CloudflareProvisioningClient(HttpClient httpClient, IConfiguration configuration)
            : this(
                httpClient,
                configuration?["ManagedConnector:ProvisioningUrl"]
                    ?? Environment.GetEnvironmentVariable("VIBEDECK_PROVISIONING_URL")
                    ?? "https://vibedeck.pp.ua/api/installations/provision")
        {
        }

        internal CloudflareProvisioningClient(HttpClient httpClient, string provisioningUrl)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (!Uri.TryCreate(provisioningUrl, UriKind.Absolute, out provisioningUri) ||
                !string.Equals(provisioningUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(provisioningUri.AbsolutePath, "/api/installations/provision", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(provisioningUri.Query) ||
                !string.IsNullOrEmpty(provisioningUri.Fragment))
            {
                throw new ArgumentException("Managed connector provisioning URL must be an HTTPS /api/installations/provision endpoint.", nameof(provisioningUrl));
            }
        }

        public async Task<TunnelProvisioningResult> ProvisionAsync(
            string installationId,
            string provisioningSecret,
            string productVersion,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                installationId,
                provisioningSecret,
                productVersion = productVersion ?? string.Empty,
                platform = "windows-x64"
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, provisioningUri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.UserAgent.ParseAdd($"VibeDeck.Host/{NormalizeVersion(productVersion)}");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = ReadErrorCode(responseBody);
                throw new TunnelProvisioningException(
                    string.IsNullOrWhiteSpace(code) ? "managed_connector.provisioning_failed" : code,
                    response.StatusCode);
            }

            TunnelProvisioningResult result;
            try
            {
                result = JsonSerializer.Deserialize<TunnelProvisioningResult>(responseBody, jsonOptions);
            }
            catch (JsonException error)
            {
                throw new TunnelProvisioningException("managed_connector.invalid_response", response.StatusCode, error);
            }

            if (result == null ||
                !string.Equals(result.InstallationId, installationId, StringComparison.Ordinal) ||
                !Uri.TryCreate(result.PublicUrl, UriKind.Absolute, out var publicUri) ||
                !string.Equals(publicUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(result.TunnelId) ||
                string.IsNullOrWhiteSpace(result.TunnelToken))
            {
                throw new TunnelProvisioningException("managed_connector.invalid_response", response.StatusCode);
            }

            return result;
        }

        private string ReadErrorCode(string responseBody)
        {
            try
            {
                return JsonSerializer.Deserialize<ProvisioningError>(responseBody, jsonOptions)?.Error ?? string.Empty;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string NormalizeVersion(string value)
        {
            var candidate = (value ?? "0").Trim();
            foreach (var character in candidate)
            {
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_'))
                {
                    return "0";
                }
            }
            return string.IsNullOrWhiteSpace(candidate) ? "0" : candidate;
        }

        private sealed class ProvisioningError
        {
            public string Error { get; set; }
        }
    }

    public sealed class TunnelProvisioningResult
    {
        public string InstallationId { get; set; }
        public string PublicUrl { get; set; }
        public string TunnelId { get; set; }
        public string TunnelToken { get; set; }
    }

    public sealed class TunnelProvisioningException : Exception
    {
        public TunnelProvisioningException(string code, HttpStatusCode statusCode, Exception innerException = null)
            : base(code, innerException)
        {
            Code = code ?? "managed_connector.provisioning_failed";
            StatusCode = statusCode;
        }

        public string Code { get; }
        public HttpStatusCode StatusCode { get; }
    }
}
