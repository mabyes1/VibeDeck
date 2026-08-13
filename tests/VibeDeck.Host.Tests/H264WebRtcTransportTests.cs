using System;
using System.Linq;
using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class H264WebRtcTransportTests
    {
        [Fact]
        public void PacketizeForTest_FragmentsLargeNalAndMarksOnlyLastPacket()
        {
            var accessUnit = new byte[1 + 4 + H264WebRtcTransport.MaximumRtpPayloadBytes + 25];
            accessUnit[0] = 0;
            accessUnit[1] = 0;
            accessUnit[2] = 0;
            accessUnit[3] = 1;
            accessUnit[4] = 0x65;
            Array.Fill(accessUnit, (byte)0x7a, 5, accessUnit.Length - 5);

            var packets = H264WebRtcTransport.PacketizeForTest(
                accessUnit,
                payloadTypeId: 100,
                timestamp: 90000,
                firstSequenceNumber: 42);

            Assert.Equal(2, packets.Count);
            Assert.Equal(new ushort[] { 42, 43 }, packets.Select(packet => packet.SequenceNumber));
            Assert.Equal(0, packets[0].MarkerBit);
            Assert.Equal(1, packets[1].MarkerBit);
            Assert.Equal(28, packets[0].Payload[0] & 0x1f);
            Assert.NotEqual(0, packets[0].Payload[1] & 0x80);
            Assert.NotEqual(0, packets[1].Payload[1] & 0x40);
        }

        [Fact]
        public void PacketizeForTest_UsesOneTimestampForWholeAccessUnit()
        {
            var accessUnit = new byte[]
            {
                0, 0, 0, 1, 0x67, 1, 2,
                0, 0, 0, 1, 0x65, 3, 4
            };

            var packets = H264WebRtcTransport.PacketizeForTest(accessUnit, 100, 12345, 7);

            Assert.Equal(2, packets.Count);
            Assert.All(packets, packet => Assert.Equal((uint)12345, packet.Timestamp));
            Assert.Equal(0, packets[0].MarkerBit);
            Assert.Equal(1, packets[1].MarkerBit);
        }

        [Fact]
        public void ParseNackEntriesForTest_PreservesEveryFciEntryInCompoundPacket()
        {
            var packet = new byte[]
            {
                // Empty receiver report: V=2, PT=RR, length=1 (8 bytes).
                0x80, 201, 0x00, 0x01, 0, 0, 0, 1,
                // Generic NACK: V=2/FMT=1, PT=RTPFB, length=4 (20 bytes).
                0x81, 205, 0x00, 0x04,
                0, 0, 0, 2, 0, 0, 0, 3,
                // Two PID/BLP entries. SIPSorcery's object model retains only
                // the first; the raw parser must retain both.
                0x12, 0x34, 0x00, 0x03,
                0xff, 0xfe, 0x80, 0x00,
                // SRTCP index/authentication tail is intentionally ignored.
                0x00, 0x00, 0x00
            };

            var entries = H264WebRtcTransport.ParseNackEntriesForTest(packet);

            Assert.Equal(2, entries.Count);
            Assert.Equal((ushort)0x1234, entries[0].PacketId);
            Assert.Equal((ushort)0x0003, entries[0].Bitmask);
            Assert.Equal((ushort)0xfffe, entries[1].PacketId);
            Assert.Equal((ushort)0x8000, entries[1].Bitmask);
        }
    }
}
