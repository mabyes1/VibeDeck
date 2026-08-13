using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VibeDeck.Host.Streaming
{
    internal static class H264RtcpFeedbackNegotiator
    {
        private static readonly Regex H264RtpMap = new Regex(
            @"^a=rtpmap:(?<pt>\d+)\s+H264/90000(?:\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// SIPSorcery 10.x only lists transport-cc as a supported feedback
        /// message, even though its RTCP parser exposes Generic NACK and PLI.
        /// Preserve those messages when the browser offered them and the Host
        /// has installed the corresponding recovery handlers.
        /// </summary>
        public static string AddLossRecoveryFeedback(string offerSdp, string answerSdp)
        {
            if (string.IsNullOrWhiteSpace(offerSdp) || string.IsNullOrWhiteSpace(answerSdp))
            {
                return answerSdp ?? string.Empty;
            }

            var offeredH264 = FindH264PayloadTypes(offerSdp);
            var answerH264 = FindH264PayloadTypes(answerSdp);
            if (offeredH264.Count == 0 || answerH264.Count == 0)
            {
                return answerSdp;
            }

            var newline = answerSdp.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = answerSdp.Replace("\r\n", "\n").Split('\n').ToList();
            foreach (var payloadType in answerH264)
            {
                if (!offeredH264.Contains(payloadType))
                {
                    continue;
                }

                var supportsNack = HasFeedback(offerSdp, payloadType, "nack");
                var supportsPli = HasFeedback(offerSdp, payloadType, "nack pli");
                if (!supportsNack)
                {
                    continue;
                }

                InsertFeedback(lines, payloadType, "nack");
                if (supportsPli)
                {
                    InsertFeedback(lines, payloadType, "nack pli");
                }
            }

            return string.Join(newline, lines);
        }

        private static HashSet<int> FindH264PayloadTypes(string sdp)
        {
            var result = new HashSet<int>();
            foreach (var line in sdp.Replace("\r\n", "\n").Split('\n'))
            {
                var match = H264RtpMap.Match(line.Trim());
                if (match.Success && int.TryParse(match.Groups["pt"].Value, out var payloadType))
                {
                    result.Add(payloadType);
                }
            }
            return result;
        }

        private static bool HasFeedback(string sdp, int payloadType, string feedback)
        {
            var exact = $"a=rtcp-fb:{payloadType} {feedback}";
            var wildcard = $"a=rtcp-fb:* {feedback}";
            return sdp.Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.Trim())
                .Any(line => line.Equals(exact, StringComparison.OrdinalIgnoreCase) ||
                    line.Equals(wildcard, StringComparison.OrdinalIgnoreCase));
        }

        private static void InsertFeedback(List<string> lines, int payloadType, string feedback)
        {
            var value = $"a=rtcp-fb:{payloadType} {feedback}";
            if (lines.Any(line => line.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var index = lines.FindIndex(line => H264RtpMap.IsMatch(line.Trim()) &&
                line.Trim().StartsWith($"a=rtpmap:{payloadType} ", StringComparison.OrdinalIgnoreCase));
            lines.Insert(index >= 0 ? index + 1 : lines.Count, value);
        }
    }
}
