using GriffKey = Griff.Input.Key;
using WpfKey = System.Windows.Input.Key;

namespace Griff.Platform.Windows;

/// <summary>Maps WPF keys to Griff keys.</summary>
public static class KeyMap
{
    public static GriffKey ToGriff(WpfKey key) => key switch
    {
        >= WpfKey.A and <= WpfKey.Z => GriffKey.A + (key - WpfKey.A),
        >= WpfKey.D0 and <= WpfKey.D9 => GriffKey.D0 + (key - WpfKey.D0),
        >= WpfKey.F1 and <= WpfKey.F12 => GriffKey.F1 + (key - WpfKey.F1),
        WpfKey.Space => GriffKey.Space,
        WpfKey.Enter => GriffKey.Enter,
        WpfKey.Escape => GriffKey.Escape,
        WpfKey.Tab => GriffKey.Tab,
        WpfKey.Back => GriffKey.Backspace,
        WpfKey.Left => GriffKey.Left,
        WpfKey.Right => GriffKey.Right,
        WpfKey.Up => GriffKey.Up,
        WpfKey.Down => GriffKey.Down,
        WpfKey.LeftShift => GriffKey.LeftShift,
        WpfKey.RightShift => GriffKey.RightShift,
        WpfKey.LeftCtrl => GriffKey.LeftControl,
        WpfKey.RightCtrl => GriffKey.RightControl,
        WpfKey.LeftAlt => GriffKey.LeftAlt,
        WpfKey.RightAlt => GriffKey.RightAlt,
        WpfKey.Delete => GriffKey.Delete,
        WpfKey.Insert => GriffKey.Insert,
        WpfKey.Home => GriffKey.Home,
        WpfKey.End => GriffKey.End,
        WpfKey.PageUp => GriffKey.PageUp,
        WpfKey.PageDown => GriffKey.PageDown,
        WpfKey.CapsLock => GriffKey.CapsLock,
        >= WpfKey.NumPad0 and <= WpfKey.NumPad9 => GriffKey.NumPad0 + (key - WpfKey.NumPad0),
        WpfKey.Add => GriffKey.NumPadPlus,
        WpfKey.Subtract => GriffKey.NumPadMinus,
        WpfKey.Multiply => GriffKey.NumPadMultiply,
        WpfKey.Divide => GriffKey.NumPadDivide,
        WpfKey.Decimal => GriffKey.NumPadDecimal,
        // Named for a US layout; on others they're the keys in the same places.
        WpfKey.OemMinus => GriffKey.Minus,
        WpfKey.OemPlus => GriffKey.Equals,
        WpfKey.OemComma => GriffKey.Comma,
        WpfKey.OemPeriod => GriffKey.Period,
        WpfKey.OemQuestion => GriffKey.Slash,
        WpfKey.OemPipe => GriffKey.Backslash,
        WpfKey.OemSemicolon => GriffKey.Semicolon,
        WpfKey.OemQuotes => GriffKey.Quote,
        WpfKey.OemOpenBrackets => GriffKey.LeftBracket,
        WpfKey.OemCloseBrackets => GriffKey.RightBracket,
        WpfKey.OemTilde => GriffKey.Grave,
        _ => GriffKey.None,
    };
}
