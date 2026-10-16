using System.ComponentModel;
using System.Windows;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Controls;

// Game > Controls: the game's input actions and their keys and buttons.
public sealed class ControlsModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddCommand<ShowControlsCommand>();
    }
}

/// <summary>Opens the Controls window for the open project (one at a time; a new project closes it).</summary>
public sealed class ShowControlsCommand : EditorCommand
{
    private readonly IProjectSession _projectSession;
    private readonly PlayMode _play;
    private readonly Core.EditorWindow _editorWindow;
    private ControlsWindow? _window;

    public ShowControlsCommand(IProjectSession projectSession, PlayMode play, Core.EditorWindow editorWindow)
    {
        _editorWindow = editorWindow;
        _projectSession = projectSession;
        _play = play;
        projectSession.PropertyChanged += OnWorkspaceChanged;
    }

    public override string Id => "Game.Controls";
    public override string Text => "C_ontrols...";
    public override string? Icon => Icons.Gamepad;
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 2, Order: -1);

    protected override bool CanExecuteCore() => _projectSession.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (_window is not null)
        {
            _window.Activate();
            return Task.CompletedTask;
        }

        _window = new ControlsWindow(new ControlsViewModel(_projectSession, _play)) { Owner = _editorWindow.Main };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        return Task.CompletedTask;
    }

    // The window edits one project's controls, so it closes when another project opens.
    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IProjectSession.Project))
            _window?.Close();
    }
}
