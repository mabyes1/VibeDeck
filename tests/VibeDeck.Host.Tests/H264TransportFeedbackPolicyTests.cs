using System;
using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class H264TransportFeedbackPolicyTests
    {
        [Fact]
        public void ExpandNack_IncludesPidAndBitmaskSequenceNumbersAcrossWrap()
        {
            var packets = H264TransportFeedbackPolicy.ExpandNack(ushort.MaxValue, 0b0000_0000_0000_0101);

            Assert.Equal(new ushort[] { ushort.MaxValue, 0, 2 }, packets);
        }

        [Fact]
        public void MissingCachedPacket_RequestsImmediateKeyFrame()
        {
            var now = DateTimeOffset.Parse("2026-08-13T00:00:00Z");
            var policy = new H264TransportFeedbackPolicy(() => now);

            policy.ObserveNack(requestedPackets: 1, recoveredPackets: 0);

            Assert.Equal(H264RecoveryAction.RestartForKeyFrame, policy.ConsumeAction(canDownshift: true));
        }

        [Fact]
        public void NackBurst_DownshiftsButSingleRecoveredLossDoesNot()
        {
            var now = DateTimeOffset.Parse("2026-08-13T00:00:00Z");
            var policy = new H264TransportFeedbackPolicy(() => now);

            policy.ObserveNack(requestedPackets: 1, recoveredPackets: 1);
            Assert.Equal(H264RecoveryAction.None, policy.ConsumeAction(canDownshift: true));

            policy.ObserveNack(requestedPackets: 10, recoveredPackets: 10);
            Assert.Equal(H264RecoveryAction.Downshift, policy.ConsumeAction(canDownshift: true));
        }

        [Fact]
        public void LastTier_IgnoresFurtherDownshiftInsteadOfRestartingEncoder()
        {
            var policy = new H264TransportFeedbackPolicy();
            policy.ObserveNack(requestedPackets: 10, recoveredPackets: 10);

            Assert.Equal(H264RecoveryAction.None, policy.ConsumeAction(canDownshift: false));
        }

        [Fact]
        public void SustainedReceiverLoss_DownshiftsAfterTwoReports()
        {
            var policy = new H264TransportFeedbackPolicy();

            policy.ObserveLossFraction(0.05);
            Assert.Equal(H264RecoveryAction.None, policy.ConsumeAction(canDownshift: true));
            policy.ObserveLossFraction(0.04);

            Assert.Equal(H264RecoveryAction.Downshift, policy.ConsumeAction(canDownshift: true));
        }
    }
}
