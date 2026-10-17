namespace Griff.Input;

/// <summary>
/// The names keys and buttons are shown by (what <c>InputPrompts</c> fills prompts in with), and which of an action's
/// bindings is in a slot: with the bindings, so a component's Scene view preview names them as the game does.
/// </summary>
internal static class BindingNames
{
    public const string Unbound = "-";

    /// <summary>The name to show for <paramref name="binding"/>, e.g. "Space", "Left Click" or "A".</summary>
    public static string Name(InputBinding binding) =>
        binding.Key is { } key ? Name(key)
        : binding.Mouse is { } mouse ? $"{mouse} Click"
        : binding.Gamepad is { } pad ? Name(pad)
        : Unbound;

    public static string Name(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
        Key.Escape => "Esc",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftControl or Key.RightControl => "Ctrl",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        Key.CapsLock => "Caps Lock",
        Key.Delete => "Del",
        Key.Insert => "Ins",
        Key.NumPadPlus => "Num +",
        Key.NumPadMinus => "Num -",
        Key.NumPadMultiply => "Num *",
        Key.NumPadDivide => "Num /",
        Key.NumPadDecimal => "Num .",
        Key.Minus => "-",
        Key.Equals => "=",
        Key.Comma => ",",
        Key.Period => ".",
        Key.Slash => "/",
        Key.Backslash => "\\",
        Key.Semicolon => ";",
        Key.Quote => "'",
        Key.LeftBracket => "[",
        Key.RightBracket => "]",
        Key.Grave => "`",
        Key.None => Unbound,
        _ => key.ToString(),
    };

    public static string Name(GamepadButton button) => button switch
    {
        GamepadButton.LeftShoulder => "LB",
        GamepadButton.RightShoulder => "RB",
        GamepadButton.LeftTrigger => "LT",
        GamepadButton.RightTrigger => "RT",
        GamepadButton.LeftStick => "LS",
        GamepadButton.RightStick => "RS",
        GamepadButton.DPadUp => "D-Pad Up",
        GamepadButton.DPadDown => "D-Pad Down",
        GamepadButton.DPadLeft => "D-Pad Left",
        GamepadButton.DPadRight => "D-Pad Right",
        _ => button.ToString(),
    };

    /// <summary>
    /// The binding in <paramref name="slot"/> among <paramref name="bindings"/> of the gamepad's (or the keyboard and
    /// mouse's, with <paramref name="gamepad"/> false), counting from 0; null past the last.
    /// </summary>
    public static InputBinding? InSlot(IReadOnlyList<InputBinding> bindings, bool gamepad, int slot)
    {
        foreach (var binding in bindings)
        {
            if ((binding.Gamepad is not null) == gamepad && slot-- == 0)
                return binding;
        }

        return null;
    }

    /// <summary>
    /// The names of <paramref name="bindings"/> on the gamepad (or the keyboard and mouse, with <paramref name="gamepad"/>
    /// false, or every one with null), each once, joined with " / ": "Esc / Right Click"; null for none.
    /// </summary>
    public static string? All(IReadOnlyList<InputBinding> bindings, bool? gamepad)
    {
        List<string>? names = null;
        foreach (var binding in bindings)
        {
            if (gamepad is { } pad && (binding.Gamepad is not null) != pad)
                continue;
            names ??= new List<string>(bindings.Count);
            if (Name(binding) is var name && !names.Contains(name))
                names.Add(name);
        }

        return names is null ? null : string.Join(" / ", names);
    }
}
