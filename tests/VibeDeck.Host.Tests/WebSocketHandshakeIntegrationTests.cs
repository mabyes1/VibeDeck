using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    [CollectionDefinition(nameof(WebSocketHandshakeIntegrationTests), DisableParallelization = true)]
    public sealed class WebSocketHandshakeCollection
    {
    }

    /// <summary>
    /// Boots the real Startup pipeline on Kestrel (ephemeral loopback port) and
    /// exercises WebSocket handshakes. LocalOriginGuard unit tests alone cannot
    /// prove /ws/* reject cross-site browsers.
    /// </summary>
    [Collection(nameof(WebSocketHandshakeIntegrationTests))]
    public sealed class WebSocketHandshakeIntegrationTests : IAsyncLifetime
    {
        private string dataRoot;
        private IHost host;
        private Uri baseHttpUri;

        public async Task InitializeAsync()
        {
            dataRoot = Path.Combine(Path.GetTempPath(), "vibedeck-ws-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            Environment.SetEnvironmentVariable("VIBEDECK_DATA", dataRoot);

            host = new HostBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Loopback, 0);
                    });
                    webBuilder.UseStartup<Startup>();
                })
                .Build();

            await host.StartAsync();
            var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var address = addresses.Addresses.GetEnumerator();
            Assert.True(address.MoveNext(), "Kestrel did not bind an address.");
            baseHttpUri = new Uri(address.Current);
        }

        public async Task DisposeAsync()
        {
            if (host != null)
            {
                await host.StopAsync(TimeSpan.FromSeconds(3));
                host.Dispose();
            }

            Environment.SetEnvironmentVariable("VIBEDECK_DATA", null);
            try
            {
                if (!string.IsNullOrWhiteSpace(dataRoot) && Directory.Exists(dataRoot))
                {
                    Directory.Delete(dataRoot, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup is best-effort.
            }
        }

        private static Uri BuildWsUri(Uri baseHttpUri, string pathAndQuery)
        {
            var builder = new UriBuilder(baseHttpUri)
            {
                Scheme = "ws",
                Path = pathAndQuery.Split('?')[0],
                Query = pathAndQuery.Contains('?') ? pathAndQuery.Substring(pathAndQuery.IndexOf('?') + 1) : ""
            };
            return builder.Uri;
        }

        private static async Task<bool> TryHandshakeAsync(
            Uri baseHttpUri,
            string pathAndQuery,
            Action<ClientWebSocket> configureSocket)
        {
            using var socket = new ClientWebSocket();
            configureSocket?.Invoke(socket);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                await socket.ConnectAsync(BuildWsUri(baseHttpUri, pathAndQuery), timeout.Token);
                return socket.State == WebSocketState.Open;
            }
            catch (WebSocketException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        [Theory]
        [InlineData("/ws/input")]
        [InlineData("/ws/display")]
        public async Task ForeignOriginHandshakeIsRejected(string path)
        {
            var ok = await TryHandshakeAsync(baseHttpUri, path, socket =>
            {
                socket.Options.SetRequestHeader("Origin", "https://evil.example");
            });
            Assert.False(ok, $"Cross-site Origin must not upgrade {path}.");
        }

        [Theory]
        [InlineData("/ws/input")]
        [InlineData("/ws/display")]
        public async Task CrossSiteFetchMetadataIsRejected(string path)
        {
            var ok = await TryHandshakeAsync(baseHttpUri, path, socket =>
            {
                socket.Options.SetRequestHeader("Sec-Fetch-Site", "cross-site");
                socket.Options.SetRequestHeader("Origin", "http://127.0.0.1:5000");
            });
            Assert.False(ok, $"Sec-Fetch-Site: cross-site must not upgrade {path}.");
        }

        [Theory]
        [InlineData("/ws/input")]
        [InlineData("/ws/display")]
        public async Task OpaqueOriginNullIsRejected(string path)
        {
            var ok = await TryHandshakeAsync(baseHttpUri, path, socket =>
            {
                socket.Options.SetRequestHeader("Origin", "null");
            });
            Assert.False(ok, $"Origin: null must not upgrade {path}.");
        }

        [Theory]
        [InlineData("/ws/input")]
        [InlineData("/ws/display")]
        public async Task LocalhostOriginHandshakeSucceeds(string path)
        {
            var ok = await TryHandshakeAsync(baseHttpUri, path, socket =>
            {
                socket.Options.SetRequestHeader("Origin", "http://localhost:5000");
            });
            Assert.True(ok, $"Localhost Origin must upgrade {path}.");
        }

        [Theory]
        [InlineData("/ws/input")]
        [InlineData("/ws/display")]
        public async Task NonBrowserLocalClientWithoutOriginSucceeds(string path)
        {
            var ok = await TryHandshakeAsync(baseHttpUri, path, _ => { });
            Assert.True(ok, $"Non-browser local client must still upgrade {path}.");
        }

        [Fact]
        public async Task TrustedDeviceTokenStillAuthorizesHandshake()
        {
            var devices = host.Services.GetRequiredService<DeviceTrustService>();
            var approval = devices.RequestApproval(
                "test-phone",
                "web",
                "Pixel",
                "ws-test-instance-" + Guid.NewGuid().ToString("N"),
                "VibeDeck-Tests",
                "127.0.0.1");
            var approved = devices.ApproveRequest(approval.RequestId);
            Assert.True(approved.Success, approved.Message);

            var poll = devices.PollApproval(approval.RequestId, approval.RequestSecret);
            Assert.True(poll.Success);
            Assert.False(string.IsNullOrWhiteSpace(poll.DeviceToken));

            var ok = await TryHandshakeAsync(baseHttpUri, "/ws/input", socket =>
            {
                socket.Options.SetRequestHeader("Origin", "https://evil.example");
                socket.Options.SetRequestHeader(DeviceTrustService.HeaderName, poll.DeviceToken);
            });

            // Paired device tokens remain valid from any origin (phone UI may not
            // always send a same-machine Origin). The local-console shortcut is
            // what requires IsTrustedLocalConsole.
            Assert.True(ok, "Trusted device token must still authorize /ws/input.");
        }

        [Fact]
        public async Task SingleUseTicketStillAuthorizesHandshake()
        {
            var tickets = host.Services.GetRequiredService<WebSocketTicketService>();
            var ticket = tickets.Issue("device-from-ticket", DateTimeOffset.UtcNow);

            var withoutTicket = await TryHandshakeAsync(baseHttpUri, "/ws/display", socket =>
            {
                socket.Options.SetRequestHeader("Origin", "https://evil.example");
            });
            Assert.False(withoutTicket, "Foreign Origin without ticket must fail.");

            var withTicket = await TryHandshakeAsync(
                baseHttpUri,
                "/ws/display?ticket=" + Uri.EscapeDataString(ticket),
                socket =>
                {
                    socket.Options.SetRequestHeader("Origin", "https://evil.example");
                });
            Assert.True(withTicket, "Single-use ticket must authorize the handshake.");

            var reused = tickets.TryRedeem(ticket, DateTimeOffset.UtcNow, out _);
            Assert.False(reused, "Ticket must be single-use.");
        }
    }
}
