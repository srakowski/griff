using System.Text;
using Griff.Input;

namespace Griff.Runtime;

/// <summary>
/// Short names for keys and buttons, the way a player reads them on screen: "Space", "Esc", "Left Click", "A", "RB".
/// </summary>
public static class InputPrompts
{
    /// <summary>What's shown for an action with nothing bound to it on the device being used.</summary>
    public const string Unbound = BindingNames.Unbound;

    /// <summary>What's shown for an action while a Rebind button waits for the player to press something.</summary>
    public const string Listening = "...";

    /// <summary>The name to show for <paramref name="binding"/>, e.g. "Space", "Left Click" or "A".</summary>
    public static string Name(InputBinding binding) => BindingNames.Name(binding);

    public static string Name(Key key) => BindingNames.Name(key);

    public static string Name(GamepadButton button) => BindingNames.Name(button);

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
    /// Every key and mouse button bound to <paramref name="action"/>, or every gamepad button while a gamepad is being
    /// played with, joined with " / ": "Esc / Right Click". What "{DropTool:all}" shows. Follows the player's rebinding.
    /// </summary>
    public static string AllFor(this IInput input, string action)
    {
        var bindings = input.BindingsOf(action);
        // Nothing on this device: what there is on the other, as PromptFor shows.
        return BindingNames.All(bindings, input.UsingGamepad) ?? BindingNames.All(bindings, null) ?? Unbound;
    }

    /// <summary>
    /// The binding in <paramref name="slot"/> of <paramref name="action"/> on the device being played with: 0 is its first
    /// key or mouse button (or gamepad button), 1 its second, and so on, as a Rebind button's
    /// <see cref="Scenes.Button.BindingSlot"/> counts them; <see cref="Unbound"/> past the last. What "{PanUp:1}" shows.
    /// </summary>
    public static string SlotFor(this IInput input, string action, int slot) =>
        BindingNames.InSlot(input.BindingsOf(action), input.UsingGamepad, slot) is { } binding ? Name(binding) : Unbound;

    /// <summary>
    /// <paramref name="text"/> with every action's name in braces swapped for its prompt: "Press {Jump}" becomes
    /// "Press Space". "{DropTool:all}" shows every binding on the device in use ("Esc / Right Click", see
    /// <see cref="AllFor"/>), and "{PanUp:1}" the one in a slot (see <see cref="SlotFor"/>). Braces around anything else
    /// (not an action) are left alone. <paramref name="listening"/> is an action shown as <see cref="Listening"/> instead,
    /// written the same way: "Jump" for all of its prompts, "Jump:1" for its slot 1 and those that show it whole.
    /// </summary>
    public static string Fill(IInput input, string text, string? listening = null)
    {
        if (!text.Contains('{'))
            return text;

        var actions = input.Actions;
        var (listenAction, listenSlot) = Split(listening);
        var result = new StringBuilder(text.Length);
        var at = 0;
        while (at < text.Length)
        {
            var open = text.IndexOf('{', at);
            var close = open < 0 ? -1 : text.IndexOf('}', open + 1);
            if (close < 0)
                break;

            var (name, form) = Split(text[(open + 1)..close]);
            var action = form == Form.Unknown ? null : actions.FirstOrDefault(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            result.Append(text, at, open - at);
            if (action is null)
                result.Append(text, open, close - open + 1);
            else if (listenAction is not null && action.Equals(listenAction, StringComparison.OrdinalIgnoreCase) &&
                     (form.Slot is null || listenSlot.Slot is null || form.Slot == listenSlot.Slot))
                result.Append(Listening);
            else
                result.Append(form.All ? input.AllFor(action) : form.Slot is { } slot ? input.SlotFor(action, slot) : input.PromptFor(action));
            at = close + 1;
        }

        return result.Append(text, at, text.Length - at).ToString();
    }

    // How a prompt in braces asks for its action: "Jump", "Jump:all" or "Jump:1". Anything else after the colon isn't a prompt.
    private readonly record struct Form(bool All, int? Slot)
    {
        public static readonly Form Unknown = new(true, -1);
    }

    private static (string? Name, Form Form) Split(string? prompt)
    {
        if (prompt is null)
            return (null, default);
        var colon = prompt.IndexOf(':');
        if (colon < 0)
            return (prompt.Trim(), default);
        var name = prompt[..colon].Trim();
        var form = prompt[(colon + 1)..].Trim();
        if (form.Equals("all", StringComparison.OrdinalIgnoreCase))
            return (name, new Form(true, null));
        return int.TryParse(form, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var slot)
            ? (name, new Form(false, slot))
            : (name, Form.Unknown);
    }
}
