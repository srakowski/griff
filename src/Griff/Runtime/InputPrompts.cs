using System.Text;

namespace Griff.Runtime;

/// <summary>
/// Short names for keys and buttons, the way a player reads them on screen: "Space", "Esc", "Left Click", "A", "RB".
/// </summary>
public static class InputPrompts
{
    /// <summary>What's shown for an action with nothing bound to it on the device being used.</summary>
    public const string Unbound = "-";

    /// <summary>What's shown for an action while a Rebind button waits for the player to press something.</summary>
    public const string Listening = "...";

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
    /// What to show for <paramref name="action"/> right now: its first gamepad button while a gamepad is being played
    /// with, else its first key or mouse button, e.g. "Press " + Input.PromptFor("Jump"). Follows the player's rebinding.
    /// </summary>
    public static string PromptFor(this IInput input, string action)
    {
        var bindings = input.BindingsOf(action);
        var pick = input.UsingGamepad
            ? bindings.FirstOrDefault(b => b.Gamepad is not null)
            : bindings.FirstOrDefault(b => b.Gamepad is null);
        return pick != default ? Name(pick) : bindings.Count > 0 ? Name(bindings[0]) : Unbound;
    }

    /// <summary>
    /// <paramref name="text"/> with every action's name in braces swapped for its prompt: "Press {Jump}" becomes
    /// "Press Space". Braces around anything else (not an action) are left alone. <paramref name="listening"/> is an
    /// action shown as <see cref="Listening"/> instead.
    /// </summary>
    public static string Fill(IInput input, string text, string? listening = null)
    {
        if (!text.Contains('{'))
            return text;

        var actions = input.Actions;
        var result = new StringBuilder(text.Length);
        var at = 0;
        while (at < text.Length)
        {
            var open = text.IndexOf('{', at);
            var close = open < 0 ? -1 : text.IndexOf('}', open + 1);
            if (close < 0)
                break;

            var name = text[(open + 1)..close].Trim();
            var action = actions.FirstOrDefault(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            result.Append(text, at, open - at);
            if (action is null)
                result.Append(text, open, close - open + 1);
            else if (listening is not null && action.Equals(listening.Trim(), StringComparison.OrdinalIgnoreCase))
                result.Append(Listening);
            else
                result.Append(input.PromptFor(action));
            at = close + 1;
        }

        return result.Append(text, at, text.Length - at).ToString();
    }
}
