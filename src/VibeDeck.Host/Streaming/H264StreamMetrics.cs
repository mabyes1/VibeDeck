using System;
using System.Threading;

namespace VibeDeck.Host.Streaming
{
    public sealed class H264StreamMetrics
    {
        private readonly object syncRoot = new object();
        private long nextOwnerId;
        private long activeOwnerId;
        private DateTimeOffset windowStartedAt = DateTimeOffset.UtcNow;
        private long windowBytes;
        private long windowFrames;
        private long windowSkippedFrames;
        private bool active;
        private DateTimeOffset? startedAt;
        private DateTimeOffset? endedAt;
        private int width;
        private int height;
        private int targetFps;
        private int targetQuality;
        private int targetBitrateKbps;
        private long encodedBytes;
        private long queuedFrames;
        private long skippedFrames;
        private double recentMbps;
        private double recentQueuedFps;
        private double recentSkippedFps;
        private string lastError;
        private string capturePath;
        private string encoder;
        private string qualityTier;
        private int downshiftCount;

        internal H264StreamMetricsLease Start(
            int width,
            int height,
            int targetFps,
            int targetQuality,
            int targetBitrateKbps,
            string capturePath = null,
            string encoder = null,
            string qualityTier = null,
            int downshiftCount = 0)
        {
            lock (syncRoot)
            {
                var ownerId = ++nextOwnerId;
                activeOwnerId = ownerId;
                active = true;
                startedAt = DateTimeOffset.UtcNow;
                endedAt = null;
                this.width = width;
                this.height = height;
                this.targetFps = targetFps;
                this.targetQuality = targetQuality;
                this.targetBitrateKbps = targetBitrateKbps;
                encodedBytes = 0;
                queuedFrames = 0;
                skippedFrames = 0;
                recentMbps = 0;
                recentQueuedFps = 0;
                recentSkippedFps = 0;
                lastError = null;
                this.capturePath = capturePath ?? "bitmap-pipe";
                this.encoder = encoder ?? string.Empty;
                this.qualityTier = qualityTier ?? "requested";
                this.downshiftCount = Math.Max(0, downshiftCount);
                ResetWindow();
                return new H264StreamMetricsLease(this, ownerId);
            }
        }

        internal void RecordEncodedBytes(long ownerId, int bytes)
        {
            lock (syncRoot)
            {
                if (ownerId != activeOwnerId) return;
                encodedBytes += Math.Max(0, bytes);
                windowBytes += Math.Max(0, bytes);
                UpdateWindowIfDue();
            }
        }

        internal void RecordQueuedFrame(long ownerId)
        {
            lock (syncRoot)
            {
                if (ownerId != activeOwnerId) return;
                queuedFrames++;
                windowFrames++;
                UpdateWindowIfDue();
            }
        }

        internal void RecordSkippedFrame(long ownerId)
        {
            lock (syncRoot)
            {
                if (ownerId != activeOwnerId) return;
                skippedFrames++;
                windowSkippedFrames++;
                UpdateWindowIfDue();
            }
        }

        internal void Stop(long ownerId, string error = null)
        {
            lock (syncRoot)
            {
                if (ownerId != activeOwnerId) return;
                UpdateWindowIfDue(force: true);
                active = false;
                activeOwnerId = 0;
                endedAt = DateTimeOffset.UtcNow;
                lastError = error;
            }
        }

        public H264StreamMetricsSnapshot GetSnapshot()
        {
            lock (syncRoot)
            {
                UpdateWindowIfDue();
                return new H264StreamMetricsSnapshot
                {
                    Active = active,
                    StartedAt = startedAt,
                    EndedAt = endedAt,
                    Width = width,
                    Height = height,
                    TargetFps = targetFps,
                    TargetQuality = targetQuality,
                    TargetBitrateKbps = targetBitrateKbps,
                    EncodedBytes = encodedBytes,
                    QueuedFrames = queuedFrames,
                    SkippedFrames = skippedFrames,
                    RecentMbps = Math.Round(recentMbps, 2),
                    RecentQueuedFps = Math.Round(recentQueuedFps, 1),
                    RecentSkippedFps = Math.Round(recentSkippedFps, 1),
                    LastError = lastError,
                    CapturePath = capturePath,
                    Encoder = encoder,
                    QualityTier = qualityTier,
                    DownshiftCount = downshiftCount
                };
            }
        }

        private void UpdateWindowIfDue(bool force = false)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - windowStartedAt;
            if (!force && elapsed.TotalMilliseconds < 1000)
            {
                return;
            }

            var seconds = Math.Max(0.001, elapsed.TotalSeconds);
            recentMbps = windowBytes * 8.0 / seconds / 1000.0 / 1000.0;
            recentQueuedFps = windowFrames / seconds;
            recentSkippedFps = windowSkippedFrames / seconds;
            ResetWindow(now);
        }

        private void ResetWindow()
        {
            ResetWindow(DateTimeOffset.UtcNow);
        }

        private void ResetWindow(DateTimeOffset now)
        {
            windowStartedAt = now;
            windowBytes = 0;
            windowFrames = 0;
            windowSkippedFrames = 0;
        }
    }

    internal sealed class H264StreamMetricsLease
    {
        private readonly H264StreamMetrics metrics;
        private readonly long ownerId;
        private int stopped;

        internal H264StreamMetricsLease(H264StreamMetrics metrics, long ownerId)
        {
            this.metrics = metrics;
            this.ownerId = ownerId;
        }

        internal void RecordEncodedBytes(int bytes)
        {
            metrics.RecordEncodedBytes(ownerId, bytes);
        }

        internal void RecordQueuedFrame()
        {
            metrics.RecordQueuedFrame(ownerId);
        }

        internal void RecordSkippedFrame()
        {
            metrics.RecordSkippedFrame(ownerId);
        }

        internal void Stop(string error = null)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            metrics.Stop(ownerId, error);
        }
    }

    public sealed class H264StreamMetricsSnapshot
    {
        public bool Active { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int TargetFps { get; set; }
        public int TargetQuality { get; set; }
        public int TargetBitrateKbps { get; set; }
        public long EncodedBytes { get; set; }
        public long QueuedFrames { get; set; }
        public long SkippedFrames { get; set; }
        public double RecentMbps { get; set; }
        public double RecentQueuedFps { get; set; }
        public double RecentSkippedFps { get; set; }
        public string LastError { get; set; }
        public string CapturePath { get; set; }
        public string Encoder { get; set; }
        public string QualityTier { get; set; }
        public int DownshiftCount { get; set; }
    }
}
