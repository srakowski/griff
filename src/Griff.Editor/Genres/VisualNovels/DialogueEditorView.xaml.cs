using System.Windows.Controls;
using System.Windows.Input;

namespace Griff.Editor.Genres.VisualNovels;

/// <summary>
/// The Dialogue Editor: the selected dialogue's lines beside the line picked (who says it, how they look, what they say,
/// its stage directions) and a preview of the stage at that line. A document of its own in the document area, which
/// follows the selection.
/// </summary>
public partial class DialogueEditorView : UserControl
{
    public DialogueEditorView(DialogueEditor model)
    {
        InitializeComponent();
        DataContext = model;
    }

    public DialogueEditor Model => (DialogueEditor)DataContext;

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
