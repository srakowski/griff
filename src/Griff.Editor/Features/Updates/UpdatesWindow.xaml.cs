using System.Windows;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.Updates;

/// <summary>
/// Help > Check for Updates' window: what <see cref="EditorUpdates"/> is doing (checking, downloading, with a bar) and then
/// what came of it. It doesn't hold the editor up; closing it leaves a download going.
/// </summary>
public partial class UpdatesWindow : ChromeWindow
{
    public UpdatesWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
