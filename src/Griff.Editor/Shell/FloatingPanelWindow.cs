using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Griff.Editor.Core.Panels;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Shell;

/// <summary>
/// A window of its own for floating panels, over the editor. It keeps the editor's shortcuts, remembers where it's put,
/// and closing it puts its panels back.
/// </summary>
public sealed class FloatingPanelWindow : ChromeWindow
{
    private readonly Window _main;
    private bool _quiet;
    private bool _closed;

    public FloatingPanelWindow(Window main, FloatingPanels floating)
    {
        _main = main;
        Floating = floating;
        Owner = main;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = 160;
        MinHeight = 120;

        // Undo, Save, Play and the rest work while a floating panel has focus.
        foreach (var binding in main.InputBindings.OfType<KeyBinding>())
            InputBindings.Add(new KeyBinding(binding.Command, binding.Key, binding.Modifiers));

        Closed += (_, _) => _closed = true;
        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
    }

    public FloatingPanels Floating { get; }

    /// <summary>Raised when someone closes the window (not when the layout closes it).</summary>
    public event EventHandler? ClosedByUser;

    /// <summary>Moves the window to where the layout has it, or near the middle of the editor when it's new.</summary>
    public void Place()
    {
        var bounds = Floating.Bounds;
        if (double.IsNaN(bounds.Left) || double.IsNaN(bounds.Top) || !OnScreen(bounds))
        {
            var (left, top) = _main.WindowState == WindowState.Maximized && _main.IsLoaded
                ? (SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Top)
                : (_main.Left, _main.Top);
            var (width, height) = (_main.ActualWidth > 0 ? _main.ActualWidth : _main.Width, _main.ActualHeight > 0 ? _main.ActualHeight : _main.Height);
            bounds = new Rect(left + (width - bounds.Width) / 2 + 60, top + (height - bounds.Height) / 2, bounds.Width, bounds.Height);
            Floating.Bounds = bounds;
        }

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    /// <summary>Closes it without putting its panels back: the layout has already moved them.</summary>
    public void CloseQuietly()
    {
        _quiet = true;
        Content = null;
        if (!_closed)
            Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_quiet && !e.Cancel)
            ClosedByUser?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether enough of a window's title bar is on the screens to drag it by: a kept layout can put one on a screen
    /// that's since been unplugged.
    /// </summary>
    internal static bool OnScreen(Rect bounds)
    {
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = Rect.Intersect(new Rect(bounds.Left, bounds.Top, bounds.Width, 30), screens);
        return !titleBar.IsEmpty && titleBar.Width >= 60 && titleBar.Height >= 10;
    }

    private void Remember()
    {
        if (IsLoaded && WindowState == WindowState.Normal)
            Floating.Bounds = new Rect(Left, Top, ActualWidth, ActualHeight);
    }
}
