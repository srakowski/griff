using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.Audio;

/// <summary>
/// Makes sound effects and music for the game, saved straight into the project. Not modal, so a sound can be made,
/// saved, and heard in the running game without closing it.
/// </summary>
public partial class SoundMakerWindow : ChromeWindow
{
    public SoundMakerWindow(SoundMakerViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
    }

    public SoundMakerViewModel Model => (SoundMakerViewModel)DataContext;

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
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
