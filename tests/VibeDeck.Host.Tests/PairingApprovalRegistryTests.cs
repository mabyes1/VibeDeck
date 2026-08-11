using System;

using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class PairingApprovalRegistryTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T00:00:00Z");

        [Fact]
        public void RequestReusesPendingRequestForSameStableClientInstance()
        {
            var registry = new PairingApprovalRegistry();

            var first = registry.Request("Phone", "web", "", "1234567890abcdef", "ua-1", "10.0.0.2", Now);
            var second = registry.Request("Changed", "web", "", "1234567890abcdef", "ua-2", "10.0.0.3", Now.AddMinutes(1));

            Assert.Equal(first.RequestId, second.RequestId);
            Assert.Equal(first.RequestSecret, second.RequestSecret);
        }

        [Fact]
        public void RequestWithoutStableIdReusesOnlyExactAddressAndUserAgent()
        {
            var registry = new PairingApprovalRegistry();

            var first = registry.Request("Phone", "web", "", "", "same-agent", "10.0.0.2", Now);
            var same = registry.Request("Phone", "web", "", "", "same-agent", "10.0.0.2", Now.AddSeconds(1));
            var other = registry.Request("Phone", "web", "", "", "same-agent", "10.0.0.3", Now.AddSeconds(2));

            Assert.Equal(first.RequestId, same.RequestId);
            Assert.NotEqual(first.RequestId, other.RequestId);
        }

        [Fact]
        public void PollFailsClosedForWrongSecretAndExpiredRequest()
        {
            var registry = new PairingApprovalRegistry();
            var request = registry.Request("Phone", "web", "", "", "ua", "10.0.0.2", Now);

            Assert.False(registry.Poll(request.RequestId, "wrong", Now.AddMinutes(1)).Success);
            Assert.False(registry.Poll(request.RequestId, request.RequestSecret, Now.AddMinutes(11)).Success);
        }

        [Fact]
        public void ApprovedRequestReturnsCredentialsOnce()
        {
            var registry = new PairingApprovalRegistry();
            var created = registry.Request("Phone", "web", "", "", "ua", "10.0.0.2", Now);
            Assert.True(registry.TryGetPending(created.RequestId, Now.AddMinutes(1), out var request));

            registry.MarkApproved(request, "device-1", "token-1", continued: true);
            var result = registry.Poll(created.RequestId, created.RequestSecret, Now.AddMinutes(2));

            Assert.True(result.Success);
            Assert.Equal("approved", result.Status);
            Assert.Equal("device-1", result.DeviceId);
            Assert.Equal("token-1", result.DeviceToken);
            Assert.True(result.Continued);
            Assert.False(registry.Poll(created.RequestId, created.RequestSecret, Now.AddMinutes(3)).Success);
        }
    }
}
