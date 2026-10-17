using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Griff.Editor.Features.Art;

/// <summary>
/// Paints pixel art for sprites and tilesets, saved straight into the project: a document of its own in the document area,
/// one tab per picture, so a sprite can be painted, saved, and seen in the scene (or the running game) a tab away. Undo
/// and Redo are the editor's (its view model is the document they go to while it's worked in).
/// </summary>
public partial class SpritePainterView : UserControl
{
    public SpritePainterView(SpritePainterViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
    }

    public SpritePainterViewModel Model => (SpritePainterViewModel)DataContext;

    /// <summary>Raised by its Close button; the tab it's in closes (asking about unsaved changes).</summary>
    public event EventHandler? CloseRequested;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var typing = e.OriginalSource is TextBox;
        if (modifiers == ModifierKeys.Control && e.Key == Key.S)
            Model.Save();
        else if (typing)
            return;
        else if (modifiers != ModifierKeys.None)
            return;
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

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
