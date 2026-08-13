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
            H264WebRtcTransport transport,
            int h264PayloadTypeId,
            int fps,
            int targetBitrateKbps,
            H264StreamMetricsLease metrics,
            bool recordEncodedFrames,
            Func<TimeSpan, bool> shouldDownshift,
            bool canDownshift,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[32 * 1024];
            var accessUnits = new H264AccessUnitAssembler();
            var cadence = new H264FrameCadencePlanner(fps);
            var clock = Stopwatch.StartNew();
            var framesSent = 0;
            transport.SetTargetBitrate(targetBitrateKbps);

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

                    // Keep RTP timestamps fixed to the requested frame cadence.
                    // The transport applies a small token-bucket pace inside
                    // each access unit so a large IDR does not hit Wi-Fi as one
                    // packet burst, while retaining a short interactive burst
                    // allowance for pointer-driven desktop updates.
                    await transport.SendAccessUnitAsync(
                        cadence.DurationTicks,
                        h264PayloadTypeId,
                        accessUnit,
                        cancellationToken).ConfigureAwait(false);
                    framesSent++;
                    if (recordEncodedFrames)
                    {
                        metrics.RecordQueuedFrame();
                    }

                    if (shouldDownshift?.Invoke(clock.Elapsed) == true)
                    {
                        throw new GpuStreamDownshiftException();
                    }

                    var recoveryAction = transport.ConsumeRecoveryAction(canDownshift);
                    if (recoveryAction == H264RecoveryAction.Downshift)
                    {
                        throw new GpuStreamDownshiftException("WebRTC packet loss requires a lower bitrate tier.");
                    }
                    if (recoveryAction == H264RecoveryAction.RestartForKeyFrame)
                    {
                        throw new H264KeyFrameRequestException();
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

        public GpuStreamDownshiftException(string message)
            : base(message)
        {
        }
    }

    internal sealed class H264KeyFrameRequestException : Exception
    {
        public H264KeyFrameRequestException()
            : base("The receiver requested a fresh H.264 key frame.")
        {
        }
    }
}
