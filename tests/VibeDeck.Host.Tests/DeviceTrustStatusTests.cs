using System;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class DeviceTrustStatusTests
    {
        [Fact]
        public void DeviceSummary_marks_recent_activity_as_connected()
        {
            var now = DateTimeOffset.UtcNow;
            var summary = DeviceSummary.From(new TrustedDeviceRecord
            {
                DeviceId = "device-1",
                Name = "BOOX",
                LastSeenAt = now.AddSeconds(-10)
            }, now);

            Assert.True(summary.Connected);
        }

        [Fact]
        public void DeviceSummary_marks_stale_activity_as_disconnected()
        {
            var now = DateTimeOffset.UtcNow;
            var summary = DeviceSummary.From(new TrustedDeviceRecord
            {
                DeviceId = "device-2",
                Name = "iPhone",
                LastSeenAt = now.AddMinutes(-1)
            }, now);

            Assert.False(summary.Connected);
        }
    }
}
