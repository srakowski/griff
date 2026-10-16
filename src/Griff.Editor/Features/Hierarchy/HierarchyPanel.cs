using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Entities;
using Griff.Editor.Themes;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Hierarchy;

public enum DropPosition
{
    None,
    Before,
    Into,
    After,
}

/// <summary>One row in the hierarchy tree.</summary>
public sealed class EntityNode(Entity entity, HierarchyPanel owner) : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isPrimary;
    private bool _isEditing;
    private string _editName = "";
    private DropPosition _dropHint;

    public Entity Entity { get; } = entity;

    public string Name => Entity.Name;

    public bool IsEnabled => Entity.IsEnabled;

    /// <summary>Whether the entity is a placed copy of a prefab (marked in its row).</summary>
    public bool IsPrefabCopy => Entity.Prefab is not null;

    /// <summary>"Copy of the Coin prefab (Prefabs/Coin.prefab)": the mark's tooltip.</summary>
    public string? PrefabTip => Entity.Prefab is { } prefab ? $"Copy of the {PrefabCopies.NameOf(prefab)} prefab ({prefab}): saving the prefab updates it" : null;

    /// <summary>
    /// Whether the entity is locked in the Scene view. Setting it locks or unlocks this row, or every selected row if
    /// this one is selected, as one undo step.
    /// </summary>
    public bool IsLocked
    {
        get => Entity.IsLocked;
        set
        {
            if (value != Entity.IsLocked)
                owner.SetLocked(this, value);
        }
    }

    /// <summary>Whether a parent's lock covers the entity (so the Scene view passes over it too).</summary>
    public bool IsLockedByParent => !Entity.IsLocked && Entity.IsLockedInHierarchy;

    /// <summary>
    /// Whether the entity is hidden in the Scene view. Setting it hides or shows this row, or every selected row if this
    /// one is selected, as one undo step.
    /// </summary>
    public bool IsHidden
    {
        get => Entity.IsHidden;
        set
        {
            if (value != Entity.IsHidden)
                owner.SetHidden(this, value);
        }
    }

    /// <summary>Whether a parent's hiding covers the entity (so the Scene view doesn't draw it either).</summary>
    public bool IsHiddenByParent => !Entity.IsHidden && Entity.IsHiddenInHierarchy;

    public EntityNodes Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                owner.OnExpandedChanged(this);
        }
    }

    /// <summary>Whether the entity is one of the selected ones (highlighted).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Whether the entity is the primary selection. Bound to the tree's own single selection, so keyboard navigation
    /// (which moves it) selects just the row it lands on.
    /// </summary>
    public bool IsPrimary
    {
        get => _isPrimary;
        set
        {
            if (SetProperty(ref _isPrimary, value) && value)
                owner.OnNodeSelected(this);
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public DropPosition DropHint
    {
        get => _dropHint;
        set => SetProperty(ref _dropHint, value);
    }

    // The entity may have changed (renamed, enabled, locked...): the row reads it again.
    internal void Refresh() => OnPropertyChanged(string.Empty);

    private bool _isFilteredOut, _isFilterContext;

    /// <summary>True while the Hierarchy's filter hides the row: neither it nor anything inside it has what's typed in its name.</summary>
    public bool IsFilteredOut
    {
        get => _isFilteredOut;
        internal set => SetProperty(ref _isFilteredOut, value);
    }

    /// <summary>
    /// True while the filter shows the row only because something inside it matches: it's drawn faint, to show where
    /// the match is.
    /// </summary>
    public bool IsFilterContext
    {
        get => _isFilterContext;
        internal set => SetProperty(ref _isFilterContext, value);
    }
}

/// <summary>
/// Rows of the hierarchy tree, which can be replaced all at once: the tree hears of it once rather than once a row, so a
/// scene of thousands of entities fills it at once.
/// </summary>
public sealed class EntityNodes : ObservableCollection<EntityNode>
{
    internal void ReplaceAll(List<EntityNode> nodes)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var node in nodes)
            Items.Add(node);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>
/// The open scene's entities as a tree. Structural changes bring it up to date, keeping the rows of entities still there
/// (so expansion and selection survive), and while the game plays it catches up ten times a second. A filter at the
/// top narrows it to the entities whose names have what's typed, with what they're inside.
/// </summary>
public sealed class HierarchyPanel : ToolPanel
{
    private readonly IProjectSession _projectSession;
    private readonly ISceneDocuments _sceneDocuments;
    private readonly ISelection _selection;
    private readonly HashSet<Guid> _expanded = [];
    private Dictionary<Guid, EntityNode> _nodes = [];
    // The rows before an update, for it to take the ones it can keep from.
    private Dictionary<Guid, EntityNode> _before = [];
    private SceneDocument? _document;
    private bool _syncingSelection;
    private long _builtVersion;
    private Entity? _anchor;
    private string _filter = "";
    // What was open before a filter opened the rows its matches are in; put back when it's emptied.
    private HashSet<Guid>? _expandedBeforeFilter;

    // The commands, asked for when the palette opens: commands that open panels are made after the panels are.
    private readonly Lazy<CommandRegistry> _commands;
    private EntityPaletteViewModel? _palette;

    public HierarchyPanel(ISceneDocuments sceneDocuments, IProjectSession projectSession, ISelection selection, Lazy<CommandRegistry> commands)
    {
        _projectSession = projectSession;
        _sceneDocuments = sceneDocuments;
        _selection = selection;
        _commands = commands;
        sceneDocuments.PropertyChanged += OnWorkspaceChanged;
        selection.PropertyChanged += (_, _) => SyncSelectionToTree();
        selection.RenameRequested += (_, _) => BeginRename();
        Track(sceneDocuments.Scene);
    }

    public override string Id => "Hierarchy";
    public override string Title => "Hierarchy";
    public override string? Icon => Icons.Hierarchy;
    public override PanelZone Zone => PanelZone.Left;

    public EntityNodes Roots { get; } = [];

    /// <summary>
    /// The Add Entity palette while it's open (null when it's shut): everything the Entity menu makes, by category, with
    /// what was made lately at the top, filtered by what's typed in its Search, its Selected one's PlanText saying what it
    /// makes. Open it with <see cref="IsAddOpen"/> (or Entity &gt; Add Entity...) and make the selected one with
    /// <see cref="AddPicked"/>. Agents can call the Entity_Create tools directly.
    /// </summary>
    public EntityPaletteViewModel? Palette
    {
        get => _palette;
        private set => SetProperty(ref _palette, value);
    }

    /// <summary>Whether the Add Entity palette is open. Opening it starts it afresh, with nothing typed.</summary>
    public bool IsAddOpen
    {
        get => Palette is not null;
        set
        {
            if (value == IsAddOpen)
                return;
            Palette = value && HasScene ? new EntityPaletteViewModel(_commands.Value) : null;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Makes the thing picked in the Add Entity palette (inside the selected entity, or at the top level) and closes the
    /// palette; one that can't be made now (an attack with nothing selected) leaves it open.
    /// </summary>
    public void AddPicked()
    {
        if (Palette?.Selected is not { CanCreate: true } picked)
            return;
        IsAddOpen = false;
        // Run as a menu item runs it, so what it throws is reported the same way.
        ((System.Windows.Input.ICommand)picked.Command).Execute(null);
    }

    public string? SceneName => _document?.Name;

    public bool HasScene => _document is not null;

    /// <summary>
    /// What's typed in the filter at the top: only entities whose names have it show, with the entities they're inside
    /// (faint, and opened to show them). Emptying it puts the tree back as it was. It stays as the scene changes.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            value ??= "";
            if (!SetProperty(ref _filter, value))
                return;
            var filtering = value.Trim().Length > 0;
            if (filtering && _expandedBeforeFilter is null)
                _expandedBeforeFilter = [.. _expanded];
            else if (!filtering && _expandedBeforeFilter is { } before)
            {
                _expandedBeforeFilter = null;
                foreach (var node in _nodes.Values)
                    node.IsExpanded = before.Contains(node.Entity.Id);
                _expanded.Clear();
                _expanded.UnionWith(before);
            }

            ApplyFilter();
            // Keep the selection in sight.
            SyncSelectionToTree();
        }
    }

    /// <summary>True while the filter has something typed in it.</summary>
    public bool IsFiltering => Filter.Trim().Length > 0;

    /// <summary>The entities whose names match the filter, top to bottom (all of them when it's empty).</summary>
    public IReadOnlyList<Entity> FilterMatches => [.. _nodes.Values.Where(n => !n.IsFilteredOut && !n.IsFilterContext).Select(n => n.Entity)];

    /// <summary>"No entities match "sl"" when the filter hides everything; otherwise null.</summary>
    public string? FilterNote => IsFiltering && Roots.All(r => r.IsFilteredOut) ? $"No entities match \"{Filter.Trim()}\"" : null;

    public bool HasFilterNote => FilterNote is not null;

    // Shows the rows whose names have the filter's text and the rows they're inside (opened), and hides the rest.
    private void ApplyFilter()
    {
        var text = Filter.Trim();
        bool Apply(EntityNode node)
        {
            var inside = false;
            foreach (var child in node.Children)
                inside |= Apply(child);
            var match = text.Length == 0 || node.Entity.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
            node.IsFilteredOut = !match && !inside;
            node.IsFilterContext = !match && inside;
            if (text.Length > 0 && inside)
                node.IsExpanded = true;
            return match || inside;
        }

        foreach (var root in Roots)
            Apply(root);
        OnPropertyChanged(nameof(IsFiltering));
        OnPropertyChanged(nameof(FilterMatches));
        OnPropertyChanged(nameof(FilterNote));
        OnPropertyChanged(nameof(HasFilterNote));
    }

    public void Select(EntityNode? node)
    {
        _anchor = node?.Entity;
        _selection.Select(node?.Entity);
    }

    /// <summary>
    /// A click on a row: a plain click selects just it, Ctrl adds or removes it, Shift selects every row from the last
    /// clicked one to it as the tree shows them (Ctrl+Shift adds that range to the selection).
    /// </summary>
    public void Click(EntityNode node, bool control, bool shift)
    {
        var entity = node.Entity;
        // The range starts at the last row clicked, or at the primary selection if that was picked somewhere else.
        var anchor = _anchor is { } a && _selection.IsSelected(a) ? a : _selection.Entity;
        var visible = VisibleEntities().ToList();
        if (shift && anchor is not null && visible.IndexOf(anchor) is var from and >= 0 && visible.IndexOf(entity) is var to and >= 0)
        {
            var range = visible[Math.Min(from, to)..(Math.Max(from, to) + 1)];
            // The clicked row ends up last, the primary one.
            if (from > to)
                range.Reverse();
            _anchor = anchor;
            _selection.SelectEntities(control ? _selection.Entities.Concat(range) : range);
            return;
        }

        _anchor = entity;
        if (control)
            _selection.Toggle(entity);
        else
            _selection.Select(entity);
    }

    /// <summary>Locks or unlocks a row's entity in the Scene view; a selected row takes the rest of the selection with it.</summary>
    public void SetLocked(EntityNode node, bool locked)
    {
        if (_document is not null)
            EntityEdits.SetLocked(_document, node.IsSelected ? _selection.Entities : [node.Entity], locked);
    }

    /// <summary>Hides or shows a row's entity in the Scene view; a selected row takes the rest of the selection with it.</summary>
    public void SetHidden(EntityNode node, bool hidden)
    {
        if (_document is not null)
            EntityEdits.SetHidden(_document, node.IsSelected ? _selection.Entities : [node.Entity], hidden);
    }

    public void CommitRename(EntityNode node)
    {
        if (!node.IsEditing)
            return;
        node.IsEditing = false;

        var name = node.EditName.Trim();
        if (_document is not null && name.Length > 0 && name != node.Entity.Name)
            _document.Execute(EntityReferences.Rename(node.Entity, name, _projectSession.Components));
    }

    public void CancelRename(EntityNode node) => node.IsEditing = false;

    public bool CanDrop(EntityNode dragged, EntityNode? target, DropPosition position)
    {
        if (target is null)
            return true;
        if (target.Entity.IsSelfOrDescendantOf(dragged.Entity))
            return false;
        return position != DropPosition.None;
    }

    /// <summary>Moves <paramref name="dragged"/> relative to <paramref name="target"/>, or to the end of the roots when target is null.</summary>
    public void Drop(EntityNode dragged, EntityNode? target, DropPosition position)
    {
        if (_document is not { } document || !CanDrop(dragged, target, position))
            return;

        var scene = document.Scene;
        var entity = dragged.Entity;
        Entity? parent;
        int? index;

        if (target is null)
        {
            (parent, index) = (null, null);
        }
        else if (position == DropPosition.Into)
        {
            (parent, index) = (target.Entity, null);
            _expanded.Add(target.Entity.Id);
        }
        else
        {
            parent = target.Entity.Parent;
            var targetIndex = scene.IndexOf(target.Entity) + (position == DropPosition.After ? 1 : 0);

            // Removing the entity first shifts later siblings up by one.
            if (entity.Parent == parent && scene.IndexOf(entity) < targetIndex)
                targetIndex--;
            index = targetIndex;
        }

        if (entity.Parent == parent && index is { } i && scene.IndexOf(entity) == i)
            return;

        document.Execute(new MoveEntityEdit(scene, entity, parent, index));
    }

    // The tree's own selection moved here (arrow keys, or a click the view didn't take): select just this row.
    internal void OnNodeSelected(EntityNode node)
    {
        if (!_syncingSelection && _selection.Entity != node.Entity)
            Select(node);
    }

    /// <summary>The entities whose rows can be seen (every ancestor expanded, and not filtered out), top to bottom.</summary>
    private IEnumerable<Entity> VisibleEntities()
    {
        return Roots.SelectMany(Visible);

        static IEnumerable<Entity> Visible(EntityNode node) =>
            node.IsFilteredOut ? []
            : node.IsExpanded ? node.Children.SelectMany(Visible).Prepend(node.Entity)
            : [node.Entity];
    }

    internal void OnExpandedChanged(EntityNode node)
    {
        if (node.IsExpanded)
            _expanded.Add(node.Entity.Id);
        else
            _expanded.Remove(node.Entity.Id);
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISceneDocuments.Scene))
            Track(_sceneDocuments.Scene);
    }

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
            _document.RuntimeRefreshed -= OnRuntimeRefreshed;
        }

        // Entering or leaving play mode swaps documents but keeps the same entity ids; keep expansion.
        var keepExpansion = _document is not null && document is not null && (_document.IsRuntime || document.IsRuntime);
        _document = document;
        if (_document is not null)
        {
            _document.Changed += OnDocumentChanged;
            _document.RuntimeRefreshed += OnRuntimeRefreshed;
        }

        if (!keepExpansion)
        {
            _expanded.Clear();
            _expandedBeforeFilter?.Clear();
        }

        Rebuild();
        OnPropertyChanged(nameof(SceneName));
        OnPropertyChanged(nameof(HasScene));
        // What the palette would make goes in the scene it was opened on.
        IsAddOpen = false;
    }

    private void OnDocumentChanged(object? sender, EditEventArgs e)
    {
        // Property edits (moving, recoloring, ...) never change what the tree shows.
        if (!IsPropertyOnly(e.Edit))
            Rebuild();
    }

    // The running game can spawn and destroy entities; only rebuild when the structure actually changed.
    private void OnRuntimeRefreshed(object? sender, EventArgs e)
    {
        if (_document is not null && _document.Scene.Version != _builtVersion)
            Rebuild();
    }

    private static bool IsPropertyOnly(IEdit edit) => edit switch
    {
        SetPropertyEdit or SetPropertiesEdit or SetPropertyGroupEdit => true,
        CompositeEdit composite => composite.Edits.All(IsPropertyOnly),
        _ => false,
    };

    // Brings the tree up to date with the scene: the rows of entities still there stay (read again), new ones get rows,
    // and a list of rows changes only where it has to.
    private void Rebuild()
    {
        (_before, _nodes) = (_nodes, _before);
        _nodes.Clear();
        if (_document is null)
        {
            _before.Clear();
            Roots.Clear();
            return;
        }

        _builtVersion = _document.Scene.Version;
        Update(Roots, _document.Scene.Roots);
        _before.Clear();
        ApplyFilter();
        SyncSelectionToTree();
    }

    private EntityNode NodeFor(Entity entity)
    {
        if (_before.Remove(entity.Id, out var node) && node.Entity == entity)
            node.Refresh();
        else
            node = new EntityNode(entity, this);
        _nodes[entity.Id] = node;
        Update(node.Children, entity.Children);
        node.IsExpanded = _expanded.Contains(entity.Id);
        return node;
    }

    private void Update(EntityNodes rows, IReadOnlyList<Entity> entities)
    {
        if (entities.Count == 0)
        {
            if (rows.Count > 0)
                rows.Clear();
            return;
        }

        var nodes = new List<EntityNode>(entities.Count);
        foreach (var entity in entities)
            nodes.Add(NodeFor(entity));

        var same = 0;
        while (same < rows.Count && same < nodes.Count && rows[same] == nodes[same])
            same++;
        if (same == rows.Count && same == nodes.Count)
            return;
        // A few added at the end (spawned, most often) are added; anything else replaces the list.
        if (same == rows.Count && nodes.Count - same <= 32)
        {
            for (var i = same; i < nodes.Count; i++)
                rows.Add(nodes[i]);
            return;
        }

        rows.ReplaceAll(nodes);
    }

    private void SyncSelectionToTree()
    {
        _syncingSelection = true;
        try
        {
            var primary = _selection.Entity;
            foreach (var node in _nodes.Values)
            {
                node.IsSelected = _selection.IsSelected(node.Entity);
                node.IsPrimary = node.Entity == primary;
            }

            // Reveal the selection.
            foreach (var selected in _selection.Entities)
            {
                for (var parent = selected.Parent; parent is not null; parent = parent.Parent)
                {
                    if (_nodes.TryGetValue(parent.Id, out var node))
                        node.IsExpanded = true;
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void BeginRename()
    {
        if (_selection.Entity is { } entity && _nodes.TryGetValue(entity.Id, out var node))
        {
            node.EditName = entity.Name;
            node.IsEditing = true;
        }
    }
}
