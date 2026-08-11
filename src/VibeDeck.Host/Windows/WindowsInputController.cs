using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace VibeDeck.Host.Windows
{
    public sealed class WindowsInputController
    {
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint MouseEventRightDown = 0x0008;
        private const uint MouseEventRightUp = 0x0010;
        private const uint InputKeyboard = 1;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventUnicode = 0x0004;
        private const ushort VirtualKeyShift = 0x10;
        private const ushort VirtualKeyControl = 0x11;
        private const ushort VirtualKeyAlt = 0x12;
        private const ushort VirtualKeyLeftWindows = 0x5B;

        private static readonly IReadOnlyDictionary<string, ushort> VirtualKeys =
            new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
            {
                ["Backspace"] = 0x08,
                ["Tab"] = 0x09,
                ["Enter"] = 0x0D,
                ["Escape"] = 0x1B,
                ["Space"] = 0x20,
                ["PageUp"] = 0x21,
                ["PageDown"] = 0x22,
                ["End"] = 0x23,
                ["Home"] = 0x24,
                ["ArrowLeft"] = 0x25,
                ["ArrowUp"] = 0x26,
                ["ArrowRight"] = 0x27,
                ["ArrowDown"] = 0x28,
                ["Insert"] = 0x2D,
                ["Delete"] = 0x2E,
                ["F1"] = 0x70,
                ["F2"] = 0x71,
                ["F3"] = 0x72,
                ["F4"] = 0x73,
                ["F5"] = 0x74,
                ["F6"] = 0x75,
                ["F7"] = 0x76,
                ["F8"] = 0x77,
                ["F9"] = 0x78,
                ["F10"] = 0x79,
                ["F11"] = 0x7A,
                ["F12"] = 0x7B
            };

        private readonly DisplayCatalog catalog;

        public WindowsInputController(DisplayCatalog catalog)
        {
            this.catalog = catalog;
        }

        public bool Apply(InputEvent inputEvent)
        {
            if (inputEvent == null || string.IsNullOrWhiteSpace(inputEvent.Type))
            {
                return false;
            }

            if (string.Equals(inputEvent.Type, "text", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyText(inputEvent.Text);
            }

            if (string.Equals(inputEvent.Type, "key", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyKey(inputEvent);
            }

            var display = FindDisplay(inputEvent.DeviceName);
            if (display == null || display.Width <= 0 || display.Height <= 0)
            {
                return false;
            }

            var x = display.Left + ClampToPixel(inputEvent.X, display.Width);
            var y = display.Top + ClampToPixel(inputEvent.Y, display.Height);
            NativeMethods.SetCursorPos(x, y);

            switch (inputEvent.Type.ToLowerInvariant())
            {
                case "pointerdown":
                    NativeMethods.mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                    return true;
                case "pointerup":
                case "pointercancel":
                    NativeMethods.mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                    return true;
                case "pointermove":
                    return true;
                case "rightclick":
                    NativeMethods.mouse_event(MouseEventRightDown, 0, 0, 0, UIntPtr.Zero);
                    NativeMethods.mouse_event(MouseEventRightUp, 0, 0, 0, UIntPtr.Zero);
                    return true;
                default:
                    return false;
            }
        }

        private static bool ApplyText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            // Keep a single browser message bounded. UTF-16 code units are sent
            // intentionally: KEYEVENTF_UNICODE accepts surrogate pairs as two
            // consecutive units and Windows reconstructs the final character.
            var value = text.Length > 256 ? text.Substring(0, 256) : text;
            var inputs = new List<Input>(value.Length * 2);
            foreach (var character in value)
            {
                if (character == '\r')
                {
                    continue;
                }

                if (character == '\n')
                {
                    inputs.Add(CreateVirtualKeyInput(0x0D, keyUp: false));
                    inputs.Add(CreateVirtualKeyInput(0x0D, keyUp: true));
                    continue;
                }

                inputs.Add(CreateUnicodeInput(character, keyUp: false));
                inputs.Add(CreateUnicodeInput(character, keyUp: true));
            }

            return SendInputs(inputs);
        }

        private static bool ApplyKey(InputEvent inputEvent)
        {
            if (!TryResolveVirtualKey(inputEvent.Key, inputEvent.Code, out var virtualKey))
            {
                return false;
            }

            var inputs = new List<Input>(10);
            AddModifier(inputs, VirtualKeyControl, inputEvent.CtrlKey, keyUp: false);
            AddModifier(inputs, VirtualKeyAlt, inputEvent.AltKey, keyUp: false);
            AddModifier(inputs, VirtualKeyShift, inputEvent.ShiftKey, keyUp: false);
            AddModifier(inputs, VirtualKeyLeftWindows, inputEvent.MetaKey, keyUp: false);
            inputs.Add(CreateVirtualKeyInput(virtualKey, keyUp: false));
            inputs.Add(CreateVirtualKeyInput(virtualKey, keyUp: true));
            AddModifier(inputs, VirtualKeyLeftWindows, inputEvent.MetaKey, keyUp: true);
            AddModifier(inputs, VirtualKeyShift, inputEvent.ShiftKey, keyUp: true);
            AddModifier(inputs, VirtualKeyAlt, inputEvent.AltKey, keyUp: true);
            AddModifier(inputs, VirtualKeyControl, inputEvent.CtrlKey, keyUp: true);
            return SendInputs(inputs);
        }

        private static void AddModifier(ICollection<Input> inputs, ushort virtualKey, bool enabled, bool keyUp)
        {
            if (enabled)
            {
                inputs.Add(CreateVirtualKeyInput(virtualKey, keyUp));
            }
        }

        internal static bool TryResolveVirtualKey(string key, string code, out ushort virtualKey)
        {
            if (!string.IsNullOrWhiteSpace(key) && VirtualKeys.TryGetValue(key, out virtualKey))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(code) && code.Length == 4 &&
                code.StartsWith("Key", StringComparison.OrdinalIgnoreCase))
            {
                var letter = char.ToUpperInvariant(code[3]);
                if (letter >= 'A' && letter <= 'Z')
                {
                    virtualKey = letter;
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(code) && code.Length == 6 &&
                code.StartsWith("Digit", StringComparison.OrdinalIgnoreCase))
            {
                var digit = code[5];
                if (digit >= '0' && digit <= '9')
                {
                    virtualKey = digit;
                    return true;
                }
            }

            if (string.Equals(code, "Space", StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = 0x20;
                return true;
            }

            virtualKey = 0;
            return false;
        }

        private static Input CreateUnicodeInput(char character, bool keyUp)
        {
            return new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        ScanCode = character,
                        Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0)
                    }
                }
            };
        }

        private static Input CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
        {
            return new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = keyUp ? KeyEventKeyUp : 0
                    }
                }
            };
        }

        private static bool SendInputs(IReadOnlyCollection<Input> inputs)
        {
            if (inputs.Count == 0)
            {
                return false;
            }

            var array = inputs.ToArray();
            return NativeMethods.SendInput((uint)array.Length, array, Marshal.SizeOf(typeof(Input))) == (uint)array.Length;
        }

        private DisplayInfo FindDisplay(string deviceName)
        {
            var displays = catalog.GetDisplays();
            return displays.FirstOrDefault(display =>
                    string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                ?? displays.FirstOrDefault(display => display.IsVibeDeckDisplay)
                ?? displays.FirstOrDefault();
        }

        private static int ClampToPixel(double value, int size)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return (int)Math.Round(Math.Max(0, Math.Min(1, value)) * Math.Max(0, size - 1));
        }
    }
}
