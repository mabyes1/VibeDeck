using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace VibeDeck.Host.Connect
{
    public sealed class ConnectionCodeBrokerService
    {
        private const int CodeLength = 8;
        private const int RegistrationAttempts = 4;
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private readonly HttpClient httpClient;
        private readonly string configuredBrokerUrl;

        public ConnectionCodeBrokerService(HttpClient httpClient, IConfiguration configuration)
            : this(httpClient, configuration?["ConnectionCode:BrokerUrl"] ?? Environment.GetEnvironmentVariable("VIBEDECK_CONNECTION_CODE_BROKER_URL"))
        {
        }

        internal ConnectionCodeBrokerService(HttpClient httpClient, string brokerUrlOverride)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            configuredBrokerUrl = brokerUrlOverride?.Trim() ?? string.Empty;
        }

        public async Task<ConnectionCodeIssueResult> IssueAsync(PublicEndpointConfiguration endpoint, CancellationToken cancellationToken)
        {
            if (endpoint == null || !endpoint.IsConfigured)
            {
                return ConnectionCodeIssueResult.Fail("connection_code.secure_url_required");
            }

            if (!TryGetBrokerUri(endpoint.BaseDomain, out var brokerUri))
            {
                return ConnectionCodeIssueResult.Fail("connection_code.broker_not_configured");
            }

            for (var attempt = 0; attempt < RegistrationAttempts; attempt++)
            {
                var code = CreateCode();
                using var content = new StringContent(
                    JsonSerializer.Serialize(new { code, publicUrl = endpoint.PublicUrl }),
                    Encoding.UTF8,
                    "application/json");

                try
                {
                    using var response = await httpClient.PostAsync(new Uri(brokerUri, "api/connect-codes"), content, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return ConnectionCodeIssueResult.Fail("connection_code.unavailable", brokerUri.Host);
                    }

                    var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                    var issued = JsonSerializer.Deserialize<BrokerIssueResponse>(payload, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (issued == null || !string.Equals(issued.Code, code, StringComparison.Ordinal) || issued.ExpiresAt <= DateTimeOffset.UtcNow)
                    {
                        return ConnectionCodeIssueResult.Fail("connection_code.invalid_response", brokerUri.Host);
                    }

                    return ConnectionCodeIssueResult.Success(code, brokerUri.ToString(), issued.ExpiresAt);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return ConnectionCodeIssueResult.Fail("connection_code.unavailable", brokerUri.Host);
                }
                catch (HttpRequestException)
                {
                    return ConnectionCodeIssueResult.Fail("connection_code.unavailable", brokerUri.Host);
                }
                catch (JsonException)
                {
                    return ConnectionCodeIssueResult.Fail("connection_code.invalid_response", brokerUri.Host);
                }
            }

            return ConnectionCodeIssueResult.Fail("connection_code.collision", brokerUri.Host);
        }

        internal static string CreateCode()
        {
            var characters = new char[CodeLength];
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            }

            return new string(characters);
        }

        private bool TryGetBrokerUri(string baseDomain, out Uri brokerUri)
        {
            brokerUri = null;
            var expectedHost = (baseDomain ?? string.Empty).Trim().TrimEnd('.');
            var candidate = string.IsNullOrWhiteSpace(configuredBrokerUrl)
                ? $"https://{expectedHost}/"
                : configuredBrokerUrl;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) ||
                !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(parsed.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase) ||
                parsed.Port != 443 ||
                !string.Equals(parsed.AbsolutePath, "/", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(parsed.Query) ||
                !string.IsNullOrEmpty(parsed.Fragment))
            {
                return false;
            }

            brokerUri = new Uri($"https://{parsed.IdnHost}/", UriKind.Absolute);
            return true;
        }

        private sealed class BrokerIssueResponse
        {
            public string Code { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
        }
    }

    public sealed class ConnectionCodeIssueResult
    {
        public bool IsSuccess { get; private set; }
        public string Code { get; private set; }
        public string BrokerUrl { get; private set; }
        public DateTimeOffset ExpiresAt { get; private set; }
        public string ErrorCode { get; private set; }

        public static ConnectionCodeIssueResult Success(string code, string brokerUrl, DateTimeOffset expiresAt)
        {
            return new ConnectionCodeIssueResult
            {
                IsSuccess = true,
                Code = code,
                BrokerUrl = brokerUrl,
                ExpiresAt = expiresAt
            };
        }

        public static ConnectionCodeIssueResult Fail(string errorCode, string brokerHost = "")
        {
            return new ConnectionCodeIssueResult
            {
                ErrorCode = errorCode,
                BrokerUrl = brokerHost
            };
        }
    }
}
