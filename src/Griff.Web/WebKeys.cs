using Griff.Input;

namespace Griff.Web;

/// <summary>
/// The browser's keys as Griff's: a <c>KeyboardEvent.code</c> ("KeyA", "ArrowLeft", "ShiftLeft") names the key where it
/// sits, whatever the keyboard's layout, as Griff's <see cref="Key"/> does on Windows.
/// </summary>
public static class WebKeys
{
    private static readonly Dictionary<string, Key> Named = new(StringComparer.Ordinal)
    {
        ["Space"] = Key.Space,
        ["Enter"] = Key.Enter,
        ["NumpadEnter"] = Key.Enter,
        ["Escape"] = Key.Escape,
        ["Tab"] = Key.Tab,
        ["Backspace"] = Key.Backspace,
        ["ArrowLeft"] = Key.Left,
        ["ArrowRight"] = Key.Right,
        ["ArrowUp"] = Key.Up,
        ["ArrowDown"] = Key.Down,
        ["ShiftLeft"] = Key.LeftShift,
        ["ShiftRight"] = Key.RightShift,
        ["ControlLeft"] = Key.LeftControl,
        ["ControlRight"] = Key.RightControl,
        ["AltLeft"] = Key.LeftAlt,
        ["AltRight"] = Key.RightAlt,
        ["Delete"] = Key.Delete,
        ["Insert"] = Key.Insert,
        ["Home"] = Key.Home,
        ["End"] = Key.End,
        ["PageUp"] = Key.PageUp,
        ["PageDown"] = Key.PageDown,
        ["CapsLock"] = Key.CapsLock,
        ["NumpadAdd"] = Key.NumPadPlus,
        ["NumpadSubtract"] = Key.NumPadMinus,
        ["NumpadMultiply"] = Key.NumPadMultiply,
        ["NumpadDivide"] = Key.NumPadDivide,
        ["NumpadDecimal"] = Key.NumPadDecimal,
        ["Minus"] = Key.Minus,
        ["Equal"] = Key.Equals,
        ["Comma"] = Key.Comma,
        ["Period"] = Key.Period,
        ["Slash"] = Key.Slash,
        ["Backslash"] = Key.Backslash,
        ["Semicolon"] = Key.Semicolon,
        ["Quote"] = Key.Quote,
        ["BracketLeft"] = Key.LeftBracket,
        ["BracketRight"] = Key.RightBracket,
        ["Backquote"] = Key.Grave,
    };

    /// <summary>The key with this <c>KeyboardEvent.code</c>, or <see cref="Key.None"/> for one Griff has no name for.</summary>
    public static Key ToGriff(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return Key.None;
        if (Named.TryGetValue(code, out var key))
            return key;

        // KeyA..KeyZ, Digit0..Digit9, Numpad0..Numpad9 and F1..F12 run in order, as Griff's do.
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && code[3] is >= 'A' and <= 'Z')
            return Key.A + (code[3] - 'A');
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && code[5] is >= '0' and <= '9')
            return Key.D0 + (code[5] - '0');
        if (code.Length == 7 && code.StartsWith("Numpad", StringComparison.Ordinal) && code[6] is >= '0' and <= '9')
            return Key.NumPad0 + (code[6] - '0');
        if (code[0] == 'F' && int.TryParse(code.AsSpan(1), out var f) && f is >= 1 and <= 12 && code.Length <= 3)
            return Key.F1 + (f - 1);
        return Key.None;
    }
}
