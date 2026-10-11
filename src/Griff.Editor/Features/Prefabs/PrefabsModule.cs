using System.IO;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Prefabs;

/// <summary>Prefabs open on their own, each in a tab of its own beside the scene's.</summary>
public sealed class PrefabsModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services
        .AddSingleton<PrefabTabs>()
        .AddCommand<OpenPrefabCommand>()
        .AddCommand<EditPrefabCommand>()
        .AddCommand<ClosePrefabCommand>();
}

/// <summary>
/// Opens one of the project's prefabs to edit on its own, in a tab of its own: the Hierarchy, Inspector and Scene view
/// edit it until the scene's tab is picked again. Saving it (Save, Ctrl+S) updates every copy of it in every scene.
/// </summary>
public sealed class OpenPrefabCommand(IWorkspace workspace, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Prefab.Open";
    public override string Text => "Open P_refab...";
    public override string? Icon => Icons.Package;
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 2, Order: 0);

    protected override bool CanExecuteCore() => workspace.Project is not null && !workspace.IsPlaying;

    protected override async Task ExecuteCoreAsync()
    {
        // A prefab's name or its path in Assets will do, as well as a full path.
        if (workspace.Project is not null && dialogs.ShowOpenFileDialog("Griff prefabs (*.prefab)|*.prefab") is { } chosen)
            await workspace.OpenPrefabAsync(chosen);
    }
}

/// <summary>
/// Opens the prefab the selected copy was placed from in a tab of its own, to edit the prefab itself rather than this
/// copy of it. Saving it updates every copy, keeping what each changed.
/// </summary>
public sealed class EditPrefabCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.EditPrefab";
    public override string Text => "_Edit Prefab";
    public override string? Icon => Icons.Package;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 4);

    protected override bool CanExecuteCore() => !workspace.IsPlaying && Link() is not null;

    protected override Task ExecuteCoreAsync() => Link() is { } link ? workspace.OpenPrefabAsync(link) : Task.CompletedTask;

    // The prefab of the copy the selection is (or is inside).
    private string? Link() => selection.Entity is { } entity ? PrefabOverrides.CopyFor(entity)?.Prefab : null;
}

/// <summary>
/// Closes the prefab whose tab is showing, asking to save its changes first, and shows the scene again.
/// </summary>
public sealed class ClosePrefabCommand(IWorkspace workspace) : EditorCommand
{
    public override string Id => "Prefab.Close";
    public override string Text => "_Close Prefab";
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 2, Order: 1);

    protected override bool CanExecuteCore() => workspace.Scene is { IsPrefab: true };

    protected override Task ExecuteCoreAsync() =>
        workspace.Scene is { IsPrefab: true } prefab ? workspace.ClosePrefabAsync(prefab) : Task.CompletedTask;
}
