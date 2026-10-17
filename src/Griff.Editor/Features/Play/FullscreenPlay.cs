using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.Play;

/// <summary>
/// Shows the game fullscreen while <see cref="PlayMode.IsFullscreen"/> says so: the Game tab's view (the game, and the
/// timeline over it while it's paused) moves into a window with no border filling the screen the editor is on, and back
/// into the tab after. The game's keys come from that window meanwhile, and the editor's shortcuts work there as in its own.
/// </summary>
public sealed class FullscreenPlay
{
    private readonly PlayMode _play;
    private readonly PlayShortcuts _shortcuts;
    private readonly EditorWindow _editorWindow;
    private Window? _window;

    public FullscreenPlay(PlayMode play, PlayShortcuts shortcuts, EditorWindow editorWindow)
    {
        (_play, _shortcuts, _editorWindow) = (play, shortcuts, editorWindow);
        play.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(PlayMode.IsFullscreen))
                return;
            if (play.IsFullscreen)
                Show();
            else
                PutBack(close: true);
        };
    }

    /// <summary>The window the game is fullscreen in, while it is.</summary>
    public Window? Window => _window;

    private void Show()
    {
        if (_window is not null || _editorWindow.Main is not { } main || _editorWindow.GameTab is not { Content: UIElement view } tab)
            return;

        var window = new Window
        {
            Title = main.Title,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = 640,
            Height = 360,
            Background = Brushes.Black,
            Owner = main,
            ShowInTaskbar = false,
        };

        // The editor's shortcuts, the Game menu's first while the game takes the keys (so before the game hears them).
        foreach (var binding in main.InputBindings.OfType<KeyBinding>())
            window.InputBindings.Add(new KeyBinding { Command = binding.Command, Gesture = binding.Gesture });
        _shortcuts.Attach(window);

        // Made on the screen the editor is on, then maximized there: with no border, that's the whole screen.
        window.SourceInitialized += (_, _) => ChromeWindow.MoveToScreenOf(window, main);
        window.Closing += OnClosing;
        _window = window;

        // The game's surface keeps its own window (and its OpenGL) as it moves: WPF parks it while it has no parent.
        tab.Content = null;
        window.Content = view;
        window.Show();
        window.WindowState = WindowState.Maximized;
        window.Activate();
        _play.TakeKeysFrom(window);
        Keyboard.Focus(window);

        // The editor's window is behind the game: going back to it (the taskbar, Alt+Tab, a dialog closing) goes to the game.
        main.Activated += OnMainActivated;
    }

    // Once the editor's window has finished becoming active, not in the middle of it.
    private void OnMainActivated(object? sender, EventArgs e) => _window?.Dispatcher.BeginInvoke(() => _window?.Activate());

    // Closed some other way than leaving fullscreen (Alt+F4, the editor closing): the game goes back to its tab first.
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (sender != _window)
            return;
        PutBack(close: false);
        _play.IsFullscreen = false;
    }

    private void PutBack(bool close)
    {
        if (_window is not { } window)
            return;

        _window = null;
        var main = _editorWindow.Main;
        if (main is not null)
            main.Activated -= OnMainActivated;
        _play.TakeKeysFrom(null);
        var view = window.Content;
        window.Content = null;
        if (_editorWindow.GameTab is { } tab)
            tab.Content = view;
        if (!close)
            return;

        window.Close();
        if (main is { IsVisible: true })
        {
            main.Activate();
            main.Focus();
        }
    }
}
