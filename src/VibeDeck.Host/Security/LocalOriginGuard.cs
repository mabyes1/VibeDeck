using System;
using System.Collections.Generic;

namespace VibeDeck.Host.Security
{
    /// <summary>
    /// Decides whether a request that arrived on a local socket really is the local console.
    ///
    /// A loopback peer is not sufficient on its own: a page whose DNS name has been rebound to
    /// 127.0.0.1 also connects from loopback, and would otherwise inherit local-console
    /// privileges (including the action token) while running attacker script.
    /// </summary>
    public static class LocalOriginGuard
    {
        /// <summary>True when <paramref name="hostHeader"/> names this machine.</summary>
        public static bool IsExpectedHost(string hostHeader, ICollection<string> localHostNames)
        {
            if (string.IsNullOrWhiteSpace(hostHeader) || localHostNames == null)
            {
                return false;
            }

            return localHostNames.Contains(StripPort(hostHeader));
        }

        /// <summary>
        /// True when the caller is a browser acting for another site.
        /// Callers that send neither Sec-Fetch-Site nor Origin are non-browser local
        /// clients and pass. An explicit <c>Origin: null</c> is always cross-site:
        /// sandboxed/opaque origins must never inherit local-console privilege.
        /// </summary>
        public static bool IsCrossSite(string secFetchSite, string origin, ICollection<string> localHostNames)
        {
            if (string.Equals(secFetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Opaque / sandboxed origin. Not "missing Origin" — the browser named it.
            if (string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(origin))
            {
                return false;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            {
                return true;
            }

            return localHostNames == null || !localHostNames.Contains(originUri.Host);
        }

        /// <summary>Removes a trailing :port, leaving bare IPv6 literals and bracket forms intact.</summary>
        public static string StripPort(string host)
        {
            var value = (host ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return value;
            }

            if (value[0] == '[')
            {
                var close = value.IndexOf(']');
                return close > 0 ? value.Substring(1, close - 1) : value;
            }

            var colon = value.LastIndexOf(':');
            // Only strip when there is exactly one colon; several means a bare IPv6 literal.
            if (colon > 0 && value.IndexOf(':') == colon)
            {
                return value.Substring(0, colon);
            }

            return value;
        }
    }
}
