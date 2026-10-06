using System.Windows;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.DesignSystem;

public sealed class DesignSystemModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<DesignSystemWindow>();
        services.AddCommand<ShowDesignSystemCommand>();
    }
}

public sealed class ShowDesignSystemCommand(IServiceProvider services) : EditorCommand
{
    private DesignSystemWindow? _window;

    public override string Id => "Help.DesignSystem";
    public override string Text => "_Design System Gallery";
    public override string? Icon => Icons.Palette;
    public override MenuPlacement? Placement { get; } = new(Menus.Help, Group: 0, Order: 0);

    protected override Task ExecuteCoreAsync()
    {
        if (_window is not null)
        {
            _window.Activate();
            return Task.CompletedTask;
        }

        _window = services.GetRequiredService<DesignSystemWindow>();
        _window.Owner = Application.Current.MainWindow;
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        return Task.CompletedTask;
    }
}
