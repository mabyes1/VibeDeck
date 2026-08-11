using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace VibeDeck.Host.Streaming
{
    internal static class H264WebRtcRelay
    {
        public static async Task<int> RelayAsync(
            Stream output,
            RTCPeerConnection peer,
            int h264PayloadTypeId,
            int fps,
            H264StreamMetricsLease metrics,
            bool recordEncodedFrames,
            Func<TimeSpan, bool> shouldDownshift,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[32 * 1024];
            var accessUnits = new H264AccessUnitAssembler();
            var cadence = new H264FrameCadencePlanner(fps);
            var clock = Stopwatch.StartNew();
            var framesSent = 0;

            while (!cancellationToken.IsCancellationRequested && peer.connectionState == RTCPeerConnectionState.connected)
            {
                var read = await output.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read <= 0)
                {
                    return framesSent;
                }
                metrics.RecordEncodedBytes(read);

                foreach (var accessUnit in accessUnits.Append(buffer, read))
                {
                    if (peer.connectionState != RTCPeerConnectionState.connected)
                    {
                        return framesSent;
                    }

                    // Send each encoded access unit as soon as it is ready. The
                    // encoder/capture pipeline already establishes the source
                    // cadence; waiting here adds a stable one-frame feedback
                    // delay on interactive pointer movement. Keep RTP timing
                    // fixed so removing this wait does not reintroduce the old
                    // variable-duration timestamps.
                    peer.VideoStream.SendH264Frame(cadence.DurationTicks, h264PayloadTypeId, accessUnit);
                    framesSent++;
                    if (recordEncodedFrames)
                    {
                        metrics.RecordQueuedFrame();
                    }

                    if (shouldDownshift?.Invoke(clock.Elapsed) == true)
                    {
                        throw new GpuStreamDownshiftException();
                    }
                }
            }

            return framesSent;
        }
    }

    internal sealed class GpuStreamDownshiftException : Exception
    {
        public GpuStreamDownshiftException()
            : base("GPU capture could not sustain the target frame rate.")
        {
        }
    }
}
