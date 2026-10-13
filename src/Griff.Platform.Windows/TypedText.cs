using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Griff.Platform.Windows;

/// <summary>Works out the text a key press types in the current keyboard layout, with Shift, Caps Lock and AltGr.</summary>
public static class TypedText
{
    // Don't change the keyboard's state (dead keys waiting for a letter), so typing elsewhere isn't disturbed.
    private const uint DontChangeKeyboardState = 0x4;

    /// <summary>The characters <paramref name="key"/> types right now, or "" for keys that don't type anything.</summary>
    public static string Of(Key key)
    {
        try
        {
            var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            var state = new byte[256];
            if (virtualKey == 0 || !GetKeyboardState(state))
                return "";

            // Ctrl+letter makes a control character, not text; AltGr (Ctrl+Alt) still types.
            const int ctrl = 0x11, alt = 0x12;
            if ((state[ctrl] & 0x80) != 0 && (state[alt] & 0x80) == 0)
                return "";

            var buffer = new char[8];
            var layout = GetKeyboardLayout(0);
            var count = ToUnicodeEx(virtualKey, MapVirtualKeyEx(virtualKey, 0, layout), state, buffer, buffer.Length, DontChangeKeyboardState, layout);
            return count > 0 ? new string(buffer, 0, count) : "";
        }
        catch (EntryPointNotFoundException)
        {
            return "";
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState, [Out] char[] buffer, int bufferSize, uint flags, IntPtr layout);
}
