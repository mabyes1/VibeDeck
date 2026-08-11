using System;
using System.Runtime.InteropServices;
using VibeDeck.Host.Windows;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class WindowsInputControllerTests
    {
        [Theory]
        [InlineData("Backspace", "", 0x08)]
        [InlineData("ArrowLeft", "", 0x25)]
        [InlineData("F12", "", 0x7B)]
        [InlineData("c", "KeyC", 0x43)]
        [InlineData("7", "Digit7", 0x37)]
        [InlineData(" ", "Space", 0x20)]
        public void Resolves_browser_keys_to_windows_virtual_keys(string key, string code, int expected)
        {
            var resolved = WindowsInputController.TryResolveVirtualKey(key, code, out var virtualKey);

            Assert.True(resolved);
            Assert.Equal((ushort)expected, virtualKey);
        }

        [Fact]
        public void Rejects_unknown_browser_keys()
        {
            Assert.False(WindowsInputController.TryResolveVirtualKey("MediaPlayPause", "MediaPlayPause", out _));
        }

        [Fact]
        public void Native_input_layout_matches_windows_send_input_contract()
        {
            var expected = IntPtr.Size == 8 ? 40 : 28;

            Assert.Equal(expected, Marshal.SizeOf<Input>());
        }
    }
}
