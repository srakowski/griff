using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Griff.Editor.Themes.Controls;
using Griff.Platform.Windows;
using Griff.Runtime;
using WpfKey = System.Windows.Input.Key;

namespace Griff.Editor.Features.Controls;

/// <summary>
/// Edits the game's controls: pick an action, then press the keys (or click the mouse buttons, or press the controller
/// buttons) that should do it. Not modal, so it can stay open beside a running game.
/// </summary>
public partial class ControlsWindow : ChromeWindow
{
    private readonly DispatcherTimer _padTimer;
    private readonly InputState _pad = new();

    public ControlsWindow(ControlsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
        PreviewMouseDown += OnMouseDown;
        _padTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => PollPad(), Dispatcher);
        _padTimer.Stop();
        model.PropertyChanged += OnModelChanged;
        Closed += (_, _) =>
        {
            _padTimer.Stop();
            model.PropertyChanged -= OnModelChanged;
        };
    }

    public ControlsViewModel Model => (ControlsViewModel)DataContext;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ControlsViewModel.Listening))
            return;
        if (Model.IsListeningForPad)
        {
            // Buttons already held don't count: only one pressed from now on.
            XInputGamepad.Poll(_pad);
            _pad.EndFrame();
            _padTimer.Start();
        }
        else
        {
            _padTimer.Stop();
        }
    }

    private void PollPad()
    {
        XInputGamepad.Poll(_pad);
        if (!_pad.IsGamepadConnected)
            Model.NoController();
        else if (_pad.AnyPressed is { Gamepad: not null } pressed)
            Model.Add(pressed);
        _pad.EndFrame();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == WpfKey.System ? e.SystemKey : e.Key;
        if (Model.Listening != ListenFor.Nothing)
        {
            e.Handled = true;
            if (key == WpfKey.Escape)
                Model.Listening = ListenFor.Nothing;
            else if (Model.IsListeningForKey && KeyMap.ToGriff(key) is var griff and not Griff.Input.Key.None)
                Model.Add(griff);
        }
        else if (key == WpfKey.Enter && e.OriginalSource == NameBox)
        {
            NameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            NameBox.SelectAll();
            e.Handled = true;
        }
        else if (key == WpfKey.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    // While waiting for a key, clicking the waiting button with any mouse button binds that button; clicking anywhere
    // else stops waiting.
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Model.IsListeningForKey)
            return;
        if (!IsInside(e.OriginalSource as DependencyObject, ListenKeyButton))
        {
            Model.Listening = ListenFor.Nothing;
            return;
        }

        e.Handled = true;
        Griff.Runtime.MouseButton? button = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Left => Griff.Runtime.MouseButton.Left,
            System.Windows.Input.MouseButton.Right => Griff.Runtime.MouseButton.Right,
            System.Windows.Input.MouseButton.Middle => Griff.Runtime.MouseButton.Middle,
            _ => null,
        };
        if (button is { } pressed)
            Model.Add(pressed);
    }

    private void OnAddPad(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddPadButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var choice in Model.GamepadChoices)
            menu.Items.Add(new MenuItem { Header = choice.Label, Command = choice.Command, Tag = $"pad:{choice.Button}" });
        menu.IsOpen = true;
    }

    private static bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        for (var e = element; e is not null; e = e is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
        {
            if (e == ancestor)
                return true;
        }

        return false;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
