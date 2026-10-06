using System.Windows;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Projects;

public sealed class ProjectsModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<NewProjectWindow>();
        services.AddCommand<NewProjectCommand>();
    }
}

public sealed class NewProjectCommand(IServiceProvider services) : EditorCommand
{
    public override string Id => "Project.New";
    public override string Text => "New _Project...";
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
