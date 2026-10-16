using System.IO;
using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Projects;

namespace Griff.Editor.Shell;

/// <summary>Shown when no project is open: create, open, or pick a recent project.</summary>
public partial class StartPage : UserControl
{
    private readonly IProjectSession _projectSession;
    private readonly IDialogService _dialogs;

    public StartPage(IProjectSession projectSession, IDialogService dialogs, CommandRegistry commands)
    {
        InitializeComponent();
        _projectSession = projectSession;
        _dialogs = dialogs;
        NewButton.Command = commands.Get<NewProjectCommand>();
        OpenButton.Command = commands.Get<OpenProjectCommand>();

        projectSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.RecentProjects))
                ShowRecent();
        };
        ShowRecent();
    }

    private void ShowRecent()
    {
        RecentList.ItemsSource = _projectSession.RecentProjects.ToList();
        RecentHeader.Visibility = _projectSession.RecentProjects.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnRecentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
            return;

        try
        {
            await _projectSession.OpenProjectAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _dialogs.ShowError("Couldn't open project", ex.Message);
        }
    }
}
