#if DEBUG
using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Features.Controls;
using Griff.Input;
using Griff.Runtime;
using WpfKey = System.Windows.Input.Key;

namespace Griff.Editor.Diagnostics.Demo;

// Controls: the Controls window's actions, keys and buttons.
internal sealed partial class DemoUi
{
    /// <summary>The open Controls window, if any.</summary>
    public static ControlsWindow? ControlsWindow =>
        Application.Current.Windows.OfType<ControlsWindow>().FirstOrDefault(w => w.IsVisible);

    private static ControlsWindow OpenControls => ControlsWindow ?? throw new InvalidOperationException("The Controls window isn't open.");

    /// <summary>Waits for the Controls window to open (after Game > Controls...).</summary>
    public static async Task<ControlsWindow> WaitForControls()
    {
        var window = await WaitFor(() => ControlsWindow, "the Controls window");
        await Settle(400);
        return window;
    }

    /// <summary>
    /// A part of the open Controls window: actions (the list), action:Jump (its row), addaction, move, name, builtin,
    /// usedby, keys, pads, chip:Space (a key or button on the selected action), remove:Space (its ×), listenkey, addpad,
    /// listenpad, playerchoice, code, reset, delete, status, close; or the whole window.
    /// </summary>
    public static FrameworkElement ControlsPart(string part)
    {
        var window = OpenControls;
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        FrameworkElement Named(string x) => (FrameworkElement)window.FindName(x);
        return kind.ToLowerInvariant() switch
        {
            "" or "window" => (FrameworkElement)window.Content,
            "actions" => Named("ActionPanel"),
            "action" => ActionItem(window, name) ?? throw new InvalidOperationException($"No action {name} in the list."),
            "addaction" => Named("AddActionButton"),
            "move" => Named("MoveNote"),
            "name" => Named("NameBox"),
            "builtin" => Named("BuiltInTag"),
            "usedby" => Named("UsedByText"),
            "keys" => Named("KeyPanel"),
            "pads" => Named("PadPanel"),
            "chip" => Chip(window, name) ?? throw new InvalidOperationException($"{name} isn't on the selected action."),
            "remove" => (Chip(window, name) is { } chip ? Descendants<Button>(chip).FirstOrDefault(b => b.Tag as string == "chip:remove") : null)
                ?? throw new InvalidOperationException($"{name} isn't on the selected action."),
            "listenkey" => Named("ListenKeyButton"),
            "addpad" => Named("AddPadButton"),
            "listenpad" => Named("ListenPadButton"),
            "playerchoice" => Named("PlayerChoiceText"),
            "code" => Named("CodeText"),
            "reset" => Named("ResetButton"),
            "delete" => Named("DeleteButton"),
            "status" => Named("StatusText"),
            "close" => Named("CloseButton"),
            _ => throw new FormatException($"Unknown Controls part \"{part}\" (actions, action:, addaction, move, name, builtin, usedby, keys, pads, chip:, remove:, listenkey, addpad, listenpad, playerchoice, code, reset, delete, status, close)."),
        };
    }

    private static ListBoxItem? ActionItem(ControlsWindow window, string name) =>
        Descendants<ListBoxItem>(window).FirstOrDefault(i => i.DataContext is ActionRow a && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && i.IsVisible);

    // A chip's outline, found by the text it shows ("Space", "Left Click", "LB") or the binding's own name ("Gamepad LeftShoulder").
    private static StackPanel? Chip(ControlsWindow window, string name) =>
        Descendants<StackPanel>(window).FirstOrDefault(p => p.DataContext is BindingChip c && p.IsVisible && p.Children.Count == 2 && p.Children[0] is Border &&
            (c.Text.Equals(name, StringComparison.OrdinalIgnoreCase) || c.Binding.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Clicks an action in the Controls window's list.</summary>
    public async Task PickAction(string name)
    {
        var window = OpenControls;
        // The list only makes rows for what it shows, so an action further down is scrolled to first.
        if (ActionItem(window, name) is null && window.Model.Actions.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } row)
            Descendants<ListBox>(window).FirstOrDefault(l => l.Items.Contains(row))?.ScrollIntoView(row);
        var item = await WaitFor(() => ActionItem(window, name), $"{name} in the actions");
        await Click(item);
        if (window.Model.Selected?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) != true)
        {
            window.Model.Selected = window.Model.Actions.First(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Log?.Invoke($"FORCED action {name}");
            await Settle();
        }
    }

    /// <summary>
    /// Adds a key or mouse button to the selected action the way someone would: click Press a Key, then press the key
    /// (or click the button with that mouse button). <paramref name="text"/> is a binding's name: "W", "Mouse Right".
    /// </summary>
    public async Task BindKey(string text)
    {
        var window = OpenControls;
        if (!InputBinding.TryParse(text, out var binding) || binding.Gamepad is not null)
            throw new FormatException($"\"{text}\" isn't a key or mouse button (W, Space, LeftShift, Mouse Right).");
        var row = window.Model.Selected ?? throw new InvalidOperationException("No action is selected.");

        var button = (FrameworkElement)window.FindName("ListenKeyButton");
        await Click(button);
        await Settle(250);
        if (binding.Key is { } key)
            await input.Press(DemoKey(key));
        else if (binding.Mouse is Griff.Input.MouseButton.Right)
            await input.Click(Center(button), right: true, glide: false);
        else if (binding.Mouse is Griff.Input.MouseButton.Left)
            await input.Click(Center(button), glide: false);
        await Settle(200);

        if (!row.Bindings.Contains(binding))
        {
            window.Model.Add(binding);
            Log?.Invoke($"FORCED bind {text}");
            await Settle();
        }
    }

    /// <summary>Adds a gamepad button to the selected action from the Add Button list, e.g. "LB" or "A".</summary>
    public async Task BindPad(string text)
    {
        var window = OpenControls;
        var choice = window.Model.GamepadChoices.FirstOrDefault(c => InputPrompts.Name(c.Button).Equals(text, StringComparison.OrdinalIgnoreCase) || c.Button.ToString().Equals(text, StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException($"No gamepad button {text}.");
        var row = window.Model.Selected ?? throw new InvalidOperationException("No action is selected.");

        await Click((FrameworkElement)window.FindName("AddPadButton"));
        var item = await WaitFor(() => Find<MenuItem>(m => m.Tag as string == $"pad:{choice.Button}" && m.IsVisible), $"{text} in the list", 3000);
        await Settle(250);
        await Click(item);
        await Settle(150);

        if (!row.Bindings.Contains(choice.Button))
        {
            if (Find<ContextMenu>(m => m.IsOpen) is { } open)
                open.IsOpen = false;
            window.Model.Add(choice.Button);
            Log?.Invoke($"FORCED bindpad {text}");
            await Settle();
        }
    }

    // The WPF key that types a Griff key (the demo presses real keys).
    private static WpfKey DemoKey(Griff.Input.Key key) =>
        Enum.GetValues<WpfKey>().FirstOrDefault(k => k != WpfKey.None && Griff.Platform.Windows.KeyMap.ToGriff(k) == key) is var wpf and not WpfKey.None
            ? wpf
            : throw new FormatException($"The demo can't press {key}.");
}
#endif
