using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Packetises and paces H.264 RTP while retaining a short packet history
    /// for Generic NACK recovery. A separate RFC 4588 RTX SSRC is not available
    /// in SIPSorcery's raw-video sender; re-sending the missing packet on the
    /// negotiated media SSRC provides equivalent repair for packets the browser
    /// never received, without advertising an RTX stream we cannot honour.
    /// </summary>
    public sealed class H264WebRtcTransport : IDisposable
    {
        internal const int MaximumRtpPayloadBytes = 1200;
        private static readonly TimeSpan PacketLifetime = TimeSpan.FromSeconds(2.5);
        private const int MaximumCachedPackets = 3072;
        private const int MaximumCachedBytes = 4 * 1024 * 1024;

        private readonly RTCPeerConnection peer;
        private readonly H264TransportFeedbackPolicy feedbackPolicy;
        private readonly object sendSync = new object();
        private readonly object cacheSync = new object();
        private readonly Dictionary<ushort, CachedPacket> cache = new Dictionary<ushort, CachedPacket>();
        private readonly Dictionary<ushort, DateTimeOffset> recentRetransmissions = new Dictionary<ushort, DateTimeOffset>();
        private readonly Queue<CachedPacket> cacheOrder = new Queue<CachedPacket>();
        private readonly Stopwatch pacingClock = Stopwatch.StartNew();
        private double nextPacedSendSeconds;
        private int targetBitrateKbps = 1200;
        private int cachedBytes;
        private uint timestamp;
        private long packetsSent;
        private long packetsRetransmitted;
        private long nackRequests;
        private long pliRequests;
        private int disposed;
        private RTPChannel rawFeedbackChannel;

        public H264WebRtcTransport(RTCPeerConnection peer)
            : this(peer, null)
        {
        }

        internal H264WebRtcTransport(RTCPeerConnection peer, H264TransportFeedbackPolicy feedbackPolicy)
        {
            this.peer = peer ?? throw new ArgumentNullException(nameof(peer));
            this.feedbackPolicy = feedbackPolicy ?? new H264TransportFeedbackPolicy();
            timestamp = peer.VideoLocalTrack?.Timestamp ?? 0;
            peer.OnReceiveReport += HandleReceiveReport;
        }

        public void SetTargetBitrate(int bitrateKbps)
        {
            Interlocked.Exchange(ref targetBitrateKbps, Math.Max(350, Math.Min(10000, bitrateKbps)));
            EnsureRawFeedbackSubscription();
        }

        public async Task SendAccessUnitAsync(
            uint duration,
            int payloadTypeId,
            byte[] accessUnit,
            CancellationToken cancellationToken)
        {
            if (accessUnit == null || accessUnit.Length == 0)
            {
                return;
            }

            foreach (var packet in Packetize(accessUnit, payloadTypeId, timestamp))
            {
                await PaceAsync(packet.Payload.Length, cancellationToken).ConfigureAwait(false);
                SendPacket(packet, cacheForRecovery: true);
            }
            timestamp = unchecked(timestamp + duration);
        }

        internal H264RecoveryAction ConsumeRecoveryAction(bool canDownshift)
        {
            return feedbackPolicy.ConsumeAction(canDownshift);
        }

        public H264TransportSnapshot GetSnapshot()
        {
            lock (cacheSync)
            {
                PruneCache(DateTimeOffset.UtcNow);
                var pacingDebtMs = Math.Max(
                    0d,
                    (Volatile.Read(ref nextPacedSendSeconds) - pacingClock.Elapsed.TotalSeconds) * 1000d);
                return new H264TransportSnapshot
                {
                    TargetBitrateKbps = Volatile.Read(ref targetBitrateKbps),
                    PacketsSent = Interlocked.Read(ref packetsSent),
                    NackRequests = Interlocked.Read(ref nackRequests),
                    PacketsRetransmitted = Interlocked.Read(ref packetsRetransmitted),
                    PliRequests = Interlocked.Read(ref pliRequests),
                    CachedPackets = cache.Count,
                    PacingDebtMs = Math.Round(pacingDebtMs, 1)
                };
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            peer.OnReceiveReport -= HandleReceiveReport;
            if (rawFeedbackChannel != null)
            {
                rawFeedbackChannel.OnRTPDataReceived -= HandleRawRtpOrRtcp;
                rawFeedbackChannel = null;
            }
            lock (cacheSync)
            {
                cache.Clear();
                cacheOrder.Clear();
                recentRetransmissions.Clear();
                cachedBytes = 0;
            }
        }

        internal static IReadOnlyList<H264RtpPacket> PacketizeForTest(
            byte[] accessUnit,
            int payloadTypeId,
            uint timestamp,
            ushort firstSequenceNumber)
        {
            var sequence = firstSequenceNumber;
            return PacketizeCore(accessUnit, payloadTypeId, timestamp, () => sequence++).ToArray();
        }

        internal static IReadOnlyList<(ushort PacketId, ushort Bitmask)> ParseNackEntriesForTest(byte[] packet)
        {
            return ParseRawFeedback(packet).Nacks;
        }

        private IEnumerable<H264RtpPacket> Packetize(byte[] accessUnit, int payloadTypeId, uint packetTimestamp)
        {
            return PacketizeCore(
                accessUnit,
                payloadTypeId,
                packetTimestamp,
                () => peer.VideoLocalTrack.GetNextSeqNum());
        }

        private static IEnumerable<H264RtpPacket> PacketizeCore(
            byte[] accessUnit,
            int payloadTypeId,
            uint packetTimestamp,
            Func<ushort> nextSequenceNumber)
        {
            foreach (var nal in H264Packetiser.ParseNals(accessUnit))
            {
                if (nal.NAL == null || nal.NAL.Length == 0)
                {
                    continue;
                }

                if (nal.NAL.Length <= MaximumRtpPayloadBytes)
                {
                    var payload = new byte[nal.NAL.Length];
                    Buffer.BlockCopy(nal.NAL, 0, payload, 0, payload.Length);
                    yield return new H264RtpPacket(
                        nextSequenceNumber(),
                        packetTimestamp,
                        nal.IsLast ? 1 : 0,
                        payloadTypeId,
                        payload);
                    continue;
                }

                var nalHeader = nal.NAL[0];
                var nalPayloadLength = nal.NAL.Length - 1;
                for (var offset = 0; offset < nalPayloadLength; offset += MaximumRtpPayloadBytes)
                {
                    var payloadLength = Math.Min(MaximumRtpPayloadBytes, nalPayloadLength - offset);
                    var first = offset == 0;
                    var final = offset + payloadLength >= nalPayloadLength;
                    var rtpHeader = H264Packetiser.GetH264RtpHeader(nalHeader, first, final);
                    var payload = new byte[payloadLength + rtpHeader.Length];
                    Buffer.BlockCopy(rtpHeader, 0, payload, 0, rtpHeader.Length);
                    Buffer.BlockCopy(nal.NAL, offset + 1, payload, rtpHeader.Length, payloadLength);
                    yield return new H264RtpPacket(
                        nextSequenceNumber(),
                        packetTimestamp,
                        nal.IsLast && final ? 1 : 0,
                        payloadTypeId,
                        payload);
                }
            }
        }

        private async Task PaceAsync(int payloadBytes, CancellationToken cancellationToken)
        {
            var now = pacingClock.Elapsed.TotalSeconds;
            // Low-rate CBR streams still contain a much larger IDR than their
            // average frame. Allow up to ~0.9 Mbps for that short burst; the
            // encoder's average remains at the negotiated target and the burst
            // stays well below the ZenPad path ceiling measured in practice.
            var rateBitsPerSecond = Math.Max(900000d, Volatile.Read(ref targetBitrateKbps) * 1120d);
            const double burstAllowanceSeconds = 0.045;
            if (nextPacedSendSeconds < now - burstAllowanceSeconds)
            {
                nextPacedSendSeconds = now - burstAllowanceSeconds;
            }

            var delaySeconds = nextPacedSendSeconds - now;
            nextPacedSendSeconds += (payloadBytes + 40) * 8d / rateBitsPerSecond;
            if (delaySeconds > 0.001)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
            }
        }

        private void SendPacket(H264RtpPacket packet, bool cacheForRecovery)
        {
            if (Volatile.Read(ref disposed) != 0 || peer.connectionState != RTCPeerConnectionState.connected)
            {
                return;
            }

            lock (sendSync)
            {
                peer.VideoStream.SetRtpHeaderExtensionValue(
                    TransportWideCCExtension.RTP_HEADER_EXTENSION_URI,
                    null);
                peer.SendRtpRaw(
                    SDPMediaTypesEnum.video,
                    packet.Payload,
                    packet.Timestamp,
                    packet.MarkerBit,
                    packet.PayloadTypeId,
                    packet.SequenceNumber);
            }

            Interlocked.Increment(ref packetsSent);
            if (cacheForRecovery)
            {
                Store(packet);
            }
        }

        private void HandleReceiveReport(
            IPEndPoint remoteEndPoint,
            SDPMediaTypesEnum mediaType,
            RTCPCompoundPacket report)
        {
            if (Volatile.Read(ref disposed) != 0 || mediaType != SDPMediaTypesEnum.video || report == null)
            {
                return;
            }

            var feedback = report.Feedback;
            if (feedback != null && feedback.Header.PacketType == RTCPReportTypesEnum.RTPFB &&
                feedback.Header.FeedbackMessageType == RTCPFeedbackTypesEnum.NACK)
            {
                var requested = H264TransportFeedbackPolicy.ExpandNack(feedback.PID, feedback.BLP);
                var recovered = 0;
                foreach (var sequenceNumber in requested)
                {
                    if (TryRetransmit(sequenceNumber))
                    {
                        recovered++;
                    }
                }
                Interlocked.Add(ref nackRequests, requested.Count);
                feedbackPolicy.ObserveNack(requested.Count, recovered);
            }
            else if (feedback != null && feedback.Header.PacketType == RTCPReportTypesEnum.PSFB &&
                (feedback.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI ||
                 feedback.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.FIR))
            {
                Interlocked.Increment(ref pliRequests);
                feedbackPolicy.RequestKeyFrame();
            }

            var receptionReports = report.ReceiverReport?.ReceptionReports;
            if (receptionReports != null)
            {
                foreach (var sample in receptionReports)
                {
                    feedbackPolicy.ObserveLossFraction(sample.FractionLost / 256d);
                }
            }

            var statuses = report.TWCCFeedback?.PacketStatuses;
            if (statuses != null && statuses.Count >= 8)
            {
                var missing = statuses.Count(item => item.Status == TWCCPacketStatusType.NotReceived);
                feedbackPolicy.ObserveLossFraction(missing / (double)statuses.Count);
            }
        }

        private void EnsureRawFeedbackSubscription()
        {
            if (rawFeedbackChannel != null || Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            var channel = peer.VideoStream?.GetRTPChannel();
            if (channel == null)
            {
                return;
            }

            lock (sendSync)
            {
                if (rawFeedbackChannel != null || Volatile.Read(ref disposed) != 0)
                {
                    return;
                }
                rawFeedbackChannel = channel;
                // RTPSession subscribed first. Its secure handler decrypts the
                // SRTCP buffer in place before this observer receives the same
                // byte array, which lets us preserve every Generic NACK FCI
                // entry that RTCPFeedback's single PID/BLP model drops.
                channel.OnRTPDataReceived += HandleRawRtpOrRtcp;
            }
        }

        private void HandleRawRtpOrRtcp(int localPort, IPEndPoint remoteEndPoint, byte[] packet)
        {
            if (Volatile.Read(ref disposed) != 0 || packet == null || packet.Length < 12)
            {
                return;
            }

            var feedback = ParseRawFeedback(packet);
            foreach (var entry in feedback.Nacks)
            {
                var requested = H264TransportFeedbackPolicy.ExpandNack(entry.PacketId, entry.Bitmask);
                var recovered = 0;
                foreach (var sequenceNumber in requested)
                {
                    if (TryRetransmit(sequenceNumber))
                    {
                        recovered++;
                    }
                }
                Interlocked.Add(ref nackRequests, requested.Count);
                feedbackPolicy.ObserveNack(requested.Count, recovered);
            }

            if (feedback.RequestsKeyFrame)
            {
                Interlocked.Increment(ref pliRequests);
                feedbackPolicy.RequestKeyFrame();
            }
        }

        private bool TryRetransmit(ushort sequenceNumber)
        {
            H264RtpPacket packet;
            var now = DateTimeOffset.UtcNow;
            lock (cacheSync)
            {
                PruneCache(now);
                if (!cache.TryGetValue(sequenceNumber, out var cached))
                {
                    return false;
                }
                if (recentRetransmissions.TryGetValue(sequenceNumber, out var lastSentAt) &&
                    now - lastSentAt < TimeSpan.FromMilliseconds(15))
                {
                    return true;
                }
                recentRetransmissions[sequenceNumber] = now;
                packet = cached.Packet;
            }

            SendPacket(packet, cacheForRecovery: false);
            Interlocked.Increment(ref packetsRetransmitted);
            return true;
        }

        private void Store(H264RtpPacket packet)
        {
            var now = DateTimeOffset.UtcNow;
            var cached = new CachedPacket(packet, now);
            lock (cacheSync)
            {
                if (cache.TryGetValue(packet.SequenceNumber, out var previous))
                {
                    cachedBytes -= previous.Packet.Payload.Length;
                }
                cache[packet.SequenceNumber] = cached;
                cacheOrder.Enqueue(cached);
                cachedBytes += packet.Payload.Length;
                PruneCache(now);
            }
        }

        private void PruneCache(DateTimeOffset now)
        {
            while (cacheOrder.Count > 0)
            {
                var oldest = cacheOrder.Peek();
                var overLimit = cache.Count > MaximumCachedPackets || cachedBytes > MaximumCachedBytes;
                if (!overLimit && now - oldest.SentAt <= PacketLifetime)
                {
                    break;
                }

                cacheOrder.Dequeue();
                if (cache.TryGetValue(oldest.Packet.SequenceNumber, out var current) &&
                    ReferenceEquals(current, oldest))
                {
                    cache.Remove(oldest.Packet.SequenceNumber);
                    recentRetransmissions.Remove(oldest.Packet.SequenceNumber);
                    cachedBytes -= oldest.Packet.Payload.Length;
                }
            }
        }

        private static RawFeedback ParseRawFeedback(byte[] packet)
        {
            var result = new RawFeedback();
            var offset = 0;
            while (offset + 4 <= packet.Length)
            {
                var first = packet[offset];
                if ((first >> 6) != 2)
                {
                    break;
                }

                var packetType = packet[offset + 1];
                var packetLength = (BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset + 2, 2)) + 1) * 4;
                if (packetLength < 4 || offset + packetLength > packet.Length)
                {
                    break;
                }

                var feedbackType = first & 0x1f;
                if (packetType == (byte)RTCPReportTypesEnum.RTPFB &&
                    feedbackType == (int)RTCPFeedbackTypesEnum.NACK &&
                    packetLength >= 16)
                {
                    var feedbackOffset = offset + 12;
                    var feedbackEnd = offset + packetLength;
                    while (feedbackOffset + 4 <= feedbackEnd)
                    {
                        result.Nacks.Add((
                            BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(feedbackOffset, 2)),
                            BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(feedbackOffset + 2, 2))));
                        feedbackOffset += 4;
                    }
                }
                else if (packetType == (byte)RTCPReportTypesEnum.PSFB &&
                    (feedbackType == (int)PSFBFeedbackTypesEnum.PLI ||
                     feedbackType == (int)PSFBFeedbackTypesEnum.FIR))
                {
                    result.RequestsKeyFrame = true;
                }

                offset += packetLength;
            }
            return result;
        }

        private sealed class CachedPacket
        {
            public H264RtpPacket Packet { get; }
            public DateTimeOffset SentAt { get; }

            public CachedPacket(H264RtpPacket packet, DateTimeOffset sentAt)
            {
                Packet = packet;
                SentAt = sentAt;
            }
        }

        private sealed class RawFeedback
        {
            public List<(ushort PacketId, ushort Bitmask)> Nacks { get; } =
                new List<(ushort PacketId, ushort Bitmask)>();
            public bool RequestsKeyFrame { get; set; }
        }
    }

    internal sealed class H264RtpPacket
    {
        public ushort SequenceNumber { get; }
        public uint Timestamp { get; }
        public int MarkerBit { get; }
        public int PayloadTypeId { get; }
        public byte[] Payload { get; }

        public H264RtpPacket(
            ushort sequenceNumber,
            uint timestamp,
            int markerBit,
            int payloadTypeId,
            byte[] payload)
        {
            SequenceNumber = sequenceNumber;
            Timestamp = timestamp;
            MarkerBit = markerBit;
            PayloadTypeId = payloadTypeId;
            Payload = payload ?? Array.Empty<byte>();
        }
    }

    public sealed class H264TransportSnapshot
    {
        public int TargetBitrateKbps { get; set; }
        public long PacketsSent { get; set; }
        public long NackRequests { get; set; }
        public long PacketsRetransmitted { get; set; }
        public long PliRequests { get; set; }
        public int CachedPackets { get; set; }
        public double PacingDebtMs { get; set; }
    }
}
