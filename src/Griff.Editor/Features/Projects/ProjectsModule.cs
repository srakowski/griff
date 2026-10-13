using System.Windows;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
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
        services
            .AddCommand<NewProjectCommand>()
            .AddCommand<OpenProjectCommand>()
            .AddCommand<ExitCommand>();
    }
}

/// <summary>
/// Opens the New Project window: a name, a folder and a template to start from. Its Templates lists every one Griff has
/// (Platformer, Top-down, Match-3...), each with what it is and how to play; set SelectedTemplate to one by its name.
/// </summary>
public sealed class NewProjectCommand(IServiceProvider services) : EditorCommand
{
    public override string Id => "Project.New";
    public override string Text => "_New Project...";
    public override string? Icon => Icons.NewProject;
    public override KeyGesture? Gesture { get; } = new(Key.N, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 0, Order: 0);

    protected override Task ExecuteCoreAsync()
    {
        var window = services.GetRequiredService<NewProjectWindow>();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
        return Task.CompletedTask;
    }
}

/// <summary>Opens a project: its folder or its project.griff file.</summary>
public sealed class OpenProjectCommand(IWorkspace workspace, IDialogService dialogs) : EditorCommand
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
            await workspace.OpenProjectAsync(path);
    }
}

/// <summary>Closes the editor (asking to save unsaved changes first).</summary>
[Destructive]
public sealed class ExitCommand : EditorCommand
{
    public override string Id => "App.Exit";
    public override string Text => "E_xit";
    public override string? Icon => Icons.Exit;

    // Alt+F4 is handled by Windows itself; this is display-only.
    public override string? GestureText => "Alt+F4";
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 9, Order: 0);

    // Closing the window routes through MainWindow.OnClosing, which handles unsaved changes.
    protected override Task ExecuteCoreAsync()
    {
        Application.Current.MainWindow?.Close();
        return Task.CompletedTask;
    }
}
