using System;
using System.Collections.Generic;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class LocalOriginGuardTests
    {
        private static HashSet<string> LocalNames() => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "127.0.0.1",
            "::1",
            "DESKTOP-KEN",
            "DESKTOP-KEN.local",
            "192.168.1.50",
        };

        [Theory]
        [InlineData("localhost:5000")]
        [InlineData("127.0.0.1:5000")]
        [InlineData("127.0.0.1")]
        [InlineData("LOCALHOST:5443")]
        [InlineData("desktop-ken:5000")]
        [InlineData("DESKTOP-KEN.local")]
        [InlineData("192.168.1.50:5000")]
        [InlineData("[::1]:5000")]
        public void RealLocalConsoleHostnamesAreAccepted(string host)
        {
            Assert.True(LocalOriginGuard.IsExpectedHost(host, LocalNames()), host);
        }

        [Theory]
        [InlineData("attacker.com:5000")]
        [InlineData("rebind.attacker.com")]
        [InlineData("vd-a1b2c3d4e5f60718.vibedeck.pp.ua")]
        [InlineData("")]
        [InlineData(null)]
        public void ForeignHostnamesAreRejected(string host)
        {
            // The DNS-rebinding case: the peer is loopback but the page still says who it is.
            Assert.False(LocalOriginGuard.IsExpectedHost(host, LocalNames()), host ?? "<null>");
        }

        [Fact]
        public void NonBrowserCallersWithoutFetchMetadataArePassedThrough()
        {
            Assert.False(LocalOriginGuard.IsCrossSite(null, null, LocalNames()));
            Assert.False(LocalOriginGuard.IsCrossSite("", "", LocalNames()));
        }

        [Theory]
        [InlineData("same-origin")]
        [InlineData("same-site")]
        [InlineData("none")]
        public void SameSiteNavigationsAreNotCrossSite(string fetchSite)
        {
            Assert.False(LocalOriginGuard.IsCrossSite(fetchSite, "http://localhost:5000", LocalNames()));
        }

        [Fact]
        public void CrossSiteFetchMetadataIsRejected()
        {
            Assert.True(LocalOriginGuard.IsCrossSite("cross-site", null, LocalNames()));
            Assert.True(LocalOriginGuard.IsCrossSite("cross-site", "http://localhost:5000", LocalNames()));
        }

        [Fact]
        public void ForeignOriginIsRejectedEvenWhenFetchMetadataIsAbsent()
        {
            // Older browsers omit Sec-Fetch-Site; Origin still gives the attacker away.
            Assert.True(LocalOriginGuard.IsCrossSite(null, "https://attacker.com", LocalNames()));
            Assert.True(LocalOriginGuard.IsCrossSite(null, "not-a-url", LocalNames()));
        }

        [Fact]
        public void OpaqueOriginIsRejected()
        {
            // Origin: null is an explicit opaque/sandboxed origin. It must never
            // inherit local-console privilege for /ws/* or RequireTrustedDevice.
            Assert.True(LocalOriginGuard.IsCrossSite(null, "null", LocalNames()));
            Assert.True(LocalOriginGuard.IsCrossSite("none", "null", LocalNames()));
            Assert.True(LocalOriginGuard.IsCrossSite(null, "NULL", LocalNames()));
        }

        [Fact]
        public void DriveByWebSocketFromForeignSiteIsCrossSite()
        {
            // RequireTrustedDeviceAsync uses IsTrustedLocalConsole, which rejects
            // a browser page that opens ws://127.0.0.1:5000/ws/input or /ws/display
            // from another origin. Loopback peer + local Host is not enough.
            Assert.True(LocalOriginGuard.IsCrossSite(null, "https://evil.example", LocalNames()));
            Assert.True(LocalOriginGuard.IsCrossSite("cross-site", "http://127.0.0.1:5000", LocalNames()));
            Assert.False(LocalOriginGuard.IsCrossSite(null, "http://127.0.0.1:5000", LocalNames()));
            Assert.False(LocalOriginGuard.IsCrossSite(null, "https://192.168.1.50:5443", LocalNames()));
            Assert.False(LocalOriginGuard.IsCrossSite(null, null, LocalNames()));
            Assert.False(LocalOriginGuard.IsCrossSite("", "", LocalNames()));
        }

        [Theory]
        [InlineData("localhost:5000", "localhost")]
        [InlineData("localhost", "localhost")]
        [InlineData("[::1]:5000", "::1")]
        [InlineData("[fe80::1]", "fe80::1")]
        [InlineData("::1", "::1")]
        [InlineData("192.168.1.50:5443", "192.168.1.50")]
        public void PortIsStrippedWithoutManglingIpv6Literals(string input, string expected)
        {
            Assert.Equal(expected, LocalOriginGuard.StripPort(input));
        }
    }
}
