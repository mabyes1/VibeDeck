using System;
using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class GpuStreamHealthMonitorTests
    {
        [Fact]
        public void RecordFrame_DownshiftsOnlyAfterThreeSlowWindows()
        {
            var monitor = new GpuStreamHealthMonitor(60);

            Assert.False(monitor.RecordFrame(TimeSpan.Zero));
            Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(1.1))); // Warm-up window.
            Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(2.2)));
            Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(3.3)));
            Assert.True(monitor.RecordFrame(TimeSpan.FromSeconds(4.4)));
        }

        [Fact]
        public void RecordFrame_HealthyWindowClearsSlowStreak()
        {
            var monitor = new GpuStreamHealthMonitor(10, minimumRatio: 0.7);

            Assert.False(monitor.RecordFrame(TimeSpan.Zero));
            Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(1.1)));
            Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(2.2)));
            Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(3.3)));

            for (var index = 1; index <= 10; index++)
            {
                Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(3.3 + index * 0.1)));
            }

            Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(5.5)));
            Assert.False(monitor.RecordFrame(TimeSpan.FromSeconds(6.6)));
            Assert.True(monitor.RecordFrame(TimeSpan.FromSeconds(7.7)));
        }
    }
}
