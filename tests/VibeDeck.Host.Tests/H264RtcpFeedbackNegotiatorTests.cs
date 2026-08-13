using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class H264RtcpFeedbackNegotiatorTests
    {
        [Fact]
        public void AddLossRecoveryFeedback_PreservesOfferedNackAndPli()
        {
            var offer = string.Join("\r\n", new[]
            {
                "m=video 9 UDP/TLS/RTP/SAVPF 100 101",
                "a=rtpmap:100 H264/90000",
                "a=rtcp-fb:100 nack",
                "a=rtcp-fb:100 nack pli",
                "a=rtcp-fb:100 transport-cc",
                "a=rtpmap:101 rtx/90000",
                "a=fmtp:101 apt=100",
                string.Empty
            });
            var answer = string.Join("\r\n", new[]
            {
                "m=video 9 UDP/TLS/RTP/SAVPF 100",
                "a=rtpmap:100 H264/90000",
                "a=rtcp-fb:100 transport-cc",
                string.Empty
            });

            var result = H264RtcpFeedbackNegotiator.AddLossRecoveryFeedback(offer, answer);

            Assert.Contains("a=rtcp-fb:100 nack\r\n", result);
            Assert.Contains("a=rtcp-fb:100 nack pli\r\n", result);
            Assert.Equal(1, Count(result, "a=rtcp-fb:100 nack\r\n"));
        }

        [Fact]
        public void AddLossRecoveryFeedback_DoesNotAdvertiseFeedbackBrowserDidNotOffer()
        {
            const string offer = "m=video 9 UDP/TLS/RTP/SAVPF 100\r\na=rtpmap:100 H264/90000\r\n";
            const string answer = "m=video 9 UDP/TLS/RTP/SAVPF 100\r\na=rtpmap:100 H264/90000\r\n";

            var result = H264RtcpFeedbackNegotiator.AddLossRecoveryFeedback(offer, answer);

            Assert.DoesNotContain("nack", result);
        }

        private static int Count(string value, string needle)
        {
            return (value.Length - value.Replace(needle, string.Empty).Length) / needle.Length;
        }
    }
}
