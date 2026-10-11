using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Scenes;

namespace Griff.Editor.Core.Workspace;

/// <summary>
/// The selected entities, shared by the hierarchy, inspector and scene view. <see cref="Entity"/> is the primary one
/// (the last picked), which the scene view's handles work on; the inspector edits all of <see cref="Entities"/> at once,
/// and Delete, Duplicate, Cut, Copy and Paste act on all of them.
/// </summary>
public interface ISelection : INotifyPropertyChanged
{
    /// <summary>The primary selected entity: the last one picked.</summary>
    Entity? Entity { get; }

    /// <summary>Every selected entity, in the order they were picked; the primary one is last.</summary>
    IReadOnlyList<Entity> Entities { get; }

    /// <summary>Selects just this entity, or nothing.</summary>
    void Select(Entity? entity);

    /// <summary>Selects these entities; the last one becomes the primary one.</summary>
    void SelectEntities(IEnumerable<Entity> entities);

    /// <summary>Adds the entity to the selection (as the primary one), or takes it out if it's already selected.</summary>
    void Toggle(Entity entity);

    bool IsSelected(Entity entity);

    /// <summary>Asks the hierarchy to start renaming the selected entity in place.</summary>
    event EventHandler? RenameRequested;

    void RequestRename();
}

public static class SelectionExtensions
{
    /// <summary>
    /// The selected entities in the order the hierarchy shows them, leaving out any inside another selected one (they
    /// go along with it): what Copy, Duplicate and Delete act on.
    /// </summary>
    public static IReadOnlyList<Entity> TopLevel(this ISelection selection)
    {
        var selected = selection.Entities;
        if (selected.Count == 0 || selected[0].Scene is not { } scene)
            return [];
        var set = selected.ToHashSet();
        return scene.Entities
            .Where(e => set.Contains(e) && !set.Any(other => other != e && e.IsSelfOrDescendantOf(other)))
            .ToList();
    }
}

public sealed class Selection : ObservableObject, ISelection
{
    private readonly IWorkspace _workspace;
    private SceneDocument? _document;
    private IReadOnlyList<Entity> _entities = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SceneDocument, IReadOnlyList<Entity>> _kept = new();

    public Selection(IWorkspace workspace)
    {
        _workspace = workspace;
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Scene))
                Track(workspace.Scene);
        };
        Track(workspace.Scene);
    }

    public event EventHandler? RenameRequested;

    public Entity? Entity => _entities.Count > 0 ? _entities[^1] : null;

    public IReadOnlyList<Entity> Entities => _entities;

    public void Select(Entity? entity) => SelectEntities(entity is null ? [] : [entity]);

    public void SelectEntities(IEnumerable<Entity> entities)
    {
        var list = new List<Entity>();
        foreach (var entity in entities)
        {
            if (entity.Scene != _workspace.Scene?.Scene)
                throw new ArgumentException($"{entity.Name} isn't in the open scene.", nameof(entities));
            // Picking one again moves it to the end, making it the primary one.
            list.Remove(entity);
            list.Add(entity);
        }

        Set(list);
    }

    public void Toggle(Entity entity)
    {
        if (_entities.Contains(entity))
            Set(_entities.Where(e => e != entity).ToList());
        else
            SelectEntities(_entities.Append(entity));
    }

    public bool IsSelected(Entity entity) => _entities.Contains(entity);

    public void RequestRename()
    {
        if (Entity is not null)
            RenameRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Set(List<Entity> entities)
    {
        if (entities.SequenceEqual(_entities))
            return;
        var primary = Entity;
        _entities = entities;
        if (Entity != primary)
            OnPropertyChanged(nameof(Entity));
        OnPropertyChanged(nameof(Entities));
    }

    // Going from tab to tab (the scene's and prefabs') keeps each one's selection to come back to; a prefab's tab opens
    // with the prefab selected, so the Inspector shows it straight away. Play mode picks its own (see PlayMode).
    private void Track(SceneDocument? document)
    {
        if (_document is not null)
            _document.Changed -= OnDocumentChanged;
        if (_document is { IsRuntime: false } leaving)
            _kept.AddOrUpdate(leaving, _entities);
        var switching = _document is { IsRuntime: false } && document is { IsRuntime: false };
        _document = document;
        if (_document is not null)
            _document.Changed += OnDocumentChanged;
        if (switching && _kept.TryGetValue(document!, out var kept))
            Set(kept.Where(e => e.Scene == document!.Scene).ToList());
        else
            Set(document is { IsPrefab: true } prefab ? [.. prefab.Scene.Roots.Take(1)] : []);
    }

    // Deleting (or undoing the creation of) selected entities takes them out of the selection.
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (_entities.Any(entity => entity.Scene != _document?.Scene))
            Set(_entities.Where(entity => entity.Scene == _document?.Scene).ToList());
    }
}
