using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VibeDeck.Host.Connect;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class ConnectionCodeBrokerServiceTests
    {
        [Fact]
        public async Task Issue_async_registers_a_short_lived_code_with_the_expected_broker()
        {
            var handler = new RecordingHandler(async request =>
            {
                var payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
                var code = payload.RootElement.GetProperty("code").GetString();
                return JsonResponse(HttpStatusCode.OK, new
                {
                    code,
                    expiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
                });
            });
            using var client = new HttpClient(handler);
            var service = new ConnectionCodeBrokerService(client, "https://vibedeck.test/");

            var result = await service.IssueAsync(new PublicEndpointConfiguration
            {
                BaseDomain = "vibedeck.test",
                PublicUrl = "https://vd-1234567890abcdef.vibedeck.test/"
            }, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Matches("^[A-Z2-9]{8}$", result.Code);
            Assert.Equal("https://vibedeck.test/api/connect-codes", handler.LastRequestUri);
        }

        [Fact]
        public async Task Issue_async_rejects_a_broker_outside_the_public_base_domain()
        {
            using var client = new HttpClient(new RecordingHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, new { }))));
            var service = new ConnectionCodeBrokerService(client, "https://example.test/");

            var result = await service.IssueAsync(new PublicEndpointConfiguration
            {
                BaseDomain = "vibedeck.test",
                PublicUrl = "https://vd-1234567890abcdef.vibedeck.test/"
            }, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("connection_code.broker_not_configured", result.ErrorCode);
        }

        [Fact]
        public void Generated_codes_exclude_ambiguous_characters()
        {
            for (var index = 0; index < 100; index++)
            {
                var code = ConnectionCodeBrokerService.CreateCode();
                Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$", code);
            }
        }

        [Fact]
        public void Host_dependency_injection_has_one_public_constructor()
        {
            var constructor = Assert.Single(typeof(ConnectionCodeBrokerService).GetConstructors());
            var parameters = constructor.GetParameters();

            Assert.Equal(typeof(HttpClient), parameters[0].ParameterType);
            Assert.Equal("IConfiguration", parameters[1].ParameterType.Name);
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(JsonSerializer.Serialize(value))
            };
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> handler;

            public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            {
                this.handler = handler;
            }

            public string LastRequestUri { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestUri = request.RequestUri.ToString();
                return await handler(request);
            }
        }
    }
}
