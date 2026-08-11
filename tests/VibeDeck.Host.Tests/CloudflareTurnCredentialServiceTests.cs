using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class CloudflareTurnCredentialServiceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "VibeDeckTurnTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void Turn_settings_encrypt_the_long_lived_api_token_at_rest()
        {
            var path = CreateStorePath();
            var store = new CloudflareTurnSettingsStore(path);
            store.Configure("turn_key_123456", "cloudflare-api-token-that-must-not-be-plain-text");

            var raw = File.ReadAllText(path);
            var restored = new CloudflareTurnSettingsStore(path).Get();

            Assert.DoesNotContain("cloudflare-api-token-that-must-not-be-plain-text", raw);
            Assert.True(restored.IsConfigured);
            Assert.Equal("turn_key_123456", restored.KeyId);
            Assert.Equal("cloudflare-api-token-that-must-not-be-plain-text", restored.ApiToken);
            Assert.Equal("turn…3456", restored.MaskedKeyId);
        }

        [Fact]
        public async Task Credential_service_mints_short_lived_ice_servers_and_filters_blocked_port_53()
        {
            var store = new CloudflareTurnSettingsStore(CreateStorePath());
            store.Configure("turn_key_123456", "cloudflare-api-token-that-must-not-be-plain-text");
            using var client = new HttpClient(new RecordingHandler(async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal(
                    "https://rtc.live.cloudflare.com/v1/turn/keys/turn_key_123456/credentials/generate-ice-servers",
                    request.RequestUri.ToString());
                Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
                Assert.Equal("cloudflare-api-token-that-must-not-be-plain-text", request.Headers.Authorization.Parameter);
                var body = await request.Content.ReadAsStringAsync();
                Assert.Contains("\"ttl\":3600", body);
                return JsonResponse(HttpStatusCode.Created, new
                {
                    iceServers = new[]
                    {
                        new
                        {
                            urls = new[] { "stun:stun.cloudflare.com:3478", "stun:stun.cloudflare.com:53" },
                            username = (string)null,
                            credential = (string)null
                        },
                        new
                        {
                            urls = new[]
                            {
                                "turn:turn.cloudflare.com:3478?transport=udp",
                                "turn:turn.cloudflare.com:53?transport=udp",
                                "turns:turn.cloudflare.com:443?transport=tcp"
                            },
                            username = "short-lived-user",
                            credential = "short-lived-credential"
                        }
                    }
                });
            }));
            var service = new CloudflareTurnCredentialService(client, store);

            var result = await service.CreateIceServersAsync("browser-test", CancellationToken.None);

            Assert.True(result.TurnConfigured);
            Assert.True(result.TurnAvailable);
            Assert.Contains(result.IceServers, server => server.Urls.Contains("stun:stun.cloudflare.com:3478"));
            Assert.Contains(result.IceServers, server => server.Urls.Contains("turn:turn.cloudflare.com:3478?transport=udp"));
            Assert.Contains(result.IceServers, server => server.Urls.Contains("turns:turn.cloudflare.com:443?transport=tcp"));
            Assert.DoesNotContain(result.IceServers.SelectMany(server => server.Urls), url => url.Contains(":53"));
        }

        [Fact]
        public async Task Credential_service_keeps_direct_stun_available_without_turn_settings()
        {
            using var client = new HttpClient(new RecordingHandler(_ => throw new Xunit.Sdk.XunitException("TURN must not be called without settings.")));
            var service = new CloudflareTurnCredentialService(client, new CloudflareTurnSettingsStore(CreateStorePath()));

            var result = await service.CreateIceServersAsync("browser-test", CancellationToken.None);

            Assert.False(result.TurnConfigured);
            Assert.False(result.TurnAvailable);
            Assert.Single(result.IceServers);
            Assert.Equal("stun:stun.cloudflare.com:3478", Assert.Single(result.IceServers[0].Urls));
        }

        private string CreateStorePath()
        {
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "cloudflare-turn.json");
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(JsonSerializer.Serialize(value))
            };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch
            {
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> handler;

            public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            {
                this.handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return handler(request);
            }
        }
    }
}
