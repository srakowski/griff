using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.Art;

/// <summary>
/// Paints pixel art for sprites and tilemaps, saved straight into the project. Not modal, so a sprite can be painted,
/// saved, and seen in the scene (or the running game) without closing it.
/// </summary>
public partial class SpritePainterWindow : ChromeWindow
{
    public SpritePainterWindow(SpritePainterViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
        Closed += (_, _) => model.Stop();
    }

    public SpritePainterViewModel Model => (SpritePainterViewModel)DataContext;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var typing = e.OriginalSource is TextBox;
        if (modifiers == ModifierKeys.Control && e.Key == Key.S)
            Model.Save();
        else if (typing)
            return;
        else if (modifiers == ModifierKeys.Control && e.Key == Key.Z)
            Model.Undo();
        else if (modifiers == ModifierKeys.Control && e.Key == Key.Y || modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
            Model.Redo();
        else if (modifiers != ModifierKeys.None)
            return;
        else if (e.Key == Key.Escape)
            Close();
        else if (e.Key is Key.Left or Key.OemComma)
            Model.Frame--;
        else if (e.Key is Key.Right or Key.OemPeriod)
            Model.Frame++;
        else if (e.Key == Key.M)
            Model.Mirror = !Model.Mirror;
        else if (Model.Tools.FirstOrDefault(t => t.Key == e.Key.ToString()) is { } tool)
            Model.Tool = tool.Tool;
        else
            return;
        e.Handled = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
