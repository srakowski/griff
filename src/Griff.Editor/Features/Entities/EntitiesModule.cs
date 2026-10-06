using System.IO;
using System.Windows.Input;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
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
            .AddCommand<CreateTilemapCommand>()
            .AddCommand<CreateParticlesCommand>()
            .AddCommand<CreateButtonCommand>()
            .AddCommand<RenameEntityCommand>()
            .AddCommand<DuplicateEntityCommand>()
            .AddCommand<DeleteEntityCommand>()
            .AddCommand<CopyEntityCommand>()
            .AddCommand<CutEntityCommand>()
            .AddCommand<PasteEntityCommand>()
            .AddCommand<SaveAsPrefabCommand>();
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

/// <summary>Creates an empty tilemap; selecting it puts the Scene view into tile painting.</summary>
public sealed class CreateTilemapCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateTilemap";
    public override string Text => "Create Tile_map";
    public override string? Icon => Icons.Palette;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 4);
    protected override string EntityName => "Tilemap";
    protected override void Configure(Entity entity) => entity.AddComponent<Tilemap>().Tint = Griff.Color.FromBytes(0x48, 0x4B, 0x55);
}

public sealed class CreateParticlesCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateParticles";
    public override string Text => "Create Pa_rticles";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 5);
    protected override string EntityName => "Particles";
    protected override void Configure(Entity entity) => entity.AddComponent<ParticleEmitter>();
}

/// <summary>Creates a clickable button: a background sprite, a label and a Button set to do nothing until configured.</summary>
public sealed class CreateButtonCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateButton";
    public override string Text => "Create _Button";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 6);
    protected override string EntityName => "Button";

    protected override void Configure(Entity entity)
    {
        var size = new System.Numerics.Vector2(3, 1);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Griff.Color.FromBytes(0x3A, 0x3F, 0x4B);
        sprite.Order = 100;
        var label = entity.AddComponent<Griff.Scenes.Text>();
        label.Value = "Button";
        label.Size = 0.5f;
        entity.AddComponent<Griff.Scenes.Button>().Size = size;
    }
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

/// <summary>Entities on the clipboard are scene JSON, so they paste across scenes and even projects.</summary>
public static class EntityClipboard
{
    public const string Format = "Griff.Entity";

    public static void Put(string json)
    {
        var data = new System.Windows.DataObject();
        data.SetData(Format, json);
        data.SetText(json);
        try
        {
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard busy; the copy is lost but nothing else is affected.
        }
    }

    // Another app can hold the clipboard open; treat that as "nothing to paste" rather than failing.
    public static string? Get()
    {
        try
        {
            return System.Windows.Clipboard.ContainsData(Format) ? System.Windows.Clipboard.GetData(Format) as string : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }
}

public sealed class CopyEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Copy";
    public override string Text => "_Copy";
    public override string? Icon => Icons.Copy;
    public override KeyGesture? Gesture { get; } = new(Key.C, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (selection.Entity is { } entity)
            EntityClipboard.Put(workspace.Serializer.SerializeEntity(entity));
        return Task.CompletedTask;
    }
}

public sealed class CutEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Cut";
    public override string Text => "Cu_t";
    public override KeyGesture? Gesture { get; } = new(Key.X, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is { } document && selection.Entity is { } entity)
        {
            EntityClipboard.Put(workspace.Serializer.SerializeEntity(entity));
            document.Execute(new RemoveEntityEdit(document.Scene, entity));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Pastes after the selected entity (as its sibling), or at the end of the scene.</summary>
public sealed class PasteEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Paste";
    public override string Text => "_Paste";
    public override KeyGesture? Gesture { get; } = new(Key.V, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => workspace.Scene is not null && EntityClipboard.Get() is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document || EntityClipboard.Get() is not { } json)
            return Task.CompletedTask;

        var entity = workspace.Serializer.DeserializeEntity(json);
        var anchor = selection.Entity;
        var index = anchor is null ? (int?)null : document.Scene.IndexOf(anchor) + 1;
        document.Execute(new AddEntityEdit(document.Scene, entity, anchor?.Parent, index));
        selection.Select(entity);
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

/// <summary>Prefab files as the editor uses them: saved from a selection, instantiated by drag-and-drop.</summary>
public static class PrefabFiles
{
    /// <summary>A file name made from an entity name: invalid characters become underscores.</summary>
    public static string SafeName(string entityName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(entityName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return name.Length == 0 ? "Prefab" : name;
    }

    /// <summary>A new entity from the prefab at <paramref name="assetPath"/> (relative to Assets), or null if it can't be read.</summary>
    public static Entity? Instantiate(IWorkspace workspace, string assetPath, ICollection<string>? warnings = null)
    {
        if (workspace.Project is not { } project)
            return null;

        var path = Path.Combine(project.AssetsPath, assetPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            return workspace.Serializer.LoadPrefabFromString(File.ReadAllText(path), warnings);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            warnings?.Add(ex.Message);
            return null;
        }
    }
}

/// <summary>
/// Saves the selected entity and its children as Assets/Prefabs/&lt;name&gt;.prefab, ready for
/// <c>Game.Spawn("name")</c> or dragging into a scene. Works in play mode too, to keep a tweaked live entity.
/// </summary>
public sealed class SaveAsPrefabCommand(IWorkspace workspace, ISelection selection, IDialogService dialogs, IStatusService status) : EditorCommand
{
    public override string Id => "Entity.SaveAsPrefab";
    public override string Text => "Save as _Prefab";
    public override string? Icon => Icons.Package;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 3);

    protected override bool CanExecuteCore() => selection.Entity is not null && workspace.Project is not null;

    protected override async Task ExecuteCoreAsync()
    {
        if (selection.Entity is not { } entity || workspace.Project is not { } project)
            return;

        var name = PrefabFiles.SafeName(entity.Name);
        var path = project.DefaultPrefabPath(name);
        if (File.Exists(path) && !dialogs.Confirm("Replace Prefab", $"{name}{Griff.Projects.Project.PrefabExtension} already exists. Replace it with \"{entity.Name}\"?"))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, workspace.Serializer.SavePrefabToString(entity));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Couldn't save the prefab", ex.Message);
            return;
        }

        status.Show($"Saved {project.GetRelativePath(path)}. Spawn it with Game.Spawn(\"{name}\").");
    }
}
