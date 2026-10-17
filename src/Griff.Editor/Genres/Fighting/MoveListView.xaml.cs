using System.Windows.Controls;
using System.Windows.Input;

namespace Griff.Editor.Genres.Fighting;

/// <summary>
/// The Move List: the selected fighter's moves beside the move picked (its input as arrows and picked from a list of
/// motions, its clip, frames, blocking and cancels) and its frame data drawn as a bar. A document of its own in the
/// document area, which follows the selection.
/// </summary>
public partial class MoveListView : UserControl
{
    public MoveListView(MoveListEditor model)
    {
        InitializeComponent();
        DataContext = model;
    }

    public MoveListEditor Model => (MoveListEditor)DataContext;

    /// <summary>Raised by its Close button; the tab it's in closes.</summary>
    public event EventHandler? CloseRequested;

    private void OnClose(object sender, System.Windows.RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    // Enter keeps what's typed, as leaving the box does.
    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}
