using System.Windows.Input;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Entities;

// Entity menu groups: 0 = create, 1 = edit selected.
public sealed class EntitiesModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMenu(Menus.Entity, "E_ntity", order: 3);
        services
            .AddCommand<CreateEmptyCommand>()
            .AddCommand<CreateSpriteCommand>()
            .AddCommand<CreateCameraCommand>()
            .AddCommand<CreateTextCommand>()
            .AddCommand<RenameEntityCommand>()
            .AddCommand<DuplicateEntityCommand>()
            .AddCommand<DeleteEntityCommand>();
    }
}

/// <summary>Creates an entity as a child of the selection (or at the root) and selects it.</summary>
public abstract class CreateEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    protected abstract string EntityName { get; }

    protected virtual void Configure(Entity entity)
    {
    }

    protected override bool CanExecuteCore() => workspace.Scene is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document)
            return Task.CompletedTask;

        var entity = new Entity(EntityName);
        Configure(entity);
        document.Execute(new AddEntityEdit(document.Scene, entity, selection.Entity));
        selection.Select(entity);
        return Task.CompletedTask;
    }
}

public sealed class CreateEmptyCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateEmpty";
    public override string Text => "Create _Empty";
    public override string? Icon => Icons.Add;
    public override KeyGesture? Gesture { get; } = new(Key.N, ModifierKeys.Control | ModifierKeys.Alt);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 0);
    protected override string EntityName => "Entity";
}

public sealed class CreateSpriteCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateSprite";
    public override string Text => "Create _Sprite";
    public override string? Icon => Icons.Image;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 1);
    protected override string EntityName => "Sprite";
    protected override void Configure(Entity entity) => entity.AddComponent<Sprite>();
}

public sealed class CreateCameraCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateCamera";
    public override string Text => "Create _Camera";
    public override string? Icon => Icons.Camera;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 2);
    protected override string EntityName => "Camera";
    protected override void Configure(Entity entity) => entity.AddComponent<Camera>();
}

public sealed class CreateTextCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateText";
    public override string Text => "Create _Text";
    public override string? Icon => Icons.Font;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 3);
    protected override string EntityName => "Text";
    protected override void Configure(Entity entity) => entity.AddComponent<Griff.Scenes.Text>();
}

public sealed class RenameEntityCommand(ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Rename";
    public override string Text => "_Rename";
    public override KeyGesture? Gesture { get; } = new(Key.F2);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        selection.RequestRename();
        return Task.CompletedTask;
    }
}

/// <summary>Copies the selected entity (and its children) right after the original, and selects the copy.</summary>
public sealed class DuplicateEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Duplicate";
    public override string Text => "_Duplicate";
    public override string? Icon => Icons.Copy;
    public override KeyGesture? Gesture { get; } = new(Key.D, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } original)
            return Task.CompletedTask;

        var copy = workspace.Serializer.Clone(original);
        var index = document.Scene.IndexOf(original) + 1;
        document.Execute(new AddEntityEdit(document.Scene, copy, original.Parent, index));
        selection.Select(copy);
        return Task.CompletedTask;
    }
}

public sealed class DeleteEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Delete";
    public override string Text => "De_lete";
    public override string? Icon => Icons.Delete;
    public override KeyGesture? Gesture { get; } = new(Key.Delete);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is { } document && selection.Entity is { } entity)
            document.Execute(new RemoveEntityEdit(document.Scene, entity));
        return Task.CompletedTask;
    }
}
