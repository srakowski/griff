using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Hierarchy;

public sealed class HierarchyModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPanel<HierarchyPanel, HierarchyView>();
        services.AddCommand<AddEntityCommand>();
    }
}

/// <summary>
/// Opens the Add Entity palette at the top of the Hierarchy: everything the Entity menu makes, by category, with what
/// was made lately at the top. Type to find something (by its name, category or what it does) and press Enter to make it
/// inside the selected entity, or at the top level. Agents can call the Entity_Create tools straight away; the palette is
/// the Hierarchy panel's Palette.
/// </summary>
public sealed class AddEntityCommand(IWorkspace workspace, HierarchyPanel panel, PanelLayout layout) : EditorCommand
{
    public override string Id => "Entity.AddEntity";
    public override string Text => "_Add Entity...";
    public override string? Icon => Icons.Add;
    public override KeyGesture? Gesture { get; } = new(Key.A, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: -1);

    protected override bool CanExecuteCore() => workspace.Scene is not null;

    protected override Task ExecuteCoreAsync()
    {
        layout.Show(panel.Id);
        panel.IsAddOpen = true;
        return Task.CompletedTask;
    }
}
