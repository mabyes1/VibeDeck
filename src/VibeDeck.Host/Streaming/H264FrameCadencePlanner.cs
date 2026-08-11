using System;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Plans a fixed nominal frame cadence for an encoded stream. If an
    /// access unit arrives after its deadline, the baseline is reset instead
    /// of sending several late frames back-to-back.
    /// </summary>
    internal sealed class H264FrameCadencePlanner
    {
        private readonly TimeSpan frameInterval;
        private TimeSpan nextDeadline;
        private bool initialized;

        public H264FrameCadencePlanner(int fps)
        {
            var safeFps = Math.Max(1, fps);
            frameInterval = TimeSpan.FromSeconds(1d / safeFps);
            DurationTicks = (uint)Math.Max(1, 90000 / safeFps);
        }

        public uint DurationTicks { get; }

        internal TimeSpan FrameInterval => frameInterval;

        public TimeSpan DelayUntilNextFrame(TimeSpan monotonicNow)
        {
            if (!initialized)
            {
                initialized = true;
                nextDeadline = monotonicNow + frameInterval;
                return TimeSpan.Zero;
            }

            if (monotonicNow < nextDeadline)
            {
                var delay = nextDeadline - monotonicNow;
                nextDeadline += frameInterval;
                return delay;
            }

            // The source/encoder is late. Send this frame now, then establish
            // a new baseline so queued access units cannot create a catch-up
            // burst.
            nextDeadline = monotonicNow + frameInterval;
            return TimeSpan.Zero;
        }
    }
}
