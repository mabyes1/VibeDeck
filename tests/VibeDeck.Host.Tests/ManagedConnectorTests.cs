using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VibeDeck.Host.Connect;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class ManagedConnectorTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "VibeDeckManagedConnectorTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Provisioning_client_validates_and_returns_the_assigned_tunnel()
        {
            using var client = new HttpClient(new RecordingHandler(request =>
            {
                Assert.Equal("https://vibedeck.test/api/installations/provision", request.RequestUri.ToString());
                return JsonResponse(HttpStatusCode.Created, new
                {
                    installationId = "vd-1234567890abcdef",
                    publicUrl = "https://vd-1234567890abcdef.vibedeck.test/",
                    tunnelId = "11111111-2222-4333-8444-555555555555",
                    tunnelToken = "tunnel-token"
                });
            }));
            var service = new CloudflareProvisioningClient(client, "https://vibedeck.test/api/installations/provision");

            var result = await service.ProvisionAsync(
                "vd-1234567890abcdef",
                "provisioning-secret",
                "0.1.29",
                CancellationToken.None);

            Assert.Equal("https://vd-1234567890abcdef.vibedeck.test/", result.PublicUrl);
            Assert.Equal("11111111-2222-4333-8444-555555555555", result.TunnelId);
            Assert.Equal("tunnel-token", result.TunnelToken);
        }

        [Fact]
        public void Tunnel_state_encrypts_and_restores_local_credentials()
        {
            var path = Path.Combine(directory, "managed-tunnel.json");
            var store = new ManagedTunnelStateStore(path);
            var pending = store.LoadOrCreate("vd-1234567890abcdef");
            store.SaveProvisioned(
                pending,
                "https://vd-1234567890abcdef.vibedeck.test/",
                "11111111-2222-4333-8444-555555555555",
                "sensitive-tunnel-token");

            var raw = File.ReadAllText(path);
            var restored = new ManagedTunnelStateStore(path).LoadOrCreate("vd-1234567890abcdef");

            Assert.DoesNotContain("sensitive-tunnel-token", raw);
            Assert.DoesNotContain(pending.ProvisioningSecret, raw);
            Assert.Equal("sensitive-tunnel-token", restored.TunnelToken);
            Assert.Equal(pending.ProvisioningSecret, restored.ProvisioningSecret);
            Assert.True(restored.IsProvisioned);
        }

        [Fact]
        public void Connect_info_falls_back_when_the_managed_connector_is_not_running()
        {
            Directory.CreateDirectory(directory);
            var endpoints = new PublicEndpointService(Path.Combine(directory, "endpoint.json"), "vibedeck.test");
            var configuration = endpoints.GetConfiguration();
            endpoints.Configure($"https://{configuration.InstallationId}.vibedeck.test/");
            using var provisioningHttp = new HttpClient(new RecordingHandler(_ => JsonResponse(HttpStatusCode.ServiceUnavailable, new { })));
            var connector = new CloudflareConnectorService(
                endpoints,
                new CloudflareProvisioningClient(provisioningHttp, "https://vibedeck.test/api/installations/provision"),
                null,
                new HttpClient(),
                new ManagedTunnelStateStore(Path.Combine(directory, "managed.json")),
                true);

            var info = new ConnectInfoProvider(endpoints, connector).Get(new DefaultHttpContext());

            Assert.True(info.PublicConnectorManaged);
            Assert.Equal("waiting", info.PublicConnectorState);
            Assert.False(info.UsesTrustedPublicUrl);
            Assert.Equal($"https://{configuration.InstallationId}.vibedeck.test/", info.PublicUrl);
        }

        [Fact]
        public void Startup_shares_one_managed_connector_with_hosting_and_connect_info()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            new Startup().ConfigureServices(services);
            using var provider = services.BuildServiceProvider();

            var connector = provider.GetRequiredService<CloudflareConnectorService>();
            var hostedConnector = Assert.Single(provider.GetServices<IHostedService>(), service => service is CloudflareConnectorService);
            var connectInfo = provider.GetRequiredService<ConnectInfoProvider>();
            var connectorField = typeof(ConnectInfoProvider).GetField("managedConnector", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.Same(connector, hostedConnector);
            Assert.NotNull(connectorField);
            Assert.Same(connector, connectorField.GetValue(connectInfo));
        }

        [Fact]
        public async Task Connector_output_drain_returns_control_before_output_arrives()
        {
            using var server = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            using var client = new AnonymousPipeClientStream(PipeDirection.Out, server.GetClientHandleAsString());
            using var reader = new StreamReader(server);
            using var cancellation = new CancellationTokenSource();

            var invocation = Task.Run<Task>(() => CloudflareConnectorService.DrainOutputAsync(reader, cancellation.Token));
            var drain = await invocation.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(drain.IsCompleted);
            cancellation.Cancel();
            client.Dispose();
            await drain.WaitAsync(TimeSpan.FromSeconds(1));
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
            private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

            public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                this.handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(handler(request));
            }
        }
    }
}
