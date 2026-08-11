using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using VibeDeck.Host.Security;

namespace VibeDeck.Host.Connect
{
    public sealed class ConnectInfoProvider
    {
        private const int HttpPort = 5000;
        private const int HttpsPort = 5443;

        private readonly PublicEndpointService publicEndpoint;
        private readonly CloudflareConnectorService managedConnector;

        public ConnectInfoProvider(PublicEndpointService publicEndpoint)
            : this(publicEndpoint, null)
        {
        }

        public ConnectInfoProvider(
            PublicEndpointService publicEndpoint,
            CloudflareConnectorService managedConnector)
        {
            this.publicEndpoint = publicEndpoint;
            this.managedConnector = managedConnector;
        }

        public ConnectInfo Get(HttpRequest request)
        {
            return Get(request, false);
        }

        public ConnectInfo Get(HttpContext context)
        {
            return Get(context.Request, publicEndpoint?.IsTrustedPublicRequest(context) == true);
        }

        private ConnectInfo Get(HttpRequest request, bool hideLanAddresses)
        {
            var addresses = GetLanAddresses().ToList();
            var primaryAddress = addresses.FirstOrDefault() ?? "127.0.0.1";
            var localNameHost = $"{Dns.GetHostName()}.local";
            var httpsUrl = $"https://{primaryAddress}:{HttpsPort}/";
            var httpUrl = $"http://{primaryAddress}:{HttpPort}/";
            var localNameHttpsUrl = $"https://{localNameHost}:{HttpsPort}/";
            var localNameHttpUrl = $"http://{localNameHost}:{HttpPort}/";
            var rootCertificateUrl = new Uri(new Uri(httpUrl), "cert/vibedeck-root.cer").ToString();
            var hostCertificateUrl = new Uri(new Uri(httpUrl), "cert/vibedeck-host.cer").ToString();
            var endpoint = publicEndpoint?.GetConfiguration() ?? new PublicEndpointConfiguration();
            var connector = managedConnector?.GetSnapshot() ?? new ManagedConnectorSnapshot
            {
                IsManaged = false,
                State = "external"
            };
            var usesTrustedPublicUrl = endpoint.IsConfigured &&
                (managedConnector == null || managedConnector.ShouldAdvertise(endpoint.PublicUrl));
            var localHttpsAvailable = LocalHttpsCertificate.IsConfigured;
            var httpsAvailable = usesTrustedPublicUrl || localHttpsAvailable;
            var preferredUrl = usesTrustedPublicUrl
                ? endpoint.PublicUrl
                : localHttpsAvailable ? httpsUrl : httpUrl;
            var canonicalHttpsUrl = usesTrustedPublicUrl ? endpoint.PublicUrl : httpsUrl;
            return new ConnectInfo
            {
                HostName = Dns.GetHostName(),
                PrimaryAddress = hideLanAddresses ? string.Empty : primaryAddress,
                PrettyHostName = localNameHost,
                PrettyHttpsUrl = localNameHttpsUrl,
                PrettyHttpUrl = localNameHttpUrl,
                LocalNameHost = localNameHost,
                LocalNameHttpsUrl = localNameHttpsUrl,
                LocalNameHttpUrl = localNameHttpUrl,
                HttpsUrl = canonicalHttpsUrl,
                HttpUrl = httpUrl,
                PreferredUrl = preferredUrl,
                HttpsAvailable = httpsAvailable,
                RootCertificateUrl = usesTrustedPublicUrl ? null : rootCertificateUrl,
                HostCertificateUrl = usesTrustedPublicUrl ? null : hostCertificateUrl,
                HttpsSetupHint = usesTrustedPublicUrl
                    ? "Open the VibeDeck secure URL. No browser warning or certificate installation is required."
                    : localHttpsAvailable
                    ? "Open the HTTPS URL and continue past the browser's first-use certificate warning."
                    : "Restart VibeDeck Host so it can mint a local HTTPS certificate on port 5443.",
                PublicUrl = endpoint.PublicUrl,
                InstallationId = endpoint.InstallationId,
                PublicBaseDomain = endpoint.BaseDomain,
                UsesTrustedPublicUrl = usesTrustedPublicUrl,
                PublicConnectorManaged = connector.IsManaged,
                PublicConnectorState = connector.State,
                PublicConnectorRunning = connector.IsRunning,
                PublicConnectorHealthy = connector.IsHealthy,
                PublicConnectorError = connector.LastError,
                IsHttpsRequest = request.IsHttps,
                WakeLockNeedsHttps = !request.IsHttps,
                Addresses = hideLanAddresses ? Array.Empty<string>() : addresses
            };
        }

        private static IEnumerable<string> GetLanAddresses()
        {
            var candidates = new List<LanAddressCandidate>();
            var order = 0;
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = networkInterface.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.Any.Equals(gateway.Address) &&
                    !IPAddress.None.Equals(gateway.Address));
                var virtualLike = IsVirtualLike(networkInterface);
                foreach (var address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var text = address.Address.ToString();
                    if (text.StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    candidates.Add(new LanAddressCandidate
                    {
                        Address = text,
                        Score = ScoreAddress(networkInterface, hasGateway, virtualLike),
                        Order = order++
                    });
                }
            }

            return candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Order)
                .Select(candidate => candidate.Address);
        }

        private static int ScoreAddress(NetworkInterface networkInterface, bool hasGateway, bool virtualLike)
        {
            var score = 0;
            if (hasGateway)
            {
                score += 100;
            }

            if (virtualLike)
            {
                score -= 80;
            }
            else
            {
                score += 40;
            }

            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            {
                score += 20;
            }

            return score;
        }

        private static bool IsVirtualLike(NetworkInterface networkInterface)
        {
            var text = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();
            return text.Contains("virtual") ||
                text.Contains("hyper-v") ||
                text.Contains("wsl") ||
                text.Contains("vmware") ||
                text.Contains("virtualbox") ||
                text.Contains("hamachi") ||
                text.Contains("loopback");
        }

        private sealed class LanAddressCandidate
        {
            public string Address { get; set; }
            public int Score { get; set; }
            public int Order { get; set; }
        }
    }
}
