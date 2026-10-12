using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Griff.Editor.Themes.Controls;

/// <summary>
/// A box at the top of a panel that narrows what it lists as you type: a search mark, a <see cref="Placeholder"/> while
/// it's empty, and a button (or Escape) that empties it. Bind its Text with UpdateSourceTrigger=PropertyChanged.
/// </summary>
public class FilterBox : TextBox
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(FilterBox));

    /// <summary>Empties the box: its clear button.</summary>
    public static readonly RoutedCommand ClearCommand = new(nameof(ClearCommand), typeof(FilterBox));

    public FilterBox()
    {
        IsUndoEnabled = false;
        CommitTextBox.PassUndoToEditor(this);
        CommandBindings.Add(new CommandBinding(ClearCommand, (_, _) => Clear(), (_, e) => e.CanExecute = Text.Length > 0));
    }

    /// <summary>What the box says while it's empty, e.g. "Filter fields".</summary>
    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Text.Length > 0)
        {
            Clear();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }
}
