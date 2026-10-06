using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Core.Dialogs;

/// <summary>Asks for one line of text, validating as the user types.</summary>
public partial class PromptWindow : ChromeWindow
{
    private readonly Func<string, string?> _validate;

    public PromptWindow(string title, string label, string initial, Func<string, string?> validate)
    {
        InitializeComponent();
        _validate = validate;
        Title = title;
        LabelText.Text = label;
        Input.Text = initial;
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
        Validate();
    }

    public string Value => Input.Text.Trim();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Validate();

    private void Validate()
    {
        var error = _validate(Value);
        ErrorText.Text = error;
        ErrorPanel.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = error is null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_validate(Value) is null)
            DialogResult = true;
    }
}
