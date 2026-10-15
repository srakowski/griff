using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.Story;

/// <summary>
/// The Dialogue Editor's window: the selected dialogue's lines beside the line picked (who says it, how they look, what
/// they say, its stage directions) and a preview of the stage at that line. Not modal, and it follows the selection.
/// </summary>
public partial class DialogueEditorWindow : ChromeWindow
{
    public DialogueEditorWindow(DialogueEditor model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
    }

    public DialogueEditor Model => (DialogueEditor)DataContext;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox or ComboBox)
            return;
        if (e.Key == Key.Escape)
            Close();
        else
            return;
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
