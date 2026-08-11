using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class H264StreamMetricsTests
    {
        [Fact]
        public void StaleSessionCannotRecordIntoOrStopNewerSessionMetrics()
        {
            var metrics = new H264StreamMetrics();
            var oldSession = metrics.Start(
                width: 800,
                height: 450,
                targetFps: 30,
                targetQuality: 40,
                targetBitrateKbps: 1200,
                capturePath: "old-session");
            oldSession.RecordEncodedBytes(50);
            oldSession.RecordQueuedFrame();

            var newSession = metrics.Start(
                width: 1024,
                height: 576,
                targetFps: 30,
                targetQuality: 48,
                targetBitrateKbps: 2542,
                capturePath: "new-session");
            newSession.RecordEncodedBytes(100);
            newSession.RecordQueuedFrame();

            oldSession.RecordEncodedBytes(5000);
            oldSession.RecordQueuedFrame();
            oldSession.Stop("old session disconnected");

            var active = metrics.GetSnapshot();
            Assert.True(active.Active);
            Assert.Null(active.EndedAt);
            Assert.Null(active.LastError);
            Assert.Equal(1024, active.Width);
            Assert.Equal(576, active.Height);
            Assert.Equal(2542, active.TargetBitrateKbps);
            Assert.Equal("new-session", active.CapturePath);
            Assert.Equal(100, active.EncodedBytes);
            Assert.Equal(1, active.QueuedFrames);

            newSession.Stop();
            var stopped = metrics.GetSnapshot();
            Assert.False(stopped.Active);
            Assert.NotNull(stopped.EndedAt);
        }

        [Fact]
        public void MetricsLeaseStopIsIdempotent()
        {
            var metrics = new H264StreamMetrics();
            var session = metrics.Start(1024, 576, 30, 48, 2542);

            session.Stop("first");
            var endedAt = metrics.GetSnapshot().EndedAt;
            session.Stop("second");

            var snapshot = metrics.GetSnapshot();
            Assert.Equal(endedAt, snapshot.EndedAt);
            Assert.Equal("first", snapshot.LastError);
        }
    }
}
