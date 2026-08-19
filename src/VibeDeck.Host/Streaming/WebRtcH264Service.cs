using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VibeDeck.Host.Diagnostics;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Owns browser WebRTC sessions for the virtual display. Signalling is a
    /// single HTTPS offer/answer request; media uses the best nominated
    /// DTLS/SRTP path (direct when possible, TURN relay when configured).
    /// </summary>
    public sealed class WebRtcH264Service
    {
        private readonly H264AnnexBStreamer h264;
        private readonly CloudflareTurnCredentialService turnCredentials;
        private readonly AuditTrailService audit;
        private readonly ConcurrentDictionary<Guid, WebRtcSession> sessions = new ConcurrentDictionary<Guid, WebRtcSession>();
        private readonly object sessionRegistrationSync = new object();

        public WebRtcH264Service(
            H264AnnexBStreamer h264,
            CloudflareTurnCredentialService turnCredentials,
            AuditTrailService audit)
        {
            this.h264 = h264;
            this.turnCredentials = turnCredentials;
            this.audit = audit;
        }

        public bool IsAvailable => h264.IsAvailable;

        public async Task<WebRtcOfferAnswer> CreateAnswerAsync(
            string offerSdp,
            string deviceName,
            int fps,
            int quality,
            CancellationToken cancellationToken = default,
            string trustedDeviceId = null,
            int receiverMaxBitrateKbps = 0,
            int playoutDelayMs = H264PlayoutDelayExtension.DefaultMaximumDelayMs)
        {
            if (string.IsNullOrWhiteSpace(offerSdp))
            {
                throw new ArgumentException("WebRTC offer SDP is required.", nameof(offerSdp));
            }

            IceServerConfiguration iceConfiguration;
            try
            {
                iceConfiguration = await turnCredentials.CreateIceServersAsync(
                    "host-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                    cancellationToken);
            }
            catch (TurnCredentialException error)
            {
                iceConfiguration = turnCredentials.GetStunOnlyConfiguration();
                iceConfiguration.TurnConfigured = true;
                iceConfiguration.Warning = error.Code;
                audit.Record(
                    "warning",
                    "stream",
                    "turn-host-credentials",
                    "fallback-stun",
                    subject: deviceName,
                    details: new Dictionary<string, string> { ["code"] = error.Code });
            }

            var configuration = new RTCConfiguration
            {
                // Include local addresses for LAN use, and STUN/TURN candidates
                // for cross-network clients. TURN credentials are short lived.
                X_ICEIncludeAllInterfaceAddresses = true,
                X_UseRtpFeedbackProfile = true,
                iceServers = ToSipsIceServers(iceConfiguration.IceServers)
            };
            var peer = new RTCPeerConnection(configuration);
            var track = new MediaStreamTrack(
                SDPMediaTypesEnum.video,
                false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(new VideoFormat(
                        VideoCodecsEnum.H264,
                        102,
                        90000,
                        H264WebRtcCodecContract.SdpFormatParameters))
                },
                MediaStreamStatusEnum.SendOnly);
            peer.addTrack(track);

            var session = new WebRtcSession(
                Guid.NewGuid(),
                peer,
                deviceName,
                fps,
                quality,
                iceConfiguration.TurnAvailable ? "turn-ready" : "direct-stun",
                trustedDeviceId,
                receiverMaxBitrateKbps,
                playoutDelayMs);
            peer.onconnectionstatechange += state => OnConnectionStateChanged(session, state);
            peer.oniceconnectionstatechange += state =>
            {
                Console.Error.WriteLine($"[WebRTC] {session.Id} ice={state} device={session.DeviceName}");
                session.LastIceState = state.ToString();
                RecordSessionEvent(session, "ice-state", state.ToString(), state == RTCIceConnectionState.failed ? "warning" : "information");
                if (state == RTCIceConnectionState.failed || state == RTCIceConnectionState.closed)
                {
                    CloseSession(session, "ICE connection closed");
                }
            };

            var result = peer.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = offerSdp
            });
            if (result != SetDescriptionResultEnum.OK)
            {
                session.Transport.Dispose();
                peer.Close("Invalid WebRTC offer.");
                throw new InvalidOperationException($"WebRTC offer rejected: {result}.");
            }

            var playoutDelayExtensionId = 0;
            if (H264PlayoutDelayExtension.TryGetOfferedId(offerSdp, out playoutDelayExtensionId))
            {
                // SIPSorcery 10.0.9 ignores Chrome's playout-delay extmap while
                // parsing the offer. Register it on the sending track ourselves;
                // AddToAnswer below completes the negotiation in the SDP answer.
                track.HeaderExtensions[playoutDelayExtensionId] =
                    new H264PlayoutDelayExtension(playoutDelayExtensionId, session.PlayoutDelayMs);
            }

            RegisterReplacingDeviceSessions(session);
            try
            {
                var answer = peer.createAnswer(null);
                await peer.setLocalDescription(answer);
                // SIPSorcery gathers host ICE candidates asynchronously.  Returning
                // the SDP created above can therefore omit the server candidate and
                // leaves Safari stuck in "checking" before it ever receives H.264.
                // Wait briefly, then return the actual local description populated
                // by setLocalDescription.
                await WaitForIceGatheringAsync(peer, 1500);
                RecordSessionEvent(session, "created", "ready");
                var answerSdp = peer.localDescription != null
                    ? peer.localDescription.sdp.ToString()
                    : answer.sdp;

                if (playoutDelayExtensionId > 0)
                {
                    answerSdp = H264PlayoutDelayExtension.AddToAnswer(
                        answerSdp,
                        playoutDelayExtensionId);
                }

                return new WebRtcOfferAnswer
                {
                    Type = "answer",
                    Sdp = H264RtcpFeedbackNegotiator.AddLossRecoveryFeedback(offerSdp, answerSdp)
                };
            }
            catch
            {
                CloseSession(session, "WebRTC answer failed");
                throw;
            }
        }

        private static async Task WaitForIceGatheringAsync(RTCPeerConnection peer, int timeoutMs)
        {
            if (peer.iceGatheringState == RTCIceGatheringState.complete)
            {
                return;
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<RTCIceGatheringState> handler = state =>
            {
                if (state == RTCIceGatheringState.complete)
                {
                    completion.TrySetResult(true);
                }
            };
            peer.onicegatheringstatechange += handler;
            try
            {
                // Gathering may complete between the initial check and the
                // subscription; re-check so we do not wait out the full timeout.
                if (peer.iceGatheringState == RTCIceGatheringState.complete)
                {
                    completion.TrySetResult(true);
                }

                // Do not Dispose() the delay task: if ICE finishes first the
                // delay is still running, and disposing an incomplete Task
                // throws and aborts /api/stream/webrtc/offer with HTTP 500.
                await Task.WhenAny(completion.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            }
            finally
            {
                peer.onicegatheringstatechange -= handler;
            }
        }

        private void OnConnectionStateChanged(WebRtcSession session, RTCPeerConnectionState state)
        {
            Console.Error.WriteLine($"[WebRTC] {session.Id} connection={state} device={session.DeviceName}");
            session.LastConnectionState = state.ToString();
            RecordSessionEvent(session, "connection-state", state.ToString(),
                state == RTCPeerConnectionState.failed ? "warning" : "information");
            if (state == RTCPeerConnectionState.connected)
            {
                Interlocked.Increment(ref session.DisconnectedGeneration);
                if (Interlocked.Exchange(ref session.StreamStarted, 1) == 0)
                {
                    _ = Task.Run(() => StreamSessionAsync(session));
                }
                return;
            }

            if (state == RTCPeerConnectionState.disconnected)
            {
                ScheduleDisconnectedCleanup(session);
                return;
            }

            // A disconnected state is often transient on mobile Wi-Fi.  Keep
            // the peer alive long enough for ICE to nominate a replacement
            // pair; the browser-side grace period mirrors this behaviour.
            if (state == RTCPeerConnectionState.failed ||
                state == RTCPeerConnectionState.closed)
            {
                CloseSession(session, $"WebRTC {state}");
            }
        }

        public WebRtcDiagnosticsSnapshot GetDiagnostics()
        {
            var active = sessions.Values
                .OrderByDescending(session => session.CreatedAt)
                .Select(session => new WebRtcSessionDiagnostics
                {
                    SessionId = session.Id.ToString("N"),
                    DeviceName = session.DeviceName,
                    CreatedAt = session.CreatedAt.ToString("O"),
                    ConnectionState = session.LastConnectionState,
                    IceState = session.LastIceState,
                    TransportPlan = session.TransportPlan,
                    ReceiverMaxBitrateKbps = session.ReceiverMaxBitrateKbps,
                    PlayoutDelayMs = session.PlayoutDelayMs,
                    Transport = session.Transport.GetSnapshot()
                })
                .ToArray();
            return new WebRtcDiagnosticsSnapshot
            {
                GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
                ActiveSessions = active
            };
        }

        private async Task StreamSessionAsync(WebRtcSession session)
        {
            try
            {
                await h264.StreamToWebRtcAsync(
                    session.Peer,
                    session.Transport,
                    session.DeviceName,
                    session.Fps,
                    session.Quality,
                    session.ReceiverMaxBitrateKbps,
                    session.Cancellation.Token);
            }
            catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[WebRTC] {session.Id} stream error: {error}");
                CloseSession(session, "H.264 stream failed");
            }
        }

        /// <summary>
        /// Tears down every video stream belonging to a device. Called when the device is
        /// revoked: dropping it from the trust store does not by itself stop a stream that
        /// is already running, so the screen would keep being sent to a revoked phone.
        /// </summary>
        public int CloseSessionsForDevice(string trustedDeviceId)
        {
            if (string.IsNullOrEmpty(trustedDeviceId))
            {
                return 0;
            }

            var closed = 0;
            foreach (var session in sessions.Values)
            {
                if (string.Equals(session.TrustedDeviceId, trustedDeviceId, StringComparison.Ordinal))
                {
                    CloseSession(session, "device revoked");
                    closed++;
                }
            }

            return closed;
        }

        /// <summary>Tears down every stream that belongs to a paired device.</summary>
        public int CloseAllDeviceSessions()
        {
            var closed = 0;
            foreach (var session in sessions.Values)
            {
                if (!string.IsNullOrEmpty(session.TrustedDeviceId))
                {
                    CloseSession(session, "pairings cleared");
                    closed++;
                }
            }

            return closed;
        }

        private void CloseSession(WebRtcSession session, string reason)
        {
            if (Interlocked.Exchange(ref session.Closed, 1) != 0)
            {
                return;
            }

            sessions.TryRemove(session.Id, out _);
            Console.Error.WriteLine($"[WebRTC] {session.Id} closing: {reason}");
            RecordSessionEvent(session, "closed", reason, "information");
            session.Cancellation.Cancel();
            session.Transport.Dispose();
            try
            {
                session.Peer.Close(reason);
            }
            catch
            {
            }
        }

        private void RegisterReplacingDeviceSessions(WebRtcSession session)
        {
            var replaced = new List<WebRtcSession>();
            lock (sessionRegistrationSync)
            {
                if (!string.IsNullOrWhiteSpace(session.TrustedDeviceId))
                {
                    foreach (var existing in sessions.Values)
                    {
                        if (existing.Id != session.Id &&
                            string.Equals(
                                existing.TrustedDeviceId,
                                session.TrustedDeviceId,
                                StringComparison.Ordinal))
                        {
                            sessions.TryRemove(existing.Id, out _);
                            replaced.Add(existing);
                        }
                    }
                }
                sessions[session.Id] = session;
            }

            foreach (var existing in replaced)
            {
                CloseSession(existing, "replaced by reconnect from the same device");
            }
        }

        private void ScheduleDisconnectedCleanup(WebRtcSession session)
        {
            var generation = Interlocked.Increment(ref session.DisconnectedGeneration);
            var cancellationToken = session.Cancellation.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                    if (!cancellationToken.IsCancellationRequested &&
                        generation == Volatile.Read(ref session.DisconnectedGeneration) &&
                        string.Equals(session.LastConnectionState, "disconnected", StringComparison.OrdinalIgnoreCase))
                    {
                        CloseSession(session, "WebRTC disconnected timeout");
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        private void RecordSessionEvent(WebRtcSession session, string action, string outcome, string severity = "information")
        {
            audit.Record(
                severity,
                "stream",
                "webrtc-" + action,
                outcome,
                subject: session.DeviceName,
                details: new Dictionary<string, string>
                {
                    ["session"] = session.Id.ToString("N"),
                    ["transportPlan"] = session.TransportPlan,
                    ["ice"] = session.LastIceState,
                    ["connection"] = session.LastConnectionState
                });
        }

        private static List<RTCIceServer> ToSipsIceServers(IEnumerable<WebRtcIceServer> source)
        {
            var result = new List<RTCIceServer>();
            foreach (var server in source ?? Enumerable.Empty<WebRtcIceServer>())
            {
                foreach (var url in server.Urls ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    result.Add(new RTCIceServer
                    {
                        urls = url,
                        username = server.Username ?? string.Empty,
                        credential = server.Credential ?? string.Empty,
                        credentialType = RTCIceCredentialType.password
                    });
                }
            }
            return result;
        }

        private sealed class WebRtcSession
        {
            public Guid Id { get; }
            public RTCPeerConnection Peer { get; }
            public string DeviceName { get; }

            /// <summary>Trusted device this stream belongs to; null for the local console.</summary>
            public string TrustedDeviceId { get; }
            public int Fps { get; }
            public int Quality { get; }
            public int ReceiverMaxBitrateKbps { get; }
            public int PlayoutDelayMs { get; }
            public string TransportPlan { get; }
            public H264WebRtcTransport Transport { get; }
            public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();
            public string LastConnectionState = "new";
            public string LastIceState = "new";
            public int StreamStarted;
            public int Closed;
            public int DisconnectedGeneration;

            public WebRtcSession(
                Guid id,
                RTCPeerConnection peer,
                string deviceName,
                int fps,
                int quality,
                string transportPlan,
                string trustedDeviceId = null,
                int receiverMaxBitrateKbps = 0,
                int playoutDelayMs = H264PlayoutDelayExtension.DefaultMaximumDelayMs)
            {
                Id = id;
                Peer = peer;
                DeviceName = deviceName;
                Fps = Math.Max(1, Math.Min(60, fps));
                Quality = Math.Max(25, Math.Min(85, quality));
                ReceiverMaxBitrateKbps = receiverMaxBitrateKbps <= 0
                    ? 0
                    : Math.Max(500, Math.Min(10000, receiverMaxBitrateKbps));
                PlayoutDelayMs = H264PlayoutDelayExtension.NormalizeMaximumDelayMs(playoutDelayMs);
                TransportPlan = transportPlan ?? "direct-stun";
                TrustedDeviceId = trustedDeviceId;
                Transport = new H264WebRtcTransport(peer);
            }
        }
    }

    public sealed class WebRtcOfferAnswer
    {
        public string Type { get; set; }
        public string Sdp { get; set; }
    }

    public sealed class WebRtcDiagnosticsSnapshot
    {
        public string GeneratedAt { get; set; }
        public WebRtcSessionDiagnostics[] ActiveSessions { get; set; } = Array.Empty<WebRtcSessionDiagnostics>();
    }

    public sealed class WebRtcSessionDiagnostics
    {
        public string SessionId { get; set; }
        public string DeviceName { get; set; }
        public string CreatedAt { get; set; }
        public string ConnectionState { get; set; }
        public string IceState { get; set; }
        public string TransportPlan { get; set; }
        public int ReceiverMaxBitrateKbps { get; set; }
        public int PlayoutDelayMs { get; set; }
        public H264TransportSnapshot Transport { get; set; }
    }
}
