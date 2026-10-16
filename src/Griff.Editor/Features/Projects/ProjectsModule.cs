using Griff.Editor.Core;
using System.Windows;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Projects;

// File menu groups: 0 = project, 1 = scene (ScenesModule), 9 = exit.
public sealed class ProjectsModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<NewProjectWindow>();
        services.AddFactory<NewProjectWindow>();
        services
            .AddCommand<NewProjectCommand>()
            .AddCommand<OpenProjectCommand>()
            .AddCommand<CloseProjectCommand>()
            .AddCommand<ExitCommand>();
    }
}

/// <summary>
/// Opens the New Project window: a name, a folder and a template to start from. Its Templates lists every one Griff has
/// (Platformer, Top-down, Match-3...), each with what it is and how to play; set SelectedTemplate to one by its name.
/// </summary>
public sealed class NewProjectCommand(Func<NewProjectWindow> createWindow, Core.EditorWindow editorWindow) : EditorCommand
{
    public override string Id => "Project.New";
    public override string Text => "_New Project...";
    public override string? Icon => Icons.NewProject;
    public override KeyGesture? Gesture { get; } = new(Key.N, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 0, Order: 0);

    protected override Task ExecuteCoreAsync()
    {
        var window = createWindow();
        window.Owner = editorWindow.Main;
        window.ShowDialog();
        return Task.CompletedTask;
    }
}

/// <summary>Opens a project: its folder or its project.griff file.</summary>
public sealed class OpenProjectCommand(IProjectSession projectSession, IDialogService dialogs) : EditorCommand
{
    public const string ProjectFilter = "Griff project (project.griff)|project.griff";

    public override string Id => "Project.Open";
    public override string Text => "_Open Project...";
    public override string? Icon => Icons.Open;
    public override KeyGesture? Gesture { get; } = new(Key.O, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 0, Order: 1);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 0, Order: 0);

    protected override async Task ExecuteCoreAsync()
    {
        if (dialogs.ShowOpenFileDialog(ProjectFilter) is { } path)
            await projectSession.OpenProjectAsync(path);
    }
}

/// <summary>
/// Closes the open project and returns the editor to its start, with no project open: stops the game if one is playing and
/// asks to save what's unsaved first (save, don't save or cancel). Open or create a project afterwards to carry on.
/// </summary>
public sealed class CloseProjectCommand(IProjectSession projectSession, PlayMode play) : EditorCommand
{
    public override string Id => "Project.Close";
    public override string Text => "_Close Project";
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 0, Order: 2);

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override async Task ExecuteCoreAsync()
    {
        play.Stop();
        await projectSession.CloseProjectAsync();
    }
}

/// <summary>Closes the editor (asking to save unsaved changes first).</summary>
[Destructive]
public sealed class ExitCommand(IProjectSession projectSession, Core.EditorWindow editorWindow) : EditorCommand
{
    public override string Id => "App.Exit";
    public override string Text => "E_xit";
    public override string? Icon => Icons.Exit;

    // Alt+F4 is handled by Windows itself; this is display-only.
    public override string? GestureText => "Alt+F4";
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 9, Order: 0);

    // The unsaved-changes questions are asked here, where the command can wait for the answer (and an agent can give it),
    // then the window closes without asking again.
    protected override async Task ExecuteCoreAsync()
    {
        if (await projectSession.ConfirmCloseProjectAsync())
            CloseWindow();
    }

    /// <summary>Closes the editor's window once the questions are answered.</summary>
    private void CloseWindow() => editorWindow.CloseConfirmed();
}
