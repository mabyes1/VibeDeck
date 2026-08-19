using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace VibeDeck.Host.CustomDecks
{
    internal static class CustomDeckProxyTarget
    {
        public static Uri Parse(string value)
        {
            if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidDataException("Proxy Deck url must be an absolute HTTP or HTTPS URL.");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidDataException("Proxy Deck url must not contain credentials.");
            }

            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return EnsureDirectoryBase(uri);
            if (!IPAddress.TryParse(uri.Host, out var address) || !IsPrivate(address))
            {
                throw new InvalidDataException("Proxy Deck url must use a loopback or private-network IP address.");
            }

            return EnsureDirectoryBase(uri);
        }

        public static bool IsPrivate(IPAddress address)
        {
            if (IPAddress.IsLoopback(address)) return true;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                return bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168);
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal) return true;
                var bytes = address.GetAddressBytes();
                return (bytes[0] & 0xfe) == 0xfc;
            }

            return false;
        }

        private static Uri EnsureDirectoryBase(Uri uri)
        {
            var builder = new UriBuilder(uri) { Fragment = string.Empty };
            if (!builder.Path.EndsWith("/", StringComparison.Ordinal)) builder.Path += "/";
            return builder.Uri;
        }
    }
}
