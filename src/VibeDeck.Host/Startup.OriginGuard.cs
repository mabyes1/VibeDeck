using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using VibeDeck.Host.Security;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        /// <summary>
        /// Host header exactly as Kestrel received it, captured before UseForwardedHeaders can
        /// replace it with an X-Forwarded-Host value.
        /// </summary>
        internal const string OriginalHostItemKey = "VibeDeck.OriginalHost";

        private static readonly TimeSpan LocalHostNameCacheTtl = TimeSpan.FromSeconds(60);
        private static readonly object LocalHostNameGate = new object();
        private static HashSet<string> cachedLocalHostNames;
        private static DateTimeOffset localHostNamesRefreshedAt = DateTimeOffset.MinValue;

        private static bool IsExpectedLocalHost(HttpContext context)
        {
            // Deliberately the pre-forwarding value. A rebinding page is itself a loopback peer,
            // and loopback is a known proxy, so it could otherwise spoof X-Forwarded-Host.
            var host = context.Items.TryGetValue(OriginalHostItemKey, out var captured)
                ? captured as string
                : context.Request.Host.Value;

            return LocalOriginGuard.IsExpectedHost(host, LocalHostNames());
        }

        private static bool IsCrossSiteRequest(HttpContext context)
        {
            return LocalOriginGuard.IsCrossSite(
                context.Request.Headers["Sec-Fetch-Site"].FirstOrDefault(),
                context.Request.Headers["Origin"].FirstOrDefault(),
                LocalHostNames());
        }

        /// <summary>
        /// Local-console privilege requires a local peer, a hostname that names this machine, and
        /// an origin that is not another site.
        /// </summary>
        private static bool IsTrustedLocalConsole(HttpContext context)
        {
            return IsLocalRequest(context) && !IsCrossSiteRequest(context);
        }

        /// <summary>Names this machine answers to, refreshed periodically as interfaces change.</summary>
        private static HashSet<string> LocalHostNames()
        {
            var now = DateTimeOffset.UtcNow;
            lock (LocalHostNameGate)
            {
                if (cachedLocalHostNames != null && now - localHostNamesRefreshedAt < LocalHostNameCacheTtl)
                {
                    return cachedLocalHostNames;
                }

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "localhost",
                    "127.0.0.1",
                    "::1",
                };

                try
                {
                    var machine = Dns.GetHostName();
                    if (!string.IsNullOrWhiteSpace(machine))
                    {
                        names.Add(machine);
                        names.Add(machine + ".local");
                    }
                }
                catch
                {
                }

                try
                {
                    foreach (var unicast in NetworkInterface.GetAllNetworkInterfaces()
                        .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                        .SelectMany(nic => nic.GetIPProperties().UnicastAddresses))
                    {
                        var address = unicast.Address;
                        if (address.IsIPv4MappedToIPv6)
                        {
                            address = address.MapToIPv4();
                        }

                        if (address.AddressFamily == AddressFamily.InterNetwork ||
                            address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            names.Add(address.ToString());
                        }
                    }
                }
                catch
                {
                }

                cachedLocalHostNames = names;
                localHostNamesRefreshedAt = now;
                return names;
            }
        }
    }
}
