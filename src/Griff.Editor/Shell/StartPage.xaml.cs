using System.IO;
using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Workspace;

namespace Griff.Editor.Shell;

/// <summary>Shown when no project is open: create, open, or pick a recent project.</summary>
public partial class StartPage : UserControl
{
    private readonly IWorkspace _workspace;
    private readonly IDialogService _dialogs;

    public StartPage(IWorkspace workspace, IDialogService dialogs, CommandRegistry commands)
    {
        InitializeComponent();
        _workspace = workspace;
        _dialogs = dialogs;
        NewButton.Command = commands.Get("Project.New");
        OpenButton.Command = commands.Get("Project.Open");

        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.RecentProjects))
                ShowRecent();
        };
        ShowRecent();
    }

    private void ShowRecent()
    {
        RecentList.ItemsSource = _workspace.RecentProjects.ToList();
        RecentHeader.Visibility = _workspace.RecentProjects.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnRecentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
            return;

        try
        {
            await _workspace.OpenProjectAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _dialogs.ShowError("Couldn't open project", ex.Message);
        }
    }
}
