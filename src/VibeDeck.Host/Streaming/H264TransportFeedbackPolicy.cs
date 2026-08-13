using System;
using System.Collections.Generic;

namespace VibeDeck.Host.Streaming
{
    internal enum H264RecoveryAction
    {
        None = 0,
        RestartForKeyFrame = 1,
        Downshift = 2
    }

    /// <summary>
    /// Small, deterministic loss controller for the raw H.264 sender.  The
    /// encoder cannot change rate in-place, so feedback is converted into one
    /// of two safe actions: restart the current tier for an immediate IDR, or
    /// restart on the next lower bitrate tier after sustained congestion.
    /// </summary>
    internal sealed class H264TransportFeedbackPolicy
    {
        private static readonly TimeSpan NackWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DownshiftCooldown = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan KeyFrameCooldown = TimeSpan.FromMilliseconds(850);
        private readonly object sync = new object();
        private readonly Func<DateTimeOffset> utcNow;
        private DateTimeOffset nackWindowStarted;
        private DateTimeOffset lastDownshiftAt = DateTimeOffset.MinValue;
        private DateTimeOffset lastKeyFrameAt = DateTimeOffset.MinValue;
        private int nackRequests;
        private int congestedReports;
        private H264RecoveryAction pendingAction;

        public H264TransportFeedbackPolicy(Func<DateTimeOffset> utcNow = null)
        {
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            nackWindowStarted = this.utcNow();
        }

        public void ObserveNack(int requestedPackets, int recoveredPackets)
        {
            if (requestedPackets <= 0)
            {
                return;
            }

            lock (sync)
            {
                var now = utcNow();
                ResetNackWindowIfNeeded(now);
                nackRequests += requestedPackets;

                // A packet outside the retransmit window cannot be recovered;
                // ask the encoder for a fresh reference picture immediately.
                if (recoveredPackets < requestedPackets)
                {
                    RequestKeyFrameCore(now);
                }

                // A burst or repeated NACKs means the fixed rate is above what
                // this path can currently sustain. One isolated lost packet is
                // handled by retransmission without changing picture quality.
                if (nackRequests >= 10)
                {
                    RequestDownshiftCore(now);
                    nackRequests = 0;
                    nackWindowStarted = now;
                }
            }
        }

        public void ObserveLossFraction(double fractionLost)
        {
            if (double.IsNaN(fractionLost) || double.IsInfinity(fractionLost))
            {
                return;
            }

            lock (sync)
            {
                var now = utcNow();
                if (fractionLost >= 0.035)
                {
                    congestedReports++;
                    if (congestedReports >= 2)
                    {
                        RequestDownshiftCore(now);
                        congestedReports = 0;
                    }
                }
                else if (fractionLost <= 0.01)
                {
                    congestedReports = Math.Max(0, congestedReports - 1);
                }
            }
        }

        public void RequestKeyFrame()
        {
            lock (sync)
            {
                RequestKeyFrameCore(utcNow());
            }
        }

        public H264RecoveryAction ConsumeAction(bool canDownshift)
        {
            lock (sync)
            {
                var action = pendingAction;
                pendingAction = H264RecoveryAction.None;
                if (action == H264RecoveryAction.Downshift && !canDownshift)
                {
                    // At the last tier there is nowhere lower to go. Recovered
                    // NACK traffic is not a reason to restart the encoder: that
                    // restart itself creates a visible freeze. Missing-cache
                    // NACKs and explicit PLI use the key-frame action instead.
                    return H264RecoveryAction.None;
                }
                return action;
            }
        }

        internal static IReadOnlyList<ushort> ExpandNack(ushort packetId, ushort bitmask)
        {
            var sequenceNumbers = new List<ushort>(17) { packetId };
            for (var bit = 0; bit < 16; bit++)
            {
                if ((bitmask & (1 << bit)) != 0)
                {
                    sequenceNumbers.Add(unchecked((ushort)(packetId + bit + 1)));
                }
            }
            return sequenceNumbers;
        }

        private void RequestKeyFrameCore(DateTimeOffset now)
        {
            if (now - lastKeyFrameAt < KeyFrameCooldown)
            {
                return;
            }
            lastKeyFrameAt = now;
            if (pendingAction < H264RecoveryAction.RestartForKeyFrame)
            {
                pendingAction = H264RecoveryAction.RestartForKeyFrame;
            }
        }

        private void RequestDownshiftCore(DateTimeOffset now)
        {
            if (now - lastDownshiftAt < DownshiftCooldown)
            {
                return;
            }
            lastDownshiftAt = now;
            pendingAction = H264RecoveryAction.Downshift;
        }

        private void ResetNackWindowIfNeeded(DateTimeOffset now)
        {
            if (now - nackWindowStarted < NackWindow)
            {
                return;
            }
            nackWindowStarted = now;
            nackRequests = 0;
        }
    }
}
