using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Http;
using VibeDeck.Host.Connect;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class PublicEndpointServiceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "VibeDeckPublicEndpointTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void Installation_id_and_configured_endpoint_persist_across_restarts()
        {
            var path = CreateStorePath();
            var first = new PublicEndpointService(path, "vibedeck.test");
            var initial = first.GetConfiguration();
            var expectedUrl = $"https://{initial.InstallationId}.vibedeck.test/";

            var configured = first.Configure(expectedUrl);
            var restarted = new PublicEndpointService(path, "vibedeck.test").GetConfiguration();

            Assert.StartsWith("vd-", initial.InstallationId);
            Assert.Equal(19, initial.InstallationId.Length);
            Assert.True(configured.IsConfigured);
            Assert.Equal(expectedUrl, configured.PublicUrl);
            Assert.Equal(initial.InstallationId, restarted.InstallationId);
            Assert.Equal(expectedUrl, restarted.PublicUrl);
        }

        [Fact]
        public void Endpoint_must_be_the_installation_specific_https_hostname()
        {
            var service = new PublicEndpointService(CreateStorePath(), "vibedeck.test");
            var endpoint = $"https://{service.GetConfiguration().InstallationId}.vibedeck.test/";

            Assert.Throws<PublicEndpointException>(() => service.Configure(endpoint.Replace("https://", "http://")));
            Assert.Throws<PublicEndpointException>(() => service.Configure(endpoint + "extra"));
            Assert.Throws<PublicEndpointException>(() => service.Configure(endpoint + "?mode=pair"));
            Assert.Throws<PublicEndpointException>(() => service.Configure("https://someone-else.vibedeck.test/"));
            Assert.True(service.Configure(endpoint).IsConfigured);
        }

        [Fact]
        public void Null_store_recovers_to_a_new_persisted_installation_id()
        {
            var path = CreateStorePath();
            File.WriteAllText(path, "null");

            var configuration = new PublicEndpointService(path, "vibedeck.test").GetConfiguration();
            var restarted = new PublicEndpointService(path, "vibedeck.test").GetConfiguration();

            Assert.StartsWith("vd-", configuration.InstallationId);
            Assert.False(configuration.IsConfigured);
            Assert.Equal(configuration.InstallationId, restarted.InstallationId);
        }

        [Fact]
        public void Trusted_public_request_requires_loopback_connector_exact_host_and_https()
        {
            var service = new PublicEndpointService(CreateStorePath(), "vibedeck.test");
            var configuration = service.GetConfiguration();
            service.Configure($"https://{configuration.InstallationId}.vibedeck.test/");

            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Host = new HostString($"{configuration.InstallationId}.vibedeck.test");
            context.Items[PublicEndpointService.OriginalRemoteAddressItemKey] = IPAddress.Loopback;

            Assert.True(service.IsTrustedPublicRequest(context));

            context.Items[PublicEndpointService.OriginalRemoteAddressItemKey] = IPAddress.Parse("192.168.1.18");
            Assert.False(service.IsTrustedPublicRequest(context));

            context.Items[PublicEndpointService.OriginalRemoteAddressItemKey] = IPAddress.Loopback;
            context.Request.Host = new HostString("someone-else.vibedeck.test");
            Assert.False(service.IsTrustedPublicRequest(context));
        }

        [Fact]
        public void Connect_info_prefers_the_trusted_public_url_when_configured()
        {
            var service = new PublicEndpointService(CreateStorePath(), "vibedeck.test");
            var configuration = service.GetConfiguration();
            var expectedUrl = $"https://{configuration.InstallationId}.vibedeck.test/";
            service.Configure(expectedUrl);

            var info = new ConnectInfoProvider(service).Get(new DefaultHttpContext());

            Assert.True(info.UsesTrustedPublicUrl);
            Assert.True(info.HttpsAvailable);
            Assert.Equal(expectedUrl, info.PublicUrl);
            Assert.Equal(expectedUrl, info.PreferredUrl);
            Assert.Equal(expectedUrl, info.HttpsUrl);
            Assert.Null(info.RootCertificateUrl);
        }

        private string CreateStorePath()
        {
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "public-endpoint.json");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch
            {
            }
        }
    }
}
