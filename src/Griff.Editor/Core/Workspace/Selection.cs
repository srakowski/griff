using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Scenes;

namespace Griff.Editor.Core.Workspace;

/// <summary>The selected entity, shared by the hierarchy, inspector and scene view.</summary>
public interface ISelection : INotifyPropertyChanged
{
    Entity? Entity { get; }

    void Select(Entity? entity);

    /// <summary>Asks the hierarchy to start renaming the selected entity in place.</summary>
    event EventHandler? RenameRequested;

    void RequestRename();
}

public sealed class Selection : ObservableObject, ISelection
{
    private readonly IWorkspace _workspace;
    private SceneDocument? _document;
    private Entity? _entity;

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

    public Entity? Entity
    {
        get => _entity;
        private set => SetProperty(ref _entity, value);
    }

    public void Select(Entity? entity)
    {
        if (entity is not null && entity.Scene != _workspace.Scene?.Scene)
            throw new ArgumentException("The entity isn't in the open scene.", nameof(entity));
        Entity = entity;
    }

    public void RequestRename()
    {
        if (Entity is not null)
            RenameRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
            _document.Changed -= OnDocumentChanged;
        _document = document;
        if (_document is not null)
            _document.Changed += OnDocumentChanged;
        Entity = null;
    }

    // Deleting (or undoing the creation of) the selected entity clears the selection.
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (Entity is not null && Entity.Scene != _document?.Scene)
            Entity = null;
    }
}
