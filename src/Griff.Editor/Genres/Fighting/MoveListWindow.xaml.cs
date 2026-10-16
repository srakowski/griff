using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Genres.Fighting;

/// <summary>
/// The Move List's window: the selected fighter's moves beside the move picked (its input as arrows and picked from a
/// list of motions, its clip, frames, blocking and cancels) and its frame data drawn as a bar. Not modal, and it follows
/// the selection.
/// </summary>
public partial class MoveListWindow : ChromeWindow
{
    public MoveListWindow(MoveListEditor model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
    }

    public MoveListEditor Model => (MoveListEditor)DataContext;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox or ComboBox)
            return;
        if (e.Key != Key.Escape)
            return;
        Close();
        e.Handled = true;
    }

    private void OnClose(object sender, System.Windows.RoutedEventArgs e) => Close();

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
