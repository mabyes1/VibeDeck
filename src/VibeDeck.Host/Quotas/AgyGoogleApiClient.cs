using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static VibeDeck.Host.Quotas.QuotaJsonHelpers;
using static VibeDeck.Host.Quotas.QuotaPaths;
using static VibeDeck.Host.Quotas.QuotaShared;

namespace VibeDeck.Host.Quotas
{
    internal sealed class AgyGoogleApiClient
    {
        private const string ClientIdEnv = "AGY_GOOGLE_CLIENT_ID";
        private const string ClientSecretEnv = "AGY_GOOGLE_CLIENT_SECRET";
        private const string UserAgent = "antigravity/1.20.5 windows/amd64 google-api-nodejs-client/10.3.0";
        private const string OAuthScope = "openid email https://www.googleapis.com/auth/cloud-platform";
        private static readonly string[] QuotaApiBases =
        {
            "https://daily-cloudcode-pa.googleapis.com",
            "https://cloudcode-pa.googleapis.com"
        };

        private readonly HttpClient httpClient;
        private readonly Lazy<AgyGoogleOAuthClientConfig> oauthClient;

        internal AgyGoogleApiClient(HttpClient httpClient = null, Func<AgyGoogleOAuthClientConfig> configLoader = null)
        {
            this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            oauthClient = new Lazy<AgyGoogleOAuthClientConfig>(configLoader ?? LoadDefaultConfig);
        }

        internal bool TryGetOAuthClient(out AgyGoogleOAuthClientConfig config, out string error)
        {
            config = oauthClient.Value;
            if (config != null &&
                !string.IsNullOrWhiteSpace(config.ClientId) &&
                !string.IsNullOrWhiteSpace(config.ClientSecret))
            {
                error = null;
                return true;
            }

            error =
                "AGY Google OAuth is not configured. Set environment variables " +
                ClientIdEnv + " and " + ClientSecretEnv +
                ", or create the Host secrets folder (agy-google-oauth.json) " +
                "with clientId and clientSecret.";
            config = null;
            return false;
        }

        internal string BuildAuthorizationUrl(
            AgyGoogleOAuthClientConfig config,
            string redirectUri,
            string state,
            string codeChallenge)
        {
            var query = BuildQuery(new Dictionary<string, string>
            {
                ["client_id"] = config?.ClientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = OAuthScope,
                ["access_type"] = "offline",
                ["prompt"] = "consent",
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state
            });
            return $"https://accounts.google.com/o/oauth2/v2/auth?{query}";
        }

        internal async Task<AgyOAuthTokenResult> ExchangeAuthorizationCodeAsync(
            string code,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken)
        {
            var config = RequireOAuthClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            });
            using var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Google OAuth token exchange failed with {(int)response.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var idToken = TryGetString(root, "id_token");
            var identity = ReadGoogleIdentityFromIdToken(idToken);
            return new AgyOAuthTokenResult
            {
                AccessToken = TryGetString(root, "access_token"),
                RefreshToken = TryGetString(root, "refresh_token"),
                IdToken = idToken,
                Email = identity.Email,
                Subject = identity.Subject
            };
        }

        internal async Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var config = RequireOAuthClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            });
            using var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var accessToken = TryGetString(doc.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Google OAuth token response did not include access_token.");
            }

            return accessToken;
        }

        internal async Task WarmLoadCodeAssistAsync(string accessToken, CancellationToken cancellationToken)
        {
            using var response = await PostAgyApiAsync("v1internal:loadCodeAssist", accessToken, cancellationToken);
        }

        internal async Task<JsonElement> RetrieveQuotaSummaryAsync(string accessToken, CancellationToken cancellationToken)
        {
            using var response = await PostAgyApiAsync("v1internal:retrieveUserQuotaSummary", accessToken, cancellationToken);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        internal static AgyGoogleOAuthClientConfig LoadConfig(string clientId, string clientSecret, string secretsPath)
        {
            if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
            {
                return new AgyGoogleOAuthClientConfig
                {
                    ClientId = clientId.Trim(),
                    ClientSecret = clientSecret.Trim(),
                    Source = "environment"
                };
            }

            if (string.IsNullOrWhiteSpace(secretsPath) || !File.Exists(secretsPath))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(secretsPath));
                var root = doc.RootElement;
                clientId = TryGetString(root, "clientId") ?? TryGetString(root, "client_id");
                clientSecret = TryGetString(root, "clientSecret") ?? TryGetString(root, "client_secret");
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    return null;
                }

                return new AgyGoogleOAuthClientConfig
                {
                    ClientId = clientId.Trim(),
                    ClientSecret = clientSecret.Trim(),
                    Source = "local-secrets-file"
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return null;
            }
        }

        internal static AgyGoogleIdentity ReadGoogleIdentityFromIdToken(string idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken))
            {
                return new AgyGoogleIdentity();
            }

            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return new AgyGoogleIdentity();
            }

            try
            {
                var payload = Encoding.UTF8.GetString(DecodeBase64Url(parts[1]));
                using var doc = JsonDocument.Parse(payload);
                return new AgyGoogleIdentity
                {
                    Subject = TryGetString(doc.RootElement, "sub"),
                    Email = TryGetString(doc.RootElement, "email")
                };
            }
            catch (Exception ex) when (ex is FormatException || ex is JsonException || ex is ArgumentException)
            {
                return new AgyGoogleIdentity();
            }
        }

        private AgyGoogleOAuthClientConfig RequireOAuthClient()
        {
            if (!TryGetOAuthClient(out var config, out var configError))
            {
                throw new InvalidOperationException(configError);
            }

            return config;
        }

        private static AgyGoogleOAuthClientConfig LoadDefaultConfig()
        {
            return LoadConfig(
                Environment.GetEnvironmentVariable(ClientIdEnv),
                Environment.GetEnvironmentVariable(ClientSecretEnv),
                AgyGoogleOAuthSecretsPath());
        }

        private async Task<HttpResponseMessage> PostAgyApiAsync(string path, string accessToken, CancellationToken cancellationToken)
        {
            Exception lastError = null;
            foreach (var baseUrl in QuotaApiBases)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{path}")
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Headers.TryAddWithoutValidation("x-goog-api-client", "gl-node/22.21.1");
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                    var response = await httpClient.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }

                    lastError = new HttpRequestException($"AGY quota API returned {(int)response.StatusCode} for {request.RequestUri}.");
                    response.Dispose();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new HttpRequestException($"AGY quota API request failed for {path}.");
        }

        private static string BuildQuery(IReadOnlyDictionary<string, string> values)
        {
            return string.Join("&", values
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        }
    }

    internal sealed class AgyGoogleOAuthClientConfig
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Source { get; set; }
    }

    internal sealed class AgyOAuthTokenResult
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string IdToken { get; set; }
        public string Email { get; set; }
        public string Subject { get; set; }
    }

    internal sealed class AgyGoogleIdentity
    {
        public string Subject { get; set; }
        public string Email { get; set; }
    }
}
