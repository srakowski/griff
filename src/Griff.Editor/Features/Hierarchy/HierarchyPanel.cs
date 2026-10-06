using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editing;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Scenes;

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
    private bool _isEditing;
    private string _editName = "";
    private DropPosition _dropHint;

    public Entity Entity { get; } = entity;

    public string Name => Entity.Name;

    public bool IsEnabled => Entity.IsEnabled;

    public ObservableCollection<EntityNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                owner.OnExpandedChanged(this);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value) && value)
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
}

/// <summary>The open scene's entities as a tree. Structural changes rebuild it; expansion and selection survive.</summary>
public sealed class HierarchyPanel : ToolPanel
{
    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly HashSet<Guid> _expanded = [];
    private readonly Dictionary<Guid, EntityNode> _nodes = [];
    private SceneDocument? _document;
    private bool _syncingSelection;

    public HierarchyPanel(IWorkspace workspace, ISelection selection)
    {
        _workspace = workspace;
        _selection = selection;
        workspace.PropertyChanged += OnWorkspaceChanged;
        selection.PropertyChanged += (_, _) => SyncSelectionToTree();
        selection.RenameRequested += (_, _) => BeginRename();
        Track(workspace.Scene);
    }

    public override string Id => "Hierarchy";
    public override string Title => "Hierarchy";
    public override string? Icon => Icons.Hierarchy;
    public override PanelZone Zone => PanelZone.Left;

    public ObservableCollection<EntityNode> Roots { get; } = [];

    public string? SceneName => _document?.Name;

    public bool HasScene => _document is not null;

    public void Select(EntityNode? node) => _selection.Select(node?.Entity);

    public void CommitRename(EntityNode node)
    {
        if (!node.IsEditing)
            return;
        node.IsEditing = false;

        var name = node.EditName.Trim();
        if (_document is not null && name.Length > 0 && name != node.Entity.Name)
            _document.Execute(new RenameEntityEdit(node.Entity, name));
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

    internal void OnNodeSelected(EntityNode node)
    {
        if (!_syncingSelection)
            _selection.Select(node.Entity);
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
        if (e.PropertyName == nameof(IWorkspace.Scene))
            Track(_workspace.Scene);
    }

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
            _document.Changed -= OnDocumentChanged;
        _document = document;
        if (_document is not null)
            _document.Changed += OnDocumentChanged;

        _expanded.Clear();
        Rebuild();
        OnPropertyChanged(nameof(SceneName));
        OnPropertyChanged(nameof(HasScene));
    }

    private void OnDocumentChanged(object? sender, EditEventArgs e)
    {
        // Property edits (moving, recoloring, ...) never change what the tree shows.
        if (!IsPropertyOnly(e.Edit))
            Rebuild();
    }

    private static bool IsPropertyOnly(IEdit edit) => edit switch
    {
        SetPropertyEdit => true,
        CompositeEdit composite => composite.Edits.All(IsPropertyOnly),
        _ => false,
    };

    private void Rebuild()
    {
        _nodes.Clear();
        Roots.Clear();
        if (_document is null)
            return;

        foreach (var root in _document.Scene.Roots)
            Roots.Add(CreateNode(root));
        SyncSelectionToTree();
    }

    private EntityNode CreateNode(Entity entity)
    {
        var node = new EntityNode(entity, this);
        _nodes[entity.Id] = node;
        foreach (var child in entity.Children)
            node.Children.Add(CreateNode(child));
        node.IsExpanded = _expanded.Contains(entity.Id);
        return node;
    }

    private void SyncSelectionToTree()
    {
        _syncingSelection = true;
        try
        {
            var selected = _selection.Entity;
            foreach (var node in _nodes.Values)
                node.IsSelected = node.Entity == selected;

            // Reveal the selection.
            for (var parent = selected?.Parent; parent is not null; parent = parent.Parent)
            {
                if (_nodes.TryGetValue(parent.Id, out var node))
                    node.IsExpanded = true;
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
