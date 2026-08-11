using System;
using System.IO;
using System.Text;
using System.Text.Json;

using VibeDeck.Host.Quotas;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class AgyGoogleApiClientTests
    {
        [Fact]
        public void LoadConfigPrefersExplicitCredentials()
        {
            var config = AgyGoogleApiClient.LoadConfig(" client-id ", " secret ", "missing.json");

            Assert.Equal("client-id", config.ClientId);
            Assert.Equal("secret", config.ClientSecret);
            Assert.Equal("environment", config.Source);
        }

        [Fact]
        public void LoadConfigReadsCamelCaseSecretsFile()
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = Path.Combine(directory, "agy-google-oauth.json");
                File.WriteAllText(path, "{\"clientId\":\"file-client\",\"clientSecret\":\"file-secret\"}");

                var config = AgyGoogleApiClient.LoadConfig(null, null, path);

                Assert.Equal("file-client", config.ClientId);
                Assert.Equal("file-secret", config.ClientSecret);
                Assert.Equal("local-secrets-file", config.Source);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void BuildAuthorizationUrlPreservesPkceAndOfflineConsentParameters()
        {
            var client = new AgyGoogleApiClient(configLoader: () => new AgyGoogleOAuthClientConfig
            {
                ClientId = "client id",
                ClientSecret = "secret"
            });

            var url = client.BuildAuthorizationUrl(
                new AgyGoogleOAuthClientConfig { ClientId = "client id", ClientSecret = "secret" },
                "http://127.0.0.1/callback?a=1",
                "state value",
                "challenge/value");

            Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
            Assert.Contains("client_id=client%20id", url);
            Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%2Fcallback%3Fa%3D1", url);
            Assert.Contains("access_type=offline", url);
            Assert.Contains("prompt=consent", url);
            Assert.Contains("code_challenge=challenge%2Fvalue", url);
            Assert.Contains("code_challenge_method=S256", url);
            Assert.Contains("state=state%20value", url);
        }

        [Fact]
        public void ReadGoogleIdentityFromIdTokenDecodesPayloadWithoutDependingOnSignature()
        {
            var payload = JsonSerializer.Serialize(new { sub = "account-123", email = "agy@example.com" });
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            var identity = AgyGoogleApiClient.ReadGoogleIdentityFromIdToken($"header.{encoded}.signature");

            Assert.Equal("account-123", identity.Subject);
            Assert.Equal("agy@example.com", identity.Email);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-jwt")]
        [InlineData("a.invalid-base64.c")]
        public void ReadGoogleIdentityFromIdTokenFailsClosed(string token)
        {
            var identity = AgyGoogleApiClient.ReadGoogleIdentityFromIdToken(token);

            Assert.Null(identity.Subject);
            Assert.Null(identity.Email);
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "VibeDeck-AgyGoogleClientTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
