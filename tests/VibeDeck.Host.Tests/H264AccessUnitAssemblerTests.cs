using System.Linq;
using VibeDeck.Host.Streaming;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class H264AccessUnitAssemblerTests
    {
        [Fact]
        public void Append_PreservesSplitStartCodeAndReturnsCompleteAccessUnit()
        {
            var assembler = new H264AccessUnitAssembler();

            Assert.Empty(assembler.Append(new byte[] { 0x00, 0x00 }, 2));

            var remainder = new byte[]
            {
                0x01, 0x09, 0xF0,
                0x00, 0x00, 0x01, 0x65, 0xAA, 0xBB,
                0x00, 0x00, 0x00, 0x01, 0x09, 0xF0
            };
            var accessUnits = assembler.Append(remainder, remainder.Length).ToArray();

            var accessUnit = Assert.Single(accessUnits);
            Assert.Equal(
                new byte[]
                {
                    0x00, 0x00, 0x01, 0x09, 0xF0,
                    0x00, 0x00, 0x01, 0x65, 0xAA, 0xBB
                },
                accessUnit);
        }
    }
}
