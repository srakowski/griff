using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Griff.Editor.Features.Audio;

/// <summary>
/// Makes sound effects and music for the game, saved straight into the project: a document of its own in the document
/// area, one tab per sound, so a sound can be made, saved, and heard in the running game a tab away.
/// </summary>
public partial class SoundMakerView : UserControl
{
    public SoundMakerView(SoundMakerViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
    }

    public SoundMakerViewModel Model => (SoundMakerViewModel)DataContext;

    /// <summary>Raised by its Close button; the tab it's in closes (asking about unsaved changes).</summary>
    public event EventHandler? CloseRequested;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Model.Save();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && e.OriginalSource is not TextBox)
        {
            Model.Play();
            e.Handled = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
