using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class H264FrameCadencePlannerTests
    {
        [Fact]
        public void ThirtyFps_UsesFixedRtpDurationTicks()
        {
            var planner = new H264FrameCadencePlanner(30);

            Assert.Equal((uint)3000, planner.DurationTicks);
            Assert.InRange(planner.FrameInterval.TotalMilliseconds, 33.3, 33.4);
        }

        [Fact]
        public void BurstAccessUnits_AreSpacedByNominalDeadline()
        {
            var planner = new H264FrameCadencePlanner(30);

            Assert.Equal(System.TimeSpan.Zero, planner.DelayUntilNextFrame(System.TimeSpan.Zero));
            var secondDelay = planner.DelayUntilNextFrame(System.TimeSpan.FromMilliseconds(1));
            var thirdDelay = planner.DelayUntilNextFrame(System.TimeSpan.FromMilliseconds(33.4));

            Assert.InRange(secondDelay.TotalMilliseconds, 32.2, 32.4);
            Assert.InRange(thirdDelay.TotalMilliseconds, 33.2, 33.4);
        }

        [Fact]
        public void LateSource_ResetsBaselineInsteadOfCatchingUp()
        {
            var planner = new H264FrameCadencePlanner(30);

            planner.DelayUntilNextFrame(System.TimeSpan.Zero);
            planner.DelayUntilNextFrame(System.TimeSpan.FromMilliseconds(1));

            var lateDelay = planner.DelayUntilNextFrame(System.TimeSpan.FromSeconds(1));
            var nextDelay = planner.DelayUntilNextFrame(System.TimeSpan.FromMilliseconds(1000.5));

            Assert.Equal(System.TimeSpan.Zero, lateDelay);
            Assert.InRange(nextDelay.TotalMilliseconds, 32.7, 33.4);
        }
    }
}
