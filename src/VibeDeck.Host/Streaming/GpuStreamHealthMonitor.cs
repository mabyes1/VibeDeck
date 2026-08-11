using System;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Detects sustained encoder under-performance. A single slow second is
    /// tolerated; three consecutive slow windows request the next lower tier.
    /// </summary>
    internal sealed class GpuStreamHealthMonitor
    {
        private readonly int targetFps;
        private readonly double minimumRatio;
        private bool initialized;
        private TimeSpan windowStartedAt;
        private int framesInWindow;
        private int slowWindows;
        private bool firstWindow = true;

        public GpuStreamHealthMonitor(int targetFps, double minimumRatio = 0.72)
        {
            this.targetFps = Math.Max(1, targetFps);
            this.minimumRatio = Math.Max(0.1, Math.Min(1, minimumRatio));
        }

        public bool RecordFrame(TimeSpan now)
        {
            if (!initialized)
            {
                initialized = true;
                windowStartedAt = now;
            }
            framesInWindow++;

            var elapsed = now - windowStartedAt;
            if (elapsed.TotalMilliseconds < 1000)
            {
                return false;
            }

            var actualFps = framesInWindow / Math.Max(0.001, elapsed.TotalSeconds);
            if (firstWindow)
            {
                firstWindow = false;
                slowWindows = 0;
            }
            else if (actualFps < targetFps * minimumRatio)
            {
                slowWindows++;
            }
            else
            {
                slowWindows = 0;
            }

            windowStartedAt = now;
            framesInWindow = 0;
            return slowWindows >= 3;
        }
    }
}
