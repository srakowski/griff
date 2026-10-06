using GriffKey = Griff.Runtime.Key;
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
        _ => GriffKey.None,
    };
}
