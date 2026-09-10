using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.Net;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// WebRTC playout-delay RTP extension. SIPSorcery 10.0.9 does not recognise
    /// this Chrome extension, so VibeDeck negotiates and writes it explicitly.
    /// Values are 12-bit fields in 10 ms units packed into three bytes.
    /// </summary>
    internal sealed class H264PlayoutDelayExtension : RTPHeaderExtension
    {
        internal const string HeaderExtensionUri = "http://www.webrtc.org/experiments/rtp-hdrext/playout-delay";
        internal const int DefaultMaximumDelayMs = 40;
        private const int PayloadSize = 3;
        private readonly int maximumDelayUnits;

        public H264PlayoutDelayExtension(int id, int maximumDelayMs = DefaultMaximumDelayMs)
            : base(id, HeaderExtensionUri, PayloadSize, RTPHeaderExtensionType.OneByte, SDPMediaTypesEnum.video)
        {
            maximumDelayUnits = NormalizeMaximumDelayMs(maximumDelayMs) / 10;
        }

        public override void Set(object value)
        {
            // The interactive stream uses one product-level bound. A future
            // adaptive controller can replace this with a mutable value.
        }

        public override byte[] Marshal()
        {
            return new[]
            {
                (byte)((Id << 4) | (PayloadSize - 1)),
                (byte)0,
                (byte)((maximumDelayUnits >> 8) & 0x0f),
                (byte)(maximumDelayUnits & 0xff)
            };
        }

        public override object Unmarshal(RTPHeader header, byte[] data)
        {
            if (data == null || data.Length != PayloadSize)
            {
                return null;
            }

            var minimumUnits = (data[0] << 4) | (data[1] >> 4);
            var maximumUnits = ((data[1] & 0x0f) << 8) | data[2];
            return new[] { minimumUnits * 10, maximumUnits * 10 };
        }

        internal static int NormalizeMaximumDelayMs(int value)
        {
            return value == 20 || value == 80 ? value : 40;
        }

        internal static bool TryGetOfferedId(string offerSdp, out int id)
        {
            id = 0;
            var inVideo = false;
            foreach (var sourceLine in SplitLines(offerSdp))
            {
                var line = sourceLine.Trim();
                if (line.StartsWith("m=", StringComparison.Ordinal))
                {
                    inVideo = line.StartsWith("m=video ", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inVideo || !line.StartsWith("a=extmap:", StringComparison.OrdinalIgnoreCase) ||
                    line.IndexOf(HeaderExtensionUri, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var idStart = "a=extmap:".Length;
                var idEnd = line.IndexOfAny(new[] { '/', ' ', '\t' }, idStart);
                if (idEnd < 0) idEnd = line.Length;
                if (int.TryParse(line.Substring(idStart, idEnd - idStart), out var parsed) &&
                    parsed >= 1 && parsed <= 14)
                {
                    id = parsed;
                    return true;
                }
            }
            return false;
        }

        internal static string AddToAnswer(string answerSdp, int id)
        {
            if (string.IsNullOrWhiteSpace(answerSdp) || id < 1 || id > 14 ||
                answerSdp.IndexOf(HeaderExtensionUri, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return answerSdp;
            }

            var newline = answerSdp.Contains("\r\n") ? "\r\n" : "\n";
            var lines = SplitLines(answerSdp.TrimEnd('\r', '\n')).ToList();
            var videoIndex = lines.FindIndex(line =>
                line.TrimStart().StartsWith("m=video ", StringComparison.OrdinalIgnoreCase));
            if (videoIndex < 0)
            {
                return answerSdp;
            }

            lines.Insert(videoIndex + 1, $"a=extmap:{id} {HeaderExtensionUri}");
            var trailingNewline = answerSdp.EndsWith("\r\n", StringComparison.Ordinal) ||
                answerSdp.EndsWith("\n", StringComparison.Ordinal);
            return string.Join(newline, lines) + (trailingNewline ? newline : string.Empty);
        }

        private static IEnumerable<string> SplitLines(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
        }
    }
}
