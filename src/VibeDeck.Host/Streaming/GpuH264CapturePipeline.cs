using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VibeDeck.Host.Windows;
using SIPSorcery.Net;

namespace VibeDeck.Host.Streaming
{
    public interface IGpuH264CapturePipeline
    {
        bool IsAvailable { get; }
        string PreferredEncoderName { get; }

        Task<bool> TryStreamAsync(
            RTCPeerConnection peer,
            H264WebRtcTransport transport,
            int h264PayloadTypeId,
            string deviceName,
            int requestedFps,
            int requestedQuality,
            int receiverMaxBitrateKbps,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Optional D3D11 capture -> hardware H.264 path. It owns no compatibility
    /// behavior; returning false tells the caller to use the established bitmap
    /// pipeline.
    /// </summary>
    internal sealed class GpuH264CapturePipeline : IGpuH264CapturePipeline
    {
        private readonly DisplayCatalog displays;
        private readonly FfmpegGpuCaptureRuntime runtime;
        private readonly H264StreamMetrics metrics;

        public GpuH264CapturePipeline(
            DisplayCatalog displays,
            FfmpegGpuCaptureRuntime runtime,
            H264StreamMetrics metrics)
        {
            this.displays = displays;
            this.runtime = runtime;
            this.metrics = metrics;
        }

        public bool IsAvailable => runtime.IsAvailable;

        public string PreferredEncoderName => runtime.HardwareEncoders.FirstOrDefault();

        public async Task<bool> TryStreamAsync(
            RTCPeerConnection peer,
            H264WebRtcTransport transport,
            int h264PayloadTypeId,
            string deviceName,
            int requestedFps,
            int requestedQuality,
            int receiverMaxBitrateKbps,
            CancellationToken cancellationToken)
        {
            if (!runtime.IsAvailable)
            {
                return false;
            }

            var display = displays.GetDisplays()
                .FirstOrDefault(item => string.Equals(item.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                ?? displays.GetDisplays().FirstOrDefault(item => item.IsVibeDeckDisplay);
            if (display == null || display.Width <= 0 || display.Height <= 0)
            {
                return false;
            }
            // FFmpeg ddagrab uses the DXGI output index of its default adapter.
            // A Win32 monitor-enumeration index is unrelated and can capture a
            // different screen. Secondary adapters safely use bitmap fallback
            // until the runtime explicitly selects their D3D11 device.
            if (display.AdapterIndex != 0 || display.OutputIndex < 0)
            {
                return false;
            }

            var dimensions = GpuStreamQualityLadder.FitWithin(display.Width, display.Height);
            var profiles = GpuStreamQualityLadder.Create(requestedFps, requestedQuality);
            Exception lastError = null;

            foreach (var encoder in runtime.HardwareEncoders)
            {
                for (var profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
                {
                    var profile = profiles[profileIndex];
                    var canDownshift = profileIndex + 1 < profiles.Count;
                    var keyFrameRestarts = 0;
                    try
                    {
                        while (true)
                        {
                            try
                            {
                                await RunAttemptAsync(
                                    peer,
                                    transport,
                                    h264PayloadTypeId,
                                    display.DeviceName,
                                    display.OutputIndex,
                                    dimensions.Width,
                                    dimensions.Height,
                                    encoder,
                                    profile,
                                    profileIndex,
                                    canDownshift,
                                    receiverMaxBitrateKbps,
                                    cancellationToken);
                                break;
                            }
                            catch (H264KeyFrameRequestException)
                            {
                                keyFrameRestarts++;
                                Console.Error.WriteLine(
                                    $"[H264] restarting encoder for PLI: encoder={encoder} tier={profile.Name}");
                            }
                        }
                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (GpuStreamDownshiftException error) when (canDownshift)
                    {
                        lastError = error;
                        Console.Error.WriteLine(
                            $"[H264] GPU capture downshift: encoder={encoder} tier={profile.Name} fps={profile.Fps}");
                    }
                    catch (Exception error)
                    {
                        lastError = error;
                        Console.Error.WriteLine(
                            $"[H264] GPU capture unavailable for encoder={encoder}: {error.Message}");
                        break;
                    }
                }
            }

            if (lastError != null)
            {
                Console.Error.WriteLine($"[H264] falling back to bitmap capture: {lastError.Message}");
            }
            return false;
        }

        private async Task RunAttemptAsync(
            RTCPeerConnection peer,
            H264WebRtcTransport transport,
            int h264PayloadTypeId,
            string deviceName,
            int outputIndex,
            int width,
            int height,
            string encoder,
            GpuStreamQualityProfile profile,
            int downshiftCount,
            bool canDownshift,
            int receiverMaxBitrateKbps,
            CancellationToken cancellationToken)
        {
            Process process = null;
            Task<string> errorTask = null;
            string stopError = null;
            H264StreamMetricsLease metricsLease = null;
            try
            {
                var bitrateKbps = GpuStreamQualityLadder.ApplyReceiverLimit(
                    GpuStreamQualityLadder.EstimateBitrateKbps(
                        width,
                        height,
                        profile.Fps,
                        profile.Quality),
                    receiverMaxBitrateKbps,
                    profile.BitrateScale);
                process = runtime.Start(
                    encoder,
                    outputIndex,
                    width,
                    height,
                    profile.Fps,
                    bitrateKbps);
                errorTask = FfmpegGpuCaptureRuntime.ReadErrorTailAsync(process);
                metricsLease = metrics.Start(
                    width,
                    height,
                    profile.Fps,
                    profile.Quality,
                    bitrateKbps,
                    "d3d11-gpu",
                    encoder,
                    profile.Name,
                    downshiftCount,
                    deviceName);

                var health = canDownshift ? new GpuStreamHealthMonitor(profile.Fps) : null;
                Func<TimeSpan, bool> healthCheck = health == null ? null : health.RecordFrame;
                var framesSent = await H264WebRtcRelay.RelayAsync(
                    process.StandardOutput.BaseStream,
                    peer,
                    transport,
                    h264PayloadTypeId,
                    profile.Fps,
                    bitrateKbps,
                    metricsLease,
                    recordEncodedFrames: true,
                    healthCheck,
                    canDownshift,
                    cancellationToken);

                if (!cancellationToken.IsCancellationRequested &&
                    peer.connectionState == RTCPeerConnectionState.connected)
                {
                    var error = errorTask != null ? await errorTask : string.Empty;
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error)
                            ? $"FFmpeg GPU capture ended after {framesSent} frames."
                            : error);
                }
            }
            catch (Exception error)
            {
                stopError = error.Message;
                throw;
            }
            finally
            {
                metricsLease?.Stop(stopError);
                if (process != null)
                {
                    TryKill(process);
                    process.Dispose();
                }

                if (errorTask != null)
                {
                    try
                    {
                        await errorTask;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1500);
                }
            }
            catch
            {
            }
        }
    }
}
