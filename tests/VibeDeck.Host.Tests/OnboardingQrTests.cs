using VibeDeck.Host.Connect;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class OnboardingQrTests
    {
        [Fact]
        public void Trusted_public_qr_uses_the_human_entry_domain_and_installation_route()
        {
            var connectInfo = new ConnectInfo
            {
                UsesTrustedPublicUrl = true,
                PublicBaseDomain = "vibedeck.pp.ua",
                InstallationId = "vd-1234567890abcdef",
                PreferredUrl = "https://vd-1234567890abcdef.vibedeck.pp.ua/"
            };

            var url = Startup.BuildOnboardingQrUrl(connectInfo);

            Assert.Equal("https://vibedeck.pp.ua/to/vd-1234567890abcdef", url);
        }

        [Fact]
        public void Local_fallback_qr_still_opens_the_host_directly()
        {
            var connectInfo = new ConnectInfo
            {
                UsesTrustedPublicUrl = false,
                PreferredUrl = "https://192.168.0.20:5443/"
            };

            var url = Startup.BuildOnboardingQrUrl(connectInfo);

            Assert.Equal("https://192.168.0.20:5443/index.html", url);
        }
    }
}
