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

// Entity menu groups: 0 = create (Add Entity..., then a submenu per [Category] of the create commands and presets), 1 = edit
// selected. Each create command's [Category] says where it's listed, as a component's does in the catalog.
public sealed class EntitiesModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMenu(Menus.Entity, "E_ntity", order: 3);
        services
            .AddCommand<AddAttackCommand>()
            .AddCommand<RenameEntityCommand>()
            .AddCommand<DuplicateEntityCommand>()
            .AddCommand<DeleteEntityCommand>()
            .AddCommand<CopyEntityCommand>()
            .AddCommand<CutEntityCommand>()
            .AddCommand<PasteEntityCommand>()
            .AddCommand<SaveAsPrefabCommand>()
            .AddCommand<UnlinkPrefabCommand>()
            .AddCommand<RevertToPrefabCommand>()
            .AddCommand<LockEntityCommand>()
            .AddCommand<HideEntityCommand>()
            .AddCommand<FitPolygonColliderCommand>()
            .AddCommand<AddHingeCommand>()
            .AddCommand<AddRopeCommand>()
            .AddCommand<AddBreakableCommand>();

        // The modules' presets (Create Match Board, Create Tactics Unit) and actions (Generate Dungeon), each a command of its own.
        foreach (var preset in EntityPreset.All)
            services.AddSingleton<EditorCommand>(sp => new PresetCommand(preset, sp.GetRequiredService<IProjectSession>(), sp.GetRequiredService<ISceneDocuments>(), sp.GetRequiredService<ISelection>()));
        foreach (var action in EntityAction.All)
            services.AddSingleton<EditorCommand>(sp => new EntityActionCommand(action, sp.GetRequiredService<IProjectSession>(), sp.GetRequiredService<ISceneDocuments>(), sp.GetRequiredService<ISelection>()));
    }
}

/// <summary>
/// Creates what an <see cref="EntityPreset"/> makes (Griff's own and its modules'), from the Entity menu: a command
/// standing for the preset, with its id, text, shortcut and description (the preset's /// summary). The new entity goes
/// inside the selection and is selected, its children and the preset's changes to the scene in the same undo step.
/// </summary>
public sealed class PresetCommand(EntityPreset preset, IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public EntityPreset Preset => preset;

    public override string Id => preset.Id;

    public override string Text => preset.Text;

    public override string? Icon => Icons.Named(preset.Icon) ?? Icons.Add;

    public override KeyGesture? Gesture { get; } = preset.Shortcut is { } shortcut ? (KeyGesture?)new KeyGestureConverter().ConvertFromInvariantString(shortcut) : null;

    // Its category (a [Category] naming its module) comes from the preset too, as its description does.
    public override Type DescribedBy => preset.GetType();

    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: preset.Order);

    protected override bool CanExecuteCore() => sceneDocuments.Scene is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is not { } document)
            return Task.CompletedTask;

        PutFilesInAssets();
        var scene = document.Scene;
        var entity = new Entity(preset.EntityName(scene));
        preset.Configure(entity, scene);
        var children = preset.CreateChildren(scene).ToList();
        var inside = preset.InsideOf(selection.Entity);
        preset.Arrange(entity, children, inside);
        var edits = preset.EditScene(scene).ToList();
        using (children.Count + edits.Count > 0 ? document.History.BeginGroup($"Add {entity.Name}") : null)
        {
            document.Execute(new AddEntityEdit(scene, entity, document.ParentFor(inside)));
            foreach (var child in children)
                document.Execute(new AddEntityEdit(scene, child, entity));
            foreach (var edit in edits)
                document.Execute(edit);
        }

        selection.Select(entity);
        return Task.CompletedTask;
    }

    // The pictures, sounds and tables it uses go into Assets first, where they aren't already, and what keeps them loaded
    // (the tables) takes them in at once.
    private void PutFilesInAssets()
    {
        if (projectSession.Project?.AssetsPath is not { } assets)
            return;
        foreach (var (path, bytes) in preset.Files())
        {
            var file = Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
            projectSession.AssetChanged(path);
        }
    }
}

/// <summary>
/// Does what a module's <see cref="EntityAction"/> does to the selected entity, from the Entity menu: a command standing for
/// the action, with its id, text, icon and description (the action's /// summary), its edits one undo step.
/// </summary>
public sealed class EntityActionCommand(EntityAction action, IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public EntityAction Action => action;

    public override string Id => action.Id;

    public override string Text => action.Text;

    public override string? Icon => Icons.Named(action.Icon);

    public override Type DescribedBy => action.GetType();

    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: action.Order);

    protected override bool CanExecuteCore() => sceneDocuments.Scene is not null && selection.Entity is { } entity && action.AppliesTo(entity);

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } entity || !action.AppliesTo(entity))
            throw new InvalidOperationException(action.NotApplicable);

        using (document.History.BeginGroup(action.Text.Replace("_", "")))
        {
            foreach (var edit in action.Edits(entity, document.Scene, projectSession.Components))
                document.Execute(edit);
        }

        document.History.Seal();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives the selected entity a sword: a "Hitbox" child to its right (a trigger on the Attack layer with a
/// <see cref="DamageOnTouch"/> that hurts enemies) and an <see cref="Attack"/> that swings it out with the Attack action.
/// One undo step.
/// </summary>
[Category("Characters")]
public sealed class AddAttackCommand(ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.CreateHitbox";
    public override string Text => "Add Atta_ck";
    public override string? Icon => Icons.Bolt;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 4);

    protected override bool CanExecuteCore() => sceneDocuments.Scene is not null && selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } owner)
            return Task.CompletedTask;

        var hitbox = new Entity("Hitbox");
        hitbox.Transform.Position = new System.Numerics.Vector3(0.9f, 0, 0);
        var collider = hitbox.AddComponent<BoxCollider>();
        collider.Size = new System.Numerics.Vector2(1, 0.8f);
        collider.IsTrigger = true;
        collider.Layer = "Attack";
        var damage = hitbox.AddComponent<DamageOnTouch>();
        damage.Layers = "Enemy";
        damage.Knockback = 6;

        using (document.History.BeginGroup($"Add Attack to {owner.Name}"))
        {
            document.Execute(new AddEntityEdit(document.Scene, hitbox, owner));
            if (owner.GetComponent<Attack>() is null)
                document.Execute(new AddComponentEdit(owner, new Attack()));
        }

        selection.Select(hitbox);
        return Task.CompletedTask;
    }
}

/// <summary>Starts renaming the selected entity in the Hierarchy (agents: set its name with entity_update instead).</summary>
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

/// <summary>
/// Copies the selected entities (and their children), each right after its original, and selects the copies. Several
/// selected entities are duplicated as one undo step.
/// </summary>
public sealed class DuplicateEntityCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Duplicate";
    public override string Text => "_Duplicate";
    public override string? Icon => Icons.Copy;
    public override KeyGesture? Gesture { get; } = new(Key.D, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        var originals = selection.TopLevel();
        if (sceneDocuments.Scene is not { } document || originals.Count == 0)
            return Task.CompletedTask;

        var scene = document.Scene;
        var copies = originals.Select(projectSession.Serializer.Clone).ToList();
        // Insert from the bottom up, so each copy's index is still right when it goes in.
        var edits = originals.Select((original, i) => (Original: original, Copy: copies[i], Index: scene.IndexOf(original) + 1))
            .Reverse()
            .Select(x => (IEdit)new AddEntityEdit(scene, x.Copy, x.Original.Parent, x.Index))
            .ToList();
        document.Execute(EntityEdits.Group(edits, "Duplicate", originals));
        selection.SelectEntities(copies);
        return Task.CompletedTask;
    }
}

/// <summary>Edits to several selected entities at once, as one undo step.</summary>
internal static class EntityEdits
{
    /// <summary>One entity's edit as it is; several as one step named like "Delete 3 entities".</summary>
    public static IEdit Group(IReadOnlyList<IEdit> edits, string verb, IReadOnlyList<Entity> entities) =>
        edits.Count == 1 ? edits[0] : new CompositeEdit($"{verb} {entities.Count} entities", edits);

    /// <summary>Removes entities (in scene order) from the bottom up, so each one's place is still right on undo.</summary>
    public static IEdit Remove(Scene scene, IReadOnlyList<Entity> entities, string verb) =>
        Group(entities.Reverse().Select(e => (IEdit)new RemoveEntityEdit(scene, e)).ToList(), verb, entities);

    /// <summary>Locks or unlocks the entities that aren't that way already, as one undo step.</summary>
    public static void SetLocked(SceneDocument document, IEnumerable<Entity> entities, bool locked)
    {
        if (entities.Where(e => e.IsLocked != locked).ToList() is { Count: > 0 } changing)
            document.Execute(new SetEntitiesLockedEdit(changing, locked));
    }

    /// <summary>Hides or shows the entities that aren't that way already, as one undo step.</summary>
    public static void SetHidden(SceneDocument document, IEnumerable<Entity> entities, bool hidden)
    {
        if (entities.Where(e => e.IsHidden != hidden).ToList() is { Count: > 0 } changing)
            document.Execute(new SetEntitiesHiddenEdit(changing, hidden));
    }
}

/// <summary>Entities on the clipboard are scene JSON, so they paste across scenes and even projects.</summary>
public static class EntityClipboard
{
    /// <summary>The clipboard format entities are copied in (they're put there as text too).</summary>
    public const string Format = "Griff.Entity";
}

/// <summary>Copies the selected entities (and their children) to the clipboard.</summary>
public sealed class CopyEntityCommand(IProjectSession projectSession, ISelection selection, IClipboard clipboard) : EditorCommand
{
    public override string Id => "Entity.Copy";
    public override string Text => "_Copy";
    public override string? Icon => Icons.Copy;
    public override KeyGesture? Gesture { get; } = new(Key.C, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (selection.TopLevel() is { Count: > 0 } entities)
            clipboard.Set(EntityClipboard.Format, projectSession.Serializer.SerializeEntities(entities));
        return Task.CompletedTask;
    }
}

/// <summary>Copies the selected entities (and their children) to the clipboard and deletes them, as one undo step.</summary>
[Destructive]
public sealed class CutEntityCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection, IClipboard clipboard) : EditorCommand
{
    public override string Id => "Entity.Cut";
    public override string Text => "Cu_t";
    public override KeyGesture? Gesture { get; } = new(Key.X, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is { } document && selection.TopLevel() is { Count: > 0 } entities)
        {
            clipboard.Set(EntityClipboard.Format, projectSession.Serializer.SerializeEntities(entities));
            document.Execute(EntityEdits.Remove(document.Scene, entities, "Cut"));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Pastes what's on the clipboard after the selected entity (as its siblings), or at the end of the scene, and selects
/// it. Several entities paste in their order, as one undo step.
/// </summary>
public sealed class PasteEntityCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection, IClipboard clipboard) : EditorCommand
{
    public override string Id => "Entity.Paste";
    public override string Text => "_Paste";
    public override KeyGesture? Gesture { get; } = new(Key.V, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => sceneDocuments.Scene is not null && clipboard.Get(EntityClipboard.Format) is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is not { } document || clipboard.Get(EntityClipboard.Format) is not { } json)
            return Task.CompletedTask;

        var entities = projectSession.Serializer.DeserializeEntities(json);
        var anchor = selection.Entity;
        var parent = anchor?.Parent;
        var index = anchor is null ? (int?)null : document.Scene.IndexOf(anchor) + 1;

        // In a prefab's tab, what would go at the top goes inside the prefab, at the end.
        if (parent is null && document.ParentFor(null) is { } inside)
            (parent, index) = (inside, null);
        var edits = entities.Select((e, i) => (IEdit)new AddEntityEdit(document.Scene, e, parent, index + i)).ToList();
        document.Execute(EntityEdits.Group(edits, "Paste", entities));
        selection.SelectEntities(entities);
        return Task.CompletedTask;
    }
}

/// <summary>Deletes the selected entities and everything inside them, as one undo step.</summary>
[Destructive]
public sealed class DeleteEntityCommand(ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Delete";
    public override string Text => "De_lete";
    public override string? Icon => Icons.Delete;
    public override KeyGesture? Gesture { get; } = new(Key.Delete);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is { } document && selection.TopLevel() is { Count: > 0 } entities)
            document.Execute(EntityEdits.Remove(document.Scene, entities, "Delete"));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Locks the selected entities in the Scene view, so clicks and box selections pass through them (and what's inside
/// them) to what's behind: a backdrop or a big tilemap stops catching every drag. Unlocks them if they're all locked
/// already. The Hierarchy still selects locked entities, and the game ignores locking. One undo step.
/// </summary>
public sealed class LockEntityCommand(ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Lock";
    public override string Text => "Loc_k";
    public override string? Icon => Icons.Lock;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 7);

    public override bool? IsChecked => selection.Entities.Count > 0 && selection.Entities.All(e => e.IsLocked);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is { } document && selection.Entities.Count > 0)
            EntityEdits.SetLocked(document, selection.Entities, IsChecked != true);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hides the selected entities in the Scene view, so a pause menu, a dialogue box or a tilemap layer stops covering
/// the level while you work on it: they (and what's inside them) aren't drawn, clicked or box selected there. Shows
/// them again if they're all hidden already. The Hierarchy still selects hidden entities, and the game shows them as
/// usual. One undo step.
/// </summary>
public sealed class HideEntityCommand(ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Hide";
    public override string Text => "_Hide";
    public override string? Icon => Icons.Hide;

    // H is handled by the Scene view and the Hierarchy, so typing an H elsewhere doesn't hide anything.
    public override string? GestureText => "H";
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 8);

    public override bool? IsChecked => selection.Entities.Count > 0 && selection.Entities.All(e => e.IsHidden);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is { } document && selection.Entities.Count > 0)
            EntityEdits.SetHidden(document, selection.Entities, IsChecked != true);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Saves the selected entity and its children as a prefab, ready for <c>Game.Spawn("name")</c> or dragging into a
/// scene: a placed copy over its own prefab (applying everything it changed), anything else as
/// Assets/Prefabs/&lt;name&gt;.prefab. The entity becomes a copy of the prefab, and every other copy of it, in the open
/// scene (one undo step) and in every other scene, is brought up to date: it takes the prefab's components, scale and
/// children, keeping its own name and place and whatever it changed from the prefab. Works in play mode too, to keep a
/// tweaked live entity.
/// </summary>
public sealed class SaveAsPrefabCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection, IDialogService dialogs, IStatusService status, PrefabFiles prefabFiles) : EditorCommand
{
    public override string Id => "Entity.SaveAsPrefab";
    public override string Text => "Save as _Prefab";
    public override string? Icon => Icons.Package;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 3);

    protected override bool CanExecuteCore() => selection.Entity is not null && projectSession.Project is not null;

    protected override async Task ExecuteCoreAsync()
    {
        if (selection.Entity is not { } entity || projectSession.Project is not { } project)
            return;

        // In a prefab's tab, the entity at the top is the prefab: saving it is saving the tab.
        if (sceneDocuments.Scene is { IsPrefab: true } tab && tab.Scene.Roots is [var root, ..] && root == entity)
        {
            await sceneDocuments.SaveSceneAsync();
            return;
        }

        // A copy saves over its own prefab (wherever it is, whatever the copy is called).
        var linked = entity.Prefab is { } link ? project.FindPrefab(link) : null;
        var path = linked ?? project.DefaultPrefabPath(PrefabFiles.SafeName(entity.Name));
        var name = Path.GetFileNameWithoutExtension(path);
        if (File.Exists(path))
        {
            var replace = linked is not null
                ? dialogs.Confirm("Save Prefab", $"Every copy of the {name} prefab, in every scene, will be updated to match, keeping what each copy changed itself. Save \"{entity.Name}\" over it?")
                : dialogs.Confirm("Replace Prefab", $"{name}{Griff.Projects.Project.PrefabExtension} already exists, and every copy of it, in every scene, will be updated to match, keeping what each copy changed itself. Replace it with \"{entity.Name}\"?");
            if (!replace)
                return;
        }

        var json = projectSession.Serializer.SavePrefabToString(entity);
        var oldJson = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Couldn't save the prefab", ex.Message);
            return;
        }

        var update = prefabFiles.UpdateCopies(PrefabFiles.AssetPathOf(project, path), json, oldJson, entity);
        if (update.Problems.Count > 0)
            dialogs.ShowError("Some copies weren't updated", string.Join("\n", update.Problems));
        status.Show($"Saved {project.GetRelativePath(path)}. {(update.Copies > 0 ? update.Summary : $"Spawn it with Game.Spawn(\"{name}\").")}");
    }
}

/// <summary>
/// Unlinks the selected copies from their prefab: they become ordinary entities, which saving the prefab no longer
/// changes. One undo step.
/// </summary>
public sealed class UnlinkPrefabCommand(ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.UnlinkPrefab";
    public override string Text => "_Unlink from Prefab";
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 5);

    protected override bool CanExecuteCore() => selection.Entities.Any(e => e.Prefab is not null);

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is { } document && selection.Entities.Where(e => e.Prefab is not null).ToList() is { Count: > 0 } copies)
            document.Execute(new SetEntitiesPrefabEdit(copies, null));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reverts the selected copies to their prefab: every field they changed goes back to the prefab's value, and
/// components and children they added or took away are put back as the prefab has them. Each keeps its own name and
/// place. One undo step.
/// </summary>
public sealed class RevertToPrefabCommand(ISceneDocuments sceneDocuments, ISelection selection, IStatusService status, PrefabFiles prefabFiles) : EditorCommand
{
    public override string Id => "Entity.RevertToPrefab";
    public override string Text => "_Revert to Prefab";
    public override string? Icon => Icons.Undo;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 6);

    protected override bool CanExecuteCore() => selection.Entities.Any(e => e.Prefab is not null);

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is not { } document)
            return Task.CompletedTask;
        if (prefabFiles.Revert(document.Scene, selection.Entities.Where(e => e.Prefab is not null).ToList()) is { } edit)
            document.Execute(edit);
        else
            status.Show("Nothing to revert: the selection is just like its prefab.");
        return Task.CompletedTask;
    }
}
